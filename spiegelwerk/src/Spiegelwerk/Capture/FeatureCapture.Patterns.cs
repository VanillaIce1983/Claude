using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>Linear, circular, curve, sketch and table driven patterns: seeds, directions and the placements of the instances.</summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>Beyond this many instances the placements are not read (one call each); the mass check remains.</summary>
        private const int MaxInstancesRead = 400;

        private LinearPatternRecord CaptureLinearPattern(Feature f, string type, LinearPatternFeatureData d)
        {
            var r = new LinearPatternRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Direction1 = RequireDirection(d.D1Axis, "Richtung 1");
                r.Reverse1 = d.D1ReverseDirection;
                r.Spacing1 = d.D1Spacing;
                r.Count1 = d.D1TotalInstances;
                r.HasDirection2 = d.IsDirection2Specified();
                if (r.HasDirection2)
                {
                    r.Direction2 = RequireDirection(d.D2Axis, "Richtung 2");
                    r.Reverse2 = d.D2ReverseDirection;
                    r.Spacing2 = d.D2Spacing;
                    r.Count2 = d.D2TotalInstances;
                    r.SeedOnly2 = d.D2PatternSeedOnly;
                }
                if (d.D1EndCondition != (int)swPatternEndCondition_e.swPatternEndCondition_SpacingAndInstances ||
                    (r.HasDirection2 && d.D2EndCondition != (int)swPatternEndCondition_e.swPatternEndCondition_SpacingAndInstances))
                    r.Notes.Add("„Bis Referenz“ wird als feste Anzahl und fester Abstand übernommen (Werte des Originals).");

                ReadSeeds(r, d.BodyPattern, d.PatternFeatureArray, d.PatternFaceArray, d.PatternBodyArray, d.SkippedItemArray);
                NoteScope(d, r);
                r.GeometryPattern = d.GeometryPattern;
                r.VarySketch = d.VarySketch;
                int n = r.Count1 * (r.HasDirection2 ? Math.Max(1, r.Count2) : 1);
                ReadInstances(r, n, d.GetTransform);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private CircularPatternRecord CaptureCircularPattern(Feature f, string type, CircularPatternFeatureData d)
        {
            var r = new CircularPatternRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Axis = RequireDirection(d.Axis, "Achse");
                r.Reverse = d.ReverseDirection;
                r.Spacing = d.Spacing;
                r.Count = d.TotalInstances;
                r.EqualSpacing = d.EqualSpacing;
                r.Direction2 = d.Direction2;
                if (r.Direction2)
                {
                    r.Symmetric = d.Symmetric;
                    r.Spacing2 = d.Spacing2;
                    r.Count2 = d.TotalInstances2;
                    r.EqualSpacing2 = d.EqualSpacing2;
                }

                ReadSeeds(r, d.BodyPattern, d.PatternFeatureArray, d.PatternFaceArray, d.PatternBodyArray, d.SkippedItemArray);
                NoteScope(d, r);
                r.GeometryPattern = d.GeometryPattern;
                r.VarySketch = d.VarySketch;
                ReadInstances(r, r.Count + (r.Direction2 ? r.Count2 : 0), d.GetTransform);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private CurvePatternRecord CaptureCurvePattern(Feature f, string type, CurveDrivenPatternFeatureData d)
        {
            var r = new CurvePatternRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Curve1 = RequirePatternCurve(d.D1Direction, "Kurve");
                r.Reverse1 = d.D1ReverseDirection;
                r.Count1 = d.D1InstanceCount;
                r.EqualSpacing1 = d.D1IsEqualSpaced;
                r.Spacing1 = d.D1Spacing;
                r.CurveMethod = d.D1CurveMethod;
                r.Alignment = d.D1AlignmentMethod;
                // Newer API versions only: the face whose normal orients the instances along a 3D curve.
                if (SwLate.Get(d, "D1FaceNormal") is object normal)
                {
                    r.FaceNormal = ToGeometryRef(normal);
                    if (r.FaceNormal == null || r.FaceNormal.Kind != GeometryRefKind.Face)
                    {
                        r.FaceNormal = null;
                        r.Notes.Add("Die Fläche für die Normalenrichtung ist nicht lesbar und wird nicht übernommen.");
                    }
                }
                r.HasDirection2 = d.Dir2Specified;
                if (r.HasDirection2)
                {
                    r.Curve2 = RequirePatternCurve(d.D2Direction, "Richtung 2");
                    r.Reverse2 = d.D2ReverseDirection;
                    r.Count2 = d.D2InstanceCount;
                    r.EqualSpacing2 = d.D2IsEqualSpaced;
                    r.Spacing2 = d.D2Spacing;
                    r.SeedOnly2 = d.D2PatternSeedOnly;
                }

                ReadSeeds(r, d.BodyPattern, d.PatternFeatureArray, d.PatternFaceArray, d.PatternBodyArray, d.SkippedItemArray);
                NoteScope(d, r);
                r.GeometryPattern = d.GeometryPattern;
                r.VarySketch = d.VarySketch;
                ReadInstances(r, r.InstanceCount, d.GetTransform);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private SketchPatternRecord CaptureSketchPattern(Feature f, string type, SketchPatternFeatureData d)
        {
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                object reference = d.Sketch;
                Sketch sketch = reference as Sketch ?? (reference as Feature)?.GetSpecificFeature2() as Sketch
                                ?? throw new NotSupportedException("Referenzskizze des Musters nicht lesbar.");
                if (sketch.Is3D()) throw new NotSupportedException("Skizzenmuster über eine 3D-Skizze werden nicht unterstützt.");
                var r = new SketchPatternRecord(f.Name, type, SwDoc.SketchNameOf(sketch))
                {
                    UseCentroid = d.UseCentroid,
                    SketchPointCount = InstancePointBound(sketch),
                };
                if (!r.UseCentroid)
                {
                    (GeometryRef? point, bool origin) = ReadReferencePoint(d.ReferencePoint);
                    r.ReferencePoint = point;
                    r.ReferenceIsOrigin = origin;
                }

                ReadSeeds(r, d.BodyPattern, d.PatternFeatureArray, d.PatternFaceArray, d.PatternBodyArray, null);
                NoteScope(d, r);
                r.GeometryPattern = d.GeometryPattern;
                // Seed plus one instance per sketch point at most; indices without an instance give nothing.
                ReadInstances(r, r.SketchPointCount + 1, d.GetTransform);
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        private TablePatternRecord CaptureTablePattern(Feature f, string type, TablePatternFeatureData d)
        {
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (!(d.CoordinateSystem is Feature cs))
                    throw new NotSupportedException("Koordinatensystem des Tabellenmusters nicht lesbar.");
                Frame3 frame = SwDoc.CoordinateSystemFrame(_doc, _math, cs.Name)
                               ?? throw new NotSupportedException($"Lage des Koordinatensystems „{cs.Name}“ nicht lesbar.");
                var r = new TablePatternRecord(f.Name, type, cs.Name, frame)
                {
                    UseCentroid = d.UseCentroid,
                    PropagateVisualProperty = d.PropagateVisualProperty,
                };
                r.Points.AddRange(TablePoints.FromArray(Doubles(d.PointArray)));
                if (r.Points.Count == 0) throw new NotSupportedException("Punkte des Tabellenmusters nicht lesbar.");
                if (!r.UseCentroid)
                {
                    (GeometryRef? point, bool origin) = ReadReferencePoint(d.ReferencePoint);
                    r.ReferencePoint = point;
                    r.ReferenceIsOrigin = origin;
                }

                // No BodyPattern flag on this data object: a body pattern is one with seed bodies.
                ReadSeeds(r, d.GetPatternBodyCount() > 0, d.PatternFeatureArray, d.PatternFaceArray, d.PatternBodyArray, d.SkippedItemArray);
                NoteScope(d, r);
                r.GeometryPattern = d.GeometryPattern;
                // Seed plus one instance per table point at most; indices without an instance give nothing.
                ReadInstances(r, r.Points.Count + 1, d.GetTransform);
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        private static double[] Doubles(object o) => o switch
        {
            double[] ds => ds,
            object[] os => os.Select(Convert.ToDouble).ToArray(),
            _ => Array.Empty<double>(),
        };

        /// <summary>
        /// Chosen reference point of a sketch or table driven pattern: the origin (rebuilt as
        /// the target's origin where that is right), otherwise a sketch point or vertex.
        /// </summary>
        private (GeometryRef? point, bool origin) ReadReferencePoint(object point)
        {
            if (IsOrigin(point)) return (null, true);
            if (point is SketchPoint sp && sp.GetSketch() is Sketch owner && owner.Is3D())
                throw new NotSupportedException("Referenzpunkt in einer 3D-Skizze wird nicht unterstützt.");
            GeometryRef? g = ToGeometryRef(point);
            if (g == null || g.Kind == GeometryRefKind.Unknown)
                throw new NotSupportedException("Referenzpunkt des Musters nicht lesbar (erwartet: Skizzenpunkt, Eckpunkt oder Ursprung).");
            return (g, false);
        }

        /// <summary>
        /// Seeds are read in the state before the pattern (selection access rolls the model
        /// back): features by name, faces by fingerprint, bodies by volume and first moment.
        /// </summary>
        private static void ReadSeeds(PatternRecord r, bool bodyPattern, object features, object faces, object bodies, object? skipped)
        {
            r.BodyPattern = bodyPattern;
            if (bodyPattern)
            {
                if (bodies is object[] bs)
                    foreach (Body2 b in bs.OfType<Body2>())
                    {
                        // CenterOfMassX, Y, Z, Volume, ...
                        var mp = (double[])b.GetMassProperties(1.0);
                        if (mp != null && mp.Length >= 4) r.SeedBodies.Add(MassState.FromBodies(new[] { (mp[3], new Vec3(mp[0], mp[1], mp[2])) }));
                    }
            }
            else
            {
                if (features is object[] fs)
                    foreach (Feature seed in fs.OfType<Feature>()) r.SeedFeatures.Add(seed.Name);
                if (faces is object[] fcs)
                    foreach (Face2 face in fcs.OfType<Face2>()) r.SeedFaces.Add(TopologyCapture.Face(face));
            }
            if (r.SeedCount == 0) throw new NotSupportedException("Muster ohne lesbare Features, Flächen oder Körper.");

            switch (skipped)
            {
                case int[] ints:
                    r.SkippedItems.AddRange(ints);
                    break;
                case object[] objs:
                    r.SkippedItems.AddRange(objs.Select(Convert.ToInt32));
                    break;
            }
        }

        /// <summary>
        /// Placements seed → instance as the pattern reports them. Indices that do not exist
        /// give nothing; the rebuilt pattern is read with the same indices, so both sets
        /// describe the same thing whatever the numbering.
        /// </summary>
        private void ReadInstances(PatternRecord r, int count, Func<int, MathTransform> transform)
        {
            if (count <= 0 || count > MaxInstancesRead) return;
            foreach (Affine a in SwPattern.Instances(_math, count, transform)) r.Instances.Add(a);
        }

        /// <summary>A pattern limited to chosen bodies (newer API versions only) is rebuilt without that limit.</summary>
        private static void NoteScope(object d, PatternRecord r)
        {
            if (SwLate.Get(d, "FeatureScope") is bool scope && scope && SwLate.Get(d, "AutoSelect") is bool auto && !auto)
                r.Notes.Add("Der Feature-Bereich (ausgewählte Körper) wird nicht übernommen.");
        }

        /// <summary>
        /// Upper bound for the instances of a sketch driven pattern: standalone points plus
        /// arc and ellipse centres – not the end points of segments, which in a layout sketch
        /// can be many times more and would push the count past <see cref="MaxInstancesRead"/>.
        /// </summary>
        private static int InstancePointBound(Sketch sketch)
        {
            try
            {
                return sketch.GetUserPointsCount() + sketch.GetArcCount() + sketch.GetEllipseCount();
            }
            catch (Exception)
            {
                return (sketch.GetSketchPoints2() as object[])?.Length ?? 0;
            }
        }

        /// <summary>The origin, as a point of the origin "sketch" or as the origin feature itself.</summary>
        private static bool IsOrigin(object o) => o switch
        {
            SketchPoint p => p.GetSketch() is Feature owner && owner.GetTypeName2() == "OriginProfileFeature",
            Feature f => f.GetTypeName2() == "OriginProfileFeature",
            _ => false,
        };

        /// <summary>
        /// Curve of a curve driven pattern: sketch segment, whole sketch or model edge – a
        /// straight edge too stays an edge (the pattern runs along it, it is no axis). Curves
        /// of 3D sketches and curve features (composite curve, helix) are not rebuilt.
        /// </summary>
        private GeometryRef RequirePatternCurve(object o, string what)
        {
            if (o is SketchSegment s && s.GetSketch() is Sketch sk && sk.Is3D())
                throw new NotSupportedException($"{what}: Kurven aus 3D-Skizzen werden nicht unterstützt.");
            GeometryRef? g = o is Edge e ? GeometryRef.ToEdge(TopologyCapture.Edge(e)) : ToGeometryRef(o);
            if (g != null && g.Kind != GeometryRefKind.Unknown) return g;
            throw new NotSupportedException($"{what}: Bezug {(g == null ? "fehlt" : g.ToString())} – erwartet Skizzenkurve, Skizze oder Modellkante (Verbund- und Helixkurven werden nicht unterstützt).");
        }

        /// <summary>Pattern direction or axis: edge, axis, sketch line, plane or face – not a dimension.</summary>
        private GeometryRef RequireDirection(object o, string what)
        {
            GeometryRef? g = ToGeometryRef(o);
            if (g != null && g.Kind != GeometryRefKind.Unknown && g.Kind != GeometryRefKind.Sketch) return g;
            throw new NotSupportedException($"{what}: Bezug {(g == null ? "fehlt" : g.ToString())} – Richtungen über Bemaßungen werden nicht unterstützt.");
        }
    }
}
