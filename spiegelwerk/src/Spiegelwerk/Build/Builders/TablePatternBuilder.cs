using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;
using static Spiegelwerk.Build.Builders.PatternSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Table driven pattern of features, faces or bodies: one instance at each point of a
    /// table, given in the XY plane of a coordinate system, placed by moving the reference
    /// point (the seed's centroid or a chosen point) there.
    ///
    /// The coordinate system of the copy is the rebuilt one of the original if this job made
    /// it, otherwise a helper at the same place: X' = R(x), Y' = R(y), Z' = X' × Y'. In it the
    /// table keeps X and Y; only a Z value changes sign (TablePoints). Only translations –
    /// nothing to flip; the mirrored placements of the original serve as the check.
    ///
    /// Creation: the feature data object (CreateDefinition(swFmTablePattern) with coordinate
    /// system, PointArray, reference point and seeds, CreateFeature) – its properties are
    /// documented; the selection with InsertTableDrivenPattern2 is the fallback (the help
    /// example uses a table file; marks and point format are not documented). The points are
    /// compared afterwards and set again if they differ.
    /// </summary>
    internal sealed class TablePatternBuilder : IFeatureBuilder
    {
        private const int CoordinateSystemMark = 0, PointMark = 32;

        public bool Handles(FeatureRecord record) => record is TablePatternRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (TablePatternRecord)record;
            var notes = new List<string>();

            // Everything that can refuse the pattern before a helper coordinate system is added:
            // a refused pattern leaves nothing behind.
            if (FindSeeds(ctx, r, new List<string>()) == null) return Fail(NoSeeds(r), notes);
            // R(0) = 0 only if the mirror plane passes through the origin.
            if (r.ReferenceIsOrigin && !ctx.Mirror.Apply(Vec3.Zero).AlmostEquals(Vec3.Zero))
                return Fail("Referenzpunkt ist der Ursprung; die Spiegelebene geht nicht durch ihn, gespiegelt liegt er nicht im Ursprung des Ziels.", notes);
            if (!r.ReferenceIsOrigin && !r.UseCentroid && Find(ctx, r.ReferencePoint, new List<string>()) == null)
                return Fail("Referenzpunkt des Musters im Ziel nicht gefunden.", notes);

            Frame3 frame = ctx.Mirror.ApplyKeepingXY(r.CoordinateSystem);
            Feature cs;
            bool helper, created;
            try
            {
                cs = CoordinateSystem(ctx, r, frame, notes, out helper, out created);
            }
            catch (InvalidOperationException ex)
            {
                return Fail($"Koordinatensystem des Musters ließ sich im Ziel nicht anlegen ({ex.Message}).", notes);
            }

            Feature? f = Create(ctx, r, cs, notes, out string? refused);
            if (f == null)
            {
                // A helper made just for this pattern goes with it.
                if (created) ctx.Planes.RemoveCoordinateSystem(cs);
                return Fail(refused ?? "SOLIDWORKS hat das Tabellenmuster nicht erzeugt.", notes);
            }
            BuildResult result = Finish(ctx, r, f, cs, helper, notes);
            // Removed again by the job (it reached the original): the helper made for it goes too.
            if (created) result.RemoveHelpers = () => ctx.Planes.RemoveCoordinateSystem(cs);
            return result;
        }

        /// <summary>Reference point and seeds looked up again (a helper coordinate system rebuilt the model), then the pattern.</summary>
        private static Feature? Create(BuildContext ctx, TablePatternRecord r, Feature cs, List<string> notes, out string? refused)
        {
            refused = null;
            object? point = null;
            if (r.ReferenceIsOrigin)
            {
                point = SwDoc.OriginPoint(ctx.Doc);
            }
            else if (!r.UseCentroid)
            {
                point = Find(ctx, r.ReferencePoint, notes);
                if (point == null)
                {
                    refused = "Referenzpunkt des Musters im Ziel nicht gefunden.";
                    return null;
                }
            }
            Seeds? seeds = FindSeeds(ctx, r, notes);
            if (seeds == null)
            {
                refused = NoSeeds(r);
                return null;
            }

            List<Vec3> points = TablePoints.Mirror(r.Points);
            Feature? f = CreateByDefinition(ctx, r, seeds, cs, points, point) ?? CreateBySelection(ctx, r, seeds, cs, points, point);
            if (f != null) SetExactValues(ctx, r, f, points, point, notes);
            return f;
        }

        private static BuildResult Finish(BuildContext ctx, TablePatternRecord r, Feature f, Feature cs, bool helper, List<string> notes)
        {
            string placements = MatchPlacements(ctx, r, () => ReadInstances(ctx, f, r), Array.Empty<Func<bool>>(), notes, out bool _);
            ctx.Register(r.Name, f);
            // A helper is no deviation (like helper planes and axes): named in the message, not as a note.
            string system = helper ? $"Hilfs-Koordinatensystem „{cs.Name}“ (an gespiegelter Lage von „{r.CoordinateSystemName}“)" : $"„{cs.Name}“";
            return Ok(f, $"Tabellenmuster, {r.Points.Count} Punkte in {system}, " + (r.UseCentroid ? "Schwerpunkt" : "gewählter Referenzpunkt") +
                         $", {SeedText(r)}" + placements, notes);
        }

        /// <summary>Points, skipped instances and options, compared and set again where they differ.</summary>
        private static void SetExactValues(BuildContext ctx, TablePatternRecord r, Feature f, List<Vec3> points, object? point, List<string> notes)
        {

            if (NeedsEdit(ctx.Doc, f, r, points))
            {
                bool ok = FeatureEdit.TablePattern(ctx.Doc, f, d =>
                {
                    if (!TablePoints.Same(TablePoints.FromArray(Doubles(d.PointArray)), points)) d.PointArray = TablePoints.ToArray(points);
                    if (r.SkippedItems.Count > 0) d.SkippedItemArray = r.SkippedItems.ToArray();
                    if (d.UseCentroid != r.UseCentroid)
                    {
                        d.UseCentroid = r.UseCentroid;
                        // Without the centroid the pattern needs its point.
                        if (point != null) d.ReferencePoint = point;
                    }
                    if (d.GeometryPattern != r.GeometryPattern) d.GeometryPattern = r.GeometryPattern;
                    if (d.PropagateVisualProperty != r.PropagateVisualProperty) d.PropagateVisualProperty = r.PropagateVisualProperty;
                });
                if (!ok && r.SkippedItems.Count > 0)
                    notes.Add($"Die {r.SkippedItems.Count} übersprungenen Instanzen ließen sich nicht übernehmen.");
                if (NeedsEdit(ctx.Doc, f, r, points, skippedToo: false))
                    notes.Add("Punkte bzw. Optionen des Tabellenmusters weichen ab und ließen sich nicht übernehmen.");
            }
        }

        /// <summary>
        /// The rebuilt coordinate system of the original if this job made it and it lies right,
        /// otherwise one of this job at the mirrored place, otherwise a new helper.
        /// <paramref name="helper"/>: not the rebuilt one; <paramref name="created"/>: made just now.
        /// </summary>
        private static Feature CoordinateSystem(BuildContext ctx, TablePatternRecord r, Frame3 frame, List<string> notes, out bool helper, out bool created)
        {
            helper = false;
            created = false;
            if (ctx.Created.TryGetValue(r.CoordinateSystemName, out Feature built))
            {
                Frame3? actual = SwDoc.CoordinateSystemFrame(ctx.Doc, ctx.Math, built.Name);
                if (actual.HasValue && PlaneResolver.SameFrame(actual.Value, frame)) return built;
                notes.Add($"Das gespiegelte Koordinatensystem „{built.Name}“ liegt nicht wie erwartet; das Muster bekommt ein eigenes.");
            }
            helper = true;
            return ctx.Planes.CoordinateSystem(frame, out created);
        }

        private static Feature? CreateByDefinition(BuildContext ctx, TablePatternRecord r, Seeds seeds, Feature cs, List<Vec3> points, object? point)
        {
            FeatureManager fm = ctx.Doc.FeatureManager;
            SwSelect.Clear(ctx.Doc);
            try
            {
                if (!(fm.CreateDefinition((int)swFeatureNameID_e.swFmTablePattern) is TablePatternFeatureData d)) return null;
                d.CoordinateSystem = cs;
                d.PointArray = TablePoints.ToArray(points);
                d.UseCentroid = r.UseCentroid;
                if (point != null) d.ReferencePoint = point;
                // No BodyPattern flag on this data object: the seed bodies say it.
                SetSeeds(seeds, r, v => d.PatternFeatureArray = v, v => d.PatternFaceArray = v, v => d.PatternBodyArray = v, _ => { });
                d.GeometryPattern = r.GeometryPattern;
                d.PropagateVisualProperty = r.PropagateVisualProperty;
                return fm.CreateFeature(d);
            }
            catch (Exception)
            {
                // Not supported in this version, or a property was refused.
                return null;
            }
        }

        private static Feature? CreateBySelection(BuildContext ctx, TablePatternRecord r, Seeds seeds, Feature cs, List<Vec3> points, object? point)
        {
            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            try
            {
                bool selected = SwSelect.Select(doc, cs, CoordinateSystemMark, false);
                if (point != null) selected &= SwSelect.Select(doc, point, PointMark, true);
                selected &= SelectSeeds(doc, seeds);
                return selected
                    ? doc.FeatureManager.InsertTableDrivenPattern2("", TablePoints.ToArray(points), r.UseCentroid, r.GeometryPattern, r.PropagateVisualProperty)
                    : null;
            }
            catch (Exception)
            {
                // Refused.
                return null;
            }
            finally
            {
                SwSelect.Clear(doc);
            }
        }

        /// <summary>Whether points, options (or, with <paramref name="skippedToo"/>, skipped instances) still need setting.</summary>
        private static bool NeedsEdit(ModelDoc2 doc, Feature f, TablePatternRecord r, List<Vec3> points, bool skippedToo = true)
        {
            try
            {
                var d = (TablePatternFeatureData)f.GetDefinition();
                if (!d.AccessSelections(doc, null)) return skippedToo && r.SkippedItems.Count > 0;
                try
                {
                    return !TablePoints.Same(TablePoints.FromArray(Doubles(d.PointArray)), points) ||
                           d.UseCentroid != r.UseCentroid || d.GeometryPattern != r.GeometryPattern ||
                           d.PropagateVisualProperty != r.PropagateVisualProperty ||
                           (skippedToo && r.SkippedItems.Count > 0);
                }
                finally
                {
                    d.ReleaseSelectionAccess();
                }
            }
            catch (Exception)
            {
                return skippedToo && r.SkippedItems.Count > 0;
            }
        }

        private static double[] Doubles(object o) => o switch
        {
            double[] ds => ds,
            object[] os => os.Select(Convert.ToDouble).ToArray(),
            _ => Array.Empty<double>(),
        };

        /// <summary>Placements of the new pattern, read under selection access like those of the original.</summary>
        private static List<Affine>? ReadInstances(BuildContext ctx, Feature f, TablePatternRecord r)
        {
            try
            {
                var d = (TablePatternFeatureData)f.GetDefinition();
                if (!d.AccessSelections(ctx.Doc, null)) return null;
                try
                {
                    return SwPattern.Instances(ctx.Math, r.Points.Count + 1, d.GetTransform);
                }
                finally
                {
                    d.ReleaseSelectionAccess();
                }
            }
            catch (Exception)
            {
                // Placements not readable: the mass check has to decide.
                return null;
            }
        }
    }
}
