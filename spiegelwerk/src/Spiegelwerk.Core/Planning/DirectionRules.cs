using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// How direction flags change when a feature is rebuilt mirrored.
    ///
    /// A sketch on plane S (normal z) becomes a sketch on plane S' = R(S). The target
    /// sketch normal z' is either R(z) or -R(z), whatever SOLIDWORKS defines for the plane
    /// we sketch on. σ = sign(z' · R(z)) captures which one it is:
    ///
    ///  σ = +1: the in-plane mapping is a reflection; 2D geometry appears mirrored, arcs
    ///          change their sense of rotation, "along the normal" stays along the normal.
    ///  σ = -1: the in-plane mapping is a rotation; arcs keep their sense, but every
    ///          direction defined relative to the sketch normal (extrude depth, revolve start
    ///          side) has to be reversed.
    /// </summary>
    public static class DirectionRules
    {
        /// <remarks>
        /// The same comparison serves any reference direction a flag is relative to – the
        /// normal of a neutral plane, a linear edge giving the pull direction: the copy's
        /// reference points along R(original) (σ = +1) or against it (σ = −1).
        /// </remarks>
        public static int Sigma(Vec3 sourceNormal, Reflection r, Vec3 targetNormal) =>
            targetNormal.Dot(r.ApplyVector(sourceNormal)) >= 0 ? 1 : -1;

        /// <summary>
        /// Extrude/cut "reverse direction". Independent of whether SOLIDWORKS extrudes a boss
        /// along and a cut against the normal: that convention is the same for original and copy.
        /// </summary>
        public static bool ReverseFlag(bool sourceReverse, int sigma) => sigma < 0 ? !sourceReverse : sourceReverse;

        /// <summary>Arc sense of rotation about the target sketch normal (+1 CCW, -1 CW).</summary>
        public static int ArcDirection(int sourceDirection, int sigma) => -sigma * sourceDirection;

        /// <summary>
        /// The world direction an extrude actually goes, for checking: base is +z for a boss
        /// and -z for a cut.
        /// </summary>
        public static Vec3 ExtrudeDirection(Vec3 sketchNormal, bool isCut, bool reverse)
        {
            Vec3 d = isCut ? -sketchNormal : sketchNormal;
            return reverse ? -d : d;
        }
    }
}
