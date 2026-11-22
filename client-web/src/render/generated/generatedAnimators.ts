import * as THREE from "three";
import { ActionKind, Activity, Animator } from "../characters/rig";

/** Clip slots written by scripts/meshy-generate.mjs into the manifest. */
export type ClipSlot = "idle" | "walk" | "run" | "attack" | "cast" | "hit" | "death" | "cheer";

const IDLE_BELOW_SPEED = 0.25;
const RUN_ABOVE_SPEED = 3.6;
/** Ground speed the walk and run clips were authored at; playback rate follows the real speed. */
const WALK_CLIP_SPEED = 1.4;
const RUN_CLIP_SPEED = 4.5;
const MIN_PLAYBACK_RATE = 0.6;
const MAX_PLAYBACK_RATE = 1.7;
const LOCOMOTION_BLEND_RATE = 8;
const ONE_SHOT_FADE_SECONDS = 0.12;
const DEATH_FADE_SECONDS = 0.25;
const HIT_FLASH_DECAY = 4;
const HIT_FLASH_COLOR = new THREE.Color(0xff3a2a);
const ACTION_SLOTS: Record<ActionKind, ClipSlot[]> = {
  attack: ["attack"],
  cast: ["cast", "attack"],
  hit: ["hit"],
  dodge: [],
  cheer: ["cheer"],
};

/** Red emissive pulse on hit, shared by both generated-model animators. */
class HitFlash {
  private readonly materials: THREE.MeshStandardMaterial[] = [];
  private readonly base = new Map<THREE.MeshStandardMaterial, THREE.Color>();
  private strength = 0;

  constructor(root: THREE.Object3D) {
    root.traverse((child) => {
      if (child instanceof THREE.Mesh) {
        const materials = (Array.isArray(child.material) ? child.material : [child.material]).map((material) => material.clone());
        child.material = Array.isArray(child.material) ? materials : materials[0];
        materials.filter((material): material is THREE.MeshStandardMaterial => material instanceof THREE.MeshStandardMaterial).forEach((material) => {
          this.materials.push(material);
          this.base.set(material, material.emissive.clone());
        });
      }
    });
  }

  trigger(): void {
    this.strength = 1;
  }

  update(deltaSeconds: number): void {
    if (this.strength <= 0) {
      return;
    }
    this.strength = Math.max(0, this.strength - deltaSeconds * HIT_FLASH_DECAY);
    for (const material of this.materials) {
      material.emissive.copy(this.base.get(material)!).lerp(HIT_FLASH_COLOR, this.strength);
    }
  }
}

/** Plays Meshy library clips: idle/walk/run blended by speed, combat one-shots on top, death clamped. */
export class ClipAnimator implements Animator {
  private readonly mixer: THREE.AnimationMixer;
  private readonly actions = new Map<ClipSlot, THREE.AnimationAction>();
  private readonly flash: HitFlash;
  private oneShot: THREE.AnimationAction | null = null;
  private dead = false;
  private weights = { idle: 1, walk: 0, run: 0 };

  constructor(root: THREE.Object3D, clips: Map<ClipSlot, THREE.AnimationClip>) {
    this.mixer = new THREE.AnimationMixer(root);
    this.flash = new HitFlash(root);
    for (const [slot, clip] of clips) {
      this.actions.set(slot, this.mixer.clipAction(clip));
    }
    for (const slot of ["idle", "walk", "run"] as const) {
      this.actions.get(slot)?.play().setEffectiveWeight(slot === "idle" ? 1 : 0);
    }
    this.mixer.addEventListener("finished", (event) => {
      if (event.action === this.oneShot) {
        this.oneShot.fadeOut(ONE_SHOT_FADE_SECONDS);
        this.oneShot = null;
      }
    });
    this.mixer.update(Math.random() * 2);
  }

  get isDead(): boolean {
    return this.dead;
  }

  play(kind: ActionKind): void {
    if (kind === "hit") {
      this.flash.trigger();
    }
    const action = ACTION_SLOTS[kind].map((slot) => this.actions.get(slot)).find(Boolean);
    if (!action || this.dead) {
      return;
    }
    this.oneShot?.fadeOut(ONE_SHOT_FADE_SECONDS);
    action.reset().setLoop(THREE.LoopOnce, 1).fadeIn(ONE_SHOT_FADE_SECONDS).play();
    action.clampWhenFinished = false;
    this.oneShot = action;
  }

  setDead(dead: boolean): void {
    if (dead === this.dead) {
      return;
    }
    this.dead = dead;
    const death = this.actions.get("death");
    if (!death) {
      return;
    }
    if (dead) {
      this.oneShot?.stop();
      this.oneShot = null;
      death.reset().setLoop(THREE.LoopOnce, 1).fadeIn(DEATH_FADE_SECONDS).play();
      death.clampWhenFinished = true;
    } else {
      death.fadeOut(DEATH_FADE_SECONDS);
    }
  }

  update(deltaSeconds: number, speed: number, _activity: Activity): void {
    this.flash.update(deltaSeconds);
    const target = this.dead || speed < IDLE_BELOW_SPEED ? "idle" : speed > RUN_ABOVE_SPEED && this.actions.has("run") ? "run" : "walk";
    const blend = Math.min(1, deltaSeconds * LOCOMOTION_BLEND_RATE);
    const deathWeight = scheduledWeight(this.actions.get("death"));
    const shotWeight = scheduledWeight(this.oneShot);
    const locomotionShare = Math.max(0, 1 - Math.max(deathWeight, shotWeight));
    for (const slot of ["idle", "walk", "run"] as const) {
      this.weights[slot] += ((slot === target ? 1 : 0) - this.weights[slot]) * blend;
      this.actions.get(slot)?.setEffectiveWeight(this.weights[slot] * locomotionShare);
    }
    this.actions.get("walk")?.setEffectiveTimeScale(playbackRate(speed, WALK_CLIP_SPEED));
    this.actions.get("run")?.setEffectiveTimeScale(playbackRate(speed, RUN_CLIP_SPEED));
    this.mixer.update(deltaSeconds);
  }
}

/** Unrigged meshes (crawlers, elementals, dummies): bob, lunge, shudder and topple procedurally. */
export class StaticMeshAnimator implements Animator {
  private readonly flash: HitFlash;
  private clock = Math.random() * 10;
  private action: { kind: ActionKind; elapsed: number } | null = null;
  private dead = false;
  private deathProgress = 0;

  constructor(private readonly pivot: THREE.Object3D, private readonly height: number) {
    this.flash = new HitFlash(pivot);
  }

  get isDead(): boolean {
    return this.dead;
  }

  play(kind: ActionKind): void {
    this.action = { kind, elapsed: 0 };
    if (kind === "hit") {
      this.flash.trigger();
    }
  }

  setDead(dead: boolean): void {
    this.dead = dead;
    if (!dead) {
      this.deathProgress = 0;
    }
  }

  update(deltaSeconds: number, speed: number, _activity: Activity): void {
    this.clock += deltaSeconds;
    this.flash.update(deltaSeconds);
    const moving = Math.min(1, speed / WALK_CLIP_SPEED);
    const bob = Math.sin(this.clock * (2 + moving * 8)) * this.height * (0.015 + moving * 0.03);
    this.pivot.position.set(0, Math.max(0, bob), 0);
    this.pivot.rotation.set(0, 0, Math.sin(this.clock * 7) * 0.05 * moving);
    this.applyAction(deltaSeconds);
    if (this.dead || this.deathProgress > 0) {
      this.deathProgress = this.dead ? Math.min(1, this.deathProgress + deltaSeconds * 1.6) : 0;
      this.pivot.rotation.x = -this.deathProgress * Math.PI * 0.5;
      this.pivot.position.y = -this.deathProgress * this.height * 0.1;
    }
  }

  private applyAction(deltaSeconds: number): void {
    const action = this.action;
    if (!action) {
      return;
    }
    action.elapsed += deltaSeconds;
    const progress = action.elapsed / 0.5;
    if (progress >= 1) {
      this.action = null;
      return;
    }
    const pulse = Math.sin(progress * Math.PI);
    if (action.kind === "attack" || action.kind === "cast") {
      this.pivot.position.z = pulse * this.height * 0.25;
      this.pivot.rotation.x = pulse * 0.25;
    } else if (action.kind === "hit") {
      this.pivot.position.x = Math.sin(progress * Math.PI * 6) * 0.08 * (1 - progress);
    } else if (action.kind === "dodge") {
      this.pivot.position.x = pulse * 0.4;
    } else {
      this.pivot.position.y += pulse * this.height * 0.2;
    }
  }
}

/** Actions report weight 1 before they ever play, so only count the ones the mixer is running. */
function scheduledWeight(action: THREE.AnimationAction | null | undefined): number {
  return action?.isScheduled() ? action.getEffectiveWeight() : 0;
}

function playbackRate(speed: number, clipSpeed: number): number {
  return THREE.MathUtils.clamp(speed / clipSpeed, MIN_PLAYBACK_RATE, MAX_PLAYBACK_RATE);
}
