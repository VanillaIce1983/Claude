using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Blechbestand.Model;

namespace Blechbestand.Tests
{
    internal static class Program
    {
        private static int failures;
        private static int checks;

        private static void Check(bool condition, string what)
        {
            checks++;
            if (condition) return;
            failures++;
            Console.WriteLine("  FEHLER: " + what);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), what + " – erwartet „" + expected + "“, erhalten „" + actual + "“");
        }

        private static int Main()
        {
            Run("Zahlen lesen und schreiben", Numbers_);
            Run("Ampel / Mindestbestand", Levels_);
            Run("Startdaten aus der Excel-Liste", Seed_);
            Run("Sortierung und Gruppen", Groups_);
            Run("Speichern, Sicherung, Verlauf", Store_);
            Run("Zwei Rechner gleichzeitig", Concurrency_);
            Run("Bestell-Mail", Mail_);
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? $"Alle {checks} Prüfungen bestanden." : $"{failures} von {checks} Prüfungen fehlgeschlagen.");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            Console.WriteLine("• " + name);
            try { test(); }
            catch (Exception ex) { failures++; Console.WriteLine("  AUSNAHME: " + ex); }
        }

        private static void Numbers_()
        {
            Check(Numbers.TryParseQuantity("1,5", out decimal? a) && a == 1.5m, "1,5");
            Check(Numbers.TryParseQuantity("1.5", out a) && a == 1.5m, "1.5 mit Punkt");
            Check(Numbers.TryParseQuantity(" 20 ", out a) && a == 20m, "Leerzeichen");
            Check(Numbers.TryParseQuantity("", out a) && a == null, "leer = null");
            Check(Numbers.TryParseQuantity("-", out a) && a == null, "„-“ = null wie in der Excel-Liste");
            Check(Numbers.TryParseQuantity("1.000,5", out a) && a == 1000.5m, "Tausenderpunkt mit Komma");
            Check(Numbers.TryParseQuantity("107,64 kg", out a) && a == 107.64m, "Gewicht mit kg");
            Check(!Numbers.TryParseQuantity("abc", out _), "Text ist ungültig");
            Check(!Numbers.TryParseQuantity("-1", out _), "negativ ist ungültig");
            Equal("1,5", Numbers.Format(1.5m), "Format 1,5");
            Equal("20", Numbers.Format(20m), "Format 20");
            Equal("", Numbers.Format(null), "Format null");
            Equal("107,64 kg", Numbers.FormatWeight(107.64m), "Gewicht");
            Equal("1.234,5 kg", Numbers.FormatWeight(1234.5m), "Gewicht mit Tausenderpunkt");
            Equal(1.5m, Numbers.FirstNumber("1,5-2"), "Erste Zahl aus Riffelblech-Dicke");
            Equal(36m, Numbers.SheetWeight("3000x1500", "1", 8m), "Gewicht 1 mm Stahl");
            Equal(18.56m, Numbers.SheetWeight("3000x1500", "1,5", 2.75m), "Gewicht 1,5 mm Alu");
            Equal(540m, Numbers.SheetWeight("3000 × 1500", "15", 8m), "Gewicht mit ×");
            Equal(2.75m, Numbers.DefaultDensity("Aluminium", "3.3535"), "Dichte Alu");
            Equal(8.0m, Numbers.DefaultDensity("Edelstahl", "1.4301"), "Dichte Edelstahl");
            Equal("1 Tafel", Units.Format(1m, Units.Sheets), "Einzahl Tafel");
            Equal("1,5 Tafeln", Units.Format(1.5m, Units.Sheets), "Mehrzahl Tafeln");
            Equal("2 Pakete", Units.Format(2m, Units.Packages), "Mehrzahl Pakete");
        }

        private static void Levels_()
        {
            Equal(StockLevel.None, Levels.Of(1m, null), "ohne Mindestbestand");
            Equal(StockLevel.BelowMinimum, Levels.Of(1m, 2m), "unter Minimum");
            Equal(StockLevel.AtMinimum, Levels.Of(2m, 2m), "genau Minimum");
            Equal(StockLevel.Ok, Levels.Of(3m, 2m), "über Minimum");
            Equal(StockLevel.BelowMinimum, Levels.Of(null, 1m), "leer zählt als 0");
            var i = new SheetItem { Packages = 1, MinPackages = 2, Sheets = 30, MinSheets = 11 };
            Equal(StockLevel.BelowMinimum, i.Level, "schlechtere Ampel zählt");
            Equal("1 Paket + 30 Tafeln", i.StockText, "Bestandstext");
            Equal(Units.Sheets, i.PreferredUnit, "Einheit bei Paket und Tafeln");
            Equal(Units.Packages, new SheetItem { Packages = 2, MinPackages = 2 }.PreferredUnit, "paketweise geführt");
            Equal(1m, OrderMail.SuggestQuantity(new SheetItem { Packages = 1, MinPackages = 2 }, Units.Packages), "Vorschlag Pakete");
            Equal(9m, OrderMail.SuggestQuantity(new SheetItem { Sheets = 3, MinSheets = 6 }, Units.Sheets), "Vorschlag bis doppelter Mindestbestand");
            Equal(1m, OrderMail.SuggestQuantity(new SheetItem { Sheets = 1, MinSheets = 1 }, Units.Sheets), "Vorschlag mindestens 1");
            Equal(1m, OrderMail.SuggestQuantity(new SheetItem { Sheets = 20 }, Units.Sheets), "Vorschlag ohne Mindestbestand");
        }

        private static void Seed_()
        {
            Inventory inv = SeedData.Create();
            Equal(35, inv.Items.Count, "Anzahl Positionen");
            Equal(6, inv.Groups.Count, "Anzahl Gruppen");
            Equal(35, inv.Items.Select(i => i.Id).Distinct().Count(), "eindeutige IDs");
            var red = inv.Items.Where(i => i.Level == StockLevel.BelowMinimum).Select(i => i.DisplayName).OrderBy(x => x).ToList();
            var yellow = inv.Items.Where(i => i.Level == StockLevel.AtMinimum).Select(i => i.DisplayName).OrderBy(x => x).ToList();
            // Die 13 roten Positionen der Excel-Liste bleiben rot
            string[] expectedRed =
            {
                "1.0330 1 mm", "1.0332 4 mm", "1.0332 5 mm", "1.0226 2 mm", "1.0226 3 mm",
                "1.4301 1,5 mm", "1.4301 4 mm", "1.4301 5 mm", "1.4301 6 mm", "1.4301 8 mm",
                "3.3535 1,5 mm", "3.3535 4 mm", "EN AW-5754 1,5-2 mm",
            };
            Equal(13, red.Count, "rote Positionen");
            foreach (string r in expectedRed) Check(red.Contains(r), "rot: " + r);
            // Die 5 gelben der Excel-Liste bleiben gelb
            foreach (string y in new[] { "1.0332 15 mm", "1.4301 12 mm", "3.3535 5 mm", "3.3535 6 mm", "3.3535 8 mm" })
                Check(yellow.Contains(y), "gelb: " + y);
            Console.WriteLine("  gelb: " + string.Join(", ", yellow));
            SheetItem brushed = inv.Items.Single(i => i.Note == "gebürstet");
            Equal("0012771", brushed.ErpNumber, "gebürstet mit eigener Eulanda-Nr.");
            Equal(1.5m, inv.Items.Single(i => i.Material == "1.0332" && i.Thickness == "12").Sheets, "halbe Tafel");
            decimal kg = inv.Items.Sum(i => (i.Sheets ?? 0) * (i.WeightPerSheet ?? 0));
            Equal(13258.54m, Math.Round(kg, 2), "Gewicht aller losen Tafeln");
        }

        private static void Groups_()
        {
            Inventory inv = SeedData.Create();
            List<SheetItem> s = inv.Sorted();
            Equal("1.0330 1 mm", s[0].DisplayName, "erste Zeile");
            Equal("1.0330 1,5 mm", s[1].DisplayName, "1,5 nach 1");
            Equal("1.0332 4 mm", s[4].DisplayName, "Paketware DD11");
            Equal("1.0332 15 mm", s[10].DisplayName, "15 mm nach 12 mm (numerisch sortiert)");
            Equal("1.4301 2 mm gebürstet", s[17].DisplayName, "gebürstet nach normal");
            Equal("EN AW-5754 2,5-4 mm", s[34].DisplayName, "letzte Zeile");

            string g = inv.EnsureGroup("edelstahl");
            Equal("Edelstahl", g, "Gruppe ohne Groß/Klein-Unterschied gefunden");
            Equal(6, inv.Groups.Count, "keine doppelte Gruppe");
            inv.EnsureGroup("Kupfer");
            Equal(7, inv.Groups.Count, "neue Gruppe");
            Equal(6, inv.UsedGroups().Count(), "leere Gruppe wird nicht angezeigt");

            inv.RenameGroup("Aluminium NOX", "Alu eloxiert");
            Check(inv.Items.Any(i => i.Group == "Alu eloxiert"), "Umbenennen ändert Positionen");
            inv.MoveGroup("Alu Duett W2", -1);
            Equal("Alu Duett W2", inv.UsedGroups().ElementAt(4), "Gruppe nach oben");
            inv.RenameGroup("Alu Duett W2", "Aluminium");
            Equal(6 - 1, inv.UsedGroups().Count(), "Umbenennen in vorhandene Gruppe führt zusammen");
        }

        private static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "blechbestand-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        private static void Store_()
        {
            string dir = TempDir();
            try
            {
                var store = new DataStore(Path.Combine(dir, "bestand.xml")) { UserName = "Laser", MachineName = "LASER01" };
                store.CreateIfMissing(SeedData.Create);
                Check(store.Exists, "Datei angelegt");
                Inventory a = store.Load();
                Equal(35, a.Items.Count, "geladen");
                SheetItem item = a.Items.Single(i => i.Note == "gebürstet");

                Inventory b = store.Modify((inv, log) =>
                {
                    SheetItem it = inv.Find(item.Id);
                    log.Add(it.DisplayName, "Tafeln", Numbers.Format(it.Sheets), "10,5");
                    it.Sheets = 10.5m;
                    it.ChangedAt = new DateTime(2026, 11, 2, 10, 7, 0);
                    it.ChangedBy = "Laser";
                });
                Equal(10.5m, b.Find(item.Id).Sheets, "geänderte Menge");
                Inventory c = store.Load();
                Equal(10.5m, c.Find(item.Id).Sheets, "Menge nach Neuladen");
                Equal("gebürstet", c.Find(item.Id).Note, "Umlaut bleibt erhalten");
                Equal(new DateTime(2026, 11, 2, 10, 7, 0), c.Find(item.Id).ChangedAt, "Zeitstempel");
                Check(c.Find(item.Id).MinPackages == null, "leere Zahl bleibt leer");

                string[] backups = Directory.GetFiles(store.BackupFolder);
                Equal(1, backups.Length, "eine Tagessicherung");
                Equal(11m, new DataStore(backups[0]).Load().Find(item.Id).Sheets, "Sicherung enthält den alten Stand");

                string[] log = File.ReadAllLines(store.LogPath, Encoding.UTF8);
                Equal(2, log.Length, "Verlauf: Kopf + 1 Zeile");
                Check(log[1].Contains(";Laser;LASER01;1.4301 2 mm gebürstet;Tafeln;11;10,5"), "Verlaufszeile: " + log[1]);

                store.Modify((inv, l) => inv.Items.Find(i => i.Material == "1.0330").Sheets = 0);
                Equal(1, Directory.GetFiles(store.BackupFolder).Length, "nur eine Sicherung pro Tag");
                Check(!File.Exists(store.FilePath + ".lock"), "Sperrdatei wieder entfernt");
                Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "keine Zwischendateien übrig");

                string xml = File.ReadAllText(store.FilePath);
                Check(xml.Contains("<Material>1.4301</Material>"), "lesbares XML");

                // Fehlende Datei (z. B. Netzlaufwerk weg): keine Änderung, kein leerer Neuanfang
                var missing = new DataStore(Path.Combine(dir, "weg", "bestand.xml"));
                bool refused = false;
                try { missing.Modify((inv, l) => inv.Items.Add(new SheetItem())); }
                catch (FileNotFoundException) { refused = true; }
                Check(refused && !missing.Exists, "Änderung ohne Datei wird abgelehnt");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void Concurrency_()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "bestand.xml");
                new DataStore(path).CreateIfMissing(SeedData.Create);
                Inventory start = new DataStore(path).Load();
                Guid laserItem = start.Items[0].Id;
                Guid officeItem = start.Items[1].Id;
                Guid shared = start.Items[2].Id;
                decimal laserStart = start.Find(laserItem).Sheets ?? 0;
                decimal officeStart = start.Find(officeItem).Sheets ?? 0;
                decimal sharedStart = start.Find(shared).Packages ?? 0;

                // Zwei „Rechner“ mit eigener DataStore-Instanz ändern gleichzeitig
                const int n = 40;
                Task t1 = Task.Run(() =>
                {
                    var s = new DataStore(path) { UserName = "Laser" };
                    for (int i = 0; i < n; i++)
                        s.Modify((inv, log) => { inv.Find(laserItem).Sheets += 1; inv.Find(shared).Packages += 1; });
                });
                Task t2 = Task.Run(() =>
                {
                    var s = new DataStore(path) { UserName = "Buero" };
                    for (int i = 0; i < n; i++)
                        s.Modify((inv, log) => { inv.Find(officeItem).Sheets += 1; inv.Find(shared).Packages += 1; });
                });
                Task.WaitAll(t1, t2);
                Inventory end = new DataStore(path).Load();
                Equal(laserStart + n, end.Find(laserItem).Sheets, "keine verlorene Änderung (Laser)");
                Equal(officeStart + n, end.Find(officeItem).Sheets, "keine verlorene Änderung (Büro)");
                Equal(sharedStart + 2 * n, end.Find(shared).Packages, "gemeinsame Position zählt beide");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void Mail_()
        {
            Inventory inv = SeedData.Create();
            var lines = new List<OrderLine>
            {
                new OrderLine { Item = inv.Items.Single(i => i.Note == "gebürstet"), Quantity = 10, Unit = Units.Sheets },
                new OrderLine { Item = inv.Items.Single(i => i.Material == "1.0332" && i.Thickness == "4"), Quantity = 1, Unit = Units.Packages },
            };
            Equal(720m, OrderMail.TotalWeight(lines), "Gewicht nur aus Tafeln");
            string html = OrderMail.BuildHtml(lines, "Hallo,\n\nbitte bestellen:", "Danke & Gruß");
            Check(html.Contains("10 Tafeln"), "Menge im HTML");
            Check(html.Contains("1 Paket"), "Paket im HTML");
            Check(html.Contains("0012771"), "Eulanda-Nr. im HTML");
            Check(html.Contains("Danke &amp; Gruß") || html.Contains("Danke &amp; Gru&#223;"), "HTML maskiert");
            Check(html.Contains("Hallo,<br><br>bitte bestellen:"), "Zeilenumbrüche");
            Check(html.Contains("720 kg"), "Gesamtgewicht");

            string text = OrderMail.BuildText(lines, "Hallo", "Danke");
            Check(text.Contains("1. 10 Tafeln 1.4301 2 mm 3000x1500 gebürstet (Edelstahl), Eulanda-Nr. 0012771"), "Textzeile: " + text);

            Equal("Blechbestellung 02.11.2026", OrderMail.Subject("Blechbestellung {datum}", new DateTime(2026, 11, 2)), "Betreff");
            Equal("10 Tafeln am 02.11.2026", OrderMail.OrderNote(lines[0], new DateTime(2026, 11, 2)), "Bestellvermerk");

            Equal("mailto:einkauf@firma.de?subject=Blechbestellung%2002.11.2026",
                OrderMail.BuildMailto("einkauf@firma.de", "", "Blechbestellung 02.11.2026"), "mailto ohne Cc");
            Equal("mailto:einkauf@firma.de,chef@firma.de?cc=lager@firma.de&subject=Bleche%20f%C3%BCr%20KW%2045%20%26%20Rest",
                OrderMail.BuildMailto(" einkauf@firma.de; chef@firma.de ", "lager@firma.de", "Bleche für KW 45 & Rest"), "mailto mit mehreren Empfängern, Umlaut, &");
            Equal("mailto:?subject=X", OrderMail.BuildMailto("", null, "X"), "mailto ohne Empfänger");

            string fragment = "<b>Grüße</b>";
            byte[] cf = OrderMail.BuildClipboardHtml(fragment);
            string all = Encoding.UTF8.GetString(cf);
            int Offset(string key) => int.Parse(all.Split(new[] { key + ":" }, StringSplitOptions.None)[1].Substring(0, 10));
            int sf = Offset("StartFragment"), ef = Offset("EndFragment"), eh = Offset("EndHTML");
            Equal(fragment, Encoding.UTF8.GetString(cf, sf, ef - sf), "Zwischenablage: Fragment-Offsets (UTF-8)");
            Equal(cf.Length, eh, "Zwischenablage: EndHTML");

            Equal("<html><body lang=DE><p>X</p><p>Signatur</p></body></html>",
                OrderMail.InsertIntoBody("<html><body lang=DE><p>Signatur</p></body></html>", "<p>X</p>"), "Liste über der Outlook-Signatur");
            Equal("<html><body><p>X</p></body></html>", OrderMail.InsertIntoBody("", "<p>X</p>"), "leere Mail");
        }
    }
}
