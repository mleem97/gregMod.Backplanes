# Changelog

## v2.2.2

- **Boosted servers silently failed to spawn (playtest):** `TryGetBaseId` rejected
  base IDs of `0`. Vanilla SystemX's shop card has `itemID=0`, so the map held
  `9001 → 0` but the remap never ran — `GetPrefabForItem` returned null and
  `SpawnAllPurchasedItems` skipped the cart entry with no `SpawnPhysicalItem` log.
  Also: cards that were already marked registered via `ShopContainsVariant` did
  not refill the base-ID map after `ResetForScene`.

## v2.2.1

- **Shop variant cards show the real name again instead of "Unknown":**
  `TryRegisterAll` always runs `RefreshVariantCardTexts` (previously early-returned
  once all 20 variants were registered); added a `ShopItem.Start` postfix (the game
  ID-lookup writes "Unknown" for item IDs 9001–9020).

## v2.2.0

- **Variant matrix expanded to 20 servers** (4 families × 5 bandwidth tiers): new
  1M / 2M / 4M IOPS tiers with 100G / 200G / 400G ports (QSFP28 / QSFP56 / QSFP-DD).
- **gregMod.MoreModules compatibility:** tiers ≥40G use vanilla QSFP+ `sfpType` (3) so
  MoreModules 100G–400G modules insert without port hacks; shop labels name the
  recommended module; empty ports advertise matching connector/module profile.
- Prices scale with Gbps (20K → 1M $); XP unlocks scale with tier. Item IDs 9001–9020
  (no clash with MoreModules 1000–3999).
- `SizeKey` is derived from IOPS (no longer binary 100k/500k); shop name matching
  prefers the longest `VariantDisplayName`.

## v2.1.3

- **Boosted-server ports stuck at Vanilla 1 Gbps fixed:** Harmony postfixes on
  `Server.RegisterLink` and `CableLink.Start` configure known variant ports as soon as
  the game wires them (insert previously ran before ports were discoverable →
  `Ports geprueft=0`). `CollectServerLinks` accepts unassigned ports while
  `typeOfLink` is still `None`, falls back to a scene-wide `parentServer` pointer
  match, and logs raw source counts when empty. Watchlist now audits free port speeds
  every 5 s (not just `maxProcessingSpeed`) and retries when 0 ports are found.
  Verify logs `gefunden` / `belegt` / `abweichend`.

## v2.1.2

- Persisted boosted servers survive save/reload: `ReadServerId` accepts gregCore's stable
  `gregID:Server:<hex>` ids (previously only `Server.*`, so every sidecar stayed empty and
  no markers were ever written). Insert path falls back to `ServerSaveData.serverID`.

## v2.1.1

- Purchase-to-variant mapping hardened, verified against the live game assembly
  (ilspycmd decompile): all hook signatures and cable/port/server fields confirmed.
- Removed the last price-only fallback in `TrackPurchase` (8 variants share 2 price
  points — arbitrary mapping, source of wrong/duplicate configs).
- Pending buy is now consumed exactly once at spawn (FIFO `ConsumePendingSpecForSpawn`);
  `FinalizeInsertedServer` no longer removes a pending entry (double-consume removed).
- Orphaned spawn UIDs expire after 60 s — the 1/sec sweep no longer runs forever.
- Version 2.1.1 (MelonInfo, csproj, workshop metadata); BUGFIX_NOTES + issues tracker
  updated (`docs/issues/`, custom ASCII-safe format).

## v2.1.0

- In-game configuration: **F6 panel on the gregCore UI Toolkit stack** (MusicPlayer
  pattern — `GregPanel` with camera/movement/interact locking, game font, click-routing
  fallback) plus a **Backplanes tab in the gregCore settings hub**. Toggles apply live,
  repair-window steppers, status + **Repair now**. Requires gregCore for the UI.
- Renamed the small tier from 125K to 100K IOPS (internal speed 1.0); old `125k`
  markers (all legacy prefixes) migrate automatically to the new `*_100k` IDs.
- Added runtime visual differentiation: family tint (SystemX orange, RISC violet,
  Mainframe red, GPU lime; 500K brighter) and a taller 4U/8U look (visual only,
  rack slots unchanged). Toggles `ServerTint` / `ServerScale`, default on.
- Hardened empty-port normalization and added click diagnostics: rejected cable
  connections are logged (throttled) with full port state + held cable info.
- Added explicit `BaseSpeed` per variant (0.05 3U / 0.12 7U) instead of magic numbers.

## v2.0.0

- Full refactor of BackplaneBoostServers v1.0.1 into the `GregMod.Backplanes`
  namespace (`gregMod.Backplanes`, typed-first, no reflection graph scans).
- Fixed save/load IOPS+port reset: column-tolerant sidecar, live
  `SpawnPhysicalItem` configuration, time-boxed repair sweep (`Server.Start` +
  1/sec scan). No more rebuying servers after reload.
- Fixed stack-overflow crash (0xC00000FD) via native-pointer re-entrancy guard.
- Connected ports are never rewritten; cable check is warn-only and never blocks.
- Removed shop lag (single typed shop scan, no per-frame work, no `ShopItem` patches).
- Custom-color purchases are never intercepted; strict name-based spec resolution
  (no price-only fallback, no phantom cart duplicates).
