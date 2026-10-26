using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
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
        /// <returns>False if the user stopped the run with Esc.</returns>
        public static bool Measure(ModelDoc2 doc, IEnumerable<FeatureRecord> records, RunProgress progress)
        {
            FeatureManager fm = doc.FeatureManager;
            try
            {
                foreach (FeatureRecord r in records)
                {
                    progress.Show(RunSteps.Measure, r.Name);
                    MeasureOne(doc, fm, r);
                    if (progress.CancelAfterStep()) return false;
                }
                return true;
            }
            finally
            {
                fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }

        private static void MeasureOne(ModelDoc2 doc, FeatureManager fm, FeatureRecord r)
        {
            // A feature that also changed bodies the copy does not get has no usable reference.
            if (!r.ChangesSolid || r is UnsupportedRecord || r.OtherBodies.Count > 0) return;
            if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, r.Name)) return;
            var before = SwDoc.MassOf(doc);
            if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, r.Name)) return;
            r.ExpectedChange = SwDoc.MassOf(doc) - before;
            if (SwDoc.FeatureByName(doc, r.Name) is Feature f) SampleFaces(f, r);
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
