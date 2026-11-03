using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>
    /// Sonderposition für die Bestellung: Material, das nicht im Bestand geführt wird (Sonderfall).
    /// Auf Wunsch wird es gleich dauerhaft in den Bestand aufgenommen.
    /// </summary>
    internal sealed class SpecialOrderDialog : Form
    {
        private readonly Inventory inventory;
        private readonly SheetItem original;
        private readonly ComboBox material;
        private readonly ComboBox thickness;
        private readonly ComboBox dimension;
        private readonly TextBox note;
        private readonly TextBox quantity;
        private readonly ComboBox unit;
        private readonly Label weightInfo;
        private readonly CheckBox addToStock;
        private readonly ComboBox group;
        private readonly ErrorProvider errors;
        private bool groupTouched;
        private bool settingGroup;

        public SheetItem Item { get; private set; }
        public decimal Quantity { get; private set; }
        public string Unit { get; private set; }
        public bool AddToStock { get; private set; }

        /// <param name="item">Vorhandene Sonderposition zum Bearbeiten oder null für eine neue.</param>
        /// <param name="toStock">Haken „Dauerhaft in den Bestand aufnehmen“ schon setzen.</param>
        public SpecialOrderDialog(Inventory inventory, SheetItem item, decimal? currentQuantity, string currentUnit, bool toStock = false)
        {
            this.inventory = inventory;
            original = item;
            bool isNew = item == null;

            Text = isNew ? "Sonderposition bestellen" : "Sonderposition bearbeiten";
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
            errors = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

            int w = Theme.S(280);
            material = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = w };
            MaterialPicker.Fill(material, inventory);
            thickness = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = Theme.S(100), MaxDropDownItems = 18 };
            thickness.Items.AddRange(MaterialCatalog.Thicknesses.Cast<object>().ToArray());
            dimension = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = Theme.S(160) };
            dimension.Items.AddRange(MaterialCatalog.Dimensions.Cast<object>().ToArray());
            note = new TextBox { Width = w };
            quantity = new TextBox { Width = Theme.S(80), TextAlign = HorizontalAlignment.Right };
            unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(100) };
            unit.Items.AddRange(new object[] { Units.Sheets, Units.Packages });
            weightInfo = DialogLayout.HintLabel("");
            addToStock = new CheckBox { Text = "Dauerhaft in den Bestand aufnehmen", AutoSize = true };
            group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = w, Enabled = false };
            group.Items.AddRange(inventory.Groups.Cast<object>().ToArray());
            group.Items.AddRange(MaterialCatalog.All.Select(m => m.Group)
                .Where(g => !inventory.Groups.Contains(g, StringComparer.CurrentCultureIgnoreCase))
                .Distinct().Cast<object>().ToArray());

            var table = DialogLayout.Table();
            DialogLayout.Section(table, "Material");
            Label materialHint = DialogLayout.Row(table, "Werkstoff", material, MaterialPicker.DefaultHint);
            DialogLayout.Row(table, "Dicke T [mm]", thickness, null);
            DialogLayout.Row(table, "Dimension [mm]", dimension, null);
            DialogLayout.Row(table, "Bemerkung", note, "z. B. Folie einseitig, geschliffen K240");

            DialogLayout.Section(table, "Menge");
            var quantityStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var quantityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            quantity.Margin = new Padding(Theme.S(3));
            unit.Margin = new Padding(Theme.S(3));
            quantityRow.Controls.Add(quantity);
            quantityRow.Controls.Add(unit);
            quantityStack.Controls.Add(quantityRow);
            quantityStack.Controls.Add(weightInfo);
            DialogLayout.Row(table, "Bestellmenge", quantityStack, null);

            DialogLayout.Section(table, "Bestand");
            var stockStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            addToStock.Margin = new Padding(Theme.S(3), Theme.S(3), Theme.S(3), Theme.S(2));
            stockStack.Controls.Add(addToStock);
            stockStack.Controls.Add(DialogLayout.HintLabel(
                "Ohne Haken gilt die Position nur für diese Bestellung und taucht im Reiter „Bestand“ nicht auf.\n" +
                "Mit Haken steht das Material danach im Bestand (ohne Mindestbestand), wird als bestellt vermerkt und die Lieferung kann gebucht werden."));
            DialogLayout.Row(table, "", stockStack, null);
            DialogLayout.Row(table, "Materialgruppe", group, null);

            var ok = Theme.PrimaryButton(isNew ? "Zur Bestellung hinzufügen" : "Übernehmen");
            ok.MinimumSize = new Size(Theme.S(110), 0);
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

            // Werte eintragen
            material.Text = item?.Material ?? "";
            thickness.Text = item?.Thickness ?? "";
            dimension.Text = string.IsNullOrWhiteSpace(item?.Dimension) ? "3000x1500" : item.Dimension;
            note.Text = item?.Note ?? "";
            quantity.Text = Numbers.Format(currentQuantity);
            unit.SelectedItem = currentUnit == Units.Packages ? Units.Packages : Units.Sheets;
            group.Text = item?.Group ?? "";
            groupTouched = group.Text.Length > 0;

            group.TextChanged += (s, e) => { if (!settingGroup) groupTouched = true; };
            addToStock.CheckedChanged += (s, e) => group.Enabled = addToStock.Checked;
            addToStock.Checked = toStock;
            MaterialPicker.Attach(material, materialHint, m =>
            {
                if (groupTouched) return;
                settingGroup = true;
                group.Text = inventory.Groups.FirstOrDefault(g => string.Equals(g, m.Group, StringComparison.CurrentCultureIgnoreCase)) ?? m.Group;
                settingGroup = false;
            });
            EventHandler update = (s, e) => UpdateWeight();
            material.TextChanged += update;
            thickness.TextChanged += update;
            dimension.TextChanged += update;
            quantity.TextChanged += update;
            unit.SelectedIndexChanged += update;
            UpdateWeight();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (material.Text.Length == 0) material.Focus();
            else quantity.Focus();
        }

        private decimal? SheetWeight() =>
            Numbers.SheetWeight(dimension.Text, thickness.Text, Numbers.DefaultDensity(group.Text, material.Text));

        /// <summary>Gewicht pro Tafel und der Bestellmenge, bei Paketen mit Tafeln pro Paket.</summary>
        private void UpdateWeight()
        {
            decimal? kg = SheetWeight();
            if (!kg.HasValue)
            {
                weightInfo.Text = "Gewicht wird berechnet, sobald Werkstoff, Dicke und Dimension eingetragen sind.";
                return;
            }
            string text = "ca. " + Numbers.FormatWeight(kg) + " pro Tafel";
            decimal? perPackage = Packaging.SheetsPerPackage(kg, inventory.PackageWeight);
            bool packages = (unit.SelectedItem as string) == Units.Packages;
            if (packages && perPackage.HasValue) text += " · 1 Paket = " + Units.Format(perPackage.Value, Units.Sheets);
            decimal? q = Numbers.ParseQuantityOrNull(quantity.Text);
            if (q.HasValue && q.Value > 0)
            {
                decimal? total = packages ? (perPackage.HasValue ? q * perPackage * kg : null) : q * kg;
                if (total.HasValue) text += " · zusammen ca. " + Numbers.FormatWeight(total);
            }
            weightInfo.Text = text;
        }

        private void Accept()
        {
            errors.Clear();
            bool ok = true;
            if (material.Text.Trim().Length == 0) { errors.SetError(material, "Bitte den Werkstoff angeben."); ok = false; }
            if (thickness.Text.Trim().Length == 0) { errors.SetError(thickness, "Bitte die Dicke angeben."); ok = false; }
            if (!Numbers.TryParseQuantity(quantity.Text, out decimal? q) || !q.HasValue || q.Value <= 0)
            {
                errors.SetError(quantity, "Bitte eine Bestellmenge eingeben, z. B. 3 oder 1,5.");
                ok = false;
            }
            if (addToStock.Checked && group.Text.Trim().Length == 0) { errors.SetError(group, "Bitte eine Materialgruppe angeben."); ok = false; }
            if (!ok)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            Item = new SheetItem
            {
                Id = original?.Id ?? Guid.NewGuid(),
                Group = group.Text.Trim(),
                Material = MaterialCatalog.Normalize(material.Text),
                Thickness = SheetItem.CleanThickness(thickness.Text),
                Dimension = SheetItem.CleanDimension(dimension.Text),
                Note = note.Text.Trim(),
                WeightPerSheet = SheetWeight(),
            };
            Quantity = q.Value;
            Unit = (unit.SelectedItem as string) ?? Units.Sheets;
            AddToStock = addToStock.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
