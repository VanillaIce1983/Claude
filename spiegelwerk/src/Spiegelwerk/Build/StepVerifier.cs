using Spiegelwerk.Build.Builders;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build
{
    /// <summary>
    /// Compares what a rebuilt feature did to the solid bodies (volume, centroid of the
    /// change) with the mirrored change of the original feature. If it does not match, the
    /// builder's alternatives are tried one by one and the best variant is kept. This turns
    /// "which way does SOLIDWORKS put this by default" from a guess into a measurement.
    /// </summary>
    internal static class StepVerifier
    {
        public static string Verify(BuildContext ctx, FeatureRecord record, BuildResult result, MassState before)
        {
            if (!ctx.Options.VerifySteps || !record.ChangesSolid || record.ExpectedChange == null || !result.Success)
                return "";

            MassState expected = record.ExpectedChange.Value.Mirror(ctx.Mirror);
            MassComparison best = Measure(ctx, expected, before);
            if (best.Passes) return best.ToString();

            string chosen = "";
            foreach (Alternative alt in result.Alternatives)
            {
                if (!alt.Toggle()) continue;
                ctx.Doc.EditRebuild3();
                MassComparison cmp = Measure(ctx, expected, before);
                if (cmp.Score < best.Score)
                {
                    best = cmp;
                    chosen = alt.Description;
                    if (cmp.Passes) break;
                }
                else
                {
                    alt.Toggle();
                    ctx.Doc.EditRebuild3();
                }
            }
            ctx.Topology.Invalidate();

            if (chosen.Length > 0) result.Notes.Add($"Korrigiert nach Messung: {chosen}.");
            if (!best.Passes)
            {
                result.Status = ReportStatus.Warning;
                result.Notes.Add("Das Feature weicht vom gespiegelten Original ab – bitte prüfen.");
            }
            return best.ToString();
        }

        private static MassComparison Measure(BuildContext ctx, MassState expected, MassState before) =>
            MassComparison.Compare(expected, SwDoc.MassOf(ctx.Doc) - before);
    }
}
