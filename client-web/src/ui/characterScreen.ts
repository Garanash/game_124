import { ABILITY_LABELS, ABILITY_ORDER, ContentCatalog, RaceDefinition } from "../api/content";
import { CharacterSummary, GatewayClient } from "../api/gateway";
import {
  ALIGNMENT_CELLS, POINT_BUY_BUDGET, POINT_BUY_MIN, abilityModifier, canChangeScore, pointsSpent, validateName, withRaceBonuses,
} from "../game/rules";
import { errorText, h, replaceChildren } from "./dom";

const STARTING_SCORES = [POINT_BUY_MIN, POINT_BUY_MIN, POINT_BUY_MIN, POINT_BUY_MIN, POINT_BUY_MIN, POINT_BUY_MIN];
const DEFAULT_ALIGNMENT = "TrueNeutral";

export interface CharacterScreenHandlers {
  onPlay: (character: CharacterSummary) => void;
  onSignOut: () => void;
}

export async function showCharacterList(root: HTMLElement, gateway: GatewayClient, catalog: ContentCatalog, handlers: CharacterScreenHandlers): Promise<void> {
  const characters = await gateway.listCharacters();
  const cards = characters.map((character) => h("button", { class: "character-card", onclick: () => handlers.onPlay(character) },
    portrait(character.raceId),
    h("div", {},
      h("strong", {}, character.name),
      h("span", {}, `${catalog.races[character.raceId]?.name ?? character.raceId} · ур. ${character.level}`),
      h("span", { class: "muted" }, catalog.zones[character.zoneId]?.name ?? character.zoneId),
      h("span", { class: "muted" }, character.classes.length ? character.classes.map((id) => catalog.classes[id]?.name ?? id).join(", ") : "Странник"))));
  replaceChildren(root, h("div", { class: "screen center" },
    h("div", { class: "panel list-panel" },
      h("h2", {}, "Ваши герои"),
      cards.length ? h("div", { class: "character-list" }, ...cards) : h("p", { class: "muted" }, "Пока никого. Создайте первого героя."),
      h("div", { class: "row" },
        h("button", { class: "primary", onclick: () => showCreateCharacter(root, gateway, catalog, handlers) }, "Создать героя"),
        h("button", { onclick: handlers.onSignOut }, "Выйти")))));
}

function showCreateCharacter(root: HTMLElement, gateway: GatewayClient, catalog: ContentCatalog, handlers: CharacterScreenHandlers): void {
  const races = Object.values(catalog.races).sort(byPlayableThenName);
  let selectedRace: RaceDefinition = races[0];
  let alignment = DEFAULT_ALIGNMENT;
  let scores = [...STARTING_SCORES];

  const nameInput = h("input", { placeholder: "Имя героя", maxlength: 20 });
  const raceInfo = h("div", { class: "race-info" });
  const alignmentGrid = h("div", { class: "alignment-grid" });
  const abilityRows = h("div", { class: "ability-rows" });
  const budget = h("div", { class: "budget" });
  const status = h("p", { class: "form-error" });
  const raceGrid = h("div", { class: "race-grid" });

  const renderRaces = () => replaceChildren(raceGrid, ...races.map((race) => {
    const selected = race.id === selectedRace.id ? " selected" : "";
    return race.playable
      ? h("button", { class: `race-card${selected}`, title: race.name, onclick: () => { selectedRace = race; renderAll(); } },
        portrait(race.id), h("span", {}, race.name))
      : h("button", { class: "race-card locked", title: `${race.name} — скоро`, disabled: true },
        portrait(race.id), h("span", {}, race.name), h("small", { class: "race-lock" }, "скоро"));
  }));

  const renderRaceInfo = () => {
    const bonuses = Object.entries(selectedRace.abilityBonuses)
      .map(([ability, bonus]) => `${ABILITY_LABELS[ability as keyof typeof ABILITY_LABELS] ?? ability} +${bonus}`).join(", ");
    const zone = catalog.zones[selectedRace.startZoneId];
    const town = zone?.settlements.find((settlement) => settlement.id === selectedRace.startSettlementId);
    const continent = catalog.continents[selectedRace.continentId];
    const start = [town?.name, zone?.name ?? selectedRace.startZoneId].filter(Boolean).join(", ");
    replaceChildren(raceInfo,
      h("h3", {}, selectedRace.name),
      h("p", {}, `Старт: ${start}${continent ? ` · ${continent.name}` : ""}`),
      h("p", { class: "muted" }, `Бонусы: ${bonuses || "нет"} · Скорость ${selectedRace.speedCells} клеток`),
      selectedRace.traits.length ? h("p", { class: "muted" }, `Черты: ${selectedRace.traits.join(", ")}`) : null);
  };

  const renderAlignment = () => replaceChildren(alignmentGrid, ...ALIGNMENT_CELLS.map((cell) =>
    h("button", { class: `alignment-cell${cell.id === alignment ? " selected" : ""}`, onclick: () => { alignment = cell.id; renderAll(); } }, cell.label)));

  const renderAbilities = () => {
    const final = withRaceBonuses(scores, selectedRace);
    replaceChildren(abilityRows, ...ABILITY_ORDER.map((ability, index) => {
      const modifier = abilityModifier(final[index]);
      return h("div", { class: "ability-row" },
        h("span", { class: "ability-name" }, ABILITY_LABELS[ability]),
        h("button", { class: "small", disabled: !canChangeScore(scores, index, -1), onclick: () => change(index, -1) }, "−"),
        h("span", { class: "ability-score" }, String(scores[index])),
        h("button", { class: "small", disabled: !canChangeScore(scores, index, 1), onclick: () => change(index, 1) }, "+"),
        h("span", { class: "ability-final" }, `${final[index]} (${modifier >= 0 ? "+" : ""}${modifier})`));
    }));
    budget.textContent = `Свободных очков: ${POINT_BUY_BUDGET - pointsSpent(scores)} из ${POINT_BUY_BUDGET}`;
  };

  const change = (index: number, delta: number) => {
    if (canChangeScore(scores, index, delta)) {
      scores = scores.map((score, position) => (position === index ? score + delta : score));
      renderAbilities();
    }
  };

  const renderAll = () => {
    renderRaces();
    renderRaceInfo();
    renderAlignment();
    renderAbilities();
  };

  const create = async () => {
    const nameError = validateName(nameInput.value);
    if (nameError) {
      status.textContent = nameError;
      return;
    }
    try {
      const character = await gateway.createCharacter({ name: nameInput.value.trim(), raceId: selectedRace.id, alignment, abilities: scores });
      handlers.onPlay(character);
    } catch (error) {
      status.textContent = errorText(error);
    }
  };

  renderAll();
  replaceChildren(root, h("div", { class: "screen scroll" },
    h("div", { class: "panel create-panel" },
      h("h2", {}, "Новый герой"),
      h("p", { class: "muted" }, "Класс выбирается в игре: пройдите испытание у наставника в стартовой зоне."),
      h("div", { class: "create-layout" },
        h("section", {}, h("h3", {}, "Раса"), raceGrid),
        h("section", { class: "create-side" },
          raceInfo,
          h("h3", {}, "Имя"), nameInput,
          h("h3", {}, "Мировоззрение"), alignmentGrid,
          h("h3", {}, "Характеристики"), budget, abilityRows,
          status,
          h("div", { class: "row" },
            h("button", { class: "primary", onclick: () => void create() }, "Начать путь"),
            h("button", { onclick: () => void showCharacterList(root, gateway, catalog, handlers) }, "Назад")))))));
}

function byPlayableThenName(left: RaceDefinition, right: RaceDefinition): number {
  return Number(right.playable) - Number(left.playable) || left.name.localeCompare(right.name, "ru");
}

function portrait(raceId: string): HTMLElement {
  const image = h("img", { class: "portrait", src: `/portraits/${raceId}.png`, alt: "", loading: "lazy" });
  image.addEventListener("error", () => image.replaceWith(h("div", { class: "portrait placeholder" }, raceId.slice(0, 2).toUpperCase())));
  return image;
}
