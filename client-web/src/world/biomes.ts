import { distance, Point } from "./geometry";

/** What the ground is made of; each kind has its own painted texture and decor mix. */
export type GroundKind = "meadow" | "forest" | "rocky" | "sand" | "ash";

/** At most four kinds per zone, because the ground shader blends them through one RGBA splat map. */
export const MAX_GROUND_LAYERS = 4;

const CONTINENT_GROUND: Record<string, GroundKind> = {
  aldmark: "meadow",
  eldarwen: "forest",
  kargath: "rocky",
  sarrat: "sand",
  talmarin: "meadow",
  veylmark: "ash",
};

/** Land within this distance of a town is fully its biome; it fades into the zone's own land by the outer radius. */
const REGION_INNER_METERS = 230;
const REGION_OUTER_METERS = 340;
const BORDER_WOBBLE_METERS = 55;

export interface BiomeRegion { center: Point; ground: GroundKind }

export function groundForContinent(continentId: string): GroundKind {
  return CONTINENT_GROUND[continentId] ?? "meadow";
}

/**
 * Splits a zone into land types: every town colours the land around it, the rest is the zone's own continent.
 * Weights are a pure function of position, so decor and the ground texture always agree.
 */
export class BiomeMap {
  readonly layers: GroundKind[];

  constructor(private readonly base: GroundKind, private readonly regions: BiomeRegion[]) {
    this.layers = [...new Set([base, ...regions.map((region) => region.ground)])].slice(0, MAX_GROUND_LAYERS);
  }

  /** One weight per entry of `layers`, summing to 1. */
  weightsAt(point: Point): number[] {
    const weights = this.layers.map((layer): number => (layer === this.base ? 1 : 0));
    const wobble = (borderNoise(point) - 0.5) * 2 * BORDER_WOBBLE_METERS;
    for (const region of this.regions) {
      const reach = distance(point, region.center) + wobble;
      const influence = 1 - smoothstep(REGION_INNER_METERS, REGION_OUTER_METERS, reach);
      const layer = this.layers.indexOf(region.ground);
      if (influence <= 0 || layer < 0) {
        continue;
      }
      for (let index = 0; index < weights.length; index++) {
        weights[index] *= 1 - influence;
      }
      weights[layer] += influence;
    }
    const total = weights.reduce((sum, weight) => sum + weight, 0) || 1;
    return weights.map((weight) => weight / total);
  }

  dominantAt(point: Point): GroundKind {
    const weights = this.weightsAt(point);
    return this.layers[weights.indexOf(Math.max(...weights))];
  }
}

export function smoothstep(edge0: number, edge1: number, value: number): number {
  const t = Math.max(0, Math.min(1, (value - edge0) / (edge1 - edge0)));
  return t * t * (3 - 2 * t);
}

/** Cheap smooth value noise in 0..1 with features about 90 m wide, enough to make region borders organic. */
function borderNoise(point: Point): number {
  return valueNoise(point.x / 90, point.z / 90) * 0.7 + valueNoise(point.x / 31 + 17, point.z / 31 + 5) * 0.3;
}

export function valueNoise(x: number, z: number): number {
  const cellX = Math.floor(x);
  const cellZ = Math.floor(z);
  const fx = x - cellX;
  const fz = z - cellZ;
  const u = fx * fx * (3 - 2 * fx);
  const v = fz * fz * (3 - 2 * fz);
  const a = latticeValue(cellX, cellZ);
  const b = latticeValue(cellX + 1, cellZ);
  const c = latticeValue(cellX, cellZ + 1);
  const d = latticeValue(cellX + 1, cellZ + 1);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}

function latticeValue(x: number, z: number): number {
  let hash = Math.imul(x, 374761393) + Math.imul(z, 668265263);
  hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
  return ((hash ^ (hash >>> 16)) >>> 0) / 0x100000000;
}
