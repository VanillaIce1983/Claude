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
            var sm = (SelectionMgr)doc.SelectionManager;
            SelectData data = sm.CreateSelectData();
            data.Mark = mark;
            switch (item)
            {
                case SketchSegment s:
                    return s.Select4(append, data);
                case SketchPoint p:
                    return p.Select4(append, data);
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
}
