using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Measures what each original feature did to the solid bodies: volume and first moment
    /// with the rollback bar just before and just after the feature. Mirrored, these numbers
    /// are the reference the rebuilt feature has to reproduce. In the same state a few points
    /// on the feature's faces are taken for the shape check. The rollback bar is always put
    /// back to the end, also on errors and when the user stops the run.
    /// </summary>
    internal static class MassCapture
    {
        /// <summary>
        /// With selected bodies (or seed bodies pulled in): a body with a solid feature that is not
        /// collected has no counterpart in the copy. If a feature changes such a body in its own
        /// rolled-back state – also one that is gone in the final state, which
        /// <see cref="Spiegelwerk.Core.Planning.BodyScope"/> cannot see –, it gets no reference.
        /// </summary>
        internal sealed class ForeignRule
        {
            /// <param name="treeWalk">Names of the tree walk (features in creation order).</param>
            /// <param name="collected">Collected features, also those skipped – as for the body scope.</param>
            public ForeignRule(IEnumerable<string> treeWalk, IEnumerable<string> collected)
            {
                TreeWalk = new HashSet<string>(treeWalk, StringComparer.Ordinal);
                Collected = new HashSet<string>(collected, StringComparer.Ordinal);
            }

            private HashSet<string> TreeWalk { get; }
            private HashSet<string> Collected { get; }

            public bool IsForeign(Body2 b) => FeatureCollector.SolidFeatures(b, TreeWalk).Any(f => !Collected.Contains(f.Name));
        }

        /// <returns>False if the user stopped the run with Esc.</returns>
        public static bool Measure(ModelDoc2 doc, IEnumerable<FeatureRecord> records, RunProgress progress, ForeignRule? foreign = null)
        {
            FeatureManager fm = doc.FeatureManager;
            try
            {
                foreach (FeatureRecord r in records)
                {
                    progress.Show(RunSteps.Measure, r.Name);
                    MeasureOne(doc, fm, r, foreign);
                    if (progress.CancelAfterStep()) return false;
                }
                return true;
            }
            finally
            {
                fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }

        private static void MeasureOne(ModelDoc2 doc, FeatureManager fm, FeatureRecord r, ForeignRule? foreign)
        {
            // A feature that also changed bodies the copy does not get has no usable reference.
            if (!r.ChangesSolid || r is UnsupportedRecord || r.OtherBodies.Count > 0) return;
            if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, r.Name)) return;
            var before = SwDoc.MassOf(doc);
            List<ForeignBody>? foreignBefore = foreign == null ? null : ForeignBodies(doc, foreign, r.Name);
            if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, r.Name)) return;
            MassState after = SwDoc.MassOf(doc);
            if (foreign != null && foreignBefore != null)
            {
                List<ForeignBody> foreignAfter = ForeignBodies(doc, foreign, r.Name);
                MassState change = Sum(foreignAfter) - Sum(foreignBefore);
                if (!MassComparison.Compare(MassState.Empty, change, after).Passes)
                {
                    NoteOtherBodies(r, ChangedBodies(foreignBefore, foreignAfter, after));
                    return;
                }
            }
            r.ExpectedChange = after - before;
            if (SwDoc.FeatureByName(doc, r.Name) is Feature f) SampleFaces(f, r);
        }

        private sealed class ForeignBody
        {
            public ForeignBody(string name, MassState mass, bool byFeature)
            {
                Name = name;
                Mass = mass;
                ByFeature = byFeature;
            }

            public string Name { get; }
            public MassState Mass { get; }

            /// <summary>The measured feature is among the body's features.</summary>
            public bool ByFeature { get; }
        }

        /// <summary>The foreign solid bodies of the current (rolled-back) state.</summary>
        private static List<ForeignBody> ForeignBodies(ModelDoc2 doc, ForeignRule rule, string feature)
        {
            var result = new List<ForeignBody>();
            foreach (Body2 b in SwDoc.SolidBodies(doc))
            {
                if (!rule.IsForeign(b)) continue;
                bool byFeature = b.GetFeatures() is object[] fs && fs.OfType<Feature>().Any(f => f.Name == feature);
                result.Add(new ForeignBody(b.Name, SwDoc.MassOf(new[] { b }), byFeature));
            }
            return result;
        }

        private static MassState Sum(IEnumerable<ForeignBody> bodies) => bodies.Aggregate(MassState.Empty, (s, b) => s + b.Mass);

        /// <summary>
        /// The foreign bodies the feature changed, by name: those it now belongs to, else those
        /// of the same name whose mass changed, else those that are gone. A body takes the name
        /// of its last feature, so the one the feature changed is named after it, not twice.
        /// </summary>
        private static List<string> ChangedBodies(List<ForeignBody> before, List<ForeignBody> after, MassState total)
        {
            var names = after.Where(b => b.ByFeature).Select(b => b.Name).ToList();
            foreach (ForeignBody a in after)
                if (!names.Contains(a.Name) && before.FirstOrDefault(b => b.Name == a.Name) is ForeignBody old &&
                    !MassComparison.Compare(MassState.Empty, a.Mass - old.Mass, total).Passes)
                    names.Add(a.Name);
            if (names.Count == 0) names.AddRange(before.Select(b => b.Name).Where(n => after.All(a => a.Name != n)));
            if (names.Count == 0) names.AddRange(before.Concat(after).Select(b => b.Name).Distinct());
            return names;
        }

        /// <summary>The copy acts on the mirrored bodies only: what the original did to other bodies is no reference for it.</summary>
        internal static void NoteOtherBodies(FeatureRecord r, IEnumerable<string> bodies)
        {
            foreach (string b in bodies)
                if (!r.OtherBodies.Contains(b)) r.OtherBodies.Add(b);
            r.Notes.Add($"Das Original verändert auch Körper, die nicht mitgespiegelt werden ({string.Join(", ", r.OtherBodies.Select(n => $"\"{n}\""))}). " +
                        "Die Kopie wirkt nur auf die gespiegelten Körper und wurde deshalb nicht mit dem Original verglichen.");
        }

        /// <summary>Points on the feature's faces for the shape check, spread over at most <see cref="MaxSamples"/> faces.</summary>
        private static void SampleFaces(Feature f, FeatureRecord r)
        {
            if (!(f.GetFaces() is object[] faces) || faces.Length == 0) return;
            int n = Math.Min(MaxSamples, faces.Length);
            for (int i = 0; i < n; i++)
            {
                if (!(faces[i * faces.Length / n] is Face2 face)) continue;
                try
                {
                    r.FaceSamples.Add(TopologyCapture.Face(face).Sample);
                }
                catch (Exception)
                {
                    // A face the kernel cannot evaluate simply gives no sample.
                }
            }
        }

        private const int MaxSamples = 8;
    }
}
