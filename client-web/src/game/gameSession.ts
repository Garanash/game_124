import { ContentCatalog, itemName, ZoneDefinition } from "../api/content";
import { CharacterState, GatewayClient } from "../api/gateway";
import { playerErrorText } from "../net/errorText";
import { CombatCommandType, EntityKind, ItemStack, ServerMessages } from "../net/protocol";
import { ZoneAddress, ZoneConnection } from "../net/zoneConnection";
import { Activity } from "../render/characters/rig";
import { CombatOverlay } from "../render/combatOverlay";
import { NpcLook } from "../render/models";
import { EntityPresentation, WorldRenderer } from "../render/worldRenderer";
import { DiceRoller } from "../ui/diceRoller";
import { Hud } from "../ui/hud";
import { populate } from "../world/ambientLife";
import { NavigationGrid } from "../world/navigation";
import { generateSettlement, settlementObstacles } from "../world/settlement";
import { CombatPresenter } from "./combatPresenter";
import { CombatState } from "./combatState";
import { npcActivity } from "./npcLife";
import { RouteFollower } from "./routeFollower";
import { knownAbilities, levelForXp, xpForNextLevel } from "./rules";
import { WorldEntity, WorldState } from "./worldState";

/** Matches ZoneSimulation.InteractionRangeMeters with a little slack for interpolation lag. */
const INTERACTION_RANGE_METERS = 3.5;
const PING_INTERVAL_MS = 5000;
const MAX_FRAME_SECONDS = 0.1;
const FAREWELL_OPTION = "bye";
const WANDERER_LABEL = "Странник";
/** Rewards are written by the character grain after the fight; refetch once it has had time to land. */
const REWARD_REFRESH_DELAY_MS = 1500;
/** Gap between the approach move and the attack so the server applies them in order within one turn. */
const ATTACK_AFTER_MOVE_MS = 120;
const DEFAULT_SPEED_CELLS = 6;
const MOVING_SPEED_EPSILON = 0.05;

type CombatMode = "move" | "attack" | "ability";

interface PendingInteraction { entityId: number }

/** One play session of one character: zone connection, world view, input and HUD. */
export class GameSession {
  private readonly world = new WorldState();
  private readonly renderer: WorldRenderer;
  private readonly overlay: CombatOverlay;
  private readonly hud: Hud;
  private readonly presenter: CombatPresenter;
  private readonly route = new RouteFollower();
  private readonly observedEncounters = new Map<string, CombatState>();
  /** Combatants can leave the interest area mid-fight; the log should still name them. */
  private readonly knownNames = new Map<number, string>();
  private connection: ZoneConnection | null = null;
  private zone: ZoneDefinition | null = null;
  private navigation: NavigationGrid | null = null;
  private combat: CombatState | null = null;
  private combatMode: CombatMode = "move";
  private selectedAbilityId: string | null = null;
  private hoverCell: { cellX: number; cellY: number } | null = null;
  private pending: PendingInteraction | null = null;
  private character: CharacterState | null = null;
  private selfMaxHp = 0;
  private selfHp = 0;
  private lastFrame = performance.now();
  private running = true;
  private pingTimer = 0;
  private combatPanelDirty = true;
  private lastCombatSecond = -1;
  private afterCombatShow: (() => void) | null = null;

  constructor(
    private readonly container: HTMLElement,
    private readonly gateway: GatewayClient,
    private readonly catalog: ContentCatalog,
    private readonly characterId: string,
    private readonly onLeave: (reason?: string) => void,
  ) {
    this.renderer = new WorldRenderer(container, (definitionId) => this.npcLook(definitionId));
    this.overlay = new CombatOverlay(this.renderer.scene);
    this.hud = new Hud((channel, text) => this.connection?.send("ChatSendRequest", { channel, text }), () => this.leave());
    container.appendChild(this.hud.root);
    this.presenter = new CombatPresenter({
      world: this.world,
      renderer: this.renderer,
      dice: new DiceRoller(),
      stage: this.hud.root,
      nameOf: (entityId) => this.nameOf(entityId),
      ability: (abilityId) => this.catalog.abilities[abilityId],
      onHpChanged: () => this.syncSelfHpFromWorld(),
    });
    this.bindInput();
  }

  async start(): Promise<void> {
    await this.refreshCharacter();
    const enter = await this.gateway.enterWorld(this.characterId);
    this.connect({ host: enter.host, webSocketPort: enter.webSocketPort, ticket: enter.ticket });
    this.pingTimer = window.setInterval(() => this.connection?.send("PingRequest", { clientTime: performance.now() / 1000 }), PING_INTERVAL_MS);
    requestAnimationFrame(() => this.frame());
  }

  leave(reason?: string): void {
    this.running = false;
    window.clearInterval(this.pingTimer);
    this.connection?.close();
    this.container.replaceChildren();
    this.onLeave(reason);
  }

  private connect(address: ZoneAddress): void {
    this.connection?.close();
    const connection = new ZoneConnection(address);
    this.connection = connection;
    connection.onClose((reason) => {
      if (this.connection === connection && this.running) {
        this.leave(`Связь с зоной потеряна: ${reason}`);
      }
    });
    connection.on("WelcomeMessage", (message) => this.onWelcome(message));
    connection.on("EntitySpawnMessage", (message) => this.onSpawn(message));
    connection.on("EntityDespawnMessage", (message) => this.world.despawn(message.entityId));
    connection.on("WorldSnapshotMessage", (message) => this.world.applySnapshot(message));
    connection.on("EncounterStartedMessage", (message) => this.onEncounterStarted(message));
    connection.on("CombatEventsMessage", (message) => this.onCombatEvents(message));
    connection.on("EncounterEndedMessage", (message) => this.onEncounterEnded(message));
    connection.on("ChatMessage", (message) => this.hud.chat(message.channel, message.senderName, message.text));
    connection.on("ErrorMessage", (message) => this.hud.toast(playerErrorText(message.code, message.text), "error"));
    connection.on("DialogueMessage", (message) => this.onDialogue(message));
    connection.on("QuestUpdateMessage", (message) => this.onQuestUpdate(message));
    connection.on("InventoryChangedMessage", (message) => this.onInventory(message.added, message.gold));
    connection.on("ZoneTransferMessage", (message) => this.onZoneTransfer(message));
  }

  private onWelcome(message: ServerMessages["WelcomeMessage"]): void {
    this.world.welcome(message);
    this.zone = this.catalog.zones[message.zoneId] ?? null;
    this.combat = null;
    this.observedEncounters.clear();
    this.presenter.clear();
    this.route.cancel();
    this.overlay.hide();
    this.hud.hideCombat();
    this.hud.hideDialogue();
    if (this.zone) {
      this.loadZone(this.zone);
    }
  }

  private loadZone(zone: ZoneDefinition): void {
    const npcs = Object.values(this.catalog.npcs).filter((npc) => npc.zoneId === zone.id);
    const settlement = generateSettlement({ zone, npcs, mobs: this.catalog.mobs });
    const navigation = new NavigationGrid(zone.sizeMeters, settlementObstacles(settlement));
    this.navigation = navigation;
    const residentRaces = Object.values(this.catalog.races).filter((race) => race.startZoneId === zone.id).map((race) => race.id);
    const raceIds = residentRaces.length > 0 ? residentRaces : [...new Set(npcs.map((npc) => npc.raceId))];
    this.renderer.loadZone({ zone, settlement, population: populate(settlement, zone.id, raceIds, navigation) });
    const continent = this.catalog.continents[zone.continentId]?.name ?? "";
    const pvp = zone.pvp !== "None" ? " · PvP" : "";
    this.hud.setZone(zone.name, `${continent} · ур. ${zone.minLevel}–${zone.maxLevel}${pvp}`);
  }

  private onSpawn(message: ServerMessages["EntitySpawnMessage"]): void {
    this.world.spawn(message);
    this.knownNames.set(message.entityId, message.name);
    if (message.entityId === this.world.selfId) {
      this.selfMaxHp = message.maxHp;
      this.selfHp = message.hp;
      this.updatePlayerPanel();
    }
  }

  private onEncounterStarted(message: ServerMessages["EncounterStartedMessage"]): void {
    const existing = this.observedEncounters.get(message.encounterId);
    const state = existing ?? CombatState.fromStarted(message);
    if (existing) {
      existing.resync(message);
    }
    this.observedEncounters.set(message.encounterId, state);
    for (const view of state.combatants.values()) {
      const entity = this.world.entities.get(view.entityId);
      if (entity) {
        entity.hp = view.hp;
        entity.maxHp = Math.max(entity.maxHp, view.maxHp);
      }
    }
    if (state.combatants.has(this.world.selfId) && this.combat !== state) {
      this.combat = state;
      this.combatMode = "move";
      this.selectedAbilityId = null;
      this.pending = null;
      this.route.cancel();
      this.overlay.show(state);
      this.hud.hideDialogue();
      this.presenter.announce("Бой!", "fight");
    }
    this.combatPanelDirty = true;
  }

  private onCombatEvents(message: ServerMessages["CombatEventsMessage"]): void {
    const state = this.observedEncounters.get(message.encounterId);
    if (!state) {
      return;
    }
    state.applyEvents(message, (id) => this.nameOf(id), (id) => this.catalog.abilities[id]?.name ?? id);
    this.presenter.enqueue(state, message.events, state === this.combat);
    if (state === this.combat) {
      this.hud.setCombatLog(state.log);
      this.combatPanelDirty = true;
    }
  }

  private onEncounterEnded(message: ServerMessages["EncounterEndedMessage"]): void {
    const ended = this.observedEncounters.get(message.encounterId);
    this.observedEncounters.delete(message.encounterId);
    if (this.combat?.encounterId !== message.encounterId) {
      return;
    }
    this.combat = null;
    this.overlay.hide();
    this.hud.hideCombat();
    this.creditXpLocally(message.victory ? message.xp : 0);
    this.afterCombatShow = () => this.presentOutcome(message, ended);
    window.setTimeout(() => void this.refreshCharacter(), REWARD_REFRESH_DELAY_MS);
  }

  /** Runs once the last dice and death animations have played, so the result never spoils them. */
  private presentOutcome(message: ServerMessages["EncounterEndedMessage"], ended: CombatState | undefined): void {
    if (ended) {
      this.presenter.reconcile(ended);
    }
    if (message.victory) {
      const loot = describeItems(this.catalog, message.items);
      this.presenter.announce(`Победа! +${message.xp} опыта`, "victory");
      this.hud.toast(`Победа! +${message.xp} опыта${message.gold ? `, +${message.gold} з.` : ""}${loot ? `, ${loot}` : ""}`, "loot");
    } else {
      this.presenter.announce("Поражение", "defeat");
      this.hud.toast("Поражение. Вы приходите в себя у входа в зону.", "error");
    }
  }

  private flushCombatOutcome(): void {
    if (this.afterCombatShow && this.presenter.idle) {
      const show = this.afterCombatShow;
      this.afterCombatShow = null;
      show();
    }
  }

  /** Shows the reward at once; the gateway refresh that follows replaces it with the stored value. */
  private creditXpLocally(xp: number): void {
    const sheet = this.character?.sheet;
    if (sheet && xp > 0) {
      const levelBefore = levelForXp(sheet.xp);
      sheet.xp += xp;
      if (levelForXp(sheet.xp) > levelBefore) {
        this.hud.toast(`Новый уровень: ${levelForXp(sheet.xp)}!`, "quest");
      }
      this.updatePlayerPanel();
    }
  }

  private onDialogue(message: ServerMessages["DialogueMessage"]): void {
    this.hud.showDialogue(message.npcName, message.text, message.options, (optionId) => {
      if (optionId === FAREWELL_OPTION) {
        this.hud.hideDialogue();
        return;
      }
      this.connection?.send("InteractRequest", { targetEntityId: message.npcEntityId, optionId });
    });
  }

  private onQuestUpdate(message: ServerMessages["QuestUpdateMessage"]): void {
    const quest = this.catalog.quests[message.questId];
    this.hud.toast(message.completed ? `Задание выполнено: ${quest?.name ?? message.questId}` : `Задание обновлено: ${quest?.name ?? message.questId}`, "quest");
    void this.refreshCharacter();
  }

  private onInventory(items: ItemStack[], gold: number): void {
    const loot = describeItems(this.catalog, items);
    if (loot || gold) {
      this.hud.toast(`Получено: ${[loot, gold ? `${gold} з.` : ""].filter(Boolean).join(", ")}`, "loot");
    }
    void this.refreshCharacter();
  }

  private onZoneTransfer(message: ServerMessages["ZoneTransferMessage"]): void {
    const target = this.catalog.zones[message.zoneId]?.name ?? message.zoneId;
    this.hud.toast(`Переход: ${target}`, "info");
    this.connect({ host: message.host, webSocketPort: message.webSocketPort, ticket: message.ticket });
  }

  private async refreshCharacter(): Promise<void> {
    try {
      this.character = await this.gateway.character(this.characterId);
      const hp = this.character.sheet.currentHp;
      if (!this.combat && hp > 0) {
        this.selfHp = hp;
        this.selfMaxHp = Math.max(this.selfMaxHp, hp);
      } else if (!this.combat) {
        this.selfHp = this.selfMaxHp;
      }
      this.updatePlayerPanel();
      this.updateQuests();
    } catch (error) {
      this.hud.toast(error instanceof Error ? error.message : String(error), "error");
    }
  }

  private syncSelfHpFromWorld(): void {
    const self = this.world.self;
    if (self && self.maxHp > 0) {
      this.selfHp = self.hp;
      this.selfMaxHp = self.maxHp;
      this.updatePlayerPanel();
    }
  }

  private updatePlayerPanel(): void {
    const sheet = this.character?.sheet;
    if (!sheet) {
      return;
    }
    const level = levelForXp(sheet.xp);
    const classes = Object.entries(sheet.classLevels)
      .filter(([classId]) => classId !== "wanderer")
      .map(([classId, classLevel]) => `${this.catalog.classes[classId]?.name ?? classId} ${classLevel}`);
    this.hud.setPlayer({
      name: sheet.name,
      raceId: sheet.raceId,
      raceName: this.catalog.races[sheet.raceId]?.name ?? sheet.raceId,
      level,
      classes: classes.length ? classes.join(", ") : WANDERER_LABEL,
      hp: this.selfHp,
      maxHp: this.selfMaxHp,
      xp: sheet.xp,
      xpForCurrent: level > 1 ? xpForNextLevel(level - 1) ?? 0 : 0,
      xpForNext: xpForNextLevel(level),
      gold: sheet.gold,
    });
  }

  private updateQuests(): void {
    const lines = (this.character?.activeQuests ?? []).filter((progress) => !progress.isComplete).map((progress) => {
      const quest = this.catalog.quests[progress.questId];
      const step = quest?.steps.find((candidate) => candidate.id === progress.stepId);
      const counter = step && step.count > 1 ? ` (${progress.count}/${step.count})` : "";
      return `${quest?.name ?? progress.questId}: ${step?.text ?? ""}${counter}`;
    });
    this.hud.setQuests(lines);
  }

  private frame(): void {
    if (!this.running) {
      return;
    }
    const now = performance.now();
    const delta = Math.min(MAX_FRAME_SECONDS, (now - this.lastFrame) / 1000);
    this.lastFrame = now;
    this.world.advance(delta);
    this.combat?.tick(delta);
    this.presenter.update(delta);
    this.flushCombatOutcome();
    this.followRoute();
    this.checkPendingInteraction();
    if (this.combat) {
      const myTeam = this.combat.combatants.get(this.world.selfId)?.team ?? 0;
      const ability = this.selectedAbilityId ? this.catalog.abilities[this.selectedAbilityId] : undefined;
      const area = this.combatMode === "ability" && ability?.target === "Area" ? ability.areaRadiusCells : 0;
      this.overlay.update(this.combat, myTeam, (id) => this.world.entities.has(id), this.isMyTurn() ? this.hoverCell : null, area);
      const second = Math.ceil(this.combat.turnTimeRemaining);
      if (this.combatPanelDirty || second !== this.lastCombatSecond) {
        this.lastCombatSecond = second;
        this.combatPanelDirty = false;
        this.renderCombatPanel();
      }
    }
    this.renderer.render(this.world, delta, this.presentation);
    requestAnimationFrame(() => this.frame());
  }

  private readonly presentation: EntityPresentation = {
    isTargetable: (entity) => this.isHostileCombatant(entity.id),
    activityOf: (entity) => this.activityOf(entity),
  };

  private activityOf(entity: WorldEntity): { animation: Activity; label: string } {
    if (entity.inCombat) {
      return { animation: "idle", label: "" };
    }
    switch (entity.kind) {
      case EntityKind.Npc: {
        const npc = this.catalog.npcs[entity.definitionId];
        return npc ? npcActivity(npc, this.world.latestServerTime, entity.latest) : { animation: "idle", label: "" };
      }
      case EntityKind.Mob:
        return { animation: this.catalog.mobs[entity.definitionId]?.isGame && !isMoving(entity) ? "graze" : "idle", label: "" };
      case EntityKind.Player:
        return entity.isGathering ? { animation: "gather", label: "собирает" } : { animation: "idle", label: "" };
      default:
        return { animation: "idle", label: "" };
    }
  }

  private renderCombatPanel(): void {
    const combat = this.combat;
    const sheet = this.character?.sheet;
    if (!combat) {
      return;
    }
    const myTeam = combat.combatants.get(this.world.selfId)?.team ?? 0;
    const abilities = sheet ? knownAbilities(sheet, this.catalog).map((id) => this.catalog.abilities[id]).filter(Boolean) : [];
    this.hud.showCombat({
      isMyTurn: this.isMyTurn(),
      currentName: this.nameOf(combat.currentTurnId),
      round: combat.round,
      secondsLeft: combat.turnTimeRemaining,
      turnOrder: combat.turnOrder.map((id) => ({
        name: this.nameOf(id),
        isCurrent: id === combat.currentTurnId,
        isFriendly: combat.combatants.get(id)?.team === myTeam,
        alive: combat.combatants.get(id)?.alive ?? false,
      })),
      abilities,
      selectedAbilityId: this.selectedAbilityId,
      mode: this.combatMode,
    }, {
      onMoveMode: () => this.setCombatMode("move"),
      onAttackMode: () => this.setCombatMode("attack"),
      onAbility: (abilityId) => this.selectAbility(abilityId),
      onDodge: () => this.combatCommand(CombatCommandType.Dodge),
      onEndTurn: () => this.combatCommand(CombatCommandType.EndTurn),
    });
  }

  private setCombatMode(mode: CombatMode): void {
    this.combatMode = mode;
    this.selectedAbilityId = null;
    this.combatPanelDirty = true;
  }

  private selectAbility(abilityId: string): void {
    const ability = this.catalog.abilities[abilityId];
    const me = this.combat?.combatants.get(this.world.selfId);
    if (ability?.target === "Self" && me) {
      this.combatCommand(CombatCommandType.UseAbility, { targetId: me.entityId, cellX: me.cellX, cellY: me.cellY, abilityId });
      return;
    }
    this.combatMode = "ability";
    this.selectedAbilityId = abilityId;
    this.combatPanelDirty = true;
  }

  private combatCommand(commandType: CombatCommandType, target: { targetId?: number; cellX?: number; cellY?: number; abilityId?: string } = {}): void {
    this.connection?.send("CombatCommandRequest", {
      commandType,
      targetId: target.targetId ?? 0,
      cellX: target.cellX ?? 0,
      cellY: target.cellY ?? 0,
      abilityId: target.abilityId ?? "",
    });
    if (commandType === CombatCommandType.UseAbility || commandType === CombatCommandType.Attack) {
      this.setCombatMode("move");
    }
  }

  /** A click on a distant enemy walks up to it first, so one click is enough for a melee swing. */
  private attack(targetId: number): void {
    const combat = this.combat!;
    const movementLeft = combat.movementLeft(this.world.selfId, this.speedCells());
    const plan = combat.approachCell(this.world.selfId, targetId, movementLeft);
    if (!plan) {
      this.combatCommand(CombatCommandType.Attack, { targetId });
      return;
    }
    if (movementLeft <= 0) {
      this.hud.toast("Цель далеко, а ход уже потрачен — нажмите «Конец хода» (Пробел).", "info");
      return;
    }
    this.combatCommand(CombatCommandType.Move, { cellX: plan.cellX, cellY: plan.cellY });
    if (plan.reachesTarget) {
      window.setTimeout(() => this.combatCommand(CombatCommandType.Attack, { targetId }), ATTACK_AFTER_MOVE_MS);
    } else {
      this.hud.toast("Подходите к цели — она дальше, чем вы успеете пройти за ход.", "info");
    }
  }

  private speedCells(): number {
    const raceId = this.character?.sheet.raceId ?? "";
    return this.catalog.races[raceId]?.speedCells ?? DEFAULT_SPEED_CELLS;
  }

  private isMyTurn(): boolean {
    return this.combat !== null && this.combat.currentTurnId === this.world.selfId;
  }

  private isHostileCombatant(entityId: number): boolean {
    const combat = this.combat;
    if (!combat) {
      return false;
    }
    const target = combat.combatants.get(entityId);
    const me = combat.combatants.get(this.world.selfId);
    return target !== undefined && me !== undefined && target.alive && target.team !== me.team;
  }

  private nameOf(entityId: number): string {
    if (entityId === this.world.selfId) {
      return this.character?.sheet.name ?? "Вы";
    }
    return this.world.entities.get(entityId)?.name ?? this.knownNames.get(entityId) ?? "Призванный";
  }

  private npcLook(definitionId: string): NpcLook | undefined {
    const npc = this.catalog.npcs[definitionId];
    return npc ? { raceId: npc.raceId, role: npc.role } : undefined;
  }

  private bindInput(): void {
    const canvas = this.renderer.canvas;
    canvas.addEventListener("pointerdown", (event) => {
      if (event.button === 0) {
        this.handleClick(event.clientX, event.clientY);
      }
    });
    canvas.addEventListener("pointermove", (event) => {
      const point = this.combat ? this.renderer.pickGround(event.clientX, event.clientY) : null;
      this.hoverCell = point && this.combat ? this.combat.worldToCell(point.x, point.z) : null;
    });
    canvas.addEventListener("wheel", (event) => {
      event.preventDefault();
      this.renderer.zoom(Math.sign(event.deltaY));
    }, { passive: false });
    window.addEventListener("keydown", (event) => {
      if (!this.running || this.hud.isTyping) {
        return;
      }
      if (event.key === "Enter") {
        event.preventDefault();
        this.hud.focusChat();
      } else if (event.key === "Escape") {
        this.hud.hideDialogue();
        this.setCombatMode("move");
      } else if (event.code === "Space" && this.isMyTurn()) {
        event.preventDefault();
        this.combatCommand(CombatCommandType.EndTurn);
      }
    });
  }

  private handleClick(clientX: number, clientY: number): void {
    if (this.combat) {
      this.handleCombatClick(clientX, clientY);
      return;
    }
    const pick = this.renderer.pick(clientX, clientY);
    const entity = pick.entityId !== null ? this.world.entities.get(pick.entityId) : undefined;
    if (entity && entity.id !== this.world.selfId) {
      this.interactWith(entity);
      return;
    }
    if (pick.point) {
      this.pending = null;
      this.renderer.showClickMarker(pick.point);
      this.walkTo(pick.point);
    }
  }

  /** Routes around buildings; falls back to a straight line when no path is known. */
  private walkTo(goal: { x: number; z: number }): void {
    const self = this.world.self;
    if (!self) {
      return;
    }
    const legs = this.navigation?.findPath(self.latest, goal) ?? [goal];
    const first = this.route.start(legs.length > 0 ? legs : [goal]);
    if (first) {
      this.connection?.send("MoveToRequest", { x: first.x, z: first.z });
    }
  }

  private followRoute(): void {
    const self = this.world.self;
    if (!self || !this.route.active || this.combat) {
      return;
    }
    const next = this.route.advance(self.latest);
    if (next) {
      this.connection?.send("MoveToRequest", { x: next.x, z: next.z });
    }
  }

  private handleCombatClick(clientX: number, clientY: number): void {
    const combat = this.combat!;
    if (!this.isMyTurn()) {
      this.hud.toast("Сейчас не ваш ход.", "info");
      return;
    }
    const pick = this.renderer.pick(clientX, clientY);
    const groundPoint = this.renderer.pickGround(clientX, clientY);
    const cell = groundPoint ? combat.worldToCell(groundPoint.x, groundPoint.z) : null;
    const clickedId = pick.entityId ?? (cell ? combat.combatantAt(cell.cellX, cell.cellY)?.entityId ?? null : null);
    const target = clickedId !== null ? combat.combatants.get(clickedId) : undefined;
    const ability = this.selectedAbilityId ? this.catalog.abilities[this.selectedAbilityId] : undefined;

    if (this.combatMode === "ability" && ability) {
      if (ability.target === "Area" && cell) {
        this.combatCommand(CombatCommandType.UseAbility, { abilityId: ability.id, cellX: cell.cellX, cellY: cell.cellY });
      } else if (target) {
        this.combatCommand(CombatCommandType.UseAbility, { abilityId: ability.id, targetId: target.entityId, cellX: target.cellX, cellY: target.cellY });
      }
      return;
    }
    if (target && this.isHostileCombatant(target.entityId)) {
      this.attack(target.entityId);
      return;
    }
    if (this.combatMode === "move" && cell && !target) {
      this.combatCommand(CombatCommandType.Move, { cellX: cell.cellX, cellY: cell.cellY });
    }
  }

  private interactWith(entity: WorldEntity): void {
    if (entity.inCombat) {
      const encounter = [...this.observedEncounters.values()].find((state) => state.combatants.has(entity.id));
      if (encounter) {
        this.connection?.send("JoinEncounterRequest", { encounterId: encounter.encounterId });
        return;
      }
    }
    if (entity.kind === EntityKind.Player) {
      this.route.cancel();
      this.connection?.send("InteractRequest", { targetEntityId: entity.id, optionId: "" });
      return;
    }
    this.pending = { entityId: entity.id };
    if (!this.tryCompleteInteraction()) {
      this.walkTo(entity.latest);
    }
  }

  /** Walking up to a roaming mob first means the fight opens in melee range instead of across the arena. */
  private checkPendingInteraction(): void {
    if (!this.pending || this.combat || this.tryCompleteInteraction()) {
      return;
    }
    const target = this.world.entities.get(this.pending.entityId);
    if (!target) {
      this.pending = null;
    } else if (!this.route.active) {
      this.walkTo(target.latest);
    }
  }

  private tryCompleteInteraction(): boolean {
    const self = this.world.self;
    const entity = this.pending ? this.world.entities.get(this.pending.entityId) : undefined;
    if (!self || !entity) {
      return false;
    }
    const distance = Math.hypot(self.latest.x - entity.latest.x, self.latest.z - entity.latest.z);
    if (distance > INTERACTION_RANGE_METERS) {
      return false;
    }
    this.pending = null;
    this.route.cancel();
    this.connection?.send("MoveToRequest", { x: self.latest.x, z: self.latest.z });
    if (entity.kind === EntityKind.Portal) {
      this.connection?.send("UsePortalRequest", { portalIndex: this.portalIndexOf(entity) });
    } else {
      this.connection?.send("InteractRequest", { targetEntityId: entity.id, optionId: "" });
    }
    return true;
  }

  private portalIndexOf(entity: WorldEntity): number {
    const portals = this.zone?.portals ?? [];
    let best = -1;
    let bestDistance = Number.POSITIVE_INFINITY;
    portals.forEach((portal, index) => {
      const distance = Math.hypot(portal.position.x - entity.latest.x, portal.position.z - entity.latest.z);
      if (portal.toZoneId === entity.definitionId && distance < bestDistance) {
        best = index;
        bestDistance = distance;
      }
    });
    return best;
  }
}

function isMoving(entity: WorldEntity): boolean {
  const samples = entity.samples;
  if (samples.length < 2) {
    return false;
  }
  const newest = samples[samples.length - 1];
  const older = samples[samples.length - 2];
  return Math.hypot(newest.x - older.x, newest.z - older.z) > MOVING_SPEED_EPSILON;
}

function describeItems(catalog: ContentCatalog, items: ItemStack[]): string {
  return items.map((item) => `${itemName(catalog, item.itemId)}${item.quantity > 1 ? ` ×${item.quantity}` : ""}`).join(", ");
}
