using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Build.Builders;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
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
    ///
    /// The second half of a mirror feature is checked as one step of the job (all its features
    /// together, against the mirror feature of the original); its own steps as their seeds allow
    /// (<see cref="BuildContext.Check"/>): step by step keeping only a variant that passes, or
    /// for information only.
    /// </summary>
    internal static class StepVerifier
    {
        /// <summary>A face sample counts as hit within this distance (as the centroid tolerance, 10 µm).</summary>
        private const double ShapeTol = 1e-5;

        public static string Verify(BuildContext ctx, FeatureRecord record, BuildResult result, MassState before)
        {
            // Original also changed other bodies: nothing to compare with, so no variant search either.
            if (!ctx.Options.VerifySteps || ctx.Check == StepCheck.Off || !record.ChangesSolid || record.OtherBodies.Count > 0 || !result.Success)
                return "";
            bool informOnly = ctx.Check == StepCheck.ReportOnly;
            if (record.ExpectedChange == null)
            {
                // Its first half was not checked either; the mirror feature as a whole is.
                if (informOnly) return "nicht geprüft";
                // MassCapture could not roll the original back to this feature: say so instead of a silent OK.
                result.Notes.Add("Nicht geprüft: Das Original ließ sich für die Messung nicht bis zu diesem Feature zurückrollen.");
                if (result.Status == ReportStatus.Ok) result.Status = ReportStatus.Warning;
                return "nicht geprüft";
            }

            MassState expected = record.ExpectedChange.Value.Mirror(ctx.Mirror);
            List<Vec3> samples = record.FaceSamples.Select(ctx.Mirror.Apply).ToList();
            IReadOnlyList<Feature> features = result.AllFeatures;
            Outcome best = Measure(ctx, expected, before, features, samples);
            // Halves that interact: the step is shown, the mirror feature decides.
            if (informOnly) return MirrorFeatureRules.InformationOnly(best.ToString(), ctx.MirrorName ?? "");
            if (best.Passes) return best.ToString();
            bool passOnly = ctx.Check == StepCheck.PassOnly;

            // Every alternative kept stays applied. The toggles are self-inverse and commute, so
            // what is changed in the end is each single change kept an odd number of times.
            var chosen = new List<string>();
            foreach (Alternative alt in result.Alternatives)
            {
                if (!alt.Toggle()) continue;
                ctx.Doc.EditRebuild3();
                ctx.Topology.Invalidate();
                Outcome o = Measure(ctx, expected, before, features, samples);
                // A second half keeps a variant only if it is right: a better but still wrong one
                // may just make up for an interaction the step on its own cannot see.
                if (passOnly ? o.Passes : o.IsBetterThan(best))
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
                    best = Measure(ctx, expected, before, features, samples);
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
                if (result.MismatchHint != null) result.Notes.Add(result.MismatchHint);
            }
            return best.ToString();
        }

        /// <param name="features">The step's feature, or all features of a mirror feature's second half.</param>
        private static Outcome Measure(BuildContext ctx, MassState expected, MassState before, IReadOnlyList<Feature> features, List<Vec3> samples)
        {
            MassState after = SwDoc.MassOf(ctx.Doc);
            MassComparison mass = MassComparison.Compare(expected, after - before, after);
            // Of a second half its features, not its sketches – as a single step checks its feature, not its sketch.
            bool error = features.Where(f => features.Count == 1 || !SwDoc.IsSketch(f)).Any(f =>
            {
                bool isWarning = false;
                return f.GetErrorCode2(out isWarning) != 0 && !isWarning;
            });
            int missed = error ? samples.Count : MissedSamples(ctx, features, samples);
            return new Outcome(mass, error, missed, samples.Count);
        }

        /// <summary>
        /// Samples that lie on no face: the faces of the step's own features are tried first
        /// (cheap), then every face of the bodies that may carry it – a coplanar face merged into
        /// an older one belongs to that one. In "separate body" mode only the mirrored bodies
        /// count, the original may well have a face at the same place.
        /// </summary>
        private static int MissedSamples(BuildContext ctx, IReadOnlyList<Feature> features, List<Vec3> samples)
        {
            if (samples.Count == 0) return 0;
            List<Face2> own = features.SelectMany(f => ((object)f.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>()).ToList();
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
