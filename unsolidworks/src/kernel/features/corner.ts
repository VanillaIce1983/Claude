// Lasche (planar edge extension), Geschlossene Ecke (close the gap between
// two flanges at a corner) and Ecke brechen (chamfer / round outline corners).

import { FoundEdge, Part, allEdges, computePlacements, findEdge, isSeamTag, stripFlat } from '../model';
import { Ring, clippedArea, isSimple, pointInPolygon, distPointSegment, ringsCross, signedArea } from '../polygon';
import { Affine, V2, V3, add2, applyA, applyDirA, cross2, dist2, dot3, frame2, norm2, rotCCW, rotCW, scale2, sub2, sub3, v3 } from '../vec';
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
    checkFlatOverlap(part, edge.face.id);
  });
}

/** Throw if the outline of `faceId` overlaps any other face or bend strip in the flat pattern. */
function checkFlatOverlap(part: Part, faceId: string): void {
  const face = part.faces.get(faceId)!;
  const mine = face.outer.pts;
  const pieces: V2[][] = [];
  for (const other of part.faces.values()) if (other.id !== faceId) pieces.push(other.outer.pts);
  for (const b of part.bends.values()) {
    if (b.ba < 1e-9) continue;
    const quad = [stripFlat(b, b.w0, 0), stripFlat(b, b.w0, 1), stripFlat(b, b.w1, 1), stripFlat(b, b.w1, 0)];
    pieces.push(signedArea(quad) < 0 ? quad.reverse() : quad);
  }
  // exact clipping when one side is convex (strips always are), sampling for two concave outlines
  for (const piece of pieces) {
    const convex = isConvex(piece);
    const area = convex ? clippedArea(mine, piece) : isConvex(mine) ? clippedArea(piece, mine) : sampleOverlap(mine, piece);
    if (area > 1e-4) {
      throw new Error('Die Abwicklung würde sich überlappen. Bitte Länge oder Spalt verringern.');
    }
  }
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

/** Rough overlap test for two concave polygons: interior sample points of one inside the other. */
function sampleOverlap(a: V2[], b: V2[]): number {
  const inside = (p: V2, poly: V2[]): boolean => pointInPolygon(p, poly) && poly.every((_, i) => distPointSegment(p, poly[i], poly[(i + 1) % poly.length]) > 1e-4);
  for (const [s, t] of [
    [a, b],
    [b, a],
  ] as const) {
    for (let i = 0; i < s.length; i++) {
      const p = s[i];
      const q = s[(i + 1) % s.length];
      const m = { x: (p.x + q.x) / 2, y: (p.y + q.y) / 2 };
      const inward = scale2(rotCCW(norm2(sub2(q, p))), 1e-3);
      if (inside(add2(m, inward), t)) return 1;
    }
  }
  return 0;
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
  };
}

/**
 * Distances along `from.out` to the two sheet surfaces of `faceId`
 * (near = the one reached first). Null when the face is nearly parallel.
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
  return { near: Math.min(d0, d1), far: Math.max(d0, d1) };
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
 * flange. Überlappen: the picked flange runs to the outer surface of the
 * other one, which then stops at the picked flange's inner surface (minus the
 * gap). Unterlappen: the other way round. Both flanges are extended or
 * trimmed as needed.
 */
export function applyClosedCorner(part: Part, f: ClosedCornerFeature): void {
  if (f.edges.length === 0) throw new Error('Bitte mindestens eine Seitenkante einer Lasche auswählen.');
  if (!(f.gap >= 0)) throw new Error('Der Spalt darf nicht negativ sein.');
  const overlap = (f.kind ?? 'overlap') === 'overlap';
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
    const La = overlap ? ab.far : ab.near - f.gap;
    const Lb = overlap ? ba.near - f.gap : ba.far;
    const id = f.edges.length > 1 ? `${f.id}.${i}` : f.id;
    for (const [side, L, suffix] of [
      [a, La, 'a'],
      [b, Lb, 'b'],
    ] as const) {
      if (Math.abs(L) < 1e-3) continue;
      // re-find the edge: the other side's change may have rebuilt this face's outline
      const edge = findEdge(part, { tag: side.edge.tag, at: { x: (side.edge.a.x + side.edge.b.x) / 2, y: (side.edge.a.y + side.edge.b.y) / 2 } });
      attachToEdge(part, edge, 0, dist2(edge.a, edge.b), L, relief, `${id}${suffix}`, `${id}${suffix}`, edge.tag);
      checkFlatOverlap(part, edge.face.id);
      changedAny = true;
    }
  });
  if (!changedAny) throw new Error('Die Ecke ist bereits geschlossen.');
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
