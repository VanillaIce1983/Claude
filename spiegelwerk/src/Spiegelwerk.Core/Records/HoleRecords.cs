using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Core.Records
{
    /// <summary>
    /// Hole Wizard hole. The holes sit at the points of the position sketch, which is captured
    /// with the hole and rebuilt into the position sketch of the new hole. Sizes are kept
    /// exactly: a mirrored part needs the same bores – and the same right-hand threads; a
    /// cosmetic thread carries no handedness, so nothing has to be mirrored there.
    /// </summary>
    public sealed class HoleRecord : FeatureRecord
    {
        public HoleRecord(string name, string typeName, string positionSketchName, SketchModel position) : base(name, typeName)
        {
            PositionSketchName = positionSketchName;
            Position = position;
        }

        public string PositionSketchName { get; }

        /// <summary>The position sketch: one hole at each of its points.</summary>
        public SketchModel Position { get; }

        /// <summary>Hole centres in model coordinates (unmirrored), as the hole reports them.</summary>
        public List<Vec3> Points { get; } = new List<Vec3>();

        /// <summary>swWzdHoleTypes_e, the detailed type.</summary>
        public int HoleType { get; set; }

        /// <summary>swWzdGeneralHoleTypes_e, derived from <see cref="HoleType"/>.</summary>
        public int GenericType { get; set; }

        /// <summary>swWzdHoleStandards_e.</summary>
        public int Standard { get; set; }

        /// <summary>swWzdHoleStandardFastenerTypes_e.</summary>
        public int FastenerType { get; set; }

        public string Size { get; set; } = "";
        public EndCondition End { get; set; }
        public bool Reverse { get; set; }
        public string ThreadClass { get; set; } = "";

        /// <summary>Placement face (or plane).</summary>
        public GeometryRef? Face { get; set; }

        /// <summary>Up-to reference of the end condition, if it has one.</summary>
        public GeometryRef? EndReference { get; set; }

        /// <summary>
        /// Sizes and options by property name of IWizardHoleFeatureData2 (lengths in metres,
        /// angles in radians, options as numbers). Only values that differ from the standard
        /// sizes of the new hole are written, so a standard hole stays a standard hole.
        /// </summary>
        public Dictionary<string, double> Values { get; } = new Dictionary<string, double>();
    }

    /// <summary>
    /// A sub-feature that is not rebuilt on its own but together with its owner – the sketches
    /// of a Hole Wizard hole. Only valid while the owner is rebuilt too; otherwise the job
    /// captures the sketch on its own again.
    /// </summary>
    public sealed class PartOfFeatureRecord : FeatureRecord
    {
        public PartOfFeatureRecord(string name, string typeName, string owner) : base(name, typeName)
        {
            Owner = owner;
        }

        public string Owner { get; }
        public override bool ChangesSolid => false;
    }

    /// <summary>Hole Wizard type numbers.</summary>
    public static class HoleTypes
    {
        public const int CounterBore = 0, CounterSink = 1, Hole = 2, PipeTap = 3, Tap = 4, Legacy = 5;
        public const int CounterBoreSlot = 6, CounterSinkSlot = 7, HoleSlot = 8;

        /// <summary>
        /// swWzdHoleTypes_e (what a hole reports) → swWzdGeneralHoleTypes_e (what InitializeHole
        /// and HoleWizard5 take). The API has no getter for the general type. Null for numbers
        /// this table does not know (newer versions).
        /// </summary>
        public static int? GenericOf(int holeType)
        {
            if (holeType >= 0 && holeType <= 9) return Legacy;
            if (holeType >= 10 && holeType <= 21) return CounterBore;
            if (holeType == 22 || holeType == 23 || (holeType >= 25 && holeType <= 28)) return Hole;
            if (holeType == 24 || holeType == 29 || holeType == 30 || (holeType >= 43 && holeType <= 45)) return CounterSink;
            if ((holeType >= 31 && holeType <= 36) || (holeType >= 46 && holeType <= 56)) return Tap;
            if (holeType >= 37 && holeType <= 42) return PipeTap;
            if (holeType >= 57 && holeType <= 68) return CounterBoreSlot;
            if (holeType == 69 || holeType == 70 || (holeType >= 72 && holeType <= 75)) return HoleSlot;
            if (holeType == 71 || (holeType >= 76 && holeType <= 79) || holeType == 90) return CounterSinkSlot;
            return null;
        }

        public static bool IsSlot(int generic) => generic == CounterBoreSlot || generic == CounterSinkSlot || generic == HoleSlot;
    }
}
