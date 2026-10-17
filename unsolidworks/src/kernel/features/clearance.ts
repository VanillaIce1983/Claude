// Keeps a new hem clear of the rest of the part. A hem is a prism along its
// edge (bend strip plus leg), so the places where it would run into another
// piece are intervals along that edge, found by moving the hem's cross-section
// along the edge direction.

import { Bend, Face, Part, computePlacements, stripFractions, stripPoint, stripRange } from '../model';
import { distPointSegment, pointInPolygon } from '../polygon';
import { Affine, V2, V3, applyA, applyDirA, frame2, invertRigid, norm3, v2, v3 } from '../vec';

/** Gap a shortened hem keeps to the sheet it would otherwise touch (mm). */
export const HEM_CLEARANCE = 0.1;

const TOUCH = 1e-6;

/** Points around the cross-section of bend `b` and its leg at w = wRef, in model space. */
function crossSection(part: Part, placements: Map<string, Affine>, b: Bend, wRef: number): V3[] {
  const T = part.T;
  const MA = placements.get(b.faceA)!;
  const pts: V3[] = [];
  const n = Math.max(8, Math.ceil(b.angle / (Math.PI / 32)));
  for (let i = 0; i <= n; i++) for (const h of [0, T / 2, T]) pts.push(applyA(MA, stripPoint(b, T, wRef, i / n, h)));
  const leg = b.faceB ? part.faces.get(b.faceB) : undefined;
  const MB = b.faceB ? placements.get(b.faceB) : undefined;
  if (leg && MB) {
    // the leg is the rectangle t ∈ [ba, end] of the strip frame, in its own placement
    const ts = leg.outer.pts.map((p) => (p.x - b.origin.x) * b.nrm.x + (p.y - b.origin.y) * b.nrm.y);
    const end = Math.max(...ts);
    for (let i = 0; i <= 8; i++) {
      const p = frame2(b.origin, b.dir, b.nrm, wRef, b.ba + ((end - b.ba) * i) / 8);
      for (const h of [0, T / 2, T]) pts.push(applyA(MB, v3(p.x, p.y, h)));
    }
  }
  return pts;
}

function onOrInside(p: V2, face: Face): boolean {
  const rings = [face.outer, ...face.holes];
  if (rings.some((r) => r.pts.some((a, i) => distPointSegment(p, a, r.pts[(i + 1) % r.pts.length]) <= TOUCH))) return true;
  return pointInPolygon(p, face.outer.pts) && !face.holes.some((h) => pointInPolygon(p, h.pts));
}

/** Parameters t where c + t·d lies in (or on) the sheet of a planar face. */
function lineInFace(part: Part, face: Face, M: Affine, c: V3, d: V3): [number, number][] {
  const inv = invertRigid(M);
  const p = applyA(inv, c);
  const q = applyDirA(inv, d);
  let lo = -1e9;
  let hi = 1e9;
  if (Math.abs(q.z) < 1e-12) {
    if (p.z < -TOUCH || p.z > part.T + TOUCH) return [];
  } else {
    const t0 = (-TOUCH - p.z) / q.z;
    const t1 = (part.T + TOUCH - p.z) / q.z;
    lo = Math.min(t0, t1);
    hi = Math.max(t0, t1);
  }
  const ts = [lo, hi];
  const den = (a: V2, b: V2): number => q.x * (b.y - a.y) - q.y * (b.x - a.x);
  for (const r of [face.outer, ...face.holes]) {
    r.pts.forEach((a, i) => {
      const b = r.pts[(i + 1) % r.pts.length];
      const dd = den(a, b);
      if (Math.abs(dd) < 1e-12) return;
      // p + t·q = a + u·(b − a)
      const t = ((a.x - p.x) * (b.y - a.y) - (a.y - p.y) * (b.x - a.x)) / dd;
      const u = ((a.x - p.x) * q.y - (a.y - p.y) * q.x) / dd;
      if (u >= -1e-9 && u <= 1 + 1e-9 && t > lo && t < hi) ts.push(t);
    });
  }
  ts.sort((x, y) => x - y);
  const out: [number, number][] = [];
  for (let i = 0; i + 1 < ts.length; i++) {
    const m = (ts[i] + ts[i + 1]) / 2;
    if (onOrInside(v2(p.x + m * q.x, p.y + m * q.y), face)) {
      const last = out[out.length - 1];
      if (last && ts[i] - last[1] < 1e-9) last[1] = ts[i + 1];
      else out.push([ts[i], ts[i + 1]]);
    }
  }
  // a line that only grazes a corner still touches
  for (const t of ts) if (!out.some(([a, b]) => t >= a - 1e-9 && t <= b + 1e-9) && onOrInside(v2(p.x + t * q.x, p.y + t * q.y), face)) out.push([t, t]);
  return out;
}

/**
 * Parameters t (sampled every 0.02 mm) where c + t·d lies in the sheet of a
 * bend strip. The line is clipped to the strip's bounding box first.
 */
function lineInBend(part: Part, placements: Map<string, Affine>, b: Bend, c: V3, d: V3): [number, number][] {
  const M = placements.get(b.faceA);
  if (!M) return [];
  const T = part.T;
  const inv = invertRigid(M);
  const p = applyA(inv, c);
  const q = applyDirA(inv, d);
  const rel = v2(p.x - b.origin.x, p.y - b.origin.y);
  // the line in the strip's own coordinates: along the bend line, across it, height
  const at = { w: rel.x * b.dir.x + rel.y * b.dir.y, o: rel.x * b.nrm.x + rel.y * b.nrm.y, z: p.z };
  const dt = { w: q.x * b.dir.x + q.y * b.dir.y, o: q.x * b.nrm.x + q.y * b.nrm.y, z: q.z };
  const ranges = stripFractions(b).map((f) => stripRange(b, f));
  const rr = b.radius + T + TOUCH;
  const zc = b.up ? T + b.radius : -b.radius;
  const box: [keyof typeof at, number, number][] = [
    ['w', Math.min(...ranges.map((r) => r[0]), b.w0) - TOUCH, Math.max(...ranges.map((r) => r[1]), b.w1) + TOUCH],
    ['o', -rr, rr],
    ['z', zc - rr, zc + rr],
  ];
  let t0 = -Infinity;
  let t1 = Infinity;
  for (const [k, lo, hi] of box) {
    if (Math.abs(dt[k]) < 1e-12) {
      if (at[k] < lo || at[k] > hi) return [];
      continue;
    }
    const [a, z] = [(lo - at[k]) / dt[k], (hi - at[k]) / dt[k]];
    t0 = Math.max(t0, Math.min(a, z));
    t1 = Math.min(t1, Math.max(a, z));
  }
  if (!(t0 <= t1)) return [];
  const step = 0.02;
  const out: [number, number][] = [];
  for (let t = t0; t <= t1 + 1e-12; t += step) {
    const w = at.w + t * dt.w;
    const o = at.o + t * dt.o;
    const dz = b.up ? zc - (at.z + t * dt.z) : at.z + t * dt.z - zc;
    let phi = Math.atan2(o, dz);
    if (phi < -1e-9) phi += 2 * Math.PI;
    const rho = Math.hypot(o, dz);
    if (phi > b.angle + 1e-9 || rho < b.radius - TOUCH || rho > b.radius + T + TOUCH) continue;
    const [wl, wr] = stripRange(b, Math.min(1, phi / b.angle));
    if (w < wl - TOUCH || w > wr + TOUCH) continue;
    const last = out[out.length - 1];
    if (last && t - last[1] <= step + 1e-9) last[1] = Math.min(t1, t + step);
    else out.push([Math.max(t0, t - step), Math.min(t1, t + step)]);
  }
  return out;
}

/**
 * The width [w0, w1] bend `bendId` (with its leg) may keep so that it stays
 * HEM_CLEARANCE away from every piece not in `own`; null when it is clear
 * already. A conflict in the middle of the edge, or one that leaves too
 * little width, throws.
 */
export function clearWidth(part: Part, bendId: string, own: Set<string>): [number, number] | null {
  const b = part.bends.get(bendId)!;
  const placements = computePlacements(part);
  const MA = placements.get(b.faceA);
  if (!MA) return null;
  const wRef = (b.w0 + b.w1) / 2;
  const d = norm3(applyDirA(MA, v3(b.dir.x, b.dir.y, 0)));
  let w0 = b.w0;
  let w1 = b.w1;
  const hit = (a: number, z: number): void => {
    // as w along the edge; only what reaches the hem (plus the clearance) matters
    const [lo, hi] = [wRef + a, wRef + z];
    if (hi < b.w0 - HEM_CLEARANCE || lo > b.w1 + HEM_CLEARANCE) return;
    if ((lo + hi) / 2 < wRef) w0 = Math.max(w0, hi + HEM_CLEARANCE);
    else w1 = Math.min(w1, lo - HEM_CLEARANCE);
  };
  for (const c of crossSection(part, placements, b, wRef)) {
    for (const face of part.faces.values()) {
      const M = placements.get(face.id);
      if (!M || own.has(face.id)) continue;
      for (const [a, z] of lineInFace(part, face, M, c, d)) hit(a, z);
    }
    for (const other of part.bends.values()) {
      if (own.has(other.id) || !placements.has(other.faceA)) continue;
      for (const [a, z] of lineInBend(part, placements, other, c, d)) hit(a, z);
    }
  }
  if (Math.abs(w0 - b.w0) < 1e-9 && Math.abs(w1 - b.w1) < 1e-9) return null;
  if (w1 - w0 < 1) throw new Error('Der Saum würde in eine benachbarte Lasche laufen. Bitte die Saumbreite verringern oder die Lasche kürzen.');
  return [w0, w1];
}
