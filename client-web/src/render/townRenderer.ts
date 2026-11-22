import * as THREE from "three";
import { Building, PropPlacement, Settlement, wallRotation } from "../world/settlement";
import { distance } from "../world/geometry";
import { TownStyle } from "./townStyles";

const WALL_HEIGHT = 3.2;
const WALL_THICKNESS = 0.9;
const STOREY_METERS = 2.8;
const DECAL_HEIGHT = 0.025;
const NIGHT_WINDOW_GLOW = 1.6;

/** Shared materials per town: buildings are static, so one material per colour keeps draw state small. */
class Palette {
  private readonly cache = new Map<string, THREE.MeshStandardMaterial>();
  readonly windows = new THREE.MeshStandardMaterial({ color: 0x3a3020, emissive: 0xffc870, emissiveIntensity: 0, flatShading: true });
  readonly flames = new THREE.MeshStandardMaterial({ color: 0xff8a2a, emissive: 0xff7a1a, emissiveIntensity: 2, flatShading: true });
  readonly lampGlass = new THREE.MeshStandardMaterial({ color: 0xfff0c0, emissive: 0xffd27a, emissiveIntensity: 0.2, flatShading: true });

  get(color: number, roughness = 0.85): THREE.MeshStandardMaterial {
    const key = `${color}:${roughness}`;
    let material = this.cache.get(key);
    if (!material) {
      material = new THREE.MeshStandardMaterial({ color, flatShading: true, roughness });
      this.cache.set(key, material);
    }
    return material;
  }
}

/** Builds the meshes of a generated settlement and animates its lights and fires. */
export class TownRenderer {
  readonly group = new THREE.Group();
  private readonly palette = new Palette();
  private readonly flickers: THREE.Object3D[] = [];
  private clock = 0;

  constructor(private readonly settlement: Settlement, private readonly style: TownStyle) {
    this.buildGround();
    settlement.buildings.forEach((building) => this.group.add(this.building(building)));
    settlement.props.forEach((prop) => this.group.add(this.prop(prop)));
    this.buildWalls();
    this.group.traverse((child) => {
      if (child instanceof THREE.Mesh && child.userData.decal !== true) {
        child.castShadow = true;
        child.receiveShadow = true;
      }
    });
  }

  /** daylight: 0 at midnight to 1 at noon. */
  update(deltaSeconds: number, daylight: number): void {
    this.clock += deltaSeconds;
    const night = 1 - daylight;
    this.palette.windows.emissiveIntensity = night * NIGHT_WINDOW_GLOW;
    this.palette.lampGlass.emissiveIntensity = 0.2 + night * 2.2;
    this.palette.flames.emissiveIntensity = 1.8 + Math.sin(this.clock * 13) * 0.4;
    for (const flame of this.flickers) {
      flame.scale.y = 1 + Math.sin(this.clock * 11 + flame.id) * 0.18;
      flame.rotation.y += deltaSeconds * 2;
    }
  }

  dispose(): void {
    this.group.traverse((child) => child instanceof THREE.Mesh && child.geometry.dispose());
  }

  private add(parent: THREE.Object3D, geometry: THREE.BufferGeometry, material: THREE.Material, x: number, y: number, z: number): THREE.Mesh {
    const mesh = new THREE.Mesh(geometry, material);
    mesh.position.set(x, y, z);
    parent.add(mesh);
    return mesh;
  }

  private buildGround(): void {
    const roadMaterial = new THREE.MeshStandardMaterial({ color: this.style.road, roughness: 1, polygonOffset: true, polygonOffsetFactor: -1 });
    for (const road of this.settlement.roads) {
      const length = distance(road.from, road.to);
      const strip = new THREE.Mesh(new THREE.BoxGeometry(road.width, DECAL_HEIGHT, length), roadMaterial);
      strip.rotation.y = Math.atan2(road.to.x - road.from.x, road.to.z - road.from.z);
      strip.position.set((road.from.x + road.to.x) / 2, DECAL_HEIGHT, (road.from.z + road.to.z) / 2);
      strip.receiveShadow = true;
      strip.userData.decal = true;
      this.group.add(strip);
    }
    if (this.settlement.kind === "town" || this.settlement.kind === "village") {
      const plaza = new THREE.Mesh(
        new THREE.CircleGeometry(this.settlement.plazaRadius + 2, 40),
        new THREE.MeshStandardMaterial({ color: this.style.plaza, roughness: 0.95, polygonOffset: true, polygonOffsetFactor: -2 }));
      plaza.rotation.x = -Math.PI / 2;
      plaza.position.set(this.settlement.center.x, DECAL_HEIGHT * 1.5, this.settlement.center.z);
      plaza.receiveShadow = true;
      plaza.userData.decal = true;
      this.group.add(plaza);
      for (const building of this.settlement.buildings) {
        const path = new THREE.Mesh(new THREE.CircleGeometry(1.6, 12), roadMaterial);
        path.rotation.x = -Math.PI / 2;
        path.position.set(building.door.x, DECAL_HEIGHT, building.door.z);
        path.userData.decal = true;
        this.group.add(path);
      }
    }
  }

  private building(building: Building): THREE.Group {
    const group = new THREE.Group();
    group.position.set(building.x, 0, building.z);
    group.rotation.y = building.rotation;
    const width = building.halfWidth * 2;
    const depth = building.halfDepth * 2;
    switch (building.kind) {
      case "house":
        this.house(group, width, depth, 1 + (building.seed % 3 === 0 ? 1 : 0), building.seed);
        break;
      case "barn":
        this.house(group, width, depth, 1, building.seed, this.style.cloth[0]);
        break;
      case "tavern":
        this.house(group, width, depth, 2, building.seed);
        this.sign(group, depth, 0x8a5a2a, "mug");
        break;
      case "guildhall":
        this.house(group, width, depth, 2, building.seed);
        this.columns(group, width, depth);
        this.banner(group, -width / 2 + 0.8, depth / 2 + 0.6, this.style.cloth[1]);
        this.banner(group, width / 2 - 0.8, depth / 2 + 0.6, this.style.cloth[1]);
        break;
      case "townhall":
        this.house(group, width, depth, 2, building.seed);
        this.clockTower(group);
        this.columns(group, width, depth);
        break;
      case "auction_house":
        this.house(group, width, depth, 2, building.seed);
        this.dome(group, width);
        this.sign(group, depth, this.style.trim, "coin");
        break;
      case "workshop":
        this.workshop(group, width, depth);
        break;
      case "market_stall":
        this.stall(group, width, depth, this.style.cloth[building.seed % this.style.cloth.length]);
        break;
      case "watchtower":
        this.tower(group, width, 8);
        break;
      case "tent":
        this.tent(group, width, depth, this.style.cloth[building.seed % this.style.cloth.length]);
        break;
      case "ruin":
        this.ruin(group, width, depth, building.seed);
        break;
      case "shrine":
        this.tower(group, width, 4);
        break;
    }
    return group;
  }

  private house(group: THREE.Group, width: number, depth: number, storeys: number, seed: number, wallColor = this.style.wall): void {
    const height = storeys * STOREY_METERS;
    const palette = this.palette;
    this.add(group, new THREE.BoxGeometry(width + 0.3, 0.4, depth + 0.3), palette.get(this.style.stone), 0, 0.2, 0);
    this.add(group, new THREE.BoxGeometry(width, height, depth), palette.get(wallColor), 0, height / 2 + 0.2, 0);
    for (const x of [-width / 2, width / 2]) {
      for (const z of [-depth / 2, depth / 2]) {
        this.add(group, new THREE.BoxGeometry(0.28, height, 0.28), palette.get(this.style.timber), x, height / 2 + 0.2, z);
      }
    }
    for (let storey = 1; storey < storeys; storey++) {
      this.add(group, new THREE.BoxGeometry(width + 0.1, 0.22, depth + 0.1), palette.get(this.style.timber), 0, storey * STOREY_METERS + 0.2, 0);
    }
    this.add(group, new THREE.BoxGeometry(1.2, 2.1, 0.12), palette.get(this.style.timber), 0, 1.25, depth / 2 + 0.02);
    this.add(group, new THREE.BoxGeometry(1.45, 0.18, 0.18), palette.get(this.style.trim), 0, 2.35, depth / 2 + 0.05);
    for (let storey = 0; storey < storeys; storey++) {
      const y = storey * STOREY_METERS + 1.7;
      const columns = Math.max(1, Math.floor(width / 2.6));
      for (let column = 0; column < columns; column++) {
        const x = -width / 2 + (column + 0.5) * (width / columns);
        if (storey === 0 && Math.abs(x) < 1.2) {
          continue;
        }
        this.add(group, new THREE.BoxGeometry(0.75, 0.85, 0.1), palette.windows, x, y, depth / 2 + 0.03);
        this.add(group, new THREE.BoxGeometry(0.9, 0.1, 0.16), palette.get(this.style.trim), x, y - 0.5, depth / 2 + 0.06);
      }
      for (const side of [1, -1]) {
        this.add(group, new THREE.BoxGeometry(0.1, 0.85, 0.75), palette.windows, side * (width / 2 + 0.03), y, 0);
      }
    }
    this.roof(group, width, depth, height + 0.2);
    if (seed % 2 === 0) {
      this.add(group, new THREE.BoxGeometry(0.6, 1.6, 0.6), palette.get(this.style.stone), width / 4, height + 1.3, -depth / 4);
    }
  }

  private roof(group: THREE.Group, width: number, depth: number, baseY: number): void {
    const material = this.palette.get(this.style.roof);
    if (this.style.roofShape === "flat") {
      this.add(group, new THREE.BoxGeometry(width + 0.4, 0.3, depth + 0.4), material, 0, baseY + 0.15, 0);
      this.add(group, new THREE.BoxGeometry(width + 0.4, 0.5, 0.2), this.palette.get(this.style.trim), 0, baseY + 0.4, depth / 2 + 0.1);
      return;
    }
    const rise = this.style.roofShape === "steep" ? width * 0.55 : width * 0.38;
    const shape = new THREE.Shape();
    shape.moveTo(-width / 2 - 0.45, 0);
    shape.lineTo(width / 2 + 0.45, 0);
    shape.lineTo(0, rise);
    shape.closePath();
    const geometry = new THREE.ExtrudeGeometry(shape, { depth: depth + 0.7, bevelEnabled: false });
    geometry.translate(0, 0, -(depth + 0.7) / 2);
    this.add(group, geometry, material, 0, baseY, 0);
  }

  private columns(group: THREE.Group, width: number, depth: number): void {
    const count = 4;
    for (let index = 0; index < count; index++) {
      const x = -width / 2 + 1 + (index / (count - 1)) * (width - 2);
      if (Math.abs(x) < 1) {
        continue;
      }
      this.add(group, new THREE.CylinderGeometry(0.3, 0.35, 4.6, 8), this.palette.get(this.style.stone), x, 2.5, depth / 2 + 1.1);
    }
    this.add(group, new THREE.BoxGeometry(width, 0.4, 2), this.palette.get(this.style.stone), 0, 4.9, depth / 2 + 1);
  }

  private clockTower(group: THREE.Group): void {
    const stone = this.palette.get(this.style.stone);
    this.add(group, new THREE.BoxGeometry(3, 10, 3), stone, 0, 5, 0);
    this.add(group, new THREE.CylinderGeometry(0.9, 0.9, 0.1, 16), this.palette.get(0xf4f0e0), 0, 8.4, 1.52).rotateX(Math.PI / 2);
    this.add(group, new THREE.BoxGeometry(0.08, 0.7, 0.05), this.palette.get(0x1a1a1a), 0, 8.6, 1.6);
    this.add(group, new THREE.ConeGeometry(2.4, 3.2, 4), this.palette.get(this.style.roof), 0, 11.6, 0).rotateY(Math.PI / 4);
    this.banner(group, 0, 0, this.style.cloth[0], 14);
  }

  private dome(group: THREE.Group, width: number): void {
    this.add(group, new THREE.SphereGeometry(width * 0.28, 12, 8, 0, Math.PI * 2, 0, Math.PI / 2), this.palette.get(this.style.trim, 0.4), 0, 2 * STOREY_METERS + 0.6, 0);
  }

  private sign(group: THREE.Group, depth: number, color: number, icon: "mug" | "coin"): void {
    this.add(group, new THREE.BoxGeometry(0.1, 0.1, 1.2), this.palette.get(this.style.timber), 1.4, 3.2, depth / 2 + 0.6);
    this.add(group, new THREE.BoxGeometry(0.9, 0.65, 0.08), this.palette.get(color), 1.4, 2.75, depth / 2 + 1.1);
    const emblem = icon === "mug"
      ? new THREE.CylinderGeometry(0.16, 0.14, 0.3, 8)
      : new THREE.CylinderGeometry(0.22, 0.22, 0.05, 12).rotateX(Math.PI / 2);
    this.add(group, emblem, this.palette.get(icon === "mug" ? 0xe8d8a0 : 0xe7c36a, 0.4), 1.4, 2.75, depth / 2 + 1.16);
  }

  private banner(group: THREE.Group, x: number, z: number, color: number, height = 5): void {
    this.add(group, new THREE.CylinderGeometry(0.06, 0.06, height, 6), this.palette.get(this.style.timber), x, height / 2, z);
    const flag = this.add(group, new THREE.BoxGeometry(0.05, 1.3, 0.9), this.palette.get(color), x, height - 0.8, z + 0.48);
    flag.name = "flag";
  }

  private workshop(group: THREE.Group, width: number, depth: number): void {
    const palette = this.palette;
    this.add(group, new THREE.BoxGeometry(width, 0.3, depth), palette.get(this.style.stone), 0, 0.15, 0);
    this.add(group, new THREE.BoxGeometry(width, 3, 0.4), palette.get(this.style.wall), 0, 1.65, -depth / 2 + 0.2);
    for (const side of [1, -1]) {
      this.add(group, new THREE.BoxGeometry(0.4, 3, depth), palette.get(this.style.wall), side * (width / 2 - 0.2), 1.65, 0);
      this.add(group, new THREE.BoxGeometry(0.3, 3.2, 0.3), palette.get(this.style.timber), side * (width / 2 - 0.2), 1.6, depth / 2 - 0.2);
    }
    const roof = this.add(group, new THREE.BoxGeometry(width + 0.6, 0.25, depth + 1.2), palette.get(this.style.roof), 0, 3.4, 0.3);
    roof.rotation.x = -0.18;
    this.add(group, new THREE.BoxGeometry(1.8, 1.1, 1.4), palette.get(this.style.stone), -width / 4, 0.85, -depth / 2 + 1.2);
    const coals = this.add(group, new THREE.BoxGeometry(1.3, 0.2, 1), palette.flames, -width / 4, 1.45, -depth / 2 + 1.2);
    this.flickers.push(coals);
    this.add(group, new THREE.BoxGeometry(0.9, 4.5, 0.9), palette.get(this.style.stone), -width / 4, 3.4, -depth / 2 + 0.6);
    this.add(group, new THREE.BoxGeometry(2, 0.12, 0.8), palette.get(this.style.timber), width / 4, 1.05, -depth / 2 + 0.9);
    for (let index = 0; index < 4; index++) {
      this.add(group, new THREE.BoxGeometry(0.08, 0.5, 0.08), palette.get(0x9a9ea8, 0.4), width / 4 - 0.7 + index * 0.45, 2.1, -depth / 2 + 0.45);
    }
  }

  private stall(group: THREE.Group, width: number, depth: number, cloth: number): void {
    const palette = this.palette;
    this.add(group, new THREE.BoxGeometry(width, 1, 0.8), palette.get(this.style.timber), 0, 0.5, depth / 2 - 0.4);
    for (const x of [-width / 2 + 0.1, width / 2 - 0.1]) {
      for (const z of [-depth / 2 + 0.1, depth / 2 - 0.1]) {
        this.add(group, new THREE.CylinderGeometry(0.07, 0.07, 2.6, 5), palette.get(this.style.timber), x, 1.3, z);
      }
    }
    const stripes = 5;
    for (let stripe = 0; stripe < stripes; stripe++) {
      const color = stripe % 2 === 0 ? cloth : 0xf4efe2;
      const strip = this.add(group, new THREE.BoxGeometry(width / stripes, 0.06, depth + 0.5), palette.get(color), -width / 2 + (stripe + 0.5) * (width / stripes), 2.65, 0);
      strip.rotation.x = 0.18;
    }
    const goods = [0xd84a2a, 0xe8c040, 0x6ab04a, 0x8a5aa8, 0xd8883a];
    for (let index = 0; index < 6; index++) {
      this.add(group, new THREE.BoxGeometry(0.35, 0.25, 0.3), palette.get(goods[index % goods.length]), -width / 2 + 0.5 + index * ((width - 1) / 5), 1.12, depth / 2 - 0.4);
    }
  }

  private tower(group: THREE.Group, width: number, height: number): void {
    const stone = this.palette.get(this.style.stone);
    this.add(group, new THREE.BoxGeometry(width, height, width), stone, 0, height / 2, 0);
    for (const [x, z] of [[1, 1], [1, -1], [-1, 1], [-1, -1]]) {
      this.add(group, new THREE.BoxGeometry(0.8, 0.8, 0.8), stone, x * (width / 2 - 0.4), height + 0.4, z * (width / 2 - 0.4));
    }
    this.add(group, new THREE.BoxGeometry(0.6, 1, 0.1), this.palette.windows, 0, height * 0.6, width / 2 + 0.02);
    this.banner(group, 0, 0, this.style.cloth[0], height + 3);
  }

  private tent(group: THREE.Group, width: number, depth: number, cloth: number): void {
    const shape = new THREE.Shape();
    shape.moveTo(-width / 2, 0);
    shape.lineTo(width / 2, 0);
    shape.lineTo(0, 2.4);
    shape.closePath();
    const geometry = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: false });
    geometry.translate(0, 0, -depth / 2);
    this.add(group, geometry, this.palette.get(cloth), 0, 0, 0);
    this.add(group, new THREE.BoxGeometry(0.9, 1.4, 0.05), this.palette.get(0x2a2018), 0, 0.7, depth / 2 + 0.01);
  }

  private ruin(group: THREE.Group, width: number, depth: number, seed: number): void {
    const stone = this.palette.get(this.style.stone);
    const heights = [3.2, 1.4, 2.4, 0.8];
    heights.forEach((height, index) => {
      const x = -width / 2 + (index + 0.5) * (width / heights.length);
      this.add(group, new THREE.BoxGeometry(width / heights.length - 0.1, height * (0.6 + ((seed >> index) % 5) / 10), 0.8), stone, x, height / 2, -depth / 2);
    });
    const pillar = this.add(group, new THREE.CylinderGeometry(0.4, 0.45, 3.5, 8), stone, 0.5, 0.4, 0.8);
    pillar.rotation.z = Math.PI / 2 - 0.1;
    this.add(group, new THREE.CylinderGeometry(0.45, 0.5, 2.2, 8), stone, width / 2 - 0.6, 1.1, depth / 2 - 0.6);
  }

  private prop(prop: PropPlacement): THREE.Group {
    const group = new THREE.Group();
    group.position.set(prop.x, 0, prop.z);
    group.rotation.y = prop.rotation;
    const palette = this.palette;
    switch (prop.kind) {
      case "lamp":
        this.add(group, new THREE.CylinderGeometry(0.07, 0.1, 3.2, 6), palette.get(0x2a2a2a), 0, 1.6, 0);
        this.add(group, new THREE.BoxGeometry(0.4, 0.5, 0.4), palette.lampGlass, 0, 3.3, 0);
        this.add(group, new THREE.ConeGeometry(0.35, 0.3, 4), palette.get(0x2a2a2a), 0, 3.7, 0).rotateY(Math.PI / 4);
        break;
      case "bench":
        this.add(group, new THREE.BoxGeometry(1.8, 0.1, 0.5), palette.get(this.style.timber), 0, 0.5, 0);
        this.add(group, new THREE.BoxGeometry(1.8, 0.5, 0.08), palette.get(this.style.timber), 0, 0.8, -0.22);
        for (const x of [-0.75, 0.75]) {
          this.add(group, new THREE.BoxGeometry(0.1, 0.5, 0.45), palette.get(this.style.stone), x, 0.25, 0);
        }
        break;
      case "fountain":
        this.add(group, new THREE.CylinderGeometry(2, 2.2, 0.7, 16), palette.get(this.style.stone), 0, 0.35, 0);
        this.add(group, new THREE.CylinderGeometry(1.75, 1.75, 0.1, 16), palette.get(0x4a9ad8, 0.2), 0, 0.66, 0);
        this.add(group, new THREE.CylinderGeometry(0.3, 0.4, 1.8, 8), palette.get(this.style.stone), 0, 1.2, 0);
        this.add(group, new THREE.CylinderGeometry(0.9, 0.6, 0.25, 12), palette.get(this.style.stone), 0, 2.1, 0);
        this.add(group, new THREE.SphereGeometry(0.25, 8, 6), palette.get(0x8ad0ff, 0.1), 0, 2.4, 0);
        break;
      case "well":
        this.add(group, new THREE.CylinderGeometry(1, 1, 0.9, 12), palette.get(this.style.stone), 0, 0.45, 0);
        this.add(group, new THREE.ConeGeometry(1.4, 0.9, 4), palette.get(this.style.roof), 0, 2.6, 0).rotateY(Math.PI / 4);
        break;
      case "barrel":
        this.add(group, new THREE.CylinderGeometry(0.4, 0.4, 0.95, 10), palette.get(this.style.timber), 0, 0.48, 0);
        this.add(group, new THREE.CylinderGeometry(0.42, 0.42, 0.08, 10), palette.get(0x3a3a3a), 0, 0.7, 0);
        break;
      case "crate":
        this.add(group, new THREE.BoxGeometry(0.8, 0.8, 0.8), palette.get(0xa8804a), 0, 0.4, 0);
        this.add(group, new THREE.BoxGeometry(0.6, 0.6, 0.6), palette.get(0x9a7040), 0.15, 1.1, 0.1);
        break;
      case "cart":
        this.add(group, new THREE.BoxGeometry(1.4, 0.5, 2.2), palette.get(this.style.timber), 0, 0.9, 0);
        for (const x of [-0.8, 0.8]) {
          this.add(group, new THREE.CylinderGeometry(0.55, 0.55, 0.1, 10), palette.get(0x4a3020), x, 0.55, 0.3).rotateZ(Math.PI / 2);
        }
        this.add(group, new THREE.BoxGeometry(1.2, 0.4, 1.4), palette.get(0xd8c070), 0, 1.3, 0.1);
        break;
      case "haystack":
        this.add(group, new THREE.ConeGeometry(1.1, 1.6, 8), palette.get(0xd8b850), 0, 0.8, 0);
        break;
      case "tree":
        this.tree(group);
        break;
      case "anvil":
        this.add(group, new THREE.BoxGeometry(0.4, 0.5, 0.4), palette.get(0x5a3a22), 0, 0.25, 0);
        this.add(group, new THREE.BoxGeometry(0.9, 0.28, 0.35), palette.get(0x3a3a40, 0.3), 0, 0.64, 0);
        break;
      case "campfire": {
        for (let index = 0; index < 6; index++) {
          const angle = (index / 6) * Math.PI * 2;
          this.add(group, new THREE.DodecahedronGeometry(0.22, 0), palette.get(0x6a6a6a), Math.cos(angle) * 0.7, 0.12, Math.sin(angle) * 0.7);
        }
        const fire = this.add(group, new THREE.ConeGeometry(0.4, 1.1, 6), palette.flames, 0, 0.55, 0);
        this.flickers.push(fire);
        break;
      }
      case "brazier": {
        this.add(group, new THREE.CylinderGeometry(0.15, 0.25, 1.2, 6), palette.get(0x3a3a3a), 0, 0.6, 0);
        this.add(group, new THREE.CylinderGeometry(0.5, 0.3, 0.35, 8), palette.get(0x3a3a3a), 0, 1.35, 0);
        const flame = this.add(group, new THREE.ConeGeometry(0.35, 0.8, 6), palette.flames, 0, 1.85, 0);
        this.flickers.push(flame);
        break;
      }
      case "pillar":
        this.add(group, new THREE.CylinderGeometry(0.55, 0.65, 5, 8), palette.get(this.style.stone), 0, 2.5, 0);
        this.add(group, new THREE.BoxGeometry(1.5, 0.4, 1.5), palette.get(this.style.stone), 0, 5.1, 0);
        break;
      case "banner":
        this.banner(group, 0, 0, this.style.cloth[0], 4.5);
        break;
      case "notice_board":
        for (const x of [-0.7, 0.7]) {
          this.add(group, new THREE.BoxGeometry(0.12, 2, 0.12), palette.get(this.style.timber), x, 1, 0);
        }
        this.add(group, new THREE.BoxGeometry(1.6, 1, 0.08), palette.get(0x8a6a44), 0, 1.4, 0);
        for (let index = 0; index < 3; index++) {
          this.add(group, new THREE.BoxGeometry(0.35, 0.45, 0.02), palette.get(0xf0e6c8), -0.45 + index * 0.45, 1.4, 0.06);
        }
        break;
      case "fence":
        this.add(group, new THREE.BoxGeometry(2, 0.1, 0.08), palette.get(this.style.timber), 0, 0.7, 0);
        break;
      case "garden":
        this.add(group, new THREE.BoxGeometry(1.6, 0.25, 1), palette.get(0x5a3a22), 0, 0.12, 0);
        for (let index = 0; index < 4; index++) {
          this.add(group, new THREE.ConeGeometry(0.15, 0.4, 5), palette.get(index % 2 ? 0x6ab04a : 0xd84a8a), -0.55 + index * 0.37, 0.45, 0);
        }
        break;
    }
    return group;
  }

  private tree(group: THREE.Group): void {
    const trunk = this.palette.get(0x6b4a2b);
    const leaves = this.palette.get(this.style.foliage);
    switch (this.style.tree) {
      case "pine":
        this.add(group, new THREE.CylinderGeometry(0.2, 0.3, 1.4, 6), trunk, 0, 0.7, 0);
        this.add(group, new THREE.ConeGeometry(1.4, 2.4, 7), leaves, 0, 2.2, 0);
        this.add(group, new THREE.ConeGeometry(1.0, 1.9, 7), leaves, 0, 3.4, 0);
        break;
      case "palm":
        this.add(group, new THREE.CylinderGeometry(0.15, 0.25, 4, 6), trunk, 0, 2, 0).rotateZ(0.15);
        for (let index = 0; index < 6; index++) {
          const frond = this.add(group, new THREE.BoxGeometry(0.4, 0.06, 2), leaves, 0.3, 4, 0);
          frond.rotation.set(0.5, (index / 6) * Math.PI * 2, 0);
          frond.translateZ(0.9);
        }
        break;
      case "dead":
        this.add(group, new THREE.CylinderGeometry(0.12, 0.25, 3, 5), this.palette.get(0x3a3028), 0, 1.5, 0);
        this.add(group, new THREE.CylinderGeometry(0.05, 0.1, 1.4, 4), this.palette.get(0x3a3028), 0.4, 2.6, 0).rotateZ(-0.8);
        break;
      case "round":
        this.add(group, new THREE.CylinderGeometry(0.2, 0.3, 1.6, 6), trunk, 0, 0.8, 0);
        this.add(group, new THREE.IcosahedronGeometry(1.3, 1), leaves, 0, 2.5, 0);
        break;
    }
  }

  private buildWalls(): void {
    const stone = this.palette.get(this.style.stone);
    for (const wall of this.settlement.walls) {
      const length = distance(wall.from, wall.to);
      const segment = new THREE.Group();
      segment.position.set((wall.from.x + wall.to.x) / 2, 0, (wall.from.z + wall.to.z) / 2);
      segment.rotation.y = wallRotation(wall);
      this.add(segment, new THREE.BoxGeometry(length + 0.6, WALL_HEIGHT, WALL_THICKNESS), stone, 0, WALL_HEIGHT / 2, 0);
      const merlons = Math.floor(length / 1.4);
      for (let index = 0; index < merlons; index++) {
        this.add(segment, new THREE.BoxGeometry(0.7, 0.6, WALL_THICKNESS + 0.1), stone, -length / 2 + (index + 0.5) * (length / merlons), WALL_HEIGHT + 0.3, 0);
      }
      this.group.add(segment);
    }
    for (const towerPoint of this.settlement.gateTowers) {
      const tower = new THREE.Group();
      tower.position.set(towerPoint.x, 0, towerPoint.z);
      this.add(tower, new THREE.CylinderGeometry(1.5, 1.7, WALL_HEIGHT + 2.5, 10), stone, 0, (WALL_HEIGHT + 2.5) / 2, 0);
      this.add(tower, new THREE.ConeGeometry(2, 2.2, 10), this.palette.get(this.style.roof), 0, WALL_HEIGHT + 3.6, 0);
      this.add(tower, new THREE.BoxGeometry(0.4, 0.6, 0.4), this.palette.lampGlass, 0, WALL_HEIGHT + 1.2, 1.6);
      this.group.add(tower);
    }
  }
}
