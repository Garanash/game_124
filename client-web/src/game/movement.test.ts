import { describe, expect, it } from "vitest";
import { playerErrorText } from "../net/errorText";
import { CombatEventType } from "../net/protocol";
import { CombatState } from "./combatState";
import { RouteFollower } from "./routeFollower";

const HERO = 1;
const DUMMY = 2;
const BLOCKER = 3;

function encounter(hero: [number, number], dummy: [number, number], blocker?: [number, number]): CombatState {
  const combatant = (entityId: number, team: number, [cellX, cellY]: [number, number]) =>
    ({ entityId, team, hp: 10, maxHp: 10, cellX, cellY, initiative: 10 });
  const combatants = [combatant(HERO, 0, hero), combatant(DUMMY, 1, dummy)];
  if (blocker) {
    combatants.push(combatant(BLOCKER, 1, blocker));
  }
  return CombatState.fromStarted({
    encounterId: "e", originX: 0, originZ: 0, width: 12, height: 12, combatants, turnOrder: combatants.map((c) => c.entityId),
  });
}

describe("approachCell", () => {
  it("does not move when already next to the target", () => {
    expect(encounter([3, 3], [4, 4]).approachCell(HERO, DUMMY, 6)).toBeNull();
  });

  it("stops on the nearest free cell beside the target", () => {
    const cell = encounter([0, 5], [5, 5]).approachCell(HERO, DUMMY, 6);
    expect(cell).toMatchObject({ cellX: 4, reachesTarget: true });
    expect(Math.abs(cell!.cellY - 5)).toBeLessThanOrEqual(1);
  });

  it("skips cells taken by other combatants", () => {
    const cell = encounter([0, 5], [5, 5], [4, 5]).approachCell(HERO, DUMMY, 6);
    expect(cell).not.toMatchObject({ cellX: 4, cellY: 5 });
    expect(cell!.cellX).toBe(4);
  });

  it("goes only as far as one turn of movement allows", () => {
    expect(encounter([0, 0], [11, 0]).approachCell(HERO, DUMMY, 4)).toEqual({ cellX: 4, cellY: 0, reachesTarget: false });
  });
});

describe("movementLeft", () => {
  it("spends cells on Moved and refills on the next turn", () => {
    const state = encounter([0, 0], [11, 0]);
    const event = (type: CombatEventType, value = 0) =>
      ({ type, actorId: HERO, targetId: 0, value, roll: 0, success: false, cellX: 3, cellY: 0, abilityId: null, condition: 0, damageType: 0 });
    const batch = (...events: ReturnType<typeof event>[]) =>
      state.applyEvents({ encounterId: "e", currentTurnEntityId: HERO, turnTimeRemaining: 30, round: 1, events }, String, String);
    batch(event(CombatEventType.TurnStarted), event(CombatEventType.Moved, 3));
    expect(state.movementLeft(HERO, 5)).toBe(2);
    batch(event(CombatEventType.TurnStarted));
    expect(state.movementLeft(HERO, 5)).toBe(5);
  });
});

describe("RouteFollower", () => {
  it("hands out the next leg once the current one is reached", () => {
    const route = new RouteFollower();
    expect(route.start([{ x: 1, z: 0 }, { x: 5, z: 0 }])).toEqual({ x: 1, z: 0 });
    expect(route.advance({ x: 0, z: 0 })).toBeNull();
    expect(route.advance({ x: 0.9, z: 0 })).toEqual({ x: 5, z: 0 });
    expect(route.advance({ x: 5, z: 0 })).toBeNull();
    expect(route.active).toBe(false);
  });
});

describe("playerErrorText", () => {
  it("translates known codes and keeps unknown ones as sent", () => {
    expect(playerErrorText("combat.out_of_range", "Target is out of range.")).toContain("далеко");
    expect(playerErrorText("misc.other", "Something")).toBe("Something");
  });
});
