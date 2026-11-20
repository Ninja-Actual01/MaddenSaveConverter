# Madden Save Converter — Dev Notes

This project provides tools for working with Madden/football game save data, including bidirectional roster conversion between **Madden 26** and **Madden 27** and **Franchise Studio** features for franchise save editing (targeting Madden 26/27 and CFB27, as supported by the current codebase).

## For users

- **Platform:** Windows (x64).
- **Run:** Download the latest release build of `MaddenSaveConverter.exe` and run it (self-contained release builds do not require a separate .NET runtime install).
- **GUI:** Launch the app and select source/destination roster files as prompted. The app is branded simply **Madden Save Converter**; no intro video or third-party splash branding is included.
- **CLI:**
  ```text
  MaddenSaveConverter.exe --convert <source> <destination>
  ```
- **Roster naming/format notes:**
  - Game-readable roster files must follow the expected naming convention (e.g. `ROSTER-NAME` with a single hyphen).
  - Roster files use the expected FBCHUNKS format/size for supported conversions.
- **Troubleshooting:** If a converted roster crashes in-game, try loading it in the in-game roster editor and use **Depth Charts → Auto-Reorder** for all teams.

## For contributors / developers

### Prerequisites
- **.NET SDK** compatible with the project target (see `MaddenSaveConverter.csproj`, currently targets `net10.0-windows`).
- **Node.js** (for script-based workflows and/or franchise engine components, as applicable).
- Git.

### Build and publish
From the project root:

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

The `publish/` output contains the distributable executable and required content files.

### Node.js conversion scripts (development)
Some conversion flows are available as Node scripts (may require dependencies such as `madden-file-tools`):

```powershell
npm install
node convert_m26_to_m27.js <m26 roster> <output path>
node convert_m27_to_m26.js <m27 roster> <m26 template> <output path>
node convert_franchise_m26_to_m27.mjs <source-franchise> <m27-template-franchise> <output-franchise> [full|core|tight|select:group1,group2]
```

`select:` mode is for the short-term roster-first workaround. It copies only
explicitly requested non-player table groups from an M26 source into an already
correct M27 base franchise (for example one created from a converted roster).
It never copies `Player`, `Player[]`, or `DepthChart`, and it automatically backs
up an existing output file before writing. Available groups: `coaches`, `staff`,
`stadiums`, `teamnames`, `identity`, `personnel`, `contracts`, `draftclass`.

`select:coaches` does **not** paste whole rows by row number. M26 and M27 order
their Coach tables differently. It matches each team by `AssetName`, follows that
team's `HeadCoach` / `OffensiveCoordinator` / `DefensiveCoordinator` /
`UserCharacter` references in both saves, and pastes only fields whose names
exist in both schemas (identity, asset names, portrait/presentation, team index,
age, experience and career values). Unmatched fields/rows are ignored. Broad
`Coach*` event/eval/reaction tables are intentionally excluded because they are
cross-version sensitive and caused load freezes. The `stadiums` group is likewise
limited to `Stadium`, `StadiumOwnerData`, and `StadiumPartInfo`; stadium
eval/upgrade/renovation event tables are excluded.
Example:

```powershell
node convert_franchise_m26_to_m27.mjs <m26-source> <m27-base> <m27-out> select:coaches,stadiums,teamnames
```

`M27_641_1.gz` must remain present under
`node_modules/madden-franchise/data/schemas/27/` for the franchise conversion to work, both in the repo and in deployed copies.

### Project structure (high level)
- `Convert/`, `Franchise/`, `FranchiseEngine/`, `Tdb2/` — conversion and franchise/save handling logic
- `Assets/`, `Movies/`, `Music/`, `Sounds/` — bundled UI/media/content (branding intro assets removed)
- `MainWindow.xaml(.cs)`, `App.xaml(.cs)` — WPF application shell
- `publish/` — local publish output (release artifacts)

### Release notes
- Validate conversions on copies of saves/rosters.
- Keep backups of original files before conversion or franchise edits.
- When publishing a GitHub release, include the latest self-contained Windows build artifacts and summarize user-facing changes and any compatibility notes.
- Do not tag/publish a release until a converted franchise loads with correct rosters and no duplicated or missing players.

## Franchise conversion status (M26 -> M27) — experimental

`convert_franchise_m26_to_m27.mjs` accepts a fourth profile argument: `full`, `core`, or `tight`.

- `full` / `core`: copy many more tables but crash to desktop immediately after
  selecting the franchise in Madden 27. Not usable.
- `tight`: copies only curated player/roster/team/coach/user/contract tables.
  This loads in Madden 27. Roster accuracy is still being validated.

### Current working state

- Latest candidate: `CAREER-MSCTIGHT26`
- Last known-good fallback: `CAREER-MSCTIGHT12`
- New short-term path: use `select:` mode to layer non-player tables onto an
  M27 base franchise created from a converted roster, one group at a time.
- Deployed converter script is being synced with the repo version.

### Verified working pieces

- Table matching uses stable `uniqueId`, not `tableId` (which differs between
  M26 and M27).
- References are remapped by target `tableId`; interned-string references are
  passed through unchanged.
- Invalid/out-of-range references emit an empty reference.
- `autoUnempty = true`; the global `recalculateEmptyRecordReferences()` call and
  full empty-record-chain rebuild remain disabled (they caused crashes).
- All 3,133 non-empty source `Player` rows are copied correctly:
  0 missing rows, 0 name mismatches, 0 team mismatches.
- `Player[]` rows that are empty in the source are cleared/zeroed. Generalized
  row-zeroing outside `Player[]` crashes and is not enabled.
- `Team` rows are remapped by `AssetName` because M26 has empty source slots
  20/24 that shift later team row numbers. Team table references are rewritten
  accordingly.
- `Player.TeamIndex` / `PrevTeamIndex` use `TEAM_LOGO` IDs, not Team row numbers.
  These values are identical in M26 and M27 and are copied unchanged. The M27
  template Bengals QB (Joe Burrow) has `TeamIndex = 1`, confirming Bengals = 1.
- `Coach.TeamIndex` also uses logo IDs, not Team row numbers. Verified in both
  M26 and M27: Patriots coaches have `TeamIndex = 21`. Do not row-remap it.
- Stale M27 template `Player` rows are neutralized by blanking fields and
  parking them on `TEAM_FREE_AGENTS = 32`; rows themselves are not zeroed.
- Stale references to source-empty `Player` rows are cleared only in
  `PlayerAcquisitionEvaluation` and `HistoryEntry` (11,300 refs total).
  Clearing all skipped-table Player refs crashes.
- 56 stale template players remain retired/blanked.
- Tight profile now also copies `PlayerContract`, `PlayerBaseContract`,
  `PlayerBaseContract[]`, `ContractOffer`, `ContractOffer[]`,
  `PlayerContractManager`, and `ContractIncentive`.

### Known issues (do not publish as a stable release yet)

1. Andy Dalton is present in the converted save with intact `Team.Roster` and
   `DepthChart` chains, but he does not appear anywhere in Madden 27. Next
   suspect is his M26 portrait ID `4356` not resolving in M27.
2. Advancing the week crashes to desktop in TIGHT26. A reference audit found
   that copied `Player[]` tables (notably the DepthChart backing array
   `uid=889352590`) still contain references to the 56 retired template Player
   rows. The next fix to try is clearing only references to retired template
   rows (source row index >= 3133) inside `Player[]` tables.
3. Character visuals/gear are not fully correct. M26/M27 asset names differ and
   many M27 visual-adjacent tables are still skipped by the tight profile.
4. Contracts are still wrong. M27 moved contract data out of `Player` into
   reference tables, and M26-only fields such as `ContractYear`,
   `ContractSalary0..7`, and `ContractBonus0..7` are dropped by the schema.
   Contract tables are now copied, but data is not fully reconstructed.
5. Empty-record-chain rewrites, broad row-zeroing, and broad skipped-ref
   clearing were tested and caused crashes. Do not re-enable them without
   in-game validation.
6. A final reference comparison found 517 fields where the source value was an
   empty reference but the output contains a real reference (examples in
   `Coach`, `CharacterVisuals`, etc.). These are likely stale template values
   from source-empty rows that were not zeroed outside `Player[]`.
7. `Madden Franchise Tool` opens the converted save with a broken/cut-off
   ribbon layout, while other franchise files open normally. The cause is
   unknown; possible incomplete port or UI/header table mismatch.
8. Season, schedule, history, stats, and progression remain M27 template
   defaults by design.

### Deployment / testing

- Working repo: `E:\Game stuff\mods\Repos\Repos\MaddenSaveConverter`
- Deployed app: `E:\Game stuff\mods\madden mod tools\Madden Save Converter`
- Smaller fallback tool: `E:\Game stuff\mods\madden mod tools\Madden Save Converter.zip`
- Current validation save: `CAREER-MSCTIGHT26` (loads, Dalton absent, advancing week crashes)
- Fallback save: `CAREER-MSCTIGHT12`
- Do not tag/publish a release until a converted franchise loads with correct
  rosters and no duplicated or missing players.
