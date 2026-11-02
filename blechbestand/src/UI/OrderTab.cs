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
    /// Reiter „Bestellung / Einkauf“: Materialien anhaken, Menge eingeben und als E-Mail an den Einkauf
    /// in Outlook öffnen (oder in die Zwischenablage kopieren).
    /// </summary>
    internal sealed class OrderTab : UserControl
    {
        private const int ColCheck = 0, ColMaterial = 1, ColThickness = 2, ColNote = 3, ColStock = 4, ColMin = 5,
            ColQuantity = 6, ColUnit = 7, ColErp = 8, ColOrder = 9;

        private const string AllGroups = "Alle Materialgruppen";

        private sealed class Entry
        {
            public bool Selected;
            public decimal? Quantity;
            public string Unit;
        }

        private readonly IAppHost host;
        private readonly Dictionary<Guid, Entry> entries = new Dictionary<Guid, Entry>();
        private readonly Dictionary<string, int> groupCounts = new Dictionary<string, int>();
        private readonly DataGridView grid;
        private readonly TextBox search;
        private readonly ComboBox groupFilter;
        private readonly CheckBox onlyLow;
        private readonly CheckBox onlySelected;
        private readonly TextBox to;
        private readonly TextBox cc;
        private readonly TextBox subject;
        private readonly TextBox intro;
        private readonly TextBox outro;
        private Control resultPanel;
        private Label resultLabel;
        private List<OrderLine> exportedLines;
        private readonly Label summary;
        private List<object> rowKeys = new List<object>();
        private bool updating;
        private bool fillingFilters;

        public OrderTab(IAppHost host)
        {
            this.host = host;
            BackColor = Theme.Window;
            Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(8));

            // ---------- Werkzeugleiste ----------
            search = new TextBox { Width = Theme.S(190), Margin = new Padding(Theme.S(3), Theme.S(5), Theme.S(3), Theme.S(3)) };
            Theme.SetCue(search, "Werkstoff, Dicke …");
            search.TextChanged += (s, e) => Rebuild();
            groupFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(200), Margin = new Padding(Theme.S(12), Theme.S(5), Theme.S(3), Theme.S(3)) };
            groupFilter.SelectedIndexChanged += (s, e) => { if (!fillingFilters) Rebuild(); };
            onlyLow = new CheckBox { Text = "Nur knappe", AutoSize = true, Margin = new Padding(Theme.S(12), Theme.S(8), Theme.S(3), Theme.S(3)) };
            onlyLow.CheckedChanged += (s, e) => Rebuild();
            onlySelected = new CheckBox { Text = "Nur ausgewählte", AutoSize = true, Margin = new Padding(Theme.S(8), Theme.S(8), Theme.S(3), Theme.S(3)) };
            onlySelected.CheckedChanged += (s, e) => Rebuild();
            var suggest = Theme.SecondaryButton("Knappe Positionen vorschlagen");
            suggest.Click += (s, e) => SuggestLow();
            var clear = Theme.SecondaryButton("Auswahl leeren");
            clear.Click += (s, e) => ClearSelection();

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, Theme.S(6)) };
            toolbar.Controls.Add(Theme.FlowLabel("Suchen", search.PreferredHeight));
            toolbar.Controls.Add(search);
            toolbar.Controls.Add(groupFilter);
            toolbar.Controls.Add(onlyLow);
            toolbar.Controls.Add(onlySelected);
            toolbar.Controls.Add(suggest);
            toolbar.Controls.Add(clear);

            // ---------- Tabelle ----------
            grid = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(grid, host.Settings.FontSize);
            // Häkchen werden per Klick/Leertaste selbst umgeschaltet (zuverlässiger als der Bearbeitungsmodus)
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Bestellen", FillWeight = 62, MinimumWidth = Theme.S(60), SortMode = DataGridViewColumnSortMode.NotSortable, ReadOnly = true });
            AddText("Material", 100, false);
            AddText("Dicke T\n[mm]", 60, false, right: true);
            AddText("Bemerkung", 85, false);
            AddText("Bestand", 130, false, right: true);
            AddText("Mindest-\nbestand", 80, false, right: true, muted: true);
            AddText("Bestell-\nmenge", 72, true, right: true);
            var unit = new DataGridViewComboBoxColumn
            {
                HeaderText = "Einheit",
                FillWeight = 80,
                MinimumWidth = Theme.S(76),
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DisplayStyleForCurrentCellOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
            unit.Items.AddRange(Units.Sheets, Units.Packages);
            grid.Columns.Add(unit);
            AddText("Eulanda-\nnummer", 80, false);
            AddText("Offene Bestellung", 140, false);
            grid.Columns[ColQuantity].DefaultCellStyle.Font = Theme.UiFont(host.Settings.FontSize + 0.5f, FontStyle.Bold);
            grid.Columns[ColQuantity].HeaderCell.Style.BackColor = Theme.AccentDark;
            grid.Columns[ColCheck].HeaderCell.Style.BackColor = Theme.AccentDark;
            grid.Columns[ColUnit].HeaderCell.Style.BackColor = Theme.AccentDark;

            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // Einheit sofort übernehmen, nicht erst beim Verlassen der Zelle
                if (grid.IsCurrentCellDirty && grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == ColUnit)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellBeginEdit += (s, e) =>
            {
                if (!(KeyAt(e.RowIndex) is Guid) || (e.ColumnIndex != ColQuantity && e.ColumnIndex != ColUnit))
                    e.Cancel = true;
            };
            grid.CellValidating += Grid_CellValidating;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CellEndEdit += (s, e) => grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "";
            grid.EditingControlShowing += (s, e) =>
            {
                if (e.Control is TextBox tb) { tb.TextAlign = HorizontalAlignment.Right; tb.SelectAll(); }
            };
            grid.DataError += (s, e) => { e.ThrowException = false; };
            grid.RowPostPaint += (s, e) =>
            {
                if (KeyAt(e.RowIndex) is string g) Theme.PaintGroupRow(grid, e, g, groupCounts.TryGetValue(g, out int n) ? n : 0);
            };
            grid.CellMouseClick += (s, e) =>
            {
                // Linksklick irgendwo auf eine Zeile außer Menge/Einheit setzt oder entfernt das Häkchen
                if (e.Button != MouseButtons.Left || e.RowIndex < 0 || !(KeyAt(e.RowIndex) is Guid)) return;
                if (e.ColumnIndex == ColQuantity || e.ColumnIndex == ColUnit) return;
                ToggleRow(e.RowIndex);
            };
            // Rechtsklick: Lieferung buchen (vor allem auf „Offene Bestellung“)
            var menu = new ContextMenuStrip { Font = Theme.UiFont(10f) };
            var delivered = new ToolStripMenuItem("Geliefert – Menge zum Bestand addieren …", null, (s, e) => BookDelivery())
            {
                Font = Theme.UiFont(10f, FontStyle.Bold),
            };
            var clearNote = new ToolStripMenuItem("Bestellvermerk entfernen (ohne Mengenänderung)", null, (s, e) => ClearOpenOrder());
            menu.Items.Add(delivered);
            menu.Items.Add(clearNote);
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || !(KeyAt(e.RowIndex) is Guid id)) return;
                EndEditing();
                try { grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)]; } catch (InvalidOperationException) { return; }
                SheetItem it = host.Inventory.Find(id);
                bool hasOrder = it != null && it.OpenOrder.Length > 0;
                delivered.Enabled = hasOrder;
                clearNote.Enabled = hasOrder;
                List<OrderNoteEntry> notes = hasOrder ? OrderNotes.Parse(it.OpenOrder) : new List<OrderNoteEntry>();
                delivered.Text = "Geliefert – Menge zum Bestand addieren" +
                    (notes.Count == 1 && notes[0].Quantity.HasValue ? " (" + Units.Format(notes[0].Quantity.Value, notes[0].Unit) + ")" : "") + " …";
                menu.Show(grid, grid.PointToClient(Cursor.Position));
            };
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && grid.CurrentCell != null && KeyAt(grid.CurrentCell.RowIndex) is Guid
                    && grid.CurrentCell.ColumnIndex != ColQuantity && grid.CurrentCell.ColumnIndex != ColUnit)
                {
                    ToggleRow(grid.CurrentCell.RowIndex);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            var gridFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
            gridFrame.Controls.Add(grid);

            // ---------- E-Mail an den Einkauf ----------
            Control mailPanel = BuildMailPanel(out to, out cc, out subject, out intro, out outro, out summary);

            // Hinweisbalken nach dem Export: erst nach Bestätigung „bestellt“ vermerken und Auswahl leeren
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(0xE2, 0xF0, 0xD9),
                Padding = new Padding(Theme.S(12), Theme.S(6), Theme.S(8), Theme.S(6)),
                Margin = new Padding(0),
                Visible = false,
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.Paint += (s, e) =>
            {
                using (var p = new Pen(Color.FromArgb(0x8F, 0xC1, 0x6F))) e.Graphics.DrawRectangle(p, 0, 0, bar.Width - 1, bar.Height - 1);
            };
            resultLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.FromArgb(0x1B, 0x4D, 0x1F),
                Font = Theme.UiFont(10f),
                Margin = new Padding(0, Theme.S(4), Theme.S(12), Theme.S(4)),
            };
            var confirm = Theme.PrimaryButton("Als bestellt vermerken");
            confirm.Click += (s, e) => ConfirmOrdered();
            var keep = Theme.SecondaryButton("Nicht vermerken");
            keep.Click += (s, e) => HideResult();
            var barButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            barButtons.Controls.Add(confirm);
            barButtons.Controls.Add(keep);
            bar.Controls.Add(resultLabel, 0, 0);
            bar.Controls.Add(barButtons, 1, 0);
            resultPanel = bar;
            var barGap = new Panel { Dock = DockStyle.Top, Height = Theme.S(6), Visible = false };
            bar.VisibleChanged += (s, e) => barGap.Visible = bar.Visible;

            var split = new Panel { Dock = DockStyle.Fill };
            split.Controls.Add(gridFrame);
            split.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.S(12) });
            split.Controls.Add(mailPanel);
            gridFrame.BringToFront();

            Controls.Add(split);
            Controls.Add(barGap);
            Controls.Add(bar);
            Controls.Add(toolbar);
            split.BringToFront();

            host.DataChanged += (s, e) => { FillFilters(); Rebuild(); };
            FillFilters();
            UpdateSummary();
        }

        public bool IsEditing => grid.IsCurrentCellInEditMode;

        private void ToggleRow(int rowIndex)
        {
            DataGridViewCell cell = grid.Rows[rowIndex].Cells[ColCheck];
            cell.Value = !(cell.Value is bool b && b); // löst CellValueChanged aus
        }

        private SheetItem CurrentItem() =>
            grid.CurrentCell != null && KeyAt(grid.CurrentCell.RowIndex) is Guid id ? host.Inventory.Find(id) : null;

        private void BookDelivery()
        {
            SheetItem it = CurrentItem();
            if (it == null || it.OpenOrder.Length == 0) return;
            DeliveryDialog.Book(FindForm(), host, it.Id);
        }

        private void ClearOpenOrder()
        {
            SheetItem it = CurrentItem();
            if (it == null || it.OpenOrder.Length == 0) return;
            Guid id = it.Id;
            host.Modify((inv, log) =>
            {
                SheetItem x = inv.Find(id) ?? throw new ItemGoneException();
                log.Add(x.DisplayName, "Offene Bestellung", x.OpenOrder, "entfernt");
                x.OpenOrder = "";
                x.ChangedAt = DateTime.Now;
                x.ChangedBy = host.Store.UserName;
            });
        }

        public void EndEditing()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
        }

        public void FocusSearch()
        {
            search.Focus();
            search.SelectAll();
        }

        public void ApplyFontSize(float size)
        {
            Theme.ApplyGridFont(grid, size);
            grid.Columns[ColQuantity].DefaultCellStyle.Font = Theme.UiFont(size + 0.5f, FontStyle.Bold);
            grid.Invalidate();
        }

        private void AddText(string header, int width, bool editable, bool right = false, bool muted = false)
        {
            var c = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = width,
                MinimumWidth = Theme.S(Math.Min(width, 60)),
                ReadOnly = !editable,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
            if (right) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (muted) c.DefaultCellStyle.ForeColor = Theme.Muted;
            grid.Columns.Add(c);
        }

        private Control BuildMailPanel(out TextBox toBox, out TextBox ccBox, out TextBox subjectBox, out TextBox introBox,
            out TextBox outroBox, out Label summaryLabel)
        {
            AppSettings st = host.Settings;
            int width = Theme.S(350);
            var card = new Panel { Dock = DockStyle.Right, Width = width, BackColor = Color.White, Padding = new Padding(1) };
            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoScroll = true,
                BackColor = Color.White,
                Padding = new Padding(Theme.S(14), Theme.S(12), Theme.S(14), Theme.S(12)),
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.Paint += (s, e) =>
            {
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            int fieldWidth = width - Theme.S(34);
            Label Caption(string text) => new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Margin = new Padding(0, Theme.S(8), 0, Theme.S(2)),
                Font = Theme.UiFont(9.5f),
            };
            TextBox Box(string value, bool multi, int lines)
            {
                var b = new TextBox
                {
                    Text = value ?? "",
                    Width = fieldWidth,
                    Multiline = multi,
                    AcceptsReturn = multi,
                    ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None,
                    Margin = new Padding(0, 0, 0, Theme.S(2)),
                    Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                };
                if (multi) b.Height = TextRenderer.MeasureText("Ag", b.Font).Height * lines + Theme.S(14);
                return b;
            }

            var title = new Label
            {
                Text = "E-Mail an den Einkauf",
                AutoSize = true,
                Font = Theme.UiFont(13f, FontStyle.Bold),
                ForeColor = Theme.AccentDark,
                Margin = new Padding(0, 0, 0, Theme.S(4)),
            };
            inner.Controls.Add(title);
            inner.Controls.Add(Caption("An"));
            toBox = Box(st.OrderTo, false, 1);
            Theme.SetCue(toBox, "einkauf@firma.de");
            inner.Controls.Add(toBox);
            inner.Controls.Add(Caption("Cc (optional)"));
            ccBox = Box(st.OrderCc, false, 1);
            inner.Controls.Add(ccBox);
            inner.Controls.Add(Caption("Betreff"));
            subjectBox = Box(OrderMail.Subject(st.OrderSubject, DateTime.Now), false, 1);
            inner.Controls.Add(subjectBox);
            inner.Controls.Add(Caption("Text vor der Liste"));
            introBox = Box(st.OrderIntro, true, 3);
            inner.Controls.Add(introBox);
            inner.Controls.Add(Caption("Text nach der Liste"));
            outroBox = Box(st.OrderOutro, true, 2);
            inner.Controls.Add(outroBox);

            summaryLabel = new Label
            {
                AutoSize = true,
                Font = Theme.UiFont(10.5f, FontStyle.Bold),
                Margin = new Padding(0, Theme.S(8), 0, Theme.S(8)),
                MaximumSize = new Size(fieldWidth, 0),
            };
            inner.Controls.Add(summaryLabel);

            var outlook = Theme.PrimaryButton("In Outlook öffnen");
            outlook.Font = Theme.UiFont(11f, FontStyle.Bold);
            outlook.AutoSize = false;
            outlook.Size = new Size(fieldWidth, Theme.S(42));
            outlook.Click += (s, e) => ExportOutlook();
            inner.Controls.Add(outlook);

            var copy = Theme.SecondaryButton("In Zwischenablage kopieren");
            copy.AutoSize = false;
            copy.Size = new Size(fieldWidth, Theme.S(34));
            copy.Margin = new Padding(0, Theme.S(6), 0, 0);
            copy.Click += (s, e) => ExportClipboard();
            inner.Controls.Add(copy);

            inner.Controls.Add(new Label
            {
                Text = "Öffnet eine neue E-Mail mit der Bestellliste als Tabelle. Gesendet wird erst, wenn du in Outlook auf „Senden“ klickst. " +
                       "„bestellt“ wird erst vermerkt, wenn du es danach bestätigst.",
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(9f),
                MaximumSize = new Size(fieldWidth, 0),
                Margin = new Padding(0, Theme.S(10), 0, 0),
            });

            card.Controls.Add(inner);
            return card;
        }

        // =====================================================================
        // Anzeige
        // =====================================================================

        private object KeyAt(int rowIndex) => rowIndex >= 0 && rowIndex < grid.Rows.Count ? grid.Rows[rowIndex].Tag : null;

        private Entry EntryFor(SheetItem item)
        {
            if (!entries.TryGetValue(item.Id, out Entry e))
            {
                e = new Entry { Unit = item.PreferredUnit };
                entries[item.Id] = e;
            }
            return e;
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
                foreach (string x in host.Inventory.UsedGroups()) groupFilter.Items.Add(x);
                groupFilter.SelectedItem = g != null && groupFilter.Items.Contains(g) ? g : AllGroups;
                groupFilter.EndUpdate();
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
            if (onlyLow.Checked) items = items.Where(i => i.IsLow);
            if (onlySelected.Checked) items = items.Where(i => entries.TryGetValue(i.Id, out Entry e) && e.Selected);
            string q = search.Text.Trim();
            if (q.Length > 0)
            {
                string[] words = q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                items = items.Where(i =>
                {
                    string hay = (i.Group + " " + i.Material + " " + i.Thickness + " " + i.Thickness + "mm " + i.Dimension + " " + i.ErpNumber + " " + i.Note).ToLowerInvariant();
                    return words.All(w => hay.Contains(w));
                });
            }
            return items.ToList();
        }

        private void Rebuild()
        {
            if (grid.IsCurrentCellInEditMode) return;

            // Einträge zu gelöschten Positionen entfernen
            foreach (Guid gone in entries.Keys.Where(id => host.Inventory.Find(id) == null).ToList()) entries.Remove(gone);

            List<SheetItem> items = VisibleItems();
            var keys = new List<object>();
            groupCounts.Clear();
            string lastGroup = null;
            foreach (SheetItem i in items)
            {
                if (!string.Equals(i.Group, lastGroup, StringComparison.CurrentCultureIgnoreCase)) { keys.Add(i.Group); lastGroup = i.Group; }
                groupCounts[i.Group] = (groupCounts.TryGetValue(i.Group, out int n) ? n : 0) + 1;
                keys.Add(i.Id);
            }
            Dictionary<Guid, SheetItem> byId = items.ToDictionary(i => i.Id);

            updating = true;
            try
            {
                if (keys.SequenceEqual(rowKeys))
                {
                    int stripe = 0;
                    for (int r = 0; r < grid.Rows.Count; r++)
                        if (rowKeys[r] is Guid id) FillRow(grid.Rows[r], byId[id], stripe++);
                    grid.Invalidate();
                }
                else
                {
                    object selectedKey = grid.CurrentCell != null ? KeyAt(grid.CurrentCell.RowIndex) : null;
                    int selectedColumn = grid.CurrentCell?.ColumnIndex ?? ColQuantity;
                    int firstRow = grid.FirstDisplayedScrollingRowIndex;
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
                            // Häkchen und Auswahlliste der Gruppenzeile werden von PaintGroupRow übermalt
                            row.Cells[ColCheck].Value = false;
                            row.Cells[ColUnit].Value = null;
                            row.ReadOnly = true;
                            row.DefaultCellStyle.BackColor = Theme.GroupBack;
                            row.DefaultCellStyle.SelectionBackColor = Theme.GroupBack;
                        }
                        else
                        {
                            FillRow(row, byId[(Guid)k], stripe++);
                        }
                        rows.Add(row);
                    }
                    grid.Rows.AddRange(rows.ToArray());
                    rowKeys = keys;
                    int target = selectedKey != null ? keys.FindIndex(x => x.Equals(selectedKey)) : -1;
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
                        int first = keys.FindIndex(x => x is Guid);
                        try { grid.CurrentCell = first >= 0 ? grid.Rows[first].Cells[ColMaterial] : null; } catch (InvalidOperationException) { }
                    }
                }
            }
            finally
            {
                updating = false;
            }
            UpdateSummary();
        }

        private void FillRow(DataGridViewRow row, SheetItem i, int stripe)
        {
            Entry e = entries.TryGetValue(i.Id, out Entry x) ? x : null;
            row.Tag = i.Id;
            Color back = e != null && e.Selected ? Color.FromArgb(0xE2, 0xF0, 0xD9) : stripe % 2 == 1 ? Theme.Stripe : Color.White;
            row.DefaultCellStyle.BackColor = back;
            row.Cells[ColCheck].Value = e != null && e.Selected;
            row.Cells[ColMaterial].Value = i.Material;
            row.Cells[ColThickness].Value = i.Thickness;
            row.Cells[ColNote].Value = i.Note;
            row.Cells[ColStock].Value = i.StockText;
            row.Cells[ColMin].Value = i.MinimumText;
            row.Cells[ColQuantity].Value = e?.Quantity.HasValue == true ? Numbers.Format(e.Quantity) : "";
            row.Cells[ColUnit].Value = e?.Unit ?? i.PreferredUnit;
            row.Cells[ColErp].Value = i.ErpNumber;
            row.Cells[ColOrder].Value = i.OpenOrder;
            Theme.LevelColors(i.Level, out Color lb, out Color lf);
            row.Cells[ColStock].Style.BackColor = lb;
            row.Cells[ColStock].Style.ForeColor = lf;
            row.Cells[ColStock].Style.SelectionBackColor = Theme.Shade(lb);
            row.Cells[ColStock].ToolTipText = "Bestand: " + i.StockText + (i.MinimumText.Length > 0 ? "\nMindestbestand: " + i.MinimumText : "");
            row.Cells[ColStock].Style.SelectionForeColor = lf;
            row.Cells[ColOrder].Style.ForeColor = i.OpenOrder.Length > 0 ? Theme.AccentDark : Color.Empty;
            row.Cells[ColOrder].ToolTipText = i.OpenOrder.Length > 0
                ? "Bereits bestellt: " + i.OpenOrder + "\nRechtsklick → „Geliefert – Menge zum Bestand addieren“"
                : "";
        }

        private void UpdateSummary()
        {
            List<OrderLine> lines = CollectLines(out int missing);
            if (lines.Count == 0 && missing == 0)
            {
                summary.Text = "Noch nichts ausgewählt.\nMaterial links anhaken und Menge eintragen.";
                summary.ForeColor = Theme.Muted;
                return;
            }
            string text = lines.Count == 1 ? "1 Position" : lines.Count + " Positionen";
            decimal kg = OrderMail.TotalWeight(lines);
            if (kg > 0) text += " · ca. " + Numbers.FormatWeight(kg);
            if (missing > 0) text += "\n" + missing + " ausgewählt ohne Menge";
            summary.Text = text;
            summary.ForeColor = missing > 0 ? Theme.RedFore : Theme.AccentDark;
        }

        // =====================================================================
        // Auswahl und Mengen
        // =====================================================================

        private void Grid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex != ColQuantity || !grid.IsCurrentCellInEditMode) return;
            if (!Numbers.TryParseQuantity(Convert.ToString(e.FormattedValue), out _))
            {
                e.Cancel = true;
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "Bitte eine Zahl eingeben, z. B. 10 oder 1,5. Esc bricht ab.";
                System.Media.SystemSounds.Beep.Play();
            }
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (updating || e.RowIndex < 0 || !(KeyAt(e.RowIndex) is Guid id)) return;
            SheetItem item = host.Inventory.Find(id);
            if (item == null) return;
            Entry entry = EntryFor(item);
            DataGridViewRow row = grid.Rows[e.RowIndex];
            updating = true;
            try
            {
                if (e.ColumnIndex == ColCheck)
                {
                    entry.Selected = row.Cells[ColCheck].Value is bool b && b;
                    if (!entry.Selected) entry.Quantity = null;
                    if (entry.Selected && (!entry.Quantity.HasValue || entry.Quantity.Value <= 0))
                    {
                        entry.Unit = entry.Unit ?? item.PreferredUnit;
                        entry.Quantity = OrderMail.SuggestQuantity(item, entry.Unit);
                    }
                    if (entry.Selected && item.OpenOrder.Length > 0)
                    {
                        // Hinweis, nicht blockieren: steht schon in der Spalte „Offene Bestellung“
                        row.Cells[ColOrder].ToolTipText = "Achtung, schon bestellt: " + item.OpenOrder;
                    }
                }
                else if (e.ColumnIndex == ColQuantity)
                {
                    entry.Quantity = Numbers.ParseQuantityOrNull(Convert.ToString(row.Cells[ColQuantity].Value));
                    entry.Selected = entry.Quantity.HasValue && entry.Quantity.Value > 0;
                }
                else if (e.ColumnIndex == ColUnit)
                {
                    string newUnit = Convert.ToString(row.Cells[ColUnit].Value);
                    if (newUnit != entry.Unit)
                    {
                        entry.Unit = newUnit;
                        if (entry.Selected) entry.Quantity = OrderMail.SuggestQuantity(item, newUnit);
                    }
                }
                int stripe = 0;
                for (int r = 0; r < e.RowIndex; r++) if (rowKeys[r] is Guid) stripe++;
                FillRow(row, item, stripe);
            }
            finally
            {
                updating = false;
            }
            UpdateSummary();
        }

        private void SuggestLow()
        {
            EndEditing();
            int added = 0, skipped = 0;
            foreach (SheetItem i in host.Inventory.Items.Where(x => x.IsLow))
            {
                Entry e = EntryFor(i);
                if (e.Selected) continue;
                if (i.OpenOrder.Length > 0) { skipped++; continue; }
                e.Selected = true;
                e.Unit = i.PreferredUnit;
                e.Quantity = OrderMail.SuggestQuantity(i, e.Unit);
                added++;
            }
            rowKeys = new List<object>(); // vollständig neu zeichnen
            Rebuild();
            string msg = added == 0 ? "Keine weiteren knappen Positionen gefunden." : added + " knappe Position(en) mit Mengenvorschlag ausgewählt. Bitte Mengen prüfen.";
            if (skipped > 0) msg += "\n\n" + skipped + " knappe Position(en) übersprungen, weil dafür schon eine Bestellung vermerkt ist.";
            MessageBox.Show(FindForm(), msg, "Knappe Positionen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearSelection()
        {
            EndEditing();
            entries.Clear();
            rowKeys = new List<object>();
            Rebuild();
        }

        /// <summary>Hakt eine Position an (aus dem Reiter „Bestand“) und zeigt sie.</summary>
        public void Select(Guid id)
        {
            SheetItem item = host.Inventory.Find(id);
            if (item == null) return;
            Entry e = EntryFor(item);
            if (!e.Selected)
            {
                e.Selected = true;
                e.Unit = e.Unit ?? item.PreferredUnit;
                if (!e.Quantity.HasValue || e.Quantity <= 0) e.Quantity = OrderMail.SuggestQuantity(item, e.Unit);
            }
            search.Text = "";
            onlySelected.Checked = false;
            if (onlyLow.Checked && !item.IsLow) onlyLow.Checked = false;
            groupFilter.SelectedItem = AllGroups;
            rowKeys = new List<object>();
            Rebuild();
            int idx = rowKeys.FindIndex(k => k is Guid g && g == id);
            if (idx >= 0)
            {
                grid.CurrentCell = grid.Rows[idx].Cells[ColQuantity];
                grid.Focus();
            }
        }

        private List<OrderLine> CollectLines(out int missingQuantity)
        {
            missingQuantity = 0;
            var lines = new List<OrderLine>();
            foreach (SheetItem i in host.Inventory.Sorted())
            {
                if (!entries.TryGetValue(i.Id, out Entry e) || !e.Selected) continue;
                if (!e.Quantity.HasValue || e.Quantity.Value <= 0) { missingQuantity++; continue; }
                lines.Add(new OrderLine
                {
                    Item = i,
                    Quantity = e.Quantity.Value,
                    Unit = e.Unit ?? i.PreferredUnit,
                    SheetsPerPackage = i.PackageSheets(host.Inventory.PackageWeight),
                });
            }
            return lines;
        }

        // =====================================================================
        // Export
        // =====================================================================

        public void StoreMailSettings()
        {
            AppSettings st = host.Settings;
            st.OrderTo = to.Text.Trim();
            st.OrderCc = cc.Text.Trim();
            st.OrderIntro = intro.Text;
            st.OrderOutro = outro.Text;
        }

        private List<OrderLine> PrepareExport()
        {
            EndEditing();
            List<OrderLine> lines = CollectLines(out int missing);
            if (lines.Count == 0)
            {
                MessageBox.Show(FindForm(), "Bitte zuerst links Material anhaken und eine Bestellmenge eintragen.", "Bestellung",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            if (missing > 0 && MessageBox.Show(FindForm(),
                    missing + " ausgewählte Position(en) haben keine Menge und werden nicht mitbestellt.\n\nTrotzdem fortfahren?",
                    "Bestellung", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return null;
            StoreMailSettings();
            host.Settings.Save();
            return lines;
        }

        private void ExportOutlook()
        {
            List<OrderLine> lines = PrepareExport();
            if (lines == null) return;
            string html = OrderMail.BuildHtml(lines, intro.Text, outro.Text);
            string subj = subject.Text.Trim().Length > 0 ? subject.Text.Trim() : OrderMail.Subject(host.Settings.OrderSubject, DateTime.Now);
            HideResult();

            Cursor.Current = Cursors.WaitCursor;
            bool ok = OutlookExporter.TryCreateDraft(to.Text.Trim(), cc.Text.Trim(), subj, html, out string error);
            Cursor.Current = Cursors.Default;
            if (ok)
            {
                ShowResult("✓ E-Mail in Outlook geöffnet (" + DateTime.Now.ToString("HH:mm") + ", " + Count(lines) + "). Dort prüfen, senden und hier bestätigen.\n" +
                           "Keine E-Mail zu sehen? Dann „Nicht vermerken“ und rechts „In Zwischenablage kopieren“ benutzen.", lines);
                return;
            }

            // Kein klassisches Outlook (z. B. neues Outlook): neue Mail im Standard-Mailprogramm, Liste per Strg+V einfügen
            if (!CopyToClipboard(lines)) return;
            MessageBox.Show(FindForm(),
                "Das klassische Outlook lässt sich auf diesem Rechner nicht ansteuern" + (string.IsNullOrEmpty(error) ? "." : ":\n" + error) +
                "\n\nStattdessen öffnet sich jetzt eine neue E-Mail in deinem Standard-Mailprogramm (z. B. im neuen Outlook) mit Empfänger und Betreff." +
                "\n\nDie Bestellliste ist schon in der Zwischenablage:\nin die E-Mail klicken und Strg+V drücken.",
                "Bestellung", MessageBoxButtons.OK, MessageBoxIcon.Information);
            bool opened;
            string mailError = null;
            try
            {
                Process.Start(new ProcessStartInfo(OrderMail.BuildMailto(to.Text, cc.Text, subj)) { UseShellExecute = true });
                opened = true;
            }
            catch (Exception ex)
            {
                opened = false;
                mailError = ex.Message;
            }
            if (!opened)
            {
                MessageBox.Show(FindForm(),
                    "Es ließ sich kein Mailprogramm öffnen" + (string.IsNullOrEmpty(mailError) ? "." : ":\n" + mailError) +
                    "\n\nDie Bestellliste ist in der Zwischenablage: eine neue E-Mail öffnen und mit Strg+V einfügen.",
                    "Bestellung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ShowResult("✓ " + (opened ? "Neue E-Mail geöffnet" : "Bestellliste kopiert") + " (" + DateTime.Now.ToString("HH:mm") + ", " + Count(lines) + ").\n" +
                       "Die Liste ist in der Zwischenablage: in der E-Mail Strg+V drücken, senden und hier bestätigen.", lines);
        }

        private void ExportClipboard()
        {
            List<OrderLine> lines = PrepareExport();
            if (lines == null) return;
            HideResult();
            if (!CopyToClipboard(lines)) return;
            ShowResult("✓ Bestellliste in der Zwischenablage (" + DateTime.Now.ToString("HH:mm") + ", " + Count(lines) + ").\n" +
                       "In einer neuen E-Mail mit Strg+V einfügen, senden und hier bestätigen.", lines);
        }

        private static string Count(List<OrderLine> lines) => lines.Count == 1 ? "1 Position" : lines.Count + " Positionen";

        /// <summary>Legt die komplette Mail (Text, Tabelle, Schluss) als HTML und als Text in die Zwischenablage.</summary>
        private bool CopyToClipboard(List<OrderLine> lines)
        {
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.Html, new MemoryStream(OrderMail.BuildClipboardHtml(OrderMail.BuildHtml(lines, intro.Text, outro.Text))));
                data.SetData(DataFormats.UnicodeText, OrderMail.BuildText(lines, intro.Text, outro.Text));
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Kopieren in die Zwischenablage fehlgeschlagen:\n" + ex.Message, "Bestellung",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>Zeigt die Rückmeldung nach dem Export. Die Auswahl bleibt, bis „Als bestellt vermerken“ bestätigt wird.</summary>
        private void ShowResult(string text, List<OrderLine> lines)
        {
            exportedLines = lines;
            resultLabel.Text = text;
            resultPanel.Visible = true;
        }

        private void HideResult()
        {
            exportedLines = null;
            resultPanel.Visible = false;
        }

        /// <summary>Vermerkt die exportierten Positionen als bestellt und nimmt sie aus der Auswahl.</summary>
        private void ConfirmOrdered()
        {
            List<OrderLine> lines = exportedLines;
            if (lines == null) { HideResult(); return; }
            DateTime today = DateTime.Now;
            var notes = lines.ToDictionary(l => l.Item.Id, l => OrderMail.OrderNote(l, today));
            bool saved = host.Modify((inv, log) =>
            {
                foreach (KeyValuePair<Guid, string> n in notes)
                {
                    SheetItem it = inv.Find(n.Key);
                    if (it == null) continue;
                    string before = it.OpenOrder;
                    it.OpenOrder = before.Length > 0 ? before + "; " + n.Value : n.Value;
                    log.Add(it.DisplayName, "Offene Bestellung", before, it.OpenOrder);
                }
            });
            if (!saved) return; // Fehlermeldung kam schon; Auswahl und Rückmeldung bleiben
            HideResult();
            foreach (OrderLine l in lines) entries.Remove(l.Item.Id);
            rowKeys = new List<object>();
            Rebuild();
        }
    }
}
