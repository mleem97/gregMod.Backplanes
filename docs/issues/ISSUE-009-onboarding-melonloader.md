# ISSUE-009 - Installation/onboarding (MelonLoader + FixCoreModule) is a support burden

- **Status:** Partial fix (README install section maintained, v2.1.1); Workshop version open
- **Priority:** Low
- **Area:** Docs/Ship-Being (Workshop page, README)
- **Mod:** all (affects BackplaneBoostServers / gregMod.Backplanes as an example)
- **Reports (Steam Workshop):**
  - *Brilyn911, 18 May* - gave a step-by-step guide (MelonLoader 0.7.2/0.7.3
    via Steam with the MelonLoader installer, FixCoreModule as a NEW mod loader /
    compatibility layer) - recurring support demand for "mod missing / DLL not
    loaded" / "game starts, but no mod".

## Symptom
Many users fail at setup: MelonLoader + FixCoreModule (for Unity 6000.x) +
mod DLL in `Data Center/Mods/`. The install workflow is not a one-off thread but
regular support requests.

## Expected vs. actual
- **Expected:** An easy-to-find install path on the Workshop or project page.
- **Actual:** Scattered across threads/comments; a common wrong order
  (FixCoreModule not as a mod but as a loader helper) -> mod "doesn't show up".

## Proposal
1. Short install section in `README.md` (also applies to gregCore-dependent mods):
   - MelonLoader (Steam installer), target: "Data Center"
   - FixCoreModule in `Mods/` (compatibility for Unity 6 / IL2CPP metadata)
   - gregMod DLLs to `Data Center/Mods/`
   - Validate: log/`F5` panel shows up; troubleshooting: `MelonLoader` folder log.
2. Extend the Workshop description with the first 3 steps.
3. (Optional) Promote `gregModmanager` as the installer once the Workshop/Steam sources
   are supported there.

## State in v2.1.1
- README `Installation` section is maintained (MelonLoader >= 0.7.2, gregCore note,
  V1 DLL removal, marker migration): `README.md#Installation`.
- Open: extend the Workshop description (`SteamWorkshop_Beschreibungen.md`) with steps 1-3
  and link install troubleshooting (MelonLoader log).

## Remaining work
- Workshop page: add install steps 1-3 + troubleshooting link.
