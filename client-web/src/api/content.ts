// Subset of server/src/Shared.Rules/Definitions the browser needs; served by GET /content/catalog.

export const ABILITY_ORDER = ["Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"] as const;
export type AbilityName = (typeof ABILITY_ORDER)[number];

export const ABILITY_LABELS: Record<AbilityName, string> = {
  Strength: "Сила",
  Dexterity: "Ловкость",
  Constitution: "Телосложение",
  Intelligence: "Интеллект",
  Wisdom: "Мудрость",
  Charisma: "Харизма",
};

export interface Point { x: number; z: number }

export interface RaceDefinition {
  id: string; name: string; ipStatus: string; continentId: string; startZoneId: string;
  size: string; speedCells: number; abilityBonuses: Partial<Record<AbilityName, number>>; traits: string[];
}

export interface ClassDefinition {
  id: string; name: string; hitDie: number; primaryAbility: AbilityName;
  abilities: { level: number; abilityId: string }[];
}

export interface AbilityDefinition {
  id: string; name: string; description: string; kind: "Spell" | "Skill";
  cost: "Action" | "BonusAction" | "Free"; target: "Self" | "Enemy" | "Ally" | "Area";
  rangeCells: number; areaRadiusCells: number; spellLevel: number; cooldownRounds: number;
}

export interface PerkDefinition { id: string; name: string; grantsAbilities: string[] }
export interface ItemDefinition { id: string; name: string; rarity: string; tags: string[] }
export interface MobDefinition { id: string; name: string; level: number; isGame: boolean; speedCells: number }
export interface ScheduleEntry { fromHour: number; toHour: number; activity: string; position: Point }
export interface NpcDefinition { id: string; name: string; role: string; raceId: string; zoneId: string; schedule: ScheduleEntry[] }
export interface MobSpawnDefinition { mobId: string; position: Point; count: number }
export interface NodeSpawnDefinition { nodeId: string; position: Point }
export interface QuestStep { id: string; type: string; text: string; targetId: string; count: number }
export interface QuestDefinition { id: string; name: string; steps: QuestStep[] }
export interface PortalDefinition { toZoneId: string; position: Point; arrival: Point }

export interface ZoneDefinition {
  id: string; name: string; continentId: string; kind: string; minLevel: number; maxLevel: number;
  pvp: string; sizeMeters: number; spawn: Point; portals: PortalDefinition[];
  mobs: MobSpawnDefinition[]; nodes: NodeSpawnDefinition[];
}

export interface ContinentDefinition { id: string; name: string }

export interface ContentCatalog {
  version: string;
  races: Record<string, RaceDefinition>;
  classes: Record<string, ClassDefinition>;
  abilities: Record<string, AbilityDefinition>;
  perks: Record<string, PerkDefinition>;
  items: Record<string, ItemDefinition>;
  mobs: Record<string, MobDefinition>;
  npcs: Record<string, NpcDefinition>;
  quests: Record<string, QuestDefinition>;
  zones: Record<string, ZoneDefinition>;
  continents: Record<string, ContinentDefinition>;
}

export function itemName(catalog: ContentCatalog, itemId: string): string {
  return catalog.items[itemId]?.name ?? itemId;
}
