import { corners, Footprint, Point } from "./geometry";

const CELL_METERS = 1;
const OBSTACLE_PADDING_METERS = 0.6;
const MAX_EXPANDED_NODES = 120_000;
const NEAREST_FREE_SEARCH_CELLS = 12;
const DIAGONAL_COST = Math.SQRT2;
const NEIGHBOURS: Array<[number, number, number]> = [
  [1, 0, 1], [-1, 0, 1], [0, 1, 1], [0, -1, 1],
  [1, 1, DIAGONAL_COST], [1, -1, DIAGONAL_COST], [-1, 1, DIAGONAL_COST], [-1, -1, DIAGONAL_COST],
];

/**
 * Walkability grid over a zone. The server moves entities in straight lines, so the client turns a
 * click into a chain of straight legs that avoid buildings and sends them one at a time.
 */
export class NavigationGrid {
  private readonly size: number;
  private readonly blocked: Uint8Array;

  constructor(zoneSizeMeters: number, obstacles: Footprint[]) {
    this.size = Math.ceil(zoneSizeMeters / CELL_METERS);
    this.blocked = new Uint8Array(this.size * this.size);
    obstacles.forEach((obstacle) => this.rasterize(obstacle));
  }

  isWalkable(point: Point): boolean {
    const cellX = Math.floor(point.x / CELL_METERS);
    const cellZ = Math.floor(point.z / CELL_METERS);
    return this.inBounds(cellX, cellZ) && this.blocked[cellZ * this.size + cellX] === 0;
  }

  /** Straight-line legs from start to goal, or [] when the goal cannot be reached. */
  findPath(start: Point, goal: Point): Point[] {
    if (this.hasLineOfSight(start, goal)) {
      return [goal];
    }
    const startCell = this.nearestFreeCell(start);
    const goalCell = this.nearestFreeCell(goal);
    if (startCell === null || goalCell === null) {
      return [];
    }
    const cells = this.search(startCell, goalCell);
    if (cells.length === 0) {
      return [];
    }
    const points = cells.map((cell) => this.cellCenter(cell));
    const reachedGoal = goalCell === this.cellIndex(goal);
    points[points.length - 1] = reachedGoal ? goal : points[points.length - 1];
    return this.smooth(start, points);
  }

  hasLineOfSight(from: Point, to: Point): boolean {
    const length = Math.hypot(to.x - from.x, to.z - from.z);
    const steps = Math.max(1, Math.ceil(length / (CELL_METERS * 0.4)));
    for (let step = 1; step <= steps; step++) {
      const t = step / steps;
      if (!this.isWalkable({ x: from.x + (to.x - from.x) * t, z: from.z + (to.z - from.z) * t })) {
        return false;
      }
    }
    return true;
  }

  private search(startCell: number, goalCell: number): number[] {
    const size = this.size;
    const cost = new Float32Array(size * size).fill(Number.POSITIVE_INFINITY);
    const parent = new Int32Array(size * size).fill(-1);
    const closed = new Uint8Array(size * size);
    const goalX = goalCell % size;
    const goalZ = Math.floor(goalCell / size);
    const heap = new MinHeap();
    cost[startCell] = 0;
    heap.push(startCell, 0);
    let expanded = 0;
    while (heap.length > 0 && expanded < MAX_EXPANDED_NODES) {
      const current = heap.pop();
      if (current === goalCell) {
        return this.reconstruct(parent, goalCell);
      }
      if (closed[current]) {
        continue;
      }
      closed[current] = 1;
      expanded++;
      const x = current % size;
      const z = Math.floor(current / size);
      for (const [dx, dz, stepCost] of NEIGHBOURS) {
        const nx = x + dx;
        const nz = z + dz;
        if (!this.inBounds(nx, nz)) {
          continue;
        }
        const next = nz * size + nx;
        if (this.blocked[next] || closed[next]) {
          continue;
        }
        if (dx !== 0 && dz !== 0 && (this.blocked[z * size + nx] || this.blocked[nz * size + x])) {
          continue;
        }
        const tentative = cost[current] + stepCost;
        if (tentative < cost[next]) {
          cost[next] = tentative;
          parent[next] = current;
          heap.push(next, tentative + octile(nx - goalX, nz - goalZ));
        }
      }
    }
    return [];
  }

  private reconstruct(parent: Int32Array, goalCell: number): number[] {
    const cells: number[] = [];
    for (let cell = goalCell; cell !== -1; cell = parent[cell]) {
      cells.push(cell);
    }
    return cells.reverse();
  }

  /** Drops every waypoint that can be skipped without losing line of sight. */
  private smooth(start: Point, points: Point[]): Point[] {
    const result: Point[] = [];
    let anchor = start;
    let index = 0;
    while (index < points.length) {
      let farthest = index;
      for (let candidate = points.length - 1; candidate > index; candidate--) {
        if (this.hasLineOfSight(anchor, points[candidate])) {
          farthest = candidate;
          break;
        }
      }
      result.push(points[farthest]);
      anchor = points[farthest];
      index = farthest + 1;
    }
    return result;
  }

  private nearestFreeCell(point: Point): number | null {
    const cellX = Math.floor(point.x / CELL_METERS);
    const cellZ = Math.floor(point.z / CELL_METERS);
    for (let radius = 0; radius <= NEAREST_FREE_SEARCH_CELLS; radius++) {
      let best: number | null = null;
      let bestDistance = Number.POSITIVE_INFINITY;
      for (let dz = -radius; dz <= radius; dz++) {
        for (let dx = -radius; dx <= radius; dx++) {
          if (Math.max(Math.abs(dx), Math.abs(dz)) !== radius || !this.inBounds(cellX + dx, cellZ + dz)) {
            continue;
          }
          const index = (cellZ + dz) * this.size + cellX + dx;
          const span = dx * dx + dz * dz;
          if (!this.blocked[index] && span < bestDistance) {
            best = index;
            bestDistance = span;
          }
        }
      }
      if (best !== null) {
        return best;
      }
    }
    return null;
  }

  private rasterize(obstacle: Footprint): void {
    const padded = { ...obstacle, halfWidth: obstacle.halfWidth + OBSTACLE_PADDING_METERS, halfDepth: obstacle.halfDepth + OBSTACLE_PADDING_METERS };
    const outline = corners(padded);
    const minX = Math.max(0, Math.floor(Math.min(...outline.map((point) => point.x)) / CELL_METERS));
    const maxX = Math.min(this.size - 1, Math.ceil(Math.max(...outline.map((point) => point.x)) / CELL_METERS));
    const minZ = Math.max(0, Math.floor(Math.min(...outline.map((point) => point.z)) / CELL_METERS));
    const maxZ = Math.min(this.size - 1, Math.ceil(Math.max(...outline.map((point) => point.z)) / CELL_METERS));
    const cos = Math.cos(-padded.rotation);
    const sin = Math.sin(-padded.rotation);
    for (let z = minZ; z <= maxZ; z++) {
      for (let x = minX; x <= maxX; x++) {
        const dx = (x + 0.5) * CELL_METERS - padded.x;
        const dz = (z + 0.5) * CELL_METERS - padded.z;
        const localX = dx * cos + dz * sin;
        const localZ = -dx * sin + dz * cos;
        if (Math.abs(localX) <= padded.halfWidth && Math.abs(localZ) <= padded.halfDepth) {
          this.blocked[z * this.size + x] = 1;
        }
      }
    }
  }

  private cellIndex(point: Point): number {
    return Math.floor(point.z / CELL_METERS) * this.size + Math.floor(point.x / CELL_METERS);
  }

  private cellCenter(cell: number): Point {
    return { x: ((cell % this.size) + 0.5) * CELL_METERS, z: (Math.floor(cell / this.size) + 0.5) * CELL_METERS };
  }

  private inBounds(x: number, z: number): boolean {
    return x >= 0 && z >= 0 && x < this.size && z < this.size;
  }
}

function octile(dx: number, dz: number): number {
  const ax = Math.abs(dx);
  const az = Math.abs(dz);
  return Math.max(ax, az) + (DIAGONAL_COST - 1) * Math.min(ax, az);
}

class MinHeap {
  private readonly items: number[] = [];
  private readonly priorities: number[] = [];

  get length(): number {
    return this.items.length;
  }

  push(item: number, priority: number): void {
    this.items.push(item);
    this.priorities.push(priority);
    let index = this.items.length - 1;
    while (index > 0) {
      const parent = (index - 1) >> 1;
      if (this.priorities[parent] <= priority) {
        break;
      }
      this.swap(index, parent);
      index = parent;
    }
  }

  pop(): number {
    const top = this.items[0];
    const lastItem = this.items.pop()!;
    const lastPriority = this.priorities.pop()!;
    if (this.items.length > 0) {
      this.items[0] = lastItem;
      this.priorities[0] = lastPriority;
      let index = 0;
      for (;;) {
        const left = index * 2 + 1;
        const right = left + 1;
        let smallest = index;
        if (left < this.items.length && this.priorities[left] < this.priorities[smallest]) smallest = left;
        if (right < this.items.length && this.priorities[right] < this.priorities[smallest]) smallest = right;
        if (smallest === index) break;
        this.swap(index, smallest);
        index = smallest;
      }
    }
    return top;
  }

  private swap(a: number, b: number): void {
    [this.items[a], this.items[b]] = [this.items[b], this.items[a]];
    [this.priorities[a], this.priorities[b]] = [this.priorities[b], this.priorities[a]];
  }
}
