// Sketch editor for cut profiles on a face ("Skizze auf Fläche"): rectangles,
// circles, slots, regular polygons and closed polylines drawn over the face
// outline, with grid and point snapping and an editable element list.
// Coordinates are sketch coordinates: origin at the reference edge start,
// x along the edge, y into the face.

import './faceSketcher.css';
import { bbox2, distPointSegment, isSimple, pointInPolygon, signedArea } from '../kernel/polygon';
import { SketchEntity, SketchEntityKind, entityOutline, entityProblem, newEntityId } from '../kernel/sketch';
import { V2, add2, dist2, dot2, lerp2, rotCCW, scale2, sub2, v2 } from '../kernel/vec';
import { h, svgIcon } from './dom';
import { inputValue, num, parseNumber } from './format';
import { ICONS } from './icons';

export interface FaceSketchContext {
  /** Dialog title, e.g. 'Skizze auf Kantenlasche1'. */
  title: string;
  /** Face outline in sketch coordinates (closed). */
  outline: V2[];
  /** Existing holes in sketch coordinates. */
  holes: V2[][];
  /** Bend seam edges in sketch coordinates. */
  seams: [V2, V2][];
}

type Tool = 'select' | 'rect' | 'crect' | 'circle' | 'slot' | 'polygon' | 'polyline';
type PolylineEntity = Extract<SketchEntity, { kind: 'polyline' }>;

interface Snapped {
  p: V2;
  /** Sits on an existing point, so sizes taken from it are not rounded to the grid. */
  hit: boolean;
}

type Gesture =
  | { kind: 'pan'; x: number; y: number; ox: number; oy: number }
  | { kind: 'pinch'; d0: number; s0: number; m: V2 }
  | { kind: 'drag'; e: SketchEntity; orig: SketchEntity; grab: V2; down: V2; sx: number; sy: number; moved: boolean }
  | { kind: 'tap'; sx: number; sy: number; at: Snapped };

interface NumOpts {
  unit?: string;
  /** Exclusive minimum. */
  gt?: number;
  min?: number;
  max?: number;
  integer?: boolean;
}

const NS = 'http://www.w3.org/2000/svg';
const SNAP_PX = 9;
const SLOT_WIDTH = 10;
/** Canvas presses this soon after opening belong to the click that opened the dialog (at least the usual double-click time). */
const OPEN_GRACE_MS = 500;
const CLICKS: Record<Exclude<Tool, 'select' | 'polyline'>, number> = { rect: 2, crect: 2, circle: 2, polygon: 2, slot: 3 };

const SNAPS: [string, number][] = [
  ['0,5 mm', 0.5],
  ['1 mm', 1],
  ['5 mm', 5],
  ['10 mm', 10],
  ['Aus', 0],
];

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
  { id: 'rect', label: 'Rechteck', icon: ICONS.rect },
  { id: 'crect', label: 'Mittelpunkt-Rechteck', icon: icon(`<rect x="4" y="6" width="16" height="12"/><path d="M12 12l8 6" stroke-dasharray="2 2"/>${dot(12, 12)}`) },
  { id: 'circle', label: 'Kreis', icon: icon(`<circle cx="12" cy="12" r="8"/><path d="M12 12l5.7-5.7" stroke-dasharray="2 2"/>${dot(12, 12)}`) },
  { id: 'slot', label: 'Langloch', icon: icon(`<path d="M8 8h8a4 4 0 0 1 0 8H8a4 4 0 0 1 0-8z"/><path d="M8 12h8" stroke-dasharray="2 2"/>${dot(8, 12)}${dot(16, 12)}`) },
  { id: 'polygon', label: 'Vieleck', icon: icon(`<path d="M12 3.5l7.4 4.25v8.5L12 20.5l-7.4-4.25v-8.5z"/>${dot(12, 12)}`) },
  { id: 'polyline', label: 'Linienzug', icon: icon(`<path d="M4 17L7 6l7 4 6-3-1 12z"/>${dot(4, 17)}${dot(7, 6)}${dot(14, 10)}${dot(20, 7)}${dot(19, 19)}`) },
];

let dialogSeq = 0;

const deg = (rad: number): number => (rad * 180) / Math.PI;
const fmt = (v: number): number => Math.round(v * 100) / 100;
/** Round a size to the grid, but never down to zero. */
const roundTo = (v: number, g: number): number => (g > 0 ? Math.max(g, Math.round(v / g) * g) : v);
const clampScale = (s: number): number => Math.min(200, Math.max(0.05, s));

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

/** Points that snap and that a drag can grab: centres, corners, slot centres. */
function keyPoints(e: SketchEntity): V2[] {
  switch (e.kind) {
    case 'polyline':
      return e.points;
    case 'circle':
      return [v2(e.cx, e.cy)];
    case 'slot':
      return [v2(e.x1, e.y1), v2(e.x2, e.y2), v2((e.x1 + e.x2) / 2, (e.y1 + e.y2) / 2)];
    default:
      return [v2(e.cx, e.cy), ...outlineOf(e)];
  }
}

function problemOf(e: SketchEntity): string {
  const p = entityProblem(e);
  if (p || e.kind !== 'polyline') return p;
  // the cut feature rejects crossing profiles; say so before it does
  const pts = e.points.filter((q, i) => dist2(q, e.points[(i + 1) % e.points.length]) >= 1e-6);
  return isSimple(pts) ? '' : 'Der Linienzug überschneidet sich selbst. Bitte die Punkte so setzen, dass sich die Linien nicht kreuzen.';
}

function dimLabel(e: SketchEntity): { at: V2; text: string } | null {
  switch (e.kind) {
    case 'rect':
      return { at: v2(e.cx, e.cy), text: `${num(e.w)} × ${num(e.h)}` };
    case 'circle':
      return { at: v2(e.cx, e.cy), text: `Ø ${num(2 * e.r)}` };
    case 'slot': {
      const len = Math.hypot(e.x2 - e.x1, e.y2 - e.y1) + e.width;
      return { at: v2((e.x1 + e.x2) / 2, (e.y1 + e.y2) / 2), text: `${num(len)} × ${num(e.width)}` };
    }
    case 'polygon':
      return { at: v2(e.cx, e.cy), text: `${e.sides}-Eck R ${num(e.r)}` };
    case 'polyline':
      return null;
  }
}

function previewText(e: SketchEntity): string {
  switch (e.kind) {
    case 'polygon':
      return `R ${num(e.r)}  ${num(e.angle, 1)}°`;
    case 'slot':
      return `Breite ${num(e.width)}`;
    default:
      return dimLabel(e)?.text ?? '';
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
    input.value = inputValue(get());
    box.classList.remove('fs-bad');
  };
  input.addEventListener('input', () => {
    const v = parseNumber(input.value);
    box.classList.toggle('fs-bad', !valid(v));
    if (valid(v) && v !== get()) set(v);
  });
  // on blur or Enter: tidy the text, or fall back to the last valid value
  input.addEventListener('change', refresh);
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') refresh();
    if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
    e.preventDefault();
    const step = e.shiftKey ? 10 : e.altKey && !opts.integer ? 0.1 : 1;
    const next = Math.round(((parseNumber(input.value) ?? get()) + (e.key === 'ArrowUp' ? step : -step)) * 1e6) / 1e6;
    if (valid(next)) {
      set(next);
      refresh();
    }
  });
  return { box, refresh };
}

export function openFaceSketcher(init: SketchEntity[], ctx: FaceSketchContext): Promise<SketchEntity[] | null> {
  return new Promise((resolve) => {
    const entities = init.map(cloneEntity);
    let tool: Tool = entities.length ? 'select' : 'rect';
    let clicks: V2[] = [];
    let lastClick = v2(0, 0);
    let lastAdd: { p: V2; t: number } | null = null;
    let lastTap: { s: V2; t: number } | null = null;
    let cursor: Snapped | null = null;
    let lastScreen: V2 | null = null;
    let shift = false;
    let selected: string | null = null;
    let gesture: Gesture | null = null;
    const pointers = new Map<number, V2>();
    let snapOn = true;
    let grid = 1;
    let sides = 6;
    let scale = 2;
    let ox = 100;
    let oy = 400;
    let userView = false;
    let showErrors = false;
    let done = false;
    const uid = `fs${++dialogSeq}`;
    // the dialog opens under the cursor: the second click of a double-click on the face must not start a shape
    const openedAt = performance.now();
    const prevFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;

    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('class', 'fs-svg');
    svg.setAttribute('role', 'img');
    svg.setAttribute('aria-label', 'Skizzenfläche');
    const readout = h('div', { class: 'fs-readout', hidden: true });
    const canvas = h('div', { class: 'sk-canvas' }, svg, readout);
    const side = h('div', { class: 'sk-side' });
    const toolBox = h('div', { class: 'section' });
    const hintBox = h('div', { class: 'fs-hint' });
    const listHead = h('h3', {});
    const listBox = h('div', { class: 'fs-list' });
    const msgBox = h('div', { class: 'errbox', role: 'alert', hidden: true });
    const cards = new Map<string, { card: HTMLElement; err: HTMLElement; refresh: () => void }>();

    const toScreen = (p: V2): V2 => v2(ox + p.x * scale, oy - p.y * scale);
    const toModel = (s: V2): V2 => v2((s.x - ox) / scale, (oy - s.y) / scale);
    const screenDist = (a: V2, b: V2): number => dist2(a, b) * scale;
    const gridStep = (): number => (snapOn ? grid : 0);

    const finish = (result: SketchEntity[] | null): void => {
      if (done) return;
      done = true;
      window.removeEventListener('keydown', onKey, true);
      window.removeEventListener('keyup', onKeyUp, true);
      resizer.disconnect();
      modal.remove();
      prevFocus?.focus();
      resolve(result);
    };

    // ------------------------------------------------------------ snapping

    const snapTargets = (exclude: string | null): V2[] => {
      const out: V2[] = [...ctx.outline, ...clicks];
      ctx.outline.forEach((p, i) => out.push(lerp2(p, ctx.outline[(i + 1) % ctx.outline.length], 0.5)));
      for (const hole of ctx.holes) out.push(...hole);
      for (const e of entities) if (e.id !== exclude) out.push(...keyPoints(e));
      return out;
    };

    const snap = (raw: V2, from: V2 | null, exclude: string | null = null): Snapped => {
      const g = gridStep();
      if (shift && from) {
        const step = Math.PI / 12;
        const a = Math.round(Math.atan2(raw.y - from.y, raw.x - from.x) / step) * step;
        const d = v2(Math.cos(a), Math.sin(a));
        return { p: add2(from, scale2(d, roundTo(Math.max(0, dot2(sub2(raw, from), d)), g))), hit: false };
      }
      if (snapOn) {
        let best: V2 | null = null;
        let bd = SNAP_PX / scale;
        for (const q of snapTargets(exclude)) {
          const d = dist2(q, raw);
          if (d < bd) {
            bd = d;
            best = q;
          }
        }
        if (best) return { p: { ...best }, hit: true };
      }
      return { p: g > 0 ? v2(Math.round(raw.x / g) * g, Math.round(raw.y / g) * g) : raw, hit: false };
    };

    const shiftFrom = (): V2 | null => {
      if (tool === 'polyline' && clicks.length) return clicks[clicks.length - 1];
      if (tool === 'slot' && clicks.length === 1) return clicks[0];
      return null;
    };
    const snapAt = (s: V2): Snapped => snap(toModel(s), shiftFrom());

    // ------------------------------------------------------------ drawing tools

    /** Placed clicks, or the start of a press-drag stroke that has not been released yet. */
    const pending = (): V2[] => {
      if (clicks.length) return clicks;
      const g = gesture;
      return g?.kind === 'tap' && lastScreen && Math.hypot(lastScreen.x - g.sx, lastScreen.y - g.sy) > 6 ? [g.at.p] : [];
    };

    /** The entity the current tool would create with the next click at `c`. */
    const draft = (c: Snapped): SketchEntity | null => {
      const a = pending()[0];
      if (!a) return null;
      const p = c.p;
      const g = c.hit ? 0 : gridStep();
      switch (tool) {
        case 'rect':
          return { kind: 'rect', id: '', cx: (a.x + p.x) / 2, cy: (a.y + p.y) / 2, w: Math.abs(p.x - a.x), h: Math.abs(p.y - a.y), angle: 0 };
        case 'crect':
          return { kind: 'rect', id: '', cx: a.x, cy: a.y, w: 2 * Math.abs(p.x - a.x), h: 2 * Math.abs(p.y - a.y), angle: 0 };
        case 'circle':
          return { kind: 'circle', id: '', cx: a.x, cy: a.y, r: roundTo(2 * dist2(a, p), g) / 2 };
        case 'polygon': {
          const ang = deg(Math.atan2(p.y - a.y, p.x - a.x));
          return { kind: 'polygon', id: '', cx: a.x, cy: a.y, r: roundTo(dist2(a, p), g), sides, angle: shift ? Math.round(ang / 15) * 15 : ang };
        }
        case 'slot': {
          const b = clicks[1];
          if (!b) return { kind: 'slot', id: '', x1: a.x, y1: a.y, x2: p.x, y2: p.y, width: SLOT_WIDTH };
          return { kind: 'slot', id: '', x1: a.x, y1: a.y, x2: b.x, y2: b.y, width: roundTo(2 * distPointSegment(p, a, b), g) };
        }
        default:
          return null;
      }
    };

    const add = (e: SketchEntity): void => {
      const ent = { ...e, id: newEntityId() };
      entities.push(ent);
      lastAdd = { p: lastClick, t: performance.now() };
      clicks = [];
      selected = ent.id;
      renderList();
      revealCard(ent.id);
      revalidate();
      renderHint();
      render();
    };

    const closePolyline = (): void => {
      if (tool === 'polyline' && clicks.length >= 3) add({ kind: 'polyline', id: '', points: clicks });
    };

    const addDefaultSlot = (): void => {
      if (tool !== 'slot' || clicks.length !== 2) return;
      const [a, b] = clicks;
      add({ kind: 'slot', id: '', x1: a.x, y1: a.y, x2: b.x, y2: b.y, width: SLOT_WIDTH });
    };

    const place = (c: Snapped): void => {
      if (tool === 'select') return;
      const last = clicks[clicks.length - 1];
      lastClick = c.p;
      if (tool === 'polyline' && clicks.length >= 3 && screenDist(c.p, clicks[0]) < SNAP_PX) {
        closePolyline();
        return;
      }
      // a second click on the same spot adds nothing, also right after it finished a shape (double click)
      if (last && screenDist(c.p, last) < 3) return;
      if (!last && lastAdd && performance.now() - lastAdd.t < 500 && screenDist(c.p, lastAdd.p) < 3) return;
      if (tool === 'polyline' || clicks.length + 1 < CLICKS[tool]) {
        clicks.push(c.p);
        renderHint();
        return;
      }
      const e = draft(c);
      if (e && !problemOf(e)) add(e);
    };

    const cancelDrag = (): void => {
      if (gesture?.kind === 'drag') Object.assign(gesture.e, cloneEntity(gesture.orig));
      gesture = null;
    };

    const abortAction = (): void => {
      cancelDrag();
      clicks = [];
      renderHint();
      render();
    };

    // ------------------------------------------------------------ selection & list

    const revealCard = (id: string): void => {
      const card = cards.get(id)?.card;
      // only scroll the side panel itself; on phones the page must not jump away from the canvas
      if (!card || side.scrollHeight <= side.clientHeight) return;
      const r = card.getBoundingClientRect();
      const b = side.getBoundingClientRect();
      if (r.top < b.top) side.scrollTop -= b.top - r.top + 8;
      else if (r.bottom > b.bottom) side.scrollTop += r.bottom - b.bottom + 8;
    };

    const select = (id: string | null, reveal: boolean): void => {
      selected = id;
      for (const [k, c] of cards) c.card.classList.toggle('on', k === id);
      if (reveal && id) revealCard(id);
      render();
    };

    const remove = (id: string): void => {
      const i = entities.findIndex((e) => e.id === id);
      if (i < 0) return;
      entities.splice(i, 1);
      if (gesture?.kind === 'drag' && gesture.e.id === id) gesture = null;
      if (selected === id) selected = null;
      renderList();
      revalidate();
      render();
    };

    const validate = (): string => {
      if (!entities.length) return 'Die Skizze ist leer. Bitte mindestens ein Profil zeichnen.';
      for (let i = 0; i < entities.length; i++) {
        const p = problemOf(entities[i]);
        if (p) return `${KIND_NAMES[entities[i].kind]} ${i + 1}: ${p}`;
      }
      return '';
    };

    const revalidate = (): void => {
      if (!showErrors) return;
      const m = validate();
      msgBox.textContent = m;
      msgBox.hidden = !m;
    };

    const changed = (e: SketchEntity): void => {
      const c = cards.get(e.id);
      if (c) {
        const p = problemOf(e);
        c.card.classList.toggle('bad', p !== '');
        c.err.textContent = p;
        c.err.hidden = !p;
      }
      revalidate();
      render();
    };

    const card = (e: SketchEntity, i: number): HTMLElement => {
      const name = `${KIND_NAMES[e.kind]} ${i + 1}`;
      const refreshers: (() => void)[] = [];
      const input = (get: () => number, set: (v: number) => void, opts: NumOpts, aria?: string): HTMLElement => {
        const f = numInput(get, (v) => (set(v), changed(e)), opts, aria);
        refreshers.push(f.refresh);
        return f.box;
      };
      const field = (label: string, get: () => number, set: (v: number) => void, opts: NumOpts): HTMLElement =>
        h('label', { class: 'fs-f' }, h('span', {}, label), input(get, set, opts));
      const fields = (...kids: HTMLElement[]): HTMLElement => h('div', { class: 'fs-fields' }, ...kids);

      const pointTable = (p: PolylineEntity): HTMLElement => {
        const rows: HTMLElement[] = [h('span', { class: 'h' }, '#'), h('span', { class: 'h' }, 'X (mm)'), h('span', { class: 'h' }, 'Y (mm)'), h('span', {})];
        p.points.forEach((_, j) => {
          const coord = (axis: 'x' | 'y'): HTMLElement =>
            input(() => p.points[j][axis], (v) => (p.points[j] = { ...p.points[j], [axis]: v }), {}, `${name}, Punkt ${j + 1} ${axis.toUpperCase()}`);
          const del = (): void => {
            p.points.splice(j, 1);
            renderList();
            changed(p);
          };
          rows.push(
            h('span', {}, String(j + 1)),
            coord('x'),
            coord('y'),
            h('button', { type: 'button', class: 'iconbtn', title: 'Punkt löschen', 'aria-label': `Punkt ${j + 1} löschen`, disabled: p.points.length <= 3, onclick: del }, svgIcon(ICONS.trash)),
          );
        });
        const insert = (): void => {
          // split the closing edge, so the shape stays the same until the new point is moved
          const a = p.points[p.points.length - 1] ?? v2(0, 0);
          p.points.push(lerp2(a, p.points[0] ?? a, 0.5));
          renderList();
          changed(p);
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
          case 'rect': {
            const r = e;
            return fields(
              field('Mitte X', () => r.cx, (v) => (r.cx = v), MM),
              field('Mitte Y', () => r.cy, (v) => (r.cy = v), MM),
              field('Breite', () => r.w, (v) => (r.w = v), LEN),
              field('Höhe', () => r.h, (v) => (r.h = v), LEN),
              field('Drehung', () => r.angle, (v) => (r.angle = v), DEG),
            );
          }
          case 'circle': {
            const c = e;
            return fields(
              field('Mitte X', () => c.cx, (v) => (c.cx = v), MM),
              field('Mitte Y', () => c.cy, (v) => (c.cy = v), MM),
              field('Durchmesser', () => 2 * c.r, (v) => (c.r = v / 2), LEN),
            );
          }
          case 'slot': {
            const s = e;
            return fields(
              field('X1', () => s.x1, (v) => (s.x1 = v), MM),
              field('Y1', () => s.y1, (v) => (s.y1 = v), MM),
              field('X2', () => s.x2, (v) => (s.x2 = v), MM),
              field('Y2', () => s.y2, (v) => (s.y2 = v), MM),
              field('Breite', () => s.width, (v) => (s.width = v), LEN),
            );
          }
          case 'polygon': {
            const p = e;
            return fields(
              field('Mitte X', () => p.cx, (v) => (p.cx = v), MM),
              field('Mitte Y', () => p.cy, (v) => (p.cy = v), MM),
              field('Radius', () => p.r, (v) => (p.r = v), LEN),
              field('Seiten', () => p.sides, (v) => (p.sides = v), { integer: true, min: 3, max: 200 }),
              field('Drehung', () => p.angle, (v) => (p.angle = v), DEG),
            );
          }
          case 'polyline':
            return pointTable(e);
        }
      };

      const problem = problemOf(e);
      const err = h('div', { class: 'fs-card-err', hidden: !problem }, problem);
      const node = h(
        'div',
        { class: `fs-card${e.id === selected ? ' on' : ''}${problem ? ' bad' : ''}` },
        h(
          'div',
          { class: 'fs-card-head' },
          h('span', { class: 'fs-idx' }, String(i + 1)),
          h('span', { class: 'fs-kind' }, KIND_NAMES[e.kind]),
          h('button', { type: 'button', class: 'iconbtn', title: 'Element löschen', 'aria-label': `${name} löschen`, onclick: () => remove(e.id) }, svgIcon(ICONS.trash)),
        ),
        body(),
        err,
      );
      const pickThis = (): void => {
        if (selected !== e.id) select(e.id, false);
      };
      node.addEventListener('pointerdown', pickThis);
      node.addEventListener('focusin', pickThis);
      cards.set(e.id, { card: node, err, refresh: () => refreshers.forEach((f) => f()) });
      return node;
    };

    const renderList = (): void => {
      cards.clear();
      listHead.textContent = entities.length ? `Elemente (${entities.length})` : 'Elemente';
      if (!entities.length) {
        listBox.replaceChildren(h('p', { class: 'note' }, 'Noch keine Elemente. Ein Werkzeug wählen und auf der Fläche zeichnen.'));
        return;
      }
      listBox.replaceChildren(...entities.map(card));
    };

    // ------------------------------------------------------------ side panel

    const hintText = (): string => {
      const n = clicks.length;
      const esc = n ? ' Esc bricht ab.' : '';
      switch (tool) {
        case 'select':
          return 'Element anklicken wählt es aus, Ziehen verschiebt es, Entf löscht es. Freie Fläche ziehen verschiebt die Ansicht.';
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
    };

    const renderHint = (): void => {
      const btn = (label: string, onclick: () => void): HTMLElement => h('button', { type: 'button', class: 'btn fs-small', onclick }, label);
      const acts: HTMLElement[] = [];
      if (tool === 'slot' && clicks.length === 2) acts.push(btn(`Breite ${SLOT_WIDTH} mm`, addDefaultSlot));
      if (tool === 'polyline' && clicks.length >= 3) acts.push(btn('Linienzug schließen', closePolyline));
      if (clicks.length) acts.push(btn('Verwerfen', abortAction));
      hintBox.replaceChildren(h('p', { class: 'note' }, hintText()), ...(acts.length ? [h('div', { class: 'btnrow' }, ...acts)] : []));
    };

    const setTool = (t: Tool): void => {
      cancelDrag();
      tool = t;
      clicks = [];
      svg.style.cursor = t === 'select' ? 'default' : '';
      if (lastScreen) cursor = t === 'select' ? { p: toModel(lastScreen), hit: false } : snapAt(lastScreen);
      renderTools();
      render();
    };

    const renderTools = (): void => {
      const btns = TOOLS.map((t) =>
        h(
          'button',
          { type: 'button', class: `tb small${tool === t.id ? ' on' : ''}`, title: t.label, 'aria-pressed': tool === t.id ? 'true' : 'false', onclick: () => setTool(t.id) },
          svgIcon(t.icon),
          t.label,
        ),
      );
      const sidesField = (): HTMLElement =>
        h('label', { class: 'field' }, h('span', {}, 'Seiten des Vielecks'), numInput(() => sides, (v) => ((sides = v), render()), { integer: true, min: 3, max: 200 }).box);
      toolBox.replaceChildren(h('h3', {}, 'Werkzeug'), h('div', { class: 'fs-tools' }, ...btns), ...(tool === 'polygon' ? [sidesField()] : []), hintBox);
      renderHint();
    };

    const snapCb = h('input', { type: 'checkbox', checked: snapOn });
    const snapSel = h('select', {}, ...SNAPS.map(([l, v]) => h('option', { value: String(v), selected: v === grid }, l)));
    snapCb.addEventListener('change', () => {
      snapOn = snapCb.checked;
      snapSel.disabled = !snapOn;
    });
    snapSel.addEventListener('change', () => (grid = Number(snapSel.value)));

    side.append(
      toolBox,
      h(
        'div',
        { class: 'section' },
        h('h3', {}, 'Fangen'),
        h('label', { class: 'check' }, snapCb, 'Fangen'),
        h('label', { class: 'field' }, h('span', {}, 'Raster'), snapSel),
        h('p', { class: 'note fs-note' }, 'Fängt Ecken und Kantenmitten der Fläche, Bohrungen und die Punkte der Elemente. Mausrad oder zwei Finger zoomen, mittlere oder rechte Maustaste verschiebt die Ansicht.'),
      ),
      msgBox,
      h('div', { class: 'section' }, listHead, listBox),
    );

    // ------------------------------------------------------------ rendering

    const el = (tag: string, attrs: Record<string, string | number>, text?: string): SVGElement => {
      const e = document.createElementNS(NS, tag);
      for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, String(v));
      if (text !== undefined) e.textContent = text;
      return e;
    };

    const pathD = (pts: V2[], close: boolean): string =>
      pts
        .map((p, i) => {
          const s = toScreen(p);
          return `${i ? 'L' : 'M'}${fmt(s.x)} ${fmt(s.y)}`;
        })
        .join('') + (close && pts.length ? 'Z' : '');

    const textEl = (cls: string, at: V2, label: string, angle = 0): SVGElement =>
      el('text', { class: cls, x: fmt(at.x), y: fmt(at.y), ...(angle ? { transform: `rotate(${fmt(angle)} ${fmt(at.x)} ${fmt(at.y)})` } : {}) }, label);

    /** Screen angle of a segment, kept readable (never upside down). */
    const readableAngle = (sa: V2, sb: V2): number => {
      const a = deg(Math.atan2(sb.y - sa.y, sb.x - sa.x));
      return a > 90 ? a - 180 : a < -90 ? a + 180 : a;
    };

    const hatch = `${uid}-hatch`;
    const hatchSel = `${uid}-hatch-sel`;
    const defs = (): SVGElement => {
      const pattern = (id: string, cls: string): SVGElement => {
        const p = el('pattern', { id, class: cls, patternUnits: 'userSpaceOnUse', width: 7, height: 7, patternTransform: 'rotate(45)' });
        p.appendChild(el('line', { x1: 0, y1: 0, x2: 0, y2: 7 }));
        return p;
      };
      const d = el('defs', {});
      d.append(pattern(hatch, 'fs-hatch'), pattern(hatchSel, 'fs-hatch fs-hatch-sel'));
      return d;
    };

    const gridLayer = (w: number, hh: number): SVGElement[] => {
      const g = [0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000].find((s) => s * scale >= 10) ?? 1000;
      const tl = toModel(v2(0, 0));
      const br = toModel(v2(w, hh));
      let minor = '';
      let major = '';
      for (let i = Math.floor(tl.x / g); i * g <= br.x; i++) {
        const seg = `M${fmt(toScreen(v2(i * g, 0)).x)} 0V${hh}`;
        if (i % 5 === 0) major += seg;
        else minor += seg;
      }
      for (let i = Math.floor(br.y / g); i * g <= tl.y; i++) {
        const seg = `M0 ${fmt(toScreen(v2(0, i * g)).y)}H${w}`;
        if (i % 5 === 0) major += seg;
        else minor += seg;
      }
      return [el('path', { class: 'fs-grid', d: minor }), el('path', { class: 'fs-grid fs-grid-major', d: major })];
    };

    const faceLayer = (): SVGElement[] => {
      const out: SVGElement[] = [];
      if (ctx.outline.length >= 3) {
        // one even-odd path, so holes show the background like the area around the face
        out.push(el('path', { class: 'fs-face', d: [ctx.outline, ...ctx.holes].map((r) => pathD(r, true)).join('') }));
      }
      for (const [a, b] of ctx.seams) {
        const sa = toScreen(a);
        const sb = toScreen(b);
        out.push(el('line', { class: 'fs-seam', x1: fmt(sa.x), y1: fmt(sa.y), x2: fmt(sb.x), y2: fmt(sb.y) }));
        if (dist2(sa, sb) < 60) continue;
        // label on the face side of the seam
        const mid = lerp2(a, b, 0.5);
        const n = scale2(rotCCW(sub2(b, a)), 12 / scale / dist2(a, b));
        let at = add2(mid, n);
        if (ctx.outline.length >= 3 && !pointInPolygon(at, ctx.outline)) at = sub2(mid, n);
        out.push(textEl('fs-seam-label', toScreen(at), 'Biegung', readableAngle(sa, sb)));
      }
      return out;
    };

    const axesLayer = (): SVGElement[] => {
      const o = toScreen(v2(0, 0));
      const x = fmt(o.x);
      const y = fmt(o.y);
      return [
        el('path', { class: 'fs-axis', d: `M${x} ${y}h44m-6 -4l6 4l-6 4M${x} ${y}v-44m-4 6l4 -6l4 6` }),
        el('circle', { class: 'fs-origin', cx: x, cy: y, r: 3.5 }),
        el('text', { class: 'fs-axis-label', x: x + 50, y: y + 4 }, 'X'),
        el('text', { class: 'fs-axis-label', x: x - 4, y: y - 50 }, 'Y'),
      ];
    };

    const entityLayer = (): SVGElement[] => {
      const out: SVGElement[] = [];
      const sel = entities.find((e) => e.id === selected) ?? null;
      const order = sel ? [...entities.filter((e) => e !== sel), sel] : entities;
      for (const e of order) {
        const pts = outlineOf(e);
        if (pts.length < 2) continue;
        const on = e === sel ? ' on' : '';
        const d = pathD(pts, true);
        out.push(el('path', { class: `fs-ent-bg${on}`, d }));
        out.push(el('path', { class: `fs-ent${on}${problemOf(e) ? ' bad' : ''}`, d, fill: `url(#${on ? hatchSel : hatch})` }));
      }
      // labels go on top of all shapes so overlapping profiles stay readable
      for (const e of order) {
        const lab = dimLabel(e);
        const pts = outlineOf(e);
        if (!lab || pts.length < 3) continue;
        const b = bbox2(pts);
        if (Math.max(b.maxX - b.minX, b.maxY - b.minY) * scale < 44) continue;
        out.push(textEl('fs-dim', toScreen(lab.at), lab.text));
      }
      if (!sel) return out;
      if (sel.kind === 'polyline') {
        sel.points.forEach((a, i) => {
          const b = sel.points[(i + 1) % sel.points.length];
          const sa = toScreen(a);
          const sb = toScreen(b);
          if (dist2(sa, sb) < 40) return;
          const n = scale2(rotCCW(sub2(sb, sa)), 9 / dist2(sa, sb));
          out.push(textEl('fs-dim fs-seg', add2(lerp2(sa, sb, 0.5), n), num(dist2(a, b)), readableAngle(sa, sb)));
        });
      }
      for (const k of keyPoints(sel)) {
        const s = toScreen(k);
        out.push(el('rect', { class: 'fs-key', x: fmt(s.x - 3.5), y: fmt(s.y - 3.5), width: 7, height: 7 }));
      }
      return out;
    };

    const previewLayer = (): SVGElement[] => {
      const clicks = pending();
      if (tool === 'select' || !cursor || !clicks.length) return [];
      const out: SVGElement[] = [];
      const c = cursor.p;
      let label = '';
      if (tool === 'polyline') {
        const last = clicks[clicks.length - 1];
        out.push(el('path', { class: 'fs-preview-line', d: pathD([...clicks, c], false) }));
        if (clicks.length >= 2) out.push(el('path', { class: 'fs-preview-close', d: pathD([c, clicks[0]], false) }));
        const f = toScreen(clicks[0]);
        const closes = clicks.length >= 3 && screenDist(c, clicks[0]) < SNAP_PX;
        out.push(el('circle', { class: `fs-first${closes ? ' on' : ''}`, cx: fmt(f.x), cy: fmt(f.y), r: 6 }));
        label = `${num(dist2(last, c))} mm  ${num(deg(Math.atan2(c.y - last.y, c.x - last.x)), 1)}°`;
      } else {
        const e = draft(cursor);
        const pts = e ? outlineOf(e) : [];
        if (pts.length >= 3) out.push(el('path', { class: 'fs-preview', d: pathD(pts, true) }));
        if (tool === 'slot' && clicks.length === 1) {
          out.push(el('path', { class: 'fs-preview-line', d: pathD([clicks[0], c], false) }));
          label = `${num(dist2(clicks[0], c))} mm  ${num(deg(Math.atan2(c.y - clicks[0].y, c.x - clicks[0].x)), 1)}°`;
        } else {
          if (tool !== 'rect' && tool !== 'slot') out.push(el('path', { class: 'fs-preview-close', d: pathD([clicks[0], c], false) }));
          if (e) label = previewText(e);
        }
      }
      if (label) {
        const s = toScreen(c);
        out.push(el('text', { class: 'fs-preview-label', x: fmt(s.x + 14), y: fmt(s.y - 14) }, label));
      }
      return out;
    };

    const cursorLayer = (): SVGElement[] => {
      const out = clicks.map((p) => {
        const s = toScreen(p);
        return el('circle', { class: 'fs-click', cx: fmt(s.x), cy: fmt(s.y), r: 3.5 });
      });
      const dragging = gesture?.kind === 'drag' && gesture.moved;
      if (!cursor || (tool === 'select' && !dragging)) return out;
      const s = toScreen(cursor.p);
      if (cursor.hit) out.push(el('rect', { class: 'fs-snap', x: fmt(s.x - 6), y: fmt(s.y - 6), width: 12, height: 12 }));
      if (tool !== 'select') out.push(el('circle', { class: 'fs-cursor', cx: fmt(s.x), cy: fmt(s.y), r: 2.5 }));
      return out;
    };

    const render = (): void => {
      const w = canvas.clientWidth || 800;
      const hh = canvas.clientHeight || 500;
      svg.setAttribute('viewBox', `0 0 ${w} ${hh}`);
      svg.replaceChildren(defs(), ...gridLayer(w, hh), ...faceLayer(), ...axesLayer(), ...entityLayer(), ...previewLayer(), ...cursorLayer());
      readout.hidden = !cursor;
      if (cursor) readout.textContent = `X ${num(cursor.p.x)}   Y ${num(cursor.p.y)}`;
    };

    const fit = (): void => {
      const w = canvas.clientWidth || 800;
      const hh = canvas.clientHeight || 500;
      const pts = [...ctx.outline, ...entities.flatMap(outlineOf)];
      const b = pts.length ? bbox2(pts) : { minX: -10, minY: -10, maxX: 110, maxY: 80 };
      const bw = Math.max(b.maxX - b.minX, 20);
      const bh = Math.max(b.maxY - b.minY, 20);
      scale = clampScale(Math.min(Math.max(w - 80, 40) / bw, Math.max(hh - 80, 40) / bh));
      ox = w / 2 - ((b.minX + b.maxX) / 2) * scale;
      oy = hh / 2 + ((b.minY + b.maxY) / 2) * scale;
      render();
    };

    // ------------------------------------------------------------ pointer input

    /** Entity under a screen point: outlines and key points win over interiors, then the smallest shape. */
    const pick = (s: V2): SketchEntity | null => {
      const m = toModel(s);
      let best: SketchEntity | null = null;
      let bestScore = Infinity;
      for (const e of entities) {
        const pts = outlineOf(e);
        let d = Infinity;
        for (let i = 0; i < pts.length; i++) d = Math.min(d, distPointSegment(m, pts[i], pts[(i + 1) % pts.length]));
        for (const k of keyPoints(e)) d = Math.min(d, dist2(m, k));
        const px = d * scale;
        const score = px < 8 ? px : pts.length >= 3 && pointInPolygon(m, pts) ? 100 + Math.abs(signedArea(pts)) : Infinity;
        if (score < bestScore) {
          bestScore = score;
          best = e;
        }
      }
      return best;
    };

    const screenPos = (e: MouseEvent): V2 => {
      const r = svg.getBoundingClientRect();
      return v2(e.clientX - r.left, e.clientY - r.top);
    };

    const startPinch = (): void => {
      const [a, b] = [...pointers.values()];
      cancelDrag();
      gesture = { kind: 'pinch', d0: Math.max(dist2(a, b), 1), s0: scale, m: toModel(lerp2(a, b, 0.5)) };
      render();
    };

    svg.addEventListener('pointerdown', (e) => {
      if (e.button > 2 || performance.now() - openedAt < OPEN_GRACE_MS) return;
      svg.setPointerCapture(e.pointerId);
      const s = screenPos(e);
      pointers.set(e.pointerId, s);
      shift = e.shiftKey;
      lastScreen = s;
      if (pointers.size === 2) {
        startPinch();
        return;
      }
      if (pointers.size > 2) return;
      if (e.button === 1 || e.button === 2) {
        cancelDrag();
        gesture = { kind: 'pan', x: s.x, y: s.y, ox, oy };
        return;
      }
      if (tool === 'select') {
        const hit = pick(s);
        select(hit ? hit.id : null, true);
        if (!hit) {
          gesture = { kind: 'pan', x: s.x, y: s.y, ox, oy };
          svg.style.cursor = 'grabbing';
          return;
        }
        const m = toModel(s);
        const keys = keyPoints(hit);
        const grab = keys.reduce((g, k) => (dist2(k, m) < dist2(g, m) ? k : g), keys[0] ?? m);
        gesture = { kind: 'drag', e: hit, orig: cloneEntity(hit), grab: { ...grab }, down: m, sx: s.x, sy: s.y, moved: false };
        return;
      }
      cursor = snapAt(s);
      gesture = { kind: 'tap', sx: s.x, sy: s.y, at: cursor };
      render();
    });

    svg.addEventListener('pointermove', (e) => {
      const s = screenPos(e);
      if (pointers.has(e.pointerId)) pointers.set(e.pointerId, s);
      shift = e.shiftKey;
      lastScreen = s;
      const g = gesture;
      if (g?.kind === 'pinch') {
        if (pointers.size < 2) return;
        const [a, b] = [...pointers.values()];
        const mid = lerp2(a, b, 0.5);
        scale = clampScale((g.s0 * dist2(a, b)) / g.d0);
        ox = mid.x - g.m.x * scale;
        oy = mid.y + g.m.y * scale;
        userView = true;
        render();
        return;
      }
      if (g?.kind === 'pan') {
        ox = g.ox + s.x - g.x;
        oy = g.oy + s.y - g.y;
        userView = true;
        render();
        return;
      }
      if (g?.kind === 'drag') {
        if (!g.moved && Math.hypot(s.x - g.sx, s.y - g.sy) < 3) return;
        g.moved = true;
        // the grabbed key point follows the pointer and is the point that snaps
        cursor = snap(add2(g.grab, sub2(toModel(s), g.down)), null, g.e.id);
        Object.assign(g.e, moveEntity(g.orig, sub2(cursor.p, g.grab)));
        render();
        return;
      }
      if (tool === 'select') {
        cursor = { p: toModel(s), hit: false };
        svg.style.cursor = pick(s) ? 'move' : 'default';
      } else cursor = snapAt(s);
      render();
    });

    const endPointer = (e: PointerEvent, commit: boolean): void => {
      pointers.delete(e.pointerId);
      const g = gesture;
      if (g?.kind === 'pinch') {
        if (pointers.size < 2) gesture = null;
        return;
      }
      gesture = null;
      if (tool === 'select') svg.style.cursor = 'default';
      if (g?.kind === 'drag' && g.moved) {
        cards.get(g.e.id)?.refresh();
        changed(g.e);
        return;
      }
      if (!commit || (g?.kind !== 'tap' && g?.kind !== 'drag')) return;
      const s = screenPos(e);
      // the svg content is rebuilt between the clicks, so the browser's dblclick never arrives
      const now = performance.now();
      const double = lastTap !== null && now - lastTap.t < 450 && dist2(s, lastTap.s) < 8;
      lastTap = double ? null : { s, t: now };
      if (g.kind === 'drag') {
        if (double) cards.get(g.e.id)?.card.querySelector('input')?.focus();
        return;
      }
      if (double && tool === 'polyline' && clicks.length >= 3) {
        closePolyline();
        return;
      }
      // press, drag and release draws a whole shape in one stroke (handy on touch screens)
      if (!clicks.length && Math.hypot(s.x - g.sx, s.y - g.sy) > 6) place(g.at);
      cursor = snapAt(s);
      place(cursor);
      render();
    };
    svg.addEventListener('pointerup', (e) => endPointer(e, true));
    svg.addEventListener('pointercancel', (e) => endPointer(e, false));
    svg.addEventListener('pointerleave', () => {
      if (gesture) return;
      cursor = null;
      lastScreen = null;
      render();
    });
    svg.addEventListener('contextmenu', (e) => e.preventDefault());
    svg.addEventListener(
      'wheel',
      (e) => {
        e.preventDefault();
        const s = screenPos(e);
        const before = toModel(s);
        const dy = e.deltaMode === 1 ? e.deltaY * 33 : e.deltaY;
        scale = clampScale(scale * Math.exp(-dy * 0.0015));
        ox = s.x - before.x * scale;
        oy = s.y + before.y * scale;
        userView = true;
        if (tool !== 'select' && !gesture) cursor = snapAt(s);
        render();
      },
      { passive: false },
    );

    // ------------------------------------------------------------ keyboard

    const reSnap = (): void => {
      if (!lastScreen || tool === 'select' || gesture) return;
      cursor = snapAt(lastScreen);
      render();
    };

    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Shift') {
        shift = true;
        reSnap();
        return;
      }
      const editable = isEditable(e.target);
      if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        // leave the field first (it keeps its last valid value), then abort, then close
        if (editable) modal.focus();
        else if (gesture || clicks.length) abortAction();
        else if (!e.repeat) finish(null);
        return;
      }
      if (editable) return;
      if (e.key === 'Backspace' && tool === 'polyline' && clicks.length) {
        e.preventDefault();
        clicks.pop();
        renderHint();
        render();
      } else if ((e.key === 'Delete' || (e.key === 'Backspace' && tool === 'select')) && selected && !e.repeat) {
        e.preventDefault();
        remove(selected);
      } else if (e.key === 'Enter' && !(e.target instanceof HTMLButtonElement)) {
        if (tool === 'slot' && clicks.length === 2) {
          e.preventDefault();
          addDefaultSlot();
        } else if (tool === 'polyline' && clicks.length >= 3) {
          e.preventDefault();
          closePolyline();
        }
      }
    };
    const onKeyUp = (e: KeyboardEvent): void => {
      if (e.key !== 'Shift') return;
      shift = false;
      reSnap();
    };
    window.addEventListener('keydown', onKey, true);
    window.addEventListener('keyup', onKeyUp, true);

    // ------------------------------------------------------------ dialog

    const accept = (): void => {
      // finish what can be finished instead of silently dropping it
      if (tool === 'polyline' && clicks.length >= 3) closePolyline();
      else if (tool === 'slot' && clicks.length === 2) addDefaultSlot();
      if (gesture || clicks.length) abortAction();
      showErrors = true;
      revalidate();
      if (!validate()) {
        finish(entities.map(cloneEntity));
        return;
      }
      const bad = entities.find((e) => problemOf(e));
      if (bad) select(bad.id, true);
      msgBox.scrollIntoView({ block: 'nearest' });
    };

    const modal = h(
      'div',
      { class: 'modal fs-modal', role: 'dialog', 'aria-modal': 'true', 'aria-label': ctx.title, tabindex: '-1' },
      h(
        'div',
        { class: 'modal-head' },
        h('h2', {}, ctx.title),
        h('button', { type: 'button', class: 'btn', onclick: () => ((userView = false), fit()) }, svgIcon(ICONS.fit), 'Einpassen'),
        h('button', { type: 'button', class: 'btn', onclick: () => finish(null) }, svgIcon(ICONS.close), 'Abbrechen'),
        h('button', { type: 'button', class: 'btn primary', onclick: accept }, svgIcon(ICONS.check), 'Übernehmen'),
      ),
      h('div', { class: 'sk-body' }, canvas, side),
    );
    // keep the view fitted while the dialog settles or resizes, until the user zooms or pans
    const resizer = new ResizeObserver(() => (userView ? render() : fit()));

    document.body.appendChild(modal);
    svg.style.cursor = tool === 'select' ? 'default' : '';
    renderTools();
    renderList();
    fit();
    modal.focus();
    resizer.observe(canvas);
  });
}
