using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Mirror features of the original: „Features/Flächen spiegeln“ (MirrorPattern) and
    /// „Körper spiegeln“ (MirrorSolid). Read as data – the plane and the seeds –, rebuilt
    /// without the mirror API (MirrorFeatureBuilder). Plane, seeds and seed bodies are read
    /// under AccessSelections, in the state at the mirror feature.
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        /// <summary>Names of the tree walk: sub-features outside it (cosmetic threads) are no seeds.</summary>
        private HashSet<string>? _treeWalk;

        private MirrorFeatureRecord CaptureMirrorPattern(Feature f, string type, MirrorPatternFeatureData d)
        {
            ReadTreeWalk();
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                Plane3 plane = MirrorPlane((object)d.Plane) ?? throw new NotSupportedException("Spiegelebene des Spiegel-Features nicht lesbar.");
                var r = new MirrorFeatureRecord(f.Name, type, MirrorSeedKind.Features, plane)
                {
                    PlaneIsFace = d.GetMirrorPlaneType() == (int)swMirrorPlaneType_e.swMirrorPlaneType_Face,
                    GeometryPattern = d.GeometryPattern,
                    PropagateVisualProperty = d.PropagateVisualProperty,
                };
                if ((object)d.PatternFeatureArray is object[] seeds)
                    foreach (Feature seed in seeds.OfType<Feature>())
                        if (IsSeed(seed) && !r.SeedFeatures.Contains(seed.Name)) r.SeedFeatures.Add(seed.Name);

                if ((object)d.MirrorFaceArray is object[] faces && faces.Length > 0)
                {
                    FaceOwners owners = FaceOwnersOf(faces);
                    if (owners.Incomplete.Count > 0)
                        throw new NotSupportedException("Spiegeln einzelner Flächen wird nicht nachgebaut (nur ganze Features): Von " +
                                                        string.Join(", ", owners.Incomplete.Select(n => $"„{n}“")) +
                                                        " sind nicht alle Flächen gewählt. Abhilfe: im Original die Features statt der Flächen spiegeln.");
                    r.SeedFaces.AddRange(owners.Signatures);
                    foreach (Feature owner in owners.Complete)
                        if (IsSeed(owner) && !r.SeedFeatures.Contains(owner.Name)) r.SeedFeatures.Add(owner.Name);
                }
                if (d.FeatureScope == (int)swFeatureScope_e.swFeatureScope_SelectedBodiesWithOutAutoSelect && !_ofOwnCopy)
                    r.Notes.Add("Der Feature-Bereich des Spiegel-Features (ausgewählte Körper) wird nicht übernommen.");
                if (r.SeedFeatures.Count == 0)
                    throw new NotSupportedException("Spiegel-Feature ohne lesbare (nicht unterdrückte) Saat-Features.");
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        private MirrorFeatureRecord CaptureMirrorSolid(Feature f, string type, MirrorSolidFeatureData d)
        {
            ReadTreeWalk();
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (d.KnitSurface)
                    throw new NotSupportedException("„Flächen zusammenfügen“ beim Körperspiegeln (Flächenkörper) wird nicht nachgebaut.");
                object planeObject = (object)d.Face;
                Plane3 plane = MirrorPlane(planeObject)
                               ?? Reflection.TryFromAffine(_math.ToAffine(d.GetTransform()), 1e-7)?.Plane
                               ?? throw new NotSupportedException("Spiegelebene von „Körper spiegeln“ nicht lesbar.");
                var r = new MirrorFeatureRecord(f.Name, type, MirrorSeedKind.Bodies, plane)
                {
                    PlaneIsFace = planeObject is Face2,
                    Merge = d.Merge,
                    PropagateVisualProperty = d.PropagateVisualProperty,
                };
                if ((object)d.PatternBodyArray is object[] bodies)
                    foreach (Body2 b in bodies.OfType<Body2>())
                    {
                        // IBody2.GetType hides object.GetType: called on the interface.
                        if (((IBody2)b).GetType() != (int)swBodyType_e.swSolidBody)
                            throw new NotSupportedException("Nur Volumenkörper werden gespiegelt, keine Flächenkörper.");
                        // CenterOfMassX, Y, Z, Volume, ...
                        var mp = (double[])(object)b.GetMassProperties(1.0);
                        if (mp == null || mp.Length < 4)
                            throw new NotSupportedException($"Volumen und Schwerpunkt des Körpers „{b.Name}“ nicht lesbar.");
                        IEnumerable<string> made = (object)b.GetFeatures() is object[] fs
                            ? fs.OfType<Feature>().Where(IsSeed).Select(x => x.Name).Distinct()
                            : Enumerable.Empty<string>();
                        r.SeedBodies.Add(new MirrorSeedBody(b.Name, MassState.FromBodies(new[] { (mp[3], new Vec3(mp[0], mp[1], mp[2])) }), made));
                    }
                if (r.SeedBodies.Count == 0) throw new NotSupportedException("„Körper spiegeln“ ohne lesbare Körper.");
                r.SeedFeaturesFromBodies();
                if (r.SeedFeatures.Count == 0) throw new NotSupportedException("Die Features der zu spiegelnden Körper sind nicht lesbar.");
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>
        /// The plane of a mirror feature, in part coordinates: a planar face or a reference
        /// plane. Only its geometry is used – nothing in the target is ever selected as the
        /// mirror plane of a second half. Null if there is none.
        /// </summary>
        private Plane3? MirrorPlane(object? o)
        {
            switch (o)
            {
                case null:
                    return null;
                case Face2 face:
                    return TopologyCapture.PlaneOfFace(face) ?? throw new NotSupportedException("Die Spiegelfläche ist nicht eben.");
                // A reference plane object is also a feature: ask for it first, as in ToGeometryRef.
                case RefPlane rp:
                    return _math.PlaneOf(rp);
                case Feature feat:
                    return feat.GetSpecificFeature2() is RefPlane plane
                        ? _math.PlaneOf(plane)
                        : throw new NotSupportedException($"Spiegelebene ist weder Ebene noch ebene Fläche ({feat.GetTypeName2()}).");
                default:
                    throw new NotSupportedException("Spiegelebene ist weder Ebene noch ebene Fläche.");
            }
        }

        /// <summary>
        /// A feature that shapes a body: no sketch, reference geometry, system or suppressed
        /// feature, and one of the tree walk (not a cosmetic thread under a hole) – only those
        /// have a counterpart the second half can be read from.
        /// </summary>
        private bool IsSeed(Feature f) =>
            _treeWalk != null && _treeWalk.Contains(f.Name) && !SwDoc.IsSketch(f) && !FeatureCollector.IsDatum(f) && !SwDoc.IsSystemFeature(f) && !f.IsSuppressed();

        /// <summary>Read once, before the first selection access rolls the model back.</summary>
        private void ReadTreeWalk() =>
            _treeWalk ??= new HashSet<string>(SwDoc.FeaturesInCreationOrder(_doc).Select(x => x.Name), StringComparer.Ordinal);

        internal sealed class FaceOwners
        {
            /// <summary>Features all of whose faces are among the faces to mirror.</summary>
            public List<Feature> Complete { get; } = new List<Feature>();

            /// <summary>Names of features of which only some faces are to be mirrored.</summary>
            public List<string> Incomplete { get; } = new List<string>();

            public List<FaceSignature> Signatures { get; } = new List<FaceSignature>();
        }

        /// <summary>
        /// The faces to mirror, grouped by the feature they belong to. SOLIDWORKS copies only
        /// those faces; Spiegelwerk rebuilds features, so a group is accepted only if it is the
        /// whole set of faces of its feature – one face of a cut that made three holes, a face of
        /// the base extrude, would be rebuilt as more than was mirrored. Read in the state at the
        /// mirror feature (selection access), where both sets of faces give the same fingerprints.
        /// </summary>
        internal static FaceOwners FaceOwnersOf(object[] faces)
        {
            var result = new FaceOwners();
            var groups = new List<(Feature owner, List<FaceSignature> selected)>();
            foreach (Face2 face in faces.OfType<Face2>())
            {
                FaceSignature sig = TopologyCapture.Face(face);
                result.Signatures.Add(sig);
                if (!((object)face.GetFeature() is Feature owner))
                {
                    if (!result.Incomplete.Contains("einer Fläche ohne Feature")) result.Incomplete.Add("einer Fläche ohne Feature");
                    continue;
                }
                int i = groups.FindIndex(g => g.owner.Name == owner.Name);
                if (i < 0) groups.Add((owner, new List<FaceSignature> { sig }));
                else groups[i].selected.Add(sig);
            }
            foreach (var (owner, selected) in groups)
            {
                List<FaceSignature> all = (object)owner.GetFaces() is object[] own
                    ? own.OfType<Face2>().Select(TopologyCapture.Face).ToList()
                    : new List<FaceSignature>();
                if (MirrorFeatureRules.SameFaces(all, selected)) result.Complete.Add(owner);
                else result.Incomplete.Add(owner.Name);
            }
            return result;
        }
    }
}
