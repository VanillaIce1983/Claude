// Skizzierte Biegung (sketched bend) and Knick (jog): both cut a face along a
// line and insert bends there.

import { Bend, Face, Part, computePlacements, connectedFaces, findEdge, seamTag, stripFlat, stripRange } from '../model';
import { Ring, simplifyRing, splitRing } from '../polygon';
import { SheetSettings, bendAllowance, describeCalc, insideSetback, outsideSetback, resolveBendParams } from '../sheet';
import { V2, add2, dot2, norm2, rad, rotCCW, rotCW, scale2, sub2 } from '../vec';
import { rectRing } from './attach';
import type { BendPosition, FaceLine, JogFeature, SketchedBendFeature } from './types';

const TOL = 1e-6;

interface LineFrame {
  face: Face;
  /** Point on the bend line. */
  base: V2;
  /** Unit vector from the fixed side towards the moving side. */
  nrm: V2;
}

function resolveLine(part: Part, line: FaceLine, moveEdgeSide: boolean): LineFrame {
  const e = findEdge(part, line.edge);
  const d = norm2(sub2(e.b, e.a));
  const outward = rotCW(d);
  const inward = scale2(outward, -1);
  if (!(line.distance > 0)) throw new Error('Der Abstand der Biegelinie zur Bezugskante muss größer als 0 sein.');
  const base = add2(e.a, scale2(inward, line.distance));
  return { face: e.face, base, nrm: moveEdgeSide ? outward : inward };
}

/** Offset of the first tangent line from the sketch line for a bend position. */
export function bendLineOffset(position: BendPosition, angle: number, R: number, T: number, hc: number, ba: number): number {
  const cot = Math.cos(angle) / Math.sin(angle);
  switch (position) {
    case 'centerline':
      return -ba / 2;
    case 'materialInside':
      return -outsideSetback(angle, R, T) - hc * cot;
    case 'materialOutside':
      return -insideSetback(angle, R) + (T - hc) * cot;
    case 'bendOutside':
      return 0;
  }
}

interface SplitResult {
  fixed: Face;
  moving: Face;
  dir: V2;
  w0: number;
  w1: number;
}

/**
 * Cut `face` into fixed (σ ≤ c1) and moving (σ ≥ c2) parts, where
 * σ = dot(X − base, nrm). The band in between is removed and must be a plain
 * rectangle across the face. Existing bends and holes are reassigned.
 */
function splitFace(part: Part, lf: LineFrame, c1: number, c2: number, seamA: string, seamB: string, movingId: string): SplitResult {
  const { face, base, nrm } = lf;
  const dir = rotCCW(nrm); // so that nrm = rotCW(dir), like a CCW face edge
  const sigma = (p: V2): number => dot2(sub2(p, base), nrm);
  const first = splitRing(face.outer, base, nrm, c1, seamA);
  if (!first.neg || !first.pos) throw new Error('Die Biegelinie liegt außerhalb der Fläche. Bitte den Abstand ändern.');
  let movingRing: Ring | null;
  let band: Ring | null = null;
  if (c2 - c1 > TOL) {
    const second = splitRing(first.pos, base, nrm, c2, seamB);
    band = second.neg;
    movingRing = second.pos;
  } else {
    movingRing = { pts: first.pos.pts, tags: first.pos.tags.map((t) => (t === seamA ? seamB : t)) };
  }
  if (!movingRing) throw new Error('Die Fläche ist hinter der Biegelinie zu kurz für diese Biegung.');

  // extent of the bend along the line, from the fixed part's cut edge
  const lineOrigin = add2(base, scale2(nrm, c1));
  const wOf = (p: V2): number => dot2(sub2(p, lineOrigin), dir);
  const onC1 = first.neg.pts.filter((p) => Math.abs(sigma(p) - c1) < 1e-5).map(wOf);
  if (onC1.length < 2) throw new Error('Die Biegelinie schneidet die Fläche nicht vollständig.');
  const w0 = Math.min(...onC1);
  const w1 = Math.max(...onC1);
  if (band) {
    const simple = simplifyRing({ pts: band.pts, tags: band.pts.map(() => 'x') });
    const onC2 = band.pts.filter((p) => Math.abs(sigma(p) - c2) < 1e-5).map(wOf);
    if (
      simple.pts.length !== 4 ||
      onC2.length < 2 ||
      Math.abs(Math.min(...onC2) - w0) > 1e-4 ||
      Math.abs(Math.max(...onC2) - w1) > 1e-4
    ) {
      throw new Error('Im Biegebereich muss die Fläche rechteckig sein (Kanten senkrecht zur Biegelinie, keine Ecken).');
    }
  }
  const movingOn = movingRing.pts.filter((p) => Math.abs(sigma(p) - c2) < 1e-5).map(wOf);
  if (movingOn.length < 2 || Math.abs(Math.min(...movingOn) - w0) > 1e-4 || Math.abs(Math.max(...movingOn) - w1) > 1e-4) {
    throw new Error('Im Biegebereich muss die Fläche rechteckig sein (Kanten senkrecht zur Biegelinie, keine Ecken).');
  }

  const fixedHoles: Ring[] = [];
  const movingHoles: Ring[] = [];
  for (const h of face.holes) {
    const ss = h.pts.map(sigma);
    if (Math.max(...ss) <= c1 + TOL) fixedHoles.push(h);
    else if (Math.min(...ss) >= c2 - TOL) movingHoles.push(h);
    else throw new Error('Ein Ausschnitt liegt im Biegebereich. Bitte Ausschnitt oder Biegelinie verschieben.');
  }

  const fixed: Face = { id: face.id, feature: face.feature, outer: first.neg, holes: fixedHoles };
  const moving: Face = { id: movingId, feature: face.feature, outer: movingRing, holes: movingHoles };

  // reattach existing bends whose seam lies on the moving part
  for (const b of part.bends.values()) {
    for (const side of ['A', 'B'] as const) {
      const owner = side === 'A' ? b.faceA : b.faceB;
      if (owner !== face.id) continue;
      const fraction = side === 'A' ? 0 : 1;
      const [wl, wr] = stripRange(b, fraction);
      const p0 = stripFlat(b, wl, fraction);
      const p1 = stripFlat(b, wr, fraction);
      const s0 = sigma(p0);
      const s1 = sigma(p1);
      if (Math.max(s0, s1) <= c1 + 1e-5) continue;
      if (Math.min(s0, s1) >= c2 - 1e-5) {
        if (side === 'A') b.faceA = movingId;
        else b.faceB = movingId;
        continue;
      }
      throw new Error('Die Biegelinie schneidet eine vorhandene Biegung. Bitte den Abstand ändern.');
    }
  }
  part.faces.set(fixed.id, fixed);
  part.faces.set(moving.id, moving);
  return { fixed, moving, dir, w0, w1 };
}

function shiftFaces(part: Part, ids: Set<string>, delta: V2): void {
  const mv = (p: V2): V2 => add2(p, delta);
  for (const id of ids) {
    const f = part.faces.get(id)!;
    f.outer.pts = f.outer.pts.map(mv);
    for (const h of f.holes) {
      h.pts = h.pts.map(mv);
      for (const tag of new Set(h.tags)) {
        const c = part.circles.get(tag);
        if (c) c.c = mv(c.c);
      }
    }
  }
  for (const b of part.bends.values()) {
    if (ids.has(b.faceA) && (b.faceB === null || ids.has(b.faceB))) b.origin = mv(b.origin);
  }
}

function keepFixedFaceInPlace(part: Part, faceId: string): void {
  const placements = computePlacements(part);
  const M = placements.get(faceId);
  if (M) part.anchor = { face: faceId, M };
}

export function applySketchedBend(part: Part, sheet: SheetSettings, f: SketchedBendFeature): void {
  const { T, R, calc } = resolveBendParams(sheet, f.overrides);
  if (!(f.angle > 0 && f.angle < 180)) throw new Error('Der Biegewinkel muss größer als 0° und kleiner als 180° sein.');
  const th = rad(f.angle);
  const ba = bendAllowance(th, R, T, calc);
  const lf = resolveLine(part, f.line, f.moveEdgeSide);
  keepFixedFaceInPlace(part, lf.face.id);
  const up = !f.reverse;
  const hc = up ? T : 0;
  const c1 = bendLineOffset(f.position, th, R, T, hc, ba);
  const id = f.id;
  const movingId = `${id}:m`;
  const sp = splitFace(part, lf, c1, c1 + ba, seamTag(id, 'A'), seamTag(id, 'B'), movingId);
  const bend: Bend = {
    id,
    feature: id,
    kind: 'sketch',
    faceA: sp.fixed.id,
    faceB: sp.moving.id,
    origin: add2(lf.base, scale2(lf.nrm, c1)),
    dir: sp.dir,
    nrm: lf.nrm,
    w0: sp.w0,
    w1: sp.w1,
    angle: th,
    up,
    radius: R,
    ba,
    calc: describeCalc(calc),
  };
  part.bends.set(id, bend);
}

/** Elevation of the jogged face's reference surface for a given dimension type. */
export function jogElevation(offset: number, dimension: JogFeature['dimension'], T: number): number {
  switch (dimension) {
    case 'outside':
      return offset;
    case 'inside':
      return offset + T;
    case 'overall':
      return offset - T;
  }
}

export function applyJog(part: Part, sheet: SheetSettings, f: JogFeature): void {
  const { T, R, calc } = resolveBendParams(sheet, f.overrides);
  if (!(f.angle > 0 && f.angle < 180)) throw new Error('Der Knickwinkel muss größer als 0° und kleiner als 180° sein.');
  if (!(f.offset > 0)) throw new Error('Der Knickversatz muss größer als 0 sein.');
  const th = rad(f.angle);
  const ba = bendAllowance(th, R, T, calc);
  const e = jogElevation(f.offset, f.dimension, T);
  // elevation e = (2R + T)(1 − cos θ) + web·sin θ
  const web = (e - (2 * R + T) * (1 - Math.cos(th))) / Math.sin(th);
  if (web < 0.01) {
    const min = (2 * R + T) * (1 - Math.cos(th)) + 0.01 * Math.sin(th);
    throw new Error(`Der Versatz ist zu klein für zwei Biegungen. Mindestens ${(min - (e - f.offset)).toFixed(2)} mm sind nötig.`);
  }
  const lf = resolveLine(part, f.line, f.moveEdgeSide);
  keepFixedFaceInPlace(part, lf.face.id);
  const up = !f.reverse;
  const hc = up ? T : 0;
  const c1 = bendLineOffset(f.position, th, R, T, hc, ba);
  const jogFlat = 2 * ba + web;
  const id = f.id;
  const b1 = `${id}.1`;
  const b2 = `${id}.2`;
  const movingId = `${id}:m`;
  const webId = `${id}:web`;

  let c2 = c1 + jogFlat;
  let shift = 0;
  if (f.fixedProjectedLength) {
    // where the jogged face starts in projection; material is added so it keeps its length
    const x3 = c1 + (2 * R + T) * Math.sin(th) + web * Math.cos(th);
    if (x3 <= c1 + TOL) throw new Error('Mit diesem Winkel lässt sich die projizierte Länge nicht halten.');
    c2 = x3;
    shift = c1 + jogFlat - x3;
  }
  const sp = splitFace(part, lf, c1, c2, seamTag(b1, 'A'), seamTag(b2, 'B'), movingId);
  if (Math.abs(shift) > TOL) {
    const moved = connectedFaces(part, movingId, sp.fixed.id);
    shiftFaces(part, moved, scale2(lf.nrm, shift));
  }
  const origin1 = add2(lf.base, scale2(lf.nrm, c1));
  const origin2 = add2(lf.base, scale2(lf.nrm, c1 + ba + web));
  const frame = { origin: origin1, dir: sp.dir, nrm: lf.nrm, w0: sp.w0, w1: sp.w1 };
  const webRing = rectRing(frame, ba, ba + web, {
    seam: seamTag(b1, 'B'),
    side0: `${id}:web0`,
    end: seamTag(b2, 'A'),
    side1: `${id}:web1`,
  });
  part.faces.set(webId, { id: webId, feature: id, outer: webRing, holes: [] });
  const common = { feature: id, kind: 'jog' as const, dir: sp.dir, nrm: lf.nrm, w0: sp.w0, w1: sp.w1, angle: th, radius: R, ba, calc: describeCalc(calc) };
  part.bends.set(b1, { ...common, id: b1, faceA: sp.fixed.id, faceB: webId, origin: origin1, up });
  part.bends.set(b2, { ...common, id: b2, faceA: webId, faceB: sp.moving.id, origin: origin2, up: !up });
}
