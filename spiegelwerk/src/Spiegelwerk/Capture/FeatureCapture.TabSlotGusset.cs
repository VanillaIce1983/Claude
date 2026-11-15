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

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Tab and slot and the sheet-metal gusset (A4 (2) §6, §7, blech-plan S8). Through the safe sheet-metal
    /// dispatch (<see cref="CaptureSheetMetal"/>), so a call that throws makes this feature unsupported, not the
    /// run fail. Under AccessSelections (the state before the feature): the values, the tab edges, slot faces,
    /// tab faces and reference points of every group; the legs, reference line and point of a gusset and the bend
    /// it stands on. With the bar right after a gusset: its section on the bend, and the same for every earlier
    /// gusset on that bend – what its offset counts from is decided from these (A4 P9).
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>Tab-and-slot records captured so far: a later one with the same edges and faces is the slot the tree did not recognise.</summary>
        private readonly List<TabAndSlotRecord> _tabsAndSlots = new List<TabAndSlotRecord>();

        private FeatureRecord CaptureTabAndSlot(Feature f, string type, TabAndSlotFeatureData d)
        {
            // Made in an assembly's context: its references point into another file (A4 P12).
            if (f.ListExternalFileReferencesCount() > 0) throw new NotSupportedException(TabSlotRules.InContext);
            var r = new TabAndSlotRecord(f.Name, type) { SlotFeatureName = _sheetMetal.SlotOf(f.Name) };
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition von Lasche und Schlitz konnte nicht gelesen werden (AccessSelections).");
            try
            {
                int i = 0;
                foreach (TabAndSlotGroupData g in SwSheetMetal.GroupsOf(d))
                {
                    i++;
                    var group = new TabSlotGroup();
                    SwSheetMetal.ReadTabSlotScalars(g, group);
                    group.TabEdge = (object)g.SelectionTabEdge is Edge e
                        ? TopologyCapture.Edge(e)
                        : throw new NotSupportedException($"Laschenkante der Gruppe {i} von Lasche und Schlitz nicht lesbar.");
                    group.SlotFace = (object)g.SelectionSlotFace is Face2 slot
                        ? TopologyCapture.Face(slot)
                        : throw new NotSupportedException($"Schlitzfläche der Gruppe {i} von Lasche und Schlitz nicht lesbar.");
                    if ((object)g.TabFace is Face2 tabFace) group.TabFace = TopologyCapture.Face(tabFace);
                    group.StartReference = PointReference((object)g.SelectionStartReferencePoint, "Startpunkt", i);
                    group.EndReference = PointReference((object)g.SelectionEndReferencePoint, "Endpunkt", i);
                    r.Groups.Add(group);
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Groups.Count == 0) throw new NotSupportedException("Lasche und Schlitz ohne lesbare Gruppen.");

            // The slot feature, should the tree not have recognised it (B64): the same groups as one captured before.
            TabAndSlotRecord? earlier = _tabsAndSlots.FirstOrDefault(x => SameGroups(x, r));
            if (earlier != null)
                return new UnsupportedRecord(f.Name, type, TabSlotRules.SameAsEarlier(earlier.Name), false) { TriggersFallback = false };
            _tabsAndSlots.Add(r);
            return r;
        }

        /// <summary>A start or end point of a group: a vertex or a sketch point; anything else makes the feature unsupported.</summary>
        private GeometryRef? PointReference(object? o, string what, int group)
        {
            if (o == null) return null;
            GeometryRef? reference = ToGeometryRef(o);
            if (reference == null || (reference.Kind != GeometryRefKind.Vertex && reference.Kind != GeometryRefKind.SketchPoint))
                throw new NotSupportedException($"{what} der Gruppe {group} von Lasche und Schlitz ist weder Eckpunkt noch Skizzenpunkt.");
            return reference;
        }

        private static bool SameGroups(TabAndSlotRecord a, TabAndSlotRecord b) =>
            a.Groups.Count == b.Groups.Count &&
            a.Groups.Zip(b.Groups, (x, y) => !double.IsPositiveInfinity(x.TabEdge.DistanceTo(y.TabEdge)) &&
                                             x.SlotFace.CouldBe(y.SlotFace) && x.SlotFace.Sample.AlmostEquals(y.SlotFace.Sample)).All(same => same);

        /// <summary>The bend and section of a gusset, once measured (also for later gussets on the same bend).</summary>
        private sealed class GussetPlace
        {
            public Line3? Bend { get; set; }
            public Vec3? EndA { get; set; }
            public Vec3? EndB { get; set; }
            public Vec3? Section { get; set; }
            public double Thickness { get; set; }
        }

        private readonly Dictionary<string, GussetPlace> _gussetPlaces = new Dictionary<string, GussetPlace>(StringComparer.Ordinal);

        private SheetMetalGussetRecord CaptureGusset(Feature f, string type, SMGussetFeatureData d)
        {
            var r = new SheetMetalGussetRecord(f.Name, type);
            var place = new GussetPlace();
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition der Versteifungsrippe konnte nicht gelesen werden (AccessSelections).");
            try
            {
                SwSheetMetal.ReadGussetScalars(d, r);
                if (r.ProfileType == GussetProfileTypes.Custom) throw new NotSupportedException(GussetRules.CustomProfile);
                var legs = new List<Face2>();
                foreach (object o in (object)d.SupportingFaces as object[] ?? Array.Empty<object>())
                {
                    if (!(o is Face2 leg)) throw new NotSupportedException("Versteifungsrippe: ein Schenkel ist keine Fläche.");
                    legs.Add(leg);
                    r.SupportingFaces.Add(TopologyCapture.Face(leg));
                }
                if (legs.Count == 0) throw new NotSupportedException("Versteifungsrippe ohne lesbare Schenkelflächen.");
                object? line = (object)d.ReferenceLine;
                if (line != null)
                {
                    r.ReferenceLine = ToGeometryRef(line);
                    if (r.ReferenceLine == null || r.ReferenceLine.Kind == GeometryRefKind.Unknown)
                        throw new NotSupportedException("Bezugslinie der Versteifungsrippe nicht lesbar.");
                }
                object? point = (object)d.ReferencePoint;
                if (point != null)
                {
                    r.ReferencePoint = ToGeometryRef(point);
                    if (r.ReferencePoint == null || (r.ReferencePoint.Kind != GeometryRefKind.Vertex && r.ReferencePoint.Kind != GeometryRefKind.SketchPoint))
                        throw new NotSupportedException("Bezugspunkt der Versteifungsrippe ist weder Eckpunkt noch Skizzenpunkt (ein Referenzpunkt wird noch nicht übertragen).");
                }
                FillBend(place, legs, r.GussetThickness);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            place.Section = MeasureSection(f, place.Bend);
            _gussetPlaces[f.Name] = place;
            r.BendLine = place.Bend;
            r.BendEndA = place.EndA;
            r.BendEndB = place.EndB;
            r.SectionPoint = place.Section;

            // What its offset counts from: needs the earlier gussets on the same bend only without an own point.
            if (r.ReferencePoint == null && r.UseOffset && place.Bend is Line3 bend)
                r.PreviousOnBend.AddRange(EarlierGussetsOn(f, bend));
            var (anchor, anchorPoint, anchorGusset) = GussetRules.AnchorOf(r);
            r.Anchor = anchor;
            r.AnchorPoint = anchorPoint;
            r.AnchorGusset = anchorGusset;
            if (place.Bend == null || place.Section == null) r.Notes.Add(GussetRules.PlaceUnchecked);
            return r;
        }

        /// <summary>The bend under the legs (state before the gusset) and its ends; nothing if it does not read.</summary>
        private static void FillBend(GussetPlace place, List<Face2> legs, double thickness)
        {
            place.Thickness = thickness;
            if (!(SwSheetMetal.BendOf(legs) is (Line3 axis, List<Vec3> points))) return;
            place.Bend = axis;
            if (GussetRules.Ends(axis, points) is (Vec3 a, Vec3 b))
            {
                place.EndA = a;
                place.EndB = b;
            }
        }

        /// <summary>Its section on the bend, with the bar right after it (later features may have cut its faces); null if not readable.</summary>
        private Vec3? MeasureSection(Feature f, Line3? bend)
        {
            if (!(bend is Line3 line)) return null;
            if (!RollBack((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, f.Name)) return null;
            try
            {
                return GussetRules.Section(line, SwSheetMetal.FacePoints(f));
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                _doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }
        }

        /// <summary>The gussets before <paramref name="f"/> in creation order whose bend is the same line, with their sections.</summary>
        private List<GussetSection> EarlierGussetsOn(Feature f, Line3 bend)
        {
            var result = new List<GussetSection>();
            Dictionary<string, int> order = CreationOrderIndex();
            if (!order.TryGetValue(f.Name, out int at)) return result;
            List<Feature> earlier;
            try
            {
                earlier = SwDoc.FeaturesInCreationOrder(_doc).Where(x => order.TryGetValue(x.Name, out int i) && i < at && x.GetTypeName2() == "SMGusset").ToList();
            }
            catch (Exception)
            {
                return result;
            }
            foreach (Feature g in earlier)
                if (PlaceOf(g) is GussetPlace p && p.Bend is Line3 other && other.IsSameLine(bend) && p.Section is Vec3 s)
                    result.Add(new GussetSection(g.Name, s, p.Thickness));
            return result;
        }

        /// <summary>The place of an earlier gusset: measured when it was captured, otherwise now (it need not be in the run).</summary>
        private GussetPlace? PlaceOf(Feature g)
        {
            if (_gussetPlaces.TryGetValue(g.Name, out GussetPlace known)) return known;
            var place = new GussetPlace();
            try
            {
                if (!((object)g.GetDefinition() is SMGussetFeatureData d) || !d.AccessSelections(_doc, null)) return null;
                try
                {
                    List<Face2> legs = ((object)d.SupportingFaces as object[] ?? Array.Empty<object>()).OfType<Face2>().ToList();
                    FillBend(place, legs, d.GussetThickness);
                }
                finally
                {
                    d.ReleaseSelectionAccess();
                }
                place.Section = MeasureSection(g, place.Bend);
            }
            catch (Exception)
            {
                return null;
            }
            _gussetPlaces[g.Name] = place;
            return place;
        }
    }
}
