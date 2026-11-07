using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Edge flange (A2 phase 2b, blech-plan S3). The profile sketches cannot be rebuilt as free
    /// sketches: SOLIDWORKS makes each on its edge (InsertSketchForEdgeFlange), and the mirrored
    /// profile is written into it.
    ///  1. Hinges – on the job's own bodies with a body of its own – and every reference, before
    ///     anything is made (A2 P12).
    ///  2. The profile plane: InsertSketchForEdgeFlange with the candidates of
    ///     <see cref="FlangeProfiles.AngleCandidates"/> – the sketch angle is a rotation about the
    ///     directed hinge, so the copy needs −θ or θ depending on how its edge runs (A2 P1) – until one
    ///     (angle, FlipDir) pair gives every edge a sketch whose frame takes the mirrored profile
    ///     (<see cref="FlangeProfiles.FitsFrame"/>); every rejected sketch is removed again.
    ///  3. The mirrored profile, rebased onto its hinge (the position offset is SOLIDWORKS's to apply),
    ///     written into each sketch (<see cref="SketchBuilder.ReplaceInto"/>).
    ///  4. The flange by the data object, then InsertSheetMetalEdgeFlange2, for one edge the single-edge
    ///     call (<see cref="EdgeFlangeRules.Paths"/>); an attempt without one sound flange of the
    ///     original's edge count is removed with its sketches before the next. BendAngle is the chosen
    ///     angle, ReverseOffset the chosen FlipDir – never the raw θ (it would turn the profile again).
    ///  5. Values that read back otherwise in one edit (without allowance and direction flags), then the
    ///     two direction flags in an edit of their own (tbcphutai: set together with the binding they
    ///     read back right while the profile still pointed the other way).
    ///  6. The profile check, always on (A2 P2): plane, in-plane content, offset off the hinge; if it
    ///     fails, the other direction states are searched (<see cref="EdgeFlangeRules.DirectionVariants"/>).
    ///  7. The allowance in an edit of its own, the read-back, the bends; the profiles get their original
    ///     names. The direction alternatives go to the check only if the profile check could not confirm.
    /// </summary>
    internal sealed class EdgeFlangeBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is EdgeFlangeRecord;

        /// <summary>The targets of the flange's references; null where the record has none.</summary>
        private sealed class References
        {
            public object? Offset;
            public object? Position;
            public object? Angle;
        }

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (EdgeFlangeRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();
            if (r.Profiles.Count == 0) return Fail("Kantenlasche ohne Profile.", notes);

            // ---- 1. hinges and references, before anything is made
            List<(Edge edge, EdgeSignature signature)>? hinges = FlangeSupport.ResolveEdges(ctx, r.Profiles.Select(p => p.Hinge), "der Kantenlasche", notes, out string? missing);
            if (hinges == null) return Fail(missing!, notes);
            var along = new List<bool>();
            foreach (var (edge, mirrored) in hinges)
            {
                EdgeSignature actual = TopologyCapture.Edge(edge);
                if (actual.Kind != CurveKind.Line) return Fail("Kante der Kantenlasche ist im Ziel nicht gerade.", notes);
                along.Add(FlangeProfiles.Along(actual).Dot(FlangeProfiles.Along(mirrored)) > 0);
            }
            var refs = new References();
            string? why = null;
            if (EdgeFlangeRules.UsesReference(r.OffsetType) && !Resolve(ctx, r.OffsetReference, "Bezug der Laschenlänge", notes, out refs.Offset, out why) ||
                r.UsePositionOffset && EdgeFlangeRules.UsesReference(r.PositionOffsetType) &&
                !Resolve(ctx, r.PositionOffsetReference, "Bezug des Versatzes", notes, out refs.Position, out why))
                return Fail(why!, notes);
            // The angle is known as a number: a reference that cannot be had costs only the reference.
            if (r.AngleReference != null && !Resolve(ctx, r.AngleReference, "Bezug des Laschenwinkels", notes, out refs.Angle, out why))
            {
                refs.Angle = null;
                notes.Add($"{why} {FeatureCapture.AngleReferenceLost}");
            }

            // ---- 2. the profile plane: one (angle, FlipDir) pair for every edge
            List<(double angle, bool flip)> candidates = FlangeProfiles.AngleCandidates(r.BendAngle, along, r.ReverseOffset);
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            var sketches = new List<Feature>();
            string? stuck = null;
            int? chosen = FlangeProfiles.CommonCandidate(candidates.Count, r.Profiles.Count,
                (c, e) => stuck == null && MakeProfilePlane(ctx, r.Profiles[e], hinges[e].edge, candidates[c], sketches),
                c =>
                {
                    sketches.Clear();
                    List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                    if (left.Count > 0) stuck ??= FlangeSupport.LeftBehind(left);
                });
            if (stuck != null)
            {
                notes.Add(stuck);
                return Fail("Die Profilebene der Kantenlasche ließ sich nicht bestimmen.", notes);
            }
            if (chosen == null) return Fail(EdgeFlangeRules.NoProfilePlane(candidates.Count), notes);
            (double angle, bool flip) = candidates[chosen.Value];
            info.Add(EdgeFlangeRules.DescribeCandidate(angle, flip, chosen.Value, candidates.Count));

            // ---- 3. and 4. profiles and flange, path by path; a failed attempt goes with its sketches
            Feature? flange = null;
            EdgeFlangePath used = EdgeFlangePath.Definition;
            List<string> names = new List<string>();
            var failures = new List<string>();
            var tried = new List<string>();
            foreach (EdgeFlangePath path in EdgeFlangeRules.Paths(r.Profiles.Count))
            {
                tried.Add(EdgeFlangeRules.Describe(path));
                if (sketches.Count == 0)
                    for (int e = 0; e < r.Profiles.Count; e++)
                        if (!MakeProfilePlane(ctx, r.Profiles[e], hinges[e].edge, (angle, flip), sketches))
                            return Abandon(ctx, r, before, notes, failures, "Die gewählte Profilebene ließ sich nicht wieder anlegen.");
                names = sketches.Select(s => s.Name).ToList();
                string? drawn = WriteProfiles(ctx, r, sketches, notes);
                if (drawn != null) return Abandon(ctx, r, before, notes, failures, drawn);

                Feature? made = null;
                try
                {
                    made = Create(ctx, path, r, hinges.Select(h => h.edge).ToList(), sketches, refs, angle, flip, failures);
                }
                catch (Exception ex)
                {
                    failures.Add($"{EdgeFlangeRules.Describe(path)}: {ex.Message}");
                }
                finally
                {
                    SwSelect.Clear(doc);
                    ctx.Topology.Invalidate();
                }
                made ??= SwSheetMetal.NewSince(doc, before.Names).FirstOrDefault(x => x.GetTypeName2() == "EdgeFlange");
                if (made != null)
                {
                    string? wrong = Unsound(ctx, r, made);
                    if (wrong == null)
                    {
                        flange = made;
                        used = path;
                        info.Add($"Angelegt über {EdgeFlangeRules.Describe(path)}.");
                        break;
                    }
                    failures.Add($"{EdgeFlangeRules.Describe(path)}: {wrong}; wieder entfernt.");
                }
                else failures.Add($"{EdgeFlangeRules.Describe(path)}: keine Kantenlasche entstanden.");
                foreach (FlangeProfile p in r.Profiles) ctx.Sketches.Remove(p.SketchName);
                sketches.Clear();
                List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
                if (left.Count > 0)
                {
                    notes.AddRange(failures);
                    notes.Add(FlangeSupport.LeftBehind(left));
                    return Fail($"SOLIDWORKS hat die Kantenlasche nicht erzeugt ({string.Join("; ", tried)}).", notes);
                }
            }
            if (flange == null)
            {
                notes.AddRange(failures);
                return Fail($"SOLIDWORKS hat die Kantenlasche nicht erzeugt ({string.Join("; ", tried)}).", notes);
            }
            info.AddRange(failures);
            Feature f = flange;

            // ---- 5. values that read back otherwise (without allowance and direction flags), then the directions on their own
            EdgeFlangeRecord? now = SwSheetMetal.ReadBackEdgeFlange(doc, f, out int _);
            // The insert calls take no references: those come with this edit.
            bool referencesUnset = used != EdgeFlangePath.Definition && (refs.Offset != null || refs.Position != null || refs.Angle != null);
            if (now == null || !EdgeFlangeRules.Differences(r, now).None || referencesUnset)
            {
                if (!FeatureEdit.EdgeFlange(doc, f, d => WriteValues(d, r, refs, angle, now))) notes.Add("Werte der Kantenlasche ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            bool reverse = flip, reversePosition = r.ReversePositionOffset;
            if (!SetDirections(ctx, f, reverse, reversePosition))
                notes.Add("Die Richtungen der Kantenlasche ließen sich nicht in einer eigenen Bearbeitung setzen.");

            // ---- 6. profile check, always on; the other direction states if it fails
            List<ProfileCheck>? checks = CheckProfiles(ctx, r, hinges, names);
            if (checks != null && !checks.All(c => c.Passed))
            {
                bool found = false;
                foreach (var (vr, vp) in EdgeFlangeRules.DirectionVariants(flip, r.ReversePositionOffset, r.UsePositionOffset))
                {
                    if (!SetDirections(ctx, f, vr, vp)) continue;
                    List<ProfileCheck>? again = CheckProfiles(ctx, r, hinges, names);
                    if (again == null || !again.All(c => c.Passed)) continue;
                    reverse = vr;
                    reversePosition = vp;
                    found = true;
                    info.Add($"Richtung durch die Profilprüfung bestimmt: {EdgeFlangeRules.DescribeDirections(vr != flip, vp != r.ReversePositionOffset)}.");
                    break;
                }
                // None fits: back to the state the profile plane was chosen with.
                if (!found) SetDirections(ctx, f, reverse, reversePosition);
            }

            // ---- 7. allowance, read-back, bends, final profile check
            (bool, BendAllowanceData?) ReadAllowance()
            {
                EdgeFlangeRecord? back = SwSheetMetal.ReadBackEdgeFlange(doc, f, out int _);
                return (back != null, back?.OwnAllowance);
            }
            FlangeSupport.SettleAllowance(ctx, f, r.TypeName, r.OwnAllowance, ReadAllowance, "Kantenlasche", notes);
            now = SwSheetMetal.ReadBackEdgeFlange(doc, f, out int _);
            if (now == null) notes.Add("Die Kantenlasche ließ sich nicht zurücklesen.");
            else
            {
                Deviations d = EdgeFlangeRules.Differences(r, now);
                notes.AddRange(d.Differences.Select(x => $"Kantenlasche: {x}"));
                notes.AddRange(d.Notes.Select(x => $"Kantenlasche: {x}"));
            }
            FlangeSupport.CheckBends(ctx, f, r.BendCount, EdgeFlangeRules.Parent(r), r.Bends, r.BendOverrides, notes, info);

            checks = CheckProfiles(ctx, r, hinges, names);
            bool confirmed = checks != null && checks.All(c => c.Passed);
            if (checks == null) notes.Add("Profile der Kantenlasche nicht lesbar; Lage und Richtung sind nicht geprüft.");
            else if (!confirmed) notes.AddRange(checks.SelectMany(c => c.Describe()));
            else info.Add("Profile geprüft: Ebene, Lage und Abstand zur Kante wie gespiegelt.");

            ctx.Register(r.Name, f);
            var natives = new List<Feature>();
            for (int i = 0; i < names.Count; i++)
                if (Live(doc, names[i]) is Feature live)
                {
                    natives.Add(live);
                    ctx.Register(r.Profiles[i].SketchName, live);
                }
            BuildResult result = Ok(f, EdgeFlangeRules.Describe(r), notes);
            result.Info.AddRange(info);
            // A profile SOLIDWORKS shows at the top level stays when the flange is deleted without it.
            result.RemoveHelpers = () =>
            {
                foreach (Feature s in natives)
                    if (FlangeSupport.Live(doc, s) is Feature live)
                        Delete(ctx, live);
            };
            if (!confirmed)
            {
                // The profile check could not confirm a direction: the mass check decides (A2 §7).
                Func<bool> Turn(bool turnReverse, bool turnPosition) =>
                    FlangeSupport.KeepingAllowance(ctx, f, r.TypeName, r.OwnAllowance, ReadAllowance,
                        DirectionToggle(ctx, r, hinges, names, f, () => (reverse, reversePosition), (a, b) =>
                        {
                            reverse = a;
                            reversePosition = b;
                        }, turnReverse, turnPosition));
                result.Alternatives.Add(new Alternative(EdgeFlangeRules.FlangeTurned, Turn(true, false)));
                if (r.UsePositionOffset)
                {
                    result.Alternatives.Add(new Alternative(EdgeFlangeRules.OffsetTurned, Turn(false, true)));
                    result.Alternatives.Add(new Alternative($"{EdgeFlangeRules.FlangeTurned}, {EdgeFlangeRules.OffsetTurned}", Turn(true, true),
                        new[] { EdgeFlangeRules.FlangeTurned, EdgeFlangeRules.OffsetTurned }));
                }
            }
            return result;
        }

        /// <summary>
        /// A reference of the flange in the target (A2 P12): found, and – with a body of its own – on one
        /// of the job's bodies (sheet metal has no feature scope: the copy would end at the original).
        /// Planes, axes and sketch points have no body and are taken as found.
        /// </summary>
        private static bool Resolve(BuildContext ctx, GeometryRef? original, string what, List<string> notes, out object? target, out string? why)
        {
            why = null;
            target = Find(ctx, original, notes);
            if (target == null)
            {
                why = original == null ? $"{what} fehlt." : $"{what} im Ziel nicht gefunden.";
                return false;
            }
            if (ctx.SeparateBody && FlangeSupport.BodyOf(target) is Body2 body && !ctx.IsOwnBody(body))
            {
                why = $"{what} liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// One profile sketch on the copy's edge with a candidate (angle, FlipDir); added to
        /// <paramref name="sketches"/> when it is made, whether it fits or not – the caller removes them.
        /// The sketch may come back open (eyfel): it is closed, keeping whatever SOLIDWORKS drew (B32).
        /// True if its frame takes the mirrored profile (<see cref="FlangeProfiles.FitsFrame"/>).
        /// </summary>
        private static bool MakeProfilePlane(BuildContext ctx, FlangeProfile profile, Edge edge, (double angle, bool flip) candidate, List<Feature> sketches)
        {
            ModelDoc2 doc = ctx.Doc;
            Feature? sketch = null;
            try
            {
                SwSelect.Clear(doc);
                sketch = (object)doc.InsertSketchForEdgeFlange(edge, candidate.angle, candidate.flip) as Feature;
            }
            catch (Exception)
            {
                sketch = null;
            }
            finally
            {
                if (doc.SketchManager.ActiveSketch != null) doc.SketchManager.InsertSketch(true);
                SwSelect.Clear(doc);
                ctx.Topology.Invalidate();
            }
            if (sketch == null) return false;
            sketches.Add(sketch);
            try
            {
                return FlangeProfiles.FitsFrame(profile, ctx.Mirror, ctx.Math.SketchFrame((Sketch)(object)sketch.GetSpecificFeature2()));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The mirrored profiles into the sketches SOLIDWORKS made, each rebased onto its hinge
        /// (<see cref="FlangeProfiles.Rebase"/>); the reason if one fails – then the attempt is given up.
        /// </summary>
        private static string? WriteProfiles(BuildContext ctx, EdgeFlangeRecord r, List<Feature> sketches, List<string> notes)
        {
            for (int i = 0; i < r.Profiles.Count; i++)
            {
                FlangeProfile p = r.Profiles[i];
                try
                {
                    new SketchBuilder(ctx).ReplaceInto(FlangeProfiles.Rebase(p.Sketch, p.Hinge), sketches[i], notes);
                }
                catch (Exception ex)
                {
                    return $"Profil „{p.SketchName}“ ließ sich nicht in die Skizze der Kante schreiben ({ex.InnerException?.Message ?? ex.Message}).";
                }
            }
            return null;
        }

        /// <summary>Removes what the step made and fails with the reason.</summary>
        private static BuildResult Abandon(BuildContext ctx, EdgeFlangeRecord r, SheetMetalSnapshot before, List<string> notes, List<string> failures, string reason)
        {
            foreach (FlangeProfile p in r.Profiles) ctx.Sketches.Remove(p.SketchName);
            List<string> left = FlangeSupport.RemoveMade(ctx, before.Names);
            notes.AddRange(failures);
            if (left.Count > 0) notes.Add(FlangeSupport.LeftBehind(left));
            return Fail(reason, notes);
        }

        /// <summary>One array form for the edges and the profile sketches (STOLPERSTEINE 101, A2 P15).</summary>
        private sealed class ArrayForm
        {
            public ArrayForm(string name, object edges, object sketches)
            {
                Name = name;
                Edges = edges;
                Sketches = sketches;
            }

            public string Name { get; }
            public object Edges { get; }
            public object Sketches { get; }
        }

        /// <summary>
        /// The array forms in the order to try (B33): typed Edge[] with Sketch[] (help example,
        /// tbcphutai) or Feature[] (eyfel) – <paramref name="featuresFirst"/> puts the latter first, as
        /// InsertSheetMetalEdgeFlange2 was seen to take it –, then object[], then DispatchWrapper[].
        /// </summary>
        private static List<ArrayForm> ArrayForms(List<Edge> edges, List<Feature> sketches, bool featuresFirst)
        {
            Edge[] typedEdges = edges.ToArray();
            Feature[] features = sketches.ToArray();
            Sketch[] typedSketches = sketches.Select(s => (Sketch)(object)s.GetSpecificFeature2()).ToArray();
            var withSketches = new ArrayForm("Edge[]/Sketch[]", typedEdges, typedSketches);
            var withFeatures = new ArrayForm("Edge[]/Feature[]", typedEdges, features);
            object items = featuresFirst ? features.Cast<object>().ToArray() : typedSketches.Cast<object>().ToArray();
            object wrapped = featuresFirst
                ? features.Select(x => new DispatchWrapper(x)).ToArray()
                : typedSketches.Select(x => new DispatchWrapper(x)).ToArray();
            return new List<ArrayForm>
            {
                featuresFirst ? withFeatures : withSketches,
                featuresFirst ? withSketches : withFeatures,
                new ArrayForm("object[]", edges.Cast<object>().ToArray(), items),
                new ArrayForm("DispatchWrapper[]", edges.Select(x => new DispatchWrapper(x)).ToArray(), wrapped),
            };
        }

        /// <summary>The flange by one path; what did not work goes to <paramref name="failures"/>. Null if nothing was made.</summary>
        private static Feature? Create(BuildContext ctx, EdgeFlangePath path, EdgeFlangeRecord r, List<Edge> edges, List<Feature> sketches, References refs,
            double angle, bool flip, List<string> failures)
        {
            ModelDoc2 doc = ctx.Doc;
            FeatureManager fm = doc.FeatureManager;
            string label = EdgeFlangeRules.Describe(path);
            if (path == EdgeFlangePath.Definition)
            {
                foreach (ArrayForm form in ArrayForms(edges, sketches, featuresFirst: false))
                {
                    // A fresh data object per form: a refused AddEdges may have left something in the last one.
                    var d = (object)fm.CreateDefinition(EdgeFlangeRules.FeatureId) as EdgeFlangeFeatureData;
                    if (d == null)
                    {
                        failures.Add($"{label}: CreateDefinition liefert keine Kantenlaschen-Daten.");
                        return null;
                    }
                    int code;
                    try
                    {
                        code = d.AddEdges(form.Edges, form.Sketches);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{label}, AddEdges ({form.Name}): {ex.Message}");
                        continue;
                    }
                    if (code != 0)
                    {
                        failures.Add($"{label}, AddEdges ({form.Name}): {EdgeFlangeRules.AddEdgesError(code)}");
                        continue;
                    }
                    WriteValues(d, r, refs, angle, null);
                    d.ReverseOffset = flip;
                    if (r.UsePositionOffset) d.ReversePositionOffset = r.ReversePositionOffset;
                    try
                    {
                        FeatureEdit.AllowanceInto(d, r.OwnAllowance);
                    }
                    catch (Exception)
                    {
                        // Not at creation then: the allowance edit afterwards sets it (§1.4).
                    }
                    Feature? made = fm.CreateFeature(d);
                    if (made == null) failures.Add($"{label} ({form.Name}): CreateFeature liefert nichts.");
                    return made;
                }
                return null;
            }

            ReliefData? relief = r.OwnRelief;
            int options = EdgeFlangeRules.CreationOptions(r, flip);
            int reliefType = (int)(relief?.Kind ?? ReliefKind.Rectangular);
            double ratio = relief?.Ratio ?? 0.5, width = relief?.Width ?? 0, depth = relief?.Depth ?? 0;
            // The edges selected as well: the call was seen to want them after the sketch was made (eyfel).
            void SelectEdges()
            {
                SwSelect.Clear(doc);
                for (int i = 0; i < edges.Count; i++) SwSelect.Select(doc, edges[i], 0, i > 0);
            }
            if (path == EdgeFlangePath.Single)
            {
                SelectEdges();
                Feature? single = fm.InsertSheetMetalEdgeFlange(edges[0], sketches[0], options, angle, r.BendRadius, r.PositionType, r.OffsetDistance,
                    reliefType, ratio, width, depth, r.OffsetDimType, SwSheetMetal.NewAllowance(fm, r.OwnAllowance));
                if (single == null) failures.Add($"{label}: liefert nichts.");
                return single;
            }
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            foreach (ArrayForm form in ArrayForms(edges, sketches, featuresFirst: true))
            {
                Feature? made;
                try
                {
                    SelectEdges();
                    made = fm.InsertSheetMetalEdgeFlange2(form.Edges, form.Sketches, options, angle, r.BendRadius, r.PositionType, r.OffsetDistance,
                        reliefType, ratio, width, depth, r.OffsetDimType, SwSheetMetal.NewAllowance(fm, r.OwnAllowance));
                }
                catch (Exception ex)
                {
                    failures.Add($"{label} ({form.Name}): {ex.Message}");
                    continue;
                }
                made ??= SwSheetMetal.NewSince(doc, before.Names).FirstOrDefault(x => x.GetTypeName2() == "EdgeFlange");
                if (made != null) return made;
                failures.Add($"{label} ({form.Name}): liefert nichts.");
            }
            return null;
        }

        /// <summary>Why a new flange is not this one: a rebuild error, unreadable data, another number of edges; null if sound.</summary>
        private static string? Unsound(BuildContext ctx, EdgeFlangeRecord r, Feature f)
        {
            int code = f.GetErrorCode2(out bool warning);
            if (code != 0 && !warning) return $"Neuaufbaufehler (Code {code})";
            EdgeFlangeRecord? got = SwSheetMetal.ReadBackEdgeFlange(ctx.Doc, f, out int count);
            if (got == null) return "nicht zurückzulesen";
            if (count >= 0 && count != r.Profiles.Count)
                return $"Kantenlasche mit {count} statt {r.Profiles.Count} Kante{(r.Profiles.Count == 1 ? "" : "n")}";
            return null;
        }

        /// <summary>
        /// Every value the flange uses, its references and – late-bound – what only newer versions have;
        /// never the allowance (an edit of its own) or the direction flags (an edit of their own). With
        /// <paramref name="now"/> (the read-back of an existing flange) the bend angle is written only if it
        /// reads otherwise, folded: writing it again turned the profile once more (tbcphutai). Up to edge and
        /// merge: LockAngle off before the angle, as the help says, and back afterwards.
        /// </summary>
        private static void WriteValues(EdgeFlangeFeatureData d, EdgeFlangeRecord r, References refs, double angle, EdgeFlangeRecord? now)
        {
            bool lockedMerge = r.OffsetType == FlangeOffsetTypes.UptoEdgeAndMerge && r.LockAngle == true;
            if (now == null || Math.Abs(SheetMetalCompare.Fold(now.BendAngle) - SheetMetalCompare.Fold(angle)) > SheetMetalCompare.AngleTolerance)
            {
                if (lockedMerge) SwLate.Set(d, "LockAngle", false);
                d.BendAngle = angle;
            }
            d.UseDefaultBendRadius = r.UseDefaultRadius;
            if (!r.UseDefaultRadius) d.BendRadius = r.BendRadius;
            d.OffsetType = r.OffsetType;
            if (EdgeFlangeRules.UsesDistance(r.OffsetType)) d.OffsetDistance = r.OffsetDistance;
            if (EdgeFlangeRules.UsesDimType(r.OffsetType)) d.OffsetDimType = r.OffsetDimType;
            if (refs.Offset != null) d.OffsetReference = refs.Offset;
            d.PositionType = r.PositionType;
            d.UsePositionTrimSideBends = r.TrimSideBends;
            d.UsePositionOffset = r.UsePositionOffset;
            if (r.UsePositionOffset)
            {
                d.PositionOffsetType = r.PositionOffsetType;
                if (EdgeFlangeRules.UsesDistance(r.PositionOffsetType)) d.PositionOffsetDistance = r.PositionOffsetDistance;
                if (refs.Position != null) d.PositionOffsetReference = refs.Position;
            }
            d.GapDistance = r.GapDistance;
            d.UseDefaultBendRelief = r.OwnRelief == null;
            if (r.OwnRelief != null)
            {
                d.AutoReliefType = (int)r.OwnRelief.Kind;
                if (r.OwnRelief.TearType > 0) d.ReliefTearType = r.OwnRelief.TearType;
                d.UseReliefRatio = r.OwnRelief.UseRatio;
                if (r.OwnRelief.UseRatio) d.ReliefRatio = r.OwnRelief.Ratio;
                else
                {
                    d.ReliefWidth = r.OwnRelief.Width;
                    d.ReliefDepth = r.OwnRelief.Depth;
                }
            }
            if (r.NormalToFlangePlane.HasValue) SwLate.Set(d, "NormalToFlangePlane", r.NormalToFlangePlane.Value);
            if (r.PerpendicularToFace.HasValue) SwLate.Set(d, "PerpendicularToFace", r.PerpendicularToFace.Value);
            if (refs.Angle != null) SwLate.Set(d, "AngleReference", refs.Angle);
            if (r.LockAngle.HasValue && r.OffsetType == FlangeOffsetTypes.UptoEdgeAndMerge) SwLate.Set(d, "LockAngle", r.LockAngle.Value);
        }

        /// <summary>
        /// The two direction flags in an edit of their own (tbcphutai, B34): set together with the
        /// binding they read back right while the profile still pointed the other way. Rebuilt with
        /// force – the change moves the planes of the absorbed profiles.
        /// </summary>
        private static bool SetDirections(BuildContext ctx, Feature f, bool reverseOffset, bool reversePositionOffset)
        {
            bool ok = FeatureEdit.EdgeFlange(ctx.Doc, f, d =>
            {
                d.ReverseOffset = reverseOffset;
                if (d.UsePositionOffset) d.ReversePositionOffset = reversePositionOffset;
            });
            try
            {
                ctx.Doc.ForceRebuild3(false);
            }
            catch (Exception)
            {
                ctx.Doc.EditRebuild3();
            }
            ctx.Topology.Invalidate();
            return ok;
        }

        private static Feature? Live(ModelDoc2 doc, string name)
        {
            try
            {
                return SwDoc.FeatureByName(doc, name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The copy's profiles as they stand, each against the mirrored original's (<see cref="FlangeProfiles.Check"/>); null if one does not read.</summary>
        private static List<ProfileCheck>? CheckProfiles(BuildContext ctx, EdgeFlangeRecord r, List<(Edge edge, EdgeSignature signature)> hinges, List<string> names)
        {
            var capture = new SketchCapture(ctx.Math);
            var result = new List<ProfileCheck>();
            try
            {
                for (int i = 0; i < r.Profiles.Count; i++)
                {
                    if (!(Live(ctx.Doc, names[i]) is Feature live)) return null;
                    result.Add(FlangeProfiles.Check(r.Profiles[i], ctx.Mirror, capture.Read(live), hinges[i].signature));
                }
            }
            catch (Exception)
            {
                return null;
            }
            return result;
        }

        /// <summary>Every profile point of the copy as it stands (model coordinates); null if one does not read.</summary>
        private static List<Vec3>? ProfileState(BuildContext ctx, List<string> names)
        {
            var capture = new SketchCapture(ctx.Math);
            var points = new List<Vec3>();
            try
            {
                foreach (string name in names)
                {
                    if (!(Live(ctx.Doc, name) is Feature live)) return null;
                    points.AddRange(FlangeProfiles.ProfilePoints(capture.Read(live)));
                }
            }
            catch (Exception)
            {
                return null;
            }
            return points;
        }

        /// <summary>
        /// A direction alternative for the check (A2 §7): the first call turns the flags, the second turns
        /// them back and is true only if the profiles are where they were – ModifyDefinition on a
        /// regenerated profile was not idempotent (tbcphutai); a toggle that does not restore says so.
        /// </summary>
        private static Func<bool> DirectionToggle(BuildContext ctx, EdgeFlangeRecord r, List<(Edge edge, EdgeSignature signature)> hinges, List<string> names,
            Feature f, Func<(bool, bool)> state, Action<bool, bool> setState, bool turnReverse, bool turnPosition)
        {
            List<Vec3>? baseline = null;
            bool applied = false;
            return () =>
            {
                var (reverse, position) = state();
                reverse ^= turnReverse;
                position ^= turnPosition;
                setState(reverse, position);
                if (!applied)
                {
                    baseline = ProfileState(ctx, names);
                    applied = true;
                    return SetDirections(ctx, f, reverse, position);
                }
                applied = false;
                if (!SetDirections(ctx, f, reverse, position)) return false;
                List<Vec3>? back = ProfileState(ctx, names);
                return baseline != null && back != null && FlangeProfiles.InPlaneDeviation(baseline, back) <= Tol.Length;
            };
        }
    }
}
