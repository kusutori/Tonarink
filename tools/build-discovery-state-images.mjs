// Derive static discovery states from the existing SearchingDevices artwork.
// node tools/build-discovery-state-images.mjs
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = JSON.parse(readFileSync(join(root, 'src/Tonarink.App/Assets/Lottie/SearchingDevices.json'), 'utf8'));
const output = join(root, 'src/Tonarink.App/Assets/Illustrations');
mkdirSync(output, { recursive: true });
const monitor = source.layers.find(layer => layer.nm === 'Monitor');
const rows = source.layers.find(layer => layer.nm === 'Rows');
const color = rgba => `#${rgba.slice(0, 3).map(value => Math.round(value * 255).toString(16).padStart(2, '0')).join('')}`;

function rectangle(group, overrides = {}) {
  const shape = group.it.find(item => item.ty === 'rc');
  const fill = group.it.find(item => item.ty === 'fl');
  const stroke = group.it.find(item => item.ty === 'st');
  if (!shape || !fill) throw new Error(`Missing source rectangle: ${group.nm}`);
  const [width, height] = shape.s.k;
  const [cx, cy] = shape.p.k;
  const outline = stroke ? ` stroke="${overrides.stroke || color(stroke.c.k)}" stroke-width="${stroke.w.k}"` : '';
  return `<rect x="${cx - width / 2}" y="${cy - height / 2}" width="${width}" height="${height}" rx="${shape.r.k}" fill="${overrides.fill || color(fill.c.k)}"${outline}/>`;
}

function computer(stopped) {
  return ['Neck', 'Base', 'Screen'].map(name => {
    const group = monitor.shapes.find(item => item.nm === name);
    if (!group) throw new Error(`Missing source group: ${name}`);
    return rectangle(group, stopped
      ? name === 'Screen' ? { fill: '#143c44', stroke: '#57878c' } : { fill: '#35636c' }
      : {});
  }).join('\n  ');
}

function inactiveRows() {
  return rows.shapes.map(group => {
    const dot = group.it.find(item => item.ty === 'el');
    return `<circle cx="${dot.p.k[0]}" cy="${dot.p.k[1]}" r="${dot.s.k[0] / 2}" fill="#14c4c2" opacity="0.45"/>\n  ${rectangle(group, { fill: '#709b9d' })}`;
  }).join('\n  ');
}

const stopped = `${computer(true)}
  <g fill="none" stroke="#a3c5c6" stroke-width="4.5" stroke-linecap="round">
    <path d="M 117 108 A 17 17 0 1 0 139 108"/>
    <path d="M 128 101 V 118"/>
  </g>`;
const faulted = `${computer(false)}
  ${inactiveRows()}
  <circle cx="182" cy="151" r="22" fill="#59343b" stroke="#ff9ba5" stroke-width="3"/>
  <path d="M 174 143 L 190 159 M 190 143 L 174 159" fill="none" stroke="#ffe9eb" stroke-width="4.5" stroke-linecap="round"/>`;

for (const [name, artwork] of [['DevicesStopped', stopped], ['DevicesFaulted', faulted]]) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${source.w}" height="${source.h}" viewBox="0 0 ${source.w} ${source.h}">\n  ${artwork}\n</svg>\n`;
  writeFileSync(join(output, `${name}.svg`), svg);
  console.log(`Generated ${name}.svg from ${source.nm}`);
}
