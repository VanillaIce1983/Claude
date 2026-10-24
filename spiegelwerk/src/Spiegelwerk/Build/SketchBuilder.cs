using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build
{
    /// <summary>
    /// Writes a mirrored sketch into the target:
    ///  1. open a sketch on the mirrored plane and read the frame SOLIDWORKS gives it,
    ///  2. mirror the neutral model into that frame (Core: SketchMirror),
    ///  3. create geometry literally (AddToDB), merge shared endpoints,
    ///  4. add relations, 5. add dimensions and check every dimension measures what the
    ///     original measured before setting its value – a dimension that picked the wrong
    ///     angle or side is removed instead of being allowed to distort the sketch.
    /// </summary>
    internal sealed class SketchBuilder
    {
        private readonly BuildContext _ctx;
        private readonly ModelDoc2 _doc;
        private readonly SketchManager _sm;

        public SketchBuilder(BuildContext ctx)
        {
            _ctx = ctx;
            _doc = ctx.Doc;
            _sm = ctx.Doc.SketchManager;
        }

        public SketchMap Build(SketchRecord rec, List<string> notes)
        {
            SketchModel src = rec.Sketch;
            Feature plane = _ctx.Planes.Resolve(_ctx.Mirror.Apply(src.Frame.Plane));

            SwSelect.Clear(_doc);
            SwSelect.Select(_doc, plane, 0, false);
            _sm.InsertSketch(true);
            Sketch sketch = _sm.ActiveSketch ?? throw new InvalidOperationException("Skizze konnte nicht geöffnet werden.");

            SketchMap map;
            try
            {
                Frame3 target = _ctx.Math.SketchFrame(sketch);
                SketchMirrorResult res = SketchMirror.Mirror(src, _ctx.Mirror, target);
                notes.AddRange(res.Warnings);
                notes.AddRange(res.Sketch.Warnings);
                map = new SketchMap((Feature)sketch, sketch, src, res);

                using (new SketchInputScope(_sm))
                {
                    CreateGeometry(map, notes);
                    CreateRelations(map, notes);
                }
                CreateDimensions(map, notes);
            }
            catch
            {
                // Leave no half-written sketch behind: close it and delete it.
                SwSelect.Clear(_doc);
                _sm.InsertSketch(true);
                if (_doc.Extension.GetLastFeatureAdded() is Feature broken && SwDoc.IsSketch(broken))
                {
                    SwSelect.Select(_doc, broken, 0, false);
                    _doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                }
                throw;
            }

            SwSelect.Clear(_doc);
            _sm.InsertSketch(true);
            _ctx.Sketches[src.Name] = map;
            return map;
        }

        // ---------------------------------------------------------------- geometry

        private void CreateGeometry(SketchMap map, List<string> notes)
        {
            SketchModel m = map.Mirrored.Sketch;
            var merges = new List<(SketchPoint keep, SketchPoint other)>();

            foreach (SkSegment seg in m.Segments)
            {
                SketchSegment? created = CreateSegment(m, seg);
                if (created == null)
                {
                    notes.Add($"Skizzenelement {seg} konnte nicht erzeugt werden.");
                    continue;
                }
                created.ConstructionGeometry = seg.Construction;
                map.Segments[seg.Id] = created;

                List<SketchPoint> newPoints = PointsOf(created);
                foreach (string id in seg.PointIds().Distinct())
                {
                    SkPoint want = m.Point(id);
                    SketchPoint? got = newPoints
                        .OrderBy(p => Dist(p, want))
                        .FirstOrDefault(p => Dist(p, want) <= Tol.Length);
                    if (got == null)
                    {
                        notes.Add($"Punkt {id} von {seg} liegt nicht an der erwarteten Stelle.");
                        continue;
                    }
                    if (map.Points.TryGetValue(id, out SketchPoint existing))
                        merges.Add((existing, got));
                    else
                        map.Points[id] = got;
                }
            }

            foreach (SkPoint p in m.FreePoints())
                map.Points[p.Id] = _sm.CreatePoint(p.X, p.Y, 0);

            // Endpoints shared in the original (one SketchPoint, two segments) are separate
            // points after AddToDB creation; a coincident relation joins them again so
            // contours stay closed.
            SketchRelationManager rm = map.Sketch.RelationManager;
            foreach (var (keep, other) in merges)
                if (rm.AddRelation(new object[] { keep, other }, (int)swConstraintType_e.swConstraintType_COINCIDENT) == null)
                    notes.Add("Gemeinsamer Endpunkt konnte nicht verbunden werden; die Kontur ist eventuell offen.");
        }

        private SketchSegment? CreateSegment(SketchModel m, SkSegment s)
        {
            SkPoint P(string? id) => m.Point(id!);
            switch (s.Kind)
            {
                case SkSegmentKind.Line:
                {
                    SkPoint a = P(s.StartId), b = P(s.EndId);
                    return _sm.CreateLine(a.X, a.Y, 0, b.X, b.Y, 0);
                }
                case SkSegmentKind.Circle:
                {
                    SkPoint c = P(s.CenterId);
                    return _sm.CreateCircleByRadius(c.X, c.Y, 0, s.Radius);
                }
                case SkSegmentKind.Arc:
                {
                    SkPoint c = P(s.CenterId), a = P(s.StartId), b = P(s.EndId);
                    return _sm.CreateArc(c.X, c.Y, 0, a.X, a.Y, 0, b.X, b.Y, 0, (short)s.Direction);
                }
                case SkSegmentKind.Ellipse:
                {
                    SkPoint c = P(s.CenterId), ma = P(s.MajorId), mi = P(s.MinorId);
                    return _sm.CreateEllipse(c.X, c.Y, 0, ma.X, ma.Y, 0, mi.X, mi.Y, 0);
                }
                case SkSegmentKind.EllipticalArc:
                {
                    SkPoint c = P(s.CenterId), ma = P(s.MajorId), mi = P(s.MinorId), a = P(s.StartId), b = P(s.EndId);
                    return _sm.CreateEllipticalArc(c.X, c.Y, 0, ma.X, ma.Y, 0, mi.X, mi.Y, 0, a.X, a.Y, 0, b.X, b.Y, 0, (short)s.Direction);
                }
                case SkSegmentKind.Spline:
                {
                    double[] pts = s.FitPointIds.Select(P).SelectMany(p => new[] { p.X, p.Y, 0.0 }).ToArray();
                    return _sm.CreateSpline2(pts, true);
                }
                case SkSegmentKind.Parabola:
                {
                    SkPoint f = P(s.CenterId), apex = P(s.MajorId), a = P(s.StartId), b = P(s.EndId);
                    return _sm.CreateParabola(f.X, f.Y, 0, apex.X, apex.Y, 0, a.X, a.Y, 0, b.X, b.Y, 0);
                }
                default:
                    return null;
            }
        }

        /// <summary>All sketch points a created segment owns (ends, centre, axis points, fit points).</summary>
        private static List<SketchPoint> PointsOf(SketchSegment seg)
        {
            var result = new List<object>();
            switch (seg)
            {
                case SketchLine l:
                    result.Add(l.GetStartPoint2());
                    result.Add(l.GetEndPoint2());
                    break;
                case SketchArc a:
                    result.Add(a.GetStartPoint2());
                    result.Add(a.GetEndPoint2());
                    result.Add(a.GetCenterPoint2());
                    break;
                case SketchEllipse e:
                    result.Add(e.GetStartPoint2());
                    result.Add(e.GetEndPoint2());
                    result.Add(e.GetCenterPoint2());
                    result.Add(e.GetMajorPoint2());
                    result.Add(e.GetMinorPoint2());
                    break;
                case SketchSpline sp:
                    if (sp.GetPoints2() is object[] fit) result.AddRange(fit);
                    break;
                case SketchParabola p:
                    result.Add(p.GetStartPoint2());
                    result.Add(p.GetEndPoint2());
                    result.Add(p.GetFocalPoint2());
                    result.Add(p.GetApexPoint2());
                    break;
            }
            return result.OfType<SketchPoint>().ToList();
        }

        private static double Dist(SketchPoint p, SkPoint want) => Math.Sqrt((p.X - want.X) * (p.X - want.X) + (p.Y - want.Y) * (p.Y - want.Y));

        // ---------------------------------------------------------------- relations

        private void CreateRelations(SketchMap map, List<string> notes)
        {
            SketchRelationManager rm = map.Sketch.RelationManager;
            var fixedAlready = new HashSet<object>();

            foreach (SkRelation rel in map.Mirrored.Sketch.Relations)
            {
                List<object?> objs = rel.Entities.Select(e => ResolveRef(map, e, notes)).ToList();
                if (objs.Any(o => o == null))
                {
                    Fallback(map, rel.Entities, objs, $"Beziehung {rel.Kind} zu {Describe(rel.Entities)}", fixedAlready, notes);
                    continue;
                }

                // Typed API first; it accepts sketch entities of this sketch.
                if (!rel.HasExternal && rm.AddRelation(objs.ToArray(), (int)SketchCapture.ToSw(rel.Kind)) != null) continue;

                // Relations to external entities (origin, edges, other sketches) go through the
                // selection-based command, the same way the UI creates them.
                if (!AddBySelection(map, objs!, rel.Kind))
                    notes.Add($"Beziehung {rel.Kind} zu {Describe(rel.Entities)} konnte nicht erzeugt werden.");
            }
        }

        private bool AddBySelection(SketchMap map, List<object> objs, RelationKind kind)
        {
            string? cmd = SketchCapture.ToConstraintString(kind);
            if (cmd == null) return false;
            SketchRelationManager rm = map.Sketch.RelationManager;
            int before = rm.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
            SwSelect.Clear(_doc);
            for (int i = 0; i < objs.Count; i++)
                if (!SwSelect.Select(_doc, objs[i], 0, i > 0)) return false;
            _doc.SketchAddConstraints(cmd);
            SwSelect.Clear(_doc);
            return rm.GetRelationsCount((int)swSketchRelationFilterType_e.swAll) > before;
        }

        /// <summary>
        /// The relation or dimension refers to something that does not exist in the target.
        /// "Fix" keeps the local entities where they are (fully defined, but no longer driven
        /// by the reference); "Drop" leaves them free.
        /// </summary>
        private void Fallback(SketchMap map, List<SkRef> refs, List<object?> resolved, string what, HashSet<object> fixedAlready, List<string> notes)
        {
            if (_ctx.Options.ExternalReferences == ExternalReferenceHandling.Drop)
            {
                notes.Add($"{what}: Bezug fehlt im Ziel, entfällt.");
                return;
            }
            SketchRelationManager rm = map.Sketch.RelationManager;
            for (int i = 0; i < refs.Count; i++)
            {
                if (!refs[i].IsLocal || resolved[i] == null || !fixedAlready.Add(resolved[i]!)) continue;
                rm.AddRelation(new[] { resolved[i]! }, (int)swConstraintType_e.swConstraintType_FIXED);
            }
            notes.Add($"{what}: Bezug fehlt im Ziel, die Skizzenelemente wurden fixiert.");
        }

        /// <summary>Resolves a (mirrored) reference to an object of the target document.</summary>
        private object? ResolveRef(SketchMap map, SkRef r, List<string> notes)
        {
            switch (r.Kind)
            {
                case SkRefKind.Point:
                    return map.Points.TryGetValue(r.Id, out SketchPoint p) ? p : null;
                case SkRefKind.Segment:
                    return map.Segments.TryGetValue(r.Id, out SketchSegment s) ? s : null;
                case SkRefKind.Origin:
                    // The mirrored origin is the target origin only if the mirror plane passes through it.
                    return r.Position.AlmostEquals(Vec3.Zero) ? SwDoc.OriginPoint(_doc) : null;
                case SkRefKind.OtherSketchPoint:
                    return _ctx.Sketches.TryGetValue(r.SketchName, out SketchMap op) && op.Points.TryGetValue(r.Id, out SketchPoint opp) ? opp : null;
                case SkRefKind.OtherSketchSegment:
                    return _ctx.Sketches.TryGetValue(r.SketchName, out SketchMap os) && os.Segments.TryGetValue(r.Id, out SketchSegment oss) ? oss : null;
                case SkRefKind.ModelEdge:
                    return _ctx.Topology.FindEdge(r.Edge!, notes);
                case SkRefKind.ModelVertex:
                    return _ctx.Topology.FindVertex(r.Position);
                case SkRefKind.Plane:
                    return _ctx.Planes.FindExisting(r.PlaneGeometry);
                default:
                    return null;
            }
        }

        private static string Describe(IEnumerable<SkRef> refs) => string.Join(", ", refs);

        // ---------------------------------------------------------------- dimensions

        private void CreateDimensions(SketchMap map, List<string> notes)
        {
            var fixedAlready = new HashSet<object>();
            foreach (SkDimension dim in map.Mirrored.Sketch.Dimensions)
            {
                List<object?> objs = dim.Refs.Select(e => ResolveRef(map, e, notes)).ToList();
                string what = $"Bemaßung {dim.Name}";
                if (objs.Count == 0 || objs.Any(o => o == null))
                {
                    Fallback(map, dim.Refs, objs, what, fixedAlready, notes);
                    continue;
                }

                DisplayDimension? dd = PlaceDimension(map, dim, objs!);
                if (dd == null)
                {
                    notes.Add($"{what} ({dim.Kind}) konnte nicht so erzeugt werden, dass sie {Format(dim)} misst; entfällt.");
                    continue;
                }

                Dimension d = dd.GetDimension2(0);
                if (dim.Driven)
                    d.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriven;
                else
                    d.SetSystemValue3(dim.Value, (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations, null);
                TryRename(d, dim.Name);
            }
        }

        /// <summary>
        /// Places a dimension and checks what it measures. The text position decides which of
        /// the possible angles an angular dimension shows; the mirrored position normally picks
        /// the right one. The API documentation does not say whether the position is read in
        /// model or sketch coordinates, so both are tried and the result is measured.
        /// </summary>
        private DisplayDimension? PlaceDimension(SketchMap map, SkDimension dim, List<object> objs)
        {
            Frame3 frame = map.Mirrored.Sketch.Frame;
            Vec3 model = dim.TextPosition ?? DefaultTextPosition(map, dim);
            Vec3 local = frame.ToLocal(frame.Plane.Project(model));
            Vec3[] positions = { model, new Vec3(local.X, local.Y, 0) };

            foreach (Vec3 pos in positions)
            {
                SwSelect.Clear(_doc);
                bool selected = true;
                for (int i = 0; i < objs.Count; i++) selected &= SwSelect.Select(_doc, objs[i], 0, i > 0);
                if (!selected) return null;

                object created = dim.Kind switch
                {
                    DimensionKind.Horizontal => _doc.AddHorizontalDimension2(pos.X, pos.Y, pos.Z),
                    DimensionKind.Vertical => _doc.AddVerticalDimension2(pos.X, pos.Y, pos.Z),
                    DimensionKind.Radial => _doc.AddRadialDimension2(pos.X, pos.Y, pos.Z),
                    DimensionKind.Diameter => _doc.AddDiameterDimension2(pos.X, pos.Y, pos.Z),
                    _ => _doc.AddDimension2(pos.X, pos.Y, pos.Z),
                };
                SwSelect.Clear(_doc);
                if (!(created is DisplayDimension dd)) continue;

                double measured = dd.GetDimension2(0).SystemValue;
                if (Matches(dim, measured)) return dd;
                DeleteDimension(dd);
            }
            return null;
        }

        private static bool Matches(SkDimension dim, double measured)
        {
            double tol = dim.Kind == DimensionKind.Angular ? 1e-6 : Math.Max(1e-7, Math.Abs(dim.Value) * 1e-6);
            return Math.Abs(measured - dim.Value) <= tol;
        }

        private Vec3 DefaultTextPosition(SketchMap map, SkDimension dim)
        {
            var pts = dim.Refs.Where(r => r.Kind == SkRefKind.Point).Select(r => map.TargetModelPoint(r.Id)).ToList();
            Vec3 c = pts.Count > 0 ? pts.Aggregate(Vec3.Zero, (a, b) => a + b) / pts.Count : map.Mirrored.Sketch.Frame.Origin;
            return c + map.Mirrored.Sketch.Frame.YAxis * 0.005;
        }

        private void DeleteDimension(DisplayDimension dd)
        {
            SwSelect.Clear(_doc);
            ((Annotation)dd.GetAnnotation()).Select3(false, null);
            _doc.EditDelete();
            SwSelect.Clear(_doc);
        }

        private static void TryRename(Dimension d, string name)
        {
            if (string.IsNullOrEmpty(name) || d.Name == name) return;
            try
            {
                d.Name = name;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Name already taken in this sketch; keep the generated one.
            }
        }

        private static string Format(SkDimension d) =>
            d.Kind == DimensionKind.Angular ? $"{d.Value * 180 / Math.PI:0.###}°" : $"{d.Value * 1000:0.###} mm";
    }
}
