import { applyBaseFlange } from './features/baseFlange';
import { applyBreakCorner, applyClosedCorner, applyTab } from './features/corner';
import { applyCut } from './features/cut';
import { applyEdgeFlange, applyHem } from './features/flange';
import { applySketchCut } from './features/sketchCut';
import { applyJog, applySketchedBend } from './features/split';
import type { Feature } from './features/types';
import { Part, clonePart, emptyPart } from './model';
import { SheetSettings, defaultSheet, validateSheet } from './sheet';

export interface Doc {
  format: 'unsolidworks';
  version: 1;
  name: string;
  sheet: SheetSettings;
  features: Feature[];
}

export function newDoc(name = 'Blechteil'): Doc {
  return { format: 'unsolidworks', version: 1, name, sheet: defaultSheet(), features: [] };
}

export interface FeatureStatus {
  ok: boolean;
  error?: string;
}

export interface RegenResult {
  part: Part | null;
  status: Map<string, FeatureStatus>;
  /** Error that applies to the whole document (e.g. invalid sheet settings). */
  docError?: string;
}

function applyFeature(part: Part, sheet: SheetSettings, f: Feature): void {
  switch (f.type) {
    case 'baseFlange':
      return applyBaseFlange(part, sheet, f);
    case 'edgeFlange':
      return applyEdgeFlange(part, sheet, f);
    case 'hem':
      return applyHem(part, sheet, f);
    case 'sketchedBend':
      return applySketchedBend(part, sheet, f);
    case 'jog':
      return applyJog(part, sheet, f);
    case 'cut':
      return applyCut(part, f);
    case 'sketchCut':
      return applySketchCut(part, f);
    case 'tab':
      return applyTab(part, f);
    case 'closedCorner':
      return applyClosedCorner(part, f);
    case 'breakCorner':
      return applyBreakCorner(part, f);
  }
}

/**
 * Rebuild the part from the feature list. A failing feature is skipped
 * (and reported); later features still run, as in a feature-based CAD tree.
 * `upTo` stops after the feature with that index (rollback).
 */
export function regenerate(doc: Doc, upTo = Infinity): RegenResult {
  const status = new Map<string, FeatureStatus>();
  try {
    validateSheet(doc.sheet);
  } catch (e) {
    return { part: null, status, docError: (e as Error).message };
  }
  let part: Part | null = null;
  doc.features.forEach((f, i) => {
    if (i > upTo || f.suppressed) return;
    if (!part) {
      if (f.type !== 'baseFlange') {
        status.set(f.id, { ok: false, error: 'Zuerst wird eine Basislasche benötigt.' });
        return;
      }
      part = emptyPart(doc.sheet.thickness);
    }
    const work = clonePart(part);
    try {
      applyFeature(work, doc.sheet, f);
      part = work;
      status.set(f.id, { ok: true });
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      status.set(f.id, { ok: false, error: msg });
      if (f.type === 'baseFlange' && part.faces.size === 0) part = null;
    }
  });
  return { part, status };
}

export function parseDoc(text: string): Doc {
  let data: unknown;
  try {
    data = JSON.parse(text);
  } catch {
    throw new Error('Die Datei ist kein gültiges JSON.');
  }
  const d = data as Partial<Doc>;
  if (d?.format !== 'unsolidworks' || !Array.isArray(d.features) || !d.sheet) {
    throw new Error('Die Datei ist kein UnsolidWorks-Projekt.');
  }
  const base = defaultSheet();
  return {
    format: 'unsolidworks',
    version: 1,
    name: typeof d.name === 'string' ? d.name : 'Blechteil',
    sheet: {
      ...base,
      ...d.sheet,
      calc: { ...base.calc, ...d.sheet.calc },
      relief: { ...base.relief, ...d.sheet.relief },
      material: { ...base.material, ...d.sheet.material },
    },
    features: d.features as Feature[],
  };
}

/**
 * Parse a document from storage or a file. Unlike parseDoc (also used for
 * undo snapshots), this upgrades documents saved by older versions.
 */
export function loadDoc(text: string): Doc {
  const doc = parseDoc(text);
  renameOldCuts(doc);
  return doc;
}

/**
 * Hole features ('cut') used to be named "Ausschnitt1, 2, …". That name now
 * belongs to the sketched cut, so those defaults become the next free "BohrungN".
 */
function renameOldCuts(doc: Doc): void {
  const names = new Set(doc.features.map((f) => f.name));
  for (const f of doc.features) {
    if (f.type !== 'cut' || !/^Ausschnitt\d+$/.test(f.name)) continue;
    let n = 1;
    while (names.has(`Bohrung${n}`)) n++;
    names.delete(f.name);
    f.name = `Bohrung${n}`;
    names.add(f.name);
  }
}
