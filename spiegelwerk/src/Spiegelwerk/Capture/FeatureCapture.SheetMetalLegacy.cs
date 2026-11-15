using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Normal Cut and legacy Insert Bends (blech-plan S6, A1 §6): the readers. Both read under AccessSelections –
    /// the state before the feature, where the faces of a normal cut are still slanted and the fixed face of
    /// Insert Bends lies on the solid it turns into sheet metal.
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>
        /// A normal cut: its values, its face groups (every face of every group, or not at all), the offset plane
        /// (a plane or the top or bottom face; required in that mode) and the direction (an edge stays an edge;
        /// optional – a direction that does not read is a note, SOLIDWORKS' default stands in).
        /// </summary>
        private NormalCutRecord CaptureNormalCut(Feature f, string type, SMNormalCutFeatureData2 d)
        {
            var r = new NormalCutRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                SwSheetMetal.ReadNormalCutScalars(d, r);
                if (r.Parameters == NormalCutParameterTypes.OffsetPlane)
                {
                    object? plane = (object)d.OffsetPlaneReference;
                    r.OffsetPlane = plane == null ? null : ToGeometryRef(plane);
                    if (r.OffsetPlane == null || (r.OffsetPlane.Kind != GeometryRefKind.Plane && r.OffsetPlane.Kind != GeometryRefKind.Face))
                        throw new NotSupportedException(NormalCutRules.OffsetPlaneUnreadable);
                }
                object? direction = (object)d.CutDirection;
                if (direction != null)
                {
                    r.CutDirection = direction is Edge edge ? GeometryRef.ToEdge(TopologyCapture.Edge(edge)) : ToGeometryRef(direction);
                    if (r.CutDirection == null || r.CutDirection.Kind == GeometryRefKind.Unknown)
                    {
                        r.CutDirection = null;
                        r.Notes.Add(NormalCutRules.DirectionUnreadable);
                    }
                }
                List<List<Face2>> groups = SwSheetMetal.NormalCutGroups(d)
                                           ?? throw new NotSupportedException("Flächengruppen des normalen Schnitts nicht lesbar.");
                foreach (List<Face2> group in groups) r.Groups.Add(group.Select(TopologyCapture.Face).ToList());
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Groups.Count == 0) throw new NotSupportedException(NormalCutRules.NoGroups);
            return r;
        }

        /// <summary>
        /// Legacy Insert Bends, anchored on its SheetMetal (<see cref="SheetMetalTree.IsLegacyAnchor"/>): the
        /// SheetMetal's parameters with its fixed reference, the part's defaults, FlattenBends and ProcessBends with
        /// what they have of their own, their dimensions, and whether the part was flattened (ProcessBends
        /// suppressed) before the run – without them if they are switched off with the FlattenBends
        /// (<see cref="SheetMetalRules.LegacyPartsInRun"/>). The fixed face: the SheetMetal's fixed reference, else the
        /// one of FlattenBends, else – one sheet-metal body only – the one of the part's defaults. Not rebuilt: features
        /// the original got between the bends features while flattened.
        /// </summary>
        private LegacyBendsRecord CaptureLegacyBends(Feature f, string type)
        {
            IReadOnlyList<string> between = _sheetMetal.BetweenLegacyBends(f.Name);
            if (between.Count > 0) throw new NotSupportedException(SheetMetalRules.LegacyBetween(between));
            // FlattenBends suppressed (ProcessBends with it): Insert Bends switched off past the SheetMetal – the copy gets the SheetMetal alone.
            var (flattenName, processName) = SheetMetalRules.LegacyPartsInRun(_sheetMetal.LegacyParts(f.Name),
                name => SwDoc.FeatureByName(_doc, name) is Feature part && SwSheetMetal.IsSuppressed(part));
            var r = new LegacyBendsRecord(f.Name, type) { FlattenBendsName = flattenName, ProcessBendsName = processName };
            r.Body = SwSheetMetal.ReadParameters(_doc, f, true, FaceOrEdgeRef);
            r.FixedFace = r.Body.FixedReference;
            if (SwSheetMetal.Template(_doc) is Feature template)
            {
                try
                {
                    r.Defaults = SwSheetMetal.ReadParameters(_doc, template, false, FaceOrEdgeRef);
                }
                catch (Exception)
                {
                    // Then the body's values are all there is (as before 2013).
                    r.Defaults = null;
                }
            }

            if (flattenName != null && SwDoc.FeatureByName(_doc, flattenName) is Feature flatten)
            {
                r.FlattenBendsValues = SwSheetMetal.ReadLegacyBends(_doc, flatten, FaceOrEdgeRef, out GeometryRef? fixedFace);
                r.FixedFace ??= fixedFace;
                CompanionDimensionsOf(flatten, r.FlattenBendsDimensions);
            }
            if (processName != null && SwDoc.FeatureByName(_doc, processName) is Feature process)
            {
                r.ProcessBendsValues = SwSheetMetal.ReadLegacyBends(_doc, process, null, out _);
                CompanionDimensionsOf(process, r.ProcessBendsDimensions);
                r.Flattened = _wasFlattened?.Invoke(processName) ?? false;
            }
            // From 2013 the fixed reference may live on the part's defaults (help, FixedReference) – only telling with one sheet-metal body.
            if (r.FixedFace == null && r.Defaults?.FixedReference != null && SwSheetMetal.OfType(_doc, "SheetMetal").Count == 1) r.FixedFace = r.Defaults.FixedReference;
            if (r.FixedFace == null) throw new NotSupportedException(SheetMetalRules.LegacyFixedFaceUnreadable);
            return r;
        }

        /// <summary>The own dimensions of a feature that comes with another; unreadable: none (equations on them stay with the report).</summary>
        private static void CompanionDimensionsOf(Feature f, List<Core.Matching.NamedDimension> into)
        {
            try
            {
                into.AddRange(SwDoc.OwnDimensions(f).Select(SwDoc.Named));
            }
            catch (Exception)
            {
                into.Clear();
            }
        }
    }
}
