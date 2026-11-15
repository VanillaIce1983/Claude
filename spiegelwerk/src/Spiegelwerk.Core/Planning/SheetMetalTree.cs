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

    /// <summary>What the add-in knows about an edge flange from its data (A2 P13).</summary>
    public sealed class EdgeFlangeFacts
    {
        /// <param name="edgeCount">Its number of edges.</param>
        /// <param name="parentsOnHinge">Top-level parent sketches whose support edge (ISketch.GetReferenceEntity) is one of its edges.</param>
        public EdgeFlangeFacts(int edgeCount, IEnumerable<string>? parentsOnHinge = null)
        {
            EdgeCount = edgeCount;
            ParentsOnHinge = parentsOnHinge?.ToList() ?? new List<string>();
        }

        public int EdgeCount { get; }
        public IReadOnlyList<string> ParentsOnHinge { get; }
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
    ///  - features after a flat pattern exist only in the flattened state;
    ///  - the width sketches of a hem belong to it (S2): the new hem brings its own, they are written into those;
    ///  - the sketch of a cross break (its break lines, B48) belongs to it (S5), alike;
    ///  - the profile sketches of an edge flange belong to it (S3): its sketch sub-features and – the layout
    ///    SOLIDWORKS sometimes shows (tbcphutai) – a top-level parent sketch, but only one that is absorbed by
    ///    nothing, stands on one of its edges, is no other feature's parent, while the flange has fewer
    ///    profile sub-sketches than edges (A2 P13: not an up-to reference, not a shared sketch);
    ///  - legacy Insert Bends (S6, A1 phase 1b): a SheetMetal without a base flange whose FlattenBends and
    ///    ProcessBends follow it – by their parents, else the next ones in tree order before the next
    ///    SheetMetal – is their anchor, a carrier of its own (<see cref="LegacyAnchors"/>); FlattenBends and
    ///    ProcessBends belong to it. A flat pattern of such a body belongs to that SheetMetal;
    ///  - the slot feature of a tab and slot (S8, B64) belongs to it: the first feature after the tab in tree
    ///    order that is its child and answers the same data, has its type or „Slot“ in its type – or whose name
    ///    is the tab's with another word (<see cref="TabSlotRules.IsSlotName"/>).
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
        private readonly Dictionary<string, (string? flatten, string? process)> _legacy = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _legacyBetween = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _slots = new Dictionary<string, string>(StringComparer.Ordinal);

        private SheetMetalTree()
        {
        }

        /// <summary>A part without sheet metal.</summary>
        public static SheetMetalTree Empty { get; } = new SheetMetalTree();

        /// <param name="topLevel">Top-level features in tree order; folders carry their contents as sub-features.</param>
        /// <param name="flatPatterns">Flat pattern name → what its data and body say.</param>
        /// <param name="edgeFlanges">Edge flange name → what its data say (only needed where a top-level parent sketch may be a profile).</param>
        /// <param name="tabAndSlots">The features whose data answer as tab and slot (its type name is not confirmed, B64).</param>
        public static SheetMetalTree Of(IEnumerable<TreeFeature> topLevel, IReadOnlyDictionary<string, FlatPatternFacts>? flatPatterns = null,
            IReadOnlyDictionary<string, EdgeFlangeFacts>? edgeFlanges = null, IReadOnlyCollection<string>? tabAndSlots = null)
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

            tree.FindLegacyBends(top, all);

            // A hem's sketches are its width sketches: they come into being with it.
            foreach (TreeFeature hem in all.Where(f => f.Type == "Hem"))
                foreach (TreeFeature sub in hem.SubFeatures.Where(s => IsSketchType(s.Type)))
                    tree._owners[sub.Name] = (hem.Name, PartOwnerKind.Hem);

            if (tabAndSlots != null && tabAndSlots.Count > 0) tree.FindSlots(all, tabAndSlots);

            // A cross break's sketch holds its break lines (B48): the new cross break brings its own, the lines are written into it.
            foreach (TreeFeature cross in all.Where(f => f.Type == "CrossBreak"))
                foreach (TreeFeature sub in cross.SubFeatures.Where(s => IsSketchType(s.Type)))
                    tree._owners[sub.Name] = (cross.Name, PartOwnerKind.CrossBreak);

            // An edge flange's sketches are its profiles; a top-level parent only under the P13 rule.
            foreach (TreeFeature flange in all.Where(f => f.Type == "EdgeFlange"))
            {
                List<TreeFeature> profiles = flange.SubFeatures.Where(s => IsSketchType(s.Type)).ToList();
                foreach (TreeFeature sub in profiles) tree._owners[sub.Name] = (flange.Name, PartOwnerKind.EdgeFlange);
                if (edgeFlanges == null || !edgeFlanges.TryGetValue(flange.Name, out EdgeFlangeFacts facts) || profiles.Count >= facts.EdgeCount) continue;
                foreach (string parent in flange.Parents.Where(facts.ParentsOnHinge.Contains))
                    if (IsExternalProfile(parent, flange, top, all))
                        tree._owners[parent] = (flange.Name, PartOwnerKind.EdgeFlange);
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
                // The first base flange of its body (by tree order: GetFeatures says nothing about order), or its legacy SheetMetal.
                string? owner = facts.BodyFeatures.Where(n => tree._types.TryGetValue(n, out string t) && (t == "SMBaseFlange" || tree._legacy.ContainsKey(n)))
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
        /// Legacy Insert Bends (S6): each SheetMetal without a base flange, in tree order, with the FlattenBends
        /// and ProcessBends that follow it – by their parents (FlattenBends on the SheetMetal, ProcessBends on
        /// either), else the next unclaimed ones in tree order before the next SheetMetal. With at least one of
        /// them it is an anchor; the top-level features between it and its last bends feature (sketches and
        /// folders aside) are what the part got while flattened (<see cref="BetweenLegacyBends"/>).
        /// </summary>
        private void FindLegacyBends(List<TreeFeature> top, List<TreeFeature> all)
        {
            List<TreeFeature> Distinct(IEnumerable<TreeFeature> fs) =>
                fs.GroupBy(f => f.Name, StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => _order[f.Name]).ToList();
            List<TreeFeature> sheetMetals = Distinct(all.Where(f => f.Type == "SheetMetal" && !_owners.ContainsKey(f.Name)));
            List<TreeFeature> allSheetMetals = Distinct(all.Where(f => f.Type == "SheetMetal"));
            List<TreeFeature> bends = Distinct(all.Where(f => f.Type == "FlattenBends" || f.Type == "ProcessBends"));
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (TreeFeature sm in sheetMetals)
            {
                int start = _order[sm.Name];
                int end = allSheetMetals.Where(o => _order[o.Name] > start).Select(o => _order[o.Name]).DefaultIfEmpty(int.MaxValue).Min();
                TreeFeature? Next(string type, int after) =>
                    bends.FirstOrDefault(b => b.Type == type && !claimed.Contains(b.Name) && _order[b.Name] > after && _order[b.Name] < end);
                TreeFeature? flatten = bends.FirstOrDefault(b => b.Type == "FlattenBends" && !claimed.Contains(b.Name) && b.Parents.Contains(sm.Name))
                                       ?? Next("FlattenBends", start);
                TreeFeature? process = bends.FirstOrDefault(b => b.Type == "ProcessBends" && !claimed.Contains(b.Name) &&
                                                                 (b.Parents.Contains(sm.Name) || (flatten != null && b.Parents.Contains(flatten.Name))))
                                       ?? Next("ProcessBends", flatten != null ? _order[flatten.Name] : start);
                if (flatten == null && process == null) continue;
                _legacy[sm.Name] = (flatten?.Name, process?.Name);
                foreach (TreeFeature? part in new[] { flatten, process })
                {
                    if (part == null) continue;
                    claimed.Add(part.Name);
                    _owners[part.Name] = (sm.Name, PartOwnerKind.LegacyBends);
                }
                // What lies between the anchor and its last bends feature at the top level.
                int from = top.FindIndex(f => f.Name == sm.Name);
                string last = (process ?? flatten)!.Name;
                int to = top.FindIndex(f => f.Name == last);
                if (from < 0 || to <= from) continue;
                List<string> between = top.Skip(from + 1).Take(to - from - 1)
                    .Where(f => f.Name != flatten?.Name && !IsSketchType(f.Type) && !IsSystemType(f.Type))
                    .Select(f => f.Name).ToList();
                if (between.Count > 0) _legacyBetween[sm.Name] = between;
            }
        }

        /// <summary>
        /// Tab and slot (S8, A4 P12, B64): in tree order, each feature that answers as tab and slot and is no slot
        /// itself is a tab; its slot is the first unclaimed feature after it (no sketch) that is its child and
        /// answers the same data, has its type or „Slot“ in its type, or whose name is the tab's with another word.
        /// </summary>
        private void FindSlots(List<TreeFeature> all, IReadOnlyCollection<string> tabAndSlots)
        {
            var answers = new HashSet<string>(tabAndSlots, StringComparer.Ordinal);
            List<TreeFeature> ordered = all.GroupBy(f => f.Name, StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => _order[f.Name]).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                TreeFeature tab = ordered[i];
                if (!answers.Contains(tab.Name) || _owners.ContainsKey(tab.Name)) continue;
                TreeFeature? slot = ordered.Skip(i + 1).FirstOrDefault(c =>
                    !IsSketchType(c.Type) && !_owners.ContainsKey(c.Name) && !_slots.ContainsKey(c.Name) &&
                    ((c.Parents.Contains(tab.Name) &&
                      (answers.Contains(c.Name) || c.Type == tab.Type || c.Type.IndexOf("Slot", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                     TabSlotRules.IsSlotName(tab.Name, c.Name)));
                if (slot == null) continue;
                _owners[slot.Name] = (tab.Name, PartOwnerKind.TabAndSlot);
                _slots[tab.Name] = slot.Name;
            }
        }

        /// <summary>
        /// A parent of an edge flange that is its profile at the top level (A2 P13): a sketch at the top
        /// level, absorbed by no feature, and a parent of no feature but this flange and its own
        /// sub-features (a shared sketch or an up-to reference of something else stays a sketch of its own).
        /// </summary>
        private static bool IsExternalProfile(string name, TreeFeature flange, List<TreeFeature> top, List<TreeFeature> all)
        {
            TreeFeature? sketch = top.FirstOrDefault(f => f.Name == name);
            if (sketch == null || !IsSketchType(sketch.Type)) return false;
            if (all.Any(f => f.SubFeatures.Any(s => s.Name == name))) return false;
            var own = new HashSet<string>(flange.SubFeatures.Select(s => s.Name), StringComparer.Ordinal) { flange.Name };
            return !all.Any(f => !own.Contains(f.Name) && f.Name != name && f.Parents.Contains(name));
        }

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
        /// The base flange that made the body of a feature, by name: a SheetMetal → its owner (the SheetMetal of
        /// legacy Insert Bends: itself); a base flange → the owner of its SheetMetal (a tab shares the one of
        /// its body), itself without one. Anything else: null.
        /// </summary>
        private string? BaseFlangeOf(string name, List<TreeFeature> all)
        {
            string type = _types[name];
            if (type == "SheetMetal")
                return _owners.TryGetValue(name, out var sm) && sm.kind == PartOwnerKind.SheetMetal ? sm.owner
                    : _legacy.ContainsKey(name) ? name : null;
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

        /// <summary>Part name → its owner and the owner's kind (SheetMetal → base flange; flat-pattern sketches and own corners → flat pattern; width sketches → hem; profiles → edge flange; break lines → cross break; FlattenBends and ProcessBends → their legacy SheetMetal; the slot feature → its tab and slot).</summary>
        public IReadOnlyDictionary<string, (string owner, PartOwnerKind kind)> Owners => _owners;

        /// <summary>Flat pattern → the base flange of its body (or its legacy SheetMetal).</summary>
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

        /// <summary>
        /// The SheetMetal feature has no base flange of its own and is no anchor of legacy Insert Bends: its body
        /// comes from something else (a convert, a lofted bend, a swept flange – a carrier).
        /// </summary>
        public bool IsUnownedSheetMetal(string name) =>
            _types.TryGetValue(name, out string type) && type == "SheetMetal" && !_owners.ContainsKey(name) && !_legacy.ContainsKey(name);

        /// <summary>The slot feature of a tab and slot (S8); null if it has none or the feature is no tab.</summary>
        public string? SlotOf(string tab) => _slots.TryGetValue(tab, out string slot) ? slot : null;

        /// <summary>The SheetMetal features of legacy Insert Bends (S6): carriers with a record of their own.</summary>
        public IReadOnlyCollection<string> LegacyAnchors => _legacy.Keys;

        /// <summary>The SheetMetal is the anchor of legacy Insert Bends.</summary>
        public bool IsLegacyAnchor(string name) => _legacy.ContainsKey(name);

        /// <summary>The FlattenBends and ProcessBends of a legacy anchor; (null, null) for any other feature.</summary>
        public (string? flatten, string? process) LegacyParts(string anchor) => _legacy.TryGetValue(anchor, out var parts) ? parts : (null, null);

        /// <summary>
        /// Top-level features between a legacy anchor and its last bends feature: added while the part was
        /// flattened, they act on the flat state and are bent with ProcessBends. The copy creates SheetMetal,
        /// FlattenBends and ProcessBends in one call and cannot put anything between them – such Insert Bends
        /// are not rebuilt (<see cref="SheetMetalRules.LegacyBetween"/>). Empty for anything else.
        /// </summary>
        public IReadOnlyList<string> BetweenLegacyBends(string anchor) =>
            _legacyBetween.TryGetValue(anchor, out List<string> between) ? between : (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>
        /// What a selection brings along (A1 P6): a base flange its SheetMetal and the flat pattern of
        /// its body, a flat pattern its sketches and own corner features; legacy Insert Bends come as one –
        /// the SheetMetal with its FlattenBends, ProcessBends and flat pattern, either bends feature with its
        /// SheetMetal; a tab and slot comes as one, tab with slot and slot with tab. Returns the names to add.
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
                if (type == "SMBaseFlange" || _legacy.ContainsKey(n))
                {
                    foreach (var kv in _owners.Where(kv => kv.Value.owner == n)) Add(kv.Key);
                    foreach (var kv in _flatPatternOwners.Where(kv => kv.Value == n)) Add(kv.Key);
                }
                else if (_owners.TryGetValue(n, out var legacyPart) && legacyPart.kind == PartOwnerKind.LegacyBends)
                {
                    Add(legacyPart.owner);
                }
                else if (_slots.TryGetValue(n, out string slot))
                {
                    Add(slot);
                }
                else if (_owners.TryGetValue(n, out var slotPart) && slotPart.kind == PartOwnerKind.TabAndSlot)
                {
                    Add(slotPart.owner);
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
