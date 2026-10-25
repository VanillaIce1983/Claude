using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    public class AdvancedHoleTests
    {
        // Plate 10 mm thick between z = 0 and z = 0.01, hole from the top face (z = 0.01) downwards.
        private static readonly Vec3 Centre = new Vec3(0.02, 0.03, 0.01);
        private static readonly Vec3 Down = new Vec3(0, 0, -1);

        [Fact]
        public void Opposite_face_of_a_plate_is_the_exit()
        {
            var bottom = new Plane3(new Vec3(0, 0, 0), new Vec3(0, 0, -1));
            double? t = HoleAxis.ExitDistance(Centre, Down, bottom);
            Assert.NotNull(t);
            Assert.Equal(0.01, t!.Value, 9);
        }

        [Fact]
        public void Near_face_itself_entry_faces_and_parallel_faces_are_no_exit()
        {
            var top = new Plane3(new Vec3(0, 0, 0.01), new Vec3(0, 0, 1));
            var side = new Plane3(new Vec3(0.05, 0, 0), new Vec3(1, 0, 0));
            var pocketFloor = new Plane3(new Vec3(0, 0, 0.004), new Vec3(0, 0, 1));   // faces up: the axis enters there
            var behind = new Plane3(new Vec3(0, 0, 0.02), new Vec3(0, 0, -1));       // behind the centre
            Assert.Null(HoleAxis.ExitDistance(Centre, Down, top));
            Assert.Null(HoleAxis.ExitDistance(Centre, Down, side));
            Assert.Null(HoleAxis.ExitDistance(Centre, Down, pocketFloor));
            Assert.Null(HoleAxis.ExitDistance(Centre, Down, behind));
        }

        [Fact]
        public void First_exit_takes_the_nearest_covered_face()
        {
            // A step: the lower face at z = 0.004 covers the axis, the one at z = 0.002 does not.
            var planes = new List<Plane3>
            {
                new Plane3(new Vec3(0, 0, 0), new Vec3(0, 0, -1)),
                new Plane3(new Vec3(0, 0, 0.002), new Vec3(0, 0, -1)),
                new Plane3(new Vec3(0, 0, 0.004), new Vec3(0, 0, -1)),
            };
            Assert.Equal(2, HoleAxis.FirstExit(Centre, Down, planes, (i, p) => i != 1));
            Assert.Equal(0, HoleAxis.FirstExit(Centre, Down, planes, (i, p) => i == 0));
            Assert.Equal(-1, HoleAxis.FirstExit(Centre, Down, planes, (i, p) => false));
        }

        [Fact]
        public void Covers_is_asked_at_the_crossing_point()
        {
            var planes = new List<Plane3> { new Plane3(new Vec3(0, 0, 0), new Vec3(0, 0, -1)) };
            Vec3? asked = null;
            HoleAxis.FirstExit(Centre, Down, planes, (i, p) =>
            {
                asked = p;
                return true;
            });
            Assert.True(asked.HasValue && asked.Value.AlmostEquals(new Vec3(0.02, 0.03, 0)));
        }

        [Fact]
        public void Element_types_are_the_five_general_types()
        {
            for (int t = 0; t <= 4; t++) Assert.True(AdvancedHoleTypes.IsKnown(t));
            Assert.False(AdvancedHoleTypes.IsKnown(5));
            Assert.False(AdvancedHoleTypes.IsKnown(-1));
        }
    }
}
