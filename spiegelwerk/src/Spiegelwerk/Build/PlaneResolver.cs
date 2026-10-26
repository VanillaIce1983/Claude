using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build
{
    /// <summary>
    /// Provides a reference plane in the target for a given geometric plane, without ever
    /// referencing the original: an existing plane if one coincides, otherwise an offset of a
    /// standard plane, otherwise a plane through three fixed points of a helper 3D sketch.
    /// Reference axes the same way: on a fixed line of a helper 3D sketch; coordinate systems
    /// on a fixed point and two fixed lines of one.
    /// </summary>
    internal sealed class PlaneResolver
    {
        private readonly BuildContext _ctx;
        private readonly List<(Feature feature, Plane3 plane, Feature? anchor)> _known = new List<(Feature, Plane3, Feature?)>();
        private readonly List<(Feature feature, Line3 line, Feature anchor)> _axes = new List<(Feature, Line3, Feature)>();
        private readonly List<(Feature feature, Frame3 frame, Feature anchor)> _coordinateSystems = new List<(Feature, Frame3, Feature)>();
        private readonly List<Plane3> _failedPlanes = new List<Plane3>();
        private readonly List<Line3> _failedAxes = new List<Line3>();
        private readonly List<Frame3> _failedCoordinateSystems = new List<Frame3>();

        /// <summary>Helper planes and axes made on demand for the feature being built (see <see cref="RemoveStepHelpers"/>).</summary>
        private readonly List<Feature> _stepHelpers = new List<Feature>();
        private Feature[]? _standard;
        private int _counter;
        private int _axisCounter;
        private int _anchorCounter;
        private int _coordinateSystemCounter;

        public PlaneResolver(BuildContext ctx)
        {
            _ctx = ctx;
        }

        /// <summary>
        /// Known planes: the standard planes and the planes this job creates – never other
        /// planes already in the document. In the same part those belong to the original, and
        /// a mirrored sketch placed on one of them would depend on the original again.
        /// </summary>
        private void Init()
        {
            if (_standard != null) return;
            _standard = SwDoc.StandardPlanes(_ctx.Doc, _ctx.Math);
            Plane3[] geometry = { Plane3.FrontXY, Plane3.TopXZ, Plane3.RightYZ };
            for (int i = 0; i < 3; i++) _known.Add((_standard[i], geometry[i], null));
        }

        /// <summary>
        /// A plane feature coinciding with <paramref name="wanted"/> (either normal orientation):
        /// a known one, otherwise a new helper. A place where creating one has failed before is
        /// not tried again.
        /// </summary>
        public Feature Resolve(Plane3 wanted)
        {
            Init();
            PlaneMatch m = PlaneClassifier.Classify(wanted, _known.Select(k => k.plane).ToList(), StandardGeometry);
            if (m.Kind == PlaneMatchKind.Existing) return _known[m.Index].feature;
            if (_failedPlanes.Any(p => p.IsCoplanarWith(wanted)))
                throw new InvalidOperationException($"Hilfsebene für {wanted} ließ sich schon zuvor nicht erzeugen.");
            Feature f = Create(wanted, m, $"Spiegelwerk-Ebene {++_counter}");
            _stepHelpers.Add(f);
            return f;
        }

        /// <summary>Only an existing plane, never creates one (for relations to planes).</summary>
        public Feature? FindExisting(Plane3 wanted)
        {
            Init();
            return _known.FirstOrDefault(k => k.plane.IsCoplanarWith(wanted)).feature;
        }

        /// <summary>
        /// Rebuilds a reference plane of the original as an independent plane with the given
        /// name. Null if the mirrored plane is a standard plane: nothing to create.
        /// </summary>
        public Feature? CreateNamed(Plane3 wanted, string name)
        {
            Init();
            PlaneMatch m = PlaneClassifier.Classify(wanted, Array.Empty<Plane3>(), StandardGeometry);
            if (m.Kind == PlaneMatchKind.OffsetFromStandard && Math.Abs(m.Offset) < Tol.Length) return null;
            return Create(wanted, m, name);
        }

        private static readonly Plane3[] StandardGeometry = { Plane3.FrontXY, Plane3.TopXZ, Plane3.RightYZ };

        private Feature Create(Plane3 wanted, PlaneMatch m, string name)
        {
            Feature f;
            Feature? anchor = null;
            try
            {
                f = m.Kind == PlaneMatchKind.OffsetFromStandard
                    ? CreateOffset(wanted, _standard![m.Index], m.Offset)
                    : CreateThreePoint(wanted, out anchor);
            }
            catch (Exception)
            {
                // Every further attempt at this place would end the same way.
                _failedPlanes.Add(wanted);
                throw;
            }
            SwDoc.Rename(_ctx.Doc, f, name);
            _ctx.RegisterHelper(f);
            _known.Add((f, wanted, anchor));
            // A new feature means a rebuild: cached faces and edges are stale now.
            _ctx.Topology.Invalidate();
            return f;
        }

        private Feature CreateOffset(Plane3 wanted, Feature standard, double offset)
        {
            // Which side the offset goes is not documented reliably: build it, measure it,
            // and build it flipped if it landed on the wrong side.
            foreach (bool flip in new[] { offset < 0, offset >= 0 })
            {
                Feature? f = InsertPlane(() => SwSelect.Select(_ctx.Doc, standard, 0, false),
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance |
                    (flip ? (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip : 0),
                    Math.Abs(offset), 0, 0);
                if (f == null) continue;
                if (PlaneOf(f).IsCoplanarWith(wanted)) return f;
                Delete(f);
            }
            throw new InvalidOperationException($"Versatzebene für {wanted} konnte nicht erzeugt werden.");
        }

        /// <summary>Plane through three fixed points of a helper 3D sketch (<paramref name="anchor"/>); a failure leaves neither behind.</summary>
        private Feature CreateThreePoint(Plane3 wanted, out Feature anchor)
        {
            ModelDoc2 doc = _ctx.Doc;
            SketchManager sm = doc.SketchManager;
            Vec3[] pts = PlaneClassifier.ThreePointsOn(wanted);

            SwSelect.Clear(doc);
            sm.Insert3DSketch(true);
            var created = new List<SketchPoint>();
            using (new SketchInputScope(sm))
            {
                foreach (Vec3 p in pts)
                {
                    SketchPoint sp = sm.CreatePoint(p.X, p.Y, p.Z);
                    created.Add(sp);
                    // Fixed, so nothing can drag the plane away later.
                    sm.ActiveSketch.RelationManager.AddRelation(new object[] { sp }, (int)swConstraintType_e.swConstraintType_FIXED);
                }
            }
            sm.Insert3DSketch(true);
            anchor = doc.Extension.GetLastFeatureAdded();
            SwDoc.Rename(doc, anchor, $"Spiegelwerk-Anker {++_anchorCounter}");

            Feature? f = null;
            try
            {
                f = InsertPlane(() =>
                    {
                        bool ok = true;
                        for (int i = 0; i < 3; i++) ok &= SwSelect.Select(doc, created[i], i, i > 0);
                        return ok;
                    },
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Coincident,
                    0,
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Coincident,
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Coincident);
                if (f == null || !PlaneOf(f).IsCoplanarWith(wanted))
                    throw new InvalidOperationException($"Ebene durch drei Punkte für {wanted} konnte nicht erzeugt werden.");
            }
            catch (Exception)
            {
                if (f != null) Delete(f);
                Delete(anchor);
                _ctx.Topology.Invalidate();
                throw;
            }
            _ctx.RegisterHelper(anchor);
            return f;
        }

        // ---------------------------------------------------------------- axes

        /// <summary>An axis of this job on the given line (either direction), if there is one.</summary>
        public Feature? FindAxis(Line3 wanted) => _axes.FirstOrDefault(a => a.line.IsSameLine(wanted)).feature;

        /// <summary>
        /// An axis on the line: one of this job, otherwise a new helper axis. A line where
        /// creating one has failed before is not tried again.
        /// </summary>
        public Feature Axis(Line3 wanted)
        {
            Feature? existing = FindAxis(wanted);
            if (existing != null) return existing;
            if (_failedAxes.Any(l => l.IsSameLine(wanted)))
                throw new InvalidOperationException($"Achse auf {wanted} ließ sich schon zuvor nicht erzeugen.");
            Feature axis = CreateAxis(wanted, $"Spiegelwerk-Achse {++_axisCounter}");
            _stepHelpers.Add(axis);
            return axis;
        }

        /// <summary>Rebuilds a reference axis of the original as an independent axis with the given name.</summary>
        public Feature CreateNamedAxis(Line3 wanted, string name) => CreateAxis(wanted, name);

        /// <summary>
        /// Reference axis on a line of a helper 3D sketch, the line and its ends fixed: like the
        /// three-point planes it depends on nothing else, least of all on the original. If it
        /// cannot be made right, nothing stays behind – neither the axis nor its helper sketch.
        /// </summary>
        private Feature CreateAxis(Line3 wanted, string name)
        {
            ModelDoc2 doc = _ctx.Doc;
            SketchManager sm = doc.SketchManager;
            // A short piece around the point of the line closest to the origin.
            Vec3 mid = wanted.Point - wanted.Direction * wanted.Point.Dot(wanted.Direction);
            Vec3 a = mid - wanted.Direction * 0.05, b = mid + wanted.Direction * 0.05;

            SwSelect.Clear(doc);
            sm.Insert3DSketch(true);
            SketchSegment? line;
            using (new SketchInputScope(sm))
            {
                line = sm.CreateLine(a.X, a.Y, a.Z, b.X, b.Y, b.Z);
                if (line is SketchLine l)
                {
                    SketchRelationManager rm = sm.ActiveSketch.RelationManager;
                    rm.AddRelation(new object[] { line }, (int)swConstraintType_e.swConstraintType_FIXED);
                    rm.AddRelation(new object[] { l.GetStartPoint2() }, (int)swConstraintType_e.swConstraintType_FIXED);
                    rm.AddRelation(new object[] { l.GetEndPoint2() }, (int)swConstraintType_e.swConstraintType_FIXED);
                }
            }
            sm.Insert3DSketch(true);
            Feature anchor = doc.Extension.GetLastFeatureAdded();
            SwDoc.Rename(doc, anchor, $"Spiegelwerk-Anker {++_anchorCounter}");

            Feature? axis = null;
            try
            {
                SwSelect.Clear(doc);
                bool ok = line != null && SwSelect.Select(doc, line, 0, false) && doc.InsertAxis2(true);
                SwSelect.Clear(doc);
                axis = ok ? doc.Extension.GetLastFeatureAdded() : null;
                if (axis == null || !(axis.GetSpecificFeature2() is RefAxis refAxis) || !SwDoc.AxisLine(refAxis).IsSameLine(wanted))
                    throw new InvalidOperationException($"Achse auf {wanted} konnte nicht erzeugt werden.");
            }
            catch (Exception)
            {
                _failedAxes.Add(wanted);
                if (axis != null) Delete(axis);
                Delete(anchor);
                _ctx.Topology.Invalidate();
                throw;
            }
            SwDoc.Rename(doc, axis, name);
            _ctx.RegisterHelper(anchor);
            _ctx.RegisterHelper(axis);
            _axes.Add((axis, wanted, anchor));
            _ctx.Topology.Invalidate();
            return axis;
        }

        // ---------------------------------------------------------------- coordinate systems

        /// <summary>Selection marks of InsertCoordinateSystem: origin, X axis, Y axis (example of the API help).</summary>
        private const int OriginMark = 1, XAxisMark = 2, YAxisMark = 4;

        /// <summary>A coordinate system of this job with the given origin and axes, if there is one.</summary>
        public Feature? FindCoordinateSystem(Frame3 wanted) =>
            _coordinateSystems.FirstOrDefault(c => SameFrame(c.frame, wanted)).feature;

        /// <summary>
        /// A coordinate system with the given origin and axes: one of this job, otherwise a new
        /// helper (<paramref name="created"/>). A place where creating one has failed before is
        /// not tried again – every attempt would leave nothing but another failure.
        /// </summary>
        public Feature CoordinateSystem(Frame3 wanted, out bool created)
        {
            created = false;
            Feature? existing = FindCoordinateSystem(wanted);
            if (existing != null) return existing;
            if (_failedCoordinateSystems.Any(f => SameFrame(f, wanted)))
                throw new InvalidOperationException($"Koordinatensystem {wanted} ließ sich schon zuvor nicht erzeugen.");
            Feature cs = CreateCoordinateSystem(wanted, $"Spiegelwerk-KS {++_coordinateSystemCounter}");
            created = true;
            return cs;
        }

        /// <summary>Rebuilds a coordinate system of the original as an independent one with the given name.</summary>
        public Feature CreateNamedCoordinateSystem(Frame3 wanted, string name) => CreateCoordinateSystem(wanted, name);

        /// <summary>Removes a helper coordinate system this job made, with its helper sketch – for a feature that failed after asking for it.</summary>
        public void RemoveCoordinateSystem(Feature cs)
        {
            int i = _coordinateSystems.FindIndex(c => c.feature.Name == cs.Name);
            if (i < 0) return;
            Feature anchor = _coordinateSystems[i].anchor;
            _coordinateSystems.RemoveAt(i);
            Delete(cs);
            Delete(anchor);
            _ctx.Topology.Invalidate();
        }

        /// <summary>Planes, axes and coordinate systems deleted with a failed step are not offered again.</summary>
        public void Forget(ICollection<string> names)
        {
            _known.RemoveAll(k => names.Contains(k.feature.Name));
            _axes.RemoveAll(a => names.Contains(a.feature.Name));
            _coordinateSystems.RemoveAll(c => names.Contains(c.feature.Name));
            _stepHelpers.RemoveAll(h => names.Contains(h.Name));
        }

        /// <summary>A new feature is about to be built: helper planes and axes made from now on are made for it.</summary>
        public void BeginStep() => _stepHelpers.Clear();

        /// <summary>
        /// Removes the helper planes and axes made for the current feature, each with its helper
        /// sketch – for a feature that came to nothing or that the job removed again. Planes and
        /// axes it found already there were not made for it and stay; so does a helper something
        /// else still builds on.
        /// </summary>
        public void RemoveStepHelpers()
        {
            bool removed = false;
            for (int i = _stepHelpers.Count - 1; i >= 0; i--)
            {
                Feature helper = _stepHelpers[i];
                if (HasDependents(helper)) continue;
                string name = helper.Name;
                Feature? anchor = _known.FirstOrDefault(k => k.feature.Name == name).anchor ?? _axes.FirstOrDefault(a => a.feature.Name == name).anchor;
                _known.RemoveAll(k => k.feature.Name == name);
                _axes.RemoveAll(a => a.feature.Name == name);
                Delete(helper);
                if (anchor != null) Delete(anchor);
                removed = true;
            }
            _stepHelpers.Clear();
            if (removed) _ctx.Topology.Invalidate();
        }

        /// <summary>Whether something still builds on the feature: a sketch on the plane, a feature using it as a reference …</summary>
        private static bool HasDependents(Feature f) =>
            f.GetChildren() is object[] children && children.OfType<Feature>().Any(c => !SwDoc.IsSystemFeature(c));

        public static bool SameFrame(Frame3 a, Frame3 b) =>
            a.Origin.AlmostEquals(b.Origin) && a.XAxis.IsSameDirection(b.XAxis, 1e-6) && a.YAxis.IsSameDirection(b.YAxis, 1e-6);

        /// <summary>
        /// Coordinate system on a helper 3D sketch: a fixed line from the origin along X and
        /// one along Y, their common start point the origin – like helper planes and axes it
        /// depends on nothing else. Whether SOLIDWORKS takes a line's direction from its start
        /// to its end is not documented: the frame is read back, and an axis that came out
        /// reversed is flipped (XFlipped/YFlipped). If it cannot be made right, nothing stays
        /// behind – neither the coordinate system nor its helper sketch.
        /// </summary>
        private Feature CreateCoordinateSystem(Frame3 wanted, string name)
        {
            ModelDoc2 doc = _ctx.Doc;
            SketchManager sm = doc.SketchManager;
            Vec3 o = wanted.Origin, x = o + wanted.XAxis * 0.05, y = o + wanted.YAxis * 0.05;

            SwSelect.Clear(doc);
            sm.Insert3DSketch(true);
            SketchLine? xLine, yLine;
            using (new SketchInputScope(sm))
            {
                xLine = sm.CreateLine(o.X, o.Y, o.Z, x.X, x.Y, x.Z) as SketchLine;
                yLine = sm.CreateLine(o.X, o.Y, o.Z, y.X, y.Y, y.Z) as SketchLine;
                SketchRelationManager rm = sm.ActiveSketch.RelationManager;
                foreach (SketchLine? l in new[] { xLine, yLine })
                {
                    if (l == null) continue;
                    rm.AddRelation(new object[] { l }, (int)swConstraintType_e.swConstraintType_FIXED);
                    rm.AddRelation(new object[] { l.GetStartPoint2() }, (int)swConstraintType_e.swConstraintType_FIXED);
                    rm.AddRelation(new object[] { l.GetEndPoint2() }, (int)swConstraintType_e.swConstraintType_FIXED);
                }
            }
            sm.Insert3DSketch(true);
            Feature anchor = doc.Extension.GetLastFeatureAdded();
            SwDoc.Rename(doc, anchor, $"Spiegelwerk-Anker {++_anchorCounter}");

            Feature? cs = null;
            try
            {
                if (xLine == null || yLine == null || !(xLine.GetStartPoint2() is SketchPoint origin))
                    throw new InvalidOperationException($"Hilfsskizze für das Koordinatensystem {wanted} konnte nicht erzeugt werden.");

                cs = InsertCoordinateSystem(origin, xLine, yLine);
                if (cs == null) throw new InvalidOperationException($"Koordinatensystem {wanted} konnte nicht erzeugt werden.");
                SwDoc.Rename(doc, cs, name);

                Frame3? actual = SwDoc.CoordinateSystemFrame(doc, _ctx.Math, cs.Name);
                if (actual.HasValue && !SameFrame(actual.Value, wanted))
                {
                    bool flipX = actual.Value.XAxis.IsSameDirection(-wanted.XAxis, 1e-6);
                    bool flipY = actual.Value.YAxis.IsSameDirection(-wanted.YAxis, 1e-6);
                    if (flipX || flipY)
                        Builders.FeatureEdit.CoordinateSystem(doc, cs, d =>
                        {
                            if (flipX) d.XFlipped = !d.XFlipped;
                            if (flipY) d.YFlipped = !d.YFlipped;
                        });
                    actual = SwDoc.CoordinateSystemFrame(doc, _ctx.Math, cs.Name);
                }
                if (!actual.HasValue || !SameFrame(actual.Value, wanted))
                    throw new InvalidOperationException($"Koordinatensystem {wanted} lag nach dem Erzeugen falsch ({actual?.ToString() ?? "nicht lesbar"}).");
            }
            catch (Exception)
            {
                if (cs != null) Delete(cs);
                Delete(anchor);
                _failedCoordinateSystems.Add(wanted);
                _ctx.Topology.Invalidate();
                throw;
            }
            _ctx.RegisterHelper(anchor);
            _ctx.RegisterHelper(cs!);
            _coordinateSystems.Add((cs!, wanted, anchor));
            _ctx.Topology.Invalidate();
            return cs!;
        }

        /// <summary>InsertCoordinateSystem with the marks of the API help; CreateCoordinateSystem with the entities as fallback.</summary>
        private Feature? InsertCoordinateSystem(SketchPoint origin, SketchLine xLine, SketchLine yLine)
        {
            ModelDoc2 doc = _ctx.Doc;
            SwSelect.Clear(doc);
            try
            {
                if (SwSelect.Select(doc, origin, OriginMark, false) &&
                    SwSelect.Select(doc, xLine, XAxisMark, true) &&
                    SwSelect.Select(doc, yLine, YAxisMark, true) &&
                    doc.FeatureManager.InsertCoordinateSystem(false, false, false) is Feature cs)
                    return cs;
            }
            catch (Exception)
            {
                // Refused: the entity route follows.
            }
            finally
            {
                SwSelect.Clear(doc);
            }
            try
            {
                return doc.FeatureManager.CreateCoordinateSystem(origin, new object[] { xLine }, new object[] { yLine }, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private Feature? InsertPlane(Func<bool> select, int c1, double v1, int c2, int c3)
        {
            ModelDoc2 doc = _ctx.Doc;
            SwSelect.Clear(doc);
            if (!select()) return null;
            object plane = doc.FeatureManager.InsertRefPlane(c1, v1, c2, 0, c3, 0);
            SwSelect.Clear(doc);
            return plane == null ? null : doc.Extension.GetLastFeatureAdded();
        }

        private Plane3 PlaneOf(Feature f) => _ctx.Math.PlaneOf((RefPlane)f.GetSpecificFeature2());

        private void Delete(Feature f)
        {
            SwSelect.Select(_ctx.Doc, f, 0, false);
            _ctx.Doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
        }
    }
}
