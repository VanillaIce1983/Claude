# Integration plan: sheet metal in Spiegelwerk (areas 1–4)

This plan merges the four cross-checked sheet-metal designs into one build order. Inputs:

- the drafts `docs/entwurf/blech-grundlagen.md`, `blech-laschen.md`, `blech-biegungen-ecken.md` and `blech-umformwerkzeuge-ersatzweg.md`;
- their reviews in this folder: `blech-grundlagen-gegenpruefung.md` (A1), `blech-laschen-gegenpruefung.md` (A2), `blech-biegungen-ecken-gegenpruefung.md` (A3) and `blech-umformwerkzeuge-ersatzweg-gegenpruefung.md` (A4). The section "(2) Corrected design" of each review supersedes its draft;
- the code in `/home/user/installer-lane/spiegelwerk` (HEAD `54a24d7`), plus the two finished but unmerged branches that rewrite the same files: `feat-spiegel-features` (`2f9787d`, worktree `/home/user/lane-mirror`) and `feat-skizzenbereiche` (`3c808c4`, worktree `/home/user/lane-regions`).

Where this plan and a review's own implementation plan (A1 §12, A2 §10, A3 §10, A4 §12) disagree, this plan wins. Everything else in the corrected designs stands. References like "A2 P6" or "A4 §4.3" point into the reviews. Nothing outside `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech` was modified.

**Decisions in brief**

1. **Branches first.** Merge `feat-spiegel-features` and `feat-skizzenbereiche` before any sheet-metal work. They move `BuildOne`, `RemoveAgain`, `RemoveLeftovers` and `WithdrawParts` into `Build/StepRunner.cs`, add `FeatureCapture.CaptureAll`, `BuildShared`, `AbortRunException`, group results (`BuildResult.Group`/`AllFeatures`), and refactor `ExtrudeBuilder` into `ProfileSupport`. Every slice below touches these files.
2. **Refusal.** One two-step model replaces the four proposals:
   - **Before capture**, by type: a sheet-metal type is refused until the slice that decides it has landed. Carrier types without a builder are refused while the fallback is off.
   - **After capture and planning**, by position: an unsupported record that carries a sheet-metal body is refused when no fallback plan covers it.
   - The model starts out inert (S0 refuses exactly what is refused today). Each slice opens its own types.
3. **One shared mechanism per job.** Area 1 owns the value types and `SwSheetMetal`. Beyond that there is one each of:
   - ownership API;
   - sketch-on-face mechanism;
   - side-preserving face pick by ray;
   - witness check;
   - new-feature detector (`NewSince`, folder-aware);
   - capture-exception wrapper.
4. **Order:**
   - S0: inert foundation.
   - S1a/S1b: base flange and flat pattern; new part first, then the same part.
   - S2: hem and miter flange.
   - S3: edge flange.
   - S4: sketched bend and jog.
   - S5: corners, unfold/fold, cross break, corner relief.
   - S6: normal cut and legacy Insert Bends.
   - **S7: fallback** (S7a capture-time triggers, S7b restart).
   - S8: gusset, tab and slot.
   - S9: forming tools.
   - S10: optional items.
5. **The fallback comes seventh.** It is off by default and needs an owner decision about the README promise. Its make-or-break live tests (B54, B55) are probed with a small macro in the first live session, long before S7. All of its infrastructure is built earlier, so S7 adds only the planner, snapshots, builder and restart.
6. **Live tests.** 89 assumption rows from the four reviews are merged into 73 (B1–B73): A1 has 15 of its own plus 19 draft rows it keeps, A2 has 18, A3 17 and A4 20. The superseded draft rows were checked for anything that would otherwise be lost. Nine rows gate a slice; together with five others they form the first live session (§5.2).
7. **Effort.** About 37–48 developer days (≈ 43 typical) plus 10–12 days of live tests (§6).

---

## 0. Ground rules for every slice

- **No mirror API.** No `InsertMirrorFeature*`, `MirrorPart*`, `InsertMirrorAll`, `MirrorFeature`, derived parts or `MirrorComponents`. The fallback transforms a temporary body copy (A4 P6). This is not a mirror feature, but it does break the "fully editable tree" promise, so it stays opt-in.
- **Ship safely.** Each slice builds against interop 27.3.0 and 32.1.0, runs the Core tests on net8.0 and net48, and leaves every sheet-metal type it does not decide refused before capture (§1.5). No release may build half a sheet-metal part from a family whose slice has not landed.
- **Local checks are always on.** A builder decides direction, side, angle or allowance by its own geometric or read-back check, whether or not "Jeden Schritt prüfen" is on. The checks are:
  - the profile check (A2 §7);
  - witnesses (A3 P2);
  - the jog probe (A2 P6);
  - the convert's local check (A4 §4.3);
  - the gusset section check (A4 P9).

  `StepVerifier` (mass and samples) and the flat-state check (A1 §9) come on top, only with "Jeden Schritt prüfen".
- **Read-back never uses the source-bound capture.** Read-backs go through scalar readers under `AccessSelections` on the target feature: no sketches, no rollback, `ReleaseSelectionAccess` in `finally` (A2 P11). Every edit is followed by a read-back, because `ModifyDefinition` can return true and change nothing (A1 P9, A3 P5).
- **Records** are C# 9, nullable-safe and property-based, with constructors that call `FeatureRecord(name, typeName)` (A2 P15, A3 P14). They hold no COM objects.
- **Report texts** are German. Each slice updates the README row „Noch nicht“, STOLPERSTEINE 139, the new STOLPERSTEINE rows, the live-test table and `docs/entwurf/STAND.md`.

---

## 1. Shared prerequisites

### 1.1 Branch prerequisites (before S0)

| Branch | What sheet metal reuses or must follow |
|---|---|
| `feat-spiegel-features` | <ul><li>`StepRunner.BuildOne/RemoveAgain/RemoveLeftovers/WithdrawParts/CheckRebuildError` (the hook points of the A1 guard and of companions);</li><li>`AbortRunException` + `BuildShared.AbortReason` (the guard's abort, A1 §3);</li><li>`BuildResult.Group`/`AllFeatures` (companions join `AllFeatures`, K22);</li><li>`FeatureCapture.CaptureAll` (owner fix-up, K3);</li><li>`BuildShared` (a restart needs a fresh instance, K20);</li><li>`FeatureCapture.RollBack` + `RollbackFailures` (reused by witness and support-face capture);</li><li>child contexts for mirror features (K23).</li></ul> |
| `feat-skizzenbereiche` | <ul><li>`ExtrudeBuilder` → `ProfileSupport` (A1's second `LinkToThickness` edit lands there);</li><li>`IProfileRecord` (a cut on sheet metal keeps its regions);</li><li>`SwSelect` `SketchRegion` case and `ContourSelectionScope`.</li></ul> |

Line numbers in the reviews refer to `54a24d7`. After the merge, the hole-specific places A1 P18, A2 P13 and A4 P15 name are these:
- `FeatureCapture.CaptureAll` (owner set, l.76 on the branch);
- the Esc loop in `MirrorJob.Run` (l.180) and in `MirrorFeatureBuilder` (l.224);
- `StepRunner.RemoveAgain` (l.292–293);
- `FeatureCapture.CaptureOnItsOwn`;
- `HolePart`/`IsPositionedHole` in `FeatureCapture.Holes.cs`.

### 1.2 Core value types (`Spiegelwerk.Core/Records/SheetMetalRecords.cs`, owned by Area 1)

Area 1's types become the only ones (A1 P17). Area 2's `BendAllowanceSettings`/`ReliefSettings`, Area 3's `BendAllowanceSpec` and Area 4's own `SheetMetalParameters` are dropped.

| Type | Content | Used by |
|---|---|---|
| `BendAllowanceKind`, `ReliefKind` | `swBendAllowanceTypes_e` 1–5, `swSheetMetalReliefTypes_e` 1–5 | all |
| `BendAllowanceData` | Kind, K, BA, BD, `BendTableFile`, `BendCalculationTableFile` (late-bound) | all; feature-level `OwnAllowance` (null = default) |
| `ReliefData` | auto relief, kind, ratio flag, ratio, width, depth (2022+) | A1, A2, A4 |
| `GaugeData` | table path, gauge name, table radius, override flags, K | A1, A4 |
| `SheetMetalParameters` | Thickness, BendRadius, `BendAllowance`, `Relief`, gauge use/file, `UseMaterialParameters?` (late), override flags 0/1/2, **`OverrideDefaults?`** (late, A1 §4), **`OverrideReadCode?`** (A1 §4), `FixedReference` (Face or Edge only, A1 P11) | A1 (template + body), A4 (fallback) |
| `BendValues` (from A3) | Name, `UseDefaultRadius`, Radius, `UseDefaultAllowance`, `Allowance?`, **`UseDefaultRelief`, `Relief?`** (added, A2 P5 also reads relief) | A2, A3, A4 |
| `BendOverride` (from A2) | `Bend`, `What`; `Note()` gives „Biegung „EdgeBend2“ hat eine eigene Biegezugabe (K 0,38); über die API nicht übertragbar.“ | A2, A3, A4 |

Core rules shared by all areas:
- **`Planning/SheetMetalCompare.cs`** merges A1 `SheetMetalRules.Differences` and A2 `SheetMetalParams.Differences`:
  - `Parameters(want, got)`, `Allowance(want, got)`, `Scalars(IEnumerable<(name, want, got, kind)>)`;
  - `Fold(a)` = a mod 2π folded into [0, π];
  - tolerances 1e-9 m for lengths and 1e-6 for K;
  - table paths compared in full, otherwise by file name with a note.
- **`Planning/AllowanceRules.cs`**: `Settable(featureKind, kind)` (Jog + table → false, A2 P7) and `WriteOrder` (`UseDefaultBendAllowance` = false → values → `Type` last → `SetCustomBendAllowance`).
- **`Planning/BendOverrides.cs`**: `Differences(BendValues parent, IEnumerable<BendValues> bends) → List<BendOverride>` (A3 P6, A2 P5). The `BendOrder` difference is note-only.

### 1.3 `FeatureRecord` API (one ownership model, K3)

```csharp
// FeatureRecord
/// <summary>Sketches absorbed by this feature that belong to it alone (hole position/profile, edge-flange profiles, hem width sketches, cross-break lines, form-tool sketches).</summary>
public virtual IReadOnlyList<string> OwnedSketches => System.Array.Empty<string>();
/// <summary>Sub-features or companions come into being with this feature (PartOf records point to it).</summary>
public virtual bool BringsParts => OwnedSketches.Count > 0;
/// <summary>Feature whose state "after this record" is measured (legacy bends: ProcessBends).</summary>
public virtual string MeasureUntil => Name;
/// <summary>May write the part's shared sheet-metal defaults: runs under SheetMetalGuard (A1 §3).</summary>
public virtual bool TouchesSheetMetalDefaults => false;
/// <summary>Faces live on bend sub-features (HemBend, SketchBend …): MassCapture also samples non-sketch sub-features.</summary>
public virtual bool SampleSubFeatures => false;

// UnsupportedRecord
public bool TriggersFallback { get; set; } = true;   // welded corner: false (A3 P11)
```

- `PositionedFeatureRecord` (A4 §3) is the abstract base of `PositionedHoleRecord` and `FormToolRecord`. It has `OwnedSketches => new[] { PositionSketchName }`; a form tool adds its orientation sketch.
- `BaseFlangeRecord` and `FlatPatternRecord` override `BringsParts => true` (SheetMetal companion, flat-pattern sub-sketches and own corner trim). `LegacyBendsRecord` overrides it as well, and sets `MeasureUntil` and `TouchesSheetMetalDefaults`.
- `EdgeFlangeRecord` and `HemRecord` list their sketches. `CrossBreakRecord` lists its line sketch, if any.
- Area 2's `IOwnsSketches`, Area 3's `IOwnsParts` and the draft-1 `BringsParts`-only variant are dropped.
- **Places that use it:**
  - `CaptureAll` owner set: `records.Where(r => r.BringsParts)`.
  - Both Esc loops: `WithdrawParts` for every later record with `BringsParts`.
  - `StepRunner.RemoveAgain`:
    1. `Delete(result.Feature, withAbsorbed: record.OwnedSketches.Count > 0)`;
    2. `ForgetSketch` for each owned sketch;
    3. `result.RemoveHelpers` (companions);
    4. `Unregister`.
  - `CaptureOnItsOwn`, which branches on the owner kind:
    - hole or form tool: the position sketch is read as an ordinary sketch;
    - flange profile or hem sketch: `UnsupportedRecord(false, „Profilskizze der Kantenlasche „X“, die nicht gespiegelt wird – entfällt mit ihr.“)`;
    - SheetMetal or flat-pattern part: A1 §6 texts.
- `FeatureCapture.NonSolidTypes` moves to Core as `UnsupportedFeatures.ChangesSolid(type)`, so it can be tested. `CrossBreak` joins it (A3 P11).

### 1.4 SheetMetal parameters: one model, one write path

- **Two levels.** `Defaults` = `TemplateSheetMetal.GetDefinition()` (2013+, otherwise null; builders use `Defaults ?? Body`). `Body` = the body's `SheetMetal` with its override flags, read with return codes (A1 P9, S11).
- **Feature level.** Each flange or bend feature carries `OwnAllowance: BendAllowanceData?` and `OwnRelief: ReliefData?`, where null means it uses the default. Bend sub-features carry `BendValues`. Differences become notes (`BendOverrides`); there is no write, because writes to `IOneBendFeatureData` are ignored (B23).
- **Allowance write policy** (merges A1 P9, A2 P7, A3 P5, A4 P1). In order of preference:
  1. at creation, when the call takes it (PCBA in base flange v1/v2, `InsertSheetMetal3dBend`, `InsertSheetMetalHem2`; `Initialize` in a data object; `InsertBends2`);
  2. otherwise one **allowance-only** edit with `AllowanceRules.WriteOrder`, never mixed with the geometric edit;
  3. after **every** later edit of that feature, read the allowance back. If it was reset, add a note, and recreate where the area says so (A3 P5);
  4. if `AllowanceRules.Settable` is false, add a note and make no attempt.

  The flat-state check is the real confirmation (A1 P16). The read-back is a hint.
- **Shared defaults are protected** (A1 P1). Every record with `TouchesSheetMetalDefaults` runs under `SheetMetalGuard`: `BaseFlangeRecord`, `LegacyBendsRecord` and `SheetMetalFallbackRecord`.
  - The guard snapshots the template and every original SheetMetal before `Build`. It compares them after `Verify` and again after `RemoveAgain`.
  - On a difference it writes the snapshot back. If that does not restore the template, it throws `AbortRunException` with A1's text.
  - Expected values: in the same part, the snapshot; in a new part, `Defaults ?? Body` from the first base flange (or convert) on.
- **Creation values** come from Core `SheetMetalRules.CreationValues(first, tab, defaults, body, template)` (A1 §8.3).
  - The gauge table is set only on the first body of a new part, and never in the same part (A1 §11, A4 §4.4).
  - Material-driven parameters become explicit values with a note (A1 P10).
- **Writers.** `SwSheetMetal.ReadParameters`/`WriteParameters`/`WriteAllowance`/`NewAllowance` (`Type` last) are the only writers. Every `Set…`/`Get…` return code is checked; non-zero means "not applicable" and gives a note.

### 1.5 Refusal: what replaces `UnsupportedFeatures.RefusesRun` (K1, K2)

**Type table** (`Spiegelwerk.Core/Planning/SheetMetalTypes.cs`). It replaces the plain `SheetMetal` set and keeps `IsSheetMetal(type)`.

```csharp
public enum SheetMetalRole { System, Part, Carrier, Leaf }
public enum SheetMetalState { NotYet, Rebuilt, Unsupported }
public sealed class SheetMetalType
{
    public SheetMetalType(SheetMetalRole role, SheetMetalState state, string? reason = null, bool triggersFallback = true) { … }
    public SheetMetalRole Role { get; } public SheetMetalState State { get; }
    public string? Reason { get; } public bool TriggersFallback { get; }
}
// Each slice flips State for its types (§3). S0: every entry NotYet, so behaviour is today's.
```

| Role | Types (S0 set, plus the two Area 3 adds) |
|---|---|
| System | `TemplateSheetMetal`, `TemplateFlatPattern` |
| Part (sub-feature; never collected on its own) | `OneBend`, `SketchBend` (+ `HemBend`, `Miterbend` once confirmed, B23) |
| Carrier (creates or re-bases a sheet-metal body) | `SMBaseFlange` (a tab is a leaf, same type), `SheetMetal` (only when it has no accepted owner, see below), `FlattenBends`, `ProcessBends`, `SolidToSheetMetal`, `LoftedBend`, `ToroidalBend` (meaning unknown, kept as carrier conservatively) |
| Leaf (acts on an existing body) | `EdgeFlange`, `SMMiteredFlange`, `Hem`, `Jog`, `SM3dBend`, `FlatPattern`, `UnFold`, `Fold`, `BreakCorner`, `CornerTrim`, `CrossBreak`, `CornerFeat`, `CornerRelief`, `NormalCut`, `Rip`, `SMGusset`, `FormToolInstance` |

Types whose names are still unconfirmed (swept flange, tab and slot, vent, welded corner, corner relief) are **not** refused by name. A leaf of that kind is an ordinary Failed record. A carrier of that kind is caught by the positional rule below. Its name is added to the table once its live test confirms it (B44, B64, B71, B72).

**Step 1, before capture** (`MirrorJob.Run`, where `RefusesRun` is called today):

```csharp
public static bool RefusesBeforeCapture(string type, bool partOfAcceptedOwner, bool fallbackOn) =>
    type == "MirrorStock" ||
    (IsSheetMetal(type) && !partOfAcceptedOwner &&
     (StateOf(type) == SheetMetalState.NotYet ||
      (RoleOf(type) == SheetMetalRole.Carrier && StateOf(type) != SheetMetalState.Rebuilt && !fallbackOn)));
```

- `partOfAcceptedOwner` comes from `SheetMetalTree.Owners` (A1 §2): a SheetMetal owned by an accepted `SMBaseFlange` (from S6 also by an accepted legacy Insert Bends), or the flat pattern's own corner trim or break corner. A sub-feature is never refused on its own when its owner is accepted.
- **An unowned SheetMetal** (its body comes from a convert, lofted bend, swept flange or an unknown feature) is a carrier with state `NotYet` until S7a and `Unsupported` from S7a, so with the switch on the fallback can cover such a body. An owned SheetMetal never reaches the state test, because `partOfAcceptedOwner` is true; it is accepted only through its owner.
- **Until S1b:** a same-part run that contains any sheet-metal type is also refused before capture.

**Step 2, after capture and planning, before `CreatePart`** (`SheetMetalPlanner.Refusal`, Core):
- **Input:** `BodyHistory` (name, `IsSheetMetal`, ordered solid feature names, selected) for **every** solid body, also in whole-part mode (A4 P2).
- **Rule:** refuse when no fallback plan covers a sheet-metal body and one of these holds:
  - an `UnsupportedRecord` is the first solid feature of the body's history;
  - an `UnsupportedRecord` has role Carrier;
  - the body's SheetMetal has no accepted owner.
- **From S1a:** active with no plans. This catches swept-flange or lofted bases and unknown carriers.
- **From S7a:** with plans.

**Messages** (all end with „Es wurde nichts gespiegelt.“, which `AssemblyMirror` relies on through the exception):

| Case | Text |
|---|---|
| NotYet type | „Blech-Feature „{f}“ ({Typ}) wird noch nicht nachgebaut. Es wurde nichts gespiegelt.“ |
| Same part before S1b | „Blech im selben Teil wird noch nicht unterstützt („{f}“). Bitte in ein neues Teil spiegeln. Es wurde nichts gespiegelt.“ |
| Uncovered carrier | „„{f}“ trägt den Blechkörper „{Körper}“ und wird nicht nachgebaut ({Grund}).“ + with the switch off: „ Der Ersatzweg ist ausgeschaltet (Option „Blech: nicht nachbaubare Teile als importierten Körper übernehmen“).“ + „ Es wurde nichts gespiegelt.“ |
| MirrorStock | unchanged |

- S0 keeps the old text „Blechteile werden nicht unterstützt (…)“, so `ReportTextTests` stays green.
- S1a switches to the NotYet text and adjusts the test.
- `Reason(type)` gets per-type texts for Unsupported states, for example corner trim (A3 P3), corner relief on 2019–2021, the welded corner, and swept flange on 2019–2020 (A2 P4).

**Leaf failures after a type is decided** are ordinary entries:
- Failed with „Folgende Features … können ebenfalls scheitern“, or Skipped for non-solid records;
- **or** a fallback trigger (§4).

That is the reconciliation of A1 P4 (refuse what the slices do not cover) with A4 P3 (do not refuse a run because of one leaf).

### 1.6 Capture helpers (add-in)

| Helper | Origin | Note |
|---|---|---|
| **Safe dispatch** `FeatureCapture.SheetMetal(f, type, def)` | A1 P13, A2 P10, A3 P9, A4 P15 | Every sheet-metal case goes through one method: `catch (Exception ex) when (!(ex is NotSupportedException))` → `NotSupportedException($"Blech-Feature nicht lesbar ({ex.Message}).")`. Each reader keeps `ReleaseSelectionAccess` and `EditRollback(End)` in `finally`. Cases with a null definition (form tool in 2019) are dispatched by type name **before** `switch (def)`. |
| `SwLate.Invoke(com, method, params object[]) → object?`, `InvokeOut(com, method, object[] args, bool[] byRef)` | A1 P13, A3 §1.3 | `GetCustomBendAllowance()` (base and swept flange: missing from the 2019 interop, present in 2023; convert solid: 2025; static since 2019 on the other sheet-metal data interfaces), corner relief `GetFaces`/`AddNewCorner`/`GetCornerAtIndex` |
| `FaceOrEdgeRef(object)` | A1 P11 = A2 `FlangeRef` | Face → `ToFace`, Edge → `ToEdge` (a straight edge stays an edge); anything else → null with a note. Used for every sheet-metal reference that `ToGeometryRef` would turn into an axis. |
| Scalar readers `Read<Kind>Scalars(data, record)` | A2 P11 | The same code fills the record at capture and the read-back at build time |
| `SwSheetMetal.ReadBends(feature) → List<(Feature sub, BendValues values)>` | A2 P5, A3 P6, P8 | Sub-features whose definition `is OneBendFeatureData`, each under its own `AccessSelections`; counting by definition, not by type name (A2 P8) |
| `CaptureSupportFace(sketch)` | A2 P6, A3 P7 (K7) | `RollBack(AfterFeature, sketch)` → `ISketch.GetReferenceEntity(out _) as Face2` → `FaceSignature`; stored as `SketchSupportFaces[sketchName]`. For absorbed sketches this is the state just before the owner, so A2's and A3's states coincide. |
| `CaptureWitnesses(f, fixedFace)` | A3 P2, A2 P6 (K10) | `RollBack(AfterFeature, f)`, ≤ 12 points (sub-feature faces first, then faces turned > 1° from the fixed face), `finally` End. Used by sketched bend **and** jog; the jog's fixed point is one of them. |
| Body histories | A4 P2 | `SwDoc.BodyHistories(doc)`: `GetFeatures()` of every solid body at the end state, ordered by creation order. Feeds §1.5 step 2 from S1a and the planner from S7a. |
| Flat-pattern sub-features | A3 P3 | `FeatureCollector` lists the non-sketch sub-features of every FlatPattern (top level and folder). `SheetMetalTree` makes the flat pattern's own corner trim and break corner (`GetAddCornerTrim`/`GetBreakCorners`) `PartOf`; every other `CornerTrim` becomes Unsupported (non-solid, „Ecke trimmen (abgewickelt): …“). |
| Mirror features with sheet-metal seeds | K23 | In `CaptureMirrorPattern`/`CaptureMirrorSolid`, any seed with `IsSheetMetal(type)` gives `NotSupported(„Spiegel-Feature über Blech-Features wird noch nicht nachgebaut.“)`. The record is Unsupported, changes the solid and triggers the fallback. |

### 1.7 Builder support (add-in)

| Item | Origin | Content |
|---|---|---|
| `Sw/SwSheetMetal.cs` | A1 §1, A2 §1, A3 §1.5, A4 §1 | <ul><li>parameters and allowance read/write (§1.4);</li><li>`Snapshot(doc)` (top-level names plus `GetSheetMetalFolder`/`GetFlatPatternFolder` contents, plus body count);</li><li>`NewSince(doc, snap)`, `RemoveNew(doc, made)` (absorbed, two rounds, checked gone);</li><li>`LastAddedOf(doc, snap, types, isData)` built on `NewSince`, not on `GetLastFeatureAdded` (K6);</li><li>`ReadBends`;</li><li>`InsertBends` wrapper (S6, reused by fallback route B).</li></ul> |
| `Sw/SheetMetalSession.cs` | A1 §3 | Folds every unsuppressed FlatPattern (and, from S6, every suppressed ProcessBends) before collecting; restores in a `finally`; gives the copies their recorded state. Refuses the run if folding fails. |
| `Sw/SheetMetalGuard.cs` | A1 §3 | §1.4. Aborts through `AbortRunException`. |
| `BuildContext` additions | A1 §7, A3 §1.7 | <ul><li>`SheetMetalBodies: Dictionary<string, SheetMetalBody>` (original owner name → `{ Feature SheetMetal; Feature? FlatPattern; int SigmaBase; int? SigmaSheet }`, K11);</li><li>`Guard`;</li><li>`SketchSupportFaces: Dictionary<string, FaceSignature>`;</li><li>`Bends: Dictionary<string, Feature>` (original bend name → target bend; `Forget` also drops a forgotten owner's bends).</li></ul> Session and guard belong to the job (`BuildShared`); `SketchSupportFaces` and `Bends` belong to each context, like `Sketches`. |
| `BuildResult` additions | A1 §7, A4 §4.3 | <ul><li>`Companions: List<(Feature, string originalName, List<NamedDimension>)>`, included in `AllFeatures` so `CheckRebuildError` covers them (K22);</li><li>`RemoveHelpers` set by sheet-metal builders;</li><li>`Covered(message)` (Warning, `HasEquivalent`, no feature).</li></ul> |
| `StepRunner` hooks | A1 §7 | <ul><li>guard snapshot before `Build`, check after `Verify` and after `RemoveAgain`;</li><li>`RestoreDimensionNames` per companion;</li><li>`RemoveLeftovers` with the folder-aware snapshot (template folders are system types);</li><li>`RemoveAgain` per §1.3.</li></ul> |
| `BuilderSupport.SelectFaceByRay(ctx, face, p, mark, append)` | A3 P1, A2 P6 (K8) | Ray from p + 1e-5·n along −n, radius 1e-4, `swSelFACES`. Accepted only if the selected object is the wanted face (own body in separate-body mode) and `GetSelectionPoint2` is within `Tol.Length` of p. **Never** a selection without a point. `SelectFaceAt` stays for holes and for opening a sketch on a face. |
| Core `Planning/BendSide.cs` | A3 §3, A2 §10 (K8, K9) | <ul><li>`RegionOf`;</li><li>`SideCandidates` (one point per region, the farthest from every line, clearance ≥ max(2t, 1 mm));</li><li>`PointBeside` (the single-line case, used by the jog);</li><li>`FixedPointSpace.Choose(preferred, asSketch, dSketch, asModel, dModel, tol)`.</li></ul> |
| `Sw/SwShape.Misses(ctx, points, ownOnly)` | A3 P4 | Kernel distance of witness points to the faces of own bodies, or of all bodies |
| `SketchBuilder` face support and `ReplaceInto` | A3 P7, A2 §2 | `Build` opens a sketch on `ctx.SketchSupportFaces[name]` when that face is found, planar, coplanar with R(sketch plane) and on an own body, and reports whether it did. The consumer decides whether a plane is acceptable: a sketched bend proceeds with a note, a jog fails (B36). `ReplaceInto` clears and rewrites a native sketch without `_everyPointCounts` (A2 P15). A form tool's position sketch and a vent's substitute-cut sketch use the same face support (K7). |
| `FeatureEdit` | all | <ul><li>per-type geometric edits;</li><li>a generic `Allowance(doc, f, BendAllowanceData)` (late-bound `UseDefaultBendAllowance` + `SetCustomBendAllowance`, write order of §1.4);</li><li>`Late(doc, f, data, change)` for corner relief.</li></ul> Every edit is followed by a read-back. |
| `MassCapture` | A1 §6, A2 §7, A3 P4 | <ul><li>"after" = `MeasureUntil`;</li><li>`SampleSubFeatures` (or < 2 own faces) also samples non-sketch sub-features;</li><li>`FlatChange` for a `FlatPatternRecord` (bar at the end, one flat pattern at a time from the folded state).</li></ul> |

### 1.8 Run flow after all slices (`MirrorJob.Run`)

1. Start checks (open sketch, rollback bar).
2. `SheetMetalSession.Fold` when the source has SheetMetal, template or FlatPattern features. On failure the run is refused.
3. Collect: `SheetMetalTree` closure, folder contents, flat-pattern sub-features, skip reasons (A1 §2).
4. **Refusal step 1** (§1.5).
5. Capture through `CaptureAll`: safe dispatch, support faces, witnesses, bends; owner fix-up by `BringsParts`; `MarkOtherBodies`; `CaptureDimensions` (+ companion dimensions).
6. Body histories → `SheetMetalPlanner.Plan` (S7a) → **refusal step 2**. Throw before `CreatePart`.
7. Snapshot pass (S7a, independent of "Jeden Schritt prüfen"), including the original's `FlatChange` for final-state snapshots (K28).
8. `MassCapture` with "Jeden Schritt prüfen": changes, samples, `FlatChange`, per-feature masters (S7b, cap 64).
9. `RollToEnd`, then create the target and `BuildContext`.
10. Build loop with `StepRunner.BuildOne` (guard hooks); S7b restart.
11. Finish: session restore and copy states in a `finally`; equations (companions known); `DeleteOriginals` refused when sheet-metal records were built (A1 P8); snapshot masters released.

---

## 2. Conflicts between the corrected designs, and their resolution

| # | Conflict | Resolution | Slice |
|---|---|---|---|
| K1 | **Four refusal models.** <ul><li>A1 P4: `SupportedSheetMetal` per type.</li><li>A2 §1.2: `Rebuildable` + `FallbackCovers(type)`.</li><li>A3 P11: `RefusesRun(type, fallbackApplies)`.</li><li>A4 P3: two steps, before capture and after planning.</li></ul> The fallback works per body, not per type, so `FallbackCovers(type)` and `fallbackApplies` cannot be evaluated before capture. | Two-step model of §1.5, with A1's per-type growth expressed as `SheetMetalType.State`. A3's `fallbackApplies` becomes the post-planning step. | S0 (inert), S1a (active) |
| K2 | **Leaves: refuse or fail?** A1 P4 says edge flanges must keep refusing until Area 2 lands. A4 P3 says a leaf should be an ordinary Failed entry. | Both hold at different times. A leaf type is refused **until its slice decides it** (`NotYet`); after that, a failure is an ordinary entry or a fallback trigger. Unconfirmed names are never refused by name (§1.5). | all |
| K3 | **Ownership API.** <ul><li>A1: `BringsParts`.</li><li>A2: `BringsParts => OwnedSketches.Count > 0` + `OwnedSketches`, replacing `IOwnsSketches`.</li><li>A3: an `IOwnsParts` marker.</li><li>A4: `PositionedFeatureRecord`.</li></ul> A2's `RemoveAgain` deletes with `withAbsorbed: record.BringsParts`. A1 P2 deletes "FlatPattern, SheetMetal, flange, all with `swDelete_Absorbed`". **Both would delete the base flange's profile sketch**, which is an ordinary `SketchRecord` registered in `ctx.Sketches` and may be shared. | One API (§1.3). The absorbed delete applies only when `OwnedSketches.Count > 0`. The base flange is deleted with option 0 (its profile sketch stays, as `ExtrudeBuilder` does). Companions: FlatPattern with absorbed (its own sub-sketches), SheetMetal with option 0. `PositionedFeatureRecord` stays as the base of hole and form tool. | S0, S1a |
| K4 | **Three sets of allowance, relief and parameter classes** (A1 P17, A2 P3, A3 P14, A4 P14) | Area 1's types only (§1.2) | S0 |
| K5 | **Per-bend overrides in three shapes:** A2 `BendOverride(Bend, What)`, A3 `BendValues` + `BendOverrides.Differences → strings`, A4 `List<string>` | `BendValues` (data, with relief added) → `BendOverrides.Differences` → `List<BendOverride>`. Area 4 uses the bend names. One reader, `SwSheetMetal.ReadBends`. | S0, S2 |
| K6 | **New-feature detection in four ways:** <ul><li>A1 `NewSince` (folder-aware);</li><li>A2 top-level name sets;</li><li>A3 `LastAddedOf` on `GetLastFeatureAdded`;</li><li>draft L8 / A2 L18 / A3 L15 doubt `GetLastFeatureAdded`.</li></ul> | `NewSince` everywhere. `LastAddedOf` filters `NewSince` by type name **and** data interface (UnFold and Fold share one interface, A3 P14). `GetLastFeatureAdded` is only a cross-check (B10). | S1a |
| K7 | **Sketch on a face.** <ul><li>A2 §1.4 uses Area 3's *draft* mechanism (`SketchRecord.SupportFace` + `FaceSupportedSketches`, bar after the sketch).</li><li>A3 P7 replaced it with `SketchedBendRecord.SketchFace` read under the bend's `AccessSelections` + `ctx.SketchSupportFaces`.</li><li>A4 opens its own sketch on the face for form tools and vents.</li></ul> | One dictionary `ctx.SketchSupportFaces`, filled at capture by `CaptureSupportFace` (forced rollback after the sketch, which equals "before the owner" for absorbed sketches). One `SketchBuilder` face path. The consumer decides whether a plane is acceptable. | S4 (S9 reuses it) |
| K8 | **Side-preserving face pick.** A2 P6 (`PointBeside` + `SelectByRay` + `IsSameFace`) vs A3 P1 (`SelectFaceByRay` + `SideCandidates`). A4 uses `SelectFaceAt`. | One `SelectFaceByRay` and one `BendSide`. The jog is the single-line case. Clearance: the farthest point per region, ≥ max(2t, 1 mm); A2's "a quarter of the face" becomes a preference. `SelectFaceAt` only where the side does not matter. | S4 |
| K9 | **Fixed-point space, opposite preference orders.** A2 `FixedPointInModel` tries the model reading first; A3 `ChooseFixedPoint` tries the documented sketch reading first. | `FixedPointSpace.Choose(preferred, …)`. Sketched bend: sketch first (documented). Jog: model first (undocumented, A2's evidence). If both readings lie on the face in **different** regions, the result is ambiguous: the sketched bend takes the sketch reading plus a note; the jog gets `Unknown` (its "feste Seite" alternative is allowed). | S4 |
| K10 | **Fixed-side verification.** The A2 jog probe uses one point; A3 uses ≤ 12 witnesses captured with a rollback. | One mechanism: `CaptureWitnesses` + `SwShape.Misses` for both. The jog's fixed point is a witness. This costs one rollback per jog; the gain is that a wrong half is caught (CADapter's silent 90° case, B43). | S4 |
| K11 | **σ_sheet.** A3 §1.1 expects Area 1 to expose `SheetMetalBodyMap.Sigma`; A1's corrected `SheetMetalBodies` has no such field. | `SheetMetalBody.SigmaBase` (the base flange's sketch σ) is always set. `SigmaSheet` stays null until B39 settles the convention, then becomes a formula in `SheetMetalRules`. The witnesses decide the direction anyway; σ_sheet only orders the first guess. | S1a (field), S4 (use) |
| K12 | **Corner trims.** A3 P3 found they are never collected. A1's `FlatPatternRecord` carries `CornerTrimRelief*` for the flat pattern's own option. | No conflict once separated: A1 keeps the flat pattern's own flags. User corner trims are collected as flat-pattern sub-features and become Unsupported (non-solid, no trigger, blocks deletion). This is decided in **S1a**, because a flat pattern can carry them from the first slice on. | S1a |
| K13 | **Capture exception handling** written four times (A1 P13, A2 P10, A3 P9, A4 P15) | One safe dispatch (§1.6) | S0 |
| K14 | **Read-back.** A2 P11 forbids reusing the capture helpers; A3 does not say. | Scalar readers for all (§0) | S0 rule |
| K15 | **Allowance writes.** A1 writes at creation; A2 uses a separate edit; A3 passes PCBA only and reads back after every edit; A4 uses the A1 path, then `InsertBends2`. | One policy (§1.4) with `AllowanceRules` | S0, S2 |
| K16 | **Sub-feature face sampling** specified twice (A2 §7, A3 P4) | `FeatureRecord.SampleSubFeatures` + the < 2 faces rule, in `MassCapture.SampleFaces` | S2 |
| K17 | **Fallback triggers.** A4: any unsupported solid-changing record on a qualifying body. A3: the welded corner must not trigger; corner trim is non-solid. | Trigger = `UnsupportedRecord` with `ChangesSolid && TriggersFallback`. Until its type name is confirmed (B44), a welded corner is a generic record and **does** trigger; this is accepted and noted in STOLPERSTEINE. | S7a |
| K18 | **Lofted bend.** A3: refused unless the fallback covers it. A4: carrier types refused before capture with the switch off. | Covered by K1: role Carrier, state Unsupported (from S7). Refused before capture with the switch off; after planning when uncovered. | S7a |
| K19 | **Swept flange.** A2 P4: do not depend on the type name. A4 P3 lists `SweptFlange` (before 2021) among the carrier types. | Positional rule only: as the first feature of a body it is a carrier (step 2); otherwise a leaf. On 2019–2020 its definition is unknown, so it is Unsupported and a trigger. Name added after B72. | S1a (rule) |
| K20 | **Restart** (A4 P8) vs the mirror branch's `BuildShared` | A restart creates a fresh `BuildShared`, `BuildContext` and report. `RunProgress.Start` again. Job snapshot S0 is taken before the first step. | S7b |
| K21 | **Guard abort** (A1 §3: `report.Aborted`) vs the mirror branch's abort | `throw new AbortRunException(text)`: `StepRunner` removes the step and sets `AbortReason`, and both build loops stop. | S1a |
| K22 | **Companions** (A1) vs **group members** (A4 §4.3, mirror branch `BuildResult.Group`) | Companions are part of `AllFeatures` (rebuild-error check, own faces) but are not group members (no logical-name registration). Their original names are registered through `Companions`. | S1a |
| K23 | **Mirror features over sheet-metal seeds.** The mirror branch captures the copies in the target (`ofOwnCopy`); sheet-metal capture rolls back (support faces, witnesses, profiles) and needs the session and guard. | Unsupported with a reason until S10's dedicated slice (§1.6). A MirrorSolid of a sheet-metal body is a new body's first feature, so the positional rule refuses it (or the fallback covers it). | S0 |
| K24 | **Flat pattern after a fallback.** A1's `FlatPatternBuilder` looks up `SheetMetalBodies[r.Owner]` (the base flange). In the fallback the owner is covered. | `SheetMetalFallbackBuilder` fills `SheetMetalBodies[owner]` for every covered owner of the body with the convert's SheetMetal and FlatPattern (A4 P14). With route B (Insert Bends) the FlatPattern record is covered too. | S7a |
| K25 | **Bend lookup for Area 3.** A2 §1.5: "bends are found by `IOneBendFeatureData` definitions in sub-feature order". A3 P8: ordinal only when the owner has exactly one bend, because a reflection reverses in-plane order. | A3 P8 wins. `ctx.Bends` is filled by every successful `FindBend` and by owner builders only for single-bend owners. Area 2 builders do not fill it by order. | S5 |
| K26 | **Convert fixed face.** A4 §4.2 takes the planar face on the plane of the flat pattern's `FixedFace2`. A1 P11 allows a fixed **edge** (cylindrical case). | Edge → the planar face of the snapshot adjacent to R(edge) with the larger area, otherwise A4's chain (`SheetMetal.FixedReference`, largest planar face), each with a note | S7a |
| K27 | **„Original löschen“.** A3 P3 (corner trim), A4 (covered entries) and A2 P9 (hem relief note) each block it; A1 P8 refuses it outright for sheet metal. | A1 P8 holds until the S10 item "delete original with measurement" (a new live test, §3.13). The finer rules (A2 P9 note only when unverified, A3/A4 blocks) are implemented anyway, because they matter once A1 P8 is lifted. | S1b |
| K28 | **A silently wrong fallback allowance with "Jeden Schritt prüfen" off.** A4 P1 relies on the flat-state check to catch an allowance that the convert did not take. A1 §6/§9 measures the original's `FlatChange` only in `MassCapture`, that is only with "Jeden Schritt prüfen" (A1 P16: otherwise „nicht geprüft“). So A4's P1 failure stays silent exactly when the user did not ask for checks. | The snapshot pass (always on, §1.8 step 7) also measures the original's `FlatChange` for every body whose snapshot is its final state. `SheetMetalFallbackBuilder` compares the convert's flat change with it locally. A1's flat check for parametric flat patterns stays bound to "Jeden Schritt prüfen": there the allowance is set at creation and read back. | S7a |

Smaller consistency points, no decision needed:
- **Gauge table:** first body of a new part only (A1 §11 = A4 §4.4).
- **σ = +1 on a face:** A2 §6 = A3 §6.
- **Same part, selected features:** references may lie on the original body (A2 §8, A3 §8). The guard then only protects the shared defaults.

---

## 3. Implementation order

### 3.0 Overview

| Slice | Content | Needs | Opens (state → Rebuilt unless noted) | Days |
|---|---|---|---|---|
| S0 | Foundation, inert | merged branches | — | 2–3 |
| S1a | Base flange/tab, SheetMetal, flat pattern, session, guard (new part, assembly), flat check, cut `LinkToThickness`, positional carrier refusal | S0 | `SMBaseFlange`, `SheetMetal` (owned), `FlatPattern`, template folders (system), `CornerTrim` (Unsupported) | 5–6 |
| S1b | Same part: guard snapshot mode, tab scope (path C first), restore both states, deletion refusal | S1a | the same types in the same part | 2–3 |
| S2 | Hem, miter flange, bend infrastructure | S1b | `Hem`, `SMMiteredFlange` | 2–3 |
| S3 | Edge flange | S2 | `EdgeFlange` | 3–4 |
| S4 | Sketched bend and jog: sketch on face, ray pick, side candidates, witnesses | S2 (bend infrastructure, allowance edit); independent of S3 | `SM3dBend`, `Jog` | 4–5 |
| S5 | Closed corner, break corner, unfold/fold, cross break, corner relief | S3, S4 | `CornerFeat`, `BreakCorner`, `UnFold`, `Fold`, `CrossBreak`, `CornerRelief` (2022+; Unsupported trigger on 2019–2021) | 4–5 |
| S6 | Normal Cut feature, legacy Insert Bends | S1b | `NormalCut`, `FlattenBends`, `ProcessBends`, `SheetMetal` (legacy owner) | 2 |
| S7a | Fallback: planner, snapshots, import, convert or Insert Bends, allowance, capture-time triggers; switch off by default | S6, owner decision, B54/B55 | `FormToolInstance`, `SMGusset`, `Rip` → Unsupported (leaf); `LoftedBend`, `SolidToSheetMetal`, `ToroidalBend`, unowned `SheetMetal` → Unsupported (carrier) | 6–7 |
| S7b | Fallback restart after a build-time failure | S7a, B63 | — | 2–3 |
| S8 | Gusset, tab and slot | S1b (in practice S3) | `SMGusset` → Rebuilt; tab and slot by its confirmed name | 2–3 |
| S9 | Forming tools | S4 (sketch on face) | `FormToolInstance` → Rebuilt | 3–4 |
| S10 | Optional: vent substitute cut, swept flange (2021+), lofted bend, mirror features over sheet metal, delete original, convert builder (2025 SP2), `MirrorTool` | per item | per item | — |

S3 and S4 can run in parallel lanes after S2. S6 can run at any time after S1b. If S7 is dropped (B54/B55 fail, or the owner says no), **S7′** (0.5 day) sets `FormToolInstance`, `SMGusset` and `Rip` to Unsupported leaves; carriers stay refused.

### 3.1 S0 – Foundation (inert)

- **What:** §1.2 value types and Core rules, §1.3 record API, §1.5 type table and the two-step API with every state `NotYet`, the safe dispatch, `SwLate.Invoke`/`InvokeOut`, `NonSolidTypes` moved to Core, mirror features over sheet-metal seeds made Unsupported. No sheet-metal feature is built yet.
- **Files:**
  - Core:
    - `Records/SheetMetalRecords.cs` (value types only);
    - `Records/FeatureRecords.cs` (virtuals, `TriggersFallback`);
    - `Records/HoleRecords.cs` (`PositionedFeatureRecord`);
    - `Planning/SheetMetalTypes.cs`, `Planning/UnsupportedFeatures.cs`;
    - `Planning/SheetMetalCompare.cs`, `Planning/AllowanceRules.cs`, `Planning/BendOverrides.cs`.
  - Add-in:
    - `Capture/FeatureCapture.cs` (`CaptureAll` by `BringsParts`, safe dispatch, `ChangesSolid` via Core);
    - `Capture/FeatureCapture.Holes.cs` (`OwnedPart`, `CaptureOnItsOwn` by owner kind);
    - `Capture/FeatureCapture.SheetMetal.cs` (empty dispatch);
    - `Capture/FeatureCapture.Mirror.cs`;
    - `Build/StepRunner.cs` (`RemoveAgain`);
    - `Build/MirrorJob.cs` and `Build/Builders/MirrorFeatureBuilder.cs` (Esc loops, `RefusesBeforeCapture`);
    - `Sw/SwLate.cs`.
- **Core tests:**
  - `SheetMetalSharedTests`:
    - `Fold_maps_2pi_minus_theta_to_theta` (A2 #3);
    - `Differences_list_only_deviating_values` (A2 #15, A1 #10);
    - `Allowance_rules_refuse_tables_on_jogs_only` (A2 #14);
    - `Bend_overrides_name_bends_that_differ_from_the_parent` (A3 #11).
  - `OwnershipTests`:
    - hole, form-tool stub, base-flange stub and edge-flange stub report `BringsParts`/`OwnedSketches` (A2 #17);
    - the absorbed-delete rule is false for a base flange (K3).
  - `SheetMetalRefusalTests`:
    - every type of the old set is still refused with all states `NotYet` (regression);
    - `MirrorStock` is always refused;
    - `partOfAcceptedOwner` exempts;
    - `ChangesSolid("CrossBreak")` is false.
  - `ReportTextTests`: unchanged assertions pass.
- **Stays refused:** every sheet-metal type, exactly as today.
- **Live:** none (a regression run on non-sheet-metal sample parts).

### 3.2 S1a – Base flange and flat pattern, new part and assembly

- **What:** A1 (2) §2–§10 for base flange/tab, SheetMetal, flat pattern and the cut's `LinkToThickness`, **minus the same part**. In addition:
  - `SheetMetalTree` with flat-pattern sub-features and corner trims (K12);
  - `SwDoc.SystemTypes` += template types;
  - `FeaturesInCreationOrder` lists folder contents;
  - collector closure and skip reasons;
  - session (fold/restore; the new part gets the recorded state);
  - guard in new-part mode (multibody: from the second base flange on);
  - companions, `RemoveHelpers` (K3), `NewSince`;
  - the flat check with "Jeden Schritt prüfen";
  - body histories and **refusal step 2 without plans** (K19);
  - `SheetMetalBody.SigmaBase` (K11).
- **Files:**
  - Core:
    - `Records/SheetMetalRecords.cs` (+ `FlangeSide`, `BaseFlangeRecord`, `FlatPatternRecord`);
    - `Records/FeatureRecords.cs` (`ExtrudeRecord.LinkToThickness`);
    - `Planning/SheetMetalRules.cs` (`BaseFlangeFlags`, `InPlaneSideFlag`, `FromFlangeOffset`/`ToFlangeOffset`, `CreationValues`, `ExpectedBodyDelta`, `CreationPathOrder`);
    - `Planning/SheetMetalTree.cs`;
    - `Planning/SheetMetalPlanner.cs` (`BodyHistory`, `Refusal`).
  - Add-in:
    - `Sw/SwSheetMetal.cs`, `Sw/SheetMetalSession.cs`, `Sw/SheetMetalGuard.cs`;
    - `Sw/SwDoc.cs`;
    - `Capture/FeatureCollector.cs`;
    - `Capture/FeatureCapture.SheetMetal.cs` (base flange, flat pattern, `FaceOrEdgeRef`);
    - `Capture/MassCapture.cs`;
    - `Build/BuildContext.cs`;
    - `Build/Builders/IFeatureBuilder.cs` (`Companions`, `FeatureEdit.BaseFlange/FlatPattern`);
    - `Build/Builders/BaseFlangeBuilder.cs`, `Build/Builders/FlatPatternBuilder.cs`;
    - `Build/Builders/ExtrudeBuilder.cs`/`ProfileSupport.cs` (second edit);
    - `Build/StepRunner.cs` (guard hooks, companion names);
    - `Build/MirrorJob.cs` (session, both refusal steps, the same-part refusal).
- **Core tests** (`SheetMetalTests`, `SheetMetalTreeTests`):
  - A1 §13 #1–#8, #11 (flat-check sensitivity on the analytic L sheet, K 0.5/0.44), #12 (tree; plus `User_corner_trim_is_unsupported_and_flat_patterns_own_corner_trim_is_part_of_it`), #13; #10 is already in S0, and #14 (grain direction) is kept for a later API;
  - `Creation_path_order_puts_C_first_only_for_a_tab_in_separate_body_mode`;
  - refusal: the S1a states, `Unowned_SheetMetal_is_a_carrier`, `Unsupported_first_feature_of_a_sheet_metal_body_refuses_without_a_plan`, `Leaf_of_unknown_type_does_not_refuse`;
  - `SwConstTests`: `swFeatureNameID_e` 30/31/32/34/38/115, `swFlangeOffsetTypes_e` (6 only in 32.1).
- **Stays refused:**
  - before capture: all leaves except FlatPattern and CornerTrim;
  - the carriers `FlattenBends`, `ProcessBends`, `SolidToSheetMetal`, `LoftedBend`, `ToroidalBend`;
  - **every same-part run with sheet metal**;
  - after planning: unsupported carriers (no fallback yet).
- **Live smoke:**
  - a plate 100 × 50 × 2 with holes, a U-channel from an open profile (K 0.44), and a 2-body part with different thickness;
  - each mirrored at *rechts* and *vorne*, with "Jeden Schritt prüfen" on and off;
  - B1–B22, B50.

### 3.3 S1b – Same part

- **What:** the A1 guard in snapshot mode; tabs with scope on own bodies (path C first); the first mirrored body never merges; the session restores both originals and copies; `DeleteOriginals` refused (A1 P8, K27); the equation note. Lift the same-part refusal.
- **Files:** `Sw/SheetMetalGuard.cs`, `Build/Builders/BaseFlangeBuilder.cs`, `Build/MirrorJob.cs` (`DeleteOriginals`, refusal), `Planning/SheetMetalRules.cs` (`GuardExpectation`), `Planning/UnsupportedFeatures.cs`.
- **Core tests:**
  - `Guard_expects_the_snapshot_in_the_same_part_and_defaults_or_body_in_a_new_part`;
  - `Same_part_offers_no_ReverseThickness_toggle_for_a_closed_profile` (A1 P1);
  - `Delete_original_refused_when_sheet_metal_records_were_built` (Core helper `SheetMetalRules.DeleteRefusal(records)`);
  - refusal with the S1b states.
- **Stays refused:** as in S1a, without the same-part rule.
- **Live smoke:**
  - same part, whole part, plate with a tab, original flattened;
  - check that template and original are unchanged and that both states are restored;
  - B14, B11, B19.

### 3.4 S2 – Hem, miter flange, bend infrastructure

- **What:**
  - A2 phase 2a with P7 (split edits), P8 (miter variants and edge-count rejection), P9 (hem relief note only when unverified; all width sketches paired);
  - `ReadBends`/`BendOverrides` (P5, K5);
  - `SampleSubFeatures` (K16), `SketchBuilder.ReplaceInto`;
  - scalar readers for read-back (P11);
  - `FeatureEdit.Allowance` (K15);
  - own-body checks for every reference (A2 P12).
- **Files:**
  - Core: `Records/SheetMetalFlangeRecords.cs` (`HemRecord`, `MiterFlangeRecord`), `Planning/FlangeProfiles.cs` (`Assign`, `IsFullEdgeLine`), `Planning/MiterRules.cs` (`Variants(edgeCount)`).
  - Add-in:
    - `Capture/FeatureCapture.SheetMetalFlanges.cs`;
    - `Build/Builders/HemBuilder.cs`, `Build/Builders/MiterFlangeBuilder.cs`;
    - `Build/SketchBuilder.cs`;
    - `Capture/MassCapture.cs`;
    - `Sw/SwSheetMetal.cs`;
    - `Build/Builders/IFeatureBuilder.cs`.
- **Core tests:**
  - A2 #10 (`Hem_width_sketch_spanning_the_whole_edge_is_default`, `Two_width_sketches_pair_with_two_edges`), #13 (`Miter_swap_alternative_only_for_unequal_offsets`), #15 (`Bend_overrides_become_notes`), #16;
  - `Miter_mark0_retry_only_for_single_edge_chains` (P8);
  - `Hem_relief_note_only_when_unverified` (P9).
- **Stays refused:** EdgeFlange, Jog, SM3dBend, the corners and folds, NormalCut, legacy, Area 4 types, carriers.
- **Live smoke:** hem on a plate edge (closed, open, teardrop), miter flange over 1 and 3 edges; B23–B27.

### 3.5 S3 – Edge flange

- **What:**
  - A2 phase 2b with P1 (ordered angle candidates, one pair for all edges, `BendAngle` = the chosen angle, `Fold` read-back);
  - P2 (final-state read plus reference read; three-part profile check);
  - P13 (top-level parent sketch rule);
  - P15 (array ladder, `out int`, casts);
  - P16 (arc and circle centres);
  - paths A/B/C with deletion between attempts; the separate `ReverseOffset` edit; the allowance edit.
- **Files:** `Records/SheetMetalFlangeRecords.cs` (`FlangeProfile`, `EdgeFlangeRecord`), `Planning/FlangeProfiles.cs` (`AngleCandidates`, `Fold`, `FitsFrame`, `Rebase`, `NormalOffset`, `InPlaneDeviation`, `CommonCandidate`, `Direction`), `Capture/FeatureCapture.SheetMetalFlanges.cs`, `Build/Builders/EdgeFlangeBuilder.cs`.
- **Core tests:** A2 #1, #2, #4–#9 (conjugated angle, both hinge directions, common candidate, direction, frame fit, rebase/normal-offset split, deviation with arcs, `Assign_*`).
- **Stays refused:** Jog, SM3dBend, corners and folds, NormalCut, legacy, Area 4, carriers.
- **Live smoke:**
  - a box from a base flange plus 4 edge flanges at 90°, and one flange at 60° on a reversed edge;
  - a flange with a position offset and one with a custom K;
  - B28–B35.

### 3.6 S4 – Sketched bend and jog

- **What:**
  - **Shared:**
    - `CaptureSupportFace` + `ctx.SketchSupportFaces` + `SketchBuilder` face path (K7);
    - `SelectFaceByRay`, `BendSide` (K8, K9);
    - `CaptureWitnesses` + `SwShape.Misses` (K10).
  - **Sketched bend:** A3 P1, P2, P5, P6, P7 and P12 (own-body pre-checks).
  - **Jog:** A2 P6 and P7, with a witness probe.
- **Files:**
  - Core:
    - `Records/SheetMetalBendRecords.cs` (`SketchedBendRecord`), `Records/SheetMetalFlangeRecords.cs` (`JogRecord`);
    - `Planning/BendSide.cs`;
    - `Planning/SheetMetalBendRules.cs` (`BendPosForCreation`, `FirstReverseGuess`, `CompareVariants`);
    - `Planning/JogRules.cs` (DimPos/BendPos encodings).
  - Add-in:
    - `Capture/FeatureCapture.SheetMetalBends.cs` (sketched bend), the jog part of `FeatureCapture.SheetMetalFlanges.cs`;
    - `Capture/FeatureCapture.cs` (`CaptureSupportFace`, `CaptureWitnesses`);
    - `Build/Builders/BuilderSupport.cs`, `Sw/SwShape.cs`, `Build/SketchBuilder.cs`;
    - `Build/Builders/SketchedBendBuilder.cs`, `Build/Builders/JogBuilder.cs`.
- **Core tests:**
  - A3 #1–#6, #14;
  - A2 #11 and #12 merged into `BendSideTests`: `Fixed_point_space_prefers_the_documented_reading_and_reports_ambiguity` (sketched-bend and jog variants), `PointBeside_is_the_single_line_case_of_SideCandidates`, `Side_sign_is_not_invariant_but_the_mirrored_point_is_on_its_side`.
- **Stays refused:** corners and folds, NormalCut, legacy, Area 4, carriers.
- **Live smoke:** A3's "first live test" (100 × 50 × 2, bend at x = 50, K 0.3, mirrored at *rechts* and *vorne*, "Jeden Schritt prüfen" off), a U-channel from two lines, and a jog on a plate; B36–B43.

### 3.7 S5 – Corners, unfold/fold, cross break, corner relief

- **What:**
  - A3 (2) §5.2–§5.6 with P8 (`ctx.Bends`, the ordinal only for single-bend owners, K25), P10 (cross-break lines), P11 (reasons; welded corner `TriggersFallback` = false once named), P12 and P13;
  - `BendMatcher`;
  - `FeatureEdit.ClosedCorner/BreakCorner/Folds/CrossBreak/Late`.
- **Files:**
  - Core: `Records/SheetMetalBendRecords.cs` (`BendRef`, `CrossBreakRecord`, `ClosedCornerRecord`, `BreakCornerRecord`, `CornerReliefItem`, `CornerReliefRecord`, `FoldRecord`), `Matching/BendMatcher.cs`, `Planning/SheetMetalBendRules.cs` (`SwapOverlap`, `LegacyReliefLengths`), `Planning/SheetMetalTypes.cs`.
  - Add-in: capture partials, `ClosedCornerBuilder`, `BreakCornerBuilder`, `FoldBuilder`, `CrossBreakBuilder`, `CornerReliefBuilder`, `Build/BuildContext.cs` (`Bends`).
- **Core tests:** A3 #7–#10, #12, #13 (adjusted to the type table), #15.
- **Stays refused:** NormalCut, legacy, Area 4, carriers. On 2019–2021 corner relief is Unsupported (Failed, or a trigger from S7a).
- **Live smoke:** a box with 4 edge flanges plus closed corners (butt/overlap), break corners, Unfold → cut across a bend → Fold, a cross break, and a corner relief on 2024; B44–B49.

### 3.8 S6 – Normal Cut and legacy Insert Bends

- **What:** A1 `NormalCutBuilder` (data object, `AddSMNormalCut*` fallback); `LegacyBendsBuilder` (phase 1b) with the guard, `NewSince` and `RemoveHelpers`; the session unsuppresses ProcessBends; the `SwSheetMetal.InsertBends` wrapper that fallback route B reuses.
- **Files:** `Records/SheetMetalRecords.cs` (`NormalCutRecord`, `LegacyBendsRecord`), `Planning/SheetMetalRules.cs` (`LegacyArguments`), `Planning/SheetMetalTree.cs` (1b owners), `Build/Builders/NormalCutBuilder.cs`, `Build/Builders/LegacyBendsBuilder.cs`, `Sw/SheetMetalSession.cs`, `Sw/SwSheetMetal.cs`.
- **Core tests:**
  - A1 #9 (`Legacy_insert_bends_arguments_follow_the_allowance_kind`, `Offset_ratio_outside_0_05_to_2_is_rejected`);
  - #12 (1b) `Flatten_and_process_bends_belong_to_the_legacy_sheet_metal`;
  - `Normal_cut_groups_mirror_face_by_face`.
- **Stays refused:** Area 4 types, `SolidToSheetMetal`, `LoftedBend`, `ToroidalBend`, `Rip`.
- **Live smoke:** a shelled box → Insert Bends (K 0.44) and a Normal Cut; B51, B52.

### 3.9 S7a – Fallback, capture-time

See §4 for its place. The content is A4 (2) §2–§4 without the restart:
- the planner on full histories (P2);
- `MirrorOptions.SheetMetalFallback` (default false) with its page checkbox, passed through the assembly path (P6);
- the snapshot pass (P7), including the original's flat change for final-state snapshots (K28);
- import, then attempts A1/A2/B1/B2/F3 with local checks and cleanup (P4, P5);
- the allowance policy (P1);
- companions and `SheetMetalBodies` (K24);
- `BuildResult.Covered`;
- the report.

The type table flips `FormToolInstance`/`SMGusset`/`Rip` to Unsupported leaves (triggers) and `LoftedBend`/`SolidToSheetMetal`/`ToroidalBend`/unowned `SheetMetal` to Unsupported carriers.

- **Files:**
  - Core: `Planning/SheetMetalPlanner.cs` (`Plan`), `Records/SheetMetalArea4Records.cs` (`SheetMetalFallbackRecord`, `CoveredByFallbackRecord`), `Geometry/ReflectionArrays.cs`, `Geometry/PointSets.cs` (grid), `Records/MirrorOptions.cs`.
  - Add-in: `Capture/SheetMetalSnapshots.cs`, `Build/FallbackSnapshotStore.cs`, `Build/Builders/SheetMetalFallbackBuilder.cs`, `Build/Builders/CoveredByFallbackBuilder.cs`, `UI/MirrorPage.cs`, `AssemblyContext/AssemblyMirror.cs`, `Build/MirrorJob.cs`.
- **Core tests:** A4 §13 `FallbackPlannerTests` (all except replan), `RefusalTests` with the switch, `ReflectionArrayTests`, `SheetMetalArgsTests`, `FallbackReportTests`, and `Welded_corner_does_not_trigger_once_named` (K17).
- **Stays refused:** with the switch off, the carriers before capture; uncovered carriers after planning (switch on, body does not qualify).
- **Live smoke:** a plate with a vent (switch on/off), a lofted-bend part, a tab and slot shared by two bodies (expect no plan, P2), the same part next to the original; B53–B62.

### 3.10 S7b – Fallback restart

- **What:** A4 P8 and §2.8:
  - job snapshot S0;
  - restart only for a qualifying body;
  - fresh `BuildShared`/context/report (K20) and `RunProgress.Start`;
  - no restart after Esc; `StopOnError` restarts only a qualifying body; at most three restarts per run;
  - the same part: `RemoveLeftovers(S0)` with folder contents, then an original mass check;
  - per-feature masters in `MassCapture` (cap 64).
- **Files:** `Build/MirrorJob.cs`, `Capture/MassCapture.cs`, `Planning/SheetMetalPlanner.cs` (`Replan`), `Planning/RestartRules.cs`.
- **Core tests:** the replan tests (A4 §13); `Restart_policy_truth_table` (Esc, `StopOnError`, cap, final state).
- **Live smoke:** an edge flange forced to fail (curved hinge) on a qualifying body, in a new part and in the same part; B63.

### 3.11 S8 – Gusset, tab and slot

- **What:** A4 §6 and §7 with P9 (explicit `ReferencePoint`; always-on section check) and P12 (in-context refusal; slot companion as `PartOf`; note on slot options; both bodies own).
- **Files:** `Records/SheetMetalArea4Records.cs`, `Planning/TabSlotRules.cs`, `Planning/GussetRules.cs`, `Capture/FeatureCapture.TabSlotGusset.cs`, `Build/Builders/TabAndSlotBuilder.cs`, `Build/Builders/SheetMetalGussetBuilder.cs`.
- **Core tests:** `TabSlotRulesTests`, `GussetRulesTests` (A4 §13).
- **Stays refused:** FormToolInstance as an Unsupported leaf; carriers as in S7a.
- **Live smoke:** two gussets on one bend (no reference point) on a reversed bend line, and the help sample `tab_and_slot.sldprt`; B64–B66.

### 3.12 S9 – Forming tools

- **What:** A4 §5 with P10 (pre-selected position sketch via the shared face path, K7), P11 (configuration candidates, no `MirrorTool`) and P15 (dispatch before `switch (def)`); registration with the cached rotation sense.
- **Files:** `Records/SheetMetalArea4Records.cs` (`FormToolRecord`), `Planning/FormToolPlacement.cs`, `Capture/FeatureCapture.FormTool.cs`, `Build/Builders/FormToolBuilder.cs`.
- **Core tests:** `FormToolPlacementTests` (A4 §13).
- **Stays refused:** carriers as in S7a.
- **Live smoke:** a louver, a dimple and a lance from the design library at angles 0°/30°/90°, with a two-configuration tool; B67–B70.

### 3.13 S10 – Optional items (each its own slice)

| Item | Content | Gate |
|---|---|---|
| Vent substitute cut | A4 §8 phase 5 | B71 |
| Swept flange (2021+) | A2 §5 late-bound builder; `ReverseDirection` from `InPlaneSideFlag` | B72 |
| Lofted bend | A3 phase 2 | draft A3 L12 (B73) |
| Mirror features over sheet metal | Child capture in the target with session and guard; lift K23 | spiegel-features live test 7 on sheet metal |
| Delete original with sheet metal | Measure the copy before and after; `EditUndo2` on a change (A1 P8) | new live test |
| Convert builder, 2025 SP2+ | `IConvertSolidFeatureData` | B60 |
| `MirrorTool` | A4 P11 (save location with a confirmation) | draft A4 18 |
| Corner-relief legacy path on 2019–2021 | A3 P13 | B49 |

---

## 4. The fallback's place

**Position: S7, after the parametric families (S1–S6), before Area 4's parametric features (S8, S9).**

**Why not first,** as the Area 4 draft proposed:
- It needs Area 1's infrastructure (guard, folder-aware snapshot, companions, flat check, allowance write path) and S6's Insert Bends wrapper (route B). Built first, it would have to build them itself, untested by a parametric caller.
- It exists only if B54/B55 pass (`ApplyTransform` with det −1; import of the reflected body). Making the other slices wait on that would block them on an unproven premise.
- It breaks the README promise of a fully editable tree (A4 P6) and must stay opt-in. A safety net that is off by default cannot be the route by which early slices ship. Each slice must be safe through refusal instead.
- If it came early, every still-undecided family (edge flanges before S3, and so on) would have to be routed into it, and those parts would be non-parametric up to their last edge flange. That conflicts with "parametric first" and churns the test parts.

**Why not last:**
- Area 4's parametric features rely on it as their exit: a chiral or misconfigured form tool, a gusset with `ProfileType` custom, and vents.
- The long tail it covers (lofted and swept bases, unreadable features, corner relief on 2019–2021, mirror features over sheet metal) is otherwise refused.

**What happens before S7:**
- The two-step refusal is already active from S1a: carriers by position, leaves by type state.
- The triggers the areas define are ordinary entries until then:
  - A2: curved hinge, ambiguous profile pairing, jog sketch that is not a single line, swept flange on 2019–2020, every Failed builder;
  - A3: corner relief on 2019–2021, lofted bend (a carrier, so refused);
  - A4: missing tool file, vent.

  Once S7a lands they become triggers on qualifying bodies, with no change in those areas.

**Gate and decisions before S7 starts:**
1. **Owner decision** (A4 P6): is a non-editable imported body acceptable at all, behind the switch? If no, S7 is dropped and S7′ applies.
2. **B54/B55**, probed in the first live session with a macro of about 30 lines (`Copy2` → `ApplyTransform(det −1)` → `CreateFeatureFromBody3`, in a new part and next to the original). If they fail, S7 is dropped and S7′ applies.
3. **B57** (convert on round bends) decides between routes A and B. If both fail, the result is F3 only, which helps little; reconsider then whether S7 is worth building.

**Rules that hold once S7 exists:**
- It never replaces a parametric builder that succeeds.
- **Capture-time trigger** (S7a): an `UnsupportedRecord` with `ChangesSolid && TriggersFallback` on a qualifying body.
- **Build-time trigger** (S7b): a sheet-metal record on a qualifying body ends Failed or "weicht ab".
- Covered entries are warnings. With A1 P8, deletion of the original stays refused anyway.
- The switch is on the page in the group „Prüfung und Bezüge“; the assembly path passes it through.

---

## 5. Live-test assumptions (merged and de-duplicated)

### 5.1 Table

Source keys:
- **A1:** S1–S15 from A1 §14, plus the draft rows L1–L24 (A1 keeps L3–L18, L20, L23 and L24; L1, L2, L19 and L21 are folded into its S rows; L22 moved to Area 4);
- **A2:** L1–L18 from A2 §12; "A2d" = draft A2 §9 rows that the review folded into its own;
- **A3:** L1–L17 from A3 §12; "A3d" = draft A3 §9;
- **A4:** L1–L20 from A4 §14; "A4d" = draft A4 §8.

"Gate" = without it the slice cannot ship as designed.

| # | Assumption | Sources | Slice | If wrong |
|---|---|---|---|---|
| **B1** | Which base-flange path builds on 2019/2022/2025, for closed **and** open profiles: A (`CreateDefinition(34)` without `Initialize`), B (v1, `DirToUse` = `OffsetDirections`), C (v2) | A1 S1, L1, L2 | S1a **gate** | Attempt order; the report names the path. If none builds, S1 is blocked. |
| B2 | Path A merges or not with a touching body (there is no Merge member) | A1 S2 | S1a | Body-delta check; path C with Merge = false |
| B3 | Closed profile and tab: `ReverseDirection` decides the side; `ReverseThickness`/`ThickenDir` have no effect | A1 L4 | S1a | Toggle alternatives |
| B4 | Open profile: the material side is measured against the sketch normal and our segment direction (model a), not the extrusion direction (model b) | A1 S14, L3 | S1a | "Materialseite" toggle; if model b holds: always flip, and the same rule for `ExtrudeBuilder` thin features |
| B5 | `D1EndConditionType` and `D1OffsetType` agree in newer versions; type 6 (Up to edge and merge, 32.1) | A1 S12, P12 | S1a | Trust the new member, add a note; 6 → Unsupported |
| B6 | A tab is recognised geometrically and by its shared SheetMetal parent | A1 S15, L20 | S1a | The geometric test is primary |
| B7 | SheetMetal is a parent of the base flange and precedes it in creation order | A1 L11 | S1a | Owner search via `GetChildren` |
| B8 | Tree shapes: <ul><li>single body 2013+ (SheetMetal and FlatPattern top-level?);</li><li>multibody (inside the folders; the folder appears with the 2nd body, also in the same part);</li><li>flat-pattern sub-sketches are `ProfileFeature` and suppressed when folded;</li><li>user corner trims are flat-pattern sub-features.</li></ul> | A1 S9, L12, L13; A3 L1 (part) | S1a | The snapshot covers both shapes; owner by parent |
| B9 | `IBody2.GetFeatures()` of a sheet-metal body lists SheetMetal/FlatPattern; a body at a rolled-back state is found as the one whose `GetFeatures()` contains a given feature | A1 S10; A4 L16 | S1a (histories), S7a | Closure by name (`SheetMetalTree`); no snapshot for that body |
| B10 | The flat pattern is created with the flange and suppressed; later features land before it; `GetLastFeatureAdded` returns the new feature | A1 L8; A2 L18; A3 L15; A2d 15 | S1a | `NewSince` is the primary rule anyway (K6) |
| **B11** | Session: `SetSuppression2(0/1, ThisConfiguration)` folds and flattens per flat pattern, also multibody and under `UpdateFreeze`, and restores cleanly; a flattened original is captured correctly once folded | A1 S7, L10; A2 L15 | S1a **gate** | Run refused; report line |
| B12 | `IFlatPatternFeatureData` edits work on the suppressed flat pattern | A1 S8, L9 | S1a | Unsuppress, edit, suppress |
| B13 | `ISheetMetalFeatureData` of `TemplateSheetMetal` comes from `GetDefinition` | A1 L23 | S1a | The single body-level SheetMetal |
| **B14** | A later base flange created with the template values and overridden on its own SheetMetal leaves the template and the other bodies unchanged (new part, multibody, and the same part) | A1 S3, L19 | S1a/S1b **gate** for S1b | Guard restores, or aborts; same part stays refused |
| B15 | `SetOverrideDefaultParameter2` + thickness/radius/allowance take effect on a body's SheetMetal on 2019; template writes take effect; `GetOverrideDefaultParameter2` returns non-zero in single-body parts | A1 S4, S11, L6 | S1a | Read-back + guard; Warning; "not applicable" |
| B16 | Allowance at creation: PCBA (paths B/C) or `Initialize` (A) sets the first body's and the template's allowance; `Type` written last | A1 S5, L5 | S1a | Flat check |
| B17 | A gauge table can be set afterwards by `ModifyDefinition` in the order UseGaugeTable → Path → ThicknessTableName → TableRadius → Override* | A1 L7 | S1a | Fixed values with a note |
| B18 | `ICustomBendAllowance.BendCalculationTableFile` is readable late-bound | A1 L24 | S1a | Note „Biegeberechnungstabelle nicht übertragbar“ |
| B19 | `RemoveHelpers` leaves no orphan SheetMetal/FlatPattern, and the folders stay | A1 S6 | S1a | Snapshot comparison after the removal |
| B20 | Equations on `Thickness@Sheet-Metal1` resolve after the companion rename | A1 S13 | S1a | Existing equation report |
| B21 | Grain direction has no API and is not visible as a parent | A1 L14 | S1a | Note in the flat pattern's details |
| B22 | Flat measurement in the **target** during the build (unsuppress → `MassOf` → suppress) works under `UpdateFreeze`, in the new and the same part | A1 §9 (implicit there; added here); A4 L20 | S1a | „nicht geprüft“ + Warning |
| B23 | Bend sub-features: `IOneBendFeatureData` readable under the sub-feature's `AccessSelections`; writes are ignored; the copy's bend order equals the original's; type names of `HemBend`/`Miterbend` and that their definition is `OneBendFeatureData` | A2 L16, L9 (part); A3 L10; A2d 16 | S2 | Notes only; count by definition |
| B24 | Bend faces belong to the bend sub-features (`HemBend`, `SketchBend`), not to the parent | A3 L9; A2 L9 (part) | S2 | Sub-feature sampling (K16) |
| B25 | Miter flange: sketch on mark 0, edges on mark 1 (2019); the mark-0 retry gives a full flange for a single edge | A2 L8 | S2 | Order variant; edge-count mismatch rejected |
| B26 | Hem: edges on mark 0 with a null allowance; the width sketch(es) are editable; several width sketches pair with the edges | A2 L9; A2d 10, 11 | S2 | Keep the native width sketch with a note |
| B27 | Per-feature allowance: edge flange, miter, hem, jog and sketched bend take a K-factor through the allowance-only edit; the jog refuses a bend table (2019 too); PCBA is honoured at creation (sketched bend); an edit can reset a custom allowance | A2 L14, P7; A3 L5; A3d 3 | S2 (S4) | Read back after every edit; note; recreate (A3 P5) |
| B28 | `CreateDefinition(37)` + `AddEdges` + `CreateFeature` works on 2019 | A2 L1; A2d 1 | S3 | Paths B, C; the report names the path |
| **B29** | The `InsertSketchForEdgeFlange` angle is a directed rotation about the B-rep hinge; what `FlipDir` (and the `swInsertEdgeFlangeOptions_e` bits) do; the frame's Y points along the flange | A2 L2; A2d 2, 5 | S3 **gate** | 8-candidate search with `FitsFrame`; otherwise fail (fallback) |
| B30 | `BendAngle` accepts 2π−θ, reads back θ or 2π−θ, and gives the mirrored flat pattern | A2 L3 | S3 | `Fold` comparison; flat check |
| B31 | Reading without rollback gives the offset profile; `ReadSketch` gives the same ids; profiles of a multi-edge flange are found as sub-features or as a top-level parent (P13 rule) | A2 L4; A2d 6, 7, 8 | S3 | Pair by geometry; otherwise Unsupported |
| B32 | The native profile sketch may be empty or hold a rectangle, and may be open or closed | A2 L5; A2d 3 | S3 | `ReplaceInto` clears it; `ActiveSketch` checked |
| B33 | Array forms for `AddEdges`/EF2 (`Edge[]`/`Sketch[]`, `Feature[]`, `object[]`, `DispatchWrapper[]`) | A2 L6; A2d 4 | S3 | The ladder |
| B34 | A separate edit for `ReverseOffset` settles the profile direction (2019 too); toggling twice restores | A2 L7; A2d 17 | S3 | Toggle search with re-check |
| B35 | Late-bound `LockAngle`, `AngleReference`, `NormalToFlangePlane`, `PerpendicularToFace` exist only on newer versions | A2d 18 | S3 | `SwLate` null; note if the original used them |
| **B36** | Sketch on the mirrored face: the jog and `SM3dBend` accept it; whether a coplanar reference plane is accepted instead; the support-face fingerprint from the forced rollback after the sketch is found in the target | A2 L11; A3 L8; A2d 13; A3d 4 | S4 **gate** (jog) | Jog fails with a reason; bend on a plane with a note |
| B37 | `SelectByRay` selects the face under `UpdateFreeze` (graphics and tree off); `GetSelectionPoint2` returns the hit point | A3 L3; A2 L10 (part) | S4 | Next candidate; never a point-less selection |
| B38 | Fixed-point spaces: the `GetFixedFace` point is in sketch space (help); the space of `IJogFeatureData.FixedPoint` and whether it can be set | A3 L2; A2 L12; A2d 14 | S4 | Distance disambiguation; ambiguity noted; jog "feste Seite" alternative only when the space is unknown |
| B39 | The bend-direction reference: sketch normal, face, or the hidden sheet orientation set by the base-flange flags (eyfel); the σ_sheet formula | A3 L7; A3d 5 | S4 | Witnesses decide; the first guess stays σ_sketch |
| B40 | `InsertSheetMetal3dBend` ignores `FlipDir`; `ModifyDefinition(ReverseDirection)` works | A3 L4; A3d 3 | S4 | Witnesses; recreate |
| B41 | `BendPos` 0 = centre line; jog DimPos 1-based and BendPos 0-based | A3 L6; A2 L10 (part) | S4 | Read back `PositionType`; edit |
| B42 | Jog: angle in **degrees**; segment on mark 0 plus face by ray | A2 L10; A2d 12 | S4 | Radians retry; sketch-feature selection retry |
| B43 | The witness check detects a held wrong side (CADapter's 90° rotation), for jog and sketched bend | A2 L13; A3 P2 | S4 | Mass check as a second line |
| B44 | Type names: `CornerFeat` (closed corner), corner relief, welded corner | A3 L1; A3d 1 | S5 | Collected by data interface; unknown names get the generic reason; the welded corner triggers until named (K17) |
| B45 | Closed corner: mark 1 for faces to extend, 0x40000000 for faces to match; what `Faces` returns; in the same part it acts only on the selected entities | A3 L12; A3d 6, 13 | S5 | Variants a–c; read-back as a set |
| B46 | Break corner: mark 0 for edges and faces; `Entities` reads back the same set | A3 L17; A3d 7 | S5 | — |
| B47 | Unfold/Fold: bends on mark 4 (official) vs 2; the `FixedFace`/`Bends` setters work through `ModifyDefinition`; `GetFaces` of a bend under `AccessSelections` | A3 L14; A3d 10 | S5 | Variants; name map |
| B48 | Cross break: reverse is relative to the outward normal; whether it has an editable sketch sub-feature | A3 L11; A3d 11 | S5 | Lines compared; report text |
| B49 | Corner relief (2022+): `CreateDefinition(120)`, `InvokeOut` through IDispatch, `GetCornerAtIndex`, legacy marks (faces mark 4, body mark 0), `SetBodyScope` on the copy's body | A3 L13; A3d 9, 13 | S5 | Legacy path; Unsupported |
| B50 | The relief settings of a user corner trim are readable nowhere | A3 L16; A3d 8 | S1a | Unsupported (K12) |
| B51 | Normal Cut via `CreateDefinition(115)` (marks 4/8/1, `CreateGroup`) on 2019 | A1 L17 | S6 | Obsolete `AddSMNormalCut*` |
| B52 | `InsertBends2` takes the fixed face from the current selection (mark 0; marks 1 and 0 tried); on a body with round bends (fallback route B) K/BA are taken | A1 L15; A4 L4; A4d 12 | S6, S7a | Route B dropped |
| B53 | Convert/Insert Bends argument values: `ReliefType` 0 vs 1–5, `OverlapType` 0 vs 1; rip overlap/underlap does not depend on orientation | A4 L18; A1 L18 | S7a | Values from the enum; toggle alternative |
| **B54** | `ApplyTransform` accepts det −1 on a `Copy2` temporary body (direct array or `(−M, scale −1)`); vertices and volume as expected | A4 L1; A4d 8 | S7 **gate** | No fallback; S7′ |
| **B55** | `CreateFeatureFromBody3` of the reflected temporary body gives a separate valid solid without external references, also in the same part next to a touching original; whether the temporary body is consumed | A4 L2; A4d 9, 10 | S7 **gate** | Fresh copies anyway; otherwise no fallback |
| B56 | Temporary bodies survive later rollbacks and the build in another document (assembly) | A4 L15; A4d 9 | S7a | Snapshot pass right before the build |
| **B57** | Convert with `FindBends` = true and only a fixed face on a body with round bends: bends collected, geometry unchanged; which face side works | A4 L3; A1 L16; A4d 11 | S7a **gate** (routes) | A2, then B, then F3 |
| B58 | An allowance write takes effect on the convert's SheetMetal | A4 L5; A4d 13 | S7a | Warning from A4 P1; route B |
| B59 | Same part: convert/Insert Bends leave the template and the original unchanged | A4 L6; A4d 14 | S7a | Guard restores the snapshot, else abort |
| B60 | `IConvertSolidFeatureData` readable and creatable late-bound from 2025 SP2; the numeric value of `swFmSolidToSheetMetal` | A4 L19; A1 L22 | S7a (S10) | The 2019 path |
| B61 | The flat-state check works on the convert's flat pattern | A4 L20 | S7a | „Abwicklung nicht geprüft“ |
| B62 | A covered body's later records find their faces on the imported body by fingerprint (exactly the mirrored state) | A4d §4 "When it is triggered" (implicit there; added here) | S7a | Those records fail and a restart (S7b) moves k |
| B63 | `CloseDoc` on an unsaved new part closes without a prompt (restart) | A4 L17 | S7b | No restart in a new part; final-state import instead |
| B64 | Tab and slot: type names, slot companion as a child, the slot feature's definition; `CreateDefinition(88)` returns `ITabAndSlotFeatureData` in 2019; groups can be modified after creation | A4 L11; A4d 1, 16 | S8 | Name pattern; `PartOf` |
| B65 | Gusset: the implied reference is which bend end; with a second gusset, which edge of the previous one | A4 L12 | S8 | Alternatives L − d |
| B66 | Gusset: v1 legs on mark 0; `ReferencePoint`/`ReferenceLine` can be set after creation; the meaning of `ProfileType` custom | A4 L13; A4d 15 | S8 | v2 with arrays; custom → NotSupported |
| **B67** | `InsertFormToolFeature` with a pre-selected 2D sketch on the face places one instance per point and absorbs the sketch | A4 L7; A4d 4 | S9 **gate** (route) | Face plus variant A |
| B68 | The second sub-sketch carries the rotation as geometry; the rotation sense of `RotAngle` | A4 L8; A4d 3, 5, 7 | S9 | 3D registration only; ± tried |
| B69 | `FormToolInstance.GetDefinition()` is null in 2019–2024; `ILibraryFormToolFeatureData` from 2025 SP2; units of `RotationAngle`; the "Sheet Metal tab visible" requirement | A4 L9; A4d 2 | S9 | 2019 path |
| B70 | `ListExternalFileReferences2` lists the tool path for linked and unlinked tools | A4 L10; A4d 6 | S9 | Library search |
| B71 | Vent type name; a sheet-metal vent is a prismatic cut | A4 L14; A4d 1 | S10 | Trigger only |
| B72 | Swept-flange type name; whether 2021+ late-bound `GetDefinition` members are readable through IDispatch | A2 L17 | S10 (S1a for the name) | Generic Unsupported; positional carrier rule |
| B73 | Lofted bend: segment selection with mark 1; the `Direction` ↔ `ThickDirType` mapping | A3d 12 | S10 | Stays refused or covered |

**Dropped as superseded:**
- draft A4 17 (body names stable): replaced by B9 and A4 P7;
- draft A4 L(Save/Restore): dropped with A4 P16;
- draft A4 18 (`CreateFormTool2` marks): needed only by `MirrorTool` in S10.

### 5.2 First live session (gating set, before or with S1a)

Run these on 2019 first, then 2024. Rows marked † are probed with small macros before their slice exists, so the decision is known early:
- base flange and session: B1, B2, B11, B12, B14, B15, B16;
- edge flange: B28†, B29†;
- sketch on face: B36†;
- fallback: B54†, B55†, B57†;
- form tools: B67†.

Results go into the STOLPERSTEINE live-test table. A failed gate changes the slice before work on it starts:
- B29: edge flanges stay `NotYet` until resolved;
- B36: the jog becomes Unsupported (decided);
- B54/B55: S7′;
- B57: S7 limited to route B or F3;
- B67: variant A only.

---

## 6. Effort

| Slice | Days | Basis |
|---|---|---|
| S0 | 2–3 | refactor + Core types/tests |
| S1a + S1b | 7–9 | A1 (2) minus the parts in S6 |
| S2 + S3 | 5–7 | A2 (2) without the jog |
| S4 | 4–5 | A3 sketched bend + A2 jog + the shared pick/witness/face infrastructure |
| S5 | 4–5 | rest of A3 |
| S6 | 2 | A1 phase 1b + normal cut |
| S7a + S7b | 8–10 | A4 phases 1–3 |
| S8 + S9 | 5–7 | A4 phases 4–5 |
| Docs per slice | included | README, ARCHITEKTUR "Blech" section, STOLPERSTEINE, STAND |
| **Total** | **≈ 37–48 (≈ 43 typical)** | plus 10–12 days of live tests (first session ≈ 2 days, then ≈ 1 per slice) |

S10 items are extra (vent 2, swept flange 2, lofted bend 2–3, mirror features over sheet metal 2–3, delete original 1).

## 7. Open owner decisions

1. **The fallback at all** (A4 P6). It is the one place where a body is taken over as geometry, by an ordinary body transform. It needs a switch, off by default, and a README change.
2. **The leaf policy after a type is decided** (K2). A failed leaf (e.g. a form tool without its file) is a Failed entry and the rest of the part is built, like an unsupported fillet. The alternative is to keep refusing whole runs for missing builders.
3. **„Original löschen“ for sheet metal** stays refused until the S10 item with its measurement and undo.
