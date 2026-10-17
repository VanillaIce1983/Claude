// Measuring (Messen): distances, angles, lengths, radii and areas between
// picked entities. Pure geometry, no UI. All values in mm and degrees.

import type { PickEdge } from './mesh';
import { Bend, CircleInfo, Face, Part, computePlacements, isSeamTag, seamTag, stripFlat, stripOutline, stripPoint, stripRange } from './model';
import { Ring, distPointSegment, signedArea } from './polygon';
import {
  Affine,
  EPS,
  V2,
  V3,
  add2,
  add3,
  applyA,
  applyDirA,
  cross3,
  deg,
  dist2,
  dist3,
  dot2,
  dot3,
  invertRigid,
  len3,
  rad,
  rotCW,
  scale2,
  scale3,
  sub2,
  sub3,
  v2,
  v3,
} from './vec';

export type MeasureEntity =
  | { kind: 'point'; p: V3; label: string }
  | {
      kind: 'edge';
      a: V3;
      b: V3;
      label: string;
      /** Set when the edge belongs to a round hole: the whole circle. */
      circle?: { center: V3; normal: V3; r: number };
    }
  | {
      kind: 'plane';
      /** Any point on the plane. */
      origin: V3;
      /** Unit normal. */
      normal: V3;
      /** Area of the picked planar face (mm²) and its perimeter (mm). */
      area: number;
      perimeter: number;
      label: string;
    }
  | {
      kind: 'cylinder';
      axisPoint: V3;
      /** Unit axis direction. */
      axisDir: V3;
      radius: number;
      /** Swept angle of the cylindrical face (degrees) and its length along the axis (mm). */
      angleDeg: number;
      length: number;
      /** Area of the picked cylindrical face (mm²). */
      area: number;
      label: string;
    };

export type MeasureUnit = 'mm' | '°' | 'mm²' | '';

export interface MeasureRow {
  label: string;
  value: number;
  unit: MeasureUnit;
}

/** A dimension to draw in the 3D view: a line with a text label at its midpoint. */
export interface MeasureLine {
  a: V3;
  b: V3;
  text: string;
}

export interface MeasureResult {
  rows: MeasureRow[];
  lines: MeasureLine[];
}

type PointEnt = Extract<MeasureEntity, { kind: 'point' }>;
type EdgeEnt = Extract<MeasureEntity, { kind: 'edge' }>;
type PlaneEnt = Extract<MeasureEntity, { kind: 'plane' }>;
type CylEnt = Extract<MeasureEntity, { kind: 'cylinder' }>;
type Circle = NonNullable<EdgeEnt['circle']>;

/** Below this angle two directions count as parallel. */
const PARALLEL_DEG = 0.01;

const RANK: Record<MeasureEntity['kind'], number> = { point: 0, edge: 1, plane: 2, cylinder: 3 };

/** German number: comma, at most 2 decimals, no trailing zeros. */
const DE = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 2 });
/** Same rounding and grouping as the panel (ui/format num), so a label always matches its row. */
const num = (v: number): string => (Number.isFinite(v) ? DE.format(Math.abs(v) < 0.005 ? 0 : v) : '–');
const mmText = (v: number): string => `${num(v)} mm`;

const emptyResult = (): MeasureResult => ({ rows: [], lines: [] });

function addRow(res: MeasureResult, label: string, value: number, unit: MeasureUnit): void {
  res.rows.push({ label, value, unit });
}

function addLine(res: MeasureResult, a: V3, b: V3, text: string): void {
  if (dist3(a, b) > 1e-9) res.lines.push({ a, b, text });
}

function unit(a: V3): V3 | null {
  const l = len3(a);
  return l > EPS ? scale3(a, 1 / l) : null;
}

const mid3 = (a: V3, b: V3): V3 => scale3(add3(a, b), 0.5);
const lineDir = (e: EdgeEnt): V3 => unit(sub3(e.b, e.a)) ?? v3(1, 0, 0);

function anyPerpendicular(n: V3): V3 {
  const t = Math.abs(n.x) < 0.9 ? v3(1, 0, 0) : v3(0, 1, 0);
  return unit(cross3(n, t)) ?? v3(0, 0, 1);
}

/** Angle between two directions, 0…180°. atan2 stays accurate near 0° and 180°. */
const dirAngle = (u: V3, v: V3): number => deg(Math.atan2(len3(cross3(u, v)), dot3(u, v)));

/** Angle between two undirected lines (or between two planes via their normals), 0…90°. */
function lineAngle(u: V3, v: V3): number {
  const a = dirAngle(u, v);
  return Math.min(a, 180 - a);
}

/** Angle between a line and a plane, 0° = parallel. */
const linePlaneAngle = (u: V3, n: V3): number => deg(Math.atan2(Math.abs(dot3(u, n)), len3(cross3(u, n))));

/** `d` must be a unit vector. */
const footOnLine = (p: V3, o: V3, d: V3): V3 => add3(o, scale3(d, dot3(sub3(p, o), d)));
const footOnPlane = (p: V3, o: V3, n: V3): V3 => sub3(p, scale3(n, dot3(sub3(p, o), n)));

function closestOnSegment(p: V3, a: V3, b: V3): V3 {
  const ab = sub3(b, a);
  const l2 = dot3(ab, ab);
  if (l2 < EPS * EPS) return a;
  const t = Math.max(0, Math.min(1, dot3(sub3(p, a), ab) / l2));
  return add3(a, scale3(ab, t));
}

function closestOnCircle(p: V3, c: Circle): V3 {
  const q = footOnPlane(p, c.center, c.normal);
  const u = unit(sub3(q, c.center)) ?? anyPerpendicular(c.normal);
  return add3(c.center, scale3(u, c.r));
}

const clamp01 = (x: number): number => Math.max(0, Math.min(1, x));

/** Closest points of segments p1q1 and p2q2; handles points and parallel segments. */
function closestSegments(p1: V3, q1: V3, p2: V3, q2: V3): [V3, V3] {
  const d1 = sub3(q1, p1);
  const d2 = sub3(q2, p2);
  const r = sub3(p1, p2);
  const a = dot3(d1, d1);
  const e = dot3(d2, d2);
  const f = dot3(d2, r);
  const tiny = EPS * EPS;
  let s = 0;
  let t = 0;
  if (a <= tiny && e <= tiny) {
    // both degenerate
  } else if (a <= tiny) {
    t = clamp01(f / e);
  } else {
    const c = dot3(d1, r);
    if (e <= tiny) {
      s = clamp01(-c / a);
    } else {
      const b = dot3(d1, d2);
      const denom = a * e - b * b;
      // parallel segments: any s works, the clamping below finds the matching t
      s = denom > 1e-12 * a * e ? clamp01((b * f - c * e) / denom) : 0;
      t = (b * s + f) / e;
      if (t < 0) {
        t = 0;
        s = clamp01(-c / a);
      } else if (t > 1) {
        t = 1;
        s = clamp01((b - c) / a);
      }
    }
  }
  return [add3(p1, scale3(d1, s)), add3(p2, scale3(d2, t))];
}

/** Unit directions everywhere; an entity without a usable direction falls back to a point. */
function sanitize(e: MeasureEntity): MeasureEntity {
  switch (e.kind) {
    case 'point':
      return e;
    case 'edge': {
      const n = e.circle && e.circle.r > EPS ? unit(e.circle.normal) : null;
      if (e.circle && n) return { ...e, circle: { ...e.circle, normal: n } };
      if (dist3(e.a, e.b) > EPS) return { kind: 'edge', a: e.a, b: e.b, label: e.label };
      return { kind: 'point', p: e.a, label: e.label };
    }
    case 'plane': {
      const n = unit(e.normal);
      return n ? { ...e, normal: n } : { kind: 'point', p: e.origin, label: e.label };
    }
    case 'cylinder': {
      const d = unit(e.axisDir);
      return d ? { ...e, axisDir: d } : { kind: 'point', p: e.axisPoint, label: e.label };
    }
  }
}

const edgeLength = (e: EdgeEnt): number => (e.circle ? 2 * Math.PI * e.circle.r : dist3(e.a, e.b));

function single(e: MeasureEntity): MeasureResult {
  const res = emptyResult();
  switch (e.kind) {
    case 'point':
      addRow(res, 'X', e.p.x, 'mm');
      addRow(res, 'Y', e.p.y, 'mm');
      addRow(res, 'Z', e.p.z, 'mm');
      break;
    case 'edge': {
      const L = edgeLength(e);
      addRow(res, e.circle ? 'Umfang' : 'Länge', L, 'mm');
      const c = e.circle;
      if (!c) {
        addLine(res, e.a, e.b, mmText(L));
        break;
      }
      addRow(res, 'Durchmesser', 2 * c.r, 'mm');
      addRow(res, 'Radius', c.r, 'mm');
      addRow(res, 'Mittelpunkt X', c.center.x, 'mm');
      addRow(res, 'Mittelpunkt Y', c.center.y, 'mm');
      addRow(res, 'Mittelpunkt Z', c.center.z, 'mm');
      const u = sub3(closestOnCircle(e.a, c), c.center);
      addLine(res, sub3(c.center, u), add3(c.center, u), `Ø ${mmText(2 * c.r)}`);
      break;
    }
    case 'plane':
      addRow(res, 'Fläche', e.area, 'mm²');
      addRow(res, 'Umfang', e.perimeter, 'mm');
      break;
    case 'cylinder':
      addRow(res, 'Radius', e.radius, 'mm');
      addRow(res, 'Durchmesser', 2 * e.radius, 'mm');
      addRow(res, 'Winkel', e.angleDeg, '°');
      addRow(res, 'Bogenlänge', e.radius * rad(e.angleDeg), 'mm');
      addRow(res, 'Länge', e.length, 'mm');
      addRow(res, 'Fläche', e.area, 'mm²');
      break;
  }
  return res;
}

function pointPoint(res: MeasureResult, a: PointEnt, b: PointEnt): void {
  const d = sub3(b.p, a.p);
  const L = len3(d);
  addRow(res, 'Abstand', L, 'mm');
  addRow(res, 'ΔX', Math.abs(d.x), 'mm');
  addRow(res, 'ΔY', Math.abs(d.y), 'mm');
  addRow(res, 'ΔZ', Math.abs(d.z), 'mm');
  addLine(res, a.p, b.p, mmText(L));
}

function pointEdge(res: MeasureResult, p: PointEnt, e: EdgeEnt): void {
  const q = e.circle ? closestOnCircle(p.p, e.circle) : closestOnSegment(p.p, e.a, e.b);
  const L = dist3(p.p, q);
  addRow(res, 'Abstand', L, 'mm');
  if (e.circle) addRow(res, 'Abstand zum Mittelpunkt', dist3(p.p, e.circle.center), 'mm');
  addLine(res, p.p, q, mmText(L));
}

function pointPlane(res: MeasureResult, p: PointEnt, pl: PlaneEnt): void {
  const q = footOnPlane(p.p, pl.origin, pl.normal);
  const L = dist3(p.p, q);
  addRow(res, 'Senkrechter Abstand', L, 'mm');
  addLine(res, p.p, q, mmText(L));
}

function pointCylinder(res: MeasureResult, p: PointEnt, c: CylEnt): void {
  const q = footOnLine(p.p, c.axisPoint, c.axisDir);
  const L = dist3(p.p, q);
  addRow(res, 'Abstand zur Achse', L, 'mm');
  addLine(res, p.p, q, mmText(L));
}

/** Point on line a near the middle of the stretch it shares with segment b (for parallel lines). */
function overlapPoint(a: EdgeEnt, b: EdgeEnt, u: V3): V3 {
  const L = dist3(a.a, a.b);
  const t1 = dot3(sub3(b.a, a.a), u);
  const t2 = dot3(sub3(b.b, a.a), u);
  const lo = Math.max(0, Math.min(t1, t2));
  const hi = Math.min(L, Math.max(t1, t2));
  const t = Math.max(0, Math.min(L, (lo + hi) / 2));
  return add3(a.a, scale3(u, t));
}

function edgeEdge(res: MeasureResult, a: EdgeEnt, b: EdgeEnt): void {
  if (a.circle && b.circle) {
    const L = dist3(a.circle.center, b.circle.center);
    addRow(res, 'Mittelpunktabstand', L, 'mm');
    addRow(res, 'Winkel', lineAngle(a.circle.normal, b.circle.normal), '°');
    addLine(res, a.circle.center, b.circle.center, mmText(L));
    return;
  }
  if (a.circle || b.circle) {
    const [c, l] = a.circle ? [a.circle, b] : [b.circle!, a];
    const q = closestOnSegment(c.center, l.a, l.b);
    const L = dist3(c.center, q);
    addRow(res, 'Abstand zum Mittelpunkt', L, 'mm');
    addLine(res, c.center, q, mmText(L));
    return;
  }
  const u = lineDir(a);
  const v = lineDir(b);
  const ang = lineAngle(u, v);
  addRow(res, 'Winkel', ang, '°');
  if (ang < PARALLEL_DEG) {
    const p = overlapPoint(a, b, u);
    const q = footOnLine(p, b.a, v);
    const L = dist3(p, q);
    addRow(res, 'Abstand', L, 'mm');
    addLine(res, p, q, mmText(L));
    return;
  }
  const [p, q] = closestSegments(a.a, a.b, b.a, b.b);
  const L = dist3(p, q);
  addRow(res, 'Minimaler Abstand', L, 'mm');
  addLine(res, p, q, mmText(L));
}

function edgePlane(res: MeasureResult, e: EdgeEnt, pl: PlaneEnt): void {
  // a circle is measured by its own plane, a line by its direction
  const ang = e.circle ? lineAngle(e.circle.normal, pl.normal) : linePlaneAngle(lineDir(e), pl.normal);
  addRow(res, 'Winkel', ang, '°');
  if (ang >= PARALLEL_DEG) return;
  const p = e.circle ? e.circle.center : mid3(e.a, e.b);
  const q = footOnPlane(p, pl.origin, pl.normal);
  const L = dist3(p, q);
  addRow(res, 'Abstand', L, 'mm');
  addLine(res, p, q, mmText(L));
}

function edgeCylinder(res: MeasureResult, e: EdgeEnt, c: CylEnt): void {
  const ang = lineAngle(e.circle ? e.circle.normal : lineDir(e), c.axisDir);
  addRow(res, 'Winkel', ang, '°');
  // a circle's centre has a distance to any axis; a line only to a parallel one
  if (!e.circle && ang >= PARALLEL_DEG) return;
  const p = e.circle ? e.circle.center : mid3(e.a, e.b);
  const q = footOnLine(p, c.axisPoint, c.axisDir);
  const L = dist3(p, q);
  addRow(res, 'Abstand zur Achse', L, 'mm');
  addLine(res, p, q, mmText(L));
}

function planePlane(res: MeasureResult, a: PlaneEnt, b: PlaneEnt): void {
  const ang = lineAngle(a.normal, b.normal);
  addRow(res, 'Winkel', ang, '°');
  if (ang >= PARALLEL_DEG) return;
  const L = Math.abs(dot3(sub3(b.origin, a.origin), a.normal));
  addRow(res, 'Abstand', L, 'mm');
  addLine(res, a.origin, footOnPlane(a.origin, b.origin, b.normal), mmText(L));
}

function planeCylinder(res: MeasureResult, pl: PlaneEnt, c: CylEnt): void {
  const ang = linePlaneAngle(c.axisDir, pl.normal);
  addRow(res, 'Winkel', ang, '°');
  if (ang >= PARALLEL_DEG) return;
  const q = footOnPlane(c.axisPoint, pl.origin, pl.normal);
  const L = dist3(c.axisPoint, q);
  addRow(res, 'Abstand zur Achse', L, 'mm');
  addLine(res, c.axisPoint, q, mmText(L));
}

function cylinderCylinder(res: MeasureResult, a: CylEnt, b: CylEnt): void {
  const ang = lineAngle(a.axisDir, b.axisDir);
  addRow(res, 'Winkel', ang, '°');
  if (ang >= PARALLEL_DEG) return;
  const q = footOnLine(a.axisPoint, b.axisPoint, b.axisDir);
  const L = dist3(a.axisPoint, q);
  addRow(res, 'Achsabstand', L, 'mm');
  addLine(res, a.axisPoint, q, mmText(L));
}

function pair(x: MeasureEntity, y: MeasureEntity): MeasureResult {
  // order-independent: always handle the lower-ranked kind first
  const [a, b] = RANK[x.kind] <= RANK[y.kind] ? [x, y] : [y, x];
  const res = emptyResult();
  switch (a.kind) {
    case 'point':
      if (b.kind === 'point') pointPoint(res, a, b);
      else if (b.kind === 'edge') pointEdge(res, a, b);
      else if (b.kind === 'plane') pointPlane(res, a, b);
      else pointCylinder(res, a, b);
      break;
    case 'edge':
      if (b.kind === 'edge') edgeEdge(res, a, b);
      else if (b.kind === 'plane') edgePlane(res, a, b);
      else if (b.kind === 'cylinder') edgeCylinder(res, a, b);
      break;
    case 'plane':
      if (b.kind === 'plane') planePlane(res, a, b);
      else if (b.kind === 'cylinder') planeCylinder(res, a, b);
      break;
    case 'cylinder':
      if (b.kind === 'cylinder') cylinderCylinder(res, a, b);
      break;
  }
  return res;
}

const isEdge = (e: MeasureEntity): e is EdgeEnt => e.kind === 'edge';
const isSurface = (e: MeasureEntity): e is PlaneEnt | CylEnt => e.kind === 'plane' || e.kind === 'cylinder';
const isPoint = (e: MeasureEntity): e is PointEnt => e.kind === 'point';

/** Sums for three or more entities of one kind; null when the kinds are mixed. */
function totals(list: MeasureEntity[]): MeasureResult | null {
  const res = emptyResult();
  if (list.every(isEdge)) {
    addRow(res, 'Gesamtlänge', list.reduce((s, e) => s + edgeLength(e), 0), 'mm');
  } else if (list.every(isSurface)) {
    addRow(res, 'Gesamtfläche', list.reduce((s, e) => s + e.area, 0), 'mm²');
  } else if (list.every(isPoint)) {
    let sum = 0;
    for (let i = 0; i + 1 < list.length; i++) {
      const L = dist3(list[i].p, list[i + 1].p);
      sum += L;
      addLine(res, list[i].p, list[i + 1].p, mmText(L));
    }
    addRow(res, 'Abstand', sum, 'mm');
  } else {
    return null;
  }
  return res;
}

/**
 * Measure one or more entities.
 * - 1 entity: its own properties (coordinates, length, diameter, area …)
 * - 2 entities: distance and/or angle between them, plus a dimension line
 * - more: totals (summed edge lengths or face areas) where meaningful
 */
export function measure(entities: MeasureEntity[]): MeasureResult {
  if (entities.length === 0) return emptyResult();
  if (entities.length === 1) return single(sanitize(entities[0]));
  if (entities.length > 2) {
    const t = totals(entities);
    if (t) return t;
  }
  return pair(sanitize(entities[0]), sanitize(entities[1]));
}

// --- builders: picks in the view → entities ---

function placementOf(part: Part, faceId: string, flat: boolean): Affine {
  if (flat) return part.anchor.M;
  const M = computePlacements(part).get(faceId);
  if (!M) throw new Error('Die Fläche ist nicht mit dem Teil verbunden und kann nicht gemessen werden.');
  return M;
}

const faceNormal = (M: Affine): V3 => unit(applyDirA(M, v3(0, 0, 1))) ?? v3(0, 0, 1);

/** Exact circle of a round hole, if `tag` names one whose ring is still whole. */
function holeCircle(part: Part, face: Face, tag: string): CircleInfo | undefined {
  const ring = face.holes.find((h) => h.tags.includes(tag));
  if (!ring || !ring.tags.every((t) => t === tag)) return undefined;
  return part.circles.get(tag);
}

function ringLength(r: Ring): number {
  let s = 0;
  for (let i = 0; i < r.pts.length; i++) s += dist2(r.pts[i], r.pts[(i + 1) % r.pts.length]);
  return s;
}

const diaLabel = (prefix: string, r: number): string => `${prefix} Ø ${mmText(2 * r)}`;

export function pointEntity(p: V3, label: string): MeasureEntity {
  return { kind: 'point', p: { ...p }, label };
}

export function edgeEntity(part: Part, e: PickEdge, side: 'top' | 'bottom', flat: boolean): MeasureEntity {
  const pts = flat ? e.flat : e.folded;
  // corner edges run through the thickness: [top, bottom, top, bottom]
  if (e.corner) return { kind: 'edge', a: pts[0], b: pts[1], label: 'Eckkante' };
  const [a, b] = side === 'top' ? [pts[0], pts[1]] : [pts[2], pts[3]];
  const face = part.faces.get(e.faceId);
  const c = e.hole && face ? holeCircle(part, face, e.tag) : undefined;
  if (!c) return { kind: 'edge', a, b, label: 'Kante' };
  const M = placementOf(part, e.faceId, flat);
  const h = side === 'top' ? part.T : 0;
  return {
    kind: 'edge',
    a,
    b,
    label: diaLabel('Kreiskante', c.r),
    circle: { center: applyA(M, v3(c.c.x, c.c.y, h)), normal: faceNormal(M), r: c.r },
  };
}

export function faceEntity(part: Part, faceId: string, surface: 0 | 1 | 2, hitPoint: V3, flat: boolean): MeasureEntity {
  const face = part.faces.get(faceId);
  if (!face) throw new Error('Die gewählte Fläche existiert nicht mehr. Bitte neu auswählen.');
  const M = placementOf(part, faceId, flat);
  if (surface === 2) return sideWall(part, face, M, hitPoint);
  const p0 = face.outer.pts[0];
  const n = faceNormal(M);
  return {
    kind: 'plane',
    origin: applyA(M, v3(p0.x, p0.y, surface === 0 ? part.T : 0)),
    normal: surface === 0 ? n : scale3(n, -1),
    area: Math.abs(signedArea(face.outer.pts)) - face.holes.reduce((s, h) => s + Math.abs(signedArea(h.pts)), 0),
    perimeter: [face.outer, ...face.holes].reduce((s, r) => s + ringLength(r), 0),
    label: 'Fläche',
  };
}

interface NearEdge {
  ring: Ring;
  i: number;
  hole: boolean;
}

/** Outline or hole edge nearest to p; at a corner the plain edge wins over a seam. */
function nearestEdge(face: Face, p: V2, allowSeams: boolean): NearEdge | null {
  const rings: [Ring, boolean][] = [[face.outer, false], ...face.holes.map((h) => [h, true] as [Ring, boolean])];
  let best: NearEdge | null = null;
  let bestD = Infinity;
  for (const [ring, hole] of rings) {
    for (let i = 0; i < ring.pts.length; i++) {
      const a = ring.pts[i];
      const b = ring.pts[(i + 1) % ring.pts.length];
      const seam = isSeamTag(ring.tags[i]);
      if ((!allowSeams && seam) || dist2(a, b) < EPS) continue;
      const d = distPointSegment(p, a, b) + (seam ? 1e-6 : 0);
      if (d < bestD) {
        bestD = d;
        best = { ring, i, hole };
      }
    }
  }
  return best;
}

/** Interval of [lo, hi] not covered by `covered` that contains w (or lies nearest to it). */
function freeInterval(lo: number, hi: number, covered: [number, number][], w: number): [number, number] | null {
  const gaps: [number, number][] = [];
  let at = lo;
  for (const [c0, c1] of [...covered].sort((x, y) => x[0] - y[0])) {
    if (c0 > at + 1e-6) gaps.push([at, Math.min(c0, hi)]);
    at = Math.max(at, c1);
  }
  if (hi > at + 1e-6) gaps.push([at, hi]);
  const dist = ([g0, g1]: [number, number]): number => Math.max(0, g0 - w, w - g1);
  return gaps.reduce<[number, number] | null>((best, g) => (!best || dist(g) < dist(best) ? g : best), null);
}

const seamOf = (part: Part, tag: string): { bend: Bend; f: 0 | 1 } | null => {
  const m = /^seam:(.+):([AB])$/.exec(tag);
  const bend = m ? part.bends.get(m[1]) : undefined;
  return bend ? { bend, f: m![2] === 'A' ? 0 : 1 } : null;
};

/** Side wall of a face at the outline or hole edge nearest to the hit point. */
function sideWall(part: Part, face: Face, M: Affine, hitPoint: V3): MeasureEntity {
  const q = applyA(invertRigid(M), hitPoint);
  const p = v2(q.x, q.y);
  // a hit on a wall lies on its edge; a seam edge has a wall only where no bend strip continues it
  let best = nearestEdge(face, p, true);
  let span: [V2, V2] | null = null;
  if (best && isSeamTag(best.ring.tags[best.i])) {
    const a = best.ring.pts[best.i];
    const b = best.ring.pts[(best.i + 1) % best.ring.pts.length];
    const s = seamOf(part, best.ring.tags[best.i]);
    if (s) {
      const along = (x: V2): number => dot2(sub2(x, s.bend.origin), s.bend.dir);
      const [wa, wb] = [along(a), along(b)];
      const free = freeInterval(Math.min(wa, wb), Math.max(wa, wb), [stripRange(s.bend, s.f)], along(p));
      // points on the seam at w, keeping the outline's direction
      const at = (w: number): V2 => add2(a, scale2(sub2(b, a), (w - wa) / (wb - wa)));
      if (free) span = wa < wb ? [at(free[0]), at(free[1])] : [at(free[1]), at(free[0])];
    }
    if (!span) best = nearestEdge(face, p, false);
  }
  if (!best) throw new Error('An dieser Stelle wurde keine Seitenfläche gefunden. Bitte erneut auswählen.');
  const T = part.T;
  const c = best.hole ? holeCircle(part, face, best.ring.tags[best.i]) : undefined;
  if (c) {
    return {
      kind: 'cylinder',
      axisPoint: applyA(M, v3(c.c.x, c.c.y, 0)),
      axisDir: faceNormal(M),
      radius: c.r,
      angleDeg: 360,
      length: T,
      area: 2 * Math.PI * c.r * T,
      label: diaLabel('Bohrung', c.r),
    };
  }
  const [a, b] = span ?? [best.ring.pts[best.i], best.ring.pts[(best.i + 1) % best.ring.pts.length]];
  const len = dist2(a, b);
  // CCW outline and CW holes: rotCW points away from the material in both cases
  const out = rotCW(scale2(sub2(b, a), 1 / len));
  return {
    kind: 'plane',
    origin: applyA(M, v3(a.x, a.y, 0)),
    normal: unit(applyDirA(M, v3(out.x, out.y, 0))) ?? v3(0, 0, 1),
    area: len * T,
    perimeter: 2 * (len + T),
    label: 'Seitenfläche',
  };
}

/** Strip fraction of a point in face A's frame (folded) or in flat coordinates. */
function hitFraction(b: Bend, T: number, q: V3, flat: boolean): number {
  const off = dot2(sub2(v2(q.x, q.y), b.origin), b.nrm);
  let f = 0;
  if (flat) f = b.ba > 0 ? off / b.ba : 0;
  else {
    const phi = Math.atan2(off, b.up ? T + b.radius - q.z : q.z + b.radius);
    f = (phi < -1e-6 ? phi + 2 * Math.PI : phi) / b.angle;
  }
  return Math.min(1, Math.max(0, f));
}

/** ∫ (wr − wl) dφ over the bend: the strip width integrated across the bend, exact for mitred ends. */
function sweptWidth(b: Bend): number {
  const integral = (end: Bend['ext0']): number =>
    (end?.pieces ?? []).reduce((sum, q) => {
      const p0 = b.angle * q.f0;
      const p1 = b.angle * q.f1;
      return sum + q.a * (p1 - p0) + q.b * (Math.cos(p0) - Math.cos(p1)) + q.c * (Math.sin(p1) - Math.sin(p0));
    }, 0);
  return (b.w1 - b.w0) * b.angle + integral(b.ext0) + integral(b.ext1);
}

/**
 * Side wall along a strip edge (f = 0 or 1) where no face continues the
 * strip, e.g. the free end of a rolled hem or a strip that runs past its face.
 */
function stripEdgeWall(part: Part, b: Bend, M: Affine, f: 0 | 1, w: number, flat: boolean): MeasureEntity {
  const T = part.T;
  const [hl, hr] = stripRange(b, f);
  const faceId = f === 0 ? b.faceA : b.faceB;
  const face = faceId ? part.faces.get(faceId) : undefined;
  const covered: [number, number][] = [];
  const tag = seamTag(b.id, f === 0 ? 'A' : 'B');
  const along = (x: V2): number => dot2(sub2(x, b.origin), b.dir);
  for (const ring of face ? [face.outer] : []) {
    ring.pts.forEach((a, i) => {
      if (ring.tags[i] !== tag) return;
      const [wa, wb] = [along(a), along(ring.pts[(i + 1) % ring.pts.length])];
      covered.push([Math.min(wa, wb), Math.max(wa, wb)]);
    });
  }
  const [s0, s1] = freeInterval(hl, hr, covered, w) ?? [hl, hr];
  const len = s1 - s0;
  let normal: V3;
  let origin: V3;
  if (flat) {
    normal = applyDirA(M, v3(f === 0 ? -b.nrm.x : b.nrm.x, f === 0 ? -b.nrm.y : b.nrm.y, 0));
    const o = stripFlat(b, s0, f);
    origin = applyA(M, v3(o.x, o.y, 0));
  } else {
    // the strip tangent across the bend at that edge, pointing away from the strip
    const phi = b.angle * f;
    const k = f === 0 ? -1 : 1;
    normal = applyDirA(M, v3(k * b.nrm.x * Math.cos(phi), k * b.nrm.y * Math.cos(phi), k * (b.up ? Math.sin(phi) : -Math.sin(phi))));
    origin = applyA(M, stripPoint(b, T, s0, f, 0));
  }
  return { kind: 'plane', origin, normal: unit(normal) ?? v3(0, 0, 1), area: len * T, perimeter: 2 * (len + T), label: 'Seitenfläche' };
}

export function bendEntity(part: Part, bendId: string, surface: 0 | 1 | 2, hitPoint: V3, flat: boolean): MeasureEntity {
  const b = part.bends.get(bendId);
  if (!b) throw new Error('Die gewählte Biegung existiert nicht mehr. Bitte neu auswählen.');
  const M = placementOf(part, b.faceA, flat);
  const T = part.T;
  const R = b.radius;
  const dir3 = v3(b.dir.x, b.dir.y, 0);
  // closed corners mitre the strip ends; length and axis are taken at mid-bend
  const [wl, wr] = stripRange(b, 0.5);
  const L = wr - wl;
  if (surface === 2) {
    const q = applyA(invertRigid(M), hitPoint);
    const w = dot2(sub2(v2(q.x, q.y), b.origin), b.dir);
    const f = hitFraction(b, T, q, flat);
    const [hl, hr] = stripRange(b, f);
    // a side wall either closes a strip end (w at hl or hr) or runs along a free strip edge (f at 0 or 1)
    const off = dot2(sub2(v2(q.x, q.y), b.origin), b.nrm);
    const across = flat ? b.ba : b.angle * Math.hypot(off, b.up ? T + R - q.z : q.z + R);
    if (Math.min(f, 1 - f) * across < Math.min(Math.abs(w - hl), Math.abs(w - hr))) return stripEdgeWall(part, b, M, f < 0.5 ? 0 : 1, w, flat);
    const atStart = Math.abs(w - hl) <= Math.abs(w - hr);
    // a profiled end (closed corner, mitre, a bend cut back at a corner) is curved unless its offset is the same everywhere
    const profile = atStart ? b.ext0 : b.ext1;
    const offset = profile?.pieces.every((q) => q.b === 0 && q.c === 0 && q.a === profile.pieces[0].a) ? profile.pieces[0].a : profile ? null : 0;
    if (offset === null) throw new Error('Die Stirnseite dieser Biegung ist gekrümmt. Bitte eine Kante oder die Biegefläche wählen.');
    const end = atStart ? b.w0 - offset : b.w1 + offset;
    const normal = unit(applyDirA(M, atStart ? scale3(dir3, -1) : dir3)) ?? v3(0, 0, 1);
    if (flat) {
      const o = stripFlat(b, end, 0);
      return { kind: 'plane', origin: applyA(M, v3(o.x, o.y, 0)), normal, area: b.ba * T, perimeter: 2 * (b.ba + T), label: 'Biegung' };
    }
    return {
      kind: 'plane',
      origin: applyA(M, stripPoint(b, T, end, 0, 0)),
      normal,
      area: (b.angle / 2) * ((R + T) ** 2 - R ** 2),
      perimeter: b.angle * (2 * R + T) + 2 * T,
      label: 'Biegung',
    };
  }
  const top = surface === 0;
  if (flat) {
    const o = stripFlat(b, b.w0, 0);
    const n = faceNormal(M);
    const outline = stripOutline(b);
    return {
      kind: 'plane',
      origin: applyA(M, v3(o.x, o.y, top ? T : 0)),
      normal: top ? n : scale3(n, -1),
      area: Math.abs(signedArea(outline)),
      perimeter: outline.reduce((s, p, i) => s + dist2(p, outline[(i + 1) % outline.length]), 0),
      label: 'Biegung',
    };
  }
  // axis as in foldTransform; the concave side of the bend has the inner radius R
  const axis = v3(b.origin.x + b.dir.x * wl, b.origin.y + b.dir.y * wl, b.up ? T + R : -R);
  return {
    kind: 'cylinder',
    axisPoint: applyA(M, axis),
    axisDir: unit(applyDirA(M, dir3)) ?? v3(1, 0, 0),
    radius: b.up === top ? R : R + T,
    angleDeg: deg(b.angle),
    length: L,
    area: (b.up === top ? R : R + T) * sweptWidth(b),
    label: 'Biegung',
  };
}
