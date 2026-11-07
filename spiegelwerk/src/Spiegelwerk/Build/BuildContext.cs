using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build
{
    /// <summary>A rebuilt sketch and how the original's points and segments map onto it.</summary>
    internal sealed class SketchMap
    {
        public SketchMap(Feature feature, Sketch sketch, SketchModel source, SketchMirrorResult mirrored)
        {
            Feature = feature;
            Sketch = sketch;
            Source = source;
            Mirrored = mirrored;
        }

        public Feature Feature { get; }
        public Sketch Sketch { get; }

        /// <summary>The original sketch (source coordinates).</summary>
        public SketchModel Source { get; }

        /// <summary>The mirrored model in target sketch coordinates, with the axis map and σ.</summary>
        public SketchMirrorResult Mirrored { get; }

        public int Sigma => Mirrored.Map.Sigma;

        /// <summary>Original point id → point of the new sketch.</summary>
        public Dictionary<string, SketchPoint> Points { get; } = new Dictionary<string, SketchPoint>(StringComparer.Ordinal);

        /// <summary>Original segment id → segment of the new sketch.</summary>
        public Dictionary<string, SketchSegment> Segments { get; } = new Dictionary<string, SketchSegment>(StringComparer.Ordinal);

        /// <summary>Original dimension name → dimension of the new sketch (for variable patterns, which name them).</summary>
        public Dictionary<string, DisplayDimension> Dimensions { get; } = new Dictionary<string, DisplayDimension>(StringComparer.Ordinal);

        /// <summary>Model coordinates (target) of an original point, after mirroring.</summary>
        public Vec3 TargetModelPoint(string id) => Mirrored.Sketch.ToModel(Mirrored.Sketch.Point(id));

        /// <summary>Opened on the mirrored face of the original's support face (<see cref="BuildContext.SketchSupportFaces"/>), not on a plane.</summary>
        public bool OnSupportFace { get; set; }

        /// <summary>
        /// Why a sketch that should go on its mirrored support face went on a plane; null if it went on
        /// the face or had none. The consumer decides whether a plane will do (blech-plan K7).
        /// </summary>
        public string? SupportFaceMissed { get; set; }
    }

    /// <summary>
    /// The copy of a sheet-metal body as its base flange made it (K11): its SheetMetal and flat
    /// pattern, and σ of the base flange's sketch. σ of the sheet (the bend direction reference)
    /// stays unknown until a live test settles the convention (B39).
    /// </summary>
    internal sealed class SheetMetalBody
    {
        public SheetMetalBody(Feature? sheetMetal, Feature? flatPattern, int sigmaBase)
        {
            SheetMetal = sheetMetal;
            FlatPattern = flatPattern;
            SigmaBase = sigmaBase;
        }

        public Feature? SheetMetal { get; }
        public Feature? FlatPattern { get; }
        public int SigmaBase { get; }
        public int? SigmaSheet { get; set; }
    }

    /// <summary>
    /// State shared by all builders during one job – or, as a child context
    /// (<see cref="Nested"/>), during the second half of a mirror feature: the same document,
    /// another reflection, records named after the first-half copies in the target.
    /// </summary>
    internal sealed class BuildContext
    {
        public BuildContext(SldWorks sw, ModelDoc2 doc, SwMath math, Reflection mirror, MirrorOptions options, MirrorReport report, bool separateBody)
        {
            Sw = sw;
            Doc = doc;
            Math = math;
            Mirror = mirror;
            Options = options;
            Report = report;
            SeparateBody = separateBody;
            Shared = new BuildShared();
            _ownNames = Shared.JobNames;
            Shared.Planes = new PlaneResolver(this);
            Topology = new TopologyFinder(this);
        }

        private BuildContext(BuildContext parent, Reflection mirror, bool separateBody, HashSet<string> ownNames, string mirrorName, MirrorReport report)
        {
            Sw = parent.Sw;
            Doc = parent.Doc;
            Math = parent.Math;
            Options = parent.Options;
            Shared = parent.Shared;
            RegionSpaces = parent.RegionSpaces;
            Mirror = mirror;
            SeparateBody = separateBody;
            Report = report;
            Parent = parent;
            MirrorName = mirrorName;
            _ownNames = ownNames;
            Topology = new TopologyFinder(this);
        }

        /// <summary>
        /// The context in which the second half of <paramref name="mirrorName"/> is built: the
        /// same document and plane resolver, the reflection at P′ = R_Π(P), its own sketch maps,
        /// registrations, report and known dimensions (thrown away: a second half never carries
        /// a name equations could rely on). <paramref name="ownNames"/>: the features whose
        /// bodies count as its own – the job's (features mirrored onto the job's bodies) or a
        /// fresh set („Körper spiegeln“ makes bodies of its own).
        /// </summary>
        public static BuildContext Nested(BuildContext parent, Reflection mirror, bool separateBody, HashSet<string> ownNames, string mirrorName) =>
            new BuildContext(parent, mirror, separateBody, ownNames, mirrorName, new MirrorReport());

        public SldWorks Sw { get; }

        /// <summary>The document the features are created in.</summary>
        public ModelDoc2 Doc { get; }

        public SwMath Math { get; }

        /// <summary>The reflection used for building (part coordinates of the target).</summary>
        public Reflection Mirror { get; }

        public MirrorOptions Options { get; }
        public MirrorReport Report { get; }
        public BuildShared Shared { get; }
        public PlaneResolver Planes => Shared.Planes;
        public TopologyFinder Topology { get; }

        /// <summary>The context this one builds the second half of a mirror feature for; null for the job's own.</summary>
        public BuildContext? Parent { get; }

        public bool IsNested => Parent != null;

        /// <summary>Name of the mirror feature whose second half this context builds.</summary>
        public string? MirrorName { get; }

        /// <summary>How the steps are checked; a second half is checked as its seeds allow.</summary>
        public StepCheck Check { get; set; } = StepCheck.Full;

        /// <summary>
        /// Same part, body or whole-part mode: the mirrored features form their own body. The
        /// first boss does not merge, later features are limited to the bodies this job created.
        /// </summary>
        public bool SeparateBody { get; }

        public bool SamePart => Options.Target == MirrorTarget.SamePart;

        /// <summary>New part with "copy equations": dimension names decide which equations come along.</summary>
        public bool CopiesEquations => !SamePart && Options.CopyEquations;

        public Dictionary<string, SketchMap> Sketches { get; } = new Dictionary<string, SketchMap>(StringComparer.Ordinal);

        /// <summary>
        /// What the run has learnt about the coordinate space of sketch region edges – on the
        /// original during capture, or on the target (shared with <see cref="Capture.FeatureCapture"/>).
        /// </summary>
        public RegionSpaceMemory RegionSpaces { get; set; } = new RegionSpaceMemory();

        /// <summary>Sketch of a hole (original name) → its hole (original name).</summary>
        public Dictionary<string, string> PartOwners { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Original names of sketches in which every sketch point is an instance – reference
        /// sketches of sketch driven patterns: a stand-in there must not be a point of its own.
        /// </summary>
        public HashSet<string> PointSketches { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Original sketch name → the face it was drawn on (unmirrored, state right after the sketch;
        /// blech-plan K7): <see cref="SketchBuilder.Build"/> opens the copy on the mirrored face where
        /// it is found. Filled from the records of sketched bends and jogs (<see cref="NoteSupportFaces"/>).
        /// </summary>
        public Dictionary<string, FaceSignature> SketchSupportFaces { get; } = new Dictionary<string, FaceSignature>(StringComparer.Ordinal);

        /// <summary>The support faces of the sketches that sketched bends and jogs are made from.</summary>
        public void NoteSupportFaces(IEnumerable<FeatureRecord> records)
        {
            foreach (FeatureRecord r in records)
                switch (r)
                {
                    case SketchedBendRecord b when b.SketchFace != null:
                        SketchSupportFaces[b.SketchName] = b.SketchFace;
                        break;
                    case JogRecord j when j.SketchFace != null:
                        SketchSupportFaces[j.SketchName] = j.SketchFace;
                        break;
                }
        }

        /// <summary>Original feature name → new feature.</summary>
        public Dictionary<string, Feature> Created { get; } = new Dictionary<string, Feature>(StringComparer.Ordinal);

        /// <summary>Original base flange → the sheet-metal body its copy made (SheetMetal, flat pattern, σ).</summary>
        public Dictionary<string, SheetMetalBody> SheetMetalBodies { get; } = new Dictionary<string, SheetMetalBody>(StringComparer.Ordinal);

        /// <summary>
        /// Original name of a mirror feature → the features of its second half, in tree order,
        /// and whether each changes the solid: its counterpart is not one feature but these.
        /// </summary>
        public Dictionary<string, List<(Feature feature, bool solid)>> Groups { get; } =
            new Dictionary<string, List<(Feature, bool)>>(StringComparer.Ordinal);

        /// <summary>
        /// Dimensions of the original ("D1@Skizze1") that the copy has under their original name,
        /// identified as the same one: the only ones a copied equation may name.
        /// </summary>
        public HashSet<string> KnownDimensions { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Features whose bodies count as this context's own.</summary>
        private readonly HashSet<string> _ownNames;

        /// <summary>
        /// Original name → the name its feature got in the target. Unregistering goes by this
        /// name: the feature itself may already be deleted, and its Name is not read again.
        /// </summary>
        private readonly Dictionary<string, string> _targetNames = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The features that make this context's own bodies (the job's, or those of a „Körper spiegeln“).</summary>
        public HashSet<string> OwnNames => _ownNames;

        /// <summary>Name the report gives a feature in the target: the original's, „X (Spiegeln1)“ for a second half.</summary>
        public string Logical(string target) => Shared.Built.TryGetValue(target, out BuiltFeature b) ? b.Logical : target;

        /// <summary>
        /// Name in the report of what a record becomes: the original's own name; in a second half
        /// the name of its first half with the mirror feature's („Boss-Extrude1 (Spiegeln1)“).
        /// </summary>
        public string LogicalFor(string recordName) =>
            MirrorName == null ? recordName : MirrorFeatureRules.SecondHalfName(Logical(recordName), MirrorName);

        /// <summary>The name a new feature gets: its name in the report, in the same part with the suffix last.</summary>
        public string NameFor(string original) => FeatureNames.InTarget(LogicalFor(original), SamePart ? Options.NameSuffix : null);

        /// <summary>Registers a new feature, gives it its name and invalidates cached topology.</summary>
        public string Register(string originalName, Feature created)
        {
            string name = SwDoc.Rename(Doc, created, NameFor(originalName));
            _ownNames.Add(name);
            Shared.JobNames.Add(name);
            Created[originalName] = created;
            _targetNames[originalName] = name;
            Shared.Built[name] = new BuiltFeature(LogicalFor(originalName));
            Topology.Invalidate();
            return name;
        }

        /// <summary>The second half of a mirror feature: its counterpart for later features (patterns of it, mirrors of it).</summary>
        public void RegisterGroup(string originalName, List<(Feature feature, bool solid)> members)
        {
            Groups[originalName] = members;
            foreach (var (feature, _) in members)
            {
                _ownNames.Add(feature.Name);
                Shared.JobNames.Add(feature.Name);
            }
            Topology.Invalidate();
        }

        /// <summary>
        /// The counterparts of a feature of the original: its rebuilt feature, or the features of a
        /// mirror feature's second half (with <paramref name="solidOnly"/> only those that change
        /// the solid). Only this context's own: a second half never falls back on the first half.
        /// </summary>
        public List<Feature> Counterparts(string original, bool solidOnly)
        {
            if (Groups.TryGetValue(original, out List<(Feature feature, bool solid)> group))
                return group.Where(m => !solidOnly || m.solid).Select(m => m.feature).ToList();
            return Created.TryGetValue(original, out Feature f) ? new List<Feature> { f } : new List<Feature>();
        }

        /// <summary>Whether a feature of the target is a counterpart registered in this context.</summary>
        public bool IsCounterpart(Feature f)
        {
            string name = f.Name;
            return Created.Values.Any(c => c.Name == name) || Groups.Values.Any(g => g.Any(m => m.feature.Name == name));
        }

        /// <summary>Whether the counterpart <paramref name="f"/> changes the solid (as its record said).</summary>
        public bool ChangesSolid(Feature f) => Shared.Built.TryGetValue(f.Name, out BuiltFeature b) && b.ChangesSolid;

        /// <summary>
        /// A registered feature that was removed again: later features must not use it. Callers
        /// may delete it first, so the feature itself is not touched here.
        /// </summary>
        public void Unregister(string originalName)
        {
            if (_targetNames.TryGetValue(originalName, out string target)) Shared.Built.Remove(target);
            _targetNames.Remove(originalName);
            Created.Remove(originalName);
            Groups.Remove(originalName);
            SheetMetalBodies.Remove(originalName);
            Topology.Invalidate();
        }

        /// <summary>A rebuilt sketch that was removed again (with its hole): neither as feature nor as sketch map any more.</summary>
        public void ForgetSketch(string originalName)
        {
            Sketches.Remove(originalName);
            Unregister(originalName);
        }

        /// <summary>
        /// Features of a failed step about to be deleted again: neither registered (as feature,
        /// sketch map or member of a second half) nor known to the plane resolver any more.
        /// </summary>
        public void Forget(ICollection<string> names)
        {
            List<string> keys = Created.Where(kv => names.Contains(kv.Value.Name)).Select(kv => kv.Key)
                .Concat(Sketches.Where(kv => names.Contains(kv.Value.Feature.Name)).Select(kv => kv.Key))
                .Concat(Groups.Where(kv => kv.Value.Any(m => names.Contains(m.feature.Name))).Select(kv => kv.Key))
                .Distinct().ToList();
            foreach (string key in keys) ForgetSketch(key);
            foreach (string name in names) Shared.Built.Remove(name);
            // A sheet-metal body whose flange or companions go is gone with them.
            foreach (string key in SheetMetalBodies.Where(kv => Gone(kv.Key, kv.Value, names)).Select(kv => kv.Key).ToList()) SheetMetalBodies.Remove(key);
            Planes.Forget(names);
            Topology.Invalidate();
        }

        private bool Gone(string owner, SheetMetalBody body, ICollection<string> names) =>
            !Created.ContainsKey(owner) ||
            (body.SheetMetal != null && names.Contains(body.SheetMetal.Name)) ||
            (body.FlatPattern != null && names.Contains(body.FlatPattern.Name));

        /// <summary>Helper features (planes, anchor sketches) also count as "ours".</summary>
        public void RegisterHelper(Feature f)
        {
            _ownNames.Add(f.Name);
            Shared.JobNames.Add(f.Name);
        }

        public bool IsOwnBody(Body2 b) =>
            b.GetFeatures() is object[] fs && fs.OfType<Feature>().Any(f => _ownNames.Contains(f.Name));

        public List<Body2> OwnBodies() => SwDoc.SolidBodies(Doc).Where(IsOwnBody).ToList();

        /// <summary>Bodies no feature of this context has touched – in the same part, the original (and, for „Körper spiegeln“, the first half).</summary>
        public List<Body2> OriginalBodies() => SwDoc.SolidBodies(Doc).Where(b => !IsOwnBody(b)).ToList();
    }
}
