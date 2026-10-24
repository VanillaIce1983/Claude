using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;

namespace Spiegelwerk.Capture
{
    /// <summary>Fingerprints of edges, faces and vertices (used on both sides: original and rebuilt part).</summary>
    internal static class TopologyCapture
    {
        public static EdgeSignature Edge(Edge e)
        {
            CurveParamData cp = e.GetCurveParams3();
            var curve = (Curve)e.GetCurve();
            Vec3 start = Vec3.FromArray((double[])cp.StartPoint);
            Vec3 end = Vec3.FromArray((double[])cp.EndPoint);
            double u0 = cp.UMinValue, u1 = cp.UMaxValue;
            var midEval = (double[])curve.Evaluate2((u0 + u1) / 2, 0);
            Vec3 mid = Vec3.FromArray(midEval);
            double length = curve.GetLength3(u0, u1);

            CurveKind kind;
            if (curve.IsLine()) kind = CurveKind.Line;
            else if (curve.IsCircle()) kind = CurveKind.Circle;
            else if (curve.Identity() == (int)swCurveTypes_e.ELLIPSE_TYPE) kind = CurveKind.Ellipse;
            else kind = CurveKind.Other;

            var sig = new EdgeSignature(kind, start, end, mid, length);
            if (kind == CurveKind.Circle)
            {
                // centre x,y,z, axis x,y,z, radius
                var c = (double[])curve.CircleParams;
                sig.Center = Vec3.FromArray(c, 0);
                sig.Axis = Vec3.FromArray(c, 3).Normalized();
                sig.Radius = c[6];
            }
            else if (kind == CurveKind.Line)
            {
                // root point x,y,z, direction x,y,z
                var l = (double[])curve.LineParams;
                sig.Axis = Vec3.FromArray(l, 3).Normalized();
            }
            return sig;
        }

        public static FaceSignature Face(Face2 f)
        {
            var surface = (Surface)f.GetSurface();
            SurfaceKind kind = surface.Identity() switch
            {
                (int)swSurfaceTypes_e.PLANE_TYPE => SurfaceKind.Plane,
                (int)swSurfaceTypes_e.CYLINDER_TYPE => SurfaceKind.Cylinder,
                (int)swSurfaceTypes_e.CONE_TYPE => SurfaceKind.Cone,
                (int)swSurfaceTypes_e.SPHERE_TYPE => SurfaceKind.Sphere,
                (int)swSurfaceTypes_e.TORUS_TYPE => SurfaceKind.Torus,
                _ => SurfaceKind.Other,
            };

            // A point on the face: the face point closest to the centre of its bounding box.
            var box = (double[])f.GetBox();
            var centre = new Vec3((box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2);
            var closest = (double[])f.GetClosestPointOn(centre.X, centre.Y, centre.Z);
            var sig = new FaceSignature(kind, f.GetArea(), Vec3.FromArray(closest));
            if (kind == SurfaceKind.Plane)
                sig.Normal = Vec3.FromArray((double[])f.Normal).Normalized();
            return sig;
        }

        public static Vec3 Vertex(Vertex v) => Vec3.FromArray((double[])v.GetPoint());

        /// <summary>Distance from a point to a face, by the kernel.</summary>
        public static double DistanceToFace(Face2 f, Vec3 p)
        {
            var c = (double[])f.GetClosestPointOn(p.X, p.Y, p.Z);
            return c == null ? double.PositiveInfinity : Vec3.FromArray(c).DistanceTo(p);
        }

        /// <summary>Planar face → plane with outward normal.</summary>
        public static Plane3? PlaneOfFace(Face2 f)
        {
            var surface = (Surface)f.GetSurface();
            if (!surface.IsPlane()) return null;
            FaceSignature sig = Face(f);
            return new Plane3(sig.Sample, sig.Normal);
        }

        public static Line3? LineOfEdge(Edge e)
        {
            var curve = (Curve)e.GetCurve();
            if (!curve.IsLine()) return null;
            var l = (double[])curve.LineParams;
            return new Line3(Vec3.FromArray(l, 0), Vec3.FromArray(l, 3));
        }

    }
}
