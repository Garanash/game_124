using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Quests;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Core.Economy;
using Realmforge.Server.Core.Matchmaking;
using Realmforge.Server.Core.Social;
using Realmforge.Server.Core.Store;

namespace Realmforge.Server.Grains;

public static class GrainKeys
{
    /// <summary>Key for realm-wide singleton grains (auction house, ladder, directories).</summary>
    public const string Global = "global";

    public static string Matchmaking(string instanceId, int partySize) => instanceId + ":" + partySize;
}

public interface IAccountGrain : IGrainWithGuidKey
{
    Task<DomainResult<CharacterSummary>> CreateCharacter(CreateCharacterRequest request);
    Task<IReadOnlyList<CharacterSummary>> ListCharacters();
    Task<bool> OwnsCharacter(Guid characterId);
    Task<DomainResult> DeleteCharacter(Guid characterId);
    Task<EntitlementView> GetEntitlements();
    Task<DomainResult> ApplyPlatformPurchase(string platform, string transactionId, string skuId);
    Task<DomainResult> BuyWithCrystals(string skuId);
}

public interface ICharacterGrain : IGrainWithGuidKey
{
    Task Initialize(CharacterState state);
    Task<CharacterState> GetState();
    Task MarkDeleted();

    Task<RewardOutcome> ApplyEncounterRewards(long xp, LootResult loot);
    Task<IReadOnlyList<QuestProgressView>> OnQuestEvent(QuestEvent questEvent);
    Task<DomainResult<List<QuestProgressView>>> ChooseQuestOption(string questId, string choiceId);
    Task<DomainResult<DialogueView>> Interact(string npcId, string optionId);
    Task<DomainResult<RewardOutcome>> Gather(string nodeId);
    Task<DomainResult<RewardOutcome>> Craft(string recipeId);

    Task<DomainResult> Fuse(string recipeId);
    Task<DomainResult> MoveBackpackItem(Guid instanceId, int x, int y, int rotation);
    Task<DomainResult> PlaceFromBag(string itemId, int x, int y, int rotation);
    Task<DomainResult> StashToBag(Guid instanceId);
    Task<DomainResult> TakePerk(string perkId);
    Task<DomainResult> TakeClassLevel(string classId);

    Task<DomainResult> StartOfflineJob(string jobId, TimeSpan duration);
    Task<DomainResult<RewardOutcome>> ClaimOfflineJob();
    Task<RewardOutcome?> TakeOfflineReport();

    Task<DomainResult> ClaimHousePlot(string districtZoneId, int plotNumber);
    Task<DomainResult> BuildHouseModule(string moduleId, int x, int z, bool rotated);
    Task<DomainResult> DemolishHouseModule(Guid instanceId);
    Task<DomainResult> EquipCosmetic(string cosmeticId);

    Task SavePosition(string zoneId, float x, float z, int hp);
    Task ApplyPvpKill(int lawShift, int goodShift);
    Task<long> ApplyPvpDeath(int goldLossPercent);

    Task SetParty(Guid? partyId);
    Task SetGuild(Guid? guildId);
    Task SetPendingInstance(InstanceAssignment? assignment);

    Task<DomainResult> SpendGold(long amount);
    Task AddGold(long amount);
    Task<DomainResult> TakeMaterials(string itemId, int quantity);
    Task GrantItems(List<ItemQuantity> items);
}

public interface IPartyGrain : IGrainWithGuidKey
{
    Task<DomainResult> Create(Guid leaderId);
    Task<DomainResult> Invite(Guid inviterId, Guid inviteeId);
    Task<DomainResult> Accept(Guid inviteeId);
    Task Decline(Guid inviteeId);
    Task<DomainResult> Leave(Guid memberId);
    Task<DomainResult> Kick(Guid leaderId, Guid memberId);
    Task<DomainResult> ConvertToRaid(Guid leaderId);
    Task<PartyState?> GetState();
}

public interface IGuildGrain : IGrainWithGuidKey
{
    Task<DomainResult> Found(string name, string tag, Guid founderId);
    Task<DomainResult> Invite(Guid inviterId, Guid inviteeId);
    Task<DomainResult> Accept(Guid inviteeId);
    Task<DomainResult> SetRank(Guid actorId, Guid memberId, GuildRank rank);
    Task<DomainResult> Leave(Guid memberId);
    Task<DomainResult> SetMessageOfTheDay(Guid actorId, string text);
    Task<GuildState?> GetState();
}

/// <summary>Realm-wide uniqueness of guild names and tags.</summary>
public interface IGuildRegistryGrain : IGrainWithStringKey
{
    Task<bool> TryReserve(string name, string tag, Guid guildId);
    Task Release(Guid guildId);
}

public interface IMatchmakingGrain : IGrainWithStringKey
{
    Task<DomainResult> Enqueue(QueueTicket ticket);
    Task<bool> Cancel(Guid characterId);
    Task<int> QueuedPlayers();
    /// <summary>Forms runs now instead of waiting for the periodic timer; used by tests and admin tools.</summary>
    Task<int> FormRunsNow();
}

public interface IAuctionGrain : IGrainWithStringKey
{
    Task<DomainResult<AuctionListing>> Create(Guid sellerCharacterId, string itemId, int quantity, long buyoutPrice, TimeSpan duration);
    Task<DomainResult> Buy(Guid listingId, Guid buyerCharacterId);
    Task<DomainResult> Cancel(Guid listingId, Guid sellerCharacterId);
    Task<IReadOnlyList<AuctionListing>> Search(string? itemId, int page, int pageSize);
}

public sealed record LadderEntry(Guid CharacterId, int Rating, int Wins, int Losses);

public interface IArenaLadderGrain : IGrainWithStringKey
{
    Task<(int WinnerRating, int LoserRating)> ReportResult(Guid winnerId, Guid loserId);
    Task<LadderEntry> Get(Guid characterId);
    Task<IReadOnlyList<LadderEntry>> Top(int count);
}

/// <summary>A zone or instance server process as seen by the directory.</summary>
public sealed record ZoneServerInfo(string ServerId, IReadOnlyList<string> ZoneIds, string Host, int UdpPort, int WebSocketPort, int Players, Guid? InstanceRunId);

public interface IZoneDirectoryGrain : IGrainWithStringKey
{
    Task Heartbeat(ZoneServerInfo server);
    Task<ZoneServerInfo?> ResolveZone(string zoneId);
    Task<ZoneServerInfo?> ResolveInstance(Guid runId);
    Task AssignInstance(Guid runId, ZoneServerInfo server);
    Task<IReadOnlyList<ZoneServerInfo>> List();
}
