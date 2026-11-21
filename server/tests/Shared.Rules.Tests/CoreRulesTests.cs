using Realmforge.Rules.Characters;
using Realmforge.Rules.Dice;
using Xunit;

namespace Realmforge.Rules.Tests;

public class CoreRulesTests
{
    [Theory]
    [InlineData("2d6+3", 2, 6, 3)]
    [InlineData("d20", 1, 20, 0)]
    [InlineData("1d4-1", 1, 4, -1)]
    [InlineData("5", 0, 0, 5)]
    public void Parses_dice_notation(string text, int count, int sides, int modifier)
    {
        DiceExpression expression = DiceExpression.Parse(text);

        Assert.Equal(count, expression.Count);
        Assert.Equal(sides, expression.Sides);
        Assert.Equal(modifier, expression.Modifier);
    }

    [Fact]
    public void Dice_rolls_stay_within_bounds_and_are_deterministic()
    {
        var first = new SeededRandom(42);
        var second = new SeededRandom(42);
        DiceExpression expression = DiceExpression.Parse("3d6+2");

        for (int index = 0; index < 1000; index++)
        {
            int roll = expression.Roll(first);
            Assert.InRange(roll, expression.Min, expression.Max);
            Assert.Equal(roll, expression.Roll(second));
        }
    }

    [Theory]
    [InlineData(1, -5)]
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(15, 2)]
    [InlineData(20, 5)]
    public void Ability_modifier_follows_srd(int score, int expected)
    {
        Assert.Equal(expected, AbilityScores.ModifierOf(score));
    }

    [Fact]
    public void Point_buy_accepts_standard_spread_and_rejects_overspend()
    {
        Assert.True(PointBuy.Validate(new AbilityScores(15, 14, 13, 12, 10, 8)).Success);
        Assert.Equal("point_buy.over_budget", PointBuy.Validate(new AbilityScores(15, 15, 15, 15, 8, 8)).Code);
        Assert.Equal("point_buy.out_of_range", PointBuy.Validate(new AbilityScores(16, 8, 8, 8, 8, 8)).Code);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 2)]
    [InlineData(355000, 20)]
    public void Level_follows_xp_table(long xp, int level)
    {
        Assert.Equal(level, Progression.LevelForXp(xp));
    }

    [Fact]
    public void Alignment_cell_derives_from_axes_and_shifts_are_clamped()
    {
        Alignment alignment = Alignment.FromCell(AlignmentCell.LawfulGood);
        Assert.Equal(AlignmentCell.LawfulGood, alignment.Cell);

        Alignment fallen = alignment.Shift(-200, -150);
        Assert.Equal(-Alignment.Limit, fallen.LawChaos);
        Assert.Equal(AlignmentCell.ChaoticEvil, fallen.Cell);

        Assert.Equal(AlignmentCell.TrueNeutral, new Alignment(10, -20).Cell);
    }

    [Fact]
    public void Character_starts_as_wanderer_in_race_start_zone()
    {
        var catalog = TestCatalog.Create();

        CharacterSheet sheet = CharacterRules.Create(Guid.NewGuid(), Guid.NewGuid(), TestCatalog.DwarfRequest(), catalog, DateTime.UtcNow);

        Assert.False(sheet.HasClass);
        Assert.Equal("start_dwarf", sheet.ZoneId);
        Assert.Equal(10, sheet.PositionX);
        Assert.Equal(15, sheet.Abilities(catalog)[Ability.Constitution]);
        Assert.Equal(CharacterSheet.WandererHitDie + 2, sheet.CurrentHp);
    }

    [Fact]
    public void Class_requires_level_trial_and_alignment()
    {
        var catalog = TestCatalog.Create();
        CharacterSheet sheet = CharacterRules.Create(Guid.NewGuid(), Guid.NewGuid(), TestCatalog.DwarfRequest(), catalog, DateTime.UtcNow);

        Assert.Equal("class.level", CharacterRules.CanTakeClassLevel(sheet, "fighter", catalog).Code);

        sheet.Xp = Progression.XpForLevel(Progression.ClassChoiceLevel);
        Assert.Equal("class.trial", CharacterRules.CanTakeClassLevel(sheet, "fighter", catalog).Code);

        sheet.UnlockedClasses.Add("fighter");
        sheet.UnlockedClasses.Add("paladin");
        sheet.SetAlignment(Alignment.FromCell(AlignmentCell.NeutralEvil));
        Assert.Equal("class.alignment", CharacterRules.CanTakeClassLevel(sheet, "paladin", catalog).Code);

        CharacterRules.TakeClassLevel(sheet, "fighter", catalog);
        Assert.Equal(1, sheet.ClassLevels["fighter"]);
        Assert.Contains("second_wind", CharacterRules.KnownAbilities(sheet, catalog));
        Assert.Equal("class.no_levels", CharacterRules.CanTakeClassLevel(sheet, "fighter", catalog).Code);
    }
}
