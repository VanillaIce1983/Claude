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
| **Kantenlasche** | Eine oder mehrere Kanten, Ober- oder Unterkante wählt die Richtung. Winkel 0–180°. Länge bis äußere virtuelle Spitze, innere virtuelle Spitze oder tangentiale Biegung. Position Material innen, Material außen, Biegung außen, Biegung von virtueller Spitze, tangential zur Biegung. Breite: ganze Kante, Abstand von den Enden, mittig mit fester Breite. Automatische Eckenfreistellung „offen“, „45°“ oder „gerade“ mit Spalt (Vorgabe 45°, 0,25 mm): wo zwei gewählte Kanten zusammenstoßen oder die neue Lasche in eine Lasche eines früheren Features laufen würde, schneidet 45° beide Laschen an der winkelhalbierenden Ebene, gerade lässt eine durchlaufen und schneidet die andere gerade davor ab. Das gilt auch bei Winkeln ungleich 90°, schrägen Plattenecken und Laschen in einer Ebene, etwa nach innen zeigenden Rücklaschen; der Schnitt steht senkrecht zum Blech, die Biegezonen werden mitgeschnitten. Ohne Gehrung wird an stumpfen Plattenecken (Innenwinkel über 90°) die zuerst angelegte Lasche samt Biegung an der Ebene gekappt, an der die neue Biegung beginnt, damit sich Abwicklung und Biegezonen nicht überlappen. Eigener Radius, eigene Biegezugabe, eigener Freistich |
| **Saum** | Geschlossen, offen (Spaltabstand), Tropfenform (Winkel, Radius), gerollt (Winkel, Radius). Position Material innen oder Biegung außen. Läuft ein Saum in eine Nachbarlasche oder einen anderen Saum, wird er an diesem Ende gekürzt und hält 0,1 mm Abstand; ein schmaler Steg zur Ecke wird mit ausgeschnitten |
| **Skizzierte Biegung** | Biegelinie parallel zu einer Bezugskante. Biegemittellinie, Material innen, Material außen, Biegung außen. Wählbar, welche Seite fest bleibt |
| **Knick** | Zwei gegenläufige Biegungen mit Versatz. Bemaßung Versatz außen, Versatz innen oder Gesamtabmessung, Knickwinkel, projizierte Länge fixieren, Knickposition wie bei der skizzierten Biegung |
| **Ausschnitt** | Rund, Rechteck oder Langloch auf einer Fläche, Lage per Klick und Abstand zu einer Kante, Drehung, lineares Muster |
| **Lasche** | Freie Kanten in der Ebene der Fläche verlängern, ganze Kante oder Teilbreite |
| **Geschlossene Ecke** | Offene Ecke zwischen zwei Laschen schließen: beide Laschen werden passend verlängert oder gekürzt, Eckentyp Stoß (beide gleichmäßig bis zur Innenseite der anderen), Überlappen oder Unterlappen, Spaltabstand. Die Biegebereiche laufen mit in die Ecke und treffen sich auf Gehrung, nur am Radius bleibt eine kleine Öffnung; wahlweise bleibt der Biegebereich offen. Eine Seitenkante pro Ecke genügt |
| **Ecke brechen** | Verrundung oder Fase an Außen- und Innenecken der Kontur |
| **Abwicklung** | Exakte Abwicklung mit Biegelinien (oben/unten), Biegetabelle mit Winkel, Radius, Biegezugabe und Biegeabzug, Zuschnittmaß, kleinstes umschließendes Rechteck, Fläche, Schnittlänge, Masse |

### Messen

Werkzeug „Messen“ (Taste M): Punkte, Kanten, Flächen und Biegungen anklicken.

- Ein Element: Koordinaten, Kantenlänge, Durchmesser, Radius und Mittelpunkt von Bohrungen, Fläche und Umfang, Biegeradius innen oder außen mit Winkel und Bogenlänge.
- Zwei Elemente: Abstand, ΔX/ΔY/ΔZ, senkrechter Abstand, minimaler Abstand, Achsabstand und Winkel. Das Ergebnis erscheint als Maßlinie im 3D-Fenster.
- Funktioniert auch in der Abwicklung.

Dazu: Feature-Baum mit Bearbeiten, Unterdrücken und Löschen, Live-Vorschau beim Bearbeiten, Rückgängig/Wiederholen, Skizzen-Editor mit Raster, Winkelfang und Vorlagen, Projekt speichern und öffnen (JSON).

### Export

- **DXF** (R12, mm) der Abwicklung: Kontur als geschlossene Polylinien, runde Bohrungen als echte Kreise, Biegelinien auf eigenen Layern (`BIEGUNG_OBEN`, `BIEGUNG_UNTEN`) und Biegenotizen (`BIEGENOTIZEN`).
- **STEP** (AP214, mm) des gefalteten Teils als exaktes Volumenmodell: ebene Flächen, Biegungen als Zylinderflächen, runde Bohrungen als Zylinder, die schräg abgeschnittenen Biegezonen geschlossener Ecken als exakte B-Spline-Flächen. In SolidWorks öffnen und mit „Biegungen einfügen“ wieder als Blechteil weiterbearbeiten.
- **3MF** des gefalteten Teils für den 3D-Druck: geschlossenes Dreiecksnetz in mm, Z nach oben, auf der Druckplatte liegend.
- **SVG** der Abwicklung, **STL** des gefalteten Teils, **PNG** der Ansicht, **JSON** des Projekts.

Wo das Blech auf sich selbst aufliegt (zugedrückter Saum mit Innenradius 0, Ende eines Tropfensaums), lassen STEP und 3MF einen Abstand von 0,001 mm, damit der Körper sich nicht selbst berührt.

Parasolid (X_T) wird nicht angeboten: Das Format gehört Siemens, die Formatbeschreibung gibt es nur auf Anfrage beim Hersteller, und ohne den Parasolid-Kern lässt sich nicht prüfen, ob eine selbst geschriebene Datei korrekt gelesen wird. STEP überträgt dieselbe exakte Geometrie.

In der gehosteten Fassung werden DXF, STEP, STL und 3MF als ZIP geliefert, weil dort nur gängige Dateitypen direkt gespeichert werden dürfen.

## Rechenregeln

Mit Biegewinkel θ, Innenradius R, Blechdicke T und K-Faktor K:

- Biegezugabe: BZ = θ · (R + K · T)
- Äußerer Rücksprung: OSSB = (R + T) · tan(θ/2), innerer Rücksprung: ISSB = R · tan(θ/2)
- Biegeabzug: BA = 2 · OSSB − BZ

Die Abwicklungslänge einer Lasche ist damit A + B − BA (A, B bis zu den äußeren virtuellen Spitzen gemessen). Die Freistiche sind d = Verhältnis × T breit und reichen d über den Biegebereich hinaus.

## Noch nicht enthalten

Gehrungslasche, Lasche aus einer frei skizzierten Kontur, Eckfreistich als eigenes Feature, Ausschnitte über Biegungen hinweg (Abwickeln/Zurückbiegen), Umformwerkzeuge, Biegetabellen und Blechstärketabellen, Parasolid-Export (X_T).

## Entwicklung

```bash
npm install
npm test               # Kern-Tests (Abwicklungslängen, Maße, geschlossene Netze)
npm run typecheck
npm run build          # dist/index.html, eine einzelne Datei
npm run build:artifact # zusätzlich dist/artifact.html + dist/app.js zum Hosten als Seite
```

Aufbau:

- `src/kernel/` – Blech-Kern ohne Abhängigkeit zur Oberfläche. Jede Fläche liegt als Polygon in einem gemeinsamen Abwicklungs-Koordinatensystem, Flächen sind über Biegestreifen verbunden. Das 3D-Teil entsteht durch Falten um die Biegeachsen, deshalb ist die Abwicklung exakt.
- `src/kernel/features/` – Basislasche, Kantenlasche, Saum, Skizzierte Biegung, Knick, Ausschnitt, Lasche, Geschlossene Ecke, Ecke brechen.
- `src/kernel/mesh.ts` – geschlossenes Dreiecksnetz für gefalteten und abgewickelten Zustand.
- `src/kernel/flat.ts`, `src/kernel/export/` – Abwicklung, DXF, SVG, STEP, STL, 3MF, ZIP.
- `src/ui/` – Oberfläche mit three.js.
- `tests/` – Tests gegen Handrechnungen.
