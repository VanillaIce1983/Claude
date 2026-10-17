import { describe, expect, it } from 'vitest';
import { loadDoc, parseDoc, regenerate } from '../src/kernel/doc';
import type { CutFeature, Feature } from '../src/kernel/features/types';
import type { MeasureEntity } from '../src/kernel/measure';
import { Face, findEdge } from '../src/kernel/model';
import { distPointSegment } from '../src/kernel/polygon';
import { v3 } from '../src/kernel/vec';
import { exampleDoc } from '../src/ui/example';
import { newCut, newSketchCut, newSketchedBend, nextName, pickPrompt, referenceEdgeIndex } from '../src/ui/features';
import { addsUp } from '../src/ui/measure';

const cutNames = (features: Feature[]): string[] => features.filter((f) => f.type === 'cut').map((f) => f.name);

/** The example as the previous version saved it: its holes still carried the old default names. */
function oldExampleJson(): string {
  const d = exampleDoc();
  for (const f of d.features) if (f.type === 'cut') f.name = f.name.replace('Bohrung', 'Ausschnitt');
  return JSON.stringify(d);
}

describe('hole names (Bohrung vs. Ausschnitt)', () => {
  it('the example part names its holes Bohrung, so a new sketched cut is Ausschnitt1', () => {
    const d = exampleDoc();
    expect(cutNames(d.features)).toEqual(['Bohrung1', 'Bohrung2']);
    expect(nextName(d, 'sketchCut')).toBe('Ausschnitt1');
    expect(nextName(d, 'cut')).toBe('Bohrung3');
  });

  it('loading an old document renames its AusschnittN holes to BohrungN and still rebuilds', () => {
    const d = loadDoc(oldExampleJson());
    expect(cutNames(d.features)).toEqual(['Bohrung1', 'Bohrung2']);
    expect([...regenerate(d).status.values()].every((s) => s.ok)).toBe(true);
    expect(nextName(d, 'sketchCut')).toBe('Ausschnitt1');
  });

  it('takes the next free BohrungN and leaves sketched cuts and own names alone', () => {
    const d = exampleDoc();
    const [c1, c2] = d.features.filter((f): f is CutFeature => f.type === 'cut');
    c1.name = 'Ausschnitt1';
    c2.name = 'Bohrung1';
    const extra: CutFeature = { ...c1, id: 'cut3', name: 'Ausschnitt links' };
    const sketched = { ...newSketchCut(d), name: 'Ausschnitt2' };
    d.features.push(extra, sketched);
    const loaded = loadDoc(JSON.stringify(d));
    const name = (id: string): string => loaded.features.find((f) => f.id === id)!.name;
    expect(name('cut1')).toBe('Bohrung2');
    expect(name('cut2')).toBe('Bohrung1');
    expect(name('cut3')).toBe('Ausschnitt links');
    expect(name(sketched.id)).toBe('Ausschnitt2');
  });

  it('parseDoc (undo snapshots) keeps names exactly as stored', () => {
    expect(cutNames(parseDoc(oldExampleJson()).features)).toEqual(['Ausschnitt1', 'Ausschnitt2']);
  });
});

describe('prompts before anything is picked', () => {
  it('a new Ausschnitt, Bohrung or sketched bend asks for the pick instead of reporting a vanished edge', () => {
    const d = exampleDoc();
    for (const f of [newSketchCut(d), newCut(d), newSketchedBend(d)]) {
      const r = regenerate({ ...d, features: [...d.features, f] });
      expect(r.status.get(f.id)?.ok).toBe(false);
      const prompt = pickPrompt(f);
      expect(prompt).toMatch(/^Bitte im 3D-Fenster/);
      expect(prompt).not.toMatch(/existiert nicht mehr/);
    }
  });

  it('no prompt once a reference is set', () => {
    const d = exampleDoc();
    const f = newSketchCut(d);
    f.sketch.edge = { tag: 'base:e3', at: { x: 0, y: 60 } };
    expect(pickPrompt(f)).toBeUndefined();
  });
});

describe('reference edge for a face pick', () => {
  const part = regenerate(exampleDoc()).part!;

  it('near a rounded corner, the nearest straight edge wins over an arc chord', () => {
    const face = part.faces.get('fl1.1')!;
    const p = { x: 177.5, y: 153.5 };
    const i = referenceEdgeIndex(face, p);
    const tag = face.outer.tags[i];
    expect(tag.startsWith('bc1:')).toBe(false);
    const a = face.outer.pts[i];
    const b = face.outer.pts[(i + 1) % face.outer.pts.length];
    expect(Math.hypot(b.x - a.x, b.y - a.y)).toBeGreaterThan(3);
    // the frame follows a straight edge, so it is axis-parallel on this face
    expect(Math.min(Math.abs(b.x - a.x), Math.abs(b.y - a.y))).toBeLessThan(1e-9);
    // a findEdge round trip resolves to the same straight edge
    const mid = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
    expect(findEdge(part, { tag, at: mid }).index).toBe(i);
  });

  it('away from arcs, it is simply the nearest edge', () => {
    const face = part.faces.get('base')!;
    const p = { x: 100, y: 15 };
    const i = referenceEdgeIndex(face, p);
    const n = face.outer.pts.length;
    const d = (k: number): number => distPointSegment(p, face.outer.pts[k], face.outer.pts[(k + 1) % n]);
    for (let k = 0; k < n; k++) expect(d(i)).toBeLessThanOrEqual(d(k) + 1e-9);
  });

  it('falls back to a chord when the outline has nothing else', () => {
    const pts = Array.from({ length: 12 }, (_, k) => ({ x: 10 * Math.cos((k * Math.PI) / 6), y: 10 * Math.sin((k * Math.PI) / 6) }));
    const face: Face = { id: 'c', feature: 'c', outer: { pts, tags: pts.map(() => 'arc') }, holes: [] };
    expect(referenceEdgeIndex(face, { x: 10, y: 1 })).toBe(0);
  });
});

describe('measure: which picks add up', () => {
  const edge = { kind: 'edge', a: v3(0, 0, 0), b: v3(1, 0, 0), label: 'Kante' } as MeasureEntity;
  const point = { kind: 'point', p: v3(0, 0, 0), label: 'Punkt' } as MeasureEntity;
  const plane = { kind: 'plane', origin: v3(0, 0, 0), normal: v3(0, 0, 1), area: 1, perimeter: 4, label: 'Fläche' } as MeasureEntity;

  it('three of one summable kind add up; a mixed third pick does not', () => {
    expect(addsUp([edge, edge, edge])).toBe(true);
    expect(addsUp([plane, plane, plane])).toBe(true);
    expect(addsUp([point, point, point])).toBe(true);
    expect(addsUp([plane, edge, point])).toBe(false);
    expect(addsUp([plane, plane, point])).toBe(false);
  });
});
