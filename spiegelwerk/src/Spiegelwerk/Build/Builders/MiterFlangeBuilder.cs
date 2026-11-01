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
    /// Miter flange (A2 phase 2a, P8, blech-plan S2). Its open profile is an ordinary sketch,
    /// rebuilt mirrored before it; the flange is made with InsertSheetMetalMiterFlange from the
    /// selection – the sketch on mark 0 and the chain on mark 1 (soheil, VERIFIED), then the
    /// variants of <see cref="MiterRules.Variants"/>. A flange whose number of edges is not the
    /// original's is removed again and the next variant tried (with the edges on mark 0 SOLIDWORKS
    /// made one flange only, CADapter). Radius, gap, position, offsets, relief and allowance are
    /// unchanged by the reflection; the profile is mirrored as a sketch, so the start of the chain
    /// stays where the profile is – swapping the offsets is offered to the check only.
    /// </summary>
    internal sealed class MiterFlangeBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is MiterFlangeRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (MiterFlangeRecord)record;
            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap sketch))
                return BuildResult.Failed($"Skizze \"{r.SketchName}\" wurde nicht neu aufgebaut.");
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            List<(Edge edge, EdgeSignature signature)>? edges = FlangeSupport.ResolveEdges(ctx, r.Edges, "der Gehrungslasche", notes, out string? missing);
            if (edges == null) return Fail(missing!, notes);

            // ---- create: each selection variant checked (one new flange, the original's number of edges) and removed before the next
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            var failures = new List<string>();
            Feature? miter = null;
            MiterSelection used = MiterSelection.SketchFirst;
            foreach (MiterSelection variant in MiterRules.Variants(r.Edges.Count))
            {
                tried.Add(MiterRules.Describe(variant));
                Feature? made = null;
                try
                {
                    if (Select(ctx, sketch.Feature, edges.Select(e => e.edge).ToList(), variant)) made = Create(doc.FeatureManager, r);
                    else failures.Add($"{MiterRules.Describe(variant)}: Auswahl nicht möglich.");
                }
                catch (Exception ex)
                {
                    failures.Add($"{MiterRules.Describe(variant)}: {ex.Message}");
                }
                finally
                {
                    SwSelect.Clear(doc);
                    ctx.Topology.Invalidate();
                }
                made ??= SwSheetMetal.NewSince(doc, before.Names).FirstOrDefault(x => x.GetTypeName2() == "SMMiteredFlange");
                if (made != null)
                {
                    MiterFlangeRecord? got = SwSheetMetal.ReadBackMiter(doc, made, out int count);
                    if (got != null && (count < 0 || count == r.Edges.Count))
                    {
                        miter = made;
                        used = variant;
                        break;
                    }
                    failures.Add(got == null ? $"{MiterRules.Describe(variant)}: nicht zurückzulesen; wieder entfernt." : $"{MiterRules.Describe(variant)}: {MiterRules.EdgeCountMismatch(r.Edges.Count, count)}");
                }
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                if (left.Count > 0)
                {
                    notes.AddRange(failures);
                    notes.Add(FlangeSupport.LeftBehind(left));
                    return Fail($"SOLIDWORKS hat die Gehrungslasche nicht erzeugt (Auswahl: {string.Join("; ", tried)}).", notes);
                }
            }
            if (miter == null)
            {
                notes.AddRange(failures);
                return Fail($"SOLIDWORKS hat die Gehrungslasche nicht erzeugt (Auswahl: {string.Join("; ", tried)}).", notes);
            }

            // ---- values that read back otherwise: one edit, without the allowance (A2 P7)
            Feature f = miter;
            MiterFlangeRecord? now = SwSheetMetal.ReadBackMiter(doc, f, out int _);
            if (now != null && !MiterRules.Differences(r, now).None)
            {
                if (!FeatureEdit.MiterFlange(doc, f, d => WriteValues(d, r))) notes.Add("Werte der Gehrungslasche ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            (bool, BendAllowanceData?) ReadAllowance()
            {
                MiterFlangeRecord? back = SwSheetMetal.ReadBackMiter(doc, f, out int _);
                return (back != null, back?.OwnAllowance);
            }
            FlangeSupport.SettleAllowance(ctx, f, r.TypeName, r.OwnAllowance, ReadAllowance, "Gehrungslasche", notes);
            now = SwSheetMetal.ReadBackMiter(doc, f, out int _);
            if (now == null) notes.Add("Die Gehrungslasche ließ sich nicht zurücklesen.");
            else
            {
                Deviations d = MiterRules.Differences(r, now);
                notes.AddRange(d.Differences.Select(x => $"Gehrungslasche: {x}"));
                notes.AddRange(d.Notes.Select(x => $"Gehrungslasche: {x}"));
            }

            var info = new List<string> { $"Angelegt mit der Auswahl {MiterRules.Describe(used)}." };
            if (failures.Count > 0) info.AddRange(failures);
            FlangeSupport.CheckBends(ctx, f, r.BendCount, MiterRules.Parent(r), r.Bends, r.BendOverrides, notes, info);

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, Describe(r), notes);
            result.Info.AddRange(info);
            if (MiterRules.OffersSwap(r.StartOffset, r.EndOffset))
                result.Alternatives.Add(new Alternative("Start-/Endversatz getauscht",
                    FlangeSupport.KeepingAllowance(ctx, f, r.TypeName, r.OwnAllowance, ReadAllowance,
                        () => FeatureEdit.MiterFlange(doc, f, d =>
                        {
                            double start = d.StartOffset;
                            d.StartOffset = d.EndOffset;
                            d.EndOffset = start;
                        }))));
            return result;
        }

        /// <summary>The profile sketch and the chain, with the marks and in the order of the variant.</summary>
        private static bool Select(BuildContext ctx, Feature sketch, List<Edge> edges, MiterSelection variant)
        {
            var (sketchMark, edgeMark, chainFirst) = MiterRules.Marks(variant);
            SwSelect.Clear(ctx.Doc);
            bool ok = true, append = false;
            void One(object o, int mark)
            {
                ok &= SwSelect.Select(ctx.Doc, o, mark, append);
                append = true;
            }
            if (!chainFirst) One(sketch, sketchMark);
            foreach (Edge e in edges) One(e, edgeMark);
            if (chainFirst) One(sketch, sketchMark);
            return ok;
        }

        /// <summary>The call with every value it takes, the allowance at creation; the relief the original's or the default.</summary>
        private static Feature? Create(FeatureManager fm, MiterFlangeRecord r)
        {
            ReliefData? relief = r.OwnRelief;
            return fm.InsertSheetMetalMiterFlange(r.UseDefaultRadius, r.BendRadius, r.GapDistance, relief == null, relief?.UseRatio ?? true,
                relief?.Ratio ?? 0.5, relief?.Width ?? 0, relief?.Depth ?? 0, (int)(relief?.Kind ?? ReliefKind.Rectangular), r.TrimSideBends, r.PositionType,
                r.StartOffset, r.EndOffset, SwSheetMetal.NewAllowance(fm, r.OwnAllowance));
        }

        /// <summary>Radius, position, side bends, gap, offsets and relief; never the allowance.</summary>
        private static void WriteValues(MiterFlangeFeatureData d, MiterFlangeRecord r)
        {
            d.UseDefaultBendRadius = r.UseDefaultRadius;
            if (!r.UseDefaultRadius) d.BendRadius = r.BendRadius;
            d.PositionType = r.PositionType;
            d.UsePositionTrimSideBends = r.TrimSideBends;
            d.GapDistance = r.GapDistance;
            d.StartOffset = r.StartOffset;
            d.EndOffset = r.EndOffset;
            d.UseDefaultBendRelief = r.OwnRelief == null;
            if (r.OwnRelief == null) return;
            d.ReliefType = (int)r.OwnRelief.Kind;
            d.UseReliefRatio = r.OwnRelief.UseRatio;
            if (r.OwnRelief.UseRatio) d.ReliefRatio = r.OwnRelief.Ratio;
            else
            {
                d.ReliefWidth = r.OwnRelief.Width;
                d.ReliefDepth = r.OwnRelief.Depth;
            }
        }

        private static string Describe(MiterFlangeRecord r)
        {
            string edges = r.Edges.Count == 1 ? "1 Kante" : $"{r.Edges.Count} Kanten";
            string partial = r.StartOffset > 0 || r.EndOffset > 0 ? ", Teillasche" : "";
            return $"Gehrungslasche, {edges}{partial}";
        }
    }
}
