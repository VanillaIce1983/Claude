using System;
using System.Collections.Generic;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>How an edge flange is made in the target (A2 §5 step 4), tried in this order.</summary>
    public enum EdgeFlangePath
    {
        /// <summary>CreateDefinition(swFmEdgeFlange) + AddEdges + CreateFeature: the way the help names (tbcphutai on 2024).</summary>
        Definition,

        /// <summary>InsertSheetMetalEdgeFlange2 with the edges and the profile sketches (obsolete, works with profiles: eyfel on 2026).</summary>
        Insert2,

        /// <summary>InsertSheetMetalEdgeFlange for a flange on a single edge (CADapter).</summary>
        Single,
    }

    /// <summary>
    /// Edge flange (A2 phase 2b, blech-plan S3): the ways to make it, what a value of it means, the
    /// read-back comparison and the direction search. Lengths, angle, radius, position, gap, relief and
    /// allowance are unchanged by the reflection (they are defined by the sheet's faces, which R maps
    /// onto the copy's); only the edges and profiles are mirrored, the sketch angle follows
    /// <see cref="FlangeProfiles.AngleCandidates"/>, and the two direction flags – whose reference
    /// SOLIDWORKS does not document – are decided by the profile check (<see cref="FlangeProfiles.Check"/>).
    /// </summary>
    public static class EdgeFlangeRules
    {
        /// <summary>swFeatureNameID_e.swFmEdgeFlange (the same in the 2019 and 2024 interop).</summary>
        public const int FeatureId = 37;

        /// <summary>The ways to try: the data object, then InsertSheetMetalEdgeFlange2, the single-edge call only for one edge.</summary>
        public static IReadOnlyList<EdgeFlangePath> Paths(int edgeCount) => edgeCount == 1
            ? new[] { EdgeFlangePath.Definition, EdgeFlangePath.Insert2, EdgeFlangePath.Single }
            : new[] { EdgeFlangePath.Definition, EdgeFlangePath.Insert2 };

        /// <summary>For the report: how the flange was made.</summary>
        public static string Describe(EdgeFlangePath p) => p switch
        {
            EdgeFlangePath.Insert2 => "InsertSheetMetalEdgeFlange2",
            EdgeFlangePath.Single => "InsertSheetMetalEdgeFlange (eine Kante)",
            _ => "Datenobjekt (CreateDefinition, AddEdges, CreateFeature)",
        };

        /// <summary>The length has a distance of its own: blind, mid plane, offset from a surface (the others end at their reference).</summary>
        public static bool UsesDistance(int offsetType) =>
            offsetType == FlangeOffsetTypes.Blind || offsetType == FlangeOffsetTypes.MidPlane || offsetType == FlangeOffsetTypes.FromSurface;

        /// <summary>Where the length is measured matters only for a length of its own given as a number: blind and mid plane.</summary>
        public static bool UsesDimType(int offsetType) => offsetType == FlangeOffsetTypes.Blind || offsetType == FlangeOffsetTypes.MidPlane;

        /// <summary>The length (or position offset) ends at a reference: vertex, surface, offset from a surface, edge.</summary>
        public static bool UsesReference(int offsetType) =>
            offsetType == FlangeOffsetTypes.UpToVertex || offsetType == FlangeOffsetTypes.UpToSurface ||
            offsetType == FlangeOffsetTypes.FromSurface || offsetType == FlangeOffsetTypes.UptoEdgeAndMerge;

        /// <summary>
        /// swInsertEdgeFlangeOptions_e for InsertSheetMetalEdgeFlange(2): default radius, FlipDir (the
        /// chosen flip of the profile plane), position offset and its direction, side bends, relief.
        /// Which bit means what is not documented beyond the names (B29); every value is set again in
        /// the edit after creation and read back.
        /// </summary>
        public static int CreationOptions(EdgeFlangeRecord r, bool flip)
        {
            int o = 0;
            if (r.UseDefaultRadius) o |= EdgeFlangeOptions.UseDefaultRadius;
            if (flip) o |= EdgeFlangeOptions.FlipDir;
            if (r.UsePositionOffset) o |= EdgeFlangeOptions.DoOffset;
            if (r.UsePositionOffset && r.ReversePositionOffset) o |= EdgeFlangeOptions.ReverseOffsetDir;
            if (r.OwnRelief != null && r.OwnRelief.Kind == ReliefKind.Tear && r.OwnRelief.TearType == ReliefTearTypes.Rip) o |= EdgeFlangeOptions.TearClip;
            if (r.TrimSideBends) o |= EdgeFlangeOptions.TrimSideBend;
            if (r.OwnRelief == null || r.OwnRelief.UseRatio) o |= EdgeFlangeOptions.UseReliefRatio;
            if (r.OwnRelief == null) o |= EdgeFlangeOptions.UseDefaultRelief;
            return o;
        }

        /// <summary>What the flange hands down to its bends: radius, own allowance and own relief (null: the default – then every own one counts).</summary>
        public static BendValues Parent(EdgeFlangeRecord r) => new BendValues
        {
            Name = r.Name,
            UseDefaultRadius = r.UseDefaultRadius,
            Radius = r.BendRadius,
            UseDefaultAllowance = r.OwnAllowance == null,
            Allowance = r.OwnAllowance,
            UseDefaultRelief = r.OwnRelief == null,
            Relief = r.OwnRelief,
        };

        /// <summary>
        /// The flange as read back against what it should be, without the allowance (an edit of its
        /// own) and without the two direction flags (the profile check decides those). Only what the
        /// flange uses is compared – a value SOLIDWORKS does not use can come back as anything
        /// (tbcphutai): the length and where it is measured only where it is a number, the position
        /// offset's type and distance only with a position offset, the late-bound flags only where both
        /// read them and where they apply. The bend angle is compared folded: θ and 2π − θ are the same
        /// bend about an edge that runs the other way (A2 P1).
        /// </summary>
        public static Deviations Differences(EdgeFlangeRecord want, EdgeFlangeRecord got, IFormatProvider? culture = null)
        {
            static double F(bool b) => b ? 1 : 0;
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Biegewinkel", want.BendAngle, got.BendAngle, ScalarKind.Angle),
                ("Standard-Biegeradius", F(want.UseDefaultRadius), F(got.UseDefaultRadius), ScalarKind.Flag),
                ("Endbedingung der Laschenlänge", want.OffsetType, got.OffsetType, ScalarKind.Choice),
                ("Laschenposition", want.PositionType, got.PositionType, ScalarKind.Choice),
                ("Seitliche Biegungen trimmen", F(want.TrimSideBends), F(got.TrimSideBends), ScalarKind.Flag),
                ("Versatz", F(want.UsePositionOffset), F(got.UsePositionOffset), ScalarKind.Flag),
                ("Spaltabstand", want.GapDistance, got.GapDistance, ScalarKind.Length),
            };
            if (!want.UseDefaultRadius && !got.UseDefaultRadius) values.Add(("Biegeradius", want.BendRadius, got.BendRadius, ScalarKind.Length));
            if (want.OffsetType == got.OffsetType)
            {
                if (UsesDistance(want.OffsetType)) values.Add(("Laschenlänge", want.OffsetDistance, got.OffsetDistance, ScalarKind.Length));
                if (UsesDimType(want.OffsetType)) values.Add(("Bemaßung der Laschenlänge", want.OffsetDimType, got.OffsetDimType, ScalarKind.Choice));
                if (want.OffsetType == FlangeOffsetTypes.UpToVertex && want.NormalToFlangePlane.HasValue && got.NormalToFlangePlane.HasValue)
                    values.Add(("Senkrecht zur Laschenebene", F(want.NormalToFlangePlane.Value), F(got.NormalToFlangePlane.Value), ScalarKind.Flag));
                if (want.OffsetType == FlangeOffsetTypes.UptoEdgeAndMerge && want.LockAngle.HasValue && got.LockAngle.HasValue)
                    values.Add(("Winkel sperren", F(want.LockAngle.Value), F(got.LockAngle.Value), ScalarKind.Flag));
            }
            if (want.UsePositionOffset && got.UsePositionOffset)
            {
                values.Add(("Versatzart", want.PositionOffsetType, got.PositionOffsetType, ScalarKind.Choice));
                if (want.PositionOffsetType == got.PositionOffsetType && UsesDistance(want.PositionOffsetType))
                    values.Add(("Versatzabstand", want.PositionOffsetDistance, got.PositionOffsetDistance, ScalarKind.Length));
            }
            if (want.PerpendicularToFace.HasValue && got.PerpendicularToFace.HasValue)
                values.Add(("Senkrecht zur Fläche", F(want.PerpendicularToFace.Value), F(got.PerpendicularToFace.Value), ScalarKind.Flag));
            var d = new Deviations();
            d.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            d.Add(SheetMetalCompare.Relief(want.OwnRelief, got.OwnRelief, culture));
            return d;
        }

        /// <summary>
        /// The direction states to try when the profile check fails with the chosen one (A2 P2, tbcphutai's
        /// search): the flange direction turned, and – only with a position offset – the offset direction
        /// turned and both. Absolute values (ReverseOffset, ReversePositionOffset), each set in an edit of
        /// its own and checked again.
        /// </summary>
        public static IReadOnlyList<(bool reverseOffset, bool reversePositionOffset)> DirectionVariants(bool reverseOffset, bool reversePositionOffset, bool usePositionOffset)
        {
            var result = new List<(bool, bool)> { (!reverseOffset, reversePositionOffset) };
            if (usePositionOffset)
            {
                result.Add((reverseOffset, !reversePositionOffset));
                result.Add((!reverseOffset, !reversePositionOffset));
            }
            return result;
        }

        /// <summary>For the report: a direction state relative to the one the profile plane was chosen with.</summary>
        public static string DescribeDirections(bool offsetTurned, bool positionTurned) => (offsetTurned, positionTurned) switch
        {
            (true, true) => $"{FlangeTurned}, {OffsetTurned}",
            (true, false) => FlangeTurned,
            (false, true) => OffsetTurned,
            _ => "Richtungen wie gewählt",
        };

        /// <summary>Alternative and report text: ReverseOffset turned.</summary>
        public const string FlangeTurned = "Laschenrichtung umgekehrt";

        /// <summary>Alternative and report text: ReversePositionOffset turned.</summary>
        public const string OffsetTurned = "Versatzrichtung der Lasche umgekehrt";

        /// <summary>For the report: the profile plane the copy's sketches were made with.</summary>
        public static string DescribeCandidate(double angle, bool flip, int index, int count, IFormatProvider? culture = null) =>
            $"Profilebene mit Winkel {ReportText.Number(angle * 180 / Math.PI, 4, culture)}° und FlipDir {(flip ? "ja" : "nein")} (Kandidat {index + 1} von {count}).";

        /// <summary>Why no profile plane was found.</summary>
        public static string NoProfilePlane(int candidates) =>
            $"Keine passende Profilebene für die Kantenlasche ({candidates} Kandidaten aus Winkel und Richtung probiert).";

        /// <summary>An edge flange on a curved edge (possible from 2021 on) is not rebuilt.</summary>
        public const string CurvedEdge = "Kantenlasche an einer gekrümmten Kante wird nicht nachgebaut.";

        /// <summary>swEdgeFlangeError_e of AddEdges, German.</summary>
        public static string AddEdgesError(int code) => code switch
        {
            0 => "kein Fehler",
            1 => "keine Kante angegeben",
            2 => "keine Skizze angegeben",
            3 => "Zahl der Kanten und Skizzen verschieden",
            4 => "Kante schon vorhanden",
            5 => "ungültige Kante",
            6 => "mindestens eine Kante nötig",
            7 => "allgemeiner Fehler",
            _ => $"Fehler {code}",
        };

        /// <summary>„Kantenlasche, 2 Kanten, 90°“.</summary>
        public static string Describe(EdgeFlangeRecord r, IFormatProvider? culture = null)
        {
            string edges = r.Profiles.Count == 1 ? "1 Kante" : $"{r.Profiles.Count} Kanten";
            string offset = r.UsePositionOffset ? ", mit Versatz" : "";
            return $"Kantenlasche, {edges}, {ReportText.Number(SheetMetalCompare.Fold(r.BendAngle) * 180 / Math.PI, 4, culture)}°{offset}";
        }
    }
}
