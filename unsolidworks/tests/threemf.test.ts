import { describe, expect, it } from 'vitest';
import { exportPart } from '../src/kernel/export/prepare';
import { meshTo3mf, partTo3mf, printMesh } from '../src/kernel/export/threemf';
import { crc32 } from '../src/kernel/export/zip';
import type { EdgeFlangeFeature, HemFeature } from '../src/kernel/features/types';
import { buildMesh, meshBounds, meshVolume } from '../src/kernel/mesh';
import type { Part } from '../src/kernel/model';
import { exampleDoc } from '../src/ui/example';
import { build, checkClosed, doc, plate } from './helpers';

/** Entries of a stored (uncompressed) ZIP, CRC-checked. */
function unzip(bytes: Uint8Array): Map<string, string> {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const dec = new TextDecoder();
  const out = new Map<string, string>();
  let o = 0;
  while (view.getUint32(o, true) === 0x04034b50) {
    expect(view.getUint16(o + 8, true)).toBe(0);
    const size = view.getUint32(o + 18, true);
    const nameLen = view.getUint16(o + 26, true);
    const start = o + 30 + nameLen + view.getUint16(o + 28, true);
    const data = bytes.subarray(start, start + size);
    expect(crc32(data)).toBe(view.getUint32(o + 14, true));
    out.set(dec.decode(bytes.subarray(o + 30, o + 30 + nameLen)), dec.decode(data));
    o = start + size;
  }
  expect(view.getUint32(o, true)).toBe(0x02014b50);
  return out;
}

/** Minimal well-formedness check: one root, balanced tags, quoted attributes, predefined entities only. */
function wellFormed(xml: string): boolean {
  const body = xml.replace(/^<\?xml [^?]*\?>/, '');
  const tag = /<(\/?)([A-Za-z_][\w:.-]*)((?:\s+[A-Za-z_][\w:.-]*="[^"<]*")*)\s*(\/?)>/y;
  const badEntity = /&(?!(?:amp|lt|gt|quot|apos);)/;
  const stack: string[] = [];
  let roots = 0;
  let i = 0;
  for (;;) {
    const lt = body.indexOf('<', i);
    const text = body.slice(i, lt < 0 ? body.length : lt);
    if (badEntity.test(text) || (stack.length === 0 && text.trim() !== '')) return false;
    if (lt < 0) break;
    tag.lastIndex = lt;
    const m = tag.exec(body);
    if (!m || badEntity.test(m[3]) || (m[1] && m[4])) return false;
    if (m[1]) {
      if (stack.pop() !== m[2]) return false;
    } else {
      if (stack.length === 0) roots++;
      if (!m[4]) stack.push(m[2]);
    }
    i = tag.lastIndex;
  }
  return stack.length === 0 && roots === 1;
}

function parseModel(xml: string): { positions: Float64Array; indices: Uint32Array } {
  const p: number[] = [];
  for (const m of xml.matchAll(/<vertex x="([^"]+)" y="([^"]+)" z="([^"]+)"\/>/g)) p.push(+m[1], +m[2], +m[3]);
  const t: number[] = [];
  for (const m of xml.matchAll(/<triangle v1="(\d+)" v2="(\d+)" v3="(\d+)"\/>/g)) t.push(+m[1], +m[2], +m[3]);
  return { positions: Float64Array.from(p), indices: Uint32Array.from(t) };
}

const MODEL = '3D/3dmodel.model';

function modelOf(part: Part, name = 'Teil'): string {
  return unzip(partTo3mf(part, { name })).get(MODEL)!;
}

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

const tray = flange({
  edges: [
    { tag: 'base:e0', at: { x: 50, y: 0 }, side: 'top' },
    { tag: 'base:e1', at: { x: 100, y: 25 }, side: 'top' },
    { tag: 'base:e2', at: { x: 50, y: 50 }, side: 'top' },
    { tag: 'base:e3', at: { x: 0, y: 25 }, side: 'top' },
  ],
});

const closedHem: HemFeature = {
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

/** Unit cube (model space) at an offset, outward CCW triangles. */
function cube(ox: number, oy: number, oz: number): { p: number[]; t: number[] } {
  const p: number[] = [];
  for (let v = 0; v < 8; v++) p.push(ox + (v & 1), oy + ((v >> 1) & 1), oz + ((v >> 2) & 1));
  const quads = [
    [0, 2, 3, 1],
    [4, 5, 7, 6],
    [0, 1, 5, 4],
    [2, 6, 7, 3],
    [0, 4, 6, 2],
    [1, 3, 7, 5],
  ];
  return { p, t: quads.flatMap(([a, b, c, d]) => [a, b, c, a, c, d]) };
}

function merge(...meshes: { p: number[]; t: number[] }[]): { positions: Float64Array; indices: Uint32Array } {
  const p: number[] = [];
  const t: number[] = [];
  for (const m of meshes) {
    const base = p.length / 3;
    p.push(...m.p);
    t.push(...m.t.map((v) => v + base));
  }
  return { positions: Float64Array.from(p), indices: Uint32Array.from(t) };
}

function distinctPoints(positions: Float64Array): number {
  const keys = new Set<string>();
  for (let i = 0; i < positions.length; i += 3) keys.add(`${positions[i]},${positions[i + 1]},${positions[i + 2]}`);
  return keys.size;
}

describe('3MF package', () => {
  const files = unzip(partTo3mf(build(doc([plate()])).part, { name: 'Halter & <Blech> "1"' }));

  it('holds content types, the start part relationship and the model', () => {
    expect([...files.keys()]).toEqual(['[Content_Types].xml', '_rels/.rels', MODEL]);
    const types = files.get('[Content_Types].xml')!;
    expect(wellFormed(types)).toBe(true);
    expect(types).toContain('Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"');
    expect(types).toContain('Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"');
    const rels = files.get('_rels/.rels')!;
    expect(wellFormed(rels)).toBe(true);
    expect(rels).toContain('Target="/3D/3dmodel.model"');
    expect(rels).toContain('Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"');
  });

  it('model is well-formed millimetre XML with escaped name, one object and one build item', () => {
    const xml = files.get(MODEL)!;
    expect(wellFormed(xml)).toBe(true);
    expect(xml).toMatch(/<model unit="millimeter" xml:lang="de-DE" xmlns="http:\/\/schemas\.microsoft\.com\/3dmanufacturing\/core\/2015\/02">/);
    expect(xml).toContain('<metadata name="Title">Halter &amp; &lt;Blech&gt; &quot;1&quot;</metadata>');
    expect(xml).toContain('<metadata name="Application">UnsolidWorks</metadata>');
    expect(xml.match(/<object /g)).toHaveLength(1);
    expect(xml).toContain('<object id="1" type="model" ');
    expect(xml.match(/<item /g)).toHaveLength(1);
    expect(xml).toContain('<item objectid="1"/>');
  });

  it('the well-formedness check itself rejects broken XML', () => {
    expect(wellFormed('<a><b></a></b>')).toBe(false);
    expect(wellFormed('<a x="1 & 2"/>')).toBe(false);
    expect(wellFormed('<a/><b/>')).toBe(false);
  });
});

describe('3MF mesh', () => {
  const parts: [string, () => Part][] = [
    ['plate', () => build(doc([plate()])).part],
    ['tray', () => build(doc([plate(), tray])).part],
    ['closed hem', () => build(doc([plate(), closedHem])).part],
    ['example part', () => build(exampleDoc()).part],
  ];

  for (const [name, make] of parts) {
    it(`${name}: welded, manifold, on the build plate, same volume`, () => {
      const part = make();
      const mesh = buildMesh(exportPart(part));
      const expected = printMesh(mesh.positions, mesh.indices);
      const file = parseModel(modelOf(part));
      expect(file.positions.length).toBe(expected.positions.length);
      expect(file.indices.length).toBe(expected.indices.length);
      expect(file.positions).toEqual(expected.positions);
      expect(file.indices).toEqual(expected.indices);
      expect(checkClosed(file.indices).bad).toBe(0);
      const n = file.positions.length / 3;
      for (let i = 0; i < file.indices.length; i += 3) {
        const [a, b, c] = file.indices.subarray(i, i + 3);
        expect(a !== b && b !== c && c !== a && Math.max(a, b, c) < n).toBe(true);
      }
      expect(distinctPoints(file.positions)).toBe(n);
      const box = meshBounds(file.positions);
      expect([box.min.x, box.min.y, box.min.z]).toEqual([0, 0, 0]);
      // a proper rotation keeps the outward winding, so the volume stays positive and equal
      expect(meshVolume(file.positions, file.indices)).toBeCloseTo(meshVolume(mesh.positions, mesh.indices), 3);
    });
  }

  it('turns model Y-up into build-volume Z-up: (x, y, z) → (x, −z, y)', () => {
    const r = build(doc([plate(), tray]));
    const box = meshBounds(parseModel(modelOf(r.part)).positions);
    expect(box.max.x).toBeCloseTo(r.bounds.max.x - r.bounds.min.x, 6);
    expect(box.max.y).toBeCloseTo(r.bounds.max.z - r.bounds.min.z, 6);
    expect(box.max.z).toBeCloseTo(r.bounds.max.y - r.bounds.min.y, 6);
    expect([box.max.x, box.max.y, box.max.z].map((v) => +v.toFixed(6))).toEqual([100, 50, 30]);
  });

  it('closed hem: exported with the minimal radius, the part itself is untouched', () => {
    const part = build(doc([plate(), closedHem])).part;
    const radii = [...part.bends.values()].map((b) => b.radius);
    expect(radii).toContain(0);
    modelOf(part);
    expect([...part.bends.values()].map((b) => b.radius)).toEqual(radii);
  });

  it('a raw zero-radius hem collapses its inner bend surface but stays manifold', () => {
    const mesh = buildMesh(build(doc([plate(), closedHem])).part);
    const pm = printMesh(mesh.positions, mesh.indices);
    expect(pm.positions.length).toBeLessThan(mesh.positions.length);
    expect(pm.indices.length).toBeLessThan(mesh.indices.length);
    expect(checkClosed(pm.indices).bad).toBe(0);
    expect(distinctPoints(pm.positions)).toBe(pm.positions.length / 3);
    expect(meshVolume(pm.positions, pm.indices)).toBeCloseTo(meshVolume(mesh.positions, mesh.indices), 3);
  });
});

describe('welding', () => {
  it('welds pieces that touch in a single point', () => {
    const m = merge(cube(0, 0, 0), cube(1, 1, 1));
    const pm = printMesh(m.positions, m.indices);
    expect(pm.positions.length / 3).toBe(15);
    expect(checkClosed(pm.indices).bad).toBe(0);
  });

  it('keeps pieces apart where welding would share an edge between four triangles', () => {
    const m = merge(cube(0, 0, 0), cube(1, 1, 0));
    const pm = printMesh(m.positions, m.indices);
    expect(pm.positions.length / 3).toBe(16);
    expect(checkClosed(pm.indices).bad).toBe(0);
  });

  it('drops zero-area triangles only when the mesh stays manifold', () => {
    // fin pair along edge 0-1 through its midpoint: removable
    const fins = cube(0, 0, 0);
    fins.p.push(0.5, 0, 0);
    fins.t.push(0, 8, 1, 1, 8, 0);
    expect(printMesh(Float64Array.from(fins.p), Uint32Array.from(fins.t)).indices.length).toBe(36);
    // the face y = 0 splits edge 0-1 at its midpoint: the sliver closes the T-junction and must stay
    const tee = cube(0, 0, 0);
    tee.p.push(0.5, 0, 0);
    const i = tee.t.findIndex((_, k) => k % 3 === 0 && tee.t[k] === 0 && tee.t[k + 1] === 1 && tee.t[k + 2] === 5);
    tee.t.splice(i, 3, 0, 8, 5, 8, 1, 5);
    tee.t.push(0, 1, 8);
    const pm = printMesh(Float64Array.from(tee.p), Uint32Array.from(tee.t));
    expect(pm.indices.length).toBe(tee.t.length);
    expect(checkClosed(pm.indices).bad).toBe(0);
  });

  it('meshTo3mf writes exactly the welded mesh', () => {
    const m = merge(cube(0, 0, 0), cube(1, 1, 1));
    const file = parseModel(unzip(meshTo3mf(m.positions, m.indices, { name: 'Würfel' })).get(MODEL)!);
    expect(file).toEqual(printMesh(m.positions, m.indices));
  });
});
