using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Rules.Inventory;

namespace Realmforge.Content
{
    public enum IssueSeverity
    {
        Warning,
        Error,
    }

    public sealed class ContentIssue
    {
        public ContentIssue(IssueSeverity severity, string where, string message)
        {
            Severity = severity;
            Where = where;
            Message = message;
        }

        public IssueSeverity Severity { get; }
        public string Where { get; }
        public string Message { get; }

        public override string ToString()
        {
            return Severity.ToString().ToUpperInvariant() + " " + Where + ": " + Message;
        }
    }

    /// <summary>Cross-reference and sanity checks run in CI so broken content never reaches a server.</summary>
    public sealed class ContentValidator
    {
        private static readonly HashSet<int> AllowedPartySizes = new HashSet<int> { 1, 3, 5, 25 };
        private static readonly HashSet<string> KnownStorePlatforms = new HashSet<string> { "apple", "google", "steam", "xsolla" };
        private const int HoursPerDay = 24;

        private readonly ContentCatalog _catalog;
        private readonly List<ContentIssue> _issues = new List<ContentIssue>();

        private ContentValidator(ContentCatalog catalog)
        {
            _catalog = catalog;
        }

        public static IReadOnlyList<ContentIssue> Validate(ContentCatalog catalog)
        {
            var validator = new ContentValidator(catalog);
            validator.Run();
            return validator._issues;
        }

        private void Run()
        {
            ValidateRaces();
            ValidateClasses();
            ValidateAbilities();
            ValidatePerks();
            ValidateItems();
            ValidateLoot();
            ValidateWorld();
            ValidateNpcs();
            ValidateQuests();
            ValidateEconomy();
            ValidateStore();
        }

        private void ValidateRaces()
        {
            foreach (RaceDefinition race in _catalog.Races.Values)
            {
                string where = "races/" + race.Id;
                Require(_catalog.Continents.ContainsKey(race.ContinentId), where, "unknown continent " + race.ContinentId);
                if (!_catalog.Zones.TryGetValue(race.StartZoneId, out ZoneDefinition? zone))
                {
                    Error(where, "unknown start zone " + race.StartZoneId);
                }
                else if (zone.Kind != ZoneKind.StartZone)
                {
                    Error(where, "start zone " + zone.Id + " is not of kind StartZone");
                }
                else if (race.StartSettlementId.Length > 0 && zone.FindSettlement(race.StartSettlementId) == null)
                {
                    Error(where, "unknown start settlement " + race.StartSettlementId + " in " + zone.Id);
                }
                if (race.IpStatus == IpStatus.NonSrd || race.IpStatus == IpStatus.ThirdParty)
                {
                    Warn(where, "name/concept is " + race.IpStatus + " IP: rename and redesign before commercial release");
                }
                Require(race.AbilityBonuses.Values.Sum() <= 3, where, "ability bonuses exceed +3 total");
            }
        }

        private void ValidateClasses()
        {
            foreach (ClassDefinition definition in _catalog.Classes.Values)
            {
                string where = "classes/" + definition.Id;
                foreach (ClassAbilityUnlock unlock in definition.Abilities)
                {
                    Require(_catalog.Abilities.ContainsKey(unlock.AbilityId), where, "unknown ability " + unlock.AbilityId);
                }
                if (!_catalog.Quests.TryGetValue(definition.TrialQuestId, out QuestDefinition? trial))
                {
                    Error(where, "unknown trial quest " + definition.TrialQuestId);
                    continue;
                }
                Require(trial.Rewards.UnlocksClassId == definition.Id, where, "trial quest does not unlock this class");
            }
        }

        private void ValidateAbilities()
        {
            foreach (AbilityDefinition ability in _catalog.Abilities.Values)
            {
                string where = "abilities/" + ability.Id;
                Require(ability.Effects.Count > 0, where, "ability has no effects");
                Require(ability.Target != TargetKind.Area || ability.AreaRadiusCells >= 0, where, "area radius must be non-negative");
                foreach (EffectDefinition effect in ability.Effects)
                {
                    bool needsDice = effect.Type == EffectType.Damage || effect.Type == EffectType.Heal;
                    Require(!needsDice || DiceExpression.TryParse(effect.Dice, out _), where, "invalid dice '" + effect.Dice + "'");
                    if (effect.Type == EffectType.Summon)
                    {
                        Require(effect.SummonHp > 0 && DiceExpression.TryParse(effect.SummonDamage, out _), where, "summon needs hp and damage dice");
                    }
                }
            }
        }

        private void ValidatePerks()
        {
            foreach (PerkDefinition perk in _catalog.Perks.Values)
            {
                string where = "perks/" + perk.Id;
                foreach (string required in perk.Requires)
                {
                    Require(_catalog.Perks.ContainsKey(required), where, "unknown required perk " + required);
                }
                foreach (string abilityId in perk.GrantsAbilities)
                {
                    Require(_catalog.Abilities.ContainsKey(abilityId), where, "unknown granted ability " + abilityId);
                }
                Require(perk.Source != PerkSource.Achievement || perk.AchievementId.Length > 0, where, "achievement perk needs achievement_id");
            }
        }

        private void ValidateItems()
        {
            foreach (ItemDefinition item in _catalog.Items.Values)
            {
                string where = "items/" + item.Id;
                Require(string.IsNullOrEmpty(item.WeaponDamage) || DiceExpression.TryParse(item.WeaponDamage, out _), where, "invalid weapon dice");
                Require(item.Shape.All(cell => cell.X >= 0 && cell.Y >= 0), where, "shape offsets must be non-negative");
                Require(item.Shape.All(cell => cell.X < Backpack.MaxWidth && cell.Y < Backpack.MaxHeight), where, "shape exceeds max backpack size");
            }
            HashSet<string> tags = new HashSet<string>(_catalog.Items.Values.SelectMany(item => item.Tags));
            foreach (SynergyDefinition synergy in _catalog.Synergies.Values)
            {
                string where = "synergies/" + synergy.Id;
                Require(tags.Contains(synergy.SourceTag), where, "no item carries tag " + synergy.SourceTag);
                Require(tags.Contains(synergy.NeighborTag), where, "no item carries tag " + synergy.NeighborTag);
            }
            foreach (FusionRecipe fusion in _catalog.Fusions.Values)
            {
                string where = "fusions/" + fusion.Id;
                Require(fusion.Inputs.Count >= 2, where, "fusion needs at least two inputs");
                RequireItems(where, fusion.Inputs.Append(fusion.Output));
            }
        }

        private void ValidateLoot()
        {
            foreach (LootTable table in _catalog.LootTables.Values)
            {
                string where = "loot/" + table.Id;
                RequireItems(where, table.Entries.Select(entry => entry.ItemId));
                Require(table.Entries.All(entry => entry.Weight > 0 && entry.Min <= entry.Max), where, "weights must be positive and min <= max");
                Require(table.GoldMin <= table.GoldMax, where, "gold_min exceeds gold_max");
            }
        }

        private void ValidateWorld()
        {
            foreach (ZoneDefinition zone in _catalog.Zones.Values)
            {
                string where = "zones/" + zone.Id;
                Require(_catalog.Continents.ContainsKey(zone.ContinentId), where, "unknown continent " + zone.ContinentId);
                Require(zone.SceneAddress.Length > 0, where, "scene_address is required");
                Require(zone.MinLevel <= zone.MaxLevel, where, "min_level exceeds max_level");
                Require(zone.Kind != ZoneKind.PvpZone || zone.Pvp != PvpRule.None, where, "PvP zone needs a pvp rule");
                Require(InsideZone(zone, zone.Spawn), where, "spawn is outside the zone");
                foreach (SettlementDefinition settlement in zone.Settlements)
                {
                    Require(_catalog.Continents.ContainsKey(settlement.ContinentId), where, "settlement " + settlement.Id + " has unknown continent " + settlement.ContinentId);
                    Require(InsideZone(zone, settlement.Center), where, "settlement outside zone: " + settlement.Id);
                }
                foreach (PortalDefinition portal in zone.Portals)
                {
                    Require(_catalog.Zones.ContainsKey(portal.ToZoneId), where, "portal to unknown zone " + portal.ToZoneId);
                }
                foreach (MobSpawnDefinition spawn in zone.Mobs)
                {
                    Require(_catalog.Mobs.ContainsKey(spawn.MobId), where, "unknown mob " + spawn.MobId);
                    Require(InsideZone(zone, spawn.Position), where, "mob spawn outside zone: " + spawn.MobId);
                }
                foreach (NodeSpawnDefinition node in zone.Nodes)
                {
                    Require(_catalog.Nodes.ContainsKey(node.NodeId), where, "unknown node " + node.NodeId);
                }
            }
            foreach (MobDefinition mob in _catalog.Mobs.Values)
            {
                string where = "mobs/" + mob.Id;
                Require(DiceExpression.TryParse(mob.Damage, out _), where, "invalid damage dice");
                Require(mob.LootTableId.Length == 0 || _catalog.LootTables.ContainsKey(mob.LootTableId), where, "unknown loot table " + mob.LootTableId);
                Require(mob.FactionId.Length == 0 || _catalog.Factions.ContainsKey(mob.FactionId), where, "unknown faction " + mob.FactionId);
                foreach (string abilityId in mob.Abilities)
                {
                    Require(_catalog.Abilities.ContainsKey(abilityId), where, "unknown ability " + abilityId);
                }
            }
            foreach (InstanceDefinition instance in _catalog.Instances.Values)
            {
                string where = "instances/" + instance.Id;
                Require(_catalog.Zones.TryGetValue(instance.ZoneId, out ZoneDefinition? zone) && zone.Kind == ZoneKind.Instance, where, "zone must exist and be of kind Instance");
                Require(instance.PartySizes.Count > 0 && instance.PartySizes.All(AllowedPartySizes.Contains), where, "party sizes must be from {1,3,5,25}");
                Require(instance.FinalLootTableId.Length == 0 || _catalog.LootTables.ContainsKey(instance.FinalLootTableId), where, "unknown loot table");
            }
            foreach (FactionDefinition faction in _catalog.Factions.Values)
            {
                foreach (string enemy in faction.Enemies)
                {
                    Require(_catalog.Factions.ContainsKey(enemy), "factions/" + faction.Id, "unknown enemy faction " + enemy);
                }
            }
        }

        private void ValidateNpcs()
        {
            foreach (NpcDefinition npc in _catalog.Npcs.Values)
            {
                string where = "npcs/" + npc.Id;
                Require(_catalog.Zones.ContainsKey(npc.ZoneId), where, "unknown zone " + npc.ZoneId);
                Require(npc.FactionId.Length == 0 || _catalog.Factions.ContainsKey(npc.FactionId), where, "unknown faction " + npc.FactionId);
                Require(npc.RaceId.Length == 0 || _catalog.Races.ContainsKey(npc.RaceId), where, "unknown race " + npc.RaceId);
                foreach (string questId in npc.Quests)
                {
                    Require(_catalog.Quests.ContainsKey(questId), where, "unknown quest " + questId);
                }
                RequireItems(where, npc.SellsItems);
                Require(npc.Schedule.All(entry => entry.FromHour >= 0 && entry.FromHour < HoursPerDay && entry.ToHour >= 0 && entry.ToHour <= HoursPerDay),
                    where, "schedule hours must be within 0..24");
            }
            List<string> trialQuests = _catalog.Classes.Values.Select(definition => definition.TrialQuestId).ToList();
            foreach (ZoneDefinition zone in _catalog.Zones.Values.Where(zone => zone.Kind == ZoneKind.StartZone))
            {
                bool hasMentor = _catalog.Npcs.Values.Any(npc => npc.ZoneId == zone.Id && npc.Role == NpcRole.Mentor
                                                                 && trialQuests.All(npc.Quests.Contains));
                Require(hasMentor, "zones/" + zone.Id, "start zone needs a mentor offering every class trial");
            }
        }

        private void ValidateQuests()
        {
            foreach (QuestDefinition quest in _catalog.Quests.Values)
            {
                string where = "quests/" + quest.Id;
                Require(quest.Steps.Count > 0, where, "quest has no steps");
                var stepIds = new HashSet<string>();
                foreach (QuestStep step in quest.Steps)
                {
                    Require(stepIds.Add(step.Id), where, "duplicate step " + step.Id);
                }
                foreach (QuestStep step in quest.Steps)
                {
                    string stepWhere = where + "/" + step.Id;
                    Require(step.Next.Length == 0 || stepIds.Contains(step.Next), stepWhere, "next step " + step.Next + " not found");
                    ValidateStepTarget(step, stepWhere);
                    if (step.Type == QuestStepType.Choice)
                    {
                        Require(step.Choices.Count > 0, stepWhere, "choice step without choices");
                    }
                    foreach (QuestChoice choice in step.Choices)
                    {
                        Require(choice.Next.Length == 0 || stepIds.Contains(choice.Next), stepWhere, "choice " + choice.Id + " leads to unknown step");
                        RequireFactions(stepWhere, choice.Reputation);
                    }
                }
                foreach (string required in quest.Requires)
                {
                    Require(_catalog.Quests.ContainsKey(required), where, "unknown prerequisite " + required);
                }
                RequireItems(where, quest.Rewards.Items.Select(item => item.ItemId));
                RequireFactions(where, quest.Rewards.Reputation);
                Require(quest.Rewards.UnlocksClassId.Length == 0 || _catalog.Classes.ContainsKey(quest.Rewards.UnlocksClassId), where, "unlocks unknown class");
            }
        }

        private void ValidateStepTarget(QuestStep step, string where)
        {
            switch (step.Type)
            {
                case QuestStepType.Kill:
                    Require(_catalog.Mobs.ContainsKey(step.TargetId), where, "unknown mob " + step.TargetId);
                    break;
                case QuestStepType.Collect:
                case QuestStepType.Craft:
                    Require(_catalog.Items.ContainsKey(step.TargetId), where, "unknown item " + step.TargetId);
                    break;
                case QuestStepType.Talk:
                    Require(_catalog.Npcs.ContainsKey(step.TargetId), where, "unknown npc " + step.TargetId);
                    break;
                case QuestStepType.Reach:
                    Require(_catalog.Zones.ContainsKey(step.TargetId), where, "unknown zone " + step.TargetId);
                    break;
            }
        }

        private void ValidateEconomy()
        {
            foreach (GatheringNodeDefinition node in _catalog.Nodes.Values)
            {
                string where = "nodes/" + node.Id;
                Require(_catalog.Professions.TryGetValue(node.ProfessionId, out ProfessionDefinition? profession) && profession.Kind == ProfessionKind.Gathering,
                    where, "node needs a gathering profession");
                Require(_catalog.LootTables.ContainsKey(node.LootTableId), where, "unknown loot table " + node.LootTableId);
            }
            foreach (CraftingRecipe recipe in _catalog.Recipes.Values)
            {
                string where = "recipes/" + recipe.Id;
                Require(_catalog.Professions.TryGetValue(recipe.ProfessionId, out ProfessionDefinition? profession) && profession.Kind == ProfessionKind.Crafting,
                    where, "recipe needs a crafting profession");
                RequireItems(where, recipe.Inputs.Select(input => input.ItemId).Append(recipe.Output.ItemId));
            }
            foreach (OfflineJobDefinition job in _catalog.OfflineJobs.Values)
            {
                string where = "offline_jobs/" + job.Id;
                Require(job.CycleMinutes > 0, where, "cycle_minutes must be positive");
                Require(job.ProfessionId.Length == 0 || _catalog.Professions.ContainsKey(job.ProfessionId), where, "unknown profession " + job.ProfessionId);
                switch (job.Kind)
                {
                    case OfflineJobKind.Gather:
                        Require(_catalog.Nodes.ContainsKey(job.TargetId), where, "unknown node " + job.TargetId);
                        break;
                    case OfflineJobKind.Hunt:
                        Require(_catalog.Mobs.TryGetValue(job.TargetId, out MobDefinition? game) && game.IsGame, where, "hunt target must be a game animal");
                        break;
                    case OfflineJobKind.Craft:
                        Require(_catalog.Recipes.ContainsKey(job.TargetId), where, "unknown recipe " + job.TargetId);
                        break;
                }
            }
            foreach (HouseModuleDefinition module in _catalog.HouseModules.Values)
            {
                RequireItems("housing/" + module.Id, module.Cost.Select(cost => cost.ItemId));
            }
            Require(_catalog.HouseModules.Count == 0 || _catalog.HouseModules.Values.Any(module => module.IsFoundation), "housing", "at least one foundation module is required");
        }

        private void ValidateStore()
        {
            foreach (CosmeticDefinition cosmetic in _catalog.Cosmetics.Values)
            {
                string where = "cosmetics/" + cosmetic.Id;
                Require(cosmetic.AddressableKey.Length > 0, where, "addressable_key is required");
                foreach (string raceId in cosmetic.RaceIds)
                {
                    Require(_catalog.Races.ContainsKey(raceId), where, "unknown race " + raceId);
                }
            }
            foreach (StoreSkuDefinition sku in _catalog.StoreSkus.Values)
            {
                string where = "store/" + sku.Id;
                foreach (string grant in sku.Grants)
                {
                    Require(_catalog.Cosmetics.ContainsKey(grant), where, "grants unknown cosmetic " + grant);
                }
                foreach (string platform in sku.PlatformProducts.Keys)
                {
                    Require(KnownStorePlatforms.Contains(platform), where, "unknown platform " + platform);
                }
                Require(sku.Kind != SkuKind.PremiumCurrency || sku.CrystalAmount > 0, where, "currency pack needs crystal_amount");
                Require(sku.PriceCents > 0 || sku.PriceCrystals > 0, where, "sku needs a price");
            }
        }

        private static bool InsideZone(ZoneDefinition zone, WorldPoint point)
        {
            return point.X >= 0 && point.Z >= 0 && point.X <= zone.SizeMeters && point.Z <= zone.SizeMeters;
        }

        private void RequireItems(string where, IEnumerable<string> itemIds)
        {
            foreach (string itemId in itemIds)
            {
                Require(_catalog.Items.ContainsKey(itemId), where, "unknown item " + itemId);
            }
        }

        private void RequireFactions(string where, IEnumerable<ReputationChange> changes)
        {
            foreach (ReputationChange change in changes)
            {
                Require(_catalog.Factions.ContainsKey(change.FactionId), where, "unknown faction " + change.FactionId);
            }
        }

        private void Require(bool condition, string where, string message)
        {
            if (!condition)
            {
                Error(where, message);
            }
        }

        private void Error(string where, string message)
        {
            _issues.Add(new ContentIssue(IssueSeverity.Error, where, message));
        }

        private void Warn(string where, string message)
        {
            _issues.Add(new ContentIssue(IssueSeverity.Warning, where, message));
        }
    }
}
