using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Sheet metal (blech-plan §1.6): every sheet-metal type is captured through one dispatch.
    /// Readers are added slice by slice; a type without one becomes an unsupported record. Each
    /// reader keeps ReleaseSelectionAccess and EditRollback(End) in its own finally.
    /// S1a: base flange (and tab) with its SheetMetal and the part's defaults, flat pattern.
    /// S2: hem and miter flange, S3: edge flange (<c>FeatureCapture.SheetMetalFlanges.cs</c>).
    /// S4: sketched bend (<c>FeatureCapture.SheetMetalBends.cs</c>) and jog.
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>Who comes into being with whom in a sheet-metal part.</summary>
        private readonly SheetMetalTree _sheetMetal;

        /// <summary>Whether a flat pattern was flattened before the run folded it.</summary>
        private readonly Func<string, bool>? _wasFlattened;

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

        /// <summary>Readers by definition (switch on the data interface).</summary>
        private FeatureRecord? SheetMetalByDefinition(Feature f, string type, object? def) => def switch
        {
            BaseFlangeFeatureData bf => CaptureBaseFlange(f, type, bf),
            FlatPatternFeatureData fp => CaptureFlatPattern(f, type, fp),
            HemFeatureData hm => CaptureHem(f, type, hm),
            MiterFlangeFeatureData mf => CaptureMiterFlange(f, type, mf),
            EdgeFlangeFeatureData ef => CaptureEdgeFlange(f, type, ef),
            SketchedBendFeatureData sb => CaptureSketchedBend(f, type, sb),
            JogFeatureData jg => CaptureJog(f, type, jg),
            _ => null,
        };

        /// <summary>
        /// A sheet-metal fixed reference (fixed face of a flat pattern, fixed reference of a
        /// SheetMetal): a face stays a face, an edge an edge – a straight one too, unlike the
        /// general reading, which makes an axis of it (A1 P11). Anything else is no reference.
        /// </summary>
        private static GeometryRef? FaceOrEdgeRef(object? o) => o switch
        {
            Face2 face => GeometryRef.ToFace(TopologyCapture.Face(face)),
            Edge edge => GeometryRef.ToEdge(TopologyCapture.Edge(edge)),
            _ => null,
        };

        private BaseFlangeRecord CaptureBaseFlange(Feature f, string type, BaseFlangeFeatureData d)
        {
            Feature sketch = SketchOf(f);
            var r = new BaseFlangeRecord(f.Name, type, sketch.Name) { ClosedProfile = AllContoursClosed(sketch) };
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Thickness = d.Thickness;
                r.ReverseThickness = d.ReverseThickness;
                r.ReverseDirection = d.ReverseDirection;
                r.BendRadius = d.BendRadius;
                r.OffsetDirections = d.OffsetDirections == 2 ? 2 : 1;
                if (!r.ClosedProfile)
                {
                    ReadFlangeSide(d, 1, r.Side1, r.Notes);
                    if (r.OffsetDirections == 2) ReadFlangeSide(d, 2, r.Side2, r.Notes);
                }
                if (d.UseGaugeTable)
                    r.Gauge = new GaugeData
                    {
                        TablePath = d.GaugeTablePath ?? "",
                        ThicknessName = d.ThicknessTableName ?? "",
                        TableRadius = d.TableRadius,
                        OverrideThickness = d.OverrideThickness,
                        OverrideRadius = d.OverrideRadius,
                        OverrideKFactor = d.OverrideKFactor,
                        KFactor = d.KFactor,
                    };
                // Newer versions only (late-bound): symmetric thickness, override flag, material, own allowance and relief.
                r.SymmetricThickness = SwLate.Get(d, "SymmetricThickness") is bool symmetric && symmetric;
                if (SwLate.Get(d, "OverrideDefaultSheetMetalParameters") is bool overrides) r.OverrideDefaults = overrides;
                r.UsesMaterialParameters = SwLate.Get(d, "UseMaterialSheetMetalParameters") is bool material && material;
                if (SwLate.Get(d, "UseDefaultBendAllowance") is bool defaultAllowance && !defaultAllowance &&
                    SwLate.Invoke(d, "GetCustomBendAllowance") is CustomBendAllowance cba)
                    r.OwnBendAllowance = SwSheetMetal.ReadAllowance(cba);
                if (SwLate.Get(d, "UseDefaultBendRelief") is bool defaultRelief && !defaultRelief) r.OwnRelief = ReliefOf(d);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }

            // The body's SheetMetal: a parent (help example GetParents()[1]); its own only if this flange is its owner – a tab shares the body's.
            Feature? sm = f.GetParents() is object[] parents ? parents.OfType<Feature>().FirstOrDefault(p => p.GetTypeName2() == "SheetMetal") : null;
            sm ??= _sheetMetal.Owners.Where(kv => kv.Value.owner == f.Name).Select(kv => SwDoc.FeatureByName(_doc, kv.Key)).FirstOrDefault(x => x != null);
            if (sm != null)
            {
                bool own = !_sheetMetal.Owners.TryGetValue(sm.Name, out var owner) || owner.owner == f.Name;
                if (own)
                {
                    r.SheetMetalName = sm.Name;
                    try
                    {
                        r.CompanionDimensions.AddRange(SwDoc.OwnDimensions(sm).Select(SwDoc.Named));
                    }
                    catch (Exception)
                    {
                        // Unreadable: equations on the sheet thickness then stay with the report.
                        r.CompanionDimensions.Clear();
                    }
                }
                else
                {
                    r.TabOfSheetMetal = sm.Name;
                }
                r.Body = SwSheetMetal.ReadParameters(_doc, sm, true, FaceOrEdgeRef);
            }
            if (SwSheetMetal.Template(_doc) is Feature template) r.Defaults = SwSheetMetal.ReadParameters(_doc, template, false, FaceOrEdgeRef);

            r.MergesIntoExisting = JoinsAnEarlierBody(f, r.TabOfSheetMetal != null, r.Notes);
            return r;
        }

        /// <summary>Every contour of the profile sketch is closed (a plate or tab); otherwise an open profile.</summary>
        private static bool AllContoursClosed(Feature sketch)
        {
            var sk = (Sketch)(object)sketch.GetSpecificFeature2();
            object[] contours = (object)sk.GetSketchContours() as object[] ?? Array.Empty<object>();
            List<SketchContour> list = contours.OfType<SketchContour>().ToList();
            return list.Count > 0 && list.All(c => c.IsClosed());
        }

        /// <summary>
        /// One direction of an open profile. The newer members (D1EndCondition…, late-bound) win
        /// over the obsolete D1Offset… ones; where both answer and differ, a note says so (B5).
        /// An end condition a base flange does not have („bis Kante und zusammenführen“) is not rebuilt.
        /// </summary>
        private void ReadFlangeSide(BaseFlangeFeatureData d, int which, FlangeSide side, List<string> notes)
        {
            int oldType = which == 1 ? d.D1OffsetType : d.D2OffsetType;
            double oldDistance = which == 1 ? d.D1OffsetDistance : d.D2OffsetDistance;
            int type = SwLate.Get(d, $"D{which}EndConditionType") is int newType ? newType : oldType;
            double distance = SwLate.Get(d, $"D{which}EndConditionDistance") is double newDistance ? newDistance : oldDistance;
            if (type != oldType || Math.Abs(distance - oldDistance) > SheetMetalCompare.LengthTolerance)
                notes.Add($"Richtung {which}: alte und neue Angabe der Endbedingung weichen ab ({oldType}/{type}); die neue gilt.");
            side.End = SheetMetalRules.FromFlangeOffset(type)
                       ?? throw new NotSupportedException($"Endbedingung {type} der Basislasche (Richtung {which}) wird nicht unterstützt.");
            side.Distance = distance;
            side.ReverseOffset = which == 1 ? d.D1ReverseOffset : d.D2ReverseOffset;
            if (!ExtrudeSide.NeedsReference(side.End)) return;
            object? reference = SwLate.Get(d, $"D{which}EndConditionReference") ?? (which == 1 ? (object)d.D1OffsetReference : (object)d.D2OffsetReference);
            side.Reference = ToGeometryRef(reference!);
            if (side.Reference == null || side.Reference.Kind == GeometryRefKind.Unknown)
                throw new NotSupportedException($"Bezug der Endbedingung {side.End} der Basislasche ist im Original nicht lesbar.");
        }

        /// <summary>The flange's own relief (newer versions, late-bound getters).</summary>
        private static ReliefData ReliefOf(object d)
        {
            var relief = new ReliefData();
            if (SwLate.Get(d, "ReliefType") is int kind) relief.Kind = (ReliefKind)kind;
            relief.UseAutoRelief = relief.Kind != ReliefKind.None;
            if (SwLate.Get(d, "UseReliefRatio") is bool useRatio) relief.UseRatio = useRatio;
            if (SwLate.Get(d, "ReliefRatio") is double ratio) relief.Ratio = ratio;
            if (SwLate.Get(d, "ReliefWidth") is double width) relief.Width = width;
            if (SwLate.Get(d, "ReliefDepth") is double depth) relief.Depth = depth;
            return relief;
        }

        /// <summary>
        /// A tab: the flange joined a body that was there before it – the solid body count does not
        /// grow with it (rolled back before and after the flange). The shared SheetMetal is the
        /// cross-check (B6); if they disagree, the count wins, with a note. Without a rollback only
        /// the shared SheetMetal is left to tell.
        /// </summary>
        private bool JoinsAnEarlierBody(Feature f, bool sharesSheetMetal, List<string> notes)
        {
            int? before = null, after = null;
            try
            {
                if (RollBack((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, f.Name)) before = SwDoc.SolidBodies(_doc).Count;
                if (RollBack((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, f.Name)) after = SwDoc.SolidBodies(_doc).Count;
            }
            finally
            {
                _doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
            if (!before.HasValue || !after.HasValue)
            {
                if (sharesSheetMetal) notes.Add("Lasche angenommen: Die Basislasche teilt das Blech-Feature eines früheren Körpers (Körperzahl nicht messbar).");
                return sharesSheetMetal;
            }
            bool tab = after.Value == before.Value;
            if (tab != sharesSheetMetal)
                notes.Add(tab
                    ? "Lasche erkannt (die Körperzahl wächst nicht), obwohl die Basislasche ein eigenes Blech-Feature hat."
                    : "Eigener Körper erkannt (die Körperzahl wächst), obwohl die Basislasche das Blech-Feature eines früheren Körpers teilt.");
            return tab;
        }

        /// <summary>
        /// Fixed face, options, own corner features and excluded faces of a flat pattern; its
        /// owner from the tree (the first base flange of its body). The run has folded it, so it is
        /// suppressed; if its data do not open then, it is read flattened for a moment.
        /// </summary>
        private FlatPatternRecord CaptureFlatPattern(Feature f, string type, FlatPatternFeatureData d)
        {
            string owner = _sheetMetal.FlatPatternOwners.TryGetValue(f.Name, out string known) ? known : "";
            var r = new FlatPatternRecord(f.Name, type, owner) { Flattened = _wasFlattened?.Invoke(f.Name) ?? false, OwnerFromTree = _sheetMetal.OwnerFromTree(f.Name) };
            if (d.AccessSelections(_doc, null))
            {
                ReadFlatPattern(d, r);
                return r;
            }
            if (!SwSheetMetal.SetSuppressed(_doc, f, false))
                throw new NotSupportedException("Feature-Definition der Abwicklung konnte nicht gelesen werden (AccessSelections).");
            try
            {
                var flat = (FlatPatternFeatureData)(object)f.GetDefinition();
                if (!flat.AccessSelections(_doc, null))
                    throw new NotSupportedException("Feature-Definition der Abwicklung konnte nicht gelesen werden (AccessSelections).");
                ReadFlatPattern(flat, r);
            }
            finally
            {
                SwSheetMetal.SetSuppressed(_doc, f, true);
            }
            return r;
        }

        /// <summary>Reads the options under the open selection access and releases it.</summary>
        private static void ReadFlatPattern(FlatPatternFeatureData d, FlatPatternRecord r)
        {
            try
            {
                object? fixedFace = (object)d.FixedFace2;
                r.FixedFace = FaceOrEdgeRef(fixedFace);
                if (fixedFace != null && r.FixedFace == null)
                    r.Notes.Add("Feste Fläche der Abwicklung ist weder Fläche noch Kante; im Ziel bleibt die Vorgabe von SOLIDWORKS.");
                r.MergeFaces = d.MergeFace;
                r.SimplifyBends = d.SimplifyBends;
                r.CornerTreatment = d.CornerTreatment;
                r.CornerTrimReliefType = d.CornerTrimReliefType;
                r.CornerTrimReliefDistance = d.CornerTrimReliefDistance;
                r.CornerTrimRatioToThickness = d.CornerTrimRatioToThickness;
                r.UseRatioToThickness = d.UseRatioToThickness;
                r.BreakCornerType = d.BreakCornerType;
                r.BreakCornerRadius = d.BreakCornerRadius;
                r.ShowSlitInCornerRelief = d.ShowSlitInCornerRelief;
                Feature? trim = null, breakCorner = null;
                r.AddCornerTrim = d.GetAddCornerTrim(out trim);
                r.BreakCorners = d.GetBreakCorners(out breakCorner);
                if (d.IGetExcludedFacesCount() > 0 && (object)d.ExcludedFaces is object[] excluded)
                    r.ExcludedFaces.AddRange(excluded.OfType<Face2>().Select(TopologyCapture.Face));
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }
    }
}
