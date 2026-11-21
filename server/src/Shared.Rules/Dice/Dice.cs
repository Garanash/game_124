using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Realmforge.Rules.Dice
{
    public interface IRandomSource
    {
        int NextInt(int minInclusive, int maxExclusive);
    }

    /// <summary>
    /// Deterministic xorshift64* generator: the server, replays and offline-job simulation
    /// must reproduce identical rolls from the same seed on every platform.
    /// </summary>
    public sealed class SeededRandom : IRandomSource
    {
        private const ulong FallbackSeed = 0x9E3779B97F4A7C15UL;
        private const ulong Multiplier = 0x2545F4914F6CDD1DUL;
        private ulong _state;

        public SeededRandom(ulong seed)
        {
            _state = seed == 0 ? FallbackSeed : seed;
        }

        public static ulong CombineSeed(string text, long salt)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char symbol in text)
            {
                hash ^= symbol;
                hash *= 1099511628211UL;
            }
            return hash ^ (ulong)salt;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            ulong value = _state * Multiplier;
            ulong range = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(value % range);
        }
    }

    public enum RollMode
    {
        Normal,
        Advantage,
        Disadvantage,
    }

    public readonly struct D20Result
    {
        public const int CriticalHit = 20;
        public const int CriticalMiss = 1;

        public D20Result(int natural)
        {
            Natural = natural;
        }

        public int Natural { get; }
        public bool IsCriticalHit => Natural == CriticalHit;
        public bool IsCriticalMiss => Natural == CriticalMiss;
    }

    public static class DiceRoller
    {
        public const int D20 = 20;

        public static int Roll(IRandomSource random, int sides)
        {
            return random.NextInt(1, sides + 1);
        }

        public static D20Result RollD20(IRandomSource random, RollMode mode)
        {
            int first = Roll(random, D20);
            if (mode == RollMode.Normal)
            {
                return new D20Result(first);
            }
            int second = Roll(random, D20);
            int chosen = mode == RollMode.Advantage ? Math.Max(first, second) : Math.Min(first, second);
            return new D20Result(chosen);
        }

        public static RollMode Combine(bool hasAdvantage, bool hasDisadvantage)
        {
            if (hasAdvantage == hasDisadvantage)
            {
                return RollMode.Normal;
            }
            return hasAdvantage ? RollMode.Advantage : RollMode.Disadvantage;
        }
    }

    /// <summary>Dice notation such as "2d6+3", "d20", "1d4-1" or a flat "5".</summary>
    public readonly struct DiceExpression
    {
        private static readonly Regex Notation = new Regex(
            @"^\s*(?:(?<count>\d*)d(?<sides>\d+))?\s*(?<modifier>[+-]?\s*\d+)?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public DiceExpression(int count, int sides, int modifier)
        {
            Count = count;
            Sides = sides;
            Modifier = modifier;
        }

        public int Count { get; }
        public int Sides { get; }
        public int Modifier { get; }
        public int Min => Count + Modifier;
        public int Max => Count * Sides + Modifier;

        public static DiceExpression Parse(string text)
        {
            if (!TryParse(text, out DiceExpression expression))
            {
                throw new FormatException("Invalid dice notation: " + text);
            }
            return expression;
        }

        public static bool TryParse(string? text, out DiceExpression expression)
        {
            expression = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            Match match = Notation.Match(text);
            if (!match.Success || (!match.Groups["sides"].Success && !match.Groups["modifier"].Success))
            {
                return false;
            }
            int count = 0;
            int sides = 0;
            if (match.Groups["sides"].Success)
            {
                string countText = match.Groups["count"].Value;
                count = countText.Length == 0 ? 1 : int.Parse(countText, CultureInfo.InvariantCulture);
                sides = int.Parse(match.Groups["sides"].Value, CultureInfo.InvariantCulture);
                if (sides < 1)
                {
                    return false;
                }
            }
            int modifier = 0;
            if (match.Groups["modifier"].Success)
            {
                modifier = int.Parse(match.Groups["modifier"].Value.Replace(" ", string.Empty), CultureInfo.InvariantCulture);
            }
            expression = new DiceExpression(count, sides, modifier);
            return true;
        }

        public int Roll(IRandomSource random)
        {
            int total = Modifier;
            for (int index = 0; index < Count; index++)
            {
                total += DiceRoller.Roll(random, Sides);
            }
            return total;
        }

        /// <summary>Critical hits double the dice, not the flat modifier (SRD 5.1).</summary>
        public int RollCritical(IRandomSource random)
        {
            return new DiceExpression(Count * 2, Sides, Modifier).Roll(random);
        }

        public override string ToString()
        {
            string dice = Count > 0 ? Count + "d" + Sides : string.Empty;
            if (Modifier == 0)
            {
                return dice.Length == 0 ? "0" : dice;
            }
            string sign = Modifier > 0 && dice.Length > 0 ? "+" : string.Empty;
            return dice + sign + Modifier.ToString(CultureInfo.InvariantCulture);
        }
    }
}
