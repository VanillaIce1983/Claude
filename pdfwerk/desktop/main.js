// Desktop-Hülle für PDFwerk (Electron): Fenster, Menü und Dateizugriff.
// Die Oberfläche aus dist/desktop läuft über das eigene Schema app://pdfwerk und spricht nur über preload.js mit der Hülle.
'use strict';
const { app, BrowserWindow, Menu, dialog, ipcMain, nativeTheme, protocol, screen, session, shell } = require('electron');
const fs = require('node:fs');
const fsp = fs.promises;
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');

const ORIGIN = 'app://pdfwerk';
const ROOT = path.join(__dirname, '../dist/desktop');
const isMac = process.platform === 'darwin', isWin = process.platform === 'win32';
const TEST = process.env.PDFWERK_TEST === '1';
const IMAGES = new Set(['png', 'jpg', 'jpeg', 'webp', 'gif']);
const CSP = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; worker-src 'self' blob:; connect-src 'self' data: blob:; object-src 'none'; base-uri 'none'; form-action 'none'";
const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.map': 'application/json; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8', '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.gif': 'image/gif', '.webp': 'image/webp', '.svg': 'image/svg+xml', '.ico': 'image/x-icon',
  '.wasm': 'application/wasm',
};
const SAVE_FILTERS = {
  pdf: { name: 'PDF-Dokument', extensions: ['pdf'] },
  zip: { name: 'ZIP-Archiv', extensions: ['zip'] },
  txt: { name: 'Textdatei', extensions: ['txt'] },
  png: { name: 'PNG-Bild', extensions: ['png'] },
  jpg: { name: 'JPEG-Bild', extensions: ['jpg', 'jpeg'] },
};
const PERMISSIONS = new Set(['clipboard-sanitized-write', 'fullscreen']);

// Zähler für scripts/smoke-desktop.mjs
const stats = TEST ? (globalThis.__pdfwerkStats = { windows: 0, saveFile: 0, saveDialog: 0, openDialog: 0, openExternal: 0, print: 0, errors: [] }) : null;
const count = (k) => { if (stats) stats[k]++; };
if (TEST && process.env.PDFWERK_USER_DATA) app.setPath('userData', process.env.PDFWERK_USER_DATA);

const fromApp = (u) => typeof u === 'string' && (u === ORIGIN || u.startsWith(ORIGIN + '/'));
const extOf = (p) => path.extname(p).slice(1).toLowerCase();
const canOpen = (p) => extOf(p) === 'pdf' || IMAGES.has(extOf(p));
// Freigabe: Windows unterscheidet Groß/Klein nicht; beim Erkennen schon geöffneter Dateien auch macOS nicht
const grantKey = (p) => { const r = path.resolve(p); return isWin ? r.toLowerCase() : r; };
const sameFile = (a, b) => {
  const k = (p) => { const r = path.resolve(p).normalize('NFC'); return isWin || isMac ? r.toLowerCase() : r; };
  return !!a && !!b && k(a) === k(b);
};

/* ---------- Auslieferung von dist/desktop über app://pdfwerk ---------- */
protocol.registerSchemesAsPrivileged([{ scheme: 'app', privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } }]);

function assetPath(pathname) {
  let rel;
  try { rel = decodeURIComponent(pathname); } catch { return null; }
  if (rel.includes('\0') || rel.includes('\\')) return null;
  const parts = rel.split('/').filter(Boolean);
  if (!parts.length) parts.push('index.html');
  if (parts.some((s) => s === '.' || s === '..' || s.includes(':') || /^(con|prn|aux|nul|com\d|lpt\d)(\.|$)/i.test(s))) return null;
  const file = path.join(ROOT, ...parts);
  const inside = path.relative(ROOT, file);
  if (!inside || inside.startsWith('..') || path.isAbsolute(inside)) return null;
  return file;
}
async function serve(request) {
  const notFound = () => new Response('Nicht gefunden', { status: 404, headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
  let url;
  try { url = new URL(request.url); } catch { return notFound(); }
  if (url.host !== 'pdfwerk') return notFound();
  if (request.method !== 'GET' && request.method !== 'HEAD') return new Response(null, { status: 405 });
  const file = assetPath(url.pathname);
  if (!file) return notFound();
  try {
    if (!(await fsp.stat(file)).isFile()) return notFound();
    const body = await fsp.readFile(file);
    return new Response(request.method === 'HEAD' ? null : body, { status: 200, headers: {
      'Content-Type': MIME[path.extname(file).toLowerCase()] || 'application/octet-stream',
      'Content-Security-Policy': CSP,
      'X-Content-Type-Options': 'nosniff',
      'Cache-Control': 'no-cache',
    } });
  } catch { return notFound(); }
}

/* ---------- Freigegebene Dateien und Schreiben ---------- */
const granted = new Set();
const grant = (p) => granted.add(grantKey(p));
const isGranted = (p) => granted.has(grantKey(p));

function saveError(e) {
  if (['EBUSY', 'EPERM', 'EACCES', 'EROFS'].includes(e?.code)) return 'Die Datei ist schreibgeschützt oder in einem anderen Programm geöffnet.';
  if (['ENOSPC', 'EDQUOT'].includes(e?.code)) return 'Nicht genug Speicherplatz.';
  return 'Die Datei konnte nicht gespeichert werden.' + (e?.code ? ` (${e.code})` : '');
}
// Schreibt erst eine temporäre Datei im Zielordner und ersetzt dann das Ziel in einem Schritt
async function writeAtomic(target, data) {
  try { target = await fsp.realpath(target); } catch { /* neue Datei */ }
  let mode;
  try {
    const st = await fsp.stat(target);
    if (!st.isFile()) throw Object.assign(new Error('keine Datei'), { code: 'EISDIR' });
    await fsp.access(target, fs.constants.W_OK);
    mode = st.mode & 0o7777;
  } catch (e) { if (e.code !== 'ENOENT') throw e; }
  const tmp = path.join(path.dirname(target), `.pdfwerk-${crypto.randomBytes(6).toString('hex')}.tmp`);
  let fh;
  try {
    fh = await fsp.open(tmp, 'wx', mode ?? 0o666);
    await fh.writeFile(data);
    await fh.sync();
    await fh.close(); fh = null;
    if (mode !== undefined) await fsp.chmod(tmp, mode).catch(() => {});
    // Windows: Virenscanner und Indexdienst halten Dateien kurz fest
    for (let i = 0; ; i++) {
      try { await fsp.rename(tmp, target); break; }
      catch (e) { if (!isWin || i >= 5 || !['EPERM', 'EBUSY', 'EACCES'].includes(e.code)) throw e; await new Promise((r) => setTimeout(r, 100 * (i + 1))); }
    }
  } catch (e) {
    await fh?.close().catch(() => {});
    await fsp.rm(tmp, { force: true }).catch(() => {});
    throw e;
  }
}
const asBytes = (data) => (data instanceof Uint8Array ? data : null);

/* ---------- Fenster ---------- */
const wins = new Map();     // BrowserWindow -> Zustand
let focusOrder = [];
const stateOf = (win) => (win && !win.isDestroyed() ? wins.get(win) : null);
const lastFocused = () => {
  const f = BrowserWindow.getFocusedWindow();
  if (stateOf(f)) return f;
  return focusOrder.find((w) => stateOf(w)) || [...wins.keys()].filter((w) => !w.isDestroyed()).at(-1) || null;
};
const boundsFile = () => path.join(app.getPath('userData'), 'fenster.json');

function visible(b) {
  const area = screen.getDisplayMatching(b).workArea;
  return b.x >= area.x - 8 && b.y >= area.y - 8 && b.x + b.width <= area.x + area.width + 8 && b.y + b.height <= area.y + area.height + 8;
}
function nextBounds() {
  const area = screen.getPrimaryDisplay().workArea;
  const size = { width: Math.min(1280, area.width), height: Math.min(860, area.height) };
  const ref = lastFocused();
  if (ref) {
    const b = ref.getNormalBounds();
    const c = { x: b.x + 28, y: b.y + 28, width: b.width, height: b.height };
    return { bounds: visible(c) ? c : { width: b.width, height: b.height }, maximized: false };
  }
  try {
    const s = JSON.parse(fs.readFileSync(boundsFile(), 'utf8'));
    const b = { x: s.x | 0, y: s.y | 0, width: Math.max(720, s.width | 0), height: Math.max(520, s.height | 0) };
    if (visible(b)) return { bounds: b, maximized: !!s.maximized };
  } catch { /* keine gemerkte Größe */ }
  return { bounds: size, maximized: false };
}
function rememberBounds(win) {
  try { fs.writeFileSync(boundsFile(), JSON.stringify({ ...win.getNormalBounds(), maximized: win.isMaximized() })); } catch { /* egal */ }
}

function createWindow(paths = []) {
  const { bounds, maximized } = nextBounds();
  const win = new BrowserWindow({
    ...bounds, minWidth: 720, minHeight: 520, show: false, title: 'PDFwerk',
    backgroundColor: nativeTheme.shouldUseDarkColors ? '#151922' : '#eef0f4',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true, sandbox: true, nodeIntegration: false, webSecurity: true, spellcheck: false,
      devTools: !app.isPackaged, navigateOnDragDrop: false, safeDialogs: true,
      additionalArguments: [`--pdfwerk-version=${app.getVersion()}`],
    },
  });
  wins.set(win, { pending: paths.slice(), loading: false, ready: false, doc: null, reopen: null, asking: false, forceClose: false, afterSave: null });
  count('windows');
  win.once('ready-to-show', () => { if (maximized) win.maximize(); win.show(); });
  win.on('focus', () => { focusOrder = [win, ...focusOrder.filter((w) => w !== win)]; });
  win.on('page-title-updated', (e) => e.preventDefault());
  win.on('close', (e) => onClose(win, e));
  win.on('closed', () => { wins.delete(win); focusOrder = focusOrder.filter((w) => w !== win); });
  const wc = win.webContents;
  wc.setVisualZoomLevelLimits(1, 1).catch(() => {});
  wc.on('will-prevent-unload', (e) => e.preventDefault());
  wc.on('render-process-gone', (_e, details) => crashed(win, details));
  win.loadURL(ORIGIN + '/index.html');
  return win;
}
function focusWin(win) {
  if (!stateOf(win)) return;
  if (win.isMinimized()) win.restore();
  if (win.isVisible()) { win.show(); win.focus(); }
}
function updateTitle(win) {
  const doc = stateOf(win)?.doc;
  win.setTitle(doc ? `${doc.name}${doc.dirty && !isMac ? ' (geändert)' : ''} – PDFwerk` : 'PDFwerk');
}
async function crashed(win, details) {
  const st = stateOf(win);
  if (!st || st.forceClose || details.reason === 'clean-exit') return;
  st.reopen = st.doc?.path || null;
  st.doc = null;
  const { response } = await dialog.showMessageBox(win, {
    type: 'error', buttons: ['Neu laden', 'Fenster schließen'], defaultId: 0, cancelId: 1, noLink: true,
    message: 'Das Fenster wurde unerwartet beendet.',
    detail: 'Nicht gespeicherte Änderungen sind verloren. Beim Neuladen wird die zuletzt gespeicherte Fassung geöffnet.',
  });
  if (!stateOf(win)) return;
  if (response === 0) win.webContents.reload(); else closeNow(win);
}

/* ---------- Schließen und Beenden ---------- */
let quitApproved = false, quitFlow = null;

function closeNow(win) {
  const st = stateOf(win);
  if (!st) return;
  st.forceClose = true;
  win.close();
}
async function askSave(win) {
  const st = stateOf(win);
  st.asking = true;
  try {
    if (win.isMinimized()) win.restore();
    win.show();
    const { response } = await dialog.showMessageBox(win, {
      type: 'warning', buttons: ['Speichern', 'Nicht speichern', 'Abbrechen'], defaultId: 0, cancelId: 2, noLink: true,
      message: `Änderungen an „${st.doc?.name || 'Dokument'}“ speichern?`,
      detail: 'Ohne Speichern gehen die Änderungen verloren.',
    });
    return response;
  } finally { st.asking = false; }
}
// Ergebnis: 'closed', 'saving' (die Oberfläche speichert und ruft close()) oder 'cancel'
async function confirmClose(win) {
  const answer = await askSave(win);
  if (!stateOf(win)) return 'closed';
  if (answer === 1) { closeNow(win); return 'closed'; }
  if (answer === 0) { win.webContents.send('pdfwerk:command', 'saveAndClose'); return 'saving'; }
  return 'cancel';
}
function onClose(win, e) {
  const st = stateOf(win);
  if (!st) return;
  if (st.forceClose || quitApproved || !st.doc?.dirty) { rememberBounds(win); return; }
  e.preventDefault();
  if (st.asking) return;
  st.afterSave?.(false);   // erneutes Schließen bricht ein wartendes Beenden ab
  confirmClose(win).catch(() => {});
}
// Wartet nach „Speichern“ auf das Schließen. Ein abgebrochener Speichern-Dialog oder eine Eingabe im Fenster
// (die Oberfläche meldet Fehlschläge nicht) bricht das Beenden ab, damit es nicht hängen bleibt.
function waitSaved(win) {
  const st = stateOf(win);
  return new Promise((resolve) => {
    const wc = win.webContents;
    const done = (ok) => {
      if (st.afterSave === done) st.afterSave = null;
      win.removeListener('closed', onClosed);
      if (!wc.isDestroyed()) { wc.removeListener('before-input-event', onKey); wc.removeListener('before-mouse-event', onMouse); }
      resolve(ok);
    };
    const onClosed = () => done(true);
    const onKey = (_e, input) => { if (input.type === 'keyDown') done(false); };
    const onMouse = (_e, input) => { if (input.type === 'mouseDown') done(false); };
    st.afterSave = done;
    win.once('closed', onClosed);
    wc.on('before-input-event', onKey);
    wc.on('before-mouse-event', onMouse);
  });
}
async function runQuit() {
  for (const win of [...wins.keys()]) {
    const st = stateOf(win);
    if (!st?.doc?.dirty) continue;
    if (st.asking) return;
    const r = await confirmClose(win);
    if (r === 'cancel') return;
    if (r === 'saving' && stateOf(win) && !(await waitSaved(win))) return;
  }
  quitApproved = true;
  app.quit();
}

/* ---------- Dateien öffnen ---------- */
const openErrorText = (p, e) => `„${path.basename(p)}“: ${e?.code === 'ENOENT' ? 'nicht gefunden' : e?.code === 'EACCES' || e?.code === 'EPERM' ? 'kein Zugriff' : 'konnte nicht gelesen werden'}`;
function showOpenErrors(errors, win) {
  if (!errors.length) return;
  if (stats) stats.errors.push(...errors);
  const opts = {
    type: 'error', buttons: ['OK'], noLink: true,
    message: errors.length > 1 ? 'Einige Dateien können nicht geöffnet werden.' : 'Die Datei kann nicht geöffnet werden.',
    detail: errors.join('\n') + '\n\nPDFwerk öffnet PDF-Dateien und Bilder (PNG, JPG, WebP, GIF).',
  };
  const parent = stateOf(win) ? win : null;
  (parent ? dialog.showMessageBox(parent, opts) : dialog.showMessageBox(opts)).catch(() => {});
}
async function readFiles(paths, win) {
  const out = [], errors = [];
  for (const p of paths) {
    try {
      const buf = await fsp.readFile(p);
      grant(p);
      out.push({ name: path.basename(p), path: p, data: new Uint8Array(buf.buffer, buf.byteOffset, buf.byteLength) });
    } catch (e) { errors.push(openErrorText(p, e)); }
  }
  showOpenErrors(errors, win);
  return out;
}
function windowWithFile(p) {
  for (const [win, st] of wins) if (!win.isDestroyed() && (sameFile(st.doc?.path, p) || st.pending.some((q) => sameFile(q, p)))) return win;
  return null;
}
function isPristine(win) {
  const st = stateOf(win);
  if (!st) return false;
  if (!st.ready) return !st.loading && !st.pending.length;   // zeigt gleich das Beispiel
  return !!st.doc?.pristine && !st.doc.dirty;
}
async function deliver(win, paths) {
  const st = stateOf(win);
  if (st.doc) st.doc.pristine = false;
  if (!st.ready) { st.pending.push(...paths); focusWin(win); return; }
  focusWin(win);
  const files = await readFiles(paths, win);
  if (files.length && stateOf(win)) win.webContents.send('pdfwerk:open-files', files);
}
// Einziger Weg für Dateien ins Programm: Startargumente, zweite Instanz, open-file, Öffnen-Dialog, Zuletzt geöffnet
async function route(list, { preferred = null } = {}) {
  const errors = [], seen = [], groups = [];
  let images = null;
  for (const raw of list) {
    if (typeof raw !== 'string' || !raw) continue;
    const p = path.resolve(raw);
    if (seen.some((q) => sameFile(q, p))) continue;
    seen.push(p);
    let st;
    try { st = await fsp.stat(p); } catch (e) { errors.push(openErrorText(p, e)); continue; }
    if (!st.isFile()) { errors.push(`„${path.basename(p)}“: keine Datei`); continue; }
    if (!canOpen(p)) { errors.push(`„${path.basename(p)}“: Dateityp wird nicht unterstützt`); continue; }
    if (extOf(p) === 'pdf') {
      const open = windowWithFile(p);
      if (open) { focusWin(open); continue; }
      groups.push([p]);
    } else if (images) images.push(p);
    else groups.push(images = [p]);
  }
  showOpenErrors(errors, preferred);
  groups.forEach((g, i) => {
    if (i === 0 && isPristine(preferred)) deliver(preferred, g).catch(() => {});
    else createWindow(g);
  });
}
function argvFiles(argv, cwd) {
  const rest = (Array.isArray(argv) ? argv : []).slice(1).filter((a) => typeof a === 'string' && a && !a.startsWith('-'));
  if (!app.isPackaged) rest.shift();   // App-Pfad bei „electron .“
  return rest.map((a) => path.resolve(cwd || process.cwd(), a));
}
async function openDialog(win) {
  count('openDialog');
  const parent = stateOf(win) ? win : null;
  const opts = {
    title: 'Öffnen', buttonLabel: 'Öffnen', properties: ['openFile', 'multiSelections'],
    defaultPath: stateOf(win)?.doc?.path ? path.dirname(stateOf(win).doc.path) : app.getPath('documents'),
    filters: [{ name: 'PDF und Bilder', extensions: ['pdf', 'png', 'jpg', 'jpeg', 'webp', 'gif'] }, { name: 'Alle Dateien', extensions: ['*'] }],
  };
  const res = parent ? await dialog.showOpenDialog(parent, opts) : await dialog.showOpenDialog(opts);
  if (!res.canceled && res.filePaths?.length) await route(res.filePaths, { preferred: parent });
}

/* ---------- Zuletzt geöffnet ---------- */
let recent = [];
const recentFile = () => path.join(app.getPath('userData'), 'recent.json');
function loadRecent() {
  try {
    const list = JSON.parse(fs.readFileSync(recentFile(), 'utf8'));
    recent = Array.isArray(list) ? list.filter((p) => typeof p === 'string' && path.isAbsolute(p)).slice(0, 10) : [];
  } catch { recent = []; }
}
function saveRecent() { try { fs.writeFileSync(recentFile(), JSON.stringify(recent)); } catch { /* egal */ } }
function addRecent(p) {
  try { app.addRecentDocument(p); } catch { /* nicht überall vorhanden */ }
  if (isMac) return;
  recent = [p, ...recent.filter((q) => grantKey(q) !== grantKey(p))].slice(0, 10);
  saveRecent();
  updateMenu();
}
function clearRecent() {
  recent = [];
  try { app.clearRecentDocuments(); } catch { /* nicht überall vorhanden */ }
  saveRecent();
  updateMenu();
}

/* ---------- Menü ---------- */
function command(cmd, menuWin) {
  const win = stateOf(menuWin) ? menuWin : BrowserWindow.getFocusedWindow();
  const st = stateOf(win);
  if (st?.ready) win.webContents.send('pdfwerk:command', cmd);
  else if (cmd === 'open') openDialog(win).catch(() => {});
}
function about(win) {
  const opts = {
    type: 'info', buttons: ['OK'], noLink: true, title: 'Über PDFwerk',
    message: `PDFwerk ${app.getVersion()}`,
    detail: `PDF-Viewer und -Editor\nElectron ${process.versions.electron} · Chromium ${process.versions.chrome}`,
  };
  (stateOf(win) ? dialog.showMessageBox(win, opts) : dialog.showMessageBox(opts)).catch(() => {});
}
function buildMenu() {
  const sep = { type: 'separator' };
  const send = (cmd) => (_item, win) => command(cmd, win);
  // app.js führt diese Kürzel selbst per keydown aus: unter Windows/Linux nur anzeigen, sonst liefe der Befehl doppelt
  const page = (accelerator) => (isMac ? { accelerator } : { accelerator, registerAccelerator: false });
  const label = (s) => (isMac ? s : s.replace(/&/g, '&&'));
  const recentMenu = isMac
    ? { label: 'Zuletzt geöffnet', role: 'recentDocuments', submenu: [{ label: 'Liste leeren', role: 'clearRecentDocuments' }] }
    : { label: 'Zuletzt geöffnet', submenu: [
      ...(recent.length ? recent.map((p) => {
        const name = path.basename(p);
        const twin = recent.some((q) => q !== p && path.basename(q) === name);
        return { label: label(twin ? `${name} – ${path.dirname(p)}` : name), click: (_i, win) => route([p], { preferred: stateOf(win) ? win : lastFocused() }) };
      }) : [{ label: 'Keine Einträge', enabled: false }]),
      sep,
      { label: 'Liste leeren', enabled: recent.length > 0, click: clearRecent },
    ] };
  return Menu.buildFromTemplate([
    ...(isMac ? [{ label: app.name, submenu: [
      { label: 'Über PDFwerk', role: 'about' }, sep,
      { label: 'Dienste', role: 'services' }, sep,
      { label: 'PDFwerk ausblenden', role: 'hide' }, { label: 'Andere ausblenden', role: 'hideOthers' }, { label: 'Alle einblenden', role: 'unhide' }, sep,
      { label: 'PDFwerk beenden', role: 'quit' },
    ] }] : []),
    { label: 'Datei', submenu: [
      { label: 'Öffnen…', ...page('CmdOrCtrl+O'), click: send('open') },
      recentMenu, sep,
      { label: 'Speichern', ...page('CmdOrCtrl+S'), click: send('save') },
      { label: 'Speichern unter…', ...page('CmdOrCtrl+Shift+S'), click: send('saveAs') }, sep,
      { label: 'Drucken…', ...page('CmdOrCtrl+P'), click: send('print') },
      { label: 'Dokumenteigenschaften…', click: send('properties') }, sep,
      { label: 'Fenster schließen', role: 'close' },
      ...(isMac ? [] : [sep, { label: 'Beenden', role: 'quit' }]),
    ] },
    { label: 'Bearbeiten', submenu: [
      { label: 'Rückgängig', ...page('CmdOrCtrl+Z'), click: send('undo') },
      { label: 'Wiederholen', ...page(isMac ? 'Shift+Cmd+Z' : 'Ctrl+Y'), click: send('redo') }, sep,
      { label: 'Ausschneiden', role: 'cut' },
      { label: 'Kopieren', role: 'copy' },
      { label: 'Einfügen', role: 'paste' },
      { label: 'Alles auswählen', role: 'selectAll', ...(isMac ? {} : { registerAccelerator: false }) }, sep,
      { label: 'Suchen…', ...page('CmdOrCtrl+F'), click: send('find') },
    ] },
    { label: 'Ansicht', submenu: [
      { label: 'Lesen', ...page('CmdOrCtrl+1'), click: send('mode:read') },
      { label: 'Bearbeiten', ...page('CmdOrCtrl+2'), click: send('mode:comment') },
      { label: 'Seiten', ...page('CmdOrCtrl+3'), click: send('mode:pages') },
      { label: 'Werkzeuge', ...page('CmdOrCtrl+4'), click: send('mode:tools') }, sep,
      { label: 'Vergrößern', ...page('CmdOrCtrl+Plus'), click: send('zoomIn') },
      ...(isMac ? [{ label: 'Vergrößern', accelerator: 'Cmd+=', visible: false, click: send('zoomIn') }] : []),
      { label: 'Verkleinern', ...page('CmdOrCtrl+-'), click: send('zoomOut') },
      { label: 'Seitenbreite', ...page('CmdOrCtrl+0'), click: send('zoomWidth') },
      { label: 'Ganze Seite', click: send('zoomPage') },
      { label: 'Originalgröße', click: send('zoomActual') }, sep,
      { label: 'Seitenleiste', click: send('toggleSidebar') },
      { label: 'Vollbild', role: 'togglefullscreen' },
      ...(app.isPackaged ? [] : [sep, { label: 'Entwicklertools', role: 'toggleDevTools' }]),
    ] },
    ...(isMac ? [{ label: 'Fenster', role: 'window', submenu: [
      { label: 'Im Dock ablegen', role: 'minimize' }, { label: 'Zoomen', role: 'zoom' }, sep,
      { label: 'Alle nach vorne bringen', role: 'front' },
    ] }] : []),
    { label: 'Hilfe', role: 'help', submenu: [
      { label: 'Tastenkürzel', click: send('shortcuts') },
      isMac ? { label: 'Über PDFwerk', role: 'about' } : { label: 'Über PDFwerk', click: (_i, win) => about(win) },
    ] },
  ]);
}
const updateMenu = () => Menu.setApplicationMenu(buildMenu());

/* ---------- Schnittstelle zur Oberfläche ---------- */
// Nur das Hauptdokument eigener Fenster auf app://pdfwerk darf die Hülle ansprechen
function senderWin(e) {
  const frame = e.senderFrame;
  if (!frame || frame.parent || !fromApp(frame.url)) return null;
  const win = BrowserWindow.fromWebContents(e.sender);
  return stateOf(win) ? win : null;
}
const handle = (channel, fn) => ipcMain.handle(channel, async (e, ...args) => {
  const win = senderWin(e);
  if (!win) throw new Error('Zugriff verweigert');
  try { return { result: await fn(win, ...args) }; }
  catch (err) { return { error: err?.message || String(err) }; }
});
const listen = (channel, fn) => ipcMain.on(channel, (e, ...args) => { const win = senderWin(e); if (win) fn(win, ...args); });

handle('pdfwerk:ready', async (win) => {
  const st = stateOf(win);
  // Neu geladen (z. B. nach Absturz): zuletzt gespeicherte Fassung wieder öffnen
  const again = st.ready ? st.reopen || st.doc?.path : null;
  if (again && !st.pending.length && isGranted(again)) st.pending.push(again);
  st.reopen = null;
  st.doc = null;
  st.loading = true;
  try { return await readFiles(st.pending.slice(), win); }
  finally { st.pending = []; st.loading = false; st.ready = true; }
});
handle('pdfwerk:open-dialog', (win) => openDialog(win));
handle('pdfwerk:save-file', async (win, file, data) => {
  count('saveFile');
  const bytes = asBytes(data);
  if (typeof file !== 'string' || !path.isAbsolute(file) || !bytes) throw new Error('Ungültige Daten zum Speichern.');
  if (!isGranted(file)) throw new Error('Diese Datei darf PDFwerk nicht überschreiben.');
  try { await writeAtomic(file, bytes); } catch (e) { throw new Error(saveError(e)); }
  if (extOf(file) === 'pdf') addRecent(file);
  return { path: file, name: path.basename(file) };
});
handle('pdfwerk:save-dialog', async (win, opts) => {
  count('saveDialog');
  const st = stateOf(win);
  const bytes = asBytes(opts?.data);
  if (!bytes) throw new Error('Ungültige Daten zum Speichern.');
  const defaultName = (String(opts.defaultName || '').split(/[\\/]/).pop() || '').replace(/[\x00-\x1f<>:"|?*]/g, '_').trim().slice(0, 200) || 'Dokument.pdf';
  const ext = extOf(defaultName) === 'jpeg' ? 'jpg' : extOf(defaultName);
  const filter = SAVE_FILTERS[ext];
  const dir = st.doc?.path ? path.dirname(st.doc.path) : app.getPath('documents');
  const res = await dialog.showSaveDialog(win, {
    title: opts.title ? String(opts.title).slice(0, 200) : 'Speichern unter',
    buttonLabel: 'Speichern',
    defaultPath: path.join(dir, defaultName),
    filters: [...(filter ? [filter] : []), { name: 'Alle Dateien', extensions: ['*'] }],
    properties: ['createDirectory', 'showOverwriteConfirmation'],
  });
  if (res.canceled || !res.filePath) { st.afterSave?.(false); return null; }
  let file = path.resolve(res.filePath);
  if (filter && !filter.extensions.includes(extOf(file))) {
    file += '.' + filter.extensions[0];
    if (fs.existsSync(file)) {
      const { response } = await dialog.showMessageBox(win, {
        type: 'warning', buttons: ['Ersetzen', 'Abbrechen'], defaultId: 1, cancelId: 1, noLink: true,
        message: `„${path.basename(file)}“ ist bereits vorhanden. Ersetzen?`,
      });
      if (response !== 0) { st.afterSave?.(false); return null; }
    }
  }
  try { await writeAtomic(file, bytes); }
  catch (e) { st.afterSave?.(false); throw new Error(saveError(e)); }
  grant(file);
  if (extOf(file) === 'pdf') addRecent(file);
  return { path: file, name: path.basename(file) };
});
handle('pdfwerk:print', (_win, html) => printHtml(String(html)));
listen('pdfwerk:adopt', (_win, p) => {
  if (typeof p !== 'string' || !path.isAbsolute(p) || !canOpen(p)) return;
  try { if (fs.statSync(p).isFile()) grant(p); } catch { /* weg */ }
});
listen('pdfwerk:set-document', (win, s) => {
  const st = stateOf(win);
  if (!s || typeof s !== 'object') return;
  const before = st.doc?.path;
  const p = typeof s.path === 'string' && path.isAbsolute(s.path) ? s.path : null;
  st.doc = { name: String(s.name || 'Dokument.pdf').slice(0, 260), path: p, dirty: !!s.dirty, pristine: !!s.pristine && !s.dirty };
  updateTitle(win);
  if (isMac) {
    win.setDocumentEdited(st.doc.dirty);
    win.setRepresentedFilename(p && isGranted(p) ? p : '');
  }
  if (p && p !== before && isGranted(p) && extOf(p) === 'pdf') addRecent(p);
});
listen('pdfwerk:close', (win) => closeNow(win));

/* ---------- Drucken ---------- */
async function printHtml(html) {
  count('print');
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'pdfwerk-druck-'));
  const file = path.join(dir, 'druck.html');
  let win = null;
  try {
    await fsp.writeFile(file, html);
    win = new BrowserWindow({ show: false, width: 800, height: 600, webPreferences: {
      sandbox: true, javascript: false, contextIsolation: true, nodeIntegration: false, webSecurity: true,
      spellcheck: false, devTools: false, partition: 'pdfwerk-druck',
    } });
    await win.loadFile(file);
    await new Promise((resolve) => win.webContents.print({ silent: false, printBackground: true }, () => resolve()));
  } finally {
    if (win && !win.isDestroyed()) win.destroy();
    await fsp.rm(dir, { recursive: true, force: true }).catch(() => {});
  }
}

/* ---------- Sicherheit ---------- */
function openExternal(url) {
  let u;
  try { u = new URL(url); } catch { return; }
  if (!['http:', 'https:', 'mailto:'].includes(u.protocol)) return;
  count('openExternal');
  Promise.resolve(shell.openExternal(u.href)).catch(() => {});
}
app.on('web-contents-created', (_e, wc) => {
  wc.setWindowOpenHandler(({ url }) => { openExternal(url); return { action: 'deny' }; });
  const stay = (e, url) => { if (!fromApp(e.url || url)) e.preventDefault(); };
  wc.on('will-navigate', stay);
  wc.on('will-redirect', stay);
  wc.on('will-attach-webview', (e) => e.preventDefault());
});
function setupSessions() {
  const ses = session.defaultSession;
  ses.setPermissionRequestHandler((wc, permission, cb, details) => cb(PERMISSIONS.has(permission) && fromApp(details?.requestingUrl || wc?.getURL())));
  ses.setPermissionCheckHandler((_wc, permission, origin) => PERMISSIONS.has(permission) && fromApp(origin));
  // Offline: nur eigene Dateien, keine Verbindungen nach außen
  ses.webRequest.onBeforeRequest((details, cb) => cb({ cancel: !/^(app|data|blob|devtools):/i.test(details.url) }));
  ses.on('will-download', (e) => e.preventDefault());
  const printSes = session.fromPartition('pdfwerk-druck');
  printSes.setPermissionRequestHandler((_wc, _p, cb) => cb(false));
  printSes.setPermissionCheckHandler(() => false);
  printSes.webRequest.onBeforeRequest((details, cb) => cb({ cancel: !/^(file|data):/i.test(details.url) }));
}

/* ---------- Start ---------- */
if (isWin) app.setAppUserModelId('app.pdfwerk.desktop');
// Alle Renderer in der Sandbox; mit --no-sandbox (Linux als root) würde das den Start verhindern
if (!app.commandLine.hasSwitch('no-sandbox')) app.enableSandbox();

if (!app.requestSingleInstanceLock({ argv: process.argv, cwd: process.cwd() })) {
  app.quit();
} else {
  let started = false, early = [], openTimer = null;
  // macOS: Doppelklick im Finder, Ziehen aufs Dock-Symbol, Zuletzt geöffnet; kurz sammeln, damit mehrere Bilder zusammenkommen
  app.on('open-file', (e, p) => {
    e.preventDefault();
    early.push(p);
    if (!started) return;
    clearTimeout(openTimer);
    openTimer = setTimeout(() => route(early.splice(0), { preferred: lastFocused() }), 120);
  });
  app.on('second-instance', (_e, argv, cwd, extra) => {
    const files = Array.isArray(extra?.argv) && typeof extra?.cwd === 'string' ? argvFiles(extra.argv, extra.cwd) : argvFiles(argv, cwd);
    const win = lastFocused();
    if (files.length) route(files, { preferred: win });
    else if (win) focusWin(win);
    else createWindow();
  });
  app.on('before-quit', (e) => {
    if (quitApproved) return;
    if (![...wins.values()].some((st) => st.doc?.dirty)) { quitApproved = true; return; }
    e.preventDefault();
    quitFlow ??= runQuit().catch(() => {}).finally(() => { quitFlow = null; });
  });
  app.on('window-all-closed', () => { if (!isMac) app.quit(); });
  app.on('activate', () => { if (started && !wins.size) createWindow(); });

  app.whenReady().then(async () => {
    protocol.handle('app', serve);
    setupSessions();
    nativeTheme.on('updated', () => { for (const w of wins.keys()) if (!w.isDestroyed()) w.setBackgroundColor(nativeTheme.shouldUseDarkColors ? '#151922' : '#eef0f4'); });
    if (isMac) app.setAboutPanelOptions({ applicationName: 'PDFwerk', applicationVersion: app.getVersion(), version: '', credits: 'PDF-Viewer und -Editor' });
    loadRecent();
    updateMenu();
    started = true;
    clearTimeout(openTimer);
    await route([...argvFiles(process.argv, process.cwd()), ...early.splice(0)]);
    if (!wins.size) createWindow();
  });
}
