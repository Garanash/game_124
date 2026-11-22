const ICON_INDEX_URL = "/models/icons/index.json";

interface IconIndex { icons?: Record<string, string> }

/** Painted item icons from `npm run meshy-icons`; items without one keep their text tile. */
export class ItemIcons {
  private icons: Record<string, string> = {};
  readonly ready: Promise<void>;

  constructor() {
    this.ready = this.load();
  }

  urlFor(itemId: string): string | null {
    const path = this.icons[itemId];
    return path ? `/${path}` : null;
  }

  private async load(): Promise<void> {
    try {
      const response = await fetch(ICON_INDEX_URL);
      const index: IconIndex = response.ok ? await response.json() : {};
      this.icons = index.icons ?? {};
    } catch {
      // No icons generated yet.
    }
  }
}
