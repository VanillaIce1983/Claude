using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Forming tool (A4 (2) §5, P10, P11, blech-plan S9). The tool part is placed unmirrored; where its instances stand,
    /// which side they protrude to and how they are turned is decided by its own check, always (blech-plan §0):
    ///  1. the position sketch is written on the mirrored placement face (never a plane; separate body: on a mirrored
    ///     body) and selected at mark 0 for InsertFormToolFeature, which puts an instance at each point and absorbs it
    ///     (B67); where that does not place it, the face is picked at the first point and the position sketch the tool
    ///     brings is written afterwards (variant A, as for holes) – not for a turned copy once the sketch placed the tool:
    ///     only the angle differs then, and that sense counts as not placed (B68);
    ///  2. the probe – angle 0, flip off, the default configuration – is measured: the side its form stands on against the
    ///     original's (else flip on), then the rotation ψ about each insertion axis that puts the mirrored samples of the
    ///     original on its faces (first guesses from the orientation sketches);
    ///  3. placed again turned by ψ – in place where the 2025 data answer, otherwise made anew – in the rotation sense
    ///     found earlier in the run, else + then −, and checked once more: every sample within 10 µm. A turn in place that
    ///     fits in neither sense is made anew (B69); a sense SOLIDWORKS does not place leaves the other its try.
    /// A tool that fits no rotation is tried with the other configurations of its file (A4 P11); none fits: „Werkzeug
    /// passt nicht“. Everything a failed attempt made is removed before the next.
    /// </summary>
    internal sealed class FormToolBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is FormToolRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (FormToolRecord)record;
            var notes = new List<string>();
            if (!File.Exists(r.ToolPath)) return Fail(FormToolPlacement.ToolMissing(r.ToolPath), notes);
            if (r.Points.Count == 0 || r.ShapeSamples.Count == 0 || r.ShapeSamples.Count != r.SampleInstance.Count ||
                r.SampleInstance.Any(i => i < 0 || i >= r.Points.Count))
                return Fail(FormToolPlacement.ShapeUnreadable, notes);
            if (r.Face?.Face == null) return Fail(FormToolPlacement.FaceNotFound, notes);
            var placement = new Placement(ctx, r, notes);
            if (placement.FaceNow(notes) == null) return Fail(placement.FaceMissing!, notes);
            return placement.Run();
        }

        /// <summary>What was placed in the end.</summary>
        private sealed class Done
        {
            public Done(Feature feature, Feature? positionSketch, string configuration, bool flip, double angle)
            {
                Feature = feature;
                PositionSketch = positionSketch;
                Configuration = configuration;
                Flip = flip;
                Angle = angle;
            }

            public Feature Feature { get; }
            public Feature? PositionSketch { get; }
            public string Configuration { get; }
            public bool Flip { get; }
            public double Angle { get; }
        }

        /// <summary>One placement of one tool: the attempts, what they left, what is said about them.</summary>
        private sealed class Placement
        {
            private readonly BuildContext _ctx;
            private readonly FormToolRecord _r;
            private readonly ModelDoc2 _doc;
            private readonly List<string> _notes;
            private readonly List<string> _info = new List<string>();
            private readonly List<string> _tried = new List<string>();
            private readonly HashSet<string> _before;
            private readonly List<Vec3> _centres;
            private readonly List<Vec3> _samples;
            private readonly List<Line3> _axes;
            private readonly List<Vec3> _orientation;
            private readonly Vec3 _normal;
            private readonly Plane3 _plane;

            /// <summary>The position sketch is selected for the call (B67); false once only the face placed the tool.</summary>
            private bool _bySketch = true;

            /// <summary>The selected position sketch placed the tool at least once (B67).</summary>
            private bool _sketchPlaced;

            /// <summary>SOLIDWORKS placed the tool at least once.</summary>
            private bool _inserted;

            /// <summary>What a removal left behind: nothing more is made.</summary>
            private List<string>? _left;

            /// <summary>What writing the last position sketch had to say.</summary>
            private List<string> _sketchNotes = new List<string>();

            /// <summary>The position sketch of the last tool placed (the one selected, or the one it brought).</summary>
            private Feature? _positionSketch;

            public Placement(BuildContext ctx, FormToolRecord r, List<string> notes)
            {
                _ctx = ctx;
                _r = r;
                _doc = ctx.Doc;
                _notes = notes;
                _before = SwSheetMetal.Snapshot(ctx.Doc).Names;
                _centres = r.Points.Select(ctx.Mirror.Apply).ToList();
                // Outward stays outward: the mirrored normal is the outward normal of the mirrored face.
                _normal = ctx.Mirror.ApplyVector(r.PlacementNormal).Normalized();
                _plane = new Plane3(_centres[0], _normal);
                _samples = r.ShapeSamples.Select(ctx.Mirror.Apply).ToList();
                _axes = r.SampleInstance.Select(i => new Line3(_centres[i], _normal)).ToList();
                _orientation = r.OrientationPoints.Select(ctx.Mirror.Apply).ToList();
                // The position sketch goes on the mirrored placement face (K7), its points are the instances.
                ctx.SketchSupportFaces[r.PositionSketchName] = r.Face!.Face!;
                ctx.PointSketches.Add(r.PositionSketchName);
            }

            /// <summary>Why the placement face is not usable; null if it is.</summary>
            public string? FaceMissing { get; private set; }

            /// <summary>The mirrored placement face as it is now – a face, never a plane; separate body: on a mirrored body.</summary>
            public Face2? FaceNow(List<string>? notes = null)
            {
                Face2? face = _ctx.Topology.FindFace(_r.Face!.Face!.Mirror(_ctx.Mirror), notes ?? new List<string>());
                FaceMissing = face == null ? FormToolPlacement.FaceNotFound
                    : _ctx.SeparateBody && !((object)face.GetBody() is Body2 body && _ctx.IsOwnBody(body)) ? FormToolPlacement.FaceNotOwn
                    : null;
                return FaceMissing == null ? face : null;
            }

            public BuildResult Run()
            {
                List<string> configurations = FormToolPlacement.ConfigurationCandidates(_r.ToolConfiguration, SwFormTool.ConfigurationNames(_ctx.Sw, _r.ToolPath));
                Done? done = null;
                foreach (string configuration in configurations)
                {
                    done = TryConfiguration(configuration);
                    if (done != null || _left != null) break;
                }
                if (done == null)
                {
                    List<string> left = _left ?? FlangeSupport.RemoveMade(_ctx, _before);
                    _ctx.Sketches.Remove(_r.PositionSketchName);
                    _notes.AddRange(_tried);
                    if (left.Count > 0) _notes.Add(FlangeSupport.LeftBehind(left));
                    return Fail(_inserted ? FormToolPlacement.NoFit : FormToolPlacement.NotCreated, _notes);
                }
                return Finish(done);
            }

            /// <summary>
            /// One configuration: the probe without and – if its form stands on the other side, or the side tells nothing and
            /// no rotation fits – with flip; then turned by the rotation found. Null if it does not fit.
            /// </summary>
            private Done? TryConfiguration(string configuration)
            {
                foreach (bool flip in new[] { false, true })
                {
                    Feature? probe = Place(configuration, flip, 0);
                    if (_left != null) return null;
                    if (probe == null)
                    {
                        // What this call did not place, the other flip will not either.
                        _tried.Add(FormToolPlacement.Tried(configuration, flip, FormToolPlacement.NotInserted));
                        return null;
                    }
                    int side = FormToolPlacement.ProtrusionSide(SwFormTool.ShapePoints(probe), _plane, _r.Thickness);
                    bool sideKnown = _r.ProtrusionSide != 0 && side != 0;
                    if (sideKnown && side != _r.ProtrusionSide)
                    {
                        _tried.Add(FormToolPlacement.Tried(configuration, flip, FormToolPlacement.OtherSide));
                        if (!Remove()) return null;
                        continue;
                    }
                    double? psi = Register(probe);
                    if (psi == null)
                    {
                        _tried.Add(FormToolPlacement.Tried(configuration, flip, FormToolPlacement.NoRotation));
                        if (!Remove()) return null;
                        // The side was right: the other flip cannot fit either.
                        if (sideKnown) return null;
                        continue;
                    }
                    return Turn(probe, configuration, flip, psi.Value);
                }
                return null;
            }

            /// <summary>The rotation that carries the probe onto the mirrored original (<see cref="FormToolPlacement.Register3D"/>); null if none.</summary>
            private double? Register(Feature probe)
            {
                Func<Vec3, double>? distance = DistanceTo(probe);
                return distance == null ? null : FormToolPlacement.Register3D(_samples, _axes, distance, Guesses(probe));
            }

            /// <summary>
            /// The kernel's distance of a point from the tool's faces – those of the instance it belongs to (the nearest insertion
            /// point; turning about its axis keeps that): faces within its samples' reach of it plus a millimetre, so a tool with
            /// many instances costs no more per sample than one. Null without faces.
            /// </summary>
            private Func<Vec3, double>? DistanceTo(Feature f)
            {
                List<Face2> faces = SwFormTool.Faces(f);
                if (faces.Count == 0) return null;
                var near = new List<Face2>[_centres.Count];
                for (int i = 0; i < _centres.Count; i++)
                {
                    Vec3 centre = _centres[i];
                    double reach = _samples.Where((s, j) => _r.SampleInstance[j] == i).Select(s => s.DistanceTo(centre)).DefaultIfEmpty(0).Max() + 1e-3;
                    near[i] = faces.Where(x => SwFormTool.Distance(new[] { x }, centre) <= reach).ToList();
                    if (near[i].Count == 0) near[i] = faces;
                }
                return p => SwFormTool.Distance(near[FormToolPlacement.NearestInstance(p, _centres)], p);
            }

            /// <summary>First guesses: the probe's orientation sketch turned onto the mirrored original's (A4 P10, B68); none if either does not read.</summary>
            private List<double> Guesses(Feature probe)
            {
                if (_orientation.Count == 0) return new List<double>();
                Feature? orientation = SwFormTool.Sketches(probe).FirstOrDefault(s => _positionSketch == null || s.Name != _positionSketch.Name);
                if (orientation == null) return new List<double>();
                List<Vec3> actual = SwFormTool.SketchPoints(_ctx.Math, orientation, userOnly: false);
                if (actual.Count == 0) return new List<double>();
                Vec3 centroid = actual.Aggregate(Vec3.Zero, (a, p) => a + p) / actual.Count;
                Vec3 centre = _centres[FormToolPlacement.NearestInstance(centroid, _centres)];
                return FormToolPlacement.Register2D(_orientation, actual, new Line3(centre, _normal));
            }

            /// <summary>Every mirrored sample lies on the tool's faces.</summary>
            private bool Fits(Feature f, out double deviation)
            {
                Func<Vec3, double>? distance = DistanceTo(f);
                deviation = distance == null ? double.PositiveInfinity : FormToolPlacement.Deviation(_samples, distance);
                return deviation <= FormToolPlacement.CheckTolerance;
            }

            /// <summary>
            /// The probe turned by ψ: as it is if it fits already; otherwise in the rotation sense found earlier in the run first,
            /// in place where the 2025 data answer, else made anew – the first that fits is kept and its sense remembered. A turn
            /// in place that fits in neither sense is tried once more made anew (B69: unit and effect of RotationAngle are not
            /// confirmed – the 2019 path); fits that, the run turns no tool in place any more.
            /// </summary>
            private Done? Turn(Feature probe, string configuration, bool flip, double psi)
            {
                if (Fits(probe, out double _)) return new Done(probe, _positionSketch, configuration, flip, 0);
                List<int>? turnedInPlace = _ctx.Shared.FormToolNoInPlace ? null : new List<int>();
                Done? done = TurnSenses(probe, FormToolPlacement.Senses(_ctx.Shared.FormToolSense), configuration, flip, psi, turnedInPlace);
                if (done != null || _left != null || turnedInPlace == null || turnedInPlace.Count == 0) return done;
                // Everything is removed: the senses SOLIDWORKS turned in place, placed anew.
                done = TurnSenses(null, turnedInPlace, configuration, flip, psi, null);
                if (done != null)
                {
                    _ctx.Shared.FormToolNoInPlace = true;
                    _info.Add(FormToolPlacement.TurnedAnew);
                }
                return done;
            }

            /// <summary>
            /// The senses in their order: <paramref name="current"/> turned in place where <paramref name="turnedInPlace"/> is
            /// given (it collects the senses whose turn in place was checked), else made anew. A sense whose copy SOLIDWORKS does
            /// not place leaves the other its try. Null if none fits (everything removed).
            /// </summary>
            private Done? TurnSenses(Feature? current, IEnumerable<int> senses, string configuration, bool flip, double psi, List<int>? turnedInPlace)
            {
                foreach (int sense in senses)
                {
                    double angle = FormToolPlacement.FinalAngle(0, psi, sense);
                    Feature? turned = null;
                    if (turnedInPlace != null && current != null && SwFormTool.TryRotate(_doc, current, angle))
                    {
                        turned = FlangeSupport.Live(_doc, current);
                        if (turned != null) turnedInPlace.Add(sense);
                    }
                    bool inPlace = turned != null;
                    _ctx.Topology.Invalidate();
                    if (turned == null)
                    {
                        if (!Remove()) return null;
                        turned = Place(configuration, flip, angle);
                        if (_left != null) return null;
                        if (turned == null)
                        {
                            // Only the angle differs from the probe: turned the wrong way the form may run over the face's edge (B68).
                            _tried.Add(FormToolPlacement.Tried(configuration, flip, FormToolPlacement.TurnedNotInserted(angle)));
                            current = null;
                            continue;
                        }
                    }
                    if (Fits(turned, out double deviation))
                    {
                        _ctx.Shared.FormToolSense = sense;
                        return new Done(turned, _positionSketch, configuration, flip, angle);
                    }
                    _tried.Add(FormToolPlacement.Tried(configuration, flip, FormToolPlacement.Turned(angle, deviation, inPlace)));
                    current = turned;
                }
                Remove();
                return null;
            }

            /// <summary>The tool on what is selected at mark 0, linked and shown in the flat pattern as the original (where read).</summary>
            private Feature? Insert(bool flip, double angle, string configuration) =>
                SwFormTool.Insert(_doc, _r.ToolPath, flip, angle, configuration, _r.LinkToTool, _r.OverrideDisplay == true, _r.ShowPunch, _r.ShowProfile, _r.ShowCenter);

            /// <summary>Removes what the attempts made; false (and nothing more is made) if something stays behind.</summary>
            private bool Remove()
            {
                List<string> left = FlangeSupport.RemoveMade(_ctx, _before);
                _ctx.Sketches.Remove(_r.PositionSketchName);
                _positionSketch = null;
                if (left.Count == 0) return true;
                _left = left;
                return false;
            }

            /// <summary>
            /// One tool: on the position sketch written on the face (B67), else – the sketch not placing it, or the face taking no
            /// sketch – on the face, its own position sketch written afterwards (variant A); from then on always so. Not so for a
            /// turned tool once the sketch placed it: null then, as when neither places it (what was made is removed).
            /// </summary>
            private Feature? Place(string configuration, bool flip, double angle)
            {
                if (_bySketch)
                {
                    Feature? onSketch = PlaceOnSketch(configuration, flip, angle);
                    if (onSketch != null)
                    {
                        _inserted = true;
                        _sketchPlaced = true;
                        return onSketch;
                    }
                    if (!Remove()) return null;
                    // The sketch placed the probe, only the angle differs: turned the wrong way the form runs over the face's edge
                    // (B68). The face, placing one instance, would hide that and say nothing about the sketch (B67).
                    if (_sketchPlaced && angle != 0) return null;
                }
                Feature? onFace = PlaceOnFace(configuration, flip, angle);
                if (onFace == null)
                {
                    Remove();
                    return null;
                }
                _inserted = true;
                if (_bySketch)
                {
                    // Only now: a configuration that places nothing either way says nothing about the sketch.
                    _bySketch = false;
                    _info.Add(FormToolPlacement.SketchWrittenAfter);
                }
                return onFace;
            }

            /// <summary>The position sketch on the face, selected, the tool placed on it; null unless the tool absorbed it.</summary>
            private Feature? PlaceOnSketch(string configuration, bool flip, double angle)
            {
                var sketchNotes = new List<string>();
                SketchMap? map;
                try
                {
                    map = new SketchBuilder(_ctx).BuildOnFace(new SketchRecord(_r.PositionSketchName, _r.Position), sketchNotes, out string? _);
                }
                catch (Exception)
                {
                    return null;
                }
                if (map == null) return null;
                string sketchName = map.Feature.Name;
                Feature? f = null;
                SwSelect.Clear(_doc);
                if (SwSelect.Select(_doc, map.Feature, 0, false)) f = Insert(flip, angle, configuration);
                SwSelect.Clear(_doc);
                _ctx.Topology.Invalidate();
                f ??= SwSheetMetal.NewOfType(_doc, _before, SwFormTool.TypeName, _ => true);
                // One instance at each point means: the sketch is the tool's now (B67).
                Feature? absorbed = f == null ? null : SwFormTool.Sketches(f).FirstOrDefault(s => s.Name == sketchName);
                if (absorbed == null) return null;
                _sketchNotes = sketchNotes;
                _positionSketch = absorbed;
                return f;
            }

            /// <summary>Variant A: the face picked at the first point, the tool placed, the mirrored position sketch written into the one it brought.</summary>
            private Feature? PlaceOnFace(string configuration, bool flip, double angle)
            {
                Face2? face = FaceNow();
                if (face == null) return null;
                Feature? f = null;
                if (SelectFaceAt(_ctx, face, _centres[0], 0)) f = Insert(flip, angle, configuration);
                SwSelect.Clear(_doc);
                _ctx.Topology.Invalidate();
                f ??= SwSheetMetal.NewOfType(_doc, _before, SwFormTool.TypeName, _ => true);
                if (f == null) return null;
                Feature? position = SwFormTool.Sketches(f).FirstOrDefault();
                if (position == null) return null;
                var sketchNotes = new List<string>();
                try
                {
                    new SketchBuilder(_ctx).BuildInto(_r.Position, position, sketchNotes);
                }
                catch (Exception)
                {
                    return null;
                }
                _doc.EditRebuild3();
                _ctx.Topology.Invalidate();
                _sketchNotes = sketchNotes;
                _positionSketch = position;
                return f;
            }

            private BuildResult Finish(Done d)
            {
                Feature f = d.Feature;
                List<Feature> sketches = SwFormTool.Sketches(f);
                Feature? position = d.PositionSketch ?? sketches.FirstOrDefault();
                Feature? orientation = sketches.FirstOrDefault(s => position == null || s.Name != position.Name);
                if (position != null)
                {
                    int count = SwFormTool.SketchPoints(_ctx.Math, position, userOnly: true).Count;
                    if (count != _r.Points.Count) _notes.Add(FormToolPlacement.CountOff(count, _r.Points.Count));
                }
                foreach (string note in _sketchNotes.Distinct())
                    if (!_notes.Contains(note))
                        _notes.Add(note);
                if (position != null) _ctx.Register(_r.PositionSketchName, position);
                if (_r.OrientationSketchName != null && orientation != null) _ctx.Register(_r.OrientationSketchName, orientation);
                _ctx.Register(_r.Name, f);

                BuildResult result = Ok(f, FormToolPlacement.Describe(_r, d.Angle), _notes);
                result.Info.Add(FormToolPlacement.Checked(_samples.Count, _r.Points.Count));
                if (d.Flip) result.Info.Add(FormToolPlacement.Flipped);
                if (d.Configuration.Length > 0) result.Info.Add(FormToolPlacement.Configuration(d.Configuration));
                if (FormToolPlacement.Link(_r) is string link) result.Info.Add(link);
                if (FormToolPlacement.Library(_r) is string library) result.Info.Add(library);
                if (FormToolPlacement.Display(_r) is string display) result.Info.Add(display);
                result.Info.AddRange(_info);
                result.Info.AddRange(_tried);
                return result;
            }
        }
    }
}
