# Zwischenstand (1.11.2026)

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
  Erfassen (noch ohne Leser), Spiegel-Features über Blech als nicht unterstützt, `SwLate.Invoke`/`InvokeOut`.
  Abweichend vom Plan: STOLPERSTEINE 139 aus dem Plan ist nach dem Einfügen der beiden Zweige 151; die neuen Zeilen
  stehen als 164–171 in einem eigenen Abschnitt. Die geschlossene Ecke (`CornerFeat`) steht schon in der Tabelle, die
  Eckentlastung nicht (Name unbestätigt, §1.5).

## In Arbeit

| Thema | Stand |
| --- | --- |
| Blech | nach `blech-plan.md` in prüfbaren Schritten; S0 steht (Zweig `feat-blech`), als Nächstes S1a; bis ein Typ seinen Schritt hat, lehnt `UnsupportedFeatures.RefusesBeforeCapture` den Lauf ab (`SheetMetalTypes`) |

## Reihenfolge beim Fortsetzen

1. Blech S0 prüfen und gegenprüfen, dann S1a (Basislasche, Abwicklung, neues Teil) usw. nach `blech-plan.md`;
   jeder Schritt mit Core-Tests, Prüfung und Gegenprüfung.
2. Erster Test in einer laufenden SOLIDWORKS-Sitzung: die Annahmen unter „Was beim ersten Test in SOLIDWORKS zu prüfen ist“
   (STOLPERSTEINE) und `blech-plan.md` §5.2.
3. Doku, Version, Regressionsprüfung, Release (Tag nur nach Rückfrage).
