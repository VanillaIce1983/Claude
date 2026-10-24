using SolidWorks.Interop.sldworks;
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
            Feature feature = result.Feature!;
            MassComparison best = Measure(ctx, expected, before);
            double bestScore = Score(best, feature);
            if (best.Passes && !HasError(feature)) return best.ToString();

            string chosen = "";
            foreach (Alternative alt in result.Alternatives)
            {
                if (!alt.Toggle()) continue;
                ctx.Doc.EditRebuild3();
                MassComparison cmp = Measure(ctx, expected, before);
                double score = Score(cmp, feature);
                if (score < bestScore)
                {
                    best = cmp;
                    bestScore = score;
                    chosen = alt.Description;
                    if (cmp.Passes && !HasError(feature)) break;
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

        private static MassComparison Measure(BuildContext ctx, MassState expected, MassState before)
        {
            MassState after = SwDoc.MassOf(ctx.Doc);
            return MassComparison.Compare(expected, after - before, after);
        }

        /// <summary>A variant that leaves the feature in error never wins, even if it happens to change nothing.</summary>
        private static double Score(MassComparison cmp, Feature f) => HasError(f) ? double.PositiveInfinity : cmp.Score;

        private static bool HasError(Feature f)
        {
            bool isWarning = false;
            return f.GetErrorCode2(out isWarning) != 0 && !isWarning;
        }
    }
}
