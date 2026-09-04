// FranchiseEngine — Node service wrapping madden-franchise (bep) v4.3.6.
// Speaks JSON-lines on stdin/stdout: one JSON request per line, one JSON response per line.
//
// Safety rules enforced here:
//   * The caller's original file is NEVER the work file. We always open/copy via caller paths.
//   * Copy-with-backup: we copy src -> workPath, verify byte hash, and back up the ORIGINAL src.
//   * All mutating commands save to the workPath (a copy), leaving the original untouched.

import pkg from 'madden-franchise';
import fs from 'fs';
import readline from 'readline';
import { createRequire } from 'module';

const require = createRequire(import.meta.url);
const FranchiseFile = pkg.FranchiseFile;

function respond(id, ok, data, error) {
  const msg = { id, ok, data };
  if (error) msg.error = String(error && error.message ? error.message : error);
  process.stdout.write(JSON.stringify(msg) + '\n');
}

function hashFile(p) {
  return require('crypto').createHash('sha256').update(fs.readFileSync(p)).digest('hex');
}

// madden-franchise (v4.3.6) lazily attaches schemas and can return 0 records for a
// table until the schema cache is warmed by touching another table. Reading the
// first table first reliably warms it. Call after parse() before reading any table.
async function warmSchemas(franchise) {
  if (franchise.tables && franchise.tables.length) {
    try { await franchise.tables[0].readRecords(); } catch (e) { /* ignore */ }
  }
}

function ensureWorkCopy(src, workPath, backupPath) {
  if (!fs.existsSync(src)) throw new Error('Source not found: ' + src);
  fs.mkdirSync(require('path').dirname(workPath), { recursive: true });
  if (src !== workPath) {
    if (fs.existsSync(backupPath)) fs.rmSync(backupPath, { force: true });
    fs.copyFileSync(src, workPath);
    if (hashFile(src) !== hashFile(workPath)) throw new Error('Copy verification failed (hash mismatch)');
    fs.copyFileSync(src, backupPath);
  }
  return { workPath, backupPath, bytes: fs.statSync(workPath).size };
}

// Resolve a table by name; when a name matches multiple tables, prefer the one
// with the most records (that is usually the "real" data store), or one at tableIndex.
function resolveTable(franchise, name, tableIndex) {
  const matches = franchise.getAllTablesByName(name);
  if (!matches.length) throw new Error('Table not found: ' + name);
  if (typeof tableIndex === 'number') return matches[tableIndex] || matches[matches.length - 1];
  return matches.reduce((best, t) => {
    const n = (t.header && t.header.data1RecordCount) || 0;
    const b = (best && best.header && best.header.data1RecordCount) || 0;
    return n > b ? t : best;
  }, matches[0]) || matches[0];
}

function tableMeta(franchise) {
  const byName = new Map();
  for (const t of franchise.tables) {
    const name = t && t.header && typeof t.header.name === 'string' ? t.header.name : null;
    const n = (t.header && t.header.data1RecordCount) || 0;
    if (name) byName.set(name, (byName.get(name) || 0) + (n > 0 ? n : 0));
  }
  return {
    count: franchise.tables.length,
    byName: [...byName.entries()].map(([name, records]) => ({ name, records })).sort((a, b) => b.records - a.records),
  };
}

// Read records of a table, returning the selected fields for non-empty rows.
async function readTableRows(table, fields) {
  await table.readRecords(fields && fields.length ? fields : undefined);
  const rows = [];
  for (const rec of table.records) {
    if (rec.isEmpty) continue;
    const row = { index: rec.index };
    if (rec.fields) row._fieldKeys = Object.keys(rec.fields);
    if (fields && fields.length) {
      for (const f of fields) {
        try { row[f] = rec[f]; } catch (e) { row[f] = null; }
      }
    }
    rows.push(row);
  }
  return rows;
}

const handlers = {

  async ping() {
    return { pong: true, node: process.version, madden_franchise: pkg.version || '?' };
  },

  // Open (a copy of) a franchise save and report metadata + table summary.
  async introspect(args) {
    const src = args.src;
    const workPath = args.workPath || src;
    const backupPath = args.backupPath || (workPath + '.orig.bak');
    if (args.copy !== false && src !== workPath) ensureWorkCopy(src, workPath, backupPath);
    const franchise = new FranchiseFile(workPath);
    await franchise.parse();
    return {
      filePath: workPath,
      gameYear: franchise.gameYear,
      gameType: franchise.gameType,
      expectedSchema: franchise.expectedSchemaVersion,
      files: { bytes: fs.statSync(workPath).size, backupPath },
      tables: tableMeta(franchise),
    };
  },

  // Read records from a table (optionally only the listed fields).
  async readTable(args) {
    const { src, workPath, tableName, fields, tableIndex } = args;
    const backupPath = args.backupPath || (workPath + '.orig.bak');
    const cp = args.copy !== false && src !== workPath ? ensureWorkCopy : (() => ({ workPath, backupPath }));
    cp(src, workPath, backupPath);
    const franchise = new FranchiseFile(workPath);
    await franchise.parse();
    await warmSchemas(franchise);
    const table = resolveTable(franchise, tableName, tableIndex);
    const rows = await readTableRows(table, fields);
    // Expose field metadata (name/type/maxLength) so the GUI can constrain input.
    const fieldSchema = (table.offsetTable || [])
      .map((f) => ({ name: f.name, type: f.type, maxLength: f.maxLength == null ? null : f.maxLength, isReference: !!f.isReference }))
      .sort((a, b) => a.name.localeCompare(b.name));
    let fieldKeys = [];
    if (rows[0]) fieldKeys = rows[0]._fieldKeys || [];
    return { table: tableName, count: rows.length, fieldKeys, fieldSchema, rows };
  },

  // Edit fields on matching records, then save to workPath. Match by {field, value}.
  async editTable(args) {
    const { src, workPath, tableName, edits, tableIndex } = args;
    const backupPath = args.backupPath || (workPath + '.orig.bak');
    ensureWorkCopy(src, workPath, backupPath);
    if (!tableName) throw new Error('tableName required');
    const franchise = new FranchiseFile(workPath);
    await franchise.parse();
    await warmSchemas(franchise);
    const table = resolveTable(franchise, tableName, tableIndex);
    await table.readRecords();
    let changed = 0;
    const applied = [];
    for (const edit of edits || []) {
      let targets = table.records;
      if (edit.match && edit.match.field !== undefined) {
        targets = table.records.filter((r) => !r.isEmpty && String(r[edit.match.field]) === String(edit.match.value));
      }
      for (const rec of targets) {
        if (rec.isEmpty) continue;
        const before = rec[edit.field];
        rec[edit.field] = edit.value;
        applied.push({ index: rec.index, field: edit.field, before, after: rec[edit.field] });
        changed++;
      }
    }
    await franchise.save(workPath);
    const reload = new FranchiseFile(workPath);
    await reload.parse();
    return { table: tableName, edited: changed, applied: applied.slice(0, 200), reloadedOk: true };
  },

  // Prove the save round-trips: open copy, save to outPath, re-open the result.
  async saveRoundTrip(args) {
    const { src, workPath, outPath } = args;
    if (!fs.existsSync(src)) throw new Error('Source not found: ' + src);
    fs.mkdirSync(require('path').dirname(outPath), { recursive: true });
    if (src !== workPath) fs.copyFileSync(src, workPath);
    const franchise = new FranchiseFile(workPath);
    await franchise.parse();
    await franchise.save(outPath || workPath);
    const reload = new FranchiseFile(outPath || workPath);
    await reload.parse();
    return {
      gameYear: reload.gameYear,
      gameType: reload.gameType,
      byteIdentical: hashFile(src) === hashFile(outPath || workPath),
      srcBytes: fs.statSync(src).size,
      outBytes: fs.statSync(outPath || workPath).size,
      reloadedOk: true,
    };
  },

};

// ---------------------------------------------------------------------------
// Main loop (JSON-lines protocol)
// ---------------------------------------------------------------------------

const rl = readline.createInterface({ input: process.stdin, output: process.stdout, terminal: false });

rl.on('line', async (line) => {
  let req;
  try { req = JSON.parse(line); } catch (e) { respond(0, false, null, 'Bad JSON request'); return; }
  const { id, cmd, args } = req;
  const handler = handlers[cmd];
  if (!handler) { respond(id, false, null, 'Unknown command: ' + cmd); return; }
  try {
    const data = await handler(args || {});
    respond(id, true, data);
  } catch (e) {
    respond(id, false, null, e);
  }
});

process.on('SIGINT', () => { process.exit(0); });

// Direct invocation: node engine.mjs <cmd> --key=value ...
if (process.argv.length > 2) {
  const cmd = process.argv[2];
  const args = {};
  for (const a of process.argv.slice(3)) {
    const eq = a.indexOf('=');
    if (eq === -1) continue;
    args[a.slice(2, eq)] = a.slice(eq + 1);
  }
  const h = handlers[cmd];
  if (!h) { process.stdout.write(JSON.stringify({ ok: false, error: 'Unknown command: ' + cmd }) + '\n'); process.exit(1); }
  // Coerce known array args from comma-separated strings; parse JSON for object args.
  if (args.fields && typeof args.fields === 'string') args.fields = args.fields.split(',').map((s) => s.trim()).filter(Boolean);
  if (args.edits && typeof args.edits === 'string') {
    try { args.edits = JSON.parse(args.edits); } catch (e) { args.edits = []; }
  }
  h(args).then((d) => { process.stdout.write(JSON.stringify({ ok: true, data: d }) + '\n'); process.exit(0); })
    .catch((e) => { process.stdout.write(JSON.stringify({ ok: false, error: String(e && e.message || e) }) + '\n'); process.exit(1); });
}