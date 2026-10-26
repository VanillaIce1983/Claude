namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// The run as the progress bar counts it: one step per feature in capture, in the
    /// measurement of the original (only with step checks) and in the build. Esc is looked at
    /// between two steps; after the last one there is nothing left to stop.
    /// </summary>
    public sealed class RunSteps
    {
        public const string Capture = "Erfassen";
        public const string Measure = "Vermessen";
        public const string Build = "Aufbauen";
        public const string Finish = "Abschließen";

        public RunSteps(int features, bool measure)
        {
            Total = features <= 0 ? 0 : features * (measure ? 3 : 2);
        }

        public int Total { get; }
        public int Position { get; private set; }

        /// <summary>One step done. True if another one follows, so stopping here still leaves work undone.</summary>
        public bool Advance()
        {
            if (Position < Total) Position++;
            return Position < Total;
        }

        /// <summary>"Spiegelwerk – Aufbauen: Austragung1"; without feature or phase only what is given.</summary>
        public static string Title(string phase, string feature)
        {
            if (phase.Length == 0) return "Spiegelwerk";
            return feature.Length == 0 ? $"Spiegelwerk – {phase}" : $"Spiegelwerk – {phase}: {feature}";
        }

        /// <summary>Report note for a run stopped with Esc in capture or measurement: nothing was built yet.</summary>
        public static string CancelledBeforeBuild(string phase) =>
            $"Mit Esc abgebrochen beim {phase} des Originals; es wurde nichts aufgebaut.";

        /// <summary>Report note for a run stopped with Esc after building <paramref name="feature"/>; <paramref name="left"/> features were not built.</summary>
        public static string CancelledInBuild(string feature, int left)
        {
            if (left <= 0) return $"Mit Esc abgebrochen nach „{feature}“.";
            string rest = left == 1 ? "1 weiteres Feature wurde" : $"{left} weitere Features wurden";
            return $"Mit Esc abgebrochen nach „{feature}“; {rest} nicht mehr aufgebaut.";
        }
    }
}
