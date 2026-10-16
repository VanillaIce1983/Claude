import { EPS, V2, cross2, dist2, dot2, sub2, v2 } from './vec';

/**
 * A closed polygon ring in flat coordinates. `tags[i]` names the edge
 * from `pts[i]` to `pts[(i + 1) % n]`. Tags are how features refer to edges.
 */
export interface Ring {
  pts: V2[];
  tags: string[];
}

export const GEO_TOL = 1e-6;

export function cloneRing(r: Ring): Ring {
  return { pts: r.pts.map((p) => ({ ...p })), tags: [...r.tags] };
}

export function signedArea(pts: V2[]): number {
  let a = 0;
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i];
    const q = pts[(i + 1) % pts.length];
    a += p.x * q.y - q.x * p.y;
  }
  return a / 2;
}

/** Reverse a ring while keeping each tag on the same geometric edge. */
export function reverseRing(r: Ring): Ring {
  const n = r.pts.length;
  const pts: V2[] = [];
  const tags: string[] = [];
  for (let j = 0; j < n; j++) {
    pts.push(r.pts[n - 1 - j]);
    tags.push(r.tags[(((n - 2 - j) % n) + n) % n]);
  }
  return { pts, tags };
}

export function orientRing(r: Ring, ccw: boolean): Ring {
  const a = signedArea(r.pts);
  return (a > 0) === ccw ? r : reverseRing(r);
}

export function edgeAt(r: Ring, i: number): { a: V2; b: V2; tag: string } {
  return { a: r.pts[i], b: r.pts[(i + 1) % r.pts.length], tag: r.tags[i] };
}

export function pointInPolygon(p: V2, pts: V2[]): boolean {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const a = pts[i];
    const b = pts[j];
    if (a.y > p.y !== b.y > p.y) {
      const x = ((b.x - a.x) * (p.y - a.y)) / (b.y - a.y) + a.x;
      if (p.x < x) inside = !inside;
    }
  }
  return inside;
}

export function distPointSegment(p: V2, a: V2, b: V2): number {
  const ab = sub2(b, a);
  const l2 = dot2(ab, ab);
  if (l2 < EPS * EPS) return dist2(p, a);
  let t = dot2(sub2(p, a), ab) / l2;
  t = Math.max(0, Math.min(1, t));
  return dist2(p, { x: a.x + ab.x * t, y: a.y + ab.y * t });
}

/** True when the closed segments ab and cd share at least one point (touching counts). */
export function segmentsIntersect(a: V2, b: V2, c: V2, d: V2, tol = GEO_TOL): boolean {
  const r = sub2(b, a);
  const s = sub2(d, c);
  const denom = cross2(r, s);
  const ca = sub2(c, a);
  const lr = Math.hypot(r.x, r.y);
  const ls = Math.hypot(s.x, s.y);
  if (Math.abs(denom) < tol * Math.max(1, lr * ls)) {
    // parallel: only collinear segments can touch
    if (Math.abs(cross2(ca, r)) > tol * Math.max(1, lr)) return false;
    const rr = dot2(r, r);
    if (rr < tol * tol) return false;
    const t0 = dot2(ca, r) / rr;
    const t1 = t0 + dot2(s, r) / rr;
    const lenTol = tol / Math.sqrt(rr);
    return Math.max(t0, t1) >= -lenTol && Math.min(t0, t1) <= 1 + lenTol;
  }
  const t = cross2(ca, s) / denom;
  const u = cross2(ca, r) / denom;
  const tt = tol / Math.max(lr, tol);
  const uu = tol / Math.max(ls, tol);
  return t >= -tt && t <= 1 + tt && u >= -uu && u <= 1 + uu;
}

/** Simple-polygon test (no self intersections, no repeated vertices, positive extent). */
export function isSimple(pts: V2[]): boolean {
  const n = pts.length;
  if (n < 3) return false;
  for (let i = 0; i < n; i++) {
    if (dist2(pts[i], pts[(i + 1) % n]) < GEO_TOL) return false;
  }
  for (let i = 0; i < n; i++) {
    const a = pts[i];
    const b = pts[(i + 1) % n];
    for (let j = i + 1; j < n; j++) {
      if (j === i || (j + 1) % n === i || (i + 1) % n === j) continue;
      if (segmentsIntersect(a, b, pts[j], pts[(j + 1) % n])) return false;
    }
  }
  return Math.abs(signedArea(pts)) > GEO_TOL;
}

/** Whether two rings cross each other (any edge pair intersects). */
export function ringsCross(a: V2[], b: V2[]): boolean {
  for (let i = 0; i < a.length; i++) {
    const p = a[i];
    const q = a[(i + 1) % a.length];
    for (let j = 0; j < b.length; j++) {
      if (segmentsIntersect(p, q, b[j], b[(j + 1) % b.length])) return true;
    }
  }
  return false;
}

/** Remove vertices whose two edges are collinear and carry the same tag. */
export function simplifyRing(r: Ring): Ring {
  let pts = r.pts.slice();
  let tags = r.tags.slice();
  let changed = true;
  while (changed && pts.length > 3) {
    changed = false;
    for (let i = 0; i < pts.length; i++) {
      const prev = (i - 1 + pts.length) % pts.length;
      const next = (i + 1) % pts.length;
      const a = pts[prev];
      const b = pts[i];
      const c = pts[next];
      const sameTag = tags[prev] === tags[i];
      const ab = sub2(b, a);
      const bc = sub2(c, b);
      const degenerate = dist2(a, b) < GEO_TOL;
      const collinear =
        Math.abs(cross2(ab, bc)) < GEO_TOL * Math.max(1, Math.hypot(ab.x, ab.y) + Math.hypot(bc.x, bc.y)) &&
        dot2(ab, bc) > 0;
      if (degenerate || (sameTag && collinear)) {
        // a zero-length edge gives way to the real edge that follows it
        if (degenerate) tags[prev] = tags[i];
        pts.splice(i, 1);
        tags.splice(i, 1);
        changed = true;
        break;
      }
    }
  }
  return { pts, tags };
}

export interface SplitResult {
  neg: Ring | null;
  pos: Ring | null;
}

/**
 * Split a simple ring by the line {X : dot(X - origin, normal) = c}.
 * Supports the common case where the line crosses the outline exactly twice.
 * The cut edge on both pieces gets `cutTag`.
 */
export function splitRing(r: Ring, origin: V2, normal: V2, c: number, cutTag: string): SplitResult {
  const n = r.pts.length;
  const d = r.pts.map((p) => dot2(sub2(p, origin), normal) - c);
  const sgn = d.map((x) => (Math.abs(x) < GEO_TOL ? 0 : x > 0 ? 1 : -1));
  if (sgn.every((s) => s >= 0)) return { neg: null, pos: r };
  if (sgn.every((s) => s <= 0)) return { neg: r, pos: null };

  // augmented sequence with intersection points inserted
  const pts: V2[] = [];
  const tags: string[] = [];
  const side: number[] = [];
  for (let i = 0; i < n; i++) {
    const j = (i + 1) % n;
    pts.push(r.pts[i]);
    tags.push(r.tags[i]);
    side.push(sgn[i]);
    if (sgn[i] * sgn[j] < 0) {
      const t = d[i] / (d[i] - d[j]);
      const a = r.pts[i];
      const b = r.pts[j];
      pts.push(v2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t));
      tags.push(r.tags[i]);
      side.push(0);
    }
  }

  // count sign changes in cyclic order (ignoring points on the line)
  const nonzero = side.filter((s) => s !== 0);
  let changes = 0;
  for (let i = 0; i < nonzero.length; i++) {
    if (nonzero[i] !== nonzero[(i + 1) % nonzero.length]) changes++;
  }
  if (changes > 2) {
    throw new Error('Die Linie schneidet die Fläche mehrfach. Bitte eine einfachere Fläche oder einen anderen Abstand wählen.');
  }

  const build = (keep: (s: number) => boolean): Ring | null => {
    const m = pts.length;
    const idx: number[] = [];
    for (let i = 0; i < m; i++) if (keep(side[i])) idx.push(i);
    if (idx.length < 3) return null;
    const outPts: V2[] = [];
    const outTags: string[] = [];
    for (let k = 0; k < idx.length; k++) {
      const i = idx[k];
      const next = idx[(k + 1) % idx.length];
      outPts.push(pts[i]);
      outTags.push((i + 1) % m === next ? tags[i] : cutTag);
    }
    const ring = simplifyRing({ pts: outPts, tags: outTags });
    return Math.abs(signedArea(ring.pts)) > GEO_TOL ? ring : null;
  };

  return { neg: build((s) => s <= 0), pos: build((s) => s >= 0) };
}

/** Polygon approximating a circle, CW (as needed for holes) or CCW. */
export function circlePoints(c: V2, r: number, segments: number, ccw: boolean): V2[] {
  const out: V2[] = [];
  for (let i = 0; i < segments; i++) {
    const a = ((ccw ? 1 : -1) * 2 * Math.PI * i) / segments;
    out.push(v2(c.x + r * Math.cos(a), c.y + r * Math.sin(a)));
  }
  return out;
}

export function bbox2(pts: V2[]): { minX: number; minY: number; maxX: number; maxY: number } {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const p of pts) {
    minX = Math.min(minX, p.x);
    minY = Math.min(minY, p.y);
    maxX = Math.max(maxX, p.x);
    maxY = Math.max(maxY, p.y);
  }
  return { minX, minY, maxX, maxY };
}

/** Area of the part of `subject` (any simple polygon) inside the convex polygon `clip` (CCW). */
export function clippedArea(subject: V2[], clip: V2[]): number {
  let out = subject.slice();
  for (let i = 0; i < clip.length && out.length; i++) {
    const a = clip[i];
    const b = clip[(i + 1) % clip.length];
    const inside = (p: V2): boolean => cross2(sub2(b, a), sub2(p, a)) >= -1e-12;
    const input = out;
    out = [];
    for (let j = 0; j < input.length; j++) {
      const p = input[j];
      const q = input[(j + 1) % input.length];
      const pin = inside(p);
      const qin = inside(q);
      if (pin) out.push(p);
      if (pin !== qin) {
        const d1 = cross2(sub2(b, a), sub2(p, a));
        const d2 = cross2(sub2(b, a), sub2(q, a));
        const t = d1 / (d1 - d2);
        out.push(v2(p.x + (q.x - p.x) * t, p.y + (q.y - p.y) * t));
      }
    }
  }
  return out.length >= 3 ? Math.abs(signedArea(out)) : 0;
}
