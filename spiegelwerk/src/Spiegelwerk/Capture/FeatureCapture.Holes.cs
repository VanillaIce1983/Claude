using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>Hole Wizard holes: hole data, hole centres and the position sketch; the parts of features (sketches of both kinds of hole).</summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>Name of a part → its owner and the owner's kind; built on the first sketch.</summary>
        private Dictionary<string, (string owner, PartOwnerKind kind)>? _ownedParts;

        /// <summary>Hole name → name of its position sketch, read only when needed.</summary>
        private readonly Dictionary<string, string?> _positionSketches = new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// The owner a feature is part of, if it does not come into being on its own: the
        /// sketches of a Hole Wizard hole (position and profile) or an Advanced Hole – the new
        /// hole brings its own, and the position sketch is written into that –, and in sheet metal
        /// the SheetMetal of a base flange and the sketches and own corner features of a flat
        /// pattern (<see cref="SheetMetalTree.Owners"/>).
        /// </summary>
        private PartOfFeatureRecord? OwnedPart(Feature f, string type)
        {
            // Sheet metal: the SheetMetal of a base flange, the sketches and own corners of a flat pattern.
            if (_sheetMetal.Owners.TryGetValue(f.Name, out var sheetMetal))
                return new PartOfFeatureRecord(f.Name, type, sheetMetal.owner, sheetMetal.kind);
            if (type != "ProfileFeature" && type != "3DProfileFeature") return null;
            // Tree walk only – no selection access, so runs without holes cost nothing extra.
            if (_ownedParts == null)
            {
                _ownedParts = new Dictionary<string, (string, PartOwnerKind)>(StringComparer.Ordinal);
                foreach (Feature hole in SwDoc.TopLevelFeatures(_doc).Where(IsPositionedHole))
                    foreach (Feature sub in SwDoc.SubFeatures(hole).Where(SwDoc.IsSketch))
                        _ownedParts[sub.Name] = (hole.Name, PartOwnerKind.Hole);
            }
            return _ownedParts.TryGetValue(f.Name, out var owner) ? new PartOfFeatureRecord(f.Name, type, owner.owner, owner.kind) : null;
        }

        /// <summary>
        /// Hole Wizard hole or Advanced Hole. The type name of the latter is not documented:
        /// features with "Hole" in it are asked for their definition – only those, so the
        /// tree walk stays cheap.
        /// </summary>
        private static bool IsPositionedHole(Feature f)
        {
            string type = f.GetTypeName2();
            if (type == "HoleWzd") return true;
            if (type.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) < 0) return false;
            try
            {
                return f.GetDefinition() is AdvancedHoleFeatureData;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The sketch the holes sit on, for either kind of hole.</summary>
        private Feature? PositionSketchOf(Feature hole) =>
            hole.GetDefinition() is AdvancedHoleFeatureData ? SwAdvancedHole.PositionSketch(hole) : SwHole.PositionSketch(_doc, hole);

        private bool IsPositionSketch(string hole, string sketch)
        {
            if (!_positionSketches.TryGetValue(hole, out string? position))
            {
                try
                {
                    position = SwDoc.FeatureByName(_doc, hole) is Feature f ? PositionSketchOf(f)?.Name : null;
                }
                catch (Exception)
                {
                    position = null;
                }
                _positionSketches[hole] = position;
            }
            return position == sketch;
        }

        /// <summary>
        /// A part whose owner is not rebuilt (not in the job, unsupported, skipped). The position
        /// sketch of a hole becomes an ordinary sketch again, so its points stay available; every
        /// other part means nothing without its owner and is left out (<see cref="PartOfFeatureRecord.LeftOut"/>).
        /// A 3D sub-sketch of a hole can only be a position sketch (on several or curved faces) –
        /// profiles are always 2D.
        /// </summary>
        public FeatureRecord CaptureOnItsOwn(Feature f, PartOfFeatureRecord part)
        {
            string type = f.GetTypeName2();
            if (!part.OwnerHasPositionSketch) return new UnsupportedRecord(f.Name, type, part.LeftOut, false);
            if (type != "ProfileFeature")
                return new UnsupportedRecord(f.Name, type, "3D-Skizzen werden noch nicht gespiegelt.", false);
            if (!IsPositionSketch(part.Owner, f.Name))
                return new UnsupportedRecord(f.Name, type, part.LeftOut, false);
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
                face = ToGeometryRef((object)d.Face);
                if (face == null || (face.Kind != GeometryRefKind.Face && face.Kind != GeometryRefKind.Plane))
                    throw new NotSupportedException("Platzierungsfläche der Bohrung nicht lesbar.");
                if (ExtrudeSide.NeedsReference((EndCondition)end))
                {
                    endReference = ToGeometryRef((object)d.GetEndConditionReference(out int _));
                    if (endReference == null || endReference.Kind == GeometryRefKind.Unknown)
                        throw new NotSupportedException($"Bezug der Endbedingung {(EndCondition)end} ist im Original nicht lesbar.");
                }
                values = SwHole.Read(d);
                NoteScope(notes, d.FeatureScope, d.AutoSelect);
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
