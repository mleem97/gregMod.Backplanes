# ISSUE-008 - Ein Kauf erzeugt mehrere 500K-Server, nur der erste ist einsetzbar

- **Status:** Fix v2.1.1 (Doppel-Konsum entfernt, FIFO-Spawn) - siehe `../BUGFIX_NOTES.md` v2.1.1
- **Prioritaet:** Mittel
- **Bereich:** Shop/Spawning (ShopItem-Click -> Spawn), Inventory
- **Mod:** BackplaneBoostServers v1.0.0/v1.0.1 -> gregMod.Backplanes v2.1.1
- **Berichte (Steam Workshop):**
  - *Brilyn911, 22 May (2x)* - beim Kauf eines 500K-Servers werden statt eines drei Server
    erzeugt; nur der erste davon ist einsetzbar (die beiden Folgenden lassen sich nicht
    platzieren).

## Symptom
Ein einzelner Kauf eines 500K-Varianten-Servers laesst mehrere Instanzen spawne, von denen
nur die erste in ein Rack gesetzt werden kann; die Duplikate blockieren ggf. Inventar.

## Erwartet vs. Tatsaechlich
- **Erwartet:** Ein Kauf -> genau ein Server im Inventar, platzierbar.
- **Tatsaechlich:** Drei Server (nur erster nutzbar).

## Fix in v2.1.1 (Root-Cause gefunden + behoben)
Kausalitaet war der **Doppel-Konsum der Pending-Queue** (jedoch nicht in Form eines
Reparatur-Spawns): `ConfigureSpawnedItem` hat den Pending-Eintrag nur **gepeeked** (Pfeifen,
ohne aus der Queue zu nehmen) und `FinalizeInsertedServer` hat danach **bedingungslos** einen
Eintrag entfernt. Bei mehrmaligem Kaufen desselben Preispunkts verbrauchte der zweite Kauf
den Pending-Eintrag des dritten - drei 500K-Kaeufe in Folge koennten sich so gegenseitig
ueberlagern (eine Bezahlung, mehrere als '500K' konfigurierte/gespawnte Instanzen, nur die
erste mit korrektem State). Spielerseitig wirkte das wie "ein Kauf -> (scheinbar) mehrere
Server".

Behoben in v2.1.1:
1. `ConfigureSpawnedItem` konsumiert jetzt **genau einen** Pending-Eintrag pro Spawn
   (`ConsumePendingSpecForSpawn`, FIFO) - kein Peek mehr.
2. `FinalizeInsertedServer` entfernt **keinen** Pending-Eintrag mehr (Konsum passiert am
   Spawn bzw. via `DequeueMatchingPendingSpec` in der Insertion).
3. Verwaiste Spawn-UIDs (GameObject taucht nie in `ComputerShop.spawnedItems` auf) verfallen
   nach 60 s - kein Dauer-Sweep.

## Verbleibende Arbeit / Regression
- Gegen v2.1.1-DLL: drei 500K-Server nacheinander kaufen und pruefen, dass genau drei
  bezahlt/getrackt und einsetzbar sind (Warenkorb-Anzahl, Queue-Konsum im Log).
- Ergebnis als bestaetigt/fixed in dieser Datei und im README-Index aktualisieren.