import { ContentCatalog, ItemDefinition } from "../api/content";
import { BackpackState, CharacterState, CharacterStats, PlacedItem } from "../api/gateway";
import { canPlace, Cell, findFreeSpot, Placement, ROTATION_COUNT, shapeCells, synergizingItems } from "../game/backpack";
import { h, replaceChildren } from "./dom";
import { ItemIcons } from "./itemIcons";
import { itemSummary, modifierText, RARITY_LABELS, STAT_LABELS, signed } from "./labels";

const CELL_PIXELS = 56;
/** Pointer travel before a press becomes a drag, so double-clicks don't start one. */
const ICON_INSET_PIXELS = 4;
const DRAG_THRESHOLD_PIXELS = 5;
const TOOLTIP_OFFSET_PIXELS = 16;

export interface InventoryActions {
  move(instanceId: string, placement: Placement): Promise<void>;
  place(itemId: string, placement: Placement): Promise<void>;
  stash(instanceId: string): Promise<void>;
  fuse(recipeId: string): Promise<void>;
}

type DragSource = { kind: "backpack"; instanceId: string } | { kind: "bag" };

interface DragState {
  source: DragSource;
  itemId: string;
  rotation: number;
  /** Which cell of the item shape is under the pointer, so the item doesn't jump to its corner. */
  grabCell: Cell;
  startX: number;
  startY: number;
  active: boolean;
  ghost: HTMLElement | null;
}

/** Backpack Brawl style grid: drag items around, R rotates, drop on the bag to unequip. */
export class InventoryWindow {
  readonly root = h("div", { class: "game-window inventory-window panel hidden" });
  private readonly grid = h("div", { class: "backpack-grid" });
  private readonly bag = h("div", { class: "bag-list" });
  private readonly side = h("div", { class: "inventory-side" });
  private readonly gold = h("span", { class: "gold" });
  private readonly tooltip = h("div", { class: "item-tooltip hidden" });
  private state: CharacterState | null = null;
  private stats: CharacterStats | null = null;
  private drag: DragState | null = null;
  private busy = false;

  constructor(
    private readonly catalog: ContentCatalog,
    private readonly icons: ItemIcons,
    private readonly actions: InventoryActions,
    onClose: () => void,
  ) {
    this.root.append(
      h("div", { class: "window-header" },
        h("h3", {}, "Инвентарь"), this.gold,
        h("button", { class: "small window-close", title: "Закрыть (I)", onclick: onClose }, "✕")),
      h("div", { class: "inventory-layout" },
        h("div", {},
          h("h4", {}, "Рюкзак"),
          this.grid,
          h("p", { class: "muted hint-line" }, "Перетаскивайте предметы · R — повернуть · двойной клик — убрать в сумку")),
        h("div", {},
          h("h4", {}, "Сумка"),
          this.bag,
          h("p", { class: "muted hint-line" }, "Двойной клик — положить в рюкзак")),
        this.side));
    document.body.append(this.tooltip);
    window.addEventListener("pointermove", (event) => this.onPointerMove(event));
    window.addEventListener("pointerup", (event) => void this.onPointerUp(event));
    window.addEventListener("keydown", (event) => this.onKey(event), true);
    void icons.ready.then(() => this.rerender());
  }

  get isOpen(): boolean {
    return !this.root.classList.contains("hidden");
  }

  setOpen(open: boolean): void {
    this.root.classList.toggle("hidden", !open);
    if (!open) {
      this.cancelDrag();
      this.hideTooltip();
    }
  }

  render(state: CharacterState, stats: CharacterStats | null): void {
    this.state = state;
    this.stats = stats;
    this.gold.textContent = `${state.sheet.gold} з.`;
    this.renderGrid(state.backpack);
    this.renderBag(state.materials.stacks);
    this.renderSide(stats);
  }

  private renderGrid(backpack: BackpackState): void {
    this.grid.style.width = `${backpack.width * CELL_PIXELS}px`;
    this.grid.style.height = `${backpack.height * CELL_PIXELS}px`;
    const cells = Array.from({ length: backpack.width * backpack.height }, (_, index) => {
      const cell = h("div", { class: "backpack-cell" });
      cell.style.left = `${(index % backpack.width) * CELL_PIXELS}px`;
      cell.style.top = `${Math.floor(index / backpack.width) * CELL_PIXELS}px`;
      cell.dataset.cell = `${index % backpack.width},${Math.floor(index / backpack.width)}`;
      return cell;
    });
    const glowing = synergizingItems(backpack, this.catalog);
    const items = backpack.items.map((placed) => this.placedItemElement(placed, glowing.has(placed.instanceId)));
    replaceChildren(this.grid, ...cells, ...items);
  }

  private placedItemElement(placed: PlacedItem, synergizing: boolean): HTMLElement {
    const definition = this.catalog.items[placed.itemId];
    const shape = definition ? shapeCells(definition, placed.rotation) : [{ x: 0, y: 0 }];
    const element = this.itemTiles(placed.itemId, shape, synergizing ? "synergy" : "");
    element.style.left = `${placed.x * CELL_PIXELS}px`;
    element.style.top = `${placed.y * CELL_PIXELS}px`;
    element.addEventListener("pointerdown", (event) => {
      const grab = this.cellUnderPointer(event) ?? { x: placed.x, y: placed.y };
      const grabCell = { x: grab.x - placed.x, y: grab.y - placed.y };
      this.beginDrag(event, { kind: "backpack", instanceId: placed.instanceId }, placed.itemId, placed.rotation, grabCell);
    });
    element.addEventListener("dblclick", () => void this.run(() => this.actions.stash(placed.instanceId)));
    this.attachTooltip(element, definition);
    return element;
  }

  private renderBag(stacks: Record<string, number>): void {
    const entries = Object.entries(stacks).filter(([, quantity]) => quantity > 0);
    if (entries.length === 0) {
      replaceChildren(this.bag, h("p", { class: "muted" }, "Пусто. Добыча с мобов и сбор ресурсов попадают сюда."));
      return;
    }
    entries.sort(([first], [second]) => (this.catalog.items[first]?.name ?? first).localeCompare(this.catalog.items[second]?.name ?? second, "ru"));
    replaceChildren(this.bag, ...entries.map(([itemId, quantity]) => {
      const definition = this.catalog.items[itemId];
      const icon = this.icons.urlFor(itemId);
      const row = h("div", { class: `bag-item ${rarityClass(definition)}` },
        h("span", { class: "bag-item-title" },
          icon ? h("img", { class: "bag-icon", src: icon, alt: "", draggable: "false" }) : null,
          definition?.name ?? itemId),
        quantity > 1 ? h("span", { class: "muted" }, `×${quantity}`) : null);
      row.addEventListener("pointerdown", (event) => this.beginDrag(event, { kind: "bag" }, itemId, 0, { x: 0, y: 0 }));
      row.addEventListener("dblclick", () => void this.placeAnywhere(itemId));
      this.attachTooltip(row, definition);
      return row;
    }));
  }

  private renderSide(stats: CharacterStats | null): void {
    const bonuses = Object.entries(stats?.bonuses ?? {});
    const fusions = (stats?.availableFusions ?? []).map((recipeId) => this.catalog.fusions[recipeId]).filter(Boolean);
    replaceChildren(this.side,
      h("h4", {}, "Бонусы предметов"),
      bonuses.length === 0
        ? h("p", { class: "muted" }, "Нет. Кладите рядом предметы, которые усиливают друг друга, — они подсветятся.")
        : h("ul", { class: "bonus-list" }, ...bonuses.map(([stat, value]) => h("li", {}, `${STAT_LABELS[stat] ?? stat} ${signed(value)}`))),
      h("h4", {}, "Слияние"),
      fusions.length === 0
        ? h("p", { class: "muted" }, "Положите вплотную одинаковые предметы или ингредиенты рецепта, чтобы слить их.")
        : h("div", { class: "fusion-list" }, ...fusions.map((recipe) => h("button", {
          class: "primary small",
          onclick: () => void this.run(() => this.actions.fuse(recipe.id)),
        }, `${recipe.inputs.map((input) => this.catalog.items[input]?.name ?? input).join(" + ")} → ${this.catalog.items[recipe.output]?.name ?? recipe.output}`))));
  }

  private beginDrag(event: PointerEvent, source: DragSource, itemId: string, rotation: number, grabCell: Cell): void {
    if (event.button !== 0 || this.busy) {
      return;
    }
    event.preventDefault();
    this.drag = { source, itemId, rotation, grabCell, startX: event.clientX, startY: event.clientY, active: false, ghost: null };
  }

  private onPointerMove(event: PointerEvent): void {
    const drag = this.drag;
    if (!drag) {
      return;
    }
    if (!drag.active && Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) > DRAG_THRESHOLD_PIXELS) {
      drag.active = true;
      this.hideTooltip();
      drag.ghost = this.ghostFor(drag.itemId, drag.rotation);
      document.body.append(drag.ghost);
    }
    if (drag.active && drag.ghost) {
      drag.ghost.style.left = `${event.clientX - (drag.grabCell.x + 0.5) * CELL_PIXELS}px`;
      drag.ghost.style.top = `${event.clientY - (drag.grabCell.y + 0.5) * CELL_PIXELS}px`;
      this.previewDrop(event);
    }
  }

  private async onPointerUp(event: PointerEvent): Promise<void> {
    const drag = this.drag;
    if (!drag) {
      return;
    }
    this.cancelDrag();
    if (!drag.active) {
      return;
    }
    const placement = this.dropPlacement(event, drag);
    const overBag = this.bag.contains(document.elementFromPoint(event.clientX, event.clientY));
    if (placement && this.state && canPlace(this.state.backpack, this.catalog, drag.itemId, placement, sourceInstance(drag.source))) {
      const source = drag.source;
      await this.run(() => source.kind === "backpack" ? this.actions.move(source.instanceId, placement) : this.actions.place(drag.itemId, placement));
    } else if (overBag && drag.source.kind === "backpack") {
      const instanceId = drag.source.instanceId;
      await this.run(() => this.actions.stash(instanceId));
    }
  }

  private onKey(event: KeyboardEvent): void {
    if (!this.drag?.active || event.code !== "KeyR") {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    const drag = this.drag;
    drag.rotation = (drag.rotation + 1) % ROTATION_COUNT;
    drag.grabCell = { x: 0, y: 0 };
    drag.ghost?.remove();
    drag.ghost = this.ghostFor(drag.itemId, drag.rotation);
    document.body.append(drag.ghost);
  }

  private previewDrop(event: PointerEvent): void {
    this.grid.querySelectorAll(".backpack-cell").forEach((cell) => cell.classList.remove("drop-ok", "drop-bad"));
    const drag = this.drag;
    const definition = drag ? this.catalog.items[drag.itemId] : undefined;
    const placement = drag ? this.dropPlacement(event, drag) : null;
    if (!drag || !definition || !placement || !this.state) {
      return;
    }
    const fits = canPlace(this.state.backpack, this.catalog, drag.itemId, placement, sourceInstance(drag.source));
    for (const cell of shapeCells(definition, placement.rotation)) {
      this.grid.querySelector(`[data-cell="${placement.x + cell.x},${placement.y + cell.y}"]`)?.classList.add(fits ? "drop-ok" : "drop-bad");
    }
  }

  private dropPlacement(event: PointerEvent, drag: DragState): Placement | null {
    const cell = this.cellUnderPointer(event);
    return cell ? { x: cell.x - drag.grabCell.x, y: cell.y - drag.grabCell.y, rotation: drag.rotation } : null;
  }

  private cellUnderPointer(event: PointerEvent): Cell | null {
    const bounds = this.grid.getBoundingClientRect();
    const x = Math.floor((event.clientX - bounds.left) / CELL_PIXELS);
    const y = Math.floor((event.clientY - bounds.top) / CELL_PIXELS);
    const backpack = this.state?.backpack;
    return backpack && x >= 0 && y >= 0 && x < backpack.width && y < backpack.height ? { x, y } : null;
  }

  private ghostFor(itemId: string, rotation: number): HTMLElement {
    const definition = this.catalog.items[itemId];
    return this.itemTiles(itemId, definition ? shapeCells(definition, rotation) : [{ x: 0, y: 0 }], "drag-ghost");
  }

  /** One framed tile per cell of the item's shape, with the painted icon stretched over the whole shape. */
  private itemTiles(itemId: string, shape: Cell[], extraClass: string): HTMLElement {
    const definition = this.catalog.items[itemId];
    const element = h("div", { class: `backpack-item ${rarityClass(definition)} ${extraClass}`.trim() });
    for (const cell of shape) {
      const part = h("div", { class: "item-cell" });
      part.style.left = `${cell.x * CELL_PIXELS}px`;
      part.style.top = `${cell.y * CELL_PIXELS}px`;
      element.append(part);
    }
    const icon = this.icons.urlFor(itemId);
    if (icon) {
      const columns = Math.max(...shape.map((cell) => cell.x)) + 1;
      const rows = Math.max(...shape.map((cell) => cell.y)) + 1;
      const side = Math.min(columns, rows) * CELL_PIXELS - ICON_INSET_PIXELS * 2;
      const image = h("img", { class: "item-icon", src: icon, alt: definition?.name ?? itemId, draggable: "false" });
      image.style.width = `${side}px`;
      image.style.height = `${side}px`;
      image.style.left = `${(columns * CELL_PIXELS - side) / 2}px`;
      image.style.top = `${(rows * CELL_PIXELS - side) / 2}px`;
      element.append(image);
    } else {
      element.append(h("span", { class: "item-name" }, definition?.name ?? itemId));
    }
    return element;
  }

  private rerender(): void {
    if (this.state && this.isOpen) {
      this.render(this.state, this.stats);
    }
  }

  private cancelDrag(): void {
    this.drag?.ghost?.remove();
    this.drag = null;
    this.grid.querySelectorAll(".backpack-cell").forEach((cell) => cell.classList.remove("drop-ok", "drop-bad"));
  }

  private async placeAnywhere(itemId: string): Promise<void> {
    const spot = this.state ? findFreeSpot(this.state.backpack, this.catalog, itemId) : null;
    if (spot) {
      await this.run(() => this.actions.place(itemId, spot));
    }
  }

  /** One request at a time: the window re-renders from the server's answer, not from guesses. */
  private async run(action: () => Promise<void>): Promise<void> {
    if (this.busy) {
      return;
    }
    this.busy = true;
    this.root.classList.add("busy");
    try {
      await action();
    } finally {
      this.busy = false;
      this.root.classList.remove("busy");
    }
  }

  private attachTooltip(element: HTMLElement, definition: ItemDefinition | undefined): void {
    if (!definition) {
      return;
    }
    element.addEventListener("pointerenter", (event) => {
      if (this.drag?.active) {
        return;
      }
      replaceChildren(this.tooltip,
        h("strong", { class: `rarity-text-${definition.rarity.toLowerCase()}` }, definition.name),
        h("small", { class: "muted" }, RARITY_LABELS[definition.rarity] ?? definition.rarity),
        ...itemSummary(definition).map((line) => h("p", {}, line)),
        ...this.synergyHints(definition));
      this.tooltip.classList.remove("hidden");
      this.moveTooltip(event);
    });
    element.addEventListener("pointermove", (event) => this.moveTooltip(event));
    element.addEventListener("pointerleave", () => this.hideTooltip());
  }

  private synergyHints(definition: ItemDefinition): HTMLElement[] {
    return Object.values(this.catalog.synergies)
      .filter((synergy) => definition.tags.includes(synergy.sourceTag))
      .map((synergy) => h("p", { class: "synergy-hint" }, `Рядом с «${synergy.neighborTag}»: ${synergy.bonus.map(modifierText).join(", ")}`));
  }

  private moveTooltip(event: PointerEvent): void {
    this.tooltip.style.left = `${event.clientX + TOOLTIP_OFFSET_PIXELS}px`;
    this.tooltip.style.top = `${event.clientY + TOOLTIP_OFFSET_PIXELS}px`;
  }

  private hideTooltip(): void {
    this.tooltip.classList.add("hidden");
  }
}

function sourceInstance(source: DragSource): string | undefined {
  return source.kind === "backpack" ? source.instanceId : undefined;
}

function rarityClass(definition: ItemDefinition | undefined): string {
  return `rarity-${(definition?.rarity ?? "Common").toLowerCase()}`;
}
