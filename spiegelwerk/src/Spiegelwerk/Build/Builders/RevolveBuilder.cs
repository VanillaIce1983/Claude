using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Revolve and Revolve-Cut (also thin). A mirror turns the sense of rotation around, and
    /// how SOLIDWORKS picks the default side of a revolve is not documented; the first guess
    /// follows the sketch normal like an extrude, the verifier corrects it if needed.
    /// </summary>
    internal sealed class RevolveBuilder : IFeatureBuilder
    {
        /// <summary>
        /// Selection mark for the axis: the reference documents 4, newer official examples use 16.
        /// Both are tried.
        /// </summary>
        private static readonly int[] AxisMarks = { 16, 4 };

        public bool Handles(FeatureRecord record) => record is RevolveRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (RevolveRecord)record;
            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap sketch))
                return BuildResult.Failed($"Skizze \"{r.SketchName}\" wurde nicht neu aufgebaut.");

            var notes = new List<string>();

            // Separate body: as for extrusions, never let the copy touch the original. Checked
            // first, so a refused revolve leaves no helper axis behind.
            string? refused = PlanSeparateBody(ctx, r.IsCut, r.Merge, out bool scope, out bool createMerge);
            if (refused != null) return Fail(refused, notes);

            object? axis = Find(ctx, r.Axis, notes);
            if (axis == null)
                return Fail("Drehachse im Ziel nicht gefunden.", notes);

            (bool singleDir, EndCondition dir1, double a1, EndCondition dir2, double a2) = Layout(r);
            bool reverse = DirectionRules.ReverseFlag(r.Reverse, sketch.Sigma);
            List<object>? contours = MapContours(r.Contours, sketch, notes);

            ModelDoc2 doc = ctx.Doc;
            Feature? f = Create(ctx, r, sketch, contours, axis, singleDir, reverse, dir1, a1, dir2, a2, createMerge);
            bool contoursPending = false;
            if (f == null && contours != null)
            {
                f = Create(ctx, r, sketch, null, axis, singleDir, reverse, dir1, a1, dir2, a2, createMerge);
                contoursPending = f != null;
            }
            if (f == null) return Fail("SOLIDWORKS hat das Feature nicht erzeugt.", notes);

            if (scope || contoursPending)
            {
                bool ok = FeatureEdit.Revolve(doc, f, d =>
                {
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
                    return Fail("Konturauswahl oder Feature-Bereich ließen sich nicht setzen; das Feature wurde wieder entfernt.", notes);
                }
            }

            ctx.Register(r.Name, f);
            var result = BuildResult.Ok(f, (r.IsCut ? "Rotationsschnitt" : "Rotation") + $", {a1 * 180 / Math.PI:0.##}°");
            result.Notes.AddRange(notes);
            if (notes.Count > 0) result.Status = ReportStatus.Warning;
            if (r.DirectionMatters)
                result.Alternatives.Add(new Alternative("Drehrichtung umgekehrt",
                    () => FeatureEdit.Revolve(doc, f, d => d.ReverseDirection = !d.ReverseDirection)));
            if (r.IsThin && IsThinOneSided(r.ThinType))
                result.Alternatives.Add(new Alternative("Wandseite gewechselt",
                    () => FeatureEdit.Revolve(doc, f, d => d.ThinWallType = OtherThinSide(d.ThinWallType))));
            if (r.IsThin && r.ThinType == (int)swThinWallType_e.swThinWallTwoDirection && Math.Abs(r.Thickness1 - r.Thickness2) > 1e-12)
                result.Alternatives.Add(new Alternative("Wandstärken getauscht", () => FeatureEdit.Revolve(doc, f, d =>
                {
                    double a = d.GetWallThickness(true), b = d.GetWallThickness(false);
                    d.SetWallThickness(true, b);
                    d.SetWallThickness(false, a);
                })));
            return result;
        }

        private static Feature? Create(BuildContext ctx, RevolveRecord r, SketchMap sketch, List<object>? contours, object axis,
            bool singleDir, bool reverse, EndCondition dir1, double a1, EndCondition dir2, double a2, bool merge)
        {
            ModelDoc2 doc = ctx.Doc;
            foreach (int mark in AxisMarks)
            {
                SwSelect.Clear(doc);
                if (contours != null)
                    for (int i = 0; i < contours.Count; i++) SwSelect.Select(doc, contours[i], 0, i > 0);
                else
                    SwSelect.Select(doc, sketch.Feature, 0, false);
                SwSelect.Select(doc, axis, mark, true);
                Feature? f = doc.FeatureManager.FeatureRevolve2(singleDir, true, r.IsThin, r.IsCut, reverse, false,
                    (int)dir1, (int)dir2, a1, a2, false, false, 0, 0,
                    r.ThinType, r.Thickness1, r.Thickness2, merge, false, true);
                SwSelect.Clear(doc);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>swRevolveType_e → parameters of FeatureRevolve2.</summary>
        private static (bool singleDir, EndCondition dir1, double a1, EndCondition dir2, double a2) Layout(RevolveRecord r)
        {
            const double full = 2 * Math.PI;
            switch ((swRevolveType_e)r.RevolveType)
            {
                case swRevolveType_e.swRevolveTypeMidPlane:
                    return (true, EndCondition.MidPlane, r.Angle1, EndCondition.Blind, 0);
                case swRevolveType_e.swRevolveTypeTwoDirection:
                case swRevolveType_e.swRevolveTypeTwoDirection360Degrees:
                    return (false, EndCondition.Blind, r.Angle1, EndCondition.Blind, r.Angle2);
                case swRevolveType_e.swRevolveTypeOneDirection360Degrees:
                    return (true, EndCondition.Blind, full, EndCondition.Blind, 0);
                case swRevolveType_e.swRevolveTypeMidPlane360Degrees:
                    return (true, EndCondition.MidPlane, full, EndCondition.Blind, 0);
                default:
                    return (true, EndCondition.Blind, r.Angle1, EndCondition.Blind, 0);
            }
        }
    }
}
