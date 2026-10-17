// Lasche (planar edge extension), Geschlossene Ecke (close the gap between
// two flanges at a corner) and Ecke brechen (chamfer / round outline corners).

import polygonClipping from 'polygon-clipping';
import type { Ring as PcRing } from 'polygon-clipping';
import {
  Bend,
  EdgeRef,
  EndPiece,
  FoundEdge,
  Part,
  StripEnd,
  allEdges,
  computePlacements,
  findEdge,
  isSeamTag,
  seamTag,
  stripFlat,
  stripFractions,
  stripOutline,
  stripPoint,
  stripRange,
  uniqFractions,
} from '../model';
import { Ring, bbox2, clippedArea, distPointSegment, isSimple, orientRing, pointInPolygon, ringsCross, signedArea, splitRing } from '../polygon';
import {
  Affine,
  V2,
  V3,
  add2,
  add3,
  applyA,
  applyDirA,
  composeA,
  cross2,
  cross3,
  dist2,
  dot2,
  dot3,
  frame2,
  invertRigid,
  len3,
  norm2,
  norm3,
  rotCCW,
  rotCW,
  scale2,
  scale3,
  sub2,
  sub3,
  v2,
  v3,
} from '../vec';
import { attachToEdge, requireFreeOuterEdge, resolveWidth } from './attach';
import type { BreakCornerFeature, ClosedCornerFeature, CornerRef, TabFeature } from './types';

const relief = { type: 'tear' as const, ratio: 0.5 };

export function applyTab(part: Part, f: TabFeature): void {
  if (f.edges.length === 0) throw new Error('Bitte mindestens eine Kante auswählen.');
  if (!(f.length > 0)) throw new Error('Die Laschenlänge muss größer als 0 sein.');
  f.edges.forEach((ref, i) => {
    const edge = findEdge(part, ref);
    requireFreeOuterEdge(edge);
    const [w0, w1] = resolveWidth(f.width, dist2(edge.a, edge.b));
    const id = f.edges.length > 1 ? `${f.id}.${i}` : f.id;
    const full = w0 <= 1e-6 && w1 >= dist2(edge.a, edge.b) - 1e-6;
    // a full-width tab moves the edge itself, so it keeps its name
    attachToEdge(part, edge, w0, w1, f.length, relief, id, id, full ? edge.tag : `${id}:end`);
    checkFlatOverlap(part, faceKey(edge.face.id));
  });
}

const faceKey = (id: string): string => `face:${id}`;
const bendKey = (id: string): string => `bend:${id}`;

/** Flat outlines (CCW) of all faces and bend strips, keyed by faceKey / bendKey. */
function flatPieces(part: Part): Map<string, V2[]> {
  const out = new Map<string, V2[]>();
  for (const face of part.faces.values()) out.set(faceKey(face.id), face.outer.pts);
  for (const b of part.bends.values()) if (b.ba >= 1e-9) out.set(bendKey(b.id), stripOutline(b));
  return out;
}

/** Throw if the flat outline of one face or strip overlaps any other face or bend strip. */
function checkFlatOverlap(part: Part, key: string): void {
  const pieces = flatPieces(part);
  const mine = pieces.get(key);
  if (!mine) return;
  for (const [k, piece] of pieces) {
    if (k !== key && overlapArea(mine, piece) > 1e-4) {
      throw new Error('Die Abwicklung würde sich überlappen. Bitte Länge oder Spalt verringern.');
    }
  }
}

/** Overlap areas of the given pieces with every other piece of the flat pattern. */
function flatOverlaps(part: Part, keys: string[]): Map<string, number> {
  const pieces = flatPieces(part);
  const out = new Map<string, number>();
  for (const key of keys) {
    const mine = pieces.get(key);
    if (!mine) continue;
    for (const [k, piece] of pieces) {
      const a = k === key ? 0 : overlapArea(mine, piece);
      if (a > 1e-4) out.set(`${key}|${k}`, a);
    }
  }
  return out;
}

/** Whether no overlap grew against `before` (overlaps elsewhere were there already). */
function noNewOverlap(part: Part, keys: string[], before: Map<string, number>): boolean {
  for (const [k, a] of flatOverlaps(part, keys)) if (a > (before.get(k) ?? 0) + 1e-4) return false;
  return true;
}

const toPc = (pts: V2[]): PcRing => pts.map((p) => [p.x, p.y]);

/** Overlap area of two simple CCW polygons: cheap clipping when one is convex, exact boolean otherwise. */
function overlapArea(a: V2[], b: V2[]): number {
  const ba = bbox2(a);
  const bb = bbox2(b);
  if (ba.maxX <= bb.minX || bb.maxX <= ba.minX || ba.maxY <= bb.minY || bb.maxY <= ba.minY) return 0;
  if (isConvex(b)) return clippedArea(a, b);
  if (isConvex(a)) return clippedArea(b, a);
  let common;
  try {
    common = polygonClipping.intersection([toPc(a)], [toPc(b)]);
  } catch {
    throw new Error('Die Abwicklung lässt sich nicht auf Überlappung prüfen. Bitte Länge oder Spalt leicht ändern.');
  }
  const ringArea = (r: PcRing): number => Math.abs(signedArea(r.map(([x, y]) => v2(x, y))));
  return common.reduce((s, poly) => s + poly.reduce((t, r, i) => t + (i === 0 ? ringArea(r) : -ringArea(r)), 0), 0);
}

function isConvex(pts: V2[]): boolean {
  let sign = 0;
  for (let i = 0; i < pts.length; i++) {
    const c = cross2(sub2(pts[(i + 1) % pts.length], pts[i]), sub2(pts[(i + 2) % pts.length], pts[(i + 1) % pts.length]));
    if (Math.abs(c) < 1e-12) continue;
    const s = Math.sign(c);
    if (sign === 0) sign = s;
    else if (s !== sign) return false;
  }
  return true;
}

/** A free side edge of a flange face, seen in model space. */
interface Side {
  edge: FoundEdge;
  /** Mid-thickness midpoint of the edge. */
  mid: V3;
  /** Edge direction. */
  dir: V3;
  /** Outward in-plane normal: the direction the face grows when the edge is extended. */
  out: V3;
  /** Normal of the face. */
  nrm: V3;
}

function sideOf(part: Part, placements: Map<string, Affine>, edge: FoundEdge): Side | null {
  const M = placements.get(edge.face.id);
  if (!M) return null;
  const d = norm2(sub2(edge.b, edge.a));
  const o = rotCW(d);
  const m = { x: (edge.a.x + edge.b.x) / 2, y: (edge.a.y + edge.b.y) / 2 };
  return {
    edge,
    mid: applyA(M, v3(m.x, m.y, part.T / 2)),
    dir: applyDirA(M, v3(d.x, d.y, 0)),
    out: applyDirA(M, v3(o.x, o.y, 0)),
    nrm: applyDirA(M, v3(0, 0, 1)),
  };
}

/**
 * Distances along `from.out` to the two sheet surfaces of `faceId`
 * (near = the one reached first), from the middle of the sheet. Where the
 * faces do not meet at a right angle, one sheet surface of `from` gets there
 * earlier; `near` is measured from that one, so a flange cut back to it
 * stays clear on both surfaces. Null when the face is nearly parallel.
 */
function surfaceHits(part: Part, placements: Map<string, Affine>, from: Side, faceId: string): { near: number; far: number } | null {
  const MA = placements.get(faceId);
  if (!MA) return null;
  const n = applyDirA(MA, v3(0, 0, 1));
  const denom = dot3(from.out, n);
  if (Math.abs(denom) < 0.25) return null;
  const base = dot3(sub3(applyA(MA, v3(0, 0, 0)), from.mid), n);
  const d0 = base / denom;
  const d1 = (base + part.T) / denom;
  const lean = (Math.abs(dot3(from.nrm, n) / denom) * part.T) / 2;
  return { near: Math.min(d0, d1) - lean, far: Math.max(d0, d1) };
}

/** The free side edge of another flange that forms a corner with `a`. */
function cornerPartner(part: Part, placements: Map<string, Affine>, a: Side): Side | null {
  const maxR = Math.max(0, ...[...part.bends.values()].map((b) => b.radius));
  const reach = 2 * (maxR + part.T) + part.T + 1;
  let best: { side: Side; dist: number } | null = null;
  for (const e of allEdges(part)) {
    if (e.face.id === a.edge.face.id || e.ringIndex !== -1 || isSeamTag(e.tag)) continue;
    const b = sideOf(part, placements, e);
    if (!b || Math.abs(dot3(a.dir, b.dir)) < 0.95) continue;
    // both edges must point at the other face
    const ab = surfaceHits(part, placements, a, e.face.id);
    const ba = surfaceHits(part, placements, b, a.edge.face.id);
    if (!ab || !ba || ab.far < -reach || ba.far < -reach || ab.near > reach || ba.near > reach) continue;
    // distance between the two (parallel) edge lines, and overlap along them
    const w = sub3(b.mid, a.mid);
    const along = dot3(w, a.dir);
    const perp = Math.hypot(w.x - a.dir.x * along, w.y - a.dir.y * along, w.z - a.dir.z * along);
    const la = dist2(a.edge.a, a.edge.b);
    const lb = dist2(e.a, e.b);
    if (perp > reach || Math.abs(along) > (la + lb) / 2) continue;
    if (!best || perp < best.dist) best = { side: b, dist: perp };
  }
  return best?.side ?? null;
}

/**
 * Closes the corner between the picked flange side and the neighbouring
 * flange. Both flanges are extended or trimmed as needed:
 * - Stoß: both are treated alike and run to the other one's inner surface
 *   (minus the gap); the outer corner keeps a thickness × thickness notch.
 * - Überlappen: the picked flange runs to the other one's outer surface, the
 *   other stops at the picked flange's inner surface (minus the gap).
 * - Unterlappen: the other way round.
 * Unless the bend region stays open, both bend strips also run into the
 * corner and meet at the mitre plane, so only a small opening stays at the radius.
 */
export function applyClosedCorner(part: Part, f: ClosedCornerFeature): void {
  if (f.edges.length === 0) throw new Error('Bitte mindestens eine Seitenkante einer Lasche auswählen.');
  if (!(f.gap >= 0)) throw new Error('Der Spalt darf nicht negativ sein.');
  const kind = f.kind ?? 'overlap';
  const done = new Set<string>();
  let changedAny = false;
  f.edges.forEach((ref, i) => {
    const placements = computePlacements(part);
    const picked = findEdge(part, ref);
    requireFreeOuterEdge(picked);
    const a = sideOf(part, placements, picked);
    const b = a && cornerPartner(part, placements, a);
    if (!a || !b) throw new Error('An dieser Kante liegt keine benachbarte Lasche. Bitte die Seitenkante einer Lasche an einer offenen Ecke wählen.');
    const key = [a.edge.face.id, b.edge.face.id].sort().join('|') + `@${Math.round(a.mid.x)},${Math.round(a.mid.y)},${Math.round(a.mid.z)}`;
    const keyB = [a.edge.face.id, b.edge.face.id].sort().join('|') + `@${Math.round(b.mid.x)},${Math.round(b.mid.y)},${Math.round(b.mid.z)}`;
    if (done.has(key) || done.has(keyB)) return; // both sides of the same corner were picked
    done.add(key);
    const ab = surfaceHits(part, placements, a, b.edge.face.id)!;
    const ba = surfaceHits(part, placements, b, a.edge.face.id)!;
    const La = kind === 'overlap' ? ab.far : ab.near - f.gap;
    const Lb = kind === 'underlap' ? ba.far : ba.near - f.gap;
    const id = f.edges.length > 1 ? `${f.id}.${i}` : f.id;
    // the bends are found at the side edges as they are before the flanges change
    const bends = f.openBend ? null : cornerPair(part, a.edge, b.edge);
    for (const [side, L, suffix] of [
      [a, La, 'a'],
      [b, Lb, 'b'],
    ] as const) {
      if (Math.abs(L) < 1e-3) continue;
      // re-find the edge: the other side's change may have rebuilt this face's outline
      const edge = findEdge(part, { tag: side.edge.tag, at: { x: (side.edge.a.x + side.edge.b.x) / 2, y: (side.edge.a.y + side.edge.b.y) / 2 } });
      attachToEdge(part, edge, 0, dist2(edge.a, edge.b), L, relief, `${id}${suffix}`, `${id}${suffix}`, edge.tag);
      checkFlatOverlap(part, faceKey(edge.face.id));
      changedAny = true;
    }
    if (bends && closeBendRegion(part, bends, f.gap)) changedAny = true;
  });
  if (!changedAny) throw new Error('Die Ecke ist bereits geschlossen.');
}

const isSideTag = (e: FoundEdge): boolean => e.tag === `${e.face.id}:side0` || e.tag === `${e.face.id}:side1`;

/** The side edge of a flange on one of `faces` whose end lies nearest to `a`. */
function mitrePartner(part: Part, placements: Map<string, Affine>, a: Side, faces: Set<string>): Side | null {
  const maxR = Math.max(0, ...[...part.bends.values()].map((b) => b.radius));
  const reach = 2 * (maxR + part.T) + part.T + 1;
  const ends = (e: FoundEdge): V3[] => [e.a, e.b].map((p) => applyA(placements.get(e.face.id)!, v3(p.x, p.y, part.T / 2)));
  const mine = ends(a.edge);
  let best: { side: Side; dist: number } | null = null;
  for (const e of allEdges(part)) {
    if (e.face.id === a.edge.face.id || !faces.has(e.face.id) || e.ringIndex !== -1 || !isSideTag(e)) continue;
    const b = sideOf(part, placements, e);
    if (!b) continue;
    const dist = Math.min(...ends(e).flatMap((p) => mine.map((q) => len3(sub3(p, q)))));
    if (dist <= reach && (!best || dist < best.dist)) best = { side: b, dist };
  }
  return best?.side ?? null;
}

/**
 * Mitres the corner between the flange side edge `ref` and the nearest side
 * edge of a flange on one of `partners`, like an edge flange on several
 * edges in SolidWorks. Both flanges are extended or trimmed up to the plane
 * that halves the angle between them, each keeping half the gap from it, so
 * they never come closer than the gap. The cut stays perpendicular to the
 * sheet (it is made in the flat), so the sheet surface nearer the plane
 * limits it. The bends meet on the same plane. Returns whether the part
 * changed, or null when no flange forms a corner there; throws when the
 * corner cannot be mitred (the caller keeps it open).
 */
export function mitreCorner(part: Part, ref: EdgeRef, gap: number, partners: Set<string>, done: Set<string>): boolean | null {
  const T = part.T;
  const placements = computePlacements(part);
  const a = sideOf(part, placements, findEdge(part, ref));
  const b = a && mitrePartner(part, placements, a, partners);
  if (!a || !b) return null;
  const key = [a.edge.tag, b.edge.tag].sort().join('|');
  if (done.has(key)) return false;
  done.add(key);
  // the corner line, where the two mid-planes meet
  const MA = placements.get(a.edge.face.id)!;
  const MB = placements.get(b.edge.face.id)!;
  const nA = applyDirA(MA, v3(0, 0, 1));
  const nB = applyDirA(MB, v3(0, 0, 1));
  const u = cross3(nA, nB);
  const uu = dot3(u, u);
  if (uu < 1e-2) throw new Error('Die Laschen liegen parallel.');
  const hA = dot3(nA, applyA(MA, v3(0, 0, T / 2)));
  const hB = dot3(nB, applyA(MB, v3(0, 0, T / 2)));
  const p0 = scale3(add3(scale3(cross3(nB, u), hA), scale3(cross3(u, nA), hB)), 1 / uu);
  // in each flange, the direction across the corner line towards it
  const towards = (s: Side, nX: V3): V3 => {
    const d = norm3(cross3(u, nX));
    const k = dot3(d, s.out);
    if (Math.abs(k) < 0.2) throw new Error('Die Seitenkante zeigt nicht zur Ecke.');
    return k > 0 ? d : scale3(d, -1);
  };
  const diff = sub3(towards(a, nA), towards(b, nB));
  if (len3(diff) < 0.1) throw new Error('Die Laschen liegen in einer Ebene.');
  const n = norm3(diff);
  // the bends at both side edges, found before the flanges change
  const pair = cornerPair(part, a.edge, b.edge);
  const atSides = [...cornerBends(part, a.edge).map((c) => [c, 1] as const), ...cornerBends(part, b.edge).map((c) => [c, -1] as const)];
  // the flat pattern may overlap already at corners not mitred yet; this one must not add to it
  const keys = [faceKey(a.edge.face.id), faceKey(b.edge.face.id), ...new Set(atSides.map(([c]) => bendKey(c.bend.id)))];
  const overlapBefore = flatOverlaps(part, keys);
  const flatOk = (): boolean => noNewOverlap(part, keys, overlapBefore);
  for (const [s, sign] of [
    [a, 1],
    [b, -1],
  ] as const) {
    // keep n·(p − p0) ≤ −gap/2 on both sheet surfaces, as a line in the flat
    const M = placements.get(s.edge.face.id)!;
    const nn = scale3(n, sign);
    const k = dot3(nn, sub3(applyA(M, v3(0, 0, 0)), p0));
    const lx = dot3(nn, applyDirA(M, v3(1, 0, 0)));
    const ly = dot3(nn, applyDirA(M, v3(0, 1, 0)));
    const g = Math.hypot(lx, ly);
    if (g < 0.2) throw new Error('Die Gehrung liegt fast parallel zum Blech.');
    const nrm = v2(lx / g, ly / g);
    const c = (-gap / 2 - k - Math.max(0, dot3(nn, applyDirA(M, v3(0, 0, 1))) * T)) / g;
    let edge = findEdge(part, { tag: s.edge.tag, at: scale2(add2(s.edge.a, s.edge.b), 0.5) });
    const out = dot2(nrm, rotCW(norm2(sub2(edge.b, edge.a))));
    if (out < 0.2) throw new Error('Die Gehrung zeigt nicht von der Seitenkante weg.');
    // grow the side edge past the cut where the flange does not reach it yet
    const grow = Math.max(...[edge.a, edge.b].map((p) => (c - dot2(nrm, p)) / out));
    if (grow > 1e-6) {
      attachToEdge(part, edge, 0, dist2(edge.a, edge.b), grow + 1, relief, s.edge.face.id, s.edge.face.id, edge.tag);
      edge = findEdge(part, { tag: s.edge.tag, at: scale2(add2(s.edge.a, s.edge.b), 0.5) });
    }
    const face = edge.face;
    const kept = splitRing(face.outer, v2(0, 0), nrm, c, s.edge.tag).neg;
    if (!kept || !isSimple(kept.pts)) throw new Error('Die Gehrung zerschneidet die Lasche.');
    if (face.outer.tags.some((t) => isSeamTag(t) && !kept.tags.includes(t))) throw new Error('Die Gehrung schneidet die Biegung ab.');
    if (face.holes.some((h) => ringsCross(kept.pts, h.pts) || !pointInPolygon(h.pts[0], kept.pts))) throw new Error('Ein Ausschnitt liegt im Bereich der Gehrung.');
    face.outer = orientRing(kept, true);
  }
  if (pair) closeBendRegion(part, pair, gap, { plane: { p0, n }, trimFallback: true, flatOk });
  // every bend at the corner stays on its side of the mitre and under its flange's seam
  for (const [c, sign] of atSides) {
    const M = placements.get(c.bend.faceA)!;
    const nn = scale3(n, sign);
    for (const f of profileSamples(c.bend)) {
      const [wl, wr] = stripRange(c.bend, f);
      for (const h of [0, T]) {
        const p = applyA(M, stripPoint(c.bend, T, c.s > 0 ? wr : wl, f, h));
        if (dot3(nn, sub3(p, p0)) > -gap / 2 + 1e-6) throw new Error('Die Biegung reicht über die Gehrung.');
      }
    }
    const [wl, wr] = stripRange(c.bend, c.fF);
    const reach = seamReach(part, c);
    if (reach === null || (c.s > 0 ? wr - c.bend.w1 : c.bend.w0 - wl) > reach + 1e-6) throw new Error('Die Biegung reicht über die Lasche hinaus.');
  }
  if (!flatOk()) throw new Error('Die Abwicklung würde sich überlappen.');
  return true;
}

/** The end of a bend strip at a flange side edge: the bend part of a corner. */
interface CornerBend {
  bend: Bend;
  /** +1: the w1 end lies at the corner, -1: the w0 end. */
  s: 1 | -1;
  /** Strip fraction at the flange face; the web face is at 1 − fF. */
  fF: 0 | 1;
  web: string;
}

const seamSide = (fraction: number): 'A' | 'B' => (fraction === 0 ? 'A' : 'B');
const cornerEnd = (c: CornerBend): number => (c.s > 0 ? c.bend.w1 : c.bend.w0);

/** Bends whose seam on the face of `edge` ends at that side edge. */
function cornerBends(part: Part, edge: FoundEdge): CornerBend[] {
  const out: CornerBend[] = [];
  const ring = edge.face.outer;
  const n = ring.pts.length;
  for (const bend of part.bends.values()) {
    for (const fF of [0, 1] as const) {
      const web = fF === 0 ? bend.faceB : bend.faceA;
      if ((fF === 0 ? bend.faceA : bend.faceB) !== edge.face.id || !web) continue;
      const tag = seamTag(bend.id, seamSide(fF));
      for (let i = 0; i < n; i++) {
        if (ring.tags[i] !== tag) continue;
        const ends = [ring.pts[i], ring.pts[(i + 1) % n]];
        for (const [v, other] of [ends, [ends[1], ends[0]]]) {
          if (dist2(v, edge.a) > 1e-6 && dist2(v, edge.b) > 1e-6) continue;
          // the seam ends at the side edge, so its direction there tells the strip end; an
          // earlier closed corner may have moved that seam end past the strip end
          out.push({ bend, s: dot2(sub2(v, other), bend.dir) > 0 ? 1 : -1, fF, web });
        }
      }
    }
  }
  return out;
}

/** The two bends of a corner: one at each side edge, both starting from the same web face. */
function cornerPair(part: Part, ea: FoundEdge, eb: FoundEdge): [CornerBend, CornerBend] | null {
  const atB = cornerBends(part, eb);
  for (const ca of cornerBends(part, ea)) {
    const cb = atB.find((c) => c.web === ca.web && c.bend.id !== ca.bend.id);
    if (cb) return [ca, cb];
  }
  return null;
}

/** Mitre plane between the two bends of a corner, through the web corner where both start. */
interface Mitre {
  p0: V3;
  /** Unit normal, from bend a's side towards bend b's. */
  n: V3;
}

function cornerMitre(part: Part, placements: Map<string, Affine>, a: CornerBend, b: CornerBend): Mitre | null {
  const web = part.faces.get(a.web);
  const MW = placements.get(a.web);
  if (!web || !MW) return null;
  // the web corner is where the two web seam lines meet
  const qa = stripFlat(a.bend, 0, 1 - a.fF);
  const qb = stripFlat(b.bend, 0, 1 - b.fF);
  const den = cross2(a.bend.dir, b.bend.dir);
  if (Math.abs(den) < 1e-9) return null;
  const wa = cross2(sub2(qb, qa), b.bend.dir) / den;
  const corner = add2(qa, scale2(a.bend.dir, wa));
  const wb = dot2(sub2(corner, qb), b.bend.dir);
  // both strips must reach the corner; the first flange of a box may run past it
  if (a.s * (cornerEnd(a) - wa) < -1e-4 || b.s * (cornerEnd(b) - wb) < -1e-4) return null;
  if (!isWebCorner(web.outer, corner, [seamTag(a.bend.id, seamSide(1 - a.fF)), seamTag(b.bend.id, seamSide(1 - b.fF))])) return null;
  const axis = (c: CornerBend): V3 | null => {
    const M = placements.get(c.bend.faceA);
    return M ? norm3(applyDirA(M, v3(c.s * c.bend.dir.x, c.s * c.bend.dir.y, 0))) : null;
  };
  const Da = axis(a);
  const Db = axis(b);
  if (!Da || !Db || len3(sub3(Da, Db)) < 1e-6) return null;
  const n = norm3(sub3(Da, Db));
  if (dot3(n, Da) <= 1e-6 || -dot3(n, Db) <= 1e-6) return null;
  return { p0: applyA(MW, v3(corner.x, corner.y, 0)), n };
}

/** Whether the outline has a vertex at `p` between the two given edges. */
function isWebCorner(ring: Ring, p: V2, tags: string[]): boolean {
  const n = ring.pts.length;
  for (let i = 0; i < n; i++) {
    if (dist2(ring.pts[i], p) > 1e-4) continue;
    const around = [ring.tags[(i - 1 + n) % n], ring.tags[i]];
    if (tags.every((t) => around.includes(t))) return true;
  }
  return false;
}

/** a + b·sin(angle·f) + c·cos(angle·f). */
interface Trig {
  a: number;
  b: number;
  c: number;
}

/**
 * Largest extension of the strip end that keeps the fibre at height h on its
 * side of the mitre plane (n·(p − p0) ≤ −gap/2), for both sheet surfaces.
 * A fibre is C + sin φ·S + cos φ·K in model space, so the limit is exactly
 * a + b·sin φ + c·cos φ; C, S and K come from φ = 0°, 90° and 180°.
 */
function fibreLimits(part: Part, placements: Map<string, Affine>, c: CornerBend, n: V3, p0: V3, gap: number): Trig[] {
  const b = c.bend;
  const M = placements.get(b.faceA)!;
  const k = dot3(n, applyDirA(M, v3(c.s * b.dir.x, c.s * b.dir.y, 0)));
  return [0, part.T].map((h) => {
    const at = (phi: number): V3 => applyA(M, stripPoint(b, part.T, cornerEnd(c), phi / b.angle, h));
    const p00 = at(0);
    const p180 = at(Math.PI);
    const C = scale3(add3(p00, p180), 0.5);
    const S = sub3(at(Math.PI / 2), C);
    const K = scale3(sub3(p00, p180), 0.5);
    return { a: (-gap / 2 - dot3(n, sub3(C, p0))) / k, b: -dot3(n, S) / k, c: -dot3(n, K) / k };
  });
}

/** Lower envelope of the terms on [0, 1], split where the smallest one changes. */
function lowerEnvelope(terms: Trig[], angle: number): EndPiece[] {
  const val = (t: Trig, f: number): number => t.a + t.b * Math.sin(angle * f) + t.c * Math.cos(angle * f);
  const lowest = (f: number): number => {
    let k = 0;
    for (let i = 1; i < terms.length; i++) if (val(terms[i], f) < val(terms[k], f) - 1e-12) k = i;
    return k;
  };
  const SAMPLES = 256;
  const starts = [{ f0: 0, k: lowest(0) }];
  let guard = 0;
  for (let i = 1; i <= SAMPLES; i++) {
    const hi = i / SAMPLES;
    while (lowest(hi) !== starts[starts.length - 1].k && guard++ < 1000) {
      const cur = starts[starts.length - 1];
      let lo = Math.max(cur.f0, (i - 1) / SAMPLES);
      let up = hi;
      while (up - lo > 1e-12) {
        const mid = (lo + up) / 2;
        if (lowest(mid) === cur.k) lo = mid;
        else up = mid;
      }
      starts.push({ f0: up, k: lowest(up) });
    }
  }
  // drop slivers (ties, e.g. both fibres at f = 0), then merge neighbours with the same source
  const kept = starts.filter((s, i) => (starts[i + 1]?.f0 ?? 1) - s.f0 > 1e-9);
  if (kept.length === 0) kept.push(starts[starts.length - 1]);
  const merged = kept.filter((s, i) => i === 0 || s.k !== kept[i - 1].k);
  return merged.map((s, i) => ({ f0: i === 0 ? 0 : s.f0, f1: merged[i + 1]?.f0 ?? 1, ...terms[s.k] }));
}

/** How far the flange face's seam reaches beyond the strip end, towards the corner. */
function seamReach(part: Part, c: CornerBend): number | null {
  const b = c.bend;
  const flange = part.faces.get((c.fF === 0 ? b.faceA : b.faceB) ?? '');
  if (!flange) return null;
  const tag = seamTag(b.id, seamSide(c.fF));
  const ring = flange.outer;
  let reach = -Infinity;
  for (let i = 0; i < ring.pts.length; i++) {
    if (ring.tags[i] !== tag) continue;
    for (const p of [ring.pts[i], ring.pts[(i + 1) % ring.pts.length]]) {
      reach = Math.max(reach, c.s * (dot2(sub2(p, b.origin), b.dir) - cornerEnd(c)));
    }
  }
  return Number.isFinite(reach) ? reach : null;
}

function sameEnd(x: StripEnd | undefined, y: StripEnd | undefined): boolean {
  if (!x || !y) return x === y;
  const keys = ['f0', 'f1', 'a', 'b', 'c'] as const;
  return x.pieces.length === y.pieces.length && x.pieces.every((p, i) => keys.every((k) => Math.abs(p[k] - y.pieces[i][k]) < 1e-9));
}

const profileSamples = (b: Bend): number[] => uniqFractions([...Array.from({ length: 65 }, (_, i) => i / 64), ...stripFractions(b)]);

function wideEnough(b: Bend): boolean {
  return profileSamples(b).every((f) => {
    const [wl, wr] = stripRange(b, f);
    return wr - wl >= 1e-3;
  });
}

/**
 * The mitre only separates the two bends. With different radii the grown
 * strip can still reach into the other flange's sheet; no continuous end
 * profile follows that boundary.
 */
function clearOfFlange(part: Part, placements: Map<string, Affine>, c: CornerBend, other: CornerBend): boolean {
  const flangeId = other.fF === 0 ? other.bend.faceA : other.bend.faceB;
  const flange = flangeId ? part.faces.get(flangeId) : undefined;
  const MF = flangeId ? placements.get(flangeId) : undefined;
  const MA = placements.get(c.bend.faceA);
  if (!flange || !MF || !MA) return true;
  const toFlange = composeA(invertRigid(MF), MA);
  const T = part.T;
  const tol = 1e-4;
  for (const f of profileSamples(c.bend)) {
    const d = c.s > 0 ? stripRange(c.bend, f)[1] - c.bend.w1 : c.bend.w0 - stripRange(c.bend, f)[0];
    if (d <= tol) continue;
    for (const t of [0.5, 1]) {
      for (const h of [0, T / 2, T]) {
        const q = applyA(toFlange, stripPoint(c.bend, T, cornerEnd(c) + c.s * d * t, f, h));
        const p = v2(q.x, q.y);
        const pts = flange.outer.pts;
        if (q.z <= tol || q.z >= T - tol || !pointInPolygon(p, pts)) continue;
        if (pts.every((a, i) => distPointSegment(p, a, pts[(i + 1) % pts.length]) > tol)) return false;
      }
    }
  }
  return true;
}

function clearInFlat(part: Part, b: Bend): boolean {
  try {
    checkFlatOverlap(part, bendKey(b.id));
    return true;
  } catch {
    return false;
  }
}

interface BendRegionOptions {
  /** Mitre plane instead of the one through the web corner. */
  plane: Mitre;
  /** If the strips cannot meet, still cut them back where they cross the plane. */
  trimFallback: boolean;
  /** Flat-pattern check instead of "no overlap at all". */
  flatOk: () => boolean;
}

/**
 * Runs both bend strips into the corner up to the mitre plane (`plane`, or
 * the one through the web corner), each staying half the gap away from it.
 * The cut is perpendicular to the sheet, so the inner or the outer fibre
 * limits it, and no strip runs past its flange's seam. Returns whether a
 * profile changed. Corners without such a bend pair, or where a grown strip
 * would get too narrow, reach into the other flange or overlap in the flat
 * pattern, keep their bend region open, as before; with `trimFallback` the
 * strips are then only cut back where they cross the mitre plane.
 */
function closeBendRegion(part: Part, pair: [CornerBend, CornerBend], gap: number, o: Partial<BendRegionOptions> = {}): boolean {
  const placements = computePlacements(part);
  const found = cornerMitre(part, placements, pair[0], pair[1]);
  if (!found) return false;
  const mitre = o.plane ?? found;
  const flatOk = o.flatOk ?? ((): boolean => pair.every((c) => clearInFlat(part, c.bend)));
  const caps = pair.map((c) => seamReach(part, c));
  if (caps[0] === null || caps[1] === null) return false;
  const key = (c: CornerBend): 'ext0' | 'ext1' => (c.s > 0 ? 'ext1' : 'ext0');
  const before = pair.map((c) => c.bend[key(c)]);
  const restore = (): void =>
    pair.forEach((c, i) => {
      if (before[i]) c.bend[key(c)] = before[i];
      else delete c.bend[key(c)];
    });
  const profile = (grow: boolean): boolean => {
    let changed = false;
    pair.forEach((c, i) => {
      const n = i === 0 ? mitre.n : scale3(mitre.n, -1);
      const terms = [...fibreLimits(part, placements, c, n, mitre.p0, gap), { a: caps[i]!, b: 0, c: 0 }];
      if (!grow) terms.push({ a: 0, b: 0, c: 0 });
      const pieces = lowerEnvelope(terms, c.bend.angle);
      const end = pieces.every((q) => q.a === 0 && q.b === 0 && q.c === 0) ? undefined : { pieces };
      if (!sameEnd(c.bend[key(c)], end)) changed = true;
      if (end) c.bend[key(c)] = end;
      else delete c.bend[key(c)];
    });
    return changed;
  };
  let changed = profile(true);
  if (pair.every((c, i) => wideEnough(c.bend) && clearOfFlange(part, placements, c, pair[1 - i])) && flatOk()) return changed;
  restore();
  if (!o.trimFallback) return false;
  // the bend region stays open, but no strip may cross the mitre
  changed = profile(false);
  if (pair.every((c) => wideEnough(c.bend)) && flatOk()) return changed;
  restore();
  return false;
}

/**
 * Where bend `id` starts or ends at a web corner that an older bend on the
 * same face runs past, the older bend and its flange are cut back to the
 * plane through that corner perpendicular to the new bend line. At obtuse
 * plate corners the older bend would otherwise run into the new one, in 3D
 * and in the flat pattern; at right and acute corners it lies behind that
 * plane already, so nothing changes there. The cut is perpendicular to the
 * sheet, so the inner fibre limits it; the bends then meet only at the
 * corner of the web. An older bend end with a profile already, one curling
 * back over the web, or one whose cut would take off another seam or a
 * hole stays as it is.
 */
export function yieldToBend(part: Part, id: string): void {
  const s = part.bends.get(id);
  const placements = computePlacements(part);
  const MW = s && placements.get(s.faceA);
  if (!s || !MW) return;
  for (const [w, into] of [
    [s.w0, s.dir],
    [s.w1, scale2(s.dir, -1)],
  ] as const) {
    const corner = add2(s.origin, scale2(s.dir, w));
    for (const r of part.bends.values()) {
      if (r === s || r.faceA !== s.faceA || r.angle > Math.PI + 1e-9 || Math.abs(cross2(r.dir, s.dir)) < 1e-6) continue;
      // the corner lies on the older bend line, inside its strip
      const rel = sub2(corner, r.origin);
      const at = dot2(rel, r.dir);
      const [lo, hi] = stripRange(r, 0);
      if (Math.abs(dot2(rel, r.nrm)) > 1e-6 || at <= lo + 1e-6 || at >= hi - 1e-6) continue;
      // the end running past the corner points out of the web, towards the new bend's strip;
      // it runs into the new bend only if it also points along it (an obtuse corner)
      const end = dot2(r.dir, s.nrm) > 0 ? 1 : -1;
      if (end * dot2(r.dir, into) < 1e-9) continue;
      trimToPlane(part, placements, { bend: r, s: end, fF: 1, web: r.faceA }, applyA(MW, v3(corner.x, corner.y, 0)), applyDirA(MW, v3(into.x, into.y, 0)));
    }
  }
}

/** Length of the edges with each seam tag on a ring. */
function seamLengths(r: Ring): Map<string, number> {
  const out = new Map<string, number>();
  r.pts.forEach((p, i) => {
    if (isSeamTag(r.tags[i])) out.set(r.tags[i], (out.get(r.tags[i]) ?? 0) + dist2(p, r.pts[(i + 1) % r.pts.length]));
  });
  return out;
}

/** Cuts the corner end of a bend and its flange back to the plane n·(p − p0) ≤ 0. */
function trimToPlane(part: Part, placements: Map<string, Affine>, c: CornerBend, p0: V3, n: V3): void {
  const b = c.bend;
  const key = c.s > 0 ? 'ext1' : 'ext0';
  if (b[key]) return;
  const pieces = lowerEnvelope([...fibreLimits(part, placements, c, n, p0, 0), { a: 0, b: 0, c: 0 }], b.angle);
  if (pieces.every((q) => q.a === 0 && q.b === 0 && q.c === 0)) return;
  b[key] = { pieces };
  const face = b.faceB ? part.faces.get(b.faceB) : undefined;
  if (!wideEnough(b)) {
    delete b[key];
    return;
  }
  if (!face) return;
  // the flange ends where its seam ends now
  const [wl, wr] = stripRange(b, 1);
  const ring = face.outer;
  const n2 = ring.pts.length;
  const seam = seamTag(b.id, 'B');
  let end = -1;
  let far = -Infinity;
  ring.pts.forEach((p, i) => {
    const onSeam = ring.tags[i] === seam || ring.tags[(i - 1 + n2) % n2] === seam;
    const w = c.s * dot2(sub2(p, b.origin), b.dir);
    if (onSeam && w > far) [end, far] = [i, w];
  });
  let kept: Ring | null = null;
  if (end >= 0) {
    // the outline edge leaving the seam at its corner end names the cut
    const sideTag = ring.tags[end] === seam ? ring.tags[(end - 1 + n2) % n2] : ring.tags[end];
    try {
      kept = splitRing(ring, b.origin, scale2(b.dir, c.s), c.s * (c.s > 0 ? wr : wl), sideTag).neg;
    } catch {
      kept = null;
    }
  }
  const before = seamLengths(ring);
  const after = kept && seamLengths(kept);
  const fits =
    kept &&
    after &&
    isSimple(kept.pts) &&
    [...before].every(([t, len]) => t === seam || (after.get(t) ?? 0) >= len - 1e-9) &&
    !face.holes.some((h) => ringsCross(kept!.pts, h.pts) || !pointInPolygon(h.pts[0], kept!.pts));
  if (fits) face.outer = orientRing(kept!, true);
  else delete b[key];
}

function findCorner(part: Part, c: CornerRef): { ring: Ring; index: number; faceId: string } {
  let best: { ring: Ring; index: number; faceId: string } | null = null;
  let bd = Infinity;
  for (const face of part.faces.values()) {
    const r = face.outer;
    const n = r.pts.length;
    for (let i = 0; i < n; i++) {
      if (r.tags[(i - 1 + n) % n] !== c.prevTag || r.tags[i] !== c.nextTag) continue;
      const dd = dist2(r.pts[i], c.at);
      if (dd < bd) {
        bd = dd;
        best = { ring: r, index: i, faceId: face.id };
      }
    }
  }
  if (!best) throw new Error('Die gewählte Ecke existiert nicht mehr. Bitte die Ecke neu auswählen.');
  return best;
}

export function applyBreakCorner(part: Part, f: BreakCornerFeature): void {
  if (f.corners.length === 0) throw new Error('Bitte mindestens eine Ecke auswählen.');
  if (!(f.distance > 0)) throw new Error(f.kind === 'fillet' ? 'Der Radius muss größer als 0 sein.' : 'Der Fasenabstand muss größer als 0 sein.');
  f.corners.forEach((c, k) => {
    const { ring, index: i, faceId } = findCorner(part, c);
    if (isSeamTag(c.prevTag) || isSeamTag(c.nextTag)) throw new Error('Ecken an Biegungen lassen sich nicht brechen.');
    const n = ring.pts.length;
    const V = ring.pts[i];
    const Pp = ring.pts[(i - 1 + n) % n];
    const Pn = ring.pts[(i + 1) % n];
    const dIn = norm2(sub2(V, Pp));
    const dOut = norm2(sub2(Pn, V));
    const turn = Math.atan2(cross2(dIn, dOut), dIn.x * dOut.x + dIn.y * dOut.y);
    if (Math.abs(turn) < 1e-3) throw new Error('An dieser Stelle ist keine Ecke.');
    const t = f.kind === 'chamfer' ? f.distance : f.distance * Math.tan(Math.abs(turn) / 2);
    if (t >= dist2(V, Pp) - 1e-6 || t >= dist2(Pn, V) - 1e-6) {
      throw new Error(`${f.kind === 'fillet' ? 'Der Radius' : 'Die Fase'} ist zu groß für die angrenzenden Kanten.`);
    }
    const P1 = sub2(V, scale2(dIn, t));
    const P2 = add2(V, scale2(dOut, t));
    const tag = `${f.id}:k${k}`;
    const insert: V2[] = [P1];
    if (f.kind === 'fillet') {
      const r = f.distance;
      const nrm = turn > 0 ? rotCCW(dIn) : rotCW(dIn);
      const C = add2(P1, scale2(nrm, r));
      const a1 = Math.atan2(P1.y - C.y, P1.x - C.x);
      const steps = Math.max(4, Math.ceil(Math.abs(turn) / ((5 * Math.PI) / 180)));
      for (let j = 1; j < steps; j++) {
        const a = a1 + (turn * j) / steps;
        insert.push(frame2(C, { x: 1, y: 0 }, { x: 0, y: 1 }, r * Math.cos(a), r * Math.sin(a)));
      }
    }
    insert.push(P2);
    const pts = [...ring.pts.slice(0, i), ...insert, ...ring.pts.slice(i + 1)];
    const tags = [...ring.tags.slice(0, i), ...insert.slice(0, -1).map(() => tag), ring.tags[i], ...ring.tags.slice(i + 1)];
    if (!isSimple(pts)) throw new Error('Die Fläche würde sich selbst überschneiden. Bitte einen kleineren Wert wählen.');
    const face = part.faces.get(faceId)!;
    for (const h of face.holes) {
      if (ringsCross(pts, h.pts)) throw new Error('Ein Ausschnitt liegt im Bereich der Ecke.');
    }
    face.outer = { pts, tags };
  });
}
