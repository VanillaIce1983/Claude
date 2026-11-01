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
    }
}
