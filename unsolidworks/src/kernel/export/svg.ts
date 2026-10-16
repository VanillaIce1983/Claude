// SVG drawing of the flat pattern (1 unit = 1 mm).

import type { FlatPattern } from '../flat';
import { fmt } from '../vec';
import { bendNote } from './dxf';

export interface SvgColors {
  outline: string;
  up: string;
  down: string;
  text: string;
  fill: string;
}

const LIGHT: SvgColors = { outline: '#1c2630', up: '#1f7a4d', down: '#c0392b', text: '#334155', fill: '#e8edf2' };

export function flatToSvg(fp: FlatPattern, colors: SvgColors = LIGHT, margin = 10): string {
  const { minX, minY, maxX, maxY } = fp.bounds;
  const w = maxX - minX + 2 * margin;
  const h = maxY - minY + 2 * margin;
  const X = (x: number): string => fmt(x - minX + margin, 4);
  const Y = (y: number): string => fmt(maxY - y + margin, 4);
  const sw = Math.max(0.2, Math.min(w, h) / 600);
  const parts: string[] = [];
  parts.push(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${fmt(w, 2)}mm" height="${fmt(h, 2)}mm" viewBox="0 0 ${fmt(w, 3)} ${fmt(h, 3)}">`,
  );
  // outer loops filled, inner loops cut out with evenodd
  let d = '';
  for (const l of fp.loops) {
    if (l.circle) {
      const { c, r } = l.circle;
      d += `M${X(c.x - r)} ${Y(c.y)}a${fmt(r, 4)} ${fmt(r, 4)} 0 1 0 ${fmt(2 * r, 4)} 0a${fmt(r, 4)} ${fmt(r, 4)} 0 1 0 ${fmt(-2 * r, 4)} 0Z`;
      continue;
    }
    d += 'M' + l.pts.map((p) => `${X(p.x)} ${Y(p.y)}`).join('L') + 'Z';
  }
  parts.push(`<path d="${d}" fill="${colors.fill}" fill-rule="evenodd" stroke="${colors.outline}" stroke-width="${fmt(sw, 3)}"/>`);
  for (const b of fp.bendLines) {
    const col = b.up ? colors.up : colors.down;
    parts.push(
      `<line x1="${X(b.a.x)}" y1="${Y(b.a.y)}" x2="${X(b.b.x)}" y2="${Y(b.b.y)}" stroke="${col}" stroke-width="${fmt(sw, 3)}" stroke-dasharray="${fmt(sw * 8, 3)} ${fmt(sw * 4, 3)}"/>`,
    );
    const mx = (b.a.x + b.b.x) / 2;
    const my = (b.a.y + b.b.y) / 2;
    let rot = -(Math.atan2(b.b.y - b.a.y, b.b.x - b.a.x) * 180) / Math.PI;
    if (rot > 90) rot -= 180;
    if (rot < -90) rot += 180;
    const fs = Math.max(2, Math.min(5, b.length / 12));
    parts.push(
      `<text x="${X(mx)}" y="${Y(my)}" font-family="sans-serif" font-size="${fmt(fs, 2)}" fill="${col}" text-anchor="middle" dy="${fmt(-fs * 0.4, 2)}" transform="rotate(${fmt(rot, 2)} ${X(mx)} ${Y(my)})">${bendNote(b.up, b.angleDeg, b.radius)}</text>`,
    );
  }
  parts.push('</svg>');
  return parts.join('\n');
}
