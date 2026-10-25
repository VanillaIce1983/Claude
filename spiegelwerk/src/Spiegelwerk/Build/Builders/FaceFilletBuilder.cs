using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Face fillet and full round fillet with FeatureFillet3, after selecting the face sets
    /// with the documented marks: side set 1 with 2, side set 2 with 4, the centre set of a
    /// full round fillet with 512. The "reverse face normal" flags go in as options – outward
    /// normals stay outward under a mirror, so they carry over unchanged; the opposite is still
    /// offered to the verifier. Hold lines are set afterwards on the feature data.
    /// </summary>
    internal sealed class FaceFilletBuilder : IFeatureBuilder
    {
        private const int Set1Mark = 2, Set2Mark = 4, CenterMark = 512;

        public bool Handles(FeatureRecord record) => record is FaceFilletRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (FaceFilletRecord)record;
            var notes = new List<string>();

            List<object>? set1 = FindAll(ctx, r.Set1, notes);
            List<object>? set2 = FindAll(ctx, r.Set2, notes);
            List<object>? center = r.Kind == FaceFilletKind.FullRound ? FindAll(ctx, r.Center, notes) : new List<object>();
            if (set1 == null || set2 == null || center == null)
                return Fail("Eine Fläche der Verrundung wurde im Ziel nicht gefunden.", notes);

            var holds = new List<object>();
            foreach (EdgeSignature e in r.HoldLines)
            {
                Edge? found = ctx.Topology.FindEdge(e.Mirror(ctx.Mirror), notes);
                if (found != null) holds.Add(Safe(found)!);
            }
            if (holds.Count < r.HoldLines.Count)
                notes.Add($"{r.HoldLines.Count - holds.Count} von {r.HoldLines.Count} Haltelinien nicht gefunden.");

            int options = 0;
            if (r.Propagate) options |= (int)swFeatureFilletOptions_e.swFeatureFilletPropagate;
            if (r.KeepFeatures) options |= (int)swFeatureFilletOptions_e.swFeatureFilletKeepFeatures;
            if (r.CurvatureContinuous) options |= (int)swFeatureFilletOptions_e.swFeatureFilletCurvatureContinuous;
            if (r.ConstantWidth) options |= (int)swFeatureFilletOptions_e.swFeatureFilletConstantWidth;
            if (r.ReverseNormal1) options |= (int)swFeatureFilletOptions_e.swFeatureFilletReverseFace1Dir;
            if (r.ReverseNormal2) options |= (int)swFeatureFilletOptions_e.swFeatureFilletReverseFace2Dir;

            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            bool selected = true;
            bool first = true;
            void Select(List<object> faces, int mark)
            {
                foreach (object face in faces)
                {
                    selected &= SwSelect.Select(doc, face, mark, !first);
                    first = false;
                }
            }
            // Order as the user interface asks for a full round fillet: side 1, centre, side 2.
            Select(set1, Set1Mark);
            Select(center, CenterMark);
            Select(set2, Set2Mark);
            int type = r.Kind == FaceFilletKind.FullRound
                ? (int)swFeatureFilletType_e.swFeatureFilletType_FullRound
                : (int)swFeatureFilletType_e.swFeatureFilletType_Face;
            Feature? f = selected
                ? doc.FeatureManager.FeatureFillet3(options, r.Radius, 0, 0, type, r.OverflowType,
                    (int)swFeatureFilletProfileType_e.swFeatureFilletCircular, null, null, null, null, null, null, null) as Feature
                : null;
            SwSelect.Clear(doc);
            if (f == null) return Fail("SOLIDWORKS hat die Verrundung nicht erzeugt.", notes);

            if (r.Kind == FaceFilletKind.Face)
            {
                bool ok = FeatureEdit.SimpleFillet(doc, f, d =>
                {
                    const int s1 = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet1, s2 = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet2;
                    if (d.get_ReverseFaceNormal(s1) != r.ReverseNormal1) d.set_ReverseFaceNormal(s1, r.ReverseNormal1);
                    if (d.get_ReverseFaceNormal(s2) != r.ReverseNormal2) d.set_ReverseFaceNormal(s2, r.ReverseNormal2);
                    if (holds.Count > 0) d.HoldLines = holds.ToArray();
                });
                if (!ok) notes.Add("Flächennormalen oder Haltelinien ließen sich nicht setzen.");
            }

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, r.Kind == FaceFilletKind.FullRound
                ? $"Vollständige Verrundung, {r.Set1.Count} + {r.Center.Count} + {r.Set2.Count} Flächen"
                : $"Flächenverrundung R = {r.Radius * 1000:0.###} mm, {r.Set1.Count} + {r.Set2.Count} Flächen" + (holds.Count > 0 ? $", {holds.Count} Haltelinien" : ""),
                notes);
            if (r.Kind == FaceFilletKind.Face)
            {
                result.Alternatives.Add(new Alternative("Normale der Flächengruppe 1 umgekehrt", () => FeatureEdit.SimpleFillet(doc, f, d =>
                    d.set_ReverseFaceNormal((int)swSimpleFilletWhichFaces_e.swFaceFilletSet1, !d.get_ReverseFaceNormal((int)swSimpleFilletWhichFaces_e.swFaceFilletSet1)))));
                result.Alternatives.Add(new Alternative("Normale der Flächengruppe 2 umgekehrt", () => FeatureEdit.SimpleFillet(doc, f, d =>
                    d.set_ReverseFaceNormal((int)swSimpleFilletWhichFaces_e.swFaceFilletSet2, !d.get_ReverseFaceNormal((int)swSimpleFilletWhichFaces_e.swFaceFilletSet2)))));
            }
            return result;
        }

        /// <summary>All faces of a set, or null if one is missing – a fillet between other faces is a different fillet.</summary>
        private static List<object>? FindAll(BuildContext ctx, List<FaceSignature> faces, List<string> notes)
        {
            var result = new List<object>();
            foreach (FaceSignature face in faces)
            {
                Face2? found = ctx.Topology.FindFace(face.Mirror(ctx.Mirror), notes);
                if (found == null) return null;
                result.Add(found);
            }
            return result;
        }
    }
}
