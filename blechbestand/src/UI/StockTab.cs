using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>
    /// Reiter „Bestand“: die Liste wie in der Excel-Tabelle. Pakete und Tafeln werden direkt in der Zelle
    /// überschrieben (z. B. vom Mann an der Lasermaschine), alles andere über „Bearbeiten“.
    /// </summary>
    internal sealed class StockTab : UserControl
    {
        private const int ColMaterial = 0, ColThickness = 1, ColDimension = 2, ColPackages = 3, ColSheets = 4,
            ColErp = 5, ColWeight = 6, ColNote = 7, ColMinPackages = 8, ColMinSheets = 9, ColOrder = 10, ColChanged = 11;

        private const string AllGroups = "Alle Materialgruppen";
        private const string AllThicknesses = "Alle Dicken";

        private readonly IAppHost host;
        private readonly DataGridView grid;
        private readonly TextBox search;
        private readonly ComboBox groupFilter;
        private readonly ComboBox thicknessFilter;
        private readonly CheckBox onlyLow;
        private readonly Button editButton;
        private readonly Button deleteButton;
        private readonly Label summary;
        private readonly ContextMenuStrip itemMenu;
        private readonly ContextMenuStrip groupMenu;
        private readonly Dictionary<string, int> groupCounts = new Dictionary<string, int>();

        private List<object> rowKeys = new List<object>();
        private bool rebuildPending;
        private bool fillingFilters;

        public StockTab(IAppHost host)
        {
            this.host = host;
            BackColor = Theme.Window;
            Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(4));

            // ---------- Werkzeugleiste ----------
            search = new TextBox { Width = Theme.S(210), Margin = new Padding(Theme.S(3), Theme.S(5), Theme.S(3), Theme.S(3)) };
            Theme.SetCue(search, "Werkstoff, Dicke, Eulanda-Nr. …");
            search.TextChanged += (s, e) => Rebuild();
            groupFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(210), Margin = new Padding(Theme.S(12), Theme.S(5), Theme.S(3), Theme.S(3)) };
            groupFilter.SelectedIndexChanged += (s, e) => { if (!fillingFilters) Rebuild(); };
            thicknessFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(110), Margin = new Padding(Theme.S(6), Theme.S(5), Theme.S(3), Theme.S(3)) };
            thicknessFilter.SelectedIndexChanged += (s, e) => { if (!fillingFilters) Rebuild(); };
            onlyLow = new CheckBox { Text = "Nur knappe Bestände", AutoSize = true, Margin = new Padding(Theme.S(12), Theme.S(8), Theme.S(3), Theme.S(3)) };
            onlyLow.CheckedChanged += (s, e) => Rebuild();

            var filters = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            filters.Controls.Add(Theme.FlowLabel("Suchen", search.PreferredHeight));
            filters.Controls.Add(search);
            filters.Controls.Add(groupFilter);
            filters.Controls.Add(thicknessFilter);
            filters.Controls.Add(onlyLow);

            var addButton = Theme.PrimaryButton("+  Neues Material");
            addButton.Click += (s, e) => AddItem();
            editButton = Theme.SecondaryButton("Bearbeiten");
            editButton.Click += (s, e) => EditSelected();
            deleteButton = Theme.SecondaryButton("Löschen");
            deleteButton.Click += (s, e) => DeleteSelected();
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(0) };
            actions.Controls.Add(addButton);
            actions.Controls.Add(editButton);
            actions.Controls.Add(deleteButton);

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 0, 0, Theme.S(6)) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.Controls.Add(filters, 0, 0);
            toolbar.Controls.Add(actions, 1, 0);

            // ---------- Tabelle ----------
            grid = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(grid, host.Settings.FontSize);
            AddColumn("Material", 110, ColMaterial);
            AddColumn("Dicke T\n[mm]", 70, ColThickness, right: true);
            AddColumn("Dimension", 95, ColDimension);
            AddColumn("Pakete", 70, ColPackages, right: true, editable: true);
            AddColumn("Tafeln", 70, ColSheets, right: true, editable: true);
            AddColumn("Eulanda-\nnummer", 90, ColErp);
            AddColumn("Gewicht\npro Tafel", 85, ColWeight, right: true);
            AddColumn("Bemerkung", 100, ColNote);
            AddColumn("Mindest\nPakete", 66, ColMinPackages, right: true, muted: true);
            AddColumn("Mindest\nTafeln", 66, ColMinSheets, right: true, muted: true);
            AddColumn("Offene Bestellung", 150, ColOrder);
            AddColumn("Geändert", 130, ColChanged, muted: true);
            var editableHeader = Color.FromArgb(0x0B, 0x4A, 0x9E);
            grid.Columns[ColPackages].HeaderCell.Style.BackColor = editableHeader;
            grid.Columns[ColSheets].HeaderCell.Style.BackColor = editableHeader;
            grid.Columns[ColPackages].HeaderCell.ToolTipText = "Anzahl Pakete – direkt in die Zelle tippen, Enter speichert";
            grid.Columns[ColSheets].HeaderCell.ToolTipText = "Anzahl Tafeln – direkt in die Zelle tippen, Enter speichert";

            grid.CellBeginEdit += Grid_CellBeginEdit;
            grid.CellValidating += Grid_CellValidating;
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.EditingControlShowing += (s, e) =>
            {
                if (e.Control is TextBox tb) { tb.TextAlign = HorizontalAlignment.Right; tb.SelectAll(); }
            };
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex == ColPackages || e.ColumnIndex == ColSheets) { grid.BeginEdit(true); return; }
                if (e.ColumnIndex == ColOrder && SelectedItem()?.OpenOrder.Length > 0) { BookDelivery(); return; }
                if (KeyAt(e.RowIndex) is Guid) EditSelected();
            };
            grid.RowPostPaint += (s, e) =>
            {
                if (KeyAt(e.RowIndex) is string g) Theme.PaintGroupRow(grid, e, g, groupCounts.TryGetValue(g, out int n) ? n : 0);
            };
            grid.CellMouseDown += Grid_CellMouseDown;
            grid.SelectionChanged += (s, e) => UpdateButtons();
            grid.KeyDown += Grid_KeyDown;

            itemMenu = new ContextMenuStrip { Font = Theme.UiFont(10f) };
            groupMenu = new ContextMenuStrip { Font = Theme.UiFont(10f) };
            BuildMenus();

            // ---------- Fußzeile: Legende und Schnellbuttons ----------
            var legend = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            legend.Controls.Add(Swatch(Theme.RedBack, Theme.RedFore));
            legend.Controls.Add(LegendLabel("unter Mindestbestand"));
            legend.Controls.Add(Swatch(Theme.YellowBack, Theme.YellowFore));
            legend.Controls.Add(LegendLabel("Mindestbestand erreicht"));
            summary = LegendLabel("");
            summary.Font = Theme.UiFont(10f, FontStyle.Bold);
            summary.Margin = new Padding(Theme.S(16), Theme.S(6), Theme.S(3), Theme.S(3));
            legend.Controls.Add(summary);
            var hint = LegendLabel("Pakete und Tafeln einfach in die Zelle tippen, Enter speichert sofort.");
            hint.ForeColor = Theme.Muted;
            hint.Margin = new Padding(Theme.S(16), Theme.S(6), Theme.S(3), Theme.S(3));
            legend.Controls.Add(hint);

            var minus = Theme.SecondaryButton("−1");
            minus.Font = Theme.UiFont(12f, FontStyle.Bold);
            minus.MinimumSize = new Size(Theme.S(52), 0);
            minus.Click += (s, e) => Step(-1);
            var plus = Theme.SecondaryButton("+1");
            plus.Font = Theme.UiFont(12f, FontStyle.Bold);
            plus.MinimumSize = new Size(Theme.S(52), 0);
            plus.Click += (s, e) => Step(+1);
            var tip = new ToolTip();
            tip.SetToolTip(minus, "Markierte Menge um 1 verringern (Pakete oder Tafeln)");
            tip.SetToolTip(plus, "Markierte Menge um 1 erhöhen (Pakete oder Tafeln)");
            var history = Theme.SecondaryButton("Verlauf");
            history.Click += (s, e) => OpenHistory();
            tip.SetToolTip(history, "Wer hat wann welche Menge geändert (öffnet sich in Excel)");
            var export = Theme.SecondaryButton("Export für Excel");
            export.Click += (s, e) => ExportCsv();
            var quick = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(0) };
            quick.Controls.Add(minus);
            quick.Controls.Add(plus);
            quick.Controls.Add(history);
            quick.Controls.Add(export);

            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, Theme.S(6), 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(legend, 0, 0);
            footer.Controls.Add(quick, 1, 0);

            var gridFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
            gridFrame.Controls.Add(grid);

            Controls.Add(gridFrame);
            Controls.Add(footer);
            Controls.Add(toolbar);
            gridFrame.BringToFront();

            host.DataChanged += (s, e) => { FillFilters(); Rebuild(); };
            FillFilters();
            UpdateButtons();
        }

        /// <summary>Gerade wird eine Zelle bearbeitet – dann kein Neuladen von außen.</summary>
        public bool IsEditing => grid.IsCurrentCellInEditMode || pendingSave != null;

        /// <summary>Beendet eine laufende Eingabe und speichert sie sofort (z. B. beim Schließen des Programms).</summary>
        public void EndEditing()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            FlushPendingSave();
        }

        private Tuple<Guid, bool, decimal?> pendingSave;

        private void FlushPendingSave()
        {
            Tuple<Guid, bool, decimal?> p = pendingSave;
            pendingSave = null;
            if (p != null) SaveQuantity(p.Item1, p.Item2, p.Item3);
        }

        public void FocusSearch()
        {
            search.Focus();
            search.SelectAll();
        }

        public void ApplyFontSize(float size)
        {
            Theme.ApplyGridFont(grid, size);
            grid.Columns[ColPackages].DefaultCellStyle.Font = Theme.UiFont(size + 0.5f, FontStyle.Bold);
            grid.Columns[ColSheets].DefaultCellStyle.Font = Theme.UiFont(size + 0.5f, FontStyle.Bold);
            grid.Invalidate();
        }

        private void AddColumn(string header, int width, int index, bool right = false, bool editable = false, bool muted = false)
        {
            var c = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Name = "c" + index,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FillWeight = width,
                MinimumWidth = Theme.S(Math.Min(width, 60)),
                ReadOnly = !editable,
            };
            if (right) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (muted) c.DefaultCellStyle.ForeColor = Theme.Muted;
            if (editable) c.DefaultCellStyle.Font = Theme.UiFont(host.Settings.FontSize + 0.5f, FontStyle.Bold);
            grid.Columns.Add(c);
        }

        private static Label Swatch(Color back, Color border)
        {
            var l = new Label
            {
                AutoSize = false,
                Size = new Size(Theme.S(18), Theme.S(18)),
                BackColor = back,
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = border,
                Margin = new Padding(Theme.S(3), Theme.S(8), Theme.S(2), Theme.S(3)),
            };
            return l;
        }

        private static Label LegendLabel(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(Theme.S(2), Theme.S(8), Theme.S(10), Theme.S(3)),
            Font = Theme.UiFont(10f),
        };

        // =====================================================================
        // Anzeige
        // =====================================================================

        private object KeyAt(int rowIndex) => rowIndex >= 0 && rowIndex < grid.Rows.Count ? grid.Rows[rowIndex].Tag : null;

        private SheetItem SelectedItem()
        {
            if (grid.CurrentCell == null) return null;
            return KeyAt(grid.CurrentCell.RowIndex) is Guid id ? host.Inventory.Find(id) : null;
        }

        private string SelectedGroup()
        {
            if (grid.CurrentCell == null) return null;
            return KeyAt(grid.CurrentCell.RowIndex) as string;
        }

        private string FilteredGroup()
        {
            string g = groupFilter.SelectedItem as string;
            return g == AllGroups ? null : g;
        }

        private void FillFilters()
        {
            fillingFilters = true;
            try
            {
                string g = groupFilter.SelectedItem as string;
                string t = thicknessFilter.SelectedItem as string;
                groupFilter.BeginUpdate();
                groupFilter.Items.Clear();
                groupFilter.Items.Add(AllGroups);
                foreach (string x in host.Inventory.UsedGroups()) groupFilter.Items.Add(x);
                groupFilter.SelectedItem = g != null && groupFilter.Items.Contains(g) ? g : AllGroups;
                groupFilter.EndUpdate();

                thicknessFilter.BeginUpdate();
                thicknessFilter.Items.Clear();
                thicknessFilter.Items.Add(AllThicknesses);
                foreach (string x in host.Inventory.Items.Where(i => i.Thickness.Length > 0)
                             .GroupBy(i => i.Thickness).Select(grp => grp.First())
                             .OrderBy(i => i.ThicknessValue).ThenBy(i => i.Thickness).Select(i => i.Thickness))
                    thicknessFilter.Items.Add(x);
                thicknessFilter.SelectedItem = t != null && thicknessFilter.Items.Contains(t) ? t : AllThicknesses;
                thicknessFilter.EndUpdate();
            }
            finally
            {
                fillingFilters = false;
            }
        }

        private List<SheetItem> VisibleItems()
        {
            IEnumerable<SheetItem> items = host.Inventory.Sorted();
            string g = groupFilter.SelectedItem as string;
            if (g != null && g != AllGroups) items = items.Where(i => string.Equals(i.Group, g, StringComparison.CurrentCultureIgnoreCase));
            string t = thicknessFilter.SelectedItem as string;
            if (t != null && t != AllThicknesses) items = items.Where(i => i.Thickness == t);
            if (onlyLow.Checked) items = items.Where(i => i.IsLow);
            string q = search.Text.Trim();
            if (q.Length > 0)
            {
                string[] words = q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                items = items.Where(i =>
                {
                    string hay = (i.Group + " " + i.Material + " " + i.Thickness + " " + i.Thickness + "mm " + i.Dimension + " " +
                                  i.ErpNumber + " " + i.Note + " " + i.OpenOrder).ToLowerInvariant();
                    return words.All(w => hay.Contains(w));
                });
            }
            return items.ToList();
        }

        /// <summary>
        /// Baut die Tabelle neu auf. Ist die Zeilenfolge unverändert, werden nur die Werte aktualisiert,
        /// damit Markierung und Bildlaufposition stehen bleiben.
        /// </summary>
        private void Rebuild()
        {
            if (grid.IsCurrentCellInEditMode) { rebuildPending = true; return; }
            rebuildPending = false;

            List<SheetItem> items = VisibleItems();
            var keys = new List<object>();
            var itemById = new Dictionary<Guid, SheetItem>();
            groupCounts.Clear();
            string lastGroup = null;
            foreach (SheetItem i in items)
            {
                if (!string.Equals(i.Group, lastGroup, StringComparison.CurrentCultureIgnoreCase))
                {
                    keys.Add(i.Group);
                    lastGroup = i.Group;
                }
                groupCounts[i.Group] = (groupCounts.TryGetValue(i.Group, out int n) ? n : 0) + 1;
                keys.Add(i.Id);
                itemById[i.Id] = i;
            }

            if (keys.SequenceEqual(rowKeys))
            {
                int stripe = 0;
                for (int r = 0; r < grid.Rows.Count; r++)
                {
                    if (rowKeys[r] is Guid id) FillItemRow(grid.Rows[r], itemById[id], stripe++);
                }
                grid.Invalidate();
            }
            else
            {
                object selectedKey = grid.CurrentCell != null ? KeyAt(grid.CurrentCell.RowIndex) : null;
                int selectedColumn = grid.CurrentCell?.ColumnIndex ?? ColSheets;
                int firstRow = grid.FirstDisplayedScrollingRowIndex;

                grid.SuspendLayout();
                grid.Rows.Clear();
                var rows = new List<DataGridViewRow>();
                int stripe = 0;
                foreach (object k in keys)
                {
                    var row = new DataGridViewRow();
                    row.CreateCells(grid);
                    row.Height = grid.RowTemplate.Height;
                    if (k is string g)
                    {
                        row.Tag = g;
                        row.ReadOnly = true;
                        row.DefaultCellStyle.BackColor = Theme.GroupBack;
                        row.DefaultCellStyle.SelectionBackColor = Theme.GroupBack;
                        row.DefaultCellStyle.SelectionForeColor = Theme.GroupText;
                        foreach (DataGridViewCell c in row.Cells) c.Value = "";
                    }
                    else
                    {
                        FillItemRow(row, itemById[(Guid)k], stripe++);
                    }
                    rows.Add(row);
                }
                grid.Rows.AddRange(rows.ToArray());
                rowKeys = keys;
                grid.ResumeLayout();

                // Markierung und Bildlauf wiederherstellen
                int target = selectedKey != null ? keys.FindIndex(k => k.Equals(selectedKey)) : -1;
                if (target < 0) target = keys.FindIndex(k => k is Guid);
                if (target >= 0)
                {
                    try
                    {
                        if (firstRow >= 0 && firstRow < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = firstRow;
                        grid.CurrentCell = grid.Rows[target].Cells[selectedColumn];
                    }
                    catch (InvalidOperationException) { }
                }
                else
                {
                    grid.CurrentCell = null;
                }
            }
            UpdateSummary();
            UpdateButtons();
        }

        private void FillItemRow(DataGridViewRow row, SheetItem i, int stripe)
        {
            row.Tag = i.Id;
            row.DefaultCellStyle.BackColor = stripe % 2 == 1 ? Theme.Stripe : Color.White;
            row.Cells[ColMaterial].Value = i.Material;
            row.Cells[ColThickness].Value = i.Thickness;
            row.Cells[ColDimension].Value = i.Dimension;
            row.Cells[ColPackages].Value = i.Packages.HasValue ? Numbers.Format(i.Packages) : "-";
            row.Cells[ColSheets].Value = i.Sheets.HasValue ? Numbers.Format(i.Sheets) : "-";
            row.Cells[ColErp].Value = i.ErpNumber.Length > 0 ? i.ErpNumber : "-";
            row.Cells[ColWeight].Value = Numbers.FormatWeight(i.WeightPerSheet);
            row.Cells[ColNote].Value = i.Note;
            row.Cells[ColMinPackages].Value = Numbers.Format(i.MinPackages);
            row.Cells[ColMinSheets].Value = Numbers.Format(i.MinSheets);
            row.Cells[ColOrder].Value = i.OpenOrder;
            row.Cells[ColChanged].Value = i.ChangedAt.HasValue
                ? i.ChangedAt.Value.ToString("dd.MM.yy HH:mm") + (i.ChangedBy.Length > 0 ? " · " + i.ChangedBy : "")
                : "";
            StyleLevelCell(row.Cells[ColPackages], i.PackagesLevel, i.MinPackages, Units.Packages);
            decimal? perPackage = i.PackageSheets(host.Inventory.PackageWeight);
            if (perPackage.HasValue)
                row.Cells[ColPackages].ToolTipText = "1 Paket = " + Units.Format(perPackage.Value, Units.Sheets) +
                    (i.WeightPerSheet.HasValue ? " ≈ " + Numbers.FormatWeight(perPackage.Value * i.WeightPerSheet.Value) : "") +
                    (i.SheetsPerPackage.HasValue ? " (eigener Wert)" : "") + "\n" + row.Cells[ColPackages].ToolTipText;
            StyleLevelCell(row.Cells[ColSheets], i.SheetsLevel, i.MinSheets, Units.Sheets);
            row.Cells[ColOrder].Style.ForeColor = i.OpenOrder.Length > 0 ? Theme.AccentDark : Color.Empty;
            row.Cells[ColOrder].ToolTipText = i.OpenOrder.Length > 0
                ? "Bestellt: " + i.OpenOrder + "\nDoppelklick oder Rechtsklick → „Geliefert – Menge zum Bestand addieren“"
                : "";
            row.Cells[ColNote].ToolTipText = i.Note;
        }

        private static void StyleLevelCell(DataGridViewCell cell, StockLevel level, decimal? minimum, string unit)
        {
            Theme.LevelColors(level, out Color back, out Color fore);
            cell.Style.BackColor = back;
            cell.Style.ForeColor = fore;
            cell.Style.SelectionBackColor = Theme.Shade(back);
            cell.Style.SelectionForeColor = fore;
            string tip = minimum.HasValue ? "Mindestbestand: " + Units.Format(minimum.Value, unit) : "Kein Mindestbestand hinterlegt";
            if (level == StockLevel.BelowMinimum) tip = "Unter Mindestbestand! " + tip;
            else if (level == StockLevel.AtMinimum) tip = "Mindestbestand erreicht. " + tip;
            cell.ToolTipText = tip + "\nZum Ändern einfach die neue Menge eintippen.";
        }

        private void UpdateSummary()
        {
            int red = host.Inventory.Items.Count(i => i.Level == StockLevel.BelowMinimum);
            int yellow = host.Inventory.Items.Count(i => i.Level == StockLevel.AtMinimum);
            summary.Text = red + " unter Mindestbestand · " + yellow + " am Mindestbestand";
            summary.ForeColor = red > 0 ? Theme.RedFore : Color.FromArgb(0x1B, 0x5E, 0x20);
        }

        private void UpdateButtons()
        {
            bool item = SelectedItem() != null;
            editButton.Enabled = item;
            deleteButton.Enabled = item;
        }

        // =====================================================================
        // Mengen direkt in der Tabelle ändern
        // =====================================================================

        private void Grid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (!(KeyAt(e.RowIndex) is Guid) || (e.ColumnIndex != ColPackages && e.ColumnIndex != ColSheets))
            {
                e.Cancel = true;
                return;
            }
            // „-“ beim Bearbeiten nicht stehen lassen
            if ((grid[e.ColumnIndex, e.RowIndex].Value as string) == "-") grid[e.ColumnIndex, e.RowIndex].Value = "";
        }

        private void Grid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (!grid.IsCurrentCellInEditMode || (e.ColumnIndex != ColPackages && e.ColumnIndex != ColSheets)) return;
            if (!Numbers.TryParseQuantity(Convert.ToString(e.FormattedValue), out _))
            {
                e.Cancel = true;
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "Bitte eine Zahl eingeben, z. B. 3 oder 1,5 (leer = keine). Esc bricht ab.";
                SystemSounds.Beep.Play();
            }
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            DataGridViewCell cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            cell.ErrorText = "";
            if (!(KeyAt(e.RowIndex) is Guid id) || (e.ColumnIndex != ColPackages && e.ColumnIndex != ColSheets))
            {
                if (rebuildPending) BeginInvoke(new Action(Rebuild));
                return;
            }
            bool packages = e.ColumnIndex == ColPackages;
            decimal? value = Numbers.ParseQuantityOrNull(Convert.ToString(cell.Value));
            // Direkt nach dem Ereignis speichern (nicht darin, sonst stört der Neuaufbau die Tabelle).
            // EndEditing() speichert sofort, falls das Programm vorher geschlossen wird.
            pendingSave = Tuple.Create(id, packages, value);
            BeginInvoke(new Action(FlushPendingSave));
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (grid.CurrentCell == null) return;
            if (e.KeyCode == Keys.Add || (e.KeyCode == Keys.Oemplus && !e.Shift)) { Step(+1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus) { Step(-1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Delete && KeyAt(grid.CurrentCell.RowIndex) is Guid && grid.CurrentCell.ColumnIndex != ColPackages && grid.CurrentCell.ColumnIndex != ColSheets)
            {
                DeleteSelected();
                e.Handled = true;
            }
        }

        /// <summary>Speichert eine neue Menge. Bei einer Erhöhung wird nach einer vermerkten Bestellung gefragt.</summary>
        private void SaveQuantity(Guid id, bool packages, decimal? value)
        {
            SheetItem current = host.Inventory.Find(id);
            if (current == null) { Rebuild(); return; }
            decimal? old = packages ? current.Packages : current.Sheets;
            if (old == value)
            {
                Rebuild(); // Schreibweise vereinheitlichen, z. B. „1.5“ → „1,5“
                return;
            }
            bool clearOrder = false;
            if ((value ?? 0) > (old ?? 0) && current.OpenOrder.Length > 0)
            {
                clearOrder = MessageBox.Show(FindForm(),
                    "Für " + current.DisplayName + " ist eine Bestellung vermerkt:\n„" + current.OpenOrder + "“\n\n" +
                    "Ist die Lieferung eingetroffen? Dann wird der Vermerk entfernt.",
                    "Lieferung eingetroffen?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }
            string field = packages ? "Pakete" : "Tafeln";
            void Apply(Inventory inv, OrderHistory history, ChangeLog log)
            {
                SheetItem it = inv.Find(id) ?? throw new ItemGoneException();
                decimal? before = packages ? it.Packages : it.Sheets;
                if (packages) it.Packages = value; else it.Sheets = value;
                it.ChangedAt = DateTime.Now;
                it.ChangedBy = host.Store.UserName;
                log.Add(it.DisplayName, field, Numbers.Format(before), Numbers.Format(value));
                if (history != null && it.OpenOrder.Length > 0)
                {
                    // Lieferung = Erhöhung der Menge; ist genau eine Bestellung vermerkt, wird sie zugeordnet
                    List<OrderNoteEntry> notes = OrderNotes.Parse(it.OpenOrder);
                    OrderNoteEntry entry = notes.Count == 1 ? notes[0] : new OrderNoteEntry { Raw = it.OpenOrder };
                    decimal delivered = (value ?? 0) - (before ?? 0);
                    if (delivered > 0)
                        history.Deliveries.Add(DeliveryRecord.Create(it, entry, delivered, packages ? Units.Packages : Units.Sheets,
                            DateTime.Today, false, host.Store.UserName, it.PackageSheets(inv.PackageWeight)));
                    log.Add(it.DisplayName, "Offene Bestellung", it.OpenOrder, "geliefert");
                    it.OpenOrder = "";
                }
            }
            if (clearOrder) host.Modify((inv, history, log) => Apply(inv, history, log));
            else host.Modify((inv, log) => Apply(inv, null, log));
        }

        /// <summary>Schnelländerung um ±1 für die markierte Zelle (Pakete oder Tafeln).</summary>
        private void Step(int delta)
        {
            EndEditing();
            SheetItem it = SelectedItem();
            if (it == null)
            {
                MessageBox.Show(FindForm(), "Bitte zuerst eine Zeile in der Tabelle anklicken.", "Blechbestand", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int col = grid.CurrentCell.ColumnIndex;
            bool packages = col == ColPackages || (col != ColSheets && it.PreferredUnit == Units.Packages);
            decimal current = (packages ? it.Packages : it.Sheets) ?? 0m;
            decimal next = Math.Max(0m, current + delta);
            if (next == current) return;
            SaveQuantity(it.Id, packages, next);
        }

        // =====================================================================
        // Materialien anlegen, bearbeiten, löschen
        // =====================================================================

        public void AddItem()
        {
            EndEditing();
            SheetItem template = SelectedItem();
            var fresh = new SheetItem
            {
                Group = template?.Group ?? SelectedGroup() ?? FilteredGroup() ?? "",
                Material = template?.Material ?? "",
                Dimension = template?.Dimension ?? "3000x1500",
            };
            using (var dlg = new ItemDialog(host.Inventory, fresh, true))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                SheetItem result = dlg.Result;
                result.ChangedAt = DateTime.Now;
                result.ChangedBy = host.Store.UserName;
                if (host.Modify((inv, log) =>
                {
                    result.Group = inv.EnsureGroup(result.Group);
                    inv.Items.Add(result);
                    log.Add(result.DisplayName, "Neu angelegt", "", result.StockText);
                }))
                {
                    SelectItem(result.Id, ColSheets);
                }
            }
        }

        private void EditSelected()
        {
            EndEditing();
            SheetItem original = SelectedItem();
            if (original == null) return;
            original = original.Clone();
            using (var dlg = new ItemDialog(host.Inventory, original, false))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                SheetItem edited = dlg.Result;
                host.Modify((inv, log) =>
                {
                    SheetItem it = inv.Find(original.Id) ?? throw new ItemGoneException();
                    ApplyChanges(original, edited, it, log, inv);
                    it.ChangedAt = DateTime.Now;
                    it.ChangedBy = host.Store.UserName;
                });
            }
        }

        /// <summary>
        /// Übernimmt nur die im Dialog tatsächlich geänderten Felder. So bleibt eine Mengenänderung,
        /// die ein anderer Rechner währenddessen gespeichert hat, erhalten.
        /// </summary>
        private static void ApplyChanges(SheetItem before, SheetItem after, SheetItem target, ChangeLog log, Inventory inv)
        {
            string name = target.DisplayName;
            void Text(string field, string a, string b, Action<string> set)
            {
                if (a == b) return;
                set(b);
                log.Add(name, field, a, b);
            }
            void Num(string field, decimal? a, decimal? b, Action<decimal?> set)
            {
                if (a == b) return;
                set(b);
                log.Add(name, field, Numbers.Format(a), Numbers.Format(b));
            }
            Text("Gruppe", before.Group, after.Group, v => target.Group = inv.EnsureGroup(v));
            Text("Werkstoff", before.Material, after.Material, v => target.Material = v);
            Text("Dicke", before.Thickness, after.Thickness, v => target.Thickness = v);
            Text("Dimension", before.Dimension, after.Dimension, v => target.Dimension = v);
            Text("Bemerkung", before.Note, after.Note, v => target.Note = v);
            Text("Eulandanummer", before.ErpNumber, after.ErpNumber, v => target.ErpNumber = v);
            Text("Offene Bestellung", before.OpenOrder, after.OpenOrder, v => target.OpenOrder = v);
            Num("Pakete", before.Packages, after.Packages, v => target.Packages = v);
            Num("Tafeln", before.Sheets, after.Sheets, v => target.Sheets = v);
            Num("Mindest Pakete", before.MinPackages, after.MinPackages, v => target.MinPackages = v);
            Num("Mindest Tafeln", before.MinSheets, after.MinSheets, v => target.MinSheets = v);
            Num("Gewicht pro Tafel", before.WeightPerSheet, after.WeightPerSheet, v => target.WeightPerSheet = v);
            Num("Tafeln pro Paket", before.SheetsPerPackage, after.SheetsPerPackage, v => target.SheetsPerPackage = v);
        }

        private void DeleteSelected()
        {
            EndEditing();
            SheetItem it = SelectedItem();
            if (it == null) return;
            if (MessageBox.Show(FindForm(),
                    "Material „" + it.DisplayName + "“ (" + it.Group + ") wirklich aus der Liste löschen?\n\nBestand: " + it.StockText,
                    "Material löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            Guid id = it.Id;
            host.Modify((inv, log) =>
            {
                SheetItem x = inv.Find(id);
                if (x == null) return;
                inv.Items.Remove(x);
                log.Add(x.DisplayName, "Gelöscht", x.StockText, "");
            });
        }

        private void SelectItem(Guid id, int column)
        {
            int idx = rowKeys.FindIndex(k => k is Guid g && g == id);
            if (idx < 0)
            {
                // Filter zurücksetzen, damit die neue Position sichtbar ist
                search.Text = "";
                onlyLow.Checked = false;
                groupFilter.SelectedItem = AllGroups;
                thicknessFilter.SelectedItem = AllThicknesses;
                Rebuild();
                idx = rowKeys.FindIndex(k => k is Guid g && g == id);
            }
            if (idx >= 0)
            {
                grid.CurrentCell = grid.Rows[idx].Cells[column];
                grid.Focus();
            }
        }

        // =====================================================================
        // Kontextmenü
        // =====================================================================

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            EndEditing();
            try { grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)]; } catch (InvalidOperationException) { return; }
            Point at = grid.PointToClient(Cursor.Position);
            if (KeyAt(e.RowIndex) is Guid) { UpdateItemMenu(); itemMenu.Show(grid, at); }
            else if (KeyAt(e.RowIndex) is string) groupMenu.Show(grid, at);
        }

        private ToolStripMenuItem receivedItem;
        private ToolStripMenuItem deliveredItem;

        private void BuildMenus()
        {
            deliveredItem = new ToolStripMenuItem("Geliefert – Menge zum Bestand addieren …", null, (s, e) => BookDelivery())
            {
                Font = Theme.UiFont(10f, FontStyle.Bold),
            };
            itemMenu.Items.Add(deliveredItem);
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add("Bearbeiten …", null, (s, e) => EditSelected());
            itemMenu.Items.Add("Zur Bestellung vormerken", null, (s, e) => { SheetItem it = SelectedItem(); if (it != null) host.AddToOrder(it.Id); });
            receivedItem = new ToolStripMenuItem("Bestellvermerk entfernen (ohne Mengenänderung)", null, (s, e) => ClearOpenOrder());
            itemMenu.Items.Add(receivedItem);
            itemMenu.Items.Add("Alle Bestellvermerke entfernen …", null, (s, e) => ClearAllOpenOrders());
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add("Neues Material …", null, (s, e) => AddItem());
            itemMenu.Items.Add("Ähnliches Material anlegen (Kopie) …", null, (s, e) => DuplicateSelected());
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add("Löschen", null, (s, e) => DeleteSelected());

            groupMenu.Items.Add("Gruppe umbenennen …", null, (s, e) => RenameGroup());
            groupMenu.Items.Add("Gruppe nach oben", null, (s, e) => MoveGroup(-1));
            groupMenu.Items.Add("Gruppe nach unten", null, (s, e) => MoveGroup(+1));
            groupMenu.Items.Add(new ToolStripSeparator());
            groupMenu.Items.Add("Neues Material in dieser Gruppe …", null, (s, e) => AddItem());
            groupMenu.Items.Add("Alle Bestellvermerke entfernen …", null, (s, e) => ClearAllOpenOrders());
        }

        private void UpdateItemMenu()
        {
            SheetItem it = SelectedItem();
            bool hasOrder = it != null && it.OpenOrder.Length > 0;
            receivedItem.Enabled = hasOrder;
            deliveredItem.Enabled = hasOrder;
            List<OrderNoteEntry> notes = hasOrder ? OrderNotes.Parse(it.OpenOrder) : new List<OrderNoteEntry>();
            deliveredItem.Text = "Geliefert – Menge zum Bestand addieren" +
                (notes.Count == 1 && notes[0].Quantity.HasValue ? " (" + Units.Format(notes[0].Quantity.Value, notes[0].Unit) + ")" : "") + " …";
        }

        private void BookDelivery()
        {
            EndEditing();
            SheetItem it = SelectedItem();
            if (it == null || it.OpenOrder.Length == 0) return;
            DeliveryDialog.Book(FindForm(), host, it.Id);
        }

        private void ClearOpenOrder()
        {
            SheetItem it = SelectedItem();
            if (it == null || it.OpenOrder.Length == 0) return;
            Guid id = it.Id;
            host.Modify((inv, log) =>
            {
                SheetItem x = inv.Find(id) ?? throw new ItemGoneException();
                log.Add(x.DisplayName, "Offene Bestellung", x.OpenOrder, "geliefert");
                x.OpenOrder = "";
                x.ChangedAt = DateTime.Now;
                x.ChangedBy = host.Store.UserName;
            });
        }

        /// <summary>Entfernt alle Vermerke „bestellt am …“ (z. B. nach einem Probeexport).</summary>
        private void ClearAllOpenOrders()
        {
            List<SheetItem> marked = host.Inventory.Items.Where(i => i.OpenOrder.Length > 0).ToList();
            if (marked.Count == 0)
            {
                MessageBox.Show(FindForm(), "Es ist bei keiner Position eine Bestellung vermerkt.", "Bestellvermerke",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string list = string.Join("\n", marked.Take(12).Select(i => "• " + i.DisplayName + ": " + i.OpenOrder));
            if (marked.Count > 12) list += "\n• … und " + (marked.Count - 12) + " weitere";
            if (MessageBox.Show(FindForm(),
                    "Bei " + marked.Count + " Position(en) ist eine Bestellung vermerkt:\n\n" + list +
                    "\n\nAlle diese Vermerke entfernen? Die Mengen bleiben unverändert.",
                    "Bestellvermerke entfernen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            host.Modify((inv, log) =>
            {
                foreach (SheetItem x in inv.Items.Where(i => i.OpenOrder.Length > 0))
                {
                    log.Add(x.DisplayName, "Offene Bestellung", x.OpenOrder, "entfernt");
                    x.OpenOrder = "";
                }
            });
        }

        private void DuplicateSelected()
        {
            SheetItem it = SelectedItem();
            if (it == null) return;
            SheetItem copy = it.Clone();
            copy.Id = Guid.NewGuid();
            copy.Packages = null;
            copy.Sheets = null;
            copy.ErpNumber = "";
            copy.OpenOrder = "";
            copy.ChangedAt = null;
            copy.ChangedBy = "";
            using (var dlg = new ItemDialog(host.Inventory, copy, true))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                SheetItem result = dlg.Result;
                result.ChangedAt = DateTime.Now;
                result.ChangedBy = host.Store.UserName;
                if (host.Modify((inv, log) =>
                {
                    result.Group = inv.EnsureGroup(result.Group);
                    inv.Items.Add(result);
                    log.Add(result.DisplayName, "Neu angelegt", "", result.StockText);
                }))
                    SelectItem(result.Id, ColSheets);
            }
        }

        private void RenameGroup()
        {
            string g = SelectedGroup();
            if (g == null) return;
            string name = Prompt.Show(FindForm(), "Gruppe umbenennen", "Neuer Name für „" + g + "“:", g);
            if (string.IsNullOrWhiteSpace(name) || name == g) return;
            host.Modify((inv, log) =>
            {
                inv.RenameGroup(g, name);
                log.Add(g, "Gruppe umbenannt", g, name);
            });
        }

        private void MoveGroup(int delta)
        {
            string g = SelectedGroup();
            if (g == null) return;
            host.Modify((inv, log) => inv.MoveGroup(g, delta));
            int idx = rowKeys.FindIndex(k => k is string s && s == g);
            if (idx >= 0) grid.CurrentCell = grid.Rows[idx].Cells[0];
        }

        // =====================================================================
        // Export und Verlauf
        // =====================================================================

        private void ExportCsv()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Bestand für Excel exportieren",
                Filter = "CSV für Excel (*.csv)|*.csv",
                FileName = "Blechbestand_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(";", "Gruppe", "Material", "Dicke T [mm]", "Dimension", "Pakete", "Tafeln", "Eulandanummer",
                    "Gewicht pro Tafel [kg]", "Bemerkung", "Mindest Pakete", "Mindest Tafeln", "Status", "Offene Bestellung", "Geändert", "Geändert von"));
                foreach (SheetItem i in host.Inventory.Sorted())
                {
                    string status = i.Level == StockLevel.BelowMinimum ? "unter Mindestbestand" : i.Level == StockLevel.AtMinimum ? "Mindestbestand erreicht" : "";
                    sb.AppendLine(string.Join(";", new[]
                    {
                        i.Group, i.Material, i.Thickness, i.Dimension, Numbers.Format(i.Packages), Numbers.Format(i.Sheets),
                        // Eulanda-Nummern mit führenden Nullen als Text exportieren
                        i.ErpNumber.Length > 0 ? "=\"" + i.ErpNumber + "\"" : "",
                        Numbers.Format(i.WeightPerSheet), i.Note, Numbers.Format(i.MinPackages), Numbers.Format(i.MinSheets), status,
                        i.OpenOrder, i.ChangedAt?.ToString("dd.MM.yyyy HH:mm") ?? "", i.ChangedBy,
                    }.Select((v, idx) => idx == 6 ? v : Csv.Escape(v))));
                }
                try
                {
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                    if (MessageBox.Show(FindForm(), "Exportiert nach\n" + dlg.FileName + "\n\nJetzt öffnen?", "Export",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Open(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Export fehlgeschlagen:\n" + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenHistory()
        {
            string path = host.Store?.LogPath;
            if (path == null || !File.Exists(path))
            {
                MessageBox.Show(FindForm(), "Es wurden noch keine Änderungen protokolliert.\n\nDer Verlauf wird hier gespeichert:\n" + path,
                    "Verlauf", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Open(path);
        }

        private void Open(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Die Datei konnte nicht geöffnet werden:\n" + path + "\n\n" + ex.Message, "Blechbestand",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
