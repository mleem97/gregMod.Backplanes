# ISSUE-008 - One purchase spawns multiple 500K servers, only the first is usable

- **Status:** Fix v2.1.1 (double-consume removed, FIFO spawn) - see `../BUGFIX_NOTES.md` v2.1.1
- **Priority:** Medium
- **Area:** Shop/spawning (ShopItem click -> spawn), inventory
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.1
- **Reports (Steam Workshop):**
  - *Brilyn911, 22 May (2x)* - buying one 500K server spawns three servers instead of one;
    only the first of them is usable (the two following ones can't be
    placed).

## Symptom
A single purchase of a 500K variant server spawns multiple instances, of which
only the first can be put into a rack; the duplicates may block inventory.

## Expected vs. actual
- **Expected:** One purchase -> exactly one server in inventory, placeable.
- **Actual:** Three servers (only the first usable).

## Fix in v2.1.1 (root cause found + fixed)
Causality was the **double-consume of the pending queue** (though not in the form of a
repair spawn): `ConfigureSpawnedItem` only **peeked** the pending entry (peeking,
without taking it from the queue) and `FinalizeInsertedServer` then **unconditionally** removed
one entry. When buying repeatedly at the same price point, the second purchase consumed
the third's pending entry - three 500K purchases in a row could thus overlap
(one payment, several instances configured/spawned as '500K', only the
first with correct state). Player-side it looked like "one purchase -> (seemingly) multiple
servers".

Fixed in v2.1.1:
1. `ConfigureSpawnedItem` now consumes **exactly one** pending entry per spawn
   (`ConsumePendingSpecForSpawn`, FIFO) - no more peeking.
2. `FinalizeInsertedServer` no longer removes **any** pending entry (consumption happens at
   spawn resp. via `DequeueMatchingPendingSpec` in the insertion).
3. Orphaned spawn UIDs (GameObject never shows up in `ComputerShop.spawnedItems`) expire
   after 60 s - no permanent sweep.

## Remaining work / Regression
- Against the v2.1.1 DLL: buy three 500K servers in a row and check that exactly three
  are paid/tracked and usable (cart count, queue consumption in the log).
- Update the result as confirmed/fixed in this file and in the README index.
