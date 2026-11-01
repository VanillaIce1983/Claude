using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>What a scalar is, for its tolerance and how the report writes it.</summary>
    public enum ScalarKind
    {
        /// <summary>Metres, written in mm; tolerance 1e-9 m.</summary>
        Length,

        /// <summary>Radians, compared folded (<see cref="SheetMetalCompare.Fold"/>), written in degrees.</summary>
        Angle,

        /// <summary>A plain number such as a K-factor or a ratio; tolerance 1e-6.</summary>
        Factor,

        /// <summary>0 or 1, written „ja“/„nein“.</summary>
        Flag,

        /// <summary>An enum value, compared exactly.</summary>
        Choice,
    }

    /// <summary>
    /// The outcome of a comparison: <see cref="Differences"/> make a step a warning,
    /// <see cref="Notes"/> are information only (a table found under its file name elsewhere).
    /// </summary>
    public sealed class Deviations
    {
        public List<string> Differences { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();
        public bool None => Differences.Count == 0;

        internal void Add(Deviations other)
        {
            Differences.AddRange(other.Differences);
            Notes.AddRange(other.Notes);
        }
    }

    /// <summary>
    /// Sheet-metal values compared as data: a read-back against what was written, a bend
    /// against its owner. One rule set for every area (blech-plan §1.2): 1e-9 m for lengths,
    /// 1e-6 for K-factors and ratios, angles folded into [0, π], table paths in full and
    /// otherwise by file name with a note. Texts are German, numbers in the given culture.
    /// </summary>
    public static class SheetMetalCompare
    {
        public const double LengthTolerance = 1e-9;
        public const double FactorTolerance = 1e-6;
        public const double AngleTolerance = 1e-9;

        /// <summary>
        /// The geometric angle: a mod 2π folded into [0, π]. A bend of θ and one of 2π − θ fold
        /// the same way about a hinge that runs the other way, which is what a mirrored edge does.
        /// </summary>
        public static double Fold(double a)
        {
            const double full = 2 * Math.PI;
            double m = a % full;
            if (m < 0) m += full;
            return m > Math.PI ? full - m : m;
        }

        /// <summary>Each scalar that deviates, as „Name: erwartet …, gelesen …“.</summary>
        public static List<string> Scalars(IEnumerable<(string name, double want, double got, ScalarKind kind)> values, IFormatProvider? culture = null)
        {
            var result = new List<string>();
            foreach (var (name, want, got, kind) in values)
                if (!Same(want, got, kind)) result.Add($"{name}: erwartet {Format(want, kind, culture)}, gelesen {Format(got, kind, culture)}");
            return result;
        }

        private static bool Same(double want, double got, ScalarKind kind) => kind switch
        {
            ScalarKind.Length => Math.Abs(want - got) <= LengthTolerance,
            ScalarKind.Angle => Math.Abs(Fold(want) - Fold(got)) <= AngleTolerance,
            ScalarKind.Factor => Math.Abs(want - got) <= FactorTolerance,
            ScalarKind.Flag => (want != 0) == (got != 0),
            _ => want == got,
        };

        private static string Format(double value, ScalarKind kind, IFormatProvider? culture) => kind switch
        {
            ScalarKind.Length => Mm(value, culture),
            ScalarKind.Angle => ReportText.Number(value * 180 / Math.PI, 4, culture) + "°",
            ScalarKind.Factor => ReportText.Number(value, 6, culture),
            ScalarKind.Flag => value != 0 ? "ja" : "nein",
            _ => ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        internal static string Mm(double metres, IFormatProvider? culture) => ReportText.Number(metres * 1000, 6, culture) + " mm";

        private static (string, double, double, ScalarKind) Flag(string name, bool want, bool got) => (name, want ? 1 : 0, got ? 1 : 0, ScalarKind.Flag);

        /// <summary>
        /// Two bend allowances. Null is "uses the default" (a feature without its own allowance).
        /// Only the value that belongs to the kind is compared; a bend calculation table the
        /// copy does not report (late-bound) is a note, not a difference.
        /// </summary>
        public static Deviations Allowance(BendAllowanceData? want, BendAllowanceData? got, IFormatProvider? culture = null)
        {
            var d = new Deviations();
            if (want == null && got == null) return d;
            if (want == null)
            {
                d.Differences.Add($"Biegezugabe: erwartet Standard, gelesen eigene ({Describe(got!, culture)})");
                return d;
            }
            if (got == null)
            {
                d.Differences.Add($"Biegezugabe: erwartet eigene ({Describe(want, culture)}), gelesen Standard");
                return d;
            }
            if (want.Kind != got.Kind)
            {
                d.Differences.Add($"Biegezugabe: erwartet {Describe(want, culture)}, gelesen {Describe(got, culture)}");
                return d;
            }
            switch (want.Kind)
            {
                case BendAllowanceKind.KFactor:
                    d.Differences.AddRange(Scalars(new[] { ("K-Faktor", want.KFactor, got.KFactor, ScalarKind.Factor) }, culture));
                    break;
                case BendAllowanceKind.Direct:
                    d.Differences.AddRange(Scalars(new[] { ("Biegezugabe", want.BendAllowance, got.BendAllowance, ScalarKind.Length) }, culture));
                    break;
                case BendAllowanceKind.Deduction:
                    d.Differences.AddRange(Scalars(new[] { ("Biegeabzug", want.BendDeduction, got.BendDeduction, ScalarKind.Length) }, culture));
                    break;
                case BendAllowanceKind.BendTable:
                    SameTable(want.BendTableFile, got.BendTableFile, "Biegetabelle", d);
                    break;
                case BendAllowanceKind.BendCalculationTable:
                    if (want.BendCalculationTableFile.Length > 0 && got.BendCalculationTableFile.Length == 0)
                        d.Notes.Add("Biegeberechnungstabelle der Kopie nicht lesbar; nicht verglichen.");
                    else
                        SameTable(want.BendCalculationTableFile, got.BendCalculationTableFile, "Biegeberechnungstabelle", d);
                    break;
            }
            return d;
        }

        /// <summary>
        /// Two reliefs; null is "uses the default". Kind and size count only with automatic
        /// relief, the size only for relief kinds that have one. Width and depth are readable
        /// only in newer versions: compared only when both are known (non-zero).
        /// </summary>
        public static Deviations Relief(ReliefData? want, ReliefData? got, IFormatProvider? culture = null)
        {
            var d = new Deviations();
            if (want == null && got == null) return d;
            if (want == null || got == null)
            {
                d.Differences.Add(want == null
                    ? $"Entlastung: erwartet Standard, gelesen eigene ({Describe(got!, culture)})"
                    : $"Entlastung: erwartet eigene ({Describe(want, culture)}), gelesen Standard");
                return d;
            }
            if (want.UseAutoRelief != got.UseAutoRelief || (want.UseAutoRelief && want.Kind != got.Kind))
            {
                d.Differences.Add($"Entlastung: erwartet {Describe(want, culture)}, gelesen {Describe(got, culture)}");
                return d;
            }
            if (!want.UseAutoRelief || !HasSize(want.Kind)) return d;
            var values = new List<(string, double, double, ScalarKind)> { Flag("Entlastung im Verhältnis zur Dicke", want.UseRatio, got.UseRatio) };
            if (want.UseRatio && got.UseRatio) values.Add(("Entlastungsverhältnis", want.Ratio, got.Ratio, ScalarKind.Factor));
            if (!want.UseRatio && !got.UseRatio)
            {
                if (want.Width > 0 && got.Width > 0) values.Add(("Entlastungsbreite", want.Width, got.Width, ScalarKind.Length));
                if (want.Depth > 0 && got.Depth > 0) values.Add(("Entlastungstiefe", want.Depth, got.Depth, ScalarKind.Length));
            }
            d.Differences.AddRange(Scalars(values, culture));
            return d;
        }

        private static bool HasSize(ReliefKind kind) => kind == ReliefKind.Rectangular || kind == ReliefKind.Obround;

        /// <summary>
        /// The sheet-metal parameters of a body or of the part's defaults. Override flags count
        /// only where both were read and apply (return code 0); the late-bound flags only where
        /// both are known. The fixed reference is geometry and not compared here.
        /// </summary>
        public static Deviations Parameters(SheetMetalParameters want, SheetMetalParameters got, IFormatProvider? culture = null)
        {
            var d = new Deviations();
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Blechdicke", want.Thickness, got.Thickness, ScalarKind.Length),
                ("Biegeradius", want.BendRadius, got.BendRadius, ScalarKind.Length),
                Flag("Blechstärkentabelle", want.UseGaugeTable, got.UseGaugeTable),
            };
            if (want.UseMaterialParameters.HasValue && got.UseMaterialParameters.HasValue)
                values.Add(Flag("Blechparameter aus dem Werkstoff", want.UseMaterialParameters.Value, got.UseMaterialParameters.Value));
            if (want.OverrideDefaults.HasValue && got.OverrideDefaults.HasValue)
                values.Add(Flag("Standardparameter überschreiben", want.OverrideDefaults.Value, got.OverrideDefaults.Value));
            if (want.OverrideReadCode == 0 && got.OverrideReadCode == 0)
            {
                values.Add(Flag("eigene Biegeparameter", want.OverrideBendParameters, got.OverrideBendParameters));
                values.Add(Flag("eigene Biegezugabe", want.OverrideBendAllowance, got.OverrideBendAllowance));
                values.Add(Flag("eigene automatische Entlastung", want.OverrideAutoRelief, got.OverrideAutoRelief));
            }
            d.Differences.AddRange(Scalars(values, culture));
            if (want.UseGaugeTable && got.UseGaugeTable) SameTable(want.GaugeTableFile, got.GaugeTableFile, "Blechstärkentabelle", d);
            d.Add(Allowance(want.BendAllowance, got.BendAllowance, culture));
            d.Add(Relief(want.Relief, got.Relief, culture));
            return d;
        }

        /// <summary>
        /// Table paths: the same path (case and slash direction aside) is the same table; the same
        /// file name in another folder is accepted with a note – a new part may find the table
        /// elsewhere –; anything else is a difference.
        /// </summary>
        internal static void SameTable(string want, string got, string label, Deviations into)
        {
            string w = Normalize(want), g = Normalize(got);
            if (string.Equals(w, g, StringComparison.OrdinalIgnoreCase)) return;
            string wName = FileName(w), gName = FileName(g);
            if (wName.Length > 0 && string.Equals(wName, gName, StringComparison.OrdinalIgnoreCase))
                into.Notes.Add($"{label}: dieselbe Datei „{wName}“ in einem anderen Ordner („{want}“ und „{got}“).");
            else
                into.Differences.Add($"{label}: erwartet „{want}“, gelesen „{got}“");
        }

        private static string Normalize(string path) => (path ?? "").Trim().Replace('/', '\\');

        private static string FileName(string normalized) => normalized.Substring(normalized.LastIndexOf('\\') + 1);

        /// <summary>„K 0,38“, „Biegezugabe 0,5 mm“, „Biegetabelle „Stahl.xlsx““ …</summary>
        public static string Describe(BendAllowanceData a, IFormatProvider? culture = null) => a.Kind switch
        {
            BendAllowanceKind.KFactor => "K " + ReportText.Number(a.KFactor, 6, culture),
            BendAllowanceKind.Direct => "Biegezugabe " + Mm(a.BendAllowance, culture),
            BendAllowanceKind.Deduction => "Biegeabzug " + Mm(a.BendDeduction, culture),
            BendAllowanceKind.BendTable => $"Biegetabelle „{FileName(Normalize(a.BendTableFile))}“",
            _ => a.BendCalculationTableFile.Length > 0
                ? $"Biegeberechnung „{FileName(Normalize(a.BendCalculationTableFile))}“"
                : "Biegeberechnung",
        };

        /// <summary>„rechteckig, Verhältnis 0,5“, „ausreißen“, „keine automatische Entlastung“ …</summary>
        public static string Describe(ReliefData r, IFormatProvider? culture = null)
        {
            if (!r.UseAutoRelief) return "keine automatische Entlastung";
            string kind = r.Kind switch
            {
                ReliefKind.Rectangular => "rechteckig",
                ReliefKind.Tear => "ausreißen",
                ReliefKind.Obround => "Langloch",
                ReliefKind.None => "keine",
                _ => "ausreißen (Biegung)",
            };
            if (!HasSize(r.Kind)) return kind;
            if (r.UseRatio) return $"{kind}, Verhältnis {ReportText.Number(r.Ratio, 6, culture)}";
            return r.Width > 0 || r.Depth > 0 ? $"{kind}, Breite {Mm(r.Width, culture)}, Tiefe {Mm(r.Depth, culture)}" : kind;
        }

        /// <summary>Several items in one sentence: „a“, „a und b“, „a, b und c“.</summary>
        internal static string JoinGerman(IReadOnlyList<string> items) => items.Count switch
        {
            0 => "",
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " und " + items[items.Count - 1],
        };
    }
}
