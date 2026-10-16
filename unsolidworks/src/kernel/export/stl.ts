// Binary STL of the folded part (mm).

export function meshToStl(positions: Float64Array, indices: Uint32Array, name = 'UnsolidWorks'): ArrayBuffer {
  const triCount = indices.length / 3;
  const buf = new ArrayBuffer(84 + triCount * 50);
  const view = new DataView(buf);
  const header = `${name} - erstellt mit UnsolidWorks`.slice(0, 80);
  for (let i = 0; i < 80; i++) view.setUint8(i, i < header.length ? header.charCodeAt(i) & 0x7f : 0x20);
  view.setUint32(80, triCount, true);
  let o = 84;
  for (let t = 0; t < triCount; t++) {
    const a = indices[3 * t] * 3;
    const b = indices[3 * t + 1] * 3;
    const c = indices[3 * t + 2] * 3;
    const ux = positions[b] - positions[a], uy = positions[b + 1] - positions[a + 1], uz = positions[b + 2] - positions[a + 2];
    const vx = positions[c] - positions[a], vy = positions[c + 1] - positions[a + 1], vz = positions[c + 2] - positions[a + 2];
    let nx = uy * vz - uz * vy;
    let ny = uz * vx - ux * vz;
    let nz = ux * vy - uy * vx;
    const l = Math.hypot(nx, ny, nz) || 1;
    nx /= l;
    ny /= l;
    nz /= l;
    for (const v of [nx, ny, nz]) {
      view.setFloat32(o, v, true);
      o += 4;
    }
    for (const idx of [a, b, c]) {
      view.setFloat32(o, positions[idx], true);
      view.setFloat32(o + 4, positions[idx + 1], true);
      view.setFloat32(o + 8, positions[idx + 2], true);
      o += 12;
    }
    view.setUint16(o, 0, true);
    o += 2;
  }
  return buf;
}
