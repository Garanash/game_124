using System.Collections.Generic;
using Realmforge.Rules.Characters;

namespace Realmforge.Rules.Definitions
{
    public enum ProfessionKind
    {
        Gathering,
        Crafting,
    }

    public sealed class ProfessionDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public ProfessionKind Kind { get; set; }
        public Ability KeyAbility { get; set; } = Ability.Wisdom;
    }

    public sealed class GatheringNodeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ProfessionId { get; set; } = string.Empty;
        public int MinSkill { get; set; } = 1;
        public string LootTableId { get; set; } = string.Empty;
        public int GatherSeconds { get; set; } = 5;
        public int RespawnSeconds { get; set; } = 120;
        public int Xp { get; set; } = 10;
    }

    public sealed class CraftingRecipe
    {
        public string Id { get; set; } = string.Empty;
        public string ProfessionId { get; set; } = string.Empty;
        public int MinSkill { get; set; } = 1;
        public List<ItemQuantity> Inputs { get; set; } = new List<ItemQuantity>();
        public ItemQuantity Output { get; set; } = new ItemQuantity();
        public int CraftSeconds { get; set; } = 10;
        public int Xp { get; set; } = 15;
    }

    public enum OfflineJobKind
    {
        Gather,
        Hunt,
        Craft,
        CityWork,
        Trade,
    }

    public sealed class OfflineJobDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public OfflineJobKind Kind { get; set; }
        public string ProfessionId { get; set; } = string.Empty;
        /// <summary>Gathering node or hunted mob id for Gather/Hunt; recipe id for Craft.</summary>
        public string TargetId { get; set; } = string.Empty;
        public int CycleMinutes { get; set; } = 10;
        public int GoldPerCycle { get; set; }
        public int XpPerCycle { get; set; } = 5;
    }

    public enum CosmeticSlot
    {
        Body,
        Head,
        Weapon,
        Cloak,
        Mount,
        Pet,
        Emote,
        HouseDecor,
    }

    public sealed class CosmeticDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public CosmeticSlot Slot { get; set; }
        /// <summary>Empty means the cosmetic fits every race model.</summary>
        public List<string> RaceIds { get; set; } = new List<string>();
        public string AddressableKey { get; set; } = string.Empty;
    }

    public enum SkuKind
    {
        Cosmetic,
        Bundle,
        Convenience,
        PremiumCurrency,
    }

    public sealed class StoreSkuDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public SkuKind Kind { get; set; }
        public int PriceCents { get; set; }
        public int PriceCrystals { get; set; }
        public List<string> Grants { get; set; } = new List<string>();
        public int CrystalAmount { get; set; }
        /// <summary>Product ids per platform: apple, google, steam, xsolla.</summary>
        public Dictionary<string, string> PlatformProducts { get; set; } = new Dictionary<string, string>();
    }
}
