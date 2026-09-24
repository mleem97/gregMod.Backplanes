# ISSUE-005 - Shop purchase: first click doesn't count / duplicate items on single payment

- **Status:** Fix v2.1.1 (spawning FIFO) - see `../BUGFIX_NOTES.md` #5/#7 + v2.1.1 section
- **Priority:** Medium
- **Area:** Shop/cart resolve (FindByShopValues), purchase->spawn consumption
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.1
- **Reports (Steam Workshop):**
  - *Shaun Eyebright, 24 May* - when buying: the first click doesn't register, the player
    clicks several times, then gets more products than paid for (paid once, duplicate/multiple
    in the cart / delivered).
  - *Edmix, 17 May* - lag when opening Computer/Shop (separate symptom; root cause
    performance - see below).

## Symptom
Click on "Buy" in the shop: the first actuation is swallowed (confirmations in the mod path),
follow-up clicks cause duplicate/multiple items to be delivered although checkout was
pressed only once; cart entries duplicate.

## Expected vs. actual
- **Expected:** One click = one item = one charge; no swallowing.
- **Actual:** First click is lost, then multiple delivery on single payment.

## Known root cause (v1.0.x, from `../BUGFIX_NOTES.md` #7)
`FindByPurchase`/`ResolveSpecFromCartItem` fell back to **price-only** matching
(`BaseItemId + Price`), so vanilla items with the same price were treated as variants
and the pending queue was worked off twice.

## Fix in v2.1.0
`FindByShopValues` is **name-only** - no price fallback, no phantom matches. Additionally
in v2.1.0: clicks are routed manually (no EventSystem), double-fire guard (500 ms)
in the trainer-panel pattern; nothing touched in the shop itself, only resolve corrected.

## Fix in v2.1.1 (code verified against the live assembly)
Second double-delivery path removed: `ConfigureSpawnedItem` only **peeked** the pending entry
(without consuming), `FinalizeInsertedServer` then unconditionally removed one
entry - double-consume that, at identical price points (20000/100000), wrongly consumed the **next**
purchase. Now exactly **one** pending entry per spawn is consumed FIFO
(`ConsumePendingSpecForSpawn`), `FinalizeInsertedServer` no longer removes anything from the queue.
`SpawnedItem`s whose GameObject never shows up expire after 60 s (no permanent sweep).

## Remaining work / To-Verify
1. Buying variant servers together with vanilla items at the same price -> no
   double delivery, correct charge (confirm against the v2.1.1 DLL in game).
2. "First click swallowed": verify whether it was purely the former resolve bug
   or UI focus/click routing in the game itself (then not a mod bug).
3. Edmix lag: recorded as fixed in `../BUGFIX_NOTES.md` #5 + v2.1.1 (sweep expiry) -
   measure in large saves against v2.1.1.
