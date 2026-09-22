# VARIANT_EXTENDING — eigene Server-Varianten hinzufügen

Alle Varianten sind datengetrieben in `src/ServerVariantSpec.cs` (`ServerVariantSpec.All`).
Eine neue Variante ist ein Eintrag — kein neuer Patch nötig.

## Beispiel: 1M-IOPS Variante auf Mainframe-7U-Basis

```csharp
new ServerVariantSpec
{
    FamilyKey = "mainframe",
    BaseDisplayName = "Mainframe 7U 12000 IOPs",      // muss den Shop-Namen des Basis-Servers enthalten
    BaseAssetName = "ShopItemSO_Server_Purple2",       // Asset-Name des Basis-ShopItems
    VariantId = "greg_backplanes_mainframe_1m",        // eindeutig, stabil — NIE nachträglich ändern
    VariantDisplayName = "Mainframe 1M IOPS",          // Shop-/Cart-Anzeige
    Iops = 1000000,                                    // 1M / 100000 = 10.0 interne Speed
    Price = 250000,
    XpToUnlock = 50000,
    ConnectorHint = "QSFP+",
    NetworkSpeedGbps = 40f,                            // intern /5 → 8
    SfpType = 3,                                       // 2 = SFP28, 3 = QSFP+
    FiberLaneCount = 4,                                // 1 oder 4 (nur Anzeige/Empfehlung)
    BaseSpeed = 0.12f,                                 // 0.05f für 3U-Basis, 0.12f für 7U-Basis
    TintColor = new Color(1f, 0.2f, 0.5f, 1f),         // Wunsch-Farbe (Orange/Violett/Rot/Lime-Schema beachten)
    FamilyBaseColor = new Color(0.5f, 0f, 1f, 1f),     // Vanilla-Körperfarbe für Material-Matching
    ScaleY = 8f / 7f,                                  // 4f/3f für 3U-Basis (visuell only)
},
```

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
5. **`FiberLaneCount`** steuert nur Shop-Label (`1-lane fiber` / `4-lane fiber`) und
   die Kabel-Empfehlung — geblockt wird nichts.
6. **Größenwahn vermeiden:** extrem hohe IOPS-Werte (> ein paar Millionen) wurden nicht
   getestet; das Spiel balanciert Kundenbedarfe um Vanilla-Werte.
7. **Visuals:** `TintColor` aus dem Familien-Schema wählen (SystemX=Orange, RISC=Violett,
   Mainframe=Rot, GPU=Lime; 500K jeweils heller). `FamilyBaseColor` = Vanilla-Körperfarbe
   für das Material-Matching. `ScaleY` = 4/3 (3U) bzw. 8/7 (7U) — visuell only.

## Test-Checkliste (im Spiel)

- [ ] Variante erscheint im Shop mit korrektem Namen/Preis
- [ ] Kauf → Rack-Einbau → IOPS + Port-Speed korrekt, Farbe + Höhe sichtbar
- [ ] Save → Quit to Desktop → Reload → Werte + Visuals bleiben (ohne Neu-Kauf)
- [ ] Kabel abziehen/wieder anstecken funktioniert
- [ ] Technician-Reparatur (EOL) behält die Variante
- [ ] Custom-farbige Kabel/Racks funktionieren weiterhin
