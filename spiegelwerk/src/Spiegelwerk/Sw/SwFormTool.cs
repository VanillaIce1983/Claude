using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Forming tools (blech-plan S9, A4 (2) §5): FormToolInstance has no data interface before 2025 SP2 (then
    /// ILibraryFormToolFeatureData, late-bound only – neither the 2019 nor the 2024 interop knows it). The tool file
    /// comes from the feature's external references (path only, A4 P11), late-bound from its data, or from the
    /// design library by the feature's name; the copy is placed with InsertFormToolFeature (2019 on).
    /// </summary>
    internal static class SwFormTool
    {
        public const string TypeName = "FormToolInstance";

        /// <summary>VBA's „Nothing“ for the component of AccessSelections: a null dispatch pointer.</summary>
        private static object Nothing => new DispatchWrapper(null);

        /// <summary>The sketch sub-features of a forming tool in their order: its position sketch first, then its orientation sketch.</summary>
        public static List<Feature> Sketches(Feature tool) => SwDoc.SubFeatures(tool).Where(SwDoc.IsSketch).ToList();

        /// <summary>The files a feature names as external references (ListExternalFileReferences2, the path only); none if it does not read.</summary>
        public static List<string> ReferencedFiles(Feature f)
        {
            var result = new List<string>();
            try
            {
                if (f.ListExternalFileReferencesCount() <= 0) return result;
                f.ListExternalFileReferences2(out object models, out object _, out object _, out object _, out object _, out object _, out object _, out int _, out string _);
                result.AddRange(Strings(models).Where(s => s.Length > 0));
            }
            catch (Exception)
            {
                // Then the data (2025 SP2) or the design library.
            }
            return result;
        }

        /// <summary>The strings of a COM array (string[], object[]) or a single string.</summary>
        private static IEnumerable<string> Strings(object? o) => o switch
        {
            string s => new[] { s },
            string[] ss => ss,
            object[] os => os.OfType<string>(),
            _ => Enumerable.Empty<string>(),
        };

        /// <summary>Late-bound (2025 SP2 and later): what the tool's data say; null where they do not answer.</summary>
        public static (string? path, string? configuration, bool? link, double? angle) FromData(object? def)
        {
            if (def == null) return (null, null, null, null);
            string? path = SwLate.Get(def, "FormToolPath") as string;
            string? configuration = SwLate.Get(def, "FormToolConfiguration") as string;
            bool? link = SwLate.Get(def, "LinkToFormTool") is bool b ? b : (bool?)null;
            double? angle = SwLate.Get(def, "RotationAngle") is double a ? a : (double?)null;
            return (string.IsNullOrWhiteSpace(path) ? null : path, configuration, link, angle);
        }

        /// <summary>
        /// Late-bound (2025 SP2 and later): the flat-pattern display of the tool (OverrideSettings, ShowPunch, ShowProfile,
        /// ShowCenter); overrideDoc null where the data do not answer.
        /// </summary>
        public static (bool? overrideDoc, bool punch, bool profile, bool center) DisplayFromData(object? def)
        {
            if (def == null || !(SwLate.Get(def, "OverrideSettings") is bool overrideDoc)) return (null, false, false, false);
            return (overrideDoc, SwLate.Get(def, "ShowPunch") is bool punch && punch, SwLate.Get(def, "ShowProfile") is bool profile && profile,
                    SwLate.Get(def, "ShowCenter") is bool center && center);
        }

        /// <summary>Directories looked at in the design library at most (a library on a network drive can be large).</summary>
        private const int MaxDirectories = 5000;

        /// <summary>
        /// A tool file in the folders of the design library and of the forming-tool palette (swFileLocationsDesignLibrary,
        /// swFileLocationsPaletteFormTools; several separated by „;“), searched through their subfolders for one of
        /// <paramref name="fileNames"/> (case ignored); null if none is found. A folder that cannot be read is skipped.
        /// </summary>
        public static string? SearchLibrary(SldWorks? sw, IReadOnlyList<string> fileNames)
        {
            if (sw == null || fileNames.Count == 0) return null;
            var roots = new List<string>();
            foreach (int pref in new[] { (int)swUserPreferenceStringValue_e.swFileLocationsDesignLibrary, (int)swUserPreferenceStringValue_e.swFileLocationsPaletteFormTools })
            {
                try
                {
                    foreach (string folder in (sw.GetUserPreferenceStringValue(pref) ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
                        if (!roots.Contains(folder, StringComparer.OrdinalIgnoreCase))
                            roots.Add(folder);
                }
                catch (Exception)
                {
                    // That preference is not readable.
                }
            }
            foreach (string name in fileNames)
            {
                int seen = 0;
                foreach (string root in roots)
                    if (Find(root, name, ref seen) is string hit)
                        return hit;
            }
            return null;
        }

        private static string? Find(string root, string fileName, ref int seen)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0 && seen < MaxDirectories)
            {
                string dir = pending.Pop();
                seen++;
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string file in Directory.EnumerateFiles(dir))
                        if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                            return file;
                    foreach (string sub in Directory.EnumerateDirectories(dir)) pending.Push(sub);
                }
                catch (Exception)
                {
                    // Not readable: the others still are.
                }
            }
            return null;
        }

        /// <summary>The configurations of the tool file (ISldWorks.GetConfigurationNames); none if it does not read.</summary>
        public static List<string> ConfigurationNames(SldWorks sw, string path)
        {
            try
            {
                return Strings((object)sw.GetConfigurationNames(path)).ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        /// <summary>The points of a sketch in model coordinates: its user points (the instances of a position sketch) or all its points.</summary>
        public static List<Vec3> SketchPoints(SwMath math, Feature sketchFeature, bool userOnly)
        {
            var result = new List<Vec3>();
            try
            {
                if (!((object)sketchFeature.GetSpecificFeature2() is Sketch sketch) || sketch.Is3D()) return result;
                Frame3 frame = math.SketchFrame(sketch);
                object? points = userOnly ? (object)sketch.GetUserPoints2() : (object)sketch.GetSketchPoints2();
                foreach (SketchPoint p in (points as object[] ?? Array.Empty<object>()).OfType<SketchPoint>()) result.Add(frame.ToWorld(p.X, p.Y));
            }
            catch (Exception)
            {
                result.Clear();
            }
            return result;
        }

        /// <summary>
        /// Places the tool on what is selected at mark 0 (a sketch with points: one instance at each; a face: one), with the
        /// document's flat-pattern display unless <paramref name="overrideDoc"/> (then punch, profile and centre as given – they
        /// count only so); null if SOLIDWORKS makes none or the call throws.
        /// </summary>
        public static Feature? Insert(ModelDoc2 doc, string path, bool flip, double angle, string configuration, bool link,
                                      bool overrideDoc = false, bool punch = false, bool profile = false, bool center = false)
        {
            try
            {
                return doc.FeatureManager.InsertFormToolFeature(path, flip, angle, configuration, overrideDoc,
                    overrideDoc && punch, overrideDoc && profile, overrideDoc && center, link);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 2025 SP2 and later: turns the tool in place (ILibraryFormToolFeatureData.RotationAngle + ModifyDefinition, late-bound);
        /// false where the data do not answer or the edit does not take – the caller places it anew.
        /// </summary>
        public static bool TryRotate(ModelDoc2 doc, Feature f, double angle)
        {
            object? def;
            try
            {
                def = (object?)f.GetDefinition();
            }
            catch (Exception)
            {
                return false;
            }
            if (def == null || !(SwLate.Get(def, "RotationAngle") is double)) return false;
            if (!SwLate.Call(def, "AccessSelections", doc, Nothing)) return false;
            bool ok = false;
            try
            {
                ok = SwLate.Set(def, "RotationAngle", angle) && f.ModifyDefinition(def, doc, null);
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (!ok) SwLate.Call(def, "ReleaseSelectionAccess");
            }
        }

        /// <summary>The faces of a feature; none if they do not read.</summary>
        public static List<Face2> Faces(Feature f)
        {
            try
            {
                return ((object)f.GetFaces() as object[])?.OfType<Face2>().ToList() ?? new List<Face2>();
            }
            catch (Exception)
            {
                return new List<Face2>();
            }
        }

        /// <summary>The kernel's distance of a point from the nearest of the faces; infinite without faces.</summary>
        public static double Distance(IReadOnlyList<Face2> faces, Vec3 p)
        {
            double best = double.PositiveInfinity;
            foreach (Face2 face in faces)
            {
                try
                {
                    best = Math.Min(best, TopologyCapture.DistanceToFace(face, p));
                }
                catch (Exception)
                {
                    // A face that does not answer counts as far.
                }
                if (best <= FormToolPlacement.CheckTolerance / 10) break;
            }
            return best;
        }

        /// <summary>Points on a feature's faces: a point on each face and the ends and middles of their edges; none if they do not read.</summary>
        public static List<Vec3> ShapePoints(Feature f)
        {
            var points = new List<Vec3>();
            try
            {
                foreach (Face2 face in Faces(f)) points.Add(TopologyCapture.Face(face).Sample);
            }
            catch (Exception)
            {
                points.Clear();
            }
            points.AddRange(SwSheetMetal.FacePoints(f));
            return points;
        }
    }
}
