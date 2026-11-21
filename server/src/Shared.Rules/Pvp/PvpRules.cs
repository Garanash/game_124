using System;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Pvp
{
    public sealed class PvpParticipant
    {
        public Guid CharacterId { get; set; }
        public string FactionId { get; set; } = string.Empty;
        public Guid? PartyId { get; set; }
        public Guid? GuildId { get; set; }
        public Alignment Alignment { get; set; }
        public int Level { get; set; }
    }

    public sealed class PvpKillConsequence
    {
        public int KillerGoodShift { get; set; }
        public int KillerLawShift { get; set; }
        public int VictimGoldLossPercent { get; set; }
    }

    public static class PvpRules
    {
        /// <summary>Attacking far weaker players in free-for-all zones is allowed but marks the killer as evil.</summary>
        public const int GankLevelGap = 5;
        private const int GankGoodShift = -5;
        private const int GankLawShift = -2;
        private const int FreeForAllGoldLossPercent = 5;

        public static bool CanAttack(PvpRule rule, PvpParticipant attacker, PvpParticipant defender, ContentCatalog catalog)
        {
            if (attacker.CharacterId == defender.CharacterId || SameGroup(attacker.PartyId, defender.PartyId))
            {
                return false;
            }
            switch (rule)
            {
                case PvpRule.FreeForAll:
                case PvpRule.Arena:
                    return true;
                case PvpRule.FactionOnly:
                    return Quests.FactionRules.AreEnemies(catalog, attacker.FactionId, defender.FactionId);
                default:
                    return false;
            }
        }

        public static PvpKillConsequence OnKill(PvpRule rule, PvpParticipant killer, PvpParticipant victim)
        {
            var consequence = new PvpKillConsequence();
            if (rule == PvpRule.Arena)
            {
                return consequence;
            }
            if (rule == PvpRule.FreeForAll)
            {
                consequence.VictimGoldLossPercent = FreeForAllGoldLossPercent;
            }
            bool isGank = killer.Level - victim.Level >= GankLevelGap;
            bool victimWasGood = victim.Alignment.Moral == MoralAxis.Good;
            if (isGank || victimWasGood)
            {
                consequence.KillerGoodShift = GankGoodShift;
                consequence.KillerLawShift = GankLawShift;
            }
            return consequence;
        }

        private static bool SameGroup(Guid? first, Guid? second)
        {
            return first.HasValue && second.HasValue && first.Value == second.Value;
        }
    }

    /// <summary>Elo rating for arenas.</summary>
    public static class ArenaRating
    {
        public const int StartingRating = 1500;
        private const int KFactor = 32;
        private const double Scale = 400.0;

        public static (int Winner, int Loser) Update(int winnerRating, int loserRating)
        {
            double expectedWin = 1.0 / (1.0 + Math.Pow(10, (loserRating - winnerRating) / Scale));
            int delta = (int)Math.Round(KFactor * (1 - expectedWin));
            return (winnerRating + delta, loserRating - delta);
        }
    }
}
