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
