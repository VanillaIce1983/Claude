using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Turns features of the original into <see cref="FeatureRecord"/>s. References to
    /// geometry are read while IFeatureData.AccessSelections is active: during that time the
    /// model is rolled back to the feature, so faces and edges are read in the state the
    /// feature saw them, not in the final state (later fillets have not split them yet).
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        private readonly ModelDoc2 _doc;
        private readonly SwMath _math;
        private readonly SketchCapture _sketches;
        private readonly RegionSpaceMemory _regionSpaces;

        /// <summary>
        /// Reading back the job's own copies in the target (the first half of a mirror
        /// feature): the feature scope the job itself set in the same part is no loss to report.
        /// </summary>
        private readonly bool _ofOwnCopy;

        /// <param name="regionSpaces">What the run learns about the coordinate space of sketch regions; shared with the build.</param>
        /// <param name="ofOwnCopy">The features are the job's own copies in the target, not the original.</param>
        /// <param name="sheetMetal">Who comes into being with whom in a sheet-metal part; none for other parts.</param>
        /// <param name="wasFlattened">Whether a flat pattern was flattened before the run folded it; none: folded.</param>
        public FeatureCapture(ModelDoc2 doc, SwMath math, RegionSpaceMemory regionSpaces, bool ofOwnCopy = false, SheetMetalTree? sheetMetal = null,
            Func<string, bool>? wasFlattened = null)
        {
            _doc = doc;
            _math = math;
            _ofOwnCopy = ofOwnCopy;
            _sketches = new SketchCapture(math);
            _regionSpaces = regionSpaces;
            _sheetMetal = sheetMetal ?? SheetMetalTree.Empty;
            _wasFlattened = wasFlattened;
        }

        /// <summary>
        /// How often moving the rollback bar for a capture failed (sketch with model references,
        /// closed shell, advanced hole). Such a feature is read in the final state or not at all.
        /// </summary>
        public int RollbackFailures { get; private set; }

        private bool RollBack(int to, string feature)
        {
            bool ok = _doc.FeatureManager.EditRollback(to, feature);
            if (!ok) RollbackFailures++;
            return ok;
        }

        private const string ScopeNote = "Der Feature-Bereich (ausgewählte Körper) wird nicht übernommen.";

        /// <summary>A feature limited to chosen bodies is rebuilt without that limit – unless the job set it on its own copy.</summary>
        private void NoteScope(List<string> notes, bool scope, bool autoSelect)
        {
            if (scope && !autoSelect && !_ofOwnCopy) notes.Add(ScopeNote);
        }

        /// <summary>
        /// Captures the features in order. Parts of a feature (the sketches of a hole) are rebuilt
        /// with it – only if it is rebuilt itself (<see cref="FeatureRecord.BringsParts"/>);
        /// otherwise a part is captured on its own as its owner kind says: a position sketch as an
        /// ordinary sketch, everything else is left out. <paramref name="starting"/> runs before
        /// each feature, <paramref name="cancelled"/> after it: true stops, the result is null.
        /// </summary>
        public List<FeatureRecord>? CaptureAll(IReadOnlyList<Feature> features, Action<Feature>? starting = null, Func<bool>? cancelled = null)
        {
            var records = new List<FeatureRecord>();
            foreach (Feature f in features)
            {
                starting?.Invoke(f);
                records.Add(Capture(f));
                if (cancelled != null && cancelled()) return null;
            }
            // A flat pattern comes into being only with its base flange; without it, it and its parts are left out.
            var baseFlanges = new HashSet<string>(records.OfType<BaseFlangeRecord>().Select(r => r.Name), StringComparer.Ordinal);
            for (int i = 0; i < records.Count; i++)
                if (records[i] is FlatPatternRecord fp && !baseFlanges.Contains(fp.Owner))
                    records[i] = new UnsupportedRecord(fp.Name, fp.TypeName, SheetMetalTree.FlatPatternWithoutOwner(fp.Owner), false) { TriggersFallback = false };
            var owners = new HashSet<string>(records.Where(r => r.BringsParts).Select(r => r.Name), StringComparer.Ordinal);
            for (int i = 0; i < records.Count; i++)
                if (records[i] is PartOfFeatureRecord part && !owners.Contains(part.Owner))
                    records[i] = CaptureOnItsOwn(features[i], part);
            return records;
        }

        public FeatureRecord Capture(Feature f)
        {
            string type = f.GetTypeName2();
            try
            {
                if (OwnedPart(f, type) is PartOfFeatureRecord part)
                    return part;
                if (type == "ProfileFeature") return new SketchRecord(f.Name, ReadSketch(f));
                if (type == "3DProfileFeature") return new UnsupportedRecord(f.Name, type, "3D-Skizzen werden noch nicht gespiegelt.", false);
                if (type == "RefPlane") return new RefPlaneRecord(f.Name, _math.PlaneOf((RefPlane)(object)f.GetSpecificFeature2()));
                if (type == "RefAxis") return new RefAxisRecord(f.Name, SwDoc.AxisLine((RefAxis)(object)f.GetSpecificFeature2()));
                if (type == "CoordSys")
                    return new CoordSysRecord(f.Name, SwDoc.CoordinateSystemFrame(_doc, _math, f.Name)
                                                      ?? throw new NotSupportedException("Lage des Koordinatensystems nicht lesbar."));

                // Every sheet-metal type goes through one exception-safe dispatch, also those without a definition.
                if (UnsupportedFeatures.IsSheetMetal(type)) return CaptureSheetMetal(f, type);

                object def = f.GetDefinition();
                switch (def)
                {
                    case ExtrudeFeatureData2 ex:
                        return CaptureExtrude(f, type, ex);
                    case RevolveFeatureData2 rev:
                        return CaptureRevolve(f, type, rev);
                    // Before the simple fillet: a variable fillet's data might answer to both.
                    case VariableFilletFeatureData2 vf:
                        return CaptureVariableFillet(f, type, vf);
                    case SimpleFilletFeatureData2 fil when IsFaceFillet(fil):
                        return CaptureFaceFillet(f, type, fil);
                    case SimpleFilletFeatureData2 fil:
                        return CaptureFillet(f, type, fil);
                    case ChamferFeatureData2 ch:
                        return CaptureChamfer(f, type, ch);
                    case SweepFeatureData sw:
                        return CaptureSweep(f, type, sw);
                    case LoftFeatureData lo:
                        return CaptureLoft(f, type, lo);
                    case LinearPatternFeatureData lp:
                        return CaptureLinearPattern(f, type, lp);
                    case CircularPatternFeatureData cp:
                        return CaptureCircularPattern(f, type, cp);
                    case CurveDrivenPatternFeatureData cdp:
                        return CaptureCurvePattern(f, type, cdp);
                    case SketchPatternFeatureData sp:
                        return CaptureSketchPattern(f, type, sp);
                    case TablePatternFeatureData tp:
                        return CaptureTablePattern(f, type, tp);
                    case FillPatternFeatureData fp:
                        return CaptureFillPattern(f, type, fp);
                    case DimPatternFeatureData vp:
                        return CaptureVariablePattern(f, type, vp);
                    case WizardHoleFeatureData2 wh:
                        return CaptureHole(f, type, wh);
                    case AdvancedHoleFeatureData ah:
                        return CaptureAdvancedHole(f, type, ah);
                    case ShellFeatureData sh:
                        return CaptureShell(f, type, sh);
                    case DraftFeatureData2 dr:
                        return CaptureDraft(f, type, dr);
                    case MirrorPatternFeatureData mp:
                        return CaptureMirrorPattern(f, type, mp);
                    case MirrorSolidFeatureData ms:
                        return CaptureMirrorSolid(f, type, ms);
                }
                return new UnsupportedRecord(f.Name, type, UnsupportedFeatures.Reason(type), ChangesSolid(type));
            }
            catch (NotSupportedException ex)
            {
                return new UnsupportedRecord(f.Name, type, ex.Message, ChangesSolid(type));
            }
        }

        /// <summary>
        /// A sketch that refers to model edges, vertices or faces is read again with the
        /// rollback bar right after it: later features (fillets, cuts) may have split or
        /// shortened those edges, and their fingerprints must describe the state the sketch saw.
        /// </summary>
        private Core.Sketch.SketchModel ReadSketch(Feature f)
        {
            Core.Sketch.SketchModel model = _sketches.Read(f);
            bool refersToModel = model.Relations.SelectMany(x => x.Entities).Concat(model.Dimensions.SelectMany(x => x.Refs))
                .Any(x => x.Kind == Core.Sketch.SkRefKind.ModelEdge || x.Kind == Core.Sketch.SkRefKind.ModelVertex || x.Kind == Core.Sketch.SkRefKind.Plane);
            if (!refersToModel) return model;

            if (!RollBack((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, f.Name)) return model;
            try
            {
                return _sketches.Read(f);
            }
            finally
            {
                _doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }

        /// <summary>
        /// The face a sketch was drawn on (blech-plan §1.6, K7), read with the bar right after the sketch –
        /// for a sketch a feature absorbs that is the state just before the feature, before a bend split
        /// the face or moved part of it. Null for a sketch on a plane (no rollback then), if the bar does
        /// not move, or if the reference is no face.
        /// </summary>
        private FaceSignature? CaptureSupportFace(Feature sketchFeature)
        {
            bool onPlane = false;
            try
            {
                if (!((object)sketchFeature.GetSpecificFeature2() is Sketch sketch)) return null;
                int kind = 0;
                object? reference = (object)sketch.GetReferenceEntity(ref kind);
                onPlane = reference is RefPlane || (reference is Feature plane && (object)plane.GetSpecificFeature2() is RefPlane);
            }
            catch (Exception)
            {
                // Not readable in the finished part (the face may be split): asked again before the feature.
            }
            if (onPlane) return null;
            if (!RollBack((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, sketchFeature.Name)) return null;
            try
            {
                if (!((object)sketchFeature.GetSpecificFeature2() is Sketch sketch)) return null;
                int kind = 0;
                return (object)sketch.GetReferenceEntity(ref kind) is Face2 face ? TopologyCapture.Face(face) : null;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                _doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }

        /// <summary>A face turned against the fixed face by more than this counts as moved by a bend (A3 P2).</summary>
        private static readonly double TurnedAngle = Math.PI / 180;

        /// <summary>What <see cref="CaptureWitnesses"/> took, unmirrored, in the state right after the feature.</summary>
        private sealed class WitnessCapture
        {
            /// <summary>Points on the feature's own faces and those of its bend sub-features.</summary>
            public List<Vec3> Own { get; } = new List<Vec3>();

            /// <summary>Points on the other faces of its body it made or moved.</summary>
            public List<Vec3> Moved { get; } = new List<Vec3>();

            /// <summary>The features that made its body before it: the moved faces lie on the copy only if it has them all.</summary>
            public List<string> BodyFeatures { get; } = new List<string>();

            /// <summary>The fixed point still lies on a face of the body.</summary>
            public bool FixedPointStays { get; set; }
        }

        /// <summary>
        /// Witnesses of a sketched bend or jog (A3 P2, blech-plan K10): with the bar right after the
        /// feature, at most <see cref="SheetMetalBendRules.MaxWitnesses"/> points on its own faces and
        /// those of its bend sub-features (made by them, not shared with an older feature), as many on the
        /// other faces of its body it made or moved –
        /// those whose fingerprint none of <paramref name="before"/> (the body's faces before it) has;
        /// without those, the planar faces turned more than 1° against the fixed face –, the features
        /// that made the body before it (a moved face of a hole the copy lacks lies nowhere there: the
        /// build counts the moved ones only if the copy has them all, <see cref="SheetMetalBendRules.WitnessesFor"/>),
        /// and whether <paramref name="fixedPoint"/> still lies on a face of the body (10 µm): in the bend
        /// zone it lies on none afterwards, and then it is no witness. Null if the bar does not move or the
        /// faces do not read.
        /// </summary>
        private WitnessCapture? CaptureWitnesses(Feature f, FaceSignature fixedFace, IReadOnlyList<FaceSignature> before, Vec3? fixedPoint)
        {
            // Read with the bar at the end: rolled back, the tree walk would miss what lies after the bar.
            Dictionary<string, int> order = CreationOrderIndex();
            if (!RollBack((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, f.Name)) return null;
            try
            {
                List<Feature> ownFeatures = new[] { f }.Concat(SwDoc.SubFeatures(f).Where(s => !SwDoc.IsSketch(s))).ToList();
                var ownNames = new HashSet<string>(ownFeatures.Select(x => x.Name), StringComparer.Ordinal);
                List<Face2> ownFaces = ownFeatures.SelectMany(FacesOf).ToList();
                var own = new List<Vec3>();
                // GetFaces may list faces it shares with older features (a moved hole wall): by the feature that made
                // them (Face2.GetFeature, the oldest) they go with the moved ones; unreadable, they stay its own.
                foreach (Face2 face in ownFaces)
                    if (!(MadeBy(face) is string maker) || ownNames.Contains(maker))
                        own.Add(TopologyCapture.Face(face).Sample);
                List<Body2> bodies = SwDoc.SolidBodies(_doc).Where(b => b.GetFeatures() is object[] fs && fs.OfType<Feature>().Any(x => x.Name == f.Name)).ToList();
                if (bodies.Count == 0)
                    bodies = ownFaces.Select(x => (object)x.GetBody() as Body2).Where(b => b != null).Select(b => b!).GroupBy(b => b.Name).Select(g => g.First()).ToList();
                List<Face2> bodyFaces = bodies.SelectMany(b => ((object)b.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>()).ToList();
                var moved = new List<Vec3>();
                foreach (Face2 face in bodyFaces)
                {
                    FaceSignature sig = TopologyCapture.Face(face);
                    // Its own faces are in the other group (they need no earlier feature in the copy).
                    if (own.Any(o => o.AlmostEquals(sig.Sample, Tol.Length))) continue;
                    if (before.Count > 0)
                    {
                        if (!before.Any(b => b.CouldBe(sig) && b.Sample.AlmostEquals(sig.Sample, Tol.Length))) moved.Add(sig.Sample);
                    }
                    else if (sig.Kind == SurfaceKind.Plane && Math.Acos(Math.Max(-1, Math.Min(1, sig.Normal.Dot(fixedFace.Normal)))) > TurnedAngle)
                        moved.Add(sig.Sample);
                }
                var result = new WitnessCapture { FixedPointStays = fixedPoint is Vec3 p && bodyFaces.Any(x => OnFace(x, p)) };
                result.Own.AddRange(SheetMetalBendRules.PickWitnesses(new IReadOnlyList<Vec3>[] { own }));
                result.Moved.AddRange(SheetMetalBendRules.PickWitnesses(new IReadOnlyList<Vec3>[] { moved }));
                // The features before it in the tree walk (no sub-features of others, not the flat pattern at the
                // end); if its place is unknown, every other one – the cautious side: the moved ones then rarely count.
                bool placed = order.TryGetValue(f.Name, out int at);
                foreach (Feature x in bodies.SelectMany(b => b.GetFeatures() is object[] fs ? fs.OfType<Feature>() : Enumerable.Empty<Feature>()))
                {
                    if (ownNames.Contains(x.Name) || SwDoc.IsSketch(x) || FeatureCollector.IsDatum(x) || SwDoc.IsSystemFeature(x)) continue;
                    // No faces of their own in the folded body.
                    if (x.GetTypeName2() is "SheetMetal" or "FlatPattern") continue;
                    if (placed && !(order.TryGetValue(x.Name, out int i) && i < at)) continue;
                    if (!result.BodyFeatures.Contains(x.Name)) result.BodyFeatures.Add(x.Name);
                }
                return result;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                _doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }

        /// <summary>Name of the feature that made a face (Face2.GetFeature); null if it does not read.</summary>
        private static string? MadeBy(Face2 face)
        {
            try
            {
                return ((object)face.GetFeature() as Feature)?.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Position of every feature in creation order, read once (the capture changes no tree); empty if it does not read.</summary>
        private Dictionary<string, int> CreationOrderIndex()
        {
            if (_creationOrder != null) return _creationOrder;
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                List<Feature> all = SwDoc.FeaturesInCreationOrder(_doc);
                for (int i = 0; i < all.Count; i++) order[all[i].Name] = i;
            }
            catch (Exception)
            {
                order.Clear();
            }
            return _creationOrder = order;
        }

        private Dictionary<string, int>? _creationOrder;

        /// <summary>The point lies on the face by the kernel's distance (as the witness check measures, <see cref="SwShape.Tolerance"/>).</summary>
        private static bool OnFace(Face2 face, Vec3 p)
        {
            try
            {
                return TopologyCapture.DistanceToFace(face, p) <= SwShape.Tolerance;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The faces of a feature; none for one the kernel does not list.</summary>
        private static List<Face2> FacesOf(Feature f)
        {
            try
            {
                return ((object)f.GetFaces() as object[])?.OfType<Face2>().ToList() ?? new List<Face2>();
            }
            catch (Exception)
            {
                return new List<Face2>();
            }
        }

        /// <summary>The fingerprints of every face of the body a face lies on; empty if it does not read.</summary>
        private static List<FaceSignature> BodyFaces(Face2 face)
        {
            try
            {
                if (!((object)face.GetBody() is Body2 body)) return new List<FaceSignature>();
                return (((object)body.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>()).Select(TopologyCapture.Face).ToList();
            }
            catch (Exception)
            {
                return new List<FaceSignature>();
            }
        }

        private ExtrudeRecord CaptureExtrude(Feature f, string type, ExtrudeFeatureData2 d)
        {
            Feature sketch = SketchOf(f);
            var r = new ExtrudeRecord(f.Name, type, sketch.Name);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.IsCut = !d.IsBossFeature();
                r.IsThin = d.IsThinFeature();
                r.BothDirections = d.BothDirections;
                r.Reverse = d.ReverseDirection;
                r.FlipSideToCut = d.FlipSideToCut;
                r.Merge = d.Merge;
                r.FeatureScope = d.FeatureScope;
                r.AutoSelect = d.AutoSelect;
                r.Start = (StartCondition)d.FromType;
                r.StartOffset = d.FromOffsetDistance;
                r.StartOffsetReverse = d.FromOffsetReverse;
                if (r.Start == StartCondition.Surface || r.Start == StartCondition.Vertex)
                {
                    d.GetFromEntity(out object fromEntity, out int _);
                    r.StartReference = ToGeometryRef(fromEntity);
                }
                if (r.IsCut)
                {
                    r.NormalCut = d.NormalCut;
                    r.OptimizeGeometry = d.OptimizeGeometry;
                }
                // Only ever true in sheet metal: the depth follows the sheet thickness.
                r.LinkToThickness = d.LinkToThickness;
                if (r.IsThin)
                {
                    r.ThinType = d.ThinWallType;
                    r.CapEnds = d.CapEnds;
                    r.CapThickness = d.CapThickness;
                    r.AutoFillet = d.GetAutoFilletCorners();
                    r.AutoFilletRadius = d.GetAutoFilletRadius();
                }
                NoteScope(r.Notes, r.FeatureScope, r.AutoSelect);
                ReadSide(d, true, r.Side1, r.IsThin, true);
                // A single-direction thin feature still needs the second wall thickness; its
                // direction-2 end condition is unused (and may hold stale settings).
                if (r.BothDirections || r.IsThin) ReadSide(d, false, r.Side2, r.IsThin, r.BothDirections);

                // "Direction of extrusion": extruded along a line/edge/plane normal instead of the sketch normal.
                d.GetDirectionReference(out object dir1, out int _, out object dir2, out int _);
                r.DirectionReference1 = ToGeometryRef(dir1);
                r.DirectionReference2 = ToGeometryRef(dir2);

                ReadProfile(sketch, () => d.GetContoursCount(), () => (object)d.Contours, r);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        /// <summary>
        /// Selected contours (as sets of segment ids) and sketch regions (as shapes in sketch
        /// coordinates), read while AccessSelections is active – region objects only exist then.
        /// All or nothing: if any item cannot be read, neither list keeps anything, the record
        /// is marked (<see cref="IProfileRecord.SelectionLost"/>) and gets one note; the builder
        /// then decides between the whole sketch and not building (same part, merge mode).
        /// Never throws: an unreadable region must not end the run.
        /// </summary>
        private void ReadProfile<T>(Feature sketch, Func<int> count, Func<object?> items, T r) where T : FeatureRecord, IProfileRecord
        {
            string? why;
            try
            {
                int n;
                try
                {
                    n = count();
                }
                catch (Exception)
                {
                    // Unknown: the list itself tells whether there is a selection.
                    n = -1;
                }
                if (n == 0) return;
                object[]? list = items() as object[];
                if (n < 0 && (list == null || list.Length == 0)) return;
                why = TryReadProfile(sketch, list, r);
            }
            catch (Exception ex)
            {
                why = ex.Message;
            }
            if (why == null) return;
            r.Contours.Clear();
            r.Regions.Clear();
            r.RegionChordTolerance = 0;
            r.SelectionLost = true;
            r.Notes.Add(ProfileRules.Unreadable(why));
        }

        /// <summary>Reads the items; returns why not, or null. Regions are told apart from contours first (TypeOf … Is SketchRegion).</summary>
        private string? TryReadProfile(Feature sketch, object[]? items, IProfileRecord r)
        {
            if (items == null || items.Length == 0) return "die Feature-Daten nennen keine Konturen";
            var regions = new List<SketchRegion>();
            foreach (object item in items)
            {
                switch (item)
                {
                    case SketchRegion region:
                        if (SwDoc.SketchNameOf(region.Sketch) != sketch.Name) return "ein Skizzenbereich gehört zu einer anderen Skizze";
                        regions.Add(region);
                        break;
                    case SketchContour contour:
                        if (SwDoc.SketchNameOf(contour.Sketch) != sketch.Name) return "eine Kontur gehört zu einer anderen Skizze";
                        object[] segments = (object)contour.GetSketchSegments() as object[] ?? Array.Empty<object>();
                        if (segments.Length == 0) return "eine Kontur nennt keine Skizzenelemente";
                        r.Contours.Add(segments.OfType<SketchSegment>().Select(SwDoc.IdOf).ToList());
                        break;
                    default:
                        return "ein Element ist weder Kontur noch Skizzenbereich";
                }
            }
            if (regions.Count == 0) return null;

            var sk = (Sketch)(object)sketch.GetSpecificFeature2();
            Frame3 frame = _math.SketchFrame(sk);
            List<ProfileRegion>? shapes = RegionCapture.ReadSelected(regions, frame, RegionCapture.SketchPoints(sk), _regionSpaces, out double eps, out string? why);
            if (shapes == null) return why ?? "Skizzenbereich nicht lesbar";
            r.Regions.AddRange(shapes);
            r.RegionChordTolerance = eps;
            return null;
        }

        private void ReadSide(ExtrudeFeatureData2 d, bool forward, ExtrudeSide side, bool thin, bool used)
        {
            side.End = (EndCondition)d.GetEndCondition(forward);
            side.Depth = d.GetDepth(forward);
            side.Draft = d.GetDraftWhileExtruding(forward);
            side.DraftOutward = d.GetDraftOutward(forward);
            side.DraftAngle = d.GetDraftAngle(forward);
            side.OffsetReverse = d.GetReverseOffset(forward);
            side.TranslateSurface = d.GetTranslateSurface(forward);
            if (thin) side.WallThickness = d.GetWallThickness(forward);
            if (used && ExtrudeSide.NeedsReference(side.End))
            {
                object reference = d.GetEndConditionReference(forward, out int _);
                side.Reference = ToGeometryRef(reference);
                if (side.Reference == null || side.Reference.Kind == GeometryRefKind.Unknown)
                    throw new NotSupportedException($"Bezug der Endbedingung {side.End} ist im Original nicht lesbar.");
            }
        }

        private RevolveRecord CaptureRevolve(Feature f, string type, RevolveFeatureData2 d)
        {
            Feature sketch = SketchOf(f);
            var r = new RevolveRecord(f.Name, type, sketch.Name);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.IsCut = !d.IsBossFeature();
                r.IsThin = d.IsThinFeature();
                r.RevolveType = d.Type;
                r.Angle1 = d.GetRevolutionAngle(true);
                r.Angle2 = d.GetRevolutionAngle(false);
                r.Reverse = d.ReverseDirection;
                r.Merge = d.Merge;
                if (r.IsThin)
                {
                    r.ThinType = d.ThinWallType;
                    r.Thickness1 = d.GetWallThickness(true);
                    r.Thickness2 = d.GetWallThickness(false);
                }
                r.Axis = ToGeometryRef((object)d.Axis);
                NoteScope(r.Notes, d.FeatureScope, d.AutoSelect);
                ReadProfile(sketch, () => d.GetContoursCount(), () => (object)d.Contours, r);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private FilletRecord CaptureFillet(Feature f, string type, SimpleFilletFeatureData2 d)
        {
            if (d.Type != (int)swSimpleFilletType_e.swConstRadiusFillet)
                throw new NotSupportedException($"Verrundungstyp {d.Type} wird nicht unterstützt.");
            if (d.ConicTypeForCrossSectionProfile != (int)swFeatureFilletProfileType_e.swFeatureFilletCircular)
                throw new NotSupportedException("Nur kreisförmige Verrundungsprofile werden unterstützt (kein Kegelschnitt).");
            if (d.AsymmetricFillet)
                throw new NotSupportedException("Asymmetrische Verrundungen werden nicht unterstützt.");

            var r = new FilletRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Radius = d.DefaultRadius;
                r.MultipleRadius = d.IsMultipleRadius;
                r.Propagate = d.PropagateToTangentFaces;
                r.OverflowType = d.OverflowType;
                r.KeepFeatures = d.KeepFeatures;
                r.RoundCorners = d.RoundCorners;

                // FilletItemsCount/GetFilletItemAtIndex only describe multiple-radius fillets
                // (-1 otherwise); a plain fillet lists its items as edges and faces.
                int n = r.MultipleRadius ? d.FilletItemsCount : 0;
                for (int i = 0; i < n; i++)
                {
                    object item = d.GetFilletItemAtIndex(i);
                    if (item != null) AddFilletItem(r, item, d.GetRadius(item));
                }
                if (r.Items.Count == 0)
                {
                    if (d.GetEdgeCount() > 0 && d.Edges is object[] edges)
                        foreach (object e in edges) AddFilletItem(r, e, r.MultipleRadius ? d.GetRadius(e) : r.Radius);
                    const int single = (int)swSimpleFilletWhichFaces_e.swSimpleFilletSingleRadius;
                    if (d.GetFaceCount(single) > 0 && d.GetFaces(single) is object[] faces)
                        foreach (object face in faces) AddFilletItem(r, face, r.MultipleRadius ? d.GetRadius(face) : r.Radius);
                }
                if (d.GetLoopCount() > 0 || d.GetFeatureCount() > 0)
                    r.Notes.Add("Schleifen oder Features als Verrundungselemente werden nicht übernommen.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Items.Count == 0) throw new NotSupportedException("Verrundung ohne übertragbare Kanten oder Flächen.");
            return r;
        }

        private static void AddFilletItem(FilletRecord r, object item, double radius)
        {
            switch (item)
            {
                case Edge e:
                    r.Items.Add(new FilletItem(TopologyCapture.Edge(e), null, radius));
                    break;
                case Face2 face:
                    r.Items.Add(new FilletItem(null, TopologyCapture.Face(face), radius));
                    break;
                default:
                    r.Notes.Add("Ein Verrundungselement (Schleife oder Feature) wird nicht übernommen.");
                    break;
            }
        }

        private ChamferRecord CaptureChamfer(Feature f, string type, ChamferFeatureData2 d)
        {
            var chType = (swChamferType_e)d.Type;
            if (chType == swChamferType_e.swChamferVertex)
                throw new NotSupportedException("Eckpunkt-Fasen werden nicht unterstützt.");

            var r = new ChamferRecord(f.Name, type) { ChamferType = d.Type };
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Distance1 = d.GetEdgeChamferDistance(0);
                r.Distance2 = d.GetEdgeChamferDistance(1);
                r.Angle = d.EdgeChamferAngle;
                r.TangentPropagation = d.TangentPropagation;
                r.KeepFeatures = d.KeepFeatures;
                if (d.GetEdgeCount() > 0)
                {
                    var edges = ((object[])(object)d.Edges).Cast<Edge>().ToList();
                    r.Flip = d.GetIsFlipped(edges[0]);
                    if (edges.Any(e => d.GetIsFlipped(e) != r.Flip))
                        r.Notes.Add("Fasenrichtung ist nicht bei allen Kanten gleich; es wird die Richtung der ersten Kante verwendet.");
                    r.Edges.AddRange(edges.Select(TopologyCapture.Edge));
                }
                if (d.GetFaceCount() > 0)
                    r.Faces.AddRange(((object[])(object)d.Faces).Cast<Face2>().Select(TopologyCapture.Face));
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Edges.Count == 0 && r.Faces.Count == 0) throw new NotSupportedException("Fase ohne übertragbare Kanten oder Flächen.");
            return r;
        }

        /// <summary>The sketch a feature is built from: first absorbed sketch, otherwise a sketch parent (shared sketch).</summary>
        private static Feature SketchOf(Feature f)
        {
            Feature? sketch = SwDoc.SubFeatures(f).FirstOrDefault(SwDoc.IsSketch);
            if (sketch == null && f.GetParents() is object[] parents)
                sketch = parents.OfType<Feature>().FirstOrDefault(SwDoc.IsSketch);
            if (sketch == null) throw new NotSupportedException("Skizze des Features nicht gefunden.");
            if (sketch.GetTypeName2() != "ProfileFeature") throw new NotSupportedException("Features aus 3D-Skizzen werden nicht unterstützt.");
            return sketch;
        }

        private GeometryRef? ToGeometryRef(object o)
        {
            switch (o)
            {
                case null:
                    return null;
                case Face2 face:
                    return GeometryRef.ToFace(TopologyCapture.Face(face));
                case Edge e:
                {
                    Line3? line = TopologyCapture.LineOfEdge(e);
                    return line.HasValue ? GeometryRef.ToAxis(line.Value, "Kante") : GeometryRef.ToEdge(TopologyCapture.Edge(e));
                }
                case Vertex v:
                    return GeometryRef.ToVertex(TopologyCapture.Vertex(v));
                case SketchPoint p:
                {
                    Frame3 frame = _math.SketchFrame(p.GetSketch());
                    return GeometryRef.ToSketchPoint(SwDoc.SketchNameOf(p.GetSketch()), SwDoc.IdOf(p), frame.ToWorld(p.X, p.Y));
                }
                case SketchSegment s:
                    return GeometryRef.ToSketchSegment(SwDoc.SketchNameOf(s.GetSketch()), SwDoc.IdOf(s));
                // A sketch object is also a feature (and the other way round): ask for it before Feature.
                case Sketch sk:
                    return SketchRef(sk);
                case RefPlane rp:
                    return GeometryRef.ToPlane(_math.PlaneOf(rp), "Ebene");
                case RefAxis ax:
                    return GeometryRef.ToAxis(SwDoc.AxisLine(ax), "Achse");
                case Feature feat:
                    // Only the specific objects handled above; anything else would recurse.
                    return feat.GetSpecificFeature2() switch
                    {
                        RefPlane rp => GeometryRef.ToPlane(_math.PlaneOf(rp), "Ebene"),
                        RefAxis ax => GeometryRef.ToAxis(SwDoc.AxisLine(ax), "Achse"),
                        Sketch sk => SketchRef(sk),
                        _ => GeometryRef.Unknown(feat.GetTypeName2()),
                    };
                default:
                    return GeometryRef.Unknown(o.GetType().Name);
            }
        }

        private static GeometryRef SketchRef(Sketch sk) =>
            sk.Is3D() ? GeometryRef.Unknown("3D-Skizze") : GeometryRef.ToSketch(SwDoc.SketchNameOf(sk));

        private static bool ChangesSolid(string type) => UnsupportedFeatures.ChangesSolid(type);
    }
}
