# ISSUE-004 - Custom-Farben fuer Kabel/Racks liefern immer Default-Farbe

- **Status:** Fix v2.1.0 (To-Verify) - siehe `../BUGFIX_NOTES.md` #6
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