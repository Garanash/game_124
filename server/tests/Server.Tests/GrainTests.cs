using Microsoft.Extensions.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Core.Matchmaking;
using Realmforge.Server.Grains;
using Xunit;

namespace Realmforge.Server.Tests;

public sealed class ClusterFixture : IDisposable
{
    public ClusterFixture()
    {
        var builder = new TestClusterBuilder(initialSilosCount: 1);
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public TestCluster Cluster { get; }

    public void Dispose()
    {
        Cluster.StopAllSilos();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddRealmforgeGrains(new ConfigurationBuilder().Build(), SharedContent.Catalog);
            siloBuilder.UseInMemoryReminderService();
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.Services.AddSerializer(serializer => serializer.AddRealmforgeSerializers());
        }
    }
}

internal static class SharedContent
{
    public static readonly ContentCatalog Catalog = ContentSource.Load();
}

public class GrainTests : IClassFixture<ClusterFixture>
{
    private static readonly int[] StandardPointBuy = { 15, 15, 15, 8, 8, 8 };
    private readonly IGrainFactory _grains;

    public GrainTests(ClusterFixture fixture)
    {
        _grains = fixture.Cluster.GrainFactory;
    }

    private async Task<(Guid AccountId, CharacterSummary Character)> CreateCharacter(string name, string raceId = "dwarf")
    {
        Guid accountId = Guid.NewGuid();
        var request = new CreateCharacterRequest { Name = name, RaceId = raceId, Alignment = AlignmentCell.LawfulGood, Abilities = StandardPointBuy };
        DomainResult<CharacterSummary> created = await _grains.GetGrain<IAccountGrain>(accountId).CreateCharacter(request);
        Assert.True(created.Success, created.Message);
        return (accountId, created.Value!);
    }

    private ICharacterGrain Character(Guid id) => _grains.GetGrain<ICharacterGrain>(id);

    [Fact]
    public async Task NewCharacterStartsInRaceStartZone()
    {
        (Guid accountId, CharacterSummary character) = await CreateCharacter("Торин");

        IReadOnlyList<CharacterSummary> list = await _grains.GetGrain<IAccountGrain>(accountId).ListCharacters();
        CharacterState state = await Character(character.CharacterId).GetState();

        Assert.Single(list);
        Assert.Equal(SharedContent.Catalog.Races["dwarf"].StartZoneId, state.Sheet.ZoneId);
        Assert.Equal("faction_kargath", state.FactionId);
    }

    [Fact]
    public async Task PartyMembershipIsMirroredOnCharacters()
    {
        CharacterSummary leader = (await CreateCharacter("Лидер")).Character;
        CharacterSummary member = (await CreateCharacter("Соратник")).Character;
        Guid partyId = Guid.NewGuid();
        IPartyGrain party = _grains.GetGrain<IPartyGrain>(partyId);

        Assert.True((await party.Create(leader.CharacterId)).Success);
        Assert.True((await party.Invite(leader.CharacterId, member.CharacterId)).Success);
        Assert.True((await party.Accept(member.CharacterId)).Success);

        Assert.Equal(partyId, (await Character(member.CharacterId).GetState()).PartyId);
        Assert.True((await party.Leave(member.CharacterId)).Success);
        Assert.Null((await Character(member.CharacterId).GetState()).PartyId);
    }

    [Fact]
    public async Task AuctionMovesItemsAndGoldWithFees()
    {
        CharacterSummary seller = (await CreateCharacter("Продавец")).Character;
        CharacterSummary buyer = (await CreateCharacter("Покупатель")).Character;
        await Character(seller.CharacterId).GrantItems(new List<ItemQuantity> { new("copper_ore", 5) });
        await Character(seller.CharacterId).AddGold(100);
        await Character(buyer.CharacterId).AddGold(100);
        IAuctionGrain auction = _grains.GetGrain<IAuctionGrain>("auction-test");

        var listing = await auction.Create(seller.CharacterId, "copper_ore", 5, 50, TimeSpan.FromHours(12));
        Assert.True(listing.Success, listing.Message);
        Assert.True((await auction.Buy(listing.Value!.Id, buyer.CharacterId)).Success);

        CharacterState sellerState = await Character(seller.CharacterId).GetState();
        CharacterState buyerState = await Character(buyer.CharacterId).GetState();
        const long startingGold = CharacterRules.StartingGold;
        Assert.Equal(startingGold + 100 - 1 + 48, sellerState.Sheet.Gold);
        Assert.Equal(startingGold + 50, buyerState.Sheet.Gold);
        Assert.Equal(5, buyerState.Materials.Count("copper_ore"));
        Assert.Equal(0, sellerState.Materials.Count("copper_ore"));
    }

    [Fact]
    public async Task CrystalPurchaseThenCosmeticEquip()
    {
        (Guid accountId, CharacterSummary character) = await CreateCharacter("Модница", "elf");
        IAccountGrain account = _grains.GetGrain<IAccountGrain>(accountId);

        Assert.True((await account.ApplyPlatformPurchase("apple", "txn-42", "crystals_small")).Success);
        Assert.False((await account.ApplyPlatformPurchase("apple", "txn-42", "crystals_small")).Success);
        Assert.True((await account.BuyWithCrystals("sku_shadow_cloak")).Success);

        Assert.Equal(100, (await account.GetEntitlements()).Crystals);
        Assert.True((await Character(character.CharacterId).EquipCosmetic("skin_shadow_cloak")).Success);
    }

    [Fact]
    public async Task OfflineJobCanBeStartedOnceAndClaimed()
    {
        CharacterSummary character = (await CreateCharacter("Трудяга")).Character;
        string jobId = SharedContent.Catalog.OfflineJobs.Values.First(job => job.Kind == OfflineJobKind.CityWork).Id;

        Assert.True((await Character(character.CharacterId).StartOfflineJob(jobId, TimeSpan.FromHours(2))).Success);
        Assert.False((await Character(character.CharacterId).StartOfflineJob(jobId, TimeSpan.FromHours(2))).Success);

        Assert.True((await Character(character.CharacterId).ClaimOfflineJob()).Success);
        Assert.Null((await Character(character.CharacterId).GetState()).OfflineJob);
    }

    [Fact]
    public async Task SoloQueueAllocatesInstanceOnRegisteredServer()
    {
        CharacterSummary character = (await CreateCharacter("Одиночка")).Character;
        IZoneDirectoryGrain directory = _grains.GetGrain<IZoneDirectoryGrain>(GrainKeys.Global);
        await directory.Heartbeat(new ZoneServerInfo("zone-dev-1", new[] { "start_ironhold" }, "127.0.0.1", 7777, 7778, 0, null));
        IMatchmakingGrain queue = _grains.GetGrain<IMatchmakingGrain>(GrainKeys.Matchmaking("inst_trial_crypt", 1));

        Assert.True((await queue.Enqueue(new QueueTicket(Guid.NewGuid(), new[] { character.CharacterId }, 1, DateTimeOffset.UtcNow))).Success);
        Assert.Equal(1, await queue.FormRunsNow());

        InstanceAssignment assignment = (await Character(character.CharacterId).GetState()).PendingInstance!;
        ZoneServerInfo? server = await directory.ResolveInstance(assignment.RunId);
        Assert.Equal("inst_trial_crypt", assignment.ZoneId);
        Assert.Equal("zone-dev-1", server!.ServerId);
    }
}
