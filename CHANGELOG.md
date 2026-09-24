# Changelog — gregMod.Backplanes

Format: [Keep a Changelog](https://keepachangelog.com/de/1.0.0/).

## [2.3.0] — 2026-09-24

## [2.2.3] — 2026-09-23

### Fixed

- **Bulk-Käufe (30+ Units, gemischte Preispunkte):** Spawn-Zuordnung läuft
  jetzt über einen Checkout-Snapshot (eine Spec pro Unit in Cart-Reihenfolge,
  qty expandiert) statt Preis-Peek — alle 30 Spawns bekommen die exakte Spec,
  auch bei gemischten Familien zum selben Preis. Familien-Check am Prefab
  korrigiert Cart-Order-Drift via Preis-Peek.
- **Pending-Cap 12 → 200** (LargerCart-Kontext): Bulk-Käufe verdrängen keine
  Einträge mehr; Expiry (10 min) räumt weiter auf.
- **Post-Checkout-Verify:** konfigurierte vs. erwartete Units — bei Abweichung
  Warnung im Log **und** sichtbare gregCore-Notification (`NotifyCore`).

### Added

- **Titan 40M IOPS** (ID 9021): Custom-Server mit 4 TBit pro Port (beide Ports, Redundanz inklusive), RGB-Streifen (Hue rotiert, ~8 s) statt statischem Tint.
- **40M-Tier für SystemX/RISC/Mainframe** (IDs 9022–9024, statische Tints).
- **Vanilla-Port-Audit**: freie Ports aller Nicht-Varianten-Server werden auf Tier-Speed gehoben (IOPS-Leiter, belegte Ports unangetastet, alle 30 s).

### Fixed

- Vanilla-Shop zeigt nur ~5 Karten pro Reihe: Overflow-Reflow verteilt aktive Karten auf 5er-Chunks in geklonten Overflow-Reihen (idempotent, mit Layout-Rebuild).

## [2.2.2] — 2026-09-23

### Fixed

- **Boosted-Server spawnen nicht (stiller Fehlschlag):** `TryGetBaseId` wies
  Base-IDs mit Wert `0` ab — Vanilla-SystemX hat `itemID=0`, die Map hielt
  `9001 → 0`, das Prefab-Routing griff nie und `GetPrefabForItem` lieferte
  null (kaufbar, aber kein physisches Item). Zusätzlich: Karten, die schon
  via `ShopContainsVariant` registriert waren, füllten die Base-ID-Map nach
  `ResetForScene` nicht mehr nach.

## [2.2.1] — 2026-09-23

### Fixed

- **Shop-Karten der Varianten zeigen wieder den echten Namen statt „Unknown":**
  `TryRegisterAll` ruft `RefreshVariantCardTexts` auch dann, wenn alle 20
  Varianten schon registriert sind (vorher Early-Return); zusätzlich Postfix
  auf `ShopItem.Start` (Game-Lookup schreibt für Item-IDs 9001–9020 „Unknown").

## [2.2.0] — 2026-09-23

### Added

- **20 Varianten** statt 8: pro Familie (SystemX/RISC/Mainframe/GPU) fünf Stufen —
  100K/25G (SFP28), 500K/40G (QSFP+), **1M/100G (QSFP28)**, **2M/200G (QSFP56)**,
  **4M/400G (QSFP-DD)**. Preise 20K/100K/250K/500K/1M $ (mehr Gbit ⇒ teurer).
- **gregMod.MoreModules-kompatibel:** ab 40G `sfpType=3` (Vanilla-QSFP+), Shop-Label
  nennt das empfohlene Modul (100G–400G), leere Ports sind auf den Connector vorbelegt.
- Item-IDs 9001–9020 (kollidiert nicht mit MoreModules 1000–3999).

### Fixed

- `SizeKey` ist nicht mehr binär auf 100k/500k begrenzt — er wird aus den IOPS
  abgeleitet (`1m`/`2m`/`4m`). Shop-Namensmatch bevorzugt den längsten
  `VariantDisplayName`.

## [2.1.3] — 2026-09-23

### Fixed

- **Ports am boosted Server bleiben nicht mehr bei 1 Gbps** (u. a. 500K/40G):
  `Server.RegisterLink` + `CableLink.Start` konfigurieren bekannte Varianten-Ports
  sofort; `CollectServerLinks` akzeptiert unzugeteilte Ports auch wenn `typeOfLink`
  noch `None` ist (Insert lief oft vor der Port-Registrierung) und fällt bei leerer
  Trefferliste auf `Resources.FindObjectsOfTypeAll` mit `parentServer`-Pointer-Match
  zurück. Watchlist prüft jetzt auch freie Ports (nicht nur `maxProcessingSpeed`)
  und retryet bei 0 gefundenen Ports alle 5 s.

## [2.1.2] — 2026-09-23

### Fixed

- Persistierte Boosted-Server überleben Save/Reload: `ReadServerId` akzeptiert die stabilen
  gregCore-IDs `gregID:Server:<hex>` (vorher nur `Server.*` → Sidecars blieben leer, Marker
  nie geschrieben). Fallback auf `ServerSaveData.serverID` beim Insert.

### Added

- Konfigurierbarer Toggle-Hotkey (`ToggleKey`-Pref, Default F6), Tasten-HUD-Eintrag und Oeffner fuers F1-Hub (nur mit gregCore).
