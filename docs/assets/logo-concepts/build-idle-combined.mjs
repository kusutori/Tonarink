// Combine B's transfer light with C's layered motion; preserve both originals.
// node docs/assets/logo-concepts/build-idle-combined.mjs [official-player-root]
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const read = filename => JSON.parse(readFileSync(join(root, filename), 'utf8'));
const light = read('03-idle-b-transfer-light.lottie.json');
const combined = read('03-idle-c-layered-float.lottie.json');
combined.nm = 'Tonarink idle — layered float and transfer light';
const arrow = doc => doc.layers.find(layer => layer.nm === 'Curved transfer arrow');
const lightFill = arrow(light).shapes[0].it.find(shape => shape.ty === 'gf');
const combinedFill = arrow(combined).shapes[0].it.find(shape => shape.ty === 'gf');
combinedFill.g = structuredClone(lightFill.g);

// Attach the material gradient to the same elastic bridge as the arrow outline.
// The C variant has matching linear sample times in its rear/front positions.
const rear = combined.layers.find(layer => layer.nm === 'Rear aqua panel');
const front = combined.layers.find(layer => layer.nm === 'Front teal panel');
function followBridge(property) {
  const result = structuredClone(rear.ks.p);
  result.k = result.k.map((key, index) => {
    function move(rearPosition, frontPosition) {
      const [x, y] = property.k;
      const weight = Math.max(0, Math.min(1, (x - 142) / 115));
      return [x + rearPosition[0] * (1 - weight) + frontPosition[0] * weight,
        y + rearPosition[1] * (1 - weight) + frontPosition[1] * weight];
    }
    return { ...key, s: move(key.s, front.ks.p.k[index].s),
      ...(key.e ? { e: move(key.e, front.ks.p.k[index].e) } : {}) };
  });
  return result;
}
combinedFill.s = followBridge(combinedFill.s);
combinedFill.e = followBridge(combinedFill.e);

const encoded = JSON.stringify(combined, null, 2) + '\n';
const output = join(root, '03-idle-combined.lottie.json');
writeFileSync(output, encoded);
console.log(output);
if (process.argv[2]) {
  const scene = resolve(process.argv[2], 'public/projects/tonarink-logo-03/scene-6');
  mkdirSync(scene, { recursive: true });
  const target = join(scene, 'lottie.json');
  if (existsSync(target)) JSON.parse(readFileSync(target, 'utf8'));
  writeFileSync(target, encoded);
  console.log(scene);
}
