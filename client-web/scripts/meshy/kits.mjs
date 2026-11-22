// Appearance kits: instead of one baked model per race, a kit has rigged bald base bodies, face/skin textures,
// outfit textures painted on the same UVs, and separate hair meshes. The client mixes them per character.

const STYLE = "stylized hand-painted fantasy RPG game character, detailed face, clean readable silhouette";
/** Skin textures keep the body neutral so only the head and hands are read from them. */
const NEUTRAL_BODY = "bald head with no hair at all, wearing a plain tight light grey bodysuit, barefoot";
/** Outfit textures keep the head neutral; the client reads the head from the skin texture instead. */
const NEUTRAL_HEAD = "bald head with no hair, calm neutral face";
/**
 * Hair is tinted in the client, so it is generated nearly white. Meshy adds a mannequin whenever the prompt mentions
 * body parts or an updo/crown, so styles avoid those words, and the mannequin is ruled out explicitly.
 */
const HAIR_STYLE = "isolated 3D hair asset only, a hollow wig shell floating in empty space, no mannequin, no head, no face, no neck, no shoulders, no bust, platinum white hair, stylized hand-painted fantasy RPG game asset, no base, no background";

/** Outfit ids match the client's role mapping (src/render/generated/kitAppearance.ts). */
const OUTFIT_IDS = ["commoner", "ranger", "mage", "noble", "guard", "artisan"];

const ELF = {
  raceId: "elf",
  heights: { female: 1.7, male: 1.8 },
  sexes: {
    female: "slender graceful elf woman with long pointed ears, delicate face, high cheekbones",
    male: "lean athletic elf man with long pointed ears, sharp elegant face, high cheekbones",
  },
  skins: {
    female: [
      "pale moonlit porcelain skin, silver-blue eyes, thin silver eyebrows, soft pink lips",
      "fair rosy skin with light freckles across the nose and cheeks, bright emerald green eyes, thin light brown eyebrows",
      "warm golden sun-kissed skin, amber eyes, thin golden eyebrows, delicate gold leaf patterns painted on the temples",
      "rich dark bronze skin, hazel green eyes, dark eyebrows, thin green vine tattoos curling on both cheeks and down the neck",
      "ashen grey-violet skin, glowing crimson eyes, white eyebrows, white runic tattoos on the forehead and chin",
      "light olive skin, deep blue eyes, dark eyebrows, a small thin scar across the left eyebrow, beauty mark under the eye",
    ],
    male: [
      "pale porcelain skin, cold grey eyes, thin silver eyebrows, stern expression",
      "tanned skin, bright green eyes, light brown eyebrows, blue woad tribal tattoos across the cheeks and forehead",
      "deep brown skin, amber eyes, dark eyebrows, an old diagonal scar across the right cheek, small silver ear rings",
      "golden skin, glowing golden eyes, blond eyebrows, a thin gold sun tattoo in the middle of the forehead",
      "dusky grey-violet skin, violet eyes, white eyebrows, silver line tattoos along the jaw and under the eyes",
    ],
  },
  outfits: {
    commoner: "simple belted linen tunic in muted green and brown, rolled trousers, soft leather shoes, cloth sleeves to the wrists",
    ranger: "forest ranger outfit: layered green leather armor with leaf-shaped plates, brown long-sleeved shirt, bracers, belt with pouches, tall soft boots",
    mage: "long flowing deep blue mage robe with silver star embroidery, wide sleeves to the wrists, sash belt, slippers",
    noble: "elegant noble attire: white and gold silk doublet with intricate embroidery, high collar, fitted trousers, polished boots",
    guard: "elven guard armor: polished silver plate breastplate with leaf engravings, chainmail sleeves, blue tabard, armored boots",
    artisan: "craftsman outfit: rolled-sleeve shirt with leather apron, sturdy work trousers, tool belt, worn boots",
  },
  hair: [
    "very long straight flowing hair falling down the back to the waist",
    "elegant braided crown updo with one long braid hanging at the back",
    "high ponytail with long strands framing the face",
    "short messy wind-swept hair",
    "long wavy flowing hair",
    "two long twin braids",
    "top knot bun with short undercut hair",
    "shoulder-length hair with a single side braid",
  ],
};

const HUMAN = {
  raceId: "human",
  heights: { female: 1.68, male: 1.8 },
  sexes: {
    female: "athletic human woman with an average build, round ears, expressive realistic face",
    male: "sturdy human man with broad shoulders, round ears, rugged realistic face, clean shaven",
  },
  skins: {
    female: [
      "fair pale skin with a light blush, blue eyes, light brown eyebrows",
      "light skin with dense freckles over the nose and cheeks, green eyes, ginger eyebrows",
      "warm olive Mediterranean skin, dark brown eyes, thick dark eyebrows, red lips",
      "deep dark brown skin, warm brown eyes, black eyebrows, small golden nose ring",
      "tanned East Asian skin, dark eyes, thin black eyebrows, a small blue flower tattoo under the left eye",
      "weathered sunburnt skin, grey eyes, dark eyebrows, a thin scar across the chin",
    ],
    male: [
      "fair skin, blue eyes, blond eyebrows, light stubble on the jaw",
      "tanned skin, brown eyes, dark eyebrows, a short dark beard and moustache",
      "deep dark brown skin, dark eyes, black eyebrows, a thick black full beard",
      "pale skin, green eyes, red eyebrows, a bushy red beard, freckles",
      "olive skin, hazel eyes, grey eyebrows, a grey goatee, deep wrinkles and an old scar over the left eye",
    ],
  },
  outfits: {
    commoner: "peasant clothes: rough brown wool tunic with a rope belt, patched linen trousers, simple leather shoes, sleeves to the wrists",
    ranger: "adventurer outfit: brown leather jerkin over a green long-sleeved shirt, hooded short cape, bracers, belt with pouches, tall boots",
    mage: "scholar wizard robe in deep crimson with golden trim and arcane symbols, wide sleeves to the wrists, leather belt with scroll cases, boots",
    noble: "rich noble attire: blue velvet doublet with gold buttons and embroidery, white ruffled collar, fitted trousers, polished knee boots",
    guard: "town guard armor: steel breastplate over chainmail, red and gold tabard with a lion emblem, padded sleeves, armored boots and gauntlets",
    artisan: "blacksmith outfit: rolled-sleeve linen shirt, heavy leather apron, sturdy work trousers, leather gloves, worn boots",
  },
  hair: [
    "short neat side-parted hair",
    "long straight hair falling to the middle of the back",
    "messy curly hair of medium length",
    "low ponytail tied at the nape",
    "short spiky cropped hair",
    "chin-length straight bob haircut with bangs",
    "long thick wavy hair with a single loose braid",
    "short slicked-back hair",
  ],
};

const DWARF = {
  raceId: "dwarf",
  heights: { female: 1.32, male: 1.36 },
  sexes: {
    female: "short stout dwarf woman, very broad sturdy body, thick strong arms and legs, wide shoulders, round friendly face with a broad nose",
    male: "short stout dwarf man, very broad barrel-chested body, thick muscular arms, short strong legs, big nose, with a very long thick braided beard reaching the belt",
  },
  skins: {
    female: [
      "rosy fair skin with red cheeks, blue eyes, thick ginger eyebrows",
      "ruddy tanned skin, brown eyes, thick dark brown eyebrows, small braided-looking knot tattoos on the cheeks",
      "pale skin with freckles, green eyes, thick blond eyebrows",
      "deep brown skin, amber eyes, thick black eyebrows, golden rune tattoo on the forehead",
      "grey-tinted stone-like skin, pale grey eyes, thick white eyebrows, black soot smudges on the cheeks",
      "olive skin, hazel eyes, thick dark eyebrows, a scar across the nose",
    ],
    male: [
      "ruddy fair skin, blue eyes, bushy fiery red eyebrows, the long braided beard is fiery red with golden beard rings",
      "tanned weathered skin, brown eyes, bushy black eyebrows, the long braided beard is jet black with iron beard rings",
      "pale skin, grey eyes, bushy white eyebrows, the long braided beard is snow white, deep wrinkles of an elder",
      "deep brown skin, amber eyes, bushy dark brown eyebrows, the long braided beard is dark brown, blue rune tattoos on the forehead",
      "soot-stained ruddy skin, green eyes, bushy blond eyebrows, the long braided beard is golden blond, a burn scar on the left cheek",
    ],
  },
  outfits: {
    commoner: "dwarven work clothes: thick brown wool tunic with a wide leather belt and big buckle, sturdy trousers, heavy boots, sleeves to the wrists",
    ranger: "dwarven mountain scout outfit: fur-trimmed leather armor, dark green shirt, thick bracers, belt with pouches, fur boots",
    mage: "dwarven runepriest robe in dark grey and bronze with glowing blue rune embroidery, heavy sleeves to the wrists, chain belt, boots",
    noble: "dwarven noble attire: rich burgundy and gold coat with geometric dwarven patterns, fur collar, golden chain, polished boots",
    guard: "heavy dwarven armor: thick steel plate breastplate with geometric engravings, chainmail sleeves, iron pauldrons, armored boots and gauntlets",
    artisan: "dwarven smith outfit: sleeveless leather vest, thick leather apron with burn marks, leather gloves, sturdy trousers, iron-toed boots",
  },
  hair: [
    "two thick long braids decorated with metal rings",
    "short rough cropped hair",
    "wild bushy shoulder-length mane of hair",
    "long hair tied back with a single thick braid",
    "high mohawk crest of hair",
    "thick wavy hair with many small braids",
    "long straight hair parted in the middle",
    "tight bun on the back of the scalp",
  ],
};

function raceKit(race) {
  const bodyPrompt = (sex) => `${race.sexes[sex]}, ${NEUTRAL_BODY}, arms straight out to the sides in a T-pose, full body, ${STYLE}, no base, no background, single character`;
  return {
    raceId: race.raceId,
    heights: race.heights,
    bases: Object.fromEntries(Object.keys(race.sexes).map((sex) => [sex, bodyPrompt(sex)])),
    skins: Object.fromEntries(Object.entries(race.skins).map(([sex, looks]) =>
      [sex, looks.map((look) => `${race.sexes[sex]}, ${look}, ${NEUTRAL_BODY}, ${STYLE}`)])),
    outfits: Object.fromEntries(OUTFIT_IDS.map((outfit) =>
      [outfit, Object.fromEntries(Object.keys(race.sexes).map((sex) =>
        [sex, `${race.sexes[sex]}, wearing ${race.outfits[outfit]}, ${NEUTRAL_HEAD}, ${STYLE}`]))])),
    hair: race.hair.map((style) => `${style}, ${HAIR_STYLE}`),
  };
}

export const KITS = Object.fromEntries([ELF, HUMAN, DWARF].map((race) => [race.raceId, raceKit(race)]));

/** Every Meshy job of a kit, in the order they depend on each other (bases first). */
export function kitJobs(kit) {
  const prefix = `kit:${kit.raceId}`;
  const jobs = [];
  for (const [sex, prompt] of Object.entries(kit.bases)) {
    jobs.push({ key: `${prefix}:base:${sex}`, kind: "base", sex, prompt });
  }
  for (const [sex, prompts] of Object.entries(kit.skins)) {
    prompts.forEach((prompt, index) => jobs.push({ key: `${prefix}:skin:${sex}:${index}`, kind: "skin", sex, index, prompt }));
  }
  for (const [outfit, bySex] of Object.entries(kit.outfits)) {
    for (const [sex, prompt] of Object.entries(bySex)) {
      jobs.push({ key: `${prefix}:outfit:${outfit}:${sex}`, kind: "outfit", sex, outfit, prompt });
    }
  }
  kit.hair.forEach((prompt, index) => jobs.push({ key: `${prefix}:hair:${index}`, kind: "hair", index, prompt }));
  return jobs;
}

/** Credits per job kind at the current Meshy prices (biped base: mesh 5, texture 10, rig 5, 8 clips × 3). */
export const KIT_JOB_CREDITS = { base: 44, skin: 10, outfit: 10, hair: 15 };
