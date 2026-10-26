using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;
using static Spiegelwerk.Build.Builders.PatternSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Fill pattern: the mirrored boundary (faces or the rebuilt sketch), direction and seed
    /// centre, the same layout values – except the stagger angle of a perforation layout
    /// (π − θ) and the rotation of a seed cut (−α), which the mirror reverses
    /// (FillPatternRules). Whether SOLIDWORKS orients the plane of the copy as assumed is not
    /// documented, so the other choice is offered to the verifier; the API reports no
    /// instance placements, so volume, centroid and face samples decide.
    ///
    /// Creation: the feature data object (CreateDefinition(swFmFillPattern), properties set,
    /// the references also selected with the marks of the API help: boundary 16384,
    /// direction 1, seed centre 3, features 4, faces 5), CreateFeature; FeatureFillPattern
    /// with the same selection as fallback. In "separate body" mode the feature scope is set
    /// to the mirrored bodies (the fill pattern has one in every API version).
    /// </summary>
    internal sealed class FillPatternBuilder : IFeatureBuilder
    {
        private const int DirectionMark = 1, CenterMark = 3, FeatureMark = 4, FaceMark = 5, BodyMark = 256, BoundaryMark = 16384;
        private const int Perforation = (int)swPatternLayoutType_e.swPatternLayoutPerforation;

        public bool Handles(FeatureRecord record) => record is FillPatternRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (FillPatternRecord)record;
            var notes = new List<string>();

            // Nothing to pattern: fail before a direction can add a helper axis.
            if (!r.IsSeedCut && FindSeeds(ctx, r, new List<string>()) == null) return Fail(NoSeeds(r), notes);
            // A seed cut is a cut: in "separate body" mode it needs a mirrored body to act on.
            string? refused = PlanSeparateBody(ctx, r.IsSeedCut, false, out bool _, out bool _);
            if (refused != null) return Fail(refused, notes);
            bool scope = ctx.SeparateBody;

            var boundary = new List<object>();
            foreach (GeometryRef g in r.Boundary)
            {
                // Faces only as faces: no plane through a face that is missing.
                object? target = g.Kind == GeometryRefKind.Face && g.Face is FaceSignature sig
                    ? Safe(ctx.Topology.FindFace(sig.Mirror(ctx.Mirror), notes))
                    : Find(ctx, g, notes);
                if (target == null) return Fail($"Begrenzung des Füllmusters ({g}) im Ziel nicht gefunden.", notes);
                boundary.Add(target);
            }
            // The seed position before the direction: a direction may add a helper axis, and a
            // pattern refused afterwards would leave it behind.
            if (r.SeedCenterIsOrigin && !ctx.Mirror.Apply(Vec3.Zero).AlmostEquals(Vec3.Zero))
                return Fail("Die Saat sitzt im Ursprung; die Spiegelebene geht nicht durch ihn, gespiegelt liegt er nicht im Ursprung des Ziels.", notes);
            if (r.SeedCenter != null && Find(ctx, r.SeedCenter, new List<string>()) == null)
                return Fail("Lage der Saat des Füllmusters im Ziel nicht gefunden.", notes);
            object? direction = null;
            if (r.Direction != null)
            {
                direction = Find(ctx, r.Direction, notes);
                if (direction == null) return Fail("Musterrichtung des Füllmusters im Ziel nicht gefunden.", notes);
            }
            // A helper axis may have been added: look the seed position and the seeds up again.
            object? center = r.SeedCenterIsOrigin ? SwDoc.OriginPoint(ctx.Doc) : r.SeedCenter != null ? Find(ctx, r.SeedCenter, notes) : null;
            if (r.SeedCenter != null && center == null) return Fail("Lage der Saat des Füllmusters im Ziel nicht gefunden.", notes);
            Seeds? seeds = null;
            if (!r.IsSeedCut)
            {
                seeds = FindSeeds(ctx, r, notes);
                if (seeds == null) return Fail(NoSeeds(r), notes);
            }

            // A symmetric lattice (60°, 90°, 120°) is its own mirror image: it keeps its angle.
            double stagger = MirroredStagger(r);
            double rotation = FillPatternRules.MirrorRotation(r.SeedCutRotation);
            var refs = new References(boundary, direction, center, seeds);

            Feature? f = CreateByDefinition(ctx, r, refs, stagger, rotation, scope) ?? CreateBySelection(ctx, r, refs, stagger, rotation);
            if (f == null) return Fail("SOLIDWORKS hat das Füllmuster nicht erzeugt.", notes);

            // ---- exact values, skipped instances, feature scope
            List<string> differing = Read(ctx.Doc, f, d => Differences(d, r, stagger, rotation)) ?? new List<string>();
            bool scopeNeeded = scope && !(Read(ctx.Doc, f, d => (object)(d.FeatureScope && !d.AutoSelect)) is bool set && set);
            if (differing.Count > 0 || r.SkippedItems.Count > 0 || scopeNeeded)
            {
                bool ok = FeatureEdit.FillPattern(ctx.Doc, f, d =>
                {
                    Apply(d, r, stagger, rotation);
                    if (r.SkippedItems.Count > 0) d.SkippedItemArray = r.SkippedItems.ToArray();
                    if (scopeNeeded)
                    {
                        d.FeatureScope = true;
                        d.AutoSelect = false;
                        d.FeatureScopeBodies = OwnBodiesBeforeFeature(ctx);
                    }
                });
                if (!ok && scopeNeeded)
                {
                    Delete(ctx, f);
                    return Fail("Der Feature-Bereich des Füllmusters ließ sich nicht auf den gespiegelten Körper beschränken; das Muster wurde wieder entfernt.", notes);
                }
                if (!ok && r.SkippedItems.Count > 0) notes.Add($"Die {r.SkippedItems.Count} übersprungenen Instanzen ließen sich nicht übernehmen.");
                differing = Read(ctx.Doc, f, d => Differences(d, r, stagger, rotation)) ?? differing;
            }
            if (differing.Count > 0) notes.Add($"Nicht übernommen: {string.Join(", ", differing)}.");
            // The numbering follows the in-plane frame, which the mirror turns the other way round.
            if (r.SkippedItems.Count > 0)
                notes.Add($"{r.SkippedItems.Count} übersprungene Instanzen nach Nummer übernommen; die Spiegelung kehrt die Reihenfolge in der Ebene um – bitte prüfen, ob die richtigen fehlen.");

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, Describe(r), notes);
            AddAlternatives(result, ctx.Doc, f, r, stagger, rotation);
            return result;
        }

        private sealed class References
        {
            public References(List<object> boundary, object? direction, object? center, Seeds? seeds)
            {
                Boundary = boundary;
                Direction = direction;
                Center = center;
                Seeds = seeds;
            }

            public List<object> Boundary { get; }
            public object? Direction { get; }
            public object? Center { get; }
            public Seeds? Seeds { get; }
        }

        // ---------------------------------------------------------------- creation

        /// <summary>The references selected with the marks of the API help – also for the data object, whose example selects them.</summary>
        private static bool Select(ModelDoc2 doc, References refs)
        {
            SwSelect.Clear(doc);
            bool ok = true;
            foreach (object b in refs.Boundary) ok &= SwSelect.Select(doc, b, BoundaryMark, true);
            if (refs.Direction != null) ok &= SwSelect.Select(doc, refs.Direction, DirectionMark, true);
            if (refs.Center != null) ok &= SwSelect.Select(doc, refs.Center, CenterMark, true);
            if (refs.Seeds != null)
            {
                foreach (Feature seed in refs.Seeds.Features) ok &= SwSelect.Select(doc, seed, FeatureMark, true);
                foreach (Face2 face in refs.Seeds.Faces) ok &= SwSelect.Select(doc, face, FaceMark, true);
                foreach (Body2 body in refs.Seeds.Bodies) ok &= SwSelect.Select(doc, body, BodyMark, true);
            }
            return ok;
        }

        private static Feature? CreateByDefinition(BuildContext ctx, FillPatternRecord r, References refs, double stagger, double rotation, bool scope)
        {
            ModelDoc2 doc = ctx.Doc;
            FeatureManager fm = doc.FeatureManager;
            try
            {
                if (!(fm.CreateDefinition((int)swFeatureNameID_e.swFmFillPattern) is FillPatternFeatureData d)) return null;
                d.PatternFillBoundaryArray = refs.Boundary.ToArray();
                if (refs.Direction != null) d.PatternDirection = refs.Direction;
                if (refs.Center != null) d.SeedFeatureCenter = refs.Center;
                // The element switch first: features/faces or bodies.
                Try(() => d.PatternElement = (int)(r.BodyPattern ? swPatternElementSelection_e.swBodiesToPattern : swPatternElementSelection_e.swFeatureFaces));
                if (refs.Seeds != null)
                    SetSeeds(refs.Seeds, r, v => d.PatternFeatureArray = v, v => d.PatternFaceArray = v, v => d.PatternBodyArray = v, _ => { });
                d.FeaturesToPatternType = r.FeaturesToPattern;
                Apply(d, r, stagger, rotation);
                if (scope)
                {
                    // Nothing is rolled back here: the own bodies now are the bodies before the pattern.
                    d.FeatureScope = true;
                    d.AutoSelect = false;
                    d.FeatureScopeBodies = ctx.OwnBodies().Cast<object>().ToArray();
                }
                if (!Select(doc, refs)) return null;
                return fm.CreateFeature(d);
            }
            catch (Exception)
            {
                // Not supported in this version, or a property was refused.
                return null;
            }
            finally
            {
                SwSelect.Clear(doc);
            }
        }

        private static Feature? CreateBySelection(BuildContext ctx, FillPatternRecord r, References refs, double stagger, double rotation)
        {
            ModelDoc2 doc = ctx.Doc;
            try
            {
                if (!Select(doc, refs)) return null;
                return doc.FeatureManager.FeatureFillPattern(r.LayoutType, r.SpacingType, r.InstanceSpacing, stagger, r.Margins, r.LoopSpacing,
                    r.InstanceCount, r.PolygonSides, r.FeaturesToPattern, r.SeedCutType, r.SeedCutDiameter, r.SeedCutDimension, rotation,
                    r.SeedCutDiagonal, r.SeedCutPolygonSides, r.SeedCutOuterRadius, r.SeedCutInnerRadius, r.SeedCutFlip, r.GeometryPattern);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                SwSelect.Clear(doc);
            }
        }

        // ---------------------------------------------------------------- values

        private static List<string> Differences(FillPatternFeatureData d, FillPatternRecord r, double stagger, double rotation)
        {
            var result = new List<string>();
            void Check(string name, bool same)
            {
                if (!same) result.Add(name);
            }
            Check("Layout", d.PatternLayoutType == r.LayoutType);
            Check("Abstandsart", d.LayoutSpacingType == r.SpacingType);
            Check("Instanzabstand", Same(d.InstanceSpacing, r.InstanceSpacing));
            Check("Versatzwinkel", Same(d.StaggerAngle, stagger));
            Check("Rand", Same(d.Margins, r.Margins));
            Check("Ringabstand", Same(d.LoopSpacing, r.LoopSpacing));
            Check("Anzahl", d.NoOfInstances == r.InstanceCount);
            Check("Polygonseiten", d.PatternLayoutPolygonSides == r.PolygonSides);
            Check("Saat-Art", d.FeaturesToPatternType == r.FeaturesToPattern);
            Check("Geometriemuster", d.GeometryPattern == r.GeometryPattern);
            Check("Darstellung übernehmen", d.PropagateVisualProperty == r.PropagateVisualProperty);
            if (!r.IsSeedCut && r.BodyPattern) Check("Saat-Art (Körper)", d.PatternElement == (int)swPatternElementSelection_e.swBodiesToPattern);
            if (!r.IsSeedCut) return result;
            Check("Saat-Schnitt", d.CreateSeedCutType == r.SeedCutType);
            Check("Durchmesser", Same(d.Diameter, r.SeedCutDiameter));
            Check("Maß", Same(d.Dimension, r.SeedCutDimension));
            Check("Drehung", Same(d.Rotation, rotation));
            Check("Diagonale", Same(d.Diagonal, r.SeedCutDiagonal));
            Check("Polygonseiten des Saat-Schnitts", d.CreateSeedCutPolygonSides == r.SeedCutPolygonSides);
            Check("Außenradius", Same(d.OuterRadius, r.SeedCutOuterRadius));
            Check("Innenradius", Same(d.InnerRadius, r.SeedCutInnerRadius));
            Check("Schnittrichtung", d.SeedCutFlipShapeDirection == r.SeedCutFlip);
            return result;
        }

        /// <summary>Layout and seed cut values; a refused one does not stop the others.</summary>
        private static void Apply(FillPatternFeatureData d, FillPatternRecord r, double stagger, double rotation)
        {
            Try(() => d.PatternLayoutType = r.LayoutType);
            Try(() => d.LayoutSpacingType = r.SpacingType);
            Try(() => d.InstanceSpacing = r.InstanceSpacing);
            Try(() => d.StaggerAngle = stagger);
            Try(() => d.Margins = r.Margins);
            Try(() => d.LoopSpacing = r.LoopSpacing);
            Try(() => d.NoOfInstances = r.InstanceCount);
            Try(() => d.PatternLayoutPolygonSides = r.PolygonSides);
            Try(() => d.GeometryPattern = r.GeometryPattern);
            Try(() => d.PropagateVisualProperty = r.PropagateVisualProperty);
            if (!r.IsSeedCut) return;
            Try(() => d.CreateSeedCutType = r.SeedCutType);
            Try(() => d.Diameter = r.SeedCutDiameter);
            Try(() => d.Dimension = r.SeedCutDimension);
            Try(() => d.Rotation = rotation);
            Try(() => d.Diagonal = r.SeedCutDiagonal);
            Try(() => d.CreateSeedCutPolygonSides = r.SeedCutPolygonSides);
            Try(() => d.OuterRadius = r.SeedCutOuterRadius);
            Try(() => d.InnerRadius = r.SeedCutInnerRadius);
            Try(() => d.SeedCutFlipShapeDirection = r.SeedCutFlip);
        }

        /// <summary>The stagger angle of the copy: the supplement, unless the lattice is symmetric anyway.</summary>
        private static double MirroredStagger(FillPatternRecord r) =>
            r.LayoutType == Perforation && !FillPatternRules.StaggerIsSymmetric(r.StaggerAngle) ? FillPatternRules.MirrorStagger(r.StaggerAngle) : r.StaggerAngle;

        /// <summary>
        /// The verifier's choices, each a self-inverse toggle. First all at once – if SOLIDWORKS
        /// lays the plane of the copy the other way, stagger, rotation and cut direction are all
        /// reversed, and while the cut goes the wrong way a single other change measures
        /// nothing –, then the cut direction, then stagger and rotation on their own.
        /// </summary>
        private static void AddAlternatives(BuildResult result, ModelDoc2 doc, Feature f, FillPatternRecord r, double stagger, double rotation)
        {
            bool staggerChoice = r.LayoutType == Perforation && !FillPatternRules.StaggerIsSymmetric(r.StaggerAngle);
            bool rotationChoice = r.IsSeedCut && Math.Abs(r.SeedCutRotation) > 1e-9 && r.SeedCutType != (int)swCreateSeedCutType_e.swCreateSeedCutCircle;
            double otherStagger = r.StaggerAngle, otherRotation = r.SeedCutRotation;
            void ToggleStagger(FillPatternFeatureData d) => d.StaggerAngle = Same(d.StaggerAngle, stagger) ? otherStagger : stagger;
            void ToggleRotation(FillPatternFeatureData d) => d.Rotation = Same(d.Rotation, rotation) ? otherRotation : rotation;
            void ToggleFlip(FillPatternFeatureData d) => d.SeedCutFlipShapeDirection = !d.SeedCutFlipShapeDirection;

            int parts = (staggerChoice ? 1 : 0) + (rotationChoice ? 1 : 0) + (r.IsSeedCut ? 1 : 0);
            if (parts >= 2)
                result.Alternatives.Add(new Alternative("Ebene der Kopie andersherum (Versatz, Drehung, Schnittrichtung)", () => FeatureEdit.FillPattern(doc, f, d =>
                {
                    if (staggerChoice) ToggleStagger(d);
                    if (rotationChoice) ToggleRotation(d);
                    if (r.IsSeedCut) ToggleFlip(d);
                })));
            if (r.IsSeedCut) result.Alternatives.Add(new Alternative("Schnittrichtung umgekehrt", () => FeatureEdit.FillPattern(doc, f, ToggleFlip)));
            if (staggerChoice) result.Alternatives.Add(new Alternative("Versatzwinkel nicht gespiegelt", () => FeatureEdit.FillPattern(doc, f, ToggleStagger)));
            if (rotationChoice) result.Alternatives.Add(new Alternative("Drehung des Saat-Schnitts nicht gespiegelt", () => FeatureEdit.FillPattern(doc, f, ToggleRotation)));
        }

        private static string Describe(FillPatternRecord r)
        {
            string layout = r.LayoutType switch
            {
                (int)swPatternLayoutType_e.swPatternLayoutCircular => "kreisförmig",
                (int)swPatternLayoutType_e.swPatternLayoutSquare => "quadratisch",
                (int)swPatternLayoutType_e.swPatternLayoutPolygon => "polygonal",
                Perforation => $"versetzt {r.StaggerAngle * 180 / Math.PI:0.##}°" +
                               (Same(MirroredStagger(r), r.StaggerAngle) ? "" : $" → {MirroredStagger(r) * 180 / Math.PI:0.##}°"),
                _ => $"Layout {r.LayoutType}",
            };
            string seed = r.IsSeedCut ? "Saat-Schnitt" : SeedText(r);
            return $"Füllmuster ({layout}), {seed}";
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
        private static T? Read<T>(ModelDoc2 doc, Feature f, Func<FillPatternFeatureData, T> read) where T : class
        {
            try
            {
                var d = (FillPatternFeatureData)f.GetDefinition();
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
    }
}
