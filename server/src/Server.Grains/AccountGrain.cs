using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Core.Store;

namespace Realmforge.Server.Grains;

public sealed class AccountState
{
    public List<Guid> Characters { get; set; } = new();
    public EntitlementLedger Ledger { get; set; } = new();
}

public sealed class AccountGrain : Grain, IAccountGrain
{
    private readonly IPersistentState<AccountState> _state;
    private readonly ContentCatalog _catalog;
    private readonly CharacterOperations _operations;
    private readonly TimeProvider _time;

    public AccountGrain(
        [PersistentState("account", GrainStorageNames.Main)] IPersistentState<AccountState> state,
        ContentCatalog catalog,
        CharacterOperations operations,
        TimeProvider time)
    {
        _state = state;
        _catalog = catalog;
        _operations = operations;
        _time = time;
    }

    private AccountState Account => _state.State;

    public async Task<DomainResult<CharacterSummary>> CreateCharacter(CreateCharacterRequest request)
    {
        if (Account.Characters.Count >= Account.Ledger.CharacterSlots)
        {
            return DomainResult<CharacterSummary>.Fail("account.no_slots", "Нет свободных слотов персонажей.");
        }
        Guid characterId = Guid.NewGuid();
        DomainResult<CharacterState> created = _operations.Create(characterId, this.GetPrimaryKey(), request, _time.GetUtcNow().UtcDateTime);
        if (!created.Success)
        {
            return DomainResult<CharacterSummary>.Fail(created.Code, created.Message);
        }
        await GrainFactory.GetGrain<ICharacterGrain>(characterId).Initialize(created.Value!);
        Account.Characters.Add(characterId);
        await _state.WriteStateAsync();
        return DomainResult<CharacterSummary>.Ok(CharacterOperations.Summarize(created.Value!));
    }

    public async Task<IReadOnlyList<CharacterSummary>> ListCharacters()
    {
        CharacterState[] states = await Task.WhenAll(Account.Characters.Select(id => GrainFactory.GetGrain<ICharacterGrain>(id).GetState()));
        return states.Where(state => state.Exists).Select(CharacterOperations.Summarize).ToList();
    }

    public Task<bool> OwnsCharacter(Guid characterId) => Task.FromResult(Account.Characters.Contains(characterId));

    public async Task<DomainResult> DeleteCharacter(Guid characterId)
    {
        if (!Account.Characters.Remove(characterId))
        {
            return DomainResult.Fail("account.not_owner", "Персонаж не принадлежит аккаунту.");
        }
        await GrainFactory.GetGrain<ICharacterGrain>(characterId).MarkDeleted();
        await _state.WriteStateAsync();
        return DomainResult.Ok;
    }

    public Task<EntitlementView> GetEntitlements() => Task.FromResult(EntitlementView.Of(Account.Ledger));

    public async Task<DomainResult> ApplyPlatformPurchase(string platform, string transactionId, string skuId)
    {
        if (!_catalog.StoreSkus.TryGetValue(skuId, out StoreSkuDefinition? sku))
        {
            return DomainResult.Fail("store.unknown_sku", "Неизвестный товар.");
        }
        return await PersistIfSuccessful(Account.Ledger.ApplyPlatformPurchase(platform, transactionId, sku));
    }

    public async Task<DomainResult> BuyWithCrystals(string skuId)
    {
        if (!_catalog.StoreSkus.TryGetValue(skuId, out StoreSkuDefinition? sku))
        {
            return DomainResult.Fail("store.unknown_sku", "Неизвестный товар.");
        }
        return await PersistIfSuccessful(Account.Ledger.BuyWithCrystals(sku));
    }

    private async Task<DomainResult> PersistIfSuccessful(DomainResult result)
    {
        if (result.Success)
        {
            await _state.WriteStateAsync();
        }
        return result;
    }
}
