using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Measures what each original feature did to the solid bodies: volume and first moment
    /// with the rollback bar just before and just after the feature. Mirrored, these numbers
    /// are the reference the rebuilt feature has to reproduce. In the same state a few points
    /// on the feature's faces are taken for the shape check. The rollback bar is always put
    /// back to the end, also on errors.
    /// </summary>
    internal static class MassCapture
    {
        public static void Measure(ModelDoc2 doc, IEnumerable<FeatureRecord> records)
        {
            FeatureManager fm = doc.FeatureManager;
            try
            {
                foreach (FeatureRecord r in records)
                {
                    if (!r.ChangesSolid || r is UnsupportedRecord) continue;
                    if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, r.Name)) continue;
                    var before = SwDoc.MassOf(doc);
                    if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, r.Name)) continue;
                    r.ExpectedChange = SwDoc.MassOf(doc) - before;
                    if (SwDoc.FeatureByName(doc, r.Name) is Feature f) SampleFaces(f, r);
                }
            }
            finally
            {
                fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
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
