using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Sweep and swept cut. Profile, path and guide curves are rebuilt sketches by the time the
    /// sweep is built; they are selected with the marks the creation methods expect (profile 1,
    /// guide curves 2, path 4). The path keeps its direction under the mirror (its start point
    /// is the mirror of the original start point), so everything that is measured along the
    /// path carries over unchanged.
    ///
    /// Not so the twist: a profile turning right-handed about the path turns left-handed in the
    /// mirror image. The first variant therefore reverses the twist direction; whether
    /// SOLIDWORKS measures it about the path or about the profile normal is not documented,
    /// so the opposite is offered to the verifier, whose face check sees the difference even
    /// where volume and centroid do not.
    ///
    /// Direction vectors, path alignment vector and – in "separate body" mode – the feature
    /// scope are set in one ModifyDefinition after creation; selecting them with marks (128 …)
    /// is reported not to work reliably.
    /// </summary>
    internal sealed class SweepBuilder : IFeatureBuilder
    {
        private const int ProfileMark = 1, GuideMark = 2, PathMark = 4;

        public bool Handles(FeatureRecord record) => record is SweepRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (SweepRecord)record;
            var notes = new List<string>();

            // Before resolving anything: a refused feature must not leave helper planes or axes behind.
            string? refused = PlanSeparateBody(ctx, r.IsCut, r.Merge, out bool scope, out bool createMerge);
            if (refused != null) return Fail(refused, notes);

            object? profile = null;
            if (!r.CircularProfile)
            {
                profile = Find(ctx, r.Profile, notes);
                if (profile == null) return Fail($"Profil ({r.Profile}) wurde nicht neu aufgebaut.", notes);
            }
            object? path = Find(ctx, r.Path, notes);
            if (path == null) return Fail($"Pfad ({r.Path}) wurde nicht neu aufgebaut.", notes);
            var guides = new List<object>();
            foreach (GeometryRef g in r.Guides)
            {
                object? guide = Find(ctx, g, notes);
                if (guide == null) return Fail($"Führungskurve ({g}) wurde nicht neu aufgebaut.", notes);
                guides.Add(guide);
            }

            Tangency start = r.StartTangency, end = r.EndTangency, align = r.PathAlignment;
            object? startDir = DirectionOrNone(ctx, r.StartDirection, ref start, "Starttangente", notes);
            object? endDir = DirectionOrNone(ctx, r.EndDirection, ref end, "Endtangente", notes);
            object? alignDir = DirectionOrNone(ctx, r.PathAlignmentDirection, ref align, "Pfadausrichtung", notes);

            // Direction vectors are attached afterwards; until then the condition is "none".
            Feature? f = Create(ctx, r, profile, path, guides, createMerge,
                start == Tangency.DirectionVector ? Tangency.None : start,
                end == Tangency.DirectionVector ? Tangency.None : end,
                align == Tangency.DirectionVector ? Tangency.None : align);
            if (f == null) return Fail("SOLIDWORKS hat die Austragung nicht erzeugt.", notes);

            bool reverseTwist1 = r.HasTwist ? !r.ReverseTwist1 : r.ReverseTwist1;
            bool reverseTwist2 = r.HasTwist ? !r.ReverseTwist2 : r.ReverseTwist2;
            bool bidirectional = r.Direction == (int)swSweepDirection_e.swSweepBidirectional;
            bool needsEdit = r.HasTwist || scope || startDir != null || endDir != null || alignDir != null;
            if (needsEdit)
            {
                bool ok = FeatureEdit.Sweep(ctx.Doc, f, d =>
                {
                    if (r.HasTwist)
                    {
                        d.D1ReverseTwistDir = reverseTwist1;
                        if (bidirectional)
                        {
                            d.SetD2TwistAngle(r.TwistAngle2);
                            d.D2ReverseTwistDir = reverseTwist2;
                        }
                    }
                    if (startDir != null)
                    {
                        d.StartTangencyType = (short)Tangency.DirectionVector;
                        d.StartDirectionVector = startDir;
                    }
                    if (endDir != null)
                    {
                        d.EndTangencyType = (short)Tangency.DirectionVector;
                        d.EndDirectionVector = endDir;
                    }
                    if (alignDir != null)
                    {
                        d.PathAlignmentType = (int)Tangency.DirectionVector;
                        d.SetPathAlignmentDirectionVector(alignDir);
                    }
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
                    return Fail("Verdrehung, Richtungsvektoren oder Feature-Bereich ließen sich nicht setzen; das Feature wurde wieder entfernt.", notes);
                }
            }

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, Describe(r), notes);
            AddAlternatives(result, ctx.Doc, f, r, bidirectional);
            return result;
        }

        private static Feature? Create(BuildContext ctx, SweepRecord r, object? profile, object path, List<object> guides, bool merge,
            Tangency start, Tangency end, Tangency align)
        {
            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            bool selected = true;
            if (profile != null) selected &= SwSelect.Select(doc, profile, ProfileMark, false);
            selected &= SwSelect.Select(doc, path, PathMark, profile != null);
            foreach (object g in guides) selected &= SwSelect.Select(doc, g, GuideMark, true);
            if (!selected)
            {
                SwSelect.Clear(doc);
                return null;
            }

            FeatureManager fm = doc.FeatureManager;
            Feature? f = r.IsCut
                ? fm.InsertCutSwept5(r.TangentPropagation, r.AlignWithEndFaces, (int)r.Twist, r.MaintainTangency, r.AdvancedSmoothing,
                    (int)start, (int)end, r.IsThin, r.Thickness1, r.Thickness2, r.ThinType, (int)align,
                    false, true, r.TwistAngle, r.MergeSmoothFaces, false, false, false,
                    r.CircularProfile, r.CircularDiameter, r.Direction)
                : fm.InsertProtrusionSwept4(r.TangentPropagation, r.AlignWithEndFaces, (int)r.Twist, r.MaintainTangency, r.AdvancedSmoothing,
                    (int)start, (int)end, r.IsThin, r.Thickness1, r.Thickness2, r.ThinType, (int)align,
                    merge, false, true, r.TwistAngle, r.MergeSmoothFaces,
                    r.CircularProfile, r.CircularDiameter, r.Direction);
            SwSelect.Clear(doc);
            return f;
        }

        /// <summary>
        /// The reference of a direction-vector condition. Not found: the condition falls back
        /// to "none" with a note (the verifier then shows how much that changes).
        /// </summary>
        private static object? DirectionOrNone(BuildContext ctx, GeometryRef? original, ref Tangency condition, string what, List<string> notes)
        {
            if (condition != Tangency.DirectionVector) return null;
            object? target = Find(ctx, original, notes);
            if (target == null)
            {
                notes.Add($"{what}: Richtungsvektor im Ziel nicht gefunden, Bedingung „keine“ verwendet.");
                condition = Tangency.None;
            }
            return target;
        }

        private static void AddAlternatives(BuildResult result, ModelDoc2 doc, Feature f, SweepRecord r, bool bidirectional)
        {
            if (r.HasTwist)
                result.Alternatives.Add(new Alternative("Verdrehrichtung umgekehrt", () => FeatureEdit.Sweep(doc, f, d =>
                {
                    d.D1ReverseTwistDir = !d.D1ReverseTwistDir;
                    if (bidirectional) d.D2ReverseTwistDir = !d.D2ReverseTwistDir;
                })));
            if (r.IsThin && IsThinOneSided(r.ThinType))
                result.Alternatives.Add(new Alternative("Wandseite gewechselt",
                    () => FeatureEdit.Sweep(doc, f, d => d.ThinWallType = (short)OtherThinSide(d.ThinWallType))));
            if (r.IsThin && HasUnequalWalls(r.ThinType, r.Thickness1, r.Thickness2))
                result.Alternatives.Add(new Alternative("Wandstärken getauscht", () => FeatureEdit.Sweep(doc, f, d =>
                {
                    double a = d.GetWallThickness(true), b = d.GetWallThickness(false);
                    d.SetWallThickness(true, b);
                    d.SetWallThickness(false, a);
                })));
            if (r.CircularProfile && !bidirectional)
                result.Alternatives.Add(new Alternative("Austragungsrichtung gewechselt", () => FeatureEdit.Sweep(doc, f, d =>
                    d.Direction = d.Direction == (int)swSweepDirection_e.swSweepDirection1
                        ? (int)swSweepDirection_e.swSweepDirection2
                        : (int)swSweepDirection_e.swSweepDirection1)));
        }

        private static string Describe(SweepRecord r)
        {
            string what = (r.IsCut ? "Austragungsschnitt" : "Austragung") + (r.IsThin ? " (dünnwandig)" : "");
            string profile = r.CircularProfile ? $"Kreisprofil Ø {r.CircularDiameter * 1000:0.###} mm" : "Profilskizze";
            string guides = r.Guides.Count > 0 ? $", {r.Guides.Count} Führungskurve(n)" : "";
            string twist = r.HasTwist ? $", Verdrehung {r.TwistAngle * 180 / Math.PI:0.##}° gegensinnig" : "";
            return $"{what}, {profile}{guides}{twist}";
        }
    }
}
