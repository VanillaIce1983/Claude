using System.Collections.Generic;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>One write of a feature's bend allowance, in the order <see cref="AllowanceRules.WriteOrder"/> gives.</summary>
    public enum AllowanceStep
    {
        /// <summary>UseDefaultBendAllowance = true: the feature takes the default; nothing else is written.</summary>
        UseDefault,

        /// <summary>UseDefaultBendAllowance = false, first – otherwise the values silently revert.</summary>
        UseOwn,

        KFactor,
        BendAllowance,
        BendDeduction,
        BendTableFile,

        /// <summary>Late-bound (newer versions).</summary>
        BendCalculationTableFile,

        /// <summary>ICustomBendAllowance.Type, always after the values.</summary>
        Type,

        /// <summary>SetCustomBendAllowance with the filled object.</summary>
        Apply,
    }

    /// <summary>
    /// How a flange or bend feature gets its own bend allowance (blech-plan §1.4): at creation
    /// where the call takes it, otherwise in one edit of its own – never together with the
    /// geometric edit, so a refused value cannot take the geometry with it.
    /// </summary>
    public static class AllowanceRules
    {
        /// <summary>
        /// False where SOLIDWORKS refuses the kind on that feature type (GetTypeName2): a jog takes
        /// a K-factor but no bend table and no bend calculation table. Then: a note, no attempt.
        /// </summary>
        public static bool Settable(string featureType, BendAllowanceKind kind) =>
            !(featureType == "Jog" && (kind == BendAllowanceKind.BendTable || kind == BendAllowanceKind.BendCalculationTable));

        /// <summary>The note for an allowance <see cref="Settable"/> refuses.</summary>
        public static string NotSettable(BendAllowanceData own, System.IFormatProvider? culture = null) =>
            $"Die eigene Biegezugabe ({SheetMetalCompare.Describe(own, culture)}) lässt sich bei diesem Feature-Typ nicht setzen; die Kopie verwendet die Standard-Biegezugabe.";

        /// <summary>
        /// The writes for a feature's own allowance (null: the default): UseDefaultBendAllowance
        /// = false first, then the value that belongs to the kind, the kind (Type) last, then
        /// SetCustomBendAllowance (the help of ICustomBendAllowance: set Type last).
        /// </summary>
        public static IReadOnlyList<AllowanceStep> WriteOrder(BendAllowanceData? own)
        {
            if (own == null) return new[] { AllowanceStep.UseDefault };
            AllowanceStep value = own.Kind switch
            {
                BendAllowanceKind.KFactor => AllowanceStep.KFactor,
                BendAllowanceKind.Direct => AllowanceStep.BendAllowance,
                BendAllowanceKind.Deduction => AllowanceStep.BendDeduction,
                BendAllowanceKind.BendTable => AllowanceStep.BendTableFile,
                _ => AllowanceStep.BendCalculationTableFile,
            };
            return new[] { AllowanceStep.UseOwn, value, AllowanceStep.Type, AllowanceStep.Apply };
        }
    }
}
