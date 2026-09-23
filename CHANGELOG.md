# Changelog — gregMod.Backplanes

Format: [Keep a Changelog](https://keepachangelog.com/de/1.0.0/).

## [Unreleased]

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
