using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
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
        public static List<Feature> Collect(ModelDoc2 doc, SwMath math, IReadOnlyCollection<Feature> features, IReadOnlyCollection<Body2> bodies,
            List<(Feature feature, string reason)> skipped)
        {
            List<Feature> all = SwDoc.FeaturesInCreationOrder(doc);
            var standard = new HashSet<string>(SwDoc.StandardPlanes(doc, math).Select(f => f.Name), StringComparer.Ordinal);
            List<Feature> result = Select(doc, all, standard, features, bodies);

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

        private static List<Feature> Select(ModelDoc2 doc, List<Feature> all, HashSet<string> standard, IReadOnlyCollection<Feature> features, IReadOnlyCollection<Body2> bodies)
        {
            if (features.Count == 0 && bodies.Count == 0)
                return all.Where(f => !SwDoc.IsSystemFeature(f) && !standard.Contains(f.Name)).ToList();

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (Feature f in features) wanted.Add(f.Name);
            foreach (Body2 b in bodies)
                if (b.GetFeatures() is object[] bodyFeatures)
                    foreach (Feature f in bodyFeatures.OfType<Feature>()) wanted.Add(f.Name);

            // Seeds of wanted patterns (also of patterns of patterns).
            var queue = new Queue<Feature>(all.Where(x => wanted.Contains(x.Name)));
            while (queue.Count > 0)
                foreach (Feature seed in SeedsOf(doc, queue.Dequeue()))
                    if (wanted.Add(seed.Name)) queue.Enqueue(seed);

            // Sketches the wanted features are built from (absorbed or shared).
            foreach (Feature f in all.Where(x => wanted.Contains(x.Name)).ToList())
            {
                foreach (Feature sub in SwDoc.SubFeatures(f).Where(SwDoc.IsSketch)) wanted.Add(sub.Name);
                if (f.GetParents() is object[] parents)
                    foreach (Feature p in parents.OfType<Feature>().Where(SwDoc.IsSketch)) wanted.Add(p.Name);
            }

            return all.Where(f => wanted.Contains(f.Name) && !SwDoc.IsSystemFeature(f) && !standard.Contains(f.Name)).ToList();
        }

        /// <summary>The features a linear or circular pattern repeats (none for face and body patterns).</summary>
        private static IEnumerable<Feature> SeedsOf(ModelDoc2 doc, Feature f)
        {
            object? seeds = null;
            switch (f.GetDefinition())
            {
                case LinearPatternFeatureData lp when lp.AccessSelections(doc, null):
                    try
                    {
                        seeds = lp.PatternFeatureArray;
                    }
                    finally
                    {
                        lp.ReleaseSelectionAccess();
                    }
                    break;
                case CircularPatternFeatureData cp when cp.AccessSelections(doc, null):
                    try
                    {
                        seeds = cp.PatternFeatureArray;
                    }
                    finally
                    {
                        cp.ReleaseSelectionAccess();
                    }
                    break;
            }
            return seeds is object[] list ? list.OfType<Feature>().ToList() : Enumerable.Empty<Feature>();
        }
    }
}
