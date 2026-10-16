// Ausschnitt (sketched cut): closed sketch profiles are subtracted from one
// face. polygon-clipping does the boolean work; edge tags are recovered
// afterwards because later features find their edges by tag.

import polygonClipping from 'polygon-clipping';
import type { MultiPolygon, Ring as PcRing } from 'polygon-clipping';
import { Part, isSeamTag } from '../model';
import { GEO_TOL, Ring, bbox2, distPointSegment, isSimple, orientRing, pointInPolygon, segmentsIntersect, signedArea } from '../polygon';
import { SketchEntity, SketchFrame, entityOutline, entityProblem, sketchFrame, sketchToFlat } from '../sketch';
import { V2, cross2, dist2, dot2, lerp2, sub2, v2 } from '../vec';
import type { SketchCutFeature } from './types';

interface Seg {
  a: V2;
  b: V2;
  tag: string;
}

const BEND_MSG = 'Der Ausschnitt berührt eine Biegung. Ausschnitte über Biegungen sind nicht möglich – bitte die Skizze von der Biegelinie wegrücken.';
const TOUCH_MSG = 'Der Ausschnitt berührt die Kontur oder einen anderen Ausschnitt nur in einem Punkt. Bitte die Skizze etwas verschieben.';

function ringSegs(r: Ring): Seg[] {
  return r.pts.map((a, i) => ({ a, b: r.pts[(i + 1) % r.pts.length], tag: r.tags[i] }));
}

const toPc = (pts: V2[]): PcRing => pts.map((p) => [p.x, p.y]);

/** Absolute material area of polygons given as [outer, ...holes]. */
function materialArea(polys: V2[][][]): number {
  return polys.reduce((s, rings) => s + rings.reduce((t, r, i) => t + (i === 0 ? 1 : -1) * Math.abs(signedArea(r)), 0), 0);
}

function profileOutline(frame: SketchFrame, e: SketchEntity): V2[] {
  const raw = entityOutline(e).map((p) => sketchToFlat(frame, p));
  // repeated clicks on one spot would make zero-length edges
  const pts = raw.filter((p, i) => dist2(p, raw[(i + 1) % raw.length]) >= GEO_TOL);
  if (pts.length < 3 || !isSimple(pts)) {
    throw new Error('Das Profil überschneidet sich selbst. Bitte die Punkte so setzen, dass sich die Linien nicht kreuzen.');
  }
  return pts;
}

/** Seams must survive unchanged, so the cut may neither touch nor contain a seam edge. */
function checkSeams(seams: Seg[], profiles: V2[][]): void {
  for (const s of seams) {
    for (const pts of profiles) {
      if (pointInPolygon(lerp2(s.a, s.b, 0.5), pts)) throw new Error(BEND_MSG);
      for (let i = 0; i < pts.length; i++) {
        if (segmentsIntersect(s.a, s.b, pts[i], pts[(i + 1) % pts.length])) throw new Error(BEND_MSG);
      }
    }
  }
}

function subtract(rings: Ring[], profiles: V2[][]): V2[][][] {
  let out: MultiPolygon;
  try {
    const [first, ...rest] = profiles.map((p) => [toPc(p)]);
    const cut = polygonClipping.union(first, ...rest);
    out = polygonClipping.difference(rings.map((r) => toPc(r.pts)), cut);
  } catch {
    throw new Error('Der Ausschnitt lässt sich nicht berechnen. Bitte die Skizze etwas verschieben.');
  }
  // polygon-clipping repeats the first point at the end of every ring
  return out.map((poly) => poly.map((r) => r.slice(0, -1).map(([x, y]) => v2(x, y))));
}

/** Uniform grid over bounding boxes, so the tolerance searches stay fast for sketches with many profiles. */
interface Grid<T> {
  size: number;
  cells: Map<string, T[]>;
}

function cellKeys(size: number, a: V2, b: V2): string[] {
  const keys: string[] = [];
  const j0 = Math.floor((Math.min(a.y, b.y) - GEO_TOL) / size);
  const j1 = Math.floor((Math.max(a.y, b.y) + GEO_TOL) / size);
  for (let i = Math.floor((Math.min(a.x, b.x) - GEO_TOL) / size); i <= Math.floor((Math.max(a.x, b.x) + GEO_TOL) / size); i++) {
    for (let j = j0; j <= j1; j++) keys.push(`${i},${j}`);
  }
  return keys;
}

function makeGrid<T>(size: number, items: T[], ends: (item: T) => [V2, V2]): Grid<T> {
  const cells = new Map<string, T[]>();
  for (const item of items) {
    for (const k of cellKeys(size, ...ends(item))) {
      const cell = cells.get(k);
      if (cell) cell.push(item);
      else cells.set(k, [item]);
    }
  }
  return { size, cells };
}

/** Items whose box shares a cell with the box of ab (may repeat items). */
function near<T>(g: Grid<T>, a: V2, b: V2 = a): T[] {
  return cellKeys(g.size, a, b).flatMap((k) => g.cells.get(k) ?? []);
}

const pointGrid = (size: number, pts: V2[]): Grid<V2> => makeGrid(size, pts, (p) => [p, p]);
const segGrid = (size: number, segs: Seg[]): Grid<Seg> => makeGrid(size, segs, (s) => [s.a, s.b]);

function nearest(p: V2, cands: V2[]): V2 | null {
  let best: V2 | null = null;
  let bd = GEO_TOL;
  for (const c of cands) {
    const d = dist2(p, c);
    if (d < bd) {
      bd = d;
      best = c;
    }
  }
  return best;
}

/** Points of `cands` strictly inside segment ab (collinear within tolerance), ordered from a to b. */
function pointsOn(a: V2, b: V2, cands: V2[]): V2[] {
  const ab = sub2(b, a);
  const l2 = dot2(ab, ab);
  const l = Math.sqrt(l2);
  const hits: { t: number; p: V2 }[] = [];
  for (const c of cands) {
    const ac = sub2(c, a);
    const t = dot2(ac, ab) / l2;
    if (t * l < GEO_TOL || (1 - t) * l < GEO_TOL) continue;
    if (Math.abs(cross2(ab, ac)) / l >= GEO_TOL) continue;
    hits.push({ t, p: c });
  }
  hits.sort((x, y) => x.t - y.t);
  return hits.filter((h, i) => i === 0 || dist2(h.p, hits[i - 1].p) >= GEO_TOL).map((h) => h.p);
}

interface Recovery {
  origPts: Grid<V2>;
  profilePts: Grid<V2>;
  origSegs: Grid<Seg>;
  profileSegs: Grid<Seg>;
  orig: Set<V2>;
  cutTag: string;
}

/**
 * Rebuild a tagged ring from a polygon-clipping ring: snap to the original
 * points, put back vertices the clipper dropped as collinear, then name each
 * edge after the original or profile edge it lies on.
 */
function recoverRing(raw: V2[], rc: Recovery, ccw: boolean): Ring {
  const snapped = raw.map((p) => nearest(p, near(rc.origPts, p)) ?? nearest(p, near(rc.profilePts, p)) ?? p);
  const base = snapped.filter((p, i) => p !== snapped[(i + 1) % snapped.length]);
  const pts: V2[] = [];
  base.forEach((a, i) => {
    const b = base[(i + 1) % base.length];
    pts.push(a, ...pointsOn(a, b, [...near(rc.origPts, a, b), ...near(rc.profilePts, a, b)]));
  });
  const tags = pts.map((a, i) => {
    const b = pts[(i + 1) % pts.length];
    const on = (s: Seg): boolean => distPointSegment(a, s.a, s.b) < GEO_TOL && distPointSegment(b, s.a, s.b) < GEO_TOL;
    return near(rc.origSegs, a).find(on)?.tag ?? near(rc.profileSegs, a).find(on)?.tag ?? rc.cutTag;
  });
  return orientRing(dropInserted({ pts, tags }, rc.orig), ccw);
}

/** Remove vertices this feature added that sit inside a straight run of one tag; original vertices stay. */
function dropInserted(r: Ring, orig: Set<V2>): Ring {
  const pts = r.pts.slice();
  const tags = r.tags.slice();
  for (let i = 0; i < pts.length && pts.length > 3; ) {
    const prev = (i - 1 + pts.length) % pts.length;
    const ab = sub2(pts[i], pts[prev]);
    const bc = sub2(pts[(i + 1) % pts.length], pts[i]);
    const straight = Math.abs(cross2(ab, bc)) < GEO_TOL * (Math.hypot(ab.x, ab.y) + Math.hypot(bc.x, bc.y)) && dot2(ab, bc) > 0;
    if (!orig.has(pts[i]) && tags[prev] === tags[i] && straight) {
      pts.splice(i, 1);
      tags.splice(i, 1);
    } else {
      i++;
    }
  }
  return { pts: pts.map((p) => ({ ...p })), tags };
}

/** Every ring simple, and no two rings touching (a pinch would make a non-manifold solid). */
function checkRings(rings: Ring[], size: number): void {
  if (!rings.every((r) => isSimple(r.pts))) throw new Error(TOUCH_MSG);
  const segs = rings.flatMap((r, k) => ringSegs(r).map((s) => ({ ...s, k })));
  const grid = makeGrid(size, segs, (s) => [s.a, s.b]);
  for (const s of segs) {
    for (const o of near(grid, s.a, s.b)) {
      if (o.k > s.k && segmentsIntersect(s.a, s.b, o.a, o.b)) throw new Error(TOUCH_MSG);
    }
  }
}

function seamKept(s: Seg, rings: Ring[]): boolean {
  const same = (p: V2, q: V2): boolean => p.x === q.x && p.y === q.y;
  return rings.some((r) => r.pts.some((p, i) => r.tags[i] === s.tag && same(p, s.a) && same(r.pts[(i + 1) % r.pts.length], s.b)));
}

export function applySketchCut(part: Part, f: SketchCutFeature): void {
  const frame = sketchFrame(part, f.sketch.edge);
  const face = frame.face;
  if (f.sketch.entities.length === 0) throw new Error('Die Skizze ist leer. Bitte mindestens ein geschlossenes Profil zeichnen.');
  const profiles: V2[][] = [];
  const profileSegs: Seg[] = [];
  for (const e of f.sketch.entities) {
    const problem = entityProblem(e);
    if (problem) throw new Error(problem);
    const pts = profileOutline(frame, e);
    profiles.push(pts);
    profileSegs.push(...ringSegs({ pts, tags: pts.map(() => `${f.id}:${e.id}`) }));
  }

  const rings = [face.outer, ...face.holes];
  const origSegs = rings.flatMap(ringSegs);
  const seams = origSegs.filter((s) => isSeamTag(s.tag));
  checkSeams(seams, profiles);

  const result = subtract(rings, profiles);
  if (materialArea(result) >= materialArea([rings.map((r) => r.pts)]) - 1e-6) {
    throw new Error('Der Ausschnitt liegt außerhalb der Fläche.');
  }
  if (result.length !== 1) throw new Error('Der Ausschnitt würde die Fläche zerteilen oder ganz entfernen.');

  const origPts = rings.flatMap((r) => r.pts);
  const profilePts = profiles.flat();
  const box = bbox2([...origPts, ...profilePts]);
  const size = Math.max(box.maxX - box.minX, box.maxY - box.minY, 1) / 64;
  const rc: Recovery = {
    origPts: pointGrid(size, origPts),
    profilePts: pointGrid(size, profilePts),
    origSegs: segGrid(size, origSegs),
    profileSegs: segGrid(size, profileSegs),
    orig: new Set(origPts),
    cutTag: `${f.id}:cut`,
  };
  const [outer, ...holes] = result[0].map((raw, i) => recoverRing(raw, rc, i === 0));
  checkRings([outer, ...holes], size);
  if (!seams.every((s) => seamKept(s, [outer, ...holes]))) throw new Error(BEND_MSG);

  face.outer = outer;
  face.holes = holes;
  for (const e of f.sketch.entities) {
    if (e.kind !== 'circle') continue;
    const tag = `${f.id}:${e.id}`;
    // only an intact circle is a true circle; merged or notched ones are plain contours
    if (holes.some((h) => h.tags.every((t) => t === tag))) part.circles.set(tag, { c: sketchToFlat(frame, v2(e.cx, e.cy)), r: e.r });
  }
}
