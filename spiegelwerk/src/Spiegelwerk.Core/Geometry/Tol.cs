namespace Spiegelwerk.Core.Geometry
{
    /// <summary>
    /// Tolerances. SOLIDWORKS models are accurate to about 1e-8 m; mirrored geometry is
    /// recomputed by the kernel, so comparisons allow a little more than that.
    /// </summary>
    public static class Tol
    {
        /// <summary>Point coincidence, metres (1 µm).</summary>
        public const double Length = 1e-6;

        /// <summary>Direction comparisons on unit vectors (1 - |cos|).</summary>
        public const double Angle = 1e-7;

        /// <summary>Relative tolerance for lengths, areas and volumes.</summary>
        public const double Relative = 1e-5;
    }
}
