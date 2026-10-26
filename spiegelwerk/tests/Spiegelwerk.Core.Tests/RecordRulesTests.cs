using System;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Rules in the records that steer the builders: end conditions and their blind equivalent,
    /// revolve direction and layout, twisted sweeps, loft tangency, slots, measured dimensions.
    /// </summary>
    public class RecordRulesTests
    {
        private const double Full = 2 * Math.PI;

        // ------------------------------------------------------------ extrude end conditions

        [Theory]
        [InlineData(EndCondition.Blind, false)]
        [InlineData(EndCondition.ThroughAll, false)]
        [InlineData(EndCondition.ThroughNext, false)]
        [InlineData(EndCondition.UpToVertex, true)]
        [InlineData(EndCondition.UpToSurface, true)]
        [InlineData(EndCondition.OffsetFromSurface, true)]
        [InlineData(EndCondition.MidPlane, false)]
        [InlineData(EndCondition.UpToBody, true)]
        [InlineData(EndCondition.ThroughAllBoth, false)]
        [InlineData(EndCondition.UpToSelection, true)]
        [InlineData(EndCondition.UpToNext, false)]
        public void End_conditions_up_to_or_offset_from_something_need_a_reference(EndCondition end, bool needs)
        {
            Assert.Equal(needs, ExtrudeSide.NeedsReference(end));
        }

        private static ExtrudeSide Side(EndCondition end, GeometryRef? reference, double offset = 0, bool reverse = false) =>
            new ExtrudeSide { End = end, Reference = reference, Depth = offset, OffsetReverse = reverse };

        [Theory]
        [InlineData(0.03)]
        [InlineData(-0.03)]
        public void Up_to_a_parallel_plane_is_a_blind_depth_on_either_side_of_the_sketch(double z)
        {
            GeometryRef plane = GeometryRef.ToPlane(new Plane3(new Vec3(0.1, 0.2, z), -Vec3.UnitZ), "Ebene1");
            Near(0.03, Side(EndCondition.UpToSurface, plane).BlindEquivalent(Plane3.FrontXY)!.Value);
        }

        [Fact]
        public void Offset_from_a_parallel_face_takes_the_offset_off_unless_reversed()
        {
            GeometryRef face = GeometryRef.ToFace(new FaceSignature(SurfaceKind.Plane, 1e-4, new Vec3(0.01, 0.01, 0.03)) { Normal = -Vec3.UnitZ });
            Near(0.025, Side(EndCondition.OffsetFromSurface, face, 0.005).BlindEquivalent(Plane3.FrontXY)!.Value);
            Near(0.035, Side(EndCondition.OffsetFromSurface, face, 0.005, reverse: true).BlindEquivalent(Plane3.FrontXY)!.Value);
            // Offset back to the sketch plane or beyond: no depth left.
            Assert.Null(Side(EndCondition.OffsetFromSurface, face, 0.03).BlindEquivalent(Plane3.FrontXY));
            Assert.Null(Side(EndCondition.OffsetFromSurface, face, 0.04).BlindEquivalent(Plane3.FrontXY));
        }

        [Fact]
        public void Only_parallel_planes_and_planar_faces_give_a_blind_depth()
        {
            GeometryRef parallel = GeometryRef.ToPlane(new Plane3(new Vec3(0, 0, 0.03), Vec3.UnitZ), "Ebene1");
            GeometryRef tilted = GeometryRef.ToPlane(new Plane3(new Vec3(0, 0, 0.03), new Vec3(0, 0.1, 1)), "Ebene2");
            GeometryRef cylinder = GeometryRef.ToFace(new FaceSignature(SurfaceKind.Cylinder, 1e-4, new Vec3(0, 0, 0.03)));

            Assert.Null(Side(EndCondition.UpToSurface, tilted).BlindEquivalent(Plane3.FrontXY));
            Assert.Null(Side(EndCondition.UpToSurface, cylinder).BlindEquivalent(Plane3.FrontXY));
            Assert.Null(Side(EndCondition.UpToVertex, GeometryRef.ToVertex(new Vec3(0, 0, 0.03))).BlindEquivalent(Plane3.FrontXY));
            Assert.Null(Side(EndCondition.UpToBody, parallel).BlindEquivalent(Plane3.FrontXY));
            Assert.Null(Side(EndCondition.UpToSurface, null).BlindEquivalent(Plane3.FrontXY));   // reference not readable
            Assert.Null(Side(EndCondition.UpToSurface, GeometryRef.ToPlane(Plane3.FrontXY, "Vorne")).BlindEquivalent(Plane3.FrontXY));
            Near(0.03, Side(EndCondition.UpToSurface, parallel).BlindEquivalent(new Plane3(new Vec3(0.2, -0.1, 0), -Vec3.UnitZ))!.Value);
        }

        // ------------------------------------------------------------ revolve

        private static RevolveRecord Revolve(int type, double a1, double a2 = 0) =>
            new RevolveRecord("Rotation1", "Revolution", "Skizze1") { RevolveType = type, Angle1 = a1, Angle2 = a2 };

        [Theory]
        [InlineData(RevolveTypes.OneDirection, 1.0, true)]
        [InlineData(RevolveTypes.OneDirection, Full - 1e-6, true)]
        [InlineData(RevolveTypes.OneDirection, Full, false)]           // a full turn has no side
        [InlineData(RevolveTypes.MidPlane, 1.0, false)]                // symmetric
        [InlineData(RevolveTypes.TwoDirection, 1.0, true)]
        [InlineData(RevolveTypes.OneDirection360Degrees, Full, false)]
        [InlineData(RevolveTypes.MidPlane360Degrees, Full, false)]
        [InlineData(RevolveTypes.TwoDirection360Degrees, 1.0, false)]
        public void Revolve_direction_matters_for_partial_one_sided_and_two_sided_revolves(int type, double angle, bool matters)
        {
            Assert.Equal(matters, Revolve(type, angle, 0.5).DirectionMatters);
        }

        [Fact]
        public void Revolve_layout_follows_the_revolve_type()
        {
            Assert.Equal((true, EndCondition.Blind, 1.0, EndCondition.Blind, 0.0), Revolve(RevolveTypes.OneDirection, 1.0, 0.5).Layout());
            Assert.Equal((true, EndCondition.MidPlane, 1.0, EndCondition.Blind, 0.0), Revolve(RevolveTypes.MidPlane, 1.0, 0.5).Layout());
            Assert.Equal((false, EndCondition.Blind, 1.0, EndCondition.Blind, 0.5), Revolve(RevolveTypes.TwoDirection, 1.0, 0.5).Layout());

            // The 360° types turn fully whatever angle was read; two directions keep their split.
            Assert.Equal((true, EndCondition.Blind, Full, EndCondition.Blind, 0.0), Revolve(RevolveTypes.OneDirection360Degrees, 1.0).Layout());
            Assert.Equal((true, EndCondition.MidPlane, Full, EndCondition.Blind, 0.0), Revolve(RevolveTypes.MidPlane360Degrees, 1.0).Layout());
            Assert.Equal((false, EndCondition.Blind, 4.0, EndCondition.Blind, Full - 4.0), Revolve(RevolveTypes.TwoDirection360Degrees, 4.0, Full - 4.0).Layout());

            // A type this table does not know: one direction with the angle read.
            Assert.Equal((true, EndCondition.Blind, 1.0, EndCondition.Blind, 0.0), Revolve(17, 1.0, 0.5).Layout());
        }

        // ------------------------------------------------------------ sweep and loft

        [Theory]
        [InlineData(TwistControl.ConstantTwistAlongPath, 0.5, 0.0, true)]
        [InlineData(TwistControl.NormalConstantTwistAlongPath, 0.0, -0.3, true)]   // second direction only
        [InlineData(TwistControl.ConstantTwistAlongPath, 0.0, 0.0, false)]         // twist control without a twist
        [InlineData(TwistControl.FollowPath, 0.5, 0.5, false)]                     // angles left over from an earlier setting
        [InlineData(TwistControl.KeepNormalConstant, 0.5, 0.0, false)]
        [InlineData(TwistControl.FollowPathFirstGuideCurve, 0.5, 0.0, false)]
        [InlineData(TwistControl.FollowFirstSecondGuideCurves, 0.5, 0.0, false)]
        public void Sweep_twists_only_with_a_twist_control_and_an_angle(TwistControl twist, double a1, double a2, bool expected)
        {
            var r = new SweepRecord("Austragung1", "Sweep") { Twist = twist, TwistAngle = a1, TwistAngle2 = a2 };
            Assert.Equal(expected, r.HasTwist);
        }

        [Theory]
        [InlineData(Tangency.None, false)]
        [InlineData(Tangency.NormalToProfile, true)]
        [InlineData(Tangency.DirectionVector, true)]
        [InlineData(Tangency.AllFaces, false)]
        [InlineData(Tangency.MinimumTwist, false)]
        public void Loft_tangency_has_a_side_only_normal_to_profile_or_along_a_vector(Tangency t, bool side)
        {
            Assert.Equal(side, LoftRecord.HasSide(t));
        }

        // ------------------------------------------------------------ hole wizard

        [Theory]
        [InlineData(HoleTypes.CounterBore, false)]
        [InlineData(HoleTypes.CounterSink, false)]
        [InlineData(HoleTypes.Hole, false)]
        [InlineData(HoleTypes.PipeTap, false)]
        [InlineData(HoleTypes.Tap, false)]
        [InlineData(HoleTypes.Legacy, false)]
        [InlineData(HoleTypes.CounterBoreSlot, true)]
        [InlineData(HoleTypes.CounterSinkSlot, true)]
        [InlineData(HoleTypes.HoleSlot, true)]
        public void Slots_are_the_three_slot_types(int generic, bool slot)
        {
            Assert.Equal(slot, HoleTypes.IsSlot(generic));
        }

        // ------------------------------------------------------------ dimensions placed in the copy

        private static SkDimension Dim(DimensionKind kind, double value) => new SkDimension(kind, new SkRef[0], value);

        [Fact]
        public void Placed_length_must_measure_the_value_within_a_millionth()
        {
            SkDimension d = Dim(DimensionKind.Aligned, 0.5);
            Assert.True(d.Matches(0.5 + 4e-7));
            Assert.False(d.Matches(0.5 + 6e-7));
            Assert.True(Dim(DimensionKind.Diameter, 2.0).Matches(2.0 - 1.5e-6));

            // Below 0.1 m the floor of 0.1 µm applies.
            SkDimension small = Dim(DimensionKind.Radial, 0.004);
            Assert.True(small.Matches(0.004 + 9e-8));
            Assert.False(small.Matches(0.004 + 2e-7));
        }

        [Fact]
        public void Placed_angle_must_measure_the_angle_not_its_supplement()
        {
            SkDimension d = Dim(DimensionKind.Angular, Math.PI / 3);
            Assert.True(d.Matches(Math.PI / 3 + 5e-7));
            Assert.False(d.Matches(Math.PI / 3 + 2e-6));
            Assert.False(d.Matches(2 * Math.PI / 3));
        }
    }
}
