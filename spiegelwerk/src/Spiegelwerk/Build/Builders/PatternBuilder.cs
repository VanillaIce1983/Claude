using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Linear and circular pattern of features, faces or bodies.
    ///
    /// Seeds: the rebuilt counterparts of the seed features, the mirrored seed faces, the
    /// bodies with the mirrored volume and centroid. Directions and axes are found like any
    /// other reference (a helper axis if nothing lies on the mirrored line).
    ///
    /// Direction flags: the pattern reports where it puts each instance (GetTransform). The
    /// original's placements, mirrored (R ∘ T ∘ R), are what the new pattern has to report;
    /// the flag combinations are tried until the sets agree. So the sense of rotation of a
    /// circular pattern – which the mirror reverses – and the direction along an edge whose
    /// orientation in the new part is arbitrary are measured, not guessed.
    ///
    /// Creation: selection plus FeatureLinearPattern5/FeatureCircularPattern5 (direction 1
    /// mark 1, direction 2 mark 2, features 4, faces 128, bodies 256), or the feature data
    /// object with IFeatureManager.CreateDefinition/CreateFeature, which newer versions
    /// recommend and which needs no marks; the other one is the fallback.
    /// </summary>
    internal sealed class PatternBuilder : IFeatureBuilder
    {
        private const int Direction1Mark = 1, Direction2Mark = 2, FeatureMark = 4, FaceMark = 128, BodyMark = 256;

        public bool Handles(FeatureRecord record) => record is LinearPatternRecord || record is CircularPatternRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (PatternRecord)record;
            var notes = new List<string>();

            Seeds? seeds = FindSeeds(ctx, r, notes);
            if (seeds == null) return Fail("Keines der Saat-Elemente des Musters wurde im Ziel gefunden.", notes);

            object? dir1, dir2 = null;
            if (r is LinearPatternRecord lin)
            {
                dir1 = Find(ctx, lin.Direction1, notes);
                if (lin.HasDirection2) dir2 = Find(ctx, lin.Direction2, notes);
                if (dir1 == null || (lin.HasDirection2 && dir2 == null)) return Fail("Musterrichtung im Ziel nicht gefunden.", notes);
            }
            else
            {
                dir1 = Find(ctx, ((CircularPatternRecord)r).Axis, notes);
                if (dir1 == null) return Fail("Musterachse im Ziel nicht gefunden.", notes);
            }

            // First guess, corrected below by the reported placements: a linear pattern keeps
            // its flag, a circular one turns the other way.
            bool rev1 = r is LinearPatternRecord l1 ? l1.Reverse1 : !((CircularPatternRecord)r).Reverse;
            bool rev2 = r is LinearPatternRecord l2 && l2.Reverse2;

            // Features only: the documented selection marks first. Faces and bodies: the data object first.
            Func<Feature?>[] attempts = seeds.Faces.Count == 0 && seeds.Bodies.Count == 0
                ? new Func<Feature?>[] { () => CreateBySelection(ctx, r, seeds, dir1, dir2, rev1, rev2), () => CreateByDefinition(ctx, r, seeds, dir1, dir2, rev1, rev2) }
                : new Func<Feature?>[] { () => CreateByDefinition(ctx, r, seeds, dir1, dir2, rev1, rev2), () => CreateBySelection(ctx, r, seeds, dir1, dir2, rev1, rev2) };
            Feature? f = null;
            foreach (Func<Feature?> attempt in attempts)
            {
                f = attempt();
                if (f != null) break;
            }
            if (f == null) return Fail("SOLIDWORKS hat das Muster nicht erzeugt.", notes);

            if (r.SkippedItems.Count > 0 && !Edit(ctx.Doc, f, lp => lp.SkippedItemArray = r.SkippedItems.ToArray(), cp => cp.SkippedItemArray = r.SkippedItems.ToArray()))
                notes.Add($"Die {r.SkippedItems.Count} übersprungenen Instanzen ließen sich nicht übernehmen.");

            string directions = MatchPlacements(ctx, f, r, notes);
            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, Describe(r) + directions, notes);
            AddAlternatives(result, ctx.Doc, f, r);
            return result;
        }

        // ---------------------------------------------------------------- seeds

        private sealed class Seeds
        {
            public List<Feature> Features { get; } = new List<Feature>();
            public List<Face2> Faces { get; } = new List<Face2>();
            public List<Body2> Bodies { get; } = new List<Body2>();
        }

        private static Seeds? FindSeeds(BuildContext ctx, PatternRecord r, List<string> notes)
        {
            var seeds = new Seeds();
            var missing = new List<string>();
            foreach (string name in r.SeedFeatures)
            {
                if (ctx.Created.TryGetValue(name, out Feature f)) seeds.Features.Add(f);
                else missing.Add($"\"{name}\"");
            }
            foreach (var face in r.SeedFaces)
            {
                Face2? found = ctx.Topology.FindFace(face.Mirror(ctx.Mirror), notes);
                if (found != null) seeds.Faces.Add(found);
                else missing.Add("eine Fläche");
            }
            foreach (MassState body in r.SeedBodies)
            {
                Body2? found = FindBody(ctx, body.Mirror(ctx.Mirror));
                if (found != null) seeds.Bodies.Add(found);
                else missing.Add("ein Körper");
            }
            if (missing.Count > 0)
                notes.Add($"Nicht gespiegelt und daher nicht gemustert: {string.Join(", ", missing)}.");
            return seeds.Features.Count + seeds.Faces.Count + seeds.Bodies.Count == 0 ? null : seeds;
        }

        /// <summary>The body with the given volume and centroid; bodies of this job first.</summary>
        private static Body2? FindBody(BuildContext ctx, MassState wanted)
        {
            Body2? fallback = null;
            foreach (Body2 b in SwDoc.SolidBodies(ctx.Doc))
            {
                if (!MassComparison.Compare(wanted, SwDoc.MassOf(new[] { b })).Passes) continue;
                if (ctx.IsOwnBody(b)) return b;
                if (!ctx.SamePart) fallback ??= b;
            }
            return fallback;
        }

        // ---------------------------------------------------------------- creation

        private static Feature? CreateBySelection(BuildContext ctx, PatternRecord r, Seeds seeds, object dir1, object? dir2, bool rev1, bool rev2)
        {
            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            bool selected = SwSelect.Select(doc, dir1, Direction1Mark, false);
            if (dir2 != null) selected &= SwSelect.Select(doc, dir2, Direction2Mark, true);
            foreach (Feature f in seeds.Features) selected &= SwSelect.Select(doc, f, FeatureMark, true);
            foreach (Face2 face in seeds.Faces) selected &= SwSelect.Select(doc, face, FaceMark, true);
            foreach (Body2 body in seeds.Bodies) selected &= SwSelect.Select(doc, body, BodyMark, true);
            if (!selected)
            {
                SwSelect.Clear(doc);
                return null;
            }

            FeatureManager fm = doc.FeatureManager;
            Feature? created;
            if (r is LinearPatternRecord l)
            {
                created = fm.FeatureLinearPattern5(l.Count1, l.Spacing1, l.HasDirection2 ? l.Count2 : 1, l.HasDirection2 ? l.Spacing2 : 0,
                    rev1, rev2, "NULL", "NULL", l.GeometryPattern, l.VarySketch,
                    false, false, false, false, false, false, false, false, 0, 0, l.SeedOnly2, false);
            }
            else
            {
                var c = (CircularPatternRecord)r;
                created = fm.FeatureCircularPattern5(c.Count, c.Spacing, rev1, "NULL", c.GeometryPattern, c.EqualSpacing, c.VarySketch, false,
                    c.Direction2, c.Symmetric, c.Count2, c.Spacing2, "NULL", c.EqualSpacing2);
            }
            SwSelect.Clear(doc);
            return created;
        }

        private static Feature? CreateByDefinition(BuildContext ctx, PatternRecord r, Seeds seeds, object dir1, object? dir2, bool rev1, bool rev2)
        {
            FeatureManager fm = ctx.Doc.FeatureManager;
            SwSelect.Clear(ctx.Doc);
            try
            {
                if (r is LinearPatternRecord l)
                {
                    if (!(fm.CreateDefinition((int)swFeatureNameID_e.swFmLPattern) is LinearPatternFeatureData d)) return null;
                    d.D1Axis = dir1;
                    d.D1Spacing = l.Spacing1;
                    d.D1TotalInstances = l.Count1;
                    d.D1ReverseDirection = rev1;
                    if (dir2 != null)
                    {
                        d.D2Axis = dir2;
                        d.D2Spacing = l.Spacing2;
                        d.D2TotalInstances = l.Count2;
                        d.D2ReverseDirection = rev2;
                        d.D2PatternSeedOnly = l.SeedOnly2;
                    }
                    SetSeeds(seeds, r, v => d.PatternFeatureArray = v, v => d.PatternFaceArray = v, v => d.PatternBodyArray = v, v => d.BodyPattern = v);
                    d.GeometryPattern = l.GeometryPattern;
                    d.VarySketch = l.VarySketch;
                    return fm.CreateFeature(d);
                }
                else
                {
                    var c = (CircularPatternRecord)r;
                    if (!(fm.CreateDefinition((int)swFeatureNameID_e.swFmCirPattern) is CircularPatternFeatureData d)) return null;
                    d.Axis = dir1;
                    d.Spacing = c.Spacing;
                    d.TotalInstances = c.Count;
                    d.EqualSpacing = c.EqualSpacing;
                    d.ReverseDirection = rev1;
                    if (c.Direction2)
                    {
                        d.Direction2 = true;
                        d.Symmetric = c.Symmetric;
                        d.Spacing2 = c.Spacing2;
                        d.TotalInstances2 = c.Count2;
                        d.EqualSpacing2 = c.EqualSpacing2;
                    }
                    SetSeeds(seeds, r, v => d.PatternFeatureArray = v, v => d.PatternFaceArray = v, v => d.PatternBodyArray = v, v => d.BodyPattern = v);
                    d.GeometryPattern = c.GeometryPattern;
                    d.VarySketch = c.VarySketch;
                    return fm.CreateFeature(d);
                }
            }
            catch (Exception)
            {
                // Not supported for this pattern in this version, or a property was refused.
                return null;
            }
        }

        private static void SetSeeds(Seeds seeds, PatternRecord r, Action<object> features, Action<object> faces, Action<object> bodies, Action<bool> bodyPattern)
        {
            if (r.BodyPattern)
            {
                bodyPattern(true);
                bodies(seeds.Bodies.Cast<object>().ToArray());
                return;
            }
            if (seeds.Features.Count > 0) features(seeds.Features.Cast<object>().ToArray());
            if (seeds.Faces.Count > 0) faces(seeds.Faces.Cast<object>().ToArray());
        }

        // ---------------------------------------------------------------- direction flags

        /// <summary>
        /// Compares the placements of the new pattern with the mirrored ones of the original and
        /// tries the other flag combinations if they differ. Returns a remark for the report.
        /// </summary>
        private static string MatchPlacements(BuildContext ctx, Feature f, PatternRecord r, List<string> notes)
        {
            if (r.Instances.Count == 0) return "";
            List<Affine> wanted = PatternInstances.Mirror(r.Instances, ctx.Mirror);
            List<Affine>? actual = ReadInstances(ctx, f, r);
            if (actual == null || actual.Count == 0) return "";
            int best = PatternInstances.Unmatched(wanted, actual);
            if (best == 0) return ", Lage der Instanzen geprüft";

            // Toggles applied on top of each other: (1), (1+2), (2) for two directions.
            var toggles = new List<Func<bool>> { () => ToggleFirst(ctx.Doc, f) };
            if (r is LinearPatternRecord l && l.HasDirection2)
            {
                toggles.Add(() => ToggleSecond(ctx.Doc, f));
                toggles.Add(() => ToggleFirst(ctx.Doc, f));
            }
            int applied = 0, bestAt = 0;
            foreach (Func<bool> toggle in toggles)
            {
                if (!toggle()) break;
                applied++;
                List<Affine>? now = ReadInstances(ctx, f, r);
                int unmatched = now == null ? int.MaxValue : PatternInstances.Unmatched(wanted, now);
                if (unmatched < best)
                {
                    best = unmatched;
                    bestAt = applied;
                }
                if (best == 0) break;
            }
            // Back to the best combination seen (undo the toggles applied after it).
            for (int i = applied - 1; i >= bestAt; i--) toggles[i]();
            ctx.Topology.Invalidate();

            if (best == 0) return bestAt > 0 ? ", Richtung nach Lage der Instanzen korrigiert" : ", Lage der Instanzen geprüft";
            notes.Add($"{best} von {wanted.Count} Instanzen liegen nicht an der gespiegelten Stelle.");
            return "";
        }

        private static bool ToggleFirst(ModelDoc2 doc, Feature f) =>
            Edit(doc, f, d => d.D1ReverseDirection = !d.D1ReverseDirection, d => d.ReverseDirection = !d.ReverseDirection);

        private static bool ToggleSecond(ModelDoc2 doc, Feature f) =>
            Edit(doc, f, d => d.D2ReverseDirection = !d.D2ReverseDirection, _ => { });

        private static bool Edit(ModelDoc2 doc, Feature f, Action<LinearPatternFeatureData> linear, Action<CircularPatternFeatureData> circular) =>
            f.GetDefinition() is LinearPatternFeatureData
                ? FeatureEdit.LinearPattern(doc, f, linear)
                : FeatureEdit.CircularPattern(doc, f, circular);

        /// <summary>Placements of the new pattern, read under selection access like those of the original.</summary>
        private static List<Affine>? ReadInstances(BuildContext ctx, Feature f, PatternRecord r)
        {
            try
            {
                switch (f.GetDefinition())
                {
                    case LinearPatternFeatureData d when d.AccessSelections(ctx.Doc, null):
                        try
                        {
                            var l = (LinearPatternRecord)r;
                            return SwPattern.Instances(ctx.Math, l.Count1 * (l.HasDirection2 ? Math.Max(1, l.Count2) : 1), d.GetTransform);
                        }
                        finally
                        {
                            d.ReleaseSelectionAccess();
                        }
                    case CircularPatternFeatureData d when d.AccessSelections(ctx.Doc, null):
                        try
                        {
                            var c = (CircularPatternRecord)r;
                            return SwPattern.Instances(ctx.Math, c.Count + (c.Direction2 ? c.Count2 : 0), d.GetTransform);
                        }
                        finally
                        {
                            d.ReleaseSelectionAccess();
                        }
                }
            }
            catch (Exception)
            {
                // Placements not readable: the mass check has to decide.
            }
            return null;
        }

        private static void AddAlternatives(BuildResult result, ModelDoc2 doc, Feature f, PatternRecord r)
        {
            if (r is LinearPatternRecord l)
            {
                result.Alternatives.Add(new Alternative("Richtung 1 umgekehrt", () => ToggleFirst(doc, f)));
                if (l.HasDirection2) result.Alternatives.Add(new Alternative("Richtung 2 umgekehrt", () => ToggleSecond(doc, f)));
            }
            else
            {
                result.Alternatives.Add(new Alternative("Drehrichtung umgekehrt", () => ToggleFirst(doc, f)));
            }
        }

        private static string Describe(PatternRecord r)
        {
            string seeds = r.BodyPattern ? $"{r.SeedBodies.Count} Körper" : $"{r.SeedFeatures.Count + r.SeedFaces.Count} Saat-Elemente";
            return r switch
            {
                LinearPatternRecord l => $"Lineares Muster, {l.Count1} × {l.Spacing1 * 1000:0.###} mm" +
                                         (l.HasDirection2 ? $" und {l.Count2} × {l.Spacing2 * 1000:0.###} mm" : "") + $", {seeds}",
                CircularPatternRecord c => $"Kreismuster, {c.Count} × {c.Spacing * 180 / Math.PI:0.##}°" + (c.EqualSpacing ? " gesamt" : "") +
                                           (c.Direction2 ? $" und {c.Count2} in Richtung 2" : "") + $", {seeds}",
                _ => seeds,
            };
        }
    }
}
