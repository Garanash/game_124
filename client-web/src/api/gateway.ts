import { ContentCatalog } from "./content";

const API_PREFIX = "/api";
const TOKEN_STORAGE_KEY = "realmforge.session";

export interface CharacterSummary { characterId: string; name: string; raceId: string; level: number; zoneId: string; classes: string[] }
export interface EnterWorldResponse { zoneId: string; host: string; udpPort: number; webSocketPort: number; ticket: string }
export interface CharacterLook {
  sex: "female" | "male"; face: number; hair: number; hairColor: number; eyeColor: number;
  build: "slender" | "athletic" | "sturdy"; heightPercent: number; outfit: string;
}
export interface CreateCharacterRequest { name: string; raceId: string; alignment: string; abilities: number[]; look?: CharacterLook }

export interface CharacterSheet {
  id: string; name: string; raceId: string; xp: number; currentHp: number; gold: number;
  classLevels: Record<string, number>; perks: string[]; zoneId: string;
  baseAbilities: number[];
  look: CharacterLook | null;
  /** Alignment axes: positive is lawful / good, negative chaotic / evil. */
  lawChaos: number; goodEvil: number;
  reputation: Record<string, number>; professionXp: Record<string, number>;
  achievements: string[]; completedQuests: string[];
}

export interface PlacedItem { instanceId: string; itemId: string; x: number; y: number; rotation: number }
export interface BackpackState { width: number; height: number; items: PlacedItem[] }

export const EQUIPMENT_SLOTS = ["Head", "Amulet", "Chest", "MainHand", "OffHand", "Gloves", "Belt", "LeftRing", "RightRing", "Boots"] as const;
export type EquipmentSlot = typeof EQUIPMENT_SLOTS[number];
export interface EquippedItem { instanceId: string; itemId: string }
export interface EquipmentState { slots: Partial<Record<EquipmentSlot, EquippedItem>> }

export interface CharacterState {
  sheet: CharacterSheet;
  backpack: BackpackState;
  equipment: EquipmentState;
  /** Unplaced items ("the bag"): stacks by item id. */
  materials: { stacks: Record<string, number> };
  activeQuests: { questId: string; stepId: string; count: number; isComplete: boolean }[];
  factionId: string;
}

export interface AbilityScoreSummary { ability: string; base: number; total: number; modifier: number }

/** Server-computed numbers, identical to what an encounter uses (CharacterStats.Build). */
export interface CharacterStats {
  level: number; proficiencyBonus: number; maxHp: number; armorClass: number; speedCells: number; initiative: number;
  attackBonus: number; weaponId: string; damage: string; damageType: string; damageBonus: number; rangeCells: number;
  spellAttackBonus: number; spellSaveDc: number; spellSlots: number[];
  abilities: AbilityScoreSummary[];
  bonuses: Record<string, number>;
  unspentAbilityPoints: number;
}

export class ApiError extends Error {
  constructor(readonly code: string, message: string, readonly status: number) {
    super(message);
  }
}

export class GatewayClient {
  private token: string | null = localStorage.getItem(TOKEN_STORAGE_KEY);

  get isSignedIn(): boolean {
    return this.token !== null;
  }

  signOut(): void {
    this.token = null;
    localStorage.removeItem(TOKEN_STORAGE_KEY);
  }

  async register(email: string, password: string): Promise<void> {
    await this.startSession("/auth/register", email, password);
  }

  async login(email: string, password: string): Promise<void> {
    await this.startSession("/auth/login", email, password);
  }

  catalog(): Promise<ContentCatalog> {
    return this.request<ContentCatalog>("GET", "/content/catalog");
  }

  listCharacters(): Promise<CharacterSummary[]> {
    return this.request<CharacterSummary[]>("GET", "/characters");
  }

  createCharacter(request: CreateCharacterRequest): Promise<CharacterSummary> {
    return this.request<CharacterSummary>("POST", "/characters", request);
  }

  character(characterId: string): Promise<CharacterState> {
    return this.request<CharacterState>("GET", `/characters/${characterId}`);
  }

  stats(characterId: string): Promise<CharacterStats> {
    return this.request<CharacterStats>("GET", `/characters/${characterId}/stats`);
  }

  moveBackpackItem(characterId: string, instanceId: string, x: number, y: number, rotation: number): Promise<unknown> {
    return this.request("POST", `/characters/${characterId}/backpack/move`, { instanceId, x, y, rotation });
  }

  placeFromBag(characterId: string, itemId: string, x: number, y: number, rotation: number): Promise<unknown> {
    return this.request("POST", `/characters/${characterId}/backpack/place`, { itemId, x, y, rotation });
  }

  stashToBag(characterId: string, instanceId: string): Promise<unknown> {
    return this.request("POST", `/characters/${characterId}/backpack/${instanceId}/stash`, {});
  }

  equip(characterId: string, instanceId: string, slot: EquipmentSlot): Promise<unknown> {
    return this.request("POST", `/characters/${characterId}/equipment/equip`, { instanceId, slot });
  }

  unequip(characterId: string, slot: EquipmentSlot, x: number, y: number, rotation: number): Promise<unknown> {
    return this.request("POST", `/characters/${characterId}/equipment/unequip`, { slot, x, y, rotation });
  }

  raiseAbility(characterId: string, ability: string): Promise<unknown> {
    return this.request("POST", `/characters/${characterId}/abilities/${ability}/raise`, {});
  }

  enterWorld(characterId: string): Promise<EnterWorldResponse> {
    return this.request<EnterWorldResponse>("POST", `/characters/${characterId}/enter`, {});
  }

  private async startSession(path: string, email: string, password: string): Promise<void> {
    const session = await this.request<{ token: string }>("POST", path, { email, password });
    this.token = session.token;
    localStorage.setItem(TOKEN_STORAGE_KEY, session.token);
  }

  private async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const headers: Record<string, string> = { "Content-Type": "application/json" };
    if (this.token) {
      headers.Authorization = `Bearer ${this.token}`;
    }
    const response = await fetch(API_PREFIX + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    if (response.status === 401) {
      this.signOut();
      throw new ApiError("auth.expired", "Сессия истекла, войдите снова.", response.status);
    }
    const text = await response.text();
    const payload: unknown = text.length > 0 ? JSON.parse(text) : null;
    if (!response.ok) {
      const error = payload as { code?: string; message?: string } | null;
      throw new ApiError(error?.code ?? "http." + response.status, error?.message ?? `Ошибка сервера (${response.status})`, response.status);
    }
    return payload as T;
  }
}
