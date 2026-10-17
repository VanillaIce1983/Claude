// Builds a closed triangle mesh of the part (folded and flat state share one
// topology), plus the pickable edge list for the UI.
//
// Each face polygon and each bend strip is meshed separately. Pieces are
// glued only along bend seams, so cuts like tear reliefs stay open even
// where two pieces touch in the flat pattern. Boundary edges of the top
// surface get side walls; the same boundary loops are the flat pattern.

import earcut from 'earcut';
import { Bend, Face, Part, computePlacements, isSeamTag, stripFlat, stripFractions, stripPoint, stripRange, uniqFractions } from './model';
import { Ring, signedArea } from './polygon';
import { Affine, V2, V3, applyA, applyDirA, cross2, dist2, dot2, sub2, v3 } from './vec';

export type Surface = 0 | 1 | 2; // top, bottom, side

export interface MeshPiece {
  kind: 'face' | 'bend';
  id: string;
  feature: string;
}

export interface BoundaryEdge {
  a: number;
  b: number;
  tag: string;
  piece: number;
}

export interface PartMesh {
  /** Folded positions (x, y, z per vertex). */
  positions: Float64Array;
  /** Flat-pattern positions, laid in the plane of the fixed face. */
  flatPositions: Float64Array;
  /** Flat 2D coordinates per vertex. */
  flat2: Float64Array;
  /** Surface normal per vertex (top/bottom sheet surfaces), folded and flat. */
  normals: Float64Array;
  flatNormals: Float64Array;
  indices: Uint32Array;
  triPiece: Int32Array;
  triSurface: Uint8Array;
  pieces: MeshPiece[];
  /** Top-surface boundary edges (directed, material on the left in flat). */
  boundary: BoundaryEdge[];
  /** Model edges for display: vertex index pairs (outline top and bottom, sharp corners through the thickness). */
  edgeLines: Uint32Array;
  placements: Map<string, Affine>;
  warnings: string[];
}

const TOL = 1e-6;

interface VertexStore {
  pos: number[];
  flat: number[];
  flat2: number[];
  nrm: number[];
  flatNrm: number[];
}

function pushVertex(vs: VertexStore, p: V3, f: V3, f2: V2, n: V3, fn: V3): number {
  vs.pos.push(p.x, p.y, p.z);
  vs.flat.push(f.x, f.y, f.z);
  vs.flat2.push(f2.x, f2.y);
  vs.nrm.push(n.x, n.y, n.z);
  vs.flatNrm.push(fn.x, fn.y, fn.z);
  return vs.pos.length / 3 - 1;
}

/** Normal of the top sheet surface inside a bend strip, in face A's frame. */
function stripNormal(b: Bend, f: number): V3 {
  const phi = b.angle * f;
  const s = b.up ? -Math.sin(phi) : Math.sin(phi);
  return v3(b.nrm.x * s, b.nrm.y * s, Math.cos(phi));
}

const neg = (a: V3): V3 => v3(-a.x, -a.y, -a.z);

/** Points of a bend seam (in flat) that lie on the given side of the strip. */
function seamPoints(b: Bend, ws: number[], side: 0 | 1): V2[] {
  return ws.map((w) => stripFlat(b, w, side));
}

function onSeam(b: Bend, p: V2, side: 0 | 1): number | null {
  const rel = sub2(p, b.origin);
  const t = dot2(rel, b.nrm);
  const target = side === 0 ? 0 : b.ba;
  if (Math.abs(t - target) > 1e-5) return null;
  const w = dot2(rel, b.dir);
  const [wl, wr] = stripRange(b, side);
  if (w < wl - 1e-5 || w > wr + 1e-5) return null;
  return w;
}

function uniqSorted(values: number[]): number[] {
  const s = [...values].sort((a, b) => a - b);
  const out: number[] = [];
  for (const v of s) if (out.length === 0 || v - out[out.length - 1] > 1e-6) out.push(v);
  return out;
}

/** Insert extra collinear points into a ring's edges (used to avoid T-junctions). */
function augmentRing(ring: Ring, extra: V2[], allowedTag: (tag: string) => boolean): Ring {
  const pts: V2[] = [];
  const tags: string[] = [];
  const n = ring.pts.length;
  for (let i = 0; i < n; i++) {
    const a = ring.pts[i];
    const b = ring.pts[(i + 1) % n];
    const tag = ring.tags[i];
    pts.push(a);
    tags.push(tag);
    if (!allowedTag(tag)) continue;
    const ab = sub2(b, a);
    const l2 = dot2(ab, ab);
    if (l2 < TOL * TOL) continue;
    const ins: { t: number; p: V2 }[] = [];
    for (const p of extra) {
      const ap = sub2(p, a);
      const t = dot2(ap, ab) / l2;
      if (t <= 1e-9 || t >= 1 - 1e-9) continue;
      if (Math.abs(cross2(ab, ap)) / Math.sqrt(l2) > 1e-5) continue;
      if (dist2(p, a) < 1e-6 || dist2(p, b) < 1e-6) continue;
      ins.push({ t, p });
    }
    ins.sort((x, y) => x.t - y.t);
    let last = a;
    for (const { p } of ins) {
      if (dist2(p, last) < 1e-6) continue;
      pts.push(p);
      tags.push(tag);
      last = p;
    }
  }
  return { pts, tags };
}

/** Sum of the triangle areas: larger than the polygon's area when triangles overlap. */
function coveredArea(coords: number[], idx: number[]): number {
  let sum = 0;
  for (let i = 0; i < idx.length; i += 3) {
    const [a, b, c] = [2 * idx[i], 2 * idx[i + 1], 2 * idx[i + 2]];
    sum += Math.abs((coords[b] - coords[a]) * (coords[c + 1] - coords[a + 1]) - (coords[c] - coords[a]) * (coords[b + 1] - coords[a + 1])) / 2;
  }
  return sum;
}

/**
 * earcut can return overlapping triangles for some faces with several holes,
 * depending on where the outer ring starts. The covered area shows it, so
 * other start vertices are tried until the triangles tile the face exactly.
 */
function triangulate(coords: number[], holeIdx: number[]): number[] {
  const n = holeIdx.length ? holeIdx[0] : coords.length / 2;
  const ringArea = (from: number, to: number): number => {
    let a = 0;
    for (let i = from; i < to; i++) {
      const j = i + 1 < to ? i + 1 : from;
      a += coords[2 * i] * coords[2 * j + 1] - coords[2 * j] * coords[2 * i + 1];
    }
    return Math.abs(a) / 2;
  };
  const bounds = [0, ...holeIdx, coords.length / 2];
  let want = ringArea(0, n);
  for (let k = 1; k + 1 < bounds.length; k++) want -= ringArea(bounds[k], bounds[k + 1]);
  const tol = 1e-9 * Math.max(1, want);
  let best: number[] = [];
  let bestErr = Infinity;
  for (let rot = 0; rot < Math.min(n, 16); rot++) {
    const c = rot === 0 ? coords : [...coords.slice(2 * rot, 2 * n), ...coords.slice(0, 2 * rot), ...coords.slice(2 * n)];
    const idx = earcut(c, holeIdx, 2).map((i) => (i < n ? (i + rot) % n : i));
    const err = Math.abs(coveredArea(coords, idx) - want);
    if (err < bestErr) {
      best = idx;
      bestErr = err;
    }
    if (err <= tol) break;
  }
  return best;
}

/**
 * Make an earcut triangulation conforming: drop zero-area slivers (earcut makes
 * them at collinear points) and fan-split triangles whose edges pass through
 * other vertices. Without this, seams show pixel cracks (T-junctions).
 */
function conformTriangles(coords: number[], idx: number[]): number[] {
  const n = coords.length / 2;
  const X = (i: number): number => coords[2 * i];
  const Y = (i: number): number => coords[2 * i + 1];
  const area2 = (a: number, b: number, c: number): number => (X(b) - X(a)) * (Y(c) - Y(a)) - (X(c) - X(a)) * (Y(b) - Y(a));
  const len2 = (a: number, b: number): number => (X(b) - X(a)) ** 2 + (Y(b) - Y(a)) ** 2;
  const stack: [number, number, number][] = [];
  for (let i = 0; i < idx.length; i += 3) {
    const [a, b, c] = [idx[i], idx[i + 1], idx[i + 2]];
    const L = Math.max(len2(a, b), len2(b, c), len2(c, a));
    if (Math.abs(area2(a, b, c)) > 1e-10 * L) stack.push([a, b, c]);
  }
  const out: number[] = [];
  let guard = 0;
  while (stack.length && guard++ < 1_000_000) {
    const [a, b, c] = stack.pop()!;
    let split = false;
    for (const [u, v, w] of [
      [a, b, c],
      [b, c, a],
      [c, a, b],
    ] as const) {
      const l2 = len2(u, v);
      if (l2 < 1e-18) continue;
      let best = -1;
      let bestT = 2;
      for (let p = 0; p < n; p++) {
        if (p === u || p === v || p === w) continue;
        const t = ((X(p) - X(u)) * (X(v) - X(u)) + (Y(p) - Y(u)) * (Y(v) - Y(u))) / l2;
        if (t <= 1e-9 || t >= 1 - 1e-9 || t >= bestT) continue;
        const cr = (X(v) - X(u)) * (Y(p) - Y(u)) - (Y(v) - Y(u)) * (X(p) - X(u));
        if (cr * cr > 1e-14 * l2 * Math.max(1, l2)) continue;
        // a vertex clearly outside the triangle only lies near the line of a long
        // diagonal; splitting there would fold triangles over each other
        if (cr * Math.sign(area2(u, v, w)) < -1e-9 * Math.sqrt(l2)) continue;
        best = p;
        bestT = t;
      }
      if (best >= 0) {
        stack.push([u, best, w], [best, v, w]);
        split = true;
        break;
      }
    }
    if (!split) out.push(a, b, c);
  }
  return out;
}

/**
 * Breakpoints (positions w along the bend line) of every bend: the seam
 * vertices of face A within the strip's extent at f = 0, those of face B
 * within its extent at f = 1, and both strip ends at f = 0 and f = 1.
 */
export function bendBreakpoints(part: Part): Map<string, number[]> {
  const breaks = new Map<string, number[]>();
  for (const b of part.bends.values()) {
    const ws: number[] = [];
    for (const side of [0, 1] as const) {
      const [wl, wr] = stripRange(b, side);
      ws.push(wl, wr);
      const faceId = side === 0 ? b.faceA : b.faceB;
      const face = faceId ? part.faces.get(faceId) : undefined;
      for (const p of face?.outer.pts ?? []) {
        const w = onSeam(b, p, side);
        if (w !== null && w >= wl - 1e-6 && w <= wr + 1e-6) ws.push(w);
      }
    }
    breaks.set(b.id, uniqSorted(ws));
  }
  return breaks;
}

/** One row of strip vertices at fraction f: both strip ends plus the breakpoints between them. */
export function rowBreakpoints(b: Bend, all: number[], f: number): number[] {
  const [wl, wr] = stripRange(b, f);
  return [wl, ...all.filter((w) => w > wl + 1e-6 && w < wr - 1e-6), wr];
}

/** Outer ring of a face with the strip rows at its seams inserted into its seam edges. */
export function seamSplitOuter(part: Part, face: Face, breaks: Map<string, number[]>): Ring {
  const extra: V2[] = [];
  for (const b of part.bends.values()) {
    if (b.faceA === face.id) extra.push(...seamPoints(b, rowBreakpoints(b, breaks.get(b.id)!, 0), 0));
    if (b.faceB === face.id) extra.push(...seamPoints(b, rowBreakpoints(b, breaks.get(b.id)!, 1), 1));
  }
  return augmentRing(face.outer, extra, isSeamTag);
}

function bendSlices(angle: number): number {
  return Math.max(2, Math.ceil((angle * 180) / Math.PI / 6));
}

/** Rows of a strip: the regular slices plus every fraction where an end profile changes. */
function stripRows(b: Bend, all: number[]): { f: number; ws: number[] }[] {
  const N = bendSlices(b.angle);
  const fs = uniqFractions([...Array.from({ length: N + 1 }, (_, j) => j / N), ...stripFractions(b)], 1e-6);
  return fs.map((f) => ({ f, ws: rowBreakpoints(b, all, f) }));
}

/**
 * Triangles between two rows of vertices, each sorted by w. The band between
 * two rows is a convex trapezoid, so merging by w always gives a valid fan.
 * Triangles are counter-clockwise in the strip's (w, f) frame.
 */
function zipRows(r0: number[], w0: number[], r1: number[], w1: number[]): number[] {
  const out: number[] = [];
  let i = 0;
  let j = 0;
  while (i + 1 < r0.length || j + 1 < r1.length) {
    if (i + 1 >= r0.length || (j + 1 < r1.length && w1[j + 1] <= w0[i + 1] + 1e-9)) {
      out.push(r0[i], r1[j + 1], r1[j]);
      j++;
    } else {
      out.push(r0[i], r0[i + 1], r1[j]);
      i++;
    }
  }
  return out;
}

export function buildMesh(part: Part): PartMesh {
  const placements = computePlacements(part);
  const warnings: string[] = [];
  const T = part.T;
  const anchorM = part.anchor.M;
  const vs: VertexStore = { pos: [], flat: [], flat2: [], nrm: [], flatNrm: [] };
  const flatUp = applyDirA(anchorM, v3(0, 0, 1));
  const tris: number[] = [];
  const triPiece: number[] = [];
  const triSurface: number[] = [];
  const pieces: MeshPiece[] = [];
  /** directed top edges: key "a,b" → tag */
  const edgeTag = new Map<string, string>();
  const topToBottom = new Map<number, number>();

  // 1. breakpoints along each bend (union of both seams' vertices)
  const breaks = bendBreakpoints(part);

  // 2. faces
  const faceVerts = new Map<string, { p: V2; top: number; bot: number }[]>();
  for (const face of part.faces.values()) {
    const M = placements.get(face.id);
    if (!M) {
      warnings.push(`Fläche ${face.id} ist nicht mit dem Teil verbunden.`);
      continue;
    }
    const outer = seamSplitOuter(part, face, breaks);
    const rings = [outer, ...face.holes];
    const pieceIndex = pieces.push({ kind: 'face', id: face.id, feature: face.feature }) - 1;
    const coords: number[] = [];
    const holeIdx: number[] = [];
    const top: number[] = [];
    const list: { p: V2; top: number; bot: number }[] = [];
    const up = applyDirA(M, v3(0, 0, 1));
    for (let r = 0; r < rings.length; r++) {
      if (r > 0) holeIdx.push(coords.length / 2);
      const ring = rings[r];
      const ringTop: number[] = [];
      for (const p of ring.pts) {
        coords.push(p.x, p.y);
        const t = pushVertex(vs, applyA(M, v3(p.x, p.y, T)), applyA(anchorM, v3(p.x, p.y, T)), p, up, flatUp);
        const bt = pushVertex(vs, applyA(M, v3(p.x, p.y, 0)), applyA(anchorM, v3(p.x, p.y, 0)), p, neg(up), neg(flatUp));
        topToBottom.set(t, bt);
        top.push(t);
        ringTop.push(t);
        list.push({ p, top: t, bot: bt });
      }
      for (let i = 0; i < ring.pts.length; i++) {
        edgeTag.set(`${ringTop[i]},${ringTop[(i + 1) % ring.pts.length]}`, ring.tags[i]);
      }
    }
    faceVerts.set(face.id, list);
    const idx = conformTriangles(coords, triangulate(coords, holeIdx));
    for (let i = 0; i < idx.length; i += 3) {
      let a = idx[i];
      let b = idx[i + 1];
      let c = idx[i + 2];
      const area =
        (coords[2 * b] - coords[2 * a]) * (coords[2 * c + 1] - coords[2 * a + 1]) -
        (coords[2 * c] - coords[2 * a]) * (coords[2 * b + 1] - coords[2 * a + 1]);
      if (area < 0) [b, c] = [c, b];
      tris.push(top[a], top[b], top[c]);
      triPiece.push(pieceIndex);
      triSurface.push(0);
      tris.push(topToBottom.get(top[a])!, topToBottom.get(top[c])!, topToBottom.get(top[b])!);
      triPiece.push(pieceIndex);
      triSurface.push(1);
    }
  }

  const findFaceVertex = (faceId: string | null, p: V2): { top: number; bot: number } | null => {
    if (!faceId) return null;
    const list = faceVerts.get(faceId);
    if (!list) return null;
    let best: { top: number; bot: number } | null = null;
    let bd = 1e-5;
    for (const v of list) {
      const d = dist2(v.p, p);
      if (d < bd) {
        bd = d;
        best = v;
      }
    }
    return best;
  };

  // 3. bend strips: one row of vertices per fraction, rows zipped together
  for (const b of part.bends.values()) {
    const MA = placements.get(b.faceA);
    if (!MA) continue;
    const pieceIndex = pieces.push({ kind: 'bend', id: b.id, feature: b.feature }) - 1;
    const rows = stripRows(b, breaks.get(b.id)!);
    const last = rows.length - 1;
    const gridTop: number[][] = [];
    rows.forEach(({ f, ws }, k) => {
      const n = applyDirA(MA, stripNormal(b, f));
      gridTop.push(
        ws.map((w) => {
          const fp = stripFlat(b, w, f);
          let shared: { top: number; bot: number } | null = null;
          if (k === 0) shared = findFaceVertex(b.faceA, fp);
          if (k === last) shared = findFaceVertex(b.faceB, fp);
          if (shared) return shared.top;
          const t = pushVertex(vs, applyA(MA, stripPoint(b, T, w, f, T)), applyA(anchorM, v3(fp.x, fp.y, T)), fp, n, flatUp);
          const bt = pushVertex(vs, applyA(MA, stripPoint(b, T, w, f, 0)), applyA(anchorM, v3(fp.x, fp.y, 0)), fp, neg(n), neg(flatUp));
          topToBottom.set(t, bt);
          return t;
        }),
      );
    });
    // zipRows is CCW in (w, f); a clockwise (dir, nrm) frame flips it
    const ccw = cross2(b.dir, b.nrm) < 0;
    for (let k = 0; k < last; k++) {
      const tri = zipRows(gridTop[k], rows[k].ws, gridTop[k + 1], rows[k + 1].ws);
      for (let i = 0; i < tri.length; i += 3) {
        const [a, bb, c] = ccw ? [tri[i], tri[i + 2], tri[i + 1]] : [tri[i], tri[i + 1], tri[i + 2]];
        tris.push(a, bb, c);
        triPiece.push(pieceIndex);
        triSurface.push(0);
        tris.push(topToBottom.get(a)!, topToBottom.get(c)!, topToBottom.get(bb)!);
        triPiece.push(pieceIndex);
        triSurface.push(1);
      }
    }
    // tags for strip boundary edges
    const tagBoth = (a: number, c: number, tag: string, keep: boolean): void => {
      for (const key of [`${a},${c}`, `${c},${a}`]) if (!keep || !edgeTag.has(key)) edgeTag.set(key, tag);
    };
    for (let k = 0; k < last; k++) {
      tagBoth(gridTop[k][0], gridTop[k + 1][0], 'bend-side', false);
      tagBoth(gridTop[k][gridTop[k].length - 1], gridTop[k + 1][gridTop[k + 1].length - 1], 'bend-side', false);
    }
    for (const k of [0, last]) {
      for (let i = 0; i + 1 < gridTop[k].length; i++) tagBoth(gridTop[k][i], gridTop[k][i + 1], k === 0 ? 'bend-start' : 'bend-end', true);
    }
  }

  // 4. boundary edges of the top surface → side walls
  const count = new Map<string, number>();
  const topTriCount = tris.length / 3;
  for (let t = 0; t < topTriCount; t++) {
    if (triSurface[t] !== 0) continue;
    for (let e = 0; e < 3; e++) {
      const a = tris[3 * t + e];
      const b = tris[3 * t + ((e + 1) % 3)];
      const key = a < b ? `${a},${b}` : `${b},${a}`;
      count.set(key, (count.get(key) ?? 0) + 1);
    }
  }
  const boundary: BoundaryEdge[] = [];
  for (let t = 0; t < topTriCount; t++) {
    if (triSurface[t] !== 0) continue;
    for (let e = 0; e < 3; e++) {
      const a = tris[3 * t + e];
      const b = tris[3 * t + ((e + 1) % 3)];
      const key = a < b ? `${a},${b}` : `${b},${a}`;
      if (count.get(key) !== 1) continue;
      boundary.push({ a, b, tag: edgeTag.get(`${a},${b}`) ?? 'edge', piece: triPiece[t] });
    }
  }
  for (const e of boundary) {
    const ab = topToBottom.get(e.a)!;
    const bb = topToBottom.get(e.b)!;
    tris.push(ab, bb, e.b, ab, e.b, e.a);
    triPiece.push(e.piece, e.piece);
    triSurface.push(2, 2);
  }

  // 5. display edges: outline on both sheet surfaces, plus through-thickness lines at sharp corners
  const outgoing = new Map<number, BoundaryEdge[]>();
  for (const e of boundary) {
    if (!outgoing.has(e.a)) outgoing.set(e.a, []);
    outgoing.get(e.a)!.push(e);
  }
  const lines: number[] = [];
  const f2 = vs.flat2;
  const cosSharp = Math.cos((20 * Math.PI) / 180);
  for (const e of boundary) {
    lines.push(e.a, e.b, topToBottom.get(e.a)!, topToBottom.get(e.b)!);
    const ux = f2[2 * e.b] - f2[2 * e.a];
    const uy = f2[2 * e.b + 1] - f2[2 * e.a + 1];
    const lu = Math.hypot(ux, uy);
    for (const n of outgoing.get(e.b) ?? []) {
      const wx = f2[2 * n.b] - f2[2 * n.a];
      const wy = f2[2 * n.b + 1] - f2[2 * n.a + 1];
      const lw = Math.hypot(wx, wy);
      if (lu < 1e-9 || lw < 1e-9 || (ux * wx + uy * wy) / (lu * lw) < cosSharp) lines.push(e.b, topToBottom.get(e.b)!);
    }
  }

  return {
    positions: Float64Array.from(vs.pos),
    flatPositions: Float64Array.from(vs.flat),
    flat2: Float64Array.from(vs.flat2),
    normals: Float64Array.from(vs.nrm),
    flatNormals: Float64Array.from(vs.flatNrm),
    indices: Uint32Array.from(tris),
    triPiece: Int32Array.from(triPiece),
    triSurface: Uint8Array.from(triSurface),
    pieces,
    boundary,
    edgeLines: Uint32Array.from(lines),
    placements,
    warnings,
  };
}

/** Enclosed volume (mm³) of a closed mesh. */
export function meshVolume(positions: Float64Array, indices: Uint32Array): number {
  let v = 0;
  for (let i = 0; i < indices.length; i += 3) {
    const a = indices[i] * 3;
    const b = indices[i + 1] * 3;
    const c = indices[i + 2] * 3;
    const ax = positions[a], ay = positions[a + 1], az = positions[a + 2];
    const bx = positions[b], by = positions[b + 1], bz = positions[b + 2];
    const cx = positions[c], cy = positions[c + 1], cz = positions[c + 2];
    v += ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
  }
  return v / 6;
}

export interface Box3 {
  min: V3;
  max: V3;
}

export function meshBounds(positions: Float64Array): Box3 {
  const min = v3(Infinity, Infinity, Infinity);
  const max = v3(-Infinity, -Infinity, -Infinity);
  for (let i = 0; i < positions.length; i += 3) {
    min.x = Math.min(min.x, positions[i]);
    min.y = Math.min(min.y, positions[i + 1]);
    min.z = Math.min(min.z, positions[i + 2]);
    max.x = Math.max(max.x, positions[i]);
    max.y = Math.max(max.y, positions[i + 1]);
    max.z = Math.max(max.z, positions[i + 2]);
  }
  return { min, max };
}

/** Pickable edges of faces (free edges, cut-outs and seams), in folded and flat state. */
export interface PickEdge {
  tag: string;
  faceId: string;
  feature: string;
  seam: boolean;
  hole: boolean;
  flatA: V2;
  flatB: V2;
  /** Set for the through-thickness edge at an outline corner. */
  corner?: { prevTag: string; nextTag: string };
  /** [top a, top b, bottom a, bottom b] in folded space. */
  folded: [V3, V3, V3, V3];
  flat: [V3, V3, V3, V3];
}

export function pickEdges(part: Part, placements: Map<string, Affine>): PickEdge[] {
  const out: PickEdge[] = [];
  const T = part.T;
  const A = part.anchor.M;
  for (const face of part.faces.values()) {
    const M = placements.get(face.id);
    if (!M) continue;
    const rings: [Ring, boolean][] = [[face.outer, false], ...face.holes.map((h) => [h, true] as [Ring, boolean])];
    for (const [ring, hole] of rings) {
      const n = ring.pts.length;
      for (let i = 0; i < n; i++) {
        const a = ring.pts[i];
        const b = ring.pts[(i + 1) % n];
        const tag = ring.tags[i];
        const map = (X: Affine): [V3, V3, V3, V3] => [
          applyA(X, v3(a.x, a.y, T)),
          applyA(X, v3(b.x, b.y, T)),
          applyA(X, v3(a.x, a.y, 0)),
          applyA(X, v3(b.x, b.y, 0)),
        ];
        out.push({
          tag,
          faceId: face.id,
          feature: face.feature,
          seam: isSeamTag(tag),
          hole,
          flatA: a,
          flatB: b,
          folded: map(M),
          flat: map(A),
        });
      }
    }
  }
  return out;
}

/** Through-thickness edges at outline corners whose two edges are both free. */
export function pickCorners(part: Part, placements: Map<string, Affine>): PickEdge[] {
  const out: PickEdge[] = [];
  const T = part.T;
  const A = part.anchor.M;
  for (const face of part.faces.values()) {
    const M = placements.get(face.id);
    if (!M) continue;
    const r = face.outer;
    const n = r.pts.length;
    for (let i = 0; i < n; i++) {
      const prevTag = r.tags[(i - 1 + n) % n];
      const nextTag = r.tags[i];
      if (isSeamTag(prevTag) || isSeamTag(nextTag)) continue;
      const p = r.pts[i];
      const a = sub2(p, r.pts[(i - 1 + n) % n]);
      const b = sub2(r.pts[(i + 1) % n], p);
      if (Math.abs(cross2(a, b)) < 1e-6 * Math.hypot(a.x, a.y) * Math.hypot(b.x, b.y)) continue;
      const map = (X: Affine): [V3, V3, V3, V3] => {
        const top = applyA(X, v3(p.x, p.y, T));
        const bot = applyA(X, v3(p.x, p.y, 0));
        return [top, bot, top, bot];
      };
      out.push({
        tag: `corner:${prevTag}|${nextTag}`,
        faceId: face.id,
        feature: face.feature,
        seam: false,
        hole: false,
        flatA: p,
        flatB: p,
        corner: { prevTag, nextTag },
        folded: map(M),
        flat: map(A),
      });
    }
  }
  return out;
}

export function faceArea(face: Face): number {
  return signedArea(face.outer.pts) + face.holes.reduce((s, h) => s + signedArea(h.pts), 0);
}
