using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>Speicherort der Daten, Schriftgröße und Vorlage für den Betreff.</summary>
    internal sealed class SettingsDialog : Form
    {
        private readonly TextBox pathBox;
        private readonly NumericUpDown fontSize;
        private readonly TextBox subjectTemplate;
        private string dataFileSetting;

        /// <summary>Gewählte Datendatei, leer = automatisch.</summary>
        public string DataFileSetting => dataFileSetting;

        public float GridFontSize => (float)fontSize.Value;

        public string SubjectTemplate => subjectTemplate.Text.Trim().Length > 0 ? subjectTemplate.Text.Trim() : "Blechbestellung {datum}";

        public SettingsDialog(AppSettings settings, string currentPath)
        {
            dataFileSetting = settings.DataFile ?? "";

            Text = "Einstellungen";
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

            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            int width = Theme.S(560);

            table.Controls.Add(Heading("Speicherort der Bestandsdatei", true));
            pathBox = new TextBox { ReadOnly = true, Width = width, Text = currentPath, BackColor = Theme.Window, TabStop = false };
            table.Controls.Add(pathBox);
            var pathButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, Theme.S(4), 0, 0) };
            var change = Theme.SecondaryButton("Andere Datei wählen …");
            change.Click += (s, e) => ChooseFile();
            var reset = Theme.SecondaryButton("Standard");
            reset.Click += (s, e) =>
            {
                dataFileSetting = "";
                pathBox.Text = new AppSettings().ResolveDataFile();
            };
            var open = Theme.SecondaryButton("Ordner öffnen");
            open.Click += (s, e) => OpenFolder();
            pathButtons.Controls.Add(change);
            pathButtons.Controls.Add(reset);
            pathButtons.Controls.Add(open);
            table.Controls.Add(pathButtons);
            table.Controls.Add(Hint(
                "Damit der Laser-Rechner und das Büro dieselben Zahlen sehen, die Datei auf ein Netzlaufwerk legen " +
                "(z. B. \\\\server\\lager\\bestand.xml) und an jedem Rechner hier auswählen. Mehrere Rechner können gleichzeitig " +
                "arbeiten; jede Eingabe wird sofort gespeichert. Tägliche Sicherungen liegen im Unterordner „Sicherungen“.", width));

            table.Controls.Add(Heading("Anzeige", false));
            var fontRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            fontSize = new NumericUpDown
            {
                Minimum = 8,
                Maximum = 18,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = (decimal)Math.Max(8f, Math.Min(18f, settings.FontSize)),
                Width = Theme.S(70),
                Margin = new Padding(Theme.S(3)),
            };
            fontRow.Controls.Add(Theme.FlowLabel("Schriftgröße der Tabellen", fontSize.PreferredHeight));
            fontRow.Controls.Add(fontSize);
            fontRow.Controls.Add(Theme.FlowLabel("pt  (größer = besser lesbar an der Maschine)", fontSize.PreferredHeight));
            table.Controls.Add(fontRow);

            table.Controls.Add(Heading("Bestellung", false));
            table.Controls.Add(new Label { Text = "Betreff der E-Mail ({datum} = heutiges Datum)", AutoSize = true, Margin = new Padding(0, 0, 0, Theme.S(2)) });
            subjectTemplate = new TextBox { Width = width, Text = settings.OrderSubject };
            table.Controls.Add(subjectTemplate);

            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            table.Controls.Add(Hint("Blechbestand " + v.Major + "." + v.Minor + "." + v.Build +
                " · Einstellungen: " + AppSettings.SettingsPath, width));

            var ok = Theme.PrimaryButton("OK");
            ok.MinimumSize = new Size(Theme.S(100), 0);
            ok.DialogResult = DialogResult.OK;
            var cancel = Theme.SecondaryButton("Abbrechen");
            cancel.DialogResult = DialogResult.Cancel;
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(14), 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            table.Controls.Add(buttons);

            Controls.Add(table);
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => { pathBox.SelectionLength = 0; fontSize.Focus(); };
        }

        private static Label Heading(string text, bool first) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = Theme.UiFont(10.5f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            Margin = new Padding(0, first ? 0 : Theme.S(14), 0, Theme.S(4)),
        };

        private static Label Hint(string text, int width) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = Theme.UiFont(9f),
            MaximumSize = new Size(width, 0),
            Margin = new Padding(0, Theme.S(6), 0, 0),
        };

        private void ChooseFile()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Bestandsdatei wählen (vorhandene Datei auswählen oder neuen Speicherort angeben)",
                Filter = "Blechbestand-Daten (*.xml)|*.xml",
                FileName = AppSettings.DataFileName,
                OverwritePrompt = false,
                CheckFileExists = false,
                AddExtension = true,
                DefaultExt = "xml",
            })
            {
                try { dlg.InitialDirectory = Path.GetDirectoryName(pathBox.Text); } catch (Exception) { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                dataFileSetting = dlg.FileName;
                pathBox.Text = dlg.FileName;
            }
        }

        private void OpenFolder()
        {
            try
            {
                string folder = Path.GetDirectoryName(pathBox.Text);
                if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
