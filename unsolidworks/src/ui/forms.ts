// Form controls for the property panels.

import { h, svgIcon } from './dom';
import { inputValue, parseNumber } from './format';

let fieldSeq = 0;
const fid = (): string => `fld${++fieldSeq}`;

export interface NumOpts {
  unit?: string;
  min?: number;
  max?: number;
  /** Exclusive minimum (value must be greater). */
  gt?: number;
  digits?: number;
  integer?: boolean;
  id?: string;
}

export function numberField(label: string, get: () => number, set: (v: number) => void, opts: NumOpts = {}): HTMLElement {
  const id = opts.id ?? fid();
  const input = h('input', { id, type: 'text', inputmode: 'decimal', autocomplete: 'off', value: inputValue(get(), opts.digits ?? 4) });
  const box = h('div', { class: 'num' }, input, opts.unit ? h('span', { class: 'unit' }, opts.unit) : null);
  let timer: number | undefined;
  const valid = (v: number | null): v is number => {
    if (v === null || !Number.isFinite(v)) return false;
    if (opts.integer && !Number.isInteger(v)) return false;
    if (opts.min !== undefined && v < opts.min) return false;
    if (opts.max !== undefined && v > opts.max) return false;
    if (opts.gt !== undefined && v <= opts.gt) return false;
    return true;
  };
  const commit = (): void => {
    const v = parseNumber(input.value);
    if (valid(v)) {
      box.style.borderColor = '';
      if (v !== get()) set(v);
    } else {
      box.style.borderColor = 'var(--danger)';
    }
  };
  input.addEventListener('input', () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(commit, 220);
  });
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      window.clearTimeout(timer);
      commit();
      input.value = inputValue(get(), opts.digits ?? 4);
    }
    if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
      e.preventDefault();
      const step = e.shiftKey ? 10 : opts.integer ? 1 : e.altKey ? 0.1 : 1;
      const cur = parseNumber(input.value) ?? get();
      const next = cur + (e.key === 'ArrowUp' ? step : -step);
      if (valid(next)) {
        input.value = inputValue(next, opts.digits ?? 4);
        set(next);
      }
    }
  });
  input.addEventListener('blur', () => {
    window.clearTimeout(timer);
    commit();
    input.value = inputValue(get(), opts.digits ?? 4);
    box.style.borderColor = '';
  });
  return h('label', { class: 'field', for: id }, h('span', {}, label), box);
}

export function selectField<T extends string>(label: string, options: [T, string][], get: () => T, set: (v: T) => void): HTMLElement {
  const id = fid();
  const sel = h('select', { id }, ...options.map(([v, l]) => h('option', { value: v, selected: v === get() }, l)));
  sel.addEventListener('change', () => set(sel.value as T));
  return h('label', { class: 'field', for: id }, h('span', {}, label), sel);
}

export function wideSelect<T extends string>(options: [T, string][], get: () => T, set: (v: T) => void, ariaLabel: string): HTMLElement {
  const sel = h('select', { id: fid(), 'aria-label': ariaLabel }, ...options.map(([v, l]) => h('option', { value: v, selected: v === get() }, l)));
  sel.addEventListener('change', () => set(sel.value as T));
  return sel;
}

export function checkField(label: string, get: () => boolean, set: (v: boolean) => void): HTMLElement {
  const id = fid();
  const cb = h('input', { id, type: 'checkbox', checked: get() });
  cb.addEventListener('change', () => set(cb.checked));
  return h('label', { class: 'check', for: id }, cb, label);
}

export interface SegOption<T> {
  value: T;
  label: string;
  icon?: string;
}

export function segField<T extends string>(options: SegOption<T>[], get: () => T, set: (v: T) => void, ariaLabel: string): HTMLElement {
  const wrap = h('div', { class: 'seg', role: 'radiogroup', 'aria-label': ariaLabel });
  const render = (): void => {
    wrap.replaceChildren(
      ...options.map((o) => {
        const b = h(
          'button',
          {
            type: 'button',
            class: o.value === get() ? 'on' : '',
            role: 'radio',
            'aria-checked': o.value === get() ? 'true' : 'false',
            title: o.label,
            onclick: () => {
              set(o.value);
              render();
            },
          },
          o.icon ? svgIcon(o.icon) : null,
          o.label,
        );
        return b;
      }),
    );
  };
  render();
  return wrap;
}

export function section(title: string, ...children: (Node | null | false)[]): HTMLElement {
  return h('div', { class: 'section' }, h('h3', {}, title), ...children);
}
