using System;
using System.Runtime.InteropServices;
using Blechbestand.Model;

namespace Blechbestand.UI
{
    /// <summary>
    /// Öffnet eine neue E-Mail im klassischen Outlook (Microsoft 365 / Office) über dessen COM-Schnittstelle.
    /// Die Mail wird nur angezeigt, nicht gesendet. Eine eingerichtete Outlook-Signatur bleibt unter der Liste erhalten.
    /// Das „neue Outlook“ für Windows hat keine solche Schnittstelle; dann greift die .eml-Datei als Ersatz.
    /// </summary>
    internal static class OutlookExporter
    {
        private const int OlMailItem = 0;
        private const int OlFormatHtml = 2;

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
                error = "Outlook (klassisch) ist auf diesem Rechner nicht installiert.";
                return false;
            }

            object app = null, mail = null;
            try
            {
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
                return true;
            }
            catch (COMException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                // Outlook-Fenster bleibt offen; nur unsere Referenzen freigeben.
                if (mail != null && Marshal.IsComObject(mail)) Marshal.ReleaseComObject(mail);
                if (app != null && Marshal.IsComObject(app)) Marshal.ReleaseComObject(app);
            }
        }
    }
}
