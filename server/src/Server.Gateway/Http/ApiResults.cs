using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Realmforge.Server.Core;
using Realmforge.Server.Grains;

namespace Realmforge.Server.Gateway.Http;

public sealed record ApiError(string Code, string Message);

public static class ApiResults
{
    public static IResult From(DomainResult result) =>
        result.Success ? Results.Ok() : Results.BadRequest(new ApiError(result.Code, result.Message));

    public static IResult From<T>(DomainResult<T> result) =>
        result.Success ? Results.Ok(result.Value) : Results.BadRequest(new ApiError(result.Code, result.Message));

    public static IResult Error(string code, string message, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new ApiError(code, message), statusCode: status);

    public static IResult NotYourCharacter() =>
        Error("character.not_owned", "Персонаж не принадлежит аккаунту.", StatusCodes.Status403Forbidden);
}

public static class PlayerIdentity
{
    public static Guid AccountId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Authenticated request without subject."));

    /// <summary>Every character-scoped route goes through this check so one account cannot act for another's character.</summary>
    public static async Task<bool> Owns(this ClaimsPrincipal user, IGrainFactory grains, Guid characterId) =>
        await grains.GetGrain<IAccountGrain>(user.AccountId()).OwnsCharacter(characterId);
}
