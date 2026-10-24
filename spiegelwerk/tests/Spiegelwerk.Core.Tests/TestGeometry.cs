using System;
using Spiegelwerk.Core.Geometry;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    internal static class TestGeometry
    {
        /// <summary>SOLIDWORKS standard sketch frames (sketch X, sketch Y, normal).</summary>
        public static readonly Frame3 Front = new Frame3(Vec3.Zero, Vec3.UnitX, Vec3.UnitY);
        public static readonly Frame3 Top = new Frame3(Vec3.Zero, Vec3.UnitX, new Vec3(0, 0, -1));
        public static readonly Frame3 Right = new Frame3(Vec3.Zero, new Vec3(0, 0, -1), Vec3.UnitY);

        public static void Near(Vec3 expected, Vec3 actual, double tol = 1e-9)
        {
            Assert.True(expected.DistanceTo(actual) <= tol, $"erwartet {expected}, erhalten {actual}");
        }

        public static void Near(double expected, double actual, double tol = 1e-9)
        {
            Assert.True(Math.Abs(expected - actual) <= tol, $"erwartet {expected}, erhalten {actual}");
        }

        public static Vec3 RandomUnit(Random rnd)
        {
            while (true)
            {
                var v = new Vec3(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
                if (v.Length > 0.2 && v.Length < 1) return v.Normalized();
            }
        }

        public static Vec3 RandomPoint(Random rnd, double size = 0.2) =>
            new Vec3((rnd.NextDouble() * 2 - 1) * size, (rnd.NextDouble() * 2 - 1) * size, (rnd.NextDouble() * 2 - 1) * size);

        public static Frame3 RandomFrame(Random rnd)
        {
            Vec3 x = RandomUnit(rnd);
            Vec3 y = x.Cross(RandomUnit(rnd)).Normalized();
            return new Frame3(RandomPoint(rnd), x, y);
        }

        /// <summary>Point in the middle of a 2D arc, given its sense of rotation.</summary>
        public static (double x, double y) ArcMid(double cx, double cy, double sx, double sy, double ex, double ey, int dir)
        {
            double a0 = Math.Atan2(sy - cy, sx - cx);
            double a1 = Math.Atan2(ey - cy, ex - cx);
            double sweep = dir > 0 ? a1 - a0 : a0 - a1;
            while (sweep <= 0) sweep += 2 * Math.PI;
            double am = a0 + dir * sweep / 2;
            double r = Math.Sqrt((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy));
            return (cx + r * Math.Cos(am), cy + r * Math.Sin(am));
        }
    }
}
