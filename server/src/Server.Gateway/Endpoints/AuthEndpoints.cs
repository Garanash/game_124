using Realmforge.Server.Core.Auth;
using Realmforge.Server.Gateway.Accounts;
using Realmforge.Server.Gateway.Http;
using Realmforge.Server.Hosting;

namespace Realmforge.Server.Gateway.Endpoints;

public sealed record CredentialsRequest(string Email, string Password);

public sealed record SessionResponse(Guid AccountId, string Token);

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";
    private const int MaxPasswordLength = 128;

    public static void MapAuth(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder auth = routes.MapGroup("/auth").RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/register", Register);
        auth.MapPost("/login", Login);
    }

    private static async Task<IResult> Register(CredentialsRequest request, IAccountStore accounts, TokenService tokens, TimeProvider time, IAnalytics analytics)
    {
        string? email = AccountEmail.Normalize(request.Email);
        if (email == null)
        {
            return ApiResults.Error("auth.email_invalid", "Некорректный email.");
        }
        if (request.Password.Length < PasswordHasher.MinPasswordLength || request.Password.Length > MaxPasswordLength)
        {
            return ApiResults.Error("auth.password_length", $"Пароль должен быть от {PasswordHasher.MinPasswordLength} до {MaxPasswordLength} символов.");
        }
        var account = new AccountRecord(Guid.NewGuid(), email, PasswordHasher.Hash(request.Password), time.GetUtcNow());
        if (!await accounts.TryCreate(account))
        {
            return ApiResults.Error("auth.email_taken", "Аккаунт с таким email уже существует.", StatusCodes.Status409Conflict);
        }
        analytics.Track(AnalyticsEvents.AccountRegistered, account.Id);
        return Results.Ok(new SessionResponse(account.Id, tokens.IssueSession(account.Id)));
    }

    /// <summary>Unknown email and wrong password share one error so the endpoint does not reveal which emails exist.</summary>
    private static async Task<IResult> Login(CredentialsRequest request, IAccountStore accounts, TokenService tokens, IAnalytics analytics)
    {
        string? email = AccountEmail.Normalize(request.Email);
        AccountRecord? account = email == null ? null : await accounts.FindByEmail(email);
        if (account == null || !PasswordHasher.Verify(request.Password, account.PasswordHash))
        {
            return ApiResults.Error("auth.invalid_credentials", "Неверный email или пароль.", StatusCodes.Status401Unauthorized);
        }
        analytics.Track(AnalyticsEvents.LoggedIn, account.Id);
        return Results.Ok(new SessionResponse(account.Id, tokens.IssueSession(account.Id)));
    }
}
