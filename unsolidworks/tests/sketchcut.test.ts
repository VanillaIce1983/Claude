import { describe, expect, it } from 'vitest';
import { regenerate } from '../src/kernel/doc';
import { flatToDxf } from '../src/kernel/export/dxf';
import type { CutFeature, EdgeFlangeFeature, Feature, SketchCutFeature } from '../src/kernel/features/types';
import { allEdges, findEdge } from '../src/kernel/model';
import type { Part } from '../src/kernel/model';
import { distPointSegment, signedArea } from '../src/kernel/polygon';
import { SketchEntity, entityOutline, flatToSketch, sketchFrame } from '../src/kernel/sketch';
import { build, checkClosed, doc, edgeRef, expectedVolume, plate } from './helpers';

const BEND_MSG = 'Der Ausschnitt berührt eine Biegung. Ausschnitte über Biegungen sind nicht möglich – bitte die Skizze von der Biegelinie wegrücken.';
const TOUCH_MSG = 'Der Ausschnitt berührt die Kontur oder einen anderen Ausschnitt nur in einem Punkt. Bitte die Skizze etwas verschieben.';

const onE0 = { tag: 'base:e0', at: { x: 50, y: 0 } };
const onE2 = { tag: 'base:e2', at: { x: 50, y: 50 } };

const sketchCut = (entities: SketchEntity[], edge = onE0, id = 'sc'): SketchCutFeature => ({
  id,
  name: 'Ausschnitt1',
  type: 'sketchCut',
  sketch: { edge, entities },
});

const circle = (id: string, cx: number, cy: number, r: number): SketchEntity => ({ kind: 'circle', id, cx, cy, r });
const rect = (id: string, cx: number, cy: number, w: number, h: number, angle = 0): SketchEntity => ({ kind: 'rect', id, cx, cy, w, h, angle });

const flange = (over: Partial<EdgeFlangeFeature> = {}): EdgeFlangeFeature => ({
  id: 'fl',
  name: 'Kantenlasche1',
  type: 'edgeFlange',
  edges: [{ tag: 'base:e0', at: { x: 50, y: 0 }, side: 'top' }],
  angle: 90,
  length: 30,
  lengthRef: 'outer',
  position: 'materialInside',
  reverse: false,
  width: { mode: 'full', offset1: 0, offset2: 0, width: 0 },
  ...over,
});

const outlineArea = (e: SketchEntity): number => Math.abs(signedArea(entityOutline(e)));

const hole = (id: string, edge: { tag: string; at: { x: number; y: number } }, u: number, v: number, over: Partial<CutFeature> = {}): CutFeature => ({
  id,
  name: id,
  type: 'cut',
  edge,
  u,
  v,
  shape: 'circle',
  diameter: 8,
  width: 0,
  height: 0,
  angle: 0,
  count: 1,
  spacing: 0,
  ...over,
});

/** Base flange with a slanted right edge base:e1 from (100, 0) to (x2, 50). */
const trapezoid = (x2: number): Feature => ({
  ...plate(),
  points: [
    { x: 0, y: 0 },
    { x: 100, y: 0 },
    { x: x2, y: 50 },
    { x: 0, y: 50 },
  ],
} as Feature);

function expectSolid(r: ReturnType<typeof build>): void {
  expect(checkClosed(r.mesh.indices).bad).toBe(0);
  expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
}

function errorOf(features: Feature[], id = 'sc'): string | undefined {
  const s = regenerate(doc(features)).status.get(id);
  expect(s?.ok).toBe(false);
  return s?.error;
}

const tagsOf = (part: Part, tag: string) => allEdges(part).filter((e) => e.tag === tag);

describe('Ausschnitt (sketched cut)', () => {
  it('circle inside the face becomes a true round hole', () => {
    const c = circle('c', 30, 20, 5);
    const r = build(doc([plate(), sketchCut([c])]));
    expectSolid(r);
    expect(r.part.faces.get('base')!.holes).toHaveLength(1);
    expect(r.fp.area).toBeCloseTo(5000 - outlineArea(c), 6);
    const circles = r.fp.loops.filter((l) => l.circle);
    expect(circles).toHaveLength(1);
    expect(circles[0].circle!.r).toBeCloseTo(5, 9);
    expect(circles[0].circle!.c.x).toBeCloseTo(30, 9);
    expect(circles[0].circle!.c.y).toBeCloseTo(20, 9);
    expect(flatToDxf(r.fp)).toContain('CIRCLE');
    for (const t of ['base:e0', 'base:e1', 'base:e2', 'base:e3']) expect(tagsOf(r.part, t)).toHaveLength(1);
  });

  it('rectangle over an edge notches the outline and keeps the edge names', () => {
    const r = build(doc([plate(), sketchCut([rect('n', 50, 0, 20, 20)])]));
    expectSolid(r);
    const face = r.part.faces.get('base')!;
    expect(face.holes).toHaveLength(0);
    expect(face.outer.pts).toHaveLength(8);
    expect(r.fp.area).toBeCloseTo(5000 - 200, 6);
    const e0 = tagsOf(r.part, 'base:e0');
    expect(e0).toHaveLength(2);
    expect(e0.map((e) => [e.a.x, e.b.x]).sort((a, b) => a[0] - b[0])).toEqual([
      [0, 40],
      [60, 100],
    ]);
    for (const t of ['base:e1', 'base:e2', 'base:e3']) expect(tagsOf(r.part, t)).toHaveLength(1);
    expect(tagsOf(r.part, 'sc:n')).toHaveLength(3);
  });

  it('two overlapping circles merge into one hole that is not a circle', () => {
    const a = circle('a', 40, 25, 8);
    const b = circle('b', 50, 25, 8);
    const r = build(doc([plate(), sketchCut([a, b])]));
    expectSolid(r);
    const holes = r.part.faces.get('base')!.holes;
    expect(holes).toHaveLength(1);
    expect(new Set(holes[0].tags)).toEqual(new Set(['sc:a', 'sc:b']));
    expect(r.fp.loops.filter((l) => l.circle)).toHaveLength(0);
    expect(flatToDxf(r.fp)).not.toContain('CIRCLE');
    expect(r.fp.area).toBeLessThan(5000 - outlineArea(a));
    expect(r.fp.area).toBeGreaterThan(5000 - 2 * outlineArea(a));
  });

  it('slot and polygon become holes, a polyline notches a corner', () => {
    const slot: SketchEntity = { kind: 'slot', id: 'sl', x1: 15, y1: 25, x2: 35, y2: 30, width: 8 };
    const hex: SketchEntity = { kind: 'polygon', id: 'hx', cx: 65, cy: 25, r: 10, sides: 6, angle: 15 };
    const tri: SketchEntity = {
      kind: 'polyline',
      id: 'tri',
      points: [
        { x: -5, y: -5 },
        { x: 15, y: -5 },
        { x: -5, y: 15 },
      ],
    };
    const r = build(doc([plate(), sketchCut([slot, hex, tri])]));
    expectSolid(r);
    const face = r.part.faces.get('base')!;
    expect(face.holes).toHaveLength(2);
    for (const h of face.holes) expect(new Set(h.tags).size).toBe(1);
    // the triangle removes the corner triangle (0,0) (10,0) (0,10)
    expect(r.fp.area).toBeCloseTo(5000 - outlineArea(slot) - outlineArea(hex) - 50, 6);
    expect(tagsOf(r.part, 'sc:tri')).toHaveLength(1);
  });

  it('rotated rectangle and the frame of another edge', () => {
    // base:e2 runs from (100, 50) to (0, 50), so sketch y points down into the plate
    const r = build(doc([plate(), sketchCut([rect('r', 20, 15, 10, 6, 30)], onE2)]));
    expectSolid(r);
    const hole = r.part.faces.get('base')!.holes[0];
    const cx = hole.pts.reduce((s, p) => s + p.x, 0) / hole.pts.length;
    const cy = hole.pts.reduce((s, p) => s + p.y, 0) / hole.pts.length;
    expect(cx).toBeCloseTo(80, 9);
    expect(cy).toBeCloseTo(35, 9);
    expect(r.fp.area).toBeCloseTo(5000 - 60, 6);
  });

  it('merges with an existing hole and keeps untouched round holes round', () => {
    const cut = (id: string, u: number): CutFeature => ({
      id,
      name: id,
      type: 'cut',
      edge: onE0,
      u,
      v: 25,
      shape: 'circle',
      diameter: 10,
      width: 0,
      height: 0,
      angle: 0,
      count: 1,
      spacing: 0,
    });
    const r = build(doc([plate(), cut('h1', 20), cut('h2', 80), sketchCut([rect('m', 25, 25, 10, 4)])]));
    expectSolid(r);
    const holes = r.part.faces.get('base')!.holes;
    expect(holes).toHaveLength(2);
    expect(holes.some((h) => h.tags.includes('h1:h0') && h.tags.includes('sc:m'))).toBe(true);
    const circles = r.fp.loops.filter((l) => l.circle);
    expect(circles).toHaveLength(1);
    expect(circles[0].circle!.c.x).toBeCloseTo(80, 9);
  });

  it('a cut touching or crossing a bend is rejected', () => {
    // material inside trims the base face back to the seam at y = R + T = 4
    expect(errorOf([plate(), flange(), sketchCut([rect('r', 50, 46, 10, 10)], onE2)])).toBe(BEND_MSG);
    expect(errorOf([plate(), flange(), sketchCut([rect('r', 50, 41, 10, 10)], onE2)])).toBe(BEND_MSG);
    // a profile that swallows the whole seam
    expect(errorOf([plate(), flange(), sketchCut([rect('r', 50, 45, 120, 20)], onE2)])).toBe(BEND_MSG);
  });

  it('a cut near a bend keeps the seam untouched', () => {
    const r1 = build(doc([plate(), flange()]));
    const r = build(doc([plate(), flange(), sketchCut([rect('r', 50, 40, 10, 10), rect('s', 0, 30, 10, 10)], onE2)]));
    expectSolid(r);
    const seam = (p: Part) => allEdges(p).filter((e) => e.tag === 'seam:fl:A').map((e) => [e.a, e.b]);
    expect(seam(r.part)).toEqual(seam(r1.part));
    expect(seam(r.part)).toHaveLength(1);
  });

  it('rejects cuts that split the face, remove it or miss it', () => {
    const split = errorOf([plate(), sketchCut([rect('r', 50, 25, 10, 60)])]);
    expect(split).toBe('Der Ausschnitt würde die Fläche zerteilen oder ganz entfernen.');
    expect(errorOf([plate(), sketchCut([rect('r', 50, 25, 200, 200)])])).toBe(split);
    expect(errorOf([plate(), sketchCut([circle('c', 50, 80, 5)])])).toBe('Der Ausschnitt liegt außerhalb der Fläche.');
  });

  it('rejects empty sketches and broken profiles', () => {
    expect(errorOf([plate(), sketchCut([])])).toMatch(/leer/);
    expect(errorOf([plate(), sketchCut([circle('c', 50, 25, 0)])])).toMatch(/Durchmesser/);
    const bowtie: SketchEntity = {
      kind: 'polyline',
      id: 'b',
      points: [
        { x: 10, y: 10 },
        { x: 30, y: 30 },
        { x: 30, y: 10 },
        { x: 10, y: 30 },
      ],
    };
    expect(errorOf([plate(), sketchCut([bowtie])])).toMatch(/überschneidet sich selbst/);
    // a diamond whose tip touches base:e0 from inside
    expect(errorOf([plate(), sketchCut([rect('d', 50, 5 * Math.SQRT2, 10, 10, 45)])])).toMatch(/nur in einem Punkt/);
  });

  it('edges left by a notch still take flanges', () => {
    const notch = sketchCut([rect('n', 50, 50, 20, 20)]);
    const r1 = build(doc([plate(), notch]));
    expect(tagsOf(r1.part, 'base:e2')).toHaveLength(2);
    const r = build(doc([plate(), notch, flange({ edges: [edgeRef(r1.part, 'base:e1')] })]));
    expectSolid(r);
    expect(r.part.bends.size).toBe(1);

    const notch0 = sketchCut([rect('n', 50, 0, 20, 20)]);
    const left = { tag: 'base:e0', at: { x: 20, y: 0 }, side: 'top' as const };
    const r2 = build(doc([plate(), notch0, flange({ edges: [left] })]));
    expectSolid(r2);
    const b = [...r2.part.bends.values()][0];
    expect(b.w1 - b.w0).toBeCloseTo(40, 6);
  });

  it('the sketch follows its reference edge when the flange length changes', () => {
    const r1 = build(doc([plate(), flange()]));
    const end = edgeRef(r1.part, 'fl:end');
    const cut = sketchCut([circle('c', 50, 10, 4)], { tag: end.tag, at: end.at });
    for (const length of [30, 40]) {
      const r = build(doc([plate(), flange({ length }), cut]));
      expectSolid(r);
      expect(r.part.faces.get('fl')!.holes).toHaveLength(1);
      const info = r.part.circles.get('sc:c')!;
      const e = findEdge(r.part, { tag: 'fl:end', at: end.at });
      expect(distPointSegment(info.c, e.a, e.b)).toBeCloseTo(10, 9);
      expect(r.fp.loops.filter((l) => l.circle)).toHaveLength(1);
    }
  });
  it('a profile edge lying on a slanted outline edge notches cleanly', () => {
    // rectangle dragged from base:e1 with grid snap: its lower edge is exactly on the edge
    const onE1 = { tag: 'base:e1', at: { x: 85, y: 25 } };
    const r = build(doc([trapezoid(70), sketchCut([rect('n', 20, 2, 8, 4)], onE1)]));
    expectSolid(r);
    expect(r.part.faces.get('base')!.holes).toHaveLength(0);
    expect(r.fp.area).toBeCloseTo(4250 - 32, 6);
    expect(tagsOf(r.part, 'base:e1')).toHaveLength(2);

    // polyline through the edge's midpoint and end point, the face sketcher's own snap targets
    for (const x2 of [56, 59, 64]) {
      const ref = { tag: 'base:e1', at: { x: (100 + x2) / 2, y: 25 } };
      const frame = sketchFrame(build(doc([trapezoid(x2)])).part, ref);
      const mid = flatToSketch(frame, ref.at);
      const end = flatToSketch(frame, { x: x2, y: 50 });
      const tri: SketchEntity = { kind: 'polyline', id: 't', points: [mid, end, { x: mid.x, y: mid.y + 10 }] };
      const rt = build(doc([trapezoid(x2), sketchCut([tri], ref)]));
      expectSolid(rt);
      expect(rt.fp.area).toBeCloseTo(((100 + x2) / 2) * 50 - (Math.hypot(100 - x2, 50) / 2) * 5, 6);
    }
  });

  it('a sketch and a Bohrung keep their place when an earlier notch in their edge changes', () => {
    // the reference point (65, 0) lies on the piece of base:e0 to the right of notch A
    const right = { tag: 'base:e0', at: { x: 65, y: 0 } };
    const notch = (cx: number, w = 10): SketchCutFeature => sketchCut([rect('n', cx, 0, w, 10)], onE0, 'A');
    const sc = sketchCut([circle('c', 50, 25, 4)], right, 'B');
    const b: SketchCutFeature = { ...sc, sketch: { ...sc.sketch, anchor: 'line' } };
    const h = hole('H', right, 50, 15, { anchor: 'line' });
    for (const a of [notch(25), notch(45), notch(65), notch(75), notch(25, 4), { ...notch(25), suppressed: true }, null]) {
      const r = build(doc(a ? [plate(), a, b, h] : [plate(), b, h]));
      expectSolid(r);
      expect(r.part.circles.get('B:c')!.c).toEqual({ x: 50, y: 25 });
      expect(r.part.circles.get('H:h0')!.c).toEqual({ x: 50, y: 15 });
    }
  });

  it('documents saved before the line anchor keep measuring from the picked piece', () => {
    // notch A splits base:e0 at x 20..30; the reference point lies on the right piece, which starts at x = 30
    const right = { tag: 'base:e0', at: { x: 65, y: 0 } };
    const a = sketchCut([rect('n', 25, 0, 10, 10)], onE0, 'A');
    const r = build(doc([plate(), a, sketchCut([circle('c', 20, 25, 4)], right, 'B'), hole('H', right, 20, 15)]));
    expectSolid(r);
    expect(r.part.circles.get('B:c')!.c).toEqual({ x: 50, y: 25 });
    expect(r.part.circles.get('H:h0')!.c).toEqual({ x: 50, y: 15 });
  });

  it('a cut closer to an outline edge or seam than the mesh can keep apart is rejected', () => {
    // 5e-6 below base:e2: the mesh would merge the hole into the edge
    expect(errorOf([plate(), sketchCut([rect('r', 50, 50 - 5e-6 - 5, 10, 10)])])).toBe(TOUCH_MSG);
    expect(errorOf([plate(), sketchCut([rect('r', 50, 50 - 2e-5 - 5, 10, 10)])])).toBe(TOUCH_MSG);
    // 1e-5 off the seam at y = 4 (sketch y runs down from base:e2)
    expect(errorOf([plate(), flange(), sketchCut([rect('r', 50, 46 - 1e-5 - 5, 10, 10)], onE2)])).toBe(BEND_MSG);
    expect(errorOf([plate(), flange(), sketchCut([rect('r', 50, 46 - 5e-6 - 5, 10, 10)], onE2)])).toBe(BEND_MSG);
    // a notch that nearly reaches the opposite edge
    expect(errorOf([plate(), sketchCut([rect('r', 50, 25 - 5e-6, 10, 50)])])).toBe(TOUCH_MSG);
    const near = errorOf([plate(), hole('sc', onE0, 50, 50 - 5e-6 - 5, { shape: 'rect', width: 10, height: 10 })]);
    expect(near).toMatch(/nicht vollständig auf der Fläche/);

    // a clear gap still works and gives a closed solid
    const r = build(doc([plate(), sketchCut([rect('r', 50, 50 - 1e-4 - 5, 10, 10)])]));
    expectSolid(r);
    expect(r.fp.area).toBeCloseTo(4900, 6);
    const rh = build(doc([plate(), hole('h', onE0, 50, 50 - 1e-4 - 5, { shape: 'rect', width: 10, height: 10 })]));
    expectSolid(rh);
    expect(rh.fp.area).toBeCloseTo(4900, 6);
  });

  it('a new Ausschnitt or Bohrung without a face asks for a click on a face', () => {
    expect(errorOf([plate(), sketchCut([], { tag: '', at: { x: 0, y: 0 } })])).toBe('Bitte die Fläche anklicken, auf der skizziert werden soll.');
    expect(errorOf([plate(), hole('sc', { tag: '', at: { x: 0, y: 0 } }, 0, 0)])).toBe('Bitte auf die Fläche klicken, wo die Bohrung sitzen soll.');
  });
});
