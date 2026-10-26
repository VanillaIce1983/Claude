using System.Collections.Generic;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// SOLIDWORKS equations read as text: "D1@Skizze1" = "Breite" / 2 + 5 'Kommentar.
    /// Names stand in double quotes: dimensions ("D1@Skizze1"), global variables ("Breite"),
    /// features ("Extrude1" = "suppressed") and the words of such a state. A ' outside the
    /// quotes starts a comment.
    /// </summary>
    public static class EquationText
    {
        /// <summary>Every quoted name, in order.</summary>
        public static List<string> Names(string equation)
        {
            var names = new List<string>();
            for (int i = 0; i < equation.Length; i++)
            {
                if (equation[i] == '\'') break;
                if (equation[i] != '"') continue;
                int end = equation.IndexOf('"', i + 1);
                if (end < 0) break;
                names.Add(equation.Substring(i + 1, end - i - 1));
                i = end;
            }
            return names;
        }

        /// <summary>
        /// The quoted name left of "=": the dimension, global variable or feature the equation
        /// sets. Null if the left side is not a single quoted name.
        /// </summary>
        public static string? Driven(string equation)
        {
            string s = equation.TrimStart();
            if (s.Length == 0 || s[0] != '"') return null;
            int end = s.IndexOf('"', 1);
            if (end < 0) return null;
            string rest = s.Substring(end + 1).TrimStart();
            return rest.Length > 0 && rest[0] == '=' ? s.Substring(1, end - 1) : null;
        }

        /// <summary>"D1@Skizze1" (also with "@Teil1.SLDPRT" after it) → ("D1", "Skizze1"); null for a global variable or a feature.</summary>
        public static (string name, string owner)? Dimension(string name)
        {
            string[] parts = name.Split('@');
            return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0 ? (parts[0], parts[1]) : ((string, string)?)null;
        }
    }
}
