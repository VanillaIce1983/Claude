// German number formatting and a small, safe arithmetic parser for inputs
// (so "120/2+5" or "12,5" both work in a field).

const nf = (digits: number) => new Intl.NumberFormat('de-DE', { maximumFractionDigits: digits, minimumFractionDigits: 0 });
const cache = new Map<number, Intl.NumberFormat>();

export function num(v: number, digits = 2): string {
  if (!Number.isFinite(v)) return '–';
  if (!cache.has(digits)) cache.set(digits, nf(digits));
  const r = Math.abs(v) < 0.5 * 10 ** -digits ? 0 : v;
  return cache.get(digits)!.format(r);
}

export function mm(v: number, digits = 2): string {
  return `${num(v, digits)} mm`;
}

/** Plain value for an input field (no thousands separator). */
export function inputValue(v: number, digits = 4): string {
  if (!Number.isFinite(v)) return '';
  const r = Math.abs(v) < 0.5 * 10 ** -digits ? 0 : v;
  const s = r.toFixed(digits);
  const t = s.includes('.') ? s.replace(/0+$/, '').replace(/\.$/, '') : s;
  return t.replace('.', ',');
}

/** Evaluate + − × ÷ and parentheses; accepts comma or dot as decimal separator. */
export function parseNumber(text: string): number | null {
  const src = text.trim().replace(/\s+/g, '').replace(/,/g, '.').replace(/[×x*]/g, '*').replace(/[÷:]/g, '/');
  if (!src || !/^[0-9.+\-*/()]+$/.test(src)) return null;
  let i = 0;
  const peek = (): string => src[i];
  const expr = (): number => {
    let v = term();
    while (peek() === '+' || peek() === '-') {
      const op = src[i++];
      const r = term();
      v = op === '+' ? v + r : v - r;
    }
    return v;
  };
  const term = (): number => {
    let v = factor();
    while (peek() === '*' || peek() === '/') {
      const op = src[i++];
      const r = factor();
      v = op === '*' ? v * r : v / r;
    }
    return v;
  };
  const factor = (): number => {
    if (peek() === '-') {
      i++;
      return -factor();
    }
    if (peek() === '+') {
      i++;
      return factor();
    }
    if (peek() === '(') {
      i++;
      const v = expr();
      if (peek() !== ')') throw new Error('paren');
      i++;
      return v;
    }
    const m = /^[0-9]*\.?[0-9]+|^[0-9]+\.?/.exec(src.slice(i));
    if (!m) throw new Error('num');
    i += m[0].length;
    return parseFloat(m[0]);
  };
  try {
    const v = expr();
    if (i !== src.length || !Number.isFinite(v)) return null;
    return v;
  } catch {
    return null;
  }
}
