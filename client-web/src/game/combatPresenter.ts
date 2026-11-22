import * as THREE from "three";
import { AbilityDefinition } from "../api/content";
import { CombatEventDto, CombatEventType, CONDITION_NAMES } from "../net/protocol";
import { damageColor, isPhysical } from "../render/combatEffects";
import { WorldRenderer } from "../render/worldRenderer";
import { DiceRoller } from "../ui/diceRoller";
import { h } from "../ui/dom";
import { CELL_SIZE_METERS, CombatState } from "./combatState";
import { WorldState } from "./worldState";

const MELEE_REACH_METERS = CELL_SIZE_METERS * 1.6;
const PIERCING_DAMAGE = 1;
const HEAL_COLOR = 0x7dffa8;
const SHORT_BEAT_SECONDS = 0.18;
const ACTION_BEAT_SECONDS = 0.45;
const IMPACT_BEAT_SECONDS = 0.35;
const DEATH_BEAT_SECONDS = 0.7;
const BANNER_SECONDS = 1.3;
const FAST_QUEUE_LENGTH = 4;
const VERY_FAST_QUEUE_LENGTH = 10;
const CRIT_SHAKE = 0.6;
const HIT_SHAKE = 0.18;
const DEATH_SHAKE = 0.35;

type Beat = (speed: number) => Promise<void> | number;

interface EncounterContext {
  state: CombatState;
  /** Only the player's own fight gets the centre-screen dice; everyone else's is just animated. */
  showDice: boolean;
}

export interface PresenterHost {
  world: WorldState;
  renderer: WorldRenderer;
  dice: DiceRoller;
  stage: HTMLElement;
  nameOf(entityId: number): string;
  ability(abilityId: string): AbilityDefinition | undefined;
  /** Called whenever displayed HP changes so the HUD can follow. */
  onHpChanged(): void;
}

/**
 * Turns the server's instant combat event batches into a paced show. HP shown on bars is applied
 * at the moment the hit lands, then reconciled with the authoritative state once the queue drains.
 */
export class CombatPresenter {
  private readonly queue: Beat[] = [];
  private readonly banner = h("div", { class: "combat-banner hidden" });
  private readonly contexts = new Set<EncounterContext>();
  private waitSeconds = 0;
  private waitingOnPromise = false;
  private bannerTimer = 0;

  constructor(private readonly host: PresenterHost) {
    host.stage.append(host.dice.root, this.banner);
  }

  get idle(): boolean {
    return this.queue.length === 0 && this.waitSeconds <= 0 && !this.waitingOnPromise;
  }

  announce(text: string, kind: "fight" | "turn" | "round" | "victory" | "defeat"): void {
    this.banner.textContent = text;
    this.banner.className = `combat-banner ${kind}`;
    void this.banner.offsetWidth;
    this.banner.classList.add("show");
    this.bannerTimer = BANNER_SECONDS;
  }

  enqueue(state: CombatState, events: CombatEventDto[], showDice: boolean): void {
    const context = { state, showDice };
    this.contexts.add(context);
    events.forEach((event, index) => {
      const beat = this.beatFor(context, event, events.slice(index + 1));
      if (beat) {
        this.queue.push(beat);
      }
    });
    this.queue.push(() => {
      this.contexts.delete(context);
      return 0;
    });
  }

  /** Makes displayed HP match the server once nothing is left to show. */
  reconcile(state: CombatState): void {
    for (const view of state.combatants.values()) {
      const entity = this.host.world.entities.get(view.entityId);
      if (entity) {
        entity.hp = view.hp;
        entity.maxHp = Math.max(entity.maxHp, view.maxHp);
      }
    }
    this.host.onHpChanged();
  }

  clear(): void {
    this.queue.length = 0;
    this.waitSeconds = 0;
    this.contexts.clear();
  }

  update(deltaSeconds: number): void {
    this.host.dice.update(deltaSeconds);
    this.bannerTimer -= deltaSeconds;
    if (this.bannerTimer <= 0) {
      this.banner.classList.remove("show");
    }
    if (this.waitingOnPromise) {
      return;
    }
    this.waitSeconds -= deltaSeconds;
    while (this.waitSeconds <= 0 && !this.waitingOnPromise && this.queue.length > 0) {
      const speed = this.queue.length > VERY_FAST_QUEUE_LENGTH ? 4 : this.queue.length > FAST_QUEUE_LENGTH ? 2 : 1;
      const result = this.queue.shift()!(speed);
      if (typeof result === "number") {
        this.waitSeconds += result / speed;
      } else {
        this.waitingOnPromise = true;
        void result.finally(() => {
          this.waitingOnPromise = false;
          this.waitSeconds = 0;
        });
      }
    }
  }

  private beatFor(context: EncounterContext, event: CombatEventDto, following: CombatEventDto[]): Beat | null {
    const { renderer } = this.host;
    switch (event.type) {
      case CombatEventType.RoundStarted:
        return () => {
          if (context.showDice && event.value > 1) {
            this.announce(`Раунд ${event.value}`, "round");
          }
          return SHORT_BEAT_SECONDS;
        };
      case CombatEventType.TurnStarted:
        return () => {
          if (context.showDice && event.actorId === this.host.world.selfId) {
            this.announce("Ваш ход!", "turn");
          }
          return SHORT_BEAT_SECONDS;
        };
      case CombatEventType.AttackRolled:
        return (speed) => this.attackBeat(context, event, speed);
      case CombatEventType.SaveRolled:
        return (speed) => this.saveBeat(context, event, speed);
      case CombatEventType.AbilityUsed:
        return () => this.abilityBeat(context, event, following);
      case CombatEventType.Damaged:
        return () => this.damageBeat(event);
      case CombatEventType.Healed:
        return () => {
          this.changeHp(event.targetId, event.value);
          this.float(event.targetId, `+${event.value}`, "heal");
          const position = renderer.entityPosition(event.targetId);
          if (position) {
            renderer.effects.sparkle(position, HEAL_COLOR);
          }
          return IMPACT_BEAT_SECONDS;
        };
      case CombatEventType.ConditionApplied:
        return () => {
          this.float(event.targetId, CONDITION_NAMES[event.condition] ?? "эффект", "status");
          return SHORT_BEAT_SECONDS;
        };
      case CombatEventType.Dodged:
        return () => {
          renderer.playAction(event.actorId, "dodge");
          this.float(event.actorId, "Уклонение", "status");
          return SHORT_BEAT_SECONDS;
        };
      case CombatEventType.Died:
        return () => {
          const entity = this.host.world.entities.get(event.targetId);
          if (entity) {
            entity.hp = 0;
          }
          this.host.onHpChanged();
          renderer.setDead(event.targetId, true);
          this.float(event.targetId, "Повержен", "death");
          if (context.showDice) {
            renderer.shake(DEATH_SHAKE);
          }
          if (event.actorId && event.actorId !== event.targetId) {
            renderer.playAction(event.actorId, "cheer");
          }
          return DEATH_BEAT_SECONDS;
        };
      case CombatEventType.Summoned:
        return () => {
          const position = renderer.entityPosition(event.targetId) ?? this.cellPosition(context.state, event);
          renderer.effects.burst(position, 8, 1);
          return ACTION_BEAT_SECONDS;
        };
      default:
        return null;
    }
  }

  private async attackBeat(context: EncounterContext, event: CombatEventDto, speed: number): Promise<void> {
    const { renderer } = this.host;
    if (context.showDice) {
      await this.host.dice.roll({
        natural: event.roll,
        total: event.value,
        title: `${this.host.nameOf(event.actorId)} атакует ${this.host.nameOf(event.targetId)}`,
        outcome: event.success ? "hit" : "miss",
        friendly: this.isFriendly(context.state, event.actorId),
      }, speed);
    }
    renderer.playAction(event.actorId, "attack", event.targetId);
    const from = renderer.entityPosition(event.actorId);
    const to = renderer.entityPosition(event.targetId);
    if (from && to && from.distanceTo(to) > MELEE_REACH_METERS) {
      await new Promise<void>((resolve) => renderer.effects.projectile(from, to, PIERCING_DAMAGE, resolve));
    } else {
      await delay(250 / speed);
    }
    if (!event.success) {
      renderer.playAction(event.targetId, "dodge");
      this.float(event.targetId, "Промах", "miss");
    } else if (to && from) {
      renderer.effects.slash(to, Math.atan2(to.x - from.x, to.z - from.z), event.roll === 20 ? 0xffd34a : 0xffffff);
    }
  }

  private async saveBeat(context: EncounterContext, event: CombatEventDto, speed: number): Promise<void> {
    if (!context.showDice) {
      return;
    }
    await this.host.dice.roll({
      natural: event.roll || event.value,
      total: event.value,
      title: `Спасбросок: ${this.host.nameOf(event.targetId)}`,
      outcome: event.success ? "success" : "fail",
      friendly: this.isFriendly(context.state, event.targetId),
    }, speed);
  }

  private async abilityBeat(context: EncounterContext, event: CombatEventDto, following: CombatEventDto[]): Promise<void> {
    const { renderer } = this.host;
    const ability = this.host.ability(event.abilityId ?? "");
    const effect = following.find((next) => next.type === CombatEventType.Damaged || next.type === CombatEventType.Healed);
    const damageType = effect?.type === CombatEventType.Damaged ? effect.damageType : 8;
    renderer.playAction(event.actorId, "cast", event.targetId || undefined);
    this.float(event.actorId, ability?.name ?? "Умение", "status");
    const from = renderer.entityPosition(event.actorId);
    if (!from) {
      return;
    }
    if (ability?.target === "Area") {
      const center = this.cellPosition(context.state, event);
      await new Promise<void>((resolve) => renderer.effects.projectile(from, center, damageType, resolve));
      renderer.effects.burst(center, damageType, Math.max(1, ability.areaRadiusCells) * CELL_SIZE_METERS);
      renderer.shake(HIT_SHAKE);
      return;
    }
    const to = event.targetId ? renderer.entityPosition(event.targetId) : null;
    if (to && event.targetId !== event.actorId && ability?.target !== "Ally" && !isPhysical(damageType)) {
      await new Promise<void>((resolve) => renderer.effects.projectile(from, to, damageType, resolve));
      return;
    }
    renderer.effects.sparkle(to ?? from, effect?.type === CombatEventType.Healed ? HEAL_COLOR : damageColor(damageType));
    await delay(300);
  }

  private damageBeat(event: CombatEventDto): number {
    const { renderer } = this.host;
    const critical = event.roll === 20;
    this.changeHp(event.targetId, -event.value);
    renderer.playAction(event.targetId, "hit");
    this.float(event.targetId, critical ? `${event.value}!` : `-${event.value}`, critical ? "crit" : "damage");
    const position = renderer.entityPosition(event.targetId);
    if (position && !isPhysical(event.damageType)) {
      renderer.effects.burst(position, event.damageType, 0.8);
    }
    renderer.shake(critical || event.targetId === this.host.world.selfId ? CRIT_SHAKE : HIT_SHAKE);
    return IMPACT_BEAT_SECONDS;
  }

  private changeHp(entityId: number, delta: number): void {
    const entity = this.host.world.entities.get(entityId);
    if (entity) {
      entity.hp = THREE.MathUtils.clamp(entity.hp + delta, 0, entity.maxHp);
      this.host.onHpChanged();
    }
  }

  private float(entityId: number, text: string, kind: Parameters<WorldRenderer["effects"]["floatText"]>[2]): void {
    const position = this.host.renderer.entityPosition(entityId);
    if (position) {
      this.host.renderer.effects.floatText(position.add(new THREE.Vector3(0, 0.8, 0)), text, kind);
    }
  }

  private cellPosition(state: CombatState, event: CombatEventDto): THREE.Vector3 {
    const world = state.cellToWorld(event.cellX, event.cellY);
    return new THREE.Vector3(world.x, 0.8, world.z);
  }

  private isFriendly(state: CombatState, entityId: number): boolean {
    const myTeam = state.combatants.get(this.host.world.selfId)?.team;
    return myTeam === undefined ? false : state.combatants.get(entityId)?.team === myTeam;
  }
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
}
