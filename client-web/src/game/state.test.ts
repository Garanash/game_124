import { describe, expect, it } from "vitest";
import { CombatEventType, EntityKind } from "../net/protocol";
import { CombatState } from "./combatState";
import { WorldState } from "./worldState";

describe("world interpolation", () => {
  it("blends between the snapshots around the render time", () => {
    const world = new WorldState();
    world.welcome({ entityId: 1, zoneId: "z", sceneAddress: "", contentVersion: "", serverTime: 100 });
    world.spawn({ entityId: 1, kind: EntityKind.Player, name: "A", definitionId: "elf", cosmetics: [], x: 0, z: 0, hp: 10, maxHp: 10, level: 1 });
    world.applySnapshot({ tick: 1, serverTime: 100, entities: [{ entityId: 1, x: 0, z: 0, heading: 0, flags: 0 }] });
    world.applySnapshot({ tick: 2, serverTime: 100.1, entities: [{ entityId: 1, x: 10, z: 0, heading: 0, flags: 0 }] });

    expect(world.self!.positionAt(100.05).x).toBeCloseTo(5);
    expect(world.self!.positionAt(200).x).toBe(10);
  });
});

describe("combat state", () => {
  const started = {
    encounterId: "e1", originX: 10, originZ: 20, width: 21, height: 21, turnOrder: [1, 2],
    combatants: [
      { entityId: 1, team: 1, hp: 12, maxHp: 12, cellX: 9, cellY: 10, initiative: 15 },
      { entityId: 2, team: 2, hp: 7, maxHp: 7, cellX: 11, cellY: 10, initiative: 8 },
    ],
  };
  const event = (type: CombatEventType, actorId: number, targetId: number, value: number) => ({
    type, actorId, targetId, value, roll: 0, success: true, cellX: 0, cellY: 0, abilityId: "", condition: 0, damageType: 0,
  });

  it("maps cells to world points with 1.5 m cells", () => {
    const combat = CombatState.fromStarted(started);
    expect(combat.cellToWorld(2, 4)).toEqual({ x: 13, z: 26 });
    expect(combat.worldToCell(13.2, 25.9)).toEqual({ cellX: 2, cellY: 4 });
    expect(combat.worldToCell(0, 0)).toBeNull();
  });

  it("tracks damage, death and summons from events and writes a log", () => {
    const combat = CombatState.fromStarted(started);
    combat.applyEvents({
      encounterId: "e1", currentTurnEntityId: 2, turnTimeRemaining: 30, round: 1,
      events: [
        event(CombatEventType.Damaged, 1, 2, 4),
        event(CombatEventType.Damaged, 1, 2, 9),
        event(CombatEventType.Died, 1, 2, 0),
        event(CombatEventType.Summoned, 1, 900, 5),
      ],
    }, (id) => `#${id}`, (id) => id);

    expect(combat.combatants.get(2)).toMatchObject({ hp: 0, alive: false });
    expect(combat.combatants.get(900)).toMatchObject({ team: 1, hp: 5 });
    expect(combat.currentTurnId).toBe(2);
    expect(combat.log).toContain("#2 повержен");
  });
});
