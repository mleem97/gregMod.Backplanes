# Issue-Tracker — gregMod.Backplanes

Backlog aus den Steam-Workshop-Kommentaren zu **BackplaneBoostServers** (v1.0.0/v1.0.1,
Legacy-Mod) → Transfer in **gregMod.Backplanes** (v2.1.0). Jede Datei `ISSUE-###-*.md`
dokumentiert ein gemeldetes Symptom inkl. Reproduktion, Umgebung und Verweis auf den
Stand in `../BUGFIX_NOTES.md`.

Legende Status: **Open** (offen / zu verifizieren) · **Fix v2.1.0** (im Changelog als
behoben verzeichnet, Regression offen) · **Fix v2.1.1** (gegen Live-Assembly verifiziert,
im Code behoben) · **Fixed** (bestätigt).

| # | Titel | Status | Priorität | Bereich | Reporter |
|---|-------|--------|-----------|---------|----------|
| [001](ISSUE-001-save-reload-config-verlust.md) | Modded Server verlieren nach Save/Reload IOPS + Port-Config | Fix v2.1.0 (To-Verify) | Hoch | Save-Repair | DaSlayerOfGames, Felix_IT, M@dm@X, MFDuskink |
| [002](ISSUE-002-stackoverflow-saveload-crash.md) | Stack Overflow (0xC00000FD) beim Laden von Saves mit modded Servern | Fix v2.1.0 (To-Verify) | Hoch | Save-Repair | BrassPeddler |
| [003](ISSUE-003-port-kabel-sfp-anforderungen.md) | Ports verlangen bei jedem Laden andere Kabel/SFP; 0G Max; kein Wiedereinstecken; 1 Gbps trotz 500K | Fix v2.1.0 + v2.1.3 (To-Verify); Matrix v2.2.0 | Hoch | Ports/Cables | Brilyn911, Shaun Eyebright, Playtest |
| [004](ISSUE-004-custom-farben-kabel-racks.md) | Custom-Farben für Kabel/Racks liefern immer Default-Farbe | Fix v2.1.0 (To-Verify) | Mittel | Shop/Cart | Shaun Eyebright, Gothicdude1044, Revoltec, DNFplays |
| [005](ISSUE-005-shop-klick-doppelartikel.md) | Shop-Kauf: erster Klick zählt nicht / doppelte Artikel bei einmaliger Zahlung | Fix v2.1.1 (Spawning-FIFO) | Mittel | Shop/Cart | Shaun Eyebright |
| [006](ISSUE-006-inkompatibel-svcservice.md) | Inkompatibilität mit „Svc Service“-Mod → Saves laden nicht | Open (To-Verify) | Mittel | Kompatibilität | szymon511, BrassPeddler |
| [007](ISSUE-007-visuelle-varianten-unterscheidung.md) | Keine visuelle Unterscheidung der Varianten (wirkt „cheaty“, 125K/5K verwechselbar) | Ok in v2.1.0 (Toggle), Feedback offen | Mittel | Visuals | MFDuskink, H3draut3r, Gothicdude1044 |
| [008](ISSUE-008-mehrere-server-pro-kauf.md) | Ein Kauf erzeugt mehrere 500K-Server, nur der erste ist einsetzbar | Fix v2.1.1 (Doppel-Konsum entfernt) | Mittel | Shop/Spawning | Brilyn911 |
| [009](ISSUE-009-onboarding-melonloader.md) | Installation/Onboarding (MelonLoader + FixCoreModule) ist Support-Last | Open (Doku) | Niedrig | Docs/Ship | Brilyn911 |