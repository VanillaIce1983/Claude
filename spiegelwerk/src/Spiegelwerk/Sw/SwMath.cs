using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Conversions between SOLIDWORKS math objects and the Core geometry types. Frames are
    /// derived by transforming points and unit vectors instead of reading ArrayData by index,
    /// so nothing depends on the (row/column) layout of IMathTransform.
    /// </summary>
    internal sealed class SwMath
    {
        private readonly MathUtility _mu;

        public SwMath(SldWorks sw)
        {
            _mu = (MathUtility)sw.GetMathUtility();
        }

        public Vec3 TransformPoint(Vec3 p, MathTransform t)
        {
            var mp = (MathPoint)_mu.CreatePoint(p.ToArray());
            mp = (MathPoint)mp.MultiplyTransform(t);
            return Vec3.FromArray((double[])mp.ArrayData);
        }

        public Vec3 TransformVector(Vec3 v, MathTransform t)
        {
            var mv = (MathVector)_mu.CreateVector(v.ToArray());
            mv = (MathVector)mv.MultiplyTransform(t);
            return Vec3.FromArray((double[])mv.ArrayData);
        }

        /// <summary>Frame whose local coordinates the transform maps to world coordinates.</summary>
        public Frame3 FrameOf(MathTransform localToWorld)
        {
            Vec3 o = TransformPoint(Vec3.Zero, localToWorld);
            Vec3 x = TransformVector(Vec3.UnitX, localToWorld);
            Vec3 y = TransformVector(Vec3.UnitY, localToWorld);
            Vec3 z = TransformVector(Vec3.UnitZ, localToWorld);
            return Frame3.FromAxes(o, x, y, z);
        }

        /// <summary>Sketch → model frame of a 2D sketch.</summary>
        public Frame3 SketchFrame(Sketch sketch)
        {
            MathTransform modelToSketch = sketch.ModelToSketchTransform;
            return FrameOf((MathTransform)modelToSketch.Inverse());
        }

        /// <summary>Plane of a reference plane; its transform's Z axis is the plane normal.</summary>
        public Plane3 PlaneOf(RefPlane plane)
        {
            Frame3 f = FrameOf(plane.Transform);
            return new Plane3(f.Origin, f.ZAxis);
        }

        public Affine ToAffine(MathTransform t)
        {
            Vec3 o = TransformPoint(Vec3.Zero, t);
            return new Affine(TransformVector(Vec3.UnitX, t), TransformVector(Vec3.UnitY, t), TransformVector(Vec3.UnitZ, t), o);
        }

        public MathTransform ToMathTransform(Affine a)
        {
            var x = (MathVector)_mu.CreateVector(a.Col0.ToArray());
            var y = (MathVector)_mu.CreateVector(a.Col1.ToArray());
            var z = (MathVector)_mu.CreateVector(a.Col2.ToArray());
            var t = (MathVector)_mu.CreateVector(a.Translation.ToArray());
            return _mu.ComposeTransform(x, y, z, t, 1.0);
        }
    }
}
