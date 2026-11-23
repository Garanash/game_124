import { ContentCatalog, ItemDefinition } from "../api/content";
import { BackpackState, CharacterState, CharacterStats, EquipmentSlot, PlacedItem } from "../api/gateway";
import { canPlace, Cell, findFreeSpot, Placement, ROTATION_COUNT, shapeCells } from "../game/backpack";
import { isWearable, preferredSlot, slotsFor } from "../game/equipment";
import { CharacterViewer } from "../render/characterViewer";
import { h, replaceChildren } from "./dom";
import { ItemIcons } from "./itemIcons";
import { itemSummary, RARITY_LABELS, STAT_LABELS, signed } from "./labels";
import { PaperDoll } from "./paperDoll";

const CELL_PIXELS = 40;
const ICON_INSET_PIXELS = 3;
/** Pointer travel before a press becomes a drag, so double-clicks don't start one. */
const DRAG_THRESHOLD_PIXELS = 5;
const TOOLTIP_OFFSET_PIXELS = 16;

export interface InventoryActions {
  move(instanceId: string, placement: Placement): Promise<void>;
  place(itemId: string, placement: Placement): Promise<void>;
  stash(instanceId: string): Promise<void>;
  equip(instanceId: string, slot: EquipmentSlot): Promise<void>;
  unequip(slot: EquipmentSlot, placement: Placement): Promise<void>;
}

type DragSource =
  | { kind: "backpack"; instanceId: string }
  | { kind: "bag" }
  | { kind: "equipment"; slot: EquipmentSlot };

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

/** Diablo-style equipment: a paper doll around the hero, a 14×6 backpack below and the bag of loose loot beside. */
export class InventoryWindow {
  readonly root = h("div", { class: "game-window inventory-window panel hidden" });
  private readonly grid = h("div", { class: "backpack-grid" });
  private readonly bag = h("div", { class: "bag-list" });
  private readonly side = h("div", { class: "inventory-side" });
  private readonly gold = h("span", { class: "gold" });
  private readonly tooltip = h("div", { class: "item-tooltip hidden" });
  private readonly doll: PaperDoll;
  private state: CharacterState | null = null;
  private stats: CharacterStats | null = null;
  private drag: DragState | null = null;
  private busy = false;

  constructor(
    private readonly catalog: ContentCatalog,
    private readonly icons: ItemIcons,
    private readonly viewer: CharacterViewer,
    private readonly actions: InventoryActions,
    onClose: () => void,
  ) {
    this.doll = new PaperDoll(viewer.element, {
      itemTile: (itemId, width, height) => this.slotTile(itemId, width, height),
      onPress: (slot, event) => this.pressSlot(slot, event),
      onDoubleClick: (slot) => void this.unequipAnywhere(slot),
      describe: (element, itemId) => this.attachTooltip(element, this.catalog.items[itemId]),
    });
    this.root.append(
      h("div", { class: "window-header" },
        h("h3", {}, "Снаряжение"), this.gold,
        h("button", { class: "small window-close", title: "Закрыть (I)", onclick: onClose }, "✕")),
      h("div", { class: "inventory-layout" },
        h("div", { class: "inventory-main" },
          this.doll.element,
          this.grid,
          h("p", { class: "muted hint-line" }, "Перетаскивайте вещи на куклу, чтобы надеть · R — повернуть · двойной клик — надеть или убрать в сумку")),
        h("div", { class: "inventory-column" },
          h("h4", {}, "Сумка"),
          this.bag,
          h("p", { class: "muted hint-line" }, "Двойной клик — положить в рюкзак"),
          this.side)));
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
    this.viewer.setActive(open);
    if (!open) {
      this.cancelDrag();
      this.hideTooltip();
    }
  }

  render(state: CharacterState, stats: CharacterStats | null): void {
    this.state = state;
    this.stats = stats;
    this.gold.textContent = `${state.sheet.gold} з.`;
    this.viewer.show({ raceId: state.sheet.raceId, name: state.sheet.name });
    this.doll.render(state.equipment);
    this.renderGrid(state.backpack);
    this.renderBag(state.materials.stacks);
    this.renderBonuses(stats);
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
    replaceChildren(this.grid, ...cells, ...backpack.items.map((placed) => this.placedItemElement(placed)));
  }

  private placedItemElement(placed: PlacedItem): HTMLElement {
    const definition = this.catalog.items[placed.itemId];
    const shape = definition ? shapeCells(definition, placed.rotation) : [{ x: 0, y: 0 }];
    const element = this.itemTiles(placed.itemId, shape, "");
    element.style.left = `${placed.x * CELL_PIXELS}px`;
    element.style.top = `${placed.y * CELL_PIXELS}px`;
    element.addEventListener("pointerdown", (event) => {
      const grab = this.cellUnderPointer(event) ?? { x: placed.x, y: placed.y };
      const grabCell = { x: grab.x - placed.x, y: grab.y - placed.y };
      this.beginDrag(event, { kind: "backpack", instanceId: placed.instanceId }, placed.itemId, placed.rotation, grabCell);
    });
    element.addEventListener("dblclick", () => void this.useBackpackItem(placed));
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

  private renderBonuses(stats: CharacterStats | null): void {
    const bonuses = Object.entries(stats?.bonuses ?? {});
    replaceChildren(this.side,
      h("h4", {}, "Бонусы снаряжения"),
      bonuses.length === 0
        ? h("p", { class: "muted" }, "Нет. Бонусы дают только надетые вещи.")
        : h("ul", { class: "bonus-list" }, ...bonuses.map(([stat, value]) => h("li", {}, `${STAT_LABELS[stat] ?? stat} ${signed(value)}`))));
  }

  private pressSlot(slot: EquipmentSlot, event: PointerEvent): void {
    const worn = this.state?.equipment.slots[slot];
    if (worn) {
      this.beginDrag(event, { kind: "equipment", slot }, worn.itemId, 0, { x: 0, y: 0 });
    }
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
    if (drag.active) {
      await this.drop(drag, event);
    }
  }

  private async drop(drag: DragState, event: PointerEvent): Promise<void> {
    const source = drag.source;
    const slot = this.doll.slotAt(event.clientX, event.clientY);
    if (slot && source.kind === "backpack" && slotsFor(this.catalog.items[drag.itemId]).includes(slot)) {
      await this.run(() => this.actions.equip(source.instanceId, slot));
      return;
    }
    const placement = this.dropPlacement(event, drag);
    if (placement && this.state && canPlace(this.state.backpack, this.catalog, drag.itemId, placement, sourceInstance(source))) {
      await this.run(() => this.moveTo(source, drag.itemId, placement));
      return;
    }
    const overBag = this.bag.contains(document.elementFromPoint(event.clientX, event.clientY));
    if (overBag && source.kind === "backpack") {
      await this.run(() => this.actions.stash(source.instanceId));
    }
  }

  private moveTo(source: DragSource, itemId: string, placement: Placement): Promise<void> {
    switch (source.kind) {
      case "backpack": return this.actions.move(source.instanceId, placement);
      case "bag": return this.actions.place(itemId, placement);
      case "equipment": return this.actions.unequip(source.slot, placement);
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
    const accepting = drag?.source.kind === "backpack" ? slotsFor(definition) : [];
    this.doll.highlight(accepting, drag ? this.doll.slotAt(event.clientX, event.clientY) : null);
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
    const columns = Math.max(...shape.map((cell) => cell.x)) + 1;
    const rows = Math.max(...shape.map((cell) => cell.y)) + 1;
    element.append(this.iconOrName(itemId, columns * CELL_PIXELS, rows * CELL_PIXELS));
    return element;
  }

  /** A worn item: one frame filling the whole doll slot. */
  private slotTile(itemId: string, width: number, height: number): HTMLElement {
    return h("div", { class: `slot-item ${rarityClass(this.catalog.items[itemId])}` }, this.iconOrName(itemId, width, height));
  }

  private iconOrName(itemId: string, width: number, height: number): HTMLElement {
    const definition = this.catalog.items[itemId];
    const icon = this.icons.urlFor(itemId);
    if (!icon) {
      return h("span", { class: "item-name" }, definition?.name ?? itemId);
    }
    const side = Math.min(width, height) - ICON_INSET_PIXELS * 2;
    const image = h("img", { class: "item-icon", src: icon, alt: definition?.name ?? itemId, draggable: "false" });
    image.style.width = `${side}px`;
    image.style.height = `${side}px`;
    image.style.left = `${(width - side) / 2}px`;
    image.style.top = `${(height - side) / 2}px`;
    return image;
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
    this.doll.highlight([], null);
  }

  private async useBackpackItem(placed: PlacedItem): Promise<void> {
    const definition = this.catalog.items[placed.itemId];
    const slot = this.state && isWearable(definition) ? preferredSlot(definition, this.state.equipment) : null;
    await this.run(() => (slot ? this.actions.equip(placed.instanceId, slot) : this.actions.stash(placed.instanceId)));
  }

  private async unequipAnywhere(slot: EquipmentSlot): Promise<void> {
    const worn = this.state?.equipment.slots[slot];
    const spot = worn && this.state ? findFreeSpot(this.state.backpack, this.catalog, worn.itemId) : null;
    if (spot) {
      await this.run(() => this.actions.unequip(slot, spot));
    }
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
        ...itemSummary(definition).map((line) => h("p", {}, line)));
      this.tooltip.classList.remove("hidden");
      this.moveTooltip(event);
    });
    element.addEventListener("pointermove", (event) => this.moveTooltip(event));
    element.addEventListener("pointerleave", () => this.hideTooltip());
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
