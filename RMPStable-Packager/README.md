# RMP Stable one-click packager

Double-click `一键打包.cmd` to build a distributable ZIP from the sibling
`RMPStable` source directory. ZIP files are written to `output` beside this
script.

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

The ZIP also contains `update.cmd`. End users can double-click it to fetch the
latest published GitHub Release and safely replace `RMPStable.dll`,
`RMPStable.pck`, and `RMPStable.json`. Each release must upload the generated
`RMPStable-v<version>.zip` as a GitHub Release asset.
