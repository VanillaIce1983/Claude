// Rauchtest der Desktop-Fassung: startet die Electron-Hülle und spielt Öffnen, Speichern, Schließen und die Sicherheitsregeln durch.
// Voraussetzung: npm run build. Ohne Bildschirm: xvfb-run -a npm run test:desktop
// PDFWERK_EXE=<Programmdatei> prüft statt des Quellordners das fertige Paket (z. B. release/linux-unpacked/pdfwerk).
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync, realpathSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
const require = createRequire(import.meta.url);
const { _electron } = require('playwright-core');
const { PDFDocument, StandardFonts } = require('pdf-lib');

const root = fileURLToPath(new URL('../', import.meta.url));
const exe = process.env.PDFWERK_EXE ? resolve(process.env.PDFWERK_EXE) : null;
// echter Pfad: macOS-Temp liegt hinter einem Symlink, Windows-Temp oft als 8.3-Kurzname
const out = realpathSync.native(mkdtempSync(join(tmpdir(), 'pdfwerk-desktop-')));
const sandboxArgs = process.platform === 'linux' && process.getuid?.() === 0 ? ['--no-sandbox'] : [];
const appArgs = (args) => [...sandboxArgs, ...(exe ? [] : [root]), ...args];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let fails = 0;
const ok = (cond, msg) => { console.log((cond ? 'ok   ' : 'FAIL ') + msg); if (!cond) fails++; };
const step = async (name, fn) => { try { await fn(); } catch (e) { ok(false, `${name}: ${e.message.split('\n')[0]}`); } };

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
async function until(fn, ms = 15000) {
  const end = Date.now() + ms;
  for (;;) { const v = await fn(); if (v || Date.now() > end) return v; await sleep(100); }
}
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
      const find = (top, label) => menu.items.find((i) => i.label === top).submenu.items.find((i) => i.label === label);
      const save = find('Datei', 'Speichern');
      return { top: menu.items.map((i) => i.label), acc: save.accelerator, reg: save.registerAccelerator, dev: !!find('Ansicht', 'Entwicklertools'), packaged: app.isPackaged };
    });
    const top = process.platform === 'darwin' ? ['PDFwerk', 'Datei', 'Bearbeiten', 'Ansicht', 'Fenster', 'Hilfe'] : ['Datei', 'Bearbeiten', 'Ansicht', 'Hilfe'];
    ok(JSON.stringify(m.top) === JSON.stringify(top), 'deutsches Menü: ' + m.top.join(', '));
    ok(m.acc === 'CmdOrCtrl+S' && m.reg === (process.platform === 'darwin') && m.dev === !m.packaged, 'Kürzel der Seite im Menü nur angezeigt, Entwicklertools nur ohne Paket');
    const click = (top, label) => app.evaluate(({ Menu, BrowserWindow }, [top, label]) => {
      Menu.getApplicationMenu().items.find((i) => i.label === top).submenu.items.find((i) => i.label === label).click({}, BrowserWindow.getAllWindows()[0]);
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
      proto.print = function (opts, cb) {
        globalThis.__prints.push({ opts, url: this.getURL(), js: this.getLastWebPreferences().javascript, sandbox: this.getLastWebPreferences().sandbox });
        setTimeout(() => cb(true, ''), 50);
      };
    });
    await p.keyboard.press('Control+p');
    const pr = await until(() => app.evaluate(() => globalThis.__prints[0]), 30000);
    ok(pr && pr.url.startsWith('file:') && pr.url.endsWith('/druck.html') && pr.opts.silent === false && pr.opts.printBackground === true && pr.js === false && pr.sandbox === true, 'Drucken über verstecktes Fenster ohne JavaScript');
    await sleep(500);
    ok(!existsSync(fileURLToPath(pr.url)) && (await winCount(app)) === 1, 'Druckfenster und temporäre Datei wieder entfernt');
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
    const r = await p.evaluate(async () => {
      const f = document.querySelector('#testDrop').files[0];
      const path = pdfwerkHost.adoptDroppedFile(f);
      const none = pdfwerkHost.adoptDroppedFile(new File(['x'], 'nix.pdf'));
      const saved = await pdfwerkHost.saveFile(path, new Uint8Array(await f.arrayBuffer()));
      return { path, none, saved };
    });
    ok(r.path === dropped && r.none === null && r.saved.name === 'abgelegt.pdf', 'abgelegte Datei wird per adoptDroppedFile freigegeben');
    const bad = await p.evaluate(() => pdfwerkHost.saveFile('/tmp/x.pdf', [1, 2]).then(() => 'angenommen', (e) => e.message));
    ok(bad !== 'angenommen', 'Daten müssen Uint8Array sein');
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
    const id = await app.browserWindow(w).then((h) => h.evaluate((win) => { win.webContents.forcefullyCrashRenderer(); return win.id; }));
    await until(async () => (await boxes(app)).length, 10000);
    // Die Playwright-Seite überlebt den Absturz nicht, daher über den Hauptprozess nachsehen
    const path = await until(() => app.evaluate(({ BrowserWindow }, id) => BrowserWindow.fromId(id).webContents
      .executeJavaScript('typeof S !== "undefined" && S.pdf ? S.path : null').catch(() => null), id), 20000);
    ok((await boxes(app))[0].startsWith('Das Fenster wurde unerwartet beendet.') && path === p1, 'nach einem Absturz lädt „Neu laden“ die Datei wieder');
  });
  await finish(run);
}

console.log(fails ? `${fails} Prüfung(en) fehlgeschlagen, Testdateien in ${out}` : 'Alle Prüfungen bestanden');
if (!fails) try { rmSync(out, { recursive: true, force: true, maxRetries: 5 }); } catch { /* Windows: Datei noch gesperrt */ }
process.exit(fails ? 1 : 0);
