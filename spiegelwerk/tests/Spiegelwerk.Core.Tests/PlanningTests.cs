using System;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    public class PlanningTests
    {
        private static readonly Plane3[] Standard = { Plane3.FrontXY, Plane3.TopXZ, Plane3.RightYZ };

        [Fact]
        public void Extrude_direction_follows_the_mirror_for_every_plane_combination()
        {
            var rnd = new Random(11);
            for (int i = 0; i < 500; i++)
            {
                Frame3 src = RandomFrame(rnd);
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Vec3 zt = rnd.Next(2) == 0 ? r.ApplyVector(src.ZAxis) : -r.ApplyVector(src.ZAxis);
                bool isCut = rnd.Next(2) == 0, reverse = rnd.Next(2) == 0;

                int sigma = DirectionRules.Sigma(src.ZAxis, r, zt);
                Vec3 wanted = r.ApplyVector(DirectionRules.ExtrudeDirection(src.ZAxis, isCut, reverse));
                Vec3 built = DirectionRules.ExtrudeDirection(zt, isCut, DirectionRules.ReverseFlag(reverse, sigma));
                Near(wanted, built);
            }
        }

        [Fact]
        public void Coincident_planes_are_reused_whatever_their_normal()
        {
            PlaneMatch m = PlaneClassifier.Classify(new Plane3(new Vec3(0.3, 0.1, 0), -Vec3.UnitZ), Standard, Standard);
            Assert.Equal(PlaneMatchKind.Existing, m.Kind);
            Assert.Equal(0, m.Index);
        }

        [Fact]
        public void Parallel_planes_become_signed_offsets_of_a_standard_plane()
        {
            PlaneMatch m = PlaneClassifier.Classify(new Plane3(new Vec3(-0.05, 0.2, 0.3), Vec3.UnitX), Standard, Standard);
            Assert.Equal(PlaneMatchKind.OffsetFromStandard, m.Kind);
            Assert.Equal(2, m.Index);
            Near(-0.05, m.Offset);
        }

        [Fact]
        public void Oblique_planes_need_three_points_that_keep_the_normal()
        {
            var p = new Plane3(new Vec3(0.01, 0.02, 0.03), new Vec3(1, 2, 3));
            Assert.Equal(PlaneMatchKind.ThreePoints, PlaneClassifier.Classify(p, Standard, Standard).Kind);

            Vec3[] pts = PlaneClassifier.ThreePointsOn(p);
            foreach (Vec3 q in pts) Assert.True(p.Contains(q, 1e-12));
            Vec3 n = (pts[1] - pts[0]).Cross(pts[2] - pts[0]).Normalized();
            Near(p.Normal, n);
        }

        [Fact]
        public void Component_placement_puts_the_mirrored_part_where_the_mirror_image_is()
        {
            var rnd = new Random(13);
            for (int i = 0; i < 100; i++)
            {
                Affine t = Affine.FromFrame(RandomFrame(rnd));
                var asmPlane = new Plane3(RandomPoint(rnd), RandomUnit(rnd));
                var rAsm = new Reflection(asmPlane);
                var rPart = new Reflection(ComponentPlacement.ChoosePartMirrorPlane(t, asmPlane));

                Affine placed = ComponentPlacement.MirroredPlacement(t, rAsm, rPart);
                Assert.True(placed.IsProperRigid(1e-9));
                for (int k = 0; k < 5; k++)
                {
                    Vec3 x = RandomPoint(rnd);
                    Near(rAsm.Apply(t.Apply(x)), placed.Apply(rPart.Apply(x)), 1e-12);
                }
            }
        }

        [Fact]
        public void Mirroring_the_part_about_the_exact_plane_keeps_the_original_placement()
        {
            var rnd = new Random(17);
            Affine t = Affine.FromFrame(RandomFrame(rnd));
            var asmPlane = new Plane3(RandomPoint(rnd), RandomUnit(rnd));
            var rPart = new Reflection(ComponentPlacement.ExactPartMirrorPlane(t, asmPlane));
            Affine placed = ComponentPlacement.MirroredPlacement(t, new Reflection(asmPlane), rPart);
            Vec3 x = RandomPoint(rnd);
            Near(t.Apply(x), placed.Apply(x), 1e-12);
        }

        [Fact]
        public void Part_mirror_plane_is_the_standard_plane_closest_to_the_assembly_plane()
        {
            // Component rotated 90° about Z: its X axis points along assembly Y.
            var t = new Affine(Vec3.UnitY, -Vec3.UnitX, Vec3.UnitZ, new Vec3(0.1, 0, 0));
            Plane3 p = ComponentPlacement.ChoosePartMirrorPlane(t, new Plane3(Vec3.Zero, Vec3.UnitY));
            Near(Vec3.UnitX, p.Normal);
        }
    }
}
