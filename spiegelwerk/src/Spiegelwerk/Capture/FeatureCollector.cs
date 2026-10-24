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
    /// cut the existing body, not to rebuild the body it was cut from.
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
            List<Feature> result = Select(all, standard, features, bodies);

            var kept = new List<Feature>();
            foreach (Feature f in result)
            {
                string? reason = SkipReason(f);
                if (reason == null) kept.Add(f);
                else skipped.Add((f, reason));
            }
            return kept;
        }

        private static string? SkipReason(Feature f)
        {
            if (f.IsSuppressed()) return "Im Original unterdrückt – nicht gespiegelt.";
            bool isWarning = false;
            int code = f.GetErrorCode2(out isWarning);
            if (code != 0 && !isWarning) return $"Im Original fehlerhaft (Code {code}) – nicht gespiegelt.";
            return null;
        }

        private static List<Feature> Select(List<Feature> all, HashSet<string> standard, IReadOnlyCollection<Feature> features, IReadOnlyCollection<Body2> bodies)
        {
            if (features.Count == 0 && bodies.Count == 0)
                return all.Where(f => !SwDoc.IsSystemFeature(f) && !standard.Contains(f.Name)).ToList();

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (Feature f in features) wanted.Add(f.Name);
            foreach (Body2 b in bodies)
                if (b.GetFeatures() is object[] bodyFeatures)
                    foreach (Feature f in bodyFeatures.OfType<Feature>()) wanted.Add(f.Name);

            // Sketches the wanted features are built from (absorbed or shared).
            foreach (Feature f in all.Where(x => wanted.Contains(x.Name)).ToList())
            {
                foreach (Feature sub in SwDoc.SubFeatures(f).Where(SwDoc.IsSketch)) wanted.Add(sub.Name);
                if (f.GetParents() is object[] parents)
                    foreach (Feature p in parents.OfType<Feature>().Where(SwDoc.IsSketch)) wanted.Add(p.Name);
            }

            return all.Where(f => wanted.Contains(f.Name) && !SwDoc.IsSystemFeature(f) && !standard.Contains(f.Name)).ToList();
        }
    }
}
