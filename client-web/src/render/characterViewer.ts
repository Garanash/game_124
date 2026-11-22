import * as THREE from "three";
import { adventurerOutfit, raceLook, stableHash } from "./characters/appearance";
import { buildHumanoid } from "./characters/humanoid";
import { Animator, CharacterAnimator } from "./characters/rig";
import { GeneratedModels, raceModelKey } from "./generated/generatedModels";

const FIELD_OF_VIEW = 28;
const DEFAULT_DISTANCE_PER_METER = 3.3;
const EXPOSURE = 1.6;
const MIN_ZOOM = 0.35;
const MAX_ZOOM = 1.5;
const ZOOM_PER_WHEEL_STEP = 0.0012;
/** Zooming in slides the camera from the chest towards the face. */
const FOCUS_CHEST = 0.5;
const FOCUS_FACE = 0.88;
const YAW_PER_PIXEL = 0.012;
const PITCH_PER_PIXEL = 0.004;
const MIN_PITCH = -0.15;
const MAX_PITCH = 0.55;
const IDLE_SPIN_PER_SECOND = 0.25;
const IDLE_SPIN_DELAY_SECONDS = 4;
const START_YAW = -0.45;

export interface ViewedHero { raceId: string; name: string }

/** A turntable for the player's own hero in the character window: drag to turn, wheel to zoom. */
export class CharacterViewer {
  readonly element = document.createElement("div");
  private readonly renderer: THREE.WebGLRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(FIELD_OF_VIEW, 1, 0.05, 50);
  private readonly turntable = new THREE.Group();
  private readonly runes: THREE.Mesh;
  private readonly clock = new THREE.Clock();
  private hero: ViewedHero | null = null;
  private body: THREE.Object3D | null = null;
  private animator: Animator | null = null;
  private height = 1.8;
  private zoom = 1;
  private pitch = 0.12;
  private idleSeconds = 0;
  private dragX: number | null = null;
  private dragY = 0;
  private frame = 0;

  constructor(private readonly models: GeneratedModels) {
    this.element.className = "hero-viewer";
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = EXPOSURE;
    this.renderer.shadowMap.enabled = true;
    this.element.append(this.renderer.domElement);
    this.runes = runeCircle();
    this.scene.add(this.turntable, pedestal(), this.runes, ...lights());
    this.listen();
    models.onReady((key) => {
      if (this.hero && key === raceModelKey(this.hero.raceId)) {
        this.rebuild();
      }
    });
  }

  show(hero: ViewedHero): void {
    if (this.hero?.raceId === hero.raceId && this.hero.name === hero.name) {
      return;
    }
    this.hero = hero;
    this.models.request(raceModelKey(hero.raceId));
    this.rebuild();
  }

  /** Renders only while the window is open, so a hidden viewer costs nothing. */
  setActive(active: boolean): void {
    cancelAnimationFrame(this.frame);
    if (active) {
      this.clock.getDelta();
      this.frame = requestAnimationFrame(() => this.tick());
    }
  }

  private rebuild(): void {
    if (!this.hero) {
      return;
    }
    if (this.body) {
      this.turntable.remove(this.body);
    }
    const hints = { seed: stableHash(this.hero.name), isAdventurer: true };
    const generated = this.models.instantiate(raceModelKey(this.hero.raceId), hints);
    if (generated) {
      this.body = generated.body;
      this.animator = generated.animator;
      this.height = generated.height;
    } else {
      const rig = buildHumanoid(raceLook(this.hero.raceId), adventurerOutfit(hints.seed));
      this.body = rig.root;
      this.animator = new CharacterAnimator(rig);
      this.height = rig.height;
    }
    this.body.traverse((child) => {
      child.castShadow = true;
    });
    this.turntable.add(this.body);
    this.turntable.rotation.y = START_YAW;
  }

  private tick(): void {
    this.frame = requestAnimationFrame(() => this.tick());
    const deltaSeconds = Math.min(this.clock.getDelta(), 0.1);
    this.resize();
    this.idleSeconds += deltaSeconds;
    if (this.dragX === null && this.idleSeconds > IDLE_SPIN_DELAY_SECONDS) {
      this.turntable.rotation.y += deltaSeconds * IDLE_SPIN_PER_SECOND;
    }
    this.runes.rotation.z += deltaSeconds * 0.15;
    this.animator?.update(deltaSeconds, 0, "idle");
    this.placeCamera();
    this.renderer.render(this.scene, this.camera);
  }

  private placeCamera(): void {
    const closeness = (MAX_ZOOM - this.zoom) / (MAX_ZOOM - MIN_ZOOM);
    const focus = this.height * THREE.MathUtils.lerp(FOCUS_CHEST, FOCUS_FACE, closeness * closeness);
    const distance = this.height * DEFAULT_DISTANCE_PER_METER * this.zoom;
    this.camera.position.set(0, focus + Math.sin(this.pitch) * distance, Math.cos(this.pitch) * distance);
    this.camera.lookAt(0, focus, 0);
  }

  private resize(): void {
    const width = this.element.clientWidth;
    const height = this.element.clientHeight;
    const canvas = this.renderer.domElement;
    if (width > 0 && height > 0 && (canvas.width !== Math.floor(width * this.renderer.getPixelRatio()) || canvas.height !== Math.floor(height * this.renderer.getPixelRatio()))) {
      this.renderer.setSize(width, height, false);
      this.camera.aspect = width / height;
      this.camera.updateProjectionMatrix();
    }
  }

  private listen(): void {
    const canvas = this.renderer.domElement;
    canvas.addEventListener("pointerdown", (event) => {
      this.dragX = event.clientX;
      this.dragY = event.clientY;
      canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener("pointermove", (event) => {
      if (this.dragX === null) {
        return;
      }
      this.turntable.rotation.y += (event.clientX - this.dragX) * YAW_PER_PIXEL;
      this.pitch = THREE.MathUtils.clamp(this.pitch + (event.clientY - this.dragY) * PITCH_PER_PIXEL, MIN_PITCH, MAX_PITCH);
      this.dragX = event.clientX;
      this.dragY = event.clientY;
      this.idleSeconds = 0;
    });
    canvas.addEventListener("pointerup", () => {
      this.dragX = null;
    });
    canvas.addEventListener("wheel", (event) => {
      event.preventDefault();
      this.zoom = THREE.MathUtils.clamp(this.zoom + event.deltaY * ZOOM_PER_WHEEL_STEP, MIN_ZOOM, MAX_ZOOM);
      this.idleSeconds = 0;
    }, { passive: false });
  }
}

function lights(): THREE.Light[] {
  const key = new THREE.DirectionalLight(0xfff0dc, 2.6);
  key.position.set(2.5, 4, 3);
  key.castShadow = true;
  key.shadow.mapSize.set(1024, 1024);
  const rim = new THREE.DirectionalLight(0x8ab4ff, 1.6);
  rim.position.set(-3, 2.5, -3);
  const front = new THREE.DirectionalLight(0xffffff, 1.2);
  front.position.set(0, 1.5, 4);
  const fill = new THREE.HemisphereLight(0xd8d0ff, 0x4a3a2a, 1.8);
  return [key, rim, front, fill];
}

function pedestal(): THREE.Mesh {
  const stone = new THREE.Mesh(
    new THREE.CylinderGeometry(0.85, 0.95, 0.14, 48),
    new THREE.MeshStandardMaterial({ color: 0x3a3442, roughness: 0.8, metalness: 0.1 }));
  stone.position.y = -0.07;
  stone.receiveShadow = true;
  return stone;
}

function runeCircle(): THREE.Mesh {
  const ring = new THREE.Mesh(
    new THREE.RingGeometry(0.88, 0.98, 64),
    new THREE.MeshBasicMaterial({ color: 0xffc860, transparent: true, opacity: 0.55, side: THREE.DoubleSide }));
  ring.rotation.x = -Math.PI / 2;
  ring.position.y = 0.005;
  return ring;
}
