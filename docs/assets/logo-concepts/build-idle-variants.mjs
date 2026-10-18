// Compare three quiet 4-second idle loops without modifying the splash artwork.
// node docs/assets/logo-concepts/build-idle-variants.mjs [official-player-root]
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const source = JSON.parse(readFileSync(join(root, '03-overlapping-panels.lottie.json'), 'utf8'));
const splash = JSON.parse(readFileSync(join(root, '03-splash.lottie.json'), 'utf8'));
const baseArrow = splash.layers.find(layer => layer.nm === 'Curved transfer arrow').shapes[0].it[0].ks.k;
const fixed = k => ({ a: 0, k });
const round = n => Math.round(n * 100000) / 100000;
const clamp = n => Math.max(0, Math.min(1, n));
const last = 239;

function keyframes(entries, linear = false) {
  return { a: 1, k: entries.map(([t, value], index) => ({
    t, s: value,
    ...(index < entries.length - 1 ? {
      e: entries[index + 1][1],
      o: { x: [linear ? 0.33333 : 0.42], y: [linear ? 0.33333 : 0] },
      i: { x: [linear ? 0.66667 : 0.58], y: [linear ? 0.66667 : 1] },
    } : {}),
  })) };
}

function deformArrow(map) {
  const vertices = baseArrow.v.map(map);
  const tangents = key => baseArrow[key].map((tangent, index) => {
    const handle = map(tangent.map((v, axis) => v + baseArrow.v[index][axis]));
    return handle.map((v, axis) => round(v - vertices[index][axis]));
  });
  return { c: true, v: vertices, i: tangents('i'), o: tangents('o') };
}

function base(name) {
  const doc = structuredClone(source);
  doc.nm = name;
  doc.op = 240;
  doc.markers = [{ tm: 0, cm: 'LoopStart', dr: 0 }, { tm: 239, cm: 'LoopEnd', dr: 0 }];
  for (const layer of doc.layers) {
    layer.op = 240;
    layer.ks.p = fixed([0, 0, 0]);
    layer.ks.o = fixed(100);
  }
  doc.layers[0].shapes[0].it[0].ks = fixed(baseArrow);
  return doc;
}

// A: the head and panels stay anchored; only the soft ribbon tail breathes.
const a = base('Tonarink idle A — ribbon breath');
function tailPose(amount) {
  return deformArrow(([x, y]) => {
    const tailWeight = Math.pow(clamp((257 - x) / 115), 0.9);
    return [round(x - amount * 0.2 * tailWeight), round(y + amount * tailWeight)];
  });
}
a.layers[0].shapes[0].it[0].ks = keyframes([
  [0, [tailPose(0)]], [48, [tailPose(9)]], [112, [tailPose(-7)]],
  [172, [tailPose(4)]], [216, [tailPose(0)]], [239, [tailPose(0)]],
]);

// B: a broad, low-contrast material highlight travels along the existing arrow
// gradient. Original alpha stops stay unchanged, including its fading tail.
const b = base('Tonarink idle B — transfer light');
const arrowFill = b.layers[0].shapes[0].it.find(item => item.ty === 'gf');
const originalFill = structuredClone(arrowFill.g);
const colorCount = originalFill.p;
const original = originalFill.k.k;
const sampleCount = 25;
function interpolateStop(position, alpha = false) {
  const stride = alpha ? 2 : 4;
  const start = alpha ? colorCount * 4 : 0;
  const length = alpha ? (original.length - start) / 2 : colorCount;
  let index = 0;
  while (index < length - 2 && position > original[start + (index + 1) * stride]) index++;
  const left = start + index * stride;
  const right = left + stride;
  const t = clamp((position - original[left]) / (original[right] - original[left]));
  return Array.from({ length: stride - 1 }, (_, c) => original[left + 1 + c] * (1 - t) + original[right + 1 + c] * t);
}
function lightGradient(frame) {
  const progress = clamp((frame - 36) / 138);
  const centre = -0.5 + 2.05 * progress;
  const envelope = Math.sin(Math.PI * progress) ** 2;
  const colors = [];
  const opacity = [];
  for (let index = 0; index < sampleCount; index++) {
    const position = index / (sampleCount - 1);
    const light = 0.68 * envelope * Math.exp(-(((position - centre) / 0.24) ** 2));
    const color = interpolateStop(position).map(v => round(v + (1 - v) * light));
    colors.push(position, ...color);
    opacity.push(position, ...interpolateStop(position, true));
  }
  return [...colors, ...opacity];
}
const samples = [...Array.from({ length: 40 }, (_, i) => i * 6), last];
arrowFill.g = { p: sampleCount, k: keyframes(samples.map(t => [t, lightGradient(t)]), true) };

// C: counter-phase panel float with an elastic bridge. Baked coordinates keep
// all motion expression-free, periodic, and usable in native Lottie players.
const c = base('Tonarink idle C — layered float');
const wave = frame => Math.sin(2 * Math.PI * frame / last);
const rearAt = frame => [-4 * wave(frame), -6 * wave(frame), 0].map(round);
const frontAt = frame => [4 * wave(frame), 4 * wave(frame), 0].map(round);
c.layers[2].ks.p = keyframes(samples.map(t => [t, rearAt(t)]), true);
c.layers[1].ks.p = keyframes(samples.map(t => [t, frontAt(t)]), true);
c.layers[0].shapes[0].it[0].ks = keyframes(samples.map(t => {
  const rear = rearAt(t);
  const front = frontAt(t);
  return [t, [deformArrow(([x, y]) => {
    const weight = clamp((x - 142) / 115);
    return [round(x + rear[0] * (1 - weight) + front[0] * weight),
      round(y + rear[1] * (1 - weight) + front[1] * weight)];
  })]];
}), true);

const variants = [
  ['a-ribbon-breath', 3, a], ['b-transfer-light', 4, b], ['c-layered-float', 5, c],
];
for (const [name, sceneNumber, doc] of variants) {
  const encoded = JSON.stringify(doc, null, 2) + '\n';
  const output = join(root, `03-idle-${name}.lottie.json`);
  writeFileSync(output, encoded);
  console.log(output);
  if (process.argv[2]) {
    const scene = resolve(process.argv[2], `public/projects/tonarink-logo-03/scene-${sceneNumber}`);
    mkdirSync(scene, { recursive: true });
    const target = join(scene, 'lottie.json');
    // Respect editor-written state before regenerating a known target scene.
    if (existsSync(target)) JSON.parse(readFileSync(target, 'utf8'));
    writeFileSync(target, encoded);
    console.log(scene);
  }
}
