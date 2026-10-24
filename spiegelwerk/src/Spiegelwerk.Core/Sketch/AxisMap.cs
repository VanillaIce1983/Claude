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

        public static AxisImage Classify(Vec3 mirroredAxis, Frame3 target)
        {
            Vec3 l = target.VectorToLocal(mirroredAxis);
            const double tol = 1e-7;
            if (Math.Abs(l.X - 1) < tol) return AxisImage.PlusX;
            if (Math.Abs(l.X + 1) < tol) return AxisImage.MinusX;
            if (Math.Abs(l.Y - 1) < tol) return AxisImage.PlusY;
            if (Math.Abs(l.Y + 1) < tol) return AxisImage.MinusY;
            return AxisImage.Oblique;
        }

        public static bool LandsOnX(AxisImage a) => a == AxisImage.PlusX || a == AxisImage.MinusX;
        public static bool LandsOnY(AxisImage a) => a == AxisImage.PlusY || a == AxisImage.MinusY;

        public override string ToString() => $"X→{X}, Y→{Y}, σ={Sigma:+0;-0}";
    }
}
