const fs = require('fs');
function parse(file) {
  const buf = fs.readFileSync(file);
  let off = 12, json, bin;
  while (off < buf.length) {
    const cl = buf.readUInt32LE(off), ct = buf.readUInt32LE(off + 4);
    const data = buf.subarray(off + 8, off + 8 + cl);
    if (ct === 0x4E4F534A) json = JSON.parse(data.toString('utf8'));
    else if (ct === 0x004E4942) bin = data;
    off += 8 + cl;
  }
  return { json, bin };
}
const COMP = { 5120: [Int8Array, 1], 5121: [Uint8Array, 1], 5122: [Int16Array, 2], 5123: [Uint16Array, 2], 5125: [Uint32Array, 4], 5126: [Float32Array, 4] };
const NCOMP = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT2: 4, MAT3: 9, MAT4: 16 };
function accessor(g, i) {
  const a = g.json.accessors[i], bv = g.json.bufferViews[a.bufferView];
  const [T, sz] = COMP[a.componentType], n = NCOMP[a.type];
  const stride = bv.byteStride || n * sz;
  const base = (bv.byteOffset || 0) + (a.byteOffset || 0);
  const out = new T(a.count * n);
  const dv = new DataView(g.bin.buffer, g.bin.byteOffset, g.bin.byteLength);
  for (let k = 0; k < a.count; k++) for (let c = 0; c < n; c++) {
    const p = base + k * stride + c * sz;
    out[k * n + c] = T === Float32Array ? dv.getFloat32(p, true) : T === Uint16Array ? dv.getUint16(p, true) : T === Uint32Array ? dv.getUint32(p, true) : T === Uint8Array ? dv.getUint8(p) : T === Int16Array ? dv.getInt16(p, true) : dv.getInt8(p);
  }
  return out;
}
// node world transforms (translation/rotation/scale only), returns Float64 4x4 col-major arrays
function mat4FromTRS(t = [0, 0, 0], q = [0, 0, 0, 1], s = [1, 1, 1]) {
  const [x, y, z, w] = q, x2 = x + x, y2 = y + y, z2 = z + z;
  const xx = x * x2, xy = x * y2, xz = x * z2, yy = y * y2, yz = y * z2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
  return [(1 - (yy + zz)) * s[0], (xy + wz) * s[0], (xz - wy) * s[0], 0, (xy - wz) * s[1], (1 - (xx + zz)) * s[1], (yz + wx) * s[1], 0, (xz + wy) * s[2], (yz - wx) * s[2], (1 - (xx + yy)) * s[2], 0, t[0], t[1], t[2], 1];
}
function mul(a, b) { const o = new Array(16).fill(0); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) { let s = 0; for (let k = 0; k < 4; k++) s += a[k * 4 + r] * b[c * 4 + k]; o[c * 4 + r] = s; } return o; }
function worlds(json) {
  const W = new Array(json.nodes.length), parent = new Array(json.nodes.length).fill(-1);
  json.nodes.forEach((n, i) => (n.children || []).forEach(c => parent[c] = i));
  function get(i) { if (W[i]) return W[i]; const n = json.nodes[i]; const l = n.matrix || mat4FromTRS(n.translation, n.rotation, n.scale); return W[i] = parent[i] < 0 ? l : mul(get(parent[i]), l); }
  json.nodes.forEach((_, i) => get(i));
  return { W, parent };
}
module.exports = { parse, accessor, worlds, mat4FromTRS, mul };
