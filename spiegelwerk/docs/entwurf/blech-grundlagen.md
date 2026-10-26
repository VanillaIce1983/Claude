# Spiegelwerk sheet metal, area 1 (foundation): design for SheetMetal parameters, base flange/tab, flat pattern, sheet-metal cuts and legacy bend features

## 0. Summary

- **Base flange: three ways to create it.** All of them exist in SOLIDWORKS 2019 (interop 27.3):
  - `IFeatureManager.InsertSheetMetalBaseFlange2` (19 parameters)
  - `IFeatureManager.InsertSheetMetalBaseFlange` (16 parameters)
  - `CreateDefinition(swFmBaseFlange = 34)` followed by `CreateFeature`. The 2019 interop has `swFmBaseFlange`, but `IBaseFlangeFeatureData.Initialize` only appears in a later interop (present in 32.1). The official gauge-table example from 2015 uses `CreateDefinition` + `CreateFeature` without `Initialize`.
  - Recommended order: try `Flange2`, then `Flange`, then the data object. After creation, everything is checked by reading it back and corrected in one `ModifyDefinition`.
- **Three features appear at once.** A new base flange creates `SheetMetal` + `SMBaseFlange` + `FlatPattern` (and in a part made in 2013 or later, a `TemplateSheetMetal` folder). This was confirmed live by a third party.
  - The SheetMetal feature is therefore treated like the sketches of a hole: a `PartOfFeatureRecord` whose owner is the base flange. Its parameters are stored inside the `BaseFlangeRecord`.
  - The flat pattern gets its own record. It is built last and edits the flat pattern that the base flange created.
- **Thickness side (the key σ rule).**
  - Closed profile or tab: the thickness goes along the sketch normal, and `IBaseFlangeFeatureData.ReverseDirection` controls it. A third party verified live that `ThickenDir` and `ReverseThickness` have no effect here. Rule: `DirectionRules.ReverseFlag(rev, σ)`, the same as the extrude flag.
  - Open profile: the depth flag (`ReverseDirection`) follows the same σ rule. The material side of the contour (`ReverseThickness`) lies in the sketch plane, so it obeys the opposite rule: flip it when σ = +1, keep it when σ = −1. That matches the in-plane geometry (n′×t′ = −σ·R(n×t)). Both flags are also offered to the verifier as alternatives.
- **Flat pattern.** The fixed face maps to the mirrored face by fingerprint. Bend allowance, K-factor, bend table, gauge table and reliefs stay unchanged (justified in §6). Grain direction cannot be transferred: there is no API member in 27.3, 32.1 or the current help.
- **New check: the flattened state.** `MassCapture` measures the original's (flat − folded) mass change. `FlatPatternBuilder` compares the mirrored value against the copy's flattened state. This is the only way to verify K-factor, bend allowance and the fixed face, because the folded mass does not depend on them.

## 1. Sources and how far each is verified

- **Interop reflection.** I ran a reflection probe on 27.3 (= SOLIDWORKS 2019) and 32.1. Every signature below comes from it, and every signature is verified. Probe: /tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/smprobe/Program.cs (`dotnet out/smprobe.dll <27|32> <types|members|enum|find> <regex>`). Dumps: …/smprobe/members_27.txt, members_32.txt, find_27.txt, find_32.txt, fmid_27.txt, fmid_32.txt.
- **API help.** help.solidworks.com was not reachable. I used a GitHub mirror of the current help (streetartist/solidworks-automation, raw files), saved in /tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/gh/help. It covers parameter text, remarks, the `CreateDefinition` type list, the `GetTypeName2` table and the member lists. Official examples are in …/gh/ex.
- **Third-party live findings** (marked "3P-live" below; each comes from someone else's live SOLIDWORKS session, not ours):
  - soheil-mp/solidworks-mcp `docs/solidworks-api-notes.md`, statements marked VERIFIED there (version 2025/26, most likely).
  - luigidoria/CADapter `solidworks/docs/SHEET_METAL.md` and `sw_sheetmetal.py` (live on SOLIDWORKS 2017 and 2026).
  - ladla90077-web/solidworks-mcp `LESSONS.md`.
  - gokworks (K/R tool): its own notes say "not verified in real SOLIDWORKS".
- **Not verified anywhere:** everything listed in §13.

## 2. Feature types and data interfaces

| `GetTypeName2` | Feature | `GetDefinition` → | 2019 interop | Handling in area 1 |
|---|---|---|---|---|
| `SMBaseFlange` | Base flange / tab | `IBaseFlangeFeatureData` | yes | `BaseFlangeRecord` + `BaseFlangeBuilder` |
| `SheetMetal` | Sheet-Metal*n* (body level, or legacy) | `ISheetMetalFeatureData` | yes | `PartOfFeatureRecord` (owner = base flange / convert); legacy → `LegacyBendsRecord` |
| `TemplateSheetMetal` | "Sheet-Metal" folder, part defaults (2013+) | the table says `ISheetMetalFolder`; the official example casts `GetDefinition` to `SheetMetalFeatureData` | yes (`GetTemplateSheetMetal`) | Not collected. Its defaults are read into `BaseFlangeRecord.Defaults`. Never deleted. |
| `FlatPattern` | Flat-Pattern*n* | `IFlatPatternFeatureData` | yes | `FlatPatternRecord` + `FlatPatternBuilder` |
| `TemplateFlatPattern` | "Flat-Pattern" folder | `IFlatPatternFolder` | yes | System folder, but its FlatPattern sub-features must be listed (§9) |
| `Cut` / `CutThin` (also `ICE`) | Cut-extrude in sheet metal | `IExtrudeFeatureData2` (`LinkToThickness`, `NormalCut`, `OptimizeGeometry`) | yes | `ExtrudeRecord` + `LinkToThickness` |
| `NormalCut` | Normal Cut feature | `ISMNormalCutFeatureData2` | yes (`swFmNormalCut` = 115) | `NormalCutRecord` + `NormalCutBuilder` |
| `FlattenBends`, `ProcessBends` | Legacy Insert Bends | `IBendsFeatureData` | yes | `PartOfFeatureRecord` (owner = legacy SheetMetal) |
| `SolidToSheetMetal` | Convert-Solid | `IConvertSolidFeatureData` | **no** (absent in 27.3 and 32.1; help example is 2025) | `ConvertSolidRecord`, read late-bound; otherwise the fallback |
| `OneBend`, `SketchBend` | Bend sub-features | `IOneBendFeatureData` | yes | Area 2 (never top-level) |

Enums verified in 27.3:
- `swBendAllowanceTypes_e`: BendTable 1, KFactor 2, Direct 3, Deduction 4, BendCalculationTable 5.
- `swSheetMetalReliefTypes_e`: Rectangular 1, Tear 2, Obround 3, None 4, TearBend 5.
- `swFlangeOffsetTypes_e`: Blind 1, UpToVertex 2, UpToSurface 3, FromSurface 4, MidPlane 5.
- `swSheetMetalOverrideDefaultParameters_e`: BendParameters 0, BendAllowance 1, AutoRelief 2.
- `swSMBendState_e`: None 0, Sharps 1, Flattened 2, Folded 3.
- `swNormalCutParameters_e`: Extent 0, OffsetPlane 1.
- `swFeatureSuppressionAction_e`: Suppress 0, Unsuppress 1.
- `swInConfigurationOpts_e.swThisConfiguration` = 1.
- `swFeatureNameID_e` (2019): SheetMetal 30, FlattenBends 31, ProcessBends 32, BaseFlange 34, FlatPattern 38, NormalCut 115. **`swFmSolidToSheetMetal` is not in 27.3 or 32.1.**

## 3. Exact API surface in 27.3 (SOLIDWORKS 2019)

### Creation

```text
IFeatureManager.Feature InsertSheetMetalBaseFlange2(double Thickness, bool ThickenDir, double Radius,
    double ExtrudeDist1, double ExtrudeDist2, bool FlipExtruDir, int EndCondition1, int EndCondition2, int DirToUse,
    CustomBendAllowance PCBA, bool UseDefaultRelief, int ReliefType, double ReliefWidth, double ReliefDepth,
    double ReliefRatio, bool UseReliefRatio, bool Merge, bool UseFeatScope, bool UseAutoSelect)
IFeatureManager.Feature InsertSheetMetalBaseFlange(<same first 16 parameters>)            // no Merge/scope
IFeatureManager.object  CreateDefinition(int Type)  +  IFeatureManager.Feature CreateFeature(object FeatureData)
IFeatureManager.CustomBendAllowance CreateCustomBendAllowance()
IFeatureManager.bool InsertConvertToSheetMetal2(double Thickness, bool ReverseThickDir, bool FindBends, double Radius,
    double Gap, int ReliefType, double ReliefRatio, int OverlapType, double OverlapRatio, bool KeepBody)
IPartDoc.bool InsertBends2(double Radius, string UseBendTable, double UseKfactor, double UseBendAllowance,
    bool UseAutoRelief, double OffsetRatio, bool DoFlatten)
IFeatureManager.Feature FeatureCut4(… bool NormalCut (18th), bool UseFeatScope, bool UseAutoSelect, …, bool OptimizeGeometry)
```

What the parameters mean (from the help mirror):
- `Thickness` is in metres. `ThickenDir` is the thicken direction. `Radius` is the global bend radius.
- `ExtrudeDist1/2` are the depths for open profiles. `FlipExtruDir` reverses the extrusion.
- `EndCondition1/2` take `swEndConditions_e` values (Blind 0, MidPlane 6).
- `DirToUse` is documented as "End condition type as defined in swEndConditions_e". That text is nonsensical:
  - Every known working call passes 1: the official example, as reported by soheil-mp and ladla.
  - ladla records that 0 makes `Flange2` fail.
  - CADapter passes 0 to the 16-parameter v1 and it works there.
- `PCBA` is the custom bend allowance object, or `null` for the defaults. `ReliefType` takes `swSheetMetalReliefTypes_e`.
- `Merge` means "connect bodies into a single sheet metal body".
- When `UseAutoSelect` = false, the scope bodies are selected with **mark 8** (help remark).
- The profile sketch is selected with **mark 0**: official example `SelectByID2("Sketch1","SKETCH",…,0)` and soheil-mp `Select2(false,0)`.

**The help marks `InsertSheetMetalBaseFlange2` obsolete in favour of `CreateDefinition`/`CreateFeature`.** The live evidence conflicts:
- CADapter (SOLIDWORKS 2017): the 19-parameter v2 is a "silent no-op", while the 16-parameter v1 builds.
- soheil-mp (newer version): v2 builds with `(t,false,r,0.02,0.01,false,0,0,1,null,false,0,1e-4,1e-4,0.5,true,false,true,true)`.

### Data interfaces in 27.3

`IBaseFlangeFeatureData`:
- Properties: `Thickness`, `ReverseThickness`, `BendRadius`, `ReverseDirection`, `OffsetDirections` (1 or 2), `D1OffsetType`/`D2OffsetType` (`swFlangeOffsetTypes_e`), `D1OffsetDistance`/`D2OffsetDistance`, `D1OffsetReference`/`D2OffsetReference`, `D1ReverseOffset`/`D2ReverseOffset`.
- Gauge table: `UseGaugeTable`, `GaugeTablePath`, `ThicknessTableName` (the gauge name, e.g. "Gauge 3"), `TableThickness`, `TableRadius`, `OverrideThickness`, `OverrideRadius`, `KFactor`, `OverrideKFactor`, `TableKFactor` (get only).
- Methods: `GetD1OffsetReferenceType()`, `GetD2OffsetReferenceType()`, `GetTableThicknessesCount()`, `GetTableThicknesses()`, `GetTableRadiiCount(string)`, `GetTableRadii(string)`, `AccessSelections`, `ReleaseSelectionAccess`.
- **2019 has no** `Merge`, no relief getters, no `GetCustomBendAllowance`, no `Initialize`.
- 32.1 adds: `Initialize(bool UseMaterialSheetMetalParameters, bool OverrideDefaultBendAllowance, object CustomBendAllowance, bool OverrideDefaultBendRelief, int ReliefType, bool UseReliefRatio, double ReliefRatio, double ReliefWidth, double ReliefDepth)`, `GetCustomBendAllowance()`, `D1/D2EndConditionType/Distance/Reference`, `SymmetricThickness`, `OverrideDefaultSheetMetalParameters`, and get-only `UseMaterialSheetMetalParameters`, `UseDefaultBendAllowance`, `UseDefaultBendRelief`, `ReliefType`, `UseReliefRatio`, `ReliefRatio`, `ReliefWidth`, `ReliefDepth`.
- Help remark: the `ReverseThickness` setter is valid only when `OverrideDefaultSheetMetalParameters` = true and `UseMaterialSheetMetalParameters` = false.

`ISheetMetalFeatureData`:
- Properties: `Thickness`, `BendRadius`, `FixedReference` (face or edge), `UseAutoRelief`, `AutoReliefType`, `ReliefRatio`, plus the obsolete `BendAllowanceType`/`KFactor`/`BendAllowance`/`BendTableFile`.
- Methods: `GetCustomBendAllowance()`/`SetCustomBendAllowance(CustomBendAllowance)`, `int GetOverrideDefaultParameter2(int, out bool)`/`int SetOverrideDefaultParameter2(int, bool)` (multibody, 2013+), `int GetUseGaugeTable(out bool, out string)`/`int SetUseGaugeTable(bool, string)`.
- 32.1 adds `UseMaterialSheetMetalParameters`.

`ICustomBendAllowance` (27.3 = 32.1): `Type`, `KFactor`, `BendAllowance`, `BendDeduction`, `BendTableFile`. Help: **set `Type` last**. `BendCalculationTableFile` exists only after 32.1, so it is read late-bound.

`IFlatPatternFeatureData` (unchanged in 32.1):
- Properties: `FixedFace2` (a face, or an edge for cylindrical parts; obsolete `FixedFace`), `MergeFace`, `SimplifyBends`, `CornerTreatment`, `CornerTrimReliefType`, `CornerTrimReliefDistance`, `CornerTrimRatioToThickness`, `UseRatioToThickness`, `BreakCornerType`, `BreakCornerRadius`, `ShowSlitInCornerRelief`, `ExcludedFaces`.
- Methods: `bool GetAddCornerTrim(out Feature)`/`SetAddCornerTrim(bool)`, `bool GetBreakCorners(out Feature)`/`SetBreakCorners(bool)`.
- **No grain direction.**

`IBendsFeatureData`: `object GetFixedFace()`, `BendRadius`, `BendAllowanceType`, `BendTableFile`, `KFactor`, `BendAllowance`, `UseDefaultBendRadius`, `UseDefaultBendAllowance`, `Get/SetCustomBendAllowance`.

`ISMNormalCutFeatureData2`: `GetGroupNames()`, `object CreateGroup(out int)`, `GetGroupByName(string)`, `DeleteGroupByName(string)`, `AutoPropagation`, `OptimizeGeometry`, `NormalCutParameters`, `OffsetPlaneReference`, `CutDirection`, `LinkToKFactor`, `LayerAdjustmentValue`. `ISMNormalCutGroupData`: `GetGroupName()`, `Faces`. Marks (help): faces 1, offset plane 4, direction 8, then `CreateGroup` per group, then `CreateFeature`.

`IExtrudeFeatureData2`: `LinkToThickness`, `NormalCut`, `OptimizeGeometry`.

Other members used:
- `IModelDocExtension.object GetTemplateSheetMetal()` (null for parts made in 2012 or earlier)
- `IFeatureManager.GetSheetMetalFolder()` → `ISheetMetalFolder.GetSheetMetals()`/`GetFeature()`
- `IFeatureManager.GetFlatPatternFolder()` → `IFlatPatternFolder.GetFlatPatterns()`
- `IBody2.IsSheetMetal()`, `ISketchContour.IsClosed()`
- `IFeature.bool SetSuppression2(int, int, object)`, `IFeature.object IsSuppressed2(int, object)`
- `IModelDoc2.SetBendState` (help: legacy and converted parts only; new style means suppressing or unsuppressing the flat pattern; CADapter found it a no-op)

## 4. How to create a base flange: the options

| Path | 2019 | Gauge table at creation | Merge / scope | Evidence | Role |
|---|---|---|---|---|---|
| B: `InsertSheetMetalBaseFlange2` | yes | no (set afterwards) | yes (`Merge`, scope bodies on mark 8) | soheil-mp, ladla: works. CADapter 2017: no-op. | **Try first** |
| C: `InsertSheetMetalBaseFlange` (16 parameters) | yes | no | no | CADapter 2017: works | Second, only if `Merge` does not matter (first body, or New Part) |
| A: `CreateDefinition(34)` → `IBaseFlangeFeatureData` → (late-bound `Initialize` if it exists) → sketch selected on mark 0 → `CreateFeature` | data object yes, `Initialize` no | yes | no merge property | Official examples: gauge example (2015) without `Initialize`, IBaseFlangeFeatureData example (2022) with `Initialize` | Third. Help: "you must initialize" in newer versions. |

**After creation, every path is followed by one `FeatureEdit.BaseFlange`** (`AccessSelections` → set → `ModifyDefinition`). It sets: `OffsetDirections`; `D1/D2OffsetType`, `Distance`, `ReverseOffset`; references (`D1/D2OffsetReference`); the gauge table (`UseGaugeTable` → `GaugeTablePath` → `ThicknessTableName` → `TableRadius` → `Override*`, in exactly the order of the official example); `Thickness`/`BendRadius` (when there is no gauge table, or with an override); `SymmetricThickness` (late-bound).

Then the definition is read back and compared. CADapter found that `ModifyDefinition` can return true without changing anything.

## 5. How SOLIDWORKS structures a sheet-metal part, and what follows for Spiegelwerk

1. **Part defaults versus body values.** From 2013 on, the part defaults live on the `TemplateSheetMetal` feature (`GetTemplateSheetMetal().GetDefinition()` → `ISheetMetalFeatureData`). The body-level `SheetMetal` feature only overrides them through `SetOverrideDefaultParameter2(0|1|2, true)`.
   - This explains CADapter's findings: bend radius takes effect on the body only after an override, and the K-factor "never" changes on the body without one.
   - It also explains "thickness belongs to the document": a second base flange at 3 mm thickened the first.
   - → Capture both levels. In a New Part, apply the defaults to the template. Apply overrides only where the original had them.
   - → **In Same Part, never change the template**: it is shared with the original.
2. **One step creates SheetMetal + SMBaseFlange + FlatPattern** (3P-live). The flat pattern stays last in the tree and later features are inserted before it (3P-live).
   - → The `BaseFlangeBuilder` finds the new SheetMetal and FlatPattern features by comparing the lists before and after, including folder contents. It records them in `ctx.SheetMetalBodies`.
   - → `FlatPatternBuilder` edits that flat pattern instead of creating one.
3. **The flat pattern is suppressed in the folded state.** `FeatureCollector.SkipReason` currently skips every suppressed feature; it would skip `FlatPattern` and its sub-sketches, and "Original löschen" (delete original) would be blocked. → An exception is needed: the suppression is recorded as a state.
4. **In a multibody part, SheetMetal and FlatPattern features sit inside the `TemplateSheetMetal`/`TemplateFlatPattern` folders** (live test L12). `FeaturesInCreationOrder` only lists the top level plus absorbed sketches. → Flat patterns inside the folder must be listed as well.
5. **`TemplateSheetMetal` and `TemplateFlatPattern` must never be collected or deleted.** Today `TemplateSheetMetal` would end up as an `UnsupportedRecord` with `ChangesSolid` = true, reported as Failed. `DeleteOriginals` must never delete it.

## 6. Mirror rules with justification

σ = `SketchMap.Sigma` of the rebuilt profile sketch (as in `ExtrudeBuilder`).

| Quantity | Rule | Justification |
|---|---|---|
| **Closed profile / tab: thickness side** (`ReverseDirection`; `ThickenDir`/`ReverseThickness` copied along) | `DirectionRules.ReverseFlag(r.ReverseDirection, σ)`: flip when σ = −1 | The plate grows along ±n of the sketch, exactly like a blind boss. 3P-live: `ReverseDirection` moves the plate from y −2..0 to 0..2; `ThickenDir` and `ReverseThickness` do nothing on a closed profile. Example: sketch on *Right*, mirrored at *Right*. The copy reuses *Right* (σ = −1), and the plate must be on the other side of the plane. |
| **Open profile: depth** (`ReverseDirection`; `D1`/`D2`, `OffsetDirections`) | `ReverseFlag(·, σ)`; distances and types unchanged | Like the extrude reverse flag. "Reverse" flips D1 and D2 as a pair, so swapped depths are covered. |
| **Open profile: side of the contour** (`ReverseThickness`, creation `ThickenDir`) | First guess: `σ > 0 ? !rev : rev` (new: `DirectionRules.InPlaneSideFlag`). Plus a toggle as alternative. | "Left of the contour" = n × t. `SketchBuilder` keeps start → end of every segment, so t′ = R(t), and n′ = σR(n). Then n′×t′ = σ·(R(n)×R(t)) = −σ·R(n×t). With σ = +1 (a reflection in the plane) left and right swap; with σ = −1 (a rotation) they stay. **Assumption:** SOLIDWORKS defines the side relative to the sketch normal, not relative to the extrusion direction. Under the other model the flag would always flip. That difference only shows when σ = −1 (L3). |
| `SymmetricThickness` (newer versions) | unchanged | No side |
| `D1/D2ReverseOffset` | unchanged | Relative to the reference face, whose outward normal stays outward (as `ExtrudeSide.OffsetReverse`) |
| `D1/D2OffsetReference` | `GeometryRef.Mirror` → `ctx.Topology.Resolve`; when not found, use the equivalent blind depth | As `ExtrudeBuilder.ResolveEnd` / `BlindEquivalent` |
| Thickness, bend radius, gauge table/name, K-factor, BA, BD, bend table, bend calculation table, relief type/ratio/width/depth, auto relief | **unchanged** | A reflection keeps all lengths and angles. Every bend of the copy has the same t, r and angle, and allowance tables are indexed only by (t, r, angle, material). So the same allowance gives R(flat). Reliefs are defined symmetrically about the bend. |
| `Merge` | unchanged. Same Part: only with this job's own bodies (scope on mark 8). The first mirrored body never merges. | As `PlanSeparateBody` |
| Flat pattern `FixedFace2` (face or edge) | Mirrored `FaceSignature`/`EdgeSignature`, found **only as a face or edge**, never as a stand-in plane | Unfolding is defined by intrinsic geometry, the fixed face and the allowance: Flat(R(P), R(F)) = R(Flat(P, F)). Outward normals stay outward, so there is no σ. |
| `ExcludedFaces` | mirrored | as above |
| `MergeFace`, `SimplifyBends`, corner trim/break options, `ShowSlitInCornerRelief`, add-corner-trim, break corners | unchanged | Not orientation-dependent |
| Grain direction | Would be the mirrored **unoriented** line (no sign question). **No API member in 27.3, 32.1 or the current help**: cannot be read or set. | Report line, see L14 |
| Flat pattern suppression (flattened or folded, active configuration) | unchanged | State, not geometry. A derived configuration "…SM-FLAT-PATTERN" is not recreated; SOLIDWORKS makes it when needed (note in the report). |
| Cut `LinkToThickness`, `NormalCut`, `OptimizeGeometry` | unchanged; direction as in the extrude rule | |
| Normal Cut feature: face groups, `CutDirection`, `OffsetPlaneReference` | mirrored references; `AutoPropagation`, `LinkToKFactor`, `LayerAdjustmentValue` unchanged | |
| Convert-Solid `ReverseThickness` | unchanged (toggle as alternative) | Relative to the fixed face: inward or outward from the face, like `ShellRecord.Direction` |
| Convert bend edges, fixed face, rip sketches | mirrored | |
| Rip overlap/underlap | unchanged first; toggle 2↔3 as alternative (L18) | Which flange overlaps may depend on edge orientation |

### How the `ExtrudeBuilder` thin-feature rules carry over

`ExtrudeBuilder` uses these rules for thin features:
- `Reverse` = `ReverseFlag(·, σ)`.
- `ThinType` is copied unchanged.
- Alternatives: "Richtung umgekehrt" (direction reversed), "Wandseite gewechselt" (One ↔ Opp), "Wandstärken getauscht" (thicknesses swapped, TwoDirection only), "Materialseite gewechselt" (material side, cuts).

They map to the base flange as follows:
- **Extrude `Reverse`** → `ReverseDirection`, with the same rule, for both closed profiles (thickness) and open profiles (depth).
- **Thin wall side (`ThinType` One/Opp)** → `ReverseThickness` for open profiles. Here I propose the derived in-plane rule as first guess instead of copying blindly, and keep the toggle.
  - If live test L3 confirms model (a), the same rule should also be applied to `ExtrudeBuilder`/`RevolveBuilder` `ThinType` (optional; it saves verifier rounds).
- **`swThinWallMidPlane`** → `SymmetricThickness` (no side).
- **"Wandstärken getauscht"** has no counterpart: a base flange has a single thickness.

## 7. Records (`Spiegelwerk.Core/Records/SheetMetalRecords.cs`)

```csharp
namespace Spiegelwerk.Core.Records
{
    /// <summary>Same numbers as swBendAllowanceTypes_e.</summary>
    public enum BendAllowanceKind { BendTable = 1, KFactor = 2, Direct = 3, Deduction = 4, BendCalculationTable = 5 }

    /// <summary>Same numbers as swSheetMetalReliefTypes_e.</summary>
    public enum ReliefKind { Rectangular = 1, Tear = 2, Obround = 3, None = 4, TearBend = 5 }

    /// <summary>ICustomBendAllowance as data. Carried over unchanged: a mirror keeps t, r and angle of every bend.</summary>
    public sealed class BendAllowanceData
    {
        public BendAllowanceKind Kind { get; set; } = BendAllowanceKind.KFactor;
        public double KFactor { get; set; } = 0.5;
        public double BendAllowance { get; set; }          // m
        public double BendDeduction { get; set; }          // m
        public string BendTableFile { get; set; } = "";
        public string BendCalculationTableFile { get; set; } = "";   // newer API only (late-bound)
    }

    public sealed class ReliefData
    {
        public bool UseAutoRelief { get; set; } = true;
        public ReliefKind Kind { get; set; } = ReliefKind.Rectangular;
        public bool UseRatio { get; set; } = true;
        public double Ratio { get; set; } = 0.5;
        public double Width { get; set; }                  // only readable in 2022+ (base flange getters)
        public double Depth { get; set; }
    }

    public sealed class GaugeData
    {
        public string TablePath { get; set; } = "";
        public string ThicknessName { get; set; } = "";    // IBaseFlangeFeatureData.ThicknessTableName ("Gauge 3")
        public double TableRadius { get; set; }
        public bool OverrideThickness { get; set; }
        public bool OverrideRadius { get; set; }
        public bool OverrideKFactor { get; set; }
        public double KFactor { get; set; }
    }

    /// <summary>ISheetMetalFeatureData: part defaults (TemplateSheetMetal) or one body (SheetMetal) with its overrides.</summary>
    public sealed class SheetMetalParameters
    {
        public double Thickness { get; set; }
        public double BendRadius { get; set; }
        public BendAllowanceData BendAllowance { get; set; } = new BendAllowanceData();
        public ReliefData Relief { get; set; } = new ReliefData();
        public bool UseGaugeTable { get; set; }
        public string GaugeTableFile { get; set; } = "";
        public bool? UseMaterialParameters { get; set; }   // late-bound, null = not available
        // body level only (GetOverrideDefaultParameter2 0/1/2)
        public bool OverrideBendParameters { get; set; }
        public bool OverrideBendAllowance { get; set; }
        public bool OverrideAutoRelief { get; set; }
        /// <summary>ISheetMetalFeatureData.FixedReference (face or edge), legacy/converted parts.</summary>
        public GeometryRef? FixedReference { get; set; }
    }

    /// <summary>One direction of an open-profile base flange; End uses swEndConditions_e numbers (mapped from swFlangeOffsetTypes_e).</summary>
    public sealed class FlangeSide
    {
        public EndCondition End { get; set; } = EndCondition.Blind;
        public double Distance { get; set; }
        public bool ReverseOffset { get; set; }
        public GeometryRef? Reference { get; set; }
    }

    /// <summary>
    /// Base flange / tab. Brings its SheetMetal feature (and the part's template and flat pattern) with it:
    /// those are PartOf records owned by this one. Side rules: see SheetMetalRules.BaseFlangeFlags.
    /// </summary>
    public sealed class BaseFlangeRecord : FeatureRecord
    {
        public BaseFlangeRecord(string name, string typeName, string sketchName) : base(name, typeName) => SketchName = sketchName;
        public string SketchName { get; }
        public bool ClosedProfile { get; set; }
        public double Thickness { get; set; }
        public bool ReverseThickness { get; set; }
        public bool ReverseDirection { get; set; }
        public bool SymmetricThickness { get; set; }
        public double BendRadius { get; set; }
        public int OffsetDirections { get; set; } = 1;
        public FlangeSide Side1 { get; } = new FlangeSide();
        public FlangeSide Side2 { get; } = new FlangeSide();
        public GaugeData? Gauge { get; set; }
        /// <summary>2022+: the flange's own allowance/relief when it does not use the default; null otherwise.</summary>
        public BendAllowanceData? OwnBendAllowance { get; set; }
        public ReliefData? OwnRelief { get; set; }
        /// <summary>Joined an existing sheet-metal body (tab); measured geometrically at capture.</summary>
        public bool MergesIntoExisting { get; set; }
        public string SheetMetalName { get; set; } = "";
        public SheetMetalParameters? Body { get; set; }
        /// <summary>TemplateSheetMetal (2013+); null for older parts.</summary>
        public SheetMetalParameters? Defaults { get; set; }
        public override bool BringsParts => true;          // new virtual on FeatureRecord, also true for PositionedHoleRecord
    }

    public sealed class FlatPatternRecord : FeatureRecord
    {
        public FlatPatternRecord(string name, string typeName, string owner) : base(name, typeName) => Owner = owner;
        /// <summary>Base flange, convert or legacy SheetMetal that made the body.</summary>
        public string Owner { get; }
        public GeometryRef? FixedFace { get; set; }        // Face or Edge
        public bool MergeFaces { get; set; }
        public bool SimplifyBends { get; set; }
        public bool CornerTreatment { get; set; }
        public int CornerTrimReliefType { get; set; }
        public double CornerTrimReliefDistance { get; set; }
        public double CornerTrimRatioToThickness { get; set; }
        public bool UseRatioToThickness { get; set; }
        public int BreakCornerType { get; set; }
        public double BreakCornerRadius { get; set; }
        public bool AddCornerTrim { get; set; }
        public bool BreakCorners { get; set; }
        public bool ShowSlitInCornerRelief { get; set; }
        public List<FaceSignature> ExcludedFaces { get; } = new List<FaceSignature>();
        /// <summary>Unsuppressed (flattened) in the active configuration of the original.</summary>
        public bool Flattened { get; set; }
        /// <summary>Flattened minus folded, unmirrored: verifies fixed face, radius and allowance at once.</summary>
        public MassState? FlatChange { get; set; }
        public List<Vec3> FlatSamples { get; } = new List<Vec3>();
        public override bool ChangesSolid => false;
    }

    /// <summary>Legacy Insert Bends: SheetMetal + FlattenBends + ProcessBends, anchored on the SheetMetal feature.</summary>
    public sealed class LegacyBendsRecord : FeatureRecord
    {
        public LegacyBendsRecord(string name, string typeName) : base(name, typeName) { }
        public double BendRadius { get; set; }
        public BendAllowanceData BendAllowance { get; set; } = new BendAllowanceData();
        public bool UseAutoRelief { get; set; }
        public double OffsetRatio { get; set; } = 0.5;     // 0.05..2.0 (help)
        public GeometryRef? FixedFace { get; set; }        // IBendsFeatureData.GetFixedFace / SheetMetal.FixedReference
        public string? FlattenBendsName { get; set; }
        public string? ProcessBendsName { get; set; }
        public bool ProcessBendsSuppressed { get; set; }
        public override string MeasureUntil => ProcessBendsName ?? Name;   // new virtual, used by MassCapture
        public override bool BringsParts => true;
    }

    public sealed class ConvertSolidRecord : FeatureRecord   // readable only via late binding (2025+)
    {
        public ConvertSolidRecord(string name, string typeName) : base(name, typeName) { }
        public double Thickness { get; set; }
        public bool ReverseThickness { get; set; }
        public double BendRadius { get; set; }
        public bool KeepBody { get; set; }
        public double RipGap { get; set; }
        public int RipOverlapType { get; set; } = 1;       // 1 open butt, 2 overlap, 3 underlap
        public double RipOverlapRatio { get; set; } = 0.5;
        public ReliefData Relief { get; set; } = new ReliefData();
        public BendAllowanceData BendAllowance { get; set; } = new BendAllowanceData();
        public GaugeData? Gauge { get; set; }
        public GeometryRef? FixedFace { get; set; }
        public List<EdgeSignature> BendEdges { get; } = new List<EdgeSignature>();   // state before the feature
        public List<string> RipSketches { get; } = new List<string>();
        public override bool BringsParts => true;
    }

    public sealed class NormalCutRecord : FeatureRecord
    {
        public NormalCutRecord(string name, string typeName) : base(name, typeName) { }
        public List<List<FaceSignature>> Groups { get; } = new List<List<FaceSignature>>();
        public bool AutoPropagation { get; set; }
        public bool OptimizeGeometry { get; set; }
        public int Parameters { get; set; }                // swNormalCutParameters_e
        public GeometryRef? OffsetPlane { get; set; }
        public GeometryRef? CutDirection { get; set; }
        public bool LinkToKFactor { get; set; }
        public double LayerAdjustment { get; set; }
    }
}
```

Changes to existing records:
- `ExtrudeRecord.LinkToThickness` (bool).
- `FeatureRecord`: `public virtual bool BringsParts => false;` and `public virtual string MeasureUntil => Name;`.
- `PositionedHoleRecord` overrides `BringsParts` → true.

Core planning, `Planning/SheetMetalRules.cs`:
- `bool InPlaneSideFlag(bool src, int σ) => σ > 0 ? !src : src` (or in `DirectionRules`)
- `(bool reverseDirection, bool reverseThickness) BaseFlangeFlags(bool closed, bool rd, bool rt, int σ)` (closed: rt unchanged)
- `EndCondition FromFlangeOffset(int)` / `int ToFlangeOffset(EndCondition)` (1↔0, 2↔3, 3↔4, 4↔5, 5↔6)
- `InsertBendsArgs? LegacyArguments(BendAllowanceData, double offsetRatio)`
- `IReadOnlyList<string> Differences(SheetMetalParameters want, SheetMetalParameters got)` (tolerances 1e-9 m, 1e-6 for K; table paths by full path, otherwise by file name with a note)

`Planning/SheetMetalTree.cs`: neutral ownership rules (input: name, type, parents, children, sub-features) → owner map + excluded names.

## 8. Capture outline (`Capture/FeatureCapture.SheetMetal.cs`)

```csharp
// In Capture(), before GetDefinition:
if (type == "TemplateSheetMetal" || type == "TemplateFlatPattern") return Skipped-by-collector (never reached)
if (SheetMetalPart(f, type) is PartOfFeatureRecord part) return part;   // SheetMetal→base flange/convert; FlattenBends/ProcessBends→legacy SM; FlatPattern sub-sketches→flat pattern
// switch (def):
case BaseFlangeFeatureData bf:      return CaptureBaseFlange(f, type, bf);
case FlatPatternFeatureData fp:     return CaptureFlatPattern(f, type, fp);
case SheetMetalFeatureData sm when IsLegacyAnchor(f): return CaptureLegacyBends(f, type, sm);
case SMNormalCutFeatureData2 nc:    return CaptureNormalCut(f, type, nc);
// type "SolidToSheetMetal": def is not an interop type before 2025 → CaptureConvertLateBound(f, def) or Unsupported("… Ersatzweg")

private BaseFlangeRecord CaptureBaseFlange(Feature f, string type, BaseFlangeFeatureData d)
{
    Feature sketch = SketchOf(f);
    var r = new BaseFlangeRecord(f.Name, type, sketch.Name)
    {
        ClosedProfile = ((object[])((Sketch)sketch.GetSpecificFeature2()).GetSketchContours() ?? new object[0])
                        .OfType<SketchContour>().All(c => c.IsClosed()),
    };
    if (!d.AccessSelections(_doc, null)) throw new NotSupportedException("… (AccessSelections).");
    try
    {
        r.Thickness = d.Thickness; r.ReverseThickness = d.ReverseThickness; r.ReverseDirection = d.ReverseDirection;
        r.BendRadius = d.BendRadius; r.OffsetDirections = d.OffsetDirections;
        ReadFlangeSide(d.D1OffsetType, d.D1OffsetDistance, d.D1ReverseOffset, d.D1OffsetReference, r.Side1);
        if (r.OffsetDirections == 2) ReadFlangeSide(d.D2OffsetType, d.D2OffsetDistance, d.D2ReverseOffset, d.D2OffsetReference, r.Side2);
        if (d.UseGaugeTable) r.Gauge = new GaugeData { TablePath = d.GaugeTablePath, ThicknessName = d.ThicknessTableName,
            TableRadius = d.TableRadius, OverrideThickness = d.OverrideThickness, OverrideRadius = d.OverrideRadius,
            OverrideKFactor = d.OverrideKFactor, KFactor = d.KFactor };
        r.SymmetricThickness = SwLate.Get(d, "SymmetricThickness") is bool s && s;
        if (SwLate.Get(d, "UseDefaultBendAllowance") is bool ub && !ub) r.OwnBendAllowance = SwSheetMetal.Read(SwLate.Get(d, "GetCustomBendAllowance")) // via Call
        if (SwLate.Get(d, "UseDefaultBendRelief") is bool ur && !ur) r.OwnRelief = SwSheetMetal.ReadReliefLate(d);
    }
    finally { d.ReleaseSelectionAccess(); }

    Feature? sm = SwSheetMetal.SheetMetalParentOf(f);      // parent with type "SheetMetal" (help example: GetParents()[1])
    r.SheetMetalName = sm?.Name ?? "";
    r.Body = sm == null ? null : SwSheetMetal.ReadParameters(_doc, sm, bodyLevel: true, ToGeometryRef);
    r.Defaults = _doc.Extension.GetTemplateSheetMetal() is Feature t ? SwSheetMetal.ReadParameters(_doc, t, false, ToGeometryRef) : null;
    r.MergesIntoExisting = JoinedEarlierBody(f);          // rollback after f: body of f.GetFaces()[0] also carries faces of earlier non-sketch features
    if (r.Gauge != null && !File.Exists(r.Gauge.TablePath)) r.Notes.Add("Blechstärkentabelle nicht gefunden …; Werte werden fest übernommen.");
    return r;
}
```

`SwSheetMetal.ReadParameters(doc, feature, bodyLevel, toRef)`:
- `GetDefinition` → `ISheetMetalFeatureData`; `AccessSelections`.
- Read `Thickness`, `BendRadius`, `UseAutoRelief`/`AutoReliefType`/`ReliefRatio`, and `GetCustomBendAllowance()` (`Type`, `KFactor`, `BendAllowance`, `BendDeduction`, `BendTableFile`, late `BendCalculationTableFile`).
- `GetUseGaugeTable(out, out)`; `FixedReference` → `toRef`.
- If `bodyLevel`: `GetOverrideDefaultParameter2(0|1|2, out bool)`.
- Late-bound `UseMaterialSheetMetalParameters`.
- `ReleaseSelectionAccess`.

`CaptureFlatPattern`:
- `AccessSelections`: `FixedFace2` (Face2 → Face, Edge → Edge; not as an axis), `MergeFace` …, `GetAddCornerTrim(out _)`, `GetBreakCorners(out _)`, `IGetExcludedFacesCount`/`ExcludedFaces` → signatures.
- `Flattened = !(((object[])f.IsSuppressed2(1, null))[0] is bool b && b)` (fallback `!f.IsSuppressed()`).
- `Owner`: the body of the fixed face → `IBody2.GetFeatures()` → first `SMBaseFlange` / `SolidToSheetMetal` / legacy `SheetMetal`.
- `FlatChange`/`FlatSamples`: measured by `MassCapture` (§9).

`CaptureLegacyBends` (the SheetMetal feature whose children include `FlattenBends`/`ProcessBends` and no `SMBaseFlange`):
- `ISheetMetalFeatureData` → radius, allowance, `UseAutoRelief`, `ReliefRatio` (= offset ratio), `FixedReference`.
- `FlattenBends` `IBendsFeatureData.GetFixedFace()` under its own `AccessSelections` as a second source.
- Names and suppression of FlattenBends/ProcessBends.

`CaptureNormalCut`:
- `AccessSelections`; `GetGroupNames()` → `GetGroupByName` → `Faces` → signatures (rolled back, so the cut faces are not yet normal).
- `CutDirection`/`OffsetPlaneReference` → `ToGeometryRef`; flags.

`CaptureConvertLateBound`:
- `SwLate.Call(def, "AccessSelections", _doc, null)`; read `SheetThickness`, `ReverseThickness`, `BendRadius`, `KeepBody`, `RipGap`, `RipOverlapRatio`, `ReliefType`, `ReliefRatio`, `GetFixedFace()`, `BendEdges` (Edge → signature), `RipSketches`, `GetCustomBendAllowance()`, gauge members.
- If it is not readable (2019–2024): `UnsupportedRecord("Konvertierung in Blech: Feature-Daten erst ab SOLIDWORKS 2025 lesbar – Ersatzweg.", changesSolid: true)`, which hands over to the fallback.

`CaptureExtrude`: add `r.LinkToThickness = d.LinkToThickness;` (both boss and cut; only true in sheet metal).

## 9. Changes to Collector, MirrorJob, MassCapture, SwDoc and BuildContext

- **`SwDoc.SystemTypes`** += `"TemplateFlatPattern"`.
- **`FeatureCollector`**:
  - Never collect `TemplateSheetMetal`.
  - `SkipReason`: no "suppressed" skip for `FlatPattern`, its sub-features, or legacy `FlattenBends`/`ProcessBends`.
- **`SwDoc.FeaturesInCreationOrder`**: also list the `FlatPattern` sub-features of `TemplateFlatPattern` folders as items (like absorbed sketches), so multibody flat patterns are collected.
- **`FeatureCapture.NonSolidTypes`** += `SheetMetal`, `TemplateSheetMetal`, `FlatPattern`, `TemplateFlatPattern`.
- **`MirrorJob`**:
  - The owner check becomes `records.Where(r => r.BringsParts)` instead of `PositionedHoleRecord`.
  - `CaptureOnItsOwn` for a sheet-metal PartOf whose owner is not in the run: `UnsupportedRecord(…, changesSolid: false)` with the text "entsteht nur mit dem Basisflansch …" (only comes into being with the base flange).
  - `DeleteOriginals` excludes `Template*`.
- **`MassCapture`**:
  - Rollback "after" = `r.MeasureUntil`.
  - New for `FlatPatternRecord`: `folded = MassOf(doc)` → `SetSuppression2(Unsuppress, swThisConfiguration, null)` → `EditRebuild3` → `flat = MassOf(doc)`, up to 8 samples on the faces of the flattened body → restore the original suppression → rebuild. `FlatChange = flat − folded`. Wrap it in try/finally like the rollback.
  - When the original is already flattened, it is measured the other way round.
- **`BuildContext`**: `Dictionary<string, SheetMetalBodyMap> SheetMetalBodies`, keyed by the original owner name, value `(Feature SheetMetal, Feature? FlatPattern)`.
- **`FeatureEdit`**: add `BaseFlange`, `SheetMetal`, `FlatPattern`, `Bends`, `NormalCut`. These follow the existing pattern, and because `ModifyDefinition` can lie, every edit is followed by a read-back.

## 10. Builder outlines

### `BaseFlangeBuilder`

```csharp
public BuildResult Build(FeatureRecord record, BuildContext ctx)
{
    var r = (BaseFlangeRecord)record;
    if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap sketch)) return BuildResult.Failed($"Skizze \"{r.SketchName}\" …");
    var notes = new List<string>();
    string? refused = PlanSeparateBody(ctx, false, r.MergesIntoExisting, out bool scope, out bool createMerge);
    if (refused != null) return Fail(refused, notes);
    var (revDir, revThk) = SheetMetalRules.BaseFlangeFlags(r.ClosedProfile, r.ReverseDirection, r.ReverseThickness, sketch.Sigma);
    object? ref1 = ResolveFlangeEnd(r.Side1, …), ref2 = r.OffsetDirections == 2 ? ResolveFlangeEnd(r.Side2, …) : null;  // blind equivalent like ExtrudeBuilder

    var before = SwSheetMetal.Snapshot(ctx.Doc);                 // SheetMetal/FlatPattern/Template names incl. folder contents
    SheetMetalParameters p = r.Body ?? r.Defaults ?? Fallback(r);
    CustomBendAllowance cba = SwSheetMetal.NewAllowance(ctx.Doc.FeatureManager, r.OwnBendAllowance ?? (ctx.SamePart ? null : (r.Defaults ?? p).BendAllowance)); // values, Type last
    double d1 = r.ClosedProfile ? 0.01 : r.Side1.Distance, d2 = r.OffsetDirections == 2 ? r.Side2.Distance : 0;
    int e1 = (int)TemporaryEnd(r.Side1, ref1), e2 = (int)TemporaryEnd(r.Side2, ref2);   // up-to → Blind first, reference set in the edit
    ReliefData rel = r.OwnRelief ?? p.Relief;
    bool merge = createMerge || (r.MergesIntoExisting && !ctx.SeparateBody) || scope;

    Feature? f = Select(ctx, sketch, scope) ? fm.InsertSheetMetalBaseFlange2(r.Thickness, revThk, r.BendRadius, d1, d2, revDir, e1, e2, 1,
                     cba, false, (int)rel.Kind, rel.Width, rel.Depth, rel.Ratio, rel.UseRatio, merge, scope, !scope) : null;
    if (f == null && (!ctx.SamePart || ctx.OwnBodies().Count == 0) && Select(ctx, sketch, false))
        f = fm.InsertSheetMetalBaseFlange(r.Thickness, revThk, r.BendRadius, d1, d2, revDir, e1, e2, 1, cba, false, (int)rel.Kind, rel.Width, rel.Depth, rel.Ratio, rel.UseRatio)
            ?? (Select(ctx, sketch, false) ? fm.InsertSheetMetalBaseFlange(… DirToUse: 0 …) : null);
    if (f == null) f = CreateFromDataObject(ctx, r, sketch, revDir, revThk, cba, rel);   // CreateDefinition(34); late-bound Initialize(false, true, cba, true, …) if present; CreateFeature
    if (f == null || f.GetTypeName2() != "SMBaseFlange") { RemoveNew(ctx, before); return Fail("SOLIDWORKS hat den Basisflansch nicht erzeugt.", notes); }

    var made = SwSheetMetal.NewSince(ctx.Doc, before);          // SheetMetal, FlatPattern, Template (if any)
    bool ok = FeatureEdit.BaseFlange(ctx.Doc, f, d => {
        d.OffsetDirections = r.OffsetDirections; d.ReverseDirection = revDir; d.ReverseThickness = revThk;
        ApplySide(d, true, r.Side1, ref1); if (r.OffsetDirections == 2) ApplySide(d, false, r.Side2, ref2);  // D1OffsetType = ToFlangeOffset(...)
        if (r.Gauge != null && File.Exists(r.Gauge.TablePath)) { d.UseGaugeTable = true; d.GaugeTablePath = …; d.ThicknessTableName = …;
            d.TableRadius = …; d.OverrideThickness = …; d.OverrideRadius = …; d.OverrideKFactor = …; if (…) d.KFactor = r.Gauge.KFactor; }
        else { d.Thickness = r.Thickness; d.BendRadius = r.BendRadius; }
        if (r.SymmetricThickness) SwLate.Set(d, "SymmetricThickness", true);
    });
    if (!ok) notes.Add("Basisflansch: Richtungen/Endbedingungen/Tabelle ließen sich nicht vollständig setzen.");
    if (!ctx.SamePart && made.Template != null && r.Defaults != null) notes.AddRange(SwSheetMetal.ApplyAndCompare(ctx.Doc, made.Template, r.Defaults, bodyLevel: false));
    if (made.SheetMetal != null && r.Body != null) notes.AddRange(SwSheetMetal.ApplyAndCompare(ctx.Doc, made.SheetMetal, r.Body, bodyLevel: true)); // SetOverrideDefaultParameter2(i, flag) first
    notes.AddRange(SwSheetMetal.CompareBaseFlange(ctx.Doc, f, r, revDir, revThk));   // read-back

    ctx.Register(r.Name, f);
    if (made.SheetMetal != null && r.SheetMetalName.Length > 0) ctx.Register(r.SheetMetalName, made.SheetMetal);  // keeps "Thickness@Sheet-Metal1" equations working
    ctx.SheetMetalBodies[r.Name] = new SheetMetalBodyMap(made.SheetMetal, made.FlatPattern);
    BuildResult result = Ok(f, Describe(r, revDir != r.ReverseDirection, revThk != r.ReverseThickness), notes);
    AddAlternatives(result, ctx, f, r);
    return result;
}
```

`Select` uses `SwSelect.Select(doc, sketch.Feature, 0, false)`; with scope it also selects `OwnBodies()` on mark 8.

Alternatives:
- Closed profile: "Blechdicke auf die andere Seite" (thickness to the other side), toggling `ReverseDirection`; then "Dickenrichtung (ReverseThickness) umgekehrt", for versions where it acts.
- Open profile: "Richtung umgekehrt" (`ReverseDirection`) and "Materialseite der Kontur gewechselt" (`ReverseThickness`). The two are independent (one shifts along the normal, the other in the plane), so the greedy order of `StepVerifier` finds the combination.
- No direction alternative for mid-plane or `SymmetricThickness`.

### `FlatPatternBuilder`

```csharp
var r = (FlatPatternRecord)record;
if (!ctx.SheetMetalBodies.TryGetValue(r.Owner, out var map) || map.FlatPattern == null)
    return BuildResult.Failed($"Im Ziel gibt es keine Abwicklung zu „{r.Owner}“.");
Feature fp = map.FlatPattern;
object? fixedEnt = r.FixedFace == null ? null : (r.FixedFace.Kind == GeometryRefKind.Face
    ? Safe(ctx.Topology.FindFace(r.FixedFace.Face!.Mirror(ctx.Mirror), notes)) : Safe(ctx.Topology.FindEdge(r.FixedFace.Edge!.Mirror(ctx.Mirror), notes)));
if (r.FixedFace != null && fixedEnt == null) notes.Add("Feste Fläche der Abwicklung im Ziel nicht gefunden; SOLIDWORKS-Vorgabe bleibt.");
var excluded = r.ExcludedFaces.Select(s => Safe(ctx.Topology.FindFace(s.Mirror(ctx.Mirror), notes))).Where(x => x != null).ToArray();
bool ok = FeatureEdit.FlatPattern(ctx.Doc, fp, d => {
    if (fixedEnt != null) d.FixedFace2 = fixedEnt;
    d.MergeFace = r.MergeFaces; d.SimplifyBends = r.SimplifyBends; d.CornerTreatment = r.CornerTreatment;
    d.CornerTrimReliefType = …; d.CornerTrimReliefDistance = …; d.UseRatioToThickness = …; d.CornerTrimRatioToThickness = …;
    d.BreakCornerType = …; d.BreakCornerRadius = …; d.SetAddCornerTrim(r.AddCornerTrim); d.SetBreakCorners(r.BreakCorners);
    d.ShowSlitInCornerRelief = r.ShowSlitInCornerRelief; if (excluded.Length > 0) d.ExcludedFaces = excluded;
});
// If AccessSelections on the suppressed flat pattern fails (L9): unsuppress → edit → restore.
notes.AddRange(SwSheetMetal.CompareFlatPattern(ctx, fp, r, fixedEnt));      // read-back incl. fixed face signature
ctx.Register(r.Name, fp);
string check = FlatStateCheck(ctx, fp, r, notes);                           // see §11
SwSheetMetal.SetFlattened(fp, r.Flattened);                                 // SetSuppression2(…, swThisConfiguration, null)
ctx.Report.General.Add(GrainNote) once per job;                             // grain direction not accessible via API
return Ok(fp, $"Abwicklung, feste Fläche {(fixedEnt != null ? "gespiegelt" : "Vorgabe")}; {check}", notes);
```

### `ExtrudeBuilder` (small change)

- `needsEdit |= r.LinkToThickness`.
- In the edit lambda, after `ApplySide`: `if (r.LinkToThickness) d.LinkToThickness = true;`.
- If only the link fails: a note, not a failure (the geometry is the same).
- `NormalCut`/`OptimizeGeometry` are already passed to `FeatureCut4`.

### `NormalCutBuilder`

1. `def = (SMNormalCutFeatureData2)fm.CreateDefinition(115)`; set `AutoPropagation`, `OptimizeGeometry`, `NormalCutParameters`, `LinkToKFactor`, `LayerAdjustmentValue`.
2. Select the plane on mark 4 (offset plane mode) or the direction on mark 8 (extent mode).
3. For each group: select its mirrored faces on mark 1 (`Entity.Select4`), call `def.CreateGroup(out err)`, then `ClearSelection2`.
4. `CreateFeature(def)`.
5. Fallback: the obsolete `AddSMNormalCutType`/`AddSMNormalCut`/`FinishSMNormalCut` (L17).

A missing face makes the whole feature fail, because otherwise the copy would be a different cut. No direction flag: everything is reference-based.

### `LegacyBendsBuilder`

1. Select the mirrored fixed face (`Entity.Select4`, mark 0; L15).
2. `args = SheetMetalRules.LegacyArguments(r.BendAllowance, r.OffsetRatio)` gives (table, K, BA), with −1 for "not used". The offset ratio must be within 0.05..2 (help).
3. `((PartDoc)doc).InsertBends2(r.BendRadius, args.Table, args.K, args.BA, r.UseAutoRelief, r.OffsetRatio, doFlatten: r.FlattenBendsName != null)`.
4. Find the new features by type (`SheetMetal`, `FlattenBends`, `ProcessBends`) and register them under the original names.
5. Kinds not expressible here (deduction, calculation table): `SwSheetMetal.ApplyAndCompare` on the new legacy SheetMetal.
6. `ProcessBends` suppression as in the original.

### `ConvertSolidBuilder` (2025+ path, and the fallback's conversion step)

- With a `ConvertSolidRecord`: select the fixed face (mark 0) and the bend edges (mark 1, both via `Entity.Select4`; CADapter says the marks make no difference). Then late-bound `CreateDefinition(swFmSolidToSheetMetal)` + `Initialize` + `CreateFeature`. Fallback: `InsertConvertToSheetMetal2(t, ReverseThickness, false, r, gap, relief, ratio, overlapType, overlapRatio, keepBody)`.
  - The result is judged by geometry only: a new `SolidToSheetMetal` feature exists and `IBody2.IsSheetMetal()`. CADapter found the call "returns False even when it builds".
- **Helper for the fallback** (`SwSheetMetal.ConvertBody(ctx, body, SheetMetalParameters, mirroredFixedFace)`):
  1. `InsertConvertToSheetMetal2(t, false, FindBends: true, r, …)` with only the fixed face selected. `FindBends` means "find pre-made bends", which suits the mirrored dumb body that already has round bends (L16).
  2. Otherwise `InsertBends2(r, …, doFlatten: false)`.
  3. Then `ApplyAndCompare` for the parameters, `FlatPatternBuilder` logic for the fixed face, and the flat-state check.
  4. Report note "Ersatzweg: Blechkörper als Geometrie gespiegelt und in Blech umgewandelt" (fallback: body mirrored as geometry and converted to sheet metal).

## 11. Verification and the alternatives the verifier tries

- **Base flange:** the standard `StepVerifier` check (volume, first moment, face samples). The sides are clearly separable:
  - Closed profile on the wrong side: the centroid shifts by t along n.
  - Open profile on the wrong side: inner and outer perimeters differ, which changes volume and centroid in the plane.
- **SheetMetal parameters** (folded mass does not see K, BA or the table): checked by **read-back comparison** after `ApplyAndCompare`, with differences listed as notes (Warning) such as "K-Faktor Soll 0,44 / Ist 0,5". Final confirmation comes from the flat-state check.
- **Flat pattern, flattened state:**
  1. `before = MassOf(scope)`, where scope = `OwnBodies()` in Same Part and the whole part otherwise.
  2. Unsuppress the flat pattern and rebuild; `after`.
  3. `MassComparison.Compare(r.FlatChange.Mirror(ctx.Mirror), after − before, after)`; face samples `r.FlatSamples` mirrored (`MissedSamples` made internal static).
  4. Restore the suppression.
  - Pass: "Abwicklung geprüft". Fail: Warning "Abwicklung weicht ab – K-Faktor/Biegezugabe/feste Fläche prüfen". No toggles, because no flag can sensibly be flipped here.
  - Justification: the flattening construction commutes with R when the allowance is the same and the fixed face is the mirrored one, so the (flat − folded) change must be its mirror image. It is compared exactly like a feature change (`MassState.Mirror` is linear and works for differences).
- **`ExtrudeRecord` with `LinkToThickness`:** standard check, with the existing alternatives.
- **Legacy bends:** standard check across the range SheetMetal … ProcessBends (`MeasureUntil`).
- **Convert:** standard check; alternative "Dickenrichtung umgekehrt"; rip "Überlappung ↔ Unterlappung" (overlap ↔ underlap).
- **Normal cut:** standard check; no alternatives.
- **Same Part:** the existing check that the original bodies are untouched (mass) also applies. The template is never edited.

## 12. Core tests (`tests/Spiegelwerk.Core.Tests/SheetMetalTests.cs`)

1. `Open_profile_thickness_side_is_mirrored_for_every_plane_combination`: random reflections, σ ±1. The side vector s = (rev ? −1 : 1)·(n×t); the copy uses n′ = σR(n), t′ = R(t) and `InPlaneSideFlag`; assert s′ ≈ R(s).
2. `Closed_profile_plate_lies_on_the_mirrored_side`: thickness vector (rev ? −n : n) with `ReverseFlag`, random cases (as in `DraftDirectionTests`).
3. `Sketch_on_right_mirrored_at_right_flips_depth_but_keeps_contour_side` and `Sketch_on_front_mirrored_at_right_keeps_depth_but_flips_contour_side`: the two worked examples from §6.
4. `BaseFlangeFlags_closed_profile_never_touches_ReverseThickness`.
5. `Flange_offset_types_map_to_end_conditions_and_back`: (1..5 ↔ 0, 3, 4, 5, 6); an unknown value gives null.
6. `Legacy_insert_bends_arguments_follow_the_allowance_kind`: K → ("", K, −1); Direct → ("", −1, BA); Table → (file, −1, −1); Deduction/CalcTable → null.
7. `Offset_ratio_outside_0_05_to_2_is_rejected`.
8. `Parameter_differences_name_each_mismatch`: K, radius, thickness, kind, table path; within tolerance → empty.
9. `Flat_state_change_of_an_L_sheet_mirrors_like_a_mass_change`: analytic L sheet. Folded = two plates; flat = one plate in the plane of the fixed face, length L1 + L2 + BA. Show that `MassState.Mirror(flat − folded)` equals (flat − folded) built with the mirrored geometry and mirrored fixed face, for several mirror planes.
10. Ownership rules (`SheetMetalTree`):
    - `SheetMetal_belongs_to_its_base_flange`
    - `Flatten_and_process_bends_belong_to_the_legacy_sheet_metal`
    - `Flat_pattern_subsketches_belong_to_the_flat_pattern`
    - `Template_folders_are_never_collected`
    - `Flat_pattern_inside_template_folder_is_listed`
    - `Sheet_metal_without_its_flange_in_the_run_is_reported_not_rebuilt`
11. `Grain_direction_is_an_unoriented_line`: R(d) and −R(d) count as the same line (in case a later API allows it).

## 13. Assumptions that need a live test

| # | Assumption | Fallback / how it shows |
|---|---|---|
| L1 | `InsertSheetMetalBaseFlange2` with `DirToUse` = 1 and the sketch on mark 0 builds on 2019–2026 (CADapter: no-op on 2017) | v1 (16 parameters) with `DirToUse` 1, then 0; then `CreateDefinition(34)` |
| L2 | `CreateDefinition(34)` + `CreateFeature` works in 2019 without `Initialize`; in newer versions with late-bound `Initialize` (2nd parameter = "OverrideDefaultBendAllowance" in the interop, "UseDefaultBendAllowance" in the help comment) | Report names the path taken |
| L3 | The open-profile `ReverseThickness` side is relative to the sketch normal (model a), and SOLIDWORKS uses our segment direction | "Materialseite" alternative; the report says "korrigiert nach Messung" when the guess was wrong. If L3 holds, apply the same rule to `ExtrudeBuilder` thin features. |
| L4 | Closed profile: `ReverseDirection` (not `ReverseThickness`) decides the side (3P-live), also for tabs on a face | Toggle alternatives |
| L5 | `PCBA` at creation sets the defaults of the new SheetMetal/template; Type written last | Read-back; `ApplyAndCompare` |
| L6 | Changes on the template (`GetTemplateSheetMetal().GetDefinition()`) take effect (K, radius, thickness), but **body-level** changes only after `SetOverrideDefaultParameter2(i, true)` (CADapter: K never changed on the body) | Warning with target vs actual values, plus the flat-state check |
| L7 | A gauge table can be set afterwards via `ModifyDefinition` on `IBaseFlangeFeatureData` in the order UseGaugeTable → Path → ThicknessTableName → TableRadius → Override* (CADapter changed the gauge this way) | Fixed values with a note |
| L8 | The flat pattern is created with the base flange and is **suppressed**; later features land before it; `GetLastFeatureAdded` still returns the new feature | Before/after comparison of names |
| L9 | `AccessSelections` + `FixedFace2` + `ModifyDefinition` works on a suppressed flat pattern (CADapter `flat_options`) | Unsuppress → edit → restore |
| L10 | `SetSuppression2(1, swThisConfiguration, null)` flattens and `0` folds (3P-live); in multibody this applies per body; several may be flattened at once | Measure one flat pattern at a time |
| L11 | The SheetMetal feature is a parent of the base flange (help example `GetParents()[1]`), so it comes first in `CreationOrder` | Owner search via `GetChildren` |
| L12 | Multibody: the SheetMetal/FlatPattern features sit inside the `TemplateSheetMetal`/`TemplateFlatPattern` folders as sub-features; types as in the table | Tree walk through `GetSheetMetalFolder().GetSheetMetals()` / `GetFlatPatternFolder().GetFlatPatterns()` |
| L13 | The flat pattern's sub-sketches (Bend-Lines, Bounding-Box) have the type `ProfileFeature` and are suppressed when folded | Owner rule by parent, not by type |
| L14 | Grain direction (newer versions, set in the flat pattern PropertyManager) has no API and is not visible as a parent | General report line. If it shows up as a parent sketch or edge: per-entry note (Warning) |
| L15 | `InsertBends2` takes the fixed face as the current selection (mark 0) | Marks 1 and 0 tried |
| L16 | `InsertConvertToSheetMetal2(FindBends: true)` finds the existing round bends of a dumb mirrored body with only the fixed face selected (CADapter: without bend edges, `FindBends` = false destroys the body) | `InsertBends2`; geometry check (volume unchanged, `IsSheetMetal`) |
| L17 | Normal cut via `CreateDefinition(115)` (marks 1/4/8, `CreateGroup` per group) works in 2019 (help example 2019) | Obsolete `AddSMNormalCut*` |
| L18 | Rip overlap/underlap is not orientation-dependent | Toggle alternative |
| L19 | In Same Part, a second base flange (`Merge` = false) makes its own sheet-metal body and does not change the original's SheetMetal or template (3P: "a part that is sheet metal already may refuse a second base flange") | Original mass check; otherwise the feature is removed and reported |
| L20 | `MergesIntoExisting` can be recognized geometrically after rolling back past the base flange (the face body also carries earlier features) | Note "Zusammenführen angenommen" (merge assumed) |
| L21 | Equations such as "D1@Sheet-Metal1" keep working after `Register` renames the new SheetMetal to the original name | Existing equation report |
| L22 | `IConvertSolidFeatureData` (2025+) can be read late-bound through `SwLate`; bend edges are in the pre-feature state during `AccessSelections` | Otherwise Unsupported, which hands over to the fallback |
| L23 | `ISheetMetalFeatureData` on `TemplateSheetMetal` comes from `GetDefinition` (the type table says `ISheetMetalFolder` for `GetSpecificFeature2`) | Fall back to the single body-level SheetMetal |
| L24 | `ICustomBendAllowance.BendCalculationTableFile` (type 5) is available late-bound in newer versions | Note "Biegeberechnungstabelle nicht übertragbar" (bend calculation table not transferable) |

## 14. Effort estimate

- **Records:** 5 new `FeatureRecord` types (`BaseFlangeRecord`, `FlatPatternRecord`, `LegacyBendsRecord`, `ConvertSolidRecord`, `NormalCutRecord`), 5 helper data classes (`BendAllowanceData`, `ReliefData`, `GaugeData`, `SheetMetalParameters`, `FlangeSide`), 2 enums, 1 field on `ExtrudeRecord`, 2 virtuals on `FeatureRecord`.
- **Builders:** 5 new (`BaseFlange`, `FlatPattern`, `NormalCut`, `LegacyBends`, `ConvertSolid`), 1 changed (`ExtrudeBuilder`); `PartOfBuilder` reused.
- **Add-in infrastructure:**
  - `Sw/SwSheetMetal.cs` (about 350 lines: read/apply/compare, CBA, template and folders, flattened state, snapshot).
  - `Capture/FeatureCapture.SheetMetal.cs` (about 400 lines).
  - 5 `FeatureEdit` methods.
  - Changes to `FeatureCollector`, `SwDoc`, `MirrorJob`, `MassCapture`, `BuildContext` (about 150 lines).
- **Core:** `SheetMetalRecords.cs` (about 220), `SheetMetalRules.cs` (about 120), `SheetMetalTree.cs` (about 90), tests (about 12 facts, about 350 lines).
- **Size:** about 2,000–2,400 lines in the add-in plus about 800 in Core and tests, roughly 1.5× the hole wizard work package. Plus docs: an ARCHITEKTUR section, about 15 STOLPERSTEINE entries, and the rows from §13.
- **Suggested phases:**
  - P1 (about 60%): `SheetMetalParameters`, `BaseFlange`, `FlatPattern` with the flat-state check, `LinkToThickness`. This is enough for the user's main workflow and is the foundation for areas 2 and 3.
  - P2 (about 40%): `NormalCut`, legacy `InsertBends`, `ConvertSolid` (2025+), and the conversion helper for the fallback.

Repository files read (unchanged): `/home/user/Claude/spiegelwerk/docs/ARCHITEKTUR.md`, `/home/user/Claude/spiegelwerk/docs/STOLPERSTEINE.md`, `/home/user/Claude/spiegelwerk/src/Spiegelwerk/Build/Builders/ExtrudeBuilder.cs`, `/home/user/Claude/spiegelwerk/src/Spiegelwerk/Build/Builders/ShellBuilder.cs`, `/home/user/Claude/spiegelwerk/src/Spiegelwerk/Build/StepVerifier.cs`, `/home/user/Claude/spiegelwerk/src/Spiegelwerk/Capture/FeatureCollector.cs`, `/home/user/Claude/spiegelwerk/src/Spiegelwerk/Capture/MassCapture.cs`, `/home/user/Claude/spiegelwerk/src/Spiegelwerk.Core/Records/FeatureRecords.cs`, `/home/user/Claude/spiegelwerk/src/Spiegelwerk.Core/Planning/DirectionRules.cs`.