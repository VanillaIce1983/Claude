# Area 3 design: sketched bends, cross break, corners, unfold/fold and lofted bend

I read the code and docs and changed nothing in `/home/user/Claude`. Evidence labels used below:

- **[I]** checked by reflection in interop 27.3.0 (SOLIDWORKS 2019). Where it says "since X", I also checked 29.4.1, 30.5.1 and 32.1.0.
- **[D]** official API help, read as markdown from `streetartist/solidworks-automation/docs/sldworksapi`, plus official examples from `f1owkang/solidworks-plugin-dev/references/source-docs/Feature`.
- **[M]** third-party live measurement found on GitHub:
  - `luigidoria/CADapter` `solidworks/sw_sheetmetal.py` (SW 2017)
  - `eyfel/mcp-server-solidworks` `SolidWorksService.SheetMetal.cs` and `.Analyze.cs`
  - `soheil-mp` notes (a sibling agent's copy at `scratchpad/ghsrc/soheil_notes.md`)
  - `xarial/codestack` sketched-bend macro
- **[?]** not verified; needs a live test.

help.solidworks.com and codestack.net could not be reached. `raw.githubusercontent.com` and GitHub code search worked.

## 0. API facts (verified)

### GetTypeName2 → data interface

| Feature | GetTypeName2 | Data interface | Evidence |
|---|---|---|---|
| Sketched bend | `SM3dBend` | `ISketchedBendFeatureData` | [D] help table, codestack, official example |
| Its bend sub-features | `SketchBend` | `IOneBendFeatureData` | [D] |
| Edge-flange and other bends | `OneBend` (shown as "EdgeBend3" etc.) | `IOneBendFeatureData` | [D] |
| Cross break | `CrossBreak` | `ICrossBreakFeatureData` | [D] help table and example |
| Closed corner | `CornerFeat` | `IClosedCornerFeatureData` | [D] "Get All Sheet Metal Feature Data" example; [M] CADapter, SW_MCP |
| Break corner (folded) | `BreakCorner` | `IBreakCornerFeatureData` | [D] |
| Corner trim (flattened) | `CornerTrim` | `IBreakCornerFeatureData` | [D] |
| Unfold / Fold | `UnFold` (capital F) / `Fold` | `IFoldsFeatureData` | [D] |
| Lofted bend | `LoftedBend` | `ILoftedBendsFeatureData` | [D] |
| Corner relief | unknown; SW_MCP's map says `CornerRelief` | `ICornerReliefFeatureData` + `ISMCornerReliefData` | [?] name; [I] interface only from interop 30.5.1 (SW 2022), absent in 27.3.0 and 29.4.1 |
| Welded corner | unknown | none | [I] no data interface and no creation method in 27.3, 29.4, 30.5 or 32.1. Weldment `WeldCornerFeat` is a different feature. |

Because of these gaps, capture identifies features by their data interface (as the existing code does), not by type name. The only exceptions are corner relief (late-bound probe) and welded corner (type name collected in the live test).

### Data interfaces, members identical in 27.3.0 and 32.1.0 [I]

- **`ISketchedBendFeatureData`**
  - Properties: `double BendAngle {get;set;}`, `double BendRadius`, `int PositionType` (values of `swFlangePositionTypes_e`: 1 MaterialInside, 2 MaterialOutside, 3 BendOutside, 4 BendCenterLine), `bool ReverseDirection`, `bool UseDefaultBendAllowance`, `bool UseDefaultBendRadius`.
  - Methods: `CustomBendAllowance GetCustomBendAllowance()`, `void SetCustomBendAllowance(CustomBendAllowance)`, `object GetFixedFace(out double X, out double Y, out double Z)`, `void SetFixedFace(double X, double Y, double Z, object EdgeArray)`, `bool AccessSelections(object, object)`, `void ReleaseSelectionAccess()`.
  - 32.1.0 adds `OverrideValue {get}` and `UseGaugeTable {get;set}`; they are not in 30.5.1.
  - [D] `GetFixedFace` returns the face plus "a point on the fixed face, but in the space of the sketch … z is always 0".
- **`ICrossBreakFeatureData`**: `double BreakAngle`, `double BreakRadius`, `object Face {get;set;}`, `bool ReverseDirection`, `AccessSelections`, `ReleaseSelectionAccess`.
- **`IClosedCornerFeatureData`**: `int CornerType` (`swClosedCornerTypes_e`: Butt 1, Overlap 2, Underlap 3), `object Faces {get;set;}`, `double GapDistance`, `bool OpenBendRegion`, `double OverlapUnderlapRatio`, `IGetFaces()`, `IGetFacesCount()`, `ISetFaces(int, ref object)`, access/release.
- **`IBreakCornerFeatureData`**: `int BreakType` (`swBreakCornerTypes_e`: Fillet 0, Chamfer 1), `double Distance`, `object Entities {get;set;}` (faces or edges), `int GetEntitiesCount()`, `bool InternalCornersOnly`, `bool CenteredOnBendLines`, access/release.
- **`IFoldsFeatureData`**: `object Bends {get;set;}` (array of bend Features), `object FixedFace {get;set;}`, `IGetBends()`, `IGetBendsCount()`, `ISetBends(int, ref object)`, access/release.
- **`ILoftedBendsFeatureData`**: `Profiles {get;set;}`, `GetProfileCount()`, `double Thickness`, `bool Direction`, `bool FormedMethod`, `int FacetingOption` (`swLoftedBendFacetOptions_e` 0–3), `double FacetValue`, `int BendLineControlOption`, `int NumberOfBendLines`, `double MaximumDeviation`, `bool ReferToEndPoint`, `bool CanCreateBendLines()`.
- **`ICornerReliefFeatureData`** (SW 2022+, late-bound only, because the code compiles against 27.3.0):
  - `int CornerType {get}` (`swCornerReliefBendType_e`: TwoBend 0, ThreeBend 1), `int CornerCount {get}`.
  - `bool Initialize(int CornerType)`, `int SetBodyScope(object SheetBody)`, `object GetBodyScope()`.
  - `int AddNewCorner(object Face1, object Face2, object Face3, int ReliefType, out object Corner)`, `int CollectAllCorners(int ReliefType, out object Corners)`, `object GetCorners(int type)` (−1 = all), `int GetCornerAtIndex(int, out object)`, `bool RemoveCorner(int)`.
  - Integer returns are `swCornerReliefError_e` (0 = None).
- **`ISMCornerReliefData`**: `int ReliefType` (`swCornerReliefType_e` 0–9; 9 Rectangular only from 32.x), `double SlotLength`, `SlotWidth`, `CornerFilletDiameter`, `bool AddFilletedCorners`, `CenterOnBendLines`, `RatioToThickness`, `TangentToBend`, `int SuitCaseType`, `int CornerIndex {get}`, `void GetFaces(out object F1, out object F2, out object F3)`.
- **`IOneBendFeatureData`**: identifies bend sub-features. Has `BendRadius`, `BendAngle`, `KFactor`, `BendAllowanceType`, and so on.
- **`ICustomBendAllowance`**: `int Type` (`swBendAllowanceTypes_e` 1–5), `double KFactor`, `BendAllowance`, `BendDeduction`, `string BendTableFile`.
- **`IFlatPatternFeatureData`** (corner-trim settings live here): `CornerTrimReliefType`, `CornerTrimReliefDistance`, `CornerTrimRatioToThickness`, `bool GetAddCornerTrim(out Feature)`, `bool GetBreakCorners(out Feature)`, `BreakCornerType`, `BreakCornerRadius`.

### Creation calls in 27.3.0 [I], with parameter meaning [D]

| Call | Returns | Selection before the call |
|---|---|---|
| `IFeatureManager.InsertSheetMetal3dBend(double Angle, bool BUseDefaultRadius, double Radius, bool FlipDir, short BendPos, CustomBendAllowance PCBA)` | `Feature` | bend sketch with mark 0, then the fixed face appended with mark 0, picked at a point on the side that stays fixed ([M] CADapter, eyfel, soheil; the official corner-relief example does the same). `BendPos`: 0 bend centre line, 1 material inside, 2 material outside, 3 bend outside — a different encoding from `PositionType`. `PCBA` null means the parent's allowance. |
| `IModelDoc2.InsertSheetMetal3dBend(double, double, bool, short)` | void | obsolete; do not use |
| `IFeatureManager.InsertCrossBreak(double Angle, double Radius)` | `Feature` | the face, mark 0 |
| `IModelDoc2.InsertSheetMetalClosedCorner()` | void | faces to extend, mark 1 ([M] CADapter: one flange side face; marks 0 and 1 work, 2 and 4 do not). The official example also selects a second face with mark 1073741824 (0x40000000), probably "faces to match" [?]. |
| `IModelDoc2.InsertSheetMetalBreakCorner(int Type, double Distance)` | void | corner edges across the thickness, or whole faces, mark 0 ([D] example, [M] SW_MCP). Distance is the radius for a fillet. |
| `IFeatureManager.InsertSheetMetalCornerTrim(int InternalCornerFlag, int BreakType, double BreakDist, int ReliefType, double Param)` | `Feature` | flattened state only. `ReliefType`: 0 circular (Param = diameter), 1 square (side), 2 bend-waist (radius). [M] Needs several edges; one edge returns null; flatten by unsuppressing Flat-Pattern, because `SetBendState` does not flatten through COM. |
| `IModelDoc2.InsertSheetMetalUnfold()` / `InsertSheetMetalFold()` | void | fixed face mark 1, bend sub-features mark 4 ([D] official example). [M] CADapter: mark 2 for bends also works; with a wrong selection SOLIDWORKS creates "UnFold1" that unfolds nothing, with no error. |
| `IFeatureManager.AddCornerReliefCorner()` → `int`, `AddCornerReliefType(int CornerIndex, int ReliefType, double Length1, double Length2, double Length3, bool CenterOnBendLines, bool RatioToThickness, bool TangentToBend, bool AddFilletedCorners, bool NarrowCorner)` → `bool`, `FinishCornerRelief()` → `Feature` | | in 2019. Body mark 0 (append), the two faces of a corner mark 4, then `AddCornerReliefCorner` and `AddCornerReliefType(-1, …)`; repeat per corner; then `FinishCornerRelief`. `Length1` is unused; `Length2` = length (square/obround) or radius (circular); `Length3` = slot width (obround) or fillet radius (square with fillets). |
| `IFeatureManager.CreateDefinition(int)` + `CreateFeature(object)` | | `swFmCornerRelief` = 120 exists from 30.x. `swFmSketchBend` = 35 exists in 2019 but [M] returns null on 2017; it works in the 2024 help example. |
| `IFeatureManager.InsertSheetMetalLoftedBend2(int ThickDirType /*0 outside, 1 inside*/, double Thickness, bool BFormed, double DRadius, bool BReferToEndPoint, int EFacetOption, double DChordTol, int INumBends, double DSegLength, double DSegAngle)` | `Feature` | [D] profiles with mark 1. [M] CADapter: the segments of both sketches must be selected with mark 1; whole sketches with mark 0, 1 or 4 return null. |

**Calls named in the task that do not exist** (in any of 27.3, 29.4, 30.5 or 32.1): `InsertBreakCorner`, `InsertSheetMetalCornerRelief`, any welded-corner insert, and `IModelDoc2.InsertCrossBreak`.

**Measured traps:**
- `InsertSheetMetal3dBend` ignores `FlipDir`: true and false gave identical folds, and the result reads back `ReverseDirection = false`. Setting `ReverseDirection` afterwards with `ModifyDefinition` works. [M] eyfel
- `ModifyDefinition` on `SM3dBend` returns false when the bend allowance is changed. [M] CADapter, SW 2017
- `BendPos = 4` is a silent no-op. [M] CADapter, consistent with the 0–3 range in [D]
- Picking the fixed face on or near the bend line holds the wrong half. [M] CADapter (jog), eyfel ("4-1 Bend1 lesson")
- Cross break does not change the solid. [M] CADapter

## 1. Prerequisites shared with Areas 1 and 2

1. **`FeatureEdit`**: add typed edits `SketchedBend`, `CrossBreak`, `ClosedCorner`, `BreakCorner`, `Folds`, following the existing pattern. Add `FeatureEdit.Late(doc, f, object data, Action change)`, which accesses and releases through `SwLate` for corner relief.
2. **`SwLate`**: add `Invoke(com, method, params object[])` that returns the result object. Add `InvokeOut(com, method, object[] args, bool[] byRef)` using `ParameterModifier`, for `GetFaces(out,out,out)` and `AddNewCorner(..., out)` [?].
3. **`SwFeatures.LastAddedOf<TData>(doc, string beforeName)`**: for the void creators. Same logic as `ShellBuilder.Create`, but checks `GetDefinition() is TData` instead of the type name.
4. **`BuilderSupport.SelectFaceAt(ctx, face, at, mark, append)`**: add the `append` overload. The current one clears the selection first.
5. **Bend lookup** (new `TopologyFinder.FindBend`, section 5.6). It needs bend-owning features of Areas 1 and 2 (base, edge, miter flange, hem, jog) to be registered in `ctx.Created`.
6. **Sketch on a face.** `SketchBuilder` always sketches on a resolved plane. Whether `SM3dBend` accepts a bend sketch on a reference plane coplanar with the face is unknown [?]. Proposal:
   - Add `SketchRecord.SupportFace` (a `FaceSignature`). Capture it in `FeatureCapture.ReadSketch` via `ISketch.GetReferenceEntity(out int)` [I] when it is a `Face2`, using the existing "rollback after sketch" branch.
   - Add `BuildContext.FaceSupportedSketches` (a `HashSet<string>`, like `PointSketches`). `MirrorJob` fills it with `SketchedBendRecord.SketchName` (Area 2 adds jog sketches).
   - `SketchBuilder.Build` opens these sketches on the mirrored face if it is found, coplanar with the mirrored sketch plane, and an own body in SeparateBody mode. Otherwise it falls back to the plane, with a note.
7. **Flat-pattern scope** (owned by the flat-pattern area): unsuppress the target's Flat-Pattern, act, re-suppress. Corner trims are sub-features of the normally suppressed Flat-Pattern [?], so `FeatureCollector` (top level only) never sees them, and `SkipReason` would skip them as suppressed.

## 2. Records (`Spiegelwerk.Core/Records/SheetMetalBendRecords.cs`)

```csharp
public sealed class BendAllowanceSpec          // ICustomBendAllowance; share with Area 1 if it defines one
{ public int Type; public double KFactor, BendAllowance, BendDeduction; public string BendTableFile = ""; }

/// A bend sub-feature (OneBend/SketchBend/EdgeBend…), found again by the faces it made.
public sealed class BendRef
{
    public BendRef(string owner, int ordinal, string name) { … }
    public string Owner { get; }      // original name of the owning feature (IFeature.GetOwnerFeature)
    public int Ordinal { get; }       // index among the owner's sub-features whose data is IOneBendFeatureData
    public string Name { get; }       // for the report
    public List<FaceSignature> Faces { get; } = new();   // IFeature.GetFaces of the sub-feature, in the captured state
    public BendRef Mirror(Reflection r);                 // faces mirrored, owner/ordinal kept
}

public sealed class SketchedBendRecord : FeatureRecord   // "SM3dBend"
{
    public SketchedBendRecord(string name, string typeName, string sketchName);
    public string SketchName { get; }                     // absorbed ProfileFeature, rebuilt first as SketchRecord
    public double Angle { get; set; }                     // rad
    public bool UseDefaultRadius { get; set; }
    public double Radius { get; set; }
    public int PositionType { get; set; }                 // swFlangePositionTypes_e as read (1..4)
    public bool Reverse { get; set; }
    public bool UseDefaultBendAllowance { get; set; }
    public BendAllowanceSpec? BendAllowance { get; set; }
    public FaceSignature FixedFace { get; set; } = null!;
    public Vec3? FixedPoint { get; set; }                 // model coords on FixedFace; null = side unknown
    public int BendCount { get; set; }                    // SketchBend sub-features
}

public sealed class CrossBreakRecord : FeatureRecord     // "CrossBreak"
{ public double Angle, Radius; public bool Reverse; public FaceSignature Face = null!;
  public override bool ChangesSolid => false; }          // cosmetic [M]

public sealed class ClosedCornerRecord : FeatureRecord   // "CornerFeat"
{ public int CornerType; public double Gap, OverlapRatio; public bool OpenBendRegion;
  public List<FaceSignature> Faces { get; } = new(); }   // IClosedCornerFeatureData.Faces, original order

public enum BreakCornerKind { BreakCorner, CornerTrim }
public sealed class BreakCornerRecord : FeatureRecord    // "BreakCorner" / "CornerTrim"
{ public BreakCornerKind Kind; public int BreakType; public double Distance;
  public bool InternalCornersOnly, CenteredOnBendLines;
  public List<EdgeSignature> Edges { get; } = new(); public List<FaceSignature> Faces { get; } = new();
  public int TrimReliefType = -1; public double TrimReliefSize; public bool TrimRatioToThickness; } // CornerTrim only, from IFlatPatternFeatureData

public sealed class CornerReliefItem
{ public FaceSignature Face1 = null!, Face2 = null!; public FaceSignature? Face3;
  public int ReliefType, SuitCaseType; public double SlotLength, SlotWidth, CornerFilletDiameter;
  public bool AddFilletedCorners, CenterOnBendLines, RatioToThickness, TangentToBend; }
public sealed class CornerReliefRecord : FeatureRecord   // type name [?]
{ public int CornerType; public FaceSignature? BodyFace;  // identifies the GetBodyScope body (like ShellRecord.BodyFace)
  public List<CornerReliefItem> Corners { get; } = new(); }

public enum FoldKind { Unfold, Fold }
public sealed class FoldRecord : FeatureRecord           // "UnFold" / "Fold"
{ public FoldKind Kind; public FaceSignature FixedFace = null!; public List<BendRef> Bends { get; } = new(); }

public sealed class LoftedBendRecord : FeatureRecord     // phase 2, "LoftedBend"
{ public List<string> ProfileSketches = new(); public double Thickness; public bool Direction, Formed, ReferToEndPoint;
  public double Radius; public int FacetOption; public double FacetValue; public int BendLineControl, NumberOfBendLines;
  public double MaxDeviation; }
```

Welded corner becomes an `UnsupportedRecord` (changes the solid) with reason "Verschweißte Ecke: keine API (bis 2024)".

## 3. Core rules (`Spiegelwerk.Core/Planning/SheetMetalBendRules.cs`) and matching

```csharp
public static class SheetMetalBendRules
{
    // PositionType (swFlangePositionTypes_e) → BendPos of InsertSheetMetal3dBend; null = cannot be created
    public static short? BendPosForCreation(int positionType) => positionType switch { 4 => 0, 1 => 1, 2 => 2, 3 => 3, _ => (short?)null };
    public static bool SketchedBendReverse(bool reverse, int sigma) => DirectionRules.ReverseFlag(reverse, sigma);
    public static Vec3 OtherSideOfLine(Vec3 p, Vec3 a, Vec3 b);          // reflect p across line ab, in its plane
    public static int SwapOverlap(int cornerType) => cornerType == 2 ? 3 : cornerType == 3 ? 2 : cornerType;
    public static (double length2, double length3) LegacyReliefLengths(CornerReliefItem c); // for AddCornerReliefType
    public static Vec3? ChooseFixedPoint(Vec3 asSketch, double dSketch, Vec3 asModel, double dModel, double tol);
}
// Matching/BendMatcher.cs
public static MatchResult FindBend(IReadOnlyList<FaceSignature> wanted,
    IReadOnlyList<IReadOnlyList<FaceSignature>> candidates, Func<int,int,double> sampleDistance /*cand i, wanted j*/,
    Func<int,bool>? preferred = null);
// A candidate matches only if every wanted face matches one of its faces (TopologyMatcher.FindFace rules).
```

For `LegacyReliefLengths`: square or obround give L2 = SlotLength; circular gives L2 = SlotWidth (radius semantics [?]); obround gives L3 = SlotWidth; square with fillets gives L3 = CornerFilletDiameter / 2.

## 4. Capture (`Capture/FeatureCapture.SheetMetalBends.cs`)

Add these cases to the `switch (def)` in `FeatureCapture.Capture`. Each one reads geometry inside AccessSelections and releases in `finally`.

```csharp
case SketchedBendFeatureData sb: return CaptureSketchedBend(f, type, sb);
case CrossBreakFeatureData cb:   return CaptureCrossBreak(f, type, cb);
case ClosedCornerFeatureData cc: return CaptureClosedCorner(f, type, cc);
case BreakCornerFeatureData bc:  return CaptureBreakCorner(f, type, bc);   // Kind from type ("CornerTrim")
case FoldsFeatureData fd:        return CaptureFold(f, type, fd);
case LoftedBendsFeatureData _:   throw new NotSupportedException("Ausgeformte Biegung: noch nicht parametrisch.");
// after the switch, before UnsupportedRecord:
if (def != null && SwLate.Get(def, "CornerCount") != null) return CaptureCornerRelief(f, type, def);
```

**`CaptureSketchedBend`**
- `sketch = SketchOf(f)` (absorbed `ProfileFeature`; codestack confirms).
- Read `BendAngle`, `UseDefaultBendRadius`, `BendRadius`, `PositionType`, `ReverseDirection`, `UseDefaultBendAllowance`; if not default, also `GetCustomBendAllowance()`.
- If `BendPosForCreation(PositionType) == null`, throw `NotSupported`.
- Under AccessSelections: `face = (Face2)d.GetFixedFace(out x, out y, out z)` and `FixedFace = TopologyCapture.Face(face)`. Interpret the point as `asSketch = _math.SketchFrame((Sketch)sketch.GetSpecificFeature2()).ToWorld(x, y)` and as `asModel = (x, y, z)`; `FixedPoint = ChooseFixedPoint(…, DistanceToFace…)`. If it is null, add a note.
- `BendCount` = sub-features of type `SketchBend`.
- If `SwLate.Get(d, "UseGaugeTable") is true`, add the note "Blechstärkentabelle wird nicht übernommen".

**`CaptureCrossBreak`**: `BreakAngle`, `BreakRadius`, `ReverseDirection`; `Face` read under access. If the cross break owns a `ProfileFeature` sub-feature [?], mark it as `PartOfFeatureRecord` by generalising `HolePart`, otherwise an orphan sketch is rebuilt (Stolperstein 72).

**`CaptureClosedCorner`**: `CornerType`, `GapDistance`, `OverlapUnderlapRatio`, `OpenBendRegion`; `Faces` read under access, keeping order. With no faces, throw `NotSupported`.

**`CaptureBreakCorner`**
- `BreakType`, `Distance`, `InternalCornersOnly`, `CenteredOnBendLines`; `Entities` under access, `Edge` → `EdgeSignature`, `Face2` → `FaceSignature`.
- For `CornerTrim`, also read the owner Flat-Pattern's `IFlatPatternFeatureData.CornerTrim*`. If the trim is the one returned by `GetAddCornerTrim` / `GetBreakCorners`, it belongs to the flat-pattern record instead.
- Capture runs inside the flat-pattern scope.

**`CaptureFold`**
- `Kind` from the type; under access: `FixedFace` → signature.
- For each `Feature b` in `(object[])d.Bends`: `owner = b.GetOwnerFeature()` [I]; `ordinal` = index of `b` among `SubFeatures(owner)` whose definition `is OneBendFeatureData`; `Faces` = `b.GetFaces()` → signatures, read in this rolled-back state (folded for UnFold, flat for Fold).

**`CaptureCornerRelief`** (late-bound, SW 2022+; on 2019–2021 it falls through to Unsupported "Eckfreistellung erst ab SOLIDWORKS 2022 lesbar")
- `SwLate.Call(def, "AccessSelections", _doc, null)`; `CornerType`.
- `GetBodyScope()` → a `Body2` face not made by this feature → `BodyFace`.
- `GetCorners(-1)` → for each corner: its properties, then `GetFaces` via `InvokeOut`.
- `finally ReleaseSelectionAccess`.

**`MirrorJob`**
- Add to `Builders`: `SketchedBendBuilder`, `CrossBreakBuilder`, `ClosedCornerBuilder`, `BreakCornerBuilder`, `CornerReliefBuilder`, `FoldBuilder`.
- Pre-pass: `ctx.FaceSupportedSketches.Add(sb.SketchName)`.
- `MassCapture` needs no change; cross break is skipped because `ChangesSolid` is false.
- Corner-trim mass needs the flat-pattern scope; without it, only values are compared.

## 5. Builders

All builders follow the same skeleton: resolve references first; in SeparateBody mode reject references that are not on an own body; create; finish in one `FeatureEdit`; `ctx.Register`; read back and compare; add alternatives.

### 5.1 SketchedBendBuilder
1. `ctx.Sketches[r.SketchName]`, otherwise fail. `pos = BendPosForCreation(r.PositionType)`.
2. `fixed = Topology.FindFace(r.FixedFace.Mirror(ctx.Mirror))`. Fail if missing; no plane substitute.
3. `pick = FixedPoint.HasValue ? Mirror.Apply(FixedPoint) : TopologyCapture.Face(fixed).Sample`.
4. `cba = UseDefaultBendAllowance ? null : fm.CreateCustomBendAllowance()`, filled from the spec.
5. Clear the selection. Select the sketch feature (mark 0). `SelectFaceAt(ctx, fixed, pick, 0, append: true)`. Then `fm.InsertSheetMetal3dBend(r.Angle, r.UseDefaultRadius, r.Radius, rev, pos, cba)` with `rev = SketchedBendReverse(r.Reverse, sketch.Sigma)`.
   - Second try (late-bound): `CreateDefinition(35)` → set `BendAngle`, `UseDefaultBendRadius`, `BendRadius`, `ReverseDirection`, `SetCustomBendAllowance` → `CreateFeature`.
6. Always run `FeatureEdit.SketchedBend(d => { d.ReverseDirection = rev; if (d.PositionType != r.PositionType) d.PositionType = r.PositionType; })`, because `FlipDir` is ignored [M]. Read back angle, radius, position and allowance. Because allowance changes fail [M], it is passed only at creation; a mismatch becomes a note.
7. If `FixedPoint` was null and the mirrored `FaceSamples` miss: delete the bend and recreate it picking `OtherSideOfLine(pick, mirrored bend line)`; keep the variant with fewer misses. This reuses `StepVerifier.MissedSamples` made internal, and follows the `AdvancedHoleBuilder` precedent.
8. Alternative: "Biegerichtung umgekehrt", which toggles `ReverseDirection` via `FeatureEdit`.

### 5.2 CrossBreakBuilder
- Find the face (own body), `SelectFaceAt(face, sample, 0)`, `fm.InsertCrossBreak(Angle, Radius)`.
- `FeatureEdit.CrossBreak(d => d.ReverseDirection = r.Reverse)`; read back angle, radius, reverse, and that the face is on an own body.
- No alternatives: there is no mass change to measure. The report says "Richtung nach Regel, nicht messbar".

### 5.3 ClosedCornerBuilder
- Resolve all faces; one missing means fail, since another corner would be closed.
- Try these selection variants in order, each followed by `doc.InsertSheetMetalClosedCorner()` and `LastAddedOf<ClosedCornerFeatureData>`:
  - (a) all faces with mark 1;
  - (b) pairs: even index mark 1, odd index mark 0x40000000 (official example);
  - (c) all with mark 0.
- After each, read back `Faces` against the mirrored signatures as a set. If they differ, delete and try the next variant.
- Then `FeatureEdit.ClosedCorner` with `CornerType`, `GapDistance`, `OverlapUnderlapRatio`, `OpenBendRegion`. If that fails, delete and fail.
- Alternative for types 2 and 3: "Überlappung ↔ Unterlappung", toggled with `SwapOverlap`.

### 5.4 BreakCornerBuilder
- **BreakCorner**: resolve all edges and faces (fail if any is missing), select them all with mark 0, `doc.InsertSheetMetalBreakCorner(BreakType, Distance)`, `LastAddedOf<BreakCornerFeatureData>`. `FeatureEdit` sets `InternalCornersOnly` and `CenteredOnBendLines` if they differ. Read back `GetEntitiesCount`.
- **CornerTrim**: built only through the flat-pattern scope hook. Select the edges, then `fm.InsertSheetMetalCornerTrim(Internal ? 1 : 0, BreakType, Distance, TrimReliefType, TrimReliefSize)`.

### 5.5 CornerReliefBuilder (needs a running SW 2022+; guaranteed, because capture succeeded in the same session)
- Find the body via `BodyFace` (the `ShellBuilder.ShelledBody` logic) and resolve every corner face.
- `def = fm.CreateDefinition(120)`; `Initialize(CornerType)`; `SetBodyScope(body)` must return 0. For each corner: `AddNewCorner(f1, f2, f3 ?? null, type, out c)` must return 0, then set the corner's properties. `fm.CreateFeature(def)`.
- Fallback if the definition is null: the 2019 selection path, two-bend corners only (body mark 0; faces mark 4; `AddCornerReliefCorner`; `AddCornerReliefType(-1, type, 0, L2, L3, center, ratio, tangent, filleted, false)`; `FinishCornerRelief`), with a note.
- Read back `GetCorners(-1)` and match each corner by its mirrored face pair.

### 5.6 FoldBuilder (Unfold and Fold)
- `fixed = FindFace(mirror)` (own body).
- For each bend: `Topology.FindBend(b.Mirror(R))`:
  - Candidates: every sub-feature of the target whose definition `is OneBendFeatureData`, with faces from `GetFaces()`, matched with `BendMatcher` (prefer bends owned by `ctx.Created[owner]`).
  - Fallback: `ctx.Created[owner]`'s sub-feature at index `ordinal`, with a note.
  - All bends are required.
- Selection variants: (fixed face mark 1, bends mark 4), then (1, 2). Then `doc.InsertSheetMetalUnfold()` or `InsertSheetMetalFold()`, and `LastAddedOf<FoldsFeatureData>`.
- Read back `FixedFace` and `Bends`. If they differ, `FeatureEdit.Folds(d => { d.FixedFace = fixed; d.Bends = bends.ToArray(); })`. If they still differ, delete and try the next variant.
- No alternatives.

### 5.7 LoftedBendBuilder (phase 2)
- Select the segments of both rebuilt sketches with mark 1, in original order.
- `InsertSheetMetalLoftedBend2(Direction ? 1 : 0 [?], Thickness, Formed, Radius, ReferToEndPoint, FacetOption, FacetValue…)`.
- Alternative: "Dickenrichtung umgekehrt".
- Recommendation: do not support it in the first round. The user calls it rare, and its thickness side and profile alignment need live work. Until then it is an `UnsupportedRecord`, which goes to the fallback, with the note that a formed lofted bend cannot be converted back by Insert Bends.

## 6. Mirror rules and why

| Feature | Rule | Why |
|---|---|---|
| Sketched bend | bend lines through the rebuilt sketch | same as every sketch (`SketchMirror`) |
| | fixed face → face found by its mirrored fingerprint | the target is exactly in the mirrored state before the bend (Stolperstein 28) |
| | fixed point → R(p) | the pick decides which side of the bend line stays still; R maps the fixed region onto the fixed region. It is a position, not an orientation, so σ plays no part. |
| | angle, radius, PositionType, bend allowance and K-factor unchanged | a reflection preserves lengths and angles. "Material inside/outside" and "bend outside" refer to the bend line and the moving flange, which map onto each other. |
| | `ReverseDirection' = ReverseDirection XOR (σ = −1)`, σ of the rebuilt bend sketch (`SketchMap.Sigma`) | "up" is taken as along the sketch normal (assumption, as for extrudes): R(bent dir) = cos θ·R(u) + s·sin θ·R(n), and the target normal is n′ = σ·R(n), so s′ = σ·s. With the sketch on the mirrored face, n′ is the outward normal R(n), so σ = +1 and the flag stays. If the convention is face-relative instead, the alternative fixes it; the moment of the change differs, so the mass check sees it. |
| | BendPos converted from PositionType (4 → 0) | two encodings [D][I][M] |
| Cross break | face mapped; Reverse, angle, radius unchanged | up/down is relative to the face's outward normal, and a reflection keeps outward normals outward |
| Closed corner | faces mapped in order; type, gap, ratio, open bend region unchanged | the face-to-extend / face-to-match roles follow the selected faces one to one |
| Break corner / corner trim | edges and faces mapped via `TopologyFinder`; type, distance and internal-only unchanged | concave/convex is mirror-invariant |
| Corner relief | Face1, Face2 and Face3 each mapped (order kept); all values unchanged | roles carried by the mapping |
| Unfold / Fold | fixed face mapped; bends mapped by their mirrored face fingerprints | nothing has a direction |

## 7. Verifier alternatives
- **SketchedBend**: "Biegerichtung umgekehrt". The fixed side is checked inside the builder only when the pick point was unknown, because `SetFixedFace(…, EdgeArray)` is undocumented [?].
- **ClosedCorner**: "Überlappung ↔ Unterlappung".
- **LoftedBend** (phase 2): "Dickenrichtung umgekehrt".
- **BreakCorner, CornerTrim, CornerRelief, Fold, CrossBreak**: no alternatives; values are read back and compared.
- **Mass check**:
  - Bends and unfold/fold barely change volume but clearly change the moment, which `MassComparison` compares separately.
  - Closed corners add material; break corners and reliefs remove it.
  - Cross break is checked by read-back only.

## 8. Core tests (`SheetMetalBendTests.cs`)
1. `Bend_position_maps_to_the_creation_code`: 4 → 0, 1 → 1, 2 → 2, 3 → 3; 0, 5 and 6 → null.
2. `Sketched_bend_direction_is_mirrored_whichever_way_the_copy_sketch_normal_points`: 300 random reflections. Model: dir = cos θ·u + s·sin θ·n; the copy normal is ±R(n); assert R(dir) = dir′.
3. `Other_side_of_line`: keeps the distance, lands on the opposite side, is its own inverse, and commutes with R.
4. `Fixed_point_takes_the_interpretation_that_lies_on_the_face`: sketch-space reading, model-space reading, or null.
5. `Overlap_and_underlap_swap_butt_stays`.
6. `Legacy_corner_relief_lengths`: per relief type; fillet diameter becomes a radius.
7. `BendRef_mirror_mirrors_every_face_and_keeps_owner_and_ordinal`.
8. `Bend_matcher`: a full match wins; a partial match is rejected; own bodies are preferred; two equal candidates are flagged ambiguous.
9. `Corner_relief_item_mirror_keeps_face_roles`.

## 9. Live-test assumptions (go into the Stolpersteine table)
1. Type names of corner relief and welded corner. Corner trim is a sub-feature of Flat-Pattern.
2. The `GetFixedFace` point is in sketch space (capture validates against the face, with a model-space fallback).
3. `InsertSheetMetal3dBend` ignores `FlipDir`, and `ModifyDefinition(ReverseDirection)` works (eyfel). Allowance changes after creation fail (CADapter, 2017).
4. A bend sketch on a coplanar reference plane is accepted, or the face support from section 1.6 is required.
5. Sketched-bend "reverse" is relative to the sketch normal (σ rule) or relative to the face.
6. Closed corner:
   - Mark 1 works for faces to extend; 0x40000000 is "faces to match".
   - What `Faces` returns: faces to extend only, or pairs.
   - The newer options (faces to match, coplanar, narrow corner, auto propagation) are not in the API, so defaults are used.
7. Break corner: mark 0 for edges and faces; `Entities` reads back the same count.
8. Corner trim: needs the flat pattern unsuppressed; `InternalCornerFlag` semantics; several edges needed; relief settings come from `IFlatPatternFeatureData`.
9. Corner relief: calls through IDispatch with out parameters (`GetFaces`, `AddNewCorner`); `CreateDefinition(120)`; legacy marks (body 0, faces 4).
10. Unfold/Fold: bends with mark 4 vs 2; the `FixedFace` and `Bends` setters work through `ModifyDefinition`; `GetFaces` on a bend sub-feature inside AccessSelections returns its faces in that state.
11. Cross break: no mass change; whether it owns an editable sketch sub-feature.
12. Lofted bend: segment selection with mark 1; the `Direction` ↔ `ThickDirType` mapping.
13. Same part: closed and break corners act only on the selected entities; corner relief `SetBodyScope` on the mirrored sheet body.

## 10. Effort and order
- **Records**: 7 (`SketchedBend`, `CrossBreak`, `ClosedCorner`, `BreakCorner` with two kinds, `CornerRelief`, `Fold` with two kinds, `LoftedBend` in phase 2), plus 3 helpers (`BendAllowanceSpec`, `BendRef`, `CornerReliefItem`). Core rules plus `BendMatcher` ≈ 200 lines; tests ≈ 300 lines.
- **Builders**: 6 now (SketchedBend, CrossBreak, ClosedCorner, BreakCorner, CornerRelief, Fold) and 1 in phase 2 (LoftedBend).
- **Other add-in code**:
  - capture partial ≈ 350 lines;
  - builders ≈ 900 lines;
  - `FeatureEdit`, `SwLate` and `LastAddedOf` ≈ 120 lines;
  - `FindBend` ≈ 60 lines;
  - `SketchBuilder` face support ≈ 50 lines.
- **Total**: ≈ 1,800 lines, about 6–7 development days plus 2 days of live testing.
- **Implementation order**, by the user's usage:
  1. Sketched bend
  2. Closed corner
  3. Break corner
  4. Unfold/Fold
  5. Corner relief (2022+)
  6. Cross break
  7. Corner trim (together with the flat-pattern area)
  8. Lofted bend
- **Welded corner**: Unsupported, because no API exists. Recommend reporting it as "add by hand" rather than triggering the whole-body fallback for one weld bead.

## Files
- Probe (reflection with `MetadataLoadContext`): `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/area3probe/Program.cs` (output in `area3probe/out`)
- Interop 29.4.1 and 30.5.1 used to date the corner-relief interface: `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/area3nupkg`
- Downloaded API docs and examples: `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/area3docs` (third-party sources in `area3docs/gh`: `cadapter_sm.py`, `eyfel_analyze.cs`, `swmcp_sm.py`)

Sources: [GetTypeName2 table (API help as markdown)](https://raw.githubusercontent.com/streetartist/solidworks-automation/main/docs/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~GetTypeName2.md), [closed corner example](https://raw.githubusercontent.com/f1owkang/solidworks-plugin-dev/main/references/source-docs/Feature/Create_and_Modify_Closed_Corner_Feature_Example_CSharp.md), [CADapter sw_sheetmetal.py](https://raw.githubusercontent.com/luigidoria/CADapter/main/solidworks/sw_sheetmetal.py), [eyfel Analyze.cs](https://raw.githubusercontent.com/eyfel/mcp-server-solidworks/main/execution/solidworks/SolidworksExecution/Services/SolidWorksService.Analyze.cs), [codestack sketched-bend macro](https://raw.githubusercontent.com/xarial/codestack/master/solidworks-api/document/sheet-metal/sketched-bend-feature-sketch-lines/Macro.vba), [SW_MCP sw_sheetmetal.py](https://raw.githubusercontent.com/d-veloping/SW_MCP/main/solidworks_mcp/sw_sheetmetal.py)