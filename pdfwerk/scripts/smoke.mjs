// Rauchtest: startet PDFwerk.html im Browser und spielt die wichtigsten Abläufe durch.
// Voraussetzung: npm run build, Playwright mit Chromium.
import { mkdtempSync, statSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { ({ chromium } = await import('/opt/node22/lib/node_modules/playwright/index.mjs')); }
const { PDFDocument } = require('pdf-lib');
import { SECRET, FIELD_SECRET, KEEP, makeSecretPdf, findText } from './testdateien.mjs';

const out = mkdtempSync(join(tmpdir(), 'pdfwerk-'));
const page = new URL('../PDFwerk.html', import.meta.url).pathname;
const b = await chromium.launch();
const p = await b.newPage({ viewport: { width: 1400, height: 900 }, acceptDownloads: true });
const errs = [];
p.on('pageerror', (e) => errs.push(e.message));
await p.route(/fonts\.(googleapis|gstatic)/, (r) => r.fulfill({ body: '', contentType: 'text/css' }));
await p.goto('file://' + page);
await p.waitForFunction(() => typeof S !== 'undefined' && S.pages.length === 3);

let fails = 0;
const ok = (cond, msg) => { console.log((cond ? 'ok   ' : 'FAIL ') + msg); if (!cond) fails++; };
const st = (fn) => p.evaluate(fn);
const download = async (action) => { const dl = p.waitForEvent('download'); await action(); const d = await dl; const f = join(out, d.suggestedFilename()); await d.saveAs(f); return f; };
const submit = () => p.click('.backdrop button[type=submit]');
const box = (i) => p.locator('.page').nth(i).boundingBox();

// Zeichnen
await p.click('[data-mode=comment]');
let bx = await box(0);
await p.click('button[title^="Rechteck"]');
await p.mouse.move(bx.x + 100, bx.y + 120); await p.mouse.down(); await p.mouse.move(bx.x + 300, bx.y + 220, { steps: 4 }); await p.mouse.up();
await p.click('button[title^="Freihand"]');
await p.mouse.move(bx.x + 400, bx.y + 150); await p.mouse.down();
for (let k = 0; k < 15; k++) await p.mouse.move(bx.x + 400 + k * 8, bx.y + 150 + (k % 3) * 10);
await p.mouse.up();
await p.click('button[title^="Textfeld"]');
await p.mouse.click(bx.x + 500, bx.y + 300);
await p.keyboard.type('Grüße aus PDFwerk');
await p.click('button[title^="Auswählen"]');
ok((await st(() => S.ann[0].map((a) => a.type).join())) === 'rect,ink,text', 'Rechteck, Freihand und Text angelegt');
await p.keyboard.press('Control+z');
ok((await st(() => S.ann[0].length)) === 2, 'Rückgängig entfernt den Text');
await p.keyboard.press('Control+y');
ok((await st(() => S.ann[0].length)) === 3, 'Wiederholen stellt ihn her');

// Stempel platzieren
await p.click('button[title="Stempel"]');
await p.click('.stamps button >> nth=0');
await p.mouse.click(bx.x + 600, bx.y + 500);
ok((await st(() => S.ann[0].at(-1).type)) === 'image', 'Stempel platziert');

// Markieren per Auswahl
await p.click('[data-mode=read]');
const t = await p.locator('.textLayer span', { hasText: 'Markieren Sie diesen' }).first().boundingBox();
await p.mouse.move(t.x + 2, t.y + t.height / 2); await p.mouse.down(); await p.mouse.move(t.x + 200, t.y + t.height / 2, { steps: 4 }); await p.mouse.up();
await p.click('.selpop button:has-text("Markieren")');
ok((await st(() => S.ann[0].some((a) => a.type === 'highlight'))), 'Textauswahl markiert');

// Seiten
await p.click('[data-mode=pages]');
await p.click('.otile[data-i="2"] button[title="Rechts drehen"]');
await p.waitForFunction(() => S.pages[2].rot === 90);
ok(true, 'Seite 3 gedreht');
await p.click('#ctxBar button:has-text("Leere Seite")');
await p.waitForFunction(() => S.pages.length === 4);
ok(true, 'Leere Seite eingefügt');
await p.click('.otile[data-i="3"] button[title="Löschen"]');
await p.waitForFunction(() => S.pages.length === 3);
ok((await st(() => S.ann[0].length)) === 5, 'Anmerkungen bleiben bei Seitenänderungen erhalten');

// Werkzeuge
const tool = async (name) => { await p.click('[data-mode=tools]'); await p.click(`.tcard:has-text("${name}")`); };
await tool('Seitenzahlen'); await submit(); await p.waitForTimeout(500);
await tool('Formular ausfüllen'); await p.locator('.backdrop label:has-text("E-Mail") input').fill('jana@example.org'); await submit(); await p.waitForTimeout(600);
const zip = await download(async () => { await tool('Teilen'); await submit(); });
ok(zip.endsWith('.zip') && statSync(zip).size > 1000, 'Teilen liefert ZIP');
const txt = await download(() => tool('Text extrahieren'));
ok(readFileSync(txt, 'utf8').includes('Kursplan Herbst'), 'Text extrahiert');

// Speichern und prüfen
const saved = await download(() => p.click('#btnSave'));
const doc = await PDFDocument.load(readFileSync(saved));
ok(doc.getPageCount() === 3, 'Gespeicherte PDF hat 3 Seiten');
ok(doc.getForm().getTextField('E-Mail').getText() === 'jana@example.org', 'Formularwert gespeichert');
ok(doc.getPage(2).getRotation().angle === 90, 'Drehung gespeichert');

// Bilder zu PDF
const png = join(out, 'bild.png');
writeFileSync(png, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP4z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==', 'base64'));
await tool('Bilder zu PDF');
await p.setInputFiles('.backdrop input[type=file]', png);
await submit();
await p.waitForFunction(() => S.pages.length === 1);
ok(true, 'Bild in PDF umgewandelt');

// Komprimieren und Rückgängig: die Anmerkungen kommen zurück
await st(() => addAnn(0, { type: 'rect', x1: 10, y1: 10, x2: 100, y2: 100, color: '#d0312d', width: 2 }));
await tool('Komprimieren'); await submit();
await p.waitForFunction(() => S.ann[0].length === 0 && !document.querySelector('.backdrop'), null, { timeout: 20000 });
await st(() => undo());
ok((await st(() => S.ann[0].length)) === 1, 'Rückgängig nach Komprimieren bringt die Anmerkungen zurück');

// Schwärzen: Text und Formularwert sind danach nirgends mehr in der Datei, auch mit Lesezeichen und Link auf die Seite
const secretFile = join(out, 'geheim.pdf');
writeFileSync(secretFile, await makeSecretPdf());
await p.setInputFiles('#fileInput', secretFile);
await p.click('.backdrop button[type=submit]');   // ungespeicherte Änderungen verwerfen
await p.waitForFunction(() => S.name === 'geheim.pdf' && S.pages.length === 2);
await st(() => addAnn(0, { type: 'redact', x1: 0, y1: 0, x2: S.pages[0].w, y2: S.pages[0].h, rot: 0 }));
const red = readFileSync(await download(() => p.click('#btnSave')));
const leak = [await findText(red, SECRET), await findText(red, FIELD_SECRET)].filter(Boolean);
ok(!leak.length && !!(await findText(red, KEEP)), 'Schwärzen entfernt Text und Feldwert vollständig, Seite 2 bleibt' + (leak.length ? ': ' + leak.join(', ') : ''));
// auch im Seitenbild: das Formularfeld (pdf.js zeichnet es über den Seiteninhalt) liegt unter Schwarz
const bright = await p.evaluate(async (b64) => {
  const pdf = await pdfjsLib.getDocument({ data: Uint8Array.from(atob(b64), (c) => c.charCodeAt(0)) }).promise;
  const c = await renderToCanvas(await pdf.getPage(1), 1);
  const d = c.getContext('2d').getImageData(60, 220, 250, 22).data;   // Feld bei x 60–310, y 600–622 (PDF)
  let max = 0; for (let k = 0; k < d.length; k += 4) max = Math.max(max, d[k], d[k + 1], d[k + 2]);
  await pdf.destroy();
  return max;
}, Buffer.from(red).toString('base64'));
ok(bright < 80, 'geschwärztes Formularfeld ist auch im Seitenbild schwarz (hellster Wert ' + bright + ')');

ok(!errs.length, 'keine Skriptfehler' + (errs.length ? ': ' + errs.join(' | ') : ''));
await b.close();
console.log(fails ? `${fails} Prüfung(en) fehlgeschlagen` : 'Alle Prüfungen bestanden');
process.exit(fails ? 1 : 0);
