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
        /// sub-features) must not end the run: any exception becomes an unsupported record of
        /// this feature alone – a NotSupportedException with its own text, any other as „Blech-
        /// Feature nicht lesbar (…)“. Every unsupported record of a sheet-metal type takes the
        /// type's fallback flag (<see cref="SheetMetalTypes.TriggersFallback"/>).
        /// </summary>
        private FeatureRecord CaptureSheetMetal(Feature f, string type)
        {
            string reason;
            try
            {
                // By type name first: some have no definition (a form tool before 2025).
                FeatureRecord? record = SheetMetalByType(f, type) ?? SheetMetalByDefinition(f, type, (object?)f.GetDefinition());
                if (record == null) reason = UnsupportedFeatures.Reason(type);
                else if (record is UnsupportedRecord own) return WithFallbackFlag(own, type);
                else return record;
            }
            catch (NotSupportedException ex)
            {
                reason = ex.Message;
            }
            catch (Exception ex)
            {
                reason = $"Blech-Feature nicht lesbar ({ex.Message}).";
            }
            return WithFallbackFlag(new UnsupportedRecord(f.Name, type, reason, ChangesSolid(type)), type);
        }

        private static UnsupportedRecord WithFallbackFlag(UnsupportedRecord record, string type)
        {
            if (!SheetMetalTypes.TriggersFallback(type)) record.TriggersFallback = false;
            return record;
        }

        /// <summary>Readers by type name, for types whose definition may be null. None yet.</summary>
        private FeatureRecord? SheetMetalByType(Feature f, string type) => null;

        /// <summary>Readers by definition (switch on the data interface). None yet.</summary>
        private FeatureRecord? SheetMetalByDefinition(Feature f, string type, object? def) => null;
    }
}
