using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GregMod.Backplanes
{
    /// <summary>
    /// Persists serverId -&gt; variantId markers across saves (sidecar TSV).
    ///
    /// v1.x failure modes fixed here:
    /// <list type="bullet">
    /// <item>Ambiguous columns: v1.0.0 wrote variant IDs into the server-ID column,
    ///   v1.0.1 then dropped EVERY row as "polluted" and the registry stayed empty,
    ///   so save-loaded servers silently fell back to 12K IOPS / 1G. The loader is
    ///   now column-order tolerant: whichever column resolves to a known variant wins.</item>
    /// <item>Silent drops: every ignored row is counted and logged with its reason.</item>
    /// <item>Legacy prefixes (dc_automator_*, bbs_*, …) resolve to canonical
    ///   greg_backplanes_* IDs and are normalized on the next save.</item>
    /// <item>Non-atomic writes (partial TSV on crash) replaced by tmp-file + replace
    ///   with a .bak of the previous state.</item>
    /// </list>
    /// </summary>
    internal sealed class RuntimeVariantRegistry
    {
        private readonly Dictionary<string, string> _serverVariants =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly object _sync = new object();
        private bool _loaded;

        private static readonly string[] KnownBaseTokens =
        {
            "Server_Yellow1", "Server_Yellow2", "Server_Blue1", "Server_Blue2",
            "Server_Purple1", "Server_Purple2", "Server_Green1", "Server_Green2",
            "Server.Yellow1", "Server.Yellow2", "Server.Blue1", "Server.Blue2",
            "Server.Purple1", "Server.Purple2", "Server.Green1", "Server.Green2",
        };

        internal int Count
        {
            get { lock (_sync) { EnsureLoadedLocked(); return _serverVariants.Count; } }
        }

        private static string GameRoot
        {
            get
            {
                string[] candidates = { AppContext.BaseDirectory, AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory };
                foreach (string dir in candidates)
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        string full = Path.GetFullPath(dir);
                        if (File.Exists(Path.Combine(full, "Data Center.exe")))
                            return full;
                    }
                    catch { /* fall through */ }
                }
                return Path.GetFullPath(AppContext.BaseDirectory);
            }
        }

        private static string RegistryPath => Path.Combine(GameRoot, "UserData", "BackplaneBoostServers", "server-variants.tsv");
        private static string LegacyRegistryPath => Path.Combine(GameRoot, "UserData", "DataCenterAutomatorServers", "server-variants.tsv");

        internal void Load()
        {
            lock (_sync) EnsureLoadedLocked();
        }

        /// <summary>Serialisiert Marker fuer Sidecar-Ablage (GregSaveGuard).</summary>
        internal string Serialize()
        {
            lock (_sync)
            {
                EnsureLoadedLocked();
                var lines = new System.Collections.Generic.List<string>();
                lines.Add("# serverId\tvariantId");
                foreach (var p in _serverVariants.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                    lines.Add(p.Key + "\t" + p.Value);
                return string.Join("\n", lines.ToArray());
            }
        }

        /// <summary>Laedt Marker aus Sidecar-Content (GregSaveGuard).</summary>
        internal void Deserialize(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            lock (_sync)
            {
                EnsureLoadedLocked();
                int kept = 0;
                foreach (string raw in content.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] cols = line.Split('\t');
                    if (cols.Length < 2) continue;
                    string a = cols[0].Trim();
                    string b = cols[1].Trim();
                    ServerVariantSpec spec = ServerVariantSpec.FindById(b) ?? ServerVariantSpec.FindById(a);
                    if (spec == null) continue;
                    string serverId = ReferenceEquals(spec, ServerVariantSpec.FindById(b)) ? a : b;
                    if (serverId.Length == 0) continue;
                    _serverVariants[serverId] = spec.VariantId;
                    kept++;
                }
                if (kept > 0)
                    Log.Info($"Loaded {kept} marker(s) from save sidecar.");
            }
        }


        internal ServerVariantSpec Get(string serverId)
        {
            lock (_sync)
            {
                EnsureLoadedLocked();
                if (string.IsNullOrWhiteSpace(serverId)) return null;
                return _serverVariants.TryGetValue(serverId, out string variantId)
                    ? ServerVariantSpec.FindById(variantId)
                    : null;
            }
        }

        internal bool Set(string serverId, ServerVariantSpec spec)
        {
            lock (_sync)
            {
                EnsureLoadedLocked();
                if (string.IsNullOrWhiteSpace(serverId) || spec == null) return false;
                if (_serverVariants.TryGetValue(serverId, out string existing) &&
                    string.Equals(existing, spec.VariantId, StringComparison.OrdinalIgnoreCase))
                    return false;
                _serverVariants[serverId] = spec.VariantId;
                SaveLocked();
                return true;
            }
        }

        internal void Forget(string serverId)
        {
            lock (_sync)
            {
                EnsureLoadedLocked();
                if (!string.IsNullOrWhiteSpace(serverId) && _serverVariants.Remove(serverId))
                    SaveLocked();
            }
        }

        private void EnsureLoadedLocked()
        {
            if (_loaded) return;
            _loaded = true;

            string path = RegistryPath;
            string legacy = LegacyRegistryPath;
            string source = File.Exists(path) ? path : legacy;
            if (!File.Exists(source)) return;

            try
            {
                int kept = 0, unknownVariant = 0, wrongBase = 0, malformed = 0;
                foreach (string raw in File.ReadAllLines(source))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] cols = line.Split('\t');
                    if (cols.Length < 2) { malformed++; continue; }

                    string a = cols[0].Trim();
                    string b = cols[1].Trim();

                    // Column-order tolerant: whichever side resolves to a variant wins.
                    ServerVariantSpec spec = ServerVariantSpec.FindById(b) ?? ServerVariantSpec.FindById(a);
                    if (spec == null) { unknownVariant++; continue; }

                    string serverId = ReferenceEquals(spec, ServerVariantSpec.FindById(b)) ? a : b;
                    if (serverId.Length == 0) { malformed++; continue; }

                    if (ServerIdLooksLikeDifferentBase(serverId, spec)) { wrongBase++; continue; }

                    _serverVariants[serverId] = spec.VariantId;
                    kept++;
                }

                Log.Info($"Loaded {kept.ToString(CultureInfo.InvariantCulture)} persisted server variant marker(s) from {source}.");
                if (unknownVariant > 0) Log.Warning($"Ignored {unknownVariant} marker(s) with unknown variant IDs.");
                if (wrongBase > 0) Log.Warning($"Ignored {wrongBase} marker(s) pointing at a different base server family.");
                if (malformed > 0) Log.Warning($"Ignored {malformed} malformed marker line(s).");

                // Normalize legacy files / legacy IDs into the canonical location + IDs.
                if (!string.Equals(source, path, StringComparison.OrdinalIgnoreCase) || unknownVariant > 0 || wrongBase > 0)
                {
                    SaveLocked();
                    Log.Info("Normalized persisted server variant markers to " + path + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Could not load server variant registry '" + source + "': " + ex.Message);
            }
        }

        private void SaveLocked()
        {
            string path = RegistryPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                string[] lines = _serverVariants
                    .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(p => p.Key + "\t" + p.Value)
                    .Prepend("# serverId\tvariantId")
                    .ToArray();
                string tmp = path + ".tmp";
                File.WriteAllLines(tmp, lines);
                if (File.Exists(path))
                    File.Copy(path, path + ".bak", overwrite: true);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Log.Warning("Could not save server variant registry '" + path + "': " + ex.Message);
            }
        }

        private static bool ServerIdLooksLikeDifferentBase(string serverId, ServerVariantSpec spec)
        {
            if (!serverId.StartsWith("Server.", StringComparison.OrdinalIgnoreCase)) return false;
            string expected = spec.BaseRuntimeToken;
            return KnownBaseTokens.Any(token =>
                !string.Equals(token.Replace('_', '.'), expected.Replace('_', '.'), StringComparison.OrdinalIgnoreCase) &&
                serverId.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
