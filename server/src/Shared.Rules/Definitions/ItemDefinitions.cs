using System.Collections.Generic;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Definitions
{
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
    }

    public sealed class CellOffset
    {
        public CellOffset()
        {
        }

        public CellOffset(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class ItemDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public ItemRarity Rarity { get; set; } = ItemRarity.Common;
        public List<string> Tags { get; set; } = new List<string>();
        /// <summary>Cells occupied in the backpack relative to the item origin; empty means a single cell.</summary>
        public List<CellOffset> Shape { get; set; } = new List<CellOffset>();
        public List<StatModifier> Modifiers { get; set; } = new List<StatModifier>();
        public string WeaponDamage { get; set; } = string.Empty;
        public DamageType WeaponDamageType { get; set; } = DamageType.Bludgeoning;
        public int MaxStack { get; set; } = 1;
        public int Value { get; set; }
    }

    /// <summary>Bonus applied to items tagged <see cref="SourceTag"/> for each orthogonally adjacent item tagged <see cref="NeighborTag"/>.</summary>
    public sealed class SynergyDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string SourceTag { get; set; } = string.Empty;
        public string NeighborTag { get; set; } = string.Empty;
        public List<StatModifier> Bonus { get; set; } = new List<StatModifier>();
    }

    /// <summary>Backpack fusion: when all inputs touch each other in the grid, they can be merged into the output.</summary>
    public sealed class FusionRecipe
    {
        public string Id { get; set; } = string.Empty;
        public List<string> Inputs { get; set; } = new List<string>();
        public string Output { get; set; } = string.Empty;
    }

    public sealed class LootEntry
    {
        public string ItemId { get; set; } = string.Empty;
        public int Weight { get; set; } = 1;
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 1;
    }

    public sealed class LootTable
    {
        public string Id { get; set; } = string.Empty;
        public int Rolls { get; set; } = 1;
        public int GoldMin { get; set; }
        public int GoldMax { get; set; }
        public List<LootEntry> Entries { get; set; } = new List<LootEntry>();
    }

    public sealed class ItemQuantity
    {
        public ItemQuantity()
        {
        }

        public ItemQuantity(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public string ItemId { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
    }
}
