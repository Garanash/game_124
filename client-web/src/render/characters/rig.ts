import * as THREE from "three";

export type Locomotion = "biped" | "quadruped" | "hover" | "static" | "crawler";

/** What a character is busy with while standing still; drives the idle animation loop. */
export type Activity =
  | "idle" | "hammer" | "trade" | "teach" | "talk" | "drink" | "gavel" | "sleep"
  | "lookout" | "graze" | "gather" | "sweep" | "read" | "cheer" | "sit";

export type ActionKind = "attack" | "cast" | "hit" | "dodge" | "cheer";

/**
 * Joints of an assembled model. Limbs are pivots: their children hang along -Y, so a negative
 * rotation.x swings them forward (models face +Z).
 */
export interface Rig {
  root: THREE.Group;
  body: THREE.Group;
  torso?: THREE.Object3D;
  head?: THREE.Object3D;
  armL?: THREE.Object3D;
  armR?: THREE.Object3D;
  extraArms: THREE.Object3D[];
  legL?: THREE.Object3D;
  legR?: THREE.Object3D;
  /** Quadruped legs in order front-left, front-right, back-left, back-right; crawlers may have more. */
  legs: THREE.Object3D[];
  tail?: THREE.Object3D;
  wingL?: THREE.Object3D;
  wingR?: THREE.Object3D;
  cloak?: THREE.Object3D;
  height: number;
  locomotion: Locomotion;
  /** Metres covered by one full gait cycle; longer legs take longer strides. */
  stride: number;
  materials: THREE.MeshStandardMaterial[];
}

interface ActiveAction {
  kind: ActionKind;
  elapsed: number;
  duration: number;
}

const ACTION_SECONDS: Record<ActionKind, number> = { attack: 0.55, cast: 0.8, hit: 0.35, dodge: 0.45, cheer: 1.2 };
const WALK_BLEND_SPEED = 1.2;
const DEATH_SECONDS = 0.7;
const HIT_FLASH_COLOR = new THREE.Color(0xff3a2a);
const TAU = Math.PI * 2;

/** Procedural animation layered as locomotion, then the idle activity, then one-shot combat actions. */
export class CharacterAnimator {
  private phase = Math.random() * TAU;
  private clock = Math.random() * 10;
  private walkWeight = 0;
  private action: ActiveAction | null = null;
  private deathProgress = 0;
  private dead = false;
  private flash = 0;
  private flashApplied = false;

  constructor(readonly rig: Rig) {}

  play(kind: ActionKind): void {
    this.action = { kind, elapsed: 0, duration: ACTION_SECONDS[kind] };
    if (kind === "hit") {
      this.flash = 1;
    }
  }

  setDead(dead: boolean): void {
    this.dead = dead;
    if (!dead) {
      this.deathProgress = 0;
    }
  }

  get isDead(): boolean {
    return this.dead;
  }

  update(deltaSeconds: number, speed: number, activity: Activity): void {
    const rig = this.rig;
    this.clock += deltaSeconds;
    this.phase = (this.phase + (speed / rig.stride) * TAU * deltaSeconds) % TAU;
    const targetWalk = Math.min(1, speed / WALK_BLEND_SPEED);
    this.walkWeight += (targetWalk - this.walkWeight) * Math.min(1, deltaSeconds * 10);

    resetPose(rig);
    this.applyLocomotion();
    this.applyActivity(activity, 1 - this.walkWeight);
    this.applyAction(deltaSeconds);
    this.applyDeath(deltaSeconds);
    this.applyFlash(deltaSeconds);
  }

  private applyLocomotion(): void {
    const rig = this.rig;
    const weight = this.walkWeight;
    const swing = Math.sin(this.phase);
    const breathe = Math.sin(this.clock * 2.2) * 0.012;
    switch (rig.locomotion) {
      case "biped":
        rotateX(rig.legL, swing * 0.7 * weight);
        rotateX(rig.legR, -swing * 0.7 * weight);
        rotateX(rig.armL, -swing * 0.55 * weight);
        rotateX(rig.armR, swing * 0.55 * weight);
        rig.body.position.y = Math.abs(Math.cos(this.phase)) * 0.06 * weight;
        rig.body.rotation.z = swing * 0.03 * weight;
        rig.legs.forEach((leg, index) => rotateX(leg, (index % 2 === 0 ? swing : -swing) * (index < 2 ? 0.6 : -0.6) * weight));
        break;
      case "quadruped":
        rig.legs.forEach((leg, index) => {
          const diagonal = index === 0 || index === 3 ? 1 : -1;
          rotateX(leg, swing * diagonal * 0.65 * weight);
        });
        rig.body.position.y = Math.abs(Math.cos(this.phase)) * 0.05 * weight;
        rotateX(rig.head, Math.sin(this.phase * 2) * 0.06 * weight);
        rotateX(rig.armL, -swing * 0.5 * weight);
        rotateX(rig.armR, swing * 0.5 * weight);
        break;
      case "crawler":
        rig.legs.forEach((leg, index) => {
          const side = index % 2 === 0 ? 1 : -1;
          leg.rotation.y += Math.sin(this.phase + index * 1.3) * 0.35 * weight * side;
          leg.rotation.z += Math.max(0, Math.cos(this.phase + index * 1.3)) * 0.25 * weight * side;
        });
        rig.body.position.y = Math.abs(Math.sin(this.phase * 2)) * 0.03 * weight;
        break;
      case "hover":
        rig.body.position.y = 0.35 + Math.sin(this.clock * 2.4) * 0.12;
        rig.body.rotation.x = 0.25 * weight;
        rotateX(rig.armL, -0.3 * weight);
        rotateX(rig.armR, -0.3 * weight);
        break;
      case "static":
        break;
    }
    if (rig.torso) {
      rig.torso.scale.y = 1 + breathe;
    }
    if (rig.tail) {
      rig.tail.rotation.y = Math.sin(this.clock * 3 + this.phase) * (0.25 + 0.2 * weight);
    }
    if (rig.cloak) {
      rig.cloak.rotation.x = 0.08 + 0.25 * weight + Math.sin(this.clock * 2) * 0.03;
    }
    const flapRate = rig.locomotion === "hover" ? 14 : 3;
    const flapRange = rig.locomotion === "hover" ? 0.7 : 0.12;
    if (rig.wingL) {
      rig.wingL.rotation.y = 0.5 + Math.sin(this.clock * flapRate) * flapRange;
    }
    if (rig.wingR) {
      rig.wingR.rotation.y = -0.5 - Math.sin(this.clock * flapRate) * flapRange;
    }
  }

  private applyActivity(activity: Activity, weight: number): void {
    if (weight <= 0.01) {
      return;
    }
    const rig = this.rig;
    const t = this.clock;
    switch (activity) {
      case "hammer": {
        const strike = Math.pow(Math.max(0, Math.sin(t * 4)), 3);
        rotateX(rig.armR, (-2.4 + strike * 1.9) * weight);
        rotateX(rig.torso, (0.15 + strike * 0.1) * weight);
        break;
      }
      case "trade":
        rotateX(rig.armR, (-0.9 + Math.sin(t * 2.5) * 0.25) * weight);
        rotateZ(rig.armR, Math.sin(t * 1.3) * 0.2 * weight);
        rotateY(rig.head, Math.sin(t * 0.7) * 0.5 * weight);
        break;
      case "teach":
        rotateX(rig.armR, (-1.6 + Math.sin(t * 1.7) * 0.4) * weight);
        rotateX(rig.armL, -0.5 * weight);
        rotateY(rig.head, Math.sin(t * 0.9) * 0.35 * weight);
        rotateY(rig.torso, Math.sin(t * 0.5) * 0.2 * weight);
        break;
      case "talk":
        rotateX(rig.armR, (-0.5 + Math.sin(t * 3.1) * 0.35) * weight);
        rotateX(rig.armL, (-0.3 + Math.sin(t * 2.3 + 1) * 0.25) * weight);
        rotateX(rig.head, Math.sin(t * 4) * 0.08 * weight);
        break;
      case "drink": {
        const sip = Math.max(0, Math.sin(t * 0.9));
        rotateX(rig.armR, (-0.6 - sip * 1.6) * weight);
        rotateX(rig.head, -sip * 0.35 * weight);
        rig.body.rotation.z += Math.sin(t * 0.8) * 0.04 * weight;
        break;
      }
      case "gavel": {
        const bang = Math.pow(Math.max(0, Math.sin(t * 2.2)), 6);
        rotateX(rig.armR, (-1.9 + bang * 1.2) * weight);
        rotateX(rig.armL, (-1.1 + Math.sin(t * 1.1) * 0.3) * weight);
        break;
      }
      case "sleep":
        rig.body.position.y -= 0.35 * weight;
        rotateX(rig.legL, -1.4 * weight);
        rotateX(rig.legR, -1.4 * weight);
        rotateX(rig.head, (0.45 + Math.sin(t * 0.8) * 0.05) * weight);
        rotateX(rig.torso, 0.25 * weight);
        break;
      case "sit":
        rig.body.position.y -= 0.35 * weight;
        rotateX(rig.legL, -1.4 * weight);
        rotateX(rig.legR, -1.4 * weight);
        rotateX(rig.armR, -0.4 * weight);
        break;
      case "lookout":
        rotateY(rig.head, Math.sin(t * 0.6) * 0.8 * weight);
        rotateX(rig.armR, -0.25 * weight);
        break;
      case "graze": {
        const down = Math.max(0, Math.sin(t * 0.5));
        rotateX(rig.head, down * 0.9 * weight);
        break;
      }
      case "gather":
        rotateX(rig.torso, 0.55 * weight);
        rotateX(rig.armR, (-1 + Math.sin(t * 6) * 0.4) * weight);
        rotateX(rig.armL, (-1 + Math.cos(t * 6) * 0.4) * weight);
        break;
      case "sweep":
        rotateX(rig.armR, -0.8 * weight);
        rotateX(rig.armL, -0.8 * weight);
        rotateY(rig.torso, Math.sin(t * 3) * 0.4 * weight);
        break;
      case "read":
        rotateX(rig.armL, -1.1 * weight);
        rotateX(rig.armR, -1.0 * weight);
        rotateX(rig.head, 0.35 * weight);
        break;
      case "cheer":
        rotateX(rig.armR, (-2.8 + Math.sin(t * 6) * 0.2) * weight);
        rotateX(rig.armL, (-2.8 + Math.cos(t * 6) * 0.2) * weight);
        rig.body.position.y += Math.abs(Math.sin(t * 6)) * 0.12 * weight;
        break;
      case "idle":
        rotateY(rig.head, Math.sin(t * 0.4) * 0.25 * weight);
        rotateX(rig.armL, Math.sin(t * 1.1) * 0.04 * weight);
        rotateX(rig.armR, -Math.sin(t * 1.1) * 0.04 * weight);
        break;
    }
  }

  private applyAction(deltaSeconds: number): void {
    const action = this.action;
    if (!action) {
      return;
    }
    action.elapsed += deltaSeconds;
    const progress = Math.min(1, action.elapsed / action.duration);
    const rig = this.rig;
    switch (action.kind) {
      case "attack": {
        const windUp = progress < 0.4 ? progress / 0.4 : 1 - (progress - 0.4) / 0.6;
        const strike = progress < 0.4 ? 0 : Math.sin(((progress - 0.4) / 0.6) * Math.PI);
        rotateX(rig.armR, -2.6 * windUp + 1.2 * strike);
        rotateX(rig.torso, -0.2 * windUp + 0.35 * strike);
        rig.body.position.z += strike * 0.45;
        rig.legs.slice(0, 2).forEach((leg) => rotateX(leg, -0.9 * windUp));
        if (rig.locomotion !== "biped") {
          rotateX(rig.head, 0.5 * strike - 0.3 * windUp);
        }
        break;
      }
      case "cast": {
        const raise = Math.sin(progress * Math.PI);
        rotateX(rig.armR, -2.7 * raise);
        rotateX(rig.armL, -2.7 * raise);
        rig.body.position.y += raise * 0.15;
        rotateX(rig.head, -0.3 * raise);
        break;
      }
      case "hit": {
        const recoil = Math.sin(progress * Math.PI);
        rig.body.rotation.x -= recoil * 0.35;
        rig.body.position.z -= recoil * 0.25;
        rotateX(rig.head, -0.4 * recoil);
        break;
      }
      case "dodge": {
        const lean = Math.sin(progress * Math.PI);
        rig.body.position.x += lean * 0.6;
        rig.body.rotation.z -= lean * 0.4;
        break;
      }
      case "cheer":
        this.applyActivity("cheer", Math.sin(progress * Math.PI));
        break;
    }
    if (progress >= 1) {
      this.action = null;
    }
  }

  private applyDeath(deltaSeconds: number): void {
    this.deathProgress = this.dead ? Math.min(1, this.deathProgress + deltaSeconds / DEATH_SECONDS) : 0;
    if (this.deathProgress <= 0) {
      return;
    }
    const eased = 1 - Math.pow(1 - this.deathProgress, 3);
    const rig = this.rig;
    if (rig.locomotion === "biped" || rig.locomotion === "hover") {
      rig.body.rotation.x = -Math.PI / 2 * eased;
      rig.body.position.y = 0.15 * eased;
    } else {
      rig.body.rotation.z = Math.PI / 2 * eased;
      rig.body.position.y = 0.1 * eased;
    }
  }

  private applyFlash(deltaSeconds: number): void {
    if (this.flash <= 0 && !this.flashApplied) {
      return;
    }
    this.flash = Math.max(0, this.flash - deltaSeconds * 4);
    for (const material of this.rig.materials) {
      const base = material.userData.baseEmissive as THREE.Color;
      material.emissive.copy(base).lerp(HIT_FLASH_COLOR, this.flash);
      material.emissiveIntensity = Math.max(material.userData.baseEmissiveIntensity as number, this.flash * 1.5);
    }
    this.flashApplied = this.flash > 0;
  }
}

const restPoses = new WeakMap<THREE.Object3D, THREE.Euler>();

/** Remembers the authored pose on first use so every frame starts from the same rest rotation. */
function resetPose(rig: Rig): void {
  rig.body.position.set(0, 0, 0);
  rig.body.rotation.set(0, 0, 0);
  const joints = [rig.torso, rig.head, rig.armL, rig.armR, rig.legL, rig.legR, rig.tail, rig.wingL, rig.wingR, rig.cloak, ...rig.legs, ...rig.extraArms];
  for (const node of joints) {
    if (!node) {
      continue;
    }
    let rest = restPoses.get(node);
    if (!rest) {
      rest = node.rotation.clone();
      restPoses.set(node, rest);
    }
    node.rotation.copy(rest);
  }
}

function rotateX(node: THREE.Object3D | undefined, angle: number): void {
  if (node) {
    node.rotation.x += angle;
  }
}

function rotateY(node: THREE.Object3D | undefined, angle: number): void {
  if (node) {
    node.rotation.y += angle;
  }
}

function rotateZ(node: THREE.Object3D | undefined, angle: number): void {
  if (node) {
    node.rotation.z += angle;
  }
}
