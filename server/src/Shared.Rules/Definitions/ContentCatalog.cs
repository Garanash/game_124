using System.Collections.Generic;

namespace Realmforge.Rules.Definitions
{
    /// <summary>Immutable-after-load registry of every content definition, keyed by id.</summary>
    public sealed class ContentCatalog
    {
        public string Version { get; set; } = "0.0.0";
        public Dictionary<string, RaceDefinition> Races { get; } = new Dictionary<string, RaceDefinition>();
        public Dictionary<string, ClassDefinition> Classes { get; } = new Dictionary<string, ClassDefinition>();
        public Dictionary<string, AbilityDefinition> Abilities { get; } = new Dictionary<string, AbilityDefinition>();
        public Dictionary<string, PerkDefinition> Perks { get; } = new Dictionary<string, PerkDefinition>();
        public Dictionary<string, ItemDefinition> Items { get; } = new Dictionary<string, ItemDefinition>();
        public Dictionary<string, LootTable> LootTables { get; } = new Dictionary<string, LootTable>();
        public Dictionary<string, ContinentDefinition> Continents { get; } = new Dictionary<string, ContinentDefinition>();
        public Dictionary<string, ZoneDefinition> Zones { get; } = new Dictionary<string, ZoneDefinition>();
        public Dictionary<string, MobDefinition> Mobs { get; } = new Dictionary<string, MobDefinition>();
        public Dictionary<string, InstanceDefinition> Instances { get; } = new Dictionary<string, InstanceDefinition>();
        public Dictionary<string, NpcDefinition> Npcs { get; } = new Dictionary<string, NpcDefinition>();
        public Dictionary<string, FactionDefinition> Factions { get; } = new Dictionary<string, FactionDefinition>();
        public Dictionary<string, QuestDefinition> Quests { get; } = new Dictionary<string, QuestDefinition>();
        public Dictionary<string, ProfessionDefinition> Professions { get; } = new Dictionary<string, ProfessionDefinition>();
        public Dictionary<string, GatheringNodeDefinition> Nodes { get; } = new Dictionary<string, GatheringNodeDefinition>();
        public Dictionary<string, CraftingRecipe> Recipes { get; } = new Dictionary<string, CraftingRecipe>();
        public Dictionary<string, OfflineJobDefinition> OfflineJobs { get; } = new Dictionary<string, OfflineJobDefinition>();
        public Dictionary<string, HouseModuleDefinition> HouseModules { get; } = new Dictionary<string, HouseModuleDefinition>();
        public Dictionary<string, CosmeticDefinition> Cosmetics { get; } = new Dictionary<string, CosmeticDefinition>();
        public Dictionary<string, StoreSkuDefinition> StoreSkus { get; } = new Dictionary<string, StoreSkuDefinition>();
    }
}
