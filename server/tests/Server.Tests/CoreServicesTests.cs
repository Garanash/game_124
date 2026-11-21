using Realmforge.Rules.Definitions;
using Realmforge.Server.Core.Auth;
using Realmforge.Server.Core.Economy;
using Realmforge.Server.Core.Matchmaking;
using Realmforge.Server.Core.Social;
using Realmforge.Server.Core.Store;
using Xunit;

namespace Realmforge.Server.Tests;

public class AuthTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string TestSigningKey = "test-signing-key-that-is-long-enough-1234567890";

    [Fact]
    public void PasswordHashVerifiesOnlyTheOriginalPassword()
    {
        string hash = PasswordHasher.Hash("correct horse");

        Assert.True(PasswordHasher.Verify("correct horse", hash));
        Assert.False(PasswordHasher.Verify("wrong horse", hash));
        Assert.NotEqual(hash, PasswordHasher.Hash("correct horse"));
    }

    [Fact]
    public void ZoneTicketRoundTripsAndExpires()
    {
        var time = new ManualTimeProvider(Start);
        var tokens = new TokenService(new TokenOptions { SigningKey = TestSigningKey }, time);
        var ticket = new ZoneTicket(Guid.NewGuid(), Guid.NewGuid(), "start_ironhold", Guid.NewGuid());

        string token = tokens.IssueZoneTicket(ticket);

        Assert.Equal(ticket, tokens.ValidateZoneTicket(token));
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(tokens.ValidateZoneTicket(token));
    }

    [Fact]
    public void SessionTokenIsNotAcceptedAsZoneTicket()
    {
        var tokens = new TokenService(new TokenOptions { SigningKey = TestSigningKey }, new ManualTimeProvider(Start));

        Assert.Null(tokens.ValidateZoneTicket(tokens.IssueSession(Guid.NewGuid())));
    }
}

public class SocialTests
{
    [Fact]
    public void PartyHandsLeadershipOverWhenLeaderLeaves()
    {
        Guid leader = Guid.NewGuid(), member = Guid.NewGuid();
        PartyState party = PartyState.Create(Guid.NewGuid(), leader);

        Assert.True(party.Invite(leader, member).Success);
        Assert.True(party.Accept(member).Success);
        Assert.True(party.Leave(leader).Success);

        Assert.Equal(member, party.LeaderId);
        Assert.Single(party.Members);
    }

    [Fact]
    public void PartyIsCappedUntilConvertedToRaid()
    {
        Guid leader = Guid.NewGuid();
        PartyState party = PartyState.Create(Guid.NewGuid(), leader);
        for (int index = 1; index < PartyState.MaxPartySize; index++)
        {
            Guid member = Guid.NewGuid();
            party.Invite(leader, member);
            party.Accept(member);
        }

        Assert.Equal("party.full", party.Invite(leader, Guid.NewGuid()).Code);
        Assert.True(party.ConvertToRaid(leader).Success);
        Assert.True(party.Invite(leader, Guid.NewGuid()).Success);
    }

    [Fact]
    public void OnlyOfficersCanInviteToGuild()
    {
        Guid master = Guid.NewGuid(), recruit = Guid.NewGuid();
        GuildState guild = GuildState.Found(Guid.NewGuid(), "Iron Oath", "iron", master);
        guild.Invite(master, recruit);
        guild.Accept(recruit);

        Assert.Equal("IRON", guild.Tag);
        Assert.False(guild.Invite(recruit, Guid.NewGuid()).Success);
        Assert.False(GuildState.ValidateIdentity("x", "ab").Success);
    }

    [Fact]
    public void ChatRateLimiterDropsSustainedSpam()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new ChatRateLimiter(time);
        Guid sender = Guid.NewGuid();

        int accepted = Enumerable.Range(0, 20).Count(_ => limiter.TryConsume(sender));
        time.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(5, accepted);
        Assert.True(limiter.TryConsume(sender));
    }
}

public class MatchmakerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static QueueTicket Ticket(int members, int level, int waitedSeconds = 0)
    {
        return new QueueTicket(Guid.NewGuid(), Enumerable.Range(0, members).Select(_ => Guid.NewGuid()).ToList(), level, Start.AddSeconds(-waitedSeconds));
    }

    [Fact]
    public void FillsRunFromGroupsOfCloseLevel()
    {
        var matchmaker = new Matchmaker(runSize: 5);
        matchmaker.Enqueue(Ticket(3, 10));
        matchmaker.Enqueue(Ticket(2, 11));
        matchmaker.Enqueue(Ticket(1, 20));

        IReadOnlyList<MatchedGroup> runs = matchmaker.FormRuns(Start);

        Assert.Single(runs);
        Assert.Single(matchmaker.Queue);
    }

    [Fact]
    public void LevelWindowWidensWithWaitTime()
    {
        var matchmaker = new Matchmaker(runSize: 3);
        matchmaker.Enqueue(Ticket(2, 10, waitedSeconds: 120));
        matchmaker.Enqueue(Ticket(1, 16));

        Assert.Single(matchmaker.FormRuns(Start));
    }

    [Fact]
    public void RejectsDoubleQueue()
    {
        var matchmaker = new Matchmaker(runSize: 5);
        QueueTicket ticket = Ticket(2, 5);
        matchmaker.Enqueue(ticket);

        Assert.False(matchmaker.Enqueue(ticket with { TicketId = Guid.NewGuid() }).Success);
    }
}

public class EconomyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void SaleTakesCutAndRemovesListing()
    {
        var auction = new AuctionHouse();
        Guid seller = Guid.NewGuid();
        AuctionListing listing = auction.Create(seller, "Seller", "iron_ore", 10, 1000, TimeSpan.FromHours(12), Now).Value!;

        var sale = auction.Buy(listing.Id, Guid.NewGuid(), Now);

        Assert.Equal(950, sale.Value!.Gold);
        Assert.Equal(seller, sale.Value.RecipientId);
        Assert.Empty(auction.Listings);
        Assert.Equal(20, AuctionHouse.DepositFor(1000));
    }

    [Fact]
    public void ExpiredListingReturnsItemsToSeller()
    {
        var auction = new AuctionHouse();
        auction.Create(Guid.NewGuid(), "Seller", "iron_ore", 3, 100, TimeSpan.FromHours(1), Now);

        IReadOnlyList<AuctionSettlement> returned = auction.Expire(Now.AddHours(2));

        Assert.Equal(3, Assert.Single(returned).Quantity);
        Assert.False(auction.Create(Guid.NewGuid(), "Seller", "iron_ore", 1, 100, TimeSpan.FromHours(72), Now).Success);
    }

    [Fact]
    public void PlatformPurchaseIsIdempotent()
    {
        var ledger = new EntitlementLedger();
        var crystals = new StoreSkuDefinition { Id = "sku_crystals_500", Kind = SkuKind.PremiumCurrency, PriceCents = 499, CrystalAmount = 500 };

        Assert.True(ledger.ApplyPlatformPurchase("apple", "txn-1", crystals).Success);
        Assert.False(ledger.ApplyPlatformPurchase("apple", "txn-1", crystals).Success);

        Assert.Equal(500, ledger.Crystals);
    }

    [Fact]
    public void CrystalPurchaseRequiresBalance()
    {
        var ledger = new EntitlementLedger { Crystals = 100 };
        var skin = new StoreSkuDefinition { Id = "sku_skin", Kind = SkuKind.Cosmetic, PriceCrystals = 300, Grants = { "skin_ember_cloak" } };

        Assert.False(ledger.BuyWithCrystals(skin).Success);
        ledger.Crystals = 300;
        Assert.True(ledger.BuyWithCrystals(skin).Success);
        Assert.Contains("skin_ember_cloak", ledger.OwnedCosmetics);
    }
}
