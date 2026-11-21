using System;
using System.Linq;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Quests
{
    public enum Attitude
    {
        Hostile,
        Unfriendly,
        Neutral,
        Friendly,
        Honored,
    }

    public static class FactionRules
    {
        public const int ReputationLimit = 3000;
        private const int AlignmentAffinityBonus = 300;
        private const int AlignmentMismatchPenalty = -300;
        private const int HostileBelow = -1000;
        private const int UnfriendlyBelow = -200;
        private const int FriendlyFrom = 500;
        private const int HonoredFrom = 1500;

        public static int ClampReputation(int value)
        {
            return Math.Max(-ReputationLimit, Math.Min(ReputationLimit, value));
        }

        /// <summary>Reputation drives attitude, but alignment colours first impressions of the faction.</summary>
        public static Attitude AttitudeOf(FactionDefinition faction, CharacterSheet sheet)
        {
            bool hasAffinityRule = faction.Affinity.Law.Count > 0 || faction.Affinity.Moral.Count > 0;
            int alignmentBias = !hasAffinityRule ? 0
                : faction.Affinity.IsSatisfiedBy(sheet.Alignment) ? AlignmentAffinityBonus : AlignmentMismatchPenalty;
            int effective = sheet.ReputationWith(faction.Id) + alignmentBias;
            if (effective < HostileBelow)
            {
                return Attitude.Hostile;
            }
            if (effective < UnfriendlyBelow)
            {
                return Attitude.Unfriendly;
            }
            if (effective >= HonoredFrom)
            {
                return Attitude.Honored;
            }
            return effective >= FriendlyFrom ? Attitude.Friendly : Attitude.Neutral;
        }

        public static int PricePercent(Attitude attitude)
        {
            switch (attitude)
            {
                case Attitude.Honored:
                    return 85;
                case Attitude.Friendly:
                    return 95;
                case Attitude.Unfriendly:
                    return 125;
                case Attitude.Hostile:
                    return 0;
                default:
                    return 100;
            }
        }

        public static bool WillTrade(Attitude attitude)
        {
            return attitude != Attitude.Hostile;
        }

        public static bool AreEnemies(ContentCatalog catalog, string factionA, string factionB)
        {
            return catalog.Factions.TryGetValue(factionA, out FactionDefinition? first) && first.Enemies.Contains(factionB)
                   || catalog.Factions.TryGetValue(factionB, out FactionDefinition? second) && second.Enemies.Contains(factionA);
        }

        public static string? HomeFaction(ContentCatalog catalog, string raceId)
        {
            if (!catalog.Races.TryGetValue(raceId, out RaceDefinition? race))
            {
                return null;
            }
            return catalog.Factions.Keys.FirstOrDefault(id => id == "faction_" + race.ContinentId);
        }
    }
}
