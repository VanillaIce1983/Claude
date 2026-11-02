using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace Blechbestand.Model
{
    /// <summary>Der gesamte Bestand: Materialgruppen in fester Reihenfolge und alle Positionen.</summary>
    [XmlRoot("Blechbestand")]
    public class Inventory
    {
        public int FormatVersion { get; set; } = 1;

        /// <summary>Paketgewicht beim Lieferanten in kg (0 = keine Paketgewichte berechnen).</summary>
        public decimal PackageWeight { get; set; } = Packaging.DefaultPackageWeight;

        [XmlArray("Groups"), XmlArrayItem("Group")]
        public List<string> Groups { get; set; } = new List<string>();

        [XmlArray("Items"), XmlArrayItem("Item")]
        public List<SheetItem> Items { get; set; } = new List<SheetItem>();

        public SheetItem Find(Guid id) => Items.FirstOrDefault(i => i.Id == id);

        public int GroupIndex(string group)
        {
            int i = Groups.FindIndex(g => string.Equals(g, group, StringComparison.CurrentCultureIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        }

        /// <summary>Legt eine Gruppe an, falls es sie noch nicht gibt, und liefert die gespeicherte Schreibweise.</summary>
        public string EnsureGroup(string group)
        {
            group = (group ?? "").Trim();
            if (group.Length == 0) group = "Sonstiges";
            string existing = Groups.FirstOrDefault(g => string.Equals(g, group, StringComparison.CurrentCultureIgnoreCase));
            if (existing != null) return existing;
            Groups.Add(group);
            return group;
        }

        /// <summary>Gruppen in Anzeige-Reihenfolge, die mindestens eine Position enthalten.</summary>
        public IEnumerable<string> UsedGroups() =>
            Groups.Where(g => Items.Any(i => string.Equals(i.Group, g, StringComparison.CurrentCultureIgnoreCase)));

        /// <summary>Positionen sortiert wie in der Excel-Liste: Gruppe, Werkstoff, Dicke, Zusatz.</summary>
        public List<SheetItem> Sorted() =>
            Items.OrderBy(i => GroupIndex(i.Group))
                 .ThenBy(i => i.Material, StringComparer.CurrentCultureIgnoreCase)
                 .ThenBy(i => i.ThicknessValue)
                 .ThenBy(i => i.Thickness, StringComparer.CurrentCultureIgnoreCase)
                 .ThenBy(i => i.Dimension, StringComparer.CurrentCultureIgnoreCase)
                 .ThenBy(i => i.Note, StringComparer.CurrentCultureIgnoreCase)
                 .ToList();

        public void RenameGroup(string oldName, string newName)
        {
            newName = (newName ?? "").Trim();
            if (newName.Length == 0) return;
            int idx = Groups.FindIndex(g => string.Equals(g, oldName, StringComparison.CurrentCultureIgnoreCase));
            string target = Groups.FirstOrDefault(g => string.Equals(g, newName, StringComparison.CurrentCultureIgnoreCase));
            if (target != null && idx >= 0 && !string.Equals(Groups[idx], target, StringComparison.CurrentCultureIgnoreCase))
            {
                // Mit vorhandener Gruppe zusammenführen
                Groups.RemoveAt(idx);
                newName = target;
            }
            else if (idx >= 0) Groups[idx] = newName;
            else Groups.Add(newName);
            foreach (SheetItem i in Items.Where(i => string.Equals(i.Group, oldName, StringComparison.CurrentCultureIgnoreCase)))
                i.Group = newName;
        }

        public void MoveGroup(string group, int delta)
        {
            int idx = Groups.FindIndex(g => string.Equals(g, group, StringComparison.CurrentCultureIgnoreCase));
            if (idx < 0) return;
            // Nur über sichtbare (benutzte) Gruppen hinweg verschieben
            List<string> used = UsedGroups().ToList();
            int u = used.FindIndex(g => string.Equals(g, group, StringComparison.CurrentCultureIgnoreCase));
            int targetU = u + delta;
            if (u < 0 || targetU < 0 || targetU >= used.Count) return;
            string neighbour = used[targetU];
            int nIdx = Groups.FindIndex(g => string.Equals(g, neighbour, StringComparison.CurrentCultureIgnoreCase));
            Groups[idx] = neighbour;
            Groups[nIdx] = group;
        }

        /// <summary>Repariert Dateien von Hand oder aus älteren Versionen: null-Listen, fehlende Gruppen, doppelte IDs.</summary>
        public void Normalize()
        {
            if (PackageWeight < 0) PackageWeight = 0;
            if (Groups == null) Groups = new List<string>();
            if (Items == null) Items = new List<SheetItem>();
            Groups = Groups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim())
                           .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            var seen = new HashSet<Guid>();
            foreach (SheetItem i in Items)
            {
                i.Normalize();
                if (!seen.Add(i.Id)) { i.Id = Guid.NewGuid(); seen.Add(i.Id); }
                i.Group = EnsureGroup(i.Group);
            }
        }
    }
}
