using System;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>The shared sheet-metal rules (blech-plan §1.2): folded angles, comparisons, allowance writes, bend overrides.</summary>
    public class SheetMetalSharedTests
    {
        private static readonly NumberFormatInfo Comma = new NumberFormatInfo { NumberDecimalSeparator = ",", NegativeSign = "-" };

        // ------------------------------------------------------------ Fold

        [Theory]
        [InlineData(0.3)]
        [InlineData(Math.PI / 2)]
        [InlineData(2.9)]
        [InlineData(Math.PI)]
        public void Fold_maps_2pi_minus_theta_to_theta(double theta)
        {
            Assert.Equal(theta, SheetMetalCompare.Fold(theta), 12);
            Assert.Equal(theta, SheetMetalCompare.Fold(2 * Math.PI - theta), 12);
            Assert.Equal(theta, SheetMetalCompare.Fold(theta + 2 * Math.PI), 12);
            Assert.Equal(theta, SheetMetalCompare.Fold(-theta), 12);
        }

        [Fact]
        public void Fold_stays_in_zero_to_pi()
        {
            for (double a = -20; a <= 20; a += 0.01)
            {
                double f = SheetMetalCompare.Fold(a);
                Assert.InRange(f, 0, Math.PI);
            }
        }

        [Fact]
        public void Scalars_compare_folded_angles()
        {
            double theta = 60 * Math.PI / 180;
            Assert.Empty(SheetMetalCompare.Scalars(new[] { ("Biegewinkel", theta, 2 * Math.PI - theta, ScalarKind.Angle) }));
            Assert.Single(SheetMetalCompare.Scalars(new[] { ("Biegewinkel", theta, Math.PI - theta, ScalarKind.Angle) }));
        }

        // ------------------------------------------------------------ Differences

        private static SheetMetalParameters Plate() => new SheetMetalParameters
        {
            Thickness = 0.002,
            BendRadius = 0.001,
            BendAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44 },
            Relief = new ReliefData { Kind = ReliefKind.Rectangular, UseRatio = true, Ratio = 0.5 },
            OverrideReadCode = 0,
        };

        [Fact]
        public void Differences_list_only_deviating_values()
        {
            // Within tolerance: nothing.
            SheetMetalParameters want = Plate(), got = Plate();
            got.Thickness += 5e-10;
            got.BendAllowance.KFactor += 5e-7;
            Assert.True(SheetMetalCompare.Parameters(want, got).None);

            // Each mismatch named once, with both values.
            got = Plate();
            got.Thickness = 0.0025;
            got.BendAllowance.KFactor = 0.5;
            got.OverrideBendAllowance = true;
            Deviations d = SheetMetalCompare.Parameters(want, got, Comma);
            Assert.Equal(3, d.Differences.Count);
            Assert.Contains("Blechdicke: erwartet 2 mm, gelesen 2,5 mm", d.Differences);
            Assert.Contains("K-Faktor: erwartet 0,44, gelesen 0,5", d.Differences);
            Assert.Contains("eigene Biegezugabe: erwartet nein, gelesen ja", d.Differences);

            // Override flags do not apply (single body): not compared.
            got.OverrideReadCode = 2;
            Assert.Equal(2, SheetMetalCompare.Parameters(want, got, Comma).Differences.Count);
        }

        [Fact]
        public void Allowance_difference_compares_only_the_value_of_the_kind()
        {
            var k = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44, BendAllowance = 0.001 };
            var other = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44, BendAllowance = 0.002 };
            Assert.True(SheetMetalCompare.Allowance(k, other).None);

            var direct = new BendAllowanceData { Kind = BendAllowanceKind.Direct, BendAllowance = 0.0005 };
            Assert.Equal("Biegezugabe: erwartet K 0,44, gelesen Biegezugabe 0,5 mm", SheetMetalCompare.Allowance(k, direct, Comma).Differences.Single());
        }

        [Fact]
        public void Null_allowance_is_the_default_and_differs_from_a_set_one()
        {
            var k = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.38 };
            Assert.True(SheetMetalCompare.Allowance(null, null).None);
            Assert.Equal("Biegezugabe: erwartet Standard, gelesen eigene (K 0,38)", SheetMetalCompare.Allowance(null, k, Comma).Differences.Single());
            Assert.Equal("Biegezugabe: erwartet eigene (K 0,38), gelesen Standard", SheetMetalCompare.Allowance(k, null, Comma).Differences.Single());
        }

        [Fact]
        public void Table_paths_compare_in_full_otherwise_by_file_name_with_a_note()
        {
            BendAllowanceData Table(string path) => new BendAllowanceData { Kind = BendAllowanceKind.BendTable, BendTableFile = path };

            Assert.True(SheetMetalCompare.Allowance(Table(@"C:\Tabellen\Stahl.xlsx"), Table("c:/tabellen/STAHL.xlsx")).None);
            Assert.Empty(SheetMetalCompare.Allowance(Table(@"C:\Tabellen\Stahl.xlsx"), Table("c:/tabellen/STAHL.xlsx")).Notes);

            Deviations moved = SheetMetalCompare.Allowance(Table(@"C:\Tabellen\Stahl.xlsx"), Table(@"D:\Projekt\Stahl.xlsx"));
            Assert.True(moved.None);
            Assert.Contains("Stahl.xlsx", moved.Notes.Single());

            Deviations other = SheetMetalCompare.Allowance(Table(@"C:\Tabellen\Stahl.xlsx"), Table(@"C:\Tabellen\Alu.xlsx"));
            Assert.Single(other.Differences);
        }

        [Fact]
        public void Unreadable_bend_calculation_table_is_a_note_not_a_difference()
        {
            var want = new BendAllowanceData { Kind = BendAllowanceKind.BendCalculationTable, BendCalculationTableFile = @"C:\T\Berechnung.xlsx" };
            var got = new BendAllowanceData { Kind = BendAllowanceKind.BendCalculationTable };
            Deviations d = SheetMetalCompare.Allowance(want, got);
            Assert.True(d.None);
            Assert.Single(d.Notes);
        }

        [Fact]
        public void Relief_size_counts_only_with_automatic_relief_of_a_kind_that_has_one()
        {
            var tear = new ReliefData { Kind = ReliefKind.Tear, Ratio = 0.5 };
            Assert.True(SheetMetalCompare.Relief(tear, new ReliefData { Kind = ReliefKind.Tear, Ratio = 1.0 }).None);

            var off = new ReliefData { UseAutoRelief = false, Kind = ReliefKind.Rectangular };
            Assert.True(SheetMetalCompare.Relief(off, new ReliefData { UseAutoRelief = false, Kind = ReliefKind.Obround }).None);

            var rect = new ReliefData { Kind = ReliefKind.Rectangular, Ratio = 0.5 };
            Assert.Equal("Entlastungsverhältnis: erwartet 0,5, gelesen 1", SheetMetalCompare.Relief(rect, new ReliefData { Kind = ReliefKind.Rectangular, Ratio = 1.0 }, Comma).Differences.Single());

            // Width and depth are unknown (0) before 2022: not compared.
            var sized = new ReliefData { Kind = ReliefKind.Rectangular, UseRatio = false, Width = 0.001, Depth = 0.002 };
            Assert.True(SheetMetalCompare.Relief(sized, new ReliefData { Kind = ReliefKind.Rectangular, UseRatio = false }).None);
        }

        // ------------------------------------------------------------ allowance writes

        [Fact]
        public void Allowance_rules_refuse_tables_on_jogs_only()
        {
            foreach (BendAllowanceKind kind in Enum.GetValues(typeof(BendAllowanceKind)))
            {
                bool table = kind == BendAllowanceKind.BendTable || kind == BendAllowanceKind.BendCalculationTable;
                Assert.Equal(!table, AllowanceRules.Settable("Jog", kind));
                foreach (string type in new[] { "EdgeFlange", "SMMiteredFlange", "Hem", "SM3dBend", "SMBaseFlange" })
                    Assert.True(AllowanceRules.Settable(type, kind));
            }
            Assert.Contains("Biegetabelle „Stahl.xlsx“",
                AllowanceRules.NotSettable(new BendAllowanceData { Kind = BendAllowanceKind.BendTable, BendTableFile = @"C:\T\Stahl.xlsx" }));
        }

        [Fact]
        public void Allowance_write_order_sets_the_type_last()
        {
            Assert.Equal(new[] { AllowanceStep.UseDefault }, AllowanceRules.WriteOrder(null));
            Assert.Equal(new[] { AllowanceStep.UseOwn, AllowanceStep.KFactor, AllowanceStep.Type, AllowanceStep.Apply },
                AllowanceRules.WriteOrder(new BendAllowanceData { Kind = BendAllowanceKind.KFactor }));
            Assert.Equal(AllowanceStep.BendAllowance, AllowanceRules.WriteOrder(new BendAllowanceData { Kind = BendAllowanceKind.Direct })[1]);
            Assert.Equal(AllowanceStep.BendDeduction, AllowanceRules.WriteOrder(new BendAllowanceData { Kind = BendAllowanceKind.Deduction })[1]);
            Assert.Equal(AllowanceStep.BendTableFile, AllowanceRules.WriteOrder(new BendAllowanceData { Kind = BendAllowanceKind.BendTable })[1]);
            Assert.Equal(AllowanceStep.BendCalculationTableFile, AllowanceRules.WriteOrder(new BendAllowanceData { Kind = BendAllowanceKind.BendCalculationTable })[1]);
            foreach (BendAllowanceKind kind in Enum.GetValues(typeof(BendAllowanceKind)))
            {
                var steps = AllowanceRules.WriteOrder(new BendAllowanceData { Kind = kind });
                Assert.Equal(AllowanceStep.UseOwn, steps[0]);
                Assert.Equal(AllowanceStep.Type, steps[steps.Count - 2]);
                Assert.Equal(AllowanceStep.Apply, steps[steps.Count - 1]);
            }
        }

        // ------------------------------------------------------------ bend overrides

        [Fact]
        public void Bend_overrides_name_bends_that_differ_from_the_parent()
        {
            var parent = new BendValues
            {
                Name = "Kantenlasche1",
                UseDefaultRadius = false,
                Radius = 0.002,
                UseDefaultAllowance = false,
                Allowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44 },
                Relief = new ReliefData(),
            };
            var bends = new[]
            {
                new BendValues { Name = "EdgeBend1" },
                // Own values, but the same as the parent's: nothing lost.
                new BendValues { Name = "EdgeBend2", UseDefaultRadius = false, Radius = 0.002, UseDefaultAllowance = false, Allowance = new BendAllowanceData { KFactor = 0.44 } },
                new BendValues { Name = "EdgeBend3", UseDefaultAllowance = false, Allowance = new BendAllowanceData { KFactor = 0.38 } },
                new BendValues { Name = "EdgeBend4", UseDefaultRadius = false, Radius = 0.003, UseDefaultRelief = false, Relief = new ReliefData { Kind = ReliefKind.Tear } },
            };

            var overrides = BendOverrides.Differences(parent, bends, Comma);

            Assert.Equal(new[] { "EdgeBend3", "EdgeBend4" }, overrides.Select(o => o.Bend));
            Assert.Equal("Biegung „EdgeBend3“ hat eine eigene Biegezugabe (K 0,38); über die API nicht übertragbar.", overrides[0].Note());
            Assert.Equal("Biegung „EdgeBend4“ hat einen eigenen Biegeradius (3 mm) und eine eigene Entlastung (ausreißen); über die API nicht übertragbar.", overrides[1].Note());
        }

        [Fact]
        public void Own_allowance_of_a_bend_counts_when_the_parents_is_unknown()
        {
            var parent = new BendValues { Name = "Saum1", Radius = 0.001 };
            var bend = new BendValues { Name = "HemBend1", UseDefaultAllowance = false, Allowance = new BendAllowanceData { KFactor = 0.5 } };
            Assert.Single(BendOverrides.Differences(parent, new[] { bend }));
            var unreadable = new BendValues { Name = "HemBend2", UseDefaultAllowance = false };
            Assert.Contains("nicht lesbar", BendOverrides.Differences(parent, new[] { unreadable }).Single().What);
        }
    }
}
