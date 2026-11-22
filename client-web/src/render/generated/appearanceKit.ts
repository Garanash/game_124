import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { clone as cloneSkinned } from "three/addons/utils/SkeletonUtils.js";
import { ClipAnimator } from "./generatedAnimators";
import { AppearanceHints, BUILD_WIDTH, KitAppearance, KitContents, OutfitId, rollAppearance, Sex } from "./kitAppearance";
import { GeneratedModel, LoadedAsset, loadAsset, ManifestEntry } from "./modelAssets";

/** manifest.kits[raceId], written by scripts/meshy-kit.mjs. Every part is optional while generation runs. */
export interface KitManifest {
  heights?: Partial<Record<Sex, number>>;
  bases?: Partial<Record<Sex, ManifestEntry>>;
  skins?: Partial<Record<Sex, Record<string, string>>>;
  outfits?: Partial<Record<OutfitId, Partial<Record<Sex, string>>>>;
  hair?: Record<string, string>;
}

/** Bones whose skin comes from the face texture; everything else is clothing from the outfit texture. */
const SKIN_REGION_BONES = new Set(["Head", "head_end", "headfront", "neck", "LeftHand", "RightHand"]);
const HEAD_BONES = new Set(["Head", "head_end", "headfront"]);
const HEAD_BONE = "Head";
const MASK_SIZE = 1024;
/** Painted UV islands bleed a little past their triangles; widening the mask hides the seam. */
const MASK_EDGE_PIXELS = 3;
const MASK_BLUR_PIXELS = 2;
/** The hair cap is the top part of the hair mesh that sits on the scalp. */
const HAIR_CAP_FRACTION = 0.3;
const SKULL_WIDTH_OVER_DEPTH = 0.95;
const HAIR_WIDTH_OVER_SKULL = 1.25;
const HAIR_TOP_CLEARANCE = 0.03;
const HAIR_BACK_OFFSET = 0.06;
/** How far below the jaw (in head heights) hair hands over from the head to the chest. */
const HAIR_BLEND_OVER_HEAD = 0.6;
const CHEST_BONE = "Spine";

interface LoadedBase {
  asset: LoadedAsset;
  mask: THREE.Texture;
  /** Head bounds in the normalised holder's space at the rest pose. */
  head: THREE.Box3;
  mesh: THREE.SkinnedMesh;
}

interface FittedHairPart {
  geometry: THREE.BufferGeometry;
  material: THREE.MeshStandardMaterial;
}

interface LoadedHair {
  scene: THREE.Object3D;
  cap: THREE.Box3;
}

/** One race's mix-and-match look: rigged bases, face and outfit textures on shared UVs, and hair meshes. */
export class AppearanceKit {
  private readonly bases = new Map<Sex, LoadedBase>();
  private readonly hair = new Map<number, LoadedHair>();
  private readonly textures = new Map<string, THREE.Texture>();
  private readonly fitted = new Map<string, FittedHairPart[]>();
  readonly contents: KitContents;

  constructor(private readonly manifest: KitManifest, private readonly loader: GLTFLoader) {
    this.contents = {
      sexes: (Object.keys(manifest.bases ?? {}) as Sex[]),
      skins: { female: idsOf(manifest.skins?.female), male: idsOf(manifest.skins?.male) },
      hair: idsOf(manifest.hair),
      outfits: Object.keys(manifest.outfits ?? {}) as OutfitId[],
    };
  }

  get isEmpty(): boolean {
    return this.contents.sexes.length === 0;
  }

  async load(): Promise<void> {
    const bases = Object.entries(this.manifest.bases ?? {}) as [Sex, ManifestEntry][];
    const hair = Object.entries(this.manifest.hair ?? {});
    await Promise.all([
      ...bases.map(async ([sex, entry]) => this.bases.set(sex, prepareBase(await loadAsset(this.loader, entry)))),
      ...hair.map(async ([index, path]) => this.hair.set(Number(index), prepareHair((await this.loader.loadAsync(`/${path}`)).scene))),
    ]);
  }

  instantiate(hints: AppearanceHints): GeneratedModel | null {
    const look = rollAppearance(this.contents, hints);
    const base = this.bases.get(look.sex);
    if (!base) {
      return null;
    }
    const model = cloneSkinned(base.asset.scene);
    this.dress(model, base, look);
    this.attachHair(model, look);
    const body = new THREE.Group();
    body.add(model);
    const width = BUILD_WIDTH[look.build] * look.heightScale;
    body.scale.set(width, look.heightScale, width);
    return { body, animator: new ClipAnimator(model, base.asset.clips), height: base.asset.entry.height * look.heightScale };
  }

  private dress(model: THREE.Object3D, base: LoadedBase, look: KitAppearance): void {
    const skinPath = look.skinIndex === null ? undefined : this.manifest.skins?.[look.sex]?.[String(look.skinIndex)];
    const outfitPath = this.manifest.outfits?.[look.outfit]?.[look.sex];
    model.traverse((child) => {
      if (child instanceof THREE.SkinnedMesh && child.material instanceof THREE.MeshStandardMaterial) {
        const original = child.material.map;
        child.material = layeredMaterial(child.material, {
          skin: skinPath ? this.texture(skinPath) : original,
          outfit: outfitPath ? this.texture(outfitPath) : original,
          mask: base.mask,
        });
      }
    });
  }

  private attachHair(model: THREE.Object3D, look: KitAppearance): void {
    if (look.hairIndex === null) {
      return;
    }
    const parts = this.fittedHair(look.sex, look.hairIndex);
    const body = firstSkinnedMesh(model);
    for (const part of parts) {
      const material = part.material.clone();
      material.color.setHex(look.hairColor);
      const strands = new THREE.SkinnedMesh(part.geometry, material);
      strands.name = "hair";
      // Skinned bounds follow the rest pose only; a swinging braid must not be culled.
      strands.frustumCulled = false;
      strands.castShadow = true;
      model.add(strands);
      strands.bind(body.skeleton, new THREE.Matrix4());
    }
  }

  /** Hair geometry skinned to one base, built once per base and hairstyle and shared by every instance. */
  private fittedHair(sex: Sex, hairIndex: number): FittedHairPart[] {
    const key = `${sex}:${hairIndex}`;
    let parts = this.fitted.get(key);
    const base = this.bases.get(sex);
    const hair = this.hair.get(hairIndex);
    if (!parts && base && hair) {
      parts = fitHair(base, hair);
      this.fitted.set(key, parts);
    }
    return parts ?? [];
  }

  /** Textures load in the background; a character first shows the base colours, then repaints itself. */
  private texture(path: string): THREE.Texture {
    let texture = this.textures.get(path);
    if (!texture) {
      texture = new THREE.TextureLoader().load(`/${path}`);
      texture.flipY = false;
      texture.colorSpace = THREE.SRGBColorSpace;
      this.textures.set(path, texture);
    }
    return texture;
  }
}

function idsOf(record: Record<string, string> | undefined): number[] {
  return Object.keys(record ?? {}).map(Number).sort((left, right) => left - right);
}

function prepareBase(asset: LoadedAsset): LoadedBase {
  const mesh = firstSkinnedMesh(asset.scene);
  const head = new THREE.Box3();
  forEachVertex(mesh, (position, bone) => {
    if (HEAD_BONES.has(bone)) {
      head.expandByPoint(position);
    }
  });
  return { asset, mask: regionMask(mesh), head, mesh };
}

function prepareHair(scene: THREE.Object3D): LoadedHair {
  scene.updateMatrixWorld(true);
  const bounds = new THREE.Box3().setFromObject(scene);
  const capBottom = bounds.max.y - (bounds.max.y - bounds.min.y) * HAIR_CAP_FRACTION;
  const cap = new THREE.Box3();
  const point = new THREE.Vector3();
  scene.traverse((child) => {
    if (child instanceof THREE.Mesh) {
      const positions = child.geometry.getAttribute("position");
      for (let vertex = 0; vertex < positions.count; vertex++) {
        point.fromBufferAttribute(positions, vertex).applyMatrix4(child.matrixWorld);
        if (point.y >= capBottom) {
          cap.expandByPoint(point);
        }
      }
    }
  });
  return { scene, cap };
}

/** Places the hair cap over the scalp at the rest pose, in the base holder's space. */
function hairPlacement(base: LoadedBase, hair: LoadedHair): THREE.Matrix4 {
  const headSize = base.head.getSize(new THREE.Vector3());
  const headCenter = base.head.getCenter(new THREE.Vector3());
  const capSize = hair.cap.getSize(new THREE.Vector3());
  const capCenter = hair.cap.getCenter(new THREE.Vector3());
  // Elf ears stretch the head sideways, so the skull is measured front to back.
  const skullWidth = Math.min(headSize.x, headSize.z * SKULL_WIDTH_OVER_DEPTH);
  const scale = capSize.x > 0 ? (skullWidth * HAIR_WIDTH_OVER_SKULL) / capSize.x : 1;
  const offset = new THREE.Vector3(
    headCenter.x - capCenter.x * scale,
    base.head.max.y + headSize.y * HAIR_TOP_CLEARANCE - hair.cap.max.y * scale,
    headCenter.z - headSize.z * HAIR_BACK_OFFSET - capCenter.z * scale,
  );
  return new THREE.Matrix4().compose(offset, new THREE.Quaternion(), new THREE.Vector3(scale, scale, scale));
}

/**
 * Bakes the hair into skinned geometry for one base: the cap follows the head, strands below the jaw blend onto
 * the upper chest, so long hair falls with the body instead of swinging rigidly with every nod.
 */
function fitHair(base: LoadedBase, hair: LoadedHair): FittedHairPart[] {
  const bones = base.mesh.skeleton.bones;
  const headIndex = bones.findIndex((bone) => bone.name === HEAD_BONE);
  const chestIndex = bones.findIndex((bone) => bone.name === CHEST_BONE);
  if (headIndex < 0 || chestIndex < 0) {
    return [];
  }
  // At the rest pose every bone maps bind space to holder space by the same transform; undo it once.
  base.mesh.skeleton.update();
  const holderToBind = new THREE.Matrix4()
    .multiplyMatrices(bones[headIndex].matrixWorld, base.mesh.skeleton.boneInverses[headIndex])
    .invert();
  const placement = hairPlacement(base, hair);
  const jaw = base.head.min.y;
  const blendHeight = (base.head.max.y - base.head.min.y) * HAIR_BLEND_OVER_HEAD;
  const parts: FittedHairPart[] = [];
  hair.scene.traverse((child) => {
    if (!(child instanceof THREE.Mesh) || !(child.material instanceof THREE.MeshStandardMaterial)) {
      return;
    }
    const geometry = child.geometry.clone();
    geometry.applyMatrix4(new THREE.Matrix4().multiplyMatrices(placement, child.matrixWorld));
    const positions = geometry.getAttribute("position");
    const skinIndex = new Uint16Array(positions.count * 4);
    const skinWeight = new Float32Array(positions.count * 4);
    for (let vertex = 0; vertex < positions.count; vertex++) {
      const headWeight = THREE.MathUtils.clamp((positions.getY(vertex) - (jaw - blendHeight)) / blendHeight, 0, 1);
      skinIndex.set([headIndex, chestIndex, 0, 0], vertex * 4);
      skinWeight.set([headWeight, 1 - headWeight, 0, 0], vertex * 4);
    }
    geometry.applyMatrix4(holderToBind);
    geometry.setAttribute("skinIndex", new THREE.Uint16BufferAttribute(skinIndex, 4));
    geometry.setAttribute("skinWeight", new THREE.Float32BufferAttribute(skinWeight, 4));
    parts.push({ geometry, material: child.material });
  });
  return parts;
}

function firstSkinnedMesh(root: THREE.Object3D): THREE.SkinnedMesh {
  let found: THREE.SkinnedMesh | null = null;
  root.traverse((child) => {
    if (!found && child instanceof THREE.SkinnedMesh) {
      found = child;
    }
  });
  if (!found) {
    throw new Error("kit base has no skinned mesh");
  }
  return found;
}

/** Calls back with each skinned vertex at the rest pose (holder space) and the name of the bone that moves it most. */
function forEachVertex(mesh: THREE.SkinnedMesh, visit: (position: THREE.Vector3, bone: string, vertex: number) => void): void {
  const positions = mesh.geometry.getAttribute("position");
  const point = new THREE.Vector3();
  mesh.skeleton.update();
  for (let vertex = 0; vertex < positions.count; vertex++) {
    mesh.getVertexPosition(vertex, point).applyMatrix4(mesh.matrixWorld);
    visit(point, dominantBone(mesh, vertex), vertex);
  }
}

function dominantBone(mesh: THREE.SkinnedMesh, vertex: number): string {
  const indices = mesh.geometry.getAttribute("skinIndex");
  const weights = mesh.geometry.getAttribute("skinWeight");
  let best = 0;
  for (let slot = 1; slot < 4; slot++) {
    if (weights.getComponent(vertex, slot) > weights.getComponent(vertex, best)) {
      best = slot;
    }
  }
  return mesh.skeleton.bones[indices.getComponent(vertex, best)]?.name ?? "";
}

/** White where the face texture shows (head, neck, hands), black where the outfit texture shows. */
function regionMask(mesh: THREE.SkinnedMesh): THREE.Texture {
  const inSkinRegion: boolean[] = [];
  forEachVertex(mesh, (_position, bone, vertex) => {
    inSkinRegion[vertex] = SKIN_REGION_BONES.has(bone);
  });
  const uvs = mesh.geometry.getAttribute("uv");
  const index = mesh.geometry.getIndex();
  const triangleCount = (index ? index.count : uvs.count) / 3;
  const vertexAt = (corner: number) => (index ? index.getX(corner) : corner);

  const shapes = document.createElement("canvas");
  shapes.width = shapes.height = MASK_SIZE;
  const draw = shapes.getContext("2d")!;
  draw.fillStyle = draw.strokeStyle = "#fff";
  draw.lineWidth = MASK_EDGE_PIXELS;
  draw.lineJoin = "round";
  for (let triangle = 0; triangle < triangleCount; triangle++) {
    const corners = [0, 1, 2].map((corner) => vertexAt(triangle * 3 + corner));
    if (corners.filter((vertex) => inSkinRegion[vertex]).length < 2) {
      continue;
    }
    draw.beginPath();
    corners.forEach((vertex, corner) => {
      const x = uvs.getX(vertex) * MASK_SIZE;
      const y = uvs.getY(vertex) * MASK_SIZE;
      corner === 0 ? draw.moveTo(x, y) : draw.lineTo(x, y);
    });
    draw.closePath();
    draw.fill();
    draw.stroke();
  }

  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = MASK_SIZE;
  const soft = canvas.getContext("2d")!;
  soft.fillStyle = "#000";
  soft.fillRect(0, 0, MASK_SIZE, MASK_SIZE);
  soft.filter = `blur(${MASK_BLUR_PIXELS}px)`;
  soft.drawImage(shapes, 0, 0);
  const texture = new THREE.CanvasTexture(canvas);
  texture.flipY = false;
  return texture;
}

interface Layers {
  skin: THREE.Texture | null;
  outfit: THREE.Texture | null;
  mask: THREE.Texture;
}

/** The base material with its colour map split in two: the face texture where the mask is white, the outfit elsewhere. */
function layeredMaterial(source: THREE.MeshStandardMaterial, layers: Layers): THREE.MeshStandardMaterial {
  const material = source.clone();
  material.map = layers.skin;
  const uniforms = { outfitMap: { value: layers.outfit }, regionMask: { value: layers.mask } };
  material.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, uniforms);
    shader.fragmentShader = shader.fragmentShader
      .replace("#include <map_pars_fragment>", "#include <map_pars_fragment>\nuniform sampler2D outfitMap;\nuniform sampler2D regionMask;")
      .replace("#include <map_fragment>", `#ifdef USE_MAP
  vec4 skinColor = texture2D( map, vMapUv );
  vec4 outfitColor = texture2D( outfitMap, vMapUv );
  diffuseColor *= mix( outfitColor, skinColor, texture2D( regionMask, vMapUv ).r );
#endif`);
  };
  return material;
}
