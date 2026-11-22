import { EntityFlags, EntityKind, EntityState, ServerMessages } from "../net/protocol";

/** Rendering runs this far behind the newest snapshot so there are always two samples to blend. */
export const INTERPOLATION_DELAY_SECONDS = 0.1;
const MAX_SAMPLES = 8;

interface Sample { time: number; x: number; z: number; heading: number }

export class WorldEntity {
  readonly samples: Sample[] = [];
  flags = 0;

  constructor(
    readonly id: number,
    readonly kind: EntityKind,
    readonly name: string,
    readonly definitionId: string,
    public hp: number,
    public maxHp: number,
    public level: number,
    x: number,
    z: number,
  ) {
    this.samples.push({ time: Number.NEGATIVE_INFINITY, x, z, heading: 0 });
  }

  get inCombat(): boolean {
    return (this.flags & EntityFlags.InCombat) !== 0;
  }

  get isGathering(): boolean {
    return (this.flags & EntityFlags.Gathering) !== 0;
  }

  get latest(): Sample {
    return this.samples[this.samples.length - 1];
  }

  addSample(time: number, state: EntityState): void {
    this.samples.push({ time, x: state.x, z: state.z, heading: state.heading });
    if (this.samples.length > MAX_SAMPLES) {
      this.samples.shift();
    }
    this.flags = state.flags;
  }

  /** Linear blend between the two samples around renderTime; holds the edge sample outside the buffer. */
  positionAt(renderTime: number): Sample {
    const samples = this.samples;
    if (renderTime >= samples[samples.length - 1].time) {
      return samples[samples.length - 1];
    }
    for (let index = samples.length - 1; index > 0; index--) {
      const older = samples[index - 1];
      const newer = samples[index];
      if (renderTime >= older.time) {
        const span = newer.time - older.time;
        const t = span > 0 && Number.isFinite(span) ? (renderTime - older.time) / span : 1;
        return {
          time: renderTime,
          x: older.x + (newer.x - older.x) * t,
          z: older.z + (newer.z - older.z) * t,
          heading: t < 0.5 ? older.heading : newer.heading,
        };
      }
    }
    return samples[0];
  }
}

/** Entities the server says are in range, keyed by id; driven purely by zone messages. */
export class WorldState {
  readonly entities = new Map<number, WorldEntity>();
  selfId = 0;
  zoneId = "";
  latestServerTime = 0;

  get self(): WorldEntity | undefined {
    return this.entities.get(this.selfId);
  }

  get renderTime(): number {
    return this.latestServerTime - INTERPOLATION_DELAY_SECONDS;
  }

  welcome(message: ServerMessages["WelcomeMessage"]): void {
    this.entities.clear();
    this.selfId = message.entityId;
    this.zoneId = message.zoneId;
    this.latestServerTime = message.serverTime;
  }

  spawn(message: ServerMessages["EntitySpawnMessage"]): WorldEntity {
    const entity = new WorldEntity(
      message.entityId, message.kind, message.name, message.definitionId,
      message.hp, message.maxHp, message.level, message.x, message.z);
    this.entities.set(entity.id, entity);
    return entity;
  }

  despawn(entityId: number): void {
    this.entities.delete(entityId);
  }

  applySnapshot(message: ServerMessages["WorldSnapshotMessage"]): void {
    this.latestServerTime = Math.max(this.latestServerTime, message.serverTime);
    for (const state of message.entities) {
      this.entities.get(state.entityId)?.addSample(message.serverTime, state);
    }
  }

  /** Advances the clock between snapshots so interpolation keeps moving at render rate. */
  advance(deltaSeconds: number): void {
    this.latestServerTime += deltaSeconds;
  }
}
