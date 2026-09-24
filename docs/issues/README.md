# Issue tracker — gregMod.Backplanes

Backlog from the Steam Workshop comments on **BackplaneBoostServers** (v1.0.0/v1.0.1,
legacy mod) → transferred to **gregMod.Backplanes** (v2.1.0). Each `ISSUE-###-*.md` file
documents one reported symptom incl. reproduction, environment, and a pointer to the
state in `../BUGFIX_NOTES.md`.

Status legend: **Open** (open / to verify) · **Fix v2.1.0** (recorded as fixed in the changelog,
regression open) · **Fix v2.1.1** (verified against the live assembly,
fixed in code) · **Fixed** (confirmed).

| # | Title | Status | Priority | Area | Reporter |
|---|-------|--------|-----------|---------|----------|
| [001](ISSUE-001-save-reload-config-verlust.md) | Modded servers lose IOPS + port config after save/reload | Fix v2.1.0 (To-Verify) | High | Save-Repair | DaSlayerOfGames, Felix_IT, M@dm@X, MFDuskink |
| [002](ISSUE-002-stackoverflow-saveload-crash.md) | Stack Overflow (0xC00000FD) when loading saves with modded servers | Fix v2.1.0 (To-Verify) | High | Save-Repair | BrassPeddler |
| [003](ISSUE-003-port-kabel-sfp-anforderungen.md) | Ports demand different cables/SFP on every load; 0G max; no re-plugging; 1 Gbps despite 500K | Fix v2.1.0 + v2.1.3 (To-Verify); Matrix v2.2.0 | High | Ports/Cables | Brilyn911, Shaun Eyebright, Playtest |
| [004](ISSUE-004-custom-farben-kabel-racks.md) | Custom colors for cables/racks always deliver the default color | Fix v2.1.0 (To-Verify) | Medium | Shop/Cart | Shaun Eyebright, Gothicdude1044, Revoltec, DNFplays |
| [005](ISSUE-005-shop-klick-doppelartikel.md) | Shop purchase: first click doesn't count / duplicate items on single payment | Fix v2.1.1 (spawning FIFO) | Medium | Shop/Cart | Shaun Eyebright |
| [006](ISSUE-006-inkompatibel-svcservice.md) | Incompatibility with the "Svc Service" mod → saves don't load | Open (To-Verify) | Medium | Compatibility | szymon511, BrassPeddler |
| [007](ISSUE-007-visuelle-varianten-unterscheidung.md) | No visual distinction between variants (looks "cheaty", 125K/5K easily confused) | OK in v2.1.0 (toggle), feedback open | Medium | Visuals | MFDuskink, H3draut3r, Gothicdude1044 |
| [008](ISSUE-008-mehrere-server-pro-kauf.md) | One purchase spawns multiple 500K servers, only the first is usable | Fix v2.1.1 (double-consume removed) | Medium | Shop/Spawning | Brilyn911 |
| [009](ISSUE-009-onboarding-melonloader.md) | Installation/onboarding (MelonLoader + FixCoreModule) is a support burden | Open (docs) | Low | Docs/Ship | Brilyn911 |
