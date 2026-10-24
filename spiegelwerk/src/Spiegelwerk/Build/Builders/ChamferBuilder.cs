using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Edge chamfer (angle-distance, distance-distance, equal distance). Which adjacent face
    /// receives the first distance depends on the edge's orientation, which a mirror can
    /// reverse; a flipped variant is offered to the verifier.
    /// </summary>
    internal sealed class ChamferBuilder : IFeatureBuilder
    {
        /// <summary>Selection mark for chamfer edges; 1 like fillets, 0 as fallback.</summary>
        private static readonly int[] Marks = { 1, 0 };

        public bool Handles(FeatureRecord record) => record is ChamferRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (ChamferRecord)record;
            var notes = new List<string>();
            var found = new List<object>();
            foreach (var e in r.Edges)
            {
                Edge? edge = ctx.Topology.FindEdge(e.Mirror(ctx.Mirror), notes);
                if (edge != null) found.Add(edge);
            }
            foreach (var fs in r.Faces)
            {
                Face2? face = ctx.Topology.FindFace(fs.Mirror(ctx.Mirror), notes);
                if (face != null) found.Add(face);
            }
            int total = r.Edges.Count + r.Faces.Count;
            if (found.Count == 0) return Fail("Keine der gefasten Kanten wurde im Ziel gefunden.", notes);
            if (found.Count < total) notes.Add($"{total - found.Count} von {total} Kanten/Flächen nicht gefunden; Fase nur an den übrigen.");

            int options = 0;
            if (r.Flip) options |= (int)swFeatureChamferOption_e.swFeatureChamferFlipDirection;
            if (r.KeepFeatures) options |= (int)swFeatureChamferOption_e.swFeatureChamferKeepFeature;
            if (r.TangentPropagation) options |= (int)swFeatureChamferOption_e.swFeatureChamferTangentPropagation;

            ModelDoc2 doc = ctx.Doc;
            Feature? f = null;
            foreach (int mark in Marks)
            {
                SwSelect.Clear(doc);
                for (int i = 0; i < found.Count; i++) SwSelect.Select(doc, found[i], mark, i > 0);
                f = doc.FeatureManager.InsertFeatureChamfer(options, r.ChamferType, r.Distance1, r.Angle, r.Distance2, 0, 0, 0);
                SwSelect.Clear(doc);
                if (f != null) break;
            }
            if (f == null) return Fail("SOLIDWORKS hat die Fase nicht erzeugt.", notes);

            ctx.Register(r.Name, f);
            var result = BuildResult.Ok(f, Describe(r));
            result.Notes.AddRange(notes);
            if (notes.Count > 0) result.Status = ReportStatus.Warning;

            bool asymmetric = r.ChamferType == (int)swChamferType_e.swChamferAngleDistance ||
                              (r.ChamferType == (int)swChamferType_e.swChamferDistanceDistance && System.Math.Abs(r.Distance1 - r.Distance2) > 1e-9);
            if (asymmetric)
                result.Alternatives.Add(new Alternative("Fasenrichtung umgekehrt", () => FeatureEdit.Chamfer(doc, f, d =>
                {
                    if (d.Edges is object[] edges)
                        foreach (object edge in edges) d.SetIsFlipped(edge, !d.GetIsFlipped(edge));
                })));
            return result;
        }

        private static string Describe(ChamferRecord r) => (swChamferType_e)r.ChamferType switch
        {
            swChamferType_e.swChamferAngleDistance => $"Winkel-Abstand {r.Distance1 * 1000:0.###} mm × {r.Angle * 180 / System.Math.PI:0.##}°",
            swChamferType_e.swChamferDistanceDistance => $"Abstand-Abstand {r.Distance1 * 1000:0.###} / {r.Distance2 * 1000:0.###} mm",
            _ => $"gleicher Abstand {r.Distance1 * 1000:0.###} mm",
        };

        private static BuildResult Fail(string message, List<string> notes)
        {
            var result = BuildResult.Failed(message);
            result.Notes.AddRange(notes);
            return result;
        }
    }
}
