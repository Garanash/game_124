import { ContentCatalog } from "../api/content";
import { ApiError, CharacterState, CharacterStats, GatewayClient } from "../api/gateway";
import { playerErrorText } from "../net/errorText";
import { CharacterWindow } from "../ui/characterWindow";
import { Hud } from "../ui/hud";
import { InventoryWindow } from "../ui/inventoryWindow";

/** Inventory and character sheet windows: fetch stats, run backpack actions, redraw from the server's answer. */
export class CharacterPanels {
  private readonly inventory: InventoryWindow;
  private readonly sheet: CharacterWindow;
  private state: CharacterState | null = null;
  private stats: CharacterStats | null = null;

  constructor(
    private readonly gateway: GatewayClient,
    catalog: ContentCatalog,
    private readonly characterId: string,
    private readonly hud: Hud,
    /** Backpack changes alter max HP and the weapon, so the whole session refreshes its character. */
    private readonly onCharacterChanged: () => Promise<void>,
  ) {
    this.inventory = new InventoryWindow(catalog, {
      move: (instanceId, placement) => this.act(() => gateway.moveBackpackItem(characterId, instanceId, placement.x, placement.y, placement.rotation)),
      place: (itemId, placement) => this.act(() => gateway.placeFromBag(characterId, itemId, placement.x, placement.y, placement.rotation)),
      stash: (instanceId) => this.act(() => gateway.stashToBag(characterId, instanceId)),
      fuse: (recipeId) => this.act(() => gateway.fuse(characterId, recipeId), "Предметы слиты!"),
    }, () => this.toggleInventory());
    this.sheet = new CharacterWindow(catalog, () => this.toggleCharacter());
    hud.addWindow(this.inventory.root);
    hud.addWindow(this.sheet.root);
  }

  get anyOpen(): boolean {
    return this.inventory.isOpen || this.sheet.isOpen;
  }

  toggleInventory(): void {
    this.inventory.setOpen(!this.inventory.isOpen);
    void this.refreshIfOpen();
  }

  toggleCharacter(): void {
    this.sheet.setOpen(!this.sheet.isOpen);
    void this.refreshIfOpen();
  }

  closeAll(): void {
    this.inventory.setOpen(false);
    this.sheet.setOpen(false);
  }

  /** Called by the session whenever it re-fetched the character (loot, quests, rewards). */
  update(state: CharacterState): void {
    this.state = state;
    this.redraw();
    void this.refreshIfOpen(false);
  }

  private async refreshIfOpen(refetchState = true): Promise<void> {
    if (!this.anyOpen) {
      return;
    }
    try {
      if (refetchState || !this.state) {
        this.state = await this.gateway.character(this.characterId);
      }
      this.stats = await this.gateway.stats(this.characterId);
      this.redraw();
    } catch (error) {
      this.reportError(error);
    }
  }

  private redraw(): void {
    if (!this.state) {
      return;
    }
    if (this.inventory.isOpen) {
      this.inventory.render(this.state, this.stats);
    }
    if (this.sheet.isOpen) {
      this.sheet.render(this.state, this.stats);
    }
  }

  private async act(request: () => Promise<unknown>, successText?: string): Promise<void> {
    try {
      await request();
      if (successText) {
        this.hud.toast(successText, "loot");
      }
    } catch (error) {
      this.reportError(error);
    }
    await this.onCharacterChanged();
    await this.refreshIfOpen();
  }

  private reportError(error: unknown): void {
    const text = error instanceof ApiError ? playerErrorText(error.code, error.message) : String(error);
    this.hud.toast(text, "error");
  }
}
