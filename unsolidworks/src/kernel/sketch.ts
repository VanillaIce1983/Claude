// Sketches on a face. A face sketch is attached to a reference edge of the
// face: the origin is the edge's start point, x runs along the edge and y
// points into the face. Because the frame follows the edge, a sketch keeps
// its place when earlier features change the size of the part.

import { EdgeRef, Face, Part, findEdge } from './model';
import { GEO_TOL } from './polygon';
import { V2, add2, norm2, rotCCW, scale2, sub2, dot2, v2 } from './vec';

export type SketchEntity =
  /** Closed polygon through the points (in order). */
  | { kind: 'polyline'; id: string; points: V2[] }
  /** Rectangle around a centre, rotated by `angle` degrees. */
  | { kind: 'rect'; id: string; cx: number; cy: number; w: number; h: number; angle: number }
  | { kind: 'circle'; id: string; cx: number; cy: number; r: number }
  /** Straight slot: two arc centres and the slot width. */
  | { kind: 'slot'; id: string; x1: number; y1: number; x2: number; y2: number; width: number }
  /** Regular polygon: centre, circumradius, number of sides, rotation in degrees. */
  | { kind: 'polygon'; id: string; cx: number; cy: number; r: number; sides: number; angle: number };

export type SketchEntityKind = SketchEntity['kind'];

/**
 * Where the origin sits on a reference edge that earlier features split into
 * pieces: 'line' is the start of the whole edge line (sketches made since this
 * option exists); without it, the start of the piece `at` lies on, as in older
 * documents, so those keep their geometry.
 */
export type SketchAnchor = 'line';

export interface FaceSketch {
  edge: EdgeRef;
  entities: SketchEntity[];
  anchor?: SketchAnchor;
}

export interface SketchFrame {
  face: Face;
  /** Flat-pattern position of the sketch origin. */
  origin: V2;
  /** Unit x axis (along the reference edge), in flat coordinates. */
  x: V2;
  /** Unit y axis (into the face), in flat coordinates. */
  y: V2;
}

/** Pieces of one split edge may sit this far off its line (snapping during a cut). */
const LINE_TOL = 10 * GEO_TOL;

/**
 * Frame of a face sketch (also used by Bohrung). A notch from an earlier
 * feature splits the reference edge into pieces with the same tag, and `at`
 * finds whichever piece is nearest. With anchor 'line' the origin is the start
 * of the first piece along the edge line, not of the found piece, so moving or
 * suppressing that notch does not move the sketch. A notch that removes the
 * start of the edge still moves the origin to where the edge now begins.
 */
export function sketchFrame(part: Part, edge: EdgeRef, anchor?: SketchAnchor): SketchFrame {
  const e = findEdge(part, edge);
  const x = norm2(sub2(e.b, e.a));
  const y = rotCCW(x);
  if (anchor !== 'line') return { face: e.face, origin: e.a, x, y };
  let start = 0;
  for (const ring of [e.face.outer, ...e.face.holes]) {
    ring.pts.forEach((a, i) => {
      if (ring.tags[i] !== e.tag) return;
      const b = ring.pts[(i + 1) % ring.pts.length];
      const da = sub2(a, e.a);
      const db = sub2(b, e.a);
      const onLine = Math.abs(dot2(da, y)) < LINE_TOL && Math.abs(dot2(db, y)) < LINE_TOL;
      if (onLine && dot2(db, x) > dot2(da, x)) start = Math.min(start, dot2(da, x));
    });
  }
  return { face: e.face, origin: add2(e.a, scale2(x, start)), x, y };
}

export function sketchToFlat(f: SketchFrame, p: V2): V2 {
  return add2(f.origin, add2(scale2(f.x, p.x), scale2(f.y, p.y)));
}

export function flatToSketch(f: SketchFrame, p: V2): V2 {
  const d = sub2(p, f.origin);
  return v2(dot2(d, f.x), dot2(d, f.y));
}

export function circleSegments(r: number): number {
  return Math.max(24, Math.min(96, Math.round(r * 6)));
}

/** Validation message for an entity, or '' when it is usable. */
export function entityProblem(e: SketchEntity): string {
  switch (e.kind) {
    case 'polyline':
      return e.points.length < 3 ? 'Ein Linienzug braucht mindestens drei Punkte.' : '';
    case 'rect':
      return e.w > 0 && e.h > 0 ? '' : 'Breite und Höhe müssen größer als 0 sein.';
    case 'circle':
      return e.r > 0 ? '' : 'Der Durchmesser muss größer als 0 sein.';
    case 'slot':
      return e.width > 0 && Math.hypot(e.x2 - e.x1, e.y2 - e.y1) > 1e-6 ? '' : 'Ein Langloch braucht zwei verschiedene Mittelpunkte und eine Breite.';
    case 'polygon':
      return e.r > 0 && e.sides >= 3 && Number.isInteger(e.sides) ? '' : 'Ein Vieleck braucht mindestens drei Seiten und einen Radius.';
  }
}

/** Counter-clockwise outline of an entity in sketch coordinates. */
export function entityOutline(e: SketchEntity): V2[] {
  let pts: V2[];
  switch (e.kind) {
    case 'polyline':
      pts = e.points.map((p) => ({ ...p }));
      break;
    case 'rect': {
      const a = (e.angle * Math.PI) / 180;
      const c = Math.cos(a);
      const s = Math.sin(a);
      pts = [
        [-e.w / 2, -e.h / 2],
        [e.w / 2, -e.h / 2],
        [e.w / 2, e.h / 2],
        [-e.w / 2, e.h / 2],
      ].map(([x, y]) => v2(e.cx + x * c - y * s, e.cy + x * s + y * c));
      break;
    }
    case 'circle': {
      const n = circleSegments(e.r);
      pts = [];
      for (let i = 0; i < n; i++) {
        const a = (2 * Math.PI * i) / n;
        pts.push(v2(e.cx + e.r * Math.cos(a), e.cy + e.r * Math.sin(a)));
      }
      break;
    }
    case 'slot': {
      const r = e.width / 2;
      const d = norm2(v2(e.x2 - e.x1, e.y2 - e.y1));
      const base = Math.atan2(d.y, d.x);
      const seg = Math.max(8, Math.round(circleSegments(r) / 2));
      pts = [];
      for (let i = 0; i <= seg; i++) {
        const a = base - Math.PI / 2 + (Math.PI * i) / seg;
        pts.push(v2(e.x2 + r * Math.cos(a), e.y2 + r * Math.sin(a)));
      }
      for (let i = 0; i <= seg; i++) {
        const a = base + Math.PI / 2 + (Math.PI * i) / seg;
        pts.push(v2(e.x1 + r * Math.cos(a), e.y1 + r * Math.sin(a)));
      }
      break;
    }
    case 'polygon': {
      const a0 = (e.angle * Math.PI) / 180;
      pts = [];
      for (let i = 0; i < e.sides; i++) {
        const a = a0 + (2 * Math.PI * i) / e.sides;
        pts.push(v2(e.cx + e.r * Math.cos(a), e.cy + e.r * Math.sin(a)));
      }
      break;
    }
  }
  let area = 0;
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i];
    const q = pts[(i + 1) % pts.length];
    area += p.x * q.y - q.x * p.y;
  }
  return area < 0 ? pts.reverse() : pts;
}

let seq = 0;
export function newEntityId(): string {
  seq += 1;
  return `s${Date.now().toString(36).slice(-4)}${seq}`;
}
