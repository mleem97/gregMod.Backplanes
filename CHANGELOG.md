# Changelog — gregMod.Backplanes

Format: [Keep a Changelog](https://keepachangelog.com/de/1.0.0/).

## [2.3.1] — 2026-09-24

### Changed

- F1-hub wiring via GregMenuBinding.BindToggle.
- English strings throughout.

## [2.3.0] — 2026-09-24

### Added

- `PortSpeedMemory`: event-driven port-speed enforcement without polling.
  `ConfigurePort` registers our ports once (instance ID + target speed);
  `InsertSFP`/`InteractOnClick` (existing) plus new `SetConnectionSpeed`/
  `SecondActionOnClick` postfixes correct drift via direct field write.
  Vanilla ports never registered (no effect); stale entries
  self-cleaning via reference check; clear on scene load. Every real
  correction logs one line (`PortSpeed [Hook]: old -> new (name)`) as
  diagnostics for which vanilla path writes speeds back.
## [2.2.3] — 2026-09-23

### Fixed

- **Bulk buys (30+ units, mixed price points):** spawn assignment now runs
  via a checkout snapshot (one spec per unit in cart order,
  qty expanded) instead of price peek — all 30 spawns get their exact spec,
  even with mixed families at the same price. Family check on the prefab
  corrects cart-order drift via price peek.
- **Pending cap 12 → 200** (LargerCart context): bulk buys no longer push out
  entries; expiry (10 min) keeps cleaning up.
- **Post-checkout verify:** configured vs. expected units — on deviation a
  warning in the log **and** a visible gregCore notification (`NotifyCore`).

### Added

- **Titan 40M IOPS** (ID 9021): custom server with 4 TBit per port (both ports, redundancy included), RGB stripe (hue rotating, ~8 s) instead of a static tint.
- **40M tier for SystemX/RISC/Mainframe** (IDs 9022–9024, static tints).
- **Vanilla port audit**: free ports of all non-variant servers are raised to tier speed (IOPS ladder, occupied ports untouched, every 30 s).

### Fixed

- Vanilla shop only shows ~5 cards per row: overflow reflow distributes active cards across 5-chunks in cloned overflow rows (idempotent, with layout rebuild).

## [2.2.2] — 2026-09-23

### Fixed

- **Boosted servers don't spawn (silent failure):** `TryGetBaseId` rejected
  base IDs with value `0` — vanilla SystemX has `itemID=0`, the map held
  `9001 → 0`, prefab routing never engaged and `GetPrefabForItem` returned
  null (buyable, but no physical item). Additionally: cards already
  registered via `ShopContainsVariant` no longer refilled the base-ID map after
  `ResetForScene`.

## [2.2.1] — 2026-09-23

### Fixed

- **Variant shop cards show the real name again instead of "Unknown":**
  `TryRegisterAll` calls `RefreshVariantCardTexts` even when all 20
  variants are already registered (previously early return); additionally a postfix
  on `ShopItem.Start` (game lookup writes "Unknown" for item IDs 9001–9020).

## [2.2.0] — 2026-09-23

### Added

- **20 variants** instead of 8: per family (SystemX/RISC/Mainframe/GPU) five tiers —
  100K/25G (SFP28), 500K/40G (QSFP+), **1M/100G (QSFP28)**, **2M/200G (QSFP56)**,
  **4M/400G (QSFP-DD)**. Prices 20K/100K/250K/500K/1M $ (more Gbit ⇒ pricier).
- **gregMod.MoreModules compatible:** from 40G `sfpType=3` (vanilla QSFP+), shop label
  names the recommended module (100G–400G), empty ports are pre-profiled for the connector.
- Item IDs 9001–9020 (no clash with MoreModules 1000–3999).

### Fixed

- `SizeKey` is no longer binary-limited to 100k/500k — it derives from IOPS
  (`1m`/`2m`/`4m`). Shop name match prefers the longest
  `VariantDisplayName`.

## [2.1.3] — 2026-09-23

### Fixed

- **Ports on boosted servers no longer stay at 1 Gbps** (incl. 500K/40G):
  `Server.RegisterLink` + `CableLink.Start` configure known variant ports
  immediately; `CollectServerLinks` also accepts unassigned ports while `typeOfLink`
  is still `None` (insert often ran before port registration) and falls back to
  `Resources.FindObjectsOfTypeAll` with a `parentServer` pointer match on an empty
  hit list. Watchlist now also checks free ports (not just `maxProcessingSpeed`)
  and retries at 0 ports found every 5 s.

## [2.1.2] — 2026-09-23

### Fixed

- Persisted boosted servers survive save/reload: `ReadServerId` accepts the stable
  gregCore IDs `gregID:Server:<hex>` (previously only `Server.*` → sidecars stayed empty, markers
  never written). Fallback to `ServerSaveData.serverID` on insert.

### Added

- Configurable toggle hotkey (`ToggleKey` pref, default F6), key-HUD entry and opener for the F1 hub (only with gregCore).
