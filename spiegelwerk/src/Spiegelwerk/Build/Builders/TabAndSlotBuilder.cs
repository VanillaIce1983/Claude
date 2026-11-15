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
    /// Tab and slot (A4 (2) §6, P12, blech-plan S8), as the help example makes it: CreateDefinition(88), a group
    /// per original group with the mirrored tab edge and slot face, CreateFeature – first with every value in the
    /// data object, then as the example does (edge, face, offset flag) –, then one edit for whatever reads back
    /// otherwise (tab face, reference points, offsets, spacing, height, edge treatment, clearance). Every edge,
    /// face and point is found before anything is made, separate body: on mirrored bodies only – the tab body and
    /// the slot body both (A4 P12). D1 and D2 are swapped where the copy's tab edge runs the other way and nothing
    /// else fixes the start (<see cref="TabSlotRules.Offsets"/>). The slot feature SOLIDWORKS makes with it is a
    /// companion under the original slot's name. Variants for the check: D1/D2 swapped, the tab offset reversed.
    /// </summary>
    internal sealed class TabAndSlotBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is TabAndSlotRecord;

        private sealed class Target
        {
            public Edge Edge = null!;
            public EdgeSignature Wanted = null!;
            public Face2 SlotFace = null!;
            public FaceSignature WantedSlot = null!;
            public Face2? TabFace;
            public object? Start;
            public object? End;

            /// <summary>The values the copy's group should have (D1/D2 possibly swapped).</summary>
            public TabSlotGroup Values = null!;
        }

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (TabAndSlotRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();

            // ---- every entity before anything is made
            List<(Edge edge, EdgeSignature signature)>? edges = FlangeSupport.ResolveEdges(ctx, r.Groups.Select(g => g.TabEdge), "von Lasche und Schlitz", notes, out string? why);
            if (edges == null) return Fail(why!, notes);
            List<Face2>? slotFaces = CornerSupport.ResolveFaces(ctx, r.Groups.Select(g => g.SlotFace), "des Schlitzes", notes, out why);
            if (slotFaces == null) return Fail(why!, notes);
            var targets = new List<Target>();
            for (int i = 0; i < r.Groups.Count; i++)
            {
                TabSlotGroup g = r.Groups[i];
                var t = new Target
                {
                    Edge = edges[i].edge,
                    Wanted = edges[i].signature,
                    SlotFace = slotFaces[i],
                    WantedSlot = g.SlotFace.Mirror(ctx.Mirror),
                };
                if (g.TabFace != null)
                {
                    t.TabFace = CornerSupport.ResolveFaces(ctx, new[] { g.TabFace }, "der Lasche", notes, out string? tabWhy)?.FirstOrDefault();
                    if (t.TabFace == null) notes.Add($"{Label(i, r.Groups.Count)}: {tabWhy} Die Kopie nimmt die Fläche, die SOLIDWORKS wählt.");
                }
                t.Start = Find(ctx, g.StartReference, notes);
                t.End = Find(ctx, g.EndReference, notes);
                if ((g.StartReference != null && t.Start == null) || (g.EndReference != null && t.End == null))
                    return Fail($"{Label(i, r.Groups.Count)}: Start- oder Endpunkt im Ziel nicht gefunden.", notes);
                // A vertex of the original would tie the copy to it (separate body: mirrored bodies only).
                if (ctx.SeparateBody && new[] { t.Start, t.End }.Any(p => p is Vertex v && !CornerSupport.OnOwnBody(ctx, v)))
                    return Fail($"{Label(i, r.Groups.Count)}: Start- oder Endpunkt liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.", notes);
                var offsets = TabSlotRules.Offsets(g, t.Wanted, TopologyCapture.Edge(t.Edge));
                if (offsets == null)
                {
                    notes.Add(TabSlotRules.OffsetsUndetermined(i + 1, r.Groups.Count));
                    t.Values = g;
                }
                else
                {
                    t.Values = g.WithOffsets(offsets.Value.d1, offsets.Value.d2);
                    if (offsets.Value.swapped) info.Add(TabSlotRules.OffsetsSwappedInfo(i + 1, r.Groups.Count));
                }
                targets.Add(t);
            }

            // ---- create: every value in the data object, then the help example's minimum
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            Feature? tab = Create(ctx, targets, full: true, tried) ?? Create(ctx, targets, full: false, tried);
            if (tab == null)
            {
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                notes.AddRange(tried);
                if (left.Count > 0) notes.Add(FlangeSupport.LeftBehind(left));
                return Fail("SOLIDWORKS hat Lasche und Schlitz nicht erzeugt.", notes);
            }
            // The slot feature SOLIDWORKS made with it (B64): the one other new feature that is no sketch – at the top level, else under the tab.
            List<Feature> others = SwSheetMetal.NewSince(doc, before.Names).Where(x => x.Name != tab.Name && !SwDoc.IsSketch(x)).ToList();
            if (others.Count == 0) others = SwDoc.SubFeatures(tab).Where(x => !SwDoc.IsSketch(x)).ToList();
            Feature? slot = others.Count == 1 ? others[0] : null;

            // ---- values: one edit where they read back otherwise, read back again
            List<EdgeSignature> wantedEdges = targets.Select(t => t.Wanted).ToList();
            List<FaceSignature> wantedFaces = targets.Select(t => t.WantedSlot).ToList();
            List<TabSlotGroup> wanted = targets.Select(t => t.Values).ToList();
            List<TabSlotGroup>? got = SwSheetMetal.ReadBackTabAndSlot(doc, tab, wantedEdges, wantedFaces, out _, out _);
            bool references = targets.Any(t => t.Start != null || t.End != null || t.TabFace != null);
            if (got == null || references || TabSlotRules.Differences(wanted, got).Count > 0)
            {
                if (!FeatureEdit.TabAndSlot(doc, tab, d => WriteGroups(d, targets, entities: true)))
                    notes.Add("Werte von Lasche und Schlitz ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            got = SwSheetMetal.ReadBackTabAndSlot(doc, tab, wantedEdges, wantedFaces, out List<bool> sameEdge, out List<bool> sameFace);
            if (got == null) notes.Add("Lasche und Schlitz ließ sich nicht zurücklesen.");
            else
            {
                notes.AddRange(TabSlotRules.Differences(wanted, got));
                for (int i = 0; i < Math.Min(sameEdge.Count, targets.Count); i++)
                {
                    if (!sameEdge[i]) notes.Add(TabSlotRules.OtherEntities(i + 1, targets.Count, "Laschenkante"));
                    if (!sameFace[i]) notes.Add(TabSlotRules.OtherEntities(i + 1, targets.Count, "Schlitzfläche"));
                }
            }
            if (r.SlotFeatureName != null && slot == null)
                info.Add(TabSlotRules.SlotNotFound(r.SlotFeatureName, others.Count));

            // ---- the slot options: a note only where nothing checks them (like the hem's relief, A2 P9)
            bool checkable = TabSlotRules.SlotOptionsCheckable(ctx.Options.VerifySteps, ctx.Check == StepCheck.Full || ctx.Check == StepCheck.PassOnly,
                r.ExpectedChange != null && r.OtherBodies.Count == 0);
            if (TabSlotRules.SlotOptionsNote(checkable) is string slotNote) notes.Add(slotNote);

            ctx.Register(r.Name, tab);
            if (slot != null && r.SlotFeatureName != null) ctx.Register(r.SlotFeatureName, slot);
            BuildResult result = Ok(tab, TabSlotRules.Describe(r), notes);
            result.Info.AddRange(info);
            if (slot != null && r.SlotFeatureName != null) result.Companions.Add(new Companion(slot, r.SlotFeatureName, new List<NamedDimension>()));
            if (checkable)
            {
                result.MismatchHint = TabSlotRules.SlotOptionsHint;
                result.PassNote = TabSlotRules.SlotOptionsChecked;
            }
            // Removed again: the tab goes first (StepRunner), then whatever the step made since the snapshot (its slot).
            HashSet<string> snapshot = before.Names;
            result.RemoveHelpers = () => FlangeSupport.RemoveMade(ctx, snapshot);

            Feature f = tab;
            if (r.Groups.Any(TabSlotRules.OffsetsMaySwap))
                result.Alternatives.Add(new Alternative(TabSlotRules.OffsetsSwapped, () => FeatureEdit.TabAndSlot(doc, f, d =>
                {
                    List<TabAndSlotGroupData> groups = SwSheetMetal.GroupsOf(d);
                    for (int k = 0; k < Math.Min(groups.Count, r.Groups.Count); k++)
                        if (TabSlotRules.OffsetsMaySwap(r.Groups[k]))
                        {
                            double d1 = groups[k].D1OffsetFromStart;
                            groups[k].D1OffsetFromStart = groups[k].D2OffsetFromEnd;
                            groups[k].D2OffsetFromEnd = d1;
                        }
                })));
            if (r.Groups.Any(TabSlotRules.HasTabOffset))
                result.Alternatives.Add(new Alternative(TabSlotRules.TabOffsetReversed, () => FeatureEdit.TabAndSlot(doc, f, d =>
                {
                    List<TabAndSlotGroupData> groups = SwSheetMetal.GroupsOf(d);
                    for (int k = 0; k < Math.Min(groups.Count, r.Groups.Count); k++)
                        if (TabSlotRules.HasTabOffset(r.Groups[k]))
                            groups[k].TabReverseDirection = !groups[k].TabReverseDirection;
                })));
            return result;
        }

        private static string Label(int i, int count) => count == 1 ? "Lasche und Schlitz" : $"Lasche und Schlitz, Gruppe {i + 1}";

        /// <summary>
        /// One attempt: a new data object, a group per target with edge and face, every value (<paramref name="full"/>)
        /// or only the offset flag (the help example), CreateFeature. What it made is the returned feature, else the new
        /// feature whose data answer as tab and slot. A failed attempt removes what it made.
        /// </summary>
        private static Feature? Create(BuildContext ctx, List<Target> targets, bool full, List<string> tried)
        {
            ModelDoc2 doc = ctx.Doc;
            HashSet<string> before = SwSheetMetal.AllNames(doc);
            string how = full ? "mit allen Werten" : "wie im Beispiel der Hilfe";
            Feature? made = null;
            try
            {
                var d = (object)doc.FeatureManager.CreateDefinition(TabSlotRules.TabAndSlotId) as TabAndSlotFeatureData;
                if (d == null)
                {
                    tried.Add($"Lasche und Schlitz ({how}): kein Datenobjekt (CreateDefinition({TabSlotRules.TabAndSlotId})).");
                    return null;
                }
                foreach (Target t in targets)
                {
                    var g = (object)d.SelectionAddNewGroup() as TabAndSlotGroupData;
                    if (g == null)
                    {
                        tried.Add($"Lasche und Schlitz ({how}): keine neue Gruppe (SelectionAddNewGroup).");
                        return null;
                    }
                    g.Offset = t.Values.Offset;
                    g.SelectionTabEdge = t.Edge;
                    g.SelectionSlotFace = t.SlotFace;
                    if (full) WriteGroup(g, t, entities: true);
                }
                made = doc.FeatureManager.CreateFeature(d);
            }
            catch (Exception ex)
            {
                tried.Add($"Lasche und Schlitz ({how}): {ex.Message}");
            }
            finally
            {
                SwSelect.Clear(doc);
                ctx.Topology.Invalidate();
            }
            made ??= SwSheetMetal.NewSince(doc, before).FirstOrDefault(SwSheetMetal.IsTabAndSlot);
            if (made != null) return made;
            if (tried.Count == 0 || !tried[tried.Count - 1].StartsWith($"Lasche und Schlitz ({how})", StringComparison.Ordinal))
                tried.Add($"Lasche und Schlitz ({how}): nichts entstanden.");
            List<string> left = FlangeSupport.RemoveMade(ctx, before);
            if (left.Count > 0) tried.Add(FlangeSupport.LeftBehind(left));
            return null;
        }

        /// <summary>Every group's values (and with <paramref name="entities"/> its tab face and reference points) into an edit.</summary>
        private static void WriteGroups(TabAndSlotFeatureData d, List<Target> targets, bool entities)
        {
            List<TabAndSlotGroupData> groups = SwSheetMetal.GroupsOf(d);
            for (int i = 0; i < Math.Min(groups.Count, targets.Count); i++) WriteGroup(groups[i], targets[i], entities);
        }

        private static void WriteGroup(TabAndSlotGroupData g, Target t, bool entities)
        {
            if (entities)
            {
                if (t.TabFace != null) g.TabFace = t.TabFace;
                if (t.Start != null) g.SelectionStartReferencePoint = t.Start;
                if (t.End != null) g.SelectionEndReferencePoint = t.End;
            }
            SwSheetMetal.WriteTabSlotScalars(g, t.Values);
        }
    }
}
