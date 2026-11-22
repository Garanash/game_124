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
        var backpack = new Backpack();
        backpack.Place(Guid.NewGuid(), "sword", 0, 0, 0, catalog);
        backpack.Place(Guid.NewGuid(), "whetstone", 1, 0, 0, catalog);

        CharacterStats stats = CharacterStats.Build(sheet, backpack, catalog);
        Combatant combatant = CombatantFactory.FromCharacter(0, sheet, backpack, catalog, 0);

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
    public void Touching_fusion_inputs_are_offered()
    {
        var catalog = TestCatalog.Create();
        CharacterSheet sheet = CharacterRules.Create(Guid.NewGuid(), Guid.NewGuid(), TestCatalog.DwarfRequest(), catalog, DateTime.UtcNow);
        var backpack = new Backpack();
        backpack.Place(Guid.NewGuid(), "sword", 0, 0, 0, catalog);
        Assert.Empty(CharacterStats.Build(sheet, backpack, catalog).AvailableFusions);

        backpack.Place(Guid.NewGuid(), "sword", 1, 0, 0, catalog);
        Assert.Equal(new[] { "forge_great_sword" }, CharacterStats.Build(sheet, backpack, catalog).AvailableFusions);
    }
}
