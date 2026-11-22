import * as THREE from "three";

const BACKDROP_RADIUS = 2.6;
const BACKDROP_DEPTH = -1.6;
const RUNE_TEXTURE_PIXELS = 512;
const RUNES = "ᚠᚢᚦᚨᚱᚲᚷᚹᚺᚾᛁᛃᛇᛈᛉᛊᛏᛒᛖᛗᛚᛜᛞᛟ";
const SPARK_COUNT = 70;
const SPARK_RISE_PER_SECOND = 0.5;
const OUTER_RING_TURN_PER_SECOND = 0.12;
const INNER_RING_TURN_PER_SECOND = -0.22;
const FLASH_FADE_PER_SECOND = 1.6;
const RESTING_GLOW = 0.55;

export interface StageTheme { runes: number; sparks: number; candle: number }

export const FRIENDLY_THEME: StageTheme = { runes: 0xffc860, sparks: 0xffd890, candle: 0xffa040 };
export const HOSTILE_THEME: StageTheme = { runes: 0x8a7aff, sparks: 0xb0a0ff, candle: 0x7060ff };

/** The backdrop behind the d20: a leather medallion with a slowly turning rune circle, drifting sparks and candlelight. */
export class DiceStage {
  readonly group = new THREE.Group();
  private readonly outerRing: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshBasicMaterial>;
  private readonly innerRing: THREE.Mesh<THREE.PlaneGeometry, THREE.MeshBasicMaterial>;
  private readonly sparks: THREE.Points<THREE.BufferGeometry, THREE.PointsMaterial>;
  private readonly candle = new THREE.PointLight(0xffa040, 6, 9, 1.6);
  private readonly flashColor = new THREE.Color();
  private readonly themeColor = new THREE.Color();
  private flash = 0;
  private clock = 0;

  constructor() {
    const backdrop = new THREE.Mesh(
      new THREE.CircleGeometry(BACKDROP_RADIUS, 64),
      new THREE.MeshBasicMaterial({ map: leatherTexture() }));
    backdrop.position.z = BACKDROP_DEPTH;
    this.outerRing = runeLayer(runeCircleTexture(true), BACKDROP_RADIUS * 0.98, BACKDROP_DEPTH + 0.01);
    this.innerRing = runeLayer(runeCircleTexture(false), BACKDROP_RADIUS * 0.62, BACKDROP_DEPTH + 0.02);
    this.sparks = sparkField();
    this.candle.position.set(-1.8, -1.4, 2.4);
    this.group.add(backdrop, this.outerRing, this.innerRing, this.sparks, this.candle);
    this.setTheme(FRIENDLY_THEME);
  }

  setTheme(theme: StageTheme): void {
    this.themeColor.set(theme.runes);
    this.sparks.material.color.set(theme.sparks);
    this.candle.color.set(theme.candle);
    this.flash = 0;
  }

  /** A burst of colour through the runes, for critical hits and fumbles. */
  burst(color: number): void {
    this.flashColor.set(color);
    this.flash = 1;
  }

  update(deltaSeconds: number): void {
    this.clock += deltaSeconds;
    this.outerRing.rotation.z += deltaSeconds * OUTER_RING_TURN_PER_SECOND;
    this.innerRing.rotation.z += deltaSeconds * INNER_RING_TURN_PER_SECOND;
    this.flash = Math.max(0, this.flash - deltaSeconds * FLASH_FADE_PER_SECOND);
    const glow = RESTING_GLOW + Math.sin(this.clock * 2.2) * 0.08 + this.flash * 0.6;
    for (const ring of [this.outerRing, this.innerRing]) {
      ring.material.color.copy(this.themeColor).lerp(this.flashColor, this.flash).multiplyScalar(glow);
    }
    this.candle.intensity = 5 + Math.sin(this.clock * 9) * 0.8 + Math.sin(this.clock * 23) * 0.5 + this.flash * 8;
    this.driftSparks(deltaSeconds);
  }

  private driftSparks(deltaSeconds: number): void {
    const positions = this.sparks.geometry.getAttribute("position") as THREE.BufferAttribute;
    for (let index = 0; index < positions.count; index++) {
      let y = positions.getY(index) + deltaSeconds * SPARK_RISE_PER_SECOND * (0.5 + (index % 5) * 0.2);
      if (y > BACKDROP_RADIUS) {
        y = -BACKDROP_RADIUS;
      }
      positions.setY(index, y);
      positions.setX(index, positions.getX(index) + Math.sin(this.clock * 1.5 + index) * deltaSeconds * 0.08);
    }
    positions.needsUpdate = true;
  }
}

function runeLayer(texture: THREE.Texture, radius: number, depth: number): THREE.Mesh<THREE.PlaneGeometry, THREE.MeshBasicMaterial> {
  const layer = new THREE.Mesh(
    new THREE.PlaneGeometry(radius * 2, radius * 2),
    new THREE.MeshBasicMaterial({ map: texture, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false }));
  layer.position.z = depth;
  return layer;
}

function sparkField(): THREE.Points<THREE.BufferGeometry, THREE.PointsMaterial> {
  const positions = new Float32Array(SPARK_COUNT * 3);
  for (let index = 0; index < SPARK_COUNT; index++) {
    positions[index * 3] = (Math.random() * 2 - 1) * BACKDROP_RADIUS * 0.9;
    positions[index * 3 + 1] = (Math.random() * 2 - 1) * BACKDROP_RADIUS;
    positions[index * 3 + 2] = BACKDROP_DEPTH + 0.3 + Math.random() * 2.2;
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  const material = new THREE.PointsMaterial({
    size: 0.07, map: sparkTexture(), transparent: true, blending: THREE.AdditiveBlending, depthWrite: false,
  });
  return new THREE.Points(geometry, material);
}

function canvasTexture(size: number, paint: (context: CanvasRenderingContext2D, size: number) => void): THREE.CanvasTexture {
  const canvas = document.createElement("canvas");
  canvas.width = size;
  canvas.height = size;
  paint(canvas.getContext("2d")!, size);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

function leatherTexture(): THREE.CanvasTexture {
  return canvasTexture(RUNE_TEXTURE_PIXELS, (context, size) => {
    const center = size / 2;
    const gradient = context.createRadialGradient(center, center * 0.9, size * 0.05, center, center, size / 2);
    gradient.addColorStop(0, "#3a2a26");
    gradient.addColorStop(0.65, "#1e1418");
    gradient.addColorStop(1, "#08060a");
    context.fillStyle = gradient;
    context.fillRect(0, 0, size, size);
    for (let index = 0; index < 2600; index++) {
      context.fillStyle = `rgba(${Math.random() < 0.5 ? "255,220,200" : "0,0,0"},${Math.random() * 0.05})`;
      context.fillRect(Math.random() * size, Math.random() * size, 2, 2);
    }
  });
}

function runeCircleTexture(outer: boolean): THREE.CanvasTexture {
  return canvasTexture(RUNE_TEXTURE_PIXELS, (context, size) => {
    const center = size / 2;
    context.translate(center, center);
    context.strokeStyle = "#ffffff";
    context.fillStyle = "#ffffff";
    context.shadowColor = "#ffffff";
    context.shadowBlur = 10;
    const ring = (radius: number, width: number) => {
      context.lineWidth = width;
      context.beginPath();
      context.arc(0, 0, radius, 0, Math.PI * 2);
      context.stroke();
    };
    if (outer) {
      ring(size * 0.47, 3);
      ring(size * 0.39, 2);
      context.font = `${Math.round(size * 0.055)}px serif`;
      context.textAlign = "center";
      context.textBaseline = "middle";
      [...RUNES].forEach((rune, index, runes) => {
        context.save();
        context.rotate((index / runes.length) * Math.PI * 2);
        context.fillText(rune, 0, -size * 0.43);
        context.restore();
      });
      return;
    }
    ring(size * 0.46, 2.5);
    starPolygon(context, size * 0.45, 7, 3);
    ring(size * 0.2, 2);
  });
}

function starPolygon(context: CanvasRenderingContext2D, radius: number, points: number, step: number): void {
  context.lineWidth = 2;
  context.beginPath();
  for (let index = 0; index <= points; index++) {
    const angle = ((index * step) / points) * Math.PI * 2 - Math.PI / 2;
    const x = Math.cos(angle) * radius;
    const y = Math.sin(angle) * radius;
    if (index === 0) {
      context.moveTo(x, y);
    } else {
      context.lineTo(x, y);
    }
  }
  context.stroke();
}

function sparkTexture(): THREE.CanvasTexture {
  return canvasTexture(32, (context, size) => {
    const gradient = context.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    gradient.addColorStop(0, "rgba(255,255,255,1)");
    gradient.addColorStop(0.4, "rgba(255,255,255,0.5)");
    gradient.addColorStop(1, "rgba(255,255,255,0)");
    context.fillStyle = gradient;
    context.fillRect(0, 0, size, size);
  });
}
