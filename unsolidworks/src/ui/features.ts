// Defaults, names and one-line summaries for features.

import type { Doc } from '../kernel/doc';
import {
  BaseFlangeFeature,
  BreakCornerFeature,
  ClosedCornerFeature,
  SketchCutFeature,
  TabFeature,
  CutFeature,
  EdgeFlangeFeature,
  FEATURE_LABELS,
  Feature,
  FeatureType,
  HemFeature,
  JogFeature,
  SketchedBendFeature,
} from '../kernel/features/types';
import type { V2 } from '../kernel/vec';
import { num } from './format';

let counter = 0;
export function featureId(type: FeatureType): string {
  counter += 1;
  return `${type}${Date.now().toString(36).slice(-4)}${counter}`;
}

export function nextName(doc: Doc, type: FeatureType): string {
  const label = FEATURE_LABELS[type];
  let n = 1;
  const names = new Set(doc.features.map((f) => f.name));
  while (names.has(`${label}${n}`)) n++;
  return `${label}${n}`;
}

const fullWidth = { mode: 'full' as const, offset1: 10, offset2: 10, width: 50 };

export function newBaseFlange(doc: Doc, points: V2[], closed: boolean): BaseFlangeFeature {
  return {
    id: featureId('baseFlange'),
    name: nextName(doc, 'baseFlange'),
    type: 'baseFlange',
    plane: closed ? 'top' : 'front',
    points,
    closed,
    reverse: false,
    depth: 100,
    midPlane: false,
  };
}

export function newEdgeFlange(doc: Doc): EdgeFlangeFeature {
  return {
    id: featureId('edgeFlange'),
    name: nextName(doc, 'edgeFlange'),
    type: 'edgeFlange',
    edges: [],
    angle: 90,
    length: 25,
    lengthRef: 'outer',
    position: 'materialInside',
    reverse: false,
    width: { ...fullWidth },
  };
}

export function newHem(doc: Doc): HemFeature {
  return {
    id: featureId('hem'),
    name: nextName(doc, 'hem'),
    type: 'hem',
    edges: [],
    hemType: 'closed',
    position: 'materialInside',
    reverse: false,
    length: 10,
    gap: 1,
    angle: 270,
    radius: 2,
    width: { ...fullWidth },
  };
}

export function newSketchedBend(doc: Doc): SketchedBendFeature {
  return {
    id: featureId('sketchedBend'),
    name: nextName(doc, 'sketchedBend'),
    type: 'sketchedBend',
    line: { edge: { tag: '', at: { x: 0, y: 0 } }, distance: 20 },
    angle: 90,
    position: 'centerline',
    moveEdgeSide: true,
    reverse: false,
  };
}

export function newJog(doc: Doc): JogFeature {
  return {
    id: featureId('jog'),
    name: nextName(doc, 'jog'),
    type: 'jog',
    line: { edge: { tag: '', at: { x: 0, y: 0 } }, distance: 20 },
    offset: 10,
    dimension: 'outside',
    angle: 90,
    position: 'materialInside',
    fixedProjectedLength: false,
    moveEdgeSide: true,
    reverse: false,
  };
}

export function newCut(doc: Doc): CutFeature {
  return {
    id: featureId('cut'),
    name: nextName(doc, 'cut'),
    type: 'cut',
    edge: { tag: '', at: { x: 0, y: 0 } },
    u: 20,
    v: 20,
    shape: 'circle',
    diameter: 10,
    width: 30,
    height: 10,
    angle: 0,
    count: 1,
    spacing: 30,
  };
}

export function newSketchCut(doc: Doc): SketchCutFeature {
  return { id: featureId('sketchCut'), name: nextName(doc, 'sketchCut'), type: 'sketchCut', sketch: { edge: { tag: '', at: { x: 0, y: 0 } }, entities: [] } };
}

const ENTITY_LABELS: Record<string, [string, string]> = {
  rect: ['Rechteck', 'Rechtecke'],
  circle: ['Kreis', 'Kreise'],
  slot: ['Langloch', 'Langlöcher'],
  polygon: ['Vieleck', 'Vielecke'],
  polyline: ['Linienzug', 'Linienzüge'],
};

/** "2 Kreise, 1 Rechteck" */
export function sketchContents(entities: { kind: string }[]): string {
  if (!entities.length) return 'leere Skizze';
  const counts = new Map<string, number>();
  for (const e of entities) counts.set(e.kind, (counts.get(e.kind) ?? 0) + 1);
  return [...counts.entries()].map(([k, n]) => `${n} ${ENTITY_LABELS[k]?.[n === 1 ? 0 : 1] ?? k}`).join(', ');
}

export function newTab(doc: Doc): TabFeature {
  return { id: featureId('tab'), name: nextName(doc, 'tab'), type: 'tab', edges: [], length: 20, width: { ...fullWidth, mode: 'centered', width: 30 } };
}

export function newClosedCorner(doc: Doc): ClosedCornerFeature {
  return { id: featureId('closedCorner'), name: nextName(doc, 'closedCorner'), type: 'closedCorner', edges: [], gap: 0.1, kind: 'overlap' };
}

export function newBreakCorner(doc: Doc): BreakCornerFeature {
  return { id: featureId('breakCorner'), name: nextName(doc, 'breakCorner'), type: 'breakCorner', corners: [], kind: 'fillet', distance: 5 };
}

export const POSITION_LABELS: Record<string, string> = {
  materialInside: 'Material innen',
  materialOutside: 'Material außen',
  bendOutside: 'Biegung außen',
  virtualSharp: 'Biegung von virtueller Spitze',
  tangentToBend: 'Tangential zur Biegung',
  centerline: 'Biegemittellinie',
};

export const LENGTH_LABELS: Record<string, string> = {
  outer: 'Äußere virtuelle Spitze',
  inner: 'Innere virtuelle Spitze',
  tangent: 'Tangentiale Biegung',
};

export const HEM_LABELS: Record<string, string> = {
  closed: 'Geschlossen',
  open: 'Offen',
  teardrop: 'Tropfenform',
  rolled: 'Gerollt',
};

export const JOG_DIM_LABELS: Record<string, string> = {
  outside: 'Versatz außen',
  inside: 'Versatz innen',
  overall: 'Gesamtabmessung',
};

export function summary(f: Feature): string {
  switch (f.type) {
    case 'baseFlange':
      return f.closed ? `Platte · ${f.points.length} Ecken` : `Profil · ${f.points.length - 1} Segmente · Tiefe ${num(f.depth)} mm`;
    case 'edgeFlange':
      return `${num(f.angle)}° · ${num(f.length)} mm · ${POSITION_LABELS[f.position]}${f.edges.length > 1 ? ` · ${f.edges.length} Kanten` : ''}`;
    case 'hem':
      return `${HEM_LABELS[f.hemType]} · ${f.hemType === 'teardrop' || f.hemType === 'rolled' ? `${num(f.angle)}° R${num(f.radius)}` : `${num(f.length)} mm`}`;
    case 'sketchedBend':
      return `${num(f.angle)}° · Abstand ${num(f.line.distance)} mm`;
    case 'jog':
      return `Versatz ${num(f.offset)} mm · ${num(f.angle)}°`;
    case 'cut': {
      const shape = f.shape === 'circle' ? `Ø ${num(f.diameter)}` : f.shape === 'rect' ? `${num(f.width)} × ${num(f.height)}` : `Langloch ${num(f.width)} × ${num(f.height)}`;
      return f.count > 1 ? `${f.count} × ${shape}` : shape;
    }
    case 'sketchCut':
      return `Skizze · ${sketchContents(f.sketch.entities)}`;
    case 'tab':
      return `${num(f.length)} mm${f.edges.length > 1 ? ` · ${f.edges.length} Kanten` : ''}`;
    case 'closedCorner':
      return `${f.kind === 'underlap' ? 'Unterlappen' : 'Überlappen'} · Spalt ${num(f.gap)} mm${f.edges.length > 1 ? ` · ${f.edges.length} Kanten` : ''}`;
    case 'breakCorner':
      return `${f.kind === 'fillet' ? 'Verrundung R' : 'Fase '}${num(f.distance)} mm · ${f.corners.length} ${f.corners.length === 1 ? 'Ecke' : 'Ecken'}`;
  }
}
