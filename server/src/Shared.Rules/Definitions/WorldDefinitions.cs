using System.Collections.Generic;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Definitions
{
    public enum ZoneKind
    {
        StartZone,
        City,
        Wilderness,
        PvpZone,
        Instance,
        Housing,
    }

    public enum PvpRule
    {
        None,
        FactionOnly,
        FreeForAll,
        Arena,
    }

    public sealed class WorldPoint
    {
        public WorldPoint()
        {
        }

        public WorldPoint(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float X { get; set; }
        public float Z { get; set; }
    }

    public sealed class ContinentDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ContentVersion { get; set; } = "1.0.0";
    }

    public sealed class PortalDefinition
    {
        public string ToZoneId { get; set; } = string.Empty;
        public WorldPoint Position { get; set; } = new WorldPoint();
        public WorldPoint Arrival { get; set; } = new WorldPoint();
    }

    public sealed class MobSpawnDefinition
    {
        public string MobId { get; set; } = string.Empty;
        public WorldPoint Position { get; set; } = new WorldPoint();
        public int Count { get; set; } = 1;
        public int RespawnSeconds { get; set; } = 60;
    }

    public sealed class NodeSpawnDefinition
    {
        public string NodeId { get; set; } = string.Empty;
        public WorldPoint Position { get; set; } = new WorldPoint();
    }

    /// <summary>A town inside a large zone; its continent decides the look of the land around it.</summary>
    public sealed class SettlementDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ContinentId { get; set; } = string.Empty;
        public WorldPoint Center { get; set; } = new WorldPoint();
    }

    public sealed class ZoneDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ContinentId { get; set; } = string.Empty;
        public ZoneKind Kind { get; set; } = ZoneKind.Wilderness;
        public int MinLevel { get; set; } = 1;
        public int MaxLevel { get; set; } = 20;
        public PvpRule Pvp { get; set; } = PvpRule.None;
        /// <summary>Addressables key of the Unity scene; new zones ship as separate bundles.</summary>
        public string SceneAddress { get; set; } = string.Empty;
        public float SizeMeters { get; set; } = 512;
        public WorldPoint Spawn { get; set; } = new WorldPoint();
        /// <summary>Towns of an open-world zone; empty for classic zones whose single town sits at the spawn.</summary>
        public List<SettlementDefinition> Settlements { get; set; } = new List<SettlementDefinition>();
        public List<PortalDefinition> Portals { get; set; } = new List<PortalDefinition>();
        public List<MobSpawnDefinition> Mobs { get; set; } = new List<MobSpawnDefinition>();
        public List<NodeSpawnDefinition> Nodes { get; set; } = new List<NodeSpawnDefinition>();
        public string ContentVersion { get; set; } = "1.0.0";

        public SettlementDefinition? FindSettlement(string settlementId)
        {
            foreach (SettlementDefinition settlement in Settlements)
            {
                if (settlement.Id == settlementId)
                {
                    return settlement;
                }
            }
            return null;
        }

        /// <summary>Fallen heroes return to the closest town, or to the spawn of a zone without towns.</summary>
        public WorldPoint RespawnPointNear(float x, float z)
        {
            WorldPoint best = Spawn;
            float bestDistance = float.MaxValue;
            foreach (SettlementDefinition settlement in Settlements)
            {
                float dx = settlement.Center.X - x;
                float dz = settlement.Center.Z - z;
                float distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = settlement.Center;
                }
            }
            return best;
        }
    }

    public sealed class MobDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; } = 1;
        public int Hp { get; set; } = 10;
        public int ArmorClass { get; set; } = 12;
        public int AttackBonus { get; set; } = 3;
        public string Damage { get; set; } = "1d6+1";
        public DamageType DamageType { get; set; } = DamageType.Slashing;
        public int SpeedCells { get; set; } = 6;
        public int Dexterity { get; set; } = 12;
        public float AggroRadius { get; set; } = 8;
        public int Xp { get; set; } = 50;
        public string FactionId { get; set; } = string.Empty;
        public string LootTableId { get; set; } = string.Empty;
        public List<string> Abilities { get; set; } = new List<string>();
        /// <summary>Hunting targets are passive animals that drop profession materials.</summary>
        public bool IsGame { get; set; }
    }

    public sealed class InstanceDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public List<int> PartySizes { get; set; } = new List<int>();
        public int MinLevel { get; set; } = 1;
        public string FinalLootTableId { get; set; } = string.Empty;
    }

    public enum NpcRole
    {
        Citizen,
        Vendor,
        Mentor,
        QuestGiver,
        Guard,
        Trainer,
        Auctioneer,
    }

    public sealed class ScheduleEntry
    {
        public int FromHour { get; set; }
        public int ToHour { get; set; }
        public string Activity { get; set; } = string.Empty;
        public WorldPoint Position { get; set; } = new WorldPoint();
    }

    public sealed class NpcDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public string FactionId { get; set; } = string.Empty;
        public NpcRole Role { get; set; } = NpcRole.Citizen;
        public string RaceId { get; set; } = string.Empty;
        public List<string> Quests { get; set; } = new List<string>();
        public List<string> SellsItems { get; set; } = new List<string>();
        public List<ScheduleEntry> Schedule { get; set; } = new List<ScheduleEntry>();
    }

    public sealed class FactionDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>NPCs of the faction warm up to characters matching this alignment.</summary>
        public AlignmentRequirement Affinity { get; set; } = new AlignmentRequirement();
        public List<string> Enemies { get; set; } = new List<string>();
    }

    public sealed class HouseModuleDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; } = 1;
        public int Depth { get; set; } = 1;
        public List<ItemQuantity> Cost { get; set; } = new List<ItemQuantity>();
        public List<StatModifier> Bonuses { get; set; } = new List<StatModifier>();
        public bool IsFoundation { get; set; }
    }
}
