using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Normal Cut (blech-plan S6, A1 §5): groups mirrored face by face, compared as sets of sets.</summary>
    public class NormalCutTests
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        private static FaceSignature Plane(double area, Vec3 sample, Vec3 normal) => new FaceSignature(SurfaceKind.Plane, area, sample) { Normal = normal };

        /// <summary>Two groups: the slanted walls of two cuts through a plate.</summary>
        private static NormalCutRecord TwoGroups()
        {
            var r = new NormalCutRecord("Normal-Cut1", "NormalCut") { AutoPropagation = true, OptimizeGeometry = true };
            Vec3 slant = new Vec3(0.3, 0, 1).Normalized();
            r.Groups.Add(new List<FaceSignature>
            {
                Plane(1e-4, new Vec3(0.01, 0.02, 0.001), slant),
                Plane(1.2e-4, new Vec3(0.012, 0.025, 0.001), new Vec3(0, 0.3, 1).Normalized()),
            });
            r.Groups.Add(new List<FaceSignature> { new FaceSignature(SurfaceKind.Cylinder, 3e-5, new Vec3(0.05, 0.01, 0.001)) });
            return r;
        }

        /// <summary>The distance of a point to a face: its sample (the faces here are as small as their samples are apart).</summary>
        private static Func<int, int, Vec3, double> Distance(IReadOnlyList<IReadOnlyList<FaceSignature>> got) =>
            (g, k, p) => got[g][k].Sample.DistanceTo(p);

        [Fact]
        public void Normal_cut_groups_mirror_face_by_face()
        {
            NormalCutRecord r = TwoGroups();
            var rnd = new Random(6);
            for (int n = 0; n < 50; n++)
            {
                var m = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
                List<List<FaceSignature>> mirrored = NormalCutRules.MirroredGroups(r, m);
                Assert.Equal(r.Groups.Count, mirrored.Count);
                for (int g = 0; g < r.Groups.Count; g++)
                {
                    Assert.Equal(r.Groups[g].Count, mirrored[g].Count);
                    for (int k = 0; k < r.Groups[g].Count; k++)
                    {
                        FaceSignature o = r.Groups[g][k], c = mirrored[g][k];
                        Assert.Equal(o.Kind, c.Kind);
                        Assert.Equal(o.Area, c.Area);
                        TestGeometry.Near(m.Apply(o.Sample), c.Sample);
                        if (o.Kind == SurfaceKind.Plane) TestGeometry.Near(m.ApplyVector(o.Normal), c.Normal);
                    }
                }
                // The copy may list groups and their faces the other way round: still the same groups.
                IReadOnlyList<IReadOnlyList<FaceSignature>> wanted = mirrored.Select(g => (IReadOnlyList<FaceSignature>)g).ToList();
                IReadOnlyList<IReadOnlyList<FaceSignature>> reversed = mirrored.AsEnumerable().Reverse()
                    .Select(g => (IReadOnlyList<FaceSignature>)g.AsEnumerable().Reverse().ToList()).ToList();
                Assert.True(NormalCutRules.SameGroups(wanted, reversed, Distance(reversed)));
            }
        }

        [Fact]
        public void Groups_must_match_as_groups_not_as_one_set_of_faces()
        {
            NormalCutRecord r = TwoGroups();
            IReadOnlyList<IReadOnlyList<FaceSignature>> wanted = r.Groups.Select(g => (IReadOnlyList<FaceSignature>)g).ToList();
            // Merged into one group: the same faces, but not the same groups.
            IReadOnlyList<IReadOnlyList<FaceSignature>> merged = new List<IReadOnlyList<FaceSignature>> { r.Groups.SelectMany(g => g).ToList() };
            Assert.False(NormalCutRules.SameGroups(wanted, merged, Distance(merged)));
            // A face missing, or one more (propagated where the original did not): not the same.
            IReadOnlyList<IReadOnlyList<FaceSignature>> missing = new List<IReadOnlyList<FaceSignature>> { r.Groups[0].Take(1).ToList(), r.Groups[1] };
            Assert.False(NormalCutRules.SameGroups(wanted, missing, Distance(missing)));
            IReadOnlyList<IReadOnlyList<FaceSignature>> extra = new List<IReadOnlyList<FaceSignature>>
            {
                r.Groups[0], r.Groups[1].Concat(new[] { Plane(1e-4, new Vec3(0.2, 0, 0), Vec3.UnitX) }).ToList(),
            };
            Assert.False(NormalCutRules.SameGroups(wanted, extra, Distance(extra)));
            // A face moved: its sample does not lie on it.
            IReadOnlyList<IReadOnlyList<FaceSignature>> moved = new List<IReadOnlyList<FaceSignature>>
            {
                new List<FaceSignature> { r.Groups[0][0], Plane(1.2e-4, new Vec3(0.012, 0.026, 0.001), r.Groups[0][1].Normal) }, r.Groups[1],
            };
            Assert.False(NormalCutRules.SameGroups(wanted, moved, Distance(moved)));
            Assert.True(NormalCutRules.SameGroups(wanted, wanted, Distance(wanted)));
        }

        [Fact]
        public void Values_are_compared_where_they_mean_something()
        {
            NormalCutRecord want = TwoGroups();
            NormalCutRecord got = TwoGroups();
            got.LinkToKFactor = true;
            got.LayerAdjustment = 0.3;
            // Extent: link and adjustment mean nothing.
            Assert.True(NormalCutRules.Differences(want, got, De).None);
            want.Parameters = got.Parameters = NormalCutParameterTypes.OffsetPlane;
            Deviations d = NormalCutRules.Differences(want, got, De);
            Assert.Equal(2, d.Differences.Count);
            Assert.StartsWith("An K-Faktor binden", d.Differences[0]);
            Assert.StartsWith("Lage der Versatzebene", d.Differences[1]);
            // Linked to the K-factor SOLIDWORKS sets the adjustment itself.
            want.LinkToKFactor = true;
            want.LayerAdjustment = 0.5;
            Assert.True(NormalCutRules.Differences(want, got, De).None);
            want.LinkToKFactor = got.LinkToKFactor = false;
            Assert.StartsWith("Lage der Versatzebene", NormalCutRules.Differences(want, got, De).Differences.Single());
            got.OptimizeGeometry = false;
            got.Parameters = NormalCutParameterTypes.Extent;
            Assert.Equal(3, NormalCutRules.Differences(want, got, De).Differences.Count);
        }

        [Fact]
        public void Paths_and_texts()
        {
            // The data object first (the help's way), the obsolete calls after it: per group, then in the help's numbered order.
            Assert.Equal(new[] { NormalCutPath.DataObject, NormalCutPath.Obsolete, NormalCutPath.ObsoleteHelpOrder }, NormalCutRules.Paths);
            Assert.Equal("AddSMNormalCut", NormalCutRules.PathName(NormalCutPath.Obsolete));
            Assert.Equal("AddSMNormalCut in der Reihenfolge der Hilfe", NormalCutRules.PathName(NormalCutPath.ObsoleteHelpOrder));
            Assert.Equal("Normaler Schnitt (Ausdehnung), 2 Flächengruppen, 3 Flächen", NormalCutRules.Describe(TwoGroups()));
            var one = new NormalCutRecord("N", "NormalCut") { Parameters = NormalCutParameterTypes.OffsetPlane };
            one.Groups.Add(new List<FaceSignature> { Plane(1e-4, Vec3.Zero, Vec3.UnitZ) });
            Assert.Equal("Normaler Schnitt (Versatzebene), 1 Flächengruppe, 1 Fläche", NormalCutRules.Describe(one));
            Assert.Equal("Fläche 2 der Flächengruppe 1 im Ziel nicht gefunden.", NormalCutRules.FaceMissing(1, 2));
            Assert.Equal("SOLIDWORKS hat den normalen Schnitt nicht mit den gespiegelten Flächengruppen angelegt (Datenobjekt: x).",
                NormalCutRules.NotCreated(new[] { "Datenobjekt: x" }));
            Assert.Equal((1, 4, 8), (NormalCutRules.FaceMark, NormalCutRules.OffsetPlaneMark, NormalCutRules.DirectionMark));
        }

        [Fact]
        public void Help_order_marks_the_first_face_of_a_group_1_and_the_others_2()
        {
            // AddSMNormalCutType remarks: cut-extrude faces with mark 1, „the group of cut-extrude faces“ with mark 2.
            Assert.Equal(2, NormalCutRules.GroupMark);
            Assert.Equal(new[] { 1 }, NormalCutRules.HelpOrderMarks(1));
            Assert.Equal(new[] { 1, 2, 2 }, NormalCutRules.HelpOrderMarks(3));
            Assert.Empty(NormalCutRules.HelpOrderMarks(0));
        }

        [Fact]
        public void Read_back_failure_names_unreadable_groups()
        {
            Assert.Equal("Der neue Schnitt ließ sich nicht zurücklesen.", NormalCutRules.ReadBackFailure(false, -1, 2));
            // Groups that do not read are not „-1 Flächengruppen“.
            Assert.Equal("Die Flächengruppen des neuen Schnitts ließen sich nicht lesen.", NormalCutRules.ReadBackFailure(true, -1, 2));
            Assert.Equal("Der neue Schnitt hat andere Flächen als die gespiegelten.", NormalCutRules.ReadBackFailure(true, 2, 2));
            Assert.Equal("Der neue Schnitt hat 1 Flächengruppen statt 2.", NormalCutRules.ReadBackFailure(true, 1, 2));
        }
    }
}
