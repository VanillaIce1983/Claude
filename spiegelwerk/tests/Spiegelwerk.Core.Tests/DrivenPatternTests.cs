using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Curve and sketch driven patterns: what the mirrored pattern has to report.</summary>
    public class DrivenPatternTests
    {
        [Fact]
        public void Sketch_pattern_mirrored_is_the_pattern_of_the_mirrored_sketch()
        {
            var rnd = new Random(11);
            for (int i = 0; i < 50; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Vec3 reference = RandomPoint(rnd);
                List<Vec3> points = Enumerable.Range(0, 5).Select(_ => RandomPoint(rnd)).ToList();

                List<Affine> wanted = PatternInstances.Mirror(PatternInstances.Translations(reference, points), r);
                List<Affine> rebuilt = PatternInstances.Translations(r.Apply(reference), points.Select(r.Apply));
                Assert.Equal(0, PatternInstances.Unmatched(wanted, rebuilt));
            }
        }

        [Fact]
        public void Sketch_pattern_with_the_unmirrored_points_does_not_match()
        {
            var r = new Reflection(Plane3.RightYZ);
            var reference = new Vec3(0.01, 0.01, 0);
            var points = new List<Vec3> { new Vec3(0.03, 0.01, 0), new Vec3(0.05, 0.02, 0) };
            List<Affine> wanted = PatternInstances.Mirror(PatternInstances.Translations(reference, points), r);
            Assert.Equal(2, PatternInstances.Unmatched(wanted, PatternInstances.Translations(reference, points)));
        }

        /// <summary>
        /// Instances tangent to an arc are rotations about its centre: along the mirrored arc
        /// the copy has to run the other way round – the flag is measured, not copied.
        /// </summary>
        [Fact]
        public void Curve_pattern_tangent_to_an_arc_runs_the_other_way_along_the_mirrored_arc()
        {
            var r = new Reflection(new Plane3(new Vec3(0.1, 0, 0), Vec3.UnitX));
            var c = new Vec3(0.02, 0.03, 0);
            double step = 15 * Math.PI / 180;
            List<Affine> Along(Vec3 centre, double s) => Enumerable.Range(0, 4).Select(i => Rotation(centre, Vec3.UnitZ, s * i)).ToList();

            List<Affine> wanted = PatternInstances.Mirror(Along(c, step), r);
            Assert.Equal(0, PatternInstances.Unmatched(wanted, Along(r.Apply(c), -step)));
            Assert.Equal(3, PatternInstances.Unmatched(wanted, Along(r.Apply(c), step)));
        }

        [Fact]
        public void Curve_pattern_instance_count_covers_both_directions()
        {
            var one = new CurvePatternRecord("CP", "CurvePattern") { Count1 = 5 };
            var two = new CurvePatternRecord("CP", "CurvePattern") { Count1 = 5, HasDirection2 = true, Count2 = 3 };
            var zero2 = new CurvePatternRecord("CP", "CurvePattern") { Count1 = 4, HasDirection2 = true, Count2 = 0 };
            Assert.Equal(5, one.InstanceCount);
            Assert.Equal(15, two.InstanceCount);
            Assert.Equal(4, zero2.InstanceCount);
        }

        private static Affine Rotation(Vec3 c, Vec3 axis, double angle)
        {
            Vec3 k = axis.Normalized();
            Vec3 Rot(Vec3 v) => v * Math.Cos(angle) + k.Cross(v) * Math.Sin(angle) + k * (k.Dot(v) * (1 - Math.Cos(angle)));
            var linear = new Affine(Rot(Vec3.UnitX), Rot(Vec3.UnitY), Rot(Vec3.UnitZ), Vec3.Zero);
            return new Affine(linear.Col0, linear.Col1, linear.Col2, c - linear.ApplyVector(c));
        }
    }
}
