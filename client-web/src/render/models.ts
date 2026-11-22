import * as THREE from "three";
import { EntityKind } from "../net/protocol";
import { adventurerOutfit, raceLook, ROLE_OUTFITS, stableHash } from "./characters/appearance";
import { buildCreature } from "./characters/creatures";
import { buildHumanoid } from "./characters/humanoid";
import { ball, box, cone, PartKit, rod } from "./characters/parts";
import { Rig } from "./characters/rig";

const LABEL_CLEARANCE = 0.55;
const NODE_LABEL_HEIGHT = 2.6;
const PORTAL_LABEL_HEIGHT = 3.2;
const SELF_RING_COLOR = 0xffe27a;
const MARKER_CLEARANCE = 0.45;

export interface NpcLook {
  raceId: string;
  role: string;
}

export interface ModelRequest {
  kind: EntityKind;
  definitionId: string;
  name: string;
  isSelf: boolean;
  npc?: NpcLook;
}

export interface EntityModel {
  root: THREE.Group;
  /** Animated characters have a rig; props such as nodes and portals do not. */
  rig: Rig | null;
  labelHeight: number;
}

export function hashHue(text: string): number {
  return stableHash(text) / 0xffffffff;
}

export function createModel(request: ModelRequest): EntityModel {
  switch (request.kind) {
    case EntityKind.Player:
      return characterModel(playerRig(request));
    case EntityKind.Npc:
      return characterModel(npcRig(request));
    case EntityKind.Mob:
      return characterModel(buildCreature(request.definitionId));
    case EntityKind.GatheringNode:
      return { root: nodeModel(request.definitionId), rig: null, labelHeight: NODE_LABEL_HEIGHT };
    case EntityKind.Portal:
      return { root: portalModel(), rig: null, labelHeight: PORTAL_LABEL_HEIGHT };
  }
}

function playerRig(request: ModelRequest): Rig {
  return buildHumanoid(raceLook(request.definitionId), adventurerOutfit(stableHash(request.name)));
}

function npcRig(request: ModelRequest): Rig {
  const look = raceLook(request.npc?.raceId ?? "half_elf");
  const outfit = ROLE_OUTFITS[request.npc?.role ?? "Citizen"] ?? ROLE_OUTFITS.Citizen;
  return buildHumanoid(look, outfit);
}

function characterModel(rig: Rig): EntityModel {
  return { root: rig.root, rig, labelHeight: labelHeightFor(rig.height) };
}

export function labelHeightFor(modelHeight: number): number {
  return modelHeight + LABEL_CLEARANCE;
}

/** Floating gem over quest givers and merchants; the renderer bobs anything named "marker". */
export function npcMarker(modelHeight: number): THREE.Mesh {
  const marker = new THREE.Mesh(
    new THREE.OctahedronGeometry(0.16, 0),
    new THREE.MeshStandardMaterial({ color: 0xffd23a, emissive: 0xb08a00, flatShading: true }));
  marker.name = "marker";
  placeMarker(marker, modelHeight);
  return marker;
}

export function placeMarker(marker: THREE.Object3D, modelHeight: number): void {
  marker.position.y = modelHeight + MARKER_CLEARANCE;
  marker.userData.baseHeight = marker.position.y;
}

export function selfRing(): THREE.Mesh {
  const ring = new THREE.Mesh(
    new THREE.RingGeometry(0.55, 0.68, 32),
    new THREE.MeshBasicMaterial({ color: SELF_RING_COLOR, transparent: true, opacity: 0.85, side: THREE.DoubleSide, depthWrite: false }));
  ring.rotation.x = -Math.PI / 2;
  ring.position.y = 0.04;
  ring.name = "self-ring";
  return ring;
}

function nodeModel(nodeId: string): THREE.Group {
  const kit = new PartKit();
  const group = new THREE.Group();
  if (/tree|wood|oak|pine|palm|bamboo/.test(nodeId)) {
    group.add(kit.mesh(rod(0.22, 0.32, 1.6, 7), 0x6b4a2b, 0, 0.8, 0));
    group.add(kit.mesh(ball(1.2, 1), 0x2f7d3a, 0, 2.3, 0));
    group.add(kit.mesh(ball(0.8, 1), 0x3a8d44, 0.5, 2.8, 0.3));
    group.add(kit.mesh(box(0.5, 0.15, 0.15), 0xc8a870, 0.4, 0.1, 0.4));
    return group;
  }
  if (/ore|vein|stone|quarry|rock|crystal|coral/.test(nodeId)) {
    const glowing = /crystal|coral/.test(nodeId);
    const color = glowing ? 0x7ad7ff : 0x8a8f96;
    group.add(kit.mesh(new THREE.DodecahedronGeometry(0.8, 0), color, 0, 0.5, 0));
    group.add(kit.mesh(new THREE.DodecahedronGeometry(0.45, 0), color, 0.7, 0.3, 0.2));
    for (let index = 0; index < 4; index++) {
      const angle = index * 1.6;
      group.add(glowing
        ? kit.glow(cone(0.12, 0.5, 5), 0x9ae8ff, Math.cos(angle) * 0.5, 0.9, Math.sin(angle) * 0.5)
        : kit.mesh(ball(0.1, 0), 0xd8903a, Math.cos(angle) * 0.6, 0.6, Math.sin(angle) * 0.6));
    }
    return group;
  }
  for (let index = 0; index < 7; index++) {
    const angle = (index / 7) * Math.PI * 2;
    const leaf = kit.mesh(cone(0.12, 0.7, 4), 0x5fbf4a, Math.cos(angle) * 0.25, 0.35, Math.sin(angle) * 0.25);
    leaf.rotation.z = Math.cos(angle) * 0.4;
    leaf.rotation.x = Math.sin(angle) * 0.4;
    group.add(leaf);
  }
  group.add(kit.glow(ball(0.14, 0), /moon/.test(nodeId) ? 0xc8d8ff : 0xf2d0ff, 0, 0.75, 0, 1.2));
  return group;
}

function portalModel(): THREE.Group {
  const kit = new PartKit();
  const group = new THREE.Group();
  for (const side of [1, -1]) {
    group.add(kit.mesh(box(0.5, 3.2, 0.5), 0x8a8478, side * 1.5, 1.6, 0));
    group.add(kit.glow(ball(0.12, 0), 0x6ad1ff, side * 1.5, 3.3, 0));
  }
  group.add(kit.mesh(box(3.6, 0.4, 0.6), 0x7a7468, 0, 3.3, 0));
  const ring = kit.glow(new THREE.TorusGeometry(1.15, 0.1, 8, 32), 0x6ad1ff, 0, 1.6, 0);
  ring.name = "spin";
  group.add(ring);
  const veil = new THREE.Mesh(new THREE.CircleGeometry(1.05, 32), new THREE.MeshBasicMaterial({ color: 0x9fe8ff, transparent: true, opacity: 0.4, side: THREE.DoubleSide, depthWrite: false }));
  veil.position.y = 1.6;
  veil.name = "spin";
  group.add(veil);
  return group;
}

export function summonModel(): THREE.Group {
  const kit = new PartKit();
  const group = new THREE.Group();
  group.add(kit.glow(ball(0.45, 0), 0xb38cff, 0, 0.8, 0));
  group.add(kit.glow(cone(0.3, 0.6, 5), 0x8a5aff, 0, 0.25, 0));
  return group;
}
