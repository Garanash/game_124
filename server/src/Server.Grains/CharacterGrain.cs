using Orleans.Runtime;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Quests;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Characters;

namespace Realmforge.Server.Grains;

/// <summary>
/// Owner of a character's persistent state. Zone servers report gameplay facts here; the grain applies
/// the rules and persists. Offline jobs finish via a reminder, so the character progresses with nobody online.
/// </summary>
public sealed class CharacterGrain : Grain, ICharacterGrain, IRemindable
{
    private const string OfflineJobReminder = "offline-job";
    private static readonly TimeSpan MinimumReminderDelay = TimeSpan.FromMinutes(1);

    private readonly IPersistentState<CharacterState> _state;
    private readonly CharacterOperations _operations;
    private readonly TimeProvider _time;

    public CharacterGrain(
        [PersistentState("character", GrainStorageNames.Main)] IPersistentState<CharacterState> state,
        CharacterOperations operations,
        TimeProvider time)
    {
        _state = state;
        _operations = operations;
        _time = time;
    }

    private CharacterState Character => _state.State;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (_state.RecordExists && _operations.Migrate(Character))
        {
            await _state.WriteStateAsync();
        }
        await base.OnActivateAsync(cancellationToken);
    }

    public async Task Initialize(CharacterState state)
    {
        _state.State = state;
        await _state.WriteStateAsync();
    }

    public Task<CharacterState> GetState() => Task.FromResult(Character);

    public Task MarkDeleted() => Mutate(state => state.IsDeleted = true);

    public Task<RewardOutcome> ApplyEncounterRewards(long xp, LootResult loot) => Mutate(state => _operations.GrantRewards(state, xp, loot));

    public Task<IReadOnlyList<QuestProgressView>> OnQuestEvent(QuestEvent questEvent) =>
        Mutate<IReadOnlyList<QuestProgressView>>(state => _operations.OnQuestEvent(state, questEvent));

    public Task<DomainResult<List<QuestProgressView>>> ChooseQuestOption(string questId, string choiceId) =>
        Mutate(state => _operations.Choose(state, questId, choiceId));

    public Task<DomainResult<DialogueView>> Interact(string npcId, string optionId) => Mutate(state => _operations.Interact(state, npcId, optionId));

    public Task<DomainResult<RewardOutcome>> Gather(string nodeId)
    {
        var random = new SeededRandom(SeededRandom.CombineSeed(this.GetPrimaryKey().ToString(), UtcNow.Ticks));
        return Mutate(state => _operations.Gather(state, nodeId, random));
    }

    public Task<DomainResult<RewardOutcome>> Craft(string recipeId) => Mutate(state => _operations.Craft(state, recipeId));

    public Task<DomainResult> RaiseAbility(Ability ability) => Mutate(state => CharacterOperations.RaiseAbility(state, ability));

    public Task<DomainResult> Equip(Guid instanceId, EquipmentSlot slot) => Mutate(state => _operations.Equip(state, instanceId, slot));

    public Task<DomainResult> Unequip(EquipmentSlot slot, int x, int y, int rotation) =>
        Mutate(state => _operations.Unequip(state, slot, x, y, rotation));

    public Task<DomainResult> MoveBackpackItem(Guid instanceId, int x, int y, int rotation) =>
        Mutate(state => _operations.MoveBackpackItem(state, instanceId, x, y, rotation));

    public Task<DomainResult> PlaceFromBag(string itemId, int x, int y, int rotation) =>
        Mutate(state => _operations.PlaceFromBag(state, itemId, x, y, rotation, Guid.NewGuid()));

    public Task<DomainResult> StashToBag(Guid instanceId) => Mutate(state => _operations.StashToBag(state, instanceId));

    public Task<DomainResult> TakePerk(string perkId) => Mutate(state => _operations.TakePerk(state, perkId));

    public Task<DomainResult> TakeClassLevel(string classId) => Mutate(state => _operations.TakeClassLevel(state, classId));

    public async Task<DomainResult> StartOfflineJob(string jobId, TimeSpan duration)
    {
        DomainResult started = await Mutate(state => _operations.StartOfflineJob(state, jobId, duration, UtcNow));
        if (started.Success)
        {
            TimeSpan due = Character.OfflineJob!.EndsAtUtc - UtcNow;
            await this.RegisterOrUpdateReminder(OfflineJobReminder, due < MinimumReminderDelay ? MinimumReminderDelay : due, MinimumReminderDelay);
        }
        return started;
    }

    public async Task<DomainResult<RewardOutcome>> ClaimOfflineJob()
    {
        DomainResult<RewardOutcome> claimed = await Mutate(state => _operations.ClaimOfflineJob(state, UtcNow));
        await UnregisterOfflineReminder();
        return claimed;
    }

    public Task<RewardOutcome?> TakeOfflineReport()
    {
        return Mutate(state =>
        {
            RewardOutcome? report = state.LastOfflineReport;
            state.LastOfflineReport = null;
            return report;
        });
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != OfflineJobReminder)
        {
            return;
        }
        if (Character.OfflineJob != null && Character.OfflineJob.EndsAtUtc <= UtcNow)
        {
            await Mutate(state =>
            {
                DomainResult<RewardOutcome> claimed = _operations.ClaimOfflineJob(state, UtcNow);
                state.LastOfflineReport = claimed.Value;
            });
        }
        if (Character.OfflineJob == null)
        {
            await UnregisterOfflineReminder();
        }
    }

    public Task<DomainResult> ClaimHousePlot(string districtZoneId, int plotNumber) =>
        Mutate(state => _operations.ClaimHousePlot(state, districtZoneId, plotNumber));

    public Task<DomainResult> BuildHouseModule(string moduleId, int x, int z, bool rotated) =>
        Mutate(state => _operations.BuildHouseModule(state, moduleId, x, z, rotated, Guid.NewGuid()));

    public Task<DomainResult> DemolishHouseModule(Guid instanceId) => Mutate(state => _operations.DemolishHouseModule(state, instanceId));

    public async Task<DomainResult> EquipCosmetic(string cosmeticId)
    {
        var entitlements = await GrainFactory.GetGrain<IAccountGrain>(Character.Sheet.AccountId).GetEntitlements();
        var owned = new HashSet<string>(entitlements.OwnedCosmetics);
        return await Mutate(state => _operations.EquipCosmetic(state, cosmeticId, owned));
    }

    public Task SavePosition(string zoneId, float x, float z, int hp) =>
        Mutate(state => CharacterOperations.SavePosition(state, zoneId, x, z, hp));

    public Task ApplyPvpKill(int lawShift, int goodShift) => Mutate(state => CharacterOperations.ApplyAlignmentShift(state, lawShift, goodShift));

    public Task<long> ApplyPvpDeath(int goldLossPercent) => Mutate(state => CharacterOperations.LoseGoldPercent(state, goldLossPercent));

    public Task SetParty(Guid? partyId) => Mutate(state => state.PartyId = partyId);

    public Task SetGuild(Guid? guildId) => Mutate(state => state.GuildId = guildId);

    public Task SetPendingInstance(InstanceAssignment? assignment) => Mutate(state => state.PendingInstance = assignment);

    public Task<DomainResult> SpendGold(long amount) => Mutate(state => CharacterOperations.SpendGold(state, amount));

    public Task AddGold(long amount) => Mutate(state => state.Sheet.Gold += Math.Max(0, amount));

    public Task<DomainResult> TakeMaterials(string itemId, int quantity) => Mutate(state => _operations.TakeMaterials(state, itemId, quantity));

    public Task GrantItems(List<ItemQuantity> items) => Mutate(state => _operations.AddItems(state, items, new List<QuestProgressView>()));

    private async Task UnregisterOfflineReminder()
    {
        IGrainReminder? reminder = await this.GetReminder(OfflineJobReminder);
        if (reminder != null)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    private async Task<T> Mutate<T>(Func<CharacterState, T> change)
    {
        T result = change(Character);
        if (result is not IDomainOutcome { Success: false })
        {
            await _state.WriteStateAsync();
        }
        return result;
    }

    private Task Mutate(Action<CharacterState> change)
    {
        return Mutate<bool>(state =>
        {
            change(state);
            return true;
        });
    }
}
