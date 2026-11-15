using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Forming tools (blech-plan S9, A4 §13 FormToolPlacementTests, P10, P11): the rotation rule θ′ = β − θ − 2α against
    /// random frames (checks.py #1), the 3D registration that measures it – it finds ψ for a mirror-symmetric tool and
    /// none for a chiral one (checks.py #2), the smallest |ψ| for a rotationally symmetric one –, the 2D registration of
    /// the orientation points, the side the instances stand on, the samples per instance, configurations and tool names.
    /// </summary>
    public class FormToolPlacementTests
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>Rotation about the tool's z-axis.</summary>
        private static Vec3 Rz(Vec3 v, double a) =>
            new Vec3(v.X * Math.Cos(a) - v.Y * Math.Sin(a), v.X * Math.Sin(a) + v.Y * Math.Cos(a), v.Z);

        /// <summary>Reflection about the plane through the tool's z-axis at angle α in its xy-plane.</summary>
        private static Vec3 S(Vec3 v, double alpha) =>
            new Vec3(v.X * Math.Cos(2 * alpha) + v.Y * Math.Sin(2 * alpha), v.X * Math.Sin(2 * alpha) - v.Y * Math.Cos(2 * alpha), v.Z);

        /// <summary>The frame of the copy's placement: origin R(o), z = R(n) (outward stays outward), x anywhere in the plane.</summary>
        private static Frame3 MirroredPlacement(Frame3 f, Reflection r, Random rnd)
        {
            Vec3 z = r.ApplyVector(f.ZAxis);
            Vec3 t = TestGeometry.RandomUnit(rnd);
            Vec3 x = (t - z * t.Dot(z)).Normalized();
            return new Frame3(r.Apply(f.Origin), x, z.Cross(x));
        }

        /// <summary>β: the angle from the copy's x-axis to R(x of the original's) about the copy's normal.</summary>
        private static double Beta(Frame3 original, Frame3 copy, Reflection r)
        {
            Vec3 rx = r.ApplyVector(original.XAxis);
            return Math.Atan2(rx.Dot(copy.YAxis), rx.Dot(copy.XAxis));
        }

        [Fact]
        public void The_rotation_rule_holds_against_random_frames()
        {
            // checks.py #1: R ∘ F ∘ Rz(θ) = F′ ∘ Rz(θ′) ∘ S_α for a tool with S_α(G) = G.
            var rnd = new Random(7);
            double worst = 0;
            for (int i = 0; i < 2000; i++)
            {
                Frame3 f = TestGeometry.RandomFrame(rnd);
                var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
                double theta = (rnd.NextDouble() * 2 - 1) * Math.PI, alpha = (rnd.NextDouble() * 2 - 1) * Math.PI;
                Frame3 f2 = MirroredPlacement(f, r, rnd);
                double theta2 = FormToolPlacement.MirroredRotation(theta, alpha, Beta(f, f2, r));
                Vec3 g = TestGeometry.RandomPoint(rnd, 0.05);
                Vec3 lhs = r.Apply(f.ToWorld(Rz(g, theta)));
                Vec3 rhs = f2.ToWorld(Rz(S(g, alpha), theta2));
                worst = Math.Max(worst, lhs.DistanceTo(rhs));
            }
            Assert.True(worst < 1e-12, $"größte Abweichung {worst}");
        }

        [Fact]
        public void The_rotation_rule_in_its_special_cases()
        {
            // α = 0 → −θ; α = π/2 → π − θ; β = π turns the copy's sketch half round.
            TestGeometry.Near(-0.3, FormToolPlacement.MirroredRotation(0.3, 0, 0));
            TestGeometry.Near(Math.PI - 0.3, FormToolPlacement.MirroredRotation(0.3, Math.PI / 2, 0));
            TestGeometry.Near(Math.PI - 0.3, FormToolPlacement.MirroredRotation(0.3, 0, Math.PI));
            TestGeometry.Near(-0.3, FormToolPlacement.MirroredRotation(0.3, Math.PI / 2, Math.PI));
            // Always in (−π, π].
            TestGeometry.Near(Math.PI, FormToolPlacement.Normalize(-Math.PI));
            TestGeometry.Near(-Math.PI / 2, FormToolPlacement.Normalize(3 * Math.PI / 2));
            TestGeometry.Near(0.1, FormToolPlacement.Normalize(0.1 + 4 * Math.PI), 1e-12);
        }

        /// <summary>A tool as segments in its own coordinates (z along the outward normal of the face it stands on).</summary>
        private sealed class Tool
        {
            public Tool(params (Vec3 a, Vec3 b)[] segments)
            {
                Segments = segments;
            }

            public (Vec3 a, Vec3 b)[] Segments { get; }

            /// <summary>Points along its segments, tool coordinates.</summary>
            public IEnumerable<Vec3> Points(params double[] at) => Segments.SelectMany(s => at.Select(t => s.a + (s.b - s.a) * t));

            /// <summary>The distance of a world point from the tool placed in <paramref name="frame"/> at <paramref name="angle"/>.</summary>
            public double Distance(Frame3 frame, double angle, Vec3 p)
            {
                Vec3 local = Rz(frame.ToLocal(p), -angle);
                return Segments.Min(s => SegmentDistance(local, s.a, s.b));
            }

            private static double SegmentDistance(Vec3 p, Vec3 a, Vec3 b)
            {
                Vec3 d = b - a;
                double t = Math.Max(0, Math.Min(1, (p - a).Dot(d) / d.Dot(d)));
                return (a + d * t).DistanceTo(p);
            }
        }

        private const double H = 0.003;

        /// <summary>Two arms of 20 mm at 3 mm height and a post on the axis: mirror-symmetric about its diagonal (α = 45°).</summary>
        private static readonly Tool SymmetricL = new Tool(
            (new Vec3(0, 0, H), new Vec3(0.02, 0, H)), (new Vec3(0, 0, H), new Vec3(0, 0.02, H)), (Vec3.Zero, new Vec3(0, 0, H)));

        /// <summary>Arms of 20 and 12 mm: no mirror plane through the axis – the mirror image is no rotation of it (checks.py #2).</summary>
        private static readonly Tool ChiralL = new Tool(
            (new Vec3(0, 0, H), new Vec3(0.02, 0, H)), (new Vec3(0, 0, H), new Vec3(0, 0.012, H)), (Vec3.Zero, new Vec3(0, 0, H)));

        /// <summary>Four arms of 15 mm: symmetric under a quarter turn and mirror-symmetric.</summary>
        private static readonly Tool Cross = new Tool(
            (new Vec3(-0.015, 0, H), new Vec3(0.015, 0, H)), (new Vec3(0, -0.015, H), new Vec3(0, 0.015, H)));

        /// <summary>
        /// The original placed in a random frame at θ, mirrored: the mirrored samples (on the original's faces), the probe
        /// (the copy at angle 0 in its own frame), the axis of the instance in the copy.
        /// </summary>
        private static (List<Vec3> wanted, Frame3 copy, Line3 axis, double beta, double theta) Placed(Tool tool, int seed)
        {
            var rnd = new Random(seed);
            Frame3 f = TestGeometry.RandomFrame(rnd);
            var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
            double theta = (rnd.NextDouble() * 2 - 1) * Math.PI;
            Frame3 copy = MirroredPlacement(f, r, rnd);
            List<Vec3> wanted = tool.Points(0.3, 0.6, 1.0).Select(g => r.Apply(f.ToWorld(Rz(g, theta)))).ToList();
            return (wanted, copy, new Line3(copy.Origin, copy.ZAxis), Beta(f, copy, r), theta);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void The_3D_registration_finds_the_rotation_of_a_symmetric_tool(int seed)
        {
            var (wanted, copy, axis, beta, theta) = Placed(SymmetricL, seed);
            var axes = wanted.Select(_ => axis).ToList();
            double? psi = FormToolPlacement.Register3D(wanted, axes, p => SymmetricL.Distance(copy, 0, p));
            Assert.NotNull(psi);
            // What the rule says (α = 45°), now measured.
            TestGeometry.Near(0, FormToolPlacement.Normalize(psi!.Value - FormToolPlacement.MirroredRotation(theta, Math.PI / 4, beta)), 1e-5);
            // Placed again at the angle found (sense +1 here: the tool turns right-handed about the outward normal), every sample lies on it ...
            double final = FormToolPlacement.FinalAngle(0, psi.Value, 1);
            Assert.True(FormToolPlacement.Fits(wanted, p => SymmetricL.Distance(copy, final, p)));
            // ... and in the other sense it does not.
            double wrong = FormToolPlacement.FinalAngle(0, psi.Value, -1);
            Assert.False(FormToolPlacement.Fits(wanted, p => SymmetricL.Distance(copy, wrong, p)));
            Assert.True(FormToolPlacement.Deviation(wanted, p => SymmetricL.Distance(copy, wrong, p)) > FormToolPlacement.CheckTolerance);
        }

        [Fact]
        public void A_chiral_tool_fits_no_rotation()
        {
            // checks.py #2: the mirror image of a tool without a mirror plane through its axis is no rotation of it.
            var (wanted, copy, axis, _, _) = Placed(ChiralL, 4);
            Assert.Null(FormToolPlacement.Register3D(wanted, wanted.Select(_ => axis).ToList(), p => ChiralL.Distance(copy, 0, p)));
            // Another tool (a configuration of other size) fits no rotation either.
            var (other, copy2, axis2, _, _) = Placed(SymmetricL, 5);
            var smaller = new Tool((new Vec3(0, 0, H), new Vec3(0.015, 0, H)), (new Vec3(0, 0, H), new Vec3(0, 0.015, H)), (Vec3.Zero, new Vec3(0, 0, H)));
            Assert.Null(FormToolPlacement.Register3D(other, other.Select(_ => axis2).ToList(), p => smaller.Distance(copy2, 0, p)));
        }

        [Fact]
        public void A_rotationally_symmetric_tool_takes_the_smallest_angle()
        {
            var (wanted, copy, axis, _, _) = Placed(Cross, 6);
            double? psi = FormToolPlacement.Register3D(wanted, wanted.Select(_ => axis).ToList(), p => Cross.Distance(copy, 0, p));
            Assert.NotNull(psi);
            Assert.True(Math.Abs(psi!.Value) <= Math.PI / 4 + 1e-6, $"ψ = {psi}");
            Assert.True(FormToolPlacement.Fits(wanted, p => Cross.Distance(copy, psi.Value, p)));
        }

        [Fact]
        public void A_good_guess_is_refined_and_spares_the_grid()
        {
            var (wanted, copy, axis, beta, theta) = Placed(SymmetricL, 8);
            var axes = wanted.Select(_ => axis).ToList();
            double truth = FormToolPlacement.MirroredRotation(theta, Math.PI / 4, beta);
            int calls = 0;
            double? psi = FormToolPlacement.Register3D(wanted, axes, p =>
            {
                calls++;
                return SymmetricL.Distance(copy, 0, p);
            }, new[] { truth + 0.004 });
            TestGeometry.Near(0, FormToolPlacement.Normalize(psi!.Value - truth), 1e-5);
            Assert.True(calls < 3000, $"{calls} Abstandsmessungen");
            // A wrong guess costs nothing but itself: the grid still finds it.
            Assert.NotNull(FormToolPlacement.Register3D(wanted, axes, p => SymmetricL.Distance(copy, 0, p), new[] { truth + 1.0 }));
            // Nothing to register with.
            Assert.Null(FormToolPlacement.Register3D(new List<Vec3>(), new List<Line3>(), _ => 0));
        }

        [Fact]
        public void Several_instances_turn_alike_about_their_own_axes()
        {
            // Two instances of one tool: every instance has its own insertion axis, one angle for all.
            var rnd = new Random(9);
            Frame3 f = TestGeometry.RandomFrame(rnd);
            var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
            const double theta = 0.7;
            var second = new Frame3(f.ToWorld(0.06, 0.01), f.XAxis, f.YAxis);
            Frame3 copy1 = MirroredPlacement(f, r, rnd);
            var copy2 = new Frame3(r.Apply(second.Origin), copy1.XAxis, copy1.YAxis);
            var wanted = new List<Vec3>();
            var axes = new List<Line3>();
            foreach (Frame3 placed in new[] { f, second })
                foreach (Vec3 g in SymmetricL.Points(0.5, 1.0))
                {
                    Vec3 w = r.Apply(placed.ToWorld(Rz(g, theta)));
                    wanted.Add(w);
                    Frame3 c = placed.Origin.AlmostEquals(f.Origin) ? copy1 : copy2;
                    axes.Add(new Line3(c.Origin, c.ZAxis));
                }
            double Distance(Vec3 p, double angle) => Math.Min(SymmetricL.Distance(copy1, angle, p), SymmetricL.Distance(copy2, angle, p));
            double? psi = FormToolPlacement.Register3D(wanted, axes, p => Distance(p, 0));
            Assert.NotNull(psi);
            Assert.True(FormToolPlacement.Fits(wanted, p => Distance(p, psi!.Value)));
        }

        [Fact]
        public void The_2D_registration_of_the_orientation_points()
        {
            var rnd = new Random(11);
            Frame3 f = TestGeometry.RandomFrame(rnd);
            var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
            const double theta = -1.1;
            Frame3 copy = MirroredPlacement(f, r, rnd);
            var axis = new Line3(copy.Origin, copy.ZAxis);
            // An outline symmetric about the tool's x-axis (α = 0): its mirror image is a rotation of it.
            var outline = new[] { new Vec3(0.01, 0, 0), new Vec3(-0.005, 0.004, 0), new Vec3(-0.005, -0.004, 0) };
            List<Vec3> wanted = outline.Select(g => r.Apply(f.ToWorld(Rz(g, theta)))).ToList();
            List<Vec3> actual = outline.Select(copy.ToWorld).ToList();
            List<double> psi = FormToolPlacement.Register2D(wanted, actual, axis);
            Assert.Single(psi);
            TestGeometry.Near(0, FormToolPlacement.Normalize(psi[0] - FormToolPlacement.MirroredRotation(theta, 0, Beta(f, copy, r))), 1e-6);
            // An asymmetric outline mirrored is no rotation of itself; another number of points is none either.
            var lopsided = new[] { new Vec3(0.01, 0, 0), new Vec3(-0.005, 0.006, 0), new Vec3(-0.005, -0.003, 0) };
            Assert.Empty(FormToolPlacement.Register2D(lopsided.Select(g => r.Apply(f.ToWorld(Rz(g, theta)))).ToList(), lopsided.Select(copy.ToWorld).ToList(), axis));
            Assert.Empty(FormToolPlacement.Register2D(wanted.Take(2).ToList(), actual, axis));
            Assert.Empty(FormToolPlacement.Register2D(new List<Vec3>(), new List<Vec3>(), axis));
            // A square: four rotations fit, the smallest first.
            var square = new[] { new Vec3(0.01, 0.01, 0), new Vec3(-0.01, 0.01, 0), new Vec3(-0.01, -0.01, 0), new Vec3(0.01, -0.01, 0) };
            List<double> four = FormToolPlacement.Register2D(square.Select(g => r.Apply(f.ToWorld(Rz(g, theta)))).ToList(), square.Select(copy.ToWorld).ToList(), axis);
            Assert.Equal(4, four.Count);
            Assert.True(Math.Abs(four[0]) <= Math.PI / 4 + 1e-9);
        }

        [Fact]
        public void The_side_the_instances_stand_on()
        {
            var face = new Plane3(Vec3.Zero, Vec3.UnitZ);
            const double t = 0.001;
            // A dimple pushed out: its outer face up to 3 mm, its inner face from the back of the sheet up to 2 mm.
            var outward = new[] { new Vec3(0, 0, 0.003), new Vec3(0.004, 0, 0.0015), new Vec3(0, 0, 0.002), new Vec3(0.006, 0, -t) };
            Assert.Equal(1, FormToolPlacement.ProtrusionSide(outward, face, t));
            // Pushed through the sheet: its outer face from the face down, its inner face beyond the back.
            var inward = new[] { new Vec3(0, 0, -0.003), new Vec3(0.004, 0, -0.0015), new Vec3(0, 0, -0.004), new Vec3(0.006, 0, 0) };
            Assert.Equal(-1, FormToolPlacement.ProtrusionSide(inward, face, t));
            // A lance's cut walls within the sheet say nothing; nothing at all neither.
            Assert.Equal(0, FormToolPlacement.ProtrusionSide(new[] { new Vec3(0, 0, -0.0005), new Vec3(0.001, 0, 0) }, face, t));
            Assert.Equal(0, FormToolPlacement.ProtrusionSide(Array.Empty<Vec3>(), face, t));
            // A mirror keeps outward normals outward, so the side: the mirrored points against the mirrored face.
            var r = new Reflection(new Plane3(new Vec3(0.1, 0.2, 0), new Vec3(1, 1, 0.3)));
            Assert.Equal(1, FormToolPlacement.ProtrusionSide(outward.Select(r.Apply), r.Apply(face), t));
            // Without the thickness the inner face counts as reaching through: the farther side still decides.
            Assert.Equal(1, FormToolPlacement.ProtrusionSide(outward, face, 0));
        }

        [Fact]
        public void Samples_go_to_their_instance_and_spare_the_axis()
        {
            var centres = new[] { Vec3.Zero, new Vec3(0.1, 0, 0) };
            Vec3 n = Vec3.UnitZ;
            var points = new List<Vec3>
            {
                new Vec3(0, 0, 0.003),                                // on the first axis: no rotation in it
                new Vec3(0.02, 0, 0.003), new Vec3(0.02, 0, 0.003),   // twice the same point
                new Vec3(0, 0.015, 0.003), new Vec3(0.0005, 0, 0.003), // the second within 10 % of the largest distance
                new Vec3(0.12, 0, 0.003), new Vec3(0.1, 0.01, 0.003), new Vec3(0.1, 0.0099, 0.003),
            };
            List<(Vec3 point, int instance)> picked = FormToolPlacement.PickSamples(points, centres, n);
            Assert.DoesNotContain(picked, s => s.point.AlmostEquals(new Vec3(0, 0, 0.003)));
            Assert.DoesNotContain(picked, s => s.point.AlmostEquals(new Vec3(0.0005, 0, 0.003)));
            Assert.Single(picked, s => s.point.AlmostEquals(new Vec3(0.02, 0, 0.003)));
            // Each sample with its nearest instance; every instance gets one before any gets two, the farthest first.
            Assert.All(picked, s => Assert.Equal(FormToolPlacement.NearestInstance(s.point, centres), s.instance));
            Assert.Equal(new[] { 0, 1 }, picked.Take(2).Select(s => s.instance));
            TestGeometry.Near(new Vec3(0.02, 0, 0.003), picked[0].point);
            TestGeometry.Near(new Vec3(0.12, 0, 0.003), picked[1].point);
            Assert.Equal(5, picked.Count);
            // The spread: of two points close together the second comes after the others.
            Assert.Equal(new Vec3(0.1, 0.0099, 0.003), picked.Last().point);
            // A cap.
            Assert.Equal(3, FormToolPlacement.PickSamples(points, centres, n, 3).Count);
            Assert.Empty(FormToolPlacement.PickSamples(points, Array.Empty<Vec3>(), n));
        }

        [Fact]
        public void Many_instances_get_one_sample_each()
        {
            var centres = Enumerable.Range(0, 40).Select(i => new Vec3(i * 0.05, 0, 0)).ToList();
            var points = centres.SelectMany(c => new[] { c + new Vec3(0.01, 0, 0.002), c + new Vec3(0, 0.008, 0.002) }).ToList();
            List<(Vec3 point, int instance)> picked = FormToolPlacement.PickSamples(points, centres, Vec3.UnitZ);
            Assert.Equal(40, picked.Count);
            Assert.Equal(Enumerable.Range(0, 40), picked.Select(s => s.instance).OrderBy(i => i));
        }

        [Fact]
        public void Final_angle_and_rotation_senses()
        {
            TestGeometry.Near(-0.5, FormToolPlacement.FinalAngle(0, 0.5, -1));
            TestGeometry.Near(0.7, FormToolPlacement.FinalAngle(0.2, 0.5, 1));
            TestGeometry.Near(-Math.PI + 0.1, FormToolPlacement.FinalAngle(Math.PI - 0.2, 0.3, 1), 1e-12);
            Assert.Equal(new[] { 1, -1 }, FormToolPlacement.Senses(null));
            Assert.Equal(new[] { 1, -1 }, FormToolPlacement.Senses(1));
            Assert.Equal(new[] { -1, 1 }, FormToolPlacement.Senses(-1));
            // Rotating about a line: right hand about its direction.
            TestGeometry.Near(new Vec3(1, 2, 0.5), FormToolPlacement.Rotate(new Vec3(2, 1, 0.5), new Line3(new Vec3(1, 1, 0), Vec3.UnitZ), Math.PI / 2));
        }

        [Fact]
        public void Configuration_candidates_and_tool_files()
        {
            // Before 2025 the tool's configuration is not readable: the default first, then the file's (A4 P11).
            Assert.Equal(new[] { "", "Default", "Large" }, FormToolPlacement.ConfigurationCandidates(null, new[] { "Default", "Large" }));
            Assert.Equal(new[] { "Large", "", "Default" }, FormToolPlacement.ConfigurationCandidates("Large", new[] { "Default", "Large" }));
            Assert.Equal(new[] { "" }, FormToolPlacement.ConfigurationCandidates(null, null));
            Assert.Equal(FormToolPlacement.MaxConfigurations,
                FormToolPlacement.ConfigurationCandidates(null, Enumerable.Range(1, 20).Select(i => $"K{i}")).Count);

            Assert.Equal("louver", FormToolPlacement.ToolName("louver1"));
            Assert.Equal("counter sink emboss", FormToolPlacement.ToolName("counter sink emboss12"));
            Assert.Equal("dimple", FormToolPlacement.ToolName("dimple"));
            Assert.Null(FormToolPlacement.ToolName("17"));
            Assert.Equal(new[] { "louver.sldftp", "louver.sldprt" }, FormToolPlacement.LibraryFileNames("louver3"));

            const string lib = @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2019\design library\forming tools\louvers\";
            Assert.Equal("louver.sldftp", FormToolPlacement.FileName(lib + "louver.sldftp"));
            Assert.Equal("louver", FormToolPlacement.ToolOf(lib + "louver.sldftp"));
            Assert.Equal("dimple", FormToolPlacement.ToolOf("/mnt/tools/dimple.SLDFTP"));
            // A forming tool named like the feature, then any forming tool, then a part.
            Assert.Equal(lib + "louver.sldftp",
                FormToolPlacement.PickToolPath(new[] { @"C:\parts\Teil.SLDPRT", lib + "other.sldftp", lib + "louver.sldftp" }, "louver2"));
            Assert.Equal(lib + "other.sldftp", FormToolPlacement.PickToolPath(new[] { @"C:\parts\louver.SLDPRT", lib + "other.sldftp" }, "louver2"));
            Assert.Equal(@"C:\parts\louver.SLDPRT", FormToolPlacement.PickToolPath(new[] { @"C:\parts\Teil.sldprt", @"C:\parts\louver.SLDPRT" }, "louver2"));
            Assert.Null(FormToolPlacement.PickToolPath(new[] { null, "", @"C:\x\y.sldasm" }, "louver2"));
        }

        [Fact]
        public void Report_texts()
        {
            var r = new FormToolRecord("louver1", "FormToolInstance", "Skizze3", new SketchModel(), "Skizze4") { ToolPath = @"C:\lib\louver.sldftp" };
            r.Points.Add(Vec3.Zero);
            r.Points.Add(Vec3.UnitX);
            Assert.Equal("Umformwerkzeug „louver“ an 2 Stellen, Drehung 30° (gemessen)", FormToolPlacement.Describe(r, Math.PI / 6, De));
            Assert.Equal("Werkzeug passt nicht (andere Datei oder Konfiguration, oder unsymmetrisches Werkzeug).", FormToolPlacement.NoFit);
            Assert.Equal("Standardkonfiguration, nicht umgekehrt: passt in keiner Drehung.", FormToolPlacement.Tried("", false, "passt in keiner Drehung."));
            Assert.Equal("Konfiguration „Large“, umgekehrt: x", FormToolPlacement.Tried("Large", true, "x"));
            Assert.Equal("Form weicht um 0,02 mm von der gespiegelten ab.", FormToolPlacement.Off(2e-5, De));
            Assert.Equal("Die Positionsskizze der Kopie hat 1 statt 2 Punkte.", FormToolPlacement.CountOff(1, 2));
            Assert.Equal("um -30° gedreht nicht eingefügt.", FormToolPlacement.TurnedNotInserted(-Math.PI / 6, De));
            Assert.Equal("um 30° gedreht: Form weicht um 0,02 mm ab.", FormToolPlacement.Turned(Math.PI / 6, 2e-5, culture: De));
            Assert.Equal("am Feature um 30° gedreht: Form weicht um 0,02 mm ab.", FormToolPlacement.Turned(Math.PI / 6, 2e-5, true, De));
        }

        [Fact]
        public void Link_info_says_when_it_is_inferred()
        {
            var r = new FormToolRecord("louver1", "FormToolInstance", "Skizze3", new SketchModel()) { ToolPath = @"C:\lib\louver.sldftp" };
            Assert.Null(FormToolPlacement.Link(r));
            // Read from the data (2025 SP2 and later).
            r.LinkToTool = true;
            Assert.Equal(@"Mit der Werkzeugdatei „C:\lib\louver.sldftp“ verknüpft wie das Original (externer Bezug).", FormToolPlacement.Link(r));
            // Before 2025 SP2: inferred from the reference (B70) – the report says so.
            r.LinkInferred = true;
            Assert.Equal(@"Mit der Werkzeugdatei „C:\lib\louver.sldftp“ verknüpft (externer Bezug), weil das Original sie als Bezug nennt – " +
                         "ob das Original verknüpft ist, lässt sich vor SOLIDWORKS 2025 SP2 nicht lesen.", FormToolPlacement.Link(r));
        }

        [Fact]
        public void Display_info_only_where_the_display_is_not_read_or_overridden()
        {
            var r = new FormToolRecord("louver1", "FormToolInstance", "Skizze3", new SketchModel());
            // Not readable (before 2025 SP2): the document's setting, said as information.
            Assert.Equal(FormToolPlacement.DisplayDefaults, FormToolPlacement.Display(r));
            Assert.Contains("vor SOLIDWORKS 2025 SP2 nicht lesbar", FormToolPlacement.DisplayDefaults);
            // Read, following the document: the copy does the same, nothing to say.
            r.OverrideDisplay = false;
            Assert.Null(FormToolPlacement.Display(r));
            // Read, overridden: the copy gets the same flags.
            r.OverrideDisplay = true;
            r.ShowPunch = true;
            r.ShowCenter = true;
            Assert.Equal("Anzeige in der Abwicklung wie im Original: Stempel ein, Profil aus, Mittelpunkt ein.", FormToolPlacement.Display(r));
        }
    }
}
