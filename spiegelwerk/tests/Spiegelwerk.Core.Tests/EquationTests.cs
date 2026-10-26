using System;
using System.Linq;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Copied equations: the names they use, and the dimension names the copy gets back.</summary>
    public class EquationTests
    {
        private const int Linear = 2, Angular = 4;

        private static NamedDimension Dim(string name, double value, int kind = Linear) => new NamedDimension(name, value, kind);

        [Fact]
        public void Names_are_the_quoted_parts_up_to_a_comment()
        {
            Assert.Equal(new[] { "D1@Skizze1", "Breite" }, EquationText.Names("\"D1@Skizze1\" = \"Breite\" / 2 + 5"));
            Assert.Equal(new[] { "Breite" }, EquationText.Names("\"Breite\" = 50 'Breite \"außen\""));
            Assert.Equal(new[] { "Extrude1", "Anzahl", "suppressed", "unsuppressed" },
                EquationText.Names("\"Extrude1\" = IIF(\"Anzahl\" > 2, \"suppressed\", \"unsuppressed\")"));
            Assert.Empty(EquationText.Names("\"offen = 3"));
        }

        [Fact]
        public void The_driven_name_is_the_quoted_one_left_of_the_equals_sign()
        {
            Assert.Equal("D1@Skizze1", EquationText.Driven("  \"D1@Skizze1\"= \"Breite\" / 2"));
            Assert.Equal("Breite", EquationText.Driven("\"Breite\" = 50"));
            Assert.Null(EquationText.Driven("\"D1@Skizze1\" + 1 = 3"));
            Assert.Null(EquationText.Driven("Breite = 50"));
        }

        [Fact]
        public void Dimension_names_split_into_name_and_owner()
        {
            Assert.Equal(("D1", "Skizze1"), EquationText.Dimension("D1@Skizze1"));
            Assert.Equal(("D2", "Linear-Austragung1"), EquationText.Dimension("D2@Linear-Austragung1@Teil1.SLDPRT"));
            Assert.Null(EquationText.Dimension("Breite"));
            Assert.Null(EquationText.Dimension("@Skizze1"));
        }

        [Fact]
        public void Matching_names_and_values_need_no_rename()
        {
            DimensionMatch m = DimensionMatcher.Match(new[] { Dim("D1", 0.01), Dim("D2", 0.02) }, new[] { Dim("D1", 0.01), Dim("D2", 0.02) });
            Assert.Equal(new[] { 0, 1 }, m.Counterparts);
            Assert.Empty(m.Renames);
        }

        [Fact]
        public void Dimensions_created_in_another_order_swap_their_names()
        {
            // Extrude made with provisional end conditions: depth and draft angle come out the other way round.
            DimensionMatch m = DimensionMatcher.Match(
                new[] { Dim("D1", 0.01), Dim("D2", 5 * Math.PI / 180, Angular) },
                new[] { Dim("D1", 5 * Math.PI / 180, Angular), Dim("D2", 0.01) });
            Assert.Equal(new[] { 1, 0 }, m.Counterparts);
            Assert.Equal(new[] { (1, "D1"), (0, "D2") }, m.Renames.OrderBy(r => r.name));
        }

        [Fact]
        public void Equal_values_that_cannot_be_told_apart_stay_unmatched()
        {
            // Both directions 10 mm, the copy numbered them anew.
            DimensionMatch m = DimensionMatcher.Match(new[] { Dim("D1", 0.01), Dim("D2", 0.01) }, new[] { Dim("D3", 0.01), Dim("D4", 0.01) });
            Assert.Equal(new[] { -1, -1 }, m.Counterparts);
            Assert.Empty(m.Renames);
        }

        [Fact]
        public void Equal_values_of_another_kind_are_other_dimensions()
        {
            // 0.1 m and 0.1 rad: only the linear one is the depth.
            DimensionMatch m = DimensionMatcher.Match(new[] { Dim("D1", 0.1) }, new[] { Dim("D1", 0.1, Angular), Dim("D2", 0.1) });
            Assert.Equal(new[] { 1 }, m.Counterparts);
            Assert.Contains((1, "D1"), m.Renames);
            // The angular one gave up "D1" for a name the original does not have.
            Assert.Contains((0, "D3"), m.Renames);
        }

        [Fact]
        public void A_dimension_without_original_never_keeps_or_gets_an_original_name()
        {
            // The original's D2 has no counterpart; the copy's extra dimension holds the name D1 it needs to give up.
            DimensionMatch m = DimensionMatcher.Match(new[] { Dim("D1", 0.01), Dim("D2", 0.03) }, new[] { Dim("D1", 0.05), Dim("D3", 0.01) });
            Assert.Equal(new[] { 1, -1 }, m.Counterparts);
            Assert.Equal(new[] { (1, "D1"), (0, "D4") }, m.Renames);
        }

        [Fact]
        public void An_original_without_counterpart_is_unmatched()
        {
            DimensionMatch m = DimensionMatcher.Match(new[] { Dim("D1", 0.01) }, new[] { Dim("D1", 0.02) });
            Assert.Equal(new[] { -1 }, m.Counterparts);
            Assert.Empty(m.Renames);
        }

        [Fact]
        public void Values_count_as_equal_within_the_tolerance_of_the_copy()
        {
            Assert.True(DimensionMatcher.SameValue(0.01, 0.01 + 1e-12));
            Assert.False(DimensionMatcher.SameValue(0.01, 0.0101));
            Assert.True(DimensionMatcher.SameValue(0, 1e-10));
        }
    }
}
