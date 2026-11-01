using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Verification;

namespace Spiegelwerk.Core.Records
{
    /// <summary>What a mirror feature of the original repeats: features (and faces as whole features), or bodies.</summary>
    public enum MirrorSeedKind
    {
        /// <summary>MirrorPattern: „Features/Flächen spiegeln“.</summary>
        Features,

        /// <summary>MirrorSolid: „Körper spiegeln“.</summary>
        Bodies,
    }

    /// <summary>A seed body of „Körper spiegeln“, read in the state at the mirror feature.</summary>
    public sealed class MirrorSeedBody
    {
        public MirrorSeedBody(string name, MassState mass, IEnumerable<string> features)
        {
            Name = name;
            Mass = mass;
            Features = features.ToList();
        }

        /// <summary>The body's name at the mirror feature (for the report).</summary>
        public string Name { get; }

        /// <summary>Volume and first moment, unmirrored.</summary>
        public MassState Mass { get; }

        /// <summary>The features that shape the body (IBody2.GetFeatures without sketches, reference geometry, system and suppressed features).</summary>
        public List<string> Features { get; }
    }

    /// <summary>
    /// „Features/Flächen spiegeln“ (MirrorPattern) or „Körper spiegeln“ (MirrorSolid) of the
    /// original. Spiegelwerk does not use the mirror API, so the second half is rebuilt as
    /// ordinary features: R_Π(R_P(x)) = R_P′(R_Π(x)) with P′ = R_Π(P) – the copies of the
    /// seeds the run has already built (the first half) are read back from the target and
    /// built once more, mirrored at P′.
    /// </summary>
    public sealed class MirrorFeatureRecord : FeatureRecord
    {
        public MirrorFeatureRecord(string name, string typeName, MirrorSeedKind kind, Plane3 plane) : base(name, typeName)
        {
            Kind = kind;
            Plane = plane;
        }

        public MirrorSeedKind Kind { get; }

        /// <summary>The mirror plane P of the original, in its coordinates (unmirrored like every record); the sign of the normal does not matter.</summary>
        public Plane3 Plane { get; }

        /// <summary>P is a planar face of the part, not a plane (for the report).</summary>
        public bool PlaneIsFace { get; set; }

        /// <summary>
        /// Original names of the seeds: the features to mirror, the owners of the faces to
        /// mirror (only whole sets of faces of a feature are accepted), the features of the
        /// bodies to mirror. Only features that shape a body: no sketches, reference geometry,
        /// system or suppressed features.
        /// </summary>
        public List<string> SeedFeatures { get; } = new List<string>();

        /// <summary>Fingerprints of the faces to mirror, for information; they are rebuilt as their whole features.</summary>
        public List<FaceSignature> SeedFaces { get; } = new List<FaceSignature>();

        /// <summary>„Körper spiegeln“: the bodies to mirror, in the state at the mirror feature.</summary>
        public List<MirrorSeedBody> SeedBodies { get; } = new List<MirrorSeedBody>();

        /// <summary>„Körper spiegeln“: merge each mirrored body with its seed.</summary>
        public bool Merge { get; set; }

        public bool GeometryPattern { get; set; }
        public bool PropagateVisualProperty { get; set; }

        /// <summary>
        /// Seed bodies the user did not select: only a body made by the mirror feature was
        /// selected, so its seed body comes along as a copy of its own (named in the report).
        /// </summary>
        public List<string> UnrequestedSeedBodies { get; } = new List<string>();

        /// <summary>„Körper spiegeln“: <see cref="SeedFeatures"/> from the seed bodies, each feature once.</summary>
        public void SeedFeaturesFromBodies()
        {
            SeedFeatures.Clear();
            foreach (string f in SeedBodies.SelectMany(b => b.Features))
                if (!SeedFeatures.Contains(f)) SeedFeatures.Add(f);
        }

        /// <summary>The mirror plane of the second half in the target: P′ = R_Π(P).</summary>
        public Plane3 SecondHalfPlane(Reflection run) => run.Apply(Plane);

        public override string ToString() => $"{TypeName} \"{Name}\" ({SeedFeatures.Count} Saat-Features)";
    }

    /// <summary>
    /// What the second half needs to know about a feature the run has built: the name the
    /// report gives it and the reference its own step was checked against, in target
    /// coordinates – a second-half step is checked against the same numbers, mirrored once more.
    /// </summary>
    public sealed class BuiltFeature
    {
        public BuiltFeature(string logical)
        {
            Logical = logical ?? throw new ArgumentNullException(nameof(logical));
        }

        /// <summary>Name in the report: the original's name, „X (Spiegeln1)“ for a second half.</summary>
        public string Logical { get; }

        public bool ChangesSolid { get; set; }

        /// <summary>The expected change of its step, in target coordinates; null if not measured.</summary>
        public MassState? Expected { get; set; }

        /// <summary>The face samples of its step, in target coordinates.</summary>
        public List<Vec3> Samples { get; } = new List<Vec3>();

        /// <summary>Merge flag of the original (extrude, revolve, sweep, loft) – the copy may have been built without merging.</summary>
        public bool? Merge { get; set; }

        /// <summary>Status of its report entry.</summary>
        public ReportStatus Status { get; set; } = ReportStatus.Ok;
    }
}
