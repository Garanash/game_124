export interface Point { x: number; z: number }

/** Oriented rectangle on the ground; rotation is the yaw of its local +Z (the front). */
export interface Footprint {
  x: number;
  z: number;
  halfWidth: number;
  halfDepth: number;
  rotation: number;
}

export function distance(a: Point, b: Point): number {
  return Math.hypot(a.x - b.x, a.z - b.z);
}

export function toLocal(footprint: Footprint, point: Point): Point {
  const dx = point.x - footprint.x;
  const dz = point.z - footprint.z;
  const cos = Math.cos(-footprint.rotation);
  const sin = Math.sin(-footprint.rotation);
  return { x: dx * cos + dz * sin, z: -dx * sin + dz * cos };
}

export function containsPoint(footprint: Footprint, point: Point, margin = 0): boolean {
  const local = toLocal(footprint, point);
  return Math.abs(local.x) <= footprint.halfWidth + margin && Math.abs(local.z) <= footprint.halfDepth + margin;
}

export function corners(footprint: Footprint): Point[] {
  const cos = Math.cos(footprint.rotation);
  const sin = Math.sin(footprint.rotation);
  return [[-1, -1], [1, -1], [1, 1], [-1, 1]].map(([sx, sz]) => {
    const lx = sx * footprint.halfWidth;
    const lz = sz * footprint.halfDepth;
    return { x: footprint.x + lx * cos + lz * sin, z: footprint.z - lx * sin + lz * cos };
  });
}

/** Separating-axis test for two oriented rectangles grown by margin. */
export function footprintsOverlap(a: Footprint, b: Footprint, margin = 0): boolean {
  const grownA = { ...a, halfWidth: a.halfWidth + margin, halfDepth: a.halfDepth + margin };
  for (const shape of [grownA, b]) {
    for (const axisAngle of [shape.rotation, shape.rotation + Math.PI / 2]) {
      const axis = { x: Math.sin(axisAngle), z: Math.cos(axisAngle) };
      const project = (points: Point[]) => points.map((point) => point.x * axis.x + point.z * axis.z);
      const projectedA = project(corners(grownA));
      const projectedB = project(corners(b));
      if (Math.max(...projectedA) < Math.min(...projectedB) || Math.max(...projectedB) < Math.min(...projectedA)) {
        return false;
      }
    }
  }
  return true;
}

export function segmentDistanceToPoint(a: Point, b: Point, point: Point): number {
  const dx = b.x - a.x;
  const dz = b.z - a.z;
  const lengthSquared = dx * dx + dz * dz;
  const t = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1, ((point.x - a.x) * dx + (point.z - a.z) * dz) / lengthSquared));
  return Math.hypot(a.x + dx * t - point.x, a.z + dz * t - point.z);
}

/** Samples the segment densely; footprints are small compared to the sampling step, so this is exact enough. */
export function segmentHitsFootprint(a: Point, b: Point, footprint: Footprint, margin: number, ignoreNearEnds = 0): boolean {
  const length = distance(a, b);
  const steps = Math.max(2, Math.ceil(length / 0.5));
  for (let step = 0; step <= steps; step++) {
    const t = step / steps;
    const along = t * length;
    if (along < ignoreNearEnds || length - along < ignoreNearEnds) {
      continue;
    }
    if (containsPoint(footprint, { x: a.x + (b.x - a.x) * t, z: a.z + (b.z - a.z) * t }, margin)) {
      return true;
    }
  }
  return false;
}

export function segmentsIntersect(a: Point, b: Point, c: Point, d: Point): boolean {
  const cross = (o: Point, p: Point, q: Point) => (p.x - o.x) * (q.z - o.z) - (p.z - o.z) * (q.x - o.x);
  const d1 = cross(c, d, a);
  const d2 = cross(c, d, b);
  const d3 = cross(a, b, c);
  const d4 = cross(a, b, d);
  return d1 * d2 < 0 && d3 * d4 < 0;
}

/** Deterministic xorshift so every client generates the same town for the same zone. */
export function seededRandom(seed: number): () => number {
  let state = Math.floor(seed) >>> 0 || 1;
  return () => {
    state ^= state << 13;
    state ^= state >>> 17;
    state ^= state << 5;
    return (state >>> 0) / 0x100000000;
  };
}
