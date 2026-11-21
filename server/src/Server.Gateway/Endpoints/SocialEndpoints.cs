using System.Security.Claims;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Core.Matchmaking;
using Realmforge.Server.Core.Social;
using Realmforge.Server.Gateway.Http;
using Realmforge.Server.Grains;

namespace Realmforge.Server.Gateway.Endpoints;

public sealed record InviteRequest(Guid CharacterId, Guid InviteeId);

public sealed record MemberRequest(Guid CharacterId);

public sealed record FoundGuildRequest(Guid CharacterId, string Name, string Tag);

public sealed record GuildRankRequest(Guid CharacterId, Guid MemberId, GuildRank Rank);

public sealed record QueueRequest(Guid CharacterId, string InstanceId, int PartySize);

/// <summary>
/// Party, guild and dungeon queue. The acting character comes in the body and is checked against the
/// session's account before any grain call.
/// </summary>
public static class SocialEndpoints
{
    public static void MapSocial(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder party = routes.MapGroup("/parties").RequireAuthorization();
        party.MapPost("/", (MemberRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IPartyGrain>(Guid.NewGuid()).Create(request.CharacterId)));
        party.MapGet("/{partyId:guid}", (Guid partyId, IGrainFactory grains) => grains.GetGrain<IPartyGrain>(partyId).GetState());
        party.MapPost("/{partyId:guid}/invite", (Guid partyId, InviteRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IPartyGrain>(partyId).Invite(request.CharacterId, request.InviteeId)));
        party.MapPost("/{partyId:guid}/accept", (Guid partyId, MemberRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IPartyGrain>(partyId).Accept(request.CharacterId)));
        party.MapPost("/{partyId:guid}/leave", (Guid partyId, MemberRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IPartyGrain>(partyId).Leave(request.CharacterId)));
        party.MapPost("/{partyId:guid}/kick", (Guid partyId, InviteRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IPartyGrain>(partyId).Kick(request.CharacterId, request.InviteeId)));
        party.MapPost("/{partyId:guid}/raid", (Guid partyId, MemberRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IPartyGrain>(partyId).ConvertToRaid(request.CharacterId)));

        RouteGroupBuilder guild = routes.MapGroup("/guilds").RequireAuthorization();
        guild.MapPost("/", (FoundGuildRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IGuildGrain>(Guid.NewGuid()).Found(request.Name, request.Tag, request.CharacterId)));
        guild.MapGet("/{guildId:guid}", (Guid guildId, IGrainFactory grains) => grains.GetGrain<IGuildGrain>(guildId).GetState());
        guild.MapPost("/{guildId:guid}/invite", (Guid guildId, InviteRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IGuildGrain>(guildId).Invite(request.CharacterId, request.InviteeId)));
        guild.MapPost("/{guildId:guid}/accept", (Guid guildId, MemberRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IGuildGrain>(guildId).Accept(request.CharacterId)));
        guild.MapPost("/{guildId:guid}/rank", (Guid guildId, GuildRankRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IGuildGrain>(guildId).SetRank(request.CharacterId, request.MemberId, request.Rank)));
        guild.MapPost("/{guildId:guid}/leave", (Guid guildId, MemberRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, () => grains.GetGrain<IGuildGrain>(guildId).Leave(request.CharacterId)));

        RouteGroupBuilder queue = routes.MapGroup("/queue").RequireAuthorization();
        queue.MapPost("/", Enqueue);
        queue.MapPost("/cancel", (QueueRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            Act(user, grains, request.CharacterId, async () =>
            {
                bool cancelled = await grains.GetGrain<IMatchmakingGrain>(GrainKeys.Matchmaking(request.InstanceId, request.PartySize)).Cancel(request.CharacterId);
                return cancelled ? DomainResult.Ok : DomainResult.Fail("queue.not_queued", "Вы не в очереди.");
            }));
    }

    /// <summary>A party leader queues the whole party as one ticket so members are never split across runs.</summary>
    private static Task<IResult> Enqueue(QueueRequest request, ClaimsPrincipal user, IGrainFactory grains, TimeProvider time)
    {
        return Act(user, grains, request.CharacterId, async () =>
        {
            CharacterState state = await grains.GetGrain<ICharacterGrain>(request.CharacterId).GetState();
            List<Guid> members = new() { request.CharacterId };
            if (state.PartyId.HasValue && await grains.GetGrain<IPartyGrain>(state.PartyId.Value).GetState() is { } partyState)
            {
                if (partyState.LeaderId != request.CharacterId)
                {
                    return DomainResult.Fail("queue.not_leader", "В очередь группу ставит лидер.");
                }
                members = partyState.Members.ToList();
            }
            var levels = new List<int>();
            foreach (Guid member in members)
            {
                levels.Add((await grains.GetGrain<ICharacterGrain>(member).GetState()).Sheet.Level);
            }
            var ticket = new QueueTicket(Guid.NewGuid(), members, (int)Math.Round(levels.Average()), time.GetUtcNow());
            return await grains.GetGrain<IMatchmakingGrain>(GrainKeys.Matchmaking(request.InstanceId, request.PartySize)).Enqueue(ticket);
        });
    }

    private static async Task<IResult> Act(ClaimsPrincipal user, IGrainFactory grains, Guid characterId, Func<Task<DomainResult>> action)
    {
        if (!await user.Owns(grains, characterId))
        {
            return ApiResults.NotYourCharacter();
        }
        return ApiResults.From(await action());
    }
}
