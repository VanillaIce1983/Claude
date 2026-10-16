// Kantenlasche (edge flange) and Saum (hem).

import { Bend, Part, findEdge, seamTag } from '../model';
import { SheetSettings, bendAllowance, describeCalc, insideSetback, outsideSetback, resolveBendParams } from '../sheet';
import { dist2, rad } from '../vec';
import { attachToEdge, rectRing, requireFreeOuterEdge, resolveWidth } from './attach';
import type { EdgeFlangeFeature, EdgeSelection, HemFeature } from './types';

/** Bend direction and selected-edge height in the canonical "bend up" frame. */
function bendSense(sel: EdgeSelection, reverse: boolean, T: number): { up: boolean; hc: number } {
  const up = (sel.side === 'top') !== reverse;
  const hSel = sel.side === 'top' ? T : 0;
  return { up, hc: up ? hSel : T - hSel };
}

export interface FlangeGeometry {
  /** Offset of the tangent line from the selected edge (negative = into the face). */
  s: number;
  /** Flat length of the flange face. */
  flat: number;
  ba: number;
}

/**
 * Edge flange position and flat length.
 *  - Material innen:   the flange's outer face lies in the plane through the selected edge
 *  - Material außen:   the flange's inner face lies in the plane through the selected edge
 *  - Biegung außen:    the bend starts at the selected edge
 *  - Virtuelle Spitze: the virtual sharp used for the length lies on the selected edge
 *  - Tangential zur Biegung (> 90°): the bend's outer surface touches the side face
 */
export function edgeFlangeGeometry(
  angleDeg: number,
  length: number,
  lengthRef: EdgeFlangeFeature['lengthRef'],
  position: EdgeFlangeFeature['position'],
  R: number,
  T: number,
  hc: number,
  ba: number,
): FlangeGeometry {
  const th = rad(angleDeg);
  if (!(angleDeg > 0 && angleDeg < 180)) throw new Error('Der Laschenwinkel muss größer als 0° und kleiner als 180° sein.');
  const ossb = outsideSetback(th, R, T);
  const issb = insideSetback(th, R);
  const cot = Math.cos(th) / Math.sin(th);
  let s: number;
  switch (position) {
    case 'materialInside':
      s = -ossb - hc * cot;
      break;
    case 'materialOutside':
      s = -issb + (T - hc) * cot;
      break;
    case 'bendOutside':
      s = 0;
      break;
    case 'virtualSharp':
      s = lengthRef === 'inner' ? -issb : -ossb;
      break;
    case 'tangentToBend':
      if (angleDeg < 90) throw new Error('„Tangential zur Biegung“ gibt es nur für Winkel ab 90°.');
      s = -(R + T);
      break;
  }
  let flat: number;
  switch (lengthRef) {
    case 'outer':
      flat = length - ossb;
      break;
    case 'inner':
      flat = length - issb;
      break;
    case 'tangent':
      if (angleDeg < 90) throw new Error('Die Länge „Tangentiale Biegung“ gibt es nur für Winkel ab 90°.');
      flat = length - (R + T);
      break;
  }
  if (flat < 1e-3) {
    throw new Error(`Die Laschenlänge ist zu kurz. Bei diesem Winkel und Radius sind mehr als ${(length - flat).toFixed(2)} mm nötig.`);
  }
  return { s, flat, ba };
}

export function applyEdgeFlange(part: Part, sheet: SheetSettings, f: EdgeFlangeFeature): void {
  if (f.edges.length === 0) throw new Error('Bitte mindestens eine Kante auswählen.');
  const { T, R, calc, relief } = resolveBendParams(sheet, f.overrides);
  f.edges.forEach((sel, i) => {
    const edge = findEdge(part, sel);
    requireFreeOuterEdge(edge);
    const { up, hc } = bendSense(sel, f.reverse, T);
    const th = rad(f.angle);
    const ba = bendAllowance(th, R, T, calc);
    const g = edgeFlangeGeometry(f.angle, f.length, f.lengthRef, f.position, R, T, hc, ba);
    const len = dist2(edge.a, edge.b);
    const [w0, w1] = resolveWidth(f.width, len);
    const id = f.edges.length > 1 ? `${f.id}.${i}` : f.id;
    const frame = attachToEdge(part, edge, w0, w1, g.s, relief, id, id);
    const ring = rectRing(frame, ba, ba + g.flat, {
      seam: seamTag(id, 'B'),
      side0: `${id}:side0`,
      end: `${id}:end`,
      side1: `${id}:side1`,
    });
    part.faces.set(id, { id, feature: f.id, outer: ring, holes: [] });
    const bend: Bend = {
      id,
      feature: f.id,
      kind: 'flange',
      faceA: edge.face.id,
      faceB: id,
      ...frame,
      angle: th,
      up,
      radius: R,
      ba,
      calc: describeCalc(calc),
    };
    part.bends.set(id, bend);
  });
}

export function applyHem(part: Part, sheet: SheetSettings, f: HemFeature): void {
  if (f.edges.length === 0) throw new Error('Bitte mindestens eine Kante auswählen.');
  const base = resolveBendParams(sheet, f.overrides);
  const T = base.T;
  f.edges.forEach((sel, i) => {
    const edge = findEdge(part, sel);
    requireFreeOuterEdge(edge);
    const { up } = bendSense(sel, f.reverse, T);
    let angleDeg: number;
    let R: number;
    switch (f.hemType) {
      case 'closed':
        angleDeg = 180;
        R = 0;
        break;
      case 'open':
        if (!(f.gap > 0)) throw new Error('Der Spaltabstand eines offenen Saums muss größer als 0 sein.');
        angleDeg = 180;
        R = f.gap / 2;
        break;
      case 'teardrop':
        if (!(f.angle > 180 && f.angle <= 330)) throw new Error('Der Winkel eines Tropfensaums muss zwischen 180° und 330° liegen.');
        if (!(f.radius > 0)) throw new Error('Der Radius muss größer als 0 sein.');
        angleDeg = f.angle;
        R = f.radius;
        break;
      case 'rolled':
        if (!(f.angle > 0 && f.angle < 360)) throw new Error('Der Winkel eines gerollten Saums muss zwischen 0° und 360° liegen.');
        if (!(f.radius > 0)) throw new Error('Der Radius muss größer als 0 sein.');
        angleDeg = f.angle;
        R = f.radius;
        break;
    }
    const th = rad(angleDeg);
    const ba = bendAllowance(th, R, T, base.calc);
    // how far the curl reaches beyond the tangent line
    const reach = angleDeg >= 90 ? R + T : (R + T) * Math.sin(th);
    const s = f.position === 'materialInside' ? -reach : 0;

    let flat = 0;
    if (f.hemType === 'closed' || f.hemType === 'open') {
      flat = f.length - (R + T);
      if (flat < 1e-3) throw new Error(`Die Saumlänge muss größer als ${(R + T).toFixed(2)} mm sein.`);
    } else if (f.hemType === 'teardrop') {
      // straight tail until the inner surface touches the face again
      flat = (R * (1 - Math.cos(th))) / -Math.sin(th);
    }

    const len = dist2(edge.a, edge.b);
    const [w0, w1] = resolveWidth(f.width, len);
    const id = f.edges.length > 1 ? `${f.id}.${i}` : f.id;
    const frame = attachToEdge(part, edge, w0, w1, s, base.relief, id, id);
    let faceB: string | null = null;
    if (flat > 1e-3) {
      faceB = id;
      const ring = rectRing(frame, ba, ba + flat, {
        seam: seamTag(id, 'B'),
        side0: `${id}:side0`,
        end: `${id}:end`,
        side1: `${id}:side1`,
      });
      part.faces.set(id, { id, feature: f.id, outer: ring, holes: [] });
    }
    part.bends.set(id, {
      id,
      feature: f.id,
      kind: 'hem',
      faceA: edge.face.id,
      faceB,
      ...frame,
      angle: th,
      up,
      radius: R,
      ba,
      calc: describeCalc(base.calc),
    });
  });
}
