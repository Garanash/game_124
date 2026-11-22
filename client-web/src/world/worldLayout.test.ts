import { describe, expect, it } from "vitest";
import { ZoneDefinition } from "../api/content";
import { BiomeMap } from "./biomes";
import { distance, segmentDistanceToPoint } from "./geometry";
import { NavigationGrid } from "./navigation";
import { generateWorldLayout } from "./worldLayout";

const ZONE_SIZE = 1536;
const ELF_TOWN = { x: 400, z: 420 };
const DWARF_TOWN = { x: 1136, z: 420 };
const HUMAN_TOWN = { x: 768, z: 1120 };
const SOLID_DECOR = new Set(["tree", "elder_tree", "pine", "palm", "dead_tree", "rock", "boulder", "cliff"]);

const heartlands: ZoneDefinition = {
  id: "heartlands", name: "Срединные земли", continentId: "aldmark", kind: "open_world", minLevel: 1, maxLevel: 10,
  pvp: "none", sizeMeters: ZONE_SIZE, spawn: { x: 768, z: 768 },
  settlements: [
    { id: "silverleaf", name: "Сильверлиф", continentId: "eldarwen", center: ELF_TOWN },
    { id: "ironhold", name: "Железная Твердыня", continentId: "kargath", center: DWARF_TOWN },
    { id: "ashford", name: "Эшфорд", continentId: "aldmark", center: HUMAN_TOWN },
  ],
  portals: [], mobs: [], nodes: [],
};

const layout = generateWorldLayout({ zone: heartlands, npcs: [], mobs: {} });

describe("world layout", () => {
  it("is identical on every client", () => {
    const again = generateWorldLayout({ zone: heartlands, npcs: [], mobs: {} });
    expect(again.decor).toEqual(layout.decor);
    expect(again.obstacles).toEqual(layout.obstacles);
  });

  it("builds a town for every settlement", () => {
    expect(layout.towns).toHaveLength(heartlands.settlements.length);
    layout.towns.forEach((town, index) => expect(distance(town.center, heartlands.settlements[index].center)).toBeLessThan(1));
  });

  it("keeps trees and stones off town plazas and roads", () => {
    const solid = layout.decor.filter((piece) => SOLID_DECOR.has(piece.kind));
    for (const town of layout.towns) {
      expect(solid.some((piece) => distance(piece, town.center) < town.plazaRadius)).toBe(false);
      for (const road of town.roads) {
        expect(solid.some((piece) => segmentDistanceToPoint(road.from, road.to, piece) < road.width / 2)).toBe(false);
      }
    }
  });

  it("makes rocks and trees impassable but leaves grass walkable", () => {
    const grid = new NavigationGrid(ZONE_SIZE, layout.obstacles);
    const boulder = layout.decor.find((piece) => piece.kind === "boulder");
    const grass = layout.decor.find((piece) => piece.kind === "grass" && grid.isWalkable(piece));
    expect(boulder).toBeDefined();
    expect(grid.isWalkable(boulder!)).toBe(false);
    expect(grass).toBeDefined();
  });

  it("connects every town to the crossroads", () => {
    const grid = new NavigationGrid(ZONE_SIZE, layout.obstacles);
    for (const town of layout.towns) {
      expect(grid.findPath(town.center, heartlands.spawn).length).toBeGreaterThan(0);
    }
  });
});

describe("biomes", () => {
  const biomes = new BiomeMap("meadow", [{ center: ELF_TOWN, ground: "forest" }, { center: DWARF_TOWN, ground: "rocky" }]);

  it("colours the land around each town with its biome", () => {
    expect(biomes.dominantAt(ELF_TOWN)).toBe("forest");
    expect(biomes.dominantAt(DWARF_TOWN)).toBe("rocky");
    expect(biomes.dominantAt(HUMAN_TOWN)).toBe("meadow");
  });

  it("returns weights that sum to one", () => {
    const weights = biomes.weightsAt({ x: 600, z: 500 });
    expect(weights).toHaveLength(biomes.layers.length);
    expect(weights.reduce((sum, weight) => sum + weight, 0)).toBeCloseTo(1);
  });
});
