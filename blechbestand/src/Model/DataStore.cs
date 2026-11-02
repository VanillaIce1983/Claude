using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace Blechbestand.Model
{
    /// <summary>Die Datendatei ist gerade von einem anderen Rechner zum Speichern gesperrt.</summary>
    public class StoreBusyException : IOException
    {
        public StoreBusyException(string message) : base(message) { }
    }

    /// <summary>Sammelt Einträge für das Änderungsprotokoll (verlauf.csv).</summary>
    public class ChangeLog
    {
        internal readonly List<string[]> Entries = new List<string[]>();

        /// <summary>Protokolliert eine Änderung: Position, Feld, alter Wert, neuer Wert.</summary>
        public void Add(string item, string field, string oldValue, string newValue)
        {
            Entries.Add(new[] { item ?? "", field ?? "", oldValue ?? "", newValue ?? "" });
        }
    }

    /// <summary>
    /// Speichert den Bestand als XML-Datei. Mehrere Rechner dürfen dieselbe Datei (z. B. auf einem
    /// Netzlaufwerk) benutzen: Jede Änderung sperrt die Datei kurz, liest den aktuellen Stand neu ein,
    /// wendet nur diese eine Änderung an und schreibt zurück. So überschreibt niemand die Eingaben eines anderen.
    /// </summary>
    public sealed class DataStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Inventory));
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(8);

        public DataStore(string filePath)
        {
            FilePath = Path.GetFullPath(filePath);
        }

        public string FilePath { get; }

        public string Folder => Path.GetDirectoryName(FilePath);

        public string BackupFolder => Path.Combine(Folder, "Sicherungen");

        public string LogPath => Path.Combine(Folder, Path.GetFileNameWithoutExtension(FilePath) + "_verlauf.csv");

        public bool Exists => File.Exists(FilePath);

        /// <summary>Wie viele Tagessicherungen aufbewahrt werden.</summary>
        public int BackupsToKeep { get; set; } = 60;

        /// <summary>Wer ändert (erscheint in „Geändert“ und im Verlauf).</summary>
        public string UserName { get; set; } = Environment.UserName;

        public string MachineName { get; set; } = Environment.MachineName;

        public DateTime LastWriteUtc => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

        /// <summary>Liest den aktuellen Stand. Wiederholt kurz, falls die Datei gerade ersetzt wird.</summary>
        public Inventory Load()
        {
            Exception last = null;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        var inv = (Inventory)Serializer.Deserialize(fs);
                        inv.Normalize();
                        return inv;
                    }
                }
                catch (FileNotFoundException) { throw; }
                catch (DirectoryNotFoundException) { throw; }
                catch (IOException ex) { last = ex; }
                catch (InvalidOperationException ex)
                {
                    // Halb geschriebene Datei (Ersetzen auf einem anderen Rechner läuft gerade) – kurz warten.
                    last = ex;
                }
                Thread.Sleep(150);
            }
            throw new IOException("Die Datendatei konnte nicht gelesen werden:\n" + FilePath + "\n\n" + last?.Message, last);
        }

        /// <summary>Legt die Datei mit den Startdaten an, falls es sie noch nicht gibt.</summary>
        public void CreateIfMissing(Func<Inventory> seed)
        {
            if (File.Exists(FilePath)) return;
            Directory.CreateDirectory(Folder);
            using (AcquireLock())
            {
                if (File.Exists(FilePath)) return;
                Inventory inv = seed();
                inv.Normalize();
                Write(inv);
            }
        }

        /// <summary>Schreibt einen kompletten Stand (z. B. beim Umzug an einen neuen Speicherort).</summary>
        public void Overwrite(Inventory inventory)
        {
            Directory.CreateDirectory(Folder);
            using (AcquireLock())
            {
                Backup();
                Write(inventory);
            }
        }

        /// <summary>
        /// Sperrt die Datei, liest den neuesten Stand, wendet die Änderung an, speichert und gibt den neuen Stand zurück.
        /// </summary>
        public Inventory Modify(Action<Inventory, ChangeLog> change)
        {
            // Nie mit einem leeren Bestand weitermachen, wenn die Datei (z. B. bei einem Netzwerkaussetzer) fehlt.
            if (!File.Exists(FilePath))
                throw new FileNotFoundException("Die Bestandsdatei ist nicht erreichbar:\n" + FilePath +
                    "\n\nIst das Netzlaufwerk verbunden? Die Änderung wurde nicht gespeichert.", FilePath);
            using (AcquireLock())
            {
                Inventory inv = Load();
                var log = new ChangeLog();
                change(inv, log);
                inv.Normalize();
                Backup();
                Write(inv);
                AppendLog(log);
                return inv;
            }
        }

        private void Write(Inventory inv)
        {
            string tmp = FilePath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (XmlWriter xw = XmlWriter.Create(fs, settings))
            {
                Serializer.Serialize(xw, inv);
            }
            try
            {
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(tmp, FilePath, null);
                    }
                    catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException || ex is UnauthorizedAccessException)
                    {
                        // Manche Netzlaufwerke können nicht atomar ersetzen.
                        File.Copy(tmp, FilePath, true);
                    }
                }
                else
                {
                    File.Move(tmp, FilePath);
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Eine Sicherung pro Tag vor der ersten Änderung, ältere werden aufgeräumt.</summary>
        private void Backup()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                Directory.CreateDirectory(BackupFolder);
                string name = Path.GetFileNameWithoutExtension(FilePath) + "_" + DateTime.Now.ToString("yyyy-MM-dd") + Path.GetExtension(FilePath);
                string target = Path.Combine(BackupFolder, name);
                if (!File.Exists(target)) File.Copy(FilePath, target);

                string pattern = Path.GetFileNameWithoutExtension(FilePath) + "_????-??-??" + Path.GetExtension(FilePath);
                foreach (string old in Directory.GetFiles(BackupFolder, pattern).OrderByDescending(f => f, StringComparer.Ordinal).Skip(BackupsToKeep))
                    File.Delete(old);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void AppendLog(ChangeLog log)
        {
            if (log.Entries.Count == 0) return;
            try
            {
                bool isNew = !File.Exists(LogPath);
                var sb = new StringBuilder();
                if (isNew) sb.Append('﻿').AppendLine("Zeitpunkt;Benutzer;Rechner;Position;Feld;Alt;Neu");
                string now = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                foreach (string[] e in log.Entries)
                {
                    sb.AppendLine(string.Join(";", new[] { now, UserName, MachineName, e[0], e[1], e[2], e[3] }.Select(Csv.Escape)));
                }
                File.AppendAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Sperre über eine Hilfsdatei „bestand.xml.lock“. Sie wird beim Schließen automatisch gelöscht,
        /// auch wenn das Programm abstürzt. Funktioniert auf lokalen Laufwerken und Netzfreigaben.
        /// </summary>
        private IDisposable AcquireLock()
        {
            string lockPath = FilePath + ".lock";
            DateTime deadline = DateTime.UtcNow + LockTimeout;
            while (true)
            {
                try
                {
                    var fs = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                    try
                    {
                        byte[] who = Encoding.UTF8.GetBytes(UserName + "@" + MachineName + " " + DateTime.Now.ToString("s"));
                        fs.Write(who, 0, who.Length);
                        fs.Flush();
                    }
                    catch (IOException) { }
                    return fs;
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow > deadline)
                        throw new StoreBusyException("Die Datendatei wird gerade von einem anderen Rechner gespeichert. Bitte gleich noch einmal versuchen.");
                    Thread.Sleep(100);
                }
            }
        }
    }

    public static class Csv
    {
        public static string Escape(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0) return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
