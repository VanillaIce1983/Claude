using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>Material anlegen oder bearbeiten.</summary>
    internal sealed class ItemDialog : Form
    {
        private readonly Inventory inventory;
        private readonly SheetItem original;
        private readonly bool isNew;

        private readonly ComboBox group;
        private readonly ComboBox material;
        private readonly TextBox thickness;
        private readonly ComboBox dimension;
        private readonly TextBox note;
        private readonly TextBox erp;
        private readonly TextBox packages;
        private readonly TextBox sheets;
        private readonly TextBox minPackages;
        private readonly TextBox minSheets;
        private readonly TextBox weight;
        private readonly TextBox density;
        private readonly TextBox openOrder;
        private readonly TextBox packageSheets;
        private readonly Label packageHint;
        private readonly ErrorProvider errors;
        private bool weightTouched;
        private bool settingWeight;
        private bool groupTouched;
        private bool settingGroup;

        public SheetItem Result { get; private set; }

        public ItemDialog(Inventory inventory, SheetItem item, bool isNew)
        {
            this.inventory = inventory;
            original = item;
            this.isNew = isNew;

            Text = isNew ? "Neues Material" : "Material bearbeiten";
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

            int w = Theme.S(260);
            group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = w };
            group.Items.AddRange(inventory.Groups.Cast<object>().ToArray());
            group.Items.AddRange(MaterialCatalog.All.Select(m => m.Group)
                .Where(g => !inventory.Groups.Contains(g, StringComparer.CurrentCultureIgnoreCase))
                .Distinct().Cast<object>().ToArray());
            material = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = w };
            MaterialPicker.Fill(material, inventory);
            thickness = new TextBox { Width = Theme.S(120) };
            dimension = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = Theme.S(160) };
            dimension.Items.AddRange(MaterialCatalog.Dimensions.Cast<object>().ToArray());
            note = new TextBox { Width = w };
            erp = new TextBox { Width = Theme.S(160) };
            packages = new TextBox { Width = Theme.S(90), TextAlign = HorizontalAlignment.Right };
            sheets = new TextBox { Width = Theme.S(90), TextAlign = HorizontalAlignment.Right };
            minPackages = new TextBox { Width = Theme.S(90), TextAlign = HorizontalAlignment.Right };
            minSheets = new TextBox { Width = Theme.S(90), TextAlign = HorizontalAlignment.Right };
            weight = new TextBox { Width = Theme.S(90), TextAlign = HorizontalAlignment.Right };
            density = new TextBox { Width = Theme.S(60), TextAlign = HorizontalAlignment.Right };
            openOrder = new TextBox { Width = w };

            var table = DialogLayout.Table();

            DialogLayout.Section(table, "Material");
            DialogLayout.Row(table, "Materialgruppe", group, "z. B. Edelstahl – neuer Name legt eine neue Gruppe an");
            Label materialHint = DialogLayout.Row(table, "Werkstoff", material, MaterialPicker.DefaultHint);
            DialogLayout.Row(table, "Dicke T [mm]", thickness, "z. B. 2 oder 1,5 – bei Riffelblech auch 1,5-2");
            DialogLayout.Row(table, "Dimension [mm]", dimension, null);
            DialogLayout.Row(table, "Bemerkung", note, "z. B. gebürstet, Folie einseitig");
            DialogLayout.Row(table, "Eulandanummer", erp, null);

            DialogLayout.Section(table, "Bestand und Mindestbestand");
            DialogLayout.Row(table, "Pakete", DialogLayout.Pair(packages, "Mindestbestand", minPackages), null);
            DialogLayout.Row(table, "Tafeln", DialogLayout.Pair(sheets, "Mindestbestand", minSheets), "Rot = unter Mindestbestand, gelb = genau erreicht. Leer = keine Ampel.");

            DialogLayout.Section(table, "Gewicht");
            var calc = Theme.SecondaryButton("Berechnen");
            calc.Click += (s, e) => CalculateWeight(true);
            var weightRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            weightRow.Controls.Add(weight);
            weightRow.Controls.Add(Theme.FlowLabel("kg   Dichte", weight.PreferredHeight));
            weightRow.Controls.Add(density);
            weightRow.Controls.Add(Theme.FlowLabel("kg/dm³", weight.PreferredHeight));
            weightRow.Controls.Add(calc);
            DialogLayout.Row(table, "Gewicht pro Tafel", weightRow, "Wird aus Dimension, Dicke und Dichte berechnet. Die Dichte kommt aus der Werkstoffliste (Stahl/Edelstahl 8,0, Alu 2,75, Kupfer 8,9, Messing 8,5).");

            packageSheets = new TextBox { Width = Theme.S(90), TextAlign = HorizontalAlignment.Right, Margin = new Padding(Theme.S(3), Theme.S(3), Theme.S(20), 0) };
            packageHint = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(8.5f),
                MaximumSize = new Size(Theme.S(430), 0),
                Margin = new Padding(Theme.S(3), Theme.S(1), Theme.S(3), Theme.S(4)),
            };
            var packageStack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            packageStack.Controls.Add(packageSheets);
            packageStack.Controls.Add(packageHint);
            DialogLayout.Row(table, "Tafeln pro Paket", packageStack, null);

            DialogLayout.Section(table, "Bestellung");
            DialogLayout.Row(table, "Offene Bestellung", openOrder, "Wird beim Export im Reiter „Bestellung“ automatisch gesetzt.");

            var ok = Theme.PrimaryButton(isNew ? "Anlegen" : "Speichern");
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
            group.Text = item.Group;
            material.Text = item.Material;
            thickness.Text = item.Thickness;
            dimension.Text = string.IsNullOrWhiteSpace(item.Dimension) ? "3000x1500" : item.Dimension;
            note.Text = item.Note;
            erp.Text = item.ErpNumber;
            packages.Text = Numbers.Format(item.Packages);
            sheets.Text = Numbers.Format(item.Sheets);
            minPackages.Text = Numbers.Format(item.MinPackages);
            minSheets.Text = Numbers.Format(item.MinSheets);
            weight.Text = Numbers.Format(item.WeightPerSheet);
            packageSheets.Text = Numbers.Format(item.SheetsPerPackage);
            openOrder.Text = item.OpenOrder;
            density.Text = Numbers.Format(Numbers.DefaultDensity(item.Group, item.Material));
            weightTouched = item.WeightPerSheet.HasValue && !isNew;

            weight.TextChanged += (s, e) => { if (!settingWeight) weightTouched = weight.Text.Trim().Length > 0; };
            group.TextChanged += (s, e) => { if (!settingGroup) groupTouched = true; };
            // Werkstoff aus der Liste gewählt: bei neuem Material die passende Gruppe vorschlagen
            MaterialPicker.Attach(material, materialHint, m =>
            {
                if (!isNew || groupTouched) return;
                settingGroup = true;
                group.Text = inventory.Groups.FirstOrDefault(g => string.Equals(g, m.Group, StringComparison.CurrentCultureIgnoreCase)) ?? m.Group;
                settingGroup = false;
            });
            EventHandler auto = (s, e) => CalculateWeight(false);
            thickness.TextChanged += auto;
            dimension.TextChanged += auto;
            density.TextChanged += auto;
            EventHandler densityGuess = (s, e) =>
            {
                if (!weightTouched) density.Text = Numbers.Format(Numbers.DefaultDensity(group.Text, material.Text));
            };
            group.TextChanged += densityGuess;
            material.TextChanged += densityGuess;
            weight.TextChanged += (s, e) => UpdatePackageHint();
            packageSheets.TextChanged += (s, e) => UpdatePackageHint();
            UpdatePackageHint();
        }

        /// <summary>Zeigt, wie viele Tafeln automatisch in ein Paket kommen (Paketgewicht ÷ Tafelgewicht, abgerundet).</summary>
        private void UpdatePackageHint()
        {
            decimal max = inventory.PackageWeight;
            decimal? w = Numbers.ParseQuantityOrNull(weight.Text);
            decimal? auto = Packaging.SheetsPerPackage(w, max);
            decimal? own = Numbers.ParseQuantityOrNull(packageSheets.Text);
            string Weight(decimal n) => w.HasValue ? " ≈ " + Numbers.FormatWeight(n * w.Value) : "";
            if (own.HasValue && own.Value > 0)
            {
                packageHint.Text = "Eigener Wert: 1 Paket = " + Units.Format(own.Value, Units.Sheets) + Weight(own.Value) +
                                   (auto.HasValue ? ". Automatisch wären es " + Units.Format(auto.Value, Units.Sheets) + "." : ".");
            }
            else if (max <= 0)
            {
                packageHint.Text = "Leer lassen = automatisch. Unter „Einstellungen“ ist kein Paketgewicht eingetragen.";
            }
            else if (auto.HasValue)
            {
                packageHint.Text = "Leer lassen = automatisch: 1 Paket = " + Units.Format(auto.Value, Units.Sheets) + Weight(auto.Value) +
                                   " (" + Numbers.FormatWeight(max) + " ÷ " + Numbers.FormatWeight(w) + ", abgerundet). Nur eintragen, wenn anders gepackt wird.";
            }
            else
            {
                packageHint.Text = "Leer lassen = automatisch aus " + Numbers.FormatWeight(max) + " Paketgewicht, sobald das Gewicht pro Tafel bekannt ist.";
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (isNew && original.Group.Length > 0 && original.Material.Length > 0) thickness.Focus();
            else if (isNew) group.Focus();
        }

        private void CalculateWeight(bool force)
        {
            if (!force && weightTouched) return;
            decimal? d = Numbers.ParseQuantityOrNull(density.Text);
            decimal? kg = d.HasValue ? Numbers.SheetWeight(dimension.Text, thickness.Text, d.Value) : null;
            if (!kg.HasValue)
            {
                if (force) MessageBox.Show(this, "Zum Berechnen bitte Dimension (z. B. 3000x1500), Dicke und Dichte angeben.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            settingWeight = true;
            weight.Text = Numbers.Format(kg);
            settingWeight = false;
            if (force) weightTouched = true;
        }

        private bool ReadNumber(TextBox box, out decimal? value)
        {
            if (Numbers.TryParseQuantity(box.Text, out value))
            {
                errors.SetError(box, "");
                return true;
            }
            errors.SetError(box, "Bitte eine Zahl eingeben (z. B. 3 oder 1,5) oder leer lassen.");
            return false;
        }

        private void Accept()
        {
            errors.Clear();
            bool ok = true;
            if (group.Text.Trim().Length == 0) { errors.SetError(group, "Bitte eine Materialgruppe angeben."); ok = false; }
            if (material.Text.Trim().Length == 0) { errors.SetError(material, "Bitte den Werkstoff angeben."); ok = false; }
            if (thickness.Text.Trim().Length == 0) { errors.SetError(thickness, "Bitte die Dicke angeben."); ok = false; }
            ok &= ReadNumber(packages, out decimal? p);
            ok &= ReadNumber(sheets, out decimal? t);
            ok &= ReadNumber(minPackages, out decimal? mp);
            ok &= ReadNumber(minSheets, out decimal? mt);
            ok &= ReadNumber(weight, out decimal? kg);
            ok &= ReadNumber(packageSheets, out decimal? perPackage);
            if (!ok)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            SheetItem r = original.Clone();
            r.Group = group.Text.Trim();
            r.Material = MaterialCatalog.Normalize(material.Text);
            r.Thickness = SheetItem.CleanThickness(thickness.Text);
            r.Dimension = SheetItem.CleanDimension(dimension.Text);
            r.Note = note.Text.Trim();
            r.ErpNumber = erp.Text.Trim();
            r.Packages = p;
            r.Sheets = t;
            r.MinPackages = mp;
            r.MinSheets = mt;
            r.WeightPerSheet = kg;
            r.SheetsPerPackage = perPackage.HasValue && perPackage.Value > 0 ? perPackage : null;
            r.OpenOrder = openOrder.Text.Trim();

            SheetItem duplicate = inventory.Items.FirstOrDefault(i => i.Id != r.Id
                && string.Equals(i.Group, r.Group, StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(i.Material, r.Material, StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(i.Thickness, r.Thickness, StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(i.Dimension, r.Dimension, StringComparison.CurrentCultureIgnoreCase)
                && string.Equals(i.Note, r.Note, StringComparison.CurrentCultureIgnoreCase));
            if (duplicate != null && MessageBox.Show(this,
                    "Dieses Material gibt es schon in der Liste:\n" + duplicate.DisplayName + " (" + duplicate.Group + "), Bestand " + duplicate.StockText +
                    "\n\nTrotzdem speichern?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
