// Inventory icons: one painted square per item id. The client frames them by rarity, so prompts ask for no border.

export const ICON_MODEL = "nano-banana-pro";
export const ICON_CREDITS = 9;

const ICON_STYLE = "classic World of Warcraft style RPG inventory icon, hand-painted digital painting, the object centered and filling the square, "
  + "dramatic rim lighting, rich saturated colors, crisp details, dark moody painted background with a soft glow behind the object, "
  + "square composition, no text, no letters, no frame, no border, no UI";

/** English subjects for every item id in content/items; ids missing here are skipped with a warning. */
const ITEM_SUBJECTS = {
  dagger: "a simple steel dagger with a leather-wrapped grip",
  twin_daggers: "a pair of crossed curved twin daggers with green-tinted blades",
  short_sword: "a short steel sword with a brass crossguard",
  long_sword: "an elegant steel longsword with a blue gem in the pommel",
  great_sword: "a massive two-handed greatsword with an ornate engraved blade",
  flame_blade: "a magical sword with a blade made of roaring orange fire",
  mace: "an iron flanged mace with a wooden handle",
  holy_mace: "a golden holy mace radiating soft white light, sun emblem on the head",
  short_bow: "a simple wooden short bow with a taut string",
  long_bow: "a tall elegant elven longbow carved with silver leaf patterns",
  quiver: "a leather quiver full of feathered arrows",
  oak_staff: "a gnarled oak wizard staff with green leaves sprouting at the top",
  arcane_staff: "an arcane staff topped with a floating glowing purple crystal",
  spellbook: "a thick leather-bound spellbook with glowing blue runes on the cover",
  holy_symbol: "a golden sun-shaped holy symbol amulet on a chain",
  wooden_shield: "a round wooden shield with iron rim and boss",
  iron_shield: "a heater-shaped iron shield with a red heraldic lion",
  tower_shield: "a huge rectangular steel tower shield with rivets and a crest",
  leather_vest: "a brown leather armor vest with stitching and buckles",
  chain_shirt: "a chainmail shirt of interlocking steel rings",
  whetstone: "a grey sharpening whetstone with water drops",
  ruby: "a faceted glowing red ruby gemstone",
  sapphire: "a faceted glowing blue sapphire gemstone",
  lucky_coin: "a shiny old gold coin with a four-leaf clover stamp",
  four_leaf_clover: "a vivid green four-leaf clover with a sparkle",
  fortune_charm: "a magical golden charm amulet with a green emerald and stars",
  healing_potion: "a round glass potion bottle with glowing red liquid and a cork",
  greater_healing_potion: "a large ornate crystal potion flask with bright glowing crimson liquid and gold filigree",
  dragon_scale: "a large iridescent red dragon scale glowing with inner fire",
  herb: "a bundle of green healing herbs tied with twine",
  moonpetal: "a pale blue glowing moonpetal flower under moonlight",
  copper_ore: "a chunk of rock with veins of shiny copper ore",
  iron_ore: "a chunk of dark rock with grey iron ore veins",
  crystal_shard: "a sharp translucent cyan crystal shard",
  wood_log: "a cut wooden log with bark and visible tree rings",
  hide: "a rough animal hide pelt with fur",
  raw_meat: "a raw red cut of meat on a bone",
  fish: "a fresh silver river fish",
  cotton: "a fluffy white cotton boll on a stem",
  stone: "a rough grey stone chunk",
  iron_ingot: "a stack of polished iron ingots",
  plank: "a few neat sawn wooden planks",
  leather: "a rolled piece of tanned brown leather tied with a cord",
  linen: "a folded bolt of off-white linen cloth",
  stone_block: "a neatly cut square stone building block",
  cooked_meat: "a juicy roasted leg of meat with steam rising",
  grilled_fish: "a grilled fish on a stick with char marks and steam",
  mana_crystal: "a glowing blue mana crystal radiating magical sparkles",
  bone: "an old white bone",
  goblin_ear: "a pointy green goblin ear trophy",
  ash_essence: "a small glass vial filled with swirling glowing grey-orange ash smoke",
};

export function iconPrompt(itemId) {
  const subject = ITEM_SUBJECTS[itemId];
  return subject ? `${ICON_STYLE}. Subject: ${subject}` : null;
}
