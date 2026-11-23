import { ItemDefinition, ItemRarity, ItemSlotKind, StatModifier } from "../api/content";

export const STAT_LABELS: Record<string, string> = {
  MaxHp: "Здоровье",
  ArmorClass: "Класс доспеха",
  AttackBonus: "Бонус атаки",
  DamageBonus: "Бонус урона",
  SpellSaveDc: "Сложность спасброска",
  Initiative: "Инициатива",
  SpeedCells: "Скорость (клеток)",
  Strength: "Сила",
  Dexterity: "Ловкость",
  Constitution: "Телосложение",
  Intelligence: "Интеллект",
  Wisdom: "Мудрость",
  Charisma: "Харизма",
  GatheringYieldPercent: "Добыча, %",
  CraftingSpeedPercent: "Скорость ремесла, %",
  RestXpPercent: "Опыт отдыха, %",
  GoldFindPercent: "Находка золота, %",
};

export const DAMAGE_TYPE_LABELS: Record<string, string> = {
  Slashing: "рубящий",
  Piercing: "колющий",
  Bludgeoning: "дробящий",
  Fire: "огонь",
  Cold: "холод",
  Lightning: "молния",
  Poison: "яд",
  Necrotic: "некротический",
  Radiant: "излучение",
  Force: "силовой",
  Psychic: "психический",
};

export const RARITY_LABELS: Record<ItemRarity, string> = {
  Common: "Обычный",
  Uncommon: "Необычный",
  Rare: "Редкий",
  Epic: "Эпический",
};

export const TAG_LABELS: Record<string, string> = {
  weapon: "оружие", blade: "клинок", blunt: "дробящее", bow: "лук", ranged: "дальнобойное", finesse: "фехтовальное",
  heavy: "тяжёлое", shield: "щит", armor: "доспех", focus: "фокус", arcane: "тайное", holy: "святое", fire: "огонь",
  cold: "холод", potion: "зелье", food: "еда", herb: "трава", ore: "руда", metal: "металл", stone: "камень", gem: "самоцвет",
  crystal: "кристалл", wood: "дерево", leather: "кожа", cloth: "ткань", material: "материал", trophy: "трофей",
  trinket: "безделушка", tool: "инструмент", book: "книга", ammo: "боеприпас",
};

export function signed(value: number): string {
  return value >= 0 ? `+${value}` : `−${Math.abs(value)}`;
}

export function modifierText(modifier: StatModifier): string {
  return `${STAT_LABELS[modifier.stat] ?? modifier.stat} ${signed(modifier.value)}`;
}

export function damageText(dice: string, bonus: number, damageType: string): string {
  const total = bonus === 0 ? dice : `${dice} ${signed(bonus)}`;
  return `${total} (${DAMAGE_TYPE_LABELS[damageType] ?? damageType})`;
}

const SLOT_KIND_LABELS: Record<ItemSlotKind, string> = {
  None: "",
  Head: "голова",
  Amulet: "шея",
  Chest: "тело",
  Weapon: "одноручное оружие",
  TwoHanded: "двуручное оружие",
  OffHand: "левая рука",
  Gloves: "руки",
  Belt: "пояс",
  Ring: "палец",
  Boots: "ноги",
};

export function itemSummary(item: ItemDefinition): string[] {
  const lines: string[] = [];
  if (item.weaponDamage) {
    lines.push(`Урон: ${damageText(item.weaponDamage, 0, item.weaponDamageType)}`);
  }
  if (item.slot && item.slot !== "None") {
    lines.push(`Надевается: ${SLOT_KIND_LABELS[item.slot]}`);
  }
  lines.push(...item.modifiers.map(modifierText));
  if (item.tags.length > 0) {
    lines.push(item.tags.map((tag) => TAG_LABELS[tag] ?? tag).join(", "));
  }
  lines.push(`Цена: ${item.value} з.`);
  return lines;
}
