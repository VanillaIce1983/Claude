using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;
using static Spiegelwerk.Build.Builders.PatternSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Sketch driven pattern of features, faces or bodies: one instance at each point of the
    /// reference sketch, placed by moving the reference point (the seed's centroid or a chosen
    /// point) there. Only translations – nothing to flip: the reference sketch and the
    /// reference point are rebuilt mirrored, and the pattern follows them. The placements the
    /// original reports, mirrored, are compared with the new ones as a check.
    ///
    /// Creation: the selection (sketch mark 64, reference point 32, seeds 4/128/256) and
    /// IFeatureManager.FeatureSketchDrivenPattern; IFeatureManager.CreateDefinition
    /// (swFmSketchPattern) with CreateFeature is the fallback.
    /// </summary>
    internal sealed class SketchPatternBuilder : IFeatureBuilder
    {
        private const int PointMark = 32, SketchMark = 64;

        public bool Handles(FeatureRecord record) => record is SketchPatternRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (SketchPatternRecord)record;
            var notes = new List<string>();

            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap sketch))
                return Fail($"Die Referenzskizze „{r.SketchName}“ wurde nicht gespiegelt.", notes);
            if (FindSeeds(ctx, r, new List<string>()) == null) return Fail(NoSeeds(r), notes);
            object? point = null;
            if (!r.UseCentroid)
            {
                point = Find(ctx, r.ReferencePoint, notes);
                if (point == null) return Fail("Referenzpunkt des Musters im Ziel nicht gefunden.", notes);
            }
            Seeds? seeds = FindSeeds(ctx, r, notes);
            if (seeds == null) return Fail(NoSeeds(r), notes);

            Feature? f = CreateBySelection(ctx, r, seeds, sketch, point) ?? CreateByDefinition(ctx, r, seeds, sketch, point);
            if (f == null) return Fail("SOLIDWORKS hat das Skizzenmuster nicht erzeugt.", notes);

            // The two options the creation call takes as flags, checked once more.
            if (OptionsDiffer(ctx.Doc, f, r) &&
                (!FeatureEdit.SketchPattern(ctx.Doc, f, d =>
                {
                    if (d.UseCentroid != r.UseCentroid) d.UseCentroid = r.UseCentroid;
                    if (d.GeometryPattern != r.GeometryPattern) d.GeometryPattern = r.GeometryPattern;
                }) || OptionsDiffer(ctx.Doc, f, r)))
                notes.Add("Schwerpunkt- bzw. Geometriemuster-Option weicht ab und ließ sich nicht übernehmen.");

            if (ctx.SeparateBody) LimitToOwnBodies(ctx, f, change => FeatureEdit.SketchPattern(ctx.Doc, f, d => change(d)), notes);

            string placements = MatchPlacements(ctx, r, () => ReadInstances(ctx, f, r), Array.Empty<Func<bool>>(), notes, out bool _);
            ctx.Register(r.Name, f);
            return Ok(f, $"Skizzenmuster über „{r.SketchName}“, " + (r.UseCentroid ? "Schwerpunkt" : "gewählter Referenzpunkt") +
                         $", {SeedText(r)}" + placements, notes);
        }

        private static Feature? CreateBySelection(BuildContext ctx, SketchPatternRecord r, Seeds seeds, SketchMap sketch, object? point)
        {
            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            try
            {
                bool selected = SwSelect.Select(doc, sketch.Feature, SketchMark, false);
                if (point != null) selected &= SwSelect.Select(doc, point, PointMark, true);
                selected &= SelectSeeds(doc, seeds);
                return selected ? doc.FeatureManager.FeatureSketchDrivenPattern(r.UseCentroid, r.GeometryPattern) : null;
            }
            catch (Exception)
            {
                // Refused: the data object follows.
                return null;
            }
            finally
            {
                SwSelect.Clear(doc);
            }
        }

        private static Feature? CreateByDefinition(BuildContext ctx, SketchPatternRecord r, Seeds seeds, SketchMap sketch, object? point)
        {
            FeatureManager fm = ctx.Doc.FeatureManager;
            SwSelect.Clear(ctx.Doc);
            try
            {
                if (!(fm.CreateDefinition((int)swFeatureNameID_e.swFmSketchPattern) is SketchPatternFeatureData d)) return null;
                d.Sketch = sketch.Sketch;
                d.UseCentroid = r.UseCentroid;
                if (point != null) d.ReferencePoint = point;
                SetSeeds(seeds, r, v => d.PatternFeatureArray = v, v => d.PatternFaceArray = v, v => d.PatternBodyArray = v, v => d.BodyPattern = v);
                d.GeometryPattern = r.GeometryPattern;
                return fm.CreateFeature(d);
            }
            catch (Exception)
            {
                // Not supported in this version, or a property was refused.
                return null;
            }
        }

        private static bool OptionsDiffer(ModelDoc2 doc, Feature f, SketchPatternRecord r)
        {
            try
            {
                var d = (SketchPatternFeatureData)f.GetDefinition();
                if (!d.AccessSelections(doc, null)) return false;
                try
                {
                    return d.UseCentroid != r.UseCentroid || d.GeometryPattern != r.GeometryPattern;
                }
                finally
                {
                    d.ReleaseSelectionAccess();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Placements of the new pattern, read under selection access like those of the original.</summary>
        private static List<Affine>? ReadInstances(BuildContext ctx, Feature f, SketchPatternRecord r)
        {
            try
            {
                var d = (SketchPatternFeatureData)f.GetDefinition();
                if (!d.AccessSelections(ctx.Doc, null)) return null;
                try
                {
                    return SwPattern.Instances(ctx.Math, r.SketchPointCount + 1, d.GetTransform);
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
