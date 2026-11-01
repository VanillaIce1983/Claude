# Cross-check of the Area 2 sheet-metal design (edge flange, miter flange, hem, jog, swept flange)

Checked: `docs/entwurf/blech-laschen.md` against the working tree of `/home/user/installer-lane/spiegelwerk` (branch `installer-lane`, HEAD `54a24d7`, clean), against the interop assemblies 27.3.0 (SOLIDWORKS 2019) and 32.1.0, against the saved API help and the third-party code the draft cites, and against the neighbouring drafts `blech-grundlagen.md` (Area 1), `blech-biegungen-ecken.md` (Area 3) and `blech-umformwerkzeuge-ersatzweg.md` (Area 4). Line numbers refer to the current files. Nothing outside the scratch folder was changed.

**Tools and evidence**
- Interop reflection: `dotnet …/scratchpad/mirrorreview/out/mirrorreview.dll <dll> members|enum|types …` on `solidworks.interop.sldworks` and `…swconst` 27.3.0 (`lib/`) and 32.1.0 (`lib/netstandard2.0/`). Member dumps of the four data interfaces and `IOneBendFeatureData` for both versions are in `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech/work-laschen/`.
- Maths: `…/blech/work-laschen/anglecheck.py` (40 000 random reflections) checks the edge-flange angle rule of P1. Largest error 1.7e-15.
- Saved help pages: `…/150560a9-…/scratchpad/apidocs/` (current help, GitHub mirror). Third-party code: `…/scratchpad/ghsrc/` (tbcphutai `EdgeFlangeReplay.cs`, eyfel, soheil notes, CADapter).
- help.solidworks.com does not resolve from this session (ENOTFOUND). The swept-flange version facts in P4 come from web-search summaries of help.solidworks.com pages and are marked as such.

**Result.** 16 problems. Five of them can give wrong geometry without an error, or keep the area from working at all:
- **P1:** the edge-flange angle and sketch-angle convention.
- **P2:** profiles are read in the rolled-back state but compared in the final state.
- **P6:** the jog fixed side: point space, a pick point near the line, and a support face that the jog itself splits.
- **P3:** Area 2 cannot run at all until the refusal and the Area 1 base are in place, and it duplicates Area 1 and Area 3 infrastructure.
- **P5:** per-bend overrides on the `OneBend` sub-features are lost without any notice.

The draft's claim about swept flanges is wrong (P4).

The area can be implemented as corrected in (2). Hem and miter flange are low risk, the edge flange is medium risk and needs a measured angle calibration, and the jog carries the most live-test risk.

---

## Verified as correct

**API members (27.3.0; the same in 32.1.0 unless noted).**
- `IEdgeFlangeFeatureData`: every property and method the draft lists exists with the stated type, including `AddEdges(object, object) → int`, `RemoveEdges`, `GetEdgeCount`, `GetPositionReferenceType`, `Get/SetCustomBendAllowance` and the obsolete `Edge`. 32.1.0 adds exactly `AngleReference`, `LockAngle`, `NormalToFlangePlane` and `PerpendicularToFace`, so late binding through `SwLate` is right.
- `IMiterFlangeFeatureData`: as listed. There is only `IGetEdgesCount()` and `IGetEdges()` with no parameter; there is no `GetEdgesCount`.
- `IHemFeatureData`: as listed. There are indeed **no relief members**.
- `IJogFeatureData`: as listed. `IGetFixedPoint()` returns a single `double`, so in .NET it is useless; use the `FixedPoint` object.
- `ICustomBendAllowance`: `Type`, `KFactor`, `BendAllowance`, `BendDeduction`, `BendTableFile`.
- `IFeatureManager.CreateCustomBendAllowance()`, `CreateDefinition(int)` and `CreateFeature(object)` exist.
- Creation calls, all with the stated signatures:
  - `IModelDoc2.InsertSketchForEdgeFlange(object, double, bool) → object`, with the typed variant `IInsertSketchForEdgeFlange → Feature`.
  - `IFeatureManager.InsertSheetMetalEdgeFlange2(…13…) → Feature`. Its `I…` variant takes `ref Feature SketchFeat`, so `Feature[]` is the native array type.
  - `IFeatureManager.InsertSheetMetalEdgeFlange(Edge, Feature, …) → Feature`.
  - `IFeatureManager.InsertSheetMetalMiterFlange(…14…) → Feature`.
  - `IFeatureManager.InsertSheetMetalHem2(…16…) → Feature`.
  - `IModelDoc2.InsertSheetMetalJog(double, double, double, bool, bool, short, short) → void`.
- These do not exist in 27.3.0 or 32.1.0: `InsertSheetMetalMiterFlange2/3`, `InsertSheetMetalJog2`, any jog method on `IFeatureManager`, and creation IDs for miter flange, hem or jog. `swFeatureNameID_e` has `swFmEdgeFlange = 37` in both versions and `swFmSweptFlange = 117` only in 32.1.0.
- `ISweptFlangeFeatureData` is absent in 27.3.0 and present in 32.1.0, with `Profile`, `Path`, `CylindricalOrConicalEdge`, `MaterialInside`, `FlangePosition`, `ReverseDirection`, `FlattenAlongPath`, offsets, relief, allowance and gauge members.
- Other members used: `ISketch.GetReferenceEntity`, `ISketch.ModelToSketchTransform`, `IFeature.GetOwnerFeature() → Feature`, `IEdge.GetTwoAdjacentFaces2() → object`, `IModelDocExtension.SelectByRay(…11…)` and `IFace2.GetClosestPointOn`.

**Enums (27.3.0 = 32.1.0 unless noted).**
- `swFlangePositionTypes_e` 1–6, `swFlangeDimTypes_e` 1–3, `swInsertEdgeFlangeOptions_e` 1/2/4/…/128, `swSheetMetalReliefTypes_e` 1–5, `swReliefTearTypes_e` 1/2.
- `swHemPositionTypes_e` 0/1, `swHemTypes_e` 0–4.
- `swJogOffsetTypes_e` 1–5, `swJogDimensionPositionType_e` 1–3, `swJogPositionType_e` 1–4.
- `swBendAllowanceTypes_e` 1–5.
- `swFlangeOffsetTypes_e` 1–5, plus `swFlangeOffsetUptoEdgeAndMerge = 6` in 32.1.0 only.

**Claims about the existing code that hold.**
- Capture dispatches on the `GetDefinition()` type (FeatureCapture.cs:47–89), using the coclass interfaces (`ExtrudeFeatureData2` and so on), which is right with embedded interop types.
- Sketches owned by a feature are handled through `PartOfFeatureRecord` (HoleRecords.cs:77) and `HolePart` (FeatureCapture.Holes.cs:25). A cheap one-time tree walk exists, and the fix-up `holes`/`CaptureOnItsOwn` is at MirrorJob.cs:150–153.
- `WithdrawParts` (MirrorJob.cs:401 ff.) is generic over `PartOwners`.
- `StepVerifier.MissedSamples` falls back to all faces of the eligible bodies (StepVerifier.cs:106–121), so samples on bend sub-feature faces are found without a change there.
- `MassCapture.SampleFaces` uses only `f.GetFaces()` (MassCapture.cs:145–161), so hem faces, which belong to `HemBend` (soheil, VERIFIED), would be missed. The draft's §2E is needed.
- `SketchBuilder.BuildInto` exists (SketchBuilder.cs:99), and `ReplaceInto` would be a small variant. `SketchMirror.Mirror` throws when the target plane is not R(source plane) (SketchMirror.cs:39–55), so a rebase is indeed needed for offset profiles.
- Sketches are only ever opened on reference planes, standard or helper (`PlaneResolver.Resolve`, SketchBuilder.cs:51–56), so a sketch on a face is new code (§2F).
- `ToGeometryRef` turns a straight edge into an `Axis` (FeatureCapture.cs:364–368), so `FlangeRef` is needed for edge references.
- `DirectionRules.ReverseFlag/Sigma`, `SwLate.Get/Set/Call`, `TopologyFinder.FindEdge/FindFace/FindVertex/Resolve`, `BuilderSupport.Safe/Find/SelectFaceAt` and `FeatureEdit.Edit` all exist as the draft uses them.
- No Mirror API is used anywhere in the draft. There is no `InsertMirrorFeature*`, `MirrorPart*` or derived part. The Area 4 fallback the draft hands over to uses `Body2.Copy2` plus `ApplyTransform`, not a mirror feature.

**Third-party facts the draft quotes, checked in the local copies.**
- tbcphutai (SW 2024), `EdgeFlangeReplay.cs`:
  - `CreateDefinition(swFmEdgeFlange)` → `AddEdges(Edge[], Sketch[])` → `CreateFeature` works (l.526–610).
  - `Edges` reads back null on a fresh definition (l.606–607).
  - `ReverseOffset` must be committed in a separate transaction after binding (l.497–503).
  - Toggling is not idempotent (l.859–862).
  - `LockAngle` must be false before `BendAngle` is set (l.462–469).
  - A profile can sit at the top level before its flange (l.794).
  - The native profile X axis is parallel to the hinge (l.1028–1032).
- eyfel (SW 2026): `InsertSketchForEdgeFlange` makes a default rectangle and, on one path, does not leave the sketch open (l.218–224, 233). `InsertSheetMetalEdgeFlange2` takes `Feature[]` profiles (l.159–160, 312–313).
- soheil (VERIFIED):
  - Hem: edges on mark 0, null allowance accepted, `HemBend` owns the faces, a teardrop at 270° gives nothing.
  - Miter flange: sketch on mark 0 and edges on mark 1, with chains of 2–4 edges; sub-features `Miterbend<n>`.
  - Jog unresolved.
- CADapter (SW 2017/2026):
  - The jog angle is in **degrees**; radians are a silent no-op.
  - The jog's fixed-face pick point must not lie on the line.
  - A miter flange with the edge on mark 0 gives one flange only.
  - Per-feature allowances: edge flange, miter flange, hem and jog accept a K-factor, **but the jog refuses a bend table**.
  - Writing `IOneBendFeatureData` has no effect.

**Mirror rules that hold.**
- Bend angle as a geometric angle, radius, lengths, gaps, miter gap and relief type/ratio/width/depth/tear type are unchanged. Reliefs are symmetric about the bend.
- `PositionType`, `OffsetDimType`, `TrimSideBends`, hem type/position/length/angle/radius, jog angle/offset/dimension position/position type and `FixProjectedLength` are unchanged. Each is defined by the outer and inner faces of base and flange, which R maps onto the copy's corresponding faces.
- K-factor, bend allowance, bend deduction and bend table are unchanged, because tables are indexed by (t, r, angle, material).
- The hinge, hem and miter edges, the jog fixed face and the up-to references are the mirrored fingerprints.
- The jog fixed side is R(point).
- Profile 2D content is mapped through `SketchMirror` into the native frame (σ from `AxisMap`).
- The miter flange's start/end offsets are unchanged, with a swap as an alternative.
- Flat pattern: up/down bend directions relative to the fixed face are preserved, because outward normals stay outward. The flat pattern of the copy is the mirror image of the original's, which is what an opposite-hand part needs. The fixed face and the flat check belong to Area 1.

---

## (1) Problems

### P1. Edge flange: "BendAngle unchanged" and the four sketch-angle candidates do not match the measured directed-angle convention
- **Evidence.**
  - tbcphutai treats the sketch angle as a rotation about the **directed** hinge: `ReflectedAngle(source, reversed)` with the comment "R Rot(t,a) R⁻¹ = Rot(Rt,−a); an antiparallel target hinge reverses sign again" (EdgeFlangeReplay.cs:1205–1211). The final angle is solved by measuring the native frame's response to a small change of angle (`SolveDirectedAngle62`, l.976–1022, call at l.1001).
  - tbcphutai then writes that calibrated value, possibly 2π − θ, into `BendAngle` of the definition (l.360, 467, 554). It compares only `cos(BendAngle)` (l.1100).
  - tbcphutai also measured that changing `BendAngle` after the profile frame was read "rotated the newly reflected sketch a second time on non-right-angle bends" (l.347–349).
  - My check (`anglecheck.py`, 40 000 cases) confirms the conjugation: with the copy's hinge along R(t), the required angle is −θ; with the hinge along −R(t), it is +θ.
  - The draft's candidates are {θ, π−θ} × FlipDir. They contain −θ only if `FlipDir` is a "side flip" of the flange. The help says it "reverses the offset direction of the flange", which is not documented as that. If it is not a side flip, the set lacks −θ in **every** case where the copy's edge runs along R(t) (20 000/20 000 in the check).
  - Path A/B then writes `BendAngle = r.BendAngle` (θ) after a profile that was created at another angle. By tbcphutai's measurement this re-rotates the profile. The read-back `Differences` with 1e-9 rad would report a false difference when the angle that fitted is 2π−θ.
  - With several edges, `BendAngle` is one value, while the fitting creation angle can differ per edge (tbcphutai needs `RequireCommonFlangeAngle61`, l.344 and l.422).
- **Failure.** For θ ≠ 90°: no candidate fits, so a flange that could be built fails. Or it fits and is then rotated by setting θ, so the profile check reports a mismatch that no `ReverseOffset` toggle repairs. That gives a Warning at best, or wrong geometry when VerifySteps is off and the profile check is skipped.
- **Fix.**
  - Candidates are ordered: first the predicted angle (−θ mod 2π when t′·R(t) > 0, else θ), then {θ, 2π−θ, π−θ, π+θ} × FlipDir ∈ {ReverseOffset, ¬ReverseOffset}, de-duplicated mod 2π.
  - Optionally use tbcphutai's two-probe measured solve.
  - **One (angle, flip) pair must fit every edge** of the flange. Otherwise the flange fails with a reason (fallback).
  - The definition gets `BendAngle = chosen angle` and `ReverseOffset = chosen flip`, never the raw θ.
  - Read-back compares the geometric angle `Fold(a) = a mod 2π folded into [0, π]`.

### P2. Profile geometry is read in the rolled-back state, but the position offset and the profile check need the final state
- **Evidence.**
  - `ReadSketch` rolls the bar to "after" the sketch whenever the sketch refers to model edges or vertices (FeatureCapture.cs:103–120). Flange profiles almost always do, through the line on the hinge. For an absorbed profile that state is "before the flange".
  - The flange itself places its profile: the position offset translates the sketch along its normal (tbcphutai l.388–392). tbcphutai therefore snapshots the profiles **before** any rollback (l.73: "Snapshot all absorbed profiles before AccessSelections changes rollback state").
  - The draft documents `FlangeProfile.Sketch` as "as SOLIDWORKS shows it (position offset included)", but reads it with `ReadSketch`. Its profile check compares the copy in the **final** state with "R(source points)".
- **Failure.** If the rolled-back frame lacks the offset, every candidate toggle fails the check. The result is "restore and warn" for a correct flange, or a wrong toggle accepted by mass alone. `RebaseOntoHinge` on a frame that is already at the hinge hides the offset completely.
- **Fix.**
  - Read the profile **twice**: `_sketches.Read(f)` without rollback for frame and points (the final state, used for the rebase and the check), and `ReadSketch(f)` only for the model-edge and vertex fingerprints of the relations. Require the same segment ids.
  - The profile check is split into three parts: (a) plane normal ∥ R(n); (b) in-plane deviation after rebasing both onto their hinge; (c) the signed normal offset of the copy against R(source offset), within `Tol.Length`.

### P3. Area 2 cannot run on its own, and it duplicates Area 1 and Area 3 infrastructure
- **Evidence.**
  - `UnsupportedFeatures.RefusesRun` refuses every run that collects `SheetMetal`, `EdgeFlange`, `SMMiteredFlange`, `Hem` or `Jog` (UnsupportedFeatures.cs:12–28, MirrorJob.cs:138). The draft never says how or when this is narrowed.
  - An edge flange, hem or jog needs a sheet-metal body, which only Area 1's `BaseFlangeBuilder` produces.
  - Value classes: Area 1 defines `BendAllowanceData`/`ReliefData` (with `BendAllowanceKind` and `ReliefKind`) and `SwSheetMetal.NewAllowance(fm, BendAllowanceData)`. Area 2 defines `BendAllowanceSettings`/`ReliefSettings` and its own `SwSheetMetal.ReadAllowance/NewAllowance`.
  - Ownership: Area 1 adds `FeatureRecord.BringsParts`. Area 2 adds `IOwnsSketches`.
  - Sketch on a face: Area 3 adds `SketchRecord.SupportFace` and `BuildContext.FaceSupportedSketches` (blech-biegungen-ecken.md:89–91). Area 2 adds `SketchModel.SupportFace` and an `OnSupportFace` flag.
  - Area 3's `FindBend` relies on the flanges of Area 2 being in `ctx.Created`, and on the ordinal of each bend among the owner's `IOneBendFeatureData` sub-features.
- **Failure.** Nothing in Area 2 is reachable. Implemented as written, two parallel class sets and two sketch-on-face mechanisms appear.
- **Fix.**
  - Area 2 is implemented after Area 1 P1. The refusal becomes a per-type rule: `UnsupportedFeatures.RefusesRun` refuses only sheet-metal types without a builder and without the Area 4 fallback.
  - Use Area 1's `BendAllowanceData`/`ReliefData`/`SwSheetMetal` and one ownership API on `FeatureRecord` (see (2) §3).
  - Use Area 3's `SketchRecord.SupportFace` plus `BuildContext.FaceSupportedSketches`, with jog sketches added.

### P4. The swept-flange facts are wrong: the feature is old, only its API is new
- **Evidence.**
  - Search summaries of help.solidworks.com pages list a "Swept Flanges" What's New entry for **2012**, a 2014 topic "adding swept flange to conical body", a 2017 "Swept Flange" topic and a **2019** "Swept Flange PropertyManager" page. The `ISweptFlangeFeatureData` member pages give availability "SOLIDWORKS 2021 FCS, Revision Number 29". The help site itself was not reachable here, so this rests on search summaries.
  - The interop agrees: the interface is absent in 27.3.0 and present in 32.1.0.
  - The `GetTypeName2` table (offline copy `fc2/llmdocs/x/types/IFeature/GetTypeName2.md`) has **no** swept-flange entry, so `"SweptFlange"` is unconfirmed.
  - `UnsupportedFeatures.SheetMetal` lacks it too.
- **Failure.**
  - "Parts made in 2019 cannot contain one" is false. 2019 and 2020 users will meet swept flanges that the API cannot read.
  - The report text "API erst ab 2020" is wrong.
  - Dispatching by an unverified type name can miss the feature.
- **Fix.**
  - On 2019 and 2020, any feature whose definition is not a known interface already becomes `UnsupportedRecord(ChangesSolid: true)` through the generic path (FeatureCapture.cs:90), which is the right fallback trigger. Keep that, and do not depend on the type name.
  - On 2021+, try a late-bound read through `SwLate.Get(def, "FlattenAlongPath")` and similar members. A builder (phase 2) is optional.
  - Report text: „Gesweepte Lasche: Feature-Daten erst ab SOLIDWORKS 2021 lesbar – Ersatzweg.“
  - Add the observed type name to `UnsupportedFeatures.SheetMetal` once the live test confirms it.
  - If the phase-2 builder is built, `ReverseDirection` (side of the open profile) starts from Area 1's in-plane rule `InPlaneSideFlag(rev, σ)`, not from a blind copy.

### P5. Per-bend overrides on the `OneBend` sub-features are lost without notice
- **Evidence.**
  - `IOneBendFeatureData` (27.3.0 = 32.1.0) has `UseDefaultBendRadius`, `BendRadius`, `UseDefaultBendAllowance`, `GetCustomBendAllowance`, `KFactor`, `BendAllowanceType`, `BendTableFile`, `UseDefaultBendRelief`, `AutoReliefType`, relief values and `BendOrder`. A user can override a single `EdgeBend<n>`.
  - CADapter measured that writing `IOneBendFeatureData` returns true and changes nothing.
  - The draft reads only the flange-level values. The folded-state mass check cannot see a K-factor or allowance difference.
- **Failure.** The copy's flat pattern differs from the mirrored original's flat pattern, and the step is reported "OK". Only Area 1's flat check would notice, and only with VerifySteps on.
- **Fix.**
  - At capture, read each bend sub-feature (definition `is OneBendFeatureData`) under its own `AccessSelections`. Compare it with the flange level.
  - If any bend has its own radius, allowance or relief, add a record note naming the bend and the value: „Biegung „EdgeBend2“ hat eine eigene Biegezugabe (K 0,38); über die API nicht übertragbar.“ That makes the step a Warning.
  - `BendOrder` differences are a note only.
  - Re-read the copy's bends after the build for the same comparison.

### P6. Jog: the fixed side is fragile in four ways, and a wrong side is silent
- **Evidence.**
  - CADapter measured that a fixed-face pick point lying on the bend line makes SOLIDWORKS "rotate half the sheet 90 degrees … with no error at all". The draft uses R(FixedPoint) directly as the pick point, and the original's point can be close to the line.
  - The `SelectFaceAt` fallback selects the face **without** a point (BuilderSupport.cs:166). That loses the side information completely.
  - `IJogFeatureData.FixedPoint` space is undocumented. The analogous `ISketchedBendFeatureData.GetFixedFace` returns a point "in the space of the sketch … z is always 0" (Area 3 [D]). The draft assumes model space.
  - The jog's sketch support face is the face the jog splits. `GetReferenceEntity` read in the final state (`ReadSketch` rolls back only if the sketch has model references, which a plain jog line often lacks) fingerprints a post-jog face. `FindFace` then fails in the target, or finds a wrong piece.
  - `SelectByID2` with coordinates picks through the screen and snaps to a pixel, 0.1–0.15 mm off (soheil).
- **Fix.**
  - Decide the point space deterministically. A point is in model space if `DistanceToFace(fixedFace, p) ≤ 10 µm`. Otherwise, if |p.Z| ≤ Tol.Length, convert it with the sketch frame. Otherwise treat the point as unknown.
  - Use the point **only to choose the side**. The pick point is always `JogGeometry.PointBeside(R(face), R(line), side)`, at least a quarter of the face size from the line.
  - Select with `SelectByRay` along −n′ from outside the body, check with `IsSameFace`, and **never** fall back to a selection without a point. If the pick fails, the jog fails.
  - Capture `SupportFace` with the bar forced right after the sketch.
  - After creation, run an always-on fixed-side probe: R(p_fixed) must still lie on a face of the copy within 10 µm. Otherwise delete the jog and fail with „Feste Seite des Jogs nicht gehalten“.

### P7. One combined `ModifyDefinition` per feature loses everything when one value is refused (jog bend table)
- **Evidence.**
  - CADapter: the jog accepts a K-factor but **refuses a bend table**. `ModifyDefinition` returns false and the re-read reverts.
  - Without `UseDefaultBendAllowance = false` first, the allowance "silently reverts". Area 1 adds that `Type` must be set last.
  - The draft's `FeatureEdit.Jog` (§5 step 7) sets angle, positions, offset, references, flags, radius, allowance and `FixedPoint` in one edit. `FeatureEdit.Edit` returns false as a whole (IFeatureBuilder.cs:192–210).
- **Fix.**
  - Two edits per feature. First all geometric scalars and references. Then the allowance alone, with `UseDefaultBendAllowance = false` → values → `Type` last → `SetCustomBendAllowance`.
  - Read back after each edit.
  - Core `AllowanceRules.Settable(featureKind, kind)`: Jog + BendTable or BendCalculationTable → false, which gives a note and no attempt.
  - Do not set `FixedPoint` in the edit unless the read-back differs (P6).

### P8. Miter flange: the mark-0 retry produces a partial flange, and the draft does not say a mismatch is rejected
- **Evidence.**
  - CADapter: with the edges on mark 0, "selecting TWO edges does not propagate – only one flange comes out". The draft retries with mark 0 when mark 1 returns null.
  - Its step 4 only says "compare edge count … and BendCount". It does not say what happens on a mismatch.
  - `BendCount` counts `"OneBend"` by type name, but the miter's bends display as `Miterbend<n>` and the hem's as `HemBend<n>`, whose type names are unverified.
- **Failure.** A one-edge miter flange is kept as "Warning", or as "OK" when VerifySteps is off.
- **Fix.**
  - The mark-0 retry is allowed only for single-edge chains.
  - An edge-count mismatch means delete, try the next variant, and otherwise fail.
  - Count bends by definition (`GetDefinition() is OneBendFeatureData`), as Area 3 does. A count mismatch after a matching edge count is a note.

### P9. Hem: an unconditional relief note makes every hem a Warning
- **Evidence.**
  - `IHemFeatureData` has no relief members, while `InsertSheetMetalHem2` has relief parameters. The draft always adds „Entlastung des Saums nicht lesbar – Standard“.
  - A note gives Warning (BuilderSupport.Ok, l.178–184), and Warning blocks "Original löschen" (`AllMirrored`, MirrorJob.cs:705).
  - The draft also assumes one width sketch per hem (`FirstOrDefault`), although a multi-edge hem may have several.
- **Fix.**
  - A relief cut changes the folded body, so it is visible to mass and face samples. Add the note only when the step is not verified (VerifySteps off, or no reference) or the check fails. With a passing check, put "Entlastung: Standard (geprüft)" into the verification text.
  - Read all sketch sub-features of the hem and pair them with its edges as `Assign` does. If the pairing is ambiguous, keep SOLIDWORKS's own width sketches with a note.

### P10. A COM error during sheet-metal capture aborts the whole run
- **Evidence.**
  - `FeatureCapture.Capture` catches only `NotSupportedException` (FeatureCapture.cs:92). The capture loop (MirrorJob.cs:143–148) runs outside the run's try block.
  - The new capture adds many fragile calls: `GetReferenceEntity`, `ReadSketch` rollbacks, late-bound reads, `GetCustomBendAllowance` on features with the default allowance, and the `OneBend` reads of P5.
- **Fix.** `CaptureEdgeFlange`, `CaptureMiterFlange`, `CaptureHem` and `CaptureJog` each wrap their body in `try { … } catch (Exception ex) when (!(ex is NotSupportedException)) { throw new NotSupportedException($"Blech-Feature nicht lesbar: {ex.Message}"); }`. `ReleaseSelectionAccess` stays in `finally`. The result is an `UnsupportedRecord`, which is a fallback trigger.

### P11. "Read back with the same capture helper" would roll back the target during the build
- **Evidence.**
  - The capture helpers are bound to the source document (`new FeatureCapture(source, _math)`, MirrorJob.cs:141). Re-reading through them includes `ReadSketch` with `EditRollback`, which is a live-test item even for capture of copies (spiegel-features-gegenpruefung, live test 7).
  - The read-back only needs scalars.
- **Fix.** Use a dedicated `SwSheetMetal.ReadScalars(def)` under `AccessSelections` on the target feature: no sketches, no rollback, `ReleaseSelectionAccess` in `finally`. Then call `SheetMetalParams.Differences`.

### P12. Up-to and angle references are not limited to the copy, and a missing reference has no rule
- **Evidence.**
  - The draft requires own bodies only for hinges, fixed faces and the type-6 target edge. `OffsetReference`, `PositionOffsetReference` and `AngleReference` go through `BuilderSupport.Find`. In separate-body mode that can resolve to a face or vertex of the original at the mirrored place. The copy then depends on the original and breaks when "Original löschen" runs.
  - What happens when the reference is not found is unspecified.
- **Fix.**
  - In separate-body mode, every resolved reference must lie on an own body (or be a helper plane or axis of this job). Otherwise the feature fails.
  - Not found: fail with the reason. There is no blind guess, because the realized length cannot be recovered from the API reliably for every `OffsetDimType`.

### P13. Hole-specific code paths are not all named, and the top-level "parent sketch" rule is too broad
- **Evidence.**
  - Besides `holes` (MirrorJob.cs:150), these places are hole-specific:
    - the Esc path, `foreach (PositionedHoleRecord …) WithdrawParts` (MirrorJob.cs:204);
    - `RemoveAgain` (MirrorJob.cs:474–475);
    - `CaptureOnItsOwn` (FeatureCapture.Holes.cs:85–99), which assumes a position sketch.
  - The draft says "WithdrawParts stays as it is", but the Esc loop must change.
  - "Plus sketch parents of an edge flange" would also swallow a top-level sketch that is only an up-to reference (a sketch point) or is shared with another feature.
- **Fix.**
  - Generalize all four places through the common ownership API.
  - A top-level parent becomes `PartOf` the flange only if it is unabsorbed, its `GetReferenceEntity` is one of the flange's edges, and the flange has fewer profile sub-sketches than edges (tbcphutai's "external" layout).

### P14. Two Core tests encode a wrong or empty rule
- **Test 11 ("sign of (p − a)·(n × t) under R, with the copy's t and n, comes out consistent").** With n′ = R n and t′ = ±R t, (R(p−a))·(Rn × ±Rt) = ∓(p−a)·(n×t), because Qa × Qb = det(Q)·Q(a×b) and det R = −1. The sign flips or not depending on the B-rep direction of the copy's line. A test of a fixed sign would fail at random, or pass only for one convention.
  - **Fix:** test that R(p) lies on the side of R(line) that contains R(p), as distance and projection facts, and that `PointBeside` returns a point on that side.
- **Test 10 ("σ ±1 with ReverseFlag").** It restates `DirectionRules` and tests nothing new.
  - **Fix:** replace it with the angle and point-space tests in (2) §11.

### P15. Small API and compile corrections
- **`swEdgeFlangeError_e`** also has `MustSpecifyAtLeastOneEdge = 6` in 27.3.0 and `GenericError = 7` in 32.1.0.
- **`ISketch.GetReferenceEntity`** takes an `out int`, not a `ref int`.
- **The record outlines do not compile.**
  - Core has `Nullable` enabled, `TreatWarningsAsErrors` and C# 9 (Spiegelwerk.Core.csproj).
  - Public non-nullable fields (`string SketchName;`, `FaceSignature FixedFace;`) are left uninitialized.
  - No constructor calls `FeatureRecord(name, typeName)`.
  - The existing records use properties.
- **COM returns typed `object`** (`Edges`, `InsertSketchForEdgeFlange`, `FixedPoint`) need `(object)` casts under embedded interop (STOLPERSTEINE 49).
- **Array forms for `AddEdges`/`InsertSheetMetalEdgeFlange2`** should follow the repository's ladder (STOLPERSTEINE 101): typed `Edge[]`/`Sketch[]` (tbcphutai) or `Feature[]` (eyfel), then `object[]`, then `DispatchWrapper[]`.
- **`ReplaceInto`** must not set `_everyPointCounts` the way `BuildInto` does (SketchBuilder.cs:107). Flange profiles may get construction stand-ins.

### P16. Profile points leave out circles and arcs
- **Evidence.** `ProfilePoints` uses "non-construction segment endpoints". A circular cut-out in a flange profile has no endpoints, and an arc's bulge is not sampled.
- **Fix.** Also include arc and circle centres and arc midpoints. The set comparison stays order-free.

---

## (2) Corrected design: Area 2, flanges on edges

### 0. Verdict

The design is parametric and uses no Mirror API. Edge flange, miter flange and hem can be rebuilt on 2019. The jog can probably be rebuilt too: CADapter built it on 2017 and 2026, while soheil did not get it to work. The swept flange is unreadable before 2021, so on 2019 and 2020 it goes to the Area 4 fallback.

Order of risk: hem < miter flange < edge flange < jog. The corrected design changes the edge-flange angle handling (P1), the profile reading (P2), the jog fixed side (P6), the edit transactions (P7) and the integration (P3). The rest of the draft stands.

### 1. Prerequisites and integration

1. **Area 1 P1 first.** That covers `SheetMetalParameters`, `BaseFlange`, `FlatPattern` and the shared value classes `BendAllowanceData`/`ReliefData` plus `SwSheetMetal` (read, new allowance with `Type` last, compare).
2. **Refusal per type.** `UnsupportedFeatures.Rebuildable` is the set of types with a builder (Area 1 types, then `EdgeFlange`, `SMMiteredFlange`, `Hem`, `Jog`). `RefusesRun(type) = type == "MirrorStock" || (IsSheetMetal(type) && !Rebuildable.Contains(type) && !FallbackCovers(type))`. Until Area 4 exists, `FallbackCovers` is false. A run with an unreadable swept flange is then refused with the existing text, and nothing silently half-built remains.
3. **One ownership API on `FeatureRecord`.** It replaces both `IOwnsSketches` and `BringsParts`:
   ```csharp
   /// <summary>Sub-features that come into being with this feature (PartOf records point to it).</summary>
   public virtual bool BringsParts => OwnedSketches.Count > 0;
   /// <summary>Of those, the sketches that are registered as SketchMaps and must be forgotten on removal.</summary>
   public virtual IReadOnlyList<string> OwnedSketches => System.Array.Empty<string>();
   ```
   - `PositionedHoleRecord` returns `new[] { PositionSketchName }`. `BaseFlangeRecord` overrides `BringsParts => true`.
   - `MirrorJob` uses it in four places: the owner set at l.150, the Esc loop at l.204, and `RemoveAgain` at l.474–475, which becomes `Delete(…, withAbsorbed: record.BringsParts)` plus `ForgetSketch` for each owned sketch.
   - `CaptureOnItsOwn` branches on the owner kind: hole as today; flange profile or hem sketch → `UnsupportedRecord(changesSolid: false, "Profilskizze der Kantenlasche „X“, die nicht gespiegelt wird – entfällt mit ihr.")`.
4. **Sketch on a face.** Area 3's `SketchRecord.SupportFace` plus `BuildContext.FaceSupportedSketches` is filled with `JogRecord.SketchName`. The fingerprint is taken with the bar forced right after the sketch (P6).
5. **Bends for Area 3.** Register each flange under its original name, which `ctx.Register` already does. Bends are found by `IOneBendFeatureData` definitions in sub-feature order.

### 2. API (corrections to the draft's §1)

The draft's tables stand, with these changes:
- `swEdgeFlangeError_e` includes 6 (and 7 in 32.1.0).
- `GetReferenceEntity(out int)`.
- Use the `FixedPoint` object, not `IGetFixedPoint`.
- `IOneBendFeatureData` is read for P5 and never written.
- `IModelDocExtension.SelectByRay` is used for the jog face.
- Swept flange: data interface from 2021 and feature from 2012 or earlier. Type name: live test.

### 3. Records (`Spiegelwerk.Core/Records/SheetMetalFlangeRecords.cs`, C# 9, nullable-safe)

```csharp
public sealed class FlangeProfile
{
    public FlangeProfile(string sketchName, SketchModel finalState, EdgeSignature hinge)
    { SketchName = sketchName; Sketch = finalState; Hinge = hinge; }
    public string SketchName { get; }
    /// <summary>Read WITHOUT rollback: frame and points as the finished flange shows them (position offset included).</summary>
    public SketchModel Sketch { get; }
    /// <summary>Hinge edge, read under AccessSelections (state before the flange).</summary>
    public EdgeSignature Hinge { get; }
}

public sealed class BendOverride       // P5: a OneBend sub-feature that differs from its flange
{
    public BendOverride(string bend, string what) { Bend = bend; What = what; }
    public string Bend { get; }
    public string What { get; }
}

public sealed class EdgeFlangeRecord : FeatureRecord
{
    public EdgeFlangeRecord(string name, string typeName) : base(name, typeName) { }
    public List<FlangeProfile> Profiles { get; } = new();          // order of IEdgeFlangeFeatureData.Edges
    public double BendAngle { get; set; }                           // as read (radians)
    public bool UseDefaultRadius { get; set; }
    public double BendRadius { get; set; }
    public int OffsetType { get; set; }
    public double OffsetDistance { get; set; }
    public int OffsetDimType { get; set; }
    public bool ReverseOffset { get; set; }
    public GeometryRef? OffsetReference { get; set; }
    public int PositionType { get; set; }
    public bool TrimSideBends { get; set; }
    public bool UsePositionOffset { get; set; }
    public int PositionOffsetType { get; set; }
    public double PositionOffsetDistance { get; set; }
    public bool ReversePositionOffset { get; set; }
    public GeometryRef? PositionOffsetReference { get; set; }
    public double GapDistance { get; set; }
    public ReliefData? OwnRelief { get; set; }                      // null = UseDefaultBendRelief (Area 1 class)
    public BendAllowanceData? OwnAllowance { get; set; }            // null = UseDefaultBendAllowance (Area 1 class)
    public bool? LockAngle { get; set; }
    public bool? NormalToFlangePlane { get; set; }
    public bool? PerpendicularToFace { get; set; }
    public GeometryRef? AngleReference { get; set; }
    public int BendCount { get; set; }                              // sub-features whose definition is IOneBendFeatureData
    public List<BendOverride> BendOverrides { get; } = new();
    public override IReadOnlyList<string> OwnedSketches => Profiles.Select(p => p.SketchName).ToList();
}
```

- `MiterFlangeRecord(name, type, sketchName)`: `Edges`, radius, `PositionType`, `TrimSideBends`, `GapDistance`, `StartOffset`, `EndOffset`, `OwnRelief`, `OwnAllowance`, `BendCount`, `BendOverrides`.
- `HemRecord(name, type)`: `Edges`, `HemType`, `Position`, `Reverse`, `Length`, `Gap`, `Angle`, `Radius`, `MiterGap`, `OwnAllowance`, `WidthSketches` (a list of `(string name, SketchModel model, int edgeIndex)`), `BendOverrides`. `OwnedSketches` lists the width sketches.
- `JogRecord(name, type, sketchName, lineId, FaceSignature fixedFace)`: `FixedSide` (`Vec3?`, model space after disambiguation), `FixedPointSpace` (Model, Sketch or Unknown), radius, angle, offset type/distance/reference, `ReverseOffset`, `ReverseDirection`, `DimensionPosition`, `FixProjectedLength`, `JogPosition`, `OwnAllowance`, `BendOverrides`.
- Notes from capture, for example `BendOverrides` and a jog with a bend table, go into `Notes` and make the step a Warning by the existing rule (MirrorJob.cs:305).

### 4. Capture (`Capture/FeatureCapture.SheetMetal.cs`)

- Dispatch on the definition type: `EdgeFlangeFeatureData`, `MiterFlangeFeatureData`, `HemFeatureData`, `JogFeatureData`. Anything else falls through to the generic `UnsupportedRecord(ChangesSolid: true)`, which covers swept flanges on 2019 and 2020 (P4). Every method is wrapped as in P10.
- **`OwnedPart`** (generalized `HolePart`): one tree walk. Sketch sub-features of `EdgeFlange` and `Hem` are owned. A top-level parent sketch is owned only under the P13 rule.
- **`CaptureEdgeFlange`**:
  1. Under `AccessSelections`: edges, where a non-line edge raises NotSupported ("Kantenlasche an gekrümmter Kante (ab 2021 möglich) – Ersatzweg"); scalars; `FlangeRef` references (straight edge stays an edge); the allowance via `SwSheetMetal.Read`; late-bound members.
  2. Outside the access, per candidate profile: `final = _sketches.Read(f)` (no rollback), `refs = ReadSketch(f)` (rollback if needed), with the same segment and point ids required. `GetReferenceEntity(out _)` gives the support edge. `FlangeProfiles.Assign` pairs profiles and edges; null raises NotSupported.
  3. Bends: every sub-feature with a `OneBendFeatureData` definition. `BendCount`, and `BendOverrides` (P5).
- **`CaptureMiterFlange`**: `SketchOf`, edges in order, scalars, allowance, bends.
- **`CaptureHem`**: edges, scalars, allowance, bends, all width sketches (final-state read plus references), paired with the edges.
- **`CaptureJog`**:
  - `LineId` is the single non-construction line of the sketch.
  - Under access: `FixedFace` → `FaceSignature`, `FixedPoint` → `JogGeometry.FixedPointInModel(raw, sketchFrame, p => DistanceToFace(fixedFace, p))`, scalars, `OffsetReference` for offset types 2–4.
  - The jog sketch gets `SupportFace` read with the bar forced after the sketch.
  - `OwnAllowance` with `Kind ∈ {BendTable, BendCalculationTable}` adds a note (P7).

### 5. Builders (registered after `DraftBuilder`)

The common rules:
- All references are resolved before anything is created and wrapped with `Safe`.
- In separate-body mode, hinges, faces and **every reference** must be on own bodies or job helpers (P12).
- Each builder takes a snapshot of the top-level names. On any failure without an exception, it removes what it made itself (`RemoveLeftovers` semantics).

**EdgeFlangeBuilder**
1. Resolve the hinges `e′ᵢ` and the target hinge directions `t′ᵢ` (vertex order).
2. Candidates `FlangeProfiles.AngleCandidates(θ, t′ᵢ·R(tᵢ) > 0)` × flip. For each (angle, flip) in order, for every edge: `InsertSketchForEdgeFlange(e′ᵢ, angle, flip)`, close the sketch if it is active, and test `FitsFrame`:
   - X ∥ R(hinge);
   - the plane contains R(hinge) after the rebase;
   - n′ ⟂ R(d);
   - Y·R(d) ≥ 1 − 1e-8.

   Keep the first pair that fits **all** edges. Delete every rejected sketch and check that it is gone. If none fits: fail with „Keine passende Profilebene für Kantenlasche – Ersatzweg“.
3. `ReplaceInto` each native sketch with `FlangeProfiles.Rebase(profile.Sketch)`: in-plane content from the final-state read, relations to edges and vertices from the reference read.
4. Create:
   - **A:** `CreateDefinition(37)`, then `AddEdges` with the array ladder (P15) requiring 0, then scalars with `BendAngle = chosen angle` and `ReverseOffset = chosen flip` (`LockAngle` false first for type 6), then `CreateFeature`.
   - **B:** `InsertSheetMetalEdgeFlange2(Edge[], Feature[] …)` with `FlangeAngle = chosen angle`, then one edit with the geometric scalars only.
   - **C:** the single-edge call.

   Before trying the next path, delete a non-null result that has a rebuild error.
5. A separate edit for `ReverseOffset`/`ReversePositionOffset` (tbcphutai).
6. Profile check (P2), always on: `|n′ × R(n)| ≤ 1e-9`, in-plane deviation ≤ `Tol.Length`, and normal offset = R(source offset) ± `Tol.Length`. On a mismatch, toggle the search (ReverseOffset, ReversePositionOffset, both). Each toggle re-checks after undoing itself.
7. Allowance edit (P7), then `ReadScalars` and `Differences`, with the angle compared as `Fold(a)`. Edge count, `BendCount`, the copy's bend overrides compared with the record. Register the flange and the profiles under their original names.

**MiterFlangeBuilder**
- Select the sketch on mark 0 and the chain on mark 1 (appended, in order), then call `InsertSheetMetalMiterFlange(…)`.
- If that returns null and the chain has more than one edge, retry once with the chain selected first and the sketch appended on mark 0. With a single edge, also retry with the edge on mark 0.
- Edge count ≠ original → delete, next variant, else fail (P8).
- Then the geometric edit, the allowance edit and the read-back. Alternative: start/end offsets swapped, only if they differ by more than `Tol.Length`.

**HemBuilder**
- Edges on mark 0, `InsertSheetMetalHem2(…, cba-or-null, true, 0, 1, true, 0.5, 0, 0)`, then edits.
- Width sketches: keep a native sketch that already equals R(width sketch), otherwise `ReplaceInto`. If the pairing is ambiguous, keep the native ones with a note.
- Relief note per P9. Alternative: "Saumrichtung umgekehrt".

**JogBuilder**
1. The line comes from `ctx.Sketches[SketchName]`, which was built on the mirrored support face (Area 3 mechanism).
2. The fixed face must be an own-body face in separate-body mode. Side: from `FixedSide` if known, otherwise the side containing the larger part of the face area (a documented heuristic) plus a note.
3. Pick point: `PointBeside`, with `SelectByRay` along −n′ and the `IsSameFace` check. No selection without a point.
4. `InsertSheetMetalJog(deg(JogAngle), r, offset, ReverseFlag(ReverseDirection, σ), FixProjectedLength, (short)DimensionPosition, (short)(JogPosition − 1))`.
   - DimPos is 1-based and BendPos 0-based. This matches CADapter's measured result change (1/0 versus 3/2) and the `InsertSheetMetal3dBend` BendPos encoding (0 = centerline … 3 = bend outside).
   - The new jog is found by comparing top-level name sets. If none appears, retry once in radians, then with the sketch feature selected instead of the segment.
5. Fixed-side probe (P6): if it fails → delete and fail.
6. Geometric edit, then the allowance edit (a table is never attempted on a jog), then read-back.
7. Alternatives: "Jog-Richtung umgekehrt", "Versatz umgekehrt", both. "Feste Seite gewechselt" is offered only when `FixedPointSpace == Unknown`.

**Swept flange**
- 2019 and 2020: generic Unsupported, which goes to the fallback or a refusal (§1.2).
- 2021+: optional phase-2 builder, late-bound, with `ReverseDirection` from `InPlaneSideFlag` as the first guess (P4).

### 6. Mirror rules (corrected)

| Quantity | Rule | Why |
|---|---|---|
| Geometric bend angle, radius, lengths, gaps, miter gap, relief type/ratio/width/depth/tear type, `PositionType`, `OffsetDimType`, `TrimSideBends`, hem type/position/length/angle/radius, jog angle/offset/positions/`FixProjectedLength`, allowance | unchanged | Isometry. Position types refer to faces that R maps onto the copy's corresponding faces. Tables are indexed by (t, r, angle). |
| Edge-flange sketch angle and `BendAngle` value | first −θ (mod 2π) if t′·R(t) > 0, else θ; then the other candidates; measured by `FitsFrame`; one value for all edges | R Rot(t,θ) R⁻¹ = Rot(Rt, −θ) (checked to 1.7e-15). `FlipDir` is undocumented. |
| Profile 2D content | `SketchMirror` into the native frame, from the final-state read, rebased onto the hinge | P2 |
| Position offset | magnitude unchanged; direction decided by the normal-offset part of the profile check | reference direction undocumented |
| `ReverseOffset` | taken from the chosen candidate; confirmed by the profile check | undocumented reference |
| Hem `ReverseDirection` | unchanged, with an alternative | probably relative to the face; unverified |
| Jog direction | `ReverseFlag(flag, σ)`, with σ measured from the frame (+1 on the face) | |
| Jog fixed side | the side of R(line) that contains R(p_fixed); pick point via `PointBeside` | a point maps to a point; signs are not invariant (P14) |
| Per-bend overrides | not transferable; note | the API write has no effect (CADapter) |

### 7. Verification and alternatives

- **Deterministic and always on, inside the builders:**
  - the edge-flange profile check with its three parts;
  - edge counts;
  - the hem width sketch;
  - the jog fixed-side probe;
  - scalar read-back with the folded angle;
  - bend override comparison.
- **StepVerifier:** mass and face samples. `MassCapture.SampleFaces` adds faces of non-sketch sub-features when the record is a flange, hem or jog record, or when `GetFaces()` gives fewer than 2 faces. That covers `HemBend` and the bend faces.
- **Flat state:** Area 1's flat-state check is the only check that sees allowance differences.
- **Alternatives:** as in the draft §7. Edge-flange alternatives are offered only when the profile check could not confirm a variant.

### 8. Same part, new part, assembly

- **New part and assembly (the counterpart is built as a new part):** everything is rebuilt. Nothing special beyond Area 1.
- **Same part, separate body (whole part or bodies):**
  - all geometry and every reference on own bodies (P12);
  - the `OriginalUntouched` guard stays the safety net, because sheet-metal features have no feature scope;
  - type 6 (up to edge and merge) only into own bodies.
- **Same part, selected features:**
  - The flange attaches to the mirrored edge of the existing body. This is the typical case of a symmetric part that gets its opposite flange.
  - References may be on the original body, as for other features in this mode.
  - The mirrored edge must exist in the final state. A later split makes `FindEdge` fail, and the feature fails with a reason.
- **All modes:** the part must be folded during capture and build. Area 1 owns this precondition; it is listed as live test 15.

### 9. Failure and cleanup

- Builders that return `Failed` delete what they created themselves: candidate sketches, profiles, a partial feature.
- An exception goes to `RemoveLeftovers` (MirrorJob.cs:283), which removes new top-level features in two rounds.
- When the owner fails or is skipped, its owned sketches are withdrawn through the generalized `WithdrawParts`, including the Esc path.
- Capture problems become `UnsupportedRecord(ChangesSolid: true)`, which is the fallback trigger (P10).
- Build failures are reported with the reason. Re-planning into the fallback is Area 4's restart rule.

### 10. Implementation plan

1. **Core.**
   - Records (§3) on Area 1's value classes, and the ownership API on `FeatureRecord`.
   - `Planning/FlangeProfiles.cs`: `Direction`, `Rebase`, `NormalOffset`, `AngleCandidates`, `Fold`, `FitsFrame`, `InPlaneDeviation`, `Assign`, `IsFullEdgeLine`, `CommonCandidate`.
   - `Planning/JogGeometry.cs`: `FixedPointInModel`, `PointBeside`, `SideOf`.
   - `Planning/SheetMetalParams.cs`: `Differences` with `Fold`, and `AllowanceRules.Settable`.
2. **Add-in prerequisites.**
   - Ownership refactor at the four MirrorJob places and `CaptureOnItsOwn`.
   - `UnsupportedFeatures.Rebuildable` and the per-type refusal.
   - `MassCapture` sub-feature samples.
   - `SketchBuilder.ReplaceInto`.
   - Sketch on a face (shared with Area 3).
   - `SwSheetMetal.ReadScalars` and `ReadBends`.
3. **Phase 2a: hem and miter flange** (soheil-verified calls). Builders, capture, `FeatureEdit.Hem/MiterFlange` with split edits.
4. **Phase 2b: edge flange.** Candidates, profiles, paths A/B/C, profile check, toggles.
5. **Phase 2c: jog.** Support-face sketch, fixed side, probe.
6. **Phase 2d (optional, 2021+): swept flange**, late-bound.
7. **Docs.**
   - ARCHITEKTUR: "Blech: Laschen an Kanten".
   - STOLPERSTEINE:
     - the angle convention;
     - profiles read in the final state;
     - the jog pick point and its degrees;
     - the jog bend table;
     - per-bend overrides;
     - the hem relief policy;
     - the miter mark 0.
   - The live-test table (§12).
   - The README "Noch nicht" row.
8. **Builds and tests.** Build against 27.3.0 and 32.1.0, run the Core tests, then the live tests on 2019 first, then 2024.

### 11. Core tests (`SheetMetalFlangeTests.cs`, `JogTests.cs`)

1. `Sketch_angle_of_the_copy_is_the_conjugated_angle`: for random R, θ ∈ (0, π) and t′ = ±R t, Rot(t′, a′)·R u = R·Rot(t, θ)·u holds with a′ = −θ when t′ = R t and a′ = θ otherwise. Also: `AngleCandidates` lists that angle first.
2. `Candidates_cover_both_hinge_directions_without_relying_on_FlipDir`.
3. `Fold_maps_2pi_minus_theta_to_theta` and `Differences_compare_folded_angles`.
4. `Common_candidate_for_multi_edge_flange_or_none`: a candidate that fits all edges is chosen; disjoint fits give null.
5. `Flange_direction_is_mirrored_with_the_profile` (the draft's test 1).
6. `Frame_fits_whichever_way_the_copy_edge_runs` (the draft's test 2).
7. `Rebase_and_normal_offset_split_the_profile_check`: a 3 mm offset flips sign under the wrong `ReversePositionOffset`, while the in-plane deviation stays 0.
8. `Profile_deviation_detects_a_flange_flipped_about_its_hinge_and_counts_arc_centres` (P16).
9. `Assign_*`: the draft's tests 6–8.
10. `Hem_width_sketch_spanning_the_whole_edge_is_default` (the draft's test 9), plus `Two_width_sketches_pair_with_two_edges`.
11. `Fixed_point_space_is_decided_by_distance_to_face`: a model-space point on the face gives Model; (x, y, 0) off the face gives Sketch and is mapped by the frame; anything else gives Unknown.
12. `PointBeside_is_on_the_requested_side_and_at_least_a_quarter_face_away_from_the_line`, and `Side_sign_is_not_invariant_but_the_mirrored_point_is_on_its_side` (P14).
13. `Miter_swap_alternative_only_for_unequal_offsets` (the draft's test 12).
14. `Allowance_rules_refuse_tables_on_jogs_only`.
15. `Differences_list_only_deviating_values` (the draft's test 13), and `Bend_overrides_become_notes`.
16. `GeometryRef_mirroring_keeps_scalars` (the draft's test 14).
17. `Ownership_api`: a hole, a base flange and an edge flange report `BringsParts` and `OwnedSketches`. The withdraw-on-Esc set includes the flanges.

### 12. Live-test assumptions (2019 first, then 2024)

| # | Assumption | Safeguard if wrong |
|---|---|---|
| L1 | `CreateDefinition(37)` + `AddEdges` + `CreateFeature` works in 2019 | Paths B and C; the report names the path |
| L2 | The `InsertSketchForEdgeFlange` angle is a directed rotation about the B-rep hinge (tbcphutai); what `FlipDir` does | 8-candidate search with `FitsFrame`; otherwise fail and use the fallback |
| L3 | `BendAngle` may be set to 2π−θ, reads back as θ or 2π−θ, and gives the mirrored flat pattern (Area 1 flat check) | `Fold` comparison; flat check |
| L4 | The final-state read without rollback gives the offset profile; `ReadSketch` gives the same ids | Pair by geometry; otherwise Unsupported |
| L5 | The native sketch may be empty or hold a rectangle, and may be open or closed | `ReplaceInto` clears it; `ActiveSketch` is checked |
| L6 | Array forms for `AddEdges`/EF2 | The ladder from P15 |
| L7 | A separate edit for `ReverseOffset` settles the profile direction (2019 too) | Toggle search with re-check |
| L8 | Miter flange: sketch on mark 0, edges on mark 1, on 2019 | Order variant; an edge-count mismatch is rejected |
| L9 | Hem on mark 0 with a null allowance; width sketch(es) editable; the type name of `HemBend` | Keep the native width sketch with a note |
| L10 | Jog: degrees; DimPos 1-based and BendPos 0-based; segment on mark 0 plus face picked by ray | Radians and sketch-feature retries; the post-edit sets exact enums |
| L11 | The jog accepts a sketch on the mirrored face (not on a coplanar plane); the support-face fingerprint taken after the sketch is found | Fail with a reason |
| L12 | `FixedPoint` space (model or sketch); whether `FixedPoint` is settable | Disambiguation by distance; the "feste Seite" alternative only when unknown |
| L13 | The fixed-side probe detects a held wrong side (CADapter's 90° rotation case) | Mass check as a second line |
| L14 | Jog refuses a bend table on 2019 too; the K-factor is accepted | Note |
| L15 | The part is folded during capture and build (flat pattern suppressed); capture of a flattened original | Area 1 precondition |
| L16 | `OneBend` overrides are readable under `AccessSelections` on the sub-feature; the bend order of the copy equals the original's | Note only |
| L17 | The swept-flange type name; whether 2021+ late-bound `GetDefinition` members are readable through IDispatch | Generic Unsupported, then fallback |
| L18 | `GetLastFeatureAdded` with the flat pattern last; new features land before it | Name-set comparison (already the rule) |

**Sources**
- Local: `…/scratchpad/apidocs/*` (help mirror), `…/scratchpad/ghsrc/EdgeFlangeReplay.cs` (tbcphutai, SW 2024), `…/ghsrc/eyfel_SheetMetal.cs`, `…/ghsrc/soheil_notes.md`, `…/ghsrc/cadapter_sw_sheetmetal.py`, `…/fc2/llmdocs/x/types/IFeature/GetTypeName2.md`.
- Web (search summaries only; help.solidworks.com not reachable from this session):
  - [ISweptFlangeFeatureData::Path (2021)](https://help.solidworks.com/2021/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISweptFlangeFeatureData~Path.html)
  - [ISweptFlangeFeatureData::StartOffset (2025)](https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISweptFlangeFeatureData~StartOffset.html)
  - [What's New 2012: Swept Flanges](https://help.solidworks.com/2012/English/WhatsNew/c_Swept_Flanges.htm)
  - [Swept Flange PropertyManager (2019)](https://help.solidworks.com/2019/English/SolidWorks/sldworks/hidd_sheet_metal_dve_swept_flange.htm)
  - [Swept Flange (2017)](https://help.solidworks.com/2017/English/SolidWorks/sldworks/t_Swept_Flanges.htm)
