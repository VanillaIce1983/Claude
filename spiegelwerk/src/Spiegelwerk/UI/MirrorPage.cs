using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using Spiegelwerk.AssemblyContext;
using Spiegelwerk.Build;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.UI
{
    /// <summary>
    /// PropertyManager page "Unabhängig spiegeln". Selections are read in OnClose (they are
    /// cleared once the page is gone) and the job runs in AfterClose (modelling inside
    /// OnClose is not allowed while the page is still closing).
    /// </summary>
    [ComVisible(true)]
    public sealed class MirrorPage : IPropertyManagerPage2Handler9
    {
        private const int PlaneMark = 1;
        private const int ItemMark = 2;

        private const int GrpPlane = 1, SelPlane = 10, LblHelper = 11, CmbHelper = 12, NumOffset = 13;
        private const int GrpItems = 2, SelItems = 20, LblItems = 21;
        private const int GrpResult = 3, OptNewPart = 30, OptSamePart = 31, ChkDelete = 32, ChkInsert = 33, ChkMaterial = 34, ChkEquations = 35, LblSuffix = 36, TxtSuffix = 37;
        private const int GrpCheck = 4, ChkVerify = 40, ChkFix = 41, ChkStop = 42;

        private readonly SldWorks _sw;
        private readonly ModelDoc2 _doc;
        private readonly bool _isAssembly;
        private readonly SwMath _math;
        private readonly MirrorOptions _options = new MirrorOptions();

        private PropertyManagerPage2 _page = null!;
        private PropertyManagerPageSelectionbox _selPlane = null!, _selItems = null!;
        private PropertyManagerPageCombobox _cmbHelper = null!;
        private PropertyManagerPageNumberbox _numOffset = null!;
        private PropertyManagerPageOption? _optNew, _optSame;
        private PropertyManagerPageCheckbox? _chkDelete, _chkInsert;
        private PropertyManagerPageCheckbox _chkMaterial = null!, _chkEquations = null!, _chkVerify = null!, _chkFix = null!, _chkStop = null!;
        private PropertyManagerPageTextbox _txtSuffix = null!;

        // Harvested in OnClose.
        private bool _accepted;
        private Plane3? _plane;
        private readonly List<Feature> _features = new List<Feature>();
        private readonly List<Body2> _bodies = new List<Body2>();
        private readonly List<Component2> _components = new List<Component2>();

        public MirrorPage(SldWorks sw, ModelDoc2 doc)
        {
            _sw = sw;
            _doc = doc;
            _isAssembly = doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;
            _math = new SwMath(sw);
        }

        public void Show()
        {
            int errors = 0;
            int options = (int)(swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                                 swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton);
            _page = (PropertyManagerPage2)_sw.CreatePropertyManagerPage("Unabhängig spiegeln", options, this, ref errors);
            if (_page == null) throw new InvalidOperationException($"PropertyManager-Seite konnte nicht erzeugt werden (Fehler {errors}).");
            Build();
            _page.Show2(0);
            UpdateState();
        }

        private void Build()
        {
            int vis = (int)(swAddControlOptions_e.swControlOptions_Visible | swAddControlOptions_e.swControlOptions_Enabled);
            short left = (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int groupOpts = (int)(swAddGroupBoxOptions_e.swGroupBoxOptions_Visible | swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);

            _page.SetMessage3(
                "Baut die gewählten Features gespiegelt als eigenständige Features neu auf – ohne Spiegel-Feature und ohne Bezug zum Original.",
                (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible,
                (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand, "Spiegelwerk");

            // ---- mirror plane
            var g1 = (PropertyManagerPageGroup)_page.AddGroupBox(GrpPlane, "Spiegelebene", groupOpts);
            _selPlane = (PropertyManagerPageSelectionbox)g1.AddControl2(SelPlane, (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Ebene oder ebene Fläche", left, vis, "Ebene oder ebene Fläche, an der gespiegelt wird");
            _selPlane.SingleEntityOnly = true;
            _selPlane.Mark = PlaneMark;
            _selPlane.Height = 20;
            _selPlane.SetSelectionFilters(new[] { (int)swSelectType_e.swSelFACES, (int)swSelectType_e.swSelDATUMPLANES });

            g1.AddControl2(LblHelper, (short)swPropertyManagerPageControlType_e.swControlType_Label, "oder temporäre Ebene:", left, vis, "");
            _cmbHelper = (PropertyManagerPageCombobox)g1.AddControl2(CmbHelper, (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "Hilfsebene", left, vis, "Ebene ohne Feature im Modell: Standardebene mit Abstand");
            _cmbHelper.AddItems(new[] { "– Auswahl verwenden –", "YZ (Rechts) + Abstand in X", "XZ (Oben) + Abstand in Y", "XY (Vorne) + Abstand in Z" });
            _cmbHelper.CurrentSelection = 0;
            _numOffset = (PropertyManagerPageNumberbox)g1.AddControl2(NumOffset, (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Abstand", left, vis, "Abstand der Hilfsebene vom Ursprung");
            _numOffset.SetRange2((int)swNumberboxUnitType_e.swNumberBox_Length, -100, 100, true, 0.001, 0.01, 0.0001);
            _numOffset.Value = 0;

            // ---- items
            var g2 = (PropertyManagerPageGroup)_page.AddGroupBox(GrpItems, _isAssembly ? "Komponenten" : "Zu spiegelnde Elemente", groupOpts);
            _selItems = (PropertyManagerPageSelectionbox)g2.AddControl2(SelItems, (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Elemente", left, vis, _isAssembly ? "Zu spiegelnde Komponenten" : "Features, Skizzen, Ebenen, Achsen, Koordinatensysteme oder Volumenkörper");
            _selItems.Mark = ItemMark;
            _selItems.Height = 90;
            _selItems.SetSelectionFilters(_isAssembly
                ? new[] { (int)swSelectType_e.swSelCOMPONENTS }
                : new[]
                {
                    (int)swSelectType_e.swSelBODYFEATURES, (int)swSelectType_e.swSelSKETCHES, (int)swSelectType_e.swSelSOLIDBODIES,
                    (int)swSelectType_e.swSelDATUMPLANES, (int)swSelectType_e.swSelDATUMAXES, (int)swSelectType_e.swSelCOORDSYS,
                });
            g2.AddControl2(LblItems, (short)swPropertyManagerPageControlType_e.swControlType_Label,
                _isAssembly ? "Teile werden als Gegenstück neu aufgebaut, gespeichert und eingefügt." : "Leer lassen für das ganze Teil.", left, vis, "");

            // ---- result
            var g3 = (PropertyManagerPageGroup)_page.AddGroupBox(GrpResult, "Ergebnis", groupOpts);
            if (!_isAssembly)
            {
                _optNew = (PropertyManagerPageOption)g3.AddControl2(OptNewPart, (short)swPropertyManagerPageControlType_e.swControlType_Option,
                    "Neues Teil (Gegenstück)", left, vis, "Alle Features in einem neuen Teil neu aufbauen");
                _optSame = (PropertyManagerPageOption)g3.AddControl2(OptSamePart, (short)swPropertyManagerPageControlType_e.swControlType_Option,
                    "Im selben Teil", left, vis, "Die gespiegelten Features im aktuellen Teil anlegen");
                _optNew.Checked = true;
                _chkDelete = Check(g3, ChkDelete, "Original löschen", "Nur wenn alles fehlerfrei gespiegelt wurde und nichts anderes vom Original abhängt", false);
            }
            else
            {
                _chkInsert = Check(g3, ChkInsert, "In Baugruppe einfügen", "Gespiegeltes Teil an der gespiegelten Lage einfügen", true);
            }
            _chkMaterial = Check(g3, ChkMaterial, "Werkstoff übernehmen", "", true);
            _chkEquations = Check(g3, ChkEquations, "Gleichungen übernehmen", "Globale Variablen und Gleichungen kopieren", true);
            g3.AddControl2(LblSuffix, (short)swPropertyManagerPageControlType_e.swControlType_Label, "Namenszusatz:", left, vis, "");
            _txtSuffix = (PropertyManagerPageTextbox)g3.AddControl2(TxtSuffix, (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "Zusatz", left, vis, "Wird an Feature-Namen (selbes Teil) bzw. Dateinamen (Baugruppe) angehängt");
            _txtSuffix.Text = _options.NameSuffix;

            // ---- checks
            var g4 = (PropertyManagerPageGroup)_page.AddGroupBox(GrpCheck, "Prüfung und Bezüge", groupOpts);
            _chkVerify = Check(g4, ChkVerify, "Jeden Schritt prüfen (Volumen, Schwerpunkt)",
                "Vergleicht jedes neue Feature mit dem gespiegelten Original und korrigiert Richtungen", true);
            _chkFix = Check(g4, ChkFix, "Fehlende Skizzenbezüge ersetzen",
                "Bezüge, die es im Ziel nicht gibt (Ursprung, Kanten), werden durch fixierte Hilfsgeometrie an gespiegelter Lage ersetzt statt weggelassen", true);
            _chkStop = Check(g4, ChkStop, "Beim ersten Fehler abbrechen", "", false);
        }

        private static PropertyManagerPageCheckbox Check(PropertyManagerPageGroup g, int id, string caption, string tip, bool value)
        {
            var c = (PropertyManagerPageCheckbox)g.AddControl2(id, (short)swPropertyManagerPageControlType_e.swControlType_Checkbox, caption,
                (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge,
                (int)(swAddControlOptions_e.swControlOptions_Visible | swAddControlOptions_e.swControlOptions_Enabled), tip);
            c.Checked = value;
            return c;
        }

        /// <summary>OK only with a mirror plane (and, in an assembly, at least one component).</summary>
        private void UpdateState()
        {
            bool hasPlane = _selPlane.ItemCount > 0 || _cmbHelper.CurrentSelection > 0;
            bool hasItems = !_isAssembly || _selItems.ItemCount > 0;
            _page.EnableButton((int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok, hasPlane && hasItems);
            if (_chkDelete != null && _optSame != null) ((PropertyManagerPageControl)_chkDelete).Enabled = _optSame.Checked;
            ((PropertyManagerPageControl)_numOffset).Enabled = _cmbHelper.CurrentSelection > 0;
        }

        // ---------------------------------------------------------------- closing

        public void OnClose(int reason)
        {
            if (reason != (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay) return;
            _accepted = true;
            _plane = ReadPlane();
            ReadItems();

            _options.Target = _optSame != null && _optSame.Checked ? MirrorTarget.SamePart : MirrorTarget.NewPart;
            _options.DeleteOriginal = _chkDelete != null && _chkDelete.Checked && _options.Target == MirrorTarget.SamePart;
            _options.InsertIntoAssembly = _chkInsert == null || _chkInsert.Checked;
            _options.CopyMaterial = _chkMaterial.Checked;
            _options.CopyEquations = _chkEquations.Checked;
            _options.VerifySteps = _chkVerify.Checked;
            _options.ExternalReferences = _chkFix.Checked ? ExternalReferenceHandling.Fix : ExternalReferenceHandling.Drop;
            _options.StopOnError = _chkStop.Checked;
            // Used in file names (assembly) and feature names (same part): keep it to characters both accept.
            char[] invalid = System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '@' }).ToArray();
            string suffix = new string((_txtSuffix.Text ?? "").Trim().Where(ch => Array.IndexOf(invalid, ch) < 0).ToArray());
            _options.NameSuffix = suffix.Length > 0 ? suffix : "_gespiegelt";
        }

        public void AfterClose()
        {
            if (!_accepted) return;
            if (_plane == null)
            {
                Message("Keine gültige Spiegelebene gewählt (ebene Fläche oder Referenzebene).", swMessageBoxIcon_e.swMbStop);
                return;
            }
            try
            {
                MirrorReport report;
                if (_isAssembly)
                {
                    report = new MirrorReport();
                    var asm = new AssemblyMirror(_sw);
                    foreach (Component2 c in _components)
                    {
                        // One component failing must not cost the report of the ones already saved and inserted.
                        string name = c.Name2;
                        try
                        {
                            MirrorReport part = asm.Run((AssemblyDoc)_doc, c, _plane.Value, _options).Report;
                            report.Append(part, name);
                            if (part.Aborted && _options.StopOnError) break;
                        }
                        catch (Exception ex)
                        {
                            report.Entries.Add(new ReportEntry(name, "Komponente", ReportStatus.Failed, $"nicht gespiegelt – {ex.Message}"));
                            if (_options.StopOnError)
                            {
                                report.Aborted = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    var request = new MirrorRequest(_doc, _plane.Value, _options);
                    request.Features.AddRange(_features);
                    request.Bodies.AddRange(_bodies);
                    report = new MirrorJob(_sw).Run(request).Report;
                }
                ShowReport(report);
            }
            catch (Exception ex)
            {
                Message("Spiegeln fehlgeschlagen: " + ex.Message, swMessageBoxIcon_e.swMbStop);
            }
        }

        private Plane3? ReadPlane()
        {
            var sm = (SelectionMgr)_doc.SelectionManager;
            if (sm.GetSelectedObjectCount2(PlaneMark) > 0)
            {
                object o = sm.GetSelectedObject6(1, PlaneMark);
                var comp = sm.GetSelectedObjectsComponent4(1, PlaneMark) as Component2;
                Plane3? p = o switch
                {
                    Face2 f => TopologyCapture.PlaneOfFace(f),
                    Feature feat when feat.GetTypeName2() == "RefPlane" => _math.PlaneOf((RefPlane)feat.GetSpecificFeature2()),
                    RefPlane rp => _math.PlaneOf(rp),
                    _ => null,
                };
                // Geometry of a component's face or plane comes in the component's coordinates.
                if (p.HasValue && comp != null && _isAssembly)
                {
                    Affine t = _math.ToAffine(comp.Transform2);
                    p = new Plane3(t.Apply(p.Value.Origin), t.ApplyVector(p.Value.Normal));
                }
                return p;
            }
            int helper = _cmbHelper.CurrentSelection;
            if (helper <= 0) return null;
            Vec3 n = helper == 1 ? Vec3.UnitX : helper == 2 ? Vec3.UnitY : Vec3.UnitZ;
            return new Plane3(n * _numOffset.Value, n);
        }

        private void ReadItems()
        {
            var sm = (SelectionMgr)_doc.SelectionManager;
            int n = sm.GetSelectedObjectCount2(ItemMark);
            for (int i = 1; i <= n; i++)
            {
                switch (sm.GetSelectedObject6(i, ItemMark))
                {
                    case Component2 c:
                        _components.Add(c);
                        break;
                    case Body2 b:
                        _bodies.Add(b);
                        break;
                    case Feature f:
                        _features.Add(f);
                        break;
                }
            }
        }

        private void ShowReport(MirrorReport report)
        {
            var frame = (SolidWorks.Interop.sldworks.Frame)_sw.Frame();
            using var form = new ReportForm(report);
            form.ShowDialog(new WindowWrapper(new IntPtr(frame.GetHWndx64())));
        }

        private void Message(string text, swMessageBoxIcon_e icon) =>
            _sw.SendMsgToUser2(text, (int)icon, (int)swMessageBoxBtn_e.swMbOk);

        // ---------------------------------------------------------------- events

        public bool OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText)
        {
            // Only planar faces make a mirror plane.
            if (Id == SelPlane && Selection is Face2 f) return TopologyCapture.PlaneOfFace(f).HasValue;
            // Sub-assemblies, virtual and suppressed components cannot be mirrored: refuse them right away.
            if (Id == SelItems && Selection is Component2 c) return AssemblyMirror.CheckComponent(c) == null;
            return true;
        }

        public void OnSelectionboxListChanged(int Id, int Count) => UpdateState();
        public void OnComboboxSelectionChanged(int Id, int Item) => UpdateState();
        public void OnOptionCheck(int Id) => UpdateState();

        public void AfterActivation() { }
        public bool OnHelp() => true;
        public bool OnPreviousPage() => true;
        public bool OnNextPage() => true;
        public bool OnPreview() => true;
        public void OnWhatsNew() { }
        public void OnUndo() { }
        public void OnRedo() { }
        public bool OnTabClicked(int Id) => true;
        public void OnGroupExpand(int Id, bool Expanded) { }
        public void OnGroupCheck(int Id, bool Checked) { }
        public void OnCheckboxCheck(int Id, bool Checked) { }
        public void OnButtonPress(int Id) { }
        public void OnTextboxChanged(int Id, string Text) { }
        public void OnNumberboxChanged(int Id, double Value) { }
        public void OnComboboxEditChanged(int Id, string Text) { }
        public void OnListboxSelectionChanged(int Id, int Item) { }
        public void OnSelectionboxFocusChanged(int Id) { }
        public void OnSelectionboxCalloutCreated(int Id) { }
        public void OnSelectionboxCalloutDestroyed(int Id) { }
        public int OnActiveXControlCreated(int Id, bool Status) => 0;
        public void OnSliderPositionChanged(int Id, double Value) { }
        public void OnSliderTrackingCompleted(int Id, double Value) { }
        public bool OnKeystroke(int Wparam, int Message, int Lparam, int Id) => false;
        public void OnPopupMenuItem(int Id) { }
        public void OnPopupMenuItemUpdate(int Id, ref int retval) { }
        public void OnGainedFocus(int Id) { }
        public void OnLostFocus(int Id) { }
        public int OnWindowFromHandleControlCreated(int Id, bool Status) => 0;
        public void OnListboxRMBUp(int Id, int PosX, int PosY) { }
        public void OnNumberBoxTrackingCompleted(int Id, double Value) { }
    }
}
