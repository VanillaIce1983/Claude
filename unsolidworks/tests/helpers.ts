import { Doc, newDoc, regenerate } from '../src/kernel/doc';
import { flatPattern } from '../src/kernel/flat';
import type { Feature } from '../src/kernel/features/types';
import { buildMesh, meshBounds, meshVolume } from '../src/kernel/mesh';
import type { Part } from '../src/kernel/model';
import { allEdges, computePlacements, stripPoint, stripRange } from '../src/kernel/model';
import { distPointSegment, pointInPolygon } from '../src/kernel/polygon';
import { V2, V3, applyA, invertRigid, v3 } from '../src/kernel/vec';

export function doc(features: Feature[], sheet: Partial<Doc['sheet']> = {}): Doc {
  const d = newDoc('Test');
  d.sheet = { ...d.sheet, ...sheet };
  d.features = features;
  return d;
}

export function build(d: Doc) {
  const r = regenerate(d);
  for (const [id, s] of r.status) {
    if (!s.ok) throw new Error(`Feature ${id} failed: ${s.error}`);
  }
  if (!r.part) throw new Error('no part');
  const mesh = buildMesh(r.part);
  const fp = flatPattern(r.part, mesh);
  return { part: r.part, mesh, fp, bounds: meshBounds(mesh.positions), volume: meshVolume(mesh.positions, mesh.indices) };
}

/** Every undirected edge must be used by exactly two triangles, in opposite directions. */
export function checkClosed(indices: Uint32Array): { open: number; bad: number } {
  const dir = new Map<string, number>();
  for (let i = 0; i < indices.length; i += 3) {
    for (let e = 0; e < 3; e++) {
      const a = indices[i + e];
      const b = indices[i + ((e + 1) % 3)];
      const k = `${a},${b}`;
      dir.set(k, (dir.get(k) ?? 0) + 1);
    }
  }
  let open = 0;
  let bad = 0;
  for (const [k, n] of dir) {
    const [a, b] = k.split(',');
    const back = dir.get(`${b},${a}`) ?? 0;
    if (back === 0) open++;
    if (n !== 1 || back !== 1) bad++;
  }
  return { open, bad };
}

export function rect(w: number, h: number): V2[] {
  return [
    { x: 0, y: 0 },
    { x: w, y: 0 },
    { x: w, y: h },
    { x: 0, y: h },
  ];
}

/** Find an edge by tag; `at` is its midpoint. */
export function edgeRef(part: Part, tag: string, side: 'top' | 'bottom' = 'top') {
  const e = allEdges(part).find((x) => x.tag === tag);
  if (!e) throw new Error(`edge ${tag} not found; have ${allEdges(part).map((x) => x.tag).join(', ')}`);
  return { tag, at: { x: (e.a.x + e.b.x) / 2, y: (e.a.y + e.b.y) / 2 }, side };
}

export const plate = (w = 100, h = 50): Feature => ({
  id: 'base',
  name: 'Basislasche1',
  type: 'baseFlange',
  plane: 'top',
  points: rect(w, h),
  closed: true,
  reverse: false,
  depth: 0,
  midPlane: false,
});

export const close = (a: number, b: number, tol = 1e-3): boolean => Math.abs(a - b) <= tol;

/** Volume the tessellated mesh should have for K = 0.5: flat area × T minus the chord error of each bend. */
export function expectedVolume(r: { part: Part; fp: { area: number } }): number {
  const T = r.part.T;
  let v = r.fp.area * T;
  for (const b of r.part.bends.values()) {
    const n = Math.max(2, Math.ceil((b.angle * 180) / Math.PI / 6));
    const x = b.angle / n;
    v -= b.ba * T * (b.w1 - b.w0) * (1 - Math.sin(x) / x);
  }
  return v;
}

interface Piece {
  id: string;
  box: number[];
  inside(q: V3, m: number): boolean;
}

function boxOf(pts: V3[]): number[] {
  const b = [Infinity, Infinity, Infinity, -Infinity, -Infinity, -Infinity];
  for (const q of pts) {
    [q.x, q.y, q.z].forEach((c, i) => {
      b[i] = Math.min(b[i], c);
      b[i + 3] = Math.max(b[i + 3], c);
    });
  }
  return b;
}

/** Faces and bend strips as solids with an "inside, at least m from the boundary" test. */
function solids(p: Part): Piece[] {
  const pl = computePlacements(p);
  const T = p.T;
  const out: Piece[] = [];
  for (const f of p.faces.values()) {
    const M = pl.get(f.id);
    if (!M) continue;
    const Mi = invertRigid(M);
    const rings = [f.outer, ...f.holes];
    out.push({
      id: `face:${f.id}`,
      box: boxOf(f.outer.pts.flatMap((q) => [applyA(M, v3(q.x, q.y, 0)), applyA(M, v3(q.x, q.y, T))])),
      inside(q, m) {
        const l = applyA(Mi, q);
        if (l.z <= m || l.z >= T - m) return false;
        const p2 = { x: l.x, y: l.y };
        if (!pointInPolygon(p2, f.outer.pts) || f.holes.some((h) => pointInPolygon(p2, h.pts))) return false;
        return rings.every((r) => r.pts.every((a, i) => distPointSegment(p2, a, r.pts[(i + 1) % r.pts.length]) >= m));
      },
    });
  }
  for (const b of p.bends.values()) {
    const MA = pl.get(b.faceA);
    if (!MA) continue;
    const Mi = invertRigid(MA);
    const R = b.radius;
    const pts: V3[] = [];
    for (let i = 0; i <= 90; i++) {
      const [wl, wr] = stripRange(b, i / 90);
      for (const w of [wl, wr]) for (const h of [0, T]) pts.push(applyA(MA, stripPoint(b, T, w, i / 90, h)));
    }
    out.push({
      id: `bend:${b.id}`,
      box: boxOf(pts),
      inside(q, m) {
        const l = applyA(Mi, q);
        const rx = l.x - b.origin.x;
        const ry = l.y - b.origin.y;
        const w = rx * b.dir.x + ry * b.dir.y;
        const t = rx * b.nrm.x + ry * b.nrm.y;
        const c = b.up ? T + R - l.z : l.z + R;
        const rho = Math.hypot(t, c);
        if (rho <= R + m || rho >= R + T - m) return false;
        let phi = Math.atan2(t, c);
        if (phi < 0) phi += 2 * Math.PI;
        if (phi <= m / rho || phi >= b.angle - m / rho) return false;
        const [wl, wr] = stripRange(b, phi / b.angle);
        return w > wl + m && w < wr - m;
      },
    });
  }
  return out;
}

/**
 * Volume (mm³, estimated on a grid) where two pieces of the part overlap by
 * more than 0.01 mm, and the overlapping pairs.
 */
export function interpenetration(p: Part): { volume: number; pairs: string[] } {
  const ps = solids(p);
  const m = 0.01;
  let volume = 0;
  const pairs: string[] = [];
  for (let i = 0; i < ps.length; i++) {
    for (let j = i + 1; j < ps.length; j++) {
      const A = ps[i].box;
      const B = ps[j].box;
      const lo = [0, 1, 2].map((k) => Math.max(A[k], B[k]));
      const hi = [0, 1, 2].map((k) => Math.min(A[k + 3], B[k + 3]));
      if (lo.some((v, k) => v >= hi[k])) continue;
      let h = 0.1;
      while (((hi[0] - lo[0]) / h) * ((hi[1] - lo[1]) / h) * ((hi[2] - lo[2]) / h) > 1.5e5) h *= 1.25;
      let n = 0;
      for (let x = lo[0] + h / 2; x < hi[0]; x += h) {
        for (let y = lo[1] + h / 2; y < hi[1]; y += h) {
          for (let z = lo[2] + h / 2; z < hi[2]; z += h) if (ps[i].inside(v3(x, y, z), m) && ps[j].inside(v3(x, y, z), m)) n++;
        }
      }
      if (n) {
        volume += n * h * h * h;
        pairs.push(`${ps[i].id}~${ps[j].id}`);
      }
    }
  }
  return { volume, pairs };
}
