# Review of the sheet-metal foundation design (area 1) for Spiegelwerk

I checked `docs/entwurf/blech-grundlagen.md` against the current tree in `/home/user/installer-lane/spiegelwerk` (HEAD `54a24d7`). Line numbers below are from those files. I found **18 problems (P1–P18)**. Five of them can damage the original, leave garbage in the model, or keep the area from working at all:

- **P1:** in the same part, the copy writes the part's shared sheet-metal defaults. That can change the original permanently, and the existing guard cannot undo it.
- **P2:** the features SOLIDWORKS creates together with a base flange (SheetMetal, FlatPattern, folders) are removed by no cleanup path. `RemoveLeftovers` could even delete a template folder.
- **P3:** a flattened original is not handled. In the same part the copy would land below the flat pattern and exist only in the flattened state.
- **P4:** the run refusal (`UnsupportedFeatures.RefusesRun`) is never opened, and the phase plan overstates what phase 1 covers.
- **P5:** the order of the creation paths contradicts the evidence, nothing is cleaned up between attempts, and an unwanted merge is not detected.

**API.** Every API member and enum value the design names exists in 27.3.0 with the stated signature. The 32.1.0 differences are as the draft says, plus two it does not mention: the `D1Offset*`/`D2Offset*` members are obsolete in the current help, and 32.1.0 adds `swFlangeOffsetUptoEdgeAndMerge = 6`.

**Mirror rules.** The rules for thickness side, depth, contour side, allowance and fixed face are geometrically right. A numeric check confirms them, and the proposed flat-state check is very sensitive.

**Mirror API.** Nothing in the design uses it.

Nothing outside `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech` was modified.

Scratch work:
- **Interop probe:** `dotnet /tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/smfound_rev/out/rv.dll <27|32> members|enum|enumval|find <regex>` (MetadataLoadContext over the NuGet 27.3.0 and 32.1.0 assemblies). The dumps are in `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech/work-a1/` (`bf_27.txt`, `bf_32.txt`, `sm_*`, `fp_*`, `cba_*`, `bends_*`, `nc_*`, `ncg_*`, `smf_*`, `fpf_*`, `enums_*`).
- **Numeric check:** `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech/work-a1/sidecheck.py` (run with `python3 -I`).
- **Help:** help.solidworks.com and cati.com do not resolve from this session (ENOTFOUND). Help statements come from the GitHub help mirror in `…/150560a9…/scratchpad/gh/help` and `…/gh/ex`. Where only a search-engine summary of a help page was available, it is marked "(search summary, unverified)".

---

## Verified as correct

**API members (27.3.0; identical in 32.1.0 unless noted)**
- `IFeatureManager`:
  - `InsertSheetMetalBaseFlange2` (19 parameters, in the order the draft gives), `InsertSheetMetalBaseFlange` (the first 16).
  - `object CreateDefinition(int)`, `Feature CreateFeature(object)`, `CustomBendAllowance CreateCustomBendAllowance()`.
  - `bool InsertConvertToSheetMetal2(double,bool,bool,double,double,int,double,int,double,bool)`.
  - `FeatureCut4` (27 parameters; `NormalCut` is the 18th, `OptimizeGeometry` the last).
  - `object GetSheetMetalFolder()`, `object GetFlatPatternFolder()`.
  - `void AddSMNormalCutType(bool,bool,bool)`, `int AddSMNormalCut()`, `object FinishSMNormalCut()`.
- **Other interfaces:**
  - `IPartDoc.bool InsertBends2(double,string,double,double,bool,double,bool)`. Help: the offset ratio must be within 0.05–2.0, and `DoFlatten` creates SheetMetal + FlattenBends + ProcessBends.
  - `IModelDocExtension.object GetTemplateSheetMetal()`. Help: returns an `IFeature`, null before 2013.
  - `IModelDoc2.int SetBendState(int)`/`GetBendState()`. Help: old-style and converted parts only; new-style parts flatten by unsuppressing the flat pattern.
  - `IFeature.bool SetSuppression2(int,int,object)`, `object IsSuppressed2(int,object)`, `IBody2.bool IsSheetMetal()`, `ISketchContour.bool IsClosed()`.
- **Feature data interfaces:**
  - `IBaseFlangeFeatureData` (27.3): exactly the draft's list.
    - 32.1 adds `GetCustomBendAllowance()`, `Initialize(bool UseMaterialSheetMetalParameters, bool OverrideDefaultBendAllowance, object, bool OverrideDefaultBendRelief, int, bool, double, double, double)`, `D1/D2EndConditionType/Distance/Reference`, `SymmetricThickness`, `OverrideDefaultSheetMetalParameters` (get/set), and the get-only relief/allowance/material flags.
    - The help remark on the `ReverseThickness` setter (it needs `OverrideDefaultSheetMetalParameters` = true and `UseMaterialSheetMetalParameters` = false) is quoted correctly.
  - `ISheetMetalFeatureData`: as in the draft, plus the v1 `Get/SetOverrideDefaultParameter(bool)`. 32.1 adds `UseMaterialSheetMetalParameters` (get/set).
  - `ICustomBendAllowance`: `Type`, `BendTableFile`, `KFactor`, `BendAllowance`, `BendDeduction` in both versions. `BendCalculationTableFile` appears only in the current help (remark on `Type`), so reading it late-bound is right.
  - `IFlatPatternFeatureData`: identical in both versions and as listed. There is no grain-direction member in 27.3, in 32.1, or in the current help's member list.
  - `IBendsFeatureData`, `ISMNormalCutFeatureData2` and `ISMNormalCutGroupData`: as listed. The help gives marks 4 (offset plane, only in offset-plane mode), 8 (direction, only in extent mode) and 1 (faces), with `CreateGroup` and `ClearSelection2` per group.
  - `IExtrudeFeatureData2.LinkToThickness/NormalCut/OptimizeGeometry`.
  - `ISheetMetalFolder` and `IFlatPatternFolder`: `GetFeature()`, `Get…Count()`, `Get…s()`.
- **Absent from both versions:** `IConvertSolidFeatureData` and `swFmSolidToSheetMetal`.
- **`InsertSheetMetalBaseFlange2` help:** obsolete (superseded by `CreateDefinition`/`CreateFeature`). Scope bodies go on mark 8 when `UseAutoSelect` = false.

**Enums (all values correct in both versions)**
- `swBendAllowanceTypes_e`: 1–5.
- `swSheetMetalReliefTypes_e`: 1–5.
- `swFlangeOffsetTypes_e`: 1–5 (32.1 adds 6, see P12).
- `swSheetMetalOverrideDefaultParameters_e`: 0/1/2.
- `swSMBendState_e`: 0–3.
- `swNormalCutParameters_e`: 0/1.
- `swFeatureSuppressionAction_e`: Suppress 0, Unsuppress 1.
- `swThisConfiguration` = 1.
- `swFeatureNameID_e`: SheetMetal 30, FlattenBends 31, ProcessBends 32, BaseFlange 34, FlatPattern 38, NormalCut 115.
- **Also checked:** `swDeleteSelectionOptions_e` Children = 1, Absorbed = 2; `swSheetMetalModifierError_e` 0–5 (`NotEnabledOnTemplate` = 2).

**Claims about the existing code that hold**
- `FeatureCollector.SkipReason` skips every suppressed feature (FeatureCollector.cs l.64), so today it would skip a folded flat pattern.
- `SwDoc.FeaturesInCreationOrder` lists only the top level plus absorbed sketches (SwDoc.cs l.65–74).
- The owner check uses `PositionedHoleRecord` (MirrorJob.cs l.150–153).
- `PlanSeparateBody`: the first mirrored body never merges (BuilderSupport.cs l.77–85).
- `ExtrudeBuilder` already passes `NormalCut` and `OptimizeGeometry` to `FeatureCut4`.
- `SketchMirror` keeps `StartId`/`EndId` and maps arc direction with `ArcDirection` (SketchMirror.cs l.63–71), so t′ = R(t) holds in model space.
- `StepVerifier.Verify` returns early for records with `ChangesSolid` = false (StepVerifier.cs l.31), so the flat pattern needs its own check.
- `MassState.Mirror` is linear in (V, M), so differences mirror like changes.
- `PartOfFeatureRecord`/`PartOfBuilder` (`AlreadyThere`) and `SwLate` exist as described.

**Mirror rules: numeric check (`sidecheck.py`, 20 000 random reflections, sketch frames and σ)**
- **Closed profile:** `ReverseFlag(rev, σ)` puts the plate on R(side). Worst deviation 1.2e-15.
- **Open profile, contour side (model a: side = ±n×t):** `InPlaneSideFlag` (flip when σ = +1) gives R(side). Worst deviation 1.2e-15.
- **Model b (side = ±d×t, d = actual extrusion direction):** "always flip" is right. Applying the draft's rule under model b is wrong in 0 of the σ = +1 cases and in all 9 980 σ = −1 cases. The draft's L3 statement holds: the models differ only when σ = −1.
- **Third-party evidence for the closed-profile rule:** eyfel (SOLIDWORKS 2026, live probe, `…/scratchpad/ghsrc/eyfel_SheetMetal.cs` l.89–97) and soheil (`…/gh/notes_soheil.md` l.314–316) both report that `ReverseDirection`, not `ReverseThickness`/`ThickenDir`, flips a closed-profile plate.
- **Allowance:** thickness, radius, angle and material are invariant under a reflection, so K-factor, BA, BD, bend and gauge tables and relief parameters stay as they are. The fixed face maps by its fingerprint, and Flat(R P, R F) = R Flat(P, F). There is no σ on the flat pattern.
- **Flat-state check, analytic L sheet** (60 + 25 mm, W 100, t = r = 2 mm), comparison with the existing `MassComparison` tolerances:
  - Correct copy: ratios 0/0.
  - Wrong fixed face: moment ratio 8.3e4 (K = 0.5) and 2.0e5 (K = 0.44).
  - K 0.5 ↔ 0.44: volume ratio 1.0e4–2.1e4.
  - With K = 0.5, (flat − folded) has zero net volume, but the moment still separates the cases.
  - So the check the draft proposes is sound and decisive.

**Mirror API.** The design calls no `InsertMirrorFeature*`, `MirrorPart` or derived-part method. `IMirrorPartFeatureData.SheetMetalInformation` exists in the interop but is not used.

---

## (1) Problems

### P1. Same part: the copy writes the part's shared sheet-metal defaults, which can change the original permanently
- **Evidence:**
  - CADapter (`…/gh/cadapter_sheet.md`): "Sheet thickness belongs to the **document**, not the body: a second base flange at 3 mm thickens the first one, which was at 2 mm."
  - Help, `IBaseFlangeFeatureData.ReverseThickness`: the setter takes effect only when `OverrideDefaultSheetMetalParameters` is true, so `ReverseThickness` belongs to the shared sheet-metal parameters. `OverrideDefaultSheetMetalParameters` exists only in 32.1.
  - Search summary (unverified) of the help page "Multibody Sheet Metal Parts": bodies inherit thickness and bend radius from the Sheet-Metal folder and may override them; only one gauge table is allowed per multibody part, set by the first sheet-metal body.
  - The draft does all of the following unconditionally:
    - It passes `r.Thickness`/`r.BendRadius` at creation (§10 l.502–505).
    - It writes `d.Thickness`, `d.BendRadius`, `d.ReverseThickness` and the gauge settings in `FeatureEdit.BaseFlange` (l.511–516).
    - It offers "Dickenrichtung (ReverseThickness) umgekehrt" as a verifier toggle (l.536).
  - When the original body overrides its thickness, or (with an open profile) a toggle runs, the write lands on the template. That changes the original bodies that use the defaults.
  - `OriginalUntouched` only measures. `RemoveAgain` (MirrorJob.cs l.471–478) deletes the base flange and nothing else. The changed default stays, and the original remains changed.
  - The draft's own L19 quotes the third-party warning, while §5.1 and §11 assert "the template is never edited".
- **The same flaw in a new part:** `ApplyAndCompare` on the template runs only when `made.Template != null` (l.520), that is, for the first base flange. A second body whose creation rewrites the template changes the first copied body after its step was already reported OK.
- **Fix:** add a `SheetMetalGuard`, see (2) §3.
  - Create every base flange after the first with the template's current thickness, radius and allowance, then apply body overrides on the new body's SheetMetal feature (`SetOverrideDefaultParameter2(0|1|2, true)`, checking the return codes).
  - Write `Thickness`/`BendRadius`/`ReverseThickness`/gauge on a non-first base flange only after a late-bound `OverrideDefaultSheetMetalParameters = true`, and only when that setter took effect.
  - Snapshot the template parameters and the original SheetMetal parameters before every sheet-metal step. Compare them after the step: after the build, after the verifier's toggles, and after any `RemoveAgain`.
  - On a difference, write the snapshot back, rebuild, and check the original masses. If that fails, abort the run with a clear report line (the original is changed; undo with Ctrl+Z).
  - In the same part, no `ReverseThickness` toggle is offered for a closed profile, where it has no effect anyway.

### P2. Companion features are not covered by any removal path, and `RemoveLeftovers` could delete a template folder
- **Evidence:**
  - **`RemoveAgain`:** it calls `BuilderSupport.Delete(ctx, f)` with option 0 (BuilderSupport.cs l.56), and `swDelete_Children` = 1 (verified), so only the base flange goes. Its SheetMetal parent and its FlatPattern child stay as orphans.
  - **`ctx.Unregister(record.Name)` (l.478):** it leaves the draft's `Created[SheetMetalName]` pointing at a deleted feature.
  - **`RemoveLeftovers`/`NewFeatures` (l.430–469):** they see only new top-level features that are not system features.
    - In a multibody part (and in the same part after the first copied body) the new SheetMetal and FlatPattern sit inside the folders (draft §5.4), invisible to it.
    - `SwDoc.SystemTypes` (SwDoc.cs l.17–24) contains neither template type, and the draft adds only `TemplateFlatPattern` (§9). A Sheet-Metal folder that SOLIDWORKS creates in a builder step that then throws would be selected and deleted together with the original's SheetMetal inside it.
  - **The draft's builder:**
    - It calls `RemoveNew` only when no `SMBaseFlange` resulted (l.508).
    - A failed `FeatureEdit.BaseFlange` keeps the half-defined flange with a note (l.519).
    - Nothing is cleaned up between the creation attempts (l.502–507). A call that returns null but leaves a partial feature contaminates the next attempt.
- **Fix:** see (2) §10.
  - `SwSheetMetal.Snapshot` covers the top level and the contents of both folders.
  - Every creation attempt is followed by `NewSince`, and its leftovers are removed before the next attempt.
  - The builder sets `BuildResult.RemoveHelpers` to delete the companions (FlatPattern, then SheetMetal, then the flange, all with `swDelete_Absorbed`) and to unregister them.
  - `RemoveLeftovers` takes the extended snapshot.
  - `TemplateSheetMetal` and `TemplateFlatPattern` both go into `SwDoc.SystemTypes`, which also keeps them out of `NewFeatures`, `SolidFeatures` and the children check of `DeleteOriginals`.
  - A failed final edit deletes the flange with its companions and fails, as `ExtrudeBuilder` does.

### P3. A flattened original, and features after the flat pattern, are not handled
- **Evidence:**
  - Search summaries (unverified) of the help pages "Flat Pattern" (2017/2019) and "FeatureManager Design Tree": features added while the part is flattened go **below** Flat-Pattern and exist only in the flattened state, and sheet-metal features cannot be dragged below Flat-Pattern.
  - In the same part with a flattened original, every mirrored feature would therefore land below the original's flat pattern.
  - The draft assumes the flat pattern is always last (§5.2, §10) and never handles flat-only features after it. Those are suppressed when folded, skipped as "unterdrückt", and their reason is wrong.
  - **The flat measurement in `MassCapture`:**
    - It needs the rollback bar at the end. The draft measures "folded = MassOf(doc)" directly after the previous record has left the bar after that feature (MassCapture.cs l.68–71).
    - "Measured the other way round when flattened" and L10 ("several may be flattened at once") leave the multibody case open.
- **Fix:** add a `SheetMetalSession` for the run, see (2) §3.
  - Before collecting, every flat pattern that is unsuppressed in the active configuration (and, in phase 1b, every suppressed ProcessBends) is put into the folded state. The flattened state is recorded per feature and restored in a `finally`. Afterwards the copies get their recorded state.
  - A feature after a flat pattern is then suppressed and skipped with its own reason: „Nur in der Abwicklung vorhanden – nicht gespiegelt.“
  - The flat measurement first moves the bar to the end and then measures one flat pattern at a time, starting from the folded state.

### P4. The run refusal is not opened, and the phase plan claims too much
- **Evidence:**
  - `MirrorJob` refuses the whole run when any collected type is in `UnsupportedFeatures.SheetMetal` (MirrorJob.cs l.138; UnsupportedFeatures.cs l.12–28). That set includes `SheetMetal`, `SMBaseFlange`, `FlatPattern`, `NormalCut`, `FlattenBends`, `ProcessBends`, `SolidToSheetMetal` and both template types.
  - `ReportTextTests` asserts `RefusesRun("SheetMetal")` and `RefusesRun("SMBaseFlange")` (l.79–84).
  - The draft never mentions this. As written, nothing in area 1 could ever run.
  - Its §14 says phase 1 "is enough for the user's main workflow". Edge flanges, miter flanges and hems are area 2 (`blech-laschen.md`), and almost every real sheet-metal part has them. Until area 2 lands, such parts must still be refused.
  - `SolidToSheetMetal` without a builder fails, and everything after it fails too. That is exactly the situation the refusal exists for (STOLPERSTEINE 139).
- **Fix:**
  - Add `UnsupportedFeatures.SupportedSheetMetal`, growing per phase: phase 1a `SheetMetal`, `SMBaseFlange`, `FlatPattern`, `NormalCut`, `TemplateSheetMetal`, `TemplateFlatPattern`; phase 1b `FlattenBends`, `ProcessBends`.
  - `RefusesRun(t) = t == "MirrorStock" || (IsSheetMetal(t) && !SupportedSheetMetal.Contains(t))`.
  - `SolidToSheetMetal` stays refused until area 4.
  - Update the tests, the README row "Noch nicht" and STOLPERSTEINE 139.

### P5. The order of the creation paths contradicts the evidence; no cleanup between attempts; an unwanted merge goes undetected
- **Evidence:**
  - **v2 (`InsertSheetMetalBaseFlange2`):**
    - CADapter (SOLIDWORKS 2017): v2 is a "silent no-op", v1 builds.
    - eyfel (SOLIDWORKS 2026, verified live, `eyfel_SheetMetal.cs` l.79–110): v2 "returned null … even with a correctly selected closed contour". `CreateDefinition(34)` → set properties → `CreateFeature` **without** `Initialize` works.
    - soheil: v2 builds closed profiles, but "an open sketch returns null" (notes_soheil.md l.276).
    - The help marks v2 obsolete.
  - **Data object:** the official examples use it in 2015 (gauge table, no `Initialize`) and in 2022 (an **open** three-line profile, `D1EndConditionType`, `Initialize`).
  - **The draft:**
    - It tries v2 first and v1 only "if Merge does not matter". That excludes every same-part case with own bodies.
    - It never checks the body count. The data object has no Merge member in either version, so whether it merges with a touching body (the original, in the same part) is unknown. `OriginalUntouched` would then remove the flange, and P1/P2 apply.
  - **`DirToUse`:** every working call passes 1, ladla reports that 0 fails v2, and tleide names the constant `direction1` (`…/smbasereview/dl1/tleide_program.cs` l.1425–1463). Most likely it is the number of directions, not an end condition.
- **Fix:** see (2) §8.
  - **Order:** data object first, then v1 with `DirToUse = OffsetDirections` (then 1), then v2.
  - **Exception:** use v2 first only when an explicit Merge/scope is required, that is, a tab in separate-body mode.
  - **After every attempt:** check the new features since the snapshot (exactly one new `SMBaseFlange`) and the body delta (+1 for a new body, 0 for a tab). Any mismatch removes the attempt and moves on to the next path.

### P6. The collector's feature and body modes do not bring the flat pattern
- **Evidence:**
  - With selected features, `FeatureCollector.Select` pulls in only sketches and pattern seeds (l.98–131).
  - With selected bodies it pulls in `IBody2.GetFeatures()`. Whether a body lists its FlatPattern is unknown, and in a multibody part the flat pattern is not even listed today.
  - Mirroring "Base-Flange1" alone therefore gives a copy whose flat pattern silently keeps SOLIDWORKS' default fixed face. Nothing reports it.
  - `DeleteOriginals` then refuses, because Flat-Pattern1 is a child of the base flange (l.722), but the report does not say why the flat pattern differs.
- **Fix:** add a `SheetMetalTree.Closure` in Core, applied in `Select`.
  - A wanted `SMBaseFlange` adds its SheetMetal parent (if listed) and the flat pattern of its body.
  - A wanted flat pattern without its owner becomes `UnsupportedRecord(…, false)`: „Abwicklung entsteht nur mit ihrem Basisflansch.“

### P7. Equations on sheet-metal parameters are never copied (the draft's L21 is wrong for the current code)
- **Evidence:**
  - "Thickness@Sheet-Metal1" and "BendRadius@Sheet-Metal1" belong to the SheetMetal feature, which the draft captures as `PartOfFeatureRecord`.
  - `CaptureDimensions` skips `PartOfFeatureRecord` (MirrorJob.cs l.345).
  - `KnownDimensions` is filled only by `RestoreDimensionNames` for a record's own built feature (l.383).
  - `EquationProblem` requires `KnownDimensions.Contains(full)` (l.655).
  - Registering the new SheetMetal under the original name (§10 l.525) is not enough. Every equation that uses the sheet thickness, which is the most common sheet-metal equation, is reported "nicht übernommen".
- **Fix:**
  - `BaseFlangeRecord.CompanionDimensions` captures the SheetMetal feature's own dimensions.
  - `BuildResult.Companions` carries (new feature, original name, dimensions).
  - `RestoreDimensionNames` is generalised to (feature, owner name, dimensions) and runs for each companion as well.
  - Correct the L21 text.

### P8. Deleting the original in the same part can change the copy, and nothing checks for it
- **Evidence:**
  - The defaults, and the single gauge table of a multibody part, are tied to the first sheet-metal body (P1 sources).
  - `DeleteOriginals` (l.695–733) deletes the original base flange and SheetMetal with `swDelete_Absorbed`. It does not measure the copy afterwards.
  - Note: a part with a flat-pattern drawing view usually also has a derived "…SM-FLAT-PATTERN" configuration. The existing configuration rule then blocks deletion anyway.
- **Fix:**
  - **Phase 1:** when the run built sheet-metal records, „Original löschen“ is refused with „Original nicht gelöscht: Die Blech-Standardwerte des Teils hängen am ersten Blechkörper; die Kopie könnte sich mit dem Löschen ändern.“
  - **Later, after a live test:** measure the copy's bodies before and after the deletion, and run `EditUndo2` with a report line if they changed.

### P9. Writing the allowance through `ISheetMetalFeatureData` is known not to take effect; return codes are ignored
- **Evidence:**
  - CADapter measured (`cadapter_sm.py`, `set_bend_radius`/`set_k_factor`/`set_bend_table`):
    - `ModifyDefinition` on `ISheetMetalFeatureData` returns true and changes nothing for the K-factor (8 combinations) and the bend table (5 combinations).
    - The radius changes only after `SetOverrideDefaultParameter`.
    - The thickness changes through `IBaseFlangeFeatureData`.
  - Help: `SetOverrideDefaultParameter2` is valid only for multibody parts created in 2013 or later, and it returns `swSheetMetalModifierError_e`.
  - The draft makes `ApplyAndCompare` the main path for K/BA/table and reads `GetOverrideDefaultParameter2(i, out bool)` without checking the return code. In a single-body part the out value is meaningless.
  - For parts created before 2013 (no template), `Defaults` is null, so the new part's template never gets the original's values (l.520).
- **Fix:**
  - The bend allowance is set at creation: PCBA in v1/v2, `Initialize` in the data object. That gives `Defaults` for the first body and the own allowance otherwise.
  - Every `Set…`/`Get…` with a return code is checked. Non-zero means "not applicable" and adds a note.
  - Read-back stays, but only as a hint. The flat-state check (P16) is the real confirmation.
  - The defaults for a new template are `Defaults ?? Body`.

### P10. Material-driven sheet-metal parameters and `OverrideDefaultSheetMetalParameters` (newer versions) are captured but never applied
- **Evidence:**
  - `SheetMetalParameters.UseMaterialParameters` is read (§7 l.245), and the builder ignores it.
  - With `UseMaterialSheetMetalParameters` = true, the original's thickness, radius and K-factor come from its material. The copy gets the part's material only in a new part with „Werkstoff übernehmen“, and per-body materials never.
  - `OverrideDefaultSheetMetalParameters` is not captured at all.
- **Fix:**
  - Capture both (late-bound).
  - The copy always gets explicit values (`UseMaterialSheetMetalParameters` = false, late-bound) with the note „Blechparameter des Werkstoffs als feste Werte übernommen.“
  - `OverrideDefaultSheetMetalParameters` follows the P1 rule.

### P11. A straight fixed edge would become an axis
- **Evidence:**
  - `ToGeometryRef(Edge)` turns a straight edge into `GeometryRef.ToAxis` (FeatureCapture.cs l.364–367).
  - The draft passes `ToGeometryRef` into `ReadParameters` for `ISheetMetalFeatureData.FixedReference` (§8 l.423–424, l.434).
  - A straight fixed edge, the cylindrical case, would be resolved as an axis or helper axis and set as the fixed reference.
- **Fix:** one helper `FaceOrEdgeRef` (Face → `ToFace`, Edge → `ToEdge`, anything else → null with a note) for every sheet-metal fixed reference and flat-pattern entity.

### P12. Obsolete members and a new enum value
- **Evidence:**
  - The current help marks `D1/D2OffsetType`, `D1/D2OffsetDistance` and `D1/D2OffsetReference` obsolete, superseded by `D1/D2EndConditionType/Distance/Reference` (also `swFlangeOffsetTypes_e`). The 2022 example uses only the new ones.
  - 32.1 adds `swFlangeOffsetUptoEdgeAndMerge = 6`, which the draft's mapping (1–5) does not cover.
- **Fix:**
  - Read and write the new members late-bound when they exist, and otherwise the 2019 ones.
  - On disagreement, trust the new one and add a note.
  - `FromFlangeOffset` returns null for any value it does not know. The flange is then captured as Unsupported with the reason „Endbedingung … wird nicht unterstützt“.

### P13. `SwLate` cannot return method results, and the capture is not exception-safe
- **Evidence:**
  - `SwLate.Get` uses `GetProperty`, and `SwLate.Call` returns only a bool (SwLate.cs).
  - `GetCustomBendAllowance()` (32.1) is a method, so the draft's `SwLate.Get(d, "GetCustomBendAllowance")` (§8 l.416) always returns null.
  - `Capture` catches only `NotSupportedException` (FeatureCapture.cs l.92), and the capture runs outside the build `try` (MirrorJob.cs l.141–148).
  - A null `GetCustomBendAllowance()`, a COM error from `IsSuppressed2`, or a cast of a late-bound result aborts the whole run before anything is built. The region review found the same pattern (skizzenbereiche-gegenpruefung P9).
- **Fix:**
  - Add `SwLate.Invoke(object, string, params object[]) → object?`.
  - `CaptureSheetMetal*` wraps everything in `catch (Exception ex)`, which becomes `NotSupportedException` with the reason.

### P14. `LinkToThickness` inside the main edit
- **Evidence:**
  - The draft sets `d.LinkToThickness` in the existing edit lambda (§10 l.569–571) and says a failure is "a note, not a failure".
  - `FeatureEdit.Edit` turns any exception into false (IFeatureBuilder.cs l.118–136), and `ExtrudeBuilder` then deletes the feature and fails.
- **Fix:** run a second `FeatureEdit.Extrude` for `LinkToThickness` only, after the main edit has succeeded. If it fails, add the note „Tiefe nicht an die Blechdicke gebunden (gleicher Wert)“.

### P15. The copy gets its own allowance and relief where the original used the defaults
- **Evidence:**
  - The draft always passes `UseDefaultRelief` = false and, in a new part, the defaults' allowance as PCBA (l.496, l.502).
  - In versions that report `UseDefaultBendAllowance`/`UseDefaultBendRelief`, the copy's flange is then "own allowance / own relief" while the original's followed the defaults.
  - The geometry is the same today, but the parametric behaviour differs, and so does the read-back comparison.
- **Fix:**
  - `UseDefaultRelief = (OwnRelief == null)`.
  - PCBA = `OwnBendAllowance`, or, only for the first body of a new part, the defaults (which then become the template; see P9).

### P16. Details of the flat-state check
- **Evidence:**
  - The bar position problem (P3).
  - There is no rule for `FlatChange` == null (VerifySteps off, or the measurement failed).
  - Core test 9 only re-derives the linearity of `MassState.Mirror`. It shows nothing about sensitivity.
  - The grain-direction line is added to the general report in every run with a flat pattern, even though it is not readable.
- **Fix:**
  - **VerifySteps off:** no check, and the entry's verification text is "nicht geprüft". Read-back differences remain warnings.
  - **Measurement failed:** "nicht geprüft" plus a Warning, as `StepVerifier` does for a missing `ExpectedChange`.
  - Add sensitivity tests (wrong fixed face, K 0.5 ↔ 0.44, BA ±0.1 mm; see `sidecheck.py`).
  - The grain line goes into the flat pattern's details, not into the general report.

### P17. Overlapping types and responsibilities across the four drafts
- **Evidence:**
  - Area 2 defines its own `SwSheetMetal.cs` and `BendAllowanceSettings` (blech-laschen.md l.145, l.167).
  - Area 3 defines `BendAllowanceSpec` "share with Area 1 if it defines one" (blech-biegungen-ecken.md l.98).
  - Area 4 defines another `SheetMetalParameters` (blech-umformwerkzeuge-ersatzweg.md l.347) and its own convert/`InsertBends2` steps (l.309–311).
  - Area 1 defines `BendAllowanceData`, `SheetMetalParameters`, `SwSheetMetal`, a `ConvertSolidBuilder` and the fallback's convert helper.
- **Fix:**
  - Area 1 owns the shared types (`BendAllowanceData`, `ReliefData`, `GaugeData`, `SheetMetalParameters`) and `Sw/SwSheetMetal.cs` (read/write allowance, snapshot, companions). Areas 2–4 use them.
  - `ConvertSolidRecord`/`ConvertSolidBuilder` and the fallback helper move to area 4. Area 1 keeps only `LegacyBends` (phase 1b).

### P18. Smaller inaccuracies
- **§5.5:** "Today `TemplateSheetMetal` would end up as an `UnsupportedRecord`". Today the run is refused before capture (MirrorJob.cs l.138). The statement is only about the state after P4.
- **§0:** "confirmed live by a third party" covers SheetMetal + SMBaseFlange + FlatPattern (soheil). It does not cover the template folder.
- **§9:** only line 150 is named. The Esc path also withdraws parts per `PositionedHoleRecord` (MirrorJob.cs l.204) and must switch to `BringsParts` as well.
- **Tab detection (`MergesIntoExisting`):** cross-check it with "the base flange's SheetMetal parent already has an earlier `SMBaseFlange` child". Treat it as an assumption; only the geometric test is primary.
- **`DirToUse`:** the help text ("end condition type") is not "nonsensical". It is most likely the number of directions (see P5).
- **§3:** the profile "selected with mark 0" comes from the 2015 example via `SelectByID2("Sketch1",…)`. The 2022 example relies on the selection left behind by `InsertSketch(true)`.

---

## (2) Corrected design

This section supersedes `blech-grundlagen.md`. Where it says "as in the draft", that part of the draft stands.

### 0. Scope and verdict

- **The approach is sound and implementable:** capture the base flange, its SheetMetal and the flat pattern as data; rebuild the flange; edit the flat pattern SOLIDWORKS creates with it; verify the folded change and the (flat − folded) change.
- **What is new** compared with the draft: the template guard (P1), companion cleanup (P2), the session that folds the part for the run (P3), refusal gating (P4), the order and checks of the creation paths (P5), the collector closure (P6) and companion dimensions (P7).
- **Phase 1a:** base flange/tab, flat pattern, SheetMetal parameters, cut-extrude with `LinkToThickness`/`NormalCut`, and the Normal Cut feature.
- **Phase 1b:** legacy Insert Bends.
- **Moved to area 4:** Convert to Sheet Metal (`SolidToSheetMetal`), with or without the fallback.
- **What phase 1 covers:** after phase 1a, only plates, tabs, open-profile base flanges, cuts and flat patterns are mirrored. Parts with edge or miter flanges and hems stay refused until area 2 lands.

### 1. API used (27.3 unless marked late-bound)

| Purpose | Member | Notes |
|---|---|---|
| Create base flange, path A | `CreateDefinition(34)` → `IBaseFlangeFeatureData` → `CreateFeature` | Sketch selected on mark 0. 2019: `D1OffsetType/Distance/Reference`, `OffsetDirections`, `ReverseDirection`, `ReverseThickness`, `Thickness`, `BendRadius`, gauge in the example's order. Late-bound: `D1/D2EndCondition*`, `OverrideDefaultSheetMetalParameters`, `SymmetricThickness`, `Initialize` (only if `CreateFeature` returned null) |
| Path B | `InsertSheetMetalBaseFlange` (16) | `DirToUse` = `OffsetDirections`, then 1. End conditions as `swEndConditions_e` (tleide), then as `swFlangeOffsetTypes_e` |
| Path C | `InsertSheetMetalBaseFlange2` (19) | `Merge` and `UseFeatScope`; bodies on mark 8. Tried first only for a tab in separate-body mode |
| Allowance object | `CreateCustomBendAllowance()`, `Type` set last | |
| Parameters | `ISheetMetalFeatureData` (`GetDefinition` on SheetMetal and on `GetTemplateSheetMetal()`), `Get/SetOverrideDefaultParameter2`, `Get/SetUseGaugeTable`, `Get/SetCustomBendAllowance` | Return codes checked; `UseMaterialSheetMetalParameters` late-bound |
| Folders | `GetSheetMetalFolder()`/`GetFlatPatternFolder()` → `GetSheetMetals()`/`GetFlatPatterns()` | Snapshot and companions |
| Flat pattern | `IFlatPatternFeatureData` (`FixedFace2`, options, `ISetExcludedFaces`), `SetSuppression2(0|1, 1, null)`, `IsSuppressed2(1, null)` | No `SetBendState` for new-style parts |
| Cut | `IExtrudeFeatureData2.LinkToThickness` (second edit) | `NormalCut`/`OptimizeGeometry` already in `FeatureCut4` |
| Normal cut | `CreateDefinition(115)`, marks 4/8/1, `CreateGroup`, `CreateFeature` | Fallback `AddSMNormalCutType`/`AddSMNormalCut`/`FinishSMNormalCut` |
| Legacy (1b) | `IPartDoc.InsertBends2`, `IBendsFeatureData.GetFixedFace()` | Offset ratio in 0.05–2.0 |
| Late-bound helper | `SwLate.Invoke` (new) | Returns the object |

### 2. Part structure, ownership and collection

- **System types:** `SwDoc.SystemTypes += "TemplateSheetMetal", "TemplateFlatPattern"`. Both are never collected, never deleted, never "new", and never children that block deletion.
- **`SwDoc.FeaturesInCreationOrder`:** the FlatPattern features inside a `TemplateFlatPattern` folder are listed as items absorbed by the folder, so they sort before it at the end of the tree. SheetMetal features inside a `TemplateSheetMetal` folder are not listed; they need no record.
- **Core, `Planning/SheetMetalTree.cs`.** Input: (name, type, parents, children, sub-features, suppressed, index in tree). It returns:
  - `Owners`:
    - a top-level SheetMetal → the `SMBaseFlange` child that has it as a parent (the first in tree order);
    - FlatPattern sub-sketches → the FlatPattern;
    - FlattenBends/ProcessBends → the legacy SheetMetal (phase 1b; until then they stay refused).
  - `FlatPatternOwner`: flat pattern → base flange of its body. The add-in passes the body's features; in Core this is pure name logic.
  - `Closure(wanted)`: a wanted base flange adds its owner SheetMetal and its flat pattern (P6).
  - `AfterFlatPattern`: features placed after a FlatPattern in the tree (P3).
- **`FeatureCollector`:**
  - `Select` applies `Closure`.
  - `SkipReason`:
    - no suppression skip for FlatPattern and its sub-sketches;
    - a suppressed feature after a flat pattern gets „Nur in der Abwicklung vorhanden – nicht gespiegelt.“;
    - everything else as today.
- **Refusal:** `UnsupportedFeatures.SupportedSheetMetal` and the new `RefusesRun` (P4).

### 3. Run level: `SheetMetalSession` and `SheetMetalGuard` (add-in, `Sw/SwSheetMetal.cs`)

**`SheetMetalSession`**
- It is created in `MirrorJob.Run` before `FeatureCollector.Collect`, only if the source has a SheetMetal, template or FlatPattern feature.
- **`Fold()`:**
  - For each FlatPattern (top level and folder content) that is unsuppressed in the active configuration: `SetSuppression2(0, swThisConfiguration, null)`.
  - Then `EditRebuild3`, then check the state.
  - If folding fails, the run is refused: „Die Abwicklung „…“ ließ sich für den Lauf nicht zurückfalten. Es wurde nichts gespiegelt.“
  - **Phase 1b:** a suppressed ProcessBends is unsuppressed in the same way.
- **`Restore()`:** called in a `finally` around capture, measurement and build. It puts the source back. In the same part it also sets each copy flat pattern to the recorded state of its original. In a new part the copies get their state at the end of the build.
- **Assembly:** `MirrorJob.Run` finishes (and restores) before `AssemblyMirror` restores the configuration, so the order is right.

**`SheetMetalGuard`**
- **Who it covers:** the same part, and any target that already has a template when a sheet-metal record is built.
- **Snapshot:** the template parameters plus every original SheetMetal's parameters and override flags (`SheetMetalParameters`).
- **When it checks:** in `MirrorJob.BuildOne`, for records with `TouchesSheetMetalDefaults` (base flange, legacy bends). The snapshot is taken before `builder.Build`. The check runs after `StepVerifier.Verify` and again after a possible `RemoveAgain`.
- **Expected values:**
  - In a new part, from the first base flange on, the expected template is the original's `Defaults ?? Body`.
  - In the same part it is the snapshot.
- **On a difference:**
  - write the expected values back (`ApplyAndCompare`), rebuild;
  - compare the original bodies' masses with the values before the step;
  - if either still differs: `report.Aborted = true` and the line „Achtung: Die Blech-Standardwerte des Teils wurden verändert und ließen sich nicht wiederherstellen (…). Das Original ist betroffen – bitte mit Rückgängig prüfen.“ The step is Failed and the run stops.

### 4. Records (`Spiegelwerk.Core/Records/SheetMetalRecords.cs`)

The draft's types stand, with these changes:
- **Shared types:** `BendAllowanceData`, `ReliefData`, `GaugeData` and `SheetMetalParameters` are the shared types for areas 1–4 (P17). Add to `SheetMetalParameters`:
  - `bool? OverrideDefaults` (late-bound `OverrideDefaultSheetMetalParameters` on the flange);
  - `int? OverrideReadCode` (the return code of `GetOverrideDefaultParameter2`; non-zero means the override flags are not applicable).
- **`FlangeSide`:** keeps `EndCondition` (Core numbers). `SheetMetalRules.FromFlangeOffset` returns null for any value other than 1–5.
- **`BaseFlangeRecord`:**
  - add `List<NamedDimension> CompanionDimensions` (the SheetMetal's own dimensions, P7);
  - add `bool UsesMaterialParameters`;
  - add `string? TabOfSheetMetal` (diagnostic);
  - `SheetMetalName`, `Body` and `Defaults` stay;
  - `Defaults` is `null` before 2013, and builders use `Defaults ?? Body`.
- **`FlatPatternRecord`:**
  - as in the draft;
  - `FixedFace` is a `GeometryRef` that is only ever `Face` or `Edge` (P11);
  - `FlatChange` null means "nicht geprüft".
- **`LegacyBendsRecord` (1b):** as in the draft.
- **`NormalCutRecord`:** as in the draft.
- **`ConvertSolidRecord`:** removed (area 4).
- **Changes to `FeatureRecord`:**
  - `virtual bool BringsParts => false`;
  - `virtual string MeasureUntil => Name`;
  - `virtual bool TouchesSheetMetalDefaults => false`;
  - `PositionedHoleRecord.BringsParts => true`;
  - `BaseFlangeRecord` and `LegacyBendsRecord` override all three as needed.
- **`ExtrudeRecord`:** `bool LinkToThickness`.

### 5. Mirror rules

| Quantity | Rule |
|---|---|
| Closed profile/tab: side (`ReverseDirection`) | `DirectionRules.ReverseFlag(rev, σ)`. Toggle "Blechdicke auf die andere Seite" |
| Closed profile: `ReverseThickness`/`ThickenDir` | Copied unchanged. No toggle in the same part (P1); elsewhere the second toggle |
| Open profile: depth (`ReverseDirection`, D1/D2, `OffsetDirections`) | `ReverseFlag(·, σ)`. Distances, types and `D1/D2ReverseOffset` unchanged. References mirrored, with the blind equivalent as `ExtrudeBuilder` does |
| Open profile: contour side (`ReverseThickness`) | `InPlaneSideFlag` (flip when σ = +1; model a, verified numerically). Toggle "Materialseite der Kontur gewechselt" (model b = always flip; differs only when σ = −1). In the same part the toggle runs under the guard |
| `SymmetricThickness`, mid-plane | Unchanged, no direction toggle |
| t, r, K, BA, BD, bend/gauge/calculation table, relief type/ratio/width/depth, auto relief | Unchanged (isometry). Written at creation as in P9/P15; the guard keeps the shared values |
| Material-driven parameters | Explicit values with a note (P10) |
| Merge/tab | `PlanSeparateBody`. Body-delta check after creation (P5) |
| Flat pattern `FixedFace2`, `ExcludedFaces` | Mirrored fingerprint, found only as a face or edge |
| Flat pattern options, corner trim/break options | Unchanged |
| Flattened state | Restored as recorded (session). The "…SM-FLAT-PATTERN" configuration is not recreated (note) |
| Grain direction | No API (27.3, 32.1, current help). Note in the flat pattern's details |
| Cut `LinkToThickness`/`NormalCut`/`OptimizeGeometry` | Unchanged; the cut direction follows the extrude rule |
| Normal Cut | Face groups, `CutDirection` and `OffsetPlaneReference` mirrored; flags unchanged; no toggles |
| Legacy (1b) | Fixed face mirrored; radius, allowance, offset ratio and auto relief unchanged; ProcessBends state as recorded |

### 6. Capture (`Capture/FeatureCapture.SheetMetal.cs`)

- **Order inside `Capture`:**
  1. `SheetMetalPart(f, type)` returns a `PartOfFeatureRecord` for the owned types (`SheetMetalTree.Owners`).
  2. Then the `switch` on the definition: `BaseFlangeFeatureData`, `FlatPatternFeatureData`, `SMNormalCutFeatureData2`, and `SheetMetalFeatureData` for the legacy anchor (1b).
  3. Everything sheet-metal runs inside `try { … } catch (Exception ex) { throw new NotSupportedException(…) }` (P13).
- **`CaptureBaseFlange`:** as in the draft, with these changes:
  - It prefers the late-bound `D1/D2EndCondition*` over `D1/D2Offset*` (P12).
  - It reads `OverrideDefaultSheetMetalParameters` and `UseMaterialSheetMetalParameters` late-bound.
  - `OwnBendAllowance` comes via `SwLate.Invoke(d, "GetCustomBendAllowance")`.
  - `CompanionDimensions = SwDoc.OwnDimensions(sm).Select(SwDoc.Named)` (P7).
  - `MergesIntoExisting`: the geometric test as in the draft, cross-checked by the shared-SheetMetal rule (P18).
- **`ReadParameters(doc, feature, bodyLevel)`:** as in the draft, with these changes:
  - `FixedReference` goes through `FaceOrEdgeRef`.
  - Return codes are kept.
  - `Defaults ?? Body` is used by the builders.
- **`CaptureFlatPattern`:** as in the draft (under `AccessSelections`).
  - `Flattened` comes from the session's record, not from the current (folded) state.
  - `Owner` is the base flange of the fixed face's body.
- **`CaptureNormalCut`:** as in the draft.
- **`CaptureExtrude`:** adds `LinkToThickness`.
- **`MirrorJob` after capture:**
  - The owner check (l.150–153) and the Esc withdrawal (l.204) use `BringsParts`.
  - `CaptureOnItsOwn` for a sheet-metal `PartOf` whose owner is not in the run gives `UnsupportedRecord(…, false)` with „Entsteht nur mit dem Basisflansch „…“.“ (SheetMetal) or „Gehört zur Abwicklung „…“.“ (sub-sketch).
- **`MassCapture`:**
  - "after" is `MeasureUntil`.
  - For a `FlatPatternRecord`: `EditRollback(End)`, then `folded = MassOf(doc)`, then `SetSuppression2(1, 1, null)` + `EditRebuild3`, then `flat = MassOf(doc)` and up to 8 samples on the flat pattern's faces, then suppress again + `EditRebuild3`, in a `try`/`finally`. `FlatChange = flat − folded`.
  - One flat pattern at a time, always starting from the folded state (the session folded everything).

### 7. Job changes (`MirrorJob`, `BuildContext`, `BuildResult`)

- **`BuildContext`:** `SheetMetalBodies` (original owner name → (SheetMetal, FlatPattern) of the copy) and `SheetMetalGuard? Guard`.
- **`BuildResult`:** `List<Companion> Companions` (Feature, original name, dimensions). `RemoveHelpers` is set by the sheet-metal builders.
- **`BuildOne`:**
  - the guard snapshot before `Build` and the check after `Verify`/`RemoveAgain` (§3);
  - `RestoreDimensionNames` also runs for each companion (P7).
- **`RemoveAgain`:** calls `result.RemoveHelpers` before `RemoveStepHelpers`, and unregisters companions.
- **`RemoveLeftovers`:** the snapshot includes the contents of both folders, and it deletes new SheetMetal/FlatPattern sub-features too. Template folders are never deleted (system types).
- **`DeleteOriginals`:** refused when sheet-metal records were built (P8).
- **Report:** a sheet-metal `PartOf` reads „Teil von „Base-Flange1“; entsteht mit diesem.“. `WithdrawParts` works unchanged through `PartOwners`.

### 8. Builders

**`BaseFlangeBuilder`**
1. Sketch map. `PlanSeparateBody(ctx, false, r.MergesIntoExisting, out scope, out createMerge)`. Refusal as in the draft.
2. `(revDir, revThk) = SheetMetalRules.BaseFlangeFlags(r.ClosedProfile, r.ReverseDirection, r.ReverseThickness, σ)`. Resolve the end references as `ExtrudeBuilder.ResolveEnd` does (blind equivalent, else fail).
3. **Creation values:**
   - `first` = the target has no template and no SheetMetal yet.
   - **First body:** values = `Defaults ?? Body` (t, r), and PCBA = `OwnBendAllowance ?? (Defaults ?? Body).BendAllowance`.
   - **Tab:** values = the target body's current SheetMetal values, PCBA = `OwnBendAllowance`.
   - **Otherwise:** values = the current template values, PCBA = `OwnBendAllowance`.
   - In all cases `UseDefaultRelief = OwnRelief == null`.
4. `before = SwSheetMetal.Snapshot(doc)` (names including folder contents, and the body count).
5. **Attempts:**
   - **Order:** A (data object), B (v1 with `DirToUse` = `OffsetDirections`, then 1; end conditions as `swEndConditions_e`, then `swFlangeOffsetTypes_e`), C (v2 with `merge`/`scope`/`!scope`, bodies on mark 8).
   - For a tab in separate-body mode the order is C, A, B.
   - **Selection:** `SwSelect.Select(sketch.Feature, 0)` must return true, plus the own bodies on mark 8 for C with scope.
   - **After each attempt:** `made = NewSince(before)`. It must contain exactly one `SMBaseFlange`, and the body delta must be +1 (new body) or 0 (tab). Otherwise `RemoveNew(made)` and the next attempt.
   - **None succeeds:** fail with „SOLIDWORKS hat den Basisflansch nicht erzeugt (Wege: …).“
6. **One `FeatureEdit.BaseFlange`:**
   - `OffsetDirections`, the side flags, end types, distances, reverse offsets and references.
   - `SymmetricThickness` (late).
   - Thickness, radius and gauge only when `first`, or after `OverrideDefaultSheetMetalParameters = true` has taken (late).
   - If the edit fails: remove the flange and its companions, then fail.
7. **Parameters:**
   - In a new part when `first`: `ApplyAndCompare(template, Defaults ?? Body)`.
   - When the original body overrides: `SetOverrideDefaultParameter2(i, true)` on the new SheetMetal (return codes checked), then the values.
   - Read-back differences become notes.
   - Material-driven originals get the note (P10).
8. **Registration:** `Register(r.Name, f)`; `Register(r.SheetMetalName, made.SheetMetal)` if it exists; `Companions`; `SheetMetalBodies[r.Name]`; `RemoveHelpers` = delete the FlatPattern, SheetMetal and flange of `made`, then unregister.
9. **Alternatives:**
   - Closed profile: `ReverseDirection`; then `ReverseThickness` (not in the same part).
   - Open profile: `ReverseDirection`, then `ReverseThickness`.
   - None for mid-plane or symmetric.

**`FlatPatternBuilder`**
1. **Find the copy's flat pattern:** `SheetMetalBodies[r.Owner].FlatPattern`. Otherwise use the flat pattern among `GetFeatures()` of the body that holds the mirrored fixed face. Otherwise fail with „Im Ziel gibt es keine Abwicklung zu „…“.“
2. **Fixed face:** `FindFace`/`FindEdge` only. If it is not found, add the note „Feste Fläche nicht gefunden; SOLIDWORKS-Vorgabe bleibt“.
3. **One edit, as in the draft:** options, `SetAddCornerTrim`, `SetBreakCorners`, `ISetExcludedFaces`. If `AccessSelections` fails while the flat pattern is suppressed: unsuppress, edit, suppress again.
4. **Read-back:** including the fixed-face fingerprint.
5. **Flat check, only with VerifySteps** (§9).
6. `Register(r.Name, fp)`. The final flattened state is applied by the session.

**`ExtrudeBuilder`:** `LinkToThickness` in a second edit (P14).

**`NormalCutBuilder`:** as in the draft. Every mirrored face must be found, otherwise fail. Fallback to `AddSMNormalCut*`.

**`LegacyBendsBuilder` (1b):** as in the draft, plus snapshot/`NewSince`, the guard, and `RemoveHelpers` for SheetMetal/FlattenBends/ProcessBends.

### 9. Verification

- **Base flange, cut, Normal Cut, legacy:** the standard `StepVerifier` (volume, moment, face samples). Legacy measures up to `MeasureUntil`.
- **Sheet-metal parameters:** creation-time values (P9); read-back differences become notes (Warning); the guard handles the shared values.
- **Flat pattern:**
  1. bar at the end;
  2. `before = MassOf(SeparateBody ? OwnBodies() : doc)`;
  3. unsuppress and rebuild;
  4. `after`;
  5. `MassComparison.Compare(FlatChange.Mirror(ctx.Mirror), after − before, after)` and the mirrored `FlatSamples` on the flattened faces;
  6. suppress again and rebuild.
  - Pass: „Abwicklung geprüft“. Fail: Warning „Abwicklung weicht ab – K-Faktor/Biegezugabe/feste Fläche prüfen“. No toggles.
  - `FlatChange` null with VerifySteps on: „nicht geprüft“ plus a Warning.
- **Same part:** `OriginalUntouched` as today, plus the `SheetMetalGuard`.

### 10. Failure and cleanup

| Situation | Handling |
|---|---|
| Creation attempt leaves features but fails the check | `RemoveNew(made)` before the next attempt |
| No attempt succeeds | Remove everything new since the snapshot; Failed |
| Final edit fails | Delete flange and companions; Failed |
| `OriginalUntouched` fails | `RemoveAgain` → `RemoveHelpers` (companions) → guard check |
| Builder exception | `RemoveLeftovers` with the folder-aware snapshot |
| Guard cannot restore the template | Run aborted with the warning line; nothing more is built |
| Session cannot fold or restore | Before the build: run refused. After it: report line „Abwicklung „…“ ließ sich nicht wiederherstellen“ |
| Flat check cannot restore the suppression | Note; the session restores the final state |

### 11. Same part, new part, assembly

| | New part | Same part | Assembly |
|---|---|---|---|
| Template | Created by the first copied base flange with `Defaults ?? Body`; guarded afterwards | Never changed; guarded | As new part |
| Body overrides | Set on each new SheetMetal | Set on each new SheetMetal | As new part |
| Gauge table | First body only; later bodies compare | Is the original's (one per part); never written | As new part |
| Flattened original | Folded for the run, copy flattened at the end | Folded for the run, both restored at the end | Referenced configuration (also a flat-pattern derived one) folded, restored before `AssemblyMirror` resets the configuration; the new part's only configuration gets the state |
| Merge/tab | `createMerge` | Scope on own bodies (path C first); first body never merges | As new part |
| Delete original | n/a | Refused in phase 1 (P8) | n/a |
| Equations | `Thickness@Sheet-Metal1` etc. copied through companion dimensions | Not transferred (as today), noted | As new part |

### 12. Implementation plan

1. **Core:**
   - `SheetMetalRecords.cs` (§4) and the `FeatureRecord` virtuals.
   - `SheetMetalRules`: `BaseFlangeFlags`, `InPlaneSideFlag`, `FromFlangeOffset`/`ToFlangeOffset` (unknown → null), `LegacyArguments`, `Differences`, `CreationValues(first, tab, defaults, body, template)`, `ExpectedBodyDelta(tab)`.
   - `SheetMetalTree` (§2).
   - `UnsupportedFeatures.SupportedSheetMetal` and the new `RefusesRun`.
2. **Core tests** (§13); update `ReportTextTests`.
3. **Add-in infrastructure:** `SwDoc.SystemTypes`, `FeaturesInCreationOrder`, `SwLate.Invoke`, `Sw/SwSheetMetal.cs` (read/apply/compare with return codes, allowance object, `Snapshot`/`NewSince`/`RemoveNew` including folders), `SheetMetalSession`, `SheetMetalGuard`.
4. **Collector and job:** `Closure` and skip reasons; `BringsParts` at MirrorJob.cs l.150 and l.204; guard hooks in `BuildOne`; `RemoveAgain`/`RemoveLeftovers` with companions; companion dimensions; `DeleteOriginals` refusal; `MassCapture` flat change.
5. **Capture:** `FeatureCapture.SheetMetal.cs`, `CaptureExtrude.LinkToThickness`.
6. **Builders:** `BaseFlangeBuilder`, `FlatPatternBuilder`, `ExtrudeBuilder` (second edit), `NormalCutBuilder`; register them in `MirrorJob.Builders`.
7. **Phase 1b:** `LegacyBendsBuilder`, with FlattenBends/ProcessBends added to `SupportedSheetMetal`.
8. **Docs:**
   - ARCHITEKTUR: a "Blech" section with session, guard, companions and flat check.
   - STOLPERSTEINE: one entry each for shared defaults, companions, the flattened state, creation paths, lying writes, `DirToUse`, obsolete `D1Offset*`, and equations on SheetMetal; update 139.
   - README: the sheet-metal rows.
9. **Build** against 27.3 and 32.1 (2019 and 2024 add-in builds), run the Core tests, then the live tests (§14).

### 13. Core tests (`tests/Spiegelwerk.Core.Tests/SheetMetalTests.cs`)

1. `Closed_profile_plate_lies_on_the_mirrored_side`: random cases, σ ±1.
2. `Open_profile_contour_side_is_mirrored_under_model_a`: the `sidecheck.py` construction.
3. `Model_b_needs_a_flip_only_where_the_rules_differ`: model b with the draft rule fails exactly when σ = −1.
4. `Sketch_on_right_mirrored_at_right_flips_depth_keeps_side` and `Sketch_on_front_mirrored_at_right_keeps_depth_flips_side`.
5. `BaseFlangeFlags_closed_profile_never_touches_ReverseThickness`.
6. `Flange_offset_types_map_to_end_conditions_and_back`: 1–5 ↔ 0/3/4/5/6; 0, 6 and 99 → null.
7. `Creation_values_use_defaults_for_the_first_body_and_the_template_otherwise`; `Tab_takes_the_target_body_values`; `Pre_2013_original_uses_body_as_defaults`.
8. `Expected_body_delta_is_one_for_a_new_body_and_zero_for_a_tab`.
9. `Legacy_insert_bends_arguments_follow_the_allowance_kind`; `Offset_ratio_outside_0_05_to_2_is_rejected`.
10. `Parameter_differences_name_each_mismatch`: K, radius, thickness, kind, table path; within tolerance → empty.
11. **Flat check (analytic L sheet):** `Flat_change_of_the_copy_matches_the_mirrored_change`, `Wrong_fixed_face_fails_on_the_moment`, `Wrong_k_factor_fails_on_the_volume`, `Bend_allowance_off_by_0_1_mm_fails`, with K = 0.5 and K = 0.44.
12. **`SheetMetalTree`:**
    - `SheetMetal_belongs_to_its_base_flange`;
    - `Flat_pattern_subsketches_belong_to_the_flat_pattern`;
    - `Base_flange_brings_its_sheet_metal_and_flat_pattern`;
    - `Flat_pattern_without_its_owner_is_reported`;
    - `Template_folders_are_system_features`;
    - `Flat_pattern_inside_the_folder_is_listed_before_the_folder`;
    - `Feature_after_a_flat_pattern_gets_its_own_skip_reason`;
    - (1b) `Flatten_and_process_bends_belong_to_the_legacy_sheet_metal`.
13. **`UnsupportedFeatures`:**
    - `Supported_sheet_metal_types_no_longer_refuse_the_run`;
    - `Edge_flange_and_convert_still_refuse_the_run`;
    - the existing tests adjusted.
14. `Grain_direction_is_an_unoriented_line`: kept for a later API.

### 14. Live-test assumptions

These keep the draft's L1–L24, corrected as follows: L1 and L2 per P5, L19 per P1, L21 per P7, L22 moves to area 4.

| # | Assumption | Fallback / how it shows |
|---|---|---|
| S1 | Which path builds in 2019/2022/2025: A without `Initialize`, B with `DirToUse` = directions, C (closed and open profiles) | Attempt order; the report names the path |
| S2 | Path A merges or not with a touching body (no Merge member) | Body-delta check; path C with Merge = false |
| S3 | A second base flange created with the template values and overridden on its SheetMetal leaves the template and the other bodies unchanged | Guard: restore, or abort with the warning |
| S4 | `SetOverrideDefaultParameter2(0, true)` + thickness/radius on the new body's SheetMetal take effect in 2019 (CADapter: radius yes with override) | Read-back plus the guard; Warning |
| S5 | PCBA (path B/C) or `Initialize` (path A) sets the allowance of the first body and the template; `Type` written last | Flat check |
| S6 | Deleting a copy's base flange (`RemoveHelpers`) leaves no orphan SheetMetal or FlatPattern, and the folders stay | Snapshot comparison after the removal |
| S7 | The session's folding (`SetSuppression2` on the flat pattern) works under `UpdateFreeze`, in a multibody part per body, and restores cleanly | Refuse run; report line |
| S8 | `IFlatPatternFeatureData` edits work on the suppressed flat pattern; otherwise unsuppress, edit, suppress | As stated |
| S9 | Single-body 2013+ tree: is SheetMetal top-level, is Flat-Pattern1 top-level, does the Flat-Pattern folder appear only with the second body (same part!) | Snapshot covers both shapes |
| S10 | `IBody2.GetFeatures()` of a sheet-metal body lists SheetMetal/FlatPattern (collector closure, `IsOwnBody`) | Closure through `SheetMetalTree` by name |
| S11 | `GetOverrideDefaultParameter2` returns a non-zero code in single-body parts | Treat as "not applicable" |
| S12 | `D1EndConditionType` and `D1OffsetType` agree in newer versions | Trust the new one, note |
| S13 | Equations on `Thickness@Sheet-Metal1` resolve after the companion rename | Existing equation report |
| S14 | SOLIDWORKS fixes the open-profile side by the sketch normal and segment direction (model a) | "Materialseite" toggle; if model b is confirmed, use always-flip |
| S15 | A tab can be recognised by its shared SheetMetal parent | Geometric test |

Draft L3–L18 and L20, L23 and L24 stay as written.

**Sources**
- Help (GitHub mirror, `…/150560a9…/scratchpad/gh/help`, `…/gh/ex`): `IFeatureManager~InsertSheetMetalBaseFlange2`, `IBaseFlangeFeatureData` (2022 example), `…~ReverseThickness`, `…~D1OffsetType`, `…~D1EndConditionType`, `…~Initialize`, `IModelDocExtension~GetTemplateSheetMetal`, `IModelDoc2~SetBendState`, `ISheetMetalFeatureData~SetOverrideDefaultParameter2`, `…~FixedReference`, `ICustomBendAllowance~Type`, `IPartDoc~InsertBends2`, `IFlatPatternFeatureData~FixedFace2`, `ISMNormalCutFeatureData2`, the gauge-table example.
- Third parties (local copies): CADapter `…/gh/cadapter_sheet.md`, `…/gh/cadapter_sm.py`; soheil `…/gh/notes_soheil.md`; eyfel `…/ghsrc/eyfel_SheetMetal.cs`; tleide `…/smbasereview/dl1/tleide_program.cs`.
- Search summaries only (help.solidworks.com unreachable): [Multibody Sheet Metal Parts (2021)](https://help.solidworks.com/2021/English/solidworks/sldworks/c_multi_body_sm_parts.htm), [Flat Pattern (2019)](https://help.solidworks.com/2019/english/SolidWorks/sldworks/c_Flat_Pattern.htm), [FeatureManager Design Tree, sheet metal tutorial (2025)](https://help.solidworks.com/2025/English/swconntutorial/c_tut_sheetmetal_fmdt.htm), [Editing a Sheet Metal Part in a Flattened State (Hawk Ridge)](https://support.hawkridgesys.com/hc/en-us/articles/115006282047-Editing-a-Sheet-Metal-Part-in-a-Flattened-State), [Sheet-Metal Multibody Parts in SOLIDWORKS 2013 (CATI)](https://www.cati.com/?p=119507).
