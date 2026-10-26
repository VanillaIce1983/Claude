using System;
using System.Collections.Generic;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>Report text for feature types (IFeature.GetTypeName2) that are not rebuilt.</summary>
    public static class UnsupportedFeatures
    {
        private const string MirrorApi = "wird bewusst nicht nachgebaut, weil Spiegelwerk die Spiegel-API nicht verwendet.";

        /// <summary>Sheet-metal features and their folders (type names from the API help of IFeature.GetTypeName2).</summary>
        private static readonly HashSet<string> SheetMetal = new HashSet<string>(StringComparer.Ordinal)
        {
            "SheetMetal", "SMBaseFlange", "EdgeFlange", "SMMiteredFlange", "Hem", "Jog", "SketchBend", "SM3dBend", "OneBend",
            "ToroidalBend", "LoftedBend", "FlatPattern", "FlattenBends", "ProcessBends", "Fold", "UnFold", "BreakCorner",
            "CornerTrim", "CrossBreak", "SMGusset", "NormalCut", "Rip", "SolidToSheetMetal", "FormToolInstance",
            "TemplateSheetMetal", "TemplateFlatPattern",
        };

        public static bool IsSheetMetal(string type) => SheetMetal.Contains(type);

        public static string Reason(string type) => type switch
        {
            // Reached only if the definition does not answer as IAdvancedHoleFeatureData.
            "AdvHoleWzd" or "AdvancedHole" => "Erweiterte Bohrung: Feature-Daten nicht lesbar.",
            "MirrorPattern" => $"Spiegel-Feature im Original: {MirrorApi} " +
                               "Abhilfe: im Original durch die gespiegelten Einzel-Features ersetzen oder nach dem Lauf von Hand spiegeln.",
            "MirrorSolid" => $"Körper spiegeln im Original: {MirrorApi} Abhilfe: die Körper nach dem Lauf im Ziel von Hand spiegeln.",
            "MirrorStock" => $"Teil spiegeln im Original: Das Teil ist von einem Ausgangsteil gespiegelt abgeleitet und {MirrorApi} " +
                             "Abhilfe: Das Ausgangsteil ist bis auf die Lage selbst das Gegenstück.",
            // Reached only if the definition does not answer as the pattern's data interface.
            "CurvePattern" or "SketchPattern" or "TablePattern" or "FillPattern" or "DimPattern" or "VarPattern" => "Muster: Feature-Daten nicht lesbar.",
            _ when IsSheetMetal(type) => "Blech wird nicht unterstützt: Blech-Features werden nicht nachgebaut.",
            _ => $"Feature-Typ {type} wird nicht unterstützt.",
        };
    }
}
