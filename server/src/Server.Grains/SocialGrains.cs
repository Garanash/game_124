using Realmforge.Server.Core;
using Realmforge.Server.Core.Social;

namespace Realmforge.Server.Grains;

public sealed class PartyGrain : Grain, IPartyGrain
{
    private readonly IPersistentState<PartyState> _state;

    public PartyGrain([PersistentState("party", GrainStorageNames.Main)] IPersistentState<PartyState> state)
    {
        _state = state;
    }

    private bool Exists => _state.RecordExists && !_state.State.IsDisbanded;

    public async Task<DomainResult> Create(Guid leaderId)
    {
        if (Exists)
        {
            return DomainResult.Fail("party.exists", "Группа уже создана.");
        }
        if ((await Character(leaderId).GetState()).PartyId.HasValue)
        {
            return DomainResult.Fail("party.already_in_party", "Сначала покиньте текущую группу.");
        }
        _state.State = PartyState.Create(this.GetPrimaryKey(), leaderId);
        await _state.WriteStateAsync();
        await Character(leaderId).SetParty(this.GetPrimaryKey());
        return DomainResult.Ok;
    }

    public async Task<DomainResult> Invite(Guid inviterId, Guid inviteeId)
    {
        if (!Exists)
        {
            return DomainResult.Fail("party.missing", "Группа не найдена.");
        }
        if ((await Character(inviteeId).GetState()).PartyId.HasValue)
        {
            return DomainResult.Fail("party.already_in_party", "Игрок уже в группе.");
        }
        return await Persist(_state.State.Invite(inviterId, inviteeId));
    }

    public async Task<DomainResult> Accept(Guid inviteeId)
    {
        DomainResult accepted = Exists ? _state.State.Accept(inviteeId) : DomainResult.Fail("party.missing", "Группа не найдена.");
        await Persist(accepted);
        if (accepted.Success)
        {
            await Character(inviteeId).SetParty(this.GetPrimaryKey());
        }
        return accepted;
    }

    public async Task Decline(Guid inviteeId)
    {
        if (Exists)
        {
            _state.State.Decline(inviteeId);
            await _state.WriteStateAsync();
        }
    }

    public async Task<DomainResult> Leave(Guid memberId)
    {
        DomainResult left = Exists ? _state.State.Leave(memberId) : DomainResult.Fail("party.missing", "Группа не найдена.");
        await Persist(left);
        if (left.Success)
        {
            await Character(memberId).SetParty(null);
        }
        return left;
    }

    public async Task<DomainResult> Kick(Guid leaderId, Guid memberId)
    {
        DomainResult kicked = Exists ? _state.State.Kick(leaderId, memberId) : DomainResult.Fail("party.missing", "Группа не найдена.");
        await Persist(kicked);
        if (kicked.Success)
        {
            await Character(memberId).SetParty(null);
        }
        return kicked;
    }

    public async Task<DomainResult> ConvertToRaid(Guid leaderId)
    {
        return await Persist(Exists ? _state.State.ConvertToRaid(leaderId) : DomainResult.Fail("party.missing", "Группа не найдена."));
    }

    public Task<PartyState?> GetState() => Task.FromResult(Exists ? _state.State : null);

    private ICharacterGrain Character(Guid characterId) => GrainFactory.GetGrain<ICharacterGrain>(characterId);

    private async Task<DomainResult> Persist(DomainResult result)
    {
        if (result.Success)
        {
            await _state.WriteStateAsync();
        }
        return result;
    }
}

public sealed class GuildGrain : Grain, IGuildGrain
{
    private const int MaxMessageOfTheDayLength = 200;

    private readonly IPersistentState<GuildState> _state;

    public GuildGrain([PersistentState("guild", GrainStorageNames.Main)] IPersistentState<GuildState> state)
    {
        _state = state;
    }

    private bool Exists => _state.RecordExists && _state.State.Members.Count > 0;

    public async Task<DomainResult> Found(string name, string tag, Guid founderId)
    {
        if (Exists)
        {
            return DomainResult.Fail("guild.exists", "Гильдия уже основана.");
        }
        DomainResult identity = GuildState.ValidateIdentity(name, tag);
        if (!identity.Success)
        {
            return identity;
        }
        if ((await Character(founderId).GetState()).GuildId.HasValue)
        {
            return DomainResult.Fail("guild.already_member", "Сначала покиньте текущую гильдию.");
        }
        bool reserved = await GrainFactory.GetGrain<IGuildRegistryGrain>(GrainKeys.Global).TryReserve(name, tag, this.GetPrimaryKey());
        if (!reserved)
        {
            return DomainResult.Fail("guild.name_taken", "Имя или тег уже заняты.");
        }
        _state.State = GuildState.Found(this.GetPrimaryKey(), name, tag, founderId);
        await _state.WriteStateAsync();
        await Character(founderId).SetGuild(this.GetPrimaryKey());
        return DomainResult.Ok;
    }

    public async Task<DomainResult> Invite(Guid inviterId, Guid inviteeId)
    {
        if (!Exists)
        {
            return DomainResult.Fail("guild.missing", "Гильдия не найдена.");
        }
        if ((await Character(inviteeId).GetState()).GuildId.HasValue)
        {
            return DomainResult.Fail("guild.already_member", "Игрок уже в гильдии.");
        }
        return await Persist(_state.State.Invite(inviterId, inviteeId));
    }

    public async Task<DomainResult> Accept(Guid inviteeId)
    {
        DomainResult accepted = Exists ? _state.State.Accept(inviteeId) : DomainResult.Fail("guild.missing", "Гильдия не найдена.");
        await Persist(accepted);
        if (accepted.Success)
        {
            await Character(inviteeId).SetGuild(this.GetPrimaryKey());
        }
        return accepted;
    }

    public async Task<DomainResult> SetRank(Guid actorId, Guid memberId, GuildRank rank)
    {
        return await Persist(Exists ? _state.State.SetRank(actorId, memberId, rank) : DomainResult.Fail("guild.missing", "Гильдия не найдена."));
    }

    public async Task<DomainResult> Leave(Guid memberId)
    {
        DomainResult left = Exists ? _state.State.Leave(memberId) : DomainResult.Fail("guild.missing", "Гильдия не найдена.");
        await Persist(left);
        if (!left.Success)
        {
            return left;
        }
        await Character(memberId).SetGuild(null);
        if (_state.State.Members.Count == 0)
        {
            await GrainFactory.GetGrain<IGuildRegistryGrain>(GrainKeys.Global).Release(this.GetPrimaryKey());
        }
        return left;
    }

    public async Task<DomainResult> SetMessageOfTheDay(Guid actorId, string text)
    {
        if (!Exists || !_state.State.HasRank(actorId, GuildRank.Officer))
        {
            return DomainResult.Fail("guild.rank", "Недостаточно прав.");
        }
        _state.State.MessageOfTheDay = ChatText.Sanitize(text) is { } clean && clean.Length <= MaxMessageOfTheDayLength ? clean : string.Empty;
        await _state.WriteStateAsync();
        return DomainResult.Ok;
    }

    public Task<GuildState?> GetState() => Task.FromResult(Exists ? _state.State : null);

    private ICharacterGrain Character(Guid characterId) => GrainFactory.GetGrain<ICharacterGrain>(characterId);

    private async Task<DomainResult> Persist(DomainResult result)
    {
        if (result.Success)
        {
            await _state.WriteStateAsync();
        }
        return result;
    }
}

public sealed class GuildRegistryState
{
    public Dictionary<string, Guid> Names { get; set; } = new();
    public Dictionary<string, Guid> Tags { get; set; } = new();
}

public sealed class GuildRegistryGrain : Grain, IGuildRegistryGrain
{
    private readonly IPersistentState<GuildRegistryState> _state;

    public GuildRegistryGrain([PersistentState("guild-registry", GrainStorageNames.Main)] IPersistentState<GuildRegistryState> state)
    {
        _state = state;
    }

    public async Task<bool> TryReserve(string name, string tag, Guid guildId)
    {
        string nameKey = name.Trim().ToUpperInvariant();
        string tagKey = tag.ToUpperInvariant();
        if (_state.State.Names.ContainsKey(nameKey) || _state.State.Tags.ContainsKey(tagKey))
        {
            return false;
        }
        _state.State.Names[nameKey] = guildId;
        _state.State.Tags[tagKey] = guildId;
        await _state.WriteStateAsync();
        return true;
    }

    public async Task Release(Guid guildId)
    {
        foreach (string key in _state.State.Names.Where(pair => pair.Value == guildId).Select(pair => pair.Key).ToList())
        {
            _state.State.Names.Remove(key);
        }
        foreach (string key in _state.State.Tags.Where(pair => pair.Value == guildId).Select(pair => pair.Key).ToList())
        {
            _state.State.Tags.Remove(key);
        }
        await _state.WriteStateAsync();
    }
}
