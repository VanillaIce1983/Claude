using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace Blechbestand.Model
{
    /// <summary>
    /// Eine gebuchte Lieferung. Die Angaben zum Material werden beim Buchen kopiert, damit die Historie
    /// auch nach Umbenennen oder Löschen der Position stimmt.
    /// </summary>
    public class DeliveryRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Lieferdatum (nur der Tag zählt).</summary>
        public DateTime DeliveredOn { get; set; }

        public decimal DeliveredQuantity { get; set; }

        public string DeliveredUnit { get; set; } = Units.Sheets;

        /// <summary>Der Bestellvermerk, auf den sich die Lieferung bezieht, z. B. „10 Tafeln am 27.10.2026“.</summary>
        public string OrderText { get; set; } = "";

        /// <summary>Bestelldatum als Text aus dem Vermerk („27.10.2026“), leer wenn unbekannt.</summary>
        public string OrderedOn { get; set; } = "";

        public decimal? OrderedQuantity { get; set; }

        public string OrderedUnit { get; set; } = "";

        /// <summary>Teillieferung: ein Rest der Bestellung ist noch offen.</summary>
        public bool Partial { get; set; }

        public Guid ItemId { get; set; }
        public string Group { get; set; } = "";
        public string Material { get; set; } = "";
        public string Thickness { get; set; } = "";
        public string Dimension { get; set; } = "";
        public string Note { get; set; } = "";
        public string ErpNumber { get; set; } = "";
        public decimal? WeightPerSheet { get; set; }

        public DateTime BookedAt { get; set; }
        public string BookedBy { get; set; } = "";

        [XmlIgnore]
        public string DisplayName
        {
            get
            {
                string s = Material.Trim();
                if (!string.IsNullOrWhiteSpace(Thickness)) s += " " + Thickness.Trim() + " mm";
                if (!string.IsNullOrWhiteSpace(Note)) s += " " + Note.Trim();
                return s.Trim();
            }
        }

        [XmlIgnore]
        public string DeliveredText => Units.Format(DeliveredQuantity, DeliveredUnit);

        [XmlIgnore]
        public string OrderedText => OrderedQuantity.HasValue && OrderedUnit.Length > 0 ? Units.Format(OrderedQuantity.Value, OrderedUnit) : "";

        /// <summary>Gewicht der gelieferten Tafeln in kg (nur bei Tafeln mit bekanntem Tafelgewicht).</summary>
        [XmlIgnore]
        public decimal? Weight => DeliveredUnit == Units.Sheets && WeightPerSheet.HasValue ? DeliveredQuantity * WeightPerSheet.Value : (decimal?)null;

        /// <summary>Neuer Eintrag aus der Position (Momentaufnahme) und dem Bestellvermerk.</summary>
        public static DeliveryRecord Create(SheetItem item, OrderNoteEntry order, decimal delivered, string unit,
            DateTime deliveredOn, bool partial, string bookedBy)
        {
            return new DeliveryRecord
            {
                DeliveredOn = deliveredOn.Date,
                DeliveredQuantity = delivered,
                DeliveredUnit = unit,
                OrderText = order?.Raw ?? "",
                OrderedOn = order?.Date ?? "",
                OrderedQuantity = order?.Quantity,
                OrderedUnit = order?.Unit ?? "",
                Partial = partial,
                ItemId = item.Id,
                Group = item.Group,
                Material = item.Material,
                Thickness = item.Thickness,
                Dimension = item.Dimension,
                Note = item.Note,
                ErpNumber = item.ErpNumber,
                WeightPerSheet = item.WeightPerSheet,
                BookedAt = DateTime.Now,
                BookedBy = bookedBy ?? "",
            };
        }

        public void Normalize()
        {
            if (Id == Guid.Empty) Id = Guid.NewGuid();
            DeliveredOn = DeliveredOn.Date;
            DeliveredUnit = string.IsNullOrEmpty(DeliveredUnit) ? Units.Sheets : DeliveredUnit;
            OrderText = OrderText ?? "";
            OrderedOn = OrderedOn ?? "";
            OrderedUnit = OrderedUnit ?? "";
            Group = Group ?? "";
            Material = Material ?? "";
            Thickness = Thickness ?? "";
            Dimension = Dimension ?? "";
            Note = Note ?? "";
            ErpNumber = ErpNumber ?? "";
            BookedBy = BookedBy ?? "";
        }
    }

    /// <summary>Alle Lieferungen eines Tages (eine Gruppe im Reiter „Bestellhistorie“).</summary>
    public class DeliveryDay
    {
        public DateTime Date { get; set; }
        public List<DeliveryRecord> Records { get; set; } = new List<DeliveryRecord>();
        public decimal TotalWeight => Records.Sum(r => r.Weight ?? 0m);
    }

    /// <summary>
    /// Eine Zeile der Ansicht „nach Bestelldatum“: entweder eine gebuchte Lieferung oder eine noch offene Bestellung
    /// (Eintrag im Feld „Offene Bestellung“ einer Position).
    /// </summary>
    public class OrderGroupLine
    {
        public DeliveryRecord Delivered { get; set; }
        public SheetItem OpenItem { get; set; }
        public OrderNoteEntry OpenEntry { get; set; }

        public bool IsOpen => Delivered == null;
        public string Group => IsOpen ? OpenItem.Group : Delivered.Group;
        public string Material => IsOpen ? OpenItem.Material : Delivered.Material;
        public string Thickness => IsOpen ? OpenItem.Thickness : Delivered.Thickness;
        public string Note => IsOpen ? OpenItem.Note : Delivered.Note;

        /// <summary>Bestelldatum als Text, wie im Vermerk.</summary>
        public string OrderedOnText => IsOpen ? (OpenEntry.Date ?? "") : Delivered.OrderedOn;

        public DateTime? OrderedOn => OrderNotes.ParseDate(OrderedOnText);

        /// <summary>Alle offenen Bestellungen aller Positionen als Zeilen.</summary>
        public static List<OrderGroupLine> OpenLines(Inventory inventory) =>
            inventory.Items.SelectMany(i => OrderNotes.Parse(i.OpenOrder).Select(e => new OrderGroupLine { OpenItem = i, OpenEntry = e })).ToList();
    }

    /// <summary>Alles, was am selben Tag bestellt wurde (eine Gruppe in der Ansicht „nach Bestelldatum“).</summary>
    public class OrderGroup
    {
        /// <summary>Bestelldatum; null = ohne erkennbares Bestelldatum.</summary>
        public DateTime? Date { get; set; }
        public List<OrderGroupLine> Lines { get; set; } = new List<OrderGroupLine>();
        public int OpenCount => Lines.Count(l => l.IsOpen);
        public int DeliveredCount => Lines.Count(l => !l.IsOpen);
        public decimal DeliveredWeight => Lines.Where(l => !l.IsOpen).Sum(l => l.Delivered.Weight ?? 0m);
    }

    /// <summary>Bestellhistorie, gespeichert in einer eigenen Datei neben der Bestandsdatei („bestand_historie.xml“).</summary>
    [XmlRoot("Bestellhistorie")]
    public class OrderHistory
    {
        public int FormatVersion { get; set; } = 1;

        [XmlArray("Deliveries"), XmlArrayItem("Delivery")]
        public List<DeliveryRecord> Deliveries { get; set; } = new List<DeliveryRecord>();

        public void Normalize()
        {
            if (Deliveries == null) Deliveries = new List<DeliveryRecord>();
            var seen = new HashSet<Guid>();
            foreach (DeliveryRecord r in Deliveries)
            {
                r.Normalize();
                if (!seen.Add(r.Id)) { r.Id = Guid.NewGuid(); seen.Add(r.Id); }
            }
        }

        /// <summary>
        /// Lieferungen nach Lieferdatum zusammengefasst, neuester Tag zuerst. Innerhalb eines Tages sortiert
        /// wie der Bestand (Gruppenreihenfolge, Werkstoff, Dicke).
        /// </summary>
        /// <summary>
        /// Gelieferte und noch offene Positionen nach Bestelldatum zusammengefasst, neueste Bestellung zuerst,
        /// Positionen ohne Bestelldatum am Ende. Innerhalb einer Bestellung wie der Bestand sortiert; eine
        /// Teillieferung und ihr offener Rest stehen untereinander.
        /// </summary>
        public static List<OrderGroup> ByOrderDate(Inventory inventory, IEnumerable<DeliveryRecord> delivered, IEnumerable<OrderGroupLine> open)
        {
            Func<string, int> groupIndex = g => inventory?.GroupIndex(g) ?? 0;
            IEnumerable<OrderGroupLine> lines = (delivered ?? Enumerable.Empty<DeliveryRecord>()).Select(r => new OrderGroupLine { Delivered = r })
                .Concat(open ?? Enumerable.Empty<OrderGroupLine>());
            return lines
                .GroupBy(l => l.OrderedOn)
                .OrderBy(g => g.Key.HasValue ? 0 : 1)
                .ThenByDescending(g => g.Key)
                .Select(g => new OrderGroup
                {
                    Date = g.Key,
                    Lines = g.OrderBy(l => groupIndex(l.Group))
                             .ThenBy(l => l.Material, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(l => Numbers.FirstNumber(l.Thickness) ?? 0m)
                             .ThenBy(l => l.Note, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(l => l.IsOpen ? 1 : 0)
                             .ThenBy(l => l.IsOpen ? DateTime.MaxValue : l.Delivered.DeliveredOn)
                             .ToList(),
                })
                .ToList();
        }

        public List<DeliveryDay> ByDeliveryDate(Inventory inventory, IEnumerable<DeliveryRecord> records = null)
        {
            Func<string, int> groupIndex = g => inventory?.GroupIndex(g) ?? 0;
            return (records ?? Deliveries)
                .GroupBy(r => r.DeliveredOn.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new DeliveryDay
                {
                    Date = g.Key,
                    Records = g.OrderBy(r => groupIndex(r.Group))
                               .ThenBy(r => r.Material, StringComparer.CurrentCultureIgnoreCase)
                               .ThenBy(r => Numbers.FirstNumber(r.Thickness) ?? 0m)
                               .ThenBy(r => r.Note, StringComparer.CurrentCultureIgnoreCase)
                               .ThenBy(r => r.BookedAt)
                               .ToList(),
                })
                .ToList();
        }
    }
}
