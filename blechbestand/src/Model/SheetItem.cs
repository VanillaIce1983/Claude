using System;
using System.Xml.Serialization;

namespace Blechbestand.Model
{
    /// <summary>Ampelstufe einer Menge gegenüber ihrem Mindestbestand.</summary>
    public enum StockLevel
    {
        /// <summary>Kein Mindestbestand hinterlegt.</summary>
        None = 0,
        Ok = 1,
        /// <summary>Menge entspricht genau dem Mindestbestand (gelb).</summary>
        AtMinimum = 2,
        /// <summary>Menge liegt unter dem Mindestbestand (rot).</summary>
        BelowMinimum = 3,
    }

    /// <summary>Eine Lagerposition, entspricht einer Zeile der ursprünglichen Excel-Tabelle.</summary>
    public class SheetItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Materialgruppe, z. B. „Edelstahl“ (Zwischenüberschrift in der Tabelle).</summary>
        public string Group { get; set; } = "";

        /// <summary>Werkstoff, z. B. „1.4301“ oder „EN AW-5754“.</summary>
        public string Material { get; set; } = "";

        /// <summary>Dicke in mm als Text, damit auch „1,5-2“ (Riffelblech) möglich ist.</summary>
        public string Thickness { get; set; } = "";

        public string Dimension { get; set; } = "3000x1500";

        public decimal? Packages { get; set; }

        public decimal? Sheets { get; set; }

        /// <summary>Artikelnummer in Eulanda.</summary>
        public string ErpNumber { get; set; } = "";

        /// <summary>Gewicht einer Tafel in kg.</summary>
        public decimal? WeightPerSheet { get; set; }

        /// <summary>Zusatz wie „gebürstet“ oder „Folie einseitig“.</summary>
        public string Note { get; set; } = "";

        public decimal? MinPackages { get; set; }

        /// <summary>
        /// Tafeln pro Paket, nur wenn dieses Material anders gepackt wird. Leer = automatisch aus dem
        /// Paketgewicht des Lieferanten (<see cref="Inventory.PackageWeight"/>), abgerundet.
        /// </summary>
        public decimal? SheetsPerPackage { get; set; }

        public decimal? MinSheets { get; set; }

        /// <summary>Vermerk über eine offene Bestellung, z. B. „10 Tafeln am 27.10.2026“.</summary>
        public string OpenOrder { get; set; } = "";

        public DateTime? ChangedAt { get; set; }

        public string ChangedBy { get; set; } = "";

        [XmlIgnore]
        public decimal ThicknessValue => Numbers.FirstNumber(Thickness) ?? 0m;

        [XmlIgnore]
        public StockLevel PackagesLevel => Levels.Of(Packages, MinPackages);

        [XmlIgnore]
        public StockLevel SheetsLevel => Levels.Of(Sheets, MinSheets);

        /// <summary>Die schlechtere der beiden Ampeln.</summary>
        [XmlIgnore]
        public StockLevel Level => PackagesLevel > SheetsLevel ? PackagesLevel : SheetsLevel;

        [XmlIgnore]
        public bool IsLow => Level == StockLevel.AtMinimum || Level == StockLevel.BelowMinimum;

        /// <summary>Kurzbezeichnung, z. B. „1.4301 2 mm gebürstet“.</summary>
        [XmlIgnore]
        public string DisplayName
        {
            get
            {
                string s = Material.Trim();
                if (!string.IsNullOrWhiteSpace(Thickness)) s += " " + Thickness.Trim() + " mm";
                if (!string.IsNullOrWhiteSpace(Note)) s += " " + Note.Trim();
                return s.Trim();
            }
        }

        /// <summary>Bestand als Text, z. B. „1 Paket + 30 Tafeln“.</summary>
        [XmlIgnore]
        public string StockText
        {
            get
            {
                string p = Packages.HasValue && Packages.Value != 0 ? Units.Format(Packages.Value, Units.Packages) : null;
                string t = Sheets.HasValue && Sheets.Value != 0 ? Units.Format(Sheets.Value, Units.Sheets) : null;
                if (p != null && t != null) return p + " + " + t;
                if (p != null) return p;
                if (t != null) return t;
                return "leer";
            }
        }

        /// <summary>Mindestbestand als Text, z. B. „2 Tafeln“.</summary>
        [XmlIgnore]
        public string MinimumText
        {
            get
            {
                string p = MinPackages.HasValue ? Units.Format(MinPackages.Value, Units.Packages) : null;
                string t = MinSheets.HasValue ? Units.Format(MinSheets.Value, Units.Sheets) : null;
                if (p != null && t != null) return p + " / " + t;
                return p ?? t ?? "";
            }
        }

        /// <summary>Bevorzugte Bestelleinheit: Pakete, wenn das Material paketweise geführt wird.</summary>
        [XmlIgnore]
        public string PreferredUnit
        {
            get
            {
                if (MinPackages.HasValue && !MinSheets.HasValue) return Units.Packages;
                if (!MinSheets.HasValue && (Packages ?? 0) > 0 && (Sheets ?? 0) == 0) return Units.Packages;
                return Units.Sheets;
            }
        }

        /// <summary>Tafeln in einem Paket: eigener Wert, sonst Paketgewicht ÷ Tafelgewicht, abgerundet.</summary>
        public decimal? PackageSheets(decimal packageWeight) =>
            SheetsPerPackage.HasValue && SheetsPerPackage.Value > 0 ? SheetsPerPackage : Packaging.SheetsPerPackage(WeightPerSheet, packageWeight);

        /// <summary>Gewicht eines Pakets in kg (Tafeln pro Paket × Tafelgewicht).</summary>
        public decimal? PackageWeight(decimal packageWeight)
        {
            decimal? n = PackageSheets(packageWeight);
            return n.HasValue && WeightPerSheet.HasValue ? n.Value * WeightPerSheet.Value : (decimal?)null;
        }

        public SheetItem Clone() => (SheetItem)MemberwiseClone();

        /// <summary>„1.5 mm“ → „1,5“.</summary>
        public static string CleanThickness(string text) =>
            (text ?? "").Trim().Replace('.', ',').Replace(" mm", "").Replace("mm", "").Trim();

        /// <summary>„3000 × 1500“ → „3000x1500“.</summary>
        public static string CleanDimension(string text) =>
            (text ?? "").Trim().Replace(" ", "").Replace("×", "x").Replace("X", "x");

        /// <summary>„1.0038“, „1.0038 – S235JR, …“ und „S235JR“ sind derselbe Werkstoff.</summary>
        public static bool SameMaterial(string a, string b)
        {
            a = (a ?? "").Trim();
            b = (b ?? "").Trim();
            if (string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase)) return true;
            CatalogMaterial ca = MaterialCatalog.Find(a);
            return ca != null && ReferenceEquals(ca, MaterialCatalog.Find(b));
        }

        /// <summary>Gleicher Werkstoff, gleiche Dicke, gleiches Format und gleiche Bemerkung (Gruppe egal).</summary>
        public bool SameSheetAs(SheetItem other) =>
            other != null
            && SameMaterial(Material, other.Material)
            && string.Equals(CleanThickness(Thickness), CleanThickness(other.Thickness), StringComparison.CurrentCultureIgnoreCase)
            && string.Equals(CleanDimension(Dimension), CleanDimension(other.Dimension), StringComparison.CurrentCultureIgnoreCase)
            && string.Equals((Note ?? "").Trim(), (other.Note ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase);

        /// <summary>Ersetzt null-Texte (z. B. aus älteren Dateien) durch leere Texte.</summary>
        public void Normalize()
        {
            if (Id == Guid.Empty) Id = Guid.NewGuid();
            Group = (Group ?? "").Trim();
            Material = (Material ?? "").Trim();
            Thickness = (Thickness ?? "").Trim();
            Dimension = (Dimension ?? "").Trim();
            ErpNumber = (ErpNumber ?? "").Trim();
            Note = (Note ?? "").Trim();
            OpenOrder = (OpenOrder ?? "").Trim();
            ChangedBy = ChangedBy ?? "";
        }
    }

    /// <summary>Wie der Lieferant packt: ein Paket wiegt höchstens so viel, die Tafelzahl wird abgerundet.</summary>
    public static class Packaging
    {
        public const decimal DefaultPackageWeight = 2500m;

        /// <summary>z. B. 2.500 kg ÷ 144 kg = 17,4 → 17 Tafeln. Mindestens 1, null ohne Tafelgewicht oder Paketgewicht.</summary>
        public static decimal? SheetsPerPackage(decimal? weightPerSheet, decimal packageWeight)
        {
            if (!weightPerSheet.HasValue || weightPerSheet.Value <= 0 || packageWeight <= 0) return null;
            return Math.Max(1m, Math.Floor(packageWeight / weightPerSheet.Value));
        }
    }

    public static class Levels
    {
        /// <summary>
        /// Rot, wenn die Menge unter dem Mindestbestand liegt, gelb, wenn sie ihn genau erreicht.
        /// Eine leere Menge zählt als 0.
        /// </summary>
        public static StockLevel Of(decimal? value, decimal? minimum)
        {
            if (!minimum.HasValue) return StockLevel.None;
            decimal v = value ?? 0m;
            if (v < minimum.Value) return StockLevel.BelowMinimum;
            if (v == minimum.Value) return StockLevel.AtMinimum;
            return StockLevel.Ok;
        }
    }

    public static class Units
    {
        public const string Sheets = "Tafeln";
        public const string Packages = "Pakete";

        /// <summary>„1 Tafel“, „1,5 Tafeln“, „3 Pakete“.</summary>
        public static string Format(decimal quantity, string unit)
        {
            bool one = quantity == 1m;
            string word = unit == Packages ? (one ? "Paket" : "Pakete") : (one ? "Tafel" : "Tafeln");
            return Numbers.Format(quantity) + " " + word;
        }
    }
}
