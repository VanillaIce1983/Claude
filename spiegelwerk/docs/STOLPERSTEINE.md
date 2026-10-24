# Stolpersteine

Was bei der programmgesteuerten Neuerzeugung von Features in SOLIDWORKS schiefgeht – und wie Spiegelwerk damit umgeht.

## Benutzereinstellungen

1. **„Bemaßungswert eingeben“ blockiert die API.** Ist die Option an, öffnet jede `AddDimension2` den Ändern-Dialog und der Aufruf hängt, bis jemand klickt. → `SwPreferences` schaltet `swInputDimValOnCreate` für die Dauer des Laufs aus und stellt sie in `Dispose` wieder her – auch nach einem Fehler.
2. **Automatische Beziehungen und Fangen.** Beim Zeichnen per API fügt SOLIDWORKS sonst eigene Horizontal-/Deckungsgleich-Beziehungen hinzu und fängt Punkte abhängig vom Zoom. → `SketchManager.AddToDB = true`, dazu `swSketchAutomaticRelations` und `swSketchInference` aus.
3. **Überbestimmende Bemaßungen** lösen eine Rückfrage aus. → `swSketchOverdefiningDimsPromptToSetState` aus, `…SetDrivenByDefault` an.

## Skizzen

4. **AddToDB verbindet keine Endpunkte.** Zwei Linien mit gleichem Endpunkt sind danach zwei Punkte an derselben Stelle; die Kontur gilt als offen. → Gemeinsame Punkte des Originals (gleiche Punkt-ID) werden mit „deckungsgleich“ verbunden.
5. **Das Skizzen-Koordinatensystem ist erst nach dem Öffnen bekannt.** Die Ebene *rechts* hat Skizzen-x = −Z, Hilfsebenen haben Achsen, die SOLIDWORKS festlegt. → Skizze öffnen, `ModelToSketchTransform` lesen, dann rechnen; nie Koordinaten „von Hand“ negieren.
6. **Koordinaten beim Erzeugen** sind in einer 2D-Skizze Skizzenkoordinaten, in einer 3D-Skizze Modellkoordinaten.
7. **`GetSketchPoints2` liefert auch Hilfspunkte** von Segmenten. Als eigenständige Punkte neu erzeugt, entstünden überzählige Punkte. → Nur Punkte aus `GetUserPoints2` werden einzeln angelegt.
8. **Bemaßungen messen nicht zwingend, was man meint.** Winkel (Nebenwinkel), Radius/Durchmesser, Bogenbedingungen hängen von Textposition und Auswahl ab; die Hilfe lässt offen, ob die Position von `AddDimension2` in Modell- oder Skizzenkoordinaten gilt. → Gespiegelte Textposition, beide Koordinatensysteme versuchen, gemessenen Wert mit dem Original vergleichen, falsche Bemaßung löschen statt Wert erzwingen.
9. **Halbe Skizzen.** Bricht etwas mitten in einer Skizze ab, bleibt sie im Bearbeitungsmodus offen und alle folgenden Aufrufe landen darin. → `SketchBuilder` schließt und löscht die angefangene Skizze; `MirrorJob` schließt eine noch aktive Skizze nach jeder Ausnahme.

## Spiegelung und Richtungen

10. **Eine Spiegelung kehrt die Orientierung um.** Kreuzprodukte, Bogensinn, Drehrichtungen, „links/rechts von der Kontur“ ändern sich. Was sich sicher herleiten lässt, regelt σ (`DirectionRules`): Bogensinn, „Richtung umkehren“ bei Extrusionen.
11. **Nicht dokumentierte Voreinstellungen.** Auf welche Seite eine Rotation standardmäßig läuft, auf welche Seite eine dünne Wand bei offenem Profil, welche Fläche bei einer Winkel-Abstand-Fase den Abstand bekommt, wohin ein Startversatz zeigt – das hängt an internen Orientierungen. → Erste Annahme nach σ, dann **messen**: Volumen und Schwerpunkt der Änderung gegen das gespiegelte Original; bei Abweichung probiert `StepVerifier` die Varianten des Builders (umkehren, Seite wechseln, Wandstärken tauschen) und behält die passende.
12. **Aufsatz und Schnitt** extrudieren laut Hilfe standardmäßig entgegengesetzt zueinander (Aufsatz entlang, Schnitt entgegen der Normale). Für die Spiegelung ist das egal, weil es für Original und Kopie gleich gilt – man darf es nur nicht „korrigieren“.

## Auswahl und Markierungen

13. **Viele Methoden lesen ihre Eingaben aus der Auswahl, unterschieden durch Markierungen (Marks)** – und die sind uneinheitlich dokumentiert: Verrundungskanten 1, Referenzebene 0/1/2 für erste/zweite/dritte Referenz, Extrusion: Endbezug 1, Richtung 16, Startbezug 32; Rotationsachse laut Referenz 4, in neueren offiziellen Beispielen 16. → Wo möglich das Feature mit einfachen Parametern anlegen und Bezüge danach über `GetDefinition` → `AccessSelections` → ändern → `ModifyDefinition` setzen (so bei „bis Fläche“, Startbedingung, Konturen, Feature-Bereich). Wo es nicht anders geht, nacheinander die dokumentierten Markierungen versuchen (Rotationsachse 16, dann 4; Fasenkanten 1, dann 0).
14. **`ReleaseSelectionAccess` nicht vergessen.** `AccessSelections` rollt das Modell auf den Stand des Features zurück. Schlägt `ModifyDefinition` fehl, bleibt es zurückgerollt, wenn man den Zugriff nicht freigibt. → `FeatureEdit` gibt bei Fehlschlag frei; beim Lesen sorgt `finally` dafür.
15. **Feature-Daten liefern Objekte im Zustand des Features** – aber nur, solange der Zugriff aktiv ist. → Geometrie (Kantenendpunkte, Flächeninhalt) innerhalb von `AccessSelections` in Fingerabdrücke umrechnen, nicht die Objekte aufheben.

## Topologie

16. **Kanten und Flächen haben keine stabile Identität über Dokumente hinweg.** Persistente IDs (`GetPersistReference3`) gelten nur im selben Dokument. → Geometrische Fingerabdrücke, gespiegelt, im Ziel gesucht – im Ziel herrscht in diesem Moment genau der gespiegelte Zustand.
17. **Gleiche Fingerabdrücke.** Zwei Halbkreise einer Bohrung haben dieselben Endpunkte → Mittelpunkt vergleichen. Im selben Teil existiert eine Kante auf der Spiegelebene zweimal → Körper bevorzugen, die der Lauf selbst erzeugt hat; mehrdeutige Treffer stehen im Bericht.
18. **Später geteilte Kanten.** Wird eine Kante erst durch einen späteren Schnitt geteilt, sieht die Verrundung davor sie noch ganz. → Erfassen über `AccessSelections` (zurückgerollter Zustand), nicht über den Endzustand.

## Namen und Sprache

19. **Namen hängen von Sprache und Vorlage ab:** „Ebene vorne“/„Front Plane“, „Ursprung“/„Origin“. → Standardebenen über ihre Geometrie suchen, den Ursprung über den Typ `OriginProfileFeature`.
20. **Namenskollisionen.** Im selben Teil gibt es die Namen schon; im neuen Teil können Hilfsebenen einen Originalnamen belegen. → Namenszusatz im selben Teil, Hilfsfeatures mit eigenem Präfix, bei Kollision „ (2)“.
21. **Gleichungen verweisen über Namen** (`"D1@Skizze1"`). → Im neuen Teil Feature- und Bemaßungsnamen übernehmen, dann Gleichungen wörtlich kopieren; was nicht passt, steht im Bericht.

## Baugruppen

22. **Geometrie einer Komponente kommt in Teilkoordinaten.** Eine in der Baugruppe gewählte Fläche oder Ebene einer Komponente liefert Normale und Punkt im Teil-Koordinatensystem. → Mit `Component2.Transform2` in Baugruppenkoordinaten umrechnen; Baugruppenebenen nicht.
23. **Eine Komponententransformation darf nicht spiegeln.** `Transform2` muss eine Drehung sein (Determinante +1). → Das Teil selbst gespiegelt aufbauen und mit T′ = R_asm ∘ T ∘ R_part platzieren.
24. **Leichtgewichtige oder unterdrückte Komponenten** haben keine Feature-Daten. → Vorher vollständig auflösen; unterdrückte werden abgelehnt.
25. **Rückrollen und `AccessSelections` brauchen das Teil als eigenes Dokument.** → Teil mit `OpenDoc6` öffnen, danach die Baugruppe wieder aktivieren. `AddComponent5` braucht die neue Datei gespeichert und geladen.
26. **Verknüpfungen werden nicht übernommen.** Die eingefügte Komponente liegt richtig, ist aber frei beweglich.

## Selbes Teil

27. **Gespiegelte Features würden das Original verändern:** Ein verschmelzender Aufsatz verschmilzt mit dem Original, sobald sie sich berühren, ein Schnitt schneidet beide. → Erster Aufsatz ohne Verschmelzen, danach Feature-Bereich auf die eigenen Körper.
28. **Original löschen** kann abhängige Features beschädigen. → Nur bei fehlerfreiem Lauf und wenn nichts außerhalb der Auswahl vom Original abhängt.

## Add-In und COM

29. **x64 und RegAsm.** SOLIDWORKS ist ein 64-Bit-Prozess: mit `Framework64\…\RegAsm.exe /codebase` registrieren, `PlatformTarget` x64.
30. **Interop-Assemblys** mit `EmbedInteropTypes = false` referenzieren und nicht mit ausliefern; zur Laufzeit gelten die der installierten Version.
31. **COM-Objekte freigeben.** Beim Entladen `Marshal.ReleaseComObject` für `SldWorks`/`CommandManager` und zweimal `GC.Collect`/`WaitForPendingFinalizers`, sonst bleibt SOLIDWORKS beim Beenden hängen.
32. **Rückruf über Namen.** `AddCommandItem2` erhält Methodennamen als Text; die Methoden müssen öffentlich auf dem an `SetAddinCallbackInfo2` übergebenen Objekt liegen (`nameof` schützt vor Tippfehlern).
33. **PropertyManager-Seite:** Auswahlen in `OnClose` lesen (danach sind sie weg), modellieren erst in `AfterClose`. OK-Schaltfläche erst freigeben, wenn eine Spiegelebene gewählt ist (`EnableButton`).
34. **Namenskonflikte in C#:** `ISketchSegment.GetType()` verdeckt `object.GetType()`; Typen wie `Frame` und `Environment` gibt es in der SOLIDWORKS-Interop und in .NET. Bei Bemaßungen besser `IDisplayDimension.Type2` als `GetType()`.
35. **Einheiten:** Die API arbeitet immer in Metern und Bogenmaß, unabhängig von den Dokumenteinheiten.

## Geschwindigkeit

36. **Jeder API-Aufruf kann einen Neuaufbau und eine Bildschirmaktualisierung auslösen.** → `FeatureManager.EnableFeatureTree = false`, `ModelView.EnableGraphicsUpdate = false`, `SketchManager.DisplayWhenAdded = false` während des Laufs.
37. **Die Prüfung kostet zwei Rückroll-Neuaufbauten pro Feature des Originals.** Bei großen Teilen spürbar; „Jeden Schritt prüfen“ lässt sich abschalten.

## Was beim ersten Test in SOLIDWORKS zu prüfen ist

Das Add-In übersetzt gegen die API 2019 und 2024, wurde aber noch nicht in einer laufenden Sitzung erprobt. Diese Stellen beruhen auf der API-Hilfe und offiziellen Beispielen, wo diese uneindeutig sind; Spiegelwerk fängt sie ab, der Bericht zeigt, welcher Weg genommen wurde:

| Stelle | Annahme | Absicherung |
| --- | --- | --- |
| Rotationsachse | Markierung 16 (neuere Beispiele), sonst 4 | beide werden versucht |
| Fasenkanten | Markierung 1 wie bei Verrundungen | 0 als zweiter Versuch |
| Ausgewählte Konturen | als `SketchContour` mit Markierung 0 vorwählen | sonst ganze Skizze und Konturen per `ModifyDefinition` |
| Textposition `AddDimension2` | Modellkoordinaten | Skizzenkoordinaten als zweiter Versuch, Wert wird nachgemessen |
| Versatzebene | Abstand entlang der Normale der Standardebene | Lage wird gemessen, sonst gespiegelt neu angelegt |
| Rotationsrichtung, Wandseite, Fasenseite, Startversatz | Annahme nach σ | Volumen-/Schwerpunktvergleich mit Varianten |
| `Body2.GetMassProperties` | Index 0–2 Schwerpunkt, 3 Volumen | bei falscher Annahme schlägt jede Prüfung an – sofort sichtbar |
| Schließen des Teilfensters in der Baugruppe | `CloseDoc` schließt nur das Fenster, die Baugruppe hält das Teil geladen | — |

Ein guter erster Test: einfache Platte mit Tasche und Verrundung auf *vorne*, an *rechts* gespiegelt, in ein neues Teil – dann dasselbe mit einer Skizze auf *rechts* (σ = −1) und mit einer schrägen Hilfsebene.
