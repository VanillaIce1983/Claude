// Profile sketch editor for the base flange: polyline and rectangle tools,
// grid and point snapping, dragging points, and an editable coordinate table.

import { isSimple } from '../kernel/polygon';
import { V2, dist2, v2 } from '../kernel/vec';
import { h, svgIcon } from './dom';
import { inputValue, num, parseNumber } from './format';
import { ICONS } from './icons';

export interface SketchResult {
  points: V2[];
  closed: boolean;
}

type Tool = 'line' | 'rect' | 'select';

export const PRESETS: { name: string; closed: boolean; points: V2[]; icon: string }[] = [
  {
    name: 'Rechteck',
    closed: true,
    points: [v2(0, 0), v2(200, 0), v2(200, 120), v2(0, 120)],
    icon: '<svg viewBox="0 0 56 40" fill="none" stroke="currentColor" stroke-width="2"><rect x="8" y="8" width="40" height="24"/></svg>',
  },
  {
    name: 'L-Winkel',
    closed: false,
    points: [v2(0, 60), v2(0, 0), v2(100, 0)],
    icon: '<svg viewBox="0 0 56 40" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M12 6v26h34"/></svg>',
  },
  {
    name: 'U-Profil',
    closed: false,
    points: [v2(0, 50), v2(0, 0), v2(120, 0), v2(120, 50)],
    icon: '<svg viewBox="0 0 56 40" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M10 8v24h36V8"/></svg>',
  },
  {
    name: 'Z-Profil',
    closed: false,
    points: [v2(0, 0), v2(40, 0), v2(40, 50), v2(80, 50)],
    icon: '<svg viewBox="0 0 56 40" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M6 32h20V8h24"/></svg>',
  },
  {
    name: 'Hutprofil',
    closed: false,
    points: [v2(0, 0), v2(25, 0), v2(25, 40), v2(95, 40), v2(95, 0), v2(120, 0)],
    icon: '<svg viewBox="0 0 56 40" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M4 32h10V10h28v22h10"/></svg>',
  },
];

const SNAPS: [string, number][] = [
  ['0,5 mm', 0.5],
  ['1 mm', 1],
  ['5 mm', 5],
  ['10 mm', 10],
  ['Aus', 0],
];

export function openSketcher(init: SketchResult | null): Promise<SketchResult | null> {
  return new Promise((resolve) => {
    let pts: V2[] = init ? init.points.map((p) => ({ ...p })) : [];
    let closed = init ? init.closed : true;
    let tool: Tool = pts.length ? 'select' : 'line';
    let drawing = false;
    let rectStart: V2 | null = null;
    let cursor: V2 | null = null;
    let selected = -1;
    let dragging = -1;
    let panning: { x: number; y: number; ox: number; oy: number } | null = null;
    let snapStep = 1;
    let scale = 2;
    let ox = 100;
    let oy = 400;
    let message = '';

    const NS = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('role', 'img');
    svg.setAttribute('aria-label', 'Skizzenfläche');
    const canvas = h('div', { class: 'sk-canvas' });
    canvas.appendChild(svg);
    const side = h('div', { class: 'sk-side' });
    const msgBox = h('div', { class: 'errbox', hidden: true });
    const tableBox = h('div', {});
    const closedCb = h('input', { type: 'checkbox', id: 'sk-closed', checked: closed });

    const toScreen = (p: V2): V2 => v2(ox + p.x * scale, oy - p.y * scale);
    const toModel = (x: number, y: number): V2 => v2((x - ox) / scale, (oy - y) / scale);

    const finish = (result: SketchResult | null): void => {
      window.removeEventListener('keydown', onKey, true);
      modal.remove();
      resolve(result);
    };

    const validate = (): string => {
      if (closed) {
        if (pts.length < 3) return 'Ein geschlossenes Profil braucht mindestens drei Punkte.';
        if (!isSimple(pts)) return 'Das Profil überschneidet sich oder hat doppelte Punkte.';
      } else {
        if (pts.length < 2) return 'Ein offenes Profil braucht mindestens zwei Punkte.';
        for (let i = 0; i + 1 < pts.length; i++) if (dist2(pts[i], pts[i + 1]) < 1e-6) return `Segment ${i + 1} hat die Länge 0.`;
      }
      return '';
    };

    const snap = (raw: V2, shift: boolean): V2 => {
      // existing points first
      for (const p of pts) {
        const s = toScreen(p);
        const r = toScreen(raw);
        if (Math.hypot(s.x - r.x, s.y - r.y) < 9) return { ...p };
      }
      let p = raw;
      const last = drawing && pts.length ? pts[pts.length - 1] : rectStart;
      if (shift && last) {
        const ang = Math.atan2(p.y - last.y, p.x - last.x);
        const a = Math.round(ang / (Math.PI / 12)) * (Math.PI / 12);
        const len = dist2(p, last);
        p = v2(last.x + Math.cos(a) * len, last.y + Math.sin(a) * len);
        if (snapStep > 0) {
          const l = Math.round(len / snapStep) * snapStep;
          p = v2(last.x + Math.cos(a) * l, last.y + Math.sin(a) * l);
        }
        return p;
      }
      if (snapStep > 0) p = v2(Math.round(p.x / snapStep) * snapStep, Math.round(p.y / snapStep) * snapStep);
      return p;
    };

    const fit = (): void => {
      const w = canvas.clientWidth || 800;
      const hgt = canvas.clientHeight || 500;
      const src = pts.length >= 2 ? pts : [v2(-10, -10), v2(210, 130)];
      let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
      for (const p of src) {
        minX = Math.min(minX, p.x);
        minY = Math.min(minY, p.y);
        maxX = Math.max(maxX, p.x);
        maxY = Math.max(maxY, p.y);
      }
      const bw = Math.max(maxX - minX, 20);
      const bh = Math.max(maxY - minY, 20);
      scale = Math.min((w - 80) / bw, (hgt - 80) / bh);
      ox = w / 2 - ((minX + maxX) / 2) * scale;
      oy = hgt / 2 + ((minY + maxY) / 2) * scale;
      render();
    };

    const el = (tag: string, attrs: Record<string, string | number>, text?: string): SVGElement => {
      const e = document.createElementNS(NS, tag);
      for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, String(v));
      if (text !== undefined) e.textContent = text;
      return e;
    };

    const render = (): void => {
      const w = canvas.clientWidth || 800;
      const hgt = canvas.clientHeight || 500;
      svg.setAttribute('viewBox', `0 0 ${w} ${hgt}`);
      svg.replaceChildren();
      // grid
      const steps = [0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];
      const g = steps.find((s) => s * scale >= 10) ?? 1000;
      const major = g * 5;
      const tl = toModel(0, 0);
      const br = toModel(w, hgt);
      const grid = el('g', { stroke: 'var(--line)', 'stroke-width': 1 });
      for (let x = Math.floor(tl.x / g) * g; x <= br.x; x += g) {
        const sx = toScreen(v2(x, 0)).x;
        grid.appendChild(el('line', { x1: sx, y1: 0, x2: sx, y2: hgt, 'stroke-opacity': Math.abs(x / major - Math.round(x / major)) < 1e-6 ? 0.9 : 0.35 }));
      }
      for (let y = Math.floor(br.y / g) * g; y <= tl.y; y += g) {
        const sy = toScreen(v2(0, y)).y;
        grid.appendChild(el('line', { x1: 0, y1: sy, x2: w, y2: sy, 'stroke-opacity': Math.abs(y / major - Math.round(y / major)) < 1e-6 ? 0.9 : 0.35 }));
      }
      svg.appendChild(grid);
      const o = toScreen(v2(0, 0));
      svg.appendChild(el('line', { x1: o.x, y1: 0, x2: o.x, y2: hgt, stroke: 'var(--bend-up)', 'stroke-width': 1.2 }));
      svg.appendChild(el('line', { x1: 0, y1: o.y, x2: w, y2: o.y, stroke: 'var(--bend-down)', 'stroke-width': 1.2 }));

      // profile
      const sp = pts.map(toScreen);
      if (sp.length >= 2) {
        const d = 'M' + sp.map((p) => `${p.x} ${p.y}`).join('L') + (closed ? 'Z' : '');
        svg.appendChild(
          el('path', {
            d,
            fill: closed ? 'var(--accent)' : 'none',
            'fill-opacity': 0.12,
            stroke: 'var(--fg)',
            'stroke-width': 2,
            'stroke-linejoin': 'round',
          }),
        );
      }
      // segment lengths
      const segCount = closed ? pts.length : pts.length - 1;
      for (let i = 0; i < segCount && pts.length >= 2; i++) {
        const a = pts[i];
        const b = pts[(i + 1) % pts.length];
        const L = dist2(a, b);
        if (L * scale < 30) continue;
        const sa = toScreen(a);
        const sb = toScreen(b);
        let ang = (Math.atan2(sb.y - sa.y, sb.x - sa.x) * 180) / Math.PI;
        if (ang > 90) ang -= 180;
        if (ang < -90) ang += 180;
        const mx = (sa.x + sb.x) / 2;
        const my = (sa.y + sb.y) / 2;
        const t = el('text', { x: mx, y: my - 6, 'text-anchor': 'middle', 'font-size': 12, fill: 'var(--muted)', transform: `rotate(${ang} ${mx} ${my})` }, `${num(L)}`);
        svg.appendChild(t);
      }
      // rubber band
      if (cursor && ((tool === 'line' && drawing && pts.length) || (tool === 'rect' && rectStart))) {
        const from = tool === 'rect' ? rectStart! : pts[pts.length - 1];
        const a = toScreen(from);
        const b = toScreen(cursor);
        if (tool === 'rect') {
          svg.appendChild(
            el('rect', { x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), width: Math.abs(b.x - a.x), height: Math.abs(b.y - a.y), fill: 'none', stroke: 'var(--sel)', 'stroke-dasharray': '5 4', 'stroke-width': 1.5 }),
          );
          svg.appendChild(el('text', { x: b.x + 10, y: b.y - 10, 'font-size': 12, fill: 'var(--sel)' }, `${num(Math.abs(cursor.x - from.x))} × ${num(Math.abs(cursor.y - from.y))}`));
        } else {
          svg.appendChild(el('line', { x1: a.x, y1: a.y, x2: b.x, y2: b.y, stroke: 'var(--sel)', 'stroke-dasharray': '5 4', 'stroke-width': 1.5 }));
          const ang = (Math.atan2(cursor.y - from.y, cursor.x - from.x) * 180) / Math.PI;
          svg.appendChild(el('text', { x: b.x + 10, y: b.y - 10, 'font-size': 12, fill: 'var(--sel)' }, `${num(dist2(from, cursor))} mm  ${num(ang, 1)}°`));
        }
      }
      // points
      pts.forEach((p, i) => {
        const s = toScreen(p);
        svg.appendChild(
          el('circle', {
            cx: s.x,
            cy: s.y,
            r: i === selected ? 6 : 4.5,
            fill: i === selected ? 'var(--accent)' : i === 0 ? 'var(--sel)' : 'var(--panel)',
            stroke: i === 0 ? 'var(--sel)' : 'var(--fg)',
            'stroke-width': 1.5,
          }),
        );
      });
      if (cursor) {
        const c = toScreen(cursor);
        svg.appendChild(el('circle', { cx: c.x, cy: c.y, r: 3, fill: 'var(--sel)' }));
        svg.appendChild(el('text', { x: 10, y: hgt - 12, 'font-size': 12, fill: 'var(--muted)' }, `X ${num(cursor.x)}   Y ${num(cursor.y)}`));
      }
      const err = validate();
      message = err;
    };

    const renderTable = (): void => {
      const rows: HTMLElement[] = [h('span', { class: 'h' }, '#'), h('span', { class: 'h' }, 'X (mm)'), h('span', { class: 'h' }, 'Y (mm)'), h('span', {})];
      pts.forEach((p, i) => {
        const mk = (axis: 'x' | 'y'): HTMLElement => {
          const input = h('input', { id: `sk-${axis}${i}`, type: 'text', inputmode: 'decimal', value: inputValue(p[axis], 3), 'aria-label': `Punkt ${i + 1} ${axis.toUpperCase()}` });
          input.addEventListener('change', () => {
            const v = parseNumber(input.value);
            if (v === null) {
              input.value = inputValue(p[axis], 3);
              return;
            }
            pts[i] = { ...pts[i], [axis]: v };
            render();
            updateMsg();
          });
          return h('div', { class: 'num' }, input);
        };
        rows.push(
          h('span', {}, String(i + 1)),
          mk('x'),
          mk('y'),
          h(
            'button',
            {
              type: 'button',
              class: 'iconbtn',
              title: 'Punkt löschen',
              'aria-label': `Punkt ${i + 1} löschen`,
              onclick: () => {
                pts.splice(i, 1);
                selected = -1;
                renderTable();
                render();
                updateMsg();
              },
            },
            svgIcon(ICONS.trash),
          ),
        );
      });
      tableBox.replaceChildren(
        h('div', { class: 'ptable' }, ...rows),
        h(
          'div',
          { class: 'btnrow', style: { marginTop: '8px' } },
          h(
            'button',
            {
              type: 'button',
              class: 'btn',
              onclick: () => {
                const last = pts[pts.length - 1] ?? v2(0, 0);
                pts.push(v2(last.x + 50, last.y));
                renderTable();
                render();
                updateMsg();
              },
            },
            svgIcon(ICONS.plus),
            'Punkt anhängen',
          ),
        ),
      );
    };

    const updateMsg = (): void => {
      msgBox.textContent = message;
      msgBox.hidden = !message;
    };

    const setTool = (t: Tool): void => {
      tool = t;
      drawing = false;
      rectStart = null;
      renderSide();
      render();
    };

    const pointerPos = (e: PointerEvent | WheelEvent): V2 => {
      const r = svg.getBoundingClientRect();
      return v2(e.clientX - r.left, e.clientY - r.top);
    };

    svg.addEventListener('pointerdown', (e) => {
      svg.setPointerCapture(e.pointerId);
      const s = pointerPos(e);
      if (e.button === 1 || e.button === 2) {
        panning = { x: s.x, y: s.y, ox, oy };
        return;
      }
      const m = snap(toModel(s.x, s.y), e.shiftKey);
      if (tool === 'select') {
        const idx = pts.findIndex((p) => {
          const q = toScreen(p);
          return Math.hypot(q.x - s.x, q.y - s.y) < 10;
        });
        selected = idx;
        if (idx >= 0) dragging = idx;
        else panning = { x: s.x, y: s.y, ox, oy };
        render();
        return;
      }
      if (tool === 'rect') {
        if (!rectStart) rectStart = m;
        else {
          const a = rectStart;
          const b = m;
          if (Math.abs(a.x - b.x) > 1e-6 && Math.abs(a.y - b.y) > 1e-6) {
            const x0 = Math.min(a.x, b.x), x1 = Math.max(a.x, b.x), y0 = Math.min(a.y, b.y), y1 = Math.max(a.y, b.y);
            pts = [v2(x0, y0), v2(x1, y0), v2(x1, y1), v2(x0, y1)];
            closed = true;
            closedCb.checked = true;
            rectStart = null;
            tool = 'select';
            renderSide();
            renderTable();
          }
        }
        render();
        updateMsg();
        return;
      }
      // polyline
      if (!drawing) {
        // continue an open profile, otherwise start a new one
        if (closed || pts.length === 0) pts = [];
        closed = false;
        closedCb.checked = false;
        drawing = true;
      }
      if (pts.length >= 3 && dist2(m, pts[0]) < 1e-9) {
        closed = true;
        closedCb.checked = true;
        drawing = false;
        tool = 'select';
        renderSide();
      } else if (!pts.length || dist2(m, pts[pts.length - 1]) > 1e-9) {
        pts.push(m);
      }
      renderTable();
      render();
      updateMsg();
    });
    svg.addEventListener('pointermove', (e) => {
      const s = pointerPos(e);
      if (panning) {
        ox = panning.ox + (s.x - panning.x);
        oy = panning.oy + (s.y - panning.y);
        render();
        return;
      }
      cursor = snap(toModel(s.x, s.y), e.shiftKey);
      if (dragging >= 0) {
        pts[dragging] = cursor;
      }
      render();
    });
    svg.addEventListener('pointerup', () => {
      if (dragging >= 0) {
        dragging = -1;
        renderTable();
        updateMsg();
      }
      panning = null;
    });
    svg.addEventListener('pointerleave', () => {
      cursor = null;
      render();
    });
    svg.addEventListener('dblclick', () => {
      if (tool === 'line' && drawing) {
        drawing = false;
        tool = 'select';
        renderSide();
        render();
        updateMsg();
      }
    });
    svg.addEventListener('contextmenu', (e) => e.preventDefault());
    svg.addEventListener(
      'wheel',
      (e) => {
        e.preventDefault();
        const s = pointerPos(e);
        const before = toModel(s.x, s.y);
        scale = Math.min(200, Math.max(0.05, scale * Math.exp(-e.deltaY * 0.0015)));
        ox = s.x - before.x * scale;
        oy = s.y + before.y * scale;
        render();
      },
      { passive: false },
    );

    const onKey = (e: KeyboardEvent): void => {
      const inInput = (e.target as HTMLElement)?.tagName === 'INPUT';
      if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        if (drawing || rectStart) {
          drawing = false;
          rectStart = null;
          tool = 'select';
          renderSide();
          render();
          updateMsg();
        } else finish(null);
      } else if ((e.key === 'Delete' || e.key === 'Backspace') && !inInput && selected >= 0) {
        e.preventDefault();
        pts.splice(selected, 1);
        selected = -1;
        renderTable();
        render();
        updateMsg();
      } else if (e.key === 'Enter' && !inInput && drawing) {
        drawing = false;
        tool = 'select';
        renderSide();
        render();
        updateMsg();
      }
    };
    window.addEventListener('keydown', onKey, true);

    closedCb.addEventListener('change', () => {
      closed = closedCb.checked;
      drawing = false;
      render();
      updateMsg();
    });

    const toolBtn = (t: Tool, label: string, icon: string): HTMLElement =>
      h('button', { type: 'button', class: `tb small${tool === t ? ' on' : ''}`, onclick: () => setTool(t), 'aria-pressed': tool === t ? 'true' : 'false' }, svgIcon(icon), label);

    const snapSel = h('select', { id: 'sk-snap' }, ...SNAPS.map(([l, v]) => h('option', { value: String(v), selected: v === snapStep }, l)));
    snapSel.addEventListener('change', () => (snapStep = Number(snapSel.value)));

    const renderSide = (): void => {
      side.replaceChildren(
        h('div', { class: 'section' }, h('h3', {}, 'Werkzeug'), h('div', { class: 'btnrow' }, toolBtn('line', 'Linienzug', ICONS.sketch), toolBtn('rect', 'Rechteck', ICONS.rect), toolBtn('select', 'Auswahl', ICONS.pointer))),
        h(
          'p',
          { class: 'note' },
          tool === 'line'
            ? 'Klicken setzt Punkte. Klick auf den ersten Punkt schließt das Profil, Doppelklick oder Enter beendet ein offenes Profil. Umschalt rastet Winkel in 15°-Schritten.'
            : tool === 'rect'
              ? 'Zwei gegenüberliegende Ecken anklicken.'
              : 'Punkte ziehen oder auswählen und mit Entf löschen. Freie Fläche ziehen verschiebt die Ansicht, Mausrad zoomt.',
        ),
        h('div', { class: 'section' }, h('h3', {}, 'Vorlagen'), h('div', { class: 'presets' }, ...PRESETS.map((p) => h('button', { type: 'button', onclick: () => { pts = p.points.map((q) => ({ ...q })); closed = p.closed; closedCb.checked = closed; drawing = false; tool = 'select'; renderSide(); renderTable(); fit(); updateMsg(); } }, svgIcon(p.icon), p.name)))),
        h('div', { class: 'section' }, h('h3', {}, 'Profil'), h('label', { class: 'check', for: 'sk-closed' }, closedCb, 'Geschlossen (Platte)'), h('label', { class: 'field', for: 'sk-snap' }, h('span', {}, 'Raster'), snapSel)),
        h('div', { class: 'section' }, h('h3', {}, 'Punkte'), tableBox),
        msgBox,
      );
    };

    const okBtn = h('button', {
      type: 'button',
      class: 'btn primary',
      onclick: () => {
        const err = validate();
        if (err) {
          message = err;
          updateMsg();
          return;
        }
        finish({ points: pts.map((p) => ({ ...p })), closed });
      },
    }, svgIcon(ICONS.check), 'Übernehmen');

    const modal = h(
      'div',
      { class: 'modal', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Skizze der Basislasche' },
      h(
        'div',
        { class: 'modal-head' },
        h('h2', {}, 'Skizze der Basislasche'),
        h('button', { type: 'button', class: 'btn', onclick: () => fit() }, svgIcon(ICONS.fit), 'Einpassen'),
        h('button', { type: 'button', class: 'btn', onclick: () => { pts = []; drawing = tool === 'line'; selected = -1; renderTable(); render(); updateMsg(); } }, svgIcon(ICONS.trash), 'Leeren'),
        h('button', { type: 'button', class: 'btn', onclick: () => finish(null) }, svgIcon(ICONS.close), 'Abbrechen'),
        okBtn,
      ),
      h('div', { class: 'sk-body' }, canvas, side),
    );
    document.body.appendChild(modal);
    renderSide();
    renderTable();
    new ResizeObserver(() => render()).observe(canvas);
    requestAnimationFrame(() => fit());
  });
}
