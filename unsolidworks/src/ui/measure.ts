// Measure tool ("Messen"): turns viewer hits into measure entities and builds
// the side panel. The geometry lives in kernel/measure.ts.
//
// Wiring in the app:
//   viewer.setPickMode('measure', [...edges, ...corners]);
//   onPick(hit): const p = pickToMeasure(hit, part, flat);
//     a pick whose key is already in the list toggles it off; otherwise append
//     it, starting over once MAX_MEASURE_PICKS are kept or when a third pick
//     would not add up with the pair (see addsUp)
//   const result = measure(picks.map((p) => p.entity));
//   viewer.setMeasureSelection(picks.map((p) => p.view));
//   viewer.setMeasureOverlay(result.lines);
//   side.replaceChildren(measurePanel(picks, result, { remove, clear, close }));
// Leaving the tool: setMeasureSelection([]) and setMeasureOverlay([]).

import './measure.css';
import { MeasureEntity, MeasureResult, MeasureRow, bendEntity, edgeEntity, faceEntity, pointEntity } from '../kernel/measure';
import type { Part } from '../kernel/model';
import type { V3 } from '../kernel/vec';
import { h, svgIcon } from './dom';
import { num } from './format';
import { ICONS } from './icons';
import type { Hit, MeasureSelectionView } from './viewer';

/** Picks kept at once; the next click after that starts a new measurement. */
export const MAX_MEASURE_PICKS = 3;

/**
 * Whether three or more entities give a result together: summed edge
 * lengths, face areas or a point chain. Anything else is measured as a pair,
 * so a further pick would have no visible effect.
 */
export function addsUp(entities: MeasureEntity[]): boolean {
  // flat faces and curved ones (bends, hole walls) add up to a total area
  const group = (e: MeasureEntity): string => (e.kind === 'cylinder' ? 'plane' : e.kind);
  const kind = entities[0] && group(entities[0]);
  return (kind === 'edge' || kind === 'plane' || kind === 'point') && entities.every((e) => group(e) === kind);
}

export const MEASURE_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
  <path d="M2.5 5.5h19v3h-19z" fill="currentColor" fill-opacity=".14"/>
  <path d="M4.5 8.5v7l3 4v-11"/>
  <path d="M11 4h6v6h-6z"/>
  <path d="M14 10v5.5l-3 4V10"/>
  <path d="M19.5 5.5v1.5M7.5 5.5v1.5M9.5 5.5v1"/></svg>`;

export interface MeasurePick {
  /** Identifies the picked thing: picking it again yields the same key. */
  key: string;
  entity: MeasureEntity;
  view: MeasureSelectionView;
}

const round = (v: number): number => Math.round(v * 1000) / 1000;
const r3 = (p: V3): string => `${round(p.x)},${round(p.y)},${round(p.z)}`;

/** Geometric identity of an entity, so the same thing hit at another spot keeps its key. */
function entityKey(e: MeasureEntity): string {
  switch (e.kind) {
    case 'point':
      return r3(e.p);
    case 'edge':
      // a round hole is one edge, whichever of its segments was hit
      return e.circle ? `${r3(e.circle.center)}:${round(e.circle.r)}` : [r3(e.a), r3(e.b)].sort().join('|');
    case 'plane':
      return `${r3(e.origin)}:${r3(e.normal)}`;
    case 'cylinder':
      return `${r3(e.axisPoint)}:${round(e.radius)}`;
  }
}

/**
 * Measure entity for a viewer hit. Throws an Error with a German message
 * when the picked piece no longer belongs to the part.
 */
export function pickToMeasure(hit: Hit, part: Part, flat: boolean): MeasurePick | null {
  switch (hit.kind) {
    case 'vertex': {
      const entity = pointEntity(hit.p, 'Punkt');
      return { key: `v:${entityKey(entity)}`, entity, view: { kind: 'vertex', p: hit.p } };
    }
    case 'edge': {
      const entity = edgeEntity(part, hit.edge, hit.side, flat);
      return { key: `e:${entityKey(entity)}`, entity, view: { kind: 'edge', edge: hit.edge, side: hit.side } };
    }
    case 'face': {
      const entity =
        hit.pieceKind === 'face'
          ? faceEntity(part, hit.id, hit.surface, hit.point, flat)
          : bendEntity(part, hit.id, hit.surface, hit.point, flat);
      return {
        key: `f:${hit.pieceKind}:${hit.id}:${hit.surface}:${entityKey(entity)}`,
        entity,
        view: { kind: 'face', pieceKind: hit.pieceKind, id: hit.id },
      };
    }
  }
}

function valueText(r: MeasureRow): string {
  if (r.unit === '°') return `${num(r.value)}°`;
  return r.unit ? `${num(r.value)} ${r.unit}` : num(r.value);
}

function pickList(picks: MeasurePick[], remove: (i: number) => void): HTMLElement {
  if (picks.length === 0) return h('div', { class: 'picklist empty' }, 'Punkte, Kanten oder Flächen im 3D-Fenster anklicken');
  return h(
    'div',
    { class: 'picklist' },
    ...picks.map((p, i) =>
      h(
        'div',
        { class: 'chip' },
        h('span', { class: 'ms-pick' }, h('span', { class: 'ms-idx' }, String(i + 1)), h('span', { class: 'ms-name' }, p.entity.label)),
        h(
          'button',
          {
            type: 'button',
            class: 'iconbtn',
            title: 'Aus der Auswahl entfernen',
            'aria-label': `${p.entity.label} entfernen`,
            onclick: () => remove(i),
          },
          svgIcon(ICONS.close),
        ),
      ),
    ),
  );
}

function resultSection(result: MeasureResult | null): HTMLElement {
  const rows = result?.rows ?? [];
  return h(
    'div',
    { class: 'section' },
    h('h3', {}, 'Ergebnis'),
    rows.length
      ? h(
          'dl',
          { class: 'kv ms-result' },
          // the first row is the headline value (distance, length, angle …)
          ...rows.flatMap((r, i) => [h('dt', { class: i === 0 ? 'ms-main' : undefined }, r.label), h('dd', { class: i === 0 ? 'ms-main' : undefined }, valueText(r))]),
        )
      : h('p', { class: 'note' }, 'Für diese Auswahl gibt es keinen Messwert. Ein Element entfernen oder andere Elemente wählen.'),
  );
}

export function measurePanel(
  picks: MeasurePick[],
  result: MeasureResult | null,
  actions: { remove(i: number): void; clear(): void; close(): void },
): HTMLElement {
  const closeBtn = h(
    'button',
    { type: 'button', class: 'pm-cancel', title: 'Messen beenden (Esc)', 'aria-label': 'Messen beenden', onclick: () => actions.close() },
    svgIcon(ICONS.close),
  );
  return h(
    'div',
    { class: 'pm' },
    h('div', { class: 'pm-head' }, svgIcon(MEASURE_ICON, 'ficon'), h('div', { class: 'pm-title' }, 'Messen'), closeBtn),
    h(
      'div',
      { class: 'pm-body' },
      h('div', { class: 'section' }, h('h3', {}, 'Auswahl'), pickList(picks, actions.remove)),
      picks.length ? resultSection(result) : null,
      h(
        'div',
        { class: 'btnrow' },
        h('button', { type: 'button', class: 'btn', disabled: picks.length === 0, onclick: () => actions.clear() }, svgIcon(ICONS.trash), 'Auswahl leeren'),
      ),
      h(
        'p',
        { class: 'note' },
        `Ein Element allein zeigt Länge, Durchmesser oder Fläche, zwei Elemente Abstand und Winkel. Bis zu ${MAX_MEASURE_PICKS} Kanten, Flächen oder Punkte ergeben eine Summe. Sonst beginnt der nächste Klick eine neue Messung. Erneuter Klick auf ein Element entfernt es.`,
      ),
    ),
  );
}
