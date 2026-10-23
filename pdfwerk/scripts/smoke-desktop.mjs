// Rauchtest der Desktop-Fassung: startet die Electron-Hülle und spielt Öffnen, Speichern, Schließen, die Sicherheitsregeln
// und das Blättern in einem langen Dokument durch.
// Voraussetzung: npm run build. Ohne Bildschirm: xvfb-run -a npm run test:desktop
// PDFWERK_EXE=<Programmdatei> prüft statt des Quellordners das fertige Paket (z. B. release/linux-unpacked/pdfwerk).
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync, realpathSync, chmodSync, renameSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
const require = createRequire(import.meta.url);
const { _electron } = require('playwright-core');
const { PDFDocument, StandardFonts, PDFName } = require('pdf-lib');
import { SECRET, FIELD_SECRET, KEEP, NOTE, makeSecretPdf, findText, makeEncryptedPdf, makeCommentedPdf, readComments, makeLongPdf, browseLongPdf } from './testdateien.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const exe = process.env.PDFWERK_EXE ? resolve(process.env.PDFWERK_EXE) : null;
// echter Pfad: macOS-Temp liegt hinter einem Symlink, Windows-Temp oft als 8.3-Kurzname
const out = realpathSync.native(mkdtempSync(join(tmpdir(), 'pdfwerk-desktop-')));
const sandboxArgs = process.platform === 'linux' && process.getuid?.() === 0 ? ['--no-sandbox'] : [];
const appArgs = (args) => [...sandboxArgs, ...(exe ? [] : [root]), ...args];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let fails = 0;
const ok = (cond, msg) => { console.log((cond ? 'ok   ' : 'FAIL ') + msg); if (!cond) fails++; };
// Ein hängender Schritt zählt als Fehler, statt den ganzen Lauf bis zur CI-Zeitgrenze aufzuhalten
const step = async (name, fn, ms = 120000) => {
  let timer;
  try { await Promise.race([fn(), new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Zeitüberschreitung')), ms); })]); }
  catch (e) { ok(false, `${name}: ${e.message.split('\n')[0]}`); }
  finally { clearTimeout(timer); }
};
setTimeout(() => { console.log('FAIL Zeitüberschreitung des ganzen Tests'); process.exit(1); }, 12 * 60000).unref();

/* ---------- Testdateien ---------- */
async function makePdf(name, pages = 2) {
  const doc = await PDFDocument.create();
  const font = await doc.embedFont(StandardFonts.Helvetica);
  for (let i = 0; i < pages; i++) doc.addPage([595, 842]).drawText(`${name} Seite ${i + 1}`, { x: 60, y: 760, size: 20, font });
  const file = join(out, name);
  writeFileSync(file, await doc.save());
  return file;
}
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP4z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==', 'base64');
const makePng = (name) => { const f = join(out, name); writeFileSync(f, PNG); return f; };
const rotation = async (file, i = 0) => (await PDFDocument.load(readFileSync(file))).getPage(i).getRotation().angle;

/* ---------- Starten und Steuern ---------- */
async function launch(args = []) {
  const userData = mkdtempSync(join(out, 'profil-'));
  const env = { ...process.env, PDFWERK_TEST: '1', PDFWERK_USER_DATA: userData };
  // mit Sandbox wie unter Windows und macOS; Playwright schaltet sie unter Linux sonst ab
  const app = await _electron.launch({ executablePath: exe || require('electron'), args: appArgs(args), cwd: out, env, timeout: 60000, chromiumSandbox: !sandboxArgs.length });
  const errors = [];
  const watch = (p) => {
    p.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
    p.on('pageerror', (e) => errors.push(e.message));
  };
  app.windows().forEach(watch);
  app.on('window', watch);
  let exited = false;
  const exit = new Promise((r) => app.process().once('exit', () => { exited = true; r(); }));
  return { app, errors, env, exit, exited: () => exited };
}
async function finish({ app, exit, exited }) {
  if (!exited()) {
    await app.evaluate(({ dialog }) => { dialog.showMessageBox = async () => ({ response: 1, checkboxChecked: false }); }).catch(() => {});
    await app.evaluate(({ app }) => { app.quit(); }).catch(() => {});
    await Promise.race([exit, sleep(10000)]);
    if (!exited()) app.process().kill('SIGKILL');
  }
  await app.close().catch(() => {});
}
const ready = (p) => p.waitForFunction(() => typeof S !== 'undefined' && !!S.pdf && S.pages.length > 0, null, { timeout: 30000 });
const doc = (p) => p.evaluate(() => ({ name: S.name, path: S.path, pages: S.pages.length, dirty: S.dirty, pristine: S.pristine }));
const stats = (app) => app.evaluate(() => ({ ...globalThis.__pdfwerkStats }));
const winCount = (app) => app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().length);
const titleOf = (app, p) => app.browserWindow(p).then((h) => h.evaluate((w) => w.getTitle()));
const closeWin = (app, p) => app.browserWindow(p).then((h) => h.evaluate((w) => w.close()));
const rotate = (p) => p.evaluate(() => rotatePages([0], 90)).then(() => p.waitForFunction(() => S.dirty));
// Wartet, bis fn() etwas Wahres liefert; auch ein einzelner Aufruf, der nie zurückkommt, endet mit der Frist
async function until(fn, ms = 15000) {
  const end = Date.now() + ms;
  for (;;) {
    const v = await Promise.race([Promise.resolve().then(fn).catch(() => null), sleep(Math.max(0, end - Date.now())).then(() => null)]);
    if (v || Date.now() > end) return v;
    await sleep(100);
  }
}
// Tastendruck wie vom Betriebssystem, über den Hauptprozess eingespeist (Cmd unter macOS, sonst Strg)
const MOD = process.platform === 'darwin' ? 'meta' : 'control';
const keys = (app, p, keyCode, modifiers = [MOD]) => app.browserWindow(p).then((h) => h.evaluate((w, [k, m]) => {
  w.webContents.sendInputEvent({ type: 'keyDown', keyCode: k, modifiers: m });
  w.webContents.sendInputEvent({ type: 'keyUp', keyCode: k, modifiers: m });
}, [keyCode, modifiers]));
const toastText = (p) => p.evaluate(() => document.querySelector('.toast')?.textContent || '');
// Neues Fenster, das nach einer Aktion aufgeht
async function newWindow(app, action) {
  const ev = app.waitForEvent('window', { timeout: 20000 });
  await action();
  const p = await ev;
  await ready(p);
  return p;
}
// Dialoge im Hauptprozess ersetzen (main.js ruft dialog.* erst zur Aufrufzeit auf)
const stubBox = (app, responses, delay = 0) => app.evaluate(({ dialog }, [responses, delay]) => {
  globalThis.__boxes = [];
  dialog.showMessageBox = async (win, opts) => {
    const o = opts || win;
    globalThis.__boxes.push(`${o.message} | ${o.detail || ''}`);
    await new Promise((r) => setTimeout(r, delay));
    return { response: responses.length > 1 ? responses.shift() : responses[0], checkboxChecked: false };
  };
}, [responses, delay]);
const boxes = (app) => app.evaluate(() => globalThis.__boxes || []);
const stubOpen = (app, files) => app.evaluate(({ dialog }, files) => { dialog.showOpenDialog = async () => ({ canceled: false, filePaths: files }); }, files);
const stubSave = (app, file) => app.evaluate(({ dialog }, file) => {
  dialog.showSaveDialog = async (win, opts) => { globalThis.__saveOpts = opts || win; return { canceled: !file, filePath: file || '' }; };
}, file);

const docA = await makePdf('a.pdf'), docB = await makePdf('b.pdf'), docC = await makePdf('c.pdf');
const img1 = makePng('bild1.png'), img2 = makePng('bild2.PNG');

/* ---------- 1. Start ohne Datei: Beispiel, Worker, Schriften, Sicherheit, Öffnen ---------- */
{
  const run = await launch();
  const { app, errors } = run;
  const p = await app.firstWindow();
  await step('Start', async () => {
    await p.waitForFunction(() => typeof S !== 'undefined' && S.pages.length === 3, null, { timeout: 30000 });
    ok(true, 'Start zeigt das Beispieldokument (3 Seiten)');
    ok(await p.evaluate((pf) => pdfwerkHost.platform === pf && /^\d+\.\d+\.\d+/.test(pdfwerkHost.version), process.platform), 'pdfwerkHost meldet Plattform und Version');
    ok(await p.evaluate(() => typeof window.require === 'undefined' && typeof window.process === 'undefined' && typeof window.pdfwerkHost.invoke === 'undefined'), 'keine Node- oder IPC-Funktionen in der Seite');
    ok(p.url() === 'app://pdfwerk/index.html' && (await titleOf(app, p)) === 'Willkommen.pdf – PDFwerk', 'Seite auf app://pdfwerk, Fenstertitel vom Dokument');
  });
  await step('Worker und Schriften', async () => {
    const w = await p.evaluate(async () => {
      const t = pdfjsLib.getDocument({ data: S.bytes.slice() });
      await t.promise;
      const real = !!t._worker?._webWorker;
      await t.destroy();
      await document.fonts.ready;
      const loaded = [...document.fonts].filter((f) => f.status === 'loaded').map((f) => f.family.replace(/"/g, ''));
      return { real, fake: typeof globalThis.pdfjsWorker, origin: location.origin, sans: loaded.includes('IBM Plex Sans'), mono: loaded.includes('IBM Plex Mono') };
    });
    ok(w.real && w.fake === 'undefined' && w.origin === 'app://pdfwerk', 'pdf.js arbeitet mit echtem Web Worker');
    ok(w.sans && w.mono, 'Schriften IBM Plex Sans und Mono geladen');
    const h = await p.evaluate(async () => {
      const r = await fetch('app://pdfwerk/index.html'), w = await fetch('app://pdfwerk/lib/pdf.worker.min.js'), f = await fetch('app://pdfwerk/fonts/ibm-plex-sans-latin-400-normal.woff2');
      return { csp: r.headers.get('content-security-policy'), html: r.headers.get('content-type'), js: w.headers.get('content-type'), font: f.headers.get('content-type') };
    });
    ok(/default-src 'self'/.test(h.csp) && /object-src 'none'/.test(h.csp) && /worker-src 'self' blob:/.test(h.csp), 'CSP-Kopfzeile wird gesendet');
    ok(h.html.startsWith('text/html') && h.js.startsWith('text/javascript') && h.font === 'font/woff2', 'MIME-Typen stimmen');
    ok(!errors.length, 'keine Konsolenfehler beim Start' + (errors.length ? ': ' + errors.join(' | ') : ''));
  });
  await step('Menü', async () => {
    const m = await app.evaluate(({ app, Menu }) => {
      const menu = Menu.getApplicationMenu();
      const find = (top, label) => menu.items.find((i) => i.label.replace(/&/g, '') === top).submenu.items.find((i) => i.label === label);
      const save = find('Datei', 'Speichern');
      return { top: menu.items.map((i) => i.label), acc: save.accelerator, reg: save.registerAccelerator, dev: !!find('Ansicht', 'Entwicklertools'), packaged: app.isPackaged };
    });
    const top = process.platform === 'darwin' ? ['PDFwerk', 'Datei', 'Bearbeiten', 'Ansicht', 'Fenster', 'Hilfe'] : ['&Datei', '&Bearbeiten', '&Ansicht', '&Hilfe'];
    ok(JSON.stringify(m.top) === JSON.stringify(top), 'deutsches Menü (Windows/Linux mit Alt-Zugriffstasten): ' + m.top.join(', '));
    ok(m.acc === 'CmdOrCtrl+S' && m.reg === (process.platform === 'darwin') && m.dev === !m.packaged, 'Kürzel der Seite im Menü nur angezeigt, Entwicklertools nur ohne Paket');
    const click = (top, label) => app.evaluate(({ Menu, BrowserWindow }, [top, label]) => {
      Menu.getApplicationMenu().items.find((i) => i.label.replace(/&/g, '') === top).submenu.items.find((i) => i.label === label).click({}, BrowserWindow.getAllWindows()[0]);
    }, [top, label]);
    await click('Ansicht', 'Seiten');
    await p.waitForFunction(() => S.mode === 'pages', null, { timeout: 5000 });
    await click('Ansicht', 'Lesen');
    await p.waitForFunction(() => S.mode === 'read', null, { timeout: 5000 });
    ok(true, 'Menübefehle erreichen das Fenster');
  });
  await step('Fenster und Navigation', async () => {
    await app.evaluate(({ shell }) => { globalThis.__ext = []; shell.openExternal = async (u) => { globalThis.__ext.push(u); }; });
    await p.evaluate(() => { window.open('https://example.org/'); window.open('file:///etc/passwd'); });
    await sleep(500);
    const ext = await app.evaluate(() => globalThis.__ext);
    ok(ext.length === 1 && ext[0] === 'https://example.org/' && (await winCount(app)) === 1 && (await stats(app)).openExternal === 1, 'window.open öffnet https im Browser, kein neues Fenster, file: gar nicht');
    await p.evaluate(() => { location.href = 'https://example.org/'; }).catch(() => {});
    await sleep(800);
    await p.evaluate(() => { location.href = 'file:///etc/passwd'; }).catch(() => {});
    await sleep(800);
    ok(p.url() === 'app://pdfwerk/index.html' && (await p.evaluate(() => S.pages.length)) === 3, 'Navigation zu fremden Adressen wird verhindert');
  });
  await step('Pfad-Ausbruch', async () => {
    const urls = [
      'app://pdfwerk/../package.json', 'app://pdfwerk/%2e%2e/package.json', 'app://pdfwerk/%2e%2e%2fpackage.json',
      'app://pdfwerk/..%2f..%2fpackage.json', 'app://pdfwerk/lib/..%2f..%2f..%2fpackage.json', 'app://pdfwerk/..%5c..%5cpackage.json',
      'app://pdfwerk/%252e%252e/package.json', 'app://pdfwerk/%2e%2e%2f%2e%2e%2fdesktop%2fmain.js', 'app://pdfwerk/..%5cdesktop%5cmain.js',
      'app://pdfwerk/' + encodeURIComponent('/etc/passwd'), 'app://pdfwerk//etc/passwd', 'app://pdfwerk/lib',
    ];
    const res = await p.evaluate(async (urls) => Promise.all(urls.map(async (u) => {
      try { const r = await fetch(u); return [u, r.status, (await r.text()).slice(0, 4000)]; } catch (e) { return [u, 0, '']; }
    })), urls);
    const leak = res.filter(([, status, text]) => status === 200 || /devDependencies|requestSingleInstanceLock|root:/.test(text));
    ok(!leak.length, 'kein Zugriff außerhalb von dist/desktop' + (leak.length ? ': ' + leak.map((r) => r[0]).join(', ') : ''));
    const other = await app.evaluate(async ({ net }) => { try { return (await net.fetch('app://anderer/index.html')).status; } catch { return 0; } });
    ok(other !== 200, 'fremder Host unter app:// wird abgewiesen');
  });
  await step('Drucken', async () => {
    await app.evaluate(({ webContents }) => {
      globalThis.__prints = [];
      const proto = Object.getPrototypeOf(webContents.getAllWebContents()[0]);
      const fs = process.mainModule.require('node:fs'), url = process.mainModule.require('node:url');
      proto.print = function (opts, cb) {
        const html = fs.readFileSync(url.fileURLToPath(this.getURL()), 'utf8');
        globalThis.__prints.push({ opts, html, url: this.getURL(), js: this.getLastWebPreferences().javascript, sandbox: this.getLastWebPreferences().sandbox });
        setTimeout(() => cb(true, ''), 50);
      };
    });
    await p.keyboard.press('Control+p');
    const pr = await until(() => app.evaluate(() => globalThis.__prints[0]), 30000);
    ok(pr && pr.url.startsWith('file:') && pr.url.endsWith('/druck.html') && pr.opts.silent === false && pr.opts.printBackground === true && pr.js === false && pr.sandbox === true, 'Drucken über verstecktes Fenster ohne JavaScript');
    // jede Seite als eigenes Bild, Größe vom Papier (eine Querseite verkleinert die anderen nicht)
    ok((pr.html.match(/<img src="seite-\d+\.jpg"/g) || []).length === 3 && /section\{width:100vw;height:100vh/.test(pr.html) && !/style="width:/.test(pr.html), 'Druckseiten als Einzelbilder, auf das Papier eingepasst');
    const gone = await until(async () => !existsSync(dirname(fileURLToPath(pr.url))) && (await winCount(app)) === 1, 10000);
    ok(gone, 'Druckfenster und temporäre Dateien wieder entfernt' + (gone ? '' : ` (Ordner da: ${existsSync(dirname(fileURLToPath(pr.url)))}, Fenster: ${await winCount(app)})`));
  });
  await step('Öffnen-Dialog', async () => {
    const broken = join(out, 'kaputt.pdf');
    writeFileSync(broken, 'kein PDF');
    await stubOpen(app, [broken]);
    await p.keyboard.press('Control+o');
    await p.waitForFunction(() => document.querySelector('.toast')?.textContent.includes('keine lesbare PDF'), null, { timeout: 15000 });
    await sleep(300);
    const pristine = await app.evaluate(({ BrowserWindow }) => {
      const w = BrowserWindow.getAllWindows()[0];
      return w.webContents.executeJavaScript('S.pristine && S.pages.length === 3');
    });
    ok(pristine && (await winCount(app)) === 1, 'unlesbare Datei lässt das Beispiel stehen');
    await stubOpen(app, [docA]);
    await p.keyboard.press('Control+o');
    await p.waitForFunction((f) => S.path === f, docA, { timeout: 15000 });
    ok((await winCount(app)) === 1 && (await doc(p)).name === 'a.pdf', 'Öffnen aus unberührtem Beispiel bleibt im selben Fenster');
    await rotate(p);
    await stubOpen(app, [docB]);
    const p2 = await newWindow(app, () => p.keyboard.press('Control+o'));
    ok((await doc(p2)).path === docB && (await doc(p)).path === docA && (await winCount(app)) === 2, 'Öffnen aus geändertem Fenster öffnet ein neues Fenster');
    await stubOpen(app, [docA]);
    await p2.evaluate(() => pdfwerkHost.openDialog());
    await sleep(800);
    ok((await winCount(app)) === 2, 'schon geöffnete Datei öffnet kein zweites Fenster');
    await stubOpen(app, [img1, docC, img2]);
    const before = (await stats(app)).windows;
    await p2.evaluate(() => pdfwerkHost.openDialog());
    await until(() => app.windows().length === 4);
    const pages = app.windows().filter((w) => w !== p && w !== p2);
    for (const w of pages) await ready(w);
    const docs = await Promise.all(pages.map(doc));
    ok((await stats(app)).windows - before === 2 && docs.some((d) => d.path === docC) && docs.some((d) => d.name === 'Zusammengefügt.pdf' && d.pages === 2 && !d.path),
      'jede PDF in eigenem Fenster, Bilder zusammen in einem');
  });
  await step('Bild mit falscher Endung', async () => {
    const fake = makePng('foto.jpg');   // PNG-Daten mit .jpg
    const e0 = errors.length;
    await stubOpen(app, [fake]);
    const w = await newWindow(app, () => p.evaluate(() => pdfwerkHost.openDialog()));
    const fresh = errors.slice(e0);
    ok((await doc(w)).pages === 1 && !fresh.length, 'PNG mit Endung .jpg wird geöffnet' + (fresh.length ? ': ' + fresh.join(' | ') : ''));
  });
  await finish(run);
}

/* ---------- 2. Start mit Datei: Speichern, Speichern unter, Freigaben, Schließen, Beenden ---------- */
{
  const work = join(out, 'arbeit');
  mkdirSync(work);
  const file = join(work, 'test.pdf');
  writeFileSync(file, readFileSync(await makePdf('vorlage.pdf')));
  const c1 = await makePdf('c1.pdf'), c2 = await makePdf('c2.pdf'), c3 = await makePdf('c3.pdf');
  const run = await launch([file]);
  const { app, errors } = run;
  const p = await app.firstWindow();
  await step('Start mit Datei', async () => {
    await ready(p);
    const d = await doc(p);
    ok(d.name === 'test.pdf' && d.path === file && d.pages === 2 && !d.pristine, 'Startargument öffnet die Datei (Name, Pfad)');
    ok((await titleOf(app, p)) === 'test.pdf – PDFwerk', 'Fenstertitel zeigt den Dateinamen');
  });
  await step('Speichern', async () => {
    await rotate(p);
    const edited = process.platform === 'darwin'
      ? (await titleOf(app, p)) === 'test.pdf – PDFwerk' && (await app.browserWindow(p).then((h) => h.evaluate((w) => w.isDocumentEdited())))
      : (await titleOf(app, p)) === 'test.pdf (geändert) – PDFwerk';
    ok(edited, 'Fenstertitel kennzeichnet Änderungen');
    await p.keyboard.press('Control+s');
    await p.waitForFunction(() => !S.dirty, null, { timeout: 15000 });
    await sleep(800);
    ok((await stats(app)).saveFile === 1, 'Strg+S speichert genau einmal');
    ok((await rotation(file)) === 90 && (await PDFDocument.load(readFileSync(file))).getPageCount() === 2, 'Änderung steht in der Originaldatei');
    ok((await titleOf(app, p)) === 'test.pdf – PDFwerk', 'Titel nach dem Speichern ohne Kennzeichen');
    if (process.platform !== 'darwin') ok(JSON.parse(readFileSync(join(run.env.PDFWERK_USER_DATA, 'recent.json'), 'utf8'))[0] === file, 'Datei steht in „Zuletzt geöffnet“');
  });
  const copy = join(work, 'kopie.pdf');
  await step('Speichern unter', async () => {
    await stubSave(app, join(work, 'kopie'));
    await p.keyboard.press('Control+Shift+s');
    await p.waitForFunction((f) => S.path === f, copy, { timeout: 15000 });
    const o = await app.evaluate(() => globalThis.__saveOpts);
    ok(existsSync(copy) && (await doc(p)).name === 'kopie.pdf' && (await rotation(copy)) === 90, 'Speichern unter schreibt die Kopie und ergänzt die Endung');
    ok(o.defaultPath === file && o.filters[0].extensions.includes('pdf'), 'Dialog startet im Ordner der Datei mit PDF-Filter');
    await rotate(p);
    // Tastendruck wie vom Betriebssystem, über den Hauptprozess eingespeist
    await app.browserWindow(p).then((h) => h.evaluate((w) => {
      w.webContents.sendInputEvent({ type: 'keyDown', keyCode: 'S', modifiers: ['control'] });
      w.webContents.sendInputEvent({ type: 'keyUp', keyCode: 'S', modifiers: ['control'] });
    }));
    await p.waitForFunction(() => !S.dirty, null, { timeout: 15000 });
    await sleep(800);
    ok((await stats(app)).saveFile === 2 && (await rotation(copy)) === 180 && (await rotation(file)) === 90, 'danach speichert Strg+S genau einmal in die Kopie');
  });
  await step('Freigaben', async () => {
    const foreign = join(work, 'fremd.pdf');
    const msg = await p.evaluate((f) => pdfwerkHost.saveFile(f, new Uint8Array([37, 80, 68, 70])).then(() => 'gespeichert', (e) => e.message), foreign);
    ok(msg === 'Diese Datei darf PDFwerk nicht überschreiben.' && !existsSync(foreign), 'nicht freigegebene Pfade werden abgelehnt');
    const traversal = await p.evaluate((f) => pdfwerkHost.saveFile(f, new Uint8Array([1])).then(() => 'gespeichert', (e) => e.message), work + '/x/../fremd.pdf');
    ok(traversal !== 'gespeichert' && !existsSync(foreign), 'auch über ../ nicht');
    const dropped = await makePdf('abgelegt.pdf');
    await p.evaluate(() => { const i = document.createElement('input'); i.type = 'file'; i.id = 'testDrop'; i.hidden = true; document.body.append(i); });
    await p.setInputFiles('#testDrop', dropped);
    await stubBox(app, [0]);
    const r = await p.evaluate(async () => {
      const f = document.querySelector('#testDrop').files[0], data = new Uint8Array(await f.arrayBuffer());
      const path = pdfwerkHost.adoptDroppedFile(f);
      const none = pdfwerkHost.adoptDroppedFile(new File(['x'], 'nix.pdf'));
      const saved = await pdfwerkHost.saveFile(path, data);
      const again = await pdfwerkHost.saveFile(path, data);
      return { path, none, saved, again };
    });
    const b = await boxes(app);
    ok(r.path === dropped && r.none === null && r.saved.name === 'abgelegt.pdf' && r.again.name === 'abgelegt.pdf' && b.length === 1 && b[0].startsWith('„abgelegt.pdf“ überschreiben?'),
      'abgelegte Datei: das erste Speichern fragt nach, danach ist sie freigegeben');
    // ein von der Seite gemeldeter Pfad (wie aus einem übernommenen Renderer) gibt nichts frei, die Rückfrage zeigt den Ordner
    const victim = await makePdf('opfer.pdf'), was = readFileSync(victim);
    await app.evaluate(({ ipcMain, BrowserWindow }, f) => {
      const wc = BrowserWindow.getAllWindows()[0].webContents;
      ipcMain.emit('pdfwerk:adopt', { sender: wc, senderFrame: wc.mainFrame }, f);
    }, victim);
    await stubBox(app, [1]);
    const m = await p.evaluate((f) => pdfwerkHost.saveFile(f, new Uint8Array([37, 80, 68, 70])).then(() => 'gespeichert', (e) => e.message), victim);
    ok(m === 'Speichern abgebrochen.' && readFileSync(victim).equals(was) && (await boxes(app))[0]?.includes(dirname(victim)), 'gemeldeter Pfad wird nicht ohne Rückfrage überschrieben');
    const bad = await p.evaluate(() => pdfwerkHost.saveFile('/tmp/x.pdf', [1, 2]).then(() => 'angenommen', (e) => e.message));
    ok(bad !== 'angenommen', 'Daten müssen Uint8Array sein');
    // root darf auch schreibgeschützte Dateien ändern, daher nur ohne root (in der CI auf allen Systemen)
    if (process.getuid?.() !== 0) {
      const locked = await makePdf('schreibgeschuetzt.pdf');
      const before = readFileSync(locked);
      chmodSync(locked, 0o444);
      await p.setInputFiles('#testDrop', locked);
      await stubBox(app, [0]);
      const m = await p.evaluate(async () => {
        const f = document.querySelector('#testDrop').files[0];
        return pdfwerkHost.saveFile(pdfwerkHost.adoptDroppedFile(f), new Uint8Array([37, 80, 68, 70])).then(() => 'gespeichert', (e) => e.message);
      });
      chmodSync(locked, 0o644);
      ok(m === 'Die Datei ist schreibgeschützt oder in einem anderen Programm geöffnet.' && readFileSync(locked).equals(before), 'schreibgeschützte Datei bleibt unverändert, Meldung auf Deutsch');
    }
  });
  await step('Schließen mit Änderungen', async () => {
    await stubOpen(app, [c1]);
    const w2 = await newWindow(app, () => p.evaluate(() => pdfwerkHost.openDialog()));
    await stubOpen(app, [c2]);
    const w3 = await newWindow(app, () => p.evaluate(() => pdfwerkHost.openDialog()));
    await rotate(w2);
    await stubBox(app, [2], 400);
    await closeWin(app, w2);
    await closeWin(app, w2);
    await sleep(1200);
    const b = await boxes(app);
    ok(b.length === 1 && b[0].startsWith('Änderungen an „c1.pdf“ speichern?') && !w2.isClosed(), 'Abbrechen lässt das Fenster offen, kein doppelter Dialog');
    await stubBox(app, [1]);
    const gone2 = w2.waitForEvent('close', { timeout: 10000 });
    await closeWin(app, w2);
    await gone2;
    ok((await rotation(c1)) === 0, 'Nicht speichern schließt ohne zu speichern');
    await rotate(w3);
    await stubBox(app, [0]);
    const saves = (await stats(app)).saveFile;
    const gone3 = w3.waitForEvent('close', { timeout: 15000 });
    await closeWin(app, w3);
    await gone3;
    ok((await rotation(c2)) === 90 && (await stats(app)).saveFile === saves + 1, 'Speichern speichert und schließt');
  });
  await step('Beenden mit mehreren geänderten Fenstern', async () => {
    await stubOpen(app, [c3]);
    const w4 = await newWindow(app, () => p.evaluate(() => pdfwerkHost.openDialog()));
    await rotate(p);
    await rotate(w4);
    await stubBox(app, [1, 2]);
    const gone1 = p.waitForEvent('close', { timeout: 10000 });
    await app.evaluate(({ app }) => { app.quit(); });
    await gone1;
    await sleep(800);
    ok((await boxes(app)).length === 2 && !w4.isClosed() && !run.exited(), 'Abbrechen beim zweiten Fenster bricht das Beenden ab');
    ok(!errors.length, 'keine Konsolenfehler' + (errors.length ? ': ' + errors.join(' | ') : ''));
    await stubBox(app, [0]);
    await app.evaluate(({ app }) => { app.quit(); }).catch(() => {});
    await Promise.race([run.exit, sleep(15000)]);
    ok(run.exited() && (await rotation(c3)) === 90, 'nach „Speichern“ geht das Beenden weiter');
  });
  await finish(run);
}

/* ---------- 3. Mehrere Startdateien, zweite Instanz, macOS open-file ---------- */
{
  const p1 = await makePdf('p1.pdf'), p2 = await makePdf('p2.pdf');
  makePng('bild3.png'); makePng('bild4.Png');
  writeFileSync(join(out, 'notiz.txt'), 'kein PDF');
  const run = await launch(['bild1.png', 'p1.pdf', 'bild2.PNG', 'p1.pdf']);
  const { app, errors } = run;
  await step('Startdateien', async () => {
    await until(() => app.windows().length === 2, 20000);
    for (const w of app.windows()) await ready(w);
    const docs = await Promise.all(app.windows().map(doc));
    ok(app.windows().length === 2 && docs.some((d) => d.path === p1) && docs.some((d) => d.name === 'Zusammengefügt.pdf' && d.pages === 2),
      'relative Startargumente: PDF eigenes Fenster, Bilder zusammen, Duplikate entfernt');
  });
  await step('Zweite Instanz', async () => {
    await stubBox(app, [0]);
    const child = spawn(exe || require('electron'), appArgs(['p2.pdf', 'p1.pdf', 'fehlt.pdf', 'notiz.txt']), { cwd: out, env: run.env, stdio: 'ignore' });
    const code = await new Promise((r) => { child.once('exit', r); setTimeout(() => r('läuft noch'), 20000); });
    ok(code === 0, 'zweite Instanz beendet sich sofort');
    await until(() => app.windows().length === 3, 15000);
    const w = app.windows()[2];
    if (w) await ready(w);
    await until(async () => (await boxes(app)).length, 5000);
    const b = await boxes(app);
    ok(app.windows().length === 3 && w && (await doc(w)).path === p2, 'zweite Instanz öffnet neue Datei im laufenden Programm, schon offene nicht doppelt');
    ok(b.length === 1 && b[0].includes('fehlt.pdf') && b[0].includes('notiz.txt'), 'fehlende und nicht unterstützte Dateien werden gemeldet');
  });
  await step('open-file', async () => {
    const w = await newWindow(app, () => app.evaluate(({ app }, files) => { for (const f of files) app.emit('open-file', { preventDefault() {} }, f); }, [join(out, 'bild3.png'), join(out, 'bild4.Png')]));
    await sleep(500);
    const d = await doc(w);
    ok(d.pages === 2 && app.windows().length === 4, 'open-file sammelt mehrere Bilder in ein Fenster');
    ok(!errors.length, 'keine Konsolenfehler' + (errors.length ? ': ' + errors.join(' | ') : ''));
  });
  await step('Beenden, Speichern abgebrochen', async () => {
    const n = app.windows().length;
    await stubBox(app, [0]);
    await stubSave(app, null);
    await app.evaluate(({ app }) => { app.quit(); });
    await until(async () => (await app.evaluate(() => globalThis.__pdfwerkStats.saveDialog)) > 0, 10000);
    await sleep(800);
    ok(!run.exited() && (await boxes(app)).length === 1 && app.windows().length === n, 'abgebrochener Speichern-Dialog bricht das Beenden ab');
  });
  await step('Absturz', async () => {
    let w = null;
    for (const x of app.windows()) if ((await doc(x)).path === p1) w = x;
    await stubBox(app, [0]);
    const id = await app.browserWindow(w).then((h) => h.evaluate((win) => {
      // erst nach dem Neuladen abfragen: eine Abfrage an die abgestürzte Seite käme nie zurück
      globalThis.__reloaded = new Promise((r) => win.webContents.once('did-finish-load', () => r(true)));
      win.webContents.forcefullyCrashRenderer();
      return win.id;
    }));
    await until(async () => (await boxes(app)).length, 10000);
    const loaded = await app.evaluate(() => Promise.race([globalThis.__reloaded, new Promise((r) => setTimeout(() => r(false), 20000))]));
    // Die Playwright-Seite überlebt den Absturz nicht, daher über den Hauptprozess nachsehen
    const path = loaded && await until(() => app.evaluate(({ BrowserWindow }, id) => Promise.race([
      BrowserWindow.fromId(id).webContents.executeJavaScript('typeof S !== "undefined" && S.pdf ? S.path : null'),
      new Promise((r) => setTimeout(() => r(null), 3000)),
    ]), id), 20000);
    ok((await boxes(app))[0]?.startsWith('Das Fenster wurde unerwartet beendet.') && path === p1, 'nach einem Absturz lädt „Neu laden“ die Datei wieder');
  });
  await finish(run);
}

/* ---------- 4. Schwärzen, geschützte PDFs, neue Dokumente, Notizen, Speichern unter Last, Beenden ---------- */
// Metadaten der Datei setzen und lesen: macOS Finder-Tag, Linux erweitertes Attribut, Windows Mark of the Web
const META = {
  darwin: { set: (f) => spawnSync('/usr/bin/xattr', ['-w', 'com.apple.metadata:_kMDItemUserTags', 'Rot', f]).status === 0, get: (f) => spawnSync('/usr/bin/xattr', ['-p', 'com.apple.metadata:_kMDItemUserTags', f], { encoding: 'utf8' }).stdout.trim() },
  linux: { set: (f) => spawnSync('python3', ['-c', 'import os,sys; os.setxattr(sys.argv[1], "user.pdfwerk.etikett", b"Rot")', f]).status === 0, get: (f) => spawnSync('python3', ['-c', 'import os,sys; print(os.getxattr(sys.argv[1], "user.pdfwerk.etikett").decode())', f], { encoding: 'utf8' }).stdout.trim() },
  win32: { set: (f) => { try { writeFileSync(f + ':Zone.Identifier', 'Rot'); return true; } catch { return false; } }, get: (f) => { try { return readFileSync(f + ':Zone.Identifier', 'utf8'); } catch { return ''; } } },
}[process.platform];
{
  const dir = join(out, 'vier');
  mkdirSync(dir);
  const secret = join(dir, 'geheim.pdf'), owner = join(dir, 'eigentuemer.pdf'), user = join(dir, 'passwort.pdf'), comments = join(dir, 'kommentiert.pdf');
  writeFileSync(secret, await makeSecretPdf());
  writeFileSync(owner, makeEncryptedPdf('Kontoauszug', '', 'eigen'));
  writeFileSync(user, makeEncryptedPdf('Kontoauszug', 'geheim', 'eigen'));
  writeFileSync(comments, await makeCommentedPdf());
  const partA = await makePdf('teil-a.pdf', 1), partB = await makePdf('teil-b.pdf', 1);
  const run = await launch([secret]);
  const { app, errors } = run;
  const p = await app.firstWindow();
  const saves = async () => (await stats(app)).saveFile;
  // wartet, bis ein mit Strg+S begonnenes Speichern fertig geschrieben ist
  const saveDone = async (w, n0) => { await until(async () => (await saves()) > n0, 20000); await w.evaluate(() => saveQueue); };
  const openNew = async (file) => { await stubOpen(app, [file]); return newWindow(app, () => p.evaluate(() => pdfwerkHost.openDialog())); };
  let wOwner, wUser, wNotes;
  await step('Schwärzen', async () => {
    await ready(p);
    await p.evaluate(() => { setMode('comment'); addAnn(0, { type: 'redact', x1: 0, y1: 0, x2: S.pages[0].w, y2: S.pages[0].h, rot: 0 }); });
    await keys(app, p, 'S');
    await p.waitForFunction(() => !S.dirty, null, { timeout: 30000 });
    const bytes = readFileSync(secret);
    const leak = [await findText(bytes, SECRET), await findText(bytes, FIELD_SECRET)].filter(Boolean);
    ok(!leak.length, 'geschwärzter Text und Wert des Formularfelds stehen nirgends mehr in der Datei' + (leak.length ? ': ' + leak.join(', ') : ''));
    const d = await PDFDocument.load(bytes);
    const dest = d.catalog.lookup(PDFName.of('Outlines'))?.lookup(PDFName.of('First'))?.lookup(PDFName.of('Dest'));
    ok(d.getPageCount() === 2 && dest?.get(0) === d.getPage(0).ref && !!(await findText(bytes, KEEP)), 'Seitenzahl, Lesezeichen auf Seite 1 und Text auf Seite 2 bleiben');
  });
  await step('Geschützte PDF', async () => {
    for (const [file, pw] of [[owner, null], [user, 'geheim']]) {
      const before = readFileSync(file), name = file.split(/[\\/]/).pop().replace('.pdf', ' (entsperrt).pdf');
      await stubOpen(app, [file]);
      const ev = app.waitForEvent('window', { timeout: 20000 });
      await p.evaluate(() => pdfwerkHost.openDialog());
      const w = await ev;
      if (pw) {
        await w.waitForSelector('.backdrop input[name=pw]', { timeout: 20000 });
        await w.fill('.backdrop input[name=pw]', pw);
        await w.click('.backdrop button[type=submit]');
      }
      await ready(w);
      if (pw) wUser = w; else wOwner = w;
      const d = await doc(w);
      ok(d.name === name && d.path === null, `${pw ? 'Öffnen' : 'Eigentümer'}-Passwort: entsperrte Kopie ohne Speicherort („${d.name}“)`);
      await stubSave(app, null);
      const st0 = await stats(app);
      await keys(app, w, 'S');
      await until(async () => (await stats(app)).saveDialog > st0.saveDialog, 20000);
      const st1 = await stats(app), o = await app.evaluate(() => globalThis.__saveOpts);
      ok(st1.saveFile === st0.saveFile && readFileSync(file).equals(before) && o?.defaultPath === join(dir, name), '… Strg+S fragt nach dem Ort (Ordner des Originals), das Original bleibt unverändert');
    }
  });
  await step('Neues Dokument', async () => {
    const before = readFileSync(secret);
    await p.evaluate(() => setMode('tools'));
    await p.click('.tcard:has-text("Zusammenfügen")');
    await p.setInputFiles('.backdrop input[type=file]', [partA, partB]);
    await p.uncheck('.backdrop input[name=cur]');
    await p.click('.backdrop button[type=submit]');
    await p.waitForFunction(() => S.name === 'Zusammengefügt.pdf', null, { timeout: 20000 });
    ok((await doc(p)).path === null && (await titleOf(app, p)).startsWith('Zusammengefügt.pdf'), 'Zusammenfügen ohne aktuelles Dokument löst die Verbindung zur geöffneten Datei');
    await stubSave(app, null);
    const st0 = await stats(app);
    await keys(app, p, 'S');
    await until(async () => (await stats(app)).saveDialog > st0.saveDialog, 20000);
    ok((await stats(app)).saveFile === st0.saveFile && readFileSync(secret).equals(before), '… Strg+S fragt nach dem Ort, die vorher geöffnete Datei bleibt unverändert');
    await p.evaluate(() => undo());
    const d = await doc(p);
    ok(d.path === secret && d.name === 'geheim.pdf', 'Rückgängig holt Dokument und Speicherort zurück');
    await keys(app, p, 'S');
    await p.waitForFunction(() => !S.dirty, null, { timeout: 20000 });
  });
  await step('Notizen', async () => {
    wNotes = await openNew(comments);
    ok((await wNotes.evaluate(() => S.ann[0].map((a) => a.type).join())) === 'note', 'nur die Notiz wird zum Bearbeiten übernommen, die Antwort hängt an ihr');
    const n0 = await saves();
    await keys(app, wNotes, 'S');
    await saveDone(wNotes, n0);
    let c = await readComments(readFileSync(comments));
    const find = (c) => ({ note: c.find((x) => x.type === '/Text' && x.author === NOTE.author), reply: c.find((x) => x.author === NOTE.reply), hl: c.find((x) => x.type === '/Highlight') });
    let f = find(c);
    ok(c.length === 5 && f.note?.date === NOTE.date && f.note.popupOnPage && f.reply?.irtOnPage && f.hl?.popupOnPage,
      'Speichern ohne Änderung: Autor, Datum, Antwort und Popups bleiben' + (c.length === 5 ? '' : ` (${c.length} Anmerkungen)`));
    await wNotes.evaluate(() => { snapshot(); S.ann[0][0].x += 30; });
    const n1 = await saves();
    await keys(app, wNotes, 'S');
    await saveDone(wNotes, n1);
    c = await readComments(readFileSync(comments));
    f = find(c);
    ok(c.length === 5 && f.note && f.note.date !== NOTE.date && f.note.text === NOTE.text && f.reply?.irtOnPage, 'verschobene Notiz: neues Änderungsdatum, Autor und Antwort bleiben');
  });
  await step('Änderung während des Speicherns', async () => {
    // langsames Laufwerk: das Ersetzen der Datei dauert 1,5 s
    await app.evaluate(() => {
      const fsp = process.mainModule.require('node:fs').promises;
      globalThis.__rename = fsp.rename;
      fsp.rename = async (...a) => { await new Promise((r) => setTimeout(r, 1500)); return globalThis.__rename.apply(fsp, a); };
    });
    try {
      const n0 = await saves();
      await keys(app, wNotes, 'S');
      await until(async () => (await saves()) > n0, 10000);
      await rotate(wNotes);
      await wNotes.waitForFunction(() => document.querySelector('.toast')?.textContent.includes('noch nicht gesichert'), null, { timeout: 15000 });
      const marked = process.platform === 'darwin'
        ? await app.browserWindow(wNotes).then((h) => h.evaluate((w) => w.isDocumentEdited()))
        : (await titleOf(app, wNotes)).includes('(geändert)');
      ok((await wNotes.evaluate(() => S.dirty)) && marked && (await rotation(comments)) === 0, 'Änderung während des Speicherns bleibt als ungespeichert markiert');
    } finally {
      await app.evaluate(() => { process.mainModule.require('node:fs').promises.rename = globalThis.__rename; });
    }
    await stubBox(app, [2]);
    await closeWin(app, wNotes);
    await until(async () => (await boxes(app)).length, 10000);
    ok((await boxes(app)).length === 1 && !wNotes.isClosed(), '… und Schließen fragt nach');
  });
  await step('Strg+S im Textfeld', async () => {
    await wNotes.evaluate(() => { setMode('comment'); pickTool('text'); });
    // der Moduswechsel baut die Ansicht im nächsten Bild evtl. neu auf; erst danach das Textfeld anlegen
    await wNotes.evaluate(() => new Promise((r) => requestAnimationFrame(() => setTimeout(r, 100))));
    await wNotes.evaluate(() => createTextAt(0, 100, 600));
    await wNotes.keyboard.type('Neuer Text');
    const n0 = await saves();
    await keys(app, wNotes, 'S');
    await saveDone(wNotes, n0);
    const found = await findText(readFileSync(comments), 'Neuer Text'), rot = await rotation(comments);
    ok(!!found && rot === 90, 'Strg+S während der Texteingabe speichert, mitsamt Text' + (found && rot === 90 ? '' : ` (Text ${found}, Drehung ${rot}, ${JSON.stringify(await wNotes.evaluate(() => ({ dirty: S.dirty, editing: S.editing, ann: S.ann[0].map((a) => a.type + ':' + (a.text || '')) })))}, ${await toastText(wNotes)})`));
  });
  await step('Fokus in Bedienelementen', async () => {
    await wNotes.evaluate(() => { setMode('comment'); pickTool('rect'); });
    await wNotes.click('#wRange');
    const bx = await wNotes.locator('.page').first().boundingBox();
    await wNotes.mouse.move(bx.x + 100, bx.y + 300); await wNotes.mouse.down();
    await wNotes.mouse.move(bx.x + 250, bx.y + 400, { steps: 4 }); await wNotes.mouse.up();
    const n = await wNotes.evaluate(() => S.ann[0].length);
    await keys(app, wNotes, 'Z');
    await sleep(300);
    ok(await wNotes.evaluate((n) => S.ann[0].length === n - 1 && document.activeElement?.id !== 'wRange', n), 'nach dem Zeichnen mit verstelltem Regler nimmt Strg+Z zurück');
    await wNotes.evaluate(() => setMode('read'));
    await rotate(wNotes);
    await wNotes.focus('#zoomSel');
    const h0 = await wNotes.evaluate(() => S.history.length);
    await keys(app, wNotes, 'Z');
    ok(!!(await until(() => wNotes.evaluate((h0) => S.history.length === h0 - 1, h0), 10000)), 'Strg+Z wirkt auch, wenn die Zoom-Auswahl den Fokus hat');
  });
  await step('Abmelden mit Änderungen', async () => {
    await stubBox(app, [2]);
    const prevented = await app.browserWindow(wNotes).then((h) => h.evaluate((win) => {
      let stop = false;
      win.emit('query-session-end', { preventDefault: () => { stop = true; }, reasons: ['logoff'] });
      return stop;
    }));
    await until(async () => (await boxes(app)).length, 10000);
    ok(prevented && (await boxes(app)).length === 1 && !run.exited(), 'Windows-Abmelden mit ungespeicherten Änderungen wird aufgehalten und fragt nach');
  });
  await step('Beenden, Änderung während der Rückfrage', async () => {
    // wOwner (unverändert) steht vor wNotes (geändert); während der Rückfrage für wNotes wird wOwner geändert
    const gone = wNotes.waitForEvent('close', { timeout: 20000 });
    await stubBox(app, [1, 2], 1500);
    await app.evaluate(({ app }) => { app.quit(); });
    await sleep(400);
    await rotate(wOwner);
    await gone;
    await until(async () => (await boxes(app)).length === 2, 10000);
    await sleep(300);
    const b = await boxes(app);
    ok(b.length === 2 && b[1].includes('eigentuemer (entsperrt).pdf') && !wOwner.isClosed() && !run.exited(), 'Beenden fragt auch für ein Fenster nach, das während der Rückfrage geändert wurde');
  });
  await step('Doppelklick während des Ladens', async () => {
    const big = await makePdf('gross.pdf', 1500);
    const n = await winCount(app);
    const ev = app.waitForEvent('window', { timeout: 20000 });
    const emit = () => app.evaluate(({ app }, f) => { app.emit('open-file', { preventDefault() {} }, f); }, big);
    await emit();
    const w = await ev;
    await w.waitForFunction(() => document.querySelector('#statusMsg')?.textContent.startsWith('Öffne'), null, { timeout: 20000 }).catch(() => {});
    await emit();
    await ready(w);
    await sleep(800);
    ok((await winCount(app)) === n + 1 && (await doc(w)).path === big, 'zweiter Doppelklick während des Ladens öffnet kein zweites Fenster');
  });
  await step('Umbenannte Datei', async () => {
    const f = await makePdf('umbenennen.pdf');
    const w = await openNew(f);
    renameSync(f, f.replace('umbenennen.pdf', 'umbenannt.pdf'));
    await rotate(w);
    await stubSave(app, null);
    const st0 = await stats(app);
    await keys(app, w, 'S');
    await until(async () => (await stats(app)).saveDialog > st0.saveDialog, 20000);
    ok(!existsSync(f), 'Speichern legt eine umbenannte Datei nicht unter dem alten Namen neu an');
  });
  const tagged = await makePdf('etikett.pdf');
  await step('Metadaten der Datei', async () => {
    const w = await openNew(tagged);
    if (!META?.set(tagged)) { ok(true, 'Metadaten: auf diesem Dateisystem nicht prüfbar, übersprungen'); return; }
    await rotate(w);
    await keys(app, w, 'S');
    await w.waitForFunction(() => !S.dirty, null, { timeout: 20000 });
    ok((await rotation(tagged)) === 90 && META.get(tagged).includes('Rot'), 'Speichern behält Finder-Tags, erweiterte Attribute bzw. den Mark of the Web');
  });
  await step('Ablegen einer schon offenen Datei', async () => {
    const target = tagged;
    if (!wUser) throw new Error('Vorbedingung fehlt');
    await wUser.evaluate(() => { const i = document.createElement('input'); i.type = 'file'; i.id = 'testDrop'; i.hidden = true; document.body.append(i); });
    await wUser.setInputFiles('#testDrop', target);
    const n = await winCount(app);
    await wUser.evaluate(() => {
      const dt = new DataTransfer();
      dt.items.add(document.querySelector('#testDrop').files[0]);
      document.querySelector('#desk').dispatchEvent(new DragEvent('drop', { dataTransfer: dt, bubbles: true, cancelable: true }));
    });
    await sleep(1000);
    ok((await doc(wUser)).name === 'passwort (entsperrt).pdf' && (await winCount(app)) === n, 'abgelegte PDF, die schon in einem anderen Fenster offen ist, wird nicht doppelt geöffnet');
    // Speichern unter auf eine Datei, die ein anderes Fenster offen hat
    const before = readFileSync(target);
    await stubSave(app, target);
    await wUser.evaluate(() => saveAs());
    ok(readFileSync(target).equals(before) && (await toastText(wUser)).includes('anderen PDFwerk-Fenster'), 'Speichern unter lehnt eine in einem anderen Fenster geöffnete Datei ab');
  });
  await step('Fehler', async () => { ok(!errors.length, 'keine Konsolenfehler' + (errors.length ? ': ' + errors.join(' | ') : '')); });
  await finish(run);
}

/* ---------- 5. Langes Dokument: nur Seiten nahe am Lesepunkt bleiben gerendert, sonst friert das Programm ein ---------- */
{
  const long = join(out, 'lang.pdf');
  writeFileSync(long, await makeLongPdf(120));
  const run = await launch([long]);
  const { app, errors } = run;
  const p = await app.firstWindow();
  await step('Langes Dokument', async () => {
    await p.waitForFunction(() => typeof S !== 'undefined' && S.pages.length === 120 && views.length === 120, null, { timeout: 30000 });
    await browseLongPdf(p, ok);
    ok(!errors.length, 'keine Konsolenfehler' + (errors.length ? ': ' + errors.join(' | ') : ''));
  });
  await finish(run);
}

/* ---------- 6. Nur gepacktes Programm: keine Fernsteuerung von außen ---------- */
if (exe) {
  await step('Fernsteuerung', async () => {
    const env = { ...process.env };
    delete env.PDFWERK_TEST;
    const child = spawn(exe, [...sandboxArgs, '--remote-debugging-port=0'], { cwd: out, env, stdio: 'ignore' });
    const code = await new Promise((r) => { child.once('exit', r); setTimeout(() => { child.kill(); r('läuft noch'); }, 20000); });
    ok(code === 1, 'Paket beendet sich bei --remote-debugging-port ohne Inspector sofort (Code ' + code + ')');
  });
}

console.log(fails ? `${fails} Prüfung(en) fehlgeschlagen, Testdateien in ${out}` : 'Alle Prüfungen bestanden');
if (!fails) try { rmSync(out, { recursive: true, force: true, maxRetries: 5 }); } catch { /* Windows: Datei noch gesperrt */ }
process.exit(fails ? 1 : 0);
