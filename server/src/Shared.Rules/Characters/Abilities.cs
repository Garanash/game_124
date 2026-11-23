using System;
using System.Collections.Generic;
using System.Linq;

namespace Realmforge.Rules.Characters
{
    public enum Ability
    {
        Strength,
        Dexterity,
        Constitution,
        Intelligence,
        Wisdom,
        Charisma,
    }

    public sealed class AbilityScores
    {
        public const int AbilityCount = 6;
        public const int DefaultScore = 10;
        public const int MaxScore = 30;

        private readonly int[] _scores;

        public AbilityScores()
            : this(Enumerable.Repeat(DefaultScore, AbilityCount).ToArray())
        {
        }

        public AbilityScores(int strength, int dexterity, int constitution, int intelligence, int wisdom, int charisma)
            : this(new[] { strength, dexterity, constitution, intelligence, wisdom, charisma })
        {
        }

        private AbilityScores(int[] scores)
        {
            _scores = scores;
        }

        public int this[Ability ability] => _scores[(int)ability];

        public static int ModifierOf(int score)
        {
            return (int)Math.Floor((score - DefaultScore) / 2.0);
        }

        public int Modifier(Ability ability)
        {
            return ModifierOf(this[ability]);
        }

        public AbilityScores With(Ability ability, int score)
        {
            int[] copy = (int[])_scores.Clone();
            copy[(int)ability] = Math.Max(1, Math.Min(MaxScore, score));
            return new AbilityScores(copy);
        }

        public AbilityScores WithBonuses(IReadOnlyDictionary<Ability, int> bonuses)
        {
            AbilityScores result = this;
            foreach (KeyValuePair<Ability, int> bonus in bonuses)
            {
                result = result.With(bonus.Key, result[bonus.Key] + bonus.Value);
            }
            return result;
        }

        public int[] ToArray()
        {
            return (int[])_scores.Clone();
        }

        public static AbilityScores FromArray(IReadOnlyList<int> scores)
        {
            if (scores.Count != AbilityCount)
            {
                throw new ArgumentException("Expected " + AbilityCount + " ability scores.", nameof(scores));
            }
            return new AbilityScores(scores.ToArray());
        }
    }

    /// <summary>SRD 5.1 point-buy: 27 points, scores 8..15 before racial bonuses.</summary>
    public static class PointBuy
    {
        public const int Budget = 27;
        public const int MinScore = 8;
        public const int MaxScore = 15;

        private static readonly Dictionary<int, int> CostByScore = new Dictionary<int, int>
        {
            { 8, 0 }, { 9, 1 }, { 10, 2 }, { 11, 3 }, { 12, 4 }, { 13, 5 }, { 14, 7 }, { 15, 9 },
        };

        public static int CostOf(int score)
        {
            if (!CostByScore.TryGetValue(score, out int cost))
            {
                throw new ArgumentOutOfRangeException(nameof(score), "Point-buy scores must be between 8 and 15.");
            }
            return cost;
        }

        public static RuleResult Validate(AbilityScores scores)
        {
            foreach (Ability ability in Enum.GetValues(typeof(Ability)))
            {
                if (scores[ability] < MinScore || scores[ability] > MaxScore)
                {
                    return RuleResult.Fail("point_buy.out_of_range", ability + " must be between 8 and 15.");
                }
            }
            int spent = Spent(scores);
            return spent <= Budget
                ? RuleResult.Ok()
                : RuleResult.Fail("point_buy.over_budget", "Spent " + spent + " of " + Budget + " points.");
        }

        /// <summary>
        /// Points left from creation. Derived from the scores rather than stored, so a hero who skipped
        /// spending them keeps them, and so do heroes created before points could be spent later.
        /// </summary>
        public static int Unspent(AbilityScores scores)
        {
            return Validate(scores).Success ? Budget - Spent(scores) : 0;
        }

        public static RuleResult CanRaise(AbilityScores scores, Ability ability)
        {
            int next = scores[ability] + 1;
            if (next > MaxScore)
            {
                return RuleResult.Fail("point_buy.max", ability + " is already at the point-buy maximum.");
            }
            if (!Validate(scores).Success || CostOf(next) - CostOf(scores[ability]) > Unspent(scores))
            {
                return RuleResult.Fail("point_buy.no_points", "Not enough unspent points.");
            }
            return RuleResult.Ok();
        }

        private static int Spent(AbilityScores scores)
        {
            return Enum.GetValues(typeof(Ability)).Cast<Ability>().Sum(ability => CostOf(scores[ability]));
        }
    }
}
