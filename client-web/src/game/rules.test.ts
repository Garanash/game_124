import { describe, expect, it } from "vitest";
import { ContentCatalog } from "../api/content";
import { CharacterSheet } from "../api/gateway";
import { canChangeScore, knownAbilities, levelForXp, pointsSpent, validateName, withRaceBonuses } from "./rules";

const STANDARD_ARRAY_COST = 27;

describe("point buy", () => {
  it("prices the standard 15/14/13/12/10/8 spread at the full budget", () => {
    expect(pointsSpent([15, 14, 13, 12, 10, 8])).toBe(STANDARD_ARRAY_COST);
  });

  it("refuses raising a score past the budget or the cap", () => {
    const full = [15, 14, 13, 12, 10, 8];
    expect(canChangeScore(full, 5, 1)).toBe(false);
    expect(canChangeScore([15, 8, 8, 8, 8, 8], 0, 1)).toBe(false);
    expect(canChangeScore([8, 8, 8, 8, 8, 8], 0, -1)).toBe(false);
    expect(canChangeScore(full, 0, -1)).toBe(true);
  });

  it("adds race bonuses on top of bought scores", () => {
    const race = { abilityBonuses: { Constitution: 2, Strength: 1 } } as never;
    expect(withRaceBonuses([10, 10, 10, 10, 10, 10], race)).toEqual([11, 10, 12, 10, 10, 10]);
  });
});

describe("names", () => {
  it("accepts letters with hyphen and apostrophe in any script", () => {
    expect(validateName("Тор'ин-Камень")).toBeNull();
    expect(validateName("Ab")).not.toBeNull();
    expect(validateName("R2D2")).not.toBeNull();
  });
});

describe("progression", () => {
  it("matches the SRD XP thresholds", () => {
    expect(levelForXp(0)).toBe(1);
    expect(levelForXp(299)).toBe(1);
    expect(levelForXp(300)).toBe(2);
    expect(levelForXp(355000)).toBe(20);
  });

  it("knows class unlocks up to the class level and perk grants", () => {
    const catalog = {
      classes: { fighter: { abilities: [{ level: 1, abilityId: "second_wind" }, { level: 5, abilityId: "extra_attack" }] } },
      perks: { tough: { grantsAbilities: ["rally"] } },
      abilities: { second_wind: {}, extra_attack: {}, rally: {} },
    } as unknown as ContentCatalog;
    const sheet = { classLevels: { fighter: 3 }, perks: ["tough"] } as unknown as CharacterSheet;

    expect(knownAbilities(sheet, catalog).sort()).toEqual(["rally", "second_wind"]);
  });
});
