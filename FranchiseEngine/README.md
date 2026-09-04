# Franchise Engine (Node)

A Node service that wraps [`madden-franchise`](https://github.com/bep713/madden-franchise) (bep) **v4.3.6** to read and edit Madden **and** College Football 27 (CFB27) franchise/dynasty saves.

> **Architecture note:** This is the "fast path" to a working editor. The C# WPF GUI (MaddenSaveConverter) invokes this service over JSON-lines on stdin/stdout. A later milestone ports this to pure C# behind the same interface.

## Interface

The service speaks **one JSON request per line, one JSON response per line** over stdin/stdout.

Request:
```json
{"id": 1, "cmd": "<command>", "args": {...}}
```
Response:
```json
{"id": 1, "ok": true, "data": {...}}
```
or `{"id": 1, "ok": false, "error": "..."}`.

The C# GUI should spawn `node FranchiseEngine/engine.mjs` once, keep stdin/stdout open, and send requests on demand. It can also be run ad-hoc: `node engine.mjs <cmd> --key=value ...`.

## Commands

### `ping`
Returns version info. No file access.

### `introspect`
Opens (a copy of) a save and reports game year, game type (`madden`/`college`), schema, and a table summary. Never touches the caller's original.
- `src`: source save path
- `workPath`: (copy) path to open
- `backupPath`: backup of the original (created + verified byte-hash)
- `copy`: set `false` to open `src` in place (only if caller guarantees it's already a copy)

### `readTable`
Reads records from a table (optionally selected fields) for display. Returns `fieldKeys`, `fieldSchema` (name/type/maxLength/isReference) and `rows`.
- `tableName`, `fields` (array), `tableIndex` (optional; default = the table with most records), `src`, `workPath`, `backupPath`

### `editTable`
Mutates matching records then saves to `workPath` (a copy), and re-opens the result to confirm it reloads.
- `edits`: array of `{ "match": {"field":"AssetName","value":"Bengals"}, "field":"DisplayName", "value":"..." }`.
  Omit `match` to edit every non-empty record.
- Respects schema `maxLength` for strings (e.g. `NickName` max 18 chars).

### `saveRoundTrip`
Proves a save re-packs + reloads cleanly. Used for verification.

## Proven capabilities (tested on real saves)

- **Open copy + byte-verified backup** — originals never modified.
- **Read tables** reliably (e.g. `Team` 32 NFL teams by `DisplayName`/`NickName`/`AssetName`/`TeamIndex`).
- **Team rename** — edit `Team.DisplayName` / `NickName` matched by `AssetName`, persists and reloads.
- **Season year** — edit `SeasonInfo.CurrentSeasonYear` (e.g. 2026 → 1985), persists and reloads.
- **Round-trip** — M27 resave is byte-identical; M26 resave differs internally but reloads cleanly.

## References

- Engine library: <https://github.com/bep713/madden-franchise>
- GUI editor front-end (feature-parity / UI reference): <https://github.com/bep713/madden-franchise-editor>

## Important library caveat

`madden-franchise` v4.3.6 lazily attaches table schemas and can return **0 records** for a table until the schema cache is warmed by reading another table first. The engine calls `warmSchemas()` after `parse()` to make reads deterministic. Keep this when porting to C#.

## Read-after-edit semantics

`readTable`/`editTable` re-copy `src` over `workPath` (with a verified backup) on each call by default. After an edit has saved changes to `workPath`, later reads must be sent with `src` equal to the `workPath` (the edited copy) so changes are not clobbered by a fresh copy from the original. The C# client targets the work copy after the first open.

## Dependencies

Only pure-JS deps are required (bundled in `node_modules/`):
- `madden-franchise@4.3.6`
- `bit-buffer`, `fast-xml-parser`, `node-xml-stream-parser`

**No native modules needed.** Decompression uses Node's built-in `zlib` (`gunzipSync`, `zstdCompressSync`/`zstdDecompressSync`). Do **not** add `lz4` or `@toondepauw/node-zstd`.