// Sketch mode ("Skizze"): draw and dimension a face sketch right on the part
// in the 3D viewport, as in SolidWorks. The view turns normal to the face; the
// left button draws, the middle button rotates, the right one pans and the
// wheel zooms. Smart dimensions ("Intelligente Bemaßung") measure between
// sketch points and lines, or to corners and edges anywhere on the part
// (projected onto the sketch plane), and drive the sketch through the solver
// in kernel/sketchSolve.ts. Entities the dimensions fix completely are drawn
// black, the others blue. Coordinates are sketch coordinates: origin at the
// reference edge start, x along the edge, y into the face.

import './sketchMode.css';
import { Part } from '../kernel/model';
import { distPointSegment, isSimple, pointInPolygon, signedArea } from '../kernel/polygon';
import {
  DimOrient,
  DimTarget,
  FaceSketch,
  ModelLineRef,
  ModelPointRef,
  SketchDim,
  SketchEntity,
  SketchEntityKind,
  entityOutline,
  entityProblem,
  flatToSketch,
  newEntityId,
  sketchToFlat,
} from '../kernel/sketch';
import { SketchContext, SolveOptions, dimAnchors, dimEntities, dimValue, entityLines, entityPoints, overDefines, sketchContext, solveSketch } from '../kernel/sketchSolve';
import { Affine, V2, V3, add2, applyA, applyDirA, cross2, dist2, dot2, lerp2, norm2, rotCCW, scale2, sub2, v2, v3 } from '../kernel/vec';
import { h, svgIcon } from './dom';
import { inputValue, num, parseNumber } from './format';
import { ICONS } from './icons';
import { OverlayLines, SketchOverlay, Viewer } from './viewer';

type Tool = 'select' | 'rect' | 'crect' | 'circle' | 'slot' | 'polygon' | 'polyline' | 'dim';
type DrawTool = Exclude<Tool, 'select' | 'dim'>;
type PolylineEntity = Extract<SketchEntity, { kind: 'polyline' }>;
export type SketchSide = 'top' | 'bottom';

export interface FaceSketchResult {
  entities: SketchEntity[];
  dims: SketchDim[];
}

export interface SketchModeHost {
  viewer: Viewer;
  /** The part before the cut (SolidWorks rolls the model back while a sketch is edited). */
  part: Part;
  /** Name of the feature the sketched face belongs to. */
  faceName: string;
  css(name: string): string;
  toast(text: string, error?: boolean): void;
  setHint(text: string): void;
  /** Leave the sketch: its result, or null to discard the changes; `finish` also confirms the feature. */
  exit(result: FaceSketchResult | null, finish: boolean): void;
}

interface Snapped {
  p: V2;
  /** Sits on an existing point, so sizes taken from it are not rounded. */
  hit: boolean;
}

/** Something the smart dimension tool can measure. */
type Cand =
  | { kind: 'point'; target: DimTarget; sk: V2; world: V3 }
  | { kind: 'line'; target: DimTarget; a: V2; b: V2; wa: V3; wb: V3; ends?: [DimTarget, DimTarget] }
  | { kind: 'circle'; entity: string; c: V2; r: number };

interface ModelPoint {
  ref: ModelPointRef;
  world: V3;
  sk: V2;
  /** On the sketch surface itself (drawing snaps to these). */
  inPlane: boolean;
}

interface ModelLine {
  ref: ModelLineRef;
  wa: V3;
  wb: V3;
  a: V2;
  b: V2;
  inPlane: boolean;
}

type Sel = { kind: 'entity' | 'dim'; id: string };

type Press =
  | { kind: 'tap'; sx: number; sy: number; at: Snapped }
  | { kind: 'drag'; id: string; before: string; orig: SketchEntity[]; grab: V2; down: V2; sx: number; sy: number; moved: boolean }
  | { kind: 'click'; sx: number; sy: number };

interface NumOpts {
  unit?: string;
  /** Exclusive minimum. */
  gt?: number;
  min?: number;
  max?: number;
  integer?: boolean;
}

const SNAP_PX = 9;
const PICK_PX = 8;
const LINE_PX = 6;
const SLOT_WIDTH = 10;
/** Viewport presses this soon after the sketch opened belong to the click that opened it. */
const OPEN_GRACE_MS = 500;
const CLICKS: Record<Exclude<DrawTool, 'polyline'>, number> = { rect: 2, crect: 2, circle: 2, polygon: 2, slot: 3 };
/** Below this turn, a vertex between two edges of one tag is a tessellated arc point, not a corner. */
const SMOOTH_TURN = (20 * Math.PI) / 180;
const STEPS = [0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100];

const KIND_NAMES: Record<SketchEntityKind, string> = {
  rect: 'Rechteck',
  circle: 'Kreis',
  slot: 'Langloch',
  polygon: 'Vieleck',
  polyline: 'Linienzug',
};

const MM: NumOpts = { unit: 'mm' };
const LEN: NumOpts = { unit: 'mm', gt: 0 };
const DEG: NumOpts = { unit: '°' };

const icon = (body: string): string =>
  `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;
const dot = (x: number, y: number): string => `<circle cx="${x}" cy="${y}" r="1.2" fill="currentColor"/>`;

const TOOLS: { id: Tool; label: string; icon: string }[] = [
  { id: 'select', label: 'Auswahl', icon: ICONS.pointer },
  { id: 'dim', label: 'Bemaßung', icon: icon(`<path d="M4 20L20 4"/><path d="M4 20l1.2-4.6M4 20l4.6-1.2M20 4l-1.2 4.6M20 4l-4.6 1.2"/><path d="M4 9v-5h5" stroke-dasharray="2 2"/>`) },
  { id: 'rect', label: 'Rechteck', icon: ICONS.rect },
  { id: 'crect', label: 'Mittelpunkt-Rechteck', icon: icon(`<rect x="4" y="6" width="16" height="12"/><path d="M12 12l8 6" stroke-dasharray="2 2"/>${dot(12, 12)}`) },
  { id: 'circle', label: 'Kreis', icon: icon(`<circle cx="12" cy="12" r="8"/><path d="M12 12l5.7-5.7" stroke-dasharray="2 2"/>${dot(12, 12)}`) },
  { id: 'slot', label: 'Langloch', icon: icon(`<path d="M8 8h8a4 4 0 0 1 0 8H8a4 4 0 0 1 0-8z"/><path d="M8 12h8" stroke-dasharray="2 2"/>${dot(8, 12)}${dot(16, 12)}`) },
  { id: 'polygon', label: 'Vieleck', icon: icon(`<path d="M12 3.5l7.4 4.25v8.5L12 20.5l-7.4-4.25v-8.5z"/>${dot(12, 12)}`) },
  { id: 'polyline', label: 'Linienzug', icon: icon(`<path d="M4 17L7 6l7 4 6-3-1 12z"/>${dot(4, 17)}${dot(7, 6)}${dot(14, 10)}${dot(20, 7)}${dot(19, 19)}`) },
];

const POINT_NAMES: Record<string, string> = { c: 'Mitte', p1: 'Mitte 1', p2: 'Mitte 2' };

const deg = (rad: number): number => (rad * 180) / Math.PI;
/** Round a size to the step, but never down to zero. */
const roundTo = (v: number, g: number): number => (g > 0 ? Math.max(g, Math.round(v / g) * g) : v);
const round2 = (v: number): number => Math.round(v * 100) / 100;
const isEditable = (t: EventTarget | null): boolean =>
  t instanceof HTMLElement && (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.tagName === 'TEXTAREA' || t.isContentEditable);

function cloneEntity(e: SketchEntity): SketchEntity {
  return e.kind === 'polyline' ? { ...e, points: e.points.map((p) => ({ ...p })) } : { ...e };
}

function moveEntity(e: SketchEntity, d: V2): SketchEntity {
  switch (e.kind) {
    case 'polyline':
      return { ...e, points: e.points.map((p) => add2(p, d)) };
    case 'slot':
      return { ...e, x1: e.x1 + d.x, y1: e.y1 + d.y, x2: e.x2 + d.x, y2: e.y2 + d.y };
    default:
      return { ...e, cx: e.cx + d.x, cy: e.cy + d.y };
  }
}

/** Outline for drawing and picking; a degenerate slot has none instead of throwing. */
function outlineOf(e: SketchEntity): V2[] {
  try {
    return entityOutline(e);
  } catch {
    return [];
  }
}

/** Points a drag can grab: centres, corners, slot centres. */
function keyPoints(e: SketchEntity): V2[] {
  return entityPoints(e).map((q) => q.p);
}

function problemOf(e: SketchEntity): string {
  const p = entityProblem(e);
  if (p || e.kind !== 'polyline') return p;
  // the cut feature rejects crossing profiles; say so before it does
  const pts = e.points.filter((q, i) => dist2(q, e.points[(i + 1) % e.points.length]) >= 1e-6);
  return isSimple(pts) ? '' : 'Der Linienzug überschneidet sich selbst. Bitte die Punkte so setzen, dass sich die Linien nicht kreuzen.';
}

/** The two named end points of a straight edge, for its length. */
function lineEnds(e: SketchEntity, line: string): [string, string] | null {
  const i = Number(line.slice(1));
  switch (e.kind) {
    case 'rect':
      return [`p${i}`, `p${(i + 1) % 4}`];
    case 'polygon':
      return [`v${i}`, `v${(i + 1) % e.sides}`];
    case 'polyline':
      return [`v${i}`, `v${(i + 1) % e.points.length}`];
    default:
      return null;
  }
}

function previewText(e: SketchEntity): string {
  switch (e.kind) {
    case 'rect':
      return `${num(e.w)} × ${num(e.h)}`;
    case 'circle':
      return `Ø ${num(2 * e.r)}`;
    case 'polygon':
      return `R ${num(e.r)}  ${num(e.angle, 1)}°`;
    case 'slot':
      return `Breite ${num(e.width)}`;
    case 'polyline':
      return '';
  }
}

/** Text input for a number (German comma and arithmetic accepted) that writes through while typing. */
function numInput(get: () => number, set: (v: number) => void, opts: NumOpts, aria?: string): { box: HTMLElement; refresh: () => void } {
  const input = h('input', { type: 'text', inputmode: 'decimal', autocomplete: 'off', value: inputValue(get()), 'aria-label': aria });
  const box = h('div', { class: 'num' }, input, opts.unit ? h('span', { class: 'unit' }, opts.unit) : null);
  const valid = (v: number | null): v is number =>
    v !== null &&
    (!opts.integer || Number.isInteger(v)) &&
    (opts.gt === undefined || v > opts.gt) &&
    (opts.min === undefined || v >= opts.min) &&
    (opts.max === undefined || v <= opts.max);
  const refresh = (): void => {
    // never rewrite the field being typed in
    if (document.activeElement === input) return;
    input.value = inputValue(get());
    box.classList.remove('fs-bad');
  };
  input.addEventListener('input', () => {
    const v = parseNumber(input.value);
    box.classList.toggle('fs-bad', !valid(v));
    if (valid(v) && v !== get()) set(v);
  });
  // on blur or Enter: tidy the text, or fall back to the last valid value
  const tidy = (): void => {
    input.value = inputValue(get());
    box.classList.remove('fs-bad');
  };
  input.addEventListener('change', tidy);
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') tidy();
    if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
    e.preventDefault();
    const step = e.shiftKey ? 10 : e.altKey && !opts.integer ? 0.1 : 1;
    const next = Math.round(((parseNumber(input.value) ?? get()) + (e.key === 'ArrowUp' ? step : -step)) * 1e6) / 1e6;
    if (valid(next)) {
      set(next);
      tidy();
    }
  });
  return { box, refresh };
}

export class SketchMode {
  readonly panel: HTMLElement;
  private readonly ctx: SketchContext;
  /** Face placement: face-local flat (x, y) and height z → model space. */
  private readonly M: Affine;
  private readonly z: number;
  /** Lines sit this far off the surface, so the part does not hide them. */
  private readonly lift: number;
  private entities: SketchEntity[];
  private dims: SketchDim[];
  private defined = new Set<string>();
  private problem = '';
  private tool: Tool;
  private sides = 6;
  private clicks: V2[] = [];
  private cursor: Snapped | null = null;
  private screenAt: { x: number; y: number } | null = null;
  private shift = false;
  private sel: Sel | null = null;
  private hoverEntity: string | null = null;
  private hoverDim: string | null = null;
  private hoverCand: Cand | null = null;
  private dimFirst: Cand | null = null;
  private dimSecond: Cand | null = null;
  private press: Press | null = null;
  private lastAdd: { p: V2; t: number } | null = null;
  private lastTap: { x: number; y: number; t: number } | null = null;
  private editing: { id: string; input: HTMLInputElement } | null = null;
  private undoStack: string[] = [];
  private redoStack: string[] = [];
  private fieldBefore: string | null = null;
  private readonly modelPoints: ModelPoint[] = [];
  private readonly modelLines: ModelLine[] = [];
  private readonly labelEls = new Map<string, HTMLElement>();
  private readonly previewDimEl = h('div', { class: 'sk-dim preview' });
  private readonly tipEl = h('div', { class: 'sk-tip' });
  private readonly openedAt = performance.now();
  private closed = false;
  private readonly els: {
    tools: HTMLElement;
    hint: HTMLElement;
    status: HTMLElement;
    msg: HTMLElement;
    dims: HTMLElement;
    dimsHead: HTMLElement;
    list: HTMLElement;
    listHead: HTMLElement;
    undo: HTMLButtonElement;
    redo: HTMLButtonElement;
  };
  private refreshers: (() => void)[] = [];

  constructor(private readonly host: SketchModeHost, sketch: FaceSketch, private readonly side: SketchSide) {
    this.ctx = sketchContext(host.part, sketch);
    const M = this.ctx.placements.get(this.ctx.frame.face.id);
    if (!M) throw new Error('Die Skizzenfläche ist nicht mit dem Teil verbunden.');
    this.M = M;
    this.z = side === 'top' ? host.part.T : 0;
    this.lift = (side === 'top' ? 1 : -1) * Math.max(0.02, 0.01 * host.part.T);
    this.entities = sketch.entities.map(cloneEntity);
    this.dims = (sketch.dims ?? []).map((d) => JSON.parse(JSON.stringify(d)) as SketchDim);
    this.tool = this.entities.length ? 'select' : 'rect';
    this.solve();
    this.buildModelRefs();

    const tools = h('div', { class: 'fs-tools' });
    const hint = h('p', { class: 'note sk-hint' });
    const status = h('div', { class: 'sk-status' });
    const msg = h('div', { class: 'errbox', role: 'alert', hidden: true });
    const dims = h('div', { class: 'sk-dims' });
    const dimsHead = h('h3', {});
    const list = h('div', { class: 'fs-list' });
    const listHead = h('h3', {});
    const undo = h('button', { type: 'button', class: 'btn fs-small', title: 'Rückgängig (Strg+Z)', onclick: () => this.undo() }, svgIcon(ICONS.undo), 'Rückgängig') as HTMLButtonElement;
    const redo = h('button', { type: 'button', class: 'btn fs-small', title: 'Wiederholen (Strg+Y)', onclick: () => this.redo() }, svgIcon(ICONS.redo), 'Wiederholen') as HTMLButtonElement;
    this.els = { tools, hint, status, msg, dims, dimsHead, list, listHead, undo, redo };
    // one undo step per field edit, taken when the field is entered
    list.addEventListener('focusin', () => (this.fieldBefore = this.snapshot()));
    list.addEventListener('focusout', () => this.commitField());

    const okBtn = h('button', { type: 'button', class: 'pm-ok', title: 'Skizze übernehmen (Strg+Enter übernimmt auch das Feature)', 'aria-label': 'Skizze übernehmen', onclick: () => this.accept(false) }, svgIcon(ICONS.check));
    const cancelBtn = h('button', { type: 'button', class: 'pm-cancel', title: 'Änderungen an der Skizze verwerfen', 'aria-label': 'Skizze verwerfen', onclick: () => this.discard() }, svgIcon(ICONS.close));
    this.panel = h(
      'div',
      { class: 'pm sk-pm' },
      h('div', { class: 'pm-head' }, svgIcon(ICONS.sketch, 'ficon'), h('div', { class: 'pm-title' }, 'Skizze', h('small', {}, ` auf ${host.faceName}`)), okBtn, cancelBtn),
      h(
        'div',
        { class: 'pm-body' },
        msg,
        h('div', { class: 'section' }, h('h3', {}, 'Werkzeug'), tools, hint),
        h(
          'div',
          { class: 'section' },
          h('h3', {}, 'Status'),
          status,
          h(
            'div',
            { class: 'btnrow' },
            undo,
            redo,
            h('button', { type: 'button', class: 'btn fs-small', title: 'Ansicht senkrecht auf die Skizze', onclick: () => this.lookNormal() }, svgIcon(ICONS.fit), 'Normal auf'),
          ),
        ),
        h('div', { class: 'section' }, dimsHead, dims),
        h('div', { class: 'section' }, listHead, list),
        h(
          'p',
          { class: 'note' },
          'Linke Maustaste zeichnet, mittlere dreht die Ansicht, rechte verschiebt sie, das Mausrad zoomt. Bemaßungen lassen sich zu jeder Ecke und Kante des Teils setzen – auch auf anderen Flächen, projiziert auf die Skizzenebene.',
        ),
      ),
    );

    for (const el of [this.previewDimEl, this.tipEl]) el.style.pointerEvents = 'none';
    host.viewer.setSketchInput({
      down: (x, y, ev) => this.onDown(x, y, ev),
      move: (x, y, ev) => this.onMove(x, y, ev),
      up: (x, y, ev) => this.onUp(x, y, ev),
      leave: () => this.onLeave(),
      cancel: () => this.onCancel(),
    });
    host.viewer.onViewChange = () => this.render();
    window.addEventListener('keydown', this.onKey, true);
    window.addEventListener('keyup', this.onKeyUp, true);
    this.renderTools();
    this.renderPanel();
    this.lookNormal();
    this.render();
  }

  /** Remove everything the sketch added to the viewport and the window. */
  destroy(): void {
    if (this.closed) return;
    this.closed = true;
    window.removeEventListener('keydown', this.onKey, true);
    window.removeEventListener('keyup', this.onKeyUp, true);
    this.host.viewer.setSketchInput(null);
    this.host.viewer.onViewChange = () => {};
    this.host.viewer.setSketchOverlay(null);
    this.host.setHint('');
  }

  // ------------------------------------------------------------ geometry

  private toWorld(p: V2, z: number): V3 {
    const f = sketchToFlat(this.ctx.frame, p);
    return applyA(this.M, v3(f.x, f.y, z));
  }

  /** Where a sketch point is drawn. */
  private w(p: V2): V3 {
    return this.toWorld(p, this.z + this.lift);
  }

  private screen(p: V2): { x: number; y: number } {
    return this.host.viewer.worldToScreen(this.w(p));
  }

  private screenDist(a: V2, b: V2): number {
    const sa = this.screen(a);
    const sb = this.screen(b);
    return Math.hypot(sa.x - sb.x, sa.y - sb.y);
  }

  /** A model point projected onto the sketch plane, in sketch coordinates. */
  private project(p: V3): V2 {
    const l = applyA(this.ctx.toFace, p);
    return flatToSketch(this.ctx.frame, v2(l.x, l.y));
  }

  /** The sketch point under a client position, or null where the plane is seen edge-on. */
  private toSketch(x: number, y: number): V2 | null {
    const o = applyA(this.M, v3(0, 0, this.z));
    const n = applyDirA(this.M, v3(0, 0, 1));
    const q = this.host.viewer.screenToPlane(x, y, o, n);
    return q ? this.project(q) : null;
  }

  private px(): number {
    return this.host.viewer.pixelSize();
  }

  /** Turn the view straight onto the sketch ("Normal auf"). */
  lookNormal(): void {
    const n = applyDirA(this.M, v3(0, 0, this.side === 'top' ? 1 : -1));
    const fy = this.ctx.frame.y;
    const up = applyDirA(this.M, v3(fy.x, fy.y, 0));
    const pts = this.ctx.frame.face.outer.pts.map((p) => applyA(this.M, v3(p.x, p.y, this.z)));
    for (const e of this.entities) for (const p of outlineOf(e)) pts.push(this.w(p));
    this.host.viewer.lookNormal(n, up, pts);
  }

  /** Corners and straight edges of the part on both sheet surfaces, for dimensions and snapping. */
  private buildModelRefs(): void {
    const part = this.host.part;
    const T = part.T;
    const seenP = new Set<string>();
    const seenL = new Set<string>();
    const key = (p: V3): string => `${p.x.toFixed(4)}|${p.y.toFixed(4)}|${p.z.toFixed(4)}`;
    const inPlane = (p: V3): boolean => Math.abs(applyA(this.ctx.toFace, p).z - this.z) < 1e-3;
    const turn = (a: V2, p: V2, b: V2): number => {
      const u = sub2(p, a);
      const v = sub2(b, p);
      return Math.abs(Math.atan2(cross2(u, v), dot2(u, v)));
    };
    for (const face of part.faces.values()) {
      const M = this.ctx.placements.get(face.id);
      if (!M) continue;
      for (const ring of [face.outer, ...face.holes]) {
        const n = ring.pts.length;
        const pt = (i: number): V2 => ring.pts[((i % n) + n) % n];
        const tag = (i: number): string => ring.tags[((i % n) + n) % n];
        for (let i = 0; i < n; i++) {
          const prevTag = tag(i - 1);
          const nextTag = tag(i);
          // an arc is tessellated into edges of one tag: its inner points are no corners
          if (prevTag === nextTag && turn(pt(i - 1), pt(i), pt(i + 1)) < SMOOTH_TURN) continue;
          for (const side of ['top', 'bottom'] as const) {
            const world = applyA(M, v3(pt(i).x, pt(i).y, side === 'top' ? T : 0));
            const k = key(world);
            if (seenP.has(k)) continue;
            seenP.add(k);
            this.modelPoints.push({ ref: { prevTag, nextTag, at: { ...pt(i) }, side }, world, sk: this.project(world), inPlane: inPlane(world) });
          }
        }
        for (let i = 0; i < n; i++) {
          const t = tag(i);
          const a = pt(i);
          const b = pt(i + 1);
          if (part.circles.has(t) || dist2(a, b) < 1e-6) continue;
          // a piece of a tessellated arc is no straight edge
          const arcBefore = tag(i - 1) === t && turn(pt(i - 1), a, b) > 1e-3;
          const arcAfter = tag(i + 1) === t && turn(a, b, pt(i + 2)) > 1e-3;
          if (arcBefore || arcAfter) continue;
          for (const side of ['top', 'bottom'] as const) {
            const z = side === 'top' ? T : 0;
            const wa = applyA(M, v3(a.x, a.y, z));
            const wb = applyA(M, v3(b.x, b.y, z));
            const k = [key(wa), key(wb)].sort().join('/');
            if (seenL.has(k)) continue;
            seenL.add(k);
            this.modelLines.push({ ref: { tag: t, at: lerp2(a, b, 0.5), side }, wa, wb, a: this.project(wa), b: this.project(wb), inPlane: inPlane(wa) && inPlane(wb) });
          }
        }
      }
    }
  }

  // ------------------------------------------------------------ state

  private snapshot(): string {
    return JSON.stringify({ entities: this.entities, dims: this.dims });
  }

  private restore(s: string): void {
    const o = JSON.parse(s) as FaceSketchResult;
    this.entities = o.entities;
    this.dims = o.dims;
    if (this.sel && !(this.sel.kind === 'entity' ? this.entities : this.dims).some((x) => x.id === this.sel!.id)) this.sel = null;
  }

  private pushUndo(before: string): void {
    if (before === this.snapshot()) return;
    this.undoStack.push(before);
    if (this.undoStack.length > 200) this.undoStack.shift();
    this.redoStack = [];
  }

  private undo(): void {
    const s = this.undoStack.pop();
    if (s === undefined) return;
    this.closeEditor(false);
    this.redoStack.push(this.snapshot());
    this.restore(s);
    this.solve();
    this.renderPanel();
    this.render();
  }

  private redo(): void {
    const s = this.redoStack.pop();
    if (s === undefined) return;
    this.closeEditor(false);
    this.undoStack.push(this.snapshot());
    this.restore(s);
    this.solve();
    this.renderPanel();
    this.render();
  }

  private commitField(): void {
    if (this.fieldBefore === null) return;
    this.pushUndo(this.fieldBefore);
    this.fieldBefore = null;
    this.updateUndoButtons();
  }

  /** Solve the dimensions; on success the entities move. */
  private solve(opts: SolveOptions = {}): boolean {
    const res = solveSketch(this.entities, this.dims, this.ctx, opts);
    if (res.ok) this.entities = res.entities;
    this.problem = res.ok ? '' : (res.problem ?? 'Die Skizze lässt sich mit diesen Maßen nicht lösen.');
    this.defined = res.defined;
    return res.ok;
  }

  private entity(id: string): SketchEntity | undefined {
    return this.entities.find((e) => e.id === id);
  }

  private entMap(): Map<string, SketchEntity> {
    return new Map(this.entities.map((e) => [e.id, e]));
  }

  // ------------------------------------------------------------ snapping

  private snapTargets(exclude: string | null): V2[] {
    const out: V2[] = [...this.clicks];
    for (const e of this.entities) if (e.id !== exclude) out.push(...keyPoints(e));
    for (const m of this.modelPoints) if (m.inPlane) out.push(m.sk);
    for (const l of this.modelLines) if (l.inPlane) out.push(lerp2(l.a, l.b, 0.5));
    return out;
  }

  /** Snap a sketch point: to points nearby on screen, else round it to a step that suits the zoom. */
  private snap(raw: V2, from: V2 | null, exclude: string | null = null): Snapped {
    const step = STEPS.find((s) => s >= 1.5 * this.px()) ?? 100;
    if (this.shift && from) {
      const inc = Math.PI / 12;
      const a = Math.round(Math.atan2(raw.y - from.y, raw.x - from.x) / inc) * inc;
      const d = v2(Math.cos(a), Math.sin(a));
      return { p: add2(from, scale2(d, roundTo(Math.max(0, dot2(sub2(raw, from), d)), step))), hit: false };
    }
    const s = this.screen(raw);
    let best: V2 | null = null;
    let bd = SNAP_PX;
    for (const q of this.snapTargets(exclude)) {
      const sq = this.screen(q);
      const d = Math.hypot(sq.x - s.x, sq.y - s.y);
      if (d < bd) {
        bd = d;
        best = q;
      }
    }
    if (best) return { p: { ...best }, hit: true };
    return { p: v2(Math.round(raw.x / step) * step, Math.round(raw.y / step) * step), hit: false };
  }

  private shiftFrom(): V2 | null {
    if (this.tool === 'polyline' && this.clicks.length) return this.clicks[this.clicks.length - 1];
    if (this.tool === 'slot' && this.clicks.length === 1) return this.clicks[0];
    return null;
  }

  private snapAt(x: number, y: number): Snapped | null {
    const raw = this.toSketch(x, y);
    return raw ? this.snap(raw, this.shiftFrom()) : null;
  }

  // ------------------------------------------------------------ drawing tools

  /** Placed clicks, or the start of a press-drag stroke that has not been released yet. */
  private pending(): V2[] {
    if (this.clicks.length) return this.clicks;
    const p = this.press;
    const s = this.screenAt;
    return p?.kind === 'tap' && s && Math.hypot(s.x - p.sx, s.y - p.sy) > 6 ? [p.at.p] : [];
  }

  /** The entity the current tool would create with the next click at `c`. */
  private draft(c: Snapped): SketchEntity | null {
    const a = this.pending()[0];
    if (!a) return null;
    const p = c.p;
    const g = c.hit ? 0 : (STEPS.find((s) => s >= 1.5 * this.px()) ?? 100);
    switch (this.tool) {
      case 'rect':
        return { kind: 'rect', id: '', cx: (a.x + p.x) / 2, cy: (a.y + p.y) / 2, w: Math.abs(p.x - a.x), h: Math.abs(p.y - a.y), angle: 0 };
      case 'crect':
        return { kind: 'rect', id: '', cx: a.x, cy: a.y, w: 2 * Math.abs(p.x - a.x), h: 2 * Math.abs(p.y - a.y), angle: 0 };
      case 'circle':
        return { kind: 'circle', id: '', cx: a.x, cy: a.y, r: roundTo(2 * dist2(a, p), g) / 2 };
      case 'polygon': {
        const ang = deg(Math.atan2(p.y - a.y, p.x - a.x));
        return { kind: 'polygon', id: '', cx: a.x, cy: a.y, r: roundTo(dist2(a, p), g), sides: this.sides, angle: this.shift ? Math.round(ang / 15) * 15 : ang };
      }
      case 'slot': {
        const b = this.clicks[1];
        if (!b) return { kind: 'slot', id: '', x1: a.x, y1: a.y, x2: p.x, y2: p.y, width: SLOT_WIDTH };
        return { kind: 'slot', id: '', x1: a.x, y1: a.y, x2: b.x, y2: b.y, width: roundTo(2 * distPointSegment(p, a, b), g) };
      }
      default:
        return null;
    }
  }

  private add(e: SketchEntity, at: V2): void {
    const before = this.snapshot();
    const ent = { ...e, id: newEntityId() };
    this.entities.push(ent);
    this.lastAdd = { p: at, t: performance.now() };
    this.clicks = [];
    this.sel = { kind: 'entity', id: ent.id };
    this.solve();
    this.pushUndo(before);
    this.renderPanel();
    this.render();
  }

  private closePolyline(): void {
    if (this.tool === 'polyline' && this.clicks.length >= 3) this.add({ kind: 'polyline', id: '', points: this.clicks }, this.clicks[this.clicks.length - 1]);
  }

  private addDefaultSlot(): void {
    if (this.tool !== 'slot' || this.clicks.length !== 2) return;
    const [a, b] = this.clicks;
    this.add({ kind: 'slot', id: '', x1: a.x, y1: a.y, x2: b.x, y2: b.y, width: SLOT_WIDTH }, b);
  }

  private place(c: Snapped): void {
    const tool = this.tool;
    if (tool === 'select' || tool === 'dim') return;
    const last = this.clicks[this.clicks.length - 1];
    if (tool === 'polyline' && this.clicks.length >= 3 && this.screenDist(c.p, this.clicks[0]) < SNAP_PX) {
      this.closePolyline();
      return;
    }
    // a second click on the same spot adds nothing, also right after it finished a shape (double click)
    if (last && this.screenDist(c.p, last) < 3) return;
    if (!last && this.lastAdd && performance.now() - this.lastAdd.t < 500 && this.screenDist(c.p, this.lastAdd.p) < 3) return;
    if (tool === 'polyline' || this.clicks.length + 1 < CLICKS[tool]) {
      this.clicks.push(c.p);
      this.renderHint();
      return;
    }
    const e = this.draft(c);
    if (e && !problemOf(e)) this.add(e, c.p);
  }

  // ------------------------------------------------------------ picking

  /** Entity under a client point: outlines and key points win over interiors, then the smallest shape. */
  private pickEntity(x: number, y: number): SketchEntity | null {
    const m = this.toSketch(x, y);
    if (!m) return null;
    const px = this.px();
    let best: SketchEntity | null = null;
    let bestScore = Infinity;
    for (const e of this.entities) {
      const pts = outlineOf(e);
      let d = Infinity;
      for (let i = 0; i < pts.length; i++) d = Math.min(d, distPointSegment(m, pts[i], pts[(i + 1) % pts.length]));
      for (const k of keyPoints(e)) d = Math.min(d, dist2(m, k));
      const dpx = d / px;
      const score = dpx < PICK_PX ? dpx : pts.length >= 3 && pointInPolygon(m, pts) ? 100 + Math.abs(signedArea(pts)) : Infinity;
      if (score < bestScore) {
        bestScore = score;
        best = e;
      }
    }
    return best;
  }

  /** What the smart dimension tool would pick at a client point: points before lines, the sketch before the part. */
  private pickCand(x: number, y: number): Cand | null {
    const viewer = this.host.viewer;
    const dScreen = (p: V3): number => {
      const s = viewer.worldToScreen(p);
      return Math.hypot(s.x - x, s.y - y);
    };
    let best: Cand | null = null;
    let bd = PICK_PX;
    for (const e of this.entities) {
      for (const q of entityPoints(e)) {
        const world = this.w(q.p);
        const d = dScreen(world) - 0.5;
        if (d < bd) {
          bd = d;
          best = { kind: 'point', target: { kind: 'point', ref: { entity: e.id, point: q.name } }, sk: q.p, world };
        }
      }
    }
    const near = this.modelPoints
      .map((m) => ({ m, d: dScreen(m.world) }))
      .filter((c) => c.d < bd)
      .sort((a, b) => a.d - b.d);
    for (const c of near.slice(0, 12)) {
      if (viewer.isHidden(c.m.world)) continue;
      best = { kind: 'point', target: { kind: 'modelPoint', ref: c.m.ref }, sk: c.m.sk, world: c.m.world };
      break;
    }
    if (best) return best;
    const segDist = (wa: V3, wb: V3): { d: number; t: number } => {
      const a = viewer.worldToScreen(wa);
      const b = viewer.worldToScreen(wb);
      const ux = b.x - a.x;
      const uy = b.y - a.y;
      const l2 = ux * ux + uy * uy;
      const t = l2 > 0 ? Math.max(0, Math.min(1, ((x - a.x) * ux + (y - a.y) * uy) / l2)) : 0;
      return { d: Math.hypot(a.x + ux * t - x, a.y + uy * t - y), t };
    };
    bd = LINE_PX;
    const m = this.toSketch(x, y);
    for (const e of this.entities) {
      for (const l of entityLines(e)) {
        const wa = this.w(l.a);
        const wb = this.w(l.b);
        const d = segDist(wa, wb).d - 0.5;
        if (d >= bd) continue;
        bd = d;
        const ends = lineEnds(e, l.name);
        best = {
          kind: 'line',
          target: { kind: 'line', ref: { entity: e.id, line: l.name } },
          a: l.a,
          b: l.b,
          wa,
          wb,
          ends: ends ? [{ kind: 'point', ref: { entity: e.id, point: ends[0] } }, { kind: 'point', ref: { entity: e.id, point: ends[1] } }] : undefined,
        };
      }
      if (!m) continue;
      if (e.kind === 'circle') {
        const d = Math.abs(dist2(m, v2(e.cx, e.cy)) - e.r) / this.px() - 0.5;
        if (d < bd) {
          bd = d;
          best = { kind: 'circle', entity: e.id, c: v2(e.cx, e.cy), r: e.r };
        }
      } else if (e.kind === 'slot') {
        // the round ends measure the slot width
        const c1 = v2(e.x1, e.y1);
        const c2 = v2(e.x2, e.y2);
        const u = norm2(sub2(c2, c1));
        for (const [c, out] of [
          [c1, -1],
          [c2, 1],
        ] as const) {
          if (dot2(sub2(m, c), u) * out < 0) continue;
          const d = Math.abs(dist2(m, c) - e.width / 2) / this.px() - 0.5;
          if (d < bd) {
            bd = d;
            best = { kind: 'circle', entity: e.id, c: lerp2(c1, c2, 0.5), r: e.width / 2 };
          }
        }
      }
    }
    if (best) return best;
    let mb: { l: ModelLine; d: number; t: number } | null = null;
    for (const l of this.modelLines) {
      const s = segDist(l.wa, l.wb);
      if (s.d < bd && (!mb || s.d < mb.d)) mb = { l, ...s };
    }
    if (mb) {
      const { l, t } = mb;
      const p = v3(l.wa.x + (l.wb.x - l.wa.x) * t, l.wa.y + (l.wb.y - l.wa.y) * t, l.wa.z + (l.wb.z - l.wa.z) * t);
      if (!viewer.isHidden(p)) return { kind: 'line', target: { kind: 'modelLine', ref: l.ref }, a: l.a, b: l.b, wa: l.wa, wb: l.wb };
    }
    return null;
  }

  // ------------------------------------------------------------ dimensions

  private static sameCand(a: Cand, b: Cand): boolean {
    if (a.kind === 'circle' || b.kind === 'circle') return a.kind === 'circle' && b.kind === 'circle' && a.entity === b.entity;
    return JSON.stringify(a.target) === JSON.stringify(b.target);
  }

  /** A circle measures from its centre once a second item is picked. */
  private static asPoint(c: Cand): Cand {
    if (c.kind !== 'circle') return c;
    return { kind: 'point', target: { kind: 'point', ref: { entity: c.entity, point: 'c' } }, sk: c.c, world: v3(0, 0, 0) };
  }

  /** The dimension the picks would make with the label at `m`, or a reason why not. */
  private dimFrom(first: Cand, second: Cand | null, m: V2): SketchDim | string | null {
    const id = '';
    if (!second) {
      if (first.kind === 'circle') return { id, kind: 'diameter', entity: first.entity, value: 0, place: sub2(m, first.c) };
      if (first.kind === 'line' && first.ends) {
        const [ta, tb] = first.ends;
        return this.pointDim(ta, tb, first.a, first.b, m);
      }
      return null;
    }
    const a = SketchMode.asPoint(first);
    const b = SketchMode.asPoint(second);
    if (a.kind === 'circle' || b.kind === 'circle') return null;
    const sketchy = (t: DimTarget): boolean => t.kind === 'point' || t.kind === 'line';
    if (!sketchy(a.target) && !sketchy(b.target)) return 'Mindestens eines der beiden Elemente muss zur Skizze gehören.';
    if (a.kind === 'point' && b.kind === 'point') return this.pointDim(a.target, b.target, a.sk, b.sk, m);
    if (a.kind === 'line' && b.kind === 'line') {
      const da = norm2(sub2(a.b, a.a));
      const db = norm2(sub2(b.b, b.a));
      if (Math.abs(cross2(da, db)) > 1e-6) return 'Zwei Linien lassen sich nur bemaßen, wenn sie parallel sind.';
    }
    const d: SketchDim = { id, kind: 'distance', a: a.target, b: b.target, orient: 'aligned', value: 0 };
    return this.placed(d, m);
  }

  /** Point to point: the mouse decides between horizontal, vertical and aligned, as in SolidWorks. */
  private pointDim(ta: DimTarget, tb: DimTarget, a: V2, b: V2, m: V2): SketchDim {
    const inX = m.x >= Math.min(a.x, b.x) && m.x <= Math.max(a.x, b.x);
    const inY = m.y >= Math.min(a.y, b.y) && m.y <= Math.max(a.y, b.y);
    const orient: DimOrient = inX && !inY ? 'h' : inY && !inX ? 'v' : 'aligned';
    return this.placed({ id: '', kind: 'distance', a: ta, b: tb, orient, value: 0 }, m);
  }

  private placed(d: SketchDim, m: V2): SketchDim {
    try {
      const an = dimAnchors(d, this.entMap(), this.ctx);
      return { ...d, place: sub2(m, lerp2(an.a, an.b, 0.5)) };
    } catch {
      return d;
    }
  }

  private resetDimPick(): void {
    this.dimFirst = null;
    this.dimSecond = null;
  }

  /** Add a dimension at its current value and open the value editor. */
  private addDim(d: SketchDim): void {
    let value: number;
    try {
      value = round2(dimValue(d, this.entities, this.ctx));
      if (overDefines(this.entities, this.dims, d, this.ctx)) {
        this.host.toast('Diese Bemaßung würde die Skizze überbestimmen: Das Maß ist durch andere Bemaßungen schon festgelegt.', true);
        this.resetDimPick();
        return;
      }
    } catch (e) {
      this.host.toast((e as Error).message, true);
      this.resetDimPick();
      return;
    }
    if (!(value > 1e-6)) {
      this.host.toast('Der Abstand ist 0. Bitte die Bemaßung anders platzieren oder andere Elemente wählen.', true);
      return;
    }
    const before = this.snapshot();
    const dim = { ...d, id: newEntityId(), value } as SketchDim;
    this.dims.push(dim);
    if (!this.solve()) {
      this.host.toast(this.problem, true);
      this.dims.pop();
      this.solve();
      this.resetDimPick();
      return;
    }
    this.pushUndo(before);
    this.resetDimPick();
    this.sel = { kind: 'dim', id: dim.id };
    this.renderPanel();
    this.render();
    this.editDim(dim.id);
  }

  private setDimValue(id: string, v: number): void {
    const d = this.dims.find((x) => x.id === id);
    if (!d || d.value === v) return;
    const before = this.snapshot();
    const old = d.value;
    d.value = v;
    if (!this.solve()) {
      const why = this.problem;
      d.value = old;
      this.solve();
      this.host.toast(why, true);
    } else this.pushUndo(before);
    this.renderPanel();
    this.render();
  }

  /** Inline value editor on the dimension label (the "Ändern" box). */
  private editDim(id: string): void {
    this.closeEditor(true);
    const d = this.dims.find((x) => x.id === id);
    const el = this.labelEls.get(id);
    if (!d || !el) return;
    const input = h('input', { type: 'text', class: 'sk-dim-in', inputmode: 'decimal', autocomplete: 'off', value: inputValue(d.value), 'aria-label': 'Maß' }) as HTMLInputElement;
    el.replaceChildren(input);
    el.classList.add('edit');
    this.editing = { id, input };
    input.addEventListener('blur', () => this.closeEditor(true));
    input.focus();
    input.select();
  }

  private closeEditor(apply: boolean): void {
    const ed = this.editing;
    if (!ed) return;
    this.editing = null;
    this.labelEls.get(ed.id)?.classList.remove('edit');
    if (apply) {
      const v = parseNumber(ed.input.value);
      if (v !== null && v > 0) this.setDimValue(ed.id, v);
      else this.host.toast('Bitte ein Maß größer als 0 eingeben.', true);
    }
    this.render();
  }

  private deleteDim(id: string): void {
    const before = this.snapshot();
    this.dims = this.dims.filter((d) => d.id !== id);
    if (this.sel?.kind === 'dim' && this.sel.id === id) this.sel = null;
    this.solve();
    this.pushUndo(before);
    this.renderPanel();
    this.render();
  }

  private deleteEntity(id: string): void {
    const before = this.snapshot();
    this.entities = this.entities.filter((e) => e.id !== id);
    // dimensions go with what they measure, as in SolidWorks
    this.dims = this.dims.filter((d) => !dimEntities(d).includes(id));
    if (this.sel?.id === id) this.sel = null;
    this.solve();
    this.pushUndo(before);
    this.renderPanel();
    this.render();
  }

  /** After a change that renumbers the points of an entity, its point and line dimensions no longer fit. */
  private dropPointDims(id: string): void {
    const n = this.dims.length;
    this.dims = this.dims.filter(
      (d) => d.kind !== 'distance' || ![d.a, d.b].some((t) => (t.kind === 'point' && t.ref.entity === id && t.ref.point !== 'c') || (t.kind === 'line' && t.ref.entity === id)),
    );
    if (this.dims.length < n) this.host.toast('Bemaßungen an den Punkten dieses Elements wurden entfernt.');
  }

  // ------------------------------------------------------------ pointer input

  private onDown(x: number, y: number, ev: PointerEvent): void {
    if (performance.now() - this.openedAt < OPEN_GRACE_MS) return;
    this.closeEditor(true);
    this.shift = ev.shiftKey;
    this.screenAt = { x, y };
    if (this.tool === 'select') {
      const hit = this.pickEntity(x, y);
      this.sel = hit ? { kind: 'entity', id: hit.id } : null;
      this.markCards();
      if (hit) {
        const m = this.toSketch(x, y) ?? v2(0, 0);
        const keys = keyPoints(hit);
        const grab = keys.reduce((g, k) => (dist2(k, m) < dist2(g, m) ? k : g), keys[0] ?? m);
        this.press = { kind: 'drag', id: hit.id, before: this.snapshot(), orig: this.entities, grab: { ...grab }, down: m, sx: x, sy: y, moved: false };
      } else this.press = { kind: 'click', sx: x, sy: y };
      this.render();
      return;
    }
    if (this.tool === 'dim') {
      this.press = { kind: 'click', sx: x, sy: y };
      return;
    }
    const c = this.snapAt(x, y);
    if (!c) return;
    this.cursor = c;
    this.press = { kind: 'tap', sx: x, sy: y, at: c };
    this.render();
  }

  private onMove(x: number, y: number, ev: PointerEvent): void {
    this.shift = ev.shiftKey;
    this.screenAt = { x, y };
    const p = this.press;
    if (p?.kind === 'drag') {
      if (!p.moved && Math.hypot(x - p.sx, y - p.sy) < 3) return;
      p.moved = true;
      this.host.viewer.setSketchCursor('move');
      const now = this.toSketch(x, y);
      if (!now) return;
      // the grabbed point follows the pointer and is the point that snaps
      const to = this.snap(add2(p.grab, sub2(now, p.down)), null, p.id);
      this.cursor = to;
      const moved = p.orig.map((e) => (e.id === p.id ? moveEntity(e, sub2(to.p, p.grab)) : e));
      // the dragged shape follows the mouse as far as its dimensions let it; others give way first
      const res = solveSketch(moved, this.dims, this.ctx, { sidesFrom: p.orig, sticky: { entity: p.id } });
      this.entities = res.ok ? res.entities : p.orig;
      this.refreshFields();
      this.render();
      return;
    }
    if (this.tool === 'select') {
      const hit = this.pickEntity(x, y);
      this.hoverEntity = hit?.id ?? null;
      this.host.viewer.setSketchCursor(hit ? 'move' : 'default');
      this.cursor = null;
    } else if (this.tool === 'dim') {
      this.hoverCand = this.pickCand(x, y);
      this.host.viewer.setSketchCursor(this.hoverCand ? 'pointer' : 'crosshair');
      this.cursor = null;
    } else {
      this.cursor = this.snapAt(x, y);
      this.host.viewer.setSketchCursor('crosshair');
    }
    this.render();
  }

  private onUp(x: number, y: number, ev: PointerEvent): void {
    const p = this.press;
    this.press = null;
    this.shift = ev.shiftKey;
    if (!p) return;
    const now = performance.now();
    const double = this.lastTap !== null && now - this.lastTap.t < 450 && Math.hypot(x - this.lastTap.x, y - this.lastTap.y) < 8;
    this.lastTap = double ? null : { x, y, t: now };
    if (p.kind === 'drag') {
      if (p.moved) {
        // recompute which entities are fully defined
        this.solve();
        this.pushUndo(p.before);
        this.renderPanel();
      } else if (double) this.focusCard(p.id);
      this.host.viewer.setSketchCursor('move');
      this.render();
      return;
    }
    if (this.tool === 'dim') {
      if (Math.hypot(x - p.sx, y - p.sy) > 5) return;
      this.dimClick(x, y);
      return;
    }
    if (p.kind !== 'tap') return;
    if (double && this.tool === 'polyline' && this.clicks.length >= 3) {
      this.closePolyline();
      return;
    }
    // press, drag and release draws a whole shape in one stroke (handy on touch screens)
    if (!this.clicks.length && Math.hypot(x - p.sx, y - p.sy) > 6) this.place(p.at);
    const c = this.snapAt(x, y);
    if (c) {
      this.cursor = c;
      this.place(c);
    }
    this.render();
  }

  private dimClick(x: number, y: number): void {
    const hit = this.pickCand(x, y);
    const m = this.toSketch(x, y);
    if (!this.dimFirst) {
      if (hit) this.dimFirst = hit;
      this.render();
      return;
    }
    if (!this.dimSecond && hit && !SketchMode.sameCand(hit, this.dimFirst)) {
      const d = this.dimFrom(this.dimFirst, hit, m ?? v2(0, 0));
      if (typeof d === 'string') {
        this.host.toast(d, true);
        this.render();
        return;
      }
      this.dimSecond = hit;
      this.render();
      return;
    }
    if (!m) return;
    const d = this.dimFrom(this.dimFirst, this.dimSecond, m);
    if (typeof d === 'string') {
      this.host.toast(d, true);
      this.resetDimPick();
    } else if (d) this.addDim(d);
    else if (hit && SketchMode.sameCand(hit, this.dimFirst)) this.resetDimPick();
    this.render();
  }

  private onLeave(): void {
    if (this.press) return;
    this.cursor = null;
    this.screenAt = null;
    this.hoverEntity = null;
    this.hoverCand = null;
    this.render();
  }

  private onCancel(): void {
    const p = this.press;
    if (p?.kind === 'drag' && p.moved) {
      this.entities = p.orig;
      this.solve();
      this.renderPanel();
    }
    this.press = null;
    this.render();
  }

  // ------------------------------------------------------------ keyboard

  private readonly onKey = (e: KeyboardEvent): void => {
    if (this.closed) return;
    const ed = this.editing;
    if (ed && e.target === ed.input) {
      if (e.key === 'Enter' || e.key === 'Escape' || e.key === 'Tab') {
        e.preventDefault();
        e.stopPropagation();
        this.closeEditor(e.key !== 'Escape');
      }
      return;
    }
    if (e.key === 'Shift') {
      this.shift = true;
      this.reSnap();
      return;
    }
    const editable = isEditable(e.target);
    const mod = e.ctrlKey || e.metaKey;
    if (e.key === 'Escape') {
      // the app would cancel the whole feature: the sketch handles Escape itself
      e.preventDefault();
      e.stopPropagation();
      if (e.repeat) return;
      if (editable) (e.target as HTMLElement).blur();
      else this.escape();
      return;
    }
    if (mod && e.key === 'Enter') {
      e.preventDefault();
      e.stopPropagation();
      (document.activeElement as HTMLElement | null)?.blur?.();
      this.accept(true);
      return;
    }
    if (editable) {
      // text undo and the like stay with the field; the app must not act on them
      if (mod) e.stopPropagation();
      return;
    }
    if (mod && (e.key.toLowerCase() === 'z' || e.key.toLowerCase() === 'y')) {
      e.preventDefault();
      e.stopPropagation();
      if (e.key.toLowerCase() === 'y' || e.shiftKey) this.redo();
      else this.undo();
      return;
    }
    if (e.key === 'Backspace' && this.tool === 'polyline' && this.clicks.length) {
      e.preventDefault();
      this.clicks.pop();
      this.renderHint();
      this.render();
    } else if ((e.key === 'Delete' || (e.key === 'Backspace' && this.tool === 'select')) && this.sel && !e.repeat) {
      e.preventDefault();
      if (this.sel.kind === 'entity') this.deleteEntity(this.sel.id);
      else this.deleteDim(this.sel.id);
    } else if (e.key === 'Enter' && !(e.target instanceof HTMLButtonElement)) {
      if (this.tool === 'slot' && this.clicks.length === 2) {
        e.preventDefault();
        this.addDefaultSlot();
      } else if (this.tool === 'polyline' && this.clicks.length >= 3) {
        e.preventDefault();
        this.closePolyline();
      }
    }
  };

  private readonly onKeyUp = (e: KeyboardEvent): void => {
    if (e.key !== 'Shift') return;
    this.shift = false;
    this.reSnap();
  };

  private reSnap(): void {
    const s = this.screenAt;
    if (!s || this.press || this.tool === 'select' || this.tool === 'dim') return;
    this.cursor = this.snapAt(s.x, s.y);
    this.render();
  }

  /** Esc: drop what is in progress, then end the tool, then the selection – but never the sketch. */
  private escape(): void {
    const p = this.press;
    if (p?.kind === 'drag' && p.moved) {
      this.entities = p.orig;
      this.solve();
      this.press = null;
      this.renderPanel();
    } else if (this.clicks.length || this.dimFirst || p) {
      this.clicks = [];
      this.press = null;
      this.resetDimPick();
    } else if (this.tool !== 'select') this.setTool('select');
    else if (this.sel) {
      this.sel = null;
      this.markCards();
    }
    this.renderHint();
    this.render();
  }

  // ------------------------------------------------------------ leaving

  private validate(): string {
    if (!this.entities.length) return 'Die Skizze ist leer. Bitte mindestens ein Profil zeichnen.';
    for (let i = 0; i < this.entities.length; i++) {
      const p = problemOf(this.entities[i]);
      if (p) return `${KIND_NAMES[this.entities[i].kind]} ${i + 1}: ${p}`;
    }
    return this.problem;
  }

  private accept(finish: boolean): void {
    this.closeEditor(true);
    // finish what can be finished instead of silently dropping it
    if (this.tool === 'polyline' && this.clicks.length >= 3) this.closePolyline();
    else if (this.tool === 'slot' && this.clicks.length === 2) this.addDefaultSlot();
    this.clicks = [];
    this.press = null;
    const err = this.validate();
    if (err) {
      this.els.msg.textContent = err;
      this.els.msg.hidden = false;
      const bad = this.entities.find((e) => problemOf(e));
      if (bad) {
        this.sel = { kind: 'entity', id: bad.id };
        this.markCards();
      }
      this.host.toast(err, true);
      this.render();
      return;
    }
    const result: FaceSketchResult = { entities: this.entities.map(cloneEntity), dims: this.dims.map((d) => JSON.parse(JSON.stringify(d)) as SketchDim) };
    this.destroy();
    this.host.exit(result, finish);
  }

  private discard(): void {
    this.destroy();
    this.host.exit(null, false);
  }

  // ------------------------------------------------------------ panel

  private setTool(t: Tool): void {
    this.closeEditor(true);
    this.tool = t;
    this.clicks = [];
    this.press = null;
    this.resetDimPick();
    this.hoverCand = null;
    this.hoverEntity = null;
    const s = this.screenAt;
    this.cursor = s && t !== 'select' && t !== 'dim' ? this.snapAt(s.x, s.y) : null;
    this.host.viewer.setSketchCursor(t === 'select' ? 'default' : 'crosshair');
    this.renderTools();
    this.render();
  }

  private renderTools(): void {
    const btns = TOOLS.map((t) =>
      h(
        'button',
        {
          type: 'button',
          class: `tb small${this.tool === t.id ? ' on' : ''}`,
          title: t.id === 'dim' ? 'Intelligente Bemaßung' : t.label,
          'aria-pressed': this.tool === t.id ? 'true' : 'false',
          onclick: () => this.setTool(t.id),
        },
        svgIcon(t.icon),
        t.label,
      ),
    );
    const extra: HTMLElement[] = [];
    if (this.tool === 'polygon') {
      extra.push(
        h(
          'label',
          { class: 'field' },
          h('span', {}, 'Seiten des Vielecks'),
          numInput(() => this.sides, (v) => ((this.sides = v), this.render()), { integer: true, min: 3, max: 200 }).box,
        ),
      );
    }
    this.els.tools.replaceChildren(...btns, ...extra);
    this.renderHint();
  }

  private hintText(): string {
    const n = this.clicks.length;
    const esc = n ? ' Esc bricht ab.' : '';
    switch (this.tool) {
      case 'select':
        return 'Element anklicken wählt es aus, Ziehen verschiebt es, soweit die Maße es zulassen. Entf löscht, Doppelklick auf ein Maß ändert es.';
      case 'dim':
        if (!this.dimFirst) return 'Punkt, Linie oder Kreis der Skizze anklicken – oder eine Ecke bzw. Kante des Teils.';
        if (!this.dimSecond && this.dimFirst.kind === 'point') return 'Zweites Element anklicken: Skizzenpunkt, Linie oder eine Ecke bzw. Kante des Teils. Esc bricht ab.';
        return 'Maß platzieren: Die Mausposition wählt waagerecht, senkrecht oder ausgerichtet. Esc bricht ab.';
      case 'rect':
        return n ? `Gegenüberliegende Ecke anklicken.${esc}` : 'Erste Ecke anklicken.';
      case 'crect':
        return n ? `Eine Ecke anklicken.${esc}` : 'Mittelpunkt anklicken.';
      case 'circle':
        return n ? `Einen Punkt auf dem Kreis anklicken.${esc}` : 'Mittelpunkt anklicken.';
      case 'polygon':
        return n ? `Eine Ecke anklicken. Umschalt rastet die Drehung in 15°-Schritten.${esc}` : 'Mittelpunkt anklicken.';
      case 'slot':
        if (n === 0) return 'Ersten Bogenmittelpunkt anklicken.';
        if (n === 1) return `Zweiten Bogenmittelpunkt anklicken. Umschalt rastet den Winkel in 15°-Schritten.${esc}`;
        return `Breite per Klick festlegen oder Enter für ${SLOT_WIDTH} mm.${esc}`;
      case 'polyline':
        return n < 3
          ? `Punkte anklicken. Umschalt rastet den Winkel in 15°-Schritten.${esc}`
          : `Weitere Punkte anklicken. Klick auf den ersten Punkt, Doppelklick oder Enter schließt den Linienzug.${esc}`;
    }
  }

  private renderHint(): void {
    const text = this.hintText();
    const btn = (label: string, onclick: () => void): HTMLElement => h('button', { type: 'button', class: 'btn fs-small', onclick }, label);
    const acts: HTMLElement[] = [];
    if (this.tool === 'slot' && this.clicks.length === 2) acts.push(btn(`Breite ${SLOT_WIDTH} mm`, () => this.addDefaultSlot()));
    if (this.tool === 'polyline' && this.clicks.length >= 3) acts.push(btn('Linienzug schließen', () => this.closePolyline()));
    this.els.hint.replaceChildren(text, ...(acts.length ? [h('span', { class: 'btnrow' }, ...acts)] : []));
    this.host.setHint(text);
  }

  private updateUndoButtons(): void {
    this.els.undo.disabled = !this.undoStack.length;
    this.els.redo.disabled = !this.redoStack.length;
  }

  private renderStatus(): void {
    const n = this.entities.length;
    const free = this.entities.filter((e) => !this.defined.has(e.id)).length;
    let cls = 'under';
    let text: string;
    if (this.problem) {
      cls = 'bad';
      text = this.problem;
    } else if (!n) text = 'Noch leer';
    else if (!free) {
      cls = 'full';
      text = 'Voll bestimmt';
    } else text = this.dims.length ? `Unterbestimmt – ${free} von ${n} ${n === 1 ? 'Element' : 'Elementen'} noch frei` : 'Unterbestimmt – noch keine Bemaßung';
    this.els.status.className = `sk-status ${cls}`;
    this.els.status.textContent = text;
    this.els.msg.hidden = true;
    this.updateUndoButtons();
  }

  private targetName(t: DimTarget): string {
    if (t.kind === 'modelPoint') return 'Teilecke';
    if (t.kind === 'modelLine') return 'Teilkante';
    const i = this.entities.findIndex((e) => e.id === t.ref.entity);
    const e = this.entities[i];
    const base = e ? `${KIND_NAMES[e.kind]} ${i + 1}` : 'Gelöscht';
    if (t.kind === 'line') return `${base} Seite ${Number(t.ref.line.slice(1)) + 1}`;
    const p = t.ref.point;
    return `${base} ${POINT_NAMES[p] ?? `${e?.kind === 'rect' ? 'Ecke' : 'Punkt'} ${Number(p.slice(1)) + 1}`}`;
  }

  private dimText(d: SketchDim): string {
    if (d.kind === 'diameter') {
      const i = this.entities.findIndex((e) => e.id === d.entity);
      const e = this.entities[i];
      return e ? `${KIND_NAMES[e.kind]} ${i + 1} ${e.kind === 'slot' ? 'Breite' : 'Ø'}` : 'Durchmesser';
    }
    const o = d.orient === 'h' ? ' (waagerecht)' : d.orient === 'v' ? ' (senkrecht)' : '';
    return `${this.targetName(d.a)} – ${this.targetName(d.b)}${o}`;
  }

  private renderDims(): void {
    this.els.dimsHead.textContent = this.dims.length ? `Bemaßungen (${this.dims.length})` : 'Bemaßungen';
    if (!this.dims.length) {
      this.els.dims.replaceChildren(h('p', { class: 'note' }, 'Mit „Bemaßung“ zwei Elemente anklicken – Skizzenpunkte, Linien, Kreise oder Ecken und Kanten des Teils – und das Maß platzieren.'));
      return;
    }
    this.els.dims.replaceChildren(
      ...this.dims.map((d) => {
        const input = h('input', { type: 'text', inputmode: 'decimal', autocomplete: 'off', value: inputValue(d.value), 'aria-label': this.dimText(d) }) as HTMLInputElement;
        const box = h('div', { class: 'num' }, input, h('span', { class: 'unit' }, 'mm'));
        // a dimension applies on Enter or when the field is left, so a half-typed value does not drive the sketch
        const apply = (): void => {
          const v = parseNumber(input.value);
          if (v !== null && v > 0) this.setDimValue(d.id, v);
          else input.value = inputValue(d.value);
        };
        input.addEventListener('change', apply);
        input.addEventListener('keydown', (e) => {
          if (e.key === 'Enter') input.blur();
        });
        const on = this.sel?.kind === 'dim' && this.sel.id === d.id;
        return h(
          'div',
          {
            class: `sk-dimrow${on ? ' on' : ''}`,
            onpointerenter: () => ((this.hoverDim = d.id), this.render()),
            onpointerleave: () => ((this.hoverDim = null), this.render()),
          },
          h('span', { class: 'sk-dimname', title: this.dimText(d) }, d.kind === 'diameter' ? '⌀ ' : '↔ ', this.dimText(d)),
          box,
          h('button', { type: 'button', class: 'iconbtn', title: 'Bemaßung löschen', 'aria-label': `Bemaßung ${this.dimText(d)} löschen`, onclick: () => this.deleteDim(d.id) }, svgIcon(ICONS.trash)),
        );
      }),
    );
  }

  /** Change one entity from a field; the dimensions are solved again with the field's value held as far as they allow. */
  private editParam(id: string, params: string[], patch: (e: SketchEntity) => SketchEntity, renumbers = false): void {
    const prev = this.entities;
    this.entities = prev.map((e) => (e.id === id ? patch(e) : e));
    if (renumbers) this.dropPointDims(id);
    this.solve({ sidesFrom: prev, sticky: { entity: id, params } });
    this.refreshFields();
    this.renderStatus();
    if (renumbers) this.renderDims();
    this.render();
  }

  private refreshFields(): void {
    for (const f of this.refreshers) f();
  }

  private card(e: SketchEntity, i: number): HTMLElement {
    const id = e.id;
    const name = `${KIND_NAMES[e.kind]} ${i + 1}`;
    const cur = (): SketchEntity => this.entity(id) ?? e;
    const rec = (): Record<string, number> => cur() as unknown as Record<string, number>;
    const field = (label: string, key: string, opts: NumOpts, factor = 1): HTMLElement => {
      const f = numInput(
        () => rec()[key] * factor,
        (v) => this.editParam(id, [key], (x) => ({ ...x, [key]: v / factor }) as SketchEntity, key === 'sides'),
        opts,
        `${name} ${label}`,
      );
      this.refreshers.push(f.refresh);
      return h('label', { class: 'fs-f' }, h('span', {}, label), f.box);
    };
    const fields = (...kids: HTMLElement[]): HTMLElement => h('div', { class: 'fs-fields' }, ...kids);

    const pointTable = (p: PolylineEntity): HTMLElement => {
      const rows: HTMLElement[] = [h('span', { class: 'h' }, '#'), h('span', { class: 'h' }, 'X (mm)'), h('span', { class: 'h' }, 'Y (mm)'), h('span', {})];
      p.points.forEach((_, j) => {
        const coord = (axis: 'x' | 'y'): HTMLElement => {
          const f = numInput(
            () => (cur() as PolylineEntity).points[j]?.[axis] ?? 0,
            (v) =>
              this.editParam(id, [`${axis}${j}`], (x) => {
                const pl = x as PolylineEntity;
                return { ...pl, points: pl.points.map((q, k) => (k === j ? { ...q, [axis]: v } : q)) };
              }),
            {},
            `${name}, Punkt ${j + 1} ${axis.toUpperCase()}`,
          );
          this.refreshers.push(f.refresh);
          return f.box;
        };
        const del = (): void => {
          const before = this.snapshot();
          this.editParam(id, [], (x) => ({ ...(x as PolylineEntity), points: (x as PolylineEntity).points.filter((_, k) => k !== j) }), true);
          this.pushUndo(before);
          this.renderPanel();
        };
        rows.push(
          h('span', {}, String(j + 1)),
          coord('x'),
          coord('y'),
          h('button', { type: 'button', class: 'iconbtn', title: 'Punkt löschen', 'aria-label': `Punkt ${j + 1} löschen`, disabled: p.points.length <= 3, onclick: del }, svgIcon(ICONS.trash)),
        );
      });
      const insert = (): void => {
        const before = this.snapshot();
        // split the closing edge, so the shape stays the same until the new point is moved
        this.editParam(
          id,
          [],
          (x) => {
            const pts = (x as PolylineEntity).points;
            const a = pts[pts.length - 1] ?? v2(0, 0);
            return { ...(x as PolylineEntity), points: [...pts, lerp2(a, pts[0] ?? a, 0.5)] };
          },
          true,
        );
        this.pushUndo(before);
        this.renderPanel();
      };
      return h(
        'div',
        { class: 'fs-points' },
        h('div', { class: 'ptable' }, ...rows),
        h('div', { class: 'btnrow' }, h('button', { type: 'button', class: 'btn fs-small', onclick: insert }, svgIcon(ICONS.plus), 'Punkt einfügen')),
      );
    };

    const body = (): HTMLElement => {
      switch (e.kind) {
        case 'rect':
          return fields(field('Mitte X', 'cx', MM), field('Mitte Y', 'cy', MM), field('Breite', 'w', LEN), field('Höhe', 'h', LEN), field('Drehung', 'angle', DEG));
        case 'circle':
          return fields(field('Mitte X', 'cx', MM), field('Mitte Y', 'cy', MM), field('Durchmesser', 'r', LEN, 2));
        case 'slot':
          return fields(field('X1', 'x1', MM), field('Y1', 'y1', MM), field('X2', 'x2', MM), field('Y2', 'y2', MM), field('Breite', 'width', LEN));
        case 'polygon':
          return fields(
            field('Mitte X', 'cx', MM),
            field('Mitte Y', 'cy', MM),
            field('Radius', 'r', LEN),
            field('Seiten', 'sides', { integer: true, min: 3, max: 200 }),
            field('Drehung', 'angle', DEG),
          );
        case 'polyline':
          return pointTable(e);
      }
    };

    const problem = problemOf(e);
    const free = !this.defined.has(id);
    const node = h(
      'div',
      { class: `fs-card${this.sel?.kind === 'entity' && this.sel.id === id ? ' on' : ''}${problem ? ' bad' : ''}`, 'data-id': id },
      h(
        'div',
        { class: 'fs-card-head' },
        h('span', { class: 'fs-idx' }, String(i + 1)),
        h('span', { class: 'fs-kind' }, KIND_NAMES[e.kind]),
        h('span', { class: `sk-def ${free ? 'under' : 'full'}`, title: free ? 'Unterbestimmt' : 'Voll bestimmt' }, free ? 'frei' : 'bestimmt'),
        h('button', { type: 'button', class: 'iconbtn', title: 'Element löschen', 'aria-label': `${name} löschen`, onclick: () => this.deleteEntity(id) }, svgIcon(ICONS.trash)),
      ),
      body(),
      h('div', { class: 'fs-card-err', hidden: !problem }, problem),
    );
    const pickThis = (): void => {
      if (this.sel?.kind === 'entity' && this.sel.id === id) return;
      this.sel = { kind: 'entity', id };
      this.markCards();
      this.render();
    };
    node.addEventListener('pointerdown', pickThis);
    node.addEventListener('focusin', pickThis);
    return node;
  }

  private markCards(): void {
    for (const c of this.els.list.querySelectorAll<HTMLElement>('.fs-card')) c.classList.toggle('on', this.sel?.kind === 'entity' && c.dataset.id === this.sel.id);
    for (const r of this.els.dims.querySelectorAll<HTMLElement>('.sk-dimrow')) r.classList.remove('on');
  }

  private focusCard(id: string): void {
    const c = [...this.els.list.querySelectorAll<HTMLElement>('.fs-card')].find((x) => x.dataset.id === id);
    c?.scrollIntoView({ block: 'nearest' });
    c?.querySelector('input')?.focus();
  }

  private renderList(): void {
    this.refreshers = [];
    this.els.listHead.textContent = this.entities.length ? `Elemente (${this.entities.length})` : 'Elemente';
    if (!this.entities.length) {
      this.els.list.replaceChildren(h('p', { class: 'note' }, 'Noch keine Elemente. Ein Werkzeug wählen und direkt auf der Fläche zeichnen.'));
      return;
    }
    this.els.list.replaceChildren(...this.entities.map((e, i) => this.card(e, i)));
  }

  private renderPanel(): void {
    this.renderStatus();
    this.renderDims();
    this.renderList();
  }

  // ------------------------------------------------------------ drawing

  private dimGeometry(d: SketchDim): { segs: [V2, V2][]; at: V2 } | null {
    let an: ReturnType<typeof dimAnchors>;
    try {
      an = dimAnchors(d, this.entMap(), this.ctx);
    } catch {
      return null;
    }
    const px = this.px();
    const L = 10 * px;
    const W = 3.2 * px;
    const over = 4 * px;
    const segs: [V2, V2][] = [];
    const arrow = (p: V2, dir: V2): void => {
      const back = sub2(p, scale2(dir, L));
      const n = rotCCW(dir);
      segs.push([p, add2(back, scale2(n, W))], [p, sub2(back, scale2(n, W))]);
    };
    const place = d.place ?? v2(0, 0);
    /** Dimension line between A' and B' (on the extension lines from a and b), label along it. */
    const linear = (a: V2, b: V2, A: V2, B: V2, at: V2): { segs: [V2, V2][]; at: V2 } => {
      for (const [p, q] of [
        [a, A],
        [b, B],
      ] as const) {
        const len = dist2(p, q);
        if (len > 2 * px) segs.push([p, add2(q, scale2(sub2(q, p), over / len))]);
      }
      const span = dist2(A, B);
      if (span > 1e-9) {
        const u = scale2(sub2(B, A), 1 / span);
        const t = dot2(sub2(at, A), u);
        segs.push([t < 0 ? at : A, t > span ? at : B]);
        // arrows inside when they fit, outside otherwise
        const inside = span > 3 * L;
        arrow(A, inside ? scale2(u, -1) : u);
        arrow(B, inside ? u : scale2(u, -1));
        if (!inside) segs.push([A, sub2(A, scale2(u, 1.6 * L))], [B, add2(B, scale2(u, 1.6 * L))]);
      }
      return { segs, at };
    };
    if (d.kind === 'diameter') {
      const e = this.entity(d.entity);
      if (e?.kind === 'slot') {
        const mid = lerp2(an.a, an.b, 0.5);
        return linear(an.a, an.b, an.a, an.b, add2(mid, place));
      }
      const c = lerp2(an.a, an.b, 0.5);
      const r = dist2(an.a, an.b) / 2;
      const len = Math.hypot(place.x, place.y);
      const dir = len > 1e-9 ? scale2(place, 1 / len) : v2(1, 0);
      const p1 = add2(c, scale2(dir, r));
      const p2 = sub2(c, scale2(dir, r));
      segs.push([p2, len > r ? add2(c, place) : p1]);
      arrow(p1, dir);
      arrow(p2, scale2(dir, -1));
      return { segs, at: add2(c, place) };
    }
    const mid = lerp2(an.a, an.b, 0.5);
    const at0 = add2(mid, place);
    if (an.kind === 'points' && d.orient !== 'aligned') {
      const u = d.orient === 'h' ? v2(1, 0) : v2(0, 1);
      const n = d.orient === 'h' ? v2(0, 1) : v2(1, 0);
      const level = dot2(at0, n);
      const A = add2(an.a, scale2(n, level - dot2(an.a, n)));
      const B = add2(an.b, scale2(n, level - dot2(an.b, n)));
      return linear(an.a, an.b, A, B, add2(A, scale2(u, dot2(sub2(at0, A), u))));
    }
    // aligned between points, or perpendicular to a line: the dimension line runs along a → b
    const ab = sub2(an.b, an.a);
    const u = Math.hypot(ab.x, ab.y) > 1e-9 ? norm2(ab) : v2(0, 1);
    const n = rotCCW(u);
    const off = dot2(place, n);
    const A = add2(an.a, scale2(n, off));
    const B = add2(an.b, scale2(n, off));
    return linear(an.a, an.b, A, B, add2(lerp2(A, B, 0.5), scale2(u, dot2(place, u))));
  }

  private dimLabelText(d: SketchDim): string {
    if (d.kind === 'diameter' && this.entity(d.entity)?.kind !== 'slot') return `Ø${num(d.value)}`;
    return num(d.value);
  }

  private labelFor(d: SketchDim): HTMLElement {
    let el = this.labelEls.get(d.id);
    if (!el) {
      const id = d.id;
      el = h('div', { class: 'sk-dim', title: 'Doppelklick ändert das Maß' });
      el.style.pointerEvents = 'auto';
      el.addEventListener('pointerdown', (e) => e.stopPropagation());
      el.addEventListener('click', (e) => {
        e.stopPropagation();
        if (this.editing?.id === id) return;
        if (this.tool === 'dim') {
          // while a new dimension is being placed, a click on a label places it there
          if (this.dimFirst) this.dimClick(e.clientX, e.clientY);
          else this.editDim(id);
          return;
        }
        this.sel = { kind: 'dim', id };
        this.markCards();
        this.renderDims();
        this.render();
      });
      el.addEventListener('dblclick', (e) => {
        e.stopPropagation();
        this.editDim(id);
      });
      el.addEventListener('pointerenter', () => {
        this.hoverDim = id;
        this.render();
      });
      el.addEventListener('pointerleave', () => {
        this.hoverDim = null;
        this.render();
      });
      this.labelEls.set(id, el);
    }
    if (this.editing?.id !== d.id) el.textContent = this.dimLabelText(d);
    el.classList.toggle('on', this.sel?.kind === 'dim' && this.sel.id === d.id);
    el.classList.toggle('hover', this.hoverDim === d.id);
    return el;
  }

  private render(): void {
    if (this.closed) return;
    const css = (n: string): string => this.host.css(n);
    const colors = { free: css('--sel'), fixed: css('--fg'), hot: css('--accent'), dim: css('--muted'), origin: css('--danger'), bad: css('--danger') };
    const groups = new Map<string, OverlayLines>();
    const line = (a: V3, b: V3, color: string, width: number, opts: { dashed?: boolean; onTop?: boolean } = {}): void => {
      const k = `${color}|${width}|${opts.dashed ? 1 : 0}|${opts.onTop ? 1 : 0}`;
      let g = groups.get(k);
      if (!g) groups.set(k, (g = { seg: [], color, width, dashed: opts.dashed, onTop: opts.onTop }));
      g.seg.push(a.x, a.y, a.z, b.x, b.y, b.z);
    };
    const loop = (pts: V2[], color: string, width: number, opts: { dashed?: boolean; onTop?: boolean; open?: boolean } = {}): void => {
      const n = pts.length;
      for (let i = 0; i < (opts.open ? n - 1 : n); i++) line(this.w(pts[i]), this.w(pts[(i + 1) % n]), color, width, opts);
    };
    const points: SketchOverlay['points'] = [];
    const labels: SketchOverlay['labels'] = [];
    const px = this.px();

    // origin and axes of the sketch
    const o = v2(0, 0);
    const ax = 34 * px;
    line(this.w(o), this.w(v2(ax, 0)), colors.origin, 1.6, { onTop: true });
    line(this.w(o), this.w(v2(0, ax)), colors.origin, 1.6, { onTop: true });
    for (const [tip, d] of [
      [v2(ax, 0), v2(1, 0)],
      [v2(0, ax), v2(0, 1)],
    ] as const) {
      const back = sub2(tip, scale2(d, 7 * px));
      const n = scale2(rotCCW(d), 3 * px);
      line(this.w(tip), this.w(add2(back, n)), colors.origin, 1.6, { onTop: true });
      line(this.w(tip), this.w(sub2(back, n)), colors.origin, 1.6, { onTop: true });
    }
    points.push({ pts: [this.w(o)], color: colors.origin, size: 8 });

    // entities: blue while free, black when fully defined, orange when selected or under the mouse
    const selId = this.sel?.kind === 'entity' ? this.sel.id : null;
    const dimHot = (this.sel?.kind === 'dim' ? this.sel.id : null) ?? this.hoverDim;
    const hotDim = dimHot ? this.dims.find((d) => d.id === dimHot) : undefined;
    const hotEnts = new Set(hotDim ? dimEntities(hotDim) : []);
    for (const e of this.entities) {
      const hot = e.id === selId || e.id === this.hoverEntity || hotEnts.has(e.id);
      const color = problemOf(e) ? colors.bad : hot ? colors.hot : this.defined.has(e.id) ? colors.fixed : colors.free;
      const pts = outlineOf(e);
      if (pts.length >= 2) loop(pts, color, hot ? 3 : 2.2, { dashed: !!problemOf(e) });
      points.push({ pts: keyPoints(e).map((p) => this.w(p)), color, size: hot ? 8 : 6 });
    }

    // what the current tool would draw
    const drawing = this.tool !== 'select' && this.tool !== 'dim';
    const pend = this.pending();
    const c = this.cursor;
    let tip = '';
    if (drawing && c && pend.length) {
      if (this.tool === 'polyline') {
        loop([...pend, c.p], colors.free, 1.8, { dashed: true, onTop: true, open: true });
        if (pend.length >= 2) loop([c.p, pend[0]], colors.free, 1, { dashed: true, onTop: true, open: true });
        const closes = pend.length >= 3 && this.screenDist(c.p, pend[0]) < SNAP_PX;
        points.push({ pts: [this.w(pend[0])], color: colors.hot, size: closes ? 14 : 10 });
        const last = pend[pend.length - 1];
        tip = `${num(dist2(last, c.p))} mm  ${num(deg(Math.atan2(c.p.y - last.y, c.p.x - last.x)), 1)}°`;
      } else {
        const e = this.draft(c);
        const pts = e ? outlineOf(e) : [];
        if (pts.length >= 3) loop(pts, colors.free, 1.8, { dashed: true, onTop: true });
        if (this.tool === 'slot' && pend.length === 1) {
          loop([pend[0], c.p], colors.free, 1.4, { dashed: true, onTop: true, open: true });
          tip = `${num(dist2(pend[0], c.p))} mm  ${num(deg(Math.atan2(c.p.y - pend[0].y, c.p.x - pend[0].x)), 1)}°`;
        } else if (e) tip = previewText(e);
      }
      points.push({ pts: pend.map((p) => this.w(p)), color: colors.free, size: 7 });
    }
    if (drawing && c) {
      points.push({ pts: [this.w(c.p)], color: c.hit ? colors.hot : colors.free, size: c.hit ? 11 : 6 });
      if (!tip) tip = `X ${num(c.p.x)}  Y ${num(c.p.y)}`;
    } else if (this.press?.kind === 'drag' && this.press.moved && c?.hit) points.push({ pts: [this.w(c.p)], color: colors.hot, size: 11 });

    // dimensions
    const drawDim = (d: SketchDim, color: string, el: HTMLElement): void => {
      const g = this.dimGeometry(d);
      if (!g) return;
      for (const [a, b] of g.segs) line(this.w(a), this.w(b), color, 1.4, { onTop: true });
      labels.push({ el, at: this.w(g.at) });
    };
    for (const d of this.dims) {
      const hot = d.id === dimHot;
      drawDim(d, hot ? colors.hot : colors.dim, this.labelFor(d));
    }

    // smart dimension: picked items, the item under the mouse and the dimension being placed
    if (this.tool === 'dim') {
      const mark = (cand: Cand, color: string): void => {
        if (cand.kind === 'point') {
          const world = cand.target.kind === 'modelPoint' ? cand.world : this.w(cand.sk);
          points.push({ pts: [world], color, size: 12 });
        } else if (cand.kind === 'line') {
          const model = cand.target.kind === 'modelLine';
          line(model ? cand.wa : this.w(cand.a), model ? cand.wb : this.w(cand.b), color, 4, { onTop: true });
          // a part edge off the sketch plane: show where it lands on the sketch
          if (model && Math.hypot(cand.wa.x - this.w(cand.a).x, cand.wa.y - this.w(cand.a).y, cand.wa.z - this.w(cand.a).z) > 1e-3) {
            line(this.w(cand.a), this.w(cand.b), color, 1.4, { dashed: true, onTop: true });
          }
        } else {
          const e = this.entity(cand.entity);
          if (e) loop(outlineOf(e), color, 4, { onTop: true });
        }
        if (cand.kind === 'point' && cand.target.kind === 'modelPoint') {
          line(cand.world, this.w(cand.sk), color, 1.2, { dashed: true, onTop: true });
          points.push({ pts: [this.w(cand.sk)], color, size: 7 });
        }
      };
      if (this.hoverCand && !(this.dimFirst && SketchMode.sameCand(this.hoverCand, this.dimFirst))) mark(this.hoverCand, colors.hot);
      if (this.dimFirst) mark(this.dimFirst, colors.free);
      if (this.dimSecond) mark(this.dimSecond, colors.free);
      const s = this.screenAt;
      const m = s ? this.toSketch(s.x, s.y) : null;
      if (this.dimFirst && m && !(this.hoverCand && !this.dimSecond && !SketchMode.sameCand(this.hoverCand, this.dimFirst))) {
        const d = this.dimFrom(this.dimFirst, this.dimSecond, m);
        if (d && typeof d !== 'string') {
          try {
            const value = dimValue(d, this.entities, this.ctx);
            this.previewDimEl.textContent = d.kind === 'diameter' && this.entity(d.entity)?.kind !== 'slot' ? `Ø${num(value)}` : num(value);
            drawDim({ ...d, value }, colors.free, this.previewDimEl);
          } catch {
            // nothing to preview
          }
        }
      }
    }

    if (tip && this.screenAt && c) {
      this.tipEl.textContent = tip;
      labels.push({ el: this.tipEl, at: this.w(c.p) });
    }
    this.host.viewer.setSketchOverlay({ lines: [...groups.values()], points, labels });
    this.renderHintMaybe();
  }

  private lastHint = '';

  /** The stage hint follows the tool state without rebuilding the panel on every mouse move. */
  private renderHintMaybe(): void {
    const t = this.hintText();
    if (t === this.lastHint) return;
    this.lastHint = t;
    this.renderHint();
  }
}
