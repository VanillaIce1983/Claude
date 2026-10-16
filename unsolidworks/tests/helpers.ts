import { Doc, newDoc, regenerate } from '../src/kernel/doc';
import { flatPattern } from '../src/kernel/flat';
import type { Feature } from '../src/kernel/features/types';
import { buildMesh, meshBounds, meshVolume } from '../src/kernel/mesh';
import type { Part } from '../src/kernel/model';
import { allEdges } from '../src/kernel/model';
import type { V2 } from '../src/kernel/vec';

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
