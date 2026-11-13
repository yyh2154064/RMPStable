# RMP Stable one-click packager

Double-click `BuildPackager.cmd` to build a distributable ZIP from the sibling
`RMPStable` source directory. ZIP files are written to `output` beside this
script. The package contains one DLL with embedded implementations for v0.107.1,
v0.111.0, and v0.113.0 Public Beta.
Run the packager once while each game branch is installed to save its DLL
references under the ignored `tools/game-references` directory. The first run
may stop after saving the current branch. Run it again after all three snapshots exist.

The script auto-detects common Steam library locations. Optional command-line
overrides are available when needed:

```powershell
.\Build-Mod.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Slay the Spire 2" -GodotPath "D:\Tools\Godot_v4.5.1-stable_win64_console.exe"
```

You may instead set `STS2_GAME_DIR` and `GODOT_EXE`. A .NET 7+ SDK is required.
If Godot 4.5.x is not found, the first run downloads the official portable
Godot 4.5.1 release into the ignored `tools` directory and verifies its SHA-256
hash. Build intermediates are isolated under `.work` and removed after every
run; only the ZIP remains.

The ZIP also contains `update.cmd`. The updater replaces the DLL, PCK, and JSON
files. The DLL selects the matching embedded implementation at startup. Each release
must have a GitHub Release tagged `v<version>` and attach the generated
`RMPStable-v<version>.zip` as an asset. Mark the formal release as Latest so
installed copies can discover it.
