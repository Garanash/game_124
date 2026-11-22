import { MobDefinition, NpcDefinition, ZoneDefinition } from "../api/content";
import { BiomeMap, groundForContinent, GroundKind } from "./biomes";
import { distance, Footprint, Point, seededRandom, segmentDistanceToPoint, segmentHitsFootprint } from "./geometry";
import { generateSettlement, hashText, Settlement, settlementObstacles } from "./settlement";

export type DecorKind = "tree" | "elder_tree" | "pine" | "palm" | "dead_tree" | "bush" | "rock" | "boulder" | "cliff" | "grass" | "flowers";

export interface DecorPiece {
  kind: DecorKind;
  x: number;
  z: number;
  scale: number;
  rotation: number;
  ground: GroundKind;
  /** 0..1, picks a colour variation so a forest is not one flat green. */
  shade: number;
}

/** Everything static in a zone, generated identically on every client from content alone. */
export interface WorldLayout {
  sizeMeters: number;
  towns: Settlement[];
  /** Towns plus the camps and ruins of the land between them. */
  settlements: Settlement[];
  biomes: BiomeMap;
  decor: DecorPiece[];
  /** Footprints that block walking: buildings, walls, solid props, trees, rocks and cliffs. */
  obstacles: Footprint[];
}

export interface WorldInput {
  zone: ZoneDefinition;
  npcs: NpcDefinition[];
  mobs: Record<string, MobDefinition>;
}

interface DecorRule { kind: DecorKind; perHectare: number; minScale: number; maxScale: number }

const DECOR_RULES: Record<GroundKind, DecorRule[]> = {
  forest: [
    { kind: "elder_tree", perHectare: 34, minScale: 0.9, maxScale: 1.6 },
    { kind: "tree", perHectare: 16, minScale: 0.8, maxScale: 1.3 },
    { kind: "bush", perHectare: 26, minScale: 0.7, maxScale: 1.3 },
    { kind: "rock", perHectare: 3, minScale: 0.6, maxScale: 1.2 },
    { kind: "grass", perHectare: 70, minScale: 0.7, maxScale: 1.3 },
    { kind: "flowers", perHectare: 14, minScale: 0.7, maxScale: 1.1 },
  ],
  meadow: [
    { kind: "tree", perHectare: 5, minScale: 0.9, maxScale: 1.5 },
    { kind: "bush", perHectare: 6, minScale: 0.7, maxScale: 1.2 },
    { kind: "rock", perHectare: 1.5, minScale: 0.6, maxScale: 1.1 },
    { kind: "grass", perHectare: 140, minScale: 0.7, maxScale: 1.4 },
    { kind: "flowers", perHectare: 45, minScale: 0.7, maxScale: 1.2 },
  ],
  rocky: [
    { kind: "pine", perHectare: 12, minScale: 0.9, maxScale: 1.7 },
    { kind: "rock", perHectare: 14, minScale: 0.7, maxScale: 1.6 },
    { kind: "boulder", perHectare: 3, minScale: 2.4, maxScale: 4.5 },
    { kind: "bush", perHectare: 3, minScale: 0.6, maxScale: 1 },
    { kind: "grass", perHectare: 30, minScale: 0.6, maxScale: 1.1 },
  ],
  sand: [
    { kind: "palm", perHectare: 3, minScale: 0.9, maxScale: 1.4 },
    { kind: "rock", perHectare: 3, minScale: 0.6, maxScale: 1.3 },
    { kind: "bush", perHectare: 2, minScale: 0.6, maxScale: 1 },
    { kind: "grass", perHectare: 12, minScale: 0.6, maxScale: 1 },
  ],
  ash: [
    { kind: "dead_tree", perHectare: 6, minScale: 0.8, maxScale: 1.4 },
    { kind: "rock", perHectare: 6, minScale: 0.7, maxScale: 1.5 },
    { kind: "boulder", perHectare: 1, minScale: 2.2, maxScale: 3.6 },
    { kind: "grass", perHectare: 8, minScale: 0.6, maxScale: 1 },
  ],
};

/** Trunk or stone radius per unit scale; foliage overhangs it, so heroes can brush past crowns. */
const BLOCKING_RADIUS: Partial<Record<DecorKind, number>> = {
  tree: 0.45, elder_tree: 0.6, pine: 0.4, palm: 0.35, dead_tree: 0.35, rock: 0.8, boulder: 0.85, cliff: 0.85,
};

const SAMPLE_CELL_METERS = 4;
const MASK_CELL_METERS = 2;
const TOWN_DECOR_MARGIN_METERS = 15;
const ROAD_DECOR_MARGIN_METERS = 2.5;
const BUILDING_DECOR_MARGIN_METERS = 3;
const SPAWN_CLEARING_METERS = 16;
const PORTAL_CLEARING_METERS = 10;
const NODE_CLEARING_METERS = 5;
const MOB_CLEARING_METERS = 9;
const NPC_STOP_CLEARING_METERS = 4;
const TOWN_NPC_RADIUS_METERS = 140;
const CLIFF_SPACING_METERS = 8;
const CLIFF_INSET_METERS = 3;
const CLIFF_MIN_SCALE = 3.5;
const CLIFF_MAX_SCALE = 6.5;
const HECTARE = 10_000;

/** Grass may grow in clearings; stones and trees may not. */
const HARD = 1;
const SOFT = 2;

export function generateWorldLayout(input: WorldInput): WorldLayout {
  const { zone } = input;
  const towns = planTowns(input);
  const wilds = zone.settlements.length > 0 ? planWilds(input, towns) : [];
  const settlements = [...towns, ...wilds];
  const biomes = new BiomeMap(groundForContinent(zone.continentId),
    zone.settlements.map((site) => ({ center: site.center, ground: groundForContinent(site.continentId) })));
  const mask = new ClearanceMask(zone.sizeMeters);
  reserveSettlements(mask, settlements);
  reserveSpots(mask, input);
  const decor = [...scatterDecor(zone, biomes, mask), ...borderCliffs(zone, biomes, mask)];
  const obstacles = [...settlements.flatMap(settlementObstacles), ...decor.flatMap(decorObstacle)];
  return { sizeMeters: zone.sizeMeters, towns, settlements, biomes, decor, obstacles };
}

/** Classic zones keep their single town at the spawn; open-world zones get one town per settlement. */
function planTowns({ zone, npcs, mobs }: WorldInput): Settlement[] {
  if (zone.settlements.length === 0) {
    return [generateSettlement({ zone, npcs, mobs })];
  }
  return zone.settlements.map((site) => generateSettlement({
    zone, mobs, site,
    npcs: npcs.filter((npc) => npc.schedule.some((entry) => distance(entry.position, site.center) < TOWN_NPC_RADIUS_METERS)),
    roadTargets: [zone.spawn],
  }));
}

/** Camps and ruins where hostile creatures live, minus anything that would sit on a road between towns. */
function planWilds({ zone, mobs }: WorldInput, towns: Settlement[]): Settlement[] {
  const wilds = generateSettlement({ zone, npcs: [], mobs, kind: "wilds" });
  const roads = towns.flatMap((town) => town.roads);
  const clearOfRoads = (footprint: Footprint) => !roads.some((road) => segmentHitsFootprint(road.from, road.to, footprint, road.width / 2 + 1));
  wilds.buildings = wilds.buildings.filter(clearOfRoads);
  wilds.props = wilds.props.filter((prop) => roads.every((road) => segmentDistanceToPoint(road.from, road.to, prop) > road.width / 2 + 1));
  return [wilds];
}

function reserveSettlements(mask: ClearanceMask, settlements: Settlement[]): void {
  for (const settlement of settlements) {
    if (settlement.kind === "town" || settlement.kind === "village") {
      mask.reserveDisc(settlement.center, settlement.radius + TOWN_DECOR_MARGIN_METERS, HARD);
    }
    for (const road of settlement.roads) {
      mask.reserveSegment(road.from, road.to, road.width / 2 + ROAD_DECOR_MARGIN_METERS, HARD);
    }
    for (const building of settlement.buildings) {
      mask.reserveDisc(building, Math.max(building.halfWidth, building.halfDepth) + BUILDING_DECOR_MARGIN_METERS, HARD);
    }
    for (const prop of settlement.props) {
      mask.reserveDisc(prop, 1.5, HARD);
    }
  }
}

function reserveSpots(mask: ClearanceMask, { zone, npcs }: WorldInput): void {
  mask.reserveDisc(zone.spawn, SPAWN_CLEARING_METERS, SOFT);
  zone.portals.forEach((portal) => mask.reserveDisc(portal.position, PORTAL_CLEARING_METERS, HARD));
  zone.nodes.forEach((node) => mask.reserveDisc(node.position, NODE_CLEARING_METERS, SOFT));
  zone.mobs.forEach((spawn) => mask.reserveDisc(spawn.position, MOB_CLEARING_METERS, SOFT));
  npcs.forEach((npc) => npc.schedule.forEach((entry) => mask.reserveDisc(entry.position, NPC_STOP_CLEARING_METERS, SOFT)));
}

function scatterDecor(zone: ZoneDefinition, biomes: BiomeMap, mask: ClearanceMask): DecorPiece[] {
  const random = seededRandom(hashText(`${zone.id}:decor`));
  const pieces: DecorPiece[] = [];
  const cells = Math.floor(zone.sizeMeters / SAMPLE_CELL_METERS);
  const cellArea = SAMPLE_CELL_METERS * SAMPLE_CELL_METERS;
  for (let row = 0; row < cells; row++) {
    for (let column = 0; column < cells; column++) {
      const point = { x: (column + random()) * SAMPLE_CELL_METERS, z: (row + random()) * SAMPLE_CELL_METERS };
      const ground = biomes.dominantAt(point);
      for (const rule of DECOR_RULES[ground]) {
        if (random() >= (rule.perHectare / HECTARE) * cellArea) {
          continue;
        }
        const blocking = BLOCKING_RADIUS[rule.kind] !== undefined;
        if (mask.isReserved(point, blocking ? HARD | SOFT : HARD)) {
          continue;
        }
        pieces.push({
          kind: rule.kind, x: point.x, z: point.z, ground,
          scale: rule.minScale + random() * (rule.maxScale - rule.minScale),
          rotation: random() * Math.PI * 2,
          shade: random(),
        });
        break;
      }
    }
  }
  return pieces;
}

/** A ring of cliffs frames the zone so its edge reads as mountains rather than the end of the floor. */
function borderCliffs(zone: ZoneDefinition, biomes: BiomeMap, mask: ClearanceMask): DecorPiece[] {
  const random = seededRandom(hashText(`${zone.id}:cliffs`));
  const size = zone.sizeMeters;
  const pieces: DecorPiece[] = [];
  for (let along = CLIFF_SPACING_METERS / 2; along < size; along += CLIFF_SPACING_METERS) {
    const edgePoints = [
      { x: along, z: CLIFF_INSET_METERS }, { x: along, z: size - CLIFF_INSET_METERS },
      { x: CLIFF_INSET_METERS, z: along }, { x: size - CLIFF_INSET_METERS, z: along },
    ];
    for (const point of edgePoints) {
      if (mask.isReserved(point, HARD)) {
        continue;
      }
      pieces.push({
        kind: "cliff", x: point.x, z: point.z, ground: biomes.dominantAt(point),
        scale: CLIFF_MIN_SCALE + random() * (CLIFF_MAX_SCALE - CLIFF_MIN_SCALE),
        rotation: random() * Math.PI * 2,
        shade: random(),
      });
    }
  }
  return pieces;
}

function decorObstacle(piece: DecorPiece): Footprint[] {
  const radius = BLOCKING_RADIUS[piece.kind];
  if (radius === undefined) {
    return [];
  }
  const half = radius * piece.scale;
  return [{ x: piece.x, z: piece.z, halfWidth: half, halfDepth: half, rotation: piece.rotation }];
}

/** Coarse occupancy grid so scattering thousands of pieces needs no per-building distance checks. */
class ClearanceMask {
  private readonly cells: number;
  private readonly flags: Uint8Array;

  constructor(sizeMeters: number) {
    this.cells = Math.ceil(sizeMeters / MASK_CELL_METERS);
    this.flags = new Uint8Array(this.cells * this.cells);
  }

  isReserved(point: Point, flags: number): boolean {
    const column = Math.floor(point.x / MASK_CELL_METERS);
    const row = Math.floor(point.z / MASK_CELL_METERS);
    if (column < 0 || row < 0 || column >= this.cells || row >= this.cells) {
      return true;
    }
    return (this.flags[row * this.cells + column] & flags) !== 0;
  }

  reserveDisc(center: Point, radius: number, flag: number): void {
    this.reserveWhere(center, center, radius, flag, (point) => distance(point, center) <= radius);
  }

  reserveSegment(from: Point, to: Point, radius: number, flag: number): void {
    this.reserveWhere(from, to, radius, flag, (point) => segmentDistanceToPoint(from, to, point) <= radius);
  }

  private reserveWhere(a: Point, b: Point, radius: number, flag: number, inside: (point: Point) => boolean): void {
    const minColumn = Math.max(0, Math.floor((Math.min(a.x, b.x) - radius) / MASK_CELL_METERS));
    const maxColumn = Math.min(this.cells - 1, Math.floor((Math.max(a.x, b.x) + radius) / MASK_CELL_METERS));
    const minRow = Math.max(0, Math.floor((Math.min(a.z, b.z) - radius) / MASK_CELL_METERS));
    const maxRow = Math.min(this.cells - 1, Math.floor((Math.max(a.z, b.z) + radius) / MASK_CELL_METERS));
    for (let row = minRow; row <= maxRow; row++) {
      for (let column = minColumn; column <= maxColumn; column++) {
        const point = { x: (column + 0.5) * MASK_CELL_METERS, z: (row + 0.5) * MASK_CELL_METERS };
        if (inside(point)) {
          this.flags[row * this.cells + column] |= flag;
        }
      }
    }
  }
}
