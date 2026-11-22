import { AbilityDefinition } from "../api/content";
import { DialogueOptionDto } from "../net/protocol";
import { h, replaceChildren } from "./dom";

const TOAST_LIFETIME_MS = 4500;
const MAX_CHAT_LINES = 80;
const MAX_LOG_LINES = 60;
const CHAT_CHANNELS = [
  { id: "say", label: "Рядом" },
  { id: "zone", label: "Зона" },
  { id: "party", label: "Группа" },
  { id: "world", label: "Мир" },
] as const;

export interface PlayerPanel {
  name: string;
  raceName: string;
  raceId: string;
  level: number;
  classes: string;
  hp: number;
  maxHp: number;
  xp: number;
  xpForNext: number | null;
  xpForCurrent: number;
  gold: number;
}

export interface CombatPanel {
  isMyTurn: boolean;
  currentName: string;
  round: number;
  secondsLeft: number;
  turnOrder: { name: string; isCurrent: boolean; isFriendly: boolean; alive: boolean }[];
  abilities: AbilityDefinition[];
  selectedAbilityId: string | null;
  mode: "move" | "attack" | "ability";
}

export interface HudWindowButtons {
  onCharacter: () => void;
  onInventory: () => void;
}

export interface CombatActions {
  onAttackMode: () => void;
  onMoveMode: () => void;
  onAbility: (abilityId: string) => void;
  onDodge: () => void;
  onEndTurn: () => void;
}

/** Heads-up display: DOM overlay above the 3D canvas. Pure view, the session decides what to show. */
export class Hud {
  readonly root = h("div", { class: "hud" });
  private readonly player = h("div", { class: "hud-player panel" });
  private readonly zone = h("div", { class: "hud-zone" });
  private readonly toasts = h("div", { class: "hud-toasts" });
  private readonly chatLines = h("div", { class: "chat-lines" });
  private readonly chatInput = h("input", { class: "chat-input", placeholder: "Enter — написать", maxlength: 256 });
  private readonly channelSelect = h("select", { class: "chat-channel" },
    ...CHAT_CHANNELS.map((channel) => h("option", { value: channel.id }, channel.label)));
  private readonly dialogue = h("div", { class: "hud-dialogue panel hidden" });
  private readonly combat = h("div", { class: "hud-combat panel hidden" });
  private readonly combatLog = h("div", { class: "hud-log panel hidden" });
  private readonly quests = h("div", { class: "hud-quests panel" });
  private readonly hint = h("div", { class: "hud-hint" },
    "ЛКМ — идти или взаимодействовать · колесо — масштаб · Enter — чат · I — инвентарь · C — персонаж");

  constructor(onChat: (channel: string, text: string) => void, onLeave: () => void, windows: HudWindowButtons) {
    const chat = h("div", { class: "hud-chat panel" }, this.chatLines,
      h("div", { class: "row" }, this.channelSelect, this.chatInput));
    this.chatInput.addEventListener("keydown", (event) => {
      event.stopPropagation();
      if (event.key === "Enter" && this.chatInput.value.trim()) {
        onChat(this.channelSelect.value, this.chatInput.value.trim());
        this.chatInput.value = "";
        this.chatInput.blur();
      } else if (event.key === "Escape") {
        this.chatInput.blur();
      }
    });
    const menu = h("div", { class: "hud-menu" },
      h("button", { class: "small", title: "Клавиша C", onclick: windows.onCharacter }, "Персонаж (C)"),
      h("button", { class: "small", title: "Клавиша I", onclick: windows.onInventory }, "Инвентарь (I)"),
      h("button", { class: "small", onclick: onLeave }, "К героям"));
    this.root.append(this.player, this.zone, menu, this.quests, this.toasts, chat, this.dialogue, this.combat, this.combatLog, this.hint);
  }

  addWindow(window: HTMLElement): void {
    this.root.append(window);
  }

  focusChat(): void {
    this.chatInput.focus();
  }

  get isTyping(): boolean {
    return document.activeElement === this.chatInput;
  }

  setPlayer(panel: PlayerPanel): void {
    const hpRatio = panel.maxHp > 0 ? panel.hp / panel.maxHp : 0;
    const xpSpan = panel.xpForNext === null ? 1 : panel.xpForNext - panel.xpForCurrent;
    const xpRatio = panel.xpForNext === null ? 1 : (panel.xp - panel.xpForCurrent) / Math.max(1, xpSpan);
    replaceChildren(this.player,
      h("img", { class: "portrait", src: `/portraits/${panel.raceId}.png`, alt: "" }),
      h("div", { class: "hud-player-info" },
        h("strong", {}, panel.name),
        h("span", { class: "muted" }, `${panel.raceName} · ${panel.classes} · ур. ${panel.level}`),
        bar("hp", hpRatio, `${panel.hp} / ${panel.maxHp} HP`),
        bar("xp", xpRatio, panel.xpForNext === null ? "максимум" : `${panel.xp} / ${panel.xpForNext} опыта`),
        h("span", { class: "gold" }, `${panel.gold} з.`)));
  }

  setZone(name: string, subtitle: string): void {
    replaceChildren(this.zone, h("strong", {}, name), h("span", {}, subtitle));
  }

  setQuests(lines: string[]): void {
    this.quests.classList.toggle("hidden", lines.length === 0);
    replaceChildren(this.quests, h("h4", {}, "Задания"), ...lines.map((line) => h("p", {}, line)));
  }

  toast(text: string, kind: "info" | "error" | "loot" | "quest" = "info"): void {
    const toast = h("div", { class: `toast ${kind}` }, text);
    this.toasts.append(toast);
    window.setTimeout(() => toast.remove(), TOAST_LIFETIME_MS);
  }

  chat(channel: string, sender: string, text: string): void {
    const label = CHAT_CHANNELS.find((entry) => entry.id === channel)?.label ?? channel;
    this.chatLines.append(h("p", { class: `chat-${channel}` }, h("span", { class: "muted" }, `[${label}] `), h("strong", {}, `${sender}: `), text));
    while (this.chatLines.childElementCount > MAX_CHAT_LINES) {
      this.chatLines.firstElementChild?.remove();
    }
    this.chatLines.scrollTop = this.chatLines.scrollHeight;
  }

  showDialogue(npcName: string, text: string, options: DialogueOptionDto[], onPick: (optionId: string) => void): void {
    this.dialogue.classList.remove("hidden");
    replaceChildren(this.dialogue,
      h("h3", {}, npcName),
      h("p", {}, text),
      h("div", { class: "dialogue-options" }, ...options.map((option) =>
        h("button", { onclick: () => onPick(option.id) }, option.text))));
  }

  hideDialogue(): void {
    this.dialogue.classList.add("hidden");
  }

  showCombat(panel: CombatPanel, actions: CombatActions): void {
    this.combat.classList.remove("hidden");
    this.hint.classList.add("hidden");
    const order = h("div", { class: "turn-order" }, ...panel.turnOrder.map((entry) =>
      h("span", { class: `turn-chip${entry.isCurrent ? " current" : ""}${entry.isFriendly ? " friendly" : " hostile"}${entry.alive ? "" : " dead"}` }, entry.name)));
    const status = panel.isMyTurn
      ? h("div", { class: "turn-status mine" }, `Ваш ход · ${Math.ceil(panel.secondsLeft)} с`)
      : h("div", { class: "turn-status" }, `Ходит ${panel.currentName} · раунд ${panel.round}`);
    const modeHint = panel.mode === "move" ? "Клик по клетке — переместиться, по врагу — атаковать"
      : panel.mode === "attack" ? "Выберите цель атаки"
        : "Выберите цель способности";
    const abilityButtons = panel.abilities.map((ability) => h("button", {
      class: `ability${panel.selectedAbilityId === ability.id ? " selected" : ""}`,
      disabled: !panel.isMyTurn,
      title: `${ability.description}${ability.spellLevel > 0 ? ` · ячейка ${ability.spellLevel} ур.` : ""}`,
      onclick: () => actions.onAbility(ability.id),
    }, ability.name, h("small", {}, costLabel(ability.cost))));
    replaceChildren(this.combat, order, status,
      h("div", { class: "combat-actions" },
        h("button", { disabled: !panel.isMyTurn, class: panel.mode === "move" ? "selected" : "", onclick: actions.onMoveMode }, "Движение"),
        h("button", { disabled: !panel.isMyTurn, class: panel.mode === "attack" ? "selected" : "", onclick: actions.onAttackMode }, "Атака"),
        ...abilityButtons,
        h("button", { disabled: !panel.isMyTurn, onclick: actions.onDodge }, "Уклонение"),
        h("button", { disabled: !panel.isMyTurn, class: "primary", onclick: actions.onEndTurn }, "Конец хода")),
      panel.isMyTurn ? h("p", { class: "muted" }, modeHint) : null);
  }

  hideCombat(): void {
    this.combat.classList.add("hidden");
    this.combatLog.classList.add("hidden");
    this.hint.classList.remove("hidden");
  }

  setCombatLog(lines: string[]): void {
    this.combatLog.classList.remove("hidden");
    replaceChildren(this.combatLog, ...lines.slice(-MAX_LOG_LINES).map((line) => h("p", {}, line)));
    this.combatLog.scrollTop = this.combatLog.scrollHeight;
  }
}

function bar(kind: string, ratio: number, text: string): HTMLElement {
  const fill = h("div", { class: "bar-fill" });
  fill.style.width = `${Math.round(Math.max(0, Math.min(1, ratio)) * 100)}%`;
  return h("div", { class: `bar ${kind}` }, fill, h("span", {}, text));
}

function costLabel(cost: AbilityDefinition["cost"]): string {
  return cost === "BonusAction" ? "бонус" : cost === "Free" ? "свободно" : "действие";
}
