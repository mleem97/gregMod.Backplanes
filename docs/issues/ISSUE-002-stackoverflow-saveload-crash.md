# ISSUE-002 - Stack Overflow (0xC00000FD) beim Laden von Saves mit modded Servern

- **Status:** Fix v2.1.0 (To-Verify) - siehe `../BUGFIX_NOTES.md` #3
- **Prioritaet:** Hoch
- **Bereich:** Save-Repair, Lifecycle-Patches (Server.Awake/Start/OnEnable)
- **Mod:** BackplaneBoostServers v1.0.1 -> gregMod.Backplanes v2.1.0
- **Berichte (Steam Workshop):**
  - *BrassPeddler, 4 Jul* - Absturz 0xC00000FD (Stack Overflow) beim Laden eines Saves
    mit modded Servern, reproduzierbar. Zeitpunkt ca. 2 s nach "Scene loaded: BaseScene".
    Umgebung: MelonLoader v0.7.2 + FixCoreModule, Unity 6000.4.12, Game-Build July 2026,
    Mod v1.0.1.

## Symptom
Beim Laden eines Saves, in dem bereits Modded Server persistiert sind, crasht der Prozess
mit `0xC00000FD` (Stack Overflow), kurz nachdem die Scene geladen ist.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Save laedt; persistierte Server werden repariert, sobald sie aktiv sind.
- **Tatsaechlich:** Unendlicher Rekursionspfad in den Server-Lifecycle-Postfixes.

## Bekannte Root-Cause (v1.0.1, aus `../BUGFIX_NOTES.md` #3)
Der `_serversBeingRepaired`-Guard verglich per **Reference Equality ueber geboxte
Il2Cpp-Wrapper** - jeder Feldzugriff boxt einen neuen Wrapper, `Contains()` verfehlte also
immer, und die `Awake/Start/OnEnable`-Postfixes rekursierten in ihren eigenen
Konfigurationspfad.

## Fix in v2.1.0
`RepairGuard` schluesselt nach **Native-Object-Points** (stabil pro Unity-Objekt) plus
globalem Depth-Cap (8) als Circuit Breaker; Lifecycle-Repair laeuft zusaetzlich nur
innerhalb des Repair-Fensters.

## Verbleibende Arbeit / To-Verify
1. Save mit modded Servern aus v1.x-Zeit laden und Crash-Freiheit gegen v2.1.0 pruefen.
2. Regression: mehrere Saves / grosse Racks / viele Kunden (`ServerApplyLoad`) parallel
   zum Sweep testen (Rekursion alternativ ueber `Server.Start` + 1/sec Sweep).
3. Meldung BrassPeddler auch mit "Svc Service"-Mod (siehe ISSUE-006) abgrasen.