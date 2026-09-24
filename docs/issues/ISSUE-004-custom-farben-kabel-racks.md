# ISSUE-004 - Custom colors for cables/racks always deliver the default color

- **Status:** Fix in progress (checkout sweep v2.2.2, To-Verify) - UID remap + ForceApply active; v2.1.0 isCustomColor fix see `../BUGFIX_NOTES.md` #6
- **Priority:** Medium
- **Area:** Shop/Cart (isCustomColor), `ComputerShop.shopItems`
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0
- **Reports (Steam Workshop):**
  - *Shaun Eyebright, 20/24 May* - custom-colored cables/racks are delivered with the default color,
    even though the color picker up front works.
  - *Gothicdude1044, 20 May* - same; plus: wish to keep being able to
    adjust the color of the modded servers ("changing colors of modded server keeping default schema").
  - *Revoltec* / *DNFplays* - confirmations, same symptom.

## Symptom
Player acquires a custom-colored cable/rack via the color picker; the delivered item
carries the default color, `isCustomColor` purchases end up in the mod path.

## Expected vs. actual
- **Expected:** Color-picker selection (piston/bucket) arrives on the delivered item.
- **Actual:** v1.x buy/cart hooks intercepted the shared buy path; the
  `shopItems` collection was rebuilt via fragile reflection, and custom-color purchases
  ended up in the mod config along the way.

## Known root cause (v1.0.x, from `../BUGFIX_NOTES.md` #6)
Buy/cart hooks on the shared buy path + reflection rebuild of the shop collection.

## Fix in v2.1.0
`ButtonBuyShopItem` prefix returns immediately for `isCustomColor` purchases; there are no
`ShopCartItem` hooks anymore; `shopItems` is extended with a typed `Il2CppReferenceArray<ShopItem>`,
the original stays intact on errors.

## Remaining work / To-Verify
1. Buy custom colors (cable/rack via color picker) against v2.1.0 and check the delivered color.
2. Gothicdude1044 color wish for modded servers -> addressed with v2.1.0 `ServerTint` (family
   material colors) (see ISSUE-007); collect feedback.
3. Regression: `ComputerShop` function unimpaired when `isCustomColor` cases are
   mixed into a parallel purchase with variant servers (purchase discounts/technician route).
4. **Live trace (v2.2.2):** After a custom-color purchase, search the MelonLoader log for
   `[Color]`. Expected chain:
   `Buy … isCustomColor=true` → `OpenColorPicker` → `ButtonChosenColor BEFORE/AFTER`
   (picker.GetColor must be the desired color) → `AddNewCartItem … chosenColor=…` /
   `ShopCartItem.Initialize … hasCustomColor=True` → Checkout → `SpawnPhysicalItem`
   (uid per item) → `ApplyColorToSpawnedItem` / `CableSpinner.ApplyColor`.
   Gap in the chain = root-cause location.
5. **First trace (17:37, v2.2.2):** Cart correct (2 colors), but both
   `ApplyColorToSpawnedItem` calls use **uid=1**. First color never reaches
   `CableSpinner.ApplyColor`; only magenta arrives. → Spawn-UID collision
   during `SpawnAllPurchasedItems` (next trace with uid/spawnedItems dump).
6. **Second trace (17:44, interim DLL without spawn dumps):** 3 custom items in the
   cart (2x rack magenta/orange, 1x cable pink), all `hasCustomColor=True` and
   stored correctly. Checkout fires **only one** `ApplyColorToSpawnedItem`
   call (`uid=65 type=Rack color=magenta`); second rack and cable get
   **no** call; `CableSpinner.ApplyColor` never. Game ran with the build from
   17:32 (before the spawn dumps). → Assumption: `SpawnAllPurchasedItems` stops
   after the first custom-color item or skips the rest. Next trace
   must deliver spawn dumps (`SpawnPhysicalItem`/`SpawnAllPurchasedItems BEGIN/END`)
   — DLL `fa8f5d18` is ready, needs a game restart.
7. **Third trace (17:58, spawn dumps active) — ROOT CAUSE:**
   - Cart correct: `itemColor=(0.744,0.000,0.822)` `hasCustomColor=True`
   - `SpawnPhysicalItem` registers the GO under **`spawnedItems[uid=0]`**,
     `uniqueID` is raised to **1**, `__result` stays **null**
   - `ApplyColorToSpawnedItem` is called with **`uid=1`** → lookup miss
   - `CableSpinner.ApplyColor` **never** fires; material stays default gray
     `RGBA(0.877,…)` before and after the call
   - **Fix:** UID remap in the prefix (`uid-1` / single key) + fallback
     `ForceApplyColorIfStillDefault` re-applies the color if vanilla doesn't take effect
     (build after 17:58, needs a restart).
8. **Fourth trace (18:06, ef5c5c1d with remap):** Single-item checkouts work
   (remap takes effect, `CableSpinner.ApplyColor` fires). **Multi-item checkout** (2 custom):
   vanilla calls `ApplyColorToSpawnedItem` **only for the first cart item**; the second
   spawns (`uid` resolve OK) but gets **no** ApplyColor call → stays gray.
   User feedback: "Erster Kauf schief, alle darauffolgenden mit Custom Color."
   → **Checkout sweep** in `SpawnAllPurchasedItems` postfix: snapshot custom-color cart entries
   (with cart index), track spawn order, remember already-colored UIDs;
   at the end, backfill missing colors via `ForceApplyColorToUid` (only when rgbColor is empty
   or the material is still 0.877 gray). Cart row i ↔ spawn i (1:1). Build after 18:20,
   needs a game restart; next test: multi-item checkout with 2+ different
   custom colors.
