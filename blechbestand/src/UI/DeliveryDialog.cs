using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>
    /// „Geliefert – Menge zum Bestand addieren“: bestellte Menge (änderbar, z. B. bei Teillieferung) auf den
    /// Bestand rechnen und den Bestellvermerk entfernen oder auf den Rest verkleinern.
    /// </summary>
    internal sealed class DeliveryDialog : Form
    {
        private readonly SheetItem item;
        private readonly List<OrderNoteEntry> entries;
        private readonly ComboBox orderChoice;
        private readonly TextBox quantity;
        private readonly ComboBox unit;
        private readonly CheckBox complete;
        private readonly Label preview;
        private readonly ErrorProvider errors = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };
        private bool completeTouched;
        private bool updating;
        private int selectedEntry = -1;

        /// <summary>Der gewählte Eintrag im Feld „Offene Bestellung“ (unverändert, zum Wiederfinden beim Speichern).</summary>
        public string EntryRaw { get; private set; }
        public decimal Delivered { get; private set; }
        public string DeliveredUnit { get; private set; }
        public bool Complete { get; private set; }

        public DeliveryDialog(SheetItem item)
        {
            this.item = item;
            entries = OrderNotes.Parse(item.OpenOrder);

            Text = "Lieferung eingetroffen";
            Icon = Assets.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = Theme.UiFont(10f);
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), Theme.S(12));

            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = item.DisplayName + "  (" + item.Group + ")",
                AutoSize = true,
                Font = Theme.UiFont(11.5f, FontStyle.Bold),
                ForeColor = Theme.AccentDark,
                Margin = new Padding(0, 0, 0, Theme.S(10)),
            };
            table.Controls.Add(title);
            table.SetColumnSpan(title, 2);

            orderChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(340) };
            foreach (OrderNoteEntry e in entries) orderChoice.Items.Add(e.Raw);
            if (entries.Count > 1)
            {
                Row(table, "Bestellung", orderChoice);
            }
            else
            {
                Row(table, "Bestellung", new Label { Text = entries.Count == 1 ? entries[0].Raw : "–", AutoSize = true, MaximumSize = new Size(Theme.S(360), 0) });
            }
            Row(table, "Bestand jetzt", new Label { Text = item.StockText, AutoSize = true });

            quantity = new TextBox { Width = Theme.S(80), TextAlign = HorizontalAlignment.Right, Margin = new Padding(Theme.S(3)) };
            unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(100), Margin = new Padding(Theme.S(3)) };
            unit.Items.AddRange(new object[] { Units.Sheets, Units.Packages });
            var qtyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            qtyRow.Controls.Add(quantity);
            qtyRow.Controls.Add(unit);
            Row(table, "Geliefert", qtyRow);

            complete = new CheckBox
            {
                Text = "Bestellung vollständig geliefert (Vermerk entfernen)",
                AutoSize = true,
                Margin = new Padding(Theme.S(3), Theme.S(8), Theme.S(3), Theme.S(4)),
            };
            table.Controls.Add(new Label { AutoSize = true });
            table.Controls.Add(complete);

            preview = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Muted,
                MaximumSize = new Size(Theme.S(380), 0),
                Margin = new Padding(Theme.S(3), Theme.S(4), Theme.S(3), Theme.S(4)),
            };
            table.Controls.Add(new Label { AutoSize = true });
            table.Controls.Add(preview);

            var ok = Theme.PrimaryButton("Zum Bestand addieren");
            ok.Click += (s, e) => Accept();
            var cancel = Theme.SecondaryButton("Abbrechen");
            cancel.DialogResult = DialogResult.Cancel;
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(14), 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            table.Controls.Add(buttons);
            table.SetColumnSpan(buttons, 2);

            Controls.Add(table);
            AcceptButton = ok;
            CancelButton = cancel;

            orderChoice.SelectedIndexChanged += (s, e) => { selectedEntry = orderChoice.SelectedIndex; LoadEntry(); };
            quantity.TextChanged += (s, e) => UpdatePreview(true);
            unit.SelectedIndexChanged += (s, e) => UpdatePreview(true);
            complete.CheckedChanged += (s, e) => { if (!updating) completeTouched = true; UpdatePreview(false); };

            selectedEntry = entries.Count > 0 ? 0 : -1;
            if (entries.Count > 1) orderChoice.SelectedIndex = 0; // lädt den Eintrag
            else LoadEntry();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            quantity.Focus();
            quantity.SelectAll();
        }

        private OrderNoteEntry CurrentEntry =>
            selectedEntry >= 0 && selectedEntry < entries.Count ? entries[selectedEntry] : null;

        private void LoadEntry()
        {
            OrderNoteEntry e = CurrentEntry;
            updating = true;
            quantity.Text = e?.Quantity.HasValue == true ? Numbers.Format(e.Quantity) : "";
            unit.SelectedItem = e?.Unit ?? item.PreferredUnit;
            completeTouched = false;
            updating = false;
            UpdatePreview(true);
        }

        private void UpdatePreview(bool suggestComplete)
        {
            if (updating) return;
            OrderNoteEntry e = CurrentEntry;
            string u = unit.SelectedItem as string ?? Units.Sheets;
            bool valid = Numbers.TryParseQuantity(quantity.Text, out decimal? q) && q.HasValue && q.Value > 0;
            if (suggestComplete && !completeTouched)
            {
                updating = true;
                complete.Checked = !valid || OrderNotes.LooksComplete(e, q ?? 0, u);
                updating = false;
            }
            if (!valid)
            {
                preview.Text = "Bitte die gelieferte Menge eingeben.";
                return;
            }
            SheetItem after = item.Clone();
            if (u == Units.Packages) after.Packages = (after.Packages ?? 0) + q.Value;
            else after.Sheets = (after.Sheets ?? 0) + q.Value;
            string note = item.OpenOrder;
            if (e != null) OrderNotes.ApplyDelivery(item.OpenOrder, e.Raw, q.Value, u, complete.Checked, out note);
            else if (complete.Checked) note = "";
            preview.Text = "Bestand danach: " + after.StockText + "\nOffene Bestellung danach: " + (note.Length > 0 ? note : "keine");
        }

        private void Accept()
        {
            errors.Clear();
            if (!Numbers.TryParseQuantity(quantity.Text, out decimal? q) || !q.HasValue || q.Value <= 0)
            {
                errors.SetError(quantity, "Bitte die gelieferte Menge eingeben, z. B. 10 oder 1,5.");
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            EntryRaw = CurrentEntry?.Raw;
            Delivered = q.Value;
            DeliveredUnit = unit.SelectedItem as string ?? Units.Sheets;
            Complete = complete.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static void Row(TableLayoutPanel t, string label, Control input)
        {
            t.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, Theme.S(7), Theme.S(14), 0),
            });
            input.Margin = new Padding(Theme.S(3), Theme.S(5), Theme.S(20), Theme.S(3));
            t.Controls.Add(input);
        }

        /// <summary>
        /// Zeigt den Dialog und bucht die Lieferung: Menge addieren, Vermerk anpassen, alles im Verlauf.
        /// Wird von beiden Reitern benutzt.
        /// </summary>
        public static void Book(IWin32Window owner, IAppHost host, Guid itemId)
        {
            SheetItem current = host.Inventory.Find(itemId);
            if (current == null || current.OpenOrder.Length == 0) return;
            using (var dlg = new DeliveryDialog(current.Clone()))
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                string raw = dlg.EntryRaw;
                decimal qty = dlg.Delivered;
                string u = dlg.DeliveredUnit;
                bool done = dlg.Complete;
                host.Modify((inv, log) =>
                {
                    SheetItem it = inv.Find(itemId) ?? throw new ItemGoneException();
                    string newNote;
                    if (raw != null)
                    {
                        if (!OrderNotes.ApplyDelivery(it.OpenOrder, raw, qty, u, done, out newNote))
                            throw new InvalidOperationException("Die Bestellung „" + raw + "“ steht nicht mehr bei dieser Position. " +
                                "Vielleicht wurde die Lieferung schon an einem anderen Rechner gebucht. Es wurde nichts geändert.");
                    }
                    else
                    {
                        newNote = done ? "" : it.OpenOrder;
                    }
                    if (u == Units.Packages)
                    {
                        decimal? before = it.Packages;
                        it.Packages = (before ?? 0) + qty;
                        log.Add(it.DisplayName, "Pakete (Lieferung)", Numbers.Format(before), Numbers.Format(it.Packages));
                    }
                    else
                    {
                        decimal? before = it.Sheets;
                        it.Sheets = (before ?? 0) + qty;
                        log.Add(it.DisplayName, "Tafeln (Lieferung)", Numbers.Format(before), Numbers.Format(it.Sheets));
                    }
                    if (newNote != it.OpenOrder)
                    {
                        log.Add(it.DisplayName, "Offene Bestellung", it.OpenOrder, newNote.Length > 0 ? newNote : "geliefert");
                        it.OpenOrder = newNote;
                    }
                    it.ChangedAt = DateTime.Now;
                    it.ChangedBy = host.Store.UserName;
                });
            }
        }
    }
}
