using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Boss-Extrude, Cut-Extrude and their thin variants.
    ///
    /// Direction: derived from σ of the rebuilt sketch (see DirectionRules); the flags that
    /// depend on the side of an open profile cannot be derived and are offered as
    /// alternatives to the verifier.
    /// End conditions with a reference (up to surface/vertex, offset from surface): the
    /// feature is created blind first and then switched to the mirrored reference with
    /// ModifyDefinition – no guessing which selection mark the creation method wants for
    /// which direction.
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

            object? end1 = ResolveEnd(r.Side1, ctx, sketch, notes, out EndCondition t1, out double d1);
            object? end2 = null;
            EndCondition t2 = r.Side2.End;
            double d2 = r.Side2.Depth;
            if (r.BothDirections) end2 = ResolveEnd(r.Side2, ctx, sketch, notes, out t2, out d2);
            if ((ExtrudeSide.NeedsReference(r.Side1.End) && end1 == null && t1 != EndCondition.Blind) ||
                (r.BothDirections && ExtrudeSide.NeedsReference(r.Side2.End) && end2 == null && t2 != EndCondition.Blind))
                return Fail("Bezug der Endbedingung im Ziel nicht gefunden.", notes);

            object? startEntity = null;
            if (r.Start == StartCondition.Surface || r.Start == StartCondition.Vertex)
            {
                startEntity = r.StartReference == null ? null : ctx.Topology.Resolve(r.StartReference.Mirror(ctx.Mirror), notes);
                if (startEntity == null) return Fail("Bezug der Startbedingung im Ziel nicht gefunden.", notes);
            }

            bool reverse = DirectionRules.ReverseFlag(r.Reverse, sketch.Sigma);
            bool merge = r.Merge && !(ctx.SeparateBody && !r.IsCut && ctx.OwnBodies().Count == 0);
            int t0 = r.Start == StartCondition.Offset ? (int)StartCondition.Offset : (int)StartCondition.SketchPlane;
            bool flipStart = sketch.Sigma < 0 ? !r.StartOffsetReverse : r.StartOffsetReverse;

            var contours = MapContours(r, sketch, notes);
            Feature? f = Create(ctx, r, sketch, contours, reverse, merge, t1, d1, t2, d2, t0, flipStart);
            if (f == null && contours != null)
            {
                notes.Add("Mit den ausgewählten Konturen nicht erzeugbar, Versuch mit ganzer Skizze und nachträglicher Konturauswahl.");
                f = Create(ctx, r, sketch, null, reverse, merge, t1, d1, t2, d2, t0, flipStart);
                if (f != null && !FeatureEdit.Extrude(doc, f, d => d.Contours = contours.ToArray()))
                    notes.Add("Konturauswahl konnte nicht übernommen werden; es wird die ganze Skizze verwendet.");
            }
            if (f == null) return Fail("SOLIDWORKS hat das Feature nicht erzeugt.", notes);

            // References and start condition, now that the feature exists.
            if (end1 != null || end2 != null || startEntity != null)
            {
                bool ok = FeatureEdit.Extrude(doc, f, d =>
                {
                    if (end1 != null) SetEnd(d, true, r.Side1, end1);
                    if (end2 != null) SetEnd(d, false, r.Side2, end2);
                    if (startEntity != null)
                    {
                        d.FromType = (int)r.Start;
                        d.SetFromEntity(startEntity);
                    }
                });
                if (!ok) return Fail("Bezüge der End-/Startbedingung konnten nicht gesetzt werden.", notes, f);
            }

            ApplyScope(ctx, f, r, notes);
            ctx.Register(r.Name, f);

            var result = BuildResult.Ok(f, Describe(r, reverse != r.Reverse));
            result.Notes.AddRange(notes);
            if (notes.Count > 0) result.Status = ReportStatus.Warning;
            AddAlternatives(result, ctx, f, r);
            return result;
        }

        private static Feature? Create(BuildContext ctx, ExtrudeRecord r, SketchMap sketch, List<object>? contours,
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

            bool sd = !r.BothDirections;
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
        /// End condition for creation. Conditions with a reference are created blind and set
        /// later; if the reference cannot be found but it is a plane parallel to the sketch,
        /// the condition is converted to the equivalent blind depth.
        /// </summary>
        private static object? ResolveEnd(ExtrudeSide side, BuildContext ctx, SketchMap sketch, List<string> notes, out EndCondition cond, out double depth)
        {
            cond = side.End;
            depth = side.Depth;
            if (!ExtrudeSide.NeedsReference(side.End)) return null;

            cond = EndCondition.Blind;
            depth = side.Depth > 0 ? side.Depth : PlaceholderDepth;
            if (side.Reference == null) return null;

            object? target = ctx.Topology.Resolve(side.Reference.Mirror(ctx.Mirror), notes);
            if (target != null) return target;

            double? blind = BlindEquivalent(side, sketch);
            if (blind.HasValue)
            {
                depth = blind.Value;
                notes.Add($"Endbedingung {side.End} ohne Bezug im Ziel, ersetzt durch Blind {depth * 1000:0.###} mm.");
                return null;
            }
            cond = side.End;
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

        private static void SetEnd(ExtrudeFeatureData2 d, bool forward, ExtrudeSide side, object reference)
        {
            d.SetEndCondition(forward, (int)side.End);
            d.SetEndConditionReference(forward, reference);
            if (side.End == EndCondition.OffsetFromSurface)
            {
                d.SetDepth(forward, side.Depth);
                d.SetReverseOffset(forward, side.OffsetReverse);
                d.SetTranslateSurface(forward, side.TranslateSurface);
            }
        }

        /// <summary>Selected contours of the original → contours of the new sketch with the same segments.</summary>
        private static List<object>? MapContours(ExtrudeRecord r, SketchMap sketch, List<string> notes)
        {
            if (r.Contours.Count == 0) return null;
            var available = ((object[])sketch.Sketch.GetSketchContours() ?? Array.Empty<object>()).OfType<SketchContour>()
                .Select(c => (contour: c, ids: new HashSet<string>(((object[])c.GetSketchSegments()).OfType<SketchSegment>().Select(SwDoc.IdOf))))
                .ToList();
            var result = new List<object>();
            foreach (List<string> wanted in r.Contours)
            {
                var newIds = new HashSet<string>(wanted.Where(sketch.Segments.ContainsKey).Select(id => SwDoc.IdOf(sketch.Segments[id])));
                var match = available.FirstOrDefault(a => a.ids.SetEquals(newIds));
                if (match.contour == null)
                {
                    notes.Add("Eine ausgewählte Kontur wurde in der neuen Skizze nicht gefunden; es wird die ganze Skizze verwendet.");
                    return null;
                }
                result.Add(match.contour);
            }
            return result;
        }

        /// <summary>
        /// Same part, separate body: cuts and merging bosses act only on the bodies this job
        /// created, so the mirrored features never touch the original body.
        /// </summary>
        public static void ApplyScope(BuildContext ctx, Feature f, ExtrudeRecord r, List<string> notes)
        {
            if (!ctx.SeparateBody) return;
            List<Body2> own = ctx.OwnBodies();
            if (own.Count == 0 || (!r.IsCut && !r.Merge)) return;
            bool ok = FeatureEdit.Extrude(ctx.Doc, f, d =>
            {
                d.FeatureScope = true;
                d.AutoSelect = false;
                d.FeatureScopeBodies = own.Cast<object>().ToArray();
            });
            if (!ok) notes.Add("Feature-Bereich konnte nicht auf die gespiegelten Körper beschränkt werden.");
        }

        private static void AddAlternatives(BuildResult result, BuildContext ctx, Feature f, ExtrudeRecord r)
        {
            ModelDoc2 doc = ctx.Doc;
            result.Alternatives.Add(new Alternative("Richtung umgekehrt",
                () => FeatureEdit.Extrude(doc, f, d => d.ReverseDirection = !d.ReverseDirection)));
            if (r.IsThin && (r.ThinType == (int)swThinWallType_e.swThinWallOneDirection || r.ThinType == (int)swThinWallType_e.swThinWallOppDirection))
                result.Alternatives.Add(new Alternative("Wandseite gewechselt",
                    () => FeatureEdit.Extrude(doc, f, d => d.ThinWallType = d.ThinWallType == (int)swThinWallType_e.swThinWallOneDirection
                        ? (int)swThinWallType_e.swThinWallOppDirection
                        : (int)swThinWallType_e.swThinWallOneDirection)));
            if (r.IsThin && r.ThinType == (int)swThinWallType_e.swThinWallTwoDirection)
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

        private static BuildResult Fail(string message, List<string> notes, Feature? created = null)
        {
            var result = BuildResult.Failed(message + (created != null ? $" (Feature \"{created.Name}\" bleibt zur Prüfung stehen)" : ""));
            result.Notes.AddRange(notes);
            return result;
        }
    }
}
