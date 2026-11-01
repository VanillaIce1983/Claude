using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Flat pattern (A1 §8): the copy's base flange already brought one; this edits it – the fixed
    /// face found by its mirrored fingerprint (a face or an edge, never a stand-in plane), the
    /// options, the excluded faces. Flattening commutes with the mirror when allowance and fixed
    /// face are the mirrored ones, Flat(R P, R F) = R Flat(P, F): there is no σ here. The folded
    /// solid does not show any of it; with „Jeden Schritt prüfen“ the flattened state is
    /// compared with the original's (<see cref="FlatPatternRecord.FlatChange"/>). The state the
    /// original had (flattened or folded) the run gives the copy at the end.
    /// </summary>
    internal sealed class FlatPatternBuilder : IFeatureBuilder
    {
        /// <summary>A flat sample counts as hit within this distance (as in the step check).</summary>
        private const double ShapeTol = 1e-5;

        public bool Handles(FeatureRecord record) => record is FlatPatternRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (FlatPatternRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();

            object? fixedEntity = null;
            if (r.FixedFace?.Kind == GeometryRefKind.Face) fixedEntity = Safe(ctx.Topology.FindFace(r.FixedFace.Face!.Mirror(ctx.Mirror), notes));
            else if (r.FixedFace?.Kind == GeometryRefKind.Edge) fixedEntity = Safe(ctx.Topology.FindEdge(r.FixedFace.Edge!.Mirror(ctx.Mirror), notes));
            // Same part: the finder falls back on the original where the copy has nothing – a flat pattern of the copy never takes its faces.
            if (fixedEntity != null && !OnOwnBody(ctx, fixedEntity))
            {
                fixedEntity = null;
                notes.Add("Feste Fläche der Abwicklung nur am Original gefunden; sie wird nicht verwendet.");
            }

            Feature? fp = ctx.SheetMetalBodies.TryGetValue(r.Owner, out SheetMetalBody body) ? body.FlatPattern : null;
            fp ??= FlatPatternOfBody(fixedEntity);
            if (fp == null) return Fail($"Im Ziel gibt es keine Abwicklung zu „{r.Owner}“.", notes);
            if (r.FixedFace != null && fixedEntity == null) notes.Add("Feste Fläche der Abwicklung im Ziel nicht gefunden; die Vorgabe von SOLIDWORKS bleibt.");

            var excluded = new List<object>();
            foreach (FaceSignature excludedFace in r.ExcludedFaces)
            {
                if (Safe(ctx.Topology.FindFace(excludedFace.Mirror(ctx.Mirror), notes)) is object found && OnOwnBody(ctx, found)) excluded.Add(found);
                else notes.Add("Eine ausgeschlossene Fläche der Abwicklung wurde im Ziel nicht gefunden.");
            }

            if (!Edit(ctx, fp, d => Apply(d, r, fixedEntity, excluded)))
                notes.Add("Optionen der Abwicklung ließen sich nicht setzen.");
            notes.AddRange(ReadBack(ctx, fp, r, fixedEntity));

            ctx.Register(r.Name, fp);
            string check = ctx.Options.VerifySteps ? FlatCheck(ctx, fp, r, notes) : "nicht geprüft";

            string face = fixedEntity != null ? "gespiegelt" : r.FixedFace == null ? "wie im Original ohne Angabe" : "Vorgabe von SOLIDWORKS";
            BuildResult result = Ok(fp, $"Abwicklung, feste Fläche {face}" + (r.Flattened ? ", wird am Ende abgewickelt" : ""), notes);
            result.Verification = check;
            result.Info.Add(SheetMetalRules.GrainNote);
            if (r.Flattened) result.Info.Add(SheetMetalRules.FlatConfigurationNote);
            if (r.OwnerFromTree) result.Info.Add(SheetMetalTree.OwnerFromTreeNote);
            return result;
        }

        /// <summary>Outside the same part any entity; in it only one on a body of the job (not the original's).</summary>
        private static bool OnOwnBody(BuildContext ctx, object entity)
        {
            if (!ctx.SamePart) return true;
            Body2? body = entity switch
            {
                Face2 face => (Body2)(object)face.GetBody(),
                Edge edge => (Body2)(object)edge.GetBody(),
                _ => null,
            };
            return body != null && ctx.IsOwnBody(body);
        }

        /// <summary>The flat pattern among the features of the body that holds the fixed face.</summary>
        private static Feature? FlatPatternOfBody(object? fixedEntity)
        {
            Body2? body = fixedEntity switch
            {
                Face2 face => (Body2)(object)face.GetBody(),
                Edge edge => (Body2)(object)edge.GetBody(),
                _ => null,
            };
            return body?.GetFeatures() is object[] fs ? fs.OfType<Feature>().FirstOrDefault(f => f.GetTypeName2() == "FlatPattern") : null;
        }

        private static void Apply(FlatPatternFeatureData d, FlatPatternRecord r, object? fixedEntity, List<object> excluded)
        {
            if (fixedEntity != null) d.FixedFace2 = fixedEntity;
            d.MergeFace = r.MergeFaces;
            d.SimplifyBends = r.SimplifyBends;
            d.CornerTreatment = r.CornerTreatment;
            d.CornerTrimReliefType = r.CornerTrimReliefType;
            d.CornerTrimReliefDistance = r.CornerTrimReliefDistance;
            d.UseRatioToThickness = r.UseRatioToThickness;
            d.CornerTrimRatioToThickness = r.CornerTrimRatioToThickness;
            d.BreakCornerType = r.BreakCornerType;
            d.BreakCornerRadius = r.BreakCornerRadius;
            d.ShowSlitInCornerRelief = r.ShowSlitInCornerRelief;
            d.SetAddCornerTrim(r.AddCornerTrim);
            d.SetBreakCorners(r.BreakCorners);
            if (excluded.Count > 0) d.ExcludedFaces = excluded.ToArray();
        }

        /// <summary>
        /// The edit; if the data of the folded (suppressed) flat pattern do not open, it is
        /// flattened for the edit and folded again.
        /// </summary>
        private static bool Edit(BuildContext ctx, Feature fp, Action<FlatPatternFeatureData> change)
        {
            if (FeatureEdit.FlatPattern(ctx.Doc, fp, change)) return true;
            if (!SwSheetMetal.IsSuppressed(fp) || !SwSheetMetal.SetSuppressed(ctx.Doc, fp, false)) return false;
            try
            {
                return FeatureEdit.FlatPattern(ctx.Doc, fp, change);
            }
            finally
            {
                SwSheetMetal.SetSuppressed(ctx.Doc, fp, true);
                ctx.Topology.Invalidate();
            }
        }

        /// <summary>Reads the options and the fixed face back under the flat pattern's own selection access.</summary>
        private static List<string> ReadBack(BuildContext ctx, Feature fp, FlatPatternRecord r, object? fixedEntity)
        {
            var notes = new List<string>();
            var d = (FlatPatternFeatureData)(object)fp.GetDefinition();
            if (d == null || !d.AccessSelections(ctx.Doc, null)) return notes;
            var values = new List<(string, double, double, ScalarKind)>();
            try
            {
                values.Add(("Flächen zusammenführen", Flag(r.MergeFaces), Flag(d.MergeFace), ScalarKind.Flag));
                values.Add(("Biegungen vereinfachen", Flag(r.SimplifyBends), Flag(d.SimplifyBends), ScalarKind.Flag));
                values.Add(("Eckenbehandlung", Flag(r.CornerTreatment), Flag(d.CornerTreatment), ScalarKind.Flag));
                Feature? trim = null, breakCorner = null;
                values.Add(("Ecke trimmen", Flag(r.AddCornerTrim), Flag(d.GetAddCornerTrim(out trim)), ScalarKind.Flag));
                values.Add(("Ecken brechen", Flag(r.BreakCorners), Flag(d.GetBreakCorners(out breakCorner)), ScalarKind.Flag));
                if (fixedEntity != null && !SameEntity((object)d.FixedFace2, r.FixedFace!.Mirror(ctx.Mirror)))
                    notes.Add("Abwicklung: Die feste Fläche ist nach dem Setzen nicht die gespiegelte.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            notes.AddRange(SheetMetalCompare.Scalars(values).Select(x => $"Abwicklung: {x}"));
            return notes;
        }

        private static double Flag(bool b) => b ? 1 : 0;

        /// <summary>The read fixed face or edge has the wanted fingerprint.</summary>
        private static bool SameEntity(object? read, GeometryRef wanted)
        {
            switch (read)
            {
                case Face2 face when wanted.Kind == GeometryRefKind.Face:
                    return wanted.Face!.CouldBe(TopologyCapture.Face(face)) && TopologyCapture.DistanceToFace(face, wanted.Face.Sample) <= Tol.Length;
                case Edge edge when wanted.Kind == GeometryRefKind.Edge:
                    return !double.IsPositiveInfinity(wanted.Edge!.DistanceTo(TopologyCapture.Edge(edge)));
                default:
                    return false;
            }
        }

        /// <summary>
        /// The flattened state against the original's (A1 §9): with the bar at the end, the copy
        /// folded, then flattened; the change must be the mirrored flat change of the original,
        /// and the mirrored samples must lie on the flattened faces. Folded again in any case.
        /// A deviation is a warning – no flag can sensibly be flipped.
        /// </summary>
        private static string FlatCheck(BuildContext ctx, Feature fp, FlatPatternRecord r, List<string> notes)
        {
            if (r.FlatChange == null)
            {
                notes.Add(SheetMetalRules.FlatNotMeasured);
                return "nicht geprüft";
            }
            ModelDoc2 doc = ctx.Doc;
            SwDoc.RollToEnd(doc);
            if (!SwSheetMetal.SetSuppressed(doc, fp, true))
            {
                notes.Add("Abwicklung nicht geprüft: Die Kopie ließ sich nicht zurückfalten.");
                return "nicht geprüft";
            }
            MassState before = Mass(ctx);
            try
            {
                if (!SwSheetMetal.SetSuppressed(doc, fp, false))
                {
                    notes.Add("Abwicklung nicht geprüft: Die Kopie ließ sich nicht abwickeln.");
                    return "nicht geprüft";
                }
                MassState after = Mass(ctx);
                MassComparison mass = MassComparison.Compare(r.FlatChange.Value.Mirror(ctx.Mirror), after - before, after);
                List<Face2> faces = (ctx.SeparateBody ? ctx.OwnBodies() : SwDoc.SolidBodies(doc))
                    .SelectMany(b => ((object)b.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>()).ToList();
                int missed = r.FlatSamples.Select(ctx.Mirror.Apply).Count(p => !faces.Any(face => TopologyCapture.DistanceToFace(face, p) <= ShapeTol));
                if (mass.Passes && missed == 0) return SheetMetalRules.FlatChecked;
                notes.Add(SheetMetalRules.FlatDeviates);
                string shape = missed == 0 ? "" : $", {missed} von {r.FlatSamples.Count} Flächenproben nicht getroffen";
                return $"Abwicklung: {mass}{shape}";
            }
            finally
            {
                if (!SwSheetMetal.SetSuppressed(doc, fp, true)) notes.Add("Die Abwicklung der Kopie ließ sich nach der Prüfung nicht zurückfalten.");
                ctx.Topology.Invalidate();
            }
        }

        /// <summary>In the same part only the job's own bodies count.</summary>
        private static MassState Mass(BuildContext ctx) => ctx.SeparateBody ? SwDoc.MassOf(ctx.OwnBodies()) : SwDoc.MassOf(ctx.Doc);
    }
}
