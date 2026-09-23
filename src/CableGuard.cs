using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using UnityEngine;

namespace GregMod.Backplanes
{
    /// <summary>
    /// Warn-only cable guidance for modded server ports.
    ///
    /// v1.x returned false from the CableLink.InteractOnClick prefix for
    /// "wrong" lane counts. Its lane heuristics misclassified cables, so ports
    /// ended up unconnectable (or dropped to 0G after power-on) and every load
    /// seemed to demand a different cable/SFP combination.
    ///
    /// v2 NEVER blocks: this check only logs and shows a throttled notification.
    /// Toggle via MelonPreferences: gregMod.Backplanes / CableWarnings.
    /// </summary>
    internal static class CableGuard
    {
        private static readonly Dictionary<string, DateTime> LastWarningByServer =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();

        private static readonly Dictionary<IntPtr, int> PreClickCableIds = new Dictionary<IntPtr, int>();

        /// <summary>Always returns true (never blocks the interaction).</summary>
        internal static bool CheckInteraction(CableLink link, CatalogInjector injector)
        {
            CapturePreClick(link);
            try
            {
                if (!ModConfig.CableWarnings || link == null) return true;
                if (link.typeOfLink != CableLink.TypeOfLink.Server) return true;
                if (link.parentServer == null) return true;

                string serverId = CatalogInjector.ReadServerId(link.parentServer);
                // Resolve via sweep-safe path: registry/inference without side effects.
                // (CatalogInjector.TryRepairServer would also fix; interaction time is
                // a good moment for a gentle re-assert, but never a block.)
                int lanes = ResolveHeldCableLanes();
                if (lanes < 0) return true; // unknown cable family: allow silently

                // We need the expected lanes; infer from the live port/server state.
                int expected = ExpectedLanesFor(link.parentServer, injector);
                if (expected < 0 || lanes == expected) return true;

                if (!Throttle(serverId)) return true;
                Log.Info($"Cable guidance: {serverId ?? "<unknown>"} works best with " +
                         (expected == 4 ? "4-lane fiber / QSFP module" : "1-lane fiber (SFP28)") +
                         $", held cable looks like {lanes}-lane. Connecting anyway — performance may be limited.");
                NotifyWrongCable();
                return true;
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging) Log.Warning("Cable guidance check failed: " + ex.Message);
                return true;
            }
        }

        private static int ExpectedLanesFor(Server server, CatalogInjector injector)
        {
            try
            {
                // Prefer the resolved variant spec (all tiers, including 1M/2M/4M).
                float speed = server.maxProcessingSpeed;
                foreach (var spec in ServerVariantSpec.All)
                {
                    if (Math.Abs(speed - spec.RuntimeProcessingSpeed) < 0.01f)
                        return spec.FiberLaneCount;
                }
                // Fallback: 100K (internal 1.0) is 1-lane; larger boosted tiers are QSFP 4-lane.
                if (Math.Abs(speed - 1.0f) < 0.01f) return 1;
                if (speed >= 4.0f) return 4;
                return -1;
            }
            catch { return -1; }
        }

        private static int ResolveHeldCableLanes()
        {
            try
            {
                var pm = PlayerManager.instance;
                if (pm == null) return -1;
                if (pm.objectInHand != PlayerManager.ObjectInHand.CableSpinner) return -1;
                var gos = pm.objectInHandGO;
                if (gos == null) return -1;
                foreach (var go in gos)
                {
                    if (go == null) continue;
                    CableSpinner spinner = null;
                    try { spinner = go.GetComponent<CableSpinner>(); } catch { /* best-effort */ }
                    if (spinner == null)
                    {
                        try
                        {
                            foreach (var s in go.GetComponentsInChildren<CableSpinner>(true)) { spinner = s; break; }
                        }
                        catch { /* best-effort */ }
                    }
                    if (spinner == null) continue;
                    string text = "";
                    try { text = (spinner.gameObject != null ? spinner.gameObject.name ?? "" : "") + " " + spinner.cableType; } catch { /* best-effort */ }
                    return LaneCountFromText(text);
                }
                return -1;
            }
            catch { return -1; }
        }

        private static int LaneCountFromText(string cableText)
        {
            if (string.IsNullOrWhiteSpace(cableText)) return -1;
            string lower = cableText.ToLowerInvariant();
            string norm = new string(cableText.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            if (norm.Contains("fiber4") || norm.Contains("fibre4") || norm.Contains("4lane") || norm.Contains("4lanes") || lower.Contains("qsfp")) return 4;
            if (norm.Contains("fiber1") || norm.Contains("fibre1") || norm.Contains("1lane") || norm.Contains("1lanes") || lower.Contains("sfp28")) return 1;
            if (norm.Contains("baset") || norm.Contains("rj45") || lower.Contains("copper") || lower.Contains("ethernet")) return 0;
            if (lower.Contains("fiber") || lower.Contains("fibre")) return 1;
            return -1;
        }

        private static bool Throttle(string serverId)
        {
            lock (Sync)
            {
                string key = serverId ?? "<unknown>";
                if (LastWarningByServer.TryGetValue(key, out DateTime last) &&
                    DateTime.UtcNow - last < TimeSpan.FromSeconds(60))
                    return false;
                LastWarningByServer[key] = DateTime.UtcNow;
                return true;
            }
        }

        private static void NotifyWrongCable()
        {
            try
            {
                var ui = StaticUIElements.instance;
                if (ui != null) ui.SetNotification(77, null, string.Empty);
            }
            catch { /* notification best-effort */ }
        }

        // ------------------------------------------------- click diagnostics
        //
        // The vanilla InteractOnClick body is a black box (IL2CPP stubs only), so a
        // rejected connection cannot be observed directly. Instead the prefix snapshots
        // cableIDsOnLink and this postfix compares: cable in hand + port still empty
        // afterwards = the game rejected the connection. The full port state is logged
        // (throttled) so reports contain actionable data instead of "sometimes RJ45".

        private static void CapturePreClick(CableLink link)
        {
            try
            {
                if (link == null) return;
                IntPtr ptr = IntPtr.Zero;
                try { ptr = link.Pointer; } catch { return; }
                if (ptr == IntPtr.Zero) return;
                int ids = 0;
                try { ids = link.cableIDsOnLink; } catch { /* best-effort */ }
                lock (Sync)
                {
                    PreClickCableIds[ptr] = ids;
                    if (PreClickCableIds.Count > 64) PreClickCableIds.Clear();
                }
            }
            catch { /* diagnostics never break gameplay */ }
        }

        internal static void DiagnosePostClick(CableLink link)
        {
            try
            {
                if (link == null) return;
                IntPtr ptr = IntPtr.Zero;
                try { ptr = link.Pointer; } catch { return; }
                if (ptr == IntPtr.Zero) return;

                int before = 0;
                bool hadPre = false;
                lock (Sync)
                {
                    hadPre = PreClickCableIds.TryGetValue(ptr, out before);
                    PreClickCableIds.Remove(ptr);
                }
                if (!hadPre) return;

                int after = 0;
                try { after = link.cableIDsOnLink; } catch { return; }
                if (before != 0 || after != 0) return; // something attached/changed: fine

                // Port still empty — only interesting when the player held a cable.
                string heldText = HeldCableText();
                if (string.IsNullOrEmpty(heldText)) return;

                string serverId = null;
                try { serverId = link.parentServer != null ? CatalogInjector.ReadServerId(link.parentServer) : null; } catch { /* best-effort */ }
                if (!Throttle("clickfail:" + (serverId ?? "unknown"))) return;

                Log.Warning($"Connection rejected on {serverId ?? "<unknown>"}: held='{heldText}', " +
                            $"port=[{DescribePort(link)}]. If this repeats, please report this line.");
            }
            catch { /* diagnostics never break gameplay */ }
        }

        private static string HeldCableText()
        {
            try
            {
                var pm = PlayerManager.instance;
                if (pm == null || pm.objectInHand != PlayerManager.ObjectInHand.CableSpinner) return null;
                var gos = pm.objectInHandGO;
                if (gos == null) return null;
                foreach (var go in gos)
                {
                    if (go == null) continue;
                    CableSpinner spinner = null;
                    try { spinner = go.GetComponent<CableSpinner>(); } catch { /* best-effort */ }
                    if (spinner == null) continue;
                    try { return ((spinner.gameObject != null ? spinner.gameObject.name ?? "" : "") + " cableType=" + spinner.cableType).Trim(); }
                    catch { return "cable-unknown"; }
                }
                return null;
            }
            catch { return null; }
        }

        private static string DescribePort(CableLink link)
        {
            try
            {
                string type = "?";
                try { type = link.typeOfLink.ToString(); } catch { /* best-effort */ }
                string speed = "?";
                try { speed = link.connectionSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); } catch { /* best-effort */ }
                string sfp = "?/?";
                try { sfp = link.sfpTypeSupported + "/" + link.sfpTypeInserted; } catch { /* best-effort */ }
                string flags = "?";
                try { flags = "sfp=" + link.isSFPPort + ",fibre=" + link.isFibrePort; } catch { /* best-effort */ }
                string module = "?";
                try { module = link.insertedSFP == null ? "<null>" : "<set>"; } catch { /* best-effort */ }
                return $"type={type},speed={speed},{flags},sfpSup/Ins={sfp},module={module}";
            }
            catch { return "<unreadable>"; }
        }
    }
}
