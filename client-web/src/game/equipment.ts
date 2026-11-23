// Client mirror of Shared.Rules EquipmentRules.SlotsFor: which doll slots accept an item, for drag highlights.
// The server decides; this only picks what to light up and where a double-click sends an item.
import { ItemDefinition, ItemSlotKind } from "../api/content";
import { EquipmentSlot, EquipmentState } from "../api/gateway";

const SLOTS_BY_KIND: Record<ItemSlotKind, EquipmentSlot[]> = {
  None: [],
  Head: ["Head"],
  Amulet: ["Amulet"],
  Chest: ["Chest"],
  Weapon: ["MainHand"],
  TwoHanded: ["MainHand"],
  OffHand: ["OffHand"],
  Gloves: ["Gloves"],
  Belt: ["Belt"],
  Ring: ["LeftRing", "RightRing"],
  Boots: ["Boots"],
};

export function slotsFor(item: ItemDefinition | undefined): EquipmentSlot[] {
  return item ? SLOTS_BY_KIND[item.slot ?? "None"] ?? [] : [];
}

export function isWearable(item: ItemDefinition | undefined): boolean {
  return slotsFor(item).length > 0;
}

/** Where a double-click puts an item: the first empty matching slot, else the first matching one (swap). */
export function preferredSlot(item: ItemDefinition | undefined, equipment: EquipmentState): EquipmentSlot | null {
  const slots = slotsFor(item);
  return slots.find((slot) => !equipment.slots[slot]) ?? slots[0] ?? null;
}
