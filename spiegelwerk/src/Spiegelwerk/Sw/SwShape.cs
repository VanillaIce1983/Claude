using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Build;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// The shape of the target against points (A3 P4, blech-plan K10): the witness check of sketched
    /// bends and jogs, always on – independent of „Jeden Schritt prüfen“.
    /// </summary>
    internal static class SwShape
    {
        /// <summary>A point lies on a face within this distance (as the face samples of the check).</summary>
        public const double Tolerance = 1e-5;

        /// <summary>
        /// How many of the points lie on no face, by the kernel's distance: faces of the job's own bodies
        /// (<paramref name="ownOnly"/>, separate body – the original may have a face at the same place)
        /// or of every solid body. A face the kernel cannot measure counts as missed for that point.
        /// </summary>
        public static int Misses(BuildContext ctx, IEnumerable<Vec3> points, bool ownOnly)
        {
            List<Vec3> list = points.ToList();
            if (list.Count == 0) return 0;
            List<Face2> faces = (ownOnly ? ctx.OwnBodies() : SwDoc.SolidBodies(ctx.Doc))
                .SelectMany(b => ((object)b.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>())
                .ToList();
            int missed = 0;
            foreach (Vec3 p in list)
                if (!faces.Any(f => On(f, p)))
                    missed++;
            return missed;
        }

        private static bool On(Face2 face, Vec3 p)
        {
            try
            {
                return TopologyCapture.DistanceToFace(face, p) <= Tolerance;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
