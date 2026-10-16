import { describe, expect, it } from 'vitest';
import { regenerate } from '../src/kernel/doc';
import type { BreakCornerFeature, ClosedCornerFeature, EdgeFlangeFeature, Feature, TabFeature } from '../src/kernel/features/types';
import { build, checkClosed, doc, edgeRef, expectedVolume, plate } from './helpers';

const T = 2;

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
  const cc = (refs: ClosedCornerFeature['edges'], kind?: ClosedCornerFeature['kind']): ClosedCornerFeature => ({
    id: 'cc',
    name: 'Geschlossene Ecke1',
    type: 'closedCorner',
    edges: refs,
    gap: 0.1,
    kind,
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

    it('material outside closes as well', () => {
      const mo: EdgeFlangeFeature = { ...twoFlanges, position: 'materialOutside' };
      const r1 = build(doc([plate(), mo]));
      const r = build(doc([plate(), mo, cc([edgeRef(r1.part, 'fl.0:side1')])]));
      expect(checkClosed(r.mesh.indices).bad).toBe(0);
      expect(r.volume).toBeCloseTo(expectedVolume(r), 6);
      expect(r.bounds.max.x).toBeCloseTo(100 + T, 6);
    });
  });

  it('refuses an edge without a neighbouring flange', () => {
    const r1 = build(doc([plate(), twoFlanges]));
    const res = regenerate(doc([plate(), twoFlanges, cc([edgeRef(r1.part, 'fl.1:end')])]));
    expect(res.status.get('cc')?.error).toMatch(/keine benachbarte Lasche/);
  });
});
