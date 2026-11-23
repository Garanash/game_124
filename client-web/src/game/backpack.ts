// Client mirror of Shared.Rules Backpack: shapes, rotation and fit checks for instant drag feedback.
// The server re-validates every move; these functions only decide what to highlight.
import { ContentCatalog, ItemDefinition } from "../api/content";
import { BackpackState, PlacedItem } from "../api/gateway";

export const ROTATION_COUNT = 4;

export interface Cell { x: number; y: number }

export interface Placement { x: number; y: number; rotation: number }

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

function cellKey(cell: Cell): string {
  return `${cell.x},${cell.y}`;
}
