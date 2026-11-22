import { describe, expect, it } from "vitest";
import { clipNameKey } from "./generatedModels";

describe("clipNameKey", () => {
  it("matches library names to the clip names inside Meshy's merged GLB", () => {
    expect(clipNameKey("Run_02")).toBe(clipNameKey("Run 2"));
    expect(clipNameKey("Walking_Woman")).toBe(clipNameKey("Walking Woman"));
    expect(clipNameKey("Hit_Reaction_with_Bow")).toBe(clipNameKey("Hit Reaction with Bow"));
  });

  it("keeps different numbered clips apart", () => {
    expect(clipNameKey("Run_02")).not.toBe(clipNameKey("Run_12"));
  });
});
