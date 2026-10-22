# PDFwerk

PDF-Viewer und -Editor im Browser und als Desktop-Programm für Windows und macOS. Lesen wie in einem PDF-Reader, kommentieren und unterschreiben, dazu ein Werkzeugkasten zum Zusammenfügen, Teilen, Komprimieren und Umwandeln. Alles läuft lokal, keine Datei wird hochgeladen.

## Starten

- **Desktop-Programm:** Installierbar unter Windows und macOS, öffnet PDFs per Doppelklick und speichert direkt in die Originaldatei, siehe [Desktop-Programm](#desktop-programm-windows-und-macos).
- **Fertige Datei:** `PDFwerk.html` im Browser öffnen. Alle Bibliotheken stecken in dieser einen Datei, sie läuft auch ohne Internet.
- **Entwicklung:** `index.html` direkt öffnen (lädt die Bibliotheken von cdnjs). `npm install`, dann `npm run build` erzeugt `PDFwerk.html` neu, `npm test` spielt die wichtigsten Abläufe im Browser durch (Playwright mit Chromium). `npm run desktop` startet die Desktop-Fassung aus dem Quelltext.

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

## Desktop-Programm (Windows und macOS)

Dieselbe Oberfläche als installierbares Programm ([Electron](https://www.electronjs.org/)). Es läuft komplett offline und arbeitet direkt mit den Dateien auf der Festplatte.

### Herunterladen

- **Versionen:** Zu jedem Tag `pdfwerk-v…` (z. B. `pdfwerk-v1.0.0`, muss zur Version in `package.json` passen) liegen die Pakete im Repository unter „Releases“.
- **Zwischenstände:** Im Repository unter „Actions“ den Workflow „pdfwerk-desktop“ wählen, einen grünen Lauf öffnen und unten unter „Artifacts“ `pdfwerk-windows`, `pdfwerk-macos` oder `pdfwerk-linux` laden. Das ist ein ZIP, in dem die eigentlichen Pakete liegen. Dafür ist eine Anmeldung bei GitHub nötig, die Artefakte bleiben 30 Tage erhalten.

| System | Datei |
| --- | --- |
| Windows (fast alle PCs) | `PDFwerk-Setup-1.0.0-x64.exe` – Installationsprogramm |
| Windows auf ARM (z. B. Snapdragon) | `PDFwerk-Setup-1.0.0-arm64.exe` |
| Windows ohne Installation | `PDFwerk-1.0.0-win-x64.zip` bzw. `PDFwerk-1.0.0-win-arm64.zip` entpacken und `PDFwerk.exe` starten |
| macOS mit Apple Silicon (M1 und neuer) | `PDFwerk-1.0.0-arm64.dmg` |
| macOS mit Intel-Prozessor | `PDFwerk-1.0.0-x64.dmg` |
| Linux | `PDFwerk-1.0.0-x86_64.AppImage` (ausführbar machen und starten) |

Welcher Mac: Apple-Menü → „Über diesen Mac“, Eintrag „Chip“ (Apple M…) oder „Prozessor“ (Intel).

Voraussetzungen: Windows 10 oder 11, macOS 13 (Ventura) oder neuer.

### Installieren

- **Windows:** Setup starten und den Schritten folgen (für mich oder alle Benutzer, Ordner wählbar). PDFwerk erscheint im Startmenü und auf dem Desktop, PDFs bietet Windows danach unter „Öffnen mit“ an. Als Standard für PDFs: Rechtsklick auf eine PDF → „Öffnen mit“ → „Andere App auswählen“ → PDFwerk → „Immer“.
- **macOS:** DMG öffnen und PDFwerk in den Ordner „Programme“ ziehen. Als Standard für PDFs: PDF im Finder auswählen → Cmd+I → „Öffnen mit“ → PDFwerk → „Alle ändern …“.

### Warnung beim ersten Start

Die Pakete sind ohne Zertifikat gebaut (siehe [Code-Signierung](#code-signierung)), deshalb warnt das System beim ersten Start.

- **Windows SmartScreen** („Der Computer wurde durch Windows geschützt“): „Weitere Informationen“ → „Trotzdem ausführen“.
- **macOS** („Apple konnte nicht überprüfen …“): Rechtsklick auf PDFwerk in „Programme“ → „Öffnen“ → „Öffnen“. Ab macOS 15 stattdessen einmal starten, dann Systemeinstellungen → Datenschutz & Sicherheit → „Dennoch öffnen“. Meldet macOS, das Programm sei beschädigt, hilft im Terminal:

  ```
  xattr -dr com.apple.quarantine /Applications/PDFwerk.app
  ```

Danach startet PDFwerk ohne Rückfrage. Beim ersten Speichern in „Schreibtisch“, „Dokumente“ oder „Downloads“ kann macOS einmal um Erlaubnis fragen, dann „Erlauben“ wählen.

### Was die Desktop-Fassung zusätzlich kann

- **Öffnen:** Doppelklick auf eine PDF, „Öffnen mit“ oder Ziehen aufs Programmsymbol. Jedes weitere Dokument bekommt ein eigenes Fenster.
- **Speichern** (Strg+S, macOS Cmd+S) schreibt direkt in die Originaldatei, über eine temporäre Datei, damit bei einem Fehler nichts verloren geht. **Speichern unter** (Umschalt+Strg+S) mit dem Dialog des Systems.
- **Ungespeicherte Änderungen:** Der Fenstertitel zeigt sie an (macOS: Punkt im Schließknopf). Beim Schließen oder Beenden fragt PDFwerk nach: Speichern, Nicht speichern oder Abbrechen.
- **Zuletzt geöffnet** im Menü „Datei“, unter macOS auch im Dock-Menü.
- **Drucken** (Strg+P) über den Druckdialog des Systems.
- **Offline:** Bibliotheken und Schriften stecken im Programm, es baut keine Verbindung ins Internet auf.
- **Menüs und Tastenkürzel** wie gewohnt; unter macOS gilt Cmd, wo sonst Strg steht.

### Selbst bauen

```
npm install
npm run desktop        # aus dem Quelltext starten
npm run dist:win       # Windows: Installer und ZIP für x64 und arm64
npm run dist:mac       # macOS: DMG und ZIP für Apple Silicon und Intel, nur auf einem Mac
npm run dist:linux     # Linux: AppImage
npm run test:desktop   # Desktop-Test mit Electron
```

Die Pakete landen in `release/`. Windows-Pakete entstehen am einfachsten unter Windows, Mac-Pakete nur unter macOS. `npm run test:desktop` startet ohne Weiteres die Fassung aus dem Quelltext; mit `PDFWERK_EXE=<Pfad zum Programm>` prüft es ein gepacktes Programm, z. B. `release/win-unpacked/PDFwerk.exe` oder `release/mac-arm64/PDFwerk.app/Contents/MacOS/PDFwerk`.

Das Programmsymbol entsteht aus `build/icon.svg`: `node scripts/make-icons.mjs` schreibt `build/icon.png` (1024 × 1024, braucht Playwright mit Chromium), daraus erzeugt electron-builder `.ico` und `.icns`.

Bei jedem Push auf Dateien unter `pdfwerk/` baut GitHub Actions alle drei Systeme, startet das gepackte Programm zur Probe und stellt die Pakete als Artefakte bereit.

### Code-Signierung

Ohne Zertifikat sind die Windows-Pakete unsigniert und die Mac-Pakete nur ad hoc signiert, daher die Warnungen oben. Für signierte Pakete:

- **Windows:** Code-Signing-Zertifikat als `.pfx` über `CSC_LINK` (Pfad oder base64) und `CSC_KEY_PASSWORD`.
- **macOS:** Zertifikat „Developer ID Application“ (Apple Developer Program) ebenfalls über `CSC_LINK` und `CSC_KEY_PASSWORD`; für die Notarisierung zusätzlich `APPLE_ID`, `APPLE_APP_SPECIFIC_PASSWORD` und `APPLE_TEAM_ID`.

In GitHub Actions kommen diese Werte als Secrets in den Schritt „Pakete bauen“ des Workflows, `CSC_IDENTITY_AUTO_DISCOVERY: false` fällt dann weg. Für macOS außerdem in `package.json` unter `build.mac` die Einträge `identity` (ad hoc), `hardenedRuntime` und `notarize` entfernen, die Notarisierung verlangt die Hardened Runtime.

## Aufbau

- `index.html` – Oberfläche und Gestaltung
- `app.js` – Programmlogik: Anzeige mit [pdf.js](https://mozilla.github.io/pdf.js/), Bearbeitung mit [pdf-lib](https://pdf-lib.js.org/), ZIP mit [JSZip](https://stuk.github.io/jszip/)
- `desktop/main.js` – Desktop-Hülle (Electron): Fenster, Menüs, Dateizugriff, Dateizuordnung, Drucken
- `desktop/preload.js` – stellt der Oberfläche `window.pdfwerkHost` bereit
- `desktop/host-api.d.ts` – Schnittstelle zwischen Oberfläche und Hülle
- `build/` – Programmsymbol (`icon.svg`, daraus `icon.png`) für electron-builder
- `scripts/build.mjs` – erzeugt `PDFwerk.html` (alles in einer Datei), `dist/artifact` (gehostete Fassung) und `dist/desktop` (Seite für das Desktop-Programm)
- `scripts/make-icons.mjs` – rendert `build/icon.svg` zu `build/icon.png`
- `scripts/smoke.mjs` – Browsertest
- `scripts/smoke-desktop.mjs` – Test der Desktop-Fassung
- `../.github/workflows/pdfwerk-desktop.yml` – baut und prüft die Pakete für Windows, macOS und Linux, veröffentlicht Releases

PDFwerk ist eine eigenständige Entwicklung und steht in keiner Verbindung zu Adobe oder PDF24.
