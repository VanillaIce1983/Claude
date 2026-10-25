using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Table driven pattern and coordinate systems: the mirrored copy stays right-handed.</summary>
    public class TablePatternTests
    {
        private static Frame3 RandomFrame(Random rnd)
        {
            Vec3 x = RandomUnit(rnd);
            return new Frame3(RandomPoint(rnd), x, x.AnyPerpendicular());
        }

        [Fact]
        public void Mirrored_coordinate_system_is_right_handed_with_mirrored_x_and_y()
        {
            var rnd = new Random(21);
            for (int i = 0; i < 100; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Frame3 f = RandomFrame(rnd);
                Frame3 m = r.ApplyKeepingXY(f);
                Near(r.Apply(f.Origin), m.Origin, 1e-12);
                Near(r.ApplyVector(f.XAxis), m.XAxis, 1e-12);
                Near(r.ApplyVector(f.YAxis), m.YAxis, 1e-12);
                Near(-r.ApplyVector(f.ZAxis), m.ZAxis, 1e-12);
                Assert.Equal(1.0, m.XAxis.Cross(m.YAxis).Dot(m.ZAxis), 9);
            }
        }

        [Fact]
        public void Table_point_in_the_mirrored_coordinate_system_is_where_the_mirror_image_is()
        {
            var rnd = new Random(22);
            for (int i = 0; i < 100; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Frame3 f = RandomFrame(rnd);
                Frame3 m = r.ApplyKeepingXY(f);
                Vec3 p = RandomPoint(rnd);
                Near(r.Apply(f.ToWorld(p)), m.ToWorld(TablePoints.Mirror(p)), 1e-12);
            }
        }

        [Fact]
        public void Points_in_the_xy_plane_keep_their_values()
        {
            var points = new List<Vec3> { new Vec3(0, 0, 0), new Vec3(0.02, 0.01, 0), new Vec3(-0.015, 0.03, 0) };
            Assert.True(TablePoints.Same(points, TablePoints.Mirror(points)));
            Assert.False(TablePoints.Same(new[] { new Vec3(0, 0, 0.01) }, TablePoints.Mirror(new[] { new Vec3(0, 0, 0.01) })));
        }

        [Fact]
        public void Flat_array_round_trip()
        {
            var points = new List<Vec3> { new Vec3(1, 2, 3), new Vec3(-4, 5, -6) };
            double[] flat = TablePoints.ToArray(points);
            Assert.Equal(new double[] { 1, 2, 3, -4, 5, -6 }, flat);
            Assert.True(TablePoints.Same(points, TablePoints.FromArray(flat)));
            Assert.Single(TablePoints.FromArray(new double[] { 1, 2, 3, 4, 5 }));
        }

        /// <summary>Instances are translations within the XY plane: mirrored they are the translations in the mirrored plane.</summary>
        [Fact]
        public void Table_pattern_placements_mirror_like_the_points()
        {
            var r = new Reflection(new Plane3(new Vec3(0.03, 0, 0), new Vec3(1, 1, 0).Normalized()));
            var f = new Frame3(new Vec3(0.01, 0.02, 0.005), Vec3.UnitX, Vec3.UnitY);
            var table = new List<Vec3> { Vec3.Zero, new Vec3(0.02, 0, 0), new Vec3(0, 0.03, 0), new Vec3(0.01, 0.01, 0) };
            Vec3 reference = f.ToWorld(table[0]);
            List<Affine> original = PatternInstances.Translations(reference, table.Select(f.ToWorld));

            Frame3 m = r.ApplyKeepingXY(f);
            List<Vec3> mirroredTable = TablePoints.Mirror(table);
            List<Affine> rebuilt = PatternInstances.Translations(m.ToWorld(mirroredTable[0]), mirroredTable.Select(m.ToWorld));
            Assert.Equal(0, PatternInstances.Unmatched(PatternInstances.Mirror(original, r), rebuilt));
        }
    }
}
