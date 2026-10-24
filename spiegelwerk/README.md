# Spiegelwerk

SOLIDWORKS-Add-In, das Bauteile **unabhängig** spiegelt. Statt eines Spiegel-Features (Körper oder Features spiegeln) baut Spiegelwerk jedes Feature des Originals gespiegelt neu auf: neue Skizzen mit umgerechneten Beziehungen und Bemaßungen, neue Extrusionen, Schnitte, Rotationen, Verrundungen und Fasen. Das Ergebnis ist ein eigener, vollständig bearbeitbarer Feature-Baum ohne Verknüpfung zum Original.

Spiegelwerk ist eine eigenständige Entwicklung und steht in keiner Verbindung zu Dassault Systèmes; SOLIDWORKS ist eine Marke von Dassault Systèmes.

## Was es tut

| Kontext | Ergebnis |
| --- | --- |
| **Teil → neues Teil** | Gegenstück in einem neuen Dokument. Feature-Namen und Bemaßungsnamen bleiben erhalten, Gleichungen, Werkstoff und Einheitensystem werden übernommen. |
| **Teil → selbes Teil** | Die gespiegelten Features werden im selben Teil angelegt. Ganzes Teil oder gewählte Körper: eigener Körper, Schnitte wirken nur auf ihn. Nur gewählte Features (z. B. eine Tasche): sie wirken auf die vorhandenen Körper. Optional wird das Original danach gelöscht. |
| **Baugruppe** | Gewählte Komponenten werden als Gegenstück neu aufgebaut, neben dem Original gespeichert (`Name_gespiegelt.SLDPRT`) und an der gespiegelten Lage eingefügt. |

Nach jedem Lauf zeigt ein Bericht pro Feature, ob es neu aufgebaut wurde, was angepasst werden musste und ob **Volumen und Schwerpunkt** der Änderung mit dem gespiegelten Original übereinstimmen. Unterdrückte und im Original fehlerhafte Features werden übersprungen und genannt.

## Unterstützte Features

| Feature | Stand |
| --- | --- |
| 2D-Skizze | Linien, Mittellinien, Bögen, Kreise, Ellipsen, Ellipsenbögen, Splines (über die Stützpunkte), Parabeln, Punkte, Konstruktionsgeometrie. Beziehungen und Bemaßungen werden umgerechnet (siehe [Skizzen spiegeln](docs/SKIZZEN.md)). |
| Linear austragen (Aufsatz/Schnitt) | Alle Endbedingungen, beide Richtungen, Formschräge, Startbedingungen, eigene Extrusionsrichtung, ausgewählte Konturen, dünnwandig. Bezüge (bis Fläche, bis Eckpunkt, Abstand von Fläche) werden im Ziel wiedergefunden. |
| Rotieren (Aufsatz/Schnitt) | Eine Richtung, Mittelebene, zwei Richtungen, ausgewählte Konturen, dünnwandig. Achse: Skizzenlinie oder gerade Kante. |
| Verrundung | Konstanter Radius, auch mit mehreren Radien, Kanten und Flächen. |
| Fase | Winkel-Abstand, Abstand-Abstand, gleicher Abstand. |
| Referenzebene | Neu an gespiegelter Lage, nur von den Standardebenen abhängig. |
| Noch nicht | Austragung (Sweep), Ausformung (Loft), Bohrungsassistent, Muster, Wandung, Formschräge, variable Verrundung, 3D-Skizzen, Skizzentext. Sie erscheinen im Bericht; Features, die darauf aufbauen, können dann ebenfalls scheitern. Wie ein weiterer Typ dazukommt, steht in der [Architektur](docs/ARCHITEKTUR.md#einen-feature-typ-ergänzen). |

## Bedienung

Befehl **Unabhängig spiegeln** (Registerkarte *Spiegelwerk*, Menü und Symbolleiste) in Teilen und Baugruppen:

1. **Spiegelebene:** Referenzebene oder ebene Fläche wählen – oder ohne Auswahl eine temporäre Ebene (YZ, XZ, XY mit Abstand), die nicht im Modell angelegt wird.
2. **Zu spiegelnde Elemente:** Features, Skizzen oder Körper; leer lassen für das ganze Teil. In Baugruppen: Komponenten.
3. **Ergebnis:** neues Teil oder selbes Teil, Original löschen, Werkstoff und Gleichungen übernehmen, Namenszusatz.
4. **Prüfung und Bezüge:** jeden Schritt prüfen, fehlende Skizzenbezüge durch fixierte Hilfsgeometrie ersetzen oder weglassen, beim ersten Fehler abbrechen.

Die Rückrollleiste muss am Ende des Feature-Baums stehen.

## Bauen und installieren

Voraussetzungen: Windows, SOLIDWORKS 2019 oder neuer, .NET Framework 4.8, Visual Studio 2022 oder .NET SDK 8.

```bat
cd spiegelwerk
dotnet build src\Spiegelwerk\Spiegelwerk.csproj -c Release
rem als Administrator:
scripts\install.cmd
```

Die Interop-Assemblys werden aus `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist` referenziert; ein anderer Installationsort: `-p:SolidWorksApiDir=...`. `install.cmd` registriert die DLL mit dem 64-Bit-RegAsm (`/codebase`); dabei trägt das Add-In sich unter `HKLM\SOFTWARE\SolidWorks\Addins` ein. Nach einem Neustart steht es unter *Extras › Zusatzanwendungen*. Entfernen: `scripts\uninstall.cmd`.

Tests der Spiegelmathematik laufen auf jedem System:

```sh
dotnet test spiegelwerk/tests/Spiegelwerk.Core.Tests
```

## Aufbau

```
spiegelwerk/
  src/Spiegelwerk.Core/        ohne SOLIDWORKS, getestet: Geometrie, Skizzenmodell und -spiegelung,
                               Richtungsregeln, Ebenenwahl, Platzierung in Baugruppen, Topologie-Zuordnung,
                               Volumen-/Schwerpunktvergleich, Feature-Datensätze, Bericht
  src/Spiegelwerk/             das COM-Add-In (.NET Framework 4.8, x64)
    Addin/                     ISwAddin, Registrierung, Befehle, Symbol
    UI/                        PropertyManager-Seite, Ergebnisfenster
    Capture/                   Original lesen: Feature-Auswahl, Skizzen, Feature-Daten, Mengenmessung
    Build/                     Ziel aufbauen: Ebenen, Kanten/Flächen wiederfinden, Skizzen, Builder je Feature-Typ, Prüfung
    AssemblyContext/           Komponenten spiegeln, speichern, einfügen
  tests/Spiegelwerk.Core.Tests xUnit
  docs/                        Architektur, Skizzen spiegeln, Stolpersteine
```

- [Architektur](docs/ARCHITEKTUR.md) – Ablauf, Klassen, Mathematik, Erweiterung
- [Skizzen spiegeln](docs/SKIZZEN.md) – Koordinaten, Bögen, Beziehungen, Bemaßungen
- [Stolpersteine](docs/STOLPERSTEINE.md) – was bei der programmgesteuerten Neuerzeugung in SOLIDWORKS schiefgeht

## Stand

Version 0.1. Die Kernlogik ist durch Tests abgesichert; das Add-In übersetzt fehlerfrei gegen die API von SOLIDWORKS 2019 und 2024 (CI) und ist durch ein Code-Review gegangen, dessen bestätigte Befunde behoben sind. In einer laufenden SOLIDWORKS-Sitzung ist es noch nicht erprobt. Die Stellen, an denen sich die API je nach Version anders verhalten kann, misst Spiegelwerk nach, statt sich auf eine Annahme zu verlassen; sie sind in den [Stolpersteinen](docs/STOLPERSTEINE.md#was-beim-ersten-test-in-solidworks-zu-prüfen-ist) aufgeführt.
