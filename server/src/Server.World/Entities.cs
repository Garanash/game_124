using Realmforge.Protocol;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Inventory;

namespace Realmforge.Server.World;

public abstract class WorldEntity
{
    protected WorldEntity(long id, EntityKind kind, string name, string definitionId, float x, float z)
    {
        Id = id;
        Kind = kind;
        Name = name;
        DefinitionId = definitionId;
        X = x;
        Z = z;
        TargetX = x;
        TargetZ = z;
    }

    public long Id { get; }
    public EntityKind Kind { get; }
    public string Name { get; }
    public string DefinitionId { get; }
    public float X { get; set; }
    public float Z { get; set; }
    public float TargetX { get; set; }
    public float TargetZ { get; set; }
    public float Heading { get; set; }
    public float SpeedMetersPerSecond { get; set; }
    public Guid? EncounterId { get; set; }
    public bool IsDead { get; set; }

    public bool InCombat => EncounterId.HasValue;

    public float DistanceTo(float x, float z) => MathF.Sqrt((X - x) * (X - x) + (Z - z) * (Z - z));

    public float DistanceTo(WorldEntity other) => DistanceTo(other.X, other.Z);

    public virtual byte Flags =>
        (byte)((InCombat ? EntityStateFlags.InCombat : 0) | (IsDead ? EntityStateFlags.Dead : 0));
}

public sealed class PlayerEntity : WorldEntity
{
    public PlayerEntity(long id, CharacterSheet sheet, Backpack backpack, float x, float z)
        : base(id, EntityKind.Player, sheet.Name, sheet.RaceId, x, z)
    {
        Sheet = sheet;
        Backpack = backpack;
    }

    public CharacterSheet Sheet { get; set; }
    public Backpack Backpack { get; set; }
    public Guid CharacterId => Sheet.Id;
    public int MaxHp { get; set; }
    public Guid? PartyId { get; set; }
    public Guid? GuildId { get; set; }
    public string FactionId { get; set; } = string.Empty;
    public string[] Cosmetics { get; set; } = Array.Empty<string>();
    public bool IsConnected { get; set; } = true;
    public long? GatheringNodeEntityId { get; set; }
    public double GatheringCompletesAt { get; set; }
    public HashSet<long> KnownEntities { get; } = new();

    public override byte Flags => (byte)(base.Flags
        | (GatheringNodeEntityId.HasValue ? EntityStateFlags.Gathering : 0)
        | (IsConnected ? 0 : EntityStateFlags.Offline));
}

public sealed class MobEntity : WorldEntity
{
    public MobEntity(long id, MobDefinition definition, float spawnX, float spawnZ, int respawnSeconds)
        : base(id, EntityKind.Mob, definition.Name, definition.Id, spawnX, spawnZ)
    {
        Definition = definition;
        SpawnX = spawnX;
        SpawnZ = spawnZ;
        RespawnSeconds = respawnSeconds;
    }

    public MobDefinition Definition { get; }
    public float SpawnX { get; }
    public float SpawnZ { get; }
    public int RespawnSeconds { get; }
    public double RespawnAt { get; set; }
}

public sealed class NpcEntity : WorldEntity
{
    public NpcEntity(long id, NpcDefinition definition, float x, float z)
        : base(id, EntityKind.Npc, definition.Name, definition.Id, x, z)
    {
        Definition = definition;
    }

    public NpcDefinition Definition { get; }
    public string Activity { get; set; } = string.Empty;
}

public sealed class NodeEntity : WorldEntity
{
    public NodeEntity(long id, GatheringNodeDefinition definition, float x, float z)
        : base(id, EntityKind.GatheringNode, definition.Name, definition.Id, x, z)
    {
        Definition = definition;
    }

    public GatheringNodeDefinition Definition { get; }
    public double AvailableAt { get; set; }

    public bool IsAvailable(double now) => now >= AvailableAt;
}

public sealed class PortalEntity : WorldEntity
{
    public PortalEntity(long id, PortalDefinition definition, string name)
        : base(id, EntityKind.Portal, name, definition.ToZoneId, definition.Position.X, definition.Position.Z)
    {
        Definition = definition;
    }

    public PortalDefinition Definition { get; }
}
