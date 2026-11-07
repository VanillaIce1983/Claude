using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Jog (A2 P6, P7, blech-plan S4): the encodings of IModelDoc2.InsertSheetMetalJog (the only
    /// creation call; it returns nothing), its one line, what the read-back compares and the report
    /// texts. Angle, radius, offset, its positions and the allowance are unchanged by a reflection –
    /// they are defined by the sheet's faces, which R maps onto the copy's. The direction flag is
    /// decided by witnesses, as for the sketched bend; ReverseOffset is relative to the offset
    /// reference and goes over unchanged.
    /// </summary>
    public static class JogRules
    {
        /// <summary>
        /// DimPos of InsertSheetMetalJog: the 1-based swJogDimensionPositionType_e as read (CADapter
        /// measured DimPos/BendPos change the result, 1/0 against 3/2, B41); null for another value.
        /// </summary>
        public static short? DimPos(int dimensionPosition) =>
            dimensionPosition >= JogDimensionPositions.InsideOffset && dimensionPosition <= JogDimensionPositions.OverallPosition ? (short)dimensionPosition : (short?)null;

        /// <summary>
        /// BendPos of InsertSheetMetalJog: 0-based, swJogPositionType_e − 1 (0 centre line, 1 material
        /// inside, 2 material outside, 3 bend outside – the encoding of InsertSheetMetal3dBend); null for another value.
        /// </summary>
        public static short? BendPos(int jogPosition) =>
            jogPosition >= JogPositions.BendCenterline && jogPosition <= JogPositions.BendOutside ? (short)(jogPosition - 1) : (short?)null;

        /// <summary>The angle InsertSheetMetalJog wants: degrees (radians are a silent no-op, B42); radians are the retry.</summary>
        public static double CreationAngle(double radians, bool inDegrees) => inDegrees ? radians * 180 / Math.PI : radians;

        /// <summary>Offset types that end at a reference (up to vertex, up to surface, offset from surface).</summary>
        public static bool UsesReference(int offsetType) =>
            offsetType == JogOffsetTypes.UpToVertex || offsetType == JogOffsetTypes.UpToSurface || offsetType == JogOffsetTypes.FromSurface;

        /// <summary>Offset types whose distance is a number the jog uses (blind, offset from surface, mid plane).</summary>
        public static bool UsesDistance(int offsetType) =>
            offsetType == JogOffsetTypes.Blind || offsetType == JogOffsetTypes.FromSurface || offsetType == JogOffsetTypes.MidPlane;

        /// <summary>
        /// The distance passed to the insert call, which only knows a blind offset: the one read where
        /// it is a number, otherwise 1 mm as a stand-in until the edit sets type and reference.
        /// </summary>
        public static double CreationOffset(JogRecord r) => UsesDistance(r.OffsetType) && r.OffsetDistance > 0 ? r.OffsetDistance : 1e-3;

        /// <summary>The insert call made a blind jog: type, reference or mid plane come with the edit.</summary>
        public static bool NeedsOffsetEdit(JogRecord r) => r.OffsetType != JogOffsetTypes.Blind;

        /// <summary>
        /// The one line of a jog sketch: its id if the sketch has exactly one non-construction segment
        /// and that is a line; null otherwise, with <paramref name="why"/>.
        /// </summary>
        public static string? JogLine(SketchModel sketch, out string? why)
        {
            List<SkSegment> drawn = sketch.Segments.Where(s => !s.Construction).ToList();
            why = drawn.Count == 1 && drawn[0].Kind == SkSegmentKind.Line ? null
                : drawn.Count == 0 ? "Die Skizze der Kröpfung hat keine Linie."
                : drawn.Count > 1 ? $"Die Skizze der Kröpfung hat {drawn.Count} Elemente; eine Kröpfung braucht genau eine Linie."
                : "Das Element der Kröpfungsskizze ist keine Linie.";
            return why == null ? drawn[0].Id : null;
        }

        /// <summary>
        /// The jog as read back against what it should be: angle, radius, offset (type, distance where it
        /// is a number, ReverseOffset), dimension and jog position, fixed projected length. Not the
        /// direction (the witnesses decide it) and not the allowance (an edit of its own).
        /// </summary>
        public static Deviations Differences(JogRecord want, JogRecord got, IFormatProvider? culture = null)
        {
            static double F(bool b) => b ? 1 : 0;
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Kröpfungswinkel", want.Angle, got.Angle, ScalarKind.PlainAngle),
                ("Standard-Biegeradius", F(want.UseDefaultRadius), F(got.UseDefaultRadius), ScalarKind.Flag),
                ("Endbedingung des Versatzes", want.OffsetType, got.OffsetType, ScalarKind.Choice),
                ("Bemaßungsposition", want.DimensionPosition, got.DimensionPosition, ScalarKind.Choice),
                ("Kröpfungsposition", want.JogPosition, got.JogPosition, ScalarKind.Choice),
                ("Projizierte Länge fixieren", F(want.FixProjectedLength), F(got.FixProjectedLength), ScalarKind.Flag),
            };
            if (!want.UseDefaultRadius && !got.UseDefaultRadius) values.Add(("Biegeradius", want.Radius, got.Radius, ScalarKind.Length));
            if (want.OffsetType == got.OffsetType)
            {
                if (UsesDistance(want.OffsetType)) values.Add(("Versatz", want.OffsetDistance, got.OffsetDistance, ScalarKind.Length));
                if (UsesReference(want.OffsetType)) values.Add(("Versatz umgekehrt", F(want.ReverseOffset), F(got.ReverseOffset), ScalarKind.Flag));
            }
            var d = new Deviations();
            d.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            return d;
        }

        /// <summary>What the jog hands down to its bends: its radius and its own allowance.</summary>
        public static BendValues Parent(JogRecord r) => new BendValues
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

        public const string FixedFaceUnreadable = "Feste Fläche der Kröpfung nicht lesbar.";

        /// <summary>No witnesses (the rollback failed) and no check to confirm.</summary>
        public const string NoWitnesses = "Kröpfungsrichtung nur nach Regel: Zeugenpunkte ließen sich nicht erfassen (Rückrollleiste); nicht geprüft.";

        /// <summary>No witnesses, but the check decides: said only if it finds a deviation.</summary>
        public const string NoWitnessesHint = "Kröpfungsrichtung und feste Seite folgen nur der Regel (keine Zeugenpunkte) – die Abweichung kann daher rühren.";

        /// <summary>Added to the text of a passing check when no witnesses decided the direction.</summary>
        public const string DirectionByCheck = "Kröpfungsrichtung geprüft";

        /// <summary>The fixed point lies in neither reading on the face, or in both on different sides.</summary>
        public const string FixedSideUnknown = "Fester Punkt der Kröpfung nicht eindeutig lesbar.";

        /// <summary>Build: the jog sketch is not on the mirrored face (B36 – a plane is not accepted, A2 L11).</summary>
        public static string SketchNotOnFace(string sketch, string? why) =>
            $"Die Skizze „{sketch}“ der Kröpfung liegt im Ziel nicht auf der gespiegelten Fläche{(why == null ? "" : $" ({why})")}; eine Kröpfung braucht die Fläche, auf der ihre Skizze liegt.";

        public static string Deviates(int missed, int witnesses) =>
            $"Kröpfung weicht vom gespiegelten Original ab: {missed} von {witnesses} Zeugenpunkten nicht auf der Kopie.";

        /// <summary>Without witnesses the fixed side cannot be checked (the verifier sees the direction only).</summary>
        public const string SideUnchecked = "Feste Seite der Kröpfung ohne Zeugenpunkte nicht geprüft.";

        public const string Turned = "Kröpfungsrichtung umgekehrt";
        public const string OffsetTurned = "Versatz der Kröpfung umgekehrt";

        public static string Describe(JogRecord r, IFormatProvider? culture = null)
        {
            string offset = UsesDistance(r.OffsetType) ? $", Versatz {SheetMetalCompare.Mm(r.OffsetDistance, culture)}" : "";
            return $"Kröpfung {SheetMetalCompare.Deg(r.Angle, culture)}{offset}";
        }
    }
}
