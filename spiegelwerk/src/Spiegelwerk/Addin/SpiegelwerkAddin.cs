using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using Spiegelwerk.UI;

namespace Spiegelwerk.Addin
{
    /// <summary>
    /// COM entry point. SOLIDWORKS finds the add-in through
    /// HKLM\SOFTWARE\SolidWorks\Addins\{GUID} (written by <see cref="Register"/> when
    /// RegAsm registers the assembly) and creates it by ProgId/CLSID.
    /// </summary>
    [ComVisible(true)]
    [Guid("8a3c6f2e-5b1d-4e7a-9c3f-6d2e1b0a4f57")]
    [ProgId("Spiegelwerk.Addin")]
    public sealed class SpiegelwerkAddin : ISwAddin
    {
        private const string Title = "Spiegelwerk";
        private const string Description = "Spiegelt Features als eigenständige, unabhängige Features (ohne Spiegel-Feature).";
        private const int GroupId = 47110;
        private const int MirrorCommandId = 1;

        private SldWorks? _sw;
        private CommandManager? _commands;
        private int _cookie;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _sw = (SldWorks)ThisSW;
            _cookie = Cookie;
            _sw.SetAddinCallbackInfo2(0, this, _cookie);
            _commands = _sw.GetCommandManager(_cookie);
            AddCommands();
            return true;
        }

        public bool DisconnectFromSW()
        {
            RemoveCommands();
            if (_commands != null) Marshal.ReleaseComObject(_commands);
            if (_sw != null) Marshal.ReleaseComObject(_sw);
            _commands = null;
            _sw = null;
            // Release every remaining RCW so SOLIDWORKS can unload the add-in cleanly.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        // ---------------------------------------------------------------- commands

        private void AddCommands()
        {
            int errors = 0;
            CommandGroup group = _commands!.CreateCommandGroup2(GroupId, Title, "Unabhängig spiegeln",
                "Spiegelt Features als eigenständige Features", -1, true, ref errors);
            string[] icons = IconFactory.Create();
            group.IconList = icons;
            group.MainIconList = icons;

            int index = group.AddCommandItem2("Unabhängig spiegeln", -1,
                "Features gespiegelt und unabhängig neu aufbauen", "Unabhängig spiegeln", 0,
                nameof(OnMirror), nameof(OnMirrorEnable), MirrorCommandId,
                (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem));
            group.HasToolbar = true;
            group.HasMenu = true;
            group.Activate();

            foreach (swDocumentTypes_e type in new[] { swDocumentTypes_e.swDocPART, swDocumentTypes_e.swDocASSEMBLY })
            {
                CommandTab existing = _commands.GetCommandTab((int)type, Title);
                if (existing != null) _commands.RemoveCommandTab(existing);
                CommandTab tab = _commands.AddCommandTab((int)type, Title);
                CommandTabBox box = tab.AddCommandTabBox();
                box.AddCommands(new[] { group.get_CommandID(index) }, new[] { (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow });
            }
        }

        private void RemoveCommands()
        {
            if (_commands == null) return;
            foreach (swDocumentTypes_e type in new[] { swDocumentTypes_e.swDocPART, swDocumentTypes_e.swDocASSEMBLY })
            {
                CommandTab tab = _commands.GetCommandTab((int)type, Title);
                if (tab != null) _commands.RemoveCommandTab(tab);
            }
            _commands.RemoveCommandGroup2(GroupId, true);
        }

        /// <summary>Called by SOLIDWORKS by name (see AddCommandItem2).</summary>
        public void OnMirror()
        {
            if (_sw == null) return;
            var doc = (ModelDoc2)_sw.ActiveDoc;
            if (doc == null) return;
            try
            {
                new MirrorPage(_sw, doc).Show();
            }
            catch (Exception ex)
            {
                _sw.SendMsgToUser2("Spiegelwerk: " + ex.Message, (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        /// <summary>1 = enabled: only in parts and assemblies.</summary>
        public int OnMirrorEnable()
        {
            if (!(_sw?.ActiveDoc is ModelDoc2 doc)) return 0;
            int type = doc.GetType();
            return type == (int)swDocumentTypes_e.swDocPART || type == (int)swDocumentTypes_e.swDocASSEMBLY ? 1 : 0;
        }

        // ---------------------------------------------------------------- COM registration

        [ComRegisterFunction]
        public static void Register(Type t)
        {
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\SolidWorks\Addins\{{{t.GUID}}}"))
            {
                key.SetValue(null, 1, RegistryValueKind.DWord);
                key.SetValue("Title", Title);
                key.SetValue("Description", Description);
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"Software\SolidWorks\AddInsStartup\{{{t.GUID}}}"))
            {
                key.SetValue(null, 1, RegistryValueKind.DWord);
            }
        }

        [ComUnregisterFunction]
        public static void Unregister(Type t)
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\SolidWorks\Addins\{{{t.GUID}}}", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\SolidWorks\AddInsStartup\{{{t.GUID}}}", false);
        }
    }
}
