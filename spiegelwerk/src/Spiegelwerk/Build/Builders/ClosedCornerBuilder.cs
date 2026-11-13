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
    /// Closed corner (A3 (2) §5.3, blech-plan S5). The mirrored faces – all of them, on mirrored bodies in
    /// separate-body mode, before anything is made (A3 P12) – selected in each of the ways of
    /// <see cref="CornerRules.ClosedCornerSelections"/> (B45), InsertSheetMetalClosedCorner (no return
    /// value: the new corner is what is new since the snapshot), the faces read back as a set; the first
    /// variant whose faces are the mirrored ones is kept. Then type, gap, overlap ratio and open bend region
    /// in one edit where they differ, read back. Overlap ↔ underlap is offered to the check (the roles should
    /// keep the type, A3 §6 – a hedge).
    /// </summary>
    internal sealed class ClosedCornerBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is ClosedCornerRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (ClosedCornerRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();
            List<Face2>? faces = CornerSupport.ResolveFaces(ctx, r.Faces, "der geschlossenen Ecke", notes, out string? why);
            if (faces == null) return Fail(why!, notes);
            List<FaceSignature> wanted = CornerSupport.Mirrored(ctx, r.Faces);

            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            Feature? corner = null;
            ClosedCornerRecord? got = null;
            foreach (var (description, marks) in CornerRules.ClosedCornerSelections(faces.Count))
            {
                string? failure = null;
                try
                {
                    if (CornerSupport.SelectAll(doc, faces.Cast<object>().ToList(), marks)) doc.InsertSheetMetalClosedCorner();
                    else failure = "Flächen ließen sich nicht wählen.";
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
                Feature? made = SwSheetMetal.NewOfType(doc, before.Names, "CornerFeat", d => d is ClosedCornerFeatureData);
                if (made != null)
                {
                    got = SwSheetMetal.ReadBackClosedCorner(doc, made, wanted, out bool same, out int count);
                    if (got != null && same)
                    {
                        corner = made;
                        if (tried.Count > 0) info.Add($"Geschlossene Ecke angelegt mit: {description}.");
                        break;
                    }
                    failure = got == null ? "Die neue Ecke ließ sich nicht zurücklesen."
                        : count == wanted.Count ? "Die neue Ecke hat andere Flächen als die gespiegelten."
                        : $"Die neue Ecke hat {count} Flächen statt {wanted.Count}.";
                }
                tried.Add($"{description}: {failure ?? "keine geschlossene Ecke entstanden."}");
                if (!CornerSupport.RemoveMade(ctx, before.Names, tried)) break;
            }
            if (corner == null || got == null)
            {
                notes.AddRange(tried);
                return Fail("SOLIDWORKS hat die geschlossene Ecke nicht mit den gespiegelten Flächen angelegt.", notes);
            }

            // Values: one edit where they differ, read back.
            if (!CornerRules.Differences(r, got).None)
            {
                if (!FeatureEdit.ClosedCorner(doc, corner, d => WriteValues(d, r))) notes.Add("Werte der geschlossenen Ecke ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            ClosedCornerRecord? now = SwSheetMetal.ReadBackClosedCorner(doc, corner, wanted, out bool stillSame, out _);
            if (now == null) notes.Add("Die geschlossene Ecke ließ sich nicht zurücklesen.");
            else
            {
                notes.AddRange(CornerRules.Differences(r, now).Differences.Select(x => $"Geschlossene Ecke: {x}"));
                if (!stillSame) notes.Add("Geschlossene Ecke: Nach dem Setzen der Werte sind es nicht mehr die gespiegelten Flächen.");
            }

            ctx.Register(r.Name, corner);
            BuildResult result = Ok(corner, CornerRules.Describe(r), notes);
            result.Info.AddRange(info);
            if (CornerRules.HasOverlapRatio(r.CornerType))
            {
                Feature c = corner;
                result.Alternatives.Add(new Alternative(CornerRules.OverlapSwapped,
                    () => FeatureEdit.ClosedCorner(doc, c, d => d.CornerType = CornerRules.SwapOverlap(d.CornerType))));
            }
            return result;
        }

        private static void WriteValues(ClosedCornerFeatureData d, ClosedCornerRecord r)
        {
            if (d.CornerType != r.CornerType) d.CornerType = r.CornerType;
            d.GapDistance = r.Gap;
            if (CornerRules.HasOverlapRatio(r.CornerType)) d.OverlapUnderlapRatio = r.OverlapRatio;
            if (d.OpenBendRegion != r.OpenBendRegion) d.OpenBendRegion = r.OpenBendRegion;
        }
    }
}
