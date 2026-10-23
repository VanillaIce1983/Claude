/* PDFwerk – PDF-Viewer und -Editor im Browser.
   Anzeige mit pdf.js, Bearbeitung mit pdf-lib. Alles läuft lokal im Browser, keine Datei verlässt den Rechner. */
'use strict';

const { PDFDocument, rgb, degrees, StandardFonts, BlendMode, PDFName, PDFString, PDFHexString, PDFArray, PDFDict, PDFRef } = PDFLib;
// Die Desktop-Fassung bringt den Worker als eigene Datei mit, im Browser kommt er vom CDN oder ist eingebettet
pdfjsLib.GlobalWorkerOptions.workerSrc = window.PDFWERK_WORKER_SRC || 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
// Schnittstelle zur Desktop-Hülle (Electron, siehe desktop/host-api.d.ts); im Browser null
const HOST = window.pdfwerkHost || null;

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const h = (tag, attrs = {}, ...kids) => {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'style') el.style.cssText = v;
    else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k === 'html') el.innerHTML = v;
    else el.setAttribute(k, v === true ? '' : v);
  }
  for (const kid of kids.flat()) if (kid != null && kid !== false) el.append(kid.nodeType ? kid : document.createTextNode(kid));
  return el;
};
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const uid = () => Math.random().toString(36).slice(2, 10);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fmtBytes = (n) => n < 1024 ? n + ' B' : n < 1048576 ? (n / 1024).toFixed(1).replace('.', ',') + ' KB' : (n / 1048576).toFixed(2).replace('.', ',') + ' MB';
const PT_PER_MM = 72 / 25.4;
const DPR = () => Math.min(window.devicePixelRatio || 1, 2.5);
const baseName = (n) => (n || 'Dokument').replace(/\.pdf$/i, '');
const MAC = HOST ? HOST.platform === 'darwin' : /Mac/.test(navigator.platform);
// Felder, in denen getippt wird; Auswahllisten, Regler und Schalter zählen nicht dazu
const isTextField = (el) => !!el && (el.isContentEditable || el.tagName === 'TEXTAREA'
  || (el.tagName === 'INPUT' && !/^(range|checkbox|radio|button|color|file|submit|reset|image)$/i.test(el.type)));
const hexToRgb = (hex) => { const n = parseInt(hex.slice(1), 16); return [(n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255]; };
const pdfColor = (hex) => rgb(...hexToRgb(hex));

const ICON = {
  select: '<path d="M5 3l14 8-6 2-3 6z"/>',
  hand: '<path d="M8 13V5.5a1.5 1.5 0 0 1 3 0V11m0-6.5a1.5 1.5 0 0 1 3 0V11m0-5a1.5 1.5 0 0 1 3 0v8a7 7 0 0 1-7 7h-1a6 6 0 0 1-5-3l-2.5-4.5a1.5 1.5 0 0 1 2.6-1.5L8 15"/>',
  highlight: '<path d="m9 15-3 3H3l2-2 1-4 9-9 4 4-9 9z"/><path d="M14 21h7" stroke="#e8c800" stroke-width="3"/>',
  ink: '<path d="M3 17c3-6 5-8 7-6s-1 6 2 7 5-6 9-9"/>',
  text: '<path d="M5 6V4h14v2M12 4v16M9 20h6"/>',
  note: '<path d="M4 4h16v12H9l-5 4z"/><path d="M8 9h8M8 12h5"/>',
  rect: '<rect x="4" y="6" width="16" height="12" rx="1"/>',
  ellipse: '<ellipse cx="12" cy="12" rx="8" ry="6"/>',
  line: '<path d="M5 19 19 5"/>',
  arrow: '<path d="M5 19 19 5M10 5h9v9"/>',
  whiteout: '<rect x="4" y="7" width="16" height="10" rx="1" fill="currentColor" fill-opacity=".15"/><path d="M8 12h8"/>',
  redact: '<rect x="4" y="8" width="16" height="8" fill="currentColor"/>',
  edittext: '<path d="M4 20h4L19 9l-4-4L4 16z"/><path d="M13 7l4 4"/>',
  image: '<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="9" cy="10" r="2"/><path d="m21 16-5-5-9 8"/>',
  sign: '<path d="M3 17c2 0 3-6 5-6s0 6 2 6 3-4 4-4 1 3 3 3 2-1 4-1"/><path d="M3 21h18"/>',
  stamp: '<path d="M9 3h6v5l3 3H6l3-3zM5 14h14v3H5zM7 20h10"/>',
  rotL: '<path d="M4 4v6h6"/><path d="M4.5 14a8 8 0 1 0 2-8.5L4 10"/>',
  rotR: '<path d="M20 4v6h-6"/><path d="M19.5 14a8 8 0 1 1-2-8.5L20 10"/>',
  trash: '<path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V4H4v12h4"/>',
  merge: '<path d="M6 3v6a6 6 0 0 0 6 6h0a6 6 0 0 1 6 6M18 3v6a6 6 0 0 1-6 6"/>',
  split: '<path d="M12 3v18M4 7l4 5-4 5M20 7l-4 5 4 5"/>',
  extract: '<rect x="4" y="3" width="11" height="15" rx="1"/><path d="M13 13h8m-3-3 3 3-3 3"/>',
  compress: '<path d="M4 9h16M4 15h16M12 3v4l-2-2m2 2 2-2M12 21v-4l-2 2m2-2 2 2"/>',
  toimg: '<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="9" cy="10" r="2"/><path d="m21 16-5-5-9 8"/>',
  fromimg: '<path d="M6 2.5h8l5 5v14H6z"/><path d="m9 17 3-3 3 3"/><path d="M12 14v6" />',
  watermark: '<path d="M12 3s6 7 6 11a6 6 0 0 1-12 0c0-4 6-11 6-11z"/>',
  numbers: '<path d="M5 4h14v16H5z"/><path d="M10 16h4M12 16v-6l-2 1"/>',
  crop: '<path d="M6 2v16h16M2 6h16v16"/>',
  meta: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.5"/>',
  form: '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M7 9h10M7 13h6"/><rect x="14" y="12" width="4" height="3"/>',
  gray: '<circle cx="12" cy="12" r="9"/><path d="M12 3a9 9 0 0 1 0 18z" fill="currentColor"/>',
  blank: '<path d="M6 2.5h8l5 5v14H6z"/><path d="M10 13l4 4m0-4-4 4"/>',
  txt: '<path d="M6 2.5h8l5 5v14H6z"/><path d="M9 12h6M9 15h6M9 18h4"/>',
  reverse: '<path d="M7 4v16m0 0-3-3m3 3 3-3M17 20V4m0 0-3 3m3-3 3 3"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  present: '<rect x="3" y="4" width="18" height="12" rx="1"/><path d="M12 16v4M8 20h8"/>',
  lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
  flatten: '<path d="M3 12h18M6 8l6-5 6 5M6 16l6 5 6-5"/>',
};
const svg = (name, extra = '') => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" ${extra}>${ICON[name] || ''}</svg>`;

/* ---------- Zustand ---------- */
const S = {
  bytes: null,          // Uint8Array – aktueller Dokumentstand (ohne eingebrannte Anmerkungen)
  name: 'Dokument.pdf',
  path: null,           // Speicherort auf der Festplatte (nur Desktop)
  pristine: false,      // Beispieldokument, noch unberührt: Desktop darf es durch eine geöffnete Datei ersetzen
  pdf: null,            // pdf.js-Dokument
  pages: [],            // {w,h,rot} in pt bei Maßstab 1 inkl. Seitendrehung
  ann: [],              // Anmerkungen je Seite (PDF-Benutzerkoordinaten)
  scale: 1, zoom: 'fitw',
  cur: 0, mode: 'read', tool: 'select',
  color: '#d0312d', width: 2, fontSize: 14, font: 'Helvetica', hlColor: '#ffe14d',
  history: [], future: [],
  dirty: false, version: 0,
  opened: 0,            // zählt frisch geöffnete Dokumente (ein laufendes Speichern erkennt so ein anderes Dokument)
  textCache: [],
  search: { q: '', hits: [], idx: -1 },
  selected: null,
  spread: false, night: false,
  pendingImage: null,   // {dataUrl,w,h} für Bild/Unterschrift/Stempel
  orgSel: new Set(),
  downloads: null,
};
const views = [];       // je Seite: {div, pdfC, ovC, tl, scale, task, vp}
const thumbCache = new Map();

/* ---------- Kleinigkeiten der Oberfläche ---------- */
function status(msg) { $('#statusMsg').textContent = msg; }
let toastTimer;
function toast(msg, ms = 2600) {
  $('.toast')?.remove();
  const t = h('div', { class: 'toast', role: 'status' }, msg);
  document.body.append(t);
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.remove(), ms);
}
function busy(msg) { status(msg); document.body.style.cursor = 'progress'; }
function idle(msg = 'Bereit') { status(msg); document.body.style.cursor = ''; }

function dialog({ title, body, ok = 'OK', cancel = 'Abbrechen', wide = false, onOk, extraButtons = [] }) {
  return new Promise((resolve) => {
    const close = (v) => { bd.remove(); document.removeEventListener('keydown', onKey); resolve(v); };
    const okBtn = h('button', { class: 'btn primary', type: 'submit' }, ok);
    const form = h('form', { class: 'dlg', style: wide ? 'width:min(760px,100%)' : '' },
      h('header', {}, h('h3', {}, title), h('button', { class: 'btn icon', type: 'button', title: 'Schließen', html: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M6 6l12 12M18 6 6 18"/></svg>', onclick: () => close(null) })),
      h('div', { class: 'body' }, body),
      h('footer', {}, ...extraButtons, cancel ? h('button', { class: 'btn outline', type: 'button', onclick: () => close(null) }, cancel) : null, ok ? okBtn : null));
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      if (!onOk) return close(true);
      okBtn.disabled = true;
      try { const r = await onOk(form); if (r !== false) close(r ?? true); }
      catch (err) { console.error(err); toast('Fehler: ' + (err.message || err)); }
      finally { okBtn.disabled = false; }
    });
    const bd = h('div', { class: 'backdrop', onmousedown: (e) => { if (e.target === bd) close(null); } }, form);
    const onKey = (e) => { if (e.key === 'Escape') close(null); };
    document.addEventListener('keydown', onKey);
    document.body.append(bd);
    setTimeout(() => form.querySelector('input:not([type=hidden]),select,textarea')?.focus(), 30);
  });
}
const field = (label, input) => h('label', { class: 'field' }, h('span', {}, label), input);
const radios = (name, opts, val) => h('div', { class: 'radios' }, opts.map(([v, l]) => h('label', {}, h('input', { type: 'radio', name, value: v, checked: v === val }), l)));
const checkbox = (name, label, checked) => h('label', { class: 'check' }, h('input', { type: 'checkbox', name, checked }), label);

/* ---------- Laden ---------- */
async function loadPdfJs(bytes, password) {
  const task = pdfjsLib.getDocument({ data: bytes.slice(), password, isEvalSupported: false });
  return task.promise;
}

const UNLOCKED_NOTE = 'Geschützte PDF: Bearbeitet wird eine entsperrte Kopie aus Seitenbildern (ohne Text). Das Original bleibt unverändert, Speichern legt eine neue Datei an.';
async function openBytes(bytes, name, { keepAnn = false, keepPos = false, fresh = true, path } = {}) {
  let pdf, unlocked = false;
  try { pdf = await loadPdfJs(bytes); }
  catch (e) {
    if (e?.name === 'PasswordException') {
      const pw = await askPassword(name);
      if (pw == null) return false;
      try { pdf = await loadPdfJs(bytes, pw); }
      catch { toast('Falsches Passwort.'); return false; }
      bytes = await unlockByRaster(pdf, name); unlocked = true;
      pdf = await loadPdfJs(bytes);
      toast(UNLOCKED_NOTE, 6000);
    } else { toast('Die Datei ist keine lesbare PDF.'); console.error(e); return false; }
  }
  // pdf-lib muss die Datei ebenfalls lesen können, sonst gehen Bearbeitungen schief
  let notes = [];
  try {
    const lib = await PDFDocument.load(bytes, { updateMetadata: false });
    if (!keepAnn) { notes = takeNotes(lib); if (notes.length) bytes = await lib.save({ updateFieldAppearances: false }); }
  }
  catch (e) {
    if (/encrypted/i.test(e.message)) {
      toast(UNLOCKED_NOTE, 6000);
      bytes = await unlockByRaster(pdf, name); unlocked = true;
      pdf = await loadPdfJs(bytes);
    } else console.warn('pdf-lib:', e);
  }
  // Die entsperrte Kopie ist nicht das Original: eigener Name, kein Pfad, Speichern fragt nach dem Ort
  if (unlocked && fresh) { name = `${baseName(name)} (entsperrt).pdf`; path = null; }
  const oldPdf = S.pdf;
  S.pdf = pdf; S.bytes = bytes; S.name = name || S.name; S.version++;
  S.pages = [];
  for (let i = 1; i <= pdf.numPages; i++) {
    const p = await pdf.getPage(i);
    const vp = p.getViewport({ scale: 1 });
    S.pages.push({ w: vp.width, h: vp.height, rot: p.rotate, vp1: vp });
  }
  if (!keepAnn) { S.ann = S.pages.map(() => []); notes.forEach((n) => S.ann[n.p]?.push(n.a)); }
  while (S.ann.length < S.pages.length) S.ann.push([]);
  S.ann.length = S.pages.length;
  S.textCache = [];
  S.search.hits = []; S.search.idx = -1;
  S.selected = null;
  clearThumbs();
  if (fresh) { S.history = []; S.future = []; S.dirty = false; S.pristine = false; S.orgSel.clear(); S.path = path ?? null; S.opened++; }
  if (!keepPos) S.cur = 0;
  S.cur = clamp(S.cur, 0, S.pages.length - 1);
  oldPdf?.destroy();
  syncDoc();
  $('#statusSize').textContent = fmtBytes(bytes.length);
  buildViewer(keepPos);
  renderSide();
  if (S.mode === 'pages') renderOrganizer();
  if (S.search.q) runSearch(S.search.q, true);
  updateUndoButtons();
  return true;
}

// Vorhandene Haftnotizen (Text-Anmerkungen) übernehmen, damit sie hier bearbeitbar sind. Beim Speichern werden sie wieder geschrieben.
// Das Notizobjekt bleibt in der Datei (orig): Autor, Datum, Kennung, Antworten und Popup gehen so nicht verloren.
// Antworten und Statusvermerke (IRT) hängen an ihrer Notiz; versteckte Notizen und Antworten auf andere Anmerkungen bleiben unberührt.
const NOTE_META = ['T', 'NM', 'Subj', 'CreationDate', 'M'];
function takeNotes(lib) {
  const out = [];
  const dictOf = (ref) => { const d = lib.context.lookup(ref); return d instanceof PDFDict ? d : null; };
  const sub = (d) => d?.get(PDFName.of('Subtype'))?.toString();
  lib.getPages().forEach((page, p) => {
    const annots = page.node.Annots();
    if (!annots) return;
    const notes = new Map();   // Ref der übernommenen Notiz -> Eintrag
    for (let k = 0; k < annots.size(); k++) {
      const ref = annots.get(k), d = dictOf(ref);
      if (!(ref instanceof PDFRef) || sub(d) !== '/Text' || d.has(PDFName.of('IRT'))) continue;
      const f = d.lookup(PDFName.of('F'));
      if (f?.asNumber && f.asNumber() & 2) continue;   // versteckt
      const rect = d.lookup(PDFName.of('Rect'));
      const contents = d.lookup(PDFName.of('Contents'));
      const c = d.lookup(PDFName.of('C'));
      const r = rect ? rect.asArray().map((n) => n.asNumber()) : [0, 0, 18, 18];
      let color = '#f5c400';
      try { if (c && c.size() === 3) color = '#' + c.asArray().map((n) => Math.round(n.asNumber() * 255).toString(16).padStart(2, '0')).join(''); } catch { /* Standardfarbe */ }
      const a = { type: 'note', id: uid(), x: Math.min(r[0], r[2]), y: Math.max(r[1], r[3]), text: contents?.decodeText?.() || '', color };
      const meta = {};
      for (const key of NOTE_META) { const v = d.lookup(PDFName.of(key)); if (v?.decodeText) meta[key] = v.decodeText(); }
      const icon = d.lookup(PDFName.of('Name'));
      if (icon instanceof PDFName) meta.Name = icon.asString().replace(/^\//, '');
      if (f?.asNumber) meta.F = f.asNumber();
      a.orig = { ref: [ref.objectNumber, ref.generationNumber], x: a.x, y: a.y, text: a.text, color, meta, replies: [] };
      notes.set(ref, a);
      out.push({ p, a });
    }
    if (!notes.size) return;
    // Antworten (auch Antworten auf Antworten) der übernommenen Notizen finden
    const rootOf = (ref) => {
      for (let n = 0; n < 20 && ref; n++) {
        if (notes.has(ref)) return notes.get(ref);
        const irt = dictOf(ref)?.get(PDFName.of('IRT'));
        ref = irt instanceof PDFRef ? irt : null;
      }
      return null;
    };
    const taken = new Set(notes.keys()), drop = [];
    for (let k = 0; k < annots.size(); k++) {
      const ref = annots.get(k), d = dictOf(ref);
      if (taken.has(ref)) { drop.push(k); continue; }
      if (!(ref instanceof PDFRef) || !d?.has(PDFName.of('IRT')) || sub(d) === '/Popup') continue;
      const a = rootOf(ref);
      if (a) { a.orig.replies.push([ref.objectNumber, ref.generationNumber]); taken.add(ref); drop.push(k); }
    }
    // nur die Popups der übernommenen Notizen und Antworten entfernen, sie kommen beim Speichern zurück
    for (let k = 0; k < annots.size(); k++) {
      const d = dictOf(annots.get(k));
      if (sub(d) === '/Popup' && taken.has(d.get(PDFName.of('Parent')))) drop.push(k);
    }
    for (const k of drop.sort((x, y) => y - x)) annots.remove(k);
  });
  return out;
}
function askPassword(name) {
  const inp = h('input', { type: 'password', name: 'pw', autocomplete: 'off' });
  return dialog({ title: 'Passwort erforderlich', ok: 'Öffnen',
    body: [h('p', { style: 'margin:0' }, `„${name}“ ist mit einem Passwort geschützt.`), field('Passwort', inp)],
    onOk: () => inp.value });
}

// Geschützte PDFs kann pdf-lib nicht schreiben. Wir bauen sie als Bildseiten neu auf.
async function unlockByRaster(pdf, name) {
  busy('Entsperre ' + name + ' …');
  const out = await PDFDocument.create();
  for (let i = 1; i <= pdf.numPages; i++) {
    const page = await pdf.getPage(i);
    const vp1 = page.getViewport({ scale: 1 });
    const c = await renderToCanvas(page, 150 / 72);
    const jpg = await out.embedJpg(await canvasBytes(c, 'image/jpeg', 0.9));
    const p = out.addPage([vp1.width, vp1.height]);
    p.drawImage(jpg, { x: 0, y: 0, width: vp1.width, height: vp1.height });
  }
  idle();
  return await out.save();
}

async function renderToCanvas(page, scale, { gray = false, annots = true } = {}) {
  const vp = page.getViewport({ scale });
  const c = document.createElement('canvas');
  c.width = Math.ceil(vp.width); c.height = Math.ceil(vp.height);
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, c.width, c.height);
  await page.render({ canvasContext: ctx, viewport: vp, annotationMode: annots ? pdfjsLib.AnnotationMode.ENABLE : pdfjsLib.AnnotationMode.DISABLE }).promise;
  if (gray) {
    const d = ctx.getImageData(0, 0, c.width, c.height), a = d.data;
    for (let i = 0; i < a.length; i += 4) { const g = a[i] * 0.299 + a[i + 1] * 0.587 + a[i + 2] * 0.114; a[i] = a[i + 1] = a[i + 2] = g; }
    ctx.putImageData(d, 0, 0);
  }
  return c;
}
const canvasBlob = (c, type = 'image/png', q) => new Promise((r) => c.toBlob(r, type, q));
async function canvasBytes(c, type, q) { return new Uint8Array(await (await canvasBlob(c, type, q)).arrayBuffer()); }

/* ---------- Dokumentzustand ---------- */
function setDirty(v) {
  S.dirty = v;
  if (v) S.pristine = false;
  syncDoc();
}
function syncDoc() {
  $('#docName').textContent = S.name;
  $('#docName').title = S.path || S.name;
  if (HOST) {
    document.title = `${S.name}${S.dirty ? ' (geändert)' : ''} – PDFwerk`;
    HOST.setDocument({ name: S.name, path: S.path, dirty: S.dirty, pristine: S.pristine && !S.dirty });
  }
}

/* ---------- Verlauf ---------- */
function cloneAnn(a = S.ann) { return JSON.parse(JSON.stringify(a)); }
// Ein Verlaufsschritt merkt sich auch Name und Speicherort: Rückgängig nach „Zusammenfügen“ zeigt wieder auf die alte Datei
// Rückgängig merkt sich ganze Dokumentfassungen; bei großen PDFs begrenzt der Speicher, nicht die Anzahl
const HISTORY_BYTES = 400 * 1048576;
function historyBytes() {
  const seen = new Set([S.bytes]);
  let n = 0;
  for (const e of [...S.history, ...S.future]) if (!seen.has(e.bytes)) { seen.add(e.bytes); n += e.bytes.length; }
  return n;
}
function snapshot() {
  S.history.push({ bytes: S.bytes, ann: cloneAnn(), name: S.name, path: S.path });
  if (S.history.length > 40) S.history.shift();
  while (S.history.length > 1 && historyBytes() > HISTORY_BYTES) S.history.shift();
  S.future = [];
  setDirty(true);
  updateUndoButtons();
}
async function restore(st, pushTo) {
  pushTo.push({ bytes: S.bytes, ann: cloneAnn(), name: S.name, path: S.path });
  const sameBytes = st.bytes === S.bytes;
  S.ann = st.ann; S.name = st.name; S.path = st.path; S.selected = null;
  if (sameBytes) { redrawAllOverlays(); renderSide(); }
  else await openBytes(st.bytes, st.name, { keepAnn: true, keepPos: true, fresh: false });
  setDirty(true);
  updateUndoButtons();
}
// Nach dem Speichern unter neuem Namen gehört der ganze Verlauf zur neuen Datei
function rebaseHistory() { for (const e of [...S.history, ...S.future]) { e.name = S.name; e.path = S.path; } }
async function undo() { if (S.history.length) await restore(S.history.pop(), S.future); }
async function redo() { if (S.future.length) await restore(S.future.pop(), S.history); }
function updateUndoButtons() {
  $('#btnUndo').disabled = !S.history.length;
  $('#btnRedo').disabled = !S.future.length;
}

/* ---------- Ansicht ---------- */
let io;
function computeScale() {
  const vw = $('#viewer').clientWidth - 40, vh = $('#viewer').clientHeight - 40;
  const maxW = Math.max(...S.pages.map((p) => p.w), 1);
  const cur = S.pages[S.cur] || S.pages[0] || { w: 600, h: 800 };
  const cols = S.spread ? 2 : 1;
  if (S.zoom === 'fitw') return clamp((vw - (cols - 1) * 16) / cols / maxW, 0.1, 6);
  if (S.zoom === 'fitp') return clamp(Math.min((vw - (cols - 1) * 16) / cols / cur.w, vh / cur.h), 0.1, 6);
  return S.zoom;
}

// Gerendert bleiben nur Seiten nahe am sichtbaren Bereich; jede Seite belegt sonst ihre volle Bildschirmauflösung,
// bei langen Dokumenten Gigabytes, und das System friert unter dem Speicherdruck ein.
const MAX_LIVE = 12;                 // höchstens so viele Seiten gleichzeitig gerendert
const MAX_CANVAS_PX = 16777216;      // 4096 × 4096: darüber wird die Auflösung einer Seite begrenzt
let ioFar;
function canvasDpr(vp) {
  const d = DPR();
  return vp.width * vp.height * d * d > MAX_CANVAS_PX ? Math.sqrt(MAX_CANVAS_PX / (vp.width * vp.height)) : d;
}
function releasePage(i) {
  const v = views[i];
  if (!v || (!v.live && !v.rendering)) return;
  v.task?.cancel();
  v.live = false; v.scale = 0; v.rendering = 0;
  v.pdfC.width = v.pdfC.height = 0;
  v.ovC.width = v.ovC.height = 0;
  v.tl.textContent = '';
}
// nie mehr als MAX_LIVE Seiten gerendert halten, die vom Lesepunkt entferntesten zuerst freigeben
function trimLive() {
  const spare = views.map((v, i) => i).filter((i) => views[i].live && !views[i].visible);
  const total = views.filter((v) => v.live).length;
  spare.sort((a, b) => Math.abs(b - S.cur) - Math.abs(a - S.cur));
  for (let k = 0; k < total - MAX_LIVE && k < spare.length; k++) releasePage(spare[k]);
}

function buildViewer(keepPos) {
  const pagesEl = $('#pages');
  const viewer = $('#viewer');
  const anchor = keepPos ? S.cur : 0;
  io?.disconnect();
  ioFar?.disconnect();
  views.forEach((v) => { v.task?.cancel(); v.pdfC.width = v.pdfC.height = 0; v.ovC.width = v.ovC.height = 0; });
  views.length = 0;
  pagesEl.textContent = '';
  pagesEl.classList.toggle('spread', S.spread);
  S.scale = computeScale();
  syncZoomSelect();
  io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      const i = +e.target.dataset.i;
      views[i].visible = e.isIntersecting;
      if (e.isIntersecting) renderPage(i);
    }
  }, { root: viewer, rootMargin: '600px 0px' });
  ioFar = new IntersectionObserver((entries) => {
    for (const e of entries) if (!e.isIntersecting) releasePage(+e.target.dataset.i);
  }, { root: viewer, rootMargin: '2400px' });
  S.pages.forEach((p, i) => {
    const div = h('div', { class: 'page', 'data-i': i });
    // ohne Größe anlegen: eine Leinwand hat sonst 300 × 150 Pixel, bei 1000 Seiten Hunderte MB
    const pdfC = h('canvas', { class: 'pdf', width: 0, height: 0 });
    const ovC = h('canvas', { class: 'ov', width: 0, height: 0 });
    const tl = h('div', { class: 'textLayer' });
    div.append(pdfC, tl, ovC, h('span', { class: 'plabel' }, String(i + 1)));
    pagesEl.append(div);
    views.push({ div, pdfC, ovC, tl, scale: 0, task: null, vp: null, visible: false, live: false });
    sizePage(i);
    attachPageEvents(i);
    io.observe(div);
    ioFar.observe(div);
  });
  applyToolClass();
  requestAnimationFrame(() => { if (views[anchor]) scrollToPage(anchor, false); updatePageIndicator(); });
}

function sizePage(i) {
  const p = S.pages[i], v = views[i];
  v.div.style.width = Math.floor(p.w * S.scale) + 'px';
  v.div.style.height = Math.floor(p.h * S.scale) + 'px';
  v.vp = p.vp1.clone({ scale: S.scale });
}

async function renderPage(i) {
  const v = views[i];
  if (!v || v.scale === S.scale || v.rendering === S.scale) return;
  v.rendering = S.scale;
  v.live = true;
  const ver = S.version;
  const page = await S.pdf.getPage(i + 1);
  if (ver !== S.version || v.rendering !== S.scale) return;
  const scale = S.scale;
  const vp = page.getViewport({ scale });
  const dpr = canvasDpr(vp);
  // in eine neue Leinwand rendern und diese einsetzen: kein Umkopieren, das alte Bild bleibt bis dahin sichtbar
  const off = h('canvas', { class: 'pdf' });
  off.width = Math.floor(vp.width * dpr); off.height = Math.floor(vp.height * dpr);
  const ctx = off.getContext('2d');
  v.task?.cancel();
  v.task = page.render({ canvasContext: ctx, viewport: vp, transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null, annotationMode: pdfjsLib.AnnotationMode.ENABLE });
  try { await v.task.promise; } catch (e) { off.width = off.height = 0; if (e?.name !== 'RenderingCancelledException') console.error(e); if (v.rendering === scale) v.rendering = 0; return; }
  if (ver !== S.version || scale !== S.scale || !v.live) { off.width = off.height = 0; return; }
  const old = v.pdfC;
  old.replaceWith(off);
  old.width = old.height = 0;
  v.pdfC = off;
  v.vp = vp; v.scale = scale; v.rendering = 0;
  drawOverlay(i);
  trimLive();
  // Textebene zum Markieren und Kopieren
  v.tl.textContent = '';
  v.tl.style.setProperty('--scale-factor', scale);
  try {
    const tc = await getText(i);
    if (ver !== S.version || scale !== S.scale) return;
    pdfjsLib.renderTextLayer({ textContentSource: tc, container: v.tl, viewport: vp, textDivs: [] });
  } catch (e) { console.warn(e); }
}

async function getText(i) {
  if (!S.textCache[i]) S.textCache[i] = S.pdf.getPage(i + 1).then((p) => p.getTextContent());
  return S.textCache[i];
}

function rezoom(z, focus) {
  S.zoom = z;
  const viewer = $('#viewer');
  const oldScale = S.scale;
  const newScale = computeScale();
  if (Math.abs(newScale - oldScale) < 1e-4) { syncZoomSelect(); return; }
  // Lesepunkt halten
  const fx = focus ? focus.x : viewer.clientWidth / 2, fy = focus ? focus.y : 0;
  const ratio = newScale / oldScale;
  const sx = (viewer.scrollLeft + fx) * ratio - fx, sy = (viewer.scrollTop + fy) * ratio - fy;
  S.scale = newScale;
  // unsichtbare Seiten freigeben; sichtbare zeigen ihr altes Bild gestreckt, bis sie neu gerendert sind
  views.forEach((v, i) => {
    if (!v.visible) releasePage(i);
    else { v.scale = 0; v.rendering = 0; v.task?.cancel(); v.tl.textContent = ''; v.ovC.width = v.ovC.height = 0; }
    sizePage(i);
  });
  viewer.scrollLeft = sx; viewer.scrollTop = sy;
  syncZoomSelect();
  clearTimeout(rezoom.t);
  rezoom.t = setTimeout(() => views.forEach((v, i) => v.visible && renderPage(i)), 60);
}
function syncZoomSelect() {
  const sel = $('#zoomSel');
  const opt = [...sel.options].find((o) => o.value === String(S.zoom));
  if (opt) sel.value = opt.value;
  else { const c = sel.querySelector('[value=custom]'); c.textContent = Math.round(S.scale * 100) + ' %'; sel.value = 'custom'; }
}
function zoomStep(dir, focus) {
  const steps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];
  const cur = S.scale;
  const next = dir > 0 ? steps.find((s) => s > cur + 0.01) ?? 5 : [...steps].reverse().find((s) => s < cur - 0.01) ?? 0.25;
  rezoom(next, focus);
}

function scrollToPage(i, smooth = true, yOffsetPt = null) {
  const v = views[i]; if (!v) return;
  const viewer = $('#viewer');
  let top = v.div.offsetTop - 12;
  if (yOffsetPt != null) top = v.div.offsetTop + yOffsetPt * S.scale - viewer.clientHeight / 3;
  viewer.scrollTo({ top, left: S.spread ? viewer.scrollLeft : v.div.offsetLeft - 20, behavior: smooth ? 'smooth' : 'auto' });
  S.cur = i; updatePageIndicator();
}
function updatePageIndicator() {
  $('#pageInput').value = S.pages.length ? S.cur + 1 : 0;
  $('#pageCount').textContent = '/ ' + S.pages.length;
  const p = S.pages[S.cur];
  if (p) $('#statusDim').textContent = `${(p.w / PT_PER_MM).toFixed(0)} × ${(p.h / PT_PER_MM).toFixed(0)} mm`;
  $$('.thumb').forEach((t) => t.classList.toggle('cur', +t.dataset.i === S.cur));
  const ct = $(`.thumb[data-i="${S.cur}"]`);
  if (ct && $('#side:not(.closed)')) { const sb = $('#sideBody'); if (ct.offsetTop < sb.scrollTop || ct.offsetTop > sb.scrollTop + sb.clientHeight - 60) sb.scrollTop = ct.offsetTop - 40; }
}
function onViewerScroll() {
  const viewer = $('#viewer');
  const mid = viewer.scrollTop + viewer.clientHeight * 0.35;
  let best = S.cur;
  for (let i = 0; i < views.length; i++) {
    const d = views[i].div;
    if (d.offsetTop <= mid && d.offsetTop + d.offsetHeight >= mid) { best = i; break; }
    if (d.offsetTop > mid) { best = Math.max(0, i - 1); break; }
  }
  if (best !== S.cur) { S.cur = best; updatePageIndicator(); }
}

/* ---------- Seitenleiste ---------- */
let sideTab = 'thumbs', thumbIO;
// Vorschaubild als PNG-Adresse: der Browser hält davon nur die komprimierte Fassung, nicht eine Leinwand je Seite
async function getThumb(i, width = 150) {
  const key = i + ':' + width + ':' + S.version;
  if (thumbCache.has(key)) return thumbCache.get(key);
  const pr = (async () => {
    const page = await S.pdf.getPage(i + 1);
    const vp1 = page.getViewport({ scale: 1 });
    const c = await renderToCanvas(page, (width * DPR()) / vp1.width);
    const blob = await canvasBlob(c, 'image/png');
    c.width = c.height = 0;
    return URL.createObjectURL(blob);
  })();
  thumbCache.set(key, pr);
  return pr;
}
function clearThumbs() {
  for (const pr of thumbCache.values()) pr.then((url) => URL.revokeObjectURL(url)).catch(() => {});
  thumbCache.clear();
}
const thumbImg = (url, width) => h('img', { src: url, alt: '', draggable: 'false', style: `width:${width}px;max-width:100%;box-shadow:var(--shadow);background:#fff;display:block` });
function renderSide() {
  $$('#sideTabs button').forEach((b) => b.classList.toggle('on', b.dataset.side === sideTab));
  const body = $('#sideBody');
  body.textContent = '';
  thumbIO?.disconnect();
  if (!S.pdf) return;
  if (sideTab === 'thumbs') {
    thumbIO = new IntersectionObserver((entries) => {
      for (const e of entries) {
        if (!e.isIntersecting || e.target.dataset.done) continue;
        e.target.dataset.done = 1;
        const i = +e.target.dataset.i, ver = S.version;
        // .catch: wird inzwischen ein anderes Dokument geöffnet, bricht pdf.js das Zeichnen ab
        getThumb(i, 130).then((url) => { if (ver === S.version) e.target.querySelector('.ph').replaceWith(thumbImg(url, 130)); }).catch(() => {});
      }
    }, { root: body, rootMargin: '300px' });
    S.pages.forEach((p, i) => {
      const ph = h('div', { class: 'ph', style: `width:130px;height:${Math.round(130 * p.h / p.w)}px;background:#fff;box-shadow:var(--shadow)` });
      const t = h('div', { class: 'thumb' + (i === S.cur ? ' cur' : ''), 'data-i': i, onclick: () => { if (S.mode === 'pages' || S.mode === 'tools') setMode('read'); scrollToPage(i); } }, ph, h('span', { class: 'num' }, String(i + 1)));
      body.append(t);
      thumbIO.observe(t);
    });
  } else if (sideTab === 'outline') {
    S.pdf.getOutline().then((ol) => {
      if (!ol?.length) { body.append(h('div', { class: 'empty' }, 'Dieses Dokument hat keine Lesezeichen.')); return; }
      const build = (items) => h('div', {}, items.map((it) => [
        h('button', { class: 'outline-item', onclick: () => goDest(it.dest) }, it.title),
        it.items?.length ? h('div', { class: 'outline-children' }, build(it.items)) : null]));
      body.append(build(ol));
    });
  } else {
    let n = 0;
    S.ann.forEach((list, i) => list.forEach((a) => {
      n++;
      body.append(h('div', { class: 'cmt', onclick: () => { setMode('comment'); S.selected = { p: i, id: a.id }; scrollToPage(i, true, S.pages[i].vp1.convertToViewportPoint(annBox(a)[0], annBox(a)[3])[1]); redrawAllOverlays(); } },
        h('b', {}, ANN_NAMES[a.type] || a.type, h('span', {}, 'S. ' + (i + 1))),
        a.text ? h('p', {}, a.text) : null));
    }));
    if (!n) body.append(h('div', { class: 'empty' }, 'Noch keine Anmerkungen. Unter „Bearbeiten“ markieren, zeichnen, Notizen und Text hinzufügen.'));
  }
}
async function goDest(dest) {
  try {
    if (typeof dest === 'string') dest = await S.pdf.getDestination(dest);
    if (!dest) return;
    const idx = await S.pdf.getPageIndex(dest[0]);
    if (S.mode !== 'read' && S.mode !== 'comment') setMode('read');
    const y = dest[1]?.name === 'XYZ' && dest[3] != null ? S.pages[idx].vp1.convertToViewportPoint(dest[2] || 0, dest[3])[1] : null;
    scrollToPage(idx, true, y);
  } catch (e) { console.warn(e); }
}

/* ---------- Anmerkungen ---------- */
const ANN_NAMES = { highlight: 'Markierung', underline: 'Unterstreichung', strike: 'Durchstreichung', ink: 'Freihand', text: 'Text', note: 'Notiz',
  rect: 'Rechteck', ellipse: 'Ellipse', line: 'Linie', arrow: 'Pfeil', whiteout: 'Abdeckung', redact: 'Schwärzung', image: 'Bild' };
const BOX_TYPES = new Set(['rect', 'ellipse', 'highlight', 'underline', 'strike', 'whiteout', 'redact', 'image']);
const AXES = { 0: { R: [1, 0], D: [0, -1] }, 90: { R: [0, 1], D: [1, 0] }, 180: { R: [-1, 0], D: [0, 1] }, 270: { R: [0, -1], D: [-1, 0] } };
const FONTS = {
  'Helvetica': ['Helvetica, Arial, sans-serif', '', 'Helvetica'], 'Helvetica-Bold': ['Helvetica, Arial, sans-serif', 'bold ', 'HelveticaBold'],
  'Times-Roman': ['"Times New Roman", Times, serif', '', 'TimesRoman'], 'Times-Bold': ['"Times New Roman", Times, serif', 'bold ', 'TimesRomanBold'],
  'Courier': ['"Courier New", Courier, monospace', '', 'Courier'], 'Courier-Bold': ['"Courier New", Courier, monospace', 'bold ', 'CourierBold'],
};
const FONT_LABELS = { 'Helvetica': 'Helvetica', 'Helvetica-Bold': 'Helvetica fett', 'Times-Roman': 'Times', 'Times-Bold': 'Times fett', 'Courier': 'Courier', 'Courier-Bold': 'Courier fett' };
const FONT_CSS = (px, f = 'Helvetica') => { const d = FONTS[f] || FONTS.Helvetica; return `${d[1]}${px}px ${d[0]}`; };
const measureCtx = document.createElement('canvas').getContext('2d');
function textDims(a) {
  measureCtx.font = FONT_CSS(a.size, a.font);
  const lines = (a.text || ' ').split('\n');
  const w = Math.max(...lines.map((l) => measureCtx.measureText(l).width), a.size * 0.5);
  return { w, h: lines.length * a.size * 1.2, lines };
}
function annBox(a) {
  if (a.type === 'ink') {
    const xs = a.pts.map((p) => p[0]), ys = a.pts.map((p) => p[1]);
    return [Math.min(...xs), Math.min(...ys), Math.max(...xs), Math.max(...ys)];
  }
  if (a.type === 'text') {
    const { w, h: hh } = textDims(a), ax = AXES[a.rot || 0];
    const xs = [a.x, a.x + ax.R[0] * w, a.x + ax.D[0] * hh, a.x + ax.R[0] * w + ax.D[0] * hh];
    const ys = [a.y, a.y + ax.R[1] * w, a.y + ax.D[1] * hh, a.y + ax.R[1] * w + ax.D[1] * hh];
    return [Math.min(...xs), Math.min(...ys), Math.max(...xs), Math.max(...ys)];
  }
  if (a.type === 'note') return [a.x, a.y - 18, a.x + 18, a.y];
  return [Math.min(a.x1, a.x2), Math.min(a.y1, a.y2), Math.max(a.x1, a.x2), Math.max(a.y1, a.y2)];
}
function toScr(i, x, y) { return views[i].vp.convertToViewportPoint(x, y); }
function toPdf(i, x, y) { return views[i].vp.convertToPdfPoint(x, y); }
function scrBox(i, a) {
  const b = annBox(a);
  const p1 = toScr(i, b[0], b[1]), p2 = toScr(i, b[2], b[3]);
  return [Math.min(p1[0], p2[0]), Math.min(p1[1], p2[1]), Math.max(p1[0], p2[0]), Math.max(p1[1], p2[1])];
}

function drawOverlay(i) {
  const v = views[i];
  if (!v?.vp) return;
  // Zeichenebene nur anlegen, wenn es auf einer gerenderten Seite etwas zu zeigen gibt
  const c = v.ovC;
  const need = v.live && v.scale === S.scale && (S.ann[i]?.length || drag?.page === i || S.selected?.p === i || S.search.hits.some((hit) => hit.p === i));
  if (!need) { if (c.width) c.width = c.height = 0; return; }
  const pd = canvasDpr(v.vp), cw = Math.floor(v.vp.width * pd), ch = Math.floor(v.vp.height * pd);
  if (c.width !== cw || c.height !== ch) { c.width = cw; c.height = ch; }
  const ctx = c.getContext('2d'), dpr = c.width / v.vp.width;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, c.width, c.height);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  for (const a of S.ann[i] || []) drawAnn(ctx, i, a);
  if (drag?.page === i && drag.preview) drawAnn(ctx, i, drag.preview);
  // Suchtreffer
  S.search.hits.forEach((hit, k) => {
    if (hit.p !== i) return;
    ctx.fillStyle = k === S.search.idx ? 'rgba(255,120,0,.45)' : 'rgba(255,200,0,.35)';
    for (const r of hit.rects) { const b = scrRect(i, r); ctx.fillRect(b[0], b[1], b[2] - b[0], b[3] - b[1]); }
  });
  // Auswahl
  if (S.selected?.p === i) {
    const a = S.ann[i].find((x) => x.id === S.selected.id);
    if (a) {
      const b = scrBox(i, a);
      ctx.save();
      ctx.strokeStyle = '#2c49b8'; ctx.lineWidth = 1; ctx.setLineDash([4, 3]);
      ctx.strokeRect(b[0] - 4, b[1] - 4, b[2] - b[0] + 8, b[3] - b[1] + 8);
      ctx.setLineDash([]);
      if (BOX_TYPES.has(a.type) || a.type === 'text') {
        ctx.fillStyle = '#fff'; ctx.strokeStyle = '#2c49b8'; ctx.lineWidth = 1.5;
        ctx.fillRect(b[2] + 1, b[3] + 1, 8, 8); ctx.strokeRect(b[2] + 1, b[3] + 1, 8, 8);
      }
      ctx.restore();
    }
  }
}
function screenDelta(i, a) { return ((S.pages[i].rot - (a.rot || 0)) % 360 + 360) % 360; }
function scrRect(i, r) { const p1 = toScr(i, r[0], r[1]), p2 = toScr(i, r[2], r[3]); return [Math.min(p1[0], p2[0]), Math.min(p1[1], p2[1]), Math.max(p1[0], p2[0]), Math.max(p1[1], p2[1])]; }
function redrawAllOverlays() { views.forEach((v, i) => v.vp && drawOverlay(i)); }

const imgCache = new Map();
function getImg(src) {
  if (imgCache.has(src)) return imgCache.get(src);
  const im = new Image(); im.onload = redrawAllOverlays; im.src = src; imgCache.set(src, im); return im;
}

function drawAnn(ctx, i, a) {
  const s = S.scale;
  ctx.save();
  ctx.lineCap = 'round'; ctx.lineJoin = 'round';
  const [x1, y1, x2, y2] = a.type === 'line' || a.type === 'arrow' ? [...toScr(i, a.x1, a.y1), ...toScr(i, a.x2, a.y2)] : [0, 0, 0, 0];
  switch (a.type) {
    case 'ink': {
      ctx.strokeStyle = a.color; ctx.lineWidth = a.width * s; ctx.globalAlpha = a.opacity ?? 1;
      ctx.beginPath();
      a.pts.forEach((p, k) => { const q = toScr(i, p[0], p[1]); k ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]); });
      if (a.pts.length === 1) { const q = toScr(i, ...a.pts[0]); ctx.lineTo(q[0] + 0.1, q[1]); }
      ctx.stroke(); break;
    }
    case 'line': case 'arrow': {
      ctx.strokeStyle = a.color; ctx.lineWidth = a.width * s;
      ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
      if (a.type === 'arrow') {
        const ang = Math.atan2(y2 - y1, x2 - x1), L = Math.max(10, a.width * 4) * s;
        ctx.beginPath();
        ctx.moveTo(x2 - L * Math.cos(ang - 0.45), y2 - L * Math.sin(ang - 0.45));
        ctx.lineTo(x2, y2);
        ctx.lineTo(x2 - L * Math.cos(ang + 0.45), y2 - L * Math.sin(ang + 0.45));
        ctx.stroke();
      }
      break;
    }
    case 'text': {
      const p = toScr(i, a.x, a.y);
      ctx.fillStyle = a.color; ctx.font = FONT_CSS(a.size * s, a.font); ctx.textBaseline = 'alphabetic';
      if (S.editing?.id === a.id) break;
      ctx.translate(p[0], p[1]); ctx.rotate(screenDelta(i, a) * Math.PI / 180);
      a.text.split('\n').forEach((l, k) => ctx.fillText(l, 0, a.size * s * (0.8 + k * 1.2)));
      break;
    }
    case 'note': {
      const b = scrBox(i, a);
      ctx.fillStyle = a.color || '#f5c400'; ctx.strokeStyle = 'rgba(0,0,0,.45)'; ctx.lineWidth = 1;
      const w = b[2] - b[0], hh = b[3] - b[1];
      ctx.beginPath(); ctx.rect(b[0], b[1], w, hh * 0.78); ctx.moveTo(b[0] + w * 0.25, b[1] + hh * 0.78); ctx.lineTo(b[0] + w * 0.2, b[3]); ctx.lineTo(b[0] + w * 0.5, b[1] + hh * 0.78);
      ctx.fill(); ctx.stroke();
      ctx.strokeStyle = 'rgba(0,0,0,.5)';
      for (let k = 1; k <= 3; k++) { ctx.beginPath(); ctx.moveTo(b[0] + w * 0.2, b[1] + hh * 0.18 * k); ctx.lineTo(b[2] - w * 0.2, b[1] + hh * 0.18 * k); ctx.stroke(); }
      break;
    }
    default: {
      const b = scrBox(i, a), w = b[2] - b[0], hh = b[3] - b[1];
      if (a.type === 'highlight') { ctx.globalCompositeOperation = 'multiply'; ctx.fillStyle = a.color; ctx.globalAlpha = 0.45; ctx.fillRect(b[0], b[1], w, hh); }
      else if (a.type === 'underline' || a.type === 'strike') {
        ctx.strokeStyle = a.color; ctx.lineWidth = Math.max(1, 1.2 * s);
        const d = screenDelta(i, a), horiz = d % 180 === 0;
        ctx.beginPath();
        if (horiz) { const yy = a.type === 'strike' ? b[1] + hh * 0.55 : d === 180 ? b[1] + 0.5 : b[3] - 0.5; ctx.moveTo(b[0], yy); ctx.lineTo(b[2], yy); }
        else { const xx = a.type === 'strike' ? b[0] + w * 0.5 : (d === 90 ? b[0] + 0.5 : b[2] - 0.5); ctx.moveTo(xx, b[1]); ctx.lineTo(xx, b[3]); }
        ctx.stroke();
      }
      else if (a.type === 'whiteout') { ctx.fillStyle = '#fff'; ctx.fillRect(b[0], b[1], w, hh); if (S.mode === 'comment') { ctx.strokeStyle = 'rgba(0,0,0,.12)'; ctx.strokeRect(b[0], b[1], w, hh); } }
      else if (a.type === 'redact') { ctx.fillStyle = '#000'; ctx.fillRect(b[0], b[1], w, hh); }
      else if (a.type === 'image') {
        const im = getImg(a.src);
        if (im.complete && im.naturalWidth) {
          // Bild steht aufrecht zur Seitendrehung beim Einfügen; wurde die Seite seither gedreht, dreht es mit
          const d = screenDelta(i, a);
          ctx.translate(b[0] + w / 2, b[1] + hh / 2);
          ctx.rotate(d * Math.PI / 180);
          const [iw, ih] = d % 180 ? [hh, w] : [w, hh];
          ctx.drawImage(im, -iw / 2, -ih / 2, iw, ih);
        }
      }
      else {
        ctx.strokeStyle = a.color; ctx.lineWidth = a.width * s;
        if (a.fill) { ctx.fillStyle = a.fill; ctx.globalAlpha = 0.35; }
        ctx.beginPath();
        if (a.type === 'ellipse') ctx.ellipse(b[0] + w / 2, b[1] + hh / 2, Math.max(w / 2, 0.5), Math.max(hh / 2, 0.5), 0, 0, Math.PI * 2);
        else ctx.rect(b[0], b[1], w, hh);
        if (a.fill) { ctx.fill(); ctx.globalAlpha = 1; }
        ctx.stroke();
      }
    }
  }
  ctx.restore();
}

/* ---------- Werkzeuge in der Kontextleiste ---------- */
const COLORS = ['#d0312d', '#e36d0b', '#e8c800', '#2b8a3e', '#1f6fd1', '#5b2bb5', '#111111', '#ffffff'];
const HL_COLORS = ['#ffe14d', '#8ef08a', '#7fd3ff', '#ff9ad5', '#ffb36b'];
const COMMENT_TOOLS = [
  ['select', 'Auswählen und verschieben (V)'], ['hand', 'Hand: Ansicht verschieben (H)'], null,
  ['highlight', 'Text markieren (M)'], ['ink', 'Freihand zeichnen (P)'], ['text', 'Textfeld (T)'], ['note', 'Notiz (N)'], null,
  ['rect', 'Rechteck (R)'], ['ellipse', 'Ellipse (E)'], ['line', 'Linie (L)'], ['arrow', 'Pfeil (A)'], null,
  ['edittext', 'Vorhandenen Text ersetzen'], ['whiteout', 'Abdecken (weiß)'], ['redact', 'Schwärzen'], null,
  ['image', 'Bild einfügen'], ['sign', 'Unterschreiben'], ['stamp', 'Stempel'],
];
const DRAW_TOOLS = new Set(['ink', 'text', 'note', 'rect', 'ellipse', 'line', 'arrow', 'edittext', 'whiteout', 'redact', 'place']);
const TOOL_KEYS = { v: 'select', h: 'hand', m: 'highlight', p: 'ink', t: 'text', n: 'note', r: 'rect', e: 'ellipse', l: 'line', a: 'arrow' };
const TOOL_HINTS = {
  select: 'Anmerkung anklicken zum Verschieben, Ecke ziehen zum Skalieren, Entf zum Löschen, Doppelklick zum Bearbeiten.',
  hand: 'Ziehen, um die Ansicht zu verschieben.',
  highlight: 'Text mit der Maus überstreichen. Auf leeren Flächen entsteht eine rechteckige Markierung.',
  ink: 'Mit gedrückter Maustaste oder Stift zeichnen.',
  text: 'Auf die Seite klicken und tippen. Klick daneben beendet die Eingabe.',
  note: 'Klicken, um eine Notiz anzuheften. Sie wird als echte PDF-Notiz gespeichert.',
  edittext: 'Auf eine Textzeile klicken: sie wird abgedeckt und als bearbeitbarer Text neu gesetzt.',
  whiteout: 'Rechteck ziehen, um Inhalte weiß abzudecken.',
  redact: 'Rechteck ziehen. Beim Speichern wird der Inhalt darunter dauerhaft entfernt.',
  place: 'Klicken oder Rechteck ziehen, um es zu platzieren.',
};

function renderCtx() {
  const bar = $('#ctxBar');
  bar.textContent = '';
  if (S.mode === 'comment') {
    for (const t of COMMENT_TOOLS) {
      if (!t) { bar.append(h('div', { class: 'sep' })); continue; }
      const [id, title] = t;
      const on = S.tool === id || (S.tool === 'place' && S.placeKind === id);
      bar.append(h('button', { class: 'btn icon' + (on ? ' on' : ''), title, 'aria-label': title, html: svg(id), onclick: () => pickTool(id) }));
    }
    bar.append(h('div', { class: 'sep' }));
    const isHL = S.tool === 'highlight' || ['highlight', 'underline', 'strike'].includes(selAnn()?.type);
    const palette = isHL ? HL_COLORS : COLORS;
    const curColor = isHL ? S.hlColor : S.color;
    bar.append(h('div', { class: 'swatches' }, palette.map((c) => h('button', { class: 'sw' + (c === curColor ? ' on' : ''), style: `background:${c}`, title: c, 'aria-label': 'Farbe ' + c,
      onclick: () => { if (isHL) S.hlColor = c; else S.color = c; applyToSelected({ color: c }); renderCtx(); } }))));
    if (['ink', 'rect', 'ellipse', 'line', 'arrow'].includes(S.tool) || ['ink', 'rect', 'ellipse', 'line', 'arrow'].includes(selAnn()?.type)) {
      bar.append(h('span', { class: 'lbl' }, 'Stärke'),
        h('input', { type: 'range', id: 'wRange', min: 0.5, max: 12, step: 0.5, value: S.width, oninput: (e) => { S.width = +e.target.value; applyToSelected({ width: S.width }, true); } }));
    }
    if (S.tool === 'text' || selAnn()?.type === 'text') {
      bar.append(h('span', { class: 'lbl' }, 'Größe'),
        h('select', { class: 'zoomsel', id: 'fsSel', onchange: (e) => { S.fontSize = +e.target.value; applyToSelected({ size: S.fontSize }); } },
          [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64].concat(S.fontSize).filter((n, k, l) => l.indexOf(n) === k).sort((x, y) => x - y)
            .map((n) => h('option', { value: n, selected: n === S.fontSize }, n + ' pt'))),
        h('select', { class: 'zoomsel', id: 'fontSel', 'aria-label': 'Schrift', onchange: (e) => { S.font = e.target.value; applyToSelected({ font: S.font }); } },
          Object.entries(FONT_LABELS).map(([k, l]) => h('option', { value: k, selected: k === (selAnn()?.font || S.font) }, l))));
    }
    if (S.selected) bar.append(h('button', { class: 'btn', html: svg('trash') + 'Löschen', onclick: deleteSelected }));
    bar.append(h('span', { class: 'hint hide-sm' }, TOOL_HINTS[S.tool] || ''));
  } else if (S.mode === 'read') {
    bar.append(
      h('button', { class: 'btn' + (S.tool === 'select' ? ' on' : ''), html: svg('select') + 'Text auswählen', onclick: () => pickTool('select') }),
      h('button', { class: 'btn' + (S.tool === 'hand' ? ' on' : ''), html: svg('hand') + 'Hand', onclick: () => pickTool('hand') }),
      h('div', { class: 'sep' }),
      h('button', { class: 'btn' + (S.spread ? ' on' : ''), title: 'Zwei Seiten nebeneinander', onclick: () => { S.spread = !S.spread; buildViewer(true); renderCtx(); } }, 'Doppelseite'),
      h('button', { class: 'btn' + (S.night ? ' on' : ''), title: 'Seiten für dunkle Umgebung invertieren', onclick: () => { S.night = !S.night; $('#viewer').classList.toggle('night', S.night); renderCtx(); } }, 'Nachtmodus'),
      h('button', { class: 'btn', html: svg('present') + 'Präsentation', onclick: startPresentation }),
      h('div', { class: 'sep' }),
      h('button', { class: 'btn', html: svg('rotL'), title: 'Aktuelle Seite links drehen', onclick: () => rotatePages([S.cur], -90) }),
      h('button', { class: 'btn', html: svg('rotR'), title: 'Aktuelle Seite rechts drehen', onclick: () => rotatePages([S.cur], 90) }),
      h('span', { class: 'hint hide-sm' }, 'Text markieren, dann im Menü hervorheben oder kopieren.'));
  } else if (S.mode === 'pages') {
    const n = S.orgSel.size;
    bar.append(
      h('span', { class: 'lbl' }, n ? `${n} ausgewählt` : 'Seiten anklicken zum Auswählen, ziehen zum Verschieben'),
      h('button', { class: 'btn', onclick: () => { S.orgSel = n === S.pages.length ? new Set() : new Set(S.pages.map((_, i) => i)); renderOrganizer(); renderCtx(); } }, n === S.pages.length ? 'Keine' : 'Alle'),
      h('div', { class: 'sep' }),
      h('button', { class: 'btn', html: svg('rotL'), title: 'Links drehen', disabled: !n, onclick: () => rotatePages([...S.orgSel], -90) }),
      h('button', { class: 'btn', html: svg('rotR'), title: 'Rechts drehen', disabled: !n, onclick: () => rotatePages([...S.orgSel], 90) }),
      h('button', { class: 'btn', html: svg('copy') + '<span class="hide-sm">Duplizieren</span>', disabled: !n, onclick: () => duplicatePages([...S.orgSel]) }),
      h('button', { class: 'btn', html: svg('extract') + '<span class="hide-sm">Extrahieren</span>', disabled: !n, onclick: () => exportPages([...S.orgSel].sort((a, b) => a - b)) }),
      h('button', { class: 'btn', html: svg('trash') + '<span class="hide-sm">Löschen</span>', disabled: !n || n === S.pages.length, onclick: () => deletePages([...S.orgSel]) }),
      h('div', { class: 'sep' }),
      h('button', { class: 'btn', html: svg('blank') + '<span class="hide-sm">Leere Seite</span>', onclick: () => insertBlank(n ? Math.max(...S.orgSel) + 1 : S.pages.length) }),
      h('button', { class: 'btn', html: svg('plus') + '<span class="hide-sm">Datei einfügen</span>', onclick: () => pickInsertFiles(n ? Math.max(...S.orgSel) + 1 : S.pages.length) }),
      h('button', { class: 'btn', html: svg('reverse') + '<span class="hide-sm">Umkehren</span>', onclick: () => reorder(S.pages.map((_, i) => S.pages.length - 1 - i)) }));
  } else {
    bar.append(h('span', { class: 'lbl' }, 'Werkzeuge wirken auf das ganze Dokument. Jeder Schritt lässt sich mit Strg+Z zurücknehmen.'));
  }
}
function selAnn() { return S.selected ? S.ann[S.selected.p]?.find((a) => a.id === S.selected.id) : null; }
function applyToSelected(patch, live) {
  const a = selAnn(); if (!a) return;
  if (a.type === 'image' || a.type === 'whiteout' || a.type === 'redact') return;
  if (!live || !applyToSelected.snap) { snapshot(); applyToSelected.snap = live; }
  Object.assign(a, patch);
  drawOverlay(S.selected.p);
  if (!live) applyToSelected.snap = false; else { clearTimeout(applyToSelected.t); applyToSelected.t = setTimeout(() => (applyToSelected.snap = false), 600); }
}
function deleteSelected() {
  if (!S.selected) return;
  snapshot();
  const { p, id } = S.selected;
  S.ann[p] = S.ann[p].filter((a) => a.id !== id);
  S.selected = null;
  drawOverlay(p); renderCtx(); if (sideTab === 'comments') renderSide();
}
function pickTool(id) {
  commitEdit();
  if (id === 'image') { $('#imgInput').click(); return; }
  if (id === 'sign') { signatureDialog(); return; }
  if (id === 'stamp') { stampDialog(); return; }
  S.tool = id; S.placeKind = null;
  if (id !== 'select') S.selected = null;
  applyToolClass(); renderCtx(); redrawAllOverlays();
}
function startPlacing(img, kind) {
  S.pendingImage = img; S.tool = 'place'; S.placeKind = kind; S.selected = null;
  if (S.mode !== 'comment') setMode('comment');
  applyToolClass(); renderCtx();
  toast('Auf die Seite klicken oder ein Rechteck ziehen.');
}
function applyToolClass() {
  const drawing = S.mode === 'comment' && DRAW_TOOLS.has(S.tool);
  views.forEach((v) => { v.div.classList.toggle('drawing', drawing); v.div.classList.toggle('tool-select', S.mode === 'comment' && S.tool === 'select'); });
  $('#viewer').classList.toggle('hand', S.tool === 'hand');
  $$('.linklayer').forEach((l) => (l.hidden = S.mode !== 'read' || S.tool === 'hand'));
}

/* ---------- Maus und Stift auf den Seiten ---------- */
let drag = null;
function evPos(i, e) {
  const r = views[i].div.getBoundingClientRect();
  return [e.clientX - r.left, e.clientY - r.top];
}
function hitTest(i, sx, sy) {
  const list = S.ann[i] || [];
  for (let k = list.length - 1; k >= 0; k--) {
    const a = list[k], b = scrBox(i, a), tol = 5;
    if (sx >= b[0] - tol && sx <= b[2] + tol && sy >= b[1] - tol && sy <= b[3] + tol) {
      if (a.type === 'line' || a.type === 'arrow') {
        const [x1, y1] = toScr(i, a.x1, a.y1), [x2, y2] = toScr(i, a.x2, a.y2);
        if (distSeg(sx, sy, x1, y1, x2, y2) > 6 + a.width * S.scale) continue;
      }
      return a;
    }
  }
  return null;
}
function distSeg(px, py, x1, y1, x2, y2) {
  const dx = x2 - x1, dy = y2 - y1, L = dx * dx + dy * dy || 1;
  const t = clamp(((px - x1) * dx + (py - y1) * dy) / L, 0, 1);
  return Math.hypot(px - x1 - t * dx, py - y1 - t * dy);
}
function addAnn(i, a, { snap = true } = {}) {
  if (snap) snapshot();
  a.id = a.id || uid();
  S.ann[i].push(a);
  drawOverlay(i);
  if (sideTab === 'comments') renderSide();
  return a;
}

function attachPageEvents(i) {
  const v = views[i];
  v.div.addEventListener('pointerdown', (e) => onPageDown(i, e));
  v.div.addEventListener('dblclick', (e) => onPageDbl(i, e));
  addLinks(i);
}

async function addLinks(i) {
  const ver = S.version;
  const page = await S.pdf.getPage(i + 1);
  const annots = await page.getAnnotations({ intent: 'display' });
  if (ver !== S.version) return;
  const links = annots.filter((a) => a.subtype === 'Link' && (a.url || a.dest));
  if (!links.length) return;
  const layer = h('div', { class: 'linklayer', hidden: S.mode !== 'read' });
  const vp1 = page.getViewport({ scale: 1 });
  for (const l of links) {
    const [x1, y1] = vp1.convertToViewportPoint(l.rect[0], l.rect[1]);
    const [x2, y2] = vp1.convertToViewportPoint(l.rect[2], l.rect[3]);
    const box = [Math.min(x1, x2), Math.min(y1, y2), Math.abs(x2 - x1), Math.abs(y2 - y1)].map((n) => (n / vp1.width * 100));
    const hb = [Math.min(y1, y2), Math.abs(y2 - y1)].map((n) => n / vp1.height * 100);
    const a = h('a', { style: `left:${box[0]}%;top:${hb[0]}%;width:${box[2]}%;height:${hb[1]}%`, title: l.url || 'Sprung im Dokument' });
    if (l.url) { a.href = l.url; a.target = '_blank'; a.rel = 'noopener'; }
    else a.addEventListener('click', (e) => { e.preventDefault(); goDest(l.dest); });
    layer.append(a);
  }
  views[i]?.div.append(layer);
}

function onPageDown(i, e) {
  if (e.button > 0 && e.pointerType === 'mouse') return;
  const v = views[i];
  if (!v.vp || S.mode !== 'comment') return;
  if (S.editing) { if (e.target.classList.contains('inlineedit')) return; commitEdit(); if (S.tool === 'text') return; }
  // Zeichnen verhindert den Fokuswechsel; ohne blur behielten Regler und Auswahllisten Strg+Z, Entf und die Pfeiltasten
  const ae = document.activeElement;
  if (ae && ae !== document.body && !isTextField(ae)) ae.blur();
  const [sx, sy] = evPos(i, e);
  const [px, py] = toPdf(i, sx, sy);
  const tool = S.tool;
  S.cur = i; updatePageIndicator();

  if (tool === 'select') {
    // Griff zum Skalieren der gewählten Anmerkung
    const sel = S.selected?.p === i ? selAnn() : null;
    if (sel && (BOX_TYPES.has(sel.type) || sel.type === 'text')) {
      const b = scrBox(i, sel);
      if (sx >= b[2] - 2 && sx <= b[2] + 12 && sy >= b[3] - 2 && sy <= b[3] + 12) {
        e.preventDefault();
        snapshot();
        drag = { page: i, kind: 'resize', a: sel, start: [sx, sy], box: b, orig: JSON.parse(JSON.stringify(sel)) };
        return capture(v, e);
      }
    }
    const a = hitTest(i, sx, sy);
    if (a) {
      e.preventDefault();
      window.getSelection()?.removeAllRanges();
      S.selected = { p: i, id: a.id };
      drag = { page: i, kind: 'move', a, start: [px, py], orig: JSON.parse(JSON.stringify(a)), moved: false };
      redrawAllOverlays(); renderCtx();
      return capture(v, e);
    }
    if (S.selected) { S.selected = null; redrawAllOverlays(); renderCtx(); }
    return; // sonst normale Textauswahl
  }
  if (tool === 'highlight') {
    // Textauswahl übernimmt; bei Ziehen auf leerer Fläche wird ein Rechteck markiert
    drag = { page: i, kind: 'hl', start: [px, py], s0: [sx, sy] };
    if (!e.target.closest('.textLayer span')) { e.preventDefault(); capture(v, e); }
    return;
  }
  if (tool === 'hand') return;
  e.preventDefault();
  if (tool === 'text') { createTextAt(i, px, py); return; }
  if (tool === 'note') { createNote(i, px, py); return; }
  if (tool === 'edittext') { editExistingText(i, px, py); return; }
  const rot = S.pages[i].rot;
  if (tool === 'ink') drag = { page: i, kind: 'ink', preview: { type: 'ink', pts: [[px, py]], color: S.color, width: S.width } };
  else if (tool === 'place') drag = { page: i, kind: 'place', start: [px, py], s0: [sx, sy], preview: null };
  else {
    const base = { type: tool, x1: px, y1: py, x2: px, y2: py, color: S.color, width: S.width, rot };
    drag = { page: i, kind: 'shape', preview: base, s0: [sx, sy] };
  }
  capture(v, e);
}
function capture(v, e) {
  if (drag) drag.captured = true;
  try { v.div.setPointerCapture(e.pointerId); } catch { /* ältere Browser */ }
  const move = (ev) => onPageMove(ev);
  const up = (ev) => { v.div.removeEventListener('pointermove', move); v.div.removeEventListener('pointerup', up); v.div.removeEventListener('pointercancel', up); onPageUp(ev); };
  v.div.addEventListener('pointermove', move);
  v.div.addEventListener('pointerup', up);
  v.div.addEventListener('pointercancel', up);
}
function onPageMove(e) {
  if (!drag) return;
  const i = drag.page;
  const [sx, sy] = evPos(i, e);
  const [px, py] = toPdf(i, sx, sy);
  if (drag.kind === 'ink') {
    const evs = e.getCoalescedEvents ? e.getCoalescedEvents() : [e];
    for (const ce of evs) { const [cx, cy] = evPos(i, ce); drag.preview.pts.push(toPdf(i, cx, cy)); }
  } else if (drag.kind === 'shape') {
    let [qx, qy] = [px, py];
    if (e.shiftKey && (drag.preview.type === 'line' || drag.preview.type === 'arrow')) {
      const [x0, y0] = [drag.preview.x1, drag.preview.y1], ang = Math.round(Math.atan2(py - y0, px - x0) / (Math.PI / 4)) * Math.PI / 4, L = Math.hypot(px - x0, py - y0);
      qx = x0 + L * Math.cos(ang); qy = y0 + L * Math.sin(ang);
    } else if (e.shiftKey) {
      const d = Math.max(Math.abs(px - drag.preview.x1), Math.abs(py - drag.preview.y1));
      qx = drag.preview.x1 + Math.sign(px - drag.preview.x1) * d; qy = drag.preview.y1 + Math.sign(py - drag.preview.y1) * d;
    }
    drag.preview.x2 = qx; drag.preview.y2 = qy;
  } else if (drag.kind === 'hl' || drag.kind === 'place') {
    drag.preview = { type: drag.kind === 'hl' ? 'highlight' : 'rect', x1: drag.start[0], y1: drag.start[1], x2: px, y2: py, color: drag.kind === 'hl' ? S.hlColor : '#2c49b8', width: 1 };
  } else if (drag.kind === 'move') {
    const dx = px - drag.start[0], dy = py - drag.start[1];
    if (!drag.moved && Math.hypot(dx, dy) * S.scale < 3) return;
    if (!drag.moved) { snapshot(); drag.moved = true; }
    moveAnn(drag.a, drag.orig, dx, dy);
  } else if (drag.kind === 'resize') {
    resizeAnn(i, drag, sx, sy, e.shiftKey);
  }
  drawOverlay(i);
}
function onPageUp(e) {
  if (!drag) return;
  const d = drag; drag = null;
  const i = d.page;
  if (d.kind === 'ink') {
    if (d.preview.pts.length) { d.preview.pts = simplify(d.preview.pts, 0.35 / S.scale); addAnn(i, d.preview); }
  } else if (d.kind === 'shape') {
    const p = d.preview, [sx, sy] = evPos(i, e);
    if (Math.hypot(sx - d.s0[0], sy - d.s0[1]) > 4) addAnn(i, p);
    else drawOverlay(i);
  } else if (d.kind === 'hl') {
    drawOverlay(i);
    if (d.preview && !hasTextSelection()) {
      const [sx, sy] = evPos(i, e);
      if (Math.hypot(sx - d.s0[0], sy - d.s0[1]) > 4) addAnn(i, { ...d.preview, rot: S.pages[i].rot });
    }
  } else if (d.kind === 'place') {
    const img = S.pendingImage; if (!img) return;
    const [sx, sy] = evPos(i, e);
    let x1, y1, x2, y2;
    if (Math.hypot(sx - d.s0[0], sy - d.s0[1]) > 8) {
      // Rechteck gezogen: Seitenverhältnis halten
      const bw = Math.abs(sx - d.s0[0]), bh = Math.abs(sy - d.s0[1]), ar = img.w / img.h;
      let w = bw, hh = bw / ar; if (hh > bh) { hh = bh; w = bh * ar; }
      const ox = Math.min(sx, d.s0[0]), oy = Math.min(sy, d.s0[1]);
      [x1, y1] = toPdf(i, ox, oy); [x2, y2] = toPdf(i, ox + w, oy + hh);
    } else {
      const wPt = img.defW || 160, w = wPt * S.scale, hh = w * img.h / img.w;
      [x1, y1] = toPdf(i, sx - w / 2, sy - hh / 2); [x2, y2] = toPdf(i, sx + w / 2, sy + hh / 2);
    }
    const a = addAnn(i, { type: 'image', src: img.src, x1, y1, x2, y2, rot: S.pages[i].rot, kind: S.placeKind });
    S.pendingImage = null; S.tool = 'select'; S.placeKind = null; S.selected = { p: i, id: a.id };
    applyToolClass(); renderCtx(); drawOverlay(i);
  } else if (d.kind === 'move' || d.kind === 'resize') {
    if (sideTab === 'comments') renderSide();
  }
}
function moveAnn(a, o, dx, dy) {
  if (a.type === 'ink') a.pts = o.pts.map((p) => [p[0] + dx, p[1] + dy]);
  else if (a.type === 'text' || a.type === 'note') { a.x = o.x + dx; a.y = o.y + dy; }
  else { a.x1 = o.x1 + dx; a.y1 = o.y1 + dy; a.x2 = o.x2 + dx; a.y2 = o.y2 + dy; }
}
function resizeAnn(i, d, sx, sy, keepRatio) {
  const a = d.a, o = d.orig, b = d.box;
  if (a.type === 'text') {
    const f = clamp((sy - b[1]) / Math.max(4, b[3] - b[1]), 0.2, 10);
    a.size = Math.max(4, Math.round(o.size * f * 2) / 2);
    S.fontSize = a.size;
    return;
  }
  let nw = Math.max(6, sx - b[0]), nh = Math.max(6, sy - b[1]);
  if (a.type === 'image' || keepRatio) { const ar = (b[2] - b[0]) / (b[3] - b[1]); if (nw / nh > ar) nw = nh * ar; else nh = nw / ar; }
  const [x1, y1] = toPdf(i, b[0], b[1]), [x2, y2] = toPdf(i, b[0] + nw, b[1] + nh);
  Object.assign(a, { x1, y1, x2, y2 });
}
function simplify(pts, tol) {
  if (pts.length < 3) return pts;
  const out = [pts[0]];
  for (let k = 1; k < pts.length - 1; k++) { const l = out[out.length - 1]; if (Math.hypot(pts[k][0] - l[0], pts[k][1] - l[1]) > tol) out.push(pts[k]); }
  out.push(pts[pts.length - 1]);
  return out.map((p) => [Math.round(p[0] * 100) / 100, Math.round(p[1] * 100) / 100]);
}
function onPageDbl(i, e) {
  if (S.mode !== 'comment') return;
  const [sx, sy] = evPos(i, e);
  const a = hitTest(i, sx, sy);
  if (a?.type === 'text') { e.preventDefault(); startEdit(i, a); }
  else if (a?.type === 'note') { e.preventDefault(); editNote(i, a); }
}

/* Textfelder */
function createTextAt(i, px, py) {
  const a = { type: 'text', x: px, y: py, text: '', size: S.fontSize, font: S.font, color: S.color, rot: S.pages[i].rot, id: uid() };
  // nach oben versetzen, damit der Klickpunkt mitten in der ersten Zeile liegt
  const ax = AXES[a.rot];
  a.x -= ax.D[0] * a.size * 0.6; a.y -= ax.D[1] * a.size * 0.6;
  snapshot();
  S.ann[i].push(a);
  startEdit(i, a, true);
}
function startEdit(i, a, isNew = false) {
  commitEdit();
  if (!isNew) snapshot();
  S.editing = { p: i, id: a.id, isNew };
  S.selected = { p: i, id: a.id };
  const v = views[i];
  const [x, y] = toScr(i, a.x, a.y);
  const fs = a.size * S.scale;
  const ta = h('textarea', { class: 'inlineedit', spellcheck: 'false', 'aria-label': 'Text' });
  ta.value = a.text;
  ta.style.cssText = `left:${x}px;top:${y}px;font:${FONT_CSS(fs, a.font)};line-height:1.2;color:${a.color}`;
  const fit = () => {
    const d = textDims({ ...a, text: ta.value || ' ' });
    ta.style.width = (d.w * S.scale + fs) + 'px';
    ta.style.height = (d.h * S.scale + 4) + 'px';
  };
  ta.addEventListener('input', () => { a.text = ta.value; fit(); });
  ta.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') { e.stopPropagation(); commitEdit(); return; }
    // Kürzel mit Strg/Cmd (Speichern, Drucken …) erreichen die Seite; AltGr meldet Strg+Alt und bleibt Texteingabe
    if ((e.ctrlKey || e.metaKey) && !e.altKey) return;
    e.stopPropagation();
  });
  ta.addEventListener('pointerdown', (e) => e.stopPropagation());
  v.div.append(ta);
  fit();
  drawOverlay(i);
  ta.focus();
  setTimeout(() => { ta.focus(); ta.setSelectionRange(ta.value.length, ta.value.length); }, 0);
  renderCtx();
}
function commitEdit() {
  const ed = S.editing; if (!ed) return;
  S.editing = null;
  $$('.inlineedit').forEach((t) => t.remove());
  const a = S.ann[ed.p]?.find((x) => x.id === ed.id);
  if (a && !a.text.trim()) {
    S.ann[ed.p] = S.ann[ed.p].filter((x) => x.id !== ed.id);
    if (S.selected?.id === ed.id) S.selected = null;
    if (ed.isNew && !ed.cover) S.history.pop(); // leeres neues Textfeld hinterlässt keinen Schritt
    updateUndoButtons();
  }
  drawOverlay(ed.p);
  if (sideTab === 'comments') renderSide();
}

/* Notizen */
async function createNote(i, px, py) {
  const ta = h('textarea', { name: 'txt', rows: 5 });
  const txt = await dialog({ title: 'Notiz', ok: 'Anheften', body: field('Text der Notiz', ta), onOk: () => ta.value.trim() });
  if (!txt) return;
  addAnn(i, { type: 'note', x: px, y: py, text: txt, color: '#f5c400' });
  if (sideTab !== 'comments') { sideTab = 'comments'; renderSide(); }
}
async function editNote(i, a) {
  const ta = h('textarea', { name: 'txt', rows: 5 }); ta.value = a.text;
  const del = h('button', { class: 'btn', type: 'button', style: 'margin-right:auto;color:var(--danger)', onclick: () => { document.querySelector('.backdrop')?.remove(); S.selected = { p: i, id: a.id }; deleteSelected(); } }, 'Löschen');
  const txt = await dialog({ title: 'Notiz bearbeiten', ok: 'Übernehmen', body: field('Text der Notiz', ta), extraButtons: [del], onOk: () => ta.value.trim() });
  if (txt == null) return;
  snapshot(); a.text = txt; drawOverlay(i); renderSide();
}

/* Vorhandenen Text ersetzen: Zeile abdecken und als bearbeitbares Textfeld neu setzen */
async function editExistingText(i, px, py) {
  const tc = await getText(i);
  let best = null;
  for (const it of tc.items) {
    if (!it.str?.trim()) continue;
    const [a, b, c, d, e, f] = it.transform;
    const fh = Math.hypot(c, d) || it.height;
    if (Math.abs(b) > 0.01 || Math.abs(c) > 0.01) continue; // gedrehten Text überspringen
    const x1 = e, x2 = e + it.width, y1 = f - fh * 0.25, y2 = f + fh * 0.95;
    if (px >= x1 - 2 && px <= x2 + 2 && py >= y1 && py <= y2) { best = { it, x1, x2, y1, y2, fh, f, e }; break; }
  }
  if (!best) { toast('Hier ist kein Text. Auf eine Textzeile klicken.'); return; }
  snapshot();
  const color = await sampleTextColor(i, best);
  S.ann[i].push({ type: 'whiteout', id: uid(), x1: best.x1 - 1, y1: best.y1, x2: best.x2 + 1, y2: best.y2, rot: S.pages[i].rot });
  const size = Math.round(best.fh * 2) / 2;
  const a = { type: 'text', id: uid(), x: best.e, y: best.f + size * 0.8, text: best.it.str, size, color, rot: 0, font: await guessFont(i, best.it, tc) };
  S.ann[i].push(a);
  startEdit(i, a, true);
  S.editing.cover = true;
}
async function guessFont(i, it, tc) {
  let name = '';
  try { const page = await S.pdf.getPage(i + 1); if (page.commonObjs.has(it.fontName)) name = page.commonObjs.get(it.fontName)?.name || ''; } catch { /* unbekannt */ }
  const fam = (tc.styles?.[it.fontName]?.fontFamily || '') + ' ' + name;
  const bold = /bold|black|heavy|semibold|demi/i.test(name);
  const base = /courier|mono/i.test(fam) ? 'Courier' : /times|serif|roman|georgia|garamond|cambria|minion/i.test(fam) && !/sans/i.test(fam) ? 'Times' : 'Helvetica';
  return base === 'Times' ? (bold ? 'Times-Bold' : 'Times-Roman') : bold ? base + '-Bold' : base;
}
async function sampleTextColor(i, box) {
  try {
    const v = views[i], dpr = v.pdfC.width / v.vp.width;
    const p1 = toScr(i, box.x1, box.y2), p2 = toScr(i, box.x2, box.y1);
    const x = Math.floor(Math.min(p1[0], p2[0]) * dpr), y = Math.floor(Math.min(p1[1], p2[1]) * dpr);
    const w = Math.max(1, Math.floor(Math.abs(p2[0] - p1[0]) * dpr)), hh = Math.max(1, Math.floor(Math.abs(p2[1] - p1[1]) * dpr));
    const d = v.pdfC.getContext('2d').getImageData(x, y, w, hh).data;
    let best = [0, 0, 0], dark = 765;
    for (let k = 0; k < d.length; k += 16) { const s = d[k] + d[k + 1] + d[k + 2]; if (s < dark) { dark = s; best = [d[k], d[k + 1], d[k + 2]]; } }
    return '#' + best.map((n) => n.toString(16).padStart(2, '0')).join('');
  } catch { return '#000000'; }
}

/* Auswahl im Text: Markieren, Unterstreichen, Durchstreichen, Kopieren */
function hasTextSelection() { const s = window.getSelection(); return s && !s.isCollapsed && s.toString().trim().length > 0; }
function selectionRects() {
  const sel = window.getSelection();
  if (!sel || sel.isCollapsed) return [];
  const out = [];
  for (let r = 0; r < sel.rangeCount; r++) {
    for (const cr of sel.getRangeAt(r).getClientRects()) {
      if (cr.width < 1 || cr.height < 1) continue;
      const i = views.findIndex((v) => { const b = v.div.getBoundingClientRect(); return cr.left >= b.left - 2 && cr.right <= b.right + 2 && cr.top >= b.top - 2 && cr.bottom <= b.bottom + 2; });
      if (i < 0 || !views[i].vp) continue;
      const b = views[i].div.getBoundingClientRect();
      const [x1, y1] = toPdf(i, cr.left - b.left, cr.top - b.top), [x2, y2] = toPdf(i, cr.right - b.left, cr.bottom - b.top);
      out.push({ i, r: [x1, y1, x2, y2] });
    }
  }
  // überlappende Rechtecke derselben Zeile zusammenfassen
  const merged = [];
  for (const o of out) {
    const m = merged.find((q) => q.i === o.i && Math.abs(Math.min(q.r[1], q.r[3]) - Math.min(o.r[1], o.r[3])) < 2 && Math.abs(Math.max(q.r[1], q.r[3]) - Math.max(o.r[1], o.r[3])) < 2);
    if (m) { m.r = [Math.min(m.r[0], m.r[2], o.r[0], o.r[2]), m.r[1], Math.max(m.r[0], m.r[2], o.r[0], o.r[2]), m.r[3]]; }
    else merged.push({ i: o.i, r: o.r.slice() });
  }
  return merged;
}
function markSelection(type) {
  const rects = selectionRects();
  if (!rects.length) return;
  snapshot();
  const color = type === 'highlight' ? S.hlColor : type === 'underline' ? '#1f6fd1' : '#d0312d';
  for (const { i, r } of rects) S.ann[i].push({ type, id: uid(), x1: r[0], y1: r[1], x2: r[2], y2: r[3], color, rot: S.pages[i].rot });
  window.getSelection().removeAllRanges();
  redrawAllOverlays(); hideSelPop();
  if (sideTab === 'comments') renderSide();
}
function hideSelPop() { $('.selpop')?.remove(); }
function onSelectionEnd() {
  hideSelPop();
  if (!hasTextSelection()) return;
  const sel = window.getSelection();
  if (!sel.anchorNode || !$('#pages').contains(sel.anchorNode)) return;
  if (S.mode === 'comment' && S.tool === 'highlight') { markSelection('highlight'); return; }
  const rect = sel.getRangeAt(sel.rangeCount - 1).getBoundingClientRect();
  const desk = $('#desk').getBoundingClientRect();
  const pop = h('div', { class: 'selpop', style: `left:${clamp(rect.left - desk.left, 8, desk.width - 330)}px;top:${Math.max(8, rect.top - desk.top - 44)}px` },
    h('button', { onclick: () => markSelection('highlight') }, 'Markieren'),
    h('button', { onclick: () => markSelection('underline') }, 'Unterstreichen'),
    h('button', { onclick: () => markSelection('strike') }, 'Durchstreichen'),
    h('button', { onclick: async () => { const t = window.getSelection().toString(); try { await navigator.clipboard.writeText(t); toast('Kopiert'); } catch { toast('Mit Strg+C kopieren'); } hideSelPop(); } }, 'Kopieren'));
  pop.addEventListener('pointerdown', (e) => e.preventDefault());
  $('#desk').append(pop);
}

/* ---------- Seiten umbauen ---------- */
// order: Liste aus Quellseitenindex | {blank:[w,h]} | {ext:{doc,idx}}; rot: Drehung je Zielposition
async function rebuild(order, { rot = {}, msg = 'Seiten geändert' } = {}) {
  busy('Seiten werden neu angeordnet …');
  const doc = await PDFDocument.load(S.bytes, { updateMetadata: false });
  const orig = doc.getPages(), used = new Set(), list = [];
  for (const o of order) {
    if (typeof o === 'number') {
      if (used.has(o)) list.push((await doc.copyPages(doc, [o]))[0]);
      else { used.add(o); list.push(orig[o]); }
    } else if (o.blank) list.push(o);
    else if (o.ext) list.push((await doc.copyPages(o.ext.doc, [o.ext.idx]))[0]);
  }
  for (let k = orig.length - 1; k >= 0; k--) doc.removePage(k);
  list.forEach((p, k) => {
    const page = p.blank ? doc.addPage(p.blank) : doc.addPage(p);
    if (rot[k]) page.setRotation(degrees(((page.getRotation().angle + rot[k]) % 360 + 360) % 360));
  });
  const bytes = await doc.save();
  snapshot();
  S.ann = order.map((o) => typeof o === 'number' ? cloneAnn(S.ann[o]).map((a) => ({ ...a, id: uid() })) : []);
  S.orgSel = new Set([...S.orgSel].filter((k) => k < order.length));
  await openBytes(bytes, S.name, { keepAnn: true, keepPos: true, fresh: false });
  idle(msg);
}
const identity = () => S.pages.map((_, i) => i);
async function rotatePages(idx, delta) {
  const rot = {}; idx.forEach((i) => (rot[i] = delta));
  await rebuild(identity(), { rot, msg: idx.length === 1 ? `Seite ${idx[0] + 1} gedreht` : `${idx.length} Seiten gedreht` });
}
async function deletePages(idx) {
  const del = new Set(idx);
  if (del.size >= S.pages.length) { toast('Mindestens eine Seite muss bleiben.'); return; }
  S.orgSel.clear();
  await rebuild(identity().filter((i) => !del.has(i)), { msg: `${del.size} Seite(n) gelöscht` });
}
async function duplicatePages(idx) {
  const set = new Set(idx), order = [];
  identity().forEach((i) => { order.push(i); if (set.has(i)) order.push(i); });
  await rebuild(order, { msg: 'Seiten dupliziert' });
}
async function reorder(order) { await rebuild(order, { msg: 'Reihenfolge geändert' }); }
async function insertBlank(at) {
  const ref = S.pages[Math.max(0, at - 1)] || { w: 595.28, h: 841.89 };
  const sz = S.pages[Math.max(0, at - 1)]?.rot % 180 ? [ref.h, ref.w] : [ref.w, ref.h];
  const order = identity(); order.splice(at, 0, { blank: sz });
  await rebuild(order, { msg: 'Leere Seite eingefügt' });
  S.orgSel = new Set([at]); if (S.mode === 'pages') { renderOrganizer(); renderCtx(); }
}
let insertAt = null;
function pickInsertFiles(at) { insertAt = at; $('#fileInput2').value = ''; $('#fileInput2').click(); }
async function filesToExt(files) {
  const ext = [];
  for (const f of files) {
    const doc = await fileToDoc(f);
    if (!doc) continue;
    for (let k = 0; k < doc.getPageCount(); k++) ext.push({ ext: { doc, idx: k } });
  }
  return ext;
}
async function fileToDoc(f, opts) {
  const buf = new Uint8Array(await f.arrayBuffer());
  if (isPdf(f, buf)) {
    try { return await PDFDocument.load(buf, { ignoreEncryption: true }); }
    catch { toast(`„${f.name}“ konnte nicht gelesen werden.`); return null; }
  }
  if (f.type.startsWith('image/')) return imagesToDoc([f], opts);
  toast(`„${f.name}“ ist weder PDF noch Bild.`); return null;
}
const isPdf = (f, buf) => f.type === 'application/pdf' || /\.pdf$/i.test(f.name) || (buf[0] === 0x25 && buf[1] === 0x50 && buf[2] === 0x44 && buf[3] === 0x46);
async function insertFiles(files, at) {
  const ext = await filesToExt(files);
  if (!ext.length) return;
  const order = identity(); order.splice(at, 0, ...ext);
  await rebuild(order, { msg: `${ext.length} Seite(n) eingefügt` });
}

/* Bilder in PDF umwandeln */
async function loadImageFile(f) {
  const url = URL.createObjectURL(f);
  try {
    const im = new Image(); im.src = url; await im.decode();
    return im;
  } finally { setTimeout(() => URL.revokeObjectURL(url), 1000); }
}
// Bildart an den ersten Bytes erkennen: die Endung stimmt nicht immer (z. B. PNG als .jpg gespeichert)
async function imageBytesForPdf(f, im) {
  const b = new Uint8Array(await f.arrayBuffer());
  if (b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff) return { kind: 'jpg', bytes: b };
  if (b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4e && b[3] === 0x47) return { kind: 'png', bytes: b };
  return { kind: 'png', bytes: await imagePng(im) };
}
async function imagePng(im) {
  const c = document.createElement('canvas'); c.width = im.naturalWidth; c.height = im.naturalHeight;
  c.getContext('2d').drawImage(im, 0, 0);
  return canvasBytes(c, 'image/png');
}
async function imagesToDoc(files, { size = 'image', margin = 0 } = {}) {
  const doc = await PDFDocument.create();
  for (const f of files) {
    let im;
    try { im = await loadImageFile(f); } catch { toast(`Bild „${f.name}“ ist beschädigt.`); continue; }
    let emb;
    try {
      const { kind, bytes } = await imageBytesForPdf(f, im);
      try { emb = kind === 'jpg' ? await doc.embedJpg(bytes) : await doc.embedPng(bytes); }
      catch { emb = await doc.embedPng(await imagePng(im)); }   // z. B. ungewöhnliche JPEG-Variante: über die Darstellung
    } catch { toast(`Bild „${f.name}“ kann nicht übernommen werden.`); continue; }
    const iw = im.naturalWidth * 0.75, ih = im.naturalHeight * 0.75; // 96 dpi → pt
    let pw, ph;
    if (size === 'image') { pw = iw + 2 * margin; ph = ih + 2 * margin; }
    else {
      const a4 = [595.28, 841.89];
      const land = size === 'a4l' || (size === 'a4auto' && iw > ih);
      [pw, ph] = land ? [a4[1], a4[0]] : a4;
    }
    const s = Math.min((pw - 2 * margin) / iw, (ph - 2 * margin) / ih, size === 'image' ? 1 : Infinity);
    const w = iw * s, hh = ih * s;
    const page = doc.addPage([pw, ph]);
    page.drawImage(emb, { x: (pw - w) / 2, y: (ph - hh) / 2, width: w, height: hh });
  }
  return doc.getPageCount() ? doc : null;
}

/* ---------- Seitenansicht (Organizer) ---------- */
let orgAnchor = null;
function renderOrganizer() {
  const el = $('#organizer');
  el.textContent = '';
  const grid = h('div', { class: 'orggrid' });
  let dragFrom = null;
  S.pages.forEach((p, i) => {
    const ph = h('div', { style: `width:150px;height:${Math.round(150 * p.h / p.w)}px;background:#fff;box-shadow:var(--shadow)` });
    const cb = h('input', { type: 'checkbox', checked: S.orgSel.has(i), 'aria-label': 'Seite ' + (i + 1) + ' auswählen', onclick: (e) => { e.stopPropagation(); toggleOrg(i, e.target.checked); } });
    const tile = h('div', { class: 'otile' + (S.orgSel.has(i) ? ' sel' : ''), draggable: 'true', 'data-i': i },
      cb, ph,
      h('div', { class: 'meta' }, h('span', { class: 'num' }, String(i + 1)),
        h('div', { class: 'acts' },
          h('button', { title: 'Links drehen', html: svg('rotL'), onclick: (e) => { e.stopPropagation(); rotatePages([i], -90); } }),
          h('button', { title: 'Rechts drehen', html: svg('rotR'), onclick: (e) => { e.stopPropagation(); rotatePages([i], 90); } }),
          h('button', { title: 'Löschen', html: svg('trash'), onclick: (e) => { e.stopPropagation(); deletePages([i]); } }))));
    tile.addEventListener('click', (e) => {
      if (e.shiftKey && orgAnchor != null) { const [a, b] = [Math.min(orgAnchor, i), Math.max(orgAnchor, i)]; for (let k = a; k <= b; k++) S.orgSel.add(k); }
      else if (e.ctrlKey || e.metaKey) toggleOrg(i, !S.orgSel.has(i), true);
      else { S.orgSel = new Set([i]); }
      orgAnchor = i;
      refreshOrgSel(); renderCtx();
    });
    tile.addEventListener('dblclick', () => { setMode('read'); scrollToPage(i, false); });
    tile.addEventListener('dragstart', (e) => { dragFrom = i; e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(i)); });
    tile.addEventListener('dragover', (e) => { e.preventDefault(); tile.classList.add('dragover'); });
    tile.addEventListener('dragleave', () => tile.classList.remove('dragover'));
    tile.addEventListener('drop', async (e) => {
      e.preventDefault(); e.stopPropagation(); tile.classList.remove('dragover');
      if (e.dataTransfer.files?.length) { await insertFiles([...e.dataTransfer.files], i); return; }
      if (dragFrom == null || dragFrom === i) return;
      const moving = S.orgSel.has(dragFrom) ? [...S.orgSel].sort((a, b) => a - b) : [dragFrom];
      const rest = identity().filter((k) => !moving.includes(k));
      const pos = rest.indexOf(i) < 0 ? rest.length : rest.indexOf(i);
      rest.splice(pos, 0, ...moving);
      S.orgSel = new Set(moving.map((m) => rest.indexOf(m)));
      dragFrom = null;
      await reorder(rest);
    });
    grid.append(tile);
    const ver = S.version;
    getThumb(i, 150).then((url) => { if (ver === S.version) ph.replaceWith(thumbImg(url, 150)); }).catch(() => {});
  });
  const endZone = h('div', { class: 'otile', style: 'justify-content:center;min-height:160px;border:2px dashed var(--line);cursor:pointer;background:transparent', onclick: () => pickInsertFiles(S.pages.length) },
    h('div', { html: svg('plus', 'style="width:28px;height:28px;color:var(--muted)"') }), h('span', { style: 'font-size:12.5px;color:var(--muted);text-align:center' }, 'PDF oder Bilder anhängen'));
  endZone.addEventListener('dragover', (e) => e.preventDefault());
  endZone.addEventListener('drop', async (e) => {
    e.preventDefault(); e.stopPropagation();
    if (e.dataTransfer.files?.length) return insertFiles([...e.dataTransfer.files], S.pages.length);
    if (dragFrom == null) return;
    const moving = S.orgSel.has(dragFrom) ? [...S.orgSel].sort((a, b) => a - b) : [dragFrom];
    const rest = identity().filter((k) => !moving.includes(k)).concat(moving);
    S.orgSel = new Set(moving.map((m) => rest.indexOf(m)));
    await reorder(rest);
  });
  grid.append(endZone);
  el.append(grid);
}
function toggleOrg(i, on, silent) { on ? S.orgSel.add(i) : S.orgSel.delete(i); orgAnchor = i; if (!silent) { refreshOrgSel(); renderCtx(); } }
function refreshOrgSel() { $$('.otile[data-i]').forEach((t) => { const on = S.orgSel.has(+t.dataset.i); t.classList.toggle('sel', on); t.querySelector('input').checked = on; }); }

/* ---------- Suche ---------- */
async function runSearch(q, keepIdx) {
  S.search.q = q;
  const run = (runSearch.run = (runSearch.run || 0) + 1);
  S.search.hits = [];
  if (!keepIdx) S.search.idx = -1;
  $('#searchCount').textContent = q ? '…' : '';
  if (!q) { redrawAllOverlays(); return; }
  const needle = q.toLocaleLowerCase('de');
  for (let i = 0; i < S.pages.length; i++) {
    const tc = await getText(i);
    if (run !== runSearch.run) return;
    let str = '', map = [];
    tc.items.forEach((it, k) => {
      if (!it.str) { if (it.hasEOL) { str += ' '; map.push([-1, 0]); } return; }
      for (let c = 0; c < it.str.length; c++) { str += it.str[c]; map.push([k, c]); }
      if (it.hasEOL) { str += ' '; map.push([-1, 0]); }
    });
    const low = str.toLocaleLowerCase('de');
    let pos = low.indexOf(needle);
    while (pos >= 0) {
      const segs = new Map();
      for (let c = pos; c < pos + needle.length; c++) {
        const [k, ch] = map[c]; if (k < 0) continue;
        const s = segs.get(k) || [ch, ch]; s[1] = ch; segs.set(k, s);
      }
      const rects = [];
      for (const [k, [c0, c1]] of segs) {
        const it = tc.items[k], [a, b, c, d, e, f] = it.transform, len = it.str.length || 1;
        const fh = Math.hypot(c, d) || it.height;
        if (Math.abs(b) < 0.01 && Math.abs(c) < 0.01) {
          const x1 = e + it.width * (c0 / len), x2 = e + it.width * ((c1 + 1) / len);
          rects.push([x1, f - fh * 0.22, x2, f + fh * 0.9]);
        } else {
          const ang = Math.atan2(b, a), L = it.width;
          const xs = [e, e + L * Math.cos(ang), e - fh * Math.sin(ang), e + L * Math.cos(ang) - fh * Math.sin(ang)];
          const ys = [f, f + L * Math.sin(ang), f + fh * Math.cos(ang), f + L * Math.sin(ang) + fh * Math.cos(ang)];
          rects.push([Math.min(...xs), Math.min(...ys), Math.max(...xs), Math.max(...ys)]);
        }
      }
      S.search.hits.push({ p: i, rects });
      pos = low.indexOf(needle, pos + Math.max(1, needle.length));
    }
    $('#searchCount').textContent = S.search.hits.length + ' Treffer';
    if (views[i]?.vp) drawOverlay(i);
  }
  const n = S.search.hits.length;
  $('#searchCount').textContent = n ? `${Math.max(1, S.search.idx + 1)} / ${n}` : 'Kein Treffer';
  if (n && S.search.idx < 0) gotoHit(0); else redrawAllOverlays();
}
function gotoHit(k) {
  const n = S.search.hits.length; if (!n) return;
  S.search.idx = (k + n) % n;
  const hit = S.search.hits[S.search.idx];
  $('#searchCount').textContent = `${S.search.idx + 1} / ${n}`;
  const p = S.pages[hit.p], r = hit.rects[0];
  if (S.mode !== 'read' && S.mode !== 'comment') setMode('read');
  scrollToPage(hit.p, true, p.vp1.convertToViewportPoint(r[0], r[3])[1]);
  redrawAllOverlays();
}

/* ---------- Ausgabe: Anmerkungen einbrennen ---------- */
function winAnsiSafe(font, text) {
  let out = '';
  for (const ch of text) { try { font.encodeText(ch); out += ch; } catch { out += ch === '\t' ? '    ' : '?'; } }
  return out;
}
async function embedDataUrl(doc, src, cache) {
  if (cache.has(src)) return cache.get(src);
  const bytes = Uint8Array.from(atob(src.split(',')[1]), (c) => c.charCodeAt(0));
  const img = src.startsWith('data:image/jpeg') ? await doc.embedJpg(bytes) : await doc.embedPng(bytes);
  cache.set(src, img);
  return img;
}
function burnAnn(doc, page, a, fonts, img) {
  const font = fonts[a.font || 'Helvetica'] || fonts.Helvetica;
  const col = a.color ? pdfColor(a.color) : rgb(0, 0, 0);
  const box = annBox(a), [x1, y1, x2, y2] = box, w = x2 - x1, hh = y2 - y1;
  switch (a.type) {
    case 'highlight': page.drawRectangle({ x: x1, y: y1, width: w, height: hh, color: col, opacity: 0.45, blendMode: BlendMode.Multiply }); break;
    case 'underline': case 'strike': {
      const r = a.rot || 0, t = 1.2, strike = a.type === 'strike';
      const cx = (x1 + x2) / 2, cy = (y1 + y2) / 2;
      if (r % 180 === 0) { const y = strike ? y1 + hh * 0.45 : (r === 0 ? y1 + 0.6 : y2 - 0.6); page.drawLine({ start: { x: x1, y }, end: { x: x2, y }, thickness: t, color: col }); }
      else { const x = strike ? cx : (r === 90 ? x2 - 0.6 : x1 + 0.6); page.drawLine({ start: { x, y: y1 }, end: { x, y: y2 }, thickness: t, color: col }); }
      void cy; break;
    }
    case 'whiteout': page.drawRectangle({ x: x1, y: y1, width: w, height: hh, color: rgb(1, 1, 1) }); break;
    case 'redact': page.drawRectangle({ x: x1, y: y1, width: w, height: hh, color: rgb(0, 0, 0) }); break;
    case 'rect': page.drawRectangle({ x: x1, y: y1, width: w, height: hh, borderColor: col, borderWidth: a.width, ...(a.fill ? { color: pdfColor(a.fill), opacity: 0.35, borderOpacity: 1 } : {}) }); break;
    case 'ellipse': page.drawEllipse({ x: x1 + w / 2, y: y1 + hh / 2, xScale: w / 2, yScale: hh / 2, borderColor: col, borderWidth: a.width }); break;
    case 'line': case 'arrow': {
      const opts = { thickness: a.width, color: col, lineCap: PDFLib.LineCapStyle.Round };
      page.drawLine({ start: { x: a.x1, y: a.y1 }, end: { x: a.x2, y: a.y2 }, ...opts });
      if (a.type === 'arrow') {
        const ang = Math.atan2(a.y2 - a.y1, a.x2 - a.x1), L = Math.max(10, a.width * 4);
        for (const s of [-0.45, 0.45]) page.drawLine({ start: { x: a.x2, y: a.y2 }, end: { x: a.x2 - L * Math.cos(ang + s), y: a.y2 - L * Math.sin(ang + s) }, ...opts });
      }
      break;
    }
    case 'ink': {
      const d = a.pts.map((p, k) => `${k ? 'L' : 'M'}${p[0].toFixed(2)} ${(-p[1]).toFixed(2)}`).join(' ') + (a.pts.length === 1 ? ` L${(a.pts[0][0] + 0.1).toFixed(2)} ${(-a.pts[0][1]).toFixed(2)}` : '');
      page.drawSvgPath(d, { x: 0, y: 0, borderColor: col, borderWidth: a.width, borderLineCap: PDFLib.LineCapStyle.Round, ...(a.opacity ? { borderOpacity: a.opacity } : {}) });
      break;
    }
    case 'text': {
      const ax = AXES[a.rot || 0];
      a.text.split('\n').forEach((line, k) => {
        const off = a.size * (0.8 + k * 1.2);
        page.drawText(winAnsiSafe(font, line), { x: a.x + ax.D[0] * off, y: a.y + ax.D[1] * off, size: a.size, font, color: col, rotate: degrees(a.rot || 0) });
      });
      break;
    }
    case 'image': {
      const r = a.rot || 0;
      const at = { 0: [x1, y1, w, hh], 90: [x2, y1, hh, w], 180: [x2, y2, w, hh], 270: [x1, y2, hh, w] }[r];
      page.drawImage(img, { x: at[0], y: at[1], width: at[2], height: at[3], rotate: degrees(r) });
      break;
    }
  }
}
// Schreibt eine Notiz als PDF-Anmerkung. Eine übernommene Notiz behält ihr Objekt samt Antworten und Popup;
// Position, Text, Farbe und Änderungsdatum werden nur angefasst, wenn sie sich geändert haben.
function writeNote(doc, page, a, used) {
  const ctx = doc.context, o = a.orig, N = (k) => PDFName.of(k);
  const changed = !o || o.x !== a.x || o.y !== a.y || o.text !== a.text || o.color !== a.color;
  const old = o?.ref ? PDFRef.of(o.ref[0], o.ref[1]) : null;
  let ref = old, d = old && ctx.lookup(old), created = false;
  if (!(d instanceof PDFDict) || d.get(N('Subtype'))?.toString() !== '/Text' || used.has(old)) {
    // neue Notiz, oder das alte Objekt fehlt (z. B. Kopie einer Seite): Angaben der alten Notiz übernehmen
    const m = o?.meta || {};
    d = ctx.obj({ Type: 'Annot', Subtype: 'Text', Name: m.Name || 'Comment', F: m.F ?? 4, Open: false });
    d.set(N('T'), PDFHexString.fromText(m.T ?? 'PDFwerk'));
    for (const k of ['NM', 'Subj']) if (m[k] != null) d.set(N(k), PDFHexString.fromText(m[k]));
    if (m.CreationDate) d.set(N('CreationDate'), PDFString.of(m.CreationDate));
    if (!changed && m.M) d.set(N('M'), PDFString.of(m.M));
    ref = ctx.register(d); created = true;
  }
  if (changed || created) {
    d.set(N('Rect'), ctx.obj([a.x, a.y - 18, a.x + 18, a.y]));
    d.set(N('Contents'), PDFHexString.fromText(a.text));
    d.set(N('C'), ctx.obj(hexToRgb(a.color || '#f5c400')));
    if (changed) d.set(N('M'), PDFString.fromDate(new Date()));
  }
  d.set(N('P'), page.ref);
  used.add(ref);
  const add = (r, x) => {
    page.node.addAnnot(r);
    // eigenes Popup wieder einhängen, ein fehlendes austragen
    const pop = x.get(N('Popup'));
    if (!(pop instanceof PDFRef)) return;
    const pd = ctx.lookup(pop);
    if (pd instanceof PDFDict && !used.has(pop)) { pd.set(N('Parent'), r); page.node.addAnnot(pop); used.add(pop); }
    else x.delete(N('Popup'));
  };
  add(ref, d);
  // Antworten und Statusvermerke wieder anhängen
  for (const [n, g] of o?.replies || []) {
    const rr = PDFRef.of(n, g), rd = ctx.lookup(rr);
    if (!(rd instanceof PDFDict) || used.has(rr)) continue;
    if (rd.get(N('IRT')) === old) rd.set(N('IRT'), ref);
    rd.set(N('P'), page.ref);
    used.add(rr);
    add(rr, rd);
  }
}

// Erzeugt die fertige Datei: Anmerkungen eingebrannt, Schwärzungen wirklich entfernt.
// src: Stand, der geschrieben wird (Speichern übergibt eine Momentaufnahme, damit spätere Änderungen nicht halb hineingeraten)
async function buildOutput({ ann = true, progress, src = S } = {}) {
  const hasAnn = ann && src.ann.some((l) => l.length);
  const doc = await PDFDocument.load(src.bytes, { updateMetadata: false });
  doc.setProducer('PDFwerk');
  doc.setModificationDate(new Date());
  if (!hasAnn) return await doc.save({ updateFieldAppearances: false });
  const fonts = {};
  for (const f of new Set(['Helvetica', ...src.ann.flat().filter((a) => a.type === 'text').map((a) => a.font || 'Helvetica')]))
    fonts[f] = await doc.embedFont(StandardFonts[FONTS[f][2]]);
  const cache = new Map(), pages = doc.getPages(), redactPages = [];
  for (let i = 0; i < pages.length; i++) {
    for (const a of src.ann[i] || []) {
      if (a.type === 'note') continue;
      const img = a.type === 'image' ? await embedDataUrl(doc, a.src, cache) : null;
      burnAnn(doc, pages[i], a, fonts, img);
    }
    if (src.ann[i]?.some((a) => a.type === 'redact')) redactPages.push(i);
  }
  // Schwärzungen auch über Anmerkungen und Formularfeldern, die pdf.js über den Seiteninhalt zeichnet
  const cover = Object.fromEntries(redactPages.map((i) => [i, src.ann[i].filter((a) => a.type === 'redact').map(annBox)]));
  // Notizen bleiben echte PDF-Anmerkungen; auf geschwärzten Seiten erst nach dem Neuaufbau
  const used = new Set();
  const notes = (d, keep) => d.getPages().forEach((pg, i) => { if (keep(i)) (src.ann[i] || []).filter((a) => a.type === 'note').forEach((a) => writeNote(d, pg, a, used)); });
  notes(doc, (i) => !redactPages.includes(i));
  let bytes = await doc.save();
  if (redactPages.length) {
    bytes = await rasterizePages(bytes, redactPages, { dpi: 200, progress, cover });
    if (redactPages.some((i) => src.ann[i].some((a) => a.type === 'note'))) {
      const d2 = await PDFDocument.load(bytes, { updateMetadata: false });
      notes(d2, (i) => redactPages.includes(i));
      bytes = await d2.save();
    }
  }
  return bytes;
}
// Ersetzt die angegebenen Seiten durch Bilder ihrer Darstellung (für Schwärzen, Komprimieren, Graustufen).
// Das Seitenobjekt bleibt, damit Lesezeichen und Links weiter darauf zeigen; sein alter Inhalt wird entfernt und am Ende
// alles gelöscht, was nicht mehr erreichbar ist. Sonst stünde der geschwärzte Text weiter als verwaistes Objekt in der Datei.
const PAGE_CONTENT_KEYS = ['Contents', 'Resources', 'Annots', 'StructParents', 'Group', 'Thumb', 'PieceInfo', 'Metadata', 'AA', 'B', 'ID', 'PZ',
  'SeparationInfo', 'Tabs', 'TemplateInstantiated', 'PresSteps', 'UserUnit', 'VP', 'BleedBox', 'TrimBox', 'ArtBox', 'BoxColorInfo', 'Trans', 'Dur', 'AF', 'DPart', 'OutputIntents'];
// cover: je Seite Rechtecke (PDF-Koordinaten), die nach dem Zeichnen schwarz gefüllt werden
async function rasterizePages(bytes, idx, { dpi = 150, quality = 0.85, gray = false, progress, cover = {} } = {}) {
  const pjs = await loadPdfJs(bytes);
  const doc = await PDFDocument.load(bytes, { updateMetadata: false });
  const ctx = doc.context, gone = new Set();
  let n = 0;
  for (const i of idx) {
    const page = await pjs.getPage(i + 1);
    const vp1 = page.getViewport({ scale: 1 });
    const c = await renderToCanvas(page, dpi / 72, { gray });
    const cx = c.getContext('2d'), vp = page.getViewport({ scale: dpi / 72 });
    cx.fillStyle = '#000';
    for (const r of cover[i] || []) {
      const [x1, y1, x2, y2] = vp.convertToViewportRectangle(r);
      cx.fillRect(Math.min(x1, x2), Math.min(y1, y2), Math.abs(x2 - x1), Math.abs(y2 - y1));
    }
    const jpg = await doc.embedJpg(await canvasBytes(c, 'image/jpeg', quality));
    const pg = doc.getPage(i), node = pg.node;
    try { for (const r of node.lookup(PDFName.of('Annots'))?.asArray?.() || []) gone.add(r); } catch { /* keine Anmerkungen */ }
    for (const k of PAGE_CONTENT_KEYS) node.delete(PDFName.of(k));
    const box = ctx.obj([0, 0, vp1.width, vp1.height]);
    node.set(PDFName.of('MediaBox'), box);
    node.set(PDFName.of('CropBox'), box);
    node.set(PDFName.of('Rotate'), ctx.obj(0));
    node.set(PDFName.of('Resources'), ctx.obj({}));
    pg.drawImage(jpg, { x: 0, y: 0, width: vp1.width, height: vp1.height });
    progress?.(++n / idx.length);
    await sleep(0);
  }
  await pjs.destroy();
  dropFormFields(doc, gone);
  // Strukturbaum (Tags) verweist auf die alten Seiteninhalte und kann deren Text als ActualText/Alt enthalten
  doc.catalog.delete(PDFName.of('StructTreeRoot'));
  doc.catalog.delete(PDFName.of('MarkInfo'));
  dropUnreachable(doc);
  return await doc.save();
}
// Formularfelder, deren Widgets auf neu aufgebauten Seiten lagen, austragen: ihr Wert und ihre Darstellung enthalten Text der Seite
function dropFormFields(doc, gone) {
  let acro;
  try { acro = doc.catalog.lookup(PDFName.of('AcroForm')); } catch { return; }
  if (!(acro instanceof PDFDict) || !gone.size) return;
  acro.delete(PDFName.of('XFA'));   // XFA enthält die Feldwerte noch einmal
  const removed = new Set();
  const prune = (arr) => {
    if (!(arr instanceof PDFArray)) return;
    for (let k = arr.size() - 1; k >= 0; k--) {
      const ref = arr.get(k), d = doc.context.lookup(ref);
      if (gone.has(ref)) { arr.remove(k); removed.add(ref); continue; }
      const kids = d instanceof PDFDict ? d.lookup(PDFName.of('Kids')) : null;
      if (kids instanceof PDFArray && kids.size()) { prune(kids); if (!kids.size()) { arr.remove(k); removed.add(ref); } }
    }
  };
  prune(acro.lookup(PDFName.of('Fields')));
  const co = acro.lookup(PDFName.of('CO'));
  if (co instanceof PDFArray) for (let k = co.size() - 1; k >= 0; k--) if (removed.has(co.get(k))) co.remove(k);
}
// Löscht alle Objekte, die vom Trailer (Katalog, Info) aus nicht mehr erreichbar sind
function dropUnreachable(doc) {
  const ctx = doc.context, seen = new Set(), todo = [];
  const visit = (o) => {
    if (o instanceof PDFRef) { if (!seen.has(o)) { seen.add(o); todo.push(ctx.lookup(o)); } }
    else if (o instanceof PDFDict) for (const [, v] of o.entries()) visit(v);
    else if (o instanceof PDFArray) for (const v of o.asArray()) visit(v);
    else if (o?.dict instanceof PDFDict) visit(o.dict);   // Streams
  };
  const { Root, Info, Encrypt } = ctx.trailerInfo;
  [Root, Info, Encrypt].forEach(visit);
  while (todo.length) visit(todo.pop());
  for (const [ref] of ctx.enumerateIndirectObjects()) if (!seen.has(ref)) ctx.delete(ref);
}

/* ---------- Dateien anbieten ---------- */
async function offerFile(filename, data) {
  if (HOST) {
    const bytes = data instanceof Blob ? new Uint8Array(await data.arrayBuffer()) : data;
    try {
      const res = await HOST.saveDialog({ defaultName: filename, data: bytes });
      if (!res) { toast('Speichern abgebrochen'); return null; }
      toast(`„${res.name}“ gespeichert`);
      return res;
    } catch (e) { toast('Speichern fehlgeschlagen: ' + (e?.message || e)); return null; }
  }
  const blob = data instanceof Blob ? data : new Blob([data], { type: filename.endsWith('.pdf') ? 'application/pdf' : 'application/octet-stream' });
  if (S.downloads) {
    try { await S.downloads.save({ filename, data: blob }); toast(`„${filename}“ gespeichert`); return true; }
    catch (e) {
      if (e?.code === 'declined') { toast('Speichern abgebrochen'); return false; }
      if (e?.code === 'rate_limited') { toast('Bitte einen Moment warten und erneut speichern.'); return false; }
      if (e?.code !== 'unavailable' && e?.code !== 'not_granted') { toast('Speichern nicht möglich: ' + (e?.message || e?.code)); return false; }
    }
  }
  if (window.self !== window.top && !S.downloads) { toast('Speichern ist in dieser Ansicht nicht verfügbar.'); return false; }
  const url = URL.createObjectURL(blob);
  const a = h('a', { href: url, download: filename });
  document.body.append(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 4000);
  toast(`„${filename}“ wird heruntergeladen`);
  return true;
}
// Speichert das Dokument. Desktop: in die geöffnete Datei, sonst mit Dialog. Ergibt true bei Erfolg.
// Aufrufe laufen nacheinander. Geschrieben wird der Stand beim Start; was sich währenddessen ändert, bleibt als ungespeichert markiert.
let saveQueue = Promise.resolve(), saveCalls = 0;
function save(opts = {}) {
  const waited = saveCalls++ > 0;
  // wartet ein zweites Strg+S auf das laufende Speichern und ist danach nichts mehr zu tun, nicht noch einmal schreiben
  const run = saveQueue.then(() => (waited && !opts.as && !S.dirty && S.path ? true : saveNow(opts))).finally(() => { saveCalls--; });
  saveQueue = run.catch(() => false);
  return run;
}
// Passt der Name nicht mehr zur Datei (z. B. umbenannt), nicht stillschweigend die alte Datei überschreiben
const pathMatchesName = () => !!S.path && S.path.split(/[\\/]/).pop().toLowerCase() === S.name.toLowerCase();
async function saveNow({ as = false } = {}) {
  commitEdit();
  if (!S.pdf) return false;
  busy('Speichere …');
  const opened = S.opened, annJson = JSON.stringify(S.ann);
  const src = { bytes: S.bytes, ann: JSON.parse(annJson) };
  // Ergebnis übernehmen; true, wenn sich seit dem Start nichts geändert hat
  const done = (res) => {
    if (S.opened !== opened) return true;   // inzwischen anderes Dokument im Fenster
    if (res?.path) { S.name = res.name; S.path = res.path; rebaseHistory(); }
    const clean = S.bytes === src.bytes && JSON.stringify(S.ann) === annJson;
    setDirty(!clean);
    if (!clean) toast('Gespeichert. Änderungen während des Speicherns sind noch nicht gesichert.', 5000);
    return clean;
  };
  try {
    const bytes = await buildOutput({ src });
    if (HOST && S.path && /\.pdf$/i.test(S.path) && !as && pathMatchesName()) {
      try {
        const res = await HOST.saveFile(S.path, bytes);
        if (done(res)) toast(`„${res.name}“ gespeichert`);
        return true;
      } catch (e) {
        // z. B. schreibgeschützt, verschoben oder in einem anderen Programm geöffnet: Speicherort neu wählen lassen
        toast((e?.message || 'Speichern fehlgeschlagen') + ' Bitte einen anderen Ort wählen.', 5000);
      }
    }
    const res = await offerFile(baseName(S.name) + '.pdf', bytes);
    if (!res) return false;
    done(HOST ? res : null);
    return true;
  } catch (e) { console.error(e); toast('Speichern fehlgeschlagen: ' + e.message); return false; }
  finally { idle(); }
}
const saveAs = () => save({ as: true });
async function exportPages(idx, name) {
  const bytes = await buildOutput();
  const src = await PDFDocument.load(bytes);
  const out = await PDFDocument.create();
  (await out.copyPages(src, idx)).forEach((p) => out.addPage(p));
  await offerFile(name || `${baseName(S.name)}_Seiten_${compactRanges(idx)}.pdf`, await out.save());
}
function compactRanges(idx) {
  const s = [...idx].sort((a, b) => a - b), out = [];
  for (let k = 0; k < s.length; k++) { let j = k; while (j + 1 < s.length && s[j + 1] === s[j] + 1) j++; out.push(j > k ? `${s[k] + 1}-${s[j] + 1}` : `${s[k] + 1}`); k = j; }
  return out.join(',');
}
function parseRanges(str, n) {
  const groups = [];
  for (const part of str.split(/[;,]/).map((s) => s.trim()).filter(Boolean)) {
    const m = part.match(/^(\d+)?\s*-\s*(\d+|ende|end)?$/i) || part.match(/^(\d+)$/);
    if (!m) throw new Error(`„${part}“ ist kein gültiger Bereich (Beispiel: 1-3, 5, 8-ende)`);
    let a, b;
    if (m.length === 2) a = b = +m[1];
    else { a = m[1] ? +m[1] : 1; b = m[2] && /^\d+$/.test(m[2]) ? +m[2] : n; }
    if (a < 1 || b > n || a > b) throw new Error(`Bereich „${part}“ liegt außerhalb von 1–${n}`);
    groups.push(Array.from({ length: b - a + 1 }, (_, k) => a - 1 + k));
  }
  if (!groups.length) throw new Error('Bitte Seiten angeben');
  return groups;
}
async function zipAndOffer(files, zipName) {
  if (files.length === 1) return offerFile(files[0].name, files[0].data);
  const zip = new JSZip();
  files.forEach((f) => zip.file(f.name, f.data));
  return offerFile(zipName, await zip.generateAsync({ type: 'blob' }));
}

/* ---------- Drucken (nur außerhalb eingebetteter Ansichten) ---------- */
async function printDoc() {
  if (window.self !== window.top) { toast('Drucken geht hier nicht. Datei speichern und im PDF-Programm drucken.'); return; }
  commitEdit();
  busy('Bereite Druck vor …');
  const bytes = await buildOutput();
  const pjs = await loadPdfJs(bytes);
  if (HOST) {
    // Seiten einzeln als JPEG an die Hülle: ein einziger großer HTML-Text scheitert bei großen Dokumenten
    let job = null;
    try {
      job = await HOST.printBegin();
      for (let i = 1; i <= pjs.numPages; i++) {
        busy(`Bereite Druck vor … Seite ${i} von ${pjs.numPages}`);
        const c = await renderToCanvas(await pjs.getPage(i), 200 / 72);
        await HOST.printPage(job, await canvasBytes(c, 'image/jpeg', 0.92));
        c.width = c.height = 0;
      }
      idle('Druckdialog geöffnet');
      const id = job; job = null;
      await HOST.printEnd(id);
    } catch (e) { console.error(e); toast('Drucken fehlgeschlagen: ' + (e?.message || e)); if (job) HOST.printAbort(job); }
    await pjs.destroy();
    idle();
    return;
  }
  const frame = h('iframe', { style: 'position:fixed;width:0;height:0;border:0;right:0;bottom:0' });
  document.body.append(frame);
  const d = frame.contentDocument;
  d.open(); d.write('<!doctype html><html><head><style>@page{margin:0}body{margin:0}img{display:block;width:100%;page-break-after:always}</style></head><body></body></html>'); d.close();
  for (let i = 1; i <= pjs.numPages; i++) {
    const c = await renderToCanvas(await pjs.getPage(i), 150 / 72);
    const im = d.createElement('img'); im.src = c.toDataURL('image/jpeg', 0.92); d.body.append(im);
  }
  await Promise.all([...d.images].map((im) => im.decode().catch(() => {})));
  idle();
  frame.contentWindow.focus(); frame.contentWindow.print();
  setTimeout(() => frame.remove(), 60000);
}

/* ---------- Präsentation ---------- */
function startPresentation() {
  document.body.classList.add('present');
  S.prevZoom = S.zoom; S.prevSpread = S.spread; S.spread = false;
  document.documentElement.requestFullscreen?.().catch(() => {});
  setTimeout(() => { buildViewer(true); rezoom('fitp'); toast('Pfeiltasten blättern, Esc beendet'); }, 150);
}
function endPresentation() {
  if (!document.body.classList.contains('present')) return;
  document.body.classList.remove('present');
  if (document.fullscreenElement) document.exitFullscreen?.().catch(() => {});
  S.spread = S.prevSpread; S.zoom = S.prevZoom;
  setTimeout(() => buildViewer(true), 100);
}

/* ---------- Werkzeugkasten ---------- */
const TOOLS = [
  ['Zusammenstellen', [
    ['merge', 'Zusammenfügen', 'Mehrere PDFs und Bilder zu einer Datei verbinden', mergeTool],
    ['split', 'Teilen', 'In Einzelseiten, feste Abschnitte oder Bereiche aufteilen', splitTool],
    ['extract', 'Seiten extrahieren', 'Ausgewählte Seiten als neue PDF speichern', extractTool],
    ['fromimg', 'Bilder zu PDF', 'JPG, PNG, WebP zu einer PDF, Seitengröße wählbar', imagesTool],
    ['blank', 'Leere Seiten entfernen', 'Unbeschriebene Seiten finden und löschen', removeBlankTool],
    ['rotR', 'Alle Seiten drehen', 'Das ganze Dokument um 90° oder 180° drehen', rotateAllTool],
  ]],
  ['Bearbeiten', [
    ['form', 'Formular ausfüllen', 'Formularfelder bearbeiten, auf Wunsch fixieren', formTool],
    ['sign', 'Unterschreiben', 'Unterschrift zeichnen, tippen oder als Bild laden', () => signatureDialog()],
    ['watermark', 'Wasserzeichen', 'Text diagonal oder gekachelt über die Seiten legen', watermarkTool],
    ['numbers', 'Seitenzahlen, Kopf- und Fußzeile', 'Nummerierung oder eigenen Text auf jede Seite', numbersTool],
    ['crop', 'Ränder zuschneiden', 'Sichtbaren Seitenbereich in mm verkleinern', cropTool],
    ['meta', 'Dokumenteigenschaften', 'Titel, Autor, Betreff, Stichwörter', metaTool],
  ]],
  ['Optimieren und umwandeln', [
    ['compress', 'Komprimieren', 'Dateigröße verringern, Qualität wählbar', compressTool],
    ['gray', 'Graustufen', 'Farbiges Dokument in Schwarzweiß umwandeln', grayTool],
    ['flatten', 'Reduzieren', 'Formularfelder und Anmerkungen fest in die Seiten einbrennen', flattenTool],
    ['toimg', 'PDF zu Bildern', 'Seiten als PNG oder JPG exportieren', toImagesTool],
    ['txt', 'Text extrahieren', 'Den gesamten Text als TXT-Datei speichern', textTool],
    ['lock', 'Passwort entfernen', 'Geschützte PDF beim Öffnen entsperren', () => toast('Öffnen Sie die geschützte Datei: Nach Eingabe des Passworts entsteht eine entsperrte Kopie.', 4500)],
  ]],
];
function renderToolbox() {
  const el = $('#toolbox');
  el.textContent = '';
  for (const [title, list] of TOOLS) {
    el.append(h('h2', {}, title), h('div', { class: 'tgrid' }, list.map(([icon, name, desc, fn]) =>
      h('button', { class: 'tcard', onclick: fn }, h('span', { html: svg(icon) }), h('div', {}, h('b', {}, name), h('span', {}, desc))))));
  }
  el.append(h('p', { class: 'note', style: 'margin-top:22px;max-width:70ch' },
    'Alle Werkzeuge arbeiten nur in diesem Browser. Die Dateien werden nirgendwohin hochgeladen. Texterkennung (OCR) und das Setzen von Passwörtern sind nicht enthalten.'));
}

function progressBar() { const i = h('i'); return { el: h('div', { class: 'progress' }, i), set: (f) => (i.style.width = Math.round(f * 100) + '%') }; }

async function mergeTool() {
  let files = [];
  const list = h('div', { class: 'formlist' });
  const incl = checkbox('cur', `Aktuelles Dokument „${S.name}“ als Erstes`, true);
  const draw = () => {
    list.textContent = '';
    if (!files.length) list.append(h('div', { class: 'empty' }, 'Noch keine Dateien gewählt.'));
    files.forEach((f, k) => list.append(h('div', { class: 'row', style: 'align-items:center;gap:6px' },
      h('span', { class: 'num', style: 'flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap' }, `${k + 1}. ${f.name}`),
      h('button', { class: 'btn icon', type: 'button', title: 'Nach oben', disabled: !k, html: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="m6 15 6-6 6 6"/></svg>', onclick: () => { [files[k - 1], files[k]] = [files[k], files[k - 1]]; draw(); } }),
      h('button', { class: 'btn icon', type: 'button', title: 'Entfernen', html: svg('trash'), onclick: () => { files.splice(k, 1); draw(); } }))));
  };
  const inp = h('input', { type: 'file', multiple: true, accept: 'application/pdf,image/*,.pdf', hidden: true, onchange: (e) => { files.push(...e.target.files); e.target.value = ''; draw(); } });
  draw();
  await dialog({ title: 'Zusammenfügen', ok: 'Zusammenfügen',
    body: [incl, list, inp, h('button', { class: 'btn outline', type: 'button', html: svg('plus') + 'Dateien hinzufügen', onclick: () => inp.click() }),
      h('div', { class: 'note' }, 'Das Ergebnis ersetzt die Ansicht und kann danach weiter bearbeitet und gespeichert werden.')],
    onOk: async (form) => {
      if (!files.length) { toast('Bitte Dateien hinzufügen.'); return false; }
      const useCur = form.cur.checked;
      if (useCur) { await insertFiles(files, S.pages.length); return; }
      const out = await PDFDocument.create();
      for (const f of files) { const d = await fileToDoc(f); if (d) (await out.copyPages(d, d.getPageIndices())).forEach((p) => out.addPage(p)); }
      if (!out.getPageCount()) return false;
      await replaceDoc(await out.save(), 'Zusammengefügt.pdf', { detach: true });
    } });
}
// detach: das Ergebnis ist ein neues Dokument; es gehört nicht mehr zur geöffneten Datei, Speichern fragt nach dem Ort
async function replaceDoc(bytes, name, { detach = false } = {}) {
  snapshot();
  if (detach) S.path = null;
  const keepHist = S.history;
  await openBytes(bytes, name, { fresh: false });
  S.history = keepHist; setDirty(true); updateUndoButtons();
}

async function splitTool() {
  const n = S.pages.length;
  const every = h('input', { type: 'number', name: 'every', min: 1, max: n, value: 1 });
  const ranges = h('input', { type: 'text', name: 'ranges', placeholder: 'z. B. 1-3, 4-6, 7-ende' });
  await dialog({ title: 'Teilen', ok: 'Teilen und speichern',
    body: [radios('how', [['every', 'Alle N Seiten'], ['ranges', 'Nach Bereichen'], ['odd', 'Gerade / ungerade']], 'every'),
      field('Seiten pro Datei', every), field('Bereiche (je Bereich eine Datei)', ranges),
      h('div', { class: 'note' }, `Das Dokument hat ${n} Seiten. Mehrere Dateien werden als ZIP gespeichert.`)],
    onOk: async (form) => {
      const how = form.how.value;
      let groups;
      if (how === 'every') { const k = Math.max(1, +every.value || 1); groups = []; for (let i = 0; i < n; i += k) groups.push(Array.from({ length: Math.min(k, n - i) }, (_, j) => i + j)); }
      else if (how === 'ranges') groups = parseRanges(ranges.value, n);
      else groups = [identity().filter((i) => i % 2 === 0), identity().filter((i) => i % 2 === 1)].filter((g) => g.length);
      busy('Teile …');
      const src = await PDFDocument.load(await buildOutput());
      const files = [];
      for (const g of groups) {
        const out = await PDFDocument.create();
        (await out.copyPages(src, g)).forEach((p) => out.addPage(p));
        files.push({ name: `${baseName(S.name)}_${how === 'odd' ? (g[0] === 0 ? 'ungerade' : 'gerade') : 'S' + compactRanges(g)}.pdf`, data: await out.save() });
      }
      idle();
      await zipAndOffer(files, `${baseName(S.name)}_geteilt.zip`);
    } });
}
async function extractTool() {
  const sel = S.orgSel.size ? compactRanges([...S.orgSel]) : String(S.cur + 1);
  const inp = h('input', { type: 'text', name: 'r', value: sel });
  await dialog({ title: 'Seiten extrahieren', ok: 'Als PDF speichern',
    body: [field('Seiten', inp), h('div', { class: 'note' }, 'Beispiele: 2 · 1-4 · 1,3,7-9 · 5-ende')],
    onOk: async () => { const idx = parseRanges(inp.value, S.pages.length).flat(); await exportPages(idx); } });
}
async function imagesTool() {
  let files = [];
  const lbl = h('div', { class: 'empty' }, 'Keine Bilder gewählt.');
  const inp = h('input', { type: 'file', multiple: true, accept: 'image/*', hidden: true, onchange: (e) => { files = [...e.target.files]; lbl.textContent = files.map((f) => f.name).join(', '); } });
  const margin = h('input', { type: 'number', name: 'margin', value: 0, min: 0, max: 50 });
  await dialog({ title: 'Bilder zu PDF', ok: 'Erstellen',
    body: [inp, h('button', { class: 'btn outline', type: 'button', html: svg('image') + 'Bilder wählen', onclick: () => inp.click() }), lbl,
      field('Seitengröße', h('select', { name: 'size' }, h('option', { value: 'a4auto' }, 'A4, Ausrichtung nach Bild'), h('option', { value: 'a4p' }, 'A4 hoch'), h('option', { value: 'a4l' }, 'A4 quer'), h('option', { value: 'image' }, 'Wie das Bild'))),
      field('Rand in mm', margin),
      radios('target', [['new', 'Neues Dokument'], ['append', 'An aktuelles anhängen']], 'new')],
    onOk: async (form) => {
      if (!files.length) { toast('Bitte Bilder wählen.'); return false; }
      const doc = await imagesToDoc(files, { size: form.size.value, margin: (+margin.value || 0) * PT_PER_MM });
      if (!doc) return false;
      if (form.target.value === 'append') {
        const order = identity().concat(doc.getPageIndices().map((k) => ({ ext: { doc, idx: k } })));
        await rebuild(order, { msg: 'Bilder angehängt' });
      } else await replaceDoc(await doc.save(), baseName(files[0].name.replace(/\.\w+$/, '')) + '.pdf', { detach: true });
    } });
}
async function removeBlankTool() {
  const thr = h('input', { type: 'range', name: 'thr', min: 0.1, max: 3, step: 0.1, value: 0.6 });
  await dialog({ title: 'Leere Seiten entfernen', ok: 'Suchen und entfernen',
    body: [field('Empfindlichkeit (Anteil nicht weißer Pixel in %)', thr), h('div', { class: 'note' }, 'Seiten mit weniger Inhalt als dem eingestellten Anteil gelten als leer. Gescannte Leerseiten haben oft etwas Rauschen, dann den Wert erhöhen.')],
    onOk: async () => {
      busy('Prüfe Seiten …');
      const blank = [];
      for (let i = 0; i < S.pages.length; i++) {
        const c = await renderToCanvas(await S.pdf.getPage(i + 1), 0.4);
        const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
        let ink = 0; for (let k = 0; k < d.length; k += 4) if (d[k] + d[k + 1] + d[k + 2] < 690) ink++;
        if (ink / (d.length / 4) * 100 < +thr.value && !S.ann[i].length) blank.push(i);
      }
      idle();
      if (!blank.length) { toast('Keine leeren Seiten gefunden.'); return; }
      if (blank.length === S.pages.length) { toast('Alle Seiten wirken leer. Empfindlichkeit verringern.'); return false; }
      await deletePages(blank);
      toast(`${blank.length} leere Seite(n) entfernt: ${compactRanges(blank)}`);
    } });
}
async function rotateAllTool() {
  await dialog({ title: 'Alle Seiten drehen', ok: 'Drehen', body: radios('deg', [['90', '90° rechts'], ['-90', '90° links'], ['180', '180°']], '90'),
    onOk: async (form) => rotatePages(identity(), +form.deg.value) });
}

/* Formulare */
async function formTool() {
  const doc = await PDFDocument.load(S.bytes, { updateMetadata: false });
  let fields = [];
  try { fields = doc.getForm().getFields(); } catch { /* kein Formular */ }
  if (!fields.length) { toast('Dieses Dokument enthält keine Formularfelder.'); return; }
  const rows = [];
  const list = h('div', { class: 'formlist' });
  for (const f of fields) {
    const name = f.getName();
    const type = f instanceof PDFLib.PDFTextField ? 'PDFTextField' : f instanceof PDFLib.PDFCheckBox ? 'PDFCheckBox' : f instanceof PDFLib.PDFDropdown ? 'PDFDropdown'
      : f instanceof PDFLib.PDFOptionList ? 'PDFOptionList' : f instanceof PDFLib.PDFRadioGroup ? 'PDFRadioGroup' : 'other';
    let input;
    try {
      if (type === 'PDFTextField') { input = h(f.isMultiline() ? 'textarea' : 'input', { type: 'text' }); input.value = f.getText() || ''; }
      else if (type === 'PDFCheckBox') input = h('input', { type: 'checkbox', checked: f.isChecked() });
      else if (type === 'PDFDropdown' || type === 'PDFOptionList') { input = h('select', {}, h('option', { value: '' }, '–'), f.getOptions().map((o) => h('option', { value: o, selected: f.getSelected().includes(o) }, o))); }
      else if (type === 'PDFRadioGroup') { input = h('select', {}, h('option', { value: '' }, '–'), f.getOptions().map((o) => h('option', { value: o, selected: f.getSelected() === o }, o))); }
      else continue;
    } catch { continue; }
    if (f.isReadOnly?.()) input.disabled = true;
    rows.push({ f, type, input });
    list.append(type === 'PDFCheckBox' ? h('label', { class: 'check' }, input, name) : field(name, input));
  }
  await dialog({ title: 'Formular ausfüllen', ok: 'Übernehmen', wide: true,
    body: [list, checkbox('flat', 'Felder danach fixieren (nicht mehr änderbar)', false)],
    onOk: async (form) => {
      for (const { f, type, input } of rows) {
        try {
          if (type === 'PDFTextField') f.setText(input.value);
          else if (type === 'PDFCheckBox') input.checked ? f.check() : f.uncheck();
          else if (type === 'PDFDropdown' || type === 'PDFOptionList') input.value ? f.select(input.value) : f.clear();
          else if (type === 'PDFRadioGroup') input.value ? f.select(input.value) : f.clear();
        } catch (e) { console.warn(f.getName(), e); }
      }
      const font = await doc.embedFont(StandardFonts.Helvetica);
      try { doc.getForm().updateFieldAppearances(font); } catch (e) { console.warn(e); }
      if (form.flat.checked) doc.getForm().flatten();
      snapshot();
      await openBytes(await doc.save(), S.name, { keepAnn: true, keepPos: true, fresh: false });
      toast('Formular übernommen');
    } });
}

/* Unterschrift */
function signatureDialog() {
  let stored = [];
  try { stored = JSON.parse(localStorage.getItem('pdfwerk.sigs') || '[]'); } catch { /* kein Speicher */ }
  const pad = h('canvas', { class: 'sigpad', width: 1000, height: 340 });
  const pctx = pad.getContext('2d');
  let drawing = false, empty = true, last;
  const pos = (e) => { const r = pad.getBoundingClientRect(); return [(e.clientX - r.left) * pad.width / r.width, (e.clientY - r.top) * pad.height / r.height]; };
  pad.addEventListener('pointerdown', (e) => { drawing = true; empty = false; last = pos(e); pad.setPointerCapture(e.pointerId); });
  pad.addEventListener('pointermove', (e) => {
    if (!drawing) return; const p = pos(e);
    pctx.strokeStyle = sigColor; pctx.lineWidth = 5 * (e.pressure && e.pointerType === 'pen' ? 0.5 + e.pressure : 1); pctx.lineCap = 'round'; pctx.lineJoin = 'round';
    pctx.beginPath(); pctx.moveTo(...last); pctx.lineTo(...p); pctx.stroke(); last = p;
  });
  pad.addEventListener('pointerup', () => (drawing = false));
  let sigColor = '#14246b';
  const typed = h('input', { type: 'text', name: 'typed', placeholder: 'Vor- und Nachname' });
  const preview = h('div', { class: 'sigtyped' }, ' ');
  typed.addEventListener('input', () => (preview.textContent = typed.value || ' '));
  let upload = null;
  const upInp = h('input', { type: 'file', accept: 'image/*', hidden: true, onchange: async (e) => { const f = e.target.files[0]; if (f) { upload = await fileToDataUrl(f); upLbl.textContent = f.name; } } });
  const upLbl = h('span', { class: 'empty' }, 'Keine Datei');
  const panes = {
    draw: h('div', { class: 'field' }, pad, h('div', { class: 'row', style: 'justify-content:space-between;align-items:center' },
      h('div', { class: 'swatches' }, ['#14246b', '#111111', '#1f6fd1'].map((c) => h('button', { type: 'button', class: 'sw', style: `background:${c}`, title: c, onclick: () => (sigColor = c) }))),
      h('button', { type: 'button', class: 'btn outline', onclick: () => { pctx.clearRect(0, 0, pad.width, pad.height); empty = true; } }, 'Leeren'))),
    type: h('div', { class: 'field' }, typed, preview),
    upload: h('div', { class: 'field' }, upInp, h('button', { type: 'button', class: 'btn outline', html: svg('image') + 'Bild wählen', onclick: () => upInp.click() }), upLbl,
      h('div', { class: 'note' }, 'Ein Scan der Unterschrift auf weißem Papier. Weiß wird automatisch transparent.')),
  };
  const tabs = radios('kind', [['draw', 'Zeichnen'], ['type', 'Tippen'], ['upload', 'Bild']], 'draw');
  const showPane = () => { const k = tabs.querySelector('input:checked').value; Object.entries(panes).forEach(([n, el]) => (el.hidden = n !== k)); };
  tabs.addEventListener('change', showPane);
  const storedEl = stored.length ? h('div', { class: 'field' }, h('span', {}, 'Gespeicherte Unterschriften'),
    h('div', { class: 'stamps' }, stored.map((src, k) => h('button', { type: 'button', title: 'Verwenden', onclick: () => { document.querySelector('.backdrop')?.remove(); placeDataUrl(src, 'sign', 150); } },
      h('img', { src, style: 'max-height:50px', alt: 'Unterschrift ' + (k + 1) }))))) : null;
  showPane();
  dialog({ title: 'Unterschreiben', ok: 'Platzieren', wide: true,
    body: [storedEl, tabs, panes.draw, panes.type, panes.upload, checkbox('keep', 'In diesem Browser merken', true)],
    onOk: async (form) => {
      const k = form.kind.value;
      let src;
      if (k === 'draw') { if (empty) { toast('Bitte zuerst unterschreiben.'); return false; } src = trimCanvas(pad).toDataURL('image/png'); }
      else if (k === 'type') {
        if (!typed.value.trim()) { toast('Bitte Namen eingeben.'); return false; }
        await document.fonts.load('80px Caveat').catch(() => {});
        const c = document.createElement('canvas'); c.width = 1400; c.height = 260;
        const x = c.getContext('2d'); x.font = '150px Caveat, "Segoe Script", cursive'; x.fillStyle = sigColor; x.textBaseline = 'middle'; x.fillText(typed.value, 20, 130);
        src = trimCanvas(c).toDataURL('image/png');
      } else {
        if (!upload) { toast('Bitte ein Bild wählen.'); return false; }
        src = await whiteToTransparent(upload);
      }
      if (form.keep.checked) { try { localStorage.setItem('pdfwerk.sigs', JSON.stringify([src, ...stored.filter((s) => s !== src)].slice(0, 4))); } catch { /* voll oder gesperrt */ } }
      await placeDataUrl(src, 'sign', 150);
    } });
}
function trimCanvas(c) {
  const ctx = c.getContext('2d'), d = ctx.getImageData(0, 0, c.width, c.height).data;
  let x1 = c.width, y1 = c.height, x2 = 0, y2 = 0;
  for (let y = 0; y < c.height; y++) for (let x = 0; x < c.width; x++) if (d[(y * c.width + x) * 4 + 3] > 10) { if (x < x1) x1 = x; if (x > x2) x2 = x; if (y < y1) y1 = y; if (y > y2) y2 = y; }
  if (x2 < x1) return c;
  const pad = 6, o = document.createElement('canvas');
  o.width = x2 - x1 + 2 * pad; o.height = y2 - y1 + 2 * pad;
  o.getContext('2d').drawImage(c, x1 - pad, y1 - pad, o.width, o.height, 0, 0, o.width, o.height);
  return o;
}
async function whiteToTransparent(src) {
  const im = new Image(); im.src = src; await im.decode();
  const c = document.createElement('canvas'); c.width = im.naturalWidth; c.height = im.naturalHeight;
  const x = c.getContext('2d'); x.drawImage(im, 0, 0);
  const d = x.getImageData(0, 0, c.width, c.height);
  for (let k = 0; k < d.data.length; k += 4) { const l = (d.data[k] + d.data[k + 1] + d.data[k + 2]) / 3; if (l > 200) d.data[k + 3] = Math.max(0, 255 - (l - 200) * 4.6); }
  x.putImageData(d, 0, 0);
  return trimCanvas(c).toDataURL('image/png');
}
const fileToDataUrl = (f) => new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res(r.result); r.onerror = rej; r.readAsDataURL(f); });
async function placeDataUrl(src, kind, defW = 180) {
  const im = new Image(); im.src = src; await im.decode();
  startPlacing({ src, w: im.naturalWidth, h: im.naturalHeight, defW }, kind);
}
async function placeImageFile(f) {
  let src = await fileToDataUrl(f);
  if (!/^data:image\/(png|jpeg)/.test(src)) {
    const im = new Image(); im.src = src; await im.decode();
    const c = document.createElement('canvas'); c.width = im.naturalWidth; c.height = im.naturalHeight; c.getContext('2d').drawImage(im, 0, 0);
    src = c.toDataURL('image/png');
  }
  await placeDataUrl(src, 'image', 200);
}

/* Stempel */
const STAMPS = [['GENEHMIGT', '#2b7a3e'], ['ERLEDIGT', '#2b7a3e'], ['GEPRÜFT', '#1f5fb8'], ['ENTWURF', '#5b5f6b'], ['VERTRAULICH', '#b8322c'], ['ABGELEHNT', '#b8322c'], ['KOPIE', '#5b5f6b'], ['EINGANG', '#1f5fb8']];
function stampImage(text, color, withDate) {
  const c = document.createElement('canvas'), x = c.getContext('2d');
  const date = new Date().toLocaleDateString('de-DE');
  x.font = 'bold 64px Helvetica, Arial, sans-serif';
  const w = Math.max(x.measureText(text).width, withDate ? 360 : 0) + 70, hh = withDate ? 150 : 110;
  c.width = w; c.height = hh;
  x.strokeStyle = color; x.fillStyle = color; x.lineWidth = 7;
  const r = 16;
  x.beginPath(); x.roundRect(5, 5, w - 10, hh - 10, r); x.stroke();
  x.lineWidth = 2; x.beginPath(); x.roundRect(14, 14, w - 28, hh - 28, r - 6); x.stroke();
  x.font = 'bold 64px Helvetica, Arial, sans-serif'; x.textAlign = 'center'; x.textBaseline = 'middle';
  x.fillText(text, w / 2, withDate ? 62 : hh / 2 + 2);
  if (withDate) { x.font = '30px Helvetica, Arial, sans-serif'; x.fillText(date, w / 2, 112); }
  return c.toDataURL('image/png');
}
function stampDialog() {
  const custom = h('input', { type: 'text', name: 'custom', placeholder: 'Eigener Text' });
  const dated = checkbox('dated', 'Mit Datum', true);
  const grid = h('div', { class: 'stamps' });
  const draw = () => {
    grid.textContent = '';
    const withDate = dated.querySelector('input').checked;
    for (const [t, col] of STAMPS) grid.append(h('button', { type: 'button', onclick: () => { document.querySelector('.backdrop')?.remove(); placeDataUrl(stampImage(t, col, withDate), 'stamp', 130); } }, h('img', { src: stampImage(t, col, withDate), alt: t, style: 'max-width:100%' })));
  };
  dated.addEventListener('change', draw); draw();
  dialog({ title: 'Stempel', ok: 'Eigenen Stempel platzieren', body: [dated, grid, field('Eigener Stempel', custom)],
    onOk: async () => { if (!custom.value.trim()) { toast('Einen Stempel anklicken oder Text eingeben.'); return false; } await placeDataUrl(stampImage(custom.value.trim().toUpperCase(), '#b8322c', dated.querySelector('input').checked), 'stamp', 130); } });
}

/* Wasserzeichen */
async function watermarkTool() {
  const text = h('input', { type: 'text', name: 'text', value: 'VERTRAULICH' });
  const size = h('input', { type: 'number', name: 'size', value: 60, min: 8, max: 200 });
  const op = h('input', { type: 'number', name: 'op', value: 20, min: 5, max: 100 });
  const color = h('input', { type: 'color', name: 'color', value: '#b8322c', style: 'height:34px;width:60px;border:1px solid var(--line);border-radius:6px;background:var(--bg)' });
  const pagesInp = h('input', { type: 'text', name: 'pages', value: '1-ende' });
  await dialog({ title: 'Wasserzeichen', ok: 'Anwenden',
    body: [field('Text', text), h('div', { class: 'row' }, field('Schriftgröße (pt)', size), field('Deckkraft (%)', op), field('Farbe', color)),
      radios('layout', [['diag', 'Diagonal'], ['horiz', 'Waagerecht'], ['tile', 'Gekachelt']], 'diag'), field('Seiten', pagesInp)],
    onOk: async (form) => {
      const idx = parseRanges(pagesInp.value, S.pages.length).flat();
      const doc = await PDFDocument.load(S.bytes, { updateMetadata: false });
      const font = await doc.embedFont(StandardFonts.HelveticaBold);
      const t = winAnsiSafe(font, text.value || ' '), fs = +size.value || 60, col = pdfColor(color.value), opacity = (+op.value || 20) / 100;
      const layout = form.layout.value;
      for (const i of idx) {
        const page = doc.getPage(i);
        const { x: bx, y: by, width: W, height: H } = page.getCropBox();
        const pr = page.getRotation().angle;
        const tw = font.widthOfTextAtSize(t, fs);
        const put = (cx, cy, ang) => {
          const a = (ang + pr) * Math.PI / 180;
          page.drawText(t, { x: cx - Math.cos(a) * tw / 2 + Math.sin(a) * fs * 0.35, y: cy - Math.sin(a) * tw / 2 - Math.cos(a) * fs * 0.35, size: fs, font, color: col, opacity, rotate: degrees(ang + pr) });
        };
        if (layout === 'tile') {
          const sx = tw + 80, sy = fs * 3.2;
          for (let yy = by + sy / 2; yy < by + H; yy += sy) for (let xx = bx + (Math.round(yy / sy) % 2 ? sx / 2 : 0); xx < bx + W + sx; xx += sx) put(xx, yy, 30);
        } else put(bx + W / 2, by + H / 2, layout === 'diag' ? (Math.atan2(pr % 180 ? W : H, pr % 180 ? H : W) * 180 / Math.PI) : 0);
      }
      snapshot();
      await openBytes(await doc.save(), S.name, { keepAnn: true, keepPos: true, fresh: false });
      toast('Wasserzeichen angewendet');
    } });
}

/* Seitenzahlen sowie Kopf- und Fußzeilen */
async function numbersTool() {
  const pattern = h('input', { type: 'text', name: 'pattern', value: 'Seite {n} von {N}' });
  const start = h('input', { type: 'number', name: 'start', value: 1 });
  const fs = h('input', { type: 'number', name: 'fs', value: 10, min: 5, max: 40 });
  const margin = h('input', { type: 'number', name: 'margin', value: 10, min: 2, max: 60 });
  const pos = h('select', { name: 'pos' }, [['bc', 'Unten mittig'], ['br', 'Unten rechts'], ['bl', 'Unten links'], ['tc', 'Oben mittig'], ['tr', 'Oben rechts'], ['tl', 'Oben links']].map(([v, l]) => h('option', { value: v }, l)));
  const pagesInp = h('input', { type: 'text', name: 'pages', value: '1-ende' });
  await dialog({ title: 'Seitenzahlen, Kopf- und Fußzeile', ok: 'Einfügen',
    body: [field('Text', pattern), h('div', { class: 'note' }, 'Platzhalter: {n} Seitenzahl · {N} Gesamtzahl · {datum} heutiges Datum · {datei} Dateiname. Für eine Kopfzeile einfach eigenen Text und „Oben“ wählen.'),
      h('div', { class: 'row' }, field('Position', pos), field('Erste Nummer', start)),
      h('div', { class: 'row' }, field('Schriftgröße (pt)', fs), field('Abstand zum Rand (mm)', margin)),
      field('Auf Seiten', pagesInp), checkbox('mirror', 'Links/rechts auf geraden Seiten spiegeln (Buchdruck)', false)],
    onOk: async (form) => {
      const idx = parseRanges(pagesInp.value, S.pages.length).flat();
      const doc = await PDFDocument.load(S.bytes, { updateMetadata: false });
      const font = await doc.embedFont(StandardFonts.Helvetica);
      const size = +fs.value || 10, m = (+margin.value || 10) * PT_PER_MM, total = idx.length;
      idx.forEach((i, k) => {
        const page = doc.getPage(i);
        const txt = winAnsiSafe(font, pattern.value.replace(/\{n\}/g, String(k + (+start.value || 1))).replace(/\{N\}/g, String(total + (+start.value || 1) - 1))
          .replace(/\{datum\}/g, new Date().toLocaleDateString('de-DE')).replace(/\{datei\}/g, baseName(S.name)));
        let p = form.pos.value;
        if (form.mirror.checked && (k + (+start.value || 1)) % 2 === 0) p = p.replace('r', 'X').replace('l', 'r').replace('X', 'l');
        const tw = font.widthOfTextAtSize(txt, size);
        const { x: bx, y: by, width: cw, height: ch } = page.getCropBox();
        const r = page.getRotation().angle;
        // Position in Bildschirmrichtung bestimmen und in Seitenkoordinaten umrechnen
        const SW = r % 180 ? ch : cw, SH = r % 180 ? cw : ch;
        const sx = p[1] === 'l' ? m : p[1] === 'r' ? SW - m - tw : (SW - tw) / 2;
        const sy = p[0] === 't' ? m + size * 0.8 : SH - m;
        const [ux, uy] = { 0: [sx, SH - sy], 90: [sy, sx], 180: [SW - sx, sy], 270: [SH - sy, SW - sx] }[r];
        page.drawText(txt, { x: bx + ux, y: by + uy, size, font, color: rgb(0.15, 0.15, 0.15), rotate: degrees(r) });
      });
      snapshot();
      await openBytes(await doc.save(), S.name, { keepAnn: true, keepPos: true, fresh: false });
      toast('Text auf ' + total + ' Seiten eingefügt');
    } });
}

/* Zuschneiden */
async function cropTool() {
  const mk = (n, v) => h('input', { type: 'number', name: n, value: v, min: 0, max: 200, step: 0.5 });
  const t = mk('t', 10), b = mk('b', 10), l = mk('l', 10), r = mk('r', 10);
  const pagesInp = h('input', { type: 'text', name: 'pages', value: String(S.cur + 1) });
  await dialog({ title: 'Ränder zuschneiden', ok: 'Zuschneiden',
    body: [h('div', { class: 'row' }, field('Oben (mm)', t), field('Unten (mm)', b)), h('div', { class: 'row' }, field('Links (mm)', l), field('Rechts (mm)', r)),
      field('Seiten', pagesInp), h('div', { class: 'note' }, 'Die Ränder beziehen sich auf die Ansicht am Bildschirm. Der Inhalt außerhalb wird ausgeblendet, nicht gelöscht.')],
    onOk: async () => {
      const idx = parseRanges(pagesInp.value, S.pages.length).flat();
      const doc = await PDFDocument.load(S.bytes, { updateMetadata: false });
      for (const i of idx) {
        const page = doc.getPage(i), box = page.getCropBox(), rot = page.getRotation().angle;
        let [mt, mb, ml, mr] = [t, b, l, r].map((x) => (+x.value || 0) * PT_PER_MM);
        // Bildschirmränder in Seitenränder übersetzen
        if (rot === 90) [mt, mr, mb, ml] = [mr, mb, ml, mt];
        else if (rot === 180) [mt, mb, ml, mr] = [mb, mt, mr, ml];
        else if (rot === 270) [mt, mr, mb, ml] = [ml, mt, mr, mb];
        const w = box.width - ml - mr, hh = box.height - mt - mb;
        if (w < 20 || hh < 20) throw new Error('Die Ränder sind größer als die Seite.');
        page.setCropBox(box.x + ml, box.y + mb, w, hh);
      }
      snapshot();
      await openBytes(await doc.save(), S.name, { keepAnn: true, keepPos: true, fresh: false });
      toast('Zugeschnitten');
    } });
}

/* Metadaten */
async function metaTool() {
  const doc = await PDFDocument.load(S.bytes, { updateMetadata: false });
  const mk = (n, v) => { const i = h('input', { type: 'text', name: n }); i.value = v || ''; return i; };
  const fName = mk('fname', S.name);
  const ti = mk('ti', doc.getTitle()), au = mk('au', doc.getAuthor()), su = mk('su', doc.getSubject()), kw = mk('kw', doc.getKeywords()), cr = mk('cr', doc.getCreator());
  const info = h('div', { class: 'note num' }, `${S.pages.length} Seiten · ${fmtBytes(S.bytes.length)} · erstellt ${doc.getCreationDate()?.toLocaleString('de-DE') || 'unbekannt'} · Programm ${doc.getProducer() || 'unbekannt'}`);
  await dialog({ title: 'Dokumenteigenschaften', ok: 'Übernehmen',
    body: [info, field('Dateiname', fName), field('Titel', ti), field('Autor', au), field('Betreff', su), field('Stichwörter', kw), field('Anwendung', cr)],
    onOk: async () => {
      doc.setTitle(ti.value); doc.setAuthor(au.value); doc.setSubject(su.value); doc.setKeywords(kw.value.split(/[,;]\s*/).filter(Boolean)); doc.setCreator(cr.value);
      snapshot();
      const nm = (fName.value.trim() || S.name).replace(/[\\/:*?"<>|]/g, '_');
      const newName = /\.pdf$/i.test(nm) ? nm : nm + '.pdf';
      // neuer Dateiname: Speichern legt eine neue Datei an statt die alte zu überschreiben
      if (newName !== S.name) S.path = null;
      await openBytes(await doc.save(), newName, { keepAnn: true, keepPos: true, fresh: false });
      toast('Eigenschaften gespeichert');
    } });
}

/* Komprimieren und Graustufen */
async function compressTool() {
  const before = S.bytes.length;
  const pb = progressBar();
  await dialog({ title: 'Komprimieren', ok: 'Komprimieren',
    body: [radios('lvl', [['lossless', 'Verlustfrei'], ['150', 'Mittel (150 dpi)'], ['110', 'Stark (110 dpi)'], ['72', 'Maximal (72 dpi)']], '150'),
      h('div', { class: 'note' }, 'Verlustfrei ordnet nur die Dateistruktur neu. Die anderen Stufen speichern jede Seite als Bild: deutlich kleiner, aber Text ist danach nicht mehr markierbar.'),
      pb.el, h('div', { class: 'note num' }, 'Aktuelle Größe: ' + fmtBytes(before))],
    onOk: async (form) => {
      const lvl = form.lvl.value;
      busy('Komprimiere …');
      let bytes;
      if (lvl === 'lossless') { const d = await PDFDocument.load(await buildOutput()); bytes = await d.save({ useObjectStreams: true }); }
      else bytes = await rasterizePages(await buildOutput(), identity(), { dpi: +lvl, quality: lvl === '72' ? 0.6 : 0.72, progress: pb.set });
      idle();
      if (bytes.length >= before && lvl === 'lossless') { toast('Die Datei ist bereits optimal gespeichert.'); return; }
      await replaceDoc(bytes, S.name);
      toast(`${fmtBytes(before)} → ${fmtBytes(bytes.length)} (${Math.round((1 - bytes.length / before) * 100)} % kleiner)`, 5000);
    } });
}
async function grayTool() {
  const pb = progressBar();
  await dialog({ title: 'Graustufen', ok: 'Umwandeln', body: [h('div', { class: 'note' }, 'Jede Seite wird in Graustufen neu gerendert (150 dpi). Text ist danach nicht mehr markierbar.'), pb.el],
    onOk: async () => {
      busy('Wandle um …');
      const bytes = await rasterizePages(await buildOutput(), identity(), { dpi: 150, quality: 0.8, gray: true, progress: pb.set });
      await replaceDoc(bytes, S.name); idle();
    } });
}
async function flattenTool() {
  await dialog({ title: 'Reduzieren', ok: 'Reduzieren', body: h('div', { class: 'note' }, 'Anmerkungen, Unterschriften und Formularfelder werden fest in die Seiten geschrieben. Danach sind sie keine eigenen Objekte mehr.'),
    onOk: async () => {
      const doc = await PDFDocument.load(await buildOutput());
      try { doc.getForm().flatten(); } catch { /* kein Formular */ }
      await replaceDoc(await doc.save(), S.name);
      toast('Dokument reduziert');
    } });
}

/* Umwandeln */
async function toImagesTool() {
  const pagesInp = h('input', { type: 'text', name: 'pages', value: '1-ende' });
  const pb = progressBar();
  await dialog({ title: 'PDF zu Bildern', ok: 'Exportieren',
    body: [radios('fmt', [['png', 'PNG'], ['jpg', 'JPG']], 'png'), radios('dpi', [['72', '72 dpi'], ['150', '150 dpi'], ['300', '300 dpi']], '150'), field('Seiten', pagesInp), pb.el],
    onOk: async (form) => {
      const idx = parseRanges(pagesInp.value, S.pages.length).flat();
      busy('Exportiere Bilder …');
      const pjs = await loadPdfJs(await buildOutput());
      const files = [], fmt = form.fmt.value;
      for (let k = 0; k < idx.length; k++) {
        const c = await renderToCanvas(await pjs.getPage(idx[k] + 1), +form.dpi.value / 72);
        files.push({ name: `${baseName(S.name)}_S${String(idx[k] + 1).padStart(String(S.pages.length).length, '0')}.${fmt}`, data: await canvasBlob(c, fmt === 'png' ? 'image/png' : 'image/jpeg', 0.9) });
        pb.set((k + 1) / idx.length);
      }
      await pjs.destroy(); idle();
      await zipAndOffer(files, `${baseName(S.name)}_Bilder.zip`);
    } });
}
async function textTool() {
  busy('Lese Text …');
  let out = '';
  for (let i = 0; i < S.pages.length; i++) {
    const tc = await getText(i);
    out += `--- Seite ${i + 1} ---\n` + tc.items.map((it) => it.str + (it.hasEOL ? '\n' : '')).join('') + '\n\n';
  }
  idle();
  if (!out.replace(/--- Seite \d+ ---/g, '').trim()) { toast('Kein Text gefunden. Das Dokument besteht vermutlich aus Bildern.'); return; }
  await offerFile(baseName(S.name) + '.txt', new Blob([out], { type: 'text/plain' }));
}

/* ---------- Modi ---------- */
function setMode(m) {
  commitEdit(); hideSelPop();
  if (S.mode === m) return;
  S.mode = m;
  $$('#modeTabs button').forEach((b) => b.classList.toggle('on', b.dataset.mode === m));
  if (m === 'read' && !['select', 'hand'].includes(S.tool)) S.tool = 'select';
  if (m !== 'comment') S.selected = null;
  $('#viewer').hidden = m === 'pages' || m === 'tools';
  $('#organizer').hidden = m !== 'pages';
  $('#toolbox').hidden = m !== 'tools';
  if (m === 'pages') renderOrganizer();
  if (m === 'tools') renderToolbox();
  if (m === 'read' || m === 'comment') { const z = S.zoom; requestAnimationFrame(() => { if (Math.abs(computeScale() - S.scale) > 1e-3) { S.zoom = z; buildViewer(true); } }); }
  applyToolClass(); renderCtx(); redrawAllOverlays();
}

/* ---------- Dateien öffnen ---------- */
// files: File-Objekte; paths: zugehörige Speicherorte (Desktop), sonst leer
async function openFiles(files, paths = []) {
  files = [...files];
  if (!files.length) return;
  if (S.dirty && S.pdf && !(await confirmDiscard())) return;
  busy('Öffne …');
  try {
    if (files.length === 1 && isPdf(files[0], new Uint8Array(await files[0].slice(0, 5).arrayBuffer()))) {
      await openBytes(new Uint8Array(await files[0].arrayBuffer()), files[0].name, { path: paths[0] || null });
    } else {
      // mehrere Dateien oder Bilder: zu einem Dokument zusammenfassen
      const out = await PDFDocument.create();
      for (const f of files) { const d = await fileToDoc(f, { size: 'a4auto', margin: 0 }); if (d) (await out.copyPages(d, d.getPageIndices())).forEach((p) => out.addPage(p)); }
      if (!out.getPageCount()) return;
      await openBytes(await out.save(), files.length === 1 ? baseName(files[0].name.replace(/\.\w+$/, '')) + '.pdf' : 'Zusammengefügt.pdf');
      setDirty(true);
    }
    if (S.mode === 'tools') setMode('read');
  } catch (e) { console.error(e); toast('Öffnen fehlgeschlagen: ' + (e?.message || e)); }
  finally { idle(); }
}
// Dateien, die die Desktop-Hülle schickt (Doppelklick, Öffnen-Dialog, zuletzt geöffnet)
async function openHostFiles(list) {
  if (!list?.length) return;
  const MIME = { pdf: 'application/pdf', png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', webp: 'image/webp', gif: 'image/gif' };
  const files = list.map((f) => new File([f.data], f.name, { type: MIME[(f.name.split('.').pop() || '').toLowerCase()] || '' }));
  // auch wenn das Öffnen scheitert: die Hülle muss wieder wissen, ob das Fenster noch das unberührte Beispiel zeigt
  try { await openFiles(files, list.map((f) => f.path)); } finally { syncDoc(); }
}
function confirmDiscard() {
  return dialog({ title: 'Ungespeicherte Änderungen', ok: 'Verwerfen und öffnen', body: h('p', { style: 'margin:0' }, `Die Änderungen an „${S.name}“ sind noch nicht gespeichert.`) });
}

/* ---------- Beispieldokument ---------- */
async function sampleDoc() {
  const doc = await PDFDocument.create();
  doc.setTitle('Willkommen bei PDFwerk'); doc.setAuthor('PDFwerk'); doc.setCreator('PDFwerk');
  const f = await doc.embedFont(StandardFonts.Helvetica), fb = await doc.embedFont(StandardFonts.HelveticaBold);
  const ink = rgb(0.1, 0.13, 0.19), mute = rgb(0.38, 0.41, 0.48), acc = rgb(0.17, 0.29, 0.72);
  const W = 595.28, H = 841.89, L = 64;
  const para = (page, text, x, y, size = 11, font = f, color = ink, width = W - 2 * L) => {
    const words = text.split(' '); let line = '', yy = y;
    for (const w of words) { const t = line ? line + ' ' + w : w; if (font.widthOfTextAtSize(t, size) > width) { page.drawText(line, { x, y: yy, size, font, color }); yy -= size * 1.45; line = w; } else line = t; }
    if (line) page.drawText(line, { x, y: yy, size, font, color });
    return yy - size * 1.45;
  };
  // Seite 1
  let p = doc.addPage([W, H]);
  p.drawRectangle({ x: 0, y: H - 8, width: W, height: 8, color: acc });
  p.drawText('BEISPIELDOKUMENT', { x: L, y: H - 70, size: 9, font: fb, color: acc });
  p.drawText('Willkommen bei PDFwerk', { x: L, y: H - 104, size: 28, font: fb, color: ink });
  let y = para(p, 'Diese Datei wurde beim Start erzeugt, damit Sie alle Funktionen sofort ausprobieren können. Öffnen Sie oben eine eigene PDF oder ziehen Sie Dateien in das Fenster.', L, H - 140, 12, f, mute);
  y -= 14;
  const items = [
    ['Lesen', 'Seiten blättern, zoomen, Doppelseite, Nachtmodus, Präsentation, Volltextsuche mit Strg+F, Lesezeichen und Links.'],
    ['Bearbeiten', 'Text markieren, unterstreichen, durchstreichen, Freihand zeichnen, Textfelder, Notizen, Formen, Pfeile, Stempel, Unterschrift und Bilder.'],
    ['Text ersetzen', 'Mit dem Stift-Werkzeug auf eine Zeile klicken: Der alte Text wird abgedeckt und kann neu geschrieben werden.'],
    ['Schwärzen', 'Geschwärzte Stellen werden beim Speichern wirklich entfernt, nicht nur überdeckt.'],
    ['Seiten', 'Per Ziehen sortieren, drehen, löschen, duplizieren, leere Seiten und weitere Dateien einfügen.'],
    ['Werkzeuge', 'Zusammenfügen, Teilen, Komprimieren, Bilder zu PDF und zurück, Wasserzeichen, Seitenzahlen, Zuschneiden, Graustufen und mehr.'],
  ];
  for (const [t, d] of items) {
    p.drawRectangle({ x: L, y: y - 2, width: 4, height: 12, color: acc });
    p.drawText(t, { x: L + 14, y, size: 12, font: fb, color: ink });
    y = para(p, d, L + 14, y - 17, 10.5, f, ink, W - 2 * L - 14) - 8;
  }
  y -= 10;
  p.drawText('Probieren Sie es aus', { x: L, y, size: 15, font: fb, color: ink });
  y = para(p, 'Markieren Sie diesen Satz mit der Maus und wählen Sie im kleinen Menü „Markieren“. Im Modus „Bearbeiten“ finden Sie alle Zeichenwerkzeuge in der Leiste oben.', L, y - 22, 11);
  p.drawText('Seite 1 von 3', { x: W - L - f.widthOfTextAtSize('Seite 1 von 3', 9), y: 40, size: 9, font: f, color: mute });
  // Seite 2: Formular
  p = doc.addPage([W, H]);
  p.drawRectangle({ x: 0, y: H - 8, width: W, height: 8, color: acc });
  p.drawText('Anmeldung Werkstattkurs', { x: L, y: H - 90, size: 22, font: fb, color: ink });
  para(p, 'Ein ausfüllbares Formular. Werkzeuge, dann Formular ausfüllen, oder direkt unten tippen, nachdem Sie die Datei in einem anderen Programm öffnen.', L, H - 120, 11, f, mute);
  const form = doc.getForm();
  const lab = (t, yy) => p.drawText(t, { x: L, y: yy + 26, size: 9.5, font: fb, color: mute });
  const tf = (name, yy, w = W - 2 * L, val = '') => { lab(name, yy); const fld = form.createTextField(name); if (val) fld.setText(val); fld.addToPage(p, { x: L, y: yy, width: w, height: 22, font: f, borderColor: rgb(0.75, 0.78, 0.84), borderWidth: 1 }); return fld; };
  tf('Name', H - 200, W - 2 * L, 'Jana Beispiel');
  tf('E-Mail', H - 252);
  tf('Firma', H - 304);
  lab('Kurs', H - 356);
  const dd = form.createDropdown('Kurs'); dd.addOptions(['Blechbearbeitung', 'Schweißen', 'CNC-Fräsen', 'Lasertechnik']); dd.select('Schweißen');
  dd.addToPage(p, { x: L, y: H - 356, width: 240, height: 22, font: f, borderColor: rgb(0.75, 0.78, 0.84), borderWidth: 1 });
  const cb = form.createCheckBox('Newsletter');
  cb.addToPage(p, { x: L, y: H - 400, width: 14, height: 14, borderColor: rgb(0.5, 0.53, 0.6), borderWidth: 1 });
  p.drawText('Ich möchte Kursangebote per E-Mail erhalten.', { x: L + 22, y: H - 397, size: 10.5, font: f, color: ink });
  p.drawLine({ start: { x: L, y: 150 }, end: { x: L + 220, y: 150 }, thickness: 0.8, color: mute });
  p.drawText('Datum, Unterschrift', { x: L, y: 136, size: 9, font: f, color: mute });
  p.drawText('Seite 2 von 3', { x: W - L - f.widthOfTextAtSize('Seite 2 von 3', 9), y: 40, size: 9, font: f, color: mute });
  // Seite 3: Querformat-Tabelle
  p = doc.addPage([H, W]);
  p.drawText('Kursplan Herbst', { x: L, y: W - 80, size: 22, font: fb, color: ink });
  const cols = [L, L + 170, L + 330, L + 470, L + 600];
  const rows = [['Kurs', 'Termin', 'Dauer', 'Plätze', 'Preis'], ['Blechbearbeitung', '04.11.2026', '3 Tage', '8', '690 €'], ['Schweißen MAG', '11.11.2026', '5 Tage', '6', '1.150 €'], ['CNC-Fräsen', '18.11.2026', '4 Tage', '10', '980 €'], ['Lasertechnik', '25.11.2026', '2 Tage', '12', '520 €']];
  rows.forEach((r, k) => {
    const yy = W - 140 - k * 32;
    if (k === 0) p.drawRectangle({ x: L - 8, y: yy - 10, width: 720, height: 30, color: rgb(0.91, 0.93, 0.98) });
    else p.drawLine({ start: { x: L - 8, y: yy - 10 }, end: { x: L + 712, y: yy - 10 }, thickness: 0.5, color: rgb(0.8, 0.82, 0.87) });
    r.forEach((c, j) => p.drawText(c, { x: cols[j], y: yy, size: 11, font: k ? f : fb, color: ink }));
  });
  p.drawText('Seite 3 von 3', { x: H - L - f.widthOfTextAtSize('Seite 3 von 3', 9), y: 36, size: 9, font: f, color: mute });
  return doc.save();
}

/* ---------- Ereignisse ---------- */
function bind() {
  $('#btnOpen').onclick = openDialog;
  $('#fileInput').onchange = (e) => openFiles(e.target.files);
  $('#fileInput2').onchange = (e) => { if (e.target.files.length) insertFiles([...e.target.files], insertAt ?? S.pages.length); };
  $('#imgInput').onchange = (e) => { const f = e.target.files[0]; e.target.value = ''; if (f) placeImageFile(f); };
  $('#btnSave').onclick = () => save();
  $('#btnPrint').onclick = printDoc;
  if (window.self !== window.top) $('#btnPrint').hidden = true;
  $('#btnUndo').onclick = undo;
  $('#btnRedo').onclick = redo;
  $('#btnSide').onclick = () => { $('#side').classList.toggle('closed'); if (S.mode === 'read' || S.mode === 'comment') setTimeout(() => S.zoom === 'fitw' || S.zoom === 'fitp' ? rezoom(S.zoom) : null, 0); };
  $$('#modeTabs button').forEach((b) => (b.onclick = () => setMode(b.dataset.mode)));
  $$('#sideTabs button').forEach((b) => (b.onclick = () => { sideTab = b.dataset.side; renderSide(); }));
  $('#btnPrev').onclick = () => scrollToPage(Math.max(0, S.cur - 1));
  $('#btnNext').onclick = () => scrollToPage(Math.min(S.pages.length - 1, S.cur + 1));
  $('#pageInput').onchange = (e) => { const n = clamp(parseInt(e.target.value, 10) || 1, 1, S.pages.length); if (S.mode !== 'read' && S.mode !== 'comment') setMode('read'); scrollToPage(n - 1, false); };
  $('#pageInput').onfocus = (e) => e.target.select();
  $('#zoomSel').onchange = (e) => { const v = e.target.value; if (v !== 'custom') rezoom(v === 'fitw' || v === 'fitp' ? v : +v); };
  $('#btnZoomIn').onclick = () => zoomStep(1);
  $('#btnZoomOut').onclick = () => zoomStep(-1);
  $('#btnSearch').onclick = toggleSearch;
  $('#btnMore').onclick = moreMenu;
  $('#searchInput').oninput = (e) => { clearTimeout(runSearch.t); runSearch.t = setTimeout(() => runSearch(e.target.value.trim()), 220); };
  $('#searchInput').onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); gotoHit(S.search.idx + (e.shiftKey ? -1 : 1)); } if (e.key === 'Escape') toggleSearch(false); };
  $('#searchNext').onclick = () => gotoHit(S.search.idx + 1);
  $('#searchPrev').onclick = () => gotoHit(S.search.idx - 1);
  $('#searchClose').onclick = () => toggleSearch(false);

  const viewer = $('#viewer');
  viewer.addEventListener('scroll', () => { onViewerScroll(); hideSelPop(); }, { passive: true });
  viewer.addEventListener('wheel', (e) => {
    if (!e.ctrlKey && !e.metaKey) return;
    e.preventDefault();
    const r = viewer.getBoundingClientRect();
    const f = Math.exp(-e.deltaY * 0.0025);
    rezoom(clamp(Math.round(S.scale * f * 100) / 100, 0.1, 6), { x: e.clientX - r.left, y: e.clientY - r.top });
  }, { passive: false });
  // Hand-Werkzeug
  let pan = null;
  viewer.addEventListener('pointerdown', (e) => {
    if (S.tool !== 'hand' && !spaceDown) return;
    pan = { x: e.clientX, y: e.clientY, l: viewer.scrollLeft, t: viewer.scrollTop };
    viewer.classList.add('grabbing'); viewer.setPointerCapture(e.pointerId); e.preventDefault();
  }, true);
  viewer.addEventListener('pointermove', (e) => { if (pan) { viewer.scrollLeft = pan.l - (e.clientX - pan.x); viewer.scrollTop = pan.t - (e.clientY - pan.y); } });
  viewer.addEventListener('pointerup', () => { pan = null; viewer.classList.remove('grabbing'); });
  document.addEventListener('pointerup', () => { if (drag?.kind === 'hl' && !drag.captured) setTimeout(() => (drag = null), 0); setTimeout(onSelectionEnd, 0); });
  document.addEventListener('pointerdown', (e) => { if (!e.target.closest('.selpop')) hideSelPop(); });

  // Ziehen und Ablegen
  const desk = $('#desk');
  let dragDepth = 0;
  desk.addEventListener('dragenter', (e) => { if (e.dataTransfer.types.includes('Files') && S.mode !== 'pages') { dragDepth++; $('#drop').classList.add('show'); } });
  desk.addEventListener('dragleave', () => { if (--dragDepth <= 0) { dragDepth = 0; $('#drop').classList.remove('show'); } });
  desk.addEventListener('dragover', (e) => { if (e.dataTransfer.types.includes('Files')) e.preventDefault(); });
  desk.addEventListener('drop', (e) => {
    dragDepth = 0; $('#drop').classList.remove('show');
    if (!e.dataTransfer.files.length) return;
    e.preventDefault();
    const files = [...e.dataTransfer.files];
    if (S.mode === 'pages') { insertFiles(files, S.pages.length); return; }
    const paths = files.map((f) => HOST?.adoptDroppedFile(f) || null);
    // Desktop: ist die PDF schon in einem anderen Fenster offen, holt die Hülle es nach vorn statt sie doppelt zu öffnen
    (async () => {
      if (HOST && files.length === 1 && paths[0] && await HOST.focusOpenFile(paths[0]).catch(() => false)) return;
      await openFiles(files, paths);
    })();
  });
  // Abgelegte Dateien dürfen nie die Seite ersetzen (Browser und Desktop würden sonst die PDF anzeigen und die Arbeit verwerfen)
  document.addEventListener('dragover', (e) => e.preventDefault());
  document.addEventListener('drop', (e) => e.preventDefault());

  // Tastatur
  let spaceDown = false;
  document.addEventListener('keydown', (e) => {
    const t = document.activeElement, text = isTextField(t);
    // Auswahllisten und Regler: Kürzel mit Strg/Cmd und Entf gelten dem Dokument, Pfeile, Leertaste und Buchstaben dem Element
    const control = !text && /SELECT|INPUT/.test(t?.tagName || '');
    const typing = text || document.querySelector('.backdrop');
    // macOS: Strg in Textfeldern ist Textnavigation (Strg+P Zeile hoch, Strg+F Zeichen vor …)
    if (text && e.ctrlKey && !e.metaKey && MAC) return;
    // Strg+Cmd-Kombinationen (z. B. ⌃⌘F Vollbild) gehören dem Menü
    const mod = (e.ctrlKey || e.metaKey) && !(e.ctrlKey && e.metaKey);
    if (mod && e.key.toLowerCase() === 's') { e.preventDefault(); e.shiftKey ? saveAs() : save(); return; }
    if (mod && e.key.toLowerCase() === 'o') { e.preventDefault(); openDialog(); return; }
    if (mod && !e.shiftKey && !e.altKey && ['1', '2', '3', '4'].includes(e.key)) { e.preventDefault(); setMode(['read', 'comment', 'pages', 'tools'][+e.key - 1]); return; }
    if (mod && e.key.toLowerCase() === 'p') { e.preventDefault(); printDoc(); return; }
    if (mod && e.key.toLowerCase() === 'f') { e.preventDefault(); toggleSearch(true); return; }
    if (typing) return;
    if (mod && e.key.toLowerCase() === 'z') { e.preventDefault(); e.shiftKey ? redo() : undo(); return; }
    if (mod && e.key.toLowerCase() === 'y') { e.preventDefault(); redo(); return; }
    if (mod && (e.key === '+' || e.key === '=')) { e.preventDefault(); zoomStep(1); return; }
    if (mod && e.key === '-') { e.preventDefault(); zoomStep(-1); return; }
    if (mod && e.key === '0') { e.preventDefault(); rezoom('fitw'); return; }
    if (mod && e.key.toLowerCase() === 'a' && S.mode === 'pages') { e.preventDefault(); S.orgSel = new Set(identity()); refreshOrgSel(); renderCtx(); return; }
    if (e.key === 'Escape') {
      if (document.body.classList.contains('present')) { endPresentation(); return; }
      if (S.tool === 'place') { S.pendingImage = null; pickTool('select'); return; }
      if (S.selected) { S.selected = null; redrawAllOverlays(); renderCtx(); return; }
    }
    if ((e.key === 'Delete' || e.key === 'Backspace')) {
      if (S.mode === 'comment' && S.selected) { e.preventDefault(); deleteSelected(); return; }
      if (S.mode === 'pages' && S.orgSel.size && S.orgSel.size < S.pages.length) { e.preventDefault(); deletePages([...S.orgSel]); return; }
    }
    if (control) return;
    if (S.mode === 'comment' && S.selected && e.key.startsWith('Arrow')) {
      e.preventDefault();
      const a = selAnn(), st = (e.shiftKey ? 10 : 1) / S.scale, i = S.selected.p;
      const [sx, sy] = { ArrowLeft: [-st, 0], ArrowRight: [st, 0], ArrowUp: [0, -st], ArrowDown: [0, st] }[e.key];
      const o = JSON.parse(JSON.stringify(a));
      const [p0x, p0y] = views[i].vp.convertToPdfPoint(0, 0), [p1x, p1y] = views[i].vp.convertToPdfPoint(sx * S.scale, sy * S.scale);
      if (!e.repeat) snapshot();
      moveAnn(a, o, p1x - p0x, p1y - p0y); drawOverlay(i);
      return;
    }
    const present = document.body.classList.contains('present');
    if (S.mode === 'read' || present) {
      if (['PageDown', 'ArrowRight'].includes(e.key) && (present || S.zoom === 'fitp')) { e.preventDefault(); $('#btnNext').click(); return; }
      if (['PageUp', 'ArrowLeft'].includes(e.key) && (present || S.zoom === 'fitp')) { e.preventDefault(); $('#btnPrev').click(); return; }
      if (e.key === ' ' && present) { e.preventDefault(); $('#btnNext').click(); return; }
    }
    if (e.key === 'Home' && S.mode !== 'pages') { scrollToPage(0); return; }
    if (e.key === 'End' && S.mode !== 'pages') { scrollToPage(S.pages.length - 1); return; }
    if (e.key === ' ' && !spaceDown && S.mode === 'comment') { spaceDown = true; $('#viewer').classList.add('hand'); e.preventDefault(); return; }
    if (S.mode === 'comment' && !mod && !e.altKey && TOOL_KEYS[e.key.toLowerCase()]) pickTool(TOOL_KEYS[e.key.toLowerCase()]);
  });
  document.addEventListener('keyup', (e) => { if (e.key === ' ' && spaceDown) { spaceDown = false; $('#viewer').classList.toggle('hand', S.tool === 'hand'); } });
  document.addEventListener('fullscreenchange', () => { if (!document.fullscreenElement) endPresentation(); });
  window.addEventListener('resize', () => { clearTimeout(bind.rt); bind.rt = setTimeout(() => { if (typeof S.zoom === 'string' && S.pdf) rezoom(S.zoom); }, 150); });
  // Im Desktop fragt die Hülle beim Schließen nach; ein beforeunload-Block würde dort das Fenster stumm offen halten
  if (!HOST) window.addEventListener('beforeunload', (e) => { if (S.dirty) { e.preventDefault(); e.returnValue = ''; } });
  if (HOST) {
    HOST.onOpenFiles((files) => openHostFiles(files).catch((e) => console.error(e)));
    HOST.onCommand((cmd) => runCommand(cmd));
    document.documentElement.dataset.platform = HOST.platform;
  }
  if (window.innerWidth < 760) $('#side').classList.add('closed');
}
function openDialog() {
  if (HOST) { HOST.openDialog(); return; }
  $('#fileInput').value = ''; $('#fileInput').click();
}
// Befehle aus dem Menü der Desktop-Hülle
const isTyping = () => isTextField(document.activeElement);
async function runCommand(cmd) {
  const modal = document.querySelector('.backdrop');
  switch (cmd) {
    case 'open': if (!modal) openDialog(); break;
    case 'save': if (!modal) await save(); break;
    case 'saveAs': if (!modal) await saveAs(); break;
    case 'saveAndClose': {
      modal?.remove();
      // nur schließen, wenn wirklich alles gesichert ist (auch Änderungen während des Speicherns)
      if (await save() && !S.dirty) HOST?.close();
      break;
    }
    case 'print': if (!modal) printDoc(); break;
    case 'undo': if (isTyping()) document.execCommand('undo'); else if (!modal) await undo(); break;
    case 'redo': if (isTyping()) document.execCommand('redo'); else if (!modal) await redo(); break;
    case 'find': toggleSearch(true); break;
    case 'zoomIn': zoomStep(1); break;
    case 'zoomOut': zoomStep(-1); break;
    case 'zoomWidth': rezoom('fitw'); break;
    case 'zoomPage': rezoom('fitp'); break;
    case 'zoomActual': rezoom(1); break;
    case 'toggleSidebar': $('#btnSide').click(); break;
    case 'properties': if (!modal) metaTool(); break;
    case 'shortcuts': if (!modal) moreMenu(); break;
    default:
      if (cmd?.startsWith('mode:')) setMode(cmd.slice(5));
  }
}
function toggleSearch(force) {
  const bar = $('#searchBar');
  const show = typeof force === 'boolean' ? force : bar.hidden;
  bar.hidden = !show;
  if (show) { if (S.mode !== 'read' && S.mode !== 'comment') setMode('read'); $('#searchInput').focus(); $('#searchInput').select(); }
  else { S.search = { q: '', hits: [], idx: -1 }; $('#searchInput').value = ''; $('#searchCount').textContent = ''; redrawAllOverlays(); }
}
function moreMenu() {
  const theme = document.documentElement.dataset.theme;
  dialog({ title: 'Ansicht', ok: null, cancel: 'Schließen', body: [
    h('div', { class: 'field' }, h('span', {}, 'Darstellung'), h('div', { class: 'row' },
      h('button', { class: 'btn outline' + (S.spread ? ' on' : ''), type: 'button', onclick: (e) => { S.spread = !S.spread; e.target.classList.toggle('on', S.spread); buildViewer(true); } }, 'Doppelseite'),
      h('button', { class: 'btn outline' + (S.night ? ' on' : ''), type: 'button', onclick: (e) => { S.night = !S.night; e.target.classList.toggle('on', S.night); $('#viewer').classList.toggle('night', S.night); } }, 'Nachtmodus'),
      h('button', { class: 'btn outline', type: 'button', onclick: () => { document.querySelector('.backdrop')?.remove(); startPresentation(); } }, 'Präsentation'))),
    h('div', { class: 'field' }, h('span', {}, 'Farbschema der Oberfläche'), radios('theme', [['', 'System'], ['light', 'Hell'], ['dark', 'Dunkel']], theme || '')),
    h('div', { class: 'field' }, h('span', {}, 'Tastenkürzel'), h('div', { class: 'note', style: 'line-height:1.7' },
      'Strg+O öffnen · Strg+S speichern · Strg+F suchen · Strg+Z / Strg+Y rückgängig / wiederholen · Strg+Mausrad zoomen · Strg+0 Breite · ',
      'Bearbeiten: V auswählen, H Hand, M markieren, P Freihand, T Text, N Notiz, R Rechteck, E Ellipse, L Linie, A Pfeil, Leertaste halten zum Verschieben, Entf löscht')),
  ] });
  setTimeout(() => $$('.backdrop input[name=theme]').forEach((r) => r.addEventListener('change', () => {
    if (r.value) document.documentElement.dataset.theme = r.value; else delete document.documentElement.dataset.theme;
    try { localStorage.setItem('pdfwerk.theme', r.value); } catch { /* egal */ }
  })), 0);
}

/* ---------- Start ---------- */
(async function init() {
  try { const t = localStorage.getItem('pdfwerk.theme'); if (t) document.documentElement.dataset.theme = t; } catch { /* kein Speicher */ }
  bind();
  renderCtx();
  window.claude?.use?.('downloads').then((d) => { S.downloads = d; }).catch(() => {});
  // Desktop: per Doppelklick oder Kommandozeile übergebene Dateien statt des Beispiels öffnen
  const initial = HOST ? await HOST.ready().catch(() => []) : [];
  if (initial.length) { await openHostFiles(initial).catch((e) => console.error(e)); if (S.pdf) return; }
  busy('Lade Beispieldokument …');
  await openBytes(await sampleDoc(), 'Willkommen.pdf');
  S.pristine = true; syncDoc();
  idle('Beispieldokument geladen. Eigene Datei mit „Öffnen“ oder per Ziehen laden.');
})();
