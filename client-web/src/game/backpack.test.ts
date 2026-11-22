import { describe, expect, it } from "vitest";
import { ContentCatalog, ItemDefinition } from "../api/content";
import { BackpackState } from "../api/gateway";
import { canPlace, findFreeSpot, shapeCells, synergizingItems } from "./backpack";

function item(id: string, tags: string[], shape: { x: number; y: number }[] = []): ItemDefinition {
  return { id, name: id, rarity: "Common", tags, shape, modifiers: [], weaponDamage: "", weaponDamageType: "", maxStack: 1, value: 1 };
}

const catalog = {
  items: {
    sword: item("sword", ["weapon", "blade"], [{ x: 0, y: 0 }, { x: 0, y: 1 }]),
    whetstone: item("whetstone", ["stone"]),
  },
  synergies: { sharpened: { id: "sharpened", sourceTag: "blade", neighborTag: "stone", bonus: [] } },
} as unknown as ContentCatalog;

function backpack(...items: BackpackState["items"]): BackpackState {
  return { width: 3, height: 3, items };
}

describe("backpack mirror", () => {
  it("rotates a vertical sword into a horizontal one", () => {
    expect(shapeCells(catalog.items.sword, 1)).toEqual([{ x: 1, y: 0 }, { x: 0, y: 0 }]);
  });

  it("rejects overlap and out-of-bounds like the server", () => {
    const pack = backpack({ instanceId: "a", itemId: "sword", x: 0, y: 0, rotation: 0 });
    expect(canPlace(pack, catalog, "whetstone", { x: 0, y: 1, rotation: 0 })).toBe(false);
    expect(canPlace(pack, catalog, "sword", { x: 2, y: 2, rotation: 0 })).toBe(false);
    expect(canPlace(pack, catalog, "sword", { x: 1, y: 2, rotation: 1 })).toBe(true);
    expect(canPlace(pack, catalog, "sword", { x: 0, y: 1, rotation: 0 }, "a")).toBe(true);
  });

  it("finds the first free spot in server scan order", () => {
    const pack = backpack({ instanceId: "a", itemId: "sword", x: 0, y: 0, rotation: 0 });
    expect(findFreeSpot(pack, catalog, "whetstone")).toEqual({ x: 1, y: 0, rotation: 0 });
  });

  it("marks touching blade and stone as synergizing", () => {
    const apart = backpack({ instanceId: "a", itemId: "sword", x: 0, y: 0, rotation: 0 }, { instanceId: "b", itemId: "whetstone", x: 2, y: 0, rotation: 0 });
    expect(synergizingItems(apart, catalog).size).toBe(0);
    const touching = backpack({ instanceId: "a", itemId: "sword", x: 0, y: 0, rotation: 0 }, { instanceId: "b", itemId: "whetstone", x: 1, y: 1, rotation: 0 });
    expect([...synergizingItems(touching, catalog)].sort()).toEqual(["a", "b"]);
  });
});
