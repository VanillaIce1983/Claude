using System;
using System.Collections.Generic;
using System.IO;
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
    /// Forming tools (A4 (2) §5, P10, P11, P15, blech-plan S9). Dispatched by its type name before its definition –
    /// null before 2025 SP2 –, through the safe sheet-metal dispatch (<see cref="CaptureSheetMetal"/>), so a call that
    /// throws makes this feature unsupported, not the run fail. Read: its position sketch in full (the instances are its
    /// points) and the points of its orientation sketch; the tool file from its references, its data (2025 SP2) or the
    /// design library; with the bar right after the position sketch (just before the tool) the placement face and the
    /// body's thickness; with the bar right after the tool the points on its faces – the samples its copy is checked
    /// against – and the side its instances stand on. A tool file that is nowhere to be found makes it unsupported.
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        private FormToolRecord CaptureFormTool(Feature f, string type)
        {
            List<Feature> sketches = SwFormTool.Sketches(f);
            if (sketches.Count == 0) throw new NotSupportedException(FormToolPlacement.NoPositionSketch);
            Feature position = sketches[0];
            if (position.GetTypeName2() != "ProfileFeature") throw new NotSupportedException(FormToolPlacement.PositionSketch3D);
            Feature? orientation = sketches.Count > 1 && sketches[1].GetTypeName2() == "ProfileFeature" ? sketches[1] : null;
            SketchModel model = ReadSketch(position);
            List<Vec3> points = model.FreePoints().Select(model.ToModel).ToList();
            if (points.Count == 0) throw new NotSupportedException(FormToolPlacement.NoPoints);
            var r = new FormToolRecord(f.Name, type, position.Name, model, orientation?.Name);
            r.Points.AddRange(points);

            ResolveTool(f, r);

            var (face, sheetMetal) = PlacementOf(position, points[0], model.Frame.ZAxis);
            if (face == null) throw new NotSupportedException(FormToolPlacement.PlacementUnreadable);
            r.Face = GeometryRef.ToFace(face);
            r.PlacementNormal = face.Normal;
            r.Thickness = ThicknessOf(sheetMetal);

            if (orientation != null) r.OrientationPoints.AddRange(SwFormTool.SketchPoints(_math, orientation, userOnly: false));
            CaptureShape(f, r);
            if (r.ShapeSamples.Count == 0) throw new NotSupportedException(FormToolPlacement.ShapeUnreadable);
            return r;
        }

        /// <summary>
        /// The tool file: named as an external reference of the feature (A4 P11: the path only – the configuration it lists is
        /// the referencing one's), else by its data (2025 SP2 and later), else – or if the file named is not there – found in
        /// the design library by its file name or the feature's name. The configuration, the angle and the flat-pattern display
        /// from the data where they answer; linked as its data say, otherwise – inferred, and marked so – if it names the tool as
        /// a reference (B70).
        /// </summary>
        private void ResolveTool(Feature f, FormToolRecord r)
        {
            object? def = null;
            try
            {
                def = (object?)f.GetDefinition();
            }
            catch (Exception)
            {
                // Before 2025 SP2 there is none.
            }
            var (dataPath, configuration, link, angle) = SwFormTool.FromData(def);
            r.ToolConfiguration = configuration;
            r.RotationAngle = angle;
            (r.OverrideDisplay, r.ShowPunch, r.ShowProfile, r.ShowCenter) = SwFormTool.DisplayFromData(def);
            string? referenced = FormToolPlacement.PickToolPath(SwFormTool.ReferencedFiles(f), f.Name);
            if (referenced != null && File.Exists(referenced))
            {
                r.ToolPath = referenced;
                r.Source = ToolSource.ExternalReference;
            }
            else if (dataPath != null && File.Exists(dataPath))
            {
                r.ToolPath = dataPath;
                r.Source = ToolSource.LateBound;
            }
            else
            {
                string? named = referenced ?? dataPath;
                var names = new List<string>();
                if (named != null) names.Add(FormToolPlacement.FileName(named));
                foreach (string n in FormToolPlacement.LibraryFileNames(f.Name))
                    if (!names.Contains(n, StringComparer.OrdinalIgnoreCase))
                        names.Add(n);
                string found = SwFormTool.SearchLibrary(_sw, names) ?? throw new NotSupportedException(FormToolPlacement.ToolNotFound);
                r.ToolPath = found;
                r.Source = ToolSource.LibrarySearch;
                if (named != null) r.Notes.Add(FormToolPlacement.Moved(named, found));
            }
            r.LinkToTool = link ?? referenced != null;
            r.LinkInferred = link == null && r.LinkToTool;
        }

        /// <summary>
        /// The face the position sketch stands on and the SheetMetal of its body, with the bar right after the sketch – just
        /// before the tool (K7): the sketch's reference entity, else the planar face of a body through the first point with the
        /// sketch's normal. (null, null) if neither reads or the face is not planar.
        /// </summary>
        private (FaceSignature? face, string? sheetMetal) PlacementOf(Feature position, Vec3 at, Vec3 sketchNormal)
        {
            if (!RollBack((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, position.Name)) return (null, null);
            try
            {
                Face2? face = null;
                if ((object)position.GetSpecificFeature2() is Sketch sketch)
                {
                    int kind = 0;
                    face = (object)sketch.GetReferenceEntity(ref kind) as Face2;
                }
                face ??= SwDoc.SolidBodies(_doc)
                    .SelectMany(b => ((object)b.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>())
                    .FirstOrDefault(x => TopologyCapture.Face(x) is FaceSignature s && s.Kind == SurfaceKind.Plane &&
                                         s.Normal.IsParallelTo(sketchNormal, 1e-6) && OnFace(x, at));
                if (face == null) return (null, null);
                FaceSignature sig = TopologyCapture.Face(face);
                return sig.Kind == SurfaceKind.Plane ? (sig, SheetMetalOfBody(face)) : (null, null);
            }
            catch (Exception)
            {
                return (null, null);
            }
            finally
            {
                _doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }

        /// <summary>
        /// With the bar right after the tool (a later bend may move it, a later cut trim it): points on its faces, the side its
        /// instances stand on and the samples per instance (<see cref="FormToolPlacement.PickSamples"/>). None if the bar does
        /// not move or the faces do not read – then the copy could not be checked.
        /// </summary>
        private void CaptureShape(Feature f, FormToolRecord r)
        {
            if (!RollBack((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, f.Name)) return;
            List<Vec3> points;
            try
            {
                points = SwFormTool.ShapePoints(f);
            }
            catch (Exception)
            {
                points = new List<Vec3>();
            }
            finally
            {
                _doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
            r.ProtrusionSide = FormToolPlacement.ProtrusionSide(points, new Plane3(r.Points[0], r.PlacementNormal), r.Thickness);
            foreach (var (p, instance) in FormToolPlacement.PickSamples(points, r.Points, r.PlacementNormal))
            {
                r.ShapeSamples.Add(p);
                r.SampleInstance.Add(instance);
            }
        }
    }
}
