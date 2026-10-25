using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;
using static Spiegelwerk.Build.Builders.PatternSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Curve driven pattern of features, faces or bodies.
    ///
    /// The curve is the rebuilt sketch segment or sketch, or the mirrored model edge. The
    /// instances are placed along it; a mirror keeps tangent alignment, curve method and
    /// spacing, but the curve of the copy may run the other way, so the reverse flags are
    /// settled like those of a linear pattern: the placements the original reports, mirrored
    /// (R ∘ T ∘ R), are what the new pattern has to report, and the flags are toggled until
    /// the sets agree.
    ///
    /// Creation: the selection (curve 1 mark 1, direction 2 mark 2, seeds 4/128/256) and
    /// IModelDoc2.FeatureCurvePattern as in the example of the API help – it returns nothing,
    /// the new feature is the last one added. IFeatureManager.CreateDefinition(swFmCurvePattern)
    /// with CreateFeature is the fallback; it is known to fail in some versions. Everything
    /// the creation call takes only as a flag or not at all (curve method, alignment, skipped
    /// instances, face for the normal) is set exactly afterwards in one edit.
    /// </summary>
    internal sealed class CurvePatternBuilder : IFeatureBuilder
    {
        private const int Curve1Mark = 1, Curve2Mark = 2;

        public bool Handles(FeatureRecord record) => record is CurvePatternRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (CurvePatternRecord)record;
            var notes = new List<string>();

            // Nothing to pattern: fail before a direction can add a helper axis.
            if (FindSeeds(ctx, r, new List<string>()) == null) return Fail(NoSeeds(r), notes);

            object? curve1 = Find(ctx, r.Curve1, notes);
            object? curve2 = r.HasDirection2 ? Find(ctx, r.Curve2, notes) : null;
            if (curve1 == null) return Fail("Kurve des Musters im Ziel nicht gefunden.", notes);
            if (r.HasDirection2 && curve2 == null) return Fail("Richtung 2 des Musters im Ziel nicht gefunden.", notes);
            Face2? faceNormal = null;
            if (r.FaceNormal?.Face is FaceSignature normalSig)
            {
                faceNormal = Safe(ctx.Topology.FindFace(normalSig.Mirror(ctx.Mirror), notes)) as Face2;
                if (faceNormal == null) notes.Add("Fläche für die Normalenrichtung im Ziel nicht gefunden; nicht übernommen.");
            }

            // A helper axis may have been added: look the seeds up again.
            Seeds? seeds = FindSeeds(ctx, r, notes);
            if (seeds == null) return Fail(NoSeeds(r), notes);

            // First guess, corrected below by the reported placements.
            Feature? f = CreateBySelection(ctx, r, seeds, curve1, curve2) ?? CreateByDefinition(ctx, r, seeds, curve1, curve2);
            if (f == null) return Fail("SOLIDWORKS hat das Kurvenmuster nicht erzeugt.", notes);

            // ---- exact values
            List<string> differing = Read(ctx.Doc, f, d => Differences(d, r)) ?? new List<string>();
            bool faceSet = faceNormal == null;
            if (differing.Count > 0 || r.SkippedItems.Count > 0 || faceNormal != null)
            {
                bool ok = FeatureEdit.CurvePattern(ctx.Doc, f, d =>
                {
                    Apply(d, r);
                    if (r.SkippedItems.Count > 0) d.SkippedItemArray = r.SkippedItems.ToArray();
                    if (faceNormal != null) faceSet = SwLate.Set(d, "D1FaceNormal", faceNormal);
                });
                // Direction 2 the creation call did not take up from the selection: an edit of
                // its own, so that a refusal can cost direction 2 only, not the values above.
                if (curve2 != null && differing.Contains("Richtung 2"))
                    FeatureEdit.CurvePattern(ctx.Doc, f, d =>
                    {
                        d.Dir2Specified = true;
                        d.D2Direction = curve2;
                        d.D2ReverseDirection = r.Reverse2;
                        Apply(d, r);
                    });
                differing = Read(ctx.Doc, f, d => Differences(d, r)) ?? differing;
                if (!ok && r.SkippedItems.Count > 0)
                    notes.Add($"Die {r.SkippedItems.Count} übersprungenen Instanzen ließen sich nicht übernehmen.");
                if (!ok) faceSet = faceNormal == null;
            }
            if (differing.Count > 0) notes.Add($"Nicht übernommen: {string.Join(", ", differing)}.");
            if (!faceSet) notes.Add("Die Fläche für die Normalenrichtung ließ sich nicht setzen (ältere API-Version?).");

            if (ctx.SeparateBody) LimitToOwnBodies(ctx, f, change => FeatureEdit.CurvePattern(ctx.Doc, f, d => change(d)), notes);

            // Toggles applied on top of each other: (1), (1+2), (2) for two directions.
            var toggles = new List<Func<bool>> { () => ToggleFirst(ctx.Doc, f) };
            if (r.HasDirection2)
            {
                toggles.Add(() => ToggleSecond(ctx.Doc, f));
                toggles.Add(() => ToggleFirst(ctx.Doc, f));
            }
            string directions = MatchPlacements(ctx, r, () => ReadInstances(ctx, f, r), toggles, notes, out bool confirmed);
            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, Describe(r) + directions, notes);
            if (!confirmed)
            {
                result.Alternatives.Add(new Alternative("Richtung 1 umgekehrt", () => ToggleFirst(ctx.Doc, f)));
                if (r.HasDirection2) result.Alternatives.Add(new Alternative("Richtung 2 umgekehrt", () => ToggleSecond(ctx.Doc, f)));
            }
            return result;
        }

        // ---------------------------------------------------------------- creation

        private static Feature? CreateBySelection(BuildContext ctx, CurvePatternRecord r, Seeds seeds, object curve1, object? curve2)
        {
            ModelDoc2 doc = ctx.Doc;
            // The name now: the object of an earlier feature need not stay readable.
            string before = doc.Extension.GetLastFeatureAdded()?.Name ?? "";
            SwSelect.Clear(doc);
            try
            {
                bool selected = SwSelect.Select(doc, curve1, Curve1Mark, false);
                if (curve2 != null) selected &= SwSelect.Select(doc, curve2, Curve2Mark, true);
                selected &= SelectSeeds(doc, seeds);
                if (!selected) return null;

                doc.FeatureCurvePattern(r.Count1, r.Spacing1, r.HasDirection2 ? r.Count2 : 1, r.HasDirection2 ? r.Spacing2 : 0,
                    r.Reverse1, r.Reverse2, r.EqualSpacing1, r.EqualSpacing2, false,
                    r.Alignment == (int)swCurveDrivenPatternAlignment_e.swCurvePatternAlignToSeed,
                    r.CurveMethod == (int)swCurveDrivenPatternCurveMethod_e.swCurvePatternOffsetCurve,
                    r.SeedOnly2);
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
            Feature? after = doc.Extension.GetLastFeatureAdded();
            return after != null && after.Name != before && after.GetDefinition() is CurveDrivenPatternFeatureData ? after : null;
        }

        private static Feature? CreateByDefinition(BuildContext ctx, CurvePatternRecord r, Seeds seeds, object curve1, object? curve2)
        {
            FeatureManager fm = ctx.Doc.FeatureManager;
            SwSelect.Clear(ctx.Doc);
            try
            {
                if (!(fm.CreateDefinition((int)swFeatureNameID_e.swFmCurvePattern) is CurveDrivenPatternFeatureData d)) return null;
                d.D1Direction = curve1;
                d.D1ReverseDirection = r.Reverse1;
                if (curve2 != null)
                {
                    d.Dir2Specified = true;
                    d.D2Direction = curve2;
                    d.D2ReverseDirection = r.Reverse2;
                }
                Apply(d, r);
                SetSeeds(seeds, r, v => d.PatternFeatureArray = v, v => d.PatternFaceArray = v, v => d.PatternBodyArray = v, v => d.BodyPattern = v);
                return fm.CreateFeature(d);
            }
            catch (Exception)
            {
                // Not supported in this version, or a property was refused.
                return null;
            }
        }

        // ---------------------------------------------------------------- values

        /// <summary>Counts, spacings and options that differ from the original (not the reverse flags – the placements decide those).</summary>
        private static List<string> Differences(CurveDrivenPatternFeatureData d, CurvePatternRecord r)
        {
            var result = new List<string>();
            if (d.D1InstanceCount != r.Count1) result.Add("Anzahl");
            if (d.D1IsEqualSpaced != r.EqualSpacing1) result.Add("gleicher Abstand");
            if (!Same(d.D1Spacing, r.Spacing1)) result.Add("Abstand");
            if (d.D1CurveMethod != r.CurveMethod) result.Add("Kurvenmethode");
            if (d.D1AlignmentMethod != r.Alignment) result.Add("Ausrichtung");
            if (d.GeometryPattern != r.GeometryPattern) result.Add("Geometriemuster");
            if (d.VarySketch != r.VarySketch) result.Add("Skizze variieren");
            if (d.Dir2Specified != r.HasDirection2) result.Add("Richtung 2");
            else if (r.HasDirection2)
            {
                if (d.D2InstanceCount != r.Count2) result.Add("Anzahl in Richtung 2");
                if (d.D2IsEqualSpaced != r.EqualSpacing2) result.Add("gleicher Abstand in Richtung 2");
                if (!Same(d.D2Spacing, r.Spacing2)) result.Add("Abstand in Richtung 2");
                if (d.D2PatternSeedOnly != r.SeedOnly2) result.Add("nur Saat in Richtung 2");
            }
            return result;
        }

        /// <summary>Writes the values of <see cref="Differences"/> that differ; one refused value does not stop the others.</summary>
        private static void Apply(CurveDrivenPatternFeatureData d, CurvePatternRecord r)
        {
            Try(() => { if (d.D1InstanceCount != r.Count1) d.D1InstanceCount = r.Count1; });
            Try(() => { if (d.D1IsEqualSpaced != r.EqualSpacing1) d.D1IsEqualSpaced = r.EqualSpacing1; });
            Try(() => { if (!Same(d.D1Spacing, r.Spacing1)) d.D1Spacing = r.Spacing1; });
            Try(() => { if (d.D1CurveMethod != r.CurveMethod) d.D1CurveMethod = r.CurveMethod; });
            Try(() => { if (d.D1AlignmentMethod != r.Alignment) d.D1AlignmentMethod = r.Alignment; });
            Try(() => { if (d.GeometryPattern != r.GeometryPattern) d.GeometryPattern = r.GeometryPattern; });
            Try(() => { if (d.VarySketch != r.VarySketch) d.VarySketch = r.VarySketch; });
            if (!r.HasDirection2) return;
            Try(() => { if (d.D2InstanceCount != r.Count2) d.D2InstanceCount = r.Count2; });
            Try(() => { if (d.D2IsEqualSpaced != r.EqualSpacing2) d.D2IsEqualSpaced = r.EqualSpacing2; });
            Try(() => { if (!Same(d.D2Spacing, r.Spacing2)) d.D2Spacing = r.Spacing2; });
            Try(() => { if (d.D2PatternSeedOnly != r.SeedOnly2) d.D2PatternSeedOnly = r.SeedOnly2; });
        }

        private static bool Same(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-12, 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b)));

        private static void Try(Action set)
        {
            try
            {
                set();
            }
            catch (Exception)
            {
                // Refused; Differences reports it.
            }
        }

        /// <summary>A value read under selection access; null if the data cannot be read.</summary>
        private static T? Read<T>(ModelDoc2 doc, Feature f, Func<CurveDrivenPatternFeatureData, T> read) where T : class
        {
            try
            {
                var d = (CurveDrivenPatternFeatureData)f.GetDefinition();
                if (!d.AccessSelections(doc, null)) return null;
                try
                {
                    return read(d);
                }
                finally
                {
                    d.ReleaseSelectionAccess();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- direction flags

        private static bool ToggleFirst(ModelDoc2 doc, Feature f) =>
            FeatureEdit.CurvePattern(doc, f, d => d.D1ReverseDirection = !d.D1ReverseDirection);

        private static bool ToggleSecond(ModelDoc2 doc, Feature f) =>
            FeatureEdit.CurvePattern(doc, f, d => d.D2ReverseDirection = !d.D2ReverseDirection);

        /// <summary>Placements of the new pattern, read under selection access like those of the original.</summary>
        private static List<Affine>? ReadInstances(BuildContext ctx, Feature f, CurvePatternRecord r) =>
            Read(ctx.Doc, f, d => SwPattern.Instances(ctx.Math, r.InstanceCount, d.GetTransform));

        private static string Describe(CurvePatternRecord r) =>
            $"Kurvenmuster, {r.Count1} Instanzen" + (r.EqualSpacing1 ? " gleichmäßig verteilt" : $" × {r.Spacing1 * 1000:0.###} mm") +
            (r.HasDirection2 ? $" und {r.Count2} in Richtung 2" : "") + $", {SeedText(r)}";
    }
}
