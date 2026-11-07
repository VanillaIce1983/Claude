using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Flanges on edges (A2, blech-plan S2, S3): hem, miter flange and edge flange; the jog (S4). All go through the
    /// safe sheet-metal dispatch (<see cref="CaptureSheetMetal"/>), so a call that throws makes this
    /// feature unsupported, not the run fail. Edges and values are read under AccessSelections
    /// (the state before the feature), sketches outside it; the bend sub-features are read for
    /// notes (A2 P5).
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        private HemRecord CaptureHem(Feature f, string type, HemFeatureData d)
        {
            var r = new HemRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition des Saums konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (SwSheetMetal.ReadHemScalars(d, r)) r.Notes.Add(OwnAllowanceUnreadable);
                r.Edges.AddRange(EdgesOf((object)d.Edges, "Saum"));
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Edges.Count == 0) throw new NotSupportedException("Saum ohne lesbare Kanten.");

            // Width sketches: read outside the selection access, paired with the edges.
            ReadWidthSketches(f, r);
            ReadBendsInto(f, HemRules.Parent(r), r.Bends, r.BendOverrides, r.Notes, out int count);
            r.BendCount = count;
            return r;
        }

        private MiterFlangeRecord CaptureMiterFlange(Feature f, string type, MiterFlangeFeatureData d)
        {
            Feature sketch = SketchOf(f);
            var r = new MiterFlangeRecord(f.Name, type, sketch.Name);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition der Gehrungslasche konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (SwSheetMetal.ReadMiterScalars(d, r)) r.Notes.Add(OwnAllowanceUnreadable);
                // Chain order: the start of the chain carries the profile.
                r.Edges.AddRange(EdgesOf((object)d.Edges, "Gehrungslasche"));
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Edges.Count == 0) throw new NotSupportedException("Gehrungslasche ohne lesbare Kanten.");
            ReadBendsInto(f, MiterRules.Parent(r), r.Bends, r.BendOverrides, r.Notes, out int count);
            r.BendCount = count;
            return r;
        }

        /// <summary>
        /// Edge flange (A2 phase 2b, blech-plan S3). Under AccessSelections: the edges – straight ones
        /// only –, the values (<see cref="SwSheetMetal.ReadEdgeFlangeScalars"/>, the same reader as the
        /// read-back) and the references of the length and the position offset where their type has
        /// one (a straight edge stays an edge, A2 P12; one that does not read makes the flange
        /// unsupported – the copy would end elsewhere), late-bound the angle's reference. Outside it:
        /// one profile per edge (<see cref="ReadFlangeProfiles"/>), then the bends.
        /// </summary>
        private EdgeFlangeRecord CaptureEdgeFlange(Feature f, string type, EdgeFlangeFeatureData d)
        {
            var r = new EdgeFlangeRecord(f.Name, type);
            var edges = new List<EdgeSignature>();
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition der Kantenlasche konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (SwSheetMetal.ReadEdgeFlangeScalars(d, r)) r.Notes.Add(OwnAllowanceUnreadable);
                edges.AddRange(EdgesOf((object)d.Edges, "Kantenlasche"));
                if (edges.Any(e => e.Kind != CurveKind.Line)) throw new NotSupportedException(EdgeFlangeRules.CurvedEdge);
                if (EdgeFlangeRules.UsesReference(r.OffsetType))
                    r.OffsetReference = FlangeReference((object)d.OffsetReference)
                                        ?? throw new NotSupportedException("Bezug der Laschenlänge nicht lesbar.");
                if (r.UsePositionOffset && EdgeFlangeRules.UsesReference(r.PositionOffsetType))
                    r.PositionOffsetReference = FlangeReference((object)d.PositionOffsetReference)
                                                ?? throw new NotSupportedException("Bezug des Versatzes der Kantenlasche nicht lesbar.");
                // Newer versions only: an angle given by a face or edge. Its value is the bend angle anyway: without it the copy keeps the number.
                if (SwLate.Get(d, "AngleReference") is object angle && !(angle is DBNull))
                {
                    r.AngleReference = FlangeReference(angle);
                    if (r.AngleReference == null) r.Notes.Add(AngleReferenceLost);
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (edges.Count == 0) throw new NotSupportedException("Kantenlasche ohne lesbare Kanten.");
            ReadFlangeProfiles(f, r, edges);
            ReadBendsInto(f, EdgeFlangeRules.Parent(r), r.Bends, r.BendOverrides, r.Notes, out int count);
            r.BendCount = count;
            return r;
        }

        /// <summary>
        /// Jog (A2 phase 2c, P6, blech-plan S4). Its sketch must hold exactly one line (anything else makes
        /// it unsupported). Under AccessSelections (the state before the jog): the values (<see cref="SwSheetMetal.ReadJogScalars"/>,
        /// the same reader as the read-back), the offset's reference where its type has one, the fixed face
        /// and point – the point's space is undocumented: read both ways, model space preferred (K9) –, the
        /// faces of its body. Outside it: the face its sketch was drawn on, its bends, the witnesses.
        /// </summary>
        private JogRecord CaptureJog(Feature f, string type, JogFeatureData d)
        {
            Feature sketchFeature = SketchOf(f);
            var r = new JogRecord(f.Name, type, sketchFeature.Name);
            SketchModel sketch = _sketches.Read(sketchFeature);
            r.LineId = JogRules.JogLine(sketch, out string? why) ?? throw new NotSupportedException(why);
            List<Line3> lines = SheetMetalBendRules.BendLines(sketch);
            if (lines.Count != 1) throw new NotSupportedException("Die Linie der Kröpfung hat keine Länge.");
            Line3 line = lines[0];
            r.Line = line;

            var before = new List<FaceSignature>();
            string? sheetMetal = null;
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition der Kröpfung konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (SwSheetMetal.ReadJogScalars(d, r)) r.Notes.Add(OwnAllowanceUnreadable);
                if (JogRules.DimPos(r.DimensionPosition) == null || JogRules.BendPos(r.JogPosition) == null)
                    throw new NotSupportedException($"Bemaßungs- oder Kröpfungsposition der Kröpfung ({r.DimensionPosition}/{r.JogPosition}) lässt sich über die API nicht anlegen.");
                if (JogRules.UsesReference(r.OffsetType))
                    r.OffsetReference = FlangeReference((object)d.OffsetReference) ?? throw new NotSupportedException("Bezug des Versatzes der Kröpfung nicht lesbar.");
                var face = (object)d.FixedFace as Face2 ?? throw new NotSupportedException(JogRules.FixedFaceUnreadable);
                r.FixedFace = TopologyCapture.Face(face);
                if (r.FixedFace.Kind != SurfaceKind.Plane) throw new NotSupportedException("Feste Fläche der Kröpfung ist nicht eben.");
                if ((object)d.FixedPoint is double[] raw && raw.Length >= 3)
                {
                    Frame3 frame = _math.SketchFrame((Sketch)(object)sketchFeature.GetSpecificFeature2());
                    FixedPointChoice choice = ChooseFixedPoint(FixedPointSpace.Model, face, frame, Vec3.FromArray(raw), new[] { line }, r.FixedFace.Normal);
                    // Both readings on the face but on different sides: the side is not known (K9).
                    r.FixedPointAmbiguous = choice.Ambiguous;
                    r.FixedPoint = choice.Ambiguous ? null : choice.Point;
                    r.FixedPointSpace = choice.Ambiguous ? FixedPointSpace.Unknown : choice.Space;
                }
                before.AddRange(BodyFaces(face));
                sheetMetal = SheetMetalOfBody(face);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            r.Thickness = ThicknessOf(sheetMetal);

            r.SketchFace = CaptureSupportFace(sketchFeature);
            ReadBendsInto(f, JogRules.Parent(r), r.Bends, r.BendOverrides, r.Notes, out int count);
            r.BendCount = count;
            r.Witnesses.AddRange(CaptureWitnesses(f, r.FixedFace!, before) ?? new List<Vec3>());
            return r;
        }

        /// <summary>
        /// A reference of an edge flange: a straight edge stays an edge (the general reading makes an
        /// axis of it), anything else as usual; null for none and for one that cannot be found again.
        /// </summary>
        private GeometryRef? FlangeReference(object? o)
        {
            if (o is Edge e) return GeometryRef.ToEdge(TopologyCapture.Edge(e));
            GeometryRef? g = o == null ? null : ToGeometryRef(o);
            return g == null || g.Kind == GeometryRefKind.Unknown ? null : g;
        }

        /// <summary>The note when the angle's reference is not carried over (capture or build).</summary>
        internal const string AngleReferenceLost = "Bezug des Laschenwinkels nicht übertragbar; die Kopie hat den Winkel als Zahl.";

        /// <summary>
        /// The profiles of an edge flange (A2 P2, P13): its sketch sub-features and the top-level parent
        /// sketches the tree gives it (<see cref="SheetMetalTree"/>), each read twice – as the finished
        /// part shows it and in the state before the flange – and merged (<see cref="FlangeProfiles.Merge"/>);
        /// paired with the edges by the edge it was made on, a line on an edge, or its frame
        /// (<see cref="FlangeProfiles.Assign"/>). Every edge needs exactly one: anything else makes the
        /// flange unsupported – a guessed pairing would build the wrong flange.
        /// </summary>
        private void ReadFlangeProfiles(Feature flange, EdgeFlangeRecord r, List<EdgeSignature> edges)
        {
            List<Feature> sketches = SwDoc.SubFeatures(flange).Where(SwDoc.IsSketch).ToList();
            foreach (var owned in _sheetMetal.Owners.Where(o => o.Value.owner == flange.Name && o.Value.kind == PartOwnerKind.EdgeFlange))
                if (sketches.All(s => s.Name != owned.Key) && SwDoc.FeatureByName(_doc, owned.Key) is Feature parent)
                    sketches.Add(parent);
            var candidates = new List<ProfileCandidate>();
            foreach (Feature s in sketches)
            {
                if (s.GetTypeName2() != "ProfileFeature") throw new NotSupportedException($"Profilskizze „{s.Name}“ der Kantenlasche ist eine 3D-Skizze.");
                candidates.Add(ReadFlangeProfile(s));
            }
            int[]? pairs = FlangeProfiles.Assign(edges, candidates, byFrame: true);
            if (pairs == null)
                throw new NotSupportedException("Die Profilskizzen der Kantenlasche lassen sich ihren Kanten nicht eindeutig zuordnen.");
            for (int i = 0; i < edges.Count; i++)
            {
                int k = Array.IndexOf(pairs, i);
                if (k < 0) throw new NotSupportedException($"Kante {i + 1} der Kantenlasche ohne Profilskizze.");
                r.Profiles.Add(new FlangeProfile(candidates[k].Name, candidates[k].Sketch, edges[i]));
            }
        }

        /// <summary>
        /// One profile, read twice (A2 P2): without rollback for its frame and content (a position offset
        /// included), and with the bar right after the sketch – the state before the flange, where the
        /// edges and vertices its relations name exist – for the relations, dimensions and the edge it
        /// was made on. If the bar does not move, both come from the finished part.
        /// </summary>
        private ProfileCandidate ReadFlangeProfile(Feature s)
        {
            SketchModel final = _sketches.Read(s);
            SketchModel atFlange = final;
            EdgeSignature? support;
            bool rolled = RollBack((int)SolidWorks.Interop.swconst.swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, s.Name);
            try
            {
                if (rolled) atFlange = _sketches.Read(s);
                support = SwSheetMetal.SupportEdgeOf(s);
            }
            finally
            {
                if (rolled) _doc.FeatureManager.EditRollback((int)SolidWorks.Interop.swconst.swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
            SketchModel merged = FlangeProfiles.Merge(final, atFlange, out string? why)
                                 ?? throw new NotSupportedException($"Profilskizze „{s.Name}“ der Kantenlasche {why}.");
            return new ProfileCandidate(s.Name, merged, support);
        }

        private const string OwnAllowanceUnreadable = "Eigene Biegezugabe nicht lesbar; die Kopie verwendet die Standard-Biegezugabe.";

        /// <summary>The edges of a flange in their order; anything else among them makes the feature unsupported (a face or loop cannot be found by an edge fingerprint).</summary>
        private static List<EdgeSignature> EdgesOf(object? edges, string what)
        {
            var result = new List<EdgeSignature>();
            if (!(edges is object[] items)) return result;
            foreach (object item in items)
            {
                if (!(item is Edge e)) throw new NotSupportedException($"{what}: ein gewähltes Element ist keine Kante.");
                result.Add(TopologyCapture.Edge(e));
            }
            return result;
        }

        /// <summary>
        /// The bend sub-features (by definition) with what differs from their owner: each bend with
        /// values of its own becomes a note (not transferable, A2 P5); one that does not read is
        /// said once. <paramref name="count"/>: every bend sub-feature, readable or not.
        /// </summary>
        private void ReadBendsInto(Feature f, BendValues parent, List<BendValues> bends, List<BendOverride> overrides, List<string> notes, out int count)
        {
            List<(Feature sub, BendValues? values)> read = SwSheetMetal.ReadBends(_doc, f);
            count = read.Count;
            bends.AddRange(read.Where(b => b.values != null).Select(b => b.values!));
            List<string> unreadable = read.Where(b => b.values == null).Select(b => b.sub.Name).ToList();
            if (unreadable.Count > 0)
                notes.Add($"Biegungen nicht lesbar ({string.Join(", ", unreadable.Select(n => $"„{n}“"))}); ob sie eigene Werte haben, ist nicht geprüft.");
            List<BendOverride> own = BendOverrides.Differences(parent, bends);
            overrides.AddRange(own);
            notes.AddRange(own.Select(o => o.Note()));
        }

        /// <summary>
        /// The width sketches of a hem (its sketch sub-features), read in the state the hem saw
        /// (rolled back after the sketch where it refers to the model) and once more as the finished
        /// part shows them; both must agree (same segments, same points). Paired with the hem's edges
        /// by the lines on them (<see cref="FlangeProfiles.Assign"/>). If any of that fails, the copy
        /// keeps the width sketches the new hem brings, with a note.
        /// </summary>
        private void ReadWidthSketches(Feature hem, HemRecord r)
        {
            List<Feature> sketches = SwDoc.SubFeatures(hem).Where(SwDoc.IsSketch).ToList();
            if (sketches.Count == 0) return;
            var candidates = new List<ProfileCandidate>();
            string? why = null;
            foreach (Feature s in sketches)
            {
                if (s.GetTypeName2() != "ProfileFeature")
                {
                    why = $"„{s.Name}“ ist eine 3D-Skizze";
                    break;
                }
                SketchModel atHem, final;
                try
                {
                    atHem = ReadSketch(s);
                    final = _sketches.Read(s);
                }
                catch (Exception ex)
                {
                    // A width sketch that does not read costs only the width, not the hem.
                    why = $"„{s.Name}“ nicht lesbar ({ex.Message})";
                    break;
                }
                if (!SameGeometry(atHem, final))
                {
                    why = $"„{s.Name}“ liegt im fertigen Teil anders als beim Saum";
                    break;
                }
                candidates.Add(new ProfileCandidate(s.Name, atHem));
            }
            int[]? pairs = why == null ? FlangeProfiles.Assign(r.Edges, candidates) : null;
            if (why == null && pairs == null) why = "sie lassen sich den Kanten nicht eindeutig zuordnen";
            if (why != null)
            {
                // Still owned: the new hem brings its own, these mean nothing without it.
                foreach (Feature s in sketches) r.WidthSketches.Add(new HemWidthSketch(s.Name, new SketchModel { Name = s.Name }, -1));
                r.WidthSketchesPaired = false;
                r.Notes.Add($"Breitenskizzen des Saums nicht übertragbar ({why}); die Kopie behält die Breitenskizzen, die SOLIDWORKS mit dem Saum anlegt.");
                return;
            }
            for (int i = 0; i < candidates.Count; i++) r.WidthSketches.Add(new HemWidthSketch(candidates[i].Name, candidates[i].Sketch, pairs![i]));
        }

        /// <summary>Same segment ids and every point at the same place (model coordinates).</summary>
        private static bool SameGeometry(SketchModel a, SketchModel b)
        {
            if (!a.Segments.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(b.Segments.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal)))
                return false;
            foreach (SkPoint p in a.Points)
            {
                SkPoint? q = b.TryPoint(p.Id);
                if (q == null || !a.ToModel(p).AlmostEquals(b.ToModel(q), Tol.Length)) return false;
            }
            return true;
        }
    }
}
