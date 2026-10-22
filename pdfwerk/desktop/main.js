// Desktop-Hülle für PDFwerk (Electron): Fenster, Menü und Dateizugriff.
// Die Oberfläche aus dist/desktop läuft über das eigene Schema app://pdfwerk und spricht nur über preload.js mit der Hülle.
'use strict';
const { app, BrowserWindow, Menu, dialog, ipcMain, nativeTheme, protocol, screen, session, shell } = require('electron');
const fs = require('node:fs');
const fsp = fs.promises;
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const { execFile } = require('node:child_process');

const ORIGIN = 'app://pdfwerk';
const ROOT = path.join(__dirname, '../dist/desktop');
const isMac = process.platform === 'darwin', isWin = process.platform === 'win32';
const TEST = process.env.PDFWERK_TEST === '1';
// Fernsteuerung über das DevTools-Protokoll im Paket ablehnen. Erlaubt nur zusammen mit einem laufenden Node-Inspector:
// den gibt es nur in Testpaketen (Playwright), in ausgelieferten Paketen ist die Fuse EnableNodeCliInspectArguments aus.
const remoteControl = app.isPackaged && ['remote-debugging-port', 'remote-debugging-pipe'].some((s) => app.commandLine.hasSwitch(s)) && !require('node:inspector').url();
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
  if (isMac && e?.stage === 'tmp' && ['EPERM', 'EACCES'].includes(e?.code)) return 'PDFwerk darf in diesem Ordner nicht schreiben. Freigabe unter Systemeinstellungen → Datenschutz & Sicherheit → Dateien und Ordner → PDFwerk.';
  if (isWin && e?.stage === 'rename' && ['EPERM', 'EACCES'].includes(e?.code)) return 'Die Datei ist in einem anderen Programm geöffnet, oder PDFwerk darf sie in diesem Ordner nicht ersetzen.';
  if (['EBUSY', 'EPERM', 'EACCES', 'EROFS'].includes(e?.code)) return 'Die Datei ist schreibgeschützt oder in einem anderen Programm geöffnet.';
  if (['ENOSPC', 'EDQUOT'].includes(e?.code)) return 'Nicht genug Speicherplatz.';
  return 'Die Datei konnte nicht gespeichert werden.' + (e?.code ? ` (${e.code})` : '');
}
// Legt die temporäre Datei als Kopie des Ziels an, damit sie dessen Metadaten trägt: Finder-Tags, Kommentare, Quarantäne,
// ACL und erweiterte Attribute. Danach wird nur der Inhalt ersetzt. Ergibt false, wenn das nicht geht (dann ohne Metadaten).
async function copyWithMeta(target, tmp) {
  if (isWin) return false;
  for (const args of isMac ? [['-c', '-p'], ['-p']] : [['-p', '--preserve=xattr', '--reflink=auto']]) {
    if (await new Promise((resolve) => execFile('/bin/cp', [...args, target, tmp], { timeout: 60000 }, (err) => resolve(!err)))) return true;
    await fsp.rm(tmp, { force: true }).catch(() => {});
  }
  return false;
}
// Schreibt erst eine temporäre Datei im Zielordner und ersetzt dann das Ziel in einem Schritt.
// Erhalten bleiben Modus, unter macOS/Linux die erweiterten Attribute, unter Windows der Mark of the Web (Zone.Identifier).
// Nicht erhalten bleiben harte Links sowie Besitzer und eigene ACL unter Windows (dafür bräuchte es ReplaceFileW).
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
    const cloned = mode !== undefined && await copyWithMeta(target, tmp);
    try { fh = await fsp.open(tmp, cloned ? 'r+' : 'wx', mode ?? 0o666); }
    catch (e) { e.stage = 'tmp'; throw e; }
    if (cloned) await fh.truncate(0);
    await fh.writeFile(data);
    await fh.sync();
    await fh.close(); fh = null;
    if (mode !== undefined) await fsp.chmod(tmp, mode).catch(() => {});
    if (isWin && mode !== undefined) {
      try { await fsp.writeFile(tmp + ':Zone.Identifier', await fsp.readFile(target + ':Zone.Identifier')); } catch { /* kein Mark of the Web */ }
    }
    // Windows: Virenscanner und Indexdienst halten Dateien kurz fest
    for (let i = 0; ; i++) {
      try { await fsp.rename(tmp, target); break; }
      catch (e) {
        if (!isWin || i >= 5 || !['EPERM', 'EBUSY', 'EACCES'].includes(e.code)) { e.stage = 'rename'; throw e; }
        await new Promise((r) => setTimeout(r, 100 * (i + 1)));
      }
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
  // opening: Pfade, die die Oberfläche gerade lädt (bis sie das Dokument meldet); dir: Ordner der zuletzt geöffneten oder gespeicherten Datei
  wins.set(win, { pending: paths.slice(), opening: [], dir: null, loading: false, ready: false, doc: null, reopen: null, asking: false, forceClose: false, afterSave: null });
  count('windows');
  win.once('ready-to-show', () => { if (maximized) win.maximize(); win.show(); });
  win.on('focus', () => { focusOrder = [win, ...focusOrder.filter((w) => w !== win)]; });
  win.on('page-title-updated', (e) => e.preventDefault());
  win.on('close', (e) => onClose(win, e));
  win.on('closed', () => { wins.delete(win); focusOrder = focusOrder.filter((w) => w !== win); abortPrints(win); });
  // Windows: Abmelden, Herunterfahren und Neustart lösen weder close noch before-quit aus. Mit ungespeicherten Änderungen
  // das Ende der Sitzung aufhalten (Windows zeigt „PDFwerk verhindert das Herunterfahren“) und die übliche Rückfrage zeigen.
  win.on('query-session-end', (e) => {
    if (quitApproved || e.reasons?.includes('critical') || ![...wins.values()].some((s) => s.doc?.dirty && !s.forceClose)) return;
    e.preventDefault();
    quitFlow ??= runQuit().catch(() => {}).finally(() => { quitFlow = null; });
  });
  win.on('session-end', () => rememberBounds(win));
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
  st.opening = [];
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
  if (st.forceClose || !st.doc?.dirty) { rememberBounds(win); return; }
  e.preventDefault();
  if (quitApproved) {
    // beim Beenden erst nach der letzten Rückfrage geändert: Beenden abbrechen und erneut fragen
    quitApproved = false;
    quitFlow ??= runQuit().catch(() => {}).finally(() => { quitFlow = null; });
    return;
  }
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
// Fragt so lange nach, bis kein geändertes Fenster mehr übrig ist: auch Fenster, die während einer Rückfrage geändert wurden
async function runQuit() {
  for (;;) {
    const win = [...wins.keys()].find((w) => { const s = stateOf(w); return s?.doc?.dirty && !s.forceClose; });
    if (!win) break;
    if (stateOf(win).asking) return;
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
  const st = stateOf(win);
  if (st && out.length) st.dir = path.dirname(out[0].path);
  return out;
}
function windowWithFile(p) {
  const has = (list) => list.some((q) => sameFile(q, p));
  for (const [win, st] of wins) if (!win.isDestroyed() && (sameFile(st.doc?.path, p) || has(st.pending) || has(st.opening))) return win;
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
  // bis die Oberfläche das Dokument meldet, gilt die Datei als in diesem Fenster geöffnet (kein zweites Fenster bei Doppelklick)
  st.opening = [...paths];
  const files = await readFiles(paths, win);
  st.opening = files.length && stateOf(win) ? files.map((f) => f.path) : [];
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
    defaultPath: stateOf(win)?.doc?.path ? path.dirname(stateOf(win).doc.path) : stateOf(win)?.dir || app.getPath('documents'),
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
  // Windows/Linux: Alt+Buchstabe öffnet das Hauptmenü (unterstrichener Buchstabe)
  const top = (s) => (isMac ? s : '&' + s);
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
    { label: top('Datei'), submenu: [
      { label: 'Öffnen…', ...page('CmdOrCtrl+O'), click: send('open') },
      recentMenu, sep,
      { label: 'Speichern', ...page('CmdOrCtrl+S'), click: send('save') },
      { label: 'Speichern unter…', ...page('CmdOrCtrl+Shift+S'), click: send('saveAs') }, sep,
      { label: 'Drucken…', ...page('CmdOrCtrl+P'), click: send('print') },
      { label: 'Dokumenteigenschaften…', click: send('properties') }, sep,
      { label: 'Fenster schließen', role: 'close' },
      ...(isMac ? [] : [sep, { label: 'Beenden', role: 'quit' }]),
    ] },
    { label: top('Bearbeiten'), submenu: [
      { label: 'Rückgängig', ...page('CmdOrCtrl+Z'), click: send('undo') },
      { label: 'Wiederholen', ...page(isMac ? 'Shift+Cmd+Z' : 'Ctrl+Y'), click: send('redo') }, sep,
      { label: 'Ausschneiden', role: 'cut' },
      { label: 'Kopieren', role: 'copy' },
      { label: 'Einfügen', role: 'paste' },
      { label: 'Alles auswählen', role: 'selectAll', ...(isMac ? {} : { registerAccelerator: false }) }, sep,
      { label: 'Suchen…', ...page('CmdOrCtrl+F'), click: send('find') },
    ] },
    { label: top('Ansicht'), submenu: [
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
    { label: top('Hilfe'), role: 'help', submenu: [
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
  // Nach einem Absturz neu geladen: zuletzt gespeicherte Fassung wieder öffnen (nur diesen von der Hülle gemerkten Pfad)
  const again = st.ready ? st.reopen : null;
  if (again && !st.pending.length && isGranted(again)) st.pending.push(again);
  st.reopen = null;
  st.doc = null;
  st.loading = true;
  try {
    const files = await readFiles(st.pending.slice(), win);
    st.opening = files.map((f) => f.path);
    return files;
  } finally { st.pending = []; st.loading = false; st.ready = true; }
});
handle('pdfwerk:open-dialog', (win) => openDialog(win));
handle('pdfwerk:save-file', async (win, file, data) => {
  count('saveFile');
  const bytes = asBytes(data);
  if (typeof file !== 'string' || !path.isAbsolute(file) || !bytes) throw new Error('Ungültige Daten zum Speichern.');
  if (!isGranted(file) && !dropped.has(grantKey(file))) throw new Error('Diese Datei darf PDFwerk nicht überschreiben.');
  // umbenannt, verschoben oder gelöscht: nicht stillschweigend unter dem alten Namen neu anlegen
  try { if (!(await fsp.stat(file)).isFile()) throw new Error(); } catch { throw new Error('Die Datei wurde verschoben, umbenannt oder gelöscht.'); }
  if (!isGranted(file)) {
    if (!(await confirmDropped(win, file))) throw new Error('Speichern abgebrochen.');
    grant(file);
  }
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
  const dir = st.doc?.path ? path.dirname(st.doc.path) : st.dir || app.getPath('documents');
  const res = await dialog.showSaveDialog(win, {
    title: opts.title ? String(opts.title).slice(0, 200) : 'Speichern unter',
    buttonLabel: 'Speichern',
    defaultPath: path.join(dir, defaultName),
    filters: [...(filter ? [filter] : []), { name: 'Alle Dateien', extensions: ['*'] }],
    properties: ['createDirectory', 'showOverwriteConfirmation'],
    showsTagField: false,   // macOS: Electron übernimmt eingetragene Tags nicht
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
  // in einem anderen Fenster offen: das würde beim nächsten Speichern dort wieder überschrieben
  const other = windowWithFile(file);
  if (other && other !== win) { st.afterSave?.(false); throw new Error(`„${path.basename(file)}“ ist in einem anderen PDFwerk-Fenster geöffnet. Bitte dort speichern oder einen anderen Namen wählen.`); }
  try { await writeAtomic(file, bytes); }
  catch (e) { st.afterSave?.(false); throw new Error(saveError(e)); }
  grant(file);
  st.dir = path.dirname(file);
  if (extOf(file) === 'pdf') addRecent(file);
  return { path: file, name: path.basename(file) };
});
handle('pdfwerk:print-begin', (win) => printBegin(win));
handle('pdfwerk:print-page', (win, id, data) => printPage(win, id, data));
handle('pdfwerk:print-end', (win, id) => printEnd(win, id));
listen('pdfwerk:print-abort', (win, id) => { const job = printJobs.get(id); if (job?.win === win) dropPrint(id); });
// Ins Fenster gezogene Dateien: den Pfad meldet die Seite selbst, daher keine Freigabe, sondern erst die Rückfrage beim ersten Speichern
const dropped = new Set();
listen('pdfwerk:adopt', (_win, p) => {
  if (typeof p !== 'string' || !path.isAbsolute(p) || !canOpen(p)) return;
  if (isWin && /^[\\/]{2}/.test(p)) return;   // UNC- und Gerätepfade: keine Netzverbindung auf Zuruf der Seite
  dropped.add(grantKey(p));
});
async function confirmDropped(win, file) {
  const { response } = await dialog.showMessageBox(win, {
    type: 'question', buttons: ['Überschreiben', 'Abbrechen'], defaultId: 0, cancelId: 1, noLink: true,
    message: `„${path.basename(file)}“ überschreiben?`,
    detail: `Die Datei wurde ins Fenster gezogen und wird zum ersten Mal gespeichert.\nOrdner: ${path.dirname(file)}`,
  });
  return response === 0;
}
// Abgelegte PDF schon in einem anderen Fenster offen: dieses nach vorn holen
handle('pdfwerk:focus-open-file', (win, p) => {
  if (typeof p !== 'string' || !path.isAbsolute(p)) return false;
  const other = windowWithFile(p);
  if (!other || other === win) return false;
  focusWin(other);
  return true;
});
listen('pdfwerk:set-document', (win, s) => {
  const st = stateOf(win);
  if (!s || typeof s !== 'object') return;
  const before = st.doc?.path;
  // nur Pfade, die die Hülle kennt (geöffnet, gespeichert oder abgelegt)
  const p = typeof s.path === 'string' && path.isAbsolute(s.path) && (isGranted(s.path) || dropped.has(grantKey(s.path))) ? s.path : null;
  st.doc = { name: String(s.name || 'Dokument.pdf').slice(0, 260), path: p, dirty: !!s.dirty, pristine: !!s.pristine && !s.dirty };
  st.opening = [];
  if (p) st.dir = path.dirname(p);
  updateTitle(win);
  if (isMac) {
    win.setDocumentEdited(st.doc.dirty);
    win.setRepresentedFilename(p && isGranted(p) ? p : '');
  }
  if (p && p !== before && isGranted(p) && extOf(p) === 'pdf') addRecent(p);
});
listen('pdfwerk:close', (win) => closeNow(win));

/* ---------- Drucken ---------- */
// Die Oberfläche schickt die Seiten einzeln als JPEG; sie liegen in einem Temp-Ordner, bis der Druckdialog fertig ist
const printJobs = new Map();   // Kennung -> { win, dir, pages }
const MAX_PRINT_PAGES = 10000;
async function printBegin(win) {
  const id = crypto.randomUUID();
  printJobs.set(id, { win, dir: await fsp.mkdtemp(path.join(os.tmpdir(), 'pdfwerk-druck-')), pages: 0 });
  return id;
}
async function printPage(win, id, data) {
  const job = printJobs.get(id), bytes = asBytes(data);
  if (!job || job.win !== win) throw new Error('Unbekannter Druckauftrag.');
  if (!bytes || bytes[0] !== 0xff || bytes[1] !== 0xd8) throw new Error('Ungültige Druckseite.');
  if (job.pages >= MAX_PRINT_PAGES) throw new Error('Zu viele Seiten zum Drucken.');
  await fsp.writeFile(path.join(job.dir, `seite-${++job.pages}.jpg`), bytes);
}
function dropPrint(id) {
  const job = printJobs.get(id);
  printJobs.delete(id);
  if (job) fsp.rm(job.dir, { recursive: true, force: true }).catch(() => {});
}
function abortPrints(win) { for (const [id, job] of printJobs) if (job.win === win) dropPrint(id); }
async function printEnd(win, id) {
  const job = printJobs.get(id);
  if (!job || job.win !== win) throw new Error('Unbekannter Druckauftrag.');
  printJobs.delete(id);
  count('print');
  let pw = null;
  try {
    // jede Seite füllt das Blatt des gewählten Papiers (feste Maße würden gemischte Formate verkleinern oder umbrechen)
    const body = Array.from({ length: job.pages }, (_, k) => `<section><img src="seite-${k + 1}.jpg" alt=""></section>`).join('');
    const file = path.join(job.dir, 'druck.html');
    await fsp.writeFile(file, '<!doctype html><html><head><meta charset="utf-8"><style>@page{margin:0}html,body{margin:0}'
      + 'section{width:100vw;height:100vh;overflow:hidden;page-break-after:always;break-after:page}section:last-child{page-break-after:auto;break-after:auto}'
      + 'img{display:block;width:100%;height:100%;object-fit:contain}</style></head><body>' + body + '</body></html>');
    pw = new BrowserWindow({ show: false, width: 800, height: 600, webPreferences: {
      sandbox: true, javascript: false, contextIsolation: true, nodeIntegration: false, webSecurity: true,
      spellcheck: false, devTools: false, partition: 'pdfwerk-druck',
    } });
    await pw.loadFile(file);
    await new Promise((resolve) => pw.webContents.print({ silent: false, printBackground: true }, () => resolve()));
  } finally {
    if (pw && !pw.isDestroyed()) pw.destroy();
    await fsp.rm(job.dir, { recursive: true, force: true }).catch(() => {});
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

if (remoteControl) {
  app.exit(1);
} else if (!app.requestSingleInstanceLock({ argv: process.argv, cwd: process.cwd() })) {
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
