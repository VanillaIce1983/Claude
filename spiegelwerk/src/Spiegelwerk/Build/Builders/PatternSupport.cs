using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// What every pattern builder needs: the seeds in the target, the seed selection, the
    /// comparison of the instance placements with the mirrored ones of the original (and the
    /// flag toggles until they agree), and the feature scope in "separate body" mode.
    /// </summary>
    internal static class PatternSupport
    {
        /// <summary>
        /// Selection marks of the seeds, the same for all pattern types: features 4 per the API
        /// help; faces 128 and bodies 256 are not documented for every type (STOLPERSTEINE 65).
        /// </summary>
        public const int FeatureMark = 4, FaceMark = 128, BodyMark = 256;

        public sealed class Seeds
        {
            public List<Feature> Features { get; } = new List<Feature>();
            public List<Face2> Faces { get; } = new List<Face2>();
            public List<Body2> Bodies { get; } = new List<Body2>();
        }

        public static string NoSeeds(PatternRecord r) =>
            "Keines der Saat-Elemente des Musters wurde im Ziel gefunden" +
            (r.SeedFeatures.Count == 0 ? " (bei Flächen- und Körpermustern müssen die Features, aus denen die Saat stammt, mitgespiegelt werden)." : ".");

        public static Seeds? FindSeeds(BuildContext ctx, PatternRecord r, List<string> notes)
        {
            var seeds = new Seeds();
            var missing = new List<string>();
            foreach (string name in r.SeedFeatures)
            {
                if (ctx.Created.TryGetValue(name, out Feature f)) seeds.Features.Add(f);
                else missing.Add($"\"{name}\"");
            }
            foreach (var face in r.SeedFaces)
            {
                Face2? found = ctx.Topology.FindFace(face.Mirror(ctx.Mirror), notes);
                if (found != null) seeds.Faces.Add(Safe(found) as Face2 ?? found);
                else missing.Add("eine Fläche");
            }
            foreach (MassState body in r.SeedBodies)
            {
                Body2? found = FindBody(ctx, body.Mirror(ctx.Mirror));
                if (found != null) seeds.Bodies.Add(found);
                else missing.Add("ein Körper");
            }
            if (missing.Count > 0)
                notes.Add($"Nicht gespiegelt und daher nicht gemustert: {string.Join(", ", missing)}.");
            return seeds.Features.Count + seeds.Faces.Count + seeds.Bodies.Count == 0 ? null : seeds;
        }

        /// <summary>The body with the given volume and centroid; bodies of this job first.</summary>
        private static Body2? FindBody(BuildContext ctx, MassState wanted)
        {
            Body2? fallback = null;
            foreach (Body2 b in SwDoc.SolidBodies(ctx.Doc))
            {
                if (!MassComparison.Compare(wanted, SwDoc.MassOf(new[] { b })).Passes) continue;
                if (ctx.IsOwnBody(b)) return b;
                if (!ctx.SamePart) fallback ??= b;
            }
            return fallback;
        }

        /// <summary>Adds the seeds to the selection with their marks; false if one could not be selected.</summary>
        public static bool SelectSeeds(ModelDoc2 doc, Seeds seeds)
        {
            bool selected = true;
            foreach (Feature f in seeds.Features) selected &= SwSelect.Select(doc, f, FeatureMark, true);
            foreach (Face2 face in seeds.Faces) selected &= SwSelect.Select(doc, face, FaceMark, true);
            foreach (Body2 body in seeds.Bodies) selected &= SwSelect.Select(doc, body, BodyMark, true);
            return selected;
        }

        /// <summary>Seeds into a feature data object (CreateDefinition route).</summary>
        public static void SetSeeds(Seeds seeds, PatternRecord r, Action<object> features, Action<object> faces, Action<object> bodies, Action<bool> bodyPattern)
        {
            if (r.BodyPattern)
            {
                bodyPattern(true);
                bodies(seeds.Bodies.Cast<object>().ToArray());
                return;
            }
            if (seeds.Features.Count > 0) features(seeds.Features.Cast<object>().ToArray());
            if (seeds.Faces.Count > 0) faces(seeds.Faces.Cast<object>().ToArray());
        }

        /// <summary>
        /// Compares the placements of the new pattern (<paramref name="read"/>) with the mirrored
        /// ones of the original and tries the flag combinations (<paramref name="toggles"/>,
        /// applied on top of each other) if they differ. Returns a remark for the report.
        /// <paramref name="confirmed"/>: the placements settle the direction flags – all match,
        /// at least one instance is not the seed itself, and no instance is skipped (whether
        /// skipped instances report a placement is not documented, so with skips the sets
        /// might agree although the wrong instance is left out).
        /// </summary>
        public static string MatchPlacements(BuildContext ctx, PatternRecord r, Func<List<Affine>?> read, IReadOnlyList<Func<bool>> toggles,
            List<string> notes, out bool confirmed)
        {
            confirmed = false;
            if (r.Instances.Count == 0) return "";
            List<Affine> wanted = PatternInstances.Mirror(r.Instances, ctx.Mirror);
            List<Affine>? actual = read();
            if (actual == null || actual.Count == 0) return "";
            bool conclusive = r.SkippedItems.Count == 0 && wanted.Any(t => !t.AlmostEquals(Affine.Identity));
            int best = PatternInstances.Unmatched(wanted, actual);
            if (best == 0)
            {
                confirmed = conclusive;
                return ", Lage der Instanzen geprüft";
            }

            int applied = 0, bestAt = 0;
            foreach (Func<bool> toggle in toggles)
            {
                if (!toggle()) break;
                applied++;
                List<Affine>? now = read();
                int unmatched = now == null ? int.MaxValue : PatternInstances.Unmatched(wanted, now);
                if (unmatched < best)
                {
                    best = unmatched;
                    bestAt = applied;
                }
                if (best == 0) break;
            }
            // Back to the best combination seen (undo the toggles applied after it).
            int at = applied;
            while (at > bestAt && toggles[at - 1]()) at--;
            ctx.Topology.Invalidate();
            if (at != bestAt)
            {
                // A toggle could not be taken back: the pattern stays as it is now, so judge that.
                List<Affine>? now = read();
                best = now == null ? int.MaxValue : PatternInstances.Unmatched(wanted, now);
                bestAt = at;
                notes.Add("Eine probeweise umgekehrte Musterrichtung ließ sich nicht zurücknehmen und bleibt.");
            }

            if (best == 0)
            {
                confirmed = conclusive;
                return bestAt > 0 ? ", Richtung nach Lage der Instanzen korrigiert" : ", Lage der Instanzen geprüft";
            }
            notes.Add($"{Math.Min(best, wanted.Count)} von {wanted.Count} Instanzen liegen nicht an der gespiegelten Stelle.");
            return "";
        }

        /// <summary>
        /// Separate body: limits the instances to the mirrored bodies. Pattern data has a
        /// feature scope only in newer API versions (2024 interop yes, 2019 no); without it the
        /// check of the original bodies in MirrorJob is what keeps the original untouched.
        /// <paramref name="edit"/> runs an action on the feature data within one FeatureEdit.
        /// </summary>
        public static void LimitToOwnBodies(BuildContext ctx, Feature f, Func<Action<object>, bool> edit, List<string> notes)
        {
            if (!(SwLate.Get(f.GetDefinition(), "FeatureScope") is bool)) return;
            bool set = false;
            bool ok = edit(d => set = SwLate.Call(d, "SetFeatureScope", true, false, OwnBodiesBeforeFeature(ctx)));
            if (!ok || !set) notes.Add("Der Feature-Bereich des Musters ließ sich nicht auf den gespiegelten Körper beschränken.");
        }

        public static string SeedText(PatternRecord r) =>
            r.BodyPattern ? $"{r.SeedBodies.Count} Körper" : $"{r.SeedFeatures.Count + r.SeedFaces.Count} Saat-Elemente";
    }
}
