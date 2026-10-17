// Property panels ("PropertyManager") for each feature and for the sheet settings.

import type { Doc } from '../kernel/doc';
import { PLANE_LABELS } from '../kernel/features/baseFlange';
import type {
  BaseFlangeFeature,
  BendPosition,
  BreakCornerFeature,
  ClosedCornerFeature,
  SketchCutFeature,
  TabFeature,
  CutFeature,
  EdgeFlangeFeature,
  Feature,
  FlangePosition,
  HemFeature,
  JogFeature,
  SketchedBendFeature,
  WidthSpec,
} from '../kernel/features/types';
import { BendCalc, BendMethod, BendOverrides, MATERIALS, ReliefType, SheetSettings } from '../kernel/sheet';
import { h, svgIcon } from './dom';
import { HEM_LABELS, JOG_DIM_LABELS, LENGTH_LABELS, POSITION_LABELS, sketchContents } from './features';
import { num } from './format';
import { checkField, numberField, section, segField, selectField, wideSelect } from './forms';
import { CORNER_DIAGRAMS, ICONS, POSITION_DIAGRAMS } from './icons';

export interface PanelHost {
  doc: Doc;
  /** Re-run the model with the current draft. */
  changed(): void;
  /** Rebuild the panel (fields depend on a choice). */
  restructure(): void;
  edgeLabel(ref: { tag: string; at: { x: number; y: number } }, side?: 'top' | 'bottom'): string;
  /** Whether a reference edge still exists in the part the feature is built on. */
  edgeExists(ref: { tag: string; at: { x: number; y: number } }): boolean;
  cornerLabel(ref: { prevTag: string; nextTag: string; at: { x: number; y: number } }): string;
  openSketch(): void;
  /** Open the face sketch of the sketched cut being edited. */
  openFaceSketch(): void;
  /** Forget the chosen face, keeping the profiles, so another face can be picked. */
  resetSketchFace(): void;
}

const METHOD_LABELS: [BendMethod, string][] = [
  ['k', 'K-Faktor'],
  ['ba', 'Biegezugabe'],
  ['bd', 'Biegeabzug'],
];
const RELIEF_LABELS: [ReliefType, string][] = [
  ['rect', 'Rechteckig'],
  ['obround', 'Abgerundet'],
  ['tear', 'Riss'],
];

function calcFields(host: PanelHost, get: () => BendCalc, set: (c: BendCalc) => void): HTMLElement[] {
  const c = get();
  const out: HTMLElement[] = [
    selectField('Berechnung', METHOD_LABELS, () => get().method, (m) => {
      set({ ...get(), method: m });
      host.changed();
      host.restructure();
    }),
  ];
  if (c.method === 'k') {
    out.push(numberField('K-Faktor', () => get().kFactor, (v) => (set({ ...get(), kFactor: v }), host.changed()), { min: 0, max: 1, digits: 4 }));
  } else if (c.method === 'ba') {
    out.push(numberField('Biegezugabe', () => get().ba, (v) => (set({ ...get(), ba: v }), host.changed()), { unit: 'mm', min: 0 }));
  } else {
    out.push(numberField('Biegeabzug', () => get().bd, (v) => (set({ ...get(), bd: v }), host.changed()), { unit: 'mm' }));
  }
  return out;
}

function overridesSection(host: PanelHost, sheet: SheetSettings, get: () => BendOverrides | undefined, set: (o: BendOverrides | undefined) => void, opts: { radius: boolean; relief: boolean }): HTMLElement {
  const o = get() ?? {};
  const upd = (patch: Partial<BendOverrides>): void => {
    const next: BendOverrides = { ...(get() ?? {}), ...patch };
    for (const k of Object.keys(next) as (keyof BendOverrides)[]) if (next[k] === undefined) delete next[k];
    set(Object.keys(next).length ? next : undefined);
  };
  const parts: (HTMLElement | null)[] = [];
  if (opts.radius) {
    parts.push(
      checkField('Eigener Biegeradius', () => get()?.radius !== undefined, (on) => {
        upd({ radius: on ? sheet.radius : undefined });
        host.changed();
        host.restructure();
      }),
    );
    if (o.radius !== undefined) {
      parts.push(numberField('Biegeradius', () => get()?.radius ?? sheet.radius, (v) => (upd({ radius: v }), host.changed()), { unit: 'mm', min: 0 }));
    }
  }
  parts.push(
    checkField('Eigene Biegezugabe', () => get()?.calc !== undefined, (on) => {
      upd({ calc: on ? { ...sheet.calc } : undefined });
      host.changed();
      host.restructure();
    }),
  );
  if (o.calc) parts.push(...calcFields(host, () => get()?.calc ?? sheet.calc, (c) => upd({ calc: c })));
  if (opts.relief) {
    parts.push(
      checkField('Eigener Freistich', () => get()?.relief !== undefined, (on) => {
        upd({ relief: on ? { ...sheet.relief } : undefined });
        host.changed();
        host.restructure();
      }),
    );
    if (o.relief) {
      parts.push(
        selectField('Freistich', RELIEF_LABELS, () => get()?.relief?.type ?? sheet.relief.type, (t) => {
          upd({ relief: { ...(get()?.relief ?? sheet.relief), type: t } });
          host.changed();
          host.restructure();
        }),
      );
      if (o.relief.type !== 'tear') {
        parts.push(
          numberField('Verhältnis zur Dicke', () => get()?.relief?.ratio ?? sheet.relief.ratio, (v) => {
            upd({ relief: { ...(get()?.relief ?? sheet.relief), ratio: v } });
            host.changed();
          }, { min: 0.05, max: 2 }),
        );
      }
    }
  }
  return section('Biegeparameter', ...parts, h('p', { class: 'note' }, 'Ohne Häkchen gelten die Werte aus „Blech“.'));
}

function refList(host: PanelHost, labels: string[], remove: (i: number) => void, emptyText: string): HTMLElement {
  if (labels.length === 0) return h('div', { class: 'picklist empty' }, emptyText);
  return h(
    'div',
    { class: 'picklist' },
    ...labels.map((label, i) =>
      h(
        'div',
        { class: 'chip' },
        h('span', {}, label),
        h(
          'button',
          {
            type: 'button',
            class: 'iconbtn',
            title: 'Aus der Auswahl entfernen',
            'aria-label': `${label} entfernen`,
            onclick: () => {
              remove(i);
              host.changed();
              host.restructure();
            },
          },
          svgIcon(ICONS.close),
        ),
      ),
    ),
  );
}

function edgeList(host: PanelHost, f: EdgeFlangeFeature | HemFeature): HTMLElement {
  return refList(host, f.edges.map((e) => host.edgeLabel(e, e.side)), (i) => f.edges.splice(i, 1), 'Kanten im 3D-Fenster anklicken');
}

function sketchCutPanel(host: PanelHost, f: SketchCutFeature): HTMLElement[] {
  const { edge, entities } = f.sketch;
  if (!edge.tag || !host.edgeExists(edge)) {
    // nothing picked yet, or the reference vanished: a face click (re)attaches the sketch
    return [
      section(
        'Skizzenfläche',
        h('div', { class: 'picklist empty' }, edge.tag ? 'Bezugskante fehlt – Fläche im 3D-Fenster anklicken' : 'Fläche im 3D-Fenster anklicken'),
        entities.length ? h('div', { class: 'infobox' }, `${sketchContents(entities)} – bleibt erhalten und wird auf die angeklickte Fläche gelegt`) : null,
        h('p', { class: 'note' }, 'Die nächstgelegene gerade Kante der angeklickten Fläche wird zur Bezugskante: Dort liegt der Skizzenursprung, die X-Achse läuft entlang der Kante.'),
      ),
    ];
  }
  return [
    section(
      'Skizze',
      h('div', { class: 'picklist' }, h('div', { class: 'chip' }, h('span', {}, `Bezug: ${host.edgeLabel(f.sketch.edge)}`))),
      h('div', { class: 'infobox' }, sketchContents(f.sketch.entities)),
      h(
        'div',
        { class: 'btnrow' },
        h('button', { type: 'button', class: 'btn primary', onclick: () => host.openFaceSketch() }, svgIcon(ICONS.sketch), 'Skizze bearbeiten'),
        h('button', { type: 'button', class: 'btn', onclick: () => host.resetSketchFace() }, 'Andere Fläche'),
      ),
    ),
    section(
      'Schnitt',
      h('p', { class: 'note' }, 'Senkrecht zur Fläche durch die Blechdicke. Profile innerhalb der Fläche werden Durchbrüche, Profile über den Rand kerben die Kontur ein. Biegungen dürfen nicht berührt werden.'),
    ),
  ];
}

function tabPanel(host: PanelHost, f: TabFeature): HTMLElement[] {
  return [
    section(
      'Kanten',
      refList(host, f.edges.map((e) => host.edgeLabel(e)), (i) => f.edges.splice(i, 1), 'Kanten im 3D-Fenster anklicken'),
      h('p', { class: 'note' }, 'Die Lasche verlängert die Fläche in ihrer eigenen Ebene, ohne Biegung.'),
    ),
    section('Lasche', numberField('Länge', () => f.length, (v) => ((f.length = v), host.changed()), { unit: 'mm', gt: 0 })),
    widthSection(host, f.width),
  ];
}

function closedCornerPanel(host: PanelHost, f: ClosedCornerFeature): HTMLElement[] {
  return [
    section(
      'Ecken',
      refList(host, f.edges.map((e) => host.edgeLabel(e)), (i) => f.edges.splice(i, 1), 'Seitenkante einer Lasche an der offenen Ecke anklicken'),
      h('p', { class: 'note' }, 'Eine Seitenkante pro Ecke genügt. Beide Laschen an der Ecke werden so verlängert oder gekürzt, dass sie sich treffen.'),
    ),
    section(
      'Eckentyp',
      segField(
        [
          { value: 'butt' as const, label: 'Stoß', icon: CORNER_DIAGRAMS.butt },
          { value: 'overlap' as const, label: 'Überlappen', icon: CORNER_DIAGRAMS.overlap },
          { value: 'underlap' as const, label: 'Unterlappen', icon: CORNER_DIAGRAMS.underlap },
        ],
        () => f.kind ?? 'overlap',
        (v) => ((f.kind = v), host.changed()),
        'Eckentyp',
      ),
      h(
        'p',
        { class: 'note' },
        'Stoß: Beide Laschen laufen gleichmäßig bis zur Innenseite der anderen, außen bleibt eine Aussparung in Blechdicke. Überlappen: Die gewählte Lasche läuft bis zur Außenseite der anderen. Unterlappen: Die andere Lasche deckt die Ecke ab.',
      ),
    ),
    section('Spalt', numberField('Spaltabstand', () => f.gap, (v) => ((f.gap = v), host.changed()), { unit: 'mm', min: 0 })),
    section(
      'Biegebereich',
      checkField('Biegebereich offen lassen', () => !!f.openBend, (v) => {
        if (v) f.openBend = true;
        else delete f.openBend;
        host.changed();
      }),
      h('p', { class: 'note' }, 'Ohne Haken laufen auch die Biegeradien beider Laschen bis in die Ecke und treffen sich schräg, wie in SolidWorks. Passt das wegen unterschiedlicher Radien nicht, bleibt der Biegebereich dieser Ecke offen.'),
    ),
  ];
}

function breakCornerPanel(host: PanelHost, f: BreakCornerFeature): HTMLElement[] {
  return [
    section(
      'Ecken',
      refList(host, f.corners.map((c) => host.cornerLabel(c)), (i) => f.corners.splice(i, 1), 'Eckkanten im 3D-Fenster anklicken'),
      h('p', { class: 'note' }, 'Gewählt wird die kurze Kante durch die Blechdicke an einer Ecke. Ecken an Biegungen sind ausgenommen.'),
    ),
    section(
      'Art',
      segField(
        [
          { value: 'fillet' as const, label: 'Verrundung' },
          { value: 'chamfer' as const, label: 'Fase' },
        ],
        () => f.kind,
        (v) => {
          f.kind = v;
          host.changed();
          host.restructure();
        },
        'Art der Ecke',
      ),
      numberField(f.kind === 'fillet' ? 'Radius' : 'Abstand', () => f.distance, (v) => ((f.distance = v), host.changed()), { unit: 'mm', gt: 0 }),
    ),
  ];
}

function widthSection(host: PanelHost, w: WidthSpec): HTMLElement {
  const parts: HTMLElement[] = [
    selectField(
      'Breite',
      [
        ['full', 'Ganze Kante'],
        ['offset', 'Abstand von den Enden'],
        ['centered', 'Mittig, feste Breite'],
      ],
      () => w.mode,
      (m) => {
        w.mode = m;
        host.changed();
        host.restructure();
      },
    ),
  ];
  if (w.mode === 'offset') {
    parts.push(numberField('Abstand Anfang', () => w.offset1, (v) => ((w.offset1 = v), host.changed()), { unit: 'mm', min: 0 }));
    parts.push(numberField('Abstand Ende', () => w.offset2, (v) => ((w.offset2 = v), host.changed()), { unit: 'mm', min: 0 }));
  } else if (w.mode === 'centered') {
    parts.push(numberField('Laschenbreite', () => w.width, (v) => ((w.width = v), host.changed()), { unit: 'mm', gt: 0 }));
  }
  return section('Laschenbreite', ...parts);
}

function edgeFlangePanel(host: PanelHost, f: EdgeFlangeFeature): HTMLElement[] {
  const positions: FlangePosition[] = ['materialInside', 'materialOutside', 'bendOutside', 'virtualSharp', 'tangentToBend'];
  return [
    section(
      'Kanten',
      edgeList(host, f),
      h('p', { class: 'note' }, 'Ober- oder Unterkante anklicken: Die Lasche biegt zu dieser Seite. Mehrere Kanten sind möglich.'),
      checkField('Richtung umkehren', () => f.reverse, (v) => ((f.reverse = v), host.changed())),
    ),
    section('Winkel', numberField('Laschenwinkel', () => f.angle, (v) => ((f.angle = v), host.changed()), { unit: '°', gt: 0, max: 179.99 })),
    section(
      'Laschenlänge',
      wideSelect(
        (['outer', 'inner', 'tangent'] as const).map((k) => [k, LENGTH_LABELS[k]] as [typeof k, string]),
        () => f.lengthRef,
        (v) => ((f.lengthRef = v), host.changed()),
        'Bezug der Laschenlänge',
      ),
      numberField('Länge', () => f.length, (v) => ((f.length = v), host.changed()), { unit: 'mm', gt: 0 }),
    ),
    section(
      'Laschenposition',
      segField(
        positions.map((p) => ({ value: p, label: POSITION_LABELS[p], icon: POSITION_DIAGRAMS[p] })),
        () => f.position,
        (v) => ((f.position = v), host.changed()),
        'Laschenposition',
      ),
    ),
    widthSection(host, f.width),
    overridesSection(host, host.doc.sheet, () => f.overrides, (o) => (f.overrides = o), { radius: true, relief: true }),
  ];
}

function hemPanel(host: PanelHost, f: HemFeature): HTMLElement[] {
  const fields: HTMLElement[] = [];
  if (f.hemType === 'closed' || f.hemType === 'open') {
    fields.push(numberField('Länge', () => f.length, (v) => ((f.length = v), host.changed()), { unit: 'mm', gt: 0 }));
  }
  if (f.hemType === 'open') {
    fields.push(numberField('Spaltabstand', () => f.gap, (v) => ((f.gap = v), host.changed()), { unit: 'mm', gt: 0 }));
  }
  if (f.hemType === 'teardrop' || f.hemType === 'rolled') {
    fields.push(numberField('Winkel', () => f.angle, (v) => ((f.angle = v), host.changed()), { unit: '°', gt: 0, max: 359 }));
    fields.push(numberField('Radius', () => f.radius, (v) => ((f.radius = v), host.changed()), { unit: 'mm', gt: 0 }));
  }
  return [
    section(
      'Kanten',
      edgeList(host, f),
      h('p', { class: 'note' }, 'Ober- oder Unterkante anklicken: Der Saum legt sich zu dieser Seite um. Läuft er dabei in eine Nachbarlasche oder einen anderen Saum, wird er an diesem Ende gekürzt und hält 0,1 mm Abstand.'),
      checkField('Richtung umkehren', () => f.reverse, (v) => ((f.reverse = v), host.changed())),
    ),
    section(
      'Saumtyp',
      segField(
        (['closed', 'open', 'teardrop', 'rolled'] as const).map((t) => ({ value: t, label: HEM_LABELS[t] })),
        () => f.hemType,
        (v) => {
          f.hemType = v;
          if (v === 'teardrop' && !(f.angle > 180 && f.angle <= 330)) f.angle = 270;
          host.changed();
          host.restructure();
        },
        'Saumtyp',
      ),
      ...fields,
    ),
    section(
      'Position',
      segField(
        (['materialInside', 'bendOutside'] as const).map((p) => ({ value: p, label: POSITION_LABELS[p], icon: POSITION_DIAGRAMS[p] })),
        () => f.position,
        (v) => ((f.position = v), host.changed()),
        'Saumposition',
      ),
    ),
    widthSection(host, f.width),
    overridesSection(host, host.doc.sheet, () => f.overrides, (o) => (f.overrides = o), { radius: false, relief: true }),
  ];
}

function lineSection(host: PanelHost, f: SketchedBendFeature | JogFeature): HTMLElement {
  const ref = f.line.edge.tag
    ? h('div', { class: 'picklist' }, h('div', { class: 'chip' }, h('span', {}, host.edgeLabel(f.line.edge))))
    : h('div', { class: 'picklist empty' }, 'Bezugskante im 3D-Fenster anklicken');
  return section(
    'Biegelinie',
    ref,
    numberField('Abstand zur Kante', () => f.line.distance, (v) => ((f.line.distance = v), host.changed()), { unit: 'mm', gt: 0 }),
    h('p', { class: 'note' }, 'Die Biegelinie verläuft parallel zur Bezugskante auf derselben Fläche.'),
    selectField(
      'Bewegt wird',
      [
        ['edge', 'Seite der Bezugskante'],
        ['other', 'Gegenüberliegende Seite'],
      ],
      () => (f.moveEdgeSide ? 'edge' : 'other'),
      (v) => ((f.moveEdgeSide = v === 'edge'), host.changed()),
    ),
  );
}

const bendPositions = (): { value: BendPosition; label: string; icon: string }[] =>
  (['centerline', 'materialInside', 'materialOutside', 'bendOutside'] as const).map((p) => ({ value: p, label: POSITION_LABELS[p], icon: POSITION_DIAGRAMS[p] }));

function sketchedBendPanel(host: PanelHost, f: SketchedBendFeature): HTMLElement[] {
  return [
    lineSection(host, f),
    section(
      'Biegung',
      numberField('Biegewinkel', () => f.angle, (v) => ((f.angle = v), host.changed()), { unit: '°', gt: 0, max: 179.99 }),
      checkField('Richtung umkehren', () => f.reverse, (v) => ((f.reverse = v), host.changed())),
    ),
    section('Biegeposition', segField(bendPositions(), () => f.position, (v) => ((f.position = v), host.changed()), 'Biegeposition')),
    overridesSection(host, host.doc.sheet, () => f.overrides, (o) => (f.overrides = o), { radius: true, relief: false }),
  ];
}

function jogPanel(host: PanelHost, f: JogFeature): HTMLElement[] {
  return [
    lineSection(host, f),
    section(
      'Knickversatz',
      numberField('Versatz', () => f.offset, (v) => ((f.offset = v), host.changed()), { unit: 'mm', gt: 0 }),
      wideSelect(
        (['outside', 'inside', 'overall'] as const).map((k) => [k, JOG_DIM_LABELS[k]] as [typeof k, string]),
        () => f.dimension,
        (v) => ((f.dimension = v), host.changed()),
        'Bemaßung des Versatzes',
      ),
      checkField('Projizierte Länge fixieren', () => f.fixedProjectedLength, (v) => ((f.fixedProjectedLength = v), host.changed())),
    ),
    section(
      'Knick',
      numberField('Knickwinkel', () => f.angle, (v) => ((f.angle = v), host.changed()), { unit: '°', gt: 0, max: 179.99 }),
      checkField('Richtung umkehren', () => f.reverse, (v) => ((f.reverse = v), host.changed())),
    ),
    section('Knickposition', segField(bendPositions(), () => f.position, (v) => ((f.position = v), host.changed()), 'Knickposition')),
    overridesSection(host, host.doc.sheet, () => f.overrides, (o) => (f.overrides = o), { radius: true, relief: false }),
  ];
}

function cutPanel(host: PanelHost, f: CutFeature): HTMLElement[] {
  const shapeFields: HTMLElement[] = [];
  if (f.shape === 'circle') {
    shapeFields.push(numberField('Durchmesser', () => f.diameter, (v) => ((f.diameter = v), host.changed()), { unit: 'mm', gt: 0 }));
  } else {
    shapeFields.push(numberField(f.shape === 'slot' ? 'Länge' : 'Breite', () => f.width, (v) => ((f.width = v), host.changed()), { unit: 'mm', gt: 0 }));
    shapeFields.push(numberField(f.shape === 'slot' ? 'Breite' : 'Höhe', () => f.height, (v) => ((f.height = v), host.changed()), { unit: 'mm', gt: 0 }));
    shapeFields.push(numberField('Drehung', () => f.angle, (v) => ((f.angle = v), host.changed()), { unit: '°' }));
  }
  const ref = f.edge.tag
    ? h('div', { class: 'picklist' }, h('div', { class: 'chip' }, h('span', {}, host.edgeLabel(f.edge))))
    : h('div', { class: 'picklist empty' }, 'Auf eine Fläche klicken');
  return [
    section(
      'Lage',
      ref,
      h('p', { class: 'note' }, 'Ein Klick auf die Fläche setzt die Mitte. Gemessen wird von der nächsten Kante.'),
      numberField('Entlang der Kante', () => f.u, (v) => ((f.u = v), host.changed()), { unit: 'mm' }),
      numberField('Abstand zur Kante', () => f.v, (v) => ((f.v = v), host.changed()), { unit: 'mm' }),
    ),
    section(
      'Form',
      segField(
        [
          { value: 'circle' as const, label: 'Rund' },
          { value: 'rect' as const, label: 'Rechteck' },
          { value: 'slot' as const, label: 'Langloch' },
        ],
        () => f.shape,
        (v) => {
          f.shape = v;
          host.changed();
          host.restructure();
        },
        'Form der Bohrung',
      ),
      ...shapeFields,
    ),
    section(
      'Muster entlang der Kante',
      numberField('Anzahl', () => f.count, (v) => ((f.count = v), host.changed()), { min: 1, max: 200, integer: true, digits: 0 }),
      numberField('Abstand', () => f.spacing, (v) => ((f.spacing = v), host.changed()), { unit: 'mm', gt: 0 }),
    ),
  ];
}

function baseFlangePanel(host: PanelHost, f: BaseFlangeFeature): HTMLElement[] {
  const parts: HTMLElement[] = [
    section(
      'Skizze',
      h('div', { class: 'infobox' }, f.closed ? `Geschlossenes Profil mit ${f.points.length} Ecken` : `Offenes Profil mit ${f.points.length - 1} Segmenten`),
      h('div', { class: 'btnrow' }, h('button', { type: 'button', class: 'btn', onclick: () => host.openSketch() }, svgIcon(ICONS.sketch), 'Skizze bearbeiten')),
      selectField(
        'Skizzenebene',
        (['top', 'front', 'right'] as const).map((p) => [p, PLANE_LABELS[p]] as [typeof p, string]),
        () => f.plane,
        (v) => ((f.plane = v), host.changed()),
      ),
    ),
  ];
  if (f.closed) {
    parts.push(section('Blechdicke', checkField('Dicke in Gegenrichtung', () => f.reverse, (v) => ((f.reverse = v), host.changed())), h('p', { class: 'note' }, `Dicke ${num(host.doc.sheet.thickness)} mm, einstellbar unter „Blech“.`)));
  } else {
    parts.push(
      section(
        'Tiefe',
        numberField('Tiefe', () => f.depth, (v) => ((f.depth = v), host.changed()), { unit: 'mm', gt: 0 }),
        checkField('Mittelebene', () => f.midPlane, (v) => ((f.midPlane = v), host.changed())),
        checkField('Material auf der anderen Linienseite', () => f.reverse, (v) => ((f.reverse = v), host.changed())),
        h('p', { class: 'note' }, 'Die Skizzenlinie ist eine Blechoberfläche, an den Ecken entstehen Biegungen mit dem Biegeradius.'),
      ),
    );
    parts.push(overridesSection(host, host.doc.sheet, () => f.overrides, (o) => (f.overrides = o), { radius: true, relief: false }));
  }
  return parts;
}

export function featurePanel(host: PanelHost, f: Feature): HTMLElement[] {
  switch (f.type) {
    case 'baseFlange':
      return baseFlangePanel(host, f);
    case 'edgeFlange':
      return edgeFlangePanel(host, f);
    case 'hem':
      return hemPanel(host, f);
    case 'sketchedBend':
      return sketchedBendPanel(host, f);
    case 'jog':
      return jogPanel(host, f);
    case 'cut':
      return cutPanel(host, f);
    case 'sketchCut':
      return sketchCutPanel(host, f);
    case 'tab':
      return tabPanel(host, f);
    case 'closedCorner':
      return closedCornerPanel(host, f);
    case 'breakCorner':
      return breakCornerPanel(host, f);
  }
}

export function sheetPanel(host: PanelHost, s: SheetSettings): HTMLElement[] {
  const matIndex = MATERIALS.findIndex((m) => m.name === s.material.name);
  return [
    section(
      'Material',
      selectField(
        'Werkstoff',
        [...MATERIALS.map((m, i) => [String(i), m.name] as [string, string]), ['custom', 'Eigener Werkstoff']],
        () => (matIndex >= 0 ? String(matIndex) : 'custom'),
        (v) => {
          if (v === 'custom') s.material = { name: 'Eigener Werkstoff', density: s.material.density };
          else s.material = { ...MATERIALS[Number(v)] };
          host.changed();
          host.restructure();
        },
      ),
      numberField('Dichte', () => s.material.density, (v) => ((s.material = { name: s.material.name, density: v }), host.changed()), { unit: 'g/cm³', gt: 0 }),
    ),
    section(
      'Blechparameter',
      numberField('Blechdicke', () => s.thickness, (v) => ((s.thickness = v), host.changed()), { unit: 'mm', gt: 0 }),
      numberField('Biegeradius', () => s.radius, (v) => ((s.radius = v), host.changed()), { unit: 'mm', min: 0 }),
    ),
    section(
      'Biegezugabe',
      ...calcFields(host, () => s.calc, (c) => (s.calc = c)),
      h(
        'p',
        { class: 'note' },
        'Faustwerte für den K-Faktor: 0,33 bei R < T, 0,4 bei R ≈ T bis 2·T, 0,5 bei R > 2·T. Genauer sind gemessene Werte der eigenen Abkantpresse.',
      ),
    ),
    section(
      'Automatischer Freistich',
      selectField('Freistichtyp', RELIEF_LABELS, () => s.relief.type, (t) => {
        s.relief.type = t;
        host.changed();
        host.restructure();
      }),
      s.relief.type !== 'tear'
        ? numberField('Verhältnis zur Dicke', () => s.relief.ratio, (v) => ((s.relief.ratio = v), host.changed()), { min: 0.05, max: 2 })
        : null,
      h('p', { class: 'note' }, 'Breite und Überstand des Freistichs = Verhältnis × Blechdicke.'),
    ),
  ];
}
