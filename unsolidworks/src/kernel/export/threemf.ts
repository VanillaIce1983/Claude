// 3MF export (3D Manufacturing Format) of the folded part's triangle mesh.
//
// The 3MF build volume is Z-up, model space is Y-up: vertices are rotated
// with (x, y, z) → (x, −z, y), a proper rotation, so the outward triangle
// winding is kept. The part is then moved so its bounding box starts at
// x = y = z = 0: it stands on the build plate in the positive quadrant.

import { buildMesh } from '../mesh';
import type { Part } from '../model';
import { exportPart } from './prepare';
import { makeZip } from './zip';

export interface ThreeMfOptions {
  /** Object name written into the model. */
  name: string;
}

export interface PrintMesh {
  /** Build-volume coordinates (mm, Z up), rounded to the 6 decimals written to the file. */
  positions: Float64Array;
  indices: Uint32Array;
}

const CORE_NS = 'http://schemas.microsoft.com/3dmanufacturing/core/2015/02';
const MODEL_PATH = '3D/3dmodel.model';
/** Coordinates are stored as whole nanometres (6 decimals in mm). */
const NANO = 1e6;

/** Rotate to Z up, move onto the plate and round; only referenced vertices count for the bounds. */
function toPlateNano(positions: Float64Array, indices: Uint32Array): Float64Array {
  const out = new Float64Array(positions.length);
  const min = [Infinity, Infinity, Infinity];
  for (let v = 0; v < positions.length / 3; v++) {
    out[3 * v] = positions[3 * v];
    out[3 * v + 1] = -positions[3 * v + 2];
    out[3 * v + 2] = positions[3 * v + 1];
  }
  for (const v of indices) for (let k = 0; k < 3; k++) min[k] = Math.min(min[k], out[3 * v + k]);
  for (let i = 0; i < out.length; i++) out[i] = Math.round((out[i] - min[i % 3]) * NANO);
  return out;
}

/** Vertices of directed edges that do not have exactly one opposite edge. */
function nonManifoldVertices(tris: number[], stride: number): Set<number> {
  const count = new Map<number, number>();
  const key = (a: number, b: number): number => a * stride + b;
  for (let i = 0; i < tris.length; i += 3) {
    for (let e = 0; e < 3; e++) {
      const k = key(tris[i + e], tris[i + ((e + 1) % 3)]);
      count.set(k, (count.get(k) ?? 0) + 1);
    }
  }
  const bad = new Set<number>();
  for (const [k, n] of count) {
    const a = Math.floor(k / stride);
    const b = k - a * stride;
    if (n !== 1 || count.get(key(b, a)) !== 1) bad.add(a).add(b);
  }
  return bad;
}

function zeroArea(p: Float64Array, a: number, b: number, c: number): boolean {
  const ux = p[3 * b] - p[3 * a], uy = p[3 * b + 1] - p[3 * a + 1], uz = p[3 * b + 2] - p[3 * a + 2];
  const vx = p[3 * c] - p[3 * a], vy = p[3 * c + 1] - p[3 * a + 1], vz = p[3 * c + 2] - p[3 * a + 2];
  return uy * vz - uz * vy === 0 && uz * vx - ux * vz === 0 && ux * vy - uy * vx === 0;
}

/**
 * Weld vertices with identical (rounded) coordinates. Triangles that collapse
 * must go (3MF forbids repeated indices); other zero-area triangles go only if
 * the mesh stays manifold. Where welding would break manifoldness (unglued
 * pieces that touch along an edge), those vertices stay separate.
 */
function weld(p: Float64Array, indices: Uint32Array): { nano: Float64Array; indices: Uint32Array } {
  const n = p.length / 3;
  const rep = new Int32Array(n);
  const members = new Map<number, number[]>();
  const first = new Map<string, number>();
  for (let v = 0; v < n; v++) {
    const k = `${p[3 * v]},${p[3 * v + 1]},${p[3 * v + 2]}`;
    const r = first.get(k) ?? v;
    first.set(k, r);
    rep[v] = r;
    if (!members.has(r)) members.set(r, []);
    members.get(r)!.push(v);
  }
  for (;;) {
    const kept: number[] = [];
    const lean: number[] = [];
    for (let i = 0; i < indices.length; i += 3) {
      const a = rep[indices[i]], b = rep[indices[i + 1]], c = rep[indices[i + 2]];
      if (a === b || b === c || c === a) continue;
      kept.push(a, b, c);
      if (!zeroArea(p, a, b, c)) lean.push(a, b, c);
    }
    let tris = lean;
    let bad = nonManifoldVertices(tris, n);
    if (bad.size && lean.length < kept.length) {
      tris = kept;
      bad = nonManifoldVertices(tris, n);
    }
    let split = false;
    for (const r of bad) {
      for (const m of members.get(r) ?? []) {
        if (rep[m] !== m) split = true;
        rep[m] = m;
      }
    }
    // an input that is not manifold by itself is written as it is
    if (!split) return compact(p, tris);
  }
}

function compact(p: Float64Array, tris: number[]): { nano: Float64Array; indices: Uint32Array } {
  const used = new Int32Array(p.length / 3).fill(-1);
  for (const v of tris) used[v] = 0;
  let next = 0;
  for (let v = 0; v < used.length; v++) if (used[v] === 0) used[v] = next++;
  const nano = new Float64Array(next * 3);
  for (let v = 0; v < used.length; v++) {
    if (used[v] < 0) continue;
    for (let k = 0; k < 3; k++) nano[3 * used[v] + k] = p[3 * v + k];
  }
  return { nano, indices: Uint32Array.from(tris, (v) => used[v]) };
}

function weldedNano(positions: Float64Array, indices: Uint32Array): { nano: Float64Array; indices: Uint32Array } {
  return weld(toPlateNano(positions, indices), indices);
}

/** The mesh exactly as meshTo3mf writes it (build-volume coordinates, welded). */
export function printMesh(positions: Float64Array, indices: Uint32Array): PrintMesh {
  const w = weldedNano(positions, indices);
  return { positions: w.nano.map((v) => v / NANO), indices: w.indices };
}

/** Exact decimal text of a whole number of nanometres, in mm. */
function mm(nano: number): string {
  const sign = nano < 0 ? '-' : '';
  const a = Math.abs(nano);
  const whole = Math.floor(a / NANO);
  const frac = a - whole * NANO;
  return frac === 0 ? `${sign}${whole}` : `${sign}${whole}.${String(frac).padStart(6, '0').replace(/0+$/, '')}`;
}

function xmlText(s: string): string {
  return s
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f￾￿]/g, '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

function modelXml(nano: Float64Array, indices: Uint32Array, name: string): string {
  const out: string[] = [
    '<?xml version="1.0" encoding="UTF-8"?>',
    `<model unit="millimeter" xml:lang="de-DE" xmlns="${CORE_NS}">`,
    ` <metadata name="Title">${xmlText(name)}</metadata>`,
    ' <metadata name="Application">UnsolidWorks</metadata>',
    ' <resources>',
    `  <object id="1" type="model" name="${xmlText(name)}">`,
    '   <mesh>',
    '    <vertices>',
  ];
  for (let i = 0; i < nano.length; i += 3) {
    out.push(`     <vertex x="${mm(nano[i])}" y="${mm(nano[i + 1])}" z="${mm(nano[i + 2])}"/>`);
  }
  out.push('    </vertices>', '    <triangles>');
  for (let i = 0; i < indices.length; i += 3) {
    out.push(`     <triangle v1="${indices[i]}" v2="${indices[i + 1]}" v3="${indices[i + 2]}"/>`);
  }
  out.push('    </triangles>', '   </mesh>', '  </object>', ' </resources>', ' <build>', '  <item objectid="1"/>', ' </build>', '</model>', '');
  return out.join('\n');
}

const CONTENT_TYPES = `<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
 <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
 <Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>
</Types>
`;

const RELS = `<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
 <Relationship Target="/${MODEL_PATH}" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/>
</Relationships>
`;

/** A complete .3mf package (ZIP bytes); model space Y-up is converted to the 3MF Z-up build volume, unit mm. */
export function meshTo3mf(positions: Float64Array, indices: Uint32Array, opts: ThreeMfOptions): Uint8Array {
  const w = weldedNano(positions, indices);
  if (w.indices.length < 12) throw new Error('Das Teil hat keine Geometrie zum Exportieren.');
  const enc = new TextEncoder();
  return makeZip([
    { name: '[Content_Types].xml', data: enc.encode(CONTENT_TYPES) },
    { name: '_rels/.rels', data: enc.encode(RELS) },
    { name: MODEL_PATH, data: enc.encode(modelXml(w.nano, w.indices, opts.name)) },
  ]);
}

/** 3MF of the folded part, with the minimal bend radius solid exports need. */
export function partTo3mf(part: Part, opts: ThreeMfOptions): Uint8Array {
  const mesh = buildMesh(exportPart(part));
  return meshTo3mf(mesh.positions, mesh.indices, opts);
}
