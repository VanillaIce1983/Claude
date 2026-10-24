using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;

namespace Spiegelwerk.Core.Sketch
{
    public sealed class SketchMirrorResult
    {
        public SketchMirrorResult(SketchModel sketch, AxisMap map)
        {
            Sketch = sketch;
            Map = map;
        }

        /// <summary>The mirrored sketch, in coordinates of the target sketch.</summary>
        public SketchModel Sketch { get; }

        public AxisMap Map { get; }

        /// <summary>Relations and dimensions that could not be carried over, with the reason.</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Mirrors a sketch into a target sketch whose frame is already known (SOLIDWORKS decides
    /// the in-plane axes of a sketch only once it is opened on a plane).
    ///
    /// Geometry: every point goes sketch → model → mirror → target sketch. Nothing is
    /// "negated" by hand, so the same code works for any plane combination.
    /// Relations: invariant under isometries except horizontal/vertical, which follow the axis map.
    /// Dimensions: lengths, radii and angles keep their value; horizontal/vertical swap or are
    /// dropped like the relations; text positions are mirrored so angular dimensions measure
    /// the same angle and not its supplement.
    /// </summary>
    public static class SketchMirror
    {
        public static SketchMirrorResult Mirror(SketchModel source, Reflection r, Frame3 target)
        {
            Plane3 expected = r.Apply(source.Frame.Plane);
            if (!expected.IsCoplanarWith(target.Plane, 1e-6))
                throw new ArgumentException($"Zielskizze liegt nicht in der gespiegelten Skizzenebene ({expected}).");

            AxisMap map = AxisMap.Compute(source.Frame, r, target);
            var result = new SketchMirrorResult(new SketchModel { Name = source.Name, Frame = target }, map);
            SketchModel dst = result.Sketch;

            foreach (SkPoint p in source.Points)
            {
                Vec3 local = target.ToLocal(r.Apply(source.ToModel(p)));
                dst.Points.Add(new SkPoint(p.Id, local.X, local.Y) { Standalone = p.Standalone });
            }

            foreach (SkSegment s in source.Segments)
            {
                var m = new SkSegment(s.Id, s.Kind)
                {
                    Construction = s.Construction,
                    StartId = s.StartId,
                    EndId = s.EndId,
                    CenterId = s.CenterId,
                    MajorId = s.MajorId,
                    MinorId = s.MinorId,
                    Radius = s.Radius,
                    Direction = s.Kind == SkSegmentKind.Arc || s.Kind == SkSegmentKind.EllipticalArc
                        ? DirectionRules.ArcDirection(s.Direction, map.Sigma)
                        : s.Direction,
                };
                m.FitPointIds.AddRange(s.FitPointIds);
                dst.Segments.Add(m);
            }

            foreach (SkRelation rel in source.Relations)
            {
                RelationKind? kind = ConvertRelation(rel.Kind, map);
                if (kind == null)
                {
                    result.Warnings.Add($"Beziehung {rel.Kind} entfällt: die Skizzenachsen liegen nach dem Spiegeln schräg ({map}).");
                    continue;
                }
                dst.Relations.Add(new SkRelation(kind.Value, rel.Entities.Select(e => e.Mirror(r))));
            }

            foreach (SkDimension d in source.Dimensions)
            {
                DimensionKind? kind = ConvertDimension(d.Kind, map);
                if (kind == null)
                {
                    result.Warnings.Add($"Bemaßung {d.Name} ({d.Kind}) entfällt: die Skizzenachsen liegen nach dem Spiegeln schräg ({map}).");
                    continue;
                }
                dst.Dimensions.Add(new SkDimension(kind.Value, d.Refs.Select(e => e.Mirror(r)), d.Value)
                {
                    Name = d.Name,
                    Driven = d.Driven,
                    TextPosition = d.TextPosition.HasValue ? r.Apply(d.TextPosition.Value) : (Vec3?)null,
                });
            }

            dst.Warnings.AddRange(source.Warnings);
            return result;
        }

        /// <summary>Horizontal/vertical follow the axes; every other relation is invariant under mirroring.</summary>
        public static RelationKind? ConvertRelation(RelationKind kind, AxisMap map)
        {
            switch (kind)
            {
                case RelationKind.Horizontal:
                    return Along(map.X, RelationKind.Horizontal, RelationKind.Vertical);
                case RelationKind.Vertical:
                    return Along(map.Y, RelationKind.Horizontal, RelationKind.Vertical);
                case RelationKind.HorizontalPoints:
                    return Along(map.X, RelationKind.HorizontalPoints, RelationKind.VerticalPoints);
                case RelationKind.VerticalPoints:
                    return Along(map.Y, RelationKind.HorizontalPoints, RelationKind.VerticalPoints);
                default:
                    return kind;
            }
        }

        public static DimensionKind? ConvertDimension(DimensionKind kind, AxisMap map)
        {
            switch (kind)
            {
                case DimensionKind.Horizontal:
                    return Along(map.X, DimensionKind.Horizontal, DimensionKind.Vertical);
                case DimensionKind.Vertical:
                    return Along(map.Y, DimensionKind.Horizontal, DimensionKind.Vertical);
                default:
                    return kind;
            }
        }

        private static T? Along<T>(AxisImage image, T onX, T onY) where T : struct
        {
            if (AxisMap.LandsOnX(image)) return onX;
            if (AxisMap.LandsOnY(image)) return onY;
            return null;
        }

        /// <summary>
        /// Checks that the mirrored sketch is congruent to the source: all pairwise point distances
        /// are preserved. Used by tests and as a sanity check before writing.
        /// </summary>
        public static double MaxDistortion(SketchModel source, SketchModel mirrored)
        {
            var a = source.Points.ToDictionary(p => p.Id);
            var b = mirrored.Points.ToDictionary(p => p.Id);
            double worst = 0;
            List<string> ids = a.Keys.ToList();
            for (int i = 0; i < ids.Count; i++)
                for (int j = i + 1; j < ids.Count; j++)
                {
                    double da = a[ids[i]].Distance2D(a[ids[j]]);
                    double db = b[ids[i]].Distance2D(b[ids[j]]);
                    worst = Math.Max(worst, Math.Abs(da - db));
                }
            return worst;
        }
    }
}
