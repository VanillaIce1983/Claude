using System;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Sketch
{
    public enum AxisImage
    {
        PlusX,
        MinusX,
        PlusY,
        MinusY,

        /// <summary>The mirrored axis is not parallel to an axis of the target sketch.</summary>
        Oblique,
    }

    /// <summary>
    /// Where the X and Y axes of the source sketch end up in the target sketch after mirroring.
    /// Horizontal and vertical relations/dimensions only survive if the axes land on axes.
    /// </summary>
    public sealed class AxisMap
    {
        public AxisMap(AxisImage x, AxisImage y, int sigma)
        {
            X = x;
            Y = y;
            Sigma = sigma;
        }

        public AxisImage X { get; }
        public AxisImage Y { get; }

        /// <summary>
        /// +1 if the target sketch normal equals the mirrored source normal, -1 if it is opposite.
        /// See <see cref="Planning.DirectionRules"/>.
        /// </summary>
        public int Sigma { get; }

        public static AxisMap Compute(Frame3 source, Reflection r, Frame3 target)
        {
            int sigma = Planning.DirectionRules.Sigma(source.ZAxis, r, target.ZAxis);
            return new AxisMap(Classify(r.ApplyVector(source.XAxis), target), Classify(r.ApplyVector(source.YAxis), target), sigma);
        }

        /// <summary>
        /// Judged by the off-axis component (sine of the deviation), not by how close the
        /// on-axis component is to 1: a 0.02° tilt changes the cosine by only 6e-8 but moves
        /// the end of a 100 mm line by 35 µm, and a horizontal relation would then pull the
        /// geometry away from the true mirror image.
        /// </summary>
        public static AxisImage Classify(Vec3 mirroredAxis, Frame3 target)
        {
            Vec3 l = target.VectorToLocal(mirroredAxis);
            const double tol = Tol.Angle;
            if (Math.Abs(l.Y) <= tol && Math.Abs(l.Z) <= tol) return l.X > 0 ? AxisImage.PlusX : AxisImage.MinusX;
            if (Math.Abs(l.X) <= tol && Math.Abs(l.Z) <= tol) return l.Y > 0 ? AxisImage.PlusY : AxisImage.MinusY;
            return AxisImage.Oblique;
        }

        public static bool LandsOnX(AxisImage a) => a == AxisImage.PlusX || a == AxisImage.MinusX;
        public static bool LandsOnY(AxisImage a) => a == AxisImage.PlusY || a == AxisImage.MinusY;

        public override string ToString() => $"X→{X}, Y→{Y}, σ={Sigma:+0;-0}";
    }
}
