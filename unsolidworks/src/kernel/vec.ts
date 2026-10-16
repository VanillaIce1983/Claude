// Small, dependency-free vector and rigid-transform math for the kernel.
// 2D points live in the flat (unfolded) plane, 3D points in model space.

export interface V2 {
  x: number;
  y: number;
}

export interface V3 {
  x: number;
  y: number;
  z: number;
}

export const EPS = 1e-7;

export const v2 = (x: number, y: number): V2 => ({ x, y });
export const add2 = (a: V2, b: V2): V2 => ({ x: a.x + b.x, y: a.y + b.y });
export const sub2 = (a: V2, b: V2): V2 => ({ x: a.x - b.x, y: a.y - b.y });
export const scale2 = (a: V2, s: number): V2 => ({ x: a.x * s, y: a.y * s });
export const dot2 = (a: V2, b: V2): number => a.x * b.x + a.y * b.y;
export const cross2 = (a: V2, b: V2): number => a.x * b.y - a.y * b.x;
export const len2 = (a: V2): number => Math.hypot(a.x, a.y);
export const dist2 = (a: V2, b: V2): number => Math.hypot(a.x - b.x, a.y - b.y);
export const lerp2 = (a: V2, b: V2, t: number): V2 => ({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t });

export function norm2(a: V2): V2 {
  const l = len2(a);
  if (l < EPS) throw new Error('Nullvektor kann nicht normiert werden');
  return { x: a.x / l, y: a.y / l };
}

/** Rotate 90° clockwise: for a CCW polygon edge direction this is the outward normal. */
export const rotCW = (a: V2): V2 => ({ x: a.y, y: -a.x });
/** Rotate 90° counter-clockwise. */
export const rotCCW = (a: V2): V2 => ({ x: -a.y, y: a.x });

/** Point on the frame spanned by an origin, an along-direction and an across-direction. */
export const frame2 = (o: V2, d: V2, n: V2, w: number, t: number): V2 => ({
  x: o.x + d.x * w + n.x * t,
  y: o.y + d.y * w + n.y * t,
});

export const v3 = (x: number, y: number, z: number): V3 => ({ x, y, z });
export const add3 = (a: V3, b: V3): V3 => ({ x: a.x + b.x, y: a.y + b.y, z: a.z + b.z });
export const sub3 = (a: V3, b: V3): V3 => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
export const scale3 = (a: V3, s: number): V3 => ({ x: a.x * s, y: a.y * s, z: a.z * s });
export const dot3 = (a: V3, b: V3): number => a.x * b.x + a.y * b.y + a.z * b.z;
export const cross3 = (a: V3, b: V3): V3 => ({
  x: a.y * b.z - a.z * b.y,
  y: a.z * b.x - a.x * b.z,
  z: a.x * b.y - a.y * b.x,
});
export const len3 = (a: V3): number => Math.hypot(a.x, a.y, a.z);
export const dist3 = (a: V3, b: V3): number => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);

export function norm3(a: V3): V3 {
  const l = len3(a);
  if (l < EPS) throw new Error('Nullvektor kann nicht normiert werden');
  return { x: a.x / l, y: a.y / l, z: a.z / l };
}

/**
 * Rigid (or general affine) transform: p' = R·p + t.
 * `r` is row-major 3x3.
 */
export interface Affine {
  r: [number, number, number, number, number, number, number, number, number];
  t: V3;
}

export const IDENTITY: Affine = { r: [1, 0, 0, 0, 1, 0, 0, 0, 1], t: { x: 0, y: 0, z: 0 } };

export function applyA(a: Affine, p: V3): V3 {
  const r = a.r;
  return {
    x: r[0] * p.x + r[1] * p.y + r[2] * p.z + a.t.x,
    y: r[3] * p.x + r[4] * p.y + r[5] * p.z + a.t.y,
    z: r[6] * p.x + r[7] * p.y + r[8] * p.z + a.t.z,
  };
}

export function applyDirA(a: Affine, p: V3): V3 {
  const r = a.r;
  return {
    x: r[0] * p.x + r[1] * p.y + r[2] * p.z,
    y: r[3] * p.x + r[4] * p.y + r[5] * p.z,
    z: r[6] * p.x + r[7] * p.y + r[8] * p.z,
  };
}

/** Composition: (a ∘ b)(p) = a(b(p)). */
export function composeA(a: Affine, b: Affine): Affine {
  const A = a.r;
  const B = b.r;
  const r: Affine['r'] = [
    A[0] * B[0] + A[1] * B[3] + A[2] * B[6],
    A[0] * B[1] + A[1] * B[4] + A[2] * B[7],
    A[0] * B[2] + A[1] * B[5] + A[2] * B[8],
    A[3] * B[0] + A[4] * B[3] + A[5] * B[6],
    A[3] * B[1] + A[4] * B[4] + A[5] * B[7],
    A[3] * B[2] + A[4] * B[5] + A[5] * B[8],
    A[6] * B[0] + A[7] * B[3] + A[8] * B[6],
    A[6] * B[1] + A[7] * B[4] + A[8] * B[7],
    A[6] * B[2] + A[7] * B[5] + A[8] * B[8],
  ];
  return { r, t: applyA(a, b.t) };
}

/** Inverse of a rigid transform (rotation + translation). */
export function invertRigid(a: Affine): Affine {
  const R = a.r;
  const rt: Affine['r'] = [R[0], R[3], R[6], R[1], R[4], R[7], R[2], R[5], R[8]];
  const inv: Affine = { r: rt, t: { x: 0, y: 0, z: 0 } };
  const t = applyDirA(inv, a.t);
  inv.t = { x: -t.x, y: -t.y, z: -t.z };
  return inv;
}

export function translationA(t: V3): Affine {
  return { r: [1, 0, 0, 0, 1, 0, 0, 0, 1], t: { ...t } };
}

/** Rotation by `angle` (right-hand rule) about the line through `point` with unit direction `axis`. */
export function rotationAboutLine(point: V3, axis: V3, angle: number): Affine {
  const { x, y, z } = norm3(axis);
  const c = Math.cos(angle);
  const s = Math.sin(angle);
  const C = 1 - c;
  const r: Affine['r'] = [
    c + x * x * C,
    x * y * C - z * s,
    x * z * C + y * s,
    y * x * C + z * s,
    c + y * y * C,
    y * z * C - x * s,
    z * x * C - y * s,
    z * y * C + x * s,
    c + z * z * C,
  ];
  const rot: Affine = { r, t: { x: 0, y: 0, z: 0 } };
  const rp = applyDirA(rot, point);
  rot.t = { x: point.x - rp.x, y: point.y - rp.y, z: point.z - rp.z };
  return rot;
}

/** Frame transform from basis vectors (columns) and an origin. */
export function frameA(ex: V3, ey: V3, ez: V3, origin: V3): Affine {
  return { r: [ex.x, ey.x, ez.x, ex.y, ey.y, ez.y, ex.z, ey.z, ez.z], t: { ...origin } };
}

export function determinantA(a: Affine): number {
  const r = a.r;
  return (
    r[0] * (r[4] * r[8] - r[5] * r[7]) - r[1] * (r[3] * r[8] - r[5] * r[6]) + r[2] * (r[3] * r[7] - r[4] * r[6])
  );
}

export const deg = (rad: number): number => (rad * 180) / Math.PI;
export const rad = (degrees: number): number => (degrees * Math.PI) / 180;

/** Round for display: avoids "-0" and float noise. */
export function fmt(n: number, digits = 2): string {
  const v = Math.abs(n) < 0.5 * 10 ** -digits ? 0 : n;
  const s = v.toFixed(digits);
  return s.includes('.') ? s.replace(/0+$/, '').replace(/\.$/, '') : s;
}
