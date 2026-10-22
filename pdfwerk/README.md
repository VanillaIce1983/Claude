# PDFwerk

PDF-Viewer und -Editor im Browser. Lesen wie in einem PDF-Reader, kommentieren und unterschreiben, dazu ein Werkzeugkasten zum Zusammenfügen, Teilen, Komprimieren und Umwandeln. Alles läuft lokal im Browser, keine Datei wird hochgeladen.

## Starten

- **Fertige Datei:** `PDFwerk.html` im Browser öffnen. Alle Bibliotheken stecken in dieser einen Datei, sie läuft auch ohne Internet.
- **Entwicklung:** `index.html` direkt öffnen (lädt die Bibliotheken von cdnjs). `npm install`, dann `npm run build` erzeugt `PDFwerk.html` neu, `npm test` spielt die wichtigsten Abläufe im Browser durch (Playwright mit Chromium).

Beim Start öffnet sich ein Beispieldokument mit Formular, damit sich alles sofort ausprobieren lässt. Eigene Dateien über „Öffnen“, Strg+O oder per Ziehen ins Fenster.

## Lesen

| Funktion | Details |
| --- | --- |
| Anzeige | Fortlaufendes Scrollen, Doppelseite, Zoom (Breite, ganze Seite, 25–600 %, Strg+Mausrad), Nachtmodus, Präsentation im Vollbild |
| Navigation | Seitenvorschau, Lesezeichen (Inhaltsverzeichnis der PDF), Links im Dokument und ins Web, Seitennummer eingeben, Bild auf/ab |
| Suche | Volltextsuche mit Hervorhebung aller Treffer, Enter / Umschalt+Enter springt |
| Text | Markieren und kopieren; aus der Auswahl direkt markieren, unterstreichen oder durchstreichen |
| Formulare | Werden angezeigt und lassen sich unter Werkzeuge → Formular ausfüllen bearbeiten |
| Geschützte PDFs | Öffnen mit Passwort; zum Bearbeiten wird eine entsperrte Kopie angelegt |

## Bearbeiten

Markieren, Freihand, Textfeld (Helvetica, Times, Courier, je normal und fett), Notiz, Rechteck, Ellipse, Linie, Pfeil, Abdecken, Schwärzen, Bild, Unterschrift (zeichnen, tippen oder Bild, im Browser merkbar), Stempel (Genehmigt, Entwurf, Vertraulich … mit Datum oder eigenem Text).

- **Text ersetzen:** Auf eine Zeile klicken. Sie wird abgedeckt und als bearbeitbares Textfeld in passender Größe, Farbe und Schriftart neu gesetzt.
- **Schwärzen** entfernt den Inhalt beim Speichern wirklich: Die betroffenen Seiten werden als Bild neu aufgebaut, der Text darunter ist danach nicht mehr vorhanden.
- Anmerkungen lassen sich auswählen, verschieben, skalieren, umfärben und löschen. Alles ist rückgängig zu machen (Strg+Z / Strg+Y).
- Notizen werden als echte PDF-Notizen gespeichert und beim erneuten Öffnen wieder bearbeitbar. Alle anderen Anmerkungen werden beim Speichern fest in die Seiten geschrieben.

## Seiten

Seitenübersicht mit Mehrfachauswahl (Strg/Umschalt), Sortieren per Ziehen, Drehen, Löschen, Duplizieren, Extrahieren, leere Seite einfügen, PDFs oder Bilder an beliebiger Stelle einfügen, Reihenfolge umkehren.

## Werkzeuge

| Gruppe | Werkzeuge |
| --- | --- |
| Zusammenstellen | Zusammenfügen (Reihenfolge wählbar), Teilen (alle N Seiten, nach Bereichen, gerade/ungerade, als ZIP), Seiten extrahieren, Bilder zu PDF (A4 hoch/quer/automatisch oder Bildgröße, Rand), leere Seiten entfernen, alle Seiten drehen |
| Bearbeiten | Formular ausfüllen (optional fixieren), Unterschreiben, Wasserzeichen (diagonal, waagerecht, gekachelt), Seitenzahlen sowie Kopf- und Fußzeilen mit Platzhaltern `{n}` `{N}` `{datum}` `{datei}`, Ränder zuschneiden, Dokumenteigenschaften |
| Optimieren und umwandeln | Komprimieren (verlustfrei oder 150/110/72 dpi), Graustufen, Reduzieren (Formulare und Anmerkungen einbrennen), PDF zu PNG/JPG, Text extrahieren |

## Grenzen

- Keine Texterkennung (OCR) und kein Setzen von Passwörtern.
- „Text ersetzen“ arbeitet mit den 14 PDF-Standardschriften. Sonderzeichen außerhalb von Windows-1252 werden beim Speichern als `?` geschrieben.
- Komprimieren (außer verlustfrei), Graustufen und Schwärzen bauen Seiten als Bild neu auf; dort ist Text danach nicht mehr markierbar.

## Aufbau

- `index.html` – Oberfläche und Gestaltung
- `app.js` – Programmlogik: Anzeige mit [pdf.js](https://mozilla.github.io/pdf.js/), Bearbeitung mit [pdf-lib](https://pdf-lib.js.org/), ZIP mit [JSZip](https://stuk.github.io/jszip/)
- `scripts/build.mjs` – erzeugt `PDFwerk.html` (alles in einer Datei) und `dist/artifact` (gehostete Fassung)
- `scripts/smoke.mjs` – Browsertest

PDFwerk ist eine eigenständige Entwicklung und steht in keiner Verbindung zu Adobe oder PDF24.
