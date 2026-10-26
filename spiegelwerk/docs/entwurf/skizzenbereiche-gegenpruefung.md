I found 20 problems in the sketch-region design (P1–P20 below). Six of them can produce wrong or harmful results:

- **P1:** the coordinate-space default is unsupported, and the ambiguous case is common.
- **P2:** checking the created feature's selection afterwards is optional.
- **P3:** the area tolerance is loose enough to accept a region with 60 % of the area.
- **P7:** in same-part merge mode a larger fallback profile cuts the user's body.
- **P8:** the pick check against `r.SketchName` would accept the original's region.
- **P9:** one unreadable region aborts the whole run.

Every API member the design names exists with the stated signature in 27.3.0, and the members are identical in 32.1.0. Most claims about the existing code hold. The prototype covers less than the design says. Nothing in /home/user/Claude was modified.

Scratch work:
- Metadata probe: /tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/regionreview/Program.cs. Member dumps are in 27.3.0_27.3.0.txt and lib_32.1.0.txt in the same folder; `diff` reports IDENTICAL.
- Edge-case tests run against a copy of the prototype algorithm: /tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/regionreview2/Program.cs.

## Verified as correct

**API members, checked with the probe against 27.3.0:**
- `ISketchRegion`: `Sketch Sketch{get;}`, `GetEdgesCount()`, `object GetEdges()`, `Edge IGetEdges(Int32 Count)`, `Select(bool,int)`, `DeSelect()`, `Select2(bool, SelectData)`, `Loop2 GetFirstLoop()`. There is no `GetSketchSegments`.
- `ILoop2`: `GetNext`, `IsOuter`, `GetEdges`, `GetCoEdges`, `GetVertices`. It also has `object GetFace()` and `Face2 IGetFace()`, which the design does not use.
- `ISketch`: `GetSketchRegionCount`, `GetSketchRegions`, `IGetSketchRegions(int)`.
- `ISelectionMgr`: `EnableContourSelection{get;set;}`, `GetSelectedObject6(int,int)`, `GetSelectedObjectType3(int,int)`.
- `IModelDocExtension.SelectByID2(string,string,double,double,double,bool,int,Callout,int)`.
- `IExtrudeFeatureData2` and `IRevolveFeatureData2`: `object Contours{get;set;}`, `GetContoursCount()`, `IGetContours(int)`, `ISetContours(int, ref object)`.
- `IEdge.GetCurveParams3()` returns `CurveParamData` with `StartPoint`, `EndPoint`, `UMinValue`, `UMaxValue`, `Sense`. `ICurve.Evaluate2(double,int)` and `ICurve.GetTessPts(double,double,object,object)` exist.
- `swSelSKETCHREGION = 95`, `swSelSKETCHCONTOUR = 96`.

**Claims about the existing code:**
- §0.1 holds. `ReadContours` (FeatureCapture.cs:181–190) adds one note per region while keeping the contours, and `MapContours` then builds from the contours alone (ExtrudeBuilder.cs:85).
- §0.2 holds (ExtrudeBuilder.cs:179, RevolveBuilder.cs:106).
- §0.3 holds (ExtrudeBuilder.cs:89–93, 127).
- `TopologyCapture.Edge` uses `GetCurveParams3` together with `Evaluate2` (TopologyCapture.cs).
- The sketch map is keyed by the original name: `ctx.Sketches[r.SketchName]` (BuildContext.cs, ExtrudeBuilder.cs:37).
- A capture note makes the result a warning (MirrorJob.cs:230), and so do build notes (ExtrudeBuilder.cs:146).
- Warnings block "delete original" (`MirrorReport.AllMirrored`).
- The stand-in for a crossing plane is ±0.05 m long (SketchBuilder.cs:487–488).
- The `MapPoint` formula matches `SketchMirror.Mirror` (`target.ToLocal(r.Apply(source.ToModel(p)))`).
- σ = +1 reverses loop orientation and σ = −1 keeps it.
- STOLPERSTEINE row 200 and README rows 22/23 exist as cited.
- The design makes no claim about FeatureCollector. FeatureCollector.cs:109–116 already pulls in absorbed and shared sketches, so nothing needs to change there.

## (1) Problems

**P1. The "model space" default for the ambiguous coordinate case has no evidence behind it, and the case is common.**
- **Evidence.**
  - Both readings pass when the sketch plane is z = 0 with normal ±Z. A typical example is a sketch on the back face of a part extruded from Front: z = 0, normal −Z, in-plane axes flipped.
  - The CodeStack macro does not tell the two readings apart if its sketches lie on Front, where the transform is the identity.
  - The help for `ISketch.GetContourEdges` says that a unit Xform returns edges "in the space of the sketch" (fc2/llmdocs/x/types/ISketch/GetContourEdges.md). That points the other way.
  - The official example `Create_and_Convert_Non-manifold_Bodies` picks `SKETCHREGION` on a Front sketch at z = 0.00762. That is a ray pick from the screen, so it settles nothing.
  - A wrong guess maps the witness through mismatched frames. Typically it then selects the mirror-symmetric counterpart region, which has equal area, so no note is added. With `VerifySteps` off, StepVerifier.cs:31 returns early and the result is "OK".
- **Fix.** `RegionSpace.Decide` returns `Model | Sketch | Undecided | Error` and never defaults. When undecided, resolve in this order:
  1. A per-run cache `RegionSpace.Known`, set by the first unambiguous reading on either the source or the target side.
  2. A vote. B-rep loop vertices (`Loop2.GetVertices` → `Vertex.GetPoint`) and the centres of vertex-free circle loops are tested for coincidence (within `Tol.Length`) with the sketch's `SketchPoint.X/Y` (`ISketch.GetSketchPoints2`), under each reading.
  3. If both readings give the same polylines within 2ε, the choice does not matter.
  4. Otherwise apply the fallback policy (P7) with a note.

**P2. Checking the created feature afterwards must be mandatory.**
- **Evidence.**
  - `Create` stops at the first non-null feature (ExtrudeBuilder.cs:87–93).
  - Select return values are ignored (ExtrudeBuilder.cs:179, RevolveBuilder.cs:106).
  - The design marks reading back `GetContoursCount()` as optional.
  - If SOLIDWORKS ignores a pre-selection it does not understand (mark 0 or mark 4) and builds the whole sketch, the wrong feature is kept. It is silent with `VerifySteps` off. With `VerifySteps` on it is a warning that no alternative can fix.
  - The same latent bug exists today for contours.
- **Fix.**
  - Every `Select` must return true, otherwise skip the variant.
  - After each successful variant, and after the `ModifyDefinition` route, run `ProfileCheck`. It calls `GetDefinition`, `AccessSelections`, reads `GetContoursCount()` and `Contours`, counts items by type (`SketchRegion` before `SketchContour`), reads each returned region with `RegionCapture`, and requires every expected target witness to be Inside exactly one returned region, with no unused region. It always calls `ReleaseSelectionAccess`.
  - On a mismatch, `BuilderSupport.Delete(ctx, f)` keeps the sketch. Then try the next variant.

**P3. The area tolerance accepts a wrong-size region as "equal".**
- **Evidence (adversarial test).** A straight sliver 0.12 mm × 100 mm (ε = 10 µm) is split in the copy at x = 60 mm. The witness lands in the 60 % part:
  `src A=1.200E-005 target hit #0 A=7.200E-006 |dA|=4.800E-006 tolA=6.410E-006 -> equal area: True`.
  The 60 % part is selected with no note. The bound `2ε(Ps+Pt)` is reached even for straight edges, which have no chord error at all.
- **Fix.** Decide exactness geometrically, not by area.
  - "Exact" means a two-sided distance of at most `tolH = 2ε + 10·Tol.Length`. Every mirrored source vertex lies within tolH of the target polyline, and every target vertex lies within tolH of the mirrored source polyline.
  - Area is secondary. Use kernel areas where available: `ILoop2.GetFace()` → `IFace2.GetArea()`. That area is independent of the coordinate space, and xcad uses exactly this region→face route (`SwSketchRegion.PlanarSheetBody`, integration test `PlanarSheetSketchRegionTest`). Its tolerance is `tolA = max(1e-7·A, 2·Tol.Length·(Ps+Pt))`. Otherwise use the band-integrated polyline area (P4).

**P4. Greedy `ChainLoop` can reverse part of a loop at a pinch vertex and get the area wrong.**
- **Evidence (adversarial test).** A rectangle with a triangular hole touching the boundary at one vertex, with the same edge set chained both ways at the tie: `pinch: consistent loop area 7.5000E-004; same edges chained the other way 8.5000E-004`.
- **Fix.** Drop `ChainLoop`.
  - Weld piece endpoints (snap within `10ε`) and require even degree at every welded node, which proves the loop is closed.
  - Keep the pieces as an unordered segment set. Even-odd classification already works on that.
  - Compute polyline area by band integration, Σ Δy·L(y_mid) between consecutive distinct vertex ordinates. It is exact for polylines and independent of orientation and chaining.

**P5. Computing ε from "raw vertices" breaks for loops without vertices.**
- **Evidence (adversarial test).** A full circle has no B-rep vertex, so extent = 0 and ε is clamped to 1e-7. For r = 100 mm that gives 4097 points and **24,569 `Evaluate2` COM calls** for one circle, against 257 points at 2e-5. The target side reads every region, each shared edge twice, and does it again inside the `FeatureEdit` lambda.
- **Fix.** Take the extent from a coarse pass (8 evaluations per edge over all loops) or from `IFace2.GetBox()`. Use one ε per feature, the minimum over its source regions, and store it in the record so the target reuses it.

**P6. The witness point sits exactly where SketchBuilder places stand-ins.**
- **Evidence.**
  - The crossing-plane stand-in runs through the projection of the sketch-point centroid (SketchBuilder.cs:487–488).
  - Point stand-ins become 1 mm construction lines (SketchBuilder.cs:462).
  - The max-clearance point of a symmetric region is its centre. Adversarial test: `split at the centre x=0.05: classify vs left half = Boundary`, which leads to the whole-sketch fallback.
- **Fix.**
  - Store up to K = 8 witnesses per region, each with clearance ≥ MinClearance, taken from distinct scan bands and pairwise ≥ 2·MinClearance apart.
  - Ignore Boundary witnesses as long as at least one is Inside.
  - If Inside hits disagree, go to the cover test.

**P7. The fallbacks can damage the user's body in same-part merge mode.**
- **Evidence.**
  - `separateBody` is false when features, rather than the whole part or bodies, are selected (MirrorJob.cs:133).
  - `OriginalUntouched` only guards when `SeparateBody` is set (MirrorJob.cs:192, 232).
  - A whole-sketch fallback or a "containing larger region" cut removes extra material from the shared body. StepVerifier only warns (StepVerifier.cs:84–85).
  - Today's region and contour fallbacks have the same flaw (FeatureCapture.cs:188, BuilderSupport.cs:40).
- **Fix.** Add one policy for inexact profiles, used for regions and also for contours:
  - If `ctx.SamePart && !ctx.SeparateBody`, the feature fails and nothing is created.
  - Otherwise (new part, or separate body), build the whole sketch or the containing region with a note, which gives Warning.

**P8. The build-side ownership check would compare against the original's sketch name.**
- **Evidence.** `r.SketchName` is the original's name. In same-part mode the copy is renamed `original + NameSuffix` (BuildContext.cs:108, 113). A check of the form `SketchNameOf(picked.Sketch) == r.SketchName` accepts the original's coplanar region, for example in the σ = −1 case "Front sketch mirrored at Front". The copy would then consume the original's sketch: it would not be independent and would break when the original is deleted.
- **Fix.**
  - Compare `SwDoc.SketchNameOf(region.Sketch) == SwDoc.SketchNameOf(sketch.Sketch)`, which is the live target name.
  - Pass that name to `SelectByID2`.
  - Keep `SelectByID2` as the last resort only, because it is a ray pick (see the non-manifold example).

**P9. An exception while reading regions aborts the whole run.**
- **Evidence.**
  - `Capture` catches only `NotSupportedException` (FeatureCapture.cs:91).
  - Capture runs outside the try block in `MirrorJob.Run` (MirrorJob.cs:118–119).
  - Each of these would end the run before anything is built: a `COMException` from `GetCurveParams3` or `Evaluate2` on a temporary edge, an `InvalidCastException`, a null `Contours` array iterated in a foreach, and the `ArgumentException` thrown by the `MapPoint` Z check.
- **Fix.**
  - `RegionCapture.Read`/`ReadAll` catch `Exception` and return null with a `why`.
  - `ReadProfile` handles null and empty arrays.
  - `MapProfile` never throws; it returns null with a note.

**P10. The design does not say what happens when `MapProfile` fails inside the `FeatureEdit` lambda.**
- **Evidence.** Throwing makes `Edit` return false. The feature is then deleted and the step fails, and the scope settings in the same edit are lost too (ExtrudeBuilder.cs:136–140).
- **Fix.** Inside the lambda, leave `Contours` untouched if the fresh mapping is null and set a flag. After the edit, apply the P7 policy: in merge mode delete and fail, otherwise add a note.

**P11. Rejected trial features and stale region objects are not handled.**
- **Fix.**
  - Every variant that produces a feature rejected by `ProfileCheck` is deleted before the next try.
  - After any deletion, re-fetch the target regions and re-match them, because absorbing and un-absorbing the sketch can invalidate region objects and their order.
  - Builder exceptions are still caught by `RemoveLeftovers` (MirrorJob.cs:208).

**P12. SwSelect case order and return values.**
- **Evidence.** SwSelect.cs:24 has `case Feature f`, and line 26 has `case SketchContour c`.
- **Fix.**
  - Insert `case SketchRegion g: return g.Select2(append, data);` before `SketchContour`, for the same reason the design checks regions first at capture.
  - Callers must check the return value (see P2).

**P13. The contour-selection scope should restore the previous value, not force `false`.** `EnableContourSelection` is session state that the user may have set.

**P14. "Two source regions mapping to one target region is an error" is too strict.** If the copy lost the separating segment, the merged target region is exactly the union.
- **Fix.** Accept it when the target equals the union under the geometric test below, and add a note.

**P15. The prototype covers less than claimed and cannot be copied into Core.**
- **Evidence.**
  - regionalgo/Program.cs covers only Sample, InteriorPoint, Classify and Area. It has no ChainLoop, RegionSpace, RegionMatcher, MapPoint or cover-rule tests.
  - It uses `using P = (double X, double Y);` (C# 12), `Math.Clamp` and `l[^1]` (netstandard2.1). Spiegelwerk.Core.csproj is netstandard2.0 with LangVersion 9.0 and TreatWarningsAsErrors.
- **Fix.** Port it with a local clamp and `Count - 1`, and add the missing tests (§5).

**P16. Small corrections.**
- "Every horizontal band is hit" is false: only the 64 widest gaps and 15 uniform levels are scanned. This is harmless because acceptance relies on clearance, but the text should change.
- `_math.SketchFrame` takes a `Sketch`, so the call needs `(Sketch)sketch.GetSpecificFeature2()`.
- Use one `ReadAll` call per feature, not one per source region.

**P17. Variant 1 should also try selecting the sketch together with the regions.** Both "Create Extrude Feature Using Sketch Contours" and the non-manifold example select the SKETCH at mark 4 together with the regions or contours.

**P18. `d.Contours = object[]` may need `DispatchWrapper[]`.** The repo already needs that fallback for advanced holes (AdvancedHoleBuilder.cs:234). Retry with it in the `ModifyDefinition` route.

**P19. Dangling stand-ins.** Point stand-ins and partial edge stand-ins may appear as slits in a region's loops. Even-odd parity survives a slit, but the two-sided exactness test then fails. That correctly produces a note (Warning) instead of OK, and the cover test must not treat a slit as a split.

**P20. Missing live assumptions.**
- Does `ILoop2.GetFace()` return a face whose `GetArea()` works for region loops? xcad's integration test suggests yes.
- Can the created feature silently ignore the pre-selection?
- Do region objects stay valid after a trial feature is deleted?
- Do dangling construction lines create slits?

## (2) Corrected design: carrying over selected sketch regions of Extrude and Revolve

**Scope.** This is a read-only design. For sketch regions the answer to the user's question is yes: today they are dropped. `ReadContours` (FeatureCapture.cs:181) adds the note "Ausgewählte Skizzenbereiche … ganze Skizze". The design adds region support without any Mirror-Feature API.

**Sources.**
- Signatures were checked with a metadata probe against 27.3.0 and are identical in 32.1.0.
- help.solidworks.com is not reachable from this environment. Semantics come from:
  - the offline help extraction (fc2/llmdocs/x);
  - xcad (`SwSketchRegion`: `GetFirstLoop().IGetFace().CreateSheetBody()`, plus integration tests `RegionLoopsTest` and `PlanarSheetSketchRegionTest`, which confirm outer and inner loops and a face area for regions);
  - CodeStack.

### 0. Problems in today's code

The design's three points hold:
1. **Mixed selections.** The note and the result disagree.
2. **Selection mark.** Contours are pre-selected with mark 0, while the examples use mark 4 with contour selection enabled.
3. **Stale objects.** `contoursPending` assigns objects fetched before creation.

Two more:
4. **Select return values are ignored, and nothing checks what the created feature actually used** (P2).
5. **The whole-sketch fallback can cut the user's body in same-part merge mode** (P7).

### 1. API (27.3.0)

Copy the design's §1 as is, with these additions:
- `ILoop2.GetFace()` / `Face2 IGetFace()` → `IFace2.GetArea()` and `IFace2.GetBox()`. This gives the kernel area, which does not depend on the coordinate space; it falls back to polylines if the face is null.
- `Loop2.GetVertices()` → `Vertex.GetPoint()`, and `ISketch.GetSketchPoints2()` → `SketchPoint.X/Y` in sketch coordinates. These are used for the coordinate-space vote.
- `SelectByID2(..., "SKETCHREGION", ...)` is a ray pick (the official non-manifold example picks at z = 7.62 mm on a Front sketch). It is the last fallback only, with the live target sketch name and a `.Sketch` check.

The coordinate space of region geometry is undocumented and is never defaulted (§2.3).

### 2. Identifying the region: interior witnesses plus a geometric identity test

Keep the argument that segment ids cannot identify a region (the lens and crescents example) and that a point survives the mirror exactly.

**Pipeline (Core unless marked add-in):**

1. **Sample.** `RegionGeometry.Sample(eval, u0, u1, ε, minPieces = 8, maxDepth = 14)` as designed, checking the midpoint and both quarter points. ε = clamp(1e-4·extent, 1e-7, 1e-4), where extent comes from a coarse 8-point pass per edge or from `Face2.GetBox`, never from B-rep vertices alone. Use one ε per feature: the minimum over its source regions, stored on the record and reused for the target.

2. **Weld instead of chaining.** `RegionGeometry.Weld(pieces, 10ε)` snaps endpoints together. It returns null if any welded node has odd degree, meaning the loop is open. The result is an unordered segment set.

3. **Coordinate space.** `RegionSpace.Decide(raw, frame, sketchPoints, known)`:
   - Model space: every point satisfies |frame.ToLocal(p).Z| ≤ 10 µm.
   - Sketch space: every point satisfies |p.Z| ≤ 10 µm.
   - If exactly one holds, use it and set `known`.
   - If both hold, use `known`. Without it, vote by coincidence of loop vertices and circle-loop centres with sketch points. If both readings give the same polylines within 2ε, either is fine.
   - Otherwise `Undecided` → fallback policy.
   - If neither holds, it is an error.

4. **Witnesses.** `FindInteriorPoints(segments, ε, k = 8)`.
   - Scan levels: the midpoints of the 64 widest vertex-ordinate gaps, plus 15 uniform levels.
   - A segment crosses a level by the half-open rule. If the number of crossings is odd, skip the level.
   - Candidates are the midpoints of the inside intervals, with exact clearance. Refine with alternating vertical and horizontal rescans.
   - Accept witnesses with clearance ≥ MinClearance = 4ε + 10·Tol.Length, taken from distinct bands and ≥ 2·MinClearance apart.
   - If there is none, return null, which triggers the fallback.
   - The margin argument is unchanged: what remains to the target polyline is ≥ 2ε + 9·Tol.Length, which is more than the classification margin ε + Tol.Length.

5. **MapPoint** as designed, with `Mirror()` refactored to use it. It throws on |Z| > Tol.Length, and the add-in catches that.

6. **Classify** as designed: Boundary if the distance is below the margin, otherwise even-odd with the half-open rule.

7. **Area.** Use the kernel area if available. Otherwise use band integration over the polyline: exact for the polyline and independent of orientation.

8. **Match.** `RegionMatcher.Match(sources: mirrored ProfileRegions, targets, allowCover) → RegionMatch {Success, Indices, Notes, Reason}`, using tolH = 2ε + 10·Tol.Length.
   1. **Hits.** For each source, collect the target regions in which some witness is Inside. Boundary witnesses are ignored if at least one witness is Inside. If there are no hits, the match fails.
   2. **Exact.** There is one hit T, and the two-sided vertex-to-polyline distance between mirrored S and T is ≤ tolH. Select T with no note.
   3. **Cover** (only when `allowCover`, i.e. not thin). Take a set H of targets where:
      - every mirrored-source vertex lies within tolH of the boundary of the union of H;
      - every vertex of those targets is within tolH of the source boundary or Inside the mirrored S;
      - the witnesses of every T in H are Inside S;
      - (secondary) the summed kernel area equals A within tolA.
      Select all of H and add a note.
   4. **Merge.** Several sources hit the same T, and T equals their union under the test in step 3 with the roles swapped. Select T once and add a note.
   5. **Otherwise** it is a mismatch, handled by the policy in §3. The note quotes both areas.

### 3. Changes

**Core**
- `ProfileRegion` with `Segments` (unmirrored source sketch coordinates, Z = 0), `Witnesses`, `MinClearance`, `Area` (kernel or polyline), `Perimeter`, and `Map(Func<Vec3,Vec3>)`.
- `ExtrudeRecord` and `RevolveRecord` get `Regions` and `RegionChordTolerance`. Update the doc comment: Contours and Regions together form the selection; both empty means the whole sketch.
- `Sketch/RegionGeometry.cs` (Sample, Weld, FindInteriorPoints, Classify, Area, Distance), `Sketch/RegionSpace.cs`, `Sketch/RegionMatcher.cs`, and `SketchMirror.MapPoint`.
- netstandard2.0 / C# 9 only: no `Math.Clamp`, no index-from-end, no tuple alias.

**Add-in capture**
- `Capture/RegionCapture.cs`: `ProfileRegion? Read(SketchRegion, Frame3, double? ε, IReadOnlyList<Vec3> sketchPoints, out string? why)` and `ReadAll(Sketch, Frame3, double ε, …)`. Both wrap everything in `catch (Exception)` and return `why`.
- `FeatureCapture.ReadProfile(Feature sketch, object[]? items, …)` replaces `ReadContours`, inside the same `AccessSelections` block:
  - Test `SketchRegion` before `SketchContour`.
  - Use `_math.SketchFrame((Sketch)sketch.GetSpecificFeature2())`.
  - Check `SketchNameOf(region.Sketch) == sketch.Name`.
  - All or nothing: any failure clears both lists and adds one note giving the reason.

**Add-in build**
- **SwSelect:** add `case SketchRegion g: return g.Select2(append, data);` before `case SketchContour`.
- **Contour-selection scope:** `ContourSelectionScope : IDisposable`. It saves `EnableContourSelection`, sets it to true, and restores the saved value in `Dispose`.
- **`BuilderSupport.MapProfile(ctx, sketch, record, thin, notes) → Profile?`** with `Items` (contours and regions) and target `Witnesses`, for `ProfileCheck`.
  - Contours go through `MapContours` unchanged.
  - Regions: `ReadAll` on the target sketch with `sketch.Mirrored.Sketch.Frame` and the record's ε; map the sources with `MapPoint`; run `Match(allowCover: !thin)`.
  - It never throws.
- **`ProfileCheck.Verify(ctx, f, profile) → bool`.** Read the created feature's `Contours` under `AccessSelections` (always released). Counts per type must match, each expected witness must be Inside exactly one returned region, and every returned region must hold one.
- **Fallback policy `InexactProfile(ctx)`.** If `ctx.SamePart && !ctx.SeparateBody`, the feature fails and nothing is created. Otherwise build the whole sketch or the containing region and add a note (Warning). Use it for regions, contours, capture failures, mismatches, and a failed fresh mapping inside the lambda.
- **`ExtrudeBuilder.Create` variants, in order.** Every `Select` must return true, `ProfileCheck` runs after each created feature, and a rejected feature is deleted with `Delete(ctx, f)` and `MapProfile` re-run before the next variant:
  1. Contour selection on; profile items at mark 4.
  2. Contour selection on; sketch feature and items at mark 4.
  3. Mark 0, as today.
  4. Whole sketch; then, inside `FeatureEdit`, a fresh `MapProfile`, `d.Contours = object[]`, and a retry with `DispatchWrapper[]`; then `ProfileCheck`.

  The existing temporary state and the ThroughAllBoth fallback for cuts are kept. The final `FeatureEdit` (scope, references) runs only after a profile is accepted. Inside the lambda nothing throws (P10).
- **`RevolveBuilder` variants:**
  1. Items at mark 4 with the axis at 16.
  2. Sketch and items at mark 4 with the axis at 16.
  3. Items at mark 0 with the axis at 16, then with the axis at 4.
  4. Whole sketch plus `ModifyDefinition`.

  Never use mark 4 for both the profile and the axis. Each variant is checked as for extrudes.
- **SelectByID2 fallback** (only if objects cannot be selected): Name is the live target sketch name; read back with `GetSelectedObject6(1,-1)`; require `SketchNameOf(region.Sketch) == SketchNameOf(sketch.Sketch)`.
- **Verification is unchanged.** It is a second line of defence. With the identity test and `ProfileCheck`, a result is "OK" only if the region is identical and the created feature verifiably uses it. Cover, merge and mismatch always add a note.

**Docs**
- STOLPERSTEINE:
  - regions have no segments;
  - the coordinate space is unknown and is decided, never defaulted;
  - witnesses and clearance;
  - the identity test, cover and merge;
  - stand-ins at the sketch centre;
  - read-back after creation;
  - the merge-mode policy;
  - update row 200 to describe the mark-4 + EnableContourSelection, mark-0 and ModifyDefinition variants, each with read-back.
- README rows 22/23: "ausgewählte Konturen und Skizzenbereiche".
- ARCHITEKTUR has no contour row. Add one sentence to the capture section (line 27) and to the separate-body/merge paragraph (line 239).

### 4. Edge cases

Keep the design's table, with these changes:

| Case | Handling |
|---|---|
| Ring | Segment set plus even-odd. Area by kernel or band integration, not by shoelace on chains. |
| Pinch vertex | Weld and degree check; band-integrated area. |
| Stand-in through the centre, or a dangling stand-in | Several witnesses. A slit fails the exactness test, so the result is a note, never a silent OK. |
| Split by a stand-in | Cover (not thin) with a note; when thin, mismatch policy. |
| Lost separating segment | Merge with a note. |
| Text dropped from the copy | Mismatch policy. |
| Same-part merge mode, inexact profile | The feature fails. |
| Ambiguous coordinate space | Cache, then vote, then the invariance check, otherwise the fallback policy. |
| σ = ±1 | As designed. |
| Shared sketch | Regions are re-read per feature. |

### 5. Implementation plan and Core tests

1. `MapPoint` and the `Mirror` refactor. Test that `MapPoint` equals `Mirror` for random frames with σ = ±1, and that the Z check throws.
2. `RegionGeometry`:
   - the design's tests;
   - pinch area (7.5e-4, regardless of edge order);
   - shuffled and reversed segments;
   - a weld gap inside and outside tolerance;
   - ε for a vertex-free circle from the coarse extent;
   - K witnesses spread over the region;
   - a sliver at 2.5× and 1.5× MinClearance.
3. `RegionSpace`:
   - Top and Right planes decide uniquely;
   - Front with identity frame;
   - a z = 0 face with normal −Z resolved by vote, by cache, and as Undecided;
   - off-plane points give Error.
4. `ProfileRegion` and the records: `Map` keeps area and clearance, and witnesses stay Inside.
5. `RegionMatcher`:
   - left half of a rectangle with a centerline, σ = ±1;
   - ring and disc;
   - an adjacent pair;
   - cover (split by a line) and cover off when thin;
   - a centre split with K witnesses, which matches the cover instead of falling to Boundary;
   - the 60 % sliver part, which is a mismatch and not "equal";
   - merge of two sources;
   - a slit, which is not exact and gives a note;
   - no hit.
6. `RegionCapture` and `ReadProfile`: all or nothing, exceptions turned into notes.
7. `SwSelect`, the scope, `MapProfile`, `ProfileCheck`, `InexactProfile`.
8. The `ExtrudeBuilder` and `RevolveBuilder` variants, with delete and refetch.
9. Docs.
10. Build against 27.3.0 and 32.1.0 and run the Core tests.
11. Live test (§6).

### 6. Live-test assumptions

Keep the design's items 1–10, with item 2 reworded:

2. The coordinate space of region loops. Run it once and then hard-code `RegionSpace.Known`.

Add:

11. `ILoop2.GetFace()` returns a face for region loops, and its `GetArea()` equals the region area.
12. Whether a feature built with an unrecognised pre-selection silently uses the whole sketch. Read-back catches this either way.
13. Region objects after a trial feature is deleted: are they still valid, and is their order stable?
14. Do dangling construction lines (stand-ins) create slits or extra regions?
15. Does `d.Contours` accept `object[]`, or does it need `DispatchWrapper[]`?
16. Do two adjacent regions in one feature with Merge off give one body or two?