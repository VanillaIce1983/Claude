// STEP AP214 export of the folded part as an exact boundary representation.
//
// The solid mirrors buildMesh(): the top sheet surface (h = T) is a set of
// planar faces and cylindrical bend strips glued along their seams, the
// bottom surface (h = 0) repeats that topology, and every free edge of the
// top surface gets a side wall through the thickness. Round holes keep their
// true circles; every other outline segment stays a straight line. Mitred
// strip ends (closed corners) are exact too: each end piece is a rational
// quadratic B-spline on both bend cylinders, joined by a ruled side wall.

import { bendBreakpoints, rowBreakpoints, seamSplitOuter } from '../mesh';
import { Bend, EndPiece, Face, Part, StripEnd, computePlacements, stripFlat, stripPoint, stripRange } from '../model';
import type { Ring } from '../polygon';
import { Affine, V2, V3, add3, applyA, applyDirA, cross2, cross3, determinantA, dist2, dot3, norm3, rotCW, scale3, sub2, sub3, v3 } from '../vec';
import { exportPart } from './prepare';

export interface StepOptions {
  /** Product name written into the file. */
  name: string;
  /** ISO 8601 timestamp for the file header. */
  timestamp: string;
}

/**
 * Rational quadratic B-spline curve with `spans` arcs of equal angle, knots
 * 0..spans: the exact image of a circular arc under an affine map. `top` and
 * `bot` are the control points on the two sheet surfaces.
 */
interface Spline {
  top: V3[];
  bot: V3[];
  weights: number[];
  spans: number;
}

type Surf =
  | { kind: 'plane'; o: V3; n: V3; ref: V3 }
  /** sense: the outward normal points away from the axis. */
  | { kind: 'cyl'; o: V3; axis: V3; ref: V3; r: number; sense: boolean }
  /** Ruled surface from the top to the bottom curve of a spline edge; sense: its normal points outward. */
  | { kind: 'ruled'; spline: Spline; sense: boolean };

/** Circular edge, counter-clockwise about `axis` from its start to its end vertex. */
interface Arc {
  cTop: V3;
  cBot: V3;
  axis: V3;
  ref: V3;
  rTop: number;
  rBot: number;
}

/** Edge of the top surface; its twin on the bottom surface has the same topology. */
interface Edge {
  a: number;
  b: number;
  arc: Arc | null;
  spline?: Spline;
}

/** An edge in a top-surface loop, plus the side wall it gets if it stays free. */
interface Use {
  e: number;
  fwd: boolean;
  wall: Surf;
}

/** Planar face or bend strip: top surface, bottom surface, loops (outer first, CCW seen from the top). */
interface Sheet {
  top: Surf;
  bot: Surf;
  loops: Use[][];
}

interface Brep {
  /** Flat-pattern vertices at both heights. */
  verts: { top: V3; bot: V3 }[];
  edges: Edge[];
  lineKeys: Map<string, number>;
  sheets: Sheet[];
}

interface FaceVertex {
  p: V2;
  v: number;
}

const Z = v3(0, 0, 1);
const neg = (a: V3): V3 => v3(-a.x, -a.y, -a.z);
const flat3 = (a: V2): V3 => v3(a.x, a.y, 0);

function addVertex(B: Brep, top: V3, bot: V3): number {
  return B.verts.push({ top, bot }) - 1;
}

/** Straight edges are shared by vertex pair, so a seam edge of a face and of its strip become one edge. */
function lineUse(B: Brep, a: number, b: number, wall: Surf): Use {
  const key = a < b ? `${a},${b}` : `${b},${a}`;
  let e = B.lineKeys.get(key);
  if (e === undefined) {
    e = B.edges.push({ a, b, arc: null }) - 1;
    B.lineKeys.set(key, e);
  }
  return { e, fwd: B.edges[e].a === a, wall };
}

function arcEdge(B: Brep, a: number, b: number, arc: Arc): number {
  return B.edges.push({ a, b, arc }) - 1;
}

function splineEdge(B: Brep, a: number, b: number, spline: Spline): number {
  return B.edges.push({ a, b, arc: null, spline }) - 1;
}

/** Side wall of a straight top edge a → b with the given outward normal. */
function lineWall(B: Brep, a: number, b: number, n: V3): Surf {
  return { kind: 'plane', o: B.verts[a].top, n: norm3(n), ref: sub3(B.verts[b].top, B.verts[a].top) };
}

function findFaceVertex(list: FaceVertex[] | undefined, p: V2): number | null {
  let best: number | null = null;
  let bd = 1e-5;
  for (const fv of list ?? []) {
    const d = dist2(fv.p, p);
    if (d < bd) {
      bd = d;
      best = fv.v;
    }
  }
  return best;
}

function circleOf(part: Part, ring: Ring): { c: V2; r: number } | undefined {
  const tag = ring.tags[0];
  return ring.tags.every((t) => t === tag) ? part.circles.get(tag) : undefined;
}

function ringLoop(B: Brep, ring: Ring, M: Affine, T: number, list: FaceVertex[]): Use[] {
  const ids = ring.pts.map((p) => {
    const v = addVertex(B, applyA(M, v3(p.x, p.y, T)), applyA(M, v3(p.x, p.y, 0)));
    list.push({ p, v });
    return v;
  });
  const n = ids.length;
  return ids.map((a, i) => {
    const b = ids[(i + 1) % n];
    const out = applyDirA(M, flat3(rotCW(sub2(ring.pts[(i + 1) % n], ring.pts[i]))));
    return lineUse(B, a, b, lineWall(B, a, b, out));
  });
}

/** A round hole as two half circles; the loop runs clockwise in flat like the hole ring. */
function holeLoop(B: Brep, circle: { c: V2; r: number }, M: Affine, T: number): Use[] {
  const { c, r } = circle;
  const ex = applyDirA(M, v3(1, 0, 0));
  const ey = applyDirA(M, v3(0, 1, 0));
  const at = (x: number, h: number): V3 => applyA(M, v3(c.x + x, c.y, h));
  const p0 = addVertex(B, at(r, T), at(r, 0));
  const p1 = addVertex(B, at(-r, T), at(-r, 0));
  const arc: Arc = { cTop: at(0, T), cBot: at(0, 0), axis: norm3(cross3(ex, ey)), ref: norm3(ex), rTop: r, rBot: r };
  const wall: Surf = { kind: 'cyl', o: arc.cBot, axis: arc.axis, ref: arc.ref, r, sense: false };
  const e1 = arcEdge(B, p0, p1, arc);
  const e2 = arcEdge(B, p1, p0, arc);
  return [
    { e: e2, fwd: false, wall },
    { e: e1, fwd: false, wall },
  ];
}

function addFace(B: Brep, part: Part, face: Face, M: Affine, breaks: Map<string, number[]>, list: FaceVertex[]): void {
  const T = part.T;
  const up = norm3(applyDirA(M, Z));
  const ref = applyDirA(M, v3(1, 0, 0));
  const loops = [seamSplitOuter(part, face, breaks), ...face.holes].map((ring) => {
    const circle = circleOf(part, ring);
    return circle ? holeLoop(B, circle, M, T) : ringLoop(B, ring, M, T, list);
  });
  B.sheets.push({
    top: { kind: 'plane', o: applyA(M, v3(0, 0, T)), n: up, ref },
    bot: { kind: 'plane', o: applyA(M, v3(0, 0, 0)), n: neg(up), ref },
    loops,
  });
}

/** Unit tangent across the strip (direction of growing f) at fraction f, in face A's frame. */
function stripTangent(b: Bend, f: number): V3 {
  const phi = b.angle * f;
  const s = b.up ? Math.sin(phi) : -Math.sin(phi);
  return v3(b.nrm.x * Math.cos(phi), b.nrm.y * Math.cos(phi), s);
}

const isFlat = (p: EndPiece): boolean => Math.abs(p.b) < 1e-12 && Math.abs(p.c) < 1e-12;
const PLAIN_END: StripEnd = { pieces: [{ f0: 0, f1: 1, a: 0, b: 0, c: 0 }] };

/**
 * Control points of an end piece on the fibre at height h. A strip point is
 * affine in (cos φ, sin φ) once the end offset a + b·sin φ + c·cos φ is put in,
 * so the curve is the image of the unit circle arc's control points.
 */
function endSpline(b: Bend, MA: Affine, T: number, wEnd: number, s: number, p: EndPiece): { spline: Spline; tangent: (h: number) => V3; mid: (h: number) => V3 } {
  const phi0 = b.angle * p.f0;
  const phi1 = b.angle * p.f1;
  const spans = Math.max(1, Math.ceil((phi1 - phi0) / (Math.PI / 2) - 1e-9));
  const half = (phi1 - phi0) / spans / 2;
  const unit: [number, number][] = [[Math.cos(phi0), Math.sin(phi0)]];
  const weights = [1];
  for (let k = 0; k < spans; k++) {
    const m = phi0 + half * (2 * k + 1);
    unit.push([Math.cos(m) / Math.cos(half), Math.sin(m) / Math.cos(half)], [Math.cos(m + half), Math.sin(m + half)]);
    weights.push(Math.cos(half), 1);
  }
  const at = (phi: number, h: number): V3 =>
    applyA(MA, stripPoint(b, T, wEnd + s * (p.a + p.b * Math.sin(phi) + p.c * Math.cos(phi)), phi / b.angle, h));
  const affine = (h: number): { o: V3; c: V3; si: V3 } => {
    const p0 = at(0, h);
    const p180 = at(Math.PI, h);
    const o = scale3(add3(p0, p180), 0.5);
    return { o, c: scale3(sub3(p0, p180), 0.5), si: sub3(at(Math.PI / 2, h), o) };
  };
  const fibre = (h: number): V3[] => {
    const { o, c, si } = affine(h);
    return unit.map(([x, y]) => add3(o, add3(scale3(c, x), scale3(si, y))));
  };
  const mid = (phi0 + phi1) / 2;
  return {
    spline: { top: fibre(T), bot: fibre(0), weights, spans },
    tangent: (h) => {
      const { c, si } = affine(h);
      return add3(scale3(c, -Math.sin(mid)), scale3(si, Math.cos(mid)));
    },
    mid: (h) => at(mid, h),
  };
}

function addStrip(B: Brep, part: Part, b: Bend, MA: Affine, ws: number[], faceVerts: Map<string, FaceVertex[]>): void {
  const T = part.T;
  const R = b.radius;
  const zc = b.up ? T + R : -R;
  const center = (w: number): V3 => applyA(MA, v3(b.origin.x + b.dir.x * w, b.origin.y + b.dir.y * w, zc));
  const ref = norm3(applyDirA(MA, b.up ? neg(Z) : Z));
  const axis = norm3(cross3(ref, applyDirA(MA, flat3(b.nrm))));
  const rTop = b.up ? R : R + T;
  const rBot = b.up ? R + T : R;
  const newVertex = (w: number, f: number): number => addVertex(B, applyA(MA, stripPoint(b, T, w, f, T)), applyA(MA, stripPoint(b, T, w, f, 0)));
  const vertexAt = (w: number, f: 0 | 1): number =>
    findFaceVertex(faceVerts.get((f === 0 ? b.faceA : b.faceB) ?? ''), stripFlat(b, w, f)) ?? newVertex(w, f);
  // the two seams can differ in extent when the ends are mitred
  const v0 = rowBreakpoints(b, ws, 0).map((w) => vertexAt(w, 0));
  const v1 = rowBreakpoints(b, ws, 1).map((w) => vertexAt(w, 1));
  const out0 = applyDirA(MA, neg(flat3(b.nrm)));
  const out1 = applyDirA(MA, stripTangent(b, 1));
  const along = applyDirA(MA, flat3(b.dir));

  /** Side edges of one strip end from its f = 0 vertex to its f = 1 vertex, one per end piece. */
  const endUses = (end: StripEnd | undefined, s: 1 | -1, wEnd: number, from: number, to: number): Use[] => {
    const outward = s > 0 ? along : neg(along);
    const pieces = end?.pieces.length ? end.pieces : PLAIN_END.pieces;
    const uses: Use[] = [];
    let prev = from;
    pieces.forEach((p, i) => {
      const next = i === pieces.length - 1 ? to : newVertex(stripRange(b, p.f1)[s > 0 ? 1 : 0], p.f1);
      if (isFlat(p)) {
        const c = center(wEnd + s * p.a);
        const arc: Arc = { cTop: c, cBot: c, axis, ref, rTop, rBot };
        uses.push({ e: arcEdge(B, prev, next, arc), fwd: true, wall: { kind: 'plane', o: c, n: norm3(outward), ref } });
      } else {
        const { spline, tangent, mid } = endSpline(b, MA, T, wEnd, s, p);
        // surface parameters run along the curve (u) and from the top to the bottom fibre (v)
        const normal = cross3(tangent(T), sub3(mid(0), mid(T)));
        uses.push({ e: splineEdge(B, prev, next, spline), fwd: true, wall: { kind: 'ruled', spline, sense: dot3(normal, outward) > 0 } });
      }
      prev = next;
    });
    return uses;
  };

  const k0 = v0.length - 1;
  const k1 = v1.length - 1;
  // counter-clockwise in flat when (dir, nrm) is a counter-clockwise frame
  const loop: Use[] = [];
  for (let i = 0; i < k0; i++) loop.push(lineUse(B, v0[i], v0[i + 1], lineWall(B, v0[i], v0[i + 1], out0)));
  loop.push(...endUses(b.ext1, 1, b.w1, v0[k0], v1[k1]));
  for (let i = k1; i > 0; i--) loop.push(lineUse(B, v1[i], v1[i - 1], lineWall(B, v1[i], v1[i - 1], out1)));
  loop.push(...reverseLoop(endUses(b.ext0, -1, b.w0, v0[0], v1[0])));
  const o = center(b.w0);
  B.sheets.push({
    top: { kind: 'cyl', o, axis, ref, r: rTop, sense: !b.up },
    bot: { kind: 'cyl', o, axis, ref, r: rBot, sense: b.up },
    loops: [cross2(b.dir, b.nrm) > 0 ? loop : reverseLoop(loop)],
  });
}

function reverseLoop(loop: Use[]): Use[] {
  return loop.map((u) => ({ ...u, fwd: !u.fwd })).reverse();
}

function buildBrep(part: Part): Brep {
  const B: Brep = { verts: [], edges: [], lineKeys: new Map(), sheets: [] };
  const placements = computePlacements(part);
  const breaks = bendBreakpoints(part);
  const faceVerts = new Map<string, FaceVertex[]>();
  for (const face of part.faces.values()) {
    const M = placements.get(face.id);
    if (!M) continue;
    const list: FaceVertex[] = [];
    addFace(B, part, face, M, breaks, list);
    faceVerts.set(face.id, list);
  }
  for (const b of part.bends.values()) {
    const MA = placements.get(b.faceA);
    if (MA) addStrip(B, part, b, MA, breaks.get(b.id)!, faceVerts);
  }
  return B;
}

const startOf = (B: Brep, u: Use): number => (u.fwd ? B.edges[u.e].a : B.edges[u.e].b);
const endOf = (B: Brep, u: Use): number => (u.fwd ? B.edges[u.e].b : B.edges[u.e].a);

/** Top-surface edges used only once, chained: successor of each free edge along the outline. */
function freeOutline(B: Brep): { free: Use[]; next: Map<Use, Use> } {
  const count = new Array<number>(B.edges.length).fill(0);
  for (const s of B.sheets) for (const l of s.loops) for (const u of l) count[u.e]++;
  if (count.some((c) => c > 2)) throw new Error('STEP-Export: Das Teil ist nicht mannigfaltig (Kante von mehr als zwei Flächen benutzt).');
  const free = B.sheets.flatMap((s) => s.loops.flat()).filter((u) => count[u.e] === 1);
  const byStart = new Map<number, Use>();
  for (const u of free) {
    if (byStart.has(startOf(B, u))) throw new Error('STEP-Export: Das Teil berührt sich in einem Punkt und ist kein einfacher Volumenkörper.');
    byStart.set(startOf(B, u), u);
  }
  const next = new Map<Use, Use>();
  for (const u of free) {
    const n = byStart.get(endOf(B, u));
    if (!n) throw new Error('STEP-Export: Die Außenkontur ist nicht geschlossen.');
    next.set(u, n);
  }
  return { free, next };
}

const curved = (B: Brep, u: Use): boolean => !!(B.edges[u.e].arc || B.edges[u.e].spline);

/**
 * Whether free edge `n` continues the straight side wall of `s` on the same
 * sheet piece, so both share one side face. Bend end faces and hole walls stay
 * separate, which also keeps touching geometry (teardrop tails) out of one loop.
 */
function continuesWall(B: Brep, piece: Map<Use, number>, s: Use, n: Use): boolean {
  if (piece.get(s) !== piece.get(n) || curved(B, s) || curved(B, n)) return false;
  if (s.wall.kind !== 'plane' || n.wall.kind !== 'plane' || dot3(s.wall.n, n.wall.n) < 1 - 1e-12) return false;
  const { o, n: normal } = s.wall;
  const v = B.verts[endOf(B, n)];
  return [v.top, v.bot].every((p) => Math.abs(dot3(sub3(p, o), normal)) < 1e-7);
}

/** Runs of free edges, each becoming one side wall. */
function wallRuns(B: Brep): Use[][] {
  const { free, next } = freeOutline(B);
  const piece = new Map<Use, number>();
  B.sheets.forEach((sh, i) => sh.loops.forEach((l) => l.forEach((u) => piece.set(u, i))));
  const prev = new Map<Use, Use>();
  for (const [u, n] of next) prev.set(n, u);
  const starts = free.filter((u) => !continuesWall(B, piece, prev.get(u)!, u));
  const runs: Use[][] = [];
  const done = new Set<Use>();
  for (const s of [...starts, ...free]) {
    if (done.has(s)) continue;
    const run = [s];
    done.add(s);
    for (let n = next.get(s)!; !done.has(n) && continuesWall(B, piece, s, n); n = next.get(n)!) {
      run.push(n);
      done.add(n);
    }
    runs.push(run);
  }
  return runs;
}

// ---------------------------------------------------------------- Part 21 text

/** REAL with up to 10 significant digits and a decimal point. */
export function stepReal(v: number): string {
  if (!Number.isFinite(v)) throw new Error('STEP-Export: ungültige Zahl in der Geometrie.');
  if (Math.abs(v) < 1e-10) return '0.';
  // 15 digits keep the declared 1e-6 mm uncertainty even far from the origin
  const s = Number(v.toPrecision(15)).toString();
  const [m, e] = s.split('e');
  const mant = m.includes('.') ? m : `${m}.`;
  return e === undefined ? mant : `${mant}E${e.replace('+', '')}`;
}

/** STRING literal: apostrophes and backslashes doubled, everything outside printable ASCII as \X2\ / \X4\. */
export function stepString(s: string): string {
  let out = '';
  let wide = '';
  const flush = (): void => {
    if (wide) out += `\\X2\\${wide}\\X0\\`;
    wide = '';
  };
  for (const ch of s) {
    const cp = ch.codePointAt(0)!;
    if (cp < 0x20) {
      flush();
      out += ' ';
    } else if (cp <= 0x7e) {
      flush();
      out += ch === "'" ? "''" : ch === '\\' ? '\\\\' : ch;
    } else if (cp <= 0xffff) {
      wide += cp.toString(16).toUpperCase().padStart(4, '0');
    } else {
      flush();
      out += `\\X4\\${cp.toString(16).toUpperCase().padStart(8, '0')}\\X0\\`;
    }
  }
  flush();
  return `'${out}'`;
}

class StepWriter {
  readonly lines: string[] = [];

  add(entity: string): number {
    this.lines.push(`#${this.lines.length + 1}=${entity};`);
    return this.lines.length;
  }

  point(p: V3): number {
    return this.add(`CARTESIAN_POINT('',(${stepReal(p.x)},${stepReal(p.y)},${stepReal(p.z)}))`);
  }

  dir(d: V3): number {
    const n = norm3(d);
    return this.add(`DIRECTION('',(${stepReal(n.x)},${stepReal(n.y)},${stepReal(n.z)}))`);
  }

  placement(o: V3, axis: V3, ref: V3): number {
    const z = norm3(axis);
    const x = sub3(ref, scale3(z, dot3(ref, z)));
    return this.add(`AXIS2_PLACEMENT_3D('',#${this.point(o)},#${this.dir(z)},#${this.dir(x)})`);
  }
}

interface Ids {
  vTop: number[];
  vBot: number[];
  eTop: number[];
  eBot: number[];
  vertical: Map<number, number>;
}

function writeLine(w: StepWriter, pa: V3, pb: V3): number {
  return w.add(`LINE('',#${w.point(pa)},#${w.add(`VECTOR('',#${w.dir(sub3(pb, pa))},1.)`)})`);
}

/** Knot vector of a Spline: 0..spans, end knots tripled, inner ones doubled (degree 2). */
function splineKnots(sp: Spline): { mults: string; knots: string } {
  const mults = [3, ...Array.from({ length: sp.spans - 1 }, () => 2), 3];
  return { mults: mults.join(','), knots: Array.from({ length: sp.spans + 1 }, (_, i) => stepReal(i)).join(',') };
}

function writeSpline(w: StepWriter, sp: Spline, top: boolean): number {
  const pts = (top ? sp.top : sp.bot).map((p) => `#${w.point(p)}`).join(',');
  const { mults, knots } = splineKnots(sp);
  return w.add(
    `(BOUNDED_CURVE()B_SPLINE_CURVE(2,(${pts}),.UNSPECIFIED.,.F.,.F.)B_SPLINE_CURVE_WITH_KNOTS((${mults}),(${knots}),.UNSPECIFIED.)` +
      `CURVE()GEOMETRIC_REPRESENTATION_ITEM()RATIONAL_B_SPLINE_CURVE((${sp.weights.map(stepReal).join(',')}))REPRESENTATION_ITEM(''))`,
  );
}

function writeRuled(w: StepWriter, sp: Spline): number {
  const rows = sp.top.map((p, i) => `(#${w.point(p)},#${w.point(sp.bot[i])})`).join(',');
  const weights = sp.weights.map((x) => `(${stepReal(x)},${stepReal(x)})`).join(',');
  const { mults, knots } = splineKnots(sp);
  return w.add(
    `(BOUNDED_SURFACE()B_SPLINE_SURFACE(2,1,(${rows}),.UNSPECIFIED.,.F.,.F.,.F.)` +
      `B_SPLINE_SURFACE_WITH_KNOTS((${mults}),(2,2),(${knots}),(0.,1.),.UNSPECIFIED.)` +
      `GEOMETRIC_REPRESENTATION_ITEM()RATIONAL_B_SPLINE_SURFACE((${weights}))REPRESENTATION_ITEM('')SURFACE())`,
  );
}

function writeCircle(w: StepWriter, arc: Arc, top: boolean): number {
  const c = top ? arc.cTop : arc.cBot;
  return w.add(`CIRCLE('',#${w.placement(c, arc.axis, arc.ref)},${stepReal(top ? arc.rTop : arc.rBot)})`);
}

/** Edge curves of the top (h = T) or bottom (h = 0) sheet surface. */
function writeEdges(w: StepWriter, B: Brep, vIds: number[], top: boolean): number[] {
  // both half arcs of a round hole share one circle
  const circles = new Map<Arc, number>();
  const at = (v: number): V3 => (top ? B.verts[v].top : B.verts[v].bot);
  return B.edges.map((e) => {
    let curve: number;
    if (e.arc) {
      curve = circles.get(e.arc) ?? writeCircle(w, e.arc, top);
      circles.set(e.arc, curve);
    } else if (e.spline) {
      curve = writeSpline(w, e.spline, top);
    } else {
      curve = writeLine(w, at(e.a), at(e.b));
    }
    return w.add(`EDGE_CURVE('',#${vIds[e.a]},#${vIds[e.b]},#${curve},.T.)`);
  });
}

function writeTopology(w: StepWriter, B: Brep): Ids {
  const vTop = B.verts.map((v) => w.add(`VERTEX_POINT('',#${w.point(v.top)})`));
  const vBot = B.verts.map((v) => w.add(`VERTEX_POINT('',#${w.point(v.bot)})`));
  return { vTop, vBot, eTop: writeEdges(w, B, vTop, true), eBot: writeEdges(w, B, vBot, false), vertical: new Map() };
}

function verticalEdge(w: StepWriter, B: Brep, ids: Ids, v: number): number {
  let id = ids.vertical.get(v);
  if (id === undefined) {
    const { top, bot } = B.verts[v];
    id = w.add(`EDGE_CURVE('',#${ids.vTop[v]},#${ids.vBot[v]},#${writeLine(w, top, bot)},.T.)`);
    ids.vertical.set(v, id);
  }
  return id;
}

type Oriented = [edge: number, sense: boolean];

function writeFace(w: StepWriter, surf: Surf, loops: Oriented[][], flip: boolean): number {
  const bounds = loops.map((loop, i) => {
    const l = flip ? loop.map(([e, s]): Oriented => [e, !s]).reverse() : loop;
    const oes = l.map(([e, s]) => `#${w.add(`ORIENTED_EDGE('',*,*,#${e},${s ? '.T.' : '.F.'})`)}`);
    const el = w.add(`EDGE_LOOP('',(${oes.join(',')}))`);
    return `#${w.add(`${i === 0 ? 'FACE_OUTER_BOUND' : 'FACE_BOUND'}('',#${el},.T.)`)}`;
  });
  const s =
    surf.kind === 'plane'
      ? w.add(`PLANE('',#${w.placement(surf.o, surf.n, surf.ref)})`)
      : surf.kind === 'cyl'
        ? w.add(`CYLINDRICAL_SURFACE('',#${w.placement(surf.o, surf.axis, surf.ref)},${stepReal(surf.r)})`)
        : writeRuled(w, surf.spline);
  const sense = surf.kind === 'plane' || surf.sense;
  return w.add(`ADVANCED_FACE('',(${bounds.join(',')}),#${s},${sense ? '.T.' : '.F.'})`);
}

/** All faces of the solid: top and bottom of every sheet piece, then the side walls. */
function writeFaces(w: StepWriter, B: Brep, flip: boolean): number[] {
  const ids = writeTopology(w, B);
  const faces: number[] = [];
  for (const s of B.sheets) {
    faces.push(writeFace(w, s.top, s.loops.map((l) => l.map((u): Oriented => [ids.eTop[u.e], u.fwd])), flip));
    const bottom = s.loops.map((l) => l.map((u): Oriented => [ids.eBot[u.e], !u.fwd]).reverse());
    faces.push(writeFace(w, s.bot, bottom, flip));
  }
  for (const run of wallRuns(B)) {
    const first = startOf(B, run[0]);
    const last = endOf(B, run[run.length - 1]);
    const loop: Oriented[] = [
      ...run.map((u): Oriented => [ids.eTop[u.e], !u.fwd]).reverse(),
      [verticalEdge(w, B, ids, first), true],
      ...run.map((u): Oriented => [ids.eBot[u.e], u.fwd]),
      [verticalEdge(w, B, ids, last), false],
    ];
    faces.push(writeFace(w, run[0].wall, [loop], flip));
  }
  return faces;
}

function writeProduct(w: StepWriter, name: string, shapeRep: number): void {
  const n = stepString(name);
  const appCtx = w.add(`APPLICATION_CONTEXT('core data for automotive mechanical design processes')`);
  w.add(`APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#${appCtx})`);
  const prodCtx = w.add(`PRODUCT_CONTEXT('',#${appCtx},'mechanical')`);
  const product = w.add(`PRODUCT(${n},${n},'',(#${prodCtx}))`);
  w.add(`PRODUCT_RELATED_PRODUCT_CATEGORY('part',$,(#${product}))`);
  const formation = w.add(`PRODUCT_DEFINITION_FORMATION('','',#${product})`);
  const defCtx = w.add(`PRODUCT_DEFINITION_CONTEXT('part definition',#${appCtx},'design')`);
  const def = w.add(`PRODUCT_DEFINITION('design','',#${formation},#${defCtx})`);
  const shape = w.add(`PRODUCT_DEFINITION_SHAPE('','',#${def})`);
  w.add(`SHAPE_DEFINITION_REPRESENTATION(#${shape},#${shapeRep})`);
}

function writeContext(w: StepWriter): number {
  const mm = w.add('(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.))');
  const radian = w.add('(NAMED_UNIT(*)PLANE_ANGLE_UNIT()SI_UNIT($,.RADIAN.))');
  const sr = w.add('(NAMED_UNIT(*)SI_UNIT($,.STERADIAN.)SOLID_ANGLE_UNIT())');
  const tol = w.add(`UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-06),#${mm},'distance_accuracy_value','confusion accuracy')`);
  return w.add(
    `(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#${tol}))` +
      `GLOBAL_UNIT_ASSIGNED_CONTEXT((#${mm},#${radian},#${sr}))REPRESENTATION_CONTEXT('Context #1','3D Context with UNIT and UNCERTAINTY'))`,
  );
}

/**
 * ISO 10303-21 text of the part as one manifold solid (mm). The part goes
 * through exportPart() first (idempotent), so zero-radius hems never touch.
 */
export function partToStep(part: Part, opts: StepOptions): string {
  const B = buildBrep(exportPart(part));
  const flip = determinantA(part.anchor.M) < 0;
  const name = stepString(opts.name);
  const w = new StepWriter();
  const ctx = writeContext(w);
  const origin = w.placement(v3(0, 0, 0), Z, v3(1, 0, 0));
  const shell = w.add(`CLOSED_SHELL('',(${writeFaces(w, B, flip).map((f) => `#${f}`).join(',')}))`);
  const solid = w.add(`MANIFOLD_SOLID_BREP(${name},#${shell})`);
  writeProduct(w, opts.name, w.add(`ADVANCED_BREP_SHAPE_REPRESENTATION(${name},(#${solid},#${origin}),#${ctx})`));
  return [
    'ISO-10303-21;',
    'HEADER;',
    `FILE_DESCRIPTION(('UnsolidWorks sheet metal part'),'2;1');`,
    `FILE_NAME(${name},${stepString(opts.timestamp)},('UnsolidWorks'),(''),'UnsolidWorks','UnsolidWorks','');`,
    `FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));`,
    'ENDSEC;',
    'DATA;',
    ...w.lines,
    'ENDSEC;',
    'END-ISO-10303-21;',
    '',
  ].join('\n');
}
