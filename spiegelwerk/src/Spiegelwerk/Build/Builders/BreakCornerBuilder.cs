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
    /// Break corner in the folded state (A3 (2) §5.4, blech-plan S5). The mirrored edges and faces – all of
    /// them, on mirrored bodies in separate-body mode, before anything is made – with mark 0 (B46), then
    /// InsertSheetMetalBreakCorner with type and distance (no return value: the new corner is what is new
    /// since the snapshot). Internal corners only and centred on bend lines in one edit where they differ;
    /// everything read back, the entities as a set.
    /// </summary>
    internal sealed class BreakCornerBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is BreakCornerRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (BreakCornerRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            List<(Edge edge, EdgeSignature signature)>? edges = FlangeSupport.ResolveEdges(ctx, r.Edges, "der gebrochenen Ecke", notes, out string? why);
            if (edges == null) return Fail(why!, notes);
            List<Face2>? faces = CornerSupport.ResolveFaces(ctx, r.Faces, "der gebrochenen Ecke", notes, out why);
            if (faces == null) return Fail(why!, notes);
            List<EdgeSignature> wantedEdges = r.Edges.Select(e => e.Mirror(ctx.Mirror)).ToList();
            List<FaceSignature> wantedFaces = CornerSupport.Mirrored(ctx, r.Faces);

            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            List<object> items = edges.Select(e => (object)e.edge).Concat(faces).ToList();
            try
            {
                if (CornerSupport.SelectAll(doc, items, Enumerable.Repeat(0, items.Count).ToList())) doc.InsertSheetMetalBreakCorner(r.BreakType, r.Distance);
                else tried.Add("Kanten und Flächen ließen sich nicht wählen.");
            }
            catch (Exception ex)
            {
                tried.Add($"InsertSheetMetalBreakCorner: {ex.Message}");
            }
            finally
            {
                SwSelect.Clear(doc);
                ctx.Topology.Invalidate();
            }
            Feature? corner = SwSheetMetal.NewOfType(doc, before.Names, "BreakCorner", d => d is BreakCornerFeatureData);
            if (corner == null)
            {
                notes.AddRange(tried);
                if (!CornerSupport.RemoveMade(ctx, before.Names, notes)) return Fail("SOLIDWORKS hat die gebrochene Ecke nicht passend erzeugt.", notes);
                return Fail("SOLIDWORKS hat die gebrochene Ecke nicht erzeugt.", notes);
            }

            BreakCornerRecord? got = SwSheetMetal.ReadBackBreakCorner(doc, corner, wantedEdges, wantedFaces, out _, out _);
            if (got != null && !CornerRules.Differences(r, got).None)
            {
                if (!FeatureEdit.BreakCorner(doc, corner, d => WriteValues(d, r))) notes.Add("Werte der gebrochenen Ecke ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            BreakCornerRecord? now = SwSheetMetal.ReadBackBreakCorner(doc, corner, wantedEdges, wantedFaces, out bool same, out int count);
            if (now == null) notes.Add("Die gebrochene Ecke ließ sich nicht zurücklesen.");
            else
            {
                notes.AddRange(CornerRules.Differences(r, now).Differences.Select(x => $"Gebrochene Ecke: {x}"));
                if (!same)
                    notes.Add($"Gebrochene Ecke: Sie wirkt nicht auf die gespiegelten Kanten und Flächen ({count} Elemente gelesen, {wantedEdges.Count + wantedFaces.Count} erwartet).");
            }

            ctx.Register(r.Name, corner);
            return Ok(corner, CornerRules.Describe(r), notes);
        }

        private static void WriteValues(BreakCornerFeatureData d, BreakCornerRecord r)
        {
            if (d.BreakType != r.BreakType) d.BreakType = r.BreakType;
            d.Distance = r.Distance;
            if (d.InternalCornersOnly != r.InternalCornersOnly) d.InternalCornersOnly = r.InternalCornersOnly;
            if (d.CenteredOnBendLines != r.CenteredOnBendLines) d.CenteredOnBendLines = r.CenteredOnBendLines;
        }
    }
}
