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
            Run("Lieferung buchen (Bestellvermerke)", Delivery_);
            Run("Bestellhistorie", History_);
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

        private static void Delivery_()
        {
            List<OrderNoteEntry> one = OrderNotes.Parse("10 Tafeln am 02.11.2026");
            Equal(1, one.Count, "ein Eintrag");
            Equal(10m, one[0].Quantity, "Menge");
            Equal(Units.Sheets, one[0].Unit, "Einheit Tafeln");
            Equal("02.11.2026", one[0].Date, "Datum");

            OrderNoteEntry seed = OrderNotes.Parse("1 Paket am 27.10.2026 (Notiz „T4ST“)").Single();
            Equal(1m, seed.Quantity, "Menge aus Startdaten");
            Equal(Units.Packages, seed.Unit, "Einheit Paket");

            List<OrderNoteEntry> two = OrderNotes.Parse("3 Tafeln am 02.11.2026; 1,5 Pakete am 5.11.26");
            Equal(2, two.Count, "zwei Einträge");
            Equal(1.5m, two[1].Quantity, "Kommazahl");
            Equal("5.11.26", two[1].Date, "kurzes Datum");
            OrderNoteEntry free = OrderNotes.Parse("Lieferant ruft an").Single();
            Check(!free.Quantity.HasValue && free.Unit == null, "freier Text ohne Menge");
            Equal(0, OrderNotes.Parse("").Count, "leer");

            // Komplett geliefert: Eintrag fällt weg
            Check(OrderNotes.ApplyDelivery("10 Tafeln am 02.11.2026", "10 Tafeln am 02.11.2026", 10, Units.Sheets, true, out string n1) && n1 == "", "vollständig: " + n1);
            // Teillieferung: Rest bleibt stehen
            Check(OrderNotes.ApplyDelivery("10 Tafeln am 02.11.2026", "10 Tafeln am 02.11.2026", 8, Units.Sheets, false, out string n2), "Teillieferung gefunden");
            Equal("2 Tafeln am 02.11.2026 (Rest)", n2, "Rest");
            // Mehr geliefert als bestellt: Eintrag fällt weg, auch ohne Häkchen
            OrderNotes.ApplyDelivery("10 Tafeln am 02.11.2026", "10 Tafeln am 02.11.2026", 12, Units.Sheets, false, out string n3);
            Equal("", n3, "Überlieferung");
            // Nur einer von zwei Einträgen
            OrderNotes.ApplyDelivery("3 Tafeln am 02.11.2026; 1 Paket am 05.11.2026", "1 Paket am 05.11.2026", 1, Units.Packages, true, out string n4);
            Equal("3 Tafeln am 02.11.2026", n4, "anderer Eintrag bleibt");
            // Andere Einheit ohne Häkchen: kein Rest berechenbar, Eintrag bleibt
            OrderNotes.ApplyDelivery("1 Paket am 05.11.2026", "1 Paket am 05.11.2026", 25, Units.Sheets, false, out string n5);
            Equal("1 Paket am 05.11.2026", n5, "andere Einheit");
            // Schon gebucht (z. B. anderer Rechner): nicht gefunden
            Check(!OrderNotes.ApplyDelivery("", "10 Tafeln am 02.11.2026", 10, Units.Sheets, true, out _), "nicht mehr vorhanden");

            Check(OrderNotes.LooksComplete(one[0], 10, Units.Sheets), "10 von 10 = erledigt");
            Check(!OrderNotes.LooksComplete(one[0], 8, Units.Sheets), "8 von 10 = Teillieferung");
            Check(OrderNotes.LooksComplete(one[0], 8, Units.Packages), "andere Einheit = erledigt vorschlagen");
            Check(OrderNotes.LooksComplete(free, 5, Units.Sheets), "freier Text = erledigt vorschlagen");

            // Export-Vermerk und Zerlegen passen zusammen
            var line = new OrderLine { Item = new SheetItem(), Quantity = 12, Unit = Units.Sheets };
            OrderNoteEntry roundTrip = OrderNotes.Parse(OrderMail.OrderNote(line, new DateTime(2026, 11, 2))).Single();
            Check(roundTrip.Quantity == 12m && roundTrip.Unit == Units.Sheets && roundTrip.Date == "02.11.2026", "Vermerk aus dem Export wird erkannt");
        }

        private static void History_()
        {
            Inventory inv = SeedData.Create();
            SheetItem brushed = inv.Items.Single(i => i.Note == "gebürstet");
            SheetItem steel4 = inv.Items.Single(i => i.Material == "1.0332" && i.Thickness == "4");
            SheetItem alu = inv.Items.Single(i => i.Material == "3.3535" && i.Thickness == "2");
            var order = OrderNotes.Parse("10 Tafeln am 27.10.2026").Single();

            DeliveryRecord r = DeliveryRecord.Create(brushed, order, 8, Units.Sheets, new DateTime(2026, 11, 2, 15, 30, 0), true, "Laser");
            Equal(new DateTime(2026, 11, 2), r.DeliveredOn, "nur der Tag zählt");
            Equal("1.4301 2 mm gebürstet", r.DisplayName, "Momentaufnahme des Materials");
            Equal("0012771", r.ErpNumber, "Eulanda-Nr. kopiert");
            Equal("27.10.2026", r.OrderedOn, "Bestelldatum aus dem Vermerk");
            Equal("10 Tafeln", r.OrderedText, "bestellte Menge");
            Equal("8 Tafeln", r.DeliveredText, "gelieferte Menge");
            Equal(576m, r.Weight, "Gewicht 8 × 72 kg");
            Check(r.Partial, "Teillieferung");
            Equal((decimal?)null, DeliveryRecord.Create(steel4, null, 1, Units.Packages, DateTime.Today, false, "x").Weight, "Pakete ohne Gewicht");

            var h = new OrderHistory();
            h.Deliveries.Add(DeliveryRecord.Create(alu, null, 5, Units.Sheets, new DateTime(2026, 10, 30), false, "Büro"));
            h.Deliveries.Add(r);
            h.Deliveries.Add(DeliveryRecord.Create(steel4, null, 1, Units.Packages, new DateTime(2026, 11, 2, 7, 0, 0), false, "Laser"));
            List<DeliveryDay> days = h.ByDeliveryDate(inv);
            Equal(2, days.Count, "zwei Liefertage");
            Equal(new DateTime(2026, 11, 2), days[0].Date, "neuester Tag zuerst");
            Equal(2, days[0].Records.Count, "gleicher Tag zusammengefasst");
            Equal("1.0332", days[0].Records[0].Material, "Stahl vor Edelstahl (Reihenfolge der Gruppen)");
            Equal(576m, days[0].TotalWeight, "Tagesgewicht nur aus Tafeln");
            Equal(1, h.ByDeliveryDate(inv, h.Deliveries.Where(x => x.Group == "Aluminium")).Count, "gefilterte Gruppierung");

            // Nach Bestelldatum: Gelieferte und noch offene Positionen derselben Bestellung zusammen
            Equal(new DateTime(2026, 10, 27), OrderNotes.ParseDate("27.10.2026"), "Datum lesen");
            Equal(new DateTime(2026, 11, 5), OrderNotes.ParseDate("5.11.26"), "kurzes Datum lesen");
            Equal((DateTime?)null, OrderNotes.ParseDate("bald"), "unlesbares Datum");
            Inventory inv2 = SeedData.Create(); // offen: 1.0332 4 mm (1 Paket 27.10.) und EN AW-5005A (10 Tafeln 27.10.)
            SheetItem nox = inv2.Items.Single(i => i.Material == "EN AW-5005A");
            nox.OpenOrder = "2 Tafeln am 27.10.2026 (Rest)";
            inv2.Items.Single(i => i.Material == "1.0330" && i.Thickness == "1").OpenOrder = "3 Tafeln am 02.11.2026";
            inv2.Items.Single(i => i.Material == "1.0226" && i.Thickness == "3").OpenOrder = "Lieferant meldet sich";
            var deliveredNox = DeliveryRecord.Create(nox, OrderNotes.Parse("10 Tafeln am 27.10.2026").Single(), 8, Units.Sheets, new DateTime(2026, 10, 30), true, "Laser");
            var deliveredAlu = DeliveryRecord.Create(alu, OrderNotes.Parse("5 Tafeln am 27.10.2026").Single(), 5, Units.Sheets, new DateTime(2026, 11, 2), false, "Büro");
            List<OrderGroup> orders = OrderHistory.ByOrderDate(inv2, new[] { deliveredNox, deliveredAlu }, OrderGroupLine.OpenLines(inv2));
            Equal(3, orders.Count, "drei Bestellungen (02.11., 27.10., ohne Datum)");
            Equal(new DateTime(2026, 11, 2), orders[0].Date, "neueste Bestellung zuerst");
            Equal((DateTime?)null, orders[2].Date, "ohne Bestelldatum am Ende");
            OrderGroup oct = orders[1];
            Equal(new DateTime(2026, 10, 27), oct.Date, "Bestellung vom 27.10.");
            Equal(4, oct.Lines.Count, "1 Paket offen, Alu geliefert, NOX geliefert + Rest offen");
            Equal(2, oct.DeliveredCount, "zwei geliefert");
            Equal(2, oct.OpenCount, "zwei offen");
            int iDelivered = oct.Lines.FindIndex(l => !l.IsOpen && l.Material == "EN AW-5005A");
            int iRest = oct.Lines.FindIndex(l => l.IsOpen && l.Material == "EN AW-5005A");
            Equal(iDelivered + 1, iRest, "Teillieferung und Rest stehen untereinander");
            Equal("1.0332", oct.Lines[0].Material, "Stahl zuerst");
            Equal(5m * 24.75m + 8m * 24.75m, oct.DeliveredWeight, "Gewicht nur der gelieferten Tafeln");

            string dir = TempDir();
            try
            {
                var store = new DataStore(Path.Combine(dir, "bestand.xml")) { UserName = "Laser" };
                store.CreateIfMissing(SeedData.Create);
                Equal(0, store.LoadHistory().Deliveries.Count, "ohne Datei leere Historie");
                Check(!File.Exists(store.HistoryPath), "Historie-Datei entsteht erst beim ersten Eintrag");

                Guid id = store.Load().Items.Single(i => i.Note == "gebürstet").Id;
                store.Modify((i2, hist, log) =>
                {
                    SheetItem it = i2.Find(id);
                    it.Sheets += 8;
                    hist.Deliveries.Add(DeliveryRecord.Create(it, order, 8, Units.Sheets, new DateTime(2026, 11, 2), false, "Laser"));
                }, out OrderHistory saved);
                Equal(1, saved.Deliveries.Count, "Eintrag zurückgegeben");
                Equal("bestand_historie.xml", Path.GetFileName(store.HistoryPath), "Dateiname der Historie");
                Check(File.Exists(store.HistoryPath), "Historie-Datei angelegt");
                Check(!File.ReadAllText(store.FilePath).Contains("Delivery"), "Historie steckt nicht in bestand.xml");
                OrderHistory loaded = store.LoadHistory();
                Equal(1, loaded.Deliveries.Count, "Historie gelesen");
                Equal("1.4301 2 mm gebürstet", loaded.Deliveries[0].DisplayName, "Eintrag vollständig");
                Equal(19m, store.Load().Find(id).Sheets, "Bestand mitgebucht");

                // Normale Änderungen (auch von älteren Programmversionen) lassen die Historie unberührt
                DateTime before = store.HistoryLastWriteUtc;
                store.Modify((i2, log) => i2.Find(id).Sheets = 5);
                Equal(before, store.HistoryLastWriteUtc, "Historie bei normaler Änderung unverändert");
                Equal(1, store.LoadHistory().Deliveries.Count, "Eintrag bleibt");

                // Zweiter Eintrag am nächsten Tag, dann einen löschen
                store.Modify((i2, hist, log) => hist.Deliveries.Add(DeliveryRecord.Create(i2.Find(id), null, 2, Units.Sheets, new DateTime(2026, 11, 3), false, "Büro")), out _);
                Equal(2, store.LoadHistory().ByDeliveryDate(null).Count, "zwei Tage");
                store.Modify((i2, hist, log) => hist.Deliveries.RemoveAll(d => d.DeliveredOn == new DateTime(2026, 11, 3)), out _);
                Equal(1, store.LoadHistory().Deliveries.Count, "Eintrag gelöscht");
                Check(Directory.GetFiles(store.BackupFolder, "bestand_historie_*.xml").Length == 1, "Tagessicherung der Historie");
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
