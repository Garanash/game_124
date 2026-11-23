using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Inventory;
using Xunit;

namespace Realmforge.Rules.Tests;

public class CharacterStatsTests
{
    [Fact]
    public void Summary_matches_the_combatant_built_for_encounters()
    {
        var catalog = TestCatalog.Create();
        CharacterSheet sheet = CharacterRules.Create(Guid.NewGuid(), Guid.NewGuid(), TestCatalog.DwarfRequest(), catalog, DateTime.UtcNow);
        var equipment = new Equipment();
        equipment.Slots[EquipmentSlot.MainHand] = new EquippedItem { InstanceId = Guid.NewGuid(), ItemId = "sword" };
        equipment.Slots[EquipmentSlot.LeftRing] = new EquippedItem { InstanceId = Guid.NewGuid(), ItemId = "ring" };

        CharacterStats stats = CharacterStats.Build(sheet, equipment, catalog);
        Combatant combatant = CombatantFactory.FromCharacter(0, sheet, equipment, catalog, 0);

        Assert.Equal(combatant.ArmorClass, stats.ArmorClass);
        Assert.Equal(combatant.Attack.AttackBonus, stats.AttackBonus);
        Assert.Equal("sword", stats.WeaponId);
        Assert.Equal(1, stats.Bonuses["DamageBonus"]);
        AbilityScoreSummary constitution = stats.Abilities.Single(score => score.Ability == "Constitution");
        Assert.Equal(13, constitution.Base);
        Assert.Equal(15, constitution.Total);
        Assert.Equal(2, constitution.Modifier);
    }

    [Fact]
    public void Points_left_from_creation_are_reported()
    {
        var catalog = TestCatalog.Create();
        CharacterSheet sheet = CharacterRules.Create(Guid.NewGuid(), Guid.NewGuid(), TestCatalog.DwarfRequest(), catalog, DateTime.UtcNow);
        sheet.BaseAbilities = new[] { 8, 8, 8, 8, 8, 8 };

        Assert.Equal(PointBuy.Budget, CharacterStats.Build(sheet, new Equipment(), catalog).UnspentAbilityPoints);
    }
}
