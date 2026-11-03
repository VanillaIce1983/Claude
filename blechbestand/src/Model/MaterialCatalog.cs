using System;
using System.Collections.Generic;
using System.Linq;

namespace Blechbestand.Model
{
    /// <summary>Ein Werkstoff aus der eingebauten Werkstoffliste.</summary>
    public sealed class CatalogMaterial
    {
        public CatalogMaterial(string group, string material, string name, string description, decimal density, params string[] aliases)
        {
            Group = group;
            Material = material;
            Name = name ?? "";
            Description = description ?? "";
            Density = density;
            Aliases = aliases ?? new string[0];
        }

        /// <summary>Vorgeschlagene Materialgruppe, z. B. „Edelstahl“.</summary>
        public string Group { get; }

        /// <summary>So wird der Werkstoff in die Liste übernommen, z. B. „1.0038“ oder „Hardox 450“.</summary>
        public string Material { get; }

        /// <summary>Kurzname, z. B. „S235JR“.</summary>
        public string Name { get; }

        public string Description { get; }

        /// <summary>Dichte in kg/dm³ (wie in der Excel-Liste gerechnet: Stahl/Edelstahl 8,0, Alu 2,75).</summary>
        public decimal Density { get; }

        /// <summary>Weitere Namen, unter denen der Werkstoff gefunden wird, z. B. „V2A“.</summary>
        public string[] Aliases { get; }

        /// <summary>„S235JR, Baustahl warmgewalzt“.</summary>
        public string Info => Name.Length > 0 && Description.Length > 0 ? Name + ", " + Description : Name + Description;

        /// <summary>Eintrag in der Auswahlliste, z. B. „1.0038 – S235JR, Baustahl warmgewalzt“.</summary>
        public string Display => Info.Length > 0 ? Material + " – " + Info : Material;

        public override string ToString() => Display;
    }

    /// <summary>
    /// Gängige Werkstoffe für das Laserschneiden. Sie stehen beim Anlegen von Material und bei Sonderpositionen
    /// in der Bestellung zur Auswahl; in den Bestand kommen sie erst, wenn man sie dort anlegt.
    /// </summary>
    public static class MaterialCatalog
    {
        public const string SteelSheet = "Stahlblech DC01/DD11";
        public const string StructuralSteel = "Baustahl";
        public const string HighStrength = "Hochfest / Verschleißfest";
        public const string Galvanized = "Verzinktes Blech";
        public const string Stainless = "Edelstahl";
        public const string Aluminium = "Aluminium";
        public const string CopperBrass = "Kupfer / Messing";

        private const decimal Steel = 8.0m;
        private const decimal Alu = 2.75m;

        public static readonly IReadOnlyList<CatalogMaterial> All = new List<CatalogMaterial>
        {
            // Kaltgewalzt / warmgewalzt gebeizt
            new CatalogMaterial(SteelSheet, "1.0330", "DC01", "kaltgewalzt", Steel),
            new CatalogMaterial(SteelSheet, "1.0338", "DC04", "kaltgewalzt, Tiefziehgüte", Steel),
            new CatalogMaterial(SteelSheet, "1.0332", "DD11", "warmgewalzt, gebeizt", Steel),
            new CatalogMaterial(SteelSheet, "1.0335", "DD13", "warmgewalzt, gebeizt", Steel),

            // Baustahl
            new CatalogMaterial(StructuralSteel, "1.0038", "S235JR", "Baustahl", Steel, "St37"),
            new CatalogMaterial(StructuralSteel, "1.0044", "S275JR", "Baustahl", Steel, "St44"),
            new CatalogMaterial(StructuralSteel, "1.0577", "S355J2+N", "Baustahl", Steel, "S355J2", "St52"),
            new CatalogMaterial(StructuralSteel, "1.0976", "S355MC", "thermomechanisch gewalzt, zum Kaltumformen", Steel),
            new CatalogMaterial(StructuralSteel, "1.0980", "S420MC", "thermomechanisch gewalzt, zum Kaltumformen", Steel),
            new CatalogMaterial(StructuralSteel, "1.8965", "S355J2W", "wetterfest (Corten)", Steel, "Corten", "Cor-Ten"),

            // Hochfest und verschleißfest
            new CatalogMaterial(HighStrength, "1.8974", "S700MC", "hochfest, zum Kaltumformen", Steel, "Strenx 700MC"),
            new CatalogMaterial(HighStrength, "1.8928", "S690QL", "hochfest, vergütet", Steel),
            new CatalogMaterial(HighStrength, "Hardox 400", "", "Verschleißblech 400 HBW", Steel, "XAR 400"),
            new CatalogMaterial(HighStrength, "Hardox 450", "", "Verschleißblech 450 HBW", Steel, "XAR 450"),
            new CatalogMaterial(HighStrength, "Hardox 500", "", "Verschleißblech 500 HBW", Steel, "XAR 500"),

            // Verzinkt
            new CatalogMaterial(Galvanized, "1.0226", "DX51D+Z", "feuerverzinkt", Steel, "DX51D"),
            new CatalogMaterial(Galvanized, "1.0242", "S250GD+Z", "feuerverzinkt, Baustahlgüte", Steel),
            new CatalogMaterial(Galvanized, "DC01+ZE", "", "elektrolytisch verzinkt", Steel),

            // Edelstahl
            new CatalogMaterial(Stainless, "1.4301", "X5CrNi18-10", "V2A, AISI 304", Steel, "V2A", "304", "AISI 304"),
            new CatalogMaterial(Stainless, "1.4307", "X2CrNi18-9", "AISI 304L", Steel, "304L"),
            new CatalogMaterial(Stainless, "1.4404", "X2CrNiMo17-12-2", "V4A, AISI 316L", Steel, "V4A", "316L", "AISI 316L"),
            new CatalogMaterial(Stainless, "1.4571", "X6CrNiMoTi17-12-2", "V4A, AISI 316Ti", Steel, "316Ti"),
            new CatalogMaterial(Stainless, "1.4016", "X6Cr17", "ferritisch, AISI 430", Steel, "430"),
            new CatalogMaterial(Stainless, "1.4509", "X2CrTiNb18", "ferritisch, AISI 441", Steel, "441"),
            new CatalogMaterial(Stainless, "1.4003", "X2CrNi12", "3CR12", Steel, "3CR12"),
            new CatalogMaterial(Stainless, "1.4828", "X15CrNiSi20-12", "hitzebeständig", Steel),

            // Aluminium
            new CatalogMaterial(Aluminium, "EN AW-1050A", "Al99,5", "Reinaluminium", Alu, "Al99.5", "3.0255"),
            new CatalogMaterial(Aluminium, "EN AW-3003", "AlMn1Cu", "", Alu),
            new CatalogMaterial(Aluminium, "EN AW-5005A", "AlMg1", "eloxierfähig", Alu, "AlMg1(B)", "3.3315"),
            new CatalogMaterial(Aluminium, "EN AW-5754", "AlMg3", "", Alu, "AlMg3"),
            new CatalogMaterial(Aluminium, "3.3535", "AlMg3", "alte Werkstoffnummer für EN AW-5754", Alu),
            new CatalogMaterial(Aluminium, "EN AW-5083", "AlMg4,5Mn0,7", "", Alu, "AlMg4,5Mn", "3.3547"),
            new CatalogMaterial(Aluminium, "EN AW-6082", "AlSi1MgMn", "", Alu, "AlMgSi1", "3.2315"),

            // Kupfer und Messing
            new CatalogMaterial(CopperBrass, "CW004A", "Cu-ETP", "Kupfer", 8.9m, "Kupfer", "Cu"),
            new CatalogMaterial(CopperBrass, "CW508L", "CuZn37", "Messing", 8.5m, "Messing", "Ms63"),
        };

        /// <summary>Gängige Blechdicken in mm.</summary>
        public static readonly string[] Thicknesses =
            { "0,5", "0,8", "1", "1,25", "1,5", "2", "2,5", "3", "4", "5", "6", "8", "10", "12", "15", "20", "25" };

        /// <summary>Gängige Tafelformate in mm.</summary>
        public static readonly string[] Dimensions = { "3000x1500", "2500x1250", "2000x1000", "4000x2000", "4000x1500" };

        private static string Key(string s) =>
            new string((s ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '.' && c != '_').ToArray()).ToLowerInvariant();

        /// <summary>
        /// Sucht einen Werkstoff nach Werkstoffnummer, Kurzname oder weiterem Namen (Groß-/Kleinschreibung,
        /// Leerzeichen, Punkte und Bindestriche egal). Versteht auch den Eintrag aus der Auswahlliste.
        /// </summary>
        public static CatalogMaterial Find(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            CatalogMaterial byDisplay = All.FirstOrDefault(m => text.StartsWith(m.Material + " – ", StringComparison.CurrentCultureIgnoreCase)
                                                             || string.Equals(text, m.Display, StringComparison.CurrentCultureIgnoreCase));
            if (byDisplay != null) return byDisplay;
            string k = Key(text);
            return All.FirstOrDefault(m => Key(m.Material) == k)
                ?? All.FirstOrDefault(m => Key(m.Name) == k)
                ?? All.FirstOrDefault(m => m.Aliases.Any(a => Key(a) == k));
        }

        /// <summary>Macht aus dem Listeneintrag „1.0038 – S235JR, …“ wieder „1.0038“; sonst bleibt der Text, wie er ist.</summary>
        public static string Normalize(string text)
        {
            text = (text ?? "").Trim();
            CatalogMaterial m = All.FirstOrDefault(x => text.StartsWith(x.Material + " – ", StringComparison.CurrentCultureIgnoreCase)
                                                     || string.Equals(text, x.Display, StringComparison.CurrentCultureIgnoreCase));
            return m != null ? m.Material : text;
        }
    }
}
