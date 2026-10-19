// Rebuild concept 03 directly from its SVG, keeping the resting geometry exact.
// node docs/assets/logo-concepts/build-lottie.mjs [official-player-root]
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const svg = readFileSync(join(root, '03-overlapping-panels.svg'), 'utf8');
const offset = [-1006, -4];
const duration = 120;
const fixed = k => ({ a: 0, k });
const rounded = n => Math.round(n * 10000) / 10000;
const attrs = text => Object.fromEntries([...text.matchAll(/([\w-]+)="([^"]*)"/g)].map(m => [m[1], m[2]]));
const point = (x, y) => [Number(x) + offset[0], Number(y) + offset[1]];
const rgb = hex => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255);

// This source uses only absolute M/L/C/V/Z. Reject other commands rather than
// silently approximating artwork when a future SVG revision changes its grammar.
function parsePath(data) {
  const tokens = data.match(/[A-Za-z]|[-+]?(?:\d*\.)?\d+(?:e[-+]?\d+)?/g);
  const result = { v: [], i: [], o: [], c: false };
  let cursor = 0;
  const number = () => Number(tokens[cursor++]);
  const vertex = p => {
    result.v.push(p);
    result.i.push([0, 0]);
    result.o.push([0, 0]);
  };
  while (cursor < tokens.length) {
    const command = tokens[cursor++];
    if (command === 'M' || command === 'L') vertex(point(number(), number()));
    else if (command === 'V') vertex([result.v.at(-1)[0], number() + offset[1]]);
    else if (command === 'C') {
      const from = result.v.at(-1);
      const c1 = point(number(), number());
      const c2 = point(number(), number());
      const to = point(number(), number());
      result.o[result.o.length - 1] = c1.map((v, i) => v - from[i]);
      vertex(to);
      result.i[result.i.length - 1] = c2.map((v, i) => v - to[i]);
    } else if (command === 'Z') result.c = true;
    else throw new Error(`Unsupported SVG command: ${command}`);
  }
  // Closed paths may explicitly end on their first vertex; merge those nodes
  // so the closing curve retains its incoming tangent and has no zero segment.
  if (result.c && result.v.at(-1).every((v, i) => v === result.v[0][i])) {
    result.i[0] = result.i.pop();
    result.v.pop();
    result.o.pop();
  }
  return result;
}

const gradients = new Map([...svg.matchAll(/<linearGradient\b([^>]*)>([\s\S]*?)<\/linearGradient>/g)].map(match => {
  const a = attrs(match[1]);
  const stops = [...match[2].matchAll(/<stop\b([^>]*)\/>/g)].map(m => attrs(m[1]));
  return [a.id, { start: point(a.x1, a.y1), end: point(a.x2, a.y2), stops }];
}));
const paths = [...svg.matchAll(/<path\b([^>]*)\/>/g)].map(m => attrs(m[1]));

function gradientStyle(id, stroke = false) {
  const gradient = gradients.get(id);
  const colors = gradient.stops.flatMap(s => [Number(s.offset ?? 0), ...rgb(s['stop-color'])]);
  const opacity = gradient.stops.flatMap(s => [Number(s.offset ?? 0), Number(s['stop-opacity'] ?? 1)]);
  return {
    ty: stroke ? 'gs' : 'gf', nm: id, t: 1, o: fixed(100),
    s: fixed(gradient.start), e: fixed(gradient.end),
    g: { p: gradient.stops.length, k: fixed([...colors, ...opacity]) },
    ...(stroke ? { w: fixed(1.2), lc: 1, lj: 1, ml: 4 } : { r: 1 }),
  };
}

const groupTransform = { ty: 'tr', p: fixed([0, 0]), a: fixed([0, 0]), s: fixed([100, 100]), r: fixed(0), o: fixed(100), sk: fixed(0), sa: fixed(0) };
function group(source, id, pathProperty) {
  return { ty: 'gr', nm: id, it: [
    { ty: 'sh', nm: `${id} contour`, ks: pathProperty ?? fixed(parsePath(source.d)) },
    gradientStyle(id, !!source.stroke), structuredClone(groupTransform),
  ] };
}

const curves = {
  travel: [1, 0.49, 0, 0.55],
  send: [0.2, 0.75, 0.34, 0.94],
  settle: [0, 0.65, 0.51, 0.99],
};
function animated(values) {
  return { a: 1, k: values.map(([t, value, curve = 'settle'], index) => {
    const c = curves[curve];
    const next = values[index + 1];
    return { t, s: value, ...(next ? {
      e: next[1], o: { x: [c[0]], y: [c[1]] }, i: { x: [c[2]], y: [c[3]] },
    } : {}) };
  }) };
}

const arrowBase = parsePath(paths[2].d);
function arrowPose(shift, bend, lift = 0) {
  const deform = ([x, y]) => {
    const progress = Math.max(0, Math.min(1, (x - 142) / 196));
    return [rounded(x + shift * (0.15 + 0.85 * progress)),
      rounded(y - bend * Math.sin(Math.PI * progress) + lift * progress)];
  };
  const vertices = arrowBase.v.map(deform);
  const tangents = key => arrowBase[key].map((t, index) => {
    const endpoint = deform(t.map((v, axis) => v + arrowBase.v[index][axis]));
    return endpoint.map((v, axis) => rounded(v - vertices[index][axis]));
  });
  return { c: true, v: vertices, i: tangents('i'), o: tangents('o') };
}
const arrowPath = animated([
  [0, [arrowPose(0, 0)], 'travel'], [8, [arrowPose(0, 0)], 'travel'],
  [18, [arrowPose(-6, -4)], 'send'], [38, [arrowPose(18, 11, -3)], 'settle'],
  [58, [arrowPose(4, -2)], 'settle'], [80, [arrowPose(0, 0)]],
  [119, [arrowPose(0, 0)]],
]);

function layer(index, name, shapes, positions) {
  return {
    ddd: 0, ind: index, ty: 4, nm: name, sr: 1,
    ks: { o: fixed(100), r: fixed(0), p: positions ?? fixed([0, 0, 0]), a: fixed([0, 0, 0]), s: fixed([100, 100, 100]) },
    ao: 0, ip: 0, op: duration, st: 0, bm: 0, shapes,
  };
}

const document = {
  v: '5.12.2', fr: 60, ip: 0, op: duration, w: 512, h: 512,
  nm: 'Tonarink concept 03 — curved transfer', ddd: 0, assets: [],
  markers: [
    { tm: 0, cm: 'Rest', dr: 8 }, { tm: 8, cm: 'Anticipation', dr: 10 },
    { tm: 18, cm: 'Transfer', dr: 20 }, { tm: 80, cm: 'Settled', dr: 40 },
  ],
  layers: [
    layer(1, 'Curved transfer arrow', [group(paths[2], 'transferArrow', arrowPath)]),
    layer(2, 'Front teal panel', [group(paths[1], 'frontPanel')], animated([
      [0, [0, 0, 0]], [18, [0, 0, 0], 'travel'], [40, [4, 2, 0]],
      [76, [0, 0, 0]], [119, [0, 0, 0]],
    ])),
    layer(3, 'Rear aqua panel', [group(paths[3], 'rearEdge'), group(paths[0], 'rearPanel')], animated([
      [0, [0, 0, 0]], [10, [0, 0, 0], 'travel'], [30, [-4, -2, 0]],
      [68, [0, 0, 0]], [119, [0, 0, 0]],
    ])),
  ],
};

const encoded = JSON.stringify(document, null, 2) + '\n';
const output = join(root, '03-overlapping-panels.lottie.json');
writeFileSync(output, encoded);
console.log(output);

// Splash variant: overlap the curved arrow reveal with the panels' final
// approach, then hold the completed mark briefly. The matte only uncovers the
// original arrow; it never changes its silhouette, gradient or final placement.
const splashDuration = 84;
const splashLastFrame = splashDuration - 1;
const arrowStart = 20;
const arrowComplete = 62;
const splash = structuredClone(document);
splash.nm = 'Tonarink concept 03 — splash assembly';
splash.op = splashDuration;
splash.markers = [
  { tm: 0, cm: 'Empty', dr: 1 },
  { tm: 1, cm: 'PanelsEnter', dr: 39 },
  { tm: arrowStart, cm: 'ArrowDraw', dr: arrowComplete - arrowStart },
  { tm: arrowComplete, cm: 'Complete', dr: splashDuration - arrowComplete },
];
const rear = splash.layers[2];
rear.ks.p = animated([
  [0, [-170, 18, 0], 'send'], [36, [0, 0, 0]], [splashLastFrame, [0, 0, 0]],
]);
rear.ks.o = animated([[0, [0], 'send'], [20, [100]], [splashLastFrame, [100]]]);
const front = splash.layers[1];
front.ks.p = animated([
  [0, [170, -18, 0]], [5, [170, -18, 0], 'send'],
  [40, [0, 0, 0]], [splashLastFrame, [0, 0, 0]],
]);
front.ks.o = animated([[0, [0]], [5, [0], 'send'], [25, [100]], [splashLastFrame, [100]]]);
const arrow = splash.layers[0];
arrow.shapes[0].it[0].ks = fixed(arrowBase);
arrow.tt = 1;
arrow.ks.o = animated([[0, [0]], [arrowStart, [0], 'send'], [arrowStart + 2, [100]], [splashLastFrame, [100]]]);
const sweep = {
  c: false,
  v: [[100, 312], [230, 278], [375, 278]],
  i: [[0, 0], [-50, 0], [-48, 0]],
  o: [[53, 53], [48, 0], [0, 0]],
};
const matte = layer(4, 'Arrow curved reveal — alpha matte', [{
  ty: 'gr', nm: 'Reveal follows curved tail', it: [
    { ty: 'sh', nm: 'Curved sweep centreline', ks: fixed(sweep) },
    { ty: 'st', nm: 'Reveal coverage', c: fixed([1, 1, 1, 1]), o: fixed(100), w: fixed(168), lc: 1, lj: 2, ml: 4 },
    { ty: 'tm', nm: 'Tail to arrowhead', s: fixed(0),
      e: animated([[0, [0]], [arrowStart, [0], 'send'], [arrowComplete, [100]], [splashLastFrame, [100]]]),
      o: fixed(0), m: 1 },
    structuredClone(groupTransform),
  ],
}]);
matte.td = 1;
splash.layers = [matte, arrow, front, rear];
// Native LottieGen orders layers by ind before pairing adjacent track mattes.
splash.layers.forEach((layer, index) => {
  layer.ind = index + 1;
  layer.op = splashDuration;
});
const splashEncoded = JSON.stringify(splash, null, 2) + '\n';
const splashOutput = join(root, '03-splash.lottie.json');
writeFileSync(splashOutput, splashEncoded);
console.log(splashOutput);
if (process.argv[2]) {
  const scene = resolve(process.argv[2], 'public/projects/tonarink-logo-03/scene-1');
  mkdirSync(scene, { recursive: true });
  writeFileSync(join(scene, 'lottie.json'), encoded);
  console.log(scene);
  const splashScene = resolve(process.argv[2], 'public/projects/tonarink-logo-03/scene-2');
  mkdirSync(splashScene, { recursive: true });
  writeFileSync(join(splashScene, 'lottie.json'), splashEncoded);
  console.log(splashScene);
}
