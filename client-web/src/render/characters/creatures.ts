import * as THREE from "three";
import { darken, Outfit, RACE_LOOKS, RaceLook, ROLE_OUTFITS } from "./appearance";
import { buildHumanoid } from "./humanoid";
import { ball, box, cone, joint, PartKit, rod } from "./parts";
import { Rig } from "./rig";

interface QuadrupedSpec {
  length: number;
  legHeight: number;
  bodyHeight: number;
  width: number;
  coat: number;
  belly?: number;
  headSize: number;
  snout: number;
  ears: "pointed" | "round" | "none";
  tail: "bushy" | "thin" | "stub" | "dragon" | "none";
  tusks?: boolean;
  antlers?: boolean;
  horns?: boolean;
  wings?: boolean;
  neck?: number;
  eyes?: number;
  spikes?: boolean;
}

function quadruped(spec: QuadrupedSpec): Rig {
  const kit = new PartKit();
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  const bodyY = spec.legHeight + spec.bodyHeight / 2;
  const torso = joint(0, bodyY, 0);
  torso.add(kit.mesh(box(spec.width, spec.bodyHeight, spec.length), spec.coat));
  if (spec.belly !== undefined) {
    torso.add(kit.mesh(box(spec.width * 0.8, spec.bodyHeight * 0.3, spec.length * 0.8), spec.belly, 0, -spec.bodyHeight * 0.4, 0));
  }
  if (spec.spikes) {
    for (let index = 0; index < 5; index++) {
      torso.add(kit.mesh(cone(spec.width * 0.08, spec.bodyHeight * 0.4, 4), darken(spec.coat, 0.5), 0, spec.bodyHeight * 0.6, spec.length * (0.35 - index * 0.18)));
    }
  }
  body.add(torso);

  const neckLength = spec.neck ?? 0;
  const head = joint(0, bodyY + spec.bodyHeight * 0.3 + neckLength * 0.7, spec.length / 2 + neckLength * 0.6);
  if (neckLength > 0) {
    const neck = kit.mesh(rod(spec.headSize * 0.35, spec.headSize * 0.5, neckLength * 1.2, 6), spec.coat, 0, -neckLength * 0.35, -neckLength * 0.35);
    neck.rotation.x = 0.8;
    head.add(neck);
  }
  head.add(kit.mesh(box(spec.headSize * 0.9, spec.headSize * 0.8, spec.headSize), spec.coat, 0, 0, spec.headSize * 0.3));
  head.add(kit.mesh(box(spec.headSize * 0.55, spec.headSize * 0.45, spec.snout), spec.belly ?? darken(spec.coat, 0.85), 0, -spec.headSize * 0.15, spec.headSize * 0.8 + spec.snout / 2));
  head.add(kit.mesh(ball(spec.headSize * 0.1, 0), 0x1a1410, 0, -spec.headSize * 0.02, spec.headSize * 0.8 + spec.snout));
  for (const side of [1, -1]) {
    const eye = spec.eyes
      ? kit.glow(ball(spec.headSize * 0.09, 0), spec.eyes, side * spec.headSize * 0.32, spec.headSize * 0.15, spec.headSize * 0.78)
      : kit.mesh(ball(spec.headSize * 0.08, 0), 0x1a1410, side * spec.headSize * 0.32, spec.headSize * 0.15, spec.headSize * 0.78);
    head.add(eye);
    if (spec.ears === "pointed") {
      head.add(kit.mesh(cone(spec.headSize * 0.18, spec.headSize * 0.45, 4), spec.coat, side * spec.headSize * 0.3, spec.headSize * 0.55, spec.headSize * 0.1));
    } else if (spec.ears === "round") {
      head.add(kit.mesh(ball(spec.headSize * 0.18, 0), darken(spec.coat, 0.8), side * spec.headSize * 0.42, spec.headSize * 0.42, 0));
    }
    if (spec.tusks) {
      head.add(kit.mesh(cone(spec.headSize * 0.06, spec.headSize * 0.35, 4), 0xf4ecd8, side * spec.headSize * 0.25, -spec.headSize * 0.1, spec.headSize * 0.8 + spec.snout * 0.8));
    }
    if (spec.antlers) {
      const antler = joint(side * spec.headSize * 0.25, spec.headSize * 0.45, 0);
      antler.rotation.z = side * -0.45;
      antler.add(kit.mesh(rod(0.02, 0.03, spec.headSize * 1.4, 4), 0xd8c8a0, 0, spec.headSize * 0.7, 0));
      antler.add(kit.mesh(rod(0.015, 0.02, spec.headSize * 0.6, 4), 0xd8c8a0, side * 0.1, spec.headSize * 0.9, 0.05).rotateZ(side * -0.8));
      head.add(antler);
    }
    if (spec.horns) {
      head.add(kit.mesh(cone(spec.headSize * 0.12, spec.headSize * 0.8, 4), 0x2a2420, side * spec.headSize * 0.3, spec.headSize * 0.6, -spec.headSize * 0.2).rotateX(-0.7));
    }
  }
  body.add(head);

  const legs: THREE.Object3D[] = [];
  const legX = spec.width * 0.36;
  const legZ = spec.length * 0.38;
  for (const [x, z] of [[legX, legZ], [-legX, legZ], [legX, -legZ], [-legX, -legZ]]) {
    const leg = joint(x, spec.legHeight, z);
    leg.add(kit.mesh(box(spec.width * 0.22, spec.legHeight, spec.width * 0.22), darken(spec.coat, 0.8), 0, -spec.legHeight / 2, 0));
    body.add(leg);
    legs.push(leg);
  }

  let tail: THREE.Object3D | undefined;
  if (spec.tail !== "none") {
    tail = joint(0, bodyY + spec.bodyHeight * 0.25, -spec.length / 2);
    switch (spec.tail) {
      case "bushy":
        tail.rotation.x = 0.6;
        tail.add(kit.mesh(cone(spec.width * 0.2, spec.length * 0.5, 5), spec.coat, 0, 0, -spec.length * 0.25).rotateX(-Math.PI / 2));
        break;
      case "thin":
        tail.rotation.x = 0.9;
        tail.add(kit.mesh(rod(0.02, 0.03, spec.length * 0.45, 4), spec.coat, 0, 0, -spec.length * 0.22).rotateX(Math.PI / 2));
        break;
      case "stub":
        tail.add(kit.mesh(ball(spec.width * 0.15, 0), spec.belly ?? 0xf4f0e8, 0, 0, -0.04));
        break;
      case "dragon":
        tail.rotation.x = 0.25;
        tail.add(kit.mesh(cone(spec.width * 0.3, spec.length * 1.1, 6), spec.coat, 0, 0, -spec.length * 0.55).rotateX(-Math.PI / 2));
        break;
    }
    body.add(tail);
  }

  const rig: Rig = {
    root, body, torso, head, extraArms: [], legs, tail,
    height: bodyY + spec.bodyHeight / 2 + spec.headSize + neckLength,
    locomotion: "quadruped",
    stride: Math.max(0.8, spec.legHeight * 2.4),
    materials: kit.materials,
  };
  if (spec.wings) {
    for (const side of [1, -1]) {
      const wing = joint(side * spec.width * 0.4, bodyY + spec.bodyHeight * 0.45, spec.length * 0.1);
      const membrane = kit.mesh(new THREE.CircleGeometry(spec.length * 0.6, 5, 0, Math.PI), darken(spec.coat, 0.7), side * spec.length * 0.05, 0, 0);
      membrane.rotation.set(-Math.PI / 2, 0, side > 0 ? -Math.PI / 2 : Math.PI / 2);
      wing.add(membrane);
      body.add(wing);
      if (side === 1) {
        rig.wingL = wing;
      } else {
        rig.wingR = wing;
      }
    }
  }
  return rig;
}

function crawler(kind: "spider" | "crab"): Rig {
  const kit = new PartKit();
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  const spider = kind === "spider";
  const shell = spider ? 0x2a3a1a : 0xc8502a;
  const torso = joint(0, spider ? 0.6 : 0.5, 0);
  if (spider) {
    torso.add(kit.mesh(ball(0.35, 1), shell, 0, 0, 0.2));
    torso.add(kit.mesh(ball(0.55, 1), 0x3a5a1a, 0, 0.15, -0.55));
    torso.add(kit.mesh(box(0.3, 0.04, 0.5), 0xe8d040, 0, 0.62, -0.55));
  } else {
    const carapace = kit.mesh(ball(0.6, 1), shell);
    carapace.scale.set(1.4, 0.45, 1);
    torso.add(carapace);
  }
  body.add(torso);
  const head = joint(0, 0, spider ? 0.5 : 0.45);
  for (const side of [1, -1]) {
    if (spider) {
      head.add(kit.glow(ball(0.05, 0), 0xff3a2a, side * 0.1, 0.08, 0.05));
      head.add(kit.glow(ball(0.035, 0), 0xff3a2a, side * 0.18, 0.12, 0.0));
    } else {
      head.add(kit.mesh(rod(0.02, 0.02, 0.25, 4), shell, side * 0.15, 0.2, 0));
      head.add(kit.mesh(ball(0.05, 0), 0x1a1410, side * 0.15, 0.33, 0));
    }
  }
  torso.add(head);
  const legs: THREE.Object3D[] = [];
  const pairs = spider ? 4 : 3;
  for (let pair = 0; pair < pairs; pair++) {
    for (const side of [1, -1]) {
      const leg = joint(side * (spider ? 0.25 : 0.6), spider ? 0.6 : 0.45, 0.35 - pair * (spider ? 0.22 : 0.3));
      leg.rotation.y = side * (Math.PI / 2 - (pair - pairs / 2) * 0.35);
      const upper = kit.mesh(rod(0.04, 0.035, spider ? 0.6 : 0.4, 4), spider ? 0x1a2a10 : shell, 0, 0.12, spider ? 0.27 : 0.18);
      upper.rotation.x = Math.PI / 2 - 0.5;
      const lower = kit.mesh(rod(0.035, 0.015, spider ? 0.8 : 0.5, 4), spider ? 0x1a2a10 : shell, 0, -0.2, spider ? 0.65 : 0.42);
      lower.rotation.x = 0.35;
      leg.add(upper, lower);
      body.add(leg);
      legs.push(leg);
    }
  }
  const rig: Rig = {
    root, body, torso, head, extraArms: [], legs,
    height: spider ? 1.3 : 1.1, locomotion: "crawler", stride: 0.9, materials: kit.materials,
  };
  if (!spider) {
    for (const side of [1, -1]) {
      const claw = joint(side * 0.55, 0.55, 0.45);
      claw.rotation.x = -1.2;
      claw.add(kit.mesh(rod(0.06, 0.05, 0.45, 5), shell, 0, -0.22, 0));
      claw.add(kit.mesh(box(0.22, 0.3, 0.12), darken(shell, 0.85), 0, -0.55, 0));
      claw.add(kit.mesh(box(0.08, 0.25, 0.08), darken(shell, 0.7), side * 0.09, -0.74, 0.03));
      body.add(claw);
      if (side === 1) {
        rig.armL = claw;
      } else {
        rig.armR = claw;
      }
    }
  }
  return rig;
}

function fireElemental(): Rig {
  const kit = new PartKit();
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  const torso = joint(0, 0.8, 0);
  const flames = [0xffd23a, 0xff8a2a, 0xff4a1a];
  flames.forEach((color, index) => {
    const flame = kit.glow(cone(0.55 - index * 0.12, 1.6 - index * 0.3, 7), color, 0, 0.3 + index * 0.25, 0, 1.6);
    flame.name = "flicker";
    torso.add(flame);
  });
  torso.add(kit.glow(ball(0.35, 1), 0xffb03a, 0, 1.0, 0.05, 2));
  body.add(torso);
  const head = joint(0, 1.55, 0);
  head.add(kit.glow(ball(0.28, 1), 0xffe07a, 0, 0, 0, 2.2));
  for (const side of [1, -1]) {
    head.add(kit.mesh(box(0.08, 0.05, 0.05), 0x2a0a00, side * 0.1, 0.02, 0.25));
  }
  torso.add(head);
  const arms = [1, -1].map((side) => {
    const arm = joint(side * 0.55, 1.1, 0);
    arm.add(kit.glow(cone(0.16, 0.9, 5), 0xff7a2a, 0, -0.4, 0, 1.8).rotateX(Math.PI));
    torso.add(arm);
    return arm;
  });
  return {
    root, body, torso, head, armL: arms[0], armR: arms[1], extraArms: [], legs: [],
    height: 2.4, locomotion: "hover", stride: 1.4, materials: kit.materials,
  };
}

function trainingDummy(): Rig {
  const kit = new PartKit();
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  root.add(kit.mesh(box(0.7, 0.12, 0.7), 0x5a3f28, 0, 0.06, 0));
  const torso = joint(0, 0, 0);
  torso.add(kit.mesh(rod(0.07, 0.08, 1.6, 6), 0x7a5a3a, 0, 0.8, 0));
  torso.add(kit.mesh(rod(0.28, 0.24, 0.7, 8), 0xd8c08a, 0, 1.1, 0));
  torso.add(kit.mesh(box(1.1, 0.1, 0.1), 0x7a5a3a, 0, 1.3, 0));
  torso.add(kit.mesh(rod(0.18, 0.18, 0.03, 12), 0xc8302a, 0, 1.1, 0.26).rotateX(Math.PI / 2));
  torso.add(kit.mesh(rod(0.1, 0.1, 0.035, 12), 0xf4f0e8, 0, 1.1, 0.27).rotateX(Math.PI / 2));
  body.add(torso);
  const head = joint(0, 1.62, 0);
  head.add(kit.mesh(ball(0.2, 1), 0xc8a870));
  head.add(kit.mesh(box(0.08, 0.02, 0.02), 0x3a2a1a, 0.07, 0.04, 0.19));
  head.add(kit.mesh(box(0.08, 0.02, 0.02), 0x3a2a1a, -0.07, 0.04, 0.19));
  torso.add(head);
  return { root, body, torso, head, extraArms: [], legs: [], height: 1.85, locomotion: "static", stride: 1, materials: kit.materials };
}

function humanoidMob(look: Partial<RaceLook>, outfit: Partial<Outfit>, options: { hover?: boolean } = {}): Rig {
  const base = RACE_LOOKS.half_elf;
  const rig = buildHumanoid({ ...base, ...look }, { ...ROLE_OUTFITS.Citizen, ...outfit });
  if (options.hover) {
    rig.locomotion = "hover";
  }
  return rig;
}

const BONE = 0xe8e0c8;
const RAGS = 0x4a4038;

const MOB_BUILDERS: Record<string, () => Rig> = {
  training_dummy: trainingDummy,
  wolf: () => quadruped({ length: 1.15, legHeight: 0.5, bodyHeight: 0.42, width: 0.38, coat: 0x6a6a72, belly: 0xb8b8c0, headSize: 0.34, snout: 0.25, ears: "pointed", tail: "bushy", eyes: 0xffd040 }),
  sand_jackal: () => quadruped({ length: 0.95, legHeight: 0.45, bodyHeight: 0.34, width: 0.3, coat: 0xc8a060, belly: 0xe8d0a0, headSize: 0.28, snout: 0.24, ears: "pointed", tail: "bushy" }),
  boar: () => quadruped({ length: 1.1, legHeight: 0.32, bodyHeight: 0.6, width: 0.55, coat: 0x5a3a24, headSize: 0.42, snout: 0.22, ears: "pointed", tail: "thin", tusks: true, spikes: true }),
  deer: () => quadruped({ length: 1.1, legHeight: 0.75, bodyHeight: 0.42, width: 0.34, coat: 0xa8743e, belly: 0xe8d8b8, headSize: 0.3, snout: 0.2, ears: "pointed", tail: "stub", antlers: true, neck: 0.35 }),
  ashen_dragon: () => {
    const rig = quadruped({ length: 4.2, legHeight: 1.3, bodyHeight: 1.6, width: 1.7, coat: 0x4a4448, belly: 0x8a6a5a, headSize: 1.1, snout: 0.9, ears: "none", tail: "dragon", horns: true, wings: true, neck: 1.6, eyes: 0xff6a2a, spikes: true });
    rig.stride = 4;
    return rig;
  },
  giant_crab: () => crawler("crab"),
  jungle_spider: () => crawler("spider"),
  fire_elemental: fireElemental,
  storm_harpy: () => humanoidMob(
    { skin: 0xc8b8d8, hair: 0x3a3a5a, hairStyle: "long", wings: "feathered", covering: 0x5a5a7a, goatLegs: true, eyes: 0x8ad8ff },
    { tunic: 0x4a4a6a, pants: 0x3a3a5a }, { hover: true }),
  pixie_trickster: () => humanoidMob({ ...RACE_LOOKS.fairy, glow: 0x9aff8a, hair: 0x5aff6a }, { tunic: 0x3a8a3a, pants: 0x2a6a2a }, { hover: true }),
  bandit: () => humanoidMob({ skin: 0xd8a882, hairStyle: "none" }, { tunic: 0x3a3028, pants: 0x2a2420, cloak: 0x2a2420, headwear: "hood", rightHand: "dagger", leftHand: "dagger" }),
  bandit_captain: () => humanoidMob({ skin: 0xc89870, bulk: 1.25, height: 1.95, beard: true, hairStyle: "short", hair: 0x2a1a10 }, { tunic: 0x6a1a1a, pants: 0x2a2420, cloak: 0x8a1a1a, headwear: "bandana", rightHand: "sword", leftHand: "none", armor: false }),
  goblin_scout: () => humanoidMob(RACE_LOOKS.goblin, { tunic: 0x5a4a2a, pants: 0x3a3020, headwear: "bandana", rightHand: "bow" }),
  goblin_boss: () => humanoidMob({ ...RACE_LOOKS.goblin, height: 1.45, bulk: 1.35 }, { tunic: 0x6a2a1a, pants: 0x3a2a1a, armor: true, headwear: "crown", rightHand: "axe", leftHand: "shield", cloak: 0x4a1a10 }),
  cultist: () => humanoidMob({ skin: 0xc8b0a0, eyes: 0xff3a2a, hairStyle: "none" }, { tunic: 0x5a0a1a, trim: 0x1a0a0a, pants: 0x2a0a10, robe: true, headwear: "hood", rightHand: "dagger", leftHand: "book" }),
  skeleton: () => humanoidMob(
    { skin: BONE, covering: BONE, hairStyle: "none", ears: "none", bulk: 0.62, eyes: 0xff4a2a },
    { tunic: RAGS, trim: 0x2a2420, pants: RAGS, rightHand: "sword", leftHand: "shield" }),
  crypt_lord: () => humanoidMob(
    { skin: BONE, covering: BONE, hairStyle: "none", ears: "none", bulk: 0.75, height: 2.1, eyes: 0xb05aff, glow: 0x8a3aff },
    { tunic: 0x3a1a5a, trim: 0xe7c36a, pants: 0x1a0a2a, robe: true, cloak: 0x2a0a4a, headwear: "crown", rightHand: "staff" }),
  ash_ghoul: () => humanoidMob(
    { skin: 0x8a8580, covering: 0x6a6560, hairStyle: "none", ears: "pointed", hunch: 0.5, bulk: 0.8, eyes: 0xff5a2a, tusks: true },
    { tunic: 0x3a3430, pants: 0x2a2420 }),
  astral_stalker: () => humanoidMob(
    { skin: 0x2a2a5a, covering: 0x1a1a4a, hairStyle: "none", ears: "long", height: 2.2, bulk: 0.75, eyes: 0x9adfff, glow: 0x6a7aff },
    { tunic: 0x1a1a3a, pants: 0x101028, rightHand: "dagger", leftHand: "dagger", cloak: 0x2a2a6a }),
  sahuagin_priestess: () => humanoidMob(
    { ...RACE_LOOKS.locathah, skin: 0x3a8a6a, covering: 0x2a7a5a, horns: "crest", eyes: 0xffd040 },
    { tunic: 0x1a6a7a, trim: 0xe7c36a, pants: 0x1a4a5a, robe: true, headwear: "crown", rightHand: "spear" }),
};

function chicken(): Rig {
  const kit = new PartKit();
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  const torso = joint(0, 0.28, 0);
  const plumage = kit.mesh(ball(0.16, 1), 0xf4efe2);
  plumage.scale.set(0.9, 0.85, 1.2);
  torso.add(plumage);
  torso.add(kit.mesh(cone(0.08, 0.2, 4), 0xe8e0d0, 0, 0.08, -0.18).rotateX(-0.9));
  body.add(torso);
  const head = joint(0, 0.42, 0.13);
  head.add(kit.mesh(ball(0.08, 0), 0xf4efe2));
  head.add(kit.mesh(cone(0.03, 0.08, 4), 0xe8b03a, 0, -0.01, 0.09).rotateX(Math.PI / 2));
  head.add(kit.mesh(box(0.02, 0.06, 0.07), 0xd8302a, 0, 0.08, 0.01));
  body.add(head);
  const legs = [0.05, -0.05].map((x) => {
    const leg = joint(x, 0.16, 0);
    leg.add(kit.mesh(rod(0.012, 0.012, 0.16, 4), 0xe8b03a, 0, -0.08, 0));
    body.add(leg);
    return leg;
  });
  return { root, body, torso, head, legL: legs[0], legR: legs[1], extraArms: [], legs: [], height: 0.5, locomotion: "biped", stride: 0.25, materials: kit.materials };
}

export function buildCritter(kind: "chicken" | "cat" | "dog", seed: number): Rig {
  if (kind === "chicken") {
    return chicken();
  }
  const coats = kind === "cat" ? [0x3a3a3a, 0xd8883a, 0xe8e0d0, 0x8a7a6a] : [0x8a5a2a, 0x2a2420, 0xd8c8a0, 0x6a6a6a];
  return quadruped(kind === "cat"
    ? { length: 0.45, legHeight: 0.2, bodyHeight: 0.18, width: 0.16, coat: coats[seed % coats.length], headSize: 0.15, snout: 0.05, ears: "pointed", tail: "thin" }
    : { length: 0.7, legHeight: 0.32, bodyHeight: 0.26, width: 0.24, coat: coats[seed % coats.length], headSize: 0.22, snout: 0.14, ears: "round", tail: "bushy" });
}

export function buildCreature(mobId: string): Rig {
  const builder = MOB_BUILDERS[mobId];
  if (builder) {
    return builder();
  }
  return humanoidMob({ eyes: 0xff3a2a }, { tunic: 0x5a2a2a, rightHand: "axe" });
}
