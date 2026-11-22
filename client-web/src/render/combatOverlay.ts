import * as THREE from "three";
import { CELL_SIZE_METERS, CombatState } from "../game/combatState";
import { summonModel } from "./models";

const GRID_HEIGHT = 0.03;
const FRIENDLY_COLOR = 0x4fc3ff;
const HOSTILE_COLOR = 0xff5a4a;
const CURRENT_TURN_COLOR = 0xffe066;
const HOVER_COLOR = 0xffffff;
const AREA_COLOR = 0xff9a3a;

/** Draws the tactical grid and combatant rings for the active encounter on top of the world. */
export class CombatOverlay {
  private readonly root = new THREE.Group();
  private readonly rings = new Map<number, THREE.Mesh>();
  private readonly summons = new Map<number, THREE.Group>();
  private readonly hover: THREE.Mesh;
  private readonly area: THREE.Mesh;
  private grid: THREE.LineSegments | null = null;

  constructor(private readonly scene: THREE.Scene) {
    this.hover = new THREE.Mesh(
      new THREE.PlaneGeometry(CELL_SIZE_METERS * 0.92, CELL_SIZE_METERS * 0.92),
      new THREE.MeshBasicMaterial({ color: HOVER_COLOR, transparent: true, opacity: 0.25 }));
    this.hover.rotation.x = -Math.PI / 2;
    this.area = new THREE.Mesh(
      new THREE.CircleGeometry(1, 32),
      new THREE.MeshBasicMaterial({ color: AREA_COLOR, transparent: true, opacity: 0.22 }));
    this.area.rotation.x = -Math.PI / 2;
    this.root.add(this.hover, this.area);
    this.root.visible = false;
    scene.add(this.root);
  }

  show(combat: CombatState): void {
    this.hide();
    this.grid = buildGrid(combat);
    this.root.add(this.grid);
    this.root.visible = true;
  }

  hide(): void {
    if (this.grid) {
      this.root.remove(this.grid);
      this.grid.geometry.dispose();
      this.grid = null;
    }
    this.rings.forEach((ring) => this.root.remove(ring));
    this.rings.clear();
    this.summons.forEach((summon) => this.scene.remove(summon));
    this.summons.clear();
    this.root.visible = false;
  }

  /** hoverCell and areaRadiusCells describe what the player is aiming at right now. */
  update(combat: CombatState, myTeam: number, hasWorldEntity: (entityId: number) => boolean,
    hoverCell: { cellX: number; cellY: number } | null, areaRadiusCells: number): void {
    for (const view of combat.combatants.values()) {
      const ring = this.rings.get(view.entityId) ?? this.createRing(view.entityId);
      ring.visible = view.alive;
      const position = combat.cellToWorld(view.cellX, view.cellY);
      ring.position.set(position.x, GRID_HEIGHT * 2, position.z);
      const isCurrent = view.entityId === combat.currentTurnId;
      const color = isCurrent ? CURRENT_TURN_COLOR : view.team === myTeam ? FRIENDLY_COLOR : HOSTILE_COLOR;
      (ring.material as THREE.MeshBasicMaterial).color.setHex(color);
      ring.scale.setScalar(isCurrent ? 1 + Math.sin(performance.now() / 200) * 0.08 : 1);
      if (!hasWorldEntity(view.entityId)) {
        this.syncSummon(view.entityId, view.alive, position);
      }
    }
    this.hover.visible = hoverCell !== null;
    this.area.visible = hoverCell !== null && areaRadiusCells > 0;
    if (hoverCell) {
      const point = combat.cellToWorld(hoverCell.cellX, hoverCell.cellY);
      this.hover.position.set(point.x, GRID_HEIGHT * 3, point.z);
      this.area.position.set(point.x, GRID_HEIGHT * 2.5, point.z);
      this.area.scale.setScalar((areaRadiusCells + 0.5) * CELL_SIZE_METERS);
    }
  }

  private createRing(entityId: number): THREE.Mesh {
    const ring = new THREE.Mesh(
      new THREE.RingGeometry(0.5, 0.68, 24),
      new THREE.MeshBasicMaterial({ color: FRIENDLY_COLOR, side: THREE.DoubleSide }));
    ring.rotation.x = -Math.PI / 2;
    this.rings.set(entityId, ring);
    this.root.add(ring);
    return ring;
  }

  private syncSummon(entityId: number, alive: boolean, position: { x: number; z: number }): void {
    let summon = this.summons.get(entityId);
    if (!summon) {
      summon = summonModel();
      this.summons.set(entityId, summon);
      this.scene.add(summon);
    }
    summon.visible = alive;
    summon.position.set(position.x, 0, position.z);
  }
}

function buildGrid(combat: CombatState): THREE.LineSegments {
  const half = CELL_SIZE_METERS / 2;
  const minX = combat.originX - half;
  const minZ = combat.originZ - half;
  const maxX = minX + combat.width * CELL_SIZE_METERS;
  const maxZ = minZ + combat.height * CELL_SIZE_METERS;
  const points: number[] = [];
  for (let column = 0; column <= combat.width; column++) {
    const x = minX + column * CELL_SIZE_METERS;
    points.push(x, GRID_HEIGHT, minZ, x, GRID_HEIGHT, maxZ);
  }
  for (let row = 0; row <= combat.height; row++) {
    const z = minZ + row * CELL_SIZE_METERS;
    points.push(minX, GRID_HEIGHT, z, maxX, GRID_HEIGHT, z);
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.Float32BufferAttribute(points, 3));
  return new THREE.LineSegments(geometry, new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.35 }));
}
