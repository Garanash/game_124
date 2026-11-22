import * as THREE from "three";
import { GLTF, GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { clone as cloneSkinned } from "three/addons/utils/SkeletonUtils.js";
import { Animator } from "../characters/rig";
import { ClipAnimator, ClipSlot, StaticMeshAnimator } from "./generatedAnimators";

const MANIFEST_URL = "/models/manifest.json";

/** One entry of public/models/manifest.json, written by scripts/meshy-generate.mjs. */
interface ManifestEntry {
  model: string;
  animations?: string;
  rig: "biped" | "quadruped" | "none";
  height: number;
  clips: Partial<Record<ClipSlot, string>>;
}

interface LoadedAsset {
  entry: ManifestEntry;
  scene: THREE.Object3D;
  clips: Map<ClipSlot, THREE.AnimationClip>;
}

export interface GeneratedModel {
  /** Pivot the animator moves; already scaled to the entry height and standing on y = 0. */
  body: THREE.Group;
  animator: Animator;
  height: number;
}

export function raceModelKey(raceId: string): string {
  return `race:${raceId}`;
}

export function mobModelKey(mobId: string): string {
  return `mob:${mobId}`;
}

/**
 * Meshy-made GLB models, loaded on demand. Everything is optional: with no manifest or a failed download
 * the renderer keeps its procedural models, and a model that arrives later replaces them in place.
 */
export class GeneratedModels {
  private readonly loader = new GLTFLoader();
  private readonly manifest = new Map<string, ManifestEntry>();
  private readonly loaded = new Map<string, LoadedAsset>();
  private readonly loading = new Set<string>();
  private readonly wanted = new Set<string>();
  private readonly listeners: ((key: string) => void)[] = [];
  private manifestReady = false;

  constructor() {
    void this.loadManifest();
  }

  onReady(listener: (key: string) => void): void {
    this.listeners.push(listener);
  }

  /** Starts loading in the background; calling it again for the same key is free. */
  request(key: string): void {
    this.wanted.add(key);
    if (!this.manifestReady || this.loaded.has(key) || this.loading.has(key)) {
      return;
    }
    const entry = this.manifest.get(key);
    if (entry) {
      void this.load(key, entry);
    }
  }

  instantiate(key: string): GeneratedModel | null {
    const asset = this.loaded.get(key);
    if (!asset) {
      return null;
    }
    const model = cloneSkinned(asset.scene);
    model.traverse((child) => {
      if (child instanceof THREE.Mesh) {
        child.castShadow = true;
      }
    });
    const body = new THREE.Group();
    body.add(model);
    const animator = asset.entry.rig === "none" || asset.clips.size === 0
      ? new StaticMeshAnimator(body, asset.entry.height)
      : new ClipAnimator(model, asset.clips);
    return { body, animator, height: asset.entry.height };
  }

  private async loadManifest(): Promise<void> {
    try {
      const response = await fetch(MANIFEST_URL);
      const manifest = response.ok ? await response.json() as { assets: Record<string, ManifestEntry> } : { assets: {} };
      Object.entries(manifest.assets).forEach(([key, entry]) => this.manifest.set(key, entry));
    } catch {
      // No generated models yet: procedural ones are used everywhere.
    }
    this.manifestReady = true;
    [...this.wanted].forEach((key) => this.request(key));
  }

  private async load(key: string, entry: ManifestEntry): Promise<void> {
    this.loading.add(key);
    try {
      const [model, animations] = await Promise.all([
        this.loader.loadAsync(`/${entry.model}`),
        entry.animations ? this.loader.loadAsync(`/${entry.animations}`) : Promise.resolve(null),
      ]);
      this.loaded.set(key, { entry, scene: normalize(model.scene, entry.height), clips: clipsBySlot(entry, animations) });
      this.listeners.forEach((listener) => listener(key));
    } catch (error) {
      console.warn(`Generated model ${key} failed to load, keeping the procedural one`, error);
    } finally {
      this.loading.delete(key);
    }
  }
}

/** Scales to the intended height, centres on the feet and faces +Z like the procedural models. */
function normalize(scene: THREE.Object3D, height: number): THREE.Object3D {
  const holder = new THREE.Group();
  holder.add(scene);
  const bounds = new THREE.Box3().setFromObject(scene);
  const size = bounds.getSize(new THREE.Vector3());
  const scale = size.y > 0 ? height / size.y : 1;
  scene.scale.multiplyScalar(scale);
  const center = bounds.getCenter(new THREE.Vector3());
  scene.position.set(-center.x * scale, -bounds.min.y * scale, -center.z * scale);
  return holder;
}

function clipsBySlot(entry: ManifestEntry, animations: GLTF | null): Map<ClipSlot, THREE.AnimationClip> {
  const clips = new Map<ClipSlot, THREE.AnimationClip>();
  for (const [slot, name] of Object.entries(entry.clips) as [ClipSlot, string][]) {
    const wanted = clipNameKey(name);
    const candidates = animations?.animations ?? [];
    const clip = candidates.find((candidate) => clipNameKey(candidate.name) === wanted)
      ?? candidates.find((candidate) => clipNameKey(candidate.name).includes(wanted));
    if (clip) {
      clips.set(slot, clip);
    }
  }
  return clips;
}

/** Meshy's merged GLB renames library clips ("Run 2" becomes "Run_02"), so compare words and numeric values only. */
export function clipNameKey(name: string): string {
  return name.toLowerCase().replace(/\d+/g, (digits) => String(Number(digits))).replace(/[^a-z0-9]/g, "");
}
