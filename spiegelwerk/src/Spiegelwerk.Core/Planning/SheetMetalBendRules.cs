using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Sketched bend (A3 (2) §3, blech-plan S4): the creation code of its position, the first guess
    /// for its direction, the bend lines of its sketch, the witnesses that decide side and direction,
    /// how two variants compare, the read-back comparison and the report texts. A reflection keeps
    /// angle, radius, position and allowance; it maps the fixed face and the fixed point onto their
    /// mirror images. The direction flag is measured against a reference the help does not name
    /// (B39): the rule gives only the first guess, the witnesses decide.
    /// </summary>
    public static class SheetMetalBendRules
    {
        /// <summary>swFeatureNameID_e.swFmSketchBend and swFmSM3dBend: the data-object path, tried when the insert call makes nothing.</summary>
        public const int SketchBendId = 35, SM3dBendId = 36;

        /// <summary>
        /// The BendPos of InsertSheetMetal3dBend (0 centre line, 1 material inside, 2 material outside,
        /// 3 bend outside) for the PositionType read (swFlangePositionTypes_e: 4 is the centre line);
        /// null for a position the call cannot make (bend sharp, bend tangent and anything else).
        /// </summary>
        public static short? BendPosForCreation(int positionType) => positionType switch
        {
            4 => (short)0,
            1 => (short)1,
            2 => (short)2,
            3 => (short)3,
            _ => null,
        };

        /// <summary>
        /// The first guess for ReverseDirection (A3 P2): the reflection rule against the sheet's own
        /// orientation if that is known (σ of the sheet, B39 – not yet), otherwise against the bend
        /// sketch's normal (σ of the sketch; +1 on the mirrored face). Only the order of the variants
        /// depends on it: the witnesses decide.
        /// </summary>
        public static bool FirstReverseGuess(bool reverse, int? sigmaSheet, int sigmaSketch) => DirectionRules.ReverseFlag(reverse, sigmaSheet ?? sigmaSketch);

        /// <summary>
        /// The bend lines of a sketch: its non-construction lines in model coordinates, each through
        /// its midpoint along start → end. Anything else (arcs, points, construction) is no bend line.
        /// </summary>
        public static List<Line3> BendLines(SketchModel sketch)
        {
            var lines = new List<Line3>();
            foreach (SkSegment s in sketch.Segments)
            {
                if (s.Kind != SkSegmentKind.Line || s.Construction || s.StartId == null || s.EndId == null) continue;
                Vec3 a = sketch.ToModel(sketch.Point(s.StartId)), b = sketch.ToModel(sketch.Point(s.EndId));
                if (a.DistanceTo(b) <= Tol.Length) continue;
                lines.Add(new Line3((a + b) / 2, b - a));
            }
            return lines;
        }

        /// <summary>At most this many witnesses (A3 P2).</summary>
        public const int MaxWitnesses = 12;

        /// <summary>
        /// Witnesses from groups of points in their order of worth – the faces of the bend sub-features,
        /// the faces the feature made or moved, the faces turned against the fixed face: each group
        /// gets an equal share, spread over it, and what a short group leaves goes to the others in
        /// order. Points closer than <see cref="Tol.Length"/> to one taken count once.
        /// </summary>
        public static List<Vec3> PickWitnesses(IReadOnlyList<IReadOnlyList<Vec3>> groups, int max = MaxWitnesses)
        {
            var distinct = groups.Select(Distinct).ToList();
            var taken = new List<Vec3>();
            var used = distinct.Select(_ => new HashSet<int>()).ToList();
            int nonEmpty = distinct.Count(g => g.Count > 0);
            if (nonEmpty == 0 || max <= 0) return taken;
            int share = Math.Max(1, max / nonEmpty);
            for (int g = 0; g < distinct.Count; g++)
                foreach (int i in Spread(distinct[g].Count, Math.Min(share, max - taken.Count)))
                    if (Add(distinct[g][i])) used[g].Add(i);
            for (int g = 0; g < distinct.Count && taken.Count < max; g++)
                for (int i = 0; i < distinct[g].Count && taken.Count < max; i++)
                    if (!used[g].Contains(i) && Add(distinct[g][i])) used[g].Add(i);
            return taken;

            bool Add(Vec3 p)
            {
                if (taken.Count >= max || taken.Any(t => t.AlmostEquals(p, Tol.Length))) return false;
                taken.Add(p);
                return true;
            }
        }

        private static List<Vec3> Distinct(IReadOnlyList<Vec3> points)
        {
            var result = new List<Vec3>();
            foreach (Vec3 p in points)
                if (!result.Any(q => q.AlmostEquals(p, Tol.Length)))
                    result.Add(p);
            return result;
        }

        /// <summary><paramref name="n"/> indices spread evenly over 0 … count − 1.</summary>
        private static IEnumerable<int> Spread(int count, int n)
        {
            n = Math.Min(n, count);
            for (int k = 0; k < n; k++) yield return k * count / n;
        }

        /// <summary>
        /// The witnesses a build measures (K10), unmirrored: the points on the feature's own faces and those
        /// of its bends, and the points on the other faces of its body it moved – these only if every feature
        /// that made the body before it has its counterpart in the copy (<paramref name="rebuilt"/>): a hole
        /// the run does not rebuild, or one that failed, is missing there in every variant. No such features
        /// means they were not read (a sheet-metal body has its base feature before any bend): the moved ones
        /// are left out then too. At most <see cref="MaxWitnesses"/>; <paramref name="movedLeftOut"/> says
        /// whether moved ones were dropped. The fixed point is not among them: the builder adds it where it
        /// stayed on the original.
        /// </summary>
        public static List<Vec3> WitnessesFor(IReadOnlyList<Vec3> own, IReadOnlyList<Vec3> moved, IEnumerable<string> bodyFeatures, Func<string, bool> rebuilt,
            out bool movedLeftOut)
        {
            List<string> earlier = bodyFeatures.ToList();
            bool count = earlier.Count > 0 && earlier.All(rebuilt);
            movedLeftOut = !count && moved.Count > 0;
            return PickWitnesses(new[] { own, count ? moved : Array.Empty<Vec3>() });
        }

        /// <summary>
        /// What the witnesses say about the variant that is kept, measured on it after the allowance is
        /// settled – the best may have been made again, the allowance step may have made it once more, so
        /// the count from the search is not its count: a note (<paramref name="deviates"/>) if it misses
        /// any; nothing if it shows a rebuild error (the step reports that) or there are no witnesses;
        /// otherwise the confirmation – direction and side if the feature's own witnesses were there
        /// (<paramref name="decided"/>), only the side if the fixed point was the only one.
        /// </summary>
        public static (string? note, string? info) WitnessOutcome(int witnesses, int missed, bool error, bool decided, bool turned, bool otherSide,
            Func<int, int, string> deviates)
        {
            if (witnesses == 0) return (null, null);
            if (missed > 0) return (deviates(missed, witnesses), null);
            if (error) return (null, null);
            return (null, decided ? Confirmed(witnesses, turned, otherSide) : SideConfirmed);
        }

        /// <summary>
        /// How two variants compare by their witnesses (A3 §3): negative if A is better, positive if B
        /// is, 0 if they are equal. A feature in error never wins; otherwise fewer missed witnesses win.
        /// </summary>
        public static int CompareVariants(int missedA, bool errorA, int missedB, bool errorB)
        {
            if (errorA != errorB) return errorA ? 1 : -1;
            return missedA.CompareTo(missedB);
        }

        /// <summary>
        /// The bend as read back against what it should be: angle, radius and position. Not the
        /// direction – the witnesses decide it – and not the allowance, which is compared on its own.
        /// </summary>
        public static Deviations Differences(SketchedBendRecord want, SketchedBendRecord got, IFormatProvider? culture = null)
        {
            static double F(bool b) => b ? 1 : 0;
            var values = new List<(string, double, double, ScalarKind)>
            {
                // Not folded: a sketched bend of 200° is not one of 160°; which way it turns is the direction flag.
                ("Biegewinkel", want.Angle, got.Angle, ScalarKind.PlainAngle),
                ("Standard-Biegeradius", F(want.UseDefaultRadius), F(got.UseDefaultRadius), ScalarKind.Flag),
                ("Biegeposition", want.PositionType, got.PositionType, ScalarKind.Choice),
            };
            if (!want.UseDefaultRadius && !got.UseDefaultRadius) values.Add(("Biegeradius", want.Radius, got.Radius, ScalarKind.Length));
            var d = new Deviations();
            d.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            return d;
        }

        /// <summary>What the bend hands down to its SketchBend sub-features: its radius and its own allowance.</summary>
        public static BendValues Parent(SketchedBendRecord r) => new BendValues
        {
            Name = r.Name,
            UseDefaultRadius = r.UseDefaultRadius,
            Radius = r.Radius,
            UseDefaultAllowance = r.OwnAllowance == null,
            Allowance = r.OwnAllowance,
            UseDefaultRelief = true,
            Relief = null,
        };

        // ---- texts

        /// <summary>A position InsertSheetMetal3dBend cannot make.</summary>
        public static string PositionNotCreatable(int positionType) =>
            $"Biegeposition {positionType} der skizzierten Biegung lässt sich über die API nicht anlegen.";

        public const string FixedFaceUnreadable = "Feste Fläche der skizzierten Biegung nicht lesbar.";

        public const string NoBendLines = "Die Skizze der skizzierten Biegung hat keine Biegelinie.";

        /// <summary>
        /// Whether the step's check can confirm what the builder could not (A2 P9's rule, as for a hem's relief):
        /// „Jeden Schritt prüfen“, a check that decides, and a measured reference.
        /// </summary>
        public static bool CheckConfirms(bool verifySteps, bool checkDecides, bool hasReference) => verifySteps && checkDecides && hasReference;

        /// <summary>No witnesses (the rollback failed) and no check to confirm: the direction is the rule's guess.</summary>
        public const string NoWitnesses = "Biegerichtung nur nach Regel: Zeugenpunkte ließen sich nicht erfassen (Rückrollleiste); nicht geprüft.";

        /// <summary>No witnesses, but the check decides: said only if it finds a deviation.</summary>
        public const string NoWitnessesHint = "Die Biegerichtung folgt nur der Regel (keine Zeugenpunkte) – die Abweichung kann daher rühren.";

        /// <summary>Added to the text of a passing check when no witnesses decided the direction.</summary>
        public const string DirectionByCheck = "Biegerichtung geprüft";

        /// <summary>Both readings of the fixed point lie on the face in different regions.</summary>
        public const string AmbiguousFixedPoint =
            "Fester Punkt der Biegung in Skizzen- und Modellkoordinaten auf verschiedenen Seiten gelesen; die Skizzenlesung (laut Hilfe) wurde zuerst versucht.";

        /// <summary>Neither reading of the fixed point lies on the face.</summary>
        public const string FixedPointUnknown = "Fester Punkt der Biegung liegt in keiner Lesung auf der festen Fläche.";

        /// <summary>Without witnesses the side that stays is the rule's (or the first region's) guess.</summary>
        public const string SideUnchecked = "Ohne Zeugenpunkte ist die feste Seite der Biegung nicht geprüft.";

        /// <summary>The note if no variant puts every witness on the copy.</summary>
        public static string Deviates(int missed, int witnesses) =>
            $"Biegung weicht vom gespiegelten Original ab: {missed} von {witnesses} Zeugenpunkten nicht auf der Kopie.";

        /// <summary>The direction and side the witnesses confirmed (information).</summary>
        public static string Confirmed(int witnesses, bool turned, bool otherSide)
        {
            string what = (turned, otherSide) switch
            {
                (false, false) => "Richtung und feste Seite wie nach Regel",
                (true, false) => "Richtung umgekehrt gegenüber der Regel",
                (false, true) => "feste Seite gewechselt",
                _ => "Richtung umgekehrt und feste Seite gewechselt",
            };
            return $"Durch {(witnesses == 1 ? "1 Zeugenpunkt" : $"{witnesses} Zeugenpunkte")} bestätigt: {what}.";
        }

        /// <summary>Only the mirrored fixed point was a witness: the side is confirmed, the direction is the rule's.</summary>
        public const string SideConfirmed = "Feste Seite durch den gespiegelten festen Punkt bestätigt; die Richtung folgt der Regel.";

        /// <summary>The moved faces were left out of the witnesses: not every earlier feature of the body is in the copy.</summary>
        public const string OwnWitnessesOnly =
            "Zeugenpunkte nur auf den eigenen Flächen und denen der Biegungen: Nicht jedes frühere Feature des Körpers ist in der Kopie nachgebaut.";

        /// <summary>The moved witnesses left out because the body's earlier features could not be read (none listed).</summary>
        public const string EarlierFeaturesUnread =
            "Zeugenpunkte nur auf den eigenen Flächen und denen der Biegungen: Die früheren Features des Körpers ließen sich nicht lesen.";

        /// <summary>Why the moved witnesses were left out (<see cref="WitnessesFor"/>): unread if no earlier feature is listed.</summary>
        public static string OwnWitnessesOnlyFor(IReadOnlyCollection<string> bodyFeatures) =>
            bodyFeatures.Count == 0 ? EarlierFeaturesUnread : OwnWitnessesOnly;

        /// <summary>The allowance came back reset after a change of direction (A3 P5).</summary>
        public const string AllowanceReset = "Biegezugabe ließ sich nicht zusammen mit der Biegerichtung setzen (SOLIDWORKS setzt sie beim Ändern zurück).";

        /// <summary>The bend's sketch went on a plane instead of the mirrored face (B36): the bend is still made.</summary>
        public static string SketchOnPlane(string sketch, string? why) =>
            $"Skizze „{sketch}“ der Biegung liegt nicht auf der gespiegelten Fläche, sondern auf einer Ebene{(why == null ? "" : $" ({why})")}; ob SOLIDWORKS das annimmt, ist nicht geprüft (B36).";

        public const string Turned = "Biegerichtung umgekehrt";

        public static string Describe(SketchedBendRecord r, IFormatProvider? culture = null)
        {
            string lines = r.BendLines.Count == 1 ? "1 Biegelinie" : $"{r.BendLines.Count} Biegelinien";
            return $"Skizzierte Biegung {SheetMetalCompare.Deg(r.Angle, culture)}, {lines}";
        }
    }
}
