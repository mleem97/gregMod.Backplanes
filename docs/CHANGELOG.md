# Changelog

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
