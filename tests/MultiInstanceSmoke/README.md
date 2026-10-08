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
