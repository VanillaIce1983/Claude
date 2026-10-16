// Side panel content for the flat pattern: stock size, mass, bend table and drawing.

import { flatToSvg } from '../kernel/export/svg';
import type { FlatPattern } from '../kernel/flat';
import type { Feature } from '../kernel/features/types';
import type { SheetSettings } from '../kernel/sheet';
import { h, svgIcon } from './dom';
import { num } from './format';
import { ICONS } from './icons';

export interface FlatPanelInput {
  fp: FlatPattern;
  sheet: SheetSettings;
  volume: number;
  features: Feature[];
  css: (name: string) => string;
  onExport: (kind: 'dxf' | 'svg') => void;
}

export function flatPanel(inp: FlatPanelInput): HTMLElement {
  const { fp, sheet } = inp;
  const w = fp.bounds.maxX - fp.bounds.minX;
  const hgt = fp.bounds.maxY - fp.bounds.minY;
  const massKg = (inp.volume / 1000) * sheet.material.density / 1000;
  const names = new Map(inp.features.map((f) => [f.id, f.name]));
  const svgMarkup = flatToSvg(fp, {
    outline: inp.css('--fg'),
    up: inp.css('--bend-up'),
    down: inp.css('--bend-down'),
    text: inp.css('--muted'),
    fill: inp.css('--panel'),
  });
  const drawing = h('div', { class: 'flatsvg', html: svgMarkup });
  const svgEl = drawing.querySelector('svg');
  if (svgEl) {
    svgEl.removeAttribute('width');
    svgEl.removeAttribute('height');
    svgEl.setAttribute('role', 'img');
    svgEl.setAttribute('aria-label', 'Zeichnung der Abwicklung mit Biegelinien');
  }

  const rows = fp.bendLines.map((b, i) =>
    h(
      'tr',
      {},
      h('td', {}, `${i + 1}`),
      h('td', { class: b.up ? 'dir-up' : 'dir-down' }, b.up ? 'Oben' : 'Unten'),
      h('td', {}, `${num(b.angleDeg, 1)}°`),
      h('td', {}, num(b.radius)),
      h('td', {}, num(b.ba, 3)),
      h('td', {}, Number.isFinite(b.bd) ? num(b.bd, 3) : '–'),
      h('td', {}, num(b.length)),
      h('td', { title: b.calc }, names.get(b.feature) ?? ''),
    ),
  );

  return h(
    'div',
    { class: 'pm-body' },
    h(
      'div',
      { class: 'section' },
      h('h3', {}, 'Zuschnitt'),
      h(
        'dl',
        { class: 'kv' },
        h('dt', {}, 'Abmessung (achsparallel)'),
        h('dd', {}, `${num(w)} × ${num(hgt)} mm`),
        h('dt', {}, 'Kleinstes Rechteck'),
        h('dd', {}, `${num(fp.minRect.w)} × ${num(fp.minRect.h)} mm`),
        h('dt', {}, 'Blechdicke'),
        h('dd', {}, `${num(sheet.thickness)} mm`),
        h('dt', {}, 'Fläche'),
        h('dd', {}, `${num(fp.area / 100, 1)} cm²`),
        h('dt', {}, 'Schnittlänge'),
        h('dd', {}, `${num(fp.cutLength, 0)} mm`),
        h('dt', {}, `Masse (${sheet.material.name})`),
        h('dd', {}, `${num(massKg, 3)} kg`),
        h('dt', {}, 'Biegungen'),
        h('dd', {}, String(fp.bendLines.length)),
      ),
    ),
    h('div', { class: 'section' }, h('h3', {}, 'Zeichnung'), drawing),
    h(
      'div',
      { class: 'section' },
      h('h3', {}, 'Biegetabelle'),
      fp.bendLines.length
        ? h(
            'div',
            { class: 'tablewrap' },
            h(
              'table',
              { class: 'bends' },
              h(
                'thead',
                {},
                h('tr', {}, h('th', {}, '#'), h('th', {}, 'Richtung'), h('th', {}, 'Winkel'), h('th', {}, 'R'), h('th', {}, 'BZ'), h('th', {}, 'BA'), h('th', {}, 'Länge'), h('th', {}, 'Feature')),
              ),
              h('tbody', {}, ...rows),
            ),
          )
        : h('p', { class: 'note' }, 'Das Teil hat keine Biegungen.'),
      h('p', { class: 'note' }, 'BZ = Biegezugabe, BA = Biegeabzug (je mm). Richtung „Oben“ biegt zum Betrachter, wenn die Abwicklung von oben gesehen wird.'),
    ),
    h(
      'div',
      { class: 'section' },
      h('h3', {}, 'Export'),
      h(
        'div',
        { class: 'btnrow' },
        h('button', { type: 'button', class: 'btn primary', onclick: () => inp.onExport('dxf') }, svgIcon(ICONS.save), 'DXF für Laser'),
        h('button', { type: 'button', class: 'btn', onclick: () => inp.onExport('svg') }, svgIcon(ICONS.save), 'SVG'),
      ),
      h('p', { class: 'note' }, 'DXF R12 in mm: Kontur auf Layer KONTUR, Biegelinien auf BIEGUNG_OBEN und BIEGUNG_UNTEN, Biegenotizen auf BIEGENOTIZEN.'),
    ),
  );
}
