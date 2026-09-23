using System;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(GregMod.Backplanes.BackplanesMod), "gregMod.Backplanes", "2.2.3", "TeamGreg Modding")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace GregMod.Backplanes
{
    /// <summary>
    /// User-facing toggles. Entry-backed properties so the F6 panel, the settings
    /// hub tab and the cfg file always read the same values. Category/entry names
    /// are stable — existing MelonPreferences.cfg files keep working.
    /// </summary>
    internal static class ModConfig
    {
        internal static MelonPreferences_Entry<int> PrefRepairWindow;
        internal static MelonPreferences_Entry<bool> PrefCableWarnings;
        internal static MelonPreferences_Entry<bool> PrefVerboseLogging;
        internal static MelonPreferences_Entry<bool> PrefServerTint;
        internal static MelonPreferences_Entry<bool> PrefServerScale;
        internal static MelonPreferences_Entry<string> PrefToggleKey;
        internal static MelonPreferences_Entry<bool> PrefForcePortSpeed;
        internal static Key ToggleKey = Key.F6;

        internal static int RepairWindowSeconds => Clamp(PrefRepairWindow?.Value ?? 20, 5, 120);
        internal static bool CableWarnings => PrefCableWarnings?.Value ?? true;
        internal static bool VerboseLogging => PrefVerboseLogging?.Value ?? false;
        internal static bool ServerTint => PrefServerTint?.Value ?? true;
        internal static bool ServerScale => PrefServerScale?.Value ?? true;
        internal static bool ForcePortSpeed => PrefForcePortSpeed?.Value ?? false;

        internal static void Load()
        {
            try
            {
                var category = MelonPreferences.CreateCategory("gregMod.Backplanes", "Backplanes (High-IOPS Servers)");
                PrefRepairWindow = category.CreateEntry("RepairWindowSeconds", 20,
                    "Seconds after scene load during which save-loaded servers are repaired.");
                PrefCableWarnings = category.CreateEntry("CableWarnings", true,
                    "Show a hint when a held cable does not match the recommended family. Never blocks.");
                PrefVerboseLogging = category.CreateEntry("VerboseLogging", false,
                    "Detailed logging for troubleshooting.");
                PrefServerTint = category.CreateEntry("ServerTint", true,
                    "Recolor boosted servers (orange/violet/red/lime) to tell them apart.");
                PrefServerScale = category.CreateEntry("ServerScale", true,
                    "Give boosted servers a taller look (4U/8U). Visual only, rack slots unchanged.");
                PrefForcePortSpeed = category.CreateEntry("ForcePortSpeed", false,
                    "Set port speed even on busy (cabled) ports. May fight live traffic — use to unstick ports.");
                PrefToggleKey = category.CreateEntry("ToggleKey", "F6",
                    "Hotkey to open/close the Backplanes panel.");
                try
                {
                    if (Enum.TryParse<Key>(PrefToggleKey.Value, true, out var k) && k != Key.None)
                        ToggleKey = k;
                    else
                        MelonLogger.Warning($"gregMod.Backplanes: Unknown ToggleKey '{PrefToggleKey.Value}', defaulting to F6.");
                }
                catch { }
                category.SaveToFile(false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("gregMod.Backplanes: could not load preferences, using defaults: " + ex.Message);
            }
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    }

    /// <summary>
    /// gregMod.Backplanes v2.2.3 — refactored successor of BackplaneBoostServers v1.0.1.
    ///
    /// 20 high-IOPS backplane server variants (SystemX / RISC / Mainframe / GPU ×
    /// 100K/500K/1M/2M/4M) with bandwidth tiers aligned to gregMod.MoreModules
    /// (QSFP28/56/DD), save/load-safe persistence and runtime visual differentiation.
    /// Panel + camera locking follow gregMod.MusicPlayer (gregCore UI Toolkit stack).
    /// See docs/BUGFIX_NOTES.md for the v1.x issue -&gt; fix mapping.
    /// </summary>
    public sealed class BackplanesMod : MelonMod
    {
        internal static BackplanesMod Instance { get; private set; }
        internal static readonly CatalogInjector Injector = new CatalogInjector();

        private float _statusRefreshAt;

        public override void OnInitializeMelon()
        {
            try
            {
                Instance = this;
                ModConfig.Load();
                Log.Info("Initializing gregMod.Backplanes v2.2.3 (refactored from BackplaneBoostServers v1.0.1).");
                Log.Info($"Press {ModConfig.ToggleKey} for the Backplanes panel.");
                Patches.Apply(HarmonyInstance);
                if (GregHost.HasCore)
                {
                    RegisterSettingsTab();
                    RegisterModContract();
                    RegisterSaveSidecar();
                    RegisterHudAndOpener();
                }
                else
                {
                    Log.Info("Standalone-Modus (ohne gregCore): Basisfunktionen.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("OnInitializeMelon failed.", ex);
            }
        }

        /// <summary>
        /// Marker-Sidecar ueber GregSaveGuard: reist mit dem Savegame mit
        /// (greg_backplanes.&lt;save&gt;.tsv neben den Saves), ohne Mod inert.
        /// </summary>
        private static void RegisterSaveSidecar()
        {
            try
            {
                gregCore.Infrastructure.Persistence.GregSaveGuard.RegisterSidecar(
                    "backplanes",
                    () => Injector.RegistrySerialize(),
                    content => Injector.RegistryDeserialize(content));
            }
            catch (Exception ex)
            {
                Log.Error("Save-Sidecar-Registrierung fehlgeschlagen.", ex);
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            try
            {
                Log.Info($"Scene loaded: {sceneName} ({buildIndex}).");
                Injector.ResetForScene();
                Injector.BeginRepairWindow();
                // Best-effort early registration; shop-open triggers cover the rest.
                try
                {
                    var shop = MainGameManager.instance?.computerShop;
                    if (shop != null)
                        Injector.TryRegisterAll("scene-load", shop);
                }
                catch { /* shop may not exist yet */ }
            }
            catch (Exception ex)
            {
                Log.Error("OnSceneWasLoaded failed.", ex);
            }
        }

        public override void OnUpdate()
        {
            // Click routing for the panel (fallback when no EventSystem delivers).
            try { BackplanesOverlay.RouteClicks(); } catch (Exception ex) { Log.Error("Click routing failed.", ex); }

            // RGB-Streifen: Hue-Rotation für getrackte Titan-Materialien.
            try { RgbAnimator.Tick(); } catch (Exception ex) { Log.Error("RGB tick failed.", ex); }

            try
            {
                var kb = Keyboard.current;
                if (kb != null && kb[ModConfig.ToggleKey].wasPressedThisFrame && !IsPauseMenuActive())
                    BackplanesOverlay.Toggle();
            }
            catch { /* input best-effort */ }

            // Live status while the panel is open (repairs land during the sweep).
            try
            {
                if (BackplanesOverlay.IsVisible && Time.unscaledTime >= _statusRefreshAt)
                {
                    _statusRefreshAt = Time.unscaledTime + 2f;
                    BackplanesOverlay.Refresh();
                }
            }
            catch { /* best-effort */ }

            // Cheap timestamp gate inside; scans only within the repair window.
            Injector.Tick();
        }

        // Settings hub tab (gregCore), second surface next to the F6 panel.
        private static void RegisterSettingsTab()
        {
            try
            {
                greg.UI.Settings.GregSettingsHub.RegisterTab("backplanes.settings", "Backplanes",
                    (Action<gregCore.UI.GregPanelBuilder>)(b =>
                    {
                        b.AddToggle("Server tint", ModConfig.ServerTint, v =>
                        {
                            try { if (ModConfig.PrefServerTint != null) { ModConfig.PrefServerTint.Value = v; MelonPreferences.Save(); } } catch { /* best-effort */ }
                            Injector.RefreshVisualsAfterToggle("ServerTint");
                        });
                        b.AddToggle("Taller look", ModConfig.ServerScale, v =>
                        {
                            try { if (ModConfig.PrefServerScale != null) { ModConfig.PrefServerScale.Value = v; MelonPreferences.Save(); } } catch { /* best-effort */ }
                            Injector.RefreshVisualsAfterToggle("ServerScale");
                        });
                        b.AddToggle("Cable warnings", ModConfig.CableWarnings, v =>
                        {
                            try { if (ModConfig.PrefCableWarnings != null) { ModConfig.PrefCableWarnings.Value = v; MelonPreferences.Save(); } } catch { /* best-effort */ }
                        });
                        b.AddToggle("Verbose logging", ModConfig.VerboseLogging, v =>
                        {
                            try { if (ModConfig.PrefVerboseLogging != null) { ModConfig.PrefVerboseLogging.Value = v; MelonPreferences.Save(); } } catch { /* best-effort */ }
                        });
                        b.AddToggle("Force port speed (busy ports too)", ModConfig.ForcePortSpeed, v =>
                        {
                            try { if (ModConfig.PrefForcePortSpeed != null) { ModConfig.PrefForcePortSpeed.Value = v; MelonPreferences.Save(); } } catch { /* best-effort */ }
                        });
                        b.AddSlider("Repair window (s)", 5f, 120f, ModConfig.RepairWindowSeconds, v =>
                        {
                            try { if (ModConfig.PrefRepairWindow != null) { ModConfig.PrefRepairWindow.Value = Math.Max(5, Math.Min(120, (int)Math.Round(v))); MelonPreferences.Save(); } } catch { /* best-effort */ }
                        });
                        b.AddSecondaryButton("Repair now", () =>
                        {
                            try { Injector.BeginRepairWindow(); Log.Info("Manual repair started from settings hub."); }
                            catch (Exception ex) { Log.Error("Manual repair failed.", ex); }
                        });
                    }));
            }
            catch (Exception ex)
            {
                Log.Warning("Settings tab registration failed: " + ex.GetBaseException().Message);
            }
        }

        private static void RegisterModContract()
        {
            try
            {
                gregCore.Core.Mods.GregModRegistry.Register(
                    "gregMod.Backplanes", "Backplanes", "2.2.3",
                    new string[] { "backplanes" });
            }
            catch (Exception ex)
            {
                Log.Warning("Mod registration failed: " + ex.GetBaseException().Message);
            }
        }

        // Spieler-sichtbare Warnung via gregCore (z.B. Checkout-Verify bei
        // Bulk-Kaeufen). Nur bei HasCore aufrufen (eigene Methode wegen
        // JIT-Trennung ohne gregCore-DLL).
        internal static void NotifyCore(string message)
        {
            try { gregCore.UI.GregNotificationManager.Show(message, 5f); }
            catch (Exception ex) { Log.Warning("NotifyCore failed: " + ex.GetBaseException().Message); }
        }

        // Tasten-HUD (rechter Rand) + Oeffner fuers F1-Hub. Nur mit gregCore
        // aufrufen (eigene Methode wegen JIT-Trennung ohne gregCore-DLL).
        private static void RegisterHudAndOpener()
        {
            try
            {
                gregCore.UI.GregHudRegistry.Register("backplanes", ModConfig.ToggleKey.ToString(), "Backplanes");
                gregCore.UI.GregMenuRegistry.RegisterOpener("backplanes", () => BackplanesOverlay.Toggle());
                gregCore.UI.GregMenuRegistry.RegisterCloser("backplanes",
                    () => { try { if (BackplanesOverlay.IsVisible) BackplanesOverlay.Toggle(); } catch { /* best-effort */ } });
            }
            catch (Exception ex)
            {
                Log.Warning("HUD registration failed: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>True while a game pause/settings canvas is on screen.</summary>
        internal static bool IsPauseMenuActive()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Canvas>();
                if (all == null) return false;
                foreach (var c in all)
                {
                    if (c == null || !c.isActiveAndEnabled) continue;
                    var go = c.gameObject;
                    if (go == null) continue;
                    if (!go.scene.IsValid() || !go.scene.isLoaded) continue;
                    if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;

                    var n = go.name ?? "";
                    if (n.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("EscapeMenu", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("InGameMenu", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("SystemMenu", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("OptionsMenu", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("SettingsMenu", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { /* best-effort */ }
            return false;
        }
    }
}
