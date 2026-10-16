import { Bend, Face, Part, seamTag } from '../model';
import { Ring, isSimple, orientRing } from '../polygon';
import { SheetSettings, bendAllowance, describeCalc, insideSetback, outsideSetback, resolveBendParams } from '../sheet';
import { Affine, V3, add3, composeA, cross3, dist2, frameA, norm2, scale3, sub2, translationA, v2, v3 } from '../vec';
import type { BaseFlangeFeature, SketchPlane } from './types';

export interface PlaneBasis {
  ex: V3;
  ey: V3;
  n: V3;
}

/** Sketch planes: "Oben" (XZ), "Vorne" (XY), "Rechts" (YZ); Y points up. */
export const PLANES: Record<SketchPlane, PlaneBasis> = {
  top: { ex: v3(1, 0, 0), ey: v3(0, 0, -1), n: v3(0, 1, 0) },
  front: { ex: v3(1, 0, 0), ey: v3(0, 1, 0), n: v3(0, 0, 1) },
  right: { ex: v3(0, 0, -1), ey: v3(0, 1, 0), n: v3(1, 0, 0) },
};

export const PLANE_LABELS: Record<SketchPlane, string> = { top: 'Ebene oben', front: 'Ebene vorne', right: 'Ebene rechts' };

const sketchTo3 = (b: PlaneBasis, x: number, y: number): V3 => add3(scale3(b.ex, x), scale3(b.ey, y));

export function applyBaseFlange(part: Part, sheet: SheetSettings, f: BaseFlangeFeature): void {
  if (part.faces.size > 0) throw new Error('Es gibt bereits eine Basislasche. Ein Teil hat genau eine Basislasche.');
  const basis = PLANES[f.plane];
  if (f.closed) closedProfile(part, basis, f);
  else openProfile(part, sheet, basis, f);
}

function closedProfile(part: Part, basis: PlaneBasis, f: BaseFlangeFeature): void {
  if (f.points.length < 3) throw new Error('Ein geschlossenes Profil braucht mindestens drei Punkte.');
  if (!isSimple(f.points)) throw new Error('Das Skizzenprofil überschneidet sich selbst. Bitte die Skizze korrigieren.');
  const ring: Ring = orientRing(
    { pts: f.points.map((p) => ({ ...p })), tags: f.points.map((_, i) => `${f.id}:e${i}`) },
    true,
  );
  const face: Face = { id: f.id, feature: f.id, outer: ring, holes: [] };
  part.faces.set(face.id, face);
  let M: Affine = frameA(basis.ex, basis.ey, basis.n, v3(0, 0, 0));
  if (f.reverse) M = composeA(translationA(scale3(basis.n, -part.T)), M);
  part.anchor = { face: face.id, M };
}

function openProfile(part: Part, sheet: SheetSettings, basis: PlaneBasis, f: BaseFlangeFeature): void {
  const pts = f.points;
  if (pts.length < 2) throw new Error('Ein offenes Profil braucht mindestens zwei Punkte.');
  if (!(f.depth > 0)) throw new Error('Die Tiefe der Basislasche muss größer als 0 sein.');
  const { T, R, calc } = resolveBendParams(sheet, f.overrides);
  const n = pts.length - 1; // segments
  const dirs = [];
  const lens: number[] = [];
  for (let i = 0; i < n; i++) {
    const l = dist2(pts[i], pts[i + 1]);
    if (l < 1e-6) throw new Error(`Segment ${i + 1} der Skizze hat die Länge 0.`);
    lens.push(l);
    dirs.push(norm2(sub2(pts[i + 1], pts[i])));
  }
  const materialRight = !f.reverse;
  // bend at vertex j (1..n-1) between segment j-1 and j
  const bends: { angle: number; up: boolean; setback: number; ba: number }[] = [];
  for (let j = 1; j < n; j++) {
    const a = dirs[j - 1];
    const b = dirs[j];
    const turn = Math.atan2(a.x * b.y - a.y * b.x, a.x * b.x + a.y * b.y);
    const angle = Math.abs(turn);
    if (angle < 1e-4) throw new Error(`Die Segmente ${j} und ${j + 1} liegen auf einer Linie. Bitte den Punkt entfernen.`);
    if (angle > Math.PI - 1e-3) throw new Error(`Am Punkt ${j + 1} kehrt das Profil um (180°). Das ist mit einem Biegeradius nicht möglich.`);
    const up = materialRight ? turn < 0 : turn > 0;
    const setback = up ? outsideSetback(angle, R, T) : insideSetback(angle, R);
    bends.push({ angle, up, setback, ba: bendAllowance(angle, R, T, calc) });
  }

  const D = f.depth;
  let cursor = 0;
  const faceIds: string[] = [];
  for (let i = 0; i < n; i++) {
    const sbStart = i > 0 ? bends[i - 1].setback : 0;
    const sbEnd = i < n - 1 ? bends[i].setback : 0;
    const fl = lens[i] - sbStart - sbEnd;
    if (fl < 1e-3) {
      throw new Error(`Segment ${i + 1} ist zu kurz für Biegeradius und Blechdicke (es fehlen ${(-fl).toFixed(2)} mm).`);
    }
    const x0 = cursor;
    const x1 = cursor + fl;
    const id = i === 0 ? f.id : `${f.id}.f${i}`;
    const startTag = i === 0 ? `${f.id}:start` : seamTag(`${f.id}.b${i}`, 'B');
    const endTag = i === n - 1 ? `${f.id}:end` : seamTag(`${f.id}.b${i + 1}`, 'A');
    const ring: Ring = {
      pts: [v2(x0, 0), v2(x1, 0), v2(x1, D), v2(x0, D)],
      tags: [`${f.id}:s${i}a`, endTag, `${f.id}:s${i}b`, startTag],
    };
    part.faces.set(id, { id, feature: f.id, outer: ring, holes: [] });
    faceIds.push(id);
    if (i < n - 1) {
      const b = bends[i];
      const bend: Bend = {
        id: `${f.id}.b${i + 1}`,
        feature: f.id,
        kind: 'base',
        faceA: id,
        faceB: `${f.id}.f${i + 1}`,
        origin: v2(x1, 0),
        dir: v2(0, 1),
        nrm: v2(1, 0),
        w0: 0,
        w1: D,
        angle: b.angle,
        up: b.up,
        radius: R,
        ba: b.ba,
        calc: describeCalc(calc),
      };
      part.bends.set(bend.id, bend);
      cursor = x1 + b.ba;
    }
  }

  const d0 = add3(scale3(basis.ex, dirs[0].x), scale3(basis.ey, dirs[0].y));
  const depthAxis = materialRight ? basis.n : scale3(basis.n, -1);
  const ez = cross3(d0, depthAxis);
  const p0 = sketchTo3(basis, pts[0].x, pts[0].y);
  let origin = p0;
  if (materialRight) {
    if (f.midPlane) origin = add3(p0, scale3(basis.n, -D / 2));
  } else {
    origin = add3(p0, scale3(basis.n, f.midPlane ? D / 2 : D));
  }
  part.anchor = { face: faceIds[0], M: frameA(d0, depthAxis, ez, origin) };
}
