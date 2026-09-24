using System;
using System.Collections.Generic;
using Il2Cpp;

namespace GregMod.Backplanes
{
    /// <summary>
    /// Event-driven port speed memory — no polling. ConfigurePort registers
    /// each of our ports once (instance ID + target speed); CableLink event
    /// postfixes (InsertSFP / SetConnectionSpeed / plug actions) re-assert
    /// the speed on drift via direct field write (bypasses native clamps,
    /// no recursion). Vanilla servers are never registered → zero impact.
    /// Stale entries (destroyed cables, recycled instance IDs) self-clean
    /// on access via reference check; oversized maps get swept in Register.
    /// </summary>
    internal static class PortSpeedMemory
    {
        private sealed class Entry
        {
            public CableLink Link;
            public float Speed;
        }

        private static readonly Dictionary<int, Entry> _speeds = new();
        private const int SweepThreshold = 512;

        public static void Register(CableLink link, float speed)
        {
            try
            {
                if (link == null || speed <= 0f) return;
                int id;
                try { id = link.GetInstanceID(); } catch { return; }
                lock (_speeds)
                {
                    _speeds[id] = new Entry { Link = link, Speed = speed };
                    if (_speeds.Count >= SweepThreshold) SweepLocked();
                }
            }
            catch { }
        }

        public static void Clear()
        {
            try { lock (_speeds) { _speeds.Clear(); } } catch { }
        }

        public static void Enforce(CableLink link, string source)
        {
            try
            {
                if (link == null) return;
                if (!TryGetSpeed(link, out float want) || want <= 0f) return;
                float live;
                try { live = link.connectionSpeed; } catch { return; }
                if (Math.Abs(live - want) < 0.001f) return;
                try { link.connectionSpeed = want; }
                catch { return; }
                string name = "";
                try
                {
                    var go = link.gameObject;
                    if (go != null) name = go.name;
                }
                catch { }
                Log.Info($"PortSpeed [{source}]: {live:F0} -> {want:F0} ({name}).");
            }
            catch { }
        }

        private static bool TryGetSpeed(CableLink link, out float speed)
        {
            speed = 0f;
            try
            {
                if (link == null) return false;
                int id;
                try { id = link.GetInstanceID(); } catch { return false; }
                Entry e;
                lock (_speeds)
                {
                    if (!_speeds.TryGetValue(id, out e)) return false;
                }
                if (e == null) { Remove(id); return false; }
                CableLink stored;
                try { stored = e.Link; } catch { Remove(id); return false; }
                bool same;
                try { same = stored == link; }
                catch { same = false; }
                if (!same) { Remove(id); return false; }
                speed = e.Speed;
                return speed > 0f;
            }
            catch { return false; }
        }

        private static void Remove(int id)
        {
            try { lock (_speeds) { _speeds.Remove(id); } } catch { }
        }

        // Only call with _speeds already locked.
        private static void SweepLocked()
        {
            try
            {
                List<int> dead = null;
                foreach (var kv in _speeds)
                {
                    bool alive = false;
                    try
                    {
                        var l = kv.Value != null ? kv.Value.Link : null;
                        alive = l != null;
                    }
                    catch { alive = false; }
                    if (!alive)
                    {
                        if (dead == null) dead = new List<int>();
                        dead.Add(kv.Key);
                    }
                }
                if (dead != null)
                    foreach (var d in dead) _speeds.Remove(d);
            }
            catch { }
        }
    }
}
