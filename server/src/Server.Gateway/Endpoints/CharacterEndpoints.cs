using System.Security.Claims;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Inventory;
using Realmforge.Server.Core.Auth;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Gateway.Http;
using Realmforge.Server.Grains;

namespace Realmforge.Server.Gateway.Endpoints;

public sealed record EnterWorldResponse(string ZoneId, string Host, int UdpPort, int WebSocketPort, string Ticket);

public sealed record BackpackMoveRequest(Guid InstanceId, int X, int Y, int Rotation);

public sealed record BackpackPlaceRequest(string ItemId, int X, int Y, int Rotation);

public sealed record EquipRequest(Guid InstanceId, EquipmentSlot Slot);

public sealed record UnequipRequest(EquipmentSlot Slot, int X, int Y, int Rotation);

public sealed record OfflineJobRequest(string JobId, double Hours);

public sealed record HousePlotRequest(string DistrictZoneId, int PlotNumber);

public sealed record HouseBuildRequest(string ModuleId, int X, int Z, bool Rotated);

public static class CharacterEndpoints
{
    public const string CharacterIdRoute = "characterId";

    public static void MapCharacters(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder account = routes.MapGroup("/characters").RequireAuthorization();
        account.MapGet("/", (ClaimsPrincipal user, IGrainFactory grains) => grains.GetGrain<IAccountGrain>(user.AccountId()).ListCharacters());
        account.MapPost("/", async (CreateCharacterRequest request, ClaimsPrincipal user, IGrainFactory grains) =>
            ApiResults.From(await grains.GetGrain<IAccountGrain>(user.AccountId()).CreateCharacter(request)));

        RouteGroupBuilder character = account.MapGroup("/{characterId:guid}").AddEndpointFilter(RequireOwnership);
        character.MapDelete("/", async (Guid characterId, ClaimsPrincipal user, IGrainFactory grains) =>
            ApiResults.From(await grains.GetGrain<IAccountGrain>(user.AccountId()).DeleteCharacter(characterId)));
        character.MapGet("/", (Guid characterId, IGrainFactory grains) => Character(grains, characterId).GetState());
        character.MapGet("/stats", async (Guid characterId, IGrainFactory grains, ContentCatalog catalog) =>
        {
            CharacterState state = await Character(grains, characterId).GetState();
            return CharacterStats.Build(state.Sheet, state.Equipment, catalog);
        });
        character.MapPost("/enter", EnterWorld);

        character.MapPost("/perks/{perkId}", async (Guid characterId, string perkId, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).TakePerk(perkId)));
        character.MapPost("/classes/{classId}", async (Guid characterId, string classId, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).TakeClassLevel(classId)));

        character.MapPost("/backpack/move", async (Guid characterId, BackpackMoveRequest request, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).MoveBackpackItem(request.InstanceId, request.X, request.Y, request.Rotation)));
        character.MapPost("/backpack/place", async (Guid characterId, BackpackPlaceRequest request, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).PlaceFromBag(request.ItemId, request.X, request.Y, request.Rotation)));
        character.MapPost("/backpack/{instanceId:guid}/stash", async (Guid characterId, Guid instanceId, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).StashToBag(instanceId)));
        character.MapPost("/equipment/equip", async (Guid characterId, EquipRequest request, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).Equip(request.InstanceId, request.Slot)));
        character.MapPost("/equipment/unequip", async (Guid characterId, UnequipRequest request, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).Unequip(request.Slot, request.X, request.Y, request.Rotation)));
        character.MapPost("/abilities/{ability}/raise", async (Guid characterId, Ability ability, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).RaiseAbility(ability)));
        character.MapPost("/crafts/{recipeId}", async (Guid characterId, string recipeId, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).Craft(recipeId)));

        character.MapPost("/offline-job", async (Guid characterId, OfflineJobRequest request, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).StartOfflineJob(request.JobId, TimeSpan.FromHours(request.Hours))));
        character.MapPost("/offline-job/claim", async (Guid characterId, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).ClaimOfflineJob()));
        character.MapPost("/offline-job/report", (Guid characterId, IGrainFactory grains) =>
            Character(grains, characterId).TakeOfflineReport());

        character.MapPost("/house/claim", async (Guid characterId, HousePlotRequest request, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).ClaimHousePlot(request.DistrictZoneId, request.PlotNumber)));
        character.MapPost("/house/modules", async (Guid characterId, HouseBuildRequest request, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).BuildHouseModule(request.ModuleId, request.X, request.Z, request.Rotated)));
        character.MapDelete("/house/modules/{instanceId:guid}", async (Guid characterId, Guid instanceId, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).DemolishHouseModule(instanceId)));
        character.MapPost("/cosmetics/{cosmeticId}", async (Guid characterId, string cosmeticId, IGrainFactory grains) =>
            ApiResults.From(await Character(grains, characterId).EquipCosmetic(cosmeticId)));
    }

    public static ICharacterGrain Character(IGrainFactory grains, Guid characterId) => grains.GetGrain<ICharacterGrain>(characterId);

    private static async ValueTask<object?> RequireOwnership(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        bool parsed = Guid.TryParse(http.Request.RouteValues[CharacterIdRoute]?.ToString(), out Guid characterId);
        IGrainFactory grains = http.RequestServices.GetRequiredService<IGrainFactory>();
        if (!parsed || !await http.User.Owns(grains, characterId))
        {
            return ApiResults.NotYourCharacter();
        }
        return await next(context);
    }

    /// <summary>Pending instance runs take priority over the open world so matchmade groups land together.</summary>
    private static async Task<IResult> EnterWorld(Guid characterId, ClaimsPrincipal user, IGrainFactory grains, TokenService tokens)
    {
        CharacterState state = await Character(grains, characterId).GetState();
        IZoneDirectoryGrain directory = grains.GetGrain<IZoneDirectoryGrain>(GrainKeys.Global);
        InstanceAssignment? instance = state.PendingInstance;
        string zoneId = instance?.ZoneId ?? state.Sheet.ZoneId;
        ZoneServerInfo? server = instance != null ? await directory.ResolveInstance(instance.RunId) : await directory.ResolveZone(zoneId);
        if (server == null)
        {
            return ApiResults.Error("zone.unavailable", "Сервер зоны недоступен, попробуйте позже.", StatusCodes.Status503ServiceUnavailable);
        }
        string ticket = tokens.IssueZoneTicket(new ZoneTicket(user.AccountId(), characterId, zoneId, instance?.RunId));
        return Results.Ok(new EnterWorldResponse(zoneId, server.Host, server.UdpPort, server.WebSocketPort, ticket));
    }
}
