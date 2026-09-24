# VARIANT_EXTENDING — adding your own server variants

All variants are data-driven in `src/ServerVariantSpec.cs` (`ServerVariantSpec.All`,
currently 20 entries via `Make(...)`). A new variant is one entry — no new
patch needed.

## Tiers (v2.2.0)

Per family: **100K/25G · 500K/40G · 1M/100G · 2M/200G · 4M/400G** — more Gbps = pricier.
From 40G `SfpType = 3` (vanilla QSFP+), so **gregMod.MoreModules** modules (QSFP28/56/DD)
fit without port hacks. `RecommendedModule` ends up in the shop label.

## Example: 1M-IOPS variant on a Mainframe 7U base (`Make(...)`)

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

`SizeKey` and `VariantId` derive from IOPS (`100k`/`500k`/`1m`/`2m`/`4m`).

## Rules

1. **`BaseDisplayName` / `BaseAssetName`** must match the vanilla shop item exactly
   (family + 3U/7U). The mod finds the base button via `Contains` match.
2. **`Iops` → internal speed = Iops / 100000.** 100000 → 1.0, 500000 → 5.0.
   The 3U base runs at 0.05 (5K), the 7U base at 0.12 (12K) — set explicitly as `BaseSpeed`
   (small→3U base, large→7U base).
3. **`NetworkSpeedGbps` → internal /5** (25G→5, 40G→8). Pure port ceiling for
   empty ports; occupied ports are never touched.
4. **`VariantId` is the persistence identity** (sidecar `server-variants.tsv`).
   Once used in saves → never rename, otherwise orphaned markers (they get
   logged but are no longer assigned). Legacy prefixes see `LegacyPrefixes`.
5. **`FiberLaneCount` / `RecommendedModule`** only control the shop label and the
   cable/module recommendation — nothing is blocked.
6. **MoreModules:** from 40G keep `SfpType = 3` (vanilla QSFP+); module Gbps goes
   into `RecommendedModule` (e.g. `QSFP56 200G`). IDs 9001–9020 stay free of
   MoreModules (1000–3999).
7. **Avoid excess:** extremely high IOPS values (> a few million) were not
   tested; the game balances customer demand around vanilla values.
8. **Visuals:** pick `TintColor` from the family scheme (SystemX=orange, RISC=violet,
   Mainframe=red, GPU=lime; higher tier brighter each time). `FamilyBaseColor` = vanilla body color
   for material matching. `ScaleY` = 4/3 (3U) resp. 8/7 (7U) — visual only.

## Test checklist (in game)

- [ ] All 20 shop entries: name, price, XP, recommended module
- [ ] Buy → rack install → IOPS + port speed correct, color + height visible
- [ ] Empty ports: 40/100/200/400 Gbps instead of 1 Gbps; occupied ports untouched
- [ ] MoreModules modules (100G–400G) plug into QSFP+ ports without port hacks
- [ ] Save → quit to desktop → reload → values + visuals persist (no rebuy)
- [ ] Unplug/replug cables works
- [ ] Technician repair (EOL) keeps the variant
- [ ] Custom-colored cables/racks keep working
