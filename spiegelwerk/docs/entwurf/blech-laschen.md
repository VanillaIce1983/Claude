# AREA 2 design: flanges on edges (edge flange, miter flange, hem, jog, swept flange)

All four flange types can be rebuilt parametrically on SOLIDWORKS 2019 without the Mirror API. Each needs its own creation path. Here is what I found:

- **Edge flange.** The profile sketch cannot be rebuilt as a free sketch. It has to be created on the mirrored edge with `InsertSketchForEdgeFlange`, filled with the mirrored profile, and then passed to the flange call.
- **Miter flange and jog.** Their sketches are ordinary user sketches. They are rebuilt like any other sketch and then selected for the creation call.
- **Hem.** It creates its own width sketch, which is overwritten afterwards only if the original's differs.
- **Calls that do not exist.** `InsertSheetMetalMiterFlange2/3` and `InsertSheetMetalJog2` exist in neither interop.
- **Calls that do exist.**
  - The miter flange uses `IFeatureManager.InsertSheetMetalMiterFlange`.
  - The jog uses only the old `IModelDoc2.InsertSheetMetalJog`, which returns nothing.
  - `CreateDefinition` has no IDs for miter flange, hem or jog.
- **Swept flange.** It is not in the 2019 API at all.
- **Nothing tested live.** No signature, mark or sign rule here has been checked in a running SOLIDWORKS. Every member name and signature below comes from reflection over the 27.3.0 interop (32.1.0 for comparison). Selection marks, call order and sign conventions come from third-party code measured on SOLIDWORKS 2024–2026, not 2019. They are listed in section 9.
- **No changes to the repository.** Nothing in /home/user/Claude was modified.

**Sources:**
- **Interop reflection:** a probe I wrote. help.solidworks.com could not be reached from the sandbox (DNS failure).
- **Copies of the current API help on GitHub:**
  - `kilwizac/solidworks-api-mcp`
  - `streetartist/solidworks-automation`
  - `f1owkang/solidworks-plugin-dev`
- **Third-party code with live measurements:**
  - `tbcphutai-bit/ADDIN-SolidWorks`, file `Commands/MirrorV7/MirrorInPlace/Replay/EdgeFlangeReplay.cs`. This is a mirror add-in that replays edge flanges, tested on 2024.
  - `eyfel/mcp-server-solidworks`, file `SolidWorksService.SheetMetal.cs` (2026).
  - `soheil-mp/solidworks-mcp`, file `docs/solidworks-api-notes.md` (2026, marked VERIFIED there).
  - `luigidoria/CADapter`, file `solidworks/sw_sheetmetal.py` (2026, marked MEASURED there).

## 1. API inventory

### 1.1 Type names and data interfaces

| Feature | GetTypeName2 | Data interface (27.3.0) | Sub-features |
|---|---|---|---|
| Edge flange | `"EdgeFlange"` | `IEdgeFlangeFeatureData` | one `ProfileFeature` per edge, created by the feature and editable; bends `"OneBend"` |
| Miter flange | `"SMMiteredFlange"` | `IMiterFlangeFeatureData` | the user's open-profile `ProfileFeature`, absorbed; one bend per edge |
| Hem | `"Hem"` | `IHemFeatureData` | its own sketch ("Edit Hem Width"); `HemBend` holds the faces, according to soheil's notes |
| Jog | `"Jog"` | `IJogFeatureData` | the user's sketch with one line, absorbed |
| Swept flange | `"SweptFlange"` (seen only in community code) | `ISweptFlangeFeatureData`: **only in 32.1.0** | profile and path |

The first four strings appear in the copy of the GetTypeName2 help page and in the official example "Get All Sheet Metal Feature Data". Capture should dispatch on the type returned by `GetDefinition()`, as `FeatureCapture` already does, and not on these strings.

### 1.2 Members in 27.3.0

**`IEdgeFlangeFeatureData`**

Properties:
- `object Edge {get;set;}` (obsolete)
- `object Edges {get;set;}`
- `bool UseDefaultBendRadius`, `double BendRadius`, `double BendAngle`
- `int OffsetType` (`swFlangeOffsetTypes_e`)
- `object OffsetReference`, `bool ReverseOffset`
- `double OffsetDistance` (the flange length)
- `int OffsetDimType` (`swFlangeDimTypes_e`)
- `int PositionType` (`swFlangePositionTypes_e`)
- `bool UsePositionTrimSideBends`, `bool UsePositionOffset`
- `int PositionOffsetType`, `object PositionOffsetReference`, `double PositionOffsetDistance`, `bool ReversePositionOffset`
- `int AutoReliefType` (`swSheetMetalReliefTypes_e`), `bool UseReliefRatio`, `double ReliefRatio`, `double ReliefWidth`, `double ReliefDepth`
- `int ReliefTearType` (`swReliefTearTypes_e`: Rip=1, Extend=2)
- `bool UseDefaultBendAllowance`, `bool UseDefaultBendRelief`
- `double GapDistance`

Methods:
- `bool AccessSelections(object TopDoc, object Component)`, `void ReleaseSelectionAccess()`
- `int GetPositionReferenceType()`
- `CustomBendAllowance GetCustomBendAllowance()`, `void SetCustomBendAllowance(CustomBendAllowance)`
- `int GetEdgeCount()`
- `int AddEdges(object EdgeArray, object SketchArray)`, which returns `swEdgeFlangeError_e`
- `int RemoveEdges(object EdgeArray)`

There is **no getter for the profile sketch**.

32.1.0 adds `object AngleReference`, `bool LockAngle`, `bool PerpendicularToFace`, `bool NormalToFlangePlane` and `swFlangeOffsetUptoEdgeAndMerge = 6`. These must be read and written late-bound through `SwLate`.

**`IMiterFlangeFeatureData`**

`object Edges`, `bool UseDefaultBendRadius`, `double BendRadius`, `int PositionType`, `bool UsePositionTrimSideBends`, `double GapDistance`, `double StartOffset`, `double EndOffset`, `int ReliefType`, `bool UseReliefRatio`, `double ReliefRatio`, `ReliefWidth`, `ReliefDepth`, `bool UseDefaultBendAllowance`, `bool UseDefaultBendRelief`, `AccessSelections`/`ReleaseSelectionAccess`, `IGetEdgesCount()`, `Get/SetCustomBendAllowance`. No sketch getter.

**`IHemFeatureData`**

`object Edges`, `double MiterGap`, `bool ReverseDirection`, `int BendPosition` (`swHemPositionTypes_e`: Inside=0, Outside=1), `int Type` (`swHemTypes_e`: Open=0, Closed=1, TearDrop=2, Rolled=3, Double=4), `double Length`, `double GapDistance`, `double Angle` (radians), `double Radius`, `bool UseDefaultBendAllowance`, `GetEdgesCount()`, `Get/SetCustomBendAllowance`.

There are **no relief members**, so a hem's relief cannot be read back.

**`IJogFeatureData`**

`object FixedFace`, `bool UseDefaultBendRadius`, `double BendRadius`, `double JogAngle`, `int OffsetType` (`swJogOffsetTypes_e`: Blind=1, UpToVertex=2, UpToSurface=3, FromSurface=4, MidPlane=5), `object OffsetReference`, `bool ReverseOffset`, `bool ReverseDirection`, `double OffsetDistance`, `int DimensionPositionType` (`swJogDimensionPositionType_e`: InsideOffset=1, OutsideOffset=2, OverallPosition=3), `bool FixProjectedLength`, `int JogPositionType` (`swJogPositionType_e`: BendCenterline=1, MaterialInside=2, MaterialOutside=3, BendOutside=4), `object FixedPoint` (plus `double IGetFixedPoint()`), `bool UseDefaultBendAllowance`, `int GetOffsetReferenceType()`, `Get/SetCustomBendAllowance`. No sketch getter.

**Other members used**

- `CustomBendAllowance`: `int Type` (`swBendAllowanceTypes_e`: BendTable=1, KFactor=2, Direct=3, Deduction=4, BendCalculationTable=5), `string BendTableFile`, `double KFactor`, `double BendAllowance`, `double BendDeduction`.
- `IFeatureManager.CreateCustomBendAllowance()`.
- `ISketch.GetReferenceEntity(ref int LEntityType) → object`.
- `IFeature.GetOwnerFeature()`.
- `IEdge.GetTwoAdjacentFaces2()`.

**Enums**

- `swFlangePositionTypes_e`: MaterialInside=1, MaterialOutside=2, BendOutside=3, BendCenterLine=4, BendSharp=5, BendTangent=6.
- `swFlangeOffsetTypes_e`: Blind=1, UpToVertex=2, UpToSurface=3, FromSurface=4, MidPlane=5, plus 6 in 32.1.0.
- `swFlangeDimTypes_e`: OuterVirtualSharp=1, InnerVirtualSharp=2, BendTangentArc=3.
- `swInsertEdgeFlangeOptions_e`: UseDefaultRadius=1, FlipDir=2, DoOffset=4, ReverseOffsetDir=8, TearClip=16, TrimSideBend=32, UseReliefRatio=64, UseDefaultRelief=128.
- `swSheetMetalReliefTypes_e`: Rectangular=1, Tear=2, Obround=3, None=4, TearBend=5.
- `swEdgeFlangeError_e`: 0 NoError … 3 NumberOfEdgesAndSketchesNotEqual, 4 EdgeAlreadyExists, 5 InvalidEdge.

### 1.3 Creation calls

| Call (27.3.0) | Returns | Notes |
|---|---|---|
| `IModelDoc2.InsertSketchForEdgeFlange(object FlangeEdge, double DAngle, bool FlipDir)` | `object`, the sketch as a Feature | Creates the profile sketch on the flange plane through the edge. The help says to draw the profile afterwards. Third parties disagree on whether the sketch comes back empty (CADapter) or with a default rectangle (eyfel), and on whether it is left open for editing. Handle both. |
| `IFeatureManager.CreateDefinition((int)swFmEdgeFlange /*37*/)` → `IEdgeFlangeFeatureData.AddEdges(Edge[], Sketch[])` → `CreateFeature(data)` | `Feature` | The current help calls this the supported path and lists `swFmEdgeFlange` under CreateDefinition. The help example passes typed `Sketch[]`. tbcphutai confirmed it on 2024 with typed arrays. Whether 2019 supports it is **not verified**: the enum value 37 exists in 27.3.0. |
| `IFeatureManager.InsertSheetMetalEdgeFlange2(object FlangeEdges, object SketchFeats, int BooleanOptions, double FlangeAngle, double FlangeRadius, int BendPosition, double FlangeOffsetDist, int ReliefType, double FlangeReliefRatio, double FlangeReliefWidth, double FlangeReliefDepth, int FlangeSharpType, CustomBendAllowance CustomBendAllowance)` | `Feature` | Marked obsolete in the current help, but it works with profile sketches (eyfel on 2026, soheil). It returns null without sketches. With a sketch the length comes from the sketch, not from `FlangeOffsetDist` (eyfel), so `OffsetDistance` is set afterwards. Position-offset distance and reference are not parameters of this call. |
| `IFeatureManager.InsertSheetMetalEdgeFlange(Edge, Feature, int, double, double, int, double, int, double, double, double, int, CustomBendAllowance)` | `Feature` | Single edge. Last resort (CADapter uses it). |
| `IFeatureManager.InsertSheetMetalMiterFlange(bool UseDefaultRadius, double GlobalRadius, double RipGap, bool UseDefaultRelief, bool UseReliefRatio, double ReliefRatio, double ReliefWidth, double ReliefDepth, int ReliefType, bool TrimSideBends, int FlangePos, double OffsetDist1, double OffsetDist2, CustomBendAllowance PCBA)` | `Feature` | Selection: **sketch on mark 0, edges on mark 1** (soheil, VERIFIED 2026-10-24, chains of 2 to 4 edges work). With the edge on mark 0, CADapter got only one edge. The profile must touch the edge's end vertex. |
| `IFeatureManager.InsertSheetMetalHem2(int Type, int Position, bool Reverse, double DLength, double DGap, double DAngle, double DRad, double DMiterGap, CustomBendAllowance PCBA, bool UseDefaultRelief, int ReliefType, int ReliefTearTypes, bool UseReliefRatio, double ReliefRatio, double ReliefWidth, double ReliefDepth)` | `Feature` | Edges preselected on **mark 0** (soheil, VERIFIED). A teardrop at 270° returned nothing in soheil's tests. |
| `IModelDoc2.InsertSheetMetalJog(double Angle, double Radius, double OffsetDist, bool FlipDir, bool FixProjLen, short DimPos, short BendPos)` | `void` | The only jog call; no newer version exists in 27.3.0 or 32.1.0. Selection: the **line on mark 0**, then the **fixed face picked at a point off the line** (append, mark 0). CADapter measured that **the angle must be in degrees** (radians did nothing) and that the encodings of `DimPos`/`BendPos` are unclear. soheil could not get it to work. |

## 2. Cross-cutting design decisions

**A. Sketches that belong to their feature.** This generalizes `PartOfFeatureRecord`.

- Add `public interface IOwnsSketches { IEnumerable<string> OwnedSketches { get; } }` to Core Records. `PositionedHoleRecord` implements it too, as a refactor.
- Edge-flange profiles and the hem's width sketch become `PartOfFeatureRecord(name, type, owner)`. `FeatureCapture.HolePart` becomes `OwnedPart`: the one-time tree walk maps the sketch sub-features of `EdgeFlange` and `Hem` features (plus sketch parents of an edge flange; tbcphutai observed a profile shown at top level before its flange) to their owner.
- In `MirrorJob`, the `holes` set becomes `records.OfType<IOwnsSketches>()`. `RemoveAgain` deletes with `swDelete_Absorbed` and calls `ForgetSketch` for each owned name. `WithdrawParts` stays as it is.
- When the owner is not rebuilt, `CaptureOnItsOwn` returns `UnsupportedRecord(changesSolid:false, "Profilskizze der Kantenlasche „X“ … entfällt mit ihr")`.
- Miter-flange and jog sketches remain ordinary `SketchRecord`s, built by `SketchFeatureBuilder` first and then selected, like extrude sketches.

**B. How the edge-flange profile is rebuilt.**

1. Capture each profile's `SketchModel` together with its hinge edge.
2. In the target, call `InsertSketchForEdgeFlange(mirroredEdge, θ, flip)`. Try (θ,false), (θ,true), (π−θ,false), (π−θ,true), deduplicated for θ = 90°. Keep the first sketch whose frame satisfies `FlangeProfiles.FitsFrame` (Core):
   - the plane contains R(hinge);
   - the plane normal is perpendicular to R(d), where d is the flange direction (the unit in-plane vector from the hinge toward the profile centroid);
   - the sketch Y axis points along R(d). tbcphutai measured that Y must point along the flange.

   Delete the others.
3. Write the profile with a new `SketchBuilder.ReplaceInto(SketchModel, Feature existing, notes)`. It works like `BuildInto` but deletes all existing segments and user points **before** writing. The source model is first rebased by `FlangeProfiles.RebaseOntoHinge`, which shifts the frame origin along its normal so the plane contains the hinge again; this removes the position-offset translation. `SketchMirror` then maps into whatever frame SOLIDWORKS created. Relations to the hinge (USEEDGE becomes collinear, endpoints coincident with vertices) resolve through existing `ModelEdge`/`ModelVertex` references.
4. Create the flange from these sketches.
5. **Check after the final edit.** CreateFeature, AddEdges and ModifyDefinition can regenerate absorbed profiles (tbcphutai). Compare the model-space points of each new profile with R(source points). On a mismatch, toggle `ReverseOffset`, then `ReversePositionOffset` (only if `UsePositionOffset`), then both, each in its own FeatureEdit plus rebuild. Keep the first combination that matches. If none matches, restore and warn.
6. Register each profile under its original name with `ctx.Register` and `ctx.Sketches[...]`, so equations like `D1@Skizze7` still resolve.

**C. Shared value classes and helpers.**

- Core: `ReliefSettings`, `BendAllowanceSettings`.
- Add-in: a new `Sw/SwSheetMetal.cs` with `ReadAllowance(CustomBendAllowance)`, `NewAllowance(FeatureManager, BendAllowanceSettings)`, and `ProfilePoints(Sketch)` (model coordinates of non-construction segment endpoints, using `SwMath.SketchFrame`).
- `FeatureEdit` gets four new methods: `EdgeFlange`, `MiterFlange`, `Hem`, `Jog`, each over the coclass interfaces `EdgeFlangeFeatureData`, `MiterFlangeFeatureData`, `HemFeatureData`, `JogFeatureData`.

**D. Read back and compare.**

- After building, read the data again into a fresh record using the same capture helper, then call `SheetMetalParams.Differences(expected, actual)` (Core). Every difference becomes a note and the status becomes Warning. This catches bend radius, K-factor and relief differences, which the mass check cannot see.
- Structural counts must match the original: number of edges, and number of `OneBend` sub-features (`BendCount`).

**E. MassCapture face samples.** The hem's faces belong to `HemBend`, not to the `Hem` feature. When `GetFaces()` returns fewer than 2 faces, or the record is a sheet-metal flange record, also sample faces of the non-sketch sub-features. This is about 15 lines in `SampleFaces`. `StepVerifier` already falls back to all body faces, so it needs no change.

**F. Dependency: sketching on a face.** The jog line must lie on the fixed face. `SketchBuilder` sketches only on planes, through `PlaneResolver`. Whether SOLIDWORKS accepts a jog from a sketch on a coplanar reference plane is unknown. CADapter writes: "it HAS to be the face the sketch was created on."

Proposal:
- `SketchCapture` records `SketchModel.SupportFace` (`FaceSignature?`) from `ISketch.GetReferenceEntity` when that returns a `Face2`.
- `SketchBuilder.Build` opens the sketch on the mirrored face when the record is flagged `OnSupportFace`. `MirrorJob` sets that flag for sketches consumed by Jog (and by sketched bends from another area).
- On a face, σ = +1 by construction, because outward normals stay outward.

## 3. Records

New file `src/Spiegelwerk.Core/Records/SheetMetalFlangeRecords.cs`.

```csharp
public sealed class BendAllowanceSettings          // null on a record = UseDefaultBendAllowance
{ public int Type; public double KFactor, Allowance, Deduction; public string TableFile = ""; }

public sealed class ReliefSettings
{ public bool UseDefault = true; public int Type; public int TearType; public bool UseRatio; public double Ratio, Width, Depth; }

/// <summary>Profile sketch of one edge of an edge flange; made by the flange, editable ("Edit Flange Profile").</summary>
public sealed class FlangeProfile
{
    public FlangeProfile(string sketchName, SketchModel sketch, EdgeSignature hinge) { ... }
    public string SketchName { get; }          // original name; the rebuilt profile gets it again
    public SketchModel Sketch { get; }         // as SOLIDWORKS shows it (position offset included)
    public EdgeSignature Hinge { get; }        // read under AccessSelections (state before the flange)
}

public sealed class EdgeFlangeRecord : FeatureRecord, IOwnsSketches
{
    public List<FlangeProfile> Profiles { get; } = new();   // order of IEdgeFlangeFeatureData.Edges
    public double BendAngle;
    public bool UseDefaultRadius; public double BendRadius;
    public int OffsetType; public double OffsetDistance; public int OffsetDimType; public bool ReverseOffset;
    public GeometryRef? OffsetReference;                     // UpToVertex/UpToSurface/FromSurface; edge for type 6
    public int PositionType; public bool TrimSideBends;
    public bool UsePositionOffset; public int PositionOffsetType; public double PositionOffsetDistance;
    public bool ReversePositionOffset; public GeometryRef? PositionOffsetReference;
    public double GapDistance;
    public ReliefSettings Relief = new();
    public BendAllowanceSettings? Allowance;
    public bool? LockAngle, NormalToFlangePlane, PerpendicularToFace;  // null: not readable (2019)
    public GeometryRef? AngleReference;
    public int BendCount;                                    // OneBend sub-features of the original
    public IEnumerable<string> OwnedSketches => Profiles.Select(p => p.SketchName);
}

public sealed class MiterFlangeRecord : FeatureRecord
{
    public string SketchName;                               // user's open profile, rebuilt as SketchRecord
    public List<EdgeSignature> Edges { get; } = new();      // chain order
    public bool UseDefaultRadius; public double BendRadius;
    public int PositionType; public bool TrimSideBends; public double GapDistance;
    public double StartOffset, EndOffset;
    public ReliefSettings Relief = new(); public BendAllowanceSettings? Allowance;
    public int BendCount;
}

public sealed class HemRecord : FeatureRecord, IOwnsSketches
{
    public List<EdgeSignature> Edges { get; } = new();
    public int HemType; public int Position; public bool Reverse;
    public double Length, Gap, Angle, Radius, MiterGap;
    public BendAllowanceSettings? Allowance;                // relief: not readable (IHemFeatureData has none)
    public string? WidthSketchName; public SketchModel? WidthSketch;
    public IEnumerable<string> OwnedSketches => WidthSketchName == null ? Enumerable.Empty<string>() : new[] { WidthSketchName };
}

public sealed class JogRecord : FeatureRecord
{
    public string SketchName; public string LineId;          // the one non-construction line
    public FaceSignature FixedFace; public Vec3? FixedPoint;
    public bool UseDefaultRadius; public double BendRadius; public double JogAngle;
    public int OffsetType; public double OffsetDistance; public GeometryRef? OffsetReference;
    public bool ReverseOffset, ReverseDirection;
    public int DimensionPosition; public bool FixProjectedLength; public int JogPosition;
    public BendAllowanceSettings? Allowance;
}
```

New Core planning classes:

- **`Planning/FlangeProfiles.cs`**
  - `Vec3 Direction(IEnumerable<Vec3> profilePoints, EdgeSignature hinge)`
  - `Frame3 RebaseOntoHinge(Frame3 f, EdgeSignature hinge)`
  - `bool FitsFrame(SketchModel src, EdgeSignature hinge, Reflection r, Frame3 target, double tol)`
  - `double Deviation(IReadOnlyList<Vec3> expected, IReadOnlyList<Vec3> actual)`: maximum over expected points of the minimum distance, compared as a set.
  - `int[]? Assign(IReadOnlyList<EdgeSignature> hinges, IReadOnlyList<ProfileCandidate> profiles)`: unique injective assignment, null when missing or ambiguous. Evidence in order: the support edge from `GetReferenceEntity` matches; a non-construction profile line overlaps the hinge; the frame X axis is parallel to the hinge and the hinge lies within the plane offset.
  - `bool IsFullEdgeLine(SketchModel s, EdgeSignature e)`: used for the hem width sketch.
- **`Planning/SheetMetalParams.cs`**
  - `List<string> Differences(FeatureRecord expected, FeatureRecord actual)`, per record type, with tolerances `Tol.Length` and 1e-9 rad.
  - `bool JogReverseFirstGuess(bool flag, int sigma)`, which forwards to `DirectionRules.ReverseFlag`.

## 4. Capture outline

New file `Capture/FeatureCapture.SheetMetal.cs`. Added cases in the `Capture` switch, placed before the generic fallback:

```csharp
case EdgeFlangeFeatureData ef: return CaptureEdgeFlange(f, type, ef);
case MiterFlangeFeatureData mf: return CaptureMiterFlange(f, type, mf);
case HemFeatureData hm:        return CaptureHem(f, type, hm);
case JogFeatureData jg:        return CaptureJog(f, type, jg);
// before the switch: if (type == "SweptFlange") return new UnsupportedRecord(..., "Gesweepte Lasche: API erst ab 2020 …", true);
```

**`CaptureEdgeFlange`**

1. `AccessSelections`. `Edges` becomes a list of `EdgeSignature`; any non-line edge raises `NotSupportedException` ("Kantenlasche an gekrümmter Kante"), which leaves it to the fallback.
2. Read all scalars listed in §1.2.
3. References through a `FlangeRef(object)` helper, which is like `ToGeometryRef` but keeps a straight edge as `ToEdge` instead of `Axis` (needed for type 6). Do this for `OffsetReference` when `OffsetType` is 2, 3, 4 or 6, and for `PositionOffsetReference`.
4. `Allowance = UseDefaultBendAllowance ? null : SwSheetMetal.ReadAllowance(d.GetCustomBendAllowance())`.
5. Late-bound reads with `SwLate.Get(d, "LockAngle" | "NormalToFlangePlane" | "PerpendicularToFace" | "AngleReference")`.
6. `ReleaseSelectionAccess`.
7. Outside the selection access, read each candidate profile sketch with `ReadSketch(sketch)` (this moves the rollback bar to just after the sketch) and its support edge with `GetReferenceEntity`. Pair them with `FlangeProfiles.Assign`; null raises `NotSupportedException("Zuordnung Profilskizze ↔ Kante nicht eindeutig")`.
8. `BendCount` = number of `OneBend` sub-features.

**`CaptureMiterFlange`**

- `SketchName = SketchOf(f).Name`.
- `Edges` under access, kept in order.
- Scalars, `Allowance`, `BendCount`.

**`CaptureHem`**

- Edges and scalars under access.
- Width sketch = `SubFeatures(f).FirstOrDefault(IsSketch)`, read with `ReadSketch` outside the access.

**`CaptureJog`**

- `SketchName`.
- `LineId` = the single non-construction line in the `SketchModel`. Anything else raises `NotSupportedException`.
- Under access: `FixedFace` (Face2) becomes a `FaceSignature`; `FixedPoint` as `double[3]` is assumed to be in model coordinates (live test); the scalars; `OffsetReference` when `OffsetType` is 2, 3 or 4.

## 5. Builder outlines

All four are registered in `MirrorJob.Builders` after `DraftBuilder`. All references are resolved before anything is created, through `ctx.Topology.FindEdge/FindFace(sig.Mirror(ctx.Mirror))` and `BuilderSupport.Find`, then wrapped with `Safe()`. Several features are created in between, so the safe entities must survive those rebuilds.

In separate-body mode, every edge and face must lie on a body this run created (`ctx.IsOwnBody(face.GetBody())`); otherwise the build fails. For `OffsetType == 6` (up to edge and merge), the target edge must also be on an own body, so the flange never merges into the original. Sheet-metal flanges have no feature scope, so the existing `OriginalUntouched` guard is the safety net.

**EdgeFlangeBuilder** (about 400 lines)

1. Resolve the hinges, `offsetRef`, `posRef`, and `angleRef` (late-bound).
2. For each profile:
   - find a frame-fitting sketch with `InsertSketchForEdgeFlange` (§2B);
   - if `ActiveSketch != null`, close it with `InsertSketch(true)`; otherwise `Select2` and `EditSketch` inside `ReplaceInto`;
   - call `ReplaceInto`.

   If any step fails, delete the sketches already made and fail.
3. Create:
   - **Path A:** `CreateDefinition(swFmEdgeFlange) as EdgeFlangeFeatureData`. If it is non-null, call `AddEdges(Edge[] , Sketch[])` and require 0; set all scalars and references; for type 6 set `LockAngle=false` before `BendAngle` and restore it afterwards (help remark); then `CreateFeature`. Do not read `Edges` back before `CreateFeature`; it returned null on 2024.
   - **Path B (A unavailable or failed):** `InsertSheetMetalEdgeFlange2(Edge[], Feature[] (then Sketch[]), opts, BendAngle, BendRadius, PositionType, OffsetDistance, Relief.Type, Ratio, Width, Depth, OffsetDimType, cba)`. The options are UseDefaultRadius, FlipDir=ReverseOffset, DoOffset=UsePositionOffset, ReverseOffsetDir=ReversePositionOffset, TrimSideBend, UseReliefRatio and UseDefaultRelief; the flag mapping is an assumption. Follow with **one** `FeatureEdit.EdgeFlange` that sets every value, including `OffsetDistance`, `PositionOffsetDistance`/references and the late-bound members.
   - **Path C:** single edge with `InsertSheetMetalEdgeFlange`.

   The report names the path taken.
4. In a separate FeatureEdit, set `ReverseOffset` and `ReversePositionOffset` again (tbcphutai, 2024: when set in the same transaction they read back correctly while the geometry stayed reversed).
5. Profile check and toggles (§2B step 5).
6. Read back, compare edge count and `BendCount`.
7. Register the flange and the profiles.
8. Alternatives only if step 5 could not confirm (§7).

**MiterFlangeBuilder** (about 180 lines)

1. `ctx.Sketches[r.SketchName]` must exist; every edge must be found.
2. Select the sketch feature with mark 0 and the edges in order with mark 1, appended. Call `InsertSheetMetalMiterFlange(UseDefaultRadius, BendRadius, GapDistance, Relief.UseDefault, Relief.UseRatio, Ratio, Width, Depth, Relief.Type, TrimSideBends, PositionType, StartOffset, EndOffset, cba)`.
3. If that returns null, retry with the edges on mark 0.
4. `FeatureEdit.MiterFlange` to set anything that reads back differently; compare edge count (SOLIDWORKS may propagate along tangent edges) and `BendCount`.
5. Alternative: swap start and end offsets.

**HemBuilder** (about 220 lines)

1. Select the edges on mark 0. Call `InsertSheetMetalHem2(HemType, Position, Reverse, Length, Gap, Angle, Radius, MiterGap, cba, true /*UseDefaultRelief: not readable*/, 0, 1, true, 0.5, 0, 0)`.
2. `FeatureEdit.Hem` to compare and set.
3. Width sketch: if `FlangeProfiles.IsFullEdgeLine(mirrored)` holds and the new sub-sketch already matches, keep it. Otherwise `ReplaceInto` the hem's sub-sketch, rebuild, and compare.
4. Register the hem and the sketch.
5. Note in the report: "Entlastung des Saums nicht lesbar – Standard".

**JogBuilder** (about 250 lines)

1. The line is `ctx.Sketches[SketchName].Segments[LineId]`.
2. Fixed face: `FindFace`. Pick point: `R(FixedPoint)`; if `FixedPoint` is missing, compute a point beside the line with `JogGeometry.PointBeside(face, line, side)` (Core).
3. Select the line (mark 0, not appended). Select the face with `SelectByID2("", "FACE", p, append:true, mark:0)` and check the pick with `IsSameFace`, as `SelectFaceAt` already does.
4. Call `InsertSheetMetalJog(deg(JogAngle), BendRadius, OffsetDistance, flipGuess, FixProjectedLength, (short)DimensionPosition, (short)(JogPosition-1))`. `flipGuess = DirectionRules.ReverseFlag(ReverseDirection, map.Sigma)`.
5. The call returns nothing: the new feature is the top-level feature of type `"Jog"` that was not there before (compare name sets, as `RemoveLeftovers` does).
6. If no jog appeared, retry with the angle in radians. Then try selecting the sketch feature instead of the segment.
7. `FeatureEdit.Jog`: set `JogAngle` (radians), `JogPositionType`, `DimensionPositionType`, `OffsetType`/`Reference`/`Distance`, `ReverseOffset`, `ReverseDirection = flipGuess`, the radius, the allowance and `FixedPoint`.
8. Read back and compare.

## 6. Mirror rules and why they hold

| Quantity | Rule | Why |
|---|---|---|
| Hinge, hem and miter edges, fixed face, offset references (vertex, face, plane, edge) | the mirrored fingerprint found by `TopologyFinder`; a plane stands in only for references, never for hinges or fixed faces | R maps the original's topology onto the copy's; the target is in exactly the mirrored state at that moment |
| BendAngle, radius, lengths, gap, miter gap, reliefs, OffsetDimType, PositionType, TrimSideBends, hem type and length, jog angle, offset, Dimension/JogPosition, FixProjectedLength, allowance | unchanged | lengths and angles between faces are invariant under R. Inside/outside, virtual sharp and tangent are defined by the outer faces of base and flange, which R maps onto the copy's outer faces; material maps to material |
| Which side of the edge is "outside" | not a flag; it follows from the mirrored base faces and the mirrored profile side, which SOLIDWORKS derives itself | both inputs are mirrored; only conventions SOLIDWORKS derives internally can flip, and those are measured |
| Flange direction | R(d), with d the profile direction from §2B; selects the plane, the Y axis and the profile check | a geometric vector, so no sign convention is involved |
| Edge-flange profile 2D content | `SketchMirror` into the frame SOLIDWORKS creates (σ from `AxisMap`) | same as every other sketch; the X axis follows the edge's parameterization, which can run backwards |
| Edge flange `ReverseOffset`, `ReversePositionOffset` | start from the original; decided by the profile check | the reference vector SOLIDWORKS uses (edge direction or face normal) is undocumented; tbcphutai also needed a search |
| Hem `ReverseDirection` | start from the original; alternative | probably relative to the face's outward normal, which is invariant; unverified |
| Jog direction (`FlipDir`/`ReverseDirection`) | `ReverseFlag(flag, σ of the jog sketch)`; alternative | the offset goes along the sketch normal: z′ = σ·R(z). On the face (§2F) σ = +1, so the flag is unchanged. If it is relative to the fixed face's normal instead, the guess is still right on the face |
| Jog fixed side | R(FixedPoint) on R(FixedFace) | a point maps to a point on the same side of the mirrored line |
| Miter Start/EndOffset | unchanged; swap is an alternative when they differ | "start" is the end of the chain that carries the profile, and the profile is mirrored with it |
| Edge order and profile pairing | original order; `AddEdges` arrays kept aligned | the order matters only for that pairing |

## 7. Verifier alternatives and geometric checks

Geometric and structural checks run first, inside the builder, and are deterministic:
- the profile frame and profile points (edge flange);
- the width sketch (hem);
- edge counts, `BendCount`, and the read-back values.

The edge flange offers direction alternatives only if its profile check could not confirm the direction. This follows the PatternBuilder precedent: a geometric confirmation suppresses mass-based variants. One caveat: on 2024, ModifyDefinition on a regenerated edge-flange profile was not idempotent (tbcphutai). Each edge-flange toggle therefore re-runs the profile check after undoing itself and returns `false` if the profile is not back. StepVerifier already handles that case ("ließ sich nicht zurücknehmen").

| Builder | Alternatives in order (`Parts` for the report) |
|---|---|
| EdgeFlange | "Laschenrichtung umgekehrt" (ReverseOffset); "Versatzrichtung der Lasche umgekehrt" (ReversePositionOffset, only with UsePositionOffset); both together |
| MiterFlange | "Start-/Endversatz getauscht" (only if they differ by more than `Tol.Length`) |
| Hem | "Saumrichtung umgekehrt" (ReverseDirection) |
| Jog | "Jog-Richtung umgekehrt" (ReverseDirection); "Versatz umgekehrt" (ReverseOffset); both together; "feste Seite gewechselt" (FixedPoint moved to `PointBeside(..., other side)`, only if FixedPoint was not captured) |

Mass and moment tell these variants apart well: a flipped flange, hem or jog moves the centroid by roughly its length times its area. Face samples now include bend faces (§2E).

## 8. Core tests

New files `tests/Spiegelwerk.Core.Tests/SheetMetalFlangeTests.cs` and `JogTests.cs`.

1. `Flange_direction_is_mirrored_with_the_profile`: `Direction(R(points), R(hinge)) == R(Direction(points, hinge))`, over random planes, as in `DraftDirectionTests`.
2. `Frame_fits_whichever_way_the_copy_edge_runs`: a target frame with X = ±R(e) and Y = R(d) fits; Y = −R(d) does not; a plane tilted by 1e-4 rad does not.
3. `Rebase_onto_hinge_removes_the_position_offset_and_keeps_2D_coordinates`.
4. `Mirrored_profile_written_into_a_native_frame_lands_on_the_mirrored_points`: `SketchMirror.Mirror` into a frame with reversed X or normal; `ToModel == R(source)`.
5. `Profile_deviation_detects_a_flange_flipped_about_its_hinge`: deviation equals twice the distance; an identical set gives 0 regardless of point order.
6. `Assign_pairs_each_hinge_with_the_profile_on_it` (two edges, profiles listed in reverse order).
7. `Assign_refuses_ambiguous_or_missing_profiles` (two collinear hinges; a profile on neither).
8. `Assign_uses_frame_evidence_for_offset_profiles` (position offset of 3 mm; no line on the hinge).
9. `Hem_width_sketch_spanning_the_whole_edge_is_default`, and a partial one is not.
10. `Jog_direction_flag_follows_sigma` (σ ±1 with `ReverseFlag`; on a face σ = +1 keeps it).
11. `Fixed_point_stays_on_its_side_of_the_mirrored_jog_line` (sign of `(p − a)·(n × t)` under R, with the copy's t and n, comes out consistent), plus `PointBeside_is_off_the_line_and_on_the_face_plane`.
12. `Miter_swap_alternative_only_for_unequal_offsets`.
13. `Differences_list_only_deviating_values` (tolerances, enums, null allowance against a set allowance).
14. `Edge_flange_record_scalars_survive_reference_mirroring`: `GeometryRef.Mirror` of a vertex or face reference; angle, lengths and flags untouched.

## 9. Assumptions that need a live test (2019 first, then 2024)

| # | Assumption | Safeguard if wrong |
|---|---|---|
| 1 | `CreateDefinition(swFmEdgeFlange)` plus `AddEdges(Edge[], Sketch[])` plus `CreateFeature` works in 2019 | Path B, then Path C; the report names the path |
| 2 | `InsertSketchForEdgeFlange` angle uses the same convention as `BendAngle`; `FlipDir` picks the other side; the frame's Y axis points along the flange | four-candidate search with frame check; otherwise fail and use the fallback |
| 3 | The generated sketch may be empty or hold a default rectangle, and may be open or closed | `ReplaceInto` clears it; `ActiveSketch` is checked |
| 4 | `SketchFeats` accepts `Feature[]` (eyfel) or `Sketch[]` (help) | try both |
| 5 | Mapping of `swInsertEdgeFlangeOptions_e` bits (FlipDir=ReverseOffset, DoOffset, TearClip) | all values are set again in the post-edit, then checked |
| 6 | `ReadSketch` can roll back to just after an absorbed flange profile; `GetReferenceEntity` returns the hinge edge | `Assign` also uses line-on-hinge and frame evidence |
| 7 | Profiles of a multi-edge flange are found via `SubFeatures`, parent sketches or `GetOwnerFeature` | ambiguous pairing becomes Unsupported, then the fallback |
| 8 | Position offset shifts the profile sketch along its normal (tbcphutai) | rebase plus profile check |
| 9 | Miter flange: sketch on mark 0, edges on mark 1 | retry with edges on mark 0; edge count compared |
| 10 | Hem: edges on mark 0; hem relief unreadable means default; teardrop at 270° may fail | readback; fallback |
| 11 | The hem's sub-sketch is the width sketch and is editable with `EditSketch` | without it: keep the default and warn |
| 12 | Jog: segment on mark 0 plus face picked off the line; angle in **degrees**; `DimPos`/`BendPos` encodings | radians retry, sketch-feature selection retry; post-edit sets exact enums and angle |
| 13 | A jog accepts a sketch on a reference plane coplanar with the face | otherwise sketch on the face (§2F) |
| 14 | `FixedPoint` is in model coordinates and settable through ModifyDefinition | otherwise use `PointBeside` and the "feste Seite" alternative |
| 15 | `GetLastFeatureAdded` is unreliable for features placed before Flat-Pattern | detect new features by comparing top-level name sets instead |
| 16 | `OneBend` sub-feature counts are stable between original and copy | a mismatch is only a warning |
| 17 | Toggling `ReverseOffset` twice restores the flange | the toggle re-checks the profile and returns false otherwise |
| 18 | Late-bound `LockAngle`, `AngleReference` and type 6 exist only on newer versions | `SwLate` returns null; a note if the original used them |

## 10. Swept flange

There is no `ISweptFlangeFeatureData` and no `swFmSweptFlange` (117) in 27.3.0; the feature arrived after 2019 (my recollection: SOLIDWORKS 2020; unconfirmed). Parts made in 2019 cannot contain one.

Recommendation: capture `"SweptFlange"` as `UnsupportedRecord(changesSolid:true)` so the body fallback covers it.

An optional phase-2 builder, about 200 lines, late-bound:
- `CreateDefinition(117)` through IDispatch;
- `Profile` and `Path` set to the rebuilt ordinary sketches;
- `MaterialInside`, `FlangePosition`, `TrimSideBends`, offsets, relief, allowance, `FlattenAlongPath`, `CylindricalOrConicalEdge` (mirrored edge);
- `CreateFeature`.

`ReverseDirection` (thickness side of the open profile) flips with sketch orientation; offer it as an alternative, like the thin-wall side. Start/End offsets can be swapped as an alternative.

## 11. How this feeds the fallback

**Unsupported at capture.** These feed the planner of the "mirrored body, then Insert Bends / Convert to Sheet Metal" fallback before the build starts:
- curved hinge;
- ambiguous profile pairing;
- jog sketch that is not a single line;
- swept flange.

**Failed at build.** `BuildResult.Failed`:
- an edge or face not found;
- no fitting profile frame;
- every creation path returned null;
- a creation path removed again after a failed edit.

The report entry carries the reason. The fallback itself belongs to another area.

## 12. Effort estimate

- **Records:** 4 (EdgeFlange, MiterFlange, Hem, Jog), plus 1 optional (SweptFlange), plus 3 value classes (`FlangeProfile`, `ReliefSettings`, `BendAllowanceSettings`), plus the `IOwnsSketches` interface.
- **Core planning:** 2 classes (`FlangeProfiles`, `SheetMetalParams`, with `JogGeometry` as a small part), about 350 lines, plus about 20 tests (about 450 lines).
- **Capture:** 1 partial file (about 400 lines), plus generalizing `PartOf` in `FeatureCapture.Holes`/`MirrorJob` (about 60 lines), plus `SketchCapture.SupportFace` (about 20).
- **Builders:** 4 (EdgeFlange about 400, Hem about 220, Jog about 250, MiterFlange about 180), plus 1 optional (SweptFlange about 200).
- **Support:**
  - `SwSheetMetal` (about 120);
  - 4 `FeatureEdit` methods;
  - `SketchBuilder.ReplaceInto` (about 40);
  - sketch-on-face in `SketchBuilder` (about 60, shared with sketched bend);
  - `MassCapture` sub-feature faces (about 15).
- **Docs:** an "Blech: Laschen an Kanten" section in ARCHITEKTUR, about 12 new STOLPERSTEINE items, and 18 rows in the live-test table.
- **Total:** about 2,600 lines, roughly 6–8 developer days, plus 1–2 days of live testing on 2019 and 2024. The edge flange is about half of the risk and effort.

Files are in `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad`:
- `smprobe/Program.cs` — reflection probe; run as `bin/Release/net8.0/smprobe <lib27|lib32> members|method|enum|find|types …`
- `smprobe/m27.txt` — the 27.3.0 member listings
- `smprobe/m32.txt` — the 32.1.0 member listings
- `apidocs/` — copies of the API help pages
- `ghsrc/` — the third-party code quoted above