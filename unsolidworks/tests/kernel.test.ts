import { describe, expect, it } from 'vitest';
import { regenerate } from '../src/kernel/doc';
import { flatToDxf } from '../src/kernel/export/dxf';
import { crc32, makeZip } from '../src/kernel/export/zip';
import { edgeFlangeGeometry } from '../src/kernel/features/flange';
import type { EdgeFlangeFeature, Feature, HemFeature, JogFeature, SketchedBendFeature } from '../src/kernel/features/types';
import { bendAllowance, outsideSetback } from '../src/kernel/sheet';
import { HEM_CLEARANCE } from '../src/kernel/features/clearance';
import { Part, computePlacements, stripPoint } from '../src/kernel/model';
import { distPointSegment, pointInPolygon } from '../src/kernel/polygon';
import { V3, applyA, invertRigid, rad, v2, v3 } from '../src/kernel/vec';
import { build, checkClosed, doc, edgeRef, expectedVolume, plate } from './helpers';

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

  // lowest point of the hem above the remaining base face (the plate's top surface is at y = T)
  const lowestOverBase = (r: ReturnType<typeof build>): number => {
    const { positions: P, indices: I, triPiece, pieces } = r.mesh;
    const lo = [Infinity, Infinity];
    const hi = [-Infinity, -Infinity];
    const hemVerts = new Set<number>();
    for (let t = 0; t < I.length / 3; t++) {
      const piece = pieces[triPiece[t]];
      for (let k = 0; k < 3; k++) {
        const v = I[3 * t + k];
        if (piece.id === 'hem') hemVerts.add(v);
        else if (piece.id === 'base') {
          lo[0] = Math.min(lo[0], P[3 * v]);
          hi[0] = Math.max(hi[0], P[3 * v]);
          lo[1] = Math.min(lo[1], P[3 * v + 2]);
          hi[1] = Math.max(hi[1], P[3 * v + 2]);
        }
      }
    }
    let low = Infinity;
    for (const v of hemVerts) {
      const x = P[3 * v];
      const z = P[3 * v + 2];
      // the hem only has vertices at the ends of the edge (x), so only z must lie inside the base
      if (x >= lo[0] - 1e-6 && x <= hi[0] + 1e-6 && z > lo[1] + 1e-6 && z < hi[1] - 1e-6) low = Math.min(low, P[3 * v + 1]);
    }
    return low;
  };

  // limit 360° − acos(R / (R + T)): 306.87° for R = 3, T = 2
  for (const hemType of ['teardrop', 'rolled'] as const) {
    for (const angle of [280, 300, 306.8]) {
      it(`${hemType} hem at ${angle}° stays above the face`, () => {
        const r = build(doc([plate(), hem({ hemType, angle, radius: 3 })]));
        expectSolid(r);
        expect(lowestOverBase(r)).toBeGreaterThanOrEqual(T - 1e-6);
      });
    }
    it(`${hemType} hem past the limit is refused`, () => {
      const res = regenerate(doc([plate(), hem({ hemType, angle: 307, radius: 3 })]));
      expect(res.status.get('hem')?.error).toMatch(/eindringen.*306,8°/);
    });
  }

  it('teardrop tail ends on the face at 270°', () => {
    const r = build(doc([plate(), hem({ hemType: 'teardrop', angle: 270, radius: 3 })]));
    expect(lowestOverBase(r)).toBeCloseTo(T, 6);
  });
});

describe('hem next to other flanges', () => {
  const tray = (position: EdgeFlangeFeature['position']): EdgeFlangeFeature => ({
    id: 'fl',
    name: 'Kantenlasche1',
    type: 'edgeFlange',
    edges: [
      { tag: 'base:e0', at: { x: 50, y: 0 }, side: 'top' },
      { tag: 'base:e1', at: { x: 100, y: 25 }, side: 'top' },
      { tag: 'base:e2', at: { x: 50, y: 50 }, side: 'top' },
      { tag: 'base:e3', at: { x: 0, y: 25 }, side: 'top' },
    ],
    angle: 90,
    length: 30,
    lengthRef: 'outer',
    position,
    reverse: false,
    width: { mode: 'full', offset1: 0, offset2: 0, width: 0 },
  });
  const hemsOn = (part: Part, tags: string[], side: 'top' | 'bottom', over: Partial<HemFeature> = {}): HemFeature => ({
    id: 'hem',
    name: 'Saum1',
    type: 'hem',
    edges: tags.map((t) => ({ ...edgeRef(part, t, side), side })),
    hemType: 'closed',
    position: 'materialInside',
    reverse: false,
    length: 10,
    gap: 1,
    angle: 270,
    radius: 2,
    width: { mode: 'full', offset1: 0, offset2: 0, width: 0 },
    ...over,
  });

  /** Points through the sheet of a bend strip and of its leg face. */
  function hemPoints(part: Part, id: string): V3[] {
    const pl = computePlacements(part);
    const b = part.bends.get(id)!;
    const MA = pl.get(b.faceA)!;
    const pts: V3[] = [];
    for (let i = 0; i <= 16; i++) for (let j = 0; j <= 40; j++) for (const h of [0, T / 2, T]) pts.push(applyA(MA, stripPoint(b, T, b.w0 + ((b.w1 - b.w0) * j) / 40, i / 16, h)));
    const leg = b.faceB ? part.faces.get(b.faceB) : undefined;
    if (leg) {
      const MB = pl.get(leg.id)!;
      const xs = leg.outer.pts.map((p) => p.x);
      const ys = leg.outer.pts.map((p) => p.y);
      for (let i = 0; i <= 40; i++) for (let j = 0; j <= 10; j++) {
        const p = v2(Math.min(...xs) + ((Math.max(...xs) - Math.min(...xs)) * i) / 40, Math.min(...ys) + ((Math.max(...ys) - Math.min(...ys)) * j) / 10);
        if (pointInPolygon(p, leg.outer.pts)) for (const h of [0, T / 2, T]) pts.push(applyA(MB, v3(p.x, p.y, h)));
      }
    }
    return pts;
  }

  /** Distance from a point to the sheet of a planar face. */
  function toFace(part: Part, faceId: string, p: V3): number {
    const face = part.faces.get(faceId)!;
    const q = applyA(invertRigid(computePlacements(part).get(faceId)!), p);
    const pt = v2(q.x, q.y);
    const pts = face.outer.pts;
    const inPlane = pointInPolygon(pt, pts) ? 0 : Math.min(...pts.map((a, i) => distPointSegment(pt, a, pts[(i + 1) % pts.length])));
    return Math.hypot(inPlane, Math.max(0, -q.z, q.z - T));
  }

  const clearance = (part: Part, hemId: string, faces: string[]): number => {
    let min = Infinity;
    for (const p of hemPoints(part, hemId)) for (const f of faces) min = Math.min(min, toFace(part, f, p));
    return min;
  };

  for (const position of ['materialInside', 'virtualSharp', 'materialOutside'] as const) {
    for (const hemType of ['closed', 'open', 'teardrop', 'rolled'] as const) {
      it(`a ${hemType} hem curling into a ${position} tray is shortened to clear the side flanges`, () => {
        const fl = tray(position);
        const r1 = build(doc([plate(), fl]));
        const r = build(doc([plate(), fl, hemsOn(r1.part, ['fl.0:end'], 'top', { hemType })]));
        expectSolid(r);
        const b = r.part.bends.get('hem')!;
        expect(b.w0).toBeGreaterThan(0);
        expect(b.w1).toBeLessThan(100);
        expect(clearance(r.part, 'hem', ['fl.1', 'fl.3'])).toBeGreaterThanOrEqual(HEM_CLEARANCE - 1e-6);
        // it is not shortened more than needed: back by the side flange (T) plus the gap at most
        expect(b.w0).toBeLessThanOrEqual(T + HEM_CLEARANCE + 1e-6);
        expect(100 - b.w1).toBeLessThanOrEqual(T + HEM_CLEARANCE + 1e-6);
      });
    }
  }

  it('a hem curling away from the side flanges keeps its full width', () => {
    const fl = tray('materialInside');
    const r1 = build(doc([plate(), fl]));
    const r = build(doc([plate(), fl, hemsOn(r1.part, ['fl.0:end'], 'bottom')]));
    const b = r.part.bends.get('hem')!;
    expect(b.w0).toBe(0);
    expect(b.w1).toBeCloseTo(100, 9);
  });

  it('hems on all four flanges of a tray stay clear of each other and of the flanges', () => {
    for (const position of ['materialInside', 'bendOutside'] as const) {
      const fl = tray(position);
      const r1 = build(doc([plate(), fl]));
      const r = build(doc([plate(), fl, hemsOn(r1.part, ['fl.0:end', 'fl.1:end', 'fl.2:end', 'fl.3:end'], 'top', { hemType: 'open' })]));
      expectSolid(r);
      const legs = ['hem.0', 'hem.1', 'hem.2', 'hem.3'];
      legs.forEach((id, i) => {
        const others = ['fl.0', 'fl.1', 'fl.2', 'fl.3', ...legs].filter((f) => f !== id && f !== `fl.${i}`);
        expect(clearance(r.part, id, others)).toBeGreaterThanOrEqual(HEM_CLEARANCE - 1e-6);
      });
    }
  });

  it('a shortened hem leaves no sliver of the flange between itself and the corner', () => {
    // the side flanges of a materialOutside tray touch fl.0's end, so the hem only gives way by the gap
    const fl = tray('materialOutside');
    const r1 = build(doc([plate(), fl]));
    const r = build(doc([plate(), fl, hemsOn(r1.part, ['fl.0:end'], 'top')]));
    expectSolid(r);
    const b = r.part.bends.get('hem')!;
    expect(b.w0).toBeCloseTo(HEM_CLEARANCE, 5);
    // the flange is cut down to the hem's bend line right to its corner
    const flange = r.part.faces.get('fl.0')!;
    const top = Math.max(...flange.outer.pts.map((p) => (p.x - b.origin.x) * b.nrm.x + (p.y - b.origin.y) * b.nrm.y));
    expect(top).toBeLessThanOrEqual(1e-9);
  });
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
