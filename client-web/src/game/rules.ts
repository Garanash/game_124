// Client-side mirrors of Shared.Rules used for instant feedback; the server re-validates everything.
import { ABILITY_ORDER, AbilityName, ContentCatalog, RaceDefinition } from "../api/content";
import { CharacterSheet } from "../api/gateway";

export const POINT_BUY_BUDGET = 27;
export const POINT_BUY_MIN = 8;
export const POINT_BUY_MAX = 15;
const POINT_COST: Record<number, number> = { 8: 0, 9: 1, 10: 2, 11: 3, 12: 4, 13: 5, 14: 7, 15: 9 };

export const NAME_MIN_LENGTH = 3;
export const NAME_MAX_LENGTH = 20;
const NAME_PATTERN = /^[\p{L}][\p{L}'-]*$/u;

export const ALIGNMENT_CELLS = [
  { id: "LawfulGood", label: "Законно-добрый" },
  { id: "NeutralGood", label: "Нейтрально-добрый" },
  { id: "ChaoticGood", label: "Хаотично-добрый" },
  { id: "LawfulNeutral", label: "Законно-нейтральный" },
  { id: "TrueNeutral", label: "Истинно нейтральный" },
  { id: "ChaoticNeutral", label: "Хаотично-нейтральный" },
  { id: "LawfulEvil", label: "Законно-злой" },
  { id: "NeutralEvil", label: "Нейтрально-злой" },
  { id: "ChaoticEvil", label: "Хаотично-злой" },
] as const;

export function pointCost(score: number): number {
  const cost = POINT_COST[score];
  if (cost === undefined) {
    throw new RangeError(`Score ${score} is outside point buy range`);
  }
  return cost;
}

export function pointsSpent(scores: readonly number[]): number {
  return scores.reduce((total, score) => total + pointCost(score), 0);
}

export function canChangeScore(scores: readonly number[], index: number, delta: number): boolean {
  const next = scores[index] + delta;
  if (next < POINT_BUY_MIN || next > POINT_BUY_MAX) {
    return false;
  }
  const changed = scores.map((score, position) => (position === index ? next : score));
  return pointsSpent(changed) <= POINT_BUY_BUDGET;
}

export function abilityModifier(score: number): number {
  return Math.floor((score - 10) / 2);
}

export function withRaceBonuses(scores: readonly number[], race: RaceDefinition | undefined): number[] {
  return scores.map((score, index) => score + (race?.abilityBonuses[ABILITY_ORDER[index] as AbilityName] ?? 0));
}

export function validateName(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed.length < NAME_MIN_LENGTH || trimmed.length > NAME_MAX_LENGTH) {
    return `Имя от ${NAME_MIN_LENGTH} до ${NAME_MAX_LENGTH} символов.`;
  }
  return NAME_PATTERN.test(trimmed) ? null : "Только буквы, дефис и апостроф.";
}

/** Same rule as CharacterRules.KnownAbilities: class unlocks up to the class level plus perk grants. */
export function knownAbilities(sheet: CharacterSheet, catalog: ContentCatalog): string[] {
  const known = new Set<string>();
  for (const [classId, level] of Object.entries(sheet.classLevels)) {
    catalog.classes[classId]?.abilities.filter((unlock) => unlock.level <= level).forEach((unlock) => known.add(unlock.abilityId));
  }
  for (const perkId of sheet.perks) {
    catalog.perks[perkId]?.grantsAbilities.forEach((abilityId) => known.add(abilityId));
  }
  return [...known].filter((abilityId) => catalog.abilities[abilityId] !== undefined);
}

const XP_FOR_LEVEL = [0, 300, 900, 2700, 6500, 14000, 23000, 34000, 48000, 64000, 85000, 100000, 120000, 140000, 165000, 195000, 225000, 265000, 305000, 355000];

export function levelForXp(xp: number): number {
  let level = 1;
  while (level < XP_FOR_LEVEL.length && xp >= XP_FOR_LEVEL[level]) {
    level++;
  }
  return level;
}

export function xpForNextLevel(level: number): number | null {
  return level < XP_FOR_LEVEL.length ? XP_FOR_LEVEL[level] : null;
}
