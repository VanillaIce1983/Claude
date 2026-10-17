// Ausschnitt (sketched cut): closed sketch profiles are subtracted from one
// face. polygon-clipping does the boolean work; edge tags are recovered
// afterwards because later features find their edges by tag.

import polygonClipping from 'polygon-clipping';
import type { MultiPolygon, Ring as PcRing } from 'polygon-clipping';
import { Part, isSeamTag } from '../model';
import { GEO_TOL, Ring, bbox2, cutClearance, distPointSegment, isSimple, orientRing, pointInPolygon, segmentDistance, segmentsIntersect, signedArea } from '../polygon';
import { SketchEntity, SketchFrame, entityOutline, entityProblem, sketchFrame, sketchToFlat } from '../sketch';
import { solvedEntities } from '../sketchSolve';
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

function profileOutline(frame: SketchFrame, e: SketchEntity, clear: number): V2[] {
  const raw = entityOutline(e).map((p) => sketchToFlat(frame, p));
  // repeated clicks on one spot would make edges too short to keep apart from their neighbours
  const pts = raw.filter((p, i) => dist2(p, raw[(i + 1) % raw.length]) >= clear);
  if (pts.length < 3 || !isSimple(pts)) {
    throw new Error('Das Profil überschneidet sich selbst. Bitte die Punkte so setzen, dass sich die Linien nicht kreuzen.');
  }
  return pts;
}

/** Seams must survive unchanged, so the cut may neither touch, nearly touch nor contain a seam edge. */
function checkSeams(seams: Seg[], profiles: V2[][], clear: number): void {
  for (const s of seams) {
    for (const pts of profiles) {
      if (pointInPolygon(lerp2(s.a, s.b, 0.5), pts)) throw new Error(BEND_MSG);
      for (let i = 0; i < pts.length; i++) {
        if (segmentDistance(s.a, s.b, pts[i], pts[(i + 1) % pts.length]) < clear) throw new Error(BEND_MSG);
      }
    }
  }
}

function subtract(rings: V2[][], profiles: V2[][]): V2[][][] {
  let out: MultiPolygon;
  try {
    const [first, ...rest] = profiles.map((p) => [toPc(p)]);
    const cut = polygonClipping.union(first, ...rest);
    out = polygonClipping.difference(rings.map(toPc), cut);
  } catch {
    throw new Error('Der Ausschnitt lässt sich nicht berechnen. Bitte die Skizze etwas verschieben.');
  }
  // polygon-clipping repeats the first point at the end of every ring
  return out.map((poly) => poly.map((r) => r.slice(0, -1).map(([x, y]) => v2(x, y))));
}

/** Uniform grid over bounding boxes, so the tolerance searches stay fast for sketches with many profiles. */
interface Grid<T> {
  size: number;
  /** Boxes grow by this much, so items closer than `pad` share a cell. */
  pad: number;
  cells: Map<string, T[]>;
}

function cellKeys(g: Grid<unknown>, a: V2, b: V2): string[] {
  const { size, pad } = g;
  const keys: string[] = [];
  const j0 = Math.floor((Math.min(a.y, b.y) - pad) / size);
  const j1 = Math.floor((Math.max(a.y, b.y) + pad) / size);
  for (let i = Math.floor((Math.min(a.x, b.x) - pad) / size); i <= Math.floor((Math.max(a.x, b.x) + pad) / size); i++) {
    for (let j = j0; j <= j1; j++) keys.push(`${i},${j}`);
  }
  return keys;
}

function gridAdd<T>(g: Grid<T>, item: T, a: V2, b: V2): void {
  for (const k of cellKeys(g, a, b)) {
    const cell = g.cells.get(k);
    if (cell) cell.push(item);
    else g.cells.set(k, [item]);
  }
}

function makeGrid<T>(size: number, items: T[], ends: (item: T) => [V2, V2], pad = GEO_TOL): Grid<T> {
  const g: Grid<T> = { size, pad, cells: new Map() };
  for (const item of items) gridAdd(g, item, ...ends(item));
  return g;
}

/** Items whose box shares a cell with the box of ab (may repeat items). */
function near<T>(g: Grid<T>, a: V2, b: V2 = a): T[] {
  return cellKeys(g, a, b).flatMap((k) => g.cells.get(k) ?? []);
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

/**
 * Make touching geometry exact before clipping. polygon-clipping treats an
 * edge as shared only when both loops have identical endpoints; a profile
 * corner 1e-15 off a slanted outline edge otherwise gives a pinched sliver.
 * Profile corners snap to corners of the face and of earlier profiles, and
 * every corner lying on another loop's edge is inserted into that edge.
 */
function conformLoops(rings: V2[][], profiles: V2[][], size: number): { rings: V2[][]; profiles: V2[][] } {
  const corners = pointGrid(size, rings.flat());
  const snapped = profiles.map((pts) => {
    const out = pts.map((p) => nearest(p, near(corners, p)) ?? p);
    for (const p of out) gridAdd(corners, p, p, p);
    return out;
  });
  const loops = [...rings, ...snapped];
  const owners = makeGrid(
    size,
    loops.flatMap((pts, k) => pts.map((p) => ({ p, k }))),
    ({ p }) => [p, p],
  );
  const isRing = (k: number): boolean => k < rings.length;
  const withInserted = loops.map((pts, k) =>
    pts.flatMap((a, i) => {
      const b = pts[(i + 1) % pts.length];
      // the face's own rings never touch each other, so only profiles add corners to them
      const others = near(owners, a, b).filter((o) => o.k !== k && !(isRing(k) && isRing(o.k)));
      return [a, ...pointsOn(a, b, others.map((o) => o.p))];
    }),
  );
  return { rings: withInserted.slice(0, rings.length), profiles: withInserted.slice(rings.length) };
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

/**
 * Every ring simple and no two rings touching (a pinch would make a
 * non-manifold solid). Nothing this cut added may come closer than `clear`
 * to another edge either: the mesh would merge it into that edge.
 */
function checkRings(rings: Ring[], size: number, clear: number, newPoint: (p: V2) => boolean, newTag: (tag: string) => boolean): void {
  if (!rings.every((r) => isSimple(r.pts))) throw new Error(TOUCH_MSG);
  const segs = rings.flatMap((r, k) => ringSegs(r).map((s, i) => ({ ...s, k, i, n: r.pts.length })));
  const grid = makeGrid(size, segs, (s) => [s.a, s.b], clear);
  for (const s of segs) {
    for (const o of near(grid, s.a, s.b)) {
      if (o.k > s.k && segmentsIntersect(s.a, s.b, o.a, o.b)) throw new Error(TOUCH_MSG);
    }
  }
  rings.forEach((r, k) =>
    r.pts.forEach((p, i) => {
      for (const s of near(grid, p)) {
        if (s.k === k && (s.i === i || (s.i + 1) % s.n === i)) continue;
        if ((newPoint(p) || newTag(s.tag)) && distPointSegment(p, s.a, s.b) < clear) throw new Error(TOUCH_MSG);
      }
    }),
  );
}

function seamKept(s: Seg, rings: Ring[]): boolean {
  const same = (p: V2, q: V2): boolean => p.x === q.x && p.y === q.y;
  return rings.some((r) => r.pts.some((p, i) => r.tags[i] === s.tag && same(p, s.a) && same(r.pts[(i + 1) % r.pts.length], s.b)));
}

export function applySketchCut(part: Part, f: SketchCutFeature): void {
  if (!f.sketch.edge.tag) throw new Error('Bitte die Fläche anklicken, auf der skizziert werden soll.');
  const frame = sketchFrame(part, f.sketch.edge, f.sketch.anchor);
  const face = frame.face;
  if (f.sketch.entities.length === 0) throw new Error('Die Skizze ist leer. Bitte mindestens ein geschlossenes Profil zeichnen.');
  const rings = [face.outer, ...face.holes];
  const origPts = rings.flatMap((r) => r.pts);
  const clear = cutClearance(face.outer.pts);
  // dimensions to the part move the profiles with it
  const entities = solvedEntities(part, f.sketch);
  const raw: V2[][] = [];
  for (const e of entities) {
    const problem = entityProblem(e);
    if (problem) throw new Error(problem);
    raw.push(profileOutline(frame, e, clear));
  }
  const box = bbox2([...origPts, ...raw.flat()]);
  const size = Math.max(box.maxX - box.minX, box.maxY - box.minY, 1) / 64;
  const exact = conformLoops(rings.map((r) => r.pts), raw, size);
  const profiles = exact.profiles;
  const profileSegs = entities.flatMap((e, i) => ringSegs({ pts: profiles[i], tags: profiles[i].map(() => `${f.id}:${e.id}`) }));

  const origSegs = rings.flatMap(ringSegs);
  const seams = origSegs.filter((s) => isSeamTag(s.tag));
  checkSeams(seams, profiles, clear);

  const result = subtract(exact.rings, profiles);
  if (materialArea(result) >= materialArea([rings.map((r) => r.pts)]) - 1e-6) {
    throw new Error('Der Ausschnitt liegt außerhalb der Fläche.');
  }
  if (result.length !== 1) throw new Error('Der Ausschnitt würde die Fläche zerteilen oder ganz entfernen.');

  const profilePts = profiles.flat();
  const rc: Recovery = {
    origPts: pointGrid(size, origPts),
    profilePts: pointGrid(size, profilePts),
    origSegs: segGrid(size, origSegs),
    profileSegs: segGrid(size, profileSegs),
    orig: new Set(origPts),
    cutTag: `${f.id}:cut`,
  };
  const [outer, ...holes] = result[0].map((r, i) => recoverRing(r, rc, i === 0));
  const key = (p: V2): string => `${p.x},${p.y}`;
  const origKeys = new Set(origPts.map(key));
  checkRings(
    [outer, ...holes],
    size,
    clear,
    (p) => !origKeys.has(key(p)),
    (tag) => tag.startsWith(`${f.id}:`),
  );
  if (!seams.every((s) => seamKept(s, [outer, ...holes]))) throw new Error(BEND_MSG);

  face.outer = outer;
  face.holes = holes;
  for (const e of entities) {
    if (e.kind !== 'circle') continue;
    const tag = `${f.id}:${e.id}`;
    // only an intact circle is a true circle; merged or notched ones are plain contours
    if (holes.some((h) => h.tags.every((t) => t === tag))) part.circles.set(tag, { c: sketchToFlat(frame, v2(e.cx, e.cy)), r: e.r });
  }
}
