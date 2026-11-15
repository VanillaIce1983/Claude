# Erste Live-Sitzung mit dem Add-In

Anleitung für den Eigentümer: Spiegelwerk 0.11.0 (Commit `a0db63f`) zum ersten Mal in SOLIDWORKS 2026 (Revision 34.4),
in ein oder zwei Sitzungen. Vom Add-In ist noch nichts in einer laufenden Sitzung gelaufen; live geprüft sind nur B54/B55
mit einem Makro (`livetests.md`). Grundlage: STOLPERSTEINE „Was beim ersten Test in SOLIDWORKS zu prüfen ist“
(Prüftabelle und Testteile je Schritt), `blech-plan.md` §5.1/§5.2, `installer.md` §10.

## 1. Zweck und Regeln

**Zweck:** erstens sehen, ob das Add-In auf 2026 lädt und ein einfacher Lauf durchgeht; zweitens die Annahmen der
Prüftabelle bestätigen oder widerlegen, bei Blech zuerst die Tore. Jedes Ergebnis entscheidet etwas (Abschnitt 5).

**Regeln**

1. **Nur Testteile**, eigens dafür gebaut, in einem eigenen Ordner (z. B. `C:\Spiegelwerk-Test`); nie echte Teile oder
   Baugruppen. „Im selben Teil“ ändert das offene Original („Original löschen“ löscht Features), ein Baugruppenlauf
   speichert `Name_gespiegelt.SLDPRT` neben dem Original, und während des Aufbaus ändert Spiegelwerk SOLIDWORKS-Optionen
   (u. a. „Bemaßungswert eingeben“, automatische Beziehungen) und stellt sie danach zurück – nach einem Absturz von
   SOLIDWORKS womöglich nicht; dann diese Optionen ansehen.
2. **Ohne Speichern schließen.** Jedes Testteil einmal speichern, wenn es gebaut ist; jeder Lauf beginnt von diesem Stand.
   Danach das neue Teil (Spiegelwerk speichert es nie) und nach „Im selben Teil“ auch das Testteil ohne Speichern
   schließen. Ausnahme: Der Baugruppenlauf speichert selbst – nur im Testordner.
3. **Vor jedem Lauf:** Rückrollleiste am Ende des Baums, keine Skizze zur Bearbeitung offen (sonst lehnt Spiegelwerk ab,
   STOLPERSTEINE Nr. 4). Testteile mit einer Konfiguration; erfasst wird nur die aktive.
4. **Esc** nur, wo der Test es verlangt: Es unterbricht auch den Neuaufbau von SOLIDWORKS.

**Was zurückkommt**

- Einmal je Sitzung: SOLIDWORKS-Version mit Revision, Teilvorlage, Spiegelwerk 0.11.0 und die Prüfsumme des MSI.
- Je Lauf: Testteil (und wo es vom Bauplan hier abweicht), Einstellungen der Seite (Ebene, Ergebnis, geänderte Optionen)
  und der Text von **„Als Text kopieren“** im Ergebnisfenster. Strg+C geht dort nicht, und der Bericht wird nirgends
  gespeichert: vor „Schließen“ kopieren und in eine Textdatei je Sitzung einfügen.
- Meldungsfenster (z. B. „Spiegeln fehlgeschlagen: …“) haben keine Kopierschaltfläche: Bildschirmfoto oder abtippen.
- **Wenn etwas bricht** (Fehler, falsche Geometrie trotz „OK“, Absturz, Hängen): dazu ein Bildschirmfoto des
  FeatureManager-Baums (Original und Kopie, aufgeklappt) und den letzten Text der Statusleiste („Spiegelwerk – Aufbauen: …“).
- Ein „weicht ab“ mit winzigem Unterschied an einem kleinen Feature eines großen Körpers kann Rauschen sein (Zeile
  „Genauigkeit der Masseneigenschaften“; B54 maß 2,3·10⁻⁶ relativ) – trotzdem zurückschicken.

**Version:** Plan §5.2 verlangt 2019, dann 2024; da ist nur 2026. Ergebnisse gelten für 2026, wie bei B54/B55; dort
laufen auch die neueren Zweige (2022+, 2023+, 2025 SP2). Übersetzt ist das Add-In gegen die API 2019 und 2024, nie 2026.

**Reihenfolge**

1. **Makro B57** (mit B59), unabhängig vom Add-In, am besten vor dem Installieren. Ablauf im Kopf von
   `scripts/livetests/ersatzweg-b57.bas`; das Wichtigste:
   - Testteil: ein Blechteil mit runden Biegungen, gefaltet, Leiste am Ende – z. B. der Kasten aus G3 (Abschnitt 4).
     Eine große **ebene** Fläche anklicken (am besten die feste Fläche der Abwicklung) und gewählt lassen.
   - *Extras › Makro › Neu…*, Namen vergeben, speichern; im VBA-Editor *Datei › Datei importieren…* (Strg+M), die `.bas`
     wählen; Modul „Modul_B57“ doppelklicken, in `Sub Spiegelwerk_Test_B57` klicken, F5.
   - Am Ende öffnet sich der Editor mit dem Protokoll `%TEMP%\spiegelwerk-test-b57-<Teilname>.txt` (Zeile für Zeile
     geschrieben, nach einem Absturz also bis zum letzten Schritt da). Den ganzen Text zurückschicken.
   - Die vier neuen Teile und das Testteil ohne Speichern schließen (B59 ändert das Testteil).
2. Installieren (Abschnitt 2), Sitzung 1 (Abschnitt 3); Sitzung 2 (Abschnitt 4) erst, wenn T1 durchläuft.

## 2. Installieren aus der CI und Entfernen

**Voraussetzungen:** Windows x64 mit Administratorrechten; .NET Framework 4.8 (sonst bricht der Installer mit
„Spiegelwerk benötigt .NET Framework 4.8.“ ab); eine Standardvorlage für Teile (*Optionen › Standardvorlagen*; bei B54
`Teil.prtdot`), sonst scheitert „Neues Teil“ mit „Keine Standardvorlage für Teile eingestellt …“.

**Installieren**

1. Bei GitHub anmelden (Artefakte lassen sich nur angemeldet laden), den Lauf zu `a0db63f` öffnen
   (<https://github.com/VanillaIce1983/Claude/actions/runs/43748413629>) und das Artefakt **`spiegelwerk-installer`**
   laden (ZIP, etwa 575 kB; nicht `spiegelwerk-testergebnisse`). Ein Release gibt es noch nicht.
2. ZIP entpacken; gebraucht werden `spiegelwerk\paket\Spiegelwerk-0.11.0-x64.msi` und `SHA256SUMS.txt` (die beiden
   Logs darin sind vom CI-Rechner).
3. Prüfsumme in PowerShell im Ordner `paket`: `Get-FileHash .\Spiegelwerk-0.11.0-x64.msi -Algorithm SHA256` (oder
   `certutil -hashfile Spiegelwerk-0.11.0-x64.msi SHA256`). Sie muss mit `SHA256SUMS.txt` übereinstimmen
   (Groß-/Kleinschreibung egal), für diesen Lauf `fc7e19a70deed9ebda171c5a9612794e4d86b3975056e95ec3e433837ab9e9d9`.
   Weicht sie ab: nicht installieren.
4. SOLIDWORKS schließen. Eingabeaufforderung als Administrator im Ordner `paket`:
   `msiexec /i Spiegelwerk-0.11.0-x64.msi /l*v install.log` (mit Oberfläche und Protokoll; Doppelklick geht auch, ohne
   Protokoll). Das MSI ist nicht signiert: SmartScreen zeigt „Der Computer wurde durch Windows geschützt“ → „Weitere
   Informationen“ → „Trotzdem ausführen“, die Benutzerkontensteuerung „Herausgeber: Unbekannt“. Blockiert Smart App
   Control das MSI ganz: aufhören und Bescheid geben.
5. Lizenz, Installation, Abschluss. Erwartet: „Spiegelwerk ist für alle SOLIDWORKS-Installationen dieses Rechners
   eingetragen …“. Steht dort „Kein SOLIDWORKS ab 2019 gefunden. …“, ist das ein Befund (2026 sollte als
   `SldWorks.Application.34` gefunden werden) – trotzdem weiter.
6. Kurz prüfen: `reg query "HKLM\SOFTWARE\SolidWorks\Addins\{8A3C6F2E-5B1D-4E7A-9C3F-6D2E1B0A4F57}"` zeigt den
   Standardwert 1 und Title „Spiegelwerk“; in `C:\Program Files\Spiegelwerk` liegen `Spiegelwerk.dll` und
   `Spiegelwerk.Core.dll`.
7. SOLIDWORKS starten, *Extras › Zusatzanwendungen*: „Spiegelwerk“ aufgeführt, aktiv, „Beim Start“ angehakt. Lief die
   Installation unter einem anderen Konto, fehlt der Haken womöglich (`installer.md` §8): einmal setzen, mir sagen.
8. Ein Teil öffnen: Registerkarte *Spiegelwerk* im CommandManager mit **Unabhängig spiegeln** (dazu Menü und
   Symbolleiste). Fehlt nur die Registerkarte: Rechtsklick auf die Registerkarten des CommandManagers, einschalten. Fehlt
   das Add-In oder kommt beim Laden eine Meldung: Meldung, `install.log` und Ausgabe aus Schritt 6 schicken.

**Entfernen:** SOLIDWORKS schließen (sonst meldet Windows Dateien in Benutzung), dann *Einstellungen › Apps ›
Spiegelwerk › Deinstallieren* oder `msiexec /x Spiegelwerk-0.11.0-x64.msi /l*v uninstall.log`. Es bleiben
`%LOCALAPPDATA%\Spiegelwerk\icons` (von Hand löschbar) und von Baugruppenläufen gespeicherte Teile. Ein neueres MSI
derselben Version ersetzt das alte ohne Deinstallation, ein älteres wird abgelehnt („Eine neuere Version von Spiegelwerk
ist bereits installiert.“); „Reparieren“ schreibt die Registry neu. `scripts\install.cmd` nicht zusätzlich verwenden.

## 3. Sitzung 1: Grundlagen ohne Blech

**Wenn nicht anders gesagt:** Spiegelebene *Rechts* aus dem Baum, „Zu spiegelnde Elemente“ leer (ganzes Teil), „Neues
Teil (Gegenstück)“, Optionen wie vorgegeben („Jeden Schritt prüfen“ ist an). Gut ist: Zusammenfassung „n in Ordnung, 0 mit
Hinweis, 0 fehlgeschlagen, 0 übersprungen“, jede Prüfung „Volumen und Schwerpunkt stimmen“, im neuen Teil dieselben
Feature-Namen an gespiegelter Lage. Maße ohne Quelle in der Doku sind Vorschläge.

1. **T1 Platte** (STOLPERSTEINE „Ein guter erster Test“). Rechteck 100 × 50 mm auf *Vorne*, eine Ecke im Ursprung,
   10 mm; Tasche von der oberen Fläche 5 mm tief; Verrundung R3 an den Taschenkanten; eine Fase Winkel-Abstand
   2 mm × 30°, eine Abstand-Abstand 2/4 mm.
   - a) Vorgaben. Allgemeine Zeile „Spiegelebene: Ebene durch (0|0|0) mm, Normale (…)“. **Geht T1a nicht durch:
     aufhören und zurückschicken.**
   - b) Keine Ebene wählen, „oder temporäre Ebene“ = „YZ (Rechts) + Abstand in X“, Abstand 52,5 mm. Erwartet
     „Ebene durch (52,5|0|0) mm, Normale (1|0|0)“ (das Vorzeichen der Normalen kann wechseln).
2. **T2 Varianten:** wie T1, aber (a) die Platte auf *Rechts* skizziert, an *Rechts* gespiegelt (σ = −1); (b) die Tasche
   auf einer schrägen Hilfsebene skizziert – in der Kopie entsteht eine Ebene auf der Hilfs-3D-Skizze
   „Spiegelwerk-Anker“; (c) eine Tasche, deren Skizze oberhalb des Teils liegt, „bis Fläche“. Erwartet jeweils alles OK.
3. **T3 Selbes Teil:** T1, „Im selben Teil“. Die Kopie berührt das Original bei x = 0: erwartet ein eigener, nicht
   verschmolzener Körper, die Schnitte nur an der Kopie. Ohne Speichern schließen, neu öffnen, „Im selben Teil“ mit
   „Original löschen“: gelöscht wird nur, wenn alles OK ist.
4. **T4 Rotationsteil:** Profil auf *Vorne*, Skizzen-Mittellinie als Achse; Rotation in eine Richtung unter 360°
   (z. B. 270°), ein Rotationsschnitt, eine Rotation in zwei Richtungen. Erwartet OK (Zeilen „Rotationsachse“,
   „Rotationsrichtung, Wandseite, Fasenseite, Startversatz“).
5. **T5 Lochplatte:** drei M6-Senkbohrungen (Bohrungsassistent), zu den Kanten bemaßt; eine Gewindebohrung; lineares
   Muster der Senkbohrung entlang einer Kante; Referenzachse und Kreismuster 3 × 40° um sie. Erwartet „3 × M6 …“ ohne
   Hinweis auf nicht übertragene Bemaßungen, bei den Mustern „Lage der Instanzen geprüft“ oder „Richtung nach Lage der
   Instanzen korrigiert“, „Kreismuster, 3 × 40°“. Danach „Im selben Teil“ nur die Bohrungen gewählt, und noch einmal mit
   Esc vor einer Bohrung: Rückfrage „Spiegeln hier abbrechen? …“, Ja; die Zusammenfassung endet mit „– abgebrochen“.
   Bericht und Baum (Bildschirmfoto) zurück (Zeile „Besitz nach der Umstellung“).
6. **T6 Spiegel-Features:** Platte mit Aufsatz und Bohrung, beide mit „Features spiegeln“ an *Rechts*; das Teil dann an
   *Vorne* in ein neues Teil. Erwartet „Spiegeln1“ mit Schritten („Spiegeln1 › …“), alles OK, im Ziel **kein**
   Spiegel-Feature. Dazu „Körper spiegeln“ an einer ebenen Fläche mit und ohne „verschmelzen“ und an einer Ebene:
   Verschmilzt SOLIDWORKS im Original auch an der Ebene, ist das der Befund (Zeile „„verschmelzen“ bei „Körper spiegeln““).
7. **T7 Skizzenbereiche:** Rechteck mit senkrechter Mittellinie auf *Vorne*, nur die linke Hälfte als Skizzenbereich
   extrudiert. Erwartet „…, Profil: 1 Skizzenbereich (…)“ und die Kopie rechts. Dasselbe auf *Oben*: Der Bericht nennt
   den Koordinatenraum der Bereichskanten – diese Zeile ist der Befund (Zeile „Koordinatenraum der Bereichskanten“).
8. **T8 Baugruppe:** T1 im Testordner speichern, in eine neue Baugruppe einfügen, abseits des Ursprungs und gedreht,
   Baugruppe speichern. Ebene *Rechts* der Baugruppe, Komponenten = T1, „In Baugruppe einfügen“ an. Erwartet
   `T1_gespiegelt.SLDPRT` neben T1, Zeilen „Gespeichert: …“ und „— <Komponente> → T1_gespiegelt.SLDPRT: …“, die neue
   Komponente an gespiegelter Lage ohne Verknüpfungen; ein zweiter Lauf ergibt `T1_gespiegelt_2.SLDPRT`. Baugruppe ohne
   Speichern schließen, die erzeugten Teile löschen. Bei Zeit danach die übrigen Teile aus „Ein guter erster Test“.

## 4. Sitzung 2: Blech

**Wenn nicht anders gesagt:** neues Teil, „Jeden Schritt prüfen“ an. Die Testteile stehen ausführlicher in STOLPERSTEINE
unter „Blech (S1a)“ bis „Blech (S9)“. Ein Teil mit einem Riss oder „In Blech umwandeln“ wird vor dem Aufbau abgelehnt
(„Blech-Feature „…“ (<Typ>) wird noch nicht nachgebaut. Es wurde nichts gespiegelt.“); S7a, S7b und S10 sind nicht
gebaut. Im kopierten Text steht hinter jedem Feature sein Typname, `[OK] Name (Typ): …` – bei G5 und G7 der Befund.

### 4.1 Tore und typentscheidende Prüfungen zuerst

| Lauf | Prüfung | Art | Wenn nicht bestanden |
| --- | --- | --- | --- |
| G1 | B1 Basislasche anlegen, B11 Falten für den Lauf | Tor S1a | B1: andere Reihenfolge der Wege, baut keiner, ist S1 blockiert; B11: Lauf abgelehnt |
| G2 | B14/B15 Blech im selben Teil | Tor für S1b | die Ablehnung im selben Teil kommt zurück |
| G3 | B28/B29 Kantenlasche | Tor S3 (B29) | Kantenlasche zurück auf „noch nicht“ |
| G4 | B36 Skizze auf der Fläche | Tor S4 (Kröpfung) | Kröpfung „nicht unterstützt“, skizzierte Biegung auf einer Ebene mit Hinweis |
| G5 | B44 Typnamen, B49 Eckentlastung | S5 | Typtabelle korrigieren; Eckentlastung „nicht unterstützt“ oder älterer Aufruf |
| G6 | B51 normaler Schnitt, B52 „Biegungen einfügen“ | S6 (S7a) | Weg des normalen Schnitts; B52: Weg B des Ersatzwegs fällt weg |
| G7 | B64–B66 Lasche und Schlitz, Versteifungsrippe | S8 | Namen und Weg; falscher Kandidat in `GussetRules.AnchorOf` gestrichen |
| G8 | B67 Umformwerkzeug | Tor S9 (Weg) | nur Variante A |

**G1 – B1, B11 (S1a).** Teil: Platte 100 × 50 × 2 mm (Basislasche, geschlossenes Profil) mit zwei Bohrungen; U-Profil
aus offenem Profil (K 0,44); die Platte noch einmal: abwickeln, im abgewickelten Zustand einen Schnitt hinzufügen,
abgewickelt speichern. Wahl: *Rechts* und *Vorne*, je mit und ohne „Jeden Schritt prüfen“.
- B1 bestanden: Basislasche OK mit „Angelegt über Datenobjekt.“ (oder „… InsertSheetMetalBaseFlange.“ /
  „… InsertSheetMetalBaseFlange2.“), Prüfung „Abwicklung geprüft“, keine Hinweise auf Blechparameter. Nicht bestanden:
  „SOLIDWORKS hat die Basislasche nicht erzeugt (Wege: …)“. Wählt die Prüfung beim U-Profil „Materialseite der Kontur
  gewechselt“, ist das ein Befund zu B4.
- B11 bestanden: Das abgewickelt gespeicherte Teil läuft; danach ist das Original abgewickelt wie vorher und der Schnitt
  wieder da (Zeile „Features hinter der Abwicklung“). Nicht bestanden: „Die Abwicklung „…“ ließ sich für den Lauf nicht
  zurückfalten. …“ oder „… ließ sich im Original nicht wieder abwickeln – bitte von Hand abwickeln.“ Dazu ein
  Bildschirmfoto des Baums des Originals nach dem Lauf.

**G2 – B14/B15 (Tor für S1b).** Teil: Platte mit Lasche (ein Körper); Zweikörperteil aus zwei Basislaschen mit
verschiedenen Dicken, die erste mit eigener (überschriebener) Dicke. Vorher notieren: Dicke, Radius, K-Faktor im Ordner
„Blech“ und an den Blech-Features, Masse des Originals. Wahl: Platte „Im selben Teil“, ganzes Teil, einmal gefaltet, einmal
abgewickelt gespeichert; Zweikörperteil neues Teil, ganz und nur der erste Körper.
- Bestanden: Ordner „Blech“, Blech-Features und Masse des Originals unverändert, gefaltet bzw. abgewickelt wie vorher;
  „Blech im selben Teil: Original und Kopie teilen die Blech-Standardwerte des Teils …“; im neuen Teil beide Körper mit
  ihren Dicken.
- Nicht bestanden: „Die Blech-Standardwerte hatten sich mit diesem Schritt geändert und wurden wiederhergestellt (…)“ (der
  Wächter hat gegriffen, die Annahme hielt nicht), „Achtung: Die Blech-Standardwerte … ließen sich nicht wiederherstellen
  …“, „… frühere Körper sind danach aber nicht wie vorher.“, „Im selben Teil nicht angelegt: …“, oder andere Werte. Nach
  einem Abbruch bittet die Meldung um Strg+Z: ausführen und notieren, ob alles zurückkommt (Zeile „Rückgängig nach
  Abbruch“). Zurück: Text, Werte vorher/nachher, Bildschirmfoto des Ordners „Blech“.

**G3 – B28/B29 (Tor für S3).** Teil: Kasten aus einer Basislasche 100 × 60 × 2 mm mit vier Kantenlaschen zu 90° –
einmal eine Lasche über vier Kanten, einmal vier einzelne; dazu eine Kantenlasche zu 60°. Wahl: *Rechts* und *Vorne*.
- Bestanden: jede Kantenlasche OK mit „Profilebene mit Winkel …° und FlipDir ja/nein (Kandidat i von n).“ und „Profile
  geprüft: Ebene, Lage und Abstand zur Kante wie gespiegelt.“; der Weg (B28) wird genannt. Nicht bestanden: „Keine
  passende Profilebene für die Kantenlasche (n Kandidaten aus Winkel und Richtung probiert).“ Die Kandidat-Zeilen
  (Winkel, FlipDir) sind der eigentliche Befund.

**G4 – B36 (Tor für die Kröpfung, S4).** Teil: Basislasche 100 × 50 × 2 mm auf *Vorne* mit einer skizzierten Biegung auf
der oberen Fläche (Linie bei x = 50 mm, feste Seite x < 50, 90°, eigener K-Faktor 0,3); zweites Teil: Platte mit einer
Kröpfung. Wahl: *Rechts* und *Vorne*, „Jeden Schritt prüfen“ **aus** (hier entscheiden die Zeugenpunkte).
- Bestanden: „Auf der gespiegelten Fläche skizziert, auf der das Original liegt.“, die Biegung „Durch … Zeugenpunkte
  bestätigt: …“, K 0,3 zurückgelesen, die Kröpfung OK.
- Nicht bestanden: Kröpfung „Die Skizze „…“ der Kröpfung liegt im Ziel nicht auf der gespiegelten Fläche …“ oder
  „SOLIDWORKS hat die Kröpfung nicht erzeugt.“; Biegung „… sondern auf einer Ebene (…); ob SOLIDWORKS das annimmt, ist
  nicht geprüft (B36).“ – dann ein Bildschirmfoto, ob die Biegung trotzdem richtig dasteht.

**G5 – B44, B49 (S5).** Teil: Kasten wie G3 (vier einzelne Laschen) mit geschlossenen Ecken (stumpf und überlappend), einer
gebrochenen Ecke, Abwickeln zweier Biegungen → Schnitt über eine Biegung → Zurückbiegen, einem Querbruch auf der
Bodenfläche und einer Eckentlastung an zwei Ecken. Wahl: *Rechts*.
- B44: Erwartet die Typen `CornerFeat`, `BreakCorner`, `UnFold`, `Fold`, `CrossBreak`; die Eckentlastung hat keinen
  bestätigten Namen – ihren Typ abschreiben. Nicht bestanden: „Feature-Typ <Name> wird nicht unterstützt.“ oder eine
  Ablehnung mit einem anderen Namen.
- B49: Eckentlastung OK; „Eckentlastung über den älteren Aufruf angelegt (kein Datenobjekt): …“ heißt, der neuere Weg
  ging nicht.

**G6 – B51, B52 (S6).** Teil: Platte 100 × 50 × 2 mm mit einem normalen Schnitt (eine Flächengruppe; ein zweiter mit
zwei); gewandeter Kasten (Wandung 2 mm) mit „Biegungen einfügen“ (K 0,44, Entlastung 0,5), einmal mit unterdrücktem
`ProcessBends` gespeichert. Wahl: *Rechts*; den Kasten auch „Im selben Teil“.
- B51: Der Bericht nennt den Weg des normalen Schnitts – „Datenobjekt“, „AddSMNormalCut“ oder „AddSMNormalCut in der
  Reihenfolge der Hilfe“. Ein anderer als „Datenobjekt“ ist ein Befund.
- B52: In der Kopie entstehen `SheetMetal`, `FlattenBends` und `ProcessBends`, die Einträge sind OK, die Kopie steht im
  selben Zustand wie das Original. Bildschirmfoto des Baums. (B52 läuft auch im Makro B57 mit.)

**G7 – B64–B66 (S8).** Teil: Zweikörperteil aus zwei Basislaschen (2 mm), rechtwinklig aneinander, mit Lasche und Schlitz
(drei Laschen gleichmäßig, D1 = 10 mm, D2 = 25 mm, ohne Punkte); L-Blech mit zwei Versteifungsrippen auf derselben Biegung
ohne Bezugspunkt (30 mm, dann 20 mm weiter; 10 mm breit, 3 mm dick). Wahl: *Rechts* und *Vorne* (bei einer läuft die
Biegung der Kopie andersherum).
- B64: Lasche und Schlitz OK, „Schlitz-Optionen: Standard (geprüft)“, der Schlitz heißt wie im Original; die Typnamen
  abschreiben (nicht bestätigt). Nicht bestanden: ein Fehler mit „kein Datenobjekt (CreateDefinition(88))“ in den Details.
- B65: „Lage auf der Biegung geprüft.“ oder „Lage auf der Biegung geprüft: <Variante>.“ – die Variante sagt, von welcher
  Kante die zweite Rippe zählt. B66 nebenbei: eine Rippe mit Bezugspunkt und Profilmaßen.

**G8 – B67 (Tor für den Weg, S9).** Teil: Platte 120 × 80 × 2 mm mit einer Lamelle (louver) aus der
Konstruktionsbibliothek, drei Punkte in einer Positionsskizze, zu den Kanten bemaßt. Wahl: *Rechts*.
- Bestanden: „Seite und Drehung geprüft (… Punkte der Form an 3 Stellen).“ Nicht bestanden: „Mit der gewählten
  Positionsskizze ließ sich das Werkzeug nicht einfügen; es wurde auf der Fläche abgesetzt und seine Positionsskizze
  nachträglich geschrieben.“ Auf 2026 außerdem: kein „Am Feature gedreht passte das Werkzeug nicht …“ (B69).

### 4.2 Übrige Rauchtests je Schritt

Kurzform; Bau und Erwartung ausführlich im jeweiligen STOLPERSTEINE-Absatz.

| Schritt | Testteil (Auswahl) | Wahl | Worauf achten |
| --- | --- | --- | --- |
| S1a | Lasche auf einem Körper mit abweichender Dicke; offene Basislasche „bis Fläche“ zu einer parallelen Fläche eines nicht gewählten Körpers | *Rechts*, *Vorne* | Weg der Basislasche, „Abwicklung geprüft“; bei „bis Fläche“ „ersetzt durch Blind“ |
| S1b | Einkörper-Original; Spiegelebene auf einer Fläche; nur die Lasche gewählt; „Original löschen“; Gleichung auf der Dicke; Blechwerte aus dem Werkstoff | selbes Teil | Original unverändert; „Original löschen“ abgelehnt; Werkstoffwerte bleiben werkstoffgesteuert |
| S2 | Platte mit Säumen (geschlossen 10, offen 8/1,5, gerollt, tropfenförmig 200°), Gehrungslaschen über 1 und 3 Kanten | Prüfung an und aus | „Entlastung: Standard (geprüft)“, keine Hinweise auf Werte oder Biegungen |
| S3 | 60° auf andersherum laufender Kante, Versatz 3 mm, „bis Fläche“, K 0,38; verkürztes Profil mit Ausschnitt | auch selbes Teil | wie G3, keine Hinweise |
| S4 | U-Profil aus zwei Linien; Kröpfung blind und „bis Fläche“; Bohrung auf der beweglichen Seite (selbes Teil, nur die Biegung); feste Fläche in der Biegezone | Prüfung aus | „Zeugenpunkte nur auf den eigenen Flächen …“, kein „weicht ab“ |
| S5 | wie G5 | selbes Teil, Prüfung an und aus | gleiche Features an gespiegelter Lage |
| S6 | Schnitt schräg zur Blechebene; nach `ProcessBends` Schnitt, normaler Schnitt, Verrundung an einer Biegekante, mit unterdrücktem `ProcessBends` gespeichert; Schnitt zwischen `FlattenBends` und `ProcessBends` | *Rechts*, *Vorne* | alle drei gespiegelt und einzeln wieder unterdrückt; der letzte Fall wird abgelehnt |
| S8 | Lasche mit Startpunkt, „Versatz von Fläche“ umgekehrt; Teil ohne Blech mit Lasche und Schlitz; `tab_and_slot.sldprt` der Hilfe | auch selbes Teil | D1/D2 wo nötig getauscht |
| S9 | Sicke 90°, Prägung, Durchzug, Lamelle umgekehrt; Werkzeug mit zwei Konfigurationen; verknüpft/nicht verknüpft; Prägung mit eigener Anzeige in der Abwicklung | auch selbes Teil | „Seite und Drehung geprüft“, „Anzeige in der Abwicklung wie im Original …“; die nicht verknüpfte bleibt unverknüpft |

## 5. Was ich mit den Ergebnissen mache

1. **Rohtexte** lege ich wie bei B54/B55 unter `scripts/livetests/ergebnisse/<Datum>-<Tests>-sw2026.txt` ab.
2. **`livetests.md`** bekommt je Test einen Abschnitt im Muster von B54/B55: Datum, Version, Vorlage, Stand (Makro bzw.
   MSI mit Commit), Rohprotokoll, Testteil, Spiegelebenen, Tabelle Test | Ergebnis | Befund und „Folgen“.
3. **STOLPERSTEINE:** In der Tabelle „Was beim ersten Test in SOLIDWORKS zu prüfen ist“ bekommt jede geprüfte Stelle ihr
   Ergebnis mit Version; dabei trage ich B54/B55 nach (`livetests.md` wartete auf das Einfügen von `feat-blech`, das mit
   `629ed6c` geschehen ist). Zeigt sich Rauschen bei „weicht ab“, ändere ich `MassComparison.NoiseRelative` (Zeile
   „Genauigkeit der Masseneigenschaften“).
4. **Ein gescheitertes Tor setzt den Typ zurück**, wie `STAND.md` und `blech-plan.md` §5.2 sagen (Tabelle in 4.1): B14 →
   Blech im selben Teil wieder abgelehnt; B29 → `EdgeFlange` „noch nicht“; B36 → Kröpfung „nicht unterstützt“; B67 → nur
   Variante A; B1 ohne einen Weg → S1 blockiert. Danach `STAND.md`, README „Unterstützte Features“ und Tests nachziehen,
   neues MSI aus der CI, den betroffenen Rauchtest wiederholen.
5. **B57** wählt den Weg von S7a (A2, dann B, dann F3); scheitern A und B, bleibt nur F3, und S7 ist neu abzuwägen
   (`STAND.md`, „Reihenfolge beim Fortsetzen“, Punkt 1).
6. **Fehler, die keine Annahme betreffen** (Absturz, falsche Geometrie trotz „OK“): Core-Test, Korrektur mit Prüfung und
   Nachprüfung wie bei jedem Schritt. Bestanden heißt nur auf 2026; 2019 und 2024 bleiben offen. Tag nur nach Rückfrage.
