using System;
using System.Drawing;
using System.Windows.Forms;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.UI
{
    /// <summary>
    /// Result list after a run: one line per feature with status, message and check. Shown
    /// without blocking SOLIDWORKS, owned by its main window.
    /// </summary>
    internal sealed class ReportForm : Form
    {
        public ReportForm(MirrorReport report)
        {
            Text = "Spiegelwerk – Ergebnis";
            Width = 980;
            Height = 560;
            // CenterParent works for modal dialogs only.
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Font = SystemFonts.MessageBoxFont;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(8, 8, 8, 0),
                Text = $"{report.TargetName}\n{report.Summary}",
            };

            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
            };
            list.Columns.Add("Status", 90);
            list.Columns.Add("Feature", 170);
            list.Columns.Add("Typ", 100);
            list.Columns.Add("Meldung", 380);
            list.Columns.Add("Prüfung", 210);

            foreach (string g in report.General)
                list.Items.Add(new ListViewItem(new[] { "", "", "", g, "" }) { ForeColor = SystemColors.GrayText });
            foreach (ReportEntry e in report.Entries)
            {
                var item = new ListViewItem(new[] { MirrorReport.StatusText(e.Status), e.Feature, e.Type, e.Message, e.Verification })
                {
                    ForeColor = e.Status switch
                    {
                        ReportStatus.Failed => Color.Firebrick,
                        ReportStatus.Warning => Color.DarkGoldenrod,
                        ReportStatus.Skipped => SystemColors.GrayText,
                        _ => SystemColors.WindowText,
                    },
                };
                list.Items.Add(item);
                foreach (string d in e.Details)
                    list.Items.Add(new ListViewItem(new[] { "", "", "", "   " + d, "" }) { ForeColor = item.ForeColor });
            }

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var close = new Button { Text = "Schließen", AutoSize = true };
            close.Click += (s, a) => Close();
            var copy = new Button { Text = "Als Text kopieren", AutoSize = true };
            copy.Click += (s, a) => Clipboard.SetText(report.ToText());
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            AcceptButton = close;

            Controls.Add(list);
            Controls.Add(buttons);
            Controls.Add(header);
        }
    }

    /// <summary>Lets a WinForms window use the SOLIDWORKS main window as owner.</summary>
    internal sealed class WindowWrapper : IWin32Window
    {
        public WindowWrapper(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }
    }
}
