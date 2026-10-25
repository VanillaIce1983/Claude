using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
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
        };

        public static bool IsSystemFeature(Feature f) => SystemTypes.Contains(f.GetTypeName2());

        public static IEnumerable<Feature> TopLevelFeatures(ModelDoc2 doc)
        {
            var f = (Feature)doc.FirstFeature();
            while (f != null)
            {
                yield return f;
                f = (Feature)f.GetNextFeature();
            }
        }

        public static IEnumerable<Feature> SubFeatures(Feature parent)
        {
            var f = (Feature)parent.GetFirstSubFeature();
            while (f != null)
            {
                yield return f;
                f = (Feature)f.GetNextSubFeature();
            }
        }

        /// <summary>
        /// All features in dependency order (Core.Planning.CreationOrder): absorbed sketches
        /// before the feature that absorbed them, every feature after its parents – also a
        /// guide-curve sketch at the top level after the profile sketches a later loft absorbed.
        /// </summary>
        public static List<Feature> FeaturesInCreationOrder(ModelDoc2 doc)
        {
            List<Feature> top = TopLevelFeatures(doc).ToList();
            // Only what the tree walk lists anyway (top level and absorbed sketches) is reordered, nothing is added.
            var listed = new HashSet<string>(top.Select(f => f.Name).Concat(top.SelectMany(f => SubFeatures(f).Where(IsSketch)).Select(f => f.Name)),
                StringComparer.Ordinal);
            IEnumerable<Feature> Parents(Feature f) =>
                f.GetParents() is object[] parents ? parents.OfType<Feature>().Where(p => listed.Contains(p.Name)) : Enumerable.Empty<Feature>();
            return CreationOrder.Sort(top, f => SubFeatures(f).Where(IsSketch), Parents, f => f.Name);
        }

        public static bool IsSketch(Feature f) => f.GetTypeName2() == "ProfileFeature" || f.GetTypeName2() == "3DProfileFeature";

        public static Feature FindOrigin(ModelDoc2 doc) =>
            TopLevelFeatures(doc).FirstOrDefault(f => f.GetTypeName2() == "OriginProfileFeature")
            ?? throw new InvalidOperationException("Ursprung des Dokuments nicht gefunden.");

        /// <summary>The single point of the origin "sketch"; used for relations and dimensions to the origin.</summary>
        public static SketchPoint OriginPoint(ModelDoc2 doc)
        {
            var sketch = (Sketch)FindOrigin(doc).GetSpecificFeature2();
            var points = (object[])sketch.GetSketchPoints2();
            return (SketchPoint)points[0];
        }

        public static IEnumerable<(Feature feature, Plane3 plane)> RefPlanes(ModelDoc2 doc, SwMath math)
        {
            foreach (Feature f in TopLevelFeatures(doc))
            {
                if (f.GetTypeName2() != "RefPlane") continue;
                var rp = (RefPlane)f.GetSpecificFeature2();
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
            var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, false);
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
                var mp = (double[])b.GetMassProperties(1.0);
                if (mp == null || mp.Length < 4) continue;
                items.Add((mp[3], new Vec3(mp[0], mp[1], mp[2])));
            }
            return MassState.FromBodies(items);
        }

        /// <summary>The line of a reference axis.</summary>
        public static Line3 AxisLine(RefAxis ax)
        {
            // start point x,y,z, end point x,y,z
            var p = (double[])ax.GetRefAxisParams();
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
            doc is PartDoc p ? (Feature)p.FeatureByName(name) : null;

        public static bool HasFeatureNamed(ModelDoc2 doc, string name) => FeatureByName(doc, name) != null;

        /// <summary>Renames, avoiding clashes by appending " (2)", " (3)" ...</summary>
        public static string Rename(ModelDoc2 doc, Feature f, string wanted)
        {
            if (f.Name == wanted) return wanted;
            string name = wanted;
            for (int i = 2; HasFeatureNamed(doc, name); i++) name = $"{wanted} ({i})";
            f.Name = name;
            return f.Name;
        }

        public static string IdOf(SketchPoint p) => "P" + string.Join("-", (int[])p.GetID());

        public static string IdOf(SketchSegment s) => "S" + string.Join("-", (int[])s.GetID());

        public static string SketchNameOf(Sketch s) => ((Feature)s).Name;
    }
}
