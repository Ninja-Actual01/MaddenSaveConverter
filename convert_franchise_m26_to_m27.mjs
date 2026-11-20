import pkg from 'madden-franchise';
const FranchiseFile = pkg.FranchiseFile;
import fs from 'fs';
import os from 'os';
import path from 'path';
import crypto from 'crypto';

async function convert(srcPath, tplPath, outPath, profile = 'full') {
  console.log(`Loading src ${srcPath}`);
  const src = new FranchiseFile(srcPath);
  await src.parse();
  console.log(`src tables ${src.tables.length}`);

  // Never modify the user's real template: parse and save a temporary copy.
  const tmpTpl = path.join(os.tmpdir(), `msc-franch-tpl-${crypto.randomBytes(6).toString('hex')}`);
  fs.copyFileSync(tplPath, tmpTpl);
  console.log(`Loading tpl ${tplPath} (working copy)`);
  let tpl;
  try {
    tpl = new FranchiseFile(tmpTpl);
    await tpl.parse();
    console.log(`tpl tables ${tpl.tables.length}`);

    // Important: when we write into an "empty" slot, the library must mark the
    // row as used and repair the empty-record linked list. Otherwise the game
    // will most likely crash on load.
    tpl.settings.autoUnempty = true;

    const skipTablePatterns = [
      // system/event/engine/flow tables (cross-version sensitive)
      /Event$/i, /Reaction$/i, /Transaction/i, /Request$/i, /Manager$/i, /Director$/i,
      /Eval$/i, /Arc/i, /Quest/i, /Scenario/i, /Story/i, /Flow/i, /CareerFlow/i,
      /ThemeWeek/i, /Marketplace/i, /Notification/i, /Trophy/i,
      /^Advance/i, /AutoSim/i, /Patch/i, /Version/i, /Schema/i,
      // season/schedule/progression/history/stats are handled by separate tools
      /Season/i, /Schedule/i, /Calendar/i, /Week/i, /Standings/i, /Playoff/i,
      /Progression/i, /Stat/i, /Stats/i, /Career(?!$)/i, /History/i, /RecordBook/i,
      /Award/i, /Milestone/i, /Legacy/i, /Draft(?!Class)/i
    ];

    // Roster/core-only bisect profile. Keeps players, rosters, teams, coaches,
    // staff, depth charts, contracts and identity/visual tables, while leaving
    // seasons, stats, events, progress and UI state at M27 template defaults.
    const corePatterns = [
      /^Player/i, /^Roster/i, /^Team/i, /^Coach/i, /^Staff/i, /^Owner/i,
      /^DepthChart/i, /^Stadium/i, /^Contract/i, /^Personnel/i,
      /^CharacterVisuals/i, /^CharacterGameplay/i, /^College/i,
      /^DraftClass/i, /^Association/i
    ];
    const tightNames = new Set([
      'Player', 'Player[]', 'Team', 'Team[]', 'Coach', 'Coach[]', 'DepthChart',
      'RosterInfo', 'PlayerPersonnel', 'Stadium', 'StadiumOwnerData',
      'Owner', 'CharacterVisuals',
      'PlayerContract', 'PlayerBaseContract', 'PlayerBaseContract[]',
      'ContractOffer', 'ContractOffer[]', 'PlayerContractManager',
      'ContractIncentive',
      'UserRegistry', 'User[]', 'FranchiseUser'
    ]);
    // Incremental "select" profile: copy only explicitly requested table groups
    // into an already-correct M27 base (for example a franchise created from a
    // converted roster). Player/Player[]/DepthChart are never copied here.
    const selectGroups = {
      coaches: [/^Coach/i, /^Staff/i],
      stadiums: [/^Stadium/i],
      teamnames: [/^Team$/i],
      identity: [/^Owner$/i, /^UserRegistry$/i, /^User\[\]$/i, /^FranchiseUser$/i],
      personnel: [/^PlayerPersonnel$/i, /^RosterInfo$/i],
      contracts: [/^Contract/i, /^PlayerContract/i, /^PlayerBaseContract/i],
      draftclass: [/^DraftClass/i]
    };
    const selectTokens = profile.startsWith('select:')
      ? profile.slice(7).split(',').map(s => s.trim()).filter(Boolean)
      : [];
    const forbiddenSelectNames = new Set(['Player', 'Player[]', 'DepthChart']);
    function copyAllowed(name) {
      if (profile.startsWith('select:')) {
        if (forbiddenSelectNames.has(name)) return false;
        for (const tok of selectTokens) {
          if (selectGroups[tok]?.some(rx => rx.test(name))) return true;
          if (tok === name) return true;
        }
        return false;
      }
      if (profile === 'tight') return tightNames.has(name);
      if (profile === 'core') return corePatterns.some(rx => rx.test(name));
      return !skipTablePatterns.some(rx => rx.test(name));
    }

    // tableId is different between M26 and M27. uniqueId is stable.
    function uniqueKey(table) {
      const h = table.header || {};
      if (h.uniqueId && h.uniqueId !== 0) {
        return `u:${h.uniqueId}|${h.name || ''}|${h.data1Id || ''}`;
      }
      return null;
    }
    function fallbackKey(table) {
      const h = table.header || {};
      return `n:${h.name || ''}|${h.data1Id || ''}|${h.numMembers || 0}|${h.record1Size || 0}`;
    }
    function nameKey(table) {
      const h = table.header || {};
      return `n:${h.name || ''}|${h.data1Id || ''}`;
    }

    const byUnique = new Map();
    const byFallback = new Map();
    const byName = new Map();
    function add(map, key, table) {
      if (!key) return;
      if (!map.has(key)) map.set(key, []);
      map.get(key).push(table);
    }
    for (const t of tpl.tables) {
      if (!t.header?.name || t.header.name.startsWith('\0')) continue;
      add(byUnique, uniqueKey(t), t);
      add(byFallback, fallbackKey(t), t);
      add(byName, nameKey(t), t);
    }

    function chooseBest(candidates, srcTable) {
      if (!candidates || candidates.length === 0) return null;
      if (candidates.length === 1) return candidates[0];
      const sh = srcTable.header || {};
      let best = null;
      let bestScore = Infinity;
      for (const c of candidates) {
        const ch = c.header || {};
        const score =
          Math.abs((ch.recordCapacity || 0) - (sh.recordCapacity || 0)) * 4 +
          Math.abs((ch.numMembers || 0) - (sh.numMembers || 0)) * 2 +
          Math.abs((ch.record1Size || 0) - (sh.record1Size || 0));
        if (score < bestScore) {
          bestScore = score;
          best = c;
        }
      }
      return best;
    }

    function findTarget(srcTable) {
      const uk = uniqueKey(srcTable);
      if (uk && byUnique.has(uk)) return chooseBest(byUnique.get(uk), srcTable);
      const fk = fallbackKey(srcTable);
      if (byFallback.has(fk)) return chooseBest(byFallback.get(fk), srcTable);
      // Last resort: only accept an unambiguous same-name/same-store match.
      const nk = nameKey(srcTable);
      const candidates = byName.get(nk) || [];
      return candidates.length === 1 ? candidates[0] : null;
    }

    // A franchise reference is 15 bits tableId + 17 bits rowNumber. M26 and
    // M27 assign different tableIds, so every reference must be rewritten.
    const tableIdMap = new Map();
    const tplById = new Map();
    const srcTableIds = new Set();
    for (const t of tpl.tables) {
      if (t.header?.tableId) tplById.set(t.header.tableId, t);
    }
    for (const st of src.tables) {
      if (!st.header?.tableId) continue;
      srcTableIds.add(st.header.tableId);
      const tt = findTarget(st);
      if (tt?.header?.tableId) tableIdMap.set(st.header.tableId, tt.header.tableId);
    }
    // The modded M26 save contains references to Player rows that are empty.
    // Copying those references into M27 makes the game read stale template
    // players from those rows. Emit an empty reference instead.
    const srcPlayerTable = src.tables.find(t => t.header?.uniqueId === 1612938518);
    await srcPlayerTable?.readRecords();
    const srcPlayerEmpty = new Set();
    if (srcPlayerTable) {
      srcPlayerTable.records.forEach((r, i) => {
        if (r && r.isEmpty) srcPlayerEmpty.add(i);
      });
    }
    const srcPlayerTableId = srcPlayerTable?.header?.tableId;

    // Team row numbers differ between M26/M27 (two empty source slots), so map
    // source row -> target row by AssetName for team table references only.
    // Player.TeamIndex uses TEAM_LOGO IDs, which are identical in both versions,
    // so numeric team fields must be copied unchanged.
    const srcTeamTable = src.tables.find(t => t.header?.uniqueId === 637929298);
    const tplTeamTable = tpl.tables.find(t => t.header?.uniqueId === 637929298);
    await srcTeamTable?.readRecords();
    await tplTeamTable?.readRecords();
    function teamKey(rec) {
      return (rec?.AssetName || rec?.DisplayName || rec?.NickName || '').toString().trim();
    }
    const srcTeamRowToTarget = new Map();
    if (srcTeamTable && tplTeamTable) {
      const tplRowByKey = new Map();
      tplTeamTable.records.forEach((r, i) => {
        if (!r || r.isEmpty) return;
        const key = teamKey(r);
        if (key && !tplRowByKey.has(key)) tplRowByKey.set(key, i);
      });
      srcTeamTable.records.forEach((r, i) => {
        if (!r || r.isEmpty) return;
        const target = tplRowByKey.get(teamKey(r));
        if (target !== undefined) srcTeamRowToTarget.set(i, target);
      });
    }
    const srcTeamTableId = srcTeamTable?.header?.tableId;

    const TEAM_FREE_AGENTS = 32;

    function dec2bin(n, bits) {
      return (n >>> 0).toString(2).padStart(bits, '0').slice(-bits);
    }
    function remapReference(value) {
      if (typeof value !== 'string' || !/^[01]{32}$/.test(value)) return null;
      const srcTableId = parseInt(value.slice(0, 15), 2);
      const rowNumber = parseInt(value.slice(15, 32), 2);
      if (srcTableId === 0) return value; // empty reference
      if (srcTableId === srcPlayerTableId && srcPlayerEmpty.has(rowNumber)) {
        return '00000000000000000000000000000000';
      }
      const targetTableId = tableIdMap.get(srcTableId);
      if (targetTableId === undefined) {
        // Some references (e.g. tableId 16384) point into the shared interned
        // string lookup rather than a table in the save. M26 and M27 ship the
        // same lookup, so those row indices are valid as-is.
        return srcTableIds.has(srcTableId) ? null : value;
      }
      let outRow = rowNumber;
      if (srcTableId === srcTeamTableId) {
        const mapped = srcTeamRowToTarget.get(rowNumber);
        if (mapped === undefined) return null;
        outRow = mapped;
      }
      const targetTable = tplById.get(targetTableId);
      if (targetTable && outRow >= (targetTable.header.recordCapacity || Infinity)) return null;
      return dec2bin(targetTableId, 15) + dec2bin(outRow, 17);
    }

    // Mirror the source save's empty-record linked list onto the M27 table.
    // Calling record.empty() one row at a time makes the library append rows to
    // its own chain in write order, which produced an invalid nextRecordToUse
    // and crashed the game. Writing the same chain the source uses avoids that.
    function rebuildEmptyChain(srcTable, tplTable) {
      // Tables without a real uniqueId use a generated generic schema. Touching
      // them makes the library rewrite their records at the wrong length.
      if (!srcTable.header?.uniqueId || !tplTable.header?.uniqueId) return 0;
      const hdr = tplTable.header || {};
      const capacity = hdr.recordCapacity || tplTable.records.length || 0;
      const srcRecords = srcTable.records || [];
      const tplRecords = tplTable.records || [];
      const limit = Math.min(srcRecords.length, tplRecords.length);
      let emptied = 0;

      // Copy the source chain pointers verbatim. The game does not require the
      // chain to be in ascending index order, so recomputing it from scratch
      // produced a different (but still structurally plausible) list.
      for (let i = 0; i < limit; i++) {
        const rec = tplRecords[i];
        if (!rec) continue;
        const sRec = srcRecords[i];
        if (sRec && sRec.isEmpty) {
          const rawNext = sRec._data ? sRec._data.readUInt32BE(0) : capacity;
          const nextPtr = rawNext >= capacity ? capacity : rawNext;
          tplTable._setBufferToEmptyRecordReference(i, nextPtr);
          tplTable._setRecordInternalBuffer(i, nextPtr);
          rec.isEmpty = true;
          rec.clearCachedFieldValues();
          emptied++;
        } else if (rec.isEmpty) {
          // Source row is valid but target row is still marked empty.
          tplTable._changeRecordBuffers(i, 0);
          rec.isEmpty = false;
          rec.clearCachedFieldValues();
        }
      }

      // Target rows past the source table length are never used: mark them empty.
      for (let i = limit; i < tplRecords.length; i++) {
        const rec = tplRecords[i];
        if (rec && !rec.isEmpty) {
          tplTable._setBufferToEmptyRecordReference(i, capacity);
          tplTable._setRecordInternalBuffer(i, capacity);
          rec.isEmpty = true;
          rec.clearCachedFieldValues();
          emptied++;
        }
      }

      const rawHeaderNext = srcTable.header && typeof srcTable.header.nextRecordToUse === 'number'
        ? srcTable.header.nextRecordToUse
        : capacity;
      const nextToUse = rawHeaderNext >= capacity ? capacity : rawHeaderNext;
      tplTable.setNextRecordToUse(nextToUse, false);

      // Rebuild the in-memory map by following the exact chain we wrote.
      const emptyMap = new Map();
      const seen = new Set();
      let cur = nextToUse;
      let previous = null;
      while (cur !== capacity && cur < tplRecords.length && !seen.has(cur)) {
        const rec = tplRecords[cur];
        if (!rec || !rec.isEmpty) break;
        const nextPtr = rec._data.readUInt32BE(0);
        emptyMap.set(cur, { previous, next: nextPtr });
        if (previous !== null && emptyMap.has(previous)) emptyMap.get(previous).next = cur;
        seen.add(cur);
        previous = cur;
        cur = nextPtr;
      }
      tplTable.emptyRecords = emptyMap;
      tplTable.isChanged = true;
      return emptied;
    }

    let common = 0, copied = 0, copiedRefs = 0, cleared = 0, neutralized = 0, reemptied = 0, skipped = 0, skippedTables = 0;
    let skippedRefs = 0, skippedType = 0, skippedWidth = 0, modifiedTables = 0;

    for (const srcTable of src.tables) {
      const name = srcTable.header?.name;
      if (!name || name.startsWith('\0')) continue;
      if (!copyAllowed(name)) { skippedTables++; continue; }
      // Only the roster arrays were safe to clear in earlier testing.
      const clearEmptySlots = profile === 'tight' && name === 'Player[]';

      const tplTable = findTarget(srcTable);
      if (!tplTable) continue;

      try { await srcTable.readRecords(); } catch (e) { skipped++; continue; }
      try { await tplTable.readRecords(); } catch (e) { skipped++; continue; }
      if (srcTable.records.length === 0 || tplTable.records.length === 0) continue;
      common++;

      const n = Math.min(srcTable.records.length, tplTable.records.length);
      let modified = false;
      const useTeamMap = name === 'Team' && srcTeamRowToTarget.size > 0;

      for (let i = 0; i < srcTable.records.length; i++) {
        const sRec = srcTable.records[i];
        const targetIndex = useTeamMap ? (srcTeamRowToTarget.get(i) ?? i) : i;
        if (targetIndex >= tplTable.records.length) continue;
        const tRec = tplTable.records[targetIndex];
        if (!tRec) continue;
        if (!sRec || sRec.isEmpty) {
          if (clearEmptySlots && tRec && !tRec.isEmpty) {
            try {
              tRec.empty();
              // empty() only rewrites the first 4 bytes (the chain pointer).
              // Zero the rest of the row so template player references do not
              // survive inside an "empty" roster slot.
              const start = tplTable.header.table1StartIndex + i * tplTable.header.record1Size;
              tplTable.data.fill(0, start + 4, start + tplTable.header.record1Size);
              if (tRec._data) tRec._data.fill(0, 4);
              tRec.clearCachedFieldValues();
              cleared++;
              modified = true;
            } catch {}
          }
          continue;
        }

        const sFields = sRec.fields || {};
        const tFields = tRec.fields || {};

        for (const k of Object.keys(sFields)) {
          if (!(k in tFields)) continue;
          try {
            const sField = sRec.getFieldByKey ? sRec.getFieldByKey(k) : sFields[k];
            const tField = tRec.getFieldByKey ? tRec.getFieldByKey(k) : tFields[k];
            if (!sField || !tField) continue;

            const so = sField.offset || {};
            const to = tField.offset || {};

            // Rewrite references instead of dropping them. The row index is
            // preserved; only the table ID changes between M26 and M27.
            if (sField.isReference || so.isReference || tField.isReference || to.isReference) {
              const remapped = remapReference(sRec[k]);
              if (remapped === null) { skippedRefs++; continue; }
              tRec[k] = remapped;
              copiedRefs++;
              modified = true;
              continue;
            }
            if (so.type !== to.type || !!so.enum !== !!to.enum) {
              skippedType++;
              continue;
            }
            if (!!so.isSigned !== !!to.isSigned ||
                !!so.valueInSecondTable !== !!to.valueInSecondTable ||
                !!so.valueInThirdTable !== !!to.valueInThirdTable) {
              skippedType++;
              continue;
            }

            let value = sRec[k];
            // Coach/Staff TeamIndex is a Team row index (unlike Player.TeamIndex,
            // which is a logo ID). In select mode we must translate it through
            // the AssetName-based team row map, or coaches land on wrong teams.
            if (profile.startsWith('select:') && k === 'TeamIndex' &&
                /^(Coach|Staff)/i.test(name) && typeof value === 'number' &&
                srcTeamRowToTarget.has(value)) {
              value = srcTeamRowToTarget.get(value);
            }
            if (typeof value === 'number' && Number.isFinite(value) && to.length) {
              const bits = to.length;
              const signed = !!to.isSigned;
              const min = signed ? -Math.pow(2, bits - 1) : 0;
              const max = signed ? Math.pow(2, bits - 1) - 1 : Math.pow(2, bits) - 1;
              if (value < min || value > max) {
                skippedWidth++;
                continue;
              }
            }

            tRec[k] = value;
            copied++;
            modified = true;
          } catch (e) {
            // Individual field copy failures are non-fatal; keep going.
          }
        }
      }

      // Stale M27 template players that have no source counterpart are still
      // non-empty records. The game can count them toward roster limits, so
      // retire them and blank their identity without changing the empty chain.
      if (profile === 'tight' && name === 'Player') {
        for (let i = 0; i < n; i++) {
          const sRec = srcTable.records[i];
          const tRec = tplTable.records[i];
          if (!sRec || !sRec.isEmpty || !tRec || tRec.isEmpty) continue;
          try {
            tRec.FirstName = '';
            tRec.LastName = '';
            tRec.PLYR_ASSETNAME = '';
            tRec.TeamIndex = TEAM_FREE_AGENTS;
            tRec.PrevTeamIndex = TEAM_FREE_AGENTS;
            tRec.ContractStatus = 'Retired';
            tRec.PLYR_CAREERPHASE = 'Retired';
            neutralized++;
            modified = true;
          } catch {}
        }
      }

      for (let i = n; i < tplTable.records.length; i++) {
        const tRec = tplTable.records[i];
        if (clearEmptySlots && tRec && !tRec.isEmpty) {
          try {
            tRec.empty();
            const start = tplTable.header.table1StartIndex + i * tplTable.header.record1Size;
            tplTable.data.fill(0, start + 4, start + tplTable.header.record1Size);
            if (tRec._data) tRec._data.fill(0, 4);
            tRec.clearCachedFieldValues();
            cleared++;
            modified = true;
          } catch {}
        }
      }

      // Empty-chain edits are disabled: they are not safe on this template.
      // if (APPLY_EMPTY_CHAIN) reemptied += rebuildEmptyChain(srcTable, tplTable);
      if (modified) modifiedTables++;
    }

    // Tables that are not copied keep their M27 template references. Those
    // references point into Player row indices that now hold M26 players, so
    // the game shows phantom duplicates. Clear every such reference.
    const tplPlayerTable = tpl.tables.find(t => t.header?.uniqueId === 1612938518);
    const tplPlayerTableId = tplPlayerTable?.header?.tableId;
    let clearedSkippedRefs = 0, skippedRefTables = 0;
    if (tplPlayerTableId && !profile.startsWith('select:')) {
      const clearRefsIn = new Set(['PlayerAcquisitionEvaluation', 'HistoryEntry']);
      for (const t of tpl.tables) {
        if (!t.header?.name || !clearRefsIn.has(t.header.name)) continue;
        try { await t.readRecords(); } catch { continue; }
        let n = 0;
        for (const r of t.records) {
          if (!r) continue;
          for (const k of Object.keys(r.fields || {})) {
            const v = r[k];
            if (typeof v !== 'string' || !/^[01]{32}$/.test(v)) continue;
            if (parseInt(v.slice(0, 15), 2) !== tplPlayerTableId) continue;
            try { r[k] = '00000000000000000000000000000000'; n++; } catch {}
          }
        }
        if (n) { clearedSkippedRefs += n; skippedRefTables++; }
      }
    }

    console.log(`profile=${profile}, common tables ${common}, fields copied ${copied}, refs remapped ${copiedRefs}, rows cleared ${cleared}, stale players retired ${neutralized}, skipped-refs cleared ${clearedSkippedRefs} in ${skippedRefTables} tables, skipped tables ${skippedTables}`);
    console.log(`match=uniqueId, skippedRefs=${skippedRefs}, skippedType=${skippedType}, skippedWidth=${skippedWidth}, modifiedTables=${modifiedTables}, readSkipped=${skipped}`);
    if (fs.existsSync(outPath)) {
      const stamp = new Date().toISOString().replace(/[:.]/g, '-');
      const backup = `${outPath}.bak-${stamp}`;
      fs.copyFileSync(outPath, backup);
      console.log(`backup=${backup}`);
    }
    await tpl.save(outPath);
    console.log(`saved to ${outPath} size ${fs.statSync(outPath).size}`);
  } finally {
    try { fs.unlinkSync(tmpTpl); } catch {}
  }
}

const args = process.argv.slice(2);
if (args.length < 3) {
  console.log('usage: node convert_franchise_m26_to_m27.mjs <src> <base> <out> [full|core|tight|select:group1,group2]');
  console.log('select groups: coaches, stadiums, teamnames, identity, personnel, contracts, draftclass');
  console.log('select mode never copies Player/Player[]/DepthChart and auto-backs up an existing output file.');
  process.exit(1);
}
await convert(args[0], args[1], args[2], args[3] || 'full');
