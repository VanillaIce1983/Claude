// Turns the single-file build (dist/index.html) into a hosted page:
// dist/artifact.html holds the title, styles and markup and loads the app
// from the separate file dist/app.js, so the page itself stays small.
import { readFileSync, writeFileSync } from 'node:fs';

const src = readFileSync(new URL('../dist/index.html', import.meta.url), 'utf8');
const pick = (re) => [...src.matchAll(re)].map((m) => m[0]);

const title = pick(/<title>[\s\S]*?<\/title>/g);
const metas = pick(/<meta name="claude:[^>]*>/g);
const links = pick(/<link[^>]*>/g);
const styles = pick(/<style[^>]*>[\s\S]*?<\/style>/g);
const scripts = [...src.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
const body = /<body[^>]*>([\s\S]*?)<\/body>/.exec(src)?.[1] ?? '';
const bodyWithoutScripts = body.replace(/<script[^>]*>[\s\S]*?<\/script>/g, '').trim();

if (scripts.length !== 1) throw new Error(`expected one inline script, found ${scripts.length}`);
writeFileSync(new URL('../dist/app.js', import.meta.url), scripts[0]);
const out = [...title, ...metas, ...links, ...styles, bodyWithoutScripts, '<script type="module" src="app.js"></script>'].join('\n');
writeFileSync(new URL('../dist/artifact.html', import.meta.url), out);
console.log(`dist/artifact.html: ${(out.length / 1024).toFixed(0)} kB, dist/app.js: ${(scripts[0].length / 1024).toFixed(0)} kB`);
