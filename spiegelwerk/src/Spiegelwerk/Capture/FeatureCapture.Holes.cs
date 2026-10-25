using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>Hole Wizard holes: hole data, hole centres and the position sketch.</summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>Sketch name → the hole it belongs to and whether it is the position sketch.</summary>
        private Dictionary<string, (string owner, bool position)>? _holeParts;

        /// <summary>
        /// The hole a sketch belongs to, if it is one of the two sketches of a Hole Wizard hole
        /// (position and profile). Those are not rebuilt on their own: the new hole brings its
        /// own, and the position sketch is written into that.
        /// </summary>
        private PartOfFeatureRecord? HolePart(Feature sketch, string type)
        {
            if (_holeParts == null)
            {
                _holeParts = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
                foreach (Feature hole in SwDoc.TopLevelFeatures(_doc).Where(x => x.GetTypeName2() == "HoleWzd"))
                {
                    string? position = PositionSketchName(hole);
                    foreach (Feature sub in SwDoc.SubFeatures(hole).Where(SwDoc.IsSketch))
                        _holeParts[sub.Name] = (hole.Name, sub.Name == position);
                }
            }
            return _holeParts.TryGetValue(sketch.Name, out var part) ? new PartOfFeatureRecord(sketch.Name, type, part.owner, part.position) : null;
        }

        private string? PositionSketchName(Feature hole)
        {
            try
            {
                return SwHole.PositionSketch(_doc, hole)?.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A sketch of a hole that is not rebuilt (not in the job, unsupported, skipped): the
        /// position sketch becomes an ordinary sketch again, so its points stay available; the
        /// profile sketch means nothing without the hole and is left out.
        /// </summary>
        public FeatureRecord CaptureOnItsOwn(Feature f, PartOfFeatureRecord part)
        {
            string type = f.GetTypeName2();
            if (!part.IsPositionSketch)
                return new UnsupportedRecord(f.Name, type, $"Profilskizze der Bohrung „{part.Owner}“, die nicht gespiegelt wird – entfällt mit ihr.", false);
            if (type != "ProfileFeature")
                return new UnsupportedRecord(f.Name, type, "3D-Skizzen werden noch nicht gespiegelt.", false);
            try
            {
                return new SketchRecord(f.Name, ReadSketch(f));
            }
            catch (NotSupportedException ex)
            {
                return new UnsupportedRecord(f.Name, type, ex.Message, false);
            }
        }

        private HoleRecord CaptureHole(Feature f, string type, WizardHoleFeatureData2 d)
        {
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            Feature position;
            var points = new List<Vec3>();
            int holeType, generic, standard, fastener, end;
            string size, threadClass;
            bool reverse;
            GeometryRef? face, endReference = null;
            Dictionary<string, double> values;
            var notes = new List<string>();
            try
            {
                holeType = d.Type;
                generic = HoleTypes.GenericOf(holeType)
                          ?? throw new NotSupportedException($"Bohrungstyp {holeType} ist unbekannt (neuere SOLIDWORKS-Version?).");
                if (generic == HoleTypes.Legacy)
                    throw new NotSupportedException("Bohrungen im alten Format (Legacy) werden nicht unterstützt.");
                if (HoleTypes.IsSlot(generic))
                    throw new NotSupportedException("Langlöcher des Bohrungsassistenten werden nicht unterstützt (Ausrichtung über die API nicht lesbar).");

                if (!(d.GetSketchPoints() is object[] pts) || pts.Length == 0 || !(pts[0] is SketchPoint first))
                    throw new NotSupportedException("Bohrung ohne lesbare Positionspunkte.");
                Sketch sketch = first.GetSketch();
                if (sketch.Is3D())
                    throw new NotSupportedException("Bohrungen mit 3D-Positionsskizze (auf mehreren oder gekrümmten Flächen) werden nicht unterstützt.");
                position = (Feature)sketch;
                Frame3 frame = _math.SketchFrame(sketch);
                foreach (SketchPoint p in pts.OfType<SketchPoint>()) points.Add(frame.ToWorld(p.X, p.Y));

                standard = d.Standard2;
                fastener = d.FastenerType2;
                size = d.FastenerSize ?? "";
                end = d.EndCondition;
                reverse = d.ReverseDirection;
                threadClass = d.ThreadClass ?? "";
                face = ToGeometryRef(d.Face);
                if (face == null || (face.Kind != GeometryRefKind.Face && face.Kind != GeometryRefKind.Plane))
                    throw new NotSupportedException("Platzierungsfläche der Bohrung nicht lesbar.");
                if (ExtrudeSide.NeedsReference((EndCondition)end))
                {
                    endReference = ToGeometryRef(d.GetEndConditionReference(out int _));
                    if (endReference == null || endReference.Kind == GeometryRefKind.Unknown)
                        throw new NotSupportedException($"Bezug der Endbedingung {(EndCondition)end} ist im Original nicht lesbar.");
                }
                values = SwHole.Read(d);
                if (d.FeatureScope && !d.AutoSelect)
                    notes.Add("Der Feature-Bereich (ausgewählte Körper) wird nicht übernommen.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }

            // Read outside the selection access: ReadSketch may move the rollback bar itself.
            var r = new HoleRecord(f.Name, type, position.Name, ReadSketch(position))
            {
                HoleType = holeType,
                GenericType = generic,
                Standard = standard,
                FastenerType = fastener,
                Size = size,
                End = (EndCondition)end,
                Reverse = reverse,
                ThreadClass = threadClass,
                Face = face,
                EndReference = endReference,
            };
            r.Points.AddRange(points);
            foreach (var kv in values) r.Values[kv.Key] = kv.Value;
            r.Notes.AddRange(notes);
            return r;
        }
    }
}
