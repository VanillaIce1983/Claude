using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Blechbestand.Model
{
    /// <summary>Ein Eintrag im Feld „Offene Bestellung“, z. B. „10 Tafeln am 02.11.2026“.</summary>
    public class OrderNoteEntry
    {
        /// <summary>Der Eintrag genau so, wie er im Feld steht.</summary>
        public string Raw { get; set; } = "";

        /// <summary>Bestellte Menge, null wenn der Text keine Menge enthält.</summary>
        public decimal? Quantity { get; set; }

        /// <summary><see cref="Units.Sheets"/> oder <see cref="Units.Packages"/>, null wenn unbekannt.</summary>
        public string Unit { get; set; }

        /// <summary>Bestelldatum als Text („02.11.2026“), null wenn keins angegeben ist.</summary>
        public string Date { get; set; }
    }

    /// <summary>
    /// Liest und ändert das Feld „Offene Bestellung“. Mehrere Bestellungen stehen dort durch „; “ getrennt,
    /// so wie sie beim Export im Reiter „Bestellung“ eingetragen werden.
    /// </summary>
    public static class OrderNotes
    {
        private static readonly Regex Pattern = new Regex(
            @"^\s*(?<q>\d+(?:[.,]\d+)?)\s*(?<u>Tafeln|Tafel|Pakete|Paket)\b(?:\s+am\s+(?<d>\d{1,2}\.\d{1,2}\.\d{2,4}))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static List<OrderNoteEntry> Parse(string openOrder)
        {
            var list = new List<OrderNoteEntry>();
            foreach (string part in Split(openOrder))
            {
                var e = new OrderNoteEntry { Raw = part };
                Match m = Pattern.Match(part);
                if (m.Success)
                {
                    e.Quantity = decimal.Parse(m.Groups["q"].Value.Replace('.', ','), NumberStyles.AllowDecimalPoint, Numbers.German);
                    e.Unit = m.Groups["u"].Value.StartsWith("P", StringComparison.OrdinalIgnoreCase) ? Units.Packages : Units.Sheets;
                    e.Date = m.Groups["d"].Success ? m.Groups["d"].Value : null;
                }
                list.Add(e);
            }
            return list;
        }

        /// <summary>
        /// Wendet eine Lieferung auf das Feld an und liefert den neuen Text.
        /// <paramref name="complete"/> = Bestellung erledigt: der Eintrag fällt weg.
        /// Sonst bleibt bei einer Teillieferung in derselben Einheit der Rest stehen („2 Tafeln am 02.11.2026 (Rest)“);
        /// in einer anderen Einheit lässt sich kein Rest rechnen, dann bleibt der Eintrag unverändert.
        /// Gibt false zurück, wenn es den Eintrag nicht (mehr) gibt.
        /// </summary>
        public static bool ApplyDelivery(string openOrder, string raw, decimal delivered, string unit, bool complete, out string newOpenOrder)
        {
            List<string> parts = Split(openOrder);
            int idx = parts.FindIndex(p => string.Equals(p, (raw ?? "").Trim(), StringComparison.Ordinal));
            if (idx < 0)
            {
                newOpenOrder = openOrder ?? "";
                return false;
            }
            OrderNoteEntry entry = Parse(parts[idx]).Single();
            if (complete)
            {
                parts.RemoveAt(idx);
            }
            else if (entry.Quantity.HasValue && entry.Unit == unit)
            {
                decimal rest = entry.Quantity.Value - delivered;
                if (rest <= 0) parts.RemoveAt(idx);
                else parts[idx] = Units.Format(rest, unit) + (entry.Date != null ? " am " + entry.Date : "") + " (Rest)";
            }
            newOpenOrder = string.Join("; ", parts);
            return true;
        }

        /// <summary>Ist die Bestellung mit dieser Lieferung voraussichtlich erledigt? (Vorschlag für das Häkchen im Dialog)</summary>
        public static bool LooksComplete(OrderNoteEntry entry, decimal delivered, string unit)
        {
            if (entry == null || !entry.Quantity.HasValue || entry.Unit != unit) return true;
            return delivered >= entry.Quantity.Value;
        }

        private static List<string> Split(string openOrder) =>
            (openOrder ?? "").Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }
}
