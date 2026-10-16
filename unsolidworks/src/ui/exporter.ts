// File output that works both standalone (plain browser download) and inside
// a claude.ai artifact (downloads capability; DXF/STL go into a ZIP there,
// because only common file types may be saved from an artifact).

import { makeZip } from '../kernel/export/zip';

interface DownloadsNs {
  save(req: { filename: string; data: string | Blob | ArrayBuffer | ArrayBufferView }): Promise<{ status: string }>;
}
interface ClaudeHost {
  use(name: string): Promise<unknown>;
}

const ARTIFACT_ALLOWED = new Set(['txt', 'json', 'md', 'csv', 'svg', 'png', 'pdf', 'zip', 'html']);

let downloads: Promise<DownloadsNs | null> | null = null;

function host(): ClaudeHost | null {
  const c = (window as unknown as { claude?: ClaudeHost }).claude;
  return c && typeof c.use === 'function' ? c : null;
}

export function initDownloads(): void {
  const c = host();
  downloads = c ? (c.use('downloads') as Promise<DownloadsNs | null>).catch(() => null) : Promise.resolve(null);
}

export type SaveOutcome = 'saved' | 'declined' | 'failed';

function toBytes(data: string | ArrayBuffer | Uint8Array): Uint8Array {
  if (typeof data === 'string') return new TextEncoder().encode(data);
  return data instanceof Uint8Array ? data : new Uint8Array(data);
}

function browserDownload(filename: string, data: string | ArrayBuffer | Uint8Array, mime: string): void {
  const blob = new Blob([toBytes(data)], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 4000);
}

export async function saveFile(filename: string, data: string | ArrayBuffer | Uint8Array, mime: string): Promise<{ outcome: SaveOutcome; filename: string; message?: string }> {
  if (!downloads) initDownloads();
  const ns = await downloads!;
  if (!ns) {
    browserDownload(filename, data, mime);
    return { outcome: 'saved', filename };
  }
  const ext = filename.split('.').pop()?.toLowerCase() ?? '';
  let name = filename;
  let payload: string | Uint8Array = typeof data === 'string' ? data : toBytes(data);
  if (!ARTIFACT_ALLOWED.has(ext)) {
    payload = makeZip([{ name: filename, data: toBytes(data) }]);
    name = `${filename}.zip`;
  }
  try {
    await ns.save({ filename: name, data: payload });
    return { outcome: 'saved', filename: name };
  } catch (e) {
    const code = (e as { code?: string })?.code;
    if (code === 'declined') return { outcome: 'declined', filename: name };
    return { outcome: 'failed', filename: name, message: (e as { message?: string })?.message ?? String(e) };
  }
}

export function safeName(s: string): string {
  return (s || 'Blechteil').replace(/[\\/:*?"<>|]+/g, '_').trim().slice(0, 80) || 'Blechteil';
}
