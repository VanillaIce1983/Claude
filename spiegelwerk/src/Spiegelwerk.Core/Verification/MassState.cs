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

    public readonly struct MassComparison
    {
        public MassComparison(double volumeError, double centroidError)
        {
            VolumeError = volumeError;
            CentroidError = centroidError;
        }

        /// <summary>Relative volume difference of the feature's change.</summary>
        public double VolumeError { get; }

        /// <summary>Distance between the centroids of the feature's change, metres.</summary>
        public double CentroidError { get; }

        /// <summary>For ranking alternatives: smaller is better.</summary>
        public double Score => VolumeError + CentroidError * 1000;

        public bool Passes => VolumeError <= 1e-4 && CentroidError <= 1e-5;

        public static MassComparison Compare(MassState expected, MassState actual)
        {
            double scale = Math.Max(Math.Abs(expected.Volume), 1e-15);
            double volErr = Math.Abs(expected.Volume - actual.Volume) / scale;
            // Moment difference divided by volume is a length: how far apart the centroids of the change are.
            double cenErr = (expected.Moment - actual.Moment).Length / scale;
            if (Math.Abs(expected.Volume) < 1e-15 && Math.Abs(actual.Volume) < 1e-15) cenErr = 0;
            return new MassComparison(volErr, cenErr);
        }

        public override string ToString() =>
            Passes ? "Volumen und Schwerpunkt stimmen" : $"Abweichung: Volumen {VolumeError:P3}, Schwerpunkt {CentroidError * 1000:0.###} mm";
    }
}
