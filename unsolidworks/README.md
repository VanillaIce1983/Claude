# UnsolidWorks

Blech-CAD im Browser. Bleche konstruieren, falten, abwickeln und als DXF für den Laser ausgeben – ohne Installation.

UnsolidWorks ist eine eigenständige Neuentwicklung. Der Code stammt nicht aus anderer Software und wurde nicht durch Dekompilieren gewonnen. Die Blechfunktionen folgen den in der Blechkonstruktion üblichen Begriffen und Rechenregeln (K-Faktor, Biegezugabe, Biegeabzug, virtuelle Spitze). Das Projekt steht in keiner Verbindung zu Dassault Systèmes; SOLIDWORKS ist eine Marke von Dassault Systèmes.

## Starten

- **Fertige Datei:** `UnsolidWorks.html` herunterladen und im Browser öffnen. Alles steckt in dieser einen Datei.
- **Entwicklung:** `npm install`, dann `npm run dev` und die angezeigte Adresse öffnen.

## Blechfunktionen

| Funktion | Optionen |
| --- | --- |
| **Blech** (Blechparameter) | Blechdicke, Biegeradius, Biegezugabe per K-Faktor, fester Biegezugabe oder Biegeabzug, automatischer Freistich (rechteckig, abgerundet, Riss) mit Verhältnis zur Dicke, Werkstoff und Dichte |
| **Basislasche** | Geschlossenes Profil → Platte. Offenes Profil (Linienzug) → gebogenes Profil mit Biegungen an jeder Ecke, Tiefe einseitig oder Mittelebene, Material auf beiden Linienseiten möglich. Skizzenebene oben, vorne oder rechts |
| **Kantenlasche** | Eine oder mehrere Kanten, Ober- oder Unterkante wählt die Richtung. Winkel 0–180°. Länge bis äußere virtuelle Spitze, innere virtuelle Spitze oder tangentiale Biegung. Position Material innen, Material außen, Biegung außen, Biegung von virtueller Spitze, tangential zur Biegung. Breite: ganze Kante, Abstand von den Enden, mittig mit fester Breite. Eigener Radius, eigene Biegezugabe, eigener Freistich |
| **Saum** | Geschlossen, offen (Spaltabstand), Tropfenform (Winkel, Radius), gerollt (Winkel, Radius). Position Material innen oder Biegung außen |
| **Skizzierte Biegung** | Biegelinie parallel zu einer Bezugskante. Biegemittellinie, Material innen, Material außen, Biegung außen. Wählbar, welche Seite fest bleibt |
| **Knick** | Zwei gegenläufige Biegungen mit Versatz. Bemaßung Versatz außen, Versatz innen oder Gesamtabmessung, Knickwinkel, projizierte Länge fixieren, Knickposition wie bei der skizzierten Biegung |
| **Ausschnitt** | Rund, Rechteck oder Langloch auf einer Fläche, Lage per Klick und Abstand zu einer Kante, Drehung, lineares Muster |
| **Lasche** | Freie Kanten in der Ebene der Fläche verlängern, ganze Kante oder Teilbreite |
| **Geschlossene Ecke** | Laschenseite bis zur benachbarten Lasche verlängern (Stoß) mit Spaltabstand. Funktioniert mit der Seitenkante jeder der beiden Laschen an der Ecke |
| **Ecke brechen** | Verrundung oder Fase an Außen- und Innenecken der Kontur |
| **Abwicklung** | Exakte Abwicklung mit Biegelinien (oben/unten), Biegetabelle mit Winkel, Radius, Biegezugabe und Biegeabzug, Zuschnittmaß, kleinstes umschließendes Rechteck, Fläche, Schnittlänge, Masse |

Dazu: Feature-Baum mit Bearbeiten, Unterdrücken und Löschen, Live-Vorschau beim Bearbeiten, Rückgängig/Wiederholen, Skizzen-Editor mit Raster, Winkelfang und Vorlagen, Projekt speichern und öffnen (JSON).

### Export

- **DXF** (R12, mm) der Abwicklung: Kontur als geschlossene Polylinien, runde Bohrungen als echte Kreise, Biegelinien auf eigenen Layern (`BIEGUNG_OBEN`, `BIEGUNG_UNTEN`) und Biegenotizen (`BIEGENOTIZEN`).
- **SVG** der Abwicklung, **STL** des gefalteten Teils, **PNG** der Ansicht, **JSON** des Projekts.

In der gehosteten Fassung werden DXF und STL als ZIP geliefert, weil dort nur gängige Dateitypen direkt gespeichert werden dürfen.

## Rechenregeln

Mit Biegewinkel θ, Innenradius R, Blechdicke T und K-Faktor K:

- Biegezugabe: BZ = θ · (R + K · T)
- Äußerer Rücksprung: OSSB = (R + T) · tan(θ/2), innerer Rücksprung: ISSB = R · tan(θ/2)
- Biegeabzug: BA = 2 · OSSB − BZ

Die Abwicklungslänge einer Lasche ist damit A + B − BA (A, B bis zu den äußeren virtuellen Spitzen gemessen). Die Freistiche sind d = Verhältnis × T breit und reichen d über den Biegebereich hinaus.

## Noch nicht enthalten

Gehrungslasche, Lasche aus einer frei skizzierten Kontur, geschlossene Ecke als Überlappung oder Unterlappung, Eckfreistich als eigenes Feature, Ausschnitte über Biegungen hinweg (Abwickeln/Zurückbiegen), Umformwerkzeuge, Biegetabellen und Blechstärketabellen, STEP-Export.

## Entwicklung

```bash
npm install
npm test               # Kern-Tests (Abwicklungslängen, Maße, geschlossene Netze)
npm run typecheck
npm run build          # dist/index.html, eine einzelne Datei
npm run build:artifact # zusätzlich dist/artifact.html für das Einbetten in eine Seite
```

Aufbau:

- `src/kernel/` – Blech-Kern ohne Abhängigkeit zur Oberfläche. Jede Fläche liegt als Polygon in einem gemeinsamen Abwicklungs-Koordinatensystem, Flächen sind über Biegestreifen verbunden. Das 3D-Teil entsteht durch Falten um die Biegeachsen, deshalb ist die Abwicklung exakt.
- `src/kernel/features/` – Basislasche, Kantenlasche, Saum, Skizzierte Biegung, Knick, Ausschnitt, Lasche, Geschlossene Ecke, Ecke brechen.
- `src/kernel/mesh.ts` – geschlossenes Dreiecksnetz für gefalteten und abgewickelten Zustand.
- `src/kernel/flat.ts`, `src/kernel/export/` – Abwicklung, DXF, SVG, STL, ZIP.
- `src/ui/` – Oberfläche mit three.js.
- `tests/` – Tests gegen Handrechnungen.
