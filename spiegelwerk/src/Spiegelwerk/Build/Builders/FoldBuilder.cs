using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Unfold and fold (A3 (2) §5.6, P8, blech-plan S5, K25). The mirrored fixed face and every bend –
    /// found by the run's name map, by their mirrored faces or as their owner's only bend
    /// (<see cref="CornerSupport.FindBend"/>); separate body: on mirrored bodies, all before anything is made
    /// – selected in each of the ways of <see cref="FoldRules.Selections"/> (B47), InsertSheetMetalUnfold or
    /// …Fold (no return value: the new feature is what is new since the snapshot, of the type, A3 P14). Read
    /// back: fixed face and bends; if they are not the wanted ones, one edit sets them (safe entities), read
    /// back again; still not: the variant goes and the next follows. Nothing has a direction (A3 §6).
    /// </summary>
    internal sealed class FoldBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is FoldRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (FoldRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();
            string kind = FoldRules.KindName(r.Kind);
            if (r.FixedFace == null) return Fail($"Feste Fläche von „{kind}“ nicht lesbar.", notes);
            FaceSignature wantedFixed = r.FixedFace.Mirror(ctx.Mirror);
            Face2? found = ctx.Topology.FindFace(wantedFixed, notes);
            if (found == null) return Fail(FoldRules.FixedFaceMissing(r.Kind), notes);
            if (ctx.SeparateBody && !CornerSupport.OnOwnBody(ctx, found)) return Fail(FoldRules.FixedFaceNotOwn(r.Kind), notes);
            var fixedFace = (Face2)Safe(found)!;
            string? why;

            var bends = new List<Feature>();
            foreach (BendRef b in r.Bends)
            {
                Feature? bend = CornerSupport.FindBend(ctx, b, notes, info, out why);
                if (bend == null) return Fail(why!, notes);
                if (bends.All(x => x.Name != bend.Name)) bends.Add(bend);
            }
            List<string> wantedBends = bends.Select(b => b.Name).ToList();
            Feature? unfold = r.PrecedingUnfold != null ? ctx.Counterparts(r.PrecedingUnfold, false).FirstOrDefault() : null;
            string type = r.Kind == FoldKind.Fold ? "Fold" : "UnFold";

            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            Feature? made = null;
            foreach (FoldSelection sel in FoldRules.Selections(r.Kind, unfold != null))
            {
                string? failure = null;
                try
                {
                    if (Select(doc, sel, unfold, fixedFace, bends))
                    {
                        if (r.Kind == FoldKind.Fold) doc.InsertSheetMetalFold();
                        else doc.InsertSheetMetalUnfold();
                    }
                    else failure = "Auswahl ließ sich nicht setzen.";
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }
                finally
                {
                    SwSelect.Clear(doc);
                    ctx.Topology.Invalidate();
                }
                Feature? f = SwSheetMetal.NewOfType(doc, before.Names, type, d => d is FoldsFeatureData);
                if (f != null)
                {
                    if (AsWanted(ctx, f, wantedFixed, wantedBends)) made = f;
                    else
                    {
                        // CADapter: a wrong selection makes a feature that unfolds nothing, without an error – set what it acts on.
                        Feature live = f;
                        bool edited = FeatureEdit.Folds(doc, live, d =>
                        {
                            d.FixedFace = Safe(fixedFace);
                            d.Bends = bends.Cast<object>().ToArray();
                        });
                        doc.EditRebuild3();
                        ctx.Topology.Invalidate();
                        if (edited && AsWanted(ctx, live, wantedFixed, wantedBends))
                        {
                            made = live;
                            info.Add($"Feste Fläche und Biegungen von „{kind}“ nachträglich gesetzt ({sel.Description}).");
                        }
                        else failure = "Feste Fläche oder Biegungen weichen ab.";
                    }
                    if (made != null)
                    {
                        if (tried.Count > 0) info.Add($"„{kind}“ angelegt mit: {sel.Description}.");
                        break;
                    }
                }
                tried.Add($"{sel.Description}: {failure ?? $"kein Feature „{kind}“ entstanden."}");
                if (!CornerSupport.RemoveMade(ctx, before.Names, tried)) break;
            }
            if (made == null)
            {
                notes.AddRange(tried);
                return Fail(FoldRules.NotAsWanted, notes);
            }
            ctx.Register(r.Name, made);
            BuildResult result = Ok(made, FoldRules.Describe(r), notes);
            result.Info.AddRange(info);
            return result;
        }

        /// <summary>One selection: the unfold (a fold, mark 0), the fixed face, the bends, each with its mark.</summary>
        private static bool Select(ModelDoc2 doc, FoldSelection sel, Feature? unfold, Face2 fixedFace, List<Feature> bends)
        {
            var items = new List<object>();
            var marks = new List<int>();
            if (sel.WithUnfold && unfold != null)
            {
                items.Add(unfold);
                marks.Add(0);
            }
            items.Add(fixedFace);
            marks.Add(sel.FixedMark);
            foreach (Feature b in bends)
            {
                items.Add(b);
                marks.Add(sel.BendMark);
            }
            return CornerSupport.SelectAll(doc, items, marks);
        }

        private static bool AsWanted(BuildContext ctx, Feature f, FaceSignature fixedFace, List<string> bends) =>
            SwSheetMetal.ReadBackFold(ctx.Doc, f, fixedFace, out bool sameFixed, out List<string> got) && sameFixed && FoldRules.SameBends(bends, got);
    }
}
