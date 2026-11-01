using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
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

        /// <summary>Keeps the target's shared sheet-metal values as they were (steps with TouchesSheetMetalDefaults); null without sheet metal.</summary>
        public SheetMetalGuard? Guard { get; set; }

        /// <summary>The folded state of the original for the run; null without sheet metal.</summary>
        public SheetMetalSession? Session { get; set; }

        /// <summary>The run builds at most one base flange with a body of its own: the copy stays single-body.</summary>
        public bool SingleSheetMetalBody { get; set; }

        /// <summary>Own values of the first body, waiting for the second sheet-metal body (B15).</summary>
        public List<PendingBodyOverrides> PendingBodyOverrides { get; } = new List<PendingBodyOverrides>();
    }

    /// <summary>
    /// The override groups of the copy's first sheet-metal body: SetOverrideDefaultParameter2 does
    /// not apply while the copy is single-body, so they are set once a second body exists.
    /// </summary>
    internal sealed class PendingBodyOverrides
    {
        public PendingBodyOverrides(string flange, Feature sheetMetal, SheetMetalParameters body, IReadOnlyList<int> groups, SheetMetalParameters ownValues, bool gauge)
        {
            Flange = flange;
            SheetMetal = sheetMetal;
            Body = body;
            Groups = groups;
            OwnValues = ownValues;
            Gauge = gauge;
        }

        /// <summary>Original name of the base flange.</summary>
        public string Flange { get; }

        /// <summary>Its SheetMetal in the target.</summary>
        public Feature SheetMetal { get; }

        /// <summary>The original body's values.</summary>
        public SheetMetalParameters Body { get; }

        public IReadOnlyList<int> Groups { get; }

        /// <summary>What the body has in all (<see cref="Core.Planning.SheetMetalRules.OwnValues"/>): the part's values if no second body comes.</summary>
        public SheetMetalParameters OwnValues { get; }

        /// <summary>The first body set the gauge table: its values are not written as fixed ones.</summary>
        public bool Gauge { get; }
    }
}
