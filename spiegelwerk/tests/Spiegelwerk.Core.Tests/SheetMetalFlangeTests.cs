using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Hem and miter flange (blech-plan S2, A2 §11 #10, #13, #15, #16, P8, P9): width sketches, selections, read-back, bends, relief.</summary>
    public class SheetMetalFlangeTests
    {
        private static readonly NumberFormatInfo Comma = new NumberFormatInfo { NumberDecimalSeparator = ",", NegativeSign = "-" };

        private static EdgeSignature Line(Vec3 a, Vec3 b) => new EdgeSignature(CurveKind.Line, a, b, (a + b) / 2, a.DistanceTo(b)) { Axis = (b - a).Normalized() };

        /// <summary>A sketch in <paramref name="frame"/> with the given lines (local 2D coordinates); construction lines optional.</summary>
        private static SketchModel Sketch(Frame3 frame, string name, IEnumerable<(double x1, double y1, double x2, double y2)> lines,
            IEnumerable<(double x1, double y1, double x2, double y2)>? construction = null)
        {
            var s = new SketchModel { Name = name, Frame = frame };
            int n = 0;
            void Add((double x1, double y1, double x2, double y2) l, bool c)
            {
                string a = $"P{n}a", b = $"P{n}b";
                s.Points.Add(new SkPoint(a, l.x1, l.y1));
                s.Points.Add(new SkPoint(b, l.x2, l.y2));
                s.Segments.Add(new SkSegment($"S{n}", SkSegmentKind.Line) { StartId = a, EndId = b, Construction = c });
                n++;
            }
            foreach (var l in lines) Add(l, false);
            foreach (var l in construction ?? Enumerable.Empty<(double, double, double, double)>()) Add(l, true);
            return s;
        }

        // ------------------------------------------------------------ hem width sketches (A2 #10)

        [Fact]
        public void Hem_width_sketch_spanning_the_whole_edge_is_default()
        {
            EdgeSignature edge = Line(new Vec3(0, 0, 0), new Vec3(0.1, 0, 0));
            Frame3 f = TestGeometry.Front;
            Assert.True(FlangeProfiles.IsFullEdgeLine(Sketch(f, "Skizze1", new[] { (0.0, 0.0, 0.1, 0.0) }), edge));
            // Either way round; construction lines do not count.
            Assert.True(FlangeProfiles.IsFullEdgeLine(Sketch(f, "Skizze1", new[] { (0.1, 0.0, 0.0, 0.0) }, new[] { (0.0, 0.0, 0.0, 0.02) }), edge));
            // A partial hem, a second line, a line beside the edge: not the default.
            Assert.False(FlangeProfiles.IsFullEdgeLine(Sketch(f, "Skizze1", new[] { (0.01, 0.0, 0.09, 0.0) }), edge));
            Assert.False(FlangeProfiles.IsFullEdgeLine(Sketch(f, "Skizze1", new[] { (0.0, 0.0, 0.05, 0.0), (0.05, 0.0, 0.1, 0.0) }), edge));
            Assert.False(FlangeProfiles.IsFullEdgeLine(Sketch(f, "Skizze1", new[] { (0.0, 0.001, 0.1, 0.001) }), edge));
            // Within the length tolerance.
            Assert.True(FlangeProfiles.IsFullEdgeLine(Sketch(f, "Skizze1", new[] { (0.0, 0.0, 0.1 + 5e-7, 0.0) }), edge));
        }

        [Fact]
        public void Two_width_sketches_pair_with_two_edges()
        {
            // Two edges of a plate meeting at a corner; the sketches are listed the other way round.
            var edges = new[] { Line(new Vec3(0, 0, 0), new Vec3(0.1, 0, 0)), Line(new Vec3(0.1, 0, 0), new Vec3(0.1, 0.05, 0)) };
            Frame3 f = TestGeometry.Front;
            var sketches = new[]
            {
                new ProfileCandidate("Skizze8", Sketch(f, "Skizze8", new[] { (0.1, 0.01, 0.1, 0.04) })),   // a partial hem on the second edge
                new ProfileCandidate("Skizze7", Sketch(f, "Skizze7", new[] { (0.0, 0.0, 0.1, 0.0) })),
            };
            Assert.Equal(new[] { 1, 0 }, FlangeProfiles.Assign(edges, sketches));

            // The edge the sketch was made on wins over its lines.
            var supported = new ProfileCandidate("Skizze9", Sketch(f, "Skizze9", new[] { (0.0, 0.0, 0.1, 0.0) }), edges[1]);
            Assert.Equal(new[] { 1 }, FlangeProfiles.Assign(edges, new[] { supported }));
        }

        [Fact]
        public void Width_sketches_without_a_unique_edge_are_not_paired()
        {
            Frame3 f = TestGeometry.Front;
            var edges = new[] { Line(new Vec3(0, 0, 0), new Vec3(0.1, 0, 0)), Line(new Vec3(0.1, 0, 0), new Vec3(0.1, 0.05, 0)) };
            // On neither edge.
            Assert.Null(FlangeProfiles.Assign(edges, new[] { new ProfileCandidate("S", Sketch(f, "S", new[] { (0.0, 0.02, 0.1, 0.02) })) }));
            // Two sketches on one edge.
            var twice = new[]
            {
                new ProfileCandidate("A", Sketch(f, "A", new[] { (0.0, 0.0, 0.05, 0.0) })),
                new ProfileCandidate("B", Sketch(f, "B", new[] { (0.05, 0.0, 0.1, 0.0) })),
            };
            Assert.Null(FlangeProfiles.Assign(edges, twice));
            // Two collinear edges (a slot in between) and one sketch across both: no unique edge.
            var collinear = new[] { Line(new Vec3(0, 0, 0), new Vec3(0.04, 0, 0)), Line(new Vec3(0.06, 0, 0), new Vec3(0.1, 0, 0)) };
            Assert.Null(FlangeProfiles.Assign(collinear, new[] { new ProfileCandidate("S", Sketch(f, "S", new[] { (0.0, 0.0, 0.1, 0.0) })) }));
            // Touching an edge at its end is no overlap.
            Assert.False(FlangeProfiles.LiesOn((new Vec3(0.1, 0, 0), new Vec3(0.2, 0, 0)), edges[0]));
        }

        [Fact]
        public void Width_sketch_pairing_and_default_hold_for_the_mirror_image()
        {
            var rnd = new Random(23);
            for (int k = 0; k < 200; k++)
            {
                var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
                Frame3 f = TestGeometry.RandomFrame(rnd);
                var edges = new[] { Line(f.ToWorld(0, 0), f.ToWorld(0.1, 0)), Line(f.ToWorld(0.1, 0), f.ToWorld(0.1, 0.05)) };
                SketchModel full = Sketch(f, "A", new[] { (0.0, 0.0, 0.1, 0.0) });
                SketchModel part = Sketch(f, "B", new[] { (0.1, 0.01, 0.1, 0.04) });

                // The copy: every point reflected (frame with mirrored X and Y, same local coordinates).
                Frame3 mf = r.ApplyKeepingXY(f);
                EdgeSignature[] mirrored = edges.Select(e => e.Mirror(r)).ToArray();
                SketchModel mFull = Sketch(mf, "A", new[] { (0.0, 0.0, 0.1, 0.0) });
                SketchModel mPart = Sketch(mf, "B", new[] { (0.1, 0.01, 0.1, 0.04) });

                Assert.True(FlangeProfiles.IsFullEdgeLine(full, edges[0], 1e-9));
                Assert.True(FlangeProfiles.IsFullEdgeLine(mFull, mirrored[0], 1e-9));
                Assert.False(FlangeProfiles.IsFullEdgeLine(mPart, mirrored[1], 1e-9));
                Assert.Equal(FlangeProfiles.Assign(edges, new[] { new ProfileCandidate("B", part), new ProfileCandidate("A", full) }, 1e-9),
                    FlangeProfiles.Assign(mirrored, new[] { new ProfileCandidate("B", mPart), new ProfileCandidate("A", mFull) }, 1e-9));
            }
        }

        // ------------------------------------------------------------ miter flange (A2 #13, P8)

        [Fact]
        public void Miter_swap_alternative_only_for_unequal_offsets()
        {
            Assert.False(MiterRules.OffersSwap(0, 0));
            Assert.False(MiterRules.OffersSwap(0.005, 0.005 + 5e-7));
            Assert.True(MiterRules.OffersSwap(0.005, 0));
            Assert.True(MiterRules.OffersSwap(0, 0.002));
        }

        [Fact]
        public void Miter_mark0_retry_only_for_single_edge_chains()
        {
            // With the edges on mark 0 SOLIDWORKS made one flange only (CADapter): never for a chain.
            Assert.Contains(MiterSelection.AllOnMark0, MiterRules.Variants(1));
            foreach (int n in new[] { 2, 3, 4 })
            {
                IReadOnlyList<MiterSelection> v = MiterRules.Variants(n);
                Assert.DoesNotContain(MiterSelection.AllOnMark0, v);
                Assert.Equal(new[] { MiterSelection.SketchFirst, MiterSelection.ChainFirst }, v);
            }
            // The verified selection first: sketch on 0, edges on 1.
            Assert.Equal(MiterSelection.SketchFirst, MiterRules.Variants(1)[0]);
            Assert.Equal((0, 1, false), MiterRules.Marks(MiterSelection.SketchFirst));
            Assert.Equal((0, 1, true), MiterRules.Marks(MiterSelection.ChainFirst));
            Assert.Equal((0, 0, false), MiterRules.Marks(MiterSelection.AllOnMark0));
            Assert.Equal("Gehrungslasche mit 1 statt 3 Kanten entstanden; wieder entfernt.", MiterRules.EdgeCountMismatch(3, 1));
        }

        private static MiterFlangeRecord Miter() => new MiterFlangeRecord("Gehrungslasche1", "SMMiteredFlange", "Skizze5")
        {
            UseDefaultRadius = false,
            BendRadius = 0.001,
            PositionType = 1,
            GapDistance = 0.0005,
            StartOffset = 0.002,
            EndOffset = 0,
            OwnRelief = new ReliefData { Kind = ReliefKind.Obround, UseRatio = true, Ratio = 0.5 },
        };

        [Fact]
        public void Miter_differences_list_only_deviating_values()
        {
            MiterFlangeRecord want = Miter(), got = Miter();
            got.BendRadius += 5e-10;
            Assert.True(MiterRules.Differences(want, got).None);

            got = Miter();
            got.StartOffset = 0;
            got.EndOffset = 0.002;
            got.OwnRelief = null;
            Deviations d = MiterRules.Differences(want, got, Comma);
            Assert.Equal(new[] { "Startversatz: erwartet 2 mm, gelesen 0 mm", "Endversatz: erwartet 0 mm, gelesen 2 mm" }, d.Differences.Take(2));
            Assert.Contains(d.Differences, x => x.StartsWith("Entlastung: erwartet eigene (Langloch, Verhältnis 0,5)"));
            Assert.Equal(3, d.Differences.Count);

            // The radius value counts only where the flange has its own.
            want.UseDefaultRadius = got.UseDefaultRadius = true;
            got = Miter();
            got.UseDefaultRadius = true;
            got.BendRadius = 0.003;
            want = Miter();
            want.UseDefaultRadius = true;
            Assert.True(MiterRules.Differences(want, got).None);
        }

        // ------------------------------------------------------------ hem values and relief (P9)

        private static HemRecord Hem(int type) => new HemRecord("Saum1", "Hem")
        {
            HemType = type,
            Position = 0,
            Length = 0.01,
            Gap = 0.0015,
            Angle = Math.PI,
            Radius = 0.002,
            MiterGap = 0.0005,
        };

        [Fact]
        public void Hem_values_follow_the_hem_type()
        {
            Assert.Equal(new[] { "Saumlänge", "Gehrungsspalt" }, HemRules.Values(Hem(HemTypes.Closed)).Select(v => v.name));
            Assert.Equal(new[] { "Saumlänge", "Spaltabstand", "Gehrungsspalt" }, HemRules.Values(Hem(HemTypes.Open)).Select(v => v.name));
            Assert.Equal(new[] { "Saumwinkel", "Saumradius", "Gehrungsspalt" }, HemRules.Values(Hem(HemTypes.Rolled)).Select(v => v.name));
            Assert.Equal(new[] { "Saumwinkel", "Saumradius", "Gehrungsspalt" }, HemRules.Values(Hem(HemTypes.TearDrop)).Select(v => v.name));

            // A closed hem does not use the radius: a different one read back is no deviation.
            HemRecord want = Hem(HemTypes.Closed), got = Hem(HemTypes.Closed);
            got.Radius = 0.005;
            Assert.Empty(HemRules.Differences(want, got));
            got.Length = 0.012;
            got.Reverse = true;
            Assert.Equal(new[] { "Saumrichtung umgekehrt: erwartet nein, gelesen ja", "Saumlänge: erwartet 10 mm, gelesen 12 mm" }, HemRules.Differences(want, got, Comma));
            // Another type: the type is the deviation, its values are not compared.
            Assert.Equal(new[] { "Saumart: erwartet 1, gelesen 3" }, HemRules.Differences(want, Hem(HemTypes.Rolled)));
            // A hem angle is not folded: 2π − θ is another hem (a tear-drop hem of 200° is not one of 160°).
            HemRecord rolled = Hem(HemTypes.Rolled), back = Hem(HemTypes.Rolled);
            rolled.Angle = 0.8 * Math.PI;
            back.Angle = 2 * Math.PI - rolled.Angle;
            Assert.Equal(new[] { "Saumwinkel: erwartet 144°, gelesen 216°" }, HemRules.Differences(rolled, back, Comma));
            HemRecord tearDrop = Hem(HemTypes.TearDrop), clamped = Hem(HemTypes.TearDrop);
            tearDrop.Angle = 200 * Math.PI / 180;
            clamped.Angle = 160 * Math.PI / 180;
            Assert.Equal(new[] { "Saumwinkel: erwartet 200°, gelesen 160°" }, HemRules.Differences(tearDrop, clamped, Comma));
            // A whole turn more is the same angle.
            back.Angle = rolled.Angle + 2 * Math.PI;
            Assert.Empty(HemRules.Differences(rolled, back));
            back.Angle = rolled.Angle - 2 * Math.PI;
            Assert.Empty(HemRules.Differences(rolled, back));
        }

        [Fact]
        public void Hem_relief_note_only_when_unverified()
        {
            // Checked against a measured reference: no note (a passing check says so, a failing one gets the hint).
            Assert.True(HemRules.ReliefCheckable(verifySteps: true, checkDecides: true, hasReference: true));
            Assert.Null(HemRules.ReliefNote(true));
            // Anything less is no check of the relief: then the note.
            foreach (var (verify, decides, reference) in new[] { (false, true, true), (true, false, true), (true, true, false), (false, false, false) })
            {
                bool checkable = HemRules.ReliefCheckable(verify, decides, reference);
                Assert.False(checkable);
                Assert.Equal(HemRules.ReliefUnchecked, HemRules.ReliefNote(checkable));
            }
            Assert.Contains("nicht geprüft", HemRules.ReliefUnchecked);
            Assert.Contains("geprüft", HemRules.ReliefChecked);
        }

        // ------------------------------------------------------------ bends (A2 #15, P5)

        [Fact]
        public void Bend_overrides_become_notes()
        {
            // The hem uses the default allowance; its bend has K 0,38 of its own.
            HemRecord hem = Hem(HemTypes.Closed);
            var bends = new[]
            {
                new BendValues { Name = "HemBend1", UseDefaultAllowance = false, Allowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.38 } },
            };
            List<BendOverride> overrides = BendOverrides.Differences(HemRules.Parent(hem), bends, Comma);
            Assert.Equal("Biegung „HemBend1“ hat eine eigene Biegezugabe (K 0,38); über die API nicht übertragbar.", overrides.Single().Note());

            // The hem has its own K, the bend just inherits it: nothing to say.
            hem.OwnAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.38 };
            Assert.Empty(BendOverrides.Differences(HemRules.Parent(hem), bends));

            // A miter flange with its own radius: a bend with the same radius is no override, one with another is.
            MiterFlangeRecord miter = Miter();
            var miterBends = new[]
            {
                new BendValues { Name = "Miterbend1", UseDefaultRadius = false, Radius = 0.001, UseDefaultRelief = false, Relief = miter.OwnRelief },
                new BendValues { Name = "Miterbend2", UseDefaultRadius = false, Radius = 0.003 },
            };
            Assert.Equal(new[] { "Miterbend2" }, BendOverrides.Differences(MiterRules.Parent(miter), miterBends).Select(o => o.Bend));
        }

        [Fact]
        public void Copy_bends_are_compared_by_what_differs_and_by_count()
        {
            var original = new List<BendOverride> { new BendOverride("EdgeBend2", "eine eigene Biegezugabe (K 0,38)") };
            // The copy cannot have it (no write): nothing more to say than the original's note.
            Assert.Empty(BendOverrides.UnexpectedInCopy(original, new BendOverride[0]));
            // Same values, other names (the copy names its bends itself).
            Assert.Empty(BendOverrides.UnexpectedInCopy(original, new[] { new BendOverride("EdgeBend7", "eine eigene Biegezugabe (K 0,38)") }));
            // Something the original has nowhere: named.
            Assert.Equal(new[] { "Biegung „HemBend3“ der Kopie hat eine eigene Entlastung (ausreißen); im Original hat keine Biegung das." },
                BendOverrides.UnexpectedInCopy(original, new[] { new BendOverride("HemBend3", "eine eigene Entlastung (ausreißen)") }));

            Assert.Null(BendOverrides.CountNote(2, 2));
            Assert.Equal("Die Kopie hat 1 Biegung, das Original 3.", BendOverrides.CountNote(3, 1));
            Assert.Equal("Die Kopie hat 2 Biegungen, das Original 1.", BendOverrides.CountNote(1, 2));
        }

        // ------------------------------------------------------------ records (A2 #16)

        [Fact]
        public void GeometryRef_mirroring_keeps_scalars()
        {
            // Only references are mirrored: an edge keeps kind and length, a vertex or face reference its kind; the scalars of the records stay.
            var r = new Reflection(new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX));
            EdgeSignature e = Line(new Vec3(0, 0, 0), new Vec3(0, 0.1, 0.02));
            EdgeSignature m = e.Mirror(r);
            Assert.Equal(e.Kind, m.Kind);
            Assert.Equal(e.Length, m.Length, 12);
            TestGeometry.Near(new Vec3(0.1, 0, 0), m.Start);

            GeometryRef vertex = GeometryRef.ToVertex(new Vec3(0.01, 0.02, 0.03)).Mirror(r);
            Assert.Equal(GeometryRefKind.Vertex, vertex.Kind);
            TestGeometry.Near(new Vec3(0.09, 0.02, 0.03), vertex.Point);

            HemRecord hem = Hem(HemTypes.Open);
            hem.Edges.Add(e);
            var copy = new HemRecord(hem.Name, hem.TypeName) { HemType = hem.HemType, Length = hem.Length, Gap = hem.Gap, Angle = hem.Angle, Radius = hem.Radius, MiterGap = hem.MiterGap };
            copy.Edges.AddRange(hem.Edges.Select(x => x.Mirror(r)));
            Assert.Empty(HemRules.Differences(hem, copy));
        }

        [Fact]
        public void Hem_owns_its_width_sketches_and_the_miter_flange_uses_an_ordinary_sketch()
        {
            var hem = new HemRecord("Saum1", "Hem");
            Assert.False(hem.BringsParts);
            hem.WidthSketches.Add(new HemWidthSketch("Skizze7", new SketchModel(), 0));
            Assert.True(hem.BringsParts);
            Assert.True(hem.DeleteWithAbsorbed);
            Assert.Equal(new[] { "Skizze7" }, hem.OwnedSketches);
            Assert.True(hem.SampleSubFeatures);

            // Its profile is a record of its own and may be shared: removed again, the flange leaves it.
            var miter = new MiterFlangeRecord("Gehrungslasche1", "SMMiteredFlange", "Skizze5");
            Assert.False(miter.BringsParts);
            Assert.False(miter.DeleteWithAbsorbed);
            Assert.True(miter.SampleSubFeatures);
            Assert.True(miter.ChangesSolid);
        }
    }
}
