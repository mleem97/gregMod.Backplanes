# VARIANT_EXTENDING — eigene Server-Varianten hinzufügen

Alle Varianten sind datengetrieben in `src/ServerVariantSpec.cs` (`ServerVariantSpec.All`,
aktuell 20 Einträge über `Make(...)`). Eine neue Variante ist ein Eintrag — kein neuer
Patch nötig.

## Tiers (v2.2.0)

Pro Familie: **100K/25G · 500K/40G · 1M/100G · 2M/200G · 4M/400G** — mehr Gbps = teurer.
Ab 40G `SfpType = 3` (Vanilla-QSFP+), damit **gregMod.MoreModules**-Module (QSFP28/56/DD)
ohne Port-Hacks passen. `RecommendedModule` landet im Shop-Label.

## Beispiel: 1M-IOPS Variante auf Mainframe-7U-Basis (`Make(...)`)

```csharp
Make(
    "mainframe",
    "Mainframe 7U 12000 IOPs",           // BaseDisplayName 3U (Contains-Match)
    "ShopItemSO_Server_Purple2",          // BaseAssetName 3U
    "Mainframe 7U 12000 IOPs",            // BaseDisplayName 7U (falls small=false)
    "ShopItemSO_Server_Purple2",          // BaseAssetName 7U
    "Mainframe",                          // VariantId-Prefix → greg_backplanes_mainframe_<sizekey>
    1000000,                              // IOPS (intern /100000 → 10.0)
    250000,                               // Price (mehr Gbps ⇒ teurer)
    50000,                                // XpToUnlock
    9015,                                 // VariantItemId (9001-9020, eindeutig)
    "QSFP28",                             // ConnectorHint (Shop-Label)
    100f,                                 // NetworkSpeedGbps (intern /5 → 20)
    3,                                    // SfpType (3 = Vanilla-QSFP+, MoreModules)
    4,                                    // FiberLaneCount
    "QSFP28 100G",                        // RecommendedModule (Label)
    new Color(0.2f, 0.6f, 1f, 1f),        // TintColor (Familien-Schema)
    new Color(0.5f, 0f, 1f, 1f),          // FamilyBaseColor (Vanilla-Matching)
    small: false                          // true = 3U-Basis, false = 7U-Basis
),
```

`SizeKey` und `VariantId` leiten sich aus IOPS ab (`100k`/`500k`/`1m`/`2m`/`4m`).

## Regeln

1. **`BaseDisplayName` / `BaseAssetName`** müssen exakt zum Vanilla-ShopItem passen
   (Familie + 3U/7U). Die Mod findet den Basis-Button per `Contains`-Match.
2. **`Iops` → interne Speed = Iops / 100000.** 100000 → 1.0, 500000 → 5.0.
   Die 3U-Basis läuft mit 0.05 (5K), die 7U-Basis mit 0.12 (12K) — als `BaseSpeed`
   explizit setzen (klein→3U-Basis, groß→7U-Basis).
3. **`NetworkSpeedGbps` → intern /5** (25G→5, 40G→8). Reine Port-Obergrenze für
   leere Ports; belegte Ports werden nie angefasst.
4. **`VariantId` ist die Persistenz-Identität** (Sidecar `server-variants.tsv`).
   Einmal in Saves verwendet → nie umbenennen, sonst verwaiste Marker (sie werden
   geloggt, aber nicht mehr zugeordnet). Legacy-Präfixe siehe `LegacyPrefixes`.
5. **`FiberLaneCount` / `RecommendedModule`** steuern nur Shop-Label und die
   Kabel-/Modul-Empfehlung — geblockt wird nichts.
6. **MoreModules:** ab 40G `SfpType = 3` (Vanilla-QSFP+) belassen; Modul-Gbps landet
   in `RecommendedModule` (z. B. `QSFP56 200G`). IDs 9001–9020 bleiben frei von
   MoreModules (1000–3999).
7. **Größenwahn vermeiden:** extrem hohe IOPS-Werte (> ein paar Millionen) wurden nicht
   getestet; das Spiel balanciert Kundenbedarfe um Vanilla-Werte.
8. **Visuals:** `TintColor` aus dem Familien-Schema wählen (SystemX=Orange, RISC=Violett,
   Mainframe=Rot, GPU=Lime; höhere Tier jeweils kräftiger). `FamilyBaseColor` = Vanilla-Körperfarbe
   für das Material-Matching. `ScaleY` = 4/3 (3U) bzw. 8/7 (7U) — visuell only.

## Test-Checkliste (im Spiel)

- [ ] Alle 20 Shop-Einträge: Name, Preis, XP, empfohlenes Modul
- [ ] Kauf → Rack-Einbau → IOPS + Port-Speed korrekt, Farbe + Höhe sichtbar
- [ ] Leere Ports: 40/100/200/400 Gbps statt 1 Gbps; belegte Ports unangetastet
- [ ] MoreModules-Module (100G–400G) stecken in QSFP+-Ports ohne Port-Hacks
- [ ] Save → Quit to Desktop → Reload → Werte + Visuals bleiben (ohne Neu-Kauf)
- [ ] Kabel abziehen/wieder anstecken funktioniert
- [ ] Technician-Reparatur (EOL) behält die Variante
- [ ] Custom-farbige Kabel/Racks funktionieren weiterhin
