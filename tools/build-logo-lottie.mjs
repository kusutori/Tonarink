// Promote the approved splash and B+C idle artwork to native app resources.
// node tools/build-logo-lottie.mjs [official-player-root]
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const designs = join(root, 'docs/assets/logo-concepts');
const output = join(root, 'src/Tonarink.App/Assets/Lottie');
for (const [input, name, sceneNumber] of [
  ['03-splash.lottie.json', 'SplashLogo', 1],
  ['03-idle-combined.lottie.json', 'IdleLogo', 2],
]) {
  const doc = JSON.parse(readFileSync(join(designs, input), 'utf8'));
  // Remove excess concept-sheet padding, matching AppIcon.svg's 384px canvas.
  doc.w = doc.h = 384;
  doc.nm = `Tonarink ${name}`;
  for (const [index, layer] of doc.layers.entries()) {
    if (layer.parent !== undefined) throw new Error('Expected independent logo layers.');
    // LottieGen resolves stacking and adjacent track mattes by layer index.
    // Keep those indices aligned with the exported top-to-bottom array order.
    layer.ind = index + 1;
    const move = position => position.map((value, index) => index < 2 ? value - 64 : value);
    const position = layer.ks.p;
    if (position.a === 0) position.k = move(position.k);
    else for (const key of position.k) {
      key.s = move(key.s);
      if (key.e) key.e = move(key.e);
    }
  }
  const encoded = JSON.stringify(doc) + '\n';
  const target = join(output, `${name}.json`);
  writeFileSync(target, encoded);
  console.log(target);
  if (process.argv[2]) {
    const scene = resolve(process.argv[2], `public/projects/tonarink-app-branding/scene-${sceneNumber}`);
    mkdirSync(scene, { recursive: true });
    const sceneFile = join(scene, 'lottie.json');
    if (existsSync(sceneFile)) JSON.parse(readFileSync(sceneFile, 'utf8'));
    writeFileSync(sceneFile, encoded);
  }
}
