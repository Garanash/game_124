// Client mirror of Shared.Rules Backpack: shapes, rotation and fit checks for instant drag feedback.
// The server re-validates every move; these functions only decide what to highlight.
import { ContentCatalog, ItemDefinition } from "../api/content";
import { BackpackState, PlacedItem } from "../api/gateway";

export const ROTATION_COUNT = 4;

export interface Cell { x: number; y: number }

export interface Placement { x: number; y: number; rotation: number }

const ORTHOGONAL_STEPS: Cell[] = [{ x: 1, y: 0 }, { x: -1, y: 0 }, { x: 0, y: 1 }, { x: 0, y: -1 }];

/** Same as Backpack.ShapeCells: quarter turns clockwise, then shifted so the top-left cell is (0, 0). */
export function shapeCells(item: ItemDefinition, rotation: number): Cell[] {
  let cells = item.shape.length === 0 ? [{ x: 0, y: 0 }] : item.shape.map((offset) => ({ x: offset.x, y: offset.y }));
  const turns = ((rotation % ROTATION_COUNT) + ROTATION_COUNT) % ROTATION_COUNT;
  for (let turn = 0; turn < turns; turn++) {
    cells = cells.map((cell) => ({ x: -cell.y, y: cell.x }));
  }
  const minX = Math.min(...cells.map((cell) => cell.x));
  const minY = Math.min(...cells.map((cell) => cell.y));
  return cells.map((cell) => ({ x: cell.x - minX, y: cell.y - minY }));
}

export function occupiedCells(placed: PlacedItem, catalog: ContentCatalog): Cell[] {
  const definition = catalog.items[placed.itemId];
  return definition ? shapeCells(definition, placed.rotation).map((cell) => ({ x: placed.x + cell.x, y: placed.y + cell.y })) : [];
}

export function canPlace(backpack: BackpackState, catalog: ContentCatalog, itemId: string, placement: Placement, ignoreInstanceId?: string): boolean {
  const definition = catalog.items[itemId];
  if (!definition) {
    return false;
  }
  const taken = new Set(backpack.items
    .filter((item) => item.instanceId !== ignoreInstanceId)
    .flatMap((item) => occupiedCells(item, catalog))
    .map(cellKey));
  return shapeCells(definition, placement.rotation).every((cell) => {
    const x = placement.x + cell.x;
    const y = placement.y + cell.y;
    return x >= 0 && y >= 0 && x < backpack.width && y < backpack.height && !taken.has(cellKey({ x, y }));
  });
}

/** First spot in the same scan order as Backpack.TryAutoPlace, so "put in backpack" lands where the server would. */
export function findFreeSpot(backpack: BackpackState, catalog: ContentCatalog, itemId: string): Placement | null {
  for (let rotation = 0; rotation < ROTATION_COUNT; rotation++) {
    for (let y = 0; y < backpack.height; y++) {
      for (let x = 0; x < backpack.width; x++) {
        if (canPlace(backpack, catalog, itemId, { x, y, rotation })) {
          return { x, y, rotation };
        }
      }
    }
  }
  return null;
}

export function areAdjacent(first: PlacedItem, second: PlacedItem, catalog: ContentCatalog): boolean {
  const secondCells = new Set(occupiedCells(second, catalog).map(cellKey));
  return occupiedCells(first, catalog).some((cell) =>
    ORTHOGONAL_STEPS.some((step) => secondCells.has(cellKey({ x: cell.x + step.x, y: cell.y + step.y }))));
}

/** Instances that currently trigger at least one synergy, as source or neighbour; the UI makes them glow. */
export function synergizingItems(backpack: BackpackState, catalog: ContentCatalog): Set<string> {
  const active = new Set<string>();
  const synergies = Object.values(catalog.synergies);
  for (const source of backpack.items) {
    const sourceTags = catalog.items[source.itemId]?.tags ?? [];
    for (const neighbour of backpack.items) {
      if (neighbour.instanceId === source.instanceId || !areAdjacent(source, neighbour, catalog)) {
        continue;
      }
      const neighbourTags = catalog.items[neighbour.itemId]?.tags ?? [];
      if (synergies.some((synergy) => sourceTags.includes(synergy.sourceTag) && neighbourTags.includes(synergy.neighborTag))) {
        active.add(source.instanceId);
        active.add(neighbour.instanceId);
      }
    }
  }
  return active;
}

function cellKey(cell: Cell): string {
  return `${cell.x},${cell.y}`;
}
