# LiveSharingSmoke

Developer-only in-game integration harness. **Do not install in the normal game
directory or run it against your real profile.** The installer ZIP does not
contain this harness.

It creates a disposable Ironclad run saved only in an isolated test profile, opens a generated merchant, shows
three card candidates, and enters ExoskeletonsWeak combat. It also remaps the
spectator key in the isolated profile. Card reward candidates here intentionally
come from the starting deck: the test exercises presentation and lifecycle, not
reward generation or choosing a reward. It exercises damaged health bars, long
multi-hit intent text, native sort comparers, inspection boundaries, keyword
placement, upgrade/base previews, and the embedded panel. It verifies that no
Window is created, the foreground FPS limit is unchanged, and real viewport
pointer events can browse, drag and resize the panel without acting on the game.
It draws and
erases map strokes, shows loot, chooses Neow's Leafy Poultice, then Tezcatara's
Biiig Hug and completes the real removal selection/confirmation flow. These
choices affect only the disposable test run. It also checks the full 1920x1080
subviewport, clipped panel content, equal frame margins and no gap below the
title, including after resizing. Optimization checks cover nested DTO changes,
unchanged-domain reuse, deck-cost invalidation, retained map nodes, a 200-card
virtualized deck, and native subscription cleanup on close.

The current local UI preview supplies the real player's native platform name
plus four explicitly marked simulated entries. All five display the same local
run data. Pointer tests flip between three/two entries, select the fifth and
return to the local ID. Separate DTO fixtures cover 0-3 and seven entries,
first/last-page bounds, no wrapping, name/ID selection confirmation, list shrink,
white text, native arrow/frame resources, compact boxes, darker borders and
reserved dragging space. The selected source restores the native loot-frame
blue. Pointer tests close the panel with the native angular cross tinted red,
reopen it with F8, and verify that resizing scales player boxes and the close
icon together while keeping the icon fully inside the navigation row.
Simulated sources do not enable multiplayer.

The interaction checks cover the native deck's view-all-upgrades checkbox,
HUD and relic hover tips, relic details with bounded previous/next navigation,
all three combat pile browsers and their native grid layout, card hover/detail
navigation, and the real F5 confirmation. It accepts F5 in the disposable run,
verifies automatic restoration of the open panel, geometry and fourth source,
then starts another run and reloads the profile preferences from disk. Explicit
closed-state persistence and the new 5/6 minimum/default size are also checked.

`*-live-performance.json` samples 180 normal production frames in combat and on
the map. It reports frame P95/P99, counts above 33/50 ms, capture slice costs,
and acquisition versus display-update time. `map-performance.json` separately
drains 60 full captures synchronously, one per frame, as a stress test and
reports CPU cost, allocations and GC counts. Its FPS is **not** normal-play FPS.
The stress benchmark can also compare the previous DLL: without
`SnapshotEquality`, it includes the previous JSON round-trip. Full UI assertions
require the current version; earlier comparisons used the harness revision
matching that version. Sliced-capture stage timings
include frame waits; use synchronous stress captures for stage CPU/allocation
comparisons. Test screenshots and stage profiling add overhead and are excluded
from the normal frame sampling windows.

After changing DTO properties, regenerate the exact typed comparisons with
`python tests/LiveSharingSmoke/generate_equality.py`. The generator reads only
`SpectatorSnapshot.cs` and writes `SnapshotEquality.cs` from the repository root
derived from its own path. Normal local play skips JSON round-trips; explicitly
set `RMP_SPECTATOR_TRANSPORT_TEST=1` to exercise them. Stage profiling is opt-in
with `RMP_SPECTATOR_METRICS=1` (also enabled by the smoke output variable).

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
comparison. Outputs also include `*-embedded.png` showing the panel inside the
original game. The renderer's screenshots are test outputs only. Optional
`RMP_SMOKE_HOLD=1` keeps the final event window open for manual inspection.

Success is `[LiveSharingSmoke] ALL PASSED`; assertion failures write
`failure.txt` and request exit code 1. Outputs include page JSON and, when
rendering is enabled, spectator/original PNGs. Keep a fresh output folder or
check timestamps so old screenshots or failure files cannot be mistaken for
the latest run. Engine shutdown resource warnings are distinct from harness
assertions; inspect the complete log for runtime errors as well.
