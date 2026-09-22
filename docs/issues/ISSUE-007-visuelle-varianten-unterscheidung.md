# ISSUE-007 - Keine visuelle Unterscheidung der Varianten (wirkt "cheaty", 125K/5K verwechselbar)

- **Status:** Ok in v2.1.0 (Toggle `ServerTint`/`ServerScale`), Feedback offen
- **Prioritaet:** Mittel
- **Bereich:** Visuals (Tint/Scale), Shop-Karten-Labels
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.0
- **Berichte (Steam Workshop):**
  - *MFDuskink* - "the game frequently convert 125 into 5" + kein Unterschied sichtbar;
    125K-Karten sehen aus wie Standard.
  - *H3draut3r, 12 Aug* - Original-Modelle fuer beide Tiers sehen "cheaty" aus; Wunsch:
    Groessen-Skalierung (wie bei echten Servern: 500K = 40U, 125K = 15U) ggf. auch
    Farbschema fuer IOPS-Server.
  - *Gothicdude1044, 20 May* - Farben der modded Server bei Default-Schema der Vanilla
    belassen (kombinierbar mit ISSUE-004).

## Symptom
Die aufgeboosteten Server sind von Vanilla nicht unterscheidbar (gleiche 3U/7U-Groesse,
gleiche Farben) -> Spieler erkennen nach Reload nicht, ob ihre 125K/500K "noch da" sind;
125K wird leicht mit 5K verwechselt.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Varianten klar erkennbar (Groesse und/oder Farbe), Karten verstaendlich
  benannt.
- **Tatsaechlich:** v1.x liess Server optisch identisch mit Basis-Modellen.

## Stand in v2.1.0
- **Tint:** Familien-Materialien werden zur Laufzeit umgefaerbt (SystemX->orange,
  RISC->violett, Mainframe->rot, GPU->lime; 500K heller); Matching per Basisfarb-Naehe +
  Familien-Farbwort-Fallback; Screens/Lichter/Glas ausgenommen. Toggle `ServerTint` (on).
- **Scale:** absolute Y-Skalierung 4/3 (klein) / 8/7 (gross) fuer 4U/8U-Look. **Visuell
  nur** - Rack-Slot-Belegung unveraendert, Nachbarn koennen sich visuell ueberlappen.
  Toggle `ServerScale` (on).
- **100K-Tier:** kleine Varianten nun 100K IOPS (intern speed 1.0); IDs
  `greg_backplanes_*_100k`; alte `125k`-Marker (alle Legacy-Praefixe) loesen zu 100K auf
  und werden beim Speichern normalisiert. Preise/XP unveraendert.
- **Karten:** Shop-Karten tragen Labels "+ 1-lane fiber" / "+ 4-lane fiber" mit
  unterschiedlichen Preisen (seit v2.0.0).

## Verbleibende Arbeit / To-Verify
1. Skalen-Wirkung in dicht bepackten Racks pruefen (ueberlappt optisch mit Nachbarn) -
  ggf. Doku-Warnung auf der Workshop-Seite.
2. Spieler-Feedback einholen, ob Tint+Scale die Verwechslung 125K/5K aus der Welt schafft.
3. "40U/15U"-Grosserollwunsch (H3draut3r) technisch nicht umsetzbar (Rack-Slots sind es
   nicht), daher als bewusste Groessenentscheidung dokumentieren.