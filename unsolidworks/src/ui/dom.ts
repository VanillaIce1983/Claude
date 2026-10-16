// Tiny DOM helpers (no framework).

type Child = Node | string | number | null | undefined | false;
type Attrs = Record<string, unknown>;

export function h<K extends keyof HTMLElementTagNameMap>(tag: K, attrs: Attrs = {}, ...children: Child[]): HTMLElementTagNameMap[K] {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v === undefined || v === null || v === false) continue;
    if (k === 'class') el.className = String(v);
    else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
    else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v as EventListener);
    else if (k === 'html') el.innerHTML = String(v);
    else if (v === true) el.setAttribute(k, '');
    else if (k in el && typeof v !== 'string') (el as unknown as Record<string, unknown>)[k] = v;
    else el.setAttribute(k, String(v));
  }
  append(el, children);
  return el;
}

export function append(el: Element, children: Child[]): void {
  for (const c of children) {
    if (c === null || c === undefined || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
}

export function svgIcon(markup: string, cls = ''): SVGSVGElement {
  const tpl = document.createElement('template');
  tpl.innerHTML = markup.trim();
  const svg = tpl.content.firstElementChild as SVGSVGElement;
  if (cls) svg.setAttribute('class', cls);
  svg.setAttribute('aria-hidden', 'true');
  return svg;
}

export function clear(el: Element): void {
  while (el.firstChild) el.removeChild(el.firstChild);
}

let uid = 0;
export const nextId = (p = 'f'): string => `${p}-${++uid}`;
