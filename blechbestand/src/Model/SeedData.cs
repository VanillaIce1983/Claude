namespace Blechbestand.Model
{
    /// <summary>
    /// Startbestand aus „Blechbestand.xlsx“ (Stand 02.11.2026). Wird nur beim allerersten Start verwendet,
    /// wenn es noch keine Datendatei gibt.
    /// Die Mindestbestände stammen aus der bedingten Formatierung der Excel-Liste:
    /// Rot = unter Mindestbestand, Gelb = Mindestbestand genau erreicht.
    /// </summary>
    public static class SeedData
    {
        public static Inventory Create()
        {
            var inv = new Inventory();
            const string steel = "Stahlblech DC01/DD11";
            const string galv = "Verzinktes Blech";
            const string stainless = "Edelstahl";
            const string alu = "Aluminium";
            const string aluNox = "Aluminium NOX";
            const string duett = "Alu Duett W2";
            inv.Groups.AddRange(new[] { steel, galv, stainless, alu, aluNox, duett });

            // Gruppe, Werkstoff, Dicke, Pakete, Tafeln, Eulanda-Nr., kg/Tafel, Zusatz, Min. Pakete, Min. Tafeln, offene Bestellung
            Add(inv, steel, "1.0330", "1", null, 1, "0013484", 36m, "", null, 2);
            Add(inv, steel, "1.0330", "1,5", null, 20, "0011492", 54m, "", null, 6);
            Add(inv, steel, "1.0330", "2", 2, null, "0011493", 72m, "", 2, null);
            Add(inv, steel, "1.0330", "3", 4, null, "0011495", 107.64m, "", 3, null);
            Add(inv, steel, "1.0332", "4", 1, null, "0011497", 144m, "", 2, null, "1 Paket am 27.10.2026 (Notiz „T4ST“)");
            Add(inv, steel, "1.0332", "5", 1, null, "0003348", 180m, "", 2, null);
            Add(inv, steel, "1.0332", "6", 2, null, "0011499", 216m, "", 2, null);
            Add(inv, steel, "1.0332", "8", null, 2, "0012773", 288m, "", null, 2);
            Add(inv, steel, "1.0332", "10", null, 3, "0013741", 360m, "", null, 2);
            Add(inv, steel, "1.0332", "12", null, 1.5m, "0014242", 432m, "", null, 1);
            Add(inv, steel, "1.0332", "15", null, 1, "0013852", 540m, "", null, 1);

            Add(inv, galv, "1.0226", "1,25", 1, 30, "0001209", 45m, "", null, 11);
            Add(inv, galv, "1.0226", "2", null, 10, "0001977", 72m, "", null, 11);
            Add(inv, galv, "1.0226", "3", null, 3, "0014746", 107.64m, "", null, 6);

            Add(inv, stainless, "1.4301", "1", null, 17, "0010597", 36m, "", null, 2);
            Add(inv, stainless, "1.4301", "1,5", null, 3, "0013100", 54m, "", null, 4);
            Add(inv, stainless, "1.4301", "2", null, 11, "0004991", 72m, "", null, 4);
            Add(inv, stainless, "1.4301", "2", null, 11, "0012771", 72m, "gebürstet", null, 4);
            Add(inv, stainless, "1.4301", "3", null, 11, "0012131", 108m, "", null, 4);
            Add(inv, stainless, "1.4301", "4", null, 1, "0014471", 144m, "", null, 2);
            Add(inv, stainless, "1.4301", "5", null, 1, "0014472", 180m, "", null, 4);
            Add(inv, stainless, "1.4301", "6", null, 1, "", 216m, "", null, 2);
            Add(inv, stainless, "1.4301", "8", null, 1, "0013012", 288m, "", null, 2);
            Add(inv, stainless, "1.4301", "10", null, 2, "", 360m, "", null, 2);
            Add(inv, stainless, "1.4301", "12", null, 1, "0014365", 432m, "", null, 1);

            Add(inv, alu, "3.3535", "1,5", null, 3, "0001979", 18.56m, "", null, 4);
            Add(inv, alu, "3.3535", "2", null, 14, "0001428", 24.75m, "", null, 4);
            Add(inv, alu, "3.3535", "3", null, 11, "0001996", 37.13m, "", null, 4);
            Add(inv, alu, "3.3535", "4", null, 1, "0001997", 49.50m, "", null, 2);
            Add(inv, alu, "3.3535", "5", null, 1, "", 61.88m, "", null, 1);
            Add(inv, alu, "3.3535", "6", null, 1, "", 74.25m, "", null, 1);
            Add(inv, alu, "3.3535", "8", null, 1, "", 99m, "", null, 1);

            Add(inv, aluNox, "EN AW-5005A", "2", null, 5, "0013035", 24.75m, "", null, 2, "10 Tafeln am 27.10.2026 (Notiz „T2ALNOX“)");

            Add(inv, duett, "EN AW-5754", "1,5-2", null, 1, "0000095", 22.23m, "", null, 2);
            Add(inv, duett, "EN AW-5754", "2,5-4", null, 4, "0001202", 34.60m, "", null, 2);

            inv.Normalize();
            return inv;
        }

        private static void Add(Inventory inv, string group, string material, string thickness,
            decimal? packages, decimal? sheets, string erp, decimal weight, string note,
            decimal? minPackages, decimal? minSheets, string openOrder = "")
        {
            inv.Items.Add(new SheetItem
            {
                Group = group,
                Material = material,
                Thickness = thickness,
                Dimension = "3000x1500",
                Packages = packages,
                Sheets = sheets,
                ErpNumber = erp,
                WeightPerSheet = weight,
                Note = note,
                MinPackages = minPackages,
                MinSheets = minSheets,
                OpenOrder = openOrder,
            });
        }
    }
}
