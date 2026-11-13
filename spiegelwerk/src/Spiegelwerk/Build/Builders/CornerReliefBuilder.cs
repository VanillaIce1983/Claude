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
    /// Corner relief (A3 (2) §5.5, P13, blech-plan S5; 2022 and later – it was captured in this session, so
    /// the data object exists). The body by the mirrored face of its scope, every corner's mirrored faces in
    /// their roles – separate body: on mirrored bodies, before anything is made. The data object
    /// (CreateDefinition(120), late-bound, B49): Initialize with the bend type, SetBodyScope, AddNewCorner per
    /// corner with its values, CreateFeature. Without a data object the 2019 call (two-bend corners of the
    /// documented types only): the faces of each corner on mark 4, AddCornerReliefCorner, AddCornerReliefType
    /// with the documented lengths, FinishCornerRelief – tried once more with the body on mark 0 first. Read
    /// back: the corners paired with the mirrored ones by their faces, their values compared; what differs
    /// becomes a note (there is no edit).
    /// </summary>
    internal sealed class CornerReliefBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is CornerReliefRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (CornerReliefRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();

            // ---- references before anything is made
            var corners = new List<(Face2 f1, Face2 f2, Face2? f3)>();
            for (int i = 0; i < r.Corners.Count; i++)
            {
                CornerReliefItem c = r.Corners[i];
                List<Face2>? faces = CornerSupport.ResolveFaces(ctx, c.Faces(), $"der Ecke {i + 1} der Eckentlastung", notes, out string? why);
                if (faces == null) return Fail(why!, notes);
                corners.Add((faces[0], faces[1], faces.Count > 2 ? faces[2] : null));
            }
            Body2? body = BodyOf(ctx, r, corners, notes, out string? noBody);
            if (body == null) return Fail(noBody!, notes);
            List<CornerReliefItem> wanted = r.Corners.Select(c => c.Mirror(ctx.Mirror)).ToList();

            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            bool legacy = false;
            Feature? relief = ByDefinition(ctx, r, body, corners, before, tried, notes);
            if (relief == null)
            {
                if (!CornerSupport.RemoveMade(ctx, before.Names, tried))
                {
                    notes.AddRange(tried);
                    return Fail("SOLIDWORKS hat die Eckentlastung nicht passend erzeugt.", notes);
                }
                if (r.CornerType != CornerReliefBendTypes.TwoBend)
                {
                    notes.AddRange(tried);
                    return Fail("Eckentlastung mit drei Biegungen: Der ältere Aufruf kennt nur Ecken mit zwei Biegungen.", notes);
                }
                for (int i = 0; i < r.Corners.Count; i++)
                    if (CornerRules.LegacyReliefLengths(r.Corners[i]) == null)
                    {
                        notes.AddRange(tried);
                        return Fail(CornerRules.NotLegacy(i, r.Corners[i].ReliefType), notes);
                    }
                foreach (bool withBody in new[] { false, true })
                {
                    relief = Legacy(ctx, r, withBody ? body : null, corners, before, tried);
                    if (relief != null) break;
                    if (!CornerSupport.RemoveMade(ctx, before.Names, tried)) break;
                }
                legacy = relief != null;
            }
            if (relief == null)
            {
                notes.AddRange(tried);
                return Fail("SOLIDWORKS hat die Eckentlastung nicht erzeugt.", notes);
            }
            if (legacy) notes.Add(CornerRules.LegacyCornerRelief);

            // ---- read-back
            List<CornerReliefItem>? got = SwCornerRelief.ReadBack(doc, relief, wanted, out int[]? pairs, out int cornerType);
            if (got == null) notes.Add("Die Eckentlastung ließ sich nicht zurücklesen.");
            else if (pairs == null) notes.Add($"Die Ecken der Eckentlastung sind nicht die gespiegelten ({got.Count} gelesen, {wanted.Count} erwartet).");
            else
            {
                if (cornerType >= 0 && cornerType != r.CornerType) notes.Add($"Eckentlastung: Biegeart erwartet {r.CornerType}, gelesen {cornerType}.");
                for (int i = 0; i < wanted.Count; i++)
                    notes.AddRange(CornerRules.Differences(wanted[i], got[pairs[i]], legacy).Select(x => $"Ecke {i + 1} der Eckentlastung: {x}"));
            }

            ctx.Register(r.Name, relief);
            BuildResult result = Ok(relief, CornerRules.Describe(r), notes);
            // Why the data object did not do (only the 2019 call made it): information.
            result.Info.AddRange(tried);
            result.Info.AddRange(info);
            return result;
        }

        /// <summary>
        /// The body the relief acts on: the body of the mirrored face of its scope; without one, the body of its
        /// first corner. Separate body: a mirrored one. Null and why otherwise.
        /// </summary>
        private static Body2? BodyOf(BuildContext ctx, CornerReliefRecord r, List<(Face2 f1, Face2 f2, Face2? f3)> corners, List<string> notes, out string? why)
        {
            why = null;
            Body2? body = null;
            if (r.BodyFace != null && ctx.Topology.FindFace(r.BodyFace.Mirror(ctx.Mirror), notes) is Face2 face) body = FlangeSupport.BodyOf(face);
            body ??= corners.Count > 0 ? FlangeSupport.BodyOf(corners[0].f1) : null;
            if (body == null)
            {
                why = "Körper der Eckentlastung im Ziel nicht gefunden.";
                return null;
            }
            if (ctx.SeparateBody && !ctx.IsOwnBody(body))
            {
                why = "Körper der Eckentlastung ist im Ziel kein gespiegelter; das Original bleibt unberührt.";
                return null;
            }
            return body;
        }

        /// <summary>
        /// The data object (late-bound); null and what failed in <paramref name="tried"/> if it does not come about.
        /// Values of a corner that did not take go to <paramref name="notes"/> (the read-back says what they are).
        /// </summary>
        private static Feature? ByDefinition(BuildContext ctx, CornerReliefRecord r, Body2 body, List<(Face2 f1, Face2 f2, Face2? f3)> corners,
            SheetMetalSnapshot before, List<string> tried, List<string> notes)
        {
            object? def;
            try
            {
                def = (object)ctx.Doc.FeatureManager.CreateDefinition(CornerRules.CornerReliefId);
            }
            catch (Exception ex)
            {
                tried.Add($"Datenobjekt der Eckentlastung: {ex.Message}");
                return null;
            }
            if (!SwCornerRelief.Is(def))
            {
                tried.Add("Kein Datenobjekt für die Eckentlastung (vor SOLIDWORKS 2022).");
                return null;
            }
            if (!SwCornerRelief.Initialize(def!, r.CornerType))
            {
                tried.Add("Datenobjekt der Eckentlastung: Initialize lehnt die Biegeart ab.");
                return null;
            }
            int? scope = SwCornerRelief.SetBodyScope(def!, body);
            if (scope != 0)
            {
                tried.Add($"Datenobjekt der Eckentlastung: SetBodyScope lehnt den Körper ab (Code {scope?.ToString() ?? "–"}).");
                return null;
            }
            for (int i = 0; i < corners.Count; i++)
            {
                CornerReliefItem c = r.Corners[i];
                var (f1, f2, f3) = corners[i];
                int? code = SwCornerRelief.AddNewCorner(def!, f1, f2, f3, c.ReliefType, out object? corner);
                if (code != 0 || corner == null)
                {
                    tried.Add($"Datenobjekt der Eckentlastung: Ecke {i + 1} nicht angenommen (Code {code?.ToString() ?? "–"}).");
                    return null;
                }
                List<string> failed = SwCornerRelief.WriteValues(corner, c);
                if (failed.Count > 0) notes.Add($"Ecke {i + 1} der Eckentlastung: nicht gesetzt {string.Join(", ", failed)}.");
            }
            try
            {
                Feature? f = ctx.Doc.FeatureManager.CreateFeature(def);
                ctx.Topology.Invalidate();
                return f ?? SwSheetMetal.NewSince(ctx.Doc, before.Names).FirstOrDefault(x => SwCornerRelief.Is((object)x.GetDefinition()));
            }
            catch (Exception ex)
            {
                tried.Add($"Datenobjekt der Eckentlastung: {ex.Message}");
                return null;
            }
        }

        /// <summary>The 2019 call (P13): per corner its faces on mark 4 (the body first on mark 0 if given), then FinishCornerRelief.</summary>
        private static Feature? Legacy(BuildContext ctx, CornerReliefRecord r, Body2? body, List<(Face2 f1, Face2 f2, Face2? f3)> corners, SheetMetalSnapshot before,
            List<string> tried)
        {
            ModelDoc2 doc = ctx.Doc;
            FeatureManager fm = doc.FeatureManager;
            string variant = body == null ? "älterer Aufruf, Flächen auf Markierung 4" : "älterer Aufruf, Körper auf 0, Flächen auf 4";
            try
            {
                for (int i = 0; i < corners.Count; i++)
                {
                    CornerReliefItem c = r.Corners[i];
                    var (length2, length3) = CornerRules.LegacyReliefLengths(c)!.Value;
                    var items = new List<object>();
                    var marks = new List<int>();
                    if (body != null)
                    {
                        items.Add(body);
                        marks.Add(0);
                    }
                    items.Add(corners[i].f1);
                    items.Add(corners[i].f2);
                    marks.Add(4);
                    marks.Add(4);
                    if (!CornerSupport.SelectAll(doc, items, marks))
                    {
                        tried.Add($"{variant}: Flächen der Ecke {i + 1} ließen sich nicht wählen.");
                        return null;
                    }
                    fm.AddCornerReliefCorner();
                    if (!fm.AddCornerReliefType(-1, c.ReliefType, 0, length2, length3, c.CenterOnBendLines, c.RatioToThickness, c.TangentToBend, c.AddFilletedCorners, false))
                    {
                        tried.Add($"{variant}: Ecke {i + 1} nicht angenommen.");
                        return null;
                    }
                }
                Feature? f = fm.FinishCornerRelief();
                ctx.Topology.Invalidate();
                f ??= SwSheetMetal.NewSince(doc, before.Names).FirstOrDefault(x => SwCornerRelief.Is((object)x.GetDefinition()));
                if (f == null) tried.Add($"{variant}: keine Eckentlastung entstanden.");
                return f;
            }
            catch (Exception ex)
            {
                tried.Add($"{variant}: {ex.Message}");
                return null;
            }
            finally
            {
                SwSelect.Clear(doc);
            }
        }
    }
}
