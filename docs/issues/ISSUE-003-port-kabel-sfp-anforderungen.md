# ISSUE-003 - Ports verlangen bei jedem Laden andere Kabel/SFP; 0G Max; kein Wiedereinstecken

- **Status:** Fix v2.1.0 (To-Verify) + Fix v2.1.3 Port-Speed 1 Gbps (To-Verify) - siehe `../BUGFIX_NOTES.md` #4 + v2.1.3; Matrix v2.2.0 (20 Varianten / MoreModules)
- **Prioritaet:** Hoch
- **Bereich:** ConfigurePort / ConfigureCableLink, CableGuard, CollectServerLinks, RegisterLink
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0/v2.1.3/v2.2.0
- **Berichte (Steam Workshop):**
  - *Brilyn911, 22 May* - Anforderungen wechseln mit jedem Save/Load: mal braucht ein Port
    Ethernet, mal SFP, manchmal 4-Way-SFP; nach Reload ging "nothing else back in" zu pluggen.
  - *Shaun Eyebright, 24 May* - nach dem Einschalten eines Servers zeigen Ports "0 gbps max",
    nach Ziehen eines Kabels laesst sich kein Kabel mehr anschliessen.
  - *Brilyn911, 22 May (Speed)* - 1G-SFP/Ethernet-Links laufen nur auf 10/10; Uplink auf
    25/25 trotz 1-Lane-Fiber - maessige Bandbreiten-Erwartung (z. T. Spiel-/Topologie-Frage,
    nicht zwingend Bug; als To-Verify-Doku aufnehmen).
  - *Playtest, 23 Sep (v2.1.2)* - frische 500K-Server: Ports bleiben "1 Gbps" obwohl
    IOPS-Verify OK ist (`Ports geprueft=0`); auch ohne Reload. -> Fix v2.1.3.

## Symptom
Port-Konfiguration ist nicht stabil: Kabel-/SFP-Anforderungen wechseln zwischen Loads,
Ports zeigen "0 gbps max" und verweigern das (Wieder-)Einstecken von Kabeln.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Ports behalten ihren Typ; Kabel stecken ein, solange Steckplatz + Kabel
  zueinander passen; Ports zeigen ihre echte Max.-Rate.
- **Tatsaechlich:** Repair ueberschrieb auch belegte Ports, nullte eingesteckte SFPs und
  der `CableLink.InteractOnClick`-Prefix blockierte Verbindungen anhand von Lane-Heuristiken.

## Bekannte Root-Cause (v1.0.x, aus `../BUGFIX_NOTES.md` #4)
`ConfigureCableLink` schrieb `connectionSpeed`, `sfpType*`, `isSFP/FibrePort` bei **jedem**
Repair neu (auch bei belegten, verbundenen Ports) und nullte `insertedSFP`; der
`InteractOnClick`-Prefix gab `return false` je nach Lane-Heuristik (Fehlklassifikation).

## Fix in v2.1.0
`ConfigurePort` kehrt bei verbundenen Ports (`cableIDsOnLink != 0` oder `insertedSFP`)
sofort zurueck und entfernt nie ein eingestecktes Modul; `CableGuard` ist nur noch
Warn-/Log (immer `return true`, Toggle `CableWarnings`); leere Ports werden immer voll
normalisiert (Speed-Cap + `sfpTypeSupported/Inserted` + `isSFP/isFibrePort` zusammen).
Klick-Diagnostik vergleicht `cableIDsOnLink` vor/nach `InteractOnClick` und loggt Ablage-
Gruende, falls Kabel verweigert wird.

## Fix in v2.1.3 (1 Gbps trotz 500K)
`CollectServerLinks` sah beim Insert 0 Ports (Timing/`typeOfLink=None`). Neu:
`Server.RegisterLink` + `CableLink.Start` Postfixe konfigurieren bekannte
Varianten-Ports sofort; Kinderscan akzeptiert `typeOfLink.None`; leerer Treffer ->
`FindObjectsOfTypeAll` via `parentServer`-Pointer; Watchlist prueft freie
`connectionSpeed`-Werte alle 5 s und retryet bei 0 Ports.

## v2.2.0 (Matrix / MoreModules)
20 Varianten (100K–4M), freie Ports vorbelegt (25/40/100/200/400 Gbps), ab 40G
`sfpType=3`; Shop-Label nennt das empfohlene MoreModules-Modul. Belegte Ports
weiter nie rewrite.

## Verbleibende Arbeit / To-Verify
1. Flow "Kabel ziehen -> neu einstecken" und "Server einschalten -> Port-Rate" gegen v2.1.0.
2. "0 gbps max" nach PowerOn - war das Fremdeffekt der Repair-Schreibzugriffe oder echte
   Rate? Mit Diagnose-Log pruefen.
3. Bandbreiten-Erwartung 10/10 bzw. 25/25 (Brilyn911) separat dokumentieren (Spiel-Mechanik
   vs. Mod); ggf. README-Notiz, keine Code-Aenderung.
4. Live-Log v2.1.3+: `Ports gefunden>0`, `abweichend=0`, UI zeigt 40/100/200/400 Gbps
   bei 500K–4M-Servern (ohne Reload und nach Save/Reload).
5. MoreModules-Module (100G–400G) in QSFP+-Ports ohne Port-Hacks stecken.