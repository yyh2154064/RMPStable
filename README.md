# RMP Stable source repository

This repository stores the editable source code for RMP Stable. Compiled mod
artifacts are deliberately not kept under `RMPStable`.

- `RMPStable/src`: C# source and project file.
- `RMPStable/assets`: Godot source project for the icon and localization.
- `RMPStable/RMPStable.json`: source manifest used by the packager.
- `RMPStable-Packager`: double-clickable Windows release packager.
- `RMPStable-Packager/output`: generated release ZIP files.

On Windows, double-click `RMPStable-Packager/一键打包.cmd`. The packager builds
the DLL and PCK from source and creates a versioned ZIP containing the
`RMPStable` mod directory. If Godot 4.5 is not installed, the first run
downloads and verifies the official portable Godot 4.5.1 build automatically.
