// What to generate with Meshy: one model per playable race (players, NPCs and villagers share it)
// and one per mob. Prompts are English because Meshy understands them best.
// rig: "biped" and "quadruped" get a skeleton and library animations; "none" stays a static mesh
// that the client animates procedurally (bobbing, lunges), for shapes Meshy cannot rig.

const STYLE = "stylized hand-painted fantasy RPG game character, full body, clean readable silhouette, " +
  "vibrant colors, no base, no background, single character";

const MEDIUM = 1.75;
const SMALL = 1.05;

const RACES = {
  elf: [MEDIUM, "graceful wood elf ranger with long pointed ears, green leather armor and a hooded cloak"],
  high_elf: [MEDIUM, "noble high elf mage with pointed ears, silver hair, white and gold robes"],
  half_elf: [MEDIUM, "half-elf adventurer with slightly pointed ears, travel coat, sword at the belt"],
  astral_elf: [MEDIUM, "astral elf with starlit glowing skin, pointed ears, cosmic blue robes with star patterns"],
  fairy: [SMALL, "tiny fairy with translucent butterfly wings, flower petal dress, pointed ears"],
  firbolg: [2.2, "gentle giant firbolg with blue-grey skin, cow-like ears, mossy beard, druid robes"],
  satyr: [MEDIUM, "satyr bard with goat legs and hooves, small curled horns, vest and lute"],
  owlin: [MEDIUM, "owlfolk humanoid with brown feathers, owl head and large eyes, feathered arms, scholar robe"],
  harengon: [SMALL, "rabbitfolk humanoid with long ears and fur, light scout clothes"],
  centaur: [2.1, "centaur warrior, human torso on a chestnut horse body, leather armor, spear"],
  dwarf: [1.35, "stout dwarf warrior with a huge braided red beard, plate armor and a horned helmet"],
  goliath: [2.3, "towering goliath with grey stone-like skin and tribal markings, fur loincloth, muscular"],
  warforged: [1.9, "warforged living construct made of wood and steel plates, glowing eyes, armored body"],
  autognome: [SMALL, "small clockwork gnome automaton, brass gears and pipes, glowing lens eyes"],
  goblin: [SMALL, "goblin rogue with green skin, big pointy ears, ragged leather armor, dagger"],
  hobgoblin: [MEDIUM, "disciplined hobgoblin soldier with orange-red skin, lamellar armor, sword"],
  bugbear: [2.0, "hairy bugbear brute with bear-like face, long arms, crude armor"],
  kobold: [SMALL, "small reptilian kobold with red scales, snout and tail, miner clothes"],
  halfling: [SMALL, "cheerful halfling with curly hair and bare hairy feet, waistcoat and backpack"],
  kender: [SMALL, "kender wanderer with topknot hair, many pouches, hoopak staff"],
  gnome: [SMALL, "gnome tinkerer with big nose, goggles on the forehead, leather apron and tools"],
  leonin: [1.9, "lion-headed leonin warrior with golden fur and mane, bronze armor"],
  tabaxi: [MEDIUM, "cat-folk tabaxi with spotted leopard fur, cat ears and tail, light rogue outfit"],
  loxodon: [2.2, "elephant-headed loxodon monk with trunk and large ears, grey skin, simple robes"],
  minotaur: [2.1, "minotaur gladiator with bull head and big horns, muscular, leather harness"],
  pandaren: [MEDIUM, "panda-folk monk with black and white fur, martial arts robe, round belly"],
  yuan_ti: [MEDIUM, "snake-like humanoid with green scales, serpent eyes, elegant dark robes"],
  lizardfolk: [MEDIUM, "lizardfolk hunter with green scales, crocodile snout and tail, bone spear"],
  grung: [SMALL, "small frog-folk grung with bright blue poisonous skin, tribal loincloth"],
  thri_kreen: [MEDIUM, "insectoid thri-kreen mantis warrior with four arms and green chitin"],
  dragonborn: [1.95, "dragonborn knight with bronze dragon scales and head, plate armor"],
  triton: [MEDIUM, "aquatic triton with blue-green skin, fin ears, coral armor and trident"],
  locathah: [MEDIUM, "fish-folk locathah with scales, fish head and fins, simple harness"],
  simic_hybrid: [MEDIUM, "simic hybrid humanoid with grafted crab claw arm and manta glide membranes"],
  vedalken: [1.85, "tall slender vedalken scholar with blue skin, hairless head, sleek robes"],
  giff: [2.0, "hippo-headed giff soldier with grey skin, military uniform and musket"],
  aarakocra: [MEDIUM, "birdfolk aarakocra with eagle head and large feathered wings, light armor"],
  kenku: [MEDIUM, "raven-folk kenku with black feathers and beak, hooded rogue cloak"],
  hadozee: [MEDIUM, "monkey-like hadozee sailor with brown fur and glide membranes, pirate clothes"],
  half_orc: [1.9, "half-orc barbarian with grey-green skin, small tusks, fur armor and axe"],
  troll: [2.4, "lanky green troll with long nose and arms, ragged clothes"],
  shifter: [MEDIUM, "shifter ranger with feral wolf-like features, sideburns, fur trimmed leather"],
  verdan: [SMALL, "green-skinned verdan goblinoid with big ears, colorful patchwork clothes"],
  githyanki: [1.85, "githyanki warrior with yellow skin, sunken nose, topknot, ornate silver sword"],
  kalashtar: [MEDIUM, "serene kalashtar monk with faint glowing psychic aura, flowing robes"],
  aasimar: [MEDIUM, "aasimar paladin with glowing golden eyes, faint halo, white and gold armor"],
  genasi: [MEDIUM, "fire genasi with ember-red skin and flame-like hair, desert clothes"],
};

const MOBS = {
  training_dummy: ["none", 1.8, "wooden training dummy on a pole made of straw sacks and planks"],
  wolf: ["quadruped", 0.9, "grey wolf, realistic proportions, snarling"],
  boar: ["quadruped", 0.9, "wild boar with tusks and bristly brown fur"],
  deer: ["quadruped", 1.4, "red deer with antlers"],
  giant_crab: ["none", 1.0, "giant red crab with big claws"],
  jungle_spider: ["none", 0.9, "giant hairy jungle spider, purple and black"],
  sand_jackal: ["quadruped", 0.7, "desert jackal with sandy fur and big ears"],
  storm_harpy: ["biped", 1.7, "harpy with feathered wings instead of arms, bird legs and talons, storm grey"],
  pixie_trickster: ["biped", 0.8, "mischievous pixie with dragonfly wings and pointy hat"],
  bandit: ["biped", 1.75, "masked bandit with a hood, leather armor and short sword"],
  bandit_captain: ["biped", 1.85, "bandit captain with tricorn hat, long coat and sabre"],
  goblin_scout: ["biped", 1.0, "goblin scout with bow, leaf camouflage cloak"],
  goblin_boss: ["biped", 1.3, "goblin chieftain with a bone crown, spiked armor and big cleaver"],
  cultist: ["biped", 1.75, "dark cultist in crimson hooded robes with a ritual dagger"],
  skeleton: ["biped", 1.75, "undead skeleton warrior with rusty sword and broken shield"],
  crypt_lord: ["biped", 2.1, "lich crypt lord with skull face, glowing eyes, dark crown and tattered robes"],
  ash_ghoul: ["biped", 1.7, "hunched ghoul with ash-grey skin and long claws"],
  astral_stalker: ["biped", 1.9, "astral stalker hunter with dark violet skin and glowing star tattoos, twin blades"],
  fire_elemental: ["none", 2.2, "fire elemental made of swirling flames with a humanoid shape"],
  sahuagin_priestess: ["biped", 1.8, "sahuagin shark-folk priestess with fins, coral staff"],
  ashen_dragon: ["quadruped", 4.0, "ash-grey dragon with large wings, glowing ember cracks"],
};

/** Races whose body Meshy's biped skeleton cannot fit. */
const UNRIGGABLE_RACES = new Set(["centaur", "thri_kreen", "fairy"]);

export function assetList() {
  const races = Object.entries(RACES).map(([id, [height, description]]) => ({
    key: `race:${id}`,
    rig: UNRIGGABLE_RACES.has(id) ? "none" : "biped",
    height,
    prompt: `${description}, ${STYLE}`,
  }));
  const mobs = Object.entries(MOBS).map(([id, [rig, height, description]]) => ({
    key: `mob:${id}`,
    rig,
    height,
    prompt: `${description}, ${rig === "biped" ? STYLE : STYLE.replace("character", "creature")}`,
  }));
  return [...races, ...mobs];
}
