using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Corners, folds and the cross break (blech-plan S5): the scalar readers used at capture and for the
    /// read-back, the read-backs that compare faces, edges and bends as sets inside the copy's selection
    /// access (the entities are valid only there), the new feature of a creator that returns nothing, and
    /// every bend sub-feature of a document.
    /// </summary>
    internal static partial class SwSheetMetal
    {
        public static void ReadClosedCornerScalars(ClosedCornerFeatureData d, ClosedCornerRecord r)
        {
            r.CornerType = d.CornerType;
            r.Gap = d.GapDistance;
            r.OverlapRatio = d.OverlapUnderlapRatio;
            r.OpenBendRegion = d.OpenBendRegion;
        }

        public static void ReadBreakCornerScalars(BreakCornerFeatureData d, BreakCornerRecord r)
        {
            r.BreakType = d.BreakType;
            r.Distance = d.Distance;
            r.InternalCornersOnly = d.InternalCornersOnly;
            r.CenteredOnBendLines = d.CenteredOnBendLines;
        }

        public static void ReadCrossBreakScalars(CrossBreakFeatureData d, CrossBreakRecord r)
        {
            r.Angle = d.BreakAngle;
            r.Radius = d.BreakRadius;
            r.Reverse = d.ReverseDirection;
        }

        /// <summary>Faces of an IFeatureData property as faces; anything else among them makes it null.</summary>
        private static List<Face2>? FacesOf(object? items)
        {
            if (!(items is object[] array)) return new List<Face2>();
            var faces = new List<Face2>();
            foreach (object o in array)
            {
                if (!(o is Face2 face)) return null;
                faces.Add(face);
            }
            return faces;
        }

        /// <summary>The faces compared with the wanted ones as a set, by fingerprint and kernel distance (while they are valid).</summary>
        private static bool SameFaces(IReadOnlyList<FaceSignature> wanted, List<Face2> got) =>
            FaceSets.Same(wanted, got.Select(TopologyCapture.Face).ToList(), (k, p) => TopologyCapture.DistanceToFace(got[k], p));

        /// <summary>The closed corner as it is now, its faces against <paramref name="wanted"/> (mirrored); null if its data do not open.</summary>
        public static ClosedCornerRecord? ReadBackClosedCorner(ModelDoc2 doc, Feature f, IReadOnlyList<FaceSignature> wanted, out bool sameFaces, out int faces)
        {
            sameFaces = false;
            faces = -1;
            var d = (object)f.GetDefinition() as ClosedCornerFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var r = new ClosedCornerRecord(f.Name, "CornerFeat");
                ReadClosedCornerScalars(d, r);
                List<Face2>? got = FacesOf((object)d.Faces);
                faces = got?.Count ?? -1;
                sameFaces = got != null && SameFaces(wanted, got);
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>The break corner as it is now, its edges and faces against the wanted ones (mirrored); null if its data do not open.</summary>
        public static BreakCornerRecord? ReadBackBreakCorner(ModelDoc2 doc, Feature f, IReadOnlyList<EdgeSignature> edges, IReadOnlyList<FaceSignature> faces,
            out bool sameEntities, out int count)
        {
            sameEntities = false;
            count = -1;
            var d = (object)f.GetDefinition() as BreakCornerFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var r = new BreakCornerRecord(f.Name, "BreakCorner");
                ReadBreakCornerScalars(d, r);
                count = d.GetEntitiesCount();
                object[] items = (object)d.Entities as object[] ?? Array.Empty<object>();
                List<Edge> gotEdges = items.OfType<Edge>().ToList();
                List<Face2> gotFaces = items.OfType<Face2>().ToList();
                List<EdgeSignature> edgeSigs = gotEdges.Select(TopologyCapture.Edge).ToList();
                bool sameEdges = SetMatch.Pair(edges.Count, edgeSigs.Count, (i, j) => edges[i].DistanceTo(edgeSigs[j], p => DistanceToEdge(gotEdges[j], p))) != null;
                sameEntities = gotEdges.Count + gotFaces.Count == items.Length && sameEdges && SameFaces(faces, gotFaces);
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        private static double DistanceToEdge(Edge e, Vec3 p)
        {
            var c = e.GetClosestPointOn(p.X, p.Y, p.Z) as double[];
            return c == null || c.Length < 3 ? double.PositiveInfinity : Vec3.FromArray(c).DistanceTo(p);
        }

        /// <summary>The cross break as it is now, its face against <paramref name="wanted"/> (mirrored); null if its data do not open.</summary>
        public static CrossBreakRecord? ReadBackCrossBreak(ModelDoc2 doc, Feature f, FaceSignature? wanted, out bool sameFace)
        {
            sameFace = false;
            var d = (object)f.GetDefinition() as CrossBreakFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var r = new CrossBreakRecord(f.Name, "CrossBreak");
                ReadCrossBreakScalars(d, r);
                if (wanted != null && (object)d.Face is Face2 face) sameFace = SameFaces(new[] { wanted }, new List<Face2> { face });
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>
        /// What an unfold or fold acts on now: whether its fixed face is <paramref name="wanted"/> (mirrored) and the
        /// names of its bends. False if its data do not open.
        /// </summary>
        public static bool ReadBackFold(ModelDoc2 doc, Feature f, FaceSignature wanted, out bool sameFixed, out List<string> bends)
        {
            sameFixed = false;
            bends = new List<string>();
            var d = (object)f.GetDefinition() as FoldsFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return false;
            try
            {
                if ((object)d.FixedFace is Face2 face) sameFixed = SameFaces(new[] { wanted }, new List<Face2> { face });
                if ((object)d.Bends is object[] items) bends.AddRange(items.OfType<Feature>().Select(b => b.Name));
                return true;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>
        /// The feature a creator that returns nothing made (InsertSheetMetalClosedCorner, …BreakCorner, …Unfold,
        /// …Fold): new since the snapshot (K6, not GetLastFeatureAdded), of the type and with the data interface
        /// – unfold and fold share theirs (A3 P14). Null if there is none.
        /// </summary>
        public static Feature? NewOfType(ModelDoc2 doc, HashSet<string> before, string type, Func<object?, bool> isData) =>
            NewSince(doc, before).FirstOrDefault(f =>
            {
                try
                {
                    return f.GetTypeName2() == type && isData((object)f.GetDefinition());
                }
                catch (Exception)
                {
                    return false;
                }
            });

        /// <summary>
        /// Every bend sub-feature of the document (definition IOneBendFeatureData, counted by definition as
        /// <see cref="ReadBends"/>), with its owner: the sub-features of every top-level feature and folder content.
        /// </summary>
        public static List<(Feature bend, Feature owner)> AllBends(ModelDoc2 doc)
        {
            var result = new List<(Feature, Feature)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Feature top in SwDoc.TopLevelFeatures(doc))
            {
                var owners = new List<Feature> { top };
                owners.AddRange(FolderContents(doc, top));
                foreach (Feature owner in owners)
                    foreach (Feature sub in SwDoc.SubFeatures(owner))
                    {
                        if (seen.Contains(sub.Name) || !IsBend(sub)) continue;
                        seen.Add(sub.Name);
                        result.Add((sub, owner));
                    }
            }
            return result;
        }

        /// <summary>The bend sub-features of one feature, in their order (by definition).</summary>
        public static List<Feature> BendsOf(Feature owner) => SwDoc.SubFeatures(owner).Where(IsBend).ToList();

        private static bool IsBend(Feature f)
        {
            try
            {
                return (object)f.GetDefinition() is OneBendFeatureData;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
