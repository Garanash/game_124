import * as THREE from "three";
import { CSS2DObject } from "three/addons/renderers/CSS2DRenderer.js";

const FLOAT_SECONDS = 1.3;
const FLOAT_RISE_METERS = 1.4;
const PROJECTILE_SPEED_METERS_PER_SECOND = 16;
const BURST_SECONDS = 0.6;
const SPARKLE_SECONDS = 1.0;

export type FloatKind = "damage" | "crit" | "heal" | "miss" | "status" | "death";

/** Indexed by Realmforge.Rules DamageType: slashing, piercing, bludgeoning, fire, cold, lightning, poison, necrotic, radiant, force, psychic. */
const DAMAGE_COLORS = [0xe8e8e8, 0xe8e8e8, 0xe8e8e8, 0xff7a2a, 0x8ad8ff, 0xfff07a, 0x9aff4a, 0x9a5aff, 0xffe8a0, 0xb8a0ff, 0xff6ad8];
const PHYSICAL_DAMAGE_TYPES = 3;

export function isPhysical(damageType: number): boolean {
  return damageType < PHYSICAL_DAMAGE_TYPES;
}

interface Effect {
  update(deltaSeconds: number): boolean;
  dispose(): void;
}

/** Short-lived visual feedback for combat events; everything removes itself when finished. */
export class CombatEffects {
  private readonly effects: Effect[] = [];

  constructor(private readonly scene: THREE.Scene) {}

  floatText(position: THREE.Vector3, text: string, kind: FloatKind): void {
    const element = document.createElement("div");
    element.className = `float-text ${kind}`;
    element.textContent = text;
    const label = new CSS2DObject(element);
    const start = position.clone();
    start.x += (Math.random() - 0.5) * 0.4;
    label.position.copy(start);
    this.scene.add(label);
    let age = 0;
    this.effects.push({
      update: (delta) => {
        age += delta;
        const progress = age / FLOAT_SECONDS;
        label.position.y = start.y + FLOAT_RISE_METERS * (1 - Math.pow(1 - progress, 2));
        element.style.opacity = String(progress < 0.7 ? 1 : 1 - (progress - 0.7) / 0.3);
        return progress < 1;
      },
      dispose: () => this.scene.remove(label),
    });
  }

  /** Flies a glowing bolt and calls onArrive when it reaches the target. */
  projectile(from: THREE.Vector3, to: THREE.Vector3, damageType: number, onArrive?: () => void): void {
    const color = damageColor(damageType);
    const bolt = new THREE.Mesh(new THREE.IcosahedronGeometry(0.22, 1), new THREE.MeshBasicMaterial({ color }));
    const halo = new THREE.Mesh(new THREE.IcosahedronGeometry(0.45, 1), new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.35, depthWrite: false }));
    bolt.add(halo);
    const light = new THREE.PointLight(color, 6, 6);
    bolt.add(light);
    bolt.position.copy(from);
    this.scene.add(bolt);
    const distance = from.distanceTo(to);
    const duration = Math.max(0.15, distance / PROJECTILE_SPEED_METERS_PER_SECOND);
    let age = 0;
    this.effects.push({
      update: (delta) => {
        age += delta;
        const progress = Math.min(1, age / duration);
        bolt.position.lerpVectors(from, to, progress);
        bolt.position.y += Math.sin(progress * Math.PI) * distance * 0.12;
        halo.scale.setScalar(1 + Math.sin(age * 30) * 0.2);
        if (progress >= 1) {
          this.burst(to, damageType, 1.2);
          onArrive?.();
        }
        return progress < 1;
      },
      dispose: () => {
        this.scene.remove(bolt);
        bolt.geometry.dispose();
      },
    });
  }

  burst(position: THREE.Vector3, damageType: number, radius: number): void {
    const color = damageColor(damageType);
    const ring = new THREE.Mesh(
      new THREE.RingGeometry(0.6, 1, 32),
      new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.9, side: THREE.DoubleSide, depthWrite: false }));
    ring.rotation.x = -Math.PI / 2;
    ring.position.set(position.x, 0.1, position.z);
    const flash = new THREE.Mesh(new THREE.IcosahedronGeometry(0.6, 1), new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.8, depthWrite: false }));
    flash.position.copy(position);
    this.scene.add(ring, flash);
    let age = 0;
    this.effects.push({
      update: (delta) => {
        age += delta;
        const progress = age / BURST_SECONDS;
        ring.scale.setScalar(0.3 + progress * radius * 1.6);
        flash.scale.setScalar(1 + progress * radius);
        (ring.material as THREE.MeshBasicMaterial).opacity = 0.9 * (1 - progress);
        (flash.material as THREE.MeshBasicMaterial).opacity = 0.8 * (1 - progress);
        return progress < 1;
      },
      dispose: () => this.scene.remove(ring, flash),
    });
  }

  sparkle(position: THREE.Vector3, color: number): void {
    const group = new THREE.Group();
    const motes: Array<{ mesh: THREE.Mesh; speed: number; angle: number; radius: number }> = [];
    const material = new THREE.MeshBasicMaterial({ color, transparent: true, depthWrite: false });
    for (let index = 0; index < 14; index++) {
      const mesh = new THREE.Mesh(new THREE.OctahedronGeometry(0.07, 0), material);
      motes.push({ mesh, speed: 1 + Math.random() * 1.5, angle: Math.random() * Math.PI * 2, radius: 0.3 + Math.random() * 0.4 });
      group.add(mesh);
    }
    group.position.set(position.x, 0, position.z);
    this.scene.add(group);
    let age = 0;
    this.effects.push({
      update: (delta) => {
        age += delta;
        const progress = age / SPARKLE_SECONDS;
        for (const mote of motes) {
          const angle = mote.angle + age * 3;
          mote.mesh.position.set(Math.cos(angle) * mote.radius, age * mote.speed * 1.6, Math.sin(angle) * mote.radius);
        }
        material.opacity = 1 - progress;
        return progress < 1;
      },
      dispose: () => this.scene.remove(group),
    });
  }

  /** A streak along the swing so melee hits read even when zoomed out. */
  slash(position: THREE.Vector3, heading: number, color = 0xffffff): void {
    const arc = new THREE.Mesh(
      new THREE.RingGeometry(0.7, 0.85, 20, 1, -0.9, 1.8),
      new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.9, side: THREE.DoubleSide, depthWrite: false }));
    arc.position.copy(position);
    arc.rotation.set(-Math.PI / 2 + 0.4, 0, heading + Math.PI / 2);
    this.scene.add(arc);
    let age = 0;
    this.effects.push({
      update: (delta) => {
        age += delta;
        const progress = age / 0.3;
        arc.scale.setScalar(0.8 + progress * 0.5);
        (arc.material as THREE.MeshBasicMaterial).opacity = 0.9 * (1 - progress);
        return progress < 1;
      },
      dispose: () => this.scene.remove(arc),
    });
  }

  update(deltaSeconds: number): void {
    for (let index = this.effects.length - 1; index >= 0; index--) {
      if (!this.effects[index].update(deltaSeconds)) {
        this.effects[index].dispose();
        this.effects.splice(index, 1);
      }
    }
  }

  clear(): void {
    this.effects.forEach((effect) => effect.dispose());
    this.effects.length = 0;
  }
}

export function damageColor(damageType: number): number {
  return DAMAGE_COLORS[damageType] ?? 0xffffff;
}
