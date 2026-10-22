// Baut aus index.html und app.js zwei Fassungen:
//  PDFwerk.html  – eine einzige Datei mit allen Bibliotheken, läuft ohne Internet
//  dist/artifact – Seite ohne Dokumentgerüst plus app.js für die gehostete Fassung
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';

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
const inline = (code) => `<script>${code.replace(/<\/script/gi, '<\\/script')}</script>`;

let single = html.replace(/<script src="https:\/\/cdnjs\.cloudflare\.com\/ajax\/libs\/([^"]+)"><\/script>/g, (m, path) => {
  if (!libs[path]) throw new Error('unbekannte Bibliothek ' + path);
  return inline(read(libs[path]));
});
single = single.replace('<script src="app.js"></script>', () => inline(app));
writeFileSync(new URL('PDFwerk.html', root), single);

// Gehostete Fassung: das Gerüst (doctype, html, head, body) setzt der Host selbst
const page = html
  .replace(/<!doctype html>\s*/i, '')
  .replace(/<html[^>]*>\s*|<\/html>\s*/g, '')
  .replace(/<head>\s*|<\/head>\s*|<body>\s*|<\/body>\s*/g, '')
  .replace(/<meta charset="utf-8">\s*/, '')
  .replace(/<meta name="viewport"[^>]*>\s*/, '');
mkdirSync(new URL('dist/artifact/', root), { recursive: true });
writeFileSync(new URL('dist/artifact/index.html', root), page);
writeFileSync(new URL('dist/artifact/app.js', root), app);
console.log(`PDFwerk.html: ${(single.length / 1024).toFixed(0)} kB, dist/artifact/index.html: ${(page.length / 1024).toFixed(0)} kB`);
