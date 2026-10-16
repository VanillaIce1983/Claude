import { describe, expect, it } from 'vitest';
import type { CutFeature, EdgeFlangeFeature, HemFeature } from '../src/kernel/features/types';
import { MeasureEntity, MeasureResult, bendEntity, edgeEntity, faceEntity, measure, pointEntity } from '../src/kernel/measure';
import { PickEdge, pickCorners, pickEdges } from '../src/kernel/mesh';
import { Part, computePlacements } from '../src/kernel/model';
import { V3, dist3, v3 } from '../src/kernel/vec';
import { build, doc, plate } from './helpers';

const T = 2;
const R = 2;
const BA90 = (Math.PI / 2) * (R + 0.5 * T);
const O = v3(0, 0, 0);

function value(r: MeasureResult, label: string): number {
  const row = r.rows.find((x) => x.label === label);
  if (!row) throw new Error(`row ${label} missing; have ${r.rows.map((x) => x.label).join(', ')}`);
  return row.value;
}

const labels = (r: MeasureResult): string[] => r.rows.map((x) => x.label);
const pt = (x: number, y: number, z: number): MeasureEntity => pointEntity(v3(x, y, z), 'Punkt');
const edge = (a: V3, b: V3): MeasureEntity => ({ kind: 'edge', a, b, label: 'Kante' });
const circle = (center: V3, normal: V3, r: number): MeasureEntity => ({
  kind: 'edge',
  a: v3(center.x + r, center.y, center.z),
  b: v3(center.x, center.y + r, center.z),
  label: 'Kreiskante',
  circle: { center, normal, r },
});
const plane = (origin: V3, normal: V3, area = 0): MeasureEntity => ({
  kind: 'plane',
  origin,
  normal,
  area,
  perimeter: 0,
  label: 'Fläche',
});
const cyl = (axisPoint: V3, axisDir: V3, radius = 1): MeasureEntity => ({
  kind: 'cylinder',
  axisPoint,
  axisDir,
  radius,
  angleDeg: 90,
  length: 10,
  label: 'Zylinder',
});

function expectV3(a: V3, b: V3): void {
  expect(dist3(a, b)).toBeCloseTo(0, 6);
}

describe('measure: single entities', () => {
  it('nothing picked gives no rows', () => {
    expect(measure([])).toEqual({ rows: [], lines: [] });
  });

  it('point coordinates', () => {
    const r = measure([pt(1, -2, 3.5)]);
    expect(labels(r)).toEqual(['X', 'Y', 'Z']);
    expect(value(r, 'Z')).toBe(3.5);
  });

  it('edge length with a dimension line in German number format', () => {
    const r = measure([edge(O, v3(42.5, 0, 0))]);
    expect(value(r, 'Länge')).toBeCloseTo(42.5, 9);
    expect(r.lines).toHaveLength(1);
    expect(r.lines[0].text).toBe('42,5 mm');
  });

  it('circle edge gives diameter, radius and centre', () => {
    const r = measure([circle(v3(10, 20, 30), v3(0, 0, 2), 4)]);
    expect(value(r, 'Durchmesser')).toBeCloseTo(8, 9);
    expect(value(r, 'Radius')).toBeCloseTo(4, 9);
    expect(value(r, 'Länge')).toBeCloseTo(8 * Math.PI, 9);
    expect(value(r, 'Mittelpunkt X')).toBe(10);
    expect(value(r, 'Mittelpunkt Y')).toBe(20);
    expect(value(r, 'Mittelpunkt Z')).toBe(30);
    expect(r.lines).toHaveLength(1);
    expect(dist3(r.lines[0].a, r.lines[0].b)).toBeCloseTo(8, 9);
    expectV3(r.lines[0].a, v3(6, 20, 30));
    expect(r.lines[0].text).toBe('Ø 8 mm');
  });

  it('plane area and perimeter', () => {
    const r = measure([{ kind: 'plane', origin: O, normal: v3(0, 0, 1), area: 5000, perimeter: 300, label: 'Fläche' }]);
    expect(r.rows).toEqual([
      { label: 'Fläche', value: 5000, unit: 'mm²' },
      { label: 'Umfang', value: 300, unit: 'mm' },
    ]);
  });

  it('cylinder radius, angle and arc length', () => {
    const r = measure([cyl(O, v3(1, 0, 0), 2)]);
    expect(value(r, 'Radius')).toBe(2);
    expect(value(r, 'Durchmesser')).toBe(4);
    expect(value(r, 'Winkel')).toBe(90);
    expect(value(r, 'Bogenlänge')).toBeCloseTo(Math.PI, 9);
    expect(value(r, 'Länge')).toBe(10);
  });
});

describe('measure: pairs', () => {
  it('point to point, in either order', () => {
    const r = measure([pt(0, 0, 0), pt(3, -4, 12)]);
    expect(value(r, 'Abstand')).toBeCloseTo(13, 9);
    expect(value(r, 'ΔX')).toBe(3);
    expect(value(r, 'ΔY')).toBe(4);
    expect(value(r, 'ΔZ')).toBe(12);
    expect(r.lines[0].text).toBe('13 mm');
    expect(measure([pt(3, -4, 12), pt(0, 0, 0)]).rows).toEqual(r.rows);
  });

  it('dimension text rounds to two decimals with a comma', () => {
    expect(measure([pt(0, 0, 0), pt(1 / 3, 0, 0)]).lines[0].text).toBe('0,33 mm');
    expect(measure([pt(0, 0, 0), pt(1000, 0, 0)]).lines[0].text).toBe('1000 mm');
  });

  it('point to segment: perpendicular inside, endpoint outside', () => {
    const e = edge(O, v3(10, 0, 0));
    expect(value(measure([pt(5, 3, 0), e]), 'Abstand')).toBeCloseTo(3, 9);
    const r = measure([e, pt(13, 4, 0)]);
    expect(value(r, 'Abstand')).toBeCloseTo(5, 9);
    expectV3(r.lines[0].b, v3(10, 0, 0));
  });

  it('point to circle measures to the curve and to the centre', () => {
    const r = measure([pt(8, 0, 4), circle(O, v3(0, 0, 1), 5)]);
    expect(value(r, 'Abstand')).toBeCloseTo(5, 9);
    expect(value(r, 'Abstand zum Mittelpunkt')).toBeCloseTo(Math.sqrt(80), 9);
    expectV3(r.lines[0].b, v3(5, 0, 0));
  });

  it('point to plane uses the foot point (normal need not be unit)', () => {
    const r = measure([plane(O, v3(0, 0, 2)), pt(1, 2, -7)]);
    expect(value(r, 'Senkrechter Abstand')).toBeCloseTo(7, 9);
    expectV3(r.lines[0].b, v3(1, 2, 0));
  });

  it('point to cylinder axis', () => {
    expect(value(measure([pt(5, 3, 4), cyl(O, v3(1, 0, 0))]), 'Abstand zur Achse')).toBeCloseTo(5, 9);
  });

  it('edge angles 0, 45 and 90, folded to the acute angle', () => {
    const x = edge(O, v3(10, 0, 0));
    expect(value(measure([x, edge(v3(0, 5, 0), v3(-10, 5, 0))]), 'Winkel')).toBeCloseTo(0, 9);
    expect(value(measure([x, edge(O, v3(10, 10, 0))]), 'Winkel')).toBeCloseTo(45, 9);
    expect(value(measure([x, edge(O, v3(-10, 10, 0))]), 'Winkel')).toBeCloseTo(45, 9);
    expect(value(measure([x, edge(O, v3(0, 0, 7))]), 'Winkel')).toBeCloseTo(90, 9);
  });

  it('parallel edges report the perpendicular distance, not the gap', () => {
    const r = measure([edge(O, v3(10, 0, 0)), edge(v3(20, 5, 0), v3(30, 5, 0))]);
    expect(value(r, 'Abstand')).toBeCloseTo(5, 9);
    expect(labels(r)).not.toContain('Minimaler Abstand');
    expect(dist3(r.lines[0].a, r.lines[0].b)).toBeCloseTo(5, 9);
  });

  it('parallel overlapping edges put the dimension inside the overlap', () => {
    const r = measure([edge(O, v3(10, 0, 0)), edge(v3(6, 0, 3), v3(20, 0, 3))]);
    expect(value(r, 'Abstand')).toBeCloseTo(3, 9);
    expectV3(r.lines[0].a, v3(8, 0, 0));
    expectV3(r.lines[0].b, v3(8, 0, 3));
  });

  it('closest points of skew segments', () => {
    const r = measure([edge(O, v3(10, 0, 0)), edge(v3(5, -5, 3), v3(5, 5, 3))]);
    expect(value(r, 'Winkel')).toBeCloseTo(90, 9);
    expect(value(r, 'Minimaler Abstand')).toBeCloseTo(3, 9);
    expectV3(r.lines[0].a, v3(5, 0, 0));
    expectV3(r.lines[0].b, v3(5, 0, 3));
    // the closest points of the infinite lines lie outside both segments
    const s = measure([edge(O, v3(1, 0, 0)), edge(v3(3, -1, 2), v3(3, 1, 2))]);
    expect(value(s, 'Minimaler Abstand')).toBeCloseTo(Math.sqrt(8), 9);
    expectV3(s.lines[0].a, v3(1, 0, 0));
    expectV3(s.lines[0].b, v3(3, 0, 2));
  });

  it('a zero-length edge is measured like a point', () => {
    const r = measure([edge(v3(1, 1, 0), v3(1, 1, 0)), edge(O, v3(10, 0, 0))]);
    expect(value(r, 'Abstand')).toBeCloseTo(1, 9);
  });

  it('two circles give the centre distance', () => {
    const r = measure([circle(O, v3(0, 0, 1), 3), circle(v3(30, 40, 0), v3(0, 0, -1), 5)]);
    expect(value(r, 'Mittelpunktabstand')).toBeCloseTo(50, 9);
    expect(value(r, 'Winkel')).toBeCloseTo(0, 9);
  });

  it('circle and straight edge give the centre distance to the edge', () => {
    const r = measure([edge(O, v3(100, 0, 0)), circle(v3(50, 25, 0), v3(0, 0, 1), 5)]);
    expect(value(r, 'Abstand zum Mittelpunkt')).toBeCloseTo(25, 9);
  });

  it('edge and plane: angle, and distance when parallel', () => {
    const p = plane(O, v3(0, 0, 1));
    const r = measure([edge(v3(0, 0, 4), v3(10, 0, 4)), p]);
    expect(value(r, 'Winkel')).toBeCloseTo(0, 9);
    expect(value(r, 'Abstand')).toBeCloseTo(4, 9);
    const c = Math.cos(Math.PI / 6);
    const s = measure([p, edge(O, v3(c, 0, 0.5))]);
    expect(value(s, 'Winkel')).toBeCloseTo(30, 9);
    expect(labels(s)).not.toContain('Abstand');
    expect(value(measure([p, edge(O, v3(0, 0, 3))]), 'Winkel')).toBeCloseTo(90, 9);
  });

  it('parallel planes give angle 0 and their distance', () => {
    const r = measure([plane(v3(5, 5, 0), v3(0, 0, 1)), plane(v3(-3, 2, 7), v3(0, 0, -1))]);
    expect(value(r, 'Winkel')).toBeCloseTo(0, 9);
    expect(value(r, 'Abstand')).toBeCloseTo(7, 9);
    expectV3(r.lines[0].a, v3(5, 5, 0));
    expectV3(r.lines[0].b, v3(5, 5, 7));
  });

  it('plane angles 90 and 45 (135 between normals folds to 45)', () => {
    const z = plane(O, v3(0, 0, 1));
    expect(value(measure([z, plane(O, v3(1, 0, 0))]), 'Winkel')).toBeCloseTo(90, 9);
    const r = measure([z, plane(O, v3(0, 1, 1))]);
    expect(value(r, 'Winkel')).toBeCloseTo(45, 9);
    expect(labels(r)).not.toContain('Abstand');
    expect(value(measure([z, plane(O, v3(0, 1, -1))]), 'Winkel')).toBeCloseTo(45, 9);
  });

  it('nearly parallel planes keep a precise small angle', () => {
    const a = (0.005 * Math.PI) / 180;
    const r = measure([plane(O, v3(0, 0, 1)), plane(v3(0, 0, 2), v3(Math.sin(a), 0, Math.cos(a)))]);
    expect(value(r, 'Winkel')).toBeCloseTo(0.005, 9);
    expect(value(r, 'Abstand')).toBeCloseTo(2, 6);
  });

  it('plane and cylinder: axis parallel to the plane', () => {
    const r = measure([cyl(v3(0, 0, 6), v3(0, 1, 0)), plane(O, v3(0, 0, 1))]);
    expect(value(r, 'Winkel')).toBeCloseTo(0, 9);
    expect(value(r, 'Abstand zur Achse')).toBeCloseTo(6, 9);
    expect(value(measure([cyl(O, v3(0, 0, 1)), plane(O, v3(0, 0, 1))]), 'Winkel')).toBeCloseTo(90, 9);
  });

  it('edge and cylinder: parallel edge gets the axis distance', () => {
    const r = measure([edge(v3(0, 3, 4), v3(10, 3, 4)), cyl(O, v3(1, 0, 0))]);
    expect(value(r, 'Winkel')).toBeCloseTo(0, 9);
    expect(value(r, 'Abstand zur Achse')).toBeCloseTo(5, 9);
    expect(labels(measure([edge(O, v3(0, 1, 0)), cyl(O, v3(1, 0, 0))]))).toEqual(['Winkel']);
  });

  it('parallel cylinders give the axis distance', () => {
    const r = measure([cyl(O, v3(0, 0, 1)), cyl(v3(6, 8, 50), v3(0, 0, -1))]);
    expect(value(r, 'Winkel')).toBeCloseTo(0, 9);
    expect(value(r, 'Achsabstand')).toBeCloseTo(10, 9);
  });
});

describe('measure: three or more', () => {
  it('sums edge lengths, face areas and polyline lengths', () => {
    const edges = [edge(O, v3(10, 0, 0)), edge(O, v3(0, 5, 0)), circle(O, v3(0, 0, 1), 1)];
    expect(measure(edges).rows).toEqual([{ label: 'Gesamtlänge', value: 15 + 2 * Math.PI, unit: 'mm' }]);
    const planes = [plane(O, v3(0, 0, 1), 10), plane(O, v3(0, 1, 0), 20), plane(O, v3(1, 0, 0), 5)];
    expect(measure(planes).rows).toEqual([{ label: 'Gesamtfläche', value: 35, unit: 'mm²' }]);
    const r = measure([pt(0, 0, 0), pt(3, 4, 0), pt(3, 4, 10)]);
    expect(value(r, 'Abstand')).toBeCloseTo(15, 9);
    expect(r.lines).toHaveLength(2);
  });

  it('mixed kinds measure the first pair only', () => {
    const r = measure([pt(0, 0, 0), pt(0, 0, 9), edge(O, v3(1, 0, 0))]);
    expect(value(r, 'Abstand')).toBeCloseTo(9, 9);
  });
});

// --- picks on a real part ---

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

const hem: HemFeature = {
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
};

const hole = (id: string, u: number, diameter = 10): CutFeature => ({
  id,
  name: 'Bohrung',
  type: 'cut',
  edge: { tag: 'base:e0', at: { x: 50, y: 0 } },
  u,
  v: 25,
  shape: 'circle',
  diameter,
  width: 0,
  height: 0,
  angle: 0,
  count: 1,
  spacing: 0,
});

function pick(part: Part, tag: string): PickEdge {
  const e = pickEdges(part, computePlacements(part)).find((x) => x.tag === tag);
  if (!e) throw new Error(`pick edge ${tag} not found`);
  return e;
}

describe('measure: picks on a part', () => {
  it('base and 90° flange faces are at 90°', () => {
    const { part } = build(doc([plate(), flange()]));
    const base = faceEntity(part, 'base', 0, O, false);
    for (const s of [0, 1] as const) {
      expect(value(measure([base, faceEntity(part, 'fl', s, O, false)]), 'Winkel')).toBeCloseTo(90, 6);
    }
    // flat: everything lies in one plane
    const r = measure([faceEntity(part, 'base', 0, O, true), faceEntity(part, 'fl', 0, O, true)]);
    expect(value(r, 'Winkel')).toBeCloseTo(0, 6);
    expect(value(r, 'Abstand')).toBeCloseTo(0, 6);
  });

  it('top and bottom of the base are T apart, area and perimeter of the plate', () => {
    const { part } = build(doc([plate()]));
    const top = faceEntity(part, 'base', 0, O, false);
    expect(top.kind === 'plane' && top.normal.y).toBeCloseTo(1, 9);
    const r = measure([top, faceEntity(part, 'base', 1, O, false)]);
    expect(value(r, 'Abstand')).toBeCloseTo(T, 9);
    expect(value(measure([top]), 'Fläche')).toBeCloseTo(5000, 6);
    expect(value(measure([top]), 'Umfang')).toBeCloseTo(300, 6);
  });

  it('closed hem lies parallel on the base, its outside 2·T above the base bottom', () => {
    const { part } = build(doc([plate(), hem]));
    const outside = faceEntity(part, 'hem', 1, O, false);
    const top = measure([faceEntity(part, 'base', 0, O, false), outside]);
    expect(value(top, 'Winkel')).toBeCloseTo(0, 6);
    expect(value(top, 'Abstand')).toBeCloseTo(T, 6);
    expect(value(measure([outside, faceEntity(part, 'base', 1, O, false)]), 'Abstand')).toBeCloseTo(2 * T, 6);
    expect(value(measure([faceEntity(part, 'base', 0, O, false), faceEntity(part, 'hem', 0, O, false)]), 'Abstand')).toBeCloseTo(0, 6);
  });

  it('bend surfaces are cylinders with inner radius R and outer radius R + T', () => {
    const { part } = build(doc([plate(), flange()]));
    const radii = ([0, 1] as const).map((s) => bendEntity(part, 'fl', s, O, false));
    const r = radii.map((c) => value(measure([c]), 'Radius')).sort();
    expect(r[0]).toBeCloseTo(R, 9);
    expect(r[1]).toBeCloseTo(R + T, 9);
    const c = measure([radii[0]]);
    expect(value(c, 'Winkel')).toBeCloseTo(90, 9);
    expect(value(c, 'Länge')).toBeCloseTo(100, 9);
    // axis: outer surfaces are flush with y = 0 and z = 0
    expect(value(measure([pt(37, R + T, -(R + T)), radii[0]]), 'Abstand zur Achse')).toBeCloseTo(0, 6);
    // the axis is parallel to the base
    const pr = measure([faceEntity(part, 'base', 1, O, false), radii[1]]);
    expect(value(pr, 'Winkel')).toBeCloseTo(0, 6);
    expect(value(pr, 'Abstand zur Achse')).toBeCloseTo(R + T, 6);
  });

  it('flat bend is the strip rectangle; end wall is an annular sector', () => {
    const { part } = build(doc([plate(), flange()]));
    const flat = bendEntity(part, 'fl', 0, O, true);
    expect(flat.kind).toBe('plane');
    expect(value(measure([flat]), 'Fläche')).toBeCloseTo(BA90 * 100, 6);
    const side = bendEntity(part, 'fl', 2, v3(99, 3, -3), false);
    expect(side.kind === 'plane' && side.normal.x).toBeCloseTo(1, 9);
    expect(value(measure([side]), 'Fläche')).toBeCloseTo((Math.PI / 4) * ((R + T) ** 2 - R ** 2), 6);
  });

  it('round hole edge gives the diameter and the centre at the picked side', () => {
    const { part } = build(doc([plate(), hole('cut', 50)]));
    const e = pick(part, 'cut:h0');
    const top = edgeEntity(part, e, 'top', false);
    expect(top.label).toBe('Kreiskante Ø 10 mm');
    const r = measure([top]);
    expect(value(r, 'Durchmesser')).toBeCloseTo(10, 9);
    expect(value(r, 'Mittelpunkt X')).toBeCloseTo(50, 9);
    expect(value(r, 'Mittelpunkt Y')).toBeCloseTo(T, 9);
    expect(value(r, 'Mittelpunkt Z')).toBeCloseTo(-25, 9);
    expect(value(measure([edgeEntity(part, e, 'bottom', false)]), 'Mittelpunkt Y')).toBeCloseTo(0, 9);
    // hole centre to the plate edge
    const front = edgeEntity(part, pick(part, 'base:e0'), 'top', false);
    expect(front.label).toBe('Kante');
    expect(value(measure([front, top]), 'Abstand zum Mittelpunkt')).toBeCloseTo(25, 9);
  });

  it('hole wall is a cylinder; two holes give the axis distance', () => {
    const { part, fp } = build(doc([plate(), hole('c1', 20), hole('c2', 70, 8)]));
    const w1 = faceEntity(part, 'base', 2, v3(25, 1, -25), false);
    const w2 = faceEntity(part, 'base', 2, v3(66, 1, -25), false);
    expect(w1.kind).toBe('cylinder');
    expect(w1.label).toBe('Bohrung Ø 10 mm');
    expect(value(measure([w2]), 'Radius')).toBeCloseTo(4, 6);
    expect(value(measure([w1, w2]), 'Achsabstand')).toBeCloseTo(50, 6);
    // face area without the holes: matches the flat pattern of this single face
    const area = value(measure([faceEntity(part, 'base', 0, O, false)]), 'Fläche');
    expect(area).toBeCloseTo(fp.area, 6);
    expect(area).toBeCloseTo(5000 - Math.PI * (25 + 16), -1);
  });

  it('outline side wall is a plane facing outwards', () => {
    const { part } = build(doc([plate()]));
    const w = faceEntity(part, 'base', 2, v3(40, 1, 0.01), false);
    expect(w.kind).toBe('plane');
    if (w.kind !== 'plane') return;
    expectV3(w.normal, v3(0, 0, 1));
    expect(w.area).toBeCloseTo(100 * T, 9);
    const opposite = faceEntity(part, 'base', 2, v3(40, 1, -50), false);
    expect(value(measure([w, opposite]), 'Abstand')).toBeCloseTo(50, 9);
  });

  it('corner edge runs through the thickness', () => {
    const { part } = build(doc([plate()]));
    const c = pickCorners(part, computePlacements(part))[0];
    const e = edgeEntity(part, c, 'top', false);
    expect(e.label).toBe('Eckkante');
    expect(value(measure([e]), 'Länge')).toBeCloseTo(T, 9);
  });
});
