import * as THREE from "three";
import { GLTF, GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { Animator } from "../characters/rig";
import { ClipSlot } from "./generatedAnimators";

const ROOT_BONE = "Hips";
const FORWARD = new THREE.Vector3(0, 0, 1);
const UP = new THREE.Vector3(0, 1, 0);

/** One model entry of public/models/manifest.json, written by scripts/meshy-generate.mjs and meshy-kit.mjs. */
export interface ManifestEntry {
  model: string;
  animations?: string;
  rig: "biped" | "quadruped" | "none";
  height: number;
  clips: Partial<Record<ClipSlot, string>>;
}

export interface LoadedAsset {
  entry: ManifestEntry;
  /** Normalised holder: scaled to the entry height, feet on y = 0, centred. Clone it, never add it to a scene. */
  scene: THREE.Object3D;
  clips: Map<ClipSlot, THREE.AnimationClip>;
}

export interface GeneratedModel {
  /** Pivot the animator moves; already scaled to the entry height and standing on y = 0. */
  body: THREE.Group;
  animator: Animator;
  height: number;
}

export async function loadAsset(loader: GLTFLoader, entry: ManifestEntry): Promise<LoadedAsset> {
  const [model, animations] = await Promise.all([
    loader.loadAsync(`/${entry.model}`),
    entry.animations ? loader.loadAsync(`/${entry.animations}`) : Promise.resolve(null),
  ]);
  return { entry, scene: normalize(model.scene, entry.height), clips: clipsBySlot(entry, animations) };
}

/** Scales to the intended height, centres on the feet and faces +Z like the procedural models. */
export function normalize(scene: THREE.Object3D, height: number): THREE.Object3D {
  const holder = new THREE.Group();
  holder.add(scene);
  const bounds = new THREE.Box3().setFromObject(scene);
  const size = bounds.getSize(new THREE.Vector3());
  const scale = size.y > 0 ? height / size.y : 1;
  scene.scale.multiplyScalar(scale);
  const center = bounds.getCenter(new THREE.Vector3());
  scene.position.set(-center.x * scale, -bounds.min.y * scale, -center.z * scale);
  holder.updateMatrixWorld(true);
  return holder;
}

export function castShadows(object: THREE.Object3D): void {
  object.traverse((child) => {
    if (child instanceof THREE.Mesh) {
      child.castShadow = true;
    }
  });
}

function clipsBySlot(entry: ManifestEntry, animations: GLTF | null): Map<ClipSlot, THREE.AnimationClip> {
  const clips = new Map<ClipSlot, THREE.AnimationClip>();
  for (const [slot, name] of Object.entries(entry.clips) as [ClipSlot, string][]) {
    const wanted = clipNameKey(name);
    const candidates = animations?.animations ?? [];
    const clip = candidates.find((candidate) => clipNameKey(candidate.name) === wanted)
      ?? candidates.find((candidate) => clipNameKey(candidate.name).includes(wanted));
    if (clip) {
      clips.set(slot, faceForward(clip));
    }
  }
  return clips;
}

/**
 * Meshy library clips are authored in a fighting stance turned up to 60° away from +Z. Turning each clip by its
 * average root facing makes idles face where the character looks and attacks land on the target.
 */
export function faceForward(clip: THREE.AnimationClip): THREE.AnimationClip {
  const rotation = clip.tracks.find((track) => track.name === `${ROOT_BONE}.quaternion`);
  if (!rotation) {
    return clip;
  }
  const facing = new THREE.Vector3();
  const frame = new THREE.Quaternion();
  for (let offset = 0; offset < rotation.values.length; offset += 4) {
    facing.add(FORWARD.clone().applyQuaternion(frame.fromArray(rotation.values, offset)));
  }
  const correction = new THREE.Quaternion().setFromAxisAngle(UP, -Math.atan2(facing.x, facing.z));
  const turned = clip.clone();
  for (const track of turned.tracks) {
    if (track.name === `${ROOT_BONE}.quaternion`) {
      for (let offset = 0; offset < track.values.length; offset += 4) {
        frame.fromArray(track.values, offset).premultiply(correction).toArray(track.values, offset);
      }
    } else if (track.name === `${ROOT_BONE}.position`) {
      const point = new THREE.Vector3();
      for (let offset = 0; offset < track.values.length; offset += 3) {
        point.fromArray(track.values, offset).applyQuaternion(correction).toArray(track.values, offset);
      }
    }
  }
  return turned;
}

/** Meshy's merged GLB renames library clips ("Run 2" becomes "Run_02"), so compare words and numeric values only. */
export function clipNameKey(name: string): string {
  return name.toLowerCase().replace(/\d+/g, (digits) => String(Number(digits))).replace(/[^a-z0-9]/g, "");
}
