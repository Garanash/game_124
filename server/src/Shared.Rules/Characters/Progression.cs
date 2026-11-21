using System;

namespace Realmforge.Rules.Characters
{
    public static class Progression
    {
        public const int MaxLevel = 20;
        public const int ClassChoiceLevel = 3;
        public const int PerkLevelInterval = 4;
        public const int MulticlassAbilityMinimum = 13;
        private const int BaseProficiencyBonus = 2;
        private const int LevelsPerProficiencyStep = 4;

        private static readonly int[] XpThresholds =
        {
            0, 300, 900, 2700, 6500, 14000, 23000, 34000, 48000, 64000,
            85000, 100000, 120000, 140000, 165000, 195000, 225000, 265000, 305000, 355000,
        };

        public static int LevelForXp(long xp)
        {
            int level = 1;
            for (int index = 1; index < XpThresholds.Length; index++)
            {
                if (xp >= XpThresholds[index])
                {
                    level = index + 1;
                }
            }
            return level;
        }

        public static int XpForLevel(int level)
        {
            int clamped = Math.Max(1, Math.Min(MaxLevel, level));
            return XpThresholds[clamped - 1];
        }

        public static int ProficiencyBonus(int level)
        {
            return BaseProficiencyBonus + (Math.Max(1, level) - 1) / LevelsPerProficiencyStep;
        }

        public static bool GrantsPerk(int level)
        {
            return level > 0 && level % PerkLevelInterval == 0;
        }

        public static int PerkSlotsAtLevel(int level)
        {
            return Math.Max(0, level) / PerkLevelInterval;
        }
    }
}
