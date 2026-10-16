// Application controller: document, undo/redo, commands, panels, viewport.

import { Doc, RegenResult, newDoc, parseDoc, regenerate } from '../kernel/doc';
import { flatToDxf } from '../kernel/export/dxf';
import { meshToStl } from '../kernel/export/stl';
import { flatToSvg } from '../kernel/export/svg';
import { FlatPattern, flatPattern } from '../kernel/flat';
import { FEATURE_LABELS, Feature, FeatureType } from '../kernel/features/types';
import { PartMesh, PickEdge, buildMesh, meshVolume, pickCorners, pickEdges } from '../kernel/mesh';
import { EdgeRef, Part, findEdge, isSeamTag } from '../kernel/model';
import { distPointSegment } from '../kernel/polygon';
import { describeCalc } from '../kernel/sheet';
import { V2, dist2, dot2, norm2, rotCCW, sub2 } from '../kernel/vec';
import { h, svgIcon } from './dom';
import { exampleDoc } from './example';
import { safeName, saveFile, initDownloads } from './exporter';
import { newBaseFlange, newBreakCorner, newClosedCorner, newCut, newEdgeFlange, newHem, newJog, newSketchCut, newSketchedBend, newTab, summary } from './features';
import { flatPanel } from './flatpanel';
import { num } from './format';
import { ICONS, IconName } from './icons';
import { PanelHost, featurePanel, sheetPanel } from './panels';
import { PRESETS, SketchResult, openSketcher } from './sketcher';
import { Hit, Viewer, ViewColors } from './viewer';

const STORAGE_KEY = 'unsolidworks.doc.v1';

interface Model {
  regen: RegenResult;
  part: Part | null;
  mesh: PartMesh | null;
  fp: FlatPattern | null;
  edges: PickEdge[];
  corners: PickEdge[];
  volume: number;
  meshError?: string;
}

interface Session {
  kind: 'feature' | 'sheet';
  draft: Feature | null;
  index: number;
  isNew: boolean;
  before: string;
  pre: Model;
}

const ICON_FOR: Record<FeatureType, IconName> = {
  baseFlange: 'baseFlange',
  edgeFlange: 'edgeFlange',
  hem: 'hem',
  sketchedBend: 'sketchedBend',
  jog: 'jog',
  cut: 'cut',
  sketchCut: 'sketchCut',
  tab: 'tab',
  closedCorner: 'closedCorner',
  breakCorner: 'breakCorner',
};

function buildModel(doc: Doc, upTo = Infinity): Model {
  const regen = regenerate(doc, upTo);
  const part = regen.part;
  if (!part) return { regen, part: null, mesh: null, fp: null, edges: [], corners: [], volume: 0 };
  try {
    const mesh = buildMesh(part);
    const fp = flatPattern(part, mesh);
    return {
      regen,
      part,
      mesh,
      fp,
      edges: pickEdges(part, mesh.placements),
      corners: pickCorners(part, mesh.placements),
      volume: meshVolume(mesh.positions, mesh.indices),
    };
  } catch (e) {
    return { regen, part, mesh: null, fp: null, edges: [], corners: [], volume: 0, meshError: (e as Error).message };
  }
}

export class App implements PanelHost {
  doc: Doc;
  private undoStack: string[] = [];
  private redoStack: string[] = [];
  private model!: Model;
  private flat = false;
  private session: Session | null = null;
  private selected: string | null = null;
  private lastPick = { id: '', t: 0 };
  private viewer!: Viewer;
  private els!: {
    top: HTMLElement;
    side: HTMLElement;
    stage: HTMLElement;
    status: HTMLElement;
    hint: HTMLElement;
    legend: HTMLElement;
    empty: HTMLElement;
    toasts: HTMLElement;
    docName: HTMLInputElement;
    errBox: HTMLElement | null;
    okBtn: HTMLButtonElement | null;
  };

  constructor(private root: HTMLElement) {
    this.doc = this.loadStored() ?? exampleDoc();
    initDownloads();
    this.layout();
    this.viewer = new Viewer(this.els.stage, this.colors());
    this.viewer.onPick = (hit) => this.onPick(hit);
    this.viewer.onHover = () => {};
    this.watchTheme();
    this.keyboard();
    this.refresh(true);
  }

  // ---------------------------------------------------------------- storage

  private loadStored(): Doc | null {
    try {
      const s = localStorage.getItem(STORAGE_KEY);
      return s ? parseDoc(s) : null;
    } catch {
      return null;
    }
  }

  private store(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.doc));
    } catch {
      /* storage unavailable: the document stays in memory */
    }
  }

  private snapshot(): string {
    return JSON.stringify(this.doc);
  }

  private commit(before: string): void {
    if (before === this.snapshot()) return;
    this.undoStack.push(before);
    if (this.undoStack.length > 100) this.undoStack.shift();
    this.redoStack = [];
    this.store();
  }

  undo(): void {
    if (this.session) return this.cancel();
    const prev = this.undoStack.pop();
    if (!prev) return;
    this.redoStack.push(this.snapshot());
    this.doc = parseDoc(prev);
    this.store();
    this.refresh();
  }

  redo(): void {
    if (this.session) return;
    const next = this.redoStack.pop();
    if (!next) return;
    this.undoStack.push(this.snapshot());
    this.doc = parseDoc(next);
    this.store();
    this.refresh();
  }

  // ---------------------------------------------------------------- layout

  private layout(): void {
    const stage = h('div', { class: 'stage' });
    const hint = h('div', { class: 'hint', hidden: true, role: 'status' });
    const legend = h(
      'div',
      { class: 'legend', hidden: true },
      h('span', { style: { color: 'var(--bend-up)' } }, h('i', {}), 'Biegung oben'),
      h('span', { style: { color: 'var(--bend-down)' } }, h('i', {}), 'Biegung unten'),
    );
    const empty = h('div', { class: 'empty-stage', hidden: true });
    const views = h(
      'div',
      { class: 'views' },
      ...(
        [
          ['iso', 'Iso'],
          ['top', 'Oben'],
          ['front', 'Vorne'],
          ['right', 'Rechts'],
          ['normal', 'Normal'],
        ] as const
      ).map(([v, l]) => h('button', { type: 'button', title: `Ansicht ${l}`, onclick: () => this.viewer.setView(v) }, l)),
      h('button', { type: 'button', title: 'Einpassen', onclick: () => this.viewer.fit() }, 'Einpassen'),
    );
    stage.append(views, hint, legend, empty);
    const docName = h('input', { class: 'docname', id: 'docname', value: this.doc.name, 'aria-label': 'Name des Teils' });
    docName.addEventListener('change', () => {
      const before = this.snapshot();
      this.doc.name = docName.value.trim() || 'Blechteil';
      this.commit(before);
    });
    const top = h('header', { class: 'topbar' });
    const side = h('aside', { class: 'side', 'aria-label': 'FeatureManager' });
    const status = h('footer', { class: 'statusbar', role: 'status' });
    const toasts = h('div', { class: 'toasts', 'aria-live': 'polite' });
    this.root.replaceChildren(top, h('div', { class: 'main' }, side, stage), status, toasts);
    this.els = { top, side, stage, status, hint, legend, empty, toasts, docName, errBox: null, okBtn: null };
  }

  private tb(icon: IconName, label: string, onclick: () => void, opts: { disabled?: boolean; on?: boolean; title?: string } = {}): HTMLElement {
    return h(
      'button',
      { type: 'button', class: `tb${opts.on ? ' on' : ''}`, disabled: opts.disabled, title: opts.title ?? label, 'aria-pressed': opts.on ? 'true' : undefined, onclick },
      svgIcon(ICONS[icon]),
      label,
    );
  }

  private renderToolbar(): void {
    const hasPart = !!this.model.part;
    const hasBase = this.doc.features.some((f) => f.type === 'baseFlange');
    const busy = !!this.session;
    const t = (type: FeatureType, icon: IconName, label: string, title: string): HTMLElement =>
      this.tb(icon, label, () => this.startCommand(type), {
        disabled: busy || (type === 'baseFlange' ? false : !hasPart),
        on: this.session?.draft?.type === type,
        title,
      });
    this.els.docName.value = this.doc.name;
    this.els.top.replaceChildren(
      h('div', { class: 'brand' }, svgIcon(ICONS.logo), 'UnsolidWorks', this.els.docName),
      h(
        'div',
        { class: 'group' },
        this.tb('newDoc', 'Neu', () => this.newDocument(), { disabled: busy }),
        this.tb('open', 'Öffnen', () => this.openFile(), { disabled: busy, title: 'Projekt (.json) öffnen' }),
        this.tb('save', 'Speichern', () => this.exportAs('json'), { disabled: busy, title: 'Projekt als .json speichern' }),
        this.tb('undo', 'Zurück', () => this.undo(), { disabled: !busy && this.undoStack.length === 0, title: 'Rückgängig (Strg+Z)' }),
        this.tb('redo', 'Vor', () => this.redo(), { disabled: busy || this.redoStack.length === 0, title: 'Wiederholen (Strg+Y)' }),
      ),
      h(
        'div',
        { class: 'group' },
        h('span', { class: 'group-label' }, 'Blech'),
        t('baseFlange', 'baseFlange', hasBase ? 'Basislasche' : 'Basislasche', 'Basislasche: Platte oder gebogenes Profil aus einer Skizze'),
        t('edgeFlange', 'edgeFlange', 'Kantenlasche', 'Kantenlasche an eine oder mehrere Kanten'),
        t('hem', 'hem', 'Saum', 'Saum: geschlossen, offen, Tropfenform oder gerollt'),
        t('sketchedBend', 'sketchedBend', 'Skizz. Biegung', 'Skizzierte Biegung entlang einer Linie'),
        t('jog', 'jog', 'Knick', 'Knick: zwei gegenläufige Biegungen mit Versatz'),
        t('cut', 'cut', 'Ausschnitt', 'Bohrung, Rechteck oder Langloch'),
        t('tab', 'tab', 'Lasche', 'Lasche: Kante in der Ebene verlängern'),
        t('closedCorner', 'closedCorner', 'Geschl. Ecke', 'Geschlossene Ecke: Laschenseite bis zur Nachbarlasche verlängern'),
        t('breakCorner', 'breakCorner', 'Ecke brechen', 'Ecke brechen: Fase oder Verrundung'),
      ),
      h(
        'div',
        { class: 'group' },
        this.tb('flat', 'Abwicklung', () => this.toggleFlat(), { disabled: busy || !hasPart, on: this.flat, title: 'Abwicklung ein/aus' }),
        this.tb('exportIcon', 'Export', () => this.exportMenu(), { disabled: busy || !hasPart, title: 'DXF, SVG, STL, Bild' }),
      ),
    );
  }

  // ---------------------------------------------------------------- model refresh

  refresh(fitView = false): void {
    this.model = buildModel(this.doc);
    const m = this.model;
    if (!m.part || !m.mesh) this.flat = false;
    this.viewer.setModel(m.mesh, m.part, this.flat);
    this.viewer.setFlatBendLines(this.flat ? m.fp : null, m.part);
    if (fitView && m.mesh) this.viewer.setView(this.flat ? 'normal' : 'iso');
    this.updatePicking();
    this.renderToolbar();
    this.renderSide();
    this.renderStatus();
    this.renderStageOverlays();
  }

  /** Lighter refresh while a panel is open: model + viewport + error box only. */
  changed(): void {
    this.model = buildModel(this.doc);
    const m = this.model;
    this.viewer.setModel(m.mesh, m.part, false);
    this.viewer.setFlatBendLines(null, m.part);
    this.updatePicking();
    this.updateSessionError();
    this.renderStatus();
  }

  restructure(): void {
    this.renderSide();
  }

  private colors(): ViewColors {
    return {
      steel: this.css('--steel'),
      edge: this.css('--edge'),
      sel: this.css('--sel'),
      accent: this.css('--accent'),
      up: this.css('--bend-up'),
      down: this.css('--bend-down'),
    };
  }

  css(name: string): string {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || '#888';
  }

  private watchTheme(): void {
    const update = (): void => {
      this.viewer.setColors(this.colors());
      this.viewer.setFlatBendLines(this.flat ? this.model.fp : null, this.model.part);
      if (this.flat && !this.session) this.renderSide();
    };
    try {
      window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', update);
    } catch {
      /* older browsers */
    }
    new MutationObserver(update).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'class', 'style'] });
  }

  // ---------------------------------------------------------------- commands

  private async startCommand(type: FeatureType): Promise<void> {
    if (this.session) return;
    if (this.flat) this.toggleFlat();
    if (type === 'baseFlange') {
      const existing = this.doc.features.find((f) => f.type === 'baseFlange');
      if (existing) return this.editFeature(existing.id);
      const sk = await openSketcher(null);
      if (sk) this.newBase(sk);
      return;
    }
    if (!this.model.part) {
      this.toast('Zuerst eine Basislasche erstellen.', true);
      return;
    }
    const make: Record<Exclude<FeatureType, 'baseFlange'>, () => Feature> = {
      edgeFlange: () => newEdgeFlange(this.doc),
      hem: () => newHem(this.doc),
      sketchedBend: () => newSketchedBend(this.doc),
      jog: () => newJog(this.doc),
      cut: () => newCut(this.doc),
      sketchCut: () => newSketchCut(this.doc),
      tab: () => newTab(this.doc),
      closedCorner: () => newClosedCorner(this.doc),
      breakCorner: () => newBreakCorner(this.doc),
    };
    const draft = make[type]();
    this.beginSession(draft, this.doc.features.length, true);
  }

  private newBase(sk: SketchResult): void {
    const f = newBaseFlange(this.doc, sk.points, sk.closed);
    this.beginSession(f, 0, true);
  }

  editFeature(id: string): void {
    if (this.session) return;
    const index = this.doc.features.findIndex((f) => f.id === id);
    if (index < 0) return;
    if (this.flat) this.toggleFlat();
    const draft = JSON.parse(JSON.stringify(this.doc.features[index])) as Feature;
    this.beginSession(draft, index, false);
  }

  private beginSession(draft: Feature, index: number, isNew: boolean): void {
    const before = this.snapshot();
    const pre = buildModel(this.doc, index - 1);
    if (isNew) this.doc.features.splice(index, 0, draft);
    else this.doc.features[index] = draft;
    this.session = { kind: 'feature', draft, index, isNew, before, pre };
    this.selected = draft.id;
    this.refresh();
  }

  private editSheet(): void {
    if (this.session) return;
    if (this.flat) this.toggleFlat();
    const before = this.snapshot();
    this.session = { kind: 'sheet', draft: null, index: -1, isNew: false, before, pre: this.model };
    this.refresh();
  }

  ok(): void {
    const s = this.session;
    if (!s) return;
    if (s.draft) {
      const st = this.model.regen.status.get(s.draft.id);
      if (st && !st.ok) {
        this.toast(st.error ?? 'Das Feature kann so nicht erstellt werden.', true);
        return;
      }
    } else if (this.model.regen.docError) {
      this.toast(this.model.regen.docError, true);
      return;
    }
    this.session = null;
    this.commit(s.before);
    this.refresh();
  }

  cancel(): void {
    const s = this.session;
    if (!s) return;
    this.doc = parseDoc(s.before);
    this.session = null;
    this.refresh();
  }

  private deleteFeature(id: string): void {
    const before = this.snapshot();
    const i = this.doc.features.findIndex((f) => f.id === id);
    if (i < 0) return;
    const f = this.doc.features[i];
    this.doc.features.splice(i, 1);
    this.commit(before);
    if (this.selected === id) this.selected = null;
    this.refresh();
    this.toast(`${f.name} gelöscht. Strg+Z stellt es wieder her.`);
  }

  private toggleSuppress(id: string): void {
    const before = this.snapshot();
    const f = this.doc.features.find((x) => x.id === id);
    if (!f) return;
    f.suppressed = !f.suppressed;
    this.commit(before);
    this.refresh();
  }

  private toggleFlat(): void {
    if (!this.model.part) return;
    this.flat = !this.flat;
    this.viewer.setModel(this.model.mesh, this.model.part, this.flat);
    this.viewer.setFlatBendLines(this.flat ? this.model.fp : null, this.model.part);
    this.viewer.setView(this.flat ? 'normal' : 'iso');
    this.renderToolbar();
    this.renderSide();
    this.renderStageOverlays();
  }

  private newDocument(): void {
    const before = this.snapshot();
    this.doc = newDoc('Blechteil');
    this.flat = false;
    this.selected = null;
    this.commit(before);
    this.refresh();
  }

  async openSketch(): Promise<void> {
    const s = this.session;
    if (!s?.draft || s.draft.type !== 'baseFlange') return;
    const f = s.draft;
    const res = await openSketcher({ points: f.points, closed: f.closed });
    if (!res) return;
    f.points = res.points;
    f.closed = res.closed;
    if (!f.closed && f.depth <= 0) f.depth = 100;
    this.changed();
    this.restructure();
  }

  // Sketched cuts get their face sketch editor in the next step.
  openFaceSketch(): void {}

  resetSketchFace(): void {}

  // ---------------------------------------------------------------- picking

  private updatePicking(): void {
    const s = this.session;
    if (!s || !s.draft) {
      this.viewer.setPickMode('none');
      this.viewer.setSelection([]);
      this.viewer.setHighlightFeature(this.flat ? null : this.selected);
      return;
    }
    const d = s.draft;
    this.viewer.setHighlightFeature(null);
    const edges = s.pre.edges;
    const find = (ref: EdgeRef): PickEdge | undefined => {
      let best: PickEdge | undefined;
      let bd = Infinity;
      for (const e of edges) {
        if (e.tag !== ref.tag) continue;
        const dd = distPointSegment(ref.at, e.flatA, e.flatB);
        if (dd < bd) {
          bd = dd;
          best = e;
        }
      }
      return best;
    };
    if (d.type === 'edgeFlange' || d.type === 'hem') {
      this.viewer.setPickMode('edge', edges, (e) => !e.seam && !e.hole);
      this.viewer.setSelection(d.edges.map((r) => ({ edge: find(r)!, side: r.side })).filter((x) => x.edge));
    } else if (d.type === 'sketchedBend' || d.type === 'jog') {
      this.viewer.setPickMode('edge', edges, (e) => !e.hole);
      const e = d.line.edge.tag ? find(d.line.edge) : undefined;
      this.viewer.setSelection(e ? [{ edge: e, side: 'top' }] : []);
    } else if (d.type === 'tab' || d.type === 'closedCorner') {
      this.viewer.setPickMode('edge', edges, (e) => !e.seam && !e.hole);
      this.viewer.setSelection(d.edges.map((r) => ({ edge: find(r)!, side: 'top' as const })).filter((x) => x.edge));
    } else if (d.type === 'breakCorner') {
      const corners = s.pre.corners;
      this.viewer.setPickMode('edge', corners);
      const sel = d.corners
        .map((c) => {
          let best: PickEdge | undefined;
          let bd = Infinity;
          for (const e of corners) {
            if (e.corner?.prevTag !== c.prevTag || e.corner?.nextTag !== c.nextTag) continue;
            const dd = dist2(e.flatA, c.at);
            if (dd < bd) {
              bd = dd;
              best = e;
            }
          }
          return best;
        })
        .filter((e): e is PickEdge => !!e)
        .map((edge) => ({ edge, side: 'top' as const }));
      this.viewer.setSelection(sel);
    } else if (d.type === 'cut') {
      this.viewer.setPickMode('face');
      const e = d.edge.tag ? find(d.edge) : undefined;
      this.viewer.setSelection(e ? [{ edge: e, side: 'top' }] : []);
    } else {
      this.viewer.setPickMode('none');
      this.viewer.setSelection([]);
    }
    this.renderStageOverlays();
  }

  private onPick(hit: Hit | null): void {
    const s = this.session;
    if (!s || !s.draft) {
      if (hit?.kind === 'face') {
        const now = performance.now();
        if (this.lastPick.id === hit.feature && now - this.lastPick.t < 400) {
          this.editFeature(hit.feature);
          return;
        }
        this.lastPick = { id: hit.feature, t: now };
        this.selected = hit.feature;
      } else this.selected = null;
      this.viewer.setHighlightFeature(this.flat ? null : this.selected);
      this.renderSide();
      return;
    }
    const d = s.draft;
    if (!hit) return;
    if ((d.type === 'edgeFlange' || d.type === 'hem') && hit.kind === 'edge') {
      const at = mid(hit.edge.flatA, hit.edge.flatB);
      const i = d.edges.findIndex((r) => r.tag === hit.edge.tag && distPointSegment(r.at, hit.edge.flatA, hit.edge.flatB) < 1e-3);
      if (i >= 0) {
        if (d.edges[i].side === hit.side) d.edges.splice(i, 1);
        else d.edges[i].side = hit.side;
      } else d.edges.push({ tag: hit.edge.tag, at, side: hit.side });
      this.changed();
      this.restructure();
    } else if ((d.type === 'sketchedBend' || d.type === 'jog') && hit.kind === 'edge') {
      d.line.edge = { tag: hit.edge.tag, at: mid(hit.edge.flatA, hit.edge.flatB) };
      const len = dist2(hit.edge.flatA, hit.edge.flatB);
      if (d.line.distance <= 0) d.line.distance = Math.min(20, len / 4);
      this.changed();
      this.restructure();
    } else if ((d.type === 'tab' || d.type === 'closedCorner') && hit.kind === 'edge') {
      const at = mid(hit.edge.flatA, hit.edge.flatB);
      const i = d.edges.findIndex((r) => r.tag === hit.edge.tag && distPointSegment(r.at, hit.edge.flatA, hit.edge.flatB) < 1e-3);
      if (i >= 0) d.edges.splice(i, 1);
      else d.edges.push({ tag: hit.edge.tag, at });
      this.changed();
      this.restructure();
    } else if (d.type === 'breakCorner' && hit.kind === 'edge' && hit.edge.corner) {
      const c = hit.edge.corner;
      const i = d.corners.findIndex((r) => r.prevTag === c.prevTag && r.nextTag === c.nextTag && dist2(r.at, hit.edge.flatA) < 1e-3);
      if (i >= 0) d.corners.splice(i, 1);
      else d.corners.push({ prevTag: c.prevTag, nextTag: c.nextTag, at: { ...hit.edge.flatA } });
      this.changed();
      this.restructure();
    } else if (d.type === 'cut' && hit.kind === 'face' && hit.pieceKind === 'face' && hit.flat && s.pre.part) {
      const face = s.pre.part.faces.get(hit.id);
      if (!face) return;
      // nearest outline edge of that face becomes the reference
      let best = -1;
      let bd = Infinity;
      face.outer.pts.forEach((p, i) => {
        const q = face.outer.pts[(i + 1) % face.outer.pts.length];
        const dd = distPointSegment(hit.flat!, p, q);
        if (dd < bd) {
          bd = dd;
          best = i;
        }
      });
      const a = face.outer.pts[best];
      const b = face.outer.pts[(best + 1) % face.outer.pts.length];
      const dir = norm2(sub2(b, a));
      d.edge = { tag: face.outer.tags[best], at: mid(a, b) };
      d.u = round2(dot2(sub2(hit.flat, a), dir));
      d.v = round2(dot2(sub2(hit.flat, a), rotCCW(dir)));
      this.changed();
      this.restructure();
    }
  }

  edgeLabel(ref: { tag: string; at: V2 }, side?: 'top' | 'bottom'): string {
    const part = this.session?.pre.part ?? this.model.part;
    if (!part) return ref.tag;
    try {
      const e = findEdge(part, ref);
      const owner = this.doc.features.find((f) => f.id === e.face.feature);
      const kind = isSeamTag(e.tag) ? 'Biegekante' : e.ringIndex >= 0 ? 'Ausschnittkante' : 'Kante';
      const s = side ? ` · ${side === 'top' ? 'oben' : 'unten'}` : '';
      return `${owner?.name ?? 'Fläche'} · ${kind} ${num(dist2(e.a, e.b), 1)} mm${s}`;
    } catch {
      return 'Kante fehlt – bitte neu wählen';
    }
  }

  cornerLabel(ref: { prevTag: string; nextTag: string; at: V2 }): string {
    const part = this.session?.pre.part ?? this.model.part;
    const face = part ? [...part.faces.values()].find((f) => f.outer.tags.includes(ref.prevTag) && f.outer.tags.includes(ref.nextTag)) : undefined;
    const owner = face ? this.doc.features.find((f) => f.id === face.feature) : undefined;
    return `${owner?.name ?? 'Fläche'} · Ecke bei ${num(ref.at.x, 1)} | ${num(ref.at.y, 1)}`;
  }

  // ---------------------------------------------------------------- side panel

  private renderSide(): void {
    const side = this.els.side;
    this.els.errBox = null;
    this.els.okBtn = null;
    if (this.session) {
      side.replaceChildren(this.sessionPanel(this.session));
      this.updateSessionError();
      return;
    }
    const parts: HTMLElement[] = [];
    if (this.flat && this.model.fp) {
      parts.push(
        h('div', { class: 'pane-head' }, h('span', {}, 'Abwicklung'), h('button', { type: 'button', class: 'btn', onclick: () => this.toggleFlat() }, 'Zurück zum 3D-Modell')),
        flatPanel({
          fp: this.model.fp,
          sheet: this.doc.sheet,
          volume: this.model.volume,
          features: this.doc.features,
          css: (n) => this.css(n),
          onExport: (k) => this.exportAs(k),
        }),
      );
    }
    parts.push(this.treePane());
    side.replaceChildren(...parts);
  }

  private treePane(): HTMLElement {
    const st = this.model.regen.status;
    const sheet = this.doc.sheet;
    const items: HTMLElement[] = [];
    items.push(
      h(
        'li',
        { onclick: () => this.editSheet(), title: 'Blechparameter bearbeiten', tabindex: 0, onkeydown: (e: KeyboardEvent) => e.key === 'Enter' && this.editSheet() },
        svgIcon(ICONS.sheet),
        h('span', {}, h('span', { class: 'tname' }, 'Blech'), h('span', { class: 'tsub' }, `${num(sheet.thickness)} mm · R ${num(sheet.radius)} · ${describeCalc(sheet.calc)}`)),
        h('span', {}),
      ),
    );
    for (const f of this.doc.features) {
      const s = st.get(f.id);
      const err = s && !s.ok ? s.error : undefined;
      const cls = [f.id === this.selected ? 'sel' : '', err ? 'err' : '', f.suppressed ? 'sup' : ''].join(' ');
      items.push(
        h(
          'li',
          {
            class: cls,
            tabindex: 0,
            title: 'Klicken zum Bearbeiten',
            onclick: () => this.editFeature(f.id),
            onkeydown: (e: KeyboardEvent) => e.key === 'Enter' && this.editFeature(f.id),
            onmouseenter: () => !this.flat && this.viewer.setHighlightFeature(f.id),
            onmouseleave: () => this.viewer.setHighlightFeature(this.flat ? null : this.selected),
          },
          svgIcon(ICONS[ICON_FOR[f.type]]),
          h('span', {}, h('span', { class: 'tname' }, f.name), h('span', { class: `tsub${err ? ' err' : ''}` }, err ?? (f.suppressed ? 'unterdrückt' : summary(f)))),
          h(
            'span',
            { class: 'acts' },
            h(
              'button',
              {
                type: 'button',
                class: 'iconbtn',
                title: f.suppressed ? 'Aktivieren' : 'Unterdrücken',
                'aria-label': f.suppressed ? `${f.name} aktivieren` : `${f.name} unterdrücken`,
                onclick: (e: Event) => {
                  e.stopPropagation();
                  this.toggleSuppress(f.id);
                },
              },
              svgIcon(f.suppressed ? ICONS.eye : ICONS.eyeOff),
            ),
            h(
              'button',
              {
                type: 'button',
                class: 'iconbtn',
                title: 'Löschen',
                'aria-label': `${f.name} löschen`,
                onclick: (e: Event) => {
                  e.stopPropagation();
                  this.deleteFeature(f.id);
                },
              },
              svgIcon(ICONS.trash),
            ),
          ),
        ),
      );
    }
    if (this.model.part) {
      items.push(
        h(
          'li',
          { class: this.flat ? 'sel' : '', tabindex: 0, onclick: () => this.toggleFlat(), title: 'Abwicklung ein/aus' },
          svgIcon(ICONS.flat),
          h('span', {}, h('span', { class: 'tname' }, 'Abwicklung'), h('span', { class: 'tsub' }, this.model.fp ? `${num(this.model.fp.bounds.maxX - this.model.fp.bounds.minX)} × ${num(this.model.fp.bounds.maxY - this.model.fp.bounds.minY)} mm` : '')),
          h('span', {}),
        ),
      );
    }
    return h(
      'div',
      {},
      h('div', { class: 'pane-head' }, h('span', {}, 'Feature-Baum'), h('span', {}, this.doc.features.length ? `${this.doc.features.length} Features` : '')),
      h('ul', { class: 'tree' }, ...items),
      this.doc.features.length === 0 ? h('p', { class: 'tree-hint' }, 'Noch leer. Mit „Basislasche“ beginnt jedes Blechteil: als Platte aus einem geschlossenen Profil oder als gebogenes Profil aus einem Linienzug.') : null,
      this.doc.features.length > 0 && this.doc.features.length < 9
        ? h('p', { class: 'tree-hint' }, 'Feature anklicken zum Bearbeiten. Im 3D-Fenster: linke Maustaste dreht, rechte verschiebt, Mausrad zoomt; Doppelklick auf eine Fläche öffnet ihr Feature.')
        : null,
    );
  }

  private sessionPanel(s: Session): HTMLElement {
    const icon = s.draft ? ICONS[ICON_FOR[s.draft.type]] : ICONS.sheet;
    const okBtn = h('button', { type: 'button', class: 'pm-ok', title: 'OK (Strg+Enter)', 'aria-label': 'Übernehmen', onclick: () => this.ok() }, svgIcon(ICONS.check)) as HTMLButtonElement;
    const cancelBtn = h('button', { type: 'button', class: 'pm-cancel', title: 'Abbrechen (Esc)', 'aria-label': 'Abbrechen', onclick: () => this.cancel() }, svgIcon(ICONS.close));
    const title = s.draft ? s.draft.name : 'Blech';
    const nameInput = s.draft
      ? h('input', { class: 'textin', id: 'feat-name', value: s.draft.name, 'aria-label': 'Name des Features' })
      : null;
    nameInput?.addEventListener('change', () => {
      if (s.draft) s.draft.name = nameInput.value.trim() || FEATURE_LABELS[s.draft.type];
    });
    const errBox = h('div', { class: 'errbox', hidden: true, role: 'alert' });
    this.els.errBox = errBox;
    this.els.okBtn = okBtn;
    const body = h(
      'div',
      { class: 'pm-body' },
      errBox,
      ...(s.draft ? featurePanel(this, s.draft) : sheetPanel(this, this.doc.sheet)),
      nameInput ? h('div', { class: 'section' }, h('h3', {}, 'Name'), nameInput) : null,
    );
    return h('div', { class: 'pm' }, h('div', { class: 'pm-head' }, svgIcon(icon, 'ficon'), h('div', { class: 'pm-title' }, title), okBtn, cancelBtn), body);
  }

  private updateSessionError(): void {
    const s = this.session;
    const box = this.els.errBox;
    if (!s || !box) return;
    let err: string | undefined;
    if (s.draft) {
      const st = this.model.regen.status.get(s.draft.id);
      err = st && !st.ok ? st.error : undefined;
    } else err = this.model.regen.docError;
    if (!err && this.model.meshError) err = this.model.meshError;
    box.textContent = err ?? '';
    box.hidden = !err;
    if (this.els.okBtn) this.els.okBtn.disabled = !!err;
    if (!s.draft) return;
    // later features that break because of this edit
    const broken = this.doc.features
      .slice(s.index + 1)
      .filter((f) => this.model.regen.status.get(f.id)?.ok === false)
      .map((f) => f.name);
    if (!err && broken.length) {
      box.hidden = false;
      box.textContent = `Hinweis: ${broken.join(', ')} ${broken.length > 1 ? 'schlagen' : 'schlägt'} danach fehl.`;
    }
  }

  // ---------------------------------------------------------------- stage overlays & status

  private renderStageOverlays(): void {
    const { hint, legend, empty } = this.els;
    legend.hidden = !this.flat;
    const s = this.session;
    let text = '';
    if (s?.draft) {
      switch (s.draft.type) {
        case 'edgeFlange':
          text = s.draft.edges.length ? 'Weitere Kanten anklicken oder mit ✓ übernehmen' : 'Kante anklicken, an der die Lasche entstehen soll';
          break;
        case 'hem':
          text = s.draft.edges.length ? 'Weitere Kanten anklicken oder mit ✓ übernehmen' : 'Kante für den Saum anklicken';
          break;
        case 'sketchedBend':
        case 'jog':
          text = s.draft.line.edge.tag ? 'Abstand und Optionen links einstellen' : 'Bezugskante anklicken – die Biegelinie läuft parallel dazu';
          break;
        case 'cut':
          text = s.draft.edge.tag ? 'Erneut klicken zum Verschieben, Maße links' : 'Auf die Fläche klicken, wo der Ausschnitt sitzen soll';
          break;
        case 'tab':
          text = s.draft.edges.length ? 'Weitere Kanten anklicken oder mit ✓ übernehmen' : 'Kante anklicken, die verlängert werden soll';
          break;
        case 'closedCorner':
          text = s.draft.edges.length ? 'Weitere Ecken anklicken oder mit ✓ übernehmen' : 'Seitenkante einer Lasche an der offenen Ecke anklicken';
          break;
        case 'breakCorner':
          text = s.draft.corners.length ? 'Weitere Ecken anklicken oder mit ✓ übernehmen' : 'Kurze Eckkante (durch die Blechdicke) anklicken';
          break;
      }
    }
    hint.textContent = text;
    hint.hidden = !text;
    const isEmpty = !this.doc.features.length && !s;
    empty.hidden = !isEmpty;
    if (isEmpty) {
      empty.replaceChildren(
        h(
          'div',
          { class: 'start' },
          h('h2', {}, 'Neues Blechteil'),
          h('p', {}, 'Wähle eine Vorlage für die Basislasche oder zeichne eine eigene Skizze. Maße lassen sich danach jederzeit ändern.'),
          h(
            'div',
            { class: 'presets' },
            ...PRESETS.map((p) => h('button', { type: 'button', onclick: () => this.newBase({ points: p.points.map((q) => ({ ...q })), closed: p.closed }) }, svgIcon(p.icon), p.name)),
            h('button', { type: 'button', onclick: () => this.startCommand('baseFlange') }, svgIcon(ICONS.sketch), 'Eigene Skizze'),
          ),
          h('div', { class: 'btnrow', style: { marginTop: '14px' } }, h('button', { type: 'button', class: 'btn', onclick: () => this.loadExample() }, 'Beispielteil laden')),
        ),
      );
    }
  }

  private renderStatus(): void {
    const m = this.model;
    const sheet = this.doc.sheet;
    const errors = [...m.regen.status.values()].filter((s) => !s.ok).length;
    const items: (HTMLElement | string)[] = [h('span', {}, `Blech ${num(sheet.thickness)} mm · R ${num(sheet.radius)} mm · ${describeCalc(sheet.calc)}`)];
    if (m.fp) {
      const w = m.fp.bounds.maxX - m.fp.bounds.minX;
      const hh = m.fp.bounds.maxY - m.fp.bounds.minY;
      items.push(h('span', {}, 'Zuschnitt ', h('b', {}, `${num(w, 1)} × ${num(hh, 1)} mm`)));
      items.push(h('span', {}, 'Masse ', h('b', {}, `${num((m.volume / 1e6) * sheet.material.density, 3)} kg`)));
      items.push(h('span', {}, `${m.fp.bendLines.length} Biegungen`));
    }
    if (m.regen.docError) items.push(h('span', { class: 'err' }, m.regen.docError));
    else if (errors && !this.session) items.push(h('span', { class: 'err' }, `${errors} Feature${errors > 1 ? 's' : ''} mit Fehler`));
    if (m.meshError) items.push(h('span', { class: 'err' }, m.meshError));
    this.els.status.replaceChildren(...items);
  }

  toast(text: string, error = false): void {
    const t = h('div', { class: `toast${error ? ' err' : ''}` }, text);
    this.els.toasts.appendChild(t);
    setTimeout(() => t.remove(), error ? 5200 : 3200);
  }

  private loadExample(): void {
    const before = this.snapshot();
    this.doc = exampleDoc();
    this.commit(before);
    this.refresh(true);
  }

  // ---------------------------------------------------------------- files

  private openFile(): void {
    const input = h('input', { type: 'file', accept: '.json,application/json', style: { display: 'none' } });
    input.addEventListener('change', async () => {
      const file = input.files?.[0];
      input.remove();
      if (!file) return;
      try {
        const doc = parseDoc(await file.text());
        const before = this.snapshot();
        this.doc = doc;
        this.flat = false;
        this.commit(before);
        this.refresh(true);
        this.toast(`„${doc.name}“ geöffnet.`);
      } catch (e) {
        this.toast((e as Error).message, true);
      }
    });
    document.body.appendChild(input);
    input.click();
  }

  private exportMenu(): void {
    const old = document.querySelector('.menu');
    if (old) {
      old.remove();
      return;
    }
    const btn = [...this.els.top.querySelectorAll('.tb')].find((b) => b.textContent?.includes('Export')) as HTMLElement | undefined;
    const r = btn?.getBoundingClientRect();
    const item = (label: string, sub: string, kind: 'dxf' | 'svg' | 'stl' | 'png' | 'json'): HTMLElement =>
      h('button', { type: 'button', onclick: () => (menu.remove(), this.exportAs(kind)) }, label, h('small', {}, sub));
    const menu = h(
      'div',
      { class: 'menu', role: 'menu', style: { top: `${(r?.bottom ?? 60) + 4}px`, left: `${Math.max(8, Math.min((r?.left ?? 8), window.innerWidth - 250))}px` } },
      item('Abwicklung als DXF', 'Für Laser und Stanze, R12, mm', 'dxf'),
      item('Abwicklung als SVG', 'Zeichnung mit Biegelinien', 'svg'),
      item('3D-Modell als STL', 'Gefaltetes Teil, mm', 'stl'),
      item('Ansicht als PNG', 'Bild des 3D-Fensters', 'png'),
      item('Projekt als JSON', 'Zum späteren Weiterbearbeiten', 'json'),
    );
    document.body.appendChild(menu);
    const close = (e: Event): void => {
      if (!menu.contains(e.target as Node)) {
        menu.remove();
        window.removeEventListener('pointerdown', close, true);
      }
    };
    setTimeout(() => window.addEventListener('pointerdown', close, true), 0);
  }

  async exportAs(kind: 'dxf' | 'svg' | 'stl' | 'png' | 'json'): Promise<void> {
    const base = safeName(this.doc.name);
    const m = this.model;
    let res: Awaited<ReturnType<typeof saveFile>>;
    if (kind === 'json') res = await saveFile(`${base}.json`, JSON.stringify(this.doc, null, 2), 'application/json');
    else if (!m.part || !m.mesh || !m.fp) {
      this.toast('Es gibt noch kein Teil zum Exportieren.', true);
      return;
    } else if (kind === 'dxf') res = await saveFile(`${base}_Abwicklung.dxf`, flatToDxf(m.fp), 'application/dxf');
    else if (kind === 'svg') res = await saveFile(`${base}_Abwicklung.svg`, flatToSvg(m.fp), 'image/svg+xml');
    else if (kind === 'stl') res = await saveFile(`${base}.stl`, meshToStl(m.mesh.positions, m.mesh.indices, base), 'model/stl');
    else {
      const data = this.viewer.snapshot();
      const bin = atob(data.split(',')[1]);
      const bytes = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
      res = await saveFile(`${base}.png`, bytes, 'image/png');
    }
    if (res.outcome === 'saved') this.toast(`${res.filename} gespeichert.`);
    else if (res.outcome === 'failed') this.toast(`Speichern nicht möglich: ${res.message ?? 'unbekannter Fehler'}`, true);
  }

  // ---------------------------------------------------------------- keyboard

  private keyboard(): void {
    window.addEventListener('keydown', (e) => {
      if (document.querySelector('.modal')) return;
      const tag = (e.target as HTMLElement)?.tagName;
      const typing = tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA';
      const mod = e.ctrlKey || e.metaKey;
      if (e.key === 'Escape' && this.session) {
        e.preventDefault();
        this.cancel();
      } else if (mod && e.key === 'Enter' && this.session) {
        e.preventDefault();
        (document.activeElement as HTMLElement)?.blur?.();
        setTimeout(() => this.ok(), 0);
      } else if (!typing && mod && e.key.toLowerCase() === 'z' && !e.shiftKey) {
        e.preventDefault();
        this.undo();
      } else if (!typing && mod && (e.key.toLowerCase() === 'y' || (e.key.toLowerCase() === 'z' && e.shiftKey))) {
        e.preventDefault();
        this.redo();
      } else if (!typing && !mod && e.key.toLowerCase() === 'f') {
        this.viewer.fit();
      }
    });
  }

  /** For tests: the current part and flat pattern. */
  debugState(): { features: number; flat: boolean; errors: string[]; bounds?: FlatPattern['bounds'] } {
    return {
      features: this.doc.features.length,
      flat: this.flat,
      errors: [...this.model.regen.status.entries()].filter(([, s]) => !s.ok).map(([id, s]) => `${id}: ${s.error}`),
      bounds: this.model.fp?.bounds,
    };
  }
}

const mid = (a: V2, b: V2): V2 => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });
const round2 = (v: number): number => Math.round(v * 100) / 100;

