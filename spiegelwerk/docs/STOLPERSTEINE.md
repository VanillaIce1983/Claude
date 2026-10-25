# Stolpersteine

Was bei der programmgesteuerten Neuerzeugung von Features in SOLIDWORKS schiefgeht – und wie Spiegelwerk damit umgeht. Ein Teil davon stammt aus einem unabhängigen Code-Review, bei dem jeder Befund von drei Prüfern gegengeprüft wurde.

## Benutzereinstellungen und Modellzustand

1. **„Bemaßungswert eingeben“ blockiert die API.** Ist die Option an, öffnet jede `AddDimension2` den Ändern-Dialog und der Aufruf hängt, bis jemand klickt. → `SwPreferences` schaltet `swInputDimValOnCreate` für die Dauer des Laufs aus und stellt sie in `Dispose` wieder her – auch nach einem Fehler.
2. **Automatische Beziehungen und Fangen.** Beim Zeichnen per API fügt SOLIDWORKS sonst eigene Horizontal-/Deckungsgleich-Beziehungen hinzu und fängt Punkte abhängig vom Zoom. → `SketchManager.AddToDB = true`, dazu `swSketchAutomaticRelations` und `swSketchInference` aus.
3. **Überbestimmende Bemaßungen** lösen eine Rückfrage aus. → `swSketchOverdefiningDimsPromptToSetState` aus, `…SetDrivenByDefault` an; eine so getrieben angelegte Bemaßung wird im Bericht gemeldet.
4. **Rückrollleiste.** Steht sie nicht am Ende, sind spätere Features zurückgerollt, und das Erfassen verstellt sie zwangsläufig. → Spiegelwerk verlangt, dass sie am Ende steht.
5. **Unterdrückte und fehlerhafte Features.** Der Feature-Baum liefert auch unterdrückte Features; neu aufgebaut wären sie plötzlich aktiv. Features, die schon im Original einen Fehler haben, ergeben keine sinnvolle Kopie. → Beide werden übersprungen und im Bericht genannt.

## Skizzen

6. **AddToDB und gemeinsame Endpunkte.** Zwei verbundene Segmente teilen im Original einen Punkt. Beim Neuanlegen kann SOLIDWORKS bitgleiche Endpunkte selbst zusammenlegen – oder zwei Punkte an derselben Stelle anlegen, dann gilt die Kontur als offen. → Punkt-IDs vergleichen; nur getrennte Punkte zusammenführen (`MERGEPOINTS`, sonst deckungsgleich) – sofort, und den überlebenden Punkt gleich wieder einlesen, weil das Zusammenführen einen von beiden entfernt. Wer erst am Ende einliest, schließt an einer Ecke mit drei Segmenten das dritte an einen gelöschten Punkt an.
7. **Das Skizzen-Koordinatensystem ist erst nach dem Öffnen bekannt.** Die Ebene *rechts* hat Skizzen-x = −Z, Hilfsebenen haben Achsen, die SOLIDWORKS festlegt. → Skizze öffnen, `ModelToSketchTransform` lesen, dann rechnen; nie Koordinaten „von Hand“ negieren.
8. **Koordinaten beim Erzeugen** sind in einer 2D-Skizze Skizzenkoordinaten, in einer 3D-Skizze Modellkoordinaten.
9. **`GetSketchPoints2` liefert auch Hilfspunkte** von Segmenten. Als eigenständige Punkte neu erzeugt, entstünden überzählige Punkte. → Nur Punkte aus `GetUserPoints2` werden einzeln angelegt.
10. **Bemaßungen messen nicht zwingend, was man meint.** Winkel (Nebenwinkel), Radius/Durchmesser, Bogenbedingungen hängen von Textposition und Auswahl ab; die Hilfe lässt offen, ob die Position von `AddDimension2` in Modell- oder Skizzenkoordinaten gilt. → Gespiegelte Textposition, beide Koordinatensysteme versuchen, gemessenen Wert mit dem Original vergleichen, falsche Bemaßung löschen statt Wert erzwingen.
11. **Fehlende Bezüge nicht pauschal fixieren.** Wer statt einer verlorenen Beziehung zum Ursprung oder zu einer Kante die eigenen Skizzenelemente fixiert, überbestimmt die Skizze leicht (eine fixierte Diagonale fixiert die Ecken, die Horizontal-Beziehungen werden redundant). → Fixierte Hilfsgeometrie an der gespiegelten Stelle anlegen und die ursprüngliche Beziehung oder Bemaßung daran hängen; danach den Bestimmtheitsgrad mit dem Original vergleichen (`GetConstrainedStatus`).
12. **`SketchAddConstraints` hat eigene Kennungen für Punktbeziehungen** (`sgHORIZONTALPOINTS2D` statt `sgHORIZONTAL2D`, `sgMERGEPOINTS`), und Hilfe und Makroaufzeichnung schreiben manche verschieden. → Typisierte `AddRelation` zuerst, auch für den Ursprung und andere Skizzen; sonst Kandidaten der Reihe nach, Erfolg an der Zahl der Beziehungen ablesen.
13. **„Auf Kante“ und „Versatz“ sind keine Beziehungen, die man anlegen kann.** Übernommene Elemente hängen über `USEEDGE` an Modellkanten, versetzte über `OFFSETEDGE`/`SKETCHOFFSET` an ihrer Quelle. → Übernommene kollinear bzw. koradial zur gespiegelten Kante anbinden; versetzte nur parallel bzw. konzentrisch, den Abstand macht die Versatzbemaßung – kollinear zöge sie auf die Quelle.
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

## Austragung, Ausformung, Muster

57. **Profil, Pfad und Führungskurven sind eigene Skizzen.** Sie werden vorher als gewöhnliche Skizzen gespiegelt und dann mit den Markierungen der Hilfe gewählt: Profil 1, Führungskurve 2, Pfad 4 (Austragung); Profile 1 in Originalreihenfolge, Führungskurven 2, Mittellinie 4 (Ausformung). Ein Pfad aus einer 3D-Skizze, einer Helix oder aus Modellkanten wird nicht unterstützt und im Bericht genannt.
58. **Richtungsvektoren per Markierung (128 …) werden laut Anwenderberichten beim Erzeugen ignoriert.** → Start-/Endtangente, Pfadausrichtung und Richtungsvektoren entstehen zunächst als „keine“ und werden per `ModifyDefinition` gesetzt.
59. **Die Verdrehung einer Austragung läuft im Spiegelbild andersherum.** Ob SOLIDWORKS den Drehsinn um den Pfad oder um die Profilnormale misst, ist nicht dokumentiert. → Erste Variante gegensinnig, die andere als Alternative. Mit symmetrischem Profil haben beide Varianten dasselbe Volumen und denselben Schwerpunkt – erst die Flächenproben unterscheiden sie.
60. **Verbinder einer Ausformung** ergeben sich aus der Stelle, an der jedes Profil gewählt wurde. Mit `Select2` ohne Punkt verbindet SOLIDWORKS irgendwo, die Ausformung ist verdreht. → Skizzenprofile per `SelectByID2` am gespiegelten Auswahlpunkt wählen; danach `ILoftFeatureData.PickPoints` mit den gespiegelten Punkten vergleichen und bei Abweichung setzen. Die Struktur (je Kette ein Feld von `MathPoint`) stammt aus dem Beispiel der Hilfe; passt sie nicht, steht es im Bericht.
61. **„Normal zum Profil“ zeigt entlang der Skizzennormale,** die in der Kopie umgekehrt sein kann (σ < 0). → Umkehrflag nach σ, Gegenrichtung als Alternative.
62. **Ausformungsschnitte kennen beim Erzeugen weder Tangentenlänge noch -richtung** (`InsertCutBlend`). → Nachträglich per `ModifyDefinition`.
63. **Die Richtung einer Kante im neuen Teil ist beliebig,** der Drehsinn eines Kreismusters kehrt sich im Spiegelbild um. Ein kopiertes „Richtung umkehren“ ist deshalb Glückssache. → Die Lage der Instanzen (`GetTransform`) des Originals wird gespiegelt (R ∘ T ∘ R) und mit der des neuen Musters als Menge verglichen; die Flags werden durchprobiert, bis beide übereinstimmen. Erst wenn die Lage nicht lesbar ist, entscheidet die Massenprüfung.
64. **Muster ohne Saat.** Wer nur das Muster wählt, bekäme ein Muster ohne etwas zu wiederholen. → Die Saat eines gewählten Musters wird automatisch mitgespiegelt: Saat-Features, bei Flächenmustern die Features der Flächen, bei Körpermustern die Features der Körper. Ist eine Saat nicht spiegelbar (z. B. eine Bohrung des Bohrungsassistenten), steht das beim Muster im Bericht. Bestätigt die Lage der Instanzen die Richtung, bietet das Muster der Massenprüfung keine Richtungsvarianten mehr an – eine fehlende Saat soll keine richtig gemessene Richtung umkippen.
65. **`FeatureLinearPattern5`/`FeatureCircularPattern5` gelten ab 2024 als veraltet,** der Weg über `CreateDefinition` + `CreateFeature` ist in 2019 nicht für jeden Typ belegt. → Beide Wege; bei reinen Feature-Mustern zuerst die Auswahl (Richtung 1/2, Features 4), bei Flächen- und Körpermustern zuerst das Datenobjekt (die Markierungen 128/256 sind nicht belegt).
66. **Muster haben in der API 2019 keinen Feature-Bereich** (die Interop 2024 hat `FeatureScope`/`SetFeatureScope`). Im selben Teil kann eine Instanz eines Schnitts das Original treffen. → Wo die laufende Version ihn kennt, wird der Bereich spät gebunden (IDispatch) auf die gespiegelten Körper gesetzt; in jedem Fall werden nach jedem Feature Volumen und Moment der Originalkörper verglichen, und hat sich etwas geändert, wird das Feature entfernt.
67. **Achsen ohne Gegenstück.** Eine Referenzachse des Originals hat im neuen Teil oft keine Kante, auf der sie liegt; eine Kante des Originals im selben Teil wäre wieder eine Abhängigkeit. → Hilfsachse auf einer fixierten Linie einer 3D-Skizze; Achsen des Originals werden so unter ihrem Namen neu angelegt.
68. **Bis zu 400 Instanzen** werden einzeln gelesen (ein Aufruf je Instanz); größere Muster prüft nur die Massenprüfung. Die Flächenprüfung kostet je Feature bis zu acht Abstandsmessungen gegen die Flächen des Features, im Zweifel gegen alle Flächen.
69. **Die Baumreihenfolge ist nicht die Erzeugungsreihenfolge.** Eine Ausformung nimmt ihre Profilskizzen unter sich auf, eine danach gezeichnete Führungskurve mit Durchstoß-Beziehungen zu den Profilen steht oben im Baum davor. In Baumreihenfolge neu aufgebaut, fände die Führungskurve ihre Profile noch nicht und verlöre ihre Beziehungen. → Nach Abhängigkeiten sortieren (`IFeature.GetParents`), nur unter den Features, die der Baum ohnehin liefert.
70. **Richtungsvektoren von Führungskurven** (Tangentenbedingung „Richtungsvektor“ einer Führungskurve einer Ausformung) sind über die API weder lesbar noch setzbar. → Bedingung „keine“ mit Hinweis, statt die ganze Ausformung an einer nicht übertragbaren Einzelheit scheitern zu lassen.
71. **Bezüge, die vor einer Hilfsebene oder -achse gefunden wurden,** stammen aus dem Modell vor deren Neuaufbau. → Hilfs-Features verwerfen den Kanten-/Flächen-Cache; Muster lösen erst Richtungen, dann Saat-Flächen auf.

## Bohrungsassistent

72. **Zwei Skizzen gehören der Bohrung.** Die Profil- und die Positionsskizze einer Bohrung erscheinen als gewöhnliche Unter-Features; einzeln neu aufgebaut, entstünden eine verwaiste Profilskizze und eine Positionsskizze, die die neue Bohrung nicht verwendet. → Als „Teil der Bohrung“ erfassen; die Positionsskizze wird in die Skizze der neuen Bohrung geschrieben (`SketchBuilder.BuildInto`), der Punkt der ersten Bohrung danach entfernt.
73. **Der allgemeine Bohrungstyp ist nicht lesbar.** `IWizardHoleFeatureData2.Type` liefert den genauen Typ (`swWzdHoleTypes_e`, z. B. „Senkbohrung durchgehend mit Senkung unten“), `InitializeHole` und `HoleWizard5` wollen den allgemeinen (`swWzdGeneralHoleTypes_e`). → Feste Zuordnung `HoleTypes.GenericOf` nach den Namen der Aufzählung; unbekannte Nummern (neuere Versionen) werden nicht geraten, sondern gemeldet.
74. **`HoleWizard5` hat zwölf typabhängige Werte** (`Value1`–`Value12`), deren Bedeutung je Bohrungstyp wechselt. → Erster Weg über das Datenobjekt (`CreateDefinition` + `InitializeHole`), dort sind die Maße benannte Eigenschaften; beim Ersatzweg bleiben die zwölf Werte −1 (ignoriert) und die Maße folgen per `ModifyDefinition`.
75. **Eigene Maße nicht erzwingen.** Jedes gesetzte Maß kann eine Normbohrung zur Bohrung mit benutzerdefinierten Maßen machen. → Nur Werte schreiben, die von den Normmaßen der neuen Bohrung abweichen; was sich nicht setzen lässt, steht im Bericht.
76. **Die Wahl der Fläche setzt die erste Bohrung.** Im selben Teil kann am gespiegelten Punkt auch eine Fläche des Originals liegen. → Gewählte Fläche gegen die gesuchte prüfen (Fingerabdruck; mit eigenem Körper auch, dass sie auf ihm liegt), sonst die Fläche ohne Punkt wählen; die Positionsskizze legt die Lage ohnehin fest.
77. **Gewinde nicht spiegeln.** Ein gespiegeltes Gewinde wäre ein Linksgewinde. Ein Gegenstück braucht aber dieselben Schrauben. → Gewindeart, Größe und Klasse unverändert; die kosmetische Darstellung trägt keine Drehrichtung.
78. **Nicht übertragen:** Langlöcher (ihre Ausrichtung ist über die API nicht lesbar), Bohrungen im alten Format, Bohrungen mit 3D-Positionsskizze (auf mehreren oder gekrümmten Flächen) – das gilt auch für erweiterte Bohrungen (siehe 98–103). Sie stehen mit Grund im Bericht.
79. **Jeder Skizzenpunkt der Positionsskizze ist eine Bohrung** – auch ein Ersatzpunkt für einen fehlenden Bezug (Ursprung abseits der Spiegelebene). → In der Positionsskizze werden Ersatzpunkte als Endpunkte fixierter Konstruktionslinien angelegt.
80. **Die Skizzen einer Bohrung, die nicht entsteht.** Wird die Bohrung nicht neu aufgebaut (nicht gewählt, nicht unterstützt, übersprungen), wird die Positionsskizze wieder eine gewöhnliche Skizze und die Profilskizze entfällt mit Hinweis; scheitert die Bohrung, nimmt der Bericht das „entsteht mit ihr“ zurück. Wird eine Bohrung wieder entfernt, gehen ihre Skizzen mit (`swDelete_Absorbed`), und die Zuordnung der Positionsskizze wird vergessen.
81. **Varianten ohne eigene Option** (z. B. Gewindebohrung mit entferntem Gewinde) zeigen sich nur im genauen Typ. → Typ der Kopie mit dem Original vergleichen, bei Abweichung setzen, sonst melden.

## Wandung und Formschräge

82. **`InsertFeatureShell` gibt kein Feature zurück.** → Das neue Feature ist das zuletzt hinzugefügte, wenn es eine Wandung ist und vorher nicht da war.
83. **Mehrfachdicken über `InsertFeatureShellAddThickness`** sind kaum dokumentiert (Reihenfolge, Markierungen). → Wandung mit den offenen Flächen anlegen, Flächen mit eigener Stärke danach über `MultipleThicknessFaces` und `SetMultipleThicknessAtIndex` setzen.
84. **Geschlossene Wandung:** Ohne offene Flächen sagt die Wandung nicht, welchen Körper sie aushöhlt, und ohne Auswahl könnte SOLIDWORKS einen beliebigen oder alle nehmen. → Eine Fläche des ausgehöhlten Körpers erfassen, die nicht von der Wandung stammt, und im Ziel genau den Körper wählen, auf dem ihr gespiegelter Punkt liegt (im selben Teil mit eigenem Körper nur gespiegelte Körper). Nicht eindeutig oder noch kein gespiegelter Körper: keine Wandung, sondern eine Meldung. Ohne Auswahl nur, wenn das Teil genau einen Körper hat. Achtung bei der Wandung nach außen: Die erfasste Fläche ist dann die Wand des Hohlraums, ihre Normale zeigt andersherum als vor der Wandung.
85. **Eine fehlende offene Fläche** ergäbe einen anderen Körper (dort geschlossen). → Dann keine Wandung, sondern eine Meldung. Fehlende Flächen mit eigener Stärke bekommen die Standardstärke, mit Hinweis.
86. **Die Zugrichtung einer Formschräge hängt am Bezug.** Eine Ebene, die mit der gespiegelten zusammenfällt, kann die entgegengesetzte Normale haben (die Standardebene *rechts* beim Spiegeln an *rechts*). → Flag nach σ der Bezugsrichtung; die Gegenrichtung bleibt als Variante für die Prüfung.
87. **Trennlinien.** Eine Trennlinie mit Lücke ist keine; die „andere Seite“ je Kante hängt an der Kantenorientierung. → Alle Kanten oder keine Formschräge; Seiten übernehmen, Gegenseite als Variante. Die Zuordnung der Seiten-Flags zu den Kanten folgt der Auswahlreihenfolge (Annahme, siehe Tabelle).
88. **Körper-Formschrägen und DraftXpert** lassen sich über `IDraftFeatureData2` nicht erkennen; sie werden als gewöhnliche Formschräge neu aufgebaut.

## Variable Verrundung

89. **Reihenfolge der Radien bei `FeatureFillet3` ist nicht dokumentiert.** → Anlegen mit einer plausiblen Reihenfolge (Kantenenden in Auswahlreihenfolge), dann jeden Eckpunkt über `SetRadius(Eckpunkt, …)` setzen – den Radius des gespiegelten Eckpunkts, oder den Standardradius, wo das Original keinen eigenen hatte (dort kann beim Anlegen ein Radius für eine andere Ecke gelandet sein). Die Eckpunkte werden unter `AccessSelections` aus den Kanten der neuen Verrundung gelesen und nach Lage zugeordnet.
90. **Kontrollpunkte zählen vom Kantenanfang.** Die gespiegelte Kante kann im neuen Teil andersherum laufen; 25 % wären dann 75 %. → Anfang und Ende der neuen Kante mit der gespiegelten vergleichen (wie beim Zuordnen der Kanten über die Summe der Endabstände) und die Lage notfalls von der anderen Seite zählen. Ob die Lage als Bruchteil oder in Prozent gemeldet wird, ist nicht dokumentiert: Werte über 1 gelten als Prozent.
91. **Kontrollpunkte gibt es nur in der Anzahl, die die Verrundung hat** (anfangs drei je Kante). → Nur übertragen, wenn die Anzahl je Kante mit dem Original übereinstimmt; sonst Hinweis, und die Prüfung zeigt die Abweichung.
92. **Variable und einfache Verrundung** könnten beide Datenschnittstellen beantworten. → Die variable zuerst abfragen.
93. **Verluste, die schon beim Erfassen feststehen** (Feature-Bereich, Rücksprungabstände, Regionen, nicht lesbare Verbinder …), standen bisher nur in den Details, der Eintrag blieb „OK“ – und „Original löschen“ hätte gegriffen. → Jeder Hinweis aus dem Erfassen macht ein sonst fehlerfreies Feature zur Warnung.

## Flächen- und vollständige Verrundung

94. **Eigene Markierungen für jede Flächengruppe:** Gruppe 1 = 2, Gruppe 2 = 4, Mittelflächen einer vollständigen Verrundung = 512; die Benutzeroberfläche verlangt die Reihenfolge Seite 1, Mitte, Seite 2. → Genau so wählen.
95. **Unvollständige Flächengruppen** ergäben eine Verrundung zwischen anderen Flächen. → Fehlt eine Fläche, keine Verrundung, sondern eine Meldung.
96. **Normalenrichtung der Gruppen** steuert, auf welcher Seite die Flächenverrundung entsteht. Außennormalen bleiben bei einer Spiegelung außen. → Flags unverändert (als Optionen beim Anlegen und danach geprüft). Als Varianten für die Prüfung: zuerst beide Gruppen zusammen umgekehrt – wird das Flag anders gelesen als angenommen, ist es für beide falsch, und eine einzeln umgekehrte Gruppe scheitert nur –, dann jede Gruppe einzeln. Hilfspunkt und Haltelinien aus Trennlinien-Kurven werden nicht übernommen (Hinweis).
97. **Haltelinien bestimmen die Größe**, der gespeicherte Radius ist dann bedeutungslos und passt womöglich gar nicht. → Wird eine Haltelinie im Ziel nicht gefunden, keine Verrundung (Haltelinien, die schon beim Erfassen keine Kanten sind, siehe 96: Hinweis und Warnung); passt der Radius beim Anlegen nicht, mit anderem Platzhalter anlegen (kleiner, sonst größer), bis die Haltelinien gesetzt sind; lassen sie sich nicht setzen, wird die Verrundung wieder entfernt.

## Erweiterte Bohrung

98. **Keine Flächen in den Feature-Daten:** `IAdvancedHoleFeatureData` nennt weder nah- noch fernseitige Fläche. → Beim Erfassen geometrisch bestimmen, im Zustand vor der Bohrung (Rückrollleiste vor das Feature, außerhalb des Auswahlzugriffs): nahseitig die ebene Fläche unter einem Bohrpunkt, parallel zur Positionsskizze; fernseitig die erste ebene Fläche desselben Körpers, durch die die Bohrungsachse ihn verlässt. Die fernseitige Annahme steht im Bericht.
99. **Die Elementschnittstellen erben nicht voneinander:** `ICounterboreElementData` & Co. sind keine `IAdvancedHoleElementData`. → Dasselbe COM-Objekt über beide Schnittstellen ansprechen; jeder Zugriff einzeln abgesichert.
100. **Werte hängen an Schaltern:** Ein eigener Durchmesser zählt nur mit `DiameterOverride`, ein Senkwinkel nur mit `AngleOverride`, die Tiefe nur ohne Standardtiefe. → Erst Norm, Schraubenart und Größe, dann alle Schalter, dann Endbedingung samt Bezug, dann die Werte; verglichen werden nur Werte, die für das Element gelten (Tiefe nur bei „blind“, Abstand nur bei „Abstand von Fläche“, Bohrspitzenwinkel und Gewindeklasse nur mit ihrem Schalter).
101. **Auswahl beim Anlegen undokumentiert:** Das Beispiel der Hilfe wählt nah- und fernseitige Fläche vor `AdvancedHole`; welche Markierungen gelten und ob der Aufruf `object[]` oder `DispatchWrapper[]` erwartet, steht nicht da. → Varianten nacheinander; eine Bohrung, deren Positionsskizze auf der fernseitigen Fläche liegt (Seiten vertauscht), wird entfernt, und dieselbe Variante folgt mit getauschten Flächen. Die Array-Form, die beim Anlegen funktioniert hat, gilt auch für das nachträgliche Korrigieren.
102. **Typname der erweiterten Bohrung** ist nicht dokumentiert. → Für die Teil-Skizzen werden Features, deren Typname „Hole“ enthält, nach ihrer Definition gefragt; erfasst wird ohnehin über die Datenschnittstelle.
103. **Kein Feature-Bereich:** Die erweiterte Bohrung kennt keine Körperauswahl. → Im selben Teil greift die Prüfung der Originalkörper und entfernt eine Bohrung, die das Original erreichen würde.

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
| Austragung: Profil 1, Führung 2, Pfad 4 | laut Hilfe zu `InsertProtrusionSwept3`/`InsertCutSwept4` | — |
| Verdrehrichtung der Austragung | gegensinnig zum Original | Alternative, Flächenproben |
| Ausformung: Profile 1, Führung 2, Mittellinie 4 | laut Hilfe zu `InsertProtrusionBlend` | — |
| Verbinder der Ausformung | `SelectByID2` mit Punkt, `PickPoints` als Ketten von `MathPoint` | Vergleich nach dem Erzeugen, sonst Hinweis |
| Muster: Richtung 1/2, Features 4, `DName` „NULL“ | laut Beispielen der Hilfe | `CreateDefinition`/`CreateFeature` als zweiter Weg |
| `GetTransform` eines Musters | Platzierung Saat → Instanz in Teilkoordinaten, unter `AccessSelections` lesbar | nicht lesbar: Massenprüfung mit Varianten |
| Hilfsachse | `InsertAxis2` auf einer gewählten 3D-Skizzenlinie | Lage wird nachgemessen, sonst Fehler mit Hinweis |
| Flächenproben | `IFeature.GetFaces` liefert im zurückgerollten Zustand die Flächen des Features | ohne Flächen keine Proben, nur Massenprüfung |
| Bohrung über Datenobjekt | `CreateDefinition(swFmHoleWzd)` + `InitializeHole` + Fläche am Punkt gewählt + `CreateFeature` (Beispiel der Hilfe) | `HoleWizard5` mit −1 für die typabhängigen Werte |
| Positionsskizze der neuen Bohrung | 2D-Skizze auf ebener Fläche, über `GetSketchPoints` zu finden, per `EditSketch` beschreibbar | sonst wird die Bohrung entfernt und gemeldet |
| Erweiterte Bohrung | nahseitige Fläche am ersten Bohrpunkt (Markierung 0), fernseitige mit 1; Elemente als `object[]` | Markierungen 1/2 und 0/0, `DispatchWrapper[]`; vertauschte Seiten werden erkannt, die Bohrung entfernt und mit getauschter Auswahl neu angelegt |
| Positionsskizze der erweiterten Bohrung | 2D-Unterskizze mit den Bohrpunkten auf der nahseitigen Ebene, per `EditSketch` beschreibbar | ohne sie wird die Bohrung entfernt und gemeldet |
| Elemente nachträglich ändern | `GetNearSideElements` → ändern → `SetNearSideElements` → `ModifyDefinition` | Abweichungen stehen im Bericht |
| Wandung | offene Flächen mit Markierung 1 (Beispiel eines Drittanbieters), sonst 0; `Direction` 0/1 = innen/außen | Richtung wird nach dem Anlegen verglichen und gesetzt; Alternative für die Prüfung |
| Flächen-/vollständige Verrundung | Markierungen 2/4 und 512 (API-Hilfe), Optionen für die Normalenrichtung | Flags danach verglichen, Varianten für die Prüfung |
| Variable Verrundung | Kanten mit Markierung 1, `FeatureFillet3` mit Typ „variabler Radius“ | alle Werte danach über die Feature-Daten |
| Lage der Kontrollpunkte | Bruchteil 0–1 oder Prozent, ab Kantenanfang | Prüfung zeigt Abweichungen |
| Geschlossene Wandung | Körper mit Markierung 1 (sonst 0) wählen | bei genau einem Körper auch ohne Auswahl |
| Formschräge | Zugbezug 1, Flächen 2, Trennlinien 4 (API-Hilfe) | — |
| Seiten einer Trennlinie | `SetOtherFacesFlagAtIndex` zählt in Auswahlreihenfolge | Variante „andere Seite“ für die Prüfung |
| Feature-Bereich von Mustern | `FeatureScope`/`SetFeatureScope` spät gebunden, falls vorhanden (Interop 2024) | sonst Prüfung der Originalkörper, Feature wird ggf. entfernt |
| `Body2.GetMassProperties` | Index 0–2 Schwerpunkt, 3 Volumen | bei falscher Annahme schlägt jede Prüfung an – sofort sichtbar |
| Genauigkeit der Masseneigenschaften | relativ 1e-7 | sonst Untergrenze in `MassComparison.NoiseRelative` anpassen |
| Schließen des Teilfensters in der Baugruppe | `CloseDoc` schließt nur das Fenster, die Baugruppe hält das Teil geladen | — |

Ein guter erster Test: einfache Platte mit Tasche und Verrundung auf *vorne*, an *rechts* gespiegelt, in ein neues Teil – dann dasselbe mit einer Skizze auf *rechts* (σ = −1), mit einer schrägen Hilfsebene und mit einer Tasche, deren Skizze oberhalb des Teils liegt („bis Fläche“). Danach: ein lineares Muster entlang einer Kante und ein Kreismuster mit 3 × 40° um eine Referenzachse (Richtung muss im Bericht „geprüft“ oder „korrigiert“ heißen), eine Austragung eines Quadrats mit 90° Verdrehung entlang einer Geraden und eine Ausformung zwischen einem Quadrat und einem um 45° gedrehten Quadrat (Verbinder). Für Bohrungen: drei M6-Senkbohrungen auf einer Platte, mit Abständen zu den Kanten bemaßt, an *rechts* gespiegelt – im Bericht „3 × M6“ ohne Hinweis auf nicht übernommene Maße – und ein Muster dieser Bohrung. Für Wandung und Formschräge: ein Napf mit offener Oberseite und dickerem Boden, mit 3° Formschräge an der Ebene *oben* – einmal an *rechts*, einmal an *oben* gespiegelt (σ = −1 für die Zugrichtung). Für die variable Verrundung: eine Kante mit 2 mm am einen und 5 mm am anderen Ende und einem Kontrollpunkt mit 3 mm bei 25 % – gespiegelt muss der dünne Teil am gespiegelten Ende liegen.
