import polygonClipping from 'polygon-clipping';
import { describe, expect, it } from 'vitest';
import { Doc, parseDoc, regenerate } from '../src/kernel/doc';
import type { BreakCornerFeature, ClosedCornerFeature, EdgeFlangeFeature, Feature, TabFeature } from '../src/kernel/features/types';
import { bendEntity } from '../src/kernel/measure';
import type { PartMesh } from '../src/kernel/mesh';
import { Bend, Part, computePlacements, endOffset, stripOutline, stripPoint, stripRange } from '../src/kernel/model';
import { distPointSegment, isSimple, pointInPolygon, signedArea } from '../src/kernel/polygon';
import { V2, V3, applyA, applyDirA, dot3, invertRigid, norm3, sub3, v2, v3 } from '../src/kernel/vec';
import { exampleDoc } from '../src/ui/example';
import { build, checkClosed, doc, edgeRef, expectedVolume, interpenetration, plate } from './helpers';

const T = 2;

/** Overlap area of two outlines on a 0.05 mm grid, for where the clipper gives up on pieces that only touch. */
function gridOverlap(a: V2[], b: V2[]): number {
  const box = (pts: V2[]): number[] => [Math.min(...pts.map((p) => p.x)), Math.min(...pts.map((p) => p.y)), Math.max(...pts.map((p) => p.x)), Math.max(...pts.map((p) => p.y))];
  const [p, q] = [box(a), box(b)];
  const h = 0.05;
  let area = 0;
  for (let x = Math.max(p[0], q[0]) + h / 2; x < Math.min(p[2], q[2]); x += h) {
    for (let y = Math.max(p[1], q[1]) + h / 2; y < Math.min(p[3], q[3]); y += h) if (pointInPolygon(v2(x, y), a) && pointInPolygon(v2(x, y), b)) area += h * h;
  }
  return area;
}

const tab = (over: Partial<TabFeature> = {}): TabFeature => ({
  id: 'tab',
  name: 'Lasche1',
  type: 'tab',
  edges: [{ tag: 'base:e2', at: { x: 50, y: 50 } }],
  length: 20,
  width: { mode: 'centered', offset1: 0, offset2: 0, width: 30 },
  ...over,
});

const corner = (over: Partial<BreakCornerFeature> = {}): BreakCornerFeature => ({
  id: 'bc',
  name: 'Ecke brechen1',
  type: 'breakCorner',
  corners: [{ prevTag: 'base:e3', nextTag: 'base:e0', at: { x: 0, y: 0 } }],
  kind: 'chamfer',
  distance: 5,
  ...over,
});

const twoFlanges: EdgeFlangeFeature = {
  id: 'fl',
  name: 'Kantenlasche1',
  type: 'edgeFlange',
  edges: [
    { tag: 'base:e0', at: { x: 50, y: 0 }, side: 'top' },
    { tag: 'base:e1', at: { x: 100, y: 25 }, side: 'top' },
  ],
  angle: 90,
  length: 30,
  lengthRef: 'outer',
  position: 'materialInside',
  reverse: false,
  width: { mode: 'full', offset1: 0, offset2: 0, width: 0 },
};

describe('Lasche (tab)', () => {
  it('centered tab adds a 30 × 20 rectangle in the plane', () => {
    const r = build(doc([plate(), tab()]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.fp.area).toBeCloseTo(100 * 50 + 30 * 20, 6);
    expect(r.bounds.min.z).toBeCloseTo(-70, 6);
    expect(r.bounds.max.y).toBeCloseTo(T, 6);
  });

  it('full-width tab keeps the edge name, so a flange can follow', () => {
    const t = tab({ width: { mode: 'full', offset1: 0, offset2: 0, width: 0 } });
    const r1 = build(doc([plate(), t]));
    const ref = edgeRef(r1.part, 'base:e2');
    const fl: EdgeFlangeFeature = { ...twoFlanges, edges: [ref] };
    const r = build(doc([plate(), t, fl]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.bounds.min.z).toBeCloseTo(-70, 6);
  });
});

describe('Ecke brechen', () => {
  it('chamfer removes a triangle', () => {
    const r = build(doc([plate(), corner()]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.fp.area).toBeCloseTo(5000 - 12.5, 6);
  });

  it('fillet removes close to r² − πr²/4', () => {
    const r = build(doc([plate(), corner({ kind: 'fillet', distance: 10 })]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.fp.area).toBeCloseTo(5000 - (100 - (Math.PI * 100) / 4), 0);
  });

  it('several corners and a radius that is too large', () => {
    const all = corner({
      kind: 'fillet',
      distance: 8,
      corners: [
        { prevTag: 'base:e3', nextTag: 'base:e0', at: { x: 0, y: 0 } },
        { prevTag: 'base:e0', nextTag: 'base:e1', at: { x: 100, y: 0 } },
        { prevTag: 'base:e1', nextTag: 'base:e2', at: { x: 100, y: 50 } },
        { prevTag: 'base:e2', nextTag: 'base:e3', at: { x: 0, y: 50 } },
      ],
    });
    const r = build(doc([plate(), all]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    const res = regenerate(doc([plate(), { ...all, distance: 30 } as Feature]));
    expect(res.status.get('bc')?.error).toMatch(/zu groß/);
  });
});

describe('Geschlossene Ecke', () => {
  // these check the flange faces alone, so the bend region stays open (see 'Geschlossene Ecke: Biegebereich')
  const cc = (refs: ClosedCornerFeature['edges'], kind?: ClosedCornerFeature['kind']): ClosedCornerFeature => ({
    id: 'cc',
    name: 'Geschlossene Ecke1',
    type: 'closedCorner',
    edges: refs,
    gap: 0.1,
    kind,
    openBend: true,
  });
  const lc = 30 - 4; // flat length of a 30 mm flange (outer virtual sharp, R = T = 2)

  it('material inside: the covered flange is extended to the inner face of the covering one', () => {
    const r1 = build(doc([plate(), twoFlanges]));
    const r = build(doc([plate(), twoFlanges, cc([edgeRef(r1.part, 'fl.1:side0')], 'underlap')]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
    // 4 mm from the trimmed base corner down to the inner face (T) of the first flange, minus the gap
    expect(r.fp.area - r1.fp.area).toBeCloseTo((4 - T - 0.1) * lc, 6);
    expect(r.bounds.max.x).toBeCloseTo(100, 6);
    expect(r.bounds.max.z).toBeCloseTo(0, 6);
  });

  it('picking the flange that already covers the corner gives the same result', () => {
    const r1 = build(doc([plate(), twoFlanges]));
    const r = build(doc([plate(), twoFlanges, cc([edgeRef(r1.part, 'fl.0:side1')])]));
    expect(r.fp.area - r1.fp.area).toBeCloseTo((4 - T - 0.1) * lc, 6);
  });

  it('overlap on the covered flange swaps the roles: it is extended, the other one trimmed', () => {
    const r1 = build(doc([plate(), twoFlanges]));
    const r = build(doc([plate(), twoFlanges, cc([edgeRef(r1.part, 'fl.1:side0')], 'overlap')]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
    // picked flange grows 4 mm to the other's outer face, the other loses T + gap
    expect(r.fp.area - r1.fp.area).toBeCloseTo((4 - (T + 0.1)) * lc, 6);
    expect(r.bounds.max.x).toBeCloseTo(100, 6);
    expect(r.bounds.max.z).toBeCloseTo(0, 6);
  });

  describe('flanges with bend outside (open corner on both sides)', () => {
    const outside: EdgeFlangeFeature = { ...twoFlanges, position: 'bendOutside' };
    const R = 2;

    it('extends both flanges so they meet', () => {
      const r1 = build(doc([plate(), outside]));
      const ref = edgeRef(r1.part, 'fl.0:side1');
      const r = build(doc([plate(), outside, cc([ref])]));
      expect(checkClosed(r.mesh.indices).bad).toBe(0);
      expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
      // picked flange runs R + T to the other's outer face, the other one R − gap to the picked flange's inner face
      expect(r.fp.area - r1.fp.area).toBeCloseTo((R + T + R - 0.1) * lc, 6);
      // outer size unchanged: 100 + R + T by 50 + R + T
      expect(r.bounds.max.x).toBeCloseTo(100 + R + T, 6);
      expect(r.bounds.max.z).toBeCloseTo(R + T, 6);
    });

    it('picking both sides of the same corner closes it once', () => {
      const r1 = build(doc([plate(), outside]));
      const refs = [edgeRef(r1.part, 'fl.0:side1'), edgeRef(r1.part, 'fl.1:side0')];
      const r = build(doc([plate(), outside, cc(refs)]));
      expect(r.fp.area - r1.fp.area).toBeCloseTo((R + T + R - 0.1) * lc, 6);
    });

    it('underlap lets the other flange cover the corner', () => {
      const r1 = build(doc([plate(), outside]));
      const r = build(doc([plate(), outside, cc([edgeRef(r1.part, 'fl.0:side1')], 'underlap')]));
      expect(checkClosed(r.mesh.indices).bad).toBe(0);
      expect(r.fp.area - r1.fp.area).toBeCloseTo((R - 0.1 + R + T) * lc, 6);
      expect(r.bounds.max.x).toBeCloseTo(100 + R + T, 6);
      expect(r.bounds.max.z).toBeCloseTo(R + T, 6);
    });

    it('butt extends both flanges by the same amount up to the other one\'s inner face', () => {
      const r1 = build(doc([plate(), outside]));
      const r = build(doc([plate(), outside, cc([edgeRef(r1.part, 'fl.0:side1')], 'butt')]));
      expect(checkClosed(r.mesh.indices).bad).toBe(0);
      expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
      // each flange grows R − gap; picking the other side gives the same part
      expect(r.fp.area - r1.fp.area).toBeCloseTo(2 * (R - 0.1) * lc, 6);
      const r2 = build(doc([plate(), outside, cc([edgeRef(r1.part, 'fl.1:side0')], 'butt')]));
      expect(r2.fp.area).toBeCloseTo(r.fp.area, 6);
      expect(r2.volume).toBeCloseTo(r.volume, 6);
    });

    it('material outside closes as well', () => {
      const mo: EdgeFlangeFeature = { ...twoFlanges, position: 'materialOutside' };
      const r1 = build(doc([plate(), mo]));
      const r = build(doc([plate(), mo, cc([edgeRef(r1.part, 'fl.0:side1')])]));
      expect(checkClosed(r.mesh.indices).bad).toBe(0);
      expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
      expect(r.bounds.max.x).toBeCloseTo(100 + T, 6);
    });
  });

  it('butt with material inside trims the covering flange back to the other one\'s inner face', () => {
    const r1 = build(doc([plate(), twoFlanges]));
    const r = build(doc([plate(), twoFlanges, cc([edgeRef(r1.part, 'fl.0:side1')], 'butt')]));
    expect(checkClosed(r.mesh.indices).bad).toBe(0);
    // first flange loses T + gap, second one gains 4 − T − gap
    expect(r.fp.area - r1.fp.area).toBeCloseTo((-(T + 0.1) + (4 - T - 0.1)) * lc, 6);
  });

  it('refuses an edge without a neighbouring flange', () => {
    const r1 = build(doc([plate(), twoFlanges]));
    const res = regenerate(doc([plate(), twoFlanges, cc([edgeRef(r1.part, 'fl.1:end')])]));
    expect(res.status.get('cc')?.error).toMatch(/keine benachbarte Lasche/);
  });
});

/** Order-independent fingerprint of the folded vertex positions. */
function positionSums(p: Float64Array): [number, number, number] {
  const s: [number, number, number] = [0, 0, 0];
  for (let i = 0; i < p.length; i += 3) for (let k = 0; k < 3; k++) s[k] += p[i + k];
  return s;
}

describe('mesh of plain bend strips', () => {
  // recorded with the rectangle-only strip mesher before end profiles existed
  it.each([
    ['box', () => doc([plate(), twoFlanges]), 168, 332, 69, 17797.50400733698, [12940.486820126367, 578.7829496840768, -2135.2697698104457]],
    ['box, bend outside', () => doc([plate(), { ...twoFlanges, position: 'bendOutside' }]), 136, 268, 68, 19211.134254113313, [10132.486820126369, 511.02635974726127, -1667.5131798736306]],
    ['example part', () => exampleDoc(), 560, 1128, 280, 73938.877617451, [52140.48682012636, 9753.709272952976, -33348]],
  ] as const)('%s keeps its mesh', (_, make, verts, tris, boundary, volume, sums) => {
    const r = build(make());
    expect(r.mesh.positions.length / 3).toBe(verts);
    expect(r.mesh.indices.length / 3).toBe(tris);
    expect(r.mesh.boundary.length).toBe(boundary);
    expect(r.volume).toBeCloseTo(volume, 6);
    positionSums(r.mesh.positions).forEach((s, k) => expect(s).toBeCloseTo(sums[k], 6));
  });
});

describe('Geschlossene Ecke: Biegebereich', () => {
  type Position = EdgeFlangeFeature['position'];
  type Kind = NonNullable<ClosedCornerFeature['kind']>;
  const positions: Position[] = ['materialInside', 'materialOutside', 'bendOutside', 'virtualSharp'];
  const kinds: Kind[] = ['butt', 'overlap', 'underlap'];

  const closed = (part: Part, sideTag: string, kind: Kind, gap: number, more: Partial<ClosedCornerFeature> = {}): ClosedCornerFeature => ({
    id: 'cc',
    name: 'Geschlossene Ecke1',
    type: 'closedCorner',
    edges: [edgeRef(part, sideTag)],
    gap,
    kind,
    ...more,
  });

  const boxDoc = (position: Position, kind: Kind, gap: number, more: Partial<ClosedCornerFeature> = {}, flange: Partial<EdgeFlangeFeature> = {}): Doc => {
    const fl: EdgeFlangeFeature = { ...twoFlanges, position, ...flange };
    const r1 = build(doc([plate(), fl]));
    return doc([plate(), fl, closed(r1.part, 'fl.0:side1', kind, gap, more)]);
  };

  type End = 'ext0' | 'ext1';

  /**
   * Mitre plane between the `ends[0]` end of bend a and the `ends[1]` end of
   * bend b, computed from their common web face independently of the kernel.
   */
  function mitre(part: Part, a: string, b: string, ends: [End, End] = ['ext1', 'ext0']): { p0: V3; n: V3 } {
    const ba = part.bends.get(a)!;
    const bb = part.bends.get(b)!;
    const web = [ba.faceA, ba.faceB].find((f) => f === bb.faceA || f === bb.faceB)!;
    const seam = (bend: Bend): string => `seam:${bend.id}:${bend.faceA === web ? 'A' : 'B'}`;
    const ring = part.faces.get(web)!.outer;
    const n = ring.pts.length;
    const around = (k: number): string[] => [ring.tags[(k - 1 + n) % n], ring.tags[k]];
    const i = ring.pts.findIndex((_, k) => around(k).includes(seam(ba)) && around(k).includes(seam(bb)));
    expect(i).toBeGreaterThanOrEqual(0);
    const P = computePlacements(part);
    const axis = (bend: Bend, end: End): V3 => {
      const s = end === 'ext1' ? 1 : -1;
      return applyDirA(P.get(bend.faceA)!, v3(s * bend.dir.x, s * bend.dir.y, 0));
    };
    return { p0: applyA(P.get(web)!, v3(ring.pts[i].x, ring.pts[i].y, 0)), n: norm3(sub3(axis(ba, ends[0]), axis(bb, ends[1]))) };
  }

  /** Signed distances of all vertices of a bend's mesh piece from the mitre plane: [min, max]. */
  function pieceSpan(mesh: PartMesh, bendId: string, m: { p0: V3; n: V3 }): [number, number] {
    const piece = mesh.pieces.findIndex((p) => p.kind === 'bend' && p.id === bendId);
    let lo = Infinity;
    let hi = -Infinity;
    for (let t = 0; t < mesh.triPiece.length; t++) {
      if (mesh.triPiece[t] !== piece) continue;
      for (let k = 0; k < 3; k++) {
        const i = mesh.indices[3 * t + k];
        const s = dot3(m.n, sub3(v3(mesh.positions[3 * i], mesh.positions[3 * i + 1], mesh.positions[3 * i + 2]), m.p0));
        lo = Math.min(lo, s);
        hi = Math.max(hi, s);
      }
    }
    return [lo, hi];
  }

  /** ∫₀¹ of an end profile (Simpson). */
  function profileIntegral(b: Bend, end: End): number {
    const n = 2000;
    let s = 0;
    for (let i = 0; i <= n; i++) s += (i === 0 || i === n ? 1 : i % 2 ? 4 : 2) * endOffset(b[end], b.angle, i / n);
    return s / (3 * n);
  }

  /** Total variation of an end profile over [0, 1]. */
  function profileVariation(b: Bend, end: End): number {
    let s = 0;
    for (let i = 1; i <= 1000; i++) s += Math.abs(endOffset(b[end], b.angle, i / 1000) - endOffset(b[end], b.angle, (i - 1) / 1000));
    return s;
  }

  const overlapArea = (a: V2[], b: V2[]): number => {
    try {
      return polygonClipping
        .intersection([a.map((p) => [p.x, p.y] as [number, number])], [b.map((p) => [p.x, p.y] as [number, number])])
        .reduce((s, poly) => s + poly.reduce((t, r, i) => t + (i === 0 ? 1 : -1) * Math.abs(signedArea(r.map(([x, y]) => v2(x, y)))), 0), 0);
    } catch {
      return gridOverlap(a, b);
    }
  };

  /** No strip outline overlaps another strip or any face, and the flat outline is one simple loop. */
  function expectFlatClean(r: ReturnType<typeof build>, bendIds: string[]): void {
    const outer = r.fp.loops.filter((l) => l.outer);
    expect(outer).toHaveLength(1);
    expect(isSimple(outer[0].pts)).toBe(true);
    for (const id of bendIds) {
      const mine = stripOutline(r.part.bends.get(id)!);
      for (const f of r.part.faces.values()) expect(overlapArea(mine, f.outer.pts)).toBeLessThan(1e-4);
      for (const other of r.part.bends.values()) if (other.id !== id) expect(overlapArea(mine, stripOutline(other))).toBeLessThan(1e-4);
    }
  }

  /** Mesh closed, mitre separation, clean flat pattern. */
  function expectClosedCorner(r: ReturnType<typeof build>, a: string, b: string, gap: number, ends?: [End, End]): void {
    expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
    const m = mitre(r.part, a, b, ends);
    expect(pieceSpan(r.mesh, a, m)[1]).toBeLessThanOrEqual(-gap / 2 + 1e-6);
    expect(pieceSpan(r.mesh, b, m)[0]).toBeGreaterThanOrEqual(gap / 2 - 1e-6);
    expectFlatClean(r, [a, b]);
  }

  for (const position of positions) {
    for (const kind of kinds) {
      for (const gap of [0, 0.5]) {
        it(`${position}, ${kind}, gap ${gap}: both bends run into the corner up to the mitre`, () => {
          const r = build(boxDoc(position, kind, gap));
          const open = build(boxDoc(position, kind, gap, { openBend: true }));
          const a = r.part.bends.get('fl.0')!;
          const b = r.part.bends.get('fl.1')!;
          expect(a.ext1).toBeDefined();
          expect(b.ext0).toBeDefined();
          expect(a.ext0).toBeUndefined();
          expect(b.ext1).toBeUndefined();
          expectClosedCorner(r, 'fl.0', 'fl.1', gap);
          // the strips change the flat area by their end profiles (sampled at the mesh rows) …
          const grow = a.ba * (profileIntegral(a, 'ext1') + profileIntegral(b, 'ext0'));
          expect(r.fp.area - open.fp.area).toBeCloseTo(grow, 1);
          // … and the volume by T times that, less the chord error of the 6° slices; the
          // triangulated (twisted) side walls of a mitred end add up to T²·Δφ/12 per mm of profile change
          const x = a.angle / 15;
          const twist = ((T * T * x) / 12) * (profileVariation(a, 'ext1') + profileVariation(b, 'ext0'));
          expect(Math.abs(r.volume - open.volume - (T * grow * Math.sin(x)) / x)).toBeLessThan(0.02 + twist);
          // only bend outside starts both strips at the web corner; otherwise the first
          // flange's bend runs past it and is trimmed back to the mitre
          if (position === 'bendOutside') expect(r.fp.area).toBeGreaterThan(open.fp.area + 1);
        });
      }
    }
  }

  it('bend outside: the inner fibre binds, d(f) = R·sin φ − gap/√2, capped by the flange', () => {
    const R = 2;
    for (const gap of [0, 0.5]) {
      const { part } = build(boxDoc('bendOutside', 'butt', gap));
      for (const [id, end] of [
        ['fl.0', 'ext1'],
        ['fl.1', 'ext0'],
      ] as const) {
        const b = part.bends.get(id)!;
        for (const f of [0, 0.2, 0.5, 0.7]) expect(endOffset(b[end], b.angle, f)).toBeCloseTo(R * Math.sin(b.angle * f) - gap / Math.SQRT2, 9);
        // butt: each flange runs R − gap to the other one's inner face
        expect(endOffset(b[end], b.angle, 1)).toBeCloseTo(Math.min(R - gap / Math.SQRT2, R - gap), 9);
        // pieces cover [0, 1] and join continuously
        const ps = b[end]!.pieces;
        expect(ps[0].f0).toBe(0);
        expect(ps[ps.length - 1].f1).toBe(1);
        for (let i = 1; i < ps.length; i++) {
          expect(ps[i].f0).toBe(ps[i - 1].f1);
          const phi = b.angle * ps[i].f0;
          const val = (p: (typeof ps)[number]): number => p.a + p.b * Math.sin(phi) + p.c * Math.cos(phi);
          expect(val(ps[i])).toBeCloseTo(val(ps[i - 1]), 9);
        }
      }
    }
  });

  it('the end profile is exact: strip points at the profile lie on the mitre plane', () => {
    const gap = 0.5;
    const { part } = build(boxDoc('bendOutside', 'overlap', gap));
    const m = mitre(part, 'fl.0', 'fl.1');
    const M = computePlacements(part).get('base')!;
    const a = part.bends.get('fl.0')!;
    // below the cap the inner fibre (h = T for a bend up) touches the plane at −gap/2
    for (const f of [0.1, 0.33, 0.5, 0.8]) {
      const p = applyA(M, stripPoint(a, T, stripRange(a, f)[1], f, T));
      expect(dot3(m.n, sub3(p, m.p0))).toBeCloseTo(-gap / 2, 9);
    }
  });

  const oldVolumes: [Position, Kind, number, number][] = [
    ['materialInside', 'butt', 0, 17797.504007336986],
    ['materialInside', 'butt', 0.5, 17745.504007336986],
    ['materialInside', 'overlap', 0, 17901.50400733698],
    ['materialInside', 'overlap', 0.5, 17875.50400733698],
    ['materialInside', 'underlap', 0, 17901.504007336986],
    ['materialInside', 'underlap', 0.5, 17875.504007336986],
    ['materialOutside', 'butt', 0, 18600.319130725165],
    ['materialOutside', 'butt', 0.5, 18548.319130725158],
    ['materialOutside', 'overlap', 0, 18704.319130725165],
    ['materialOutside', 'overlap', 0.5, 18678.31913072516],
    ['materialOutside', 'underlap', 0, 18704.319130725165],
    ['materialOutside', 'underlap', 0.5, 18678.31913072516],
    ['bendOutside', 'butt', 0, 19419.134254113316],
    ['bendOutside', 'butt', 0.5, 19367.134254113316],
    ['bendOutside', 'overlap', 0, 19523.134254113316],
    ['bendOutside', 'overlap', 0.5, 19497.134254113316],
    ['bendOutside', 'underlap', 0, 19523.134254113316],
    ['bendOutside', 'underlap', 0.5, 19497.134254113316],
    ['virtualSharp', 'butt', 0, 17797.504007336986],
    ['virtualSharp', 'butt', 0.5, 17745.504007336986],
    ['virtualSharp', 'overlap', 0, 17901.50400733698],
    ['virtualSharp', 'overlap', 0.5, 17875.50400733698],
    ['virtualSharp', 'underlap', 0, 17901.504007336986],
    ['virtualSharp', 'underlap', 0.5, 17875.504007336986],
  ];

  it('open bend region: no end profiles and the volume of the flange-only closed corner', () => {
    for (const [position, kind, gap, volume] of oldVolumes) {
      const r = build(boxDoc(position, kind, gap, { openBend: true }));
      for (const b of r.part.bends.values()) {
        expect(b.ext0).toBeUndefined();
        expect(b.ext1).toBeUndefined();
      }
      expect(r.volume).toBeCloseTo(volume, 6);
    }
  });

  it('bends down close the same way', () => {
    for (const position of positions) {
      for (const kind of kinds) {
        const r = build(boxDoc(position, kind, 0.5, {}, { reverse: true }));
        expect(r.part.bends.get('fl.0')!.up).toBe(false);
        expect(r.part.bends.get('fl.0')!.ext1).toBeDefined();
        expectClosedCorner(r, 'fl.0', 'fl.1', 0.5);
      }
    }
  });

  it('120° flanges: refused with a German message by the corner search', () => {
    for (const kind of kinds) {
      const d = boxDoc('bendOutside', kind, 0.5, {}, { angle: 120 });
      const st = regenerate(d).status.get('cc')!;
      if (st.ok) expectClosedCorner(build(d), 'fl.0', 'fl.1', 0.5);
      else expect(st.error).toMatch(/keine benachbarte Lasche/);
    }
  });

  it('a 120° plate corner closes along the bisector', () => {
    const h = 25 * Math.sqrt(3);
    const obtuse: Feature = { ...plate(), points: [v2(0, 0), v2(100, 0), v2(125, h), v2(0, h)] } as Feature;
    const rb = build(doc([obtuse]));
    const fl: EdgeFlangeFeature = {
      ...twoFlanges,
      position: 'bendOutside',
      edges: [
        { ...edgeRef(rb.part, 'base:e0'), side: 'top' },
        { ...edgeRef(rb.part, 'base:e1'), side: 'top' },
      ],
    };
    const r1 = build(doc([obtuse, fl]));
    for (const kind of kinds) {
      const r = build(doc([obtuse, fl, closed(r1.part, 'fl.0:side1', kind, 0.5)]));
      expectClosedCorner(r, 'fl.0', 'fl.1', 0.5);
      // the mitre halves the 120° corner: R·sin φ·tan 30° − (gap/2)/cos 30°, unless the flange stops earlier
      const a = r.part.bends.get('fl.0')!;
      const mid = 2 * Math.sin(Math.PI / 4) * Math.tan(Math.PI / 6) - 0.25 / Math.cos(Math.PI / 6);
      if (kind === 'overlap') expect(endOffset(a.ext1, a.angle, 0.5)).toBeCloseTo(mid, 9);
      else expect(endOffset(a.ext1, a.angle, 0.5)).toBeLessThanOrEqual(mid + 1e-9);
    }
  });

  it('two flange features with different radii close, or keep the bend region open when a bend would run into the other flange', () => {
    const first: EdgeFlangeFeature = { ...twoFlanges, id: 'f1', edges: [twoFlanges.edges[0]] };
    let closedCount = 0;
    let keptOpen = 0;
    for (const radius of [1, 5]) {
      for (const position of ['materialInside', 'bendOutside'] as const) {
        const f1 = { ...first, position };
        const rb = build(doc([plate(), f1]));
        const f2: EdgeFlangeFeature = { ...twoFlanges, id: 'f2', position, edges: [{ ...edgeRef(rb.part, 'base:e1'), side: 'top' }], overrides: { radius } };
        const r1 = build(doc([plate(), f1, f2]));
        for (const kind of kinds) {
          for (const side of ['f1:side1', 'f2:side0']) {
            const d = doc([plate(), f1, f2, closed(r1.part, side, kind, 0.5)]);
            expect(regenerate(d).status.get('cc')!.ok).toBe(true);
            const r = build(d);
            if (!r.part.bends.get('f1')!.ext1) {
              // the flanges still close; only the bend region stays open, as with openBend
              expect(r.part.bends.get('f2')!.ext0).toBeUndefined();
              const open = build(doc([plate(), f1, f2, { ...closed(r1.part, side, kind, 0.5), openBend: true }]));
              expect(r.fp.area).toBeCloseTo(open.fp.area, 9);
              expect(checkClosed(r.mesh.indices).bad).toBe(0);
              keptOpen++;
              continue;
            }
            expect(r.part.bends.get('f1')!.ext1).toBeDefined();
            expect(r.part.bends.get('f2')!.ext0).toBeDefined();
            expectClosedCorner(r, 'f1', 'f2', 0.5);
            closedCount++;
          }
        }
      }
    }
    expect(closedCount).toBeGreaterThan(0);
    expect(keptOpen).toBeGreaterThan(0);
  });

  it('a box with four flanges closes all corners in one feature', () => {
    for (const position of ['materialInside', 'bendOutside'] as const) {
      const four: EdgeFlangeFeature = {
        ...twoFlanges,
        position,
        edges: [
          ...twoFlanges.edges,
          { tag: 'base:e2', at: { x: 50, y: 50 }, side: 'top' },
          { tag: 'base:e3', at: { x: 0, y: 25 }, side: 'top' },
        ],
      };
      const r1 = build(doc([plate(), four]));
      const all: ClosedCornerFeature = { ...closed(r1.part, 'fl.0:side1', 'overlap', 0.3), edges: [0, 1, 2, 3].map((i) => edgeRef(r1.part, `fl.${i}:side1`)) };
      const r = build(doc([plate(), four, all]));
      for (const b of r.part.bends.values()) {
        expect(b.ext0).toBeDefined();
        expect(b.ext1).toBeDefined();
      }
      for (let i = 0; i < 4; i++) expectClosedCorner(r, `fl.${i}`, `fl.${(i + 1) % 4}`, 0.3);
    }
  });

  it('the wall of an open profile and an end flange: the flange face is face A of the wall bend', () => {
    const u: Feature = { id: 'base', name: 'Basislasche1', type: 'baseFlange', plane: 'front', points: [v2(0, 40), v2(0, 0), v2(100, 0), v2(100, 40)], closed: false, reverse: true, depth: 60, midPlane: false };
    const r0 = build(doc([u]));
    const fl: EdgeFlangeFeature = { ...twoFlanges, position: 'bendOutside', edges: [{ ...edgeRef(r0.part, 'base:s1a'), side: 'top' }] };
    const r1 = build(doc([u, fl]));
    for (const kind of kinds) {
      for (const gap of [0, 0.5]) {
        const r = build(doc([u, fl, closed(r1.part, 'fl:side0', kind, gap)]));
        const wall = r.part.bends.get('base.b1')!;
        // the wall is face A of its bend and the bottom (the web) face B
        expect([wall.faceA, wall.faceB]).toEqual(['base', 'base.f1']);
        expect(wall.ext0).toBeDefined();
        expect(r.part.bends.get('fl')!.ext0).toBeDefined();
        expectClosedCorner(r, 'base.b1', 'fl', gap, ['ext0', 'ext0']);
      }
    }
  });

  it('a partial-width flange with reliefs away from the corner gets no profile', () => {
    const fa: EdgeFlangeFeature = { ...twoFlanges, id: 'fa', position: 'bendOutside', edges: [twoFlanges.edges[0]] };
    const rb = build(doc([plate(), fa]));
    const fb: EdgeFlangeFeature = {
      ...twoFlanges,
      id: 'fb',
      position: 'bendOutside',
      edges: [{ ...edgeRef(rb.part, 'base:e1'), side: 'top' }],
      width: { mode: 'offset', offset1: 3, offset2: 5, width: 0 },
    };
    const r1 = build(doc([plate(), fa, fb]));
    const r = build(doc([plate(), fa, fb, closed(r1.part, 'fa:side1', 'overlap', 0.5)]));
    for (const b of r.part.bends.values()) {
      expect(b.ext0).toBeUndefined();
      expect(b.ext1).toBeUndefined();
    }
    expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
  });

  it('closing the bend region of a corner whose flanges already meet is a change; doing it again is not', () => {
    const fl: EdgeFlangeFeature = { ...twoFlanges, position: 'bendOutside' };
    const r1 = build(doc([plate(), fl]));
    const first = closed(r1.part, 'fl.0:side1', 'overlap', 0.5, { openBend: true });
    const r2 = build(doc([plate(), fl, first]));
    const second = { ...closed(r2.part, 'fl.0:side1', 'overlap', 0.5), id: 'cc2' };
    const r3 = build(doc([plate(), fl, first, second]));
    const single = build(boxDoc('bendOutside', 'overlap', 0.5));
    for (const f of [0, 0.3, 0.6, 1]) {
      const a = r3.part.bends.get('fl.0')!;
      expect(endOffset(a.ext1, a.angle, f)).toBeCloseTo(endOffset(single.part.bends.get('fl.0')!.ext1, a.angle, f), 9);
    }
    expect(r3.volume).toBeCloseTo(single.volume, 6);
    const third = { ...closed(r3.part, 'fl.0:side1', 'overlap', 0.5), id: 'cc3' };
    expect(regenerate(doc([plate(), fl, first, second, third])).status.get('cc3')?.error).toMatch(/bereits geschlossen/);
  });

  it('a second closed corner on the same end replaces the profile', () => {
    const fl: EdgeFlangeFeature = { ...twoFlanges, position: 'bendOutside' };
    const r1 = build(doc([plate(), fl]));
    const first = closed(r1.part, 'fl.0:side1', 'butt', 0.5);
    const r2 = build(doc([plate(), fl, first]));
    const second = { ...closed(r2.part, 'fl.0:side1', 'butt', 0.2), id: 'cc2' };
    const r = build(doc([plate(), fl, first, second]));
    const a = r.part.bends.get('fl.0')!;
    const b = r.part.bends.get('fl.1')!;
    expect(endOffset(a.ext1, a.angle, 0)).toBeCloseTo(-0.2 / Math.SQRT2, 9);
    expect(endOffset(b.ext0, b.angle, 0.5)).toBeCloseTo(2 * Math.sin(Math.PI / 4) - 0.2 / Math.SQRT2, 9);
    expectClosedCorner(r, 'fl.0', 'fl.1', 0.2);
  });

  it('regenerating twice and a JSON round trip give the same part', () => {
    const d = boxDoc('bendOutside', 'overlap', 0.5);
    expect(JSON.stringify(d)).not.toContain('openBend');
    const r1 = build(d);
    const r2 = build(d);
    const r3 = build(parseDoc(JSON.stringify(d)));
    for (const r of [r2, r3]) {
      expect([...r.part.bends.values()]).toEqual([...r1.part.bends.values()]);
      expect(Array.from(r.mesh.positions)).toEqual(Array.from(r1.mesh.positions));
      expect(Array.from(r.mesh.indices)).toEqual(Array.from(r1.mesh.indices));
    }
    expect(r1.part.bends.get('fl.0')!.ext1).toBeDefined();
  });

  it('flat bend lines and measured bends use the strip at mid-bend', () => {
    const { part, fp } = build(boxDoc('bendOutside', 'overlap', 0.5));
    const a = part.bends.get('fl.0')!;
    const [wl, wr] = stripRange(a, 0.5);
    expect(fp.bendLines.find((l) => l.id === 'fl.0')!.length).toBeCloseTo(wr - wl, 9);
    const cyl = bendEntity(part, 'fl.0', 0, v3(0, 0, 0), false);
    expect(cyl.kind === 'cylinder' && cyl.length).toBeCloseTo(wr - wl, 9);
    const flat = bendEntity(part, 'fl.0', 0, v3(0, 0, 0), true);
    expect(flat.kind === 'plane' && flat.area).toBeCloseTo(Math.abs(signedArea(stripOutline(a))), 9);
    // the mitred end wall is curved; the plain one is still a flat annular sector
    const M = computePlacements(part).get(a.faceA)!;
    const atEnd = (w: number): V3 => applyA(M, stripPoint(a, T, w, 0.5, T / 2));
    expect(() => bendEntity(part, 'fl.0', 2, atEnd(wr), false)).toThrow(/Stirnseite dieser Biegung ist gekrümmt/);
    const plain = bendEntity(part, 'fl.0', 2, atEnd(a.w0), false);
    expect(plain.kind).toBe('plane');
  });
});

describe('Kantenlasche an Plattenecken', () => {
  const P = (x: number, y: number): V2 => v2(x, y);
  const hexagon = [0, 1, 2, 3, 4, 5].map((i) => P(60 + 50 * Math.cos((i * Math.PI) / 3), 50 + 50 * Math.sin((i * Math.PI) / 3)));
  const trapezoid = [P(0, 0), P(120, 0), P(95, 60), P(25, 60)];
  const rectangle = [P(0, 0), P(100, 0), P(100, 50), P(0, 50)];

  function trayDoc(points: V2[], over: Partial<EdgeFlangeFeature> = {}, count = points.length): Doc {
    const base = { ...plate(), points } as Feature;
    const r0 = build(doc([base]));
    const f: EdgeFlangeFeature = {
      id: 'fl',
      name: 'Kantenlasche1',
      type: 'edgeFlange',
      edges: Array.from({ length: count }, (_, i) => edgeRef(r0.part, `base:e${i}`)),
      angle: 90,
      length: 30,
      lengthRef: 'outer',
      position: 'materialInside',
      reverse: false,
      width: { mode: 'full', offset1: 0, offset2: 0, width: 0 },
      miterGap: 0.25,
      ...over,
    };
    return doc([base, f]);
  }

  /** Points along the outline of a face on both sheet surfaces and up its corners, in model space. */
  function facePoints(part: Part, id: string): V3[] {
    const M = computePlacements(part).get(id)!;
    const pts = part.faces.get(id)!.outer.pts;
    const out: V3[] = [];
    pts.forEach((a, i) => {
      const b = pts[(i + 1) % pts.length];
      for (let k = 0; k < 40; k++) for (const z of [0, T]) out.push(applyA(M, v3(a.x + ((b.x - a.x) * k) / 40, a.y + ((b.y - a.y) * k) / 40, z)));
      for (let k = 1; k < 8; k++) out.push(applyA(M, v3(a.x, a.y, (T * k) / 8)));
    });
    return out;
  }

  /** Distance from p to the sheet of a face, negative inside. */
  function prismDistance(part: Part, id: string, p: V3): number {
    const face = part.faces.get(id)!;
    const l = applyA(invertRigid(computePlacements(part).get(id)!), p);
    const q = v2(l.x, l.y);
    const pts = face.outer.pts;
    const edge = Math.min(...pts.map((a, i) => distPointSegment(q, a, pts[(i + 1) % pts.length])));
    const inPlane = pointInPolygon(q, pts);
    const dz = Math.max(0, -l.z, l.z - T);
    return inPlane && dz === 0 ? -Math.min(edge, l.z, T - l.z) : Math.hypot(inPlane ? 0 : edge, dz);
  }

  /** Smallest distance between two flange faces, sampled both ways. */
  const clearance = (part: Part, a: string, b: string): number =>
    Math.min(...facePoints(part, a).map((p) => prismDistance(part, b, p)), ...facePoints(part, b).map((p) => prismDistance(part, a, p)));

  function flatOverlap(part: Part): number {
    const pieces = [...[...part.faces.values()].map((f) => f.outer.pts), ...[...part.bends.values()].map((b) => stripOutline(b))];
    let total = 0;
    for (let i = 0; i < pieces.length; i++) {
      for (let j = i + 1; j < pieces.length; j++) {
        try {
          total += polygonClipping
            .intersection([pieces[i].map((p) => [p.x, p.y] as [number, number])], [pieces[j].map((p) => [p.x, p.y] as [number, number])])
            .reduce((s, poly) => s + poly.reduce((t, r, k) => t + (k === 0 ? 1 : -1) * Math.abs(signedArea(r.map(([x, y]) => v2(x, y)))), 0), 0);
        } catch {
          total += gridOverlap(pieces[i], pieces[j]);
        }
      }
    }
    return total;
  }

  /** Side edges of the flanges that differ from the open corners. */
  function movedSides(r: Part, open: Part): number {
    let n = 0;
    for (const face of r.faces.values()) {
      if (!face.id.startsWith('fl.')) continue;
      for (const s of ['side0', 'side1']) {
        const key = (p: Part): string => {
          const f = p.faces.get(face.id)!;
          return JSON.stringify(f.outer.pts.filter((_, i) => f.outer.tags[i] === `${face.id}:${s}`).map((q) => [q.x.toFixed(6), q.y.toFixed(6)]));
        };
        if (key(r) !== key(open)) n++;
      }
    }
    return n;
  }

  const positions = ['materialInside', 'materialOutside', 'bendOutside', 'virtualSharp'] as const;
  for (const position of positions) {
    for (const angle of [60, 90, 120]) {
      it(`mitres all four tray corners (${position}, ${angle}°)`, () => {
        const d = trayDoc(rectangle, { position, angle });
        const r = build(d);
        const open = build(trayDoc(rectangle, { position, angle, miterGap: undefined }));
        expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
        expect(movedSides(r.part, open.part)).toBe(8);
        // every bend end at a corner meets its neighbour on the mitre
        expect([...r.part.bends.values()].every((b) => b.ext0 && b.ext1)).toBe(true);
        for (let i = 0; i < 4; i++) expect(clearance(r.part, `fl.${i}`, `fl.${(i + 1) % 4}`)).toBeGreaterThanOrEqual(0.25 - 1e-6);
        expect(flatOverlap(r.part)).toBeLessThan(1e-4);
      });
    }
  }

  it('keeps exactly the gap between the flanges', () => {
    for (const gap of [0.25, 1]) {
      const r = build(trayDoc(rectangle, { miterGap: gap }));
      expect(clearance(r.part, 'fl.0', 'fl.1')).toBeCloseTo(gap, 6);
    }
  });

  it('trims flanges leaning inwards that would run into each other', () => {
    const open = build(trayDoc(rectangle, { angle: 120, miterGap: undefined }));
    expect(clearance(open.part, 'fl.0', 'fl.1')).toBeLessThan(-0.5);
    const r = build(trayDoc(rectangle, { angle: 120 }));
    expect(clearance(r.part, 'fl.0', 'fl.1')).toBeCloseTo(0.25, 6);
    expect(r.volume).toBeLessThan(open.volume);
  });

  it('mitres oblique plate corners', () => {
    for (const [points, n] of [
      [hexagon, 6],
      [trapezoid, 4],
    ] as const) {
      const open = build(trayDoc(points, { miterGap: undefined }));
      const r = build(trayDoc(points));
      expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
      expect(movedSides(r.part, open.part)).toBe(2 * n);
      for (let i = 0; i < n; i++) expect(clearance(r.part, `fl.${i}`, `fl.${(i + 1) % n}`)).toBeGreaterThanOrEqual(0.25 - 1e-6);
      expect(flatOverlap(open.part)).toBeLessThan(1e-4);
      expect(flatOverlap(r.part)).toBeLessThan(1e-4);
    }
  });

  it('only touches corners between two selected edges', () => {
    const two = build(trayDoc(rectangle, {}, 2));
    const twoOpen = build(trayDoc(rectangle, { miterGap: undefined }, 2));
    expect(movedSides(two.part, twoOpen.part)).toBe(2);
    const opposite = trayDoc(rectangle);
    const f = opposite.features[1] as EdgeFlangeFeature;
    f.edges = [f.edges[0], f.edges[2]];
    const r = build(opposite);
    const plain = build({ ...opposite, features: [opposite.features[0], { ...f, miterGap: undefined }] });
    expect(Array.from(r.mesh.positions)).toEqual(Array.from(plain.mesh.positions));
  });

  it('leaves documents without the option unchanged and rejects a negative gap', () => {
    const r = build(trayDoc(rectangle, { miterGap: undefined }));
    expect([...r.part.bends.values()].some((b) => b.ext0 || b.ext1)).toBe(false);
    const bad = regenerate(trayDoc(rectangle, { miterGap: -1 }));
    expect(bad.status.get('fl')).toEqual({ ok: false, error: 'Der Spalt an den Ecken darf nicht negativ sein.' });
  });
  const octagon = [0, 1, 2, 3, 4, 5, 6, 7].map((i) => P(60 + 50 * Math.cos((i * Math.PI) / 4), 50 + 50 * Math.sin((i * Math.PI) / 4)));
  const triangle = [P(0, 0), P(120, 0), P(60, 60 * Math.sqrt(3))];

  /**
   * The plane through the web corner where `later` starts or ends, perpendicular
   * to its bend line, with the normal pointing along it, in model space.
   */
  function startPlane(part: Part, later: string, end: 0 | 1): { p0: V3; n: V3 } {
    const b = part.bends.get(later)!;
    const M = computePlacements(part).get(b.faceA)!;
    const w = end === 0 ? b.w0 : b.w1;
    const d = end === 0 ? b.dir : v2(-b.dir.x, -b.dir.y);
    return { p0: applyA(M, v3(b.origin.x + b.dir.x * w, b.origin.y + b.dir.y * w, 0)), n: applyDirA(M, v3(d.x, d.y, 0)) };
  }

  /** Largest n·(p − p0) over the corner end of bend `id` (end 0 or 1) and the part of its flange beyond `p0`. */
  function reach(part: Part, id: string, end: 0 | 1, plane: { p0: V3; n: V3 }): number {
    const b = part.bends.get(id)!;
    const pl = computePlacements(part);
    const MA = pl.get(b.faceA)!;
    let max = -Infinity;
    for (let i = 0; i <= 64; i++) {
      const [wl, wr] = stripRange(b, i / 64);
      for (const h of [0, T]) max = Math.max(max, dot3(plane.n, sub3(applyA(MA, stripPoint(b, T, end === 0 ? wl : wr, i / 64, h)), plane.p0)));
    }
    const MB = pl.get(b.faceB!)!;
    const at = (q: V2): number => (q.x - b.origin.x) * b.dir.x + (q.y - b.origin.y) * b.dir.y;
    const corner = applyA(invertRigid(MA), plane.p0);
    const wc = at(v2(corner.x, corner.y));
    for (const q of part.faces.get(b.faceB!)!.outer.pts) {
      if ((end === 1 ? at(q) - wc : wc - at(q)) < 0) continue;
      for (const z of [0, T]) max = Math.max(max, dot3(plane.n, sub3(applyA(MB, v3(q.x, q.y, z)), plane.p0)));
    }
    return max;
  }

  for (const [name, points] of [
    ['hexagon', hexagon],
    ['octagon', octagon],
    ['trapezoid', trapezoid],
  ] as const) {
    for (const position of ['materialInside', 'materialOutside', 'virtualSharp'] as const) {
      for (const angle of [60, 90]) {
        it(`cuts the older flange back at obtuse corners of a ${name} (${position}, ${angle}°)`, () => {
          const n = points.length;
          const r = build(trayDoc(points, { position, angle, miterGap: undefined }));
          expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
          expect(flatOverlap(r.part)).toBeLessThan(1e-4);
          // each later flange starts at the corner with the one before; the last one also ends at the first
          const corners: [string, 0 | 1, string, 0 | 1][] = Array.from({ length: n - 1 }, (_, i) => [`fl.${i}`, 1, `fl.${i + 1}`, 0] as [string, 0 | 1, string, 0 | 1]);
          corners.push(['fl.0', 0, `fl.${n - 1}`, 1]);
          let cut = 0;
          for (const [older, olderEnd, later, laterEnd] of corners) {
            const plane = startPlane(r.part, later, laterEnd);
            const b = r.part.bends.get(older)!;
            const turn = Math.abs(b.dir.x * r.part.bends.get(later)!.dir.x + b.dir.y * r.part.bends.get(later)!.dir.y);
            if (b[olderEnd === 0 ? 'ext0' : 'ext1']) cut++;
            // obtuse corners: the older bend and flange stay behind the later bend's end plane
            if (turn > 1e-6 && (olderEnd === 1 ? 1 : -1) * (b.dir.x * plane.n.x + b.dir.y * plane.n.y) > 0) {
              expect(reach(r.part, older, olderEnd, plane)).toBeLessThanOrEqual(1e-9);
            }
          }
          expect(cut).toBe(name === 'trapezoid' ? 2 : n);
        });
      }
    }
  }

  it('keeps the flat pattern clear at obtuse corners with a short bend allowance (BA, BD)', () => {
    for (const calc of [
      { method: 'ba' as const, kFactor: 0.5, ba: 3, bd: 0 },
      { method: 'bd' as const, kFactor: 0.5, ba: 0, bd: 3 },
    ]) {
      for (const radius of [2, 6]) {
        const d = trayDoc(hexagon, { miterGap: undefined });
        d.sheet = { ...d.sheet, radius, calc };
        const r = build(d);
        expect(flatOverlap(r.part)).toBeLessThan(1e-4);
        expect(interpenetration(r.part).volume).toBe(0);
        expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
      }
    }
  });

  it('leaves right-angled and acute plate corners as they were', () => {
    for (const points of [rectangle, triangle]) {
      for (const position of ['materialInside', 'bendOutside', 'materialOutside'] as const) {
        const r = build(trayDoc(points, { position, miterGap: undefined }));
        expect([...r.part.bends.values()].some((b) => b.ext0 || b.ext1)).toBe(false);
        expect(flatOverlap(r.part)).toBeLessThan(1e-4);
      }
    }
  });

  it('cuts the older flange when the neighbour comes from another feature', () => {
    const base = { ...plate(), points: hexagon } as Feature;
    const r0 = build(doc([base]));
    for (const [first, second] of [
      ['materialInside', 'materialInside'],
      ['bendOutside', 'materialInside'],
      ['virtualSharp', 'materialOutside'],
    ] as const) {
      for (const order of [
        [0, 1],
        [1, 0],
      ]) {
        const fa: EdgeFlangeFeature = { ...twoFlanges, id: 'fa', position: first, edges: [edgeRef(r0.part, `base:e${order[0]}`)] };
        const r1 = build(doc([base, fa]));
        const fb: EdgeFlangeFeature = { ...twoFlanges, id: 'fb', position: second, edges: [edgeRef(r1.part, `base:e${order[1]}`)] };
        const r = build(doc([base, fa, fb]));
        expect(flatOverlap(r1.part)).toBeLessThan(1e-4);
        expect(flatOverlap(r.part)).toBeLessThan(1e-4);
        expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
        const a = r.part.bends.get('fa')!;
        expect(order[0] === 0 ? a.ext1 : a.ext0).toBeDefined();
        expect(r.part.bends.get('fb')!.ext0 ?? r.part.bends.get('fb')!.ext1).toBeUndefined();
      }
    }
  });

  it('a closed hem before the neighbouring flange is cut back too; one after it is shortened as before', () => {
    const base = { ...plate(), points: hexagon } as Feature;
    const r0 = build(doc([base]));
    const hem = (id: string, tag: string, p: Part): Feature =>
      ({ id, name: id, type: 'hem', edges: [edgeRef(p, tag)], hemType: 'closed', position: 'materialInside', reverse: false, length: 10, gap: 1, angle: 270, radius: 3, width: { mode: 'full', offset1: 0, offset2: 0, width: 0 } }) as Feature;
    const h = hem('ha', 'base:e0', r0.part);
    const r1 = build(doc([base, h]));
    const after = build(doc([base, h, { ...twoFlanges, id: 'fb', edges: [edgeRef(r1.part, 'base:e1')] }]));
    expect(after.part.bends.get('ha')!.ext1).toBeDefined();
    expect(flatOverlap(after.part)).toBeLessThan(1e-4);
    const fa: EdgeFlangeFeature = { ...twoFlanges, id: 'fa', edges: [edgeRef(r0.part, 'base:e0')] };
    const r2 = build(doc([base, fa]));
    const before = build(doc([base, fa, hem('hb', 'base:e1', r2.part)]));
    expect(before.part.bends.get('fa')!.ext1).toBeUndefined();
    const hb = before.part.bends.get('hb')!;
    const alone = build(doc([base, hem('hb', 'base:e1', r0.part)])).part.bends.get('hb')!;
    expect(hb.w1 - hb.w0).toBeLessThan(alone.w1 - alone.w0 - 0.1);
    expect(flatOverlap(before.part)).toBeLessThan(1e-4);
  });

  it('keeps the older flange as it is when the cut would take off a flange on its end', () => {
    const base = { ...plate(), points: hexagon } as Feature;
    const r0 = build(doc([base]));
    const fa: EdgeFlangeFeature = { ...twoFlanges, id: 'fa', edges: [edgeRef(r0.part, 'base:e0')] };
    const r1 = build(doc([base, fa]));
    const fr: EdgeFlangeFeature = { ...twoFlanges, id: 'fr', length: 15, edges: [edgeRef(r1.part, 'fa:end')] };
    const r2 = build(doc([base, fa, fr]));
    const r = build(doc([base, fa, fr, { ...twoFlanges, id: 'fb', edges: [edgeRef(r2.part, 'base:e1')] }]));
    expect(r.part.bends.get('fa')!.ext1).toBeUndefined();
    expect(r.part.faces.get('fa')!.outer).toEqual(r2.part.faces.get('fa')!.outer);
  });

  it('closes an obtuse plate corner without the flanges running into each other', () => {
    const base = { ...plate(), points: hexagon } as Feature;
    const r0 = build(doc([base]));
    const all: EdgeFlangeFeature = { ...twoFlanges, id: 'fl', edges: [0, 1, 2, 3, 4, 5].map((i) => edgeRef(r0.part, `base:e${i}`)) };
    const r1 = build(doc([base, all]));
    for (const kind of ['butt', 'overlap'] as const) {
      const r = build(doc([base, all, { id: 'cc', name: 'cc', type: 'closedCorner', edges: [edgeRef(r1.part, 'fl.0:side1')], gap: 0.2, kind }]));
      expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
      expect(flatOverlap(r.part)).toBeLessThan(1e-4);
      expect(clearance(r.part, 'fl.0', 'fl.1')).toBeGreaterThanOrEqual(0.2 - 1e-6);
    }
  });
  describe('automatic relief of colliding flanges', () => {
    // a 200 × 120 box: walls on all edges, then return flanges on their free edges
    const box = { ...plate(200, 120) } as Feature;
    const walls = (): EdgeFlangeFeature => {
      const r0 = build(doc([box]));
      return { ...twoFlanges, id: 'fl', length: 25, edges: [0, 1, 2, 3].map((i) => edgeRef(r0.part, `base:e${i}`)), miterGap: 0.25 };
    };
    const returns = (side: 'top' | 'bottom', o: Partial<EdgeFlangeFeature> = {}): Feature[] => {
      const fl = walls();
      const r1 = build(doc([box, fl]));
      return [box, fl, { ...twoFlanges, id: 'rf', length: 25, edges: [0, 1, 2, 3].map((i) => edgeRef(r1.part, `fl.${i}:end`, side)), miterGap: 0.25, ...o }];
    };

    it('inward return flanges overlap at the corners when left open', () => {
      const r = build(doc(returns('top', { miterGap: undefined })));
      const pen = interpenetration(r.part);
      expect(pen.volume).toBeGreaterThan(100);
      expect(pen.pairs).toContain('face:rf.0~face:rf.1');
    });

    for (const style of ['mitre', 'straight'] as const) {
      it(`relieves inward return flanges in one plane (${style})`, () => {
        const r = build(doc(returns('top', { cornerStyle: style })));
        expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
        expect(interpenetration(r.part).volume).toBe(0);
        expect(flatOverlap(r.part)).toBeLessThan(1e-4);
        for (let i = 0; i < 4; i++) expect(clearance(r.part, `rf.${i}`, `rf.${(i + 1) % 4}`)).toBeCloseTo(0.25, 6);
        if (style === 'straight') {
          // the first one runs through both corners and keeps its outline
          const open = build(doc(returns('top', { miterGap: undefined })));
          expect(r.part.faces.get('rf.0')!.outer).toEqual(open.part.faces.get('rf.0')!.outer);
          expect(r.part.bends.get('rf.0')!.ext0 ?? r.part.bends.get('rf.0')!.ext1).toBeUndefined();
        }
      });

      it(`closes outward return flanges at the corners (${style})`, () => {
        const open = build(doc(returns('bottom', { miterGap: undefined })));
        const r = build(doc(returns('bottom', { cornerStyle: style })));
        expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
        expect(interpenetration(r.part).volume).toBe(0);
        expect(flatOverlap(r.part)).toBeLessThan(1e-4);
        expect(r.volume).toBeGreaterThan(open.volume);
        for (let i = 0; i < 4; i++) expect(clearance(r.part, `rf.${i}`, `rf.${(i + 1) % 4}`)).toBeCloseTo(0.25, 6);
      });
    }

    it('joins walls straight: one runs through, the other stops the gap before it', () => {
      const fl: EdgeFlangeFeature = { ...walls(), cornerStyle: 'straight' };
      const r = build(doc([box, fl]));
      expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
      expect(interpenetration(r.part).volume).toBe(0);
      expect(flatOverlap(r.part)).toBeLessThan(1e-4);
      for (let i = 0; i < 4; i++) expect(clearance(r.part, `fl.${i}`, `fl.${(i + 1) % 4}`)).toBeCloseTo(0.25, 6);
    });

    it('relieves a flange that runs into a flange of an earlier feature', () => {
      // the four return flanges added one by one
      const fl = walls();
      let feats: Feature[] = [box, fl];
      for (let i = 0; i < 4; i++) {
        const r = build(doc(feats));
        feats = [...feats, { ...twoFlanges, id: `r${i}`, length: 25, edges: [edgeRef(r.part, `fl.${i}:end`, 'top')], miterGap: 0.25 }];
      }
      const r = build(doc(feats));
      expect(checkClosed(r.mesh.indices)).toEqual({ open: 0, bad: 0 });
      expect(interpenetration(r.part).volume).toBe(0);
      expect(flatOverlap(r.part)).toBeLessThan(1e-4);
      for (let i = 0; i < 4; i++) expect(clearance(r.part, `r${i}`, `r${(i + 1) % 4}`)).toBeGreaterThanOrEqual(0.25 - 1e-6);
      // without the option they collide
      const plain = build(doc(feats.map((f) => (f.id.startsWith('r') ? { ...f, miterGap: undefined } : f)) as Feature[]));
      expect(interpenetration(plain.part).volume).toBeGreaterThan(100);
    });

    it('leaves flanges of earlier features alone when they only touch', () => {
      const r0 = build(doc([box]));
      const fa: EdgeFlangeFeature = { ...twoFlanges, id: 'fa', edges: [edgeRef(r0.part, 'base:e0')] };
      const r1 = build(doc([box, fa]));
      const fb: EdgeFlangeFeature = { ...twoFlanges, id: 'fb', edges: [edgeRef(r1.part, 'base:e1')] };
      const plain = build(doc([box, fa, fb]));
      const r = build(doc([box, fa, { ...fb, miterGap: 0.25 }]));
      expect(r.part.faces.get('fa')!.outer).toEqual(plain.part.faces.get('fa')!.outer);
      expect(r.part.faces.get('fb')!.outer).toEqual(plain.part.faces.get('fb')!.outer);
    });
  });
});
