# RMP Stable PCK source

This Godot 4.5.x project contains the original RMP Stable icon and the English
and Simplified Chinese localization resources embedded in `RMPStable.pck`.

To rebuild on Windows, install Godot 4.5.x, make `godot.exe` available as
`godot` or `godot4` in `PATH`, then run:

```powershell
.\build_pck.ps1
```

The script writes `RMPStable.pck` to the parent mod directory, replacing the
runtime asset package with a reproducible build from these sources.
