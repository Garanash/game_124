namespace Realmforge.Server.Core.Economy;

public sealed record AuctionListing(
    Guid Id,
    Guid SellerId,
    string SellerName,
    string ItemId,
    int Quantity,
    long BuyoutPrice,
    DateTimeOffset ExpiresAt);

/// <summary>Proceeds the caller must credit: items back to a seller on expiry/cancel, gold on sale.</summary>
public sealed record AuctionSettlement(Guid ListingId, Guid RecipientId, string? ItemId, int Quantity, long Gold);

/// <summary>Buyout-only auction house. A deposit and a sale cut act as gold sinks for the economy.</summary>
public sealed class AuctionHouse
{
    public const int DepositPercent = 2;
    public const int SaleCutPercent = 5;
    public const int MaxListingsPerSeller = 50;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(48);

    private readonly Dictionary<Guid, AuctionListing> _listings = new();

    public IReadOnlyCollection<AuctionListing> Listings => _listings.Values;

    public static long DepositFor(long buyoutPrice) => Math.Max(1, buyoutPrice * DepositPercent / 100);

    public DomainResult<AuctionListing> Create(Guid sellerId, string sellerName, string itemId, int quantity, long buyoutPrice, TimeSpan duration, DateTimeOffset now)
    {
        if (quantity <= 0 || buyoutPrice <= 0)
        {
            return DomainResult<AuctionListing>.Fail("auction.invalid", "Quantity and price must be positive.");
        }
        if (duration <= TimeSpan.Zero || duration > MaxDuration)
        {
            return DomainResult<AuctionListing>.Fail("auction.duration", "Duration must be up to 48 hours.");
        }
        if (_listings.Values.Count(listing => listing.SellerId == sellerId) >= MaxListingsPerSeller)
        {
            return DomainResult<AuctionListing>.Fail("auction.limit", "Too many active listings.");
        }
        var created = new AuctionListing(Guid.NewGuid(), sellerId, sellerName, itemId, quantity, buyoutPrice, now + duration);
        _listings[created.Id] = created;
        return DomainResult<AuctionListing>.Ok(created);
    }

    /// <summary>Returns the seller's gold settlement; the caller debits the buyer and grants the item.</summary>
    public DomainResult<AuctionSettlement> Buy(Guid listingId, Guid buyerId, DateTimeOffset now)
    {
        if (!_listings.TryGetValue(listingId, out AuctionListing? listing) || listing.ExpiresAt <= now)
        {
            return DomainResult<AuctionSettlement>.Fail("auction.not_found", "Listing is gone.");
        }
        if (listing.SellerId == buyerId)
        {
            return DomainResult<AuctionSettlement>.Fail("auction.own_listing", "Cannot buy your own listing.");
        }
        _listings.Remove(listingId);
        long proceeds = listing.BuyoutPrice - listing.BuyoutPrice * SaleCutPercent / 100;
        return DomainResult<AuctionSettlement>.Ok(new AuctionSettlement(listingId, listing.SellerId, null, 0, proceeds));
    }

    public DomainResult<AuctionSettlement> Cancel(Guid listingId, Guid sellerId)
    {
        if (!_listings.TryGetValue(listingId, out AuctionListing? listing) || listing.SellerId != sellerId)
        {
            return DomainResult<AuctionSettlement>.Fail("auction.not_found", "Listing not found.");
        }
        _listings.Remove(listingId);
        return DomainResult<AuctionSettlement>.Ok(new AuctionSettlement(listingId, sellerId, listing.ItemId, listing.Quantity, 0));
    }

    public IReadOnlyList<AuctionSettlement> Expire(DateTimeOffset now)
    {
        List<AuctionListing> expired = _listings.Values.Where(listing => listing.ExpiresAt <= now).ToList();
        foreach (AuctionListing listing in expired)
        {
            _listings.Remove(listing.Id);
        }
        return expired.Select(listing => new AuctionSettlement(listing.Id, listing.SellerId, listing.ItemId, listing.Quantity, 0)).ToList();
    }

    public IReadOnlyList<AuctionListing> Search(string? itemId, int page, int pageSize, DateTimeOffset now)
    {
        return _listings.Values
            .Where(listing => listing.ExpiresAt > now && (string.IsNullOrEmpty(itemId) || listing.ItemId == itemId))
            .OrderBy(listing => listing.BuyoutPrice / (double)listing.Quantity)
            .Skip(Math.Max(0, page) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    public void Restore(IEnumerable<AuctionListing> listings)
    {
        _listings.Clear();
        foreach (AuctionListing listing in listings)
        {
            _listings[listing.Id] = listing;
        }
    }
}
