# RMP Stable source repository

This repository stores the editable source code for RMP Stable. Compiled mod
artifacts are deliberately not kept under `RMPStable`.

This branch implements the singleplayer local spectator prototype (F8, off by
default). See [安装、使用与实现边界](docs/live-sharing-test.md). It is an experimental
build; multiplayer spectator transport is not included.

- `RMPStable/src`: C# source and project file.
- `RMPStable-Bootstrap`: version-detecting loader that embeds both compiled implementations.
- `RMPStable/doc`: Godot source project for the icon and localization.
- `RMPStable/RMPStable.json`: source manifest used by the packager.
- `RMPStable-Packager`: double-clickable Windows release packager.
- `RMPStable-Packager/output`: generated release ZIP files.

On Windows, double-click `RMPStable-Packager/一键打包.cmd`. The packager builds
the version-aware DLL and PCK from source and creates a versioned ZIP
containing the `RMPStable` mod directory. The packager needs local game assembly
snapshots for both v0.107.1 and v0.111.0 (saved under its ignored `tools`
directory when each version is active). If Godot 4.5 is not installed, the first run
downloads and verifies the official portable Godot 4.5.1 build automatically.
