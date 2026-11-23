using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Inventory
{
    /// <summary>Where an item can be worn; None items stay in the backpack (gems, trophies, potions).</summary>
    public enum ItemSlotKind
    {
        None,
        Head,
        Amulet,
        Chest,
        Weapon,
        TwoHanded,
        OffHand,
        Gloves,
        Belt,
        Ring,
        Boots,
    }

    public enum EquipmentSlot
    {
        Head,
        Amulet,
        Chest,
        MainHand,
        OffHand,
        Gloves,
        Belt,
        LeftRing,
        RightRing,
        Boots,
    }

    public sealed class EquippedItem
    {
        public Guid InstanceId { get; set; }
        public string ItemId { get; set; } = string.Empty;
    }

    /// <summary>Items worn on the paper doll. Only these count for stats and the attack.</summary>
    public sealed class Equipment
    {
        public Dictionary<EquipmentSlot, EquippedItem> Slots { get; set; } = new Dictionary<EquipmentSlot, EquippedItem>();

        public ItemDefinition? ItemIn(EquipmentSlot slot, ContentCatalog catalog)
        {
            return Slots.TryGetValue(slot, out EquippedItem? equipped) && catalog.Items.TryGetValue(equipped.ItemId, out ItemDefinition? item)
                ? item
                : null;
        }

        public IEnumerable<ItemDefinition> Items(ContentCatalog catalog)
        {
            return Slots.Values.Where(equipped => catalog.Items.ContainsKey(equipped.ItemId)).Select(equipped => catalog.Items[equipped.ItemId]);
        }

        public ItemDefinition? Weapon(ContentCatalog catalog)
        {
            ItemDefinition? item = ItemIn(EquipmentSlot.MainHand, catalog);
            return item != null && !string.IsNullOrEmpty(item.WeaponDamage) ? item : null;
        }
    }

    public static class EquipmentRules
    {
        private static readonly Dictionary<ItemSlotKind, EquipmentSlot[]> SlotsByKind = new Dictionary<ItemSlotKind, EquipmentSlot[]>
        {
            { ItemSlotKind.Head, new[] { EquipmentSlot.Head } },
            { ItemSlotKind.Amulet, new[] { EquipmentSlot.Amulet } },
            { ItemSlotKind.Chest, new[] { EquipmentSlot.Chest } },
            { ItemSlotKind.Weapon, new[] { EquipmentSlot.MainHand } },
            { ItemSlotKind.TwoHanded, new[] { EquipmentSlot.MainHand } },
            { ItemSlotKind.OffHand, new[] { EquipmentSlot.OffHand } },
            { ItemSlotKind.Gloves, new[] { EquipmentSlot.Gloves } },
            { ItemSlotKind.Belt, new[] { EquipmentSlot.Belt } },
            { ItemSlotKind.Ring, new[] { EquipmentSlot.LeftRing, EquipmentSlot.RightRing } },
            { ItemSlotKind.Boots, new[] { EquipmentSlot.Boots } },
        };

        public static IReadOnlyList<EquipmentSlot> SlotsFor(ItemDefinition item)
        {
            return SlotsByKind.TryGetValue(item.Slot, out EquipmentSlot[]? slots) ? slots : Array.Empty<EquipmentSlot>();
        }

        public static StatBlock Bonuses(Equipment equipment, ContentCatalog catalog)
        {
            var block = new StatBlock();
            foreach (ItemDefinition item in equipment.Items(catalog))
            {
                block.AddRange(item.Modifiers);
            }
            return block;
        }

        /// <summary>
        /// Moves a backpack item onto the doll. Whatever it pushes out (the previous item, or the off-hand
        /// when a two-handed weapon goes in) returns to the backpack; if that does not fit, nothing changes.
        /// </summary>
        public static RuleResult Equip(Backpack backpack, Equipment equipment, Guid instanceId, EquipmentSlot slot, ContentCatalog catalog)
        {
            PlacedItem? placed = backpack.Find(instanceId);
            if (placed == null)
            {
                return RuleResult.Fail("equipment.not_in_backpack", "Item is not in the backpack.");
            }
            ItemDefinition item = catalog.Items[placed.ItemId];
            if (!SlotsFor(item).Contains(slot))
            {
                return RuleResult.Fail("equipment.wrong_slot", item.Name + " cannot be worn there.");
            }
            List<EquipmentSlot> displaced = DisplacedBy(item, slot, equipment, catalog);
            List<PlacedItem> before = backpack.Items.ToList();
            backpack.Remove(instanceId);
            foreach (EquipmentSlot freed in displaced)
            {
                EquippedItem previous = equipment.Slots[freed];
                if (!backpack.TryAutoPlace(previous.InstanceId, previous.ItemId, catalog, out _))
                {
                    backpack.Items = before;
                    return RuleResult.Fail("equipment.backpack_full", "No room in the backpack for the item being replaced.");
                }
            }
            displaced.ForEach(freed => equipment.Slots.Remove(freed));
            equipment.Slots[slot] = new EquippedItem { InstanceId = placed.InstanceId, ItemId = placed.ItemId };
            return RuleResult.Ok();
        }

        public static RuleResult Unequip(Backpack backpack, Equipment equipment, EquipmentSlot slot, int x, int y, int rotation, ContentCatalog catalog)
        {
            if (!equipment.Slots.TryGetValue(slot, out EquippedItem? equipped))
            {
                return RuleResult.Fail("equipment.empty_slot", "Nothing is worn there.");
            }
            RuleResult fits = backpack.CanPlace(equipped.ItemId, x, y, rotation, catalog);
            if (!fits.Success)
            {
                return fits;
            }
            backpack.Place(equipped.InstanceId, equipped.ItemId, x, y, rotation, catalog);
            equipment.Slots.Remove(slot);
            return RuleResult.Ok();
        }

        /// <summary>Puts on every wearable backpack item that has a free slot, best first. For starter kits and old saves.</summary>
        public static void EquipBestFromBackpack(Backpack backpack, Equipment equipment, ContentCatalog catalog)
        {
            IEnumerable<PlacedItem> wearable = backpack.Items
                .Where(placed => catalog.Items.TryGetValue(placed.ItemId, out ItemDefinition? item) && item.Slot != ItemSlotKind.None)
                .OrderByDescending(placed => catalog.Items[placed.ItemId].Rarity)
                .ThenByDescending(placed => WeaponStrength(catalog.Items[placed.ItemId]))
                .ToList();
            foreach (PlacedItem placed in wearable)
            {
                ItemDefinition item = catalog.Items[placed.ItemId];
                EquipmentSlot? free = SlotsFor(item).Where(slot => !equipment.Slots.ContainsKey(slot)).Cast<EquipmentSlot?>().FirstOrDefault();
                if (free.HasValue && DisplacedBy(item, free.Value, equipment, catalog).Count == 0)
                {
                    Equip(backpack, equipment, placed.InstanceId, free.Value, catalog);
                }
            }
        }

        private static List<EquipmentSlot> DisplacedBy(ItemDefinition item, EquipmentSlot slot, Equipment equipment, ContentCatalog catalog)
        {
            var displaced = new List<EquipmentSlot>();
            if (equipment.Slots.ContainsKey(slot))
            {
                displaced.Add(slot);
            }
            if (item.Slot == ItemSlotKind.TwoHanded && equipment.Slots.ContainsKey(EquipmentSlot.OffHand))
            {
                displaced.Add(EquipmentSlot.OffHand);
            }
            if (slot == EquipmentSlot.OffHand && equipment.ItemIn(EquipmentSlot.MainHand, catalog)?.Slot == ItemSlotKind.TwoHanded)
            {
                displaced.Add(EquipmentSlot.MainHand);
            }
            return displaced;
        }

        private static int WeaponStrength(ItemDefinition item)
        {
            return string.IsNullOrEmpty(item.WeaponDamage) ? 0 : Dice.DiceExpression.Parse(item.WeaponDamage).Max;
        }
    }
}
