using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Economy;

namespace Realmforge.Server.Grains;

public sealed class AuctionState
{
    public List<AuctionListing> Listings { get; set; } = new();
}

/// <summary>
/// Realm-wide auction house. Items and the deposit leave the seller before the listing exists, and the
/// buyer pays before the listing is removed, so a failure midway can only refund, never duplicate.
/// </summary>
public sealed class AuctionGrain : Grain, IAuctionGrain
{
    private static readonly TimeSpan ExpiryInterval = TimeSpan.FromMinutes(1);

    private readonly IPersistentState<AuctionState> _state;
    private readonly TimeProvider _time;
    private readonly AuctionHouse _house = new();

    public AuctionGrain([PersistentState("auction", GrainStorageNames.Main)] IPersistentState<AuctionState> state, TimeProvider time)
    {
        _state = state;
        _time = time;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _house.Restore(_state.State.Listings);
        this.RegisterGrainTimer(ExpireListings, new GrainTimerCreationOptions(ExpiryInterval, ExpiryInterval));
        return Task.CompletedTask;
    }

    public async Task<DomainResult<AuctionListing>> Create(Guid sellerCharacterId, string itemId, int quantity, long buyoutPrice, TimeSpan duration)
    {
        if (quantity <= 0 || buyoutPrice <= 0)
        {
            return DomainResult<AuctionListing>.Fail("auction.invalid", "Количество и цена должны быть положительными.");
        }
        ICharacterGrain seller = Character(sellerCharacterId);
        string sellerName = (await seller.GetState()).Sheet.Name;
        long deposit = AuctionHouse.DepositFor(buyoutPrice);
        DomainResult paid = await seller.SpendGold(deposit);
        if (!paid.Success)
        {
            return DomainResult<AuctionListing>.Fail(paid);
        }
        DomainResult taken = await seller.TakeMaterials(itemId, quantity);
        if (!taken.Success)
        {
            await seller.AddGold(deposit);
            return DomainResult<AuctionListing>.Fail(taken);
        }
        DomainResult<AuctionListing> created = _house.Create(sellerCharacterId, sellerName, itemId, quantity, buyoutPrice, duration, _time.GetUtcNow());
        if (!created.Success)
        {
            await seller.AddGold(deposit);
            await seller.GrantItems(new List<ItemQuantity> { new(itemId, quantity) });
            return created;
        }
        await Save();
        return created;
    }

    public async Task<DomainResult> Buy(Guid listingId, Guid buyerCharacterId)
    {
        AuctionListing? listing = _house.Listings.FirstOrDefault(candidate => candidate.Id == listingId);
        if (listing == null)
        {
            return DomainResult.Fail("auction.not_found", "Лот уже продан или снят.");
        }
        if (listing.SellerId == buyerCharacterId)
        {
            return DomainResult.Fail("auction.own_listing", "Нельзя купить свой лот.");
        }
        ICharacterGrain buyer = Character(buyerCharacterId);
        DomainResult paid = await buyer.SpendGold(listing.BuyoutPrice);
        if (!paid.Success)
        {
            return paid;
        }
        DomainResult<AuctionSettlement> sale = _house.Buy(listingId, buyerCharacterId, _time.GetUtcNow());
        if (!sale.Success)
        {
            await buyer.AddGold(listing.BuyoutPrice);
            return DomainResult.Fail(sale.Code, sale.Message);
        }
        await Save();
        await buyer.GrantItems(new List<ItemQuantity> { new(listing.ItemId, listing.Quantity) });
        await Character(sale.Value!.RecipientId).AddGold(sale.Value.Gold);
        return DomainResult.Ok;
    }

    public async Task<DomainResult> Cancel(Guid listingId, Guid sellerCharacterId)
    {
        DomainResult<AuctionSettlement> cancelled = _house.Cancel(listingId, sellerCharacterId);
        if (!cancelled.Success)
        {
            return DomainResult.Fail(cancelled.Code, cancelled.Message);
        }
        await Save();
        await Settle(cancelled.Value!);
        return DomainResult.Ok;
    }

    public Task<IReadOnlyList<AuctionListing>> Search(string? itemId, int page, int pageSize) =>
        Task.FromResult(_house.Search(itemId, page, pageSize, _time.GetUtcNow()));

    private async Task ExpireListings(CancellationToken cancellation)
    {
        IReadOnlyList<AuctionSettlement> expired = _house.Expire(_time.GetUtcNow());
        if (expired.Count == 0)
        {
            return;
        }
        await Save();
        foreach (AuctionSettlement settlement in expired)
        {
            await Settle(settlement);
        }
    }

    private async Task Settle(AuctionSettlement settlement)
    {
        if (settlement.ItemId != null)
        {
            await Character(settlement.RecipientId).GrantItems(new List<ItemQuantity> { new(settlement.ItemId, settlement.Quantity) });
        }
        if (settlement.Gold > 0)
        {
            await Character(settlement.RecipientId).AddGold(settlement.Gold);
        }
    }

    private async Task Save()
    {
        _state.State.Listings = _house.Listings.ToList();
        await _state.WriteStateAsync();
    }

    private ICharacterGrain Character(Guid characterId) => GrainFactory.GetGrain<ICharacterGrain>(characterId);
}
