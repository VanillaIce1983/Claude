using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// The profile of an extrude or revolve in the mirrored sketch: the contours and regions that
    /// stand for the original's selection, and what a created feature must show it uses.
    /// </summary>
    internal sealed class Profile
    {
        public List<SketchContour> Contours { get; } = new List<SketchContour>();

        /// <summary>Segment ids (of the new sketch) of each contour, for the read-back.</summary>
        public List<HashSet<string>> ContourIds { get; } = new List<HashSet<string>>();

        public List<SketchRegion> Regions { get; } = new List<SketchRegion>();

        /// <summary>A point inside each region (target sketch coordinates), same order as <see cref="Regions"/>.</summary>
        public List<Vec3> Witnesses { get; } = new List<Vec3>();

        /// <summary>ε the regions are read with (the record's).</summary>
        public double Epsilon { get; set; }

        /// <summary>A region of the copy differs from the original's and the containing one stands in (RegionMatch.Inexact).</summary>
        public bool Inexact { get; set; }

        /// <summary>Report notes: split, merged or differing regions.</summary>
        public List<string> Notes { get; } = new List<string>();

        public IEnumerable<object> Items => Contours.Cast<object>().Concat(Regions);
    }

    /// <summary>What <see cref="ProfileSupport.Create"/> produced.</summary>
    internal sealed class ProfileOutcome
    {
        /// <summary>The created feature; null if SOLIDWORKS created none on any way.</summary>
        public Feature? Feature { get; set; }

        /// <summary>The profile the feature verifiably uses; null: the whole sketch.</summary>
        public Profile? Used { get; set; }

        /// <summary>A profile was wanted but the feature uses the whole sketch: why.</summary>
        public string? Lost { get; set; }

        /// <summary>The way that handed the profile over (for the report).</summary>
        public string? Way { get; set; }

        /// <summary>Ways whose feature did not take the profile, with what the read-back found.</summary>
        public List<string> Rejected { get; } = new List<string>();
    }

    /// <summary>
    /// Selected contours and sketch regions of extrudes and revolves. Contours are found in the
    /// new sketch by their segments, regions by interior witnesses and a geometric identity
    /// test (Core: RegionMatcher). The feature is created in the ways of
    /// <see cref="ProfileRules.Ways"/>; every feature that comes of it is read back
    /// (<see cref="Check"/>), and one that ignored or misread the selection is deleted before
    /// the next way – a feature built from the whole sketch is never kept as if it used the
    /// selection. Nothing here throws: an unreadable region gives a reason, the builder then
    /// applies the fallback policy (ProfileRules).
    /// </summary>
    internal static class ProfileSupport
    {
        /// <summary>The original built from selected contours or regions, read or not.</summary>
        public static bool Wanted(IProfileRecord r) => r.Contours.Count > 0 || r.Regions.Count > 0 || r.SelectionLost;

        /// <summary>
        /// The fallback policy before anything is created (and before references add helper
        /// geometry): in the same part without a body of its own, a profile that cannot be
        /// carried over exactly – unreadable in the original, not found in the copy, a larger
        /// containing region – refuses the feature. Returns the failure message, or null.
        /// </summary>
        public static string? Plan(BuildContext ctx, SketchMap sketch, IProfileRecord r, List<string> notes)
        {
            if (!Wanted(r) || !ProfileRules.RefusesInexact(ctx.SamePart, ctx.SeparateBody)) return null;
            string? why = ProfileRules.LostInCapture;
            Profile? p = r.SelectionLost ? null : Map(ctx, sketch, r, out why);
            if (p != null && !p.Inexact) return null;
            if (p != null) notes.AddRange(p.Notes);
            else notes.Add(why + ".");
            return ProfileRules.Refusal;
        }

        /// <summary>
        /// The fallback policy after creation: notes on what the feature uses (split, merged or
        /// differing regions; the whole sketch instead of the selection, with the ways that were
        /// rejected). Returns the failure message if the feature must go again (same part
        /// without a body of its own and not exactly the original's profile), or null.
        /// </summary>
        public static string? Finish(BuildContext ctx, ProfileOutcome o, bool wanted, List<string> notes)
        {
            if (!wanted || o.Feature == null) return null;
            bool refuses = ProfileRules.RefusesInexact(ctx.SamePart, ctx.SeparateBody);
            if (o.Rejected.Count > 0 && o.Used == null) notes.Add("Nicht übernommen: " + string.Join("; ", o.Rejected) + ".");
            if (o.Used == null)
            {
                string why = o.Lost ?? ProfileRules.NotTaken;
                if (refuses)
                {
                    notes.Add(why + ".");
                    return ProfileRules.Refusal;
                }
                notes.Add(ProfileRules.WholeSketch(why));
                return null;
            }
            notes.AddRange(o.Used.Notes);
            return o.Used.Inexact && refuses ? ProfileRules.Refusal : null;
        }

        /// <summary>Report text on the profile a feature verifiably uses, for its message; empty without one.</summary>
        public static string Describe(ProfileOutcome o)
        {
            if (o.Used == null) return "";
            int c = o.Used.Contours.Count, g = o.Used.Regions.Count;
            var parts = new List<string>();
            if (c > 0) parts.Add(c == 1 ? "1 Kontur" : $"{c} Konturen");
            if (g > 0) parts.Add(g == 1 ? "1 Skizzenbereich" : $"{g} Skizzenbereiche");
            return $", Profil: {string.Join(" und ", parts)} ({o.Way})";
        }

        /// <summary>
        /// The original's selection in the new sketch; null with the reason if a contour or
        /// region is not found there or anything cannot be read. Region objects are read fresh
        /// on every call: after a trial feature was deleted they may be stale.
        /// </summary>
        public static Profile? Map(BuildContext ctx, SketchMap sketch, IProfileRecord r, out string? why)
        {
            why = null;
            if (r.Contours.Count == 0 && r.Regions.Count == 0)
            {
                why = ProfileRules.LostInCapture;
                return null;
            }
            try
            {
                var p = new Profile { Epsilon = r.RegionChordTolerance };
                if (r.Contours.Count > 0)
                {
                    var available = ((object)sketch.Sketch.GetSketchContours() as object[] ?? Array.Empty<object>()).OfType<SketchContour>()
                        .Select(c => (contour: c, ids: new HashSet<string>(((object)c.GetSketchSegments() as object[] ?? Array.Empty<object>())
                            .OfType<SketchSegment>().Select(SwDoc.IdOf))))
                        .ToList();
                    foreach (List<string> wanted in r.Contours)
                    {
                        var newIds = new HashSet<string>(wanted.Where(sketch.Segments.ContainsKey).Select(id => SwDoc.IdOf(sketch.Segments[id])));
                        var match = available.FirstOrDefault(a => a.ids.SetEquals(newIds));
                        if (match.contour == null)
                        {
                            why = "Eine ausgewählte Kontur wurde in der neuen Skizze nicht gefunden";
                            return null;
                        }
                        p.Contours.Add(match.contour);
                        p.ContourIds.Add(newIds);
                    }
                }
                if (r.Regions.Count > 0)
                {
                    Frame3 target = sketch.Mirrored.Sketch.Frame;
                    List<(SketchRegion Region, ProfileRegion Shape)> all =
                        RegionCapture.ReadAll(sketch.Sketch, target, r.RegionChordTolerance, ctx.RegionSpaces, out int unreadable, out string? firstWhy);
                    // Witnesses and boundaries take the path of every sketch point: source sketch → model → mirror → target sketch.
                    List<ProfileRegion> mirrored = r.Regions
                        .Select(s => s.Map(q => SketchMirror.MapPoint(sketch.Source.Frame, ctx.Mirror, target, q))).ToList();
                    RegionMatch m = RegionMatcher.Match(mirrored, all.Select(a => a.Shape).ToList(), !r.IsThin);
                    if (!m.Success)
                    {
                        why = m.Reason + (unreadable > 0 ? $" ({unreadable} Bereich{(unreadable == 1 ? "" : "e")} der gespiegelten Skizze nicht lesbar: {firstWhy})" : "");
                        return null;
                    }
                    for (int k = 0; k < m.Indices.Count; k++)
                    {
                        p.Regions.Add(all[m.Indices[k]].Region);
                        p.Witnesses.Add(m.Witnesses[k]);
                    }
                    p.Notes.AddRange(m.Notes);
                    p.Inexact = m.Inexact;
                }
                return p;
            }
            catch (Exception ex)
            {
                why = $"Das Profil ließ sich in der gespiegelten Skizze nicht zuordnen ({ex.Message})";
                return null;
            }
        }

        /// <summary>
        /// Creates the feature along the ways of <see cref="ProfileRules.Ways"/>. Without a wanted
        /// profile only the whole-sketch ways, as before. With one: each pre-selection way, read
        /// back and deleted if it did not take the profile (the profile is then read anew); then
        /// the whole sketch with Contours set in ModifyDefinition (object[], then DispatchWrapper[]),
        /// mapped afresh inside the edit, where the model is rolled back to the feature. If none
        /// takes it, the feature is the whole sketch and <see cref="ProfileOutcome.Lost"/> says why.
        /// </summary>
        /// <param name="create">Selects for a way (<see cref="Select"/>) and creates; null if a selection failed or SOLIDWORKS made nothing.</param>
        public static ProfileOutcome Create(BuildContext ctx, SketchMap sketch, IProfileRecord r, bool revolve, bool wanted,
            Func<ProfileWay, Profile?, Feature?> create)
        {
            var outcome = new ProfileOutcome();
            IReadOnlyList<ProfileWay> ways = ProfileRules.Ways(revolve);
            string? lost = null;
            if (wanted && !r.SelectionLost)
            {
                Profile? profile = null;
                foreach (ProfileWay way in ways.Where(w => !w.WholeSketch))
                {
                    profile ??= Map(ctx, sketch, r, out lost);
                    if (profile == null) break;
                    Feature? f = create(way, profile);
                    if (f == null) continue;
                    if (Check(ctx, f, sketch, profile, out string? rejected))
                    {
                        outcome.Way = way.Description;
                        return Done(outcome, f, profile, null);
                    }
                    outcome.Rejected.Add($"{way.Description}: {rejected}");
                    BuilderSupport.Delete(ctx, f);
                    profile = null;
                }
            }
            else if (wanted)
            {
                lost = ProfileRules.LostInCapture;
            }

            foreach (ProfileWay way in ways.Where(w => w.WholeSketch))
            {
                Feature? f = create(way, null);
                if (f == null) continue;
                if (!wanted) return Done(outcome, f, null, null);
                if (lost != null) return Done(outcome, f, null, lost);

                bool modified = false;
                foreach (bool wrapped in new[] { false, true })
                {
                    bool ok = Assign(ctx, f, sketch, r, wrapped, out Profile? used, out string? mapWhy);
                    if (used == null)
                    {
                        lost = mapWhy ?? ProfileRules.NotTaken;
                        break;
                    }
                    if (!ok) continue;
                    modified = true;
                    if (Check(ctx, f, sketch, used, out string? rejected))
                    {
                        outcome.Way = wrapped ? way.Description + " (DispatchWrapper)" : way.Description;
                        return Done(outcome, f, used, null);
                    }
                    outcome.Rejected.Add($"{way.Description}{(wrapped ? " (DispatchWrapper)" : "")}: {rejected}");
                }
                // A definition that took something else than the profile: start again from the whole sketch.
                if (modified)
                {
                    BuilderSupport.Delete(ctx, f);
                    f = create(way, null);
                    if (f == null) continue;
                }
                return Done(outcome, f, null, lost ?? ProfileRules.NotTaken);
            }
            outcome.Lost = lost;
            return outcome;
        }

        private static ProfileOutcome Done(ProfileOutcome outcome, Feature f, Profile? used, string? lost)
        {
            outcome.Feature = f;
            outcome.Used = used;
            outcome.Lost = lost;
            return outcome;
        }

        /// <summary>
        /// Selects for a way: the whole sketch (mark 0), or the profile's contours and regions with
        /// the way's mark, the sketch first if the way says so. False if any selection fails –
        /// the way is skipped then. The whole sketch is selected as before, without that check.
        /// </summary>
        public static bool Select(BuildContext ctx, SketchMap sketch, Profile? profile, ProfileWay way)
        {
            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            if (way.WholeSketch || profile == null)
            {
                SwSelect.Select(doc, sketch.Feature, 0, false);
                return true;
            }
            try
            {
                return SelectProfile(ctx, sketch, profile, way);
            }
            catch (Exception)
            {
                // A stale or unselectable object: this way is skipped, the next one reads the profile afresh.
                return false;
            }
        }

        private static bool SelectProfile(BuildContext ctx, SketchMap sketch, Profile profile, ProfileWay way)
        {
            ModelDoc2 doc = ctx.Doc;
            bool append = false;
            if (way.WithSketch)
            {
                if (!SwSelect.Select(doc, sketch.Feature, way.ProfileMark, false)) return false;
                append = true;
            }
            foreach (SketchContour c in profile.Contours)
            {
                if (!SwSelect.Select(doc, c, way.ProfileMark, append)) return false;
                append = true;
            }
            for (int i = 0; i < profile.Regions.Count; i++)
            {
                if (!SelectRegion(ctx, sketch, profile.Regions[i], profile.Witnesses[i], way.ProfileMark, append)) return false;
                append = true;
            }
            return true;
        }

        /// <summary>
        /// Selects a region; if the object does not take the selection, picks it at its witness
        /// with SelectByID2 – a ray pick, so the picked region must belong to the new sketch: in
        /// the same part a coplanar sketch of the original may lie under the same point, and the
        /// copy would then consume the original's sketch.
        /// </summary>
        private static bool SelectRegion(BuildContext ctx, SketchMap sketch, SketchRegion region, Vec3 witness, int mark, bool append)
        {
            if (SwSelect.Select(ctx.Doc, region, mark, append)) return true;
            try
            {
                string name = SwDoc.SketchNameOf(sketch.Sketch);
                Vec3 p = sketch.Mirrored.Sketch.Frame.ToWorld(witness.X, witness.Y);
                var sm = (SelectionMgr)(object)ctx.Doc.SelectionManager;
                int before = append ? sm.GetSelectedObjectCount2(-1) : 0;
                if (!ctx.Doc.Extension.SelectByID2(name, "SKETCHREGION", p.X, p.Y, p.Z, append, mark, null, 0)) return false;
                int count = sm.GetSelectedObjectCount2(-1);
                return count == before + 1 && (object)sm.GetSelectedObject6(count, -1) is SketchRegion picked && SwDoc.SketchNameOf(picked.Sketch) == name;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads back what the created feature uses: its Contours, under AccessSelections (always
        /// released). The contours must be the profile's (same segments), the regions must hold
        /// the profile's witnesses one to one, and they must belong to the new sketch. A feature
        /// that silently took the whole sketch reports no contours and fails.
        /// </summary>
        public static bool Check(BuildContext ctx, Feature f, SketchMap sketch, Profile p, out string? why)
        {
            why = null;
            try
            {
                switch ((object)f.GetDefinition())
                {
                    case ExtrudeFeatureData2 ex:
                        if (!ex.AccessSelections(ctx.Doc, null))
                        {
                            why = "Feature-Daten nicht lesbar";
                            return false;
                        }
                        try
                        {
                            why = Compare(ctx, sketch, p, ex.GetContoursCount(), (object)ex.Contours as object[]);
                        }
                        finally
                        {
                            ex.ReleaseSelectionAccess();
                        }
                        return why == null;
                    case RevolveFeatureData2 rev:
                        if (!rev.AccessSelections(ctx.Doc, null))
                        {
                            why = "Feature-Daten nicht lesbar";
                            return false;
                        }
                        try
                        {
                            why = Compare(ctx, sketch, p, rev.GetContoursCount(), (object)rev.Contours as object[]);
                        }
                        finally
                        {
                            rev.ReleaseSelectionAccess();
                        }
                        return why == null;
                    default:
                        why = "weder Extrusion noch Rotation";
                        return false;
                }
            }
            catch (Exception ex)
            {
                why = ex.Message;
                return false;
            }
        }

        private static string? Compare(BuildContext ctx, SketchMap sketch, Profile p, int count, object[]? items)
        {
            if (count <= 0 || items == null || items.Length == 0) return "das Feature verwendet die ganze Skizze";
            string name = SwDoc.SketchNameOf(sketch.Sketch);
            Frame3 frame = sketch.Mirrored.Sketch.Frame;
            IReadOnlyList<Vec3>? points = null;
            var contours = new List<HashSet<string>>();
            var regions = new List<ProfileRegion>();
            foreach (object item in items)
            {
                switch (item)
                {
                    case SketchRegion g:
                    {
                        if (SwDoc.SketchNameOf(g.Sketch) != name) return "ein Skizzenbereich des Features gehört zu einer anderen Skizze";
                        points ??= RegionCapture.SketchPoints(sketch.Sketch);
                        ProfileRegion? shape = RegionCapture.Read(g, frame, p.Epsilon > 0 ? p.Epsilon : 1e-5, points, ctx.RegionSpaces, false, out string? why);
                        if (shape == null) return why;
                        regions.Add(shape);
                        break;
                    }
                    case SketchContour c:
                        if (SwDoc.SketchNameOf(c.Sketch) != name) return "eine Kontur des Features gehört zu einer anderen Skizze";
                        contours.Add(new HashSet<string>(((object)c.GetSketchSegments() as object[] ?? Array.Empty<object>()).OfType<SketchSegment>().Select(SwDoc.IdOf)));
                        break;
                    default:
                        return "ein Element des Features ist weder Kontur noch Skizzenbereich";
                }
            }
            if (contours.Count != p.ContourIds.Count) return $"das Feature verwendet {contours.Count} statt {p.ContourIds.Count} Konturen";
            var open = p.ContourIds.ToList();
            foreach (HashSet<string> c in contours)
            {
                int k = open.FindIndex(e => e.SetEquals(c));
                if (k < 0) return "das Feature verwendet andere Konturen als gewählt";
                open.RemoveAt(k);
            }
            return regions.Count == 0 && p.Witnesses.Count == 0 ? null : RegionMatcher.CheckUsed(p.Witnesses, regions);
        }

        /// <summary>
        /// Sets Contours in a ModifyDefinition, with the profile mapped inside the edit (the model
        /// is rolled back to the feature there, and objects fetched before its creation may be
        /// stale). Nothing in the edit throws on a failed mapping: Contours stays untouched and
        /// <paramref name="used"/> is null.
        /// </summary>
        private static bool Assign(BuildContext ctx, Feature f, SketchMap sketch, IProfileRecord r, bool wrapped, out Profile? used, out string? why)
        {
            Profile? fresh = null;
            string? mapWhy = null;
            bool ok;
            if (r is RevolveRecord)
                ok = FeatureEdit.Revolve(ctx.Doc, f, d =>
                {
                    fresh = Map(ctx, sketch, r, out mapWhy);
                    if (fresh != null) d.Contours = Pack(fresh, wrapped);
                });
            else
                ok = FeatureEdit.Extrude(ctx.Doc, f, d =>
                {
                    fresh = Map(ctx, sketch, r, out mapWhy);
                    if (fresh != null) d.Contours = Pack(fresh, wrapped);
                });
            used = fresh;
            why = mapWhy;
            ctx.Topology.Invalidate();
            return ok && fresh != null;
        }

        /// <summary>The items as object[] or, as some API members want, as DispatchWrapper[].</summary>
        private static object Pack(Profile p, bool wrapped) =>
            wrapped ? p.Items.Select(x => new DispatchWrapper(x)).ToArray() : (object)p.Items.ToArray();
    }
}
