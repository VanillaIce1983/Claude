// Prüft vor npm ci, dass jedes Paket in package-lock.json eine Prüfsumme (integrity) hat.
// Ohne sie installiert npm ci, was die Registry gerade liefert, ohne Abgleich mit einem festen Wert.
import { readFileSync } from 'node:fs';

const lock = JSON.parse(readFileSync(new URL('../package-lock.json', import.meta.url), 'utf8'));
const missing = Object.entries(lock.packages).filter(([k, v]) => k && !v.link && !v.integrity).map(([k]) => k);
if (missing.length) {
  console.log(`::error::package-lock.json: ${missing.length} Paket(e) ohne integrity: ${missing.join(', ')}`);
  process.exit(1);
}
console.log(`package-lock.json: alle ${Object.keys(lock.packages).length - 1} Pakete mit Prüfsumme`);
