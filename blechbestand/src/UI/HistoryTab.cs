using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>
    /// Reiter „Bestellhistorie“: gebuchte Lieferungen, wahlweise nach Lieferdatum oder nach Bestelldatum zusammengefasst
    /// (neueste zuerst). Nach Bestelldatum erscheinen auch die noch offenen Positionen derselben Bestellung.
    /// Einträge entstehen beim Buchen einer Lieferung im Reiter „Bestand“ oder „Bestellung“.
    /// </summary>
    internal sealed class HistoryTab : UserControl
    {
        private const int ColGroup = 0, ColMaterial = 1, ColThickness = 2, ColDimension = 3, ColNote = 4, ColOtherDate = 5,
            ColOrdered = 6, ColDelivered = 7, ColWeight = 8, ColErp = 9, ColBookedBy = 10, ColHint = 11;

        private const string AllGroups = "Alle Materialgruppen";
        private static readonly string[] Periods = { "Gesamter Zeitraum", "Letzte 30 Tage", "Letzte 3 Monate", "Letzte 12 Monate", "Dieses Jahr", "Letztes Jahr" };

        /// <summary>Kennung einer offenen Bestellung in der Tabelle.</summary>
        private sealed class OpenTag
        {
            public Guid ItemId;
            public string Raw;
            public override bool Equals(object obj) => obj is OpenTag o && o.ItemId == ItemId && o.Raw == Raw;
            public override int GetHashCode() => ItemId.GetHashCode() ^ (Raw ?? "").GetHashCode();
        }

        private sealed class Header
        {
            public string Title;
            public int Count;
            public string Extra;
        }

        private readonly IAppHost host;
        private readonly DataGridView grid;
        private readonly TextBox search;
        private readonly ComboBox period;
        private readonly ComboBox groupFilter;
        private readonly RadioButton byDelivery;
        private readonly RadioButton byOrder;
        private readonly Label summary;
        private readonly Label empty;
        private readonly Button deleteButton;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem bookItem;
        private readonly ToolStripMenuItem deleteItem;
        private readonly Dictionary<string, Header> headers = new Dictionary<string, Header>();
        private bool fillingFilters;

        public HistoryTab(IAppHost host)
        {
            this.host = host;
            BackColor = Theme.Window;
            Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(4));

            // ---------- Werkzeugleiste ----------
            byDelivery = new RadioButton { Text = "Lieferdatum", AutoSize = true, Margin = new Padding(Theme.S(4), Theme.S(8), Theme.S(3), Theme.S(3)) };
            byOrder = new RadioButton { Text = "Bestelldatum", AutoSize = true, Margin = new Padding(Theme.S(4), Theme.S(8), Theme.S(3), Theme.S(3)) };
            if (host.Settings.HistoryByOrderDate) byOrder.Checked = true; else byDelivery.Checked = true;
            EventHandler viewChanged = (s, e) =>
            {
                if (!((RadioButton)s).Checked) return;
                host.Settings.HistoryByOrderDate = byOrder.Checked;
                host.Settings.Save();
                Rebuild();
            };
            byDelivery.CheckedChanged += viewChanged;
            byOrder.CheckedChanged += viewChanged;
            var viewBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, Theme.S(16), 0) };
            var viewLabel = Theme.FlowLabel("Zusammenfassen nach", Theme.S(24));
            viewLabel.Font = Theme.UiFont(10f, FontStyle.Bold);
            viewLabel.Width = TextRenderer.MeasureText(viewLabel.Text, viewLabel.Font).Width + Theme.S(6);
            viewBox.Controls.Add(viewLabel);
            viewBox.Controls.Add(byDelivery);
            viewBox.Controls.Add(byOrder);

            search = new TextBox { Width = Theme.S(170), Margin = new Padding(Theme.S(3), Theme.S(5), Theme.S(3), Theme.S(3)) };
            Theme.SetCue(search, "Werkstoff, Dicke, Eulanda-Nr. …");
            search.TextChanged += (s, e) => Rebuild();
            period = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(175), Margin = new Padding(Theme.S(10), Theme.S(5), Theme.S(3), Theme.S(3)) };
            period.Items.AddRange(Periods.Cast<object>().ToArray());
            period.SelectedIndex = 0;
            period.SelectedIndexChanged += (s, e) => Rebuild();
            groupFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(190), Margin = new Padding(Theme.S(6), Theme.S(5), Theme.S(3), Theme.S(3)) };
            groupFilter.SelectedIndexChanged += (s, e) => { if (!fillingFilters) Rebuild(); };

            var filters = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            filters.Controls.Add(viewBox);
            filters.Controls.Add(Theme.FlowLabel("Suchen", search.PreferredHeight));
            filters.Controls.Add(search);
            filters.Controls.Add(period);
            filters.Controls.Add(groupFilter);

            var export = Theme.SecondaryButton("Export für Excel");
            export.Click += (s, e) => ExportCsv();
            deleteButton = Theme.SecondaryButton("Eintrag löschen");
            deleteButton.Click += (s, e) => DeleteSelected();
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(0) };
            actions.Controls.Add(export);
            actions.Controls.Add(deleteButton);

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 0, 0, Theme.S(6)) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.Controls.Add(filters, 0, 0);
            toolbar.Controls.Add(actions, 1, 0);

            // ---------- Tabelle ----------
            grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            Theme.StyleGrid(grid, host.Settings.FontSize);
            AddColumn("Gruppe", 145);
            AddColumn("Material", 100);
            AddColumn("Dicke T\n[mm]", 60, right: true);
            AddColumn("Dimension", 80);
            AddColumn("Bemerkung", 75);
            AddColumn("Bestellt am", 80, right: true, muted: true);
            AddColumn("Bestellt", 80, right: true, muted: true);
            AddColumn("Geliefert", 85, right: true);
            AddColumn("Gewicht ca.", 85, right: true);
            AddColumn("Eulanda-\nnummer", 80);
            AddColumn("Gebucht von", 105, muted: true);
            AddColumn("Hinweis", 150);
            grid.Columns[ColDelivered].DefaultCellStyle.Font = Theme.UiFont(host.Settings.FontSize + 0.5f, FontStyle.Bold);
            grid.RowPostPaint += (s, e) =>
            {
                if (grid.Rows[e.RowIndex].Tag is string key && headers.TryGetValue(key, out Header h))
                    Theme.PaintGroupRow(grid, e, h.Title, h.Count, h.Extra);
            };
            grid.SelectionChanged += (s, e) => deleteButton.Enabled = SelectedRecord() != null;
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is OpenTag) BookSelected();
            };

            menu = new ContextMenuStrip { Font = Theme.UiFont(10f) };
            bookItem = new ToolStripMenuItem("Geliefert – Menge zum Bestand addieren …", null, (s, e) => BookSelected()) { Font = Theme.UiFont(10f, FontStyle.Bold) };
            deleteItem = new ToolStripMenuItem("Eintrag löschen …", null, (s, e) => DeleteSelected());
            menu.Items.Add(bookItem);
            menu.Items.Add(deleteItem);
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                object tag = grid.Rows[e.RowIndex].Tag;
                if (!(tag is Guid) && !(tag is OpenTag)) return;
                try { grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)]; } catch (InvalidOperationException) { return; }
                bookItem.Visible = tag is OpenTag;
                deleteItem.Visible = tag is Guid;
                menu.Show(grid, grid.PointToClient(Cursor.Position));
            };

            empty = new Label
            {
                Text = "Noch keine Lieferungen gebucht.\n\nEine Lieferung bucht man im Reiter „Bestand“ per Rechtsklick auf „Offene Bestellung“\n" +
                       "→ „Geliefert – Menge zum Bestand addieren“. Sie erscheint dann hier.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Muted,
                BackColor = Color.White,
                Font = Theme.UiFont(11f),
                Visible = false,
            };

            var gridFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
            gridFrame.Controls.Add(grid);
            gridFrame.Controls.Add(empty);
            empty.BringToFront();

            summary = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Bottom,
                Font = Theme.UiFont(10f, FontStyle.Bold),
                ForeColor = Theme.AccentDark,
                Padding = new Padding(0, Theme.S(6), 0, Theme.S(4)),
            };

            Controls.Add(gridFrame);
            Controls.Add(summary);
            Controls.Add(toolbar);
            gridFrame.BringToFront();

            host.DataChanged += (s, e) => { FillFilters(); Rebuild(); };
            FillFilters();
            Rebuild();
        }

        private bool ByOrderDate => byOrder.Checked;

        public void FocusSearch()
        {
            search.Focus();
            search.SelectAll();
        }

        public void ApplyFontSize(float size)
        {
            Theme.ApplyGridFont(grid, size);
            grid.Columns[ColDelivered].DefaultCellStyle.Font = Theme.UiFont(size + 0.5f, FontStyle.Bold);
            grid.Invalidate();
        }

        private void AddColumn(string header, int width, bool right = false, bool muted = false)
        {
            var c = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = width,
                MinimumWidth = Theme.S(Math.Min(width, 60)),
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
            if (right) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (muted) c.DefaultCellStyle.ForeColor = Theme.Muted;
            grid.Columns.Add(c);
        }

        private void FillFilters()
        {
            fillingFilters = true;
            try
            {
                string g = groupFilter.SelectedItem as string;
                groupFilter.BeginUpdate();
                groupFilter.Items.Clear();
                groupFilter.Items.Add(AllGroups);
                IEnumerable<string> groups = host.History.Deliveries.Select(r => r.Group)
                    .Concat(host.Inventory.Items.Where(i => i.OpenOrder.Length > 0).Select(i => i.Group))
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(x => host.Inventory.GroupIndex(x)).ThenBy(x => x, StringComparer.CurrentCultureIgnoreCase);
                foreach (string x in groups) groupFilter.Items.Add(x);
                groupFilter.SelectedItem = g != null && groupFilter.Items.Contains(g) ? g : AllGroups;
                groupFilter.EndUpdate();
            }
            finally
            {
                fillingFilters = false;
            }
        }

        // =====================================================================
        // Filter
        // =====================================================================

        private bool InPeriod(DateTime? date)
        {
            if (period.SelectedIndex <= 0) return true;
            if (!date.HasValue) return false;
            DateTime today = DateTime.Today, d = date.Value;
            switch (period.SelectedIndex)
            {
                case 1: return d >= today.AddDays(-30);
                case 2: return d >= today.AddMonths(-3);
                case 3: return d >= today.AddMonths(-12);
                case 4: return d.Year == today.Year;
                case 5: return d.Year == today.Year - 1;
                default: return true;
            }
        }

        private bool Matches(string group, params string[] fields)
        {
            string g = groupFilter.SelectedItem as string;
            if (g != null && g != AllGroups && !string.Equals(group, g, StringComparison.CurrentCultureIgnoreCase)) return false;
            string q = search.Text.Trim();
            if (q.Length == 0) return true;
            string hay = (group + " " + string.Join(" ", fields)).ToLowerInvariant();
            return q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(w => hay.Contains(w));
        }

        private bool Matches(DeliveryRecord r) =>
            Matches(r.Group, r.Material, r.Thickness, r.Thickness + "mm", r.Dimension, r.Note, r.ErpNumber, r.OrderText, r.BookedBy,
                r.DeliveredOn.ToString("dd.MM.yyyy"), r.OrderedOn);

        private bool Matches(OrderGroupLine l) =>
            Matches(l.OpenItem.Group, l.OpenItem.Material, l.OpenItem.Thickness, l.OpenItem.Thickness + "mm", l.OpenItem.Dimension,
                l.OpenItem.Note, l.OpenItem.ErpNumber, l.OpenEntry.Raw, "offen");

        private List<DeliveryDay> DeliveryView() =>
            host.History.ByDeliveryDate(host.Inventory, host.History.Deliveries.Where(r => Matches(r) && InPeriod(r.DeliveredOn)));

        private List<OrderGroup> OrderView() =>
            OrderHistory.ByOrderDate(host.Inventory,
                host.History.Deliveries.Where(r => Matches(r) && InPeriod(OrderNotes.ParseDate(r.OrderedOn))),
                OrderGroupLine.OpenLines(host.Inventory).Where(l => Matches(l) && InPeriod(l.OrderedOn)));

        // =====================================================================
        // Anzeige
        // =====================================================================

        private void Rebuild()
        {
            object selected = grid.CurrentCell != null ? grid.Rows[grid.CurrentCell.RowIndex].Tag : null;
            int firstRow = grid.FirstDisplayedScrollingRowIndex;
            grid.Columns[ColOtherDate].HeaderText = ByOrderDate ? "Geliefert am" : "Bestellt am";

            headers.Clear();
            grid.Rows.Clear();
            var rows = new List<DataGridViewRow>();
            int stripe = 0;
            if (ByOrderDate)
            {
                List<OrderGroup> groups = OrderView();
                foreach (OrderGroup g in groups)
                {
                    var parts = new List<string>();
                    if (g.DeliveredCount > 0) parts.Add(g.DeliveredCount + " geliefert");
                    if (g.OpenCount > 0) parts.Add(g.OpenCount + " noch offen");
                    if (g.DeliveredWeight > 0) parts.Add("Tafeln ca. " + Numbers.FormatWeight(g.DeliveredWeight));
                    rows.Add(HeaderRow("O:" + (g.Date?.ToString("yyyy-MM-dd") ?? "ohne"), new Header
                    {
                        Title = g.Date.HasValue ? "Bestellt am " + g.Date.Value.ToString("dddd, dd.MM.yyyy", Numbers.German) : "Ohne Bestelldatum",
                        Count = g.Lines.Count,
                        Extra = string.Join(" · ", parts),
                    }));
                    foreach (OrderGroupLine l in g.Lines)
                        rows.Add(l.IsOpen ? OpenRow(l, stripe++) : DeliveredRow(l.Delivered, stripe++, true));
                }
                int lines = groups.Sum(x => x.Lines.Count), open = groups.Sum(x => x.OpenCount);
                summary.Text = lines == 0 ? "" :
                    lines + (lines == 1 ? " Position" : " Positionen") + " aus " + (groups.Count == 1 ? "1 Bestellung" : groups.Count + " Bestellungen") +
                    " · " + (lines - open) + " geliefert · " + open + " noch offen";
            }
            else
            {
                List<DeliveryDay> days = DeliveryView();
                foreach (DeliveryDay day in days)
                {
                    decimal kg = day.TotalWeight;
                    rows.Add(HeaderRow("D:" + day.Date.ToString("yyyy-MM-dd"), new Header
                    {
                        Title = "Geliefert am " + day.Date.ToString("dddd, dd.MM.yyyy", Numbers.German),
                        Count = day.Records.Count,
                        Extra = kg > 0 ? "Tafeln ca. " + Numbers.FormatWeight(kg) : null,
                    }));
                    foreach (DeliveryRecord r in day.Records) rows.Add(DeliveredRow(r, stripe++, false));
                }
                int count = days.Sum(d => d.Records.Count);
                decimal kgAll = days.Sum(d => d.TotalWeight);
                summary.Text = count == 0 ? "" :
                    (count == 1 ? "1 Lieferposition" : count + " Lieferpositionen") + " an " + (days.Count == 1 ? "1 Tag" : days.Count + " Tagen") +
                    (kgAll > 0 ? " · Tafeln ca. " + Numbers.FormatWeight(kgAll) : "");
            }
            grid.Rows.AddRange(rows.ToArray());

            int target = selected != null ? rows.FindIndex(x => x.Tag.Equals(selected)) : -1;
            try
            {
                if (firstRow >= 0 && firstRow < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = firstRow;
                grid.CurrentCell = target >= 0 ? grid.Rows[target].Cells[ColMaterial] : null;
            }
            catch (InvalidOperationException) { }

            bool nothing = host.History.Deliveries.Count == 0 && !(ByOrderDate && host.Inventory.Items.Any(i => i.OpenOrder.Length > 0));
            empty.Visible = nothing;
            if (!nothing && rows.Count == 0) summary.Text = "Keine Einträge für diesen Filter.";
            deleteButton.Enabled = SelectedRecord() != null;
        }

        private DataGridViewRow HeaderRow(string key, Header h)
        {
            headers[key] = h;
            var row = new DataGridViewRow();
            row.CreateCells(grid);
            row.Height = grid.RowTemplate.Height;
            row.Tag = key;
            row.DefaultCellStyle.BackColor = Theme.GroupBack;
            row.DefaultCellStyle.SelectionBackColor = Theme.GroupBack;
            return row;
        }

        private DataGridViewRow NewRow(int stripe)
        {
            var row = new DataGridViewRow();
            row.CreateCells(grid);
            row.Height = grid.RowTemplate.Height;
            row.DefaultCellStyle.BackColor = stripe % 2 == 1 ? Theme.Stripe : Color.White;
            return row;
        }

        private DataGridViewRow DeliveredRow(DeliveryRecord r, int stripe, bool orderView)
        {
            DataGridViewRow row = NewRow(stripe);
            row.Tag = r.Id;
            row.Cells[ColGroup].Value = r.Group;
            row.Cells[ColMaterial].Value = r.Material;
            row.Cells[ColThickness].Value = r.Thickness;
            row.Cells[ColDimension].Value = r.Dimension;
            row.Cells[ColNote].Value = r.Note;
            row.Cells[ColOtherDate].Value = orderView ? r.DeliveredOn.ToString("dd.MM.yyyy") : r.OrderedOn;
            row.Cells[ColOrdered].Value = r.OrderedText;
            row.Cells[ColDelivered].Value = r.DeliveredText;
            row.Cells[ColWeight].Value = Numbers.FormatWeight(r.Weight);
            row.Cells[ColErp].Value = r.ErpNumber;
            row.Cells[ColBookedBy].Value = r.BookedBy + (r.BookedAt > DateTime.MinValue ? " · " + r.BookedAt.ToString("dd.MM. HH:mm") : "");
            row.Cells[ColHint].Value = Hint(r);
            if (r.Partial) row.Cells[ColHint].Style.ForeColor = Theme.YellowFore;
            row.Cells[ColDelivered].ToolTipText = r.OrderText.Length > 0 ? "Bestellung: " + r.OrderText : "";
            return row;
        }

        private DataGridViewRow OpenRow(OrderGroupLine l, int stripe)
        {
            SheetItem i = l.OpenItem;
            OrderNoteEntry e = l.OpenEntry;
            DataGridViewRow row = NewRow(stripe);
            row.Tag = new OpenTag { ItemId = i.Id, Raw = e.Raw };
            row.Cells[ColGroup].Value = i.Group;
            row.Cells[ColMaterial].Value = i.Material;
            row.Cells[ColThickness].Value = i.Thickness;
            row.Cells[ColDimension].Value = i.Dimension;
            row.Cells[ColNote].Value = i.Note;
            row.Cells[ColOtherDate].Value = "–";
            row.Cells[ColOrdered].Value = e.Quantity.HasValue ? Units.Format(e.Quantity.Value, e.Unit) : e.Raw;
            row.Cells[ColDelivered].Value = "";
            row.Cells[ColWeight].Value = "";
            row.Cells[ColErp].Value = i.ErpNumber;
            row.Cells[ColBookedBy].Value = "";
            row.Cells[ColHint].Value = e.Raw.EndsWith("(Rest)") ? "Rest noch offen" : "noch offen";
            row.Cells[ColHint].Style.ForeColor = Theme.AccentDark;
            row.Cells[ColHint].Style.Font = Theme.UiFont(host.Settings.FontSize, FontStyle.Bold);
            string tip = "Offene Bestellung: " + e.Raw + "\nRechtsklick oder Doppelklick → „Geliefert – Menge zum Bestand addieren“";
            for (int c = 0; c < row.Cells.Count; c++) row.Cells[c].ToolTipText = tip;
            return row;
        }

        private static string Hint(DeliveryRecord r)
        {
            if (r.Partial) return "Teillieferung, Rest offen";
            if (r.OrderedQuantity.HasValue && r.OrderedUnit == r.DeliveredUnit && r.DeliveredQuantity > r.OrderedQuantity.Value) return "mehr als bestellt";
            if (r.OrderedQuantity.HasValue && r.OrderedUnit.Length > 0 && r.OrderedUnit != r.DeliveredUnit) return "in anderer Einheit";
            return "";
        }

        private DeliveryRecord SelectedRecord()
        {
            if (grid.CurrentCell == null) return null;
            return grid.Rows[grid.CurrentCell.RowIndex].Tag is Guid id ? host.History.Deliveries.FirstOrDefault(r => r.Id == id) : null;
        }

        // =====================================================================
        // Aktionen
        // =====================================================================

        private void BookSelected()
        {
            if (grid.CurrentCell == null || !(grid.Rows[grid.CurrentCell.RowIndex].Tag is OpenTag tag)) return;
            DeliveryDialog.Book(FindForm(), host, tag.ItemId);
        }

        private void DeleteSelected()
        {
            DeliveryRecord r = SelectedRecord();
            if (r == null) return;
            if (MessageBox.Show(FindForm(),
                    "Diesen Eintrag aus der Bestellhistorie löschen?\n\n" + r.DisplayName + " (" + r.Group + ")\nGeliefert am " +
                    r.DeliveredOn.ToString("dd.MM.yyyy") + ": " + r.DeliveredText +
                    "\n\nDer Bestand wird dabei nicht verändert.",
                    "Eintrag löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            Guid id = r.Id;
            host.Modify((inv, history, log) =>
            {
                DeliveryRecord x = history.Deliveries.FirstOrDefault(d => d.Id == id);
                if (x == null) return;
                history.Deliveries.Remove(x);
                log.Add(x.DisplayName, "Bestellhistorie", "geliefert am " + x.DeliveredOn.ToString("dd.MM.yyyy") + ": " + x.DeliveredText, "Eintrag gelöscht");
            });
        }

        private void ExportCsv()
        {
            var lines = new List<string[]>();
            if (ByOrderDate)
            {
                foreach (OrderGroup g in OrderView())
                {
                    foreach (OrderGroupLine l in g.Lines)
                    {
                        if (l.IsOpen)
                        {
                            SheetItem i = l.OpenItem;
                            lines.Add(new[]
                            {
                                l.OrderedOnText, "", "offen", i.Group, i.Material, i.Thickness, i.Dimension, i.Note,
                                l.OpenEntry.Quantity.HasValue ? Units.Format(l.OpenEntry.Quantity.Value, l.OpenEntry.Unit) : l.OpenEntry.Raw,
                                "", "", "", i.ErpNumber, "", "", "",
                            });
                        }
                        else lines.Add(CsvRow(l.Delivered));
                    }
                }
            }
            else
            {
                foreach (DeliveryDay day in DeliveryView())
                    foreach (DeliveryRecord r in day.Records) lines.Add(CsvRow(r));
            }
            if (lines.Count == 0)
            {
                MessageBox.Show(FindForm(), "Es gibt (mit diesem Filter) nichts zu exportieren.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new SaveFileDialog
            {
                Title = "Bestellhistorie für Excel exportieren",
                Filter = "CSV für Excel (*.csv)|*.csv",
                FileName = "Bestellhistorie_" + (ByOrderDate ? "nach_Bestelldatum_" : "nach_Lieferdatum_") + DateTime.Now.ToString("yyyy-MM-dd") + ".csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(";", "Bestellt am", "Geliefert am", "Status", "Gruppe", "Material", "Dicke T [mm]", "Dimension", "Bemerkung",
                    "Bestellt", "Geliefert", "Einheit", "Gewicht [kg]", "Eulandanummer", "Hinweis", "Gebucht von", "Gebucht am"));
                foreach (string[] l in lines)
                    sb.AppendLine(string.Join(";", l.Select((v, idx) => idx == 12 && v.Length > 0 ? "=\"" + v + "\"" : Csv.Escape(v))));
                try
                {
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                    if (MessageBox.Show(FindForm(), "Exportiert nach\n" + dlg.FileName + "\n\nJetzt öffnen?", "Export",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Export fehlgeschlagen:\n" + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string[] CsvRow(DeliveryRecord r) => new[]
        {
            r.OrderedOn, r.DeliveredOn.ToString("dd.MM.yyyy"), r.Partial ? "teilweise geliefert" : "geliefert", r.Group, r.Material, r.Thickness,
            r.Dimension, r.Note, r.OrderedText, Numbers.Format(r.DeliveredQuantity), r.DeliveredUnit, Numbers.Format(r.Weight), r.ErpNumber,
            Hint(r), r.BookedBy, r.BookedAt > DateTime.MinValue ? r.BookedAt.ToString("dd.MM.yyyy HH:mm") : "",
        };
    }
}
