using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Corners and the cross break (A3 (2) §5.2–§5.5, blech-plan S5): the selections a closed corner is
    /// tried with, overlap ↔ underlap, the read-back comparisons, the lengths of the 2019 corner-relief
    /// call, the pairing of corners and break lines as sets, the texts. A reflection keeps every value;
    /// it maps the faces and edges in their roles.
    /// </summary>
    public static class CornerRules
    {
        /// <summary>swFeatureNameID_e.swFmCornerRelief: the data object of a corner relief (2022 and later).</summary>
        public const int CornerReliefId = 120;

        /// <summary>The mark of a „face to match“ in the official closed-corner example (B45, not confirmed).</summary>
        public const int MatchMark = 0x40000000;

        // ---- closed corner

        /// <summary>Overlap ↔ underlap, butt stays: the variant offered to the check (the roles should keep the type, A3 §6).</summary>
        public static int SwapOverlap(int cornerType) => cornerType switch
        {
            ClosedCornerTypes.Overlap => ClosedCornerTypes.Underlap,
            ClosedCornerTypes.Underlap => ClosedCornerTypes.Overlap,
            _ => cornerType,
        };

        /// <summary>The overlap ratio means something only for overlap and underlap.</summary>
        public static bool HasOverlapRatio(int cornerType) => cornerType == ClosedCornerTypes.Overlap || cornerType == ClosedCornerTypes.Underlap;

        /// <summary>
        /// The selections a closed corner is tried with, in order (A3 §5.3, B45): (a) every face with mark 1
        /// (the user interface records 1, CADapter measured 0 and 1), (b) faces alternately with 1 and
        /// <see cref="MatchMark"/> (the official example), (c) every face with mark 0. The read-back of the
        /// faces decides which one SOLIDWORKS understood.
        /// </summary>
        public static IReadOnlyList<(string description, int[] marks)> ClosedCornerSelections(int faces)
        {
            int[] All(int mark) => Enumerable.Repeat(mark, faces).ToArray();
            var result = new List<(string, int[])> { ("Flächen mit Markierung 1", All(1)) };
            if (faces >= 2) result.Add(("Flächen abwechselnd zu verlängern und anzupassen", Enumerable.Range(0, faces).Select(i => i % 2 == 0 ? 1 : MatchMark).ToArray()));
            result.Add(("Flächen mit Markierung 0", All(0)));
            return result;
        }

        /// <summary>The closed corner as read back against the original's values (faces are compared as a set on their own).</summary>
        public static Deviations Differences(ClosedCornerRecord want, ClosedCornerRecord got, IFormatProvider? culture = null)
        {
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Eckentyp", want.CornerType, got.CornerType, ScalarKind.Choice),
                ("Spalt", want.Gap, got.Gap, ScalarKind.Length),
                ("Biegebereich öffnen", want.OpenBendRegion ? 1 : 0, got.OpenBendRegion ? 1 : 0, ScalarKind.Flag),
            };
            if (HasOverlapRatio(want.CornerType)) values.Add(("Überlappungsverhältnis", want.OverlapRatio, got.OverlapRatio, ScalarKind.Factor));
            var d = new Deviations();
            d.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            return d;
        }

        public static string Describe(ClosedCornerRecord r)
        {
            string kind = r.CornerType switch
            {
                ClosedCornerTypes.Overlap => "überlappend",
                ClosedCornerTypes.Underlap => "unterlappend",
                _ => "stumpf",
            };
            return $"Geschlossene Ecke {kind}, {Count(r.Faces.Count, "Fläche", "Flächen")}";
        }

        public const string OverlapSwapped = "Überlappung ↔ Unterlappung";

        // ---- break corner

        /// <summary>The break corner as read back against the original's values.</summary>
        public static Deviations Differences(BreakCornerRecord want, BreakCornerRecord got, IFormatProvider? culture = null)
        {
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Art", want.BreakType, got.BreakType, ScalarKind.Choice),
                (want.BreakType == BreakCornerTypes.Fillet ? "Radius" : "Abstand", want.Distance, got.Distance, ScalarKind.Length),
                ("Nur innere Ecken", want.InternalCornersOnly ? 1 : 0, got.InternalCornersOnly ? 1 : 0, ScalarKind.Flag),
                ("Auf Biegelinien zentriert", want.CenteredOnBendLines ? 1 : 0, got.CenteredOnBendLines ? 1 : 0, ScalarKind.Flag),
            };
            var d = new Deviations();
            d.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            return d;
        }

        public static string Describe(BreakCornerRecord r, IFormatProvider? culture = null)
        {
            string kind = r.BreakType == BreakCornerTypes.Chamfer ? "Fase" : "Verrundung";
            var parts = new List<string>();
            if (r.Edges.Count > 0) parts.Add(Count(r.Edges.Count, "Kante", "Kanten"));
            if (r.Faces.Count > 0) parts.Add(Count(r.Faces.Count, "Fläche", "Flächen"));
            return $"Gebrochene Ecke {kind} {SheetMetalCompare.Mm(r.Distance, culture)}, {string.Join(", ", parts)}";
        }

        // ---- cross break

        /// <summary>The cross break as read back against the original's values.</summary>
        public static Deviations Differences(CrossBreakRecord want, CrossBreakRecord got, IFormatProvider? culture = null)
        {
            var values = new List<(string, double, double, ScalarKind)>
            {
                // No hinge that a reflection runs the other way: compared as it is.
                ("Bruchwinkel", want.Angle, got.Angle, ScalarKind.PlainAngle),
                ("Radius", want.Radius, got.Radius, ScalarKind.Length),
                ("Richtung umgekehrt", want.Reverse ? 1 : 0, got.Reverse ? 1 : 0, ScalarKind.Flag),
            };
            var d = new Deviations();
            d.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            return d;
        }

        /// <summary>The break lines of a cross-break sketch: its non-construction lines, model coordinates, from start to end.</summary>
        public static List<(Vec3 a, Vec3 b)> BreakLines(Sketch.SketchModel sketch)
        {
            var lines = new List<(Vec3, Vec3)>();
            foreach (Sketch.SkSegment s in sketch.Segments)
            {
                if (s.Kind != Sketch.SkSegmentKind.Line || s.Construction || s.StartId == null || s.EndId == null) continue;
                Vec3 a = sketch.ToModel(sketch.Point(s.StartId)), b = sketch.ToModel(sketch.Point(s.EndId));
                if (a.DistanceTo(b) > Tol.Length) lines.Add((a, b));
            }
            return lines;
        }

        /// <summary>
        /// Two sets of break lines as segments, each either way round and in any order (a reflection may
        /// reverse both): the same if each line has a partner whose ends lie within <paramref name="tol"/>.
        /// </summary>
        public static bool SameLines(IReadOnlyList<(Vec3 a, Vec3 b)> want, IReadOnlyList<(Vec3 a, Vec3 b)> got, double tol = Tol.Length) =>
            SetMatch.Pair(want.Count, got.Count, (i, j) =>
            {
                double same = want[i].a.DistanceTo(got[j].a) + want[i].b.DistanceTo(got[j].b);
                double swapped = want[i].a.DistanceTo(got[j].b) + want[i].b.DistanceTo(got[j].a);
                double d = Math.Min(same, swapped);
                return d <= 2 * tol ? d : double.PositiveInfinity;
            }) != null;

        public static string Describe(CrossBreakRecord r, IFormatProvider? culture = null) =>
            $"Querbruch {SheetMetalCompare.Deg(r.Angle, culture)}{(r.Lines.Count > 0 ? $", {Count(r.Lines.Count, "Bruchlinie", "Bruchlinien")}" : "")}";

        /// <summary>The cross break changes no solid: its direction follows the rule and cannot be measured (B48).</summary>
        public const string CrossBreakByRule = "Richtung nach Regel (Außennormale der Fläche, die die Spiegelung erhält); nicht messbar, denn der Querbruch ändert den Körper nicht.";

        /// <summary>The copy's break lines are not the mirrored ones of the original (edited there), and they could not be written.</summary>
        public static string LinesNotWritten(string why) =>
            $"Bruchlinien weichen von den gespiegelten des Originals ab (dort bearbeitet) und ließen sich nicht übertragen ({why}); sie bleiben, wie SOLIDWORKS sie anlegt.";

        // ---- corner relief

        /// <summary>
        /// The lengths of the 2019 call AddCornerReliefType (Length2, Length3) for a corner, or null for a
        /// relief type the help documents no lengths for (A3 P13): square – the length, with filleted
        /// corners their radius (half the diameter); obround – length and slot width; circular – the radius,
        /// taken as the slot width (the help calls it the radial slot width of a circular relief; the slot
        /// length is valid only for rectangular and obround ones). Radius or diameter is checked live (B49).
        /// </summary>
        public static (double length2, double length3)? LegacyReliefLengths(CornerReliefItem c) => c.ReliefType switch
        {
            CornerReliefTypes.SquareRelief => (c.SlotLength, c.AddFilletedCorners ? c.CornerFilletDiameter / 2 : 0),
            CornerReliefTypes.ObroundRelief => (c.SlotLength, c.SlotWidth),
            CornerReliefTypes.CircularRelief => (c.SlotWidth, 0),
            _ => null,
        };

        /// <summary>
        /// One corner as read back against the original's: type and flags, the sizes (ratios if they
        /// are ratios to the thickness), the suitcase type. <paramref name="legacy"/>: made by the 2019 call,
        /// which sets type, the two lengths and the flags it has – the rest is not compared.
        /// </summary>
        public static List<string> Differences(CornerReliefItem want, CornerReliefItem got, bool legacy, IFormatProvider? culture = null)
        {
            ScalarKind size = want.RatioToThickness ? ScalarKind.Factor : ScalarKind.Length;
            static double F(bool b) => b ? 1 : 0;
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Art", want.ReliefType, got.ReliefType, ScalarKind.Choice),
                ("Verhältnis zur Dicke", F(want.RatioToThickness), F(got.RatioToThickness), ScalarKind.Flag),
                ("Auf Biegelinien zentriert", F(want.CenterOnBendLines), F(got.CenterOnBendLines), ScalarKind.Flag),
                ("Tangential zur Biegung", F(want.TangentToBend), F(got.TangentToBend), ScalarKind.Flag),
            };
            if (!legacy || want.ReliefType == CornerReliefTypes.SquareRelief)
                values.Add(("Ecken verrunden", F(want.AddFilletedCorners), F(got.AddFilletedCorners), ScalarKind.Flag));
            if (!legacy)
            {
                values.Add(("Schlitzlänge", want.SlotLength, got.SlotLength, size));
                values.Add(("Schlitzbreite", want.SlotWidth, got.SlotWidth, size));
                values.Add(("Eckenverrundung (Durchmesser)", want.CornerFilletDiameter, got.CornerFilletDiameter, ScalarKind.Length));
                values.Add(("Kofferform", want.SuitCaseType, got.SuitCaseType, ScalarKind.Choice));
            }
            else
            {
                (double length2, double length3)? w = LegacyReliefLengths(want), g = LegacyReliefLengths(got);
                if (w.HasValue && g.HasValue)
                {
                    values.Add((want.ReliefType == CornerReliefTypes.CircularRelief ? "Radius" : "Länge", w.Value.length2, g.Value.length2, size));
                    if (want.ReliefType != CornerReliefTypes.CircularRelief) values.Add(("Breite bzw. Eckenradius", w.Value.length3, g.Value.length3, size));
                }
            }
            return SheetMetalCompare.Scalars(values, culture);
        }

        /// <summary>
        /// The corners of the copy paired with the original's (mirrored) by their faces as sets (A3 §5.5):
        /// the order of the corners may differ. Null if one has no partner or the counts differ.
        /// <paramref name="distance"/>(corner j of the copy, its face k, point p) → distance of p to that face.
        /// </summary>
        public static int[]? PairCorners(IReadOnlyList<CornerReliefItem> want, IReadOnlyList<IReadOnlyList<FaceSignature>> got, Func<int, int, Vec3, double> distance,
            double tol = Tol.Length) =>
            SetMatch.Pair(want.Count, got.Count, (i, j) =>
            {
                List<FaceSignature> faces = want[i].Faces();
                Func<int, int, double> cost = (a, k) => FaceSets.Cost(faces[a], got[j][k], distance(j, k, faces[a].Sample), tol);
                int[]? pairs = SetMatch.Pair(faces.Count, got[j].Count, cost);
                return pairs == null ? double.PositiveInfinity : SetMatch.Total(pairs, cost);
            });

        public static string Describe(CornerReliefRecord r) =>
            $"Eckentlastung, {Count(r.Corners.Count, "Ecke", "Ecken")}{(r.CornerType == CornerReliefBendTypes.ThreeBend ? " mit drei Biegungen" : "")}";

        /// <summary>The corner relief was made by the 2019 call (no data object).</summary>
        public const string LegacyCornerRelief = "Eckentlastung über den älteren Aufruf angelegt (kein Datenobjekt): Kofferform und nicht dokumentierte Maße bleiben bei den Vorgaben von SOLIDWORKS.";

        /// <summary>A corner relief type the 2019 call does not document.</summary>
        public static string NotLegacy(int index, int reliefType) =>
            $"Ecke {index + 1} der Eckentlastung: Art {reliefType} lässt sich über den älteren Aufruf nicht anlegen.";

        /// <summary>Before SOLIDWORKS 2022 the API has no corner-relief data (by type name, which is not confirmed, B44).</summary>
        public const string CornerReliefTooOld = "Eckentlastung: erst ab SOLIDWORKS 2022 über die API lesbar – bitte im Ziel von Hand anlegen.";

        private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
    }
}
