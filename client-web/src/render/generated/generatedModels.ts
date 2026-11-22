import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { clone as cloneSkinned } from "three/addons/utils/SkeletonUtils.js";
import * as THREE from "three";
import { AppearanceKit, KitManifest } from "./appearanceKit";
import { ClipAnimator, StaticMeshAnimator } from "./generatedAnimators";
import { AppearanceHints } from "./kitAppearance";
import { castShadows, GeneratedModel, LoadedAsset, loadAsset, ManifestEntry } from "./modelAssets";

export { clipNameKey } from "./modelAssets";
export type { GeneratedModel } from "./modelAssets";
export type { AppearanceHints } from "./kitAppearance";

const MANIFEST_URL = "/models/manifest.json";
const RACE_KEY_PREFIX = "race:";

interface Manifest {
  assets?: Record<string, ManifestEntry>;
  kits?: Record<string, KitManifest>;
}

export function raceModelKey(raceId: string): string {
  return `${RACE_KEY_PREFIX}${raceId}`;
}

export function mobModelKey(mobId: string): string {
  return `mob:${mobId}`;
}

/**
 * Meshy-made models, loaded on demand. A race with an appearance kit builds every character from kit parts;
 * other keys use one model. Everything is optional: with no manifest or a failed download the renderer keeps
 * its procedural models, and a model that arrives later replaces them in place.
 */
export class GeneratedModels {
  private readonly loader = new GLTFLoader();
  private readonly entries = new Map<string, ManifestEntry>();
  private readonly kits = new Map<string, AppearanceKit>();
  private readonly loaded = new Map<string, LoadedAsset>();
  private readonly readyKits = new Set<string>();
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
    if (!this.manifestReady || this.loading.has(key) || this.loaded.has(key) || this.readyKits.has(key)) {
      return;
    }
    const kit = this.kits.get(key);
    if (kit) {
      void this.track(key, () => kit.load(), () => this.readyKits.add(key));
      return;
    }
    const entry = this.entries.get(key);
    if (entry) {
      void this.track(key, () => loadAsset(this.loader, entry), (asset) => this.loaded.set(key, asset));
    }
  }

  /** Hints pick the look inside an appearance kit; single models ignore them. */
  instantiate(key: string, hints: AppearanceHints): GeneratedModel | null {
    if (this.readyKits.has(key)) {
      return this.kits.get(key)!.instantiate(hints);
    }
    const asset = this.loaded.get(key);
    return asset ? instantiateAsset(asset) : null;
  }

  private async loadManifest(): Promise<void> {
    try {
      const response = await fetch(MANIFEST_URL);
      const manifest: Manifest = response.ok ? await response.json() : {};
      Object.entries(manifest.assets ?? {}).forEach(([key, entry]) => this.entries.set(key, entry));
      for (const [raceId, kitManifest] of Object.entries(manifest.kits ?? {})) {
        const kit = new AppearanceKit(kitManifest, this.loader);
        if (!kit.isEmpty) {
          this.kits.set(raceModelKey(raceId), kit);
        }
      }
    } catch {
      // No generated models yet: procedural ones are used everywhere.
    }
    this.manifestReady = true;
    [...this.wanted].forEach((key) => this.request(key));
  }

  private async track<T>(key: string, load: () => Promise<T>, store: (result: T) => void): Promise<void> {
    this.loading.add(key);
    try {
      store(await load());
      this.listeners.forEach((listener) => listener(key));
    } catch (error) {
      console.warn(`Generated model ${key} failed to load, keeping the procedural one`, error);
    } finally {
      this.loading.delete(key);
    }
  }
}

function instantiateAsset(asset: LoadedAsset): GeneratedModel {
  const model = cloneSkinned(asset.scene);
  castShadows(model);
  const body = new THREE.Group();
  body.add(model);
  const animator = asset.entry.rig === "none" || asset.clips.size === 0
    ? new StaticMeshAnimator(body, asset.entry.height)
    : new ClipAnimator(model, asset.clips);
  return { body, animator, height: asset.entry.height };
}
