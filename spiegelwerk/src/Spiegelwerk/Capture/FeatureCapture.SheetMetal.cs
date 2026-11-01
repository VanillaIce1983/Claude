using System;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Sheet metal (blech-plan §1.6): every sheet-metal type is captured through one dispatch.
    /// Readers are added slice by slice; a type without one becomes an unsupported record. Each
    /// reader keeps ReleaseSelectionAccess and EditRollback(End) in its own finally.
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>
        /// Captures a sheet-metal feature. Many fragile calls (late-bound reads, rollbacks, bend
        /// sub-features) must not end the run: any exception other than NotSupportedException
        /// becomes one, and with it an unsupported record of this feature alone.
        /// </summary>
        private FeatureRecord CaptureSheetMetal(Feature f, string type)
        {
            try
            {
                // By type name first: some have no definition (a form tool before 2025).
                FeatureRecord? record = SheetMetalByType(f, type) ?? SheetMetalByDefinition(f, type, (object?)f.GetDefinition());
                return record ?? new UnsupportedRecord(f.Name, type, UnsupportedFeatures.Reason(type), ChangesSolid(type));
            }
            catch (Exception ex) when (!(ex is NotSupportedException))
            {
                throw new NotSupportedException($"Blech-Feature nicht lesbar ({ex.Message}).");
            }
        }

        /// <summary>Readers by type name, for types whose definition may be null. None yet.</summary>
        private FeatureRecord? SheetMetalByType(Feature f, string type) => null;

        /// <summary>Readers by definition (switch on the data interface). None yet.</summary>
        private FeatureRecord? SheetMetalByDefinition(Feature f, string type, object? def) => null;
    }
}
