// Lasche (planar edge extension), Geschlossene Ecke (extend a flange side to
// the neighbouring flange) and Ecke brechen (chamfer / round outline corners).

import { FoundEdge, Part, allEdges, computePlacements, findEdge, isSeamTag, stripFlat } from '../model';
import { Ring, clippedArea, isSimple, pointInPolygon, distPointSegment, ringsCross, signedArea } from '../polygon';
import { Affine, V2, add2, applyA, applyDirA, cross2, dist2, dot3, frame2, invertRigid, norm2, rotCCW, rotCW, scale2, sub2, sub3, v3 } from '../vec';
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

interface CornerTarget {
  edge: FoundEdge;
  dist: number;
}

/** Distance along the outward in-plane normal of `edge` to the nearest face in front of it. */
function targetOf(part: Part, placements: Map<string, Affine>, edge: FoundEdge, only?: string): CornerTarget | null {
  const T = part.T;
  const M = placements.get(edge.face.id);
  if (!M) return null;
  const d = norm2(sub2(edge.b, edge.a));
  const o = rotCW(d);
  const mid = { x: (edge.a.x + edge.b.x) / 2, y: (edge.a.y + edge.b.y) / 2 };
  const m3 = applyA(M, v3(mid.x, mid.y, T / 2));
  const u3 = applyDirA(M, v3(o.x, o.y, 0));
  let best: CornerTarget | null = null;
  for (const other of part.faces.values()) {
    if (other.id === edge.face.id || (only && other.id !== only)) continue;
    const MA = placements.get(other.id);
    if (!MA) continue;
    const nA = applyDirA(MA, v3(0, 0, 1));
    const denom = dot3(u3, nA);
    if (Math.abs(denom) < 0.25) continue;
    const base = dot3(sub3(applyA(MA, v3(0, 0, 0)), m3), nA);
    const near = Math.min(base / denom, (base + T) / denom);
    if (near < -1e-6 || near > 200) continue;
    const local = applyA(invertRigid(MA), { x: m3.x + u3.x * near, y: m3.y + u3.y * near, z: m3.z + u3.z * near });
    const lp = { x: local.x, y: local.y };
    const pts = other.outer.pts;
    const onFace = pointInPolygon(lp, pts) || pts.some((p, k) => distPointSegment(lp, p, pts[(k + 1) % pts.length]) < 1e-3);
    if (onFace && (!best || near < best.dist)) best = { edge, dist: near };
  }
  return best;
}

export function applyClosedCorner(part: Part, f: ClosedCornerFeature): void {
  if (f.edges.length === 0) throw new Error('Bitte mindestens eine Seitenkante einer Lasche auswählen.');
  if (!(f.gap >= 0)) throw new Error('Der Spalt darf nicht negativ sein.');
  f.edges.forEach((ref, i) => {
    const placements = computePlacements(part);
    const picked = findEdge(part, ref);
    requireFreeOuterEdge(picked);
    let target = targetOf(part, placements, picked);
    if (!target) {
      // the picked flange already runs through the corner: extend the neighbour towards it instead
      const M = placements.get(picked.face.id)!;
      const pm = { x: (picked.a.x + picked.b.x) / 2, y: (picked.a.y + picked.b.y) / 2 };
      const p3 = applyA(M, v3(pm.x, pm.y, part.T / 2));
      let best: CornerTarget | null = null;
      for (const e of allEdges(part)) {
        if (e.face.id === picked.face.id || e.ringIndex !== -1 || isSeamTag(e.tag)) continue;
        const Me = placements.get(e.face.id);
        if (!Me) continue;
        const em = { x: (e.a.x + e.b.x) / 2, y: (e.a.y + e.b.y) / 2 };
        const e3 = applyA(Me, v3(em.x, em.y, part.T / 2));
        if (Math.hypot(e3.x - p3.x, e3.y - p3.y, e3.z - p3.z) > dist2(picked.a, picked.b) + 20) continue;
        const t = targetOf(part, placements, e, picked.face.id);
        if (t && t.dist - f.gap > 1e-3 && (!best || t.dist < best.dist)) best = t;
      }
      target = best;
    }
    if (!target) throw new Error('Gegenüber dieser Kante liegt keine Lasche. Bitte die Seitenkante einer Lasche an einer Ecke wählen.');
    const L = target.dist - f.gap;
    if (L < 1e-3) throw new Error('Die Ecke ist bereits geschlossen.');
    const edge = target.edge;
    const id = f.edges.length > 1 ? `${f.id}.${i}` : f.id;
    attachToEdge(part, edge, 0, dist2(edge.a, edge.b), L, relief, id, id, edge.tag);
    checkFlatOverlap(part, edge.face.id);
  });
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
