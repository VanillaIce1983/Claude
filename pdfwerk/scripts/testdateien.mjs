// Testdateien für scripts/smoke.mjs und scripts/smoke-desktop.mjs: geheimer Inhalt zum Schwärzen,
// verschlüsselte PDFs, Notizen mit Antworten, ein langes Dokument samt Prüfung gegen das Einfrieren,
// und eine Suche nach Text in allen Objekten einer Datei.
import { createHash, randomBytes } from 'node:crypto';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { PDFDocument, StandardFonts, PDFName, PDFString, PDFHexString, PDFRawStream, decodePDFRawStream } = require('pdf-lib');

export const SECRET = 'Streng geheim 4711';          // Text auf Seite 1
export const FIELD_SECRET = 'Kontonummer 98765';     // Wert eines Formularfelds auf Seite 1
export const KEEP = 'Seite zwei bleibt';             // Text auf Seite 2, der lesbar bleiben muss

// Zwei Seiten; auf Seite 1 Text und Formularfeld, dazu ein Lesezeichen und ein Link (von Seite 2) auf Seite 1
export async function makeSecretPdf() {
  const doc = await PDFDocument.create();
  const font = await doc.embedFont(StandardFonts.Helvetica);
  const p1 = doc.addPage([595, 842]), p2 = doc.addPage([595, 842]);
  p1.drawText(SECRET, { x: 60, y: 760, size: 20, font });
  p2.drawText(KEEP, { x: 60, y: 760, size: 20, font });
  const field = doc.getForm().createTextField('Konto');
  field.setText(FIELD_SECRET);
  field.addToPage(p1, { x: 60, y: 600, width: 250, height: 22, font });
  const ctx = doc.context, dest = () => ctx.obj([p1.ref, 'XYZ', null, null, null]);
  const outlines = ctx.nextRef(), item = ctx.nextRef();
  ctx.assign(item, ctx.obj({ Title: PDFHexString.fromText('Kapitel 1'), Parent: outlines, Dest: dest() }));
  ctx.assign(outlines, ctx.obj({ Type: 'Outlines', First: item, Last: item, Count: 1 }));
  doc.catalog.set(PDFName.of('Outlines'), outlines);
  p2.node.addAnnot(ctx.register(ctx.obj({ Type: 'Annot', Subtype: 'Link', Rect: [60, 700, 300, 720], Border: [0, 0, 0], Dest: dest() })));
  return doc.save();
}

// Sucht text als Klartext, als Hex (Latin-1 und UTF-16) im Dateikörper und in allen entpackten Streams. Ergibt die Fundstelle oder null.
export async function findText(bytes, text) {
  const hex = (b) => Buffer.from(b).toString('hex');
  const forms = [text, hex(Buffer.from(text, 'latin1')), hex(Buffer.from(text, 'utf16le').swap16())].map((f) => f.toLowerCase());
  const hit = (s) => { const l = s.toLowerCase(); return forms.some((f) => l.includes(f)); };
  if (hit(Buffer.from(bytes).toString('latin1'))) return 'Dateikörper';
  const doc = await PDFDocument.load(bytes, { ignoreEncryption: true, updateMetadata: false });
  for (const [ref, obj] of doc.context.enumerateIndirectObjects()) {
    if (obj instanceof PDFRawStream) {
      let data = obj.contents;
      try { data = decodePDFRawStream(obj).decode(); } catch { /* Bilddaten u. ä. */ }
      if (hit(Buffer.from(data).toString('latin1')) || hit(obj.dict.toString())) return `Objekt ${ref}`;
    } else if (hit(String(obj))) return `Objekt ${ref}`;
  }
  return null;
}

// Verschlüsselte PDF (Standard-Sicherheit, RC4 40 Bit). Mit leerem userPw öffnet sie ohne Rückfrage, ist aber gegen Ändern geschützt.
const PAD = Buffer.from('28bf4e5e4e758a4164004e56fffa01082e2e00b6d0683e802f0ca9fe6453697a', 'hex');
function rc4(key, data) {
  const s = Array.from({ length: 256 }, (_, i) => i);
  for (let i = 0, j = 0; i < 256; i++) { j = (j + s[i] + key[i % key.length]) & 255; [s[i], s[j]] = [s[j], s[i]]; }
  const out = Buffer.alloc(data.length);
  for (let k = 0, i = 0, j = 0; k < data.length; k++) {
    i = (i + 1) & 255; j = (j + s[i]) & 255; [s[i], s[j]] = [s[j], s[i]];
    out[k] = data[k] ^ s[(s[i] + s[j]) & 255];
  }
  return out;
}
export function makeEncryptedPdf(text, userPw, ownerPw) {
  const md5 = (...b) => createHash('md5').update(Buffer.concat(b)).digest();
  const pad = (pw) => Buffer.concat([Buffer.from(pw, 'latin1'), PAD]).subarray(0, 32);
  const id = randomBytes(16), P = -64, pBytes = Buffer.alloc(4);
  pBytes.writeInt32LE(P);
  const O = rc4(md5(pad(ownerPw)).subarray(0, 5), pad(userPw));
  const key = md5(pad(userPw), O, pBytes, id).subarray(0, 5);
  const U = rc4(key, PAD);
  const objKey = (n) => md5(key, Buffer.from([n & 255, (n >> 8) & 255, (n >> 16) & 255, 0, 0])).subarray(0, 10);
  const content = rc4(objKey(4), Buffer.from(`BT /F1 24 Tf 72 700 Td (${text}) Tj ET`, 'latin1'));
  const objs = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>',
    Buffer.concat([Buffer.from(`<< /Length ${content.length} >>\nstream\n`), content, Buffer.from('\nendstream')]),
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>',
    `<< /Filter /Standard /V 1 /R 2 /O <${O.toString('hex')}> /U <${U.toString('hex')}> /P ${P} >>`,
  ];
  const parts = [Buffer.from('%PDF-1.4\n%\xe2\xe3\xcf\xd3\n', 'latin1')], offsets = [];
  let pos = parts[0].length;
  objs.forEach((o, k) => {
    const b = Buffer.concat([Buffer.from(`${k + 1} 0 obj\n`), Buffer.isBuffer(o) ? o : Buffer.from(o, 'latin1'), Buffer.from('\nendobj\n')]);
    offsets.push(pos); parts.push(b); pos += b.length;
  });
  const xref = `xref\n0 ${objs.length + 1}\n0000000000 65535 f \n` + offsets.map((o) => `${String(o).padStart(10, '0')} 00000 n \n`).join('');
  const trailer = `trailer\n<< /Size ${objs.length + 1} /Root 1 0 R /Encrypt 6 0 R /ID [<${id.toString('hex')}> <${id.toString('hex')}>] >>\nstartxref\n${pos}\n%%EOF\n`;
  return Buffer.concat([...parts, Buffer.from(xref + trailer, 'latin1')]);
}

// Notiz von „Müller“ mit Popup, Antwort von „Schmidt“ und eine Markierung mit eigenem Popup, wie aus Acrobat
export const NOTE = { author: 'Müller', date: 'D:20240101120000Z', text: 'Bitte Paragraph 3 prüfen', reply: 'Schmidt' };
export async function makeCommentedPdf() {
  const doc = await PDFDocument.create();
  const page = doc.addPage([595, 842]);
  page.drawText('Vertragsentwurf', { x: 60, y: 760, size: 20, font: await doc.embedFont(StandardFonts.Helvetica) });
  const ctx = doc.context, [note, pop1, reply, hl, pop2] = [1, 2, 3, 4, 5].map(() => ctx.nextRef());
  ctx.assign(note, ctx.obj({ Type: 'Annot', Subtype: 'Text', Rect: [100, 700, 118, 718], Contents: PDFHexString.fromText(NOTE.text), T: PDFHexString.fromText(NOTE.author),
    M: PDFString.of(NOTE.date), NM: PDFString.of('notiz-1'), Name: 'Comment', F: 4, Popup: pop1, P: page.ref }));
  ctx.assign(pop1, ctx.obj({ Type: 'Annot', Subtype: 'Popup', Rect: [120, 600, 300, 700], Parent: note, Open: false }));
  ctx.assign(reply, ctx.obj({ Type: 'Annot', Subtype: 'Text', Rect: [100, 700, 118, 718], Contents: PDFHexString.fromText('Erledigt'), T: PDFHexString.fromText(NOTE.reply),
    M: PDFString.of('D:20240102120000Z'), IRT: note, RT: 'R', F: 4, P: page.ref }));
  ctx.assign(hl, ctx.obj({ Type: 'Annot', Subtype: 'Highlight', Rect: [60, 750, 260, 785], QuadPoints: [60, 785, 260, 785, 60, 750, 260, 750], C: [1, 1, 0],
    T: PDFHexString.fromText(NOTE.author), Popup: pop2, P: page.ref }));
  ctx.assign(pop2, ctx.obj({ Type: 'Annot', Subtype: 'Popup', Rect: [270, 650, 450, 750], Parent: hl }));
  page.node.set(PDFName.of('Annots'), ctx.obj([note, pop1, reply, hl, pop2]));
  return doc.save();
}
// Liest die Anmerkungen von Seite 1: Typ, Autor, Datum, Antwortziel und ob dieses Ziel (bzw. das Popup) auf der Seite steht
export async function readComments(bytes) {
  const doc = await PDFDocument.load(bytes, { updateMetadata: false });
  const annots = doc.getPage(0).node.Annots()?.asArray() || [];
  const txt = (d, k) => d.lookup(PDFName.of(k))?.decodeText?.() ?? null;
  return annots.map((ref) => {
    const d = doc.context.lookup(ref), irt = d.get(PDFName.of('IRT')), pop = d.get(PDFName.of('Popup'));
    return { ref: String(ref), type: String(d.get(PDFName.of('Subtype'))), author: txt(d, 'T'), date: txt(d, 'M'), text: txt(d, 'Contents'),
      irtOnPage: irt ? annots.includes(irt) : null, popupOnPage: pop ? annots.includes(pop) : null };
  });
}

// Langes Dokument: jede Seite mit einigen Zeilen Text
export async function makeLongPdf(pages = 120) {
  const doc = await PDFDocument.create();
  const font = await doc.embedFont(StandardFonts.Helvetica);
  for (let i = 0; i < pages; i++) {
    const page = doc.addPage([595, 842]);
    for (let y = 780; y > 60; y -= 36) page.drawText(`Seite ${i + 1} Zeile ${y}`, { x: 60, y, size: 14, font });
  }
  return doc.save();
}
// Gegen das Einfrieren (jede besuchte Seite behielt ihre Leinwand, Gigabytes bei langen Dokumenten): ein geöffnetes langes
// Dokument schnell vor- und zurückblättern. Danach dürfen höchstens MAX_LIVE Seiten plus die sichtbaren eine Leinwand haben,
// sichtbare Seiten sind gerendert, und eine weit hinten gezeichnete Anmerkung erscheint beim Zurückblättern wieder.
// p: Playwright-Seite mit PDFwerk, ok(bedingung, text) meldet das Ergebnis.
export async function browseLongPdf(p, ok) {
  await p.evaluate(() => setMode('read'));
  const n = await p.evaluate(() => S.pages.length), far = n - 20;
  const rendered = (i, ms = 20000) => p.waitForFunction((i) => views[i].live && views[i].scale === S.scale && views[i].pdfC.width > 0, i, { timeout: ms }).then(() => true, () => false);
  await p.evaluate((i) => scrollToPage(i, false), far);
  await rendered(far);
  await p.evaluate((i) => addAnn(i, { type: 'rect', x1: 100, y1: 300, x2: 400, y2: 600, color: '#d0312d', width: 6 }), far);
  // in kurzen Abständen springen, vorwärts und zurück: die Beobachter melden dabei oft mehrere Wechsel auf einmal
  await p.evaluate(async (n) => {
    const wait = (ms) => new Promise((r) => setTimeout(r, ms));
    for (let k = 0; k < n; k += 2) { scrollToPage(k, false); await wait(30); }
    for (let k = n - 1; k >= 0; k -= 7) { scrollToPage(k, false); await wait(k % 3 ? 15 : 120); }
    scrollToPage(0, false);
  }, n);
  const settled = await p.waitForFunction(() => views.every((v) => !v.rendering) && views.every((v) => !v.visible || (v.live && v.scale === S.scale && v.pdfC.width > 0)),
    null, { timeout: 20000 }).then(() => true, () => false);
  const s = await p.evaluate((far) => ({
    withCanvas: views.filter((v) => v.pdfC.width * v.pdfC.height > 0 || v.ovC.width * v.ovC.height > 0).length,
    visible: views.filter((v) => v.visible).length, white: views.map((v, i) => (v.visible && !v.pdfC.width ? i + 1 : 0)).filter(Boolean), max: MAX_LIVE,
    farReleased: !views[far].live && views[far].pdfC.width === 0, farText: views[far].tl.textContent.includes(`Seite ${far + 1} Zeile`),
  }), far);
  ok(settled, 'langes Dokument: sichtbare Seiten sind nach dem Durchblättern gerendert' + (settled ? '' : ` (weiß: Seite ${s.white.join(', ')})`));
  ok(s.withCanvas <= s.max + s.visible, `… höchstens ${s.max} + sichtbare Seiten haben eine Leinwand (${s.withCanvas} von ${n}, ${s.visible} sichtbar)`);
  ok(s.farReleased && s.farText, `… die ferne Seite ${far + 1} ist freigegeben, ihre Textebene bleibt zum Markieren und Kopieren`);
  await p.evaluate((i) => scrollToPage(i, false), far);
  const back = await rendered(far) && await p.waitForFunction((i) => {
    const c = views[i].ovC;
    if (!c.width) return false;
    const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
    for (let k = 3; k < d.length; k += 4) if (d[k]) return true;
    return false;
  }, far, { timeout: 10000 }).then(() => true, () => false);
  ok(back, `… die Anmerkung auf Seite ${far + 1} ist nach dem Zurückblättern wieder sichtbar`);
}
