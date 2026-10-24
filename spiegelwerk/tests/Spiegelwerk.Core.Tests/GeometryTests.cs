using System;
using Spiegelwerk.Core.Geometry;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    public class GeometryTests
    {
        [Fact]
        public void Reflection_keeps_points_on_the_plane_and_is_its_own_inverse()
        {
            var rnd = new Random(1);
            for (int i = 0; i < 50; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Vec3 onPlane = r.Plane.Project(RandomPoint(rnd));
                Near(onPlane, r.Apply(onPlane));

                Vec3 p = RandomPoint(rnd);
                Near(p, r.Apply(r.Apply(p)));
                Near(r.Plane.SignedDistance(p), -r.Plane.SignedDistance(r.Apply(p)));
                Near(-r.Plane.Normal, r.ApplyVector(r.Plane.Normal));
            }
        }

        [Fact]
        public void Reflection_reverses_orientation()
        {
            var rnd = new Random(2);
            for (int i = 0; i < 50; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Vec3 a = RandomUnit(rnd), b = RandomUnit(rnd);
                Near(-r.ApplyVector(a.Cross(b)), r.ApplyVector(a).Cross(r.ApplyVector(b)));
                Near(-1, r.ToAffine().Determinant);
            }
        }

        [Fact]
        public void Mirrored_frame_keeping_xy_has_reversed_normal()
        {
            var rnd = new Random(3);
            Frame3 f = RandomFrame(rnd);
            var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
            Frame3 m = r.ApplyKeepingXY(f);
            Near(-r.ApplyVector(f.ZAxis), m.ZAxis);
        }

        [Fact]
        public void Frame_round_trip()
        {
            var rnd = new Random(4);
            for (int i = 0; i < 20; i++)
            {
                Frame3 f = RandomFrame(rnd);
                Vec3 p = RandomPoint(rnd);
                Near(p, f.ToWorld(f.ToLocal(p)));
                Near(p, f.VectorToWorld(f.VectorToLocal(p)));
            }
        }

        [Fact]
        public void Standard_sketch_frames_have_the_solidworks_normals()
        {
            Near(Vec3.UnitZ, Front.ZAxis);
            Near(Vec3.UnitY, Top.ZAxis);
            Near(Vec3.UnitX, Right.ZAxis);
        }

        [Fact]
        public void Affine_inverse_compose_and_sw_array()
        {
            var rnd = new Random(5);
            for (int i = 0; i < 20; i++)
            {
                Affine t = Affine.FromFrame(RandomFrame(rnd));
                Affine reflect = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd))).ToAffine();
                Affine both = t.Compose(reflect);
                Vec3 p = RandomPoint(rnd);
                Near(p, both.Inverse().Apply(both.Apply(p)));
                Near(t.Apply(reflect.Apply(p)), both.Apply(p));

                Affine back = Affine.FromSwArray(t.ToSwArray());
                Near(t.Apply(p), back.Apply(p));
                Assert.True(t.IsProperRigid());
                Assert.False(both.IsProperRigid());
            }
        }

        [Fact]
        public void Line_comparison_ignores_direction()
        {
            var a = new Line3(new Vec3(0, 0, 0), Vec3.UnitX);
            var b = new Line3(new Vec3(0.5, 0, 0), -Vec3.UnitX);
            Assert.True(a.IsSameLine(b));
            Assert.False(a.IsSameLine(new Line3(new Vec3(0, 1e-3, 0), Vec3.UnitX)));
        }
    }
}
