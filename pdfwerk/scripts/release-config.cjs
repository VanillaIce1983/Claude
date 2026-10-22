// Konfiguration für ausgelieferte Pakete (npm run dist:*): wie "build" in package.json, aber ohne Node-Inspector-Argumente.
// Mit der Fuse EnableNodeCliInspectArguments könnte jedes Programm PDFwerk mit --inspect starten und dessen Rechte nutzen
// (unter macOS z. B. freigegebene Ordner). Testpakete (npx electron-builder --dir) behalten sie, Playwright steuert über --inspect.
const { build } = require('../package.json');

module.exports = { ...build, electronFuses: { ...build.electronFuses, enableNodeCliInspectArguments: false } };
