import * as THREE from "three";

/**
 * Builds meshes for one model and remembers every material it created, so the animator can flash the
 * whole body on a hit without touching other models (materials are never shared between models).
 */
export class PartKit {
  readonly materials: THREE.MeshStandardMaterial[] = [];
  private readonly cache = new Map<string, THREE.MeshStandardMaterial>();

  material(color: number, options: { emissive?: number; emissiveIntensity?: number; opacity?: number; metalness?: number; roughness?: number } = {}): THREE.MeshStandardMaterial {
    const key = `${color}|${options.emissive ?? 0}|${options.emissiveIntensity ?? 1}|${options.opacity ?? 1}|${options.metalness ?? 0}|${options.roughness ?? 0.8}`;
    const cached = this.cache.get(key);
    if (cached) {
      return cached;
    }
    const created = new THREE.MeshStandardMaterial({
      color,
      emissive: options.emissive ?? 0x000000,
      emissiveIntensity: options.emissiveIntensity ?? 1,
      flatShading: true,
      roughness: options.roughness ?? 0.8,
      metalness: options.metalness ?? 0,
      transparent: (options.opacity ?? 1) < 1,
      opacity: options.opacity ?? 1,
      side: (options.opacity ?? 1) < 1 ? THREE.DoubleSide : THREE.FrontSide,
    });
    created.userData.baseEmissive = created.emissive.clone();
    created.userData.baseEmissiveIntensity = created.emissiveIntensity;
    this.cache.set(key, created);
    this.materials.push(created);
    return created;
  }

  mesh(geometry: THREE.BufferGeometry, color: number | THREE.Material, x = 0, y = 0, z = 0): THREE.Mesh {
    const material = typeof color === "number" ? this.material(color) : color;
    const result = new THREE.Mesh(geometry, material);
    result.position.set(x, y, z);
    result.castShadow = true;
    result.receiveShadow = true;
    return result;
  }

  glow(geometry: THREE.BufferGeometry, color: number, x = 0, y = 0, z = 0, intensity = 1.4): THREE.Mesh {
    const result = this.mesh(geometry, this.material(color, { emissive: color, emissiveIntensity: intensity }), x, y, z);
    result.castShadow = false;
    return result;
  }
}

/** An empty transform used as a joint: children hang from it and the animator rotates it. */
export function joint(x = 0, y = 0, z = 0): THREE.Group {
  const group = new THREE.Group();
  group.position.set(x, y, z);
  return group;
}

export const box = (width: number, height: number, depth: number): THREE.BufferGeometry => new THREE.BoxGeometry(width, height, depth);
export const ball = (radius: number, detail = 1): THREE.BufferGeometry => new THREE.IcosahedronGeometry(radius, detail);
export const rod = (radiusTop: number, radiusBottom: number, height: number, segments = 6): THREE.BufferGeometry =>
  new THREE.CylinderGeometry(radiusTop, radiusBottom, height, segments);
export const cone = (radius: number, height: number, segments = 6): THREE.BufferGeometry => new THREE.ConeGeometry(radius, height, segments);
