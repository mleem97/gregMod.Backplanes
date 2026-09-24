# ISSUE-001 - Modded servers lose IOPS + port config after save/reload

- **Status:** Fix v2.1.0 + v2.1.2 (To-Verify) - see `../BUGFIX_NOTES.md` #1/#2
- **Priority:** High
- **Area:** Save-Repair, RuntimeVariantRegistry, Spawning
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0/v2.1.2
- **Root cause v2.1.2:** gregCore stamped `gregID:Server:<hex>`; `NormalizeServerIdentity`
  only accepted `Server.*` → `_registry.Set` never ran → every `greg_backplanes.*.tsv`
  stayed header-only → no repair after reload.
- **Reports (Steam Workshop):**
  - *DaSlayerOfGames, 18 May* - 125K-IOPS servers (three units, first three customers) back
    to 5K after save/reload; the only fix was "rebuy + reinstall" from the shop
    (approx. 20,000 $ cost, "practically all my cash every time i load"). SystemX tested;
    RISC/MAINFRAME/GPU not yet unlocked -> test coverage missing there.
  - *Felix_IT, 16 May* - installed server afterwards showed 12K IOPS + only 1 Gbit (no
    40G fiber connection); partly standard config even directly after purchase on install.
  - *M@dm@X, 19 Jul* - after buying/installing several **fresh** 500K servers via the shop:
    connections only 1 Gbps instead of 40 Gbps (so reproducible even without reload).
  - *MFDuskink, ~Sep* - "the game frequently convert 125 into 5" (variants fall back to
    base values).

## Symptom
Modded servers (125K/500K) lose their configured IOPS rate and their uplink/port type after
reload, partly directly on buy/install, and fall back to vanilla base values
(5K/12K IOPS, 1 Gbit, RJ45).

## Expected vs. actual
- **Expected:** Placed modded servers keep IOPS + port config after save/reload;
  once bought, the shop configures them immediately.
- **Actual:** Servers load with base stats; the workaround was rebuy/reinstall
  (expensive, no sustainable workflow); in isolated cases also on freshly bought servers
  (only 1 Gbps).

## Environment
- MelonLoader v0.7.2/v0.7.3 (+ FixCoreModule), Unity 6000.4.x, game "Data Center" (Waseku)
- Variant: SystemX 125K persisted via `server-variants.tsv`

## Known root cause (v1.0.x, from `../BUGFIX_NOTES.md` #1/#2)
- `RuntimeVariantRegistry.EnsureLoaded` discarded persisted rows with a `dc_automator_*` prefix
  -> registry empty -> no repair.
- `ShopCartItem.AddSpawnedItem` hook did not exist in the current build (dead code)
  -> spawns never configured.
- Repair in v1.x hung on `ServerInsertedInRack`, which Unity never calls for save restores.

## Fix in v2.1.0
Registry is column-tolerant and normalizes legacy IDs (`dc_automator_*`, ...) to
`greg_backplanes_*`; spawn config runs in the live `ComputerShop.SpawnPhysicalItem` postfix;
save repair as a time-boxed sweep (`Server.Start`/`Awake`/`OnLoadingComplete` postfixes
+ 1/sec `FindObjectsOfType<Server>` for `RepairWindowSeconds`).

## Remaining work / To-Verify
1. **New report M@dm@X (19 Jul):** freshly bought 500K only 1 Gbps - even without reload.
   Counts as a separate symptom of ISSUE-003; reproduce with v2.1.0.
2. MFDuskink "125 into 5" - frequent, not just first-load behavior.
3. Test coverage for RISC/MAINFRAME/GPU variants after save/reload (DaSlayerOfGames).
4. Workflow "rebuy costs 20K$" is a solution hint for the Workshop page, not a bug.
