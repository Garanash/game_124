import { CombatantDto, CombatEventDto, CombatEventType, CONDITION_NAMES, ServerMessages } from "../net/protocol";

export const CELL_SIZE_METERS = 1.5;
/** Melee weapons reach the eight surrounding cells, as in CombatGrid.IsAdjacent on the server. */
const MELEE_REACH_CELLS = 1;

export interface ApproachPlan {
  cellX: number;
  cellY: number;
  /** False when the target is further than one turn of movement and the attack has to wait. */
  reachesTarget: boolean;
}

function chebyshev(a: { cellX: number; cellY: number }, b: { cellX: number; cellY: number }): number {
  return Math.max(Math.abs(a.cellX - b.cellX), Math.abs(a.cellY - b.cellY));
}

export interface CombatantView {
  entityId: number;
  team: number;
  hp: number;
  maxHp: number;
  cellX: number;
  cellY: number;
  alive: boolean;
}

export type NameResolver = (entityId: number) => string;
export type AbilityNameResolver = (abilityId: string) => string;

/** One turn-based encounter as seen by this client, rebuilt from server combat events. */
export class CombatState {
  readonly combatants = new Map<number, CombatantView>();
  readonly log: string[] = [];
  turnOrder: number[] = [];
  currentTurnId = 0;
  turnTimeRemaining = 0;
  round = 1;
  /** Cells walked in the current turn; Moved events carry the path cost, TurnStarted resets it. */
  private readonly movementSpent = new Map<number, number>();

  constructor(
    readonly encounterId: string,
    readonly originX: number,
    readonly originZ: number,
    readonly width: number,
    readonly height: number,
  ) {}

  static fromStarted(message: ServerMessages["EncounterStartedMessage"]): CombatState {
    const state = new CombatState(message.encounterId, message.originX, message.originZ, message.width, message.height);
    state.resync(message);
    return state;
  }

  /** Late joiners trigger a fresh EncounterStarted; keep the log, replace the roster. */
  resync(message: ServerMessages["EncounterStartedMessage"]): void {
    message.combatants.forEach((dto) => this.combatants.set(dto.entityId, CombatState.view(dto)));
    this.turnOrder = message.turnOrder;
  }

  cellToWorld(cellX: number, cellY: number): { x: number; z: number } {
    return { x: this.originX + cellX * CELL_SIZE_METERS, z: this.originZ + cellY * CELL_SIZE_METERS };
  }

  worldToCell(x: number, z: number): { cellX: number; cellY: number } | null {
    const cellX = Math.round((x - this.originX) / CELL_SIZE_METERS);
    const cellY = Math.round((z - this.originZ) / CELL_SIZE_METERS);
    const inBounds = cellX >= 0 && cellY >= 0 && cellX < this.width && cellY < this.height;
    return inBounds ? { cellX, cellY } : null;
  }

  /**
   * Cell to walk to before a melee attack: the free cell next to the target closest to the attacker,
   * or as far along the way as one turn of movement allows. Null when already adjacent.
   */
  approachCell(attackerId: number, targetId: number, movementCells: number): ApproachPlan | null {
    const attacker = this.combatants.get(attackerId);
    const target = this.combatants.get(targetId);
    if (!attacker || !target || chebyshev(attacker, target) <= MELEE_REACH_CELLS) {
      return null;
    }
    let best: { cellX: number; cellY: number } | null = null;
    let bestDistance = Number.POSITIVE_INFINITY;
    for (let offsetX = -MELEE_REACH_CELLS; offsetX <= MELEE_REACH_CELLS; offsetX++) {
      for (let offsetY = -MELEE_REACH_CELLS; offsetY <= MELEE_REACH_CELLS; offsetY++) {
        const cell = { cellX: target.cellX + offsetX, cellY: target.cellY + offsetY };
        const free = (offsetX !== 0 || offsetY !== 0) && this.inBounds(cell) && !this.combatantAt(cell.cellX, cell.cellY);
        const distance = chebyshev(attacker, cell);
        if (free && distance < bestDistance) {
          best = cell;
          bestDistance = distance;
        }
      }
    }
    if (!best) {
      return null;
    }
    if (bestDistance <= movementCells) {
      return { ...best, reachesTarget: true };
    }
    const fraction = movementCells / bestDistance;
    return {
      cellX: attacker.cellX + Math.round((best.cellX - attacker.cellX) * fraction),
      cellY: attacker.cellY + Math.round((best.cellY - attacker.cellY) * fraction),
      reachesTarget: false,
    };
  }

  movementLeft(entityId: number, speedCells: number): number {
    return Math.max(0, speedCells - (this.movementSpent.get(entityId) ?? 0));
  }

  isAdjacent(firstId: number, secondId: number): boolean {
    const first = this.combatants.get(firstId);
    const second = this.combatants.get(secondId);
    return first !== undefined && second !== undefined && chebyshev(first, second) <= MELEE_REACH_CELLS;
  }

  private inBounds(cell: { cellX: number; cellY: number }): boolean {
    return cell.cellX >= 0 && cell.cellY >= 0 && cell.cellX < this.width && cell.cellY < this.height;
  }

  combatantAt(cellX: number, cellY: number): CombatantView | undefined {
    return [...this.combatants.values()].find((view) => view.alive && view.cellX === cellX && view.cellY === cellY);
  }

  applyEvents(message: ServerMessages["CombatEventsMessage"], nameOf: NameResolver, abilityName: AbilityNameResolver): void {
    this.currentTurnId = message.currentTurnEntityId;
    this.turnTimeRemaining = message.turnTimeRemaining;
    this.round = message.round;
    for (const event of message.events) {
      this.apply(event);
      const line = describe(event, nameOf, abilityName);
      if (line) {
        this.log.push(line);
      }
    }
  }

  tick(deltaSeconds: number): void {
    this.turnTimeRemaining = Math.max(0, this.turnTimeRemaining - deltaSeconds);
  }

  private apply(event: CombatEventDto): void {
    const target = this.combatants.get(event.targetId) ?? this.combatants.get(event.actorId);
    switch (event.type) {
      case CombatEventType.Joined:
        if (!this.combatants.has(event.actorId)) {
          this.combatants.set(event.actorId, { entityId: event.actorId, team: 0, hp: 1, maxHp: 1, cellX: event.cellX, cellY: event.cellY, alive: true });
          this.turnOrder = [...this.turnOrder, event.actorId];
        }
        break;
      case CombatEventType.TurnStarted:
        this.movementSpent.set(event.actorId, 0);
        break;
      case CombatEventType.Moved: {
        const mover = this.combatants.get(event.actorId);
        if (mover) {
          mover.cellX = event.cellX;
          mover.cellY = event.cellY;
        }
        this.movementSpent.set(event.actorId, (this.movementSpent.get(event.actorId) ?? 0) + event.value);
        break;
      }
      case CombatEventType.Damaged:
        if (target) {
          target.hp = Math.max(0, target.hp - event.value);
        }
        break;
      case CombatEventType.Healed:
        if (target) {
          target.hp = Math.min(target.maxHp, target.hp + event.value);
        }
        break;
      case CombatEventType.Summoned: {
        const team = this.combatants.get(event.actorId)?.team ?? 0;
        this.combatants.set(event.targetId, {
          entityId: event.targetId, team, hp: event.value, maxHp: event.value, cellX: event.cellX, cellY: event.cellY, alive: true,
        });
        break;
      }
      case CombatEventType.Died:
        if (target) {
          target.hp = 0;
          target.alive = false;
        }
        break;
    }
  }

  private static view(dto: CombatantDto): CombatantView {
    return { entityId: dto.entityId, team: dto.team, hp: dto.hp, maxHp: dto.maxHp, cellX: dto.cellX, cellY: dto.cellY, alive: dto.hp > 0 };
  }
}

function describe(event: CombatEventDto, nameOf: NameResolver, abilityName: AbilityNameResolver): string | null {
  const actor = nameOf(event.actorId);
  const target = nameOf(event.targetId);
  switch (event.type) {
    case CombatEventType.RoundStarted:
      return `— Раунд ${event.value} —`;
    case CombatEventType.TurnStarted:
      return `Ход: ${actor}`;
    case CombatEventType.AttackRolled:
      return `${actor} атакует ${target}: к20 = ${event.roll}, итог ${event.value} — ${event.success ? "попадание" : "промах"}`;
    case CombatEventType.SaveRolled:
      return `${target} спасбросок: ${event.value} — ${event.success ? "успех" : "провал"}`;
    case CombatEventType.Damaged:
      return `${target} получает ${event.value} урона`;
    case CombatEventType.Healed:
      return `${target} восстанавливает ${event.value} HP`;
    case CombatEventType.ConditionApplied:
      return `${target}: ${CONDITION_NAMES[event.condition] ?? "эффект"}`;
    case CombatEventType.AbilityUsed:
      return `${actor} применяет «${abilityName(event.abilityId ?? "")}»`;
    case CombatEventType.Summoned:
      return `${actor} призывает союзника`;
    case CombatEventType.Dodged:
      return `${actor} уклоняется`;
    case CombatEventType.Died:
      return `${target} повержен`;
    default:
      return null;
  }
}
