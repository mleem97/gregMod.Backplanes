# ISSUE-006 - Inkompatibilitaet mit "Svc Service"-Mod: Saves laden nicht

- **Status:** Open (v2.1.0 bislang nur harmlosere Hook-Flaeche) - siehe `../BUGFIX_NOTES.md` #9
- **Prioritaet:** Mittel
- **Bereich:** Kompatibilitaet, Patch-Surface
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0
- **Berichte (Steam Workshop):**
  - *szymon511* - mit "Svc Service"-Mod zusammen installiert: Spiel laedt das Save nicht
    (haengend/abstuerzend) solange beide Mods aktiv sind.
  - *BrassPeddler* - bestaetigt dieselbe Fehlfunktion (siehe auch ISSUE-002, gleiche
    Umgebung).

## Symptom
Saves starten nicht, wenn BackplaneBoostServers zusammen mit "Svc Service" aktiv ist.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Beide Mods koexistieren.
- **Tatsaechlich:** Save-Load bricht ab/haengt (v1.x).

## Bekannte Root-Cause (v1.0.x, aus `../BUGFIX_NOTES.md` #9, vermutlich)
V1.x patchte ein breites reflektives Feld (`Server.Awake/Start/OnEnable`,
`ShopItem.Awake/Start/UpdateVisualState`, `Technician.*`, Assembly-weite statische Scans),
das mit fremden Patches und den Caches anderer Mods kollidiert.

## Fix in v2.1.0
Minimale Hook-Flaeche (11 Patch-Methoden; keine `ShopItem`/`Technician`-Patches; keine
Assembly-weiten Scans); jeder Patch ist exception-proof (Mod kann den Game-Thread nie
versoegen). Koexistenz ist noch NICHT in-game verifiziert.

## Verbleibende Arbeit / To-Verify
1. Testszenario "Svc Service + gregMod.Backplanes v2.1.0": Save ohne modded Server laden,
   dann Save mit modded Servern; jeweils Startverhalten + Log pruefen.
2. Falls weiterhin blockiert: Hook-Konflikte per Patch-Report identifizieren
   (Harmony `PatchProcessor.GetOriginalInstructions`), ggf. `Server.Start`-Patch gegen
   Postfix/Dependency dynamisch aufloesen.
3. Ergebnis (ok/immer noch kaputt) auf der Workshop-Seite / im CHANGELOG vermerken.