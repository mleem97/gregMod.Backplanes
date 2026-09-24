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

        // Plausibility: a single server has only a handful of renderers.
        // Clearly more hints at a wrong root object (room/rack row)
        // - then rather touch nothing than recolor the scene.
        private const int MaxRenderersForTint = 24;

        private static readonly string[] FamilyColorWords = { "yellow", "blue", "purple", "green" };

        // Exact body material names per family (from live discovery, 18:46 log).
        // Compare lowercase, without " (Instance)" suffix. Families
        // without entry fall back to proximity matching; once their
        // discovery line is in the log, add it here.
        private static readonly Dictionary<string, string[]> ExactBodyMaterials =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "systemx", new[] { "brushedaluminiumyellow", "yellow" } },
            };

        private static readonly HashSet<string> DiscoveryLogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                // Plausibility: too many renderers = wrong root (room instead
                // of server). Neither scale nor tint in that case.
                try
                {
                    var all = root.GetComponentsInChildren<Renderer>(true);
                    if (all != null && all.Length > MaxRenderersForTint)
                    {
                        Log.Warning($"Visuals {spec.VariantDisplayName} skipped: {all.Length} renderers " +
                            $"under '{root.name}' (not a single server?).");
                        lock (Sync) AppliedByPointer[ptr] = wantKey;
                        return;
                    }
                }
                catch { }

                if (wantScale) ApplyScale(root, spec, true);
                else ApplyScale(root, spec, false); // live restore when toggled off
                if (wantTint) ApplyTint(server, root, spec);
                else RgbAnimator.Drop(server);
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

        private static void ApplyTint(Il2Cpp.Server server, GameObject root, ServerVariantSpec spec)
        {
            Renderer[] renderers = null;
            try { renderers = root.GetComponentsInChildren<Renderer>(true); } catch { return; }
            if (renderers == null) return;
            if (renderers.Length > MaxRenderersForTint)
            {
                Log.Warning($"Tint {spec.VariantDisplayName} skipped: {renderers.Length} renderers " +
                    $"under '{root.name}' (not a single server?).");
                return;
            }

            // Diagnostics once per family (not per configure): names are
            // stable per family, any further line would be spam. Models AND
            // materials: model inventory shows what the server is made of
            // (replacement base), materials serve tint matching.
            lock (Sync)
            {
                if (DiscoveryLogged.Add(spec.FamilyKey))
                {
                    Log.Info($"Model discovery for {spec.FamilyKey} on '{root.name}': {DescribeModels(root)}");
                    Log.Info($"Material discovery for {spec.FamilyKey} on '{root.name}': {DescribeMaterials(renderers)}");
                }
            }
            int tinted = 0;
            Renderer fallbackRend = null;
            int fallbackMat = -1;
            float fallbackSize = 0f;
            System.Collections.Generic.List<(Renderer, Material, string)> rgbSlots = null;
            if (spec.RgbAnimated)
                rgbSlots = new System.Collections.Generic.List<(Renderer, Material, string)>();
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

                    if (IsExcluded(matName)) continue;

                    // Fallback candidate: largest non-excluded mesh
                    // (body is practically always the largest part).
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
                        try
                        {
                            mat.SetColor(prop, spec.TintColor);
                            tinted++;
                            rgbSlots?.Add((rend, mat, prop));
                        }
                        catch { /* best-effort */ }
                    }
                    changed = true;
                }

                if (changed)
                {
                    try { rend.materials = mats; } catch { /* best-effort */ }
                }
            }

            // Fallback: nothing matched by name/proximity -> tint largest mesh.
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
                            try
                            {
                                mat.SetColor(prop, spec.TintColor);
                                tinted++;
                                rgbSlots?.Add((fallbackRend, mat, prop));
                                break;
                            }
                            catch { }
                        }
                        if (tinted > 0)
                        {
                            try { fallbackRend.materials = mats; } catch { }
                            Log.Info($"Tint fallback {spec.VariantDisplayName}: largest mesh '{mat.name}' tinted.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Tint fallback {spec.VariantDisplayName} failed: {ex.Message}");
                }
            }

            if (tinted > 0)
                Log.Info($"Tinted {tinted} material slot(s) for {spec.VariantDisplayName} -> {spec.TintColor}.");
            else
                Log.Warning($"Tint {spec.VariantDisplayName}: no material matched (see discovery above).");

            try
            {
                if (spec.RgbAnimated && rgbSlots != null && rgbSlots.Count > 0)
                    RgbAnimator.Track(server, rgbSlots);
            }
            catch { /* best-effort */ }
        }

        private static bool ShouldTint(Material mat, string matName, ServerVariantSpec spec)
        {
            // Exact hit first (deterministic, no guessing).
            try
            {
                string norm = matName ?? "";
                int inst = norm.IndexOf(" (Instance)", StringComparison.OrdinalIgnoreCase);
                if (inst >= 0) norm = norm.Substring(0, inst);
                norm = norm.Trim().ToLowerInvariant();
                if (norm.Length > 0 && ExactBodyMaterials.TryGetValue(spec.FamilyKey, out var exact))
                {
                    foreach (var name in exact)
                    {
                        if (norm == name) return true;
                    }
                }
            }
            catch { /* fallback below */ }
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

        /// <summary>
        /// Model inventory: hierarchy paths + mesh names + renderer loadout.
        /// Shows which models/assets a server is made of (replacement base).
        /// Capped at 64 nodes / depth 6 — servers are flat, racks/rooms are not.
        /// </summary>
        private static string DescribeModels(GameObject root)
        {
            var parts = new System.Collections.Generic.List<string>();
            try
            {
                var queue = new System.Collections.Generic.Queue<(Transform t, int depth, string path)>();
                if (root == null || root.transform == null) return "<no root>";
                queue.Enqueue((root.transform, 0, root.name ?? "?"));
                while (queue.Count > 0 && parts.Count < 64)
                {
                    var (t, depth, path) = queue.Dequeue();
                    if (t == null || depth > 6) continue;
                    string extra = "";
                    try
                    {
                        var mf = t.gameObject != null ? t.gameObject.GetComponent<MeshFilter>() : null;
                        if (mf != null && mf.sharedMesh != null)
                            extra += "[Mesh:" + (mf.sharedMesh.name ?? "?") + "]";
                    }
                    catch { }
                    try
                    {
                        var rend = t.gameObject != null ? t.gameObject.GetComponent<Renderer>() : null;
                        if (rend != null)
                        {
                            int n = 0;
                            try { var mats = rend.sharedMaterials; if (mats != null) n = mats.Length; } catch { }
                            extra += "[Renderer:" + n + "mats]";
                        }
                    }
                    catch { }
                    parts.Add(path + extra);
                    try
                    {
                        for (int i = 0; i < t.childCount; i++)
                        {
                            var c = t.GetChild(i);
                            if (c == null) continue;
                            queue.Enqueue((c, depth + 1, path + "/" + (c.gameObject != null ? c.gameObject.name ?? "?" : "?")));
                        }
                    }
                    catch { }
                }
            }
            catch (System.Exception ex)
            {
                parts.Add("<Abbruch: " + ex.Message + ">");
            }
            return string.Join(" | ", parts);
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
