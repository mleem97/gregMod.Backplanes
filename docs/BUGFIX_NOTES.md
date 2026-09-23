# BUGFIX_NOTES — v1.x reports → v2.x fixes

## v2.2.2 — Boosted-Server spawnen nicht (Playtest, 2026-09-23)

**Symptom:** Karte kaufbar (`Buy: id=9001 … Tracked purchase`), Cart voll,
`SpawnAllPurchasedItems` läuft durch — aber **kein** `SpawnPhysicalItem`,
`spawnedItems` bleibt `count=0`. Vanilla-Käufe (`id=0`/`id=2`) spawnen normal.

**Root cause:** `CatalogInjector.TryGetBaseId` verlangte `baseItemId != 0`.
Vanilla-SystemX-Shopkarte hat `itemID=0` (`Buy: id=0 … name='System X 3U …'`),
die Map hielt also `9001 → 0`. Der Check verhinderte das Remap in
`GetPrefabForItemPrefix` → Original suchte itemID 9001 → null → Spawn übersprungen.

Zweite Lücke: `ShopContainsVariant`-Early-Path markierte die Karte nur als
registriert, ohne die Base-ID-Map nach `ResetForScene` neu zu füllen — nach
Scene-Wechsel fehlte das Routing auch bei korrekter Karte.

**Fix:**
- `TryGetBaseId`: Erfolg = Key vorhanden (0 ist gültige Base-ID).
- `EnsureBaseIdMapping` beim Already-Registered-Pfad; Registrierungslog mit `baseId=`.
- Warnung in `GetPrefabForItemPrefix`, wenn 9001–9021 ohne Base-ID durchlaufen.

---

## v2.2.0 — Variant matrix × MoreModules (2026-09-23)

Feature release: four families × five bandwidth tiers (100K/25G · 500K/40G ·
1M/100G · 2M/200G · 4M/400G). Tiers ≥40G use vanilla QSFP+ `sfpType` so
**gregMod.MoreModules** modules (QSFP28/56/DD, IDs 1000–3999) insert without
port hacks; shop labels show the recommended module. Item IDs 9001–9020.
`SizeKey` derived from IOPS (100k/500k/1m/2m/4m).

---

## v2.1.3 — Port speeds stuck at 1 Gbps on boosted servers (playtest, 2026-09-23)

**Symptom:** fresh 500K servers show `1 Gbps` on every uplink (IOPS OK:
`Verify … max=5.000`, but `Ports geprueft=0`); log never changed a port.

**Root cause:** `ConfigureServerAndPorts` ran from `ServerInsertedInRack` before the
game had registered `CableLink`s (`typeOfLink` still `None` / `parentServer` unset /
`cablelinks` empty). `IsServerLinkFor` required `typeOfLink == Server` → 0 links →
ports kept Vanilla `connectionSpeed = 0.2` (UI format `{0:0.##} Gbps` with
`connectionSpeed * 5` → “1 Gbps”). The 5 s watchlist only re-checked
`maxProcessingSpeed`, so a correct IOPS value never triggered a port repair.

**Fix:**
- Harmony postfixes on `Server.RegisterLink` and `CableLink.Start` →
  `OnLinkRegistered` / `OnLinkStarted` configure a single port for known variants
  (before `parentServer`/`typeOfLink` are fully wired, via `GetComponentsInParent`
  fallback on Start).
- `CollectServerLinks`: `cablelinks` force-accepted; children scan accepts
  ports while `typeOfLink` is `None` (only rejects Switch/PatchPanel parents);
  empty result falls back to `Resources.FindObjectsOfTypeAll<CableLink>` matching
  `parentServer` pointer; empty search logs raw counts when verbose.
- `TickWatchlist` inspects free ports every 5 s and reconfigures on wrong speed or
  0 found ports (IOPS-correct servers no longer skip port repair).
- Verify logs `gefunden` / `belegt` / `abweichend` and warns with the expected Gbps.

---

## v2.1.2 — Marker persistence with gregCore hardware IDs (2026-09-23)

Sidecars were written but **always empty** (`# serverId\tvariantId` only). Root cause:
gregCore's `HardwareIdPersistencePatch` stamps servers as `gregID:Server:<12-hex>`, while
`CatalogInjector.NormalizeServerIdentity` only accepted `Server.*`. Every
`_registry.Set(...)` early-returned → no markers → no repair after reload → boosted
servers fell back to base IOPS.

**Fix:** accept `gregID:Server:*` (strip Unity `_suffix` if present), keep `Server.*` for
legacy, fall back to `ServerSaveData.serverID` on insert when `Server.ServerID` is empty.

---

## v2.1.1 — Purchase-to-variant mapping hardened (playtest findings from Workshop discussion)

Phase-2 verification against the **live game assembly** (`Assembly-CSharp.dll`, decompiled
with ilspycmd) confirmed every hook signature and field used by the mod, and surfaced three
remaining purchase-pipeline defects. All three are fixed in code in this release; kill-list
follows (`docs/issues/`).

**Verified against the game (no code change needed):**
- `CableLink.TypeOfLink` enum: `None/Server/Switch/Base/LB/PatchPanel` → `TypeOfLink.Server`
  is correct; `cableIDsOnLink`, `connectionSpeed`, `isSFPPort`, `sfpTypeInserted`,
  `sfpTypeSupported`, `isFibrePort`, `insertedSFP`, `parentServer` all exist.
- `ComputerShop.ButtonBuyShopItem(int, int, PlayerManager.ObjectInHand, string, bool
  isCustomColor=false)` and `ComputerShop.SpawnPhysicalItem(GameObject, int,
  PlayerManager.ObjectInHand) -> Nullable<int>` match the prefixes/postfixes.
- `ShopCartItem` is per-item with `itemID/price/itemType/itemName/itemColor/hasCustomColor/quantity`.
- `PlayerManager.ObjectInHand` contains `Server1U/Server2U/Server3U/CableSpinner`.
- `Server.Awake/Start` (virtual), private `OnLoadingComplete`, `ServerInsertedInRack`,
  `RepairDevice`, `maxProcessingSpeed/currentProcessingSpeed/activeLinks/cablelinks`.

**Fixed in code:**
1. **Price-only fallback removed (`TrackPurchase`)** — 8 variants share 2 price points
   (20000 ×4 / 100000 ×4), so `FindSpecByPrice` mapped arbitrarily. Purchases now resolve
   strictly by name (`FindByShopValues`) then exact variant ID; without a match nothing is
   tracked (warning logged). Removes the last phantom-mapping path (#7's price fallback
   that #5/#8 reported as wrong/duplicate configs).
2. **Pending buy consumed exactly once at spawn** — v2.1.0 *peeked* the pending spec at
   spawn (leaving it queued) and *also* removed one unconditionally at `FinalizeInsertedServer`,
   so a second purchase of the same price could consume the *next* purchase's entry
   (`ConfigureSpawnedItem` now uses FIFO `ConsumePendingSpecForSpawn`; `FinalizeInsertedServer`
   no longer removes a pending entry).
3. **Orphaned spawn-UIDs expire** — drained spawns now carry a creation timestamp and the
   1/sec sweep drops UIDs whose GameObject never surfaces in `ComputerShop.spawnedItems`
   after 60 seconds (previously the sweep could run every second for the whole scene).

---

Source: Steam Workshop discussion on Backplane Boost Servers v1.0.0/v1.0.1.
Each item: symptom → root cause (verified in the v1.0.1 decompile in `_legacy/`) → v2 fix + where.

## v2.1.0 — Visual differentiation, 100K tier, connection diagnostics

**Request:** boosted servers are indistinguishable from vanilla (same 3U/7U size, same
colors); 125K is awkward to calculate with; connections randomly demand RJ45 or refuse
to plug ("free hole" missing).

**Changes:**
- **Tint:** family body materials are recolored at runtime (SystemX→orange, RISC→violet,
  Mainframe→red, GPU→lime; 500K brighter). Matching is by base-color proximity + family
  color-word fallback because material names are not visible without runtime inspection;
  screens/lights/glass excluded by name. Toggle `ServerTint` (default on).
- **Scale:** absolute Y scale 4/3 (small tier) / 8/7 (large tier) for a 4U/8U look.
  **Visual only** — rack slot occupancy is unchanged, neighbors may visually overlap.
  Toggle `ServerScale` (default on).
- **100K tier:** small variants are now 100K IOPS (internal speed 1.0). Variant IDs
  changed to `greg_backplanes_*_100k`; old `125k` markers (all legacy prefixes) resolve
  to the 100K specs and are normalized on save. Prices/XP unchanged.
- **Ports:** empty ports are always fully normalized (speed cap + `sfpTypeSupported/Inserted`
  together + `isSFP/isFibrePort`), connected ports stay untouched as before.
- **Click diagnostics:** `CableLink.InteractOnClick` postfix compares `cableIDsOnLink`
  before/after — cable in hand + port still empty = rejection, logged (throttled) with
  full port state + held cable info. This turns "sometimes needs RJ45" into actionable
  reports. Port meshes themselves cannot be changed (no mesh fields in the game API).

---

Source: Steam Workshop discussion on Backplane Boost Servers v1.0.0/v1.0.1.
Each item: symptom → root cause (verified in the v1.0.1 decompile in `_legacy/`) → v2 fix + where.

## 1. Servers reset to 5K/12K IOPS + 1G after save/reload (DaSlayerOfGames, Felix_IT)

**Root cause (two compounding bugs):**
- `RuntimeVariantRegistry.EnsureLoaded` dropped rows whose *server-ID column* started
  with `dc_automator_`. v1.0.0 wrote variant IDs into that column, so after the v1.0.1
  update **every** persisted row was discarded as "polluted" and the registry stayed
  empty (`TryRepairKnownPersistedServer` early-outs on `Count == 0`).
- The `ShopCartItem.AddSpawnedItem` Harmony hook is **dead code**: no such method
  exists in the current game build, Harmony silently skips it, so bought servers were
  never configured at spawn time — only the fragile pending-queue path remained.

**Fix:** `RuntimeVariantRegistry` is column-order tolerant (whichever TSV column resolves
to a known variant wins), migrates legacy IDs to canonical `greg_backplanes_*` IDs,
saves atomically (tmp + replace + `.bak`); spawns are configured in the **live**
`ComputerShop.SpawnPhysicalItem` postfix; save-loaded servers are repaired by a
time-boxed sweep (`Server.Start`/`Awake`/`OnLoadingComplete` postfixes + 1/sec
`FindObjectsOfType<Server>` scan for `RepairWindowSeconds` after scene load).

## 2. Repair window opened but nothing was configured (Brilyn911)

**Root cause:** `BeginKnownServerRepairWindow` only *loaded* the TSV; the actual
`ConfigureServerAndPorts` depended on `ServerInsertedInRack` firing, which Unity does
**not** call for save-restored instances. Zero `serverChanged=True` lines in the log.

**Fix:** repair no longer depends on `ServerInsertedInRack` for loaded saves —
`Server.Start` postfix + active sweep configure save-restored instances directly
(same mechanism Brilyn911's companion mod proved in-game).

## 3. Stack overflow crash loading saves with modded servers (BrassPeddler, 0xC00000FD)

**Root cause:** the `_serversBeingRepaired` guard used reference equality over
**boxed** Il2Cpp wrappers — every field read boxes a new wrapper, so `Contains()`
never hit and `Awake/Start/OnEnable` postfixes recursed into their own configure path.

**Fix:** `RepairGuard` is keyed by **native object pointers** (stable per Unity object)
plus a global depth cap (8) as circuit breaker. Lifecycle repair additionally only runs
inside the repair window.

## 4. Ports demand a different cable/SFP every load; 0G max; can't reconnect (Brilyn911, Shaun Eyebright)

**Root cause:** `ConfigureCableLink` rewrote `connectionSpeed`, `sfpType*`, `isSFP/FibrePort`
and nulled `insertedSFP` on **every** repair — including live, cabled ports — fighting the
game's own link state; the `CableLink.InteractOnClick` prefix **blocked** (`return false`)
connections based on lane heuristics that misclassified cables.

**Fix:** `ConfigurePort` returns immediately for connected ports (`cableIDsOnLink != 0`
or `insertedSFP` set) and never removes an inserted module; `CableGuard` is **warn-only**
and always returns `true` (throttled notification + log, toggle `CableWarnings`).

## 5. Shop/computer lag when opening (Edmix)

**Root cause:** `TryRegisterAll` ran full assembly-wide discovery (type scans,
`Resources.FindObjectsOfTypeAll`, object-graph BFS) from many hooks including per-`ShopItem`
`Awake/Start`, plus per-second `MaintainSpawnedVariants` scans from `OnUpdate`.

**Fix:** registration is a single typed scan of `ComputerShop.shopItems`, runs once per
shop-open (early-out when all 8 registered); `OnUpdate` is a cheap timestamp gate, scans
only inside the repair window; all `ShopItem` visual patches removed (card text is set
once at clone time).

## 6. Custom-color cables/racks always deliver default (Shaun, Revoltec, Gothicdude1044, DNFplays)

**Root cause:** purchase/cart hooks intercepted the shared buy path and the shop-items
collection was rebuilt via fragile reflection; color-picker (`isCustomColor`) purchases
got caught in the crossfire.

**Fix:** `ButtonBuyShopItem` prefix returns immediately for `isCustomColor` purchases;
no `ShopCartItem` hooks at all; `shopItems` is extended with a typed
`Il2CppReferenceArray<ShopItem>` copy, original preserved on any failure.

## 7. Duplicate cart items / charged once, received twice (Shaun Eyebright)

**Root cause:** `FindByPurchase`/`ResolveSpecFromCartItem` fell back to **price-only**
matching (`BaseItemId + Price`), so vanilla items sharing a price point were treated as
variants and the pending queue was consumed twice.

**Fix:** `FindByShopValues` is **name-only** — no price fallback, no phantom matches.

## 8. 125K vs base confusion, no visual differentiation (MFDuskink, H3draut3r)

**Fix (partial):** shop cards are labeled `"<Name> (1-lane fiber)"` / `"(4-lane fiber)"`
with distinct prices; per-size 3D model/tint changes are out of scope for v2.0.0
(documented limitation — the mod reuses vanilla models like v1.x).

## 9. Incompatibility with Svc Service mod (szymon511)

**Root cause (likely):** v1.x hooked a wide reflective surface (`Server.Awake/Start/OnEnable`,
`ShopItem.Awake/Start/UpdateVisualState`, `Technician.*`, assembly-wide static scans)
that collides with other mods' patches and object graphs.

**Fix:** minimal hook surface (11 patch methods, no `ShopItem`/`Technician` patches, no
assembly-wide scans); every patch is exception-proof (mod can never take the game thread
down). Coexistence still needs in-game verification — please report.

## 10. Stale variant-ID scheme (`dc_automator_*` in a `BackplaneBoostServers` mod)

**Fix:** canonical IDs are `greg_backplanes_<family>_<size>`; all legacy prefixes
(`dc_automator_`, `data_center_automator_`, `bbs_`, `backplane_*`, …) are accepted on
read and normalized on write. No more silent fallbacks to base stats.
