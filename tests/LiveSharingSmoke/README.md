# LiveSharingSmoke

Developer-only in-game integration harness. **Do not install in the normal game
directory or run it against your real profile.** The installer ZIP does not
contain this harness.

It creates a disposable, unsaved Ironclad run, opens a generated merchant, shows
three card candidates, and enters ExoskeletonsWeak combat. It also remaps the
spectator key in the isolated profile. Card reward candidates here intentionally
come from the starting deck: the test exercises presentation and lifecycle, not
reward generation or choosing a reward. It exercises damaged health bars, long
multi-hit intent text, native sort comparers, inspection boundaries, keyword
placement, upgrade/base previews, and the always-on-top window flag. It draws and
erases map strokes, shows loot, chooses Neow's Leafy Poultice, then Tezcatara's
Biiig Hug and completes the real removal selection/confirmation flow. These
choices affect only the disposable test run. Map timing reports capture, JSON,
render-update cost and the current engine FPS limit separately.

The initializer requires both `RMP_SMOKE_OUTPUT` and the exact command-line pair
`--force-steam off`. Set `APPDATA` and `LOCALAPPDATA` to a fresh directory inside
your test workspace. Use a separate runtime copy with modding enabled in its
isolated `settings.save`. Disable FTUEs only in that profile. For headless tests,
the copy's crash reporter may be omitted to avoid the crash reporter itself
opening an error dialog; do not alter the installed game's files.

Build the main mod normally, then build this project with `GameManagedDir`
pointing at the matching game's managed assemblies. For v0.111.0 also supply
`-p:GameCompatibility=beta`. Copy its DLL and manifest into the test runtime's
`mods/LiveSharingSmoke`, beside the normal `mods/RMPStable` package. The same
harness supports testing the final dual-payload bootstrap DLL.

Example from PowerShell, after preparing the isolated runtime:

```powershell
$testRoot = Join-Path (Get-Location) '.tools/live-sharing'
$env:APPDATA = Join-Path $testRoot 'profile/Roaming'
$env:LOCALAPPDATA = Join-Path $testRoot 'profile/Local'
$env:RMP_SMOKE_OUTPUT = Join-Path $testRoot 'results'
New-Item -ItemType Directory -Force $env:RMP_SMOKE_OUTPUT | Out-Null
& (Join-Path $testRoot 'runtime/SlayTheSpire2.exe') --headless `
  --main-pack 'F:/SteamLibrary/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck' `
  --force-steam off --quit-after 4000
```

Adjust the main-pack path to the installed game of the same version. Remove
`--headless` and add `--rendering-method gl_compatibility --windowed` for image
comparison. The renderer's screenshots are test outputs only. Optional
`RMP_SMOKE_HOLD=1` keeps the final event window open for manual inspection.

Success is `[LiveSharingSmoke] ALL PASSED`; assertion failures write
`failure.txt` and request exit code 1. Outputs include page JSON and, when
rendering is enabled, spectator/original PNGs. Keep a fresh output folder or
check timestamps so old screenshots or failure files cannot be mistaken for
the latest run. Engine shutdown resource warnings are distinct from harness
assertions; inspect the complete log for runtime errors as well.
