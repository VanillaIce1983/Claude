using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>Farben und Bausteine im Stil der bisherigen Excel-Liste (Tabellenformat „Mittel 2“, blau).</summary>
    internal static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(0x00, 0x70, 0xC0);
        public static readonly Color AccentHover = Color.FromArgb(0x00, 0x5F, 0xA6);
        public static readonly Color AccentDark = Color.FromArgb(0x0B, 0x4A, 0x9E);
        public static readonly Color Window = Color.FromArgb(0xF3, 0xF6, 0xFA);
        public static readonly Color GroupBack = Color.FromArgb(0xBD, 0xD7, 0xEE);
        public static readonly Color GroupText = Color.FromArgb(0x1F, 0x3A, 0x5F);
        public static readonly Color Stripe = Color.FromArgb(0xDD, 0xEB, 0xF7);
        public static readonly Color GridLine = Color.FromArgb(0xC8, 0xD3, 0xDF);
        public static readonly Color Selection = Color.FromArgb(0x9B, 0xC8, 0xF0);
        public static readonly Color Muted = Color.FromArgb(0x5F, 0x6B, 0x7A);
        public static readonly Color RedBack = Color.FromArgb(0xFF, 0xC7, 0xCE);
        public static readonly Color RedFore = Color.FromArgb(0x9C, 0x00, 0x06);
        public static readonly Color YellowBack = Color.FromArgb(0xFF, 0xEB, 0x9C);
        public static readonly Color YellowFore = Color.FromArgb(0x9C, 0x57, 0x00);
        public static readonly Color Border = Color.FromArgb(0xC5, 0xCF, 0xDA);

        public const string FontName = "Segoe UI";

        /// <summary>Anzeigeskalierung (1,0 bei 100 %, 1,5 bei 150 %).</summary>
        public static float Scale { get; set; } = 1f;

        public static int S(int px) => (int)Math.Round(px * Scale);

        public static Font UiFont(float size, FontStyle style = FontStyle.Regular) => new Font(FontName, size, style, GraphicsUnit.Point);

        public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>Etwas dunklere Variante einer Farbe (für markierte Ampelzellen).</summary>
        public static Color Shade(Color c, float factor = 0.86f)
        {
            if (c.IsEmpty) return Color.Empty;
            return Color.FromArgb(c.A, (int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));
        }

        public static void LevelColors(StockLevel level, out Color back, out Color fore)
        {
            switch (level)
            {
                case StockLevel.BelowMinimum: back = RedBack; fore = RedFore; break;
                case StockLevel.AtMinimum: back = YellowBack; fore = YellowFore; break;
                default: back = Color.Empty; fore = Color.Empty; break;
            }
        }

        public static Button PrimaryButton(string text)
        {
            var b = BaseButton(text);
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Accent;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.FlatAppearance.MouseDownBackColor = AccentDark;
            b.Font = UiFont(10f, FontStyle.Bold);
            return b;
        }

        public static Button SecondaryButton(string text)
        {
            var b = BaseButton(text);
            b.BackColor = Color.White;
            b.ForeColor = Color.FromArgb(0x1B, 0x1B, 0x1B);
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xE8, 0xF1, 0xFB);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xD0, 0xE3, 0xF7);
            return b;
        }

        private static Button BaseButton(string text)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(10), S(3), S(10), S(3)),
                Margin = new Padding(S(3), S(3), S(3), S(3)),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Font = UiFont(10f),
            };
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        /// <summary>Beschriftung, die in einer FlowLayoutPanel-Zeile mittig zu einem Eingabefeld steht.</summary>
        public static Label FlowLabel(string text, int height)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Height = height,
                Margin = new Padding(S(8), S(3), S(2), S(3)),
                Font = UiFont(10f),
            };
            l.Width = TextRenderer.MeasureText(text, l.Font).Width + S(4);
            return l;
        }

        public static void StyleGrid(DataGridView g, float fontSize)
        {
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = GridLine;
            g.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.ColumnHeadersDefaultCellStyle.BackColor = Accent;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Accent;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(S(4), 0, S(4), 0);
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.AllowUserToOrderColumns = false;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.CellSelect;
            g.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.DefaultCellStyle.SelectionBackColor = Selection;
            g.DefaultCellStyle.SelectionForeColor = Color.Black;
            g.DefaultCellStyle.Padding = new Padding(S(4), 0, S(4), 0);
            g.ShowCellToolTips = true;
            g.StandardTab = false;
            ApplyGridFont(g, fontSize);
            EnableDoubleBuffer(g);
        }

        public static void ApplyGridFont(DataGridView g, float fontSize)
        {
            g.DefaultCellStyle.Font = UiFont(fontSize);
            g.ColumnHeadersDefaultCellStyle.Font = UiFont(fontSize, FontStyle.Bold);
            int lineHeight = TextRenderer.MeasureText("Ag", g.DefaultCellStyle.Font).Height;
            int rowHeight = (int)Math.Round(lineHeight * 1.75);
            g.RowTemplate.Height = rowHeight;
            foreach (DataGridViewRow r in g.Rows) r.Height = rowHeight;
            g.ColumnHeadersHeight = (int)Math.Round(lineHeight * 2.4);
        }

        public static void EnableDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(c, true, null);
            }
            catch (Exception) { }
        }

        /// <summary>Zeichnet eine Gruppen-Zwischenüberschrift über die ganze Zeilenbreite (wie in der Excel-Liste).</summary>
        public static void PaintGroupRow(DataGridView grid, DataGridViewRowPostPaintEventArgs e, string group, int count, string extra = null)
        {
            Rectangle r = e.RowBounds;
            int width = 0;
            foreach (DataGridViewColumn c in grid.Columns) if (c.Visible) width += c.Width;
            var bounds = new Rectangle(r.X - grid.HorizontalScrollingOffset, r.Y, Math.Max(width, r.Width), r.Height - 1);
            using (var back = new SolidBrush(GroupBack)) e.Graphics.FillRectangle(back, bounds);
            using (var line = new Pen(Accent)) e.Graphics.DrawLine(line, bounds.Left, bounds.Bottom, bounds.Right, bounds.Bottom);
            Font bold = new Font(grid.DefaultCellStyle.Font, FontStyle.Bold);
            var textRect = new Rectangle(bounds.X + S(8), bounds.Y, bounds.Width - S(16), bounds.Height);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, group, bold, textRect, GroupText, flags);
            int w = TextRenderer.MeasureText(e.Graphics, group, bold, textRect.Size, flags).Width;
            string suffix = (count == 1 ? "1 Position" : count + " Positionen") + (string.IsNullOrEmpty(extra) ? "" : " · " + extra);
            var rest = new Rectangle(textRect.X + w + S(10), textRect.Y, Math.Max(0, textRect.Width - w - S(10)), textRect.Height);
            TextRenderer.DrawText(e.Graphics, suffix, grid.DefaultCellStyle.Font, rest, Muted, flags);
            bold.Dispose();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>Grauer Hinweistext in einem leeren Textfeld (nur unter Windows).</summary>
        public static void SetCue(TextBox box, string text)
        {
            if (!IsWindows) return;
            void Apply()
            {
                try { SendMessage(box.Handle, 0x1501, (IntPtr)1, text); } catch (Exception) { }
            }
            if (box.IsHandleCreated) Apply();
            box.HandleCreated += (s, e) => Apply();
        }
    }

    internal static class Assets
    {
        private static Icon icon;
        private static Image logo;

        public static Icon AppIcon
        {
            get
            {
                if (icon == null)
                {
                    try
                    {
                        using (Stream s = typeof(Assets).Assembly.GetManifestResourceStream("Blechbestand.app.ico"))
                            icon = s != null ? new Icon(s) : SystemIcons.Application;
                    }
                    catch (Exception)
                    {
                        icon = SystemIcons.Application;
                    }
                }
                return icon;
            }
        }

        public static Image Logo
        {
            get
            {
                if (logo == null)
                {
                    try
                    {
                        using (Stream s = typeof(Assets).Assembly.GetManifestResourceStream("Blechbestand.logo.png"))
                        {
                            if (s != null)
                            {
                                using (var tmp = Image.FromStream(s)) logo = new Bitmap(tmp);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        logo = AppIcon.ToBitmap();
                    }
                }
                return logo;
            }
        }
    }

    /// <summary>Kleines Eingabefenster für einen Text (z. B. Gruppe umbenennen).</summary>
    internal static class Prompt
    {
        public static string Show(IWin32Window owner, string title, string label, string value)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.Font = Theme.UiFont(10f);
                f.BackColor = Color.White;
                f.AutoSize = true;
                f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                f.Padding = new Padding(Theme.S(12));
                var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
                var lbl = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, Theme.S(4)) };
                var box = new TextBox { Text = value ?? "", Width = Theme.S(320) };
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(10), 0, 0) };
                var ok = Theme.PrimaryButton("OK");
                var cancel = Theme.SecondaryButton("Abbrechen");
                ok.DialogResult = DialogResult.OK;
                cancel.DialogResult = DialogResult.Cancel;
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                table.Controls.Add(lbl);
                table.Controls.Add(box);
                table.Controls.Add(buttons);
                f.Controls.Add(table);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }
    }
}
