// Adds a railway peaked cap (navy crown, band + gold cord, black visor, gold winged-wheel badge)
// to TrainDriverFemale.vrm as a rigid mesh under the Head bone. Base file is never modified.
const fs = require('fs'), path = require('path');
const { parse, accessor, worlds } = require('./glb');

const SRC = process.argv[2] || 'base.vrm';
const OUT = process.argv[3] || 'out/TrainDriverFemale_Cap.vrm';
// optional JSON: { size: 1 (0.9 = 10% smaller), crown: [r,g,b] linear, crownShade, band, bandShade }
const OPT = Object.assign({
  size: 1,
  crown: [0.07, 0.09, 0.2], crownShade: [0.03, 0.035, 0.09],
  band: [0.03, 0.03, 0.05], bandShade: [0.01, 0.01, 0.02],
}, process.argv[4] ? JSON.parse(process.argv[4]) : {});
const S = OPT.size;

const g = parse(SRC), J = g.json;
const { W } = worlds(J);
const HEAD = J.extensions.VRMC_vrm.humanoid.humanBones.head.node;
const HW = W[HEAD], HT = [HW[12], HW[13], HW[14]];
const toLocal = p => { const d = [p[0] - HT[0], p[1] - HT[1], p[2] - HT[2]]; return [0, 1, 2].map(i => HW[i * 4] * d[0] + HW[i * 4 + 1] * d[1] + HW[i * 4 + 2] * d[2]); };
const toLocalDir = n => [0, 1, 2].map(i => HW[i * 4] * n[0] + HW[i * 4 + 1] * n[1] + HW[i * 4 + 2] * n[2]);

// ---------------- tunables (world, glTF: +Y up, +Z = face front) ----------------
const O = [0, 1.605, -0.034];      // cap frame origin = band bottom centre
const TILT = 9 * Math.PI / 180;    // front tipped down
// size < 1: band can't shrink below the head, so it tightens clearance and shrinks height / flare / visor instead
const BAND_H = 0.036 * S, CLEAR = 0.006 * Math.min(1, S * S), NA = 64;
const TOP_SCALE = 1 + 0.13 * S * S, TOP_FWD = 0.012 * S, MIN_TOP_H = 0.088 * S;
const VISOR_LEN = 0.062 * S, VISOR_DROP = 18 * Math.PI / 180, VISOR_SPAN = 72 * Math.PI / 180, VISOR_T = 0.0035;

// cap frame: local (x, h, z) -> world
const ca = Math.cos(TILT), sa = Math.sin(TILT);
const W2 = (x, h, z) => [O[0] + x, O[1] + h * ca - z * sa, O[2] + h * sa + z * ca];
const toCap = p => { const x = p[0] - O[0], y = p[1] - O[1], z = p[2] - O[2]; return [x, y * ca + z * sa, -y * sa + z * ca]; };
const D2 = (x, h, z) => [x, h * ca - z * sa, h * sa + z * ca];

// ---------------- head + hair envelope ----------------
const pts = [];
for (let mi = 0; mi < J.meshes.length; mi++) for (const p of J.meshes[mi].primitives) {
  const P = accessor(g, p.attributes.POSITION);
  for (let i = 0; i < P.length; i += 3) if (P[i + 1] > 1.5) pts.push(toCap([P[i], P[i + 1], P[i + 2]]));
}
const wrap = i => (i + NA) % NA;
const ang = k => k / NA * Math.PI * 2;      // 0 = +x, PI/2 = front (+z)
function envelope(h0, h1) {
  const r = new Array(NA).fill(0);
  for (const q of pts) {
    if (q[1] < h0 || q[1] > h1) continue;
    const k = wrap(Math.round(Math.atan2(q[2], q[0]) / (Math.PI * 2) * NA));
    r[k] = Math.max(r[k], Math.hypot(q[0], q[2]));
  }
  // fill empty bins, median then mean smoothing so the band is a clean oval
  for (let k = 0; k < NA; k++) if (!r[k]) r[k] = Math.max(r[wrap(k - 1)], r[wrap(k + 1)]);
  const md = r.map((_, i) => [-2, -1, 0, 1, 2].map(o => r[wrap(i + o)]).sort((a, b) => a - b)[2]);
  return md.map((_, i) => Math.max(md[i], [-3, -2, -1, 0, 1, 2, 3].reduce((s, o) => s + md[wrap(i + o)], 0) / 7));
}
const IR = envelope(-0.004, BAND_H + 0.008).map(v => v + CLEAR);   // band inner radius per angle
const BT = 0.004;                                                     // band thickness
const RB = IR.map(v => v + BT);                                       // band outer
let hairTop = 0;
for (const q of pts) if (Math.hypot(q[0], q[2]) < 0.09) hairTop = Math.max(hairTop, q[1]);
const TOP_H = Math.max(MIN_TOP_H, hairTop + 0.014 * S);
console.log('band inner r cm min/max', (Math.min(...IR) * 100).toFixed(1), (Math.max(...IR) * 100).toFixed(1), ' hair top (cap h) cm', (hairTop * 100).toFixed(1), ' top h', (TOP_H * 100).toFixed(1));

// ---------------- mesh helpers ----------------
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]], add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]], mul = (a, s) => [a[0] * s, a[1] * s, a[2] * s];
const cr = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]], dt = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const norm = a => { const l = Math.hypot(...a) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };
function Mesh() { return { pos: [], nrm: [], idx: [] }; }
function vert(m, p, n) { m.pos.push(...toLocal(p)); m.nrm.push(...toLocalDir(n)); return m.pos.length / 3 - 1; }
/** grid of cap-local points f(i,j) (i around/along, j across), smooth normals, faces oriented so normals point away from `inside(p)` */
function grid(m, ni, nj, f, closedI, outwardRef) {
  const P = [];
  for (let i = 0; i <= ni; i++) { P.push([]); for (let j = 0; j <= nj; j++) P[i].push(f(closedI && i === ni ? 0 : i, j)); }
  const N = P.map((row, i) => row.map((p, j) => {
    const ip = Math.min(i + 1, ni), im = Math.max(i - 1, 0), jp = Math.min(j + 1, nj), jm = Math.max(j - 1, 0);
    const a = closedI ? P[(i + 1) % ni][j] : P[ip][j], b = closedI ? P[(i - 1 + ni) % ni][j] : P[im][j];
    let n = norm(cr(sub(a, b), sub(P[i][jp], P[i][jm])));
    if (dt(n, outwardRef(p)) < 0) n = mul(n, -1);
    return n;
  }));
  const id = P.map((row, i) => row.map((p, j) => vert(m, W2(...p), D2(...N[i][j]))));
  for (let i = 0; i < ni; i++) for (let j = 0; j < nj; j++) {
    const a = id[i][j], b = id[i + 1][j], c = id[i + 1][j + 1], d = id[i][j + 1];
    const pa = P[i][j], pb = P[i + 1][j], pc = P[i + 1][j + 1];
    const fn = cr(sub(pb, pa), sub(pc, pa)), want = N[i][j];
    if (dt(fn, want) >= 0) m.idx.push(a, b, c, a, c, d); else m.idx.push(a, c, b, a, d, c);
  }
}
const radial = p => norm([p[0], 0, p[2]]);
const lerp = (a, b, t) => a + (b - a) * t;
const ring = (R, h, k, dz = 0) => { const t = ang(k); return [Math.cos(t) * R, h, Math.sin(t) * R + dz]; };
const Rk = (arr, k) => arr[wrap(k)];

const navy = Mesh(), band = Mesh(), black = Mesh(), gold = Mesh();

// band: outer wall, inner wall, bottom lip
grid(band, NA, 1, (k, j) => ring(Rk(RB, k), j * BAND_H, k), true, radial);
grid(navy, NA, 1, (k, j) => ring(Rk(IR, k), j * BAND_H, k), true, p => mul(radial(p), -1));
grid(band, NA, 1, (k, j) => ring(lerp(Rk(IR, k), Rk(RB, k), j), 0, k), true, () => [0, -1, 0]);

// crown: from band top flaring out to a wider, forward-shifted top edge, then a gently domed top
const CJ = 6;
const crownR = (k, t) => { const s = Math.sin(t * Math.PI * 0.5); return Rk(RB, k) * lerp(1.0, TOP_SCALE, s); };
const crownH = t => lerp(BAND_H, TOP_H - 0.004, t) + Math.sin(t * Math.PI) * 0.004;
grid(navy, NA, CJ, (k, j) => { const t = j / CJ; return ring(crownR(k, t), crownH(t), k, TOP_FWD * Math.sin(t * Math.PI * 0.5)); }, true, radial);
// rolled top edge
grid(navy, NA, 3, (k, j) => { const t = j / 3, R = crownR(k, 1) * lerp(1, 0.94, t); return ring(R, TOP_H - 0.004 + Math.sin(t * Math.PI * 0.5) * 0.006, k, TOP_FWD); }, true, p => norm([p[0], 1.2, p[2] - TOP_FWD]));
// top disc (radial rings to the centre)
grid(navy, NA, 5, (k, j) => { const t = 1 - j / 5, R = crownR(k, 1) * 0.94 * t; return ring(R, TOP_H + 0.002 + (1 - t * t) * 0.003, k, TOP_FWD); }, true, () => [0, 1, 0]);

// visor: crescent in front, attached under the band, sloping down; top + bottom + rim
const VI = 28, VJ = 6;
const visorPt = (i, j, off) => {
  const phi = (i / VI - 0.5) * 2 * VISOR_SPAN, t = Math.PI / 2 + phi;     // around the front
  const kf = t / (Math.PI * 2) * NA, k0 = Math.floor(kf), fr = kf - k0;
  const r0 = lerp(Rk(RB, k0), Rk(RB, k0 + 1), fr) - 0.002;
  const ext = VISOR_LEN * Math.pow(Math.max(0, 1 - Math.pow(phi / VISOR_SPAN, 2)), 0.6) * (j / VJ);
  const r = r0 + ext, h = 0.002 - ext * Math.tan(VISOR_DROP) - Math.pow(j / VJ, 2) * 0.004 + off;
  return [Math.cos(t) * r, h, Math.sin(t) * r];
};
grid(black, VI, VJ, (i, j) => visorPt(i, j, VISOR_T * 0.5), false, () => [0, 1, 0.3]);
grid(black, VI, VJ, (i, j) => visorPt(i, j, -VISOR_T * 0.5), false, () => [0, -1, -0.3]);
grid(black, VI, 1, (i, j) => visorPt(i, VJ, (j - 0.5) * VISOR_T), false, p => norm([p[0], 0, p[2]]));

// gold chin cord across the band front + side buttons
const CORD_SPAN = 62 * Math.PI / 180, CR = 0.0022, CH0 = 0.011;
const cordC = u => { const t = Math.PI / 2 + (u - 0.5) * 2 * CORD_SPAN, kf = t / (Math.PI * 2) * NA, k0 = Math.floor(kf), fr = kf - k0; const R = lerp(Rk(RB, k0), Rk(RB, k0 + 1), fr) + CR; return [Math.cos(t) * R, CH0 + Math.sin(u * Math.PI) * 0.004, Math.sin(t) * R]; };
grid(gold, 32, 8, (i, j) => { const u = i / 32, c = cordC(u), rd = radial(c), b = j / 8 * Math.PI * 2; return add(c, add(mul(rd, Math.cos(b) * CR), [0, Math.sin(b) * CR, 0])); }, false, p => { const u = Math.min(1, Math.max(0, (Math.atan2(p[2], p[0]) - Math.PI / 2) / (2 * CORD_SPAN) + 0.5)); return sub(p, cordC(u)); });
for (const s of [-1, 1]) {
  const c = cordC(s < 0 ? 0 : 1), rd = radial(c);
  grid(gold, 10, 4, (i, j) => { const a = i / 10 * Math.PI * 2, e = j / 4 * Math.PI * 0.5; const up = [0, 1, 0], w = norm(cr(rd, up)); return add(add(c, mul(rd, 0.0015 + Math.sin(e) * 0.003)), add(mul(up, Math.cos(a) * Math.cos(e) * 0.0045), mul(w, Math.sin(a) * Math.cos(e) * 0.0045))); }, true, p => sub(p, c));
}

// badge on the crown front: wheel (ring + hub + spokes) with two wings
{
  const t = Math.PI / 2, hB = BAND_H + 0.022;
  const tt = (hB - BAND_H) / (TOP_H - BAND_H);
  const R = crownR(NA / 4, tt) + 0.0012;
  const C = [Math.cos(t) * R, hB, Math.sin(t) * R + TOP_FWD * Math.sin(tt * Math.PI * 0.5)];
  const nrm = norm([0, -0.25, 1]), right = [1, 0, 0], up = norm(cr(nrm, right));
  const P = (x, y, z) => add(C, add(add(mul(right, x), mul(up, y)), mul(nrm, z)));
  const flat = (pts2, depth) => { // extruded flat polygon fan (convex)
    const n = pts2.length, cx = pts2.reduce((s, p) => s + p[0], 0) / n, cy = pts2.reduce((s, p) => s + p[1], 0) / n;
    const front = pts2.map(p => vert(gold, W2(...P(p[0], p[1], depth)), D2(...nrm))), c0 = vert(gold, W2(...P(cx, cy, depth)), D2(...nrm));
    for (let i = 0; i < n; i++) gold.idx.push(c0, front[(i + 1) % n], front[i]);
    for (let i = 0; i < n; i++) { // side wall
      const a = pts2[i], b = pts2[(i + 1) % n], sn = norm(add(mul(right, b[1] - a[1]), mul(up, a[0] - b[0])));
      const v = [P(a[0], a[1], 0), P(b[0], b[1], 0), P(b[0], b[1], depth), P(a[0], a[1], depth)].map(p => vert(gold, W2(...p), D2(...sn)));
      gold.idx.push(v[0], v[1], v[2], v[0], v[2], v[3]);
    }
  };
  // fix winding of front faces: make sure they face +nrm
  const wheelR = 0.0105, rim = 0.0026, NW = 24;
  for (let i = 0; i < NW; i++) {
    const a0 = i / NW * Math.PI * 2, a1 = (i + 1) / NW * Math.PI * 2;
    flat([[Math.cos(a0) * wheelR, Math.sin(a0) * wheelR], [Math.cos(a0) * (wheelR - rim), Math.sin(a0) * (wheelR - rim)], [Math.cos(a1) * (wheelR - rim), Math.sin(a1) * (wheelR - rim)], [Math.cos(a1) * wheelR, Math.sin(a1) * wheelR]].reverse(), 0.0018);
  }
  const hub = []; for (let i = 0; i < 12; i++) hub.push([Math.cos(i / 12 * Math.PI * 2) * 0.0032, Math.sin(i / 12 * Math.PI * 2) * 0.0032]);
  flat(hub, 0.0022);
  for (let s = 0; s < 6; s++) { const a = s / 6 * Math.PI, c = Math.cos(a), si = Math.sin(a), w = 0.0009, L = wheelR - rim * 0.5; flat([[-c * L - si * w, -si * L + c * w], [-c * L + si * w, -si * L - c * w], [c * L + si * w, si * L - c * w], [c * L - si * w, si * L + c * w]], 0.0014); }
  for (const s of [-1, 1]) { // swept wing made of 3 feathers
    for (let f = 0; f < 3; f++) {
      const y0 = 0.004 - f * 0.0042, len = 0.026 - f * 0.005, x0 = s * (wheelR - 0.001);
      const poly = [[x0, y0 + 0.0018], [x0 + s * len, y0 + 0.0045 - f * 0.001], [x0 + s * (len + 0.003), y0 + 0.001 - f * 0.001], [x0, y0 - 0.0018]];
      flat(s > 0 ? poly.slice().reverse() : poly, 0.0012);
    }
  }
}

// ---------------- check: hair points inside the crown/band ----------------
let poke = 0;
for (const q of pts) {
  if (q[1] < 0.002 || q[1] > TOP_H) continue;
  const k = wrap(Math.round(Math.atan2(q[2], q[0]) / (Math.PI * 2) * NA)), r = Math.hypot(q[0], q[2]);
  const lim = q[1] <= BAND_H ? IR[k] - CLEAR * 0.5 : crownR(k, Math.min(1, (q[1] - BAND_H) / (TOP_H - BAND_H)));
  if (r > lim && r < lim + 0.03) poke++;
}
console.log('head/hair points poking through band/crown:', poke);

// ---------------- write into GLB ----------------
const chunks = [g.bin]; let binLen = g.bin.length;
function addView(data, target) {
  const pad = (4 - (binLen % 4)) % 4; if (pad) { chunks.push(Buffer.alloc(pad)); binLen += pad; }
  const b = Buffer.from(new Uint8Array(data.buffer, data.byteOffset, data.byteLength));
  const bv = { buffer: 0, byteOffset: binLen, byteLength: b.length }; if (target) bv.target = target;
  chunks.push(b); binLen += b.length; J.bufferViews.push(bv); return J.bufferViews.length - 1;
}
function addAcc(data, target, componentType, type, count, minmax) {
  const a = { bufferView: addView(data, target), componentType, type, count }; if (minmax) Object.assign(a, minmax);
  J.accessors.push(a); return J.accessors.length - 1;
}
const mm = (arr, n) => { const mn = Array(n).fill(Infinity), mx = Array(n).fill(-Infinity); for (let i = 0; i < arr.length; i += n) for (let k = 0; k < n; k++) { mn[k] = Math.min(mn[k], arr[i + k]); mx[k] = Math.max(mx[k], arr[i + k]); } return { min: mn, max: mx }; };
function mtoon(name, base, shade, outline, rim, ds = false) {
  return {
    name, pbrMetallicRoughness: { baseColorFactor: [...base, 1], metallicFactor: 0, roughnessFactor: 0.8 },
    emissiveFactor: [0, 0, 0], alphaMode: 'OPAQUE', doubleSided: ds,
    extensions: {
      KHR_materials_unlit: {},
      VRMC_materials_mtoon: {
        specVersion: '1.0', transparentWithZWrite: false, renderQueueOffsetNumber: 0,
        shadeColorFactor: shade, shadingShiftFactor: -0.1, shadingToonyFactor: 0.85, giEqualizationFactor: 0.9,
        matcapFactor: [0, 0, 0], parametricRimColorFactor: rim, rimLightingMixFactor: 0.7, parametricRimFresnelPowerFactor: 4, parametricRimLiftFactor: 0,
        outlineWidthMode: 'worldCoordinates', outlineWidthFactor: 0.0004, outlineColorFactor: outline, outlineLightingMixFactor: 0,
        uvAnimationScrollXSpeedFactor: 0, uvAnimationScrollYSpeedFactor: 0, uvAnimationRotationSpeedFactor: 0,
      },
    },
  };
}
const parts = [
  [navy, mtoon('Cap_Crown', OPT.crown, OPT.crownShade, [0.01, 0.01, 0.03], [0.05, 0.06, 0.12])],
  [band, mtoon('Cap_Band', OPT.band, OPT.bandShade, [0, 0, 0], [0.05, 0.05, 0.08])],
  [black, mtoon('Cap_Visor', [0.02, 0.02, 0.025], [0.005, 0.005, 0.008], [0, 0, 0], [0.25, 0.25, 0.3], true)],
  [gold, mtoon('Cap_Gold', [0.95, 0.72, 0.22], [0.55, 0.36, 0.08], [0.2, 0.12, 0.02], [0.4, 0.3, 0.1], true)],
];
const all = Mesh(), ranges = [];
for (const [m, mat] of parts) { const base = all.pos.length / 3; all.pos.push(...m.pos); all.nrm.push(...m.nrm); ranges.push([m.idx.map(i => i + base), mat]); }
const NV = all.pos.length / 3;
if (NV > 65535) throw new Error('too many verts ' + NV);
const pos = new Float32Array(all.pos), nrm = new Float32Array(all.nrm), uv = new Float32Array(NV * 2);
const attrs = { POSITION: addAcc(pos, 34962, 5126, 'VEC3', NV, mm(pos, 3)), NORMAL: addAcc(nrm, 34962, 5126, 'VEC3', NV), TEXCOORD_0: addAcc(uv, 34962, 5126, 'VEC2', NV) };
const prims = ranges.map(([idx, mat]) => {
  const mi = J.materials.length; J.materials.push(mat);
  const ia = Uint16Array.from(idx);
  return { mode: 4, indices: addAcc(ia, 34963, 5123, 'SCALAR', ia.length), material: mi, attributes: attrs };
});
const meshIdx = J.meshes.length;
J.meshes.push({ name: 'TrainCap', primitives: prims });
const nodeIdx = J.nodes.length;
J.nodes.push({ name: 'TrainCap', mesh: meshIdx });
(J.nodes[HEAD].children = J.nodes[HEAD].children || []).push(nodeIdx);

J.buffers[0].byteLength = binLen;
let jsonStr = JSON.stringify(J); while (Buffer.byteLength(jsonStr) % 4) jsonStr += ' ';
const jsonBuf = Buffer.from(jsonStr, 'utf8');
const binBuf = Buffer.concat(chunks), binOut = Buffer.concat([binBuf, Buffer.alloc((4 - binBuf.length % 4) % 4)]);
const total = 12 + 8 + jsonBuf.length + 8 + binOut.length;
const head = Buffer.alloc(12); head.writeUInt32LE(0x46546C67, 0); head.writeUInt32LE(2, 4); head.writeUInt32LE(total, 8);
const jh = Buffer.alloc(8); jh.writeUInt32LE(jsonBuf.length, 0); jh.writeUInt32LE(0x4E4F534A, 4);
const bh = Buffer.alloc(8); bh.writeUInt32LE(binOut.length, 0); bh.writeUInt32LE(0x004E4942, 4);
fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, Buffer.concat([head, jh, jsonBuf, bh, binOut]));
console.log('wrote', OUT, total, 'bytes; verts', NV, 'tris', ranges.reduce((s, r) => s + r[0].length / 3, 0));
