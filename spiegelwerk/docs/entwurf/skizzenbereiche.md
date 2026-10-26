# Design: carrying over selected sketch regions of Extrude and Revolve features

**Scope.** This is a read-only design; nothing in `/home/user/Claude` was changed. The relayed user question asks whether these feature types are missing from the tool. For sketch regions the answer is yes: today `ReadContours` (`src/Spiegelwerk/Capture/FeatureCapture.cs:181`) drops every region and adds the note "Ausgewählte Skizzenbereiche … ganze Skizze". The design below adds region support without any Mirror-Feature API.

**Sources.**
- Member signatures were checked with a new metadata probe against the 27.3.0 interop and compared with 32.1.0. The `ISketchRegion` members are identical in both versions.
- help.solidworks.com was refused by the egress proxy (403). Semantics come from the offline help extraction already in the scratchpad (`fc2/llmdocs/x/...`), from xcad, and from the CodeStack sources.
- A prototype of the proposed Core algorithm passes every check, including the edge cases below.

## 0. Problems in today's code

1. **Mixed selections.** When a feature uses both contours and regions, `ReadContours` keeps the contours and drops the regions, adding one note per region. The builder then calls `MapContours(r.Contours…)`, which is non-empty, so it builds from the contours alone. The note claims the whole sketch is used, so the note and the result disagree. The fix below makes capture all-or-nothing.
2. **Selection mark.** Contours are pre-selected today with mark 0 and no `EnableContourSelection`. Both official examples do something else. "Create Extrude Feature Using Sketch Contours" sets `EnableContourSelection = true` and selects the sketch and the contours with mark 4. "Enable Contour Selection" calls `SelectByID2("Sketch1","SKETCHREGION",x,y,z,true,4,null,0)` and then `FeatureExtrusion3`.
3. **Contour objects in `ModifyDefinition`.** The fallback path assigns contour objects that were fetched *before* the feature was created (`contoursPending`). The sketch may have been absorbed since then.

## 1. API (interop 27.3.0)

### How a region appears in the feature data
- `IExtrudeFeatureData2` and `IRevolveFeatureData2` both have:
  - `object Contours {get;set;}`
  - `int GetContoursCount()`
  - `object IGetContours(int Count)`
  - `void ISetContours(int Count, ref object Contours)`
- The help remark for `IExtrudeFeatureData2.Contours` says: "The array … can contain both ISketchContour and ISketchRegion objects." `IRevolveFeatureData2.Contours` says the same ("sketch contours and sketch regions").
- The official VBA example "Determine if Sketch Contour or Sketch Region" tells them apart with `TypeOf … Is SketchRegion`. In C# that is `item is SketchRegion`, a COM QueryInterface. Test for it *before* `SketchContour`.

### Reading a region's boundary
- `ISketchRegion` has exactly these members, and no `GetSketchSegments`:
  - `Sketch Sketch {get;}`
  - `int GetEdgesCount()`, `object GetEdges()`, `Edge IGetEdges(int)`
  - `bool Select(bool Append, int Mark)`, `bool Select2(bool Append, SelectData Data)`, `bool DeSelect()`
  - `Loop2 GetFirstLoop()`
- Help remark: "white space … bordered by sketch entities or portions of sketch entities basically forming a face. A centerline within a sketch creates sketch regions."
- Walking the loops:
  - `ILoop2`: `object GetNext()`, `bool IsOuter()`, `object GetEdges()`, `object GetCoEdges()`, `object GetVertices()`, `object GetFace()`.
  - The "Get Sketch Regions" example iterates `GetFirstLoop` → `GetNext`.
- Geometry of each edge:
  - `IEdge.GetCurveParams3()` returns a `CurveParamData` with `StartPoint`, `EndPoint`, `UMinValue`, `UMaxValue` and `Sense`.
  - `IEdge.GetCurve()` → `ICurve.Evaluate2(double Parameter, int NumberOfDerivatives)`. `TopologyCapture.Edge` already uses this pair.
  - `ICurve.GetTessPts(double ChordTolerance, double LengthTolerance, object StartPoint, object EndPoint)` also exists, but its behaviour on closed edges is unclear, so I recommend `Evaluate2`.
- All regions of a sketch: `ISketch.GetSketchRegionCount()`, `object GetSketchRegions()`, `SketchRegion IGetSketchRegions(int)`.
- **Coordinate space.** The help documents none. The best evidence is a working CodeStack macro (`multi-extrude/Geometry.vba`): it builds a model-space plane via `ModelToSketchTransform.Inverse` and trims it with the region's edge curves through `Surface.CreateTrimmedSheet5`. That implies **model space**. Contrast `ISketch.GetContourEdges(Xform)`, which is documented as sketch space with a unit transform.

### Selecting a region for feature creation
- Pick by point: `IModelDocExtension.SelectByID2(string Name, string Type, double X, double Y, double Z, bool Append, int Mark, Callout, int SelectOption)` with `Type = "SKETCHREGION"` (`swSelectType_e.swSelSKETCHREGION = 95`; `"SKETCHCONTOUR"` is 96).
- Contour selection switch: `ISelectionMgr.EnableContourSelection {get;set;}`.
- Read back a pick: `ISelectionMgr.GetSelectedObject6(int, int)` and `GetSelectedObjectType3(int, int)`. Check `ISketchRegion.Sketch` to confirm the picked region belongs to our sketch.
- Or assign directly: `d.Contours = object[]` (regions mixed with contours) inside `AccessSelections` / `ModifyDefinition`. This is the existing `contoursPending` route.

## 2. Identifying the region in the mirrored sketch: option (a), an interior point

### Why (b), matching by boundary segment ids, does not work
- `ISketchRegion` exposes no segments. Ids would have to be recovered by matching edges to segments geometrically.
- Region edges are *portions* of segments, split at intersections, so a set of segment ids does not identify a region. Example: two circles, r = 20 mm, centres ±7.5 mm. They form three regions — a lens and two crescents — and all three are bounded by portions of exactly {C1, C2}.
- Using portions (segment id + parameter range) would mean mapping parameter ranges through the mirror. That breaks on reversed direction, relocated circle seams (STOLPERSTEINE 30) and spline reparametrisation.

### Why (a) works
A point position is preserved exactly by the mirror. The point is independent of ids, splitting, seams, loop orientation and σ.

### Pipeline (Core unless marked add-in)
1. **Sample** every loop edge (add-in calls the Core sampler `RegionGeometry.Sample(Func<double,Vec3> eval, double u0, double u1, double chordTol, int minPieces = 8, int maxDepth = 14)`).
   - Evaluate the curve over [UMin, UMax], starting from 8 pieces.
   - Split a piece when the midpoint *or* either quarter point deviates from its chord by more than `chordTol`. Checking the quarter points catches S-shaped splines whose midpoint lies on the chord.
   - This handles lines, arcs, circles, ellipses, splines (with handles) and parabolas with SOLIDWORKS's own geometry. Core has no exact spline evaluator, which is why sampling happens on the SOLIDWORKS side.
   - `chordTol = clamp(1e-4 · extent, 1e-7, 1e-4)` m, where extent is the bounding-box diagonal of the region's raw vertices. A 100 mm sketch gets 10 µm.
2. **Chain pieces into loops:** `RegionGeometry.ChainLoop(IReadOnlyList<IReadOnlyList<Vec3>> pieces, double weldTol = 10·chordTol)` joins pieces greedily by nearest endpoints, reversing pieces where needed. It needs neither coedge order nor sense from the API, and returns null if a loop stays open.
3. **Decide the coordinate space:** `RegionSpace.Decide(IEnumerable<Vec3> raw, Frame3 frame)`.
   - Model space: all points satisfy `|frame.ToLocal(p).Z| ≤ 10 µm`.
   - Sketch space: all points satisfy `|p.Z| ≤ 10 µm`.
   - Both true means the sketch plane is the world XY plane. If the frame is the identity in the plane, the two readings agree. Otherwise take the default `RawSpace.Model`, which the live test must confirm.
   - Convert with `ToSketch(...)` into source sketch coordinates — the same system as `SkPoint.X/Y` — with Z = 0.
4. **Interior point:** `RegionGeometry.FindInteriorPoint(loops, chordTol) → (Vec3 Point, double Clearance)?`. Every loop of the region (outer and holes) enters as an unordered set of edges, and inside/outside follows the even-odd rule.
   - **Scan levels:** midpoints of the 64 widest gaps between consecutive distinct vertex ordinates (no vertex lies on such a line, and every horizontal band of the region is hit), plus `ymin + h·k/16` for k = 1..15.
   - **Crossings:** an edge (a, b) crosses level y iff `(a.Y > y) != (b.Y > y)`. This half-open rule keeps parity correct when a vertex happens to lie on the line. Sort the x values; if their count is odd, skip the level.
   - **Candidates:** the midpoint of each inside interval `[x0,x1]`, `[x2,x3]`, …; its clearance is the exact minimum point-to-segment distance to the boundary.
   - **Refinement:** keep the best candidate, then rescan four times alternately vertically and horizontally through it, keeping any improvement.
   - **Acceptance:** accept only if `Clearance ≥ MinClearance = 4·chordTol + 10·Tol.Length`; otherwise return null, which triggers the fallback.
   - **Why the margin suffices:** the true boundary lies within `chordTol` of the polyline on both the source and the target side, and the rebuilt sketch is accurate to `Tol.Length`. So the mirrored point keeps at least `2·chordTol + 9·Tol.Length` from the target polyline, which exceeds the classification margin `chordTol + Tol.Length`. Points on or near the boundary therefore never count as inside.
   - Maximising clearance, rather than accepting any valid point, also absorbs small differences in the copy (for example spline handles that are not copied).
5. **Mirror the point:** `SketchMirror.MapPoint(Frame3 source, Reflection r, Frame3 target, Vec3 local) = target.ToLocal(r.Apply(source.ToWorld(x, y)))`, with the same per-point `|Z|` check as `Mirror`. Refactor `Mirror()` to call it, so sketch points and region points take one code path. That path is correct for σ = +1 and σ = −1, with no hand-made negation.
6. **Classify a point:** `RegionGeometry.Classify(loops, p, margin) → Inside | Outside | Boundary`. It returns Boundary if the distance to the boundary is below the margin; otherwise it casts a horizontal ray with the half-open rule.
7. **Match:** `RegionMatcher.Match(ProfileRegion mirrored, IReadOnlyList<ProfileRegion> candidates, bool allowCover) → RegionMatch {bool Success; List<int> Indices; List<string> Notes; string? Reason}`.
   - **Identify** the target regions whose `Classify(p)` is Inside. Any Boundary result means the geometry differs; fail with a reason. Zero hits: fail. More than one hit: fail (cannot happen for a valid sketch).
   - **Equal area** (within `tolA = max(1e-4·A, 2·chordTol·(Ps + Pt))`): select that one region.
   - **Target smaller, cover allowed:** S = the candidates whose own interior points are Inside the mirrored source loops. If |S| ≥ 2 and ΣA_S ≈ A, select all of S and add a note (the region was split by geometry the original lacked).
   - **Otherwise** select the containing region and add a note quoting both areas. The mass check then decides.
   - Two source regions that map to the same target region is an error.

## 3. Changes

### Core
- **`ProfileRegion`** (new, `Records/FeatureRecords.cs` or a new `RegionRecords.cs`):
  - Constructor `ProfileRegion(IEnumerable<IReadOnlyList<Vec3>> loops, double chordTolerance)`.
  - Properties: `Loops` (unmirrored source sketch coordinates), `ChordTolerance`, `Area` (|largest loop| − Σ other loops), `Perimeter`, `Vec3? Interior`, `Clearance`.
  - `Map(Func<Vec3,Vec3>)`.
- **`ExtrudeRecord` and `RevolveRecord`:** add `public List<ProfileRegion> Regions { get; } = new();`. Update the doc of `Contours`: Contours and Regions together form the selection; both empty means the whole sketch.
- **New classes:** `Sketch/RegionGeometry.cs`, `Sketch/RegionSpace.cs`, `Sketch/RegionMatcher.cs`, and `SketchMirror.MapPoint`.
- Records stay unmirrored, as everywhere else.

### Add-in capture
- **New `Capture/RegionCapture.cs`** (static, used by both capture and build, like `TopologyCapture`):
  - `ProfileRegion? Read(SketchRegion region, Frame3 frame, double? chordTol, out string? why)`.
  - `List<(SketchRegion Region, ProfileRegion Shape)> ReadAll(Sketch sketch, Frame3 frame, double chordTol)`.
- **`FeatureCapture`:** replace `ReadContours` with an instance method `ReadProfile(Feature sketch, object[] items, List<List<string>> contours, List<ProfileRegion> regions, List<string> notes)`.
  - Call it in the same place, while `AccessSelections` is active; region objects are only valid then.
  - Check `SketchRegion` first, then `SketchContour`.
  - All-or-nothing: if any item is unreadable or has no interior point, clear both lists and add **one** note giving the reason, then fall back to the whole sketch.
  - Use `_math.SketchFrame(sketch)`, and check that `SwDoc.SketchNameOf(region.Sketch)` equals the feature's sketch name.

### Add-in build
- **`SwSelect.Select`:** add `case SketchRegion g: return g.Select2(append, data);`.
- **Contour-selection switch:** add a scope that sets `EnableContourSelection = true` and always resets it in `finally`.
- **`BuilderSupport.MapProfile(BuildContext ctx, SketchMap sketch, List<List<string>> contours, List<ProfileRegion> regions, bool thin, List<string> notes) → List<object>?`:**
  - Contours go through the existing `MapContours`, unchanged.
  - Regions go through `MapRegions`:
    1. `RegionCapture.ReadAll(sketch.Sketch, sketch.Mirrored.Sketch.Frame, r.ChordTolerance)`. Only this sketch's regions are read, so the original's coplanar sketch in the same part cannot interfere.
    2. Map each source region with `MapPoint(sketch.Source.Frame, ctx.Mirror, targetFrame, ·)`.
    3. `RegionMatcher.Match(…, allowCover: !thin)`.
  - Any failure returns null plus today's note, and the builder falls back to the whole sketch.
- **`ExtrudeBuilder` and `RevolveBuilder`:**
  - Call `MapProfile` instead of `MapContours`.
  - `Create` tries, in this order:
    1. The documented route: `EnableContourSelection` on, mark 4.
    2. Today's mark 0.
    3. Whole sketch, then `d.Contours = …` in `ModifyDefinition`. Call `MapProfile` again **inside** the `FeatureEdit` lambda to get fresh objects in the rolled-back state.
  - In `RevolveBuilder`, skip the pairing "profile mark 4 + axis mark 4". Try axis 16 with profile 4, then the mark-0 variants.
  - Optionally read back `GetContoursCount()` after creation.
- **Verification is unchanged.** `ExpectedChange`, `FaceSamples` and the alternatives work as before: reverse, wall side, swap thicknesses, side to cut, start offset. A wrong region shows up as a mass, centroid or face-sample deviation, i.e. a warning. Build-time notes (cover used, area differs) make the result a warning through `BuildResult` notes. If regions carry over cleanly there is no capture note, so the result is "OK" once verified.
- **Docs:**
  - STOLPERSTEINE: regions have no segments; the coordinate space; the role of the interior point and its clearance; the cover rule; the marks; and update table row 200 "Ausgewählte Konturen".
  - ARCHITEKTUR and README rows 22/23: add "Skizzenbereiche".

## 4. Edge cases

| Case | Handling |
|---|---|
| Ring (nested loops) | The region is the outer loop plus its holes; even-odd handles it. Prototype: annulus r = 10/20 mm gives point (0, −15 mm), clearance 5 mm; the centre classifies as Outside. A three-level nesting (outer ring, middle ring, island) forms a partition: each interior point is Inside exactly one region. |
| Several regions | Matched independently and checked to be distinct; all appended (mark 4 or 0) or assigned as one `Contours` array, mixed with contours. |
| Regions sharing a boundary | The required clearance keeps every interior point away from the shared edge, so each source region finds its own target region. Selecting adjacent regions together is assumed to give their union (live test). |
| Thin features, open contours | Open chains stay `ISketchContour` and are matched by segment-id sets as today. Thin features from a region: same matching, but the cover rule is off (a split would add an inner wall). Wall side after mirroring is handled by the existing "Wandseite gewechselt" alternative. |
| Construction geometry | Construction segments are mirrored with their flag, so the region structure is identical. Risk: stand-ins from `SketchBuilder.Substitute` (fixed construction lines, e.g. 100 mm for a crossing plane) may split regions only in the copy, if construction lines bound regions (the API remark says centerlines do). The cover rule handles non-thin features; otherwise a note is added and the mass check decides. Dropped text or segments that could not be created give an area mismatch and a note. |
| σ = −1 | Example: Front sketch mirrored at the Front plane; the in-plane map is the identity. `MapPoint` runs the same sketch→model→mirror→target path as all sketch points. Area and even-odd do not depend on loop orientation (σ = +1 reverses it, σ = −1 keeps it). The coordinate-space test uses the target frame, which is read after the sketch exists. |
| Sliver regions | No interior point with the required clearance: fall back to the whole sketch with a note giving the reason. |
| Shared sketch, different regions per feature | Works per feature; regions are read again from the mirrored sketch (`ctx.Sketches[r.SketchName]`) for every feature. |

## 5. Implementation plan and Core tests

1. **`SketchMirror.MapPoint` and refactor `Mirror`.** Test `MapPoint_equals_Mirror_points`: random frames (`TestGeometry.RandomFrame`), target frames for σ = +1 and σ = −1, the result equals what `Mirror()` gives for the same `SkPoint`.
2. **`RegionGeometry` and its tests:**
   - Sampler: arc chord deviation ≤ ε (prototype: 6.1e-6 ≤ 1e-5 with 65 points); flat ellipse; S-curve; full circle closed.
   - `ChainLoop`: shuffled and reversed pieces; a gap inside the weld tolerance is welded; a larger gap returns null.
   - Area of a ring within `2ε·P`.
   - Interior point and classification:
     - Rectangle; L-shape (the bounding-box centre lies outside).
     - Comb with 20 teeth.
     - Diamond: the scanline passes through vertices; a point on an edge classifies as Boundary.
     - Ring; three-level nesting as a partition.
     - The lens/crescent example from §2: three distinct points, each Inside exactly one region.
     - Slivers: 2.5·MinClearance wide is accepted, 1.5· is rejected.
     - 300 random star polygons with optional holes: the point is Inside with clearance ≥ MinClearance, and reversing the loops changes nothing.
3. **`RegionSpace.Decide` and its tests.** Raw points on Top/Right-plane sketches decide uniquely for model and for sketch space; a Front-plane sketch with identity frame is unambiguous; a world-XY face with normal −Z gives Undecided and uses the default; points off the plane are an error.
4. **`ProfileRegion` and the `Regions` lists on `ExtrudeRecord`/`RevolveRecord`.** Test that `Map` keeps area and clearance and that the interior point maps to Inside.
5. **`RegionMatcher` and its tests:**
   - Rectangle with a centerline, left half selected, mirrored at `RightYZ`: σ = +1 picks the right half; the same with a target frame `(−X, Y)` (σ = −1).
   - Ring and disc; adjacent pair gives two indices.
   - Cover: the target half is split by an extra line, two indices and a note; with thin set, the cover rule is off.
   - Merged region (target larger): one index and an area note.
   - No hit, and two source regions on one target, each give a reason.
6. **`RegionCapture`, then `FeatureCapture.ReadProfile`** (all-or-nothing, which fixes the mixed-selection problem from §0).
7. **`SwSelect` case, contour-selection scope, `BuilderSupport.MapProfile`/`MapRegions`.**
8. **`ExtrudeBuilder.Create` and `RevolveBuilder.Create`:** the selection variants, and fresh objects inside the `FeatureEdit` lambda.
9. **Docs** (STOLPERSTEINE, ARCHITEKTUR, README).
10. **Build and test:** build against 27.3.0 and 32.1.0 and run the Core tests.
11. **Live test** (§6).

## 6. Assumptions only a live SOLIDWORKS test can settle

1. `Contours` returns `SketchRegion` objects for regions in Extrude and Revolve (all four creation variants and Revolve), and a region does not also answer as `SketchContour`.
2. Coordinate space of the loop edges and vertices of a region. Test a Top-plane sketch (model y = 0 versus sketch z = 0) and a sketch on a −Z face at z = 0; the default is model space.
3. `GetFirstLoop`/`GetNext` yields exactly the region's own loops (outer plus holes), and `GetCurveParams3`/`Evaluate2` work on these temporary edges, with UMin/UMax limited to the trimmed portion.
4. `ISketch.GetSketchRegions` works on a closed sketch, on an absorbed or shared sketch, and under `AccessSelections`, and its regions match the UI.
5. Whether construction lines bound regions — centerlines per the help, but also `ConstructionGeometry = true` lines and the fixed stand-ins.
6. Which pre-selection the five creation methods accept for regions: mark 4 with `EnableContourSelection` (documented), or mark 0. The same question applies to contours.
7. Whether `d.Contours = object[]` with regions (and mixed with contours) works in `ModifyDefinition`, and whether region objects fetched before creation stay valid or must be fetched again.
8. Selecting several adjacent regions produces their union for boss, cut and revolve; how a thin feature from a region behaves.
9. Fallback only: `SelectByID2("<sketch>", "SKETCHREGION", model point, …)`. Is it view-independent, is the Name parameter required, does `GetSelectedObject6` return `ISketchRegion` and does `.Sketch` give the owning sketch?
10. With a revolve, regions at mark 4 next to the axis at 16 — no clash with the axis mark.

Files are in /tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad:
- regionprobe/Program.cs — the metadata probe (prints typed signatures)
- regionalgo/Program.cs — the algorithm prototype; all checks pass ("ALL OK")
- fc2/llmdocs/x/examples/Enable_Contour_Selection_Example_CSharp.md — offline API help example
- repos/codestack/src/solidworks-api/document/macro-feature/multi-extrude/Geometry.vba — evidence for model-space region edges