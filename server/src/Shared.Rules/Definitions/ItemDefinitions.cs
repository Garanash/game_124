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
        public Inventory.ItemSlotKind Slot { get; set; } = Inventory.ItemSlotKind.None;
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
