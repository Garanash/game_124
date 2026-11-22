// Appearance kits: instead of one baked model per race, a kit has rigged bald base bodies, face/skin textures,
// outfit textures painted on the same UVs, and separate hair meshes. The client mixes them per character.

const STYLE = "stylized hand-painted fantasy RPG game character, detailed face, clean readable silhouette";
/** Skin textures keep the body neutral so only the head and hands are read from them. */
const NEUTRAL_BODY = "bald head with no hair at all, wearing a plain tight light grey bodysuit, barefoot";
/** Outfit textures keep the head neutral; the client reads the head from the skin texture instead. */
const NEUTRAL_HEAD = "bald head with no hair, calm neutral face";
/**
 * Hair is tinted in the client, so it is generated nearly white. Meshy adds a mannequin whenever the prompt mentions
 * body parts, so styles avoid words like shoulders or shaved sides, and the mannequin is ruled out explicitly.
 */
const HAIR_STYLE = "isolated 3D hair asset only, a hollow wig shell floating in empty space, no mannequin, no head, no face, no neck, no shoulders, no bust, platinum white hair, stylized hand-painted fantasy RPG game asset, no base, no background";

const ELF_SEX = {
  female: "slender graceful elf woman with long pointed ears, delicate face, high cheekbones",
  male: "lean athletic elf man with long pointed ears, sharp elegant face, high cheekbones",
};

const ELF_SKINS = {
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
};

/** Outfit ids match the client's role mapping (src/render/generated/kitAppearance.ts). */
const ELF_OUTFITS = {
  commoner: "simple belted linen tunic in muted green and brown, rolled trousers, soft leather shoes, cloth sleeves to the wrists",
  ranger: "forest ranger outfit: layered green leather armor with leaf-shaped plates, brown long-sleeved shirt, bracers, belt with pouches, tall soft boots",
  mage: "long flowing deep blue mage robe with silver star embroidery, wide sleeves to the wrists, sash belt, slippers",
  noble: "elegant noble attire: white and gold silk doublet with intricate embroidery, high collar, fitted trousers, polished boots",
  guard: "elven guard armor: polished silver plate breastplate with leaf engravings, chainmail sleeves, blue tabard, armored boots",
  artisan: "craftsman outfit: rolled-sleeve shirt with leather apron, sturdy work trousers, tool belt, worn boots",
};

const ELF_HAIR = [
  "very long straight flowing hair falling down the back to the waist",
  "elegant braided crown updo with one long braid hanging at the back",
  "high ponytail with long strands framing the face",
  "short messy wind-swept hair",
  "long wavy flowing hair",
  "two long twin braids",
  "top knot bun with short undercut hair",
  "shoulder-length hair with a single side braid",
];

function elfKit() {
  return {
    raceId: "elf",
    heights: { female: 1.7, male: 1.8 },
    bases: Object.fromEntries(Object.entries(ELF_SEX).map(([sex, body]) =>
      [sex, `${body}, ${NEUTRAL_BODY}, arms straight out to the sides in a T-pose, full body, ${STYLE}, no base, no background, single character`])),
    skins: Object.fromEntries(Object.entries(ELF_SKINS).map(([sex, looks]) =>
      [sex, looks.map((look) => `${ELF_SEX[sex]}, ${look}, ${NEUTRAL_BODY}, ${STYLE}`)])),
    outfits: Object.fromEntries(Object.entries(ELF_OUTFITS).map(([outfit, clothes]) =>
      [outfit, Object.fromEntries(Object.keys(ELF_SEX).map((sex) => [sex, `${ELF_SEX[sex]}, wearing ${clothes}, ${NEUTRAL_HEAD}, ${STYLE}`]))])),
    hair: ELF_HAIR.map((style) => `${style}, ${HAIR_STYLE}`),
  };
}

export const KITS = { elf: elfKit() };

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
