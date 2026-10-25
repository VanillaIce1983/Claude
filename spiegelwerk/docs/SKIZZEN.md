# Skizzen spiegeln

Der schwierigste Teil des unabhängigen Spiegelns ist die Skizze: Sie muss so neu entstehen, dass Geometrie, Beziehungen und Bemaßungen im gespiegelten Zustand stimmen und die Skizze danach ganz normal bearbeitbar ist. Code: `Spiegelwerk.Core/Sketch/` (Rechnung, getestet) und `Spiegelwerk/Capture/SketchCapture.cs`, `Spiegelwerk/Build/SketchBuilder.cs` (SOLIDWORKS).

## 1. Warum „x negieren“ nicht reicht

Jede Skizze hat ein eigenes Koordinatensystem, das SOLIDWORKS aus der Skizzenebene ableitet. Bei den Standardebenen:

| Ebene | Skizzen-x | Skizzen-y | Normale |
| --- | --- | --- | --- |
| vorne | +X | +Y | +Z |
| oben | +X | −Z | +Y |
| rechts | **−Z** | +Y | +X |

Spiegelt man eine Skizze auf *vorne* an *rechts*, wird in Skizzenkoordinaten x negiert. Spiegelt man eine Skizze auf *rechts* an *rechts*, bleiben die Skizzenkoordinaten gleich – dafür zeigt die Normale in die andere Richtung, und die Extrusion muss umgedreht werden. Bei einer beliebigen Spiegelebene liegt die gespiegelte Skizze auf einer Hilfsebene, deren Skizzenachsen SOLIDWORKS festlegt. Eine Regel pro Fall wäre fehleranfällig; Spiegelwerk rechnet deshalb immer über das Modell.

## 2. Der Koordinatenweg

```
Skizzenpunkt (x, y)  ──Frame der Quelle──▶  Modellpunkt p
p  ──Spiegelung R──▶  R(p)
R(p)  ──Frame des Ziels⁻¹──▶  Skizzenpunkt (x′, y′)
```

```csharp
// SketchMirror.Mirror
foreach (SkPoint p in source.Points)
{
    Vec3 local = target.ToLocal(r.Apply(source.ToModel(p)));
    dst.Points.Add(new SkPoint(p.Id, local.X, local.Y));
}
```

Der Frame der Quelle kommt aus `ISketch.ModelToSketchTransform` (invertiert). Der Frame des Ziels ist erst bekannt, wenn die Skizze auf der Zielebene geöffnet ist – deshalb öffnet `SketchBuilder` zuerst die Skizze, liest deren Transformation und rechnet erst dann:

```csharp
// SketchBuilder.Build
Feature plane = _ctx.Planes.Resolve(_ctx.Mirror.Apply(src.Frame.Plane));
SwSelect.Select(_doc, plane, 0, false);
_sm.InsertSketch(true);
Sketch sketch = _sm.ActiveSketch;
Frame3 target = _ctx.Math.SketchFrame(sketch);          // jetzt bekannt
SketchMirrorResult res = SketchMirror.Mirror(src, _ctx.Mirror, target);
```

`SketchMirror` prüft, dass die Zielskizze wirklich in der gespiegelten Ebene liegt, und ein Test (`Random_planes_and_target_frames_preserve_shape_and_arc_bulge`) bestätigt für 200 zufällige Kombinationen aus Skizzenebene, Spiegelebene und Ziel-Frame, dass alle Punktabstände erhalten bleiben.

Das Vorzeichen **σ = sign(z′ · R(z))** (Zielnormale gegen gespiegelte Quellnormale) wird dabei mitbestimmt und steuert alles, was von einer Richtung abhängt (siehe [Architektur](ARCHITEKTUR.md#spiegelung-und-σ)).

## 3. Geometrie

| Element | Erfassen | Neu erzeugen |
| --- | --- | --- |
| Linie, Mittellinie | Start-, Endpunkt | `CreateLine`; Mittellinie = Konstruktionsgeometrie |
| Bogen | Mittelpunkt, Start, Ende, Drehsinn | `CreateArc(…, Richtung)` mit **Richtung′ = −σ · Richtung** |
| Kreis | Mittelpunkt, Radius | `CreateCircleByRadius` |
| Ellipse | Mittelpunkt, Haupt- und Nebenachsenpunkt | `CreateEllipse` |
| Ellipsenbogen | wie Ellipse + Start, Ende, Drehsinn | `CreateEllipticalArc`, Drehsinn wie Bogen |
| Spline | Stützpunkte | `CreateSpline2(Punkte, natürliche Enden)` |
| Parabel | Brennpunkt, Scheitel, Start, Ende | `CreateParabola` |
| Punkt | nur eigenständige Punkte (`GetUserPoints2`) | `CreatePoint` |

**Warum der Bogensinn kippt:** Ein Bogen läuft mit wachsendem Parameter um die Skizzennormale z. Gespiegelt läuft er um R(x) × R(y) = −R(z). Liegt die Zielnormale bei R(z) (σ = +1), läuft er also im Uhrzeigersinn, wenn er vorher gegen ihn lief; bei σ = −1 bleibt der Sinn. Prüft der Test `Front_sketch_mirrored_at_right_plane_negates_x_and_flips_arcs`: Der gespiegelte Halbkreis wölbt sich weiterhin nach oben.

**Gemeinsame Endpunkte:** Im Original teilen sich zwei verbundene Segmente einen `SketchPoint`. Neu erzeugt wird mit `SketchManager.AddToDB = true` (nötig, damit SOLIDWORKS nicht an Bildschirm-Fangpunkte springt und eigene Beziehungen hinzufügt). Endpunkte mit bitgleichen Koordinaten legt SOLIDWORKS dabei unter Umständen schon selbst zusammen – das erkennt Spiegelwerk an der gleichen Punkt-ID und tut dann nichts. Sind es zwei Punkte, werden sie sofort zusammengeführt (`MERGEPOINTS`), notfalls deckungsgleich gemacht. Weil das Zusammenführen einen der beiden Punkte entfernt, wird der überlebende Punkt gleich danach neu eingelesen – an einer Ecke mit drei Segmenten (Rechteck mit Diagonale) muss das dritte an einen lebenden Punkt angeschlossen werden. Nur wenn beides scheitert, steht „Kontur eventuell offen“ im Bericht.

**Zuordnung der neuen Punkte:** Welcher neue `SketchPoint` zu welcher Original-ID gehört, wird über die Lage bestimmt (Abstand < 1 µm), nicht über Start/Ende – SOLIDWORKS darf einen Bogen intern umdrehen.

## 4. Beziehungen

Eine Spiegelung ist längen- und winkeltreu. Fast alle Beziehungen gelten deshalb unverändert für die gespiegelten Elemente:

| Unverändert | Achsenabhängig |
| --- | --- |
| deckungsgleich, zusammengeführt, tangential, parallel, senkrecht, gleich, konzentrisch, koradial, Mittelpunkt, kollinear, symmetrisch, fixiert, Schnittpunkt, gleiche Krümmung, Durchstoßpunkt | horizontal, vertikal (Linie und zwei Punkte) |

Horizontal und vertikal beziehen sich auf die Skizzenachsen. `AxisMap` bestimmt, wohin die gespiegelte Quell-x-Achse im Ziel zeigt:

- auf ±x: horizontal bleibt horizontal,
- auf ±y: horizontal wird vertikal (z. B. Spiegelebene unter 45°),
- schräg: die Beziehung entfällt mit Hinweis im Bericht (Test `Oblique_axes_drop_horizontal_and_vertical_with_a_warning`).

Für die Standardebenen und alle dazu parallelen Spiegelebenen landen die Achsen immer auf Achsen; schräg wird es nur bei schräger Spiegelebene.

Erzeugt werden Beziehungen mit `ISketchRelationManager.AddRelation`, sobald alle Beteiligten Skizzenpunkte oder -segmente sind – auch der Ursprungspunkt und Elemente anderer Skizzen. Für Kanten, Flächen und Ebenen wird wie in der Oberfläche ausgewählt und `SketchAddConstraints` aufgerufen. Punktbeziehungen haben dort eigene Kennungen, und Hilfe und Makroaufzeichnung schreiben sie unterschiedlich; Spiegelwerk probiert die Kandidaten der Reihe nach (horizontal zwischen Punkten: `sgHORIZONTALPOINTS2D`, dann `sgHORIZONTAL2D`; vertikal: `sgVERTICALPOINTS2D`, `sgVERTPOINTS2D`, `sgVERTICAL2D`; zusammengeführt: `sgMERGEPOINTS`, `sgCOINCIDENT`). Ob es geklappt hat, zeigt die Zahl der Beziehungen vorher und nachher.

### Externe Bezüge

| Bezug im Original | Im Ziel |
| --- | --- |
| Ursprung | Ursprung des Ziels – nur wenn die Spiegelebene durch den Ursprung geht (dann ist R(0) = 0) |
| Element einer anderen Skizze | das entsprechende Element der bereits gespiegelten Skizze |
| Modellkante, Eckpunkt | über den gespiegelten Fingerabdruck im bisher aufgebauten Ziel |
| Ebene | eine vorhandene Ebene an gespiegelter Lage |

Gibt es den Bezug im Ziel nicht – der Ursprung, wenn die Spiegelebene nicht durch ihn geht, oder eine Kante eines Features, das nicht mitgespiegelt wurde –, gilt die Option *Fehlende Skizzenbezüge ersetzen*: An die gespiegelte Stelle kommt **fixierte Hilfsgeometrie** (fixierter Punkt, fixierte Konstruktionslinie oder -kreis, in die Skizze projiziert – in der Positionsskizze einer Bohrung und in der Referenzskizze eines Skizzenmusters statt eines Punkts der Endpunkt einer fixierten Konstruktionslinie, sonst entstünde dort eine Bohrung bzw. eine Musterinstanz (`BuildContext.PointSketches`); für eine Ebene, die die Skizze schneidet, eine Linie entlang der Schnittgeraden), und die ursprüngliche Beziehung oder Bemaßung hängt sich daran. Die Skizze behält so genau die Freiheitsgrade des Originals – anders als ein „Fixiert“ auf den eigenen Elementen, das die Skizze überbestimmen kann. Ohne die Option entfällt die Beziehung. Beides steht im Bericht.

**Übernommene Elemente** („Elemente übernehmen“) hängen im Original über eine „Auf Kante“-Beziehung an einer Modellkante. Im Ziel wird die gespiegelte Kante gesucht und das Element wieder daran gebunden – kollinear bei Linien, koradial bei Bögen und Kreisen; fehlt die Kante, an fixierter Hilfsgeometrie. Splines und Ähnliches werden an Ort und Stelle fixiert. Die Endpunkte übernommener Linien bleiben dabei frei.

**Versetzte Elemente** („Elemente versetzen“, zu einer Modellkante oder innerhalb der Skizze) dürfen nicht kollinear angebunden werden – das zöge sie auf ihre Quelle. Sie bleiben parallel (Linien) bzw. konzentrisch (Bögen, Kreise) zur Quelle, und die mitübernommene Versatzbemaßung legt den Abstand fest. Gelingt die Anbindung nicht, bleiben sie an ihrer Stelle; fixiert wird nichts.

Nach dem Aufbau vergleicht Spiegelwerk den Bestimmtheitsgrad der Skizze (`GetConstrainedStatus`) mit dem Original und meldet, wenn die Kopie überbestimmt oder – bei voll bestimmtem Original – unterbestimmt ist.

## 5. Bemaßungen

| Typ | Umrechnung |
| --- | --- |
| Abstand (ausgerichtet), Radius, Durchmesser, Winkel | Wert unverändert |
| horizontal / vertikal | wie die Beziehungen über `AxisMap`: bleibt, tauscht oder entfällt |
| Textposition | gespiegelt (`R(Position)`) |

Die Textposition ist kein Schmuck: Bei Winkelbemaßungen entscheidet sie, ob 60° oder 120° gemessen wird, bei Radien, an welcher Stelle. Die gespiegelte Position wählt den gespiegelten Winkel.

**Jede Bemaßung wird nachgemessen, bevor ihr Wert gesetzt wird.** Würde man eine Winkelbemaßung, die den Nebenwinkel erwischt hat, auf den Originalwert setzen, verzöge sich die Skizze. Deshalb:

```csharp
// SketchBuilder.PlaceDimension (vereinfacht)
object created = _doc.AddDimension2(pos.X, pos.Y, pos.Z);   // bzw. Horizontal-/Vertikal-/Radius-/Durchmesser-Variante
double measured = dd.GetDimension2(0).SystemValue;
if (Matches(dim, measured)) return dd;                      // misst dasselbe wie im Original
DeleteDimension(dd);                                        // sonst: weg, nächster Versuch
```

Die API-Hilfe sagt nicht eindeutig, ob die Position in Modell- oder Skizzenkoordinaten gelesen wird; Spiegelwerk versucht beides und behält die Bemaßung, die stimmt. Erst dann wird der exakte Wert gesetzt (`SetSystemValue3`, alle Konfigurationen; ein Fehlercode wird gemeldet), getriebene Bemaßungen werden wieder getrieben, und der Name (`D1`) wird übernommen, damit Gleichungen wie `"D1@Skizze1" = …` weiter passen. Hat SOLIDWORKS eine im Original treibende Bemaßung als getrieben angelegt, weil sie die Kopie überbestimmen würde, steht das im Bericht – Gleichungen darauf wirken dann nicht.

Damit `AddDimension2` nicht blockiert und nichts überbestimmt wird, schaltet `SwPreferences` für die Dauer des Laufs ab: *Bemaßungswert eingeben*, automatische Beziehungen, Skizzenfang, Rückfrage bei überbestimmenden Bemaßungen.

## 6. Durchgerechnetes Beispiel

Profil auf der Ebene *vorne*: Grundlinie (10|0)–(50|0) mm, rechte Seite bis (50|20), Halbkreis um (30|20) nach (10|20) gegen den Uhrzeigersinn, linke Seite zurück. Beziehungen: Grundlinie horizontal, Seiten vertikal, rechte Seite tangential zum Bogen, (10|0) deckungsgleich mit dem Ursprung … Bemaßungen: Breite 40 horizontal, Höhe 20 vertikal, Radius 20.

Gespiegelt an *rechts* (YZ, durch den Ursprung):

- Zielebene: wieder *vorne*, σ = +1, x-Achse → −x, y-Achse → +y.
- Punkte: (−10|0), (−50|0), (−50|20), Mittelpunkt (−30|20), (−10|20).
- Bogen: jetzt im Uhrzeigersinn – von (−50|20) über (−30|40) nach (−10|20), also weiterhin nach oben gewölbt.
- Beziehungen: alle unverändert; „deckungsgleich mit dem Ursprung“ bezieht sich auf den Ursprung des Ziels, weil R(0) = 0.
- Bemaßungen: 40 bleibt horizontal, 20 vertikal, R20; die Textposition (30|−10) wird zu (−30|−10).

Dasselbe Profil auf *rechts*, gespiegelt an *rechts*: Zielebene *rechts*, σ = −1, alle Skizzenkoordinaten bleiben, der Bogen behält seinen Sinn, die Extrusion bekommt „Richtung umkehren“ umgekehrt.

## 7. Grenzen

- **Skizzentext** wird nicht übernommen (gespiegelt wäre er unlesbar; ungespiegelt ist es eine Konstruktionsentscheidung).
- **Splines** werden über ihre Stützpunkte neu erzeugt; Tangenten- und Krümmungsgriffe gehen verloren (Hinweis im Bericht).
- **Nuten, Polygone, Rechtecke** kommen als Einzelelemente mit ihren Beziehungen; das zusammenfassende Nut-Element selbst nicht.
- **Ordinatenbemaßungen**, Bogenlängen und Bogenbedingungen (min./max.) werden nicht übernommen – Letztere würden ohnehin von der Nachmessung verworfen.
- **Blöcke, Bilder, Schraffuren** und **3D-Skizzen** werden nicht unterstützt.
