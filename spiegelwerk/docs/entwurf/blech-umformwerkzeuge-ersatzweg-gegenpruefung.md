# Review of the Area 4 sheet-metal design (forming tools, tab and slot, gusset, vent, fallback)

I checked `docs/entwurf/blech-umformwerkzeuge-ersatzweg.md` against the current tree in `/home/user/installer-lane/spiegelwerk` (branch `installer-lane`, HEAD `54a24d7`, clean). I also checked it against the 27.3.0 and 32.1.0 interop assemblies, the saved API help, the third-party code the draft cites, and the reviews of Areas 1 and 2 that already exist in this scratch folder (`blech-grundlagen-gegenpruefung.md`, `blech-laschen-gegenpruefung.md`). Line numbers ("l.") refer to the current files. Nothing outside `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech` was modified.

I found **17 problems (P1–P17)**. Five of them can give wrong parts without any error, damage the original, or keep the area from running at all:

- **P1:** after a convert, the fallback's bend allowance cannot be set. The fold looks right while the flat blank is wrong, and no check notices.
- **P2:** the test for which bodies qualify reads `OtherBodies`, which is always empty in whole-part mode. A tab and slot or a cut shared by two bodies is covered for one body and lost for the other.
- **P3:** the run refusal is never opened, so the area cannot run. The refusal must also move to after the planning step.
- **P4:** in the same part, the convert can rewrite the part's shared sheet-metal defaults and so change the original. A failed attempt leaves its companion features behind.
- **P5:** a convert that is given only a fixed face and asked to find existing bends has never been shown to work. A failure mode measured by a third party leaves only one wall of the body.

**API.** Every member and enum value the draft names exists in 27.3.0 with the stated signature. The 32.1.0 differences are as stated, plus `ISheetMetalFeatureData.UseMaterialSheetMetalParameters` (32.1 only), which the draft does not mention.

**Mirror rules.** The forming-tool rotation rule θ′ = β − θ − 2α is right; I checked it independently with 20 000 random cases (largest error 3.5e-14). The rules for flip, tab-and-slot direction and leg order are right. Two rules are wrong or incomplete: the gusset's implicit reference point (P9) and the bend allowance of the fallback body (P1).

**Mirror API.** The design calls no forbidden member (`InsertMirrorFeature*`, `MirrorPart*`, `InsertMirrorAll`, derived parts). The fallback does, however, produce a mirrored body that cannot be edited. That calls for an owner decision and an opt-in switch (P6). The GitHub "evidence" for the pattern is overstated: the cited code builds its actual result with `MirrorPart2`.

**Verdict.** The area can be implemented as corrected in (2), in this order:
1. The fallback, behind a switch that is off by default. It is gated by live test L1 (`ApplyTransform` accepts det −1).
2. Gusset and tab and slot.
3. Forming tools.
4. Vent, optionally, as a measured substitute cut.

The fallback depends on the Area 1 infrastructure (`SheetMetalGuard`, a feature snapshot that also covers the contents of the sheet-metal folders, companion registration, the flat-state check).

Scratch work, all in `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech/work-area4/`:
- **Interop probe:** `p.sh 27|32 sw|const members <Type> [filter] | enum <name> | types <name>`, a wrapper around `…/150560a9…/scratchpad/mirrorreview/out/mirrorreview.dll`. Member dumps for both versions are in `I*.27.txt` / `I*.32.txt` (tab and slot, gusset, sheet-metal feature data, flat pattern, bends, custom bend allowance, fault entity).
- **Help extraction:** `doc.py Type.Member …` reads the offline API help catalogue `…/150560a9…/scratchpad/fc2/repos/offdocs/50_extract_type_member_details/metadata/api_member_details.xml`.
- **Numeric checks:** `checks.py` (run with `python3 -I`). It covers the forming-tool rule, the rejection of a chiral tool, the equivalence of the `(−M, scale −1)` form, and the gusset end-swap.
- **help.solidworks.com:** does not resolve from this session (ENOTFOUND). Statements taken from web-search summaries of help pages are marked "(search summary, unverified)".

---

## Verified as correct

**API members (27.3.0; identical in 32.1.0 unless noted)**
- **`IFeatureManager`:**
  - `Feature InsertFormToolFeature(string, bool, double, string, bool, bool, bool, bool, bool)`, `Feature CreateFormTool2(double, double)`.
  - `bool InsertConvertToSheetMetal2(double, bool, bool, double, double, int, double, int, double, bool)`.
  - `InsertSheetMetalGussetFeature` (21 parameters). `…2` adds `(object ArrayOfFaces, object ArrayRefLines, object ArrayRefPoints)`; `…3` adds `(bool BOverrideDoc, bool BShowProfile, bool BShowCenter)` to the 21.
  - `object CreateDefinition(int)`, `Feature CreateFeature(object)`, `object GetSheetMetalFolder()`, `object GetFlatPatternFolder()`.
- **`IPartDoc`:**
  - `bool InsertBends2(double, string, double, double, bool, double, bool)`.
  - `object CreateFeatureFromBody3(object, bool, int)` and `Feature ICreateFeatureFromBody4(Body2, bool, int)`.
  - The forbidden `InsertMirrorAll`, `InsertMirrorFeature`, `MirrorFeature`, `MirrorPart` and `MirrorPart2` also exist. The draft calls none of them.
- **`IBody2`:** `object Copy2(bool)`, `bool ApplyTransform(MathTransform)`, `bool IsSheetMetal()`, `FaultEntity Check3 {get;}`, `object GetMassProperties(double)`, `void Save(object)`, `bool IsTemporaryBody()`, `object GetVertices()`.
- **Other interfaces:**
  - `IFaultEntity`: `Count`, `ErrorCode(int Index)` (indexed in the help), `Entity2`.
  - `IModeler.object Restore(object)`.
  - `IMathUtility.object CreateTransform(object)`. `ISurface` has no `ApplyTransform`; `ICurve` has one.
  - `ISldWorks.string RevisionNumber()`.
- **`ITabAndSlotFeatureData` and `ITabAndSlotGroupData`:** exactly the members the draft lists, the same in both versions.
- **`ISMGussetFeatureData`:** exactly the listed members, with `AccessSelections(ModelDoc2, Component2)`. The same in both versions.
- **`ISheetMetalFeatureData`:** as listed. 32.1 adds `UseMaterialSheetMetalParameters`. The help marks `KFactor` obsolete, superseded by `Get`/`SetCustomBendAllowance`.
- **Other sheet-metal data:**
  - `IFlatPatternFeatureData`: `FixedFace`, `FixedFace2`.
  - `IBendsFeatureData`: `GetFixedFace()`, `KFactor`, `BendAllowance`, `BendAllowanceType`, `BendTableFile`, `Get`/`SetCustomBendAllowance`.
  - `ICustomBendAllowance`: `Type`, `KFactor`, `BendAllowance`, `BendDeduction`, `BendTableFile`.
- **`IFeature`:** `ListExternalFileReferences2(out object × 7, out int ConfigOption, out string ConfigName)`, `ListExternalFileReferencesCount()`.
- **Not present in either version:**
  - No interface for forming tools (`ILibraryFormToolFeatureData`), vents or convert (`IConvertSolidFeatureData`).
  - No member containing "Vent" on `IFeatureManager`, `IModelDoc2`, `IModelDocExtension` or `IPartDoc`.
- **Also present and useful for the corrections:**
  - `ISldWorks.GetConfigurationNames(string)`, `IModelDocExtension.RotateOrCopy(…)`, `IModelDoc2.GetBendState()`/`SetBendState(int)`, `IEntity.Select4(bool, SelectData)`.

**Enums (27.3 = 32.1)**
- **`swFeatureNameID_e`:**
  - `swFmFormToolInstance` = 73, `swFmTabAndSlot` = 88, `swFmImported` = 26.
  - 32.1 only: `swFmSweptFlange` = 117, `swFmSMGusset` = 118, `swFmCornerRelief` = 120.
  - In neither version: `swFmSolidToSheetMetal`.
- **`swCreateFeatureBodyOpts_e`:** Check 1, Simplify 2.
- **`swSheetMetalReliefTypes_e`:** 1 rectangular, 2 tear, 3 obround, 4 none, 5 tear-bend.
- **`swBendAllowanceTypes_e`:** 1 table, 2 K, 3 direct, 4 deduction, 5 calculation table.
- **Tab and slot:**
  - `swTabSlotFeatureHeightType_e`: Blind 0, UpToSurface 1, OffsetFromSurface 2.
  - `swTabSlotFeatureSpacingType_e`: EqualSpacing 0, SpacingLength 1.
  - `swTabEdgesType_e`: `SharpEdge` 0, `FilletEdge` 1, `ChamferEdge` 2. The names differ slightly from the draft's.
- **Gusset:**
  - `swSheetMetalGussetProfileDimType_e`: 0 indent depth, 1 profile dimensions.
  - `swSheetMetalRibGussetType_e`: 0 rounded, 1 flat.
  - `swSheetMetalGussetProfileType_e`: 0 rib, 1 custom.
- **File locations:**
  - `swUserPreferenceStringValue_e`: `swFileLocationsDesignLibrary` = 38, `swFileLocationsPaletteFormTools` = 4.
- **Bend state:** `swSMBendState_e` 0–3.

**Help statements confirmed (offline catalogue)**
- **`CreateFormTool2`:** the stopping face is selected with mark 1, the faces to remove with mark 2.
- **`InsertConvertToSheetMetal2`:**
  - `FindBends` = "True to find pre-made bends and part thickness".
  - `OverlapType` 1–3.
  - The return value reports whether the feature was created; CADapter measured `False` even on success.
- **`InsertBends2`:** the offset ratio must be within 0.05–2.0. `DoFlatten` creates SheetMetal + FlattenBends + ProcessBends.
- **`CreateFeatureFromBody3`:** `MakeRefSurface` = true makes a surface "if a solid body already exists".
- **`ITabAndSlotGroupData`:** `TabOffsetValue` is "measured from `SelectionSlotFace`". UpToSurface also goes up to the slot face. `TabReverseDirection` is valid only for OffsetFromSurface.
- **`ISMGussetFeatureData.ReferenceLine`:** defaults to the bend line next to the legs.
- **Release notes 2025 SP2:**
  - `IConvertSolidFeatureData` obsoletes `InsertConvertToSheetMetal2`. Its `Initialize(bool, bool, object CustomBendAllowance)` takes the allowance at creation.
  - `ILibraryFormToolFeatureData` obsoletes `InsertFormToolFeature`.

**Claims about the existing code that hold**
- `StepVerifier.Verify` holds `result.Feature` during the variant search (StepVerifier.cs l.205). Variant search across re-inserts therefore has to live inside the builder, as the draft says.
- These helpers exist with the signatures the draft uses:
  - `SketchBuilder.BuildInto(SketchModel, Feature, List<string>)` (l.99).
  - `BuilderSupport.SelectFaceAt` (l.154).
  - `BuilderSupport.FlagForReference` (l.125).
  - `EdgeLocation.Map` (VariableFilletRecords.cs l.78). `Map(wanted, actual, 0, 1) == 1` detects a reversed edge.
  - `TopologyCapture.DistanceToFace` (l.73), `SwMath.SketchFrame`, `SwLate.Get`/`Set`.
- `Affine.FromSwArray` multiplies the columns by the scale and leaves the translation alone (Affine.cs). `(−M, scale −1)` and `(M, scale 1)` are therefore the same map in Core (checks.py #3). `Reflection.ToAffine().ToSwArray()` exists.
- `HolePart`/`CaptureOnItsOwn`/`PositionedHoleRecord`/`PartOfFeatureRecord` exist as described and can be generalised. The places that need the generalisation are listed in P15.
- `BuildContext.IsOwnBody` decides by `GetFeatures()` against the registered names (BuildContext.cs l.154). Registering the imported feature and its companions is therefore enough for the guard.
- Builders do not mutate records, so a restart can reuse the records. Snapshots are a different matter (P7).

**Third-party evidence that holds**
- RearHubRepair (BeamNG.cad): `Copy2(false)` from one document, `ApplyTransform` with a (rigid) transform, then `CreateFeatureFromBody3(body, false, Check)` into another part that already has two bodies. The result has three bodies, even though the hub touches the rim. The import is a body of its own and does not merge.
- CADapter (SOLIDWORKS 2017, measured):
  - The gusset v1 works with the two concave-side faces at mark 0, with `BDraft` = false and the draft angle in radians.
  - The convert has to be `…2`, the entities have to be selected with `Select4`, and the return value is meaningless.
- **Maths:** checks.py #1 (θ′ = β − θ − 2α against random frames, symmetric tool, z′ = R n) and #2 (a chiral tool has no matching rotation; best residual 0.33 over a 0.05° grid).

---

## (1) Problems

### P1. After a convert, the fallback's bend allowance cannot be set. The flat blank is wrong while every check passes.
- **Evidence:**
  - Draft §4 F1 (l.307): "a new `FeatureEdit.SheetMetal` sets `BendAllowanceType`/`KFactor`/`BendAllowance`/`BendTableFile` (or `SetCustomBendAllowance`) … to the original's values".
  - CADapter measured that `ModifyDefinition` on `ISheetMetalFeatureData` returns true and changes nothing (`…/150560a9…/scratchpad/ghsrc/cadapter_sw_sheetmetal.py` l.1231–1242, l.1442, l.1479–1491):
    - for the K-factor (8 combinations);
    - for the bend table (5 combinations);
    - for the radius, unless `SetOverrideDefaultParameter` is called first.
  - "K-factor (per FLANGE — the global one won't budge)" (l.44).
  - Help: `SetOverrideDefaultParameter2` is "only valid for multibody sheet metal parts created in SOLIDWORKS 2013 and later". The Area 1 review reaches the same conclusion (its P9).
  - `InsertConvertToSheetMetal2` has no allowance parameter in 2019–2024.
  - Mirroring leaves the bend allowance unchanged, so the right value is known. It simply cannot be written.
  - The allowance changes only the flat pattern. The folded imported body is identical whatever K is, so the draft's checks pass and the entry reads "OK":
    - volume;
    - moment;
    - face samples;
    - vertex set.
  - The part is then cut with a blank of the wrong length.
  - Per-bend overrides in the covered prefix (an edge flange with its own allowance) cannot survive any global setting either. The draft does not mention them.
- **Fix:** see (2) §4.4.
  - Capture the body's allowance, gauge and relief through Area 1's `SwSheetMetal.Read`, and every per-bend override in the covered prefix (Area 2's `OneBend`/flange custom allowances).
  - **After a successful convert:**
    - Write with Area 1's path (`SetOverrideDefaultParameter2` + `SetCustomBendAllowance`, return codes checked), then read back.
    - On a mismatch, remove the convert and try `InsertBends2` (route B), which takes K, allowance and table at creation, then read back.
    - If neither matches, keep the convert with status Warning: „Biegezugabe des Originals (K 0,44) ließ sich nicht setzen – Abwicklung weicht ab.“
  - On 2025 SP2+, use the late-bound `IConvertSolidFeatureData.Initialize(false, useGauge, cba)`.
  - **Flat-state check:** when the snapshot is the body's final state, compare the flat volume with the original's (Area 1 §9 `FlatChange`). Mirroring leaves it unchanged, and it reacts to K. A mid-history snapshot gets „Abwicklung nicht geprüft“.
  - Any per-bend override in the covered prefix adds the warning „Biegungen mit eigener Biegezugabe im Ersatzkörper: … – nicht übernommen.“

### P2. The test for which bodies qualify uses `OtherBodies`, which is empty in whole-part mode. A shared feature disappears from the other body.
- **Evidence:**
  - Draft l.320: "no feature also feeds other bodies (`OtherBodies` empty)".
  - `OtherBodies` is filled only by `MarkOtherBodies` from the `BodyScope` and by `MassCapture.ForeignRule`. Both exist only with selected bodies or seed bodies:
    - FeatureCollector.cs l.38: `scope = bodies.Count > 0 || seedBodies ? ScopeOf(…) : BodyScope.None`;
    - MirrorJob.cs l.154–158.
  - In whole-part mode, which is the main case for the fallback, it is empty for every record.
  - **Typical case:** a multibody sheet-metal part where a tab and slot joins body A and body B (tab and slot always spans two bodies), followed by a vent on A.
    - The plan for A covers the tab-and-slot record (`CoveredByFallbackRecord`, `ChangesSolid` false).
    - B never gets its slots.
    - The covered record is never measured or checked, and the report shows only "im Ersatzkörper enthalten".
  - The same happens:
    - with a cut through two bodies;
    - with a cut that splits a body (both halves list the same history);
    - with a body that is later merged into another (the second body's history contains the first's features).
- **Fix:** the planner always receives the per-body histories of all solid bodies, also in whole-part mode (`FeatureCollector.SolidFeatures` for every body; this costs only a tree walk). A body qualifies for a snapshot after k only if:
  - no record at or before k in its history appears in another body's history;
  - its history is not contained in another body's (merge);
  - with selected bodies, `OtherBodies` is empty for every covered record.
  - Otherwise there is no plan for the body, and the trigger record stays Failed (P3 decides whether that refuses the run).
  - A shared feature after k is fine: it stays parametric and acts on the imported body as it acted in the original.

### P3. The run refusal is never opened, and it has to move to after the planning step
- **Evidence:**
  - `MirrorJob` refuses the run before capture when any collected type is in `UnsupportedFeatures.SheetMetal` (MirrorJob.cs l.138; UnsupportedFeatures.cs l.12–28).
  - That set contains `FormToolInstance`, `SMGusset`, `SolidToSheetMetal`, `SheetMetal`, `SMBaseFlange` and `FlatPattern`. `ReportTextTests` l.82–83 asserts the refusal.
  - The draft only says "remove `FormToolInstance` and `SMGusset` from the blanket sheet-metal reason" (l.274). As written, no fallback can ever run.
  - Whether a body has a plan is known only after capture (records, `UnsupportedRecord`s, per-body histories). A refusal based on the type name alone cannot express "covered by the fallback".
  - The Area 2 review proposes `FallbackCovers(type)`, but the fallback works per body, not per type.
  - Leaf features (forming tool, gusset, tab and slot, vent) do not carry the base body. Refusing a whole run because one forming-tool file is missing is harsher than today's handling of an unsupported fillet.
- **Fix:** see (2) §2.
  - **Before capture:** refuse only for `MirrorStock`, and for carrier types (`SMBaseFlange`, `SheetMetal`, `SolidToSheetMetal`, `FlattenBends`, `ProcessBends`, `SweptFlange` before 2021 …) that have no builder while the fallback is off.
  - **After capture and planning, still before a new part is created or anything is built:** refuse if an `UnsupportedRecord` on a sheet-metal body is a carrier and no plan covers it.
    - A carrier is a record that is the first solid feature in a body's history, or has a carrier type.
    - The message names the feature and, with the fallback off, the switch.
  - Leaf records stay ordinary Failed entries with „Folgende Features … können ebenfalls scheitern“.
  - Tests in (2) §13.

### P4. Same part: the convert can rewrite the shared sheet-metal defaults, and cleanup removes one feature at most
- **Evidence:**
  - The Area 1 review, P1 (CADapter: "a second base flange at 3 mm thickens the first one"; help: the shared parameters live in the Sheet-Metal folder of a multibody part).
  - A convert in a part that already contains the original sheet-metal body sets thickness and radius. Whether these land on the body or on the template is not documented, and the template drives the original.
  - `OriginalUntouched` measures and `RemoveAgain` deletes `result.Feature` only (MirrorJob.cs l.474). The changed default stays, and so do the companions:
    - Imported;
    - SheetMetal;
    - SolidToSheetMetal;
    - FlatPattern, which in a multibody part sits inside the folders.
  - `RemoveLeftovers` sees only top-level features (Area 1 review, P2).
  - The draft describes three routes and two fixed-face attempts (l.303–311) but no cleanup between them. A convert that fails halfway consumes the imported body (`KeepBody` = false). The next attempt then starts from a different state.
- **Fix:**
  - Use Area 1's `SheetMetalGuard`: snapshot the template and the original's SheetMetal parameters before every attempt, compare them afterwards, and write them back on a difference.
  - Use Area 1's `SwSheetMetal.Snapshot`, which includes the contents of the folders. After every failed attempt, everything new since the attempt started is deleted (absorbed, two rounds) and checked to be gone.
  - The final result carries `RemoveHelpers`, which deletes all companions.
  - `CheckRebuildError` covers every companion, not only `result.Feature`.

### P5. A convert with `FindBends` and only a fixed face is unproven, and a known failure mode destroys the body
- **Evidence:**
  - Draft l.305: `InsertConvertToSheetMetal2(t, false, FindBends: true, r, gap, …)` with only the fixed face selected. The imported body has round bends, so there are no sharp bend edges to select.
  - CADapter measured on 2017 (`cadapter_sw_sheetmetal.py` l.462–487):
    - With the fixed face alone, the method "marks the part as sheet metal and DESTROYS everything but that face's wall".
    - The fixed face has to be the concave (inner) one.
    - `ReverseThickDir` = true "SHRINKS the part".
    - Marks and relief type are "INDIFFERENT".
    - CADapter always passed `FindBends` = false and selected sharp edges.
  - No source shows `FindBends` = true on a body that already has round bends.
  - **Inconsistent argument values:**
    - The help example passes `ReliefType` 0, which is not a value of `swSheetMetalReliefTypes_e` (1–5).
    - 0xFFD passes `OverlapType` 0; the help documents 1–3.
- **Fix:** see (2) §4.4.
  - The draft's success test (new SheetMetal + SolidToSheetMetal, `IsSheetMetal()`, volume, moment and samples unchanged) stays. It becomes local and always on: the mass before the attempt is compared with the mass after it, so the original's measurement is not needed.
  - Each attempt is cleaned up (P4).
  - **Attempts:**
    - A1: R(fixed face), `ReverseThickDir` false.
    - A2: the opposite face.
    - B1/B2: `InsertBends2` with the same two faces.
  - `ReliefType` comes from the original's `AutoReliefType` (1–5), and `OverlapType` = 1.
  - This is live test L3, the first test after L1.

### P6. The fallback is on by default, and its evidence is overstated
- **Evidence:**
  - Draft §5 l.374: `SheetMetalFallback {Off, PerBody=default}`.
  - The README promises "ein eigener, vollständig bearbeitbarer Feature-Baum ohne Verknüpfung zum Original", and ARCHITEKTUR's "Grundidee" says the same.
  - The fallback replaces every feature of a body up to k with one Imported body that cannot be edited. Functionally that is "Körper spiegeln" plus "Verknüpfung lösen".
  - No forbidden member is called, so the result is within the letter of the project rule. It does break the product promise, so the owner has to decide.
  - **"Real GitHub code uses this same pattern"** (l.12, l.27) is overstated:
    - tbcphutai `NativePartMirrorV7.cs` uses `Copy2` + `ApplyTransform` only to build the *expected* bodies for its check (`ReflectSolids`, l.205–231). It produces the actual result with `((PartDoc)working).MirrorPart2(true, options, out mirrored)` (l.86), which is a forbidden API.
    - Its own transform helper says "Never assume this is accepted" and keeps a second representation in case the direct one is refused.
    - RearHubRepair imports a body moved with a transform from a plan file (rigid) and never applies a reflection.
  - So no source shows a reflected temporary body imported into a part. That is live test L1/L2, and it decides whether the fallback exists at all.
- **Fix:**
  - `MirrorOptions.SheetMetalFallback` is a bool, default **false**, with a checkbox on the page:
    - group "Prüfung und Bezüge": „Blech: nicht nachbaubare Teile als importierten Körper übernehmen (nicht parametrisch)“;
    - the assembly path passes it through.
  - With the switch off, the P3 refusal names it.
  - Update README and ARCHITEKTUR: the fallback is the one place where a body is taken over as geometry, by an ordinary body transform and not by a mirror feature.
  - Correct the evidence table.

### P7. Snapshots depend on `VerifySteps`; the temporary body can be consumed; bodies are identified by name
- **Evidence:**
  - Draft l.329: "Snapshots are taken after every feature of a sheet-metal body during `MassCapture`".
  - `MassCapture.Measure` runs only with `VerifySteps` (MirrorJob.cs l.160) and skips every `UnsupportedRecord` (MassCapture.cs l.67).
  - With "Jeden Schritt prüfen" off, the build-time trigger has no snapshot at all. The trigger "verification weicht ab" needs `VerifySteps` anyway.
  - **`CreateFeatureFromBody3`** "creates a new imported feature from the specified temporary body". Whether the temporary body is still usable afterwards is undocumented.
    - The draft's restart reuses the snapshot (l.326), and so does every repeated attempt within one run (P4).
  - **Body names:**
    - The draft keys snapshots "by body" and lists "body names stay stable" as live test 17.
    - The code already documents that they do not: "bodies take the name of their last feature" (FeatureCollector.cs l.146–149, MassCapture.cs l.120–121).
    - At state k the body usually has a different name than in the final state.
- **Fix:** see (2) §4.2.
  - Add a separate `SheetMetalSnapshots.Take` pass after planning, independent of `VerifySteps`. It takes the capture-time trigger states plus the final state of every qualifying body.
  - Per-feature snapshots, used for build-time triggers, are taken inside `MassCapture` only when `VerifySteps` is on (cap 64).
  - **Identifying the body at state k:**
    - the single solid body whose `GetFeatures()` contains the trigger feature;
    - otherwise the single one whose history up to k equals the planned history;
    - otherwise no snapshot.
  - The store keeps the *mirrored* master body. Every use passes `master.Copy2(false)`. The masters are released in a `finally` at the end of the run.

### P8. The restart is underspecified
- **Evidence:**
  - `BuildContext` is built once (MirrorJob.cs l.177). It holds:
    - `Created`;
    - `Sketches`;
    - `KnownDimensions`;
    - `PartOwners`;
    - `PointSketches`;
    - the `PlaneResolver` caches and the helpers that failed to be created.
  - `ctx.Report.Add` writes straight into the run's report.
  - The progress bar has a fixed step count (l.140).
  - **Same part:**
    - "every feature created by the job is deleted in reverse order (the RemoveLeftovers pattern)" (l.327) needs a snapshot of the whole job, which does not exist. `RemoveLeftovers` takes the snapshot per step.
    - It also misses folder contents (P4).
  - The draft does not say how a restart interacts with:
    - "Beim ersten Fehler abbrechen";
    - Esc;
    - "Original löschen";
    - the cap of three restarts per run versus per body.
- **Fix:** see (2) §2.5. In short:
  - A job snapshot S0 is taken before the first step.
  - New part: `CloseDoc` and `CreatePart`. Same part: `RemoveLeftovers(S0)` with folder contents, followed by a check that the original's masses are unchanged.
  - A fresh `BuildContext` and a fresh report for the new pass; the first pass is summarised in one General line.
  - `RunProgress.Start` again.
  - No restart after Esc. With `StopOnError`, a restart replaces the abort only for a qualifying body.
  - At most three restarts per run.

### P9. Gusset without a reference point: the implicit reference is a bend *end*, and FlipOffset is the wrong toggle
- **Evidence:**
  - Help, `ReferencePoint`/`UseOffset`: "For insertion of the first gusset, the assumed reference point is on the end of the sheet metal bend. For second and subsequent gusset insertions, the assumed reference point is on the edge of the gusset last inserted."
  - `FlipOffset`: "offset … on the opposite side of the specified reference point".
  - The draft keeps the flag, offers "Versatz auf der anderen Seite" (l.261, l.266), and says only that "the bend line's internal direction is unknown".
  - **What goes wrong:**
    - Which end is "the end" depends on how the copy's bend runs, and that B-rep direction is arbitrary.
    - If it runs the other way, the gusset lands at L − d instead of d (checks.py #4).
    - Flipping the side of the same reference point does not help: it moves the gusset off the bend.
    - Every later gusset on that bend inherits the error through "the edge of the gusset last inserted".
- **Fix:** see (2) §7.
  - **Capture:** record the actual section position (centroid of the gusset's own faces, projected onto the bend line) and both bend ends.
  - **Copy:**
    - If the original has a `ReferencePoint`, use the mirrored one.
    - Otherwise set an explicit `ReferencePoint`: the copy's vertex at R(implied end), found by fingerprint among the vertices of the bend's edges, with the original's offset.
    - If no such vertex exists, offer the alternative "Versatz vom anderen Biegungsende" (offset L − d) and, for later gussets, "Versatz vom vorigen Knotenblech".
  - A local check is always on: the section position of the new gusset has to equal R(original) within 10 µm.

### P10. Forming tools: the documented sketch pre-selection is ignored, and variants A and B are guesswork
- **Evidence:**
  - Help remarks of `InsertFormToolFeature` (offline catalogue): "select either a face or a 2D sketch containing points … sketch containing points, an instance … is placed at each point in the sketch."
  - The draft selects only the face (l.203). It then writes the position sketch afterwards (variant A, `BuildInto` into the generated sketch) or moves the generated entities (variant B, 0xFFD `MoveOrCopy`).
  - 0xFFD shows that with a face selected the tool lands at the face's sketch origin, not at the pick point (ffd_tools_sheetmetal.py l.687–704). The first insert is therefore placed arbitrarily, and the registration in step 3 has to be measured about the instance's actual insertion axis, not about R(P₁).
  - 2019 help (search summary, unverified): "Forming tool features have two different sketches. The first sketch sets the location, and the second sketch sets the orientation." Also: "The angle can be changed in the PropertyManager or while editing the profile sketch."
  - The second sketch therefore holds the rotation as sketch geometry. Comparing points of the mirrored original sketch and the copy's sketch gives ψ cheaply and exactly, better than the draft's frame-axes guess H-FT1.
- **Fix:** see (2) §5.
  1. Select the mirrored face (`SelectFaceAt`), `InsertSketch`, close the sketch, then `SketchBuilder.BuildInto(rec.Position, sketch)`. Stand-ins are endpoints of fixed construction lines (STOLPERSTEINE 79).
  2. Select that sketch with mark 0, then `InsertFormToolFeature`.
  3. Check that the sketch was absorbed and that the number of instances equals the number of points.
  4. Only if this route returns null: face selection plus variant A.
  - **ψ:**
    - First guess: 2D registration of the points of the mirrored original orientation sketch against the copy's orientation sketch, both in model coordinates via `SketchFrame`.
    - Confirmation: the 3D registration on face samples from the draft.
  - **Rotation sense:** cached per run after the first successful re-insert, so at most two inserts per later tool.

### P11. Forming tools: tool configuration and path, external reference, wrong tool reported as "asymmetric"
- **Evidence:**
  - The draft reads the tool configuration from `ListExternalFileReferences2(…, out ConfigName)` (l.139).
  - The help: `ConfigName` is the "Name of the configuration when `ConfigOption` is `swExternalFileReferencesNamedConfig`". That describes the referencing configuration, not the configuration of the tool, so in 2019–2024 the tool configuration cannot be read.
  - A tool with several configurations (sizes) placed with the wrong one matches no ψ. The draft then reports it as chiral and starts the fallback.
  - The library search by feature name without the trailing digits (l.139) can find a different tool with the same name, with the same result.
  - A linked tool (`LinkToPart`) gives the copy an external reference to the tool file. That is correct, as in the original, but the code's description of the run says "no external reference" (MirrorJob.cs l.52–56).
  - `MirrorTool` (phase 2) saves `<Werkzeug>_gespiegelt.sldftp` "next to the target". A new part has no folder yet, because it is saved only in the assembly path.
- **Fix:**
  - Candidate configurations come from `ISldWorks.GetConfigurationNames(path)`: first `""` (default), then the others, until the registration passes.
  - Report text when nothing fits: „Werkzeug passt nicht (andere Datei oder Konfiguration, oder unsymmetrisches Werkzeug)“.
  - A link to the tool is mentioned in the details.
  - `MirrorTool` is deferred until there is a defined save location (same folder as the tool, with a confirmation question).

### P12. Tab and slot: details the draft misses
- **Evidence:**
  - The help example's sample is a multibody sheet-metal part (`samples\whatsnew\sheet metal\tab_and_slot.sldprt`), where the tab edge and the slot face belong to different bodies. Tab and slot therefore always changes two bodies, which matters for P2 and the `BodyScope`.
  - The example names only "Tab and Slot-Tab1". The slot feature's type name, and whether its definition answers the interface, are unknown. If it does not, it becomes an `UnsupportedRecord` with `ChangesSolid` true: a spurious failure and a spurious fallback trigger.
  - Tab and slot made in an assembly context carries external references, which the draft does not check.
  - The 2019 interface has no member for the slot corner type or for the slot length and width offsets (only `SlotClearance`). The copy gets defaults.
  - The draft's rule for `TabReverseDirection` is right: the help measures the offset from the slot face, whose outward normal the mirror keeps outward.
- **Fix:**
  - **Capture:**
    - `ListExternalFileReferencesCount() > 0` → `NotSupported("Zapfen und Schlitz im Kontext einer Baugruppe …")`.
    - The slot feature is found as a child of the tab feature (`GetChildren`, type checked) or by the "-Slot" name, and becomes a `PartOfFeatureRecord`.
    - Always the note „Schlitz-Optionen (Ecken, Längen-/Breitenversatz) sind über die API nicht lesbar – Standardwerte“.
  - **Build:** both bodies must be in the run. Otherwise the slot side is the other body, and the existing `OtherBodies` handling applies.

### P13. Vent: "always the fallback" is too pessimistic, and vents are not limited to sheet metal
- **Evidence:**
  - The vent is a fastening feature for any part ("The SOLIDWORKS Vent Feature is not just for Sheet Metal", Javelin). Its ribs, spars and fill-in boundary can have their own depth and offset, plus draft and fillet (Vent PropertyManager, search summary).
  - Today a vent on a plastic part becomes "Feature-Typ … wird nicht unterstützt" (Failed), and the draft changes nothing there.
  - On a sheet-metal body the draft turns every vent into a fallback trigger. A vent usually comes late in the tree, so the whole body becomes non-parametric.
  - The vent's sketch (boundary, ribs, spars) is an ordinary absorbed `ProfileFeature` and readable.
  - On a flat sheet face, a vent without draft and without rib or spar depth or offset is a prismatic cut through the thickness. Its geometry is the set of new inner loops it leaves on the placement face.
- **Fix:** an optional phase 5, "Ersatzschnitt"; see (2) §8.
  - Capture the new loops of the placement face against the state before the feature, plus the depth.
  - Build them as a mirrored sketch on the mirrored face and a Cut-Extrude normal to the face.
  - Accept only with an `ExpectedChange` and a passing check. Otherwise delete it and fall back.
  - The report says what it is: „als Schnitt mit fester Kontur nachgebaut“.
  - Without phase 5, the draft's handling stays. The vent type name is not needed, because any unsupported solid-changing record on a sheet-metal body is a trigger.

### P14. Integration with Areas 1 and 2: companions, flat pattern, convert in the original
- **Evidence:**
  - Records after k stay parametric, including the body's FlatPattern (always last).
  - Area 1's `FlatPatternRecord` expects the companions of its own base flange (Area 1 review §2, §4).
  - In the copy the SheetMetal and FlatPattern come from the convert. With route B (`InsertBends2`) there is no FlatPattern at all, only FlattenBends/ProcessBends.
  - The Area 1 review moves `SolidToSheetMetal` *in the original* to Area 4 (its P17). The draft treats it only as an output.
  - `IConvertSolidFeatureData` exists only from 2025 SP2, so in 2019–2024 an original convert can only be a fallback trigger. The body is then imported in its state after the convert.
  - Sheet-metal types are defined three times across the four drafts (`SheetMetalParameters` and others).
- **Fix:**
  - The fallback builder registers its new SheetMetal (and FlatPattern) as the companions of the body under the original names of the covered SheetMetal and FlatPattern. Area 1's flat-pattern builder then finds them.
  - With route B, the FlatPattern record of the body is also covered: „Abwicklung über Biegungen einfügen (älteres Verfahren)“.
  - `SolidToSheetMetal`, `FlattenBends` and `ProcessBends` in the original are triggers, as long as no Area 1 phase 1b builder exists.
  - Use Area 1's shared types.

### P15. Capture safety and the places that need the hole-part generalisation
- **Evidence:**
  - `Capture` catches only `NotSupportedException` (FeatureCapture.cs l.92). Any other exception aborts the run before anything is built. The new capture code is prone to such exceptions:
    - file-system search;
    - late binding;
    - rollbacks ("ShapeSamples rolled back after f", "PlacementFace rolled back before f").
  - The Area 1 review found the same (its P13).
  - A forming tool's definition is null in 2019 (live test L9). Its case must therefore come before `switch (def)`.
  - The draft names only `HolePart`/`CaptureOnItsOwn`. The hole special case also lives in:
    - `MirrorJob` l.150 (owners of parts);
    - l.204 (Esc withdraws parts);
    - l.474 (`RemoveAgain` with `withAbsorbed`);
    - `IsPositionedHole` in the tree walk of `HolePart` (FeatureCapture.Holes.cs l.31).
- **Fix:**
  - `CaptureFormTool`, `CaptureTabAndSlot` and `CaptureSMGusset` wrap their bodies in `catch (Exception ex) when (!(ex is NotSupportedException))` → `NotSupportedException`.
  - Rollbacks are in `try`/`finally` with `EditRollback(End)`.
  - Generalise on `PositionedFeatureRecord` and `BringsParts` everywhere listed above.

### P16. `IBody2.Save`/`IModeler.Restore` are unnecessary and would not work as described
- **Evidence:**
  - `AssemblyMirror.Mirror` runs the whole `MirrorJob.Run` (capture and build) with the source open, and closes it only afterwards in the `finally` (AssemblyMirror.cs l.105–113).
  - `IModeler.Restore` expects an "IStream interface for storage inside the SOLIDWORKS document", obtained via `IGet3rdPartyStorage`. That is not a free in-memory stream.
- **Fix:** drop step 5 of §4 and the related live test.

### P17. Smaller inaccuracies
- **"Every member … compared against 32.1.0":** 32.1 adds `ISheetMetalFeatureData.UseMaterialSheetMetalParameters`. The Area 1 review handles it.
- **Obsolete members:**
  - `InsertSheetMetalGussetFeature` (v1) is marked obsolete, superseded by `…3`. It is still present in both versions and is the measured working one.
  - `ISheetMetalFeatureData.KFactor` is obsolete.
- **The 2025 SP2 `ILibraryFormToolFeatureData` help** requires "the Sheet Metal tab is visible on the CommandManager". That is a UI dependency of the late-bound path; add a live test.
- **`AsymmetricFormTool`:** §2 describes `Report` as "fails, and the fallback takes over", while §5 lists `Report`, `Fallback` and `MirrorTool` as separate values. Without phase 2 there is nothing to choose. Drop the option until then.
- **`CoveredByFallbackBuilder`** "returns Warning without a feature, HasEquivalent". `BuildResult` has no such factory; `HasEquivalent` can only be set by `AlreadyThere`, which gives Skipped. Add `BuildResult.Covered(message)`.
- **`MirrorOptions` additions** need the PropertyManager page (`MirrorPage`) and the assembly path. The draft names neither.
- **"Expected cost … about 1 s":** fine. The coarse registration pass should take samples far from the axis, because a sample near the axis carries no rotation.
- **Vertex-set check (`PointSets.Unmatched`):** sheet-metal bodies have thousands of vertices. Use a spatial grid, not O(n²).
- **Effort (12–16 days):** optimistic once the guard, the cleanup, the planner on full histories and the restart are included. See (2) §12.

---

## (2) Corrected design

This section supersedes `blech-umformwerkzeuge-ersatzweg.md`. Where it says "as in the draft", that part of the draft stands.

### 0. Answer to the user (German draft)

„Teilweise ja. Laschen mit Zapfen und Schlitz und Blech-Knotenbleche lassen sich in SOLIDWORKS 2019 parametrisch nachbauen. Umformwerkzeuge auch, wenn das Werkzeug spiegelsymmetrisch ist, was für die Werkzeuge der Konstruktionsbibliothek gilt. Die Drehung wird dabei gemessen, weil die API sie vor 2025 nicht liefert. Lüftungsöffnungen haben keine API; flache Lüftungen ohne Formschräge können als Schnitt mit fester Kontur nachgebaut werden.

Was sich nicht nachbauen lässt, kann auf Wunsch (eigener Schalter, standardmäßig aus) als importierter, gespiegelter Körper übernommen und wieder in Blech umgewandelt werden. Alles danach bleibt parametrisch. Das ist kein Spiegel-Feature und hat keine Verknüpfung zum Original, aber die übernommenen Features sind nicht mehr einzeln bearbeitbar, und die Biegezugabe lässt sich dabei womöglich nicht übernehmen. Der Bericht sagt beides.“

### 1. API used (27.3 unless marked late-bound)

| Purpose | Members | Note |
| --- | --- | --- |
| Body copy | `IBody2.Copy2(false)`, `IMathUtility.CreateTransform(Reflection.ToAffine().ToSwArray())`, `IBody2.ApplyTransform`, `Check3`, `GetMassProperties(1)`, `GetVertices` | Second representation `(−M, scale −1)`, verified with probe points before use |
| Import | `IPartDoc.CreateFeatureFromBody3(copy, false, swCreateFeatureBodyCheck)` | Always a fresh `Copy2` of the master |
| Convert | `IFeatureManager.InsertConvertToSheetMetal2(t, false, true, r, gap, relief(1–5), ratio, 1, overlapRatio, false)` | Entities via `IEntity.Select4`; the return value is ignored |
| Insert bends | `IPartDoc.InsertBends2(r, table, K or −1, BA or −1, autoRelief, clamp(ratio, 0.05, 2.0), true)` | Allowance at creation |
| Allowance | Area 1 `SwSheetMetal.WriteAllowance` (`SetOverrideDefaultParameter2`, `SetCustomBendAllowance`, return codes checked) | Read back; not trusted |
| 2025 SP2+ | late-bound `CreateDefinition(swFmFormToolInstance)` + `ILibraryFormToolFeatureData`; `IConvertSolidFeatureData.Initialize` | Numeric id of the convert read live (L19) |
| Forming tool | `InsertFormToolFeature(path, flip, angle, config, false, false, false, false, link)` with a pre-selected position sketch; `ISldWorks.GetConfigurationNames`; `IFeature.ListExternalFileReferences2` (path only) | — |
| Tab and slot | `CreateDefinition(88)`, `SelectionAddNewGroup`, group setters, `CreateFeature`; `SelectionGetGroups` + `ModifyDefinition` | Help example |
| Gusset | `InsertSheetMetalGussetFeature` (v1, legs mark 0), then `ISMGussetFeatureData` edit for `ReferenceLine`/`ReferencePoint`/display | v2 as second attempt |

The design calls no `InsertMirrorFeature*`, no `MirrorPart*`, no `InsertMirrorAll`, no `MirrorComponents` and no derived part.

### 2. Run flow (`MirrorJob`)

1. **Pre-capture refusal** (`UnsupportedFeatures.RefusesBeforeCapture`): `MirrorStock`, and carrier types without a builder when `SheetMetalFallback` is off.
2. **Capture as today.** New cases:
   - `FormToolInstance`, by type name, before `switch (def)`;
   - `TabAndSlotFeatureData`;
   - `SMGussetFeatureData`, before the generic default.
   - All of them are exception-safe (P15).
3. **Per-body histories** of all solid bodies (P2), then **planning** (`FallbackPlanner.Plan`, Core).
4. **Post-planning refusal** (`FallbackPlanner.Refusal`): an unsupported carrier on a sheet-metal body that no plan covers refuses the run. Nothing has been built and no new part has been created yet.
5. **Snapshot pass** (`SheetMetalSnapshots.Take`, under `UpdateFreeze`, independent of `VerifySteps`): one mirrored master per planned body at its trigger, plus its final state.
6. **`MassCapture`** as today. With `VerifySteps` it also takes per-feature masters of qualifying bodies, at most 64 per run.
7. **Build** with `records = plan.Apply(records)`:
   - a `SheetMetalFallbackRecord` at the position of the first covered record;
   - `CoveredByFallbackRecord`s for the covered records.
8. **Restart (P8):** possible only for a qualifying body, when one of its sheet-metal records ends Failed or its check ends "weicht ab".
   - Plan again with the smallest available snapshot ≥ j.
   - New part: `CloseDoc` + `CreatePart`. Same part: `RemoveLeftovers(S0)` with folder contents, then check that the original's masses are unchanged.
   - Fresh `BuildContext` and report; `RunProgress.Start` again.
   - No restart after Esc. At most three per run. After that the final state, if that snapshot exists.
   - General line: „Erster Durchgang: „X“ scheiterte (…), Körper „…“ ab dort über den Ersatzweg.“
9. **Finish as today.** Covered entries are warnings and therefore block „Original löschen“.

### 3. Records (Core)

`Spiegelwerk.Core/Records/SheetMetalArea4Records.cs` uses Area 1's `SheetMetalParameters`, `BendAllowanceData`, `ReliefData` and `GaugeData`. It defines no copies of its own.

```csharp
public abstract class PositionedFeatureRecord : FeatureRecord        // PositionedHoleRecord derives from it
{ string PositionSketchName; SketchModel Position; List<Vec3> Points; GeometryRef? Face; }

public enum ToolSource { ExternalReference, LateBound, LibrarySearch }
public sealed class FormToolRecord : PositionedFeatureRecord          // "FormToolInstance"
{ string OrientationSketchName; List<Vec3> OrientationPoints;         // second sub-sketch, model coordinates
  string ToolPath; string? ToolConfiguration; bool LinkToTool; ToolSource Source;
  double? RotationAngle; bool? OverrideDoc, ShowPunch, ShowProfile, ShowCenter;
  Vec3 PlacementNormal;                                                // outward normal of the placement face
  List<Vec3> ShapeSamples; List<int> SampleInstance; int ProtrusionSide; }

public sealed class TabSlotGroup { /* as in the draft */ }
public sealed class TabAndSlotRecord : FeatureRecord { List<TabSlotGroup> Groups; string? SlotFeatureName; }

public sealed class SheetMetalGussetRecord : FeatureRecord           // "SMGusset"
{ List<FaceSignature> SupportingFaces; GeometryRef? ReferenceLine, ReferencePoint;
  Line3 BendLine; Vec3 BendEndA, BendEndB; Vec3 SectionPoint; int OrderOnBend;
  /* every ISMGussetFeatureData field 1:1 */ }

public sealed class VentCutRecord : FeatureRecord                    // phase 5, optional
{ FaceSignature PlacementFace; SketchModel Loops; double Depth; string? VentSketchName; }

public sealed class SheetMetalFallbackRecord : FeatureRecord         // name "Spiegelwerk-Ersatz <Körper>"
{ string BodyKey; string? AfterFeature; bool FinalState;
  SheetMetalParameters Parameters; List<string> BendOverrides;        // names of bends with their own allowance
  FaceSignature FixedFace; Plane3 FixedPlane;                         // read at state k, see §4.2
  MassState SnapshotMass; int VertexCount;
  MassState? FlatChange;                                              // only when FinalState and measured (Area 1 §9)
  List<string> Covered; string Reason;
  string? SheetMetalName, FlatPatternName; }                          // original companion names (P14)

public sealed class CoveredByFallbackRecord : FeatureRecord { string FallbackName; public override bool ChangesSolid => false; }
```

- `ExpectedChange` of the fallback record is `SnapshotMass`, read from the temporary body, so it is available without `VerifySteps`. `FaceSamples` holds up to 8 points on the snapshot's faces.
- `MirrorOptions.SheetMetalFallback` (bool, default false). There is no `AsymmetricFormTool` option until phase 2 (P17).

### 4. Fallback

#### 4.1 Planner (`Spiegelwerk.Core/Planning/FallbackPlanner.cs`)

- **Input:**
  - records with kind (rebuildable, unsupported, sketch, part-of) and `ChangesSolid`;
  - consumers of each sketch;
  - `BodyHistory` for every solid body (name, `IsSheetMetal`, ordered solid features, selected);
  - the mode (whole part, bodies, features only);
  - the switch.
- **Body qualifies:**
  - the switch is on and the mode is not "features only";
  - it is sheet metal;
  - every feature of its history is collected;
  - the shared-feature rule of P2 holds;
  - with selected bodies, no covered record has `OtherBodies`.
- **Trigger k:** the last unsupported solid-changing record of its history. A forming tool whose capture failed is unsupported, so it counts.
- **Plan:**
  - covered = every record of the history up to k;
  - a sketch is covered only if all of its consumers are;
  - part-of records follow their owner;
  - the fallback record goes at the position of the first covered record.
- **Refusal:** an unsupported carrier record (first solid feature of a sheet-metal history, or a carrier type) that no plan covers.
- **Restart:** `Replan(plan, body, failedIndex, availableSnapshots)` returns the smallest snapshot ≥ failedIndex, or none.

#### 4.2 Snapshots (`Capture/SheetMetalSnapshots.cs`)

- For each planned key, roll back to after k and find the body as in P7. Then:
  - `Copy2(false)`;
  - transform with `T` (direct array first; `(−M, scale −1)` if the five probe points do not map);
  - `ApplyTransform`;
  - check `Check3.Count == 0`, volume equal and > 0, and the vertex set equal to R(original vertices) (spatial grid);
  - read `SnapshotMass`, `FaceSamples` and `VertexCount`;
  - choose `FixedFace`/`FixedPlane`.
- **Fixed face:**
  - the planar face of the body at state k that lies on the plane of the original flat pattern's `FixedFace2` (read in the final state) and contains its sample point;
  - otherwise `SheetMetal.FixedReference` (Area 1 `FaceOrEdgeRef`);
  - otherwise the largest planar face;
  - each with a note.
- **Final state:** no rollback needed.
- The master is kept in an add-in-side `FallbackSnapshotStore` and released in a `finally` at the end of the run.
- Without masters (L1 fails), there is no plan. The planner gets "no snapshots", so the refusal of P3 applies.

#### 4.3 Builder (`Build/Builders/SheetMetalFallbackBuilder.cs`)

1. `SheetMetalGuard.Begin` (Area 1) and `SwSheetMetal.Snapshot` (top level and folder contents).
2. **Import:** `CreateFeatureFromBody3(master.Copy2(false), false, Check)`.
   - Exactly one new solid body.
   - Its mass equals R(`SnapshotMass`) (`MassComparison`, local).
   - `ListExternalFileReferencesCount() == 0`.
   - `ctx.Register(record.Name, imported)`.
   - On failure: remove the new features and fail with „Import des Ersatzkörpers abgelehnt“.
3. **Conversion attempts** in order A1, A2, B1, B2 (§4.4). Each attempt:
   - starts from a new feature snapshot;
   - selects with `Select4`;
   - is evaluated by the local check: one body, `IsSheetMetal()`, mass unchanged against the mass before the attempt, all snapshot samples hit, `GetErrorCode2 == 0` for every new feature, guard unchanged.
   - On failure: delete the new features (absorbed, two rounds), check that they are gone, `SheetMetalGuard.Restore` if needed, then the next attempt.
   - After the last failed attempt: F3, the imported body only, with status Warning: „kein Blechkörper – Abwicklung fehlt; folgende Blech-Features scheitern“.
4. **Allowance, gauge, relief:** §4.4.
5. **Flat-state check** (`FinalState` and `FlatChange` present): Area 1 §9. Otherwise the detail „Abwicklung nicht geprüft“.
6. **Companions:** register the new SheetMetal, SolidToSheetMetal, FlatPattern (or FlattenBends/ProcessBends) as group members. `RemoveHelpers` deletes them all. Map them to the original names `SheetMetalName`/`FlatPatternName` for later records (P14).
7. **Result:** at best Warning, by design. The details give:
   - the route (A1/A2/B1/B2/F3);
   - the thickness;
   - the radius;
   - the allowance as achieved;
   - the covered features.

The `CoveredByFallbackBuilder` returns `BuildResult.Covered("Im Ersatzkörper „…“ enthalten – nicht parametrisch")`: Warning, `HasEquivalent`, no feature.

#### 4.4 Attempt order and allowance

| Attempt | Call | When it is kept |
| --- | --- | --- |
| A1 | convert, fixed face R(FixedFace), `ReverseThickDir` false, `FindBends` true, `ReliefType` from `AutoReliefType`, `OverlapType` 1 | local check passes |
| A2 | as A1 with the opposite face (the face whose plane is FixedPlane offset by −n·t) | local check passes |
| B1/B2 | `InsertBends2(r, table, K or −1, BA or −1, autoRelief, clamp(ratio, 0.05, 2.0), true)` with the same two faces at mark 0 | local check passes |

- **After A1/A2:** write the allowance with Area 1's checked path and read it back.
  - If it matches, done.
  - If not, and the original allowance is not the one the convert produced: run B1/B2 as well. Keep B if its read-back matches and its local check passes; otherwise go back to A, re-created, with the warning from P1.
- **Gauge table:** `SetUseGaugeTable` only in a new part, and only on the first sheet-metal body (Area 1 P1: one gauge table per part).
- **Relief:** `AutoReliefType`/`ReliefRatio` through the same write path, read back. A mismatch gives a note only; the relief cuts are already in the geometry.
- **2025 SP2+:** before A1, the late-bound `CreateDefinition(id)` + `IConvertSolidFeatureData.Initialize(false, gauge, cba)` + `SetFixedFace` + `CreateFeature`.

#### 4.5 Report

- **General line:**
  > Ersatzweg für Körper „Blech1“: Stand nach „Lüftung1“ als importierter Körper übernommen (gespiegelt per Körpertransformation, kein Spiegel-Feature, keine Verknüpfung zum Original), in Blech umgewandelt (A1; Dicke 2 mm, Radius 1 mm, K 0,44 übernommen). Grund: Lüftung1 – keine API. Nicht parametrisch: Basis-Blech1, Kante-Lasche1, … Danach wieder parametrisch: …
- **Covered entries:** Warning.
- **Fallback entry:** Warning, with the verification text (local check, `StepVerifier` with `VerifySteps`, flat check).

### 5. Forming tools

- **Capture (`FeatureCapture.FormTool.cs`):**
  - **Sketches:** the first `ProfileFeature` sub-feature is the position sketch; it becomes `PartOf` (owner = the tool) and is read in full. The second is the orientation sketch; it also becomes `PartOf`, and only its points are kept, in model coordinates.
  - **Path:**
    - from `ListExternalFileReferences2` (path only);
    - otherwise late-bound `FormToolPath`;
    - otherwise the library search in folders 38/4.
  - **Configuration:** late-bound only (2025 SP2+). Otherwise null, and the candidates come at build time from `GetConfigurationNames`.
  - **`RotationAngle` and display flags:** late-bound when available.
  - **Rolled back before f:** placement face and outward normal.
  - **Rolled back after f:** up to 24 shape samples on `f.GetFaces()`, assigned to instances by their nearest insertion point and preferring samples far from the axis; also `ProtrusionSide`.
  - Exceptions → `NotSupported` (P15). A tool file that cannot be found → `NotSupported("Werkzeugdatei nicht gefunden")`, a trigger.
- **Build (`FormToolBuilder`):**
  1. Find the mirrored placement face as a face; a substitute plane is not allowed. In the same part, only own bodies.
  2. Write the position sketch as in P10: `InsertSketch` on the face, `BuildInto`, close.
  3. **Probe insert:** sketch selected at mark 0. `InsertFormToolFeature(path, flip = original or false, θ0 = 0, config candidate, false, false, false, false, link)`.
     - If it returns null, use face selection plus variant A.
     - Check absorption and the instance count.
  4. **Flip:** `ProtrusionSide` of the new instance against the mirrored samples. If wrong, repeat 3 with the other flip.
  5. **ψ:**
     - first guess from the 2D registration of `OrientationPoints` (mirrored) against the copy's orientation sketch;
     - then the 3D registration (draft step 3) about the new instance's actual insertion axis;
     - the smallest |ψ| wins.
     - No ψ with any configuration candidate → Failed with the P11 text, and the feature becomes a trigger for a qualifying body.
  6. **Final insert** with θ0 + s·ψ, where s is the cached rotation sense (first time: + then −). In 2019 the probe is deleted first (absorbed); in 2025 SP2+ use `RotationAngle` + `ModifyDefinition` instead.
  7. Check again: 3D registration with ψ = 0 and all samples within 10 µm.
  8. Register. The position sketch maps to `rec.PositionSketchName`, as for holes.
- **Mirror rules:**
  - the tool part is not mirrored;
  - flip unchanged (outward normals stay outward);
  - θ′ = β − θ − 2α, found by measurement;
  - the position sketch is mirrored like a hole's;
  - display flags and link as in the original.

### 6. Tab and slot

As in the draft (capture via `TabAndSlotFeatureData`; build with `CreateDefinition(88)` → groups → `CreateFeature` → one `FeatureEdit.TabAndSlot`), plus P12:
- **Capture:**
  - in-context check;
  - slot companion as `PartOf`;
  - the note on slot options;
  - `TabFace`, edge and slot face read under `AccessSelections` as fingerprints.
- **Mirror rules:**
  - fingerprints are mirrored;
  - D1/D2 swapped only without reference points and with a reversed edge (`TabSlotRules.Offsets`, using `EdgeLocation.Map`);
  - `TabReverseDirection` unchanged (offset from the slot face, whose outward normal stays outward);
  - lengths, spacing, clearance and edge treatment unchanged.
- **Alternatives:** "D1/D2 getauscht", and "Laschenversatz umgekehrt" for `OffsetFromSurface` only.
- **Same part:** both bodies are own bodies, otherwise fail. Without a feature scope, the guard checks the original.

### 7. Gusset

As in the draft (legs at mark 0 via `Select4`, v1 with 21 arguments, angles in radians, `BDraft` = `DraftSideFaces`; edit afterwards for references and display; v2 as second attempt), plus P9:
- **Capture:**
  - `BendLine` and its ends (the edges of the bend's cylindrical face next to the legs);
  - `SectionPoint` = the centroid of the gusset's own faces (`GetFaces`, area-weighted samples) projected onto the bend line;
  - for a gusset without `ReferencePoint`, the implied end: the bend end, or the previous gusset's section, whose distance to `SectionPoint` equals `OffsetDistance`, otherwise none.
- **Build:**
  - after v1 succeeds, set `ReferencePoint` explicitly to the copy's vertex at R(implied end), or keep the original's mirrored `ReferencePoint`;
  - `FlipOffset` follows σ of the reference line (`FlagForReference`) when there is one, and stays unchanged otherwise;
  - local check of the section position.
- **Alternatives:** "Versatz auf der anderen Seite", "Versatz vom anderen Biegungsende" (offset L − d), and "Schenkel d1/d2 getauscht" for the profile-dimension scheme only.
- **`ProfileType` = custom (1):** NotSupported until L13 shows what it means.

### 8. Vent

- **Phase 4 (required):** a vent on a sheet-metal body is a trigger, like any unsupported solid-changing record. Elsewhere it stays Failed. The reason text is keyed to a type-name guess only for the wording.
- **Phase 5 (optional), "Ersatzschnitt":**
  - **Accept only if:**
    - the feature has exactly one absorbed 2D sketch;
    - all of its own faces are perpendicular to that sketch's plane (no draft);
    - its own faces span exactly the depth between two parallel faces of the body (no rib or spar offsets);
    - `ExpectedChange` exists and is a pure removal.
  - **Capture:** the new inner loops of the placement face (state after f against before f) as a `SketchModel` in the sketch frame, plus the depth.
  - **Build:** a mirrored sketch on the mirrored face, then a Cut-Extrude normal to the face with blind depth = `Depth` (in sheet metal with "Normal cut" and "Link to thickness" where Area 1 supports it).
    - The check must pass, otherwise delete it and fall back to the trigger.
    - The vent sketch itself is also built as an ordinary sketch for the user, as a construction aid.

### 9. Mirror rules (det −1)

| Item | Rule |
| --- | --- |
| Fallback body | R applied exactly to the B-rep. Bend direction, inside/outside position, reliefs and rips are in the geometry. |
| Bend allowance, K-factor, table, gauge | Unchanged by mirroring. Carried only as far as the API lets them be written (P1). |
| Fixed face | R(original fixed face). The flat pattern becomes the mirror image of the original's, seen from the same side. Grain direction is not readable in 2019, so it gets a note. |
| Thickness direction (convert) | Material is behind the fixed face. `ReverseThickDir` false; the other side is attempt A2. |
| Forming tool | Tool unmirrored (it must be symmetric). Flip unchanged. Rotation θ′ = β − θ − 2α, measured. |
| Tab and slot | D1/D2 swapped only with a reversed tab edge and no references. Reverse direction unchanged. |
| Gusset | Leg order kept, so `FlipDimSides` unchanged (toggle offered). Implicit end mapped explicitly (P9). `FlipOffset` follows σ of the reference line. |
| Vent substitute cut | Loops mirrored like any sketch (σ from `DirectionRules`). Depth unchanged. |

### 10. Same part, new part, assembly

- **New part:**
  - The fallback import is the first body or an additional one.
  - The convert creates the template if there is none yet. Allowance writes follow Area 1 (P9, first body).
  - The restart closes and recreates the part.
- **Same part:**
  - The import is a separate body that does not merge (RearHubRepair evidence; L2).
  - Every attempt runs under `SheetMetalGuard`, and the original-body guard runs as usual.
  - A changed template is written back. If that fails, the run is aborted with a report line (the original is changed; undo with Ctrl+Z).
  - The restart deletes all job features from S0 and checks the original.
  - Covered entries block „Original löschen“. Area 1 P8 refuses it anyway when sheet-metal records were built.
- **Assembly:**
  - As a new part. The source stays open during the whole job, so no Save/Restore is needed (P16).
  - A linked forming tool keeps its reference to the tool file.
  - A restart reuses the same `newPath`.

### 11. Failure and cleanup

- Every builder that creates more than one feature (fallback, forming tool with probe, gusset with edit, tab and slot with slot companion) works with a feature snapshot that includes folder contents.
- On failure it deletes everything new since its start (absorbed, two rounds), checks that it is gone, and reports what remains („bitte von Hand löschen“).
- `RemoveHelpers` covers all companions. `CheckRebuildError` covers group members.
- Temporary masters are released at the end of the run, also after an exception.

### 12. Implementation plan

| Phase | Content | Size |
| --- | --- | --- |
| 0 | Prerequisites from Area 1 (`SheetMetalGuard`, `SwSheetMetal` snapshot/allowance/companions, `FlatChange`) and Area 2 (per-type refusal) | from those areas |
| 1 | Core:<br>- `FallbackPlanner` (full histories, shared-feature rule, refusal, replan)<br>- records<br>- `FormToolPlacement` (rule, 2D/3D registration, protrusion side)<br>- `TabSlotRules`<br>- `GussetRules` (implied end, L − d)<br>- `ReflectionArrays`/`PointSets` (grid) | 2–3 days |
| 2 | Run flow:<br>- refusal before and after planning<br>- snapshot pass<br>- `BuildResult.Covered`<br>- restart with S0, fresh context and report<br>- option + page checkbox | 2–3 days |
| 3 | `SheetMetalFallbackBuilder` (A1/A2/B1/B2/F3, local checks, allowance, companions), behind the switch | 3–4 days |
| 4 | Gusset and tab and slot (capture, builders, edits, alternatives) | 2–3 days |
| 5 | Forming tools (capture with rollbacks, sketch pre-selection, probe/final insert, registration, configuration candidates) | 3–4 days |
| 6 | Optional vent substitute cut | 2 days |
| 7 | Docs: README (table, „Noch nicht“, switch), ARCHITEKTUR (fallback, refusal), STOLPERSTEINE 139 and new rows, live-test table | 1 day |

Total 15–20 developer days plus 3–4 days of live tests. L1–L3 come first; if L1 fails, phases 2–3 are dropped and the refusal stays.

### 13. Core tests (about 50)

- **`FallbackPlannerTests`**
  - a vent in the middle covers the prefix and keeps the tail;
  - the record goes at the first covered position;
  - sketch coverage only when every consumer is covered;
  - part-of records follow their owner;
  - **whole-part mode:** a tab and slot shared by A and B before k gives no plan (P2); shared after k gives a plan;
  - a body split by a cut gives no plan; a merged body gives no plan;
  - **selected bodies:** a covered record with `OtherBodies` gives no plan;
  - features-only mode gives no plan;
  - the switch off gives no plan;
  - **refusal:** an uncovered carrier refuses; an uncovered leaf does not;
  - **replan:** the smallest snapshot ≥ failed index; none; final state.
- **`RefusalTests`:** the new two-step refusal. `RefusesBeforeCapture("SheetMetal")` with the switch off and on; `MirrorStock` is always refused. Update `ReportTextTests` l.79–90.
- **`FormToolPlacementTests`**
  - θ′ = β − θ − 2α against random frames (port of checks.py #1);
  - α = 0 → −θ; α = π/2 → π − θ; β = π;
  - 3D registration finds ψ for a synthetic L-tool and returns none for a chiral one (checks.py #2);
  - a rotationally symmetric tool → smallest |ψ|;
  - 2D registration of orientation points;
  - `ProtrusionSide`;
  - samples per instance; samples near the axis are discarded.
- **`TabSlotRulesTests`:** D1/D2 swapped only without references and with a reversed edge; a closed edge gives null.
- **`GussetRulesTests`**
  - implied end found from `SectionPoint` and offset;
  - the copy's line reversed → explicit end R(a), or the alternative L − d (checks.py #4);
  - a subsequent gusset referencing the previous section;
  - `FlipOffset` σ rule (like `DraftDirectionTests`).
- **`ReflectionArrayTests`:** direct array det −1; `FromSwArray(−M, −1) ≈ ToAffine()`; translation not scaled; probe-point check; `PointSets.Unmatched` with a grid and 5 000 points.
- **`SheetMetalArgsTests`**
  - `InsertBends2` arguments (K or −1, BA or −1, table);
  - ratio clamped to [0.05, 2];
  - relief mapping 1–5;
  - overlap 1.
- **`FallbackReportTests`:** general line; covered entry; restart line; allowance warning; „Abwicklung nicht geprüft“.
- **`VentCutRulesTests`** (phase 6): acceptance conditions (draft, offset, pure removal).

### 14. Live-test assumptions

| # | Assumption | If it fails |
| --- | --- | --- |
| L1 | `ApplyTransform` accepts det −1 on a `Copy2` temporary body (direct array or `(−M, scale −1)`); vertices and volume as expected | no fallback; the refusal stays |
| L2 | `CreateFeatureFromBody3(copy, false, Check)` of a reflected temporary body gives a separate valid solid body without external references, also in the same part next to a touching original; whether the temporary body is consumed | fresh copies anyway (P7) |
| L3 | Convert with `FindBends` true and only a fixed face on a body with round bends: bends collected, geometry unchanged; which face side works | A2, then B, then F3 |
| L4 | `InsertBends2` on a body with round bends; fixed face at mark 0; K/BA taken | route B dropped |
| L5 | Allowance write (`SetOverrideDefaultParameter2` + `SetCustomBendAllowance`) takes effect on the convert's SheetMetal | warning from P1 |
| L6 | Same part: the convert leaves the template and the original unchanged | guard restores the snapshot; else abort |
| L7 | `InsertFormToolFeature` with a pre-selected 2D sketch on the face places one instance per point and absorbs the sketch | face plus variant A |
| L8 | The second sub-sketch carries the rotation as geometry; rotation sense of `RotAngle` | 3D registration only; ± tried |
| L9 | `FormToolInstance.GetDefinition()` is null in 2019–2024, `ILibraryFormToolFeatureData` from 2025 SP2; units of `RotationAngle`; the "Sheet Metal tab visible" requirement | 2019 path |
| L10 | `ListExternalFileReferences2` lists the tool path for linked and unlinked tools | library search |
| L11 | Type names of the tab and slot features; slot companion as child; definition of the slot feature | name pattern; PartOf |
| L12 | The gusset's implied reference: which bend end; with a second gusset, which edge of the previous one | alternatives L − d |
| L13 | `ReferencePoint` and `ReferenceLine` can be set after creation; meaning of `ProfileType` custom | v2 with arrays |
| L14 | Vent type name; a sheet-metal vent is a prismatic cut | trigger only |
| L15 | Temporary bodies survive later rollbacks and the build in another document | snapshot pass right before the build |
| L16 | Body at state k found by `GetFeatures()` containing the trigger | no snapshot |
| L17 | `CloseDoc` on an unsaved new part closes without a prompt (restart) | no restart in new part, final-state import instead |
| L18 | Convert/InsertBends `ReliefType` values (0 versus 1–5) and `OverlapType` 0 versus 1 | values from the enum |
| L19 | Numeric value of `swFmSolidToSheetMetal` (2025 SP2) | 2019 path |
| L20 | Flat-state check on the convert's flat pattern (Area 1 §9 mechanism) | „Abwicklung nicht geprüft“ |

**Sources**
- Saved help (2025 mirror): `…/150560a9…/scratchpad/area4docs/` (`IFeatureManager~InsertFormToolFeature`, `~InsertConvertToSheetMetal2`, `IPartDoc~InsertBends2`, `~CreateFeatureFromBody3`, `IBody2~ApplyTransform`/`Copy2`/`Save`, `IModeler~Restore`, `ITabAndSlotFeatureData` example, `ILibraryFormToolFeatureData`, `ReleaseNotes`).
- Offline API catalogue: `…/150560a9…/scratchpad/fc2/repos/offdocs/…/api_member_details.xml`, read with `work-area4/doc.py`.
- Third-party code: `…/150560a9…/scratchpad/area4gh/` (NativePartMirrorV7.cs, ReflectionApiTransformV7.cs, RearHubRepair.cs, ffd_tools_sheetmetal.py) and `…/scratchpad/ghsrc/cadapter_sw_sheetmetal.py`.
- Neighbouring reviews: `…/a60c880b…/scratchpad/blech/blech-grundlagen-gegenpruefung.md` (P1, P2, P9, P13, P17, §9) and `blech-laschen-gegenpruefung.md` (refusal per type, triggers).
- Web (search summaries; help.solidworks.com unreachable here, ENOTFOUND):
  - [Positioning Form Features (2019)](https://help.solidworks.com/2019/english/SolidWorks/sldworks/t_positioning_form_features.htm)
  - [Applying Forming Tools (2014)](https://help.solidworks.com/2014/English/SOLIDWORKS/Sldworks/t_Applying_Forming_Tools_to_Sheet_Metal_Parts.htm)
  - [Form Tool Feature PropertyManager (2017)](https://help.solidworks.com/2017/english/solidworks/sldworks/HIDD_DVE_FORM_FEATURE.htm)
  - [Vent PropertyManager (2024)](https://nexthelp.solidworks.com/2024/english/SolidWorks/sldworks/HIDD_DVE_VENT.htm)
  - [Javelin: The SOLIDWORKS Vent Feature is not just for Sheet Metal](https://www.javelin-tech.com/blog/2017/06/solidworks-vent-feature/)
  - [Visiativ: How to use the SOLIDWORKS Vent tool](https://visiativ.co.uk/news-resources/solidworks-vent-tool-sheetmetal-tutorial/)
