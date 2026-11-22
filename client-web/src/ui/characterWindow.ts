import { ABILITY_LABELS, AbilityName, ContentCatalog } from "../api/content";
import { CharacterState, CharacterStats } from "../api/gateway";
import { levelForXp, xpForNextLevel } from "../game/rules";
import { h, replaceChildren } from "./dom";
import { damageText, signed, STAT_LABELS } from "./labels";

const CELL_SIZE_METERS = 1.5;
/** Same threshold as Alignment.CellThreshold on the server. */
const ALIGNMENT_THRESHOLD = 33;
const LAW_LABELS = ["Законный", "Нейтральный", "Хаотичный"];
const MORAL_LABELS = ["добрый", "нейтральный", "злой"];

/** Character sheet: abilities, combat numbers, alignment, reputation and professions. */
export class CharacterWindow {
  readonly root = h("div", { class: "game-window character-window panel hidden" });
  private readonly body = h("div", { class: "character-body" });

  constructor(private readonly catalog: ContentCatalog, onClose: () => void) {
    this.root.append(
      h("div", { class: "window-header" },
        h("h3", {}, "Персонаж"),
        h("button", { class: "small window-close", title: "Закрыть (C)", onclick: onClose }, "✕")),
      this.body);
  }

  get isOpen(): boolean {
    return !this.root.classList.contains("hidden");
  }

  setOpen(open: boolean): void {
    this.root.classList.toggle("hidden", !open);
  }

  render(state: CharacterState, stats: CharacterStats | null): void {
    const sheet = state.sheet;
    const race = this.catalog.races[sheet.raceId];
    const level = levelForXp(sheet.xp);
    const next = xpForNextLevel(level);
    const classes = Object.entries(sheet.classLevels).map(([id, classLevel]) => `${this.catalog.classes[id]?.name ?? id} ${classLevel}`);
    replaceChildren(this.body,
      h("div", { class: "character-identity" },
        h("img", { class: "portrait", src: `/portraits/${sheet.raceId}.png`, alt: "" }),
        h("div", {},
          h("strong", { class: "character-name" }, sheet.name),
          h("span", {}, `${race?.name ?? sheet.raceId} · ${classes.length ? classes.join(", ") : "Странник"} · ур. ${level}`),
          h("span", { class: "muted" }, `${alignmentLabel(sheet.lawChaos, sheet.goodEvil)} · опыт ${sheet.xp}${next === null ? "" : ` / ${next}`}`))),
      h("div", { class: "character-columns" },
        h("section", {}, h("h4", {}, "Характеристики"), this.abilityTable(stats)),
        h("section", {}, h("h4", {}, "Бой"), stats ? this.combatBlock(stats, state) : h("p", { class: "muted" }, "Загрузка…"))),
      h("div", { class: "character-columns" },
        h("section", {}, h("h4", {}, "Особенности"), this.traitsBlock(state)),
        h("section", {}, h("h4", {}, "Репутация и ремёсла"), this.socialBlock(state))));
  }

  private abilityTable(stats: CharacterStats | null): HTMLElement {
    if (!stats) {
      return h("p", { class: "muted" }, "Загрузка…");
    }
    return h("table", { class: "ability-table" },
      h("thead", {}, h("tr", {}, h("th", {}, ""), h("th", {}, "База"), h("th", {}, "Итог"), h("th", {}, "Мод."))),
      h("tbody", {}, ...stats.abilities.map((score) => h("tr", {},
        h("td", {}, ABILITY_LABELS[score.ability as AbilityName] ?? score.ability),
        h("td", { class: "muted" }, String(score.base)),
        h("td", { class: score.total > score.base ? "boosted" : "" }, String(score.total)),
        h("td", { class: "modifier" }, signed(score.modifier))))));
  }

  private combatBlock(stats: CharacterStats, state: CharacterState): HTMLElement {
    const weapon = stats.weaponId ? this.catalog.items[stats.weaponId]?.name ?? stats.weaponId : "без оружия";
    const slots = stats.spellSlots.map((count, index) => (count > 0 ? `${index + 1} ур.: ${count}` : "")).filter(Boolean);
    const rows: [string, string][] = [
      ["Здоровье", `${Math.min(state.sheet.currentHp || stats.maxHp, stats.maxHp)} / ${stats.maxHp}`],
      ["Класс доспеха", String(stats.armorClass)],
      ["Инициатива", signed(stats.initiative)],
      ["Скорость", `${stats.speedCells} кл. (${stats.speedCells * CELL_SIZE_METERS} м)`],
      ["Бонус мастерства", signed(stats.proficiencyBonus)],
      ["Оружие", weapon],
      ["Атака", `${signed(stats.attackBonus)} к попаданию`],
      ["Урон", damageText(stats.damage, stats.damageBonus, stats.damageType)],
      ["Дальность", stats.rangeCells > 1 ? `${stats.rangeCells} кл.` : "ближний бой"],
      ["Атака заклинанием", signed(stats.spellAttackBonus)],
      ["Сложность спасброска", String(stats.spellSaveDc)],
      ["Ячейки заклинаний", slots.length ? slots.join(", ") : "нет"],
    ];
    const bonuses = Object.entries(stats.bonuses);
    return h("div", {},
      h("dl", { class: "stat-list" }, ...rows.flatMap(([label, value]) => [h("dt", {}, label), h("dd", {}, value)])),
      bonuses.length ? h("p", { class: "muted" }, `От предметов и перков: ${bonuses.map(([stat, value]) => `${STAT_LABELS[stat] ?? stat} ${signed(value)}`).join(", ")}`) : null);
  }

  private traitsBlock(state: CharacterState): HTMLElement {
    const race = this.catalog.races[state.sheet.raceId];
    const perks = state.sheet.perks.map((id) => this.catalog.perks[id]).filter(Boolean);
    return h("div", {},
      h("p", {}, h("span", { class: "muted" }, "Расовые черты: "), race?.traits.length ? race.traits.map(humanize).join(", ") : "—"),
      h("p", { class: "muted" }, "Перки:"),
      perks.length
        ? h("ul", { class: "bonus-list" }, ...perks.map((perk) => h("li", {}, h("strong", {}, perk.name), perk.description ? ` — ${perk.description}` : "")))
        : h("p", { class: "muted" }, "Пока нет. Перки открываются с уровнями и достижениями."));
  }

  private socialBlock(state: CharacterState): HTMLElement {
    const faction = this.catalog.factions[state.factionId]?.name ?? "—";
    const reputation = Object.entries(state.sheet.reputation);
    const professions = Object.entries(state.sheet.professionXp);
    return h("div", {},
      h("p", {}, h("span", { class: "muted" }, "Фракция: "), faction),
      reputation.length
        ? h("ul", { class: "bonus-list" }, ...reputation.map(([id, value]) => h("li", {}, `${this.catalog.factions[id]?.name ?? id}: ${signed(value)}`)))
        : null,
      h("p", { class: "muted" }, "Профессии:"),
      professions.length
        ? h("ul", { class: "bonus-list" }, ...professions.map(([id, xp]) => h("li", {}, `${this.catalog.professions[id]?.name ?? id}: ${xp} опыта`)))
        : h("p", { class: "muted" }, "Не изучены. Собирайте ресурсы, чтобы получить опыт ремесла."),
      h("p", { class: "muted" }, `Выполнено заданий: ${state.sheet.completedQuests.length}`));
  }
}

export function alignmentLabel(lawChaos: number, goodEvil: number): string {
  const law = lawChaos >= ALIGNMENT_THRESHOLD ? 0 : lawChaos <= -ALIGNMENT_THRESHOLD ? 2 : 1;
  const moral = goodEvil >= ALIGNMENT_THRESHOLD ? 0 : goodEvil <= -ALIGNMENT_THRESHOLD ? 2 : 1;
  return law === 1 && moral === 1 ? "Истинно нейтральный" : `${LAW_LABELS[law]}-${MORAL_LABELS[moral]}`;
}

function humanize(traitId: string): string {
  return traitId.replace(/_/g, " ");
}
