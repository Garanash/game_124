using System.Collections.Generic;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Cosmetics
{
    /// <summary>Cosmetics are pure visuals layered over the base model and never change stats (no pay-to-win).</summary>
    public sealed class AppearanceLoadout
    {
        public Dictionary<CosmeticSlot, string> Slots { get; set; } = new Dictionary<CosmeticSlot, string>();
    }

    public static class AppearanceRules
    {
        public static RuleResult CanEquip(string cosmeticId, string raceId, ISet<string> ownedCosmetics, ContentCatalog catalog)
        {
            if (!catalog.Cosmetics.TryGetValue(cosmeticId, out CosmeticDefinition? cosmetic))
            {
                return RuleResult.Fail("cosmetic.unknown", "Unknown cosmetic " + cosmeticId);
            }
            if (!ownedCosmetics.Contains(cosmeticId))
            {
                return RuleResult.Fail("cosmetic.not_owned", "Cosmetic is not owned.");
            }
            if (cosmetic.RaceIds.Count > 0 && !cosmetic.RaceIds.Contains(raceId))
            {
                return RuleResult.Fail("cosmetic.race", "Cosmetic does not fit this race.");
            }
            return RuleResult.Ok();
        }

        public static RuleResult Equip(AppearanceLoadout loadout, string cosmeticId, string raceId, ISet<string> ownedCosmetics, ContentCatalog catalog)
        {
            RuleResult check = CanEquip(cosmeticId, raceId, ownedCosmetics, catalog);
            if (check.Success)
            {
                loadout.Slots[catalog.Cosmetics[cosmeticId].Slot] = cosmeticId;
            }
            return check;
        }
    }
}
