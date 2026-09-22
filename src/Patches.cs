using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GregMod.Backplanes
{
    /// <summary>
    /// Minimal Harmony surface. Every patch is defensive (try/catch + null checks)
    /// and does the smallest possible work; heavy lifting lives in CatalogInjector.
    ///
    /// Deliberately NOT patched (v1.x hooks removed):
    /// <list type="bullet">
    /// <item>ShopItem.Awake/Start/UpdateVisualState — caused shop-open lag and
    ///   interfered with custom-color cables/racks. Card text is set at clone time.</item>
    /// <item>ShopCartItem.* — v1.x patched a method that does not exist in the
    ///   current build (silently dead). Spawns are handled via the live
    ///   ComputerShop.SpawnPhysicalItem postfix.</item>
    /// <item>Technician.* — technician replacements are covered by the typed
    ///   Server.RepairDevice postfix; fewer hooks = fewer cross-mod conflicts.</item>
    /// </list>
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
        /// Das Spiel rendert Kartennamen per ItemID-Lookup (Base-Namen bzw.
        /// "Unknown" bei unseren IDs). Nach jedem Vanilla-Refresh die
        /// Varianten-Texte neu setzen (Anzeige only, keine Logik).
        /// </summary>
        [HarmonyPatch(typeof(ShopItem), nameof(ShopItem.UpdateVisualState))]
        [HarmonyPostfix]
        private static void ShopItemVisualPostfix(ShopItem __instance)
        {
            try
            {
                if (__instance == null) return;
                ApplyVariantCardTexts(__instance);
            }
            catch { /* Anzeige only */ }
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
            catch { /* Anzeige only */ }
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
                // Hinweis: KEIN ClearPendingPurchases mehr hier. Gekaufte Items
                // existieren physisch weiter (Spieler traegt sie / Cart), auch
                // wenn der Shop zu ist. Wipe wuerde die Kauf->Insert-Korrelation
                // zerstoeren. Abgelaufenes raeumt Expiry weg (10min/60s).
                Log.Info("ComputerShop.ButtonClear gesehen (Pending bleibt erhalten).");
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
                // Wie oben: Shop-Schliessen darf Pending NICHT loeschen,
                // sonst ist beim spaeteren Einsetzen nichts mehr bekannt.
            }
            catch (Exception ex)
            {
                Log.Error("ComputerShopCancelPostfix failed.", ex);
            }
        }

        // ------------------------------------------------------- purchases

        /// <summary>
        /// Varianten haben eigene Item-IDs (9001-9008). Das Spiel kennt nur
        /// Base-Prefabs: Hier wird die Varianten-ID transparent auf die Base-ID
        /// der Familie zurueckgemappt (ref-Parameter), damit der Original-Code
        /// das korrekte Base-Prefab liefert. Warenkorb/Kauf behalten die
        /// Varianten-ID (eigene Identitaet end-to-end).
        /// </summary>
        [HarmonyPatch(typeof(ComputerShop), nameof(ComputerShop.GetPrefabForItem))]
        [HarmonyPrefix]
        private static bool GetPrefabForItemPrefix(ComputerShop __instance, ref int itemID,
            PlayerManager.ObjectInHand itemType)
        {
            try
            {
                if (__instance == null) return true;
                if (BackplanesMod.Injector.TryGetBaseId(itemID, out int baseId))
                {
                    itemID = baseId;
                }
                return true; // Original immer laufen lassen
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
                if (isCustomColor) return true;
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
        private static void SpawnPhysicalPostfix(ComputerShop __instance, int price,
            PlayerManager.ObjectInHand itemType, Il2CppSystem.Nullable<int> __result)
        {
            try
            {
                if (__instance == null) return;
                if (__result == null) return;
                bool hasValue = false;
                int uid = int.MinValue;
                try { hasValue = __result.HasValue; if (hasValue) uid = __result.Value; } catch { return; }
                if (!hasValue) return;
                BackplanesMod.Injector.ConfigureSpawnedItem("ComputerShop.SpawnPhysicalItem", __instance, price, itemType, uid);
            }
            catch (Exception ex)
            {
                Log.Error("SpawnPhysicalPostfix failed.", ex);
            }
        }

        // ------------------------------------------------------- server lifecycle

        /// <summary>
        /// Nach Checkout SOFORT drainen (nicht auf 1/s-Tick warten): Der Spawn
        /// ist dann garantiert registriert - Tint sitzt direkt nach dem Kauf.
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
    }
}
