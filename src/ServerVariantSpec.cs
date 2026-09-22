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
    /// <item>Network speed is Gbps / 5 (10G -&gt; 2, 25G -&gt; 5, 40G -&gt; 8).</item>
    /// </list>
    ///
    /// Visual differentiation (v2.1.0): each variant carries a TintColor applied to
    /// the family body materials at runtime plus a ScaleY giving the small variants
    /// a 4U look and the large ones an 8U look. Scale is visual only — rack slot
    /// occupancy (sizeInU) is unchanged (3U / 7U logically).
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
        /// <summary>Eigene Item-ID der Variante (9001-9008). Fix vergeben,
        /// zur Registrierung auf Kollision mit Vanilla-IDs geprueft.</summary>
        internal int VariantItemId;
        internal string ConnectorHint;
        internal float NetworkSpeedGbps;
        internal int SfpType;
        internal int FiberLaneCount;

        /// <summary>Base game speed of the 3U (5K IOPS) or 7U (12K IOPS) server this variant upgrades.</summary>
        internal float BaseSpeed;

        /// <summary>Runtime tint applied to the family body materials (see ServerVisuals).</summary>
        internal Color TintColor;

        /// <summary>Base body color used for proximity matching of materials (see ServerVisuals).</summary>
        internal Color FamilyBaseColor;

        /// <summary>Visual Y scale: 4/3 for 3U-based, 8/7 for 7U-based variants. Visual only.</summary>
        internal float ScaleY;

        internal string IopsText => Iops.ToString(CultureInfo.InvariantCulture);

        internal float RuntimeProcessingSpeed => Iops / 100000f;

        internal float RuntimeNetworkSpeed => NetworkSpeedGbps / 5f;

        internal float BaseRuntimeProcessingSpeed => BaseSpeed;

        internal string BaseRuntimeToken => BaseAssetName.Replace("ShopItemSO_", string.Empty, StringComparison.Ordinal);

        internal string SizeKey => Iops == 100000 ? "100k" : "500k";

        internal string VariantSuffix => FamilyKey + "_" + SizeKey;

        /// <summary>Recommended cable family shown in the shop label (guidance only, never enforced by blocking).</summary>
        internal string RecommendedCable => FiberLaneCount == 4 ? "4-lane fiber" : "1-lane fiber";

        internal static readonly ServerVariantSpec[] All =
        {
            new ServerVariantSpec { FamilyKey = "systemx",   BaseDisplayName = "System X 3U 5000 IOPS",  BaseAssetName = "ShopItemSO_Server_Yellow1", VariantId = "greg_backplanes_systemx_100k",   VariantDisplayName = "SystemX 100K IOPS",   Iops = 100000, Price = 20000,  XpToUnlock = 10000, VariantItemId = 9001, ConnectorHint = "SFP28", NetworkSpeedGbps = 25f, SfpType = 2, FiberLaneCount = 1, BaseSpeed = 0.05f, TintColor = new Color(1.00f, 0.45f, 0.00f, 1f), FamilyBaseColor = new Color(1f, 1f, 0f, 1f), ScaleY = 4f / 3f },
            new ServerVariantSpec { FamilyKey = "systemx",   BaseDisplayName = "System X 7U 12000 IOPS", BaseAssetName = "ShopItemSO_Server_Yellow2", VariantId = "greg_backplanes_systemx_500k",   VariantDisplayName = "SystemX 500K IOPS",   Iops = 500000, Price = 100000, XpToUnlock = 25000, VariantItemId = 9002, ConnectorHint = "QSFP+", NetworkSpeedGbps = 40f, SfpType = 3, FiberLaneCount = 4, BaseSpeed = 0.12f, TintColor = new Color(1.00f, 0.60f, 0.10f, 1f), FamilyBaseColor = new Color(1f, 1f, 0f, 1f), ScaleY = 8f / 7f },
            new ServerVariantSpec { FamilyKey = "risc",      BaseDisplayName = "RISC 3U 5000 IOPS",      BaseAssetName = "ShopItemSO_Server_Blue1",   VariantId = "greg_backplanes_risc_100k",      VariantDisplayName = "RISC 100K IOPS",      Iops = 100000, Price = 20000,  XpToUnlock = 10000, VariantItemId = 9003, ConnectorHint = "SFP28", NetworkSpeedGbps = 25f, SfpType = 2, FiberLaneCount = 1, BaseSpeed = 0.05f, TintColor = new Color(0.55f, 0.20f, 1.00f, 1f), FamilyBaseColor = new Color(0f, 0f, 1f, 1f), ScaleY = 4f / 3f },
            new ServerVariantSpec { FamilyKey = "risc",      BaseDisplayName = "RISC 7U 12000 IOPS",     BaseAssetName = "ShopItemSO_Server_Blue2",   VariantId = "greg_backplanes_risc_500k",      VariantDisplayName = "RISC 500K IOPS",      Iops = 500000, Price = 100000, XpToUnlock = 25000, VariantItemId = 9004, ConnectorHint = "QSFP+", NetworkSpeedGbps = 40f, SfpType = 3, FiberLaneCount = 4, BaseSpeed = 0.12f, TintColor = new Color(0.70f, 0.35f, 1.00f, 1f), FamilyBaseColor = new Color(0f, 0f, 1f, 1f), ScaleY = 8f / 7f },
            new ServerVariantSpec { FamilyKey = "mainframe", BaseDisplayName = "Mainframe 3U 5000 IOPs", BaseAssetName = "ShopItemSO_Server_Purple1", VariantId = "greg_backplanes_mainframe_100k", VariantDisplayName = "Mainframe 100K IOPS", Iops = 100000, Price = 20000,  XpToUnlock = 10000, VariantItemId = 9005, ConnectorHint = "SFP28", NetworkSpeedGbps = 25f, SfpType = 2, FiberLaneCount = 1, BaseSpeed = 0.05f, TintColor = new Color(0.90f, 0.08f, 0.08f, 1f), FamilyBaseColor = new Color(0.5f, 0f, 1f, 1f), ScaleY = 4f / 3f },
            new ServerVariantSpec { FamilyKey = "mainframe", BaseDisplayName = "Mainframe 7U 12000 IOPs",BaseAssetName = "ShopItemSO_Server_Purple2", VariantId = "greg_backplanes_mainframe_500k", VariantDisplayName = "Mainframe 500K IOPS", Iops = 500000, Price = 100000, XpToUnlock = 25000, VariantItemId = 9006, ConnectorHint = "QSFP+", NetworkSpeedGbps = 40f, SfpType = 3, FiberLaneCount = 4, BaseSpeed = 0.12f, TintColor = new Color(1.00f, 0.15f, 0.15f, 1f), FamilyBaseColor = new Color(0.5f, 0f, 1f, 1f), ScaleY = 8f / 7f },
            new ServerVariantSpec { FamilyKey = "gpu",       BaseDisplayName = "GPU 3U 5000 IOPS",       BaseAssetName = "ShopItemSO_Server_Green1",  VariantId = "greg_backplanes_gpu_100k",       VariantDisplayName = "GPU 100K IOPS",       Iops = 100000, Price = 20000,  XpToUnlock = 10000, VariantItemId = 9007, ConnectorHint = "SFP28", NetworkSpeedGbps = 25f, SfpType = 2, FiberLaneCount = 1, BaseSpeed = 0.05f, TintColor = new Color(0.45f, 1.00f, 0.00f, 1f), FamilyBaseColor = new Color(0f, 1f, 0f, 1f), ScaleY = 4f / 3f },
            new ServerVariantSpec { FamilyKey = "gpu",       BaseDisplayName = "GPU 7U 12000 IOPS",      BaseAssetName = "ShopItemSO_Server_Green2",  VariantId = "greg_backplanes_gpu_500k",       VariantDisplayName = "GPU 500K IOPS",       Iops = 500000, Price = 100000, XpToUnlock = 25000, VariantItemId = 9008, ConnectorHint = "QSFP+", NetworkSpeedGbps = 40f, SfpType = 3, FiberLaneCount = 4, BaseSpeed = 0.12f, TintColor = new Color(0.65f, 1.00f, 0.15f, 1f), FamilyBaseColor = new Color(0f, 1f, 0f, 1f), ScaleY = 8f / 7f },
        };

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
                var byName = All.FirstOrDefault(s =>
                    displayName.IndexOf(s.VariantDisplayName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    displayName.IndexOf(s.VariantId, StringComparison.OrdinalIgnoreCase) >= 0);
                if (byName != null)
                    return byName;
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
