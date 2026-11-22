import * as THREE from "three";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import { GroundKind } from "../world/biomes";
import { DecorKind, DecorPiece } from "../world/worldLayout";

/** Decor is split into square chunks so the camera and the sun's shadow camera skip what they cannot see. */
const CHUNK_METERS = 192;
const SHADE_SPREAD = 0.22;

type Paint = "trunk" | "foliage" | "stone" | "grass" | "blossom";

interface Part {
  geometry: THREE.BufferGeometry;
  paint: Paint;
  castShadow: boolean;
}

const FOLIAGE: Record<GroundKind, number[]> = {
  forest: [0x3a8a40, 0x2f7a46, 0x4c9a3a, 0x6aa84a, 0xc8a040, 0x8ab8a8],
  meadow: [0x4a8a3a, 0x5a9a3c, 0x3f7a34],
  rocky: [0x2f5a34, 0x335f3a, 0x2a4f30],
  sand: [0x6a9a3a, 0x7aa848],
  ash: [0x4a4a2a, 0x3a3a26],
};
const STONE: Record<GroundKind, number> = { forest: 0x7d8a70, meadow: 0x9a968e, rocky: 0x8a8780, sand: 0xc8aa80, ash: 0x4e4846 };
const GRASS: Record<GroundKind, number> = { forest: 0x3f7a32, meadow: 0x6a9a3a, rocky: 0x6a7a44, sand: 0xa8a060, ash: 0x5a5a3a };
const TRUNK = 0x5a3f28;
const BLOSSOMS = [0xf4f0e0, 0xf0d040, 0xc890e0, 0xe87878, 0x90b8f0];

const PARTS: Record<DecorKind, Part[]> = buildParts();

/** Instanced trees, rocks, cliffs and grass of a zone. */
export class DecorRenderer {
  readonly group = new THREE.Group();
  private readonly materials = new Map<Paint, THREE.MeshStandardMaterial>();

  load(pieces: DecorPiece[]): void {
    this.dispose();
    const chunks = new Map<string, DecorPiece[]>();
    for (const piece of pieces) {
      const key = `${Math.floor(piece.x / CHUNK_METERS)}:${Math.floor(piece.z / CHUNK_METERS)}:${piece.kind}`;
      const chunk = chunks.get(key) ?? [];
      chunk.push(piece);
      chunks.set(key, chunk);
    }
    for (const chunk of chunks.values()) {
      PARTS[chunk[0].kind].forEach((part) => this.group.add(this.instances(part, chunk)));
    }
  }

  dispose(): void {
    this.group.children.forEach((child) => (child as THREE.InstancedMesh).dispose());
    this.group.clear();
  }

  private instances(part: Part, pieces: DecorPiece[]): THREE.InstancedMesh {
    const mesh = new THREE.InstancedMesh(part.geometry, this.material(part.paint), pieces.length);
    const matrix = new THREE.Matrix4();
    const rotation = new THREE.Quaternion();
    const up = new THREE.Vector3(0, 1, 0);
    const color = new THREE.Color();
    pieces.forEach((piece, index) => {
      rotation.setFromAxisAngle(up, piece.rotation);
      matrix.compose(new THREE.Vector3(piece.x, 0, piece.z), rotation, new THREE.Vector3(piece.scale, piece.scale, piece.scale));
      mesh.setMatrixAt(index, matrix);
      mesh.setColorAt(index, paintColor(part.paint, piece, color));
    });
    mesh.castShadow = part.castShadow;
    mesh.receiveShadow = true;
    mesh.computeBoundingSphere();
    return mesh;
  }

  private material(paint: Paint): THREE.MeshStandardMaterial {
    let material = this.materials.get(paint);
    if (!material) {
      material = new THREE.MeshStandardMaterial({ color: 0xffffff, flatShading: paint !== "grass", roughness: paint === "stone" ? 0.95 : 0.85 });
      this.materials.set(paint, material);
    }
    return material;
  }
}

function paintColor(paint: Paint, piece: DecorPiece, target: THREE.Color): THREE.Color {
  const shade = 1 + (piece.shade - 0.5) * 2 * SHADE_SPREAD;
  switch (paint) {
    case "trunk":
      return target.set(TRUNK).multiplyScalar(shade);
    case "stone":
      return target.set(STONE[piece.ground]).multiplyScalar(shade);
    case "grass":
      return target.set(GRASS[piece.ground]).multiplyScalar(shade);
    case "blossom":
      return target.set(BLOSSOMS[Math.floor(piece.shade * BLOSSOMS.length) % BLOSSOMS.length]);
    case "foliage": {
      const palette = FOLIAGE[piece.ground];
      return target.set(palette[Math.floor(piece.shade * palette.length) % palette.length]).multiplyScalar(0.9 + piece.shade * 0.2);
    }
  }
}

function buildParts(): Record<DecorKind, Part[]> {
  const at = (geometry: THREE.BufferGeometry, x: number, y: number, z: number, scale: [number, number, number] = [1, 1, 1]) =>
    geometry.scale(...scale).translate(x, y, z);
  const solid = (geometry: THREE.BufferGeometry, paint: Paint): Part => ({ geometry, paint, castShadow: true });
  const merged = (geometries: THREE.BufferGeometry[]) => mergeGeometries(geometries.map((geometry) => geometry.toNonIndexed()));
  const frond = (angle: number) => new THREE.BoxGeometry(0.42, 0.06, 2.1).translate(0, 0, 1).rotateX(0.45).rotateY(angle).translate(0.25, 4, 0);
  const blades = (spread: number) => merged([0, 1, 2, 3].map((index) => {
    const angle = (index / 4) * Math.PI * 2;
    return new THREE.ConeGeometry(0.05, 0.45 + index * 0.06, 3).rotateZ((index - 1.5) * 0.18).translate(Math.cos(angle) * spread, 0.25, Math.sin(angle) * spread);
  }));
  return {
    tree: [
      solid(at(new THREE.CylinderGeometry(0.2, 0.3, 1.6, 6), 0, 0.8, 0), "trunk"),
      solid(merged([at(new THREE.IcosahedronGeometry(1.3, 1), 0, 2.5, 0), at(new THREE.IcosahedronGeometry(0.85, 1), 0.55, 3.15, 0.2)]), "foliage"),
    ],
    elder_tree: [
      solid(at(new THREE.CylinderGeometry(0.24, 0.44, 3.8, 7), 0, 1.9, 0), "trunk"),
      solid(merged([
        at(new THREE.IcosahedronGeometry(1.75, 1), 0, 4.4, 0, [1, 0.85, 1]),
        at(new THREE.IcosahedronGeometry(1.2, 1), 1, 3.7, -0.4),
        at(new THREE.IcosahedronGeometry(1.05, 1), -0.85, 5, 0.5),
      ]), "foliage"),
    ],
    pine: [
      solid(at(new THREE.CylinderGeometry(0.18, 0.28, 1.4, 6), 0, 0.7, 0), "trunk"),
      solid(merged([
        at(new THREE.ConeGeometry(1.5, 2.6, 7), 0, 2.1, 0),
        at(new THREE.ConeGeometry(1.15, 2.2, 7), 0, 3.3, 0),
        at(new THREE.ConeGeometry(0.75, 1.8, 7), 0, 4.4, 0),
      ]), "foliage"),
    ],
    palm: [
      solid(at(new THREE.CylinderGeometry(0.15, 0.25, 4, 6), 0, 2, 0).rotateZ(0.08), "trunk"),
      solid(merged([0, 1, 2, 3, 4, 5].map((index) => frond((index / 6) * Math.PI * 2))), "foliage"),
    ],
    dead_tree: [
      solid(merged([
        at(new THREE.CylinderGeometry(0.12, 0.26, 3, 5), 0, 1.5, 0),
        new THREE.CylinderGeometry(0.05, 0.1, 1.4, 4).rotateZ(-0.8).translate(0.45, 2.5, 0),
        new THREE.CylinderGeometry(0.04, 0.08, 1, 4).rotateZ(0.9).translate(-0.35, 2.1, 0.1),
      ]), "trunk"),
    ],
    bush: [solid(merged([at(new THREE.IcosahedronGeometry(0.8, 1), 0, 0.45, 0, [1, 0.7, 1]), at(new THREE.IcosahedronGeometry(0.55, 1), 0.55, 0.35, 0.25)]), "foliage")],
    rock: [solid(at(new THREE.DodecahedronGeometry(0.9, 0), 0, 0.3, 0, [1, 0.65, 0.85]), "stone")],
    boulder: [solid(merged([at(new THREE.DodecahedronGeometry(1, 1), 0, 0.45, 0, [1, 0.75, 0.9]), at(new THREE.DodecahedronGeometry(0.55, 0), 0.75, 0.25, 0.5)]), "stone")],
    cliff: [solid(merged([
      at(new THREE.DodecahedronGeometry(1, 0), 0, 0.9, 0, [1, 1.45, 0.9]),
      at(new THREE.DodecahedronGeometry(0.7, 0), 0.8, 0.5, 0.4, [1, 1.2, 1]),
      at(new THREE.DodecahedronGeometry(0.6, 0), -0.7, 0.4, -0.3),
    ]), "stone")],
    grass: [{ geometry: blades(0.14), paint: "grass", castShadow: false }],
    flowers: [
      { geometry: blades(0.1), paint: "grass", castShadow: false },
      { geometry: merged([0, 1, 2].map((index) => new THREE.SphereGeometry(0.07, 5, 4).translate(Math.cos(index * 2.1) * 0.12, 0.55 + index * 0.04, Math.sin(index * 2.1) * 0.12))), paint: "blossom", castShadow: false },
    ],
  };
}
