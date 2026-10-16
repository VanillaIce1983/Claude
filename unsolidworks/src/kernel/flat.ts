// Flat pattern (Abwicklung): cut contours, bend lines and stock size.

import type { PartMesh } from './mesh';
import type { Part } from './model';
import { stripFlat } from './model';
import { signedArea } from './polygon';
import { bendDeduction } from './sheet';
import { V2, cross2, deg, dist2, sub2, v2 } from './vec';

export interface FlatLoop {
  pts: V2[];
  /** Tag per edge, same indexing as in Ring. */
  tags: string[];
  /** If the whole loop is one round hole: its exact circle. */
  circle?: { c: V2; r: number };
  outer: boolean;
}

export interface FlatBendLine {
  id: string;
  feature: string;
  a: V2;
  b: V2;
  /** Region boundaries (tangent lines) of the bend zone. */
  regionA: [V2, V2];
  regionB: [V2, V2];
  up: boolean;
  angleDeg: number;
  radius: number;
  ba: number;
  bd: number;
  length: number;
  calc: string;
}

export interface FlatPattern {
  loops: FlatLoop[];
  bendLines: FlatBendLine[];
  bounds: { minX: number; minY: number; maxX: number; maxY: number };
  /** Smallest enclosing rectangle (any rotation). */
  minRect: { w: number; h: number; angleDeg: number };
  area: number;
  cutLength: number;
}

/** Chain directed boundary edges into closed loops. */
function chainLoops(mesh: PartMesh): { idx: number[]; tags: string[] }[] {
  const out = new Map<number, { b: number; tag: string }[]>();
  for (const e of mesh.boundary) {
    if (!out.has(e.a)) out.set(e.a, []);
    out.get(e.a)!.push({ b: e.b, tag: e.tag });
  }
  const loops: { idx: number[]; tags: string[] }[] = [];
  for (const start of [...out.keys()]) {
    while ((out.get(start)?.length ?? 0) > 0) {
      const idx: number[] = [];
      const tags: string[] = [];
      let cur = start;
      let guard = 0;
      while (guard++ < 1_000_000) {
        const list = out.get(cur);
        if (!list || list.length === 0) break;
        const e = list.shift()!;
        idx.push(cur);
        tags.push(e.tag);
        cur = e.b;
        if (cur === start) break;
      }
      if (idx.length >= 2) loops.push({ idx, tags });
    }
  }
  return loops;
}

/** Merge consecutive collinear edges (keeps tags of round holes intact). */
function simplifyLoop(pts: V2[], tags: string[]): { pts: V2[]; tags: string[] } {
  const P = pts.slice();
  const Tg = tags.slice();
  let changed = true;
  while (changed && P.length > 3) {
    changed = false;
    for (let i = 0; i < P.length; i++) {
      const prev = (i - 1 + P.length) % P.length;
      const next = (i + 1) % P.length;
      const ab = sub2(P[i], P[prev]);
      const bc = sub2(P[next], P[i]);
      const lab = Math.hypot(ab.x, ab.y);
      const lbc = Math.hypot(bc.x, bc.y);
      if (lab < 1e-7) {
        Tg[prev] = Tg[i];
        P.splice(i, 1);
        Tg.splice(i, 1);
        changed = true;
        break;
      }
      const straight = Math.abs(cross2(ab, bc)) < 1e-7 * lab * lbc + 1e-9 && ab.x * bc.x + ab.y * bc.y > 0;
      if (straight) {
        P.splice(i, 1);
        Tg.splice(i, 1);
        changed = true;
        break;
      }
    }
  }
  return { pts: P, tags: Tg };
}

function convexHull(points: V2[]): V2[] {
  const pts = [...points].sort((a, b) => a.x - b.x || a.y - b.y);
  if (pts.length < 3) return pts;
  const lower: V2[] = [];
  for (const p of pts) {
    while (lower.length >= 2 && cross2(sub2(lower[lower.length - 1], lower[lower.length - 2]), sub2(p, lower[lower.length - 1])) <= 0) lower.pop();
    lower.push(p);
  }
  const upper: V2[] = [];
  for (let i = pts.length - 1; i >= 0; i--) {
    const p = pts[i];
    while (upper.length >= 2 && cross2(sub2(upper[upper.length - 1], upper[upper.length - 2]), sub2(p, upper[upper.length - 1])) <= 0) upper.pop();
    upper.push(p);
  }
  upper.pop();
  lower.pop();
  return lower.concat(upper);
}

function minAreaRect(points: V2[]): { w: number; h: number; angleDeg: number } {
  const hull = convexHull(points);
  let best = { w: 0, h: 0, angleDeg: 0, area: Infinity };
  for (let i = 0; i < hull.length; i++) {
    const a = hull[i];
    const b = hull[(i + 1) % hull.length];
    const l = dist2(a, b);
    if (l < 1e-9) continue;
    const ux = (b.x - a.x) / l;
    const uy = (b.y - a.y) / l;
    let minU = Infinity, maxU = -Infinity, minV = Infinity, maxV = -Infinity;
    for (const p of hull) {
      const u = p.x * ux + p.y * uy;
      const v = -p.x * uy + p.y * ux;
      minU = Math.min(minU, u);
      maxU = Math.max(maxU, u);
      minV = Math.min(minV, v);
      maxV = Math.max(maxV, v);
    }
    const w = maxU - minU;
    const h = maxV - minV;
    if (w * h < best.area - 1e-9) {
      best = { w: Math.max(w, h), h: Math.min(w, h), angleDeg: deg(Math.atan2(uy, ux)), area: w * h };
    }
  }
  return { w: best.w, h: best.h, angleDeg: best.angleDeg };
}

export function flatPattern(part: Part, mesh: PartMesh): FlatPattern {
  const raw = chainLoops(mesh);
  const loops: FlatLoop[] = [];
  for (const r of raw) {
    const pts = r.idx.map((i) => v2(mesh.flat2[2 * i], mesh.flat2[2 * i + 1]));
    const tag0 = r.tags[0];
    const circle = r.tags.every((t) => t === tag0) ? part.circles.get(tag0) : undefined;
    const s = circle ? { pts, tags: r.tags } : simplifyLoop(pts, r.tags);
    loops.push({ pts: s.pts, tags: s.tags, circle: circle ? { c: circle.c, r: circle.r } : undefined, outer: false });
  }
  // the loop with the largest positive area is the outer contour
  let outerIdx = -1;
  let maxA = 0;
  loops.forEach((l, i) => {
    const a = signedArea(l.pts);
    if (a > maxA) {
      maxA = a;
      outerIdx = i;
    }
  });
  loops.forEach((l, i) => {
    l.outer = signedArea(l.pts) > 0;
    if (i === outerIdx) l.outer = true;
  });

  const bendLines: FlatBendLine[] = [];
  for (const b of part.bends.values()) {
    const a = stripFlat(b, b.w0, 0.5);
    const c = stripFlat(b, b.w1, 0.5);
    bendLines.push({
      id: b.id,
      feature: b.feature,
      a,
      b: c,
      regionA: [stripFlat(b, b.w0, 0), stripFlat(b, b.w1, 0)],
      regionB: [stripFlat(b, b.w0, 1), stripFlat(b, b.w1, 1)],
      up: b.up,
      angleDeg: deg(b.angle),
      radius: b.radius,
      ba: b.ba,
      bd: b.angle < Math.PI - 1e-9 ? bendDeduction(b.angle, b.radius, part.T, b.ba) : NaN,
      length: b.w1 - b.w0,
      calc: b.calc,
    });
  }

  const all = loops.flatMap((l) => l.pts);
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const p of all) {
    minX = Math.min(minX, p.x);
    minY = Math.min(minY, p.y);
    maxX = Math.max(maxX, p.x);
    maxY = Math.max(maxY, p.y);
  }
  const area = loops.reduce((s, l) => s + signedArea(l.pts), 0);
  const cutLength = loops.reduce((s, l) => {
    if (l.circle) return s + 2 * Math.PI * l.circle.r;
    let L = 0;
    for (let i = 0; i < l.pts.length; i++) L += dist2(l.pts[i], l.pts[(i + 1) % l.pts.length]);
    return s + L;
  }, 0);
  return {
    loops,
    bendLines,
    bounds: { minX, minY, maxX, maxY },
    minRect: all.length ? minAreaRect(all) : { w: 0, h: 0, angleDeg: 0 },
    area,
    cutLength,
  };
}
