using System;
using MemoryPack;

namespace Realmforge.Protocol
{
    [MemoryPackable]
    [MemoryPackUnion(0, typeof(WelcomeMessage))]
    [MemoryPackUnion(1, typeof(EntitySpawnMessage))]
    [MemoryPackUnion(2, typeof(EntityDespawnMessage))]
    [MemoryPackUnion(3, typeof(WorldSnapshotMessage))]
    [MemoryPackUnion(4, typeof(EncounterStartedMessage))]
    [MemoryPackUnion(5, typeof(CombatEventsMessage))]
    [MemoryPackUnion(6, typeof(EncounterEndedMessage))]
    [MemoryPackUnion(7, typeof(ChatMessage))]
    [MemoryPackUnion(8, typeof(ErrorMessage))]
    [MemoryPackUnion(9, typeof(PongMessage))]
    [MemoryPackUnion(10, typeof(QuestUpdateMessage))]
    [MemoryPackUnion(11, typeof(ZoneTransferMessage))]
    [MemoryPackUnion(12, typeof(DialogueMessage))]
    [MemoryPackUnion(13, typeof(InventoryChangedMessage))]
    public partial interface IServerMessage
    {
    }

    public enum EntityKind : byte
    {
        Player = 0,
        Npc = 1,
        Mob = 2,
        GatheringNode = 3,
        Portal = 4,
    }

    [MemoryPackable]
    public partial class WelcomeMessage : IServerMessage
    {
        public long EntityId { get; set; }
        public string ZoneId { get; set; } = string.Empty;
        public string SceneAddress { get; set; } = string.Empty;
        public string ContentVersion { get; set; } = string.Empty;
        public double ServerTime { get; set; }
    }

    [MemoryPackable]
    public partial class EntitySpawnMessage : IServerMessage
    {
        public long EntityId { get; set; }
        public EntityKind Kind { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Race id for players, mob/npc/node definition id otherwise; picks the prefab on the client.</summary>
        public string DefinitionId { get; set; } = string.Empty;
        public string[] Cosmetics { get; set; } = Array.Empty<string>();
        public float X { get; set; }
        public float Z { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Level { get; set; }
    }

    [MemoryPackable]
    public partial class EntityDespawnMessage : IServerMessage
    {
        public long EntityId { get; set; }
    }

    [MemoryPackable]
    public partial struct EntityState
    {
        public long EntityId { get; set; }
        public float X { get; set; }
        public float Z { get; set; }
        public float Heading { get; set; }
        public byte Flags { get; set; }
    }

    public static class EntityStateFlags
    {
        public const byte InCombat = 1;
        public const byte Dead = 2;
        public const byte Gathering = 4;
        public const byte Offline = 8;
    }

    [MemoryPackable]
    public partial class WorldSnapshotMessage : IServerMessage
    {
        public long Tick { get; set; }
        public double ServerTime { get; set; }
        public EntityState[] Entities { get; set; } = Array.Empty<EntityState>();
    }

    [MemoryPackable]
    public partial struct CombatantDto
    {
        public long EntityId { get; set; }
        public int Team { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public int Initiative { get; set; }
    }

    [MemoryPackable]
    public partial class EncounterStartedMessage : IServerMessage
    {
        public Guid EncounterId { get; set; }
        public float OriginX { get; set; }
        public float OriginZ { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public CombatantDto[] Combatants { get; set; } = Array.Empty<CombatantDto>();
        public long[] TurnOrder { get; set; } = Array.Empty<long>();
    }

    [MemoryPackable]
    public partial struct CombatEventDto
    {
        public byte Type { get; set; }
        public long ActorId { get; set; }
        public long TargetId { get; set; }
        public int Value { get; set; }
        public int Roll { get; set; }
        public bool Success { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public string AbilityId { get; set; }
        public byte Condition { get; set; }
        public byte DamageType { get; set; }
    }

    [MemoryPackable]
    public partial class CombatEventsMessage : IServerMessage
    {
        public Guid EncounterId { get; set; }
        public long CurrentTurnEntityId { get; set; }
        public float TurnTimeRemaining { get; set; }
        public int Round { get; set; }
        public CombatEventDto[] Events { get; set; } = Array.Empty<CombatEventDto>();
    }

    [MemoryPackable]
    public partial struct ItemStackDto
    {
        public string ItemId { get; set; }
        public int Quantity { get; set; }
    }

    [MemoryPackable]
    public partial class EncounterEndedMessage : IServerMessage
    {
        public Guid EncounterId { get; set; }
        public bool Victory { get; set; }
        public long Xp { get; set; }
        public long Gold { get; set; }
        public ItemStackDto[] Items { get; set; } = Array.Empty<ItemStackDto>();
    }

    [MemoryPackable]
    public partial class ChatMessage : IServerMessage
    {
        public string Channel { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public double ServerTime { get; set; }
    }

    [MemoryPackable]
    public partial class ErrorMessage : IServerMessage
    {
        public string Code { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class PongMessage : IServerMessage
    {
        public double ClientTime { get; set; }
        public double ServerTime { get; set; }
    }

    [MemoryPackable]
    public partial class QuestUpdateMessage : IServerMessage
    {
        public string QuestId { get; set; } = string.Empty;
        public string StepId { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool Completed { get; set; }
    }

    [MemoryPackable]
    public partial class ZoneTransferMessage : IServerMessage
    {
        public string ZoneId { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Ticket { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial struct DialogueOptionDto
    {
        public string Id { get; set; }
        public string Text { get; set; }
    }

    [MemoryPackable]
    public partial class DialogueMessage : IServerMessage
    {
        public long NpcEntityId { get; set; }
        public string NpcName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DialogueOptionDto[] Options { get; set; } = Array.Empty<DialogueOptionDto>();
    }

    [MemoryPackable]
    public partial class InventoryChangedMessage : IServerMessage
    {
        public ItemStackDto[] Added { get; set; } = Array.Empty<ItemStackDto>();
        public long Gold { get; set; }
    }
}
