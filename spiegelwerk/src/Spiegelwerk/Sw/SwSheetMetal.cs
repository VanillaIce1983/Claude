using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
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
            TreeFeature Node(Feature f, IEnumerable<Feature> subs)
            {
                if (f.GetTypeName2() == "FlatPattern" && !facts.ContainsKey(f.Name)) facts[f.Name] = Facts(doc, f);
                List<string> parents = f.GetParents() is object[] ps ? ps.OfType<Feature>().Select(p => p.Name).ToList() : new List<string>();
                return new TreeFeature(f.Name, f.GetTypeName2(), parents, subs.Select(s => Node(s, SwDoc.SubFeatures(s))).ToList());
            }
            foreach (Feature f in SwDoc.TopLevelFeatures(doc))
            {
                List<Feature> subs = SwDoc.SubFeatures(f).ToList();
                foreach (Feature c in FolderContents(doc, f))
                    if (subs.All(s => s.Name != c.Name)) subs.Add(c);
                top.Add(Node(f, subs));
            }
            return SheetMetalTree.Of(top, facts);
        }

        /// <summary>Own corner trim and break corners (the flat pattern's options) and the features of its body; what is not readable stays empty.</summary>
        private static FlatPatternFacts Facts(ModelDoc2 doc, Feature flatPattern)
        {
            string? trim = null, breakCorner = null;
            var body = new List<string>();
            try
            {
                if ((object)flatPattern.GetDefinition() is FlatPatternFeatureData d)
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
            try
            {
                foreach (Body2 b in SwDoc.SolidBodies(doc))
                    if (b.GetFeatures() is object[] fs && fs.OfType<Feature>().Any(f => f.Name == flatPattern.Name))
                        body.AddRange(fs.OfType<Feature>().Select(f => f.Name));
            }
            catch (Exception)
            {
                // Its owner is then not known; the capture says so.
            }
            return new FlatPatternFacts(trim, breakCorner, body);
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
        /// Material-driven values become fixed ones.
        /// </summary>
        public static List<string> Apply(ModelDoc2 doc, Feature f, SheetMetalParameters want, bool bodyLevel, IReadOnlyList<int> overrides)
        {
            var notes = new List<string>();
            string label = bodyLevel ? $"Blechparameter „{f.Name}“" : "Blech-Standardwerte";
            var groups = new HashSet<int>(bodyLevel ? overrides : new[] { 0, 1, 2 });
            if (bodyLevel && groups.Count == 0) return notes;
            var d = (SheetMetalFeatureData)(object)f.GetDefinition();
            if (d == null || !d.AccessSelections(doc, null))
            {
                notes.Add($"{label}: nicht änderbar (Feature-Daten nicht zugänglich).");
                return notes;
            }
            bool ok = false;
            try
            {
                if (SwLate.Get(d, "UseMaterialSheetMetalParameters") is bool material && material)
                    SwLate.Set(d, "UseMaterialSheetMetalParameters", false);
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

        private static string OverrideName(int i) => i switch
        {
            0 => "Biegeparameter",
            1 => "Biegezugabe",
            _ => "automatische Entlastung",
        };
    }
}
