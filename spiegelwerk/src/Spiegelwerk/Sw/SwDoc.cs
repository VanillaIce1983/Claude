using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Verification;

namespace Spiegelwerk.Sw
{
    /// <summary>Read helpers for documents: feature traversal, standard planes, bodies, mass.</summary>
    internal static class SwDoc
    {
        /// <summary>Feature types that are part of every document and never mirrored.</summary>
        private static readonly HashSet<string> SystemTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "CommentsFolder", "FavoriteFolder", "HistoryFolder", "SelectionSetFolder", "SensorFolder",
            "DocsFolder", "DetailCabinet", "SurfaceBodyFolder", "SolidBodyFolder", "EnvFolder",
            "MaterialFolder", "OriginProfileFeature", "MateReferenceGroupFolder", "EqnFolder",
            "InkMarkupFolder", "BlockFolder", "LiveSectionFolder", "FtrFolder", "MagneticGroundPlane",
            "CutListFolder", "SubWeldFolder", "Attribute", "DisplayStateFolder", "GridFeature",
            // Sheet-metal and flat-pattern folders (the part's defaults): never collected, never deleted, never new.
            "TemplateSheetMetal", "TemplateFlatPattern",
        };

        public static bool IsSystemFeature(Feature f) => SystemTypes.Contains(f.GetTypeName2());

        /// <summary>
        /// Puts the rollback bar at the end of the tree and checks it got there. Esc also
        /// interrupts SOLIDWORKS' own regeneration, a rollback included, and leaves the bar after
        /// the feature it was on.
        /// </summary>
        public static bool RollToEnd(ModelDoc2 doc)
        {
            if (!TopLevelFeatures(doc).Any(f => f.IsRolledBack())) return true;
            doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            return !TopLevelFeatures(doc).Any(f => f.IsRolledBack());
        }

        public static IEnumerable<Feature> TopLevelFeatures(ModelDoc2 doc)
        {
            var f = (Feature)(object)doc.FirstFeature();
            while (f != null)
            {
                yield return f;
                f = (Feature)(object)f.GetNextFeature();
            }
        }

        public static IEnumerable<Feature> SubFeatures(Feature parent)
        {
            var f = (Feature)(object)parent.GetFirstSubFeature();
            while (f != null)
            {
                yield return f;
                f = (Feature)(object)f.GetNextSubFeature();
            }
        }

        /// <summary>
        /// All features in dependency order (Core.Planning.CreationOrder): absorbed sketches
        /// before the feature that absorbed them, every feature after its parents – also a
        /// guide-curve sketch at the top level after the profile sketches a later loft absorbed.
        /// Sheet metal (SheetMetalTree.ListsSubFeature): the flat patterns inside the flat-pattern
        /// folder before the folder, and what lives under a flat pattern (its sketches, corner
        /// trims) before it; the SheetMetal features in their folder are not listed.
        /// </summary>
        public static List<Feature> FeaturesInCreationOrder(ModelDoc2 doc)
        {
            List<Feature> top = TopLevelFeatures(doc).ToList();
            var absorbed = new Dictionary<string, List<Feature>>(StringComparer.Ordinal);
            List<Feature> Absorbed(Feature f)
            {
                if (absorbed.TryGetValue(f.Name, out List<Feature> known)) return known;
                string type = f.GetTypeName2();
                List<Feature> subs = SubFeatures(f).ToList();
                foreach (Feature c in SwSheetMetal.FolderContents(doc, f))
                    if (subs.All(s => s.Name != c.Name)) subs.Add(c);
                List<Feature> listedSubs = subs.Where(s => SheetMetalTree.ListsSubFeature(type, s.GetTypeName2())).ToList();
                absorbed[f.Name] = listedSubs;
                return listedSubs;
            }
            // Only what the tree walk lists anyway (top level and absorbed sub-features) is reordered, nothing is added.
            var listed = new HashSet<string>(StringComparer.Ordinal);
            void Note(Feature f)
            {
                if (!listed.Add(f.Name)) return;
                foreach (Feature sub in Absorbed(f)) Note(sub);
            }
            foreach (Feature f in top) Note(f);
            IEnumerable<Feature> Parents(Feature f) =>
                f.GetParents() is object[] parents ? parents.OfType<Feature>().Where(p => listed.Contains(p.Name)) : Enumerable.Empty<Feature>();
            return CreationOrder.Sort(top, Absorbed, Parents, f => f.Name);
        }

        /// <summary>
        /// Every solid body of the final state with the features that made or changed it
        /// (IBody2.GetFeatures without sketches, reference geometry and folders), in creation order,
        /// whether it is a sheet-metal body, and the types of those features – for refusal step 2.
        /// </summary>
        public static List<BodyHistory> BodyHistories(ModelDoc2 doc, IReadOnlyList<Feature> creationOrder, Dictionary<string, string> types)
        {
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < creationOrder.Count; i++) order[creationOrder[i].Name] = i;
            var result = new List<BodyHistory>();
            foreach (Body2 b in SolidBodies(doc))
            {
                var features = new List<Feature>();
                if (b.GetFeatures() is object[] fs)
                    foreach (Feature f in fs.OfType<Feature>())
                        if (!IsSketch(f) && !IsSystemFeature(f) && !(f.GetTypeName2() is "RefPlane" or "RefAxis" or "CoordSys") && features.All(x => x.Name != f.Name))
                            features.Add(f);
                foreach (Feature f in features) types[f.Name] = f.GetTypeName2();
                List<string> names = features.Select(f => f.Name).OrderBy(n => order.TryGetValue(n, out int i) ? i : int.MaxValue).ToList();
                bool sheet;
                try
                {
                    sheet = b.IsSheetMetal();
                }
                catch (Exception)
                {
                    sheet = features.Any(f => f.GetTypeName2() == "SheetMetal");
                }
                result.Add(new BodyHistory(b.Name, names, false, sheet));
            }
            return result;
        }

        public static bool IsSketch(Feature f) => f.GetTypeName2() == "ProfileFeature" || f.GetTypeName2() == "3DProfileFeature";

        public static Feature FindOrigin(ModelDoc2 doc) =>
            TopLevelFeatures(doc).FirstOrDefault(f => f.GetTypeName2() == "OriginProfileFeature")
            ?? throw new InvalidOperationException("Ursprung des Dokuments nicht gefunden.");

        /// <summary>The single point of the origin "sketch"; used for relations and dimensions to the origin.</summary>
        public static SketchPoint OriginPoint(ModelDoc2 doc)
        {
            var sketch = (Sketch)(object)FindOrigin(doc).GetSpecificFeature2();
            var points = (object[])(object)sketch.GetSketchPoints2();
            return (SketchPoint)points[0];
        }

        public static IEnumerable<(Feature feature, Plane3 plane)> RefPlanes(ModelDoc2 doc, SwMath math)
        {
            foreach (Feature f in TopLevelFeatures(doc))
            {
                if (f.GetTypeName2() != "RefPlane") continue;
                var rp = (RefPlane)(object)f.GetSpecificFeature2();
                yield return (f, math.PlaneOf(rp));
            }
        }

        /// <summary>
        /// Front, top and right plane, found by geometry because their names depend on the
        /// language and the template ("Ebene vorne", "Front Plane", "Plan de face" ...).
        /// </summary>
        public static Feature[] StandardPlanes(ModelDoc2 doc, SwMath math)
        {
            var result = new Feature[3];
            Plane3[] wanted = { Plane3.FrontXY, Plane3.TopXZ, Plane3.RightYZ };
            foreach (var (feature, plane) in RefPlanes(doc, math))
                for (int i = 0; i < 3; i++)
                    if (result[i] == null && plane.IsCoplanarWith(wanted[i]))
                        result[i] = feature;
            if (result.Any(f => f == null))
                throw new InvalidOperationException("Standardebenen (vorne/oben/rechts) im Dokument nicht gefunden.");
            return result;
        }

        public static List<Body2> SolidBodies(ModelDoc2 doc)
        {
            if (!(doc is PartDoc part)) return new List<Body2>();
            var bodies = (object[])(object)part.GetBodies2((int)swBodyType_e.swSolidBody, false);
            return bodies?.Cast<Body2>().ToList() ?? new List<Body2>();
        }

        /// <summary>Volume and first moment of all solid bodies (density irrelevant, we use volume).</summary>
        public static MassState MassOf(ModelDoc2 doc) => MassOf(SolidBodies(doc));

        public static MassState MassOf(IEnumerable<Body2> bodies)
        {
            var items = new List<(double, Vec3)>();
            foreach (Body2 b in bodies)
            {
                // CenterOfMassX, Y, Z, Volume, Area, Mass, moments ...
                var mp = (double[])(object)b.GetMassProperties(1.0);
                if (mp == null || mp.Length < 4) continue;
                items.Add((mp[3], new Vec3(mp[0], mp[1], mp[2])));
            }
            return MassState.FromBodies(items);
        }

        /// <summary>The line of a reference axis.</summary>
        public static Line3 AxisLine(RefAxis ax)
        {
            // start point x,y,z, end point x,y,z
            var p = (double[])(object)ax.GetRefAxisParams();
            Vec3 a = Vec3.FromArray(p, 0), b = Vec3.FromArray(p, 3);
            return new Line3(a, b - a);
        }

        /// <summary>
        /// Origin and axes of a coordinate system feature. The transform maps coordinate system
        /// → model (the usual use inverts it to get coordinates in the system). Null if it is
        /// not readable or not a right-handed frame.
        /// </summary>
        public static Frame3? CoordinateSystemFrame(ModelDoc2 doc, SwMath math, string name)
        {
            try
            {
                return doc.Extension.GetCoordinateSystemTransformByName(name) is MathTransform t ? math.FrameOf(t) : (Frame3?)null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        public static Feature? FeatureByName(ModelDoc2 doc, string name) =>
            doc is PartDoc p ? (Feature)(object)p.FeatureByName(name) : null;

        public static bool HasFeatureNamed(ModelDoc2 doc, string name) => FeatureByName(doc, name) != null;

        /// <summary>Renames, avoiding clashes by appending " (2)", " (3)" ... (the feature's own name is no clash).</summary>
        public static string Rename(ModelDoc2 doc, Feature f, string wanted)
        {
            string current = f.Name;
            string name = FeatureNames.Free(wanted, current, n => HasFeatureNamed(doc, n));
            if (name == current) return current;
            f.Name = name;
            return f.Name;
        }

        /// <summary>Gives a dimension a name; false if it keeps its old one (name taken in its feature or sketch).</summary>
        public static bool TryRename(Dimension d, string name)
        {
            if (d.Name == name) return true;
            try
            {
                d.Name = name;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Name already taken; the old one stays.
            }
            return d.Name == name;
        }

        /// <summary>The feature's own display dimensions (depth, radius, spacing ...), not those of its sketches.</summary>
        public static List<DisplayDimension> OwnDimensions(Feature f)
        {
            var result = new List<DisplayDimension>();
            for (object? o = f.GetFirstDisplayDimension(); o is DisplayDimension dd; o = f.GetNextDisplayDimension(dd))
                if (dd.GetDimension2(0)?.GetFeatureOwner() is Feature owner && owner.Name == f.Name) result.Add(dd);
            return result;
        }

        public static NamedDimension Named(DisplayDimension dd)
        {
            Dimension d = dd.GetDimension2(0);
            return new NamedDimension(d.Name, d.SystemValue, dd.Type2);
        }

        public static string IdOf(SketchPoint p) => "P" + string.Join("-", (int[])(object)p.GetID());

        public static string IdOf(SketchSegment s) => "S" + string.Join("-", (int[])(object)s.GetID());

        public static string SketchNameOf(Sketch s) => ((Feature)s).Name;
    }
}
