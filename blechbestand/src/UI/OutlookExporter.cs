using System;
using System.Runtime.InteropServices;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>
    /// Öffnet eine neue E-Mail im klassischen Outlook (Microsoft 365 / Office) über dessen COM-Schnittstelle.
    /// Die Mail wird nur angezeigt, nicht gesendet. Eine eingerichtete Outlook-Signatur bleibt unter der Liste erhalten.
    /// Das „neue Outlook“ für Windows hat keine solche Schnittstelle; dafür gibt es im Reiter Bestellung den Weg
    /// über das Standard-Mailprogramm und die Zwischenablage.
    /// </summary>
    internal static class OutlookExporter
    {
        private const int OlMailItem = 0;
        private const int OlFormatHtml = 2;
        private const int AsfwAny = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        public static bool TryCreateDraft(string to, string cc, string subject, string htmlContent, out string error)
        {
            error = null;
            if (!Theme.IsWindows)
            {
                error = "Outlook gibt es nur unter Windows.";
                return false;
            }
            Type type;
            try
            {
                type = Type.GetTypeFromProgID("Outlook.Application", false);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            if (type == null)
            {
                error = "Das klassische Outlook ist auf diesem Rechner nicht installiert.";
                return false;
            }

            object app = null, mail = null, inspector = null;
            try
            {
                // Outlook darf sein Fenster in den Vordergrund holen (sonst öffnet es oft verdeckt hinter Blechbestand).
                try { AllowSetForegroundWindow(AsfwAny); } catch (Exception) { }

                app = Activator.CreateInstance(type);
                dynamic outlook = app;
                mail = outlook.CreateItem(OlMailItem);
                dynamic m = mail;
                if (!string.IsNullOrWhiteSpace(to)) m.To = to;
                if (!string.IsNullOrWhiteSpace(cc)) m.CC = cc;
                m.Subject = subject ?? "";
                m.BodyFormat = OlFormatHtml;
                // Erst anzeigen: dann fügt Outlook die Standardsignatur ein, die wir unter die Liste setzen.
                m.Display(false);
                string existing = m.HTMLBody as string;
                m.HTMLBody = OrderMail.InsertIntoBody(existing, htmlContent);
                try
                {
                    inspector = m.GetInspector;
                    ((dynamic)inspector).Activate();
                }
                catch (Exception)
                {
                    // Nach vorne holen ist nur eine Hilfe; die Mail ist trotzdem offen.
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                // Outlook-Fenster bleibt offen; nur unsere Referenzen freigeben.
                if (inspector != null && Marshal.IsComObject(inspector)) Marshal.ReleaseComObject(inspector);
                if (mail != null && Marshal.IsComObject(mail)) Marshal.ReleaseComObject(mail);
                if (app != null && Marshal.IsComObject(app)) Marshal.ReleaseComObject(app);
            }
        }
    }
}
