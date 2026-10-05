// Render with the official player's CanvasKit/Skottie, without browser UI.
// node tools/render-lottie-keyframes.cjs <player-root> <json> <output-dir> [frames] [size]
const fs = require('node:fs');
const path = require('node:path');

async function main() {
  const [playerRoot, input, output, framesArg, sizeArg] = process.argv.slice(2);
  if (!playerRoot || !input || !output) throw new Error('Expected player root, JSON and output directory');
  const kitRoot = path.resolve(playerRoot, 'node_modules/canvaskit-wasm/bin/full');
  const CanvasKit = await require(path.join(kitRoot, 'canvaskit.js'))({
    locateFile: file => path.join(kitRoot, file),
  });
  const json = fs.readFileSync(input, 'utf8');
  const doc = JSON.parse(json);
  const animation = CanvasKit.MakeManagedAnimation(json);
  if (!animation) throw new Error('Skottie could not load the animation');
  const size = Number(sizeArg || 480);
  const frames = framesArg ? framesArg.split(',').map(Number)
    : Array.from({ length: doc.op - doc.ip }, (_, i) => doc.ip + i);
  const surface = CanvasKit.MakeSurface(size, size);
  if (!surface) throw new Error('Skia surface creation failed');
  const canvas = surface.getCanvas();
  fs.mkdirSync(output, { recursive: true });
  for (const frame of frames) {
    canvas.clear(CanvasKit.TRANSPARENT);
    animation.seekFrame(frame);
    animation.render(canvas, CanvasKit.LTRBRect(0, 0, size, size));
    surface.flush();
    const image = surface.makeImageSnapshot();
    const bytes = image.encodeToBytes();
    fs.writeFileSync(path.join(output, `frame-${String(frame).padStart(2, '0')}.png`), bytes);
    image.delete();
  }
  surface.delete();
  animation.delete();
  console.log(`Rendered ${frames.length} Skottie frames at ${size}px to ${output}`);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
