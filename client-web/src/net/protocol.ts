// Mirrors server/src/Shared.Protocol. Over WebSocket with ?format=json every frame is
// { "type": "<C# class name>", "data": { ...camelCase fields } }.

export const PROTOCOL_VERSION = 1;

export enum EntityKind {
  Player = 0,
  Npc = 1,
  Mob = 2,
  GatheringNode = 3,
  Portal = 4,
}

export const EntityFlags = {
  InCombat: 1,
  Dead: 2,
  Gathering: 4,
  Offline: 8,
} as const;

export enum CombatCommandType {
  Move = 0,
  Attack = 1,
  UseAbility = 2,
  Dodge = 3,
  EndTurn = 4,
}

export enum CombatEventType {
  Joined = 0,
  RoundStarted = 1,
  TurnStarted = 2,
  Moved = 3,
  AttackRolled = 4,
  SaveRolled = 5,
  Damaged = 6,
  Healed = 7,
  ConditionApplied = 8,
  ConditionExpired = 9,
  AbilityUsed = 10,
  Summoned = 11,
  Dodged = 12,
  Died = 13,
  TurnEnded = 14,
  EncounterEnded = 15,
}

export const CONDITION_NAMES = [
  "Сбит с ног", "Отравлен", "Оглушён", "Опутан", "Благословлён", "Под щитом", "Горит", "Испуган", "Невидим",
];

export interface ItemStack { itemId: string; quantity: number }
export interface EntityState { entityId: number; x: number; z: number; heading: number; flags: number }
export interface CombatantDto { entityId: number; team: number; hp: number; maxHp: number; cellX: number; cellY: number; initiative: number }
export interface CombatEventDto {
  type: CombatEventType; actorId: number; targetId: number; value: number; roll: number; success: boolean;
  cellX: number; cellY: number; abilityId: string | null; condition: number; damageType: number;
}
export interface DialogueOptionDto { id: string; text: string }

export interface ServerMessages {
  WelcomeMessage: { entityId: number; zoneId: string; sceneAddress: string; contentVersion: string; serverTime: number };
  EntitySpawnMessage: {
    entityId: number; kind: EntityKind; name: string; definitionId: string; cosmetics: string[];
    x: number; z: number; hp: number; maxHp: number; level: number;
  };
  EntityDespawnMessage: { entityId: number };
  WorldSnapshotMessage: { tick: number; serverTime: number; entities: EntityState[] };
  EncounterStartedMessage: {
    encounterId: string; originX: number; originZ: number; width: number; height: number;
    combatants: CombatantDto[]; turnOrder: number[];
  };
  CombatEventsMessage: { encounterId: string; currentTurnEntityId: number; turnTimeRemaining: number; round: number; events: CombatEventDto[] };
  EncounterEndedMessage: { encounterId: string; victory: boolean; xp: number; gold: number; items: ItemStack[] };
  ChatMessage: { channel: string; senderName: string; text: string; serverTime: number };
  ErrorMessage: { code: string; text: string };
  PongMessage: { clientTime: number; serverTime: number };
  QuestUpdateMessage: { questId: string; stepId: string; count: number; completed: boolean };
  ZoneTransferMessage: { zoneId: string; host: string; port: number; ticket: string; webSocketPort: number };
  DialogueMessage: { npcEntityId: number; npcName: string; text: string; options: DialogueOptionDto[] };
  InventoryChangedMessage: { added: ItemStack[]; gold: number };
}

export interface ClientMessages {
  HelloRequest: { ticket: string; protocolVersion: number };
  MoveToRequest: { x: number; z: number };
  CombatCommandRequest: { commandType: CombatCommandType; cellX: number; cellY: number; targetId: number; abilityId: string };
  ChatSendRequest: { channel: string; text: string };
  InteractRequest: { targetEntityId: number; optionId: string };
  QuestChoiceRequest: { questId: string; choiceId: string };
  JoinEncounterRequest: { encounterId: string };
  UsePortalRequest: { portalIndex: number };
  PingRequest: { clientTime: number };
}

export type ServerMessageType = keyof ServerMessages;
export type ClientMessageType = keyof ClientMessages;

export interface Envelope<K extends string, T> { type: K; data: T }

export type AnyServerEnvelope = { [K in ServerMessageType]: Envelope<K, ServerMessages[K]> }[ServerMessageType];

export function encodeClient<K extends ClientMessageType>(type: K, data: ClientMessages[K]): string {
  return JSON.stringify({ type, data });
}

export function decodeServer(frame: string): AnyServerEnvelope {
  const parsed = JSON.parse(frame) as { type?: unknown; data?: unknown };
  if (typeof parsed.type !== "string" || typeof parsed.data !== "object" || parsed.data === null) {
    throw new Error("Malformed server frame");
  }
  return parsed as AnyServerEnvelope;
}
