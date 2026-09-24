using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace GregMod.Backplanes
{
    /// <summary>
    /// Typed-first refactor of the v1.x reflection injector.
    ///
    /// What changed vs BackplaneBoostServers v1.0.1:
    /// <list type="bullet">
    /// <item>Direct Il2Cpp types everywhere on the hot path (Server, CableLink,
    ///   ComputerShop, ShopItem/SO). The stringly-typed ReflectionUtil graph scans
    ///   (ScanObjectGraph / EnumerateLikelyStaticRoots / FindObjectsOfTypeAll via
    ///   reflection) are gone — shop registration is a single typed scan of
    ///   ComputerShop.shopItems. This removes the shop-open lag and the
    ///   cross-mod interference (custom color cables/racks, Svc Service).</item>
    /// <item>The ShopCartItem.AddSpawnedItem hook of v1.x is DEAD CODE in the
    ///   current game build (no such method exists, Harmony silently skipped it),
    ///   so bought servers were never configured at spawn time. v2 configures
    ///   spawns in the live ComputerShop.SpawnPhysicalItem postfix instead.</item>
    /// <item>Save/load repair is a time-boxed sweep (Server.Start/Awake/
    ///   OnLoadingComplete postfixes + 1/sec FindObjectsOfType sweep) instead of
    ///   an always-on repair that fought live links. No Server.Awake/Start work
    ///   happens outside the window, so no per-frame cost and no recursion.</item>
    /// <item>All repair entry is guarded by RepairGuard (native-pointer keyed),
    ///   fixing the 0xC00000FD stack-overflow crash on saves with modded servers.</item>
    /// <item>Connected ports (cableIDsOnLink != 0 or insertedSFP set) are never
    ///   rewritten — fixes ports dropping to 0G and refusing cables after reload.</item>
    /// <item>Cable-family enforcement is warn-only and NEVER blocks
    ///   CableLink.InteractOnClick (always returns true).</item>
    /// <item>Spec resolution is strict name-based; the v1.x price-only fallback
    ///   (phantom cart duplicates, vanilla servers misconfigured) is removed.</item>
    /// <item>Registry load is column-order tolerant and migrates legacy
    ///   dc_automator_*/bbs_* IDs to canonical greg_backplanes_* IDs.</item>
    /// </list>
    /// </summary>
    internal sealed class CatalogInjector
    {
        private readonly RuntimeVariantRegistry _registry = new RuntimeVariantRegistry();

        // variantId -> registered (per scene)
        private readonly HashSet<string> _registeredIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Varianten-ItemID (9001-9020) -> Base-ItemID (fuer GetPrefabForItem-Mapping).
        private readonly Dictionary<int, int> _variantToBaseId = new Dictionary<int, int>();

        // Pending purchases awaiting rack insertion (bounded, expiring).
        private readonly Queue<PendingInsertion> _pendingInsertions = new Queue<PendingInsertion>();

        // Checkout-Snapshot: exakte Spec pro Unit in Cart-Reihenfolge.
        // Ueberlebt Bulk-Kaeufe (30+ Units, gemischte Preispunkte), wo der
        // reine Preis-Peek (PeekPendingSpecForSpawn) mehrdeutig waere.
        // Wird im SpawnAll-Prefix aufgebaut und pro SpawnPhysicalItem
        // genau einmal konsumiert; VerifyCheckout meldet Abweichungen.
        private readonly Queue<ServerVariantSpec> _checkoutSpecQueue = new Queue<ServerVariantSpec>();
        private int _checkoutExpectedUnits;
        private int _checkoutVariantSpawned;

        // Spawn uid -> spec (correlated via ComputerShop.spawnedItems).
        private readonly Dictionary<int, ServerVariantSpec> _spawnedSpecsByUid = new Dictionary<int, ServerVariantSpec>();

        // Spawn uid -> Erstellzeitpunkt: verwaiste UIDs laufen nach 60 s aus,
        // damit der 1/sec-Sweep nicht dauerhaft weiterlaeuft (Lag/Repair-Loop).
        private readonly Dictionary<int, DateTime> _spawnedUidCreatedAt = new Dictionary<int, DateTime>();

        // Native server pointer -> spec, recorded at spawn-configure time.
        private readonly Dictionary<IntPtr, ServerVariantSpec> _pendingSpecsByPointer = new Dictionary<IntPtr, ServerVariantSpec>();

        // serverId strings already repaired this scene (stable string keys are fine here —
        // this is a done-marker, not a re-entrancy guard).
        private readonly HashSet<string> _repairedServerIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private DateTime _repairWindowUntil = DateTime.MinValue;
        private DateTime _lastSweepAt = DateTime.MinValue;
        private bool _sweepSummaryLogged;
        private bool _shopDumped;
        private int _sweepRepaired;

        // Watchlist: erfolgreich konfigurierte Varianten-Server. Wird alle 5 s
        // geprueft (auch ausserhalb des Repair-Fensters): Falls das Spiel Werte
        // nachtraeglich zuruecksetzt, wird sofort neu konfiguriert + geloggt.
        // Managed Wrapper halten (zerstoerte werfen beim Zugriff -> aussortieren).
        private static readonly List<(IntPtr ptr, Server server, ServerVariantSpec spec)> _watched =
            new List<(IntPtr, Server, ServerVariantSpec)>();
        private static DateTime _lastWatchAt = DateTime.MinValue;

        private sealed class PendingInsertion
        {
            internal ServerVariantSpec Spec;
            internal DateTime CreatedAt;
        }

        // ------------------------------------------------------------------ scene

        internal void ResetForScene()
        {
            _registeredIds.Clear();
            _variantToBaseId.Clear();
            _shopDumped = false;
            lock (_watched) { _watched.Clear(); }
            _lastWatchAt = DateTime.MinValue;
            _pendingInsertions.Clear();
            _checkoutSpecQueue.Clear();
            _checkoutExpectedUnits = 0;
            _checkoutVariantSpawned = 0;
            _spawnedSpecsByUid.Clear();
            _spawnedUidCreatedAt.Clear();
            _pendingSpecsByPointer.Clear();
            _repairedServerIds.Clear();
            _sweepSummaryLogged = false;
            _sweepRepaired = 0;
        }

        internal void BeginRepairWindow()
        {
            try
            {
                _registry.Load();
                int count = _registry.Count;
                if (count > 0)
                    Log.Info($"Loaded {count} persisted server variant marker(s).");
                _repairWindowUntil = DateTime.UtcNow.AddSeconds(ModConfig.RepairWindowSeconds);
                _lastSweepAt = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                Log.Error("BeginRepairWindow failed.", ex);
            }
        }

        internal bool InRepairWindow => DateTime.UtcNow < _repairWindowUntil;

        // ------------------------------------------------------- overlay status

        internal int RegisteredCount => _registeredIds.Count;
        internal int RepairedCount => _sweepRepaired;
        internal string LastVerifySummary { get; private set; } = "Verify: noch nicht gelaufen.";
        internal int LastVerifyOk { get; private set; }
        internal int LastVerifyMismatch { get; private set; }
        internal int LastVerifyUnknown { get; private set; }
        internal int RegistryCount
        {
            get
            {
                try { return _registry.Count; }
                catch { return -1; }
            }
        }

        /// <summary>
        /// Called after overlay visual toggles: drops the applied-visual cache and
        /// reopens the repair sweep so tint/scale changes take effect within seconds
        /// (scale-off restores live; tint-off applies going forward / after reload).
        /// </summary>
        internal void RefreshVisualsAfterToggle(string whatChanged)
        {
            try
            {
                ServerVisuals.InvalidateAll();
                BeginRepairWindow();
                Log.Info($"Visuals refresh after {whatChanged}: re-applying within the repair window.");
            }
            catch (Exception ex)
            {
                Log.Error("Visuals refresh failed.", ex);
            }
        }

        /// <summary>Called from OnUpdate; cheap timestamp check, scans only inside the window, once per second.</summary>
        internal void Tick()
        {
            try
            {
                // Watchlist laeuft IMMER (alle 5 s, intern gedrosselt): So wird
                // sichtbar wenn das Spiel Werte zuruecksetzt - und sofort
                // repariert, egal in welchem Fenster wir sind.
                try { TickWatchlist(); } catch (Exception ex) { Log.Warning("Watchlist-Tick: " + ex.Message); }
                // Pending-Kaeufe halten den Sweep am Leben (auch ausserhalb des
                // Fensters): frisch gekaufte Varianten konvergieren so immer,
                // selbst wenn das Insert-Event verpasst wurde.
                bool hasPending = _pendingInsertions.Count > 0 || _spawnedSpecsByUid.Count > 0;
                if (!InRepairWindow && !hasPending)
                {
                    if (!_sweepSummaryLogged)
                    {
                        _sweepSummaryLogged = true;
                        if (_sweepRepaired > 0)
                            Log.Info($"Repair window closed, {_sweepRepaired} persisted server(s) restored.");
                    }
                    return;
                }
                if (DateTime.UtcNow - _lastSweepAt < TimeSpan.FromSeconds(1.0)) return;
                _lastSweepAt = DateTime.UtcNow;
                SweepAllServers();
                DrainSpawnedSpecs();
            }
            catch (Exception ex)
            {
                Log.Error("Repair tick failed.", ex);
            }
        }

        private void SweepAllServers()
        {
            Server[] servers;
            try
            {
                servers = UnityEngine.Object.FindObjectsOfType<Server>();
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging) Log.Warning("Server sweep lookup failed: " + ex.Message);
                return;
            }
            if (servers == null) return;
            foreach (var server in servers)
            {
                if (server == null) continue;
                TryRepairServer("sweep", server, onlyIfKnown: true);
            }
        }

        // ------------------------------------------------------- shop registration

        /// <summary>
        /// Schreibt Varianten-Texte auf ALLE Shopkarten (nach Vanilla-Refresh).
        /// Das Spiel rendert Namen per eigenem ID-Lookup darueber - daher nach
        /// jedem Oeffnen erneut setzen, nicht nur beim Klonen.
        /// </summary>
        internal void RefreshVariantCardTexts(string source, ComputerShop shop)
        {
            try
            {
                if (shop?.shopItems == null) return;
                int fixed_ = 0;
                foreach (var item in shop.shopItems)
                {
                    if (item == null) continue;
                    int id = 0;
                    try { id = item.shopItemSO != null ? item.shopItemSO.itemID : 0; } catch { continue; }
                    var spec = ServerVariantSpec.FindByVariantItemId(id);
                    if (spec == null) continue;
                    string label = $"{spec.VariantDisplayName} ({spec.RecommendedCable})";
                    try { item.itemDisplayName = label; } catch { }
                    try { if (item.txtName != null) item.txtName.text = label; } catch { }
                    try { if (item.txtPrice != null) item.txtPrice.text = $"{spec.Price} $"; } catch { }
                    try { if (item.txtXpToUnlock != null) item.txtXpToUnlock.text = $"Unlock for: {spec.XpToUnlock} xp"; } catch { }
                    fixed_++;
                }
                if (fixed_ > 0)
                    Log.Info($"Kartentexte erneuert ({source}): {fixed_} Variante(n).");
            }
            catch (Exception ex)
            {
                Log.Warning("Kartentext-Refresh fehlgeschlagen: " + ex.Message);
            }
        }

        /// <summary>
        /// Diagnostik: loggt Live-Werte aller Shopkarten (Anzeige-/SO-/Preis-Name),
        /// um zu sehen was das Spiel wirklich rendert.
        /// </summary>
        private static void DumpShopItems(string source, ComputerShop shop)
        {
            try
            {
                var items = shop.shopItems;
                if (items == null) return;
                int n = 0;
                try { n = items.Length; } catch { return; }
                Log.Info($"Shop-Dump ({source}): {n} Eintraege.");
                int shown = 0;
                for (int i = 0; i < n && shown < 64; i++)
                {
                    ShopItem si = null;
                    try { si = items[i]; } catch { continue; }
                    if (si == null) continue;
                    string disp = "", soName = "", txt = "";
                    int price = -1, xp = -1;
                    try { disp = si.itemDisplayName ?? ""; } catch { }
                    try { soName = si.shopItemSO != null ? si.shopItemSO.itemName ?? "" : ""; } catch { }
                    try { price = si.shopItemSO != null ? si.shopItemSO.price : -1; } catch { }
                    try { xp = si.shopItemSO != null ? si.shopItemSO.xpToUnlock : -1; } catch { }
                    try { txt = si.txtName != null ? si.txtName.text ?? "" : "(kein txtName)"; } catch { txt = "(txtName-Fehler)"; }
                    Log.Info($"  Karte {i}: disp='{disp}' | SO='{soName}' | txt='{txt}' | {price}$ / {xp}xp");
                    shown++;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Shop-Dump fehlgeschlagen: " + ex.Message);
            }
        }

        internal void TryRegisterAll(string source, ComputerShop shop)
        {
            try
            {
                if (shop == null || shop.shopItems == null) return;
                // Dump nur bei echtem Shop-Oeffnen (Scene-Load ist zu frueh:
                // Karten noch unbefuellt). Einmal pro Szene.
                bool isShopOpen = source.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0
                    || source.IndexOf("Interact", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isShopOpen && !_shopDumped)
                {
                    _shopDumped = true;
                    DumpShopItems(source, shop);
                }
                // Kartentexte IMMER erneuern — auch wenn alle Varianten schon
                // registriert sind. Sonst bleibt "Unknown" stehen, sobald die
                // Vanilla-Karte Start/UpdateVisualState erneut rendert.
                if (_registeredIds.Count < ServerVariantSpec.All.Length)
                {
                    int added = 0;
                    foreach (var spec in ServerVariantSpec.All)
                    {
                        if (_registeredIds.Contains(spec.VariantId)) continue;
                        if (ShopContainsVariant(shop, spec))
                        {
                            _registeredIds.Add(spec.VariantId);
                            // Karte kann von einer frueheren Registrierung uebrig
                            // sein, waehrend ResetForScene die Base-ID-Map geloescht
                            // hat - sonst fehlt das Prefab-Routing beim Spawn.
                            EnsureBaseIdMapping(shop, spec);
                            continue;
                        }
                        // Eigene IDs duerfen nie mit Vanilla kollidieren.
                        if (VanillaUsesItemId(shop, spec.VariantItemId))
                        {
                            Log.Error($"Varianten-ID {spec.VariantItemId} ({spec.VariantDisplayName}) kollidiert " +
                                "mit Vanilla - Variante uebersprungen.");
                            continue;
                        }
                        var baseItem = FindBaseShopItem(shop, spec);
                        if (baseItem == null) continue;
                        int baseId = ReadBaseItemId(baseItem);
                        var clone = CloneShopItemForVariant(shop, baseItem, spec);
                        if (clone != null && AppendShopItem(shop, clone))
                        {
                            _registeredIds.Add(spec.VariantId);
                            _variantToBaseId[spec.VariantItemId] = baseId;
                            added++;
                            Log.Info($"Registered shop item {spec.VariantDisplayName} (baseId={baseId}).");
                        }
                    }
                    if (added > 0)
                        Log.Info($"Shop registration from {source}: +{added} variant(s), {_registeredIds.Count}/{ServerVariantSpec.All.Length} ready.");
                }
                RefreshVariantCardTexts(source, shop);
                ReflowShopRows(shop);
            }
            catch (Exception ex)
            {
                Log.Error("Shop registration failed.", ex);
            }
        }

        private static bool VanillaUsesItemId(ComputerShop shop, int itemId)
        {
            try
            {
                var items = shop.shopItems;
                if (items == null) return false;
                foreach (var item in items)
                {
                    if (item == null || item.shopItemSO == null) continue;
                    int id = 0;
                    try { id = item.shopItemSO.itemID; } catch { continue; }
                    if (id == itemId) return true;
                }
            }
            catch { }
            return false;
        }

        private static int ReadBaseItemId(ShopItem baseItem)
        {
            try { return baseItem != null && baseItem.shopItemSO != null ? baseItem.shopItemSO.itemID : 0; }
            catch { return 0; }
        }

        /// <summary>Stellt sicher, dass die Varianten-ID eine Base-ID hat, auch wenn
        /// die Karte schon existiert (ShopContainsVariant-Early-Path nach Reset).</summary>
        private void EnsureBaseIdMapping(ComputerShop shop, ServerVariantSpec spec)
        {
            try
            {
                if (_variantToBaseId.ContainsKey(spec.VariantItemId)) return;
                var baseItem = FindBaseShopItem(shop, spec);
                if (baseItem == null) return;
                int baseId = ReadBaseItemId(baseItem);
                _variantToBaseId[spec.VariantItemId] = baseId;
                if (ModConfig.VerboseLogging)
                    Log.Info($"Repaired base-id map for {spec.VariantDisplayName}: baseId={baseId}.");
            }
            catch (Exception ex)
            {
                Log.Warning($"EnsureBaseIdMapping failed for {spec.VariantDisplayName}: {ex.Message}");
            }
        }

        // Vanilla-Shop-Reihen zeigen nur ~5 Karten (Rest wird geclippt).
        // Reflow: aktive ShopItem-Kinder in 5er-Chunks auf Overflow-Reihen
        // verteilen (Reihe klonen, Kinder umhängen). Idempotent: zuerst alte
        // Overflow-Reihen zurückmergen, dann neu chunken.
        private const int MaxCardsPerRow = 5;
        private const string OverflowSuffix = " Overflow";

        private static void ReflowShopRows(ComputerShop shop)
        {
            try
            {
                // Buttons szenenweit finden — NICHT ueber shop.shopItemParent:
                // das ist beim Scene-Load-Trigger noch null, waehrend die
                // Klone (via baseItem.transform.parent) laengst einsortiert
                // sind. Deshalb lief der Reflow ins Leere (still return).
                ShopItem[] all = null;
                try { all = Resources.FindObjectsOfTypeAll<ShopItem>(); } catch { return; }
                if (all == null) return;

                var familyRows = new System.Collections.Generic.HashSet<int>();
                int buttons = 0;
                foreach (var si in all)
                {
                    if (si == null) continue;
                    string nm = "";
                    GameObject go = null;
                    try { go = si.gameObject; nm = go != null ? go.name ?? "" : ""; }
                    catch { continue; }
                    if (nm.IndexOf("greg_backplanes_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        if (go == null || !go.scene.IsValid() || !go.scene.isLoaded) continue;
                    }
                    catch { continue; }

                    buttons++;
                    try
                    {
                        var p = si.transform != null ? si.transform.parent : null;
                        if (p != null) familyRows.Add(p.GetInstanceID());
                    }
                    catch { }
                }

                if (buttons == 0)
                {
                    Log.Info("Reflow: keine Backplanes-Buttons gefunden (Shop-UI evtl. noch nicht aufgebaut).");
                    return;
                }

                // Reihen direkt einsammeln (kein shop.shopItemParent nötig).
                var rowsById = new System.Collections.Generic.Dictionary<int, Transform>();
                var scopes = new System.Collections.Generic.HashSet<int>();
                var scopeById = new System.Collections.Generic.Dictionary<int, Transform>();
                foreach (var rowId in familyRows)
                {
                    try
                    {
                        Transform row = FindRowByInstanceId(rowId);
                        if (row == null) continue;
                        rowsById[rowId] = row;
                        var parent = row.parent;
                        if (parent == null) continue;
                        int pid = 0;
                        try { pid = parent.GetInstanceID(); } catch { continue; }
                        if (pid == 0 || !scopes.Add(pid)) continue;
                        scopeById[pid] = parent;
                    }
                    catch { }
                }

                foreach (var kv in scopeById)
                {
                    try { SweepStaleOverflowRows(kv.Value); } catch { }
                }

                foreach (var kv in rowsById)
                {
                    Transform scope = null;
                    try
                    {
                        var parent = kv.Value != null ? kv.Value.parent : null;
                        scope = parent;
                    }
                    catch { }
                    if (scope == null) continue;
                    try { ReflowFamilyRow(scope, kv.Key); }
                    catch (Exception ex)
                    {
                        if (ModConfig.VerboseLogging)
                            Log.Info($"Reflow row failed: {ex.GetBaseException().Message}");
                    }
                }

                Log.Info($"Reflow: {buttons} Buttons in {rowsById.Count} Reihen verarbeitet.");

                // Layout neu aufbauen: Content per Name suchen, Fallback 4 Ebenen.
                try
                {
                    Transform anchor = null;
                    foreach (var kv in scopeById) { anchor = kv.Value; break; }
                    Transform content = anchor;
                    for (int i = 0; i < 6 && content != null; i++)
                    {
                        string nm = "";
                        try { nm = content.gameObject != null ? content.gameObject.name ?? "" : ""; } catch { }
                        if (nm.IndexOf("Content", StringComparison.OrdinalIgnoreCase) >= 0) break;
                        try { content = content.parent; } catch { content = null; }
                    }

                    if (content == null) content = anchor;
                    for (int i = 0; i < 4 && content != null; i++)
                    {
                        try
                        {
                            var rt = content.GetComponent<RectTransform>();
                            if (rt != null)
                                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                        }
                        catch { }
                        try { content = content.parent; } catch { content = null; }
                    }
                    try { Canvas.ForceUpdateCanvases(); } catch { }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Log.Warning($"ReflowShopRows failed: {ex.GetBaseException().Message}");
            }
        }

        private static Transform FindRowByInstanceId(int instanceId)
        {
            try
            {
                ShopItem[] all = Resources.FindObjectsOfTypeAll<ShopItem>();
                if (all == null) return null;
                foreach (var si in all)
                {
                    if (si == null) continue;
                    try
                    {
                        var t = si.transform;
                        if (t == null) continue;
                        var p = t.parent;
                        if (p != null)
                        {
                            try { if (p.GetInstanceID() == instanceId) return p; } catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static GameObject FindInactiveOverflowRow(Transform root, string ovName)
        {
            try
            {
                if (root == null || string.IsNullOrEmpty(ovName)) return null;
                int n = 0;
                try { n = root.childCount; } catch { n = 0; }
                for (int ci = 0; ci < n; ci++)
                {
                    Transform child = null;
                    try { child = root.GetChild(ci); } catch { continue; }
                    if (child == null) continue;
                    string nm = "";
                    try { nm = child.gameObject != null ? child.gameObject.name ?? "" : ""; } catch { continue; }
                    if (!string.Equals(nm, ovName, StringComparison.Ordinal)) continue;
                    bool active = true;
                    try { active = child.gameObject.activeInHierarchy; } catch { continue; }
                    if (!active)
                    {
                        try { return child.gameObject; } catch { return null; }
                    }

                    return null;
                }
            }
            catch { }
            return null;
        }

        // Verwaiste Leer-Reihen (keine ShopItem-Kinder, z.B. nach fehlgeschlagenem
        // Destroy) entfernen, sonst steht eine leere Reihe im Shop.
        private static void SweepStaleOverflowRows(Transform root)
        {
            try
            {
                if (root == null) return;
                var doomed = new System.Collections.Generic.List<GameObject>();
                int rootKids = 0;
                try { rootKids = root.childCount; } catch { rootKids = 0; }
                for (int ci = 0; ci < rootKids; ci++)
                {
                    Transform child = null;
                    try { child = root.GetChild(ci); } catch { continue; }
                    if (child == null) continue;
                    string nm = "";
                    try { nm = child.gameObject != null ? child.gameObject.name ?? "" : ""; } catch { continue; }
                    if (nm.IndexOf(OverflowSuffix, StringComparison.Ordinal) < 0) continue;
                    bool hasCard = false;
                    try
                    {
                        int kk = 0;
                        try { kk = child.childCount; } catch { kk = 0; }
                        for (int ki = 0; ki < kk; ki++)
                        {
                            Transform k = null;
                            try { k = child.GetChild(ki); } catch { continue; }
                            if (k == null) continue;
                            ShopItem si = null;
                            try { si = k.gameObject != null ? k.gameObject.GetComponent<ShopItem>() : null; }
                            catch { }
                            if (si != null) { hasCard = true; break; }
                        }
                    }
                    catch { }
                    if (!hasCard)
                    {
                        try { doomed.Add(child.gameObject); } catch { }
                    }
                }

                foreach (var go in doomed)
                {
                    try { go.SetActive(false); } catch { }
                    try { UnityEngine.Object.Destroy(go); } catch { }
                }

                if (doomed.Count > 0 && ModConfig.VerboseLogging)
                    Log.Info($"Reflow: {doomed.Count} verwaiste Leer-Reihe(n) entfernt.");
            }
            catch { }
        }

        private static void ReflowFamilyRow(Transform root, int rowId)
        {
            Transform row = null;
            try
            {
                int n = 0;
                try { n = root.childCount; } catch { n = 0; }
                for (int ci = 0; ci < n; ci++)
                {
                    Transform child = null;
                    try { child = root.GetChild(ci); } catch { continue; }
                    if (child == null) continue;
                    try { if (child.GetInstanceID() == rowId) { row = child; break; } } catch { }
                }
            }
            catch { }
            if (row == null) return;

            string rowName = "";
            try { rowName = row.gameObject != null ? row.gameObject.name ?? "" : ""; } catch { }
            if (rowName.EndsWith(OverflowSuffix, StringComparison.Ordinal)) return;

            // 1) Alte Overflow-Reihen dieser Familie zurückmergen + löschen.
            var overflowRows = new System.Collections.Generic.List<Transform>();
            try
            {
                Transform rowParent = null;
                try { rowParent = row.parent; } catch { rowParent = null; }
                int sn = 0;
                try { sn = rowParent != null ? rowParent.childCount : 0; } catch { sn = 0; }
                for (int si2 = 0; si2 < sn; si2++)
                {
                    Transform sibling = null;
                    try { sibling = rowParent.GetChild(si2); } catch { continue; }
                    if (sibling == null || sibling == row) continue;
                    string nm = "";
                    try { nm = sibling.gameObject != null ? sibling.gameObject.name ?? "" : ""; } catch { continue; }
                    if (nm == rowName + OverflowSuffix || nm.StartsWith(rowName + OverflowSuffix + " ",
                        StringComparison.Ordinal)) overflowRows.Add(sibling);
                }
            }
            catch { }

            foreach (var ov in overflowRows)
            {
                try
                {
                    // Sofort unsichtbar (Destroy wirkt erst am Frame-Ende;
                    // schlägt es fehl, bleibt sonst eine leere Reihe stehen).
                    try { ov.gameObject.SetActive(false); } catch { }
                    var kids = new System.Collections.Generic.List<Transform>();
                    try
                    {
                        int kn = 0;
                        try { kn = ov.childCount; } catch { kn = 0; }
                        for (int ki = 0; ki < kn; ki++)
                        {
                            Transform k = null;
                            try { k = ov.GetChild(ki); } catch { continue; }
                            if (k != null) kids.Add(k);
                        }
                    }
                    catch { }
                    foreach (var k in kids)
                    {
                        try { k.SetParent(row, false); } catch { }
                    }
                }
                catch { }
                try { UnityEngine.Object.Destroy(ov.gameObject); } catch { }
            }

            // 2) Aktive ShopItem-Kinder in Original-Reihenfolge sammeln.
            var cards = new System.Collections.Generic.List<Transform>();
            try
            {
                int rn = 0;
                try { rn = row.childCount; } catch { rn = 0; }
                for (int ri = 0; ri < rn; ri++)
                {
                    Transform k = null;
                    try { k = row.GetChild(ri); } catch { continue; }
                    if (k == null) continue;
                    ShopItem si = null;
                    try { si = k.gameObject != null ? k.gameObject.GetComponent<ShopItem>() : null; } catch { }
                    if (si == null) continue;
                    bool active = false;
                    try { active = k.gameObject.activeInHierarchy; } catch { }
                    if (active) cards.Add(k);
                }
            }
            catch { }

            cards.Sort((a, b) =>
            {
                int ia = 0, ib = 0;
                try { ia = a.GetSiblingIndex(); } catch { }
                try { ib = b.GetSiblingIndex(); } catch { }
                return ia.CompareTo(ib);
            });

            if (cards.Count <= MaxCardsPerRow)
            {
                Log.Info($"Reflow '{rowName}': {cards.Count} aktive Karten, kein Umbruch nötig.");
                return;
            }

            // 3) Chunks ab dem zweiten in Overflow-Reihen (vorhandene
            // inaktive wiederverwenden statt neu anzulegen).
            int overflowIdx = 0;
            for (int i = MaxCardsPerRow; i < cards.Count; i += MaxCardsPerRow)
            {
                overflowIdx++;
                string ovName = rowName + OverflowSuffix + (overflowIdx > 1 ? " " + overflowIdx : "");
                GameObject ovGo = FindInactiveOverflowRow(root, ovName);
                if (ovGo == null)
                {
                    try { ovGo = UnityEngine.Object.Instantiate(row.gameObject, row.parent, false); }
                    catch { continue; }
                    if (ovGo == null) continue;
                }
                try { ovGo.name = rowName + OverflowSuffix + (overflowIdx > 1 ? " " + overflowIdx : ""); } catch { }
                try
                {
                    var stale = new System.Collections.Generic.List<Transform>();
                    try
                    {
                        int sk = 0;
                        try { sk = ovGo.transform.childCount; } catch { sk = 0; }
                        for (int ski = 0; ski < sk; ski++)
                        {
                            Transform k = null;
                            try { k = ovGo.transform.GetChild(ski); } catch { continue; }
                            if (k != null) stale.Add(k);
                        }
                    }
                    catch { }
                    foreach (var k in stale)
                    {
                        try { UnityEngine.Object.Destroy(k.gameObject); } catch { }
                    }
                }
                catch { }

                int end = Math.Min(i + MaxCardsPerRow, cards.Count);
                int moved = 0;
                for (int j = i; j < end; j++)
                {
                    try { cards[j].SetParent(ovGo.transform, false); moved++; } catch { }
                }

                // Keinen leeren sichtbaren Overflow stehen lassen.
                if (moved == 0)
                {
                    try { ovGo.SetActive(false); } catch { }
                    try { UnityEngine.Object.Destroy(ovGo); } catch { }
                    continue;
                }

                try
                {
                    int sib = row.GetSiblingIndex() + overflowIdx;
                    ovGo.transform.SetSiblingIndex(sib);
                    ovGo.SetActive(true);
                }
                catch { }
            }

            if (ModConfig.VerboseLogging)
                Log.Info($"Reflow '{rowName}': {cards.Count} Karten -> {1 + overflowIdx} Reihen.");
        }

        /// <summary>Loest eine Varianten-ItemID auf die Base-ID auf (Prefab-Routing).
        /// Base-IDs duerfen 0 sein (SystemX vanilla itemID=0) - nur das Fehlen
        /// des Keys ist ein Fehlschlag.</summary>
        internal bool TryGetBaseId(int variantItemId, out int baseItemId)
        {
            try
            {
                if (_variantToBaseId.TryGetValue(variantItemId, out baseItemId))
                    return true;
            }
            catch { }
            baseItemId = 0;
            return false;
        }

        private static bool ShopContainsVariant(ComputerShop shop, ServerVariantSpec spec)
        {
            foreach (var item in shop.shopItems)
            {
                if (item == null) continue;
                string hay = (item.itemDisplayName ?? "") + " " + (item.guid ?? "") + " " + (item.shopItemSO?.itemName ?? "");
                if (hay.IndexOf(spec.VariantId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    hay.IndexOf(spec.VariantDisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static ShopItem FindBaseShopItem(ComputerShop shop, ServerVariantSpec spec)
        {
            foreach (var item in shop.shopItems)
            {
                if (item == null || item.shopItemSO == null) continue;
                string assetName = item.shopItemSO.name ?? "";
                string display = item.itemDisplayName ?? "";
                if (assetName.IndexOf(spec.BaseAssetName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    display.IndexOf(spec.BaseDisplayName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    display.IndexOf(spec.BaseDisplayName.Replace("IOPs", "IOPS", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase) >= 0)
                    return item;
            }
            return null;
        }

        private static ShopItem CloneShopItemForVariant(ComputerShop shop, ShopItem baseItem, ServerVariantSpec spec)
        {
            try
            {
                string cardLabel = $"{spec.VariantDisplayName} ({spec.RecommendedCable})";
                var parent = baseItem.transform != null ? baseItem.transform.parent : null;
                GameObject cloneGo = parent != null
                    ? UnityEngine.Object.Instantiate(baseItem.gameObject, parent, false)
                    : UnityEngine.Object.Instantiate(baseItem.gameObject);
                if (cloneGo == null) return null;

                var clone = cloneGo.GetComponent<ShopItem>();
                if (clone == null)
                {
                    UnityEngine.Object.Destroy(cloneGo);
                    return null;
                }

                var newSo = ScriptableObject.CreateInstance<ShopItemSO>();
                newSo.itemName = cardLabel;
                newSo.price = spec.Price;
                newSo.xpToUnlock = spec.XpToUnlock;
                newSo.itemType = baseItem.shopItemSO.itemType;
                // Eigene Item-ID: Boosted Server sind eigenstaendige Eintraege
                // (keine Base-Kopie mehr). Prefab-Routing via GetPrefabForItem-Prefix.
                newSo.itemID = spec.VariantItemId;
                newSo.eol = baseItem.shopItemSO.eol;
                newSo.isCustomColor = false;
                newSo.sprite = baseItem.shopItemSO.sprite;

                cloneGo.name = spec.VariantId + "_button";
                clone.shopItemSO = newSo;
                clone.guid = spec.VariantId;
                clone.itemDisplayName = cardLabel;

                if (clone.txtName != null) clone.txtName.text = cardLabel;
                if (clone.txtPrice != null) clone.txtPrice.text = $"{spec.Price} $";
                if (clone.txtXpToUnlock != null) clone.txtXpToUnlock.text = $"Unlock for: {spec.XpToUnlock} xp";
                if (clone.itemIcon != null && newSo.sprite != null) clone.itemIcon.sprite = newSo.sprite;

                try { clone.OnLoad(); } catch { /* visual state best-effort */ }
                try { clone.UpdateVisualState(); } catch { /* visual state best-effort */ }

                // UpdateVisualState() resets the name to "Unknown" for locked
                // items (xpToUnlock > 0). Re-assert the real name so the player
                // can see what they are unlocking.
                if (clone.txtName != null) clone.txtName.text = cardLabel;
                clone.itemDisplayName = cardLabel;

                cloneGo.SetActive(true);
                return clone;
            }
            catch (Exception ex)
            {
                Log.Error($"Cloning shop item for {spec.VariantDisplayName} failed.", ex);
                return null;
            }
        }

        private static bool AppendShopItem(ComputerShop shop, ShopItem clone)
        {
            try
            {
                var old = shop.shopItems;
                if (old == null) return false;
                var grown = new Il2CppReferenceArray<ShopItem>(old.Length + 1);
                for (int i = 0; i < old.Length; i++) grown[i] = old[i];
                grown[old.Length] = clone;
                shop.shopItems = grown;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not extend ComputerShop.shopItems, clone UI still parented: {ex.Message}");
                return false;
            }
        }

        // ------------------------------------------------------------- purchases

        internal void TrackPurchase(string source, int itemId, int price, PlayerManager.ObjectInHand itemType, string displayName)
        {
            try
            {
                if (!IsServerItemType(itemType)) return;
                // Strict Name -> Varianten-ID. KEIN Preis-Fallback: 20 Varianten
                // teilen 5 Preis-Punkte (20k/100k/250k/500k/1M), Preis allein ist
                // nicht eindeutig und hat in v1.x Phantom-/Fehl-Konfigurationen
                // verursacht (BUGFIX_NOTES #7). Ohne Match wird NICHT getrackt —
                // ein erratener Spec laeuft Gefahr, den falschen Server zu
                // konfigurieren (ISSUE-005/008).
                var spec = ServerVariantSpec.FindByShopValues(displayName, price);
                string how = "Name-Match";
                if (spec == null)
                {
                    // Exakte Varianten-ItemID (9001-9020) ist eindeutig.
                    spec = FindSpecByVariantId(itemId);
                    how = "ID-Match";
                }
                if (spec == null)
                {
                    Log.Warning($"Purchase without variant match (id={itemId} price={price} name='{displayName}'): not tracked.");
                    return;
                }
                EnqueuePending(spec);
                Log.Info($"Tracked purchase for {spec.VariantDisplayName} from {source} ({how}).");
            }
            catch (Exception ex)
            {
                Log.Error("Purchase tracking failed.", ex);
            }
        }

        /// <summary>Exakter Match ueber Varianten-ItemID (9001-9020).</summary>
        private static ServerVariantSpec FindSpecByVariantId(int itemId)
        {
            try
            {
                foreach (var spec in ServerVariantSpec.All)
                {
                    if (spec.VariantItemId == itemId) return spec;
                }
            }
            catch { }
            return null;
        }

        internal void RewritePurchaseDisplayName(int itemId, int price, PlayerManager.ObjectInHand itemType, ref string displayName)
        {
            try
            {
                if (!IsServerItemType(itemType) || string.IsNullOrEmpty(displayName)) return;
                var spec = ServerVariantSpec.FindByShopValues(displayName, price);
                if (spec == null) return;
                string canonical = $"{spec.VariantDisplayName} ({spec.RecommendedCable})";
                if (!string.Equals(displayName, canonical, StringComparison.Ordinal))
                {
                    displayName = canonical;
                    if (ModConfig.VerboseLogging)
                        Log.Info($"Rewrote cart display name to '{canonical}'.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Display-name rewrite failed.", ex);
            }
        }

        internal void ConfigureSpawnedItem(string source, ComputerShop shop, int price, PlayerManager.ObjectInHand itemType, int uid)
        {
            try
            {
                if (!IsServerItemType(itemType)) return;
                // Exakte Zuordnung zuerst: Checkout-Snapshot in Cart-Reihenfolge
                // (Bulk-sicher, auch bei gemischten Preispunkten). Preis-Peek
                // nur als Fallback fuer Spawns ausserhalb eines Checkouts.
                var spec = PeekCheckoutSpec();
                GameObject go = null;
                try { if (shop?.spawnedItems != null) shop.spawnedItems.TryGetValue(uid, out go); } catch { /* best-effort */ }
                if (spec != null && go != null && !GoMatchesSpecFamily(go, spec))
                {
                    var priceSpec = PeekPendingSpecForSpawn(price);
                    if (priceSpec != null && GoMatchesSpecFamily(go, priceSpec))
                        spec = priceSpec;
                    else
                        Log.Warning($"Spawn uid={uid}: Cart-Reihenfolge weicht ab (erwartet " +
                            $"{spec.VariantDisplayName}, Prefab '{go.name}') - nutze Snapshot-Spec.");
                }
                if (spec == null)
                    spec = PeekPendingSpecForSpawn(price);
                if (spec == null) return;
                ConsumeCheckoutSpec(spec);
                if (_checkoutExpectedUnits > 0) _checkoutVariantSpawned++;
                Log.Info($"Spawn ({source}): {spec.VariantDisplayName} uid={uid} - suche physisches Item.");
                _spawnedSpecsByUid[uid] = spec;
                RememberSpawnedUid(uid);

                if (go == null)
                {
                    Log.Info($"Spawn uid {uid} fuer {spec.VariantDisplayName} noch nicht in spawnedItems; Drain/Insert uebernehmen.");
                    return;
                }
                int configured = 0;
                foreach (var server in go.GetComponentsInChildren<Server>(true))
                {
                    if (server == null) continue;
                    _pendingSpecsByPointer[server.Pointer] = spec;
                    if (ConfigureServerAndPorts(server, spec, source, out _, out _)) configured++;
                    LogSpawnCheck(server, spec, source + "/spawn");
                }
                if (ModConfig.VerboseLogging || configured > 0)
                    Log.Info($"Configured spawned {spec.VariantDisplayName} uid {uid}: {configured} server(s).");
            }
            catch (Exception ex)
            {
                Log.Error("Spawned-item configuration failed.", ex);
            }
        }

        private static readonly System.Collections.Generic.HashSet<int> _keysLoggedUids =
            new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// Retries uid-correlated spawns that were not in spawnedItems yet.
        /// Trailing UIDs (never surfaced, e.g. shop closed before spawn) expire
        /// after 60 seconds so the 1/sec sweep does not run forever.
        /// </summary>
        /// <summary>
        /// Sofort-Drain ohne Throttle (z.B. direkt nach Checkout): Der Spawn
        /// ist dann garantiert registriert - kein Warten auf den 1/s-Tick.
        /// </summary>
        internal void DrainSpawnedSpecsNow(string source)
        {
            try { DrainSpawnedSpecs(); } catch { }
        }

        private void DrainSpawnedSpecs()
        {
            if (_spawnedSpecsByUid.Count == 0) return;
            DateTime now = DateTime.UtcNow;
            ComputerShop shop = null;
            try { shop = MainGameManager.instance?.computerShop; } catch { /* best-effort */ }
            if (shop?.spawnedItems == null)
            {
                PruneStaleSpawned(now);
                return;
            }

            int[] uids = _spawnedSpecsByUid.Keys.ToArray();
            foreach (int uid in uids)
            {
                GameObject go = null;
                try { shop.spawnedItems.TryGetValue(uid, out go); } catch { continue; }
                if (go == null)
                {
                    // Diagnose (einmal pro UID): welche Keys hat spawnedItems
                    // wirklich? Klaert ob unser UID-Read (0?) daneben liegt.
                    if (_keysLoggedUids.Add(uid))
                    {
                        try
                        {
                            var keys = shop.spawnedItems.Keys;
                            int n = 0;
                            try { n = keys != null ? keys.Count : -1; } catch { n = -1; }
                            var sample = new System.Collections.Generic.List<int>();
                            try
                            {
                                if (keys != null)
                                {
                                    foreach (int k in keys)
                                    {
                                        sample.Add(k);
                                        if (sample.Count >= 8) break;
                                    }
                                }
                            }
                            catch { }
                            Log.Info($"Spawn-Diagnose uid={uid}: spawnedItems hat {n} Eintraege " +
                                $"(z.B. {string.Join(",", sample.ConvertAll(k => k.ToString()).ToArray())}).");
                        }
                        catch { }
                    }
                    // Noch nicht da oder schon an den Spieler uebergeben:
                    // nach 60 s aufgeben (Insertion konfiguriert dann weiterhin).
                    if (_spawnedUidCreatedAt.TryGetValue(uid, out DateTime created) &&
                        now - created > TimeSpan.FromSeconds(60))
                    {
                        _spawnedSpecsByUid.Remove(uid);
                        _spawnedUidCreatedAt.Remove(uid);
                        Log.Warning($"Spawned uid {uid} never surfaced in spawnedItems; dropped after 60s (converged at insertion instead).");
                    }
                    continue;
                }
                var spec = _spawnedSpecsByUid[uid];
                foreach (var server in go.GetComponentsInChildren<Server>(true))
                {
                    if (server == null) continue;
                    _pendingSpecsByPointer[server.Pointer] = spec;
                    ConfigureServerAndPorts(server, spec, "spawned-drain", out _, out _);
                    LogSpawnCheck(server, spec, "spawned-drain");
                }
                _spawnedSpecsByUid.Remove(uid);
                _spawnedUidCreatedAt.Remove(uid);
            }
        }

        /// <summary>
        /// Expliziter Speed-Check direkt nach dem Spawn-Configure: liest
        /// maxProcessingSpeed zurueck und meldet OK oder MISMATCH mit
        /// Live-Wert. Unbedingt (nicht nur verbose) - ein Kauf ist selten.
        /// </summary>
        private static void LogSpawnCheck(Server server, ServerVariantSpec spec, string source)
        {
            try
            {
                float live = -1f;
                try { live = server.maxProcessingSpeed; } catch { }
                if (Approx(live, spec.RuntimeProcessingSpeed))
                    Log.Info($"Spawn-Check {spec.VariantDisplayName} ({source}): OK speed={live:F3}.");
                else
                    Log.Warning($"Spawn-Check {spec.VariantDisplayName} ({source}): MISMATCH " +
                        $"live={live:F3} erwartet={spec.RuntimeProcessingSpeed:F3}.");
            }
            catch (Exception ex)
            {
                Log.Warning($"Spawn-Check {spec?.VariantDisplayName} ({source}) fehlgeschlagen: {ex.Message}");
            }
        }

        private static void WatchServer(Server server, ServerVariantSpec spec)
        {
            if (server == null || spec == null) return;
            try
            {
                IntPtr ptr = IntPtr.Zero;
                try { ptr = server.Pointer; } catch { return; }
                if (ptr == IntPtr.Zero) return;
                lock (_watched)
                {
                    foreach (var w in _watched)
                    {
                        if (w.ptr == ptr) return;
                    }
                    _watched.Add((ptr, server, spec));
                }
            }
            catch { }
        }

        /// <summary>
        /// Alle 5 s: Watchlist pruefen. Driftet ein Varianten-Server vom Spec
        /// weg (Spiel hat ueberschrieben), sofort neu konfigurieren + melden.
        /// Tote Referenzen aussortieren. Laeuft auch ausserhalb des Fensters.
        /// </summary>
        internal void TickWatchlist()
        {
            try
            {
                if (DateTime.UtcNow - _lastWatchAt < TimeSpan.FromSeconds(5.0)) return;
                _lastWatchAt = DateTime.UtcNow;
                List<(IntPtr ptr, Server server, ServerVariantSpec spec)> snapshot;
                lock (_watched) { snapshot = new List<(IntPtr, Server, ServerVariantSpec)>(_watched); }
                if (snapshot.Count == 0) return;
                foreach (var (ptr, server, spec) in snapshot)
                {
                    if (server == null || spec == null) { DropWatched(ptr); continue; }
                    float live;
                    try { live = server.maxProcessingSpeed; }
                    catch { DropWatched(ptr); continue; } // zerstoert
                    bool speedDrifted = !Approx(live, spec.RuntimeProcessingSpeed);
                    InspectPorts(server, spec, out int freeBad, out int found);
                    // Ports pruefen, nicht nur IOPS: frisch gekaufte 500K-Server
                    // blieben sonst bei Vanilla-connectionSpeed=0.2 („1 Gbps"),
                    // waehrend maxProcessingSpeed korrekt war (v2.1.3).
                    bool portsNeedFix = freeBad > 0 || found == 0;
                    if (!speedDrifted && !portsNeedFix) continue;
                    if (speedDrifted)
                        Log.Warning($"Watchlist: {spec.VariantDisplayName} gedriftet " +
                            $"(live={live:F3} erwartet={spec.RuntimeProcessingSpeed:F3}) - konfiguriere neu.");
                    else if (freeBad > 0 && ModConfig.VerboseLogging)
                        Log.Info($"Watchlist: {spec.VariantDisplayName} — {freeBad} freie Port(s) mit falscher Bandbreite " +
                            $"(erwartet {spec.RuntimeNetworkSpeed * 5f:0.##} Gbps) - konfiguriere neu.");
                    else if (found == 0 && ModConfig.VerboseLogging)
                        Log.Info($"Watchlist: {spec.VariantDisplayName} — keine Ports gefunden - versuche Re-Suche.");
                    if (!RepairGuard.TryEnter(ptr)) continue;
                    try
                    {
                        ConfigureServerAndPorts(server, spec, "watchlist", out bool changed, out int ports);
                        if (changed || ports > 0) ForceServerDisplayRefresh(server);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Watchlist Re-Configure fehlgeschlagen: {ex.Message}");
                    }
                    finally
                    {
                        RepairGuard.Exit(ptr);
                    }
                }

                AuditVanillaPortSpeeds(snapshot);
            }
            catch (Exception ex)
            {
                Log.Warning($"Watchlist-Tick fehlgeschlagen: {ex.Message}");
            }
        }

        // IOPS -> Gbps-Leiter (wie Varianten-Tiers). Unter 100k: -1 = Vanilla lassen.
        internal static float VanillaTierGbps(float maxProcessingSpeed)
        {
            float iops = maxProcessingSpeed * 100000f;
            if (iops >= 40000000f) return 4000f;
            if (iops >= 4000000f) return 400f;
            if (iops >= 2000000f) return 200f;
            if (iops >= 1000000f) return 100f;
            if (iops >= 500000f) return 40f;
            if (iops >= 100000f) return 25f;
            return -1f;
        }

        private static DateTime _lastVanillaAuditAt = DateTime.MinValue;

        /// <summary>
        /// Alle NICHT-Varianten-Server: freie Ports auf Tier-Speed heben
        /// (Vanilla lässt sie bei 0.2 = 1 Gbps). Nur Speed, keine Flags/Typen.
        /// Belegte Ports (Kabel/Modul) werden nie angefasst. Alle 30 s.
        /// </summary>
        private static void AuditVanillaPortSpeeds(
            List<(IntPtr ptr, Server server, ServerVariantSpec spec)> watched)
        {
            try
            {
                if (DateTime.UtcNow - _lastVanillaAuditAt < TimeSpan.FromSeconds(30.0)) return;
                _lastVanillaAuditAt = DateTime.UtcNow;

                var watchedPtrs = new HashSet<IntPtr>();
                if (watched != null)
                    foreach (var (ptr, _, _) in watched)
                    {
                        if (ptr != IntPtr.Zero) watchedPtrs.Add(ptr);
                    }

                Server[] all = null;
                try { all = Resources.FindObjectsOfTypeAll<Server>(); }
                catch { return; }
                if (all == null) return;

                int fixedPorts = 0, servers = 0;
                foreach (var server in all)
                {
                    if (server == null) continue;
                    IntPtr ptr = IntPtr.Zero;
                    try { ptr = server.Pointer; } catch { continue; }
                    if (ptr == IntPtr.Zero || watchedPtrs.Contains(ptr)) continue;
                    float max = 0f;
                    try { max = server.maxProcessingSpeed; } catch { continue; }
                    if (max <= 0f) continue;
                    float gbps = VanillaTierGbps(max);
                    if (gbps < 0f) continue;
                    float target = gbps / 5f;

                    List<CableLink> links = null;
                    try { links = CollectServerLinks(server); } catch { continue; }
                    if (links == null) continue;
                    bool touched = false;
                    bool force = false;
                    try { force = ModConfig.ForcePortSpeed; } catch { }
                    foreach (var link in links)
                    {
                        if (link == null) continue;
                        GetPortState(link, out bool hasCable, out bool hasModule, out bool deadRef);
                        if (deadRef)
                        {
                            try { link.insertedSFP = null; } catch { /* best-effort */ }
                            try { hasModule = link.insertedSFP != null; } catch { hasModule = false; }
                        }

                        if ((hasCable || hasModule) && !force) continue;

                        float live = 0f;
                        try { live = link.connectionSpeed; } catch { continue; }
                        if (Approx(live, target)) continue;
                        try { link.SetConnectionSpeed(target); } catch { /* best-effort */ }
                        try
                        {
                            if (!Approx(link.connectionSpeed, target))
                                link.connectionSpeed = target;
                        }
                        catch { /* best-effort */ }
                        touched = true;
                        fixedPorts++;
                    }

                    if (touched)
                    {
                        servers++;
                        try { ForceServerDisplayRefresh(server); } catch { }
                    }
                }

                if (fixedPorts > 0)
                    Log.Info($"Vanilla-Port-Audit: {fixedPorts} Port(s) an {servers} Server(n) auf Tier-Speed gehoben.");
            }
            catch (Exception ex)
            {
                Log.Warning($"Vanilla-Port-Audit fehlgeschlagen: {ex.Message}");
            }
        }

        /// <summary>
        /// Read-only port audit for one server: counts found links and free ports
        /// whose connectionSpeed is still off-spec (1 Gbps / 0.2 vs. target).
        /// </summary>
        private static int InspectPorts(Server server, ServerVariantSpec spec, out int freeBad, out int found)
        {
            freeBad = 0;
            found = 0;
            bool force = false;
            try { force = ModConfig.ForcePortSpeed; } catch { }
            try
            {
                foreach (var link in CollectServerLinks(server))
                {
                    found++;
                    GetPortState(link, out bool hasCable, out bool hasModule, out _);
                    if ((hasCable || hasModule) && !force) continue;
                    float ls;
                    try { ls = link.connectionSpeed; } catch { continue; }
                    if (!Approx(ls, spec.RuntimeNetworkSpeed)) freeBad++;
                }
            }
            catch { /* best-effort */ }
            return found;
        }

        // Port-Zustand aufschlüsseln: Kabel-ID gesetzt? Modul live oder nur
        // zerstörte Referenz (IL2CPP meldet tote Objekte als != null)?
        private static void GetPortState(CableLink link, out bool hasCable, out bool hasModule, out bool deadModuleRef)
        {
            hasCable = false;
            hasModule = false;
            deadModuleRef = false;
            if (link == null) return;
            try { hasCable = link.cableIDsOnLink != 0; } catch { /* best-effort */ }
            SFPModule module = null;
            bool readable = false;
            try
            {
                module = link.insertedSFP;
                readable = true;
            }
            catch { readable = false; }

            if (!readable || module == null) return;
            bool alive = false;
            try
            {
                var _ = module.gameObject;
                alive = true;
            }
            catch { alive = false; }

            if (alive) hasModule = true;
            else deadModuleRef = true;
        }

        private static void DropWatched(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return;
            try
            {
                lock (_watched)
                {
                    for (int i = _watched.Count - 1; i >= 0; i--)
                    {
                        if (_watched[i].ptr == ptr) _watched.RemoveAt(i);
                    }
                }
            }
            catch { }
        }
        private void PruneStaleSpawned(DateTime now)
        {
            foreach (var kv in _spawnedUidCreatedAt.ToArray())
            {
                if (now - kv.Value > TimeSpan.FromSeconds(60) &&
                    _spawnedSpecsByUid.ContainsKey(kv.Key))
                {
                    _spawnedSpecsByUid.Remove(kv.Key);
                    _spawnedUidCreatedAt.Remove(kv.Key);
                    Log.Warning($"Dropped stale spawned-uid {kv.Key} after 60s without shop.");
                }
            }
        }

        // ------------------------------------------------------------- insertion

        internal void FinalizeInsertedServer(string source, Server server, ServerSaveData saveData)
        {
            if (server == null) return;
            IntPtr ptr = IntPtr.Zero;
            try { ptr = server.Pointer; } catch { /* best-effort */ }
            if (!RepairGuard.TryEnter(ptr)) return;
            try
            {
                float liveSpeed = -1f;
                string liveName = "";
                try { liveSpeed = server.maxProcessingSpeed; } catch { }
                try { liveName = server.gameObject != null ? server.gameObject.name ?? "" : ""; } catch { }
                // Save-Load-Burst nicht spammen: nur manuelle Inserts laut loggen.
                if (saveData == null)
                    Log.Info($"Insert ({source}): '{liveName}' ptr=0x{ptr.ToInt64():X} liveSpeed={liveSpeed:F3} saveData=null.");
                else if (ModConfig.VerboseLogging)
                    Log.Info($"Insert ({source}): '{liveName}' liveSpeed={liveSpeed:F3} saveData=ok.");
                var spec = ResolveSpecForInsertedServer(server, consumePending: saveData == null);
                if (spec == null)
                {
                    // Save-Load-Rauschen unterdruecken: Hunderte Vanilla-Server
                    // beim Laden sind kein Fehler. Nur manuelle Inserts laut melden.
                    if (saveData == null)
                        Log.Info($"Insert ({source}): keine Spec aufgeloest (liveSpeed={liveSpeed:F3}) - bleibt Vanilla.");
                    else if (ModConfig.VerboseLogging && _pendingInsertions.Count > 0)
                        Log.Info($"Insertion from {source} has {_pendingInsertions.Count} pending purchase(s) but no match (id='{ReadServerId(server) ?? ""}').");
                    return;
                }
                if (!SpeedPlausibleForSpec(liveSpeed, spec))
                {
                    Log.Warning($"Resolved {spec.VariantDisplayName} for inserted server, " +
                        $"but liveSpeed={liveSpeed:F3} passt weder zu Base ({spec.BaseRuntimeProcessingSpeed:F3}) " +
                        $"noch Variante ({spec.RuntimeProcessingSpeed:F3}). Skipping.");
                    return;
                }
                ConfigureServerAndPorts(server, spec, source, out bool serverChanged, out int changedPorts);
                ForceServerDisplayRefresh(server);
                string id = ReadServerId(server);
                if (string.IsNullOrEmpty(id) && saveData != null)
                {
                    try { id = NormalizeServerIdentity(saveData.serverID); } catch { id = null; }
                }
                if (!string.IsNullOrEmpty(id)) _registry.Set(id, spec);
                _pendingSpecsByPointer.Remove(ptr);
                // KEIN RemoveOnePendingSpec hier: ein Kauf wird genau einmal
                // konsumiert - an der Spawn-Stelle (ConsumePendingSpecForSpawn)
                // bzw. in DequeueMatchingPendingSpec. Ein zweiter Konsum hier
                // wuerde einen Folge-Kauf desselben Preispunkts (20k/100k/250k/500k/1M)
                // fehl-verbrauchen und zu Fehl-Zuordnung fuehren.
                Log.Info($"Finalized {spec.VariantDisplayName} from {source}: serverChanged={serverChanged}, changedPorts={changedPorts}.");
            }
            catch (Exception ex)
            {
                Log.Error("Inserted-server finalization failed.", ex);
            }
            finally
            {
                RepairGuard.Exit(ptr);
            }
        }

        // ---------------------------------------------------------------- repair

        internal void TryRepairServer(string source, Server server, bool onlyIfKnown)
        {
            if (server == null) return;
            IntPtr ptr = IntPtr.Zero;
            try { ptr = server.Pointer; } catch { return; }
            if (!RepairGuard.TryEnter(ptr)) return; // re-entrant (nested patch) — bail out, fixes stack overflow
            try
            {
                string id = ReadServerId(server);
                ServerVariantSpec spec = null;
                if (!string.IsNullOrEmpty(id))
                {
                    if (_repairedServerIds.Contains(id)) return;
                    spec = _registry.Get(id);
                }
                if (spec == null)
                    spec = InferSpecFromRuntime(server);
                if (spec == null)
                {
                    if (!onlyIfKnown && ModConfig.VerboseLogging)
                        Log.Info($"No variant for server id='{id ?? ""}' from {source}.");
                    return;
                }
                float repairSpeed = -1f;
                try { repairSpeed = server.maxProcessingSpeed; } catch { }
                if (!SpeedPlausibleForSpec(repairSpeed, spec))
                {
                    if (!string.IsNullOrEmpty(id)) _repairedServerIds.Add(id);
                    Log.Warning($"Skipped persisted marker for {spec.VariantDisplayName}: " +
                        $"liveSpeed={repairSpeed:F3} passt weder zu Base noch Variante (id='{id}').");
                    return;
                }
                ConfigureServerAndPorts(server, spec, source, out bool serverChanged, out int changedPorts);
                if (serverChanged || changedPorts > 0)
                    ForceServerDisplayRefresh(server);
                if (!string.IsNullOrEmpty(id))
                {
                    _repairedServerIds.Add(id);
                    _registry.Set(id, spec); // normalize legacy ids
                }
                if (serverChanged || changedPorts > 0)
                {
                    _sweepRepaired++;
                    Log.Info($"Repaired {spec.VariantDisplayName} from {source}: serverChanged={serverChanged}, changedPorts={changedPorts}.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Persisted-server repair failed.", ex);
            }
            finally
            {
                RepairGuard.Exit(ptr);
            }
        }

        internal void RepairAfterDeviceRepair(string source, Server server)
        {
            // Technician replacements respawn/reset servers; re-assert the variant.
            TryRepairServer(source, server, onlyIfKnown: false);
            if (server == null) return;
            var spec = ResolveSpecForServer(server);
            if (spec == null) return;
            IntPtr ptr = IntPtr.Zero;
            try { ptr = server.Pointer; } catch { /* best-effort */ }
            if (!RepairGuard.TryEnter(ptr)) return;
            try
            {
                ConfigureServerAndPorts(server, spec, source, out _, out _);
                ForceServerDisplayRefresh(server);
                string id = ReadServerId(server);
                if (!string.IsNullOrEmpty(id)) _registry.Set(id, spec);
                Log.Info($"Re-applied {spec.VariantDisplayName} after device repair.");
            }
            catch (Exception ex)
            {
                Log.Error("Post-repair re-application failed.", ex);
            }
            finally
            {
                RepairGuard.Exit(ptr);
            }
        }

        // ------------------------------------------------------- configure core

        /// <summary>
        /// Applies IOPS + port profile. NEVER touches connected ports
        /// (cableIDsOnLink != 0 or insertedSFP set) and never removes an
        /// inserted SFP module — this is what broke ports (0G / no reconnect)
        /// after reload in v1.x.
        /// </summary>
        internal static bool ConfigureServerAndPorts(Server server, ServerVariantSpec spec, string source,
            out bool serverChanged, out int changedPorts)
        {
            serverChanged = false;
            changedPorts = 0;
            try
            {
                float target = spec.RuntimeProcessingSpeed;
                if (!Approx(server.maxProcessingSpeed, target))
                {
                    server.maxProcessingSpeed = target;
                    serverChanged = true;
                }
                // Clamp, never zero: v1.x wrote 0 when out of range, stalling output.
                if (server.currentProcessingSpeed > target)
                {
                    server.currentProcessingSpeed = target;
                    serverChanged = true;
                }

                var links = CollectServerLinks(server);
                foreach (var link in links)
                {
                    if (ConfigurePort(link, spec, server))
                        changedPorts++;
                }
                if (links.Count == 0 && ModConfig.VerboseLogging)
                {
                    // Insert oft vor CableLink.Start/RegisterLink: Ports noch nicht
                    // erreichbar -> Watchlist retryet alle 5 s (v2.1.3).
                    Log.Info($"Configure {spec.VariantDisplayName} from {source}: 0 Ports gefunden " +
                        "(Links noch nicht registriert?) - Watchlist-Retry aktiv.");
                }

                // Visual differentiation (tint + scale). Idempotent and cheap after
                // the first pass; runs on every configure path (spawn/insert/repair).
                ServerVisuals.Apply(server, spec);

                // Read-Back-Verifikation: Beweist ob die Werte wirklich haften
                // (oder ob das Spiel sie danach zuruecksetzt). Mismatch -> Warnung.
                VerifyApplied(server, spec, source);

                // Watchlist: Speed bleibt ueberwacht, Re-Assert bei Drift.
                WatchServer(server, spec);

                if ((serverChanged || changedPorts > 0) && ModConfig.VerboseLogging)
                    Log.Info($"Configured {spec.VariantDisplayName} from {source}: serverChanged={serverChanged}, changedPorts={changedPorts}.");
                return serverChanged || changedPorts > 0;
            }
            catch (Exception ex)
            {
                Log.Error("ConfigureServerAndPorts failed.", ex);
                return false;
            }
        }

        private static List<CableLink> CollectServerLinks(Server server)
        {
            var result = new List<CableLink>();
            var seen = new HashSet<IntPtr>();
            int rawCablelinks = 0, rawActive = 0, rawChildren = 0, rejected = 0;
            void Add(CableLink link, bool force)
            {
                if (link == null) return;
                IntPtr p = IntPtr.Zero;
                try { p = link.Pointer; } catch { return; }
                if (p == IntPtr.Zero || !seen.Add(p)) return;
                if (force || IsServerLinkFor(link, server)) result.Add(link);
                else rejected++;
            }
            try
            {
                if (server.cablelinks != null)
                    foreach (var link in server.cablelinks) { rawCablelinks++; Add(link, true); }
            }
            catch { /* best-effort */ }
            try
            {
                if (server.activeLinks != null)
                    foreach (var link in server.activeLinks) { rawActive++; Add(link, false); }
            }
            catch { /* best-effort */ }
            try
            {
                // Kinder-Scan: Ports ausserhalb IsServerLinkFor-Filter akzeptieren,
                // solange sie nicht eindeutig Switch/PatchPanel zugeordnet sind.
                // typeOfLink kann vor RegisterLink noch None sein (v2.1.3).
                foreach (var link in server.GetComponentsInChildren<CableLink>(true))
                {
                    rawChildren++;
                    bool foreign = false;
                    try { foreign = link.parentSwitch != null || link.parentPatchPanel != null; } catch { }
                    if (foreign) { rejected++; continue; }
                    Add(link, true);
                }
            }
            catch { /* best-effort */ }
            // Fallback: scene-weite Suche nach parentServer-Pointer-Match
            // (Ports ggf. nicht unter der Server-Hierarchie).
            if (result.Count == 0)
            {
                try
                {
                    IntPtr want = IntPtr.Zero;
                    try { want = server.Pointer; } catch { return result; }
                    if (want == IntPtr.Zero) return result;
                    foreach (var link in Resources.FindObjectsOfTypeAll<CableLink>())
                    {
                        if (link == null) continue;
                        Server parent = null;
                        try { parent = link.parentServer; } catch { continue; }
                        if (parent == null) continue;
                        IntPtr have = IntPtr.Zero;
                        try { have = parent.Pointer; } catch { continue; }
                        if (have == want) { rawActive++; Add(link, true); }
                    }
                }
                catch { /* best-effort */ }
            }
            if (result.Count == 0 && ModConfig.VerboseLogging)
            {
                Log.Info($"CollectServerLinks empty: cablelinks={rawCablelinks} active={rawActive} " +
                    $"children={rawChildren} rejected={rejected} (ptr=0x{SafePtr(server):X}).");
            }
            return result;
        }

        private static IntPtr SafePtr(Server server)
        {
            try { return server.Pointer; } catch { return IntPtr.Zero; }
        }

        private static bool IsServerLinkFor(CableLink link, Server server)
        {
            try
            {
                if (link.parentSwitch != null || link.parentPatchPanel != null) return false;
                if (link.parentServer != null)
                {
                    IntPtr a = IntPtr.Zero, b = IntPtr.Zero;
                    try { a = link.parentServer.Pointer; } catch { return false; }
                    try { b = server.Pointer; } catch { return false; }
                    return a == b;
                }
                // Unassigned server-side port: typeOfLink kann vor RegisterLink
                // noch None sein — None + Server akzeptieren, Rest ablehnen.
                return link.typeOfLink == CableLink.TypeOfLink.Server
                    || link.typeOfLink == CableLink.TypeOfLink.None;
            }
            catch { return false; }
        }

        /// <summary>
        /// Called from Server.RegisterLink postfix: configure this port the moment
        /// the game wires it to a known variant server (fixes stuck 1 Gbps ports
        /// when insert ran before ports were discoverable).
        /// </summary>
        internal void OnLinkRegistered(string source, Server server, CableLink link)
        {
            try
            {
                if (server == null || link == null) return;
                ServerVariantSpec spec = ResolveSpecForRegisteredServer(server);
                if (spec == null) return;
                IntPtr ptr = IntPtr.Zero;
                try { ptr = server.Pointer; } catch { return; }
                if (!RepairGuard.TryEnter(ptr)) return;
                try
                {
                    if (ConfigurePort(link, spec, server) && ModConfig.VerboseLogging)
                        Log.Info($"Port configured via {source} for {spec.VariantDisplayName} " +
                            $"(speed={link.connectionSpeed:F3} -> {spec.RuntimeNetworkSpeed:F3}).");
                }
                finally { RepairGuard.Exit(ptr); }
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging) Log.Warning($"OnLinkRegistered failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Called from CableLink.Start postfix: parentServer may already be set;
        /// configure early so empty ports never stay at Vanilla 0.2 (1 Gbps).
        /// </summary>
        internal void OnLinkStarted(CableLink link)
        {
            try
            {
                if (link == null) return;
                Server server = null;
                try { server = link.parentServer; } catch { }
                if (server == null)
                {
                    try
                    {
                        var parents = link.GetComponentsInParent<Server>(true);
                        if (parents != null && parents.Length > 0) server = parents[0];
                    }
                    catch { /* best-effort */ }
                }
                if (server == null) return;
                OnLinkRegistered("CableLink.Start", server, link);
            }
            catch (Exception ex)
            {
                if (ModConfig.VerboseLogging) Log.Warning($"OnLinkStarted failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Called from CableLink.InsertSFP postfix: a real module just slid in.
        /// If the game negotiated the port down earlier (e.g. cable first at
        /// 1 Gbps, module second), raise the cap back to the tier speed ONCE.
        /// Never lowers, never touches modules — effective rate stays
        /// min(cap, module, cable) as the game computes it.
        /// </summary>
        internal void ReassertPortCapAfterModuleInsert(string source, CableLink link)
        {
            try
            {
                if (link == null) return;
                Server server = null;
                try { server = link.parentServer; } catch { return; }
                if (server == null) return;
                var spec = ResolveSpecForRegisteredServer(server);
                if (spec == null) return;
                IntPtr ptr = IntPtr.Zero;
                try { ptr = link.Pointer; } catch { return; }
                if (ptr == IntPtr.Zero || !RepairGuard.TryEnter(ptr)) return;
                try
                {
                    float live = -1f;
                    try { live = link.connectionSpeed; } catch { return; }
                    if (!Approx(live, spec.RuntimeNetworkSpeed) && live < spec.RuntimeNetworkSpeed)
                    {
                        try { link.SetConnectionSpeed(spec.RuntimeNetworkSpeed); } catch { /* best-effort */ }
                        try { link.connectionSpeed = spec.RuntimeNetworkSpeed; } catch { /* best-effort */ }
                        Log.Info($"Port cap re-asserted for {spec.VariantDisplayName} ({source}): " +
                            $"{live:F3} -> {spec.RuntimeNetworkSpeed:F3} (module inserted).");
                    }
                }
                finally { RepairGuard.Exit(ptr); }
            }
            catch (Exception ex)
            {
                Log.Warning($"Port cap re-assert failed: {ex.Message}");
            }
        }

        private ServerVariantSpec ResolveSpecForRegisteredServer(Server server)
        {
            IntPtr ptr = IntPtr.Zero;
            try { ptr = server.Pointer; } catch { return null; }
            if (ptr != IntPtr.Zero && _pendingSpecsByPointer.TryGetValue(ptr, out var byPointer))
                return byPointer;
            var byRegistry = ResolveSpecForServer(server);
            if (byRegistry != null) return byRegistry;
            // Tier: Server laeuft bereits auf Varianten-Speed (nach Finalize/Insert).
            var byTier = InferSpecBySpeedTier(server);
            if (byTier != null) return byTier;
            return null;
        }

        private static bool ConfigurePort(CableLink link, ServerVariantSpec spec, Server server)
        {
            try
            {
                // Event-driven memory (no polling): our ports are registered
                // once — CableLink event postfixes re-assert the speed on drift.
                try { if (link != null && spec != null) PortSpeedMemory.Register(link, spec.RuntimeNetworkSpeed); } catch { }
                // Port in use (cable id assigned or live SFP module inserted):
                // hands off — ausser ForcePortSpeed ist an. Zerstörte
                // Modul-Refs (IL2CPP-Fake-Null) zählen als frei und werden
                // bereinigt, sonst bleibt jeder Port ewig "belegt".
                bool force = false;
                try { force = ModConfig.ForcePortSpeed; } catch { }
                GetPortState(link, out bool hasCable, out bool hasModule, out bool deadRef);
                if (deadRef)
                {
                    try { link.insertedSFP = null; } catch { /* best-effort */ }
                    try { hasModule = link.insertedSFP != null; } catch { hasModule = false; }
                }

                if ((hasCable || hasModule) && !force) return false;

                bool changed = false;
                float targetSpeed = spec.RuntimeNetworkSpeed;
                if (!Approx(link.connectionSpeed, targetSpeed))
                {
                    try { link.SetConnectionSpeed(targetSpeed); } catch { /* setter best-effort */ }
                    if (!Approx(link.connectionSpeed, targetSpeed))
                    {
                        try { link.connectionSpeed = targetSpeed; } catch { /* best-effort */ }
                    }
                    changed = true;
                }
                try { if (!link.isSFPPort) { link.isSFPPort = true; changed = true; } } catch { /* best-effort */ }
                try { if (!link.isFibrePort) { link.isFibrePort = true; changed = true; } } catch { /* best-effort */ }
                try { if (link.sfpTypeSupported != spec.SfpType) { link.sfpTypeSupported = spec.SfpType; changed = true; } } catch { /* best-effort */ }
                // NIE sfpTypeInserted auf leeren Ports setzen: Das erzeugt ein
                // Phantom-Modul (Typ gesetzt, aber insertedSFP == null). Das Spiel
                // hält den Port dann für belegt (echte SFP+/SFP28-Module werden
                // abgewiesen) und rendert kein Modell (nichts da). Umgekehrt:
                // Altlasten früherer Versionen reparieren (gesetzt ohne Modul -> 0).
                try
                {
                    if (!hasModule && link.sfpTypeInserted != 0)
                    {
                        link.sfpTypeInserted = 0;
                        changed = true;
                    }
                }
                catch { /* best-effort */ }
                try
                {
                    if (link.parentServer == null)
                    {
                        link.parentServer = server;
                        changed = true;
                    }
                }
                catch { /* best-effort */ }
                return changed;
            }
            catch
            {
                return false;
            }
        }

        private static void ForceServerDisplayRefresh(Server server)
        {
            try
            {
                server.lastDisplayedMaxSpeed = -1f;
                server.lastDisplayedProcessingSpeed = -1f;
                server.lastDisplayedEolMinute = -1;
                server.lastDisplayedLabel = string.Empty;
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Liest die eben geschriebenen Werte zurueck. Mismatch heisst: Das Spiel
        /// (oder ein anderer Mod) hat sie danach ueberschrieben - dann ist
        /// Configure der falsche Zeitpunkt/das falsche Feld.
        /// </summary>
        private static void VerifyApplied(Server server, ServerVariantSpec spec, string source)
        {
            try
            {
                float live;
                try { live = server.maxProcessingSpeed; }
                catch (Exception ex)
                {
                    Log.Warning($"Verify {spec.VariantDisplayName} ({source}): maxProcessingSpeed nicht lesbar: {ex.Message}");
                    return;
                }
                if (!Approx(live, spec.RuntimeProcessingSpeed))
                {
                    Log.Warning($"Verify {spec.VariantDisplayName} ({source}): MISMATCH maxProcessingSpeed " +
                        $"live={live:F3} erwartet={spec.RuntimeProcessingSpeed:F3}.");
                    return;
                }
                int checkedPorts = 0, badPorts = 0, foundPorts = 0, busyPorts = 0;
                int busyCable = 0, busyModule = 0, busyDeadRef = 0;
                foreach (var link in CollectServerLinks(server))
                {
                    foundPorts++;
                    bool busy = false;
                    try
                    {
                        GetPortState(link, out bool hasCable, out bool hasModule, out bool deadRef);
                        if (deadRef) busyDeadRef++;
                        else if (hasModule) busyModule++;
                        else if (hasCable) busyCable++;
                        busy = hasCable || hasModule;
                    }
                    catch { continue; }

                    if (busy) { busyPorts++; continue; }
                    checkedPorts++;
                    float ls;
                    try { ls = link.connectionSpeed; } catch { continue; }
                    if (!Approx(ls, spec.RuntimeNetworkSpeed)) badPorts++;
                }
                Log.Info($"Verify {spec.VariantDisplayName} ({source}): OK " +
                    $"max={live:F3}, Ports gefunden={foundPorts}, geprueft={checkedPorts}, " +
                    $"belegt={busyPorts}(Kabel:{busyCable},Modul:{busyModule},tot:{busyDeadRef}), abweichend={badPorts}.");
                if (badPorts > 0)
                {
                    Log.Warning($"Verify {spec.VariantDisplayName}: {badPorts} freie Port(s) noch auf falscher " +
                        $"Bandbreite (erwartet {spec.RuntimeNetworkSpeed * 5f:0.##} Gbps = {spec.RuntimeNetworkSpeed:F3}).");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Verify {spec.VariantDisplayName} ({source}) fehlgeschlagen: {ex.Message}");
            }
        }

        /// <summary>
        /// Read-only Audit ueber alle Server: Welche Varianten-Marker sind live
        /// korrekt konfiguriert, welche weichen ab, welche sind unbekannt?
        /// Schreibt nichts - reiner Befund fuer Panel + Log.
        /// </summary>
        internal void VerifyAllServers(string source)
        {
            int ok = 0, mismatch = 0, unknown = 0;
            var mismatchNames = new List<string>();
            int auditLines = 0;
            const int MaxAuditLines = 30;
            try
            {
                Server[] servers;
                try { servers = UnityEngine.Object.FindObjectsOfType<Server>(); }
                catch (Exception ex)
                {
                    LastVerifySummary = "Verify: Server-Suche fehlgeschlagen.";
                    Log.Warning("Verify: Server-Suche fehlgeschlagen: " + ex.Message);
                    return;
                }
                if (servers == null) return;
                foreach (var server in servers)
                {
                    if (server == null) continue;
                    ServerVariantSpec spec = null;
                    try { spec = ResolveSpecForServer(server) ?? InferSpecFromRuntime(server); }
                    catch { spec = null; }
                    if (spec == null) { unknown++; continue; }
                    bool good = true;
                    try
                    {
                        if (!Approx(server.maxProcessingSpeed, spec.RuntimeProcessingSpeed)) good = false;
                    }
                    catch { good = false; }
                    if (good) ok++;
                    else
                    {
                        mismatch++;
                        if (mismatchNames.Count < 5)
                        {
                            string n = "";
                            try { n = server.gameObject != null ? server.gameObject.name ?? "" : ""; } catch { }
                            mismatchNames.Add($"{spec.VariantDisplayName} @{n}");
                        }
                    }
                    // Link-Audit (read-only, nur Varianten-Server): zeigt pro
                    // belegtem Port Cap, Kabel, Modul (+Modul-Speed) und Gegen-
                    // seite. Klaert "es kommen nur 1 Gbit an" ohne Raten.
                    if (auditLines < MaxAuditLines)
                        auditLines += AuditConnectedLinks(spec, server, MaxAuditLines - auditLines);
                }
            }
            catch (Exception ex)
            {
                Log.Error("VerifyAllServers failed.", ex);
                return;
            }
            LastVerifyOk = ok;
            LastVerifyMismatch = mismatch;
            LastVerifyUnknown = unknown;
            LastVerifySummary = $"Verify ({source}): OK={ok} Mismatch={mismatch} Unbekannt={unknown}" +
                (mismatchNames.Count > 0 ? " | z.B. " + string.Join(", ", mismatchNames.ToArray()) : "");
            Log.Info(LastVerifySummary);
        }

        /// <summary>
        /// Read-only Link-Audit fuer einen Varianten-Server. Gibt die Anzahl
        /// geloggter Zeilen zurueck (Budget vom Aufrufer). Schreibt nie.
        /// </summary>
        private static int AuditConnectedLinks(ServerVariantSpec spec, Server server, int budget)
        {
            int lines = 0;
            try
            {
                if (spec == null || server == null || budget <= 0) return 0;
                string srvName = "";
                try { srvName = server.gameObject != null ? server.gameObject.name ?? "" : ""; } catch { }
                foreach (var link in CollectServerLinks(server))
                {
                    if (lines >= budget) break;
                    if (link == null) continue;
                    bool hasCable = false, hasModule = false;
                    try { hasCable = link.cableIDsOnLink != 0; } catch { continue; }
                    try { hasModule = link.insertedSFP != null; } catch { }
                    if (!hasCable && !hasModule) continue;
                    float portGbps = -1f;
                    try { portGbps = link.connectionSpeed * 5f; } catch { }
                    string modText = "nein";
                    try
                    {
                        if (hasModule)
                        {
                            float ms = -1f;
                            try { ms = link.insertedSFP.speed * 5f; } catch { }
                            modText = ms >= 0f ? $"{ms:0.##}Gbps" : "ja";
                        }
                    }
                    catch { modText = "ja?"; }
                    string far = "?";
                    try
                    {
                        if (link.parentSwitch != null)
                            far = "switch:" + (link.parentSwitch.gameObject != null ? link.parentSwitch.gameObject.name ?? "?" : "?");
                        else if (link.parentPatchPanel != null)
                            far = "patch:" + (link.parentPatchPanel.gameObject != null ? link.parentPatchPanel.gameObject.name ?? "?" : "?");
                        else if (!string.IsNullOrEmpty(link.switchID))
                            far = "switchID:" + link.switchID;
                    }
                    catch { /* best-effort */ }
                    lines++;
                    Log.Info($"Link-Audit {spec.VariantDisplayName} @{srvName}: port={portGbps:0.##}Gbps " +
                        $"kabel={(hasCable ? "ja" : "nein")} modul={modText} -> {far}");
                }
            }
            catch { /* audit best-effort */ }
            return lines;
        }

        // ------------------------------------------------------------- resolving

        private ServerVariantSpec ResolveSpecForInsertedServer(Server server, bool consumePending)
        {
            IntPtr ptr = IntPtr.Zero;
            try { ptr = server.Pointer; } catch { /* best-effort */ }
            if (ptr != IntPtr.Zero && _pendingSpecsByPointer.TryGetValue(ptr, out var byPointer))
                return byPointer;

            var byRegistry = ResolveSpecForServer(server);
            if (byRegistry != null) return byRegistry;

            var inferred = InferSpecFromRuntime(server);
            if (inferred != null) return inferred;

            // Speed-Tier-Inferenz (namen-unabhaengig): Server laeuft bereits
            // auf Varianten-Speed (z.B. Spawn-configure, Rename durch Dritte).
            // Familie ggf. unscharf (Tint) - Werte pro Tier sind identisch.
            var byTier = InferSpecBySpeedTier(server);
            if (byTier != null) return byTier;

            if (consumePending) return DequeueMatchingPendingSpec(server);
            return null;
        }

        private ServerVariantSpec ResolveSpecForServer(Server server)
        {
            string id = ReadServerId(server);
            if (string.IsNullOrEmpty(id)) return null;
            return _registry.Get(id);
        }

        /// <summary>
        /// Speed-Plausibilitaet ohne Namen: akzeptiert Base-Speed (frisch) und
        /// Varianten-Speed (bereits konfiguriert). Schuetzt vor Konfiguration
        /// voellig fremder Server, ohne auf (umbenennbare) Objektnamen zu bauen.
        /// </summary>
        private static bool SpeedPlausibleForSpec(float liveSpeed, ServerVariantSpec spec)
        {
            if (spec == null) return false;
            return Approx(liveSpeed, spec.RuntimeProcessingSpeed)
                || Approx(liveSpeed, spec.BaseRuntimeProcessingSpeed);
        }

        /// <summary>
        /// Tier-Inferenz: Live-Speed entspricht einer Varianten-Stufe.
        /// Familien-Zuordnung ggf. unscharf (erste passende Spec).
        /// </summary>
        private static ServerVariantSpec InferSpecBySpeedTier(Server server)
        {
            try
            {
                float speed;
                try { speed = server.maxProcessingSpeed; } catch { return null; }
                foreach (var spec in ServerVariantSpec.All)
                {
                    if (Approx(speed, spec.RuntimeProcessingSpeed)) return spec;
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Self-healing fallback: a server whose name matches a variant base and
        /// whose speed already equals a variant speed was configured before
        /// (registry lost or never written). Re-assert + re-persist it instead of
        /// forcing the player to rebuy.
        /// </summary>
        private static ServerVariantSpec InferSpecFromRuntime(Server server)
        {
            try
            {
                string name = server.gameObject != null ? server.gameObject.name ?? "" : "";
                float speed;
                try { speed = server.maxProcessingSpeed; } catch { return null; }
                foreach (var spec in ServerVariantSpec.All)
                {
                    if (!Approx(speed, spec.RuntimeProcessingSpeed)) continue;
                    if (NameMatchesBaseToken(name, spec.BaseRuntimeToken)) return spec;
                }
                return null;
            }
            catch { return null; }
        }

        private static bool LooksLikeBaseForSpec(Server server, ServerVariantSpec spec)
        {
            try
            {
                string name = server.gameObject != null ? server.gameObject.name ?? "" : "";
                if (!NameMatchesBaseToken(name, spec.BaseRuntimeToken)) return false;
                return Approx(server.maxProcessingSpeed, spec.BaseRuntimeProcessingSpeed);
            }
            catch { return false; }
        }

        private static bool LooksLikeVariantForSpec(Server server, ServerVariantSpec spec)
        {
            try
            {
                string name = server.gameObject != null ? server.gameObject.name ?? "" : "";
                if (!NameMatchesBaseToken(name, spec.BaseRuntimeToken)) return false;
                return Approx(server.maxProcessingSpeed, spec.RuntimeProcessingSpeed);
            }
            catch { return false; }
        }

        private static bool NameMatchesBaseToken(string name, string token)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(token)) return false;
            string n = name.Replace('_', '.');
            string t = token.Replace('_', '.');
            return n.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string ReadServerId(Server server)
        {
            try
            {
                string id = NormalizeServerIdentity(server.ServerID);
                if (!string.IsNullOrEmpty(id)) return id;
                string objName = server.gameObject != null ? server.gameObject.name : null;
                return NormalizeServerIdentity(objName);
            }
            catch { return null; }
        }

        private static string NormalizeServerIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim();
            int clone = text.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            if (clone >= 0) text = text.Substring(0, clone).Trim();
            // gregCore HardwareIdPersistencePatch stamps stable ids as
            // gregID:Server:<12-hex>. Vanilla/legacy ids are Server.<name>.
            if (text.StartsWith("gregID:Server:", StringComparison.OrdinalIgnoreCase))
            {
                int tail = text.IndexOf('_');
                return tail > 0 ? text.Substring(0, tail) : text;
            }
            if (text.StartsWith("Server.", StringComparison.OrdinalIgnoreCase)) return text;
            return null;
        }

        private void EnqueuePending(ServerVariantSpec spec)
        {
            _pendingInsertions.Enqueue(new PendingInsertion { Spec = spec, CreatedAt = DateTime.UtcNow });
            // Bulk-Kaeufe (LargerCart): 30+ Units duerfen nicht die aeltesten
            // Eintraege verdrangen. Cap grosszuegig, Expiry (10min) raeumt auf.
            int dropped = 0;
            while (_pendingInsertions.Count > 200) { _pendingInsertions.Dequeue(); dropped++; }
            if (dropped > 0 && ModConfig.VerboseLogging)
                Log.Info($"Pending-Cap: {dropped} aelteste Eintraege verworfen.");
        }

        private ServerVariantSpec PeekPendingSpecForSpawn(int price)
        {
            // Peek (kein Konsum!): Der Eintrag bleibt fuer die Insertion
            // erhalten und wird erst dort verbraucht (FinalizeInsertedServer).
            DateTime now = DateTime.UtcNow;
            int count = _pendingInsertions.Count;
            ServerVariantSpec match = null;
            for (int i = 0; i < count; i++)
            {
                var pending = _pendingInsertions.Dequeue();
                if (now - pending.CreatedAt > TimeSpan.FromMinutes(10)) continue; // stale: droppen
                if (match == null && pending.Spec.Price == price)
                    match = pending.Spec;
                _pendingInsertions.Enqueue(pending);
            }
            return match;
        }

        private void RememberSpawnedUid(int uid)
        {
            try { _spawnedUidCreatedAt[uid] = DateTime.UtcNow; } catch { /* best-effort */ }
        }

        /// <summary>Baut den Checkout-Snapshot: eine Spec pro Unit in
        /// Cart-Reihenfolge (qty expandiert). Vanilla spawnt in Cart-Order,
        /// daher korreliert der n-te SpawnPhysicalItem mit dem n-ten Eintrag -
        /// exakt, auch bei 30+ Units und gemischten Familien zum selben Preis.
        /// Bei Familien-Mismatch am Prefab greift der Familien-Check in
        /// ConfigureSpawnedItem (Preis-Peek als Korrektur).</summary>
        internal void BeginCheckoutSnapshot(ComputerShop shop)
        {
            try
            {
                _checkoutSpecQueue.Clear();
                _checkoutExpectedUnits = 0;
                _checkoutVariantSpawned = 0;
                var cart = shop?.cartUIItems;
                if (cart == null) return;
                for (int i = 0; i < cart.Count; i++)
                {
                    var it = cart[i];
                    if (it == null) continue;
                    int id = 0;
                    try { id = it.ItemID; } catch { continue; }
                    var spec = ServerVariantSpec.FindByVariantItemId(id);
                    if (spec == null) continue;
                    int qty = 1;
                    try { qty = Math.Max(1, it.Quantity); } catch { }
                    for (int u = 0; u < qty; u++) _checkoutSpecQueue.Enqueue(spec);
                    _checkoutExpectedUnits += qty;
                }
                if (_checkoutExpectedUnits > 0)
                    Log.Info($"Checkout-Snapshot: {_checkoutExpectedUnits} Boosted-Unit(s) in Cart-Reihenfolge erwartet.");
            }
            catch (Exception ex) { Log.Warning("Checkout-Snapshot failed: " + ex.Message); }
        }

        /// <summary>Post-Checkout-Verify: konfigurierte Varianten-Units vs.
        /// erwartete Units. Abweichung = Warnung im Log + sichtbare
        /// gregCore-Notification (Sicherheitsnetz bei Bulk-Kaeufen).</summary>
        internal void VerifyCheckout(string source)
        {
            try
            {
                if (_checkoutExpectedUnits == 0) return;
                if (_checkoutVariantSpawned != _checkoutExpectedUnits)
                {
                    string msg = $"Backplanes: {_checkoutVariantSpawned}/{_checkoutExpectedUnits} Boosted-Servern konfiguriert ({source}).";
                    Log.Warning(msg + " Cart vs. Spawn weicht ab - Log pruefen.");
                    if (GregHost.HasCore)
                    {
                        try { BackplanesMod.NotifyCore(msg); } catch { /* best-effort */ }
                    }
                }
                else if (ModConfig.VerboseLogging)
                    Log.Info($"Checkout-Verify: {_checkoutVariantSpawned}/{_checkoutExpectedUnits} Boosted-Units konfiguriert.");
                int leftover = 0;
                try { leftover = _checkoutSpecQueue.Count; } catch { }
                if (leftover > 0)
                {
                    Log.Warning($"Checkout-Verify: {leftover} Boosted-Spec(s) ohne Spawn uebrig - Prefab-Routing pruefen.");
                    try { _checkoutSpecQueue.Clear(); } catch { }
                }
            }
            catch (Exception ex) { Log.Warning("Checkout-Verify failed: " + ex.Message); }
        }

        private ServerVariantSpec PeekCheckoutSpec()
        {
            try { return _checkoutSpecQueue.Count > 0 ? _checkoutSpecQueue.Peek() : null; }
            catch { return null; }
        }

        private void ConsumeCheckoutSpec(ServerVariantSpec spec)
        {
            if (spec == null) return;
            try
            {
                int count = _checkoutSpecQueue.Count;
                bool removed = false;
                for (int i = 0; i < count; i++)
                {
                    var queued = _checkoutSpecQueue.Dequeue();
                    if (!removed && queued != null &&
                        string.Equals(queued.VariantId, spec.VariantId, StringComparison.OrdinalIgnoreCase))
                        removed = true;
                    else
                        _checkoutSpecQueue.Enqueue(queued);
                }
            }
            catch { /* best-effort */ }
        }

        /// <summary>Prueft, ob das gespawnte Prefab zur Spec-Familie passt
        /// (BaseRuntimeToken, _-tolerant). Schuetzt vor Cart-Order-Drift.</summary>
        private static bool GoMatchesSpecFamily(GameObject go, ServerVariantSpec spec)
        {
            try
            {
                if (go == null || spec == null) return false;
                string token = spec.BaseRuntimeToken;
                if (string.IsNullOrEmpty(token)) return false;
                try { if (NameMatchesBaseToken(go.name ?? "", token)) return true; } catch { }
                foreach (var server in go.GetComponentsInChildren<Server>(true))
                {
                    if (server == null) continue;
                    string n = "";
                    try { n = server.gameObject != null ? server.gameObject.name ?? "" : ""; }
                    catch { continue; }
                    if (NameMatchesBaseToken(n, token)) return true;
                }
                return false;
            }
            catch { return true; } // im Zweifel nicht blockieren
        }

        private ServerVariantSpec DequeueMatchingPendingSpec(Server server)
        {
            if (_pendingInsertions.Count == 0) return null;
            ServerVariantSpec match = null;
            ServerVariantSpec nameOnlyFallback = null;
            int count = _pendingInsertions.Count;
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < count; i++)
            {
                var pending = _pendingInsertions.Dequeue();
                if (now - pending.CreatedAt > TimeSpan.FromMinutes(10)) continue;
                if (match == null && LooksLikeBaseForSpec(server, pending.Spec))
                    match = pending.Spec;
                else
                {
                    // Fallback: Name passt zur Spec-Familie, Speed aber nicht
                    // (z.B. Base-Speed im Spiel gedriftet oder bereits vorkonfiguriert).
                    // Pending-Kaeufe sind begrenzt/gueltig - besser als nichts.
                    if (nameOnlyFallback == null)
                    {
                        try
                        {
                            string n = server.gameObject != null ? server.gameObject.name ?? "" : "";
                            if (NameMatchesBaseToken(n, pending.Spec.BaseRuntimeToken))
                                nameOnlyFallback = pending.Spec;
                        }
                        catch { }
                    }
                    _pendingInsertions.Enqueue(pending);
                }
            }
            if (match == null && nameOnlyFallback != null)
            {
                Log.Warning($"Nutze Name-only-Match {nameOnlyFallback.VariantDisplayName} " +
                    "(Base-Speed passt nicht - Spiel gedriftet oder vorkonfiguriert).");
                RemoveOnePendingSpec(nameOnlyFallback);
                return nameOnlyFallback;
            }
            if (match == null)
            {
                // Letzter Fallback: aeltester gueltiger Pending-Kauf (FIFO).
                // Greift wenn Identitaet unkenntlich ist (z.B. Fremd-Rename wie
                // gregID:Server:...). Bewusst laut, damit Fehl-Zuordnungen
                // im Log sichtbar sind.
                DateTime now2 = DateTime.UtcNow;
                ServerVariantSpec oldest = null;
                DateTime oldestAt = DateTime.MaxValue;
                foreach (var pending in _pendingInsertions)
                {
                    if (now2 - pending.CreatedAt > TimeSpan.FromMinutes(10)) continue;
                    if (pending.CreatedAt < oldestAt)
                    {
                        oldestAt = pending.CreatedAt;
                        oldest = pending.Spec;
                    }
                }
                if (oldest != null)
                {
                    string srv = "";
                    try { srv = server.gameObject != null ? server.gameObject.name ?? "" : ""; } catch { }
                    Log.Warning($"Nutze FIFO-Fallback {oldest.VariantDisplayName} " +
                        $"fuer Insert '{srv}' (kein Match moeglich).");
                    RemoveOnePendingSpec(oldest);
                    return oldest;
                }
            }
            return match;
        }

        private void RemoveOnePendingSpec(ServerVariantSpec spec)
        {
            int count = _pendingInsertions.Count;
            bool removed = false;
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < count; i++)
            {
                var pending = _pendingInsertions.Dequeue();
                if (now - pending.CreatedAt > TimeSpan.FromMinutes(10)) continue;
                if (!removed && string.Equals(pending.Spec.VariantId, spec.VariantId, StringComparison.OrdinalIgnoreCase))
                    removed = true;
                else
                    _pendingInsertions.Enqueue(pending);
            }
        }

        // Hinweis: Kein hartes Loeschen von Pending-State bei Shop-Aktionen
        // mehr (Clear/Cancel): gekaufte Items existieren physisch weiter, und
        // ein Wipe zerstoert die Kauf->Insert-Korrelation. Abgelaufenes
        // raeumen Expiry-Pfade weg (Pending 10min, Spawn-UIDs 60s).

        /// <summary>Serialisiert Marker fuer GregSaveGuard-Sidecar.</summary>
        internal string RegistrySerialize()
        {
            try { return _registry.Serialize(); } catch { return ""; }
        }

        /// <summary>Laedt Marker aus GregSaveGuard-Sidecar.</summary>
        internal void RegistryDeserialize(string content)
        {
            try { _registry.Deserialize(content); } catch { }
        }

        private static bool Approx(float a, float b) => Math.Abs(a - b) < 0.001f;

        private static bool IsServerItemType(PlayerManager.ObjectInHand itemType)
        {
            return itemType == PlayerManager.ObjectInHand.Server1U ||
                   itemType == PlayerManager.ObjectInHand.Server2U ||
                   itemType == PlayerManager.ObjectInHand.Server3U;
        }
    }
}
