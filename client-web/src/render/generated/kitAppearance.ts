import { seededRandom } from "../../world/geometry";

export type Sex = "female" | "male";
export type OutfitId = "commoner" | "ranger" | "mage" | "noble" | "guard" | "artisan";
export type Build = "slender" | "athletic" | "sturdy";

/** What a kit can offer, read from the manifest. Ids may have gaps: parts are generated, rejected and redone. */
export interface KitContents {
  sexes: Sex[];
  skins: Record<Sex, number[]>;
  hair: number[];
  outfits: OutfitId[];
}

export interface KitAppearance {
  sex: Sex;
  /** Null when the kit has no face textures yet: the base texture is shown. */
  skinIndex: number | null;
  hairIndex: number | null;
  hairColor: number;
  outfit: OutfitId;
  build: Build;
  /** Multiplies the base height; builds widen or narrow the body separately. */
  heightScale: number;
}

export interface AppearanceHints {
  seed: number;
  /** NPC role from content; undefined for players and villagers. */
  role?: string;
  isAdventurer: boolean;
}

/** Width scale of the whole body per build; small enough that animated limbs do not visibly shear. */
export const BUILD_WIDTH: Record<Build, number> = { slender: 0.92, athletic: 1, sturdy: 1.09 };
const BUILDS: Build[] = ["slender", "athletic", "athletic", "sturdy"];
const MIN_HEIGHT_SCALE = 0.94;
const HEIGHT_SCALE_RANGE = 0.12;
const BALD_CHANCE = 0.06;
const SEED_WARM_UP_STEPS = 4;

/** Natural elf colours first, then rarer fey ones, so most crowds look plausible. */
export const HAIR_COLORS = [
  0xf4ecd6, 0xd9d2c0, 0xe7c06a, 0xc98a3a, 0xa5482a, 0x6e2a1e, 0x4a2e1e, 0x231c1e, 0x14121a,
  0xc8ccd8, 0x3a4c8e, 0x4e7a4a, 0x8a4ea0,
];
const COMMON_HAIR_COLORS = 9;
const RARE_HAIR_CHANCE = 0.2;

const ROLE_OUTFITS: Record<string, OutfitId> = {
  Citizen: "commoner",
  Vendor: "artisan",
  Trainer: "artisan",
  Guard: "guard",
  Mentor: "mage",
  QuestGiver: "noble",
  Auctioneer: "noble",
};
const ADVENTURER_OUTFITS: OutfitId[] = ["ranger", "ranger", "mage", "guard"];
const VILLAGER_OUTFITS: OutfitId[] = ["commoner", "commoner", "commoner", "artisan"];

/** Same seed, same elf: every client derives the look locally, so nothing needs to be synced. */
export function rollAppearance(contents: KitContents, hints: AppearanceHints): KitAppearance {
  const random = seededRandom(hints.seed);
  // xorshift's first outputs follow the seed closely, so neighbouring villager seeds would look alike.
  for (let warmUp = 0; warmUp < SEED_WARM_UP_STEPS; warmUp++) {
    random();
  }
  const pick = <T>(items: T[]): T => items[Math.floor(random() * items.length) % items.length];
  const pickOrNull = (items: number[]): number | null => (items.length === 0 ? null : pick(items));
  const sex = pick(contents.sexes);
  const rareHair = random() < RARE_HAIR_CHANCE;
  const hairColor = rareHair ? pick(HAIR_COLORS.slice(COMMON_HAIR_COLORS)) : pick(HAIR_COLORS.slice(0, COMMON_HAIR_COLORS));
  const bald = random() < BALD_CHANCE;
  const skinIndex = pickOrNull(contents.skins[sex]);
  const hairIndex = pickOrNull(contents.hair);
  return {
    sex,
    skinIndex,
    hairIndex: bald ? null : hairIndex,
    hairColor,
    outfit: available(contents, desiredOutfit(hints, pick)),
    build: pick(BUILDS),
    heightScale: MIN_HEIGHT_SCALE + random() * HEIGHT_SCALE_RANGE,
  };
}

function desiredOutfit(hints: AppearanceHints, pick: <T>(items: T[]) => T): OutfitId {
  if (hints.role) {
    return ROLE_OUTFITS[hints.role] ?? "commoner";
  }
  return pick(hints.isAdventurer ? ADVENTURER_OUTFITS : VILLAGER_OUTFITS);
}

function available(contents: KitContents, outfit: OutfitId): OutfitId {
  if (contents.outfits.includes(outfit) || contents.outfits.length === 0) {
    return outfit;
  }
  return contents.outfits.includes("commoner") ? "commoner" : contents.outfits[0];
}
