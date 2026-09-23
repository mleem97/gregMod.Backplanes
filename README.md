# gregMod.Backplanes

> High-IOPS backplane server variants for **Data Center** — refactored successor of the abandoned `BackplaneBoostServers` v1.0.1 mod.

[![License](https://img.shields.io/badge/License-Apache%202.0-green?style=for-the-badge)](./LICENSE)
[![Version](https://img.shields.io/badge/Version-2.2.3-orange?style=for-the-badge)]()
[![GameVersion](https://img.shields.io/badge/Game%20Version-1.1.0-yellow?style=for-the-badge)]()
[![Unity](https://img.shields.io/badge/Unity-6000.4.12f1-black?style=for-the-badge&logo=unity&logoColor=white)]()

## Links

- **Steam Workshop:** [My Workshop (Data Center)](https://steamcommunity.com/id/frikadelle3000/myworkshopfiles/?appid=4170200)
- **Discord / Support:** [discord.gg/greg](https://discord.gg/greg)
- **Website:** [gregframework.eu](https://gregframework.eu)

## Overview

Adds buyable high-IOPS backplane server variants to the shop. Boosted servers are
visually distinct: recolored body (orange / violet / red / lime per family) and a
taller look (4U/8U style — visual only, rack slots unchanged).

Five bandwidth tiers per family (**20 variants**), aligned with
[gregMod.MoreModules](https://steamcommunity.com/workshop/filedetails/?id=3719510811)
(QSFP28/56/DD modules, same vanilla QSFP+ `sfpType` — ports accept them out of the box):

| Tier | IOPS | Uplink / port | Recommended module | Price | Unlock | Base |
|------|------|---------------|--------------------|-------|--------|------|
| 100K | 100 000 | 25G | SFP28 (1-lane fiber) | 20 000 $ | 10 000 xp | 3U |
| 500K | 500 000 | 40G | QSFP+ 40G | 100 000 $ | 25 000 xp | 7U |
| 1M | 1 000 000 | 100G | QSFP28 100G | 250 000 $ | 50 000 xp | 7U |
| 2M | 2 000 000 | 200G | QSFP56 200G | 500 000 $ | 100 000 xp | 7U |
| 4M | 4 000 000 | 400G | QSFP-DD 400G | 1 000 000 $ | 200 000 xp | 7U |

Families: **SystemX · RISC · Mainframe · GPU** (item IDs 9001–9020; no clash with
MoreModules IDs 1000–3999). More Gbps ⇒ higher price. Shop labels show the
recommended module; empty ports are pre-profiled for that form factor (connected
ports are never rewritten).

## In-game panel (F6)

Press **F6** for the Backplanes panel — same UI Toolkit stack as gregMod.MusicPlayer
(`GregPanel` with camera/movement/interact locking + cursor, game font, click routing).
Requires **gregCore** to be installed.

- **Visuals** — Server Tint and Taller Look ON/OFF buttons, apply within seconds
  (tint-off takes effect going forward; everything is vanilla after reload)
- **Behavior** — Cable Warnings, Verbose Logging
- **Repair** — repair-window display with −/+ steppers, live status
  (shop variants ready / repaired this scene / persisted markers), **Repair now** button

There is also a **Backplanes tab in the gregCore settings hub** with the same toggles.
All settings are stored in MelonPreferences (`Data Center/UserData/MelonPreferences.cfg`,
section `[gregMod.Backplanes]`) and can still be edited by hand while the game is closed.

## Fixes vs v1.0.1

See [docs/BUGFIX_NOTES.md](./docs/BUGFIX_NOTES.md) for the full report → fix mapping. Headlines:

- **Save/load keeps IOPS + ports** — time-boxed repair sweep (`Server.Start` postfix + 1/sec scan), column-tolerant sidecar, self-healing runtime inference. No more rebuying servers after reload.
- **No more stack-overflow crash** on saves with modded servers (native-pointer re-entrancy guard).
- **Ports stay connectable** — connected ports are never rewritten; cable check is warn-only and never blocks.
- **No shop lag** — one typed shop scan per shop-open, no per-frame work, no reflection graph scans.
- **Custom-color cables/racks untouched** — custom-color purchases are never intercepted.
- **No phantom cart duplicates** — strict name-based spec resolution, no price-only fallback.

## Dependencies

- [MelonLoader](https://melonwiki.xyz/) v0.7.2 or newer
- gregCore (for the F6 panel + settings tab; logic works without it, panel needs it)

## Installation

1. Install MelonLoader for Data Center if you haven't already
2. Copy `gregMod.Backplanes.dll` into `Data Center/Mods/`
3. Launch the game — the variants appear in the shop

Old `BackplaneBoostServers.dll` must be **removed** (both mods register the same variants; do not run them side by side).
Existing `UserData/BackplaneBoostServers/server-variants.tsv` markers are migrated automatically
(legacy `dc_automator_*` / `bbs_*` IDs included; v2.0.x `125k` IDs migrate to `100k`).

## Settings (MelonPreferences → gregMod.Backplanes)

| Entry | Default | Meaning |
|-------|---------|---------|
| `RepairWindowSeconds` | 20 | Seconds after scene load during which save-loaded servers are repaired |
| `CableWarnings` | true | Hint when the held cable differs from the recommended family (never blocks) |
| `ServerTint` | true | Recolor boosted servers to tell them apart |
| `ServerScale` | true | Taller look for boosted servers (visual only, rack slots unchanged) |
| `VerboseLogging` | false | Detailed logging for troubleshooting |

## Build from Source

Requirements:

- .NET 6 SDK (build verified with .NET 8/10 SDKs targeting net6.0)

```bash
dotnet build -c Release
```

Release output: `bin/Release/net6.0/gregMod.Backplanes.dll`

## Project Structure

```
gregMod.Backplanes/
├── src/
│   ├── BackplanesMod.cs      # MelonMod entry, preferences, scene lifecycle
│   ├── ServerVariantSpec.cs  # The 8 variants + legacy ID migration
│   ├── ServerVisuals.cs      # Runtime tint + scale (visual differentiation)
│   ├── CatalogInjector.cs    # Typed shop/purchase/spawn/insert/repair logic
│   ├── RuntimeVariantRegistry.cs # Save-safe serverId→variant sidecar
│   ├── RepairGuard.cs        # Native-pointer re-entrancy guard (anti-crash)
│   ├── CableGuard.cs         # Warn-only cable guidance (never blocks)
│   ├── Patches.cs            # Minimal Harmony surface
│   └── Log.cs
├── references/               # Game + MelonLoader assemblies (compile only)
├── docs/
│   ├── BUGFIX_NOTES.md       # v1.x issue → v2 fix mapping
│   └── VARIANT_EXTENDING.md  # How to add your own variants
├── _legacy/                  # Original v1.0.1 DLL (reference only)
└── README.md
```

## Credits

- Original mod: [HighFreak1c — Backplane Boost Servers](https://steamcommunity.com/app/...) v1.0.1
- Save-reload root-cause analysis + companion-mod verification: Brilyn911, Cobalt (Steam Workshop discussion)
- Refactor into `gregMod.Backplanes` v2.0.0: [TeamGreg Modding](https://gregframework.eu)

## License

Apache-2.0, see [LICENSE](./LICENSE).
