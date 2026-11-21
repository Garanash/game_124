using System;

namespace Realmforge.Rules.Characters
{
    public enum CasterProgression
    {
        None,
        Half,
        Full,
    }

    /// <summary>SRD 5.1 spell slot table for full casters; half casters use half their class level, rounded up.</summary>
    public static class SpellSlots
    {
        public const int MaxSpellLevel = 9;

        private static readonly int[][] FullCasterTable =
        {
            new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 },
            new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 2, 0, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 0, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 2, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 1, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 2, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 1, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 1, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 1, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 1, 1 },
            new[] { 4, 3, 3, 3, 3, 1, 1, 1, 1 },
            new[] { 4, 3, 3, 3, 3, 2, 1, 1, 1 },
            new[] { 4, 3, 3, 3, 3, 2, 2, 1, 1 },
        };

        /// <summary>Returns slots indexed by spell level (index 0 = 1st-level slots).</summary>
        public static int[] ForCasterLevel(int casterLevel)
        {
            if (casterLevel <= 0)
            {
                return new int[MaxSpellLevel];
            }
            int index = Math.Min(Progression.MaxLevel, casterLevel) - 1;
            return (int[])FullCasterTable[index].Clone();
        }

        public static int CasterLevel(CasterProgression progression, int classLevel)
        {
            switch (progression)
            {
                case CasterProgression.Full:
                    return classLevel;
                case CasterProgression.Half:
                    return classLevel < 2 ? 0 : (classLevel + 1) / 2;
                default:
                    return 0;
            }
        }
    }
}
