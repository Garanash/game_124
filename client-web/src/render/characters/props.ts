import * as THREE from "three";
import { Headwear, Prop } from "./appearance";
import { ball, box, cone, PartKit, rod } from "./parts";

const WOOD = 0x6b4a2b;
const STEEL = 0xc8ccd4;
const GOLD = 0xe7c36a;
const LEATHER = 0x5a3a22;

/** Held items are authored hanging from the hand (local origin), with blades pointing down the arm. */
export function heldProp(kit: PartKit, prop: Prop | "shield", accent: number): THREE.Object3D | null {
  const group = new THREE.Group();
  const steel = kit.material(STEEL, { metalness: 0.6, roughness: 0.35 });
  switch (prop) {
    case "none":
      return null;
    case "sword":
      group.add(kit.mesh(box(0.06, 0.75, 0.02), steel, 0, -0.45, 0));
      group.add(kit.mesh(box(0.22, 0.04, 0.05), GOLD, 0, -0.06, 0));
      group.add(kit.mesh(rod(0.025, 0.025, 0.14), LEATHER, 0, 0.04, 0));
      group.rotation.x = -0.5;
      break;
    case "dagger":
      group.add(kit.mesh(box(0.04, 0.3, 0.015), steel, 0, -0.2, 0));
      group.add(kit.mesh(box(0.1, 0.03, 0.04), LEATHER, 0, -0.04, 0));
      group.rotation.x = -0.6;
      break;
    case "axe":
      group.add(kit.mesh(rod(0.025, 0.025, 0.8), WOOD, 0, -0.3, 0));
      group.add(kit.mesh(box(0.04, 0.22, 0.24), steel, 0, -0.62, 0.1));
      group.rotation.x = -0.5;
      break;
    case "staff":
      group.add(kit.mesh(rod(0.03, 0.035, 1.7), WOOD, 0, 0.25, 0));
      group.add(kit.glow(ball(0.08, 0), accent, 0, 1.15, 0, 2));
      break;
    case "cane":
      group.add(kit.mesh(rod(0.022, 0.022, 0.95), WOOD, 0, -0.42, 0.05));
      group.add(kit.mesh(ball(0.04, 0), GOLD, 0, 0.05, 0.05));
      break;
    case "spear":
      group.add(kit.mesh(rod(0.025, 0.025, 2.0), WOOD, 0, 0.35, 0));
      group.add(kit.mesh(cone(0.05, 0.25, 4), steel, 0, 1.45, 0));
      break;
    case "hammer":
      group.add(kit.mesh(rod(0.025, 0.025, 0.5), WOOD, 0, -0.18, 0));
      group.add(kit.mesh(box(0.12, 0.12, 0.22), STEEL, 0, -0.42, 0));
      group.rotation.x = -0.6;
      break;
    case "gavel":
      group.add(kit.mesh(rod(0.02, 0.02, 0.3), WOOD, 0, -0.12, 0));
      group.add(kit.mesh(rod(0.05, 0.05, 0.16, 8), WOOD, 0, -0.28, 0).rotateZ(Math.PI / 2));
      group.rotation.x = -0.6;
      break;
    case "book":
      group.add(kit.mesh(box(0.2, 0.26, 0.06), accent, 0, -0.08, 0.06));
      group.add(kit.mesh(box(0.18, 0.24, 0.065), 0xf0e6c8, 0.012, -0.08, 0.06));
      break;
    case "mug":
      group.add(kit.mesh(rod(0.06, 0.055, 0.14, 8), WOOD, 0, -0.06, 0.06));
      group.add(kit.mesh(rod(0.055, 0.055, 0.02, 8), 0xf4f0d8, 0, 0.01, 0.06));
      break;
    case "scales":
      group.add(kit.mesh(rod(0.012, 0.012, 0.3), GOLD, 0, -0.15, 0.05));
      group.add(kit.mesh(box(0.36, 0.02, 0.02), GOLD, 0, -0.02, 0.05));
      group.add(kit.mesh(rod(0.07, 0.05, 0.02, 8), GOLD, -0.17, -0.12, 0.05));
      group.add(kit.mesh(rod(0.07, 0.05, 0.02, 8), GOLD, 0.17, -0.12, 0.05));
      break;
    case "bow": {
      const limb = kit.mesh(new THREE.TorusGeometry(0.42, 0.02, 4, 12, Math.PI), WOOD, 0, -0.1, 0.05);
      limb.rotation.z = Math.PI / 2;
      group.add(limb);
      group.add(kit.mesh(box(0.005, 0.84, 0.005), 0xf0f0e0, 0.0, -0.1, 0.05));
      break;
    }
    case "torch":
      group.add(kit.mesh(rod(0.025, 0.03, 0.5), WOOD, 0, 0.05, 0.05));
      group.add(kit.glow(cone(0.07, 0.2, 5), 0xff8a2a, 0, 0.38, 0.05, 2.5));
      break;
    case "lantern":
      group.add(kit.mesh(rod(0.005, 0.005, 0.12), 0x3a3a3a, 0, -0.06, 0.04));
      group.add(kit.glow(box(0.1, 0.14, 0.1), 0xffd27a, 0, -0.2, 0.04, 1.8));
      break;
    case "shield":
      group.add(kit.mesh(rod(0.28, 0.28, 0.05, 10), accent, 0, -0.3, 0.12).rotateX(Math.PI / 2));
      group.add(kit.mesh(rod(0.08, 0.08, 0.07, 8), GOLD, 0, -0.3, 0.16).rotateX(Math.PI / 2));
      break;
  }
  return group;
}

/** Headwear is placed relative to the head centre; radius is the head sphere radius. */
export function headwear(kit: PartKit, kind: Headwear, radius: number, color: number, trim: number): THREE.Object3D | null {
  const group = new THREE.Group();
  switch (kind) {
    case "none":
      return null;
    case "hood":
      group.add(kit.mesh(new THREE.SphereGeometry(radius * 1.22, 8, 6, 0, Math.PI * 2, 0, Math.PI * 0.62), color, 0, radius * 0.05, -radius * 0.12));
      group.add(kit.mesh(cone(radius * 0.5, radius * 0.8, 5), color, 0, radius * 0.6, -radius * 0.9).rotateX(-1.2));
      break;
    case "wizard_hat":
      group.add(kit.mesh(rod(radius * 1.9, radius * 1.9, 0.03, 10), color, 0, radius * 0.75, 0));
      group.add(kit.mesh(cone(radius * 1.0, radius * 3.2, 8), color, 0, radius * 2.3, -radius * 0.2).rotateX(-0.15));
      group.add(kit.mesh(rod(radius * 1.02, radius * 1.02, 0.05, 10), trim, 0, radius * 0.85, 0));
      break;
    case "helmet":
      group.add(kit.mesh(new THREE.SphereGeometry(radius * 1.15, 8, 6, 0, Math.PI * 2, 0, Math.PI * 0.55), kit.material(0xb8bcc8, { metalness: 0.7, roughness: 0.35 }), 0, radius * 0.1, 0));
      group.add(kit.mesh(box(radius * 0.18, radius * 0.8, radius * 0.2), 0xb8bcc8, 0, -radius * 0.05, radius * 1.05));
      group.add(kit.mesh(cone(radius * 0.18, radius * 0.7, 4), trim, 0, radius * 1.4, 0));
      break;
    case "cap":
      group.add(kit.mesh(rod(radius * 1.05, radius * 1.1, radius * 0.45, 10), color, 0, radius * 0.75, 0));
      group.add(kit.mesh(box(radius * 1.2, 0.02, radius * 0.7), color, 0, radius * 0.55, radius * 0.95));
      break;
    case "top_hat":
      group.add(kit.mesh(rod(radius * 1.6, radius * 1.6, 0.03, 12), 0x1a1a1a, 0, radius * 0.75, 0));
      group.add(kit.mesh(rod(radius * 0.9, radius * 0.9, radius * 1.6, 12), 0x1a1a1a, 0, radius * 1.55, 0));
      group.add(kit.mesh(rod(radius * 0.92, radius * 0.92, radius * 0.25, 12), trim, 0, radius * 0.95, 0));
      break;
    case "circlet":
      group.add(kit.mesh(new THREE.TorusGeometry(radius * 1.02, 0.025, 4, 16), kit.material(GOLD, { metalness: 0.8, roughness: 0.3 }), 0, radius * 0.45, 0).rotateX(Math.PI / 2));
      group.add(kit.glow(ball(0.035, 0), 0x6ad1ff, 0, radius * 0.45, radius * 1.02));
      break;
    case "crown":
      group.add(kit.mesh(rod(radius * 1.05, radius * 1.0, radius * 0.5, 8, ), kit.material(GOLD, { metalness: 0.8, roughness: 0.3 }), 0, radius * 0.9, 0));
      for (let index = 0; index < 6; index++) {
        const angle = (index / 6) * Math.PI * 2;
        group.add(kit.mesh(cone(radius * 0.14, radius * 0.45, 4), GOLD, Math.sin(angle) * radius, radius * 1.35, Math.cos(angle) * radius));
      }
      break;
    case "bandana":
      group.add(kit.mesh(rod(radius * 1.04, radius * 1.04, radius * 0.35, 10), color, 0, radius * 0.45, 0));
      group.add(kit.mesh(box(0.04, radius * 0.6, 0.04), color, 0, radius * 0.1, -radius * 1.05).rotateX(0.5));
      break;
  }
  return group;
}
