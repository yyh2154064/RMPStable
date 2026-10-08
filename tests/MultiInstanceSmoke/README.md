# Isolated native mirror smoke harness

Build this project with `GameManagedDir` set to the matching game's managed directory.
Use `GameCompatibility=beta` for v0.111.0; stable uses the default.
Stage the DLL and JSON alongside RMP in an **owned copy** of the game executable/managed runtime. Do not change the installed game's mods directory.
The harness JSON has `has_pck=false`; no test PCK is required.

Set the copied game's `APPDATA` and `LOCALAPPDATA` to disposable directories inside the workspace.
Use a fresh profile with intro skipped, the mods warning acknowledged and RMP/MultiInstanceSmoke enabled. Existing non-run developer profile seeds can be copied; never use actual run saves.

Parent-only environment:

- `RMP_MULTI_TEST=1`: run the deterministic test, then exit.
- `RMP_MULTI_HEADLESS=1`: force the separately launched renderer into headless mode too.
- `RMP_MULTI_MAIN_PACK=<absolute original game PCK>`: required when the copied executable has no adjacent PCK.

Run the copied executable with `--headless --force-steam off --max-fps 30 --resolution 1280x720 --main-pack <PCK>` and an owned log path.
The renderer clears test variables, disables the test mod, authenticates over a new named pipe and writes only its own generated profile.
The harness reports `PASS` per assertion and exits with `ALL PASSED` / status 0 or `FAIL` / status 1.
Godot dummy rendering can report texture/resource warnings; these do not constitute GPU verification.

The checks cover packet identity/order/generation, exact native combat state, independent process/profile, preserved outer shell, native hitboxes, authority validation, ending turns, targeted attacks, failed hash gating, reopening mid-combat and owned child cleanup.

For GPU/input verification, launch the owned copy windowed and offscreen with
`--force-steam off`, mute its isolated settings, and keep `NoFocus` enabled.
The native suite sends messages only to the authenticated renderer HWND; it
does not move the desktop cursor. It also checks original settings/pause
controls, cancellation of source confirmations, visible drawing, and both
warm F8 and consecutive complete F5 recoveries within 1000 ms. F5 timing
starts when the confirmed restore is invoked and ends after a visible,
drawn, synchronized window with current permission accepts a legal action.

Additional parent-only options:

- `RMP_MULTI_FULL_TEST=1`: run the original map/combat/selection/reward/rest/shop/treasure/event/next-act flow and exit with `FULL PASSED`.
- `RMP_MULTI_ROOMS_TEST=1` together with full mode: skip combat and reward checks for focused room diagnostics.
- `RMP_MULTI_TREASURE_TEST=1` together with full mode: focus on treasure diagnostics.

Full mode uses a deterministic test deck and gold fixture in its disposable
run. It compares original persistent state and drawing state across the two
processes; source and replica both retain original card and room logic.
Test-only replica opt-in can save its own viewport images. These files and
the harness must never be included in the user package. Record the exact
mod/test DLL hashes before launch, and require final passing logs from that
same mod DLL before delivery.
