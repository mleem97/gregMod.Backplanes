# ISSUE-004 - Custom-Farben fuer Kabel/Racks liefern immer Default-Farbe

- **Status:** Fix in Arbeit (Checkout-Sweep v2.2.2, To-Verify) - UID-Remap + ForceApply aktiv; v2.1.0 isCustomColor-Fix siehe `../BUGFIX_NOTES.md` #6
- **Prioritaet:** Mittel
- **Bereich:** Shop/Cart (isCustomColor), `ComputerShop.shopItems`
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0
- **Berichte (Steam Workshop):**
  - *Shaun Eyebright, 20/24 May* - Custom-farbige Kabel/Racks werden mit Default-Farbe
    geliefert, obwohl der Farbpicker vorne funktioniert.
  - *Gothicdude1044, 20 May* - dasselbe; dazu: Wunsch, die Farbe der modded Server weiter
    anpassen zu koennen ("changing colors of modded server keeping default schema").
  - *Revoltec* / *DNFplays* - Bestaetigungen, gleiches Symptom.

## Symptom
Spieler waerben ein custom-farbiges Kabel/Rack ueber den Farbpicker; geliefertes Item
tragt die Default-Farbe, `isCustomColor`-Kaeufe geraten in den Mod-Pfad.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Farbpicker-Auswahl (Kolben/Bucket) kommt am gelieferten Item an.
- **Tatsaechlich:** v1.x-Kauf-/Cart-Hooks fingen den gemeinsamen Buy-Pfad ab; die
  `shopItems`-Sammlung wurde per fragiler Reflection neu aufgebaut, Custom-Color-Kaeufe
  liefen dabei in die Mod-Config.

## Bekannte Root-Cause (v1.0.x, aus `../BUGFIX_NOTES.md` #6)
Kauf-/Cart-Hooks auf dem gemeinsamen Buy-Pfad + Reflection-Rebuild der Shop-Sammlung.

## Fix in v2.1.0
`ButtonBuyShopItem`-Prefix kehrt bei `isCustomColor`-Kaeufen sofort zurueck; es gibt keine
`ShopCartItem`-Hooks mehr; `shopItems` wird um eine getypte `Il2CppReferenceArray<ShopItem>`
erweitert, Original bleibt bei Fehlern erhalten.

## Verbleibende Arbeit / To-Verify
1. Custom-Farben (Kabel/Rack via Farbpicker) gegen v2.1.0 kaufen und Lieferfarbe pruefen.
2. Gothicdude1044-Farbwunsch fuer modded Server -> ist mit v2.1.0-`ServerTint` (Familien-
   Material-Farben) adressiert (siehe ISSUE-007); Feedback einholen.
3. Regression: `ComputerShop`-Funktion unimpaired, wenn `isCustomColor`-Keller im
   Parallelkauf mit Varianten-Servern gemischt wird (Kaufrabatte/Technician-Route).
4. **Live-Trace (v2.2.2):** Nach einem Custom-Farben-Kauf die MelonLoader-Log nach
   `[Color]` durchsuchen. Erwartete Kette:
   `Buy … isCustomColor=true` → `OpenColorPicker` → `ButtonChosenColor BEFORE/AFTER`
   (picker.GetColor muss Wunschfarbe sein) → `AddNewCartItem … chosenColor=…` /
   `ShopCartItem.Initialize … hasCustomColor=True` → Checkout → `SpawnPhysicalItem`
   (uid pro Item) → `ApplyColorToSpawnedItem` / `CableSpinner.ApplyColor`.
   Luecke in der Kette = Root-Cause-Ort.
5. **Erster Trace (17:37, v2.2.2):** Cart korrekt (2 Farben), aber beide
   `ApplyColorToSpawnedItem`-Calls nutzen **uid=1**. Erste Farbe erreicht
   `CableSpinner.ApplyColor` nie; nur Magenta kommt an. → Spawn-UID-Kollision
   waehrend `SpawnAllPurchasedItems` (naechster Trace mit uid/spawnedItems-Dump).
6. **Zweiter Trace (17:44, Zwischen-DLL ohne Spawn-Dumps):** 3 Custom-Items im
   Cart (2x Rack magenta/orange, 1x Cable pink), alle `hasCustomColor=True` und
   korrekt gespeichert. Checkout feuert **nur einen** `ApplyColorToSpawnedItem`-
   Call (`uid=65 type=Rack color=magenta`); zweites Rack und Cable bekommen
   **keinen** Call; `CableSpinner.ApplyColor` nie. Game lief mit dem Build von
   17:32 (vor den Spawn-Dumps). → Vermutung: `SpawnAllPurchasedItems` bricht
   nach dem ersten Custom-Color-Item ab oder skippt den Rest. Naechster Trace
   muss Spawn-Dumps (`SpawnPhysicalItem`/`SpawnAllPurchasedItems BEGIN/END`)
   liefern — DLL `fa8f5d18` liegt bereit, braucht Game-Restart.
7. **Dritter Trace (17:58, Spawn-Dumps aktiv) — ROOT CAUSE:**
   - Cart korrekt: `itemColor=(0.744,0.000,0.822)` `hasCustomColor=True`
   - `SpawnPhysicalItem` traegt GO unter **`spawnedItems[uid=0]`** ein,
     `uniqueID` wird auf **1** erhoeht, `__result` bleibt **null**
   - `ApplyColorToSpawnedItem` wird mit **`uid=1`** aufgerufen → Lookup-Miss
   - `CableSpinner.ApplyColor` feuert **nie**; Material bleibt Default-Grau
     `RGBA(0.877,…)` vor und nach dem Call
   - **Fix:** UID-Remap im Prefix (`uid-1` / einziger Key) + Fallback
     `ForceApplyColorIfStillDefault` zieht Farbe erneut, falls Vanilla nicht greift
     (Build nach 17:58, braucht Restart).
8. **Vierter Trace (18:06, ef5c5c1d mit Remap):** Single-Item-Checkouts funktionieren
   (Remap greift, `CableSpinner.ApplyColor` feuert). **Multi-Item-Checkout** (2 Custom):
   Vanilla ruft `ApplyColorToSpawnedItem` **nur fuer das erste Cart-Item**; das zweite
   spawnt (`uid`-Resolve OK) aber bekommt **keinen** ApplyColor-Call → bleibt grau.
   Nutzer-Feedback: "Erster Kauf schief, alle darauffolgenden mit Custom Color."
   → **Checkout-Sweep** in `SpawnAllPurchasedItems` Postfix: Custom-Color-Cart-Eintraege
   (mit Cart-Index) snapshoten, Spawn-Reihenfolge tracken, bereits gefaerbte UIDs merken;
   am Ende fehlende Farben per `ForceApplyColorToUid` nachziehen (nur wenn rgbColor leer
   oder Material noch 0.877-Grau). Cart-Zeile i ↔ Spawn i (1:1). Build nach 18:20,
   braucht Game-Restart; naechster Test: Multi-Item-Checkout mit 2+ verschiedenen
   Custom-Farben.
