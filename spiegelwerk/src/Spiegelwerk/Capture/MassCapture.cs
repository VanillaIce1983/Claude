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
        /// <param name="rebuilt">
        /// With selected bodies (or seed bodies pulled in): the collected features. A body with a
        /// solid feature outside them has no counterpart in the copy; if a feature changes such a
        /// body in its own rolled-back state – also one that is gone in the final state, which
        /// <see cref="Spiegelwerk.Core.Planning.BodyScope"/> cannot see –, it gets no reference.
        /// </param>
        /// <returns>False if the user stopped the run with Esc.</returns>
        public static bool Measure(ModelDoc2 doc, IEnumerable<FeatureRecord> records, RunProgress progress, ISet<string>? rebuilt = null)
        {
            FeatureManager fm = doc.FeatureManager;
            try
            {
                foreach (FeatureRecord r in records)
                {
                    progress.Show(RunSteps.Measure, r.Name);
                    MeasureOne(doc, fm, r, rebuilt);
                    if (progress.CancelAfterStep()) return false;
                }
                return true;
            }
            finally
            {
                fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }

        private static void MeasureOne(ModelDoc2 doc, FeatureManager fm, FeatureRecord r, ISet<string>? rebuilt)
        {
            // A feature that also changed bodies the copy does not get has no usable reference.
            if (!r.ChangesSolid || r is UnsupportedRecord || r.OtherBodies.Count > 0) return;
            if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, r.Name)) return;
            var before = SwDoc.MassOf(doc);
            List<string> foreignNames = new List<string>();
            MassState foreignBefore = rebuilt == null ? MassState.Empty : SwDoc.MassOf(ForeignBodies(doc, rebuilt, foreignNames));
            if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, r.Name)) return;
            MassState after = SwDoc.MassOf(doc);
            if (rebuilt != null)
            {
                MassState foreignChange = SwDoc.MassOf(ForeignBodies(doc, rebuilt, foreignNames)) - foreignBefore;
                if (!MassComparison.Compare(MassState.Empty, foreignChange, after).Passes)
                {
                    NoteOtherBodies(r, foreignNames);
                    return;
                }
            }
            r.ExpectedChange = after - before;
            if (SwDoc.FeatureByName(doc, r.Name) is Feature f) SampleFaces(f, r);
        }

        /// <summary>Solid bodies of the current (rolled-back) state that a feature outside <paramref name="rebuilt"/> made or changed.</summary>
        private static List<Body2> ForeignBodies(ModelDoc2 doc, ISet<string> rebuilt, List<string> names)
        {
            var result = new List<Body2>();
            foreach (Body2 b in SwDoc.SolidBodies(doc))
            {
                IEnumerable<Feature> made = b.GetFeatures() is object[] fs ? fs.OfType<Feature>() : Enumerable.Empty<Feature>();
                if (!made.Any(f => !rebuilt.Contains(f.Name) && !SwDoc.IsSketch(f) && !FeatureCollector.IsDatum(f) && !SwDoc.IsSystemFeature(f))) continue;
                result.Add(b);
                if (!names.Contains(b.Name)) names.Add(b.Name);
            }
            return result;
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
