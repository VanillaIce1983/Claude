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
    /// Sketched bend (A3 (2) §5.1, blech-plan S4). The mirrored sketch – on the mirrored face it was
    /// drawn on where that is found (K7) – on mark 0, the mirrored fixed face picked by ray at a point
    /// of the side that stays (K8; never without a point), then InsertSheetMetal3dBend with the
    /// original's angle, radius, position (its creation code, BendPos 0 = centre line) and own
    /// allowance (PCBA; A3 P5). Which side stays and which way the bend goes is decided by the
    /// witnesses, always on (A3 P2, K10): for each pick point – the mirrored fixed point's region first,
    /// then one point per other region – the first guess for ReverseDirection, then the other; the first
    /// variant that puts every mirrored witness on the copy is kept, otherwise the best is made again
    /// with a warning. FlipDir may be ignored (B40): the direction is set by an edit and read back;
    /// every edit can reset the allowance (B27), so it is read back and set again after each, and if it
    /// stays reset after the direction edit, the bend is made once more with the direction passed at
    /// creation (the opposite order, A3 P5). Without witnesses the direction is the rule's guess, offered
    /// to the check as „Biegerichtung umgekehrt“.
    /// </summary>
    internal sealed class SketchedBendBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is SketchedBendRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (SketchedBendRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();

            // ---- 1. sketch, position, fixed face – before anything is made (A3 P12)
            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap map)) return Fail($"Skizze „{r.SketchName}“ der Biegung ist im Ziel nicht aufgebaut.", notes);
            if (!(SheetMetalBendRules.BendPosForCreation(r.PositionType) is short pos)) return Fail(SheetMetalBendRules.PositionNotCreatable(r.PositionType), notes);
            if (r.FixedFace == null) return Fail(SheetMetalBendRules.FixedFaceUnreadable, notes);
            Face2? face = BendSupport.FixedFace(ctx, r.FixedFace, "der Biegung", notes, out FaceSignature? fixedSig, out string? why);
            if (face == null) return Fail(why!, notes);
            // A plane will do for a sketched bend (B36 open): made anyway, with a note.
            if (map.SupportFaceMissed != null) notes.Add(SheetMetalBendRules.SketchOnPlane(r.SketchName, map.SupportFaceMissed));

            // ---- 2. pick points – one per region, the original's first – and witnesses
            List<Line3> lines = r.BendLines.Select(ctx.Mirror.Apply).ToList();
            Vec3 n = fixedSig!.Normal;
            double clearance = BendSide.Clearance(r.Thickness);
            Vec3? fixedPoint = r.FixedPoint.HasValue ? ctx.Mirror.Apply(r.FixedPoint.Value) : (Vec3?)null;
            Face2 onFace = face;
            List<Vec3> picks = BendSide.PickOrder(fixedPoint, lines, n, fixedSig.Sample, clearance, BendSupport.Reach(face), p => BendSupport.OnFace(onFace, p));
            if (picks.Count == 0) return Fail("Auf der festen Fläche fand sich kein Punkt abseits der Biegelinien, an dem sie sich wählen ließe.", notes);
            List<Vec3> witnesses = r.Witnesses.Select(ctx.Mirror.Apply).ToList();
            bool witnessed = witnesses.Count > 0;
            if (fixedPoint.HasValue && !r.FixedPointAmbiguous && SheetMetalBendRules.FixedPointWitnesses(fixedPoint.Value, lines, n, clearance))
                witnesses.Add(fixedPoint.Value);

            // ---- 3. the search: per pick point the first guess, then – if witnesses can tell – the other direction
            bool rev0 = SheetMetalBendRules.FirstReverseGuess(r.Reverse, BendSupport.SigmaSheet(ctx, face), map.Sigma);
            var states = new List<BendState> { new BendState(rev0, false, "Richtung nach Regel") };
            if (witnessed) states.Add(new BendState(!rev0, false, SheetMetalBendRules.Turned));
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var tried = new List<string>();
            var atCreation = new Dictionary<string, BendAllowanceData?>(StringComparer.Ordinal);
            Feature? Make(int i, bool flip) => MakeBend(ctx, r, map, picks[i], i, flip, pos, before, tried, atCreation);
            bool Apply(Feature f, BendState s)
            {
                bool ok = SetReverse(ctx, f, s.Reverse);
                // An edit may reset the custom allowance (B27): set again before the witnesses measure.
                Settle(ctx, r, f, new List<string>());
                return ok;
            }
            Feature? bend = BendSupport.Search(ctx, picks, i => Make(i, rev0), states, Apply, f => SwShape.Misses(ctx, witnesses, ctx.SeparateBody),
                before.Names, tried, out BendOutcome<BendState>? best, out BendOutcome<BendState>? chosen);
            if (bend == null)
            {
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                notes.AddRange(tried);
                if (left.Count > 0)
                {
                    notes.Add(FlangeSupport.LeftBehind(left));
                    return Fail("SOLIDWORKS hat die skizzierte Biegung nicht passend erzeugt.", notes);
                }
                if (best == null) return Fail("SOLIDWORKS hat die skizzierte Biegung nicht erzeugt.", notes);
                // None put every witness on the copy: the best again, with a warning.
                bend = Make(best.Pick, best.State.Reverse);
                if (bend == null || !Apply(bend, best.State))
                {
                    List<string> rest = FlangeSupport.RemoveMade(ctx, before.Names);
                    if (rest.Count > 0) notes.Add(FlangeSupport.LeftBehind(rest));
                    return Fail("Die beste Variante der skizzierten Biegung ließ sich nicht wieder anlegen.", notes);
                }
                chosen = best;
                notes.Add(SheetMetalBendRules.Deviates(best.Missed, witnesses.Count));
            }
            else
            {
                if (witnessed) info.Add(SheetMetalBendRules.Confirmed(witnesses.Count, chosen!.State.Reverse != rev0, chosen.Pick > 0 && fixedPoint.HasValue));
                else if (witnesses.Count > 0) info.Add(SheetMetalBendRules.SideConfirmed);
                // Attempts that did not work before one did: information.
                info.AddRange(tried);
            }

            // ---- 4. the allowance; if the direction edit reset it, the opposite order (A3 P5)
            bend = SettleAllowance(ctx, r, bend, chosen!, witnesses, before, atCreation, Make, notes, info);
            if (bend == null)
            {
                List<string> rest = FlangeSupport.RemoveMade(ctx, before.Names);
                if (rest.Count > 0) notes.Add(FlangeSupport.LeftBehind(rest));
                return Fail("Die skizzierte Biegung ließ sich nach dem Setzen der Biegezugabe nicht wieder anlegen.", notes);
            }

            // ---- 5. read-back, bends, side and direction notes
            SketchedBendRecord? got = SwSheetMetal.ReadBackSketchedBend(doc, bend);
            if (got == null) notes.Add("Die skizzierte Biegung ließ sich nicht zurücklesen.");
            else
            {
                Deviations d = SheetMetalBendRules.Differences(r, got);
                notes.AddRange(d.Differences.Select(x => $"Skizzierte Biegung: {x}"));
                if (got.Reverse != chosen!.State.Reverse) notes.Add("Skizzierte Biegung: Die Biegerichtung ließ sich nicht wie gewählt setzen.");
            }
            FlangeSupport.CheckBends(ctx, bend, r.BendCount, SheetMetalBendRules.Parent(r), r.Bends, r.BendOverrides, notes, info);
            bool checkable = SheetMetalBendRules.CheckConfirms(ctx.Options.VerifySteps, ctx.Check == StepCheck.Full || ctx.Check == StepCheck.PassOnly,
                r.ExpectedChange != null && r.OtherBodies.Count == 0);
            if (r.FixedPointAmbiguous) (witnessed ? info : notes).Add(SheetMetalBendRules.AmbiguousFixedPoint);
            if (r.FixedPoint == null) (witnessed ? info : notes).Add(SheetMetalBendRules.FixedPointUnknown);
            if (!witnessed && (r.FixedPoint == null || r.FixedPointAmbiguous)) notes.Add(SheetMetalBendRules.SideUnchecked);
            if (!witnessed && !checkable) notes.Add(SheetMetalBendRules.NoWitnesses);

            ctx.Register(r.Name, bend);
            BuildResult result = Ok(bend, SheetMetalBendRules.Describe(r), notes);
            result.Info.AddRange(info);
            if (!witnessed)
            {
                // The rule's guess only: the check decides (A3 P2).
                Feature b = bend;
                result.MismatchHint = SheetMetalBendRules.NoWitnessesHint;
                result.PassNote = SheetMetalBendRules.DirectionByCheck;
                result.Alternatives.Add(new Alternative(SheetMetalBendRules.Turned,
                    FlangeSupport.KeepingAllowance(ctx, b, r.TypeName, r.OwnAllowance, () => ReadAllowance(doc, b),
                        () => FeatureEdit.SketchedBend(doc, b, d => d.ReverseDirection = !d.ReverseDirection))));
            }
            return result;
        }

        /// <summary>
        /// One bend at a pick point: the sketch on mark 0, the fixed face by ray (append, mark 0), then
        /// InsertSheetMetal3dBend; if that makes nothing, the data object (swFmSketchBend, then
        /// swFmSM3dBend – CADapter found the first returns nothing; cheap to try). Values that read
        /// back otherwise (B41: the position) get one edit without direction and allowance. The allowance
        /// as created is remembered for A3 P5. Null if nothing came of it (what it made is removed).
        /// </summary>
        private static Feature? MakeBend(BuildContext ctx, SketchedBendRecord r, SketchMap map, Vec3 pick, int index, bool flip, short pos,
            SheetMetalSnapshot before, List<string> tried, Dictionary<string, BendAllowanceData?> atCreation)
        {
            ModelDoc2 doc = ctx.Doc;
            string at = $"Punkt {index + 1}";
            Feature? bend = null;
            for (int path = 0; path < 3 && bend == null; path++)
            {
                // Found again for each attempt: an attempt that went may have left the old face object stale.
                Face2? face = BendSupport.FixedFace(ctx, r.FixedFace!, "der Biegung", new List<string>(), out _, out string? why);
                if (face == null)
                {
                    tried.Add($"{at}: {why}");
                    return null;
                }
                SwSelect.Clear(doc);
                if (!SwSelect.Select(doc, BendSupport.SketchNow(ctx, map), 0, false) || !SelectFaceByRay(ctx, face, pick, 0, true))
                {
                    SwSelect.Clear(doc);
                    tried.Add($"{at}: Skizze und feste Fläche ließen sich nicht wählen (Strahl auf die Fläche).");
                    return null;
                }
                try
                {
                    bend = path == 0
                        ? doc.FeatureManager.InsertSheetMetal3dBend(r.Angle, r.UseDefaultRadius, r.Radius, flip, pos, SwSheetMetal.NewAllowance(doc.FeatureManager, r.OwnAllowance))
                        : ByDefinition(ctx, r, path == 1 ? SheetMetalBendRules.SketchBendId : SheetMetalBendRules.SM3dBendId, flip);
                }
                catch (Exception ex)
                {
                    tried.Add($"{at}: {(path == 0 ? "InsertSheetMetal3dBend" : "Datenobjekt")}: {ex.Message}");
                }
                finally
                {
                    SwSelect.Clear(doc);
                    ctx.Topology.Invalidate();
                }
                bend ??= SwSheetMetal.NewSince(doc, before.Names).FirstOrDefault(x => x.GetTypeName2() == "SM3dBend");
                if (bend != null) break;
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                if (left.Count > 0)
                {
                    tried.Add(FlangeSupport.LeftBehind(left));
                    return null;
                }
            }
            if (bend == null)
            {
                tried.Add($"{at}: keine skizzierte Biegung entstanden.");
                return null;
            }
            SketchedBendRecord? got = SwSheetMetal.ReadBackSketchedBend(doc, bend);
            atCreation[bend.Name] = got?.OwnAllowance;
            if (got != null && !SheetMetalBendRules.Differences(r, got).None)
            {
                if (!FeatureEdit.SketchedBend(doc, bend, d => WriteValues(d, r))) tried.Add($"{at}: Werte der Biegung ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            return bend;
        }

        /// <summary>The data-object path: angle, radius, position, direction and allowance set before CreateFeature.</summary>
        private static Feature? ByDefinition(BuildContext ctx, SketchedBendRecord r, int id, bool flip)
        {
            if (!((object)ctx.Doc.FeatureManager.CreateDefinition(id) is SketchedBendFeatureData d)) return null;
            d.BendAngle = r.Angle;
            d.UseDefaultBendRadius = r.UseDefaultRadius;
            if (!r.UseDefaultRadius) d.BendRadius = r.Radius;
            d.PositionType = r.PositionType;
            d.ReverseDirection = flip;
            FeatureEdit.AllowanceInto(d, r.OwnAllowance);
            return ctx.Doc.FeatureManager.CreateFeature(d);
        }

        /// <summary>Angle, radius and position; never the direction (the search sets it) or the allowance (an edit of its own).</summary>
        private static void WriteValues(SketchedBendFeatureData d, SketchedBendRecord r)
        {
            d.BendAngle = r.Angle;
            if (d.UseDefaultBendRadius != r.UseDefaultRadius) d.UseDefaultBendRadius = r.UseDefaultRadius;
            if (!r.UseDefaultRadius) d.BendRadius = r.Radius;
            if (d.PositionType != r.PositionType) d.PositionType = r.PositionType;
        }

        /// <summary>ReverseDirection as wanted: read back, an edit only if it differs (FlipDir may be ignored, B40), read back again.</summary>
        private static bool SetReverse(BuildContext ctx, Feature f, bool wanted)
        {
            SketchedBendRecord? now = SwSheetMetal.ReadBackSketchedBend(ctx.Doc, f);
            if (now == null) return false;
            if (now.Reverse == wanted) return true;
            FeatureEdit.SketchedBend(ctx.Doc, f, d => d.ReverseDirection = wanted);
            ctx.Doc.EditRebuild3();
            ctx.Topology.Invalidate();
            return SwSheetMetal.ReadBackSketchedBend(ctx.Doc, f)?.Reverse == wanted;
        }

        private static (bool ok, BendAllowanceData? own) ReadAllowance(ModelDoc2 doc, Feature f)
        {
            SketchedBendRecord? now = SwSheetMetal.ReadBackSketchedBend(doc, f);
            return (now != null, now?.OwnAllowance);
        }

        /// <summary>The allowance as the original's, by one allowance-only edit if needed (§1.4); its differences into <paramref name="notes"/>.</summary>
        private static void Settle(BuildContext ctx, SketchedBendRecord r, Feature f, List<string> notes) =>
            FlangeSupport.SettleAllowance(ctx, f, r.TypeName, r.OwnAllowance, () => ReadAllowance(ctx.Doc, f), "Skizzierte Biegung", notes);

        /// <summary>
        /// The allowance of the kept bend (A3 P5). Right: done. Wrong, but right as created – the direction
        /// edit reset it, and SOLIDWORKS takes no allowance edit on this bend (CADapter): made once more with
        /// the direction passed at creation, kept if it needs no edit, keeps the allowance and misses no more
        /// witnesses; otherwise the edited one again with the note. Wrong from the start: one allowance edit,
        /// whatever stays is a note. Returns the bend kept (null if it could not be made again).
        /// </summary>
        private static Feature? SettleAllowance(BuildContext ctx, SketchedBendRecord r, Feature bend, BendOutcome<BendState> chosen,
            List<Vec3> witnesses, SheetMetalSnapshot before, Dictionary<string, BendAllowanceData?> atCreation, Func<int, bool, Feature?> make,
            List<string> notes, List<string> info)
        {
            ModelDoc2 doc = ctx.Doc;
            BendAllowanceData? want = r.OwnAllowance;
            var (ok, now) = ReadAllowance(doc, bend);
            if (ok && SheetMetalCompare.Allowance(want, now).None) return bend;
            bool resetByEdit = atCreation.TryGetValue(bend.Name, out BendAllowanceData? created) && SheetMetalCompare.Allowance(want, created).None;
            if (!resetByEdit)
            {
                Settle(ctx, r, bend, notes);
                return bend;
            }
            // The opposite order: the direction at creation, no edit.
            List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
            if (left.Count > 0)
            {
                notes.Add(FlangeSupport.LeftBehind(left));
                return null;
            }
            var scratch = new List<string>();
            Feature? again = make(chosen.Pick, chosen.State.Reverse);
            if (again != null)
            {
                SketchedBendRecord? got = SwSheetMetal.ReadBackSketchedBend(doc, again);
                if (got != null && got.Reverse == chosen.State.Reverse && SheetMetalCompare.Allowance(want, got.OwnAllowance).None &&
                    !BendSupport.InError(again) && SwShape.Misses(ctx, witnesses, ctx.SeparateBody) <= chosen.Missed)
                {
                    info.Add("Biegerichtung beim Anlegen gesetzt: Die Bearbeitung der Richtung setzte die Biegezugabe zurück.");
                    return again;
                }
                List<string> rest = FlangeSupport.RemoveMade(ctx, before.Names);
                if (rest.Count > 0)
                {
                    notes.Add(FlangeSupport.LeftBehind(rest));
                    return null;
                }
            }
            // Back to the edited bend, with the note; one more allowance edit, in case this one takes.
            Feature? edited = make(chosen.Pick, chosen.State.Reverse);
            if (edited == null || !SetReverse(ctx, edited, chosen.State.Reverse)) return null;
            notes.Add(SheetMetalBendRules.AllowanceReset);
            Settle(ctx, r, edited, notes);
            return edited;
        }
    }
}
