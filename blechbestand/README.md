# Blechbestand

Windows-Programm für den Blechbestand im Lager. Es ersetzt die Excel-Liste „Blechbestand.xlsx“:

- **Reiter „Bestand“:** die Liste wie in der Excel-Tabelle. Der Mann an der Laserschneidmaschine trägt die noch vorhandene Menge direkt in die Zelle ein, Enter speichert sofort.
- **Reiter „Bestellung / Einkauf“:** Materialien anhaken, Menge eingeben und die Bestellung als fertige E-Mail an den Einkauf in Outlook öffnen.

![Logo](assets/logo-256.png)

## Starten

`Blechbestand.exe` doppelklicken, eine Installation ist nicht nötig. Das Programm braucht nur das .NET Framework 4.8, und das ist in Windows 10 und 11 schon enthalten.

Beim ersten Start meldet Windows SmartScreen eventuell „Der Computer wurde durch Windows geschützt“, weil die Datei nicht signiert ist. Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.

Beim allerersten Start legt das Programm die Bestandsdatei an und füllt sie mit den Daten aus der Excel-Liste (Stand 02.11.2026, 35 Positionen).

## Reiter „Bestand“

| Was | Wie |
| --- | --- |
| Menge eintragen | Zelle in **Pakete** oder **Tafeln** anklicken, Zahl tippen, **Enter**. Leer oder „-“ = keine. Auch `1,5` geht. |
| Schnell ±1 | Zelle markieren, dann **−1** / **+1** unten rechts (oder die Tasten − und +) |
| Ampel | Rot = unter Mindestbestand, gelb = Mindestbestand genau erreicht. Die Mindestbestände stehen in eigenen Spalten. |
| Neues Material | **+ Neues Material** (Strg+N). Gruppe, Werkstoff, Dicke, Dimension, Eulandanummer, Mindestbestand. Das Gewicht pro Tafel wird aus Format, Dicke und Dichte berechnet. Ein neuer Gruppenname legt eine neue Gruppe an. |
| Bearbeiten / Löschen | Knöpfe oben, Doppelklick oder Rechtsklick auf die Zeile |
| Rechtsklick auf eine Gruppe | Umbenennen, nach oben oder unten verschieben |
| Filter | Suche, Materialgruppe, Dicke, „Nur knappe Bestände“ (wie die Datenschnitte in der Excel-Liste) |
| Lieferung | Wird die Menge einer Position mit vermerkter Bestellung erhöht, fragt das Programm, ob die Lieferung da ist, und entfernt den Vermerk |
| Verlauf | Wer hat wann welche Menge geändert (öffnet sich in Excel) |
| Export für Excel | Der ganze Bestand als CSV-Datei |

## Reiter „Bestellung / Einkauf“

1. Positionen anhaken (Klick auf die Zeile oder Leertaste). Die Bestellmenge wird vorgeschlagen: bei Paketen 1 Paket, bei Tafeln bis zum doppelten Mindestbestand. Der Vorschlag lässt sich überschreiben, die Einheit (Tafeln/Pakete) ebenso.
2. Oder **Knappe Positionen vorschlagen**: Damit wird alles Rote und Gelbe angehakt, was noch nicht bestellt ist.
3. Rechts Empfänger, Betreff und Text prüfen. Empfänger und Texte merkt sich das Programm.
4. **In Outlook öffnen**: Eine neue E-Mail erscheint mit der Bestellliste als Tabelle (Werkstoff, Dicke, Format, Menge, Eulanda-Nr., Gewicht). Deine Outlook-Signatur bleibt darunter erhalten. Gesendet wird erst mit „Senden“ in Outlook.
5. Danach steht an den Positionen „10 Tafeln am 02.11.2026“ (Spalte **Offene Bestellung**). Abschalten lässt sich das mit dem Häkchen „Danach als bestellt vermerken“.

**Outlook:** Direkt angesteuert wird das klassische Outlook (Microsoft 365 / Office). Das „neue Outlook für Windows“ hat dafür keine Schnittstelle. Dann öffnet das Programm die Bestellung als E-Mail-Datei (.eml) mit dem Standard-Mailprogramm. Klappt auch das nicht: **In Zwischenablage kopieren** und in eine neue Mail einfügen (Strg+V), die Tabelle bleibt dabei erhalten.

## Mehrere Rechner (Laser und Büro)

Standardmäßig liegt die Bestandsdatei unter `Dokumente\Blechbestand\bestand.xml`. Damit Laser-Rechner und Büro dieselben Zahlen sehen, gibt es zwei Wege:

- **Einstellungen → Andere Datei wählen …** an jedem Rechner auf dieselbe Datei auf dem Netzlaufwerk zeigen lassen, z. B. `\\server\lager\bestand.xml`. Gibt es dort noch keine, bietet das Programm an, den aktuellen Bestand dorthin zu kopieren.
- Oder `Blechbestand.exe` zusammen mit einer `bestand.xml` in einen gemeinsamen Ordner legen. Jeder, der das Programm von dort startet, benutzt automatisch diese Datei.

Mehrere Rechner dürfen gleichzeitig arbeiten. Jede Eingabe sperrt die Datei kurz, liest den neuesten Stand, ändert nur diesen einen Wert und speichert. So überschreibt niemand die Eingaben eines anderen. Änderungen anderer Rechner erscheinen nach spätestens 15 Sekunden von selbst (sofort mit F5).

Neben der Datei legt das Programm an:

- `Sicherungen\bestand_JJJJ-MM-TT.xml`: eine Sicherung pro Tag vor der ersten Änderung, die letzten 60 bleiben
- `bestand_verlauf.csv`: das Änderungsprotokoll (Zeitpunkt, Benutzer, Rechner, Position, alt, neu)

Persönliche Einstellungen (Empfänger, Fenstergröße, Schriftgröße) liegen unter `%APPDATA%\Blechbestand\einstellungen.xml`. Für die Lasermaschine lässt sich unter **Einstellungen** eine größere Tabellenschrift wählen.

## Übernahme aus der Excel-Liste

- Alle 35 Positionen mit Paketen, Tafeln, Eulandanummer und Gewicht pro Tafel. „gebürstet“ (bisher in Spalte H neben der Tabelle) steht jetzt in der Spalte **Bemerkung**.
- **Mindestbestände** kommen aus der bedingten Formatierung der Excel-Liste. Die Regel ist jetzt überall gleich: rot = darunter, gelb = genau erreicht. Alle 13 bisher roten und alle 5 bisher gelben Positionen bleiben rot bzw. gelb. Vier Positionen, die genau auf ihrem Mindestbestand stehen, sind jetzt zusätzlich gelb: 1.0330 2 mm, 1.0332 6 mm, 1.0332 8 mm, 1.4301 10 mm.
- Die „3“ in der Überschriftszeile „Aluminium NOX“ wurde nicht übernommen, die Position EN AW-5005A 2 mm mit 5 Tafeln schon.
- Die Notiz „Blechbestellung 27.10.2026“ ist als offene Bestellung eingetragen: „1 Paket T4ST“ bei 1.0332 4 mm, „10 Tafeln T2ALNOX“ bei EN AW-5005A 2 mm. Bitte prüfen, ob das stimmt.
- Fehlende Eulandanummern (1.4301 6 mm und 10 mm, 3.3535 5/6/8 mm) bleiben leer und können über **Bearbeiten** nachgetragen werden.

## Aus dem Quelltext bauen

Voraussetzung: .NET SDK 8 (Windows, Linux oder macOS).

```
cd blechbestand
dotnet run -c Release --project tests/Blechbestand.Tests.csproj   # Tests der Datenlogik
dotnet build -c Release src/Blechbestand.csproj                   # → src/bin/Release/net48/Blechbestand.exe
```

Der Workflow `.github/workflows/blechbestand-windows.yml` baut und testet auf Windows, startet das Programm einmal, macht einen Screenshot und stellt `Blechbestand.exe` als Artefakt bereit.

| Ordner | Inhalt |
| --- | --- |
| `src/Model` | Daten, Speichern mit Sperre und Sicherung, Startdaten, E-Mail-Aufbau (ohne Oberfläche, getestet) |
| `src/UI` | Hauptfenster, Reiter Bestand und Bestellung, Dialoge, Outlook-Anbindung |
| `tests` | Prüfungen der Datenlogik, u. a. zwei Rechner, die gleichzeitig speichern |
| `assets`, `scripts/make_icon.py` | Logo (blauer Hintergrund mit Blechtafel-Stapel) und das Skript, das es zeichnet |
