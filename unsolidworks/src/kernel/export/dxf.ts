// DXF (R12, mm) of the flat pattern: closed polylines for the cut contour,
// true circles for round holes, bend lines on separate layers and bend notes.

import type { FlatPattern } from '../flat';
import { fmt } from '../vec';

export const DXF_LAYERS = {
  outline: { name: 'KONTUR', color: 7 },
  up: { name: 'BIEGUNG_OBEN', color: 3 },
  down: { name: 'BIEGUNG_UNTEN', color: 1 },
  region: { name: 'BIEGEBEREICH', color: 8 },
  notes: { name: 'BIEGENOTIZEN', color: 2 },
} as const;

export interface DxfOptions {
  bendLines: boolean;
  bendRegions: boolean;
  notes: boolean;
}

const num = (v: number): string => (Math.abs(v) < 1e-9 ? '0' : v.toFixed(6).replace(/0+$/, '').replace(/\.$/, ''));

export function bendNote(up: boolean, angleDeg: number, radius: number, dxfDegree = false): string {
  return `${up ? 'OBEN' : 'UNTEN'} ${fmt(angleDeg, 1)}${dxfDegree ? '%%d' : '°'} R${fmt(radius, 2)}`;
}

export function flatToDxf(fp: FlatPattern, opts: DxfOptions = { bendLines: true, bendRegions: false, notes: true }): string {
  const ox = fp.bounds.minX;
  const oy = fp.bounds.minY;
  const X = (x: number): string => num(x - ox);
  const Y = (y: number): string => num(y - oy);
  const out: string[] = [];
  const g = (code: number, value: string | number): void => {
    out.push(String(code), String(value));
  };

  g(0, 'SECTION');
  g(2, 'HEADER');
  g(9, '$ACADVER');
  g(1, 'AC1009');
  g(9, '$INSUNITS');
  g(70, 4);
  g(9, '$MEASUREMENT');
  g(70, 1);
  g(9, '$EXTMIN');
  g(10, 0);
  g(20, 0);
  g(9, '$EXTMAX');
  g(10, num(fp.bounds.maxX - ox));
  g(20, num(fp.bounds.maxY - oy));
  g(0, 'ENDSEC');

  const layers = Object.values(DXF_LAYERS);
  g(0, 'SECTION');
  g(2, 'TABLES');
  g(0, 'TABLE');
  g(2, 'LTYPE');
  g(70, 1);
  g(0, 'LTYPE');
  g(2, 'CONTINUOUS');
  g(70, 0);
  g(3, 'Solid line');
  g(72, 65);
  g(73, 0);
  g(40, 0);
  g(0, 'ENDTAB');
  g(0, 'TABLE');
  g(2, 'LAYER');
  g(70, layers.length);
  for (const l of layers) {
    g(0, 'LAYER');
    g(2, l.name);
    g(70, 0);
    g(62, l.color);
    g(6, 'CONTINUOUS');
  }
  g(0, 'ENDTAB');
  g(0, 'ENDSEC');

  g(0, 'SECTION');
  g(2, 'ENTITIES');
  for (const loop of fp.loops) {
    if (loop.circle) {
      g(0, 'CIRCLE');
      g(8, DXF_LAYERS.outline.name);
      g(10, X(loop.circle.c.x));
      g(20, Y(loop.circle.c.y));
      g(40, num(loop.circle.r));
      continue;
    }
    g(0, 'POLYLINE');
    g(8, DXF_LAYERS.outline.name);
    g(66, 1);
    g(10, 0);
    g(20, 0);
    g(70, 1);
    for (const p of loop.pts) {
      g(0, 'VERTEX');
      g(8, DXF_LAYERS.outline.name);
      g(10, X(p.x));
      g(20, Y(p.y));
    }
    g(0, 'SEQEND');
    g(8, DXF_LAYERS.outline.name);
  }
  for (const b of fp.bendLines) {
    const layer = b.up ? DXF_LAYERS.up.name : DXF_LAYERS.down.name;
    if (opts.bendLines) {
      g(0, 'LINE');
      g(8, layer);
      g(10, X(b.a.x));
      g(20, Y(b.a.y));
      g(11, X(b.b.x));
      g(21, Y(b.b.y));
    }
    if (opts.bendRegions) {
      for (const [p, q] of [b.regionA, b.regionB]) {
        g(0, 'LINE');
        g(8, DXF_LAYERS.region.name);
        g(10, X(p.x));
        g(20, Y(p.y));
        g(11, X(q.x));
        g(21, Y(q.y));
      }
    }
    if (opts.notes) {
      const mx = (b.a.x + b.b.x) / 2;
      const my = (b.a.y + b.b.y) / 2;
      const rot = (Math.atan2(b.b.y - b.a.y, b.b.x - b.a.x) * 180) / Math.PI;
      const upright = rot > 90 || rot <= -90 ? rot + 180 : rot;
      g(0, 'TEXT');
      g(8, DXF_LAYERS.notes.name);
      g(10, X(mx));
      g(20, Y(my));
      g(40, 2.5);
      g(1, bendNote(b.up, b.angleDeg, b.radius, true));
      g(50, num(upright));
      g(72, 1);
      g(11, X(mx));
      g(21, Y(my));
    }
  }
  g(0, 'ENDSEC');
  g(0, 'EOF');
  return out.join('\r\n') + '\r\n';
}
