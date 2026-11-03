Erste offizielle Version von **Blechbestand**, dem Windows-Programm für den Blechbestand im Lager. Es ersetzt die Excel-Liste „Blechbestand.xlsx“.

## Download und Start

1. **Blechbestand-1.0.0.zip** unten herunterladen und entpacken, z. B. nach `C:\Blechbestand`.
2. `Blechbestand.exe` doppelklicken. Eine Installation ist nicht nötig, Windows 10 und 11 bringen alles mit (.NET Framework 4.8).
3. Beim ersten Start wird die Bestandsdatei mit den 35 Positionen aus der Excel-Liste angelegt (`Dokumente\Blechbestand\bestand.xml`). Für mehrere Rechner unter **Einstellungen** dieselbe Datei auf dem Netzlaufwerk wählen.

Die Exe ist nicht digital signiert. Windows SmartScreen fragt deshalb beim ersten Start nach: **Weitere Informationen → Trotzdem ausführen**. Meldet ein Virenschutz wie G DATA einen Fehlalarm, trägt die IT den Programmordner als Ausnahme ein. Die Prüfsumme steht unten und in `SHA256.txt`.

## Funktionen

- **Bestand:** Liste wie in der Excel-Tabelle, nach Materialgruppen. Menge in die Zelle tippen, Enter speichert sofort. Ampel rot/gelb gegen den Mindestbestand, Filter und Suche, ±1-Knöpfe, größere Schrift für die Maschine.
- **Bestellung / Einkauf:** Positionen anhaken oder knappe vorschlagen lassen, Menge in Tafeln oder Paketen. „In Outlook öffnen“ erzeugt eine fertige Mail mit Tabelle und Gewicht (klassisches Outlook; sonst neue Mail plus Zwischenablage). Erst nach Bestätigung wird „bestellt am …“ vermerkt.
- **Sonderpositionen:** Material bestellen, das nicht im Bestand steht. Auf Wunsch wird es gleich dauerhaft in den Bestand aufgenommen.
- **Werkstoffliste:** 35 gängige Laser-Werkstoffe (Baustahl, hochfest/Hardox, verzinkt, Edelstahl, Aluminium, Kupfer/Messing) mit Gruppe und Dichte. Kurznamen wie S235JR, V2A oder V4A werden erkannt.
- **Lieferung buchen:** Rechtsklick auf die offene Bestellung → „Geliefert – Menge zum Bestand addieren“. Teillieferungen gehen auch, der Rest bleibt offen.
- **Bestellhistorie:** alle gebuchten Lieferungen, nach Lieferdatum oder Bestelldatum zusammengefasst, mit noch offenen Positionen, Filtern und Export für Excel.
- **Paketgewicht:** 1 Paket = höchstens 2.500 kg, die Tafelzahl wird abgerundet (z. B. 4 mm Stahl: 17 Tafeln ≈ 2.448 kg). Das Paketgewicht lässt sich einstellen, abweichende Packungen pro Material eintragen.
- **Mehrere Rechner gleichzeitig** (Laser und Büro) mit derselben Datei, tägliche Sicherungen und Änderungsprotokoll.
