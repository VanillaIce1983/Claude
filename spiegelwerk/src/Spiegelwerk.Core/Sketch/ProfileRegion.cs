using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Sketch
{
    /// <summary>
    /// The shape of one sketch region: its sampled boundary (outer loop and holes as one
    /// unordered segment set), witnesses well inside it, and its area. Records keep it in
    /// source sketch coordinates (unmirrored, Z = 0, like SkPoint); the builder maps it into
    /// the target sketch with <see cref="SketchMirror.MapPoint"/>.
    /// </summary>
    public sealed class ProfileRegion
    {
        public ProfileRegion(IEnumerable<RegionSegment> segments, IEnumerable<RegionWitness> witnesses, double chordTolerance, double? kernelArea)
        {
            Segments = segments.ToList();
            Witnesses = witnesses.ToList();
            ChordTolerance = chordTolerance;
            KernelArea = kernelArea;
            PolylineArea = RegionGeometry.Area(Segments);
            Perimeter = RegionGeometry.Perimeter(Segments);
        }

        /// <summary>
        /// A region from welded segments, with up to <paramref name="witnesses"/> interior
        /// points (none if it is too narrow – check <see cref="Witnesses"/>).
        /// </summary>
        public static ProfileRegion Create(IReadOnlyList<RegionSegment> segments, double chordTolerance, double? kernelArea,
            int witnesses = RegionGeometry.DefaultWitnesses) =>
            new ProfileRegion(segments, RegionGeometry.FindInteriorPoints(segments, chordTolerance, witnesses), chordTolerance, kernelArea);

        public IReadOnlyList<RegionSegment> Segments { get; }

        /// <summary>Points inside the region, the one with the largest clearance first.</summary>
        public IReadOnlyList<RegionWitness> Witnesses { get; }

        /// <summary>ε the boundary was sampled with.</summary>
        public double ChordTolerance { get; }

        public double MinClearance => RegionGeometry.MinClearance(ChordTolerance);

        /// <summary>Area of the region's face as the kernel gives it (IFace2.GetArea), if it was readable.</summary>
        public double? KernelArea { get; }

        /// <summary>Area of the sampled polyline (band integration).</summary>
        public double PolylineArea { get; }

        /// <summary>The kernel's area if there is one, otherwise the polyline's.</summary>
        public double Area => KernelArea ?? PolylineArea;

        public double Perimeter { get; }

        /// <summary>Largest witness clearance; 0 without witnesses.</summary>
        public double Clearance => Witnesses.Count > 0 ? Witnesses.Max(w => w.Clearance) : 0;

        public IEnumerable<Vec3> Vertices => RegionGeometry.Vertices(Segments);

        /// <summary>
        /// The same region under a map of the plane – an isometry such as the mirror into the
        /// target sketch: area and clearances stay.
        /// </summary>
        public ProfileRegion Map(Func<Vec3, Vec3> f) =>
            new ProfileRegion(Segments.Select(s => s.Map(f)), Witnesses.Select(w => w.Map(f)), ChordTolerance, KernelArea);

        public RegionPoint Classify(Vec3 p) => RegionGeometry.Classify(Segments, p, RegionGeometry.Margin(ChordTolerance));

        public override string ToString() =>
            $"Bereich {Area * 1e6:0.###} mm², {Segments.Count} Segmente, {Witnesses.Count} Innenpunkte";
    }
}
