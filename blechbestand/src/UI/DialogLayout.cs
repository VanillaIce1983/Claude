using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>Gemeinsamer Aufbau der Eingabedialoge: Abschnittstitel, Zeilen mit Beschriftung und Hinweis.</summary>
    internal static class DialogLayout
    {
        public static TableLayoutPanel Table()
        {
            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return table;
        }

        public static void Section(TableLayoutPanel t, string title)
        {
            var l = new Label
            {
                Text = title,
                AutoSize = true,
                Font = Theme.UiFont(10.5f, FontStyle.Bold),
                ForeColor = Theme.Accent,
                Margin = new Padding(0, t.Controls.Count == 0 ? 0 : Theme.S(12), 0, Theme.S(4)),
            };
            t.Controls.Add(l);
            t.SetColumnSpan(l, 2);
        }

        /// <summary>Zeile mit Beschriftung, Eingabe und optionalem grauem Hinweis darunter (wird zurückgegeben).</summary>
        public static Label Row(TableLayoutPanel t, string label, Control input, string hint)
        {
            var l = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, Theme.S(7), Theme.S(12), 0),
            };
            t.Controls.Add(l);
            if (hint == null)
            {
                input.Margin = new Padding(Theme.S(3), Theme.S(3), Theme.S(20), Theme.S(3));
                t.Controls.Add(input);
                return null;
            }
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            input.Margin = new Padding(Theme.S(3), Theme.S(3), Theme.S(20), 0);
            stack.Controls.Add(input);
            Label h = HintLabel(hint);
            stack.Controls.Add(h);
            t.Controls.Add(stack);
            return h;
        }

        public static Label HintLabel(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = Theme.UiFont(8.5f),
            MaximumSize = new Size(Theme.S(430), 0),
            Margin = new Padding(Theme.S(3), Theme.S(1), Theme.S(3), Theme.S(4)),
        };

        public static Control Pair(Control first, string secondLabel, Control second)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            first.Margin = new Padding(Theme.S(3));
            p.Controls.Add(first);
            p.Controls.Add(Theme.FlowLabel(secondLabel, first.PreferredSize.Height));
            second.Margin = new Padding(Theme.S(3));
            p.Controls.Add(second);
            return p;
        }
    }

    /// <summary>Auswahlfeld „Werkstoff“ mit der eingebauten Werkstoffliste.</summary>
    internal static class MaterialPicker
    {
        public const string DefaultHint = "Liste aufklappen oder frei eintragen, z. B. 1.4301, S355MC, EN AW-5754";

        /// <summary>Werkstoffliste (nach Gruppen) und Werkstoffe aus dem Bestand, die nicht in der Liste stehen.</summary>
        public static void Fill(ComboBox box, Inventory inventory)
        {
            box.Items.Clear();
            box.Items.AddRange(MaterialCatalog.All.Cast<object>().ToArray());
            box.Items.AddRange(inventory.Items.Select(i => i.Material)
                .Where(m => m.Length > 0 && MaterialCatalog.Find(m) == null)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(m => m, StringComparer.CurrentCultureIgnoreCase)
                .Cast<object>().ToArray());
            box.MaxDropDownItems = 20;
            box.DropDownWidth = Math.Max(box.Width, Theme.S(440));
        }

        /// <summary>
        /// Nach der Auswahl aus der Liste steht nur die Werkstoffnummer im Feld (z. B. „1.0038“).
        /// <paramref name="picked"/> meldet einen neu gewählten oder eingetippten Werkstoff aus der Liste
        /// (z. B. für den Gruppenvorschlag), der Hinweis darunter zeigt, was die Nummer bedeutet.
        /// </summary>
        public static void Attach(ComboBox box, Label hint, Action<CatalogMaterial> picked)
        {
            // Gruppe nur vorschlagen, wenn ein anderer Werkstoff gewählt oder eingetippt wird als zu Beginn
            CatalogMaterial last = MaterialCatalog.Find(box.Text);
            box.TextChanged += (s, e) =>
            {
                CatalogMaterial m = MaterialCatalog.Find(box.Text);
                if (m == null || ReferenceEquals(m, last)) return;
                last = m;
                picked?.Invoke(m);
            };
            box.SelectionChangeCommitted += (s, e) =>
            {
                if (!(box.SelectedItem is CatalogMaterial m)) return;
                if (box.IsHandleCreated)
                    box.BeginInvoke((Action)(() =>
                    {
                        box.Text = m.Material;
                        box.SelectionStart = box.Text.Length;
                        box.SelectionLength = 0;
                    }));
            };
            if (hint == null) return;
            EventHandler update = (s, e) =>
            {
                CatalogMaterial m = MaterialCatalog.Find(box.Text);
                string typed = MaterialCatalog.Normalize(box.Text);
                if (m == null)
                {
                    hint.Text = DefaultHint;
                    hint.ForeColor = Theme.Muted;
                    return;
                }
                bool sameText = string.Equals(typed, m.Material, StringComparison.CurrentCultureIgnoreCase);
                hint.Text = (sameText ? m.Info : "= " + m.Display) + (m.Info.Length > 0 || !sameText ? " · " : "") +
                            "Dichte " + Numbers.Format(m.Density) + " kg/dm³";
                hint.ForeColor = Theme.AccentDark;
            };
            box.TextChanged += update;
            update(null, EventArgs.Empty);
        }
    }
}
