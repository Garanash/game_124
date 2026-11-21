using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Characters
{
    public sealed class CreateCharacterRequest
    {
        public string Name { get; set; } = string.Empty;
        public string RaceId { get; set; } = string.Empty;
        public AlignmentCell Alignment { get; set; } = AlignmentCell.TrueNeutral;
        public int[] Abilities { get; set; } = new int[AbilityScores.AbilityCount];
    }

    public static class CharacterRules
    {
        public const int MinNameLength = 3;
        public const int MaxNameLength = 20;
        public const int StartingGold = 25;

        public static RuleResult ValidateCreation(CreateCharacterRequest request, ContentCatalog catalog)
        {
            string name = request.Name?.Trim() ?? string.Empty;
            if (name.Length < MinNameLength || name.Length > MaxNameLength)
            {
                return RuleResult.Fail("character.name_length", "Name must be 3-20 characters.");
            }
            if (!name.All(symbol => char.IsLetter(symbol) || symbol == '-' || symbol == '\''))
            {
                return RuleResult.Fail("character.name_symbols", "Name may contain letters, '-' and apostrophes only.");
            }
            if (!catalog.Races.ContainsKey(request.RaceId))
            {
                return RuleResult.Fail("character.unknown_race", "Unknown race " + request.RaceId);
            }
            if (request.Abilities == null || request.Abilities.Length != AbilityScores.AbilityCount)
            {
                return RuleResult.Fail("character.abilities", "Six ability scores are required.");
            }
            return PointBuy.Validate(AbilityScores.FromArray(request.Abilities));
        }

        /// <summary>Creates a level-1 Wanderer placed at the race's start zone spawn point.</summary>
        public static CharacterSheet Create(Guid characterId, Guid accountId, CreateCharacterRequest request, ContentCatalog catalog, DateTime nowUtc)
        {
            RuleResult validation = ValidateCreation(request, catalog);
            if (!validation.Success)
            {
                throw new InvalidOperationException(validation.ToString());
            }
            RaceDefinition race = catalog.Races[request.RaceId];
            ZoneDefinition? startZone = catalog.Zones.TryGetValue(race.StartZoneId, out ZoneDefinition? zone) ? zone : null;
            var sheet = new CharacterSheet
            {
                Id = characterId,
                AccountId = accountId,
                Name = request.Name.Trim(),
                RaceId = race.Id,
                BaseAbilities = (int[])request.Abilities.Clone(),
                Alignment = Characters.Alignment.FromCell(request.Alignment),
                Gold = StartingGold,
                ZoneId = race.StartZoneId,
                PositionX = startZone?.Spawn.X ?? 0,
                PositionZ = startZone?.Spawn.Z ?? 0,
                CreatedAtUtc = nowUtc,
            };
            sheet.CurrentHp = MaxHp(sheet, catalog, new StatBlock());
            return sheet;
        }

        /// <summary>Max HP: full hit die at level 1, average afterwards, plus CON per level (SRD 5.1).</summary>
        public static int MaxHp(CharacterSheet sheet, ContentCatalog catalog, StatBlock bonuses)
        {
            int constitution = sheet.Abilities(catalog).Modifier(Ability.Constitution);
            int level = sheet.Level;
            int firstHitDie = FirstHitDie(sheet, catalog);
            int averagePerLevel = firstHitDie / 2 + 1;
            int hp = firstHitDie + constitution + (level - 1) * (averagePerLevel + constitution);
            return Math.Max(1, hp + bonuses[Stat.MaxHp]);
        }

        public static int ArmorClass(CharacterSheet sheet, ContentCatalog catalog, StatBlock bonuses)
        {
            int baseArmor = sheet.ClassLevels.Keys
                .Select(id => catalog.Classes.TryGetValue(id, out ClassDefinition? definition) ? definition.BaseArmorClass : 10)
                .DefaultIfEmpty(10)
                .Max();
            return baseArmor + sheet.Abilities(catalog).Modifier(Ability.Dexterity) + bonuses[Stat.ArmorClass];
        }

        /// <summary>Class choice is gated by level, a passed mentor trial and the class alignment requirement.</summary>
        public static RuleResult CanTakeClassLevel(CharacterSheet sheet, string classId, ContentCatalog catalog)
        {
            if (!catalog.Classes.TryGetValue(classId, out ClassDefinition? definition))
            {
                return RuleResult.Fail("class.unknown", "Unknown class " + classId);
            }
            if (sheet.Level < Progression.ClassChoiceLevel)
            {
                return RuleResult.Fail("class.level", "A class can be chosen from level 3.");
            }
            if (!sheet.UnlockedClasses.Contains(classId))
            {
                return RuleResult.Fail("class.trial", "Complete the mentor trial first.");
            }
            if (!definition.Alignment.IsSatisfiedBy(sheet.Alignment))
            {
                return RuleResult.Fail("class.alignment", "Your alignment does not fit this class.");
            }
            int unspentLevels = sheet.Level - Progression.ClassChoiceLevel + 1 - sheet.ClassLevelTotal;
            if (unspentLevels <= 0)
            {
                return RuleResult.Fail("class.no_levels", "No unspent class levels.");
            }
            bool isMulticlass = sheet.HasClass && !sheet.ClassLevels.ContainsKey(classId);
            if (isMulticlass && sheet.Abilities(catalog)[definition.PrimaryAbility] < Progression.MulticlassAbilityMinimum)
            {
                return RuleResult.Fail("class.multiclass", "Multiclassing requires 13 in the primary ability.");
            }
            return RuleResult.Ok();
        }

        public static void TakeClassLevel(CharacterSheet sheet, string classId, ContentCatalog catalog)
        {
            RuleResult check = CanTakeClassLevel(sheet, classId, catalog);
            if (!check.Success)
            {
                throw new InvalidOperationException(check.ToString());
            }
            sheet.ClassLevels[classId] = sheet.ClassLevels.TryGetValue(classId, out int level) ? level + 1 : 1;
        }

        public static IReadOnlyList<string> KnownAbilities(CharacterSheet sheet, ContentCatalog catalog)
        {
            var known = new List<string>();
            foreach (KeyValuePair<string, int> classLevel in sheet.ClassLevels)
            {
                if (!catalog.Classes.TryGetValue(classLevel.Key, out ClassDefinition? definition))
                {
                    continue;
                }
                known.AddRange(definition.Abilities.Where(unlock => unlock.Level <= classLevel.Value).Select(unlock => unlock.AbilityId));
            }
            foreach (string perkId in sheet.Perks)
            {
                if (catalog.Perks.TryGetValue(perkId, out PerkDefinition? perk))
                {
                    known.AddRange(perk.GrantsAbilities);
                }
            }
            return known.Distinct().ToList();
        }

        public static long GrantXp(CharacterSheet sheet, long amount)
        {
            long cap = Progression.XpForLevel(Progression.MaxLevel);
            sheet.Xp = Math.Min(cap, sheet.Xp + Math.Max(0, amount));
            return sheet.Xp;
        }

        private static int FirstHitDie(CharacterSheet sheet, ContentCatalog catalog)
        {
            string? firstClass = sheet.ClassLevels.Keys.FirstOrDefault();
            if (firstClass != null && catalog.Classes.TryGetValue(firstClass, out ClassDefinition? definition))
            {
                return definition.HitDie;
            }
            return CharacterSheet.WandererHitDie;
        }
    }
}
