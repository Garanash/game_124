using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Characters
{
    /// <summary>Persistent character state. Plain settable properties so it serializes as-is to JSON storage.</summary>
    public sealed class CharacterSheet
    {
        /// <summary>Unclassed characters are "Wanderers" until they pass a mentor trial at level 3.</summary>
        public const string WandererClassId = "wanderer";
        public const int WandererHitDie = 8;

        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string RaceId { get; set; } = string.Empty;
        public int[] BaseAbilities { get; set; } = new int[AbilityScores.AbilityCount];
        /// <summary>Null for heroes created before looks could be chosen; clients then derive one from the name.</summary>
        public CharacterLook? Look { get; set; }
        public int LawChaos { get; set; }
        public int GoodEvil { get; set; }
        public long Xp { get; set; }
        public int CurrentHp { get; set; }
        public long Gold { get; set; }
        public Dictionary<string, int> ClassLevels { get; set; } = new Dictionary<string, int>();
        public HashSet<string> UnlockedClasses { get; set; } = new HashSet<string>();
        public List<string> Perks { get; set; } = new List<string>();
        public HashSet<string> Achievements { get; set; } = new HashSet<string>();
        public HashSet<string> CompletedQuests { get; set; } = new HashSet<string>();
        public Dictionary<string, int> Reputation { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, long> ProfessionXp { get; set; } = new Dictionary<string, long>();
        public string ZoneId { get; set; } = string.Empty;
        public float PositionX { get; set; }
        public float PositionZ { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public int Level => Progression.LevelForXp(Xp);

        public int ClassLevelTotal => ClassLevels.Values.Sum();

        public bool HasClass => ClassLevels.Count > 0;

        // Methods rather than a property: a settable computed property would be round-tripped by JSON
        // serializers and overwrite the stored axes with a default value on load.
        public Alignment GetAlignment()
        {
            return new Alignment(LawChaos, GoodEvil);
        }

        public void SetAlignment(Alignment alignment)
        {
            LawChaos = alignment.LawChaos;
            GoodEvil = alignment.GoodEvil;
        }

        public AbilityScores Abilities(ContentCatalog catalog)
        {
            AbilityScores scores = AbilityScores.FromArray(BaseAbilities);
            if (catalog.Races.TryGetValue(RaceId, out RaceDefinition? race))
            {
                scores = scores.WithBonuses(race.AbilityBonuses);
            }
            return scores;
        }

        public int ReputationWith(string factionId)
        {
            return Reputation.TryGetValue(factionId, out int value) ? value : 0;
        }
    }
}
