using System;
using System.Collections.Generic;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build
{
    /// <summary>
    /// State of the whole job, shared by its own <see cref="BuildContext"/> and the child
    /// contexts in which the second half of a mirror feature is built: one plane resolver,
    /// every feature the job made, the age of the model's topology, what is known about every
    /// feature built so far.
    /// </summary>
    internal sealed class BuildShared
    {
        /// <summary>The one plane resolver of the job; it belongs to the job's own context.</summary>
        public PlaneResolver Planes { get; set; } = null!;

        /// <summary>Every feature and helper the job created, also in a second half: the job's bodies are those made of them.</summary>
        public HashSet<string> JobNames { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Counts the changes of the model. Each <see cref="TopologyFinder"/> caches edges and
        /// faces with the value it read them at: a helper plane made for a second-half step
        /// makes the cache of the job's own context stale too.
        /// </summary>
        public int TopologyGeneration { get; set; }

        /// <summary>Name in the target → what a second half built from it needs to know (<see cref="BuiltFeature"/>).</summary>
        public Dictionary<string, BuiltFeature> Built { get; } = new Dictionary<string, BuiltFeature>(StringComparer.Ordinal);

        /// <summary>Status bar of the run; null without one.</summary>
        public RunProgress? Progress { get; set; }

        /// <summary>
        /// Set when the job cannot go on (the rollback bar of the target stuck mid-tree): every
        /// loop stops after the step that set it, the report says why.
        /// </summary>
        public string? AbortReason { get; set; }

        /// <summary>The user stopped the run with Esc within a step (the second half of a mirror feature).</summary>
        public bool Cancelled { get; set; }
    }
}
