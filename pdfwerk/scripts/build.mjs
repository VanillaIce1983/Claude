// Baut aus index.html und app.js drei Fassungen:
//  PDFwerk.html  – eine einzige Datei mit allen Bibliotheken und Schriften, läuft ohne Internet
//  dist/artifact – Seite ohne Dokumentgerüst plus app.js für die gehostete Fassung (Bibliotheken vom CDN)
//  dist/desktop  – Seite, app.js, Bibliotheken und Schriften als Einzeldateien für die Electron-Fassung
import { readFileSync, writeFileSync, mkdirSync, rmSync, copyFileSync } from 'node:fs';

const root = new URL('../', import.meta.url);
const read = (p) => readFileSync(new URL(p, root), 'utf8');
const html = read('index.html');
const app = read('app.js');

const libs = {
  'pdf.js/3.11.174/pdf.min.js': 'node_modules/pdfjs-dist/build/pdf.min.js',
  'pdf.js/3.11.174/pdf.worker.min.js': 'node_modules/pdfjs-dist/build/pdf.worker.min.js',
  'pdf-lib/1.17.1/pdf-lib.min.js': 'node_modules/pdf-lib/dist/pdf-lib.min.js',
  'jszip/3.10.1/jszip.min.js': 'node_modules/jszip/dist/jszip.min.js',
};
const CDN_SCRIPT = /<script src="https:\/\/cdnjs\.cloudflare\.com\/ajax\/libs\/([^"]+)"><\/script>\n?/g;
const FONT_LINK = /<link rel="stylesheet" href="https:\/\/fonts\.googleapis\.com\/[^"]+">/;
const inline = (code) => `<script>${code.replace(/<\/script/gi, '<\\/script')}</script>`;
const libPath = (path) => { if (!libs[path]) throw new Error('unbekannte Bibliothek ' + path); return libs[path]; };

// Schriften aus @fontsource: nur woff2, nur die genutzten Schnitte, lateinisch und lateinisch erweitert
const FONTS = [['ibm-plex-sans', [400, 500, 600]], ['ibm-plex-mono', [400, 500]], ['caveat', [500]]];
function fontFaces() {
  const faces = [];
  for (const [pkg, weights] of FONTS) {
    for (const w of weights) {
      const css = read(`node_modules/@fontsource/${pkg}/${w}.css`);
      for (const block of css.match(/@font-face\s*{[^}]*}/g)) {
        const file = /url\(\.\/files\/([^)]+\.woff2)\)/.exec(block)?.[1];
        if (!file || !/-latin(-ext)?-\d+-normal\.woff2$/.test(file)) continue;
        faces.push({ file, src: `node_modules/@fontsource/${pkg}/files/${file}`, block });
      }
    }
  }
  return faces;
}
const faceCss = (faces, url) => faces.map((f) => f.block.replace(/src:[^;]+;/, `src: url(${url(f)}) format('woff2');`)).join('\n');

// 1. Einzeldatei
const faces = fontFaces();
let single = html.replace(CDN_SCRIPT, (m, path) => inline(read(libPath(path))));
single = single.replace(FONT_LINK, () => `<style>\n${faceCss(faces, (f) => 'data:font/woff2;base64,' + readFileSync(new URL(f.src, root)).toString('base64'))}\n</style>`);
single = single.replace('<script src="app.js"></script>', () => inline(app));
writeFileSync(new URL('PDFwerk.html', root), single);

// 2. Gehostete Fassung: das Gerüst (doctype, html, head, body) setzt der Host selbst
const page = html
  .replace(/<!doctype html>\s*/i, '')
  .replace(/<html[^>]*>\s*|<\/html>\s*/g, '')
  .replace(/<head>\s*|<\/head>\s*|<body>\s*|<\/body>\s*/g, '')
  .replace(/<meta charset="utf-8">\s*/, '')
  .replace(/<meta name="viewport"[^>]*>\s*/, '');
mkdirSync(new URL('dist/artifact/', root), { recursive: true });
writeFileSync(new URL('dist/artifact/index.html', root), page);
writeFileSync(new URL('dist/artifact/app.js', root), app);

// 3. Desktop-Fassung: alles lokal, pdf.js mit echtem Worker
const desk = new URL('dist/desktop/', root);
rmSync(desk, { recursive: true, force: true });
for (const d of ['', 'lib/', 'fonts/']) mkdirSync(new URL(d, desk), { recursive: true });
for (const src of Object.values(libs)) copyFileSync(new URL(src, root), new URL('lib/' + src.split('/').pop(), desk));
for (const f of faces) copyFileSync(new URL(f.src, root), new URL('fonts/' + f.file, desk));
writeFileSync(new URL('fonts/fonts.css', desk), faceCss(faces, (f) => f.file));
let desktop = html
  .replace(FONT_LINK, '<link rel="stylesheet" href="fonts/fonts.css">')
  .replace(CDN_SCRIPT, (m, path) => {
    const file = libPath(path).split('/').pop();
    // Den Worker nicht als Skript laden: pdf.js startet ihn dann als echten Web Worker
    if (file === 'pdf.worker.min.js') return '<script>window.PDFWERK_WORKER_SRC = "lib/pdf.worker.min.js";</script>\n';
    return `<script src="lib/${file}"></script>\n`;
  })
  .replace('<meta name="claude:viewport-fit-sides" content="cover">\n', '');
writeFileSync(new URL('index.html', desk), desktop);
writeFileSync(new URL('app.js', desk), app);

console.log(`PDFwerk.html: ${(single.length / 1024).toFixed(0)} kB · dist/artifact: ${(page.length / 1024).toFixed(0)} kB · dist/desktop: ${faces.length} Schriftdateien`);
