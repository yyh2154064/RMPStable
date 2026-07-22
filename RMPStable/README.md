# RMP Stable

Standalone 16-player multiplayer mod for Slay the Spire 2 v0.107.1. It expands
multiplayer capacity and prevents duplicate remote treasure rewards from
splitting reward IDs between peers.

## Direct installation / repository layout

The repository root is also the installable mod directory. Clone or copy this
folder directly to `Slay the Spire 2/mods/RMPStable` and enable **RMP Stable**.
Do not enable another multiplayer-capacity mod at the same time.

Required runtime files at repository root:

- `RMPStable.json`
- `RMPStable.dll`
- `RMPStable.pck`

`PCK_SOURCE` is the reproducible Godot 4.5.x source project for the icon and
English/Simplified Chinese localization. It is ignored by the game at runtime.

All players in a lobby should use the same version. Real sessions with more
than four players still require multi-client testing even though the assembly
and resource pack have passed static and isolated loading checks.
