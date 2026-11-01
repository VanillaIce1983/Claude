using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>A feature of the tree as the sheet-metal rules need it: name, type, parents and sub-features.</summary>
    public sealed class TreeFeature
    {
        public TreeFeature(string name, string type, IEnumerable<string>? parents = null, IEnumerable<TreeFeature>? subFeatures = null)
        {
            Name = name;
            Type = type;
            Parents = parents?.ToList() ?? new List<string>();
            SubFeatures = subFeatures?.ToList() ?? new List<TreeFeature>();
        }

        public string Name { get; }

        /// <summary>IFeature.GetTypeName2.</summary>
        public string Type { get; }

        public IReadOnlyList<string> Parents { get; }

        /// <summary>Sub-features; for a sheet-metal or flat-pattern folder its contents.</summary>
        public IReadOnlyList<TreeFeature> SubFeatures { get; }
    }

    /// <summary>What the add-in knows about a flat pattern from its data and its body.</summary>
    public sealed class FlatPatternFacts
    {
        /// <param name="ownCornerTrim">The flat pattern's own corner trim (IFlatPatternFeatureData.GetAddCornerTrim), if any.</param>
        /// <param name="ownBreakCorner">Its own break corners (GetBreakCorners), if any.</param>
        /// <param name="bodyFeatures">The features of its body (IBody2.GetFeatures), in any order; empty if unknown.</param>
        public FlatPatternFacts(string? ownCornerTrim = null, string? ownBreakCorner = null, IEnumerable<string>? bodyFeatures = null)
        {
            OwnCornerTrim = ownCornerTrim;
            OwnBreakCorner = ownBreakCorner;
            BodyFeatures = bodyFeatures?.ToList() ?? new List<string>();
        }

        public string? OwnCornerTrim { get; }
        public string? OwnBreakCorner { get; }
        public IReadOnlyList<string> BodyFeatures { get; }
    }

    /// <summary>
    /// Who in a sheet-metal part comes into being with whom (A1 §2, A3 P3), as pure name logic:
    ///  - a SheetMetal feature belongs to the first base flange (tree order) that has it as a parent;
    ///    without one it is a carrier of its own (a converted, lofted or swept body);
    ///  - the sketches under a flat pattern and its own corner trim and break corners belong to the
    ///    flat pattern; a corner trim the user inserted under it is not rebuilt;
    ///  - a flat pattern belongs to the first base flange of its body; if its body does not name
    ///    it (B9: the run reads the tree folded, the flat pattern suppressed), to the base flange
    ///    its parents lead to, or in a part with one flat pattern and one SheetMetal to that
    ///    SheetMetal's base flange;
    ///  - features after a flat pattern exist only in the flattened state.
    /// Template folders are system features: never collected, their flat patterns listed before them.
    /// </summary>
    public sealed class SheetMetalTree
    {
        private readonly Dictionary<string, (string owner, PartOwnerKind kind)> _owners = new Dictionary<string, (string, PartOwnerKind)>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _flatPatternOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _ownerFromTree = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _userCornerTrims = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _afterFlatPattern = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _flatPatternParts = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _types = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _order = new Dictionary<string, int>(StringComparer.Ordinal);

        private SheetMetalTree()
        {
        }

        /// <summary>A part without sheet metal.</summary>
        public static SheetMetalTree Empty { get; } = new SheetMetalTree();

        /// <param name="topLevel">Top-level features in tree order; folders carry their contents as sub-features.</param>
        /// <param name="flatPatterns">Flat pattern name → what its data and body say.</param>
        public static SheetMetalTree Of(IEnumerable<TreeFeature> topLevel, IReadOnlyDictionary<string, FlatPatternFacts>? flatPatterns = null)
        {
            var tree = new SheetMetalTree();
            List<TreeFeature> top = topLevel.ToList();
            var all = new List<TreeFeature>();
            void Walk(TreeFeature f)
            {
                all.Add(f);
                tree._types[f.Name] = f.Type;
                tree._order[f.Name] = tree._order.Count;
                foreach (TreeFeature sub in f.SubFeatures) Walk(sub);
            }
            foreach (TreeFeature f in top) Walk(f);

            // SheetMetal → the first base flange (tree order) with it as a parent.
            foreach (TreeFeature sm in all.Where(f => f.Type == "SheetMetal"))
            {
                TreeFeature? flange = all.FirstOrDefault(f => f.Type == "SMBaseFlange" && f.Parents.Contains(sm.Name));
                if (flange != null) tree._owners[sm.Name] = (flange.Name, PartOwnerKind.SheetMetal);
            }

            foreach (TreeFeature fp in all.Where(f => f.Type == "FlatPattern"))
            {
                FlatPatternFacts facts = flatPatterns != null && flatPatterns.TryGetValue(fp.Name, out FlatPatternFacts known) ? known : new FlatPatternFacts();
                foreach (TreeFeature sub in fp.SubFeatures)
                {
                    tree._flatPatternParts.Add(sub.Name);
                    if (IsSketchType(sub.Type) || sub.Name == facts.OwnCornerTrim || sub.Name == facts.OwnBreakCorner)
                        tree._owners[sub.Name] = (fp.Name, PartOwnerKind.FlatPattern);
                    else if (sub.Type == "CornerTrim")
                        tree._userCornerTrims.Add(sub.Name);
                }
                // The first base flange of its body (by tree order: GetFeatures says nothing about order).
                string? owner = facts.BodyFeatures.Where(n => tree._types.TryGetValue(n, out string t) && t == "SMBaseFlange")
                    .OrderBy(n => tree._order[n]).FirstOrDefault();
                if (owner == null)
                {
                    owner = tree.OwnerByTree(fp, all);
                    if (owner != null) tree._ownerFromTree.Add(fp.Name);
                }
                if (owner != null) tree._flatPatternOwners[fp.Name] = owner;
            }

            // Top-level features after the first flat pattern (or its folder) exist only flattened.
            int first = top.FindIndex(f => f.Type == "FlatPattern" || f.Type == "TemplateFlatPattern");
            if (first >= 0)
                foreach (TreeFeature f in top.Skip(first + 1).Where(f => !IsSystemType(f.Type) && f.Type != "FlatPattern"))
                    tree._afterFlatPattern.Add(f.Name);
            return tree;
        }

        private static bool IsSketchType(string type) => type == "ProfileFeature" || type == "3DProfileFeature";

        /// <summary>
        /// The owner of a flat pattern whose body names no base flange (B9 unconfirmed: a folded,
        /// suppressed flat pattern may be missing from GetFeatures of its body): the base flange its
        /// parents lead to; else, with one flat pattern and one SheetMetal in the part, the base
        /// flange of that SheetMetal. Null if neither says.
        /// </summary>
        private string? OwnerByTree(TreeFeature fp, List<TreeFeature> all)
        {
            foreach (string p in fp.Parents.Where(_types.ContainsKey).OrderBy(p => _order[p]))
                if (BaseFlangeOf(p, all) is string owner)
                    return owner;
            // A feature can be walked twice (at the top level and in its folder): count names, not entries.
            List<string> sheetMetals = all.Where(f => f.Type == "SheetMetal").Select(f => f.Name).Distinct(StringComparer.Ordinal).ToList();
            int flatPatterns = all.Where(f => f.Type == "FlatPattern").Select(f => f.Name).Distinct(StringComparer.Ordinal).Count();
            if (flatPatterns == 1 && sheetMetals.Count == 1 &&
                _owners.TryGetValue(sheetMetals[0], out var only) && only.kind == PartOwnerKind.SheetMetal)
                return only.owner;
            return null;
        }

        /// <summary>
        /// The base flange that made the body of a feature, by name: a SheetMetal → its owner; a base
        /// flange → the owner of its SheetMetal (a tab shares the one of its body), itself without
        /// one. Anything else: null.
        /// </summary>
        private string? BaseFlangeOf(string name, List<TreeFeature> all)
        {
            string type = _types[name];
            if (type == "SheetMetal")
                return _owners.TryGetValue(name, out var sm) && sm.kind == PartOwnerKind.SheetMetal ? sm.owner : null;
            if (type != "SMBaseFlange") return null;
            TreeFeature flange = all.First(f => f.Name == name);
            foreach (string p in flange.Parents.Where(p => _types.TryGetValue(p, out string t) && t == "SheetMetal"))
                if (_owners.TryGetValue(p, out var owned) && owned.kind == PartOwnerKind.SheetMetal)
                    return owned.owner;
            return name;
        }

        /// <summary>Template folders (<see cref="SheetMetalRole.System"/>): never collected, never deleted, never new.</summary>
        public static bool IsSystemType(string type) => SheetMetalTypes.Of(type)?.Role == SheetMetalRole.System;

        /// <summary>
        /// Which sub-features the creation order lists with their owner (before it, like absorbed
        /// sketches): sketches everywhere, the flat patterns of a flat-pattern folder, and the
        /// non-sketch sub-features of a flat pattern (its corner trims). The SheetMetal features in
        /// a sheet-metal folder are not listed: they need no record of their own.
        /// </summary>
        public static bool ListsSubFeature(string ownerType, string subType) =>
            IsSketchType(subType) ||
            (ownerType == "TemplateFlatPattern" && subType == "FlatPattern") ||
            ownerType == "FlatPattern";

        /// <summary>Part name → its owner and the owner's kind (SheetMetal → base flange; flat-pattern sketches and own corners → flat pattern).</summary>
        public IReadOnlyDictionary<string, (string owner, PartOwnerKind kind)> Owners => _owners;

        /// <summary>Flat pattern → the base flange of its body.</summary>
        public IReadOnlyDictionary<string, string> FlatPatternOwners => _flatPatternOwners;

        /// <summary>The flat pattern's owner comes from the tree, not from its body (see <see cref="OwnerFromTreeNote"/>).</summary>
        public bool OwnerFromTree(string flatPattern) => _ownerFromTree.Contains(flatPattern);

        /// <summary>Corner trims the user inserted under a flat pattern: not rebuilt (their relief is not readable).</summary>
        public bool IsUserCornerTrim(string name) => _userCornerTrims.Contains(name);

        /// <summary>A top-level feature after a flat pattern: it exists only in the flattened state.</summary>
        public bool IsAfterFlatPattern(string name) => _afterFlatPattern.Contains(name);

        /// <summary>
        /// A flat pattern or one of its sub-features: suppressed whenever the part is folded, which
        /// the run does on purpose – no reason to skip it.
        /// </summary>
        public bool SuppressedWhenFolded(string name) =>
            _flatPatternParts.Contains(name) || (_types.TryGetValue(name, out string type) && type == "FlatPattern");

        /// <summary>The SheetMetal feature has no base flange of its own: its body comes from something else (a carrier).</summary>
        public bool IsUnownedSheetMetal(string name) =>
            _types.TryGetValue(name, out string type) && type == "SheetMetal" && !_owners.ContainsKey(name);

        /// <summary>
        /// What a selection brings along (A1 P6): a base flange its SheetMetal and the flat pattern of
        /// its body, a flat pattern its sketches and own corner features. Returns the names to add.
        /// </summary>
        public IReadOnlyCollection<string> Closure(IEnumerable<string> wanted)
        {
            var set = new HashSet<string>(wanted, StringComparer.Ordinal);
            var added = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(set);
            void Add(string n)
            {
                if (set.Add(n))
                {
                    added.Add(n);
                    queue.Enqueue(n);
                }
            }
            while (queue.Count > 0)
            {
                string n = queue.Dequeue();
                if (!_types.TryGetValue(n, out string type)) continue;
                if (type == "SMBaseFlange")
                {
                    foreach (var kv in _owners.Where(kv => kv.Value.owner == n)) Add(kv.Key);
                    foreach (var kv in _flatPatternOwners.Where(kv => kv.Value == n)) Add(kv.Key);
                }
                else if (type == "FlatPattern")
                {
                    foreach (var kv in _owners.Where(kv => kv.Value.owner == n)) Add(kv.Key);
                }
            }
            return added;
        }

        /// <summary>The flat pattern's report text when its base flange is not rebuilt.</summary>
        public static string FlatPatternWithoutOwner(string owner) =>
            owner.Length == 0
                ? "Abwicklung entsteht nur mit ihrer Basislasche; diese ließ sich nicht bestimmen."
                : $"Abwicklung entsteht nur mit ihrer Basislasche „{owner}“, die nicht gespiegelt wird.";

        /// <summary>Detail of a flat pattern whose owner its body did not name.</summary>
        public const string OwnerFromTreeNote = "Basislasche der Abwicklung aus dem Feature-Baum bestimmt (über ihren Körper nicht lesbar).";

        /// <summary>Report text of a top-level feature after a flat pattern, suppressed while the part is folded.</summary>
        public const string OnlyFlattened = "Nur in der Abwicklung vorhanden – nicht gespiegelt.";
    }
}
