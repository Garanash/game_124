using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Inventory
{
    /// <summary>Stackable storage for materials: bags, bank and house chest. Shape-less, quantity only.</summary>
    public sealed class StackInventory
    {
        public Dictionary<string, int> Stacks { get; set; } = new Dictionary<string, int>();

        public int Count(string itemId)
        {
            return Stacks.TryGetValue(itemId, out int quantity) ? quantity : 0;
        }

        public void Add(string itemId, int quantity)
        {
            if (quantity <= 0)
            {
                return;
            }
            Stacks[itemId] = Count(itemId) + quantity;
        }

        public void AddRange(IEnumerable<ItemQuantity> items)
        {
            foreach (ItemQuantity item in items)
            {
                Add(item.ItemId, item.Quantity);
            }
        }

        public bool HasAll(IEnumerable<ItemQuantity> items)
        {
            return items.GroupBy(item => item.ItemId).All(group => Count(group.Key) >= group.Sum(item => item.Quantity));
        }

        public bool TryRemove(string itemId, int quantity)
        {
            int current = Count(itemId);
            if (quantity <= 0 || current < quantity)
            {
                return false;
            }
            if (current == quantity)
            {
                Stacks.Remove(itemId);
            }
            else
            {
                Stacks[itemId] = current - quantity;
            }
            return true;
        }

        public bool TryRemoveAll(IReadOnlyCollection<ItemQuantity> items)
        {
            if (!HasAll(items))
            {
                return false;
            }
            foreach (ItemQuantity item in items)
            {
                TryRemove(item.ItemId, item.Quantity);
            }
            return true;
        }
    }
}
