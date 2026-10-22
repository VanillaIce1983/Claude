// Prüft ein ausgeliefertes Paket (npm run dist:*): Node-Inspector-Argumente per Fuse aus, und das Programm lässt sich
// weder über --inspect noch über das DevTools-Protokoll fernsteuern. Aufruf: node scripts/check-release.mjs <Programmdatei>
// Ohne Bildschirm unter Linux: xvfb-run -a node scripts/check-release.mjs release/linux-unpacked/pdfwerk
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
const require = createRequire(import.meta.url);
// @electron/fuses kommt mit electron-builder
const { getCurrentFuseWire, FuseV1Options } = createRequire(require.resolve('app-builder-lib/package.json'))('@electron/fuses');
const FuseState = { DISABLE: 0x30, ENABLE: 0x31 };   // Zeichen '0' und '1' in der Fuse-Leitung

const exe = process.argv[2] ? resolve(process.argv[2]) : null;
if (!exe) { console.log('Aufruf: node scripts/check-release.mjs <Programmdatei>'); process.exit(2); }
let fails = 0;
const ok = (cond, msg) => { console.log((cond ? 'ok   ' : 'FAIL ') + msg); if (!cond) fails++; };

const wire = await getCurrentFuseWire(exe);
const off = (fuse) => wire[fuse] === FuseState.DISABLE;
ok(off(FuseV1Options.EnableNodeCliInspectArguments), 'Fuse EnableNodeCliInspectArguments aus (kein --inspect, kein SIGUSR1)');
ok(off(FuseV1Options.RunAsNode) && off(FuseV1Options.EnableNodeOptionsEnvironmentVariable), 'Fuses RunAsNode und NODE_OPTIONS aus');
ok(wire[FuseV1Options.OnlyLoadAppFromAsar] === FuseState.ENABLE, 'Fuse OnlyLoadAppFromAsar an');

// Start mit Fernsteuerungs-Schaltern: muss sich sofort beenden, ohne Inspector oder DevTools-Port zu öffnen
const sandbox = process.platform === 'linux' && process.getuid?.() === 0 ? ['--no-sandbox'] : [];
const env = { ...process.env };
delete env.PDFWERK_TEST;
const child = spawn(exe, [...sandbox, '--inspect=0', '--remote-debugging-port=0'], { env, stdio: ['ignore', 'pipe', 'pipe'] });
let log = '';
child.stdout.on('data', (d) => { log += d; });
child.stderr.on('data', (d) => { log += d; });
const code = await new Promise((r) => { child.once('exit', (c) => r(c)); setTimeout(() => { child.kill(); r('läuft noch'); }, 30000); });
ok(code === 1 && !/Debugger listening|DevTools listening/.test(log), `Start mit --inspect und --remote-debugging-port wird abgewiesen (Code ${code})` + (/listening/.test(log) ? ': ' + log.trim().split('\n')[0] : ''));

console.log(fails ? `${fails} Prüfung(en) fehlgeschlagen` : 'Paket in Ordnung');
process.exit(fails ? 1 : 0);
