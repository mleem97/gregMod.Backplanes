using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace GregMod.Backplanes
{
    /// <summary>
    /// One high-IOPS backplane server variant sold in the shop.
    ///
    /// Unit conventions (verified against the game assemblies):
    /// <list type="bullet">
    /// <item>Processing speed is IOPS / 100000 (5000 -&gt; 0.05, 12000 -&gt; 0.12, 100000 -&gt; 1.0).</item>
    /// <item>Network speed is Gbps / 5 (10G -&gt; 2, 25G -&gt; 5, 40G -&gt; 8, 100G -&gt; 20).</item>
    /// </list>
    ///
    /// Bandwidth tiers are aligned with gregMod.MoreModules (100G/200G/400G…):
    /// those modules keep the vanilla QSFP+ <c>sfpType</c>, so every tier ≥40G uses
    /// <c>SfpType = 3</c> as the port cage type (<c>sfpTypeSupported</c>).
    /// <c>sfpTypeInserted</c> is NEVER written by the mod: the game sets it when a
    /// real module slides in. Writing it on empty ports creates a phantom module
    /// (port looks occupied, real modules rejected, no model rendered).
    /// Connected ports are never rewritten.
    ///
    /// Visual differentiation (v2.1.0+): TintColor on family body materials plus
    /// ScaleY (4U/8U look). Scale is visual only — rack slot occupancy is unchanged.
    /// </summary>
    internal sealed class ServerVariantSpec
    {
        internal string FamilyKey;
        internal string BaseDisplayName;
        internal string BaseAssetName;
        internal string VariantId;
        internal string VariantDisplayName;
        internal int Iops;
        internal int Price;
        internal int XpToUnlock;
        /// <summary>Eigene Item-ID der Variante. Fix vergeben (9001+), zur
        /// Registrierung auf Kollision mit Vanilla-IDs geprueft. Kollidiert nicht
        /// mit gregMod.MoreModules (MOD/BULK/TRAY_ID_BASE 1000/2000/3000).</summary>
        internal int VariantItemId;
        internal string ConnectorHint;
        internal float NetworkSpeedGbps;
        internal int SfpType;
        internal int FiberLaneCount;

        /// <summary>Optional: module family shown in the shop label (MoreModules-oriented).</summary>
        internal string RecommendedModule;

        /// <summary>Base game speed of the 3U (5K IOPS) or 7U (12K IOPS) server this variant upgrades.</summary>
        internal float BaseSpeed;

        /// <summary>Runtime tint applied to the family body materials (see ServerVisuals).</summary>
        internal Color TintColor;

        /// <summary>Base body color used for proximity matching of materials (see ServerVisuals).</summary>
        internal Color FamilyBaseColor;

        /// <summary>Visual Y scale: 4/3 for 3U-based, 8/7 for 7U-based variants. Visual only.</summary>
        internal float ScaleY;

        /// <summary>RGB-Streifen: Hue rotiert dauerhaft (RgbAnimator), statt statischem Tint.</summary>
        internal bool RgbAnimated;

        internal string IopsText => Iops.ToString(CultureInfo.InvariantCulture);

        internal float RuntimeProcessingSpeed => Iops / 100000f;

        internal float RuntimeNetworkSpeed => NetworkSpeedGbps / 5f;

        internal float BaseRuntimeProcessingSpeed => BaseSpeed;

        internal string BaseRuntimeToken => BaseAssetName.Replace("ShopItemSO_", string.Empty, StringComparison.Ordinal);

        /// <summary>Persistenz-/Match-Key aus der IOPS-Stufe (100k/500k/1m/2m/4m).</summary>
        internal string SizeKey
        {
            get
            {
                if (Iops >= 1000000 && Iops % 1000000 == 0)
                    return (Iops / 1000000).ToString(CultureInfo.InvariantCulture) + "m";
                if (Iops >= 1000 && Iops % 1000 == 0)
                    return (Iops / 1000).ToString(CultureInfo.InvariantCulture) + "k";
                return Iops.ToString(CultureInfo.InvariantCulture);
            }
        }

        internal string VariantSuffix => FamilyKey + "_" + SizeKey;

        /// <summary>Shop-label fragment: module recommendation when set, else lane hint.</summary>
        internal string RecommendedCable
        {
            get
            {
                if (!string.IsNullOrEmpty(RecommendedModule)) return RecommendedModule;
                return FiberLaneCount == 4 ? "4-lane fiber" : "1-lane fiber";
            }
        }

        // Tier ladder shared by every family (price rises with Gbps; more Gbit = teurer):
        //   size  IOPS     Gbps  Sfp  connector      price    xp
        //   100k  100_000    25   2    SFP28           20k     10k   3U
        //   500k  500_000    40   3    QSFP+          100k     25k   7U
        //   1m    1_000_000 100   3    QSFP28         250k     50k   7U   MoreModules 100G
        //   2m    2_000_000 200   3    QSFP56         500k    100k   7U   MoreModules 200G
        //   4m    4_000_000 400   3    QSFP-DD      1_000k    200k   7U   MoreModules 400G

        private static ServerVariantSpec Make(
            string familyKey, string baseDisplayName3, string asset3,
            string baseDisplayName7, string asset7,
            string displayNamePrefix, int iops, int price, int xp, int itemId,
            string connectorHint, float gbps, int sfpType, int lanes,
            string module, Color tint, Color familyBase, bool small, bool rgb = false)
        {
            return new ServerVariantSpec
            {
                FamilyKey = familyKey,
                BaseDisplayName = small ? baseDisplayName3 : baseDisplayName7,
                BaseAssetName = small ? asset3 : asset7,
                VariantId = $"greg_backplanes_{familyKey}_{(iops >= 1000000 && iops % 1000000 == 0 ? (iops / 1000000) + "m" : (iops / 1000) + "k")}",
                VariantDisplayName = $"{displayNamePrefix} {(iops >= 1000000 ? (iops / 1000000) + "M" : (iops / 1000) + "K")} IOPS",
                Iops = iops,
                Price = price,
                XpToUnlock = xp,
                VariantItemId = itemId,
                ConnectorHint = connectorHint,
                NetworkSpeedGbps = gbps,
                SfpType = sfpType,
                FiberLaneCount = lanes,
                RecommendedModule = module,
                BaseSpeed = small ? 0.05f : 0.12f,
                TintColor = tint,
                FamilyBaseColor = familyBase,
                ScaleY = small ? 4f / 3f : 8f / 7f,
                RgbAnimated = rgb,
            };
        }

        internal static readonly ServerVariantSpec[] All = BuildAll();

        private static ServerVariantSpec[] BuildAll()
        {
            // SystemX (yellow body)
            var yellow = new Color(1f, 1f, 0f, 1f);
            // RISC (blue body)
            var blue = new Color(0f, 0f, 1f, 1f);
            // Mainframe (purple body)
            var purple = new Color(0.5f, 0f, 1f, 1f);
            // GPU (green body)
            var green = new Color(0f, 1f, 0f, 1f);

            var list = new System.Collections.Generic.List<ServerVariantSpec>(20);

            // ---- SystemX
            list.Add(Make("systemx", "System X 3U 5000 IOPS", "ShopItemSO_Server_Yellow1",
                "System X 7U 12000 IOPS", "ShopItemSO_Server_Yellow2", "SystemX",
                100000, 20000, 10000, 9001, "SFP28", 25f, 2, 1, null,
                new Color(1.00f, 0.45f, 0.00f, 1f), yellow, small: true));
            list.Add(Make("systemx", "System X 3U 5000 IOPS", "ShopItemSO_Server_Yellow1",
                "System X 7U 12000 IOPS", "ShopItemSO_Server_Yellow2", "SystemX",
                500000, 100000, 25000, 9002, "QSFP+", 40f, 3, 4, "QSFP+ 40G",
                new Color(1.00f, 0.60f, 0.10f, 1f), yellow, small: false));
            list.Add(Make("systemx", "System X 3U 5000 IOPS", "ShopItemSO_Server_Yellow1",
                "System X 7U 12000 IOPS", "ShopItemSO_Server_Yellow2", "SystemX",
                1000000, 250000, 50000, 9009, "QSFP28", 100f, 3, 4, "QSFP28 100G",
                new Color(1.00f, 0.70f, 0.15f, 1f), yellow, small: false));
            list.Add(Make("systemx", "System X 3U 5000 IOPS", "ShopItemSO_Server_Yellow1",
                "System X 7U 12000 IOPS", "ShopItemSO_Server_Yellow2", "SystemX",
                2000000, 500000, 100000, 9010, "QSFP56", 200f, 3, 4, "QSFP56 200G",
                new Color(1.00f, 0.80f, 0.25f, 1f), yellow, small: false));
            list.Add(Make("systemx", "System X 3U 5000 IOPS", "ShopItemSO_Server_Yellow1",
                "System X 7U 12000 IOPS", "ShopItemSO_Server_Yellow2", "SystemX",
                4000000, 1000000, 200000, 9011, "QSFP-DD", 400f, 3, 4, "QSFP-DD 400G",
                new Color(1.00f, 0.90f, 0.40f, 1f), yellow, small: false));
            list.Add(Make("systemx", "System X 3U 5000 IOPS", "ShopItemSO_Server_Yellow1",
                "System X 7U 12000 IOPS", "ShopItemSO_Server_Yellow2", "SystemX",
                40000000, 10000000, 2000000, 9022, "QSFP-DD", 4000f, 3, 4, null,
                new Color(1.00f, 0.95f, 0.55f, 1f), yellow, small: false));

            // ---- RISC
            list.Add(Make("risc", "RISC 3U 5000 IOPS", "ShopItemSO_Server_Blue1",
                "RISC 7U 12000 IOPS", "ShopItemSO_Server_Blue2", "RISC",
                100000, 20000, 10000, 9003, "SFP28", 25f, 2, 1, null,
                new Color(0.55f, 0.20f, 1.00f, 1f), blue, small: true));
            list.Add(Make("risc", "RISC 3U 5000 IOPS", "ShopItemSO_Server_Blue1",
                "RISC 7U 12000 IOPS", "ShopItemSO_Server_Blue2", "RISC",
                500000, 100000, 25000, 9004, "QSFP+", 40f, 3, 4, "QSFP+ 40G",
                new Color(0.70f, 0.35f, 1.00f, 1f), blue, small: false));
            list.Add(Make("risc", "RISC 3U 5000 IOPS", "ShopItemSO_Server_Blue1",
                "RISC 7U 12000 IOPS", "ShopItemSO_Server_Blue2", "RISC",
                1000000, 250000, 50000, 9012, "QSFP28", 100f, 3, 4, "QSFP28 100G",
                new Color(0.75f, 0.45f, 1.00f, 1f), blue, small: false));
            list.Add(Make("risc", "RISC 3U 5000 IOPS", "ShopItemSO_Server_Blue1",
                "RISC 7U 12000 IOPS", "ShopItemSO_Server_Blue2", "RISC",
                2000000, 500000, 100000, 9013, "QSFP56", 200f, 3, 4, "QSFP56 200G",
                new Color(0.80f, 0.55f, 1.00f, 1f), blue, small: false));
            list.Add(Make("risc", "RISC 3U 5000 IOPS", "ShopItemSO_Server_Blue1",
                "RISC 7U 12000 IOPS", "ShopItemSO_Server_Blue2", "RISC",
                4000000, 1000000, 200000, 9014, "QSFP-DD", 400f, 3, 4, "QSFP-DD 400G",
                new Color(0.85f, 0.65f, 1.00f, 1f), blue, small: false));
            list.Add(Make("risc", "RISC 3U 5000 IOPS", "ShopItemSO_Server_Blue1",
                "RISC 7U 12000 IOPS", "ShopItemSO_Server_Blue2", "RISC",
                40000000, 10000000, 2000000, 9023, "QSFP-DD", 4000f, 3, 4, null,
                new Color(0.90f, 0.75f, 1.00f, 1f), blue, small: false));

            // ---- Mainframe
            list.Add(Make("mainframe", "Mainframe 3U 5000 IOPs", "ShopItemSO_Server_Purple1",
                "Mainframe 7U 12000 IOPs", "ShopItemSO_Server_Purple2", "Mainframe",
                100000, 20000, 10000, 9005, "SFP28", 25f, 2, 1, null,
                new Color(0.90f, 0.08f, 0.08f, 1f), purple, small: true));
            list.Add(Make("mainframe", "Mainframe 3U 5000 IOPs", "ShopItemSO_Server_Purple1",
                "Mainframe 7U 12000 IOPs", "ShopItemSO_Server_Purple2", "Mainframe",
                500000, 100000, 25000, 9006, "QSFP+", 40f, 3, 4, "QSFP+ 40G",
                new Color(1.00f, 0.15f, 0.15f, 1f), purple, small: false));
            list.Add(Make("mainframe", "Mainframe 3U 5000 IOPs", "ShopItemSO_Server_Purple1",
                "Mainframe 7U 12000 IOPs", "ShopItemSO_Server_Purple2", "Mainframe",
                1000000, 250000, 50000, 9015, "QSFP28", 100f, 3, 4, "QSFP28 100G",
                new Color(1.00f, 0.30f, 0.30f, 1f), purple, small: false));
            list.Add(Make("mainframe", "Mainframe 3U 5000 IOPs", "ShopItemSO_Server_Purple1",
                "Mainframe 7U 12000 IOPs", "ShopItemSO_Server_Purple2", "Mainframe",
                2000000, 500000, 100000, 9016, "QSFP56", 200f, 3, 4, "QSFP56 200G",
                new Color(1.00f, 0.45f, 0.45f, 1f), purple, small: false));
            list.Add(Make("mainframe", "Mainframe 3U 5000 IOPs", "ShopItemSO_Server_Purple1",
                "Mainframe 7U 12000 IOPs", "ShopItemSO_Server_Purple2", "Mainframe",
                4000000, 1000000, 200000, 9017, "QSFP-DD", 400f, 3, 4, "QSFP-DD 400G",
                new Color(1.00f, 0.60f, 0.60f, 1f), purple, small: false));
            list.Add(Make("mainframe", "Mainframe 3U 5000 IOPs", "ShopItemSO_Server_Purple1",
                "Mainframe 7U 12000 IOPs", "ShopItemSO_Server_Purple2", "Mainframe",
                40000000, 10000000, 2000000, 9024, "QSFP-DD", 4000f, 3, 4, null,
                new Color(1.00f, 0.70f, 0.70f, 1f), purple, small: false));

            // ---- GPU
            list.Add(Make("gpu", "GPU 3U 5000 IOPS", "ShopItemSO_Server_Green1",
                "GPU 7U 12000 IOPS", "ShopItemSO_Server_Green2", "GPU",
                100000, 20000, 10000, 9007, "SFP28", 25f, 2, 1, null,
                new Color(0.45f, 1.00f, 0.00f, 1f), green, small: true));
            list.Add(Make("gpu", "GPU 3U 5000 IOPS", "ShopItemSO_Server_Green1",
                "GPU 7U 12000 IOPS", "ShopItemSO_Server_Green2", "GPU",
                500000, 100000, 25000, 9008, "QSFP+", 40f, 3, 4, "QSFP+ 40G",
                new Color(0.65f, 1.00f, 0.15f, 1f), green, small: false));
            list.Add(Make("gpu", "GPU 3U 5000 IOPS", "ShopItemSO_Server_Green1",
                "GPU 7U 12000 IOPS", "ShopItemSO_Server_Green2", "GPU",
                1000000, 250000, 50000, 9018, "QSFP28", 100f, 3, 4, "QSFP28 100G",
                new Color(0.75f, 1.00f, 0.30f, 1f), green, small: false));
            list.Add(Make("gpu", "GPU 3U 5000 IOPS", "ShopItemSO_Server_Green1",
                "GPU 7U 12000 IOPS", "ShopItemSO_Server_Green2", "GPU",
                2000000, 500000, 100000, 9019, "QSFP56", 200f, 3, 4, "QSFP56 200G",
                new Color(0.85f, 1.00f, 0.45f, 1f), green, small: false));
            list.Add(Make("gpu", "GPU 3U 5000 IOPS", "ShopItemSO_Server_Green1",
                "GPU 7U 12000 IOPS", "ShopItemSO_Server_Green2", "GPU",
                4000000, 1000000, 200000, 9020, "QSFP-DD", 400f, 3, 4, "QSFP-DD 400G",
                new Color(0.95f, 1.00f, 0.60f, 1f), green, small: false));

            // ---- Titan (Custom): 4 TBit, beide Ports, RGB-Streifen ----
            list.Add(Make("titan", "GPU 3U 5000 IOPS", "ShopItemSO_Server_Green1",
                "GPU 7U 12000 IOPS", "ShopItemSO_Server_Green2", "Titan",
                40000000, 10000000, 2000000, 9021, "QSFP-DD", 4000f, 3, 4, null,
                new Color(1f, 1f, 1f, 1f), green, small: false, rgb: true));

            return list.ToArray();
        }

        /// <summary>
        /// Legacy ID prefixes accepted when reading markers written by older builds
        /// (pre-Workshop automator builds, Workshop v1.0.x bbs_/dc_automator_ builds,
        /// gregMod.Backplanes v2.0.x 125k IDs). New markers always use canonical IDs.
        /// </summary>
        private static readonly string[] LegacyPrefixes =
        {
            "dc_automator_", "data_center_automator_", "datacenter_automator_",
            "backplane_boost_", "backplaneboost_", "backplane_", "bbs_",
            "greg_backplanes_",
        };

        internal static ServerVariantSpec FindByVariantItemId(int variantItemId)
        {
            foreach (var spec in All)
            {
                if (spec.VariantItemId == variantItemId) return spec;
            }
            return null;
        }

        internal static ServerVariantSpec FindById(string variantId)
        {
            if (string.IsNullOrWhiteSpace(variantId))
                return null;            string normalized = NormalizeToken(variantId);
            var direct = All.FirstOrDefault(spec => VariantIdMatches(spec, normalized));
            if (direct != null) return direct;
            // v2.0.x wrote 125k IDs; v2.1.0 renamed the small tier to 100k.
            if (normalized.Contains("125k"))
            {
                string migrated = normalized.Replace("125k", "100k");
                return All.FirstOrDefault(spec => VariantIdMatches(spec, migrated));
            }
            return null;
        }

        internal static string CanonicalVariantId(string variantId)
        {
            return FindById(variantId)?.VariantId;
        }

        internal static ServerVariantSpec FindByShopValues(string displayName, int price)
        {
            if (!string.IsNullOrEmpty(displayName))
            {
                // Longest VariantDisplayName first so "…100K…" is not matched by a
                // shorter overlapping token (defensive; names are currently unique).
                var ordered = All.OrderByDescending(s => s.VariantDisplayName.Length);
                foreach (var s in ordered)
                {
                    if (displayName.IndexOf(s.VariantDisplayName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        displayName.IndexOf(s.VariantId, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s;
                }
            }
            // NOTE: deliberately no price-only fallback. The v1.x price-only fallback
            // configured vanilla servers that happened to share a price point and
            // produced phantom cart duplicates.
            return null;
        }

        private static bool VariantIdMatches(ServerVariantSpec spec, string normalized)
        {
            if (normalized == NormalizeToken(spec.VariantId)) return true;
            if (normalized == NormalizeToken(spec.VariantDisplayName)) return true;
            if (normalized == NormalizeToken(spec.VariantSuffix)) return true;
            return LegacyPrefixes.Any(prefix => normalized == NormalizeToken(prefix + spec.VariantSuffix));
        }

        private static string NormalizeToken(string text)
        {
            char[] buf = new char[text.Length];
            int n = 0;
            bool lastWasGap = false;
            foreach (char c in text.Trim())
            {
                char out_c = char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_';
                if (out_c == '_')
                {
                    if (lastWasGap) continue;
                    lastWasGap = true;
                }
                else lastWasGap = false;
                buf[n++] = out_c;
            }
            while (n > 0 && buf[n - 1] == '_') n--;
            return new string(buf, 0, n);
        }
    }
}
