using System;
using System.Globalization;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Records
{
    /// <summary>Geometry in report lines, written like every other value there: lengths in mm, current culture.</summary>
    public static class ReportText
    {
        /// <summary>"Ebene durch (50|0|0) mm, Normale (1|0|0)", through the point of the plane closest to the origin.</summary>
        /// <param name="culture">Number format; null for the current culture.</param>
        public static string Plane(Plane3 plane, IFormatProvider? culture = null)
        {
            Vec3 o = plane.ClosestPointToOrigin() * 1000, n = plane.Normal;
            return $"Ebene durch {Triple(o, 3, culture)} mm, Normale {Triple(n, 4, culture)}";
        }

        // "|" between the values: the decimal separator may be a comma.
        private static string Triple(Vec3 v, int decimals, IFormatProvider? culture) =>
            $"({Number(v.X, decimals, culture)}|{Number(v.Y, decimals, culture)}|{Number(v.Z, decimals, culture)})";

        private static string Number(double value, int decimals, IFormatProvider? culture)
        {
            double rounded = Math.Round(value, decimals);
            // A tiny negative value would read "-0".
            if (rounded == 0) rounded = 0;
            return rounded.ToString("0." + new string('#', decimals), culture ?? CultureInfo.CurrentCulture);
        }
    }
}
