using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    public class VariableFilletTests
    {
        private static EdgeSignature Line(Vec3 a, Vec3 b) => new EdgeSignature(CurveKind.Line, a, b, (a + b) / 2, a.DistanceTo(b));

        [Fact]
        public void Control_point_keeps_its_place_when_the_rebuilt_edge_runs_the_same_way()
        {
            var r = new Reflection(Plane3.RightYZ);
            EdgeSignature original = Line(new Vec3(0.01, 0, 0), new Vec3(0.05, 0.02, 0));
            EdgeSignature mirrored = original.Mirror(r);
            EdgeSignature rebuilt = Line(mirrored.Start, mirrored.End);
            Assert.Equal(0.25, EdgeLocation.Map(mirrored, rebuilt, 0.25, 1));
        }

        [Theory]
        [InlineData(0.25, 1, 0.75)]
        [InlineData(25, 100, 75)]
        public void Control_point_is_counted_from_the_other_end_when_the_rebuilt_edge_runs_backwards(double location, double full, double expected)
        {
            var r = new Reflection(Plane3.RightYZ);
            EdgeSignature mirrored = Line(new Vec3(0.01, 0, 0), new Vec3(0.05, 0.02, 0)).Mirror(r);
            EdgeSignature rebuilt = Line(mirrored.End, mirrored.Start);
            double? mapped = EdgeLocation.Map(mirrored, rebuilt, location, full);
            Assert.NotNull(mapped);
            Assert.Equal(expected, mapped!.Value, 9);

            // The mapped place is the mirrored place: same point on the edge.
            Vec3 Along(EdgeSignature e, double t) => e.Start + (e.End - e.Start) * (t / full);
            Assert.True(Along(mirrored, location).AlmostEquals(Along(rebuilt, mapped.Value)));
        }

        [Fact]
        public void Closed_or_unrelated_edges_are_not_guessed()
        {
            var c = new Vec3(0.01, 0.01, 0);
            var circle = new EdgeSignature(CurveKind.Circle, c + new Vec3(0.005, 0, 0), c + new Vec3(0.005, 0, 0), c - new Vec3(0.005, 0, 0), 0.0314);
            Assert.Null(EdgeLocation.Map(circle, circle, 0.5, 1));
            Assert.Null(EdgeLocation.Map(Line(Vec3.Zero, Vec3.UnitX * 0.01), Line(Vec3.UnitY * 0.01, Vec3.UnitX * 0.01), 0.5, 1));
        }
    }
}
