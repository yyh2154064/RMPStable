# RMP Stable

Standalone 16-player multiplayer mod for Slay the Spire 2 v0.107.1. It expands
multiplayer capacity and prevents duplicate remote treasure rewards from
splitting reward IDs between peers.

Version 0.3.0 added native-checkpoint Quick SL. Version 0.3.1 fixed its
input-settings registration and changed the updater to avoid GitHub's anonymous
API rate limit. Version 0.3.2 mounts the shared singleplayer/multiplayer
confirmation popup before initializing its native controls. Version 0.3.3 hides
the uninitialized popup frame, reloads singleplayer checkpoints without creating
the main menu, and conceals multiplayer's required internal menu transition until
the load lobby is ready. Quick SL is available from the
pause menu and through a rebindable F5 shortcut. Multiplayer requests require
host approval; clients reconnect and ready automatically, while the host is
only readied last after every originally connected client is ready. Any
eight-second client timeout cancels automatic start and leaves the final
decision to the host's normal lobby confirmation.

## Source layout

`RMPStable` is now the editable source directory, not an installable mod
directory:

- `src/` contains the complete C# project and sources for `RMPStable.dll`.
- `assets/` contains the Godot project used to create `RMPStable.pck`.
- `RMPStable.json` is the release manifest copied into the mod package.

Compiled DLL and PCK files are intentionally absent from this directory. Run
`../RMPStable-Packager/一键打包.cmd` on Windows to create
`../RMPStable-Packager/output/RMPStable-v<version>.zip`.

The packager requires:

- Slay the Spire 2 v0.107.1 installed through Steam (or `-GameDir` supplied).
- A .NET 7 or newer SDK available as `dotnet`.
- Internet access on the first run if Godot 4.5.x is not already available.
  The packager downloads the official portable Godot 4.5.1 build and verifies
  its SHA-256 hash. `GODOT_EXE` and `-GodotPath` remain available as overrides.

The ZIP contains a top-level `RMPStable` directory with the runtime files and
`update.cmd`. Extract that directory into `Slay the Spire 2/mods` and enable
the mod. End users can later double-click `update.cmd` to install the latest
published GitHub Release without downloading the ZIP manually. All lobby
members should use the same version.
