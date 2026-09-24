# AGENTS.md — Notes for AI agents (gregMod.Backplanes)

Repo: gregMod.Backplanes · License: Apache-2.0 · Version: see `VERSION` (2.3.0).

MelonMod for Data Center (`BackplanesMod : MelonMod`). High-IOPS backplane
server variants. Refactored successor of BackplaneBoostServers v1.0.1.

## Duties

1. **Read first:** `README.md`, `docs/SOURCE_LAYOUT.md`, `docs/VARIANT_EXTENDING.md` — only then make changes.
2. **Do not commit secrets** (keys, tokens, `.env`). Use keys only via environment variables.
3. **Preserve history:** no `push --force`, no history rewrite without instruction.
4. **Verify changes:** before reporting done, build the mod (`dotnet build gregMod.Backplanes.csproj -c Release` or `./build.sh Backplanes` from `ModRepositories/`).
5. **Keep docs in sync:** for new features update `README.md` + `docs/` + `CHANGELOG.md` (Unreleased).
6. **Conventions:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:` …), one logical change per commit.
7. **When unsure:** stop and ask instead of guessing — especially for deletes, migrations, CI.

## Build and references

- Target: `net6.0`, x64, `AllowUnsafeBlocks`. Game: Data Center (`MelonGame("Waseku", "Data Center")`).
- `references/` holds absolute symlinks into the Steam Data Center install
  (MelonLoader, Il2Cpp, Unity, `Assembly-CSharp`, `gregCore.dll`). Never commit
  `references/*.dll`, `bin/`, or `obj/`.
- After a fresh clone, run `../tools/sync-melon-assemblies.sh` (source:
  `$DATACENTER_HOME`, fallback `~/.local/share/Steam/steamapps/common/Data Center`).
- Deploy only with `./build.sh Backplanes --deploy` (copies the DLL to `<Game>/Mods`).

## Hard rules

- **Never** touch gregCore types outside the soft-dependency probe path — the
  mod must load and work without `gregCore.dll`.
- **Panel key is F6** (F3 = Potato, F4 = FiberTrunk, F5 = NoEOL, F7 = Trainer,
  F8 = MultiCable, F9 = MusicPlayer, F10 = NotesHUD). Do not collide.
- Catalog injection goes through `CatalogInjector` + `RuntimeVariantRegistry`;
  keep variant specs data-driven (`ServerVariantSpec`) so new backplanes do
  not require patch changes. Extension guide: `docs/VARIANT_EXTENDING.md`.
- Defensive `try/catch` + null checks in every per-frame path; no per-frame reflection.

## Layout

- `src/BackplanesMod.cs` — MelonMod entry, prefs, toggle, scene hooks.
- `src/CatalogInjector.cs`, `src/RuntimeVariantRegistry.cs`, `src/ServerVariantSpec.cs` — variant registry.
- `src/Patches.cs`, `src/CableGuard.cs`, `src/RepairGuard.cs` — Harmony patches (observational first).
- `src/BackplanesOverlay.cs`, `src/UI/` — F6 overlay.
- `src/ServerVisuals.cs`, `src/RgbAnimator.cs`, `src/PortSpeedMemory.cs` — visuals/state.
- `src/Core/`, `src/Log.cs` — support.
- `_legacy/` — superseded code, do not extend. `docs/issues/` — known issues.
