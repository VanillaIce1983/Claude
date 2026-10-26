using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
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
    }

    /// <summary>State shared by all builders during one job.</summary>
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
            Planes = new PlaneResolver(this);
            Topology = new TopologyFinder(this);
        }

        public SldWorks Sw { get; }

        /// <summary>The document the features are created in.</summary>
        public ModelDoc2 Doc { get; }

        public SwMath Math { get; }

        /// <summary>The reflection used for building (part coordinates of the target).</summary>
        public Reflection Mirror { get; }

        public MirrorOptions Options { get; }
        public MirrorReport Report { get; }
        public PlaneResolver Planes { get; }
        public TopologyFinder Topology { get; }

        /// <summary>
        /// Same part, body or whole-part mode: the mirrored features form their own body. The
        /// first boss does not merge, later features are limited to the bodies this job created.
        /// </summary>
        public bool SeparateBody { get; }

        public bool SamePart => Options.Target == MirrorTarget.SamePart;

        public Dictionary<string, SketchMap> Sketches { get; } = new Dictionary<string, SketchMap>(StringComparer.Ordinal);

        /// <summary>Sketch of a hole (original name) → its hole (original name).</summary>
        public Dictionary<string, string> PartOwners { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Original names of sketches in which every sketch point is an instance – reference
        /// sketches of sketch driven patterns: a stand-in there must not be a point of its own.
        /// </summary>
        public HashSet<string> PointSketches { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Original feature name → new feature.</summary>
        public Dictionary<string, Feature> Created { get; } = new Dictionary<string, Feature>(StringComparer.Ordinal);

        private readonly HashSet<string> _createdNames = new HashSet<string>(StringComparer.Ordinal);

        public string NameFor(string original) => SamePart ? original + Options.NameSuffix : original;

        /// <summary>Registers a new feature, gives it its name and invalidates cached topology.</summary>
        public string Register(string originalName, Feature created)
        {
            string name = SwDoc.Rename(Doc, created, NameFor(originalName));
            _createdNames.Add(name);
            Created[originalName] = created;
            Topology.Invalidate();
            return name;
        }

        /// <summary>A registered feature that was removed again: later features must not use it.</summary>
        public void Unregister(string originalName)
        {
            Created.Remove(originalName);
            Topology.Invalidate();
        }

        /// <summary>A rebuilt sketch that was removed again (with its hole): neither as feature nor as sketch map any more.</summary>
        public void ForgetSketch(string originalName)
        {
            Sketches.Remove(originalName);
            Unregister(originalName);
        }

        /// <summary>Helper features (planes, anchor sketches) also count as "ours".</summary>
        public void RegisterHelper(Feature f) => _createdNames.Add(f.Name);

        public bool IsOwnBody(Body2 b) =>
            b.GetFeatures() is object[] fs && fs.OfType<Feature>().Any(f => _createdNames.Contains(f.Name));

        public List<Body2> OwnBodies() => SwDoc.SolidBodies(Doc).Where(IsOwnBody).ToList();

        /// <summary>Bodies no feature of this job has touched – in the same part, the original.</summary>
        public List<Body2> OriginalBodies() => SwDoc.SolidBodies(Doc).Where(b => !IsOwnBody(b)).ToList();
    }
}
