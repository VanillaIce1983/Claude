using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Normal Cut and legacy Insert Bends (blech-plan S6): the scalar readers used at capture and for the
    /// read-back (the face groups of a normal cut compared as sets of sets inside the copy's selection
    /// access, where the faces are valid), the values of FlattenBends and ProcessBends, the folded state of
    /// a legacy part (ProcessBends unsuppressed – the other way round from a flat pattern), and the
    /// InsertBends2 wrapper fallback route B reuses (S7a).
    /// </summary>
    internal static partial class SwSheetMetal
    {
        // ---- normal cut

        public static void ReadNormalCutScalars(SMNormalCutFeatureData2 d, NormalCutRecord r)
        {
            r.AutoPropagation = d.AutoPropagation;
            r.OptimizeGeometry = d.OptimizeGeometry;
            r.Parameters = d.NormalCutParameters;
            r.LinkToKFactor = d.LinkToKFactor;
            r.LayerAdjustment = d.LayerAdjustmentValue;
        }

        /// <summary>
        /// The face groups of a normal cut under the open selection access: each group's faces as faces; null if
        /// a group does not read or holds anything else.
        /// </summary>
        public static List<List<Face2>>? NormalCutGroups(SMNormalCutFeatureData2 d)
        {
            var result = new List<List<Face2>>();
            if (!((object)d.GetGroupNames() is object[] names)) return result;
            foreach (string name in names.OfType<string>())
            {
                if (!((object)d.GetGroupByName(name) is SMNormalCutGroupData group)) return null;
                List<Face2>? faces = FacesOf((object)group.Faces);
                if (faces == null || faces.Count == 0) return null;
                result.Add(faces);
            }
            return result;
        }

        /// <summary>
        /// The normal cut as it is now: its values, whether its groups are <paramref name="wanted"/> (mirrored) as sets
        /// of sets, and – by <paramref name="sameReference"/>, asked inside the access with the offset plane or the
        /// direction the cut names (null: none) – whether its reference is the wanted one. Null if its data do not open.
        /// </summary>
        public static NormalCutRecord? ReadBackNormalCut(ModelDoc2 doc, Feature f, IReadOnlyList<IReadOnlyList<FaceSignature>> wanted,
            Func<object?, bool>? sameReference, out bool sameGroups, out int groups, out bool referenceOk)
        {
            sameGroups = false;
            groups = -1;
            referenceOk = true;
            var d = (object)f.GetDefinition() as SMNormalCutFeatureData2;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var r = new NormalCutRecord(f.Name, "NormalCut");
                ReadNormalCutScalars(d, r);
                List<List<Face2>>? got = NormalCutGroups(d);
                if (got != null)
                {
                    groups = got.Count;
                    List<IReadOnlyList<FaceSignature>> sigs = got.Select(g => (IReadOnlyList<FaceSignature>)g.Select(TopologyCapture.Face).ToList()).ToList();
                    sameGroups = NormalCutRules.SameGroups(wanted, sigs, (g, k, p) => TopologyCapture.DistanceToFace(got[g][k], p));
                }
                if (sameReference != null)
                {
                    object? reference = r.Parameters == NormalCutParameterTypes.OffsetPlane ? (object)d.OffsetPlaneReference : (object)d.CutDirection;
                    referenceOk = sameReference(reference);
                }
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        // ---- legacy Insert Bends

        /// <summary>
        /// FlattenBends or ProcessBends (IBendsFeatureData) under its own AccessSelections – the state before it:
        /// whether it uses the SheetMetal's radius and allowance and, where not, its own; and its fixed face through
        /// <paramref name="fixedFace"/> (a FlattenBends has one), read while the access is open. Null if its data do not open.
        /// </summary>
        public static BendValues? ReadLegacyBends(ModelDoc2 doc, Feature f, Func<object?, GeometryRef?>? fixedFace, out GeometryRef? fixedRef)
        {
            fixedRef = null;
            var d = (object)f.GetDefinition() as BendsFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var v = new BendValues
                {
                    Name = f.Name,
                    UseDefaultRadius = d.UseDefaultBendRadius,
                    Radius = d.BendRadius,
                    UseDefaultAllowance = d.UseDefaultBendAllowance,
                };
                if (!v.UseDefaultAllowance && (object)d.GetCustomBendAllowance() is CustomBendAllowance cba) v.Allowance = ReadAllowance(cba);
                if (fixedFace != null)
                {
                    try
                    {
                        fixedRef = fixedFace((object)d.GetFixedFace());
                    }
                    catch (Exception)
                    {
                        // The SheetMetal's fixed reference is the first source; this one only stands in.
                        fixedRef = null;
                    }
                }
                return v;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>The fixed face of a copy's FlattenBends against the wanted one (mirrored); null if it does not read.</summary>
        public static bool? SameLegacyFixedFace(ModelDoc2 doc, Feature flattenBends, FaceSignature wanted)
        {
            var d = (object)flattenBends.GetDefinition() as BendsFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                return (object)d.GetFixedFace() is Face2 face ? SameFaces(new[] { wanted }, new List<Face2> { face }) : (bool?)null;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>
        /// IPartDoc.InsertBends2 (A1 §8, B52) with the fixed face or edge selected – with mark 0, then 1. The return
        /// value does not count: an attempt worked if what is new since <paramref name="before"/> holds a SheetMetal and,
        /// with <paramref name="flatten"/>, a FlattenBends and a ProcessBends. A failed attempt is removed before the
        /// next. Returns what it made; empty if no attempt worked, with the reasons in <paramref name="tried"/>.
        /// </summary>
        public static List<Feature> InsertBends(ModelDoc2 doc, object fixedEntity, double radius, InsertBendsArguments args, bool autoRelief, bool flatten,
            HashSet<string> before, List<string> tried)
        {
            if (!(doc is PartDoc part))
            {
                tried.Add("kein Teil");
                return new List<Feature>();
            }
            foreach (int mark in new[] { 0, 1 })
            {
                string? failure = null;
                try
                {
                    SwSelect.Clear(doc);
                    if (SwSelect.Select(doc, fixedEntity, mark, false))
                        part.InsertBends2(radius, args.BendTable, args.KFactor, args.BendAllowance, autoRelief, args.OffsetRatio, flatten);
                    else failure = "feste Fläche ließ sich nicht wählen";
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }
                finally
                {
                    SwSelect.Clear(doc);
                }
                List<Feature> made = NewSince(doc, before);
                bool complete = made.Any(f => f.GetTypeName2() == "SheetMetal") &&
                                (!flatten || (made.Any(f => f.GetTypeName2() == "FlattenBends") && made.Any(f => f.GetTypeName2() == "ProcessBends")));
                if (complete) return made;
                tried.Add($"Markierung {mark}: {failure ?? (made.Count == 0 ? "nichts entstanden" : "unvollständig (" + string.Join(", ", made.Select(f => f.Name)) + ")")}");
                List<string> left = RemoveNew(doc, before);
                if (left.Count > 0)
                {
                    tried.Add($"nicht wieder entfernt: {string.Join(", ", left.Select(n => $"„{n}“"))}");
                    break;
                }
            }
            return new List<Feature>();
        }

        // ---- folded and flattened

        /// <summary>
        /// Flattened: a flat pattern unsuppressed, a ProcessBends of legacy Insert Bends suppressed on its own (the part
        /// shows the state FlattenBends left). A ProcessBends suppressed along with a suppressed parent – its FlattenBends
        /// or SheetMetal, Insert Bends switched off as a whole – is not a state: it is left alone, and the collector skips
        /// what is suppressed. Anything else is never flattened.
        /// </summary>
        public static bool IsFlattened(ModelDoc2 doc, Feature f) => f.GetTypeName2() switch
        {
            "FlatPattern" => !IsSuppressed(f),
            "ProcessBends" => IsSuppressed(f) && !LegacyParents(doc, f).Any(IsSuppressed),
            _ => false,
        };

        /// <summary>
        /// Flattens or folds a flat pattern or a ProcessBends (<see cref="IsFlattened"/>); true if it is in the wanted state
        /// afterwards. A ProcessBends is bent back with its dependents: what was sketched on the bent faces after it is its
        /// child and was suppressed with it (folding again suppresses them with it).
        /// </summary>
        public static bool SetFlattened(ModelDoc2 doc, Feature f, bool flattened) => f.GetTypeName2() switch
        {
            "FlatPattern" => SetSuppressed(doc, f, !flattened),
            "ProcessBends" => SetSuppressed(doc, f, flattened, withDependents: !flattened),
            _ => !flattened,
        };

        /// <summary>
        /// The parents of a ProcessBends (B52: its FlattenBends, and through it the SheetMetal); if they name no FlattenBends,
        /// also the nearest FlattenBends and SheetMetal before it at the top level – as the tree finds them (<see cref="SheetMetalTree.LegacyAnchors"/>).
        /// </summary>
        private static List<Feature> LegacyParents(ModelDoc2 doc, Feature processBends)
        {
            List<Feature> parents = processBends.GetParents() is object[] ps ? ps.OfType<Feature>().ToList() : new List<Feature>();
            if (parents.Any(p => p.GetTypeName2() == "FlattenBends")) return parents;
            Feature? flatten = null, sheetMetal = null;
            bool found = false;
            foreach (Feature f in SwDoc.TopLevelFeatures(doc))
            {
                if (f.Name == processBends.Name)
                {
                    found = true;
                    break;
                }
                switch (f.GetTypeName2())
                {
                    case "SheetMetal":
                        sheetMetal = f;
                        flatten = null;
                        break;
                    case "FlattenBends":
                        flatten = f;
                        break;
                }
            }
            if (found) parents.AddRange(new[] { flatten, sheetMetal }.OfType<Feature>());
            return parents;
        }
    }
}
