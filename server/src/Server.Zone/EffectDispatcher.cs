using Realmforge.Protocol;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Rules.Quests;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Auth;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Grains;
using Realmforge.Server.Hosting;
using Realmforge.Server.World;

namespace Realmforge.Server.Zone;

/// <summary>Everything the dispatcher may touch on the game loop thread; calls are always marshalled there.</summary>
public interface IZoneOutput
{
    void Post(Action action);
    void Send(Guid characterId, IServerMessage message);
    void RefreshCharacter(Guid characterId, CharacterState state);
    Guid? AccountOf(Guid characterId);
}

public sealed record ChatEnvelope(string Channel, string SenderName, string Text, double ServerTime);

/// <summary>
/// Executes simulation side effects against grains. Effects of one character run strictly in order
/// (a kill must be rewarded before the quest turn-in that depends on it); different characters run in parallel.
/// </summary>
public sealed class EffectDispatcher
{
    private readonly IGrainFactory _grains;
    private readonly ContentCatalog _catalog;
    private readonly TokenService _tokens;
    private readonly IMessageBus _bus;
    private readonly IZoneOutput _output;
    private readonly ILogger _logger;
    private readonly Dictionary<Guid, Task> _chains = new();
    private readonly IRandomSource _random = new SeededRandom((ulong)Environment.TickCount64);

    public EffectDispatcher(IGrainFactory grains, ContentCatalog catalog, TokenService tokens, IMessageBus bus, IZoneOutput output, ILogger logger)
    {
        _grains = grains;
        _catalog = catalog;
        _tokens = tokens;
        _bus = bus;
        _output = output;
        _logger = logger;
    }

    /// <summary>Must be called from the game loop thread.</summary>
    public void Dispatch(ZoneEffect effect)
    {
        Task previous = _chains.GetValueOrDefault(effect.CharacterId) ?? Task.CompletedTask;
        _chains[effect.CharacterId] = previous.ContinueWith(_ => Run(effect), TaskScheduler.Default).Unwrap();
    }

    public void Forget(Guid characterId) => _chains.Remove(characterId);

    private async Task Run(ZoneEffect effect)
    {
        try
        {
            await Execute(effect);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Effect {Effect} failed for {Character}", effect.GetType().Name, effect.CharacterId);
        }
    }

    private Task Execute(ZoneEffect effect) => effect switch
    {
        GrantEncounterRewards rewards => GrantRewards(rewards),
        ReportQuestEvent quest => ReportQuest(quest.CharacterId, quest.Event),
        NodeGathered gathered => Gather(gathered),
        NpcInteracted interaction => Interact(interaction),
        QuestChoiceMade choice => Choose(choice),
        SavePosition save => Character(save.CharacterId).SavePosition(save.ZoneId, save.X, save.Z, save.Hp),
        TransferToZone transfer => Transfer(transfer),
        PublishChat chat => _bus.PublishAsync(chat.Subject, new ChatEnvelope(chat.Channel, chat.SenderName, chat.Text, DateTimeOffset.UtcNow.ToUnixTimeSeconds())),
        PvpKill kill => SettlePvpKill(kill),
        InstanceBossDefeated boss => GrantInstanceLoot(boss),
        CharacterDied => Task.CompletedTask,
        _ => throw new NotSupportedException(effect.GetType().Name),
    };

    private async Task GrantRewards(GrantEncounterRewards rewards)
    {
        ICharacterGrain character = Character(rewards.CharacterId);
        RewardOutcome outcome = await character.ApplyEncounterRewards(rewards.Xp, rewards.Loot);
        var quests = outcome.Quests.ToList();
        foreach (string mobId in rewards.KilledMobIds)
        {
            quests.AddRange(await character.OnQuestEvent(new QuestEvent(QuestStepType.Kill, mobId)));
        }
        await Notify(rewards.CharacterId, outcome with { Quests = quests });
    }

    private async Task ReportQuest(Guid characterId, QuestEvent questEvent)
    {
        IReadOnlyList<QuestProgressView> updates = await Character(characterId).OnQuestEvent(questEvent);
        if (updates.Count > 0)
        {
            await Notify(characterId, new RewardOutcome(0, 0, Array.Empty<ItemGrant>(), 0, false, updates));
        }
    }

    private async Task Gather(NodeGathered gathered)
    {
        DomainResult<RewardOutcome> result = await Character(gathered.CharacterId).Gather(gathered.NodeId);
        if (!result.Success)
        {
            SendError(gathered.CharacterId, result.Code, result.Message);
            return;
        }
        await Notify(gathered.CharacterId, result.Value!);
    }

    private async Task Interact(NpcInteracted interaction)
    {
        DomainResult<DialogueView> result = await Character(interaction.CharacterId).Interact(interaction.NpcId, interaction.OptionId);
        if (!result.Success)
        {
            SendError(interaction.CharacterId, result.Code, result.Message);
            return;
        }
        DialogueView view = result.Value!;
        Send(interaction.CharacterId, new DialogueMessage
        {
            NpcEntityId = interaction.NpcEntityId,
            NpcName = view.NpcName,
            Text = view.Text,
            Options = view.Options.Select(option => new DialogueOptionDto { Id = option.Id, Text = option.Text }).ToArray(),
        });
        await Refresh(interaction.CharacterId);
    }

    private async Task Choose(QuestChoiceMade choice)
    {
        DomainResult<List<QuestProgressView>> result = await Character(choice.CharacterId).ChooseQuestOption(choice.QuestId, choice.ChoiceId);
        if (!result.Success)
        {
            SendError(choice.CharacterId, result.Code, result.Message);
            return;
        }
        await Notify(choice.CharacterId, new RewardOutcome(0, 0, Array.Empty<ItemGrant>(), 0, false, result.Value!));
    }

    /// <summary>The arrival point is saved before the ticket is issued, so a crash in between never strands the character.</summary>
    private async Task Transfer(TransferToZone transfer)
    {
        ICharacterGrain character = Character(transfer.CharacterId);
        CharacterState state = await character.GetState();
        await character.SavePosition(transfer.ZoneId, transfer.ArrivalX, transfer.ArrivalZ, state.Sheet.CurrentHp);
        ZoneServerInfo? server = await _grains.GetGrain<IZoneDirectoryGrain>(GrainKeys.Global).ResolveZone(transfer.ZoneId);
        var account = new TaskCompletionSource<Guid?>();
        _output.Post(() => account.SetResult(_output.AccountOf(transfer.CharacterId)));
        Guid? accountId = await account.Task;
        if (server == null || accountId == null)
        {
            SendError(transfer.CharacterId, "zone.unavailable", "Зона недоступна, вы вернётесь туда при следующем входе.");
            return;
        }
        string ticket = _tokens.IssueZoneTicket(new ZoneTicket(accountId.Value, transfer.CharacterId, transfer.ZoneId, null));
        Send(transfer.CharacterId, new ZoneTransferMessage { ZoneId = transfer.ZoneId, Host = server.Host, Port = server.UdpPort, Ticket = ticket });
    }

    private async Task SettlePvpKill(PvpKill kill)
    {
        await Character(kill.CharacterId).ApplyPvpKill(kill.KillerLawShift, kill.KillerGoodShift);
        long lost = await Character(kill.VictimCharacterId).ApplyPvpDeath(kill.VictimGoldLossPercent);
        if (lost > 0)
        {
            await Character(kill.CharacterId).AddGold(lost);
        }
        await Refresh(kill.CharacterId);
        await Refresh(kill.VictimCharacterId);
    }

    /// <summary>The final chest is rolled per player; the pending assignment is cleared so the next login lands in the open world.</summary>
    private async Task GrantInstanceLoot(InstanceBossDefeated boss)
    {
        ICharacterGrain character = Character(boss.CharacterId);
        CharacterState state = await character.GetState();
        InstanceDefinition? instance = state.PendingInstance == null ? null : _catalog.Instances.GetValueOrDefault(state.PendingInstance.InstanceId);
        LootResult loot = instance != null && _catalog.LootTables.TryGetValue(instance.FinalLootTableId, out LootTable? table)
            ? LootRoller.Roll(table, _random)
            : new LootResult();
        RewardOutcome outcome = await character.ApplyEncounterRewards(0, loot);
        await character.SetPendingInstance(null);
        await Notify(boss.CharacterId, outcome);
    }

    private async Task Notify(Guid characterId, RewardOutcome outcome)
    {
        if (outcome.Items.Count > 0 || outcome.Gold > 0)
        {
            Send(characterId, new InventoryChangedMessage
            {
                Added = outcome.Items.Select(item => new ItemStackDto { ItemId = item.ItemId, Quantity = item.Quantity }).ToArray(),
                Gold = outcome.Gold,
            });
        }
        foreach (QuestProgressView quest in outcome.Quests)
        {
            Send(characterId, new QuestUpdateMessage { QuestId = quest.QuestId, StepId = quest.StepId, Count = quest.Count, Completed = quest.Completed });
        }
        await Refresh(characterId);
    }

    private async Task Refresh(Guid characterId)
    {
        CharacterState state = await Character(characterId).GetState();
        _output.Post(() => _output.RefreshCharacter(characterId, state));
    }

    private void Send(Guid characterId, IServerMessage message) => _output.Post(() => _output.Send(characterId, message));

    private void SendError(Guid characterId, string code, string text) => Send(characterId, new ErrorMessage { Code = code, Text = text });

    private ICharacterGrain Character(Guid characterId) => _grains.GetGrain<ICharacterGrain>(characterId);
}
