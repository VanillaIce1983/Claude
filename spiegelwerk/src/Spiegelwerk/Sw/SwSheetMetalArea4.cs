using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Tab and slot and the sheet-metal gusset (blech-plan S8): the scalar readers used at capture and for the
    /// read-back, the read-backs that compare the copy's edges and faces inside its selection access (the
    /// entities are valid only there), the bend a gusset stands on and the points of a feature's faces.
    /// </summary>
    internal static partial class SwSheetMetal
    {
        // ---- tab and slot

        /// <summary>The data object answers as tab and slot (its type name is not confirmed, B64).</summary>
        public static bool IsTabAndSlot(Feature f)
        {
            try
            {
                return (object)f.GetDefinition() is TabAndSlotFeatureData;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void ReadTabSlotScalars(TabAndSlotGroupData g, TabSlotGroup r)
        {
            r.Offset = g.Offset;
            r.D1OffsetFromStart = g.D1OffsetFromStart;
            r.D2OffsetFromEnd = g.D2OffsetFromEnd;
            r.SpacingType = g.SpacingType;
            r.SpacingNumberOfInstances = g.SpacingNumberOfInstances;
            r.Spacing = g.Spacing;
            r.TabLength = g.TabLength;
            r.TabThickness = g.TabThickness;
            r.TabHeightType = g.TabHeightType;
            r.TabHeightValue = g.TabHeightValue;
            r.TabOffsetValue = g.TabOffsetValue;
            r.TabReverseDirection = g.TabReverseDirection;
            r.TabEdgesType = g.TabEdgesType;
            r.TabFilletEdgeTreatmentValue = g.TabFilletEdgeTreatmentValue;
            r.TabChamferEdgeTreatmentValue = g.TabChamferEdgeTreatmentValue;
            r.SlotClearance = g.SlotClearance;
        }

        /// <summary>
        /// The values of a group into the copy's group data: the offset flag always, D1/D2 only with it, the
        /// spacing of its kind, the height of its kind, the edge treatment of its kind, the clearance. Only what
        /// differs from what the data hold – a setter that is not needed cannot refuse.
        /// </summary>
        public static void WriteTabSlotScalars(TabAndSlotGroupData g, TabSlotGroup want)
        {
            if (g.Offset != want.Offset) g.Offset = want.Offset;
            if (want.Offset)
            {
                g.D1OffsetFromStart = want.D1OffsetFromStart;
                g.D2OffsetFromEnd = want.D2OffsetFromEnd;
            }
            if (g.SpacingType != want.SpacingType) g.SpacingType = want.SpacingType;
            if (want.SpacingType == TabSlotSpacingTypes.EqualSpacing) g.SpacingNumberOfInstances = want.SpacingNumberOfInstances;
            else g.Spacing = want.Spacing;
            g.TabLength = want.TabLength;
            g.TabThickness = want.TabThickness;
            if (g.TabHeightType != want.TabHeightType) g.TabHeightType = want.TabHeightType;
            if (want.TabHeightType == TabSlotHeightTypes.Blind) g.TabHeightValue = want.TabHeightValue;
            if (TabSlotRules.HasTabOffset(want))
            {
                g.TabOffsetValue = want.TabOffsetValue;
                if (g.TabReverseDirection != want.TabReverseDirection) g.TabReverseDirection = want.TabReverseDirection;
            }
            if (g.TabEdgesType != want.TabEdgesType) g.TabEdgesType = want.TabEdgesType;
            if (want.TabEdgesType == TabEdgesTypes.FilletEdge) g.TabFilletEdgeTreatmentValue = want.TabFilletEdgeTreatmentValue;
            if (want.TabEdgesType == TabEdgesTypes.ChamferEdge) g.TabChamferEdgeTreatmentValue = want.TabChamferEdgeTreatmentValue;
            g.SlotClearance = want.SlotClearance;
        }

        /// <summary>The groups of a tab-and-slot data object (SelectionGetGroups); none if it lists none.</summary>
        public static List<TabAndSlotGroupData> GroupsOf(TabAndSlotFeatureData d) =>
            ((object)d.SelectionGetGroups() as object[] ?? Array.Empty<object>()).OfType<TabAndSlotGroupData>().ToList();

        /// <summary>
        /// The tab and slot as it is now: every group's values, and per group whether its tab edge and slot face
        /// are the wanted (mirrored) ones – compared inside the copy's selection access. Null if its data do not open.
        /// </summary>
        public static List<TabSlotGroup>? ReadBackTabAndSlot(ModelDoc2 doc, Feature f, IReadOnlyList<EdgeSignature> edges, IReadOnlyList<FaceSignature> faces,
            out List<bool> sameEdge, out List<bool> sameFace)
        {
            sameEdge = new List<bool>();
            sameFace = new List<bool>();
            var d = (object)f.GetDefinition() as TabAndSlotFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var result = new List<TabSlotGroup>();
                List<TabAndSlotGroupData> groups = GroupsOf(d);
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = new TabSlotGroup();
                    ReadTabSlotScalars(groups[i], g);
                    result.Add(g);
                    bool edgeOk = false, faceOk = false;
                    if (i < edges.Count && (object)groups[i].SelectionTabEdge is Edge e)
                        edgeOk = !double.IsPositiveInfinity(edges[i].DistanceTo(TopologyCapture.Edge(e), p => DistanceToEdge(e, p)));
                    if (i < faces.Count && (object)groups[i].SelectionSlotFace is Face2 face)
                        faceOk = SameFaces(new[] { faces[i] }, new List<Face2> { face });
                    sameEdge.Add(edgeOk);
                    sameFace.Add(faceOk);
                }
                return result;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        // ---- gusset

        public static void ReadGussetScalars(SMGussetFeatureData d, SheetMetalGussetRecord r)
        {
            r.UseOffset = d.UseOffset;
            r.OffsetDistance = d.OffsetDistance;
            r.FlipOffset = d.FlipOffset;
            r.ProfileDimensionScheme = d.ProfileDimensionScheme;
            r.IndentDepth = d.IndentDepth;
            r.ProfileLengthDim = d.ProfileLengthDim;
            r.ProfileHeightDim = d.ProfileHeightDim;
            r.ProfileAngleDim = d.ProfileAngleDim;
            r.UseAngleDimForProfile = d.UseAngleDimForProfile;
            r.FlipDimSides = d.FlipDimSides;
            r.IndentWidth = d.IndentWidth;
            r.GussetThickness = d.GussetThickness;
            r.DraftSideFaces = d.DraftSideFaces;
            r.DraftAngle = d.DraftAngle;
            r.FilletInnerCorners = d.FilletInnerCorners;
            r.InnerCornerFilletRadius = d.InnerCornerFilletRadius;
            r.FilletOuterCorners = d.FilletOuterCorners;
            r.OuterCornerFilletRadius = d.OuterCornerFilletRadius;
            r.GussetType = d.GussetType;
            r.FilletGussetEdges = d.FilletGussetEdges;
            r.EdgeFilletRadius = d.EdgeFilletRadius;
            r.ProfileType = d.ProfileType;
            r.OverrideDocSettings = d.OverrideDocSettings;
            r.ShowProfile = d.ShowProfile;
            r.ShowCenter = d.ShowCenter;
        }

        /// <summary>The gusset as it is now, and whether it has a reference line and point; null if its data do not open.</summary>
        public static SheetMetalGussetRecord? ReadBackGusset(ModelDoc2 doc, Feature f, out bool hasLine, out bool hasPoint)
        {
            hasLine = hasPoint = false;
            var d = (object)f.GetDefinition() as SMGussetFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var r = new SheetMetalGussetRecord(f.Name, "SMGusset");
                ReadGussetScalars(d, r);
                hasLine = (object)d.ReferenceLine != null;
                hasPoint = (object)d.ReferencePoint != null;
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>
        /// The bend a gusset stands on (A4 P9): the cylindrical face among its legs, else the cylindrical face next
        /// to its legs (sharing an edge with each), with its axis and the ends of its edges. Read while the legs are
        /// valid (under the gusset's selection access, the state before it); null for a toroidal or sharp bend, or
        /// if nothing reads.
        /// </summary>
        public static (Line3 axis, List<Vec3> points)? BendOf(IReadOnlyList<Face2> legs)
        {
            try
            {
                Face2? bend = legs.FirstOrDefault(IsCylinder) ?? CommonCylinder(legs);
                if (bend == null) return null;
                var surface = (Surface)(object)bend.GetSurface();
                // origin x,y,z, axis x,y,z, radius
                var c = (double[])(object)surface.CylinderParams;
                var axis = new Line3(Vec3.FromArray(c, 0), Vec3.FromArray(c, 3));
                var points = new List<Vec3>();
                foreach (Edge e in EdgesOf(bend))
                {
                    if ((object)e.GetStartVertex() is Vertex s) points.Add(TopologyCapture.Vertex(s));
                    if ((object)e.GetEndVertex() is Vertex t) points.Add(TopologyCapture.Vertex(t));
                }
                return points.Count == 0 ? null : (axis, points);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsCylinder(Face2 f)
        {
            try
            {
                return ((Surface)(object)f.GetSurface()).Identity() == (int)swSurfaceTypes_e.CYLINDER_TYPE;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<Edge> EdgesOf(Face2 f) => ((object)f.GetEdges() as object[] ?? Array.Empty<object>()).OfType<Edge>().ToList();

        /// <summary>The cylindrical face that shares an edge with every leg (two planar legs of a bend).</summary>
        private static Face2? CommonCylinder(IReadOnlyList<Face2> legs)
        {
            if (legs.Count == 0) return null;
            List<Face2> Neighbours(Face2 leg) => EdgesOf(leg)
                .SelectMany(e => ((object)e.GetTwoAdjacentFaces2() as object[] ?? Array.Empty<object>()).OfType<Face2>())
                .Where(IsCylinder).ToList();
            List<Face2> first = Neighbours(legs[0]);
            foreach (Face2 candidate in first)
            {
                FaceSignature sig = TopologyCapture.Face(candidate);
                bool common = legs.Skip(1).All(leg => Neighbours(leg).Any(n => TopologyCapture.Face(n).CouldBe(sig) &&
                                                                                TopologyCapture.DistanceToFace(n, sig.Sample) <= Tol.Length));
                if (common) return candidate;
            }
            return null;
        }

        /// <summary>The ends and the middle of every edge of a feature's faces (its extent); none if they do not read.</summary>
        public static List<Vec3> FacePoints(Feature f)
        {
            var points = new List<Vec3>();
            try
            {
                foreach (Face2 face in ((object)f.GetFaces() as object[] ?? Array.Empty<object>()).OfType<Face2>())
                    foreach (Edge e in EdgesOf(face))
                    {
                        EdgeSignature s = TopologyCapture.Edge(e);
                        points.Add(s.Start);
                        points.Add(s.End);
                        points.Add(s.Mid);
                    }
            }
            catch (Exception)
            {
                points.Clear();
            }
            return points;
        }
    }
}
