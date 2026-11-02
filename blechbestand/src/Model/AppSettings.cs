using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Blechbestand.Model
{
    /// <summary>Einstellungen pro Windows-Benutzer, gespeichert unter %APPDATA%\Blechbestand\einstellungen.xml.</summary>
    [XmlRoot("Einstellungen")]
    public class AppSettings
    {
        public const string DataFileName = "bestand.xml";

        /// <summary>Gewählte Datendatei. Leer = automatisch (neben der .exe, sonst Dokumente\Blechbestand).</summary>
        public string DataFile { get; set; } = "";

        public string OrderTo { get; set; } = "";

        public string OrderCc { get; set; } = "";

        /// <summary>Betreff der Bestell-Mail, {datum} wird durch das heutige Datum ersetzt.</summary>
        public string OrderSubject { get; set; } = "Blechbestellung {datum}";

        public string OrderIntro { get; set; } = "Hallo,\r\n\r\nbitte folgende Bleche bestellen:";

        public string OrderOutro { get; set; } = "Vielen Dank!";

        /// <summary>Schriftgröße der Tabellen in Punkt (für die Lasermaschine gern größer).</summary>
        public float FontSize { get; set; } = 10.5f;

        public int WindowWidth { get; set; }

        public int WindowHeight { get; set; }

        public bool WindowMaximized { get; set; }

        [XmlIgnore]
        public static string SettingsFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blechbestand");

        [XmlIgnore]
        public static string SettingsPath => Path.Combine(SettingsFolder, "einstellungen.xml");

        /// <summary>Dokumente\Blechbestand\bestand.xml</summary>
        [XmlIgnore]
        public static string DefaultDataFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Blechbestand", DataFileName);

        /// <summary>bestand.xml im Ordner der Blechbestand.exe (z. B. auf einem Netzlaufwerk).</summary>
        [XmlIgnore]
        public static string PortableDataFile =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DataFileName);

        /// <summary>
        /// Welche Datei benutzt wird: die in den Einstellungen gewählte; sonst eine bestand.xml neben der .exe
        /// (dann teilen sich alle Rechner, die das Programm von dort starten, die Daten); sonst Dokumente\Blechbestand.
        /// </summary>
        public string ResolveDataFile()
        {
            if (!string.IsNullOrWhiteSpace(DataFile)) return DataFile;
            if (File.Exists(PortableDataFile)) return PortableDataFile;
            return DefaultDataFile;
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    using (var fs = File.OpenRead(SettingsPath))
                    {
                        var s = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(fs);
                        if (s.FontSize < 8 || s.FontSize > 20) s.FontSize = 10.5f;
                        return s;
                    }
                }
            }
            catch (Exception)
            {
                // Beschädigte Einstellungen: mit Standardwerten weitermachen.
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                var ws = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                using (var fs = File.Create(SettingsPath))
                using (XmlWriter xw = XmlWriter.Create(fs, ws))
                {
                    new XmlSerializer(typeof(AppSettings)).Serialize(xw, this);
                }
            }
            catch (Exception)
            {
                // Einstellungen sind nicht kritisch.
            }
        }
    }
}
