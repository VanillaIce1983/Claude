using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Hem (A2 phase 2a, blech-plan S2). The mirrored edges on mark 0, then InsertSheetMetalHem2
    /// with the original's type, position, direction, sizes and own allowance (soheil, VERIFIED:
    /// edges on mark 0, a null allowance takes the sheet's). Every value is unchanged by the
    /// reflection: the hem is defined by the sheet's faces at its edge, which R maps onto the
    /// copy's; only the direction is not certain and is offered to the check.
    ///
    /// After creation: the number of edges (another number: removed again), one edit for values
    /// that read back differently, the allowance in an edit of its own (§1.4), the width sketches
    /// (kept where both are the whole edge, otherwise rewritten), the bends against the original's.
    /// The hem's relief is not readable (IHemFeatureData has none): the copy gets the default, and
    /// the report says so only where no check confirms it (A2 P9).
    /// </summary>
    internal sealed class HemBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is HemRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (HemRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();

            List<(Edge edge, EdgeSignature signature)>? edges = FlangeSupport.ResolveEdges(ctx, r.Edges, "des Saums", notes, out string? missing);
            if (edges == null) return Fail(missing!, notes);

            // ---- create: the edges on mark 0, the allowance at creation, the default relief (the original's is not readable)
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            Feature? hem = null;
            try
            {
                SwSelect.Clear(doc);
                bool selected = true;
                for (int i = 0; i < edges.Count; i++) selected &= SwSelect.Select(doc, edges[i].edge, 0, i > 0);
                if (selected)
                    hem = doc.FeatureManager.InsertSheetMetalHem2(r.HemType, r.Position, r.Reverse, r.Length, r.Gap, r.Angle, r.Radius, r.MiterGap,
                        SwSheetMetal.NewAllowance(doc.FeatureManager, r.OwnAllowance), true, 0, 0, false, 0.5, 0, 0);
                else notes.Add("Die Kanten des Saums ließen sich nicht wählen.");
            }
            catch (Exception ex)
            {
                notes.Add($"InsertSheetMetalHem2: {ex.Message}");
            }
            finally
            {
                SwSelect.Clear(doc);
                ctx.Topology.Invalidate();
            }
            hem ??= SwSheetMetal.NewSince(doc, before.Names).FirstOrDefault(x => x.GetTypeName2() == "Hem");
            if (hem == null)
            {
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                if (left.Count > 0) notes.Add(FlangeSupport.LeftBehind(left));
                string hint = r.HemType == HemTypes.TearDrop ? " Ein tropfenförmiger Saum mit großem Winkel wird von SOLIDWORKS mitunter nicht angelegt." : "";
                return Fail("SOLIDWORKS hat den Saum nicht erzeugt." + hint, notes);
            }

            // ---- the number of edges: a hem on other edges is not this one
            HemRecord? got = SwSheetMetal.ReadBackHem(doc, hem, out int edgeCount);
            string? wrong = got == null ? "Der neue Saum ließ sich nicht zurücklesen"
                : edgeCount >= 0 && edgeCount != r.Edges.Count ? $"Saum mit {edgeCount} statt {r.Edges.Count} Kante{(r.Edges.Count == 1 ? "" : "n")} entstanden" : null;
            if (wrong != null)
            {
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                if (left.Count > 0) notes.Add(FlangeSupport.LeftBehind(left));
                return Fail(wrong + "; wieder entfernt.", notes);
            }

            // ---- values that read back otherwise: one edit, without the allowance (A2 P7)
            if (HemRules.Differences(r, got!).Count > 0)
            {
                if (!FeatureEdit.Hem(doc, hem, d => WriteValues(d, r))) notes.Add("Werte des Saums ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            // ---- the allowance: at creation; an edit of its own if it did not take or the edit above reset it
            Feature h = hem;
            (bool, BendAllowanceData?) ReadAllowance()
            {
                HemRecord? now = SwSheetMetal.ReadBackHem(doc, h, out int _);
                return (now != null, now?.OwnAllowance);
            }
            FlangeSupport.SettleAllowance(ctx, hem, r.TypeName, r.OwnAllowance, ReadAllowance, "Saum", notes);
            got = SwSheetMetal.ReadBackHem(doc, hem, out int _);
            if (got == null) notes.Add("Der Saum ließ sich nicht zurücklesen.");
            else notes.AddRange(HemRules.Differences(r, got).Select(x => $"Saum: {x}"));

            // ---- width sketches, then the bends
            var info = new List<string>();
            List<Feature> native = SwDoc.SubFeatures(hem).Where(SwDoc.IsSketch).ToList();
            WriteWidthSketches(ctx, r, native, edges.Select(e => e.signature).ToList(), notes, info);
            FlangeSupport.CheckBends(ctx, hem, r.BendCount, HemRules.Parent(r), r.Bends, r.BendOverrides, notes, info);

            // ---- the relief: a note only where nothing checks it (A2 P9)
            bool checkable = HemRules.ReliefCheckable(ctx.Options.VerifySteps, ctx.Check == StepCheck.Full || ctx.Check == StepCheck.PassOnly,
                r.ExpectedChange != null && r.OtherBodies.Count == 0);
            if (HemRules.ReliefNote(checkable) is string relief) notes.Add(relief);

            ctx.Register(r.Name, hem);
            BuildResult result = Ok(hem, Describe(r), notes);
            result.Info.AddRange(info);
            if (checkable)
            {
                result.MismatchHint = HemRules.ReliefHint;
                result.PassNote = HemRules.ReliefChecked;
            }
            // Width sketches the record does not own (none captured) stay at the top level when the hem is deleted without them.
            result.RemoveHelpers = () =>
            {
                foreach (Feature s in native)
                    if (FlangeSupport.Live(doc, s) is Feature live)
                        Delete(ctx, live);
            };
            result.Alternatives.Add(new Alternative("Saumrichtung umgekehrt",
                FlangeSupport.KeepingAllowance(ctx, hem, r.TypeName, r.OwnAllowance, ReadAllowance,
                    () => FeatureEdit.Hem(doc, h, d => d.ReverseDirection = !d.ReverseDirection))));
            return result;
        }

        /// <summary>The values of the hem's type – the others SOLIDWORKS does not use – and the direction; never the allowance.</summary>
        private static void WriteValues(HemFeatureData d, HemRecord r)
        {
            if (d.Type != r.HemType) d.Type = r.HemType;
            if (d.BendPosition != r.Position) d.BendPosition = r.Position;
            if (d.ReverseDirection != r.Reverse) d.ReverseDirection = r.Reverse;
            foreach (var (name, value, _) in HemRules.Values(r))
                switch (name)
                {
                    case "Saumlänge":
                        d.Length = value;
                        break;
                    case "Spaltabstand":
                        d.GapDistance = value;
                        break;
                    case "Saumwinkel":
                        d.Angle = value;
                        break;
                    case "Saumradius":
                        d.Radius = value;
                        break;
                    default:
                        d.MiterGap = value;
                        break;
                }
        }

        /// <summary>
        /// The original's width sketches in those of the new hem: paired with its edges as at capture
        /// (<see cref="FlangeProfiles.Assign"/>); where both are the whole edge – the default – the new
        /// one stays, otherwise the mirrored sketch is written into it (<see cref="SketchBuilder.ReplaceInto"/>).
        /// Each gets the original's name. If the new hem's sketches cannot be paired, they stay as
        /// SOLIDWORKS made them (note).
        /// </summary>
        private static void WriteWidthSketches(BuildContext ctx, HemRecord r, List<Feature> native, List<EdgeSignature> targetEdges, List<string> notes, List<string> info)
        {
            // None captured, or not paired (the record's note says so): the new hem's own stay.
            if (r.WidthSketches.Count == 0 || !r.WidthSketchesPaired) return;
            var capture = new SketchCapture(ctx.Math);
            var candidates = new List<ProfileCandidate>();
            try
            {
                foreach (Feature n in native) candidates.Add(new ProfileCandidate(n.Name, capture.Read(n)));
            }
            catch (Exception ex)
            {
                notes.Add($"Breitenskizzen des neuen Saums nicht lesbar ({ex.Message}); sie bleiben, wie SOLIDWORKS sie anlegt.");
                return;
            }
            int[]? pairs = FlangeProfiles.Assign(targetEdges, candidates);
            if (pairs == null)
            {
                notes.Add("Breitenskizzen des neuen Saums lassen sich seinen Kanten nicht zuordnen; sie bleiben, wie SOLIDWORKS sie anlegt.");
                return;
            }
            int written = 0;
            foreach (HemWidthSketch w in r.WidthSketches)
            {
                int k = Array.IndexOf(pairs, w.EdgeIndex);
                if (w.EdgeIndex < 0 || k < 0)
                {
                    notes.Add($"Breitenskizze „{w.Name}“: Der neue Saum hat an ihrer Kante keine; nicht übertragen.");
                    continue;
                }
                Feature target = native[k];
                if (FlangeProfiles.IsFullEdgeLine(w.Sketch, r.Edges[w.EdgeIndex]) && FlangeProfiles.IsFullEdgeLine(candidates[k].Sketch, targetEdges[w.EdgeIndex]))
                {
                    ctx.Register(w.Name, target);
                    continue;
                }
                try
                {
                    new SketchBuilder(ctx).ReplaceInto(w.Sketch, target, notes);
                    ctx.Register(w.Name, target);
                    written++;
                }
                catch (Exception ex)
                {
                    ctx.Sketches.Remove(w.Name);
                    notes.Add($"Breitenskizze „{w.Name}“ ließ sich nicht übertragen ({ex.Message}).");
                }
            }
            if (written == 0) return;
            ctx.Doc.EditRebuild3();
            ctx.Topology.Invalidate();
            info.Add($"Breite aus {written} gespiegelte{(written == 1 ? "r" : "n")} Breitenskizze{(written == 1 ? "" : "n")} übernommen.");
        }

        private static string Describe(HemRecord r)
        {
            string kind = r.HemType switch
            {
                HemTypes.Open => "offen",
                HemTypes.Closed => "geschlossen",
                HemTypes.TearDrop => "tropfenförmig",
                HemTypes.Rolled => "gerollt",
                _ => "doppelt",
            };
            string edges = r.Edges.Count == 1 ? "1 Kante" : $"{r.Edges.Count} Kanten";
            return $"Saum {kind}, {edges}";
        }
    }
}
