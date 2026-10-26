using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Decides which features of the original are rebuilt, in creation order.
    ///  - nothing selected: the whole part (all features except the standard planes and folders)
    ///  - features selected: those features plus the sketches they consume
    ///  - bodies selected: every feature that contributed to those bodies, plus their sketches
    /// Parents other than sketches are deliberately not pulled in: a mirrored cut is meant to
    /// cut the existing body, not to rebuild the body it was cut from. The one exception are
    /// the seeds of a pattern: a pattern without its seed has nothing to repeat.
    /// </summary>
    internal static class FeatureCollector
    {
        /// <param name="skipped">
        /// Features that are left out on purpose: suppressed in the active configuration (also
        /// their absorbed sketches) and features already failing in the original. Rebuilding a
        /// suppressed feature unsuppressed would change the part.
        /// </param>
        /// <param name="seedBodies">
        /// The features of the seed bodies of a selected body pattern were pulled in: like
        /// selected bodies, they have to be rebuilt as a body of their own.
        /// </param>
        /// <param name="scope">What the features pulled in through bodies also did to other bodies.</param>
        public static List<Feature> Collect(ModelDoc2 doc, SwMath math, IReadOnlyCollection<Feature> features, IReadOnlyCollection<Body2> bodies,
            List<(Feature feature, string reason)> skipped, out bool seedBodies, out BodyScope scope)
        {
            List<Feature> all = SwDoc.FeaturesInCreationOrder(doc);
            var standard = new HashSet<string>(SwDoc.StandardPlanes(doc, math).Select(f => f.Name), StringComparer.Ordinal);
            var selectedBodies = new HashSet<string>(StringComparer.Ordinal);
            List<Feature> result = Select(doc, all, standard, features, bodies, selectedBodies, out seedBodies);
            scope = bodies.Count > 0 || seedBodies ? ScopeOf(doc, all, result, features, selectedBodies) : BodyScope.None;

            var kept = new List<Feature>();
            var skippedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Feature f in result)
            {
                string? reason = SkipReason(f) ?? SkippedSketchOf(f, skippedNames);
                if (reason == null)
                {
                    kept.Add(f);
                }
                else
                {
                    skipped.Add((f, reason));
                    skippedNames.Add(f.Name);
                }
            }
            return kept;
        }

        /// <summary>
        /// Suppressed features, and features (not sketches) that fail in the original. A sketch
        /// with an error may still feed a healthy feature; the copy gets its own status check.
        /// </summary>
        private static string? SkipReason(Feature f)
        {
            if (f.IsSuppressed()) return "Im Original unterdrückt – nicht gespiegelt.";
            if (SwDoc.IsSketch(f)) return null;
            bool isWarning = false;
            int code = f.GetErrorCode2(out isWarning);
            if (code != 0 && !isWarning) return $"Im Original fehlerhaft (Code {code}) – nicht gespiegelt.";
            return null;
        }

        /// <summary>A feature whose sketch was left out cannot be built either; say why.</summary>
        private static string? SkippedSketchOf(Feature f, HashSet<string> skippedNames)
        {
            IEnumerable<Feature> sketches = SwDoc.SubFeatures(f).Where(SwDoc.IsSketch);
            if (f.GetParents() is object[] parents) sketches = sketches.Concat(parents.OfType<Feature>().Where(SwDoc.IsSketch));
            Feature? missing = sketches.FirstOrDefault(s => skippedNames.Contains(s.Name));
            return missing == null ? null : $"Skizze \"{missing.Name}\" wurde übersprungen – nicht gespiegelt.";
        }

        /// <param name="selectedBodies">Receives the names of the selected bodies and of the seed bodies pulled in.</param>
        private static List<Feature> Select(ModelDoc2 doc, List<Feature> all, HashSet<string> standard, IReadOnlyCollection<Feature> features,
            IReadOnlyCollection<Body2> bodies, HashSet<string> selectedBodies, out bool seedBodies)
        {
            seedBodies = false;
            if (features.Count == 0 && bodies.Count == 0)
                return all.Where(f => !SwDoc.IsSystemFeature(f) && !standard.Contains(f.Name)).ToList();

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (Feature f in features) wanted.Add(f.Name);
            foreach (Body2 b in bodies)
            {
                selectedBodies.Add(b.Name);
                if (b.GetFeatures() is object[] bodyFeatures)
                    foreach (Feature f in bodyFeatures.OfType<Feature>()) wanted.Add(f.Name);
            }

            // Seeds of wanted patterns (also of patterns of patterns).
            var queue = new Queue<Feature>(all.Where(x => wanted.Contains(x.Name)));
            while (queue.Count > 0)
                foreach (Feature seed in SeedsOf(doc, queue.Dequeue(), selectedBodies, ref seedBodies))
                    if (wanted.Add(seed.Name)) queue.Enqueue(seed);

            // Sketches the wanted features are built from (absorbed or shared). Planes, axes and
            // coordinate systems are rebuilt from their geometry alone; the 2D sketches they sit
            // on still come along – another collected sketch may refer to them –, but not a 3D
            // sketch: it cannot be rebuilt, and as a feature without an equivalent it would keep
            // "delete original" from running.
            foreach (Feature f in all.Where(x => wanted.Contains(x.Name)).ToList())
            {
                bool datum = IsDatum(f);
                foreach (Feature sub in SwDoc.SubFeatures(f).Where(SwDoc.IsSketch)) wanted.Add(sub.Name);
                if (f.GetParents() is object[] parents)
                    foreach (Feature p in parents.OfType<Feature>().Where(SwDoc.IsSketch))
                        if (!datum || p.GetTypeName2() != "3DProfileFeature") wanted.Add(p.Name);
            }

            return all.Where(f => wanted.Contains(f.Name) && !SwDoc.IsSystemFeature(f) && !standard.Contains(f.Name)).ToList();
        }

        private static bool IsDatum(Feature f) => f.GetTypeName2() is "RefPlane" or "RefAxis" or "CoordSys";

        /// <summary>
        /// Every solid body of the part with the features that made it, judged against what was
        /// collected. A seed body is named as it was before its pattern; if a later feature
        /// renamed it (bodies take the name of their last feature), it counts as not selected –
        /// the cautious side.
        /// </summary>
        private static BodyScope ScopeOf(ModelDoc2 doc, List<Feature> all, List<Feature> collected, IReadOnlyCollection<Feature> chosen,
            HashSet<string> selectedBodies)
        {
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < all.Count; i++) order[all[i].Name] = i;
            var bodies = new List<BodyHistory>();
            foreach (Body2 b in SwDoc.SolidBodies(doc))
            {
                // Sketches and reference geometry change no body; sub-features outside the tree walk (threads) neither.
                IEnumerable<Feature> made = b.GetFeatures() is object[] fs ? fs.OfType<Feature>() : Enumerable.Empty<Feature>();
                List<string> names = made.Where(f => order.ContainsKey(f.Name) && !SwDoc.IsSketch(f) && !IsDatum(f) && !SwDoc.IsSystemFeature(f))
                    .Select(f => f.Name).Distinct().OrderBy(n => order[n]).ToList();
                bodies.Add(new BodyHistory(b.Name, names, selectedBodies.Contains(b.Name)));
            }
            return BodyScope.Of(bodies, collected.Select(f => f.Name), chosen.Select(f => f.Name));
        }

        /// <summary>
        /// What a pattern (linear, circular, curve, sketch, table driven, fill, variable)
        /// repeats: its seed features, the features the seed faces belong to, the features
        /// that made the seed bodies (as when bodies are selected).
        /// </summary>
        private static List<Feature> SeedsOf(ModelDoc2 doc, Feature f, HashSet<string> seedBodyNames, ref bool seedBodies)
        {
            var result = new List<Feature>();
            switch (f.GetDefinition())
            {
                case LinearPatternFeatureData lp when lp.AccessSelections(doc, null):
                    try
                    {
                        seedBodies |= AddSeeds(result, seedBodyNames, lp.BodyPattern, lp.PatternFeatureArray, lp.PatternFaceArray, lp.PatternBodyArray);
                    }
                    finally
                    {
                        lp.ReleaseSelectionAccess();
                    }
                    break;
                case CircularPatternFeatureData cp when cp.AccessSelections(doc, null):
                    try
                    {
                        seedBodies |= AddSeeds(result, seedBodyNames, cp.BodyPattern, cp.PatternFeatureArray, cp.PatternFaceArray, cp.PatternBodyArray);
                    }
                    finally
                    {
                        cp.ReleaseSelectionAccess();
                    }
                    break;
                case CurveDrivenPatternFeatureData cdp when cdp.AccessSelections(doc, null):
                    try
                    {
                        seedBodies |= AddSeeds(result, seedBodyNames, cdp.BodyPattern, cdp.PatternFeatureArray, cdp.PatternFaceArray, cdp.PatternBodyArray);
                    }
                    finally
                    {
                        cdp.ReleaseSelectionAccess();
                    }
                    break;
                case TablePatternFeatureData tp when tp.AccessSelections(doc, null):
                    try
                    {
                        seedBodies |= AddSeeds(result, seedBodyNames, tp.GetPatternBodyCount() > 0, tp.PatternFeatureArray, tp.PatternFaceArray, tp.PatternBodyArray);
                    }
                    finally
                    {
                        tp.ReleaseSelectionAccess();
                    }
                    break;
                case FillPatternFeatureData fp when fp.AccessSelections(doc, null):
                    try
                    {
                        seedBodies |= AddSeeds(result, seedBodyNames, FeatureCapture.IsBodyFillPattern(fp), fp.PatternFeatureArray, fp.PatternFaceArray, fp.PatternBodyArray);
                    }
                    finally
                    {
                        fp.ReleaseSelectionAccess();
                    }
                    break;
                case DimPatternFeatureData vp:
                    // As in capture: the parents that are not sketches, and the owners of feature dimensions.
                    result.AddRange(FeatureCapture.VariablePatternSeeds(f));
                    if (vp.AccessSelections(doc, null))
                        try
                        {
                            result.AddRange(FeatureCapture.VariablePatternDimensionOwners(doc, vp).ToList());
                        }
                        finally
                        {
                            vp.ReleaseSelectionAccess();
                        }
                    break;
                case SketchPatternFeatureData sp when sp.AccessSelections(doc, null):
                    try
                    {
                        seedBodies |= AddSeeds(result, seedBodyNames, sp.BodyPattern, sp.PatternFeatureArray, sp.PatternFaceArray, sp.PatternBodyArray);
                    }
                    finally
                    {
                        sp.ReleaseSelectionAccess();
                    }
                    break;
            }
            return result;
        }

        /// <returns>True if features of seed bodies were added.</returns>
        private static bool AddSeeds(List<Feature> into, HashSet<string> seedBodyNames, bool bodyPattern, object features, object faces, object bodies)
        {
            if (bodyPattern)
            {
                int before = into.Count;
                if (bodies is object[] bs)
                    foreach (Body2 b in bs.OfType<Body2>())
                    {
                        seedBodyNames.Add(b.Name);
                        if (b.GetFeatures() is object[] made) into.AddRange(made.OfType<Feature>());
                    }
                return into.Count > before;
            }
            if (features is object[] fs) into.AddRange(fs.OfType<Feature>());
            if (faces is object[] fcs)
                foreach (Face2 face in fcs.OfType<Face2>())
                    if (face.GetFeature() is Feature owner) into.Add(owner);
            return false;
        }
    }
}
