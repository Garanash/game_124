using System.Security.Claims;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core.Economy;
using Realmforge.Server.Gateway.Http;
using Realmforge.Server.Grains;

namespace Realmforge.Server.Gateway.Endpoints;

public sealed record CreateListingRequest(Guid CharacterId, string ItemId, int Quantity, long BuyoutPrice, double Hours);

public sealed record BuyListingRequest(Guid CharacterId);

public sealed record ContentVersionResponse(string Version);

public static class EconomyEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private const int DefaultLadderSize = 50;

    public static void MapEconomy(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder auction = routes.MapGroup("/auction").RequireAuthorization();
        auction.MapGet("/", (string? itemId, int? page, int? pageSize, IGrainFactory grains) =>
            Auction(grains).Search(itemId, page ?? 0, Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize)));
        auction.MapPost("/", async (CreateListingRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
        {
            if (!await user.Owns(grains, request.CharacterId))
            {
                return ApiResults.NotYourCharacter();
            }
            return ApiResults.From(await Auction(grains).Create(request.CharacterId, request.ItemId, request.Quantity, request.BuyoutPrice, TimeSpan.FromHours(request.Hours)));
        });
        auction.MapPost("/{listingId:guid}/buy", async (Guid listingId, BuyListingRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
        {
            if (!await user.Owns(grains, request.CharacterId))
            {
                return ApiResults.NotYourCharacter();
            }
            return ApiResults.From(await Auction(grains).Buy(listingId, request.CharacterId));
        });
        auction.MapPost("/{listingId:guid}/cancel", async (Guid listingId, BuyListingRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
        {
            if (!await user.Owns(grains, request.CharacterId))
            {
                return ApiResults.NotYourCharacter();
            }
            return ApiResults.From(await Auction(grains).Cancel(listingId, request.CharacterId));
        });

        routes.MapGet("/arena/ladder", (int? count, IGrainFactory grains) =>
            grains.GetGrain<IArenaLadderGrain>(GrainKeys.Global).Top(count ?? DefaultLadderSize));
        routes.MapGet("/content/version", (ContentCatalog catalog) => new ContentVersionResponse(catalog.Version));
    }

    private static IAuctionGrain Auction(IGrainFactory grains) => grains.GetGrain<IAuctionGrain>(GrainKeys.Global);
}
