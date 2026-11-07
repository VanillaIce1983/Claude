# Zwischenstand (7.11.2026)

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

## In Arbeit

| Thema | Stand |
| --- | --- |
| Blech | nach `blech-plan.md` in prüfbaren Schritten; S0 bis S3 stehen und sind geprüft, S4 (skizzierte Biegung, Kröpfung) steht (Zweig `feat-blech`) und ist noch nicht geprüft, als Nächstes seine Prüfung und S5 (Ecken, Abwickeln/Zurückbiegen, Querbruch, Eckentlastung); S7a (Ersatzweg) wartet auf die Entscheidungen des Eigentümers (`blech-plan.md` §7); bis ein Typ seinen Schritt hat, lehnt `UnsupportedFeatures.RefusalBeforeCapture` den Lauf ab (`SheetMetalTypes`), einen Blechkörper ohne nachgebauten Träger `SheetMetalPlanner.Refusal` |

## Reihenfolge beim Fortsetzen

1. Blech S4 prüfen und gegenprüfen, dann S5 (Ecken, Abwickeln/Zurückbiegen, Querbruch, Eckentlastung) und S6 (Normal Cut,
   alte Biegungen einfügen) nach `blech-plan.md`; jeder Schritt mit Core-Tests, Prüfung und Gegenprüfung. S7a (Ersatzweg)
   erst nach den Entscheidungen des Eigentümers in `blech-plan.md` §7 (Ersatzweg überhaupt, Umgang mit gescheiterten
   Blättern, „Original löschen“), bei einem Nein stattdessen S7′; S8 und S9 hängen nicht davon ab.
2. Erster Test in einer laufenden SOLIDWORKS-Sitzung: die Annahmen unter „Was beim ersten Test in SOLIDWORKS zu prüfen ist“
   (STOLPERSTEINE) und `blech-plan.md` §5.2.
3. Doku, Version, Regressionsprüfung, Release (Tag nur nach Rückfrage).
