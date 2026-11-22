import * as THREE from "three";
import { darken, Outfit, RaceLook } from "./appearance";
import { ball, box, cone, joint, PartKit, rod } from "./parts";
import { headwear, heldProp } from "./props";
import { Rig } from "./rig";

/** Proportions are authored for a 1.8 m human and scaled to the race height afterwards. */
const REFERENCE_HEIGHT = 1.8;
const HIP_HEIGHT = 0.9;
const LEG_LENGTH = 0.84;
const SHOULDER_HEIGHT = 0.58;
const ARM_LENGTH = 0.64;
const NECK_HEIGHT = 0.7;
const HEAD_RADIUS = 0.17;
const BOOT_COLOR = 0x3a2818;
const HOOF_COLOR = 0x2a1e14;
const EYE_COLOR = 0x1a1410;
const TUSK_COLOR = 0xf4ecd8;
const BEAK_COLOR = 0xe8b03a;
const METAL = 0xb8bcc8;

interface Frame {
  kit: PartKit;
  look: RaceLook;
  outfit: Outfit;
  bulk: number;
  limbColor: number;
}

export function buildHumanoid(look: RaceLook, outfit: Outfit): Rig {
  const kit = new PartKit();
  const frame: Frame = { kit, look, outfit, bulk: look.bulk, limbColor: look.covering ?? look.skin };
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  const scaled = new THREE.Group();
  scaled.scale.setScalar(look.height / REFERENCE_HEIGHT);
  body.add(scaled);

  const isCentaur = look.body === "centaur";
  const torsoBase = isCentaur ? 1.25 : HIP_HEIGHT;
  const torso = joint(0, torsoBase, isCentaur ? 0.15 : 0);
  torso.rotation.x = look.hunch ?? 0;
  scaled.add(torso);
  buildTorso(frame, torso);

  const head = joint(0, NECK_HEIGHT, 0);
  torso.add(head);
  buildHead(frame, head);

  const armSpread = 0.23 * frame.bulk + 0.06;
  const armL = buildArm(frame, armSpread, SHOULDER_HEIGHT, outfit.leftHand);
  const armR = buildArm(frame, -armSpread, SHOULDER_HEIGHT, outfit.rightHand);
  torso.add(armL, armR);
  const extraArms: THREE.Object3D[] = [];
  if (look.body === "insect") {
    for (const side of [1, -1]) {
      const lower = buildArm(frame, side * armSpread * 0.9, SHOULDER_HEIGHT - 0.22, "none");
      lower.rotation.x = -0.5;
      torso.add(lower);
      extraArms.push(lower);
    }
  }

  const rig: Rig = {
    root, body, torso, head, armL, armR, extraArms, legs: [],
    height: look.height,
    locomotion: isCentaur ? "quadruped" : "biped",
    stride: Math.max(0.9, look.height * 0.95),
    materials: kit.materials,
  };

  if (isCentaur) {
    buildHorseBody(frame, scaled, rig);
  } else {
    rig.legL = buildLeg(frame, 0.11 * frame.bulk);
    rig.legR = buildLeg(frame, -0.11 * frame.bulk);
    scaled.add(rig.legL, rig.legR);
    rig.tail = buildTail(frame, scaled, HIP_HEIGHT - 0.05, -0.14 * frame.bulk);
  }
  buildBackAttachments(frame, torso, rig);
  if (look.glow) {
    const aura = new THREE.Mesh(
      new THREE.RingGeometry(0.35, 0.6, 24),
      new THREE.MeshBasicMaterial({ color: look.glow, transparent: true, opacity: 0.35, side: THREE.DoubleSide, depthWrite: false }));
    aura.rotation.x = -Math.PI / 2;
    aura.position.y = 0.03;
    aura.name = "aura";
    root.add(aura);
  }
  return rig;
}

function buildTorso(frame: Frame, torso: THREE.Group): void {
  const { kit, outfit, bulk, look } = frame;
  const construct = look.body === "construct";
  const chestWidth = 0.46 * bulk;
  const chestDepth = 0.27 * bulk;
  if (construct) {
    torso.add(kit.mesh(box(chestWidth * 1.05, 0.62, chestDepth * 1.1), look.covering ?? look.skin, 0, 0.33, 0));
    torso.add(kit.mesh(box(chestWidth * 0.7, 0.3, 0.04), darken(look.skin, 0.7), 0, 0.42, chestDepth * 0.58));
    torso.add(kit.glow(ball(0.05, 0), look.eyes ?? 0x6ad8ff, 0, 0.42, chestDepth * 0.62));
  } else {
    torso.add(kit.mesh(box(chestWidth, 0.62, chestDepth), outfit.tunic, 0, 0.33, 0));
  }
  torso.add(kit.mesh(box(chestWidth * 0.95, 0.2, chestDepth * 0.95), outfit.pants, 0, 0.02, 0));
  torso.add(kit.mesh(box(chestWidth * 1.02, 0.06, chestDepth * 1.04), outfit.trim, 0, 0.1, 0));
  if (outfit.armor) {
    const metal = kit.material(METAL, { metalness: 0.65, roughness: 0.35 });
    torso.add(kit.mesh(box(chestWidth * 1.08, 0.42, chestDepth * 1.12), metal, 0, 0.4, 0));
    for (const side of [1, -1]) {
      torso.add(kit.mesh(ball(0.12 * bulk, 0), metal, side * (chestWidth / 2 + 0.03), SHOULDER_HEIGHT, 0));
    }
  }
  if (outfit.robe) {
    torso.add(kit.mesh(rod(0.25 * bulk, 0.4 * bulk, 0.78, 8), outfit.tunic, 0, -0.36, 0));
    torso.add(kit.mesh(rod(0.4 * bulk, 0.41 * bulk, 0.05, 8), outfit.trim, 0, -0.74, 0));
  }
  if (outfit.apron !== undefined) {
    torso.add(kit.mesh(box(chestWidth * 0.78, 0.75, 0.02), outfit.apron, 0, 0.05, chestDepth / 2 + 0.015));
  }
  torso.add(kit.mesh(rod(0.06, 0.07, 0.14), look.skin, 0, NECK_HEIGHT - 0.06, 0));
}

function buildHead(frame: Frame, head: THREE.Group): void {
  const { kit, look, outfit } = frame;
  const radius = HEAD_RADIUS * look.headScale;
  const center = joint(0, radius * 0.95, 0);
  head.add(center);
  const construct = look.body === "construct";
  const skull = construct
    ? kit.mesh(box(radius * 1.8, radius * 1.7, radius * 1.7), look.covering ?? look.skin)
    : kit.mesh(ball(radius, 1), look.skin);
  center.add(skull);

  const eyeColor = look.eyes ?? EYE_COLOR;
  const eyeSize = look.body === "insect" || look.snout === "frog" ? radius * 0.38 : radius * 0.16;
  for (const side of [1, -1]) {
    const eye = look.eyes && look.eyes !== EYE_COLOR
      ? kit.glow(ball(eyeSize, 0), eyeColor, side * radius * 0.38, radius * 0.12, radius * 0.86)
      : kit.mesh(ball(eyeSize, 0), eyeColor, side * radius * 0.38, radius * 0.12, radius * 0.86);
    center.add(eye);
  }
  addEars(frame, center, radius);
  addSnout(frame, center, radius);
  addHorns(frame, center, radius);
  addHair(frame, center, radius);
  if (look.tusks) {
    for (const side of [1, -1]) {
      center.add(kit.mesh(cone(radius * 0.08, radius * 0.35, 4), TUSK_COLOR, side * radius * 0.3, -radius * 0.35, radius * 0.85));
    }
  }
  if (look.hood) {
    const hood = kit.mesh(new THREE.CircleGeometry(radius * 1.8, 10), look.covering ?? look.skin, 0, radius * 0.1, -radius * 0.45);
    hood.scale.set(1, 1.2, 1);
    center.add(hood);
  }
  if (look.body === "insect") {
    for (const side of [1, -1]) {
      center.add(kit.mesh(rod(0.01, 0.015, radius * 2.2, 4), darken(look.skin, 0.6), side * radius * 0.3, radius * 1.6, radius * 0.3).rotateZ(side * -0.35));
      center.add(kit.mesh(cone(radius * 0.12, radius * 0.5, 4), darken(look.skin, 0.6), side * radius * 0.25, -radius * 0.7, radius * 0.8).rotateX(2.6));
    }
  }
  const hat = headwear(kit, outfit.headwear, radius, outfit.cloak ?? outfit.tunic, outfit.trim);
  if (hat) {
    center.add(hat);
  }
}

function addEars(frame: Frame, center: THREE.Group, radius: number): void {
  const { kit, look } = frame;
  const color = look.covering ?? look.skin;
  for (const side of [1, -1]) {
    switch (look.ears) {
      case "pointed":
        center.add(kit.mesh(cone(radius * 0.16, radius * 0.7, 4), look.skin, side * radius * 1.05, radius * 0.25, -radius * 0.05).rotateZ(side * -1.1));
        break;
      case "long":
        center.add(kit.mesh(cone(radius * 0.2, radius * 1.3, 4), look.skin, side * radius * 1.35, radius * 0.35, -radius * 0.1).rotateZ(side * -1.25));
        break;
      case "rabbit":
        center.add(kit.mesh(box(radius * 0.32, radius * 1.9, radius * 0.12), color, side * radius * 0.38, radius * 1.75, -radius * 0.1).rotateZ(side * -0.15));
        break;
      case "cat":
        center.add(kit.mesh(cone(radius * 0.32, radius * 0.6, 4), color, side * radius * 0.6, radius * 0.95, 0).rotateZ(side * -0.35));
        break;
      case "bear":
        center.add(kit.mesh(ball(radius * 0.32, 0), look.covering ?? darken(color, 0.6), side * radius * 0.72, radius * 0.8, 0));
        break;
      case "elephant": {
        const ear = kit.mesh(new THREE.CircleGeometry(radius * 0.95, 8), color, side * radius * 1.0, 0, -radius * 0.2);
        ear.rotation.y = side * 1.1;
        center.add(ear);
        break;
      }
      case "fin":
        center.add(kit.mesh(cone(radius * 0.35, radius * 0.8, 3), darken(look.skin, 0.8), side * radius * 1.0, radius * 0.1, -radius * 0.2).rotateZ(side * -1.4));
        break;
      case "cow":
        center.add(kit.mesh(box(radius * 0.6, radius * 0.18, radius * 0.3), color, side * radius * 1.15, radius * 0.25, 0).rotateZ(side * 0.3));
        break;
      case "round":
        center.add(kit.mesh(ball(radius * 0.18, 0), look.skin, side * radius * 0.98, radius * 0.05, 0));
        break;
      case "none":
        break;
    }
  }
}

function addSnout(frame: Frame, center: THREE.Group, radius: number): void {
  const { kit, look } = frame;
  const color = look.covering ?? look.skin;
  switch (look.snout) {
    case "big_nose":
      center.add(kit.mesh(ball(radius * 0.22, 0), darken(look.skin, 0.92), 0, -radius * 0.05, radius * 0.98));
      break;
    case "muzzle":
      center.add(kit.mesh(box(radius * 0.8, radius * 0.5, radius * 0.5), color, 0, -radius * 0.3, radius * 0.85));
      center.add(kit.mesh(ball(radius * 0.12, 0), EYE_COLOR, 0, -radius * 0.12, radius * 1.12));
      break;
    case "lizard":
      center.add(kit.mesh(box(radius * 0.85, radius * 0.5, radius * 1.1), color, 0, -radius * 0.25, radius * 1.0));
      break;
    case "bull":
      center.add(kit.mesh(box(radius * 1.0, radius * 0.6, radius * 0.7), color, 0, -radius * 0.3, radius * 0.95));
      center.add(kit.mesh(new THREE.TorusGeometry(radius * 0.16, 0.015, 4, 10), 0xe7c36a, 0, -radius * 0.55, radius * 1.3));
      break;
    case "trunk": {
      let parent: THREE.Object3D = center;
      let y = -radius * 0.1;
      let z = radius * 0.95;
      for (let segment = 0; segment < 4; segment++) {
        const part = joint(0, y, z);
        part.rotation.x = 0.35;
        part.add(kit.mesh(rod(radius * (0.22 - segment * 0.04), radius * (0.26 - segment * 0.04), radius * 0.55), color, 0, -radius * 0.25, 0));
        parent.add(part);
        parent = part;
        y = -radius * 0.5;
        z = 0;
      }
      break;
    }
    case "beak":
      center.add(kit.mesh(cone(radius * 0.32, radius * 0.9, 4), BEAK_COLOR, 0, -radius * 0.15, radius * 1.25).rotateX(Math.PI / 2 + 0.3));
      break;
    case "frog":
      center.add(kit.mesh(box(radius * 1.6, radius * 0.4, radius * 0.9), darken(look.skin, 0.9), 0, -radius * 0.45, radius * 0.45));
      break;
    case "fish":
      center.add(kit.mesh(ball(radius * 0.55, 0), color, 0, -radius * 0.25, radius * 0.75));
      center.add(kit.mesh(cone(radius * 0.4, radius * 0.9, 3), darken(color, 0.7), 0, radius * 1.0, -radius * 0.2).rotateX(-0.4));
      break;
    case "hippo":
      center.add(kit.mesh(box(radius * 1.3, radius * 0.8, radius * 0.9), color, 0, -radius * 0.35, radius * 0.85));
      break;
    case "monkey":
      center.add(kit.mesh(ball(radius * 0.48, 0), 0xe8c8a0, 0, -radius * 0.3, radius * 0.75));
      break;
    case "none":
      break;
  }
}

function addHorns(frame: Frame, center: THREE.Group, radius: number): void {
  const { kit, look } = frame;
  const hornColor = 0xe8dcc0;
  for (const side of [1, -1]) {
    switch (look.horns) {
      case "small":
        center.add(kit.mesh(cone(radius * 0.12, radius * 0.45, 4), hornColor, side * radius * 0.45, radius * 0.9, 0).rotateZ(side * -0.3));
        break;
      case "curved":
        center.add(kit.mesh(cone(radius * 0.16, radius * 0.9, 4), hornColor, side * radius * 0.45, radius * 0.85, -radius * 0.5).rotateX(-0.9));
        break;
      case "ram": {
        const curl = kit.mesh(new THREE.TorusGeometry(radius * 0.38, radius * 0.11, 4, 10, Math.PI * 1.5), hornColor, side * radius * 0.85, radius * 0.4, -radius * 0.1);
        curl.rotation.y = Math.PI / 2;
        center.add(curl);
        break;
      }
      case "bull":
        center.add(kit.mesh(cone(radius * 0.18, radius * 1.1, 5), hornColor, side * radius * 1.2, radius * 0.75, 0).rotateZ(side * -1.0));
        break;
      case "crest":
        if (side === 1) {
          for (let spike = 0; spike < 4; spike++) {
            center.add(kit.mesh(cone(radius * 0.12, radius * 0.5, 3), darken(look.skin, 0.7), 0, radius * (1.0 - spike * 0.15), -radius * spike * 0.35));
          }
        }
        break;
      case "none":
        break;
    }
  }
}

function addHair(frame: Frame, center: THREE.Group, radius: number): void {
  const { kit, look } = frame;
  const hair = look.hair;
  const cap = (): THREE.Mesh => kit.mesh(new THREE.SphereGeometry(radius * 1.07, 8, 6, 0, Math.PI * 2, 0, Math.PI * 0.55), hair, 0, radius * 0.05, -radius * 0.05);
  switch (look.hairStyle) {
    case "short":
      center.add(cap());
      break;
    case "long":
      center.add(cap());
      center.add(kit.mesh(box(radius * 1.8, radius * 2.2, radius * 0.5), hair, 0, -radius * 0.7, -radius * 0.7));
      break;
    case "ponytail":
      center.add(cap());
      center.add(kit.mesh(rod(radius * 0.18, radius * 0.08, radius * 1.8, 5), hair, 0, -radius * 0.6, -radius * 1.15).rotateX(0.35));
      break;
    case "topknot":
      center.add(kit.mesh(ball(radius * 0.35, 0), hair, 0, radius * 1.1, -radius * 0.1));
      break;
    case "mohawk":
      center.add(kit.mesh(box(radius * 0.22, radius * 0.6, radius * 1.9), hair, 0, radius * 0.85, -radius * 0.1));
      break;
    case "bun":
      center.add(cap());
      center.add(kit.mesh(ball(radius * 0.4, 0), hair, 0, radius * 0.75, -radius * 0.75));
      break;
    case "mane":
      center.add(kit.mesh(new THREE.TorusGeometry(radius * 1.05, radius * 0.42, 5, 10), hair, 0, 0, -radius * 0.2));
      break;
    case "none":
      break;
  }
  if (look.beard) {
    center.add(kit.mesh(cone(radius * 0.6, radius * 1.3, 6), hair, 0, -radius * 0.95, radius * 0.55).rotateX(Math.PI + 0.25));
  }
}

function buildArm(frame: Frame, x: number, y: number, prop: Outfit["rightHand"] | "shield"): THREE.Group {
  const { kit, outfit, bulk, look } = frame;
  const arm = joint(x, y, 0);
  const sleeve = look.body === "construct" ? (look.covering ?? look.skin) : outfit.armor ? METAL : outfit.tunic;
  arm.add(kit.mesh(rod(0.07 * bulk, 0.055 * bulk, ARM_LENGTH - 0.06), sleeve, 0, -(ARM_LENGTH - 0.06) / 2, 0));
  arm.add(kit.mesh(ball(0.065 * Math.max(1, bulk * 0.9), 0), frame.limbColor, 0, -ARM_LENGTH, 0));
  const held = heldProp(kit, prop, outfit.trim === 0xe7c36a ? 0x6ad1ff : outfit.tunic);
  if (held) {
    held.position.y = -ARM_LENGTH;
    arm.add(held);
  }
  return arm;
}

function buildLeg(frame: Frame, x: number): THREE.Group {
  const { kit, outfit, bulk, look } = frame;
  const leg = joint(x, HIP_HEIGHT, 0);
  const goat = look.goatLegs === true;
  const legColor = goat ? (look.covering ?? look.skin) : outfit.pants;
  leg.add(kit.mesh(rod(0.085 * bulk, (goat ? 0.05 : 0.07) * bulk, LEG_LENGTH - 0.06), legColor, 0, -(LEG_LENGTH - 0.06) / 2, 0));
  leg.add(kit.mesh(box(0.13 * bulk, 0.12, goat ? 0.12 : 0.24), goat ? HOOF_COLOR : BOOT_COLOR, 0, -LEG_LENGTH + 0.04, goat ? 0 : 0.04));
  return leg;
}

function buildTail(frame: Frame, parent: THREE.Group, y: number, z: number): THREE.Group | undefined {
  const { kit, look } = frame;
  const color = look.covering ?? look.skin;
  const tail = joint(0, y, z);
  switch (look.tail) {
    case "none":
      return undefined;
    case "cat":
      tail.rotation.x = -0.5;
      tail.add(kit.mesh(rod(0.03, 0.04, 0.8), color, 0, 0, -0.4).rotateX(Math.PI / 2));
      break;
    case "lion":
      tail.rotation.x = 0.4;
      tail.add(kit.mesh(rod(0.03, 0.04, 0.8), color, 0, 0, -0.4).rotateX(Math.PI / 2));
      tail.add(kit.mesh(ball(0.08, 0), look.hair, 0, 0, -0.82));
      break;
    case "lizard":
      tail.rotation.x = 0.6;
      tail.add(kit.mesh(cone(0.11, 1.0, 5), color, 0, 0, -0.5).rotateX(-Math.PI / 2));
      break;
    case "dragon":
      tail.rotation.x = 0.55;
      tail.add(kit.mesh(cone(0.14, 1.2, 5), color, 0, 0, -0.6).rotateX(-Math.PI / 2));
      tail.add(kit.mesh(cone(0.08, 0.2, 3), darken(color, 0.6), 0, 0.08, -0.4));
      break;
    case "monkey": {
      tail.rotation.x = -0.3;
      const curl = kit.mesh(new THREE.TorusGeometry(0.25, 0.03, 4, 10, Math.PI * 1.4), color, 0, 0.2, -0.3);
      curl.rotation.y = Math.PI / 2;
      tail.add(curl);
      break;
    }
    case "fluffy":
      tail.add(kit.mesh(ball(0.1, 0), 0xf4f0e8, 0, 0, -0.08));
      break;
  }
  parent.add(tail);
  return tail;
}

function buildBackAttachments(frame: Frame, torso: THREE.Group, rig: Rig): void {
  const { kit, look, outfit, bulk } = frame;
  const back = -0.15 * bulk;
  if (outfit.cloak !== undefined) {
    const cloak = joint(0, SHOULDER_HEIGHT + 0.02, back);
    cloak.add(kit.mesh(box(0.5 * bulk, 1.05, 0.03), outfit.cloak, 0, -0.52, 0));
    torso.add(cloak);
    rig.cloak = cloak;
  }
  if (look.wings === "none") {
    return;
  }
  const fairy = look.wings === "fairy";
  const wingMaterial = fairy
    ? kit.material(look.glow ?? 0xffc8f0, { emissive: look.glow ?? 0xffc8f0, emissiveIntensity: 0.6, opacity: 0.55 })
    : kit.material(look.covering ?? look.skin);
  for (const side of [1, -1]) {
    const wing = joint(side * 0.08, SHOULDER_HEIGHT - 0.05, back - 0.02);
    const shape = fairy
      ? kit.mesh(new THREE.CircleGeometry(0.4, 10), wingMaterial, side * 0.38, 0.1, 0)
      : kit.mesh(box(0.85, 0.45, 0.03), wingMaterial, side * 0.45, -0.05, 0);
    if (fairy) {
      shape.scale.set(1, 1.4, 1);
    } else {
      shape.rotation.z = side * 0.35;
    }
    wing.add(shape);
    torso.add(wing);
    if (side === 1) {
      rig.wingL = wing;
    } else {
      rig.wingR = wing;
    }
  }
}

function buildHorseBody(frame: Frame, parent: THREE.Group, rig: Rig): void {
  const { kit, look } = frame;
  const coat = look.covering ?? 0x7a4a2a;
  parent.add(kit.mesh(box(0.55, 0.55, 1.25), coat, 0, 1.08, -0.42));
  const positions: Array<[number, number]> = [[0.18, 0.05], [-0.18, 0.05], [0.18, -0.9], [-0.18, -0.9]];
  for (const [x, z] of positions) {
    const leg = joint(x, 0.86, z);
    leg.add(kit.mesh(rod(0.075, 0.055, 0.8), coat, 0, -0.4, 0));
    leg.add(kit.mesh(box(0.12, 0.1, 0.14), HOOF_COLOR, 0, -0.82, 0));
    parent.add(leg);
    rig.legs.push(leg);
  }
  const tail = joint(0, 1.25, -1.05);
  tail.rotation.x = 0.7;
  tail.add(kit.mesh(cone(0.1, 0.7, 5), look.hair, 0, 0, -0.35).rotateX(-Math.PI / 2));
  parent.add(tail);
  rig.tail = tail;
}
