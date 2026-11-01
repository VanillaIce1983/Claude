using System;
using SolidWorks.Interop.sldworks;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Selecting objects with a mark. Many feature methods of the API take their input from
    /// the current selection and tell the inputs apart by mark (fillet edges 1, revolve axis
    /// 4/16, extrude end reference 1, reference plane inputs 0/1/2 ...).
    /// </summary>
    internal static class SwSelect
    {
        public static bool Select(ModelDoc2 doc, object item, int mark, bool append)
        {
            var sm = (SelectionMgr)(object)doc.SelectionManager;
            SelectData data = sm.CreateSelectData();
            data.Mark = mark;
            switch (item)
            {
                case SketchSegment s:
                    return s.Select4(append, data);
                case SketchPoint p:
                    return p.Select4(append, data);
                // Before contours (and features): a region is asked for first, as when the feature data is read.
                case SketchRegion g:
                    return g.Select2(append, data);
                case Feature f:
                    return f.Select2(append, mark);
                case SketchContour c:
                    return c.Select2(append, data);
                case Body2 b:
                    return b.Select2(append, data);
                case Entity e:
                    // Edge, Face2, Vertex
                    return e.Select4(append, data);
                default:
                    throw new ArgumentException($"Objekt vom Typ {item.GetType().Name} kann nicht ausgewählt werden.");
            }
        }

        public static void Clear(ModelDoc2 doc) => doc.ClearSelection2(true);
    }

    /// <summary>
    /// Contour selection (ISelectionMgr.EnableContourSelection) switched on for selecting
    /// contours and sketch regions as in the API examples. The setting belongs to the user's
    /// session: Dispose restores the value it had, it does not force it off.
    /// </summary>
    internal sealed class ContourSelectionScope : IDisposable
    {
        private readonly SelectionMgr? _sm;
        private readonly bool _before;
        private bool _disposed;

        /// <summary>Never throws: if the switch cannot be read or set, the selection goes ahead without it.</summary>
        public ContourSelectionScope(ModelDoc2 doc)
        {
            try
            {
                var sm = (SelectionMgr)(object)doc.SelectionManager;
                _before = sm.EnableContourSelection;
                sm.EnableContourSelection = true;
                _sm = sm;
            }
            catch (Exception)
            {
                _sm = null;
            }
        }

        public void Dispose()
        {
            if (_disposed || _sm == null) return;
            _disposed = true;
            try
            {
                _sm.EnableContourSelection = _before;
            }
            catch (Exception)
            {
                // Restoring a session setting must not turn a finished step into a failure.
            }
        }
    }
}
