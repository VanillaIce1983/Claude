using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// The coordinate space of region edges is not documented: decided per region, never
    /// defaulted. Also the fallback policy for profiles and the ways to hand them over.
    /// </summary>
    public class RegionSpaceTests
    {
        private const double Eps = 1e-5;

        /// <summary>Corners of a 20 × 10 mm rectangle at (10|0) in sketch coordinates – also the sketch points.</summary>
        private static readonly List<Vec3> Corners = new List<Vec3>
        {
            new Vec3(0.01, 0, 0), new Vec3(0.03, 0, 0), new Vec3(0.03, 0.01, 0), new Vec3(0.01, 0.01, 0),
        };

        /// <summary>Boundary points of that rectangle (the sampled edges), in sketch coordinates.</summary>
        private static List<Vec3> Boundary()
        {
            var points = new List<Vec3>();
            for (int i = 0; i < 4; i++)
            {
                Vec3 a = Corners[i], b = Corners[(i + 1) % 4];
                for (int k = 0; k < 8; k++) points.Add(a + (b - a) * (k / 8.0));
            }
            return points;
        }

        private static List<Vec3> InModel(Frame3 f, IEnumerable<Vec3> local) => local.Select(p => f.ToWorld(p.X, p.Y)).ToList();

        private static RegionSpaceDecision Decide(IReadOnlyCollection<Vec3> raw, Frame3 f, IReadOnlyCollection<Vec3> vertices, RawSpace? known = null,
            IReadOnlyCollection<Vec3>? sketchPoints = null) =>
            RegionSpace.Decide(raw, f, vertices, sketchPoints ?? Corners, known, Eps);

        [Fact]
        public void Top_and_right_plane_sketches_decide_uniquely_for_either_space()
        {
            foreach (Frame3 f in new[] { Top, Right })
            {
                RegionSpaceDecision model = Decide(InModel(f, Boundary()), f, InModel(f, Corners));
                Assert.Equal(RegionSpaceOutcome.Model, model.Outcome);
                Assert.Equal(RegionSpaceBasis.Plane, model.Basis);
                RegionSpaceDecision sketch = Decide(Boundary(), f, Corners);
                Assert.Equal(RegionSpaceOutcome.Sketch, sketch.Outcome);
                Assert.Equal(RegionSpaceBasis.Plane, sketch.Basis);
                // Both readings give the same sketch coordinates.
                for (int i = 0; i < Corners.Count; i++)
                    Near(Corners[i], RegionSpace.ToSketch(InModel(f, Corners)[i], RawSpace.Model, f), 1e-15);
            }
        }

        [Fact]
        public void Front_plane_with_the_identity_frame_does_not_matter()
        {
            RegionSpaceDecision d = Decide(Boundary(), Front, Corners, sketchPoints: new List<Vec3>());
            Assert.Equal(RegionSpaceOutcome.Model, d.Outcome);
            Assert.Equal(RegionSpaceBasis.Invariant, d.Basis);
        }

        [Fact]
        public void Back_face_at_z_zero_is_decided_by_the_vote_the_cache_or_not_at_all()
        {
            // A face at z = 0 with normal −Z: sketch x runs along −X. Both readings put every point into the plane.
            var back = new Frame3(Vec3.Zero, new Vec3(-1, 0, 0), Vec3.UnitY);
            List<Vec3> rawModel = InModel(back, Boundary()), verticesModel = InModel(back, Corners);

            RegionSpaceDecision model = Decide(rawModel, back, verticesModel);
            Assert.Equal(RegionSpaceOutcome.Model, model.Outcome);
            Assert.Equal(RegionSpaceBasis.Vote, model.Basis);
            RegionSpaceDecision sketch = Decide(Boundary(), back, Corners);
            Assert.Equal(RegionSpaceOutcome.Sketch, sketch.Outcome);
            Assert.Equal(RegionSpaceBasis.Vote, sketch.Basis);

            // No sketch point to vote with: undecided – unless the run already knows.
            RegionSpaceDecision none = Decide(rawModel, back, verticesModel, sketchPoints: new List<Vec3>());
            Assert.Equal(RegionSpaceOutcome.Undecided, none.Outcome);
            Assert.Null(none.Space);
            RegionSpaceDecision known = Decide(rawModel, back, verticesModel, RawSpace.Model, new List<Vec3>());
            Assert.Equal(RegionSpaceOutcome.Model, known.Outcome);
            Assert.Equal(RegionSpaceBasis.Known, known.Basis);

            // Sketch points symmetric to the flip vote for both readings alike.
            var symmetric = Corners.Concat(Corners.Select(c => new Vec3(-c.X, c.Y, 0))).ToList();
            Assert.Equal(RegionSpaceOutcome.Undecided, Decide(rawModel, back, verticesModel, sketchPoints: symmetric).Outcome);
        }

        [Fact]
        public void Points_off_the_sketch_plane_in_both_readings_are_an_error()
        {
            List<Vec3> lifted = Boundary().Select(p => new Vec3(p.X, p.Y, 0.001)).ToList();
            RegionSpaceDecision d = Decide(lifted, Front, Corners);
            Assert.Equal(RegionSpaceOutcome.Error, d.Outcome);
            Assert.Null(d.Space);
            Assert.Equal(RegionSpaceOutcome.Error, Decide(new List<Vec3>(), Front, Corners).Outcome);
        }

        [Fact]
        public void The_run_learns_only_from_a_certain_reading_and_keeps_the_first()
        {
            var memory = new RegionSpaceMemory();
            Assert.Equal(RegionSpace.Confirmed, memory.Known);
            memory.Learn(new RegionSpaceDecision(RegionSpaceOutcome.Sketch, RegionSpaceBasis.Vote));
            memory.Learn(new RegionSpaceDecision(RegionSpaceOutcome.Model, RegionSpaceBasis.Invariant));
            Assert.Null(memory.Known);
            Assert.Null(memory.ReportLine());
            memory.Learn(new RegionSpaceDecision(RegionSpaceOutcome.Sketch, RegionSpaceBasis.Plane));
            memory.Learn(new RegionSpaceDecision(RegionSpaceOutcome.Model, RegionSpaceBasis.Plane));
            Assert.Equal(RawSpace.Sketch, memory.Known);
            Assert.Equal("Skizzenbereiche: SOLIDWORKS liefert ihre Kanten in Skizzenkoordinaten (gemessen).", memory.ReportLine());
        }

        // ------------------------------------------------------------ policy and ways

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        public void Only_the_same_part_without_a_body_of_its_own_refuses_an_inexact_profile(bool samePart, bool separateBody, bool refuses)
        {
            Assert.Equal(refuses, ProfileRules.RefusesInexact(samePart, separateBody));
        }

        [Fact]
        public void Profile_ways_follow_the_api_examples_and_end_with_the_whole_sketch()
        {
            IReadOnlyList<ProfileWay> extrude = ProfileRules.Ways(false);
            Assert.Equal(4, extrude.Count);
            Assert.True(extrude[0].ContourSelection && extrude[0].ProfileMark == 4 && !extrude[0].WithSketch);
            Assert.True(extrude[1].ContourSelection && extrude[1].ProfileMark == 4 && extrude[1].WithSketch);
            Assert.True(!extrude[2].ContourSelection && extrude[2].ProfileMark == 0);
            Assert.True(extrude[3].WholeSketch);
            Assert.All(extrude, w => Assert.Equal(-1, w.AxisMark));

            IReadOnlyList<ProfileWay> revolve = ProfileRules.Ways(true);
            // Never mark 4 for profile and axis together.
            Assert.DoesNotContain(revolve, w => !w.WholeSketch && w.ProfileMark == 4 && w.AxisMark == 4);
            Assert.Equal(new[] { 16, 16, 16, 4, 16, 4 }, revolve.Select(w => w.AxisMark));
            Assert.Equal(new[] { false, false, false, false, true, true }, revolve.Select(w => w.WholeSketch));
            // Whole-sketch ways come last, in the order a revolve without a profile has always used.
            Assert.True(revolve.SkipWhile(w => !w.WholeSketch).All(w => w.WholeSketch));
        }

        [Fact]
        public void Profile_notes_name_the_reason_and_the_consequence()
        {
            Assert.Equal("Eine ausgewählte Kontur wurde in der neuen Skizze nicht gefunden; es wird die ganze Skizze verwendet.",
                ProfileRules.WholeSketch("Eine ausgewählte Kontur wurde in der neuen Skizze nicht gefunden"));
            Assert.Equal("Ausgewählte Konturen und Skizzenbereiche nicht lesbar: der Rand des Bereichs ist nicht geschlossen.",
                ProfileRules.Unreadable("der Rand des Bereichs ist nicht geschlossen"));
            Assert.Contains("nicht angelegt", ProfileRules.Refusal);
        }

        [Fact]
        public void Square_millimetres_in_the_given_format()
        {
            var comma = new NumberFormatInfo { NumberDecimalSeparator = ",", NegativeSign = "-" };
            Assert.Equal("12,5 mm²", ReportText.SquareMillimetres(1.25e-5, comma));
            Assert.Equal("1600 mm²", ReportText.SquareMillimetres(1.6e-3, CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------ records

        [Fact]
        public void Extrude_and_revolve_records_carry_contours_and_regions_as_one_profile()
        {
            IProfileRecord[] records = { new ExtrudeRecord("Aufsatz1", "Extrusion", "Skizze1"), new RevolveRecord("Rotation1", "Revolution", "Skizze2") };
            foreach (IProfileRecord r in records)
            {
                Assert.Empty(r.Contours);
                Assert.Empty(r.Regions);
                Assert.False(r.SelectionLost);
                r.Regions.Add(ProfileRegion.Create(RegionShapes.Weld(RegionShapes.Rect(0, 0, 0.02, 0.01)), Eps, null));
                Assert.Single(r.Regions);
            }
            Assert.Equal("Skizze2", records[1].SketchName);
        }
    }
}
