using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Capture
{
    /// <summary>Variable radius fillet: edges, vertex radii and control points, in the state the fillet saw them.</summary>
    internal sealed partial class FeatureCapture
    {
        private VariableFilletRecord CaptureVariableFillet(Feature f, string type, VariableFilletFeatureData2 d)
        {
            if (d.ConicTypeForCrossSectionProfile != (int)swFeatureFilletProfileType_e.swFeatureFilletCircular)
                throw new NotSupportedException("Nur kreisförmige Verrundungsprofile werden unterstützt (kein Kegelschnitt).");
            if (d.AsymmetricFillet)
                throw new NotSupportedException("Asymmetrische variable Verrundungen werden nicht unterstützt.");

            var r = new VariableFilletRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.DefaultRadius = d.DefaultRadius;
                r.TransitionType = d.TransitionType;
                r.OverflowType = d.OverflowType;
                r.Propagate = d.PropagateToTangentFaces;
                r.CurvatureContinuous = d.CurvatureContinuous;

                var edges = new List<Edge>();
                for (int i = 0; i < d.FilletEdgeCount; i++)
                    if (d.GetFilletEdgeAtIndex(i) is Edge e)
                    {
                        edges.Add(e);
                        r.Edges.Add(TopologyCapture.Edge(e));
                    }
                if (r.Edges.Count == 0) throw new NotSupportedException("Variable Verrundung ohne lesbare Kanten.");

                // Radii at the vertices that have one assigned; the others follow the default.
                foreach (Edge e in edges)
                    foreach (Vertex v in new[] { e.GetStartVertex(), e.GetEndVertex() }.OfType<Vertex>())
                    {
                        double radius = d.GetRadius2(v, out bool assigned);
                        Vec3 p = TopologyCapture.Vertex(v);
                        if (assigned && !r.Vertices.Any(x => x.Point.AlmostEquals(p)))
                            r.Vertices.Add(new VertexRadius(p, radius));
                    }

                for (int i = 0; i < d.GetControlPointsCount(); i++)
                {
                    double radius = d.GetControlPointRadiusAtIndex(i, out double location, out Edge? edge);
                    int index = edge == null ? -1 : TopologyMatcher.FindEdge(TopologyCapture.Edge(edge), r.Edges).Index;
                    if (index < 0)
                    {
                        r.Notes.Add($"Kontrollpunkt {i + 1}: Kante nicht zuzuordnen, entfällt.");
                        continue;
                    }
                    r.ControlPoints.Add(new ControlPoint(index, location, radius));
                }
                r.LocationsInPercent = r.ControlPoints.Any(c => c.Location > 1 + 1e-9);

                if (d.GetSetbackVerticesCount() > 0)
                    r.Notes.Add("Rücksprungabstände an Ecken werden nicht übernommen.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }
    }
}
