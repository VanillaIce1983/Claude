namespace Spiegelwerk.Core.Records
{
    public enum MirrorTarget
    {
        /// <summary>Opposite-hand part in a new document (also used for assembly components).</summary>
        NewPart,

        /// <summary>The mirrored features are added to the same part.</summary>
        SamePart,
    }

    public enum ExternalReferenceHandling
    {
        /// <summary>
        /// A sketch reference missing in the target (origin off the mirror plane, edge of a
        /// feature that was not mirrored) is replaced by fixed construction geometry at the
        /// mirrored location; the relation or dimension attaches to that stand-in, so the
        /// sketch stays as well defined as the original.
        /// </summary>
        Fix,

        /// <summary>Such relations and dimensions are left out; the sketch may become under-defined.</summary>
        Drop,
    }

    /// <summary>What the user chose on the PropertyManager page.</summary>
    public sealed class MirrorOptions
    {
        public MirrorTarget Target { get; set; } = MirrorTarget.NewPart;

        /// <summary>Same part only: delete the original features after a fully successful run.</summary>
        public bool DeleteOriginal { get; set; }

        /// <summary>Compare volume and centroid of every rebuilt feature with the mirrored original.</summary>
        public bool VerifySteps { get; set; } = true;

        public ExternalReferenceHandling ExternalReferences { get; set; } = ExternalReferenceHandling.Fix;

        /// <summary>New part: copy global variables and equations (dimension names are kept for that).</summary>
        public bool CopyEquations { get; set; } = true;

        /// <summary>New part: copy the material.</summary>
        public bool CopyMaterial { get; set; } = true;

        /// <summary>Appended to feature names in the same part, to file names for a new part.</summary>
        public string NameSuffix { get; set; } = "_gespiegelt";

        /// <summary>Assembly: insert the mirrored part as a component next to the original.</summary>
        public bool InsertIntoAssembly { get; set; } = true;

        /// <summary>Stop at the first failed feature instead of carrying on with the rest.</summary>
        public bool StopOnError { get; set; }
    }
}
