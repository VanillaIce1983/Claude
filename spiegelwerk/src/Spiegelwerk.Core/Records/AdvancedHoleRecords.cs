using System.Collections.Generic;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Core.Records
{
    /// <summary>
    /// Advanced Hole: a stack of hole elements from the near-side face into the material, and
    /// optionally a second stack from a far-side face back towards it – counterbores,
    /// countersinks, straight and tapped sections in any order. Like a Hole Wizard hole, the
    /// holes sit at the points of the position sketch on the near-side face. Sizes and threads
    /// are kept exactly, a reflection changes neither.
    /// </summary>
    public sealed class AdvancedHoleRecord : PositionedHoleRecord
    {
        public AdvancedHoleRecord(string name, string typeName, string positionSketchName, SketchModel position)
            : base(name, typeName, positionSketchName, position)
        {
        }

        /// <summary>Elements from the near-side face (<see cref="PositionedHoleRecord.Face"/>) inwards, in order.</summary>
        public List<AdvancedHoleElement> Near { get; } = new List<AdvancedHoleElement>();

        /// <summary>Elements from the far-side face inwards, in order; empty if the hole has no far side.</summary>
        public List<AdvancedHoleElement> Far { get; } = new List<AdvancedHoleElement>();

        /// <summary>
        /// Far-side face. The feature data does not report it: capture takes the first face the
        /// hole axis leaves the body through (planar faces only).
        /// </summary>
        public GeometryRef? FarFace { get; set; }

        public bool UseBaselineDimensions { get; set; }
        public bool CustomizeCallout { get; set; }
    }

    /// <summary>One element of an Advanced Hole (IAdvancedHoleElementData and its type-specific interface).</summary>
    public sealed class AdvancedHoleElement
    {
        public AdvancedHoleElement(int elementType, bool farSide)
        {
            ElementType = elementType;
            FarSide = farSide;
        }

        /// <summary>swAdvWzdGeneralHoleTypes_e.</summary>
        public int ElementType { get; }

        /// <summary>Element of the far-side stack (swHoleElementOrientation_Farside).</summary>
        public bool FarSide { get; }

        /// <summary>swWzdHoleStandards_e.</summary>
        public int Standard { get; set; }

        /// <summary>swWzdHoleStandardFastenerTypes_e.</summary>
        public int FastenerType { get; set; }

        public string Size { get; set; } = "";
        public EndCondition End { get; set; }

        /// <summary>Up-to/offset-from reference of the end condition, if it has one.</summary>
        public GeometryRef? EndReference { get; set; }

        /// <summary>Callout text; only carried over if the hole has a customized callout.</summary>
        public string Callout { get; set; } = "";

        /// <summary>
        /// Numbers and switches by property name of the element interfaces (lengths in metres,
        /// angles in radians, switches 0/1, options as numbers).
        /// </summary>
        public Dictionary<string, double> Values { get; } = new Dictionary<string, double>();

        /// <summary>Texts by property name (fits of a straight element).</summary>
        public Dictionary<string, string> Texts { get; } = new Dictionary<string, string>();

        public override string ToString() => $"{AdvancedHoleTypes.Text(ElementType)} {Size}".TrimEnd();
    }

    /// <summary>swAdvWzdGeneralHoleTypes_e.</summary>
    public static class AdvancedHoleTypes
    {
        public const int CounterBore = 0, CounterSink = 1, Straight = 2, StraightTap = 3, TaperTap = 4;

        public static bool IsKnown(int type) => type >= CounterBore && type <= TaperTap;

        public static string Text(int type) => type switch
        {
            CounterBore => "Senkung zylindrisch",
            CounterSink => "Senkung kegelig",
            Straight => "Bohrung",
            StraightTap => "Gewinde",
            TaperTap => "Kegelgewinde",
            _ => $"Element {type}",
        };
    }
}
