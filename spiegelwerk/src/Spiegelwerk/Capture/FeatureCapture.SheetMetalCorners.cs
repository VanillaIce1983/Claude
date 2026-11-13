using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Corners, folds and the cross break (A3 (2) §4, blech-plan S5). Through the safe sheet-metal dispatch
    /// (<see cref="CaptureSheetMetal"/>), so a call that throws makes this feature unsupported, not the run
    /// fail. Values and references are read under AccessSelections – the state before the feature: the faces
    /// a closed corner extends, the edges and faces a corner is broken at, the fixed face and bends of an
    /// unfold or fold (in the state the bends had then: folded before an unfold, flat before a fold), the
    /// face of a cross break, the corners of a corner relief (late-bound, 2022 and later).
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        private ClosedCornerRecord CaptureClosedCorner(Feature f, string type, ClosedCornerFeatureData d)
        {
            var r = new ClosedCornerRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition der geschlossenen Ecke konnte nicht gelesen werden (AccessSelections).");
            try
            {
                SwSheetMetal.ReadClosedCornerScalars(d, r);
                foreach (object o in (object)d.Faces as object[] ?? Array.Empty<object>())
                {
                    if (!(o is Face2 face)) throw new NotSupportedException("Geschlossene Ecke: ein gewähltes Element ist keine Fläche.");
                    r.Faces.Add(TopologyCapture.Face(face));
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Faces.Count == 0) throw new NotSupportedException("Geschlossene Ecke ohne lesbare Flächen.");
            return r;
        }

        private BreakCornerRecord CaptureBreakCorner(Feature f, string type, BreakCornerFeatureData d)
        {
            var r = new BreakCornerRecord(f.Name, type);
            int count;
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition der gebrochenen Ecke konnte nicht gelesen werden (AccessSelections).");
            try
            {
                SwSheetMetal.ReadBreakCornerScalars(d, r);
                count = d.GetEntitiesCount();
                foreach (object o in (object)d.Entities as object[] ?? Array.Empty<object>())
                    switch (o)
                    {
                        case Edge e:
                            r.Edges.Add(TopologyCapture.Edge(e));
                            break;
                        case Face2 face:
                            r.Faces.Add(TopologyCapture.Face(face));
                            break;
                        default:
                            throw new NotSupportedException("Gebrochene Ecke: ein gewähltes Element ist weder Kante noch Fläche.");
                    }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Edges.Count + r.Faces.Count == 0) throw new NotSupportedException("Gebrochene Ecke ohne lesbare Kanten oder Flächen.");
            if (count >= 0 && count != r.Edges.Count + r.Faces.Count)
                r.Notes.Add($"Gebrochene Ecke: {count} Elemente gemeldet, {r.Edges.Count + r.Faces.Count} gelesen; übertragen werden die gelesenen.");
            return r;
        }

        /// <summary>
        /// Cross break: values and face under AccessSelections; its sketch sub-feature (the break lines, B48) –
        /// it belongs to it (<see cref="SheetMetalTree"/>) – read as an ordinary sketch, its lines in model
        /// coordinates. More than one sketch makes it unsupported (which would get which lines?); a sketch that
        /// does not read costs only the lines (the copy keeps those SOLIDWORKS draws).
        /// </summary>
        private CrossBreakRecord CaptureCrossBreak(Feature f, string type, CrossBreakFeatureData d)
        {
            var r = new CrossBreakRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition des Querbruchs konnte nicht gelesen werden (AccessSelections).");
            try
            {
                SwSheetMetal.ReadCrossBreakScalars(d, r);
                r.Face = (object)d.Face is Face2 face ? TopologyCapture.Face(face) : throw new NotSupportedException("Fläche des Querbruchs nicht lesbar.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Face.Kind != SurfaceKind.Plane) throw new NotSupportedException("Fläche des Querbruchs ist nicht eben.");
            List<Feature> sketches = SwDoc.SubFeatures(f).Where(SwDoc.IsSketch).ToList();
            if (sketches.Count > 1) throw new NotSupportedException($"Querbruch mit {sketches.Count} Skizzen; welche Bruchlinien wohin gehören, ist nicht bestimmbar.");
            if (sketches.Count == 1)
            {
                Feature s = sketches[0];
                r.LineSketchName = s.Name;
                try
                {
                    if (s.GetTypeName2() != "ProfileFeature") throw new NotSupportedException("3D-Skizze");
                    SketchModel model = ReadSketch(s);
                    r.LineSketch = model;
                    r.Lines.AddRange(CornerRules.BreakLines(model));
                }
                catch (Exception ex)
                {
                    r.LineSketch = null;
                    r.Lines.Clear();
                    r.Notes.Add($"Bruchlinien des Querbruchs nicht lesbar ({ex.Message}); die Kopie behält die Bruchlinien, die SOLIDWORKS anlegt.");
                }
            }
            return r;
        }

        /// <summary>
        /// Unfold or fold: under AccessSelections the fixed face and each bend with its faces – in this state
        /// (folded before an unfold, flat before a fold; B47) –, then each bend's owner and its place among the
        /// owner's bends (by definition). A fold also names the last unfold before it (official selection).
        /// </summary>
        private FoldRecord CaptureFold(Feature f, string type, FoldsFeatureData d)
        {
            var r = new FoldRecord(f.Name, type);
            var bends = new List<(Feature bend, List<FaceSignature> faces)>();
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException($"Feature-Definition von „{FoldRules.KindName(r.Kind)}“ konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.FixedFace = (object)d.FixedFace is Face2 face
                    ? TopologyCapture.Face(face)
                    : throw new NotSupportedException($"Feste Fläche von „{FoldRules.KindName(r.Kind)}“ nicht lesbar.");
                foreach (object o in (object)d.Bends as object[] ?? Array.Empty<object>())
                {
                    if (!(o is Feature bend)) throw new NotSupportedException($"„{FoldRules.KindName(r.Kind)}“: ein gewähltes Element ist keine Biegung.");
                    // Without faces it is found by name or as its owner's only bend.
                    List<FaceSignature> faces = FacesOf(bend).Select(TopologyCapture.Face).ToList();
                    bends.Add((bend, faces));
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (bends.Count == 0) throw new NotSupportedException($"„{FoldRules.KindName(r.Kind)}“ ohne lesbare Biegungen.");
            foreach (var (bend, faces) in bends)
            {
                Feature? owner = OwnerOf(bend);
                List<Feature> ofOwner = owner == null ? new List<Feature>() : SwSheetMetal.BendsOf(owner);
                int ordinal = ofOwner.FindIndex(b => b.Name == bend.Name);
                var b = new BendRef(owner?.Name ?? "", bend.Name, ordinal, ofOwner.Count);
                b.Faces.AddRange(faces);
                r.Bends.Add(b);
            }
            if (r.Kind == FoldKind.Fold) r.PrecedingUnfold = PrecedingOfType(f.Name, "UnFold");
            return r;
        }

        private static Feature? OwnerOf(Feature sub)
        {
            try
            {
                return (object)sub.GetOwnerFeature() as Feature;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The last feature of a type before <paramref name="feature"/> in creation order; null if there is none or the order does not read.</summary>
        private string? PrecedingOfType(string feature, string type)
        {
            Dictionary<string, int> order = CreationOrderIndex();
            if (!order.TryGetValue(feature, out int at)) return null;
            try
            {
                return SwDoc.FeaturesInCreationOrder(_doc).Where(x => order.TryGetValue(x.Name, out int i) && i < at && x.GetTypeName2() == type)
                    .Select(x => x.Name).LastOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Corner relief (2022 and later, late-bound; B44, B49): under its selection access the bend type of its
        /// corners, a face of its body (the largest – the body scope names the body), and every corner with its
        /// faces in their roles and its values. A corner whose faces do not read makes it unsupported.
        /// </summary>
        private CornerReliefRecord CaptureCornerRelief(Feature f, string type, object def)
        {
            var r = new CornerReliefRecord(f.Name, type);
            if (!SwCornerRelief.Access(_doc, def))
                throw new NotSupportedException("Feature-Definition der Eckentlastung konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.CornerType = SwCornerRelief.CornerType(def);
                if (SwCornerRelief.BodyScope(def) is Body2 body && (object)body.GetFaces() is object[] faces)
                    r.BodyFace = faces.OfType<Face2>().Select(TopologyCapture.Face).OrderByDescending(s => s.Area).FirstOrDefault();
                int count = SwCornerRelief.CornerCount(def);
                for (int i = 0; i < count; i++)
                {
                    object corner = SwCornerRelief.CornerAt(def, i) ?? throw new NotSupportedException($"Ecke {i + 1} der Eckentlastung nicht lesbar.");
                    var (f1, f2, f3) = SwCornerRelief.Faces(corner);
                    if (f1 == null || f2 == null) throw new NotSupportedException($"Flächen der Ecke {i + 1} der Eckentlastung nicht lesbar.");
                    var item = new CornerReliefItem
                    {
                        Face1 = TopologyCapture.Face(f1),
                        Face2 = TopologyCapture.Face(f2),
                        Face3 = f3 == null ? null : TopologyCapture.Face(f3),
                    };
                    SwCornerRelief.ReadValues(corner, item);
                    r.Corners.Add(item);
                }
            }
            finally
            {
                SwCornerRelief.Release(def);
            }
            if (r.Corners.Count == 0) throw new NotSupportedException("Eckentlastung ohne lesbare Ecken.");
            if (r.BodyFace == null) r.Notes.Add("Körper der Eckentlastung nicht lesbar; angenommen wird der Körper ihrer ersten Ecke.");
            return r;
        }
    }
}
