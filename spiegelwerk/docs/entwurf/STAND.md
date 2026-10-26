# Zwischenstand (pausiert am 26.10.2026)

Dieser Zweig (`claude/spiegelwerk-installer-wip`) sichert Arbeit, die noch nicht im Hauptzweig
`claude/cool-ritchie-ltfl07` ist. Er baut absichtlich noch nicht vollständig (siehe unten); der
Commit trägt `[skip ci]`.

## Im Hauptzweig (fertig, CI grün)

- `9df9ea1`: Befunde der Gesamtprüfung behoben (Original löschen, Schrittprüfung, Aufräumen,
  Gleichungen, Oberfläche, Bericht, Fortschritt/Esc, 246 Tests).
- Noch offen dazu: unabhängige Prüfung dieses Commits (war gestartet, angehalten) und die Doku
  der Änderungen. Was die Doku sagen muss, steht je Gruppe unter `doc_notes` in
  `korrekturen-9df9ea1.json`.

## In diesem Zweig

- Installer (`installer/`): WiX 5 (MS-RL), ein MSI für SOLIDWORKS Desktop und 3DEXPERIENCE;
  CI-Job baut, installiert, erzeugt per COM, deinstalliert; Release bei Tag `spiegelwerk-v*`.
  Entwurf und Quellen: `installer.md`.
- `LICENSE` (Alle Rechte vorbehalten), `Directory.Build.props` (Version 0.11.0).
- `install.cmd`/`uninstall.cmd` überarbeitet.
- Begonnen: Interop-Typen einbetten (`Spiegelwerk.csproj`, No-PIA). Es fehlen die ausdrücklichen
  `(object)`-Casts an 109 Stellen (Liste: Build-Fehler CS0656/CS1977/CS1976/CS8133); bis dahin
  übersetzt das Add-In in diesem Zweig nicht, und der COM-Test des Installer-Jobs braucht genau das.

## Entwürfe (gegen Code und Interop geprüft, noch nicht umgesetzt)

| Datei | Inhalt | Gegenprüfung |
| --- | --- | --- |
| `spiegel-features.md` | Spiegel-Features (Features/Körper spiegeln) ohne Spiegel-API: zweite Hälfte = gespiegelte Saat im Ziel, an R_Π(P) noch einmal gespiegelt | `spiegel-features-gegenpruefung.md` |
| `skizzenbereiche.md` | gewählte Skizzenbereiche bei Extrusion/Rotation | `skizzenbereiche-gegenpruefung.md` |
| `blech-grundlagen.md` | Blech-Definition, Basislasche, Abwicklung, Schnitte | fehlt (Sitzungslimit) |
| `blech-laschen.md` | Kantenlasche, Gehrungslasche, Saum, Versatz | fehlt |
| `blech-biegungen-ecken.md` | skizzierte Biegung, Ecken, Falten/Abwickeln | fehlt |
| `blech-umformwerkzeuge-ersatzweg.md` | Umformwerkzeuge, Lasche/Schlitz, Versteifung, Lüftung, Ersatzweg (gespiegelter Körper + Biegungen einfügen) | fehlt |

`offene-punkte.json`: alle 74 Befunde der Gesamtprüfung (Funktionsumfang, Doku, Code, Werkzeuge).

## Reihenfolge beim Fortsetzen

1. Prüfung von `9df9ea1` nachholen, Befunde beheben.
2. Casts für No-PIA ergänzen, diesen Zweig in den Hauptzweig einfügen (Testprojekt auf .NET 10
   bzw. zusätzlich net48 unter Windows umstellen), CI mit Installer-Job grün.
3. Spiegel-Features und Skizzenbereiche nach den geprüften Entwürfen umsetzen.
4. Gegenprüfung der Blech-Entwürfe nachholen, dann Blech umsetzen.
5. Doku für alles, Version 0.11.0, Regressionsprüfung, Release (Tag nur nach Rückfrage).
