**Mirror features in Spiegelwerk: design for rebuilding them without the Mirror API**

**Answer to the user's question:** yes, mirror features are not supported in the tool today. `FeatureCapture.Capture` turns `MirrorPattern` into an `UnsupportedRecord` with the reason "wird bewusst nicht nachgebaut …". `MirrorSolid` and `MirrorStock` get the generic "Feature-Typ … wird nicht unterstützt". All three count as solid-changing, so the report marks them "Fehler" and the copy contains only the seed half.

The earlier wording "die zweite Hälfte entsteht aus der Saat durch eine Drehung bzw. Verschiebung" describes approach B below. The recommended design builds the second half differently: it reflects the already-mirrored first half at the mirrored mirror plane. It still uses only ordinary feature creation and no Mirror API. The sentence to the user should be corrected accordingly.

## 1. Verdict on the proposed approach

**The maths holds.** For any isometry g, g∘R_P∘g⁻¹ = R_g(P), and R_Π is its own inverse. So the second half the target needs is:

R_Π(R_P(x)) = R_P'(R_Π(x)) with P' = R_Π(P)

That is a reflection of the already-built S' across P'.

**The code supports a child context with a different reflection.**
- `ctx.Mirror` is read in 21 files and 45 places: builders, `SketchBuilder`, `StepVerifier`.
- `PlaneResolver` and `TopologyFinder` never touch `ctx.Mirror`. They only receive geometry that the caller has already mirrored.
- Swapping the `Reflection` in a child `BuildContext` is therefore enough for the geometry.

**"Same FeatureCapture/FeatureCollector/MassCapture code, target as source" does not work literally.** Six corrections are needed:

- **C1 Collection.** `FeatureCollector.Collect(target, …)` cannot be reused.
  - Its pattern seed closure would add seeds that M did not mirror.
  - Its parent-sketch rule uses `SwDoc.IsSketch`, which includes `3DProfileFeature` (the "Spiegelwerk-Anker" helper sketches).
  - It also brings standard-plane and body-scope logic that does not apply.
  - A dedicated `CollectCopies` is needed (§3.3c).
- **C2 Capture mode for the job's own copies.**
  - *(a) Feature-scope notes.* In same-part separate-body mode the job itself set `FeatureScope = true, AutoSelect = false` on every S'. The capture then adds the note "Der Feature-Bereich … wird nicht übernommen" (FeatureCapture.cs l.158, l.233; Holes l.149; Patterns l.273 and `NoteScope`; SweepLoft l.61, l.131). Any note makes a step "Hinweis", so every second-half step would be a warning and "Original löschen" would be blocked.
  - *(b) Merge flag.* In separate-body mode the first boss is created with `Merge = false` (`PlanSeparateBody`: `createMerge = !SeparateBody && merge`; scope is only set when `ownBefore > 0`). So S'₁.Merge is false. Replayed in the child, `PlanSeparateBody` returns scope = false and createMerge = false, and the copy becomes a separate body instead of merging.
  - Fix: take Merge/FeatureScope/AutoSelect from the original record of the seed, and suppress the scope notes.
- **C3 Topology cache.** `PlaneResolver` calls `_ctx.Topology.Invalidate()` on the context it was created with. If the child shares the resolver, the child's `TopologyFinder` goes stale after a helper plane or axis is created (STOLPERSTEINE 71). Fix: a job-wide invalidation counter.
- **C4 Per-step references.** ΔS'ᵢ measured in the target is a valid reference for S''ᵢ only if the second half does not interact with the first. Counter-examples: a seed straddling P, faces on P merging, through-all cuts. Fix: an independence test and a step-check mode (§3.3f).
- **C5 One record becomes many features.** Several places assume a single feature per record: `BuildOne`, `StepVerifier` (`result.Feature!`), `RemoveAgain`, `CheckRebuildError`, `PatternSupport.FindSeeds` and `VariablePatternBuilder` (`ctx.Created[name]`). They need group support.
- **C6 MirrorSolid.** The child needs its own body set so the copy becomes a new body, plus a Combine when Merge is set.

## 2. Two approaches compared, recommendation: A

**A. Nested reflection: re-capture S' from the target, rebuild at P'.**
- Reuses every orientation rule unchanged, because R_P' is a reflection: σ table, arc sense, circular pattern `!Reverse`, sweep twist "gegensinnig", fill pattern π−θ and −α, `ApplyKeepingXY`, table z-sign, `PatternInstances.Mirror`.
- S' carries flags that were already measured and corrected in the target, and the capture reads them back.
- No Core generalisation is needed.
- Costs: one re-capture per nested record (AccessSelections plus two rollbacks); stand-ins and losses of S' carry over (they are already reported on S'); needs fixes C1–C6.

**B. Rigid motion T = R_Π∘R_P applied to the source records.**
- No re-capture.
- But `Reflection` is baked into Core: `SketchMirror`, `AxisMap`, `DirectionRules.Sigma`, `MassState`/`EdgeSignature`/`FaceSignature`/`GeometryRef.Mirror`, `PatternInstances`, `TablePoints`, `FillPatternRules`.
- Every "a mirror reverses orientation" rule in the builders would need a det(T) branch (PatternBuilder l.68, SweepBuilder l.71–72, FillPatternBuilder, CoordSys, chamfer and thin-side first guesses).
- That is a large, risky refactor.

**Recommendation: A.**

## 3. Specification of A

### 3.1 Capture

**GetTypeName2 names.**
- `"MirrorPattern"`: mirror of features and/or faces.
- `"MirrorSolid"`: mirror of bodies.
- `"MirrorStock"`: mirror part.
- `"CombineBodies"`: the Combine feature.
- Source for the names: a third-party mapping (CADBooster SolidDNA `ModelFeatureTypeMapping`); confirm live.
- `swFeatureNameID_e`: `swFmMirrorSolid = 4`, `swFmMirrorPattern = 7`. Never used for creation; `InsertMirrorFeature` and `InsertMirrorFeature2` exist and stay off-limits.

**Interop 27.3.0 members** (checked by reflection; 32.1.0 is identical except one addition noted below).

`IMirrorPatternFeatureData`:
- `object Plane {get;set;}`
- `int GetMirrorPlaneType()`: `swMirrorPlaneType_e` with Face = 0, Plane = 1.
- `object PatternFeatureArray {get;set;}`, `int GetPatternFeatureCount()`.
- `object MirrorFaceArray {get;set;}`, `int GetMirrorFaceCount()`.
- `bool GeometryPattern {get;set;}`, `bool PropagateVisualProperty {get;set;}`.
- `int FeatureScope {get;set;}`: `swFeatureScope_e` with AllBodies 0, SelectedWithAutoSelect 1, SelectedWithoutAutoSelect 2.
- `object FeatureScopeBodies {get;set;}`, `int GetFeatureScopeBodiesCount()`.
- `bool AccessSelections(object TopDoc, object Component)`, `void ReleaseSelectionAccess()`.
- It has **no** PatternBodyArray, Merge, KnitSurface or GetTransform.

`IMirrorSolidFeatureData`:
- `object Face {get;set;}`.
- `object PatternBodyArray {get;set;}`, `int GetPatternBodyCount()`.
- `bool Merge {get;set;}`, `bool KnitSurface {get;set;}`, `bool PropagateVisualProperty {get;set;}`.
- `MathTransform GetTransform()`.
- `AccessSelections(object, object)`, `ReleaseSelectionAccess()`.
- 32.1.0 adds `StructureSystemToPatternArray`.
- It has no GeometryPattern and no FeatureScope.

**AccessSelections is required for both**, as in the official "Get Mirror (Solid) Feature Data" examples. Plane or face, seed features, seed faces and seed bodies must be read in the state at M. Face fingerprints and body masses must be computed inside the access window (STOLPERSTEINE 24).

**New Core record**, `src/Spiegelwerk.Core/Records/MirrorFeatureRecords.cs`:

```csharp
public enum MirrorSeedKind { Features, Bodies }
public sealed class MirrorFeatureRecord : FeatureRecord   // ChangesSolid = true (inherited)
{
    public MirrorFeatureRecord(string name, string typeName, MirrorSeedKind kind, Plane3 plane);
    public MirrorSeedKind Kind { get; }
    /// P in part coordinates of the ORIGINAL, unmirrored like every record; normal sign irrelevant.
    public Plane3 Plane { get; }
    public bool PlaneIsFace { get; set; }                       // info only
    public List<string> SeedFeatures { get; }                   // original names: seeds, owners of seed faces, features of seed bodies; suppressed seeds left out
    public List<FaceSignature> SeedFaces { get; }               // MirrorFaceArray (for the message)
    public List<MassState> SeedBodies { get; }                  // MirrorSolid, state at M
    public bool GeometryPattern { get; set; }
    public bool PropagateVisualProperty { get; set; }
    public bool Merge { get; set; }                             // MirrorSolid
}
```

**How the plane is stored.** New file `Capture/FeatureCapture.Mirror.cs` with `MirrorPlane(object o)`:
- Reuse `ToGeometryRef(o)`.
- `Kind == Plane` gives `g.Plane`.
- `Kind == Face` with a planar `FaceSignature` gives `new Plane3(g.Face.Sample, g.Face.Normal)`.
- Anything else throws `NotSupportedException`.

**MirrorSolid fallback for the plane:** `Reflection.TryFromAffine(_math.ToAffine(d.GetTransform()))`, a new Core helper. Conditions: det = −1, M symmetric and orthogonal. Then n = normalised column of (I − M), and p₀ = translation / 2.

**Capture routines.** Add `case MirrorPatternFeatureData mp:` and `case MirrorSolidFeatureData ms:` to the switch in `Capture`.

`CaptureMirrorPattern`:
- Seeds come from `PatternFeatureArray`, skipping `IsSuppressed()` seeds.
- For each face in `MirrorFaceArray`: record its fingerprint, and add `face.GetFeature()` to `SeedFeatures`.
- `FeatureScope == 2` produces a note.
- No note for `GeometryPattern`; the M-level check decides.

`CaptureMirrorSolid`:
- For each `Body2` in `PatternBodyArray`: if `((IBody2)b).GetType() != swSolidBody`, throw `NotSupported` (surface bodies, KnitSurface).
- Record the mass the same way as `ReadSeeds`.
- Add the names from `b.GetFeatures()`.
- Read `Merge`.

`UnsupportedReason` changes:
- "MirrorPattern" and "MirrorSolid" become "Spiegel-Feature: Feature-Daten nicht lesbar."
- "MirrorStock" gets its own text (below).

**MirrorStock stays unsupported.** It is a derived part: its geometry is an imported body from another file (`IMirrorPartFeatureData.PathName`, `GetModelDoc()`), and there are no seed features in this part to rebuild. Every later feature sits on it. The independent equivalent is "run Spiegelwerk (new part) on the referenced part", and the message should say exactly that.

**Copy-mode capture:** `FeatureCapture(ModelDoc2 doc, SwMath math, bool ofOwnCopy = false)`.
- With `ofOwnCopy`, all six scope notes are suppressed.
- After capture, `PatchFromOriginal(record, original)` copies `Merge`, `FeatureScope` and `AutoSelect` (Extrude, Revolve, Sweep, Loft) from the original record. It finds it through `ctx.LogicalName(targetName)` and `BuildShared.RecordsByLogicalName`.

### 3.2 FeatureCollector (outer)

Add two cases to `SeedsOf`. The existing `AddSeeds` already handles face owners, body features and `seedBodyNames`:

```csharp
case MirrorPatternFeatureData mp when mp.AccessSelections(doc, null):
    try { seedBodies |= AddSeeds(result, seedBodyNames, false, mp.PatternFeatureArray, mp.MirrorFaceArray, null); }
    finally { mp.ReleaseSelectionAccess(); } break;
case MirrorSolidFeatureData ms when ms.AccessSelections(doc, null):
    try { seedBodies |= AddSeeds(result, seedBodyNames, true, null, null, ms.PatternBodyArray); }
    finally { ms.ReleaseSelectionAccess(); } break;
```

- **Body seeds** set `seedBodies = true`. In same-part feature mode this makes the outer run a separate body, as for body patterns.
- **Seed bodies** count as selected in `ScopeOf`.
- **Chains** resolve through the existing queue: pattern of M → M → seeds.
- **Whole-part mode** already contains the seeds.
- **Selecting a body that was itself made by M** pulls in the seed body too. This is consistent with body patterns. "Original löschen" is then blocked by `BodyScope.UnselectedBodies`.

### 3.3 Build

**a. Shared state.** Add a job-wide `BuildShared` holding:
- the one `PlaneResolver`;
- `JobNames`: every feature and helper the job created;
- `HelperNames`: what `RegisterHelper` records;
- `int TopologyGeneration`;
- `RecordsByLogicalName`;
- the progress sink.

New factory: `BuildContext.Nested(parent, Reflection mirror, bool separateBody, OwnBodies own, Func<string,string> nameFor, MirrorReport report, StepCheck check)`.

| Item | Child behaviour |
| --- | --- |
| `Sw`, `Doc`, `Math`, `Options` | Same instances. |
| `Report` | Child's own report; entries are re-homed afterwards (§4). |
| `PlaneResolver` | Shared instance. It only uses `_ctx.Doc`, `Math`, `RegisterHelper` and `Topology.Invalidate`, never `Mirror`. Shared counters keep helper names unique, and the working-tree `Planes.Forget` keeps working. |
| `TopologyFinder` | One per context: it reads `_ctx.IsOwnBody`, `_ctx.Sketches` (`FindLineObject`) and `_ctx.SamePart`. |
| Invalidation | `Invalidate()` becomes `_shared.TopologyGeneration++`. `Load()` reloads when its stamp differs. This fixes C3. |
| `Sketches`, `Created`, `PartOwners`, `PointSketches` | Child-own, keyed by S' target names. |
| Own bodies | `IsOwnBody` reads `_ownNames`. MirrorPattern child: the parent's set (same instance). MirrorSolid child: a fresh set. |
| Registration | `Register` and `RegisterHelper` always also add to `JobNames`. |
| Outer context | `_ownNames == JobNames`. |

**b. Names.**
- Second-half feature: `MirrorFeatureRules.SecondHalfName(seedLogical, mName) => $"{seedLogical} ({mName})"`.
- Child `NameFor(n) = parent.NameFor(SecondHalfName(parent.LogicalName(n), m.Name))`, so the same-part suffix stays last: "Boss-Extrude1 (Spiegeln1)_gespiegelt".
- `LogicalName` maps a target name to its original name, built from `Created` (value.Name → key) and group members.
- Nested mirrors give "X (M1) (M2)".
- Hole sketches keep the names SOLIDWORKS gives them (`PartOf`).
- Helper planes, axes and coordinate systems keep the shared "Spiegelwerk-…" counters.
- `FeatureNames.Free` resolves clashes as before.

**c. Keeping helper features out: `FeatureCollector.CollectCopies(ModelDoc2 target, IEnumerable<Feature> seeds, Func<Feature,bool> isCounterpart, skipped)`.**
- `wanted` = seed counterparts, plus their absorbed `ProfileFeature` sub-sketches (holes), plus parent sketches for which `isCounterpart` is true.
- Order: `SwDoc.FeaturesInCreationOrder(target)` filtered to `wanted`.
- No pattern seed closure, and no non-sketch parents.
- Reuses `SkipReason` and `SkippedSketchOf`, so an S' with a rebuild error is skipped with a reason.
- `isCounterpart(f)` = f is a value of `Created` or a member of a group, and `!HelperNames.Contains(f.Name)`.
- Helpers only ever go through `RegisterHelper`, never `Register`, so "Spiegelwerk-Ebene/Achse/Anker/KS" cannot enter. A table pattern whose coordinate system was a helper falls back to a new helper at R_P'(frame), via `TablePatternBuilder.CoordinateSystem`.

**d. Nested MassCapture rollbacks in the target during the outer UpdateFreeze.** Order of operations inside `MirrorFeatureBuilder.Build`, before any second-half feature exists:
1. Assert there is no active sketch, and clear the selection.
2. Run `CaptureAll(new FeatureCapture(target, math, ofOwnCopy: true), copies)`. This is the shared helper factored out of `MirrorJob.Run`, including the hole `PartOf` / `CaptureOnItsOwn` post-processing.
3. If `VerifySteps` is on, run `MassCapture.Measure(target, nested)`.
4. Remove face samples on P' (`MirrorFeatureRules.OffPlane`).
5. Call `ctx.Topology.Invalidate()`: Face2 and Edge objects do not survive the rollbacks.

Why this is safe:
- `MassCapture` always returns to `swMoveRollbackBarToEnd` in `finally`.
- S' are appended after every original feature, so in same-part mode a rollback never hides original features permanently.
- UpdateFreeze only freezes the tree and graphics view; `FeatureEdit`/`AccessSelections` already roll back under it today.
- Fallback if `EditRollback` returns false while the tree is disabled: set `EnableFeatureTree = true` just for the nested capture.

**e. Body semantics.**

| Case | Child setup | Behaviour |
| --- | --- | --- |
| MirrorPattern, new part or same-part feature mode | `SeparateBody = false` | Patched original Merge, so bosses merge with what they touch and cuts cut what they reach, as SOLIDWORKS re-solves mirrored features. |
| MirrorPattern, same-part whole/body mode | `SeparateBody = true`, own bodies = parent's set (S' body) | `PlanSeparateBody` scopes bosses (merge) and cuts to own bodies. Child `StepRunner` guards `OriginalBodies()` on every step, so the original is never touched. |
| MirrorSolid, Merge = false | Always `SeparateBody = true`, fresh own-body set | The first boss creates a new body; later steps are scoped to it. The guard protects S' and the original. |
| MirrorSolid, Merge = true | As above, then a Combine | For each seed body: S' body = `FindBody(ctx, seed.Mirror(R_Π))`, new body = `FindBody(child, seed.Mirror(R_Π).Mirror(R_P'))`. Then `FeatureManager.InsertCombineFeature((int)swBodyOperationType_e.SWBODYADD /*15903*/, null, new object[]{s1Body, newBody})`. The Combine is named `ctx.NameFor(m.Name)` and becomes part of the group. |

`InsertCombineFeature(int OperationType, Body2 MainBody, object ToolVar)` exists in interop 27.3.0 (SOLIDWORKS 2019). The 2019 `swFeatureNameID_e` has no Combine definition id, so this method is the only route.

**f. Outer step verification of M.**
- `BuildOne` measures `before` before the nested run.
- `StepVerifier.Verify(parent, M, result, before)` compares the change of the whole nested run with `M.ExpectedChange.Mirror(R_Π)` (measured in the source as usual).
- Face samples: `R_Π(M.FaceSamples)` from `M.GetFaces()`.
- Group-aware: rebuild error = any group feature in error; own faces = union of group faces; no Alternatives.
- **Step-check mode for nested steps:** compute Σ `rᵢ.ExpectedChange.Mirror(R_P')` and compare it with `MassComparison.Compare(M.ExpectedChange.Mirror(R_Π), Σ, MassOf(target))`.
  - If it passes, the seeds are independent: `StepCheck.PassOnly` (variant search, but a variant is kept only if it passes; a mismatch is a warning as usual).
  - Otherwise, or if anything is unmeasured: `StepCheck.ReportOnly` (the result is printed as information; no variant search, no status change). This prevents a wrong variant from winning when the correct one interacts with the first half.

**g. Registration so later features can use M as a seed.**
- `ctx.RegisterGroup(m.Name, List<(Feature f, bool changesSolid)>)` on the outer context, in tree order.
- `ctx.Counterparts(string original, bool solidOnly)` returns the group, or the single `Created` entry.
- Replace `ctx.Created.TryGetValue` with `Counterparts` in `PatternSupport.FindSeeds` (pattern of M: seeds = solid group members, mark 4) and in `VariablePatternBuilder` (l.42).
- `Unregister` and `Forget` also drop groups.
- M as seed of M2: M2's `Counterparts("M1")` = G1, collected by `CollectCopies`. G1 members are ordinary features, so nesting is flattened: a nested run never contains a `MirrorFeatureRecord` (assert this).
- Later features that refer to geometry made by M need no registration: they find it by fingerprint.

**Required refactors.**
- Move `BuildOne`, `RemoveLeftovers`, `NewFeatures`, `RemoveAgain`, `CheckRebuildError`, `WithdrawParts`, `OriginalUntouched` and the `Builders` array (plus the new `MirrorFeatureBuilder`) from `MirrorJob` into `Build/StepRunner.cs`.
- `BuildResult` gains:
  - `IReadOnlyList<Feature>? Group` and `AllFeatures`;
  - `List<ReportEntry> ChildEntries`;
  - `OfGroup(group, status, message)`, with `Feature` = last group member.
- `RemoveAgain` deletes `AllFeatures` in reverse order and calls `ctx.Forget`.
- If no solid nested step succeeded, the builder deletes the partial group and fails.

### 4. Report

- `BuildOne` adds M's entry first, then `result.ChildEntries`.
- Child entries are re-created (`ReportEntry.Feature` is get-only) by a Core helper `MirrorReport.Nested(owner, entries, logicalName)`:
  - name `"Spiegeln1 › Boss-Extrude1"`, with the same type, status, verification and details;
  - plus the detail "im Ziel: „Boss-Extrude1 (Spiegeln1)“".
  - This follows the existing "heading › feature" convention from `Append`.
- **M's entry:**
  - Type MirrorPattern or MirrorSolid.
  - Message: "Zweite Hälfte an R(P) nachgebaut: n Features (…)"; for face seeds also "Flächen als ganze Features nachgebaut".
  - Status from `MirrorFeatureRules.Aggregate(children, wholeCheckPassed)`: Failed if no solid child succeeded; Warning if any child is not mirrored or not OK, or the whole check fails; otherwise OK.
  - Details: missing seeds, feature scope, and the GeometryPattern hint when the check fails.
- **`report.General`, once per job:** "Gleichungen steuern nur die erste Hälfte von Spiegel-Features." It goes there because a note would make the entry a warning.
- `AllMirrored` and `Blocking` see the child entries automatically.

### 5. Edge cases

- **P is a face of the part.** The face fingerprint is read in the state at M and turned into a `Plane3`. Nothing in the target is ever selected as P'; only the reflection is used. A non-planar face is unsupported.
- **P equal to Π.**
  - P' = P (flipped normal), and the second half is S at its original position.
  - In same-part mode the copy body coincides with the original. Own-body preference in `TopologyFinder`, `FindVertex` and `IsSameFace`, plus the guard, keep it apart.
  - With "Original löschen" this effectively dissolves the mirror into independent features.
  - Seed sketches on P give σ = −1, which is already handled.
- **Nested mirror features.** Flattened through groups; names "X (M1) (M2)". M2 of a merged MirrorSolid contains a `CombineBodies` feature, which today is unsupported in capture. Optional: add a `CombineRecord` (`ICombineBodiesFeatureData.MainBody`, `BodiesToCombine`, `OperationType`) with a builder.
- **Seeds that failed to build.**
  - Missing counterparts, or counterparts with a rebuild error (skipped by `SkipReason`), become Skipped child entries plus a note.
  - Seeds suppressed in the original are already left out at capture, matching the original M.
  - No solid seed in the target: M fails before anything is created.
- **Holes as seeds.** Hole sketches come in as sub-sketches and become `PartOf`. The position-sketch stand-ins (fixed construction-line ends) are replayed unchanged.
- **Patterns as seeds.**
  - Instances are re-read from S' and compared as R_P'∘T∘R_P'.
  - Coordinate systems fall back to a helper.
  - A pattern whose own seed is not among M's seeds fails with a clear message. What SOLIDWORKS itself does here needs a live test.
- **"Original löschen".** Requires M and every child entry to be OK. The configuration rule and the BodyScope rule are unchanged.
- **400-instance limit.** `MaxInstancesRead` applies unchanged to every re-captured pattern: placements above 400 instances are not read, and only the mass check remains. Re-reading doubles the `GetTransform` calls.
- **StopOnError.**
  - A failed child step stops the nested run and M is "Fehler", so the outer loop stops too; what was built is kept.
  - An exception in capture or collection goes to the outer `RemoveLeftovers`, which deletes everything created since M started.
  - The shared `Planes.Forget` drops nested helpers.
- **Progress bar.**
  - M stays one unit of the total.
  - The nested run updates only the label through the shared sink: "Spiegeln1: Saat erfassen", then "Spiegeln1 › Boss-Extrude1 (k/n)".
  - Cancel is checked between nested steps (`UpdateProgress` returns `swUpdateProgressError_UserCancel = 2`) and handled like StopOnError.
- **VerifySteps off.** No nested MassCapture, no independence test, no M check.
- **GeometryPattern.** The rebuild always re-solves end conditions, and the M check shows any difference.

### 6. Implementation plan and Core tests

1. **Core.**
   - `MirrorFeatureRecords.cs`.
   - `Planning/MirrorFeatureRules.cs`: `SecondHalfPlane(run, P)`, `SecondHalfName`, `SeedsIndependent(whole, parts, total)`, `OffPlane(samples, plane)`, `Aggregate(...)`.
   - `Reflection.TryFromAffine`.
   - `MirrorReport.Nested`.
2. **Core tests** in `tests/Spiegelwerk.Core.Tests/MirrorFeatureTests.cs`:
   - R_Π(R_P(x)) = R_{R_Π(P)}(R_Π(x)) for random points, with P intersecting, parallel and equal to Π, through and off the origin.
   - R_Π(R_Π(P)) is coplanar with P; P = Π gives P' coplanar with Π.
   - R_Π∘R_P is proper rigid (det +1): rotation by 2θ about P∩Π, or translation by twice the distance.
   - `TryFromAffine(new Reflection(P).ToAffine())` gives back P; rotations and non-orthogonal transforms are rejected.
   - `SeedsIndependent`: true for disjoint seeds, false for a boss straddling P (MassState sums).
   - `OffPlane` drops samples on P'.
   - Names: "Boss1 (M1)", "Boss1 (M1) (M2)", and the suffix applied after the second-half name.
   - `Nested` renames entries, keeps statuses, and `AllMirrored` is false on a failed child.
   - `Aggregate` truth table.
   - `MirrorFeatureRecord` defaults: `ChangesSolid`, unmirrored `Plane`.
   - `BodyScope` with seed bodies of a MirrorSolid counted as selected.
3. **Add-in prerequisites:** `BuildShared`, finder generation counter, `StepRunner` extraction, `BuildResult` group and children, `Counterparts`/`RegisterGroup`, `FindSeeds` and `VariablePatternBuilder` switched to `Counterparts`, `FindBody` made internal.
4. **Capture:** `FeatureCapture.Mirror.cs`, the `ofOwnCopy` flag with the scope-note gate, the `Capture` switch cases, `UnsupportedReason` texts (including MirrorStock), `SeedsOf` cases.
5. **Builder:** `CollectCopies`, `PatchFromOriginal`, `StepCheck` in `StepVerifier`, `MirrorFeatureBuilder` (steps of §3.3), Combine.
6. **Docs:** ARCHITEKTUR (section "Spiegel-Features", class tables), STOLPERSTEINE (C2a/b, C3, C4, MirrorStock, live-test rows), README "Noch nicht" row.
7. Build against 2019 and 2024, run the Core tests, then the live tests below.

### 7. Assumptions that only a live SOLIDWORKS test can settle

1. The type names: a body mirror is "MirrorSolid", a feature/face mirror is "MirrorPattern", and `GetDefinition` answers with the matching interfaces.
2. `IMirrorPatternFeatureData.Plane` returns a `Face2` when `GetMirrorPlaneType() == 0` and a RefPlane `Feature` when it is 1. `IMirrorSolidFeatureData.Face` behaves the same.
3. `PatternFeatureArray`, `MirrorFaceArray` and `PatternBodyArray` (with their masses) reflect the state at M under AccessSelections, and `face.GetFeature()` returns the owner feature.
4. `IMirrorSolidFeatureData.GetTransform()` is the reflection (det −1) from source to copy.
5. Mirrored bosses follow the seed's own Merge flag (`IMirrorPatternFeatureData` has no Merge). `FeatureScope` uses `swFeatureScope_e`.
6. What SOLIDWORKS does when mirroring a pattern without its seed, and how `GeometryPattern` differs from re-solving.
7. `EditRollback` and `AccessSelections` work in the target during the build under `EnableFeatureTree = false` and graphics off, in the same part too.
8. Sketch-entity pointers held in parent `SketchMap`s stay valid across those rollbacks.
9. `InsertCombineFeature(SWBODYADD = 15903, null, Body2[])` creates the Combine in 2019. The API help says MainBody must be null for Add and the operation is a `swBodyOperationType_e`, not `swCombineBodiesOperationType_e` (Add 0). Pair-wise combine for several touching bodies is also untested.
10. `IBody2.GetFeatures()` of a seed body lists every shaping feature; `IFeature.GetFaces()` of M after rollback gives the mirrored faces.
11. Merge with MirrorSolid is only offered with a planar face as P.

**Probe:** member lists came from the new reflection probe at `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/mirrorprobe` (`Program.cs`, run as `dotnet out/mirrorprobe.dll <asm> members <Type>`). help.solidworks.com was blocked by the proxy (403), so semantics come from search-result summaries only.

**Sources:**
- [IFeatureManager::InsertCombineFeature (2019)](https://help.solidworks.com/2019/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~InsertCombineFeature.html)
- [Combine Bodies Example](https://help.solidworks.com/2024/English/api/sldworksapi/Combine_Bodies_Example_VBNET.htm)
- [Forum: InsertCombineFeature](https://3dswym.3dexperience.3ds.com/question/solidworks-user-forum/how-to-use-insertcombinefeature_XWzT2S8RRTK1_4Ze1cVr3Q)
- [Get Mirror Pattern Feature Data Example](https://help.solidworks.com/2019/English/api/sldworksapi/Get_Mirror_Pattern_Feature_Data_Example_VBNET.htm)
- [IMirrorPatternFeatureData::Plane](https://help.solidworks.com/2015/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IMirrorPatternFeatureData~Plane.html)
- [Get Mirror Solid Feature Data Example](https://help.solidworks.com/2024/English/api/sldworksapi/Get_Mirror_Solid_Feature_Data_Example_CSharp.htm)
- [IMirrorSolidFeatureData::GetTransform](https://help.solidworks.com/2015/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IMirrorSolidFeatureData~GetTransform.html)