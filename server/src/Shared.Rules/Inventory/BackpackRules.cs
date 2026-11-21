using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Inventory
{
    public sealed class FusionCandidate
    {
        public FusionCandidate(FusionRecipe recipe, IReadOnlyList<PlacedItem> inputs)
        {
            Recipe = recipe;
            Inputs = inputs;
        }

        public FusionRecipe Recipe { get; }
        public IReadOnlyList<PlacedItem> Inputs { get; }
    }

    public static class BackpackRules
    {
        /// <summary>Item modifiers plus every synergy triggered by orthogonally touching items.</summary>
        public static StatBlock ComputeBonuses(Backpack backpack, ContentCatalog catalog)
        {
            var block = new StatBlock();
            foreach (PlacedItem item in backpack.Items)
            {
                block.AddRange(catalog.Items[item.ItemId].Modifiers);
            }
            foreach (PlacedItem source in backpack.Items)
            {
                List<string> sourceTags = catalog.Items[source.ItemId].Tags;
                foreach (PlacedItem neighbour in backpack.Items)
                {
                    if (neighbour.InstanceId == source.InstanceId || !backpack.AreAdjacent(source, neighbour, catalog))
                    {
                        continue;
                    }
                    List<string> neighbourTags = catalog.Items[neighbour.ItemId].Tags;
                    foreach (SynergyDefinition synergy in catalog.Synergies.Values)
                    {
                        if (sourceTags.Contains(synergy.SourceTag) && neighbourTags.Contains(synergy.NeighborTag))
                        {
                            block.AddRange(synergy.Bonus);
                        }
                    }
                }
            }
            return block;
        }

        public static ItemDefinition? BestWeapon(Backpack backpack, ContentCatalog catalog)
        {
            return backpack.Items
                .Select(item => catalog.Items[item.ItemId])
                .Where(definition => !string.IsNullOrEmpty(definition.WeaponDamage))
                .OrderByDescending(definition => definition.Rarity)
                .ThenByDescending(definition => Dice.DiceExpression.Parse(definition.WeaponDamage).Max)
                .FirstOrDefault();
        }

        public static IReadOnlyList<FusionCandidate> FindFusions(Backpack backpack, ContentCatalog catalog)
        {
            var candidates = new List<FusionCandidate>();
            foreach (FusionRecipe recipe in catalog.Fusions.Values)
            {
                List<PlacedItem>? match = MatchConnected(backpack, recipe.Inputs, catalog, new List<PlacedItem>());
                if (match != null)
                {
                    candidates.Add(new FusionCandidate(recipe, match));
                }
            }
            return candidates;
        }

        /// <summary>Consumes the touching inputs and places the output, preferring the first input's spot.</summary>
        public static RuleResult Fuse(Backpack backpack, string recipeId, Guid outputInstanceId, ContentCatalog catalog)
        {
            FusionCandidate? candidate = FindFusions(backpack, catalog).FirstOrDefault(c => c.Recipe.Id == recipeId);
            if (candidate == null)
            {
                return RuleResult.Fail("fusion.unavailable", "Required items are not touching in the backpack.");
            }
            List<PlacedItem> removed = candidate.Inputs.ToList();
            foreach (PlacedItem input in removed)
            {
                backpack.Remove(input.InstanceId);
            }
            PlacedItem anchor = removed[0];
            string output = candidate.Recipe.Output;
            for (int rotation = 0; rotation < 4; rotation++)
            {
                if (backpack.CanPlace(output, anchor.X, anchor.Y, rotation, catalog).Success)
                {
                    backpack.Place(outputInstanceId, output, anchor.X, anchor.Y, rotation, catalog);
                    return RuleResult.Ok();
                }
            }
            if (backpack.TryAutoPlace(outputInstanceId, output, catalog, out _))
            {
                return RuleResult.Ok();
            }
            backpack.Items.AddRange(removed);
            return RuleResult.Fail("fusion.no_space", "Not enough space for the fused item.");
        }

        private static List<PlacedItem>? MatchConnected(Backpack backpack, List<string> remaining, ContentCatalog catalog, List<PlacedItem> chosen)
        {
            if (remaining.Count == 0)
            {
                return IsConnected(backpack, chosen, catalog) ? new List<PlacedItem>(chosen) : null;
            }
            string next = remaining[0];
            List<string> rest = remaining.Skip(1).ToList();
            foreach (PlacedItem item in backpack.Items.Where(item => item.ItemId == next && !chosen.Contains(item)))
            {
                chosen.Add(item);
                List<PlacedItem>? match = MatchConnected(backpack, rest, catalog, chosen);
                chosen.RemoveAt(chosen.Count - 1);
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }

        private static bool IsConnected(Backpack backpack, List<PlacedItem> items, ContentCatalog catalog)
        {
            if (items.Count <= 1)
            {
                return true;
            }
            var visited = new HashSet<PlacedItem> { items[0] };
            var frontier = new Queue<PlacedItem>();
            frontier.Enqueue(items[0]);
            while (frontier.Count > 0)
            {
                PlacedItem current = frontier.Dequeue();
                foreach (PlacedItem other in items.Where(other => !visited.Contains(other) && backpack.AreAdjacent(current, other, catalog)))
                {
                    visited.Add(other);
                    frontier.Enqueue(other);
                }
            }
            return visited.Count == items.Count;
        }
    }
}
