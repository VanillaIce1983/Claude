import { describe, expect, it } from 'vitest';
import { regenerate } from '../src/kernel/doc';
import type { EdgeFlangeFeature, Feature, SketchCutFeature } from '../src/kernel/features/types';
import type { DimTarget, SketchDim, SketchEntity } from '../src/kernel/sketch';
import { entityLines, entityPoints, measureDim, overDefines, resolveModelPoint, sketchContext, solveSketch } from '../src/kernel/sketchSolve';
import { build, doc, edgeRef, plate } from './helpers';

const rect = (id: string, cx: number, cy: number, w: number, h: number): SketchEntity => ({ kind: 'rect', id, cx, cy, w, h, angle: 0 });
const circle = (id: string, cx: number, cy: number, r: number): SketchEntity => ({ kind: 'circle', id, cx, cy, r });
const pt = (entity: string, point: string): DimTarget => ({ kind: 'point', ref: { entity, point } });
const line = (entity: string, l: string): DimTarget => ({ kind: 'line', ref: { entity, line: l } });
const dist = (id: string, a: DimTarget, b: DimTarget, orient: 'h' | 'v' | 'aligned', value: number): SketchDim => ({ id, kind: 'distance', a, b, orient, value });

/** A plate 100 × 50 with a sketched cut on its top face; the reference edge is base:e0 (x along it, y into the face). */
function cutDoc(entities: SketchEntity[], dims: SketchDim[], plateW = 100): { features: Feature[]; cut: SketchCutFeature } {
  const cut: SketchCutFeature = { id: 'sc', name: 'Ausschnitt1', type: 'sketchCut', sketch: { edge: { tag: 'base:e0', at: { x: plateW / 2, y: 0 } }, anchor: 'line', entities, dims } };
  return { features: [plate(plateW, 50), cut], cut };
}

describe('sketch dimensions', () => {
  it('names the points and lines of each entity', () => {
    expect(entityPoints(rect('r', 10, 5, 4, 2)).map((p) => p.name)).toEqual(['c', 'p0', 'p1', 'p2', 'p3']);
    expect(entityPoints(rect('r', 10, 5, 4, 2))[1].p).toEqual({ x: 8, y: 4 });
    expect(entityLines(rect('r', 10, 5, 4, 2)).map((l) => l.name)).toEqual(['e0', 'e1', 'e2', 'e3']);
    expect(entityPoints(circle('c', 1, 2, 3))).toEqual([{ name: 'c', p: { x: 1, y: 2 } }]);
  });

  it('drives sizes and positions and keeps the rest where it was', () => {
    const ents = [rect('r', 30, 20, 10, 8), circle('k', 70, 25, 3)];
    const dims = [
      dist('w', pt('r', 'p0'), pt('r', 'p1'), 'h', 16),
      dist('cx', pt('k', 'c'), pt('r', 'c'), 'h', 40),
      { id: 'd', kind: 'diameter', entity: 'k', value: 10 } as SketchDim,
    ];
    const res = solveSketch(ents, dims, null);
    expect(res.ok).toBe(true);
    const [r, k] = res.entities as [Extract<SketchEntity, { kind: 'rect' }>, Extract<SketchEntity, { kind: 'circle' }>];
    expect(r.w).toBeCloseTo(16, 9);
    expect(r.h).toBeCloseTo(8, 9);
    expect(k.r).toBeCloseTo(5, 9);
    // signed distance keeps the circle to the right of the rectangle, 40 apart
    expect(k.cx - r.cx).toBeCloseTo(40, 9);
    expect(r.cy).toBeCloseTo(20, 9);
    expect(k.cy).toBeCloseTo(25, 9);
  });

  it('reports conflicting dimensions and over-defining ones', () => {
    const ents = [rect('r', 30, 20, 10, 8)];
    const a = dist('a', pt('r', 'p0'), pt('r', 'p1'), 'h', 16);
    const b = dist('b', line('r', 'e1'), line('r', 'e3'), 'aligned', 12);
    expect(solveSketch(ents, [a, b], null).ok).toBe(false);
    expect(overDefines(ents, [a], b, null)).toBe(true);
    expect(overDefines(ents, [a], dist('c', pt('r', 'p1'), pt('r', 'p2'), 'v', 5), null)).toBe(false);
  });

  it('marks entities fully defined once size and position are dimensioned', () => {
    const ents = [circle('k', 70, 25, 3)];
    const sz: SketchDim = { id: 'd', kind: 'diameter', entity: 'k', value: 8 };
    expect(solveSketch(ents, [sz], null).defined.has('k')).toBe(false);
    const { features } = cutDoc(ents, []);
    const part = build(doc([features[0]])).part;
    const sketch = { edge: { tag: 'base:e0', at: { x: 50, y: 0 } }, anchor: 'line' as const, entities: ents };
    const ctx = sketchContext(part, sketch);
    const corner: DimTarget = { kind: 'modelPoint', ref: { prevTag: 'base:e3', nextTag: 'base:e0', at: { x: 0, y: 0 }, side: 'top' } };
    expect(resolveModelPoint(ctx, (corner as Extract<DimTarget, { kind: 'modelPoint' }>).ref)).toEqual({ x: 0, y: 0 });
    const res = solveSketch(ents, [sz, dist('x', corner, pt('k', 'c'), 'h', 20), dist('y', corner, pt('k', 'c'), 'v', 15)], ctx);
    expect(res.ok).toBe(true);
    expect(res.defined.has('k')).toBe(true);
    const k = res.entities[0] as Extract<SketchEntity, { kind: 'circle' }>;
    expect([k.cx, k.cy, k.r]).toEqual([expect.closeTo(20, 9), expect.closeTo(15, 9), expect.closeTo(4, 9)]);
  });

  it('follows the part: a hole dimensioned from the far corner moves when the plate gets longer', () => {
    const corner: DimTarget = { kind: 'modelPoint', ref: { prevTag: 'base:e0', nextTag: 'base:e1', at: { x: 100, y: 0 }, side: 'top' } };
    const dims = [dist('x', pt('k', 'c'), corner, 'h', 20), dist('y', corner, pt('k', 'c'), 'v', 10)];
    for (const w of [100, 160]) {
      const { features } = cutDoc([circle('k', 80, 10, 4)], dims, w);
      const r = build(doc(features));
      const hole = r.part.faces.get('base')!.holes[0].pts;
      const cx = hole.reduce((s, p) => s + p.x, 0) / hole.length;
      expect(cx).toBeCloseTo(w - 20, 6);
    }
  });

  it('dimensions to a corner of another face project onto the sketch plane', () => {
    // a flange on e1 (x = 100) standing up; its free top corner projects onto x = 100 - (R + T) … the outer surface x = 100
    const r0 = build(doc([plate()]));
    const fl: EdgeFlangeFeature = { id: 'fl', name: 'K', type: 'edgeFlange', edges: [edgeRef(r0.part, 'base:e1')], angle: 90, length: 25, lengthRef: 'outer', position: 'materialInside', reverse: false, width: { mode: 'full', offset1: 0, offset2: 0, width: 0 } };
    const part = build(doc([plate(), fl])).part;
    const end = [...part.faces.get('fl')!.outer.pts.entries()].find(([i]) => part.faces.get('fl')!.outer.tags[i] === 'fl:side1')!;
    const ring = part.faces.get('fl')!.outer;
    const i = ring.tags.indexOf('fl:side1');
    const n = ring.pts.length;
    const ref = { prevTag: ring.tags[(i - 1 + n) % n], nextTag: ring.tags[i], at: ring.pts[i], side: 'bottom' as const };
    expect(end).toBeDefined();
    const ctx = sketchContext(part, { edge: { tag: 'base:e0', at: { x: 50, y: 0 } }, anchor: 'line', entities: [] });
    const p = resolveModelPoint(ctx, ref);
    // the flange's outer surface lies in the plane x = 100 (material inside)
    expect(p.x).toBeCloseTo(100, 6);
  });

  it('a cut with dimensions that contradict each other reports it', () => {
    const { features } = cutDoc([rect('r', 30, 20, 10, 8)], [dist('a', pt('r', 'p0'), pt('r', 'p1'), 'h', 16), dist('b', pt('r', 'p0'), pt('r', 'p1'), 'h', 18)]);
    const st = regenerate(doc(features)).status.get('sc')!;
    expect(st.ok).toBe(false);
    expect(st.error).toMatch(/widersprechen sich/);
  });

  it('measures point to line perpendicular and keeps the side', () => {
    const ents = [rect('r', 30, 20, 10, 8), circle('k', 60, 10, 2)];
    const d = dist('d', line('r', 'e1'), pt('k', 'c'), 'aligned', 5);
    const m = new Map(ents.map((e) => [e.id, e]));
    expect(measureDim(d, m, new Map())).toBeCloseTo(-25, 9);
    const res = solveSketch(ents, [d], null);
    const k = res.entities[1] as Extract<SketchEntity, { kind: 'circle' }>;
    const r = res.entities[0] as Extract<SketchEntity, { kind: 'rect' }>;
    expect(k.cx - (r.cx + r.w / 2)).toBeCloseTo(5, 6);
  });
  it('moves a shape for a position dimension instead of also resizing it', () => {
    const corner: DimTarget = { kind: 'modelPoint', ref: { prevTag: 'base:e3', nextTag: 'base:e0', at: { x: 0, y: 0 }, side: 'top' } };
    const { features } = cutDoc([], []);
    const part = build(doc([features[0]])).part;
    const ctx = sketchContext(part, { edge: { tag: 'base:e0', at: { x: 50, y: 0 } }, anchor: 'line', entities: [] });
    // the left side of the rectangle 10 from the corner: only the rectangle's position may change
    const res = solveSketch([rect('r', 50, 25, 30, 20)], [dist('x', corner, pt('r', 'p0'), 'h', 10)], ctx);
    expect(res.ok).toBe(true);
    const r = res.entities[0] as Extract<SketchEntity, { kind: 'rect' }>;
    expect(r.w).toBe(30);
    expect(r.h).toBe(20);
    expect(r.cx).toBeCloseTo(25, 9);
    // when moving is not enough, sizes give way
    const both = solveSketch([rect('r', 50, 25, 30, 20)], [dist('x', corner, pt('r', 'p0'), 'h', 10), dist('w', pt('r', 'p0'), pt('r', 'p1'), 'h', 40)], ctx);
    expect(both.ok).toBe(true);
    const r2 = both.entities[0] as Extract<SketchEntity, { kind: 'rect' }>;
    expect([r2.cx - r2.w / 2, r2.w]).toEqual([expect.closeTo(10, 9), expect.closeTo(40, 9)]);
  });

  it('keeps each signed dimension on the side it had before a drag', () => {
    const before = [circle('a', 10, 10, 2), circle('b', 40, 10, 2)];
    const dims = [dist('x', pt('a', 'c'), pt('b', 'c'), 'h', 30)];
    // b dragged past a: the dimension pulls it back to the right of a, not to the left
    const dragged = [before[0], circle('b', -5, 10, 2)];
    const keep = solveSketch(dragged, dims, null, { sidesFrom: before, sticky: { entity: 'b' } });
    expect(keep.ok).toBe(true);
    const [a, b] = keep.entities as Extract<SketchEntity, { kind: 'circle' }>[];
    expect(b.cx - a.cx).toBeCloseTo(30, 9);
    // the dragged circle holds still as far as it can; the other one follows
    expect(b.cx).toBeCloseTo(-5, 9);
    const flip = solveSketch(dragged, dims, null);
    expect((flip.entities[1] as Extract<SketchEntity, { kind: 'circle' }>).cx - (flip.entities[0] as Extract<SketchEntity, { kind: 'circle' }>).cx).toBeCloseTo(-30, 9);
  });

  it('holds a sticky parameter while the others give way', () => {
    const ents = [circle('a', 10, 10, 2), circle('b', 40, 10, 2)];
    const dims = [dist('x', pt('a', 'c'), pt('b', 'c'), 'h', 30)];
    const edited = [circle('a', 20, 10, 2), ents[1]];
    const res = solveSketch(edited, dims, null, { sidesFrom: ents, sticky: { entity: 'a', params: ['cx'] } });
    const [a, b] = res.entities as Extract<SketchEntity, { kind: 'circle' }>[];
    expect(a.cx).toBeCloseTo(20, 9);
    expect(b.cx - a.cx).toBeCloseTo(30, 9);
    // a held parameter that the dimensions fix gives way after all
    const part = build(doc([plate(100, 50)])).part;
    const ctx = sketchContext(part, { edge: { tag: 'base:e0', at: { x: 50, y: 0 } }, anchor: 'line', entities: [] });
    const corner: DimTarget = { kind: 'modelPoint', ref: { prevTag: 'base:e3', nextTag: 'base:e0', at: { x: 0, y: 0 }, side: 'top' } };
    const pinned = solveSketch(edited, [...dims, dist('p', corner, pt('a', 'c'), 'h', 10)], ctx, { sidesFrom: ents, sticky: { entity: 'a', params: ['cx'] } });
    expect(pinned.ok).toBe(true);
    expect((pinned.entities[0] as Extract<SketchEntity, { kind: 'circle' }>).cx).toBeCloseTo(10, 9);
    expect((pinned.entities[1] as Extract<SketchEntity, { kind: 'circle' }>).cx).toBeCloseTo(40, 9);
  });
});
