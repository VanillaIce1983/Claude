# Zwischenstand (15.11.2026)

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

- Blech S8 (Zweig `feat-blech`, vor S7, das auf den Live-Test B57 wartet): Versteifungsrippe und Lasche und Schlitz. Core:
  `SheetMetalArea4Records.cs` (`TabAndSlotRecord` mit `TabSlotGroup`, `SheetMetalGussetRecord` mit `GussetAnchor`/`GussetSection`, die
  swconst-Zahlen `TabSlotHeightTypes`, `TabSlotSpacingTypes`, `TabEdgesTypes`, `GussetProfileDimTypes`, `GussetTypes`, `GussetProfileTypes`),
  `TabSlotRules` (D1/D2 getauscht nur ohne Punkte und mit umgekehrter Kante, `IsSlotName`, Rücklesen nur der benutzten Werte, Texte),
  `GussetRules` (`Section`, `Ends`, `AnchorOf`, `Variants` samt L − d, `FlipOffsetFor`, Rücklesen, Texte), der Schlitz gehört seiner Lasche
  (`PartOwnerKind.TabAndSlot`, `SheetMetalTree.SlotOf`, `Closure` in beide Richtungen, `MeasureUntil` bis zum Schlitz), `SMGusset` „nachgebaut“;
  Tests `TabSlotRulesTests`, `GussetRulesTests` (A4 §13, checks.py #4), Ablehnung, Berichtstexte, Besitz, swconst-Nummern. Add-In: Leser in
  `FeatureCapture.TabSlotGusset` (Gruppen unter `AccessSelections`, externe Bezüge abgelehnt; Schenkel, Bezugslinie und -punkt, Biegung unter den
  Schenkeln, Schnitt mit der Leiste nach der Rippe, frühere Rippen auf derselben Biegung), `SwSheetMetalArea4` (Leser, Schreiber, Zurücklesen,
  `BendOf`, `FacePoints`, `IsTabAndSlot`), Fakten für den Baum in `SwSheetMetal.Tree`, `FeatureEdit.TabAndSlot`/`Gusset`, `TabAndSlotBuilder`,
  `SheetMetalGussetBuilder`, Spiegel-Features über Lasche und Schlitz abgelehnt. STOLPERSTEINE 151, 165 ergänzt, 241–245 neu, Prüftabelle (B64–B66),
  Testteil S8.
  Abweichend vom Plan: Lasche und Schlitz steht ohne bestätigten Namen nicht in der Typtabelle (§1.5; der Plan: „by its confirmed name“) – erkannt an
  `TabAndSlotFeatureData`, im allgemeinen Erfassen wie die Eckentlastung und im Baum, für den `SwSheetMetal.Tree` jedes Feature der obersten Ebene
  fragt, das keine Skizze, kein Bezug, kein Ordner und kein bekannter Blech-Typ ist. Der Schlitz wird über das Kind der Lasche oder einen Namen mit
  anderem Wort gefunden (sprachunabhängig, nicht nur „-Slot“); antwortet ein nicht erkannter Schlitz mit denselben Kanten und Flächen wie eine
  erfasste Lasche und Schlitz, wird er nicht nachgebaut. Lasche und Schlitz entsteht in zwei Versuchen (alle Werte im Datenobjekt, dann wie im
  Beispiel der Hilfe), danach eine Bearbeitung nur, wo etwas anders zurückkommt oder Punkte und Laschenfläche zu setzen sind. Der Hinweis auf die
  Schlitz-Optionen kommt wie beim Saum nur, wo die Prüfung sie nicht bestätigen kann (P12: immer). Der Schnitt einer Versteifungsrippe ist die Mitte
  der Ausdehnung ihrer Flächen entlang der Biegelinie (A4 §7: Schwerpunkt der Flächen; angenommen ist, dass die Rippe symmetrisch zu ihrer
  Schnittebene steht, B65); als Bezug einer früheren Rippe zählen auch ihre Seitenflächen (Schnitt ± halbe Breite, ggf. zuzüglich der Dicke). Die Lageprüfung legt jede Variante
  neu an (gespiegeltes Biegungsende beidseitig, ohne Punkt, L − d, andere Seite) und behält die erste passende; passt keine, bleibt die nächste mit
  Hinweis, statt dass der Schritt scheitert. Der Aufruf mit 21 Werten nimmt keinen Bezugspunkt, also wird er immer mit Versatz angelegt und der Punkt
  danach gesetzt. Ein Referenzpunkt-Feature als Bezugspunkt wird nicht übertragen. `FormToolInstance` bleibt „noch nicht“ (der Plan: ein nicht
  unterstütztes Blatt wie ab S7a) – S7a fehlt noch. Spiegel-Features mit Lasche und Schlitz in der Saat werden wie andere Blech-Saaten abgelehnt.
  Keine Tore (§5.2); nicht in einer laufenden SOLIDWORKS-Sitzung erprobt.
  Was S7a für diese Typen noch ergänzen muss (bis dahin gilt die Ablehnung aus S0–S6; S8 erfindet keine Auslöser): `SMGusset` bleibt
  „nachgebaut“ – die Typtabelle von S7a (Plan §3.0/§3.9: „nicht unterstützt“, Blatt) gilt für die Rippe nicht mehr. Eine Rippe, die sich nicht
  erfassen lässt (eigenes Profil, B66; ein Referenzpunkt-Feature als Bezugspunkt; nicht lesbar), ist heute ein gewöhnlicher Eintrag
  „fehlgeschlagen“; mit S7a wird sie bei eingeschaltetem Ersatzweg ein Auslöser (`TriggersFallback` ist für sie schon wahr). Eine Rippe, die erst
  beim Aufbau scheitert, ist Sache von S7b (Neustart); ob eine Rippe, deren Lage keine Variante trifft (heute: die nächste mit Hinweis), dann den
  Ersatzweg auslösen soll, entscheidet S7a. Lasche und Schlitz: Der Planer braucht die Körpergeschichten aller Körper (A4 P2) – Lasche und Schlitz
  verbindet stets zwei Körper; ein Körper, in dessen Geschichte bis zum Schnappschuss sie mit einem anderen geteilt ist, bekommt keinen Plan;
  deckt ein Plan die Lasche, deckt er auch ihren Schlitz (`SheetMetalTree.SlotOf`, ein `CoveredByFallbackRecord` für beide). Ein nicht erkannter
  Schlitz mit denselben Gruppen löst nichts aus (`TriggersFallback` falsch); ein Lasche und Schlitz, das sich nicht erfassen lässt (externe
  Bezüge, nicht lesbar), ist ohne bestätigten Namen ein allgemeiner Eintrag und löst nach K17 aus. `FormToolInstance` (falls S9 nicht vorher
  kommt) und `Rip` setzt S7a auf „nicht unterstützt“ (Blatt, Auslöser); die Träger bleiben abgelehnt, solange der Schalter aus ist.
- Blech S8 geprüft, Befunde behoben (Commit „Blech S8: Befunde aus der Prüfung behoben“): Lasche und Schlitz gibt es auch zwischen
  gewöhnlichen Volumenkörpern (Schweißblechen); `SwSheetMetal.Tree` baut den Baum dann auch in einem Teil ohne Blech, nur für Lasche und
  Schlitz, und `MirrorJob` liest ihn unabhängig von der Blech-Sitzung – sonst lief der Schlitz dort als eigenes, nicht unterstütztes Feature
  mit. Ein Teil nach seinem Besitzer (Schlitz, Biegungs-Features alter „Biegungen einfügen“), dessen Besitzer beim Aufbau scheiterte, heißt
  „nicht entstanden“ statt „entsteht mit diesem“ (`BuildContext.OwnersNotBuilt`, `RunSteps.PartOf`). Ein Schlitz, den erst die gleichen
  Gruppen verraten, wird der Schlitz seiner Lasche (`SlotFeatureName`): gemessen bis zu ihm, Begleiter unter seinem Namen. Seitenflächen
  einer früheren Versteifungsrippe liegen bei Schnitt ± halber Breite (`IndentWidth`) oder ± halber Breite plus Dicke, nicht bei ± halber
  Dicke (`GussetSection.Width`). Tests in `TabSlotRulesTests`, `GussetRulesTests`, `OwnershipTests`; STOLPERSTEINE 80, 241, 244,
  Prüftabelle (B64, B65), Testteil S8, README, ARCHITEKTUR.
- Blech S8 nachgeprüft, Befunde behoben (Commit „Blech S8: Befunde aus der Nachprüfung behoben“): Ein Schlitz, den erst die gleichen
  Gruppen verraten, war zwar der Schlitz seiner Lasche, blieb aber ein nicht unterstützter Eintrag – nach einer gescheiterten Lasche hieß er
  weiter „entsteht mit ihr“, nach einer gebauten zählte er als nicht gespiegelt und hielt „Original löschen“ auf. Jetzt wird er ein Teil
  der Lasche wie ein Schlitz, den der Baum findet (`TabSlotRules.SlotBySameGroups`, `PartOwnerKind.TabAndSlot`, mit Hinweis
  `SlotNotInTree`): „entsteht mit diesem“ oder „nicht entstanden“ (`OwnersNotBuilt`). Hat die Lasche schon einen Schlitz, bleibt er
  nicht unterstützt (`SameAsEarlier` nennt ihn). Tests in `TabSlotRulesTests`; STOLPERSTEINE 241.
- Blech S9 (Zweig `feat-blech`, wie S8 vor S7, das auf den Live-Test B57 wartet): Umformwerkzeuge. Core: `FormToolRecord` (ein
  `PositionedFeatureRecord`, mit `ToolSource`) in `SheetMetalArea4Records.cs`, `FormToolPlacement` (Drehregel θ′ = β − θ − 2α, `Register3D`:
  Drehung über Formpunkte – Raster 0,5°, Kandidaten über die Punkte fern der Achse, dann alle, verfeinert, kleinster |ψ| –, `Register2D` für erste
  Schätzungen aus den Orientierungsskizzen, `ProtrusionSide`, `PickSamples`, Drehsinn, Konfigurationen, Werkzeugname und -pfad, Texte), die Skizzen
  gehören dem Werkzeug (`SheetMetalTree`, `PartOwnerKind.FormTool`), `FormToolInstance` „nachgebaut“; Tests `FormToolPlacementTests` (A4 §13,
  checks.py #1 und #2), Ablehnung, Berichtstexte, Besitz, Baum, swconst-Nummern. Add-In: `FeatureCapture.FormTool` (nach dem Typnamen vor der
  Definition; Positionsskizze ganz, Orientierungsskizze als Punkte, Werkzeugdatei aus den Bezügen, den Daten oder der Konstruktionsbibliothek,
  Platzierungsfläche und Dicke mit der Leiste hinter der Positionsskizze, Formpunkte und Seite mit der Leiste hinter dem Werkzeug), `SwFormTool`,
  `SketchBuilder.BuildOnFace`, `FormToolBuilder` (Positionsskizze auf der Fläche gewählt, sonst Fläche und Skizze nachträglich; Probe, Seite,
  Drehung, neu eingesetzt und geprüft; Konfigurationen der Datei; jeder Versuch entfernt), `BuildShared.FormToolSense`, Positionsskizzen als
  Punktskizzen und mit Stützfläche, `CaptureOnItsOwn` mit der Positionsskizze eines Umformwerkzeugs. STOLPERSTEINE 151 und 164 ergänzt,
  246–251 neu, Prüftabelle (B67–B70), Testteil S9.
  Abweichend vom Plan: Verknüpft ist die Kopie, wie die Daten sagen, sonst wenn das Original das Werkzeug als Bezug nennt (B70 offen, ob
  auch nicht verknüpfte es tun; der Bericht sagt, dass es geschlossen ist). Formpunkte sind ein Punkt je Fläche und Anfang, Mitte, Ende
  ihrer Kanten (der Entwurf: zusätzlich Punkte über `Surface.Evaluate`); gemessen wird je Instanz nur gegen die Flächen nahe ihrem Einfügepunkt.
  Variante B des Entwurfs (Verschieben mit `MoveOrCopy`) entfällt: die gewählte Skizze, sonst Variante A. Die Skizze auf der Fläche geht über den
  gemeinsamen Weg (K7), aber ohne Rückfall auf eine Ebene (`BuildOnFace`). Ab 2025 SP2 wird am Feature gedreht (`RotationAngle` +
  `ModifyDefinition`), angelegt wird in jeder Version mit `InsertFormToolFeature` (kein `CreateDefinition(73)`); die Variante „Drehung +180°“ für die
  Prüfung gibt es nicht (die eigene Prüfung entscheidet immer). Ein Werkzeug ohne lesbare Formpunkte wird nicht nachgebaut (sonst wäre die Drehung
  ungeprüft). Die Probe ist immer „nicht umgekehrt“ (die Richtung des Originals ist in keiner Version lesbar), die Konfigurationen sind auf 8
  begrenzt, der Drehsinn gilt für den Lauf (`BuildShared`). `FeatureCapture` bekommt die Anwendung (`SldWorks`) für die Ordner der
  Konstruktionsbibliothek. Das Tor B67 ist nicht erprobt; wie bei S1a, S3 und S4 ist der Typ trotzdem geöffnet – scheitert B67, bleibt Variante A.
  Keine Tore sonst (§5.2); nicht in einer laufenden SOLIDWORKS-Sitzung erprobt.
  Was S7a für Umformwerkzeuge ergänzen muss: `FormToolInstance` bleibt „nachgebaut“ (die Typtabelle von S7a, Plan §3.9: „nicht unterstützt“, Blatt,
  gilt nicht mehr). Ein Umformwerkzeug, das sich nicht erfassen lässt (Werkzeugdatei nirgends, Form oder Platzierungsfläche nicht lesbar), ist ein
  gewöhnlicher Eintrag „fehlgeschlagen“ mit `TriggersFallback` (wahr) – mit S7a ein Auslöser auf einem qualifizierten Körper; eines, das erst beim
  Aufbau scheitert („Werkzeug passt nicht“, unsymmetrisches Werkzeug), ist Sache von S7b (Neustart).
- Blech S9 geprüft, Befunde behoben (Commit „Blech S9: Befunde aus der Prüfung behoben“): Ab 2025 SP2 fiel ein am Feature gedrehtes
  Werkzeug, das in keinem Drehsinn passte, nie auf das Neueinsetzen zurück (B69: Einheit und Wirkung von `RotationAngle` offen) – jetzt
  wird es in diesen Drehsinnen neu eingesetzt, und passt es so, dreht der Lauf kein Werkzeug mehr am Feature
  (`BuildShared.FormToolNoInPlace`, Information `TurnedAnew`; die Versuche am Feature heißen „am Feature um … gedreht“). Setzt SOLIDWORKS
  das gedrehte Werkzeug in einem Drehsinn nicht ein, kommt der andere trotzdem an die Reihe (`TurnedNotInserted`), statt die
  Konfiguration aufzugeben. Die Verknüpfung, vor 2025 SP2 aus dem Bezug geschlossen, heißt im Bericht so (`LinkInferred`,
  `FormToolPlacement.Link`). Die Anzeige in der Abwicklung wird ab 2025 SP2 gelesen (`OverrideSettings`, `ShowPunch`, `ShowProfile`,
  `ShowCenter`, spät gebunden – die Hilfe belegt sie, die frühere Abweichung „kein lesendes Mitglied“ war falsch) und an
  `InsertFormToolFeature` gegeben; die Information „Einstellung des Dokuments“ nur noch, wo sie nicht lesbar ist
  (`FormToolPlacement.Display`). Tests in `FormToolPlacementTests`; STOLPERSTEINE 246, 247, 249, 250, Prüftabelle (B67–B70), Testteil
  S9, README, ARCHITEKTUR.

## In Arbeit

| Thema | Stand |
| --- | --- |
| Blech | nach `blech-plan.md` in prüfbaren Schritten; S0 bis S6 stehen, sind geprüft und nachgeprüft und im Hauptzweig; S7 (Ersatzweg): das Tor B54/B55 ist auf SOLIDWORKS 2026 bestanden (`livetests.md`), die Entscheidungen des Eigentümers (`blech-plan.md` §7) gelten vorläufig nach Empfehlung – Ersatzweg ja, hinter einem Schalter, der aus ist; ein gescheitertes Blatt wird gemeldet und der Rest gebaut; „Original löschen“ bleibt bei Blech abgelehnt –, als Nächstes der Live-Test B57 (Umwandeln in Blech, Makro `scripts/livetests/ersatzweg-b57.bas`), der den Weg von S7a bestimmt; bis dahin ist S8 (Versteifungsrippe, Lasche und Schlitz) gebaut, geprüft und nachgeprüft (Befunde behoben) und S9 (Umformwerkzeuge) gebaut und geprüft (Befunde behoben), die Nachprüfung steht aus; bis ein Typ seinen Schritt hat, lehnt `UnsupportedFeatures.RefusalBeforeCapture` den Lauf ab (`SheetMetalTypes`), einen Blechkörper ohne nachgebauten Träger `SheetMetalPlanner.Refusal` |

## Reihenfolge beim Fortsetzen

1. Live-Test B57 (und B59) beim Eigentümer: Umwandeln bzw. „Biegungen einfügen“ des gespiegelten Körpers. Sein Ergebnis wählt
   den Weg von S7a (A: Umwandeln, B: „Biegungen einfügen“, sonst nur F3 – dann ist S7 neu abzuwägen). Danach S7a und S7b nach
   `blech-plan.md` mit den Folgen aus `livetests.md` (nur die direkte Spiegelmatrix, jede Transformation an Prüfpunkten bestätigen).
   S8 (Versteifungsrippe, Lasche und Schlitz) steht, ist geprüft und nachgeprüft; S9 (Umformwerkzeuge) steht und ist geprüft
   (Befunde behoben), die Nachprüfung der Korrekturen steht aus; jeder Schritt mit Core-Tests, Prüfung, Gegenprüfung und Nachprüfung der Korrekturen.
2. Erster Test in einer laufenden SOLIDWORKS-Sitzung: die Annahmen unter „Was beim ersten Test in SOLIDWORKS zu prüfen ist“
   (STOLPERSTEINE) und `blech-plan.md` §5.2.
3. Doku, Version, Regressionsprüfung, Release (Tag nur nach Rückfrage).
