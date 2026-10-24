using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Loft and lofted cut. Profiles are selected in the original order with mark 1, guide
    /// curves with 2, the centreline with 4.
    ///
    /// Which vertices of neighbouring profiles are connected follows from where each profile
    /// was picked. Sketch profiles are therefore selected by name at the mirrored pick point,
    /// and afterwards the pick points of the new loft are compared with the mirrored ones and
    /// set if they differ – a loft with the wrong connectors is twisted, with a different volume.
    ///
    /// Start and end conditions "normal to profile" point along the profile's sketch normal,
    /// which the rebuilt sketch may have reversed (σ &lt; 0); the first variant compensates for
    /// that like an extrusion, the opposite is offered to the verifier.
    /// </summary>
    internal sealed class LoftBuilder : IFeatureBuilder
    {
        private const int ProfileMark = 1, GuideMark = 2, CenterlineMark = 4;

        public bool Handles(FeatureRecord record) => record is LoftRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (LoftRecord)record;
            var notes = new List<string>();

            // Before resolving anything: a refused feature must not leave helper planes or axes behind.
            string? refused = PlanSeparateBody(ctx, r.IsCut, r.Merge, out bool scope, out bool createMerge);
            if (refused != null) return Fail(refused, notes);

            var profiles = new List<object>();
            foreach (GeometryRef p in r.Profiles)
            {
                // A face profile must be that face – the plane through it that Find falls back to is no profile.
                object? profile = p.Kind == GeometryRefKind.Face
                    ? Safe(ctx.Topology.FindFace(p.Face!.Mirror(ctx.Mirror), notes))
                    : Find(ctx, p, notes);
                if (profile == null) return Fail($"Profil {p} wurde im Ziel nicht gefunden.", notes);
                profiles.Add(profile);
            }
            var guides = new List<object>();
            foreach (GeometryRef g in r.Guides)
            {
                object? guide = Find(ctx, g, notes);
                if (guide == null) return Fail($"Führungskurve ({g}) wurde nicht neu aufgebaut.", notes);
                guides.Add(guide);
            }
            object? centerline = null;
            if (r.Centerline != null)
            {
                centerline = Find(ctx, r.Centerline, notes);
                if (centerline == null) return Fail($"Mittellinie ({r.Centerline}) wurde nicht neu aufgebaut.", notes);
            }

            Tangency start = r.StartTangency, end = r.EndTangency;
            object? startDir = DirectionOrNone(ctx, r.StartDirection, ref start, "Startbedingung", notes);
            object? endDir = DirectionOrNone(ctx, r.EndDirection, ref end, "Endbedingung", notes);
            bool reverseStart = FirstGuess(ctx, r.Profiles[0], start, r.ReverseStartTangent);
            bool reverseEnd = FirstGuess(ctx, r.Profiles[r.Profiles.Count - 1], end, r.ReverseEndTangent);

            List<List<Vec3>> wantedPicks = r.PickPoints.Select(c => c.Select(ctx.Mirror.Apply).ToList()).ToList();
            Feature? f = Create(ctx, r, profiles, guides, centerline, wantedPicks, createMerge,
                start == Tangency.DirectionVector ? Tangency.None : start,
                end == Tangency.DirectionVector ? Tangency.None : end,
                reverseStart, reverseEnd);
            if (f == null) return Fail("SOLIDWORKS hat die Ausformung nicht erzeugt.", notes);

            // The cut method has no tangent lengths and directions, neither method the guide-curve
            // influence (cut) or "apply to all"; set them with the rest.
            bool needsEdit = r.IsCut || scope || startDir != null || endDir != null || r.GuideTangency.Any(t => t != 0) ||
                             (start != Tangency.None && !r.StartApplyToAll) || (end != Tangency.None && !r.EndApplyToAll);
            if (needsEdit)
            {
                bool ok = FeatureEdit.Loft(ctx.Doc, f, d =>
                {
                    ApplyEnd(d, true, start, startDir, r.StartTangentLength, reverseStart, r.StartApplyToAll, r.StartDraftAngle, r.ReverseStartDraft);
                    ApplyEnd(d, false, end, endDir, r.EndTangentLength, reverseEnd, r.EndApplyToAll, r.EndDraftAngle, r.ReverseEndDraft);
                    if (r.Guides.Count > 0) d.GuideCurveInfluence = r.GuideCurveInfluence;
                    for (int i = 0; i < r.GuideTangency.Count; i++)
                        if (r.GuideTangency[i] != 0) d.SetGuideTangencyType((short)i, (short)r.GuideTangency[i]);
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
                    return Fail("Start-/Endbedingungen oder Feature-Bereich ließen sich nicht setzen; das Feature wurde wieder entfernt.", notes);
                }
            }
            SyncPickPoints(ctx, f, wantedPicks, notes);

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, Describe(r), notes);
            AddAlternatives(result, ctx.Doc, f, r, start, end);
            return result;
        }

        private static Feature? Create(BuildContext ctx, LoftRecord r, List<object> profiles, List<object> guides, object? centerline,
            List<List<Vec3>> picks, bool merge, Tangency start, Tangency end, bool reverseStart, bool reverseEnd)
        {
            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            bool selected = true;
            for (int i = 0; i < profiles.Count; i++)
            {
                Vec3? pick = picks.Count == profiles.Count && picks[i].Count > 0 ? picks[i][0] : (Vec3?)null;
                selected &= SelectProfile(doc, profiles[i], pick, i > 0);
            }
            foreach (object g in guides) selected &= SwSelect.Select(doc, g, GuideMark, true);
            if (centerline != null) selected &= SwSelect.Select(doc, centerline, CenterlineMark, true);
            if (!selected)
            {
                SwSelect.Clear(doc);
                return null;
            }

            FeatureManager fm = doc.FeatureManager;
            Feature? f = r.IsCut
                ? fm.InsertCutBlend(r.Closed, r.MaintainTangency, false, 1.0, (short)start, (short)end,
                    r.IsThin, r.Thickness1, r.Thickness2, (short)r.ThinType, false, true)
                : fm.InsertProtrusionBlend2(r.Closed, r.MaintainTangency, false, 1.0, (short)start, (short)end,
                    r.StartTangentLength, r.EndTangentLength, reverseStart, reverseEnd,
                    r.IsThin, r.Thickness1, r.Thickness2, (short)r.ThinType, merge, false, true, r.GuideCurveInfluence);
            SwSelect.Clear(doc);
            return f;
        }

        /// <summary>
        /// A sketch profile is selected by name at its (mirrored) pick point, which is how
        /// SOLIDWORKS learns where to connect it; faces and points are selected directly.
        /// </summary>
        private static bool SelectProfile(ModelDoc2 doc, object profile, Vec3? pick, bool append)
        {
            if (profile is Feature sketch && pick.HasValue)
            {
                Vec3 p = pick.Value;
                if (doc.Extension.SelectByID2(sketch.Name, "SKETCH", p.X, p.Y, p.Z, append, ProfileMark, null, 0)) return true;
            }
            return SwSelect.Select(doc, profile, ProfileMark, append);
        }

        private static void ApplyEnd(LoftFeatureData d, bool atStart, Tangency t, object? direction, double length, bool reverse,
            bool applyToAll, double draft, bool reverseDraft)
        {
            if (atStart)
            {
                d.StartTangencyType = (short)t;
                if (direction != null)
                {
                    d.StartDirectionVector = direction;
                    d.StartConstraintDraftAngle = draft;
                    d.StartConstraintDraftAngleDirection = reverseDraft;
                }
                if (t != Tangency.None)
                {
                    d.StartTangentLength = length;
                    d.ReverseStartTangentDirection = reverse;
                    d.StartConstraintApplyToAll = applyToAll;
                }
            }
            else
            {
                d.EndTangencyType = (short)t;
                if (direction != null)
                {
                    d.EndDirectionVector = direction;
                    d.EndConstraintDraftAngle = draft;
                    d.EndConstraintDraftAngleDirection = reverseDraft;
                }
                if (t != Tangency.None)
                {
                    d.EndTangentLength = length;
                    d.ReverseEndTangentDirection = reverse;
                    d.EndConstraintApplyToAll = applyToAll;
                }
            }
        }

        /// <summary>"Normal to profile" follows the sketch normal: reversed sketch (σ &lt; 0), reversed flag.</summary>
        private static bool FirstGuess(BuildContext ctx, GeometryRef profile, Tangency t, bool original)
        {
            if (t != Tangency.NormalToProfile || profile.Kind != GeometryRefKind.Sketch) return original;
            return ctx.Sketches.TryGetValue(profile.SketchName, out SketchMap map) && map.Sigma < 0 ? !original : original;
        }

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

        /// <summary>
        /// Compares the pick points of the new loft with the mirrored ones of the original and
        /// sets them where they differ. A failure is reported, the loft stays: the verifier
        /// measures whether the connectors came out right anyway.
        /// </summary>
        private static void SyncPickPoints(BuildContext ctx, Feature f, List<List<Vec3>> wanted, List<string> notes)
        {
            if (wanted.Count == 0) return;
            List<List<Vec3>>? actual = ReadPickPoints(ctx.Doc, f);
            if (actual != null && SamePicks(wanted, actual)) return;
            if (actual == null || actual.Count != wanted.Count || actual.Where((c, i) => c.Count != wanted[i].Count).Any())
            {
                notes.Add("Verbinder der Profile konnten nicht verglichen werden (andere Struktur der Auswahlpunkte).");
                return;
            }
            bool ok = FeatureEdit.Loft(ctx.Doc, f, d =>
            {
                var chains = (object[])d.PickPoints;
                for (int i = 0; i < chains.Length; i++)
                {
                    var points = (object[])chains[i];
                    for (int j = 0; j < points.Length; j++) ((MathPoint)points[j]).ArrayData = wanted[i][j].ToArray();
                }
                d.PickPoints = chains;
            });
            List<List<Vec3>>? after = ok ? ReadPickPoints(ctx.Doc, f) : null;
            if (after == null || !SamePicks(wanted, after))
                notes.Add("Verbinder der Profile ließen sich nicht auf die gespiegelten Auswahlpunkte setzen – Verdrehung prüfen.");
        }

        private static List<List<Vec3>>? ReadPickPoints(ModelDoc2 doc, Feature f)
        {
            var d = (LoftFeatureData)f.GetDefinition();
            if (!d.AccessSelections(doc, null)) return null;
            try
            {
                if (!(d.PickPoints is object[] chains)) return null;
                return chains.Select(c => (c as object[] ?? Array.Empty<object>()).OfType<MathPoint>()
                    .Select(p => Vec3.FromArray((double[])p.ArrayData)).ToList()).ToList();
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        private static bool SamePicks(List<List<Vec3>> a, List<List<Vec3>> b) =>
            a.Count == b.Count && a.Zip(b, (x, y) => x.Count == y.Count && x.Zip(y, (p, q) => p.AlmostEquals(q, 1e-6)).All(ok => ok)).All(ok => ok);

        private static void AddAlternatives(BuildResult result, ModelDoc2 doc, Feature f, LoftRecord r, Tangency start, Tangency end)
        {
            if (LoftRecord.HasSide(start))
                result.Alternatives.Add(new Alternative("Startbedingung umgekehrt",
                    () => FeatureEdit.Loft(doc, f, d => d.ReverseStartTangentDirection = !d.ReverseStartTangentDirection)));
            if (LoftRecord.HasSide(end))
                result.Alternatives.Add(new Alternative("Endbedingung umgekehrt",
                    () => FeatureEdit.Loft(doc, f, d => d.ReverseEndTangentDirection = !d.ReverseEndTangentDirection)));
            if (r.IsThin && IsThinOneSided(r.ThinType))
                result.Alternatives.Add(new Alternative("Wandseite gewechselt",
                    () => FeatureEdit.Loft(doc, f, d => d.ThinWallType = (short)OtherThinSide(d.ThinWallType))));
            if (r.IsThin && HasUnequalWalls(r.ThinType, r.Thickness1, r.Thickness2))
                result.Alternatives.Add(new Alternative("Wandstärken getauscht", () => FeatureEdit.Loft(doc, f, d =>
                {
                    double a = d.GetWallThickness(true), b = d.GetWallThickness(false);
                    d.SetWallThickness(true, b);
                    d.SetWallThickness(false, a);
                })));
        }

        private static string Describe(LoftRecord r)
        {
            string what = (r.IsCut ? "Ausformungsschnitt" : "Ausformung") + (r.IsThin ? " (dünnwandig)" : "");
            string extra = (r.Guides.Count > 0 ? $", {r.Guides.Count} Führungskurve(n)" : "") + (r.Centerline != null ? ", Mittellinie" : "");
            return $"{what}, {r.Profiles.Count} Profile{extra}" + (r.Closed ? ", geschlossen" : "");
        }
    }
}
