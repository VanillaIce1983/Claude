using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;
using static Spiegelwerk.Build.Builders.PatternSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Variable pattern: a table of instances, each the seed with its own values for chosen
    /// dimensions. The seed is rebuilt with the same dimension values, so the table carries
    /// over unchanged; what changes are the names of its columns. Every column of the original
    /// ("D1@Skizze2") is looked up in the copy: a sketch dimension by its original name in the
    /// rebuilt sketch (SketchBuilder gives it that name back), a feature dimension by name and
    /// value in the rebuilt feature.
    ///
    /// Creation: the seed features selected, the feature data object
    /// (CreateDefinition(swFmDimPattern), CreateFeature) as in the API example, or
    /// FeatureAdvancedTableDrivenPattern. Then, in one edit, each column added by selecting its
    /// dimension (AddDimension), rows added or removed (AddInstanceAt/DeleteInstanceAt), the
    /// values and suppression states set. The table is read back and compared. The API
    /// reports no placements; the mass check decides.
    /// </summary>
    internal sealed class VariablePatternBuilder : IFeatureBuilder
    {
        private const int SeedMark = 4;

        public bool Handles(FeatureRecord record) => record is VariablePatternRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (VariablePatternRecord)record;
            var notes = new List<string>();

            var seeds = new List<Feature>();
            foreach (string name in r.SeedFeatures)
            {
                if (!ctx.Created.TryGetValue(name, out Feature seed))
                    return Fail($"Saat „{name}“ des variablen Musters wurde nicht gespiegelt.", notes);
                seeds.Add(seed);
            }
            var columns = new List<(DisplayDimension dimension, string name)>();
            foreach (ControllingDimension c in r.Dimensions)
            {
                (DisplayDimension, string)? column = Column(ctx, c);
                if (column == null) return Fail($"Bemaßung „{c}“ des variablen Musters im Ziel nicht gefunden.", notes);
                columns.Add(column.Value);
            }

            Feature? f = Create(ctx, seeds);
            if (f == null) return Fail("SOLIDWORKS hat das variable Muster nicht erzeugt.", notes);

            var editNotes = new List<string>();
            bool ok = FeatureEdit.VariablePattern(ctx.Doc, f, d => FillTable(ctx.Doc, d, r, columns, editNotes));
            notes.AddRange(editNotes);
            if (!ok) notes.Add("Die Tabelle des variablen Musters ließ sich nicht übernehmen.");
            List<string> differing = Compare(ctx.Doc, f, r, columns);
            if (differing.Count > 0) notes.Add($"Tabelle weicht ab: {string.Join("; ", differing)}.");

            ctx.Register(r.Name, f);
            return Ok(f, $"Variables Muster, {r.Rows.Count} Instanzen, {r.Dimensions.Count} Bemaßungen ({string.Join(", ", r.Dimensions)})", notes);
        }

        // ---------------------------------------------------------------- columns

        /// <summary>The copy's dimension for a column of the original, and the column name it gives.</summary>
        private static (DisplayDimension, string)? Column(BuildContext ctx, ControllingDimension c)
        {
            if (c.OwnerIsSketch)
            {
                if (!ctx.Sketches.TryGetValue(c.Owner, out SketchMap map) || !map.Dimensions.TryGetValue(c.Name, out DisplayDimension dd)) return null;
                return (dd, ColumnName(dd, map.Feature));
            }
            if (!ctx.Created.TryGetValue(c.Owner, out Feature owner)) return null;
            DisplayDimension? found = FeatureDimension(owner, c);
            return found == null ? ((DisplayDimension, string)?)null : (found, ColumnName(found, owner));
        }

        /// <summary>
        /// A dimension of a rebuilt feature: the one with the same name and value; otherwise the
        /// only one with the same value (the creation order, which names them, may differ).
        /// </summary>
        private static DisplayDimension? FeatureDimension(Feature owner, ControllingDimension c)
        {
            var all = new List<DisplayDimension>();
            for (object? o = owner.GetFirstDisplayDimension(); o is DisplayDimension dd; o = owner.GetNextDisplayDimension(dd)) all.Add(dd);
            bool SameValue(DisplayDimension dd) => Math.Abs(dd.GetDimension2(0).SystemValue - c.Value) <= Math.Max(1e-9, Math.Abs(c.Value) * 1e-7);
            DisplayDimension? byName = all.FirstOrDefault(dd => dd.GetDimension2(0).Name == c.Name && SameValue(dd));
            if (byName != null) return byName;
            List<DisplayDimension> byValue = all.Where(SameValue).ToList();
            return byValue.Count == 1 ? byValue[0] : null;
        }

        /// <summary>Same column, whether or not one of the names carries "@Document" after it.</summary>
        private static bool SameColumn(string a, string b) =>
            Capture.FeatureCapture.SplitColumn(a) is (string, string) x && Capture.FeatureCapture.SplitColumn(b) is (string, string) y
                ? x.name == y.name && x.owner == y.owner
                : a == b;

        /// <summary>"Name@Owner" as the table names its columns.</summary>
        private static string ColumnName(DisplayDimension dd, Feature owner) => $"{dd.GetDimension2(0).Name}@{owner.Name}";

        // ---------------------------------------------------------------- creation

        private static Feature? Create(BuildContext ctx, List<Feature> seeds)
        {
            ModelDoc2 doc = ctx.Doc;
            FeatureManager fm = doc.FeatureManager;
            var attempts = new Func<Feature?>[]
            {
                () => fm.CreateDefinition((int)swFeatureNameID_e.swFmDimPattern) is DimPatternFeatureData d ? fm.CreateFeature(d) : null,
                () => fm.FeatureAdvancedTableDrivenPattern(),
            };
            foreach (int mark in new[] { SeedMark, 0 })
                foreach (Func<Feature?> attempt in attempts)
                {
                    try
                    {
                        SwSelect.Clear(doc);
                        bool selected = true;
                        for (int i = 0; i < seeds.Count; i++) selected &= SwSelect.Select(doc, seeds[i], mark, i > 0);
                        if (!selected) continue;
                        if (attempt() is Feature f && f.GetDefinition() is DimPatternFeatureData) return f;
                    }
                    catch (Exception)
                    {
                        // Refused in this form – the next one follows.
                    }
                    finally
                    {
                        SwSelect.Clear(doc);
                    }
                }
            return null;
        }

        // ---------------------------------------------------------------- table

        /// <summary>
        /// Within one edit: missing columns added, rows added or removed, values and suppression
        /// set. Whether row indices count from 0 or 1 is not documented: read off the row names.
        /// </summary>
        private static void FillTable(ModelDoc2 doc, DimPatternFeatureData d, VariablePatternRecord r, List<(DisplayDimension dimension, string name)> columns,
            List<string> notes)
        {
            List<string> present = ColumnNames(d);
            foreach (var (dimension, name) in columns)
            {
                if (present.Any(p => SameColumn(p, name))) continue;
                SwSelect.Clear(doc);
                bool added = ((Annotation)dimension.GetAnnotation()).Select3(false, null) && d.AddDimension();
                SwSelect.Clear(doc);
                if (!added) notes.Add($"Spalte „{name}“ ließ sich nicht anlegen.");
            }
            foreach (string extra in ColumnNames(d).Where(n => !columns.Any(c => SameColumn(c.name, n))).ToList())
                if (!d.DeleteDimension(extra)) notes.Add($"Überzählige Spalte „{extra}“ ließ sich nicht entfernen.");

            int first = FirstRow(d);
            for (int guard = 0; d.GetInstanceCount() < r.Rows.Count && guard < r.Rows.Count; guard++)
                if (!d.AddInstanceAt(false, d.GetInstanceCount() + first)) break;
            for (int guard = 0; d.GetInstanceCount() > r.Rows.Count && guard < 1000; guard++)
                if (!d.DeleteInstanceAt(d.GetInstanceCount() - 1 + first)) break;
            if (d.GetInstanceCount() != r.Rows.Count)
                notes.Add($"Die Kopie hat {d.GetInstanceCount()} statt {r.Rows.Count} Instanzen.");
            first = FirstRow(d);

            // The names as the table reports them (with or without "@Document").
            List<string> actual = ColumnNames(d);
            List<string> names = columns.Select(c => actual.FirstOrDefault(n => SameColumn(n, c.name)) ?? c.name).ToList();
            int rows = Math.Min(d.GetInstanceCount(), r.Rows.Count);
            for (int i = 0; i < rows; i++)
            {
                VariableInstance row = r.Rows[i];
                for (int j = 0; j < columns.Count; j++)
                    if (row.Values[j] is double value) d.SetInstanceDimensionValue(i + first, names[j], value);
                d.SetInstanceSuppressStateByIndex(i + first, row.Suppressed);
            }
            if (d.PropagateVisualProperties != r.PropagateVisualProperties) d.PropagateVisualProperties = r.PropagateVisualProperties;
        }

        private static List<string> ColumnNames(DimPatternFeatureData d)
        {
            var names = new List<string>();
            int count = d.GetControllingDimensionCount();
            foreach (int first in new[] { 0, 1 })
            {
                names.Clear();
                try
                {
                    for (int i = first; i < first + count; i++) names.Add(d.GetControllingDimensionName(i));
                }
                catch (Exception)
                {
                    continue;
                }
                if (names.All(n => !string.IsNullOrEmpty(n))) return names;
            }
            return new List<string>();
        }

        /// <summary>Index of the first row: 0 if a name can be read there, otherwise 1.</summary>
        private static int FirstRow(DimPatternFeatureData d)
        {
            if (d.GetInstanceCount() == 0) return 0;
            try
            {
                return string.IsNullOrEmpty(d.GetInstanceNameByIndex(0)) ? 1 : 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        /// <summary>Rows, values and suppression of the copy against the original.</summary>
        private static List<string> Compare(ModelDoc2 doc, Feature f, VariablePatternRecord r, List<(DisplayDimension dimension, string name)> columns)
        {
            var result = new List<string>();
            try
            {
                var d = (DimPatternFeatureData)f.GetDefinition();
                if (!d.AccessSelections(doc, null)) return result;
                try
                {
                    List<string> present = ColumnNames(d);
                    foreach (var (_, name) in columns.Where(c => !present.Any(p => SameColumn(p, c.name)))) result.Add($"Spalte „{name}“ fehlt");
                    int count = d.GetInstanceCount();
                    if (count != r.Rows.Count) result.Add($"{count} statt {r.Rows.Count} Instanzen");
                    int first = FirstRow(d);
                    for (int i = 0; i < Math.Min(count, r.Rows.Count); i++)
                    {
                        string instance = d.GetInstanceNameByIndex(i + first);
                        if (d.GetInstanceSuppressStateByIndex(i + first) != r.Rows[i].Suppressed) result.Add($"Instanz {i + 1}: Unterdrückung");
                        for (int j = 0; j < columns.Count; j++)
                        {
                            string? column = present.FirstOrDefault(p => SameColumn(p, columns[j].name));
                            if (column == null || !(r.Rows[i].Values[j] is double wanted)) continue;
                            double? now = doc.Parameter(d.GetInstanceDimensionName(instance, column)) is Dimension dim ? dim.SystemValue : (double?)null;
                            if (now == null || Math.Abs(now.Value - wanted) > Math.Max(1e-9, Math.Abs(wanted) * 1e-7))
                                result.Add($"Instanz {i + 1}, {columns[j].name}");
                        }
                    }
                }
                finally
                {
                    d.ReleaseSelectionAccess();
                }
            }
            catch (Exception ex)
            {
                result.Add($"nicht lesbar ({ex.Message})");
            }
            return result;
        }
    }
}
