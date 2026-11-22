import * as THREE from "three";
import { CSS2DObject, CSS2DRenderer } from "three/addons/renderers/CSS2DRenderer.js";
import { ZoneDefinition } from "../api/content";
import { EntityKind } from "../net/protocol";
import { WorldEntity, WorldState } from "../game/worldState";
import { daylight, hourOfDay } from "../game/npcLife";
import { AmbientPopulation, Critter, Villager } from "../world/ambientLife";
import { containsPoint, distance, seededRandom } from "../world/geometry";
import { Settlement, settlementObstacles } from "../world/settlement";
import { citizenOutfit, raceLook } from "./characters/appearance";
import { buildCritter } from "./characters/creatures";
import { buildHumanoid } from "./characters/humanoid";
import { ActionKind, Activity, Animator, CharacterAnimator } from "./characters/rig";
import { CombatEffects } from "./combatEffects";
import { GeneratedModels, mobModelKey, raceModelKey } from "./generated/generatedModels";
import { createModel, hashHue, labelHeightFor, npcMarker, NpcLook, placeMarker, selfRing } from "./models";
import { TownRenderer } from "./townRenderer";
import { styleFor, TownStyle } from "./townStyles";

const CAMERA_DISTANCE = 60;
const CAMERA_DIRECTION = new THREE.Vector3(1, 1.35, 1).normalize();
const DEFAULT_VIEW_HEIGHT_METERS = 26;
const MIN_VIEW_HEIGHT_METERS = 12;
const MAX_VIEW_HEIGHT_METERS = 60;
const ZOOM_STEP = 1.1;
const CAMERA_FOLLOW_RATE = 8;
const DECOR_PER_HECTARE = 9;
const DECOR_CLEAR_RADIUS_METERS = 14;
const GROUND_TILE_METERS = 8;
const MAX_PIXEL_RATIO = 2;
/** Display positions chase the network position no faster than this, so small corrections glide. */
const MAX_DISPLAY_SPEED = 9;
/** Farther than this the entity teleported (respawn, portal) and the view snaps. */
const SNAP_DISTANCE_METERS = 8;
const MOVING_EPSILON = 0.15;
const TURN_RATE = 10;
const AMBIENT_VISIBLE_METERS = 70;
const SHAKE_DECAY = 6;
const NIGHT_SKY = new THREE.Color(0x1a2240);
const MIN_DAYLIGHT_FOR_LIGHTS = 0.25;

const SPECIAL_ZONE_LOOKS: Record<string, Partial<TownStyle>> = {
  PvpZone: { ground: 0x6e4a3a, sky: 0x9a6f63, foliage: 0x3f2a22, tree: "dead" },
  Instance: { ground: 0x3c3a44, sky: 0x1f1d26, foliage: 0x55515f, tree: "dead" },
};

/** A body that can be swapped for a generated model once it loads; root keeps position, label and decorations. */
interface Embodied {
  root: THREE.Group;
  body: THREE.Object3D;
  modelKey: string | null;
  generated: boolean;
}

interface EntityView extends Embodied {
  animator: Animator | null;
  labelObject: CSS2DObject;
  label: HTMLDivElement;
  hpFill: HTMLDivElement;
  activityLine: HTMLSpanElement;
  labelHeight: number;
  heading: number;
  speed: number;
  facingTargetId: number | null;
  initialized: boolean;
}

interface AmbientView extends Embodied {
  animator: Animator;
  source: Villager | Critter;
}

export interface PickResult {
  entityId: number | null;
  point: THREE.Vector3 | null;
}

export interface ZoneScene {
  zone: ZoneDefinition;
  settlement: Settlement;
  population: AmbientPopulation;
}

export interface EntityPresentation {
  isTargetable(entity: WorldEntity): boolean;
  /** What a standing character is busy with, plus an optional caption under its name. */
  activityOf(entity: WorldEntity): { animation: Activity; label: string };
}

/** Owns the Three.js scene; reads WorldState every frame and never mutates game state. */
export class WorldRenderer {
  readonly scene = new THREE.Scene();
  readonly camera: THREE.OrthographicCamera;
  readonly effects: CombatEffects;
  private readonly renderer: THREE.WebGLRenderer;
  private readonly labels = new CSS2DRenderer();
  private readonly raycaster = new THREE.Raycaster();
  private readonly views = new Map<number, EntityView>();
  private readonly ambient: AmbientView[] = [];
  private readonly generatedModels = new GeneratedModels();
  private readonly sun: THREE.DirectionalLight;
  private readonly sky: THREE.HemisphereLight;
  private readonly ground: THREE.Mesh;
  private readonly decor = new THREE.Group();
  private readonly clickMarker: THREE.Mesh;
  private readonly cameraTarget = new THREE.Vector3();
  private readonly daySky = new THREE.Color();
  private town: TownRenderer | null = null;
  private viewHeight = DEFAULT_VIEW_HEIGHT_METERS;
  private clickMarkerAge = Number.POSITIVE_INFINITY;
  private shakeStrength = 0;
  private clock = 0;

  constructor(private readonly container: HTMLElement, private readonly npcLook: (definitionId: string) => NpcLook | undefined) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, MAX_PIXEL_RATIO));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    container.appendChild(this.renderer.domElement);
    this.labels.domElement.className = "label-layer";
    container.appendChild(this.labels.domElement);

    this.camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0.1, 400);
    this.sky = new THREE.HemisphereLight(0xdfefff, 0x3a3020, 1.1);
    this.scene.add(this.sky);
    const sun = new THREE.DirectionalLight(0xfff1d6, 2.2);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    const shadowExtent = 45;
    Object.assign(sun.shadow.camera, { left: -shadowExtent, right: shadowExtent, top: shadowExtent, bottom: -shadowExtent, far: 200 });
    this.scene.add(sun, sun.target);
    this.sun = sun;

    this.ground = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshStandardMaterial({ color: 0xffffff }));
    this.ground.rotation.x = -Math.PI / 2;
    this.ground.receiveShadow = true;
    this.scene.add(this.ground, this.decor);
    this.effects = new CombatEffects(this.scene);

    this.clickMarker = new THREE.Mesh(
      new THREE.RingGeometry(0.35, 0.55, 24),
      new THREE.MeshBasicMaterial({ color: 0xfff3a0, transparent: true, side: THREE.DoubleSide }));
    this.clickMarker.rotation.x = -Math.PI / 2;
    this.clickMarker.visible = false;
    this.scene.add(this.clickMarker);

    this.generatedModels.onReady((key) => this.upgradeToGenerated(key));
    window.addEventListener("resize", () => this.resize());
    this.resize();
  }

  get canvas(): HTMLCanvasElement {
    return this.renderer.domElement;
  }

  loadZone({ zone, settlement, population }: ZoneScene): void {
    const style = { ...styleFor(zone.continentId), ...SPECIAL_ZONE_LOOKS[zone.kind] };
    this.daySky.set(style.sky);
    this.scene.background = this.daySky.clone();
    this.scene.fog = new THREE.Fog(style.sky, 75, 150);
    this.ground.geometry.dispose();
    this.ground.geometry = new THREE.PlaneGeometry(zone.sizeMeters, zone.sizeMeters);
    this.ground.position.set(zone.sizeMeters / 2, 0, zone.sizeMeters / 2);
    const material = this.ground.material as THREE.MeshStandardMaterial;
    material.map?.dispose();
    material.map = groundTexture(style.ground, zone.sizeMeters);
    material.needsUpdate = true;

    if (this.town) {
      this.scene.remove(this.town.group);
      this.town.dispose();
    }
    this.town = new TownRenderer(settlement, style);
    this.scene.add(this.town.group);
    this.buildDecor(zone, settlement, style);
    this.clearEntities();
    zone.mobs.forEach((spawn) => this.generatedModels.request(mobModelKey(spawn.mobId)));
    this.buildAmbient(population);
    this.effects.clear();
  }

  zoom(direction: number): void {
    const factor = direction > 0 ? ZOOM_STEP : 1 / ZOOM_STEP;
    this.viewHeight = THREE.MathUtils.clamp(this.viewHeight * factor, MIN_VIEW_HEIGHT_METERS, MAX_VIEW_HEIGHT_METERS);
    this.resize();
  }

  showClickMarker(point: { x: number; z: number }): void {
    this.clickMarker.position.set(point.x, 0.05, point.z);
    this.clickMarker.visible = true;
    this.clickMarkerAge = 0;
  }

  shake(strength: number): void {
    this.shakeStrength = Math.max(this.shakeStrength, strength);
  }

  /** One-shot combat animation; optionally turns the character to face whoever it acts on. */
  playAction(entityId: number, kind: ActionKind, faceTargetId?: number): void {
    const view = this.views.get(entityId);
    if (!view) {
      return;
    }
    if (faceTargetId !== undefined && faceTargetId !== entityId) {
      view.facingTargetId = faceTargetId;
    }
    view.animator?.play(kind);
  }

  setDead(entityId: number, dead: boolean): void {
    this.views.get(entityId)?.animator?.setDead(dead);
  }

  /** Where the character is drawn right now, at chest height; effects spawn from here. */
  entityPosition(entityId: number): THREE.Vector3 | null {
    const view = this.views.get(entityId);
    if (!view) {
      return null;
    }
    return view.root.position.clone().setY(Math.max(0.6, view.labelHeight * 0.55));
  }

  /** Screen point in CSS pixels to the entity or ground spot under it. */
  pick(clientX: number, clientY: number): PickResult {
    this.aimRay(clientX, clientY);
    const hits = this.raycaster.intersectObjects([...this.views.values()].map((view) => view.root), true);
    for (const hit of hits) {
      const entityId = findEntityId(hit.object);
      if (entityId !== null) {
        return { entityId, point: hit.point };
      }
    }
    return { entityId: null, point: this.raycaster.intersectObject(this.ground)[0]?.point ?? null };
  }

  /** Ground point under the cursor, ignoring entities (used for combat cell targeting). */
  pickGround(clientX: number, clientY: number): THREE.Vector3 | null {
    this.aimRay(clientX, clientY);
    return this.raycaster.intersectObject(this.ground)[0]?.point ?? null;
  }

  render(world: WorldState, deltaSeconds: number, presentation: EntityPresentation): void {
    this.clock += deltaSeconds;
    this.syncEntities(world, deltaSeconds, presentation);
    this.updateAmbient(world.latestServerTime, deltaSeconds);
    this.updateCamera(world, deltaSeconds);
    this.updateDaylight(world.latestServerTime, deltaSeconds);
    this.effects.update(deltaSeconds);

    this.clickMarkerAge += deltaSeconds;
    this.clickMarker.visible = this.clickMarkerAge < 0.6;
    (this.clickMarker.material as THREE.MeshBasicMaterial).opacity = Math.max(0, 1 - this.clickMarkerAge / 0.6);

    this.renderer.render(this.scene, this.camera);
    this.labels.render(this.scene, this.camera);
  }

  private aimRay(clientX: number, clientY: number): void {
    const bounds = this.canvas.getBoundingClientRect();
    const pointer = new THREE.Vector2(
      ((clientX - bounds.left) / bounds.width) * 2 - 1,
      -((clientY - bounds.top) / bounds.height) * 2 + 1);
    this.raycaster.setFromCamera(pointer, this.camera);
  }

  private updateCamera(world: WorldState, deltaSeconds: number): void {
    const self = world.self ? this.views.get(world.self.id) : undefined;
    if (self) {
      const follow = 1 - Math.exp(-CAMERA_FOLLOW_RATE * deltaSeconds);
      this.cameraTarget.lerp(new THREE.Vector3(self.root.position.x, 0, self.root.position.z), follow);
    }
    this.shakeStrength *= Math.exp(-SHAKE_DECAY * deltaSeconds);
    const shake = new THREE.Vector3(
      Math.sin(this.clock * 71) * this.shakeStrength,
      0,
      Math.cos(this.clock * 53) * this.shakeStrength);
    this.camera.position.copy(this.cameraTarget).add(shake).addScaledVector(CAMERA_DIRECTION, CAMERA_DISTANCE);
    this.camera.lookAt(this.cameraTarget.clone().add(shake));
    this.sun.position.copy(this.cameraTarget).add(new THREE.Vector3(30, 60, 20));
    this.sun.target.position.copy(this.cameraTarget);
  }

  private updateDaylight(serverTime: number, deltaSeconds: number): void {
    const light = Math.max(MIN_DAYLIGHT_FOR_LIGHTS, daylight(hourOfDay(serverTime)));
    this.sun.intensity = 0.4 + light * 1.9;
    this.sky.intensity = 0.55 + light * 0.6;
    const sky = NIGHT_SKY.clone().lerp(this.daySky, light);
    if (this.scene.background instanceof THREE.Color) {
      this.scene.background.copy(sky);
    }
    this.scene.fog?.color.copy(sky);
    this.town?.update(deltaSeconds, light);
  }

  private syncEntities(world: WorldState, deltaSeconds: number, presentation: EntityPresentation): void {
    for (const [entityId, view] of this.views) {
      if (!world.entities.has(entityId)) {
        this.disposeView(view);
        this.views.delete(entityId);
      }
    }
    const time = world.renderTime;
    for (const entity of world.entities.values()) {
      const view = this.views.get(entity.id) ?? this.createView(entity, entity.id === world.selfId);
      this.moveView(view, entity.positionAt(time), deltaSeconds);
      this.faceView(view, entity, deltaSeconds);
      const activity = presentation.activityOf(entity);
      if (view.animator) {
        if (view.animator.isDead && entity.hp > 0 && !entity.inCombat) {
          view.animator.setDead(false);
        }
        view.animator.update(deltaSeconds, view.speed, activity.animation);
      }
      this.animateDecorations(view);
      const ratio = entity.maxHp > 0 ? entity.hp / entity.maxHp : 0;
      view.hpFill.style.width = `${Math.round(ratio * 100)}%`;
      if (view.activityLine.textContent !== activity.label) {
        view.activityLine.textContent = activity.label;
      }
      view.label.classList.toggle("in-combat", entity.inCombat);
      view.label.classList.toggle("targetable", presentation.isTargetable(entity));
      view.label.classList.toggle("gathering", entity.isGathering);
    }
  }

  private moveView(view: EntityView, target: { x: number; z: number }, deltaSeconds: number): void {
    const position = view.root.position;
    const gap = Math.hypot(target.x - position.x, target.z - position.z);
    if (!view.initialized || gap > SNAP_DISTANCE_METERS) {
      position.set(target.x, 0, target.z);
      view.initialized = true;
      view.speed = 0;
      return;
    }
    const step = Math.min(gap, MAX_DISPLAY_SPEED * deltaSeconds, gap * Math.min(1, deltaSeconds * 12));
    if (gap > 1e-4) {
      position.x += ((target.x - position.x) / gap) * step;
      position.z += ((target.z - position.z) / gap) * step;
    }
    const instantSpeed = deltaSeconds > 0 ? step / deltaSeconds : 0;
    view.speed += (instantSpeed - view.speed) * Math.min(1, deltaSeconds * 8);
    if (view.speed > MOVING_EPSILON && gap > 0.02) {
      view.heading = Math.atan2(target.x - position.x, target.z - position.z);
      view.facingTargetId = null;
    }
  }

  private faceView(view: EntityView, entity: WorldEntity, deltaSeconds: number): void {
    if (!view.animator || entity.kind === EntityKind.Portal) {
      return;
    }
    const facing = view.facingTargetId !== null ? this.views.get(view.facingTargetId) : undefined;
    if (facing) {
      view.heading = Math.atan2(facing.root.position.x - view.root.position.x, facing.root.position.z - view.root.position.z);
    }
    const current = view.root.rotation.y;
    const delta = Math.atan2(Math.sin(view.heading - current), Math.cos(view.heading - current));
    view.root.rotation.y = current + delta * Math.min(1, deltaSeconds * TURN_RATE);
  }

  private animateDecorations(view: EntityView): void {
    view.root.traverse((child) => {
      if (child.name === "spin") {
        child.rotation.z += 0.02;
      } else if (child.name === "marker") {
        child.position.y = (child.userData.baseHeight as number) + Math.sin(this.clock * 3.3) * 0.1;
        child.rotation.y += 0.03;
      } else if (child.name === "self-ring") {
        child.rotation.z += 0.01;
      }
    });
  }

  private createView(entity: WorldEntity, isSelf: boolean): EntityView {
    const npc = entity.kind === EntityKind.Npc ? this.npcLook(entity.definitionId) : undefined;
    const modelKey = generatedModelKey(entity, npc?.raceId);
    const generated = this.requestGenerated(modelKey);
    const procedural = generated ? null : createModel({ kind: entity.kind, definitionId: entity.definitionId, name: entity.name, isSelf, npc });
    const body = generated?.body ?? procedural!.root;
    const labelHeight = generated ? labelHeightFor(generated.height) : procedural!.labelHeight;
    const root = new THREE.Group();
    root.userData.entityId = entity.id;
    root.add(body);
    castShadows(body);
    if (isSelf) {
      root.add(selfRing());
    }
    if (entity.kind === EntityKind.Npc) {
      root.add(npcMarker(generated?.height ?? procedural!.rig?.height ?? labelHeight));
    }
    const label = document.createElement("div");
    label.className = `entity-label kind-${EntityKind[entity.kind].toLowerCase()}${isSelf ? " self" : ""}`;
    const name = document.createElement("span");
    name.className = "entity-name";
    name.textContent = entity.level > 0 && entity.kind !== EntityKind.Portal ? `${entity.name} · ${entity.level}` : entity.name;
    const activityLine = document.createElement("span");
    activityLine.className = "entity-activity";
    const hpBar = document.createElement("div");
    hpBar.className = "entity-hp";
    const hpFill = document.createElement("div");
    hpBar.appendChild(hpFill);
    label.append(name, activityLine, hpBar);
    const labelObject = new CSS2DObject(label);
    labelObject.position.y = labelHeight;
    root.add(labelObject);
    this.scene.add(root);
    const latest = entity.latest;
    const view: EntityView = {
      root, body, modelKey, generated: generated !== null, labelObject, label, hpFill, activityLine,
      animator: generated?.animator ?? (procedural!.rig ? new CharacterAnimator(procedural!.rig) : null),
      labelHeight,
      heading: latest.heading,
      speed: 0,
      facingTargetId: null,
      initialized: false,
    };
    root.rotation.y = latest.heading;
    this.views.set(entity.id, view);
    return view;
  }

  private disposeView(view: EntityView): void {
    view.root.traverse((child) => child instanceof CSS2DObject && child.element.remove());
    this.scene.remove(view.root);
  }

  private clearEntities(): void {
    this.views.forEach((view) => this.disposeView(view));
    this.views.clear();
    this.ambient.forEach((view) => this.scene.remove(view.root));
    this.ambient.length = 0;
  }

  private buildAmbient(population: AmbientPopulation): void {
    for (const villager of population.villagers) {
      const modelKey = raceModelKey(villager.raceId);
      const generated = this.requestGenerated(modelKey);
      if (generated) {
        this.addAmbient(generated.body, generated.animator, villager, modelKey, true);
      } else {
        const rig = buildHumanoid(raceLook(villager.raceId), citizenOutfit(villager.seed));
        this.addAmbient(rig.root, new CharacterAnimator(rig), villager, modelKey, false);
      }
    }
    for (const critter of population.critters) {
      const rig = buildCritter(critter.kind, critter.seed);
      this.addAmbient(rig.root, new CharacterAnimator(rig), critter, null, false);
    }
  }

  private addAmbient(body: THREE.Object3D, animator: Animator, source: Villager | Critter, modelKey: string | null, generated: boolean): void {
    castShadows(body);
    const root = new THREE.Group();
    root.add(body);
    this.scene.add(root);
    this.ambient.push({ root, body, modelKey, generated, animator, source });
  }

  private requestGenerated(modelKey: string | null): ReturnType<GeneratedModels["instantiate"]> {
    if (!modelKey) {
      return null;
    }
    this.generatedModels.request(modelKey);
    return this.generatedModels.instantiate(modelKey);
  }

  /** Swaps procedural bodies for a model that just finished downloading, keeping position, label and death pose. */
  private upgradeToGenerated(modelKey: string): void {
    for (const view of this.views.values()) {
      const model = this.swapBody(view, modelKey);
      if (!model) {
        continue;
      }
      model.animator.setDead(view.animator?.isDead ?? false);
      view.animator = model.animator;
      view.labelHeight = labelHeightFor(model.height);
      view.labelObject.position.y = view.labelHeight;
      const marker = view.root.getObjectByName("marker");
      if (marker) {
        placeMarker(marker, model.height);
      }
    }
    for (const view of this.ambient) {
      const model = this.swapBody(view, modelKey);
      if (model) {
        view.animator = model.animator;
      }
    }
  }

  private swapBody(view: Embodied, modelKey: string): ReturnType<GeneratedModels["instantiate"]> {
    if (view.generated || view.modelKey !== modelKey) {
      return null;
    }
    const model = this.generatedModels.instantiate(modelKey);
    if (!model) {
      return null;
    }
    castShadows(model.body);
    view.root.remove(view.body);
    view.root.add(model.body);
    view.body = model.body;
    view.generated = true;
    return model;
  }

  private updateAmbient(serverTime: number, deltaSeconds: number): void {
    for (const view of this.ambient) {
      const pose = view.source.poseAt(serverTime);
      view.root.position.set(pose.x, 0, pose.z);
      const near = distance(pose, { x: this.cameraTarget.x, z: this.cameraTarget.z }) < AMBIENT_VISIBLE_METERS;
      view.root.visible = near;
      if (!near) {
        continue;
      }
      const current = view.root.rotation.y;
      const turn = Math.atan2(Math.sin(pose.heading - current), Math.cos(pose.heading - current));
      view.root.rotation.y = current + turn * Math.min(1, deltaSeconds * TURN_RATE);
      view.animator.update(deltaSeconds, pose.speed, pose.activity);
    }
  }

  private buildDecor(zone: ZoneDefinition, settlement: Settlement, style: TownStyle): void {
    this.decor.clear();
    const random = seededRandom(hashHue(zone.id) * 0xffffffff);
    const keepClear = [zone.spawn, ...zone.portals.map((portal) => portal.position), ...zone.nodes.map((node) => node.position)];
    const obstacles = settlementObstacles(settlement);
    const townClearance = settlement.kind === "town" || settlement.kind === "village" ? settlement.radius + 6 : 0;
    const count = Math.round((zone.sizeMeters * zone.sizeMeters) / 10000 * DECOR_PER_HECTARE);
    const trunk = new THREE.MeshStandardMaterial({ color: 0x5a3f28, flatShading: true });
    const leaves = new THREE.MeshStandardMaterial({ color: style.foliage, flatShading: true });
    const stone = new THREE.MeshStandardMaterial({ color: 0x8b8b86, flatShading: true });
    for (let index = 0; index < count; index++) {
      const point = { x: random() * zone.sizeMeters, z: random() * zone.sizeMeters };
      const blocked = keepClear.some((spot) => distance(spot, point) < DECOR_CLEAR_RADIUS_METERS)
        || distance(point, settlement.center) < townClearance
        || obstacles.some((footprint) => containsPoint(footprint, point, 3));
      if (blocked) {
        continue;
      }
      const piece = new THREE.Group();
      if (random() < 0.75) {
        piece.add(...treeMeshes(style, trunk, leaves));
      } else {
        const rock = new THREE.Mesh(new THREE.DodecahedronGeometry(0.9, 0), stone);
        rock.position.y = 0.4;
        piece.add(rock);
      }
      piece.traverse((child) => {
        child.castShadow = true;
        child.receiveShadow = true;
      });
      piece.scale.setScalar(0.7 + random() * 0.9);
      piece.rotation.y = random() * Math.PI * 2;
      piece.position.set(point.x, 0, point.z);
      this.decor.add(piece);
    }
  }

  private resize(): void {
    const width = this.container.clientWidth;
    const height = this.container.clientHeight;
    const aspect = width / Math.max(1, height);
    const halfHeight = this.viewHeight / 2;
    Object.assign(this.camera, { left: -halfHeight * aspect, right: halfHeight * aspect, top: halfHeight, bottom: -halfHeight });
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width, height);
    this.labels.setSize(width, height);
  }
}

function treeMeshes(style: TownStyle, trunk: THREE.Material, leaves: THREE.Material): THREE.Mesh[] {
  const place = (mesh: THREE.Mesh, y: number) => {
    mesh.position.y = y;
    return mesh;
  };
  switch (style.tree) {
    case "palm": {
      const fronds = Array.from({ length: 5 }, (_, index) => {
        const frond = new THREE.Mesh(new THREE.BoxGeometry(0.4, 0.06, 2), leaves);
        frond.position.y = 3.6;
        frond.rotation.set(0.5, (index / 5) * Math.PI * 2, 0);
        frond.translateZ(0.9);
        return frond;
      });
      return [place(new THREE.Mesh(new THREE.CylinderGeometry(0.15, 0.25, 3.6, 6), trunk), 1.8), ...fronds];
    }
    case "dead":
      return [place(new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.25, 2.8, 5), trunk), 1.4)];
    case "round":
      return [
        place(new THREE.Mesh(new THREE.CylinderGeometry(0.2, 0.3, 1.4, 5), trunk), 0.7),
        place(new THREE.Mesh(new THREE.IcosahedronGeometry(1.3, 0), leaves), 2.3),
      ];
    case "pine":
      return [
        place(new THREE.Mesh(new THREE.CylinderGeometry(0.2, 0.3, 1.4, 5), trunk), 0.7),
        place(new THREE.Mesh(new THREE.ConeGeometry(1.3, 2.6, 6), leaves), 2.4),
      ];
  }
}

function generatedModelKey(entity: WorldEntity, npcRaceId: string | undefined): string | null {
  switch (entity.kind) {
    case EntityKind.Player:
      return raceModelKey(entity.definitionId);
    case EntityKind.Npc:
      return npcRaceId ? raceModelKey(npcRaceId) : null;
    case EntityKind.Mob:
      return mobModelKey(entity.definitionId);
    default:
      return null;
  }
}

function castShadows(object: THREE.Object3D): void {
  object.traverse((child) => {
    if (child instanceof THREE.Mesh) {
      child.castShadow = true;
    }
  });
}

function findEntityId(object: THREE.Object3D | null): number | null {
  for (let current = object; current; current = current.parent) {
    if (typeof current.userData.entityId === "number") {
      return current.userData.entityId;
    }
  }
  return null;
}

function groundTexture(color: number, sizeMeters: number): THREE.CanvasTexture {
  const size = 128;
  const canvas = document.createElement("canvas");
  canvas.width = size;
  canvas.height = size;
  const context = canvas.getContext("2d")!;
  const base = new THREE.Color(color);
  context.fillStyle = `#${base.getHexString()}`;
  context.fillRect(0, 0, size, size);
  const random = seededRandom(color);
  for (let index = 0; index < 900; index++) {
    const shade = base.clone().offsetHSL(0, 0, (random() - 0.5) * 0.08);
    context.fillStyle = `#${shade.getHexString()}`;
    context.fillRect(random() * size, random() * size, 3, 3);
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.wrapS = THREE.RepeatWrapping;
  texture.wrapT = THREE.RepeatWrapping;
  texture.repeat.set(sizeMeters / GROUND_TILE_METERS, sizeMeters / GROUND_TILE_METERS);
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}
