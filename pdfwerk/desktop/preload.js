// Brücke zwischen Oberfläche und Hülle: stellt window.pdfwerkHost nach host-api.d.ts bereit.
// Läuft in der Sandbox; nach außen gehen nur die Funktionen unten, nie ipcRenderer selbst.
'use strict';
const { contextBridge, ipcRenderer, webUtils } = require('electron');

const arg = (name) => {
  const pre = `--pdfwerk-${name}=`;
  const a = process.argv.find((x) => x.startsWith(pre));
  return a ? a.slice(pre.length) : '';
};

let onFiles = null, onCommand = null;
const queued = [];
const hostFiles = (list) => (Array.isArray(list) ? list : []).map((f) => ({ name: String(f.name), path: typeof f.path === 'string' ? f.path : null, data: f.data }));
ipcRenderer.on('pdfwerk:open-files', (_e, list) => { if (onFiles) onFiles(hostFiles(list)); else queued.push(list); });
ipcRenderer.on('pdfwerk:command', (_e, cmd) => { if (onCommand && typeof cmd === 'string') onCommand(cmd); });

// Fehler kommen als { error } zurück, damit die deutsche Meldung ohne Electron-Vorspann ankommt
async function call(channel, ...args) {
  const res = await ipcRenderer.invoke(channel, ...args);
  if (res && res.error) throw new Error(res.error);
  return res ? res.result : undefined;
}
const bytes = (data) => {
  if (!(data instanceof Uint8Array)) throw new TypeError('Daten müssen als Uint8Array übergeben werden.');
  return data;
};

contextBridge.exposeInMainWorld('pdfwerkHost', {
  platform: process.platform,
  version: arg('version'),
  ready: async () => hostFiles(await call('pdfwerk:ready')),
  onOpenFiles(cb) {
    if (typeof cb !== 'function') return;
    onFiles = cb;
    for (const list of queued.splice(0)) cb(hostFiles(list));
  },
  openDialog: () => call('pdfwerk:open-dialog'),
  saveFile: async (path, data) => call('pdfwerk:save-file', String(path), bytes(data)),
  saveDialog: async (opts) => call('pdfwerk:save-dialog', {
    defaultName: String(opts?.defaultName || ''),
    title: opts?.title ? String(opts.title) : undefined,
    data: bytes(opts?.data),
  }),
  adoptDroppedFile(file) {
    let p = '';
    try { p = webUtils.getPathForFile(file); } catch { p = ''; }
    if (!p) return null;
    ipcRenderer.send('pdfwerk:adopt', p);
    return p;
  },
  setDocument(state) {
    ipcRenderer.send('pdfwerk:set-document', {
      name: String(state?.name || ''),
      path: typeof state?.path === 'string' ? state.path : null,
      dirty: !!state?.dirty,
      pristine: !!state?.pristine,
    });
  },
  focusOpenFile: (path) => call('pdfwerk:focus-open-file', String(path)),
  printBegin: () => call('pdfwerk:print-begin'),
  printPage: (job, jpeg) => call('pdfwerk:print-page', String(job), bytes(jpeg)),
  printEnd: (job) => call('pdfwerk:print-end', String(job)),
  printAbort(job) { ipcRenderer.send('pdfwerk:print-abort', String(job)); },
  onCommand(cb) { if (typeof cb === 'function') onCommand = cb; },
  close() { ipcRenderer.send('pdfwerk:close'); },
});
