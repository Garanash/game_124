import * as THREE from "three";
import { describe, expect, it } from "vitest";
import { clipNameKey } from "./generatedModels";
import { faceForward } from "./modelAssets";

function hipsClip(yawDegrees: number[]): THREE.AnimationClip {
  const values = yawDegrees.flatMap((yaw) =>
    new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), THREE.MathUtils.degToRad(yaw)).toArray());
  return new THREE.AnimationClip("Idle", -1, [
    new THREE.QuaternionKeyframeTrack("Hips.quaternion", yawDegrees.map((_, frame) => frame), values),
  ]);
}

function yawAt(clip: THREE.AnimationClip, frame: number): number {
  const rotation = new THREE.Quaternion().fromArray(clip.tracks[0].values, frame * 4);
  const forward = new THREE.Vector3(0, 0, 1).applyQuaternion(rotation);
  return Math.round(THREE.MathUtils.radToDeg(Math.atan2(forward.x, forward.z)));
}

describe("faceForward", () => {
  it("turns a stance clip so its average facing is forward, keeping the motion", () => {
    const turned = faceForward(hipsClip([-60, -44, -28]));
    expect([0, 1, 2].map((frame) => yawAt(turned, frame))).toEqual([-16, 0, 16]);
  });

  it("leaves the loaded clip untouched for other models sharing it", () => {
    const original = hipsClip([-44, -44]);
    faceForward(original);
    expect(yawAt(original, 0)).toBe(-44);
  });
});

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
