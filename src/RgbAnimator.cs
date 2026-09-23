using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace GregMod.Backplanes
{
    /// <summary>
    /// RGB-Streifen-Effekt: faerbt getrackte Material-Slots pro Frame mit
    /// rotierendem Hue (voller Farbkreis, ~8 s). Nur fuer Specs mit
    /// RgbAnimated; normale Tints bleiben statisch. Tote Server fallen
    /// automatisch aus dem Tracking (Liveness-Check).
    /// </summary>
    internal static class RgbAnimator
    {
        private sealed class Slot
        {
            public Material Mat;
            public string Prop;
        }

        private sealed class Entry
        {
            public Server Server;
            public List<Slot> Slots = new();
            public float Offset;
        }

        private static readonly Dictionary<IntPtr, Entry> Tracked = new();
        private static readonly object Sync = new();

        // Voller Hue-Umlauf in Sekunden.
        private const float CycleSeconds = 8f;

        internal static void Track(Server server, List<(Renderer, Material, string)> slots)
        {
            if (server == null || slots == null || slots.Count == 0) return;
            IntPtr ptr = IntPtr.Zero;
            try { ptr = server.Pointer; } catch { return; }
            if (ptr == IntPtr.Zero) return;

            var entry = new Entry { Server = server };
            try { entry.Offset = (Math.Abs(ptr.GetHashCode()) % 1000) / 1000f; } catch { entry.Offset = 0f; }
            foreach (var (rend, mat, prop) in slots)
            {
                if (rend == null || mat == null || string.IsNullOrEmpty(prop)) continue;
                try
                {
                    var _ = rend.gameObject; // Liveness
                    entry.Slots.Add(new Slot { Mat = mat, Prop = prop });
                }
                catch { /* best-effort */ }
            }

            if (entry.Slots.Count == 0) return;
            lock (Sync) Tracked[ptr] = entry;
        }

        internal static void Drop(Server server)
        {
            if (server == null) return;
            try
            {
                var ptr = server.Pointer;
                if (ptr == IntPtr.Zero) return;
                lock (Sync) Tracked.Remove(ptr);
            }
            catch { /* best-effort */ }
        }

        internal static void Tick()
        {
            List<IntPtr> dead = null;
            float hueBase;
            try { hueBase = (Time.unscaledTime % CycleSeconds) / CycleSeconds; }
            catch { return; }

            lock (Sync)
            {
                if (Tracked.Count == 0) return;
                foreach (var kv in Tracked)
                {
                    var entry = kv.Value;
                    if (entry == null || entry.Server == null || entry.Slots == null || entry.Slots.Count == 0)
                    {
                        (dead ??= new List<IntPtr>()).Add(kv.Key);
                        continue;
                    }

                    bool alive = false;
                    try { var _ = entry.Server.gameObject; alive = true; } catch { alive = false; }
                    if (!alive)
                    {
                        (dead ??= new List<IntPtr>()).Add(kv.Key);
                        continue;
                    }

                    float hue = (hueBase + entry.Offset) % 1f;
                    Color col;
                    try { col = Color.HSVToRGB(hue, 1f, 1f); } catch { continue; }
                    foreach (var slot in entry.Slots)
                    {
                        if (slot?.Mat == null || string.IsNullOrEmpty(slot.Prop)) continue;
                        try { slot.Mat.SetColor(slot.Prop, col); } catch { /* Slot tot -> naechster Tick */ }
                    }
                }

                if (dead != null)
                    foreach (var ptr in dead) Tracked.Remove(ptr);
            }
        }
    }
}
