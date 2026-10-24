using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Spiegelwerk.Core.Records
{
    public enum ReportStatus
    {
        Ok,
        Warning,
        Failed,
        Skipped,
    }

    public sealed class ReportEntry
    {
        public ReportEntry(string feature, string type, ReportStatus status, string message)
        {
            Feature = feature;
            Type = type;
            Status = status;
            Message = message;
        }

        public string Feature { get; }
        public string Type { get; }
        public ReportStatus Status { get; set; }
        public string Message { get; set; }

        /// <summary>Result of the volume/centroid comparison, if one was made.</summary>
        public string Verification { get; set; } = "";

        /// <summary>
        /// Skipped, but the target already has an equivalent (e.g. a plane whose mirrored
        /// location is a standard plane). Such an original may be deleted; other skipped ones may not.
        /// </summary>
        public bool HasEquivalent { get; set; }

        public List<string> Details { get; } = new List<string>();

        /// <summary>The original has a counterpart in the target.</summary>
        public bool IsMirrored => Status == ReportStatus.Ok || Status == ReportStatus.Warning || (Status == ReportStatus.Skipped && HasEquivalent);
    }

    public sealed class MirrorReport
    {
        public List<ReportEntry> Entries { get; } = new List<ReportEntry>();

        /// <summary>Messages that concern the whole job (target document, plane, assembly placement).</summary>
        public List<string> General { get; } = new List<string>();

        public string TargetName { get; set; } = "";
        public bool Aborted { get; set; }

        public ReportEntry Add(FeatureRecord r, ReportStatus status, string message)
        {
            var e = new ReportEntry(r.Name, r.TypeName, status, message);
            e.Details.AddRange(r.Notes);
            Entries.Add(e);
            return e;
        }

        /// <summary>Merges the report of one component into an assembly-level report.</summary>
        public void Append(MirrorReport other, string heading)
        {
            General.Add($"— {heading}: {other.Summary}");
            General.AddRange(other.General.Select(g => "   " + g));
            foreach (ReportEntry e in other.Entries)
            {
                var copy = new ReportEntry($"{heading} › {e.Feature}", e.Type, e.Status, e.Message) { Verification = e.Verification, HasEquivalent = e.HasEquivalent };
                copy.Details.AddRange(e.Details);
                Entries.Add(copy);
            }
            Aborted |= other.Aborted;
            if (TargetName.Length == 0) TargetName = other.TargetName;
        }

        public int Count(ReportStatus s) => Entries.Count(e => e.Status == s);

        public bool HasProblems => Aborted || Entries.Any(e => e.Status == ReportStatus.Failed || e.Status == ReportStatus.Warning);

        /// <summary>
        /// Every feature has a counterpart and nothing needs checking: the condition for deleting
        /// the original. A skipped feature (3D sketch, axis, coordinate system ...) has no
        /// copy, so deleting its original would lose it.
        /// </summary>
        public bool AllMirrored => !Aborted && Entries.All(e => e.Status == ReportStatus.Ok || (e.Status == ReportStatus.Skipped && e.HasEquivalent));

        /// <summary>Entries without a counterpart in the target.</summary>
        public IEnumerable<ReportEntry> NotMirrored => Entries.Where(e => !e.IsMirrored);

        public string Summary =>
            $"{Count(ReportStatus.Ok)} in Ordnung, {Count(ReportStatus.Warning)} mit Hinweis, " +
            $"{Count(ReportStatus.Failed)} fehlgeschlagen, {Count(ReportStatus.Skipped)} übersprungen" +
            (Aborted ? " – abgebrochen" : "");

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Spiegelwerk – Ergebnis: {TargetName}");
            sb.AppendLine(Summary);
            foreach (string g in General) sb.AppendLine("  " + g);
            sb.AppendLine();
            foreach (ReportEntry e in Entries)
            {
                sb.AppendLine($"[{StatusText(e.Status)}] {e.Feature} ({e.Type}): {e.Message}");
                if (e.Verification.Length > 0) sb.AppendLine("    Prüfung: " + e.Verification);
                foreach (string d in e.Details) sb.AppendLine("    " + d);
            }
            return sb.ToString();
        }

        public static string StatusText(ReportStatus s) => s switch
        {
            ReportStatus.Ok => "OK",
            ReportStatus.Warning => "Hinweis",
            ReportStatus.Failed => "Fehler",
            _ => "Übersprungen",
        };
    }
}
