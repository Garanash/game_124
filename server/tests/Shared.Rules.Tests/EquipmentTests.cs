using Realmforge.Rules.Characters;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Stats;
using Xunit;

namespace Realmforge.Rules.Tests;

public class EquipmentTests
{
    [Fact]
    public void Only_worn_items_give_bonuses()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack();
        var equipment = new Equipment();
        PlacedItem ring = backpack.Place(Guid.NewGuid(), "ring", 0, 0, 0, catalog);
        Assert.Equal(0, EquipmentRules.Bonuses(equipment, catalog)[Stat.DamageBonus]);

        Assert.True(EquipmentRules.Equip(backpack, equipment, ring.InstanceId, EquipmentSlot.RightRing, catalog).Success);

        Assert.Equal(1, EquipmentRules.Bonuses(equipment, catalog)[Stat.DamageBonus]);
        Assert.Empty(backpack.Items);
    }

    [Fact]
    public void Items_only_go_into_matching_slots()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack();
        PlacedItem ring = backpack.Place(Guid.NewGuid(), "ring", 0, 0, 0, catalog);

        RuleResult result = EquipmentRules.Equip(backpack, new Equipment(), ring.InstanceId, EquipmentSlot.Head, catalog);

        Assert.Equal("equipment.wrong_slot", result.Code);
        Assert.Single(backpack.Items);
    }

    [Fact]
    public void Two_handed_weapon_sends_the_shield_back_to_the_backpack()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack();
        var equipment = new Equipment();
        PlacedItem shield = backpack.Place(Guid.NewGuid(), "shield", 0, 0, 0, catalog);
        PlacedItem greatSword = backpack.Place(Guid.NewGuid(), "great_sword", 5, 0, 0, catalog);
        EquipmentRules.Equip(backpack, equipment, shield.InstanceId, EquipmentSlot.OffHand, catalog);

        Assert.True(EquipmentRules.Equip(backpack, equipment, greatSword.InstanceId, EquipmentSlot.MainHand, catalog).Success);

        Assert.False(equipment.Slots.ContainsKey(EquipmentSlot.OffHand));
        Assert.Equal("great_sword", equipment.Weapon(catalog)?.Id);
        Assert.Contains(backpack.Items, item => item.InstanceId == shield.InstanceId);
    }

    [Fact]
    public void Nothing_changes_when_the_replaced_items_do_not_fit()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack { Width = 1, Height = 3 };
        var equipment = new Equipment();
        equipment.Slots[EquipmentSlot.MainHand] = new EquippedItem { InstanceId = Guid.NewGuid(), ItemId = "sword" };
        equipment.Slots[EquipmentSlot.OffHand] = new EquippedItem { InstanceId = Guid.NewGuid(), ItemId = "shield" };
        PlacedItem greatSword = backpack.Place(Guid.NewGuid(), "great_sword", 0, 0, 0, catalog);

        RuleResult result = EquipmentRules.Equip(backpack, equipment, greatSword.InstanceId, EquipmentSlot.MainHand, catalog);

        Assert.Equal("equipment.backpack_full", result.Code);
        Assert.Equal(new[] { greatSword }, backpack.Items);
        Assert.Equal("sword", equipment.Slots[EquipmentSlot.MainHand].ItemId);
        Assert.Equal("shield", equipment.Slots[EquipmentSlot.OffHand].ItemId);
    }

    [Fact]
    public void Unequipped_item_lands_where_it_was_dropped()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack();
        var equipment = new Equipment();
        equipment.Slots[EquipmentSlot.MainHand] = new EquippedItem { InstanceId = Guid.NewGuid(), ItemId = "sword" };

        Assert.True(EquipmentRules.Unequip(backpack, equipment, EquipmentSlot.MainHand, 4, 2, 0, catalog).Success);

        Assert.Empty(equipment.Slots);
        Assert.Equal((4, 2), (backpack.Items[0].X, backpack.Items[0].Y));
    }

    [Fact]
    public void Best_gear_from_an_old_backpack_is_put_on()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack();
        var equipment = new Equipment();
        backpack.Place(Guid.NewGuid(), "sword", 0, 0, 0, catalog);
        backpack.Place(Guid.NewGuid(), "great_sword", 2, 0, 0, catalog);
        backpack.Place(Guid.NewGuid(), "ring", 4, 0, 0, catalog);
        backpack.Place(Guid.NewGuid(), "whetstone", 5, 0, 0, catalog);

        EquipmentRules.EquipBestFromBackpack(backpack, equipment, catalog);

        Assert.Equal("great_sword", equipment.Weapon(catalog)?.Id);
        Assert.Equal("ring", equipment.Slots[EquipmentSlot.LeftRing].ItemId);
        Assert.Equal(new[] { "sword", "whetstone" }, backpack.Items.Select(item => item.ItemId).OrderBy(id => id));
    }
}

public class PointBuyTests
{
    [Fact]
    public void Unspent_points_can_be_spent_later()
    {
        var sheet = new CharacterSheet { BaseAbilities = new[] { 8, 8, 8, 8, 8, 8 } };

        Assert.True(CharacterRules.RaiseAbility(sheet, Ability.Strength).Success);

        Assert.Equal(9, sheet.BaseAbilities[(int)Ability.Strength]);
        Assert.Equal(PointBuy.Budget - 1, PointBuy.Unspent(AbilityScores.FromArray(sheet.BaseAbilities)));
    }

    [Fact]
    public void Raising_stops_at_the_budget_and_the_cap()
    {
        var capped = new CharacterSheet { BaseAbilities = new[] { 15, 8, 8, 8, 8, 8 } };
        var spent = new CharacterSheet { BaseAbilities = new[] { 15, 15, 15, 8, 8, 8 } };

        Assert.Equal("point_buy.max", CharacterRules.RaiseAbility(capped, Ability.Strength).Code);
        Assert.Equal("point_buy.no_points", CharacterRules.RaiseAbility(spent, Ability.Wisdom).Code);
        Assert.Equal(8, spent.BaseAbilities[(int)Ability.Wisdom]);
    }
}
