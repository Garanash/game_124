using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;

namespace Realmforge.Rules.Combat
{
    public sealed class LootResult
    {
        public long Gold { get; set; }
        public List<ItemQuantity> Items { get; } = new List<ItemQuantity>();

        public void Add(string itemId, int quantity)
        {
            ItemQuantity? existing = Items.FirstOrDefault(item => item.ItemId == itemId);
            if (existing != null)
            {
                existing.Quantity += quantity;
                return;
            }
            Items.Add(new ItemQuantity(itemId, quantity));
        }

        public void Merge(LootResult other)
        {
            Gold += other.Gold;
            foreach (ItemQuantity item in other.Items)
            {
                Add(item.ItemId, item.Quantity);
            }
        }
    }

    public static class LootRoller
    {
        public static LootResult Roll(LootTable table, IRandomSource random, int yieldBonusPercent = 0)
        {
            var result = new LootResult();
            if (table.GoldMax > 0)
            {
                result.Gold = random.NextInt(table.GoldMin, table.GoldMax + 1);
            }
            int totalWeight = table.Entries.Sum(entry => entry.Weight);
            if (totalWeight <= 0)
            {
                return result;
            }
            for (int roll = 0; roll < table.Rolls; roll++)
            {
                LootEntry entry = Pick(table.Entries, random.NextInt(0, totalWeight));
                int quantity = random.NextInt(entry.Min, entry.Max + 1);
                quantity += quantity * yieldBonusPercent / 100;
                if (quantity > 0)
                {
                    result.Add(entry.ItemId, quantity);
                }
            }
            return result;
        }

        private static LootEntry Pick(List<LootEntry> entries, int ticket)
        {
            foreach (LootEntry entry in entries)
            {
                if (ticket < entry.Weight)
                {
                    return entry;
                }
                ticket -= entry.Weight;
            }
            return entries[entries.Count - 1];
        }
    }
}
