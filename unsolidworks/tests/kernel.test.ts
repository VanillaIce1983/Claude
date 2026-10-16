import { describe, expect, it } from 'vitest';
import { regenerate } from '../src/kernel/doc';
import { flatToDxf } from '../src/kernel/export/dxf';
import { crc32, makeZip } from '../src/kernel/export/zip';
import { edgeFlangeGeometry } from '../src/kernel/features/flange';
import type { EdgeFlangeFeature, Feature, HemFeature, JogFeature, SketchedBendFeature } from '../src/kernel/features/types';
import { bendAllowance, outsideSetback } from '../src/kernel/sheet';
import { rad } from '../src/kernel/vec';
import { build, checkClosed, doc, expectedVolume, plate } from './helpers';

const T = 2;
const R = 2;
const BA90 = (Math.PI / 2) * (R + 0.5 * T); // 4.712389

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

function flatHeight(fp: ReturnType<typeof build>['fp']): number {
  return fp.bounds.maxY - fp.bounds.minY;
}

function expectSolid(r: ReturnType<typeof build>): void {
  const c = checkClosed(r.mesh.indices);
  expect(c.bad).toBe(0);
  expect(r.volume).toBeGreaterThan(0);
}

describe('bend math', () => {
  it('K-factor bend allowance', () => {
    expect(bendAllowance(rad(90), 2, 2, { method: 'k', kFactor: 0.5, ba: 0, bd: 0 })).toBeCloseTo(BA90, 9);
  });
  it('bend deduction converts to the same flat length', () => {
    const ossb = outsideSetback(rad(90), 2, 2);
    const ba = bendAllowance(rad(90), 2, 2, { method: 'bd', kFactor: 0.5, ba: 0, bd: 3.287611 });
    expect(ba).toBeCloseTo(2 * ossb - 3.287611, 9);
  });
  it('rejects bend deduction at 180°', () => {
    expect(() => bendAllowance(Math.PI, 2, 2, { method: 'bd', kFactor: 0.5, ba: 0, bd: 1 })).toThrow();
  });
  it('flange positions at 90° match the textbook offsets', () => {
    const g = (p: EdgeFlangeFeature['position']) => edgeFlangeGeometry(90, 30, 'outer', p, R, T, T, BA90).s;
    expect(g('materialInside')).toBeCloseTo(-(R + T), 9);
    expect(g('materialOutside')).toBeCloseTo(-R, 9);
    expect(g('bendOutside')).toBeCloseTo(0, 9);
    expect(g('virtualSharp')).toBeCloseTo(-(R + T), 9);
  });
});

describe('edge flange', () => {
  it('material inside keeps the outer dimensions (100 × 50 × 30)', () => {
    const r = build(doc([plate(), flange()]));
    expectSolid(r);
    expect(r.bounds.max.y).toBeCloseTo(30, 6);
    expect(r.bounds.min.y).toBeCloseTo(0, 6);
    expect(r.bounds.max.z).toBeCloseTo(0, 6);
    expect(r.bounds.min.z).toBeCloseTo(-50, 6);
    expect(r.bounds.max.x - r.bounds.min.x).toBeCloseTo(100, 6);
    // flat = 50 + 30 − BD, BD = 2·OSSB − BA
    expect(flatHeight(r.fp)).toBeCloseTo(50 - (R + T) + BA90 + (30 - (R + T)), 6);
    // K = 0.5: the neutral fibre sits in the middle, so volume = flat area × T
    expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
  });

  it('material outside adds the thickness outside the edge', () => {
    const r = build(doc([plate(), flange({ position: 'materialOutside' })]));
    expectSolid(r);
    expect(r.bounds.max.z).toBeCloseTo(T, 6);
    expect(flatHeight(r.fp)).toBeCloseTo(50 - R + BA90 + (30 - (R + T)), 6);
  });

  it('bend outside starts the bend at the edge', () => {
    const r = build(doc([plate(), flange({ position: 'bendOutside' })]));
    expectSolid(r);
    expect(r.bounds.max.z).toBeCloseTo(R + T, 6);
    expect(flatHeight(r.fp)).toBeCloseTo(50 + BA90 + (30 - (R + T)), 6);
  });

  it('inner virtual sharp measures from the inner surfaces', () => {
    const r = build(doc([plate(), flange({ lengthRef: 'inner' })]));
    expectSolid(r);
    expect(r.bounds.max.y).toBeCloseTo(30 + T, 6);
  });

  it('45° flange lands where the virtual sharp says', () => {
    const r = build(doc([plate(), flange({ angle: 45, position: 'bendOutside' })]));
    expectSolid(r);
    const ossb = outsideSetback(rad(45), R, T);
    const outerEnd = { o: ossb + 30 * Math.cos(rad(45)), z: 30 * Math.sin(rad(45)) };
    expect(r.bounds.max.z).toBeCloseTo(outerEnd.o, 6);
    expect(r.bounds.max.y).toBeCloseTo(outerEnd.z + T * Math.cos(rad(45)), 6);
    expect(flatHeight(r.fp)).toBeCloseTo(50 + rad(45) * (R + T / 2) + (30 - ossb), 6);
  });

  it('partial width with rectangular relief stays a closed solid', () => {
    const r = build(doc([plate(), flange({ width: { mode: 'offset', offset1: 20, offset2: 25, width: 0 } })]));
    expectSolid(r);
    expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
    expect(r.bounds.max.y).toBeCloseTo(30, 6);
    // relief: notch is the bend region plus d = 0.5·T
    expect(r.fp.loops.filter((l) => l.outer)).toHaveLength(1);
  });

  for (const type of ['tear', 'obround'] as const) {
    it(`partial width with ${type} relief stays a closed solid`, () => {
      const d = doc([plate(), flange({ width: { mode: 'centered', offset1: 0, offset2: 0, width: 40 } })]);
      d.sheet.relief = { type, ratio: 0.5 };
      const r = build(d);
      expectSolid(r);
      expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
    });
  }

  it('a tray with four flanges folds to 100 × 50 × 30', () => {
    const f = flange({
      edges: [
        { tag: 'base:e0', at: { x: 50, y: 0 }, side: 'top' },
        { tag: 'base:e1', at: { x: 100, y: 25 }, side: 'top' },
        { tag: 'base:e2', at: { x: 50, y: 50 }, side: 'top' },
        { tag: 'base:e3', at: { x: 0, y: 25 }, side: 'top' },
      ],
    });
    const r = build(doc([plate(), f]));
    expectSolid(r);
    expect(r.bounds.min.x).toBeCloseTo(0, 6);
    expect(r.bounds.max.x).toBeCloseTo(100, 6);
    expect(r.bounds.min.z).toBeCloseTo(-50, 6);
    expect(r.bounds.max.z).toBeCloseTo(0, 6);
    expect(r.bounds.max.y).toBeCloseTo(30, 6);
    expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
  });

  it('return flange on a flange', () => {
    const r1 = build(doc([plate(), flange()]));
    const end = [...r1.part.faces.values()].find((f) => f.id === 'fl')!;
    const i = end.outer.tags.indexOf('fl:end');
    const a = end.outer.pts[i];
    const b = end.outer.pts[(i + 1) % end.outer.pts.length];
    const second: EdgeFlangeFeature = flange({
      id: 'fl2',
      edges: [{ tag: 'fl:end', at: { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }, side: 'top' }],
      length: 15,
    });
    const r = build(doc([plate(), flange(), second]));
    expectSolid(r);
    expect(r.bounds.max.y).toBeCloseTo(30, 6);
  });

  it('downward flange from the bottom edge', () => {
    const r = build(doc([plate(), flange({ edges: [{ tag: 'base:e0', at: { x: 50, y: 0 }, side: 'bottom' }] })]));
    expectSolid(r);
    expect(r.bounds.min.y).toBeCloseTo(T - 30, 6);
  });
});

describe('hem', () => {
  const hem = (over: Partial<HemFeature> = {}): HemFeature => ({
    id: 'hem',
    name: 'Saum1',
    type: 'hem',
    edges: [{ tag: 'base:e0', at: { x: 50, y: 0 }, side: 'top' }],
    hemType: 'closed',
    position: 'materialInside',
    reverse: false,
    length: 10,
    gap: 1,
    angle: 270,
    radius: 3,
    width: { mode: 'full', offset1: 0, offset2: 0, width: 0 },
    ...over,
  });

  it('closed hem doubles the thickness and keeps the edge position', () => {
    const r = build(doc([plate(), hem()]));
    expectSolid(r);
    expect(r.bounds.max.y).toBeCloseTo(2 * T, 6);
    expect(r.bounds.max.z).toBeCloseTo(0, 6);
    expect(flatHeight(r.fp)).toBeCloseTo(50 - T + Math.PI * 0.5 * T + (10 - T), 6);
  });

  it('open hem leaves the gap', () => {
    const r = build(doc([plate(), hem({ hemType: 'open', gap: 1.5 })]));
    expectSolid(r);
    expect(r.bounds.max.y).toBeCloseTo(2 * T + 1.5, 6);
  });

  for (const hemType of ['teardrop', 'rolled'] as const) {
    it(`${hemType} hem builds a closed solid`, () => {
      const r = build(doc([plate(), hem({ hemType })]));
      expectSolid(r);
      expect(r.bounds.max.z).toBeCloseTo(0, 6);
    });
  }
});

describe('open profile base flange', () => {
  const u = (reverse: boolean): Feature => ({
    id: 'base',
    name: 'Basislasche1',
    type: 'baseFlange',
    plane: 'front',
    points: [
      { x: 0, y: 40 },
      { x: 0, y: 0 },
      { x: 100, y: 0 },
      { x: 100, y: 40 },
    ],
    closed: false,
    reverse,
    depth: 60,
    midPlane: false,
  });

  it('sketch on the inside surface', () => {
    const r = build(doc([u(false)]));
    expectSolid(r);
    expect(r.bounds.min.x).toBeCloseTo(-T, 6);
    expect(r.bounds.max.x).toBeCloseTo(100 + T, 6);
    expect(r.bounds.min.y).toBeCloseTo(-T, 6);
    expect(r.bounds.max.y).toBeCloseTo(40, 6);
    expect(r.bounds.max.z - r.bounds.min.z).toBeCloseTo(60, 6);
    expect(flatHeight(r.fp)).toBeCloseTo(60, 6);
    expect(r.fp.bounds.maxX - r.fp.bounds.minX).toBeCloseTo(2 * (40 - R) + (100 - 2 * R) + 2 * BA90, 6);
  });

  it('reversed: sketch on the outside surface', () => {
    const r = build(doc([u(true)]));
    expectSolid(r);
    expect(r.bounds.min.x).toBeCloseTo(0, 6);
    expect(r.bounds.max.x).toBeCloseTo(100, 6);
    expect(r.bounds.min.y).toBeCloseTo(0, 6);
    expect(r.fp.bounds.maxX - r.fp.bounds.minX).toBeCloseTo(2 * (40 - R - T) + (100 - 2 * (R + T)) + 2 * BA90, 6);
  });
});

describe('sketched bend and jog', () => {
  const line = { edge: { tag: 'base:e0', at: { x: 50, y: 0 } }, distance: 20 };

  it('sketched bend keeps the flat pattern and folds the edge strip', () => {
    const sb: SketchedBendFeature = {
      id: 'sb',
      name: 'Skizzierte Biegung1',
      type: 'sketchedBend',
      line,
      angle: 90,
      position: 'centerline',
      moveEdgeSide: true,
      reverse: false,
    };
    const r = build(doc([plate(), sb]));
    expectSolid(r);
    expect(r.fp.bounds.maxX - r.fp.bounds.minX).toBeCloseTo(100, 6);
    expect(flatHeight(r.fp)).toBeCloseTo(50, 6);
    expect(r.bounds.min.z).toBeCloseTo(-50, 6);
    expect(r.bounds.max.z).toBeCloseTo(-(20 + BA90 / 2) + R + T, 6);
    expect(r.bounds.max.y).toBeCloseTo(R + T + (20 - BA90 / 2), 6);
  });

  const jog = (over: Partial<JogFeature> = {}): JogFeature => ({
    id: 'jog',
    name: 'Knick1',
    type: 'jog',
    line,
    offset: 10,
    dimension: 'outside',
    angle: 90,
    position: 'bendOutside',
    fixedProjectedLength: false,
    moveEdgeSide: true,
    reverse: false,
    ...over,
  });

  it('jog lifts the edge strip by the outside offset', () => {
    const r = build(doc([plate(), jog()]));
    expectSolid(r);
    expect(r.bounds.max.y).toBeCloseTo(10 + T, 6);
    expect(flatHeight(r.fp)).toBeCloseTo(50, 6);
  });

  it('jog with fixed projected length keeps the far edge in place', () => {
    const r = build(doc([plate(), jog({ fixedProjectedLength: true })]));
    expectSolid(r);
    expect(r.bounds.max.z).toBeCloseTo(0, 6);
    const web = 10 - (2 * R + T);
    expect(flatHeight(r.fp)).toBeCloseTo(50 + 2 * BA90 + web - (2 * R + T), 6);
  });

  it('inside and overall dimensions', () => {
    expect(build(doc([plate(), jog({ dimension: 'inside' })])).bounds.max.y).toBeCloseTo(10 + 2 * T, 6);
    expect(build(doc([plate(), jog({ dimension: 'overall' })])).bounds.max.y).toBeCloseTo(10, 6);
  });
});

describe('cuts and exports', () => {
  const cut: Feature = {
    id: 'cut',
    name: 'Ausschnitt1',
    type: 'cut',
    edge: { tag: 'base:e0', at: { x: 50, y: 0 } },
    u: 50,
    v: 25,
    shape: 'circle',
    diameter: 10,
    width: 0,
    height: 0,
    angle: 0,
    count: 1,
    spacing: 0,
  };

  it('round hole becomes a true circle in the DXF', () => {
    const r = build(doc([plate(), cut]));
    expectSolid(r);
    const circles = r.fp.loops.filter((l) => l.circle);
    expect(circles).toHaveLength(1);
    expect(circles[0].circle!.r).toBeCloseTo(5, 9);
    const dxf = flatToDxf(r.fp);
    expect(dxf).toContain('CIRCLE');
    expect(dxf).toContain('POLYLINE');
    expect(dxf.trim().endsWith('EOF')).toBe(true);
  });

  it('cut outside the face is rejected with a message', () => {
    const d = doc([plate(), { ...cut, v: 60 } as Feature]);
    const res = regenerate(d);
    expect(res.status.get('cut')?.ok).toBe(false);
    expect(res.status.get('cut')?.error).toMatch(/Fläche/);
  });

  it('zip CRC matches the reference value', () => {
    expect(crc32(new TextEncoder().encode('123456789'))).toBe(0xcbf43926);
    const z = makeZip([{ name: 'a.txt', data: new TextEncoder().encode('hallo') }]);
    expect(z[0]).toBe(0x50);
    expect(z[1]).toBe(0x4b);
  });

  it('flat pattern of a bent part contains bend lines with direction', () => {
    const r = build(doc([plate(), flange()]));
    expect(r.fp.bendLines).toHaveLength(1);
    expect(r.fp.bendLines[0].up).toBe(true);
    expect(r.fp.bendLines[0].angleDeg).toBeCloseTo(90, 9);
    const dxf = flatToDxf(r.fp);
    expect(dxf).toContain('BIEGUNG_OBEN');
    expect(dxf).toContain('OBEN 90%%d R2');
  });
});
