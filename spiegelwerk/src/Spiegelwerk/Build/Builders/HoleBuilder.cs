using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Hole Wizard hole, in four steps:
    ///  1. Create one hole on the mirrored placement face – the documented way: feature data
    ///     from CreateDefinition, InitializeHole with type, standard, fastener and size, sizes
    ///     and options set, face selected at the first mirrored hole centre, CreateFeature.
    ///     HoleWizard5 is the fallback.
    ///  2. Write the mirrored position sketch into the position sketch of the new hole
    ///     (SketchBuilder.BuildInto): every hole centre with its relations and dimensions; the
    ///     point the first step placed is removed again.
    ///  3. Carry over sizes and options that still differ (custom sizes), the end reference
    ///     and – in "separate body" mode – the feature scope.
    ///  4. Count the holes.
    ///
    /// Hole direction: into the material, normal to the placement face. A reflection keeps
    /// outward normals outward, so "reverse direction" carries over unchanged; the opposite is
    /// still offered to the verifier. Sizes and threads are not mirrored – an opposite-hand part
    /// gets the same bores and the same right-hand threads.
    /// </summary>
    internal sealed class HoleBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is HoleRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (HoleRecord)record;
            var notes = new List<string>();

            // A hole is a cut: in "separate body" mode it needs a mirrored body to act on.
            string? refused = PlanSeparateBody(ctx, true, false, out bool scope, out bool _);
            if (refused != null) return Fail(refused, notes);

            object? face = Find(ctx, r.Face, notes);
            if (face == null) return Fail("Platzierungsfläche der Bohrung im Ziel nicht gefunden.", notes);
            object? endReference = null;
            if (ExtrudeSide.NeedsReference(r.End))
            {
                endReference = Find(ctx, r.EndReference, notes);
                if (endReference == null) return Fail($"Bezug der Endbedingung {r.End} im Ziel nicht gefunden.", notes);
            }
            List<Vec3> centres = r.Points.Select(ctx.Mirror.Apply).ToList();

            // ---- 1. one hole on the face
            bool scopeSet = false;
            Feature? f = CreateByDefinition(ctx, r, face, centres, endReference, scope, ref scopeSet);
            bool byDefinition = f != null;
            f ??= CreateByHoleWizard(ctx, r, face, centres);
            if (f == null) return Fail("SOLIDWORKS hat die Bohrung nicht erzeugt.", notes);

            // ---- 2. the position sketch
            ctx.Topology.Invalidate();
            Feature? position = SwHole.PositionSketch(ctx.Doc, f);
            if (position == null)
            {
                Delete(ctx, f, true);
                return Fail("Positionsskizze der neuen Bohrung nicht gefunden; die Bohrung wurde wieder entfernt.", notes);
            }
            try
            {
                new SketchBuilder(ctx).BuildInto(r.Position, position, notes);
            }
            catch (Exception ex)
            {
                Delete(ctx, f, true);
                return Fail($"Positionsskizze ließ sich nicht übertragen ({ex.Message}); die Bohrung wurde wieder entfernt.", notes);
            }
            ctx.Register(r.PositionSketchName, position);

            // ---- 3. sizes, end reference, scope
            List<string> differing = Differences(ctx.Doc, f, r.Values);
            bool needsEdit = differing.Count > 0 || (endReference != null && !byDefinition) || (scope && !scopeSet);
            if (needsEdit)
            {
                bool ok = FeatureEdit.Hole(ctx.Doc, f, d =>
                {
                    SwHole.Apply(d, r.Values);
                    if (endReference != null) d.SetEndConditionReference(endReference);
                    if (scope && !scopeSet)
                    {
                        d.FeatureScope = true;
                        d.AutoSelect = false;
                        d.FeatureScopeBodies = OwnBodiesBeforeFeature(ctx);
                    }
                });
                if (!ok && scope && !scopeSet)
                {
                    Delete(ctx, f, true);
                    ctx.Unregister(r.PositionSketchName);
                    return Fail("Der Feature-Bereich der Bohrung ließ sich nicht setzen; die Bohrung wurde wieder entfernt.", notes);
                }
                differing = Differences(ctx.Doc, f, r.Values);
            }
            if (differing.Count > 0)
                notes.Add($"Nicht übernommene Bohrungsmaße: {string.Join(", ", differing)}.");

            // ---- 4. count
            int count = SwHole.PointCount(ctx.Doc, f);
            if (count >= 0 && count != r.Points.Count)
                notes.Add($"Die Kopie hat {count} statt {r.Points.Count} Bohrungen.");

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, $"{r.Points.Count} × {r.Size} ({TypeText(r.GenericType)}), {r.End}", notes);
            result.Alternatives.Add(new Alternative("Bohrrichtung umgekehrt",
                () => FeatureEdit.Hole(ctx.Doc, f, d => d.ReverseDirection = !d.ReverseDirection)));
            return result;
        }

        /// <summary>The documented way: feature data, InitializeHole, sizes, face selected at the first centre, CreateFeature.</summary>
        private static Feature? CreateByDefinition(BuildContext ctx, HoleRecord r, object face, List<Vec3> centres, object? endReference,
            bool scope, ref bool scopeSet)
        {
            FeatureManager fm = ctx.Doc.FeatureManager;
            try
            {
                if (!(fm.CreateDefinition((int)swFeatureNameID_e.swFmHoleWzd) is WizardHoleFeatureData2 d)) return null;
                d.InitializeHole(r.GenericType, r.Standard, r.FastenerType, r.Size, (int)r.End);
                SwHole.Apply(d, r.Values);
                d.ReverseDirection = r.Reverse;
                if (r.ThreadClass.Length > 0) d.ThreadClass = r.ThreadClass;
                if (endReference != null) d.SetEndConditionReference(endReference);
                if (scope)
                {
                    // Nothing is rolled back here: the own bodies now are the bodies before the hole.
                    d.FeatureScope = true;
                    d.AutoSelect = false;
                    d.FeatureScopeBodies = ctx.OwnBodies().Cast<object>().ToArray();
                }
                if (!SelectFace(ctx, face, centres)) return null;
                Feature? f = fm.CreateFeature(d);
                SwSelect.Clear(ctx.Doc);
                if (f != null) scopeSet = scope;
                return f;
            }
            catch (Exception)
            {
                // Not supported in this version, or a value was refused: the classic method follows.
                SwSelect.Clear(ctx.Doc);
                return null;
            }
        }

        /// <summary>
        /// Fallback: HoleWizard5 with type, standard, fastener, size and the main sizes; the
        /// twelve type-specific values stay -1 (ignored), the rest follows in step 3.
        /// </summary>
        private static Feature? CreateByHoleWizard(BuildContext ctx, HoleRecord r, object face, List<Vec3> centres)
        {
            if (!SelectFace(ctx, face, centres)) return null;
            double Value(string name) => r.Values.TryGetValue(name, out double v) ? v : 0;
            double diameter = Value("HoleDiameter") > 0 ? Value("HoleDiameter") : Value("Diameter");
            double depth = Value("HoleDepth") > 0 ? Value("HoleDepth") : Value("Depth");
            Feature? f = ctx.Doc.FeatureManager.HoleWizard5(r.GenericType, r.Standard, r.FastenerType, r.Size, (short)r.End,
                diameter, depth, Value("Length"),
                -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
                r.ThreadClass, r.Reverse, false, true, false, false, false);
            SwSelect.Clear(ctx.Doc);
            return f;
        }

        /// <summary>
        /// The placement face, selected at the first mirrored hole centre (SOLIDWORKS places the
        /// first hole where the face was picked). The pick must hit this very face – in the same
        /// part the original may have a face at the same spot – otherwise the face is selected
        /// without a point; the position sketch moves the holes to their places anyway.
        /// </summary>
        private static bool SelectFace(BuildContext ctx, object face, List<Vec3> centres)
        {
            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            if (face is Face2 target && centres.Count > 0)
            {
                Vec3 p = centres[0];
                if (doc.Extension.SelectByID2("", "FACE", p.X, p.Y, p.Z, false, 0, null, 0) &&
                    ((SelectionMgr)doc.SelectionManager).GetSelectedObject6(1, -1) is Face2 picked && IsSameFace(ctx, picked, target))
                    return true;
                SwSelect.Clear(doc);
            }
            return SwSelect.Select(doc, face, 0, false);
        }

        private static bool IsSameFace(BuildContext ctx, Face2 picked, Face2 target)
        {
            FaceSignature a = TopologyCapture.Face(picked), b = TopologyCapture.Face(target);
            if (!b.CouldBe(a) || TopologyCapture.DistanceToFace(target, a.Sample) > Tol.Length) return false;
            return !ctx.SamePart || (picked.GetBody() is Body2 body && ctx.IsOwnBody(body));
        }

        private static List<string> Differences(ModelDoc2 doc, Feature f, IReadOnlyDictionary<string, double> wanted)
        {
            var d = (WizardHoleFeatureData2)f.GetDefinition();
            if (!d.AccessSelections(doc, null)) return new List<string>();
            try
            {
                return SwHole.Differences(d, wanted);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        private static string TypeText(int generic) => generic switch
        {
            HoleTypes.CounterBore => "Senkbohrung zylindrisch",
            HoleTypes.CounterSink => "Senkbohrung kegelig",
            HoleTypes.Hole => "Bohrung",
            HoleTypes.Tap => "Gewindebohrung",
            HoleTypes.PipeTap => "Rohrgewinde",
            _ => $"Typ {generic}",
        };
    }
}
