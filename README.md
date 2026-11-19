# Madden Save Converter

Windows tools for Madden/football save workflows, including bidirectional roster conversion between **Madden 26** and **Madden 27** and **Franchise Studio** (franchise save editing) targeting supported Madden/CFB titles.

## Download / Run

- **Users:** Download the latest release from GitHub and run the self-contained `MaddenSaveConverter.exe` (no separate .NET runtime install required for release builds).
- **Platform:** Windows x64.

## Usage

### GUI
Launch `MaddenSaveConverter.exe` and follow the prompts to select source/destination roster files.

### CLI
```text
MaddenSaveConverter.exe --convert <source> <destination>
```

### Node.js scripts (development)
Some conversion utilities are available as scripts in this repo (dependencies may be required):

```powershell
npm install
node convert_m26_to_m27.js <m26 roster> <output path>
node convert_m27_to_m26.js <m27 roster> <m26 template> <output path>
```

## Troubleshooting

- **Roster crashes in-game:** After loading the converted roster, open the in-game roster editor, go to **Depth Charts**, and **Auto-Reorder** depth charts for all teams.
- **Backups:** Always keep copies of original roster/franchise files before converting or editing.

## Notes

- Game-readable roster files must use the expected naming convention (e.g. `ROSTER-NAME` with a single hyphen).
- Supported roster files use the expected FBCHUNKS format/size.

## Development

See [DEV_NOTES.md](DEV_NOTES.md) for build instructions, project structure, and release guidance.

### Build from source

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```
