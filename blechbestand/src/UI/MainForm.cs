using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>Was die Reiter vom Hauptfenster brauchen.</summary>
    internal interface IAppHost
    {
        Inventory Inventory { get; }
        AppSettings Settings { get; }
        DataStore Store { get; }

        /// <summary>Änderung speichern (sperrt die Datei, liest neu, ändert, schreibt). Zeigt Fehler selbst an.</summary>
        bool Modify(Action<Inventory, ChangeLog> change);

        /// <summary>Der Bestand hat sich geändert (eigene Änderung oder von einem anderen Rechner).</summary>
        event EventHandler DataChanged;

        /// <summary>Wechselt in den Reiter „Bestellung“ und hakt die Position an.</summary>
        void AddToOrder(Guid itemId);
    }

    /// <summary>Die Position wurde inzwischen auf einem anderen Rechner gelöscht.</summary>
    internal class ItemGoneException : InvalidOperationException
    {
        public ItemGoneException() : base("Diese Position wurde inzwischen gelöscht (vielleicht an einem anderen Rechner). Die Liste wird neu geladen.") { }
    }

    internal sealed class MainForm : Form, IAppHost
    {
        private readonly AppSettings settings;
        private DataStore store;
        private Inventory inventory = new Inventory();
        private DateTime lastWriteUtc = DateTime.MinValue;

        private readonly TabControl tabs;
        private readonly StockTab stockTab;
        private readonly OrderTab orderTab;
        private readonly ToolStripStatusLabel statusFile;
        private readonly ToolStripStatusLabel statusSaved;
        private readonly Timer refreshTimer;

        public event EventHandler DataChanged;

        public MainForm()
        {
            settings = AppSettings.Load();
            using (Graphics g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;

            Text = "Blechbestand";
            Icon = Assets.AppIcon;
            Font = Theme.UiFont(10f);
            BackColor = Theme.Window;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Theme.S(980), Theme.S(620));
            Size = settings.WindowWidth > 0 && settings.WindowHeight > 0
                ? new Size(Math.Max(settings.WindowWidth, MinimumSize.Width), Math.Max(settings.WindowHeight, MinimumSize.Height))
                : new Size(Theme.S(1360), Theme.S(820));
            if (settings.WindowMaximized) WindowState = FormWindowState.Maximized;
            KeyPreview = true;

            Control header = BuildHeader();

            tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = Theme.UiFont(11f),
                Padding = new Point(Theme.S(18), Theme.S(6)),
            };
            stockTab = new StockTab(this) { Dock = DockStyle.Fill };
            orderTab = new OrderTab(this) { Dock = DockStyle.Fill };
            var stockPage = new TabPage("Bestand") { BackColor = Theme.Window, Padding = new Padding(0) };
            var orderPage = new TabPage("Bestellung / Einkauf") { BackColor = Theme.Window, Padding = new Padding(0) };
            stockPage.Controls.Add(stockTab);
            orderPage.Controls.Add(orderTab);
            tabs.TabPages.Add(stockPage);
            tabs.TabPages.Add(orderPage);
            tabs.SelectedIndexChanged += (s, e) => TryReload(false);

            var status = new StatusStrip { SizingGrip = true, BackColor = Color.White, Font = Theme.UiFont(9f) };
            statusFile = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            statusSaved = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleRight };
            status.Items.Add(statusFile);
            status.Items.Add(statusSaved);

            Controls.Add(tabs);
            Controls.Add(header);
            Controls.Add(status);
            tabs.BringToFront();

            // Änderungen anderer Rechner regelmäßig übernehmen
            refreshTimer = new Timer { Interval = 15000 };
            refreshTimer.Tick += (s, e) => TryReload(false);
        }

        public Inventory Inventory => inventory;
        public AppSettings Settings => settings;
        public DataStore Store => store;

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(64), BackColor = Theme.Accent };
            var logo = new PictureBox
            {
                Image = Assets.Logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(Theme.S(48), Theme.S(48)),
                Location = new Point(Theme.S(14), Theme.S(8)),
                BackColor = Color.Transparent,
            };
            var title = new Label
            {
                Text = "Blechbestand",
                Font = Theme.UiFont(18f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(Theme.S(70), Theme.S(6)),
                BackColor = Color.Transparent,
            };
            var subtitle = new Label
            {
                Text = "Lagerbestand Bleche · Bestellung an den Einkauf",
                Font = Theme.UiFont(9.5f),
                ForeColor = Color.FromArgb(0xD6, 0xE8, 0xF8),
                AutoSize = true,
                Location = new Point(Theme.S(73), Theme.S(40)),
                BackColor = Color.Transparent,
            };
            var settingsButton = new Button
            {
                Text = "Einstellungen",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Theme.AccentHover,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Theme.S(10), Theme.S(4), Theme.S(10), Theme.S(4)),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = Theme.UiFont(10f),
                UseVisualStyleBackColor = false,
            };
            settingsButton.FlatAppearance.BorderColor = Color.FromArgb(0x5A, 0xA3, 0xE0);
            settingsButton.FlatAppearance.MouseOverBackColor = Theme.AccentDark;
            settingsButton.Click += (s, e) => ShowSettings();
            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(settingsButton);
            void PlaceButton()
            {
                settingsButton.Location = new Point(header.ClientSize.Width - settingsButton.Width - Theme.S(14),
                    (header.Height - settingsButton.Height) / 2);
            }
            header.Resize += (s, e) => PlaceButton();
            settingsButton.SizeChanged += (s, e) => PlaceButton();
            PlaceButton();
            return header;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            OpenStore(settings.ResolveDataFile(), true);
            refreshTimer.Start();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (store != null) TryReload(false);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            stockTab.EndEditing();
            orderTab.EndEditing();
            settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.WindowWidth = b.Width;
            settings.WindowHeight = b.Height;
            orderTab.StoreMailSettings();
            settings.Save();
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    TryReload(true);
                    return true;
                case Keys.Control | Keys.F:
                    if (tabs.SelectedIndex == 0) stockTab.FocusSearch(); else orderTab.FocusSearch();
                    return true;
                case Keys.Control | Keys.N:
                    tabs.SelectedIndex = 0;
                    stockTab.AddItem();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Öffnet (oder legt beim ersten Start an) die Datendatei.</summary>
        private bool OpenStore(string path, bool firstStart)
        {
            var candidate = new DataStore(path);
            Inventory loaded;
            try
            {
                if (!candidate.Exists)
                {
                    candidate.CreateIfMissing(SeedData.Create);
                    if (firstStart) welcomePath = candidate.FilePath;
                }
                loaded = candidate.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Die Bestandsdatei kann nicht geöffnet werden:\n" + path + "\n\n" + ex.Message +
                    "\n\nBitte unter „Einstellungen“ einen anderen Speicherort wählen.",
                    "Blechbestand", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (store == null)
                {
                    store = candidate;
                    UpdateStatus("Datei nicht verfügbar");
                    DataChanged?.Invoke(this, EventArgs.Empty);
                }
                return false;
            }
            store = candidate;
            inventory = loaded;
            lastWriteUtc = store.LastWriteUtc;
            UpdateStatus(null);
            DataChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private string welcomePath;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (welcomePath == null) return;
            string path = welcomePath;
            welcomePath = null;
            BeginInvoke(new Action(() => MessageBox.Show(this,
                "Willkommen bei Blechbestand!\n\nDie Bestandsdatei wurde neu angelegt und mit den Daten aus der Excel-Liste „Blechbestand.xlsx“ gefüllt:\n\n" +
                path +
                "\n\nSollen mehrere Rechner (z. B. Laser und Büro) dieselben Daten benutzen, unter „Einstellungen“ einen gemeinsamen Speicherort auf dem Netzlaufwerk wählen.",
                "Blechbestand", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        }

        public bool Modify(Action<Inventory, ChangeLog> change)
        {
            if (store == null) return false;
            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                inventory = store.Modify(change);
                lastWriteUtc = store.LastWriteUtc;
                UpdateStatus(null);
                DataChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (StoreBusyException ex)
            {
                MessageBox.Show(this, ex.Message, "Blechbestand", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (ItemGoneException ex)
            {
                MessageBox.Show(this, ex.Message, "Blechbestand", MessageBoxButtons.OK, MessageBoxIcon.Information);
                TryReload(true);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Keine Schreibrechte für die Bestandsdatei:\n" + store.FilePath +
                    "\n\nDie Änderung wurde nicht gespeichert.", "Blechbestand", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Die Änderung konnte nicht gespeichert werden.\n\n" + ex.Message,
                    "Blechbestand", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = previous;
            }
            // Bei Fehlern den Stand der Datei anzeigen, damit nichts Ungespeichertes stehen bleibt.
            DataChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        /// <summary>Übernimmt Änderungen von anderen Rechnern. Während einer Eingabe wird gewartet.</summary>
        private void TryReload(bool force)
        {
            if (store == null) return;
            if (stockTab.IsEditing || orderTab.IsEditing) return;
            try
            {
                if (!store.Exists) { UpdateStatus("Datei nicht gefunden"); return; }
                DateTime w = store.LastWriteUtc;
                if (!force && w == lastWriteUtc) return;
                inventory = store.Load();
                lastWriteUtc = w;
                UpdateStatus(null);
                DataChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                UpdateStatus("Lesefehler: " + ex.Message.Split('\n')[0]);
            }
        }

        private void UpdateStatus(string problem)
        {
            statusFile.Text = store == null ? "" : "Datei: " + store.FilePath;
            if (problem != null)
            {
                statusSaved.Text = problem;
                statusSaved.ForeColor = Theme.RedFore;
                return;
            }
            statusSaved.ForeColor = Color.Black;
            string stamp = lastWriteUtc > DateTime.MinValue ? lastWriteUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "–";
            statusSaved.Text = inventory.Items.Count + " Positionen · zuletzt gespeichert " + stamp;
        }

        public void AddToOrder(Guid itemId)
        {
            tabs.SelectedIndex = 1;
            orderTab.Select(itemId);
        }

        private void ShowSettings()
        {
            stockTab.EndEditing();
            using (var dlg = new SettingsDialog(settings, store?.FilePath ?? settings.ResolveDataFile()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                settings.FontSize = dlg.GridFontSize;
                settings.OrderSubject = dlg.SubjectTemplate;
                stockTab.ApplyFontSize(settings.FontSize);
                orderTab.ApplyFontSize(settings.FontSize);

                string newSetting = dlg.DataFileSetting;
                string newPath = string.IsNullOrWhiteSpace(newSetting)
                    ? new AppSettings { DataFile = "" }.ResolveDataFile()
                    : newSetting;
                bool pathChanged = store == null || !string.Equals(Path.GetFullPath(newPath), store.FilePath, StringComparison.OrdinalIgnoreCase);
                if (pathChanged)
                {
                    if (!File.Exists(newPath))
                    {
                        DialogResult answer = MessageBox.Show(this,
                            "Am neuen Speicherort gibt es noch keine Bestandsdatei:\n" + newPath +
                            "\n\nDen aktuellen Bestand dorthin kopieren und ab jetzt diese Datei benutzen?",
                            "Speicherort ändern", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                        if (answer != DialogResult.OK) { settings.Save(); return; }
                        try
                        {
                            var target = new DataStore(newPath);
                            target.Overwrite(inventory.Items.Count > 0 ? inventory : SeedData.Create());
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, "Die Datei konnte dort nicht angelegt werden:\n" + ex.Message,
                                "Speicherort ändern", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            settings.Save();
                            return;
                        }
                    }
                    if (OpenStore(newPath, false)) settings.DataFile = newSetting;
                }
                else
                {
                    settings.DataFile = newSetting;
                }
                settings.Save();
            }
        }
    }
}
