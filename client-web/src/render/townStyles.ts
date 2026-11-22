export type RoofShape = "gable" | "steep" | "flat";
export type TreeShape = "round" | "pine" | "palm" | "dead";

export interface TownStyle {
  wall: number;
  trim: number;
  roof: number;
  roofShape: RoofShape;
  timber: number;
  stone: number;
  cloth: number[];
  ground: number;
  sky: number;
  foliage: number;
  road: number;
  plaza: number;
  tree: TreeShape;
}

export const CONTINENT_STYLES: Record<string, TownStyle> = {
  aldmark: {
    wall: 0xeadfc4, trim: 0x6a4a2e, roof: 0x5a6070, roofShape: "gable", timber: 0x4e3320, stone: 0x9c958a,
    cloth: [0x2a4a9a, 0xe8c040, 0xb02a2a], ground: 0x78a04a, sky: 0xb0d4f0, foliage: 0x4a8a3a, road: 0x9a8460, plaza: 0xaea690, tree: "round",
  },
  eldarwen: {
    wall: 0xece6d6, trim: 0xb8a07a, roof: 0x3a64b0, roofShape: "steep", timber: 0x8a6a48, stone: 0xc8c4ba,
    cloth: [0x3a7ad8, 0xe8e0c8, 0x6ab06a], ground: 0x5f9a4a, sky: 0xa8d0ec, foliage: 0x3a8a40, road: 0xb8aa88, plaza: 0xd8d2c4, tree: "round",
  },
  kargath: {
    wall: 0x9a948c, trim: 0x5a4030, roof: 0x8a3a2a, roofShape: "gable", timber: 0x4a3020, stone: 0x7a7670,
    cloth: [0xc84a2a, 0xe8c050, 0x5a5a8a], ground: 0x6a8a4a, sky: 0x9ab4cc, foliage: 0x2f6a34, road: 0x8a7a62, plaza: 0x8a8680, tree: "pine",
  },
  sarrat: {
    wall: 0xe0c494, trim: 0xa8784a, roof: 0xc89a62, roofShape: "flat", timber: 0x7a5a3a, stone: 0xd8b88a,
    cloth: [0xd84a3a, 0x3aa8c8, 0xf0c040], ground: 0xc8b07a, sky: 0xf0d8a8, foliage: 0x6a9a3a, road: 0xd8c098, plaza: 0xe8d4b0, tree: "palm",
  },
  talmarin: {
    wall: 0xf4eee4, trim: 0x5ab0b8, roof: 0x2a9a9a, roofShape: "gable", timber: 0x9a7a5a, stone: 0xe0dcd0,
    cloth: [0x2ab0c8, 0xf4e8d0, 0xf07a5a], ground: 0x8ab86a, sky: 0x9adcf0, foliage: 0x3a9a6a, road: 0xe0d4b8, plaza: 0xf0ece0, tree: "palm",
  },
  veylmark: {
    wall: 0x5a545a, trim: 0x8a2a3a, roof: 0x2a2430, roofShape: "steep", timber: 0x2a2020, stone: 0x4a4448,
    cloth: [0x8a1a2a, 0x3a2a4a, 0xc8a04a], ground: 0x5a5448, sky: 0x8a7a80, foliage: 0x3a4a2a, road: 0x6a6258, plaza: 0x5a5658, tree: "dead",
  },
};

export function styleFor(continentId: string): TownStyle {
  return CONTINENT_STYLES[continentId] ?? CONTINENT_STYLES.kargath;
}
