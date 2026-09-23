# ISSUE-001 - Modded Server verlieren nach Save/Reload IOPS + Port-Config

- **Status:** Fix v2.1.0 + v2.1.2 (To-Verify) - siehe `../BUGFIX_NOTES.md` #1/#2
- **Prioritaet:** Hoch
- **Bereich:** Save-Repair, RuntimeVariantRegistry, Spawning
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0/v2.1.2
- **Root cause v2.1.2:** gregCore stamped `gregID:Server:<hex>`; `NormalizeServerIdentity`
  only accepted `Server.*` → `_registry.Set` never ran → every `greg_backplanes.*.tsv`
  stayed header-only → no repair after reload.
- **Berichte (Steam Workshop):**
  - *DaSlayerOfGames, 18 May* - 125K-IOPS-Server (drei Stueck, erste drei Kunden) nach
    Save/Reload zurueck auf 5K; einziger Fix war "Rebuy + Reinstall" aus dem Shop
    (ca. 20 000 $ Kosten, "practically all my cash every time i load"). SystemX getestet;
    RISC/MAINFRAME/GPU noch nicht freigeschaltet -> Testabdeckung fehlt dort.
  - *Felix_IT, 16 May* - eingebauter Server zeigte danach 12K IOPS + nur 1 Gbit (kein
    40G-Faseranschluss); auch direkt nach Kauf beim Einbau teils Standard-Config.
  - *M@dm@X, 19 Jul* - nach Kauf/Install mehrerer **frischer** 500K-Server via Shop:
    Verbindungen nur 1 Gbps statt 40 Gbps (also auch ohne Reload reproduzierbar).
  - *MFDuskink, ~Sep* - "the game frequently convert 125 into 5" (Varianten fallen auf
    Basiswerte zurueck).

## Symptom
Modded Server (125K/500K) verlieren nach Reload bzw. teilweise direkt bei Kauf/Install
ihre konfigurierte IOPS-Rate und ihren Uplink/Port-Typ und fallen auf Vanilla-Basiswerte
(5K/12K IOPS, 1 Gbit, RJ45) zurueck.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Platzierten Modded Servern bleiben nach Save/Reload IOPS + Port-Config
  erhalten; einmal gekauft konfiguriert der Shop sie sofort.
- **Tatsaechlich:** Server laden mit Basis-Stats; Workaround war Rueckkauf/Neu-Install
  (teuer, kein tragbarer Workflow); in Einzelfaellen auch bei frisch gekauften Servern
  (nur 1 Gbps).

## Umgebung
- MelonLoader v0.7.2/v0.7.3 (+ FixCoreModule), Unity 6000.4.x, Game "Data Center" (Waseku)
- Variante: SystemX 125K via `server-variants.tsv` persistiert

## Bekannte Root-Cause (v1.0.x, aus `../BUGFIX_NOTES.md` #1/#2)
- `RuntimeVariantRegistry.EnsureLoaded` verwarf Persisted-Rows mit `dc_automator_*`-Praefix
  -> Registry leer -> kein Repair.
- `ShopCartItem.AddSpawnedItem`-Hook existierte im aktuellen Build nicht (dead code)
  -> Spawns nie konfiguriert.
- Repair hing v1.x an `ServerInsertedInRack`, das Unity fuer Save-Restores nicht aufruft.

## Fix in v2.1.0
Registry ist spalten-tolerant und normalisiert Legacy-IDs (`dc_automator_*`, ...) auf
`greg_backplanes_*`; Spawn-Config laeuft im Live-`ComputerShop.SpawnPhysicalItem`-Postfix;
Save-Repair als zeitbegrenzter Sweep (`Server.Start`/`Awake`/`OnLoadingComplete`-Postfixes
+ 1/sec `FindObjectsOfType<Server>` fuer `RepairWindowSeconds`).

## Verbleibende Arbeit / To-Verify
1. **Neue Meldung M@dm@X (19 Jul):** frisch gekaufte 500K nur 1 Gbps - auch ohne Reload.
   Gilt als separates Symptom von ISSUE-003; mit v2.1.0 reproduzieren.
2. MFDuskink "125 into 5" - haeufig, nicht nur beim ersten Load-Verhalten.
3. Testabdeckung fuer RISC/MAINFRAME/GPU-Varianten nach Save/Reload (DaSlayerOfGames).
4. Workflow "Rebuy kostet 20K$" ist Loesungs-Hinweis fuer die Workshop-Seite, nicht Bug.