using System;
using System.Collections.Generic;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Bend sub-features with values of their own. Writes to a bend sub-feature are ignored
    /// (ModifyDefinition returns true and nothing changes), so the copy's bends take what
    /// their owner hands down; each bend that had more becomes a note, never a write.
    /// </summary>
    public static class BendOverrides
    {
        /// <summary>
        /// The bends whose own radius, allowance or relief differs from what <paramref name="parent"/>
        /// hands down. A bend that uses the default never differs. If the owner's allowance or
        /// relief is not known (null), every own one counts – the copy cannot have it either way.
        /// </summary>
        public static List<BendOverride> Differences(BendValues parent, IEnumerable<BendValues> bends, IFormatProvider? culture = null)
        {
            var result = new List<BendOverride>();
            foreach (BendValues bend in bends)
            {
                var what = new List<string>();
                if (!bend.UseDefaultRadius && Math.Abs(bend.Radius - parent.Radius) > SheetMetalCompare.LengthTolerance)
                    what.Add($"einen eigenen Biegeradius ({SheetMetalCompare.Mm(bend.Radius, culture)})");
                if (!bend.UseDefaultAllowance)
                {
                    if (bend.Allowance == null) what.Add("eine eigene Biegezugabe (nicht lesbar)");
                    else if (parent.Allowance == null || !SheetMetalCompare.Allowance(parent.Allowance, bend.Allowance, culture).None)
                        what.Add($"eine eigene Biegezugabe ({SheetMetalCompare.Describe(bend.Allowance, culture)})");
                }
                if (!bend.UseDefaultRelief)
                {
                    if (bend.Relief == null) what.Add("eine eigene Entlastung (nicht lesbar)");
                    else if (parent.Relief == null || !SheetMetalCompare.Relief(parent.Relief, bend.Relief, culture).None)
                        what.Add($"eine eigene Entlastung ({SheetMetalCompare.Describe(bend.Relief, culture)})");
                }
                if (what.Count > 0) result.Add(new BendOverride(bend.Name, SheetMetalCompare.JoinGerman(what)));
            }
            return result;
        }

        /// <summary>
        /// The copy's bends with values of their own that the original's do not have – compared by
        /// what differs, not by name (the copy's bends have names of their own, and a reflection may
        /// reverse their order). What the original has is already a note of its own; this names
        /// only what the copy brought in, e.g. a relief the creation call set.
        /// </summary>
        public static List<string> UnexpectedInCopy(IEnumerable<BendOverride> original, IEnumerable<BendOverride> copy)
        {
            var left = new List<string>();
            foreach (BendOverride o in original) left.Add(o.What);
            var result = new List<string>();
            foreach (BendOverride c in copy)
                if (!left.Remove(c.What))
                    result.Add($"Biegung „{c.Bend}“ der Kopie hat {c.What}; im Original hat keine Biegung das.");
            return result;
        }

        /// <summary>The note for a copy with another number of bends than the original (counted by definition); null if equal.</summary>
        public static string? CountNote(int original, int copy) =>
            original == copy ? null : $"Die Kopie hat {copy} Biegung{(copy == 1 ? "" : "en")}, das Original {original}.";

        /// <summary>
        /// Bends whose place in the bend sequence differs from their counterpart's in the copy
        /// (pairs from the bend matching, both read back). Information only, never a warning: the
        /// sequence orders a drawing's bend table and does not change the part, SOLIDWORKS numbers
        /// the copy's bends itself, and a bend's own order cannot be written either. A pair with
        /// an unread order says nothing.
        /// </summary>
        public static List<string> OrderNotes(IEnumerable<(BendValues original, BendValues copy)> pairs)
        {
            var result = new List<string>();
            foreach (var (original, copy) in pairs)
                if (original.BendOrder is int was && copy.BendOrder is int now && was != now)
                    result.Add($"Biegung „{original.Name}“ steht in der Biegereihenfolge an Stelle {was}, ihr Gegenstück „{copy.Name}“ an Stelle {now}.");
            return result;
        }
    }
}
