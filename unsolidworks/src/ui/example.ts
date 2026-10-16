// The example part the app opens with: a housing bracket that shows edge
// flanges, a hem, a partial flange with bend reliefs and cut-outs.

import { Doc, newDoc, regenerate } from '../kernel/doc';
import type { Feature } from '../kernel/features/types';
import { allEdges } from '../kernel/model';

function refFor(doc: Doc, tag: string): { tag: string; at: { x: number; y: number } } {
  const r = regenerate(doc);
  if (!r.part) throw new Error('example: no part');
  const e = allEdges(r.part).find((x) => x.tag === tag);
  if (!e) throw new Error(`example: edge ${tag} missing`);
  return { tag, at: { x: (e.a.x + e.b.x) / 2, y: (e.a.y + e.b.y) / 2 } };
}

export function exampleDoc(): Doc {
  const doc = newDoc('Gehäusewinkel (Beispiel)');
  const add = (f: Feature): void => {
    doc.features.push(f);
  };
  add({
    id: 'base',
    name: 'Basislasche1',
    type: 'baseFlange',
    plane: 'top',
    points: [
      { x: 0, y: 0 },
      { x: 180, y: 0 },
      { x: 180, y: 120 },
      { x: 0, y: 120 },
    ],
    closed: true,
    reverse: false,
    depth: 100,
    midPlane: false,
  });
  add({
    id: 'fl1',
    name: 'Kantenlasche1',
    type: 'edgeFlange',
    edges: [
      { ...refFor(doc, 'base:e0'), side: 'top' },
      { ...refFor(doc, 'base:e2'), side: 'top' },
    ],
    angle: 90,
    length: 40,
    lengthRef: 'outer',
    position: 'materialInside',
    reverse: false,
    width: { mode: 'full', offset1: 10, offset2: 10, width: 50 },
  });
  add({
    id: 'hem1',
    name: 'Saum1',
    type: 'hem',
    edges: [{ ...refFor(doc, 'fl1.0:end'), side: 'top' }],
    hemType: 'closed',
    position: 'materialInside',
    reverse: false,
    length: 8,
    gap: 1,
    angle: 270,
    radius: 2,
    width: { mode: 'full', offset1: 10, offset2: 10, width: 50 },
  });
  add({
    id: 'fl2',
    name: 'Kantenlasche2',
    type: 'edgeFlange',
    edges: [{ ...refFor(doc, 'base:e1'), side: 'top' }],
    angle: 90,
    length: 25,
    lengthRef: 'outer',
    position: 'materialInside',
    reverse: false,
    width: { mode: 'centered', offset1: 10, offset2: 10, width: 60 },
  });
  add({
    id: 'cut1',
    name: 'Ausschnitt1',
    type: 'cut',
    edge: refFor(doc, 'base:e3'),
    u: 30,
    v: 35,
    shape: 'circle',
    diameter: 8,
    width: 30,
    height: 10,
    angle: 0,
    count: 2,
    spacing: 60,
  });
  add({
    id: 'cut2',
    name: 'Ausschnitt2',
    type: 'cut',
    edge: refFor(doc, 'fl1.1:end'),
    u: 90,
    v: 14,
    shape: 'slot',
    diameter: 10,
    width: 40,
    height: 8,
    angle: 0,
    count: 1,
    spacing: 30,
  });
  return doc;
}
