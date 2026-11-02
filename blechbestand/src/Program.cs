using System;
using System.Threading;
using System.Windows.Forms;
using Blechbestand.UI;

namespace Blechbestand
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }

        private static int showing;

        private static void ShowError(Exception ex)
        {
            if (Interlocked.Exchange(ref showing, 1) == 1) return;
            try
            {
                MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten:\n\n" + (ex?.Message ?? "unbekannt") +
                                "\n\nDie zuletzt gespeicherten Daten bleiben erhalten.\n\n" + ex?.GetType().Name + "\n" + ex?.StackTrace,
                    "Blechbestand", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Interlocked.Exchange(ref showing, 0);
            }
        }
    }
}
