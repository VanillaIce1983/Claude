// The sheet metal part is stored "flat first": every planar face is a polygon
// in one shared flat-pattern coordinate system, and faces are joined by bend
// strips. The folded 3D shape is derived by rotating each face about its bend
// axes, so the flat pattern is exact by construction.

import { Ring, cloneRing, distPointSegment, edgeAt, signedArea } from './polygon';
import {
  Affine,
  IDENTITY,
  V2,
  V3,
  composeA,
  cross3,
  frame2,
  invertRigid,
  rotationAboutLine,
  translationA,
  v3,
} from './vec';

export interface Face {
  id: string;
  /** Feature that created this face (for highlighting in the tree). */
  feature: string;
  /** Outline, counter-clockwise in flat coordinates. */
  outer: Ring;
  /** Cut-outs, clockwise. */
  holes: Ring[];
}

export type BendKind = 'base' | 'flange' | 'hem' | 'sketch' | 'jog';

/** d(f) = a + b·sin(angle·f) + c·cos(angle·f) on [f0, f1]. */
export interface EndPiece {
  f0: number;
  f1: number;
  a: number;
  b: number;
  c: number;
}

/**
 * Extension of a strip end along the bend line, away from the strip (mm,
 * negative trims), as a function of f. Pieces cover [0, 1] in order and join
 * continuously. The cut through the thickness stays perpendicular to the
 * sheet, like the laser cut of the flat pattern.
 */
export interface StripEnd {
  pieces: EndPiece[];
}

/**
 * A cylindrical bend between face A (where the strip starts, t = 0) and
 * face B (t = ba). In flat coordinates the strip is the rectangle
 * origin + w·dir + t·nrm with w ∈ [w0, w1], t ∈ [0, ba]; ext0/ext1 mitre
 * its ends (closed corners), see stripRange.
 */
export interface Bend {
  id: string;
  feature: string;
  kind: BendKind;
  faceA: string;
  faceB: string | null;
  origin: V2;
  dir: V2;
  /** Unit vector pointing from face A across the strip. */
  nrm: V2;
  w0: number;
  w1: number;
  /** Bend angle (deflection) in radians, > 0. */
  angle: number;
  /** true: bends towards the +n side of face A (material side). */
  up: boolean;
  /** Inner radius. */
  radius: number;
  /** Bend allowance = flat width of the strip. */
  ba: number;
  /** Short description of the bend calculation used. */
  calc: string;
  /** Profile of the w0 end (grows towards -dir). */
  ext0?: StripEnd;
  /** Profile of the w1 end (grows towards +dir). */
  ext1?: StripEnd;
}

/** Exact circle metadata for holes, so DXF export can write true circles. */
export interface CircleInfo {
  c: V2;
  r: number;
}

export interface Part {
  T: number;
  faces: Map<string, Face>;
  bends: Map<string, Bend>;
  /** The face that stays put, with its flat→3D transform. */
  anchor: { face: string; M: Affine };
  circles: Map<string, CircleInfo>;
}

export function emptyPart(T: number): Part {
  return { T, faces: new Map(), bends: new Map(), anchor: { face: '', M: IDENTITY }, circles: new Map() };
}

export function clonePart(p: Part): Part {
  const faces = new Map<string, Face>();
  for (const [k, f] of p.faces) {
    faces.set(k, { ...f, outer: cloneRing(f.outer), holes: f.holes.map(cloneRing) });
  }
  const bends = new Map<string, Bend>();
  for (const [k, b] of p.bends) bends.set(k, cloneBend(b));
  const circles = new Map<string, CircleInfo>();
  for (const [k, c] of p.circles) circles.set(k, { c: { ...c.c }, r: c.r });
  return { T: p.T, faces, bends, anchor: { face: p.anchor.face, M: { r: [...p.anchor.M.r], t: { ...p.anchor.M.t } } }, circles };
}

const cloneEnd = (e: StripEnd): StripEnd => ({ pieces: e.pieces.map((q) => ({ ...q })) });

function cloneBend(b: Bend): Bend {
  const c: Bend = { ...b, origin: { ...b.origin }, dir: { ...b.dir }, nrm: { ...b.nrm } };
  if (b.ext0) c.ext0 = cloneEnd(b.ext0);
  if (b.ext1) c.ext1 = cloneEnd(b.ext1);
  return c;
}

export const seamTag = (bendId: string, side: 'A' | 'B'): string => `seam:${bendId}:${side}`;
export const isSeamTag = (tag: string): boolean => tag.startsWith('seam:');

/**
 * Transform that folds face B's flat coordinates into face A's flat frame
 * (z = thickness direction, material on 0 ≤ z ≤ T).
 */
export function foldTransform(b: Bend, T: number): Affine {
  const o3 = v3(b.nrm.x, b.nrm.y, 0);
  const shift = translationA(v3(-b.nrm.x * b.ba, -b.nrm.y * b.ba, 0));
  const axisPoint = v3(b.origin.x, b.origin.y, b.up ? T + b.radius : -b.radius);
  const axis = cross3(o3, v3(0, 0, 1));
  const rot = rotationAboutLine(axisPoint, axis, b.up ? b.angle : -b.angle);
  return composeA(rot, shift);
}

/**
 * Point inside the bend strip, in face A's flat frame.
 * @param w position along the bend line
 * @param f fraction across the strip (0 at face A, 1 at face B)
 * @param h height in the material (0 = reference side, T = other side)
 */
export function stripPoint(b: Bend, T: number, w: number, f: number, h: number): V3 {
  const phi = b.angle * f;
  let off: number;
  let z: number;
  if (b.up) {
    const rho = T + b.radius - h;
    off = rho * Math.sin(phi);
    z = T + b.radius - rho * Math.cos(phi);
  } else {
    const rho = b.radius + h;
    off = rho * Math.sin(phi);
    z = -b.radius + rho * Math.cos(phi);
  }
  const p = frame2(b.origin, b.dir, b.nrm, w, off);
  return v3(p.x, p.y, z);
}

/** Flat position of a strip point. */
export function stripFlat(b: Bend, w: number, f: number): V2 {
  return frame2(b.origin, b.dir, b.nrm, w, f * b.ba);
}

/** Extension of a strip end at fraction f (0 for a plain end). */
export function endOffset(end: StripEnd | undefined, angle: number, f: number): number {
  if (!end || end.pieces.length === 0) return 0;
  const q = end.pieces.find((p) => f <= p.f1) ?? end.pieces[end.pieces.length - 1];
  const phi = angle * f;
  return q.a + q.b * Math.sin(phi) + q.c * Math.cos(phi);
}

/** Extent [wl, wr] of the strip along the bend line at fraction f. */
export function stripRange(b: Bend, f: number): [number, number] {
  return [b.w0 - endOffset(b.ext0, b.angle, f), b.w1 + endOffset(b.ext1, b.angle, f)];
}

/** Sorted fractions in [0, 1] without near duplicates, always starting at 0 and ending at 1. */
export function uniqFractions(fs: number[], tol = 1e-9): number[] {
  const out = [0];
  for (const f of [...fs].sort((x, y) => x - y)) {
    if (f - out[out.length - 1] > tol && f < 1 - tol) out.push(f);
  }
  out.push(1);
  return out;
}

/** Fractions where either strip end changes its formula, including 0 and 1. */
export function stripFractions(b: Bend): number[] {
  const fs = [0, 1];
  for (const end of [b.ext0, b.ext1]) for (const p of end?.pieces ?? []) fs.push(p.f0, p.f1);
  return uniqFractions(fs);
}

const OUTLINE_STEP = (3 * Math.PI) / 180;

/** Sample fractions of one strip end: piece boundaries, curved pieces at most 3° apart. */
function endSamples(end: StripEnd | undefined, angle: number): number[] {
  const fs = [0, 1];
  for (const p of end?.pieces ?? []) {
    fs.push(p.f0, p.f1);
    if (p.b === 0 && p.c === 0) continue;
    const n = Math.ceil(((p.f1 - p.f0) * angle) / OUTLINE_STEP);
    for (let i = 1; i < n; i++) fs.push(p.f0 + ((p.f1 - p.f0) * i) / n);
  }
  return uniqFractions(fs);
}

/** Flat outline of a bend strip, counter-clockwise (a rectangle without end profiles). */
export function stripOutline(b: Bend): V2[] {
  const left = endSamples(b.ext0, b.angle).map((f) => stripFlat(b, stripRange(b, f)[0], f));
  const right = endSamples(b.ext1, b.angle).map((f) => stripFlat(b, stripRange(b, f)[1], f));
  const pts = [...left, ...right.reverse()];
  return signedArea(pts) < 0 ? pts.reverse() : pts;
}

/** Placement (flat → model space) of every face, propagated from the anchor. */
export function computePlacements(part: Part): Map<string, Affine> {
  const placed = new Map<string, Affine>();
  if (!part.faces.has(part.anchor.face)) return placed;
  placed.set(part.anchor.face, part.anchor.M);
  const byFace = new Map<string, Bend[]>();
  for (const b of part.bends.values()) {
    for (const f of [b.faceA, b.faceB]) {
      if (!f) continue;
      if (!byFace.has(f)) byFace.set(f, []);
      byFace.get(f)!.push(b);
    }
  }
  const queue = [part.anchor.face];
  while (queue.length) {
    const fid = queue.shift()!;
    const M = placed.get(fid)!;
    for (const b of byFace.get(fid) ?? []) {
      if (b.faceA === fid && b.faceB && !placed.has(b.faceB)) {
        placed.set(b.faceB, composeA(M, foldTransform(b, part.T)));
        queue.push(b.faceB);
      } else if (b.faceB === fid && !placed.has(b.faceA)) {
        placed.set(b.faceA, composeA(M, invertRigid(foldTransform(b, part.T))));
        queue.push(b.faceA);
      }
    }
  }
  return placed;
}

/** Faces reachable from `start` without passing through `blocked`. */
export function connectedFaces(part: Part, start: string, blocked: string): Set<string> {
  const seen = new Set<string>([start]);
  const queue = [start];
  while (queue.length) {
    const f = queue.shift()!;
    for (const b of part.bends.values()) {
      let other: string | null = null;
      if (b.faceA === f) other = b.faceB;
      else if (b.faceB === f) other = b.faceA;
      if (other && other !== blocked && !seen.has(other)) {
        seen.add(other);
        queue.push(other);
      }
    }
  }
  return seen;
}

/** Reference to an edge: its tag plus a flat point near it (disambiguates split edges). */
export interface EdgeRef {
  tag: string;
  at: V2;
}

export interface FoundEdge {
  face: Face;
  ring: Ring;
  /** -1 for the outer ring, otherwise the hole index. */
  ringIndex: number;
  index: number;
  a: V2;
  b: V2;
  tag: string;
}

export function allEdges(part: Part): FoundEdge[] {
  const out: FoundEdge[] = [];
  for (const face of part.faces.values()) {
    const rings: [Ring, number][] = [[face.outer, -1], ...face.holes.map((h, i) => [h, i] as [Ring, number])];
    for (const [ring, ringIndex] of rings) {
      for (let i = 0; i < ring.pts.length; i++) {
        const e = edgeAt(ring, i);
        out.push({ face, ring, ringIndex, index: i, a: e.a, b: e.b, tag: e.tag });
      }
    }
  }
  return out;
}

export function findEdge(part: Part, ref: EdgeRef): FoundEdge {
  let best: FoundEdge | null = null;
  let bestD = Infinity;
  for (const e of allEdges(part)) {
    if (e.tag !== ref.tag) continue;
    const d = distPointSegment(ref.at, e.a, e.b);
    if (d < bestD) {
      bestD = d;
      best = e;
    }
  }
  if (!best) throw new Error('Die gewählte Kante existiert nicht mehr. Bitte die Kante neu auswählen.');
  return best;
}

let idCounter = 0;
export function newId(prefix: string): string {
  idCounter += 1;
  return `${prefix}${Date.now().toString(36)}${idCounter.toString(36)}`;
}
