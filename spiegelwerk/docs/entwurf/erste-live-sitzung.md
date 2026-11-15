# Erste Live-Sitzung mit dem Add-In

Anleitung für den Eigentümer: Spiegelwerk 0.11.0 (Commit `a0db63f`) zum ersten Mal in SOLIDWORKS 2026 (Revision 34.4),
in ein oder zwei Sitzungen. Vom Add-In lief noch nichts in einer laufenden Sitzung; live geprüft sind nur B54/B55 per
Makro (`livetests.md`). Ergebnisse gelten für 2026 (Plan §5.2: 2019, dann 2024); dort laufen auch die neueren Zweige
(2022+, 2023+, 2025 SP2), übersetzt ist gegen die API 2019 und 2024. Grundlage: STOLPERSTEINE „Was beim ersten Test in
SOLIDWORKS zu prüfen ist“ (Prüftabelle, Testteile je Schritt), `blech-plan.md` §5.1/§5.2, `installer.md` §9/§10.

## 1. Zweck und Regeln

**Zweck:** erstens sehen, ob das Add-In auf 2026 lädt und ein einfacher Lauf durchgeht; zweitens die Annahmen der
Prüftabelle bestätigen oder widerlegen, bei Blech zuerst die Tore. Jedes Ergebnis entscheidet etwas (Abschnitt 5).

**Regeln**
1. **Nur Testteile**, eigens gebaut, in einem eigenen Ordner (z. B. `C:\Spiegelwerk-Test`), nie echte: „Im selben Teil“
   ändert das offene Original („Original löschen“ löscht Features), ein Baugruppenlauf speichert
   `Name_gespiegelt.SLDPRT` daneben. Spiegelwerk stellt beim Aufbau fünf Systemoptionen um und danach zurück, nach einem
   Absturz womöglich nicht; vor dem ersten Lauf ihre Werte notieren (*Extras › Optionen › Systemoptionen*):
   „Bemaßungswert eingeben“ (Allgemein), „Automatische Beziehungen“ und Skizzenfang (Skizze › Beziehungen/Fang), bei
   überbestimmenden Bemaßungen Abfrage und „standardmäßig als getrieben festlegen“ (Skizze).
2. **Ohne Speichern schließen:** das neue Teil (Spiegelwerk speichert es nie) und nach „Im selben Teil“ das Testteil.
   Jedes Testteil einmal gebaut speichern; jeder Lauf beginnt von dort. Der Baugruppenlauf speichert selbst.
3. **Vor jedem Lauf:** Rückrollleiste am Ende des Baums, keine Skizze zur Bearbeitung offen (sonst lehnt Spiegelwerk ab,
   STOLPERSTEINE Nr. 4). Testteile mit einer Konfiguration; erfasst wird nur die aktive.
4. **Esc** nur, wo der Test es verlangt: Es unterbricht auch den Neuaufbau von SOLIDWORKS.

**Was zurückkommt**
- Einmal je Sitzung: Version mit Revision (*? › Über SOLIDWORKS*, z. B. 34.4.0), Teilvorlage, Prüfsumme des MSI.
- Je Lauf: Testteil (und Abweichungen vom Bauplan), Einstellungen der Seite und den Text von **„Als Text kopieren“** im
  Ergebnisfenster (Strg+C geht dort nicht, gespeichert wird nichts), vor „Schließen“ in eine Textdatei je Sitzung
  eingefügt, davor eine Kennung (z. B. „G3 Vorne, Prüfung an, Kasten 4 Einzel“) – der Bericht nennt nur den Teilnamen.
- **Wenn etwas bricht** (Fehler, falsche Geometrie trotz „OK“, Absturz, Hängen): Bildschirmfoto des Baums (Original und
  Kopie, aufgeklappt) und letzter Text der Statusleiste („Spiegelwerk – Aufbauen: …“); Meldungsfenster ohne
  Kopierschaltfläche fotografieren; nach einem Absturz die Optionen aus Regel 1 prüfen.
- Ein „weicht ab“ mit winzigem Unterschied an einem kleinen Feature eines großen Körpers kann Rauschen sein – trotzdem
  schicken. „Prüftabelle: …“ verweist auf eine Zeile der Tabelle in STOLPERSTEINE, nicht auf Text im Bericht.

**Reihenfolge**
1. **Makro B57** (mit B59), unabhängig vom Add-In, am besten vor dem Installieren; Ablauf im Kopf von
   `scripts/livetests/ersatzweg-b57.bas`. Testteil: ein gefaltetes Blechteil mit runden Biegungen, Leiste am Ende (z. B.
   der Kasten aus G3); eine große **ebene** Fläche anklicken (am besten die feste Fläche der Abwicklung) und gewählt
   lassen. *Extras › Makro › Neu…*, im VBA-Editor die `.bas` importieren (Strg+M), „Modul_B57“ öffnen, in
   `Sub Spiegelwerk_Test_B57` klicken, F5. Am Ende öffnet sich das Protokoll
   `%TEMP%\spiegelwerk-test-b57-<Teilname>.txt` (zeilenweise geschrieben, auch nach einem Absturz da). **Bestanden** ist
   B57, wenn unter „=== Ergebnis ===“ mindestens ein Weg „bestanden“ meldet (A1/A2 Umwandeln, B1/B2 Biegungen einfügen);
   B59 steht darunter. Bei „Abbruch: …“ davor (Fläche nicht eben, kein Blech-Feature, Masse nicht lesbar) Fläche bzw.
   Teil prüfen und einmal wiederholen, nach „ABBRUCH: Laufzeitfehler …“ nicht. Eine Wiederholung überschreibt das
   Protokoll: vorher umbenennen. Jedes Protokoll ganz schicken; die vier neuen Teile und das Testteil ohne Speichern
   schließen (B59 ändert es).
2. Installieren (Abschnitt 2), Sitzung 1 (Abschnitt 3); Sitzung 2 (4.1) erst, wenn T1 durchläuft; 4.2 erst nach
   Auswertung von 4.1, in einer späteren Sitzung. Bei wenig Zeit nur T1, T3, G1–G4 und G8, in dieser Reihenfolge.

## 2. Installieren aus der CI und Entfernen

**Voraussetzungen:** Windows x64 mit Administratorrechten; .NET Framework 4.8 (sonst bricht der Installer mit
„Spiegelwerk benötigt .NET Framework 4.8.“ ab); eine Standardvorlage für Teile (*Optionen › Standardvorlagen*; bei B54
`Teil.prtdot`), sonst scheitert „Neues Teil“ mit „Keine Standardvorlage für Teile eingestellt …“.

**Installieren**
1. Bei GitHub anmelden (Artefakte gibt es nur angemeldet), den Lauf zu `a0db63f` öffnen
   (<https://github.com/VanillaIce1983/Claude/actions/runs/43748413629>) und das Artefakt **`spiegelwerk-installer`**
   laden (ZIP, etwa 575 kB; nicht `spiegelwerk-testergebnisse`). Ein Release gibt es noch nicht.
2. ZIP entpacken („Alle extrahieren“, z. B. nach `…\Downloads\spiegelwerk-installer`); gebraucht werden
   `paket\Spiegelwerk-0.11.0-x64.msi` und `paket\SHA256SUMS.txt` (daneben `install.log`/`uninstall.log` vom CI-Rechner).
3. Prüfsumme in PowerShell im Ordner `paket`: `Get-FileHash .\Spiegelwerk-0.11.0-x64.msi -Algorithm SHA256`; sie muss
   mit `SHA256SUMS.txt` übereinstimmen (Groß-/Kleinschreibung egal), hier
   `fc7e19a70deed9ebda171c5a9612794e4d86b3975056e95ec3e433837ab9e9d9`. Weicht sie ab: nicht installieren.
4. SOLIDWORKS schließen; Eingabeaufforderung als Administrator (startet in `System32`):
   `cd /d "<entpackter Ordner>\paket"`, dann `msiexec /i Spiegelwerk-0.11.0-x64.msi /l*v install.log`. Per Doppelklick
   (ohne Protokoll) zeigt das nicht signierte MSI SmartScreen „Der Computer wurde durch Windows geschützt“ → „Weitere
   Informationen“ → „Trotzdem ausführen“, dann die Benutzerkontensteuerung „Herausgeber: Unbekannt“; aus der
   Administrator-Eingabeaufforderung erscheint keins von beiden. Sperrt Smart App Control das MSI: aufhören, melden.
5. Erwartet zum Abschluss „Spiegelwerk ist für alle SOLIDWORKS-Installationen dieses Rechners eingetragen …“; „Kein
   SOLIDWORKS ab 2019 gefunden. …“ ist ein Befund (2026 sollte als `SldWorks.Application.34` gelten) – trotzdem weiter.
6. Prüfen: `reg query "HKLM\SOFTWARE\SolidWorks\Addins\{8A3C6F2E-5B1D-4E7A-9C3F-6D2E1B0A4F57}"` zeigt Standardwert 1,
   Title „Spiegelwerk“; in `C:\Program Files\Spiegelwerk` liegen `Spiegelwerk.dll` und `Spiegelwerk.Core.dll`.
7. SOLIDWORKS starten, *Extras › Zusatzanwendungen*: „Spiegelwerk“ aufgeführt, aktiv, „Beim Start“ angehakt. Lief die
   Installation unter einem anderen Konto, fehlt der Haken womöglich (`installer.md` §8): einmal setzen, mir sagen.
8. Ein Teil öffnen: Registerkarte *Spiegelwerk* im CommandManager mit **Unabhängig spiegeln** (dazu Menü und
   Symbolleiste); fehlt nur die Registerkarte, per Rechtsklick auf die Registerkarten einschalten. Fehlt das Add-In oder
   kommt beim Laden eine Meldung: Meldung, `paket\install.log` und Ausgabe aus Schritt 6 schicken.

**Entfernen:** SOLIDWORKS schließen, dann *Einstellungen › Apps › Spiegelwerk › Deinstallieren* oder
`msiexec /x Spiegelwerk-0.11.0-x64.msi /l*v uninstall.log`. Es bleiben `%LOCALAPPDATA%\Spiegelwerk\icons` und
gespeicherte Gegenstücke aus Baugruppenläufen. „Reparieren“ schreibt die Registry neu; `scripts\install.cmd` nicht dazu
verwenden.

## 3. Sitzung 1: Grundlagen ohne Blech

**Ein Lauf:** *Unabhängig spiegeln*, Seite einstellen, OK; das Ergebnisfenster öffnet sich danach. **Wenn nicht anders
gesagt:** Ebene *Rechts*, „Zu spiegelnde Elemente“ leer, „Neues Teil (Gegenstück)“, Optionen wie vorgegeben („Jeden
Schritt prüfen“ an). Gut ist: „n in Ordnung, 0 mit Hinweis, 0 fehlgeschlagen, 0 übersprungen“, jede Prüfung „Volumen und
Schwerpunkt stimmen“, im neuen Teil dieselben Feature-Namen an gespiegelter Lage. Maße ohne Quelle sind Vorschläge.

1. **T1 Platte:** Rechteck 100 × 50 mm auf *Vorne*, eine Ecke im Ursprung, 10 mm; Tasche von der großen Fläche (z = 10)
   5 mm tief; Verrundung R3 an den Taschenkanten; eine Fase Winkel-Abstand 2 mm × 30°, eine Abstand-Abstand 2/4 mm.
   - a) Vorgaben. Zeile „Spiegelebene: Ebene durch (0|0|0) mm, Normale (…)“. **Scheitert T1a: aufhören,
     zurückschicken.**
   - b) Keine Ebene wählen, „oder temporäre Ebene“ = „YZ (Rechts) + Abstand in X“, Abstand 52,5 mm. Erwartet genau
     „Ebene durch (52,5|0|0) mm, Normale (1|0|0)“.
2. **T2 Varianten:** wie T1, aber (a) die Platte auf *Rechts* skizziert, an *Rechts* gespiegelt (σ = −1); (b) die Tasche
   auf einer schrägen Hilfsebene skizziert – in der Kopie entsteht eine Ebene auf der Hilfs-3D-Skizze
   „Spiegelwerk-Anker“; (c) eine Tasche, deren Skizze oberhalb des Teils liegt, „bis Fläche“. Erwartet jeweils alles OK.
3. **T3 Selbes Teil:** T1, „Im selben Teil“. Die Kopie berührt das Original bei x = 0: erwartet ein eigener, nicht
   verschmolzener Körper, die Schnitte nur an der Kopie. Ohne Speichern schließen, neu öffnen, „Im selben Teil“ mit
   „Original löschen“: gelöscht wird nur, wenn alles OK ist.
4. **T4 Rotationsteil:** Profil auf *Vorne*, Mittellinie als Achse; Rotation in eine Richtung unter 360°, ein
   Rotationsschnitt, eine in zwei Richtungen. Erwartet OK (Prüftabelle: „Rotationsachse“, „Rotationsrichtung, …“).
5. **T5 Lochplatte:** drei M6-Senkbohrungen (Bohrungsassistent), zu den Kanten bemaßt; eine Gewindebohrung; lineares
   Muster der Senkbohrung entlang einer Kante; Kreismuster um eine Referenzachse, 3 Instanzen im Abstand 40° („Gleicher
   Abstand“ aus). Erwartet „3 × M6 …“ ohne Hinweis auf nicht übertragene Bemaßungen, bei den Mustern „Lage der Instanzen
   geprüft“ oder „Richtung … korrigiert“ und „Kreismuster, 3 × 40°“ (sonst Gesamtwinkel und „gesamt“). Danach „Im selben
   Teil“ nur die Bohrungen, und noch einmal mit Esc vor einer Bohrung: „Spiegeln hier abbrechen? …“, Ja; die
   Zusammenfassung endet mit „– abgebrochen“. Baum als Bildschirmfoto dazu (Prüftabelle: „Besitz nach der Umstellung“).
6. **T6 Spiegel-Features:** Platte mit Aufsatz und Bohrung, beide mit „Features spiegeln“ an *Rechts*, das Teil an
   *Vorne* in ein neues Teil: „Spiegeln1“ mit Schritten („Spiegeln1 › …“), alles OK, im Ziel **kein** Spiegel-Feature.
   Dazu „Körper spiegeln“ an einer ebenen Fläche mit und ohne „verschmelzen“ und an einer Ebene; verschmilzt SOLIDWORKS
   im Original auch an der Ebene, ist das der Befund (Prüftabelle: „„verschmelzen“ bei „Körper spiegeln““).
7. **T7 Skizzenbereiche:** Rechteck mit senkrechter Mittellinie auf *Vorne*, nur die linke Hälfte als Skizzenbereich
   extrudiert: „…, Profil: 1 Skizzenbereich (…)“, die Kopie rechts. Dasselbe auf *Oben*: Die Berichtszeile zum
   Koordinatenraum der Bereichskanten ist der Befund (Prüftabelle: „Koordinatenraum der Bereichskanten“).
8. **T8 Baugruppe:** T1 speichern, in eine neue Baugruppe einfügen (abseits des Ursprungs, gedreht), Baugruppe
   speichern; Ebene *Rechts* der Baugruppe, Komponenten = T1, „In Baugruppe einfügen“ an. Erwartet
   `T1_gespiegelt.SLDPRT` neben T1, Zeilen „Gespeichert: …“ und „— <Komponente> → T1_gespiegelt.SLDPRT: …“, die neue
   Komponente an gespiegelter Lage ohne Verknüpfungen; ein zweiter Lauf ergibt `T1_gespiegelt_2.SLDPRT`. Baugruppe ohne
   Speichern schließen, erzeugte Teile löschen; bei Zeit die übrigen Teile aus „Ein guter erster Test“.

## 4. Sitzung 2: Blech

**Wenn nicht anders gesagt:** neues Teil, Ebene *Rechts*, „Jeden Schritt prüfen“ an; Testteile ausführlicher in
STOLPERSTEINE „Blech (S1a)“ bis „Blech (S9)“. Ein Teil mit Riss oder „In Blech umwandeln“ wird vor dem Aufbau abgelehnt
(„Blech-Feature „…“ (<Typ>) wird noch nicht nachgebaut. …“); S7a, S7b und S10 sind nicht gebaut. Im kopierten Text steht
hinter jedem Feature sein Typname, `[OK] Name (Typ): …` – bei G5 und G7 der Befund.

### 4.1 Tore zuerst: G1–G4 und G8, dann G5–G7

| Lauf | Prüfung | Art | Wenn nicht bestanden |
| --- | --- | --- | --- |
| G1 | B1 Basislasche anlegen, B11 Falten für den Lauf, B12/B16 Abwicklung | Tor S1a | B1: andere Reihenfolge der Wege, baut keiner, ist S1 blockiert; B11: Lauf abgelehnt; B12: zum Bearbeiten Unterdrückung aufheben; B16: Biegezugabe anders setzen |
| G2 | B2 Weg A an berührendem Körper, B14/B15 Blech im selben Teil | Tor für S1b (B14) | B2: Weg C (Merge aus) zuerst; B14: die Ablehnung im selben Teil kommt zurück |
| G3 | B28/B29 Kantenlasche | Tor S3 (B29) | Kantenlasche zurück auf „noch nicht“ |
| G4 | B36 Skizze auf der Fläche | Tor S4 (Kröpfung) | Kröpfung „nicht unterstützt“, skizzierte Biegung auf einer Ebene mit Hinweis |
| G8 | B67 Umformwerkzeug | Tor S9 (Weg) | nur Variante A |
| G5 | B44 Typnamen, B49 Eckentlastung | S5 | Typtabelle korrigieren; Eckentlastung „nicht unterstützt“ oder älterer Aufruf |
| G6 | B51 normaler Schnitt, B52 „Biegungen einfügen“ | S6 (S7a) | Weg des normalen Schnitts; B52: Weg B des Ersatzwegs fällt weg |
| G7 | B64–B66 Lasche und Schlitz, Versteifungsrippe | S8 | Namen und Weg; falscher Kandidat in `GussetRules.AnchorOf` gestrichen |

**G1 – B1, B11, B12, B16 (S1a).** Teile: Platte 100 × 50 × 2 mm (Basislasche, geschlossenes Profil) mit zwei Bohrungen;
U-Profil aus offenem Profil (K 0,44); für B11 das U-Profil abgewickelt, darin ein Schnitt quer über eine Biegezone,
abgewickelt gespeichert, und nach G2 das Zweikörperteil aus G2 mit beiden Abwicklungen abgewickelt gespeichert (ganz und
nur ein Körper gewählt). Wahl: *Rechts* und *Vorne*, je mit und ohne „Jeden Schritt prüfen“.
- B1 bestanden: Basislasche OK mit „Angelegt über Datenobjekt.“ (oder „… InsertSheetMetalBaseFlange.“ /
  „… InsertSheetMetalBaseFlange2.“), keine Hinweise auf Blechparameter; nicht bestanden: „SOLIDWORKS hat die Basislasche
  nicht erzeugt (Wege: …)“. „Abwicklung geprüft“ steht bei der Abwicklung, nur mit Prüfung; ohne steht dort „Prüfung:
  nicht geprüft“ (kein Fehlschlag). „Korrigiert nach Messung: Materialseite der Kontur gewechselt.“ beim U-Profil ist
  ein Befund zu B4; „…, Materialseite der Kontur gewechselt (gespiegelt)“ in der Meldung ist die erwartete Spiegelregel.
- B12: bei der Abwicklung kein „Optionen der Abwicklung ließen sich nicht setzen.“, kein „Abwicklung: Die feste Fläche
  ist nach dem Setzen nicht die gespiegelte.“ B16: beim U-Profil „Abwicklung geprüft“, kein „Abwicklung weicht ab –
  K-Faktor/Biegezugabe/feste Fläche prüfen.“, kein „Eigene Biegezugabe … nicht übertragbar …“.
- B11 bestanden (mit Prüfung): bei der Abwicklung „Abwicklung geprüft“, keine Meldung zum Falten; danach im Original
  alle Körper abgewickelt wie vorher, der Schnitt wieder da (Prüftabelle: „Features hinter der Abwicklung“, „Gewählte
  Körper und Falten“). Nicht bestanden: „Die Abwicklung „…“ ließ sich für den Lauf nicht zurückfalten. …“ oder „… ließ
  sich im Original nicht wieder abwickeln …“; dazu ein Bildschirmfoto des Baums.
- **Baut G1 keine Basislasche (B1 nicht bestanden): Sitzung 2 hier beenden und zurückschicken.** Scheitert nur B11:
  weiter, nur mit gefaltet gespeicherten Teilen.

**G2 – B2, B14/B15 (Tor für S1b).** Teile: Platte mit Lasche (ein Körper) an x = 0, damit die Kopie das Original
berührt; Zweikörperteil aus zwei Basislaschen verschiedener Dicke, die erste mit eigener (überschriebener) Dicke, die
zweite berührt die erste („Ergebnis verschmelzen“ aus). Vorher notieren: Dicke, Radius, K-Faktor (Einkörperteil an
„Blech1“, Feature bearbeiten; Zweikörperteil im Ordner „Blech“ und an den Blech-Features), Masse des Originals (*Extras
› Auswerten › Masseneigenschaften*, nur der Originalkörper gewählt). Wahl: Platte „Im selben Teil“, gefaltet und
abgewickelt gespeichert; Zweikörperteil neues Teil, ganz und nur der erste Körper.
- Bestanden: Im Plattenteil erscheint der Ordner „Blech“ (erwartet), seine Werte und die von „Blech1“ wie notiert; Masse
  des Originals unverändert, gefaltet bzw. abgewickelt wie vorher; „Blech im selben Teil: Original und Kopie teilen …“;
  im neuen Teil beide Körper mit ihren Dicken. B2: die zweite Basislasche „Angelegt über Datenobjekt.“, zwei Körper;
  „… über InsertSheetMetalBaseFlange.“ oder „… InsertSheetMetalBaseFlange2.“ heißt, das Datenobjekt hat verschmolzen
  (Befund).
- Nicht bestanden: „Die Blech-Standardwerte hatten sich … geändert und wurden wiederhergestellt (…)“ (der Wächter
  griff), „Achtung: … ließen sich nicht wiederherstellen …“, „… frühere Körper sind danach aber nicht wie vorher.“, „Im
  selben Teil nicht angelegt: …“ oder andere Werte. Bittet die Meldung, „mit Rückgängig zu prüfen“: Strg+Z einzeln,
  jedes Mal Baum und Werte ansehen, bis die Basislasche der Kopie weg ist; notieren, wie oft und ob die Werte zurück
  sind (Prüftabelle: „Rückgängig nach Abbruch“). Zurück: Text, Werte vorher/nachher, Bildschirmfoto des Ordners „Blech“.

**G3 – B28/B29 (Tor für S3).** Teil: Kasten aus einer Basislasche 100 × 60 × 2 mm mit vier Kantenlaschen zu 90° – einmal
eine Lasche über vier Kanten, einmal vier einzelne; dazu eine Kantenlasche zu 60°. Wahl: *Rechts* und *Vorne*.
- Bestanden: jede Kantenlasche OK mit „Profilebene mit Winkel …° und FlipDir ja/nein (Kandidat i von n).“ und „Profile
  geprüft: …“; B28 mit „Angelegt über Datenobjekt (CreateDefinition, AddEdges, CreateFeature).“, ein anderer Weg
  („… InsertSheetMetalEdgeFlange2.“ oder „… InsertSheetMetalEdgeFlange (eine Kante).“) ist ein Befund. Nicht bestanden:
  „Keine passende Profilebene für die Kantenlasche (…)“; die Kandidat-Zeilen (Winkel, FlipDir) sind der Befund.

**G4 – B36 (Tor für die Kröpfung, S4).** Teil: Basislasche 100 × 50 × 2 mm auf *Vorne*, eine Ecke im Ursprung (x 0…100),
skizzierte Biegung auf der großen Fläche zum Betrachter (Linie bei x = 50 mm, feste Seite x < 50, 90°, eigener K 0,3);
dazu eine Platte mit Kröpfung. Wahl: *Rechts* und *Vorne*, Prüfung **aus** (Zeugenpunkte entscheiden).
- Bestanden: „Auf der gespiegelten Fläche skizziert, auf der das Original liegt.“, die Biegung „Durch … Zeugenpunkte
  bestätigt: …“, K 0,3 zurückgelesen, die Kröpfung OK. Nicht bestanden: Kröpfung „Die Skizze „…“ der Kröpfung liegt im
  Ziel nicht auf der gespiegelten Fläche …“ oder „SOLIDWORKS hat die Kröpfung nicht erzeugt.“; Biegung „… sondern auf
  einer Ebene (…); ob SOLIDWORKS das annimmt, …“ (B36) – dann ein Bildschirmfoto, ob sie trotzdem richtig dasteht.

**G8 – B67 (Tor für den Weg, S9).** Teil: Platte 120 × 80 × 2 mm mit einer Lamelle (louver) aus der
Konstruktionsbibliothek, drei Punkte in einer Positionsskizze, zu den Kanten bemaßt.
- Bestanden: OK mit „Seite und Drehung geprüft (… Punkte der Form an 3 Stellen).“, und die Zeile „Mit der gewählten
  Positionsskizze ließ sich das Werkzeug nicht einfügen; …“ fehlt – steht sie da (auch bei OK), ist B67 nicht bestanden.
  Auf 2026 außerdem kein „Am Feature gedreht passte das Werkzeug nicht …“ (B69).

**G5 – B44, B49 (S5).** Teil: Kasten wie G3 (vier einzelne Laschen) mit geschlossenen Ecken (stumpf, überlappend),
gebrochener Ecke, Abwickeln zweier Biegungen → Schnitt über eine Biegung → Zurückbiegen, Querbruch im Boden,
Eckentlastung an zwei Ecken.
- B44: Erwartet `CornerFeat`, `BreakCorner`, `UnFold`, `Fold`, `CrossBreak`; den Typ der Eckentlastung abschreiben (Name
  unbestätigt). Nicht bestanden: „Feature-Typ <Name> wird nicht unterstützt.“ oder eine Ablehnung mit anderem Namen.
  B49: Eckentlastung OK; „Eckentlastung über den älteren Aufruf angelegt …“ heißt, der neuere Weg ging nicht.

**G6 – B51, B52 (S6).** Teile: Platte 100 × 50 × 2 mm mit einem normalen Schnitt (eine Flächengruppe; ein zweiter mit
zwei); U-Kanal: Rechteck 100 × 50 mm auf *Oben*, 30 mm extrudiert, Wandung 2 mm (entfernt: Deckfläche und beide
50-mm-Stirnflächen), dann „Biegungen einfügen“ (feste Fläche der Boden, K 0,44, Entlastung 0,5) ohne Risskanten, einmal
mit unterdrücktem `ProcessBends` gespeichert. Vor dem Lauf kein „Riss“ im Baum (ein geschlossener Kasten braucht Risse
und wird abgelehnt). Wahl: *Rechts*; den U-Kanal auch „Im selben Teil“.
- B51 bestanden: der normale Schnitt OK, und keine Zeile „Normaler Schnitt angelegt über …“ (nur wenn das Datenobjekt
  scheiterte); „… über AddSMNormalCut.“ oder „… über AddSMNormalCut in der Reihenfolge der Hilfe.“ ist der Befund. Ganz
  gescheitert: „SOLIDWORKS hat den normalen Schnitt nicht mit den gespiegelten Flächengruppen angelegt (…)“.
- B52: In der Kopie entstehen `SheetMetal`, `FlattenBends` und `ProcessBends`, die Einträge sind OK, die Kopie steht im
  selben Zustand wie das Original. Bildschirmfoto des Baums. (B52 läuft auch im Makro B57 mit.)

**G7 – B64–B66 (S8).** Teil: Zweikörperteil aus zwei Basislaschen (2 mm), rechtwinklig aneinander, mit Lasche und
Schlitz (drei Laschen gleichmäßig, D1 = 10 mm, D2 = 25 mm, ohne Punkte); L-Blech mit zwei Versteifungsrippen auf
derselben Biegung ohne Bezugspunkt (30 mm, dann 20 mm weiter; 10 mm breit, 3 mm dick). Wahl: *Rechts* und *Vorne*.
- B64: Lasche und Schlitz OK, „Schlitz-Optionen: Standard (geprüft)“, der Schlitz heißt wie im Original; Typnamen
  abschreiben (unbestätigt). Nicht bestanden: ein Fehler mit „kein Datenobjekt (CreateDefinition(88))“ in den Details.
- B65: „Lage auf der Biegung geprüft.“ oder „Lage auf der Biegung geprüft: <Variante>.“ – die Variante sagt, von welcher
  Kante die zweite Rippe zählt. B66 nebenbei: eine Rippe mit Bezugspunkt und Profilmaßen.

### 4.2 Übrige Rauchtests je Schritt (spätere Sitzung)

Erst nach Auswertung von 4.1. Kurzform; Bau und Erwartung ausführlich im jeweiligen STOLPERSTEINE-Absatz.

| Schritt | Testteil (Auswahl) | Wahl | Worauf achten |
| --- | --- | --- | --- |
| S1a | Lasche auf einem Körper mit abweichender Dicke; offene Basislasche „bis Fläche“ zu einer parallelen Fläche eines nicht gewählten Körpers | *Rechts*, *Vorne* | Weg der Basislasche, „Abwicklung geprüft“; bei „bis Fläche“ „ersetzt durch Blind“ |
| S1b | Einkörper-Original; Spiegelebene auf einer Fläche; nur die Lasche gewählt; „Original löschen“; Gleichung auf der Dicke; Blechwerte aus dem Werkstoff | selbes Teil | Original unverändert; „Original löschen“ abgelehnt; Werkstoffwerte bleiben werkstoffgesteuert |
| S2 | Platte mit Säumen (geschlossen 10, offen 8/1,5, gerollt, tropfenförmig 200°), Gehrungslaschen über 1 und 3 Kanten | Prüfung an und aus | „Entlastung: Standard (geprüft)“, keine Hinweise auf Werte oder Biegungen |
| S3 | 60° auf andersherum laufender Kante, Versatz 3 mm, „bis Fläche“, K 0,38; verkürztes Profil mit Ausschnitt | auch selbes Teil | wie G3, keine Hinweise |
| S4 | U-Profil aus zwei Linien; Kröpfung blind und „bis Fläche“; Bohrung auf der beweglichen Seite (selbes Teil, nur die Biegung); feste Fläche in der Biegezone | Prüfung aus | „Zeugenpunkte nur auf den eigenen Flächen …“, kein „weicht ab“ |
| S5 | wie G5 | selbes Teil, Prüfung an und aus | gleiche Features an gespiegelter Lage |
| S6 | Schnitt schräg zur Blechebene; am U-Kanal aus G6 (ohne Risse) nach `ProcessBends` Schnitt, normaler Schnitt, Verrundung an einer Biegekante, mit unterdrücktem `ProcessBends` gespeichert; Schnitt zwischen `FlattenBends` und `ProcessBends` | *Rechts*, *Vorne* | alle drei gespiegelt und einzeln wieder unterdrückt; der letzte Fall wird abgelehnt |
| S8 | Lasche mit Startpunkt, „Versatz von Fläche“ umgekehrt; Teil ohne Blech mit Lasche und Schlitz; `tab_and_slot.sldprt` der Hilfe | auch selbes Teil | D1/D2 wo nötig getauscht |
| S9 | Sicke 90°, Prägung, Durchzug, Lamelle umgekehrt; Werkzeug mit zwei Konfigurationen; verknüpft/nicht verknüpft; Prägung mit eigener Anzeige in der Abwicklung | auch selbes Teil | „Seite und Drehung geprüft“, „Anzeige in der Abwicklung wie im Original …“; die nicht verknüpfte bleibt unverknüpft |

## 5. Was ich mit den Ergebnissen mache

1. **Rohtexte** nach `scripts/livetests/ergebnisse/<Datum>-<Tests>-sw2026.txt`, je Test ein Abschnitt in `livetests.md`
   wie bei B54/B55, in der Prüftabelle jede geprüfte Stelle mit Ergebnis und Version (B54/B55 eingeschlossen;
   `feat-blech` ist seit `629ed6c` eingefügt). Rauschen bei „weicht ab“ (B54: 2,3·10⁻⁶ relativ) ändert
   `MassComparison.NoiseRelative` (Prüftabelle: „Genauigkeit der Masseneigenschaften“).
2. **Ein gescheitertes Tor setzt den Typ zurück**, wie `STAND.md` und `blech-plan.md` §5.1/§5.2 sagen (Tabelle in 4.1):
   B14 → Blech im selben Teil wieder abgelehnt; B29 → `EdgeFlange` „noch nicht“; B36 → Kröpfung „nicht unterstützt“; B67
   → nur Variante A; B1 ohne einen Weg → S1 blockiert. Danach `STAND.md`, README „Unterstützte Features“ und Tests
   nachziehen, neues MSI aus der CI, den betroffenen Rauchtest wiederholen.
3. **B57** wählt den Weg von S7a (A2, dann B, dann F3); ohne A und B bleibt nur F3, und S7 ist neu abzuwägen
   (`STAND.md`, „Reihenfolge beim Fortsetzen“, Punkt 1).
4. **Fehler, die keine Annahme betreffen** (Absturz, falsche Geometrie trotz „OK“): Core-Test, Korrektur mit Prüfung und
   Nachprüfung wie bei jedem Schritt. Bestanden heißt nur auf 2026; 2019 und 2024 bleiben offen. Tag nur nach Rückfrage.
