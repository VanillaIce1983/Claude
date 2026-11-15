using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Corner relief, late-bound (blech-plan S5, B49): ICornerReliefFeatureData and ISMCornerReliefData exist
    /// only from the 2022 interop, the add-in is compiled against 2019. Every member goes through
    /// <see cref="SwLate"/>; the out parameters (GetCornerAtIndex, AddNewCorner, GetFaces) through
    /// <see cref="SwLate.InvokeOut"/>. Its type name is not confirmed (B44): a definition that answers
    /// CornerCount is one.
    /// </summary>
    internal static class SwCornerRelief
    {
        /// <summary>VBA's „Nothing“ for the component of AccessSelections: a null dispatch pointer.</summary>
        private static object Nothing => new DispatchWrapper(null);

        /// <summary>The definition is a corner relief's (it answers CornerCount).</summary>
        public static bool Is(object? def) => def != null && SwLate.Get(def, "CornerCount") is int;

        public static bool Access(ModelDoc2 doc, object def) => SwLate.Call(def, "AccessSelections", doc, Nothing);

        public static void Release(object def) => SwLate.Call(def, "ReleaseSelectionAccess");

        public static int CornerCount(object def) => SwLate.Get(def, "CornerCount") is int n ? n : 0;

        public static int CornerType(object def) => SwLate.Get(def, "CornerType") is int t ? t : CornerReliefBendTypes.TwoBend;

        /// <summary>
        /// The corner at a zero-based position (GetCornerAtIndex counts from 1, like RemoveCorner and
        /// ISMCornerReliefData.CornerIndex; out parameter); null if it does not come back or the call reports
        /// an error (swCornerReliefError_e, 0 = none).
        /// </summary>
        public static object? CornerAt(object def, int index)
        {
            var args = new object?[] { index + 1, null };
            object? code = SwLate.InvokeOut(def, "GetCornerAtIndex", args, new[] { false, true });
            if (code is int c && c != 0) return null;
            return args[1] is DBNull ? null : args[1];
        }

        /// <summary>The two or three faces of a corner (GetFaces, three out parameters); a missing third is null.</summary>
        public static (Face2? f1, Face2? f2, Face2? f3) Faces(object corner)
        {
            var args = new object?[] { null, null, null };
            SwLate.InvokeOut(corner, "GetFaces", args, new[] { true, true, true });
            return (args[0] as Face2, args[1] as Face2, args[2] as Face2);
        }

        /// <summary>The values of a corner into an item (its faces are the caller's).</summary>
        public static void ReadValues(object corner, CornerReliefItem item)
        {
            item.ReliefType = SwLate.Get(corner, "ReliefType") is int type ? type : -1;
            item.SuitCaseType = SwLate.Get(corner, "SuitCaseType") is int suitcase ? suitcase : 0;
            item.SlotLength = SwLate.Get(corner, "SlotLength") is double length ? length : 0;
            item.SlotWidth = SwLate.Get(corner, "SlotWidth") is double width ? width : 0;
            item.CornerFilletDiameter = SwLate.Get(corner, "CornerFilletDiameter") is double diameter ? diameter : 0;
            item.AddFilletedCorners = SwLate.Get(corner, "AddFilletedCorners") is bool filleted && filleted;
            item.CenterOnBendLines = SwLate.Get(corner, "CenterOnBendLines") is bool centred && centred;
            item.RatioToThickness = SwLate.Get(corner, "RatioToThickness") is bool ratio && ratio;
            item.TangentToBend = SwLate.Get(corner, "TangentToBend") is bool tangent && tangent;
        }

        /// <summary>The values of an item into a corner; the names of those that did not take.</summary>
        public static List<string> WriteValues(object corner, CornerReliefItem item)
        {
            var failed = new List<string>();
            void Set(string name, object value)
            {
                if (!SwLate.Set(corner, name, value)) failed.Add(name);
            }
            // The type first: the sizes belong to it.
            Set("ReliefType", item.ReliefType);
            Set("RatioToThickness", item.RatioToThickness);
            Set("SlotLength", item.SlotLength);
            Set("SlotWidth", item.SlotWidth);
            Set("AddFilletedCorners", item.AddFilletedCorners);
            Set("CornerFilletDiameter", item.CornerFilletDiameter);
            Set("CenterOnBendLines", item.CenterOnBendLines);
            Set("TangentToBend", item.TangentToBend);
            Set("SuitCaseType", item.SuitCaseType);
            return failed;
        }

        /// <summary>The body the relief acts on (GetBodyScope); null if it does not read.</summary>
        public static Body2? BodyScope(object def) => SwLate.Invoke(def, "GetBodyScope") as Body2;

        /// <summary>Initialize(CornerType) of a new data object.</summary>
        public static bool Initialize(object def, int cornerType) => SwLate.Invoke(def, "Initialize", cornerType) is bool ok && ok;

        /// <summary>SetBodyScope: the error code (swCornerReliefError_e, 0 = none); null if the call did not answer.</summary>
        public static int? SetBodyScope(object def, Body2 body) => SwLate.Invoke(def, "SetBodyScope", body) is int code ? code : (int?)null;

        /// <summary>AddNewCorner: the error code and the new corner; null code if the call did not answer.</summary>
        public static int? AddNewCorner(object def, Face2 f1, Face2 f2, Face2? f3, int reliefType, out object? corner)
        {
            var args = new object?[] { f1, f2, f3 == null ? Nothing : f3, reliefType, null };
            object? code = SwLate.InvokeOut(def, "AddNewCorner", args, new[] { false, false, false, false, true });
            corner = args[4] is DBNull ? null : args[4];
            return code is int c ? c : (int?)null;
        }

        /// <summary>
        /// The corners of a relief as they are now, read under its selection access, each paired with the wanted
        /// (mirrored) corners by its faces (<see cref="Core.Planning.CornerRules.PairCorners"/>, inside the access,
        /// where the faces are valid). Null if the data do not open; <paramref name="pairs"/> null if they do not pair.
        /// </summary>
        public static List<CornerReliefItem>? ReadBack(ModelDoc2 doc, Feature f, IReadOnlyList<CornerReliefItem> wanted, out int[]? pairs, out int cornerType)
        {
            pairs = null;
            cornerType = -1;
            object? def = (object)f.GetDefinition();
            if (!Is(def) || !Access(doc, def!)) return null;
            try
            {
                cornerType = CornerType(def!);
                var items = new List<CornerReliefItem>();
                var faces = new List<List<Face2>>();
                int count = CornerCount(def!);
                for (int i = 0; i < count; i++)
                {
                    object? corner = CornerAt(def!, i);
                    if (corner == null) return items;
                    var (f1, f2, f3) = Faces(corner);
                    var item = new CornerReliefItem();
                    ReadValues(corner, item);
                    var list = new[] { f1, f2, f3 }.Where(x => x != null).Select(x => x!).ToList();
                    faces.Add(list);
                    items.Add(item);
                }
                List<IReadOnlyList<FaceSignature>> sigs = faces.Select(l => (IReadOnlyList<FaceSignature>)l.Select(TopologyCapture.Face).ToList()).ToList();
                pairs = Core.Planning.CornerRules.PairCorners(wanted, sigs, (j, k, p) => TopologyCapture.DistanceToFace(faces[j][k], p));
                return items;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Release(def!);
            }
        }
    }
}
