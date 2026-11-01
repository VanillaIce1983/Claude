using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Sketch
{
    /// <summary>Coordinates in which SOLIDWORKS returns the edges and vertices of a sketch region.</summary>
    public enum RawSpace
    {
        /// <summary>Model coordinates; sketch coordinates follow from the sketch frame.</summary>
        Model,

        /// <summary>Sketch coordinates with Z = 0.</summary>
        Sketch,
    }

    public enum RegionSpaceOutcome
    {
        Model,
        Sketch,

        /// <summary>Both readings fit and nothing tells them apart: not used, the fallback policy applies.</summary>
        Undecided,

        /// <summary>Neither reading puts the points into the sketch plane.</summary>
        Error,
    }

    /// <summary>What decided the coordinate space.</summary>
    public enum RegionSpaceBasis
    {
        None,

        /// <summary>Only one reading puts every point into the sketch plane: certain.</summary>
        Plane,

        /// <summary>Learnt from an earlier, certain reading in this run.</summary>
        Known,

        /// <summary>Both readings give the same sketch coordinates; either is right.</summary>
        Invariant,

        /// <summary>Loop vertices coincide with sketch points under one reading only.</summary>
        Vote,
    }

    public readonly struct RegionSpaceDecision
    {
        public RegionSpaceDecision(RegionSpaceOutcome outcome, RegionSpaceBasis basis)
        {
            Outcome = outcome;
            Basis = basis;
        }

        public RegionSpaceOutcome Outcome { get; }
        public RegionSpaceBasis Basis { get; }

        public RawSpace? Space => Outcome == RegionSpaceOutcome.Model ? RawSpace.Model : Outcome == RegionSpaceOutcome.Sketch ? RawSpace.Sketch : (RawSpace?)null;

        public override string ToString() => $"{Outcome} ({Basis})";
    }

    /// <summary>
    /// The API documents no coordinate space for the edges of a sketch region. A CodeStack
    /// macro suggests model space, the help of ISketch.GetContourEdges sketch space – and with
    /// a sketch plane z = 0 both readings fit (a sketch on the back face of a part extruded
    /// from the front plane: z = 0, normal −Z, in-plane axes flipped). A wrong guess would map
    /// the witnesses through mismatched frames and typically select the mirror-symmetric
    /// region of equal area – unnoticed. So the space is decided, never defaulted:
    ///  1. exactly one reading puts every point into the sketch plane (10 µm): that one, and
    ///     it is remembered for the run;
    ///  2. both fit: what the run already knows;
    ///  3. both readings give the same sketch coordinates: either;
    ///  4. a vote: loop vertices and the centres of vertex-free circle loops coinciding with
    ///     sketch points under one reading only;
    ///  5. otherwise undecided – the region is not used.
    /// </summary>
    public static class RegionSpace
    {
        /// <summary>Distance from the sketch plane a raw point may have (10 µm).</summary>
        public const double PlaneTolerance = 1e-5;

        /// <summary>
        /// The space confirmed by a test in SOLIDWORKS, if any; it then holds from the start of
        /// every run. Not set yet: see STOLPERSTEINE ("Was beim ersten Test … zu prüfen ist").
        /// </summary>
        public static readonly RawSpace? Confirmed = null;

        public static RegionSpaceDecision Decide(IReadOnlyCollection<Vec3> raw, Frame3 frame, IReadOnlyCollection<Vec3> vertices,
            IReadOnlyCollection<Vec3> sketchPoints, RawSpace? known, double chordTolerance)
        {
            if (raw.Count == 0) return new RegionSpaceDecision(RegionSpaceOutcome.Error, RegionSpaceBasis.None);
            bool model = raw.All(p => Math.Abs(frame.ToLocal(p).Z) <= PlaneTolerance);
            bool sketch = raw.All(p => Math.Abs(p.Z) <= PlaneTolerance);
            if (model && !sketch) return new RegionSpaceDecision(RegionSpaceOutcome.Model, RegionSpaceBasis.Plane);
            if (sketch && !model) return new RegionSpaceDecision(RegionSpaceOutcome.Sketch, RegionSpaceBasis.Plane);
            if (!model) return new RegionSpaceDecision(RegionSpaceOutcome.Error, RegionSpaceBasis.None);

            if (known.HasValue) return new RegionSpaceDecision(Of(known.Value), RegionSpaceBasis.Known);
            if (raw.All(p => ToSketch(p, RawSpace.Model, frame).DistanceTo(ToSketch(p, RawSpace.Sketch, frame)) <= 2 * chordTolerance))
                return new RegionSpaceDecision(RegionSpaceOutcome.Model, RegionSpaceBasis.Invariant);

            int asModel = Coinciding(vertices, RawSpace.Model, frame, sketchPoints);
            int asSketch = Coinciding(vertices, RawSpace.Sketch, frame, sketchPoints);
            if (asModel > asSketch) return new RegionSpaceDecision(RegionSpaceOutcome.Model, RegionSpaceBasis.Vote);
            if (asSketch > asModel) return new RegionSpaceDecision(RegionSpaceOutcome.Sketch, RegionSpaceBasis.Vote);
            return new RegionSpaceDecision(RegionSpaceOutcome.Undecided, RegionSpaceBasis.None);
        }

        /// <summary>A raw point in sketch coordinates (Z = 0) under the given reading.</summary>
        public static Vec3 ToSketch(Vec3 raw, RawSpace space, Frame3 frame)
        {
            if (space == RawSpace.Sketch) return new Vec3(raw.X, raw.Y, 0);
            Vec3 local = frame.ToLocal(raw);
            return new Vec3(local.X, local.Y, 0);
        }

        private static RegionSpaceOutcome Of(RawSpace s) => s == RawSpace.Model ? RegionSpaceOutcome.Model : RegionSpaceOutcome.Sketch;

        private static int Coinciding(IEnumerable<Vec3> vertices, RawSpace space, Frame3 frame, IReadOnlyCollection<Vec3> sketchPoints) =>
            vertices.Count(v =>
            {
                Vec3 s = ToSketch(v, space, frame);
                return sketchPoints.Any(q => new Vec3(q.X - s.X, q.Y - s.Y, 0).Length <= Tol.Length);
            });
    }

    /// <summary>
    /// What a run has learnt about the coordinate space of region geometry: the first
    /// certain reading (one reading only puts the points into the sketch plane), on the
    /// source or the target side. Starts with <see cref="RegionSpace.Confirmed"/>.
    /// </summary>
    public sealed class RegionSpaceMemory
    {
        public RawSpace? Known { get; private set; } = RegionSpace.Confirmed;

        /// <summary>Known from a measurement in this run, not from <see cref="RegionSpace.Confirmed"/>.</summary>
        public bool Measured { get; private set; }

        public void Learn(RegionSpaceDecision decision)
        {
            if (decision.Basis != RegionSpaceBasis.Plane || !decision.Space.HasValue || Known.HasValue) return;
            Known = decision.Space;
            Measured = true;
        }

        /// <summary>
        /// Report line on what the run measured – until the space is confirmed, the first test in
        /// SOLIDWORKS needs to see it. Null if nothing was measured.
        /// </summary>
        public string? ReportLine() =>
            Measured && Known.HasValue
                ? $"Skizzenbereiche: SOLIDWORKS liefert ihre Kanten in {(Known.Value == RawSpace.Model ? "Modellkoordinaten" : "Skizzenkoordinaten")} (gemessen)."
                : null;
    }
}
