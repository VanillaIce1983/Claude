using System;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Matching
{
    public enum SurfaceKind
    {
        Plane,
        Cylinder,
        Cone,
        Sphere,
        Torus,
        Other,
    }

    /// <summary>Geometric fingerprint of a model face.</summary>
    public sealed class FaceSignature
    {
        public FaceSignature(SurfaceKind kind, double area, Vec3 sample)
        {
            Kind = kind;
            Area = area;
            Sample = sample;
        }

        public SurfaceKind Kind { get; }
        public double Area { get; }

        /// <summary>A point that lies on the face.</summary>
        public Vec3 Sample { get; }

        /// <summary>Planar faces: outward normal (a reflection keeps outward normals outward).</summary>
        public Vec3 Normal { get; set; }

        public FaceSignature Mirror(Reflection r) => new FaceSignature(Kind, Area, r.Apply(Sample))
        {
            Normal = r.ApplyVector(Normal),
        };

        /// <summary>
        /// Pre-filter without SOLIDWORKS: same surface type, same area, same normal for planes.
        /// Whether <see cref="Sample"/> lies on the candidate is checked by the caller.
        /// </summary>
        public bool CouldBe(FaceSignature candidate)
        {
            if (Kind != candidate.Kind) return false;
            double areaTol = Math.Max(1e-12, Area * Tol.Relative * 10);
            if (Math.Abs(Area - candidate.Area) > areaTol) return false;
            if (Kind == SurfaceKind.Plane && !Normal.IsSameDirection(candidate.Normal, 1e-6)) return false;
            return true;
        }

        public override string ToString() => $"{Kind} A={Area:0.##########} bei {Sample}";
    }
}
