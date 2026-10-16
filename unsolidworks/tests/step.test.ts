import { describe, expect, it } from 'vitest';
import { partToStep, stepReal, stepString } from '../src/kernel/export/step';
import { MIN_EXPORT_RADIUS } from '../src/kernel/export/prepare';
import type { CutFeature, EdgeFlangeFeature, Feature, HemFeature, SketchCutFeature } from '../src/kernel/features/types';
import { emptyPart } from '../src/kernel/model';
import type { Part } from '../src/kernel/model';
import { exampleDoc } from '../src/ui/example';
import { IDENTITY, V3, add3, cross3, dot3, norm3, scale3, sub3, v3 } from '../src/kernel/vec';
import { build, doc, plate } from './helpers';

const OPTS = { name: 'Gehäusewinkel', timestamp: '2026-10-16T12:00:00' };

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

const hem = (hemType: HemFeature['hemType']): HemFeature => ({
  id: 'hem',
  name: 'Saum1',
  type: 'hem',
  edges: [{ tag: 'base:e0', at: { x: 50, y: 0 }, side: 'top' }],
  hemType,
  position: 'materialInside',
  reverse: false,
  length: 10,
  gap: 1,
  angle: 270,
  radius: 3,
  width: { mode: 'full', offset1: 0, offset2: 0, width: 0 },
});

const cut = (over: Partial<CutFeature>): CutFeature => ({
  id: 'cut',
  name: 'Bohrung1',
  type: 'cut',
  edge: { tag: 'base:e0', at: { x: 50, y: 0 } },
  u: 20,
  v: 25,
  shape: 'circle',
  diameter: 8,
  width: 20,
  height: 8,
  angle: 0,
  count: 1,
  spacing: 0,
  ...over,
});

const sketchCut: SketchCutFeature = {
  id: 'sc',
  name: 'Ausschnitt1',
  type: 'sketchCut',
  sketch: {
    edge: { tag: 'base:e0', at: { x: 50, y: 0 } },
    entities: [
      { kind: 'circle', id: 'c', cx: 30, cy: 20, r: 5 },
      { kind: 'rect', id: 'n', cx: 50, cy: 0, w: 20, h: 20, angle: 0 },
    ],
  },
};

const tray: Feature[] = [
  plate(),
  flange({
    edges: [
      { tag: 'base:e0', at: { x: 50, y: 0 }, side: 'top' },
      { tag: 'base:e1', at: { x: 100, y: 25 }, side: 'top' },
      { tag: 'base:e2', at: { x: 50, y: 50 }, side: 'top' },
      { tag: 'base:e3', at: { x: 0, y: 25 }, side: 'top' },
    ],
  }),
];

// ------------------------------------------------------------ Part 21 reading

interface Entity {
  type: string;
  args: string;
}

function parse(text: string): Map<number, Entity> {
  const out = new Map<number, Entity>();
  for (const m of text.matchAll(/^#(\d+)=([A-Z_0-9]*)\((.*)\);$/gm)) out.set(Number(m[1]), { type: m[2], args: m[3] });
  return out;
}

const refs = (s: string): number[] => [...s.matchAll(/#(\d+)/g)].map((m) => Number(m[1]));
const reals = (s: string): number[] => [...s.matchAll(/-?\d+\.\d*(?:E-?\d+)?/g)].map((m) => Number(m[0]));
const ofType = (E: Map<number, Entity>, type: string): [number, Entity][] => [...E].filter(([, e]) => e.type === type);
const sense = (args: string): boolean => args.endsWith('.T.');

interface OrientedEdge {
  edge: number;
  start: number;
  end: number;
}

function loopEdges(E: Map<number, Entity>, loop: number): OrientedEdge[] {
  return refs(E.get(loop)!.args).map((oe) => {
    const o = E.get(oe)!;
    const edge = refs(o.args)[0];
    const [a, b] = refs(E.get(edge)!.args);
    return sense(o.args) ? { edge, start: a, end: b } : { edge, start: b, end: a };
  });
}

/** Every edge used by exactly two loops in opposite directions, every loop a closed chain. */
function topology(E: Map<number, Entity>): { badEdges: number; badLoops: number; euler: number } {
  const uses = new Map<number, boolean[]>();
  let badLoops = 0;
  for (const [id, e] of ofType(E, 'EDGE_LOOP')) {
    const l = loopEdges(E, id);
    for (const oe of refs(e.args)) {
      const o = E.get(oe)!;
      uses.set(refs(o.args)[0], [...(uses.get(refs(o.args)[0]) ?? []), sense(o.args)]);
    }
    if (l.some((x, i) => x.end !== l[(i + 1) % l.length].start)) badLoops++;
  }
  const curves = ofType(E, 'EDGE_CURVE');
  const badEdges = curves.filter(([id]) => {
    const u = uses.get(id) ?? [];
    return u.length !== 2 || u[0] === u[1];
  }).length;
  const V = ofType(E, 'VERTEX_POINT').length;
  const F = ofType(E, 'ADVANCED_FACE').length;
  const L = ofType(E, 'EDGE_LOOP').length;
  return { badEdges, badLoops, euler: V - curves.length + 2 * F - L };
}

interface Frame {
  o: V3;
  z: V3;
  x: V3;
  y: V3;
}

function geometry(E: Map<number, Entity>) {
  const point = (id: number): V3 => {
    const [x, y, z] = reals(E.get(id)!.args);
    return v3(x, y, z);
  };
  const frame = (id: number): Frame => {
    const [o, z, x] = refs(E.get(id)!.args);
    const zz = norm3(point(z));
    const xx = norm3(sub3(point(x), scale3(zz, dot3(point(x), zz))));
    return { o: point(o), z: zz, x: xx, y: cross3(zz, xx) };
  };
  const vertex = (id: number): V3 => point(refs(E.get(id)!.args)[0]);
  /** Points along an oriented edge, arcs sampled counter-clockwise about their axis. */
  const samples = (oe: OrientedEdge): V3[] => {
    const curve = E.get(refs(E.get(oe.edge)!.args)[2])!;
    const [a, b] = refs(E.get(oe.edge)!.args).map(vertex);
    if (curve.type !== 'CIRCLE') return oe.start === refs(E.get(oe.edge)!.args)[0] ? [a, b] : [b, a];
    const f = frame(refs(curve.args)[0]);
    const r = reals(curve.args).pop()!;
    const ang = (p: V3): number => Math.atan2(dot3(sub3(p, f.o), f.y), dot3(sub3(p, f.o), f.x));
    const t0 = ang(a);
    let t1 = ang(b);
    if (t1 <= t0 + 1e-12) t1 += 2 * Math.PI;
    const pts: V3[] = [];
    for (let i = 0; i <= 24; i++) {
      const t = t0 + ((t1 - t0) * i) / 24;
      pts.push(add3(f.o, add3(scale3(f.x, r * Math.cos(t)), scale3(f.y, r * Math.sin(t)))));
    }
    return oe.start === refs(E.get(oe.edge)!.args)[0] ? pts : pts.reverse();
  };
  /** Vector area ½∮ p × dp of a loop; by Stokes it equals ∫ n dA of the face it bounds. */
  const vectorArea = (loop: number): { area: V3; pts: V3[] } => {
    const pts = loopEdges(E, loop).flatMap((oe) => samples(oe).slice(0, -1));
    let area = v3(0, 0, 0);
    pts.forEach((p, i) => (area = add3(area, scale3(cross3(p, pts[(i + 1) % pts.length]), 0.5))));
    return { area, pts };
  };
  return { frame, vectorArea };
}

/** Faces whose loops do not run counter-clockwise about the outward normal. */
function misoriented(E: Map<number, Entity>): number {
  const { frame, vectorArea } = geometry(E);
  let bad = 0;
  for (const [, face] of ofType(E, 'ADVANCED_FACE')) {
    const ids = refs(face.args);
    const surf = E.get(ids.pop()!)!;
    const f = frame(refs(surf.args)[0]);
    const s = sense(face.args) ? 1 : -1;
    const loops = ids.map((b) => ({ outer: E.get(b)!.type === 'FACE_OUTER_BOUND', ...vectorArea(refs(E.get(b)!.args)[0]) }));
    if (surf.type === 'PLANE') {
      if (loops.some((l) => (l.outer ? 1 : -1) * s * dot3(l.area, f.z) <= 0)) bad++;
      continue;
    }
    // cylinder: ∫ n dA points along the mean radial direction (times the sense)
    let radial = v3(0, 0, 0);
    let area = v3(0, 0, 0);
    for (const l of loops) {
      area = add3(area, l.area);
      for (const p of l.pts) {
        const w = sub3(p, f.o);
        radial = add3(radial, norm3(sub3(w, scale3(f.z, dot3(w, f.z)))));
      }
    }
    if (s * dot3(area, radial) <= 0) bad++;
  }
  return bad;
}

function roundHoles(part: Part): number {
  let n = 0;
  for (const f of part.faces.values()) {
    for (const h of f.holes) if (h.tags.every((t) => t === h.tags[0]) && part.circles.has(h.tags[0])) n++;
  }
  return n;
}

function expectValidStep(part: Part, text: string): Map<number, Entity> {
  expect(text.startsWith('ISO-10303-21;\nHEADER;\n')).toBe(true);
  expect(text).toContain("FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));");
  expect(text).toContain("('UnsolidWorks'),(''),'UnsolidWorks','UnsolidWorks',''");
  expect(text.endsWith('ENDSEC;\nEND-ISO-10303-21;\n')).toBe(true);
  expect(text).not.toMatch(/NaN|Infinity|undefined/);
  const E = parse(text);
  for (const [, p] of [...ofType(E, 'CARTESIAN_POINT'), ...ofType(E, 'DIRECTION')]) {
    expect(p.args).toMatch(/^'',\((-?\d+\.\d*(E-?\d+)?,){2}-?\d+\.\d*(E-?\d+)?\)$/);
  }
  expect(ofType(E, 'MANIFOLD_SOLID_BREP')).toHaveLength(1);
  expect(ofType(E, 'CLOSED_SHELL')).toHaveLength(1);
  expect(ofType(E, 'ADVANCED_BREP_SHAPE_REPRESENTATION')).toHaveLength(1);
  expect(ofType(E, 'SHAPE_DEFINITION_REPRESENTATION')).toHaveLength(1);
  const holes = [...part.faces.values()].reduce((s, f) => s + f.holes.length, 0);
  const t = topology(E);
  expect(t.badEdges).toBe(0);
  expect(t.badLoops).toBe(0);
  // Euler–Poincaré for one shell: V − E + 2F − L = 2 − 2·(through holes)
  expect(t.euler).toBe(2 - 2 * holes);
  expect(misoriented(E)).toBe(0);
  const cylinders = ofType(E, 'CYLINDRICAL_SURFACE');
  expect(cylinders).toHaveLength(2 * part.bends.size + 2 * roundHoles(part));
  for (const [, c] of cylinders) expect(reals(c.args).pop()!).toBeGreaterThan(0);
  return E;
}

function step(features: Feature[] | ReturnType<typeof exampleDoc>): { part: Part; text: string; E: Map<number, Entity> } {
  const { part } = build(Array.isArray(features) ? doc(features) : features);
  const text = partToStep(part, OPTS);
  return { part, text, E: expectValidStep(part, text) };
}

describe('STEP export', () => {
  it('plate: six planar faces, named product, mm units', () => {
    const { text, E } = step([plate()]);
    expect(ofType(E, 'ADVANCED_FACE')).toHaveLength(6);
    expect(ofType(E, 'PLANE')).toHaveLength(6);
    expect(ofType(E, 'EDGE_CURVE')).toHaveLength(12);
    expect(text).toContain("PRODUCT('Geh\\X2\\00E4\\X0\\usewinkel','Geh\\X2\\00E4\\X0\\usewinkel','',");
    expect(text).toContain('SI_UNIT(.MILLI.,.METRE.)');
    expect(text).toContain('SI_UNIT($,.RADIAN.)');
    expect(text).toContain('SI_UNIT($,.STERADIAN.)');
    expect(text).toContain('LENGTH_MEASURE(1.E-06)');
  });

  it('tray with four flanges: two cylinders per bend, bend lines shared', () => {
    const { E } = step(tray);
    expect(ofType(E, 'CYLINDRICAL_SURFACE')).toHaveLength(8);
    // 4 bends × 2 side arcs × top/bottom
    expect(ofType(E, 'CIRCLE')).toHaveLength(16);
  });

  it('every flange position and a downward flange', () => {
    for (const position of ['materialInside', 'materialOutside', 'bendOutside', 'virtualSharp', 'tangentToBend'] as const) {
      step([plate(), flange({ position })]);
    }
    step([plate(), flange({ angle: 60, edges: [{ tag: 'base:e0', at: { x: 50, y: 0 }, side: 'bottom' }] })]);
  });

  it('hems: the raw closed hem (radius 0) gets the minimal export radius', () => {
    const { E } = step([plate(), hem('closed')]);
    const radii = ofType(E, 'CYLINDRICAL_SURFACE').map(([, c]) => reals(c.args).pop()!);
    expect(Math.min(...radii)).toBeCloseTo(MIN_EXPORT_RADIUS, 9);
    for (const t of ['open', 'teardrop'] as const) step([plate(), hem(t)]);
  });

  it('rolled hem ends in a planar face', () => {
    const { part, E } = step([plate(), hem('rolled')]);
    expect([...part.bends.values()][0].faceB).toBeNull();
    // 2 sheet faces of the plate, 2 strip cylinders, 4 plate walls minus the seam, 2 bend ends, 1 end face
    expect(ofType(E, 'ADVANCED_FACE')).toHaveLength(2 + 2 + 3 + 2 + 1);
  });

  it('round holes are true circles with two half-cylinder walls', () => {
    const { part, E } = step([plate(), cut({ count: 3, spacing: 25, diameter: 7 }), flange({ edges: [{ tag: 'base:e2', at: { x: 50, y: 50 }, side: 'top' }] })]);
    expect(roundHoles(part)).toBe(3);
    const radii = ofType(E, 'CIRCLE').map(([, c]) => reals(c.args).pop()!);
    // one circle per hole and height, both half arcs share it
    expect(radii.filter((r) => Math.abs(r - 3.5) < 1e-12)).toHaveLength(6);
  });

  it('rectangular and slot cuts stay straight-edged', () => {
    const { E } = step([plate(), cut({ shape: 'rect', u: 30, angle: 15 }), cut({ id: 'cut2', shape: 'slot', u: 70 })]);
    expect(ofType(E, 'CYLINDRICAL_SURFACE')).toHaveLength(0);
    expect(ofType(E, 'CIRCLE')).toHaveLength(0);
  });

  it('sketch cut: circle hole and rectangular notch', () => {
    const { E } = step([plate(), sketchCut]);
    expect(ofType(E, 'CYLINDRICAL_SURFACE')).toHaveLength(2);
  });

  it('the example part', () => {
    const { part, E } = step(exampleDoc());
    expect(part.bends.size).toBe(4);
    expect(ofType(E, 'CYLINDRICAL_SURFACE')).toHaveLength(12);
  });

  it('a mirrored placement still yields outward-oriented faces', () => {
    const { part } = build(doc([plate(), cut({}), flange()]));
    const M = part.anchor.M;
    part.anchor.M = { r: [-M.r[0], -M.r[1], -M.r[2], M.r[3], M.r[4], M.r[5], M.r[6], M.r[7], M.r[8]], t: M.t };
    expectValidStep(part, partToStep(part, OPTS));
  });

  it('collinear free edges of one face share a side face', () => {
    const p = emptyPart(2);
    const pts = [v3(0, 0, 0), v3(50, 0, 0), v3(100, 0, 0), v3(100, 50, 0), v3(0, 50, 0)].map(({ x, y }) => ({ x, y }));
    p.faces.set('f', { id: 'f', feature: 'f', outer: { pts, tags: ['a', 'a', 'b', 'c', 'd'] }, holes: [] });
    p.anchor = { face: 'f', M: IDENTITY };
    const E = expectValidStep(p, partToStep(p, OPTS));
    expect(ofType(E, 'ADVANCED_FACE')).toHaveLength(6);
  });
});

describe('Part 21 encoding', () => {
  it('reals always carry a decimal point', () => {
    expect(stepReal(1)).toBe('1.');
    expect(stepReal(-0.5)).toBe('-0.5');
    expect(stepReal(0)).toBe('0.');
    expect(stepReal(-1e-13)).toBe('0.');
    expect(stepReal(1.5e-7)).toBe('1.5E-7');
    expect(stepReal(123456.789012345)).toBe('123456.789012345');
    expect(stepReal(12345678.1234567891)).toBe('12345678.1234568');
    expect(stepReal(1e21)).toBe('1.E21');
    expect(() => stepReal(NaN)).toThrow();
    expect(() => stepReal(Infinity)).toThrow();
  });

  it('strings escape apostrophes, backslashes and non-ASCII', () => {
    expect(stepString("Blech 'A'")).toBe("'Blech ''A'''");
    expect(stepString('a\\b')).toBe("'a\\\\b'");
    expect(stepString('Größe')).toBe("'Gr\\X2\\00F600DF\\X0\\e'");
    expect(stepString('Maß €')).toBe("'Ma\\X2\\00DF\\X0\\ \\X2\\20AC\\X0\\'");
    expect(stepString('😀')).toBe("'\\X4\\0001F600\\X0\\'");
  });
});
