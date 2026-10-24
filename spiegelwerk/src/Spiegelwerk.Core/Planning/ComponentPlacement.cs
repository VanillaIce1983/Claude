using System;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Placing the mirrored part in an assembly.
    ///
    /// Original component: part geometry x, placement T (part → assembly).
    /// Wanted in the assembly: R_asm(T(x)).
    /// The new part is built as R_part(x) with any reflection R_part we like.
    /// Its placement T' must satisfy T'(R_part(x)) = R_asm(T(x)), so
    ///     T' = R_asm ∘ T ∘ R_part        (R_part is its own inverse)
    /// Two reflections and a rigid motion give det = +1: a valid component transform.
    ///
    /// Because R_part is free, the part is mirrored about one of its own standard planes
    /// through its origin. The new part then looks like a designed opposite-hand part
    /// (sketches on front/top/right) instead of being shifted somewhere in space.
    /// </summary>
    public static class ComponentPlacement
    {
        public static Affine MirroredPlacement(Affine original, Reflection assemblyMirror, Reflection partMirror)
        {
            Affine t = assemblyMirror.ToAffine().Compose(original).Compose(partMirror.ToAffine());
            if (!t.IsProperRigid(1e-8))
                throw new InvalidOperationException("Berechnete Platzierung ist keine starre Bewegung.");
            return t;
        }

        /// <summary>
        /// The standard plane of the part (through its origin) whose normal is closest to the
        /// assembly mirror plane's normal seen from the part. Any choice is correct; the closest
        /// one keeps the new part's orientation intuitive.
        /// </summary>
        public static Plane3 ChoosePartMirrorPlane(Affine original, Plane3 assemblyMirrorPlane)
        {
            Vec3 local = original.Inverse().ApplyVector(assemblyMirrorPlane.Normal).Normalized();
            double ax = Math.Abs(local.X), ay = Math.Abs(local.Y), az = Math.Abs(local.Z);
            if (ax >= ay && ax >= az) return Plane3.RightYZ;
            if (ay >= az) return Plane3.TopXZ;
            return Plane3.FrontXY;
        }

        /// <summary>The assembly mirror plane expressed in part coordinates (exact, possibly off the origin).</summary>
        public static Plane3 ExactPartMirrorPlane(Affine original, Plane3 assemblyMirrorPlane)
        {
            Affine inv = original.Inverse();
            return new Plane3(inv.Apply(assemblyMirrorPlane.Origin), inv.ApplyVector(assemblyMirrorPlane.Normal));
        }
    }
}
