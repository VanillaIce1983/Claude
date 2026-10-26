using System;
using System.Collections.Generic;
using System.Linq;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Materials assigned to single bodies are not carried over: the mirrored bodies get the
    /// part's material (or none).
    /// </summary>
    public static class BodyMaterials
    {
        /// <summary>
        /// Report note naming the bodies with a material other than the part's, or null if there are none.
        /// A body without one of its own may report the part's material or nothing; neither counts.
        /// </summary>
        public static string? Note(string partMaterial, IEnumerable<(string body, string material)> bodies)
        {
            List<string> own = bodies
                .Where(b => !string.IsNullOrEmpty(b.material) && !string.Equals(b.material, partMaterial, StringComparison.OrdinalIgnoreCase))
                .Select(b => $"\"{b.body}\" ({b.material})")
                .ToList();
            if (own.Count == 0) return null;
            string more = own.Count > 5 ? $" und {own.Count - 5} weitere" : "";
            string target = string.IsNullOrEmpty(partMaterial) ? "keinen Werkstoff" : $"den Werkstoff des Teils ({partMaterial})";
            return $"Werkstoffe einzelner Körper werden nicht übernommen: {string.Join(", ", own.Take(5))}{more}. " +
                   $"Die gespiegelten Körper haben {target}; bitte bei Bedarf von Hand zuweisen.";
        }
    }
}
