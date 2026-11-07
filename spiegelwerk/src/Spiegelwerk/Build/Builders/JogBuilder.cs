using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Jog (A2 phase 2c, P6, P7, blech-plan S4). Its sketch must be on the mirrored face it was drawn on
    /// (B36: a jog takes no sketch on a plane – it fails with a reason otherwise). The line on mark 0, the
    /// fixed face picked by ray beside the line on the side that stays (K8; never without a point), then
    /// IModelDoc2.InsertSheetMetalJog – the only call, returning nothing: the new jog is what is new
    /// since the snapshot. The angle goes in degrees (B42); if nothing comes of it, in radians, then
    /// with the sketch selected instead of the line. The call knows only a blind offset: type, reference
    /// and the other values come with one edit, the allowance with one of its own (a bend table is not
    /// settable on a jog, P7), and both flags with their own. Side and direction are decided by the
    /// witnesses as for the sketched bend (K10): per side the first guess for ReverseDirection, the
    /// other, then the same with ReverseOffset turned; the jog that is kept is measured once more, with its
    /// allowance, for the report. Without witnesses the check gets the flags as variants.
    /// </summary>
    internal sealed class JogBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is JogRecord;

        /// <summary>The ways the insert call is tried: line or sketch selected, angle in degrees or radians.</summary>
        private static readonly (bool line, bool degrees, string name)[] Attempts =
        {
            (true, true, "Linie, Winkel in Grad"),
            (true, false, "Linie, Winkel im Bogenmaß"),
            (false, true, "Skizze, Winkel in Grad"),
        };

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (JogRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();

            // ---- 1. sketch on its face, line, codes, fixed face and reference – before anything is made
            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap map)) return Fail($"Skizze „{r.SketchName}“ der Kröpfung ist im Ziel nicht aufgebaut.", notes);
            if (r.SketchFace != null && !map.OnSupportFace) return Fail(JogRules.SketchNotOnFace(r.SketchName, map.SupportFaceMissed), notes);
            if (!map.Segments.ContainsKey(r.LineId)) return Fail("Die Linie der Kröpfung fehlt in der gespiegelten Skizze.", notes);
            if (!(JogRules.DimPos(r.DimensionPosition) is short dimPos) || !(JogRules.BendPos(r.JogPosition) is short bendPos))
                return Fail($"Bemaßungs- oder Kröpfungsposition ({r.DimensionPosition}/{r.JogPosition}) lässt sich über die API nicht anlegen.", notes);
            if (r.FixedFace == null || r.Line == null) return Fail(JogRules.FixedFaceUnreadable, notes);
            Face2? face = BendSupport.FixedFace(ctx, r.FixedFace, "der Kröpfung", notes, out FaceSignature? fixedSig, out string? why);
            if (face == null) return Fail(why!, notes);
            object? reference = null;
            if (JogRules.UsesReference(r.OffsetType))
            {
                reference = Find(ctx, r.OffsetReference, notes);
                if (reference == null) return Fail("Bezug des Versatzes der Kröpfung im Ziel nicht gefunden.", notes);
                if (ctx.SeparateBody && FlangeSupport.BodyOf(reference) is Body2 body && !ctx.IsOwnBody(body))
                    return Fail("Bezug des Versatzes der Kröpfung liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.", notes);
            }

            // ---- 2. pick points – the side of the mirrored fixed point first – and witnesses
            var lines = new List<Line3> { ctx.Mirror.Apply(r.Line.Value) };
            Vec3 n = fixedSig!.Normal;
            double clearance = BendSide.Clearance(r.Thickness);
            Vec3? fixedPoint = r.FixedPoint.HasValue ? ctx.Mirror.Apply(r.FixedPoint.Value) : (Vec3?)null;
            Face2 onFace = face;
            List<Vec3> picks = BendSide.PickOrder(fixedPoint, lines, n, fixedSig.Sample, clearance, BendSupport.Reach(face), p => BendSupport.OnFace(onFace, p));
            if (picks.Count == 0) return Fail("Auf der festen Fläche fand sich kein Punkt abseits der Kröpfungslinie, an dem sie sich wählen ließe.", notes);
            // The moved faces only if the copy has every earlier feature of the body, without a rebuild error (K10).
            List<Vec3> witnesses = SheetMetalBendRules.WitnessesFor(r.Witnesses, r.MovedWitnesses, r.BodyFeatures, x => BendSupport.Rebuilt(ctx, x),
                out bool movedLeftOut).Select(ctx.Mirror.Apply).ToList();
            if (movedLeftOut) info.Add(SheetMetalBendRules.OwnWitnessesOnlyFor(r.BodyFeatures));
            bool witnessed = witnesses.Count > 0;
            // The fixed point only where it stayed on a face of the original after the jog (K10).
            if (fixedPoint.HasValue && r.FixedPointStays) witnesses.Add(fixedPoint.Value);

            // ---- 3. the search
            bool rev0 = SheetMetalBendRules.FirstReverseGuess(r.ReverseDirection, BendSupport.SigmaSheet(ctx, face), map.Sigma);
            bool off0 = r.ReverseOffset;
            var states = new List<BendState> { new BendState(rev0, off0, "Richtung nach Regel") };
            if (witnessed)
            {
                states.Add(new BendState(!rev0, off0, JogRules.Turned));
                states.Add(new BendState(rev0, !off0, JogRules.OffsetTurned));
                states.Add(new BendState(!rev0, !off0, $"{JogRules.Turned}, {JogRules.OffsetTurned}"));
            }
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            int working = 0;
            Feature? Make(int i, bool flip) => MakeJog(ctx, r, map, picks[i], i, flip, dimPos, bendPos, reference, before, tried, ref working);
            bool Apply(Feature f, BendState s)
            {
                bool ok = SetFlags(ctx, f, s.Reverse, s.ReverseOffset);
                // An edit may reset the custom allowance (B27): set again before the witnesses measure.
                Settle(ctx, r, f, new List<string>());
                return ok;
            }
            Feature? jog = BendSupport.Search(ctx, picks, i => Make(i, rev0), states, Apply, f => SwShape.Misses(ctx, witnesses, ctx.SeparateBody),
                before.Names, tried, out BendOutcome<BendState>? best, out BendOutcome<BendState>? chosen);
            if (jog == null)
            {
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                notes.AddRange(tried);
                if (left.Count > 0)
                {
                    notes.Add(FlangeSupport.LeftBehind(left));
                    return Fail("SOLIDWORKS hat die Kröpfung nicht passend erzeugt.", notes);
                }
                if (best == null) return Fail("SOLIDWORKS hat die Kröpfung nicht erzeugt.", notes);
                jog = Make(best.Pick, best.State.Reverse);
                if (jog == null || !Apply(jog, best.State))
                {
                    List<string> rest = FlangeSupport.RemoveMade(ctx, before.Names);
                    if (rest.Count > 0) notes.Add(FlangeSupport.LeftBehind(rest));
                    return Fail("Die beste Variante der Kröpfung ließ sich nicht wieder anlegen.", notes);
                }
                // Made by another path than the one measured (the direction at creation): what it misses is measured on it below.
                chosen = best;
            }
            else info.AddRange(tried);
            info.Add($"Angelegt über InsertSheetMetalJog ({Attempts[working].name}).");

            // ---- 4. allowance (an edit of its own; a table is not attempted), read-back, bends
            Settle(ctx, r, jog, notes);
            // The witnesses on the jog that is kept, with its allowance: the best may have been made again.
            var (off, confirmed) = SheetMetalBendRules.WitnessOutcome(witnesses.Count, SwShape.Misses(ctx, witnesses, ctx.SeparateBody), BendSupport.InError(jog),
                witnessed, chosen!.State.Reverse != rev0 || chosen.State.ReverseOffset != off0, chosen.Pick > 0 && fixedPoint.HasValue, JogRules.Deviates);
            if (off != null) notes.Add(off);
            if (confirmed != null) info.Insert(0, confirmed);
            JogRecord? got = SwSheetMetal.ReadBackJog(doc, jog);
            if (got == null) notes.Add("Die Kröpfung ließ sich nicht zurücklesen.");
            else
            {
                notes.AddRange(JogRules.Differences(WithFlags(r, chosen!.State), got).Differences.Select(x => $"Kröpfung: {x}"));
                if (got.ReverseDirection != chosen.State.Reverse) notes.Add("Kröpfung: Die Richtung ließ sich nicht wie gewählt setzen.");
            }
            FlangeSupport.CheckBends(ctx, jog, r.BendCount, JogRules.Parent(r), r.Bends, r.BendOverrides, notes, info);
            bool checkable = SheetMetalBendRules.CheckConfirms(ctx.Options.VerifySteps, ctx.Check == StepCheck.Full || ctx.Check == StepCheck.PassOnly,
                r.ExpectedChange != null && r.OtherBodies.Count == 0);
            if (r.FixedPoint == null) (witnessed ? info : notes).Add(JogRules.FixedSideUnknown);
            if (!witnessed && r.FixedPoint == null) notes.Add(JogRules.SideUnchecked);
            if (!witnessed && !checkable) notes.Add(JogRules.NoWitnesses);

            ctx.Register(r.Name, jog);
            BuildResult result = Ok(jog, JogRules.Describe(r), notes);
            result.Info.AddRange(info);
            if (!witnessed)
            {
                // The rule's guess only: the check decides, as A2 §7 offers it.
                Feature j = jog;
                result.MismatchHint = JogRules.NoWitnessesHint;
                result.PassNote = JogRules.DirectionByCheck;
                Func<bool> Toggle(bool direction, bool offset) => FlangeSupport.KeepingAllowance(ctx, j, r.TypeName, r.OwnAllowance, () => ReadAllowance(doc, j),
                    () => FeatureEdit.Jog(doc, j, d =>
                    {
                        if (direction) d.ReverseDirection = !d.ReverseDirection;
                        if (offset) d.ReverseOffset = !d.ReverseOffset;
                    }));
                result.Alternatives.Add(new Alternative(JogRules.Turned, Toggle(true, false)));
                result.Alternatives.Add(new Alternative(JogRules.OffsetTurned, Toggle(false, true)));
                result.Alternatives.Add(new Alternative($"{JogRules.Turned}, {JogRules.OffsetTurned}", Toggle(true, true), new[] { JogRules.Turned, JogRules.OffsetTurned }));
            }
            return result;
        }

        /// <summary>
        /// One jog at a pick point, by the attempts in order from the one that worked last
        /// (<paramref name="working"/>): line or sketch on mark 0, the fixed face by ray (append, mark 0),
        /// InsertSheetMetalJog with the offset the call knows (blind), then the values in one edit
        /// (type, reference, distance, positions, radius; not the flags, not the allowance). Null if
        /// nothing came of it (what it made is removed).
        /// </summary>
        private static Feature? MakeJog(BuildContext ctx, JogRecord r, SketchMap map, Vec3 pick, int index, bool flip, short dimPos, short bendPos,
            object? reference, SheetMetalSnapshot before, List<string> tried, ref int working)
        {
            ModelDoc2 doc = ctx.Doc;
            string at = $"Punkt {index + 1}";
            Feature? jog = null;
            for (int k = 0; k < Attempts.Length && jog == null; k++)
            {
                int a = (working + k) % Attempts.Length;
                Face2? face = BendSupport.FixedFace(ctx, r.FixedFace!, "der Kröpfung", new List<string>(), out _, out string? why);
                if (face == null)
                {
                    tried.Add($"{at}: {why}");
                    return null;
                }
                SwSelect.Clear(doc);
                object first = Attempts[a].line && map.Segments.TryGetValue(r.LineId, out SketchSegment segment) ? segment : BendSupport.SketchNow(ctx, map);
                bool selected;
                try
                {
                    selected = SwSelect.Select(doc, first, 0, false) && SelectFaceByRay(ctx, face, pick, 0, true);
                }
                catch (Exception)
                {
                    selected = false;
                }
                if (!selected)
                {
                    SwSelect.Clear(doc);
                    tried.Add($"{at} ({Attempts[a].name}): Linie und feste Fläche ließen sich nicht wählen.");
                    continue;
                }
                try
                {
                    doc.InsertSheetMetalJog(JogRules.CreationAngle(r.Angle, Attempts[a].degrees), r.Radius, JogRules.CreationOffset(r), flip, r.FixProjectedLength,
                        dimPos, bendPos);
                }
                catch (Exception ex)
                {
                    tried.Add($"{at} ({Attempts[a].name}): {ex.Message}");
                }
                finally
                {
                    SwSelect.Clear(doc);
                    ctx.Topology.Invalidate();
                }
                jog = SwSheetMetal.NewSince(doc, before.Names).FirstOrDefault(x => x.GetTypeName2() == "Jog");
                if (jog != null)
                {
                    working = a;
                    break;
                }
                tried.Add($"{at} ({Attempts[a].name}): keine Kröpfung entstanden.");
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                if (left.Count > 0)
                {
                    tried.Add(FlangeSupport.LeftBehind(left));
                    return null;
                }
            }
            if (jog == null) return null;
            JogRecord? got = SwSheetMetal.ReadBackJog(doc, jog);
            if (got == null || !JogRules.Differences(WithFlags(r, new BendState(got.ReverseDirection, got.ReverseOffset, "")), got).None || JogRules.NeedsOffsetEdit(r))
            {
                if (!FeatureEdit.Jog(doc, jog, d => WriteValues(d, r, reference))) tried.Add($"{at}: Werte der Kröpfung ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            return jog;
        }

        /// <summary>
        /// The record as wanted with the flags of a state: the read-back compares ReverseOffset (with a
        /// reference) against the chosen one, not the original's – the search may have turned it.
        /// </summary>
        private static JogRecord WithFlags(JogRecord r, BendState s) => new JogRecord(r.Name, r.TypeName, r.SketchName)
        {
            UseDefaultRadius = r.UseDefaultRadius,
            Radius = r.Radius,
            Angle = r.Angle,
            OffsetType = r.OffsetType,
            OffsetDistance = r.OffsetDistance,
            ReverseOffset = s.ReverseOffset,
            ReverseDirection = s.Reverse,
            DimensionPosition = r.DimensionPosition,
            FixProjectedLength = r.FixProjectedLength,
            JogPosition = r.JogPosition,
        };

        /// <summary>The values the insert call does not take or set otherwise; never the two flags (the search) or the allowance.</summary>
        private static void WriteValues(JogFeatureData d, JogRecord r, object? reference)
        {
            if (d.UseDefaultBendRadius != r.UseDefaultRadius) d.UseDefaultBendRadius = r.UseDefaultRadius;
            if (!r.UseDefaultRadius) d.BendRadius = r.Radius;
            d.JogAngle = r.Angle;
            if (d.OffsetType != r.OffsetType) d.OffsetType = r.OffsetType;
            if (reference != null) d.OffsetReference = reference;
            if (JogRules.UsesDistance(r.OffsetType)) d.OffsetDistance = r.OffsetDistance;
            if (d.DimensionPositionType != r.DimensionPosition) d.DimensionPositionType = r.DimensionPosition;
            if (d.FixProjectedLength != r.FixProjectedLength) d.FixProjectedLength = r.FixProjectedLength;
            if (d.JogPositionType != r.JogPosition) d.JogPositionType = r.JogPosition;
        }

        /// <summary>Both flags as wanted, in an edit of their own only if one differs; read back.</summary>
        private static bool SetFlags(BuildContext ctx, Feature f, bool reverse, bool reverseOffset)
        {
            JogRecord? now = SwSheetMetal.ReadBackJog(ctx.Doc, f);
            if (now == null) return false;
            if (now.ReverseDirection == reverse && now.ReverseOffset == reverseOffset) return true;
            FeatureEdit.Jog(ctx.Doc, f, d =>
            {
                if (d.ReverseDirection != reverse) d.ReverseDirection = reverse;
                if (d.ReverseOffset != reverseOffset) d.ReverseOffset = reverseOffset;
            });
            ctx.Doc.EditRebuild3();
            ctx.Topology.Invalidate();
            JogRecord? back = SwSheetMetal.ReadBackJog(ctx.Doc, f);
            return back != null && back.ReverseDirection == reverse && back.ReverseOffset == reverseOffset;
        }

        private static (bool ok, BendAllowanceData? own) ReadAllowance(ModelDoc2 doc, Feature f)
        {
            JogRecord? now = SwSheetMetal.ReadBackJog(doc, f);
            return (now != null, now?.OwnAllowance);
        }

        private static void Settle(BuildContext ctx, JogRecord r, Feature f, List<string> notes) =>
            FlangeSupport.SettleAllowance(ctx, f, r.TypeName, r.OwnAllowance, () => ReadAllowance(ctx.Doc, f), "Kröpfung", notes);
    }
}
