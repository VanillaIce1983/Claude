// Diagnose zum Einfrieren beim Wechsel zwischen Lesen, Bearbeiten, Seiten und Werkzeuge: startet die Desktop-Fassung, wechselt
// in mehreren Szenarien per Klick, Tastenkürzel und Menübefehl durch alle zwölf Moduswechsel und misst je Wechsel die Zeit bis zum
// nächsten Bild, lange Aufgaben im Fenster, Verzögerungen im Hauptprozess, Speicher und CPU je Prozess sowie den GPU-Status.
// Voraussetzung: npm run build. Ohne Bildschirm: xvfb-run -a node scripts/diag-modes.mjs
// PDFWERK_EXE=<Programmdatei> misst statt des Quellordners ein Testpaket mit Node-Inspector (z. B. release-test/linux-unpacked/pdfwerk).
// PDFWERK_DIAG_RUNDEN=<n>: Runden je Weg und Szenario (Standard 2). PDFWERK_DIAG_JSON=<Datei>: Messwerte zusätzlich als Datei.
// Ausgabe: Tabellen, zuletzt eine Zeile „DIAG-JSON {…}“ mit allen Messwerten.
// Exit-Code 1, wenn ein Wechsel länger als 5 s dauert (ein Hänger wird bis 120 s gemessen); 2, wenn nichts gemessen werden konnte; sonst 0.
import { mkdtempSync, writeFileSync, rmSync, realpathSync } from 'node:fs';
import { tmpdir, cpus, totalmem, release, arch } from 'node:os';
import { join, resolve } from 'node:path';
import { deflateSync } from 'node:zlib';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
const require = createRequire(import.meta.url);
const { _electron } = require('playwright-core');
const { PDFDocument, StandardFonts, pushGraphicsState, popGraphicsState, concatTransformationMatrix, drawObject } = require('pdf-lib');

const root = fileURLToPath(new URL('../', import.meta.url));
const exe = process.env.PDFWERK_EXE ? resolve(process.env.PDFWERK_EXE) : null;
// echter Pfad: macOS-Temp liegt hinter einem Symlink, Windows-Temp oft als 8.3-Kurzname
const out = realpathSync.native(mkdtempSync(join(tmpdir(), 'pdfwerk-diag-')));
const sandboxArgs = process.platform === 'linux' && process.getuid?.() === 0 ? ['--no-sandbox'] : [];
const appArgs = (args) => [...sandboxArgs, ...(exe ? [] : [root]), ...args];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const RUNDEN = Math.max(1, parseInt(process.env.PDFWERK_DIAG_RUNDEN, 10) || 2);
const FRIST = 120000;                 // je Wechsel: ein Hänger wird gemessen statt abgebrochen
const GRENZE = 5000;                  // länger gilt als Einfrieren (Exit-Code 1)
const AUFFAELLIG = 250;               // ab hier in der Liste der auffälligen Wechsel
const PAUSEN = [150, 500, 1200];      // abwechselnd kurz und lang: auch mitten in noch laufendes Zeichnen hinein wechseln
const MODI = { read: 'Lesen', comment: 'Bearbeiten', pages: 'Seiten', tools: 'Werkzeuge' };
const TASTE = { read: '1', comment: '2', pages: '3', tools: '4' };
// alle zwölf Wechsel zwischen den vier Modi je einmal, Anfang und Ende in Lesen
const RUNDE = ['comment', 'pages', 'tools', 'read', 'pages', 'comment', 'tools', 'comment', 'read', 'tools', 'pages', 'read'];
const WEGE = ['Klick', 'Taste', 'Menü'];
const MOD = process.platform === 'darwin' ? 'meta' : 'control';
// in der Oberfläche mitgestoppt (synchroner Anteil; nur die Funktionen, die es gibt)
const FUNKTIONEN = ['setMode', 'renderOrganizer', 'renderToolbox', 'buildViewer', 'renderCtx', 'applyToolClass', 'redrawAllOverlays',
  'drawOverlay', 'renderSide', 'renderPage', 'releasePage', 'trimLive', 'getThumb', 'commitEdit', 'rezoom'];

const ergebnis = {
  werkzeug: 'diag-modes', start: new Date().toISOString(), programm: exe || root, runden: RUNDEN, grenzeMs: GRENZE,
  system: { plattform: process.platform, version: release(), arch: arch(), kerne: cpus().length, cpu: cpus()[0]?.model?.trim() || null, speicherGB: +(totalmem() / 2 ** 30).toFixed(1) },
  sitzungen: [], szenarien: [], fehler: [],
};
const alle = [];   // alle Wechsel aller Szenarien, für Zusammenfassung und Exit-Code
let pausenZaehler = 0;

const kurz = (e) => String(e?.message || e).split('\n')[0];
const fmt = (ms) => (ms == null ? '–' : ms >= 10000 ? `${(ms / 1000).toFixed(0)} s` : ms >= 1000 ? `${(ms / 1000).toFixed(1)} s` : `${Math.round(ms)} ms`);
const median = (a) => { const s = [...a].sort((x, y) => x - y); return s.length ? s[Math.floor(s.length / 2)] : null; };
const quantil = (a, q) => { const s = [...a].sort((x, y) => x - y); return s.length ? s[Math.min(s.length - 1, Math.ceil(q * s.length) - 1)] : null; };
const maxBy = (a, f) => a.reduce((m, x) => (m == null || f(x) > f(m) ? x : m), null);
const uhr = (t) => new Date(t).toISOString().slice(11, 23);
// Ein Aufruf, der nie zurückkommt, endet mit der Frist; seine spätere Ablehnung darf den Lauf nicht beenden
async function mitFrist(promise, ms, text = 'Zeitüberschreitung') {
  const p = Promise.resolve(promise);
  p.catch(() => {});
  let timer;
  try { return await Promise.race([p, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(text)), ms); })]); }
  finally { clearTimeout(timer); }
}
function tabelle(kopf, zeilen, rechts = new Set()) {
  const breite = kopf.map((k, i) => Math.max(String(k).length, ...zeilen.map((z) => String(z[i] ?? '').length)));
  const zeile = (z) => '  ' + z.map((x, i) => (rechts.has(i) ? String(x ?? '').padStart(breite[i]) : String(x ?? '').padEnd(breite[i]))).join('  ').trimEnd();
  console.log(zeile(kopf));
  zeilen.forEach((z) => console.log(zeile(z)));
}

/* ---------- Testdatei: 30 Seiten mit großen Bildern ---------- */
// Jede Seite trägt ein eigenes Bild in Scanauflösung (A4, 300 dpi, RGB, Flate): entpackt 26 MB je Seite, die pdf.js für Seiten und
// Vorschaubilder dekodieren und verkleinern muss. Wenig Rauschen hält die Datei bei etwa 30 MB.
async function makeBilderPdf(file, seiten = 30, W = 2480, H = 3508) {
  const doc = await PDFDocument.create();
  const font = await doc.embedFont(StandardFonts.Helvetica);
  const raw = Buffer.allocUnsafe(W * H * 3);
  for (let k = 0; k < seiten; k++) {
    let seed = 12345 + k * 7919;
    for (let y = 0; y < H; y++) {
      const foto = y > 600 && y < 2000, zeile = !foto && y % 70 < 22, row = y * W * 3;
      for (let x = 0; x < W; x++) {
        const o = row + x * 3;
        if (foto) {
          seed = (seed * 1103515245 + 12345) >>> 0;
          raw[o] = ((x * 255) / W + k * 9 + (x & 31 ? 0 : seed >>> 30)) & 255;
          raw[o + 1] = ((y >> 2) + 40 * Math.sin(x / 90 + k)) & 255;
          raw[o + 2] = (((x >> 4) ^ (y >> 4)) * 3) & 255;
        } else if (zeile && x > 200 && x < 2280 && ((x >> 5) + ((y / 70) | 0) + k) % 7 !== 0) raw[o] = raw[o + 1] = raw[o + 2] = 40;
        else raw[o] = raw[o + 1] = raw[o + 2] = 250;
      }
    }
    const bild = doc.context.register(doc.context.stream(deflateSync(raw, { level: 6 }), {
      Type: 'XObject', Subtype: 'Image', Width: W, Height: H, ColorSpace: 'DeviceRGB', BitsPerComponent: 8, Filter: 'FlateDecode',
    }));
    const page = doc.addPage([595, 842]);
    const name = page.node.newXObject('Scan', bild);
    page.pushOperators(pushGraphicsState(), concatTransformationMatrix(595, 0, 0, 842, 0, 0), drawObject(name), popGraphicsState());
    page.drawText(`Diagnose Seite ${k + 1}`, { x: 40, y: 20, size: 10, font });
  }
  const bytes = await doc.save({ useObjectStreams: false });
  writeFileSync(file, bytes);
  return bytes.length;
}

/* ---------- Messfühler ---------- */
// Hauptprozess: Takt-Verzögerung der Ereignisschleife, Speicher und CPU je Prozess jede Sekunde, abgestürzte oder hängende Prozesse
const HAUPT_MESSUNG = ({ app, BrowserWindow, screen }) => {
  if (!globalThis.__diag) {
    const D = (globalThis.__diag = { lag: [], ereignisse: [], metriken: [] });
    const merke = (typ, info = {}) => { if (D.ereignisse.length < 5000) D.ereignisse.push({ t: Date.now(), typ, ...info }); };
    app.on('child-process-gone', (_e, d) => merke('child-process-gone', { prozess: d.type, grund: d.reason, code: d.exitCode, name: d.name || d.serviceName }));
    app.on('render-process-gone', (_e, _wc, d) => merke('render-process-gone', { grund: d.reason, code: d.exitCode }));
    // Chromiums Hang-Erkennung: Fenster und Seite melden „reagiert nicht“ und „reagiert wieder“
    const fenster = (w) => {
      for (const [quelle, o] of [['Fenster', w], ['Seite', w.webContents]]) for (const typ of ['unresponsive', 'responsive']) o.on(typ, () => merke(typ, { quelle }));
    };
    BrowserWindow.getAllWindows().forEach(fenster);
    app.on('browser-window-created', (_e, w) => fenster(w));
    let last = performance.now();
    setInterval(() => { const n = performance.now(), d = n - last - 25; last = n; if (d >= 20 && D.lag.length < 50000) D.lag.push([Date.now(), Math.round(d)]); }, 25);
    setInterval(() => {
      try { if (D.metriken.length < 20000) D.metriken.push([Date.now(), app.getAppMetrics().map((m) => [m.type, m.pid, Math.round(m.memory.workingSetSize / 1024), Math.round(m.cpu.percentCPUUsage)])]); } catch { /* weiter */ }
    }, 1000);
  }
  const d = screen.getPrimaryDisplay();
  return { electron: process.versions.electron, chrome: process.versions.chrome, gepackt: app.isPackaged,
    bildschirm: { breite: d.size.width, hoehe: d.size.height, skalierung: d.scaleFactor, hz: d.displayFrequency } };
};
const HAUPT_ABHOLEN = () => { const D = globalThis.__diag; return D ? { lag: D.lag.splice(0), ereignisse: D.ereignisse.splice(0), metriken: D.metriken.splice(0) } : null; };
const GPU = async ({ app }) => {
  const info = await Promise.race([app.getGPUInfo('basic'), new Promise((r) => setTimeout(() => r(null), 10000))]).catch(() => null);
  const g = info?.gpuDevice?.find((x) => x.active) || info?.gpuDevice?.[0];
  return {
    status: app.getGPUFeatureStatus(), hardware: app.isHardwareAccelerationEnabled?.() ?? null,
    geraet: g ? { vendorId: g.vendorId, deviceId: g.deviceId, name: g.deviceString || g.vendorString || null, treiber: g.driverVersion || g.driverVendor || null } : null,
    modell: info?.machineModelName ? `${info.machineModelName} ${info.machineModelVersion || ''}`.trim() : null,
  };
};
// Fenster: lange Aufgaben, lange Frames (mit Aufteilung Skript/Layout) und die Dauer der wichtigen Funktionen
const SEITE_MESSUNG = (namen) => {
  if (window.__diag) return window.__diag.info;
  const D = (window.__diag = { aufrufe: [], lang: [], loaf: [], info: { gestoppt: [], loaf: false } });
  const t = (x) => Math.round(performance.timeOrigin + x);
  for (const n of namen) {
    const f = window[n];
    if (typeof f !== 'function') continue;
    window[n] = function (...a) {
      const s = performance.now();
      try { return f.apply(this, a); } finally {
        const d = performance.now() - s;
        if ((n === 'setMode' || d >= 2) && D.aufrufe.length < 50000) D.aufrufe.push([n, t(s), Math.round(d * 10) / 10, n === 'setMode' ? String(a[0]) : null]);
      }
    };
    D.info.gestoppt.push(n);
  }
  try { new PerformanceObserver((l) => { for (const e of l.getEntries()) D.lang.push([t(e.startTime), Math.round(e.duration)]); }).observe({ type: 'longtask', buffered: true }); } catch { /* nicht unterstützt */ }
  try {
    if (PerformanceObserver.supportedEntryTypes?.includes('long-animation-frame')) {
      new PerformanceObserver((l) => {
        for (const e of l.getEntries()) {
          if (e.duration < 100) continue;
          const ende = e.startTime + e.duration;
          D.loaf.push({ t: t(e.startTime), ms: Math.round(e.duration), blockiert: Math.round(e.blockingDuration || 0),
            skript: Math.round((e.renderStart || ende) - e.startTime),
            raf: e.renderStart ? Math.round((e.styleAndLayoutStart || ende) - e.renderStart) : 0,
            layout: e.styleAndLayoutStart ? Math.round(ende - e.styleAndLayoutStart) : 0,
            skripte: [...(e.scripts || [])].sort((a, b) => b.duration - a.duration).slice(0, 3)
              .map((s) => `${s.sourceFunctionName || s.invoker || '?'} ${Math.round(s.duration)} ms${s.forcedStyleAndLayoutDuration > 5 ? ` (erzwungenes Layout ${Math.round(s.forcedStyleAndLayoutDuration)} ms)` : ''}`) });
        }
      }).observe({ type: 'long-animation-frame', buffered: true });
      D.info.loaf = true;
    }
  } catch { /* nicht unterstützt */ }
  return D.info;
};
const SEITE_ABHOLEN = () => { const D = window.__diag; return D ? { aufrufe: D.aufrufe.splice(0), lang: D.lang.splice(0), loaf: D.loaf.splice(0) } : null; };
// wartet, bis der Modus gewechselt ist, dann auf zwei Frames; verdeckte Fenster liefern keine Frames, das wird vermerkt
const WARTE_BILD = (m) => new Promise((res) => {
  const t = Date.now();
  const fertig = (x) => res({ t2: Date.now(), modus: S.mode, ...x });
  const bild = () => {
    if (document.hidden) return setTimeout(() => fertig({ verdeckt: true }), 0);
    let erst = 0, done = false;
    const notfall = setTimeout(() => { if (!done) { done = true; fertig({ keinBild: true, t1: erst }); } }, 110000);
    requestAnimationFrame(() => { erst = Date.now(); requestAnimationFrame(() => { if (!done) { done = true; clearTimeout(notfall); fertig({ t1: erst }); } }); });
  };
  const pruefe = () => (S.mode === m ? bild() : Date.now() - t > 110000 ? fertig({ falsch: true }) : setTimeout(pruefe, 2));
  pruefe();
});
const ZUSTAND = () => {
  const cs = [...document.querySelectorAll('canvas')];
  const px = cs.reduce((a, c) => a + c.width * c.height, 0);
  return {
    seiten: S.pages.length, zoomProzent: Math.round(S.scale * 100), dpr: devicePixelRatio, fenster: `${innerWidth}×${innerHeight}`, verdeckt: document.hidden,
    leinwaende: cs.length, leinwandMB: Math.round((px * 4) / 1048576), knoten: document.getElementsByTagName('*').length, bilder: document.images.length,
    gerendert: typeof views !== 'undefined' ? views.filter((v) => v.live).length : null,
    vorschauen: typeof thumbCache !== 'undefined' ? thumbCache.size : null,
    jsHeapMB: performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1048576) : null,
    anmerkungen: Array.isArray(S.ann) ? S.ann.reduce((a, l) => a + (l?.length || 0), 0) : null,
  };
};

/* ---------- Starten und Beenden ---------- */
async function starte(name, args, seiten) {
  const userData = mkdtempSync(join(out, 'profil-'));
  const env = { ...process.env, PDFWERK_TEST: '1', PDFWERK_USER_DATA: userData };
  const sitzung = { name, gpuVorher: null, gpuNachher: null };
  ergebnis.sitzungen.push(sitzung);
  // mit Sandbox wie beim Nutzer; Playwright schaltet sie unter Linux sonst ab
  const app = await _electron.launch({ executablePath: exe || require('electron'), args: appArgs(args), cwd: out, env, timeout: 60000, chromiumSandbox: !sandboxArgs.length });
  let beendet = false;
  const exit = new Promise((r) => app.process().once('exit', () => { beendet = true; r(); }));
  const s = { name, app, exit, beendet: () => beendet, fehler: [], sitzung, haengt: false };
  Object.assign(sitzung, await mitFrist(app.evaluate(HAUPT_MESSUNG), 30000));
  const page = (s.page = await app.firstWindow());
  page.on('console', (m) => { if (m.type() === 'error') s.fehler.push(m.text()); });
  page.on('pageerror', (e) => s.fehler.push(e.message));
  await page.waitForFunction((n) => typeof S !== 'undefined' && !!S.pdf && (n ? S.pages.length === n : S.pages.length > 0), seiten, { timeout: FRIST });
  s.win = await app.browserWindow(page);
  s.winId = await s.win.evaluate((w) => w.id);
  sitzung.seite = await page.evaluate(SEITE_MESSUNG, FUNKTIONEN);
  sitzung.gpuVorher = await mitFrist(app.evaluate(GPU), 20000).catch((e) => ({ fehler: kurz(e) }));
  return s;
}
async function beende(s) {
  if (!s) return;
  s.sitzung.gpuNachher = await mitFrist(s.app.evaluate(GPU), 20000).catch((e) => ({ fehler: kurz(e) }));
  s.sitzung.konsolenfehler = s.fehler.slice(0, 20);
  if (!s.beendet()) {
    // ungespeicherte Anmerkungen: „Nicht speichern“
    await mitFrist(s.app.evaluate(({ dialog }) => { dialog.showMessageBox = async () => ({ response: 1, checkboxChecked: false }); }), 10000).catch(() => {});
    await mitFrist(s.app.evaluate(({ app }) => { app.quit(); }), 10000).catch(() => {});
    await Promise.race([s.exit, sleep(10000)]);
    if (!s.beendet()) s.app.process().kill('SIGKILL');
  }
  await mitFrist(s.app.close(), 10000).catch(() => {});
}

/* ---------- Ein Moduswechsel ---------- */
const TABS = () => Object.fromEntries([...document.querySelectorAll('#modeTabs [data-mode]')].map((b) => {
  const r = b.getBoundingClientRect(), x = r.x + r.width / 2, y = r.y + r.height / 2;
  return [b.dataset.mode, { x, y, trifft: document.elementFromPoint(x, y)?.closest('[data-mode]') === b }];
}));
function ausloesen(s, weg, nach) {
  if (weg === 'Klick') return s.page.mouse.click(s.tabs[nach].x, s.tabs[nach].y);
  // Tastendruck wie vom Betriebssystem, über den Hauptprozess eingespeist (Cmd unter macOS, sonst Strg)
  if (weg === 'Taste') {
    return s.win.evaluate((w, [k, m]) => {
      w.webContents.sendInputEvent({ type: 'keyDown', keyCode: k, modifiers: m });
      w.webContents.sendInputEvent({ type: 'keyUp', keyCode: k, modifiers: m });
    }, [TASTE[nach], [MOD]]);
  }
  // Menü Ansicht › Lesen … Werkzeuge, wie ein Klick im Menü (Klickfunktion des Menüeintrags mit dem Fenster)
  return s.app.evaluate(({ Menu, BrowserWindow }, [id, taste]) => {
    const w = BrowserWindow.fromId(id);
    const ansicht = Menu.getApplicationMenu()?.items.find((i) => i.label.replace(/&/g, '') === 'Ansicht');
    const item = ansicht?.submenu?.items.find((i) => i.accelerator === `CmdOrCtrl+${taste}`);
    if (!item) throw new Error(`Menüeintrag Ansicht › CmdOrCtrl+${taste} fehlt`);
    item.click({}, w, w.webContents);
  }, [s.winId, TASTE[nach]]);
}
async function wechsel(s, weg, von, nach) {
  const w = { weg, von, nach, t0: Date.now() };
  try {
    const r = await mitFrist((async () => { await ausloesen(s, weg, nach); return s.page.evaluate(WARTE_BILD, nach); })(), FRIST);
    w.ms = r.t2 - w.t0;
    w.t1raf = r.t1 || null; w.t2 = r.t2; w.modus = r.modus;
    if (r.verdeckt) w.verdeckt = true;
    if (r.keinBild) w.fehler = 'kein Frame nach 110 s';
    if (r.falsch) w.fehler = `Modus blieb ${MODI[r.modus] || r.modus}`;
  } catch (e) {
    w.ms = Date.now() - w.t0;
    w.fehler = kurz(e);
  }
  return w;
}
// nach einer Zeitüberschreitung: reagiert das Fenster wieder?
async function erholt(s) {
  const t = Date.now();
  const ok = await mitFrist(s.page.evaluate(() => S.mode), FRIST).then(() => true, () => false);
  console.log(ok ? `  Fenster reagiert wieder nach weiteren ${fmt(Date.now() - t)}` : '  Fenster reagiert auch nach weiteren 120 s nicht, Sitzung wird abgebrochen');
  return ok;
}

/* ---------- Szenarien ---------- */
async function anmerkungen(s) {
  const p = s.page;
  await p.evaluate(() => setMode('comment'));
  const n = await p.evaluate(() => Math.min(S.pages.length, 3));
  for (let i = 0; i < n; i++) {
    await p.evaluate((i) => scrollToPage(i, false), i);
    await sleep(500);
    const bx = await p.locator('.page').nth(i).boundingBox();
    const ziehe = async (tool, x1, y1, x2, y2) => {
      await p.evaluate((t) => pickTool(t), tool);
      await p.mouse.move(bx.x + x1, bx.y + y1); await p.mouse.down();
      await p.mouse.move(bx.x + x2, bx.y + y2, { steps: 6 }); await p.mouse.up();
    };
    await ziehe('rect', 60, 60, 220, 140);
    await ziehe('ellipse', 260, 60, 400, 150);
    await ziehe('line', 60, 180, 300, 200);
    await ziehe('arrow', 60, 230, 300, 280);
    await p.evaluate(() => pickTool('ink'));
    await p.mouse.move(bx.x + 80, bx.y + 330); await p.mouse.down();
    for (let k = 0; k < 60; k++) await p.mouse.move(bx.x + 80 + k * 5, bx.y + 330 + Math.sin(k / 4) * 30);
    await p.mouse.up();
    await p.evaluate(() => pickTool('text'));
    await p.mouse.click(bx.x + 80, bx.y + 420);
    await p.keyboard.type(`Diagnose Seite ${i + 1}`);
    await p.evaluate(() => pickTool('select'));
  }
  await p.evaluate(() => { setMode('read'); scrollToPage(0, false); });
  return `${await p.evaluate(() => S.ann.reduce((a, l) => a + l.length, 0))} Anmerkungen gezeichnet`;
}
async function zoom200(s) {
  await s.page.evaluate(() => { setMode('read'); rezoom(2); });
  return `Zoom ${await s.page.evaluate(() => Math.round(S.scale * 100))} %`;
}

async function szenario(s, name, vorbereitung) {
  console.log(`\n== Szenario „${name}“`);
  if (!s || s.haengt) { console.log('  übersprungen: kein reagierendes Fenster'); ergebnis.szenarien.push({ name, uebersprungen: true }); return; }
  const sz = { name, sitzung: s.name, hinweise: [], wechsel: [] };
  ergebnis.szenarien.push(sz);
  try {
    await mitFrist(s.page.evaluate(() => setMode('read')), FRIST);
    if (vorbereitung) {
      const h = await mitFrist(vorbereitung(s), FRIST, 'Vorbereitung dauert über 120 s');
      if (h) { sz.hinweise.push(h); console.log('  ' + h); }
    }
  } catch (e) {
    // trotzdem messen, solange das Fenster reagiert
    sz.hinweise.push('Vorbereitung gescheitert: ' + kurz(e));
    console.log('  Vorbereitung gescheitert: ' + kurz(e));
    if (!(await erholt(s))) { s.haengt = true; return; }
    await mitFrist(s.page.evaluate(() => setMode('read')), FRIST).catch(() => {});
  }
  try {
    await sleep(1500);
    s.tabs = await mitFrist(s.page.evaluate(TABS), FRIST);
    const daneben = Object.keys(MODI).filter((m) => !s.tabs[m]?.trifft);
    if (daneben.length) { sz.hinweise.push(`Reiter verdeckt: ${daneben.join(', ')}`); console.log('  ' + sz.hinweise.at(-1)); }
    sz.vorher = await mitFrist(s.page.evaluate(ZUSTAND), FRIST);
  } catch (e) {
    sz.hinweise.push('Fenster nicht bereit: ' + kurz(e));
    console.log('  Fenster nicht bereit: ' + kurz(e));
    s.haengt = !(await erholt(s));
    return;
  }
  const v = sz.vorher;
  console.log(`  ${v.seiten} Seiten, Zoom ${v.zoomProzent} %, Fenster ${v.fenster}, DPR ${v.dpr}${v.verdeckt ? ', FENSTER VERDECKT' : ''}`);
  sz.start = Date.now();
  let von = 'read';
  lauf: for (let r = 1; r <= RUNDEN; r++) {
    for (const weg of WEGE) {
      const runde = [];
      for (const nach of RUNDE) {
        const w = await wechsel(s, weg, von, nach);
        w.runde = r;
        sz.wechsel.push(w); runde.push(w);
        alle.push({ szenario: name, ...w });
        if (w.ms >= AUFFAELLIG || w.fehler) console.log(`  ${w.ms > GRENZE ? 'EINGEFROREN' : 'langsam'}: ${MODI[von]} → ${MODI[nach]} per ${weg}: ${fmt(w.ms)}${w.fehler ? ` (${w.fehler})` : ''}`);
        if (w.fehler && !w.modus && !(await erholt(s))) { s.haengt = true; break lauf; }
        von = w.modus && MODI[w.modus] ? w.modus : nach;
        await sleep(PAUSEN[pausenZaehler++ % PAUSEN.length]);
      }
      const ms = runde.map((x) => x.ms), l = maxBy(runde, (x) => x.ms);
      console.log(`  Runde ${r}, ${weg.padEnd(5)}: Median ${fmt(median(ms))}, längster ${fmt(l.ms)} (${MODI[l.von]} → ${MODI[l.nach]})`);
    }
  }
  sz.ende = Date.now();
  sz.nachher = await mitFrist(s.page.evaluate(ZUSTAND), FRIST).catch((e) => ({ fehler: kurz(e) }));
  await auswerten(s, sz);
}

/* ---------- Auswertung ---------- */
async function auswerten(s, sz) {
  const seite = await mitFrist(s.page.evaluate(SEITE_ABHOLEN), 30000).catch(() => null) || { aufrufe: [], lang: [], loaf: [] };
  const haupt = await mitFrist(s.app.evaluate(HAUPT_ABHOLEN), 30000).catch(() => null) || { lag: [], ereignisse: [], metriken: [] };
  const im = (t, a, b) => t >= a && t <= b;
  for (const w of sz.wechsel) {
    const a = w.t0, b = w.t0 + w.ms;
    const sm = seite.aufrufe.find(([n, t, , arg]) => n === 'setMode' && arg === w.nach && im(t, a - 5, b));
    if (sm) {
      w.zustellung = Math.max(0, sm[1] - a); w.setMode = sm[2];
      if (w.t2) w.bisBild = Math.max(0, w.t2 - Math.round(sm[1] + sm[2]));
    }
    const lt = seite.lang.filter(([t, d]) => t < b && t + d > a);
    w.langeAufgaben = lt.length;
    w.laengsteAufgabe = lt.length ? Math.max(...lt.map(([, d]) => d)) : 0;
    const lf = maxBy(seite.loaf.filter((x) => x.t < b && x.t + x.ms > a), (x) => x.ms);
    if (lf) w.laengsterFrame = lf;
    const lag = haupt.lag.filter(([t, d]) => t - d < b && t > a);
    w.hauptprozess = lag.length ? Math.max(...lag.map(([, d]) => d)) : 0;
    const ev = haupt.ereignisse.filter((e) => im(e.t, a, b + 1000));
    if (ev.length) w.ereignisse = ev.map((e) => e.typ + (e.quelle ? ` (${e.quelle})` : '') + (e.prozess ? ` ${e.prozess}` : ''));
    const fn = {};
    for (const [n, t, d] of seite.aufrufe) if (im(t, a, b) && n !== 'setMode') fn[n] = (fn[n] || 0) + d;
    w.funktionen = Object.entries(fn).filter(([, d]) => d >= 3).sort((x, y) => y[1] - x[1]).slice(0, 4).map(([n, d]) => `${n} ${Math.round(d)} ms`);
    // auch in der Gesamtliste
    Object.assign(alle.find((x) => x.szenario === sz.name && x.t0 === w.t0) || {}, w);
  }
  const fenster = (t) => im(t, sz.start - 1000, sz.ende + 1000);
  sz.langeAufgaben = seite.lang.filter(([t]) => fenster(t));
  sz.langeFrames = seite.loaf.filter((x) => fenster(x.t));
  sz.hauptprozessVerzoegerung = haupt.lag.filter(([t]) => fenster(t));
  sz.ereignisse = haupt.ereignisse;
  // Speicher und CPU: Summe je Prozessart je Messpunkt, davon die Spitze
  const verlauf = haupt.metriken.filter(([t]) => fenster(t)).map(([t, list]) => {
    const art = {};
    for (const [typ, , mb, cpu] of list) { art[typ] ??= { mb: 0, cpu: 0, n: 0 }; art[typ].mb += mb; art[typ].cpu += cpu; art[typ].n++; }
    return [t, art];
  });
  sz.speicherVerlauf = verlauf.map(([t, art]) => [t, Object.fromEntries(Object.entries(art).map(([k, x]) => [k, x.mb]))]);
  sz.spitze = {};
  for (const [, art] of verlauf) for (const [k, x] of Object.entries(art)) {
    const p = (sz.spitze[k] ??= { mb: 0, cpu: 0, prozesse: 0 });
    p.mb = Math.max(p.mb, x.mb); p.cpu = Math.max(p.cpu, x.cpu); p.prozesse = Math.max(p.prozesse, x.n);
  }
  const ms = sz.wechsel.map((w) => w.ms);
  const l = maxBy(sz.wechsel, (w) => w.ms);
  sz.zusammenfassung = { wechsel: ms.length, medianMs: median(ms), p95Ms: quantil(ms, 0.95), maxMs: l?.ms ?? null, laengster: l ? `${MODI[l.von]} → ${MODI[l.nach]} (${l.weg})` : null,
    ueberGrenze: sz.wechsel.filter((w) => w.ms > GRENZE).length };
}

/* ---------- Bericht ---------- */
function bericht() {
  const sys = ergebnis.system;
  console.log('\n================ Diagnose Moduswechsel ================');
  console.log(`System: ${sys.plattform} ${sys.version} ${sys.arch}, ${sys.kerne} Kerne (${sys.cpu}), ${sys.speicherGB} GB`);
  console.log(`Programm: ${ergebnis.programm}`);
  for (const se of ergebnis.sitzungen) {
    const g = se.gpuVorher, n = se.gpuNachher;
    const bs = se.bildschirm ? `${se.bildschirm.breite}×${se.bildschirm.hoehe} ×${se.bildschirm.skalierung} ${se.bildschirm.hz || '?'} Hz` : '?';
    console.log(`\nSitzung „${se.name}“: Electron ${se.electron || '?'}, Chromium ${se.chrome || '?'}, ${se.gepackt ? 'gepackt' : 'Quellordner'}, Bildschirm ${bs}`);
    if (g?.status) {
      console.log(`  GPU: ${g.geraet ? `${g.geraet.name || ''} vendor 0x${(g.geraet.vendorId || 0).toString(16)} device 0x${(g.geraet.deviceId || 0).toString(16)}${g.geraet.treiber ? ` Treiber ${g.geraet.treiber}` : ''}` : 'unbekannt'}${g.modell ? `, ${g.modell}` : ''}, Hardwarebeschleunigung ${g.hardware ? 'an' : 'aus'}`);
      console.log('  GPU-Status: ' + Object.entries(g.status).map(([k, v]) => `${k}=${v}`).join(', '));
      const geaendert = n?.status ? Object.keys(g.status).filter((k) => g.status[k] !== n.status[k]) : [];
      if (geaendert.length) console.log('  GPU-Status GEÄNDERT bis zum Ende: ' + geaendert.map((k) => `${k} ${g.status[k]} → ${n.status[k]}`).join(', '));
    }
    if (se.seite) console.log(`  gestoppte Funktionen: ${se.seite.gestoppt.length}/${FUNKTIONEN.length}, lange Frames messbar: ${se.seite.loaf ? 'ja' : 'nein'}`);
    if (se.konsolenfehler?.length) console.log(`  Konsolenfehler: ${se.konsolenfehler.join(' | ').slice(0, 500)}`);
  }
  for (const sz of ergebnis.szenarien) {
    console.log(`\nSzenario „${sz.name}“`);
    if (sz.uebersprungen) { console.log('  übersprungen'); continue; }
    sz.hinweise.forEach((h) => console.log('  ' + h));
    const v = sz.vorher, n = sz.nachher;
    if (v) console.log(`  ${v.seiten} Seiten, Zoom ${v.zoomProzent} %, Fenster ${v.fenster}, DPR ${v.dpr}, ${v.anmerkungen ?? '?'} Anmerkungen${v.verdeckt ? ', FENSTER VERDECKT' : ''}`);
    if (!sz.wechsel.length) continue;
    const zeilen = [];
    for (let i = 0; i < RUNDE.length; i++) {
      const von = i ? RUNDE[i - 1] : 'read', nach = RUNDE[i];
      zeilen.push([`${MODI[von]} → ${MODI[nach]}`, ...WEGE.map((weg) => {
        const ws = sz.wechsel.filter((w) => w.weg === weg && w.von === von && w.nach === nach);
        const l = maxBy(ws, (w) => w.ms);
        return l ? fmt(l.ms) + (l.fehler ? ' !' : '') : '–';
      })]);
    }
    tabelle([`Wechsel (längster aus ${RUNDEN} Runde${RUNDEN > 1 ? 'n' : ''})`, ...WEGE], zeilen, new Set([1, 2, 3]));
    const z = sz.zusammenfassung;
    if (z) console.log(`  ${z.wechsel} Wechsel: Median ${fmt(z.medianMs)}, 95 % ${fmt(z.p95Ms)}, längster ${fmt(z.maxMs)} (${z.laengster})${z.ueberGrenze ? `, ${z.ueberGrenze} über ${fmt(GRENZE)}` : ''}`);
    if (sz.langeAufgaben) {
      const la = sz.langeAufgaben.map(([, d]) => d), lf = maxBy(sz.langeFrames, (x) => x.ms), hv = sz.hauptprozessVerzoegerung.map(([, d]) => d);
      console.log(`  Fenster: ${la.length} lange Aufgaben, längste ${fmt(la.length ? Math.max(...la) : 0)}, zusammen ${fmt(la.reduce((a, b) => a + b, 0))}`);
      if (lf) console.log(`  längster Frame ${fmt(lf.ms)} (Skript ${lf.skript}, rAF ${lf.raf}, Stil/Layout ${lf.layout} ms)${lf.skripte.length ? ': ' + lf.skripte.join(', ') : ''}`);
      console.log(`  Hauptprozess: längste Verzögerung ${fmt(hv.length ? Math.max(...hv) : 0)}, ${hv.filter((d) => d >= 100).length}× über 100 ms`);
      const sp = Object.entries(sz.spitze || {});
      if (sp.length) console.log('  Spitze je Prozessart: ' + sp.map(([k, x]) => `${k}${x.prozesse > 1 ? `×${x.prozesse}` : ''} ${x.mb} MB / ${x.cpu} % CPU`).join(', '));
      if (n && !n.fehler) console.log(`  danach: ${n.leinwaende} Leinwände (${n.leinwandMB} MB), ${n.gerendert ?? '?'} Seiten gerendert, ${n.vorschauen ?? '?'} Vorschaubilder, ${n.knoten} DOM-Knoten, JS-Heap ${n.jsHeapMB ?? '?'} MB`);
      if (sz.ereignisse.length) console.log('  Ereignisse: ' + sz.ereignisse.map((e) => `${uhr(e.t)} ${e.typ}${e.quelle ? ` (${e.quelle})` : ''}${e.prozess ? ` ${e.prozess}` : ''}${e.grund ? ` (${e.grund}, Code ${e.code})` : ''}`).join(', '));
    }
  }
  const auff = alle.filter((w) => w.ms >= AUFFAELLIG || w.fehler).sort((a, b) => b.ms - a.ms);
  console.log(`\nAuffällige Wechsel (ab ${fmt(AUFFAELLIG)} oder mit Fehler): ${auff.length ? '' : 'keine'}`);
  if (auff.length) {
    tabelle(['Szenario', 'Weg', 'Wechsel', 'gesamt', 'bis setMode', 'setMode', 'bis Bild', 'längste Aufgabe', 'Hauptprozess', 'Funktionen / Fehler'],
      auff.slice(0, 40).map((w) => [w.szenario, w.weg, `${MODI[w.von]} → ${MODI[w.nach]}`, fmt(w.ms), fmt(w.zustellung), fmt(w.setMode), fmt(w.bisBild),
        fmt(w.laengsteAufgabe), fmt(w.hauptprozess), [w.fehler, w.verdeckt ? 'verdeckt' : null, ...(w.ereignisse || []), ...(w.funktionen || [])].filter(Boolean).join(', ')]),
      new Set([3, 4, 5, 6, 7, 8]));
  }
  const ms = alle.map((w) => w.ms), eingefroren = alle.filter((w) => w.ms > GRENZE);
  ergebnis.gesamt = { wechsel: alle.length, medianMs: median(ms), p95Ms: quantil(ms, 0.95), maxMs: ms.length ? Math.max(...ms) : null, ueberGrenze: eingefroren.length };
  console.log(`\nGesamt: ${alle.length} Wechsel, Median ${fmt(median(ms))}, 95 % ${fmt(quantil(ms, 0.95))}, längster ${fmt(ergebnis.gesamt.maxMs)}`);
  ergebnis.fehler.forEach((f) => console.log('Fehler: ' + f));
  console.log(eingefroren.length ? `EINGEFROREN: ${eingefroren.length} Wechsel dauerten länger als ${fmt(GRENZE)}` : alle.length ? `Kein Wechsel über ${fmt(GRENZE)}` : 'Keine Messung möglich');
}

let beendet = false;
function ende() {
  if (beendet) return;
  beendet = true;
  try { bericht(); } catch (e) { console.log('Bericht gescheitert: ' + kurz(e)); }
  const code = alle.some((w) => w.ms > GRENZE) ? 1 : alle.length ? 0 : 2;
  ergebnis.ende = new Date().toISOString();
  ergebnis.exitCode = code;
  const json = JSON.stringify(ergebnis);
  if (process.env.PDFWERK_DIAG_JSON) try { writeFileSync(resolve(process.env.PDFWERK_DIAG_JSON), json); } catch (e) { console.log('JSON-Datei: ' + kurz(e)); }
  console.log('DIAG-JSON ' + json);
  try { rmSync(out, { recursive: true, force: true, maxRetries: 5 }); } catch { /* Windows: Datei noch gesperrt */ }
  process.exit(code);
}
// hängt alles, trotzdem berichten, bevor die CI den Schritt abbricht
setTimeout(() => { console.log('\nAbbruch: Diagnose läuft schon 18 Minuten'); ergebnis.fehler.push('Gesamtzeit überschritten'); ende(); }, 18 * 60000).unref();

/* ---------- Ablauf ---------- */
console.log(`PDFwerk-Diagnose Moduswechsel: ${RUNDEN} Runde(n) je Weg (Klick, Tastenkürzel, Menü) und Szenario, Programm ${exe || root}`);
async function sitzung(name, args, seiten, szenarien) {
  let s = null;
  try {
    s = await mitFrist(starte(name, args, seiten), 3 * 60000, 'Start dauert über 3 min');
    for (const [titel, vorbereitung] of szenarien) await szenario(s, titel, vorbereitung);
  } catch (e) {
    ergebnis.fehler.push(`Sitzung „${name}“: ${kurz(e)}`);
    console.log(`Sitzung „${name}“ gescheitert: ${kurz(e)}`);
  } finally {
    await beende(s).catch(() => {});
  }
}
await sitzung('Beispiel', [], 0, [
  ['Beispiel', null],
  ['Beispiel mit Anmerkungen', anmerkungen],
  ['Beispiel mit Anmerkungen, Zoom 200 %', zoom200],
]);
const bilder = join(out, 'Bilder-30-Seiten.pdf');
try {
  const t = Date.now();
  const groesse = await makeBilderPdf(bilder);
  console.log(`\nTestdatei mit 30 Seiten und Bildern in Scanauflösung erzeugt: ${(groesse / 1048576).toFixed(1)} MB in ${fmt(Date.now() - t)}`);
  await sitzung('Bilder', [bilder], 30, [
    ['30 Seiten mit großen Bildern', null],
    ['30 Seiten mit großen Bildern, Zoom 200 %', zoom200],
  ]);
} catch (e) {
  ergebnis.fehler.push('Testdatei: ' + kurz(e));
}
ende();
