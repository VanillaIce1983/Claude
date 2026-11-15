using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Normal Cut (A1 §8, blech-plan S6). Every mirrored face of every group – separate body: on mirrored bodies –
    /// and the offset plane (required in that mode) are found before anything is made; a missing face fails the
    /// step, the copy would be another cut. The direction is optional: missing, SOLIDWORKS' default stands in, with
    /// a note. Created through the data object (CreateDefinition(115), each group selected with mark 1 and taken by
    /// CreateGroup, the offset plane on 4 or the direction on 8), else the obsolete calls – per group, then in the
    /// order the help numbers (B51); the new feature is
    /// read back – its groups compared as sets of sets with the mirrored ones – and a path that made other groups
    /// goes again. Values and reference that differ are set in one edit, read back. No variants: nothing has a side.
    /// </summary>
    internal sealed class NormalCutBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is NormalCutRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (NormalCutRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();
            if (r.Groups.Count == 0) return Fail(NormalCutRules.NoGroups, notes);

            // ---- every face of every group, before anything is made
            var groups = new List<List<Face2>>();
            for (int g = 0; g < r.Groups.Count; g++)
            {
                var faces = new List<Face2>();
                for (int k = 0; k < r.Groups[g].Count; k++)
                {
                    Face2? face = ctx.Topology.FindFace(r.Groups[g][k].Mirror(ctx.Mirror), notes);
                    if (face == null) return Fail(NormalCutRules.FaceMissing(g + 1, k + 1), notes);
                    if (ctx.SeparateBody && !CornerSupport.OnOwnBody(ctx, face)) return Fail(NormalCutRules.FaceNotOwn(g + 1, k + 1), notes);
                    faces.Add((Face2)Safe(face)!);
                }
                groups.Add(faces);
            }
            List<IReadOnlyList<FaceSignature>> wanted = NormalCutRules.MirroredGroups(r, ctx.Mirror).Select(g => (IReadOnlyList<FaceSignature>)g).ToList();

            // ---- the offset plane (required in that mode) or the direction (optional)
            bool offsetMode = r.Parameters == NormalCutParameterTypes.OffsetPlane;
            GeometryRef? original = offsetMode ? r.OffsetPlane : r.CutDirection;
            object? reference = null;
            if (original != null)
            {
                try
                {
                    reference = Find(ctx, original, notes);
                }
                catch (InvalidOperationException ex)
                {
                    // A plane the resolver could not make.
                    notes.Add(ex.Message);
                    ctx.Topology.Invalidate();
                }
                if (offsetMode && reference == null) return Fail(NormalCutRules.OffsetPlaneMissing, notes);
                // The top or bottom face of the copy's sheet, not of the original's.
                if (offsetMode && ctx.SeparateBody && reference is Face2 && !CornerSupport.OnOwnBody(ctx, reference))
                    return Fail(NormalCutRules.OffsetPlaneMissing, notes);
                if (!offsetMode && reference == null) notes.Add(NormalCutRules.DirectionMissing);
            }
            else if (offsetMode)
            {
                return Fail(NormalCutRules.OffsetPlaneUnreadable, notes);
            }
            int referenceMark = offsetMode ? NormalCutRules.OffsetPlaneMark : NormalCutRules.DirectionMark;
            Func<object?, bool>? sameReference = reference == null ? null : (Func<object?, bool>)(got => SameEntity(ctx, reference, got));

            // ---- create: each path read back, removed again if its groups are not the mirrored ones
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            Feature? cut = null;
            NormalCutRecord? got = null;
            foreach (NormalCutPath path in NormalCutRules.Paths)
            {
                string? failure = null;
                Feature? made = null;
                try
                {
                    made = path switch
                    {
                        NormalCutPath.DataObject => ByDataObject(doc, r, groups, reference, referenceMark, out failure),
                        NormalCutPath.Obsolete => ByObsoleteCalls(doc, r, groups, reference, referenceMark, out failure),
                        _ => ByObsoleteHelpOrder(doc, r, groups, reference, referenceMark, out failure),
                    };
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                    if (path != NormalCutPath.DataObject)
                    {
                        SwSelect.Clear(doc);
                        CloseObsolete(doc.FeatureManager);
                    }
                }
                finally
                {
                    SwSelect.Clear(doc);
                    ctx.Topology.Invalidate();
                }
                made ??= SwSheetMetal.NewOfType(doc, before.Names, "NormalCut", d => d is SMNormalCutFeatureData2);
                if (made != null)
                {
                    got = SwSheetMetal.ReadBackNormalCut(doc, made, wanted, null, out bool same, out int count, out _);
                    if (got != null && same)
                    {
                        cut = made;
                        if (tried.Count > 0) info.Add($"Normaler Schnitt angelegt über {NormalCutRules.PathName(path)}.");
                        break;
                    }
                    // A path that stopped half-way and closed its open cut keeps its reason in front.
                    string why = NormalCutRules.ReadBackFailure(got != null, count, wanted.Count);
                    failure = failure == null ? why : $"{failure} {why}";
                }
                tried.Add($"{NormalCutRules.PathName(path)}: {failure ?? "kein normaler Schnitt entstanden."}");
                if (!CornerSupport.RemoveMade(ctx, before.Names, tried)) break;
            }
            if (cut == null || got == null) return Fail(NormalCutRules.NotCreated(tried), notes);

            // ---- values and reference: one edit where they differ, read back
            SwSheetMetal.ReadBackNormalCut(doc, cut, wanted, sameReference, out _, out _, out bool referenceOk);
            if (!NormalCutRules.Differences(r, got).None || !referenceOk)
            {
                Feature live = cut;
                bool edited = FeatureEdit.NormalCut(doc, live, d => WriteValues(doc, d, r, offsetMode, referenceOk ? null : reference));
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
                if (!edited) notes.Add("Werte des normalen Schnitts ließen sich nicht nachträglich setzen.");
            }
            NormalCutRecord? now = SwSheetMetal.ReadBackNormalCut(doc, cut, wanted, sameReference, out bool stillSame, out _, out bool referenceNow);
            if (now == null) notes.Add("Der normale Schnitt ließ sich nicht zurücklesen.");
            else
            {
                notes.AddRange(NormalCutRules.Differences(r, now).Differences.Select(x => $"Normaler Schnitt: {x}"));
                if (!stillSame) notes.Add("Normaler Schnitt: Nach dem Setzen der Werte sind es nicht mehr die gespiegelten Flächengruppen.");
                if (!referenceNow) notes.Add(NormalCutRules.ReferenceNotTaken);
            }

            ctx.Register(r.Name, cut);
            BuildResult result = Ok(cut, NormalCutRules.Describe(r), notes);
            result.Info.AddRange(info);
            return result;
        }

        /// <summary>
        /// The data object (help): the values, then per group the reference (mark 4 or 8) and the group's faces (mark 1)
        /// selected and taken by CreateGroup, the selection cleared after each; the reference selected once more for
        /// CreateFeature. Null and why if a group is refused.
        /// </summary>
        private static Feature? ByDataObject(ModelDoc2 doc, NormalCutRecord r, List<List<Face2>> groups, object? reference, int referenceMark, out string? failure)
        {
            failure = null;
            FeatureManager fm = doc.FeatureManager;
            if (!((object)fm.CreateDefinition(NormalCutRules.NormalCutId) is SMNormalCutFeatureData2 d))
            {
                failure = "Datenobjekt nicht verfügbar.";
                return null;
            }
            d.AutoPropagation = r.AutoPropagation;
            d.OptimizeGeometry = r.OptimizeGeometry;
            d.NormalCutParameters = r.Parameters;
            for (int g = 0; g < groups.Count; g++)
            {
                SwSelect.Clear(doc);
                if (reference != null) SwSelect.Select(doc, reference, referenceMark, false);
                if (!CornerSupport.SelectAll(doc, groups[g].Cast<object>().ToList(), groups[g].Select(_ => NormalCutRules.FaceMark).ToList(), append: reference != null))
                {
                    failure = $"Flächen der Gruppe {g + 1} ließen sich nicht wählen.";
                    return null;
                }
                int error = 0;
                object? group = (object)d.CreateGroup(out error);
                if (group == null || error != 0)
                {
                    failure = $"Flächengruppe {g + 1} nicht angenommen (Code {error}).";
                    return null;
                }
            }
            SwSelect.Clear(doc);
            if (reference != null) SwSelect.Select(doc, reference, referenceMark, false);
            if (r.Parameters == NormalCutParameterTypes.OffsetPlane)
            {
                try
                {
                    // Valid only with an offset plane (help); an edit sets them if they did not take.
                    d.LinkToKFactor = r.LinkToKFactor;
                    if (!r.LinkToKFactor) d.LayerAdjustmentValue = r.LayerAdjustment;
                }
                catch (Exception)
                {
                    // Read back and set in the edit.
                }
            }
            return fm.CreateFeature(d);
        }

        /// <summary>
        /// The obsolete calls (B51), deliberately not in the help's numbered order (that is <see cref="ByObsoleteHelpOrder"/>):
        /// AddSMNormalCutType, per group its faces with mark 1 and AddSMNormalCut (a code other than 0 is a face it does
        /// not take), the reference with mark 4 or 8, FinishSMNormalCut. A group refused after AddSMNormalCutType still
        /// ends with FinishSMNormalCut, so the next path does not inherit an open cut; what that makes is read back and removed.
        /// </summary>
        private static Feature? ByObsoleteCalls(ModelDoc2 doc, NormalCutRecord r, List<List<Face2>> groups, object? reference, int referenceMark, out string? failure)
        {
            failure = null;
            FeatureManager fm = doc.FeatureManager;
            fm.AddSMNormalCutType(r.AutoPropagation, r.OptimizeGeometry, r.Parameters == NormalCutParameterTypes.Extent);
            for (int g = 0; g < groups.Count && failure == null; g++)
            {
                if (!CornerSupport.SelectAll(doc, groups[g].Cast<object>().ToList(), groups[g].Select(_ => NormalCutRules.FaceMark).ToList()))
                {
                    failure = $"Flächen der Gruppe {g + 1} ließen sich nicht wählen.";
                    break;
                }
                int error = fm.AddSMNormalCut();
                if (error != 0) failure = $"Flächengruppe {g + 1} nicht angenommen (Code {error}).";
            }
            SwSelect.Clear(doc);
            if (failure != null)
            {
                CloseObsolete(fm);
                return null;
            }
            if (reference != null) SwSelect.Select(doc, reference, referenceMark, false);
            return (object)fm.FinishSMNormalCut() as Feature;
        }

        /// <summary>
        /// The obsolete calls in the order the help numbers them (AddSMNormalCutType remarks, B51): AddSMNormalCut (nothing is
        /// selected yet, its code does not count), AddSMNormalCutType, every group's faces – the first with mark 1, the
        /// others with mark 2 (<see cref="NormalCutRules.HelpOrderMarks"/>; what mark 2 means is not documented) –, the
        /// reference with mark 4 or 8, FinishSMNormalCut with all of it still selected.
        /// </summary>
        private static Feature? ByObsoleteHelpOrder(ModelDoc2 doc, NormalCutRecord r, List<List<Face2>> groups, object? reference, int referenceMark, out string? failure)
        {
            failure = null;
            FeatureManager fm = doc.FeatureManager;
            SwSelect.Clear(doc);
            fm.AddSMNormalCut();
            fm.AddSMNormalCutType(r.AutoPropagation, r.OptimizeGeometry, r.Parameters == NormalCutParameterTypes.Extent);
            for (int g = 0; g < groups.Count; g++)
            {
                if (!CornerSupport.SelectAll(doc, groups[g].Cast<object>().ToList(), NormalCutRules.HelpOrderMarks(groups[g].Count), append: true))
                {
                    failure = $"Flächen der Gruppe {g + 1} ließen sich nicht wählen.";
                    SwSelect.Clear(doc);
                    CloseObsolete(fm);
                    return null;
                }
            }
            if (reference != null) SwSelect.Select(doc, reference, referenceMark, true);
            return (object)fm.FinishSMNormalCut() as Feature;
        }

        /// <summary>Ends obsolete calls that stopped half-way; what it makes is found as new and removed by the caller.</summary>
        private static void CloseObsolete(FeatureManager fm)
        {
            try
            {
                fm.FinishSMNormalCut();
            }
            catch (Exception)
            {
                // Nothing open, or nothing it could make.
            }
        }

        /// <summary>
        /// The values in the edit, and the reference where it did not take: selected again inside the edit and taken
        /// from the selection (help: selections made before AccessSelections are not valid in it).
        /// </summary>
        private static void WriteValues(ModelDoc2 doc, SMNormalCutFeatureData2 d, NormalCutRecord r, bool offsetMode, object? reference)
        {
            if (d.NormalCutParameters != r.Parameters) d.NormalCutParameters = r.Parameters;
            if (d.AutoPropagation != r.AutoPropagation) d.AutoPropagation = r.AutoPropagation;
            if (d.OptimizeGeometry != r.OptimizeGeometry) d.OptimizeGeometry = r.OptimizeGeometry;
            if (reference != null)
            {
                SwSelect.Clear(doc);
                object? selected = SwSelect.Select(doc, reference, 0, false) ? (object)((SelectionMgr)(object)doc.SelectionManager).GetSelectedObject6(1, -1) : null;
                if (offsetMode) d.OffsetPlaneReference = selected ?? reference;
                else d.CutDirection = selected ?? reference;
                SwSelect.Clear(doc);
            }
            if (offsetMode)
            {
                d.LinkToKFactor = r.LinkToKFactor;
                if (!r.LinkToKFactor) d.LayerAdjustmentValue = r.LayerAdjustment;
            }
        }

        /// <summary>
        /// Is the reference a normal cut names the one the copy selected? Faces and edges by fingerprint and kernel
        /// distance, planes by their geometry; anything else is taken as it is (not checkable). Asked inside the cut's
        /// selection access, where the faces are in the state before it – the state they were found in.
        /// </summary>
        private static bool SameEntity(BuildContext ctx, object wanted, object? got)
        {
            if (got == null) return false;
            try
            {
                switch (wanted)
                {
                    case Face2 face when got is Face2 g:
                    {
                        FaceSignature w = TopologyCapture.Face(face), s = TopologyCapture.Face(g);
                        return w.CouldBe(s) && TopologyCapture.DistanceToFace(g, w.Sample) <= Tol.Length;
                    }
                    case Edge edge when got is Edge g:
                        return !double.IsPositiveInfinity(TopologyCapture.Edge(edge).DistanceTo(TopologyCapture.Edge(g)));
                    case Feature plane when PlaneOf(ctx, plane) is Plane3 w:
                        return PlaneOf(ctx, got) is Plane3 p && p.IsParallelTo(w) && Math.Abs(w.SignedDistance(p.Origin)) <= Tol.Length;
                    default:
                        return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The plane of a reference-plane feature (or the RefPlane object itself); null for anything else.</summary>
        private static Plane3? PlaneOf(BuildContext ctx, object o) => o switch
        {
            RefPlane rp => ctx.Math.PlaneOf(rp),
            Feature f when (object)f.GetSpecificFeature2() is RefPlane rp => ctx.Math.PlaneOf(rp),
            _ => (Plane3?)null,
        };
    }
}
