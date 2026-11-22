import { describe, expect, it } from "vitest";
import { KitContents, rollAppearance } from "./kitAppearance";

const FULL_KIT: KitContents = {
  sexes: ["female", "male"],
  skins: { female: [0, 1, 2, 3, 4, 5], male: [0, 1, 2, 3, 4] },
  hair: [0, 1, 2, 3, 4, 5, 6, 7],
  outfits: ["commoner", "ranger", "mage", "noble", "guard", "artisan"],
};

describe("rollAppearance", () => {
  it("gives the same character the same look on every client", () => {
    const hints = { seed: 123456, isAdventurer: true };
    expect(rollAppearance(FULL_KIT, hints)).toEqual(rollAppearance(FULL_KIT, hints));
  });

  it("makes a crowd of villagers varied", () => {
    const crowd = Array.from({ length: 40 }, (_, seed) => rollAppearance(FULL_KIT, { seed: seed + 1, isAdventurer: false }));
    const looks = new Set(crowd.map((look) => `${look.sex}:${look.skinIndex}:${look.hairIndex}:${look.hairColor}`));
    expect(looks.size).toBeGreaterThan(35);
    expect(new Set(crowd.map((look) => look.sex)).size).toBe(2);
    expect(new Set(crowd.map((look) => look.build)).size).toBe(3);
    expect(crowd.every((look) => FULL_KIT.skins[look.sex].includes(look.skinIndex!))).toBe(true);
  });

  it("only uses the parts that exist when some were rejected", () => {
    const gaps: KitContents = { ...FULL_KIT, hair: [0, 2, 3, 7] };
    const crowd = Array.from({ length: 60 }, (_, seed) => rollAppearance(gaps, { seed: seed + 1, isAdventurer: false }));
    const usedHair = new Set(crowd.map((look) => look.hairIndex).filter((hair) => hair !== null));
    expect([...usedHair].sort()).toEqual([0, 2, 3, 7]);
  });

  it("dresses NPCs for their role", () => {
    expect(rollAppearance(FULL_KIT, { seed: 7, role: "Guard", isAdventurer: false }).outfit).toBe("guard");
    expect(rollAppearance(FULL_KIT, { seed: 7, role: "Mentor", isAdventurer: false }).outfit).toBe("mage");
  });

  it("falls back to what a partially generated kit has", () => {
    const partial: KitContents = { sexes: ["female"], skins: { female: [], male: [] }, hair: [], outfits: ["commoner"] };
    const look = rollAppearance(partial, { seed: 99, role: "Guard", isAdventurer: false });
    expect(look).toMatchObject({ sex: "female", skinIndex: null, hairIndex: null, outfit: "commoner" });
  });
});
