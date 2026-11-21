using System;
using System.Collections.Generic;

namespace Realmforge.Rules.Characters
{
    public enum LawAxis
    {
        Lawful,
        Neutral,
        Chaotic,
    }

    public enum MoralAxis
    {
        Good,
        Neutral,
        Evil,
    }

    public enum AlignmentCell
    {
        LawfulGood,
        NeutralGood,
        ChaoticGood,
        LawfulNeutral,
        TrueNeutral,
        ChaoticNeutral,
        LawfulEvil,
        NeutralEvil,
        ChaoticEvil,
    }

    /// <summary>
    /// Two continuous axes in [-100, 100]: positive LawChaos is lawful, positive GoodEvil is good.
    /// Actions shift the axes gradually, so the 3x3 D&amp;D cell is derived rather than stored.
    /// </summary>
    public readonly struct Alignment : IEquatable<Alignment>
    {
        public const int Limit = 100;
        public const int CellThreshold = 33;
        public const int StartingMagnitude = 60;

        public Alignment(int lawChaos, int goodEvil)
        {
            LawChaos = Clamp(lawChaos);
            GoodEvil = Clamp(goodEvil);
        }

        public int LawChaos { get; }
        public int GoodEvil { get; }

        public LawAxis Law => LawChaos >= CellThreshold ? LawAxis.Lawful
            : LawChaos <= -CellThreshold ? LawAxis.Chaotic
            : LawAxis.Neutral;

        public MoralAxis Moral => GoodEvil >= CellThreshold ? MoralAxis.Good
            : GoodEvil <= -CellThreshold ? MoralAxis.Evil
            : MoralAxis.Neutral;

        public AlignmentCell Cell => (AlignmentCell)((int)Moral * 3 + (int)Law);

        public static Alignment FromCell(AlignmentCell cell)
        {
            int law = (int)cell % 3;
            int moral = (int)cell / 3;
            return new Alignment(AxisValue(law), AxisValue(moral));
        }

        public Alignment Shift(int lawChaosDelta, int goodEvilDelta)
        {
            return new Alignment(LawChaos + lawChaosDelta, GoodEvil + goodEvilDelta);
        }

        public bool Equals(Alignment other)
        {
            return LawChaos == other.LawChaos && GoodEvil == other.GoodEvil;
        }

        public override bool Equals(object? obj)
        {
            return obj is Alignment other && Equals(other);
        }

        public override int GetHashCode()
        {
            return LawChaos * 397 ^ GoodEvil;
        }

        public override string ToString()
        {
            return Cell + " (" + LawChaos + ", " + GoodEvil + ")";
        }

        private static int AxisValue(int axisIndex)
        {
            return axisIndex == 0 ? StartingMagnitude : axisIndex == 2 ? -StartingMagnitude : 0;
        }

        private static int Clamp(int value)
        {
            return Math.Max(-Limit, Math.Min(Limit, value));
        }
    }

    public sealed class AlignmentShift
    {
        public int Law { get; set; }
        public int Good { get; set; }
    }

    /// <summary>Empty lists mean "any"; used by classes (e.g. paladin), quests and factions.</summary>
    public sealed class AlignmentRequirement
    {
        public List<LawAxis> Law { get; set; } = new List<LawAxis>();
        public List<MoralAxis> Moral { get; set; } = new List<MoralAxis>();

        public bool IsSatisfiedBy(Alignment alignment)
        {
            bool lawOk = Law.Count == 0 || Law.Contains(alignment.Law);
            bool moralOk = Moral.Count == 0 || Moral.Contains(alignment.Moral);
            return lawOk && moralOk;
        }
    }
}
