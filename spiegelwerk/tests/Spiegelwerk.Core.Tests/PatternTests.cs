using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Mirrored pattern placements: translations follow R, rotations change their sense.</summary>
    public class PatternTests
    {
        private static Affine Translation(Vec3 d) => new Affine(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, d);

        /// <summary>Rotation by <paramref name="angle"/> (right hand) about the line through <paramref name="c"/> along <paramref name="axis"/>.</summary>
        private static Affine Rotation(Vec3 c, Vec3 axis, double angle)
        {
            Vec3 k = axis.Normalized();
            Vec3 Rot(Vec3 v) => v * Math.Cos(angle) + k.Cross(v) * Math.Sin(angle) + k * (k.Dot(v) * (1 - Math.Cos(angle)));
            var linear = new Affine(Rot(Vec3.UnitX), Rot(Vec3.UnitY), Rot(Vec3.UnitZ), Vec3.Zero);
            return new Affine(linear.Col0, linear.Col1, linear.Col2, c - linear.ApplyVector(c));
        }

        private static List<Affine> Linear(Vec3 step, int n) => Enumerable.Range(0, n).Select(i => Translation(step * i)).ToList();

        private static List<Affine> Circular(Vec3 c, Vec3 axis, double step, int n) =>
            Enumerable.Range(0, n).Select(i => Rotation(c, axis, step * i)).ToList();

        [Fact]
        public void Mirrored_placement_puts_the_instance_where_the_mirror_image_is()
        {
            var rnd = new Random(5);
            for (int i = 0; i < 200; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Affine t = rnd.Next(2) == 0
                    ? Translation(RandomPoint(rnd))
                    : Rotation(RandomPoint(rnd), RandomUnit(rnd), rnd.NextDouble() * 6);
                Vec3 x = RandomPoint(rnd);
                Near(r.Apply(t.Apply(x)), PatternInstances.Mirror(t, r).Apply(r.Apply(x)), 1e-12);
            }
        }

        [Fact]
        public void Linear_pattern_runs_along_the_mirrored_direction()
        {
            var r = new Reflection(Plane3.RightYZ);
            var step = new Vec3(0.02, 0.005, 0);
            List<Affine> wanted = PatternInstances.Mirror(Linear(step, 4), r);

            Assert.Equal(0, PatternInstances.Unmatched(wanted, Linear(new Vec3(-0.02, 0.005, 0), 4)));
            Assert.Equal(3, PatternInstances.Unmatched(wanted, Linear(new Vec3(0.02, 0.005, 0), 4)));
            Assert.Equal(3, PatternInstances.Unmatched(wanted, Linear(new Vec3(0.02, -0.005, 0), 4)));
        }

        [Fact]
        public void Circular_pattern_turns_the_other_way_round_the_mirrored_axis()
        {
            var r = new Reflection(new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX));
            var c = new Vec3(0.01, 0.02, 0);
            double step = 25 * Math.PI / 180;
            List<Affine> wanted = PatternInstances.Mirror(Circular(c, Vec3.UnitZ, step, 5), r);

            Vec3 mc = r.Apply(c);
            Assert.Equal(0, PatternInstances.Unmatched(wanted, Circular(mc, Vec3.UnitZ, -step, 5)));
            Assert.Equal(0, PatternInstances.Unmatched(wanted, Circular(mc, -Vec3.UnitZ, step, 5)));
            Assert.Equal(4, PatternInstances.Unmatched(wanted, Circular(mc, Vec3.UnitZ, step, 5)));
        }

        [Fact]
        public void Full_circle_with_equal_spacing_is_right_in_both_senses()
        {
            var r = new Reflection(Plane3.FrontXY);
            double step = 2 * Math.PI / 6;
            List<Affine> wanted = PatternInstances.Mirror(Circular(Vec3.Zero, Vec3.UnitY, step, 6), r);
            Assert.Equal(0, PatternInstances.Unmatched(wanted, Circular(Vec3.Zero, Vec3.UnitY, step, 6)));
            Assert.Equal(0, PatternInstances.Unmatched(wanted, Circular(Vec3.Zero, Vec3.UnitY, -step, 6)));
        }

        [Fact]
        public void Order_does_not_matter_but_the_count_does()
        {
            List<Affine> a = Linear(new Vec3(0, 0, 0.01), 4);
            var shuffled = new List<Affine> { a[2], a[0], a[3], a[1] };
            Assert.Equal(0, PatternInstances.Unmatched(a, shuffled));
            Assert.Equal(1, PatternInstances.Unmatched(a, a.Take(3).ToList()));
            Assert.Equal(2, PatternInstances.Unmatched(a, Linear(new Vec3(0, 0, 0.01), 6)));
        }
    }
}
