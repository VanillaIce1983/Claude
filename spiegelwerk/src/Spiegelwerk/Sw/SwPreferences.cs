using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Switches off user settings that interfere with programmatic modelling and restores
    /// them afterwards, even when the job fails:
    ///  - "Input dimension value": every AddDimension2 would otherwise open the Modify dialog
    ///    and block the API call until someone clicks it.
    ///  - automatic relations and inference: SOLIDWORKS would add horizontal/coincident
    ///    relations of its own while geometry is created.
    ///  - over-defining dimensions: no "make driven?" prompt; such a dimension becomes driven.
    /// </summary>
    internal sealed class SwPreferences : IDisposable
    {
        private readonly SldWorks _sw;
        private readonly List<(swUserPreferenceToggle_e pref, bool value)> _saved = new List<(swUserPreferenceToggle_e, bool)>();

        public SwPreferences(SldWorks sw)
        {
            _sw = sw;
            Set(swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            Set(swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
            Set(swUserPreferenceToggle_e.swSketchInference, false);
            Set(swUserPreferenceToggle_e.swSketchOverdefiningDimsPromptToSetState, false);
            Set(swUserPreferenceToggle_e.swSketchOverdefiningDimsSetDrivenByDefault, true);
        }

        private void Set(swUserPreferenceToggle_e pref, bool value)
        {
            _saved.Add((pref, _sw.GetUserPreferenceToggle((int)pref)));
            _sw.SetUserPreferenceToggle((int)pref, value);
        }

        public void Dispose()
        {
            for (int i = _saved.Count - 1; i >= 0; i--)
                _sw.SetUserPreferenceToggle((int)_saved[i].pref, _saved[i].value);
            _saved.Clear();
        }
    }

    /// <summary>
    /// Fast, literal sketch creation: AddToDB writes entities straight into the sketch
    /// database (no snapping to whatever is near on screen, no inferred relations),
    /// DisplayWhenAdded off avoids a redraw per entity.
    /// </summary>
    internal sealed class SketchInputScope : IDisposable
    {
        private readonly SketchManager _sm;
        private readonly bool _addToDb;
        private readonly bool _display;

        public SketchInputScope(SketchManager sm)
        {
            _sm = sm;
            _addToDb = sm.AddToDB;
            _display = sm.DisplayWhenAdded;
            sm.AddToDB = true;
            sm.DisplayWhenAdded = false;
        }

        public void Dispose()
        {
            _sm.AddToDB = _addToDb;
            _sm.DisplayWhenAdded = _display;
        }
    }

    /// <summary>Freezes the FeatureManager tree and the graphics while a job runs.</summary>
    internal sealed class UpdateFreeze : IDisposable
    {
        private readonly FeatureManager _fm;
        private readonly ModelView? _view;
        private readonly bool _tree;
        private readonly bool _graphics;

        public UpdateFreeze(ModelDoc2 doc)
        {
            _fm = doc.FeatureManager;
            _tree = _fm.EnableFeatureTree;
            _fm.EnableFeatureTree = false;
            _view = doc.ActiveView as ModelView;
            if (_view != null)
            {
                _graphics = _view.EnableGraphicsUpdate;
                _view.EnableGraphicsUpdate = false;
            }
        }

        public void Dispose()
        {
            _fm.EnableFeatureTree = _tree;
            if (_view != null) _view.EnableGraphicsUpdate = _graphics;
        }
    }
}
