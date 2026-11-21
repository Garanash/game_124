using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;

namespace Realmforge.Client.Core.CharacterCreation
{
    public sealed class RaceGroup
    {
        public RaceGroup(ZoneDefinition startZone, ContinentDefinition? continent, IReadOnlyList<RaceDefinition> races)
        {
            StartZone = startZone;
            Continent = continent;
            Races = races;
        }

        public ZoneDefinition StartZone { get; }
        public ContinentDefinition? Continent { get; }
        public IReadOnlyList<RaceDefinition> Races { get; }
    }

    /// <summary>
    /// State behind the character creation screen: race grid grouped by start location, 3x3 alignment chart
    /// and 27-point buy. Validation reuses the server rules so the "Create" button never lies.
    /// </summary>
    public sealed class CharacterCreationModel
    {
        private readonly ContentCatalog _catalog;
        private readonly int[] _baseScores;

        public CharacterCreationModel(ContentCatalog catalog)
        {
            _catalog = catalog;
            _baseScores = Enumerable.Repeat(PointBuy.MinScore, Enum.GetValues(typeof(Ability)).Length).ToArray();
            RaceGroups = BuildGroups(catalog);
            SelectedRaceId = RaceGroups.SelectMany(group => group.Races).Select(race => race.Id).FirstOrDefault() ?? string.Empty;
        }

        public IReadOnlyList<RaceGroup> RaceGroups { get; }
        public string Name { get; set; } = string.Empty;
        public string SelectedRaceId { get; private set; }
        public AlignmentCell Alignment { get; set; } = AlignmentCell.TrueNeutral;

        public RaceDefinition? SelectedRace => _catalog.Races.TryGetValue(SelectedRaceId, out RaceDefinition? race) ? race : null;

        public ZoneDefinition? StartZone => SelectedRace != null && _catalog.Zones.TryGetValue(SelectedRace.StartZoneId, out ZoneDefinition? zone) ? zone : null;

        public int PointsSpent => _baseScores.Sum(PointBuy.CostOf);

        public int PointsRemaining => PointBuy.Budget - PointsSpent;

        public event Action? Changed;

        public void SelectRace(string raceId)
        {
            if (!_catalog.Races.ContainsKey(raceId))
            {
                return;
            }
            SelectedRaceId = raceId;
            Changed?.Invoke();
        }

        public void SelectAlignment(AlignmentCell alignment)
        {
            Alignment = alignment;
            Changed?.Invoke();
        }

        public int BaseScore(Ability ability) => _baseScores[(int)ability];

        public int RaceBonus(Ability ability) =>
            SelectedRace != null && SelectedRace.AbilityBonuses.TryGetValue(ability, out int bonus) ? bonus : 0;

        public int FinalScore(Ability ability) => BaseScore(ability) + RaceBonus(ability);

        public int FinalModifier(Ability ability) => AbilityScores.ModifierOf(FinalScore(ability));

        public bool CanIncrease(Ability ability)
        {
            int current = BaseScore(ability);
            return current < PointBuy.MaxScore && PointBuy.CostOf(current + 1) - PointBuy.CostOf(current) <= PointsRemaining;
        }

        public bool CanDecrease(Ability ability) => BaseScore(ability) > PointBuy.MinScore;

        public void Increase(Ability ability) => Adjust(ability, CanIncrease(ability), +1);

        public void Decrease(Ability ability) => Adjust(ability, CanDecrease(ability), -1);

        public CreateCharacterRequest ToRequest() => new CreateCharacterRequest
        {
            Name = Name.Trim(),
            RaceId = SelectedRaceId,
            Alignment = Alignment,
            Abilities = (int[])_baseScores.Clone(),
        };

        public RuleResult Validate() => CharacterRules.ValidateCreation(ToRequest(), _catalog);

        private void Adjust(Ability ability, bool allowed, int delta)
        {
            if (!allowed)
            {
                return;
            }
            _baseScores[(int)ability] += delta;
            Changed?.Invoke();
        }

        private static IReadOnlyList<RaceGroup> BuildGroups(ContentCatalog catalog)
        {
            return catalog.Races.Values
                .Where(race => catalog.Zones.ContainsKey(race.StartZoneId))
                .GroupBy(race => race.StartZoneId)
                .Select(group =>
                {
                    ZoneDefinition zone = catalog.Zones[group.Key];
                    catalog.Continents.TryGetValue(zone.ContinentId, out ContinentDefinition? continent);
                    return new RaceGroup(zone, continent, group.OrderBy(race => race.Name).ToList());
                })
                .OrderBy(group => group.Continent?.Name)
                .ThenBy(group => group.StartZone.Name)
                .ToList();
        }
    }
}
