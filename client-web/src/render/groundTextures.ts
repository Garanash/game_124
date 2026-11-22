import * as THREE from "three";
import { GroundKind } from "../world/biomes";
import { seededRandom } from "../world/geometry";

/** Pixels per side of one tile; the tile covers GROUND_TILE_METERS, so detail stays crisp at the default zoom. */
const TILE_PIXELS = 512;
export const GROUND_TILE_METERS = 7;
export const ROAD_TILE_METERS = 4;

type Rgb = [number, number, number];

interface Painter {
  base: Rgb;
  /** How strongly the low-frequency noise darkens and lightens the base. */
  mottle: number;
  details: (context: CanvasRenderingContext2D, random: () => number) => void;
}

const PAINTERS: Record<GroundKind | "road" | "cobble", (tint: THREE.Color) => Painter> = {
  meadow: (tint) => ({
    base: rgb(tint, 1),
    mottle: 0.22,
    details: (context, random) => {
      strokes(context, random, 5200, 3, 7, [rgb(tint, 0.75), rgb(tint, 1.15), shift(tint, 0.03, 1.25)]);
      dots(context, random, 160, 1.2, 2.2, [[245, 240, 220], [250, 214, 70], [190, 140, 220], [240, 120, 120]]);
    },
  }),
  forest: (tint) => ({
    base: rgb(tint, 0.62),
    mottle: 0.3,
    details: (context, random) => {
      blobs(context, random, 70, 14, 34, [shift(tint, -0.02, 0.5), [86, 70, 44]], 0.35);
      strokes(context, random, 2600, 2, 6, [rgb(tint, 0.55), rgb(tint, 0.85), shift(tint, 0.05, 0.9)]);
      leaves(context, random, 520, [[150, 92, 40], [176, 128, 52], [110, 70, 36], [120, 140, 60]]);
      twigs(context, random, 60);
    },
  }),
  rocky: (tint) => ({
    base: [118, 116, 108],
    mottle: 0.24,
    details: (context, random) => {
      blobs(context, random, 46, 16, 40, [rgb(tint, 0.7), [96, 92, 86]], 0.35);
      pebbles(context, random, 900, 1.5, 6, [[150, 146, 138], [128, 124, 118], [170, 166, 156], [106, 100, 94]]);
      cracks(context, random, 26);
      strokes(context, random, 900, 2, 5, [rgb(tint, 0.8), rgb(tint, 1)]);
    },
  }),
  sand: (tint) => ({
    base: rgb(tint, 1),
    mottle: 0.12,
    details: (context, random) => {
      ripples(context, random, 40, rgb(tint, 0.88));
      dots(context, random, 1800, 0.6, 1.3, [rgb(tint, 0.8), rgb(tint, 1.15)]);
      pebbles(context, random, 50, 1.5, 3, [[170, 150, 120], [150, 130, 100]]);
    },
  }),
  ash: (tint) => ({
    base: rgb(tint, 0.85),
    mottle: 0.3,
    details: (context, random) => {
      blobs(context, random, 50, 10, 30, [[52, 46, 44], [80, 72, 64]], 0.4);
      cracks(context, random, 40);
      dots(context, random, 70, 0.8, 1.6, [[230, 110, 40], [250, 160, 60]]);
      strokes(context, random, 500, 2, 4, [[78, 84, 52], [64, 66, 46]]);
    },
  }),
  road: (tint) => ({
    base: rgb(tint, 1),
    mottle: 0.2,
    details: (context, random) => {
      ruts(context, rgb(tint, 0.78));
      pebbles(context, random, 700, 1.2, 4.5, [rgb(tint, 1.2), rgb(tint, 0.8), [140, 134, 124]]);
    },
  }),
  cobble: (tint) => ({
    base: rgb(tint, 0.55),
    mottle: 0.1,
    details: (context, random) => cobbles(context, random, tint),
  }),
};

const cache = new Map<string, THREE.CanvasTexture>();

/** A seamless painted tile, cached per kind and tint because towns and zones share them. */
export function groundTexture(kind: GroundKind | "road" | "cobble", tint: number): THREE.CanvasTexture {
  const key = `${kind}:${tint}`;
  let texture = cache.get(key);
  if (!texture) {
    texture = new THREE.CanvasTexture(paint(PAINTERS[kind](new THREE.Color(tint)), tint));
    texture.wrapS = THREE.RepeatWrapping;
    texture.wrapT = THREE.RepeatWrapping;
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.anisotropy = 8;
    cache.set(key, texture);
  }
  return texture;
}

function paint(painter: Painter, seed: number): HTMLCanvasElement {
  const canvas = document.createElement("canvas");
  canvas.width = TILE_PIXELS;
  canvas.height = TILE_PIXELS;
  const context = canvas.getContext("2d")!;
  const image = context.createImageData(TILE_PIXELS, TILE_PIXELS);
  for (let y = 0; y < TILE_PIXELS; y++) {
    for (let x = 0; x < TILE_PIXELS; x++) {
      const shade = 1 + (periodicFbm(x / TILE_PIXELS, y / TILE_PIXELS, seed) - 0.5) * 2 * painter.mottle;
      const offset = (y * TILE_PIXELS + x) * 4;
      image.data[offset] = clamp255(painter.base[0] * shade);
      image.data[offset + 1] = clamp255(painter.base[1] * shade);
      image.data[offset + 2] = clamp255(painter.base[2] * shade);
      image.data[offset + 3] = 255;
    }
  }
  context.putImageData(image, 0, 0);
  painter.details(context, seededRandom(seed ^ 0x5bd1e995));
  return canvas;
}

/** Draws once, plus copies shifted by one tile wherever the shape crosses an edge, so the tile repeats seamlessly. */
function wrapped(context: CanvasRenderingContext2D, x: number, y: number, reach: number, draw: (x: number, y: number) => void): void {
  const offsets = [0];
  const xs = x < reach ? [0, TILE_PIXELS] : x > TILE_PIXELS - reach ? [0, -TILE_PIXELS] : offsets;
  const ys = y < reach ? [0, TILE_PIXELS] : y > TILE_PIXELS - reach ? [0, -TILE_PIXELS] : offsets;
  for (const dx of xs) {
    for (const dy of ys) {
      context.save();
      draw(x + dx, y + dy);
      context.restore();
    }
  }
}

function strokes(context: CanvasRenderingContext2D, random: () => number, count: number, minLength: number, maxLength: number, colors: Rgb[]): void {
  context.lineCap = "round";
  for (let index = 0; index < count; index++) {
    const x = random() * TILE_PIXELS;
    const y = random() * TILE_PIXELS;
    const length = minLength + random() * (maxLength - minLength);
    const lean = (random() - 0.5) * 1.4;
    const color = colors[Math.floor(random() * colors.length)];
    context.strokeStyle = css(color, 0.55 + random() * 0.4);
    context.lineWidth = 0.8 + random() * 1.1;
    wrapped(context, x, y, maxLength, (px, py) => {
      context.beginPath();
      context.moveTo(px, py);
      context.lineTo(px + Math.sin(lean) * length, py - Math.cos(lean) * length);
      context.stroke();
    });
  }
}

function dots(context: CanvasRenderingContext2D, random: () => number, count: number, minRadius: number, maxRadius: number, colors: Rgb[]): void {
  for (let index = 0; index < count; index++) {
    const x = random() * TILE_PIXELS;
    const y = random() * TILE_PIXELS;
    const radius = minRadius + random() * (maxRadius - minRadius);
    context.fillStyle = css(colors[Math.floor(random() * colors.length)], 0.9);
    wrapped(context, x, y, radius, (px, py) => {
      context.beginPath();
      context.arc(px, py, radius, 0, Math.PI * 2);
      context.fill();
    });
  }
}

function blobs(context: CanvasRenderingContext2D, random: () => number, count: number, minRadius: number, maxRadius: number, colors: Rgb[], alpha: number): void {
  for (let index = 0; index < count; index++) {
    const x = random() * TILE_PIXELS;
    const y = random() * TILE_PIXELS;
    const radius = minRadius + random() * (maxRadius - minRadius);
    const color = colors[Math.floor(random() * colors.length)];
    wrapped(context, x, y, radius, (px, py) => {
      const gradient = context.createRadialGradient(px, py, 0, px, py, radius);
      gradient.addColorStop(0, css(color, alpha));
      gradient.addColorStop(1, css(color, 0));
      context.fillStyle = gradient;
      context.fillRect(px - radius, py - radius, radius * 2, radius * 2);
    });
  }
}

function leaves(context: CanvasRenderingContext2D, random: () => number, count: number, colors: Rgb[]): void {
  for (let index = 0; index < count; index++) {
    const x = random() * TILE_PIXELS;
    const y = random() * TILE_PIXELS;
    const length = 3 + random() * 4;
    const angle = random() * Math.PI;
    const color = colors[Math.floor(random() * colors.length)];
    wrapped(context, x, y, length, (px, py) => {
      context.translate(px, py);
      context.rotate(angle);
      context.fillStyle = css(color, 0.9);
      context.beginPath();
      context.ellipse(0, 0, length, length * 0.42, 0, 0, Math.PI * 2);
      context.fill();
      context.strokeStyle = css([60, 40, 20], 0.5);
      context.lineWidth = 0.5;
      context.beginPath();
      context.moveTo(-length, 0);
      context.lineTo(length, 0);
      context.stroke();
    });
  }
}

function twigs(context: CanvasRenderingContext2D, random: () => number, count: number): void {
  context.lineCap = "round";
  for (let index = 0; index < count; index++) {
    const x = random() * TILE_PIXELS;
    const y = random() * TILE_PIXELS;
    const length = 8 + random() * 14;
    const angle = random() * Math.PI * 2;
    context.strokeStyle = css([82, 56, 34], 0.85);
    context.lineWidth = 1 + random();
    wrapped(context, x, y, length, (px, py) => {
      context.beginPath();
      context.moveTo(px, py);
      context.lineTo(px + Math.cos(angle) * length, py + Math.sin(angle) * length);
      context.lineTo(px + Math.cos(angle + 0.5) * length * 1.3, py + Math.sin(angle + 0.5) * length * 1.3);
      context.stroke();
    });
  }
}

/** Stones with a lit top-left and a shadow bottom-right read as round from the isometric camera. */
function pebbles(context: CanvasRenderingContext2D, random: () => number, count: number, minRadius: number, maxRadius: number, colors: Rgb[]): void {
  for (let index = 0; index < count; index++) {
    const x = random() * TILE_PIXELS;
    const y = random() * TILE_PIXELS;
    const radius = minRadius + random() * (maxRadius - minRadius);
    const squash = 0.6 + random() * 0.4;
    const angle = random() * Math.PI;
    const color = colors[Math.floor(random() * colors.length)];
    wrapped(context, x, y, radius * 1.6, (px, py) => {
      context.fillStyle = css([40, 36, 30], 0.35);
      context.beginPath();
      context.ellipse(px + radius * 0.3, py + radius * 0.35, radius, radius * squash, angle, 0, Math.PI * 2);
      context.fill();
      const gradient = context.createRadialGradient(px - radius * 0.35, py - radius * 0.35, 0, px, py, radius);
      gradient.addColorStop(0, css(lighten(color, 1.3), 1));
      gradient.addColorStop(1, css(color, 1));
      context.fillStyle = gradient;
      context.beginPath();
      context.ellipse(px, py, radius, radius * squash, angle, 0, Math.PI * 2);
      context.fill();
    });
  }
}

function cracks(context: CanvasRenderingContext2D, random: () => number, count: number): void {
  context.lineCap = "round";
  for (let index = 0; index < count; index++) {
    let x = random() * TILE_PIXELS;
    let y = random() * TILE_PIXELS;
    let angle = random() * Math.PI * 2;
    context.strokeStyle = css([44, 40, 36], 0.45);
    context.lineWidth = 0.8 + random() * 0.8;
    for (let segment = 0; segment < 6; segment++) {
      const length = 5 + random() * 10;
      angle += (random() - 0.5) * 1.2;
      const fromX = x;
      const fromY = y;
      x += Math.cos(angle) * length;
      y += Math.sin(angle) * length;
      wrapped(context, fromX, fromY, length + 2, (px, py) => {
        context.beginPath();
        context.moveTo(px, py);
        context.lineTo(px + (x - fromX), py + (y - fromY));
        context.stroke();
      });
    }
  }
}

function ripples(context: CanvasRenderingContext2D, random: () => number, count: number, color: Rgb): void {
  context.strokeStyle = css(color, 0.5);
  context.lineWidth = 1.4;
  for (let index = 0; index < count; index++) {
    const y = (index / count) * TILE_PIXELS + random() * 4;
    context.beginPath();
    for (let x = 0; x <= TILE_PIXELS; x += 8) {
      const wave = Math.sin((x / TILE_PIXELS) * Math.PI * 2 * 3 + index) * 4;
      if (x === 0) {
        context.moveTo(x, y + wave);
      } else {
        context.lineTo(x, y + wave);
      }
    }
    context.stroke();
  }
}

/** Two worn wheel tracks along the road direction (the tile's V axis). */
function ruts(context: CanvasRenderingContext2D, color: Rgb): void {
  for (const center of [0.3, 0.7]) {
    const gradient = context.createLinearGradient((center - 0.08) * TILE_PIXELS, 0, (center + 0.08) * TILE_PIXELS, 0);
    gradient.addColorStop(0, css(color, 0));
    gradient.addColorStop(0.5, css(color, 0.6));
    gradient.addColorStop(1, css(color, 0));
    context.fillStyle = gradient;
    context.fillRect((center - 0.08) * TILE_PIXELS, 0, 0.16 * TILE_PIXELS, TILE_PIXELS);
  }
}

function cobbles(context: CanvasRenderingContext2D, random: () => number, tint: THREE.Color): void {
  const rows = 8;
  const stone = TILE_PIXELS / rows;
  for (let row = 0; row < rows; row++) {
    const shift = row % 2 === 0 ? 0 : stone / 2;
    for (let column = 0; column <= rows; column++) {
      const x = column * stone + shift;
      const y = row * stone;
      const color = rgb(tint, 0.85 + random() * 0.35);
      wrapped(context, x + stone / 2, y + stone / 2, stone, (px, py) => {
        const left = px - stone / 2 + 3;
        const top = py - stone / 2 + 3;
        const gradient = context.createLinearGradient(left, top, left + stone, top + stone);
        gradient.addColorStop(0, css(lighten(color, 1.18), 1));
        gradient.addColorStop(1, css(lighten(color, 0.82), 1));
        context.fillStyle = gradient;
        context.beginPath();
        context.roundRect(left, top, stone - 6, stone - 6, 9);
        context.fill();
      });
    }
  }
  pebbles(context, random, 120, 1, 2, [rgb(tint, 0.6)]);
}

/** Fractal value noise that tiles: every octave's lattice repeats exactly once per tile. */
function periodicFbm(u: number, v: number, seed: number): number {
  let sum = 0;
  let amplitude = 0.5;
  let total = 0;
  for (let octave = 0, period = 4; octave < 4; octave++, period *= 2) {
    sum += periodicNoise(u * period, v * period, period, seed + octave * 101) * amplitude;
    total += amplitude;
    amplitude *= 0.5;
  }
  return sum / total;
}

function periodicNoise(x: number, y: number, period: number, seed: number): number {
  const cellX = Math.floor(x);
  const cellY = Math.floor(y);
  const fx = x - cellX;
  const fy = y - cellY;
  const u = fx * fx * (3 - 2 * fx);
  const v = fy * fy * (3 - 2 * fy);
  const at = (ix: number, iy: number) => lattice(((ix % period) + period) % period, ((iy % period) + period) % period, seed);
  const a = at(cellX, cellY);
  const b = at(cellX + 1, cellY);
  const c = at(cellX, cellY + 1);
  const d = at(cellX + 1, cellY + 1);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}

function lattice(x: number, y: number, seed: number): number {
  let hash = Math.imul(x + seed * 31, 374761393) + Math.imul(y, 668265263);
  hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
  return ((hash ^ (hash >>> 16)) >>> 0) / 0x100000000;
}

function rgb(color: THREE.Color, factor: number): Rgb {
  return [clamp255(color.r * 255 * factor), clamp255(color.g * 255 * factor), clamp255(color.b * 255 * factor)];
}

function shift(color: THREE.Color, hue: number, lightness: number): Rgb {
  return rgb(color.clone().offsetHSL(hue, 0, 0), lightness);
}

function lighten(color: Rgb, factor: number): Rgb {
  return [clamp255(color[0] * factor), clamp255(color[1] * factor), clamp255(color[2] * factor)];
}

function css(color: Rgb, alpha: number): string {
  return `rgba(${color[0]}, ${color[1]}, ${color[2]}, ${alpha})`;
}

function clamp255(value: number): number {
  return Math.max(0, Math.min(255, Math.round(value)));
}
