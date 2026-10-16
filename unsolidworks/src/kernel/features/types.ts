import type { EdgeRef } from '../model';
import type { BendOverrides } from '../sheet';
import type { V2 } from '../vec';

export type SketchPlane = 'top' | 'front' | 'right';

export interface FeatureBase {
  id: string;
  name: string;
  suppressed?: boolean;
}

/** Basis-Blechlasche: closed profile → plate, open profile → bent profile. */
export interface BaseFlangeFeature extends FeatureBase {
  type: 'baseFlange';
  plane: SketchPlane;
  points: V2[];
  closed: boolean;
  /** Closed: thickness goes opposite to the plane normal. Open: material on the left of the line. */
  reverse: boolean;
  /** Open profile only. */
  depth: number;
  midPlane: boolean;
  overrides?: BendOverrides;
}

export type FlangePosition = 'materialInside' | 'materialOutside' | 'bendOutside' | 'virtualSharp' | 'tangentToBend';
export type LengthRef = 'outer' | 'inner' | 'tangent';
export type WidthMode = 'full' | 'offset' | 'centered';

export interface EdgeSelection extends EdgeRef {
  side: 'top' | 'bottom';
}

export interface WidthSpec {
  mode: WidthMode;
  /** Offset from the start of the edge (mode 'offset'). */
  offset1: number;
  /** Offset from the end of the edge (mode 'offset'). */
  offset2: number;
  /** Width (mode 'centered'). */
  width: number;
}

/** Kantenlasche. */
export interface EdgeFlangeFeature extends FeatureBase {
  type: 'edgeFlange';
  edges: EdgeSelection[];
  angle: number; // degrees
  length: number;
  lengthRef: LengthRef;
  position: FlangePosition;
  reverse: boolean;
  width: WidthSpec;
  overrides?: BendOverrides;
}

export type HemType = 'closed' | 'open' | 'teardrop' | 'rolled';

/** Saum. */
export interface HemFeature extends FeatureBase {
  type: 'hem';
  edges: EdgeSelection[];
  hemType: HemType;
  position: 'materialInside' | 'bendOutside';
  reverse: boolean;
  length: number; // closed / open
  gap: number; // open
  angle: number; // teardrop / rolled, degrees
  radius: number; // teardrop / rolled
  width: WidthSpec;
  overrides?: BendOverrides;
}

export type BendPosition = 'centerline' | 'materialInside' | 'materialOutside' | 'bendOutside';

/** A line on a face, parallel to one of its edges. */
export interface FaceLine {
  edge: EdgeRef;
  /** Distance from the reference edge into the face. */
  distance: number;
}

/** Skizzierte Biegung. */
export interface SketchedBendFeature extends FeatureBase {
  type: 'sketchedBend';
  line: FaceLine;
  angle: number; // degrees
  position: BendPosition;
  /** true: the side containing the reference edge moves; false: it stays fixed. */
  moveEdgeSide: boolean;
  reverse: boolean;
  overrides?: BendOverrides;
}

export type JogDimension = 'outside' | 'inside' | 'overall';

/** Knick. */
export interface JogFeature extends FeatureBase {
  type: 'jog';
  line: FaceLine;
  offset: number;
  dimension: JogDimension;
  angle: number; // degrees
  position: BendPosition;
  fixedProjectedLength: boolean;
  moveEdgeSide: boolean;
  reverse: boolean;
  overrides?: BendOverrides;
}

export type CutShape = 'circle' | 'rect' | 'slot';

/** Ausschnitt / Bohrung on one face, placed relative to an edge of that face. */
export interface CutFeature extends FeatureBase {
  type: 'cut';
  edge: EdgeRef;
  /** Distance along the reference edge from its start point. */
  u: number;
  /** Distance from the reference edge into the face. */
  v: number;
  shape: CutShape;
  diameter: number;
  width: number; // rect & slot: size along the edge
  height: number; // rect & slot: size across the edge
  angle: number; // rotation in degrees, relative to the edge
  /** Optional linear pattern along the edge. */
  count: number;
  spacing: number;
}

/** A corner of a face outline, identified by its two edges. */
export interface CornerRef {
  prevTag: string;
  nextTag: string;
  at: V2;
}

/** Ecke brechen: chamfer or round outline corners. */
export interface BreakCornerFeature extends FeatureBase {
  type: 'breakCorner';
  corners: CornerRef[];
  kind: 'chamfer' | 'fillet';
  distance: number;
}

/** Lasche: extend free edges in the plane of their face. */
export interface TabFeature extends FeatureBase {
  type: 'tab';
  edges: EdgeRef[];
  length: number;
  width: WidthSpec;
}

/** Geschlossene Ecke: extend a flange side up to the neighbouring flange. */
export interface ClosedCornerFeature extends FeatureBase {
  type: 'closedCorner';
  edges: EdgeRef[];
  gap: number;
}

export type Feature =
  | BaseFlangeFeature
  | EdgeFlangeFeature
  | HemFeature
  | SketchedBendFeature
  | JogFeature
  | CutFeature
  | TabFeature
  | ClosedCornerFeature
  | BreakCornerFeature;
export type FeatureType = Feature['type'];

export const FEATURE_LABELS: Record<FeatureType, string> = {
  baseFlange: 'Basislasche',
  edgeFlange: 'Kantenlasche',
  hem: 'Saum',
  sketchedBend: 'Skizzierte Biegung',
  jog: 'Knick',
  cut: 'Ausschnitt',
  tab: 'Lasche',
  closedCorner: 'Geschlossene Ecke',
  breakCorner: 'Ecke brechen',
};
