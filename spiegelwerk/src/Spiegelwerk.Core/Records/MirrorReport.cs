using System;
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

    /// <summary>What the report knows of a step of a mirror feature's second half.</summary>
    public sealed class SecondHalfStep
    {
        public SecondHalfStep(string logical, string? target, ReportStatus firstHalf)
        {
            Logical = logical;
            Target = target;
            FirstHalf = firstHalf;
        }

        /// <summary>The name the report gives the feature it copies: the original's („Boss-Extrude1“), for nested mirrors „X (Spiegeln1)“.</summary>
        public string Logical { get; }

        /// <summary>Name of the new feature in the target, if one was made.</summary>
        public string? Target { get; }

        /// <summary>Status of the first-half copy it was read from.</summary>
        public ReportStatus FirstHalf { get; }
    }

    public sealed class MirrorReport
    {
        public List<ReportEntry> Entries { get; } = new List<ReportEntry>();

        /// <summary>Messages that concern the whole job (target document, plane, assembly placement).</summary>
        public List<string> General { get; } = new List<string>();

        public string TargetName { get; set; } = "";
        public bool Aborted { get; set; }

        /// <summary>Stopped by the user (Esc), not by an error: an assembly run mirrors no further component.</summary>
        public bool Cancelled { get; set; }

        /// <summary>
        /// Complete, but the user pressed Esc during its last step: an assembly run asks before
        /// its next component. Not carried into an assembly report.
        /// </summary>
        public bool StopRequested { get; set; }

        public ReportEntry Add(FeatureRecord r, ReportStatus status, string message)
        {
            var e = new ReportEntry(r.Name, r.TypeName, status, message);
            e.Details.AddRange(r.Notes);
            Entries.Add(e);
            return e;
        }

        /// <summary>
        /// Merges the report of one component into an assembly-level report. Its target (the
        /// saved part) goes into its heading line; <see cref="TargetName"/> stays the assembly's.
        /// </summary>
        public void Append(MirrorReport other, string heading)
        {
            string target = other.TargetName.Length > 0 ? $" → {other.TargetName}" : "";
            General.Add($"— {heading}{target}: {other.Summary}");
            General.AddRange(other.General.Select(g => "   " + g));
            foreach (ReportEntry e in other.Entries)
            {
                var copy = new ReportEntry($"{heading} › {e.Feature}", e.Type, e.Status, e.Message) { Verification = e.Verification, HasEquivalent = e.HasEquivalent };
                copy.Details.AddRange(e.Details);
                Entries.Add(copy);
            }
            Aborted |= other.Aborted;
            Cancelled |= other.Cancelled;
        }

        /// <summary>
        /// The entries of the second half a mirror feature rebuilt, re-homed under it in the
        /// style of <see cref="Append"/>: „Spiegeln1 › Boss-Extrude1“ (the copy's own name,
        /// not that of the first-half copy it was read from), with the name of the new feature in
        /// the target, and not better than its first half
        /// (<see cref="Planning.MirrorFeatureRules.WithFirstHalf"/>).
        /// </summary>
        /// <param name="lookup">Name of a step's record (the first-half copy in the target) → what is known of it; null: nothing.</param>
        public static List<ReportEntry> Nested(string owner, IEnumerable<ReportEntry> entries, Func<string, SecondHalfStep?> lookup)
        {
            var result = new List<ReportEntry>();
            foreach (ReportEntry e in entries)
            {
                SecondHalfStep? step = lookup(e.Feature);
                ReportStatus status = step == null ? e.Status : Planning.MirrorFeatureRules.WithFirstHalf(e.Status, step.FirstHalf);
                var copy = new ReportEntry($"{owner} › {step?.Logical ?? e.Feature}", e.Type, status, e.Message)
                {
                    Verification = e.Verification,
                    HasEquivalent = e.HasEquivalent,
                };
                copy.Details.AddRange(e.Details);
                if (step?.Target is string target) copy.Details.Add($"im Ziel: „{target}“");
                if (status != e.Status) copy.Details.Add($"Die erste Hälfte hat den Status „{StatusText(step!.FirstHalf)}“; die zweite ist ihre Kopie.");
                result.Add(copy);
            }
            return result;
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

        /// <summary>Entries that keep <see cref="AllMirrored"/> false: no counterpart, failed, or with a note to check.</summary>
        public IEnumerable<ReportEntry> Blocking => Entries.Where(e => !(e.Status == ReportStatus.Ok || (e.Status == ReportStatus.Skipped && e.HasEquivalent)));

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
