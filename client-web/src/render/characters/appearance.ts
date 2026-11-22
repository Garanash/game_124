// Data-driven look of every playable race and NPC role. The humanoid builder turns these
// descriptions into meshes, so adding a race is one table entry rather than a new model.

export type Ears = "none" | "round" | "pointed" | "long" | "rabbit" | "cat" | "bear" | "elephant" | "fin" | "cow";
export type Snout = "none" | "big_nose" | "muzzle" | "lizard" | "bull" | "trunk" | "beak" | "frog" | "fish" | "hippo" | "monkey";
export type Horns = "none" | "small" | "curved" | "ram" | "bull" | "crest";
export type Tail = "none" | "cat" | "lizard" | "dragon" | "monkey" | "lion" | "fluffy";
export type Wings = "none" | "feathered" | "fairy";
export type Hair = "none" | "short" | "long" | "ponytail" | "topknot" | "mohawk" | "bun" | "mane";
export type Body = "biped" | "centaur" | "insect" | "construct";

export interface RaceLook {
  height: number;
  bulk: number;
  headScale: number;
  skin: number;
  hair: number;
  hairStyle: Hair;
  beard?: boolean;
  ears: Ears;
  snout: Snout;
  horns: Horns;
  tail: Tail;
  wings: Wings;
  tusks?: boolean;
  body?: Body;
  eyes?: number;
  glow?: number;
  /** Fur, scales or plating painted over the limbs instead of bare skin. */
  covering?: number;
  hunch?: number;
  goatLegs?: boolean;
  hood?: boolean;
}

const BASE: RaceLook = {
  height: 1.8, bulk: 1, headScale: 1, skin: 0xe8c39e, hair: 0x4a3020, hairStyle: "short",
  ears: "round", snout: "none", horns: "none", tail: "none", wings: "none",
};

function look(overrides: Partial<RaceLook>): RaceLook {
  return { ...BASE, ...overrides };
}

export const RACE_LOOKS: Record<string, RaceLook> = {
  elf: look({ height: 1.82, bulk: 0.85, skin: 0xf0d2b0, hair: 0xe8d27a, hairStyle: "long", ears: "pointed" }),
  high_elf: look({ height: 1.88, bulk: 0.82, skin: 0xf6e2cc, hair: 0xf4f0e0, hairStyle: "long", ears: "long", glow: 0x9fd8ff }),
  half_elf: look({ height: 1.78, bulk: 0.92, skin: 0xe2b896, hair: 0x6a3a1a, hairStyle: "ponytail", ears: "pointed" }),
  astral_elf: look({ height: 1.86, bulk: 0.84, skin: 0xc8d4f0, hair: 0xf0f4ff, hairStyle: "long", ears: "long", glow: 0xb8c8ff, eyes: 0xe0f0ff }),
  fairy: look({ height: 0.95, bulk: 0.7, headScale: 1.25, skin: 0xf4d8e8, hair: 0xff9ad8, hairStyle: "bun", ears: "pointed", wings: "fairy", glow: 0xffc8f0 }),
  firbolg: look({ height: 2.2, bulk: 1.25, skin: 0x8fa4b8, hair: 0x9a6a3a, hairStyle: "long", beard: true, ears: "cow", snout: "big_nose" }),
  satyr: look({ height: 1.72, bulk: 0.95, skin: 0xd8a882, hair: 0x5a3a1e, hairStyle: "short", beard: true, ears: "pointed", horns: "ram", goatLegs: true, covering: 0x6a4a30 }),
  owlin: look({ height: 1.65, bulk: 0.95, headScale: 1.2, skin: 0xb89870, hair: 0xb89870, hairStyle: "none", ears: "none", snout: "beak", wings: "feathered", covering: 0x9a7a52, eyes: 0xffc83a }),
  harengon: look({ height: 1.25, bulk: 0.8, headScale: 1.15, skin: 0xd8c8b0, hair: 0xd8c8b0, hairStyle: "none", ears: "rabbit", snout: "muzzle", tail: "fluffy", covering: 0xc8b49a }),
  centaur: look({ height: 2.3, bulk: 1.1, skin: 0xc8946a, hair: 0x3a2010, hairStyle: "ponytail", ears: "round", body: "centaur", covering: 0x7a4a2a }),
  dwarf: look({ height: 1.35, bulk: 1.45, skin: 0xe0a882, hair: 0xa8501e, hairStyle: "short", beard: true, ears: "round", snout: "big_nose" }),
  goliath: look({ height: 2.35, bulk: 1.5, skin: 0x9a9ea8, hair: 0x2a2a30, hairStyle: "none", ears: "round", covering: 0x8a8e98 }),
  warforged: look({ height: 1.95, bulk: 1.3, skin: 0x8a8070, hair: 0x5a5040, hairStyle: "none", ears: "none", body: "construct", eyes: 0x6ad8ff, covering: 0x6a6458 }),
  autognome: look({ height: 1.05, bulk: 1.2, headScale: 1.2, skin: 0xb08a4a, hair: 0x7a5a2a, hairStyle: "none", ears: "none", body: "construct", eyes: 0xffd24a, covering: 0x9a7a3a }),
  goblin: look({ height: 1.05, bulk: 0.85, headScale: 1.25, skin: 0x7aa84a, hair: 0x1a1a10, hairStyle: "mohawk", ears: "long", snout: "big_nose", eyes: 0xffe04a }),
  hobgoblin: look({ height: 1.85, bulk: 1.15, skin: 0xc86a3a, hair: 0x1a1010, hairStyle: "topknot", ears: "pointed", snout: "big_nose", tusks: true }),
  bugbear: look({ height: 2.1, bulk: 1.35, skin: 0xa87a4a, hair: 0x6a4a2a, hairStyle: "mane", ears: "bear", snout: "muzzle", covering: 0x8a6038, hunch: 0.25 }),
  kobold: look({ height: 0.95, bulk: 0.8, skin: 0xb85a3a, hair: 0xb85a3a, hairStyle: "none", ears: "none", snout: "lizard", horns: "small", tail: "lizard", covering: 0xa04a2e, eyes: 0xffd040 }),
  halfling: look({ height: 1.0, bulk: 0.95, headScale: 1.15, skin: 0xf0c49e, hair: 0x7a4a1e, hairStyle: "short", ears: "pointed" }),
  kender: look({ height: 1.05, bulk: 0.85, headScale: 1.15, skin: 0xe8b48a, hair: 0xc8782a, hairStyle: "topknot", ears: "pointed" }),
  gnome: look({ height: 0.98, bulk: 0.9, headScale: 1.3, skin: 0xf0c8a8, hair: 0xe85a2a, hairStyle: "mohawk", beard: true, ears: "pointed", snout: "big_nose" }),
  leonin: look({ height: 2.0, bulk: 1.25, skin: 0xd8a04a, hair: 0x8a4a1a, hairStyle: "mane", ears: "cat", snout: "muzzle", tail: "lion", covering: 0xc8903e }),
  tabaxi: look({ height: 1.8, bulk: 0.9, skin: 0xd8a868, hair: 0x2a1a10, hairStyle: "none", ears: "cat", snout: "muzzle", tail: "cat", covering: 0xc89858, eyes: 0x8aff5a }),
  loxodon: look({ height: 2.2, bulk: 1.5, headScale: 1.2, skin: 0x8a8a90, hair: 0x8a8a90, hairStyle: "none", ears: "elephant", snout: "trunk", tusks: true, covering: 0x7a7a80 }),
  minotaur: look({ height: 2.25, bulk: 1.45, skin: 0x5a3a2a, hair: 0x2a1a10, hairStyle: "none", ears: "cow", snout: "bull", horns: "bull", tail: "lion", covering: 0x4a3020, goatLegs: true }),
  pandaren: look({ height: 1.9, bulk: 1.5, headScale: 1.15, skin: 0xf2efe6, hair: 0x1a1a1a, hairStyle: "none", ears: "bear", snout: "muzzle", covering: 0x222222 }),
  yuan_ti: look({ height: 1.85, bulk: 0.9, skin: 0x6a9a5a, hair: 0x2a3a1a, hairStyle: "none", ears: "none", snout: "lizard", tail: "lizard", hood: true, eyes: 0xffe03a, covering: 0x4a7a3a }),
  lizardfolk: look({ height: 1.9, bulk: 1.1, skin: 0x4a8a5a, hair: 0x4a8a5a, hairStyle: "none", ears: "none", snout: "lizard", horns: "crest", tail: "lizard", covering: 0x3a7a4a, eyes: 0xffd040 }),
  grung: look({ height: 0.95, bulk: 0.9, headScale: 1.3, skin: 0x3ac8e8, hair: 0x3ac8e8, hairStyle: "none", ears: "none", snout: "frog", covering: 0x2aa8c8, eyes: 0x1a1a1a }),
  thri_kreen: look({ height: 2.0, bulk: 0.9, skin: 0xa8b85a, hair: 0xa8b85a, hairStyle: "none", ears: "none", snout: "none", body: "insect", covering: 0x8a9a3a, eyes: 0x1a3a1a }),
  dragonborn: look({ height: 1.95, bulk: 1.3, skin: 0xb8402e, hair: 0xb8402e, hairStyle: "none", ears: "none", snout: "lizard", horns: "curved", tail: "dragon", covering: 0xa0382a, eyes: 0xffc03a }),
  triton: look({ height: 1.8, bulk: 1.0, skin: 0x6ab8c8, hair: 0x1a4a8a, hairStyle: "mohawk", ears: "fin", covering: 0x4a98b0 }),
  locathah: look({ height: 1.65, bulk: 1.05, skin: 0x5a8aa8, hair: 0x5a8aa8, hairStyle: "none", ears: "fin", snout: "fish", covering: 0x4a7a98, eyes: 0x1a1a1a }),
  simic_hybrid: look({ height: 1.8, bulk: 1.0, skin: 0x8ac8a8, hair: 0x2a6a5a, hairStyle: "short", ears: "fin", glow: 0x5affc8 }),
  vedalken: look({ height: 1.9, bulk: 0.85, skin: 0x6a8ad8, hair: 0x6a8ad8, hairStyle: "none", ears: "round" }),
  giff: look({ height: 2.2, bulk: 1.6, headScale: 1.2, skin: 0x8a7a8a, hair: 0x8a7a8a, hairStyle: "none", ears: "round", snout: "hippo", covering: 0x7a6a7a }),
  aarakocra: look({ height: 1.6, bulk: 0.85, skin: 0xd84a2a, hair: 0xd84a2a, hairStyle: "none", ears: "none", snout: "beak", wings: "feathered", covering: 0xc83a22, eyes: 0x1a1a1a }),
  kenku: look({ height: 1.5, bulk: 0.85, skin: 0x2a2a3a, hair: 0x2a2a3a, hairStyle: "none", ears: "none", snout: "beak", covering: 0x1e1e2a, eyes: 0xd8d8f0 }),
  hadozee: look({ height: 1.55, bulk: 0.9, skin: 0xc89a6a, hair: 0x8a5a2a, hairStyle: "none", ears: "round", snout: "monkey", tail: "monkey", covering: 0x8a5a2a }),
  half_orc: look({ height: 1.9, bulk: 1.3, skin: 0x7a9a5a, hair: 0x1a1a1a, hairStyle: "ponytail", ears: "pointed", tusks: true }),
  troll: look({ height: 2.4, bulk: 1.2, skin: 0x5a8a7a, hair: 0x2a5a4a, hairStyle: "mohawk", ears: "long", snout: "big_nose", tusks: true, hunch: 0.4 }),
  shifter: look({ height: 1.8, bulk: 1.05, skin: 0xc8a07a, hair: 0x8a6a4a, hairStyle: "mane", ears: "cat", covering: 0xa8845e }),
  verdan: look({ height: 1.1, bulk: 0.85, headScale: 1.3, skin: 0x6aba5a, hair: 0x2a5a1a, hairStyle: "none", ears: "long" }),
  githyanki: look({ height: 1.85, bulk: 0.85, skin: 0xc8c86a, hair: 0x1a1a1a, hairStyle: "topknot", ears: "pointed", eyes: 0x1a1a1a }),
  kalashtar: look({ height: 1.8, bulk: 0.9, skin: 0xe8d0c0, hair: 0x2a1a3a, hairStyle: "long", ears: "round", glow: 0xc89aff }),
  aasimar: look({ height: 1.85, bulk: 1.0, skin: 0xf6e0c8, hair: 0xfff0b0, hairStyle: "long", ears: "round", glow: 0xfff0a0, eyes: 0xffffd0 }),
  genasi: look({ height: 1.8, bulk: 1.0, skin: 0xd86a3a, hair: 0xffa02a, hairStyle: "mohawk", ears: "round", glow: 0xff7a2a, eyes: 0xffe07a }),
};

export function raceLook(raceId: string): RaceLook {
  return RACE_LOOKS[raceId] ?? BASE;
}

export type Headwear = "none" | "hood" | "wizard_hat" | "helmet" | "cap" | "top_hat" | "circlet" | "bandana" | "crown";
export type Prop = "none" | "sword" | "staff" | "spear" | "hammer" | "gavel" | "book" | "mug" | "scales" | "bow" | "dagger" | "axe" | "cane" | "torch" | "lantern";

export interface Outfit {
  tunic: number;
  trim: number;
  pants: number;
  robe: boolean;
  armor: boolean;
  apron?: number;
  cloak?: number;
  headwear: Headwear;
  rightHand: Prop;
  leftHand: Prop | "shield";
}

export const ROLE_OUTFITS: Record<string, Outfit> = {
  Mentor: { tunic: 0x4a2a7a, trim: 0xe7c36a, pants: 0x2a1a3a, robe: true, armor: false, cloak: 0x2a1a4a, headwear: "hood", rightHand: "staff", leftHand: "book" },
  QuestGiver: { tunic: 0x7a3a2a, trim: 0xe7c36a, pants: 0x3a2a1a, robe: true, armor: false, cloak: 0x5a1a1a, headwear: "circlet", rightHand: "cane", leftHand: "none" },
  Vendor: { tunic: 0x3a7a4a, trim: 0xd8b86a, pants: 0x5a4a2a, robe: false, armor: false, apron: 0xd8c8a0, headwear: "cap", rightHand: "scales", leftHand: "none" },
  Guard: { tunic: 0x2a4a8a, trim: 0xc8c8d0, pants: 0x2a2a3a, robe: false, armor: true, cloak: 0x1a3a7a, headwear: "helmet", rightHand: "spear", leftHand: "shield" },
  Auctioneer: { tunic: 0x6a1a2a, trim: 0xe7c36a, pants: 0x1a1a1a, robe: false, armor: false, cloak: 0x3a0a14, headwear: "top_hat", rightHand: "gavel", leftHand: "book" },
  Trainer: { tunic: 0x6a4a2a, trim: 0x9a7a4a, pants: 0x3a2a1a, robe: false, armor: false, apron: 0x4a3020, headwear: "bandana", rightHand: "hammer", leftHand: "none" },
  Citizen: { tunic: 0x8a6a4a, trim: 0xc8a878, pants: 0x4a3a2a, robe: false, armor: false, headwear: "none", rightHand: "none", leftHand: "none" },
};

const ADVENTURER_PALETTE = [0x8a2a2a, 0x2a5a8a, 0x3a6a2a, 0x6a2a7a, 0x8a6a1a, 0x2a6a6a, 0x5a3a1a, 0x7a7a8a];
const ADVENTURER_GEAR: Array<Pick<Outfit, "rightHand" | "leftHand" | "headwear" | "armor" | "robe">> = [
  { rightHand: "sword", leftHand: "shield", headwear: "none", armor: true, robe: false },
  { rightHand: "staff", leftHand: "none", headwear: "wizard_hat", armor: false, robe: true },
  { rightHand: "bow", leftHand: "none", headwear: "hood", armor: false, robe: false },
  { rightHand: "dagger", leftHand: "dagger", headwear: "bandana", armor: false, robe: false },
  { rightHand: "axe", leftHand: "none", headwear: "none", armor: true, robe: false },
  { rightHand: "sword", leftHand: "torch", headwear: "hood", armor: false, robe: false },
];

/** Players get a stable, distinct traveller outfit derived from their name until cosmetics are equipped. */
export function adventurerOutfit(seed: number): Outfit {
  const color = ADVENTURER_PALETTE[seed % ADVENTURER_PALETTE.length];
  const gear = ADVENTURER_GEAR[Math.floor(seed / ADVENTURER_PALETTE.length) % ADVENTURER_GEAR.length];
  return {
    tunic: color,
    trim: 0xd8b86a,
    pants: 0x3a2e24,
    cloak: (seed >> 5) % 2 === 0 ? darken(color, 0.6) : undefined,
    ...gear,
  };
}

const CITIZEN_PALETTE = [0x9a6a4a, 0x6a7a4a, 0x7a5a6a, 0x4a6a7a, 0xa88a5a, 0x8a4a3a];
const CITIZEN_PROPS: Prop[] = ["none", "none", "mug", "lantern", "none", "book"];

export function citizenOutfit(seed: number): Outfit {
  return {
    ...ROLE_OUTFITS.Citizen,
    tunic: CITIZEN_PALETTE[seed % CITIZEN_PALETTE.length],
    apron: seed % 3 === 0 ? 0xd8c8a0 : undefined,
    headwear: seed % 4 === 1 ? "cap" : "none",
    rightHand: CITIZEN_PROPS[(seed >> 3) % CITIZEN_PROPS.length],
  };
}

export function darken(color: number, factor: number): number {
  const r = Math.round(((color >> 16) & 0xff) * factor);
  const g = Math.round(((color >> 8) & 0xff) * factor);
  const b = Math.round((color & 0xff) * factor);
  return (r << 16) | (g << 8) | b;
}

export function stableHash(text: string): number {
  let hash = 2166136261;
  for (let index = 0; index < text.length; index++) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return hash >>> 0;
}
