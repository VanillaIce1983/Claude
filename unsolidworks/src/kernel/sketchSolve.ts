// Smart dimensions of a face sketch, as in SolidWorks: a dimension measures
// between sketch points and lines, or from them to corners and edges of the
// part (on any face, projected onto the sketch plane), and drives the sketch.
// A small least-change solver moves the entity parameters until every
// dimension holds; geometry that no dimension fixes stays where it is. Run on
// every rebuild, the dimensions to the part make the sketch follow the part.

import { Part, computePlacements } from './model';
import { DimTarget, FaceSketch, ModelLineRef, ModelPointRef, SketchDim, SketchEntity, SketchFrame, flatToSketch, sketchFrame } from './sketch';
import { Affine, V2, applyA, cross2, dot2, invertRigid, norm2, rotCCW, scale2, sub2, add2, v2, v3 } from './vec';

// ---------------------------------------------------------------- entity parameters

/** Names of the parameters the solver may change (rotations stay as drawn). */
function paramNames(e: SketchEntity): string[] {
  switch (e.kind) {
    case 'rect':
      return ['cx', 'cy', 'w', 'h'];
    case 'circle':
      return ['cx', 'cy', 'r'];
    case 'slot':
      return ['x1', 'y1', 'x2', 'y2', 'width'];
    case 'polygon':
      return ['cx', 'cy', 'r'];
    case 'polyline':
      return e.points.flatMap((_, i) => [`x${i}`, `y${i}`]);
  }
}

function getParams(e: SketchEntity): number[] {
  if (e.kind === 'polyline') return e.points.flatMap((p) => [p.x, p.y]);
  const rec = e as unknown as Record<string, number>;
  return paramNames(e).map((n) => rec[n]);
}

function withParams(e: SketchEntity, v: number[]): SketchEntity {
  if (e.kind === 'polyline') return { ...e, points: e.points.map((_, i) => v2(v[2 * i], v[2 * i + 1])) };
  const out = { ...e } as unknown as Record<string, number>;
  paramNames(e).forEach((n, i) => (out[n] = v[i]));
  return out as unknown as SketchEntity;
}

/** Parameters that are sizes: they must stay positive. */
function sizeParams(e: SketchEntity): string[] {
  switch (e.kind) {
    case 'rect':
      return ['w', 'h'];
    case 'circle':
    case 'polygon':
      return ['r'];
    case 'slot':
      return ['width'];
    case 'polyline':
      return [];
  }
}

// ---------------------------------------------------------------- named points and lines

const rot = (p: V2, deg: number): V2 => {
  const a = (deg * Math.PI) / 180;
  return v2(p.x * Math.cos(a) - p.y * Math.sin(a), p.x * Math.sin(a) + p.y * Math.cos(a));
};

function vertices(e: SketchEntity): V2[] {
  switch (e.kind) {
    case 'rect':
      return [
        [-1, -1],
        [1, -1],
        [1, 1],
        [-1, 1],
      ].map(([sx, sy]) => add2(v2(e.cx, e.cy), rot(v2((sx * e.w) / 2, (sy * e.h) / 2), e.angle)));
    case 'polygon':
      return Array.from({ length: e.sides }, (_, i) => add2(v2(e.cx, e.cy), rot(v2(e.r, 0), e.angle + (360 * i) / e.sides)));
    case 'polyline':
      return e.points;
    default:
      return [];
  }
}

/** The named points of an entity, for snapping and dimensions. */
export function entityPoints(e: SketchEntity): { name: string; p: V2 }[] {
  switch (e.kind) {
    case 'rect':
      return [{ name: 'c', p: v2(e.cx, e.cy) }, ...vertices(e).map((p, i) => ({ name: `p${i}`, p }))];
    case 'circle':
      return [{ name: 'c', p: v2(e.cx, e.cy) }];
    case 'slot':
      return [
        { name: 'p1', p: v2(e.x1, e.y1) },
        { name: 'p2', p: v2(e.x2, e.y2) },
        { name: 'c', p: v2((e.x1 + e.x2) / 2, (e.y1 + e.y2) / 2) },
      ];
    case 'polygon':
      return [{ name: 'c', p: v2(e.cx, e.cy) }, ...vertices(e).map((p, i) => ({ name: `v${i}`, p }))];
    case 'polyline':
      return e.points.map((p, i) => ({ name: `v${i}`, p }));
  }
}

/** The named straight edges of an entity. */
export function entityLines(e: SketchEntity): { name: string; a: V2; b: V2 }[] {
  if (e.kind === 'slot') {
    const d = sub2(v2(e.x2, e.y2), v2(e.x1, e.y1));
    if (Math.hypot(d.x, d.y) < 1e-12) return [];
    const n = scale2(rotCCW(norm2(d)), e.width / 2);
    return [
      { name: 'e0', a: add2(v2(e.x1, e.y1), n), b: add2(v2(e.x2, e.y2), n) },
      { name: 'e1', a: sub2(v2(e.x2, e.y2), n), b: sub2(v2(e.x1, e.y1), n) },
    ];
  }
  const vs = vertices(e);
  if (vs.length < 2) return [];
  return vs.map((a, i) => ({ name: `e${i}`, a, b: vs[(i + 1) % vs.length] }));
}

// ---------------------------------------------------------------- part references

export interface SketchContext {
  part: Part;
  frame: SketchFrame;
  placements: Map<string, Affine>;
  /** From model space to the sketched face's own frame (flat x, y; z through the sheet). */
  toFace: Affine;
}

export function sketchContext(part: Part, sketch: FaceSketch): SketchContext {
  const frame = sketchFrame(part, sketch.edge, sketch.anchor);
  const placements = computePlacements(part);
  const M = placements.get(frame.face.id);
  if (!M) throw new Error('Die Skizzenfläche ist nicht mit dem Teil verbunden.');
  return { part, frame, placements, toFace: invertRigid(M) };
}

/** A part point in model space, projected onto the sketch plane, in sketch coordinates. */
function project(ctx: SketchContext, p: { x: number; y: number; z: number }): V2 {
  const l = applyA(ctx.toFace, p);
  return flatToSketch(ctx.frame, v2(l.x, l.y));
}

function modelPoint3(ctx: SketchContext, faceId: string, q: V2, side: 'top' | 'bottom'): { x: number; y: number; z: number } {
  const M = ctx.placements.get(faceId);
  if (!M) throw new Error('Eine bemaßte Ecke liegt auf einer Fläche, die nicht mehr mit dem Teil verbunden ist.');
  return applyA(M, v3(q.x, q.y, side === 'top' ? ctx.part.T : 0));
}

/** Where a corner of the part lies, in sketch coordinates. */
export function resolveModelPoint(ctx: SketchContext, ref: ModelPointRef): V2 {
  let best: { face: string; p: V2 } | null = null;
  let bd = Infinity;
  for (const face of ctx.part.faces.values()) {
    for (const r of [face.outer, ...face.holes]) {
      const n = r.pts.length;
      for (let i = 0; i < n; i++) {
        if (r.tags[(i - 1 + n) % n] !== ref.prevTag || r.tags[i] !== ref.nextTag) continue;
        const d = Math.hypot(r.pts[i].x - ref.at.x, r.pts[i].y - ref.at.y);
        if (d < bd) {
          bd = d;
          best = { face: face.id, p: r.pts[i] };
        }
      }
    }
  }
  if (!best) throw new Error('Eine Ecke, auf die die Skizze bemaßt ist, existiert nicht mehr. Bitte die Bemaßung löschen oder neu setzen.');
  return project(ctx, modelPoint3(ctx, best.face, best.p, ref.side));
}

/** Where an edge of the part lies, in sketch coordinates. */
export function resolveModelLine(ctx: SketchContext, ref: ModelLineRef): [V2, V2] {
  let best: { face: string; a: V2; b: V2 } | null = null;
  let bd = Infinity;
  for (const face of ctx.part.faces.values()) {
    for (const r of [face.outer, ...face.holes]) {
      const n = r.pts.length;
      for (let i = 0; i < n; i++) {
        if (r.tags[i] !== ref.tag) continue;
        const a = r.pts[i];
        const b = r.pts[(i + 1) % n];
        const ab = sub2(b, a);
        const l2 = dot2(ab, ab);
        const t = l2 > 0 ? Math.max(0, Math.min(1, dot2(sub2(ref.at, a), ab) / l2)) : 0;
        const d = Math.hypot(a.x + ab.x * t - ref.at.x, a.y + ab.y * t - ref.at.y);
        if (d < bd) {
          bd = d;
          best = { face: face.id, a, b };
        }
      }
    }
  }
  if (!best) throw new Error('Eine Kante, auf die die Skizze bemaßt ist, existiert nicht mehr. Bitte die Bemaßung löschen oder neu setzen.');
  return [project(ctx, modelPoint3(ctx, best.face, best.a, ref.side)), project(ctx, modelPoint3(ctx, best.face, best.b, ref.side))];
}

// ---------------------------------------------------------------- measuring

type Geom = { kind: 'point'; p: V2 } | { kind: 'line'; a: V2; b: V2 };

/** Part references resolved once per solve: they do not move while the sketch does. */
type Resolved = Map<DimTarget, Geom>;

function resolveTargets(ctx: SketchContext | null, dims: SketchDim[]): Resolved {
  const out: Resolved = new Map();
  for (const d of dims) {
    if (d.kind !== 'distance') continue;
    for (const t of [d.a, d.b]) {
      if (t.kind === 'modelPoint') {
        if (!ctx) throw new Error('Bemaßungen auf das Teil brauchen die Fläche der Skizze.');
        out.set(t, { kind: 'point', p: resolveModelPoint(ctx, t.ref) });
      } else if (t.kind === 'modelLine') {
        if (!ctx) throw new Error('Bemaßungen auf das Teil brauchen die Fläche der Skizze.');
        const [a, b] = resolveModelLine(ctx, t.ref);
        out.set(t, { kind: 'line', a, b });
      }
    }
  }
  return out;
}

function geomOf(t: DimTarget, ents: Map<string, SketchEntity>, resolved: Resolved): Geom {
  if (t.kind === 'modelPoint' || t.kind === 'modelLine') return resolved.get(t)!;
  const e = ents.get(t.ref.entity);
  if (!e) throw new Error('Eine Bemaßung bezieht sich auf ein gelöschtes Skizzenelement.');
  if (t.kind === 'point') {
    const p = entityPoints(e).find((x) => x.name === t.ref.point);
    if (!p) throw new Error('Eine Bemaßung bezieht sich auf einen Punkt, den es nicht mehr gibt.');
    return { kind: 'point', p: p.p };
  }
  const l = entityLines(e).find((x) => x.name === t.ref.line);
  if (!l) throw new Error('Eine Bemaßung bezieht sich auf eine Linie, die es nicht mehr gibt.');
  return { kind: 'line', a: l.a, b: l.b };
}

/** Signed perpendicular distance of p from the line through a and b. */
function lineDistance(p: V2, a: V2, b: V2): number {
  const d = norm2(sub2(b, a));
  return cross2(d, sub2(p, a));
}

/**
 * What a dimension measures right now. Point to point along x, along y or
 * true; point to line and between parallel lines perpendicular; sizes for
 * diameters. Signed where the side matters, so the solver keeps the side.
 */
export function measureDim(d: SketchDim, ents: Map<string, SketchEntity>, resolved: Resolved): number {
  if (d.kind === 'diameter') {
    const e = ents.get(d.entity);
    if (!e) throw new Error('Eine Bemaßung bezieht sich auf ein gelöschtes Skizzenelement.');
    if (e.kind === 'circle' || e.kind === 'polygon') return 2 * e.r;
    if (e.kind === 'slot') return e.width;
    throw new Error('Durchmesser lassen sich nur an Kreisen, Vielecken und Langlöchern bemaßen.');
  }
  const a = geomOf(d.a, ents, resolved);
  const b = geomOf(d.b, ents, resolved);
  if (a.kind === 'point' && b.kind === 'point') {
    const v = sub2(b.p, a.p);
    return d.orient === 'h' ? v.x : d.orient === 'v' ? v.y : Math.hypot(v.x, v.y);
  }
  if (a.kind === 'point' && b.kind === 'line') return lineDistance(a.p, b.a, b.b);
  if (a.kind === 'line' && b.kind === 'point') return lineDistance(b.p, a.a, a.b);
  const la = a as { a: V2; b: V2 };
  const lb = b as { a: V2; b: V2 };
  const da = norm2(sub2(la.b, la.a));
  const db = norm2(sub2(lb.b, lb.a));
  if (Math.abs(cross2(da, db)) > 1e-6) throw new Error('Zwei Linien lassen sich nur bemaßen, wenn sie parallel sind.');
  return lineDistance(scale2(add2(la.a, la.b), 0.5), lb.a, lb.b);
}

/** Whether the dimension measures an unsigned length (point to point, true distance, or a size). */
function unsigned(d: SketchDim, ents: Map<string, SketchEntity>, resolved: Resolved): boolean {
  if (d.kind === 'diameter') return true;
  return d.orient === 'aligned' && geomOf(d.a, ents, resolved).kind === 'point' && geomOf(d.b, ents, resolved).kind === 'point';
}

// ---------------------------------------------------------------- solving

export interface SolveResult {
  entities: SketchEntity[];
  ok: boolean;
  /** Why not, when ok is false. */
  problem?: string;
  /** Entities whose parameters the dimensions fix completely. */
  defined: Set<string>;
}

/** Solves A·x = b (n × n) by Gaussian elimination with partial pivoting; null when singular. */
function solveLinear(A: number[][], b: number[]): number[] | null {
  const n = b.length;
  const M = A.map((row, i) => [...row, b[i]]);
  for (let c = 0; c < n; c++) {
    let p = c;
    for (let r = c + 1; r < n; r++) if (Math.abs(M[r][c]) > Math.abs(M[p][c])) p = r;
    if (Math.abs(M[p][c]) < 1e-14) return null;
    [M[c], M[p]] = [M[p], M[c]];
    for (let r = c + 1; r < n; r++) {
      const f = M[r][c] / M[c][c];
      if (f === 0) continue;
      for (let k = c; k <= n; k++) M[r][k] -= f * M[c][k];
    }
  }
  const x = new Array<number>(n).fill(0);
  for (let r = n - 1; r >= 0; r--) {
    let s = M[r][n];
    for (let k = r + 1; k < n; k++) s -= M[r][k] * x[k];
    x[r] = s / M[r][r];
  }
  return x;
}

interface System {
  /** Parameter slots in the variable vector: entity index and position within getParams. */
  slots: { ent: number; k: number; name: string }[];
  x0: number[];
  build: (x: number[]) => Map<string, SketchEntity>;
}

/** The variables: the parameters `free` lets move; the others stay as they are. */
function system(entities: SketchEntity[], free: (e: SketchEntity, name: string) => boolean = () => true): System {
  const slots: System['slots'] = [];
  const x0: number[] = [];
  const base = entities.map(getParams);
  entities.forEach((e, i) => {
    paramNames(e).forEach((name, k) => {
      if (!free(e, name)) return;
      slots.push({ ent: i, k, name });
      x0.push(base[i][k]);
    });
  });
  const build = (x: number[]): Map<string, SketchEntity> => {
    const vals = base.map((v) => v.slice());
    slots.forEach((s, j) => (vals[s.ent][s.k] = x[j]));
    return new Map(entities.map((e, i) => [e.id, withParams(e, vals[i])]));
  };
  return { slots, x0, build };
}

function jacobian(f: (x: number[]) => number[], x: number[], r0: number[]): number[][] {
  const J = r0.map(() => new Array<number>(x.length).fill(0));
  for (let j = 0; j < x.length; j++) {
    const h = 1e-7 * Math.max(1, Math.abs(x[j]));
    const xp = x.slice();
    xp[j] += h;
    const xm = x.slice();
    xm[j] -= h;
    const rp = f(xp);
    const rm = f(xm);
    for (let i = 0; i < r0.length; i++) J[i][j] = (rp[i] - rm[i]) / (2 * h);
  }
  return J;
}

/** Orthonormal basis of the row space of J (modified Gram–Schmidt). */
function rowBasis(J: number[][], tol = 1e-9): number[][] {
  const basis: number[][] = [];
  for (const row of J) {
    const v = row.slice();
    for (const q of basis) {
      const d = v.reduce((s, x, i) => s + x * q[i], 0);
      for (let i = 0; i < v.length; i++) v[i] -= d * q[i];
    }
    const n = Math.hypot(...v);
    if (n > tol * Math.max(1, Math.hypot(...row))) basis.push(v.map((x) => x / n));
  }
  return basis;
}

const normOf = (r: number[]): number => Math.hypot(...r);

/**
 * Levenberg–Marquardt with least-change steps: Δ = D Jᵀ (J D Jᵀ + μ I)⁻¹ (−r),
 * D = diag(1/w²), so a parameter with a larger weight moves less.
 */
function leastChange(f: (x: number[]) => number[], x0: number[], weights: number[]): { x: number[]; r: number[] } {
  const D = weights.map((w) => 1 / (w * w));
  let x = x0.slice();
  let r = f(x);
  let mu = 1e-9;
  for (let iter = 0; iter < 100 && normOf(r) > 1e-10; iter++) {
    const J = jacobian(f, x, r);
    let improved = false;
    for (let tries = 0; tries < 12; tries++) {
      const A = J.map((ri, i) => J.map((rj, j) => ri.reduce((s, v, k) => s + v * D[k] * rj[k], 0) + (i === j ? mu : 0)));
      const y = solveLinear(A, r.map((v) => -v));
      if (!y) {
        mu = Math.max(mu * 10, 1e-9);
        continue;
      }
      const xn = x.map((v, k) => v + D[k] * J.reduce((s, row, i) => s + row[k] * y[i], 0));
      const rn = f(xn);
      if (normOf(rn) < normOf(r)) {
        x = xn;
        r = rn;
        mu = Math.max(mu / 10, 1e-12);
        improved = true;
        break;
      }
      mu = Math.max(mu * 10, 1e-9);
    }
    if (!improved) break;
  }
  return { x, r };
}

export interface SolveOptions {
  /**
   * Each signed measure keeps the side it has in this state (the last solved
   * one while the user drags or types) instead of the side it has in the
   * entities passed in.
   */
  sidesFrom?: SketchEntity[];
  /** Parameters that give way only when nothing else can: the entity being dragged, or the field being typed in (all its parameters without `params`). */
  sticky?: { entity: string; params?: string[] };
}

/**
 * Moves the entities as little as possible so every dimension holds. `ctx`
 * resolves dimensions to the part (null when there are none). Sizes stay
 * when moving things is enough: only if the dimensions cannot hold otherwise
 * do sizes change as well, so changing a position dimension moves a shape
 * instead of also stretching it. Returns the moved entities, whether all
 * dimensions hold, and which entities are fully defined.
 */
export function solveSketch(entities: SketchEntity[], dims: SketchDim[], ctx: SketchContext | null, opts: SolveOptions = {}): SolveResult {
  if (dims.length === 0) return { entities, ok: true, defined: new Set() };
  let resolved: Resolved;
  try {
    resolved = resolveTargets(ctx, dims);
  } catch (e) {
    return { entities, ok: false, problem: (e as Error).message, defined: new Set() };
  }
  const start = new Map(entities.map((e) => [e.id, e]));
  const ref = opts.sidesFrom ? new Map([...start, ...opts.sidesFrom.map((e) => [e.id, e] as const)]) : start;
  let signs: number[];
  try {
    signs = dims.map((d) => {
      if (unsigned(d, start, resolved)) return 1;
      const m = measureDim(d, ref, resolved);
      // exactly on the line: take the side it is on now
      return (Math.abs(m) > 1e-9 ? m : measureDim(d, start, resolved)) < 0 ? -1 : 1;
    });
  } catch (e) {
    return { entities, ok: false, problem: (e as Error).message, defined: new Set() };
  }
  const sticky = (e: SketchEntity, name: string): boolean => opts.sticky?.entity === e.id && (!opts.sticky.params || opts.sticky.params.includes(name));
  const scale = Math.max(1, ...dims.map((d) => Math.abs(d.value)));
  const attempt = (free: (e: SketchEntity, name: string) => boolean): { sys: System; x: number[]; r: number[] } | { error: string } => {
    const sys = system(entities, free);
    const residual = (x: number[]): number[] => {
      const ents = sys.build(x);
      return dims.map((d, i) => measureDim(d, ents, resolved) - signs[i] * d.value);
    };
    try {
      const weights = sys.slots.map((s) => (sticky(entities[s.ent], s.name) ? 10 : 1));
      return { sys, ...leastChange(residual, sys.x0, weights) };
    } catch (e) {
      return { error: (e as Error).message };
    }
  };
  const isSize = (e: SketchEntity, name: string): boolean => sizeParams(e).includes(name);
  // from the smallest change to the largest: move the others, then the held parameters, then resize
  const tries: ((e: SketchEntity, name: string) => boolean)[] = opts.sticky
    ? [(e, n) => !isSize(e, n) && !sticky(e, n), (e, n) => !isSize(e, n) || sticky(e, n), (e, n) => !sticky(e, n), () => true]
    : [(e, n) => !isSize(e, n), () => true];
  let res: ReturnType<typeof attempt> = { error: '' };
  for (const free of tries) {
    res = attempt(free);
    if (!('error' in res) && normOf(res.r) <= 1e-7 * scale) break;
  }
  if ('error' in res) return { entities, ok: false, problem: res.error, defined: new Set() };
  const ents = res.sys.build(res.x);
  const solved = entities.map((e) => ents.get(e.id)!);
  if (normOf(res.r) > 1e-7 * scale) {
    return { entities: solved, ok: false, problem: 'Die Maße widersprechen sich. Bitte eine Bemaßung löschen oder ändern.', defined: new Set() };
  }
  for (const e of solved) {
    const rec = e as unknown as Record<string, number>;
    if (sizeParams(e).some((n) => !(rec[n] > 1e-9))) {
      return { entities: solved, ok: false, problem: 'Mit diesen Maßen würde ein Skizzenelement keine Größe mehr haben.', defined: new Set() };
    }
  }
  // fully defined: every parameter of the entity lies in the row space of the dimension Jacobian
  const full = system(solved);
  const f = (x: number[]): number[] => {
    const m = full.build(x);
    return dims.map((d) => measureDim(d, m, resolved));
  };
  const basis = rowBasis(jacobian(f, full.x0, f(full.x0)));
  const determined = full.x0.map((_, k) => {
    let rest = 1;
    for (const q of basis) rest -= q[k] * q[k];
    return rest < 1e-6;
  });
  const defined = new Set<string>();
  solved.forEach((e, i) => {
    const mine = full.slots.map((s, k) => (s.ent === i ? k : -1)).filter((k) => k >= 0);
    if (mine.length > 0 && mine.every((k) => determined[k])) defined.add(e.id);
  });
  return { entities: solved, ok: true, defined };
}

/**
 * Whether one more dimension would over-define the sketch: what it measures
 * is already fixed by the others (so it could only agree or conflict).
 */
export function overDefines(entities: SketchEntity[], dims: SketchDim[], extra: SketchDim, ctx: SketchContext | null): boolean {
  const sys = system(entities);
  const all = [...dims, extra];
  const resolved = resolveTargets(ctx, all);
  const f = (x: number[]): number[] => {
    const ents = sys.build(x);
    return all.map((d) => measureDim(d, ents, resolved));
  };
  const J = jacobian(f, sys.x0, f(sys.x0));
  return rowBasis(J).length < rowBasis(J.slice(0, -1)).length + 1;
}

/** What a dimension measures now, as a positive value (the value it would get when added). */
export function dimValue(d: SketchDim, entities: SketchEntity[], ctx: SketchContext | null): number {
  const ents = new Map(entities.map((e) => [e.id, e]));
  return Math.abs(measureDim(d, ents, resolveTargets(ctx, [d])));
}

/** Entity ids a dimension depends on. */
export function dimEntities(d: SketchDim): string[] {
  if (d.kind === 'diameter') return [d.entity];
  return [d.a, d.b].flatMap((t) => (t.kind === 'point' || t.kind === 'line' ? [t.ref.entity] : []));
}

/** The sketch as the cut uses it: solved against the part when it has dimensions. */
export function solvedEntities(part: Part, sketch: FaceSketch): SketchEntity[] {
  if (!sketch.dims?.length) return sketch.entities;
  const res = solveSketch(sketch.entities, sketch.dims, sketchContext(part, sketch));
  if (!res.ok) throw new Error(res.problem ?? 'Die Skizze lässt sich mit diesen Maßen nicht lösen.');
  return res.entities;
}

/** Two points of the measured geometry, in sketch coordinates, for drawing the dimension. */
export function dimAnchors(d: SketchDim, ents: Map<string, SketchEntity>, ctx: SketchContext | null): { a: V2; b: V2; kind: 'points' | 'lines' | 'diameter' } {
  if (d.kind === 'diameter') {
    const e = ents.get(d.entity);
    if (!e) throw new Error('Gelöschtes Skizzenelement.');
    if (e.kind === 'slot') {
      const l = entityLines(e);
      return { a: scale2(add2(l[0].a, l[0].b), 0.5), b: scale2(add2(l[1].a, l[1].b), 0.5), kind: 'diameter' };
    }
    const c = v2((e as { cx: number }).cx, (e as { cy: number }).cy);
    const r = (e as { r: number }).r;
    return { a: sub2(c, v2(r, 0)), b: add2(c, v2(r, 0)), kind: 'diameter' };
  }
  const resolved = resolveTargets(ctx, [d]);
  const ga = geomOf(d.a, ents, resolved);
  const gb = geomOf(d.b, ents, resolved);
  const foot = (p: V2, a: V2, b: V2): V2 => {
    const u = norm2(sub2(b, a));
    return add2(a, scale2(u, dot2(sub2(p, a), u)));
  };
  if (ga.kind === 'point' && gb.kind === 'point') return { a: ga.p, b: gb.p, kind: 'points' };
  if (ga.kind === 'point' && gb.kind === 'line') return { a: ga.p, b: foot(ga.p, gb.a, gb.b), kind: 'lines' };
  if (ga.kind === 'line' && gb.kind === 'point') return { a: foot(gb.p, ga.a, ga.b), b: gb.p, kind: 'lines' };
  const la = ga as { a: V2; b: V2 };
  const lb = gb as { a: V2; b: V2 };
  const m = scale2(add2(la.a, la.b), 0.5);
  return { a: m, b: foot(m, lb.a, lb.b), kind: 'lines' };
}
