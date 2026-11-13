# Live-Tests in SOLIDWORKS

Ergebnisse der Tests, die der Eigentümer in einer laufenden SOLIDWORKS-Sitzung gemacht hat. Die Annahmen stehen in
`blech-plan.md` §5 und in STOLPERSTEINE unter „Was beim ersten Test in SOLIDWORKS zu prüfen ist“; was hier steht, wird
dort nachgetragen, sobald der Zweig `feat-blech` (S5, S6) eingefügt ist.

## B54/B55 – Spiegelung einer Körperkopie und Einfügen (Tor für S7, Ersatzweg)

- **Datum:** 13.11.2026
- **Version:** SOLIDWORKS 2026 (Revision 34.4.0), Vorlage `SOLIDWORKS 2026\templates\Teil.prtdot`
- **Makro:** `scripts/livetests/ersatzweg-b54-b55.bas` (Stand `90635f0`)
- **Rohprotokoll:** `scripts/livetests/ergebnisse/2026-11-13-b54-b55-sw2026.txt`
- **Testteil:** ein echtes Blechteil des Eigentümers. Es hat einen Körper mit 988 Eckpunkten und ist zu keiner Ebene
  durch den Schwerpunkt spiegelsymmetrisch.
- **Spiegelebenen:**
  - schräge Ebene mit der Normalen (1; 2; 3)/√14, 198 mm vor dem Körper (B54, B55 getrennt, neues Teil);
  - x = xMin durch den Extrempunkt (B55 anliegend). In ihr liegen 24 Eckpunkte des Originals, also Flächenkontakt.

| Test | Ergebnis | Befund |
| --- | --- | --- |
| B54 direkt (Householder-Matrix, Determinante −1, Skalierung 1) | **bestanden** | `CreateTransform` übernimmt die Matrix unverändert, `ApplyTransform` meldet True. Alle 988 Eckpunkte liegen auf den gespiegelten des Originals (Abweichung unter 0,05 µm). Schwerpunkt 0,2 µm, Volumen relativ 2,3·10⁻⁶, `Check3` ohne Fehler |
| B54 als (−M, Skalierung −1) | **nicht bestanden** | `CreateTransform` setzt die Skalierung **stillschweigend auf +1** und behält −M: Das Ergebnis ist eine Drehung (Determinante +1), keine Spiegelung. `ApplyTransform` meldet trotzdem True, `Check3` ist ohne Fehler, das Volumen stimmt; nur Schwerpunkt und Eckpunkte liegen 337 bzw. 351 mm daneben |
| B55 selbes Teil, getrennt | **bestanden** | `CreateFeatureFromBody3(Kopie, False, swCreateFeatureBodyCheck)` legt das Feature „Importiert1“ an (Typ `BaseBody`, Fehlercode 0). Daraus wird ein eigener Körper (1 → 2), kein Blech. Die Geometrie ist wie bei B54, es gibt keine externen Bezüge, und das Original bleibt unverändert |
| B55 selbes Teil, am Original anliegend | **bestanden** | Eigener Körper (2 → 3), **nicht** mit dem Original verschmolzen. Geometrie exakt (Schwerpunkt 0,0 µm, Volumen 6,4·10⁻⁷), Original unverändert |
| B55 neues Teil | **bestanden** | Die Kopie entsteht im Testteil und wird in ein neues Teil eingefügt: ein Körper, Geometrie stimmt, keine externen Bezüge. Das stützt auch B56: Eine temporäre Kopie aus einem Dokument lässt sich in einem anderen einfügen |
| Temporäre Kopie nach dem Einfügen | **nicht verbraucht** | In allen drei Fällen meldet `IsTemporaryBody` noch True, und `Copy2` geht |

### Folgen

- **Das Tor B54/B55 ist offen.** S7 ist machbar; ob er gebaut wird, entscheidet der Eigentümer (`blech-plan.md` §7).
- **Nur die direkte Schreibweise verwenden.** Die zweite Schreibweise (−M, Skalierung −1), die die Gegenprüfung des Ersatzwegs als Rückfall vorsieht
  („direct array first; (−M, scale −1) if the five probe points do not map“), fällt weg.
  Sie scheitert nicht laut: `CreateTransform` macht ohne Fehlermeldung eine Drehung daraus.
- **Jede Transformation vor Gebrauch an Prüfpunkten bestätigen.** Das schreibt der Entwurf schon vor; nach diesem
  Befund ist es Pflicht. Geprüft ist nur 2026, der Plan verlangt auch 2019 und 2024. Fällt die Prüfung durch, gibt es
  keinen Ersatzweg, und die Ablehnung bleibt.
- **Der importierte Körper ist kein Blech** (Typ `BaseBody`). Das Zurück-zu-Blech über Umwandeln oder Biegungen einfügen
  (B57) ist das nächste Tor.
- **Eine frische `Copy2` vor jeder Verwendung** (P7) bleibt, obwohl sie auf 2026 nicht nötig wäre: Sie kostet wenig und
  hängt nicht an einem undokumentierten Verhalten.
- **Genauigkeit der Masseeigenschaften.** Gespiegelte Kopie und Original weichen im Volumen um bis zu 2,3·10⁻⁶ relativ
  voneinander ab. Das liegt unter `Tol.Relative` (10⁻⁵) und weit unter der Volumentoleranz von `MassComparison`
  (10⁻⁴), aber nur einen Faktor 4 unter `Tol.Relative`. Jeder Vergleich von Volumina an gespiegelten Körpern braucht
  daher mindestens `Tol.Relative`.
