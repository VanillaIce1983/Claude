# Area 4 design: forming tools, tab and slot, sheet-metal gusset, vent, and the fallback

## Answer for the user (German request: "can the sheet-metal functions be made to work?")

Yes, for most of Area 4, with one hard limit:

- **Tab and slot** and the **sheet-metal gusset** can be rebuilt parametrically. Both have data interfaces in 2019.
- **Forming tools** can be rebuilt parametrically when the tool is mirror-symmetric, which most library tools are. 2019 cannot read the rotation angle or the flip state, so the design finds both from the geometry.
- **Vents** cannot be rebuilt through the API in 2019 or 2024. There is no data interface and no method to create one. Vents always go through the fallback.
- **Fallback**: the mirrored body is copied with `IBody2.Copy2` plus `IBody2.ApplyTransform` and a reflection matrix, inserted with `IPartDoc.CreateFeatureFromBody3`, then turned back into sheet metal with `InsertConvertToSheetMetal2`, or `IPartDoc.InsertBends2` as the second attempt.
  - It works per body. The body is imported in its state right after the last feature that cannot be rebuilt, so every feature after that point stays parametric.
  - It uses no Mirror-feature API. Real GitHub code uses this same pattern.

Nothing in `/home/user/Claude` was modified. Every member below was checked by reflection in the 27.3.0 interop and compared against 32.1.0.

## 0. Sources and evidence

- **Reflection probe:** `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/area4probe/Program.cs`. Run as `dotnet out/area4probe.dll 27|32 types|members|exact|enum|enumval|find <regex>`.
- **Mirrored 2025 API help pages** (from streetartist/solidworks-automation): `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/area4docs/`
- **GitHub sources:** `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/area4gh/`
- **Numeric check of the rotation formula** (max deviation 2e-15 over 2000 random cases): `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/area4probe/rotcheck.py`

Real-world usage found on GitHub:

| Topic | Repository / file | What it shows |
|---|---|---|
| `Copy2` + `ApplyTransform` with a reflection | tbcphutai-bit/ADDIN-SolidWorks `Commands/MirrorV7/NativePartMirrorV7.cs`, `ReflectionApiTransformV7.cs` | Uses `body.Copy2(false)` and `copy.ApplyTransform(T)` with a Householder (det −1) matrix, then checks vertices and volume. `CreateTransform` is tried with the direct array first. If the probe points do not map correctly, it retries with the matrix negated and scale −1 ("Some API paths normalize rotation axes"). |
| Copy from one document, transform, insert into another | mcneds/BeamNG.cad `tools/RearHubRepair.cs` | `Copy2(false)` from another document, then `CreateTransform`, `ApplyTransform`, `CreateFeatureFromBody3(body,false,swCreateFeatureBodyCheck)` into a part that already has 2 bodies. The result has 3 bodies, so the import is a separate body. |
| Official example | "Combine Assembly Components into Part" (mirrored in f1owkang/solidworks-plugin-dev) | `Copy`, `ApplyTransform(comp.Transform2)`, `Operations2`, `CreateFeatureFromBody3(…, Check+Simplify)` |
| xCAD | xarial/xcad `SwDumbBody.cs` | `CreateFeatureFromBody3(body, false, swCreateFeatureBodySimplify)` |
| Convert and gusset, measured on SW 2017 | luigidoria/CADapter `solidworks/sw_sheetmetal.py` (local copy in `scratchpad/ghsrc/cadapter_sw_sheetmetal.py`) | `InsertConvertToSheetMetal2` returns **False even when it builds**. Bend edges must be selected through `IEntity.Select4` (selecting by coordinate drops the 2nd edge). `InsertSheetMetalGussetFeature` (v1, 21 arguments) works with the two concave-side faces at mark 0, BDraft=false, draft angle in radians. |
| Forming tool and convert | 0xFFD/solidworks-mcp `tools_sheetmetal.py` | `InsertFormToolFeature(path, flip, rad(angle), "", F,F,F,F, True)` with the face preselected. "The tool lands at the sketch origin": the entities of the 1st ProfileFeature sub-sketch (position sketch) are moved, and the **origin of the 2nd ProfileFeature sub-sketch is the insertion point**. Convert uses face mark 0 and edges mark 1. `CreateFormTool2` uses stop face mark 0 and remove faces mark 1, which **contradicts the help (1/2)**. |
| No data interface for forming tools | angelsix/SolidDNA, weianweigan/SolidWorksLookup | `"FormToolInstance"`: "No interface" |

## 1. API inventory (27.3.0, SOLIDWORKS 2019)

### GetTypeName2 strings

- `FormToolInstance`: no data interface in 2019–2024. `ILibraryFormToolFeatureData` arrives with **2025 SP2** (release notes) and is late-bound only.
- `SMGusset`: `ISMGussetFeatureData`
- Sheet-metal definition and flat pattern: `SheetMetal` (`ISheetMetalFeatureData`), `TemplateSheetMetal` (`ISheetMetalFolder`), `FlatPattern` (`IFlatPatternFeatureData`)
- Results of the fallback calls: `SolidToSheetMetal` from convert (`IConvertSolidFeatureData`, 2025 SP2 only), `FlattenBends` / `ProcessBends` from Insert Bends (`IBendsFeatureData`), `Imported` from CreateFeatureFromBody (no data interface)
- **Tab and slot: not documented.** The help example names the features "Tab and Slot-Tab1" and "-Slot1". Capture by the data interface `TabAndSlotFeatureData`, not by type name.
- **Vent: not documented, and there is no API.** No interface or member named *Vent* exists in 27.3 or 32.1 (only "Event" matches).

### swFeatureNameID_e (27.3)

- Present: `swFmFormToolInstance = 73`, `swFmTabAndSlot = 88`
- 32.1 only: `swFmSMGusset = 118`, `swFmSweptFlange = 117`, `swFmCornerRelief = 120`
- In neither version: `swFmSolidToSheetMetal`

### Signatures (27.3)

**IFeatureManager**
```
Feature InsertFormToolFeature(string Path, bool Flip, double RotAngle, string Config, bool OverrideDoc, bool ShowPunch, bool ShowProfile, bool ShowCenter, bool LinkToPart)
Feature CreateFormTool2(double OriginX, double OriginY)
bool    InsertConvertToSheetMetal2(double Thickness, bool ReverseThickDir, bool FindBends, double Radius, double Gap, int ReliefType, double ReliefRatio, int OverlapType, double OverlapRatio, bool KeepBody)
Feature InsertSheetMetalGussetFeature(bool BOffset, double DOffset, bool BFlipOffsetSide, int ProfDimType, double DIndentDepth, double DLength, bool BUseAngle, double DHeight, double DAngle, bool BFlipSides, double DWidth, double DThickness, bool BDraft, double DDraftAngle, bool BInnerCornerFillet, double DInnerCornerFilletRadius, bool BOuterCornerFillet, double DOuterCornerFilletRadius, int GussetType, bool BEdgeFillet, double DEdgeFilletRadius)
        // …2 adds (object ArrayOfFaces, object ArrayRefLines, object ArrayRefPoints); …3 adds (bool BOverrideDoc, bool BShowProfile, bool BShowCenter)
object  CreateDefinition(int Type);   Feature CreateFeature(object FeatureData)
object  GetSheetMetalFolder();        object GetFlatPatternFolder()
```

**IPartDoc**
```
bool    InsertBends2(double Radius, string UseBendTable, double UseKfactor, double UseBendAllowance, bool UseAutoRelief, double OffsetRatio, bool DoFlatten)
object  CreateFeatureFromBody3(object Body, bool MakeRefSurface, int Options)
Feature ICreateFeatureFromBody4(Body2 Body, bool MakeRefSurface, int Options)
```
- `swCreateFeatureBodyOpts_e`: Check=1, Simplify=2

**IBody2**
```
object Copy2(bool PreserveFaceIDs);  bool ApplyTransform(MathTransform Xform);  bool IsSheetMetal();
FaultEntity Check3 {get;};  object GetMassProperties(double Density);  void Save(object StreamIn);  bool IsTemporaryBody()
```
- `IFaultEntity`: `int Count`, `ErrorCode[int]`
- `IModeler`: `object Restore(object StreamIn)`
- `IMathUtility.CreateTransform(object ArrayDataIn)`. `ISurface` has **no** ApplyTransform, only `ICurve` does.

**ITabAndSlotFeatureData**
```
bool AccessSelections(object TopDoc, object Component);  void ReleaseSelectionAccess();  object SelectionAddNewGroup();  object SelectionGetGroups()
```

**ITabAndSlotGroupData** (all get; set)
```
object SelectionTabEdge, SelectionSlotFace, SelectionStartReferencePoint, SelectionEndReferencePoint, TabFace;
bool Offset, TabReverseDirection;  double D1OffsetFromStart, D2OffsetFromEnd, Spacing, TabLength, TabThickness, TabHeightValue, TabOffsetValue,
TabFilletEdgeTreatmentValue, TabChamferEdgeTreatmentValue, SlotClearance;  int SpacingType, SpacingNumberOfInstances, TabHeightType, TabEdgesType
```
- Enums: `swTabSlotFeatureHeightType_e` Blind 0 / UpToSurface 1 / OffsetFromSurface 2; `swTabSlotFeatureSpacingType_e` EqualSpacing 0 / SpacingLength 1; `swTabEdgesType_e` Sharp 0 / Fillet 1 / Chamfer 2.

**ISMGussetFeatureData** (`AccessSelections(ModelDoc2, Component2)`, `ReleaseSelectionAccess`)
```
object SupportingFaces, ReferenceLine, ReferencePoint; bool UseOffset, FlipOffset, UseAngleDimForProfile, FlipDimSides, DraftSideFaces,
FilletOuterCorners, FilletInnerCorners, FilletGussetEdges, OverrideDocSettings, ShowProfile, ShowCenter; double OffsetDistance, IndentDepth,
ProfileLengthDim, ProfileHeightDim, ProfileAngleDim, IndentWidth, GussetThickness, DraftAngle, OuterCornerFilletRadius, InnerCornerFilletRadius,
EdgeFilletRadius; int ProfileDimensionScheme, GussetType, ProfileType
```
- Enums: `swSheetMetalGussetProfileDimType_e` IndentDepth 0 / ProfileDimensions 1; `swSheetMetalRibGussetType_e` Rounded 0 / Flat 1.

**ISheetMetalFeatureData**
```
BendRadius, Thickness, object FixedReference, int BendAllowanceType, string BendTableFile, KFactor, BendAllowance, bool UseAutoRelief,
int AutoReliefType, ReliefRatio, GetCustomBendAllowance()/SetCustomBendAllowance(CustomBendAllowance), int GetUseGaugeTable(out bool, out string), SetUseGaugeTable(bool,string)
```
- `ICustomBendAllowance`: Type, BendTableFile, KFactor, BendAllowance, BendDeduction
- `IFlatPatternFeatureData`: `object FixedFace2`, `FixedFace`
- `IBendsFeatureData`: `GetFixedFace()`, BendRadius, KFactor, …
- Enums: `swBendAllowanceTypes_e` 1 table, 2 K, 3 direct, 4 deduction, 5 calculation table; `swSheetMetalReliefTypes_e` 1 rect, 2 tear, 3 obround, 4 none, 5 tear-bend.

**IFeature**
```
void ListExternalFileReferences2(out object ModelPathName, out object ComponentPathName, out object Feature, out object DataType, out object Status, out object RefEntity, out object FeatCom, out int ConfigOption, out string ConfigName)
int ListExternalFileReferencesCount()
```
- Other members used: `GetFaces`, `GetFirstSubFeature`/`GetNextSubFeature`.

**Version gate:** `ISldWorks.RevisionNumber()` (27.3).

### Selection marks

| Call | Marks | Source |
|---|---|---|
| Forming tool | placement face preselected, mark 0 | help + 0xFFD |
| Convert to sheet metal | fixed face 0, bend edges 1 | official example + 0xFFD; CADapter says marks are "indifferent" but objects must go through Select4 |
| Gusset | legs mark 0 | CADapter + help example |
| `CreateFormTool2` | stop face 1, remove faces 2 (help) vs 0 / 1 (0xFFD) | conflict, needs a live test |
| `InsertBends2` | not documented; assume fixed face at mark 0 | live test |

## 2. Forming tools (`FormToolInstance`)

### What can be read

| Item | 2019–2024 | 2025 SP2+ (late-bound `ILibraryFormToolFeatureData` via `SwLate`) |
|---|---|---|
| Tool path and configuration | `ListExternalFileReferences2` (ModelPathName, ConfigName) on the feature. Fallback: search the folders from `GetUserPreferenceStringValue(swFileLocationsDesignLibrary = 38 / swFileLocationsPaletteFormTools = 4)` for `<feature name without trailing digits>.sldftp/.sldprt` (heuristic, noted in the report) | `FormToolPath`, `FormToolConfiguration` |
| Link to tool | true if a reference is listed | `LinkToFormTool` |
| Rotation | not readable; measured (see below) | `RotationAngle` (still verified) |
| Flip | not readable, also not in the 2025 interface; measured | — |
| Flat display settings | unknown; use document defaults (OverrideDoc=false) and note it | `OverrideDocumentSettings`, `ShowPunch`/`Profile`/`Center` |
| Instances | position sketch = the **first** `ProfileFeature` sub-feature; every user point is one instance | same |
| Instance frame (H-FT1) | 2nd `ProfileFeature` sub-feature: its sketch frame (`SwMath.SketchFrame`) has its origin at the insertion point (0xFFD). Its axes probably carry the rotation (live test) | `PickPoint`, `PlacementFace` |

The help states that the rotation angle is measured "with respect to the default horizontal line in the placement sketch".

### Mirror rule

The tool part is **not** mirrored. A mirror-symmetric tool G (S_α(G)=G, where S_α is the reflection about the plane through the punch axis at angle α in tool coordinates) gives:

`R ∘ I ∘ S_α = F′ ∘ Rz(β − θ − 2α)`, so `θ′ = β − θ − 2α`

- θ = original rotation.
- β = angle from the x-axis of the target placement sketch to R(x of the original placement sketch), measured about R(n).
- The punch side relative to the outward face normal is preserved, because R keeps outward normals outward. So flip′ is whatever makes the new instance protrude on the same side as R(original).

Because θ, α and β are partly unreadable in 2019, the builder does not rely on the formula. It **measures**:

1. Insert at the mirrored insertion point with θ0 = 0 and flip0 = false.
2. **Flip:** compare the dominant sign of `(q−P′)·n′` for the mirrored samples q = R(p) against the samples of the new instance (`FormToolPlacement.ProtrusionSide`). If the side is wrong, insert once more with the other flip.
3. **Rotation by registration, without rebuilds:** for ψ on a 0.5° grid, rotate the mirrored samples of instance 1 about the new insertion axis by −ψ and test their distance to the new feature's faces (`TopologyCapture.DistanceToFace`).
   - Coarse pass: one sample per ψ, tolerance r·Δψ/2.
   - Fine pass: refine to ≤ 10 µm with all samples.
   - If several ψ match, the tool is rotationally symmetric; take the smallest |ψ|.
4. Re-insert with θ0+ψ and check. If that fails, use θ0−ψ (the rotation sense is undocumented).
   - 2019: delete and re-insert, since there is no data interface.
   - 2025 SP2+: `RotationAngle` + `ModifyDefinition`.
   - Expected cost: about 1–2 re-insertions and roughly 10–20k `GetClosestPointOn` calls (about 1 s).
5. **No ψ matches with either flip:** the tool is chiral (asymmetric). Apply the policy in `MirrorOptions.AsymmetricFormTool`:
   - `Report` (default): the feature fails, and on a qualifying body the fallback (section 4) takes over.
   - `MirrorTool` (opt-in, phase 2): run a nested `MirrorJob` on the tool part. Use a standard plane of the tool that contains the punch axis as the mirror plane, so S is known. Save as `<Werkzeug>_gespiegelt.sldftp` next to the target, never inside the Design Library, and place that tool. This needs the tool's definition feature rebuilt through `CreateFormTool2`. That feature has no data interface, so the stop face and the faces to remove must be guessed (blue/red face colours) and checked by placement. The report must name the new file.

When H-FT1 holds, the profile-sketch frame gives ψ directly as a first guess. Registration stays as the check.

### Position sketch

The pattern is the same as for holes (`PositionedHoleRecord`): the position sketch and the profile sketch are captured as `PartOfFeatureRecord` (Owner = the forming tool).

- **Variant A:** `SketchBuilder.BuildInto(rec.Position, newPositionSketch)`. This writes the mirrored points, relations and dimensions (to mirrored model edges), then deletes the generated entities.
- **Variant B**, if A leaves the feature in error: move the generated entities with `Extension.MoveOrCopy` from the current insertion point to P′₁ (the 0xFFD method), add points for instances 2..N, and note that the position sketch dimensions were not carried over.

### Capture outline (`FeatureCapture.FormTool.cs`)

```csharp
case when type == "FormToolInstance":
  r = new FormToolRecord(f.Name, type, positionSketch.Name, ReadSketch(positionSketch));
  SwFormTool.ResolveTool(f, out path, out config, out linked);           // ext refs → SwLate(def,"FormToolPath") → library search
  if (SwLate.Get(f.GetDefinition(),"RotationAngle") is double a) r.RotationAngle = a;  // 2025 SP2+
  r.InsertionPoints = user points of position sketch → model coords;
  r.ToolFrame = math.SketchFrame(profileSketch) (H-FT1, may be null);
  r.PlacementFace = face under InsertionPoints[0] (rolled back before f, like HoleAxis/AdvancedHole);
  r.ShapeSamples  = up to 24 points on f.GetFaces() (rolled back after f; extra UV points via Surface.Evaluate)
  if path == null → throw NotSupported("Werkzeugdatei nicht gefunden") → UnsupportedRecord (fallback trigger)
```

`HolePart`/`CaptureOnItsOwn` should be generalized to `PositionedFeatureRecord` so that both sub-sketches become `PartOfFeatureRecord`.

### Builder outline (`FormToolBuilder`)

1. Find the mirrored placement face. It must be a face; a substitute plane is not allowed.
2. `SelectFaceAt(face, R(P₁), mark 0)` (as for holes).
3. `InsertFormToolFeature(path, false, 0, config, false, false, false, false, linked)`
4. If the result is null and the version is 2025 SP2+: late-bound `CreateDefinition(73)`, set `FormToolPath`/`RotationAngle`/`LinkToFormTool`, then `CreateFeature`.
5. Position sketch (A, then B).
6. Flip (re-insert if needed).
7. Registration for ψ (re-insert or `ModifyDefinition`).
8. Instance count must equal `InsertionPoints.Count`.
9. Register; same part: the original-body guard as usual.

**Alternatives offered to StepVerifier:** only on 2025 SP2+ ("Drehung +180°" through `RotationAngle`). In 2019 the feature object changes on every re-insert, and `StepVerifier.Measure` holds `result.Feature`, so all variant search happens inside the builder.

## 3. Tab and slot, gusset, vent

### Tab and slot (2019: `TabAndSlotFeatureData`, create through `CreateDefinition(88)`)

**Capture:** `case TabAndSlotFeatureData ts:`, then `AccessSelections(_doc, null)`, then for each `(TabAndSlotGroupData)SelectionGetGroups()[i]` read:

- `TabEdge` → `EdgeSignature`
- `SlotFace`, `TabFace` → `FaceSignature`
- start and end references → `GeometryRef` (Vertex / SketchPoint)
- all numeric fields

If the "-Slot" feature also answers the interface, capture it as `PartOfFeatureRecord` (Owner = tab feature).

**Builder:**

1. `CreateDefinition(88)`, then `SelectionAddNewGroup()` per group; set `SelectionTabEdge` and `SelectionSlotFace` (objects found by fingerprint); `CreateFeature`. This matches the help example.
2. A new `FeatureEdit.TabAndSlot` sets `TabFace`, references, offsets, spacing, height and edge treatment through `SelectionGetGroups` + `ModifyDefinition` (the help example modifies exactly this way).

**Mirror rules:**

- Fingerprints are mirrored.
- **D1/D2:** with reference points, D1 stays on the mirrored start reference. Without them, D1 and D2 are counted from the ends of the edge itself, which may run backwards in the target. Use `EdgeLocation.Map(wantedMirrored, actual, 0, 1) == 1` to detect a reversed edge and swap D1/D2 if so (`TabSlotRules.Offsets`).
- `TabReverseDirection` refers to the surface's outward normal, which a mirror keeps outward, so the flag is unchanged.
- Lengths, spacing, clearance and edge treatment are unchanged.

**Alternatives:** "D1/D2 getauscht", "Laschenversatz umgekehrt" (only for OffsetFromSurface).

### Sheet-metal gusset (`SMGusset`, `ISMGussetFeatureData`)

**Capture:**

- `SupportingFaces` → ordered list of `FaceSignature` (1–2 faces)
- `ReferenceLine` → `GeometryRef` (Axis or SketchSegment)
- `ReferencePoint` → Vertex / SketchPoint
- all fields
- `OrderOnBend` = how many gussets of the same bend were created earlier. When no ReferencePoint is set, the implied reference is "the edge of the last gusset".

**Builder:**

1. Select the mirrored legs in their original order with mark 0, using `Select4` on the objects.
2. `InsertSheetMetalGussetFeature` (v1, 21 arguments; measured to work).
3. Then a new `FeatureEdit.SMGusset` for ReferenceLine, ReferencePoint, ProfileType and the override/display settings.
4. Pitfalls: angles in radians; `BDraft` must be `DraftSideFaces` and nothing else (true without a draft makes the call return null).
5. v2 with `ArrayOfFaces`/`ArrayRefLines`/`ArrayRefPoints` only as a second attempt.

**Mirror rules:**

- `FlipOffset` follows σ of the reference-line direction: `BuilderSupport.FlagForReference(ctx, rec.ReferenceLine, targetLine, flip)`. Without a reference line, the bend line's internal direction is unknown, so keep the flag and offer the toggle.
- `FlipDimSides` is tied to the leg order, which is kept, so it is unchanged (toggle offered).
- Dimensions and fillets are unchanged.
- Rebuild order on the same bend is kept, because of the "edge of the last gusset" default.

**Alternatives:** "Versatz auf der anderen Seite", "Schenkel d1/d2 getauscht" (only for the ProfileDimensions scheme).

### Vent

No API in 2019 or 2024, for reading or creating. It always becomes an `UnsupportedRecord`, which triggers the fallback on a sheet-metal body.

- Add the reason text to `UnsupportedFeatures`: "Lüftungsöffnung: keine API zum Lesen oder Anlegen".
- The type name `"Vent"` is a guess and must be confirmed live.
- Remove `FormToolInstance` and `SMGusset` from the blanket sheet-metal reason in `UnsupportedFeatures`.

## 4. Fallback: mirrored body as plain geometry, then sheet metal again

### Getting the geometry without a Mirror feature

1. **Capture phase** (the original must not be touched later): with the rollback bar after feature k, call `Body2.Copy2(false)` on the sheet-metal body. This gives a temporary body.
2. Build the transform:
   - `T = MathUtility.CreateTransform(Reflection.ToAffine().ToSwArray())` (det −1).
   - Check it with 5 probe points. If it fails, try `(−M, scale −1)`, the ReflectionApiTransformV7 approach. `Affine.FromSwArray` already multiplies the columns by the scale, so both arrays are the same map in Core.
3. `copy.ApplyTransform(T)`, then verify:
   - `Check3.Count == 0`
   - volume > 0 and equal to the original
   - the vertex set equals R(the original vertices) (`PointSets.Unmatched`)
4. Hold the snapshot on the add-in side in a `FallbackSnapshotStore`, keyed by body and "after feature". Core records hold no COM objects.
5. If temporary bodies do not survive closing the source part (assembly context), serialize with `IBody2.Save(IStream)` and restore with `IModeler.Restore`.

Rejected alternatives:

- `CreateBodyFromSurfaces` / `CreateBrepBody` with mirrored surfaces: `ISurface` has no transform, so every surface would have to be rebuilt analytically. Only worth it if `ApplyTransform` refuses det −1.
- Parasolid round trip: import has no reflection option.
- Move/Copy Body: no reflection.
- Scale with −1: not allowed, and it would depend on the source body.

### Inserting and converting (`SheetMetalFallbackBuilder`)

1. `PartDoc.CreateFeatureFromBody3(body, false, swCreateFeatureBodyCheck)` gives a separate "Imported" body.
   - Check `ListExternalFileReferencesCount() == 0`.
   - Register the feature so `IsOwnBody` holds.
2. **F1 (convert):**
   - Select the fixed face with `Select4` at mark 0: R(the original fixed face) taken from `FlatPattern.FixedFace2`, else `SheetMetal.FixedReference`.
   - Call `InsertConvertToSheetMetal2(t, false, FindBends:true, r, gap, reliefType, reliefRatio, overlapType:1, overlapRatio, KeepBody:false)`.
   - **Ignore the return value.** Success means: new `SheetMetal` + `SolidToSheetMetal` features without errors, `IsSheetMetal()` true, and volume, moment and face samples unchanged.
   - Then a new `FeatureEdit.SheetMetal` sets `BendAllowanceType`/`KFactor`/`BendAllowance`/`BendTableFile` (or `SetCustomBendAllowance`) and `SetUseGaugeTable` to the original's values.
   - Second fixed-face attempt: the opposite face (sample − n·t).
3. **F2:** `IPartDoc.InsertBends2(r, bendTable or "", K or −1, allowance or −1, useAutoRelief, clamp(reliefRatio, 0.05, 2.0), DoFlatten:true)` with the fixed face selected.
4. **F3:** imported body only, reported as "kein Blechteil – Abwicklung fehlt".
5. **2025 SP2+:** late-bound `CreateDefinition(swFmSolidToSheetMetal)` + `IConvertSolidFeatureData.Initialize` as the preferred path. The enum value is not in either interop, so it must be read live.

Bodies containing forming tools may flatten badly with F1/F2. Choose the snapshot point so that forming tools come after it whenever possible.

### When it is triggered

- **At capture time** (`FallbackPlanner.Plan`). A body qualifies if:
  - it is a sheet-metal body,
  - every feature that made it is in the run (whole part or selected bodies; never when only features are selected),
  - no feature also feeds other bodies (`OtherBodies` empty).

  Trigger: the last non-rebuildable contributing record, i.e. an `UnsupportedRecord` with ChangesSolid (vent, unknown sheet-metal type, unreadable definition), or a forming tool whose file cannot be found.

  Plan: snapshot "after k" replaces records ≤ k of that body with `CoveredByFallbackRecord`. A sketch is covered only if all its consumers are covered. Insert one `SheetMetalFallbackRecord` at the position of the first covered record. Later records stay parametric and find their faces by fingerprint, because the imported body is exactly the mirrored state.
- **At build time:** a sheet-metal record of a qualifying body ends Failed, or its verification ends "weicht ab".
  - Extend the plan with the smallest snapshot ≥ j and **restart the build**. Records are reusable data, so no recapture is needed.
  - Restart differs by target: a new part is closed without saving and recreated; in the same part, every feature created by the job is deleted in reverse order (the `RemoveLeftovers` pattern).
  - Cap: 3 restarts, then whole-body final state.
  - Snapshots are taken after every feature of a sheet-metal body during `MassCapture` (cap 64; above that, only capture-time triggers plus the final state).

### Report

- **General line:** „Ersatzweg für Körper „Blech1“: Stand nach „Lüftung1“ als importierter Körper (gespiegelt per Körpertransformation, kein Spiegel-Feature, keine Verknüpfung zum Original), in Blech umgewandelt (Dicke 2 mm, Radius 1 mm, K 0,44; feste Fläche gefunden). Grund: Lüftung1 – keine API. Danach wieder parametrisch: …"
- **Fallback entry:** Status Ok or Warning, with mass and face-sample verification, and which stage (F1/F2/F3) was used.
- **Covered features:** Warning, „Im Ersatzkörper „…“ enthalten – nicht parametrisch“. This blocks "Original löschen", which is intended.
- **Restart:** add „Erster Durchgang: „X“ scheiterte (…), Körper ab dort über den Ersatzweg.“

### Conflict with "no Mirror API"

None. The fallback does not use `InsertMirrorFeature(2)`, `IPartDoc.MirrorFeature`/`MirrorPart`/`MirrorPart2`/`InsertMirrorAll`/`InsertMirrorFeature`, derived parts or `MirrorComponents`. A generic body transform is applied to a temporary body.

The real compromise is a different one: the Imported feature is not editable as a feature. The sheet-metal features (radius, K-factor) and every feature after the snapshot are editable. The report says so, and the fallback can be switched off.

## 5. Records (Core, `Records/SheetMetalArea4Records.cs`)

```csharp
public sealed class SheetMetalParameters { double Thickness, BendRadius; int BendAllowanceType; double KFactor, BendAllowance, BendDeduction;
  string BendTableFile=""; bool UseAutoRelief; int AutoReliefType; double ReliefRatio; bool UseGaugeTable; string GaugeTableFile="";
  FaceSignature? FixedFace; }                                     // per body, from its "SheetMetal" feature (Body2.GetFeatures)

public abstract class PositionedFeatureRecord : FeatureRecord     // refactor: PositionedHoleRecord derives from it
{ string PositionSketchName; SketchModel Position; List<Vec3> Points; GeometryRef? Face; }

public sealed class FormToolRecord : PositionedFeatureRecord      // "FormToolInstance"
{ string ProfileSketchName=""; string ToolPath=""; string ToolConfiguration=""; bool LinkToTool; string ToolSource; // ext-ref|late-bound|library search
  double? RotationAngle; bool? OverrideDoc, ShowPunch, ShowProfile, ShowCenter; Frame3? ToolFrame; List<Vec3> ShapeSamples; int ProtrusionSide; }

public sealed class TabSlotGroup { EdgeSignature TabEdge; FaceSignature SlotFace; FaceSignature? TabFace; GeometryRef? StartReference, EndReference;
  bool Offset; double D1OffsetFromStart, D2OffsetFromEnd; int SpacingType, SpacingNumberOfInstances; double Spacing, TabLength, TabThickness;
  int TabHeightType; double TabHeightValue, TabOffsetValue; bool TabReverseDirection; int TabEdgesType; double TabFilletEdgeTreatmentValue,
  TabChamferEdgeTreatmentValue, SlotClearance; }
public sealed class TabAndSlotRecord : FeatureRecord { List<TabSlotGroup> Groups; string? SlotFeatureName; }

public sealed class SheetMetalGussetRecord : FeatureRecord        // "SMGusset"
{ List<FaceSignature> SupportingFaces; GeometryRef? ReferenceLine, ReferencePoint; int OrderOnBend; /* + every ISMGussetFeatureData field 1:1 */ }

public sealed class SheetMetalFallbackRecord : FeatureRecord      // name "Spiegelwerk-Ersatz <Körper>", type "Imported"
{ string BodyName; string? AfterFeature; SheetMetalParameters Parameters; List<Vec3> Vertices; List<string> Covered; string Reason; }
  // ExpectedChange = mass of the snapshot body; FaceSamples = its faces

public sealed class CoveredByFallbackRecord : FeatureRecord { string FallbackName; public override bool ChangesSolid => false; }
```

Options added to `MirrorOptions`: `SheetMetalFallback {Off, PerBody=default}`, `AsymmetricFormTool {Report=default, Fallback, MirrorTool}`.

## 6. Add-in outline

**Capture**
- `FeatureCapture.FormTool.cs`
- `FeatureCapture.TabSlotGusset.cs`: switch cases `TabAndSlotFeatureData` and `SMGussetFeatureData`, placed before the default
- `Capture/SheetMetalSnapshots.cs`: body set, parameters, fixed face, `Copy2` per feature during `MassCapture`, consumers via `GetChildren`

**Sw helpers**
- `SwFormTool.cs`: path, sub-sketches, late-bound reads
- `SwSheetMetal.cs`: `ISheetMetalFeatureData`/`FlatPattern` reads, convert / InsertBends calls
- `SwTempBody.cs`: reflection transform with probe check, `ApplyTransform`, `Check3`, vertex check

**Build**
- Builders: `FormToolBuilder`, `TabAndSlotBuilder`, `SheetMetalGussetBuilder`, `SheetMetalFallbackBuilder`, `CoveredByFallbackBuilder` (returns Warning without a feature, `HasEquivalent`)
- `FeatureEdit`: `TabAndSlot`, `SMGusset`, `SheetMetal`
- `MirrorJob`: plan application, restart loop, generalized handling of positioned features

## 7. Core tests (about 45)

- **FallbackPlannerTests**
  - a vent in the middle covers the prefix and keeps the tail parametric
  - a build failure picks the next snapshot ≥ index
  - feature-only selection gives no plan
  - a body fed by cross-body features is excluded
  - sketch coverage only when all consumers are covered
  - the fallback record sits at the first covered position
  - other bodies are untouched
- **FormToolPlacementTests**
  - `θ′ = β − θ − 2α` against random frames (port of `rotcheck.py`)
  - α=0 gives −θ; α=π/2 gives π−θ
  - target sketch x reversed (β=π)
  - registration finds ψ for an L-shaped synthetic tool and returns no result for a chiral one
  - a rotationally symmetric tool gives several ψ, and the smallest is taken
  - `ProtrusionSide`
  - partitioning samples by instance
- **TabSlotRulesTests:** D1/D2 swap only without references and with a reversed edge; `EdgeLocation` reversal detection
- **GussetDirectionTests:** `FlipOffset` follows σ of the reference line (in the style of `DraftDirectionTests`)
- **ReflectionArrayTests:**
  - direct array det −1
  - `FromSwArray(negativeScale) ≈ r.ToAffine()`
  - `PointSets.Unmatched`
- **SheetMetalArgsTests:**
  - `InsertBends2` arguments (K or −1, allowance or −1, table name)
  - OffsetRatio clamped to [0.05, 2]
  - relief and overlap mapping
- **ReportText tests:** fallback summary and covered-entry texts

## 8. Live-test assumptions

1. GetTypeName2 of tab and slot (tab feature, slot feature; does the slot feature answer the data interface?) and of a vent.
2. `FormToolInstance.GetDefinition()` returns null in 2019–2024 and `ILibraryFormToolFeatureData` in 2025 SP2+; the units of `RotationAngle`.
3. Sub-features: 1st `ProfileFeature` = position sketch with user points; 2nd = profile sketch. Its origin is the insertion point (0xFFD); whether its axes carry the rotation (H-FT1).
4. Where `InsertFormToolFeature` puts the tool (at the pick point or at the sketch origin), and whether `BuildInto` (variant A) leaves the feature valid.
5. Rotation reference (placement sketch x-axis) and rotation sense; flip semantics.
6. `ListExternalFileReferences2` lists the tool path and configuration for linked and for unlinked tools.
7. Whether the rotation angle is exposed as a display dimension (that would allow cheap toggles in 2019).
8. `ApplyTransform` accepts det −1 on a `Copy2` temporary body; which array form works; the result is valid with positive volume.
9. Temporary body from the source is usable in the target after the rollback is restored, and after the source document is closed (assembly context).
10. `CreateFeatureFromBody3` in a part with solids creates a separate "Imported" body with no external references.
11. `InsertConvertToSheetMetal2` with FindBends=true on an already-bent body: are bends collected? Do the fixed-face marks matter? Return value false even on success.
12. `InsertBends2` needs a preselected fixed face (mark 0?).
13. K-factor, bend allowance and gauge table can be set afterwards on the new `SheetMetal` feature.
14. Convert or Insert Bends on the imported body in the same part while the original sheet-metal body exists.
15. Gusset v1 legs at mark 0; v2 arrays; `ReferenceLine`/`ReferencePoint` settable after creation.
16. `CreateDefinition(88)` returns `ITabAndSlotFeatureData` in 2019; groups can be modified after creation.
17. Body names stay stable between snapshot and record mapping; `IsSheetMetal()` is correct at the snapshot state.
18. `CreateFormTool2` marks: 1/2 (help) or 0/1 (0xFFD). Only needed for `MirrorTool`.
19. Numeric value of `swFmSolidToSheetMetal` (2025 SP2).

## 9. Effort estimate

| Item | Count / size |
|---|---|
| Records | 5 new: FormTool, TabAndSlot, SMGusset, SheetMetalFallback, CoveredByFallback |
| Value types | 2 new: SheetMetalParameters, TabSlotGroup |
| Base refactor | `PositionedFeatureRecord` |
| Builders | 5 (+ optional tool-part mirroring in phase 2) |
| Capture partials | 3 |
| Sw helpers | 3 |
| FeatureEdit editors | 3 |
| Core planning files | 4: FallbackPlanner, FormToolPlacement, TabSlotRules, ReflectionArrays/PointSets |
| Core tests | about 45 |
| Size | Core about 800 LOC, tests about 700, add-in about 2,200–2,600 |
| Time | 12–16 developer days, plus 2–3 days of live testing |
| Phase-2 `MirrorTool` | +3–4 days |

Suggested order:
1. Fallback: it is the safety net for areas 1–4, and parametric is still tried first at runtime.
2. Forming tools.
3. Gusset and tab and slot.
4. Optionally, mirrored tool parts.

Sources:
- [streetartist/solidworks-automation – SOLIDWORKS API help mirror (2025)](https://github.com/streetartist/solidworks-automation)
- [tbcphutai-bit/ADDIN-SolidWorks – NativePartMirrorV7.cs / ReflectionApiTransformV7.cs](https://github.com/tbcphutai-bit/ADDIN-SolidWorks)
- [mcneds/BeamNG.cad – tools/RearHubRepair.cs](https://github.com/mcneds/BeamNG.cad)
- [luigidoria/CADapter – solidworks/sw_sheetmetal.py](https://github.com/luigidoria/CADapter)
- [0xFFD/solidworks-mcp – tools_sheetmetal.py](https://github.com/0xFFD/solidworks-mcp)
- [xarial/xcad – SwDumbBody.cs](https://github.com/xarial/xcad)
- [f1owkang/solidworks-plugin-dev – official API examples](https://github.com/f1owkang/solidworks-plugin-dev)
- [angelsix/solidworks-api (SolidDNA)](https://github.com/angelsix/solidworks-api), [weianweigan/SolidWorksLookup](https://github.com/weianweigan/SolidWorksLookup)
- [help.solidworks.com – Create Forming Tool Feature Example (C#), 2025](https://help.solidworks.com/2025/English/api/sldworksapi/Create_Forming_Tool_Feature_Example_CSharp.htm) (seen in search results only; the host is blocked from this sandbox)