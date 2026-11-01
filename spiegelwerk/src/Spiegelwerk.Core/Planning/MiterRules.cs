using System;
using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>How the profile sketch and the chain are selected for InsertSheetMetalMiterFlange.</summary>
    public enum MiterSelection
    {
        /// <summary>The sketch on mark 0, then the edges in chain order on mark 1 (soheil, VERIFIED on 2026, chains of 2–4 edges).</summary>
        SketchFirst,

        /// <summary>The edges on mark 1 first, the sketch appended on mark 0.</summary>
        ChainFirst,

        /// <summary>Sketch and edge on mark 0 (CADapter): with two edges only one flange came out – a single edge only.</summary>
        AllOnMark0,
    }

    /// <summary>
    /// Miter flange (A2 P8, blech-plan S2): the selection variants, the alternative for the check,
    /// what its bends inherit and the read-back comparison. Radius, gap, position, offsets, relief and
    /// allowance are unchanged by the reflection; the profile is mirrored as a sketch, so the start of
    /// the chain stays where the profile is.
    /// </summary>
    public static class MiterRules
    {
        /// <summary>
        /// The selections to try, in order. The edges on mark 0 propagate to one edge only
        /// (CADapter), so that retry is allowed only for a chain of one edge; any variant whose flange
        /// has another number of edges than the original's is removed again (the builder).
        /// </summary>
        public static IReadOnlyList<MiterSelection> Variants(int edgeCount) => edgeCount == 1
            ? new[] { MiterSelection.SketchFirst, MiterSelection.ChainFirst, MiterSelection.AllOnMark0 }
            : new[] { MiterSelection.SketchFirst, MiterSelection.ChainFirst };

        /// <summary>The marks of a variant: the sketch's, the edges', and whether the edges come first.</summary>
        public static (int sketchMark, int edgeMark, bool chainFirst) Marks(MiterSelection s) => s switch
        {
            MiterSelection.ChainFirst => (0, 1, true),
            MiterSelection.AllOnMark0 => (0, 0, false),
            _ => (0, 1, false),
        };

        /// <summary>For the report: how the flange was selected.</summary>
        public static string Describe(MiterSelection s) => s switch
        {
            MiterSelection.ChainFirst => "Kanten zuerst (Markierung 1), Skizze danach (0)",
            MiterSelection.AllOnMark0 => "Skizze und Kante mit Markierung 0",
            _ => "Skizze (Markierung 0), Kanten (1)",
        };

        /// <summary>
        /// „Start-/Endversatz getauscht“ is offered to the check only for a partial flange whose two
        /// offsets differ; with equal ones the swap changes nothing.
        /// </summary>
        public static bool OffersSwap(double startOffset, double endOffset) => Math.Abs(startOffset - endOffset) > Tol.Length;

        /// <summary>The note for a flange with another number of edges than the original's – such a variant is removed again.</summary>
        public static string EdgeCountMismatch(int want, int got) =>
            $"Gehrungslasche mit {got} statt {want} Kante{(want == 1 ? "" : "n")} entstanden; wieder entfernt.";

        /// <summary>What the flange hands down to its bends: radius, own allowance and own relief (null: the default – then every own one counts).</summary>
        public static BendValues Parent(MiterFlangeRecord r) => new BendValues
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
        /// own): radius (its value only if the flange has its own), position, side bends, gap, the two
        /// offsets and the relief.
        /// </summary>
        public static Deviations Differences(MiterFlangeRecord want, MiterFlangeRecord got, IFormatProvider? culture = null)
        {
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Standard-Biegeradius", want.UseDefaultRadius ? 1 : 0, got.UseDefaultRadius ? 1 : 0, ScalarKind.Flag),
                ("Laschenposition", want.PositionType, got.PositionType, ScalarKind.Choice),
                ("Seitliche Biegungen trimmen", want.TrimSideBends ? 1 : 0, got.TrimSideBends ? 1 : 0, ScalarKind.Flag),
                ("Spaltabstand", want.GapDistance, got.GapDistance, ScalarKind.Length),
                ("Startversatz", want.StartOffset, got.StartOffset, ScalarKind.Length),
                ("Endversatz", want.EndOffset, got.EndOffset, ScalarKind.Length),
            };
            if (!want.UseDefaultRadius && !got.UseDefaultRadius) values.Insert(1, ("Biegeradius", want.BendRadius, got.BendRadius, ScalarKind.Length));
            var d = new Deviations();
            d.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            d.Add(SheetMetalCompare.Relief(want.OwnRelief, got.OwnRelief, culture));
            return d;
        }
    }
}
