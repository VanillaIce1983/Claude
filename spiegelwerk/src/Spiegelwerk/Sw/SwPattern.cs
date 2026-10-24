using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Sw
{
    /// <summary>Reads the instance placements of a pattern (GetTransform), on the original and on the rebuilt feature alike.</summary>
    internal static class SwPattern
    {
        public static List<Affine> Instances(SwMath math, int count, Func<int, MathTransform> transform)
        {
            var result = new List<Affine>();
            for (int i = 0; i < count; i++)
            {
                try
                {
                    if (transform(i) is MathTransform t) result.Add(math.ToAffine(t));
                }
                catch (COMException)
                {
                    // Index without an instance (seed only in direction 2, skipped ...): nothing to compare.
                }
                catch (ArgumentException)
                {
                }
            }
            return result;
        }
    }
}
