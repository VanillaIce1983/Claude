using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>Linear and circular patterns: seeds, directions and the placements of the instances.</summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>Beyond this many instances the placements are not read (one call each); the mass check remains.</summary>
        private const int MaxInstancesRead = 400;

        private LinearPatternRecord CaptureLinearPattern(Feature f, string type, LinearPatternFeatureData d)
        {
            var r = new LinearPatternRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Direction1 = RequireDirection(d.D1Axis, "Richtung 1");
                r.Reverse1 = d.D1ReverseDirection;
                r.Spacing1 = d.D1Spacing;
                r.Count1 = d.D1TotalInstances;
                r.HasDirection2 = d.IsDirection2Specified();
                if (r.HasDirection2)
                {
                    r.Direction2 = RequireDirection(d.D2Axis, "Richtung 2");
                    r.Reverse2 = d.D2ReverseDirection;
                    r.Spacing2 = d.D2Spacing;
                    r.Count2 = d.D2TotalInstances;
                    r.SeedOnly2 = d.D2PatternSeedOnly;
                }
                if (d.D1EndCondition != (int)swPatternEndCondition_e.swPatternEndCondition_SpacingAndInstances ||
                    (r.HasDirection2 && d.D2EndCondition != (int)swPatternEndCondition_e.swPatternEndCondition_SpacingAndInstances))
                    r.Notes.Add("„Bis Referenz“ wird als feste Anzahl und fester Abstand übernommen (Werte des Originals).");

                ReadSeeds(r, d.BodyPattern, d.PatternFeatureArray, d.PatternFaceArray, d.PatternBodyArray, d.SkippedItemArray);
                r.GeometryPattern = d.GeometryPattern;
                r.VarySketch = d.VarySketch;
                int n = r.Count1 * (r.HasDirection2 ? Math.Max(1, r.Count2) : 1);
                ReadInstances(r, n, d.GetTransform);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private CircularPatternRecord CaptureCircularPattern(Feature f, string type, CircularPatternFeatureData d)
        {
            var r = new CircularPatternRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Axis = RequireDirection(d.Axis, "Achse");
                r.Reverse = d.ReverseDirection;
                r.Spacing = d.Spacing;
                r.Count = d.TotalInstances;
                r.EqualSpacing = d.EqualSpacing;
                r.Direction2 = d.Direction2;
                if (r.Direction2)
                {
                    r.Symmetric = d.Symmetric;
                    r.Spacing2 = d.Spacing2;
                    r.Count2 = d.TotalInstances2;
                    r.EqualSpacing2 = d.EqualSpacing2;
                }

                ReadSeeds(r, d.BodyPattern, d.PatternFeatureArray, d.PatternFaceArray, d.PatternBodyArray, d.SkippedItemArray);
                r.GeometryPattern = d.GeometryPattern;
                r.VarySketch = d.VarySketch;
                ReadInstances(r, r.Count + (r.Direction2 ? r.Count2 : 0), d.GetTransform);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        /// <summary>
        /// Seeds are read in the state before the pattern (selection access rolls the model
        /// back): features by name, faces by fingerprint, bodies by volume and first moment.
        /// </summary>
        private static void ReadSeeds(PatternRecord r, bool bodyPattern, object features, object faces, object bodies, object skipped)
        {
            r.BodyPattern = bodyPattern;
            if (bodyPattern)
            {
                if (bodies is object[] bs)
                    foreach (Body2 b in bs.OfType<Body2>())
                    {
                        // CenterOfMassX, Y, Z, Volume, ...
                        var mp = (double[])b.GetMassProperties(1.0);
                        if (mp != null && mp.Length >= 4) r.SeedBodies.Add(MassState.FromBodies(new[] { (mp[3], new Vec3(mp[0], mp[1], mp[2])) }));
                    }
            }
            else
            {
                if (features is object[] fs)
                    foreach (Feature seed in fs.OfType<Feature>()) r.SeedFeatures.Add(seed.Name);
                if (faces is object[] fcs)
                    foreach (Face2 face in fcs.OfType<Face2>()) r.SeedFaces.Add(TopologyCapture.Face(face));
            }
            if (r.SeedCount == 0) throw new NotSupportedException("Muster ohne lesbare Features, Flächen oder Körper.");

            switch (skipped)
            {
                case int[] ints:
                    r.SkippedItems.AddRange(ints);
                    break;
                case object[] objs:
                    r.SkippedItems.AddRange(objs.Select(Convert.ToInt32));
                    break;
            }
        }

        /// <summary>
        /// Placements seed → instance as the pattern reports them. Indices that do not exist
        /// give nothing; the rebuilt pattern is read with the same indices, so both sets
        /// describe the same thing whatever the numbering.
        /// </summary>
        private void ReadInstances(PatternRecord r, int count, Func<int, MathTransform> transform)
        {
            if (count <= 0 || count > MaxInstancesRead) return;
            foreach (Affine a in SwPattern.Instances(_math, count, transform)) r.Instances.Add(a);
        }

        /// <summary>Pattern direction or axis: edge, axis, sketch line, plane or face – not a dimension.</summary>
        private GeometryRef RequireDirection(object o, string what)
        {
            GeometryRef? g = ToGeometryRef(o);
            if (g != null && g.Kind != GeometryRefKind.Unknown && g.Kind != GeometryRefKind.Sketch) return g;
            throw new NotSupportedException($"{what}: Bezug {(g == null ? "fehlt" : g.ToString())} – Richtungen über Bemaßungen werden nicht unterstützt.");
        }
    }
}
