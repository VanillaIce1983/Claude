// Bohrung: holes (round, rectangular, slot) on one face, placed relative
// to an edge of that face, optionally repeated along the edge.

import type { Part } from '../model';
import { Ring, circlePoints, cutClearance, orientRing, pointInPolygon, ringsWithin } from '../polygon';
import { sketchFrame } from '../sketch';
import { V2, add2, rad, scale2, v2 } from '../vec';
import type { CutFeature } from './types';

function circleSegments(r: number): number {
  return Math.max(24, Math.min(96, Math.round(r * 6)));
}

/** Shape outline in local coordinates (x along the reference edge, y into the face). */
function shapeOutline(f: CutFeature): V2[] {
  switch (f.shape) {
    case 'circle': {
      if (!(f.diameter > 0)) throw new Error('Der Durchmesser muss größer als 0 sein.');
      return circlePoints(v2(0, 0), f.diameter / 2, circleSegments(f.diameter / 2), true);
    }
    case 'rect': {
      if (!(f.width > 0 && f.height > 0)) throw new Error('Breite und Höhe müssen größer als 0 sein.');
      const w = f.width / 2;
      const h = f.height / 2;
      return [v2(-w, -h), v2(w, -h), v2(w, h), v2(-w, h)];
    }
    case 'slot': {
      if (!(f.width > 0 && f.height > 0)) throw new Error('Länge und Breite müssen größer als 0 sein.');
      if (f.width <= f.height) throw new Error('Die Langlochlänge muss größer als seine Breite sein.');
      const r = f.height / 2;
      const c = f.width / 2 - r;
      const seg = Math.max(8, Math.round(circleSegments(r) / 2));
      const pts: V2[] = [];
      for (let i = 0; i <= seg; i++) {
        const a = -Math.PI / 2 + (Math.PI * i) / seg;
        pts.push(v2(c + r * Math.cos(a), r * Math.sin(a)));
      }
      for (let i = 0; i <= seg; i++) {
        const a = Math.PI / 2 + (Math.PI * i) / seg;
        pts.push(v2(-c + r * Math.cos(a), r * Math.sin(a)));
      }
      return pts;
    }
  }
}

export function applyCut(part: Part, f: CutFeature): void {
  if (!f.edge.tag) throw new Error('Bitte auf die Fläche klicken, wo die Bohrung sitzen soll.');
  // same anchor as a face sketch, so a notch in the reference edge does not move the hole
  const { face, origin, x: d, y: inward } = sketchFrame(part, f.edge, f.anchor);
  const clear = cutClearance(face.outer.pts);
  const count = Math.max(1, Math.round(f.count || 1));
  if (count > 1 && !(f.spacing > 0)) throw new Error('Der Abstand im Muster muss größer als 0 sein.');
  const local = shapeOutline(f);
  const a = rad(f.angle || 0);
  const ca = Math.cos(a);
  const sa = Math.sin(a);
  for (let k = 0; k < count; k++) {
    const u = f.u + k * (f.spacing || 0);
    const center = add2(add2(origin, scale2(d, u)), scale2(inward, f.v));
    const pts = local.map((p) => {
      const x = p.x * ca - p.y * sa;
      const y = p.x * sa + p.y * ca;
      return add2(center, add2(scale2(d, x), scale2(inward, y)));
    });
    const tag = `${f.id}:h${k}`;
    const ring: Ring = orientRing({ pts, tags: pts.map(() => tag) }, false);
    // closer than `clear` counts as touching: the mesh would merge the hole into the edge
    if (!pts.every((p) => pointInPolygon(p, face.outer.pts)) || ringsWithin(ring.pts, face.outer.pts, clear)) {
      throw new Error(
        count > 1
          ? `Bohrung ${k + 1} liegt nicht vollständig auf der Fläche. Bitte Position oder Größe anpassen.`
          : 'Die Bohrung liegt nicht vollständig auf der Fläche. Bitte Position oder Größe anpassen.',
      );
    }
    for (const h of face.holes) {
      if (ringsWithin(ring.pts, h.pts, clear) || pointInPolygon(ring.pts[0], h.pts) || pointInPolygon(h.pts[0], ring.pts)) {
        throw new Error('Die Bohrung überschneidet eine vorhandene Bohrung oder einen Ausschnitt.');
      }
    }
    face.holes.push(ring);
    if (f.shape === 'circle') part.circles.set(tag, { c: center, r: f.diameter / 2 });
  }
}
