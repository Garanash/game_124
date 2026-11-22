import { MobDefinition, NpcDefinition, ZoneDefinition } from "../api/content";
import {
  distance, Footprint, footprintsOverlap, Point, seededRandom, segmentDistanceToPoint, segmentHitsFootprint, segmentsIntersect,
} from "./geometry";

export type BuildingKind =
  | "house" | "tavern" | "guildhall" | "townhall" | "market_stall" | "auction_house" | "workshop"
  | "watchtower" | "tent" | "ruin" | "barn" | "shrine";

export type PropKind =
  | "lamp" | "barrel" | "crate" | "cart" | "bench" | "fountain" | "campfire" | "banner"
  | "tree" | "haystack" | "anvil" | "well" | "pillar" | "brazier" | "fence" | "garden" | "notice_board";

export interface Building extends Footprint {
  kind: BuildingKind;
  seed: number;
  /** Where the door opens onto; NPCs working here stand at this point. */
  door: Point;
}

export interface PropPlacement { kind: PropKind; x: number; z: number; rotation: number; blocking: boolean }
export interface WallSegment { from: Point; to: Point }
export interface Road { from: Point; to: Point; width: number }

export type SettlementKind = "town" | "village" | "wilds" | "dungeon" | "battlefield";

export interface Settlement {
  kind: SettlementKind;
  continentId: string;
  center: Point;
  radius: number;
  plazaRadius: number;
  buildings: Building[];
  props: PropPlacement[];
  walls: WallSegment[];
  gateTowers: Point[];
  roads: Road[];
  /** Spots ambient villagers walk between: doors, stalls and the plaza. */
  pointsOfInterest: Point[];
}

export interface SettlementInput {
  zone: ZoneDefinition;
  npcs: NpcDefinition[];
  mobs: Record<string, MobDefinition>;
  /** A town of an open-world zone; without it the zone kind decides the layout around the spawn. */
  site?: SettlementSite;
  /** Far points the main roads lead to besides the portals, e.g. the crossroads between towns. */
  roadTargets?: Point[];
  /** Forces the layout, e.g. "wilds" for the land between the towns of an open-world zone. */
  kind?: SettlementKind;
}

export interface SettlementSite { id: string; continentId: string; center: Point }

const BUILDING_SIZES: Record<BuildingKind, { width: number; depth: number }> = {
  house: { width: 7, depth: 6 },
  tavern: { width: 11, depth: 9 },
  guildhall: { width: 12, depth: 10 },
  townhall: { width: 13, depth: 10 },
  market_stall: { width: 4, depth: 2.6 },
  auction_house: { width: 12, depth: 9 },
  workshop: { width: 8, depth: 6.5 },
  watchtower: { width: 4, depth: 4 },
  tent: { width: 4, depth: 4 },
  ruin: { width: 6, depth: 5 },
  barn: { width: 9, depth: 7 },
  shrine: { width: 5, depth: 5 },
};

const ACTIVITY_BUILDINGS: Record<string, BuildingKind> = {
  teach: "guildhall",
  council: "townhall",
  tavern: "tavern",
  trade: "market_stall",
  home: "house",
  sleep: "house",
  auction: "auction_house",
  workshop: "workshop",
};

const TOWN_SEARCH_RADIUS = 95;
const MIN_TOWN_RADIUS = 42;
const MAX_TOWN_RADIUS = 80;
const TOWN_MARGIN = 14;
const PLAZA_RADIUS = 11;
const DOOR_CLEARANCE = 1.2;
const BUILDING_GAP = 1.6;
const PATH_CLEARANCE = 1.8;
const ROAD_WIDTH = 4;
const WALL_SIDES = 36;
const WALL_OFFSET = 7;
const SPAWN_KEEP_CLEAR = 12;
const PORTAL_KEEP_CLEAR = 9;
const NODE_KEEP_CLEAR = 6;
const MOB_KEEP_CLEAR = 9;
const FILLER_ATTEMPTS = 220;
const LAMP_SPACING = 13;
const HOSTILE_CAMP_PATTERN = /bandit|goblin|cultist|sahuagin/;
const UNDEAD_PATTERN = /skeleton|crypt|ghoul/;

/** Builds the same layout on every client from content alone, so towns need no extra server data. */
export function generateSettlement(input: SettlementInput): Settlement {
  const { zone, site } = input;
  const random = seededRandom(hashText(site ? `${zone.id}:${site.id}` : zone.id));
  const kind = input.kind ?? (site ? "town" : settlementKind(zone.kind));
  const settlement: Settlement = {
    kind, continentId: site?.continentId ?? zone.continentId, center: site?.center ?? zone.spawn, radius: 0, plazaRadius: PLAZA_RADIUS,
    buildings: [], props: [], walls: [], gateTowers: [], roads: [], pointsOfInterest: [],
  };
  const planner = new Planner(settlement, input, random);
  switch (kind) {
    case "town":
      planner.planTown();
      break;
    case "village":
      planner.planVillage();
      break;
    case "wilds":
      planner.planWilds();
      break;
    case "dungeon":
      planner.planDungeon();
      break;
    case "battlefield":
      planner.planBattlefield();
      break;
  }
  return settlement;
}

/** Everything that blocks walking: buildings, walls and solid props. */
export function settlementObstacles(settlement: Settlement): Footprint[] {
  const obstacles: Footprint[] = settlement.buildings
    .filter((building) => building.kind !== "market_stall")
    .map((building) => ({ ...building }));
  for (const wall of settlement.walls) {
    const length = distance(wall.from, wall.to);
    obstacles.push({
      x: (wall.from.x + wall.to.x) / 2,
      z: (wall.from.z + wall.to.z) / 2,
      halfWidth: length / 2 + 0.3,
      halfDepth: 0.6,
      rotation: wallRotation(wall),
    });
  }
  for (const tower of settlement.gateTowers) {
    obstacles.push({ x: tower.x, z: tower.z, halfWidth: 1.6, halfDepth: 1.6, rotation: 0 });
  }
  for (const prop of settlement.props.filter((candidate) => candidate.blocking)) {
    const size = prop.kind === "fountain" || prop.kind === "well" ? 1.6 : 0.6;
    obstacles.push({ x: prop.x, z: prop.z, halfWidth: size, halfDepth: size, rotation: prop.rotation });
  }
  return obstacles;
}

function settlementKind(zoneKind: string): SettlementKind {
  switch (zoneKind) {
    case "StartZone":
    case "City":
      return "town";
    case "Housing":
      return "village";
    case "Instance":
      return "dungeon";
    case "PvpZone":
      return "battlefield";
    default:
      return "wilds";
  }
}

class Planner {
  private readonly footprints: Footprint[] = [];
  private readonly walkways: Array<[Point, Point]> = [];
  private readonly keepClear: Array<{ point: Point; radius: number }> = [];

  constructor(private readonly settlement: Settlement, private readonly input: SettlementInput, private readonly random: () => number) {
    const zone = input.zone;
    this.keepClear.push({ point: zone.spawn, radius: SPAWN_KEEP_CLEAR }, { point: settlement.center, radius: SPAWN_KEEP_CLEAR });
    zone.portals.forEach((portal) => this.keepClear.push({ point: portal.position, radius: PORTAL_KEEP_CLEAR }));
    (zone.nodes ?? []).forEach((node) => this.keepClear.push({ point: node.position, radius: NODE_KEEP_CLEAR }));
    (zone.mobs ?? []).forEach((mob) => this.keepClear.push({ point: mob.position, radius: MOB_KEEP_CLEAR }));
    for (const npc of input.npcs) {
      const stops = npc.schedule.map((entry) => entry.position);
      stops.forEach((stop, index) => this.walkways.push([stop, stops[(index + 1) % stops.length]]));
    }
  }

  planTown(): void {
    const center = this.settlement.center;
    const workplaces = this.workplaces();
    const farthest = Math.max(0, ...workplaces.map((workplace) => distance(workplace.point, center)));
    this.settlement.radius = clamp(farthest + TOWN_MARGIN, MIN_TOWN_RADIUS, MAX_TOWN_RADIUS);
    this.planRoadsToPortals();
    for (const workplace of workplaces) {
      this.placeWorkplace(workplace.kind, workplace.point);
    }
    this.placePlaza();
    this.fillHouses(this.settlement.radius - 4, FILLER_ATTEMPTS);
    this.planWalls(this.settlement.radius + WALL_OFFSET);
    this.placeStreetProps();
  }

  planVillage(): void {
    this.settlement.radius = 48;
    this.planRoadsToPortals();
    this.placePlaza();
    this.fillHouses(this.settlement.radius, 140, ["house", "house", "barn", "house"]);
    this.placeStreetProps();
    for (const building of this.settlement.buildings) {
      this.addProp("garden", building.door.x + (this.random() - 0.5) * 4, building.door.z + (this.random() - 0.5) * 4, building.rotation, false);
    }
  }

  planWilds(): void {
    const zone = this.input.zone;
    this.settlement.radius = 30;
    this.planRoadsToPortals();
    for (const spawn of zone.mobs ?? []) {
      if (HOSTILE_CAMP_PATTERN.test(spawn.mobId)) {
        this.placeCamp(spawn.position);
      } else if (UNDEAD_PATTERN.test(spawn.mobId)) {
        this.placeRuins(spawn.position, 4);
      }
    }
    const outpost = { x: zone.spawn.x + 16, z: zone.spawn.z + 14 };
    this.placeCamp(outpost);
    this.tryPlace("watchtower", { x: zone.spawn.x - 14, z: zone.spawn.z + 10 }, 0);
  }

  planDungeon(): void {
    const zone = this.input.zone;
    this.settlement.radius = 0;
    const targets = (zone.mobs ?? []).map((spawn) => spawn.position);
    let previous = zone.spawn;
    for (const target of targets) {
      const length = distance(previous, target);
      const steps = Math.floor(length / 8);
      const dx = (target.x - previous.x) / length;
      const dz = (target.z - previous.z) / length;
      for (let step = 1; step < steps; step++) {
        for (const side of [1, -1]) {
          const x = previous.x + dx * step * 8 - dz * side * 7;
          const z = previous.z + dz * step * 8 + dx * side * 7;
          this.addProp(step % 3 === 0 ? "brazier" : "pillar", x, z, 0, true);
        }
      }
      this.placeRuins(target, 3);
      previous = target;
    }
  }

  planBattlefield(): void {
    const zone = this.input.zone;
    this.settlement.radius = 60;
    const center = { x: zone.sizeMeters / 2, z: zone.sizeMeters / 2 };
    for (let index = 0; index < 10; index++) {
      const angle = (index / 10) * Math.PI * 2;
      const point = { x: center.x + Math.cos(angle) * 45, z: center.z + Math.sin(angle) * 45 };
      if (index % 2 === 0) {
        this.placeRuins(point, 2);
      } else {
        this.addProp("banner", point.x, point.z, angle, false);
      }
    }
    this.tryPlace("watchtower", { x: zone.spawn.x + 12, z: zone.spawn.z + 12 }, Math.PI);
    this.tryPlace("watchtower", { x: zone.spawn.x - 12, z: zone.spawn.z + 12 }, Math.PI);
  }

  private workplaces(): Array<{ kind: BuildingKind; point: Point }> {
    const center = this.settlement.center;
    const seen = new Set<string>();
    const result: Array<{ kind: BuildingKind; point: Point }> = [];
    for (const npc of this.input.npcs) {
      for (const entry of npc.schedule) {
        const kind = ACTIVITY_BUILDINGS[entry.activity];
        const key = `${Math.round(entry.position.x)}:${Math.round(entry.position.z)}`;
        if (!kind || seen.has(key) || distance(entry.position, center) > TOWN_SEARCH_RADIUS) {
          continue;
        }
        seen.add(key);
        result.push({ kind, point: entry.position });
      }
    }
    const priority: BuildingKind[] = ["townhall", "guildhall", "tavern", "auction_house", "workshop", "market_stall", "house"];
    return result.sort((a, b) => priority.indexOf(a.kind) - priority.indexOf(b.kind));
  }

  /** The building sits behind the work spot, door facing the spot and therefore the town centre. */
  private placeWorkplace(kind: BuildingKind, point: Point): void {
    const center = this.settlement.center;
    const outward = Math.atan2(point.x - center.x, point.z - center.z);
    const size = BUILDING_SIZES[kind];
    const behind = kind === "market_stall" ? -(size.depth / 2 + 0.9) : size.depth / 2 + DOOR_CLEARANCE;
    for (const turn of [0, 0.5, -0.5, 1, -1, 1.6, -1.6, Math.PI]) {
      const angle = outward + turn;
      const x = point.x + Math.sin(angle) * behind;
      const z = point.z + Math.cos(angle) * behind;
      const rotation = angle + Math.PI;
      if (this.tryPlace(kind, { x, z }, rotation, point)) {
        if (kind === "workshop") {
          this.addProp("anvil", point.x + Math.sin(angle) * 1.6, point.z + Math.cos(angle) * 1.6, rotation, false);
        }
        if (kind === "market_stall") {
          this.addProp("crate", x + Math.cos(rotation) * 2.6, z - Math.sin(rotation) * 2.6, rotation, false);
        }
        if (kind === "tavern") {
          this.addProp("barrel", x + Math.cos(rotation) * (size.width / 2 + 0.8), z - Math.sin(rotation) * (size.width / 2 + 0.8), 0, true);
        }
        return;
      }
    }
  }

  /** Front of the building faces `rotation`; its door spot is in front of the facade. */
  private tryPlace(kind: BuildingKind, at: Point, rotation: number, door?: Point): boolean {
    const size = BUILDING_SIZES[kind];
    const footprint: Footprint = { x: at.x, z: at.z, halfWidth: size.width / 2, halfDepth: size.depth / 2, rotation };
    const doorPoint = door ?? {
      x: at.x + Math.sin(rotation) * (size.depth / 2 + DOOR_CLEARANCE),
      z: at.z + Math.cos(rotation) * (size.depth / 2 + DOOR_CLEARANCE),
    };
    const zone = this.input.zone;
    const inside = [at.x - size.width, at.z - size.width, zone.sizeMeters - at.x - size.width, zone.sizeMeters - at.z - size.width].every((gap) => gap > 0);
    if (!inside || this.footprints.some((other) => footprintsOverlap(footprint, other, BUILDING_GAP))) {
      return false;
    }
    if (this.keepClear.some((zoneSpot) => this.overlapsCircle(footprint, zoneSpot.point, zoneSpot.radius))) {
      return false;
    }
    if (this.walkways.some(([from, to]) => segmentHitsFootprint(from, to, footprint, PATH_CLEARANCE, 2.5))) {
      return false;
    }
    if (this.settlement.roads.some((road) => segmentHitsFootprint(road.from, road.to, footprint, road.width / 2 + 0.5))) {
      return false;
    }
    this.footprints.push(footprint);
    this.settlement.buildings.push({ ...footprint, kind, seed: Math.floor(this.random() * 1e9), door: doorPoint });
    if (kind !== "watchtower" && kind !== "ruin") {
      this.settlement.pointsOfInterest.push(doorPoint);
    }
    return true;
  }

  private overlapsCircle(footprint: Footprint, point: Point, radius: number): boolean {
    return distance(footprint, point) < radius + Math.max(footprint.halfWidth, footprint.halfDepth);
  }

  private planRoadsToPortals(): void {
    const center = this.settlement.center;
    const targets = [...this.input.zone.portals.map((portal) => portal.position), ...(this.input.roadTargets ?? [])];
    for (const target of targets) {
      this.settlement.roads.push({ from: center, to: target, width: ROAD_WIDTH });
    }
  }

  private placePlaza(): void {
    const center = this.settlement.center;
    this.settlement.pointsOfInterest.push(center);
    const benchRadius = PLAZA_RADIUS - 1.5;
    for (let index = 0; index < 6; index++) {
      const angle = (index / 6) * Math.PI * 2 + 0.3;
      const point = { x: center.x + Math.sin(angle) * benchRadius, z: center.z + Math.cos(angle) * benchRadius };
      if (!this.nearRoad(point, 2.5) && !this.nearWalkway(point, 1.2)) {
        this.addProp(index % 2 === 0 ? "bench" : "lamp", point.x, point.z, angle + Math.PI, false);
        this.settlement.pointsOfInterest.push({ x: center.x + Math.sin(angle) * (benchRadius - 1.5), z: center.z + Math.cos(angle) * (benchRadius - 1.5) });
      }
    }
    for (let attempt = 0; attempt < 12; attempt++) {
      const angle = this.random() * Math.PI * 2;
      const point = { x: center.x + Math.sin(angle) * (PLAZA_RADIUS + 3), z: center.z + Math.cos(angle) * (PLAZA_RADIUS + 3) };
      if (!this.nearRoad(point, 3) && !this.nearWalkway(point, 3)) {
        this.addProp("fountain", point.x, point.z, 0, true);
        this.footprints.push({ x: point.x, z: point.z, halfWidth: 2, halfDepth: 2, rotation: 0 });
        this.settlement.pointsOfInterest.push({ x: point.x + 2.8, z: point.z });
        break;
      }
    }
    this.addProp("notice_board", center.x + 4, center.z - PLAZA_RADIUS + 1, 0, false);
  }

  private fillHouses(radius: number, attempts: number, kinds: BuildingKind[] = ["house"]): void {
    const center = this.settlement.center;
    for (const road of this.settlement.roads) {
      const length = Math.min(distance(road.from, road.to), radius);
      const dx = (road.to.x - road.from.x) / distance(road.from, road.to);
      const dz = (road.to.z - road.from.z) / distance(road.from, road.to);
      for (let along = PLAZA_RADIUS + 7; along < length - 4; along += 9) {
        for (const side of [1, -1]) {
          const offset = road.width / 2 + 4.5;
          const at = { x: road.from.x + dx * along - dz * side * offset, z: road.from.z + dz * along + dx * side * offset };
          const facing = Math.atan2(dz * side, -dx * side);
          this.tryPlace(kinds[Math.floor(this.random() * kinds.length)], at, facing);
        }
      }
    }
    for (let attempt = 0; attempt < attempts; attempt++) {
      const angle = this.random() * Math.PI * 2;
      const reach = PLAZA_RADIUS + 8 + Math.sqrt(this.random()) * (radius - PLAZA_RADIUS - 8);
      const at = { x: center.x + Math.sin(angle) * reach, z: center.z + Math.cos(angle) * reach };
      this.tryPlace(kinds[Math.floor(this.random() * kinds.length)], at, angle + Math.PI + (this.random() - 0.5) * 0.4);
    }
  }

  /** A polygon wall with gaps wherever a road or an NPC route has to pass. */
  private planWalls(radius: number): void {
    const center = this.settlement.center;
    const vertices: Point[] = [];
    for (let index = 0; index < WALL_SIDES; index++) {
      const angle = (index / WALL_SIDES) * Math.PI * 2;
      vertices.push({ x: center.x + Math.sin(angle) * radius, z: center.z + Math.cos(angle) * radius });
    }
    const crossings: Array<[Point, Point]> = [
      ...this.settlement.roads.map((road): [Point, Point] => [road.from, road.to]),
      ...this.walkways,
    ];
    for (let index = 0; index < WALL_SIDES; index++) {
      const from = vertices[index];
      const to = vertices[(index + 1) % WALL_SIDES];
      const blocked = crossings.some(([a, b]) => crossesWallSide(from, to, a, b))
        || this.keepClear.some((spot) => segmentDistanceToPoint(from, to, spot.point) < spot.radius * 0.6);
      if (blocked) {
        this.settlement.gateTowers.push(from, to);
      } else {
        this.settlement.walls.push({ from, to });
      }
    }
    this.settlement.gateTowers = dedupe(this.settlement.gateTowers)
      .filter((tower) => !crossings.some(([a, b]) => segmentDistanceToPoint(a, b, tower) < ROAD_WIDTH));
  }

  private placeStreetProps(): void {
    for (const road of this.settlement.roads) {
      const length = Math.min(distance(road.from, road.to), this.settlement.radius + WALL_OFFSET);
      const dx = (road.to.x - road.from.x) / distance(road.from, road.to);
      const dz = (road.to.z - road.from.z) / distance(road.from, road.to);
      for (let along = PLAZA_RADIUS + 2; along < length; along += LAMP_SPACING) {
        const side = Math.round(along / LAMP_SPACING) % 2 === 0 ? 1 : -1;
        const offset = road.width / 2 + 0.8;
        this.addProp("lamp", road.from.x + dx * along - dz * side * offset, road.from.z + dz * along + dx * side * offset, 0, false);
      }
    }
    for (const building of this.settlement.buildings.filter((candidate) => candidate.kind === "house")) {
      const roll = this.random();
      const sideX = Math.cos(building.rotation) * (building.halfWidth + 0.9);
      const sideZ = -Math.sin(building.rotation) * (building.halfWidth + 0.9);
      if (roll < 0.3) {
        this.addProp("barrel", building.x + sideX, building.z + sideZ, 0, true);
      } else if (roll < 0.5) {
        this.addProp("haystack", building.x - sideX, building.z - sideZ, 0, true);
      } else if (roll < 0.62) {
        this.addProp("cart", building.x + sideX * 1.4, building.z + sideZ * 1.4, building.rotation, true);
      } else if (roll < 0.85) {
        this.addProp("tree", building.x - sideX * 1.5, building.z - sideZ * 1.5, 0, true);
      }
    }
  }

  private placeCamp(at: Point): void {
    this.addProp("campfire", at.x, at.z, 0, false);
    for (let index = 0; index < 3; index++) {
      const angle = (index / 3) * Math.PI * 2 + this.random();
      const tent = { x: at.x + Math.sin(angle) * 6.5, z: at.z + Math.cos(angle) * 6.5 };
      this.placeLoose("tent", tent, angle + Math.PI);
    }
    this.addProp("crate", at.x + 3, at.z - 2, 0.4, false);
    this.addProp("barrel", at.x - 3, at.z + 2.5, 0, false);
    this.settlement.pointsOfInterest.push(at);
  }

  private placeRuins(at: Point, pieces: number): void {
    for (let index = 0; index < pieces; index++) {
      const angle = (index / pieces) * Math.PI * 2 + this.random() * 0.5;
      const point = { x: at.x + Math.sin(angle) * 9, z: at.z + Math.cos(angle) * 9 };
      this.placeLoose("ruin", point, angle + Math.PI);
    }
  }

  /** Camps and ruins sit where their creatures live, so they ignore the keep-clear rings. */
  private placeLoose(kind: BuildingKind, at: Point, rotation: number): void {
    const size = BUILDING_SIZES[kind];
    const footprint: Footprint = { x: at.x, z: at.z, halfWidth: size.width / 2, halfDepth: size.depth / 2, rotation };
    if (this.footprints.some((other) => footprintsOverlap(footprint, other, 1))) {
      return;
    }
    this.footprints.push(footprint);
    this.settlement.buildings.push({ ...footprint, kind, seed: Math.floor(this.random() * 1e9), door: at });
  }

  private addProp(kind: PropKind, x: number, z: number, rotation: number, blocking: boolean): void {
    this.settlement.props.push({ kind, x, z, rotation, blocking });
  }

  private nearRoad(point: Point, margin: number): boolean {
    return this.settlement.roads.some((road) => segmentDistanceToPoint(road.from, road.to, point) < road.width / 2 + margin);
  }

  private nearWalkway(point: Point, margin: number): boolean {
    return this.walkways.some(([from, to]) => segmentDistanceToPoint(from, to, point) < margin);
  }
}

/** Yaw that lays a footprint's local X axis along the wall. */
/** A route that passes through a corner of the wall touches both sides without strictly crossing either, so corners count too. */
function crossesWallSide(sideFrom: Point, sideTo: Point, routeFrom: Point, routeTo: Point): boolean {
  return segmentsIntersect(sideFrom, sideTo, routeFrom, routeTo)
    || segmentDistanceToPoint(routeFrom, routeTo, sideFrom) < ROAD_WIDTH
    || segmentDistanceToPoint(routeFrom, routeTo, sideTo) < ROAD_WIDTH;
}

export function wallRotation(wall: WallSegment): number {
  return Math.atan2(-(wall.to.z - wall.from.z), wall.to.x - wall.from.x);
}

function dedupe(points: Point[]): Point[] {
  return points.filter((point, index) => points.findIndex((other) => distance(other, point) < 0.5) === index);
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

export function hashText(text: string): number {
  let hash = 2166136261;
  for (let index = 0; index < text.length; index++) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return hash >>> 0;
}
