import { Point } from "../world/geometry";

/** Close enough to a waypoint to send the next leg; the server stops within a few centimetres. */
export const WAYPOINT_REACHED_METERS = 0.6;

/** Feeds a multi-leg path to the server one straight MoveTo at a time. */
export class RouteFollower {
  private legs: Point[] = [];

  get active(): boolean {
    return this.legs.length > 0;
  }

  /** Returns the first leg to send right away, or null for an empty route. */
  start(legs: Point[]): Point | null {
    this.legs = [...legs];
    return this.legs[0] ?? null;
  }

  cancel(): void {
    this.legs = [];
  }

  /** Call every frame with the player's position; returns the next leg once the current one is reached. */
  advance(position: Point): Point | null {
    const current = this.legs[0];
    if (!current || Math.hypot(current.x - position.x, current.z - position.z) > WAYPOINT_REACHED_METERS) {
      return null;
    }
    this.legs.shift();
    return this.legs[0] ?? null;
  }
}
