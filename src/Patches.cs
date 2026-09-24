using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace GregMod.Backplanes
{
    /// <summary>
    /// Minimal Harmony surface. Every patch is defensive (try/catch + null checks)
    /// and does the smallest possible work; heavy lifting lives in CatalogInjector.
    ///
    /// Deliberately NOT patched (v1.x hooks removed):
    /// <list type="bullet">
    /// <item>ShopItem.Awake — no name rewrite needed; Start/UpdateVisualState cover render.</item>
    /// <item>ShopCartItem.* — v1.x patched a method that does not exist in the
    ///   current build (silently dead). Spawns are handled via the live
    ///   ComputerShop.SpawnPhysicalItem postfix.</item>
    /// <item>Technician.* — technician replacements are covered by the typed
    ///   Server.RepairDevice postfix; fewer hooks = fewer cross-mod conflicts.</item>
    /// </list>
    /// ShopItem.Start/UpdateVisualState/OnLoad ARE patched: the game looks up
    /// names by ItemID and shows "Unknown" for 9001–9020 unless we rewrite after.
    ///
    /// CableLink speed methods ARE patched (postfix only): our ports are
    /// registered once in PortSpeedMemory at ConfigurePort; these hooks
    /// re-assert the target speed on drift — event-driven, no polling.
    /// Vanilla ports are never registered, so the hooks are no-ops for them.
    /// </summary>
    internal static class Patches
    {
        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                harmony.PatchAll(typeof(Patches));
                Log.Info("Harmony patches installed.");
            }
            catch (Exception ex)
            {
                Log.Error("Harmony patch installation failed.", ex);
            }
        }

        // ------------------------------------------------------- shop triggers

        /// <summary>
        /// Game renders card names via itemID lookup (base names or
        /// "Unknown" for our IDs). After each vanilla refresh re-apply
        /// variant texts (display only, no logic).
        /// </summary>
        [HarmonyPatch(typeof(ShopItem), nameof(ShopItem.Start))]
        [HarmonyPostfix]
        private static void ShopItemStartPostfix(ShopItem __instance)
        {
            try
            {
                if (__instance == null) return;
                ApplyVariantCardTexts(__instance);
            }
            catch { /* display only */ }
        }

        [HarmonyPatch(typeof(ShopItem), nameof(ShopItem.UpdateVisualState))]
        [HarmonyPostfix]
        private static void ShopItemVisualPostfix(ShopItem __instance)
        {
            try
            {
                if (__instance == null) return;
                ApplyVariantCardTexts(__instance);
            }
            catch { /* display only */ }
        }

        [HarmonyPatch(typeof(ShopItem), nameof(ShopItem.OnLoad))]
        [HarmonyPostfix]
        private static void ShopItemLoadPostfix(ShopItem __instance)
        {
            try
            {
                if (__instance == null) return;
                ApplyVariantCardTexts(__instance);
            }
            catch { /* display only */ }
        }

        private static void ApplyVariantCardTexts(ShopItem item)
        {
            int id = 0;
            try { id = item.shopItemSO != null ? item.shopItemSO.itemID : 0; } catch { return; }
            var spec = ServerVariantSpec.FindByVariantItemId(id);
            if (spec == null) return;
            string label = $"{spec.VariantDisplayName} ({spec.RecommendedCable})";
            try { item.itemDisplayName = label; } catch { }
            try { if (item.txtName != null) item.txtName.text = label; } catch { }
            try { if (item.txtPrice != null) item.txtPrice.text = $"{spec.Price} $"; } catch { }
            try { if (item.txtXpToUnlock != null) item.txtXpToUnlock.text = $"Unlock for: {spec.XpToUnlock} xp"; } catch { }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.InteractOnClick))]
        [HarmonyPostfix]
        private static void ComputerShopInteractPostfix(ComputerShop __instance)
        {
            try
            {
                if (__instance == null) return;
                BackplanesMod.Injector.TryRegisterAll("ComputerShop.InteractOnClick", __instance);
            }
            catch (Exception ex)
            {
                Log.Error("ComputerShopInteractPostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonShopScreen))]
        [HarmonyPostfix]
        private static void ComputerShopScreenPostfix(ComputerShop __instance)
        {
            try
            {
                if (__instance == null) return;
                BackplanesMod.Injector.TryRegisterAll("ComputerShop.ButtonShopScreen", __instance);
            }
            catch (Exception ex)
            {
                Log.Error("ComputerShopScreenPostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonClear))]
        [HarmonyPostfix]
        private static void ComputerShopClearPostfix(ComputerShop __instance)
        {
            try
            {
                if (__instance == null) return;
                // Note: NO ClearPendingPurchases here anymore. Purchased items
                // keep existing physically (player carries them / cart), even
                // when shop is closed. Wipe would destroy buy->insert correlation.
                // Expired entries cleaned by expiry (10min/60s).
                Log.Info("ComputerShop.ButtonClear seen (pending kept).");
            }
            catch (Exception ex)
            {
                Log.Error("ComputerShopClearPostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonCancel))]
        [HarmonyPostfix]
        private static void ComputerShopCancelPostfix(ComputerShop __instance)
        {
            try
            {
                if (__instance == null) return;
                // As above: closing shop must NOT delete pending,
                // else nothing known at later insert.
            }
            catch (Exception ex)
            {
                Log.Error("ComputerShopCancelPostfix failed.", ex);
            }
        }

        // ------------------------------------------------------- purchases

        /// <summary>
        /// Variants have own item IDs (9001-9020). Game knows only
        /// base prefabs: here the variant ID is transparently mapped back to the
        /// family base ID (ref param), so original code delivers
        /// the correct base prefab. Cart/purchase keep the
        /// variant ID (own identity end-to-end).
        /// </summary>
        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.GetPrefabForItem))]
        [HarmonyPrefix]
        private static bool GetPrefabForItemPrefix(ComputerShop __instance, ref int itemID,
            PlayerManager.ObjectInHand itemType)
        {
            try
            {
                if (__instance == null) return true;
                // Variant IDs 9001+: keep path deliberately visible if
                // base-ID map incomplete (spawn would silently
                // fail - GetPrefabForItem then returns null).
                bool isVariant = itemID >= 9001 && itemID <= 9021;
                if (BackplanesMod.Injector.TryGetBaseId(itemID, out int baseId))
                {
                    if (isVariant && ModConfig.VerboseLogging)
                        Log.Info($"GetPrefabForItem: itemID={itemID} -> baseId={baseId} type={itemType}.");
                    itemID = baseId;
                }
                else if (isVariant)
                {
                    Log.Warning($"GetPrefabForItem: no base ID for itemID={itemID} " +
                        "(registration/reset?) - original never sees the ID, spawn may return null.");
                }
                return true; // always run original
            }
            catch (Exception ex)
            {
                Log.Error("GetPrefabForItemPrefix failed.", ex);
                return true;
            }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonBuyShopItem))]
        [HarmonyPrefix]
        private static bool ComputerShopBuyPrefix(ComputerShop __instance, int itemID, int price,
            PlayerManager.ObjectInHand itemType, ref string displayName, bool isCustomColor)
        {
            try
            {
                if (__instance == null) return true;
                // Custom-color purchases (colored cables/racks) are never ours — hands off.
                if (isCustomColor)
                {
                    ColorTrace("Buy", $"id={itemID} price={price} type={itemType} name='{displayName}' isCustomColor=true", __instance);
                    return true;
                }
                Log.Info($"Buy: id={itemID} price={price} type={itemType} name='{displayName}'.");
                BackplanesMod.Injector.RewritePurchaseDisplayName(itemID, price, itemType, ref displayName);
                BackplanesMod.Injector.TrackPurchase("ComputerShop.ButtonBuyShopItem", itemID, price, itemType, displayName);
                return true; // never swallow the original purchase
            }
            catch (Exception ex)
            {
                Log.Error("ComputerShopBuyPrefix failed.", ex);
                return true;
            }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.SpawnPhysicalItem))]
        [HarmonyPostfix]
        private static void SpawnPhysicalPostfix(ComputerShop __instance, GameObject prefab, int price,            PlayerManager.ObjectInHand itemType, Il2CppSystem.Nullable<int> __result)
        {
            try
            {
                if (__instance == null) return;
                string prefabName = "?";
                try { prefabName = prefab != null ? prefab.name : "null"; } catch { }
                int uid = int.MinValue;
                bool hasValue = false;
                try { hasValue = __result.HasValue; if (hasValue) uid = __result.Value; } catch { }
                Log.Info($"[Color] SpawnPhysicalItem: prefab='{prefabName}' price={price} type={itemType} uid={(hasValue ? uid.ToString() : "null")} uniqueID={__instance.uniqueID}");
                DumpSpawnedItems(__instance, "after SpawnPhysicalItem");

                // Vanilla return unreliable (null or wrong key).
                // New entry typically sits at uniqueID-1.
                int resolved = -1;
                try
                {
                    var dict = __instance.spawnedItems;
                    int expected = __instance.uniqueID - 1;
                    if (dict != null && expected >= 0 && dict.ContainsKey(expected))
                        resolved = expected;
                    else if (hasValue && dict != null && dict.ContainsKey(uid))
                        resolved = uid;
                    else if (dict != null && dict.Count > 0)
                    {
                        foreach (var k in dict.Keys) { resolved = k; break; }
                    }
                }
                catch (Exception ex) { Log.Warning("Spawn uid resolve failed: " + ex.Message); }

                if (resolved < 0)
                {
                    if (!hasValue) return;
                    resolved = uid;
                }
                else if (!hasValue || resolved != uid)
                {
                    Log.Info($"[Color] SpawnPhysicalItem: uid resolved {uid} → {resolved} (uniqueID={__instance.uniqueID})");
                }

                if (_checkoutActive) _checkoutSpawnUids.Add(resolved);
                BackplanesMod.Injector.ConfigureSpawnedItem("ComputerShop.SpawnPhysicalItem", __instance, price, itemType, resolved);
            }
            catch (Exception ex)
            {
                Log.Error("SpawnPhysicalPostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(ComputerShop), "SpawnAllPurchasedItems")]
        [HarmonyPrefix]
        private static void SpawnAllPrefix(ComputerShop __instance)
        {
            try
            {
                if (__instance == null) return;
                Log.Info($"[Color] SpawnAllPurchasedItems: BEGIN uniqueID={__instance.uniqueID}");
                BackplanesMod.Injector.BeginCheckoutSnapshot(__instance);
                DumpColorState(__instance);
                DumpSpawnedItems(__instance, "before spawn-all");

                _checkoutActive = true;
                _checkoutColors.Clear();
                _checkoutTypes.Clear();
                _checkoutCartIndexes.Clear();
                _checkoutQuantities.Clear();
                _checkoutUnitOffsets.Clear();
                _checkoutSpawnUids.Clear();
                _checkoutColoredUids.Clear();
                try
                {
                    var cart = __instance.cartUIItems;
                    if (cart != null)
                    {
                        int unitsBefore = 0;
                        for (int i = 0; i < cart.Count; i++)
                        {
                            var it = cart[i];
                            int qty = 1;
                            try { if (it != null) qty = Math.Max(1, it.Quantity); } catch { }
                            if (it != null && it.hasCustomColor)
                            {
                                _checkoutColors.Add(it.itemColor);
                                _checkoutTypes.Add(it.itemType);
                                _checkoutCartIndexes.Add(i);
                                _checkoutQuantities.Add(qty);
                                _checkoutUnitOffsets.Add(unitsBefore);
                            }
                            unitsBefore += qty;
                        }
                    }
                }
                catch (Exception ex) { Log.Warning("checkout color snapshot failed: " + ex.Message); }
                Log.Info($"[Color] checkout snapshot: {_checkoutColors.Count} custom-color cart entr(ies) " +
                         $"indexes=[{string.Join(",", _checkoutCartIndexes)}] " +
                         $"qty=[{string.Join(",", _checkoutQuantities)}] " +
                         $"offsets=[{string.Join(",", _checkoutUnitOffsets)}]");
            }
            catch (Exception ex) { Log.Warning("SpawnAllPrefix failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ComputerShop), "SpawnAllPurchasedItems")]
        [HarmonyPostfix]
        private static void SpawnAllPostfix(ComputerShop __instance)
        {
            try
            {
                if (__instance == null) return;
                Log.Info($"[Color] SpawnAllPurchasedItems: END uniqueID={__instance.uniqueID}");
                DumpSpawnedItems(__instance, "after spawn-all");

                // Vanilla often applies only the first custom-color item (or
                // SpawnPhysicalItem returns broken UIDs). Catch up: map all
                // custom-color cart entries to spawn order.
                // Line with quantity Q occupies Q consecutive spawns from
                // its unit offset (cumulated quantities of all lines before).
                int spawned = _checkoutSpawnUids.Count;
                int colored = 0, forced = 0;
                for (int i = 0; i < _checkoutColors.Count; i++)
                {
                    int qty = i < _checkoutQuantities.Count ? _checkoutQuantities[i] : 1;
                    int offset = i < _checkoutUnitOffsets.Count ? _checkoutUnitOffsets[i] : (i < _checkoutCartIndexes.Count ? _checkoutCartIndexes[i] : i);
                    for (int j = 0; j < qty; j++)
                    {
                        int spawnIdx = offset + j;
                        if (spawnIdx >= spawned)
                        {
                            Log.Warning($"[Color] checkout sweep: custom[{i}] unit {j} has no spawn (offset={offset}, spawned={spawned})");
                            break;
                        }
                        int uid = _checkoutSpawnUids[spawnIdx];
                        if (_checkoutColoredUids.Contains(uid))
                        {
                            colored++;
                            continue;
                        }
                        Log.Info($"[Color] checkout sweep: custom[{i}] unit {j} spawnIdx={spawnIdx} uid={uid} not vanilla-colored → force");
                        ForceApplyColorToUid(__instance, uid, _checkoutColors[i], _checkoutTypes[i]);
                        forced++;
                    }
                }
                Log.Info($"[Color] checkout sweep done: {colored} already colored, {forced} forced, spawns={spawned}");
                BackplanesMod.Injector.VerifyCheckout("ComputerShop.SpawnAllPurchasedItems");
            }
            catch (Exception ex) { Log.Warning("SpawnAllPostfix failed: " + ex.Message); }
            finally
            {
                _checkoutActive = false;
            }
        }

        private static void DumpSpawnedItems(ComputerShop shop, string when)
        {
            try
            {
                var dict = shop.spawnedItems;
                if (dict == null) { Log.Info($"  spawnedItems[{when}]: null"); return; }
                Log.Info($"  spawnedItems[{when}]: count={dict.Count} keys=[{string.Join(",", dict.Keys)}] uniqueID={shop.uniqueID}");
                foreach (var kv in dict)
                {
                    var go = kv.Value;
                    string n = "?";
                    try { n = go != null ? go.name : "null"; } catch { }
                    string spin = "";
                    try
                    {
                        if (go != null)
                        {
                            var cs = go.GetComponentsInChildren<CableSpinner>(true);
                            if (cs != null && cs.Length > 0)
                            {
                                var c0 = cs[0];
                                var col = c0.cableMaterial != null && c0.cableMaterial.HasProperty("_Color")
                                    ? c0.cableMaterial.color.ToString() : "?";
                                spin = $" spinners={cs.Length} rgb='{c0.rgbColor}' matColor={col}";
                            }
                        }
                    }
                    catch (Exception ex) { spin = " spin-err:" + ex.Message; }
                    Log.Info($"    uid={kv.Key} go='{n}'{spin}");
                }
            }
            catch (Exception ex) { Log.Warning("DumpSpawnedItems failed: " + ex.Message); }
        }

        // ------------------------------------------------------- color flow (ISSUE-004 diagnostics)

        /// <summary>Checkout tracking: custom-color cart entries + spawn order + already colored UIDs.</summary>
        private static readonly System.Collections.Generic.List<Color> _checkoutColors = new();
        private static readonly System.Collections.Generic.List<PlayerManager.ObjectInHand> _checkoutTypes = new();
        private static readonly System.Collections.Generic.List<int> _checkoutCartIndexes = new();
        private static readonly System.Collections.Generic.List<int> _checkoutQuantities = new();
        private static readonly System.Collections.Generic.List<int> _checkoutUnitOffsets = new();
        private static readonly System.Collections.Generic.List<int> _checkoutSpawnUids = new();
        private static readonly System.Collections.Generic.HashSet<int> _checkoutColoredUids = new();
        private static bool _checkoutActive;

        private static void ColorTrace(string step, string detail, ComputerShop shop = null)
        {
            try
            {
                Log.Info($"[Color] {step}: {detail}");
                if (shop == null) return;
                DumpColorState(shop);
            }
            catch (Exception ex)
            {
                Log.Warning("ColorTrace failed: " + ex.Message);
            }
        }

        private static void DumpColorState(ComputerShop shop)
        {
            try
            {
                var picker = shop.flexibleColorPicker;
                if (picker != null)
                {
                    var c = picker.GetColor();
                    Log.Info($"  picker.GetColor=({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}) a={c.a:F3}");
                }
                else Log.Info("  picker=null");

                try
                {
                    Log.Info($"  pending: id={shop.pendingItemID} price={shop.pendingPrice} type={shop.pendingItemType} " +
                             $"name='{shop.pendingDisplayName}' cartItem={(shop.pendingCartItem != null ? "yes" : "null")}");
                }
                catch { }

                try
                {
                    var cart = shop.cartUIItems;
                    if (cart == null || cart.Count == 0) { Log.Info("  cart: empty"); }
                    else
                    {
                        Log.Info($"  cart: {cart.Count} item(s)");
                        for (int i = 0; i < cart.Count; i++)
                        {
                            var it = cart[i];
                            if (it == null) continue;
                            var ic = it.itemColor;
                            Log.Info($"    [{i}] id={it.ItemID} qty={it.Quantity} hasCustomColor={it.hasCustomColor} " +
                                     $"itemColor=({ic.r:F3},{ic.g:F3},{ic.b:F3},{ic.a:F3})");
                        }
                    }
                }
                catch (Exception ex) { Log.Info("  cart dump failed: " + ex.Message); }
            }
            catch (Exception ex)
            {
                Log.Warning("DumpColorState failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(ComputerShop), "OpenColorPicker")]
        [HarmonyPostfix]
        private static void OpenColorPickerPostfix(ComputerShop __instance)
        {
            try { if (__instance == null) return; ColorTrace("OpenColorPicker", "opened", __instance); }
            catch (Exception ex) { Log.Warning("OpenColorPickerPostfix failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonChosenColor))]
        [HarmonyPrefix]
        private static void ButtonChosenColorPrefix(ComputerShop __instance)
        {
            try { if (__instance == null) return; ColorTrace("ButtonChosenColor", "BEFORE original", __instance); }
            catch (Exception ex) { Log.Warning("ButtonChosenColorPrefix failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonChosenColor))]
        [HarmonyPostfix]
        private static void ButtonChosenColorPostfix(ComputerShop __instance)
        {
            try { if (__instance == null) return; ColorTrace("ButtonChosenColor", "AFTER original", __instance); }
            catch (Exception ex) { Log.Warning("ButtonChosenColorPostfix failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonCancelColorPicker))]
        [HarmonyPostfix]
        private static void ButtonCancelColorPickerPostfix(ComputerShop __instance)
        {
            try { if (__instance == null) return; ColorTrace("ButtonCancelColorPicker", "cancelled", __instance); }
            catch (Exception ex) { Log.Warning("ButtonCancelColorPickerPostfix failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ComputerShop), "AddNewCartItem")]
        [HarmonyPrefix]
        private static void AddNewCartItemPrefix(ComputerShop __instance, int itemID, int price,
            PlayerManager.ObjectInHand itemType, string displayName,
            Il2CppSystem.Nullable<Color> chosenColor)
        {
            try
            {
                if (__instance == null) return;
                string col = "null";
                try
                {
                    if (chosenColor != null && chosenColor.HasValue)
                    {
                        var c = chosenColor.Value;
                        col = $"({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3})";
                    }
                }
                catch (Exception ex) { col = "err:" + ex.Message; }
                ColorTrace("AddNewCartItem", $"id={itemID} type={itemType} name='{displayName}' chosenColor={col}", __instance);
            }
            catch (Exception ex) { Log.Warning("AddNewCartItemPrefix failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ComputerShop), "ApplyColorToSpawnedItem")]
        [HarmonyPrefix]
        private static void ApplyColorToSpawnedPrefix(ComputerShop __instance, ref int uid, Color color,
            PlayerManager.ObjectInHand itemType)
        {
            try
            {
                if (__instance == null) return;
                int requested = uid;
                int resolved = ResolveSpawnedUid(__instance, uid);
                // Checkout: vanilla calls per spawn with stale UID (e.g. always 1).
                // Freshest spawn of running checkout is the real target —
                // even on exact hit (key may be a stale leftover of earlier
                // checkouts, spawnedItems is never cleared).
                if (_checkoutActive && _checkoutSpawnUids.Count > 0)
                {
                    try
                    {
                        int last = _checkoutSpawnUids[_checkoutSpawnUids.Count - 1];
                        if (__instance.spawnedItems != null && __instance.spawnedItems.ContainsKey(last) && last != resolved)
                        {
                            Log.Info($"[Color] ApplyColor uid checkout-redirect: requested={requested} -> {last} (freshest spawn)");
                            resolved = last;
                        }
                    }
                    catch { /* fallback below */ }
                }
                if (resolved != uid)
                {
                    Log.Info($"[Color] ApplyColor uid remap: requested={uid} -> resolved={resolved} " +
                             $"(spawnedItems miss; uniqueID={__instance.uniqueID})");
                    uid = resolved;
                }
                ColorTrace("ApplyColorToSpawnedItem",
                    $"uid={uid} type={itemType} color=({color.r:F3},{color.g:F3},{color.b:F3},{color.a:F3})" +
                    (requested != uid ? $" (remapped from {requested})" : ""), __instance);
                DumpSpawnedItems(__instance, $"before ApplyColor uid={uid}");
                if (_checkoutActive) _checkoutColoredUids.Add(uid);
            }
            catch (Exception ex) { Log.Warning("ApplyColorToSpawnedPrefix failed: " + ex.Message); }
        }

        /// <summary>
        /// Vanilla SpawnPhysicalItem stores under key N, but bumps uniqueID to N+1
        /// and calls ApplyColorToSpawnedItem with the post-increment value → lookup miss.
        /// Prefer exact key, else uniqueID-1, else matching existing key.
        /// </summary>
        private static int ResolveSpawnedUid(ComputerShop shop, int uid)
        {
            try
            {
                var dict = shop.spawnedItems;
                if (dict == null) return uid;
                if (dict.ContainsKey(uid)) return uid;
                int minus = uid - 1;
                if (minus >= 0 && dict.ContainsKey(minus)) return minus;
                int plus = uid + 1;
                if (dict.ContainsKey(plus)) return plus;
                // Fallback: single entry, if uniqueID fully off
                if (dict.Count == 1)
                {
                    foreach (var k in dict.Keys) return k;
                }
                return uid;
            }
            catch (Exception ex)
            {
                Log.Warning("ResolveSpawnedUid failed: " + ex.Message);
                return uid;
            }
        }

        [HarmonyPatch(typeof(ComputerShop), "ApplyColorToSpawnedItem")]
        [HarmonyPostfix]
        private static void ApplyColorToSpawnedPostfix(ComputerShop __instance, int uid, Color color,
            PlayerManager.ObjectInHand itemType)
        {
            try
            {
                if (__instance == null) return;
                Log.Info($"[Color] ApplyColorToSpawnedItem AFTER: uid={uid} color=({color.r:F3},{color.g:F3},{color.b:F3},{color.a:F3})");
                DumpSpawnedItems(__instance, $"after ApplyColor uid={uid}");
                ForceApplyColorIfStillDefault(__instance, uid, color, itemType);
            }
            catch (Exception ex) { Log.Warning("ApplyColorToSpawnedPostfix failed: " + ex.Message); }
        }

        /// <summary>
        /// If vanilla lookup/apply did not set the color despite correct UID
        /// (material still spawn-default gray / rgbColor empty), pull color again.
        /// </summary>
        private static void ForceApplyColorIfStillDefault(ComputerShop shop, int uid, Color color,
            PlayerManager.ObjectInHand itemType)
        {
            try
            {
                var dict = shop.spawnedItems;
                if (dict == null) return;
                int key = ResolveSpawnedUid(shop, uid);
                if (!dict.ContainsKey(key)) return;
                var go = dict[key];
                if (go == null) return;

                if (itemType != PlayerManager.ObjectInHand.CableSpinner) return;

                var spinners = go.GetComponentsInChildren<CableSpinner>(true);
                if (spinners == null || spinners.Length == 0) return;

                string wantRgb = "#" + ColorUtility.ToHtmlStringRGB(color);
                bool needsApply = false;
                foreach (var s in spinners)
                {
                    try
                    {
                        bool emptyRgb = string.IsNullOrEmpty(s.rgbColor);
                        bool defaultMat = false;
                        if (s.cableMaterial != null && s.cableMaterial.HasProperty("_Color"))
                        {
                            var mc = s.cableMaterial.color;
                            defaultMat = Mathf.Abs(mc.r - 0.877f) < 0.05f
                                      && Mathf.Abs(mc.g - 0.877f) < 0.05f
                                      && Mathf.Abs(mc.b - 0.877f) < 0.05f;
                        }
                        if (emptyRgb || defaultMat) needsApply = true;
                    }
                    catch { needsApply = true; }
                }
                if (!needsApply) return;

                foreach (var s in spinners)
                {
                    try
                    {
                        Log.Info($"[Color] ForceApplyColor on '{go.name}' " +
                                 $"rgb='{s.rgbColor}' -> {wantRgb} type={itemType}");
                        s.ApplyColor(color, wantRgb);
                    }
                    catch (Exception ex) { Log.Warning("ForceApplyColor spinner failed: " + ex.Message); }
                }
                DumpSpawnedItems(shop, $"after ForceApplyColor uid={key}");
            }
            catch (Exception ex) { Log.Warning("ForceApplyColorIfStillDefault failed: " + ex.Message); }
        }

        /// <summary>
        /// Checkout sweep: pull color even if vanilla never called
        /// (only first custom item in cart). Runs default check first,
        /// else unconditional.
        /// </summary>
        private static void ForceApplyColorToUid(ComputerShop shop, int uid, Color color,
            PlayerManager.ObjectInHand itemType)
        {
            ForceApplyColorIfStillDefault(shop, uid, color, itemType);
            try
            {
                if (itemType != PlayerManager.ObjectInHand.CableSpinner) return;
                var dict = shop.spawnedItems;
                if (dict == null) return;
                int key = ResolveSpawnedUid(shop, uid);
                if (!dict.ContainsKey(key)) return;
                var go = dict[key];
                if (go == null) return;
                var spinners = go.GetComponentsInChildren<CableSpinner>(true);
                if (spinners == null || spinners.Length == 0) return;
                string wantRgb = "#" + ColorUtility.ToHtmlStringRGB(color);
                bool anyDefault = false;
                foreach (var s in spinners)
                {
                    if (string.IsNullOrEmpty(s.rgbColor)) { anyDefault = true; break; }
                    if (s.cableMaterial != null && s.cableMaterial.HasProperty("_Color"))
                    {
                        var mc = s.cableMaterial.color;
                        if (Mathf.Abs(mc.r - 0.877f) < 0.05f
                            && Mathf.Abs(mc.g - 0.877f) < 0.05f
                            && Mathf.Abs(mc.b - 0.877f) < 0.05f)
                        {
                            anyDefault = true;
                            break;
                        }
                    }
                }
                if (!anyDefault) return;
                foreach (var s in spinners)
                {
                    try
                    {
                        Log.Info($"[Color] ForceApplyColorToUid on '{go.name}' " +
                                 $"rgb='{s.rgbColor}' -> {wantRgb} type={itemType} uid={key}");
                        s.ApplyColor(color, wantRgb);
                    }
                    catch (Exception ex) { Log.Warning("ForceApplyColorToUid spinner failed: " + ex.Message); }
                }
                DumpSpawnedItems(shop, $"after ForceApplyColorToUid uid={key}");
            }
            catch (Exception ex) { Log.Warning("ForceApplyColorToUid failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(ShopCartItem), nameof(ShopCartItem.Initialize))]
        [HarmonyPostfix]
        private static void ShopCartItemInitPostfix(ShopCartItem __instance,
            string itemName, int itemID, int price, PlayerManager.ObjectInHand itemType,
            Il2CppSystem.Nullable<Color> customColor)
        {
            try
            {
                if (__instance == null) return;
                string col = "null";
                try
                {
                    if (customColor != null && customColor.HasValue)
                    {
                        var c = customColor.Value;
                        col = $"({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3})";
                    }
                }
                catch (Exception ex) { col = "err:" + ex.Message; }
                var ic = __instance.itemColor;
                Log.Info($"[Color] ShopCartItem.Initialize: id={itemID} name='{itemName}' type={itemType} " +
                         $"argColor={col} hasCustomColor={__instance.hasCustomColor} " +
                         $"stored=({ic.r:F3},{ic.g:F3},{ic.b:F3},{ic.a:F3})");
            }
            catch (Exception ex) { Log.Warning("ShopCartItemInitPostfix failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(CableSpinner), nameof(CableSpinner.ApplyColor))]
        [HarmonyPrefix]
        private static void CableSpinnerApplyColorPrefix(CableSpinner __instance, Color color, string rgbString)
        {
            try
            {
                if (__instance == null) return;
string goName = "?";
                string parent = "?";
                try { goName = __instance.gameObject != null ? __instance.gameObject.name : "null"; } catch { }
                try { parent = __instance.gameObject != null && __instance.gameObject.transform.parent != null
                    ? __instance.gameObject.transform.parent.name : "?"; } catch { }
                string mat = "?";
                try
                {
                    var m = __instance.cableMaterial;
                    if (m != null && m.HasProperty("_Color")) mat = m.color.ToString();
                }
                catch (Exception ex) { mat = "err:" + ex.Message; }
                Log.Info($"[Color] CableSpinner.ApplyColor: go='{goName}' parent='{parent}' " +
                         $"color=({color.r:F3},{color.g:F3},{color.b:F3},{color.a:F3}) rgb='{rgbString}' matBefore={mat} rgbField='{__instance.rgbColor}'");
            }
            catch (Exception ex) { Log.Warning("CableSpinnerApplyColorPrefix failed: " + ex.Message); }
        }

        [HarmonyPatch(typeof(CableSpinner), nameof(CableSpinner.ApplyCustomColorFromSaveValue))]
        [HarmonyPrefix]
        private static void CableSpinnerApplySavePrefix(CableSpinner __instance)
        {
            try
            {
                if (__instance == null) return;
                Log.Info($"[Color] CableSpinner.ApplyCustomColorFromSaveValue: rgbColor='{__instance.rgbColor}'");
            }
            catch (Exception ex) { Log.Warning("CableSpinnerApplySavePrefix failed: " + ex.Message); }
        }

        // ------------------------------------------------------- server lifecycle

        /// <summary>
        /// Drain IMMEDIATELY after checkout (no wait for 1/s tick): spawn
        /// is then guaranteed registered - tint lands right after purchase.
        /// </summary>
        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.ButtonCheckOut))]
        [HarmonyPostfix]
        private static void ComputerShopCheckoutPostfix(ComputerShop __instance)
        {
            try
            {
                if (__instance == null) return;
                BackplanesMod.Injector.DrainSpawnedSpecsNow("ComputerShop.ButtonCheckOut");
            }
            catch (Exception ex)
            {
                Log.Error("ComputerShopCheckoutPostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(Server), nameof(Server.ServerInsertedInRack))]
        [HarmonyPostfix]
        private static void ServerInsertedPostfix(Server __instance, ServerSaveData serverSaveData)
        {
            try
            {
                if (__instance == null) return;
                BackplanesMod.Injector.FinalizeInsertedServer("Server.ServerInsertedInRack", __instance, serverSaveData);
            }
            catch (Exception ex)
            {
                Log.Error("ServerInsertedPostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(Server), nameof(Server.Start))]
        [HarmonyPostfix]
        private static void ServerStartPostfix(Server __instance)
        {
            try
            {
                if (__instance == null) return;
                // Save-loaded servers materialize through Start(); this is the
                // deterministic repair path (no companion mod needed).
                if (!BackplanesMod.Injector.InRepairWindow) return;
                BackplanesMod.Injector.TryRepairServer("Server.Start", __instance, onlyIfKnown: false);
            }
            catch (Exception ex)
            {
                Log.Error("ServerStartPostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(Server), nameof(Server.Awake))]
        [HarmonyPostfix]
        private static void ServerAwakePostfix(Server __instance)
        {
            try
            {
                if (__instance == null) return;
                if (!BackplanesMod.Injector.InRepairWindow) return;
                BackplanesMod.Injector.TryRepairServer("Server.Awake", __instance, onlyIfKnown: true);
            }
            catch (Exception ex)
            {
                Log.Error("ServerAwakePostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(Server), nameof(Server.OnLoadingComplete))]
        [HarmonyPostfix]
        private static void ServerLoadingCompletePostfix(Server __instance)
        {
            try
            {
                if (__instance == null) return;
                if (!BackplanesMod.Injector.InRepairWindow) return;
                BackplanesMod.Injector.TryRepairServer("Server.OnLoadingComplete", __instance, onlyIfKnown: false);
            }
            catch (Exception ex)
            {
                Log.Error("ServerLoadingCompletePostfix failed.", ex);
            }
        }

        [HarmonyPatch(typeof(Server), nameof(Server.RepairDevice))]
        [HarmonyPostfix]
        private static void ServerRepairDevicePostfix(Server __instance)
        {
            try
            {
                if (__instance == null) return;
                BackplanesMod.Injector.RepairAfterDeviceRepair("Server.RepairDevice", __instance);
            }
            catch (Exception ex)
            {
                Log.Error("ServerRepairDevicePostfix failed.", ex);
            }
        }

        /// <summary>
        /// Deterministic per-port hook: fires when the game wires a CableLink to
        /// a Server. Configures known variant ports immediately (v2.1.3) so they
        /// never stay at Vanilla connectionSpeed=0.2 („1 Gbps").
        /// </summary>
        [HarmonyPatch(typeof(Server), nameof(Server.RegisterLink))]
        [HarmonyPostfix]
        private static void ServerRegisterLinkPostfix(Server __instance, CableLink link)
        {
            try
            {
                if (__instance == null || link == null) return;
                BackplanesMod.Injector.OnLinkRegistered("Server.RegisterLink", __instance, link);
            }
            catch (Exception ex)
            {
                Log.Warning("ServerRegisterLinkPostfix failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Port Start: configure as soon as the component is live (often before
        /// ServerInsertedInRack / CollectServerLinks can see the port).
        /// </summary>
        [HarmonyPatch(typeof(CableLink), nameof(CableLink.Start))]
        [HarmonyPostfix]
        private static void CableLinkStartPostfix(CableLink __instance)
        {
            try
            {
                if (__instance == null) return;
                BackplanesMod.Injector.OnLinkStarted(__instance);
            }
            catch (Exception ex)
            {
                Log.Warning("CableLinkStartPostfix failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------- cables (warn-only)

        [HarmonyPatch(typeof(CableLink), nameof(CableLink.InteractOnClick))]
        [HarmonyPrefix]
        private static bool CableLinkInteractPrefix(CableLink __instance)
        {
            try
            {
                if (__instance == null) return true;
                // Warn-only: must ALWAYS return true. Blocking here caused
                // unconnectable ports and 0G links in v1.x.
                return CableGuard.CheckInteraction(__instance, BackplanesMod.Injector);
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging) Log.Warning("CableLinkInteractPrefix failed: " + ex.Message);
                return true;
            }
        }

        [HarmonyPatch(typeof(CableLink), nameof(CableLink.InteractOnClick))]
        [HarmonyPostfix]
        private static void CableLinkInteractPostfix(CableLink __instance)
        {
            try
            {
                if (__instance == null) return;
                CableGuard.DiagnosePostClick(__instance);
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging) Log.Warning("CableLinkInteractPostfix failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Module inserted: raise port cap once to tier speed if needed
        /// (if previously negotiated low). Only raise, never touch modules.
        /// </summary>
        [HarmonyPatch(typeof(CableLink), nameof(CableLink.InsertSFP))]
        [HarmonyPostfix]
        private static void CableLinkInsertSFPPostfix(CableLink __instance)
        {
            try
            {
                if (__instance == null) return;
                BackplanesMod.Injector.ReassertPortCapAfterModuleInsert("CableLink.InsertSFP", __instance);
            }
            catch (Exception ex)
            {
                Log.Warning("CableLinkInsertSFPPostfix failed: " + ex.Message);
            }
        }
    }
}
