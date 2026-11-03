using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Blechbestand.Model
{
    /// <summary>Zahlen im deutschen Format lesen und schreiben („1,5“ wie „1.5“).</summary>
    public static class Numbers
    {
        public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        private static readonly Regex FirstNumberPattern = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);

        /// <summary>
        /// Liest eine Menge. Leer, „-“ und „–“ bedeuten „keine Angabe“ (null).
        /// Komma und Punkt werden beide als Dezimaltrenner akzeptiert. Negative Werte sind ungültig.
        /// </summary>
        public static bool TryParseQuantity(string text, out decimal? value)
        {
            value = null;
            string s = (text ?? "").Trim();
            if (s.EndsWith("kg", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 2).Trim();
            if (s.Length == 0 || s == "-" || s == "–" || s == "—") return true;
            s = s.Replace(" ", "").Replace(" ", "");
            // „1.5“ als Dezimalzahl lesen, nicht als „15“ mit Tausenderpunkt.
            if (s.IndexOf(',') < 0 && s.Split('.').Length == 2) s = s.Replace('.', ',');
            else if (s.IndexOf(',') >= 0) s = s.Replace(".", "");
            if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, German, out decimal d)) return false;
            if (d < 0) return false;
            value = d;
            return true;
        }

        public static decimal? ParseQuantityOrNull(string text) => TryParseQuantity(text, out decimal? v) ? v : null;

        /// <summary>„1,5“, „20“, leer für null.</summary>
        public static string Format(decimal? value) => value.HasValue ? value.Value.ToString("0.###", German) : "";

        /// <summary>„107,64 kg“.</summary>
        public static string FormatWeight(decimal? kg) => kg.HasValue ? kg.Value.ToString("#,##0.##", German) + " kg" : "";

        /// <summary>Erste Zahl in einem Text, z. B. 1,5 aus „1,5-2“.</summary>
        public static decimal? FirstNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Match m = FirstNumberPattern.Match(text);
            if (!m.Success) return null;
            return decimal.Parse(m.Value.Replace('.', ','), NumberStyles.AllowDecimalPoint, German);
        }

        /// <summary>Zerlegt „3000x1500“ (auch „3000 x 1500“, „3000×1500“) in Länge und Breite in mm.</summary>
        public static bool TryParseDimension(string text, out decimal length, out decimal width)
        {
            length = 0; width = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.ToLowerInvariant().Replace("×", "x").Replace("*", "x").Split('x');
            if (parts.Length != 2) return false;
            decimal? a = FirstNumber(parts[0]);
            decimal? b = FirstNumber(parts[1]);
            if (!a.HasValue || !b.HasValue || a.Value <= 0 || b.Value <= 0) return false;
            length = a.Value; width = b.Value;
            return true;
        }

        /// <summary>Tafelgewicht in kg aus Format (mm), Dicke (mm) und Dichte (kg/dm³), auf 2 Stellen gerundet.</summary>
        public static decimal? SheetWeight(string dimension, string thickness, decimal density)
        {
            decimal? t = FirstNumber(thickness);
            if (!t.HasValue || t.Value <= 0 || density <= 0) return null;
            if (!TryParseDimension(dimension, out decimal l, out decimal w)) return null;
            return Math.Round(l * w * t.Value / 1000000m * density, 2);
        }

        /// <summary>
        /// Übliche Dichte zur Gewichtsberechnung: aus der Werkstoffliste, sonst Aluminium 2,75 und alles andere 8,0 kg/dm³
        /// (wie in der bisherigen Excel-Liste).
        /// </summary>
        public static decimal DefaultDensity(string group, string material)
        {
            CatalogMaterial known = MaterialCatalog.Find(material);
            if (known != null) return known.Density;
            string s = ((group ?? "") + " " + (material ?? "")).ToLowerInvariant();
            if (s.Contains("alu") || s.Contains("en aw") || s.Contains("3.3") || s.Contains("almg")) return 2.75m;
            return 8.0m;
        }
    }
}
