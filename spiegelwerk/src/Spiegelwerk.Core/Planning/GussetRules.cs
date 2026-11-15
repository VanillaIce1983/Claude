using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>How the copy of a gusset is placed on its bend: a reference point (none, the mirrored own one, the vertex at the mirrored anchor end), the offset and its side.</summary>
    public enum GussetPoint
    {
        /// <summary>No reference point: the offset counts from the point SOLIDWORKS assumes.</summary>
        None,

        /// <summary>The gusset's own reference point, mirrored.</summary>
        Own,

        /// <summary>The copy's vertex at the mirrored bend end the original's offset counted from (A4 P9).</summary>
        Anchor,
    }

    /// <summary>One way to place the copy of a gusset; the first whose section lies where the mirrored original's does is kept.</summary>
    public sealed class GussetVariant
    {
        public GussetVariant(string description, GussetPoint point, double offset, bool flipToggled)
        {
            Description = description;
            Point = point;
            Offset = offset;
            FlipToggled = flipToggled;
        }

        public string Description { get; }
        public GussetPoint Point { get; }

        /// <summary>Metres.</summary>
        public double Offset { get; }

        /// <summary>FlipOffset the other way than the rule says.</summary>
        public bool FlipToggled { get; }

        public override string ToString() => Description;
    }

    /// <summary>
    /// The sheet-metal gusset (A4 (2) §7, P9, blech-plan S8). A reflection keeps every value and the order of the
    /// legs (so FlipDimSides). It does not keep the bend's direction: without a reference point of its own, a
    /// gusset's offset counts from „the end of the sheet metal bend“, or from „the edge of the gusset last inserted“
    /// (help) – and which end is which depends on how the copy's bend runs. The capture therefore measures where
    /// the original's section lies on its bend and what its offset counts from (<see cref="AnchorOf"/>); the copy
    /// gets an explicit reference point at the mirrored end, and the place of every new gusset is checked against
    /// the mirrored original's, always (<see cref="SectionTolerance"/>), trying the variants of <see cref="Variants"/>.
    /// </summary>
    public static class GussetRules
    {
        /// <summary>swFeatureNameID_e.swFmSMGusset (in the enum from the 2024 interop only).</summary>
        public const int SMGussetId = 118;

        /// <summary>How far the copy's section may lie from the mirrored original's (10 µm, as the face samples).</summary>
        public const double SectionTolerance = 1e-5;

        /// <summary>The coordinate of a point along a line (from its point, in its direction).</summary>
        public static double Along(Line3 line, Vec3 p) => (p - line.Point).Dot(line.Direction);

        /// <summary>The point of a line at a coordinate.</summary>
        public static Vec3 At(Line3 line, double t) => line.Point + line.Direction * t;

        /// <summary>
        /// The section of a gusset: the middle of the extent of its faces along the bend line, as a point on the
        /// line. A gusset stands symmetric to its section plane (its width either side of it), so the middle of
        /// the extent is the section, whatever its profile, draft or fillets. Null without points.
        /// </summary>
        public static Vec3? Section(Line3 bend, IEnumerable<Vec3> points)
        {
            List<double> t = points.Select(p => Along(bend, p)).ToList();
            if (t.Count == 0) return null;
            return At(bend, (t.Min() + t.Max()) / 2);
        }

        /// <summary>The ends of a bend: the points of its face with the smallest and the largest coordinate along its axis. Null without points.</summary>
        public static (Vec3 a, Vec3 b)? Ends(Line3 bend, IEnumerable<Vec3> points)
        {
            List<Vec3> list = points.ToList();
            if (list.Count == 0) return null;
            Vec3 a = list.OrderBy(p => Along(bend, p)).First();
            Vec3 b = list.OrderBy(p => Along(bend, p)).Last();
            return (a, b);
        }

        /// <summary>The length of the bend between its ends; null if they are unknown.</summary>
        public static double? BendLength(SheetMetalGussetRecord r) =>
            r.BendLine is Line3 l && r.BendEndA is Vec3 a && r.BendEndB is Vec3 b ? Math.Abs(Along(l, b) - Along(l, a)) : (double?)null;

        /// <summary>
        /// What the offset of a gusset counts from (A4 P9), from where its section lies: its own reference point;
        /// otherwise – with an offset – the earlier gusset on the same bend whose section, or one of whose side
        /// faces, lies at the offset's distance (the last inserted first: the help names „the edge of the gusset
        /// last inserted“), then the bend end at that distance; if both ends are (a gusset in the middle), the
        /// first. Unknown otherwise. Its extent along the bend is its indent width, not its thickness: a side face
        /// lies at section ± half the width (the width as the outer measure) or ± half the width plus the wall
        /// thickness (the width as the inner measure) – which one SOLIDWORKS counts from is open (B65).
        /// </summary>
        public static (GussetAnchor anchor, Vec3? point, string? gusset) AnchorOf(SheetMetalGussetRecord r, double tol = SectionTolerance)
        {
            if (r.ReferencePoint != null) return (GussetAnchor.Own, null, null);
            if (!r.UseOffset || !(r.BendLine is Line3 line) || !(r.SectionPoint is Vec3 section)) return (GussetAnchor.Unknown, null, null);
            double t = Along(line, section), d = r.OffsetDistance;
            bool At(double from) => Math.Abs(Math.Abs(t - from) - d) <= tol;
            foreach (GussetSection previous in Enumerable.Reverse(r.PreviousOnBend))
            {
                double s = Along(line, previous.Point), h = previous.Width / 2, o = h + previous.Thickness;
                if (At(s) || At(s - h) || At(s + h) || At(s - o) || At(s + o)) return (GussetAnchor.PreviousGusset, null, previous.Name);
            }
            foreach (Vec3? end in new[] { r.BendEndA, r.BendEndB })
                if (end is Vec3 e && At(Along(line, e)))
                    return (GussetAnchor.BendEnd, e, null);
            return (GussetAnchor.Unknown, null, null);
        }

        /// <summary>
        /// The ways the copy is placed, in order (A4 P9): with an own reference point the mirrored one, then the
        /// offset on the other side; with a bend end the vertex at the mirrored end (both sides), then no point as
        /// in the original, the offset from the other end (L − d), the other side; after an earlier gusset no point
        /// (the copy inserts them in the same order), the other side; not determinable: no point, the other side,
        /// L − d. The other side only with an offset; L − d only where the bend's length is known and longer.
        /// </summary>
        public static IReadOnlyList<GussetVariant> Variants(SheetMetalGussetRecord r)
        {
            double d = r.OffsetDistance;
            double? length = BendLength(r);
            bool other = length.HasValue && length.Value - d > SectionTolerance && Math.Abs(length.Value - 2 * d) > SectionTolerance;
            var v = new List<GussetVariant>();
            void Add(string text, GussetPoint p, double offset, bool flip)
            {
                if (flip && !r.UseOffset) return;
                v.Add(new GussetVariant(text, p, offset, flip));
            }
            switch (r.Anchor)
            {
                case GussetAnchor.Own:
                    Add(AsOriginal, GussetPoint.Own, d, false);
                    Add(OtherSide, GussetPoint.Own, d, true);
                    break;
                case GussetAnchor.BendEnd:
                    Add(FromBendEnd, GussetPoint.Anchor, d, false);
                    Add(FromBendEnd + ", " + OtherSide, GussetPoint.Anchor, d, true);
                    Add(WithoutPoint, GussetPoint.None, d, false);
                    if (other) Add(FromOtherEnd, GussetPoint.None, length!.Value - d, false);
                    Add(OtherSide, GussetPoint.None, d, true);
                    break;
                case GussetAnchor.PreviousGusset:
                    Add(FromPrevious, GussetPoint.None, d, false);
                    Add(FromPrevious + ", " + OtherSide, GussetPoint.None, d, true);
                    break;
                default:
                    Add(WithoutPoint, GussetPoint.None, d, false);
                    Add(OtherSide, GussetPoint.None, d, true);
                    if (other) Add(FromOtherEnd, GussetPoint.None, length!.Value - d, false);
                    break;
            }
            return v;
        }

        /// <summary>
        /// FlipOffset of the copy: σ of the reference line (as for a draft direction) where the original names one
        /// and the copy's direction is known; unchanged otherwise – the section check decides then.
        /// </summary>
        public static bool FlipOffsetFor(bool flip, Vec3? originalLine, Reflection r, Vec3? targetLine) =>
            originalLine.HasValue && targetLine.HasValue ? DirectionRules.ReverseFlag(flip, DirectionRules.Sigma(originalLine.Value, r, targetLine.Value)) : flip;

        /// <summary>How far the copy's section lies from the wanted one, along the (mirrored) bend line.</summary>
        public static double Deviation(Line3 bend, Vec3 wanted, Vec3 measured) => Math.Abs(Along(bend, measured) - Along(bend, wanted));

        /// <summary>The copy's section is the mirrored original's (<see cref="SectionTolerance"/>).</summary>
        public static bool SectionMatches(Line3 bend, Vec3 wanted, Vec3 measured) => Deviation(bend, wanted, measured) <= SectionTolerance;

        /// <summary>The values as read back against the original's – not offset and side: the section check decides those.</summary>
        public static Deviations Differences(SheetMetalGussetRecord want, SheetMetalGussetRecord got, IFormatProvider? culture = null)
        {
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Profilbemaßung", want.ProfileDimensionScheme, got.ProfileDimensionScheme, ScalarKind.Choice),
                ("Breite", want.IndentWidth, got.IndentWidth, ScalarKind.Length),
                ("Dicke", want.GussetThickness, got.GussetThickness, ScalarKind.Length),
                ("Rippenart", want.GussetType, got.GussetType, ScalarKind.Choice),
                ("Seitenflächen mit Formschräge", Flag(want.DraftSideFaces), Flag(got.DraftSideFaces), ScalarKind.Flag),
                ("Innenecken verrundet", Flag(want.FilletInnerCorners), Flag(got.FilletInnerCorners), ScalarKind.Flag),
                ("Außenecken verrundet", Flag(want.FilletOuterCorners), Flag(got.FilletOuterCorners), ScalarKind.Flag),
                ("Dokumenteinstellungen überschreiben", Flag(want.OverrideDocSettings), Flag(got.OverrideDocSettings), ScalarKind.Flag),
            };
            if (want.ProfileDimensionScheme == GussetProfileDimTypes.IndentDepth)
                values.Add(("Einzugstiefe", want.IndentDepth, got.IndentDepth, ScalarKind.Length));
            else
            {
                values.Add(("Winkel bemaßt", Flag(want.UseAngleDimForProfile), Flag(got.UseAngleDimForProfile), ScalarKind.Flag));
                values.Add(("d1 und d2 getauscht", Flag(want.FlipDimSides), Flag(got.FlipDimSides), ScalarKind.Flag));
                if (!want.UseAngleDimForProfile || !want.FlipDimSides) values.Add(("Schenkel d1", want.ProfileLengthDim, got.ProfileLengthDim, ScalarKind.Length));
                if (!want.UseAngleDimForProfile || want.FlipDimSides) values.Add(("Schenkel d2", want.ProfileHeightDim, got.ProfileHeightDim, ScalarKind.Length));
                if (want.UseAngleDimForProfile) values.Add(("Profilwinkel", want.ProfileAngleDim, got.ProfileAngleDim, ScalarKind.PlainAngle));
            }
            if (want.DraftSideFaces) values.Add(("Formschräge", want.DraftAngle, got.DraftAngle, ScalarKind.PlainAngle));
            if (want.FilletInnerCorners) values.Add(("Radius der Innenecken", want.InnerCornerFilletRadius, got.InnerCornerFilletRadius, ScalarKind.Length));
            if (want.FilletOuterCorners) values.Add(("Radius der Außenecken", want.OuterCornerFilletRadius, got.OuterCornerFilletRadius, ScalarKind.Length));
            if (want.GussetType == GussetTypes.Flat)
            {
                values.Add(("Kanten verrundet", Flag(want.FilletGussetEdges), Flag(got.FilletGussetEdges), ScalarKind.Flag));
                if (want.FilletGussetEdges) values.Add(("Kantenradius", want.EdgeFilletRadius, got.EdgeFilletRadius, ScalarKind.Length));
            }
            if (want.OverrideDocSettings)
            {
                values.Add(("Profil in der Abwicklung", Flag(want.ShowProfile), Flag(got.ShowProfile), ScalarKind.Flag));
                values.Add(("Mittelpunkt in der Abwicklung", Flag(want.ShowCenter), Flag(got.ShowCenter), ScalarKind.Flag));
            }
            var d = new Deviations();
            d.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            return d;
        }

        private static double Flag(bool b) => b ? 1 : 0;

        public static string Describe(SheetMetalGussetRecord r, IFormatProvider? culture = null)
        {
            string kind = r.GussetType == GussetTypes.Flat ? "flach" : "gerundet";
            string size = r.ProfileDimensionScheme == GussetProfileDimTypes.IndentDepth
                ? $"Einzug {SheetMetalCompare.Mm(r.IndentDepth, culture)}"
                : r.UseAngleDimForProfile
                    ? $"Schenkel {SheetMetalCompare.Mm(r.FlipDimSides ? r.ProfileHeightDim : r.ProfileLengthDim, culture)}, {SheetMetalCompare.Deg(r.ProfileAngleDim, culture)}"
                    : $"Schenkel {SheetMetalCompare.Mm(r.ProfileLengthDim, culture)} × {SheetMetalCompare.Mm(r.ProfileHeightDim, culture)}";
            return $"Versteifungsrippe ({kind}), {size}, Dicke {SheetMetalCompare.Mm(r.GussetThickness, culture)}";
        }

        // ---- texts

        public const string AsOriginal = "wie im Original";
        public const string OtherSide = "Versatz auf der anderen Seite";
        public const string FromBendEnd = "Versatz vom gespiegelten Biegungsende";
        public const string FromOtherEnd = "Versatz vom anderen Biegungsende";
        public const string WithoutPoint = "ohne Bezugspunkt wie im Original";
        public const string FromPrevious = "Versatz von der vorigen Versteifungsrippe";

        /// <summary>The variant offered to the check for the profile-dimension scheme: d1 and d2 swapped.</summary>
        public const string LegsSwapped = "Schenkel d1/d2 getauscht";

        /// <summary>ProfileType custom: its meaning is open (B66).</summary>
        public const string CustomProfile = "Versteifungsrippe mit eigenem Profil: Bedeutung über die API nicht geklärt – wird nicht nachgebaut.";

        /// <summary>Without bend line or section, the place of the copy cannot be checked.</summary>
        public const string PlaceUnchecked = "Lage der Versteifungsrippe auf der Biegung nicht geprüft (Biegung oder Schnitt im Original nicht lesbar).";

        /// <summary>The variant that was kept, when it is not the first.</summary>
        public static string Placed(string variant) => $"Lage auf der Biegung geprüft: {variant}.";

        /// <summary>The first variant fitted.</summary>
        public const string PlaceChecked = "Lage auf der Biegung geprüft.";

        /// <summary>No variant put the section where it belongs: the best is kept.</summary>
        public static string PlaceOff(double deviation, string variant, IFormatProvider? culture = null) =>
            $"Lage der Versteifungsrippe weicht um {SheetMetalCompare.Mm(deviation, culture)} von der gespiegelten ab (beste Variante: {variant}); bitte prüfen.";

        /// <summary>A variant whose section did not fit (or could not be measured).</summary>
        public static string Tried(string variant, double? deviation, IFormatProvider? culture = null) =>
            deviation is double d ? $"{variant}: Lage weicht um {SheetMetalCompare.Mm(d, culture)} ab." : $"{variant}: Lage nicht messbar.";

        /// <summary>The vertex at the mirrored bend end was not found: the variants without it.</summary>
        public const string AnchorVertexMissing = "Eckpunkt am gespiegelten Biegungsende nicht gefunden; die Lage wird ohne Bezugspunkt gesucht.";

        /// <summary>The anchor said for the report.</summary>
        public static string AnchorNote(SheetMetalGussetRecord r) => r.Anchor switch
        {
            GussetAnchor.BendEnd => "Versatz zählt im Original von einem Ende der Biegung.",
            GussetAnchor.PreviousGusset => $"Versatz zählt im Original von der Versteifungsrippe „{r.AnchorGusset}“.",
            GussetAnchor.Unknown when r.ReferencePoint == null => "Wovon der Versatz im Original zählt, ist nicht bestimmbar.",
            _ => "",
        };
    }
}
