# ISSUE-005 - Shop-Kauf: erster Klick zaehlt nicht / doppelte Artikel bei einmaliger Zahlung

- **Status:** Fix v2.1.1 (Spawning-FIFO) - siehe `../BUGFIX_NOTES.md` #5/#7 + v2.1.1-Sektion
- **Prioritaet:** Mittel
- **Bereich:** Shop/Cart-Resolve (FindByShopValues), Purchase->Spawn-Konsum
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.1
- **Berichte (Steam Workshop):**
  - *Shaun Eyebright, 24 May* - beim Kaufen: der erste Klick registriert nicht, Spieler
    klickt mehrmals, bekommt dann mehr Produkte als bezahlt (einmal gezahlt, doppelt/mehrfach
    im Warenkorb bzw. geliefert).
  - *Edmix, 17 May* - Lag beim Oeffnen von Computer/Shop (separates Symptom; Root-Cause
    Performance - siehe unten).

## Symptom
Klick auf "Buy" im Shop: erste Betaetigung wird verschluckt (Vaetigungen im Mod-Pfad),
Folge-Klicks fuehren dazu, dass doppelt/mehrfach Artikel geliefert werden, obwohl nur einmal
Kasse gedrueckt wurde; Warenkorb-Eintraege duplizieren.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Ein Klick = ein Artikel = eine Abbuchung; kein Vuegeliges Verschluccken.
- **Tatsaechlich:** Erster Klick geht verloren, danach Mehrfachlieferung bei Einmalzahlung.

## Bekannte Root-Cause (v1.0.x, aus `../BUGFIX_NOTES.md` #7)
`FindByPurchase`/`ResolveSpecFromCartItem` fiel auf **Preis-only**-Matching zurueck
(`BaseItemId + Price`), so dass Vanilla-Items mit gleichem Preis als Varianten behandelt
und die Pending-Queue zweifach abgearbeitet wurde.

## Fix in v2.1.0
`FindByShopValues` ist **name-only** - kein Preis-Fallback, keine Phantom-Matches. Zusaetzlich
in v2.1.0: Clicks werden manuell geroutet (kein EventSystem), Doppel-Feuerschutz (500 ms)
im Trainer-Panel-Pattern; im Shop selbst nichts angefasst, nur Resolve korrigiert.

## Fix in v2.1.1 (Code gegen Live-Assembly verifiziert)
Zweiter Doppel-Lieferpfad entfernt: `ConfigureSpawnedItem` hat den Pending-Eintrag nur
**gepeeked** (Linux-Konsum), `FinalizeInsertedServer` entfernte danach bedingungslos einen
Eintrag - Doppel-Konsum, der bei gleichen Preispunkten (20000/100000) den **naechsten**
Kauf fehl-verbraucht hat. Jetzt wird genau **ein** Pending-Eintrag pro Spawn FIFO konsumiert
(`ConsumePendingSpecForSpawn`), `FinalizeInsertedServer` entfernt nichts mehr aus der Queue.
`SpawnedItem`s, deren GameObject nie auftaucht, verfallen nach 60 s (kein Dauer-Sweep).

## Verbleibende Arbeit / To-Verify
1. Kauf von Varianten-Servern zusammen mit Vanilla-Items gleichen Preises -> keine
   Doppel-Lieferung, korrekte Abbuchung (gegen v2.1.1-DLL im Spiel bestaetigen).
2. "Erster Klick verschluckt": verifizieren, ob es sich rein um den ehemaligen Resolve-Bug
   handelte oder um UI-Fokus/Click-Routing im Spiel selbst (dann kein Mod-Bug).
3. Edmix-Lag: in `../BUGFIX_NOTES.md` #5 + v2.1.1 (Sweep-Expiry) als Fix verzeichnet -
   in grossen Saves gegen v2.1.1 messen.