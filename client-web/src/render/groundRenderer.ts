import * as THREE from "three";
import { BiomeMap, GroundKind, MAX_GROUND_LAYERS } from "../world/biomes";
import { GROUND_TILE_METERS, groundTexture } from "./groundTextures";

/** Splat resolution; land borders are tens of meters wide, so a few meters per pixel is plenty. */
const SPLAT_METERS_PER_PIXEL = 3;
/** A second, rotated and scaled sample per layer hides the tile repeat over open land. */
const ANTI_TILE_SCALE = 0.29;
const ANTI_TILE_MIX = 0.4;

export interface GroundLayer { kind: GroundKind; tint: number }

/** One big plane whose colour is blended from painted tiles through a biome splat map. */
export class GroundRenderer {
  readonly mesh: THREE.Mesh;
  private readonly material: THREE.MeshStandardMaterial;
  private readonly uniforms = {
    splatMap: { value: null as THREE.DataTexture | null },
    groundLayers: { value: [] as THREE.Texture[] },
    zoneSize: { value: 1 },
    tileMeters: { value: GROUND_TILE_METERS },
  };

  constructor() {
    this.material = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.97, metalness: 0 });
    this.material.onBeforeCompile = (shader) => this.extendShader(shader);
    this.material.customProgramCacheKey = () => `ground-${MAX_GROUND_LAYERS}`;
    this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), this.material);
    this.mesh.rotation.x = -Math.PI / 2;
    this.mesh.receiveShadow = true;
  }

  load(sizeMeters: number, biomes: BiomeMap, tintOf: (kind: GroundKind) => number): void {
    this.mesh.geometry.dispose();
    this.mesh.geometry = new THREE.PlaneGeometry(sizeMeters, sizeMeters);
    this.mesh.position.set(sizeMeters / 2, 0, sizeMeters / 2);
    this.uniforms.splatMap.value?.dispose();
    this.uniforms.splatMap.value = splatTexture(sizeMeters, biomes);
    const layers = biomes.layers.map((kind) => groundTexture(kind, tintOf(kind)));
    while (layers.length < MAX_GROUND_LAYERS) {
      layers.push(layers[0]);
    }
    this.uniforms.groundLayers.value = layers;
    this.uniforms.zoneSize.value = sizeMeters;
    this.material.needsUpdate = true;
  }

  private extendShader(shader: THREE.WebGLProgramParametersWithUniforms): void {
    Object.assign(shader.uniforms, this.uniforms);
    shader.vertexShader = shader.vertexShader
      .replace("#include <common>", "#include <common>\nvarying vec2 vGroundXZ;")
      .replace("#include <begin_vertex>", "#include <begin_vertex>\nvGroundXZ = (modelMatrix * vec4(position, 1.0)).xz;");
    shader.fragmentShader = shader.fragmentShader
      .replace("#include <common>", `#include <common>
varying vec2 vGroundXZ;
uniform sampler2D splatMap;
uniform sampler2D groundLayers[${MAX_GROUND_LAYERS}];
uniform float zoneSize;
uniform float tileMeters;
vec3 groundLayer(sampler2D layer, vec2 uv) {
  vec2 rotated = mat2(0.8, -0.6, 0.6, 0.8) * uv * ${ANTI_TILE_SCALE} + 0.37;
  return mix(texture2D(layer, uv).rgb, texture2D(layer, rotated).rgb, ${ANTI_TILE_MIX});
}`)
      .replace("#include <map_fragment>", `
vec2 groundUv = vGroundXZ / tileMeters;
vec4 weights = texture2D(splatMap, vGroundXZ / zoneSize);
vec3 ground = groundLayer(groundLayers[0], groundUv) * weights.r
  + groundLayer(groundLayers[1], groundUv) * weights.g
  + groundLayer(groundLayers[2], groundUv) * weights.b
  + groundLayer(groundLayers[3], groundUv) * weights.a;
diffuseColor.rgb *= ground / max(weights.r + weights.g + weights.b + weights.a, 0.001);`);
  }
}

function splatTexture(sizeMeters: number, biomes: BiomeMap): THREE.DataTexture {
  const pixels = Math.max(2, Math.ceil(sizeMeters / SPLAT_METERS_PER_PIXEL));
  const data = new Uint8Array(pixels * pixels * 4);
  for (let row = 0; row < pixels; row++) {
    for (let column = 0; column < pixels; column++) {
      const point = { x: ((column + 0.5) / pixels) * sizeMeters, z: ((row + 0.5) / pixels) * sizeMeters };
      const weights = biomes.weightsAt(point);
      const offset = (row * pixels + column) * 4;
      for (let channel = 0; channel < MAX_GROUND_LAYERS; channel++) {
        data[offset + channel] = Math.round((weights[channel] ?? 0) * 255);
      }
    }
  }
  const texture = new THREE.DataTexture(data, pixels, pixels, THREE.RGBAFormat);
  texture.magFilter = THREE.LinearFilter;
  texture.minFilter = THREE.LinearFilter;
  texture.needsUpdate = true;
  return texture;
}
