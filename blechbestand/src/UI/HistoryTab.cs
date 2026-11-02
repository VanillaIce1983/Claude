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
    /// Reiter „Bestellhistorie“: alle gebuchten Lieferungen, nach Lieferdatum zusammengefasst (neueste zuerst).
    /// Einträge entstehen beim Buchen einer Lieferung im Reiter „Bestand“ oder „Bestellung“.
    /// </summary>
    internal sealed class HistoryTab : UserControl
    {
        private const int ColGroup = 0, ColMaterial = 1, ColThickness = 2, ColDimension = 3, ColNote = 4, ColOrderedOn = 5,
            ColOrdered = 6, ColDelivered = 7, ColWeight = 8, ColErp = 9, ColBookedBy = 10, ColHint = 11;

        private const string AllGroups = "Alle Materialgruppen";
        private static readonly string[] Periods = { "Alle Lieferungen", "Letzte 30 Tage", "Letzte 3 Monate", "Letzte 12 Monate", "Dieses Jahr", "Letztes Jahr" };

        private readonly IAppHost host;
        private readonly DataGridView grid;
        private readonly TextBox search;
        private readonly ComboBox period;
        private readonly ComboBox groupFilter;
        private readonly Label summary;
        private readonly Label empty;
        private readonly Button deleteButton;
        private readonly Dictionary<string, DeliveryDay> dayByKey = new Dictionary<string, DeliveryDay>();
        private bool fillingFilters;

        public HistoryTab(IAppHost host)
        {
            this.host = host;
            BackColor = Theme.Window;
            Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(4));

            // ---------- Werkzeugleiste ----------
            search = new TextBox { Width = Theme.S(210), Margin = new Padding(Theme.S(3), Theme.S(5), Theme.S(3), Theme.S(3)) };
            Theme.SetCue(search, "Werkstoff, Dicke, Eulanda-Nr. …");
            search.TextChanged += (s, e) => Rebuild();
            period = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(160), Margin = new Padding(Theme.S(12), Theme.S(5), Theme.S(3), Theme.S(3)) };
            period.Items.AddRange(Periods.Cast<object>().ToArray());
            period.SelectedIndex = 0;
            period.SelectedIndexChanged += (s, e) => Rebuild();
            groupFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(210), Margin = new Padding(Theme.S(6), Theme.S(5), Theme.S(3), Theme.S(3)) };
            groupFilter.SelectedIndexChanged += (s, e) => { if (!fillingFilters) Rebuild(); };

            var filters = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
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
                if (grid.Rows[e.RowIndex].Tag is string key && dayByKey.TryGetValue(key, out DeliveryDay day))
                {
                    decimal kg = day.TotalWeight;
                    Theme.PaintGroupRow(grid, e, "Geliefert am " + day.Date.ToString("dddd, dd.MM.yyyy", Numbers.German),
                        day.Records.Count, kg > 0 ? "Tafeln ca. " + Numbers.FormatWeight(kg) : null);
                }
            };
            grid.SelectionChanged += (s, e) => deleteButton.Enabled = SelectedRecord() != null;
            var menu = new ContextMenuStrip { Font = Theme.UiFont(10f) };
            menu.Items.Add("Eintrag löschen …", null, (s, e) => DeleteSelected());
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || !(grid.Rows[e.RowIndex].Tag is Guid)) return;
                try { grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)]; } catch (InvalidOperationException) { return; }
                menu.Show(grid, grid.PointToClient(Cursor.Position));
            };

            empty = new Label
            {
                Text = "Noch keine Lieferungen gebucht.\n\nEine Lieferung bucht man im Reiter „Bestand“ per Rechtsklick auf „Offene Bestellung“\n" +
                       "→ „Geliefert – Menge zum Bestand addieren“. Sie erscheint dann hier, nach Lieferdatum zusammengefasst.",
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
                IEnumerable<string> groups = host.History.Deliveries.Select(r => r.Group).Where(x => x.Length > 0)
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

        private IEnumerable<DeliveryRecord> Filtered()
        {
            IEnumerable<DeliveryRecord> list = host.History.Deliveries;
            DateTime today = DateTime.Today;
            switch (period.SelectedIndex)
            {
                case 1: list = list.Where(r => r.DeliveredOn >= today.AddDays(-30)); break;
                case 2: list = list.Where(r => r.DeliveredOn >= today.AddMonths(-3)); break;
                case 3: list = list.Where(r => r.DeliveredOn >= today.AddMonths(-12)); break;
                case 4: list = list.Where(r => r.DeliveredOn.Year == today.Year); break;
                case 5: list = list.Where(r => r.DeliveredOn.Year == today.Year - 1); break;
            }
            string g = groupFilter.SelectedItem as string;
            if (g != null && g != AllGroups) list = list.Where(r => string.Equals(r.Group, g, StringComparison.CurrentCultureIgnoreCase));
            string q = search.Text.Trim();
            if (q.Length > 0)
            {
                string[] words = q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                list = list.Where(r =>
                {
                    string hay = (r.Group + " " + r.Material + " " + r.Thickness + " " + r.Thickness + "mm " + r.Dimension + " " +
                                  r.Note + " " + r.ErpNumber + " " + r.OrderText + " " + r.BookedBy + " " +
                                  r.DeliveredOn.ToString("dd.MM.yyyy")).ToLowerInvariant();
                    return words.All(w => hay.Contains(w));
                });
            }
            return list;
        }

        private void Rebuild()
        {
            List<DeliveryDay> days = host.History.ByDeliveryDate(host.Inventory, Filtered());
            object selected = grid.CurrentCell != null ? grid.Rows[grid.CurrentCell.RowIndex].Tag : null;
            int firstRow = grid.FirstDisplayedScrollingRowIndex;

            dayByKey.Clear();
            grid.Rows.Clear();
            var rows = new List<DataGridViewRow>();
            int stripe = 0;
            foreach (DeliveryDay day in days)
            {
                string key = day.Date.ToString("yyyy-MM-dd");
                dayByKey[key] = day;
                var header = new DataGridViewRow();
                header.CreateCells(grid);
                header.Height = grid.RowTemplate.Height;
                header.Tag = key;
                header.DefaultCellStyle.BackColor = Theme.GroupBack;
                header.DefaultCellStyle.SelectionBackColor = Theme.GroupBack;
                rows.Add(header);
                foreach (DeliveryRecord r in day.Records)
                {
                    var row = new DataGridViewRow();
                    row.CreateCells(grid);
                    row.Height = grid.RowTemplate.Height;
                    row.Tag = r.Id;
                    row.DefaultCellStyle.BackColor = stripe++ % 2 == 1 ? Theme.Stripe : Color.White;
                    row.Cells[ColGroup].Value = r.Group;
                    row.Cells[ColMaterial].Value = r.Material;
                    row.Cells[ColThickness].Value = r.Thickness;
                    row.Cells[ColDimension].Value = r.Dimension;
                    row.Cells[ColNote].Value = r.Note;
                    row.Cells[ColOrderedOn].Value = r.OrderedOn;
                    row.Cells[ColOrdered].Value = r.OrderedText;
                    row.Cells[ColDelivered].Value = r.DeliveredText;
                    row.Cells[ColWeight].Value = Numbers.FormatWeight(r.Weight);
                    row.Cells[ColErp].Value = r.ErpNumber;
                    row.Cells[ColBookedBy].Value = r.BookedBy + (r.BookedAt > DateTime.MinValue ? " · " + r.BookedAt.ToString("dd.MM. HH:mm") : "");
                    row.Cells[ColHint].Value = Hint(r);
                    if (r.Partial) row.Cells[ColHint].Style.ForeColor = Theme.YellowFore;
                    row.Cells[ColDelivered].ToolTipText = r.OrderText.Length > 0 ? "Bestellung: " + r.OrderText : "";
                    rows.Add(row);
                }
            }
            grid.Rows.AddRange(rows.ToArray());

            int target = selected != null ? rows.FindIndex(x => x.Tag.Equals(selected)) : -1;
            try
            {
                if (firstRow >= 0 && firstRow < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = firstRow;
                grid.CurrentCell = target >= 0 ? grid.Rows[target].Cells[ColMaterial] : null;
            }
            catch (InvalidOperationException) { }

            bool none = host.History.Deliveries.Count == 0;
            empty.Visible = none;
            int count = days.Sum(d => d.Records.Count);
            decimal kgAll = days.Sum(d => d.TotalWeight);
            summary.Text = none ? "" :
                (count == 1 ? "1 Lieferposition" : count + " Lieferpositionen") + " an " + (days.Count == 1 ? "1 Tag" : days.Count + " Tagen") +
                (kgAll > 0 ? " · Tafeln ca. " + Numbers.FormatWeight(kgAll) : "") +
                (count < host.History.Deliveries.Count ? "   (gefiltert, insgesamt " + host.History.Deliveries.Count + ")" : "");
            deleteButton.Enabled = SelectedRecord() != null;
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
            List<DeliveryDay> days = host.History.ByDeliveryDate(host.Inventory, Filtered());
            if (days.Count == 0)
            {
                MessageBox.Show(FindForm(), "Es gibt (mit diesem Filter) keine Lieferungen zum Exportieren.", "Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new SaveFileDialog
            {
                Title = "Bestellhistorie für Excel exportieren",
                Filter = "CSV für Excel (*.csv)|*.csv",
                FileName = "Bestellhistorie_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(";", "Lieferdatum", "Gruppe", "Material", "Dicke T [mm]", "Dimension", "Bemerkung", "Bestellt am",
                    "Bestellt", "Geliefert", "Einheit", "Gewicht [kg]", "Eulandanummer", "Hinweis", "Gebucht von", "Gebucht am"));
                foreach (DeliveryDay day in days)
                {
                    foreach (DeliveryRecord r in day.Records)
                    {
                        sb.AppendLine(string.Join(";", new[]
                        {
                            r.DeliveredOn.ToString("dd.MM.yyyy"), r.Group, r.Material, r.Thickness, r.Dimension, r.Note, r.OrderedOn,
                            r.OrderedText, Numbers.Format(r.DeliveredQuantity), r.DeliveredUnit, Numbers.Format(r.Weight),
                            r.ErpNumber.Length > 0 ? "=\"" + r.ErpNumber + "\"" : "", Hint(r), r.BookedBy,
                            r.BookedAt > DateTime.MinValue ? r.BookedAt.ToString("dd.MM.yyyy HH:mm") : "",
                        }.Select((v, idx) => idx == 11 ? v : Csv.Escape(v))));
                    }
                }
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
    }
}
