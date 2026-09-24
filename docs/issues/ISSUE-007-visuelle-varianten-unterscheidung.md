# ISSUE-007 - No visual distinction between variants (looks "cheaty", 125K/5K easily confused)

- **Status:** OK in v2.1.0 (toggles `ServerTint`/`ServerScale`), feedback open
- **Priority:** Medium
- **Area:** Visuals (tint/scale), shop card labels
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0
- **Reports (Steam Workshop):**
  - *MFDuskink* - "the game frequently convert 125 into 5" + no visible difference;
    125K cards look like standard.
  - *H3draut3r, 12 Aug* - stock models for both tiers look "cheaty"; wish:
    size scaling (like real servers: 500K = 40U, 125K = 15U), possibly also a
    color scheme for IOPS servers.
  - *Gothicdude1044, 20 May* - keep the colors of the modded servers on the vanilla default
    scheme (combinable with ISSUE-004).

## Symptom
The boosted servers are indistinguishable from vanilla (same 3U/7U size,
same colors) -> after reload, players can't tell whether their 125K/500K are "still there";
125K is easily confused with 5K.

## Expected vs. actual
- **Expected:** Variants clearly recognizable (size and/or color), cards named
  understandably.
- **Actual:** v1.x left servers visually identical to the base models.

## State in v2.1.0
- **Tint:** Family materials are recolored at runtime (SystemX->orange,
  RISC->violet, Mainframe->red, GPU->lime; 500K brighter); matching by base-color proximity +
  family color-word fallback; screens/lights/glass excluded. Toggle `ServerTint` (on).
- **Scale:** absolute Y scaling 4/3 (small) / 8/7 (large) for a 4U/8U look. **Visual
  only** - rack-slot occupancy unchanged, neighbors may visually overlap.
  Toggle `ServerScale` (on).
- **100K tier:** small variants are now 100K IOPS (internal speed 1.0); IDs
  `greg_backplanes_*_100k`; old `125k` markers (all legacy prefixes) resolve to 100K
  and are normalized on save. Prices/XP unchanged.
- **Cards:** Shop cards carry labels "+ 1-lane fiber" / "+ 4-lane fiber" with
  different prices (since v2.0.0).

## Remaining work / To-Verify
1. Check the scale effect in densely packed racks (visually overlaps with neighbors) -
  possibly a docs warning on the Workshop page.
2. Collect player feedback on whether tint+scale eliminate the 125K/5K confusion.
3. "40U/15U" size wish (H3draut3r) technically not feasible (rack slots aren't),
   so document it as a deliberate sizing decision.
