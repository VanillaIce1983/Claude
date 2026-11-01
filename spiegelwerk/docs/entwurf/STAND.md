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

## In Arbeit

| Thema | Entwurf | Gegenprüfung | Stand |
| --- | --- | --- | --- |
| Spiegel-Features (Features/Körper spiegeln ohne Spiegel-API) | `spiegel-features.md` | `spiegel-features-gegenpruefung.md` | Umsetzung läuft (Zweig `feat-spiegel-features`) |
| Skizzenbereiche bei Extrusion/Rotation | `skizzenbereiche.md` | `skizzenbereiche-gegenpruefung.md` | Umsetzung läuft (Zweig `feat-skizzenbereiche`) |
| Blech, Bereich 1–4 | `blech-*.md` | fehlt noch | Blechteile werden bis dahin vor dem Aufbau abgelehnt (`UnsupportedFeatures.RefusesRun`) |

## Reihenfolge beim Fortsetzen

1. Spiegel-Features und Skizzenbereiche fertigstellen, unabhängig prüfen, Befunde beheben, einfügen.
2. Gegenprüfung der Blech-Entwürfe, Integrationsplan, dann Blech in prüfbaren Schritten umsetzen.
3. Doku, Version, Regressionsprüfung, Release (Tag nur nach Rückfrage).
