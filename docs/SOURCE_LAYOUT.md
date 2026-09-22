# Source layout

All C# source lives in `src/`, current game/MelonLoader assemblies in `references/`, and project documentation in `docs/`.

- `src/BackplanesMod.cs` — MelonMod entry point, MelonPreferences entries, scene lifecycle,
  F6/Escape overlay input, pause-menu guard, OnGUI wiring.
- `src/BackplanesOverlay.cs` — F6 UI Toolkit panel on the gregCore layer root
  (MusicPlayer pattern): toggle buttons, repair-window steppers, status section,
  Repair-now button, manual click routing, game font.
- `src/BackplanesMod.cs` — MelonMod entry point, MelonPreferences entries, scene lifecycle,
  F6 input + click routing + status refresh, settings-hub tab, gregCore mod contract.
- `src/ServerVariantSpec.cs` — the 8 shop variants (100K/500K x SystemX/RISC/Mainframe/GPU)
  with tint/scale data, canonical IDs, and legacy ID migration.
- `src/CatalogInjector.cs` — typed shop registration, purchase tracking, spawn
  configuration, rack-insert finalization, and save/load repair.
- `src/ServerVisuals.cs` — runtime tint + scale (visual differentiation), idempotent.
- `src/RuntimeVariantRegistry.cs` — save-safe serverId→variant sidecar
  (`UserData/BackplaneBoostServers/server-variants.tsv`), atomic writes + backup.
- `src/RepairGuard.cs` — native-pointer re-entrancy guard (stack-overflow fix).
- `src/CableGuard.cs` — warn-only cable guidance + click diagnostics (never blocks).
- `src/Patches.cs` — minimal Harmony surface (shop, purchase/spawn, server lifecycle, cables).
- `src/Log.cs` — MelonLogger wrapper.

`_legacy/` holds the original v1.0.1 DLL as a reference (never compiled, never shipped).
