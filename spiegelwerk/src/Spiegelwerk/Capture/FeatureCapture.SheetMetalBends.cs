using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Bends from a sketch (A3, blech-plan S4): the sketched bend. Through the safe sheet-metal
    /// dispatch (<see cref="CaptureSheetMetal"/>), so a call that throws makes this feature
    /// unsupported, not the run fail. Under AccessSelections (the state before the bend): the values,
    /// the fixed face and the point it was picked at, the faces of its body; outside it: the face its
    /// sketch was drawn on (bar after the sketch), its bends, and the witnesses (bar after the bend).
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        private SketchedBendRecord CaptureSketchedBend(Feature f, string type, SketchedBendFeatureData d)
        {
            Feature sketchFeature = SketchOf(f);
            var r = new SketchedBendRecord(f.Name, type, sketchFeature.Name);
            SketchModel sketch = _sketches.Read(sketchFeature);
            r.BendLines.AddRange(SheetMetalBendRules.BendLines(sketch));
            if (r.BendLines.Count == 0) throw new NotSupportedException(SheetMetalBendRules.NoBendLines);

            var before = new List<FaceSignature>();
            string? sheetMetal = null;
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition der skizzierten Biegung konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (SwSheetMetal.ReadSketchedBendScalars(d, r)) r.Notes.Add(OwnAllowanceUnreadable);
                if (SheetMetalBendRules.BendPosForCreation(r.PositionType) == null)
                    throw new NotSupportedException(SheetMetalBendRules.PositionNotCreatable(r.PositionType));
                // The help: the point is in the space of the sketch, z always 0 (B38) – read both ways, the face decides.
                var face = (object)d.GetFixedFace(out double x, out double y, out double z) as Face2
                           ?? throw new NotSupportedException(SheetMetalBendRules.FixedFaceUnreadable);
                r.FixedFace = TopologyCapture.Face(face);
                if (r.FixedFace.Kind != SurfaceKind.Plane) throw new NotSupportedException("Feste Fläche der skizzierten Biegung ist nicht eben.");
                Frame3 frame = _math.SketchFrame((Sketch)(object)sketchFeature.GetSpecificFeature2());
                FixedPointChoice choice = ChooseFixedPoint(FixedPointSpace.Sketch, face, frame, new Vec3(x, y, z), r.BendLines, r.FixedFace.Normal);
                r.FixedPoint = choice.Point;
                r.FixedPointSpace = choice.Space;
                r.FixedPointAmbiguous = choice.Ambiguous;
                before.AddRange(BodyFaces(face));
                sheetMetal = SheetMetalOfBody(face);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            // Newer versions only: a gauge table of the bend itself; the copy takes the bend's values as numbers.
            if (SwLate.Get(d, "UseGaugeTable") is bool gauge && gauge)
                r.Notes.Add("Die Blechstärkentabelle der Biegung wird nicht übernommen; die Kopie hat ihre Werte als Zahlen.");
            r.Thickness = ThicknessOf(sheetMetal);

            r.SketchFace = CaptureSupportFace(sketchFeature);
            ReadBendsInto(f, SheetMetalBendRules.Parent(r), r.Bends, r.BendOverrides, r.Notes, out int count);
            r.BendCount = count;
            // None: the builder says so – a note unless the check can confirm the direction.
            r.Witnesses.AddRange(CaptureWitnesses(f, r.FixedFace!, before) ?? new List<Vec3>());
            return r;
        }

        /// <summary>
        /// A fixed point read in both spaces (B38): <paramref name="raw"/> as model coordinates and, if its
        /// z is 0, as sketch coordinates mapped with the frame; each reading's distance to the fixed face
        /// by the kernel (under the feature's AccessSelections, where the face is valid).
        /// </summary>
        private static FixedPointChoice ChooseFixedPoint(FixedPointSpace preferred, Face2 face, Frame3 frame, Vec3 raw, IReadOnlyList<Line3> lines, Vec3 normal)
        {
            bool sketchLike = Math.Abs(raw.Z) <= Tol.Length;
            Vec3 asSketch = frame.ToWorld(raw.X, raw.Y);
            double dSketch = sketchLike ? TopologyCapture.DistanceToFace(face, asSketch) : double.PositiveInfinity;
            double dModel = TopologyCapture.DistanceToFace(face, raw);
            return BendSide.ChooseFixedPoint(preferred, asSketch, dSketch, raw, dModel, BendSide.OnFaceTolerance, p => BendSide.RegionOf(p, lines, normal));
        }

        /// <summary>Name of the SheetMetal feature of the body a face lies on; null if none reads.</summary>
        private static string? SheetMetalOfBody(Face2 face)
        {
            try
            {
                return ((object)face.GetBody() as Body2)?.GetFeatures() is object[] fs
                    ? fs.OfType<Feature>().FirstOrDefault(x => x.GetTypeName2() == "SheetMetal")?.Name
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The thickness of a body's SheetMetal (for the side clearance); 0 if it does not read.</summary>
        private double ThicknessOf(string? sheetMetal)
        {
            if (sheetMetal == null || SwDoc.FeatureByName(_doc, sheetMetal) is not Feature sm) return 0;
            try
            {
                return SwSheetMetal.ReadParameters(_doc, sm, bodyLevel: true).Thickness;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
