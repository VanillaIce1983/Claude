using System;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Feature references mirrored as geometry: every kind, reference axes, the direction a reference stands for.</summary>
    public class GeometryRefTests
    {
        // Oblique and off the origin, so no coordinate simply changes sign.
        private static readonly Reflection Oblique = new Reflection(new Plane3(new Vec3(0.01, -0.02, 0.005), new Vec3(1, 2, -0.5)));

        private static readonly FaceSignature PlanarFace = new FaceSignature(SurfaceKind.Plane, 2e-4, new Vec3(0.03, 0.01, 0.02)) { Normal = Vec3.UnitY };
        private static readonly EdgeSignature CircleEdge = new EdgeSignature(CurveKind.Circle, new Vec3(0.024, 0.01, 0), new Vec3(0.016, 0.01, 0), new Vec3(0.02, 0.014, 0), Math.PI * 0.004)
        {
            Center = new Vec3(0.02, 0.01, 0),
            Radius = 0.004,
            Axis = Vec3.UnitZ,
        };
        private static readonly Plane3 SomePlane = new Plane3(new Vec3(0, 0.015, 0), new Vec3(0, 1, 1));
        private static readonly Line3 SomeLine = new Line3(new Vec3(0.02, 0.01, 0), Vec3.UnitZ);
        private static readonly Vec3 SomePoint = new Vec3(0.012, 0.034, -0.008);

        private static GeometryRef Make(GeometryRefKind kind) => kind switch
        {
            GeometryRefKind.Face => GeometryRef.ToFace(PlanarFace),
            GeometryRefKind.Plane => GeometryRef.ToPlane(SomePlane, "Ebene1"),
            GeometryRefKind.Edge => GeometryRef.ToEdge(CircleEdge),
            GeometryRefKind.Vertex => GeometryRef.ToVertex(SomePoint),
            GeometryRefKind.SketchPoint => GeometryRef.ToSketchPoint("Skizze2", "p7", SomePoint),
            GeometryRefKind.SketchSegment => GeometryRef.ToSketchSegment("Skizze2", "L3"),
            GeometryRefKind.Axis => GeometryRef.ToAxis(SomeLine, "Achse1"),
            GeometryRefKind.Sketch => GeometryRef.ToSketch("Skizze3"),
            _ => GeometryRef.Unknown("Silhouettenkante"),
        };

        public static TheoryData<GeometryRefKind> AllKinds()
        {
            var data = new TheoryData<GeometryRefKind>();
            foreach (GeometryRefKind k in Enum.GetValues(typeof(GeometryRefKind))) data.Add(k);
            return data;
        }

        [Theory]
        [MemberData(nameof(AllKinds))]
        public void Every_kind_mirrors_its_geometry_and_keeps_its_names(GeometryRefKind kind)
        {
            GeometryRef g = Make(kind);
            Assert.Equal(kind, g.Kind);

            // Only a plane reference carries a plane and only an axis reference an axis; the
            // others hold an unset one (zero normal) that must not be mirrored.
            GeometryRef m = g.Mirror(Oblique);

            Assert.Equal(kind, m.Kind);
            Assert.Equal(g.SketchName, m.SketchName);
            Assert.Equal(g.EntityId, m.EntityId);
            Assert.Equal(g.Description, m.Description);
            Near(Oblique.Apply(g.Point), m.Point);
            Near(Make(kind).Point, g.Point);   // the original stays as it was

            Assert.Equal(g.Face == null, m.Face == null);
            if (g.Face != null)
            {
                Near(Oblique.Apply(g.Face.Sample), m.Face!.Sample);
                Near(Oblique.ApplyVector(g.Face.Normal), m.Face.Normal);
            }

            Assert.Equal(g.Edge == null, m.Edge == null);
            if (g.Edge != null)
            {
                Near(Oblique.Apply(g.Edge.Start), m.Edge!.Start);
                Near(Oblique.Apply(g.Edge.End), m.Edge.End);
                Near(Oblique.Apply(g.Edge.Mid), m.Edge.Mid);
                Near(Oblique.Apply(g.Edge.Center), m.Edge.Center);
                Near(Oblique.ApplyVector(g.Edge.Axis), m.Edge.Axis);
                Assert.Equal(g.Edge.Radius, m.Edge.Radius);
            }

            if (kind == GeometryRefKind.Plane)
                Assert.True(Oblique.Apply(SomePlane).IsCoplanarWith(m.Plane) && Oblique.ApplyVector(SomePlane.Normal).IsSameDirection(m.Plane.Normal, 1e-12));
            else
                Assert.Equal(g.Plane.Normal, m.Plane.Normal);

            if (kind == GeometryRefKind.Axis)
            {
                Assert.True(Oblique.Apply(SomeLine).IsSameLine(m.Axis));
                Near(Oblique.ApplyVector(SomeLine.Direction), m.Axis.Direction);
            }
            else
                Assert.Equal(g.Axis.Direction, m.Axis.Direction);
        }

        [Theory]
        [MemberData(nameof(AllKinds))]
        public void Mirroring_twice_gives_the_original(GeometryRefKind kind)
        {
            GeometryRef g = Make(kind), back = g.Mirror(Oblique).Mirror(Oblique);
            Near(g.Point, back.Point);
            Near(g.Plane.Origin, back.Plane.Origin);
            Near(g.Plane.Normal, back.Plane.Normal);
            Near(g.Axis.Point, back.Axis.Point);
            Near(g.Axis.Direction, back.Axis.Direction);
            if (g.Face != null)
            {
                Near(g.Face.Sample, back.Face!.Sample);
                Near(g.Face.Normal, back.Face.Normal);
            }
            if (g.Edge != null)
            {
                Near(g.Edge.Start, back.Edge!.Start);
                Near(g.Edge.Mid, back.Edge.Mid);
                Near(g.Edge.Axis, back.Edge.Axis);
            }
        }

        [Fact]
        public void Mirrored_axis_runs_through_the_mirrored_points()
        {
            var rnd = new Random(41);
            for (int i = 0; i < 50; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                var l = new Line3(RandomPoint(rnd), RandomUnit(rnd));
                Line3 m = r.Apply(l);
                foreach (double t in new[] { -0.4, 0.0, 0.3 })
                    Assert.True(m.DistanceTo(r.Apply(l.Point + l.Direction * t)) < 1e-12);
                Near(r.ApplyVector(l.Direction), m.Direction);

                Line3 back = r.Apply(m);
                Assert.True(back.IsSameLine(l));
                Near(l.Direction, back.Direction);
            }
        }

        [Fact]
        public void Axis_perpendicular_to_the_mirror_plane_stays_but_turns_round()
        {
            var r = new Reflection(Plane3.RightYZ);
            var axis = new Line3(new Vec3(0.03, 0.01, -0.02), Vec3.UnitX);
            Line3 m = r.Apply(axis);
            Assert.True(m.IsSameLine(axis));
            Near(-Vec3.UnitX, m.Direction);

            // An axis in the mirror plane keeps its direction.
            var inPlane = new Line3(new Vec3(0, 0.01, 0), new Vec3(0, 1, 1));
            Near(inPlane.Direction, r.Apply(inPlane).Direction);
        }

        [Fact]
        public void Direction_is_the_normal_of_planes_and_planar_faces_and_the_direction_of_axes()
        {
            Near(SomePlane.Normal, GeometryRef.ToPlane(SomePlane, "Ebene1").Direction!.Value);
            Near(Vec3.UnitY, GeometryRef.ToFace(PlanarFace).Direction!.Value);
            Near(Vec3.UnitZ, GeometryRef.ToAxis(SomeLine, "Achse1").Direction!.Value);

            Assert.Null(GeometryRef.ToFace(new FaceSignature(SurfaceKind.Cylinder, 1e-4, new Vec3(0.01, 0, 0))).Direction);
            Assert.Null(GeometryRef.ToEdge(CircleEdge).Direction);   // linear edges are captured as axes
            Assert.Null(GeometryRef.ToVertex(SomePoint).Direction);
            Assert.Null(GeometryRef.ToSketch("Skizze1").Direction);
            Assert.Null(GeometryRef.Unknown("?").Direction);
        }

        [Theory]
        [MemberData(nameof(AllKinds))]
        public void Direction_of_the_mirrored_reference_is_the_mirrored_direction(GeometryRefKind kind)
        {
            GeometryRef g = Make(kind);
            Vec3? d = g.Direction, md = g.Mirror(Oblique).Direction;
            Assert.Equal(d.HasValue, md.HasValue);
            if (d.HasValue) Near(Oblique.ApplyVector(d.Value), md!.Value);
        }
    }
}
