# Review of the Area 3 design (sketched bend, cross break, corners, unfold/fold, lofted bend)

I checked `docs/entwurf/blech-biegungen-ecken.md` against the working tree at `54a24d7`, which is clean. I also checked it against the other three sheet-metal drafts (`blech-grundlagen.md`, `blech-laschen.md`, `blech-umformwerkzeuge-ersatzweg.md`) and the saved API help and third-party sources. Nothing outside `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech` was modified.

I found **14 problems** (P1–P14). Five of them can give a wrong part, or lose data, without any error:

- **P1:** the fixed side of a sketched bend can be lost when the face is picked.
- **P2:** the bend-direction rule rests on an unproven convention. Third-party live evidence points to a different reference. With "Jeden Schritt prüfen" off, a wrong guess is reported as OK.
- **P3:** corner trims are never collected. Their relief settings cannot be read, the draft reads unrelated flat-pattern values instead, and "Original löschen" would delete them.
- **P4:** a sketched bend probably has no face samples of its own, so the draft's fixed-side retry has nothing to measure.
- **P5:** every `ModifyDefinition` on a sketched bend can reset its custom bend allowance, and the draft edits each one unconditionally.

Every API member the draft names exists in 27.3.0 with the stated signature. The mirror rules for angle, radius, position, K-factor, allowance, relief types, corner types and fixed faces are geometrically right. Nothing in the draft uses the mirror API.

As corrected below, the area can be built for the following features, once the area 1 prerequisites are in place (area 2 as well for flanges):

- sketched bend
- closed corner
- break corner
- unfold/fold
- cross break
- corner relief (SOLIDWORKS 2022 and later only)

These stay unsupported, each with a clear report entry:

- corner trim
- welded corner
- lofted bend (still refused, or left to the fallback)

**Scratch work**
- Member dumps of 27.3.0 and 32.1.0, with diffs: `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech/work-area3/*.27.txt`, `*.32.txt`, `types27.txt`, `types32.txt`. I made them with the existing probe `mirrorreview.dll`.
- Numeric check of the direction rule: `/tmp/claude-0/-home-user-Claude/a60c880b-f1a2-4892-8124-1288119ca304/scratchpad/blech/work-area3/bendcheck.py`. Over 2000 random cases the maximum error of "flip iff σ(reference) = −1" is 4.4e-16, and this holds whichever normal is the reference. Which normal SOLIDWORKS actually uses is the open question (P2).
- help.solidworks.com does not resolve from this session (ENOTFOUND). Claims that only the online help could settle stay marked [?]. Web-search summaries of the 2017 Corner-Trim and 2019/2020 Sketched-Bend help pages were readable. The local copies of the help (streetartist mirror, `area3docs`) were used.

## Verified as correct

**Data interfaces (27.3.0 vs 32.1.0, by reflection)**
- `ISketchedBendFeatureData`:
  - In both versions: `BendAngle`, `BendRadius`, `PositionType`, `ReverseDirection`, `UseDefaultBendAllowance`, `UseDefaultBendRadius`, `GetCustomBendAllowance()`, `SetCustomBendAllowance(CustomBendAllowance)`, `object GetFixedFace(out double X, out double Y, out double Z)`, `SetFixedFace(double, double, double, object EdgeArray)`, `AccessSelections`/`ReleaseSelectionAccess`.
  - 32.1.0 adds `OverrideValue {get}` and `UseGaugeTable {get;set}`. Neither is in 30.5.1, as the draft says.
  - The help says the `GetFixedFace` point is "in the space of the sketch … the z location is always 0".
- `ICrossBreakFeatureData`, `IClosedCornerFeatureData`, `IBreakCornerFeatureData`, `IFoldsFeatureData`, `IOneBendFeatureData`, `ICustomBendAllowance` and `IFlatPatternFeatureData` are identical in both versions, with exactly the members the draft lists.
- `IBreakCornerFeatureData` has only `BreakType`, `Distance`, `Entities`, `InternalCornersOnly`, `CenteredOnBendLines`, `GetEntitiesCount` and `IGetEntities`/`ISetEntities`. This matters for P3.
- `ICornerReliefFeatureData` and `ISMCornerReliefData` exist in 30.5.1 and 32.1.0. They are absent in 27.3.0 and 29.4.1. Their members match the draft exactly, including `AddNewCorner(object, object, object, int, out object)` and `GetFaces(out, out, out)`.
- Every interface the draft switches on has its coclass interface in 27.3.0 (`SketchedBendFeatureData`, `CrossBreakFeatureData`, `ClosedCornerFeatureData`, `BreakCornerFeatureData`, `FoldsFeatureData`, `LoftedBendsFeatureData`, `OneBendFeatureData`). `case X x:` therefore works as it does for the existing types.
- There is no welded-corner data interface and no creation method in 27.3.0 or 32.1.0. `IWeldmentTrimExtendFeatureData` belongs to the weldment `WeldCornerFeat`.

**Creation calls (27.3.0, unchanged in 32.1.0)**
- `IFeatureManager`:
  - `InsertSheetMetal3dBend(double, bool, double, bool, short, CustomBendAllowance)` → `Feature`
  - `InsertCrossBreak(double, double)` → `Feature`
  - `InsertSheetMetalCornerTrim(int, int, double, int, double)` → `Feature`
  - `InsertSheetMetalLoftedBend2(int, double, bool, double, bool, int, double, int, double, double)` → `Feature`
  - `AddCornerReliefCorner()` → `int`, `AddCornerReliefType(int, int, double, double, double, bool, bool, bool, bool, bool)` → `bool`, `FinishCornerRelief()` → `Feature`
  - `CreateCustomBendAllowance()`, `CreateDefinition(int)`, `CreateFeature(object)`
- `IModelDoc2`:
  - `InsertSheetMetalClosedCorner()`, `InsertSheetMetalBreakCorner(int, double)`, `InsertSheetMetalUnfold()` and `InsertSheetMetalFold()`, all `void`
  - obsolete `InsertSheetMetal3dBend(double, double, bool, short)`
- These calls do not exist: `IModelDoc2.InsertCrossBreak`, `InsertBreakCorner`, `InsertSheetMetalCornerRelief`, and any welded-corner insert.
- `IFeature.GetOwnerFeature`, `GetFaces`, `GetFirstSubFeature`/`GetNextSubFeature`, `ISketch.GetReferenceEntity(out int)`, `IModelDocExtension.SelectByRay(...)`, `ISelectData.X/Y/Z` and `ISelectionMgr.GetSelectionPoint2(int, int)` exist in 27.3.0.
- From the help: `InsertSheetMetal3dBend.BendPos` is 0 centre line, 1 material inside, 2 material outside, 3 bend outside, and `PCBA` = null takes the parent's allowance.
- From the help: `InsertSheetMetalCornerTrim.ReliefType` is 0 circular (Param = diameter), 1 square (side length), 2 bend waist (radius).
- From the help: `InsertSheetMetalLoftedBend2.DRadius` is "valid only if BFormed is false".

**Enums (identical in 27.3.0 and 32.1.0 unless noted)**
- `swFlangePositionTypes_e`: MaterialInside 1, MaterialOutside 2, BendOutside 3, BendCenterLine 4, BendSharp 5, BendTangent 6.
- `swClosedCornerTypes_e`: Butt 1, Overlap 2, Underlap 3.
- `swBreakCornerTypes_e`: Fillet 0, Chamfer 1.
- `swLoftedBendFacetOptions_e`: 0–3.
- `swBendAllowanceTypes_e`: 1–5.
- `swCornerReliefType_e`: 0–8 in 27.3.0, plus Rectangular 9 in 32.1.0.
- `swCornerReliefBendType_e` (TwoBend 0, ThreeBend 1) and `swCornerReliefError_e` (0–6): 32.1.0 only.
- `swFeatureNameID_e`: SketchBend 35, SM3dBend 36, BreakCorner 70, CornerTrim 71, CrossBreak 86 in both versions; CornerRelief 120 in 32.1.0 only.
- `swSelectType_e`: FACES 2.

**Type names.** The local copy of the `GetTypeName2` help table confirms `SM3dBend`, `SketchBend`, `OneBend`, `CrossBreak`, `BreakCorner`, `CornerTrim` (both `IBreakCornerFeatureData`), `UnFold`/`Fold` (`IFoldsFeatureData`) and `LoftedBend`. The table has no entry for the closed corner. `CornerFeat` comes only from CADapter and SW_MCP, both live third-party code.

**Claims about the existing code that hold**
- `FeatureEdit` exists with one typed method per feature data and the shared `Edit` (IFeatureBuilder.cs l.75–210). `SwLate` has `Get`/`Set`/`Call` only (SwLate.cs l.13–51).
- `ShellBuilder.Create` finds the new feature through `GetLastFeatureAdded` plus name and type (ShellBuilder.cs l.121, l.130).
- `BuilderSupport.SelectFaceAt` clears the selection first (BuilderSupport.cs l.157).
- `SketchBuilder.Build` always sketches on a plane from `PlaneResolver` (SketchBuilder.cs l.54–58).
- `BuildContext.PointSketches` exists and is filled in `MirrorJob.Run` (BuildContext.cs l.98, MirrorJob.cs l.179).
- `FeatureCollector`/`SwDoc.FeaturesInCreationOrder` list only top-level features and absorbed sketches (SwDoc.cs l.65–74). `SkipReason` skips suppressed features (FeatureCollector.cs l.64).
- `MassCapture` skips records with `ChangesSolid` = false (MassCapture.cs l.67). A cross break with `ChangesSolid => false` is therefore neither measured nor checked.
- `ReadSketch` rolls back to just after the sketch when the sketch refers to the model (FeatureCapture.cs l.103–120).
- In SeparateBody mode a step that changed the original bodies is removed again (`OriginalUntouched`/`RemoveAgain`, MirrorJob.cs l.307–311, l.471–482).

**Mirror rules (geometry)**
- **Angle, radius, bend position, K-factor, bend allowance and deduction, bend and gauge tables.** These are unchanged. A reflection keeps lengths and angles, and allowance tables are indexed by thickness, radius, angle and material.
  - "Material inside/outside" and "bend outside/centre line" are defined relative to the bend line and the inside of the bend, which a reflection maps onto themselves.
- **Fixed face and pick point.** The fixed face maps to R(fixed face) and the pick point to R(pick point). A reflection maps the fixed region onto the mirrored fixed region. Position plays a role here, orientation does not.
- **Closed corner.** Overlap and underlap are defined by the face-to-extend / face-to-match roles. The mapping keeps these roles, so the type is invariant, and the swap as a verifier alternative is only a hedge.
- **Break corner and corner relief.** Type, distance, internal-only, relief type, slot sizes, centre-on-bend-lines, tangent-to-bend and suitcase type are all intrinsic, so they stay unchanged.
- **Unfold/Fold.** No direction is involved: Unfold(R(P), R(F), R(B)) = R(Unfold(P, F, B)).
- **Algebra of the direction rule.** For a bent direction cos θ·u + s·sin θ·o, the mirrored direction expressed against a copy reference σ·R(o) has s′ = σ·s, with an error of 4.4e-16 in `bendcheck.py`.

**No mirror API.** The draft uses only the creation calls above. It names none of `InsertMirrorFeature*`, `MirrorPart*`, `MirrorFeature` or derived parts.

## (1) Problems

### P1. The fixed side of a sketched bend can be lost when the face is picked
- **Evidence, the fallback.** `SelectFaceAt` picks with `SelectByID2("", "FACE", x, y, z)`. If the picked face is not the wanted one (`IsSameFace`), it selects the face **without a point** (BuilderSupport.cs l.161–166). `SwSelect.Select` passes a `SelectData` without X/Y/Z (SwSelect.cs l.16–17, l.32).
  - For a hole, the point-less fallback is harmless, because the position sketch places the holes anyway.
  - For a sketched bend, the pick point is the only thing that says which side stays put (help: "Fixed face … does not move"). CADapter and eyfel both measured that a pick on or near the bend line holds the wrong half.
- **Evidence, the ray pick.** The regions review classes `SelectByID2` with coordinates as a ray pick. A typical case: the sketch is on the face looking +Z and the part is mirrored at *vorne* (the front plane). The mirrored face then looks −Z, and the ray can hit the opposite skin of the sheet first, one thickness away. That leads straight into the point-less fallback.
- **Evidence, the official route.** The official corner-relief example (`ICornerReliefFeatureData`, VBA) builds four sketched bends. Each one selects the fixed face with `SelectByRay(x, y, 0, 0, 0, −1, r, 2, True, 0, 0)` after the sketch, not with `SelectByID2`.
- **Evidence, the draft's own fallbacks.**
  - Step 3 falls back to `TopologyCapture.Face(fixed).Sample`, the face point nearest the bounding-box centre. For a symmetric plate with a central bend line, that point lies exactly on the line.
  - Step 7's `OtherSideOfLine` reflects the pick across the line. With a bend line at x = 10 mm on a 100 mm plate and a pick at x = 50 mm, the result is x = −30 mm, off the face (`bendcheck.py`).
  - A sketch with two or more lines (a U-channel) has three or more regions, not two.
- **Failure.** The copy bends the wrong half. With "Jeden Schritt prüfen" off this is reported as OK. With it on, it ends as "weicht ab", and no alternative fixes the side.
- **Fix.**
  - Add `BuilderSupport.SelectFaceByRay(ctx, face, p, mark, append)`:
    - Ray from p + δ·n (δ = 1e-5 m, n = outward normal of the face) along −n, radius 1e-4 m, type `swSelFACES`.
    - Accept only if the last selected object is the wanted face (same fingerprint, and on an own body when SeparateBody) and `GetSelectionPoint2(count, mark)` lies within `Tol.Length` of p.
    - Otherwise fail. **Never** select a bend's fixed face without a point.
  - Side candidates come from Core `SheetMetalBendRules.SideCandidates`. It gives one point per region of the face cut by all mirrored bend lines, each at least `clearance = max(2·t, 1 mm)` from every line, projected onto the face by the kernel and kept only if it lies on the face.
  - The captured point R(p) comes first. The other regions are tried only if the witness check (P2) fails.

### P2. The bend-direction rule is unproven, and a wrong guess is silent
- **Evidence, live third-party code.** eyfel `SolidWorksService.SheetMetal.cs` l.91–105 and `.Analyze.cs` l.649–651, from a live session:
  - "identical blank B-reps can carry OPPOSITE intrinsic sheet orientations (ReverseThickness vs ReverseDirection routes), and downstream bend directions are defined relative to that orientation".
  - "4-1's symmetric blank was B-rep-identical to a reversed t/2 one, but bends folded mirrored".
  - So the reverse flag of a sketched bend is measured against a hidden sheet orientation, not obviously against the bend-sketch normal.
- **Evidence, the copy's sheet orientation.** Area 1 flips the base flange's `ReverseDirection` when σ_base = −1 (blech-grundlagen.md §6). Whether the copy's hidden orientation is then R(o) or −R(o) depends on that rule. With `SymmetricThickness` there is no flag to flip, so it is σ_base·R(o).
- **Evidence, how much it matters.** The bend-sketch σ and this σ_sheet are independent. In `bendcheck.py` they disagree in about half the random cases.
- **Evidence, the check.** The draft's σ rule is only checked by the mass comparison, which runs only with "Jeden Schritt prüfen" (StepVerifier.cs l.31; MassCapture runs only then, MirrorJob.cs l.160).
- **Failure.** In a typical case the copy bends up where the original bends down. With "Jeden Schritt prüfen" off it is reported OK. "Original löschen" is then allowed, so the correct original goes and the wrong copy stays.
- **Fix.** Decide geometrically in the builder, independent of "Jeden Schritt prüfen":
  - **At capture**, one extra rollback per sketched bend, like `ReadSketch`: `EditRollback(AfterFeature)`, then store `BendWitnesses`, at most 12 points:
    - samples on the faces of the `SketchBend` sub-features;
    - samples on the body's planar faces whose outward normal differs from the fixed face's by more than 1°. These are the moved flanges. Unchanged flanges are also hit, which is harmless.
  - **In the builder**, after the read-back:
    - All R(witness) points must lie on the copy's body faces (≤ 10 µm).
    - If not, toggle `ReverseDirection` and check again.
    - Then try the other region candidates (P1).
    - Keep the best variant. If none passes, report a Warning ("Biegung weicht vom Original ab").
  - **First guess for the flag:**
    - `ReverseFlag(rev, σ_sheet)` when area 1 exposes σ_sheet (`SheetMetalBodyMap.Sigma`);
    - otherwise `ReverseFlag(rev, σ_sketch)`;
    - with a support face (P7), σ_sketch = +1.
  - The verifier alternative "Biegerichtung umgekehrt" is offered only when no witnesses could be captured.

### P3. Corner trims are not collected, cannot be read, and would be deleted
- **Evidence, collection.**
  - CornerTrim is a sub-feature of the normally suppressed Flat-Pattern (draft §1.7). `FeaturesInCreationOrder` lists top-level features and absorbed sketches only (SwDoc.cs l.65–74), so a corner trim is never a record.
  - Today this is hidden because `RefusesRun` refuses every sheet-metal part. Once areas 1–3 lift the refusal, corner trims are silently missing.
  - With "Original löschen", area 1's `FlatPatternRecord` makes the Flat-Pattern a deletable original. Deleting it takes the user's corner trims with it, while `AllMirrored` (MirrorReport.cs l.100) never saw them.
- **Evidence, reading.**
  - `IBreakCornerFeatureData` has no relief type and no relief size (member list above).
  - The draft reads `IFlatPatternFeatureData.CornerTrimReliefType`/`…Distance`/`…RatioToThickness` "of the owner Flat-Pattern". The help describes these as "the type of relief for the corner trim **for the Flat-Pattern feature**", the flat pattern's own "add corner-trim" option (`GetAddCornerTrim`/`SetAddCornerTrim`). For a corner trim the user inserted, they are unrelated values.
  - The Corner-Trim PropertyManager (2017 help, via search summary) has relief type Circular/Square/Bend Waist, relief length, ratio to thickness and centred on bend lines. The API exposes only `CenteredOnBendLines` of these.
- **Evidence, the hook.** The "flat-pattern scope" the draft assigns to area 1 does not exist in `blech-grundlagen.md`. Area 1 only measures the flat state and edits the Flat-Pattern's own options.
- **Fix.**
  - `FeatureCollector`: for each `FlatPattern` (top level or in `TemplateFlatPattern`), list its non-sketch sub-features. Skip them only if they are the Flat-Pattern's own corner trim or break corner (`GetAddCornerTrim(out f)` / `GetBreakCorners(out f)`); those become `PartOfFeatureRecord(owner: flat pattern)`, rebuilt by area 1's flags.
  - Every other `CornerTrim` becomes `UnsupportedRecord(changesSolid: false)` with the reason „Ecke trimmen (abgewickelt): Freistellungsart und -größe sind über die API nicht lesbar – bitte im Ziel von Hand anlegen.“
    - Status "Skipped" blocks "Original löschen".
    - Because ChangesSolid is false, it is not a fallback trigger (blech-umformwerkzeuge-ersatzweg.md §4) and is not measured.
  - Drop the CornerTrim builder and `BreakCornerRecord.Trim*`.

### P4. A sketched bend probably has no face samples of its own; "MassCapture needs no change" is wrong
- **Evidence.**
  - `SampleFaces` uses only `f.GetFaces()` (MassCapture.cs l.145–147).
  - Area 2 found that a hem's faces belong to its `HemBend` sub-feature (blech-laschen.md §E) and extends `SampleFaces` to sub-features.
  - For `SM3dBend` the bend faces belong to the `SketchBend` sub-features, and the moved flat faces keep their base-flange identity [?].
  - The draft's step 7 ("if the mirrored FaceSamples miss") then has no samples. Samples exist only with "Jeden Schritt prüfen" anyway.
  - `MissedSamples` is private (StepVerifier.cs l.106).
- **Fix.** Share area 2's sub-feature sampling: for sheet-metal records, also sample the faces of non-sketch sub-features. The builder-side decision uses the witnesses from P2, not `FaceSamples`, so `MissedSamples` can stay private. The new witness check is a small shared helper `SwShape.Misses(ctx, points, ownOnly)`.

### P5. Edits to a sketched bend can reset its custom bend allowance
- **Evidence, CADapter (measured 2026-08-31).**
  - "The SKETCHED bend (`SM3dBend`) is OUT by MEASUREMENT … there `ModifyDefinition` returns False and the re-read reverts to the default (type 2, empty file)" (cadapter_sm.py l.1556–1559).
  - The same source: `ModifyDefinition` "returns True even when it ignores you" (l.1491).
- **Evidence, the draft.**
  - Step 6 runs `FeatureEdit.SketchedBend` **always**, and the alternative toggles through `ModifyDefinition` again.
  - Whether `PCBA` is honoured at creation is unmeasured: every example passes `Nothing`/`None`.
- **Failure.** A bend with a custom K-factor gets the default K-factor. The folded geometry of a sketched bend depends on the allowance, so the flange lengths are wrong. This shows only with "Jeden Schritt prüfen" on, and even then the toggle cannot repair it.
- **Fix.**
  - Pass the allowance only as `PCBA`.
  - Read back after creation.
  - Edit only if the read-back reverse or position is wrong, and read back the allowance again after **every** edit, including the witness toggle.
  - If it was reset:
    - add the note „Biegezugabe ließ sich nicht zusammen mit der Biegerichtung setzen (SOLIDWORKS setzt sie beim Ändern zurück)“;
    - try the opposite order once: delete, recreate with the flag the read-back showed, and accept the other direction only if the witnesses still pass;
    - otherwise report a Warning.
  - Add live tests L4 and L5.

### P6. Overrides on individual bend sub-features are lost without a note
- **Evidence.**
  - Each `SketchBend` sub-feature has its own `IOneBendFeatureData` (`UseDefaultBendRadius`, `BendRadius`, `UseDefaultBendAllowance`, `GetCustomBendAllowance`, `UseDefaultBendRelief` …). The draft reads only the parent and `BendCount`.
  - CADapter measured that writing to `IOneBendFeatureData` is ignored: "ModifyDefinition returns True and the geometry does not change" (l.1441–1443). The copy therefore cannot take the overrides.
- **Fix.**
  - Capture each bend's values.
  - Core `BendOverrides.Differences(parent, bends)` names those that differ from the parent.
  - The record gets the note „Biegung „SketchBend2“ hat eigene Werte (Radius 2 mm), die nicht übernommen werden“, which makes the entry a Warning.
  - Area 2 needs the same helper for edge and miter flanges.

### P7. Capturing the sketch's support face in the final state reads the wrong face
- **Evidence.**
  - The draft captures `SketchRecord.SupportFace` in `ReadSketch` through `GetReferenceEntity`, "using the existing rollback-after-sketch branch". That branch runs only if the sketch refers to model edges, vertices or planes (FeatureCapture.cs l.106–108).
  - In the final state the bend has split and moved the support face, so the face the sketch references is either the fixed remainder or nothing.
- **Fix.**
  - Read the support face in `CaptureSketchedBend` under the bend's `AccessSelections`, where the model is just before the bend: `sketch.GetReferenceEntity(out _) as Face2` → `SketchedBendRecord.SketchFace`.
  - A pre-pass fills `ctx.SketchSupportFaces[sketchName] = SketchFace`.
  - `SketchBuilder.Build` looks there first and resolves no plane when it uses the face, so no helper plane is left behind. It sketches on the mirrored face if all of these hold:
    - the face is found;
    - it is planar and coplanar with R(sketch plane) within `Tol.Length`;
    - it is on an own body (SeparateBody).
  - Otherwise it uses the plane, with a note.

### P8. Fold bends in the flat state and the ordinal fallback can pick the wrong bend
- **Evidence.**
  - For a Fold, the bends are captured "in this rolled-back state (… flat for Fold)". Flattened bend regions are planar and may merge with neighbouring faces, so the fingerprints are weak [?].
  - The fallback "`ctx.Created[owner]`'s sub-feature at index ordinal" assumes that the copy orders its `SketchBend`/`EdgeBend` sub-features like the original. That is undocumented, and a reflection reverses in-plane order.
  - The official "Insert and Access Fold" example shows that the Fold uses **the same bend features** (`EdgeBend3`) the Unfold used.
  - `FindBend` only *prefers* own bends. In SeparateBody mode the original's bends are still candidates; `OriginalUntouched` removes the step afterwards, but only after the fact.
  - The `FixedFace`/`Bends` setters receive objects found before creation, inside a `FeatureEdit` that has rolled the model back.
- **Fix.**
  - Add `BuildContext.Bends`: original bend name → target bend feature. Every successful `FindBend` fills it, and owner builders fill it where they match sub-features.
  - A Fold looks up its bends by original name first, then by fingerprint.
  - Fall back to an ordinal only when the owner has exactly one bend.
  - In SeparateBody mode, accept only bends on own bodies.
  - Pass `Safe(...)` entities to the setters.
  - Also try the official selection variant for a Fold: the preceding UnFold feature with mark 0, the fixed face with mark 1, the bends with mark 4.

### P9. An unexpected exception in the new capture code aborts the whole run
- **Evidence.**
  - `Capture` catches only `NotSupportedException` (FeatureCapture.cs l.92).
  - The capture loop runs before the `try` (MirrorJob.cs l.141–148; the `try` starts at l.184).
  - Each of these would end the run before anything is built:
    - a null face from `GetFixedFace`;
    - `d.Bends` = null cast to `object[]`;
    - a `COMException` from `GetFaces` on a bend sub-feature;
    - `InvalidCastException` in the late-bound corner relief.
  - The regions review (its P9) found the same pattern.
- **Fix.** Every new `Capture…` method wraps COM access as `catch (Exception ex) when (!(ex is NotSupportedException)) { throw new NotSupportedException($"… nicht lesbar ({ex.Message})."); }`, checks nulls explicitly, and keeps `ReleaseSelectionAccess` in `finally`.

### P10. "Generalising HolePart" for the cross-break sketch is not enough
- **Evidence.**
  - `MirrorJob` turns every `PartOfFeatureRecord` back into a sketch unless its owner is a `PositionedHoleRecord` (MirrorJob.cs l.150–153).
  - `RemoveAgain` deletes absorbed sketches only for holes (l.474–475).
  - Area 2 already plans `OwnedPart`.
  - A cross break whose break lines the user edited would also lose them silently: the feature does not change the solid, so no check sees it.
- **Fix.**
  - Add an `IOwnsParts` marker on records (holes, edge flanges, hems, cross breaks, the flat pattern). Use it in all three places, with a single ownership map shared with area 2.
  - `CrossBreakBuilder` reads the lines of the new cross break's sketch and compares them with the original's mirrored lines in model coordinates. If they differ, it adds the note „Bruchlinien weichen ab (im Original bearbeitet)“.

### P11. The unsupported and refusal lists are not integrated
- **Evidence.**
  - `UnsupportedFeatures.SheetMetal` lacks `CornerFeat`, `CornerRelief` and the unknown welded-corner name (UnsupportedFeatures.cs l.12–18). With only a closed corner selected, the run is not refused and the reason is the generic "Feature-Typ CornerFeat wird nicht unterstützt."
  - `FeatureCapture.NonSolidTypes` lacks `CrossBreak` (l.404–408). An unreadable cross break would therefore be "Failed", with the message that later features can fail too, instead of "Skipped".
  - The draft keeps `LoftedBend` as an `UnsupportedRecord` "which goes to the fallback". But the fallback runs only in whole-part or body mode (blech-umformwerkzeuge-ersatzweg.md §4), and a lofted bend **creates** the sheet body. In feature mode, or with the fallback off, everything after it fails, and an assembly would save an empty counterpart. That is exactly the case `RefusesRun` exists for (STOLPERSTEINE 139).
  - The welded corner is defined as an `UnsupportedRecord` that changes the solid, which triggers area 4's whole-body fallback. That contradicts the draft's own §10 ("report it as add by hand").
- **Fix.**
  - Core `UnsupportedFeatures`:
    - recognise `CornerFeat` and `CornerRelief`;
    - "CornerRelief" reason on 2019–2021: „Eckfreistellung: erst ab SOLIDWORKS 2022 lesbar“;
    - "CornerTrim" reason as in P3;
    - add `CrossBreak` to the non-solid types.
  - Replace `RefusesRun(type)` with `RefusesRun(type, fallbackApplies)`. Base-body carriers without a builder (`LoftedBend`, until phase 2) refuse unless the fallback covers their body.
  - Add `UnsupportedRecord.TriggersFallback` (default true). The welded corner sets it false: entry "Failed", „Verschweißte Ecke: keine API – im Ziel von Hand anlegen“, deletion blocked.
  - Area 3's own types leave the refusal set only once their builders exist.

### P12. Same-part guards for references that do not change the solid
- **Evidence.**
  - `OriginalUntouched` runs only for records with `ChangesSolid` (MirrorJob.cs l.267). A cross break on the original's face is therefore never removed. The draft checks "the face is on an own body" only after creating the cross break.
  - `TopologyFinder.FindFace` prefers own bodies but falls back to the original's face (TopologyFinder.cs l.71–75).
- **Fix.** Check every face, edge and bend of an area-3 feature for `ctx.IsOwnBody(entity.GetBody())` **before** creating it when `ctx.SeparateBody`. Otherwise fail with nothing created ("Bezug liegt auf dem Originalkörper").

### P13. Corner relief details
- **Evidence.**
  - `GetCorners(−1)` = "all" is not documented: the help says only "Corner relief type as defined by swCornerReliefType_e".
  - The official legacy example selects the body and then calls `ClearSelection2` **before** selecting the faces with mark 4. "Body mark 0 (append)" is not supported by it.
  - The circular radius reading in `LegacyReliefLengths` (`SlotWidth`) is a guess.
  - The 2019–2021 reason „Eckfreistellung erst ab 2022 lesbar“ is not connected to anything: there is no `Reason` entry and no type name.
- **Fix.**
  - Read corners with `CornerCount` plus `GetCornerAtIndex(i, out c)`.
  - Legacy path: faces with mark 4, then optionally the body with mark 0 as a second variant.
  - Use the legacy path only for square, obround and circular corners. For any other type, fail with a note.
  - Reason entry as in P11.

### P14. Smaller inaccuracies
- `ILoftedBendsFeatureData` is **not** identical: 32.1.0 adds `SymmetricThickness`.
- `LoftedBendRecord.Radius` cannot be read: there is no member, and `DRadius` exists only at creation.
- `swFmSM3dBend` = 36 exists next to `swFmSketchBend` = 35 and is a cheap second try for `CreateDefinition`.
- `LastAddedOf<TData>` must also check the type name: UnFold and Fold share `IFoldsFeatureData`.
- `BendAllowanceSpec` duplicates area 1's `BendAllowanceData`; use that one.
- The CADapter "BendPos = 4 no-op" came from passing the `swFlangePositionTypes_e` number. This confirms the 0–3 range, but `BendPos` = 0 (centre line) is measured nowhere.
- `ChooseFixedPoint` must prefer the documented sketch-space reading. If both readings lie on the face in different regions, take the sketch reading and add a note.
- The draft's records use public fields; the codebase uses properties.

## (2) Corrected design

This section supersedes the draft where they differ.

### 0. Scope and verdict

| Feature | Verdict | Builder |
|---|---|---|
| Sketched bend (`SM3dBend`) | parametric; side and direction decided by witnesses (P1, P2) | `SketchedBendBuilder` |
| Closed corner (`CornerFeat`) | parametric | `ClosedCornerBuilder` |
| Break corner (`BreakCorner`) | parametric | `BreakCornerBuilder` |
| Unfold / Fold | parametric; bends by name map, then fingerprint | `FoldBuilder` |
| Corner relief | parametric on 2022+; Unsupported on 2019–2021 (a fallback trigger) | `CornerReliefBuilder` |
| Cross break | parametric, cosmetic; direction by rule, not measurable | `CrossBreakBuilder` |
| Corner trim (user-inserted) | **Unsupported**, non-solid; blocks deletion (P3) | — |
| Flat pattern's own corner trim / break corner | part of the flat pattern (area 1 flags) | `PartOfBuilder` |
| Welded corner | **Unsupported**, no fallback trigger (P11) | — |
| Lofted bend | phase 2. Until then: refused unless the area 4 fallback covers its body (P11) | — |

The order of work follows the user's usage: sketched bend → closed corner → break corner → unfold/fold → corner relief → cross break. Every builder needs area 1 (sheet-metal body, `SheetMetalBodies`). Closed corner, break corner, corner relief and fold of flange bends also need area 2.

### 1. Shared prerequisites
1. **Area 1** exposes `SheetMetalBodyMap.Sigma`. It is +1 when the copy's hidden sheet orientation is R(o), and −1 otherwise. For example, with `SymmetricThickness` and σ_base = −1 it is σ_base. Until area 1 provides it, it is null.
2. **Area 2 / shared:**
   - `IOwnsParts` and a single owned-part map (P10);
   - sub-feature face sampling in `MassCapture.SampleFaces` for sheet-metal records (P4);
   - Core `BendOverrides` (P6).
3. **`SwLate`:** `object? Invoke(com, method, params object[] args)` and `bool InvokeOut(com, method, object[] args, bool[] byRef)` (`ParameterModifier`).
4. **`FeatureEdit`:**
   - typed `SketchedBend`, `CrossBreak`, `ClosedCorner`, `BreakCorner`, `Folds`;
   - `Late(doc, f, data, change)` (access and release through `SwLate`);
   - every caller reads back afterwards, because `ModifyDefinition` can lie (CADapter).
5. **`SwSheetMetal.LastAddedOf(doc, before, string[] types, Func<object,bool> isData)`.** Same logic as `ShellBuilder.Create`, with type name **and** data interface. It depends on area 1 L8 (`GetLastFeatureAdded` with the Flat-Pattern last).
6. **`BuilderSupport.SelectFaceByRay`** (P1) and **`SwShape.Misses(ctx, points, ownOnly)`**: kernel distance of each point to the faces of own bodies (SeparateBody) or all bodies.
7. **`BuildContext`:**
   - `Dictionary<string, FaceSignature> SketchSupportFaces` (P7);
   - `Dictionary<string, Feature> Bends` (P8).
   - `Forget` also removes the bends of forgotten features.
8. **`FeatureCollector`:** the non-sketch sub-features of every Flat-Pattern (P3).
9. **`UnsupportedFeatures` / `RefusesRun(type, fallbackApplies)` / `UnsupportedRecord.TriggersFallback`** (P11).

### 2. Records (`Spiegelwerk.Core/Records/SheetMetalBendRecords.cs`)

```csharp
public sealed class BendRef                                // a bend sub-feature (OneBend/SketchBend/EdgeBend …)
{
    public BendRef(string owner, string name, int ordinal, int ownerBendCount);
    public string Owner { get; }  public string Name { get; }          // original names
    public int Ordinal { get; }   public int OwnerBendCount { get; }   // ordinal only usable if OwnerBendCount == 1
    public List<FaceSignature> Faces { get; } = new List<FaceSignature>();   // state of the referencing feature
    public BendRef Mirror(Reflection r);                                // faces mirrored, names kept
}

public sealed class BendValues                             // IOneBendFeatureData of one bend, for BendOverrides
{ public string Name { get; set; } = ""; public bool UseDefaultRadius { get; set; } public double Radius { get; set; }
  public bool UseDefaultAllowance { get; set; } public BendAllowanceData? Allowance { get; set; } }

public sealed class SketchedBendRecord : FeatureRecord    // "SM3dBend"
{
    public SketchedBendRecord(string name, string typeName, string sketchName);
    public string SketchName { get; }
    public double Angle { get; set; }  public bool UseDefaultRadius { get; set; }  public double Radius { get; set; }
    public int PositionType { get; set; }                  // swFlangePositionTypes_e, 1..4
    public bool Reverse { get; set; }
    public bool UseDefaultBendAllowance { get; set; }  public BendAllowanceData? BendAllowance { get; set; }  // area 1 type
    public FaceSignature FixedFace { get; set; } = null!;
    public Vec3? FixedPoint { get; set; }                  // model coordinates; null = unknown
    public FaceSignature? SketchFace { get; set; }         // support face of the bend sketch, state before the bend (P7)
    public List<Line3> BendLines { get; } = new List<Line3>();   // model coordinates, for SideCandidates (P1)
    public List<Vec3> Witnesses { get; } = new List<Vec3>();     // state after the bend (P2); empty = not captured
    public List<BendValues> Bends { get; } = new List<BendValues>();   // SketchBend sub-features, in order
}

public sealed class CrossBreakRecord : FeatureRecord     // "CrossBreak"
{ public double Angle { get; set; } public double Radius { get; set; } public bool Reverse { get; set; }
  public FaceSignature Face { get; set; } = null!;
  public List<(Vec3 a, Vec3 b)> Lines { get; } = new List<(Vec3, Vec3)>();   // break lines in model coordinates, if its sketch is readable (P10)
  public override bool ChangesSolid => false; }

public sealed class ClosedCornerRecord : FeatureRecord   // "CornerFeat"
{ public int CornerType { get; set; } public double Gap { get; set; } public double OverlapRatio { get; set; }
  public bool OpenBendRegion { get; set; } public List<FaceSignature> Faces { get; } = new List<FaceSignature>(); }

public sealed class BreakCornerRecord : FeatureRecord    // "BreakCorner" only (P3)
{ public int BreakType { get; set; } public double Distance { get; set; }
  public bool InternalCornersOnly { get; set; } public bool CenteredOnBendLines { get; set; }
  public List<EdgeSignature> Edges { get; } = new List<EdgeSignature>(); public List<FaceSignature> Faces { get; } = new List<FaceSignature>(); }

public sealed class CornerReliefItem
{ public FaceSignature Face1 { get; set; } = null!; public FaceSignature Face2 { get; set; } = null!; public FaceSignature? Face3 { get; set; }
  public int ReliefType { get; set; } public int SuitCaseType { get; set; }
  public double SlotLength { get; set; } public double SlotWidth { get; set; } public double CornerFilletDiameter { get; set; }
  public bool AddFilletedCorners { get; set; } public bool CenterOnBendLines { get; set; } public bool RatioToThickness { get; set; } public bool TangentToBend { get; set; }
  public CornerReliefItem Mirror(Reflection r); }        // faces mirrored, order and values kept

public sealed class CornerReliefRecord : FeatureRecord   // type name from the live test (L13)
{ public int CornerType { get; set; } public FaceSignature? BodyFace { get; set; }
  public List<CornerReliefItem> Corners { get; } = new List<CornerReliefItem>(); }

public enum FoldKind { Unfold, Fold }
public sealed class FoldRecord : FeatureRecord           // "UnFold" / "Fold"
{ public FoldKind Kind { get; set; } public FaceSignature FixedFace { get; set; } = null!; public List<BendRef> Bends { get; } = new List<BendRef>(); }
```

There is no `LoftedBendRecord` in this round. `UnsupportedRecord` gets `bool TriggersFallback { get; set; } = true`.

### 3. Core rules (`Spiegelwerk.Core/Planning/SheetMetalBendRules.cs`, `Matching/BendMatcher.cs`)

```csharp
public static class SheetMetalBendRules
{
    public static short? BendPosForCreation(int positionType) =>          // 4→0, 1→1, 2→2, 3→3, else null
        positionType switch { 4 => (short)0, 1 => (short)1, 2 => (short)2, 3 => (short)3, _ => (short?)null };
    /// First guess for ReverseDirection; sigmaSheet from area 1 wins over sigmaSketch (P2).
    public static bool FirstReverseGuess(bool reverse, int? sigmaSheet, int sigmaSketch) =>
        DirectionRules.ReverseFlag(reverse, sigmaSheet ?? sigmaSketch);
    /// Region of a point: sign vector against every bend line (in the face plane, normal n).
    public static int[] RegionOf(Vec3 p, IReadOnlyList<Line3> lines, Vec3 n);
    /// One candidate point per region, ≥ clearance from every line; the caller projects onto the face and keeps points on it.
    public static List<Vec3> SideCandidates(IReadOnlyList<Line3> lines, Vec3 n, Vec3 facePoint, double clearance, double reach);
    /// Documented sketch-space reading first; model reading only if the sketch reading is off the face.
    public static (Vec3? point, bool ambiguous) ChooseFixedPoint(Vec3 asSketch, double dSketch, Vec3 asModel, double dModel, double tol);
    public static int SwapOverlap(int cornerType) => cornerType == 2 ? 3 : cornerType == 3 ? 2 : cornerType;
    /// Legacy AddCornerReliefType lengths; null for types the 2019 call does not document (P13).
    public static (double length2, double length3)? LegacyReliefLengths(CornerReliefItem c);
    /// Witness verdict: all hit → Pass; fewer misses is better; a feature in error never wins.
    public static int CompareVariants(int missedA, bool errorA, int missedB, bool errorB);
}
public static class BendOverrides { public static List<string> Differences(BendValues parent, IEnumerable<BendValues> bends); }
// BendMatcher.FindBend(wanted, candidates, sampleDistance, preferred, restrictTo): full match only; restrictTo = own bodies in SeparateBody.
```

`SideCandidates` takes these inputs:
- **Reach:** the face extent from `IFace2.GetBox`, projected onto the face plane.
- **Candidates per line:** for each line, its midpoint ± (clearance … reach/2) along `n × direction`, in 4 steps.
- **Grouping:** points are grouped by `RegionOf`, keeping one per region, the one farthest from every line.

### 4. Capture (`Capture/FeatureCapture.SheetMetalBends.cs`)

Switch cases come before the default; each method follows the exception pattern from P9.

```csharp
case SketchedBendFeatureData sb: return CaptureSketchedBend(f, type, sb);
case CrossBreakFeatureData cb:   return CaptureCrossBreak(f, type, cb);
case ClosedCornerFeatureData cc: return CaptureClosedCorner(f, type, cc);
case BreakCornerFeatureData bc when type == "BreakCorner": return CaptureBreakCorner(f, type, bc);
case BreakCornerFeatureData _:   return new UnsupportedRecord(f.Name, type, UnsupportedFeatures.Reason("CornerTrim"), false);
case FoldsFeatureData fd:        return CaptureFold(f, type, fd);
// after the switch:
if (def != null && SwLate.Get(def, "CornerCount") != null) return CaptureCornerRelief(f, type, def);
```

**`CaptureSketchedBend`**
1. `sketch = SketchOf(f)`.
2. Read the values. If `BendPosForCreation(PositionType) == null`, throw `NotSupported`. If `UseDefaultBendAllowance` is false, read `GetCustomBendAllowance()` into `BendAllowanceData`. If `SwLate.Get(d, "UseGaugeTable") is true`, add the note „Blechstärkentabelle der Biegung wird nicht übernommen“.
3. Under `AccessSelections`, which is the state before the bend:
   - `GetFixedFace(out x, out y, out z) as Face2`. If it is null, throw `NotSupported` „Feste Fläche nicht lesbar“. Otherwise take its signature.
   - Run `ChooseFixedPoint` with the sketch-space reading (`_math.SketchFrame(sketch).ToWorld(x, y)`) and the model reading (x, y, z). If the result is ambiguous, add a note.
   - `SketchFace` = signature of `((Sketch)sketch.GetSpecificFeature2()).GetReferenceEntity(out _) as Face2`, if it is one.
   - `BendLines` = the sketch's lines, mapped to model coordinates with the sketch frame.
4. Release.
5. `Bends`: for each `SketchBend` sub-feature, its `IOneBendFeatureData` → `BendValues`. Run `BendOverrides.Differences` → notes (P6).
6. Witnesses (P2):
   - `EditRollback(AfterFeature, f.Name)`. If it fails, take no witnesses and add the note „Biegerichtung nur nach Regel“.
   - Otherwise collect points as in P2: `SketchBend` faces first, then body faces whose normal differs from the fixed face's normal by more than 1°. Cap at 12, spread evenly.
   - `finally` `EditRollback(End)`.

**`CaptureCrossBreak`**
- Read `BreakAngle`, `BreakRadius`, `ReverseDirection` and, under access, `Face`.
- `Lines` comes from its sketch sub-feature, if it has one [?]. That sketch becomes `PartOf` through `IOwnsParts`.

**`CaptureClosedCorner`**
- Read the values and, under access, `Faces` in order.
- With no faces, throw `NotSupported`.

**`CaptureBreakCorner`**
- Read the values and `Entities` (Edge → `EdgeSignature`, Face2 → `FaceSignature`).
- With no entities, throw `NotSupported`.

**`CaptureFold`**
- `Kind` comes from the type name. Under access: `FixedFace` → signature.
- For each bend in `d.Bends as object[] ?? throw NotSupported`:
  - `owner = b.GetOwnerFeature()`;
  - the owner's bend sub-features, those whose definition is `OneBendFeatureData`, give `Ordinal` and `OwnerBendCount`;
  - `Faces` = the bend's `GetFaces()`, read in this state.

**`CaptureCornerRelief`** (2022+, late-bound)
- `SwLate.Call(def, "AccessSelections", _doc, null)`, then read `CornerType`.
- `GetBodyScope()` → a face of that body not made by this feature → `BodyFace`.
- `for i < CornerCount`: `GetCornerAtIndex(i, out c)` (`InvokeOut`), then its properties and `GetFaces(out, out, out)` (`InvokeOut`).
- `finally` `ReleaseSelectionAccess`.

**`MirrorJob`**
- Builders: `SketchedBendBuilder`, `CrossBreakBuilder`, `ClosedCornerBuilder`, `BreakCornerBuilder`, `CornerReliefBuilder`, `FoldBuilder`.
- Pre-pass: `ctx.SketchSupportFaces[sb.SketchName] = sb.SketchFace` for every `SketchedBendRecord` with a `SketchFace`.
- The `PartOf` re-capture uses `IOwnsParts` (P10).

### 5. Builders

Common skeleton:
1. Resolve every reference.
2. In SeparateBody mode, check that each one is on an own body **before** creating anything (P12).
3. Create.
4. `LastAddedOf` for the `void` creators.
5. Read back. Edit only where the read-back differs, then read back again.
6. `ctx.Register`.
7. Add notes and alternatives.

Deleting a variant uses `BuilderSupport.Delete(ctx, f)`, which keeps the sketch.

**5.1 `SketchedBendBuilder`**
1. `map = ctx.Sketches[r.SketchName]`, else fail. `pos = BendPosForCreation(...)`.
2. `fixed = FindFace(r.FixedFace.Mirror(R))`, on an own body. Fail if missing; there is no plane substitute.
3. Candidates:
   - `R(FixedPoint)` first;
   - then `SideCandidates(R(BendLines), normal, …)`, projected onto `fixed` and kept only if on the face, with `R(FixedPoint)`'s region removed.
   - If `FixedPoint` is null, the candidates alone are used.
4. `rev0 = FirstReverseGuess(r.Reverse, ctx.SheetMetalBodies[owner]?.Sigma, map.Sigma)`.
5. For each candidate pick `p`:
   - Clear the selection and run `map.Feature.Select2(false, 0)`. Then `SelectFaceByRay(ctx, fixed, p, 0, append: true)`. If it fails, move to the next candidate.
   - Create: `fm.InsertSheetMetal3dBend(r.Angle, r.UseDefaultRadius, r.Radius, rev0, pos, cba)`. If that gives nothing, `CreateDefinition(35)` and then `(36)` with the same selection and the values set (`ReverseDirection`, `SetCustomBendAllowance`), then `CreateFeature`.
   - Read back angle, radius flags, `PositionType`, reverse, allowance and the count of `SketchBend` sub-features.
   - If reverse or position differ: `FeatureEdit.SketchedBend(...)`, then read back again, allowance included (P5).
   - Witnesses: `m = SwShape.Misses(ctx, R(Witnesses), ctx.SeparateBody)`.
     - If m > 0: toggle reverse by edit, measure, and keep the better state (P5 allowance check after each edit).
     - If m = 0: stop.
     - Otherwise delete and continue with the next candidate, remembering the best.
6. If no candidate passes, rebuild the best one and add the Warning „Biegung weicht vom gespiegelten Original ab“. With no witnesses, the first candidate with `rev0` is kept and the alternative „Biegerichtung umgekehrt“ is offered.
7. `ctx.Register`. Then fill `ctx.Bends`: match the original `SketchBend` names to the new sub-features by their mirrored faces where `FoldRecord`s need them, otherwise by order when the count is 1.

**5.2 `CrossBreakBuilder`**
1. Find the face, on an own body, before creating anything.
2. `SelectFaceByRay` at the face's mirrored sample, then `fm.InsertCrossBreak(Angle, Radius)`.
3. If reverse differs: `FeatureEdit.CrossBreak`.
4. Compare `Lines` (P10).
5. The report says „Richtung nach Regel (Außennormale), nicht messbar“.

**5.3 `ClosedCornerBuilder`**
1. Resolve all faces; one missing means fail.
2. Try the variants in order: (a) all faces with mark 1 (the UI records 1, CADapter measured 0 and 1); (b) alternating 1 / 0x40000000 (official example); (c) all with mark 0.
3. Each variant runs `doc.InsertSheetMetalClosedCorner()` and `LastAddedOf(["CornerFeat"], is ClosedCornerFeatureData)`, then reads back `Faces` as a set against the mirrored signatures. If they differ, delete and try the next.
4. `FeatureEdit.ClosedCorner` for type, gap, ratio and open bend region where they differ, then read back.
5. Alternative for types 2 and 3: „Überlappung ↔ Unterlappung“.
6. Options the API does not expose (faces to match, coplanar, narrow corner, auto propagation) stay at their defaults. This goes in the README, not into every entry.

**5.4 `BreakCornerBuilder`**
1. Resolve all entities, with mark 0.
2. `doc.InsertSheetMetalBreakCorner(BreakType, Distance)` and `LastAddedOf(["BreakCorner"], …)`.
3. Edit `InternalCornersOnly`/`CenteredOnBendLines` if they differ.
4. Read back `GetEntitiesCount` and the entity set.

**5.5 `CornerReliefBuilder`** (only after a 2022+ capture, so the same session can create it)
1. Find the body through `BodyFace`, as `ShellBuilder.ShelledBody` does, on an own body.
2. `def = fm.CreateDefinition(120)`.
3. `Initialize(CornerType)`. `SetBodyScope(body)` must return 0.
4. For each corner: `AddNewCorner(f1, f2, f3, type, out c)` must return 0. Then set its properties late-bound.
5. `CreateFeature(def)`.
6. If `def` is null: the legacy path (P13), two-bend only, with a note.
7. Read back through `CornerCount`/`GetCornerAtIndex`, matching each corner by its mirrored face pair.

**5.6 `FoldBuilder`**
1. `fixed` = mirrored face, on an own body.
2. Bends (P8): `ctx.Bends[name]`, otherwise `FindBend` (full match, own bodies only in SeparateBody), otherwise the ordinal when `OwnerBendCount == 1`. All bends are required.
3. Selection variants:
   - (fixed 1, bends 4);
   - Fold only: (preceding UnFold with mark 0, fixed 1, bends 4);
   - (fixed 1, bends 2).
4. `InsertSheetMetalUnfold`/`Fold`, then `LastAddedOf([Kind == Unfold ? "UnFold" : "Fold"], …)`.
5. Read back `FixedFace` and `Bends`. If they differ, `FeatureEdit.Folds` with `Safe(...)` entities. If they still differ, delete and try the next variant.
6. Store the resolved bends in `ctx.Bends`.

### 6. Mirror rules (corrected table)

| Feature | Rule | Why |
|---|---|---|
| Sketched bend: lines | via the rebuilt sketch, on the mirrored support face if possible | `SketchMirror`; on the face σ = +1 |
| fixed face / point | face by its mirrored fingerprint; point R(p); otherwise one candidate per region | R maps the fixed region onto the fixed region |
| angle, radius, position, allowance, K-factor | unchanged; position converted with 4 → 0 | intrinsic; two encodings |
| reverse | first guess from σ_sheet (area 1), otherwise σ_sketch; **decided by witnesses** | the reference SOLIDWORKS measures against is unproven (P2) |
| per-bend overrides | not transferable; note | writes to `IOneBendFeatureData` are ignored (P6) |
| Cross break | face mapped; angle, radius and reverse unchanged | relative to the outward normal [?, L11]; not measurable |
| Closed corner | faces in order; type, gap, ratio, open region unchanged | roles kept |
| Break corner | edges and faces mapped; values unchanged | intrinsic |
| Corner relief | Face1/2/3 mapped in order; all values unchanged | roles kept; relief shapes are symmetric about the bend lines |
| Unfold/Fold | fixed face mapped; bends by name, then fingerprint | no direction |
| Corner trim | not rebuilt | relief settings cannot be read (P3) |

### 7. Verification
- **Builder level, always on:** witnesses for the sketched bend; read-back of values and references for every builder.
- **Step level, "Jeden Schritt prüfen":** volume and moment as usual.
  - Bends and unfold/fold change the moment, not the volume.
  - Closed corners add material; break corners and reliefs remove it.
  - Face samples now include sub-feature faces (P4).
- **Cross break:** no measurement. Status OK, with the rule in the details.
- **Flat state:** area 1's flat-state check also covers the K-factor of sketched bends and unfold/fold.

### 8. Same part, new part, assembly
- **New part.** Every reference is found by mirrored fingerprint in the mirrored state.
- **Same part, separate body.** Every face, edge, bend and body must be on an own body before creation (P12). `OriginalUntouched` remains the backstop for solid-changing features. The cross break relies on the pre-check alone.
- **Same part, selected features.** These act on the existing bodies. Bends and faces may then be the original's, which is intended.
- **Assembly.** Same as a new part, with the part's standard-plane mirror (`ComponentPlacement`). There is nothing specific to this area. A refused lofted bend in feature mode (P11) saves nothing.

### 9. Failure and cleanup
- Variants are deleted with `BuilderSupport.Delete`, keeping the sketch. Only the final variant is registered.
- If no variant passes, the best one is rebuilt; if none could be created, no feature remains, and `RemoveStepHelpers` removes the helpers.
- An exception goes to `RemoveLeftovers`. All area-3 features are top-level; the corner trim, which lives under the Flat-Pattern, is not built.
- Capture never aborts the run (P9).

### 10. Implementation plan
1. **Integration first**, together with areas 1, 2 and 4:
   - `RefusesRun(type, fallbackApplies)`;
   - `UnsupportedRecord.TriggersFallback`;
   - `IOwnsParts`;
   - sub-feature sampling;
   - `BendOverrides`;
   - `SheetMetalBodyMap.Sigma`;
   - Flat-Pattern sub-feature collection (P3).
2. **Core:**
   - `SheetMetalBendRecords`, `SheetMetalBendRules`, `BendMatcher`, `BendOverrides`;
   - the `UnsupportedFeatures` entries;
   - `SwConstTests` for the new numbers.
3. **Add-in helpers:** `SwLate.Invoke`/`InvokeOut`, the `FeatureEdit` additions, `LastAddedOf`, `SelectFaceByRay`, `SwShape.Misses`, `BuildContext.SketchSupportFaces`/`Bends`, and the `SketchBuilder` face support.
4. **Capture and builders**, in this order: sketched bend; closed corner; break corner; unfold/fold; corner relief; cross break.
5. **Docs:**
   - README "Unterstützte Features" and "Noch nicht": corner trim, welded corner, lofted bend, and the closed-corner options at their defaults;
   - ARCHITEKTUR: a section on sketched bends (witnesses, side candidates);
   - STOLPERSTEINE: P1, P2, P5, P6, the corner trim, and the live-test rows.
6. **Checks:** build against 2019 and 2024, run the Core tests (net8 and net48), then the live tests below.

**Effort:** about 2,000 lines, 7–8 development days and 2–3 days of live testing.

### 11. Core tests (`SheetMetalBendTests.cs`)
1. `Bend_position_maps_to_the_creation_code`: 4 → 0, 1 → 1, 2 → 2, 3 → 3; 0, 5, 6 and 7 → null.
2. `First_reverse_guess_prefers_the_sheet_sigma`: truth table over (reverse, σ_sheet ∈ {+1, −1, null}, σ_sketch).
3. `Reverse_rule_mirrors_the_bent_direction_for_either_reference`: 1000 random reflections; the algebra from `bendcheck.py`.
4. `Side_candidates_give_one_interior_point_per_region`:
   - one line → 2 regions, two parallel lines → 3, two crossing lines → 4;
   - every point is ≥ clearance from all lines;
   - regions narrower than 2·clearance give no point;
   - the set commutes with R: `SideCandidates(R input)` ≅ R(`SideCandidates(input)`) as sets of regions.
5. `Region_of_a_point_is_invariant_under_the_reflection` (sign vector against mirrored lines).
6. `Fixed_point_prefers_the_documented_sketch_reading`: sketch reading on the face, model reading on the face, neither, and both in different regions (ambiguous).
7. `Overlap_and_underlap_swap_butt_stays`.
8. `Legacy_corner_relief_lengths`: square, obround, circular, filleted square (diameter → radius); other types → null.
9. `BendRef_mirror_keeps_names_and_mirrors_faces`; `Ordinal_is_usable_only_for_single_bend_owners`.
10. `Bend_matcher`:
    - a full match wins;
    - a partial match is rejected;
    - own bodies are preferred;
    - `restrictTo` excludes the original;
    - two equal candidates are flagged as ambiguous.
11. `Bend_overrides_name_bends_that_differ_from_the_parent`.
12. `Corner_relief_item_mirror_keeps_face_roles`.
13. `Unsupported_features`:
    - `CornerFeat` and `CornerRelief` are sheet metal;
    - the corner trim reason;
    - `CrossBreak` is non-solid;
    - `RefusesRun("LoftedBend", fallbackApplies: false)` is true, and false with the fallback;
    - the welded corner does not trigger the fallback.
14. `Variant_comparison`: a feature in error never wins; fewer misses wins.
15. `SwConstTests`, against 32.1.0:
    - `swFlangePositionTypes_e`, `swClosedCornerTypes_e`, `swBreakCornerTypes_e`, `swCornerReliefType_e` (0–8 by name), `swLoftedBendFacetOptions_e`;
    - `swFeatureNameID_e.swFmSketchBend` = 35, `swFmSM3dBend` = 36, `swFmCornerRelief` = 120.

### 12. Live-test assumptions (rows for the STOLPERSTEINE table)

| # | Assumption | Fallback |
|---|---|---|
| L1 | Type names: `CornerFeat` (closed corner), the corner relief name, the welded corner name; the corner trim is a sub-feature of Flat-Pattern | Collected by data interface; unknown names give the generic reason |
| L2 | The `GetFixedFace` point is in sketch space (help) | Model reading as fallback; ambiguity noted |
| L3 | `SelectByRay` (official example) selects the fixed face under `UpdateFreeze` (graphics off, tree off); `GetSelectionPoint2` returns the hit point | Next candidate; never a point-less selection |
| L4 | `InsertSheetMetal3dBend` ignores `FlipDir` (eyfel), and `ModifyDefinition(ReverseDirection)` works | Witnesses decide; recreate |
| L5 | `PCBA` at creation is honoured; whether an edit resets a custom allowance (CADapter: yes when the allowance is changed) | Read back after every edit; note |
| L6 | `BendPos` = 0 gives centre line | Read back `PositionType`; edit |
| L7 | The bend direction reference: bend sketch, face, or the hidden sheet orientation (eyfel) | Witnesses; first guess σ_sheet |
| L8 | A bend sketch on a coplanar reference plane is accepted (help 2020: a plane can be preselected) | Face support (P7) |
| L9 | `SM3dBend.GetFaces` versus the `SketchBend` sub-feature faces | Sub-feature sampling |
| L10 | Writes to `IOneBendFeatureData` are ignored (CADapter) | Overrides noted only |
| L11 | Cross break: reverse is relative to the outward normal; whether it has an editable sketch sub-feature | Lines compared; report text |
| L12 | Closed corner: mark 1 for faces to extend; `Faces` returns faces to extend; 0x40000000 = faces to match | Variants a–c; read-back as a set |
| L13 | Corner relief: `CreateDefinition(120)`, `InvokeOut` through IDispatch, `GetCornerAtIndex`, legacy marks | Legacy path; Unsupported |
| L14 | Unfold/Fold: bends mark 4 (official) versus 2; the setters work through `ModifyDefinition`; `GetFaces` of a bend under `AccessSelections` | Variants; name map |
| L15 | `GetLastFeatureAdded` returns the new feature while Flat-Pattern stays last (area 1 L8) | Before/after name comparison |
| L16 | The relief settings of a user corner trim are not readable anywhere | Unsupported (P3) |
| L17 | Break corner: mark 0 for edges and faces; `Entities` reads back the same set | — |

**A good first live test:**
- A 100 × 50 × 2 mm base flange on *vorne*, with one sketched bend line on its top face (fixed side x < 50, 90°, custom K = 0.3).
- Mirror it at *rechts* and at *vorne* (the face looking away from the view, P1), each time with "Jeden Schritt prüfen" off.
- Expect, every time: the mirrored side stays fixed, the bend goes the mirrored way (witnesses), and K = 0.3 is read back.
- Then a U-channel made of two lines in one sketch, a box with four edge flanges and one closed corner, and Unfold → cut across a bend → Fold.

**Sources:** [GetTypeName2 (help mirror)](https://raw.githubusercontent.com/streetartist/solidworks-automation/main/docs/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~GetTypeName2.md), [CADapter sw_sheetmetal.py](https://raw.githubusercontent.com/luigidoria/CADapter/main/solidworks/sw_sheetmetal.py), [eyfel SolidWorksService.Analyze.cs](https://raw.githubusercontent.com/eyfel/mcp-server-solidworks/main/execution/solidworks/SolidworksExecution/Services/SolidWorksService.Analyze.cs), [Break Corner/Corner-Trim PropertyManager (2017)](https://help.solidworks.com/2017/English/SolidWorks/sldworks/HIDD_SHEET_METAL_BREAK_CORNER.htm), [Sketched Bends (2019)](https://help.solidworks.com/2019/English/SolidWorks/sldworks/c_Sketched_Bends.htm), [Sketched Bend PropertyManager (2020)](https://help.solidworks.com/2020/english/SolidWorks/sldworks/HIDD_SHEET_METAL_DVE_3D_BEND.htm). The two help.solidworks.com pages were read only through search summaries, because the host does not resolve here.
