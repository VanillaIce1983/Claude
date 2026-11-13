using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>One way of creating a normal cut (blech-plan S6, B51).</summary>
    public enum NormalCutPath
    {
        /// <summary>CreateDefinition(swFmNormalCut) → ISMNormalCutFeatureData2, a group per CreateGroup, CreateFeature (help, 2019).</summary>
        DataObject,

        /// <summary>
        /// The obsolete AddSMNormalCutType, then per group its faces selected and AddSMNormalCut (its error codes are
        /// those of a face that does not fit), FinishSMNormalCut – the help lists the calls, not their order.
        /// </summary>
        Obsolete,
    }

    /// <summary>
    /// Normal Cut (A1 §5, §8, blech-plan S6): a reflection keeps every value and maps each face of each group
    /// onto its mirror image – face by face, group by group; the offset plane and the direction follow as
    /// references. What SOLIDWORKS made is read back and compared as sets (a reflection may reverse the order
    /// in which faces and groups are listed): every wanted group needs a group of the copy with exactly its
    /// faces. No variants: nothing has a side.
    /// </summary>
    public static class NormalCutRules
    {
        /// <summary>swFeatureNameID_e.swFmNormalCut: the data object of a normal cut.</summary>
        public const int NormalCutId = 115;

        /// <summary>The marks of the data object (help): faces of a group 1, offset plane 4, direction 8.</summary>
        public const int FaceMark = 1, OffsetPlaneMark = 4, DirectionMark = 8;

        /// <summary>The groups mirrored face by face, in their order.</summary>
        public static List<List<FaceSignature>> MirroredGroups(NormalCutRecord r, Reflection mirror) =>
            r.Groups.Select(g => g.Select(f => f.Mirror(mirror)).ToList()).ToList();

        /// <summary>The paths in order: the data object (the help's way, with its 2019 example), then the obsolete calls (B51).</summary>
        public static IReadOnlyList<NormalCutPath> Paths { get; } = new[] { NormalCutPath.DataObject, NormalCutPath.Obsolete };

        public static string PathName(NormalCutPath path) => path == NormalCutPath.DataObject ? "Datenobjekt" : "AddSMNormalCut";

        /// <summary>
        /// The same groups, as sets of sets: each wanted group has its own group in the copy with exactly its
        /// faces (<see cref="FaceSets.Same"/>), nothing is left over. <paramref name="distance"/>(group, face,
        /// point): distance of the point to that face of the copy.
        /// </summary>
        public static bool SameGroups(IReadOnlyList<IReadOnlyList<FaceSignature>> wanted, IReadOnlyList<IReadOnlyList<FaceSignature>> got,
            Func<int, int, Vec3, double> distance)
        {
            double Cost(int i, int j)
            {
                int[]? pairs = FaceSets.Pair(wanted[i], got[j], (k, p) => distance(j, k, p));
                return pairs == null ? double.PositiveInfinity : SetMatch.Total(pairs, (a, b) => FaceSets.Cost(wanted[i][a], got[j][b], distance(j, b, wanted[i][a].Sample)));
            }
            return SetMatch.Pair(wanted.Count, got.Count, Cost) != null;
        }

        /// <summary>The values of the copy against the original's; the adjustment and the K-factor link only with an offset plane, where they mean something.</summary>
        public static Deviations Differences(NormalCutRecord want, NormalCutRecord got, IFormatProvider? culture = null)
        {
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Art", want.Parameters, got.Parameters, ScalarKind.Choice),
                ("Tangentiale Flächen fortsetzen", want.AutoPropagation ? 1 : 0, got.AutoPropagation ? 1 : 0, ScalarKind.Flag),
                ("Geometrie optimieren", want.OptimizeGeometry ? 1 : 0, got.OptimizeGeometry ? 1 : 0, ScalarKind.Flag),
            };
            if (want.Parameters == NormalCutParameterTypes.OffsetPlane)
            {
                values.Add(("An K-Faktor binden", want.LinkToKFactor ? 1 : 0, got.LinkToKFactor ? 1 : 0, ScalarKind.Flag));
                // Linked to the K-factor SOLIDWORKS sets the adjustment itself (0.5, help).
                if (!want.LinkToKFactor) values.Add(("Lage der Versatzebene", want.LayerAdjustment, got.LayerAdjustment, ScalarKind.Factor));
            }
            var result = new Deviations();
            result.Differences.AddRange(SheetMetalCompare.Scalars(values, culture));
            return result;
        }

        public static string KindName(int parameters) => parameters == NormalCutParameterTypes.OffsetPlane ? "Versatzebene" : "Ausdehnung";

        /// <summary>„Normaler Schnitt (Ausdehnung), 2 Flächengruppen, 5 Flächen“.</summary>
        public static string Describe(NormalCutRecord r)
        {
            int faces = r.Groups.Sum(g => g.Count);
            string groups = r.Groups.Count == 1 ? "1 Flächengruppe" : $"{r.Groups.Count} Flächengruppen";
            return $"Normaler Schnitt ({KindName(r.Parameters)}), {groups}, {(faces == 1 ? "1 Fläche" : $"{faces} Flächen")}";
        }

        public static string FaceMissing(int group, int face) => $"Fläche {face} der Flächengruppe {group} im Ziel nicht gefunden.";

        public static string FaceNotOwn(int group, int face) =>
            $"Fläche {face} der Flächengruppe {group} liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.";

        public const string OffsetPlaneMissing = "Versatzebene des normalen Schnitts im Ziel nicht gefunden.";

        public const string OffsetPlaneUnreadable = "Versatzebene des normalen Schnitts im Original nicht lesbar.";

        /// <summary>A direction is optional (extent mode): without it the copy takes SOLIDWORKS' default and says so.</summary>
        public const string DirectionMissing = "Richtung des normalen Schnitts im Ziel nicht gefunden; es gilt die Vorgabe von SOLIDWORKS.";

        public const string DirectionUnreadable = "Richtung des normalen Schnitts im Original nicht lesbar; es gilt die Vorgabe von SOLIDWORKS.";

        public const string ReferenceNotTaken = "Versatzebene oder Richtung des normalen Schnitts ließ sich nicht setzen.";

        public const string NoGroups = "Normaler Schnitt ohne Flächengruppen.";

        /// <summary>No path made a normal cut with the mirrored groups.</summary>
        public static string NotCreated(IEnumerable<string> tried) =>
            $"SOLIDWORKS hat den normalen Schnitt nicht mit den gespiegelten Flächengruppen angelegt ({string.Join("; ", tried)}).";
    }
}
