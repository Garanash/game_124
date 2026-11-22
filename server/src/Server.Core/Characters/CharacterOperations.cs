using Realmforge.Rules;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Cosmetics;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Rules.Housing;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Professions;
using Realmforge.Rules.Quests;
using Realmforge.Rules.Stats;

namespace Realmforge.Server.Core.Characters;

/// <summary>
/// Character use-cases on top of the shared rules. Stateless apart from the content catalog, so the
/// CharacterGrain only loads state, calls one of these and persists.
/// </summary>
public sealed class CharacterOperations
{
    public const string OptionAccept = "accept:";
    public const string OptionChoose = "choice:";
    public const string OptionBuy = "buy:";
    public const string OptionLearn = "learn:";
    public const string OptionFarewell = "bye";
    private const int PercentBase = 100;

    private readonly ContentCatalog _catalog;

    public CharacterOperations(ContentCatalog catalog)
    {
        _catalog = catalog;
    }

    public static CharacterSummary Summarize(CharacterState state)
    {
        CharacterSheet sheet = state.Sheet;
        return new CharacterSummary(sheet.Id, sheet.Name, sheet.RaceId, sheet.Level, sheet.ZoneId, sheet.ClassLevels.Keys.ToList());
    }

    public DomainResult<CharacterState> Create(Guid characterId, Guid accountId, CreateCharacterRequest request, DateTime nowUtc)
    {
        RuleResult valid = CharacterRules.ValidateCreation(request, _catalog);
        if (!valid.Success)
        {
            return DomainResult<CharacterState>.Fail(DomainResult.From(valid));
        }
        CharacterSheet sheet = CharacterRules.Create(characterId, accountId, request, _catalog, nowUtc);
        var state = new CharacterState
        {
            Sheet = sheet,
            FactionId = FactionRules.HomeFaction(_catalog, sheet.RaceId) ?? string.Empty,
        };
        GiveStarterKit(state);
        return DomainResult<CharacterState>.Ok(state);
    }

    public bool RelocateIfZoneRemoved(CharacterState state) => CharacterRules.RelocateIfZoneRemoved(state.Sheet, _catalog);

    /// <summary>
    /// Every hero leaves home armed. The kit also teaches the backpack: the whetstone sharpens the sword
    /// when placed beside it, and the two daggers in the bag fuse into twin daggers once they touch.
    /// </summary>
    private void GiveStarterKit(CharacterState state)
    {
        foreach (string itemId in StarterBackpackItems.Where(_catalog.Items.ContainsKey))
        {
            state.Backpack.TryAutoPlace(Guid.NewGuid(), itemId, _catalog, out _);
        }
        foreach ((string itemId, int quantity) in StarterBagItems.Where(entry => _catalog.Items.ContainsKey(entry.ItemId)))
        {
            state.Materials.Add(itemId, quantity);
        }
    }

    private static readonly string[] StarterBackpackItems = { "short_sword", "leather_vest", "whetstone" };
    private static readonly (string ItemId, int Quantity)[] StarterBagItems = { ("dagger", 2), ("healing_potion", 2) };

    public RewardOutcome GrantRewards(CharacterState state, long xp, LootResult loot)
    {
        int levelBefore = state.Sheet.Level;
        long granted = CharacterRules.GrantXp(state.Sheet, xp);
        state.Sheet.Gold += loot.Gold;
        var quests = new List<QuestProgressView>();
        List<ItemGrant> items = AddItems(state, loot.Items, quests);
        return new RewardOutcome(granted, loot.Gold, items, state.Sheet.Level, state.Sheet.Level > levelBefore, quests);
    }

    public List<QuestProgressView> OnQuestEvent(CharacterState state, QuestEvent questEvent)
    {
        var updates = new List<QuestProgressView>();
        foreach (QuestProgress progress in state.ActiveQuests.ToList())
        {
            QuestDefinition quest = _catalog.Quests[progress.QuestId];
            QuestAdvance advance = QuestRules.OnEvent(quest, progress, questEvent);
            if (advance.Changed)
            {
                Settle(state, quest, progress, advance, updates);
            }
        }
        return updates;
    }

    public DomainResult<List<QuestProgressView>> Choose(CharacterState state, string questId, string choiceId)
    {
        QuestProgress? progress = state.ActiveQuests.FirstOrDefault(active => active.QuestId == questId);
        if (progress == null)
        {
            return DomainResult<List<QuestProgressView>>.Fail("quest.not_active", "Квест не взят.");
        }
        QuestDefinition quest = _catalog.Quests[questId];
        QuestAdvance advance = QuestRules.Choose(quest, progress, choiceId);
        if (!advance.Changed)
        {
            return DomainResult<List<QuestProgressView>>.Fail("quest.bad_choice", "Этот выбор сейчас недоступен.");
        }
        var updates = new List<QuestProgressView>();
        Settle(state, quest, progress, advance, updates);
        return DomainResult<List<QuestProgressView>>.Ok(updates);
    }

    /// <summary>Runs a dialogue option (empty = greeting) and returns the refreshed dialogue.</summary>
    public DomainResult<DialogueView> Interact(CharacterState state, string npcId, string optionId)
    {
        if (!_catalog.Npcs.TryGetValue(npcId, out NpcDefinition? npc))
        {
            return DomainResult<DialogueView>.Fail("npc.unknown", "Неизвестный NPC.");
        }
        string reply = RunOption(state, npc, optionId ?? string.Empty);
        return DomainResult<DialogueView>.Ok(new DialogueView(npc.Id, npc.Name, reply, OptionsFor(state, npc)));
    }

    public DomainResult<RewardOutcome> Gather(CharacterState state, string nodeId, IRandomSource random)
    {
        if (!_catalog.Nodes.TryGetValue(nodeId, out GatheringNodeDefinition? node))
        {
            return DomainResult<RewardOutcome>.Fail("gather.unknown", "Неизвестный ресурс.");
        }
        RuleResult allowed = ProfessionRules.CanGather(state.Sheet, node);
        if (!allowed.Success)
        {
            return DomainResult<RewardOutcome>.Fail(DomainResult.From(allowed));
        }
        int yieldBonus = Bonuses(state)[Stat.GatheringYieldPercent];
        LootResult loot = ProfessionRules.Gather(state.Sheet, node, _catalog, random, yieldBonus);
        return DomainResult<RewardOutcome>.Ok(GrantRewards(state, 0, loot));
    }

    public DomainResult<RewardOutcome> Craft(CharacterState state, string recipeId)
    {
        if (!_catalog.Recipes.TryGetValue(recipeId, out CraftingRecipe? recipe))
        {
            return DomainResult<RewardOutcome>.Fail("craft.unknown", "Неизвестный рецепт.");
        }
        RuleResult crafted = ProfessionRules.Craft(state.Sheet, recipe, state.Materials);
        if (!crafted.Success)
        {
            return DomainResult<RewardOutcome>.Fail(DomainResult.From(crafted));
        }
        var loot = new LootResult();
        loot.Add(recipe.Output.ItemId, recipe.Output.Quantity);
        RewardOutcome outcome = GrantRewards(state, 0, loot);
        var craftUpdates = OnQuestEvent(state, new QuestEvent(QuestStepType.Craft, recipe.Output.ItemId, recipe.Output.Quantity));
        return DomainResult<RewardOutcome>.Ok(outcome with { Quests = outcome.Quests.Concat(craftUpdates).ToList() });
    }

    public DomainResult Fuse(CharacterState state, string recipeId, Guid outputInstanceId)
    {
        return DomainResult.From(BackpackRules.Fuse(state.Backpack, recipeId, outputInstanceId, _catalog));
    }

    public DomainResult MoveBackpackItem(CharacterState state, Guid instanceId, int x, int y, int rotation)
    {
        return DomainResult.From(state.Backpack.Move(instanceId, x, y, rotation, _catalog));
    }

    /// <summary>Takes a single item from the bag and places it on the backpack grid.</summary>
    public DomainResult PlaceFromBag(CharacterState state, string itemId, int x, int y, int rotation, Guid instanceId)
    {
        if (state.Materials.Count(itemId) <= 0)
        {
            return DomainResult.Fail("bag.missing", "Предмета нет в сумке.");
        }
        RuleResult fits = state.Backpack.CanPlace(itemId, x, y, rotation, _catalog);
        if (!fits.Success)
        {
            return DomainResult.From(fits);
        }
        state.Materials.TryRemove(itemId, 1);
        state.Backpack.Place(instanceId, itemId, x, y, rotation, _catalog);
        return DomainResult.Ok;
    }

    public DomainResult StashToBag(CharacterState state, Guid instanceId)
    {
        PlacedItem? placed = state.Backpack.Find(instanceId);
        if (placed == null)
        {
            return DomainResult.Fail("backpack.missing", "Предмета нет в рюкзаке.");
        }
        state.Backpack.Remove(instanceId);
        state.Materials.Add(placed.ItemId, 1);
        return DomainResult.Ok;
    }

    public DomainResult TakePerk(CharacterState state, string perkId)
    {
        RuleResult allowed = PerkRules.CanTake(state.Sheet, perkId, _catalog);
        if (allowed.Success)
        {
            state.Sheet.Perks.Add(perkId);
        }
        return DomainResult.From(allowed);
    }

    public DomainResult TakeClassLevel(CharacterState state, string classId)
    {
        RuleResult allowed = CharacterRules.CanTakeClassLevel(state.Sheet, classId, _catalog);
        if (allowed.Success)
        {
            CharacterRules.TakeClassLevel(state.Sheet, classId, _catalog);
        }
        return DomainResult.From(allowed);
    }

    public DomainResult StartOfflineJob(CharacterState state, string jobId, TimeSpan duration, DateTime nowUtc)
    {
        if (state.OfflineJob != null)
        {
            return DomainResult.Fail("offline.busy", "Персонаж уже занят делом.");
        }
        if (!_catalog.OfflineJobs.TryGetValue(jobId, out OfflineJobDefinition? job))
        {
            return DomainResult.Fail("offline.unknown", "Неизвестное занятие.");
        }
        RuleResult allowed = OfflineJobs.CanStart(state.Sheet, job, _catalog, duration);
        if (allowed.Success)
        {
            state.OfflineJob = OfflineJobs.Start(state.Sheet, job, nowUtc, duration);
        }
        return DomainResult.From(allowed);
    }

    /// <summary>Claiming early pays out only the cycles completed so far and frees the character.</summary>
    public DomainResult<RewardOutcome> ClaimOfflineJob(CharacterState state, DateTime nowUtc)
    {
        if (state.OfflineJob == null)
        {
            return DomainResult<RewardOutcome>.Fail("offline.none", "Нет активного занятия.");
        }
        OfflineJobResult result = OfflineJobs.Simulate(state.Sheet, state.OfflineJob, _catalog, state.Materials, nowUtc);
        state.OfflineJob = null;
        int restBonus = Bonuses(state)[Stat.RestXpPercent];
        long characterXp = result.Xp * (PercentBase + restBonus) / PercentBase;
        return DomainResult<RewardOutcome>.Ok(GrantRewards(state, characterXp, result.Loot));
    }

    public DomainResult ClaimHousePlot(CharacterState state, string districtZoneId, int plotNumber)
    {
        if (state.House != null)
        {
            return DomainResult.Fail("house.owned", "У персонажа уже есть участок.");
        }
        if (!_catalog.Zones.TryGetValue(districtZoneId, out ZoneDefinition? zone) || zone.Kind != ZoneKind.Housing)
        {
            return DomainResult.Fail("house.district", "Здесь нельзя строить.");
        }
        state.House = new HousePlot { DistrictZoneId = districtZoneId, PlotNumber = plotNumber };
        return DomainResult.Ok;
    }

    public DomainResult BuildHouseModule(CharacterState state, string moduleId, int x, int z, bool rotated, Guid instanceId)
    {
        if (state.House == null)
        {
            return DomainResult.Fail("house.none", "Сначала получите участок.");
        }
        return DomainResult.From(state.House.Build(instanceId, moduleId, x, z, rotated, _catalog, state.Materials));
    }

    public DomainResult DemolishHouseModule(CharacterState state, Guid instanceId)
    {
        bool removed = state.House != null && state.House.Demolish(instanceId, _catalog, state.Materials);
        return removed ? DomainResult.Ok : DomainResult.Fail("house.module_missing", "Постройка не найдена.");
    }

    public DomainResult EquipCosmetic(CharacterState state, string cosmeticId, ISet<string> ownedCosmetics)
    {
        return DomainResult.From(AppearanceRules.Equip(state.Appearance, cosmeticId, state.Sheet.RaceId, ownedCosmetics, _catalog));
    }

    public static void ApplyAlignmentShift(CharacterState state, int lawShift, int goodShift)
    {
        state.Sheet.SetAlignment(state.Sheet.GetAlignment().Shift(lawShift, goodShift));
    }

    public static long LoseGoldPercent(CharacterState state, int percent)
    {
        long lost = state.Sheet.Gold * percent / PercentBase;
        state.Sheet.Gold -= lost;
        return lost;
    }

    public static void SavePosition(CharacterState state, string zoneId, float x, float z, int hp)
    {
        state.Sheet.ZoneId = zoneId;
        state.Sheet.PositionX = x;
        state.Sheet.PositionZ = z;
        state.Sheet.CurrentHp = hp;
    }

    public DomainResult TakeMaterials(CharacterState state, string itemId, int quantity)
    {
        return state.Materials.TryRemove(itemId, quantity)
            ? DomainResult.Ok
            : DomainResult.Fail("bag.insufficient", "Недостаточно предметов.");
    }

    public static DomainResult SpendGold(CharacterState state, long amount)
    {
        if (amount < 0 || state.Sheet.Gold < amount)
        {
            return DomainResult.Fail("gold.insufficient", "Недостаточно золота.");
        }
        state.Sheet.Gold -= amount;
        return DomainResult.Ok;
    }

    public StatBlock Bonuses(CharacterState state)
    {
        StatBlock bonuses = CombatantFactory.CharacterBonuses(state.Sheet, state.Backpack, _catalog);
        if (state.House != null)
        {
            bonuses.Merge(state.House.Bonuses(_catalog));
        }
        return bonuses;
    }

    public List<ItemGrant> AddItems(CharacterState state, IEnumerable<ItemQuantity> items, List<QuestProgressView> questUpdates)
    {
        var granted = new List<ItemGrant>();
        foreach (ItemQuantity item in items.Where(item => item.Quantity > 0))
        {
            for (int copy = 0; copy < item.Quantity && IsBackpackItem(item.ItemId); copy++)
            {
                if (!state.Backpack.TryAutoPlace(Guid.NewGuid(), item.ItemId, _catalog, out _))
                {
                    state.Materials.Add(item.ItemId, 1);
                }
            }
            if (!IsBackpackItem(item.ItemId))
            {
                state.Materials.Add(item.ItemId, item.Quantity);
            }
            granted.Add(new ItemGrant(item.ItemId, item.Quantity));
            questUpdates.AddRange(OnQuestEvent(state, new QuestEvent(QuestStepType.Collect, item.ItemId, item.Quantity)));
        }
        return granted;
    }

    private bool IsBackpackItem(string itemId)
    {
        return _catalog.Items.TryGetValue(itemId, out ItemDefinition? item) && item.MaxStack <= 1;
    }

    private void Settle(CharacterState state, QuestDefinition quest, QuestProgress progress, QuestAdvance advance, List<QuestProgressView> updates)
    {
        QuestRules.ApplyToSheet(state.Sheet, quest, advance);
        if (advance.Completed)
        {
            state.ActiveQuests.Remove(progress);
            AddItems(state, quest.Rewards.Items, updates);
        }
        updates.Add(new QuestProgressView(quest.Id, progress.StepId, progress.Count, advance.Completed));
    }

    private string RunOption(CharacterState state, NpcDefinition npc, string optionId)
    {
        if (optionId.StartsWith(OptionAccept, StringComparison.Ordinal))
        {
            return AcceptQuest(state, npc, optionId[OptionAccept.Length..]);
        }
        if (optionId.StartsWith(OptionChoose, StringComparison.Ordinal))
        {
            string[] parts = optionId[OptionChoose.Length..].Split(':');
            DomainResult<List<QuestProgressView>> chosen = parts.Length == 2 ? Choose(state, parts[0], parts[1]) : DomainResult<List<QuestProgressView>>.Fail("quest.bad_choice", "Неверный выбор.");
            return chosen.Success ? "Да будет так." : chosen.Message;
        }
        if (optionId.StartsWith(OptionBuy, StringComparison.Ordinal))
        {
            return Buy(state, npc, optionId[OptionBuy.Length..]);
        }
        if (optionId.StartsWith(OptionLearn, StringComparison.Ordinal))
        {
            DomainResult learned = npc.Role == NpcRole.Mentor ? TakeClassLevel(state, optionId[OptionLearn.Length..]) : DomainResult.Fail("npc.not_mentor", "Я не наставник.");
            return learned.Success ? "Ты готов. Ступай и помни, чему учился." : learned.Message;
        }
        return optionId == OptionFarewell ? "Доброго пути." : Greeting(state, npc);
    }

    private string Greeting(CharacterState state, NpcDefinition npc)
    {
        if (_catalog.Factions.TryGetValue(npc.FactionId, out FactionDefinition? faction))
        {
            Attitude attitude = FactionRules.AttitudeOf(faction, state.Sheet);
            if (!FactionRules.WillTrade(attitude))
            {
                return "Я не стану с тобой говорить.";
            }
        }
        return npc.Role switch
        {
            NpcRole.Mentor => "Многие приходят ко мне за наукой. Докажи, что достоин.",
            NpcRole.Vendor => "Взгляни на мой товар.",
            NpcRole.Guard => "Проходи, не задерживайся.",
            _ => "Приветствую, путник.",
        };
    }

    private string AcceptQuest(CharacterState state, NpcDefinition npc, string questId)
    {
        if (!npc.Quests.Contains(questId) || !_catalog.Quests.TryGetValue(questId, out QuestDefinition? quest))
        {
            return "У меня нет такого поручения.";
        }
        RuleResult allowed = QuestRules.CanAccept(state.Sheet, quest, state.ActiveQuests);
        if (!allowed.Success)
        {
            return allowed.Message;
        }
        state.ActiveQuests.Add(QuestRules.Start(quest));
        return "Рассчитываю на тебя.";
    }

    private string Buy(CharacterState state, NpcDefinition npc, string itemId)
    {
        if (!npc.SellsItems.Contains(itemId) || !_catalog.Items.TryGetValue(itemId, out ItemDefinition? item))
        {
            return "Этого у меня нет.";
        }
        long price = PriceFor(state, npc, item);
        if (!SpendGold(state, price).Success)
        {
            return "Тебе не хватает золота.";
        }
        AddItems(state, new[] { new ItemQuantity(itemId, 1) }, new List<QuestProgressView>());
        return "Хороший выбор.";
    }

    private long PriceFor(CharacterState state, NpcDefinition npc, ItemDefinition item)
    {
        int percent = _catalog.Factions.TryGetValue(npc.FactionId, out FactionDefinition? faction)
            ? FactionRules.PricePercent(FactionRules.AttitudeOf(faction, state.Sheet))
            : PercentBase;
        return Math.Max(1, (long)item.Value * percent / PercentBase);
    }

    private IReadOnlyList<DialogueOption> OptionsFor(CharacterState state, NpcDefinition npc)
    {
        var options = new List<DialogueOption>();
        foreach (string questId in npc.Quests.Where(_catalog.Quests.ContainsKey))
        {
            QuestDefinition quest = _catalog.Quests[questId];
            QuestProgress? active = state.ActiveQuests.FirstOrDefault(progress => progress.QuestId == questId);
            QuestStep? step = active == null ? null : QuestRules.CurrentStep(quest, active);
            if (step?.Type == QuestStepType.Choice)
            {
                options.AddRange(step.Choices.Select(choice => new DialogueOption(OptionChoose + questId + ":" + choice.Id, choice.Text)));
            }
            else if (active == null && QuestRules.CanAccept(state.Sheet, quest, state.ActiveQuests).Success)
            {
                options.Add(new DialogueOption(OptionAccept + questId, "Взять задание: " + quest.Name));
            }
        }
        if (npc.Role == NpcRole.Mentor)
        {
            options.AddRange(_catalog.Classes.Values
                .Where(definition => CharacterRules.CanTakeClassLevel(state.Sheet, definition.Id, _catalog).Success)
                .Select(definition => new DialogueOption(OptionLearn + definition.Id, "Обучиться: " + definition.Name)));
        }
        foreach (string itemId in npc.SellsItems.Where(_catalog.Items.ContainsKey))
        {
            ItemDefinition item = _catalog.Items[itemId];
            options.Add(new DialogueOption(OptionBuy + itemId, $"Купить {item.Name} — {PriceFor(state, npc, item)} з."));
        }
        options.Add(new DialogueOption(OptionFarewell, "Прощай."));
        return options;
    }
}
