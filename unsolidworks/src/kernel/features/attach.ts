// Shared geometry for features that add a bend to a free edge of a face
// (edge flange, hem): trimming or extending the face, cutting bend reliefs,
// and building the child face rectangle.

import { FoundEdge, Part, isSeamTag, seamTag } from '../model';
import { Ring, isSimple, orientRing, pointInPolygon, ringsCross, simplifyRing } from '../polygon';
import type { ReliefSettings } from '../sheet';
import { EPS, V2, cross2, dist2, dot2, frame2, norm2, rotCW, sub2 } from '../vec';
import type { WidthSpec } from './types';

const TOL = 1e-6;

export interface StripFrame {
  origin: V2;
  dir: V2;
  nrm: V2;
  w0: number;
  w1: number;
}

/** Resolve the flange width on an edge of length `len` to [w0, w1]. */
export function resolveWidth(spec: WidthSpec, len: number): [number, number] {
  let w0 = 0;
  let w1 = len;
  if (spec.mode === 'offset') {
    w0 = spec.offset1;
    w1 = len - spec.offset2;
  } else if (spec.mode === 'centered') {
    w0 = (len - spec.width) / 2;
    w1 = w0 + spec.width;
  }
  if (w0 < -TOL || w1 > len + TOL || w1 - w0 < 1e-3) {
    throw new Error(`Die Laschenbreite passt nicht auf die Kante (Kantenlänge ${len.toFixed(2)} mm).`);
  }
  return [Math.max(0, w0), Math.min(len, w1)];
}

export function requireFreeOuterEdge(e: FoundEdge): void {
  if (isSeamTag(e.tag)) throw new Error('An dieser Kante sitzt bereits eine Biegung. Bitte eine freie Kante wählen.');
  if (e.ringIndex !== -1) throw new Error('Laschen und Säume gehen nur an Außenkanten, nicht an Ausschnitten.');
}

function lineIntersect(p: V2, d: V2, q: V2, e: V2): number | null {
  // returns λ with p + λ·d on the line q + μ·e
  const den = cross2(d, e);
  if (Math.abs(den) < 1e-9) return null;
  return cross2(sub2(q, p), e) / den;
}

interface PathVertex {
  p: V2;
  tag: string;
}

/**
 * Modify the face that owns `edge` so a bend strip can start at offset `s`
 * from the edge (negative = into the face) over [w0, w1] along it.
 * Returns the strip frame. Throws with a user-facing message on failure.
 */
export function attachToEdge(
  part: Part,
  edge: FoundEdge,
  w0: number,
  w1: number,
  s: number,
  relief: ReliefSettings,
  bendId: string,
  tagPrefix: string,
): StripFrame {
  const face = edge.face;
  const ring = face.outer;
  const n = ring.pts.length;
  const k = edge.index;
  const P = ring.pts[k];
  const Q = ring.pts[(k + 1) % n];
  const len = dist2(P, Q);
  const d = norm2(sub2(Q, P));
  const o = rotCW(d);
  const W = (w: number, t: number): V2 => frame2(P, d, o, w, t);
  const origTag = ring.tags[k];
  const seam = seamTag(bendId, 'A');
  const reliefTag = `${tagPrefix}:relief`;
  const tabTag = `${tagPrefix}:tab`;
  const dr = relief.ratio * part.T;
  const full0 = w0 <= TOL;
  const full1 = w1 >= len - TOL;
  const sZero = Math.abs(s) < TOL;

  const path: PathVertex[] = [];
  let stripW0 = w0;
  let stripW1 = w1;
  let replaceQ: V2 | null = null;

  const arc = (cw: number, ct: number, r: number, a0: number, a1: number, steps: number): V2[] => {
    const out: V2[] = [];
    for (let i = 1; i < steps; i++) {
      const a = a0 + ((a1 - a0) * i) / steps;
      out.push(W(cw + r * Math.cos(a), ct + r * Math.sin(a)));
    }
    return out;
  };
  const arcSteps = 12;

  // ----- start of the edge -----
  if (full0) {
    if (sZero) {
      path.push({ p: P, tag: seam });
      stripW0 = 0;
    } else {
      const prev = ring.pts[(k - 1 + n) % n];
      const lam = lineIntersect(W(0, s), d, prev, sub2(P, prev));
      if (lam === null) throw new Error('Die Nachbarkante verläuft parallel zur gewählten Kante. Bitte eine Teilbreite verwenden.');
      const Pn = W(lam, s);
      // the moved corner must stay on the neighbouring edge when trimming
      const along = dot2(sub2(Pn, prev), sub2(P, prev)) / dot2(sub2(P, prev), sub2(P, prev));
      if (s < 0 && along < 1e-6) throw new Error('Die Nachbarkante ist zu kurz für diese Laschenposition.');
      path.push({ p: Pn, tag: seam });
      stripW0 = lam;
    }
  } else {
    path.push({ p: P, tag: origTag });
    if (sZero) {
      path.push({ p: W(w0, 0), tag: seam });
    } else if (s > 0) {
      path.push({ p: W(w0, 0), tag: tabTag });
      path.push({ p: W(w0, s), tag: seam });
    } else if (relief.type === 'tear') {
      path.push({ p: W(w0, 0), tag: reliefTag });
      path.push({ p: W(w0, s), tag: seam });
    } else {
      if (w0 - dr < TOL) throw new Error('Der Freistich reicht bis an die Ecke. Bitte den Abstand zur Kantenecke vergrößern oder einen Riss-Freistich wählen.');
      path.push({ p: W(w0 - dr, 0), tag: reliefTag });
      if (relief.type === 'rect') {
        path.push({ p: W(w0 - dr, s - dr), tag: reliefTag });
        path.push({ p: W(w0, s - dr), tag: reliefTag });
      } else {
        path.push({ p: W(w0 - dr, s - dr / 2), tag: reliefTag });
        for (const p of arc(w0 - dr / 2, s - dr / 2, dr / 2, Math.PI, 2 * Math.PI, arcSteps)) path.push({ p, tag: reliefTag });
        path.push({ p: W(w0, s - dr / 2), tag: reliefTag });
      }
      path.push({ p: W(w0, s), tag: seam });
    }
  }

  // ----- end of the edge -----
  if (full1) {
    if (sZero) {
      stripW1 = len;
    } else {
      const next = ring.pts[(k + 2) % n];
      const lam = lineIntersect(W(0, s), d, Q, sub2(next, Q));
      if (lam === null) throw new Error('Die Nachbarkante verläuft parallel zur gewählten Kante. Bitte eine Teilbreite verwenden.');
      const Qn = W(lam, s);
      const along = dot2(sub2(Qn, Q), sub2(next, Q)) / dot2(sub2(next, Q), sub2(next, Q));
      if (s < 0 && along > 1 - 1e-6) throw new Error('Die Nachbarkante ist zu kurz für diese Laschenposition.');
      replaceQ = Qn;
      stripW1 = lam;
    }
  } else {
    if (sZero) {
      path.push({ p: W(w1, 0), tag: origTag });
    } else if (s > 0) {
      path.push({ p: W(w1, s), tag: tabTag });
      path.push({ p: W(w1, 0), tag: origTag });
    } else if (relief.type === 'tear') {
      path.push({ p: W(w1, s), tag: reliefTag });
      path.push({ p: W(w1, 0), tag: origTag });
    } else {
      if (w1 + dr > len - TOL) throw new Error('Der Freistich reicht bis an die Ecke. Bitte den Abstand zur Kantenecke vergrößern oder einen Riss-Freistich wählen.');
      path.push({ p: W(w1, s), tag: reliefTag });
      if (relief.type === 'rect') {
        path.push({ p: W(w1, s - dr), tag: reliefTag });
        path.push({ p: W(w1 + dr, s - dr), tag: reliefTag });
      } else {
        path.push({ p: W(w1, s - dr / 2), tag: reliefTag });
        for (const p of arc(w1 + dr / 2, s - dr / 2, dr / 2, Math.PI, 2 * Math.PI, arcSteps)) path.push({ p, tag: reliefTag });
        path.push({ p: W(w1 + dr, s - dr / 2), tag: reliefTag });
      }
      path.push({ p: W(w1 + dr, 0), tag: origTag });
    }
  }

  if (stripW1 - stripW0 < 1e-3) throw new Error('Die Kante ist nach dem Trimmen zu kurz für eine Lasche.');

  // rebuild the ring starting at P
  const pts: V2[] = [];
  const tags: string[] = [];
  for (const v of path) {
    pts.push(v.p);
    tags.push(v.tag);
  }
  for (let i = 1; i < n; i++) {
    const idx = (k + i) % n;
    pts.push(i === 1 && replaceQ ? replaceQ : ring.pts[idx]);
    tags.push(ring.tags[idx]);
  }
  const next: Ring = simplifyRing({ pts, tags });
  if (!isSimple(next.pts)) {
    throw new Error('Die Fläche würde sich selbst überschneiden. Bitte Position, Freistich oder Breite der Lasche ändern.');
  }
  for (const h of face.holes) {
    if (ringsCross(next.pts, h.pts) || !pointInPolygon(h.pts[0], next.pts)) {
      throw new Error('Ein Ausschnitt liegt im Bereich der neuen Biegung. Bitte den Ausschnitt verschieben.');
    }
  }
  face.outer = orientRing(next, true);
  return { origin: W(0, s), dir: d, nrm: o, w0: stripW0, w1: stripW1 };
}

export interface RectTags {
  seam: string;
  side0: string;
  end: string;
  side1: string;
}

/** Rectangle in a strip frame for t ∈ [t0, t1]; the edge at t0 is the seam. */
export function rectRing(frame: StripFrame, t0: number, t1: number, tags: RectTags): Ring {
  const { origin, dir, nrm, w0, w1 } = frame;
  const c1 = frame2(origin, dir, nrm, w0, t0);
  const c2 = frame2(origin, dir, nrm, w1, t0);
  const c3 = frame2(origin, dir, nrm, w1, t1);
  const c4 = frame2(origin, dir, nrm, w0, t1);
  // (dir, nrm) is a clockwise frame, so this order is counter-clockwise
  const ring: Ring = { pts: [c1, c4, c3, c2], tags: [tags.side0, tags.end, tags.side1, tags.seam] };
  if (Math.abs(cross2(dir, nrm)) < EPS) throw new Error('Ungültiger Biegerahmen.');
  return orientRing(ring, true);
}
