using System.Collections.Generic;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// One way of handing a selected profile (contours and sketch regions) to SOLIDWORKS when
    /// an extrude or revolve is created. The API help shows two: pre-selection with mark 4 and
    /// contour selection switched on ("Create Extrude Feature Using Sketch Contours", "Enable
    /// Contour Selection", also with the sketch selected), and assigning Contours in
    /// ModifyDefinition; mark 0 is what earlier versions of Spiegelwerk used.
    /// </summary>
    public sealed class ProfileWay
    {
        public ProfileWay(string description, int profileMark, bool withSketch, bool contourSelection, bool wholeSketch, int axisMark)
        {
            Description = description;
            ProfileMark = profileMark;
            WithSketch = withSketch;
            ContourSelection = contourSelection;
            WholeSketch = wholeSketch;
            AxisMark = axisMark;
        }

        public string Description { get; }

        /// <summary>Mark of the pre-selected contours and regions (of the sketch for the whole sketch).</summary>
        public int ProfileMark { get; }

        /// <summary>The sketch feature is selected too, with <see cref="ProfileMark"/>.</summary>
        public bool WithSketch { get; }

        /// <summary>ISelectionMgr.EnableContourSelection is on while selecting and creating.</summary>
        public bool ContourSelection { get; }

        /// <summary>The feature is created from the whole sketch; the profile is assigned afterwards (Contours in ModifyDefinition).</summary>
        public bool WholeSketch { get; }

        /// <summary>Mark of the revolve axis; −1 for extrudes.</summary>
        public int AxisMark { get; }

        public override string ToString() => Description;
    }

    /// <summary>
    /// What happens when the profile of an extrude or revolve (selected contours and sketch
    /// regions) cannot be carried over exactly: unreadable in the original, not found in the
    /// copy, a containing larger region, SOLIDWORKS not taking the selection. The fallback is
    /// the whole sketch or the containing region – more material than the original added or
    /// removed. In a new part and with a body of its own that only makes the copy differ (a
    /// warning); in the same part with only features selected, the copy acts on the user's
    /// bodies, and a larger cut or boss would change them. There the feature is not built.
    /// </summary>
    public static class ProfileRules
    {
        /// <summary>Mark of contours and regions in the documented examples.</summary>
        public const int RegionMark = 4;

        /// <summary>True if an inexact profile must not be built: same part, no body of its own.</summary>
        public static bool RefusesInexact(bool samePart, bool separateBody) => samePart && !separateBody;

        /// <summary>Failure message of a refused feature.</summary>
        public const string Refusal =
            "Das Profil (ausgewählte Konturen oder Skizzenbereiche) lässt sich nicht genau übertragen. Im selben Teil wirken gewählte Features " +
            "auf die vorhandenen Körper, ein größeres Profil würde sie verändern; das Feature wurde nicht angelegt.";

        /// <summary>Note when the whole sketch is built instead: why, and the consequence.</summary>
        public static string WholeSketch(string why) => why + "; es wird die ganze Skizze verwendet.";

        /// <summary>Capture note: the original's selection could not be read; the builder decides what follows.</summary>
        public static string Unreadable(string why) => $"Ausgewählte Konturen und Skizzenbereiche nicht lesbar: {why}.";

        /// <summary>Why the whole sketch stands in for a selection that capture could not read.</summary>
        public const string LostInCapture = "Die Auswahl des Originals ließ sich nicht lesen";

        /// <summary>SOLIDWORKS took none of the ways to hand over the profile (each was read back and checked).</summary>
        public const string NotTaken = "SOLIDWORKS hat die Auswahl der Konturen und Skizzenbereiche auf keinem Weg übernommen";

        /// <summary>
        /// The ways to try, in order; every created feature is read back, and one that did not
        /// take the profile is deleted before the next way. Extrude: mark 4 with contour
        /// selection, the same with the sketch, mark 0; then the whole sketch with Contours set
        /// in ModifyDefinition. Revolve: the same with the axis at 16 – never mark 4 for profile
        /// and axis together –, mark 0 and the whole sketch also with the axis at 4. A feature
        /// without a selected profile uses only the whole-sketch ways, in their order.
        /// </summary>
        public static IReadOnlyList<ProfileWay> Ways(bool revolve)
        {
            int axis = revolve ? 16 : -1;
            var ways = new List<ProfileWay>
            {
                new ProfileWay("Markierung 4 mit Konturauswahl", RegionMark, false, true, false, axis),
                new ProfileWay("Skizze und Auswahl mit Markierung 4 und Konturauswahl", RegionMark, true, true, false, axis),
                new ProfileWay("Markierung 0", 0, false, false, false, axis),
            };
            if (revolve) ways.Add(new ProfileWay("Markierung 0, Achse mit 4", 0, false, false, false, 4));
            ways.Add(new ProfileWay("ganze Skizze, Auswahl per ModifyDefinition", 0, false, false, true, axis));
            if (revolve) ways.Add(new ProfileWay("ganze Skizze, Achse mit 4, Auswahl per ModifyDefinition", 0, false, false, true, 4));
            return ways;
        }
    }
}
