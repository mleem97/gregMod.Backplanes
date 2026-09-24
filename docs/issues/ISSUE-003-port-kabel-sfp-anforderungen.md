# ISSUE-003 - Ports demand different cables/SFP on every load; 0G max; no re-plugging

- **Status:** Fix v2.1.0 (To-Verify) + Fix v2.1.3 port speed 1 Gbps (To-Verify) - see `../BUGFIX_NOTES.md` #4 + v2.1.3; Matrix v2.2.0 (20 variants / MoreModules)
- **Priority:** High
- **Area:** ConfigurePort / ConfigureCableLink, CableGuard, CollectServerLinks, RegisterLink
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0/v2.1.3/v2.2.0
- **Reports (Steam Workshop):**
  - *Brilyn911, 22 May* - requirements change with every save/load: sometimes a port
    needs Ethernet, sometimes SFP, sometimes 4-way SFP; after reload "nothing else back in" would plug.
  - *Shaun Eyebright, 24 May* - after powering on a server, ports show "0 gbps max";
    after pulling a cable, no cable can be connected anymore.
  - *Brilyn911, 22 May (Speed)* - 1G SFP/Ethernet links only run at 10/10; uplink at
    25/25 despite 1-lane fiber - modest bandwidth expectation (partly a game/topology question,
    not necessarily a bug; record as to-verify docs).
  - *Playtest, 23 Sep (v2.1.2)* - fresh 500K servers: ports stay at "1 Gbps" although
    IOPS verify is OK (`Ports geprueft=0`); even without reload. -> Fix v2.1.3.

## Symptom
Port configuration is not stable: cable/SFP requirements change between loads,
ports show "0 gbps max" and refuse (re-)plugging of cables.

## Expected vs. actual
- **Expected:** Ports keep their type; cables plug in as long as slot + cable
  match each other; ports show their real max rate.
- **Actual:** Repair also overwrote occupied ports, nulled inserted SFPs, and
  the `CableLink.InteractOnClick` prefix blocked connections based on lane heuristics.

## Known root cause (v1.0.x, from `../BUGFIX_NOTES.md` #4)
`ConfigureCableLink` rewrote `connectionSpeed`, `sfpType*`, `isSFP/FibrePort` on **every**
repair (including occupied, connected ports) and nulled `insertedSFP`; the
`InteractOnClick` prefix returned `false` depending on lane heuristics (misclassification).

## Fix in v2.1.0
`ConfigurePort` returns immediately for connected ports (`cableIDsOnLink != 0` or `insertedSFP`)
and never removes an inserted module; `CableGuard` is now warn/log only
(always `return true`, toggle `CableWarnings`); empty ports are always fully
normalized (speed cap + `sfpTypeSupported/Inserted` + `isSFP/isFibrePort` together).
Click diagnostics compare `cableIDsOnLink` before/after `InteractOnClick` and log rejection
reasons if a cable is refused.

## Fix in v2.1.3 (1 Gbps despite 500K)
`CollectServerLinks` saw 0 ports at insert (timing/`typeOfLink=None`). New:
`Server.RegisterLink` + `CableLink.Start` postfixes configure known
variant ports immediately; child scan accepts `typeOfLink.None`; empty hit ->
`FindObjectsOfTypeAll` via `parentServer` pointer; watchlist checks free
`connectionSpeed` values every 5 s and retries at 0 ports.

## v2.2.0 (Matrix / MoreModules)
20 variants (100K–4M), free ports pre-profiled (25/40/100/200/400 Gbps), from 40G
`sfpType=3`; shop label names the recommended MoreModules module. Occupied ports
still never rewritten.

## Remaining work / To-Verify
1. Flow "pull cable -> plug back in" and "power on server -> port rate" against v2.1.0.
2. "0 gbps max" after power-on - was that a side effect of the repair writes or a real
   rate? Check with the diagnostics log.
3. Document bandwidth expectation 10/10 and 25/25 (Brilyn911) separately (game mechanics
   vs. mod); possibly a README note, no code change.
4. Live log v2.1.3+: `Ports gefunden>0`, `abweichend=0`, UI shows 40/100/200/400 Gbps
   on 500K–4M servers (without reload and after save/reload).
5. Plug MoreModules modules (100G–400G) into QSFP+ ports without port hacks.
