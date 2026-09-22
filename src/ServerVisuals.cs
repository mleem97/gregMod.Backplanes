using System;
using System.Collections.Generic;
using UnityEngine;

namespace GregMod.Backplanes
{
    /// <summary>
    /// Runtime visual differentiation for boosted servers (v2.1.0).
    ///
    /// The game exposes no color/size fields on Server, so both effects are applied
    /// to the live GameObject:
    /// <list type="bullet">
    /// <item><b>Tint:</b> body materials whose color is close to the family base color
    ///   (yellow/blue/purple/green) are recolored to the variant tint (orange/violet/
    ///   red/lime). Material names are unknown without runtime inspection, hence
    ///   proximity matching + name fallback + saturation gate. Screens, lights and
    ///   glass-ish materials are excluded by name.</item>
    /// <item><b>Scale:</b> absolute Y scale (4/3 on 3U-based, 8/7 on 7U-based) for a
    ///   4U/8U look. Visual only — rack slot occupancy is unchanged, so tall neighbors
    ///   may visually overlap. Attach points scale along, cables stay connected.</item>
    /// </list>
    /// Application is idempotent and pointer-tracked, so the per-second repair sweep
    /// costs one dictionary lookup per server after the first pass. Both effects are
    /// toggleable via MelonPreferences (ServerTint / ServerScale).
    /// </summary>
    internal static class ServerVisuals
    {
        private static readonly Dictionary<IntPtr, string> AppliedByPointer = new Dictionary<IntPtr, string>();
        private static readonly object Sync = new object();

        private static readonly string[] ExcludedNameParts =
        {
            "screen", "display", "light", "lamp", "led", "glass", "logo", "label", "text",
            "wall", "floor", "ceiling", "room", "building", "ground", "outdoor", "terrain",
            "window", "door",
        };

        // Plausibilitaet: Ein einzelner Server hat nur eine Handvoll Renderer.
        // Deutlich mehr deutet auf ein falsches Root-Objekt (Raum/Rack-Reihe)
        // hin - dann lieber nichts anfasssen als die Szene umfaerben.
        private const int MaxRenderersForTint = 24;

        private static readonly string[] FamilyColorWords = { "yellow", "blue", "purple", "green" };

        // Generous: vanilla body colors are strongly saturated, distances between
        // families are large (~1.0+), so 0.6 separates well without clipping edge cases.
        private const float ProximityThreshold = 0.6f;
        private const float MinSaturation = 0.2f;

        private static readonly string[] ColorProps = { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint", "_AlbedoColor" };

        internal static void Apply(Il2Cpp.Server server, ServerVariantSpec spec)
        {
            if (server == null || spec == null) return;
            bool wantTint = ModConfig.ServerTint;
            bool wantScale = ModConfig.ServerScale;
            // No early-out when both are off: scale-off must actively restore 1.0
            // on previously scaled servers (tint-off applies going forward; tinted
            // materials return to vanilla on scene reload).

            IntPtr ptr = IntPtr.Zero;
            try { ptr = server.Pointer; } catch { return; }
            if (ptr == IntPtr.Zero) return;

            string wantKey = spec.VariantId + "|t" + (wantTint ? "1" : "0") + "s" + (wantScale ? "1" : "0");
            lock (Sync)
            {
                if (AppliedByPointer.TryGetValue(ptr, out string done) && done == wantKey)
                    return;
            }

            try
            {
                GameObject root = null;
                try { root = server.gameObject; } catch { /* best-effort */ }
                if (root == null) return;

                // Plausibilitaet: zu viele Renderer = falsches Root (Raum statt
                // Server). Weder skalieren noch faerben in dem Fall.
                try
                {
                    var all = root.GetComponentsInChildren<Renderer>(true);
                    if (all != null && all.Length > MaxRenderersForTint)
                    {
                        Log.Warning($"Visuals {spec.VariantDisplayName} uebersprungen: {all.Length} Renderer " +
                            $"unter '{root.name}' (kein einzelner Server?).");
                        lock (Sync) AppliedByPointer[ptr] = wantKey;
                        return;
                    }
                }
                catch { }

                if (wantScale) ApplyScale(root, spec, true);
                else ApplyScale(root, spec, false); // live restore when toggled off
                if (wantTint) ApplyTint(root, spec);
                // NOTE: tint-off cannot restore original material colors (they were
                // recolored in place); it applies to newly configured servers and
                // everything is vanilla again after a scene reload. Scale restores live.

                lock (Sync) AppliedByPointer[ptr] = wantKey;
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging) Log.Warning("ServerVisuals.Apply failed: " + ex.Message);
            }
        }

        internal static void Forget(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return;
            lock (Sync) AppliedByPointer.Remove(ptr);
        }

        /// <summary>Clears the applied-state so the next configure pass re-applies
        /// visuals (used after overlay toggles). Entries are also keyed by toggle
        /// state, so this is a fast-path rather than a requirement.</summary>
        internal static void InvalidateAll()
        {
            lock (Sync) AppliedByPointer.Clear();
        }

        private static void ApplyScale(GameObject root, ServerVariantSpec spec, bool wantScaled)
        {
            try
            {
                var t = root.transform;
                if (t == null) return;
                float targetY = wantScaled ? spec.ScaleY : 1f;
                Vector3 s = t.localScale;
                if (Math.Abs(s.x - 1f) > 0.001f || Math.Abs(s.y - targetY) > 0.001f || Math.Abs(s.z - 1f) > 0.001f)
                {
                    t.localScale = new Vector3(1f, targetY, 1f);
                    if (ModConfig.VerboseLogging)
                        Log.Info($"Visual scale for {spec.VariantDisplayName}: Y {s.y:0.00} -> {targetY:0.00} (visual only, slots unchanged).");
                }
            }
            catch { /* best-effort */ }
        }

        private static void ApplyTint(GameObject root, ServerVariantSpec spec)
        {
            Renderer[] renderers = null;
            try { renderers = root.GetComponentsInChildren<Renderer>(true); } catch { return; }
            if (renderers == null) return;
            if (renderers.Length > MaxRenderersForTint)
            {
                Log.Warning($"Tint {spec.VariantDisplayName} uebersprungen: {renderers.Length} Renderer " +
                    $"unter '{root.name}' (kein einzelner Server?).");
                return;
            }

            // Diagnose immer beim ersten Durchlauf pro Spec (nicht nur verbose):
            // Ohne Materialnamen raten wir blind.
            bool discoveryLogged = false;
            int tinted = 0;
            Renderer fallbackRend = null;
            int fallbackMat = -1;
            float fallbackSize = 0f;
            foreach (var rend in renderers)
            {
                if (rend == null) continue;
                Material[] mats = null;
                try { mats = rend.materials; } catch { continue; }
                if (mats == null) continue;
                bool changed = false;

                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    string matName = "";
                    try { matName = mat.name ?? ""; } catch { continue; }

                    if (!discoveryLogged)
                    {
                        discoveryLogged = true;
                        Log.Info($"Material discovery for {spec.VariantDisplayName} on '{root.name}': {DescribeMaterials(renderers)}");
                    }

                    if (IsExcluded(matName)) continue;

                    // Fallback-Kandidat: groesstes nicht-exkludiertes Mesh
                    // (Body ist praktisch immer das groesste Teil).
                    try
                    {
                        float size = rend.bounds.size.magnitude;
                        if (size > fallbackSize) { fallbackSize = size; fallbackRend = rend; fallbackMat = m; }
                    }
                    catch { }

                    if (!ShouldTint(mat, matName, spec)) continue;

                    foreach (var prop in ColorProps)
                    {
                        bool has = false;
                        try { has = mat.HasProperty(prop); } catch { /* best-effort */ }
                        if (!has) continue;
                        Color current = Color.white;
                        try { current = mat.GetColor(prop); } catch { continue; }
                        if (ColorsClose(current, spec.TintColor, 0.02f)) continue;
                        try { mat.SetColor(prop, spec.TintColor); tinted++; } catch { /* best-effort */ }
                    }
                    changed = true;
                }

                if (changed)
                {
                    try { rend.materials = mats; } catch { /* best-effort */ }
                }
            }

            // Fallback: nichts passte per Name/Naehe -> groesstes Mesh faerben.
            if (tinted == 0 && fallbackRend != null)
            {
                try
                {
                    Material[] mats = fallbackRend.materials;
                    if (mats != null && fallbackMat >= 0 && fallbackMat < mats.Length && mats[fallbackMat] != null)
                    {
                        var mat = mats[fallbackMat];
                        foreach (var prop in ColorProps)
                        {
                            bool has = false;
                            try { has = mat.HasProperty(prop); } catch { continue; }
                            if (!has) continue;
                            try { mat.SetColor(prop, spec.TintColor); tinted++; break; } catch { }
                        }
                        if (tinted > 0)
                        {
                            try { fallbackRend.materials = mats; } catch { }
                            Log.Info($"Tint-Fallback {spec.VariantDisplayName}: groesstes Mesh '{mat.name}' gefaerbt.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Tint-Fallback {spec.VariantDisplayName} fehlgeschlagen: {ex.Message}");
                }
            }

            if (tinted > 0)
                Log.Info($"Tinted {tinted} material slot(s) for {spec.VariantDisplayName} -> {spec.TintColor}.");
            else
                Log.Warning($"Tint {spec.VariantDisplayName}: kein Material passte (siehe discovery oben).");
        }

        private static bool ShouldTint(Material mat, string matName, ServerVariantSpec spec)
        {
            string lower = matName.ToLowerInvariant();
            foreach (var word in FamilyColorWords)
            {
                if (lower.Contains(word))
                    return true;
            }
            foreach (var prop in ColorProps)
            {
                bool has = false;
                try { has = mat.HasProperty(prop); } catch { continue; }
                if (!has) continue;
                Color c;
                try { c = mat.GetColor(prop); } catch { continue; }
                if (Saturation(c) < MinSaturation) continue;
                if (ColorDistance(c, spec.FamilyBaseColor) < ProximityThreshold)
                    return true;
            }
            return false;
        }

        private static bool IsExcluded(string matName)
        {
            string lower = matName.ToLowerInvariant();
            foreach (var part in ExcludedNameParts)
            {
                if (lower.Contains(part)) return true;
            }
            return false;
        }

        private static string DescribeMaterials(Renderer[] renderers)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var rend in renderers)
            {
                if (rend == null) continue;
                Material[] mats = null;
                try { mats = rend.materials; } catch { continue; }
                if (mats == null) continue;
                foreach (var mat in mats)
                {
                    if (mat == null) continue;
                    string name = "";
                    try { name = mat.name ?? "?"; } catch { name = "?"; }
                    parts.Add(name);
                    if (parts.Count >= 24) break;
                }
                if (parts.Count >= 24) break;
            }
            return string.Join("; ", parts);
        }

        private static float Saturation(Color c)
        {
            float max = Math.Max(c.r, Math.Max(c.g, c.b));
            float min = Math.Min(c.r, Math.Min(c.g, c.b));
            if (max <= 0f) return 0f;
            return (max - min) / max;
        }

        private static float ColorDistance(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            return (float)Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static bool ColorsClose(Color a, Color b, float eps)
        {
            return Math.Abs(a.r - b.r) < eps && Math.Abs(a.g - b.g) < eps && Math.Abs(a.b - b.b) < eps;
        }
    }
}
