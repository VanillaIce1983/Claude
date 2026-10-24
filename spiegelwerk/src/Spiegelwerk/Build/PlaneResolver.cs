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
    /// </summary>
    internal sealed class PlaneResolver
    {
        private readonly BuildContext _ctx;
        private readonly List<(Feature feature, Plane3 plane)> _known = new List<(Feature, Plane3)>();
        private Feature[]? _standard;
        private int _counter;

        public PlaneResolver(BuildContext ctx)
        {
            _ctx = ctx;
        }

        private void Init()
        {
            if (_standard != null) return;
            _standard = SwDoc.StandardPlanes(_ctx.Doc, _ctx.Math);
            _known.AddRange(SwDoc.RefPlanes(_ctx.Doc, _ctx.Math));
        }

        /// <summary>A plane feature coinciding with <paramref name="wanted"/> (either normal orientation).</summary>
        public Feature Resolve(Plane3 wanted)
        {
            Init();
            PlaneMatch m = PlaneClassifier.Classify(wanted, _known.Select(k => k.plane).ToList(), StandardGeometry);
            if (m.Kind == PlaneMatchKind.Existing) return _known[m.Index].feature;
            return Create(wanted, m, $"Spiegelwerk-Ebene {++_counter}");
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
            Feature f = m.Kind == PlaneMatchKind.OffsetFromStandard
                ? CreateOffset(wanted, _standard![m.Index], m.Offset)
                : CreateThreePoint(wanted);
            SwDoc.Rename(_ctx.Doc, f, name);
            _ctx.RegisterHelper(f);
            _known.Add((f, wanted));
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

        private Feature CreateThreePoint(Plane3 wanted)
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
            Feature anchor = doc.Extension.GetLastFeatureAdded();
            SwDoc.Rename(doc, anchor, $"Spiegelwerk-Anker {_counter}");
            _ctx.RegisterHelper(anchor);

            Feature? f = InsertPlane(() =>
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
            return f;
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
