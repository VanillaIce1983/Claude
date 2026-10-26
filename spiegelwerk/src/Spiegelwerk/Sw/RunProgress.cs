using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Planning;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// The SOLIDWORKS progress bar in the status bar: phase and feature of the run. Esc comes
    /// back from UpdateProgress (swUpdateProgressError_UserCancel) and is only looked at
    /// between two features; the user confirms before the run stops. Without a bar the run
    /// goes on as before and cannot be stopped. Ended in Dispose, also on errors.
    /// </summary>
    internal sealed class RunProgress : IDisposable
    {
        private readonly SldWorks _sw;
        private UserProgressBar? _bar;
        private RunSteps _steps = new RunSteps(0, false);

        public RunProgress(SldWorks sw)
        {
            _sw = sw;
        }

        public void Start(int features, bool measure)
        {
            End();
            _steps = new RunSteps(features, measure);
            try
            {
                if (_steps.Total > 0 && _sw.GetUserProgressBar(out UserProgressBar bar) && bar != null && bar.Start(0, _steps.Total, RunSteps.Title("", "")))
                    _bar = bar;
            }
            catch (Exception)
            {
                // No bar: the run does not depend on it.
            }
        }

        /// <summary>Shows what is worked on next.</summary>
        public void Show(string phase, string feature = "")
        {
            if (_bar == null) return;
            try
            {
                _bar.UpdateTitle(RunSteps.Title(phase, feature));
            }
            catch (Exception)
            {
                _bar = null;
            }
        }

        /// <summary>
        /// One feature done. True if the user pressed Esc meanwhile and confirms stopping the run
        /// here – never after the last step.
        /// </summary>
        public bool CancelAfterStep()
        {
            bool more = _steps.Advance();
            if (_bar == null) return false;
            int status;
            try
            {
                status = _bar.UpdateProgress(_steps.Position);
            }
            catch (Exception)
            {
                _bar = null;
                return false;
            }
            if (!more || status != (int)swUpdateProgressError_e.swUpdateProgressError_UserCancel) return false;
            return _sw.SendMsgToUser2("Spiegeln hier abbrechen? Was bis jetzt aufgebaut wurde, bleibt erhalten.",
                (int)swMessageBoxIcon_e.swMbQuestion, (int)swMessageBoxBtn_e.swMbYesNo) == (int)swMessageBoxResult_e.swMbHitYes;
        }

        public void Dispose() => End();

        private void End()
        {
            if (_bar == null) return;
            try
            {
                _bar.End();
            }
            catch (Exception)
            {
                // Nothing left to restore.
            }
            _bar = null;
        }
    }
}
