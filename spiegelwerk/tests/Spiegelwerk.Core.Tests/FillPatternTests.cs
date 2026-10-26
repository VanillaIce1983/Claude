using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Fill pattern: the mirrored staggered lattice and the seed cut rotation.</summary>
    public class FillPatternTests
    {
        private static List<Vec3> Lattice(double stagger, Vec3 d, Vec3 n, int k = 4)
        {
            var points = new List<Vec3>();
            for (int i = -k; i <= k; i++)
                for (int j = -k; j <= k; j++)
                {
                    Vec3 p = FillPatternRules.LatticePoint(i, j, stagger, d, n);
                    if (p.Length <= 2.5) points.Add(p);
                }
            return points;
        }

        private static bool SameSet(List<Vec3> a, List<Vec3> b) =>
            a.Count == b.Count && a.All(p => b.Any(q => q.AlmostEquals(p, 1e-9)));

        [Theory]
        [InlineData(40)]
        [InlineData(60)]
        [InlineData(75)]
        [InlineData(90)]
        [InlineData(110)]
        public void Mirrored_lattice_is_the_one_with_the_supplementary_stagger(double degrees)
        {
            double theta = degrees * Math.PI / 180;
            // Original in-plane frame (d, n = N × d), N = +Z; reflection at the plane x = 0.
            var r = new Reflection(Plane3.RightYZ);
            Vec3 d = new Vec3(1, 1, 0).Normalized(), N = Vec3.UnitZ;
            Vec3 n = N.Cross(d);
            List<Vec3> mirrored = Lattice(theta, d, n).Select(r.ApplyVector).ToList();

            // The copy: boundary normal R(N) (outward stays outward), direction either way.
            Vec3 N2 = r.ApplyVector(N);
            foreach (Vec3 d2 in new[] { r.ApplyVector(d), -r.ApplyVector(d) })
            {
                Vec3 n2 = N2.Cross(d2);
                Assert.True(SameSet(mirrored, Lattice(FillPatternRules.MirrorStagger(theta), d2, n2)));
            }
        }

        [Fact]
        public void Hexagonal_and_square_lattices_are_symmetric()
        {
            Assert.True(FillPatternRules.StaggerIsSymmetric(Math.PI / 3));
            Assert.True(FillPatternRules.StaggerIsSymmetric(Math.PI / 2));
            Assert.True(FillPatternRules.StaggerIsSymmetric(2 * Math.PI / 3));
            Assert.False(FillPatternRules.StaggerIsSymmetric(75 * Math.PI / 180));
        }

        [Fact]
        public void Seed_cut_rotation_changes_sign()
        {
            // A square rotated by α about N, mirrored, is the square rotated by −α about R(N).
            var r = new Reflection(new Plane3(Vec3.Zero, new Vec3(1, 0.3, 0).Normalized()));
            double alpha = 20 * Math.PI / 180;
            Vec3 d = Vec3.UnitX, N = Vec3.UnitZ;
            Vec3 Rot(Vec3 v, Vec3 axis, double a) => v * Math.Cos(a) + axis.Cross(v) * Math.Sin(a) + axis * (axis.Dot(v) * (1 - Math.Cos(a)));

            Vec3 corner = Rot(d, N, alpha);
            Vec3 mirrored = r.ApplyVector(corner);
            Vec3 rebuilt = Rot(r.ApplyVector(d), r.ApplyVector(N), FillPatternRules.MirrorRotation(alpha));
            Assert.True(mirrored.AlmostEquals(rebuilt, 1e-12));
        }
    }
}
