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
    /// References that do not exist in the target (origin off the mirror plane, edges of
    /// features that were not mirrored) are replaced by fixed construction geometry at the
    /// mirrored location; the original relation or dimension then attaches to that stand-in,
    /// so the sketch keeps exactly the degrees of freedom of the original.
    /// One instance per sketch.
    /// </summary>
    internal sealed class SketchBuilder
    {
        private readonly BuildContext _ctx;
        private readonly ModelDoc2 _doc;
        private readonly SketchManager _sm;

        /// <summary>Stand-ins for missing external references, by reference key.</summary>
        private readonly Dictionary<string, object> _substitutes = new Dictionary<string, object>(StringComparer.Ordinal);

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
                CheckConstrainedStatus(map, notes);
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
                    if (!map.Points.TryGetValue(id, out SketchPoint existing))
                        map.Points[id] = got;
                    else if (SwDoc.IdOf(existing) != SwDoc.IdOf(got))
                        merges.Add((existing, got));
                    // Same id: SOLIDWORKS already welded the bit-identical endpoints on creation.
                }
            }

            foreach (SkPoint p in m.FreePoints())
                map.Points[p.Id] = _sm.CreatePoint(p.X, p.Y, 0);

            // Endpoints shared in the original (one SketchPoint, two segments) that came out as
            // two points are welded again so contours stay closed: merge first, coincident if
            // the merge is refused.
            SketchRelationManager rm = map.Sketch.RelationManager;
            foreach (var (keep, other) in merges)
            {
                if (rm.AddRelation(new object[] { keep, other }, (int)swConstraintType_e.swConstraintType_MERGEPOINTS) != null) continue;
                if (rm.AddRelation(new object[] { keep, other }, (int)swConstraintType_e.swConstraintType_COINCIDENT) != null) continue;
                notes.Add($"Gemeinsamer Endpunkt bei ({keep.X * 1000:0.###}|{keep.Y * 1000:0.###}) mm konnte nicht verbunden werden; die Kontur ist eventuell offen.");
            }

            // A merge may remove either of the two points; read the surviving ones back.
            if (merges.Count > 0) RefreshPoints(map);
        }

        private static void RefreshPoints(SketchMap map)
        {
            SketchModel m = map.Mirrored.Sketch;
            foreach (SkSegment seg in m.Segments)
            {
                if (!map.Segments.TryGetValue(seg.Id, out SketchSegment created)) continue;
                List<SketchPoint> pts = PointsOf(created);
                foreach (string id in seg.PointIds())
                {
                    SkPoint want = m.Point(id);
                    SketchPoint? got = pts.OrderBy(p => Dist(p, want)).FirstOrDefault(p => Dist(p, want) <= Tol.Length);
                    if (got != null) map.Points[id] = got;
                }
            }
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
            foreach (SkRelation rel in map.Mirrored.Sketch.Relations)
            {
                if (rel.Kind == RelationKind.ExternalLink)
                {
                    CreateExternalLink(map, rel, notes);
                    continue;
                }

                string what = $"Beziehung {rel.Kind} zu {Describe(rel.Entities)}";
                List<object>? objs = ResolveAll(map, rel.Entities, what, notes);
                if (objs == null) continue;
                if (!AddRelation(map, rm, objs, rel.Kind))
                    notes.Add($"{what} konnte nicht erzeugt werden.");
            }
        }

        /// <summary>
        /// Typed API whenever every entity is a sketch point or segment – also the origin point
        /// and entities of other sketches; selection plus SketchAddConstraints for edges, faces,
        /// planes, or if the typed call is refused.
        /// </summary>
        private bool AddRelation(SketchMap map, SketchRelationManager rm, List<object> objs, RelationKind kind)
        {
            bool sketchOnly = objs.All(o => o is SketchPoint || o is SketchSegment);
            if (sketchOnly && rm.AddRelation(objs.ToArray(), (int)SketchCapture.ToSw(kind)) != null) return true;
            return AddBySelection(map, objs, kind);
        }

        private bool AddBySelection(SketchMap map, List<object> objs, RelationKind kind)
        {
            SketchRelationManager rm = map.Sketch.RelationManager;
            foreach (string cmd in SketchCapture.ToConstraintStrings(kind))
            {
                int before = rm.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
                SwSelect.Clear(_doc);
                bool selected = true;
                for (int i = 0; i < objs.Count; i++) selected &= SwSelect.Select(_doc, objs[i], 0, i > 0);
                if (selected) _doc.SketchAddConstraints(cmd);
                SwSelect.Clear(_doc);
                if (!selected) return false;
                if (rm.GetRelationsCount((int)swSketchRelationFilterType_e.swAll) > before) return true;
            }
            return false;
        }

        /// <summary>
        /// Converted / offset entities ("On Edge"): re-linked to the mirrored edge, or to its
        /// stand-in – collinear for lines, coradial for arcs and circles. Splines and the like
        /// cannot be linked and are fixed in place (with the Fix option).
        /// </summary>
        private void CreateExternalLink(SketchMap map, SkRelation rel, List<string> notes)
        {
            SketchRelationManager rm = map.Sketch.RelationManager;
            SkRef? external = rel.Entities.FirstOrDefault(e => !e.IsLocal);
            foreach (SkRef local in rel.Entities.Where(e => e.Kind == SkRefKind.Segment))
            {
                if (!map.Segments.TryGetValue(local.Id, out SketchSegment seg)) continue;
                SkSegment model = map.Mirrored.Sketch.Segment(local.Id);
                RelationKind? link = model.Kind == SkSegmentKind.Line ? RelationKind.Collinear
                    : model.Kind == SkSegmentKind.Arc || model.Kind == SkSegmentKind.Circle ? RelationKind.Coradial
                    : (RelationKind?)null;

                string what = $"Übernommenes Element {local.Id} (auf Kante)";
                object? target = external == null ? null : ResolveRef(map, external, notes);
                if (target == null && external != null && _ctx.Options.ExternalReferences == ExternalReferenceHandling.Fix)
                    target = Substitute(map, external);
                if (link != null && target != null)
                {
                    if (AddRelation(map, rm, new List<object> { seg, target }, link.Value)) continue;
                    notes.Add($"{what}: {link} zur Kante konnte nicht erzeugt werden.");
                    continue;
                }
                if (_ctx.Options.ExternalReferences == ExternalReferenceHandling.Fix &&
                    rm.AddRelation(new object[] { seg }, (int)swConstraintType_e.swConstraintType_FIXED) != null)
                    notes.Add($"{what}: ohne Bezug zur Kante, an Ort und Stelle fixiert.");
                else
                    notes.Add($"{what}: ohne Bezug zur Kante, frei.");
            }
        }

        /// <summary>
        /// Resolves all entities of a relation or dimension. Missing external references get a
        /// fixed stand-in (Fix) or make the relation/dimension drop out (Drop). Null: skip it.
        /// </summary>
        private List<object>? ResolveAll(SketchMap map, List<SkRef> refs, string what, List<string> notes)
        {
            var result = new List<object>();
            foreach (SkRef r in refs)
            {
                object? o = ResolveRef(map, r, notes);
                if (o == null && !r.IsLocal && _ctx.Options.ExternalReferences == ExternalReferenceHandling.Fix)
                {
                    o = Substitute(map, r);
                    if (o != null) notes.Add($"{what}: {Describe(new[] { r })} fehlt im Ziel, ersetzt durch fixierte Hilfsgeometrie.");
                }
                if (o == null)
                {
                    notes.Add($"{what}: {Describe(new[] { r })} fehlt im Ziel, entfällt.");
                    return null;
                }
                result.Add(o);
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Fixed construction geometry standing in for a missing external reference, projected
        /// into the sketch like SOLIDWORKS projects external entities. Null if the reference
        /// carries no geometry that can be drawn in the sketch.
        /// </summary>
        private object? Substitute(SketchMap map, SkRef r)
        {
            string key = r.Kind + ":" + r.Id + "@" + r.SketchName + ":" + r.Position + ":" + r.Edge;
            if (_substitutes.TryGetValue(key, out object existing)) return existing;

            Frame3 frame = map.Mirrored.Sketch.Frame;
            Vec3 Local(Vec3 p) => frame.ToLocal(frame.Plane.Project(p));
            SketchRelationManager rm = map.Sketch.RelationManager;
            object? created = null;
            using (new SketchInputScope(_sm))
            {
                switch (r.Kind)
                {
                    case SkRefKind.Origin:
                    case SkRefKind.ModelVertex:
                    case SkRefKind.OtherSketchPoint:
                    {
                        Vec3 l = Local(r.Position);
                        SketchPoint p = _sm.CreatePoint(l.X, l.Y, 0);
                        if (p != null) rm.AddRelation(new object[] { p }, (int)swConstraintType_e.swConstraintType_FIXED);
                        created = p;
                        break;
                    }
                    case SkRefKind.ModelEdge when r.Edge!.Kind == Core.Matching.CurveKind.Line:
                    {
                        Vec3 a = Local(r.Edge.Start), b = Local(r.Edge.End);
                        created = Construction(_sm.CreateLine(a.X, a.Y, 0, b.X, b.Y, 0), rm);
                        break;
                    }
                    case SkRefKind.ModelEdge when r.Edge!.Kind == Core.Matching.CurveKind.Circle && r.Edge.Axis.IsParallelTo(frame.ZAxis, 1e-6):
                    {
                        Vec3 c = Local(r.Edge.Center);
                        created = Construction(_sm.CreateCircleByRadius(c.X, c.Y, 0, r.Edge.Radius), rm);
                        break;
                    }
                }
            }
            if (created != null) _substitutes[key] = created;
            return created;
        }

        private static SketchSegment? Construction(SketchSegment? seg, SketchRelationManager rm)
        {
            if (seg == null) return null;
            seg.ConstructionGeometry = true;
            rm.AddRelation(new object[] { seg }, (int)swConstraintType_e.swConstraintType_FIXED);
            // "Fixed" leaves the ends of a line free along it; fix the points too.
            foreach (SketchPoint p in PointsOf(seg))
                rm.AddRelation(new object[] { p }, (int)swConstraintType_e.swConstraintType_FIXED);
            return seg;
        }

        /// <summary>Compares how well the copy is defined with the original.</summary>
        private static void CheckConstrainedStatus(SketchMap map, List<string> notes)
        {
            var status = (swConstrainedStatus_e)map.Sketch.GetConstrainedStatus();
            var original = (swConstrainedStatus_e)map.Source.ConstrainedStatus;
            if (status == swConstrainedStatus_e.swOverConstrained || status == swConstrainedStatus_e.swNoSolution || status == swConstrainedStatus_e.swInvalidSolution)
                notes.Add($"Die gespiegelte Skizze ist {StatusText(status)} – bitte prüfen.");
            else if (original == swConstrainedStatus_e.swFullyConstrained && status == swConstrainedStatus_e.swUnderConstrained)
                notes.Add("Die gespiegelte Skizze ist unterbestimmt, das Original war voll bestimmt.");
        }

        private static string StatusText(swConstrainedStatus_e s) => s switch
        {
            swConstrainedStatus_e.swOverConstrained => "überbestimmt",
            swConstrainedStatus_e.swNoSolution => "nicht lösbar",
            swConstrainedStatus_e.swInvalidSolution => "ungültig gelöst",
            _ => s.ToString(),
        };

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
            foreach (SkDimension dim in map.Mirrored.Sketch.Dimensions)
            {
                string what = $"Bemaßung {dim.Name}";
                List<object>? objs = ResolveAll(map, dim.Refs, what, notes);
                if (objs == null) continue;

                DisplayDimension? dd = PlaceDimension(map, dim, objs);
                if (dd == null)
                {
                    notes.Add($"{what} ({dim.Kind}) konnte nicht so erzeugt werden, dass sie {Format(dim)} misst; entfällt.");
                    continue;
                }

                Dimension d = dd.GetDimension2(0);
                bool isDriven = d.DrivenState == (int)swDimensionDrivenState_e.swDimensionDriven;
                if (dim.Driven && !isDriven)
                {
                    d.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriven;
                }
                else if (!dim.Driven && isDriven)
                {
                    // SOLIDWORKS made it a reference dimension because it would over-define the copy.
                    notes.Add($"{what} ist in der Kopie getrieben (überbestimmend); Gleichungen darauf wirken nicht.");
                }
                else if (!dim.Driven)
                {
                    int status = d.SetSystemValue3(dim.Value, (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations, null);
                    if (status != (int)swSetValueReturnStatus_e.swSetValue_Successful)
                        notes.Add($"{what}: Wert konnte nicht gesetzt werden ({(swSetValueReturnStatus_e)status}).");
                }
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
