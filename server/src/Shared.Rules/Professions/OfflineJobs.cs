using System;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Rules.Inventory;

namespace Realmforge.Rules.Professions
{
    public sealed class OfflineJobState
    {
        public string JobId { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public ulong Seed { get; set; }
    }

    public sealed class OfflineJobResult
    {
        public int Cycles { get; set; }
        public long Xp { get; set; }
        public LootResult Loot { get; set; } = new LootResult();
        public int CraftedCount { get; set; }
    }

    /// <summary>
    /// Characters keep working while their player is away. The outcome is computed in one deterministic pass
    /// on claim, from a seed fixed at start, so restarts or re-claims can never change the result.
    /// </summary>
    public static class OfflineJobs
    {
        public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);

        public static RuleResult CanStart(CharacterSheet sheet, OfflineJobDefinition job, ContentCatalog catalog, TimeSpan requested)
        {
            if (requested <= TimeSpan.Zero || requested > MaxDuration)
            {
                return RuleResult.Fail("offline.duration", "Duration must be between 1 minute and 12 hours.");
            }
            if (job.Kind == OfflineJobKind.Gather && catalog.Nodes.TryGetValue(job.TargetId, out GatheringNodeDefinition? node))
            {
                return ProfessionRules.CanGather(sheet, node);
            }
            if (job.Kind == OfflineJobKind.Craft && catalog.Recipes.TryGetValue(job.TargetId, out CraftingRecipe? recipe)
                && ProfessionRules.Skill(sheet, recipe.ProfessionId) < recipe.MinSkill)
            {
                return RuleResult.Fail("profession.skill", "Requires " + recipe.ProfessionId + " " + recipe.MinSkill);
            }
            return RuleResult.Ok();
        }

        public static OfflineJobState Start(CharacterSheet sheet, OfflineJobDefinition job, DateTime nowUtc, TimeSpan duration)
        {
            TimeSpan clamped = duration > MaxDuration ? MaxDuration : duration;
            return new OfflineJobState
            {
                JobId = job.Id,
                StartedAtUtc = nowUtc,
                EndsAtUtc = nowUtc + clamped,
                Seed = SeededRandom.CombineSeed(sheet.Id.ToString(), nowUtc.Ticks),
            };
        }

        public static OfflineJobResult Simulate(CharacterSheet sheet, OfflineJobState state, ContentCatalog catalog, StackInventory materials, DateTime nowUtc)
        {
            OfflineJobDefinition job = catalog.OfflineJobs[state.JobId];
            DateTime end = nowUtc < state.EndsAtUtc ? nowUtc : state.EndsAtUtc;
            int cycles = Math.Max(0, (int)((end - state.StartedAtUtc).TotalMinutes / Math.Max(1, job.CycleMinutes)));
            var random = new SeededRandom(state.Seed);
            var result = new OfflineJobResult { Cycles = cycles };
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                RunCycle(sheet, job, catalog, materials, random, result);
            }
            result.Loot.Gold += (long)job.GoldPerCycle * cycles;
            result.Xp += (long)job.XpPerCycle * cycles;
            if (!string.IsNullOrEmpty(job.ProfessionId))
            {
                ProfessionRules.GrantXp(sheet, job.ProfessionId, result.Xp);
            }
            return result;
        }

        private static void RunCycle(CharacterSheet sheet, OfflineJobDefinition job, ContentCatalog catalog, StackInventory materials, IRandomSource random, OfflineJobResult result)
        {
            switch (job.Kind)
            {
                case OfflineJobKind.Gather:
                    if (catalog.Nodes.TryGetValue(job.TargetId, out GatheringNodeDefinition? node))
                    {
                        result.Loot.Merge(LootRoller.Roll(catalog.LootTables[node.LootTableId], random));
                    }
                    return;
                case OfflineJobKind.Hunt:
                    if (catalog.Mobs.TryGetValue(job.TargetId, out MobDefinition? game) && catalog.LootTables.TryGetValue(game.LootTableId, out LootTable? table))
                    {
                        result.Loot.Merge(LootRoller.Roll(table, random));
                    }
                    return;
                case OfflineJobKind.Craft:
                    if (catalog.Recipes.TryGetValue(job.TargetId, out CraftingRecipe? recipe) && materials.TryRemoveAll(recipe.Inputs))
                    {
                        result.Loot.Add(recipe.Output.ItemId, recipe.Output.Quantity);
                        result.CraftedCount++;
                    }
                    return;
                default:
                    return;
            }
        }
    }
}
