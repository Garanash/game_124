import { describe, expect, it } from "vitest";
import { ItemDefinition, ItemSlotKind } from "../api/content";
import { isWearable, preferredSlot, slotsFor } from "./equipment";

function item(slot: ItemSlotKind): ItemDefinition {
  return { id: slot, name: slot, rarity: "Common", tags: [], slot, shape: [], modifiers: [], weaponDamage: "", weaponDamageType: "", maxStack: 1, value: 1 };
}

describe("equipment mirror", () => {
  it("sends one- and two-handed weapons to the main hand", () => {
    expect(slotsFor(item("Weapon"))).toEqual(["MainHand"]);
    expect(slotsFor(item("TwoHanded"))).toEqual(["MainHand"]);
  });

  it("keeps gems and potions in the backpack", () => {
    expect(isWearable(item("None"))).toBe(false);
  });

  it("fills the free ring finger before swapping", () => {
    const ring = item("Ring");
    expect(preferredSlot(ring, { slots: {} })).toBe("LeftRing");
    expect(preferredSlot(ring, { slots: { LeftRing: { instanceId: "a", itemId: "ring" } } })).toBe("RightRing");
    const bothWorn = { slots: { LeftRing: { instanceId: "a", itemId: "ring" }, RightRing: { instanceId: "b", itemId: "ring" } } };
    expect(preferredSlot(ring, bothWorn)).toBe("LeftRing");
  });
});
