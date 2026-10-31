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

        /// <summary>
        /// Features that carry the base body and are not rebuilt (sheet metal, a part derived by
        /// „Teil spiegeln“): everything after them would fail too, and in an assembly an empty
        /// counterpart would be saved and inserted. A run that collects one is refused before
        /// anything is built.
        /// </summary>
        public static bool RefusesRun(string type) => type == "MirrorStock" || IsSheetMetal(type);

        /// <summary>Why the run was refused (<see cref="RefusesRun"/>).</summary>
        public static string RunRefusal(string feature, string type) => IsSheetMetal(type)
            ? $"Blechteile werden nicht unterstützt (\"{feature}\"). Es wurde nichts gespiegelt."
            : $"\"{feature}\": {Reason(type)} Es wurde nichts gespiegelt.";

        public static string Reason(string type) => type switch
        {
            // Reached only if the definition does not answer as IAdvancedHoleFeatureData.
            "AdvHoleWzd" or "AdvancedHole" => "Erweiterte Bohrung: Feature-Daten nicht lesbar.",
            "MirrorPattern" => $"Spiegel-Feature im Original: {MirrorApi} " +
                               "Abhilfe: im Original durch die gespiegelten Einzel-Features ersetzen oder nach dem Lauf von Hand spiegeln.",
            "MirrorSolid" => $"Körper spiegeln im Original: {MirrorApi} Abhilfe: die Körper nach dem Lauf im Ziel von Hand spiegeln.",
            "MirrorStock" => $"Teil spiegeln im Original: Das Teil ist von einem Ausgangsteil gespiegelt abgeleitet und {MirrorApi} " +
                             "Abhilfe: Das Ausgangsteil ist bis auf die Lage das Gegenstück, aber nur im Stand von „Teil spiegeln“. " +
                             "Features, die danach in diesem Teil angelegt wurden, fehlen ihm; sie gehören gespiegelt in eine Kopie des " +
                             "Ausgangsteils, nicht in das Ausgangsteil selbst (von dort kämen sie über die Verknüpfung auch hierher).",
            // Reached only if the definition does not answer as the pattern's data interface.
            "CurvePattern" or "SketchPattern" or "TablePattern" or "FillPattern" or "DimPattern" or "VarPattern" => "Muster: Feature-Daten nicht lesbar.",
            _ when IsSheetMetal(type) => "Blech wird nicht unterstützt: Blech-Features werden nicht nachgebaut.",
            _ => $"Feature-Typ {type} wird nicht unterstützt.",
        };
    }
}
