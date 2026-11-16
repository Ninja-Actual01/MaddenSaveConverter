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
