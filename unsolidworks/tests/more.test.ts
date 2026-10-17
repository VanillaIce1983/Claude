import { describe, expect, it } from 'vitest';
import { regenerate } from '../src/kernel/doc';
import type { CutFeature, EdgeFlangeFeature, Feature, JogFeature, SketchedBendFeature } from '../src/kernel/features/types';
import { bendAllowance, outsideSetback } from '../src/kernel/sheet';
import { rad } from '../src/kernel/vec';
import { exampleDoc } from '../src/ui/example';
import { signedArea } from '../src/kernel/polygon';
import { build, checkClosed, doc, edgeRef, expectedVolume, plate } from './helpers';
import fourHoles from './fixtures/earcut-case145.json';
import sixHoles from './fixtures/earcut-case44.json';

const T = 2;
const R = 2;

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

function failing(features: Feature[], id: string): string {
  const r = regenerate(doc(features));
  const s = r.status.get(id);
  expect(s?.ok).toBe(false);
  return s!.error!;
}

describe('example part', () => {
  it('builds without errors and is a closed solid', () => {
    const r = build(exampleDoc());
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
    expect(r.fp.loops.filter((l) => l.circle)).toHaveLength(2);
  });
});

describe('bend calculation methods on a flange', () => {
  it('fixed bend allowance', () => {
    const d = doc([plate(), flange()]);
    d.sheet.calc = { method: 'ba', kFactor: 0.5, ba: 3.5, bd: 0 };
    const r = build(d);
    expect(r.fp.bounds.maxY - r.fp.bounds.minY).toBeCloseTo(46 + 3.5 + 26, 6);
  });
  it('bend deduction reproduces flat = A + B − BD', () => {
    const d = doc([plate(), flange()]);
    d.sheet.calc = { method: 'bd', kFactor: 0.5, ba: 0, bd: 4 };
    const r = build(d);
    expect(r.fp.bounds.maxY - r.fp.bounds.minY).toBeCloseTo(50 + 30 - 4, 6);
  });
  it('per-feature override of radius and K-factor', () => {
    const r = build(doc([plate(), flange({ overrides: { radius: 5, calc: { method: 'k', kFactor: 0.3, ba: 0, bd: 0 } } })]));
    const ba = bendAllowance(rad(90), 5, T, { method: 'k', kFactor: 0.3, ba: 0, bd: 0 });
    expect(r.fp.bounds.maxY - r.fp.bounds.minY).toBeCloseTo(50 - (5 + T) + ba + (30 - (5 + T)), 6);
    expect(r.bounds.max.y).toBeCloseTo(30, 6);
  });
});

describe('edge flange variants', () => {
  it('120° flange with tangent length and tangent-to-bend position', () => {
    const r = build(doc([plate(), flange({ angle: 120, lengthRef: 'tangent', position: 'tangentToBend' })]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    // the bend's outer surface touches the plane of the original side face
    expect(r.bounds.max.z).toBeCloseTo(0, 6);
  });

  it('material outside at 60° on the bottom edge bends downwards', () => {
    const r = build(doc([plate(), flange({ angle: 60, position: 'materialOutside', edges: [{ tag: 'base:e0', at: { x: 50, y: 0 }, side: 'bottom' }] })]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.bounds.min.y).toBeLessThan(-20);
  });

  it('flange on a non-rectangular plate edge', () => {
    const tri: Feature = { ...plate(), points: [{ x: 0, y: 0 }, { x: 120, y: 0 }, { x: 60, y: 80 }] } as Feature;
    const r = build(doc([tri, flange({ edges: [{ tag: 'base:e0', at: { x: 60, y: 0 }, side: 'top' }] })]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
  });

  it('flange on a hole edge is refused', () => {
    const cut: Feature = { id: 'c', name: 'A', type: 'cut', edge: { tag: 'base:e0', at: { x: 50, y: 0 } }, u: 50, v: 25, shape: 'rect', diameter: 0, width: 20, height: 10, angle: 0, count: 1, spacing: 0 };
    const err = failing([plate(), cut, flange({ edges: [{ tag: 'c:h0', at: { x: 50, y: 20 }, side: 'top' }] })], 'fl');
    expect(err).toMatch(/Außenkanten/);
  });

  it('flange that is too short says how much is needed', () => {
    expect(failing([plate(), flange({ length: 3 })], 'fl')).toMatch(/zu kurz/);
  });

  it('flange on an already bent edge is refused', () => {
    const r1 = build(doc([plate(), flange()]));
    const seam = [...r1.part.faces.get('base')!.outer.tags].find((t) => t.startsWith('seam:'))!;
    const ref = edgeRef(r1.part, seam);
    expect(failing([plate(), flange(), flange({ id: 'fl2', edges: [ref] })], 'fl2')).toMatch(/bereits eine Biegung/);
  });
});

describe('sketched bend and jog edge cases', () => {
  const line = { edge: { tag: 'base:e0', at: { x: 50, y: 0 } }, distance: 20 };

  it('fixing the edge side moves the rest of the part', () => {
    const sb: SketchedBendFeature = { id: 'sb', name: 'S', type: 'sketchedBend', line, angle: 90, position: 'bendOutside', moveEdgeSide: false, reverse: false };
    const r = build(doc([plate(), sb]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    // the 20 mm strip at the edge stays in the XZ plane, the rest stands up
    expect(r.bounds.max.y).toBeCloseTo(R + T + (50 - 20 - rad(90) * (R + T / 2)), 6);
  });

  it('flange on the moving side of a sketched bend', () => {
    const sb: SketchedBendFeature = { id: 'sb', name: 'S', type: 'sketchedBend', line, angle: 90, position: 'centerline', moveEdgeSide: true, reverse: false };
    const r1 = build(doc([plate(), sb]));
    const ref = edgeRef(r1.part, 'base:e0');
    const r = build(doc([plate(), sb, flange({ id: 'fl2', edges: [ref], length: 15 })]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
  });

  it('sketched bend across an existing flange seam is refused', () => {
    const across: SketchedBendFeature = { id: 'sb', name: 'S', type: 'sketchedBend', line: { edge: { tag: 'base:e1', at: { x: 100, y: 25 } }, distance: 30 }, angle: 90, position: 'centerline', moveEdgeSide: true, reverse: false };
    expect(failing([plate(), flange(), across], 'sb')).toMatch(/vorhandene Biegung/);
  });

  it('jog that is too low for two bends is refused', () => {
    const jog: JogFeature = { id: 'j', name: 'K', type: 'jog', line, offset: 3, dimension: 'outside', angle: 90, position: 'bendOutside', fixedProjectedLength: false, moveEdgeSide: true, reverse: false };
    expect(failing([plate(), jog], 'j')).toMatch(/zu klein/);
  });

  it('45° jog keeps the elevation', () => {
    const jog: JogFeature = { id: 'j', name: 'K', type: 'jog', line, offset: 8, dimension: 'outside', angle: 45, position: 'centerline', fixedProjectedLength: false, moveEdgeSide: true, reverse: true };
    const r = build(doc([plate(), jog]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    // reversed: the strip goes down by 8 mm
    expect(r.bounds.min.y).toBeCloseTo(-8, 6);
  });
});

describe('open profile options', () => {
  it('mid-plane depth and an L profile with custom radius', () => {
    const l: Feature = {
      id: 'base',
      name: 'B',
      type: 'baseFlange',
      plane: 'right',
      points: [
        { x: 0, y: 50 },
        { x: 0, y: 0 },
        { x: 80, y: 0 },
      ],
      closed: false,
      reverse: true,
      depth: 40,
      midPlane: true,
      overrides: { radius: 1 },
    };
    const r = build(doc([l]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.bounds.min.x).toBeCloseTo(-20, 6);
    expect(r.bounds.max.x).toBeCloseTo(20, 6);
    const ossb = outsideSetback(rad(90), 1, T);
    expect(r.fp.bounds.maxX - r.fp.bounds.minX).toBeCloseTo(50 - ossb + 80 - ossb + rad(90) * (1 + T / 2), 6);
  });

  it('segment shorter than the setback is refused', () => {
    const u: Feature = { id: 'base', name: 'B', type: 'baseFlange', plane: 'front', points: [{ x: 0, y: 3 }, { x: 0, y: 0 }, { x: 50, y: 0 }], closed: false, reverse: true, depth: 20, midPlane: false };
    expect(failing([u], 'base')).toMatch(/zu kurz/);
  });
});

describe('mesh robustness', () => {
  // a hole vertex near the line of a long earcut diagonal used to split triangles it is not part of
  const hole = (u: number, v: number, w: number): CutFeature => ({
    id: 'h',
    name: 'Bohrung1',
    type: 'cut',
    edge: { tag: 'base:e0', at: { x: 10, y: 0 } },
    u,
    v,
    shape: 'rect',
    diameter: 0,
    width: w,
    height: w,
    angle: 0,
    count: 1,
    spacing: 0,
    anchor: 'line',
  });

  for (const [L, gap, w] of [
    [1000, 5e-4, 10],
    [1000, 2e-3, 10],
    [1000, 0.01, 1],
    [3000, 0.05, 1],
  ]) {
    it(`a ${w} mm hole ${gap} mm below the edge of a ${L} mm plate stays a closed solid`, () => {
      const r = build(doc([plate(L, 50), hole(L / 2, 50 - gap - w / 2, w)]));
      expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
      expect(r.fp.area).toBeCloseTo(L * 50 - w * w, 6);
      expect(r.mesh.indices.length / 3).toBeLessThan(200);
    });
  }

  // found by fuzzing: earcut tiled these faces with overlapping triangles unless the outer ring started elsewhere
  for (const [name, feats] of [
    ['four', fourHoles],
    ['six', sixHoles],
  ] as const) {
    it(`a face with ${name} holes is tiled exactly`, () => {
      const r = build(doc(feats as unknown as Feature[]));
      expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
      const base = r.part.faces.get('base')!;
      const area = Math.abs(signedArea(base.outer.pts)) - base.holes.reduce((sum, h) => sum + Math.abs(signedArea(h.pts)), 0);
      expect(r.fp.area).toBeCloseTo(area, 6);
    });
  }
});
