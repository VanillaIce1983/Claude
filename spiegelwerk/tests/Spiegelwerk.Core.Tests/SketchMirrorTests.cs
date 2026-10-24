using System;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Sketch;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    public class SketchMirrorTests
    {
        /// <summary>
        /// A "tombstone": bottom line, right line, upper half circle, left line; dimensioned
        /// width, height and radius; horizontal/vertical/tangent relations.
        /// </summary>
        private static SketchModel Tombstone(Frame3 frame)
        {
            var s = new SketchModel { Name = "Skizze1", Frame = frame };
            s.Points.Add(new SkPoint("p1", 0.01, 0.00));
            s.Points.Add(new SkPoint("p2", 0.05, 0.00));
            s.Points.Add(new SkPoint("p3", 0.05, 0.02));
            s.Points.Add(new SkPoint("p5", 0.01, 0.02));
            s.Points.Add(new SkPoint("c", 0.03, 0.02));
            s.Segments.Add(new SkSegment("L1", SkSegmentKind.Line) { StartId = "p1", EndId = "p2" });
            s.Segments.Add(new SkSegment("L2", SkSegmentKind.Line) { StartId = "p2", EndId = "p3" });
            s.Segments.Add(new SkSegment("A1", SkSegmentKind.Arc) { StartId = "p3", EndId = "p5", CenterId = "c", Direction = 1 });
            s.Segments.Add(new SkSegment("L4", SkSegmentKind.Line) { StartId = "p5", EndId = "p1" });
            s.Relations.Add(new SkRelation(RelationKind.Horizontal, new[] { SkRef.LocalSegment("L1") }));
            s.Relations.Add(new SkRelation(RelationKind.Vertical, new[] { SkRef.LocalSegment("L2") }));
            s.Relations.Add(new SkRelation(RelationKind.Vertical, new[] { SkRef.LocalSegment("L4") }));
            s.Relations.Add(new SkRelation(RelationKind.Tangent, new[] { SkRef.LocalSegment("L2"), SkRef.LocalSegment("A1") }));
            s.Relations.Add(new SkRelation(RelationKind.Coincident, new[] { SkRef.Origin(), SkRef.LocalPoint("p1") }));
            s.Dimensions.Add(new SkDimension(DimensionKind.Horizontal, new[] { SkRef.LocalPoint("p1"), SkRef.LocalPoint("p2") }, 0.04)
            {
                Name = "D1",
                TextPosition = frame.ToWorld(0.03, -0.01),
            });
            s.Dimensions.Add(new SkDimension(DimensionKind.Vertical, new[] { SkRef.LocalSegment("L2") }, 0.02) { Name = "D2" });
            s.Dimensions.Add(new SkDimension(DimensionKind.Radial, new[] { SkRef.LocalSegment("A1") }, 0.02) { Name = "D3" });
            return s;
        }

        [Fact]
        public void Front_sketch_mirrored_at_right_plane_negates_x_and_flips_arcs()
        {
            SketchModel src = Tombstone(Front);
            var r = new Reflection(Plane3.RightYZ);
            SketchMirrorResult res = SketchMirror.Mirror(src, r, Front);
            SketchModel m = res.Sketch;

            Assert.Equal(1, res.Map.Sigma);
            Assert.Equal(AxisImage.MinusX, res.Map.X);
            Assert.Equal(AxisImage.PlusY, res.Map.Y);

            SkPoint p1 = m.Point("p1");
            Near(-0.01, p1.X);
            Near(0.00, p1.Y);
            Assert.Equal(-1, m.Segment("A1").Direction);

            // The mirrored arc still bulges upwards: its middle is the mirror image of the original middle.
            SkPoint c = m.Point("c"), s = m.Point("p3"), e = m.Point("p5");
            var mid = ArcMid(c.X, c.Y, s.X, s.Y, e.X, e.Y, m.Segment("A1").Direction);
            Near(-0.03, mid.x);
            Near(0.04, mid.y);

            Assert.Equal(new[] { RelationKind.Horizontal, RelationKind.Vertical, RelationKind.Vertical, RelationKind.Tangent, RelationKind.Coincident },
                m.Relations.Select(x => x.Kind));
            Assert.Equal(new[] { DimensionKind.Horizontal, DimensionKind.Vertical, DimensionKind.Radial }, m.Dimensions.Select(x => x.Kind));
            Assert.Equal(new[] { 0.04, 0.02, 0.02 }, m.Dimensions.Select(x => x.Value));
            Near(new Vec3(-0.03, -0.01, 0), m.Dimensions[0].TextPosition!.Value);
            Near(Vec3.Zero, m.Relations[4].Entities[0].Position);
            Assert.Empty(res.Warnings);
            Assert.True(SketchMirror.MaxDistortion(src, m) < 1e-12);
        }

        [Fact]
        public void Right_sketch_mirrored_at_right_plane_keeps_coordinates_and_arc_sense()
        {
            SketchModel src = Tombstone(Right);
            SketchMirrorResult res = SketchMirror.Mirror(src, new Reflection(Plane3.RightYZ), Right);

            // The plane maps onto itself but its normal turns round: σ = -1.
            Assert.Equal(-1, res.Map.Sigma);
            foreach (SkPoint p in src.Points)
            {
                SkPoint q = res.Sketch.Point(p.Id);
                Near(p.X, q.X);
                Near(p.Y, q.Y);
            }
            Assert.Equal(1, res.Sketch.Segment("A1").Direction);
        }

        [Fact]
        public void Diagonal_mirror_swaps_horizontal_and_vertical()
        {
            SketchModel src = Tombstone(Front);
            var r = new Reflection(new Plane3(Vec3.Zero, new Vec3(1, -1, 0)));
            SketchMirrorResult res = SketchMirror.Mirror(src, r, Front);

            Assert.Equal(AxisImage.PlusY, res.Map.X);
            Assert.Equal(AxisImage.PlusX, res.Map.Y);
            Assert.Equal(new[] { RelationKind.Vertical, RelationKind.Horizontal, RelationKind.Horizontal, RelationKind.Tangent, RelationKind.Coincident },
                res.Sketch.Relations.Select(x => x.Kind));
            Assert.Equal(new[] { DimensionKind.Vertical, DimensionKind.Horizontal, DimensionKind.Radial },
                res.Sketch.Dimensions.Select(x => x.Kind));
            Assert.Empty(res.Warnings);
        }

        [Fact]
        public void Oblique_axes_drop_horizontal_and_vertical_with_a_warning()
        {
            SketchModel src = Tombstone(Front);
            double a = Math.PI / 6;
            var r = new Reflection(new Plane3(Vec3.Zero, new Vec3(Math.Cos(a), Math.Sin(a), 0)));
            SketchMirrorResult res = SketchMirror.Mirror(src, r, Front);

            Assert.Equal(AxisImage.Oblique, res.Map.X);
            Assert.Equal(new[] { RelationKind.Tangent, RelationKind.Coincident }, res.Sketch.Relations.Select(x => x.Kind));
            Assert.Equal(new[] { DimensionKind.Radial }, res.Sketch.Dimensions.Select(x => x.Kind));
            Assert.Equal(5, res.Warnings.Count);
            Assert.True(SketchMirror.MaxDistortion(src, res.Sketch) < 1e-12);
        }

        [Fact]
        public void Only_standalone_points_are_recreated_on_their_own()
        {
            SketchModel src = Tombstone(Front);
            src.Points.Add(new SkPoint("hidden", 0.2, 0.2));
            src.Points.Add(new SkPoint("user", 0.1, 0.1) { Standalone = true });
            SketchModel m = SketchMirror.Mirror(src, new Reflection(Plane3.RightYZ), Front).Sketch;

            Assert.Equal(new[] { "user" }, m.FreePoints().Select(p => p.Id));
            Near(-0.1, m.Point("user").X);
        }

        [Fact]
        public void Target_frame_must_lie_in_the_mirrored_plane()
        {
            SketchModel src = Tombstone(Front);
            Assert.Throws<ArgumentException>(() => SketchMirror.Mirror(src, new Reflection(Plane3.RightYZ), Top));
            var offset = new Frame3(new Vec3(0, 0, 0.001), Vec3.UnitX, Vec3.UnitY);
            Assert.Throws<ArgumentException>(() => SketchMirror.Mirror(src, new Reflection(Plane3.RightYZ), offset));
        }

        [Fact]
        public void Random_planes_and_target_frames_preserve_shape_and_arc_bulge()
        {
            var rnd = new Random(7);
            for (int i = 0; i < 200; i++)
            {
                Frame3 srcFrame = RandomFrame(rnd);
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));

                // Any right-handed frame on the mirrored plane, with either normal orientation.
                Plane3 mirrored = r.Apply(srcFrame.Plane);
                Vec3 zt = rnd.Next(2) == 0 ? mirrored.Normal : -mirrored.Normal;
                double phi = rnd.NextDouble() * 2 * Math.PI;
                Vec3 u = zt.AnyPerpendicular(), v = zt.Cross(u);
                Vec3 xt = u * Math.Cos(phi) + v * Math.Sin(phi);
                var target = new Frame3(mirrored.Project(RandomPoint(rnd)), xt, zt.Cross(xt));

                SketchModel src = Tombstone(srcFrame);
                int dir = rnd.Next(2) == 0 ? 1 : -1;
                src.Segments[2].Direction = dir;
                SketchMirrorResult res = SketchMirror.Mirror(src, r, target);

                Assert.True(SketchMirror.MaxDistortion(src, res.Sketch) < 1e-12);

                SkPoint c = src.Point("c"), s = src.Point("p3"), e = src.Point("p5");
                var mid = ArcMid(c.X, c.Y, s.X, s.Y, e.X, e.Y, dir);
                Vec3 expected = target.ToLocal(r.Apply(srcFrame.ToWorld(mid.x, mid.y)));

                SkPoint mc = res.Sketch.Point("c"), ms = res.Sketch.Point("p3"), me = res.Sketch.Point("p5");
                var mmid = ArcMid(mc.X, mc.Y, ms.X, ms.Y, me.X, me.Y, res.Sketch.Segment("A1").Direction);
                Near(expected.X, mmid.x, 1e-9);
                Near(expected.Y, mmid.y, 1e-9);
            }
        }
    }
}
