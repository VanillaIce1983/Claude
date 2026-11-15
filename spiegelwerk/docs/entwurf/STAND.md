# Zwischenstand (13.11.2026)

Arbeitsstand von Spiegelwerk 0.11 im Hauptzweig `claude/cool-ritchie-ltfl07`. Diese Datei sagt, was
erledigt ist, was läuft und in welcher Reihenfolge es weitergeht.

## Erledigt

- `9df9ea1`: Befunde der Gesamtprüfung behoben (`korrekturen-9df9ea1.json`, alle 74 Befunde der Prüfung
  in `offene-punkte.json`).
- Prüfung von `9df9ea1` nachgeholt: 18 bestätigte Befunde behoben (Zweig `fix-9df9ea1`, Teil 1–4),
  die Korrekturen ihrerseits geprüft und deren Befunde behoben (Commit „Befunde aus der Prüfung der
  Korrekturen behoben“). Doku nachgezogen.
- Installer (`installer/`): WiX 5 (MS-RL), ein MSI für SOLIDWORKS Desktop und 3DEXPERIENCE; die CI baut,
  installiert, erzeugt per COM, deinstalliert; Release bei Tag `spiegelwerk-v*`. Entwurf: `installer.md`.
- Interop-Typen eingebettet (No-PIA): `Spiegelwerk.dll` verweist auf keine Interop-Assembly; COM-Rückgaben
  vom Typ `object` sind ausdrücklich gecastet. Core-Tests unter Windows zusätzlich unter net48.
- `LICENSE` (Alle Rechte vorbehalten), `Directory.Build.props` (Version 0.11.0).
- Spiegel-Features (Features/Flächen spiegeln, Körper spiegeln) ohne Spiegel-API nach `spiegel-features-gegenpruefung.md`
  (Zweig `feat-spiegel-features`, geprüft, Befunde behoben, eingefügt).
- Ausgewählte Skizzenbereiche bei Extrusion und Rotation nach `skizzenbereiche-gegenpruefung.md`
  (Zweig `feat-skizzenbereiche`, geprüft, Befunde behoben, eingefügt).
- Gegenprüfung der vier Blech-Entwürfe (`blech-*-gegenpruefung.md`) und Integrationsplan `blech-plan.md`
  (gemeinsame Grundlagen, 28 aufgelöste Widersprüche, Schritte S0–S10, Ersatzweg als S7, Live-Test-Annahmen).
- Blech S0 (Zweig `feat-blech`): Grundlage ohne Verhaltensänderung – gemeinsame Blech-Werte und -Regeln in Core
  (`SheetMetalRecords`, `SheetMetalCompare`, `AllowanceRules`, `BendOverrides`), Typtabelle mit Rolle und Zustand
  (`SheetMetalTypes`, alle „noch nicht“) und `RefusesBeforeCapture`, ein Besitzmodell für Bohrungen und Blech
  (`OwnedSketches`, `BringsParts`, `PartOwnerKind`, `PositionedFeatureRecord`), ausnahmesicherer Blech-Verteiler beim
  Erfassen (noch ohne Leser; jeder nicht unterstützte Eintrag mit dem Ersatzweg-Flag seines Typs), Spiegel-Features
  über Blech als nicht unterstützt, `SwLate.Invoke`/`InvokeOut`; Biegereihenfolge nur als Information
  (`BendOverrides.OrderNotes`), feste Referenz eines Blechkörpers nur Fläche oder Kante.
  Abweichend vom Plan: STOLPERSTEINE 139 aus dem Plan ist nach dem Einfügen der beiden Zweige 151; die neuen Zeilen
  stehen als 164–172 in einem eigenen Abschnitt. Die geschlossene Ecke (`CornerFeat`) steht schon in der Tabelle, die
  Eckentlastung nicht (Name unbestätigt, §1.5). `RefusesRun` bleibt als Kurzform von `RefusesBeforeCapture` (ohne
  angenommenen Besitzer, Ersatzweg aus) für die unveränderten `ReportTextTests`.
- Blech S1a (Zweig `feat-blech`): Basislasche und Lasche, Blech-Feature als Begleiter, Abwicklung, Schnitte mit „an Dicke
  binden“ – in ein neues Teil und in Baugruppen. Core: `SheetMetalRules` (Seiten, Endbedingungen, Werte beim Anlegen,
  Wege), `SheetMetalTree` (Besitz, Ordner, Auswahl, Ecke trimmen, Features hinter der Abwicklung), `SheetMetalPlanner`
  (zweite Stufe der Ablehnung ohne Pläne), `UnsupportedFeatures.RefusalBeforeCapture` (Besitzer, selbes Teil),
  Zustände in `SheetMetalTypes`; Tests `SheetMetalTests` (Seiten am Zufall, L-Blech-Prüfung), `SheetMetalTreeTests`,
  Ablehnung. Add-In: `SwSheetMetal`, `SheetMetalSession`, `SheetMetalGuard`, Leser in `FeatureCapture.SheetMetal`,
  `BaseFlangeBuilder`, `FlatPatternBuilder`, Ordnerinhalt in Erstellungsreihenfolge, Snapshot und Aufräumen,
  Begleiter, Wächter und Messung des abgewickelten Zustands. STOLPERSTEINE 173–186, Prüftabelle.
  Abweichend vom Plan: Der Wächter erwartet auch im neuen Teil den Stand vor dem Schritt (nicht `Defaults ?? Body`,
  STOLPERSTEINE 173); `BodyHistory` (schon in `BodyScope.cs`) bekam `IsSheetMetal`, statt in `SheetMetalPlanner` neu zu
  entstehen; die Meldung der zweiten Stufe hat noch keinen Satz zur Ersatzweg-Option (die gibt es erst mit S7a); die
  Ordner stehen als „nachgebaut“ in der Tabelle (nie gesammelt); nur das Blech-Feature ist Begleiter, die Abwicklung hat
  ihren eigenen Datensatz und Builder; Flächenproben auf Biegungen (`SampleSubFeatures`) erst mit S2. Normal Cut bleibt
  bei S6. Nicht in einer laufenden SOLIDWORKS-Sitzung erprobt.
- Blech S1a geprüft und gegengeprüft, Befunde behoben: Basislasche aus den aufgelösten Seiten angelegt (ersetzte
  Endbedingung mit ihrer Tiefe), gewählte Körper nach dem Falten über ihren Namen neu gesucht, Besitzer der Abwicklung
  über den Körper der festen Fläche (wie A1 §6) und – Rückfall zu B9 – aus dem Baum, Wächter ohne den Körper, den eine
  Lasche erweitert, Ablehnung nennt eine nicht lesbare Basislasche mit ihrem Grund, eigene Werte des ersten Körpers
  nach dem zweiten Blechkörper (oder als Werte des Teils, wenn die Kopie ein Einkörperteil bleibt), Wiederherstellen
  des Originals auch in der Meldung eines abgelehnten Laufs. STOLPERSTEINE 187–191, Prüftabelle.
  Abweichend vom Plan: Zu B15 nennt der Plan nur Rücklesen und „nicht anwendbar“; die Überschreibungen des ersten
  Körpers warten jetzt auf den zweiten (`BodyOverrideModeOf`), und der Wächter lässt den Körper einer Lasche aus.
- Blech S1b (Zweig `feat-blech`): Basislasche und Abwicklung im selben Teil, die Ablehnung im selben Teil aufgehoben.
  Core: `SheetMetalRules.GuardExpectationOf` (Stand ab der ersten Kopie, ohne lesbaren Stand keine Basislasche),
  `GuardComparesBodies`, `GuardAbort`/`GuardRestored` (Meldung nennt das Original), `RestoreGroups`, `SamePartOverrides`
  und `BodyValues`, `BaseFlangeToggles` (keine „Dickenrichtung umgekehrt“ im selben Teil), `TabRefusal`, `DeleteRefusal`,
  Texte zu den geteilten Standardwerten; `UnsupportedFeatures.RefusalBeforeCapture` ohne `samePart`; Tests
  `SheetMetalSamePartTests`, Ablehnung. Add-In: `SheetMetalGuard` mit `samePart` (Körper samt Original immer verglichen,
  Einkörper-Stand über die abweichenden Gruppen zurückgeschrieben), `StepRunner` (ohne Stand keine Basislasche),
  `BaseFlangeBuilder` (Lasche ohne eigenen Körper abgelehnt, Masse des Originals nach jedem Versuch, Blech-Feature des
  Originals für eine Lasche mit nur gewählten Features, eigene Werte der Kopie), `FlatPatternBuilder` (Flächen nur eigener
  Körper), `SheetMetalSession.ApplyTo` einmal und auch nach einem unerwarteten Fehler, `MirrorJob` („Original löschen“
  abgelehnt, geteilte Standardwerte und Gleichungen darauf im Bericht). STOLPERSTEINE 192–198, Prüftabelle.
  Abweichend vom Plan: Der Wächter erwartet seit S1a in beiden Fällen den Stand vor dem Schritt; `GuardExpectationOf`
  unterscheidet deshalb nicht „Standardwerte oder Körper“ im neuen Teil, sondern nur, ob ein Stand da sein muss (im
  selben Teil) – der Test heißt entsprechend. Im selben Teil vergleicht der Wächter die Körper immer, nicht nur nach einer
  Änderung der Werte, und `SamePartOverrides`/`RestoreGroups` kommen hinzu (Einkörper-Original, B14/B15 offen). Mit nur
  gewählten Features darf eine Lasche am Körper des Originals anschließen (A1 §11 nennt nur den eigenen Körper; A2/A3 §8
  erlauben Bezüge auf das Original). Scheitert B14 im Live-Test, kommt die Ablehnung im selben Teil zurück.
  Nicht in einer laufenden SOLIDWORKS-Sitzung erprobt.
- Blech S1b geprüft (`539a70b`) und nachgeprüft (`8b37462`), Befunde behoben: Der Wächter schreibt nur abweichende
  Standardwerte und Blech-Features zurück und gibt „Blechparameter aus dem Werkstoff“ ihren alten Zustand zurück; die Sitzung
  prüft die abgewickelten Kopien nach dem Abwickeln der Originale noch einmal; Hinweise der Abwicklung für Flächen nur am
  Original; im selben Teil folgt die Kopie mit den geteilten Standardwerten dem Werkstoff (`MaterialNoteFor`).
  STOLPERSTEINE 196, 199–200.
- Blech S2 (Zweig `feat-blech`): Saum und Gehrungslasche samt Bausteinen für alle Laschen und Biegungen. Core:
  `SheetMetalFlangeRecords` (`HemRecord` mit `HemWidthSketch`, `MiterFlangeRecord`), `FlangeProfiles` (`Assign`,
  `IsFullEdgeLine`, `LiesOn`), `MiterRules` (`Variants`, `Marks`, `OffersSwap`, `Differences`, `Parent`), `HemRules` (Werte
  je Art, `Differences`, `Parent`, Entlastung nur ohne Prüfung als Hinweis), `BendOverrides.UnexpectedInCopy`/`CountNote`,
  Breitenskizzen gehören dem Saum (`SheetMetalTree`), `Hem` und `SMMiteredFlange` „nachgebaut“; Tests
  `SheetMetalFlangeTests`, Baum, Ablehnung. Add-In: Leser in `FeatureCapture.SheetMetalFlanges` (Kanten, Werte, Breitenskizzen
  zweimal gelesen und den Kanten zugeordnet, Biegungen nach Definition), `SwSheetMetal.ReadBends`/`ReadHemScalars`/
  `ReadMiterScalars`/`ReadBackHem`/`ReadBackMiter`, `HemBuilder`, `MiterFlangeBuilder`, `FlangeSupport` (Kanten nur auf
  eigenen Körpern, Biegezugabe in eigener Bearbeitung und nach jeder Variante, Biegungen der Kopie), `FeatureEdit.Hem`/
  `MiterFlange`/`Allowance`, `SketchBuilder.ReplaceInto`, Flächenproben auf Unter-Features (`MassCapture`),
  `BuildResult.PassNote` im Prüftext. STOLPERSTEINE 201–210, Prüftabelle (B23–B27).
  Abweichend vom Plan: Die Regeln des Saums stehen in einer eigenen Datei `HemRules` (der Plan nennt nur `FlangeProfiles` und
  `MiterRules`), das Gemeinsame der beiden Builder in `FlangeSupport`. `FeatureEdit.Allowance` ist für Saum und
  Gehrungslasche typisiert und nur für andere Blech-Daten spät gebunden (der Plan: allgemein spät gebunden). „Entlastung:
  Standard (geprüft)“ kommt über ein neues `BuildResult.PassNote` in den Prüftext. `UnsupportedFeatures.Reason` nennt einen
  nachgebauten Blech-Typ, dessen Definition nicht antwortet, „Feature-Daten nicht lesbar“ statt „wird nicht unterstützt“ (auch
  Basislasche und Abwicklung). Der Saum entsteht mit den Entlastungswerten des verifizierten Drittanbieter-Aufrufs
  (`true, 0, 0, false, 0.5, 0, 0`; mit Standard-Entlastung ohne Wirkung). Die Biegereihenfolge wird nur bei je einer Biegung
  verglichen (K25), eigene Werte der Biegungen der Kopie über das, was abweicht, nicht über Namen. `Assign` hat noch keinen
  Beleg über den Skizzenrahmen (kommt mit den Profilen der Kantenlasche in S3). Bei einer einzelnen Kante versucht die
  Gehrungslasche vor der Markierung 0 auch „Kette zuerst“. Nicht in einer laufenden SOLIDWORKS-Sitzung erprobt.
- Blech S2 geprüft, Befunde behoben (`483eb03`): Saumwinkel nur mod 2π verglichen (`ScalarKind.PlainAngle`), Breitenskizzen
  erst gespiegelt, dann geleert (`SketchBuilder.ReplaceInto`, `SketchDamagedException`). Die Nachprüfung fand noch einen Fehler,
  behoben in `71d7c0c`: Lässt sich keine Linie der vorhandenen Breitenskizze löschen, ist das ein gewöhnlicher Fehlschlag
  (`NothingClearedException`), und die Texte nennen eine nur teilweise geleerte Skizze nicht mehr „geleert“. STOLPERSTEINE 170,
  201, 203, 211.
- Blech S3 (Zweig `feat-blech`): Kantenlasche. Core: `EdgeFlangeRecord` mit `FlangeProfile` und den swconst-Zahlen
  (`FlangeOffsetTypes`, `FlangeDimTypes`, `FlangePositionTypes`, `EdgeFlangeOptions`, `ReliefTearTypes`), `FlangeProfiles` (`PredictedAngle`,
  `AngleCandidates`, `CommonCandidate`, `Direction`, `FitsFrame`, `NormalOffset`, `Rebase`, `Merge`, `ProfilePoints`, `InPlaneDeviation`,
  `Check`/`ProfileCheck`, `Assign` auch über den Rahmen), `EdgeFlangeRules` (Wege, Optionen, Zurücklesen nur des Genutzten,
  Richtungssuche, Texte), Profile gehören der Kantenlasche (`SheetMetalTree`, `EdgeFlangeFacts`, A2 P13), `ReliefData.TearType`,
  `EdgeFlange` „nachgebaut“; Tests `EdgeFlangeTests` (A2 #1, #2, #4–#9 und mehr), Ablehnung, Berichtstexte, swconst-Nummern. Add-In:
  `CaptureEdgeFlange` (Kanten, Werte, Bezüge, Profile zweimal gelesen und vereint, Zuordnung, Biegungen),
  `SwSheetMetal.ReadEdgeFlangeScalars`/`ReadBackEdgeFlange`/`SupportEdgeOf`, Baumfakten je Kantenlasche, `EdgeFlangeBuilder`
  (Kandidaten mit Rahmenprüfung, `ReplaceInto`, Datenobjekt/`InsertSheetMetalEdgeFlange2`/Einzelkante mit Array-Formen, Werte, Richtungen
  in eigener Bearbeitung, Profilprüfung mit Richtungssuche, Biegezugabe, Zurücklesen, Biegungen), `FeatureEdit.EdgeFlange`/`AllowanceInto`,
  `FlangeSupport.BodyOf`. STOLPERSTEINE 212–223, Prüftabelle (B28–B35).
  Abweichend vom Plan: Die Regeln der Kantenlasche stehen in einer eigenen Datei `EdgeFlangeRules` (der Plan nennt nur `FlangeProfiles`);
  `Fold` gibt es schon (`SheetMetalCompare.Fold`). `FitsFrame` verlangt statt „n′ ⟂ R(d)“ die Ebene durch Kante und alle Punkte des
  zurückgesetzten, gespiegelten Profils auf `Tol.Length` (strenger; sonst scheiterte `SketchMirror` an einer leicht gekippten Ebene); die
  Parallelität der Ebenen in der Profilprüfung mit `Tol.Angle` (1e-8) statt 1e-9. `Merge` verlangt dieselben Segmente und Punkte an derselben
  Stelle der Skizze (der Plan: dieselben Segmente) und nimmt Beziehungen und Bemaßungen aus der Lesung vor der Lasche. Jedes Profil wird mit
  der Leiste hinter der Skizze gelesen, nicht nur bei Bezügen aufs Modell – die Kante, auf der es angelegt wurde, gibt es nur dort. Die
  Zuordnung über den Rahmen nimmt von parallelen Kanten die nächste (der Entwurf: „die Kante liegt im Versatz der Ebene“, ohne Maß; B31).
  Die Richtungssuche setzt feste Stände (wie der Drittanbieter) und gibt der Prüfung Varianten nur, wenn keiner passt; nach der Biegezugabe
  wird noch einmal geprüft. Der Bezug des Winkels ist entbehrlich (Hinweis, der Winkel als Zahl), nicht wie die Bezüge der Länge ein Grund,
  die Lasche nicht anzulegen. `ISketch.GetReferenceEntity` nimmt in der Interop `ref int` (A2 P15: `out`). Das Tor B29 ist nicht erprobt;
  wie bei S1a sind die Typen trotzdem geöffnet – scheitert B29, kommt `EdgeFlange` auf „noch nicht“ zurück. Nicht in einer laufenden
  SOLIDWORKS-Sitzung erprobt.
- Blech S3 geprüft (5 bestätigte Befunde, behoben in `8a567ee`): Richtungsvarianten suchen die Profilskizzen unter ihren
  registrierten Namen, der Richtungsstand gilt erst nach gelungener Bearbeitung, „senkrecht zur Fläche“ nur mit Bezug des Winkels,
  Messart der Länge auch bei „bis Kante und verschmelzen“. Nachgeprüft (3 bestätigte Befunde, behoben in `5aa8995`): Biegezugabe
  auch nach einem Umschalten, das false meldet; nicht wiederhergestellte Richtungen mit Hinweis
  (`EdgeFlangeRules.DirectionsNotRestored`); ein gesetzter, aber nicht zurückgelesener Bezug des Winkels mit Hinweis
  (`EdgeFlangeRecord.HasAngleReference`, `EdgeFlangeRules.AngleReferenceMissing`). STOLPERSTEINE 208, 215, 221, 222,
  Prüftabelle (B27, B34, B35).
- Blech S4 (Zweig `feat-blech`): skizzierte Biegung und Kröpfung. Core: `SketchedBendRecord` (`SheetMetalBendRecords.cs`, mit
  `FixedPointSpace`), `JogRecord` mit den swconst-Zahlen (`JogOffsetTypes`, `JogDimensionPositions`, `JogPositions`), `BendSide` (Bereiche,
  Wahlpunkte je Bereich, `PointBeside`, `PickOrder`, `ChooseFixedPoint`, Abstand), `SheetMetalBendRules` (Code der Biegeposition, erste
  Vermutung der Richtung, Biegelinien, Zeugenauswahl, Vergleich der Varianten, Zurücklesen, Texte), `JogRules` (Codes, Grad, Versatz beim
  Anlegen, eine Linie, Zurücklesen, Texte), `Jog` und `SM3dBend` „nachgebaut“; Tests `SheetMetalBendTests` (A3 #1–#3, #14 und mehr),
  `BendSideTests` (A3 #4–#6, A2 #11/#12), Ablehnung, Berichtstexte, swconst-Nummern. Add-In: `CaptureSupportFace` und `CaptureWitnesses`
  (`FeatureCapture`), Leser in `FeatureCapture.SheetMetalBends` und `.SheetMetalFlanges`, `SwSheetMetal.ReadSketchedBendScalars`/
  `ReadJogScalars`/`ReadBack…`, `FeatureEdit.SketchedBend`/`Jog` (Biegezugabe typisiert), `BuildContext.SketchSupportFaces`/`NoteSupportFaces`,
  `SketchBuilder.Build` auf der Fläche (`SketchMap.OnSupportFace`, `SupportFaceMissed`), `BuilderSupport.SelectFaceByRay`, `SwShape.Misses`,
  `BendSupport` (feste Fläche, Suche), `SketchedBendBuilder`, `JogBuilder`. STOLPERSTEINE 224–230, Prüftabelle (B36–B43).
  Abweichend vom Plan: Zeugen sind nach den Flächen des Features und seiner Biegungen die Flächen seines Körpers, die es gemacht oder bewegt
  hat (Fingerabdruck davor nicht vorhanden); die um mehr als 1° gedrehten nur, wenn die Flächen davor nicht lesbar sind. Der Treffpunkt der
  Strahlauswahl darf 10 µm vom Wahlpunkt liegen (der Plan: `Tol.Length`). `SideCandidates` sucht auf einem Gitter um den Flächenpunkt mit
  Punkten neben jeder Linie, bevorzugt bis zu einem Viertel der Fläche Abstand und fragt den Kern (`onFace`), statt nur Punkte neben der
  Linienmitte zu nehmen; die Reihenfolge der Punkte (`PickOrder`) steht in Core. `FixedPointSpace` ist eine Aufzählung in den Datensätzen,
  die Wahl `BendSide.ChooseFixedPoint`. Die Kröpfung sucht nach `ReverseDirection` auch `ReverseOffset` ab (der Plan: unverändert, nur als
  Variante); die Variante „feste Seite gewechselt“ gibt es nicht – die Zeugen entscheiden die Seite, ohne sie sagt es ein Hinweis; `FixedPoint`
  wird nicht gesetzt. Ohne Zeugen gibt es den Hinweis nur, wenn keine Prüfung die Richtung bestätigen kann (sonst Prüfvariante und Hinweis
  bei Abweichung, wie die Entlastung des Saums). `ctx.Bends` kommt mit S5 (K25). Die Dicke für den Abstand liest die Erfassung vom
  Blech-Feature des Körpers. `SketchBend` bleibt wie `OneBend` ein Teil „noch nicht“ (nie gesammelt). Das Tor B36 ist nicht erprobt; wie
  bei S1a und S3 sind die Typen trotzdem geöffnet – scheitert B36, wird die Kröpfung „nicht unterstützt“. Nicht in einer laufenden
  SOLIDWORKS-Sitzung erprobt.
- Blech S4 geprüft (3 bestätigte Befunde, behoben in `ece2fa1`): der gespiegelte feste Punkt ist nur Zeuge, wenn er am Original
  nach dem Feature noch auf einer Fläche seines Körpers liegt (`FixedPointStays`); Zeugen getrennt in eigene und vom Feature bewegte
  Flächen anderer Features (`MovedWitnesses`, `BodyFeatures`, `SheetMetalBendRules.WitnessesFor`); die behaltene Variante wird nach
  der Biegezugabe noch einmal gemessen (`SheetMetalBendRules.WitnessOutcome`). Nachgeprüft (3 bestätigte Befunde, behoben in
  `88b232f`): bewegte Zeugen zählen nur, wenn jedes frühere Feature des Körpers in der Kopie ohne Fehler nachgebaut ist
  (`BendSupport.Rebuilt`), und nicht, wenn die früheren Features nicht lesbar waren (`OwnWitnessesOnlyFor`). STOLPERSTEINE 225,
  228, 231, Prüftabelle.
- Blech S5 (Zweig `feat-blech`): geschlossene und gebrochene Ecke, Abwickeln und Zurückbiegen, Querbruch, Eckentlastung. Core:
  `ClosedCornerRecord`, `BreakCornerRecord`, `FoldRecord` mit `BendRef`, `CrossBreakRecord`, `CornerReliefRecord`/`CornerReliefItem` und die
  swconst-Zahlen (`ClosedCornerTypes`, `BreakCornerTypes`, `CornerReliefTypes`, `CornerReliefBendTypes`) in `SheetMetalBendRecords.cs`,
  `CornerRules` (Auswahlarten der geschlossenen Ecke, Überlappung ↔ Unterlappung, Rücklese-Vergleiche, Bruchlinien, Längen des älteren
  Eckentlastungs-Aufrufs, Ecken paaren, Texte), `FoldRules` (Auswahlarten, einzige Biegung des Besitzers, Texte), `SetMatch`/`FaceSets`/
  `BendMatcher` (Mengen, volle Treffer), die Bruchlinien-Skizze gehört dem Querbruch (`PartOwnerKind.CrossBreak`), `UnFold`, `Fold`,
  `BreakCorner`, `CrossBreak`, `CornerFeat` „nachgebaut“; Tests `SheetMetalCornerTests` (A3 #7–#10, #12, #13, #15 und mehr), Ablehnung,
  Berichtstexte, swconst-Nummern. Add-In: Leser in `FeatureCapture.SheetMetalCorners` (Eckentlastung spät gebunden über `SwCornerRelief`, auch
  unter einem Namen außerhalb der Tabelle), `SwSheetMetal.Read…Scalars`/`ReadBack…` (Mengenvergleich unter dem `AccessSelections` der Kopie),
  `NewOfType`, `AllBends`/`BendsOf`, `FeatureEdit.ClosedCorner`/`BreakCorner`/`CrossBreak`/`Folds`, `BuildContext.BendFor`/`RegisterBend`,
  `CornerSupport` (Bezüge vorab auf eigenen Körpern, `FindBend`), `ClosedCornerBuilder`, `BreakCornerBuilder`, `FoldBuilder`,
  `CrossBreakBuilder`, `CornerReliefBuilder`. STOLPERSTEINE 232–236, Prüftabelle (B44–B49).
  Abweichend vom Plan: Die Regeln stehen in eigenen Dateien `CornerRules` und `FoldRules` (der Plan nennt `SheetMetalBendRules` für
  `SwapOverlap`/`LegacyReliefLengths`), der Mengenvergleich in `SetMatch`/`FaceSets` neben `BendMatcher`. `ctx.Bends` heißt
  `BendFor`/`RegisterBend` und wird nur von `FindBend` gefüllt: Die Builder der Laschen tragen ihre einzige Biegung nicht selbst ein (K25),
  das Nachschlagen über das Gegenstück eines Besitzers mit einer Biegung leistet dasselbe, wenn es gebraucht wird. Die Eckentlastung steht
  ohne bestätigten Namen nicht in der Typtabelle (§1.5, B44; der Plan zählt `CornerRelief` unter den geöffneten Typen): Sie wird über ihre Daten
  erkannt, der Name `CornerRelief` bekommt nur den Text für ältere Versionen. Kein `FeatureEdit.Late`: Die Eckentlastung wird nicht bearbeitet,
  Abweichungen sind Hinweise. Der Radius einer kreisförmigen Eckentlastung im älteren Aufruf ist als Schlitzbreite angenommen (laut Hilfe die radiale Schlitzbreite; B49). Weichen die
  Bruchlinien der Kopie von den gespiegelten ab, werden sie in die neue Skizze geschrieben (A3 P10 nennt nur einen Hinweis); ein Querbruch mit
  mehr als einer Skizze wird nicht nachgebaut. Abwickeln/Zurückbiegen versuchen vor jedem Auswahlwechsel eine Bearbeitung, die feste Fläche und
  Biegungen setzt; „sein Abwickeln“ ist das letzte davor in Erstellungsreihenfolge. Eine gebrochene Ecke, die zurückgelesen auf anderen
  Elementen sitzt, ist ein Hinweis, kein Fehlschlag. Keine Tore (§5.2); nicht in einer laufenden SOLIDWORKS-Sitzung erprobt.

- Blech S5 geprüft (Befunde behoben in `e402cf0`: `GetCornerAtIndex` zählt ab 1, Radius einer kreisförmigen Eckentlastung im
  älteren Aufruf ist die Schlitzbreite, der ältere Aufruf schließt die offene Eckenmenge in jedem Fall mit `FinishCornerRelief`, nicht
  gesetzte Werte einer Ecke nur dann als Hinweis, wenn das Datenobjekt die Eckentlastung angelegt hat) und nachgeprüft (behoben in
  `5dc7b0c`: Grund, wenn `CreateFeature` ohne Fehler nichts anlegt; der Körper für den zweiten älteren Aufruf wird neu gesucht).
  STOLPERSTEINE 151, 235, Prüftabelle (B49).
- Blech S6 (Zweig `feat-blech`): normaler Schnitt und „Biegungen einfügen“ alter Art. Core: `NormalCutRecord` (mit
  `NormalCutParameterTypes`) und `LegacyBendsRecord` in `SheetMetalRecords.cs`, `NormalCutRules` (Gruppen Fläche für Fläche gespiegelt, als
  Mengen von Mengen verglichen, Wege, Werte, Texte), `SheetMetalRules` (`LegacyArguments`, `LegacyPlaceholder`, `LegacyOffsetRatio`,
  `LegacyCreationValues`, `LegacyOverrides`, `LegacyBendsDifferences`, Texte), `SheetMetalTree` (das `SheetMetal` ohne Basislasche, dem seine
  `FlattenBends`/`ProcessBends` folgen, ist ihr Besitzer – `PartOwnerKind.LegacyBends`, `LegacyAnchors`, `LegacyParts`, `BetweenLegacyBends`,
  `Closure` in beide Richtungen, eine Abwicklung seines Körpers gehört ihm), `SheetMetalTypes` (`NormalCut`, `FlattenBends`, `ProcessBends`
  „nachgebaut“, `LegacyAnchor`), `UnsupportedFeatures.RefusalBeforeCapture` mit den Namen aus dem Baum, `SheetMetalPlanner` (Körper ohne
  seine „Biegungen einfügen“, Grund eines nicht lesbaren Besitzers), `StaysSingleBody` zählt „Biegungen einfügen“ mit; Tests `NormalCutTests`
  (`Normal_cut_groups_mirror_face_by_face` und mehr), `LegacyBendsTests` (A1 #9 und mehr), `SheetMetalTreeTests`
  (`Flatten_and_process_bends_belong_to_the_legacy_sheet_metal`), Ablehnung, Berichtstexte, Besitz, swconst-Nummern. Add-In: Leser in
  `FeatureCapture.SheetMetalLegacy` (der normale Schnitt unter `AccessSelections`, „Biegungen einfügen“ nach Typname und Baum),
  `SwSheetMetalLegacy` (`ReadNormalCutScalars`, `NormalCutGroups`, `ReadBackNormalCut`, `ReadLegacyBends`, `SameLegacyFixedFace`, `InsertBends`,
  `IsFlattened`/`SetFlattened`), `FeatureEdit.NormalCut`/`Bends`, `NormalCutBuilder`, `LegacyBendsBuilder`, `SheetMetalSession` biegt
  unterdrückte `ProcessBends` für den Lauf zurück und gibt den Kopien den Zustand, `MassCapture` nimmt Flächenproben hinter `ProcessBends`,
  `StepRunner` setzt die wartenden Werte des ersten Blechkörpers auch nach „Biegungen einfügen“, `MirrorJob` (Namen aus dem Baum an die
  Ablehnung, Zustand der Kopien, geteilte Standardwerte). STOLPERSTEINE 151, 164, 183 ergänzt, 237–240 neu, Prüftabelle (B51, B52).
  Abweichend vom Plan: Der Plan öffnet „`SheetMetal` (legacy owner)“; ein Typname kann nicht zugleich „noch nicht“ (ohne Besitzer) und
  „nachgebaut“ sein – der Baum erkennt das `SheetMetal` alter Art, `RefusalBeforeCapture` bekommt seine Namen (neuer, optionaler Parameter) und
  behandelt es als Träger „nachgebaut“ (`SheetMetalTypes.LegacyAnchor`), jedes andere `SheetMetal` ohne Basislasche bleibt „noch nicht“, ebenso
  „Biegungen einfügen“ ohne `FlattenBends`/`ProcessBends`. Features zwischen `FlattenBends` und `ProcessBends` (im abgewickelten Zustand
  angelegt) kann die Kopie nicht dazwischen setzen: solche „Biegungen einfügen“ werden nicht nachgebaut, der Lauf wird abgelehnt (im Plan nicht
  bedacht). Die Werte folgen nicht dem Entwurf (Argumente, dann `ApplyAndCompare` nur für Biegeabzug und Biegeberechnung), sondern dem
  Modell der Basislasche (erster Körper legt die Werte des Teils an, weitere über Überschreiben-Flags – `LegacyOverrides` wie
  `SamePartOverrides` –, die des ersten warten auf den zweiten Körper); nicht nehmbare Biegezugaben bekommen einen K-Faktor als Platzhalter.
  Eigene Werte von `FlattenBends`/`ProcessBends` werden gesetzt, die feste Fläche hat eine dritte Quelle (Standardwerte, nur mit einem
  Blechkörper), eine Abwicklung eines Körpers alter Art gehört seinem `SheetMetal`. Für die veralteten Aufrufe des normalen Schnitts
  nummeriert die Hilfe eine Reihenfolge (`AddSMNormalCut` vor jeder Auswahl, Flächen mit 1, „die Gruppe“ mit 2); der erste Versuch weicht
  bewusst ab (`AddSMNormalCutType`, je Gruppe Flächen und `AddSMNormalCut`, Bezug, `FinishSMNormalCut`), die der Hilfe folgt danach (B51); die
  Richtung ist entbehrlich (Hinweis), die Versatzebene nicht. Die Regeln des normalen Schnitts stehen in einer eigenen Datei `NormalCutRules`,
  A1 #9 in `LegacyBendsTests` (der Plan: `SheetMetalTests`). Das Beispiel der Ablehnung in README und STOLPERSTEINE 151 ist jetzt ein
  Umformwerkzeug. Keine Tore (§5.2); nicht in einer laufenden SOLIDWORKS-Sitzung erprobt.

- Blech S6 geprüft, Befunde behoben (Commit „Blech S6: Befunde aus der Prüfung behoben“): Die Sitzung biegt ein abgewickeltes
  `ProcessBends` mit seinen abhängigen Features zurück (`SetSuppressed(…, withDependents)`, nach dem schlichten Aufheben
  `swUnSuppressDependent`) – was nach ihm auf den gebogenen Flächen entstand, blieb sonst unterdrückt und wurde nicht gespiegelt –, und
  abgewickelt ist ein `ProcessBends` nur, wenn seine Elternteile (`FlattenBends`, `SheetMetal`; sonst die davor in der Baumreihenfolge)
  nicht unterdrückt sind („Biegungen einfügen“ als Ganzes unterdrückt bleibt unangetastet). Der normale Schnitt versucht die veralteten
  Aufrufe auch in der nummerierten Reihenfolge der Hilfe (`NormalCutPath.ObsoleteHelpOrder`, Markierung 2 angenommen für die übrigen
  Flächen einer Gruppe, `HelpOrderMarks`), schließt einen abgebrochenen veralteten Versuch mit `FinishSMNormalCut` und nennt nicht lesbare
  Gruppen beim Namen (`NormalCutRules.ReadBackFailure`; die geschlossene Ecke ebenso). Die Information zur nachträglich gesetzten Biegezugabe
  nennt die gesetzte, bei einem späteren Körper mit Platzhalter wegen der Standardwerte des Ziels beide (`LegacyTargetAllowanceLater`).
  STOLPERSTEINE 237, 239, 240, Prüftabelle (B51, abgewickelt alter Art), Testteil S6.
- Blech S6 nachgeprüft, Befunde behoben (Commit „Blech S6: Befunde aus der Nachprüfung behoben“): Was ein `ProcessBends` beim
  Zurückbiegen mit seinen abhängigen Features zurückbrachte, merkt sich die Sitzung (`SheetMetalRules.BroughtBack`) und unterdrückt die
  Kopien davon nach dem Abwickeln der Kopie einzeln – sie liegen auf Ebenen und sind keine Kinder des kopierten `ProcessBends`, das
  Unterdrücken nahm sie dort nicht mit. Die `ProcessBends` werden vor den Abwicklungen zurückgebogen, die Abwicklungen danach nach ihrem
  dann gültigen Zustand gefaltet (eine gefaltete kam womöglich mit zurück und hätte das Teil für den Lauf abgewickelt). Als Elternteile
  eines `ProcessBends` zählen nur `FlattenBends` und `SheetMetal` (eine mitgenannte Skizze ist mit ihm unterdrückt). Ist `FlattenBends`
  unterdrückt, `SheetMetal` aber nicht, kommt der Datensatz ohne die Biegungs-Features aus (`SheetMetalRules.LegacyPartsInRun`): Die Kopie
  bekommt wie das Original nur das `SheetMetal` (`InsertBends2` ohne „abwickeln“), statt gebogen zu entstehen. Tests in `LegacyBendsTests`;
  STOLPERSTEINE 239, Prüftabelle (B52, abgewickelt alter Art), Testteil S6, README, ARCHITEKTUR.

## In Arbeit

| Thema | Stand |
| --- | --- |
| Blech | nach `blech-plan.md` in prüfbaren Schritten; S0 bis S5 stehen und sind geprüft und nachgeprüft (S0 bis S4 im Hauptzweig); S6 (normaler Schnitt, „Biegungen einfügen“ alter Art) ist umgesetzt und geprüft, die Nachprüfung der Korrekturen steht aus; S7a (Ersatzweg) wartet auf die Entscheidungen des Eigentümers (`blech-plan.md` §7); bis ein Typ seinen Schritt hat, lehnt `UnsupportedFeatures.RefusalBeforeCapture` den Lauf ab (`SheetMetalTypes`), einen Blechkörper ohne nachgebauten Träger `SheetMetalPlanner.Refusal` |

## Reihenfolge beim Fortsetzen

1. Blech S6: die Korrekturen der Prüfung nachprüfen. S7a (Ersatzweg) erst nach den Entscheidungen des
   Eigentümers in `blech-plan.md` §7 (Ersatzweg überhaupt, Umgang mit gescheiterten Blättern, „Original löschen“), bei
   einem Nein stattdessen S7′; S8 (Versteifungsrippe, Lasche und Schlitz) und S9 (Umformwerkzeuge) hängen nicht davon ab
   und können vorher kommen, ebenso mit Core-Tests, Prüfung, Gegenprüfung und Nachprüfung der Korrekturen.
2. Erster Test in einer laufenden SOLIDWORKS-Sitzung: die Annahmen unter „Was beim ersten Test in SOLIDWORKS zu prüfen ist“
   (STOLPERSTEINE) und `blech-plan.md` §5.2.
3. Doku, Version, Regressionsprüfung, Release (Tag nur nach Rückfrage).
