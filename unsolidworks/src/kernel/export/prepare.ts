// Geometry handed to solid exports (STEP, 3MF). A solid must not touch
// itself, so places where the sheet rests on itself get a hairline gap:
// - a bend with inner radius 0 (closed hem) gets a minimal inner radius,
// - a teardrop hem's tail stops just short of the face it returns to.
// The flat pattern and all other dimensions stay unchanged.

import { Part, clonePart } from '../model';
import { dot2, scale2, sub2 } from '../vec';

export const MIN_EXPORT_RADIUS = 0.001;

export function exportPart(part: Part): Part {
  const p = clonePart(part);
  for (const b of p.bends.values()) {
    if (b.radius < MIN_EXPORT_RADIUS) b.radius = MIN_EXPORT_RADIUS;
    const tail = b.kind === 'hem' && b.angle > Math.PI + 1e-9 && b.faceB ? p.faces.get(b.faceB) : undefined;
    if (!tail) continue;
    const t = (q: { x: number; y: number }): number => dot2(sub2(q, b.origin), b.nrm);
    const end = Math.max(...tail.outer.pts.map(t));
    tail.outer.pts = tail.outer.pts.map((q) => (t(q) > end - 1e-9 ? sub2(q, scale2(b.nrm, MIN_EXPORT_RADIUS)) : q));
  }
  return p;
}
