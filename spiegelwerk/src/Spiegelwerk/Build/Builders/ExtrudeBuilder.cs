using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Boss-Extrude, Cut-Extrude and their thin variants.
    ///
    /// Direction: derived from σ of the rebuilt sketch (see DirectionRules); the flags that
    /// depend on the side of an open profile cannot be derived and are offered as
    /// alternatives to the verifier.
    ///
    /// Everything that needs a reference – end conditions up to a face/vertex, the start
    /// face, the direction of extrusion, the feature scope in "separate body" mode – is set
    /// in one ModifyDefinition after creation, so no guessing which selection mark the
    /// creation method wants for what. The feature is first created in a temporary state
    /// that always succeeds: blind for a boss, through all for a cut (a blind cut from a
    /// sketch plane away from the part would remove nothing and fail). If the final
    /// definition cannot be applied, the feature is removed again.
    /// </summary>
    internal sealed class ExtrudeBuilder : IFeatureBuilder
    {
        private const double PlaceholderDepth = 0.01;

        public bool Handles(FeatureRecord record) => record is ExtrudeRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (ExtrudeRecord)record;
            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap sketch))
                return BuildResult.Failed($"Skizze \"{r.SketchName}\" wurde nicht neu aufgebaut.");

            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();

            // ---- references, resolved before anything changes the model
            object? end1 = ResolveEnd(r.Side1, ctx, sketch, notes, out EndCondition final1, out double depth1);
            object? end2 = null;
            EndCondition final2 = r.Side2.End;
            double depth2 = r.Side2.Depth;
            if (r.BothDirections) end2 = ResolveEnd(r.Side2, ctx, sketch, notes, out final2, out depth2);
            if ((ExtrudeSide.NeedsReference(final1) && end1 == null) || (r.BothDirections && ExtrudeSide.NeedsReference(final2) && end2 == null))
                return Fail("Bezug der Endbedingung im Ziel nicht gefunden.", notes);

            object? startEntity = null;
            if (r.Start == StartCondition.Surface || r.Start == StartCondition.Vertex)
            {
                startEntity = r.StartReference == null ? null : Safe(ctx.Topology.Resolve(r.StartReference.Mirror(ctx.Mirror), notes));
                if (startEntity == null) return Fail("Bezug der Startbedingung im Ziel nicht gefunden.", notes);
            }

            object? dir1 = null, dir2 = null;
            if (r.DirectionReference1 != null)
            {
                dir1 = Safe(ctx.Topology.Resolve(r.DirectionReference1.Mirror(ctx.Mirror), notes));
                if (r.DirectionReference2 != null) dir2 = Safe(ctx.Topology.Resolve(r.DirectionReference2.Mirror(ctx.Mirror), notes));
                if (dir1 == null || (r.DirectionReference2 != null && dir2 == null))
                {
                    dir1 = dir2 = null;
                    notes.Add("Richtung der Extrusion: Bezug im Ziel nicht gefunden, es wird senkrecht zur Skizze extrudiert.");
                }
            }

            // ---- separate body: never let the copy touch the original
            int ownBefore = ctx.SeparateBody ? ctx.OwnBodies().Count : 0;
            if (ctx.SeparateBody && r.IsCut && ownBefore == 0)
                return Fail("Es gibt noch keinen gespiegelten Körper, auf den der Schnitt wirken könnte; das Original bleibt unberührt.", notes);
            bool scope = ctx.SeparateBody && (r.IsCut || (r.Merge && ownBefore > 0));
            bool createMerge = !ctx.SeparateBody && r.Merge;

            // ---- temporary creation state
            EndCondition tmp1 = Temporary(r, end1, startEntity, final1);
            EndCondition tmp2 = r.BothDirections ? Temporary(r, end2, startEntity, final2) : final2;
            double tmpDepth1 = depth1 > 0 ? depth1 : PlaceholderDepth;
            double tmpDepth2 = depth2 > 0 ? depth2 : PlaceholderDepth;
            bool reverse = DirectionRules.ReverseFlag(r.Reverse, sketch.Sigma);
            int t0 = r.Start == StartCondition.Offset ? (int)StartCondition.Offset : (int)StartCondition.SketchPlane;
            bool flipStart = sketch.Sigma < 0 ? !r.StartOffsetReverse : r.StartOffsetReverse;

            List<object>? contours = MapContours(r.Contours, sketch, notes);
            bool singleDir = !r.BothDirections;
            Feature? f = Create(ctx, r, sketch, contours, singleDir, reverse, createMerge, tmp1, tmpDepth1, tmp2, tmpDepth2, t0, flipStart);
            bool contoursPending = false;
            if (f == null && contours != null)
            {
                f = Create(ctx, r, sketch, null, singleDir, reverse, createMerge, tmp1, tmpDepth1, tmp2, tmpDepth2, t0, flipStart);
                contoursPending = f != null;
            }
            bool tempChanged = false;
            if (f == null && r.IsCut)
            {
                // Last resort for cuts: through all in both directions certainly cuts something.
                f = Create(ctx, r, sketch, null, true, reverse, createMerge, EndCondition.ThroughAllBoth, 0, EndCondition.Blind, 0, (int)StartCondition.SketchPlane, false);
                contoursPending = f != null && contours != null;
                tempChanged = f != null;
            }
            if (f == null)
                return Fail("SOLIDWORKS hat das Feature nicht erzeugt" + (r.IsCut ? " (auch „durch alles“ hat keinen Körper geschnitten)." : "."), notes);

            // ---- final definition in one step
            bool needsEdit = tempChanged || contoursPending || scope || end1 != null || end2 != null || startEntity != null || dir1 != null
                             || tmp1 != final1 || tmp2 != final2;
            if (needsEdit)
            {
                bool ok = FeatureEdit.Extrude(doc, f, d =>
                {
                    d.BothDirections = r.BothDirections;
                    if (tempChanged)
                    {
                        d.FromType = t0;
                        d.FromOffsetDistance = r.StartOffset;
                        d.FromOffsetReverse = flipStart;
                    }
                    ApplySide(d, true, r.Side1, final1, depth1, end1);
                    if (r.BothDirections) ApplySide(d, false, r.Side2, final2, depth2, end2);
                    if (startEntity != null)
                    {
                        d.FromType = (int)r.Start;
                        d.SetFromEntity(startEntity);
                    }
                    if (dir1 != null) d.SetDirectionReference(dir1, dir2);
                    if (contoursPending) d.Contours = contours!.ToArray();
                    if (scope)
                    {
                        if (!r.IsCut) d.Merge = true;
                        d.FeatureScope = true;
                        d.AutoSelect = false;
                        d.FeatureScopeBodies = OwnBodiesBeforeFeature(ctx);
                    }
                });
                if (!ok)
                {
                    Delete(ctx, f);
                    return Fail("Endbedingung, Bezüge oder Feature-Bereich ließen sich nicht setzen; das Feature wurde wieder entfernt.", notes);
                }
            }

            ctx.Register(r.Name, f);
            var result = BuildResult.Ok(f, Describe(r, reverse != r.Reverse));
            result.Notes.AddRange(notes);
            if (notes.Count > 0) result.Status = ReportStatus.Warning;
            AddAlternatives(result, ctx, f, r);
            return result;
        }

        /// <summary>The condition used while creating; references are attached afterwards.</summary>
        private static EndCondition Temporary(ExtrudeRecord r, object? reference, object? startEntity, EndCondition final)
        {
            if (reference == null && !(r.IsCut && startEntity != null)) return final;
            return r.IsCut ? EndCondition.ThroughAll : EndCondition.Blind;
        }

        private static void ApplySide(ExtrudeFeatureData2 d, bool forward, ExtrudeSide side, EndCondition cond, double depth, object? reference)
        {
            d.SetEndCondition(forward, (int)cond);
            if (cond == EndCondition.Blind || cond == EndCondition.MidPlane || cond == EndCondition.OffsetFromSurface)
                d.SetDepth(forward, depth);
            if (reference != null) d.SetEndConditionReference(forward, reference);
            if (cond == EndCondition.OffsetFromSurface)
            {
                d.SetReverseOffset(forward, side.OffsetReverse);
                d.SetTranslateSurface(forward, side.TranslateSurface);
            }
        }

        private static Feature? Create(BuildContext ctx, ExtrudeRecord r, SketchMap sketch, List<object>? contours, bool sd,
            bool reverse, bool merge, EndCondition t1, double d1, EndCondition t2, double d2, int t0, bool flipStart)
        {
            ModelDoc2 doc = ctx.Doc;
            FeatureManager fm = doc.FeatureManager;
            SwSelect.Clear(doc);
            if (contours != null)
            {
                for (int i = 0; i < contours.Count; i++) SwSelect.Select(doc, contours[i], 0, i > 0);
            }
            else
            {
                SwSelect.Select(doc, sketch.Feature, 0, false);
            }

            ExtrudeSide s1 = r.Side1, s2 = r.Side2;
            Feature? f;
            if (!r.IsCut && !r.IsThin)
            {
                f = fm.FeatureExtrusion3(sd, r.FlipSideToCut, reverse, (int)t1, (int)t2, d1, d2,
                    s1.Draft, s2.Draft, s1.DraftOutward, s2.DraftOutward, s1.DraftAngle, s2.DraftAngle,
                    s1.OffsetReverse, s2.OffsetReverse, s1.TranslateSurface, s2.TranslateSurface,
                    merge, false, true, t0, r.StartOffset, flipStart);
            }
            else if (r.IsCut && !r.IsThin)
            {
                f = fm.FeatureCut4(sd, r.FlipSideToCut, reverse, (int)t1, (int)t2, d1, d2,
                    s1.Draft, s2.Draft, s1.DraftOutward, s2.DraftOutward, s1.DraftAngle, s2.DraftAngle,
                    s1.OffsetReverse, s2.OffsetReverse, s1.TranslateSurface, s2.TranslateSurface,
                    r.NormalCut, false, true, false, false, false, t0, r.StartOffset, flipStart, r.OptimizeGeometry);
            }
            else if (!r.IsCut)
            {
                f = fm.FeatureExtrusionThin2(sd, r.FlipSideToCut, reverse, (int)t1, (int)t2, d1, d2,
                    s1.Draft, s2.Draft, s1.DraftOutward, s2.DraftOutward, s1.DraftAngle, s2.DraftAngle,
                    s1.OffsetReverse, s2.OffsetReverse, s1.TranslateSurface, s2.TranslateSurface,
                    merge, s1.WallThickness, s2.WallThickness, r.CapThickness, r.ThinType, r.CapEnds ? 1 : 0,
                    r.AutoFillet, r.AutoFilletRadius, false, true, t0, r.StartOffset, flipStart);
            }
            else
            {
                f = fm.FeatureCutThin2(sd, r.FlipSideToCut, reverse, (int)t1, (int)t2, d1, d2,
                    s1.Draft, s2.Draft, s1.DraftOutward, s2.DraftOutward, s1.DraftAngle, s2.DraftAngle,
                    s1.OffsetReverse, s2.OffsetReverse, s1.TranslateSurface, s2.TranslateSurface,
                    s1.WallThickness, s2.WallThickness, r.CapThickness, r.ThinType, r.CapEnds ? 1 : 0,
                    r.AutoFillet, r.AutoFilletRadius, false, true, t0, r.StartOffset, flipStart);
            }
            SwSelect.Clear(doc);
            return f;
        }

        /// <summary>
        /// Finds the mirrored reference of an up-to/offset end condition. If it cannot be found
        /// but it is a plane parallel to the sketch, the condition becomes the equivalent blind
        /// depth (<paramref name="cond"/> = Blind); otherwise the caller fails the feature.
        /// </summary>
        private static object? ResolveEnd(ExtrudeSide side, BuildContext ctx, SketchMap sketch, List<string> notes, out EndCondition cond, out double depth)
        {
            cond = side.End;
            depth = side.Depth;
            if (!ExtrudeSide.NeedsReference(side.End)) return null;
            if (side.Reference == null)
            {
                notes.Add($"Endbedingung {side.End}: Bezug im Original nicht lesbar.");
                return null;
            }

            object? target = Safe(ctx.Topology.Resolve(side.Reference.Mirror(ctx.Mirror), notes));
            if (target != null) return target;

            double? blind = BlindEquivalent(side, sketch);
            if (blind.HasValue)
            {
                cond = EndCondition.Blind;
                depth = blind.Value;
                notes.Add($"Endbedingung {side.End} ohne Bezug im Ziel, ersetzt durch Blind {depth * 1000:0.###} mm.");
            }
            return null;
        }

        /// <summary>Up to / offset from a plane parallel to the sketch plane has a fixed depth.</summary>
        private static double? BlindEquivalent(ExtrudeSide side, SketchMap sketch)
        {
            if (side.End != EndCondition.UpToSurface && side.End != EndCondition.OffsetFromSurface) return null;
            Plane3 sketchPlane = sketch.Source.Frame.Plane;
            Plane3? refPlane = side.Reference?.Kind switch
            {
                GeometryRefKind.Plane => side.Reference.Plane,
                GeometryRefKind.Face when side.Reference.Face!.Kind == Core.Matching.SurfaceKind.Plane =>
                    new Plane3(side.Reference.Face.Sample, side.Reference.Face.Normal),
                _ => (Plane3?)null,
            };
            if (refPlane == null || !refPlane.Value.IsParallelTo(sketchPlane)) return null;
            double depth = Math.Abs(refPlane.Value.SignedDistance(sketchPlane.Origin));
            if (side.End == EndCondition.OffsetFromSurface) depth += side.OffsetReverse ? side.Depth : -side.Depth;
            return depth > Tol.Length ? depth : (double?)null;
        }

        private static void AddAlternatives(BuildResult result, BuildContext ctx, Feature f, ExtrudeRecord r)
        {
            ModelDoc2 doc = ctx.Doc;
            result.Alternatives.Add(new Alternative("Richtung umgekehrt",
                () => FeatureEdit.Extrude(doc, f, d => d.ReverseDirection = !d.ReverseDirection)));
            if (r.IsThin && IsThinOneSided(r.ThinType))
                result.Alternatives.Add(new Alternative("Wandseite gewechselt",
                    () => FeatureEdit.Extrude(doc, f, d => d.ThinWallType = OtherThinSide(d.ThinWallType))));
            if (r.IsThin && r.ThinType == (int)swThinWallType_e.swThinWallTwoDirection && Math.Abs(r.Side1.WallThickness - r.Side2.WallThickness) > 1e-12)
                result.Alternatives.Add(new Alternative("Wandstärken getauscht", () => FeatureEdit.Extrude(doc, f, d =>
                {
                    double a = d.GetWallThickness(true), b = d.GetWallThickness(false);
                    d.SetWallThickness(true, b);
                    d.SetWallThickness(false, a);
                })));
            if (r.IsCut)
                result.Alternatives.Add(new Alternative("Materialseite gewechselt",
                    () => FeatureEdit.Extrude(doc, f, d => d.FlipSideToCut = !d.FlipSideToCut)));
            if (r.Start == StartCondition.Offset)
                result.Alternatives.Add(new Alternative("Startversatz umgekehrt",
                    () => FeatureEdit.Extrude(doc, f, d => d.FromOffsetReverse = !d.FromOffsetReverse)));
        }

        private static string Describe(ExtrudeRecord r, bool reversed)
        {
            string what = (r.IsCut ? "Schnitt" : "Aufsatz") + (r.IsThin ? " (dünnwandig)" : "");
            string ends = r.BothDirections ? $"{r.Side1.End} / {r.Side2.End}" : r.Side1.End.ToString();
            return $"{what}, {ends}" + (reversed ? ", Richtungsflag umgekehrt (Skizzennormale entgegengesetzt)" : "");
        }
    }
}
