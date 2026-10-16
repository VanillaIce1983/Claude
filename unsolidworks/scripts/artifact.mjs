// Turns the single-file build (dist/index.html) into page content for hosting
// inside a page skeleton: no doctype/html/head/body wrappers, title and styles
// first, the app markup, then the inlined module script.
import { readFileSync, writeFileSync } from 'node:fs';

const src = readFileSync(new URL('../dist/index.html', import.meta.url), 'utf8');
const pick = (re) => [...src.matchAll(re)].map((m) => m[0]);

const title = pick(/<title>[\s\S]*?<\/title>/g);
const metas = pick(/<meta name="claude:[^>]*>/g);
const links = pick(/<link[^>]*>/g);
const styles = pick(/<style[^>]*>[\s\S]*?<\/style>/g);
const scripts = pick(/<script[^>]*>[\s\S]*?<\/script>/g);
const body = /<body[^>]*>([\s\S]*?)<\/body>/.exec(src)?.[1] ?? '';
const bodyWithoutScripts = body.replace(/<script[^>]*>[\s\S]*?<\/script>/g, '').trim();

const out = [...title, ...metas, ...links, ...styles, bodyWithoutScripts, ...scripts].join('\n');
writeFileSync(new URL('../dist/artifact.html', import.meta.url), out);
console.log(`dist/artifact.html: ${(out.length / 1024).toFixed(0)} kB`);
