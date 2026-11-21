using Realmforge.Rules.Dice;

namespace Realmforge.Rules.Characters
{
    public readonly struct CheckResult
    {
        public CheckResult(D20Result roll, int total, int difficultyClass)
        {
            Roll = roll;
            Total = total;
            DifficultyClass = difficultyClass;
        }

        public D20Result Roll { get; }
        public int Total { get; }
        public int DifficultyClass { get; }
        public bool Success => Total >= DifficultyClass;
    }

    public static class Checks
    {
        public const int BaseSpellSaveDc = 8;

        public static CheckResult AbilityCheck(IRandomSource random, int modifier, int difficultyClass, RollMode mode = RollMode.Normal)
        {
            D20Result roll = DiceRoller.RollD20(random, mode);
            return new CheckResult(roll, roll.Natural + modifier, difficultyClass);
        }

        public static CheckResult SavingThrow(
            IRandomSource random,
            AbilityScores scores,
            Ability ability,
            bool proficient,
            int level,
            int difficultyClass,
            RollMode mode = RollMode.Normal)
        {
            int modifier = scores.Modifier(ability) + (proficient ? Progression.ProficiencyBonus(level) : 0);
            return AbilityCheck(random, modifier, difficultyClass, mode);
        }

        public static int SpellSaveDc(AbilityScores scores, Ability castingAbility, int level)
        {
            return BaseSpellSaveDc + Progression.ProficiencyBonus(level) + scores.Modifier(castingAbility);
        }
    }
}
