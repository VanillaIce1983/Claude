# Review of the mirror-features design for Spiegelwerk

I checked the design against the working tree as it stands now. The tree is still changing: during this review it gained `KnownDimensions`, `CaptureDimensions`, `RestoreDimensionNames` and a reworked `CopyEquations`. The line numbers below are from the current files.

**Interop probe.** I wrote a new probe at `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/mirrorreview/Program.cs`. It uses MetadataLoadContext and runs as `dotnet out/mirrorreview.dll <asm> members|enum|types <name>`. I ran it against the 27.3.0 and 32.1.0 sldworks and swconst assemblies.

**Maths check.** `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/mirrorreview/conj.py` tests R_Π(R_P(x)) = R_P'(R_Π(x)) on 10 000 random cases. The largest error was 5.3e-15.

**API members: all exist in 27.3.0 with the stated signatures.**
- `IMirrorPatternFeatureData` and `IMirrorSolidFeatureData` match the design's member lists.
  - 32.1.0 differs only by `IMirrorSolidFeatureData.StructureSystemToPatternArray`.
- `IMirrorPartFeatureData` has `string PathName {get;}`, `ModelDoc2 GetModelDoc()`, `MathTransform GetTransform()` and `object GetMirrorPlane()`.
- `IFeatureManager`:
  - `Feature InsertCombineFeature(int OperationType, Body2 MainBody, object ToolVar)` exists in both versions.
  - `InsertMirrorFeature(bool, bool, bool, bool)` and `InsertMirrorFeature2(bool, bool, bool, bool, int)` exist; they stay forbidden.
  - `bool EditRollback(int, string)` and `bool EnableFeatureTree {get;set;}` exist.
- `ICombineBodiesFeatureData` has `MainBody`, `BodiesToCombine`, `OperationType` and `AccessSelections(ModelDoc2, Component2)`.
- `IBody2.GetType()` returns int, so the explicit `(IBody2)` cast is needed. `IBody2.GetFeatures()` returns object. `IFace2.GetFeature()` returns object, so it needs a cast.
- Enums:
  - `swMirrorPlaneType_e`: Face = 0, Plane = 1.
  - `swFeatureScope_e`: 0 / 1 / 2.
  - `swBodyOperationType_e.SWBODYADD` = 15903.
  - `swCombineBodiesOperationType_e` (Add = 0) also exists in 27.3.0.
  - `swFeatureNameID_e`: swFmMirrorSolid = 4, swFmMirrorPattern = 7. There is no Combine id in 27.3.0 or 32.1.0.
  - `swUpdateProgressError_UserCancel` = 2 exists.
- help.solidworks.com does not resolve from here (ENOTFOUND). Search summaries confirm two things: `InsertCombineFeature` takes a `swBodyOperationType_e`, and MainBody must be null for Add.

---

## (1) Problems

### P1. Face seeds are rebuilt as their whole owner feature; the geometry can be silently wrong
- **Evidence:**
  - The design's `CaptureMirrorPattern` adds `face.GetFeature()` to `SeedFeatures`, and §3.2 relies on `AddSeeds`, which adds face owners (`FeatureCollector.cs` l.248–250).
  - SOLIDWORKS "faces to mirror" copies only those faces. Patterns keep face seeds as faces (`FeatureCapture.Patterns.cs` l.445–446, `PatternSupport.FindSeeds` l.47–52). They never replace a face with its owner.
- **Failure:**
  - Case 1: the face of one hole whose Cut-Extrude made three holes. The rebuild makes three extra holes.
  - Case 2: a face owned by the base extrude. The rebuild mirrors the whole base.
  - In feature mode the collector even pulls the base feature into the outer run.
  - Only the M-level mass check notices, and that check is off when VerifySteps is off.
- **Fix:**
  - Inside the access window at M, group the faces by owner. Accept them only if each owner's `GetFaces()` fingerprints equal the set of selected faces of that owner.
  - If they match, add the owner. Otherwise throw `NotSupportedException("Spiegeln einzelner Flächen wird nicht nachgebaut …")`.
  - `SeedsOf` applies the same rule before adding owners.

### P2. Taking second-half step references from ΔS′ measured in the target is wrong and risky
- **Evidence, first part:** EditRollback is not used under `UpdateFreeze` today.
  - It appears only in Capture/: `MassCapture` l.28/30/37, `FeatureCapture.cs` l.110/117, `ShellDraft` l.52/66, `AdvancedHole` l.53/69.
  - Build/ only uses AccessSelections (`FeatureEdit`, pattern readers). So "already roll back under it today" holds for AccessSelections only.
- **Evidence, second part:** ΔS′ᵢ describes S′ as built. If S′ deviates (its entry is "Hinweis"), the second-half step passes against the wrong S′ and is reported "OK".
- **Fix:** take references from the source. Drop the nested `MassCapture` entirely.
  - Record per target feature: Expected = `record.ExpectedChange?.Mirror(ctx.Mirror)` and Samples = `record.FaceSamples.Select(ctx.Mirror.Apply)`.
  - Child records take these values. The independence test and the step checks then use the same numbers that the S′ step was checked against.
  - This also works recursively for flattened nested mirrors.

### P3. Helpers are left behind when the group is removed
- **Evidence:**
  - The design's group `RemoveAgain` deletes `AllFeatures` and calls `Forget`. `Forget` only drops helpers from lists (`PlaneResolver.Forget` l.307–313); it deletes nothing.
  - Helper planes, axes, anchors and table-pattern coordinate systems made for child steps stay. This happens both on the guard path and on the "no solid child succeeded" path.
  - The child `BuildOne` also calls `Planes.BeginStep()` (`MirrorJob.cs` l.197 → `PlaneResolver` l.316), which clears the shared `_stepHelpers`.
- **Fix:**
  - Every removal of a mirror feature's group uses the `RemoveLeftovers` snapshot (`featuresBefore` taken when M starts, two rounds, `ctx.Forget` before deleting). That is the same code the exception path already uses (l.208).
  - `BeginStep` becomes mark-based (`int BeginStep()`, `RemoveStepHelpers(int mark)`) instead of `Clear()`.

### P4. MirrorSolid can silently produce extra or duplicate bodies
- **Evidence:** the seed list is `IBody2.GetFeatures()`.
  - A multi-contour extrude, or a cut that split a block, belongs to the seed body but also made other bodies. Rebuilt in a separate child body, it makes extra bodies.
  - The design runs no check when VerifySteps is off.
  - Nested case: if `GetFeatures(B1)` of a body made by M1 (Merge off) lists both M1 and B's features, M2's child builds B twice.
- **Fix:**
  - Add an always-on body check that needs no rollback. The `SeedBodies` masses are captured anyway.
  - The child's own bodies must be exactly one per seed, each matching `s.Mirror(R_P).Mirror(R_Π)` via `MassComparison`. Otherwise the step is a warning and no Combine runs.
  - In a post-pass over records: if a mirror's seeds contain a MirrorSolid Mk with Merge off, drop Mk's own seeds.

### P5. MirrorSolid seed lists cause false "missing" entries
- **Evidence:**
  - `GetFeatures` also lists sketches and datums; `ScopeOf` filters them out (`FeatureCollector.cs` l.139).
  - A rebuilt RefPlane is both registered and a helper: `RefPlaneBuilder` → `CreateNamed` → `Create` calls `RegisterHelper` (`PlaneResolver` l.110), then `ctx.Register`.
  - So `isCounterpart = … && !HelperNames.Contains` reports the plane as a missing seed. That gives a skipped child entry, makes M a warning and blocks deleting the original.
- **Fix:**
  - Filter MirrorSolid seeds like `ScopeOf` does (no sketches, no datums, no system features).
  - `isCounterpart` = membership in `Created` or a group only. Helpers never enter `Created`, so `HelperNames` is unnecessary.

### P6. The Merge/FeatureScope patch is partly pointless and does not reach nested members
- **Evidence:**
  - Builders never read `r.FeatureScope` or `r.AutoSelect`. Only `PlanSeparateBody` reads `r.Merge` (`ExtrudeBuilder` l.45, `RevolveBuilder` l.37, `SweepBuilder` l.40, `LoftBuilder` l.37).
  - `RevolveRecord`, `SweepRecord` and `LoftRecord` have no FeatureScope or AutoSelect fields (`FeatureRecords.cs` l.214, `SweepLoftRecords.cs` l.78/121).
  - A lookup by logical name finds no original record for flattened group members.
- **Fix:** patch Merge only, and only for those four record types. Take it from a per-target-name entry stored at build time (`Built[name].Merge`).

### P7. Second-half features pollute KnownDimensions (new working-tree code)
- **Evidence:**
  - `SketchBuilder.cs` l.613 adds `{dim.Name}@{map.Source.Name}`; `MirrorJob.cs` l.313 adds `{name}@{record.Name}`.
  - In the child these names are S′ names, which in new-part mode are the original names.
  - So a second-half success marks a first-half dimension as known. `EquationProblem` (l.543) can then let an equation through whose first-half rename failed.
- **Fix:**
  - The child gets its own `KnownDimensions`, which is thrown away.
  - Copy capture skips `CaptureDimensions` and `MarkOtherBodies`.
  - `CaptureDimensions` excludes `MirrorFeatureRecord`.

### P8. The MirrorStock advice is wrong
- R_Π∘R_P preserves orientation. The mirror of a "mirror part" is the referenced part X moved by T; when P = Π it is X itself.
- The design's advice, "run Spiegelwerk (new part) on the referenced part", yields the opposite hand.
- **Fix:** change the text (§3.1). Optionally abort `Run` early: every feature after a MirrorStock base fails anyway.

### P9. `Reflection.TryFromAffine` accepts glide reflections
- A glide reflection also has det = −1 and a symmetric orthogonal linear part (probe: `conj.py`).
- **Fix:** also require that the translation is parallel to n: |t − (t·n)n| ≤ Tol.Length.

### P10. The progress sink and cancel do not exist
- **Evidence:** grep for "Progress" in `src/Spiegelwerk` finds nothing. `MirrorPage.cs` l.247 calls `new MirrorJob(_sw).Run(request)` synchronously.
- **Fix:** remove progress and cancel from this design, or make them a separate feature.

### P11. The rollback bar can be left mid-tree in the same part
- **Evidence:** the result of `EditRollback(End)` is ignored (`MassCapture` l.37, `FeatureCapture.cs` l.117). If the bar stays before S′ᵢ, later features are inserted in the middle of the tree.
- **Fix:**
  - After the nested capture, check `SwDoc.TopLevelFeatures(doc).Any(f => f.IsRolledBack())`, as `Run` does at l.105.
  - Retry `EditRollback(End)`. If it still fails, throw: the exception path removes what was built and the run stops.

### P12. The child's counterpart lookup must not fall back to the parent
- If a pattern's seed is not among M's seeds, a fallback would resolve to the first-half S1′. The pattern would then repeat that first-half seed with instances meant for the second half, which is wrong geometry with no error.
- **Fix:** `Counterparts` is per context. The child's lookup sees only the child's own `Created` and groups.

### P13. The child context needs data the design does not mention
- `PartOwners` and `PointSketches` must be filled as `Run` does (l.135–136).
- `KnownDimensions` must be isolated (see P7).
- `LogicalName` does not exist yet; it was missing from the refactor list.

### P14. Unrequested seed body in body mode
- If the user selects only B_M (the body M created), the rebuild also creates R_Π(B).
- With MirrorSolid and Merge off, that is a body the user did not ask for. This is not quite "consistent with body patterns", where the seed is part of the pattern's result.
- **Fix:** name it in M's details. `BodyScope` already blocks deleting the original.

### P15. Statements that are inaccurate but harmless
- **C1, "IsSketch pulls in anchors":** anchors are parents of helper planes, axes and coordinate systems, not of S′ features. The other C1 reasons stand.
- **C2a, "every S′ has feature scope":** only cuts, holes and merging bosses after the first do, plus patterns on 2024 interop.
- **Combine operation type:** "not `swCombineBodiesOperationType_e`" is right, but that enum does exist in 27.3.0.
- **StopOnError vs. Aggregate:** with StopOnError, a failed child must make M "Fehler", overriding the Aggregate rule.

### P16. The answer to the user is too narrow
- The user asked about the whole "Fehlende Feature-Typen" list, not only mirror features.
- Every item on that list is currently missing:
  - There are no builders for rib, sheet metal, weldments, surfaces, imported bodies, Combine, Split or Move/Copy (`Builders` array, `MirrorJob.cs` l.62–87).
  - Regions are not carried over (`ReadContours` note, `FeatureCapture.cs` l.188). Contours are.
  - Only the active configuration is copied, and deleting the original is refused when the part has more than one configuration (`DeleteOriginals`).

---

## (2) Corrected design

### 0. Answer to the user (German draft)

„Ja. Die Liste zählt auf, was Spiegelwerk heute nicht nachbaut; ‚deine Entscheidung' heißt: du legst fest, was davon als Nächstes kommt.

- Spiegel-Features (Features/Flächen spiegeln, Körper spiegeln, Teil spiegeln) stehen im Bericht als ‚Fehler', die Kopie enthält dann nur die Saat-Hälfte.
- Rippe, Blech, Schweißkonstruktionen, Flächen-Features, importierte Körper, Kombinieren/Trennen/Verschieben-Kopieren haben keinen Nachbau.
- Bei Extrusion/Rotation werden gewählte Konturen übernommen, gewählte Bereiche (Regionen) nicht – dann wird die ganze Skizze verwendet.
- Übernommen wird nur die aktive Konfiguration; bei mehreren Konfigurationen löscht Spiegelwerk das Original nicht.

Spiegel-Features lassen sich ohne Spiegel-API nachbauen: Spiegelwerk liest die schon gespiegelte erste Hälfte im Ziel wieder ein und baut sie an der gespiegelten Spiegelebene noch einmal als gewöhnliche, einzeln bearbeitbare Features auf."

The earlier phrase "Drehung bzw. Verschiebung der Saat" is correct for the original seed: T = R_Π∘R_P is a proper rigid motion. It describes approach B, which is not the one recommended.

### 1. Verdict

- **Maths:** g∘R_P∘g⁻¹ = R_g(P). The second half is R_P′(S′) with P′ = R_Π(P).
- **Approach:** A, nested reflection. Re-capture S′ from the target and rebuild it with a child `BuildContext` whose `Mirror` is R_P′.
  - `PlaneResolver` and `TopologyFinder` never read `Mirror`.
  - All builders, `SketchBuilder` and `StepVerifier` read `ctx.Mirror`, so σ, arcs, patterns, sweep twist, fill angles, `ApplyKeepingXY`, table z and `PatternInstances.Mirror` carry over unchanged.
- **Corrections needed:**
  - C1: dedicated `CollectCopies`.
  - C2: copy capture without scope notes, Merge patched.
  - C3: topology generation counter.
  - C4: step references from the source, with an independence test.
  - C5: groups.
  - C6: separate body set for MirrorSolid, a body check, and Combine.
  - C7: isolated `KnownDimensions`.
  - C8: face-seed rule.
  - C9: rollback-state check.
  - C10: snapshot removal.
- **Approach B is rejected.** `Reflection` is built into Core (`SketchMirror`, `AxisMap`, `DirectionRules`, `MassState`, signatures, `PatternInstances`, `TablePoints`, `FillPatternRules`), and every det = −1 rule in the builders would need a branch.

### 2. Capture

**Type names**

| GetTypeName2 | Meaning | Handling |
| --- | --- | --- |
| `"MirrorPattern"` | features/faces | `MirrorPatternFeatureData` |
| `"MirrorSolid"` | bodies | `MirrorSolidFeatureData` |
| `"MirrorStock"` | mirror part | unsupported |
| `"CombineBodies"` | Combine | unsupported unless the optional `CombineRecord` is added |

- These names come from third-party mapping; confirm them live.
- The `InsertMirrorFeature*` methods are never called.

**New record** in `Spiegelwerk.Core/Records/MirrorFeatureRecords.cs`:

```csharp
public enum MirrorSeedKind { Features, Bodies }
public sealed class MirrorFeatureRecord : FeatureRecord   // ChangesSolid = true
{
    public MirrorFeatureRecord(string name, string typeName, MirrorSeedKind kind, Plane3 plane);
    public MirrorSeedKind Kind { get; }
    public Plane3 Plane { get; }                 // P, source coordinates
    public bool PlaneIsFace { get; set; }
    public List<string> SeedFeatures { get; }    // body-shaping seeds only; no sketches, datums, system or suppressed features
    public List<FaceSignature> SeedFaces { get; } // info; only complete owner face sets are accepted
    public List<MassState> SeedBodies { get; }   // MirrorSolid, state at M
    public bool Merge { get; set; }              // MirrorSolid
    public bool GeometryPattern { get; set; }
    public bool PropagateVisualProperty { get; set; }
}
```

**Plane: `Capture/FeatureCapture.Mirror.cs`, `Plane3 MirrorPlane(object o)`**
- `Face2 f`: `TopologyCapture.PlaneOfFace(f) ?? throw new NotSupportedException("Spiegelfläche ist nicht eben.")`.
- `RefPlane`, or `Feature` with a RefPlane: `_math.PlaneOf(...)`, the same branches as `ToGeometryRef`.
- MirrorSolid fallback: `Reflection.TryFromAffine(_math.ToAffine(d.GetTransform()))`. It requires:
  - det = −1;
  - M symmetric and orthogonal within 1e-9;
  - n = normalised column of (I − M) with the largest norm;
  - t parallel to n (rejects glide reflections);
  - p₀ = t/2.

**Capture switch:** `case MirrorPatternFeatureData mp:` and `case MirrorSolidFeatureData ms:`, each inside `AccessSelections(_doc, null)` / `ReleaseSelectionAccess()` in try/finally.

**`CaptureMirrorPattern`**
- Plane: `MirrorPlane(mp.Plane)`; `PlaneIsFace = mp.GetMirrorPlaneType() == 0`.
- Seeds: `PatternFeatureArray` without `IsSuppressed()` features.
- Faces: group `MirrorFaceArray` by `(Feature)face.GetFeature()`.
  - Each owner's `GetFaces()`, read inside the window, must equal the selected faces by fingerprint. Then add the owner and record the fingerprints.
  - Otherwise: `NotSupported("Spiegeln einzelner Flächen wird nicht nachgebaut (nur ganze Features); …")`.
- `FeatureScope == 2` → note "Feature-Bereich des Spiegel-Features wird nicht übernommen."
- `GeometryPattern`: no note.

**`CaptureMirrorSolid`**
- Plane: from `Face`, falling back to `GetTransform`.
- For each `Body2 b` in `PatternBodyArray`:
  - `((IBody2)b).GetType() != (int)swBodyType_e.swSolidBody` → `NotSupported`.
  - Mass as in `ReadSeeds`.
  - Seeds from `b.GetFeatures()`, filtered to body-shaping features like `ScopeOf`.
- `Merge = ms.Merge`. `KnitSurface` true → `NotSupported`.
- After all records are captured, a post-pass applies the nested rule from P4.

**`UnsupportedReason`**
- "MirrorPattern" and "MirrorSolid": "Spiegel-Feature: Feature-Daten nicht lesbar."
- "MirrorStock": „Teil spiegeln: Die Geometrie stammt aus „<PathName>“. Das Spiegelbild eines gespiegelten Teils ist dieses Ausgangsteil selbst (nur gedreht/verschoben, bei gleicher Ebene an derselben Stelle). Bitte das Ausgangsteil verwenden bzw. als unabhängige Kopie speichern."

**Copy mode:** `FeatureCapture(ModelDoc2 doc, SwMath math, bool ofOwnCopy = false)`.
- It suppresses the scope notes: `FeatureCapture.cs` l.158, l.233; `Holes` l.149; `SweepLoft` l.61, l.131; `Patterns` l.273 and `NoteScope`.
- Other notes stay; they report the same real limits as on S′.
- `CaptureDimensions` skips `MirrorFeatureRecord` and is never run on copies.

### 3. FeatureCollector (outer run)

`SeedsOf` gets two new cases (the `AddSeeds` signature is unchanged):

```csharp
case MirrorPatternFeatureData mp when mp.AccessSelections(doc, null):
    try { AddSeeds(result, seedBodyNames, false, mp.PatternFeatureArray, null, null);
          result.AddRange(CompleteFaceOwners(mp.MirrorFaceArray)); }        // P1 rule; incomplete → nothing
    finally { mp.ReleaseSelectionAccess(); } break;
case MirrorSolidFeatureData ms when ms.AccessSelections(doc, null):
    try { seedBodies |= AddSeeds(result, seedBodyNames, true, null, null, ms.PatternBodyArray); }
    finally { ms.ReleaseSelectionAccess(); } break;
```

- Suppressed seeds are not added.
- Body seeds set `seedBodies` (same-part feature mode then builds a separate body), and seed bodies count as selected in `ScopeOf`.
- Chains are resolved by the existing queue.
- **Selecting only a body made by M** brings in its seed body, which becomes an unrequested copy. M's entry names it, and `BodyScope.UnselectedBodies` blocks deleting the original.

### 4. Build

**a. Shared state: `BuildShared`**
- The single `PlaneResolver`.
- `HashSet<string> JobNames`.
- `int TopologyGeneration`.
- `Dictionary<string, BuiltInfo> Built`, keyed by target feature name:
  - `string Logical`;
  - `MassState? Expected` and `List<Vec3> Samples`, in target coordinates;
  - `bool? Merge`;
  - `ReportStatus Status`.
- No progress sink.
- `StepRunner` fills `Built` after each step that produced a feature:
  - `Expected = record.ExpectedChange?.Mirror(ctx.Mirror)`;
  - `Samples = record.FaceSamples.Select(ctx.Mirror.Apply)`;
  - `Merge` from Extrude, Revolve, Sweep or Loft records.
- `ctx.Forget` also removes `Built` keys.

`BuildContext.Nested(parent, Reflection mirror, bool separateBody, HashSet<string> ownNames, string mirrorName, StepCheck check)` builds the child:

| Member | Child |
| --- | --- |
| Sw, Doc, Math, Options | same instances |
| Mirror | `new Reflection(parent.Mirror.Apply(m.Plane))` = R_P′ |
| Planes | shared. `_ctx` stays the outer context: its `RegisterHelper` adds to `JobNames`, and its `Topology.Invalidate` bumps the shared generation. |
| Topology | own `TopologyFinder`; `Load()` reloads when its stamp ≠ `TopologyGeneration`; `Invalidate()` = generation++ (C3) |
| Sketches, Created, groups, PartOwners, PointSketches | own; filled from the child records as `Run` l.135–136 does |
| KnownDimensions | own, thrown away (C7) |
| own names | MirrorPattern: `JobNames`. MirrorSolid: a fresh set. `Register` adds to the own set and to `JobNames`. |
| NameFor(n) | `parent.NameFor(MirrorFeatureRules.SecondHalfName(Built[n].Logical, mirrorName))` → "Boss-Extrude1 (Spiegeln1)" plus the same-part suffix |
| Report | own; re-homed afterwards |
| Counterparts | own `Created` and groups only, no parent fallback (P12) |

- The `Built` entry's `Logical` is the original name for outer records and `SecondHalfName(...)` for child records. Nesting therefore gives "X (M1) (M2)".
- `PlaneResolver.BeginStep()` returns a mark and `RemoveStepHelpers(int mark)` deletes from that mark on, so a child never clears the outer list.

**b. `FeatureCollector.CollectCopies(ModelDoc2 target, IEnumerable<Feature> seedCopies, Func<Feature,bool> isCounterpart, List<(Feature, string)> skipped)`**
- `wanted` = seed copies, their absorbed sketches (`ProfileFeature`, for holes), and parent sketches where `isCounterpart` is true.
- Order: `SwDoc.FeaturesInCreationOrder(target)`.
- No pattern-seed closure and no non-sketch parents.
- `SkipReason` and `SkippedSketchOf` are reused.
- `isCounterpart(f)` = `f.Name` is a value of `Created` or a group member.

**c. `MirrorFeatureBuilder.Build`**
1. Take snapshot S0 of the top-level feature names.
2. Find seed copies with `ctx.Counterparts(seed, solidOnly: false)`. Missing seeds become skipped child entries. If no seed copy changes the solid, return Failed before creating anything.
3. Require that no sketch is active, clear the selection, run `CollectCopies`, then the shared `CaptureAll(new FeatureCapture(doc, math, ofOwnCopy: true), copies)` helper.
   - `CaptureAll` includes the hole `PartOf`/`CaptureOnItsOwn` fix-up.
   - It runs no `MarkOtherBodies`, no `CaptureDimensions` and no `MassCapture`.
   - Assert that no `MirrorFeatureRecord` appears.
4. Rollback check (C9): if any top-level feature `IsRolledBack()`, call `EditRollback(swMoveRollbackBarToEnd, "")`; if that fails, throw.
   - If `EditRollback` returns false while the tree is disabled, set `EnableFeatureTree = true` for the capture only.
5. Generation++.
6. Patch each child record from `Built[rec.Name]`:
   - `Merge` (Extrude, Revolve, Sweep, Loft only);
   - `ExpectedChange = Expected`;
   - `FaceSamples` = `Samples` minus `MirrorFeatureRules.OffPlane(samples, P′)`.
7. Choose `StepCheck`:
   - `PassOnly` when VerifySteps is on and `SeedsIndependent(m.ExpectedChange?.Mirror(ctx.Mirror), seedExpecteds, R_P′, MassOf(doc))`. That holds when every value is non-null and `Compare(mT, Σ sᵢ.Mirror(R_P′), total)` passes.
   - `ReportOnly` when VerifySteps is on but the test fails or a value is missing.
   - `Off` when VerifySteps is off.
8. Run the child `StepRunner` over the records. With StopOnError, stop at the first Failed child.
9. MirrorSolid body check, always on (P4): child `OwnBodies()` count = seed count, and each child body = `FindBody(child, s.Mirror(R_P).Mirror(R_Π))`. If this fails: Warning and no Combine.
10. MirrorSolid with Merge, for each seed:
    - `s1 = FindBody(ctx, s.Mirror(R_Π))` and `nb = FindBody(child, …)`;
    - `doc.FeatureManager.InsertCombineFeature((int)swBodyOperationType_e.SWBODYADD, null, new object[] { s1, nb })`;
    - fallback: select with marks 1 and 2, then pass nulls;
    - if the result is null or `GetErrorCode2` reports an error, delete the Combine and report Warning with "nicht verschmolzen";
    - name it `ctx.NameFor(m.Name)`; it becomes a group member.
11. If no solid child succeeded: `StepRunner.RemoveLeftovers(ctx, S0, result)` and return Failed.
    - Otherwise: `ctx.RegisterGroup(m.Name, members)` and return `BuildResult.OfGroup(members, status, message)` with `ChildEntries` attached.

**d. Body semantics**

| Case | Child setup |
| --- | --- |
| MirrorPattern, new part or same-part feature mode | `separateBody = false`; bosses merge with what they touch and cuts cut what they reach, as SOLIDWORKS re-solves mirrored features |
| MirrorPattern, same-part whole/body mode | `separateBody = true`, own names = `JobNames`; the child guard protects the original |
| MirrorSolid | always `separateBody = true` with a fresh set; the guard protects S′ and the original; then the body check and, with Merge, the Combine |

**e. Verification**
- **Outer step for M:**
  - Compare the change of the whole nested run with `M.ExpectedChange.Mirror(R_Π)`; samples are `R_Π(M.FaceSamples)`.
  - Group-aware: an error in any member counts; own faces = union of members' faces; no alternatives.
  - `CheckRebuildError` is group-aware too.
- **Child steps:**
  - `PassOnly`: variant search, but a variant is kept only if it passes.
  - `ReportOnly`: verification text only; no status change, no variants, and no "zurückrollen" note.
- **Child entry status** = worst of (its own status, `Built[S′].Status`).

**f. Groups.** `RegisterGroup` and `Counterparts(string original, bool solidOnly)`. `PatternSupport.FindSeeds` (l.44) and `VariablePatternBuilder` (l.42) use `Counterparts`; a pattern of M gets the solid group members with mark 4.

**g. Removal.** Every removal of M (outer guard, no solid child, exception) uses `RemoveLeftovers` with M's snapshot (C10).

**h. Refactors**
- Move `BuildOne`, `RemoveLeftovers`, `NewFeatures`, `RemoveAgain`, `CheckRebuildError`, `WithdrawParts`, `OriginalUntouched`, `RestoreDimensionNames` and the `Builders` array into `Build/StepRunner.cs`. `RemoveLeftovers` becomes internal static.
- `BuildResult` gains `Group`, `AllFeatures`, `ChildEntries` and `OfGroup`.
- `FindBody` becomes internal.
- `LogicalName` is replaced by `Built[...].Logical`.

### 5. Report
- M's entry comes first, followed by `MirrorReport.Nested(owner, entries, logical)`.
  - Child entries are re-created as "Spiegeln1 › Boss-Extrude1" with the detail „im Ziel: …“.
- M's status (`Aggregate`):
  - Failed if no solid child succeeded, or if StopOnError is on and a child failed (this overrides the other rules).
  - Warning if any child is not mirrored or not OK, the M check fails, the body check fails, or a Combine failed.
  - OK otherwise.
- M's details: missing seeds, feature-scope note, unrequested seed body (P14), GeometryPattern hint when the check fails.
- `report.General` gets "Gleichungen steuern nur die erste Hälfte von Spiegel-Features" only when equations were copied (new part) or `NoteEquationDrivenDimensions` (same part) found driven dimensions.

### 6. Edge cases
- **P on a face:** only geometry is used; a non-planar face is unsupported.
- **P = Π:** the second half coincides with the original seed. Own-body preference and the guard keep it apart from the original body. In feature mode the steps show no mass change, so they are warnings, but the geometry is right.
- **Nested mirrors:** flattened through groups. A merged MirrorSolid used as a seed needs the optional `CombineRecord`; without it the step fails with a report entry.
- **Seeds failed or removed in the first half:** they become skipped child entries.
- **Pattern without its seed:** fails with a clear message (P12).
- **VerifySteps off:** no mass checks, but the MirrorSolid body check still runs.
- **Configurations and "Original löschen":** unchanged rules; M and every child entry must be OK.
- **Progress bar and cancel:** none.

### 7. Plan and Core tests
1. **Core.**
   - `MirrorFeatureRecords`.
   - `MirrorFeatureRules`: `SecondHalfName`, `SeedsIndependent` (returns false when a value is null), `OffPlane`, `ExpectedNewBodies`, `Aggregate`.
   - `Reflection.TryFromAffine`.
   - `MirrorReport.Nested`.
2. **Tests:**
   - The conjugation identity, with P intersecting, parallel to and equal to Π.
   - det(R_Π∘R_P) = +1.
   - `TryFromAffine` round-trip, and rejection of rotations, shears and glide reflections.
   - `SeedsIndependent`: disjoint seeds, a seed straddling P, null values.
   - `OffPlane`.
   - Names: "X (M1)", "X (M1) (M2)", and the suffix placed last.
   - `Nested` keeps statuses and inherits the S′ status; `AllMirrored` is false when a child failed.
   - `Aggregate` truth table, including StopOnError.
   - `BodyScope` with MirrorSolid seed bodies counted as selected.
3. **Add-in prerequisites:**
   - `BuildShared` with `Built`;
   - generation counter;
   - mark-based `BeginStep`;
   - `StepRunner` extraction;
   - group `BuildResult`;
   - `Counterparts`;
   - `FindSeeds` and `VariablePatternBuilder` switched to `Counterparts`;
   - `FindBody` made internal.
4. **Capture, collector, builder:** in the order of §2 to §4.
5. **Docs:** ARCHITEKTUR, STOLPERSTEINE (C2, C3, C7, C9, MirrorStock, live-test rows), README "Noch nicht" row.
6. **Builds:** build against 2019 and 2024, run the Core tests, then the live tests below.

### 8. Live tests
1. The type names, and that `GetDefinition` answers with the matching interfaces.
2. `Plane` and `Face` return `Face2` when the plane type is 0, and a RefPlane feature when it is 1.
3. The seed arrays, masses and `GetFeatures` reflect the state at M under AccessSelections.
4. `GetTransform` is the reflection from source to copy (det −1).
5. Mirrored bosses follow the seed's own Merge flag.
6. A pattern mirrored without its seed, and the effect of `GeometryPattern`.
7. `EditRollback` and `AccessSelections` work during the build under `EnableFeatureTree = false` and graphics off, in the same part too. This is still needed for `ReadSketch`, Shell and AdvancedHole capture of the copies.
8. `SketchMap` sketch-entity pointers stay valid across those rollbacks.
9. `InsertCombineFeature(15903, null, Body2[])` works in 2019, including the selection-mark fallback.
10. What `GetFeatures` returns for a body made by MirrorSolid with Merge off: M only, or M plus the seed body's features.
11. Merge is offered only with a planar face as P.
12. `owner.GetFaces()` inside M's access window gives the face set at M (needed for the P1 rule).
13. Whether a mirrored instance's up-to end condition ends at the same face or at the mirrored face.

**Sources:**
- [IFeatureManager::InsertCombineFeature (2023)](https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeatureManager~InsertCombineFeature.html)
- [Combine Bodies Example (2024)](https://help.solidworks.com/2024/English/api/sldworksapi/Combine_Bodies_Example_VBNET.htm)
- [Get Mirror Pattern Feature Data Example (2019)](https://help.solidworks.com/2019/English/api/sldworksapi/Get_Mirror_Pattern_Feature_Data_Example_VBNET.htm)
- [Get Mirror Feature Data Example (2022)](https://help.solidworks.com/2022/English/api/sldworksapi/Get_Mirror_Feature_Data_Example_VB.htm)