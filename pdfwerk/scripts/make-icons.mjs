// Rendert build/icon.svg zu build/icon.png (1024 × 1024, transparent). Daraus erzeugt electron-builder .ico (Windows) und .icns (macOS).
// Braucht Playwright mit Chromium: das globale unter /opt/node22 oder ein per npm installiertes „playwright“.
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';

const SIZE = 1024;
const root = new URL('../', import.meta.url);
const svg = readFileSync(new URL('build/icon.svg', root), 'utf8');

async function playwright() {
  try { return await import('/opt/node22/lib/node_modules/playwright/index.mjs'); }
  catch { return createRequire(import.meta.url)('playwright'); }
}

const { chromium } = await playwright();
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: SIZE, height: SIZE }, deviceScaleFactor: 1 });
  await page.setContent(`<!doctype html><html><body style="margin:0;background:transparent">${svg}</body></html>`);
  const png = await page.locator('svg').screenshot({ omitBackground: true });
  writeFileSync(new URL('build/icon.png', root), png);
  console.log(`build/icon.png: ${SIZE} × ${SIZE}, ${(png.length / 1024).toFixed(0)} kB`);
} finally {
  await browser.close();
}
