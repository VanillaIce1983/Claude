using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Sw
{
    /// <summary>Names of every feature (top level and the contents of both sheet-metal folders) and the solid body count.</summary>
    internal sealed class SheetMetalSnapshot
    {
        public SheetMetalSnapshot(HashSet<string> names, int bodies)
        {
            Names = names;
            Bodies = bodies;
        }

        public HashSet<string> Names { get; }
        public int Bodies { get; }
    }

    /// <summary>
    /// Sheet metal in a document (blech-plan §1.4, §1.7): the tree with its folders, the
    /// parameters of a SheetMetal feature or the part's defaults – read with their return codes,
    /// written in the one order that works and read back –, the flat patterns and their state,
    /// and what a step added. The only place that writes sheet-metal parameters.
    /// </summary>
    internal static class SwSheetMetal
    {
        private static readonly HashSet<string> SheetMetalTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "SheetMetal", "TemplateSheetMetal", "FlatPattern", "TemplateFlatPattern", "SMBaseFlange",
        };

        /// <summary>The part has sheet metal: a SheetMetal, base flange, flat pattern or one of their folders at the top level.</summary>
        public static bool HasSheetMetal(ModelDoc2 doc) => SwDoc.TopLevelFeatures(doc).Any(f => SheetMetalTypes.Contains(f.GetTypeName2()));

        /// <summary>The part's defaults (TemplateSheetMetal); null before 2013 or without sheet metal.</summary>
        public static Feature? Template(ModelDoc2 doc)
        {
            try
            {
                return (object)doc.Extension.GetTemplateSheetMetal() as Feature;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The contents of a sheet-metal or flat-pattern folder (its sub-features, and what the
        /// folder object lists – the tree shape is not documented); any other feature: none.
        /// </summary>
        public static List<Feature> FolderContents(ModelDoc2 doc, Feature folder)
        {
            string type = folder.GetTypeName2();
            if (type != "TemplateSheetMetal" && type != "TemplateFlatPattern") return new List<Feature>();
            var result = SwDoc.SubFeatures(folder).ToList();
            try
            {
                object? listed = type == "TemplateSheetMetal"
                    ? ((object)doc.FeatureManager.GetSheetMetalFolder() as SheetMetalFolder)?.GetSheetMetals()
                    : ((object)doc.FeatureManager.GetFlatPatternFolder() as FlatPatternFolder)?.GetFlatPatterns();
                if (listed is object[] items)
                    foreach (Feature f in items.OfType<Feature>())
                        if (result.All(r => r.Name != f.Name)) result.Add(f);
            }
            catch (Exception)
            {
                // The folder object is not readable: the sub-features are all there is.
            }
            return result;
        }

        /// <summary>Every feature of a type at the top level or in a folder.</summary>
        public static List<Feature> OfType(ModelDoc2 doc, string type)
        {
            var result = new List<Feature>();
            foreach (Feature f in SwDoc.TopLevelFeatures(doc))
            {
                if (f.GetTypeName2() == type) result.Add(f);
                foreach (Feature c in FolderContents(doc, f).Where(c => c.GetTypeName2() == type))
                    if (result.All(r => r.Name != c.Name)) result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// The tree as <see cref="SheetMetalTree"/> needs it: every top-level feature with its
        /// parents and sub-features, folders with their contents, and for each flat pattern its own
        /// corner features and the features of its body.
        /// </summary>
        public static SheetMetalTree Tree(ModelDoc2 doc)
        {
            if (!HasSheetMetal(doc)) return SheetMetalTree.Empty;
            var top = new List<TreeFeature>();
            var facts = new Dictionary<string, FlatPatternFacts>(StringComparer.Ordinal);
            var flanges = new Dictionary<string, EdgeFlangeFacts>(StringComparer.Ordinal);
            TreeFeature Node(Feature f, IEnumerable<Feature> subs)
            {
                if (f.GetTypeName2() == "FlatPattern" && !facts.ContainsKey(f.Name)) facts[f.Name] = Facts(doc, f);
                List<Feature> parentFeatures = f.GetParents() is object[] ps ? ps.OfType<Feature>().ToList() : new List<Feature>();
                List<Feature> subList = subs.ToList();
                if (f.GetTypeName2() == "EdgeFlange" && !flanges.ContainsKey(f.Name) && EdgeFlangeFactsOf(doc, f, parentFeatures, subList) is EdgeFlangeFacts ef)
                    flanges[f.Name] = ef;
                return new TreeFeature(f.Name, f.GetTypeName2(), parentFeatures.Select(p => p.Name), subList.Select(s => Node(s, SwDoc.SubFeatures(s))).ToList());
            }
            foreach (Feature f in SwDoc.TopLevelFeatures(doc))
            {
                List<Feature> subs = SwDoc.SubFeatures(f).ToList();
                foreach (Feature c in FolderContents(doc, f))
                    if (subs.All(s => s.Name != c.Name)) subs.Add(c);
                top.Add(Node(f, subs));
            }
            return SheetMetalTree.Of(top, facts, flanges);
        }

        /// <summary>
        /// What decides whether a top-level parent sketch is a profile of an edge flange (A2 P13): its
        /// number of edges and which of its parent sketches – those it does not absorb – stand on one of
        /// them (ISketch.GetReferenceEntity, read under the flange's AccessSelections, where both the
        /// edges and the sketch are in the state before the flange). Null if the flange has a profile
        /// sub-sketch for every edge – nothing is missing – or nothing of it reads.
        /// </summary>
        private static EdgeFlangeFacts? EdgeFlangeFactsOf(ModelDoc2 doc, Feature flange, List<Feature> parents, List<Feature> subs)
        {
            var subNames = new HashSet<string>(subs.Select(s => s.Name), StringComparer.Ordinal);
            List<Feature> candidates = parents.Where(p => SwDoc.IsSketch(p) && !subNames.Contains(p.Name)).ToList();
            if (candidates.Count == 0) return null;
            int profiles = subs.Count(SwDoc.IsSketch);
            EdgeFlangeFeatureData? d = null;
            bool access = false;
            try
            {
                d = (object)flange.GetDefinition() as EdgeFlangeFeatureData;
                if (d == null) return null;
                access = d.AccessSelections(doc, null);
                if (!access) return null;
                List<EdgeSignature> edges = ((object)d.Edges as object[] ?? Array.Empty<object>()).OfType<Edge>().Select(Capture.TopologyCapture.Edge).ToList();
                if (profiles >= edges.Count) return null;
                var onHinge = new List<string>();
                foreach (Feature p in candidates)
                    if (SupportEdgeOf(p) is EdgeSignature support && edges.Any(e => !double.IsPositiveInfinity(e.DistanceTo(support))))
                        onHinge.Add(p.Name);
                return new EdgeFlangeFacts(edges.Count, onHinge);
            }
            catch (Exception)
            {
                // Then only its sub-sketches are its profiles; a missing one makes the flange unsupported at capture.
                return null;
            }
            finally
            {
                if (access) d!.ReleaseSelectionAccess();
            }
        }

        /// <summary>The edge a sketch was made on (ISketch.GetReferenceEntity), as a fingerprint; null if it has none or it does not read.</summary>
        public static EdgeSignature? SupportEdgeOf(Feature sketchFeature)
        {
            try
            {
                if (!((object)sketchFeature.GetSpecificFeature2() is Sketch sketch)) return null;
                // ref in the interop, though the API only writes it (A2 P15 says out).
                int kind = 0;
                return (object)sketch.GetReferenceEntity(ref kind) is Edge e ? Capture.TopologyCapture.Edge(e) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Own corner trim and break corners (the flat pattern's options) and the features of its
        /// body; what is not readable stays empty. The body is the one of its fixed face (A1 §6: it
        /// exists folded) and any body whose features name the flat pattern – which a folded,
        /// suppressed flat pattern may not be (B9).
        /// </summary>
        private static FlatPatternFacts Facts(ModelDoc2 doc, Feature flatPattern)
        {
            string? trim = null, breakCorner = null;
            var body = new List<string>();
            FlatPatternFeatureData? d = null;
            try
            {
                d = (object)flatPattern.GetDefinition() as FlatPatternFeatureData;
                if (d != null)
                {
                    Feature? t = null, b = null;
                    if (d.GetAddCornerTrim(out t) && t != null) trim = t.Name;
                    if (d.GetBreakCorners(out b) && b != null) breakCorner = b.Name;
                }
            }
            catch (Exception)
            {
                // Without the options its corner features count as the user's – not rebuilt, never wrongly owned.
            }
            if (d != null) body.AddRange(FixedFaceBodyFeatures(doc, d));
            try
            {
                foreach (Body2 b in SwDoc.SolidBodies(doc))
                    if (b.GetFeatures() is object[] fs && fs.OfType<Feature>().Any(f => f.Name == flatPattern.Name))
                        body.AddRange(fs.OfType<Feature>().Select(f => f.Name));
            }
            catch (Exception)
            {
                // Its owner then comes from the tree, or is not known; the capture says so.
            }
            return new FlatPatternFacts(trim, breakCorner, body.Distinct(StringComparer.Ordinal));
        }

        /// <summary>
        /// The features of the body that holds the flat pattern's fixed face (a face, or an edge of a
        /// cylindrical part), read under its selection access – the body belongs to that state, so
        /// it is read before the access is released. Empty if any of it is not readable.
        /// </summary>
        private static List<string> FixedFaceBodyFeatures(ModelDoc2 doc, FlatPatternFeatureData d)
        {
            var names = new List<string>();
            bool access = false;
            try
            {
                access = d.AccessSelections(doc, null);
                Body2? body = (object)d.FixedFace2 switch
                {
                    Face2 face => (object)face.GetBody() as Body2,
                    Edge edge => (object)edge.GetBody() as Body2,
                    _ => null,
                };
                if (body != null && (object)body.GetFeatures() is object[] fs) names.AddRange(fs.OfType<Feature>().Select(f => f.Name));
            }
            catch (Exception)
            {
                // The other sources decide.
            }
            finally
            {
                if (access) d.ReleaseSelectionAccess();
            }
            return names;
        }

        // ---- what a step made

        /// <summary>Names of the top-level features and the folder contents, and the solid body count.</summary>
        public static SheetMetalSnapshot Snapshot(ModelDoc2 doc) => new SheetMetalSnapshot(AllNames(doc), SwDoc.SolidBodies(doc).Count);

        /// <summary>Top-level names plus the contents of both sheet-metal folders.</summary>
        public static HashSet<string> AllNames(ModelDoc2 doc)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Feature f in SwDoc.TopLevelFeatures(doc))
            {
                names.Add(f.Name);
                foreach (Feature c in FolderContents(doc, f)) names.Add(c.Name);
            }
            return names;
        }

        /// <summary>
        /// Features new since the snapshot: at the top level (system folders aside – a template
        /// folder is never "new") and inside the sheet-metal folders, in tree order.
        /// </summary>
        public static List<Feature> NewSince(ModelDoc2 doc, HashSet<string> before)
        {
            var result = new List<Feature>();
            foreach (Feature f in SwDoc.TopLevelFeatures(doc))
            {
                if (!before.Contains(f.Name) && !SwDoc.IsSystemFeature(f)) result.Add(f);
                foreach (Feature c in FolderContents(doc, f))
                    if (!before.Contains(c.Name) && result.All(r => r.Name != c.Name)) result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// Deletes what a step made, in two rounds, and returns the names still there. A flat
        /// pattern goes with its own sketches; everything else alone – the profile sketch of a base
        /// flange is a record of its own and stays.
        /// </summary>
        public static List<string> RemoveNew(ModelDoc2 doc, HashSet<string> before)
        {
            for (int round = 0; round < 2; round++)
            {
                List<Feature> made = NewSince(doc, before);
                if (made.Count == 0) break;
                // Children first: flat patterns, then the flanges, then their SheetMetal.
                foreach (Feature f in made.OrderBy(DeleteRank).ToList())
                {
                    try
                    {
                        if (SwDoc.FeatureByName(doc, f.Name) == null) continue;
                        SwSelect.Clear(doc);
                        if (!SwSelect.Select(doc, f, 0, false)) continue;
                        doc.Extension.DeleteSelection2(f.GetTypeName2() == "FlatPattern" ? (int)swDeleteSelectionOptions_e.swDelete_Absorbed : 0);
                    }
                    catch (Exception)
                    {
                        // The next round, or the caller's report, sees what is left.
                    }
                    finally
                    {
                        SwSelect.Clear(doc);
                    }
                }
            }
            return NewSince(doc, before).Select(f => f.Name).ToList();
        }

        private static int DeleteRank(Feature f) => f.GetTypeName2() switch
        {
            "FlatPattern" => 0,
            "SheetMetal" => 2,
            _ => 1,
        };

        // ---- suppression = folded / flattened

        /// <summary>Suppressed in the active configuration (a flat pattern: folded).</summary>
        public static bool IsSuppressed(Feature f)
        {
            try
            {
                if (f.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null) is object o)
                {
                    if (o is bool[] flags && flags.Length > 0) return flags[0];
                    if (o is object[] items && items.Length > 0 && items[0] is bool b) return b;
                }
            }
            catch (Exception)
            {
                // Fall back on the plain question.
            }
            return f.IsSuppressed();
        }

        /// <summary>Suppresses (folds) or unsuppresses (flattens) in the active configuration; true if the state is the wanted one afterwards.</summary>
        public static bool SetSuppressed(ModelDoc2 doc, Feature f, bool suppressed)
        {
            if (IsSuppressed(f) == suppressed) return true;
            try
            {
                f.SetSuppression2(suppressed ? (int)swFeatureSuppressionAction_e.swSuppressFeature : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                doc.EditRebuild3();
            }
            catch (Exception)
            {
                return false;
            }
            return IsSuppressed(f) == suppressed;
        }

        // ---- parameters

        /// <summary>ICustomBendAllowance as data; the bend calculation table only late-bound (newer versions).</summary>
        public static BendAllowanceData ReadAllowance(CustomBendAllowance cba) => new BendAllowanceData
        {
            Kind = (BendAllowanceKind)cba.Type,
            KFactor = cba.KFactor,
            BendAllowance = cba.BendAllowance,
            BendDeduction = cba.BendDeduction,
            BendTableFile = cba.BendTableFile ?? "",
            BendCalculationTableFile = SwLate.Get(cba, "BendCalculationTableFile") as string ?? "",
        };

        /// <summary>Fills an allowance object in the order of <see cref="AllowanceRules.WriteOrder"/>: the value of its kind first, Type last.</summary>
        public static void Fill(CustomBendAllowance cba, BendAllowanceData a)
        {
            foreach (AllowanceStep step in AllowanceRules.WriteOrder(a))
                switch (step)
                {
                    case AllowanceStep.KFactor:
                        cba.KFactor = a.KFactor;
                        break;
                    case AllowanceStep.BendAllowance:
                        cba.BendAllowance = a.BendAllowance;
                        break;
                    case AllowanceStep.BendDeduction:
                        cba.BendDeduction = a.BendDeduction;
                        break;
                    case AllowanceStep.BendTableFile:
                        cba.BendTableFile = a.BendTableFile;
                        break;
                    case AllowanceStep.BendCalculationTableFile:
                        SwLate.Set(cba, "BendCalculationTableFile", a.BendCalculationTableFile);
                        break;
                    case AllowanceStep.Type:
                        cba.Type = (int)a.Kind;
                        break;
                }
        }

        /// <summary>A new allowance object for a creation call; null stands for the default.</summary>
        public static CustomBendAllowance? NewAllowance(FeatureManager fm, BendAllowanceData? a)
        {
            if (a == null) return null;
            CustomBendAllowance cba = fm.CreateCustomBendAllowance();
            if (cba == null) return null;
            Fill(cba, a);
            return cba;
        }

        /// <summary>
        /// ISheetMetalFeatureData of a SheetMetal feature or of the part's defaults, under its own
        /// AccessSelections. Return codes are kept: a non-zero one from the override flags means
        /// they do not apply (single-body or old part). <paramref name="faceOrEdge"/> reads the
        /// fixed reference as a face or edge – never as an axis.
        /// </summary>
        public static SheetMetalParameters ReadParameters(ModelDoc2 doc, Feature f, bool bodyLevel, Func<object?, GeometryRef?>? faceOrEdge = null)
        {
            var d = (SheetMetalFeatureData)(object)f.GetDefinition()
                    ?? throw new NotSupportedException($"Blechparameter von „{f.Name}“ nicht lesbar.");
            if (!d.AccessSelections(doc, null)) throw new NotSupportedException($"Blechparameter von „{f.Name}“ nicht lesbar (AccessSelections).");
            var p = new SheetMetalParameters();
            try
            {
                p.Thickness = d.Thickness;
                p.BendRadius = d.BendRadius;
                p.Relief = new ReliefData
                {
                    UseAutoRelief = d.UseAutoRelief,
                    Kind = (ReliefKind)d.AutoReliefType,
                    UseRatio = true,
                    Ratio = d.ReliefRatio,
                };
                if ((object)d.GetCustomBendAllowance() is CustomBendAllowance cba) p.BendAllowance = ReadAllowance(cba);
                bool useGauge = false;
                string gaugeFile = "";
                if (d.GetUseGaugeTable(out useGauge, out gaugeFile) == 0)
                {
                    p.UseGaugeTable = useGauge;
                    p.GaugeTableFile = gaugeFile ?? "";
                }
                if (SwLate.Get(d, "UseMaterialSheetMetalParameters") is bool material) p.UseMaterialParameters = material;
                if (bodyLevel)
                {
                    bool flag = false;
                    int code = d.GetOverrideDefaultParameter2((int)swSheetMetalOverrideDefaultParameters_e.swSheetMetalOverrideDefaultParameters_BendParameters, out flag);
                    p.OverrideReadCode = code;
                    if (code == 0)
                    {
                        p.OverrideBendParameters = flag;
                        d.GetOverrideDefaultParameter2((int)swSheetMetalOverrideDefaultParameters_e.swSheetMetalOverrideDefaultParameters_BendAllowance, out flag);
                        p.OverrideBendAllowance = flag;
                        d.GetOverrideDefaultParameter2((int)swSheetMetalOverrideDefaultParameters_e.swSheetMetalOverrideDefaultParameters_AutoRelief, out flag);
                        p.OverrideAutoRelief = flag;
                    }
                }
                if (faceOrEdge != null)
                {
                    try
                    {
                        p.FixedReference = faceOrEdge((object)d.FixedReference);
                    }
                    catch (Exception)
                    {
                        // A fixed reference matters for converted and legacy bodies only; unreadable is no reason to stop.
                    }
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return p;
        }

        /// <summary>
        /// Writes parameters into a SheetMetal feature (with <paramref name="bodyLevel"/>: the
        /// override flags in <paramref name="overrides"/> first, then only the overridden groups)
        /// or into the part's defaults, reads them back and returns what does not match.
        /// ModifyDefinition can report success and change nothing; only the read-back counts.
        /// Building a copy, material-driven values become fixed ones; with <paramref name="restore"/>
        /// (the guard writes a snapshot back) the material flag gets its state from
        /// <paramref name="want"/>, and with it on no value is written
        /// (<see cref="SheetMetalRules.MaterialOnWrite"/>).
        /// </summary>
        public static List<string> Apply(ModelDoc2 doc, Feature f, SheetMetalParameters want, bool bodyLevel, IReadOnlyList<int> overrides, bool restore = false)
        {
            var notes = new List<string>();
            string label = bodyLevel ? $"Blechparameter „{f.Name}“" : "Blech-Standardwerte";
            var groups = new HashSet<int>(bodyLevel ? overrides : new[] { 0, 1, 2 });
            (bool? material, bool writeValues) = SheetMetalRules.MaterialOnWrite(restore, want.UseMaterialParameters);
            // Restoring, the material flag may be all a body has to get back.
            if (bodyLevel && groups.Count == 0 && !(restore && material.HasValue)) return notes;
            var d = (SheetMetalFeatureData)(object)f.GetDefinition();
            if (d == null || !d.AccessSelections(doc, null))
            {
                notes.Add($"{label}: nicht änderbar (Feature-Daten nicht zugänglich).");
                return notes;
            }
            bool flagMoves = material.HasValue && SwLate.Get(d, "UseMaterialSheetMetalParameters") is bool now && now != material.Value;
            if (bodyLevel && groups.Count == 0 && !flagMoves)
            {
                d.ReleaseSelectionAccess();
                return notes;
            }
            bool ok = false;
            try
            {
                if (flagMoves) SwLate.Set(d, "UseMaterialSheetMetalParameters", material!.Value);
                if (bodyLevel)
                    foreach (int i in overrides)
                    {
                        int code = d.SetOverrideDefaultParameter2(i, true);
                        if (code != 0)
                        {
                            groups.Remove(i);
                            notes.Add($"{label}: Überschreiben der Standardwerte ({OverrideName(i)}) nicht anwendbar (Code {code}).");
                        }
                    }
                // The material drives the values: none of them is written as a fixed one.
                if (!writeValues) groups.Clear();
                if (groups.Contains(0))
                {
                    d.Thickness = want.Thickness;
                    d.BendRadius = want.BendRadius;
                }
                if (groups.Contains(1) && (object)d.GetCustomBendAllowance() is CustomBendAllowance cba)
                {
                    Fill(cba, want.BendAllowance);
                    d.SetCustomBendAllowance(cba);
                }
                if (groups.Contains(2))
                {
                    d.UseAutoRelief = want.Relief.UseAutoRelief;
                    d.AutoReliefType = (int)want.Relief.Kind;
                    d.ReliefRatio = want.Relief.Ratio;
                }
                ok = f.ModifyDefinition(d, doc, null);
            }
            catch (Exception ex)
            {
                notes.Add($"{label}: nicht vollständig gesetzt ({ex.Message}).");
            }
            finally
            {
                if (!ok) d.ReleaseSelectionAccess();
            }
            if (!ok && notes.Count == 0) notes.Add($"{label}: SOLIDWORKS hat die Änderung nicht angenommen.");
            notes.AddRange(Compare(doc, f, want, bodyLevel, groups));
            return notes;
        }

        /// <summary>Read-back against what should be there: the overridden groups of a body, all of the defaults.</summary>
        public static List<string> Compare(ModelDoc2 doc, Feature f, SheetMetalParameters want, bool bodyLevel, ICollection<int>? groups = null)
        {
            string label = bodyLevel ? $"Blechparameter „{f.Name}“" : "Blech-Standardwerte";
            SheetMetalParameters got;
            try
            {
                got = ReadParameters(doc, f, bodyLevel);
            }
            catch (Exception ex)
            {
                return new List<string> { $"{label}: nicht zurückzulesen ({ex.Message})." };
            }
            // A body takes what it does not override from the defaults: only the overridden groups are its own.
            SheetMetalParameters expected = groups == null || !bodyLevel ? want : Restrict(want, got, groups);
            Deviations dev = SheetMetalCompare.Parameters(expected, got);
            return dev.Differences.Select(x => $"{label}: {x}").Concat(dev.Notes.Select(x => $"{label}: {x}")).ToList();
        }

        /// <summary>The wanted values for the given groups, the read ones for the rest.</summary>
        private static SheetMetalParameters Restrict(SheetMetalParameters want, SheetMetalParameters got, ICollection<int> groups) => new SheetMetalParameters
        {
            Thickness = groups.Contains(0) ? want.Thickness : got.Thickness,
            BendRadius = groups.Contains(0) ? want.BendRadius : got.BendRadius,
            BendAllowance = groups.Contains(1) ? want.BendAllowance : got.BendAllowance,
            Relief = groups.Contains(2) ? want.Relief : got.Relief,
            UseGaugeTable = got.UseGaugeTable,
            GaugeTableFile = got.GaugeTableFile,
        };

        // ---- flanges on edges: bends and scalar readers (A2 P5, P11)

        /// <summary>
        /// The bend sub-features of a feature: those whose definition is IOneBendFeatureData –
        /// counted by definition, not by type name (HemBend, Miterbend and OneBend are not all
        /// confirmed, B23) –, each read under its own AccessSelections, never written. A bend whose
        /// values do not read comes back with null values; it still counts.
        /// </summary>
        public static List<(Feature sub, BendValues? values)> ReadBends(ModelDoc2 doc, Feature f)
        {
            var result = new List<(Feature, BendValues?)>();
            foreach (Feature sub in SwDoc.SubFeatures(f))
            {
                OneBendFeatureData? d;
                try
                {
                    d = (object)sub.GetDefinition() as OneBendFeatureData;
                }
                catch (Exception)
                {
                    d = null;
                }
                if (d == null) continue;
                BendValues? values;
                try
                {
                    values = ReadBend(doc, sub.Name, d);
                }
                catch (Exception)
                {
                    values = null;
                }
                result.Add((sub, values));
            }
            return result;
        }

        private static BendValues ReadBend(ModelDoc2 doc, string name, OneBendFeatureData d)
        {
            var v = new BendValues { Name = name };
            bool access = d.AccessSelections(doc, null);
            try
            {
                v.UseDefaultRadius = d.UseDefaultBendRadius;
                v.Radius = d.BendRadius;
                v.UseDefaultAllowance = d.UseDefaultBendAllowance;
                if (!v.UseDefaultAllowance && (object)d.GetCustomBendAllowance() is CustomBendAllowance cba) v.Allowance = ReadAllowance(cba);
                v.UseDefaultRelief = d.UseDefaultBendRelief;
                if (!v.UseDefaultRelief)
                    v.Relief = new ReliefData
                    {
                        UseAutoRelief = d.UseAutoRelief,
                        Kind = (ReliefKind)d.AutoReliefType,
                        UseRatio = d.UseReliefRatio,
                        Ratio = d.ReliefRatio,
                        Width = d.ReliefWidth,
                        Depth = d.ReliefDepth,
                    };
                v.BendOrder = d.BendOrder;
            }
            finally
            {
                if (access) d.ReleaseSelectionAccess();
            }
            return v;
        }

        /// <summary>
        /// A feature's own allowance: null if it uses the default. <paramref name="unreadable"/>: it
        /// has one of its own that does not read (then null too – the copy gets the default, with a note).
        /// </summary>
        private static BendAllowanceData? OwnAllowanceOf(bool useDefault, Func<object?> get, out bool unreadable)
        {
            unreadable = false;
            if (useDefault) return null;
            if (get() is CustomBendAllowance cba) return ReadAllowance(cba);
            unreadable = true;
            return null;
        }

        /// <summary>
        /// The scalars of a hem into a record – the same code at capture (the original) and for the
        /// read-back (the copy, into a fresh record). Under the caller's AccessSelections; no sketch,
        /// no rollback. Returns whether an own allowance did not read.
        /// </summary>
        public static bool ReadHemScalars(HemFeatureData d, HemRecord r)
        {
            r.HemType = d.Type;
            r.Position = d.BendPosition;
            r.Reverse = d.ReverseDirection;
            r.Length = d.Length;
            r.Gap = d.GapDistance;
            r.Angle = d.Angle;
            r.Radius = d.Radius;
            r.MiterGap = d.MiterGap;
            r.OwnAllowance = OwnAllowanceOf(d.UseDefaultBendAllowance, () => (object)d.GetCustomBendAllowance(), out bool unreadable);
            return unreadable;
        }

        /// <summary>The scalars of a miter flange into a record (as <see cref="ReadHemScalars"/>); returns whether an own allowance did not read.</summary>
        public static bool ReadMiterScalars(MiterFlangeFeatureData d, MiterFlangeRecord r)
        {
            r.UseDefaultRadius = d.UseDefaultBendRadius;
            r.BendRadius = d.BendRadius;
            r.PositionType = d.PositionType;
            r.TrimSideBends = d.UsePositionTrimSideBends;
            r.GapDistance = d.GapDistance;
            r.StartOffset = d.StartOffset;
            r.EndOffset = d.EndOffset;
            r.OwnRelief = d.UseDefaultBendRelief
                ? null
                : new ReliefData
                {
                    UseAutoRelief = true,
                    Kind = (ReliefKind)d.ReliefType,
                    UseRatio = d.UseReliefRatio,
                    Ratio = d.ReliefRatio,
                    Width = d.ReliefWidth,
                    Depth = d.ReliefDepth,
                };
            r.OwnAllowance = OwnAllowanceOf(d.UseDefaultBendAllowance, () => (object)d.GetCustomBendAllowance(), out bool unreadable);
            return unreadable;
        }

        /// <summary>
        /// The scalars of an edge flange into a record (as <see cref="ReadHemScalars"/>): the values,
        /// both direction flags, the relief with its tear type, the allowance, and late-bound what only
        /// newer versions have (lock angle, normal to the flange plane, perpendicular to the face).
        /// Edges and references are the caller's. Returns whether an own allowance did not read.
        /// </summary>
        public static bool ReadEdgeFlangeScalars(EdgeFlangeFeatureData d, EdgeFlangeRecord r)
        {
            r.BendAngle = d.BendAngle;
            r.UseDefaultRadius = d.UseDefaultBendRadius;
            r.BendRadius = d.BendRadius;
            r.OffsetType = d.OffsetType;
            r.OffsetDistance = d.OffsetDistance;
            r.OffsetDimType = d.OffsetDimType;
            r.ReverseOffset = d.ReverseOffset;
            r.PositionType = d.PositionType;
            r.TrimSideBends = d.UsePositionTrimSideBends;
            r.UsePositionOffset = d.UsePositionOffset;
            r.PositionOffsetType = d.PositionOffsetType;
            r.PositionOffsetDistance = d.PositionOffsetDistance;
            r.ReversePositionOffset = d.ReversePositionOffset;
            r.GapDistance = d.GapDistance;
            r.OwnRelief = d.UseDefaultBendRelief
                ? null
                : new ReliefData
                {
                    UseAutoRelief = true,
                    Kind = (ReliefKind)d.AutoReliefType,
                    UseRatio = d.UseReliefRatio,
                    Ratio = d.ReliefRatio,
                    Width = d.ReliefWidth,
                    Depth = d.ReliefDepth,
                    TearType = d.ReliefTearType,
                };
            r.LockAngle = SwLate.Get(d, "LockAngle") is bool lockAngle ? lockAngle : (bool?)null;
            r.NormalToFlangePlane = SwLate.Get(d, "NormalToFlangePlane") is bool normal ? normal : (bool?)null;
            r.PerpendicularToFace = SwLate.Get(d, "PerpendicularToFace") is bool perpendicular ? perpendicular : (bool?)null;
            r.OwnAllowance = OwnAllowanceOf(d.UseDefaultBendAllowance, () => (object)d.GetCustomBendAllowance(), out bool unreadable);
            return unreadable;
        }

        /// <summary>The edge flange as it is now (as <see cref="ReadBackHem"/>); null if its data do not open.</summary>
        public static EdgeFlangeRecord? ReadBackEdgeFlange(ModelDoc2 doc, Feature f, out int edges)
        {
            edges = -1;
            var d = (object)f.GetDefinition() as EdgeFlangeFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var r = new EdgeFlangeRecord(f.Name, "EdgeFlange");
                ReadEdgeFlangeScalars(d, r);
                edges = d.GetEdgeCount();
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>The hem as it is now, read back under its own AccessSelections (no capture, no rollback); null if its data do not open.</summary>
        public static HemRecord? ReadBackHem(ModelDoc2 doc, Feature f, out int edges)
        {
            edges = -1;
            var d = (object)f.GetDefinition() as HemFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var r = new HemRecord(f.Name, "Hem");
                ReadHemScalars(d, r);
                edges = d.GetEdgesCount();
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>The miter flange as it is now (as <see cref="ReadBackHem"/>); null if its data do not open.</summary>
        public static MiterFlangeRecord? ReadBackMiter(ModelDoc2 doc, Feature f, out int edges)
        {
            edges = -1;
            var d = (object)f.GetDefinition() as MiterFlangeFeatureData;
            if (d == null || !d.AccessSelections(doc, null)) return null;
            try
            {
                var r = new MiterFlangeRecord(f.Name, "SMMiteredFlange", "");
                ReadMiterScalars(d, r);
                edges = d.IGetEdgesCount();
                return r;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        private static string OverrideName(int i) => i switch
        {
            0 => "Biegeparameter",
            1 => "Biegezugabe",
            _ => "automatische Entlastung",
        };
    }
}
