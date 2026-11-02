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

        public string QuantityText => Units.Format(Quantity, Unit);

        /// <summary>Gewicht in kg, nur bei Tafeln mit bekanntem Tafelgewicht.</summary>
        public decimal? Weight => Unit == Units.Sheets && Item.WeightPerSheet.HasValue ? Quantity * Item.WeightPerSheet.Value : (decimal?)null;
    }

    /// <summary>Baut die Bestell-E-Mail an den Einkauf (HTML für Outlook, Text, .eml-Datei, Zwischenablage).</summary>
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
                Td(sb, bg, l.QuantityText, bold: true, right: true);
                Td(sb, bg, i.ErpNumber);
                Td(sb, bg, i.Note);
                Td(sb, bg, Numbers.FormatWeight(l.Weight), right: true);
                sb.Append("</tr>");
                pos++;
            }
            decimal total = TotalWeight(lines);
            if (total > 0)
            {
                sb.Append("<tr><td colspan=\"8\" style=\"").Append(Cell).Append("text-align:right;font-weight:bold;\">Gesamtgewicht der Tafeln ca.</td>");
                sb.Append("<td style=\"").Append(Cell).Append("text-align:right;font-weight:bold;white-space:nowrap;\">").Append(Enc(Numbers.FormatWeight(total))).Append("</td></tr>");
            }
            sb.Append("</table>");

            if (!string.IsNullOrWhiteSpace(outro)) sb.Append("<p style=\"margin:12px 0 0 0;\">").Append(Paragraph(outro)).Append("</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        public static string BuildFullHtml(IList<OrderLine> lines, string intro, string outro) =>
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>" + BuildHtml(lines, intro, outro) + "</body></html>";

        /// <summary>Reiner Text, z. B. für Mailprogramme ohne HTML.</summary>
        public static string BuildText(IList<OrderLine> lines, string intro, string outro)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(intro)) sb.AppendLine(Normalize(intro)).AppendLine();
            int pos = 1;
            foreach (OrderLine l in lines)
            {
                SheetItem i = l.Item;
                var parts = new List<string> { pos + ".", l.QuantityText, i.Material };
                if (!string.IsNullOrWhiteSpace(i.Thickness)) parts.Add(i.Thickness + " mm");
                if (!string.IsNullOrWhiteSpace(i.Dimension)) parts.Add(i.Dimension);
                if (!string.IsNullOrWhiteSpace(i.Note)) parts.Add(i.Note);
                string line = string.Join(" ", parts) + " (" + i.Group + ")";
                if (!string.IsNullOrWhiteSpace(i.ErpNumber)) line += ", Eulanda-Nr. " + i.ErpNumber;
                sb.AppendLine(line);
                pos++;
            }
            decimal total = TotalWeight(lines);
            if (total > 0) sb.AppendLine().AppendLine("Gesamtgewicht der Tafeln ca. " + Numbers.FormatWeight(total));
            if (!string.IsNullOrWhiteSpace(outro)) sb.AppendLine().AppendLine(Normalize(outro));
            return sb.ToString();
        }

        /// <summary>
        /// E-Mail-Datei (.eml) als ungesendeter Entwurf (X-Unsent: 1). Wird von Outlook und anderen
        /// Mailprogrammen als neue Nachricht geöffnet.
        /// </summary>
        public static string BuildEml(string to, string cc, string subject, string html, string text)
        {
            string boundary = "----=_Blechbestand_" + Guid.NewGuid().ToString("N");
            var sb = new StringBuilder();
            sb.Append("X-Unsent: 1\r\n");
            if (!string.IsNullOrWhiteSpace(to)) sb.Append("To: ").Append(HeaderValue(to)).Append("\r\n");
            if (!string.IsNullOrWhiteSpace(cc)) sb.Append("Cc: ").Append(HeaderValue(cc)).Append("\r\n");
            sb.Append("Subject: ").Append(EncodeHeader(subject ?? "")).Append("\r\n");
            sb.Append("MIME-Version: 1.0\r\n");
            sb.Append("Content-Type: multipart/alternative; boundary=\"").Append(boundary).Append("\"\r\n\r\n");
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
            sb.Append(Base64Lines(text ?? "")).Append("\r\n");
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
            sb.Append(Base64Lines(html ?? "")).Append("\r\n");
            sb.Append("--").Append(boundary).Append("--\r\n");
            return sb.ToString();
        }

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

        private static string EncodeHeader(string s)
        {
            s = HeaderValue(s);
            if (s.All(c => c >= 32 && c < 127)) return s;
            return "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "?=";
        }

        private static string Base64Lines(string s)
        {
            string b = Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder();
            for (int i = 0; i < b.Length; i += 76)
                sb.Append(b, i, Math.Min(76, b.Length - i)).Append("\r\n");
            return sb.ToString().TrimEnd();
        }
    }
}
