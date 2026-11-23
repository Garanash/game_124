import { EquipmentSlot, EquipmentState } from "../api/gateway";
import { h, replaceChildren } from "./dom";

/** Doll layout unit; slots are sized in these like Diablo's (weapons 2×4, helm 2×2, rings 1×1). */
export const DOLL_CELL_PIXELS = 40;
const DOLL_WIDTH_CELLS = 14;
const DOLL_HEIGHT_CELLS = 8;

interface SlotBox { slot: EquipmentSlot; x: number; y: number; width: number; height: number; label: string }

/** Weapons on the outside, armour next to the body, the hero turning in the middle. */
const SLOT_LAYOUT: SlotBox[] = [
  { slot: "MainHand", x: 0, y: 0.5, width: 2, height: 4, label: "Оружие" },
  { slot: "LeftRing", x: 0.5, y: 5, width: 1, height: 1, label: "Кольцо" },
  { slot: "Gloves", x: 0, y: 6, width: 2, height: 2, label: "Перчатки" },
  { slot: "Head", x: 2.25, y: 0, width: 2, height: 2, label: "Шлем" },
  { slot: "Chest", x: 2.25, y: 2.25, width: 2, height: 3, label: "Доспех" },
  { slot: "Belt", x: 2.25, y: 5.5, width: 2, height: 1, label: "Пояс" },
  { slot: "Amulet", x: 9.75, y: 0, width: 2, height: 2, label: "Амулет" },
  { slot: "Boots", x: 9.75, y: 6, width: 2, height: 2, label: "Сапоги" },
  { slot: "OffHand", x: 12, y: 0.5, width: 2, height: 4, label: "Щит" },
  { slot: "RightRing", x: 12.5, y: 5, width: 1, height: 1, label: "Кольцо" },
];
const STAGE = { x: 4.5, y: 0, width: 5, height: DOLL_HEIGHT_CELLS };

export interface DollHandlers {
  /** Draws the worn item to fill a slot of the given size in pixels. */
  itemTile(itemId: string, widthPixels: number, heightPixels: number): HTMLElement;
  onPress(slot: EquipmentSlot, event: PointerEvent): void;
  onDoubleClick(slot: EquipmentSlot): void;
  describe(element: HTMLElement, itemId: string): void;
}

/** Diablo-style paper doll: worn items sit in slots around the hero's model. */
export class PaperDoll {
  readonly element = h("div", { class: "paper-doll" });
  private readonly slots = new Map<EquipmentSlot, HTMLElement>();

  constructor(stage: HTMLElement, private readonly handlers: DollHandlers) {
    this.element.style.width = `${DOLL_WIDTH_CELLS * DOLL_CELL_PIXELS}px`;
    this.element.style.height = `${DOLL_HEIGHT_CELLS * DOLL_CELL_PIXELS}px`;
    const stageBox = h("div", { class: "doll-stage" }, stage);
    position(stageBox, STAGE);
    this.element.append(stageBox);
    for (const box of SLOT_LAYOUT) {
      const element = h("div", { class: "doll-slot", "data-label": box.label });
      position(element, box);
      element.addEventListener("pointerdown", (event) => this.handlers.onPress(box.slot, event));
      element.addEventListener("dblclick", () => this.handlers.onDoubleClick(box.slot));
      this.slots.set(box.slot, element);
      this.element.append(element);
    }
  }

  render(equipment: EquipmentState): void {
    for (const box of SLOT_LAYOUT) {
      const element = this.slots.get(box.slot)!;
      const worn = equipment.slots[box.slot];
      element.classList.toggle("filled", Boolean(worn));
      if (!worn) {
        replaceChildren(element);
        continue;
      }
      const tile = this.handlers.itemTile(worn.itemId, box.width * DOLL_CELL_PIXELS, box.height * DOLL_CELL_PIXELS);
      this.handlers.describe(tile, worn.itemId);
      replaceChildren(element, tile);
    }
  }

  slotAt(clientX: number, clientY: number): EquipmentSlot | null {
    for (const [slot, element] of this.slots) {
      const bounds = element.getBoundingClientRect();
      if (clientX >= bounds.left && clientX < bounds.right && clientY >= bounds.top && clientY < bounds.bottom) {
        return slot;
      }
    }
    return null;
  }

  /** Lights the slots a dragged item fits; the one under the pointer glows brighter. */
  highlight(accepting: EquipmentSlot[], hovered: EquipmentSlot | null): void {
    for (const [slot, element] of this.slots) {
      element.classList.toggle("accepts", accepting.includes(slot));
      element.classList.toggle("drop-ok", hovered === slot && accepting.includes(slot));
      element.classList.toggle("drop-bad", hovered === slot && !accepting.includes(slot));
    }
  }
}

function position(element: HTMLElement, box: { x: number; y: number; width: number; height: number }): void {
  element.style.left = `${box.x * DOLL_CELL_PIXELS}px`;
  element.style.top = `${box.y * DOLL_CELL_PIXELS}px`;
  element.style.width = `${box.width * DOLL_CELL_PIXELS}px`;
  element.style.height = `${box.height * DOLL_CELL_PIXELS}px`;
}
