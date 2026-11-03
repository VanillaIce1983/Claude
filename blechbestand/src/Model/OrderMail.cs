using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Blechbestand.Model
{
    /// <summary>Eine Zeile der Bestellung.</summary>
    public class OrderLine
    {
        public SheetItem Item { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = Units.Sheets;

        /// <summary>Tafeln pro Paket (aus dem Paketgewicht), für Gewicht und Angabe „1 Paket (17 Tafeln)“.</summary>
        public decimal? SheetsPerPackage { get; set; }

        public string QuantityText => Units.Format(Quantity, Unit);

        /// <summary>„1 Paket (17 Tafeln)“ bei Paketen, sonst wie <see cref="QuantityText"/>.</summary>
        public string QuantityDetail =>
            Unit == Units.Packages && SheetsPerPackage.HasValue
                ? QuantityText + " (" + Units.Format(Quantity * SheetsPerPackage.Value, Units.Sheets) + ")"
                : QuantityText;

        /// <summary>Gewicht in kg; bei Paketen über die Tafeln pro Paket.</summary>
        public decimal? Weight
        {
            get
            {
                if (!Item.WeightPerSheet.HasValue) return null;
                if (Unit == Units.Sheets) return Quantity * Item.WeightPerSheet.Value;
                return SheetsPerPackage.HasValue ? Quantity * SheetsPerPackage.Value * Item.WeightPerSheet.Value : (decimal?)null;
            }
        }
    }

    /// <summary>Baut die Bestell-E-Mail an den Einkauf (HTML für Outlook, Text, mailto-Link, Zwischenablage).</summary>
    public static class OrderMail
    {
        public static string Subject(string template, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(template)) template = "Blechbestellung {datum}";
            return template.Replace("{datum}", date.ToString("dd.MM.yyyy", Numbers.German))
                           .Replace("{Datum}", date.ToString("dd.MM.yyyy", Numbers.German));
        }

        /// <summary>Vermerk an der Position, z. B. „10 Tafeln am 02.11.2026“.</summary>
        public static string OrderNote(OrderLine line, DateTime date) =>
            line.QuantityText + " am " + date.ToString("dd.MM.yyyy", Numbers.German);

        public static decimal TotalWeight(IEnumerable<OrderLine> lines) => lines.Sum(l => l.Weight ?? 0m);

        /// <summary>
        /// Mengenvorschlag: bei Paketen 1 Paket, bei Tafeln bis auf das Doppelte des Mindestbestands auffüllen
        /// (mindestens 1). Kann jederzeit überschrieben werden.
        /// </summary>
        public static decimal SuggestQuantity(SheetItem item, string unit)
        {
            if (unit == Units.Packages) return 1m;
            decimal min = item.MinSheets ?? 0m;
            decimal current = item.Sheets ?? 0m;
            return Math.Max(1m, Math.Ceiling(2m * min - current));
        }

        private const string Font = "font-family:Calibri,'Segoe UI',Arial,sans-serif;font-size:11pt;";
        private const string Cell = "border:1px solid #BFBFBF;padding:4px 8px;";

        /// <summary>HTML-Inhalt der Mail (ohne &lt;html&gt;-Rahmen), alle Stile inline für Outlook.</summary>
        public static string BuildHtml(IList<OrderLine> lines, string intro, string outro)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"").Append(Font).Append("\">");
            if (!string.IsNullOrWhiteSpace(intro)) sb.Append("<p style=\"margin:0 0 10px 0;\">").Append(Paragraph(intro)).Append("</p>");

            sb.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;").Append(Font).Append("\">");
            sb.Append("<tr>");
            foreach (string h in new[] { "Pos.", "Material", "Werkstoff", "Dicke", "Format", "Menge", "Eulanda-Nr.", "Bemerkung", "Gewicht ca." })
                sb.Append("<th style=\"").Append(Cell).Append("background:#0070C0;color:#FFFFFF;text-align:left;font-weight:bold;\">").Append(Enc(h)).Append("</th>");
            sb.Append("</tr>");

            int pos = 1;
            foreach (OrderLine l in lines)
            {
                string bg = pos % 2 == 0 ? "background:#DDEBF7;" : "";
                SheetItem i = l.Item;
                sb.Append("<tr>");
                Td(sb, bg, pos.ToString(Numbers.German), right: true);
                Td(sb, bg, i.Group);
                Td(sb, bg, i.Material, bold: true);
                Td(sb, bg, string.IsNullOrWhiteSpace(i.Thickness) ? "" : i.Thickness + " mm", right: true);
                Td(sb, bg, i.Dimension);
                Td(sb, bg, l.QuantityDetail, bold: true, right: true);
                Td(sb, bg, i.ErpNumber);
                Td(sb, bg, i.Note);
                Td(sb, bg, Numbers.FormatWeight(l.Weight), right: true);
                sb.Append("</tr>");
                pos++;
            }
            decimal total = TotalWeight(lines);
            if (total > 0)
            {
                sb.Append("<tr><td colspan=\"8\" style=\"").Append(Cell).Append("text-align:right;font-weight:bold;\">Gesamtgewicht ca.</td>");
                sb.Append("<td style=\"").Append(Cell).Append("text-align:right;font-weight:bold;white-space:nowrap;\">").Append(Enc(Numbers.FormatWeight(total))).Append("</td></tr>");
            }
            sb.Append("</table>");

            if (!string.IsNullOrWhiteSpace(outro)) sb.Append("<p style=\"margin:12px 0 0 0;\">").Append(Paragraph(outro)).Append("</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>Reiner Text, z. B. für Mailprogramme ohne HTML.</summary>
        public static string BuildText(IList<OrderLine> lines, string intro, string outro)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(intro)) sb.AppendLine(Normalize(intro)).AppendLine();
            int pos = 1;
            foreach (OrderLine l in lines)
            {
                SheetItem i = l.Item;
                var parts = new List<string> { pos + ".", l.QuantityDetail, i.Material };
                if (!string.IsNullOrWhiteSpace(i.Thickness)) parts.Add(i.Thickness + " mm");
                if (!string.IsNullOrWhiteSpace(i.Dimension)) parts.Add(i.Dimension);
                if (!string.IsNullOrWhiteSpace(i.Note)) parts.Add(i.Note);
                string line = string.Join(" ", parts) + (string.IsNullOrWhiteSpace(i.Group) ? "" : " (" + i.Group + ")");
                if (!string.IsNullOrWhiteSpace(i.ErpNumber)) line += ", Eulanda-Nr. " + i.ErpNumber;
                sb.AppendLine(line);
                pos++;
            }
            decimal total = TotalWeight(lines);
            if (total > 0) sb.AppendLine().AppendLine("Gesamtgewicht ca. " + Numbers.FormatWeight(total));
            if (!string.IsNullOrWhiteSpace(outro)) sb.AppendLine().AppendLine(Normalize(outro));
            return sb.ToString();
        }

        /// <summary>
        /// mailto-Link für eine neue, leere E-Mail mit Empfänger, Cc und Betreff im Standard-Mailprogramm
        /// (z. B. dem neuen Outlook). Den Inhalt fügt man aus der Zwischenablage ein, weil lange Texte
        /// in einem Link nicht zuverlässig ankommen.
        /// </summary>
        public static string BuildMailto(string to, string cc, string subject)
        {
            var query = new List<string>();
            string ccList = AddressList(cc);
            if (ccList.Length > 0) query.Add("cc=" + ccList);
            if (!string.IsNullOrWhiteSpace(subject)) query.Add("subject=" + Uri.EscapeDataString(HeaderValue(subject)));
            return "mailto:" + AddressList(to) + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        }

        /// <summary>„a@x.de; b@y.de“ → „a@x.de,b@y.de“ (URL-sicher, @ bleibt lesbar).</summary>
        private static string AddressList(string addresses) =>
            string.Join(",", (addresses ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim()).Where(a => a.Length > 0)
                .Select(a => Uri.EscapeDataString(a).Replace("%40", "@")));

        /// <summary>Windows-Zwischenablageformat „HTML Format“ (CF_HTML) als UTF-8-Bytes.</summary>
        public static byte[] BuildClipboardHtml(string fragment)
        {
            const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            const string pre = "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
            const string post = "<!--EndFragment--></body></html>";
            Encoding utf8 = new UTF8Encoding(false);
            int headerLength = utf8.GetByteCount(string.Format(header, 0, 0, 0, 0));
            int startHtml = headerLength;
            int startFragment = startHtml + utf8.GetByteCount(pre);
            int endFragment = startFragment + utf8.GetByteCount(fragment);
            int endHtml = endFragment + utf8.GetByteCount(post);
            string all = string.Format(header, startHtml, endHtml, startFragment, endFragment) + pre + fragment + post;
            return utf8.GetBytes(all);
        }

        /// <summary>Fügt den eigenen Inhalt direkt hinter &lt;body&gt; ein, damit eine Outlook-Signatur darunter erhalten bleibt.</summary>
        public static string InsertIntoBody(string existingHtml, string content)
        {
            if (string.IsNullOrEmpty(existingHtml)) return "<html><body>" + content + "</body></html>";
            int body = existingHtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (body >= 0)
            {
                int close = existingHtml.IndexOf('>', body);
                if (close > 0) return existingHtml.Substring(0, close + 1) + content + existingHtml.Substring(close + 1);
            }
            return content + existingHtml;
        }

        private static void Td(StringBuilder sb, string bg, string text, bool bold = false, bool right = false)
        {
            sb.Append("<td style=\"").Append(Cell).Append(bg);
            if (bold) sb.Append("font-weight:bold;");
            if (right) sb.Append("text-align:right;white-space:nowrap;");
            sb.Append("\">").Append(Enc(text)).Append("</td>");
        }

        private static string Enc(string s) => WebUtility.HtmlEncode(s ?? "");

        private static string Normalize(string s) => (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).TrimEnd();

        private static string Paragraph(string s) =>
            string.Join("<br>", (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd().Split('\n').Select(Enc));

        private static string HeaderValue(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

    }
}
