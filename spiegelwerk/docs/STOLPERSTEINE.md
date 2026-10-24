# Stolpersteine

Was bei der programmgesteuerten Neuerzeugung von Features in SOLIDWORKS schiefgeht – und wie Spiegelwerk damit umgeht. Ein Teil davon stammt aus einem unabhängigen Code-Review, bei dem jeder Befund von drei Prüfern gegengeprüft wurde.

## Benutzereinstellungen und Modellzustand

1. **„Bemaßungswert eingeben“ blockiert die API.** Ist die Option an, öffnet jede `AddDimension2` den Ändern-Dialog und der Aufruf hängt, bis jemand klickt. → `SwPreferences` schaltet `swInputDimValOnCreate` für die Dauer des Laufs aus und stellt sie in `Dispose` wieder her – auch nach einem Fehler.
2. **Automatische Beziehungen und Fangen.** Beim Zeichnen per API fügt SOLIDWORKS sonst eigene Horizontal-/Deckungsgleich-Beziehungen hinzu und fängt Punkte abhängig vom Zoom. → `SketchManager.AddToDB = true`, dazu `swSketchAutomaticRelations` und `swSketchInference` aus.
3. **Überbestimmende Bemaßungen** lösen eine Rückfrage aus. → `swSketchOverdefiningDimsPromptToSetState` aus, `…SetDrivenByDefault` an; eine so getrieben angelegte Bemaßung wird im Bericht gemeldet.
4. **Rückrollleiste.** Steht sie nicht am Ende, sind spätere Features zurückgerollt, und das Erfassen verstellt sie zwangsläufig. → Spiegelwerk verlangt, dass sie am Ende steht.
5. **Unterdrückte und fehlerhafte Features.** Der Feature-Baum liefert auch unterdrückte Features; neu aufgebaut wären sie plötzlich aktiv. Features, die schon im Original einen Fehler haben, ergeben keine sinnvolle Kopie. → Beide werden übersprungen und im Bericht genannt.

## Skizzen

6. **AddToDB und gemeinsame Endpunkte.** Zwei verbundene Segmente teilen im Original einen Punkt. Beim Neuanlegen kann SOLIDWORKS bitgleiche Endpunkte selbst zusammenlegen – oder zwei Punkte an derselben Stelle anlegen, dann gilt die Kontur als offen. → Punkt-IDs vergleichen; nur getrennte Punkte zusammenführen (`MERGEPOINTS`, sonst deckungsgleich) und danach die überlebenden Punkte neu einlesen, weil das Zusammenführen einen von beiden entfernt.
7. **Das Skizzen-Koordinatensystem ist erst nach dem Öffnen bekannt.** Die Ebene *rechts* hat Skizzen-x = −Z, Hilfsebenen haben Achsen, die SOLIDWORKS festlegt. → Skizze öffnen, `ModelToSketchTransform` lesen, dann rechnen; nie Koordinaten „von Hand“ negieren.
8. **Koordinaten beim Erzeugen** sind in einer 2D-Skizze Skizzenkoordinaten, in einer 3D-Skizze Modellkoordinaten.
9. **`GetSketchPoints2` liefert auch Hilfspunkte** von Segmenten. Als eigenständige Punkte neu erzeugt, entstünden überzählige Punkte. → Nur Punkte aus `GetUserPoints2` werden einzeln angelegt.
10. **Bemaßungen messen nicht zwingend, was man meint.** Winkel (Nebenwinkel), Radius/Durchmesser, Bogenbedingungen hängen von Textposition und Auswahl ab; die Hilfe lässt offen, ob die Position von `AddDimension2` in Modell- oder Skizzenkoordinaten gilt. → Gespiegelte Textposition, beide Koordinatensysteme versuchen, gemessenen Wert mit dem Original vergleichen, falsche Bemaßung löschen statt Wert erzwingen.
11. **Fehlende Bezüge nicht pauschal fixieren.** Wer statt einer verlorenen Beziehung zum Ursprung oder zu einer Kante die eigenen Skizzenelemente fixiert, überbestimmt die Skizze leicht (eine fixierte Diagonale fixiert die Ecken, die Horizontal-Beziehungen werden redundant). → Fixierte Hilfsgeometrie an der gespiegelten Stelle anlegen und die ursprüngliche Beziehung oder Bemaßung daran hängen; danach den Bestimmtheitsgrad mit dem Original vergleichen (`GetConstrainedStatus`).
12. **`SketchAddConstraints` hat eigene Kennungen für Punktbeziehungen** (`sgHORIZONTALPOINTS2D` statt `sgHORIZONTAL2D`, `sgMERGEPOINTS`), und Hilfe und Makroaufzeichnung schreiben manche verschieden. → Typisierte `AddRelation` zuerst, auch für den Ursprung und andere Skizzen; sonst Kandidaten der Reihe nach, Erfolg an der Zahl der Beziehungen ablesen.
13. **„Auf Kante“ ist keine Beziehung, die man anlegen kann.** Übernommene und versetzte Elemente hängen über `USEEDGE`/`OFFSETEDGE` an Modellkanten. → Wieder anbinden: kollinear oder koradial zur gespiegelten Kante.
14. **Modellbezüge einer Skizze im Endzustand gelesen sind falsch,** wenn spätere Features die Kante gekürzt oder geteilt haben. → Skizzen mit Modellbezügen werden mit der Rückrollleiste direkt hinter der Skizze gelesen.
15. **Halbe Skizzen.** Bricht etwas mitten in einer Skizze ab, bleibt sie im Bearbeitungsmodus offen und alle folgenden Aufrufe landen darin. → `SketchBuilder` schließt und löscht die angefangene Skizze; `MirrorJob` schließt eine noch aktive Skizze nach jeder Ausnahme.

## Spiegelung und Richtungen

16. **Eine Spiegelung kehrt die Orientierung um.** Kreuzprodukte, Bogensinn, Drehrichtungen, „links/rechts von der Kontur“ ändern sich. Was sich sicher herleiten lässt, regelt σ (`DirectionRules`): Bogensinn, „Richtung umkehren“ bei Extrusionen.
17. **Nicht dokumentierte Voreinstellungen.** Auf welche Seite eine Rotation standardmäßig läuft, auf welche Seite eine dünne Wand bei offenem Profil, welche Fläche bei einer Winkel-Abstand-Fase den Abstand bekommt, wohin ein Startversatz zeigt – das hängt an internen Orientierungen. → Erste Annahme nach σ, dann **messen**: Volumen und erstes Moment der Änderung gegen das gespiegelte Original; bei Abweichung probiert `StepVerifier` die Varianten des Builders (umkehren, Seite wechseln, Wandstärken tauschen) und behält die passende. Eine Variante, die das Feature in einen Fehlerzustand bringt, gewinnt nie.
18. **Aufsatz und Schnitt** extrudieren laut Hilfe standardmäßig entgegengesetzt zueinander (Aufsatz entlang, Schnitt entgegen der Normale). Für die Spiegelung ist das egal, weil es für Original und Kopie gleich gilt – man darf es nur nicht „korrigieren“.
19. **Richtung der Extrusion.** Ein Feature kann statt entlang der Skizzennormale entlang einer Linie, Kante oder Ebenennormale extrudiert sein (`GetDirectionReference`). → Wird mitgespiegelt und nachträglich gesetzt; fehlt der Bezug, steht es im Bericht.
20. **Winkeltoleranzen als Sinus.** `1 − |cos α| < 1e-7` klingt streng, lässt aber 0,026° durch – 35 µm auf 100 mm. Dann gilt eine leicht schräge Ebene als Standardebene und eine Horizontal-Beziehung verzieht die Skizze. → Toleranzen auf die Querkomponente (`|a × b|`) beziehen, und in `SketchMirror` jeden Punkt einzeln auf der Zielebene prüfen.

## Feature-Daten und Auswahl

21. **Viele Methoden lesen ihre Eingaben aus der Auswahl, unterschieden durch Markierungen (Marks)** – und die sind uneinheitlich dokumentiert: Verrundungskanten 1, Referenzebene 0/1/2 für erste/zweite/dritte Referenz, Extrusion: Endbezug 1, Richtung 16, Startbezug 32; Rotationsachse laut Referenz 4, in neueren offiziellen Beispielen 16. → Wo möglich das Feature mit einfachen Parametern anlegen und Bezüge danach über `GetDefinition` → `AccessSelections` → ändern → `ModifyDefinition` setzen (so bei „bis Fläche“, Startbedingung, Richtung, Konturen, Feature-Bereich). Wo es nicht anders geht, nacheinander die dokumentierten Markierungen versuchen (Rotationsachse 16, dann 4; Fasenkanten 1, dann 0).
22. **Der Zwischenzustand muss sich erzeugen lassen.** Ein Schnitt „bis Fläche“, zunächst blind von einer Skizzenebene oberhalb des Teils angelegt, schneidet nichts – SOLIDWORKS erzeugt ihn gar nicht. → Schnitte entstehen zuerst „durch alles“, notfalls in beide Richtungen; die endgültige Definition kommt in einem einzigen `ModifyDefinition`. Gelingt das nicht, wird das Feature wieder entfernt.
23. **`ReleaseSelectionAccess` nicht vergessen.** `AccessSelections` rollt das Modell auf den Stand des Features zurück. Schlägt `ModifyDefinition` fehl oder wirft ein Setter eine Ausnahme, bleibt es zurückgerollt. → `FeatureEdit` gibt in beiden Fällen frei.
24. **Feature-Daten liefern Objekte im Zustand des Features** – aber nur, solange der Zugriff aktiv ist. → Geometrie (Kantenendpunkte, Flächeninhalt) innerhalb von `AccessSelections` in Fingerabdrücke umrechnen, nicht die Objekte aufheben.
25. **Verrundungselemente.** `FilletItemsCount`/`GetFilletItemAtIndex` beschreiben nur Verrundungen mit mehreren Radien (sonst −1). → Gewöhnliche Verrundungen über `Edges` und `GetFaces(swSimpleFilletSingleRadius)` lesen.
26. **Flächen und Kanten über einen Neuaufbau tragen.** Ein vor dem Erzeugen gefundener Bezug wird erst danach verwendet. → `IEntity.GetSafeEntity()`.
27. **Neuaufbaufehler.** Ein Feature kann entstehen und trotzdem fehlerhaft sein. → `GetErrorCode2` nach jedem Feature; Fehler gilt als „fehlgeschlagen“, Warnung als Hinweis.

## Topologie

28. **Kanten und Flächen haben keine stabile Identität über Dokumente hinweg.** Persistente IDs (`GetPersistReference3`) gelten nur im selben Dokument. → Geometrische Fingerabdrücke, gespiegelt, im Ziel gesucht – im Ziel herrscht in diesem Moment genau der gespiegelte Zustand.
29. **Gleiche Fingerabdrücke.** Zwei Halbkreise einer Bohrung haben dieselben Endpunkte → Mittelpunkt vergleichen. Im selben Teil existiert eine Kante auf der Spiegelebene zweimal → Körper bevorzugen, die der Lauf selbst erzeugt hat (auch bei Eckpunkten und Achsen); mehrdeutige Treffer stehen im Bericht.
30. **Geschlossene Kanten haben eine Naht,** die der Kern frei legt: Der neu gezeichnete Kreis hat seinen Anfangspunkt woanders als der gespiegelte Kreis des Originals. → Vollkreise über Mittelpunkt, Radius und Achse vergleichen, geschlossene Ellipsen und Splines über den Abstand zur Kurve (`Edge.GetClosestPointOn`).
31. **Später geteilte Kanten.** Wird eine Kante erst durch einen späteren Schnitt geteilt, sieht die Verrundung davor sie noch ganz. → Erfassen über `AccessSelections` (zurückgerollter Zustand), nicht über den Endzustand.
32. **Der Cache ist nach jedem Versuch veraltet,** auch nach einem gescheiterten – vielleicht ist ein Feature entstanden oder wieder entfernt worden. → Nach jedem Feature verwerfen.

## Prüfung

33. **Volumen allein reicht nicht.** Eine Verrundung, die an einer Innenkante Material ansetzt und an einer Außenkante gleich viel wegnimmt, ändert das Volumen kaum; ein Schwerpunkt der Änderung ist dann sinnlos. → Volumen und erstes Moment getrennt vergleichen.
34. **Die Änderung ist eine Differenz zweier Gesamtwerte** mit endlicher Genauigkeit. → Untergrenze der Toleranz aus dem ganzen Teil ableiten, sonst schlagen kleine Features beim Rauschen an.

## Namen und Sprache

35. **Namen hängen von Sprache und Vorlage ab:** „Ebene vorne“/„Front Plane“, „Ursprung“/„Origin“. → Standardebenen über ihre Geometrie suchen, den Ursprung über den Typ `OriginProfileFeature`.
36. **Namenskollisionen.** Im selben Teil gibt es die Namen schon; im neuen Teil können Hilfsebenen einen Originalnamen belegen. → Namenszusatz im selben Teil, Hilfsfeatures mit eigenem Präfix, bei Kollision „ (2)“. Der Zusatz wird von Zeichen befreit, die in Dateinamen nicht erlaubt sind.
37. **Gleichungen verweisen über Namen** (`"D1@Skizze1"`) und Zahlen ohne Einheit gelten in den Dokumenteinheiten. → Im neuen Teil Feature- und Bemaßungsnamen übernehmen, das Einheitensystem samt Einzeleinheiten kopieren, dann Gleichungen wörtlich übernehmen; was nicht passt, steht im Bericht.

## Baugruppen

38. **Geometrie einer Komponente kommt in Teilkoordinaten.** Eine in der Baugruppe gewählte Fläche oder Ebene einer Komponente liefert Normale und Punkt im Teil-Koordinatensystem. → Mit `Component2.Transform2` in Baugruppenkoordinaten umrechnen; Baugruppenebenen nicht.
39. **Eine Komponententransformation darf nicht spiegeln.** `Transform2` muss eine Drehung sein (Determinante +1). → Das Teil selbst gespiegelt aufbauen und mit T′ = R_asm ∘ T ∘ R_part platzieren.
40. **Die Komponente verwendet eine bestimmte Konfiguration** (`ReferencedConfiguration`), das geöffnete Teil zeigt seine aktive. → Vor dem Erfassen die referenzierte Konfiguration aktivieren, danach die vorige wiederherstellen.
41. **Leichtgewichtige, unterdrückte, virtuelle Komponenten, Unterbaugruppen.** → Leichtgewichtige werden aufgelöst; die anderen lehnt schon die Auswahl ab.
42. **Rückrollen und `AccessSelections` brauchen das Teil als eigenes Dokument.** → Teil mit `OpenDoc6` öffnen, danach – auch nach einem Fehler – Konfiguration zurücksetzen, Fenster schließen, Baugruppe wieder aktivieren. `AddComponent5` braucht die neue Datei gespeichert und geladen.
43. **Abbruch heißt Abbruch.** Ein abgebrochener Lauf wird nicht gespeichert und nicht eingefügt; eine fehlschlagende Komponente stoppt die anderen nur auf Wunsch, und der Bericht bleibt vollständig.
44. **Verknüpfungen werden nicht übernommen.** Die eingefügte Komponente liegt richtig, ist aber frei beweglich.

## Selbes Teil

45. **Gespiegelte Features würden das Original verändern:** Ein verschmelzender Aufsatz verschmilzt mit dem Original, sobald sie sich berühren, ein Schnitt schneidet beide – und am Volumen ist das nicht zu sehen. → Aufsätze entstehen ohne Verschmelzen; Verschmelzen und Feature-Bereich werden in einem Schritt gesetzt, mit den Körpern aus dem zurückgerollten Zustand (vor dem Feature gibt es den gespiegelten Körper noch für sich). Ein Schnitt ohne gespiegelten Körper wird nicht angelegt.
46. **Ebenen des Originals.** Liegt eine gespiegelte Skizze zufällig auf einer Ebene des Originals, hinge sie wieder am Original. → Nur Standardebenen und eigene Ebenen verwenden.
47. **Original löschen** kann abhängige Features beschädigen oder Features verlieren, die gar nicht gespiegelt wurden (Achsen, 3D-Skizzen, Koordinatensysteme). → Nur wenn jedes Feature ein fehlerfreies Gegenstück hat und nichts außerhalb der Auswahl vom Original abhängt.

## Add-In und COM

48. **x64 und RegAsm.** SOLIDWORKS ist ein 64-Bit-Prozess: mit `Framework64\…\RegAsm.exe /codebase` registrieren, `PlatformTarget` x64.
49. **Interop-Assemblys** mit `EmbedInteropTypes = false` referenzieren und nicht mit ausliefern; zur Laufzeit gelten die der installierten Version.
50. **COM-Objekte freigeben.** Beim Entladen `Marshal.ReleaseComObject` für `SldWorks`/`CommandManager` und zweimal `GC.Collect`/`WaitForPendingFinalizers`, sonst bleibt SOLIDWORKS beim Beenden hängen.
51. **Rückruf über Namen.** `AddCommandItem2` erhält Methodennamen als Text; die Methoden müssen öffentlich auf dem an `SetAddinCallbackInfo2` übergebenen Objekt liegen (`nameof` schützt vor Tippfehlern).
52. **PropertyManager-Seite:** Auswahlen in `OnClose` lesen (danach sind sie weg), modellieren erst in `AfterClose`. OK-Schaltfläche erst freigeben, wenn eine Spiegelebene gewählt ist (`EnableButton`).
53. **Namenskonflikte in C#:** `ISketchSegment.GetType()` verdeckt `object.GetType()`; Typen wie `Frame` und `Environment` gibt es in der SOLIDWORKS-Interop und in .NET. Bei Bemaßungen besser `IDisplayDimension.Type2` als `GetType()`.
54. **Einheiten:** Die API arbeitet immer in Metern und Bogenmaß, unabhängig von den Dokumenteinheiten.

## Geschwindigkeit

55. **Jeder API-Aufruf kann einen Neuaufbau und eine Bildschirmaktualisierung auslösen.** → `FeatureManager.EnableFeatureTree = false`, `ModelView.EnableGraphicsUpdate = false`, `SketchManager.DisplayWhenAdded = false` während des Laufs.
56. **Die Prüfung kostet zwei Rückroll-Neuaufbauten pro Feature des Originals**, Skizzen mit Modellbezügen einen weiteren. Bei großen Teilen spürbar; „Jeden Schritt prüfen“ lässt sich abschalten.

## Was beim ersten Test in SOLIDWORKS zu prüfen ist

Das Add-In übersetzt gegen die API 2019 und 2024, wurde aber noch nicht in einer laufenden Sitzung erprobt. Diese Stellen beruhen auf der API-Hilfe und offiziellen Beispielen, wo diese uneindeutig sind; Spiegelwerk fängt sie ab, der Bericht zeigt, welcher Weg genommen wurde:

| Stelle | Annahme | Absicherung |
| --- | --- | --- |
| Rotationsachse | Markierung 16 (neuere Beispiele), sonst 4 | beide werden versucht |
| Fasenkanten | Markierung 1 wie bei Verrundungen | 0 als zweiter Versuch |
| Ausgewählte Konturen | als `SketchContour` mit Markierung 0 vorwählen | sonst ganze Skizze und Konturen per `ModifyDefinition` |
| Textposition `AddDimension2` | Modellkoordinaten | Skizzenkoordinaten als zweiter Versuch, Wert wird nachgemessen |
| Punktbeziehungen per `SketchAddConstraints` | Kennungen laut Hilfe | mehrere Schreibweisen, Erfolg wird gezählt |
| Zusammenführen von Endpunkten | `AddRelation(MERGEPOINTS)` | deckungsgleich als zweiter Versuch, Punkte werden neu eingelesen |
| Versatzebene | Abstand entlang der Normale der Standardebene | Lage wird gemessen, sonst gespiegelt neu angelegt |
| Rotationsrichtung, Wandseite, Fasenseite, Startversatz | Annahme nach σ | Volumen-/Momentvergleich mit Varianten |
| `Body2.GetMassProperties` | Index 0–2 Schwerpunkt, 3 Volumen | bei falscher Annahme schlägt jede Prüfung an – sofort sichtbar |
| Genauigkeit der Masseneigenschaften | relativ 1e-7 | sonst Untergrenze in `MassComparison.NoiseRelative` anpassen |
| Schließen des Teilfensters in der Baugruppe | `CloseDoc` schließt nur das Fenster, die Baugruppe hält das Teil geladen | — |

Ein guter erster Test: einfache Platte mit Tasche und Verrundung auf *vorne*, an *rechts* gespiegelt, in ein neues Teil – dann dasselbe mit einer Skizze auf *rechts* (σ = −1), mit einer schrägen Hilfsebene und mit einer Tasche, deren Skizze oberhalb des Teils liegt („bis Fläche“).
