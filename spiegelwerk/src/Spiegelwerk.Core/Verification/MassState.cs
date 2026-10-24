using System;
using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Verification
{
    /// <summary>
    /// Volume V and first moment M = Σ Vᵢ·cᵢ of all solid bodies. Both are additive, so the
    /// change a single feature causes is simply after − before, and the mirrored change can be
    /// computed without knowing the bodies:  R applied to a centroid c is c − 2((c − p₀)·n)n,
    /// hence  M' = M − 2((M − V·p₀)·n)n.
    /// </summary>
    public readonly struct MassState
    {
        public readonly double Volume;
        public readonly Vec3 Moment;

        public MassState(double volume, Vec3 moment)
        {
            Volume = volume;
            Moment = moment;
        }

        public static readonly MassState Empty = new MassState(0, Vec3.Zero);

        public static MassState FromBodies(IEnumerable<(double volume, Vec3 centroid)> bodies)
        {
            double v = 0;
            Vec3 m = Vec3.Zero;
            foreach (var (volume, centroid) in bodies)
            {
                v += volume;
                m += centroid * volume;
            }
            return new MassState(v, m);
        }

        public static MassState operator -(MassState a, MassState b) => new MassState(a.Volume - b.Volume, a.Moment - b.Moment);
        public static MassState operator +(MassState a, MassState b) => new MassState(a.Volume + b.Volume, a.Moment + b.Moment);

        public MassState Mirror(Reflection r)
        {
            Plane3 p = r.Plane;
            double s = (Moment - p.Origin * Volume).Dot(p.Normal);
            return new MassState(Volume, Moment - p.Normal * (2 * s));
        }

        public override string ToString() => $"V={Volume:0.###e+0} M={Moment}";
    }

    /// <summary>
    /// Compares the change a rebuilt feature made with the mirrored change of the original.
    ///
    /// Volume and moment are compared separately, each against its own tolerance: relative to
    /// the size of the change (1e-4 of the volume, 10 µm for the centroid), but never below a
    /// noise floor derived from the whole part, because the change is a difference of two
    /// totals that the kernel computes with finite accuracy. Comparing the moment on its own
    /// also catches changes whose net volume is zero – a fillet that adds material at a
    /// concave edge and removes as much at a convex one – where a centroid is meaningless.
    /// </summary>
    public readonly struct MassComparison
    {
        /// <summary>Assumed relative accuracy of the kernel's mass properties.</summary>
        public const double NoiseRelative = 1e-7;

        /// <summary>Characteristic length for the moment noise of parts lying around the origin, metres.</summary>
        private const double NoiseLength = 0.01;

        public MassComparison(double volumeRatio, double momentRatio, double volumeError, double centroidError)
        {
            VolumeRatio = volumeRatio;
            MomentRatio = momentRatio;
            VolumeError = volumeError;
            CentroidError = centroidError;
        }

        /// <summary>Volume difference divided by its tolerance (≤ 1 passes).</summary>
        public double VolumeRatio { get; }

        /// <summary>Moment difference divided by its tolerance (≤ 1 passes).</summary>
        public double MomentRatio { get; }

        /// <summary>Relative volume difference of the change, for the report.</summary>
        public double VolumeError { get; }

        /// <summary>Moment difference expressed as a centroid distance in metres, for the report.</summary>
        public double CentroidError { get; }

        /// <summary>For ranking alternatives: smaller is better.</summary>
        public double Score => VolumeRatio + MomentRatio;

        public bool Passes => VolumeRatio <= 1 && MomentRatio <= 1;

        /// <param name="total">State of the whole part after the feature; sets the noise floor.</param>
        public static MassComparison Compare(MassState expected, MassState actual, MassState total = default)
        {
            double v = Math.Abs(expected.Volume);
            double volTol = Math.Max(Math.Max(1e-4 * v, NoiseRelative * Math.Abs(total.Volume)), 1e-15);
            double momTol = Math.Max(Math.Max(1e-5 * v, NoiseRelative * (total.Moment.Length + Math.Abs(total.Volume) * NoiseLength)), 1e-20);

            double dv = Math.Abs(expected.Volume - actual.Volume);
            double dm = (expected.Moment - actual.Moment).Length;
            double scale = Math.Max(v, volTol);
            return new MassComparison(dv / volTol, dm / momTol, dv / scale, dm / scale);
        }

        public override string ToString() =>
            Passes ? "Volumen und Schwerpunkt stimmen" : $"Abweichung: Volumen {VolumeError:P3}, Schwerpunkt {CentroidError * 1000:0.###} mm";
    }
}
