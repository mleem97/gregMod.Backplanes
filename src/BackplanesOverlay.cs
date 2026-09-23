using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UIElements;

namespace GregMod.Backplanes
{
    // UI-Toolkit-Panel auf gregCores Layer-Root, wie gregMod.MusicPlayer:
    // GregPanel (LockCamera/LockMovement/LockInteract/ShowCursor) + manuelles
    // Klick-Routing als Fallback fuers fehlende EventSystem, explicite
    // Action<ClickEvent>-Callbacks (IL2CPP-Regel), Game-Font via GregFontLoader.
    public static class BackplanesOverlay
    {
        private sealed class Clickable
        {
            public VisualElement Element;
            public Action Action;
        }

        private static IPanelChrome _chrome;
        private static VisualElement _content;
        private static DateTime _lastRealClickUtc = DateTime.MinValue;
        private static readonly List<Clickable> _clickables = new List<Clickable>();
        private static readonly List<Label> _labels = new List<Label>();

        public static bool IsVisible => _chrome != null && _chrome.IsVisible;

        public static void Toggle()
    {
        try
        {
            if (_chrome == null) _chrome = PanelChromeFactory.Create();
            if (_chrome == null) return;
            _chrome.Configure(false, 440f);
            bool willShow = !_chrome.IsVisible;
            if (willShow) Rebuild();
            _chrome.Toggle();
            Log.Info("Backplanes panel " + (_chrome.IsVisible ? "shown (F6)." : "hidden."));
            try { if (GregHost.HasCore) ReportOpenState(); } catch { /* best-effort */ }
        }
        catch (Exception ex)
        {
            Log.Error("Panel toggle failed: " + ex.GetBaseException().Message);
        }
    }

    // Separate Methode (JIT-Trennung): beruehrt gregCore-Typen und wird nur
    // aufgerufen, wenn GregHost.HasCore true ist. Meldet den Panel-Status ans
    // F1-Hub (GregMenuRegistry), damit Offen/Zu + Schliessen stimmen.
    private static void ReportOpenState()
    {
        try { gregCore.UI.GregMenuRegistry.SetOpen("backplanes", IsVisible); } catch { /* best-effort */ }
    }

    public static void Refresh()
        {
            try
            {
                if (_chrome == null || !_chrome.IsVisible) return;
                Rebuild();
            }
            catch (Exception ex)
            {
                Log.Warning("Panel refresh failed: " + ex.Message);
            }
        }

        // Jeden Frame aus Mod.OnUpdate: leitet Mausklicks an sichtbare Buttons
        // weiter (Ersatz fuers fehlende EventSystem).
        public static void RouteClicks()
        {
            if (!IsVisible || _clickables.Count == 0) return;
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse == null) return;
                if (!mouse.leftButton.wasPressedThisFrame) return;
                Vector2 pos = mouse.position.ReadValue();
                try
                {
                    if ((DateTime.UtcNow - _lastRealClickUtc).TotalMilliseconds < 500.0) return;
                }
                catch { /* best-effort */ }
                pos.y = Screen.height - pos.y;
                for (int i = _clickables.Count - 1; i >= 0; i--)
                {
                    var c = _clickables[i];
                    if (c == null || c.Element == null || c.Action == null) continue;
                    try
                    {
                        if (!c.Element.visible) continue;
                        Rect b = c.Element.worldBound;
                        if (b.width <= 0f || b.height <= 0f) continue;
                        if (b.Contains(pos))
                        {
                            c.Action();
                            return;
                        }
                    }
                    catch { /* best-effort */ }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Click routing failed: " + ex.Message);
            }
        }

        

        private static void Rebuild()
        {
            if (_chrome == null) return;
            _content = _chrome.Content;
            if (_content == null) return;

            _content.Clear();
            _clickables.Clear();
            _labels.Clear();

            var injector = BackplanesMod.Injector;

            var title = MkLabel("BACKPLANES");
            ModTheme.ApplyTextStyle(title, true);
            _content.Add(title);

            var status = MkLabel($"Shop variants: {injector.RegisteredCount}/8 ready");
            ModTheme.ApplyTextStyle(status, false);
            _content.Add(status);

            var status2 = MkLabel($"Repaired this scene: {injector.RepairedCount}  |  Markers: {injector.RegistryCount}");
            ModTheme.ApplyTextStyle(status2, false);
            _content.Add(status2);

            AddSeparator();

            var visuals = MkLabel("Visuals");
            ModTheme.ApplyTextStyle(visuals, true);
            _content.Add(visuals);

            AddToggleBtn(_content, "Server Tint", ModConfig.ServerTint,
                "Recolor boosted servers (orange/violet/red/lime).",
                () => ToggleBool(ModConfig.PrefServerTint, _ => injector.RefreshVisualsAfterToggle("ServerTint")));
            AddToggleBtn(_content, "Taller Look", ModConfig.ServerScale,
                "4U/8U style height. Visual only, rack slots unchanged.",
                () => ToggleBool(ModConfig.PrefServerScale, _ => injector.RefreshVisualsAfterToggle("ServerScale")));

            AddSeparator();

            var behavior = MkLabel("Behavior");
            ModTheme.ApplyTextStyle(behavior, true);
            _content.Add(behavior);

            AddToggleBtn(_content, "Cable Warnings", ModConfig.CableWarnings,
                "Hint when the held cable differs. Never blocks.",
                () => ToggleBool(ModConfig.PrefCableWarnings, null));
            AddToggleBtn(_content, "Verbose Logging", ModConfig.VerboseLogging,
                "Materials, repairs, rejected connections.",
                () => ToggleBool(ModConfig.PrefVerboseLogging, null));

            AddSeparator();

            var repair = MkLabel("Repair");
            ModTheme.ApplyTextStyle(repair, true);
            _content.Add(repair);

            var windowRow = Row();
            var windowLabel = MkLabel("Window: " + ModConfig.RepairWindowSeconds + "s");
            ModTheme.ApplyTextStyle(windowLabel, false);
            windowLabel.style.flexGrow = 1f;
            windowRow.Add(windowLabel);
            AddBtn(windowRow, "- 5s", () => StepRepairWindow(-5), false, 70f);
            AddBtn(windowRow, "+ 5s", () => StepRepairWindow(5), false, 70f);
            _content.Add(windowRow);

            AddBtn(_content, "Repair now", () =>
            {
                try { injector.BeginRepairWindow(); Log.Info("Manual repair started from panel (F6)."); }
                catch (Exception ex) { Log.Error("Manual repair failed.", ex); }
            }, true);

            var verifyLabel = MkLabel(injector.LastVerifySummary);
            ModTheme.ApplyTextStyle(verifyLabel, false);
            _content.Add(verifyLabel);

            AddBtn(_content, "Verify now", () =>
            {
                try
                {
                    injector.VerifyAllServers("panel");
                    Refresh();
                }
                catch (Exception ex) { Log.Error("Manual verify failed.", ex); }
            }, false);

            AddSeparator();

            AddBtn(_content, "Close (" + ModConfig.ToggleKey.ToString() + ")", () => { try { Toggle(); } catch { /* best-effort */ } }, false);
            ApplyFont();
        }

        private static void ToggleBool(MelonPreferences_Entry<bool> pref, Action<bool> onChanged)
        {
            if (pref == null) return;
            try
            {
                pref.Value = !pref.Value;
                MelonPreferences.Save();
                Log.Info($"{pref.DisplayName} = {pref.Value}");
                try { onChanged?.Invoke(pref.Value); } catch (Exception ex) { Log.Error("Toggle handler failed.", ex); }
                Refresh();
            }
            catch (Exception ex)
            {
                Log.Error("Toggle failed.", ex);
            }
        }

        private static void StepRepairWindow(int delta)
        {
            try
            {
                if (ModConfig.PrefRepairWindow == null) return;
                ModConfig.PrefRepairWindow.Value = Math.Max(5, Math.Min(120, ModConfig.PrefRepairWindow.Value + delta));
                MelonPreferences.Save();
                Log.Info($"RepairWindowSeconds = {ModConfig.PrefRepairWindow.Value}");
                Refresh();
            }
            catch (Exception ex)
            {
                Log.Error("Repair-window step failed.", ex);
            }
        }

        private static Label MkLabel(string text)
        {
            var l = new Label(text);
            _labels.Add(l);
            return l;
        }

        // Toolkit-Default-Font ist im IL2CPP-Build unbrauchbar (Text unsichtbar).
        // Daher Spiel-Font aus gregCore zuweisen (wie MusicPlayer).
        private static Font CoreFont()
        {
            return gregCore.UI.GregFontLoader.DefaultUGUIFont;
        }

        private static void ApplyFont()
        {
            Font f = null;
            try { f = GregHost.HasCore ? CoreFont() : ModLocalUI.ResolveFont(); } catch { /* best-effort */ }
            if (f == null) return;
            try
            {
                foreach (var l in _labels)
                {
                    try { if (l != null) l.style.unityFont = f; } catch { /* best-effort */ }
                }
                foreach (var c in _clickables)
                {
                    try
                    {
                        var b = c != null ? c.Element as Button : null;
                        if (b != null) b.style.unityFont = f;
                    }
                    catch { /* best-effort */ }
                }
            }
            catch { /* best-effort */ }
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 6f;
            return row;
        }

        private static void AddSeparator()
        {
            var sep = new VisualElement();
            sep.style.height = 2f;
            sep.style.backgroundColor = ModTheme.NeutralBorder;
            sep.style.marginTop = 6f;
            sep.style.marginBottom = 6f;
            _content.Add(sep);
        }

        private static void AddToggleBtn(VisualElement parent, string title, bool isOn, string hint, Action action)
        {
            var row = Row();
            var textCol = new VisualElement();
            textCol.style.flexGrow = 1f;
            var titleLabel = MkLabel(title);
            ModTheme.ApplyTextStyle(titleLabel, false);
            textCol.Add(titleLabel);
            var hintLabel = MkLabel(hint);
            ModTheme.ApplyTextStyle(hintLabel, false);
            textCol.Add(hintLabel);
            row.Add(textCol);

            var btn = new Button();
            btn.text = isOn ? "ON" : "OFF";
            btn.style.height = 34f;
            btn.style.width = 70f;
            if (isOn) ModTheme.ApplyPrimaryButtonStyle(btn);
            else ModTheme.ApplySecondaryButtonStyle(btn);
            try
            {
                btn.RegisterCallback<ClickEvent>(new Action<ClickEvent>(_ =>
                {
                    try { _lastRealClickUtc = DateTime.UtcNow; action?.Invoke(); } catch { /* best-effort */ }
                }));
            }
            catch { /* fallback routing below */ }
            row.Add(btn);
            _clickables.Add(new Clickable { Element = btn, Action = action });
            parent.Add(row);
        }

        private static void AddBtn(VisualElement parent, string label, Action action, bool primary, float width = 0f)
        {
            var btn = new Button();
            btn.text = label;
            btn.style.height = 34f;
            btn.style.marginBottom = 6f;
            if (width > 0f) btn.style.width = width;
            else btn.style.flexGrow = 1f;
            if (primary) ModTheme.ApplyPrimaryButtonStyle(btn);
            else
            {
                ModTheme.ApplySecondaryButtonStyle(btn);
                try { btn.style.color = new Color(0.88f, 0.88f, 0.88f); } catch { /* best-effort */ }
            }
            try
            {
                btn.RegisterCallback<ClickEvent>(new Action<ClickEvent>(_ =>
                {
                    try { _lastRealClickUtc = DateTime.UtcNow; action?.Invoke(); } catch { /* best-effort */ }
                }));
            }
            catch { /* fallback routing below */ }
            parent.Add(btn);
            _clickables.Add(new Clickable { Element = btn, Action = action });
        }
    }
}
