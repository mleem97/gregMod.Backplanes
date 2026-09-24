# ISSUE-006 - Incompatibility with the "Svc Service" mod: saves don't load

- **Status:** Open (v2.1.0 so far only a less invasive hook surface) - see `../BUGFIX_NOTES.md` #9
- **Priority:** Medium
- **Area:** Compatibility, patch surface
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0
- **Reports (Steam Workshop):**
  - *szymon511* - with the "Svc Service" mod installed alongside: the game doesn't load the save
    (hanging/crashing) as long as both mods are active.
  - *BrassPeddler* - confirms the same malfunction (see also ISSUE-002, same
    environment).

## Symptom
Saves don't start when BackplaneBoostServers is active together with "Svc Service".

## Expected vs. actual
- **Expected:** Both mods coexist.
- **Actual:** Save load aborts/hangs (v1.x).

## Known root cause (v1.0.x, from `../BUGFIX_NOTES.md` #9, presumably)
V1.x patched a wide reflective surface (`Server.Awake/Start/OnEnable`,
`ShopItem.Awake/Start/UpdateVisualState`, `Technician.*`, assembly-wide static scans),
which collides with foreign patches and other mods' caches.

## Fix in v2.1.0
Minimal hook surface (11 patch methods; no `ShopItem`/`Technician` patches; no
assembly-wide scans); every patch is exception-proof (the mod can never take down
the game thread). Coexistence is still NOT verified in game.

## Remaining work / To-Verify
1. Test scenario "Svc Service + gregMod.Backplanes v2.1.0": load a save without modded servers,
   then a save with modded servers; check startup behavior + log in each case.
2. If still blocked: identify hook conflicts via patch report
   (Harmony `PatchProcessor.GetOriginalInstructions`), possibly resolve the `Server.Start` patch
   dynamically against postfix/dependency.
3. Record the result (OK/still broken) on the Workshop page / in the CHANGELOG.
