# Madden Save Converter — Dev Notes

This project provides tools for working with Madden/football game save data, including bidirectional roster conversion between **Madden 26** and **Madden 27** and **Franchise Studio** features for franchise save editing (targeting Madden 26/27 and CFB27, as supported by the current codebase).

## For users

- **Platform:** Windows (x64).
- **Run:** Download the latest release build of `MaddenSaveConverter.exe` and run it (self-contained release builds do not require a separate .NET runtime install).
- **GUI:** Launch the app and select source/destination roster files as prompted.
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
```

(Additional franchise conversion scripts may be present in the repo; verify script arguments and required inputs before use.)

### Project structure (high level)
- `Convert/`, `Franchise/`, `FranchiseEngine/`, `Tdb2/` — conversion and franchise/save handling logic
- `Assets/`, `Movies/`, `Music/`, `Sounds/` — bundled UI/media/content
- `MainWindow.xaml(.cs)`, `App.xaml(.cs)` — WPF application shell
- `publish/` — local publish output (release artifacts)

### Release notes
- Validate conversions on copies of saves/rosters.
- Keep backups of original files before conversion or franchise edits.
- When publishing a GitHub release, include the latest self-contained Windows build artifacts and summarize user-facing changes and any compatibility notes.

## Franchise conversion status (M26 -> M27) — experimental

`convert_franchise_m26_to_m27.mjs` is still experimental. It accepts a fourth
profile argument: `full`, `core`, or `tight`.

- `full` / `core`: copy many more tables but crash to desktop immediately after
  selecting the franchise in Madden 27. Not usable.
- `tight`: copies only core player/roster/team/coach/user tables. This loads in
  Madden 27 and depth-chart auto-order works, but roster accuracy is not final.

### Verified working pieces

- Table matching uses stable `uniqueId`, not `tableId` (which differs between
  M26 and M27).
- References are remapped by `uniqueId`; interned-string references are passed
  through unchanged.
- All 3,133 non-empty source `Player` rows are copied correctly:
  0 missing rows, 0 name mismatches, 0 team mismatches.
- `Player[]` rows that are empty in the source are cleared and their remaining
  bytes are zeroed, which removes stale M27 template player references.
- 56 stale M27 `Player` records are retired/blanked so they should not occupy
  roster slots.
- `M27_641_1.gz` must remain present under
  `node_modules/madden-franchise/data/schemas/27/` for M27 conversion.

### Known issues (do not publish as a stable release yet)

1. Duplicate and missing players are still reported in-game even with the
   `tight` profile.
2. Empty-record-chain rewrites were tested and caused crashes; they are
   disabled in the converter. Do not re-enable them without in-game validation.
3. A final reference comparison found 517 fields where the source value was an
   empty reference but the output contains a real reference (examples in
   `Coach`, `CharacterVisuals`, etc.). These are likely stale template values
   from source-empty rows that were not zeroed outside `Player[]`.
4. `Madden Franchise Tool` opens the converted save with a broken/cut-off
   ribbon layout, while other franchise files open normally. The cause is
   unknown; possible incomplete port or UI/header table mismatch.
5. Season, schedule, history, stats, and progression remain M27 template
   defaults by design.

### Deployment / testing

- Working repo: `E:\Game stuff\mods\Repos\Repos\MaddenSaveConverter`
- Deployed app: `E:\Game stuff\mods\madden mod tools\Madden Save Converter`
- Latest validation save: `CAREER-MSCTIGHT12`
- Do not tag/publish a release until a converted franchise loads with correct
  rosters and no duplicated or missing players.
