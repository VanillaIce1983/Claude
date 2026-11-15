using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Sheet-metal gusset (A4 (2) §7, P9, blech-plan S8). The mirrored legs in their order on mark 0 (CADapter,
    /// measured), InsertSheetMetalGussetFeature with the original's values (angles in radians; „draft“ only as the
    /// original has it – true without a draft makes the call return null), then one edit for the reference line and
    /// point and the flat-pattern display; the variant with the arrays (…Feature2) where that returns nothing.
    /// Where it sits on its bend is decided by its own check, always (A4 P9): the section of the new gusset – the
    /// middle of its faces' extent along the mirrored bend line – must lie where the mirrored original's lies
    /// (10 µm). The variants (<see cref="GussetRules.Variants"/>) are tried in turn, each made afresh; the first
    /// that fits is kept, otherwise the closest is made again with a note. FlipOffset follows σ of a reference line
    /// of its own; d1/d2 swapped is offered to the mass check (profile dimensions only).
    /// </summary>
    internal sealed class SheetMetalGussetBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is SheetMetalGussetRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (SheetMetalGussetRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();
            if (r.ProfileType == GussetProfileTypes.Custom) return Fail(GussetRules.CustomProfile, notes);

            // ---- legs, references and the vertex at the mirrored bend end: before anything is made
            List<Face2>? legs = CornerSupport.ResolveFaces(ctx, r.SupportingFaces, "der Versteifungsrippe", notes, out string? why);
            if (legs == null) return Fail(why!, notes);
            object? line = Find(ctx, r.ReferenceLine, notes);
            if (r.ReferenceLine != null && line == null) return Fail("Bezugslinie der Versteifungsrippe im Ziel nicht gefunden.", notes);
            object? own = Find(ctx, r.ReferencePoint, notes);
            if (r.ReferencePoint != null && own == null) return Fail("Bezugspunkt der Versteifungsrippe im Ziel nicht gefunden.", notes);
            if (own != null && ctx.SeparateBody && FlangeSupport.BodyOf(own) is Body2 ownBody && !ctx.IsOwnBody(ownBody))
                return Fail("Bezugspunkt der Versteifungsrippe liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.", notes);
            object? anchor = null;
            if (r.Anchor == GussetAnchor.BendEnd && r.AnchorPoint is Vec3 end && ctx.Topology.FindVertex(ctx.Mirror.Apply(end)) is Vertex v &&
                (!ctx.SeparateBody || CornerSupport.OnOwnBody(ctx, v)))
                anchor = Safe(v);
            if (r.Anchor == GussetAnchor.BendEnd && anchor == null) info.Add(GussetRules.AnchorVertexMissing);
            if (GussetRules.AnchorNote(r) is string anchorNote && anchorNote.Length > 0) info.Add(anchorNote);
            bool flip = GussetRules.FlipOffsetFor(r.FlipOffset, r.ReferenceLine?.Direction, ctx.Mirror, ReferenceDirection(ctx, line));
            object? PointOf(GussetVariant x) => x.Point switch
            {
                GussetPoint.Own => own,
                GussetPoint.Anchor => anchor,
                _ => null,
            };
            List<GussetVariant> variants = GussetRules.Variants(r).Where(x => x.Point != GussetPoint.Anchor || anchor != null).ToList();
            if (variants.Count == 0) return Fail("Für die Versteifungsrippe gibt es keine Variante, sie zu platzieren.", notes);

            // ---- the variants, each made afresh, until the section lies where the mirrored original's does
            bool checkable = r.BendLine.HasValue && r.SectionPoint.HasValue;
            Line3 bend = checkable ? ctx.Mirror.Apply(r.BendLine!.Value) : default;
            Vec3 wanted = checkable ? ctx.Mirror.Apply(r.SectionPoint!.Value) : default;
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            Feature? gusset = null;
            GussetVariant? chosen = null;
            GussetVariant? best = null;
            double bestDeviation = double.PositiveInfinity;
            foreach (GussetVariant variant in checkable ? variants : variants.Take(1))
            {
                Feature? made = Make(ctx, r, legs, line, PointOf(variant), variant, flip, before.Names, tried);
                if (made == null) continue;
                if (!checkable)
                {
                    gusset = made;
                    chosen = variant;
                    break;
                }
                double? deviation = Measure(made, bend, wanted);
                if (deviation is double fits && fits <= GussetRules.SectionTolerance)
                {
                    gusset = made;
                    chosen = variant;
                    break;
                }
                tried.Add(GussetRules.Tried(variant.Description, deviation));
                if (deviation is double d && d < bestDeviation)
                {
                    best = variant;
                    bestDeviation = d;
                }
                if (!CornerSupport.RemoveMade(ctx, before.Names, tried)) break;
            }
            if (gusset == null && best != null && SwSheetMetal.NewSince(doc, before.Names).Count == 0)
            {
                // None fits: the closest again, with a note.
                gusset = Make(ctx, r, legs, line, PointOf(best), best, flip, before.Names, tried);
                chosen = best;
                if (gusset != null)
                {
                    notes.Add(GussetRules.PlaceOff(bestDeviation, best.Description));
                    info.AddRange(tried);
                }
            }
            if (gusset == null || chosen == null)
            {
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                notes.AddRange(tried);
                if (left.Count > 0) notes.Add(FlangeSupport.LeftBehind(left));
                return Fail("SOLIDWORKS hat die Versteifungsrippe nicht erzeugt.", notes);
            }
            if (checkable && chosen != best)
            {
                info.Add(chosen == variants[0] ? GussetRules.PlaceChecked : GussetRules.Placed(chosen.Description));
                if (chosen != variants[0]) info.AddRange(tried);
            }

            // ---- read-back: the values, and that the references took
            SheetMetalGussetRecord? got = SwSheetMetal.ReadBackGusset(doc, gusset, out bool hasLine, out bool hasPoint);
            if (got == null) notes.Add("Die Versteifungsrippe ließ sich nicht zurücklesen.");
            else
            {
                notes.AddRange(GussetRules.Differences(r, got).Differences.Select(x => $"Versteifungsrippe: {x}"));
                if (line != null && !hasLine) notes.Add("Versteifungsrippe: Die Bezugslinie ließ sich nicht setzen.");
                if (PointOf(chosen) != null && !hasPoint) notes.Add("Versteifungsrippe: Der Bezugspunkt ließ sich nicht setzen.");
            }

            ctx.Register(r.Name, gusset);
            BuildResult result = Ok(gusset, GussetRules.Describe(r), notes);
            result.Info.AddRange(info);
            Feature g = gusset;
            if (r.ProfileDimensionScheme == GussetProfileDimTypes.ProfileDimensions)
                result.Alternatives.Add(new Alternative(GussetRules.LegsSwapped, () => FeatureEdit.Gusset(doc, g, d => d.FlipDimSides = !d.FlipDimSides)));
            // Without the place check, the mass check decides the side of the offset.
            if (!checkable && r.UseOffset)
                result.Alternatives.Add(new Alternative(GussetRules.OtherSide, () => FeatureEdit.Gusset(doc, g, d => d.FlipOffset = !d.FlipOffset)));
            return result;
        }

        /// <summary>How far the section of the new gusset lies from the wanted one along the bend; null if its faces do not read.</summary>
        private static double? Measure(Feature gusset, Line3 bend, Vec3 wanted) =>
            GussetRules.Section(bend, SwSheetMetal.FacePoints(gusset)) is Vec3 section ? GussetRules.Deviation(bend, wanted, section) : (double?)null;

        /// <summary>
        /// One gusset as <paramref name="variant"/> says: the legs on mark 0 and the call with 21 values (no reference
        /// point can be passed: the offset is on), else the call with arrays (legs, reference line, reference point);
        /// then one edit for the references (after the first call), the offset and the flat-pattern display. What a
        /// failed attempt made is removed.
        /// </summary>
        private static Feature? Make(BuildContext ctx, SheetMetalGussetRecord r, List<Face2> legs, object? line, object? point, GussetVariant variant, bool baseFlip,
            HashSet<string> before, List<string> tried)
        {
            ModelDoc2 doc = ctx.Doc;
            bool flip = baseFlip ^ variant.FlipToggled;
            Feature? made = null;
            bool withArrays = false;
            string? failure = null;
            try
            {
                if (CornerSupport.SelectAll(doc, legs.Cast<object>().ToList(), legs.Select(_ => 0).ToList()))
                    made = doc.FeatureManager.InsertSheetMetalGussetFeature(true, variant.Offset, flip, r.ProfileDimensionScheme, r.IndentDepth,
                        r.ProfileLengthDim, r.UseAngleDimForProfile, r.ProfileHeightDim, r.ProfileAngleDim, r.FlipDimSides, r.IndentWidth, r.GussetThickness,
                        r.DraftSideFaces, r.DraftAngle, r.FilletInnerCorners, r.InnerCornerFilletRadius, r.FilletOuterCorners, r.OuterCornerFilletRadius,
                        r.GussetType, r.FilletGussetEdges, r.EdgeFilletRadius);
                else failure = "Schenkelflächen ließen sich nicht wählen";
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
            made ??= SwSheetMetal.NewOfType(doc, before, "SMGusset", d => d is SMGussetFeatureData);
            if (made == null)
            {
                if (FlangeSupport.RemoveMade(ctx, before).Count > 0) return Left(ctx, before, variant, tried);
                try
                {
                    // Arrays instead of a selection (A4 §7: the second attempt); the reference point given here, the offset as the original has it.
                    made = doc.FeatureManager.InsertSheetMetalGussetFeature2(point == null || r.UseOffset, variant.Offset, flip, r.ProfileDimensionScheme,
                        r.IndentDepth, r.ProfileLengthDim, r.UseAngleDimForProfile, r.ProfileHeightDim, r.ProfileAngleDim, r.FlipDimSides, r.IndentWidth,
                        r.GussetThickness, r.DraftSideFaces, r.DraftAngle, r.FilletInnerCorners, r.InnerCornerFilletRadius, r.FilletOuterCorners,
                        r.OuterCornerFilletRadius, r.GussetType, r.FilletGussetEdges, r.EdgeFilletRadius, legs.Cast<object>().ToArray(),
                        line == null ? null : new[] { line }, point == null ? null : new[] { point });
                    withArrays = true;
                }
                catch (Exception ex)
                {
                    failure = (failure == null ? "" : failure + "; ") + ex.Message;
                }
                finally
                {
                    SwSelect.Clear(doc);
                    ctx.Topology.Invalidate();
                }
                made ??= SwSheetMetal.NewOfType(doc, before, "SMGusset", d => d is SMGussetFeatureData);
            }
            if (made == null)
            {
                tried.Add($"{variant.Description}: keine Versteifungsrippe entstanden{(failure == null ? "" : $" ({failure})")}.");
                return FlangeSupport.RemoveMade(ctx, before).Count > 0 ? Left(ctx, before, variant, tried) : null;
            }

            // ---- references, offset and display in one edit; the call with arrays took the references already
            bool references = !withArrays && (line != null || point != null);
            bool offset = point != null && !r.UseOffset;
            if (references || offset || r.OverrideDocSettings)
            {
                Feature g = made;
                bool edited = FeatureEdit.Gusset(doc, g, d =>
                {
                    if (!withArrays && line != null) d.ReferenceLine = line;
                    if (!withArrays && point != null) d.ReferencePoint = point;
                    if (point != null && d.UseOffset != r.UseOffset) d.UseOffset = r.UseOffset;
                    if (r.OverrideDocSettings)
                    {
                        d.OverrideDocSettings = true;
                        d.ShowProfile = r.ShowProfile;
                        d.ShowCenter = r.ShowCenter;
                    }
                });
                if (!edited) tried.Add($"{variant.Description}: Bezüge oder Anzeige der Versteifungsrippe ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            return made;
        }

        /// <summary>A failed attempt left features behind: said, and nothing more is made.</summary>
        private static Feature? Left(BuildContext ctx, HashSet<string> before, GussetVariant variant, List<string> tried)
        {
            tried.Add($"{variant.Description}: {FlangeSupport.LeftBehind(SwSheetMetal.NewSince(ctx.Doc, before).Select(f => f.Name))}");
            return null;
        }
    }
}
