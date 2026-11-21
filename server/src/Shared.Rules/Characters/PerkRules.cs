using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Characters
{
    public static class PerkRules
    {
        public static int LevelPerkSlotsFree(CharacterSheet sheet, ContentCatalog catalog)
        {
            int used = sheet.Perks.Count(id => catalog.Perks.TryGetValue(id, out PerkDefinition? perk) && perk.Source == PerkSource.Level);
            return Progression.PerkSlotsAtLevel(sheet.Level) - used;
        }

        public static RuleResult CanTake(CharacterSheet sheet, string perkId, ContentCatalog catalog)
        {
            if (!catalog.Perks.TryGetValue(perkId, out PerkDefinition? perk))
            {
                return RuleResult.Fail("perk.unknown", "Unknown perk " + perkId);
            }
            if (sheet.Perks.Contains(perkId))
            {
                return RuleResult.Fail("perk.owned", "Perk already taken.");
            }
            if (sheet.Level < perk.MinLevel)
            {
                return RuleResult.Fail("perk.level", "Requires level " + perk.MinLevel);
            }
            if (perk.Requires.Any(required => !sheet.Perks.Contains(required)))
            {
                return RuleResult.Fail("perk.requires", "Missing prerequisite perks.");
            }
            if (perk.Source == PerkSource.Achievement)
            {
                return sheet.Achievements.Contains(perk.AchievementId)
                    ? RuleResult.Ok()
                    : RuleResult.Fail("perk.achievement", "Achievement not earned.");
            }
            return LevelPerkSlotsFree(sheet, catalog) > 0
                ? RuleResult.Ok()
                : RuleResult.Fail("perk.no_slots", "No free perk slots.");
        }

        public static IReadOnlyList<PerkDefinition> Available(CharacterSheet sheet, ContentCatalog catalog)
        {
            return catalog.Perks.Values.Where(perk => CanTake(sheet, perk.Id, catalog).Success).ToList();
        }

        public static StatBlock Bonuses(CharacterSheet sheet, ContentCatalog catalog)
        {
            var block = new StatBlock();
            foreach (string perkId in sheet.Perks)
            {
                if (catalog.Perks.TryGetValue(perkId, out PerkDefinition? perk))
                {
                    block.AddRange(perk.Modifiers);
                }
            }
            return block;
        }
    }
}
