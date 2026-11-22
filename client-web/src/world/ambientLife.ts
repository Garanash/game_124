import { Activity } from "../render/characters/rig";
import { distance, Point, seededRandom } from "./geometry";
import { NavigationGrid } from "./navigation";
import { hashText, Settlement } from "./settlement";

const VILLAGER_SPEED_METERS_PER_SECOND = 1.25;
const STOPS_PER_ROUTE = 5;
const MIN_PAUSE_SECONDS = 6;
const MAX_PAUSE_SECONDS = 16;
const CRITTER_HOME_RADIUS = 5;
const CRITTER_HOP_SECONDS = 4;
const VILLAGERS_BY_KIND: Record<string, number> = { town: 12, village: 7, wilds: 2, dungeon: 0, battlefield: 0 };
const CRITTERS_BY_KIND: Record<string, number> = { town: 7, village: 9, wilds: 0, dungeon: 0, battlefield: 0 };
const STOP_ACTIVITIES: Activity[] = ["talk", "sweep", "sit", "read", "drink", "idle", "cheer", "talk"];

export type CritterKind = "chicken" | "cat" | "dog";

interface Leg { from: Point; to: Point; start: number; duration: number }
interface Stop { at: Point; start: number; duration: number; activity: Activity }

export interface AmbientPose {
  x: number;
  z: number;
  heading: number;
  speed: number;
  activity: Activity;
}

/**
 * A townsperson with a fixed daily loop. Its pose is a pure function of server time, so every
 * client sees the same villager in the same place without any network traffic.
 */
export class Villager {
  private readonly legs: Leg[] = [];
  private readonly stops: Stop[] = [];
  private period = 0;

  constructor(readonly id: string, readonly seed: number, readonly raceId: string, route: Point[], navigation: NavigationGrid) {
    const random = seededRandom(seed);
    let time = 0;
    for (let index = 0; index < route.length; index++) {
      const from = route[index];
      const to = route[(index + 1) % route.length];
      const pause = MIN_PAUSE_SECONDS + random() * (MAX_PAUSE_SECONDS - MIN_PAUSE_SECONDS);
      this.stops.push({ at: from, start: time, duration: pause, activity: STOP_ACTIVITIES[Math.floor(random() * STOP_ACTIVITIES.length)] });
      time += pause;
      let cursor = from;
      for (const waypoint of navigation.findPath(from, to)) {
        const duration = distance(cursor, waypoint) / VILLAGER_SPEED_METERS_PER_SECOND;
        this.legs.push({ from: cursor, to: waypoint, start: time, duration });
        time += duration;
        cursor = waypoint;
      }
    }
    this.period = Math.max(1, time);
  }

  poseAt(serverTime: number): AmbientPose {
    const t = ((serverTime + this.seed % 1000) % this.period + this.period) % this.period;
    const stop = this.stops.find((candidate) => t >= candidate.start && t < candidate.start + candidate.duration);
    if (stop) {
      return { x: stop.at.x, z: stop.at.z, heading: (this.seed % 628) / 100, speed: 0, activity: stop.activity };
    }
    const leg = this.legs.find((candidate) => t >= candidate.start && t < candidate.start + candidate.duration) ?? this.legs[0];
    if (!leg) {
      return { x: this.stops[0]?.at.x ?? 0, z: this.stops[0]?.at.z ?? 0, heading: 0, speed: 0, activity: "idle" };
    }
    const progress = leg.duration > 0 ? (t - leg.start) / leg.duration : 1;
    return {
      x: leg.from.x + (leg.to.x - leg.from.x) * progress,
      z: leg.from.z + (leg.to.z - leg.from.z) * progress,
      heading: Math.atan2(leg.to.x - leg.from.x, leg.to.z - leg.from.z),
      speed: VILLAGER_SPEED_METERS_PER_SECOND,
      activity: "idle",
    };
  }
}

/** Chickens, cats and dogs hop between random spots around a home point, derived from the time bucket. */
export class Critter {
  constructor(readonly id: string, readonly kind: CritterKind, readonly home: Point, readonly seed: number) {}

  poseAt(serverTime: number): AmbientPose {
    const hop = Math.floor(serverTime / CRITTER_HOP_SECONDS);
    const progress = (serverTime / CRITTER_HOP_SECONDS) - hop;
    const from = this.spot(hop);
    const to = this.spot(hop + 1);
    const moving = progress > 0.6;
    const blend = moving ? (progress - 0.6) / 0.4 : 0;
    return {
      x: from.x + (to.x - from.x) * blend,
      z: from.z + (to.z - from.z) * blend,
      heading: Math.atan2(to.x - from.x, to.z - from.z),
      speed: moving ? distance(from, to) / (CRITTER_HOP_SECONDS * 0.4) : 0,
      activity: this.kind === "chicken" ? "graze" : "idle",
    };
  }

  private spot(hop: number): Point {
    const random = seededRandom(this.seed ^ Math.imul(hop, 2654435761));
    const angle = random() * Math.PI * 2;
    const reach = Math.sqrt(random()) * CRITTER_HOME_RADIUS;
    return { x: this.home.x + Math.cos(angle) * reach, z: this.home.z + Math.sin(angle) * reach };
  }
}

export interface AmbientPopulation {
  villagers: Villager[];
  critters: Critter[];
}

export function populate(settlement: Settlement, zoneId: string, raceIds: string[], navigation: NavigationGrid): AmbientPopulation {
  const random = seededRandom(hashText(`${zoneId}:life`));
  const places = settlement.pointsOfInterest.filter((point) => navigation.isWalkable(point));
  const villagers: Villager[] = [];
  const races = raceIds.length > 0 ? raceIds : ["half_elf"];
  const villagerCount = places.length >= 2 ? VILLAGERS_BY_KIND[settlement.kind] ?? 0 : 0;
  for (let index = 0; index < villagerCount; index++) {
    const route = Array.from({ length: STOPS_PER_ROUTE }, () => places[Math.floor(random() * places.length)]);
    const seed = Math.floor(random() * 1e9);
    villagers.push(new Villager(`villager-${index}`, seed, races[Math.floor(random() * races.length)], route, navigation));
  }
  const homes = settlement.buildings.filter((building) => building.kind === "house" || building.kind === "barn");
  const critters: Critter[] = [];
  const critterCount = homes.length > 0 ? CRITTERS_BY_KIND[settlement.kind] ?? 0 : 0;
  for (let index = 0; index < critterCount; index++) {
    const home = homes[Math.floor(random() * homes.length)].door;
    const kind: CritterKind = index % 4 === 3 ? (index % 8 === 3 ? "dog" : "cat") : "chicken";
    critters.push(new Critter(`critter-${index}`, kind, home, Math.floor(random() * 1e9)));
  }
  return { villagers, critters };
}
