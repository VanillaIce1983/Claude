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
    /// are the reference the rebuilt feature has to reproduce. The rollback bar is always put
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
                }
            }
            finally
            {
                fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }
    }
}
