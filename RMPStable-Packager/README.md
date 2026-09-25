# RMP Stable one-click packager

Double-click `一键打包.cmd` to build a distributable v0.3.9 ZIP from the sibling
`RMPStable` source directory. ZIP files are written to `output` beside this
script. The package contains one DLL with embedded implementations for v0.107.1
and v0.111.0 Public Beta.
Run the packager once while each game branch is installed to save its DLL
references under the ignored `tools/game-references` directory. The first run
may stop after saving the current branch; run it again after both snapshots exist.

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

The ZIP also contains `update.cmd`. The v0.3.7 updater can install v0.3.9
because the runtime package still uses the same root DLL, PCK, and JSON files.
The DLL selects the matching embedded implementation at startup. Each release
must have a GitHub Release tagged `v<version>` and attach the generated
`RMPStable-v<version>.zip` as an asset. Mark v0.3.9 as the Latest release so the
already-installed v0.3.7 updater discovers it.
