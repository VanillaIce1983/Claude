using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Build.Builders;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build
{
    /// <summary>
    /// Compares what a rebuilt feature did to the solid bodies (volume, centroid of the
    /// change) with the mirrored change of the original feature, and checks that the mirrored
    /// face samples of the original lie on faces of the result. If either does not match, the
    /// builder's alternatives are tried one by one and the best variant is kept. This turns
    /// "which way does SOLIDWORKS put this by default" from a guess into a measurement.
    ///
    /// The face check exists for variants the masses cannot tell apart: a sweep twisted the
    /// wrong way round has the same volume and – with a symmetric profile – the same centroid.
    /// </summary>
    internal static class StepVerifier
    {
        /// <summary>A face sample counts as hit within this distance (as the centroid tolerance, 10 µm).</summary>
        private const double ShapeTol = 1e-5;

        public static string Verify(BuildContext ctx, FeatureRecord record, BuildResult result, MassState before)
        {
            // Original also changed other bodies: nothing to compare with, so no variant search either.
            if (!ctx.Options.VerifySteps || !record.ChangesSolid || record.OtherBodies.Count > 0 || !result.Success)
                return "";
            // MassCapture could not roll the original back to this feature: say so instead of a silent OK.
            if (record.ExpectedChange == null)
            {
                result.Notes.Add("Nicht geprüft: Das Original ließ sich für die Messung nicht bis zu diesem Feature zurückrollen.");
                if (result.Status == ReportStatus.Ok) result.Status = ReportStatus.Warning;
                return "nicht geprüft";
            }

            MassState expected = record.ExpectedChange.Value.Mirror(ctx.Mirror);
            List<Vec3> samples = record.FaceSamples.Select(ctx.Mirror.Apply).ToList();
            Feature feature = result.Feature!;
            Outcome best = Measure(ctx, expected, before, feature, samples);
            if (best.Passes) return best.ToString();

            // Every alternative kept stays applied. The toggles are self-inverse and commute, so
            // what is changed in the end is each single change kept an odd number of times.
            var chosen = new List<string>();
            foreach (Alternative alt in result.Alternatives)
            {
                if (!alt.Toggle()) continue;
                ctx.Doc.EditRebuild3();
                ctx.Topology.Invalidate();
                Outcome o = Measure(ctx, expected, before, feature, samples);
                if (o.IsBetterThan(best))
                {
                    best = o;
                    foreach (string part in alt.Parts)
                        if (!chosen.Remove(part)) chosen.Add(part);
                    if (o.Passes) break;
                }
                else if (!alt.Toggle())
                {
                    // Not taken back: the feature stays in this variant, so that is what is measured and reported.
                    ctx.Doc.EditRebuild3();
                    ctx.Topology.Invalidate();
                    best = Measure(ctx, expected, before, feature, samples);
                    foreach (string part in alt.Parts)
                        if (!chosen.Remove(part)) chosen.Add(part);
                    result.Notes.Add($"„{alt.Description}“ ließ sich nach der Messung nicht zurücknehmen und bleibt; weitere Varianten wurden nicht probiert.");
                    break;
                }
                else
                {
                    ctx.Doc.EditRebuild3();
                }
            }
            ctx.Topology.Invalidate();

            if (chosen.Count > 0) result.Notes.Add($"Korrigiert nach Messung: {string.Join(", ", chosen)}.");
            if (!best.Passes)
            {
                result.Status = ReportStatus.Warning;
                result.Notes.Add("Das Feature weicht vom gespiegelten Original ab – bitte prüfen.");
            }
            return best.ToString();
        }

        private static Outcome Measure(BuildContext ctx, MassState expected, MassState before, Feature feature, List<Vec3> samples)
        {
            MassState after = SwDoc.MassOf(ctx.Doc);
            MassComparison mass = MassComparison.Compare(expected, after - before, after);
            bool isWarning = false;
            bool error = feature.GetErrorCode2(out isWarning) != 0 && !isWarning;
            int missed = error ? samples.Count : MissedSamples(ctx, feature, samples);
            return new Outcome(mass, error, missed, samples.Count);
        }

        /// <summary>
        /// Samples that lie on no face: the feature's own faces are tried first (cheap), then
        /// every face of the bodies that may carry it – a coplanar face merged into an older
        /// one belongs to that one. In "separate body" mode only the mirrored bodies count, the
        /// original may well have a face at the same place.
        /// </summary>
        private static int MissedSamples(BuildContext ctx, Feature feature, List<Vec3> samples)
        {
            if (samples.Count == 0) return 0;
            List<Face2> own = (feature.GetFaces() as object[])?.OfType<Face2>().ToList() ?? new List<Face2>();
            List<Face2>? all = null;
            int missed = 0;
            foreach (Vec3 p in samples)
            {
                if (own.Any(face => TopologyCapture.DistanceToFace(face, p) <= ShapeTol)) continue;
                all ??= (ctx.SeparateBody ? ctx.OwnBodies() : SwDoc.SolidBodies(ctx.Doc))
                    .SelectMany(b => (b.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>())
                    .ToList();
                if (!all.Any(face => TopologyCapture.DistanceToFace(face, p) <= ShapeTol)) missed++;
            }
            return missed;
        }

        private readonly struct Outcome
        {
            public Outcome(MassComparison mass, bool error, int missed, int samples)
            {
                Mass = mass;
                Error = error;
                Missed = missed;
                Samples = samples;
            }

            public MassComparison Mass { get; }

            /// <summary>The feature is in error; such a variant never wins, even if it happens to change nothing.</summary>
            public bool Error { get; }

            public int Missed { get; }
            public int Samples { get; }

            public bool Passes => !Error && Mass.Passes && Missed == 0;

            private int FailedChecks => (Error ? 4 : 0) + (Mass.Passes ? 0 : 1) + (Missed == 0 ? 0 : 1);

            /// <summary>Fewer failed checks, then fewer missed samples, then the smaller mass deviation.</summary>
            public bool IsBetterThan(Outcome o)
            {
                if (FailedChecks != o.FailedChecks) return FailedChecks < o.FailedChecks;
                if (Missed != o.Missed) return Missed < o.Missed;
                return Mass.Score < o.Mass.Score;
            }

            public override string ToString()
            {
                string text = Error ? "Feature fehlerhaft" : Mass.ToString();
                if (Samples == 0 || Error) return text;
                return text + (Missed == 0 ? ", Form stimmt" : $", Form: {Missed} von {Samples} Flächenproben nicht getroffen");
            }
        }
    }
}
