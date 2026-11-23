// Static game assets for Meshy: weapons held in hand, town buildings per culture, street props and nature.
// Every entry is a smart-topology mesh with a texture (15 credits on meshy-t2.5); the client scales it to
// `height` metres and, for buildings, stretches it onto the generated footprint.

export const PROP_CREDITS = 15;
export const UI_IMAGE_CREDITS = 9;

const STYLE = "stylized hand-painted fantasy RPG game asset, Warcraft-like proportions, rich colors, " +
  "single object, no ground plane, no base, no background";
const WEAPON_STYLE = `${STYLE}, standing upright vertically, handle at the bottom, centered`;

const CULTURES = {
  aldmark: "human medieval village style: whitewashed plaster walls with dark oak timber framing, grey slate gable roof, stone chimney",
  eldarwen: "elven style: pale carved stone and silver birch wood, steep blue tiled roof with curved upswept eaves, leaf ornaments, arched windows",
  kargath: "dwarven style: heavy granite blocks, red clay tile roof, iron-banded oak door, carved runes, low and sturdy",
};

const CULTURE_BUILDINGS = {
  house_a: [7, 6000, "small two-storey family house"],
  house_b: [6, 6000, "cosy one-storey cottage with a porch"],
  tavern: [9, 8000, "large tavern with a hanging sign and a balcony"],
};

const SHARED = {
  "building:market_stall": [3.2, 2500, "medieval market stall with a striped cloth awning, wooden counter with fruit baskets"],
  "building:workshop": [6, 5000, "blacksmith workshop with an open front, forge chimney and anvil"],
  "building:watchtower": [12, 5000, "tall wooden watchtower with a roofed lookout platform and ladder"],
  "building:barn": [7, 5000, "red wooden farm barn with large doors and hay loft"],
  "building:shrine": [4, 3000, "small stone shrine with a statue and candles under a little roof"],
  "prop:fountain": [2.4, 3000, "round stone town fountain with a carved central column, water basin"],
  "prop:well": [2.6, 2000, "stone water well with a small wooden roof, rope and bucket"],
  "prop:lamp": [3.2, 1200, "wrought iron street lantern on a tall post"],
  "prop:bench": [0.9, 1000, "wooden park bench with iron legs"],
  "prop:barrel": [1.1, 800, "wooden barrel with iron hoops"],
  "prop:crate": [0.9, 600, "wooden supply crate with planks"],
  "prop:notice_board": [2.2, 1200, "wooden notice board with pinned parchment notes and a small roof"],
  "prop:wall_section": [3.4, 1500, "straight section of a medieval stone town wall with crenellations, 4 meters long"],
  "prop:wall_tower": [6.5, 2500, "round stone wall tower with crenellations and a conical roof"],
  "nature:oak": [7, 3000, "big leafy oak tree with a thick trunk and lush round green crown"],
  "nature:elder_tree": [10, 3000, "tall ancient elven tree with silver bark and a lush green crown with golden leaves"],
  "nature:pine": [9, 2500, "tall mountain pine tree with layered dark green branches"],
  "nature:bush": [1.3, 800, "round leafy green shrub"],
  "nature:rock": [1, 600, "mossy grey rock"],
  "nature:boulder": [2.2, 1000, "large weathered granite boulder with moss"],
  "nature:cliff": [6, 1500, "tall jagged rocky cliff outcrop"],
};

const WEAPONS = {
  dagger: [0.4, "steel dagger with a leather wrapped grip"],
  short_sword: [0.8, "short steel sword with a simple crossguard"],
  long_sword: [1.05, "knight's longsword with a steel blade and a gilded crossguard"],
  great_sword: [1.5, "huge two-handed greatsword with a wide blade"],
  flame_blade: [1.0, "magic sword with a glowing red-orange fiery blade"],
  mace: [0.75, "iron flanged mace with a wooden handle"],
  holy_mace: [0.8, "golden holy mace with a radiant sun-shaped head"],
  short_bow: [1.1, "wooden short bow with a string"],
  long_bow: [1.6, "tall yew longbow with a string"],
  oak_staff: [1.7, "gnarled oak wizard staff"],
  arcane_staff: [1.8, "arcane mage staff topped with a glowing blue crystal"],
  spellbook: [0.3, "thick closed leather spellbook with brass corners and a rune on the cover"],
  wooden_shield: [0.7, "round wooden shield with an iron rim and boss, front view"],
  iron_shield: [0.75, "steel heater shield with a painted heraldic crest, front view"],
  tower_shield: [1.2, "tall rectangular steel tower shield with rivets, front view"],
  quiver: [0.7, "leather quiver full of arrows"],
};

const WEAPON_POLYCOUNT = 2500;

/** Painted textures for the stone and iron interface windows. */
export const UI_IMAGES = {
  "ui:stone_panel": "seamless tileable texture of dark grey carved stone wall, subtle cracks, game UI background, flat, top-down, no objects",
  "ui:slot": "square inventory slot of dark recessed stone with a thin worn iron bevelled frame, game UI, flat front view, empty",
  "ui:frame": "ornate dark iron fantasy window border with rivets and celtic knot corners, game UI frame, flat front view, empty dark center",
};

export function propList() {
  const culture = Object.entries(CULTURES).flatMap(([continentId, look]) =>
    Object.entries(CULTURE_BUILDINGS).map(([kind, [height, polycount, description]]) => ({
      key: `building:${kind}_${continentId}`, height, polycount, prompt: `${description}, ${look}, ${STYLE}`,
    })));
  const shared = Object.entries(SHARED).map(([key, [height, polycount, description]]) => ({
    key, height, polycount, prompt: `${description}, ${STYLE}`,
  }));
  const weapons = Object.entries(WEAPONS).map(([itemId, [height, description]]) => ({
    key: `weapon:${itemId}`, height, polycount: WEAPON_POLYCOUNT, prompt: `${description}, ${WEAPON_STYLE}`,
  }));
  return [...culture, ...shared, ...weapons];
}
