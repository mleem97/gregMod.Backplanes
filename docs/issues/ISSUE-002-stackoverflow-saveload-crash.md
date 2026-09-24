# ISSUE-002 - Stack Overflow (0xC00000FD) when loading saves with modded servers

- **Status:** Fix v2.1.0 (To-Verify) - see `../BUGFIX_NOTES.md` #3
- **Priority:** High
- **Area:** Save-Repair, lifecycle patches (Server.Awake/Start/OnEnable)
- **Mod:** BackplaneBoostServers v1.0.1 -> gregMod.Backplanes v2.1.0
- **Reports (Steam Workshop):**
  - *BrassPeddler, 4 Jul* - crash 0xC00000FD (stack overflow) when loading a save
    with modded servers, reproducible. Timing approx. 2 s after "Scene loaded: BaseScene".
    Environment: MelonLoader v0.7.2 + FixCoreModule, Unity 6000.4.12, game build July 2026,
    mod v1.0.1.

## Symptom
When loading a save in which modded servers are already persisted, the process crashes
with `0xC00000FD` (stack overflow) shortly after the scene has loaded.

## Expected vs. actual
- **Expected:** Save loads; persisted servers are repaired as soon as they become active.
- **Actual:** Infinite recursion path in the server-lifecycle postfixes.

## Known root cause (v1.0.1, from `../BUGFIX_NOTES.md` #3)
The `_serversBeingRepaired` guard compared by **reference equality over boxed
Il2Cpp wrappers** - every field access boxes a new wrapper, so `Contains()` always
missed, and the `Awake/Start/OnEnable` postfixes recursed into their own
configuration path.

## Fix in v2.1.0
`RepairGuard` is keyed by **native object pointers** (stable per Unity object) plus a
global depth cap (8) as a circuit breaker; lifecycle repair additionally only runs
inside the repair window.

## Remaining work / To-Verify
1. Load a save with modded servers from the v1.x era and check crash-freedom against v2.1.0.
2. Regression: test several saves / large racks / many customers (`ServerApplyLoad`) in parallel
   with the sweep (recursion alternatively via `Server.Start` + 1/sec sweep).
3. Also comb through the BrassPeddler report with the "Svc Service" mod (see ISSUE-006).
