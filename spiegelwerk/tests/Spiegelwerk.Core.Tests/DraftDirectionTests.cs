using System;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>A flag relative to a reference direction (draft pull, neutral plane normal) under a mirror.</summary>
    public class DraftDirectionTests
    {
        private static Vec3 Pull(Vec3 reference, bool reverse) => reverse ? -reference : reference;

        [Fact]
        public void Pull_direction_is_mirrored_whichever_way_the_copy_reference_points()
        {
            var rnd = new Random(23);
            for (int i = 0; i < 300; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Vec3 original = RandomUnit(rnd);
                bool reverse = rnd.Next(2) == 0;
                // The copy's reference is the mirrored one, possibly pointing the other way
                // (a coinciding plane taken with the opposite normal, an edge running backwards).
                Vec3 copy = rnd.Next(2) == 0 ? r.ApplyVector(original) : -r.ApplyVector(original);

                bool flag = DirectionRules.ReverseFlag(reverse, DirectionRules.Sigma(original, r, copy));
                Near(r.ApplyVector(Pull(original, reverse)), Pull(copy, flag));
            }
        }

        [Fact]
        public void Neutral_plane_on_the_mirror_plane_keeps_its_flag_only_if_the_copy_normal_follows()
        {
            var r = new Reflection(Plane3.RightYZ);
            // Neutral plane = right plane, normal +X. Mirrored normal −X; the copy reuses the right plane (+X): σ = −1.
            Assert.Equal(-1, DirectionRules.Sigma(Vec3.UnitX, r, Vec3.UnitX));
            Assert.True(DirectionRules.ReverseFlag(false, -1));
            // Neutral plane = top plane: unaffected by a mirror at right, σ = +1.
            Assert.Equal(1, DirectionRules.Sigma(Vec3.UnitY, r, Vec3.UnitY));
        }
    }
}
