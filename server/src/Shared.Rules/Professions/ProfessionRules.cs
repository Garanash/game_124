using System;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Rules.Inventory;

namespace Realmforge.Rules.Professions
{
    public static class ProfessionRules
    {
        public const int MaxSkill = 100;
        private const int XpPerSkillStep = 50;

        /// <summary>Quadratic curve: skill n needs 50 * n^2 XP, so early levels come fast and mastery is slow.</summary>
        public static int SkillForXp(long xp)
        {
            int skill = (int)Math.Floor(Math.Sqrt(Math.Max(0, xp) / (double)XpPerSkillStep)) + 1;
            return Math.Min(MaxSkill, skill);
        }

        public static int Skill(CharacterSheet sheet, string professionId)
        {
            return SkillForXp(sheet.ProfessionXp.TryGetValue(professionId, out long xp) ? xp : 0);
        }

        public static void GrantXp(CharacterSheet sheet, string professionId, long amount)
        {
            long current = sheet.ProfessionXp.TryGetValue(professionId, out long xp) ? xp : 0;
            sheet.ProfessionXp[professionId] = current + Math.Max(0, amount);
        }

        public static RuleResult CanGather(CharacterSheet sheet, GatheringNodeDefinition node)
        {
            return Skill(sheet, node.ProfessionId) >= node.MinSkill
                ? RuleResult.Ok()
                : RuleResult.Fail("profession.skill", "Requires " + node.ProfessionId + " " + node.MinSkill);
        }

        public static LootResult Gather(CharacterSheet sheet, GatheringNodeDefinition node, ContentCatalog catalog, IRandomSource random, int yieldBonusPercent)
        {
            RuleResult check = CanGather(sheet, node);
            if (!check.Success)
            {
                throw new InvalidOperationException(check.ToString());
            }
            int skillBonus = Skill(sheet, node.ProfessionId) - node.MinSkill;
            LootResult loot = LootRoller.Roll(catalog.LootTables[node.LootTableId], random, yieldBonusPercent + skillBonus);
            GrantXp(sheet, node.ProfessionId, node.Xp);
            return loot;
        }

        public static RuleResult CanCraft(CharacterSheet sheet, CraftingRecipe recipe, StackInventory materials)
        {
            if (Skill(sheet, recipe.ProfessionId) < recipe.MinSkill)
            {
                return RuleResult.Fail("profession.skill", "Requires " + recipe.ProfessionId + " " + recipe.MinSkill);
            }
            return materials.HasAll(recipe.Inputs)
                ? RuleResult.Ok()
                : RuleResult.Fail("craft.materials", "Not enough materials.");
        }

        public static RuleResult Craft(CharacterSheet sheet, CraftingRecipe recipe, StackInventory materials)
        {
            RuleResult check = CanCraft(sheet, recipe, materials);
            if (!check.Success)
            {
                return check;
            }
            materials.TryRemoveAll(recipe.Inputs);
            materials.Add(recipe.Output.ItemId, recipe.Output.Quantity);
            GrantXp(sheet, recipe.ProfessionId, recipe.Xp);
            return RuleResult.Ok();
        }

        public static int CraftSeconds(CraftingRecipe recipe, int craftingSpeedPercent)
        {
            return Math.Max(1, recipe.CraftSeconds * 100 / (100 + Math.Max(0, craftingSpeedPercent)));
        }
    }
}
