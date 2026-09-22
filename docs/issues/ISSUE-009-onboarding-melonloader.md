# ISSUE-009 - Installation/Onboarding (MelonLoader + FixCoreModule) ist Support-Last

- **Status:** Teil-Fix (README-Install-Sektion gepflegt, v2.1.1); Workshop-Version offen
- **Prioritaet:** Niedrig
- **Bereich:** Docs/Ship-Being (Workshop-Seite, README)
- **Mod:** alle (betrifft BackplaneBoostServers / gregMod.Backplanes exemplarisch)
- **Berichte (Steam Workshop):**
  - *Brilyn911, 18 May* - gab eine Schritt-fuer-Schritt-Anleitung (MelonLoader 0.7.2/0.7.3
    via Steam mit MelonLoader-Installer, FixCoreModule als NEUER Mod-Loader /
    Compatibility-Layer) - wiederkehrender Support-Bedarf bei "Mod fehlt / DLL wird nicht
    geladen" / "Game wird gestartet, aber keine Mod".

## Symptom
Viele Nutzer scheitern am Setup: MelonLoader + FixCoreModule (fuer Unity 6000.x) +
Mod-DLL in `Data Center/Mods/`. Der Install-Workflow ist kein einmaliger Thread, sondern
regelmaessige Support-Anfragen.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Ein leicht auffindbarer Install-Pfad auf der Workshop- oder Projektseite.
- **Tatsaechlich:** Verstreut in Threads/Kommentaren; easige falsche Reihenfolge
  (FixCoreModule nicht als Mod, sondern als Loader-Helfer) -> Mod "erscheint nicht".

## Vorschlag
1. Kurze Install-Sektion in `README.md` (gilt auch fuer gregCore-abhaengige Mods):
   - MelonLoader (Steam-Installer), Ziel: "Data Center"
   - FixCoreModule in `Mods/` (Compatibility fuer Unity 6 / IL2CPP-Metadata)
   - gregMod.DLLs nach `Data Center/Mods/`
   - Validate: log/`F5`-Panel erscheint; Fehlersuche: `MelonLoader`-Ordner-Log.
2. Workshop-Beschreibung um die ersten 3 Schritte erweitern.
3. (Optional) `gregModmanager` als Installer bewerben, sobald die Workshop-/Steam-Quellen
   dort unterstuetzt werden.

## Stand v2.1.1
- README-`Installation`-Sektion ist gepflegt (MelonLoader >= 0.7.2, gregCore-Hinweis,
  V1-DLL-Entfernung, Marker-Migration): `README.md#Installation`.
- Offen: Workshop-Beschreibung (`SteamWorkshop_Beschreibungen.md`) um Schritt 1-3 erweitern
  und Install-Fehlersuche (MelonLoader-Log) verlinken.

## Verbleibende Arbeit
- Workshop-Seite: Install-Schritte 1-3 + Troubleshooting-Link einpflegen.