using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.LiveOps;
using Realmforge.Server.Core.Store;
using Realmforge.Server.Grains;
using Realmforge.Server.Hosting;

namespace Realmforge.Server.Store;

public sealed record StoreSkuView(string Id, string Name, SkuKind Kind, int PriceCents, int PriceCrystals, IReadOnlyDictionary<string, string> PlatformProducts);

public sealed record PurchaseRequest(string SkuId, string TransactionId, string Receipt);

public sealed record StoreError(string Code, string Message);

public static class StoreEndpoints
{
    public const string StoreFeatureFlag = "store";

    public static void MapStore(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder store = routes.MapGroup("/store");
        store.MapGet("/catalog", (ContentCatalog catalog) => catalog.StoreSkus.Values
            .OrderBy(sku => sku.Kind).ThenBy(sku => sku.PriceCents)
            .Select(sku => new StoreSkuView(sku.Id, sku.Name, sku.Kind, sku.PriceCents, sku.PriceCrystals, sku.PlatformProducts)));
        store.MapGet("/entitlements", (ClaimsPrincipal user, IGrainFactory grains) => Account(grains, user).GetEntitlements())
            .RequireAuthorization();
        store.MapPost("/purchases/{platform}", Purchase).RequireAuthorization();
        store.MapPost("/crystals/{skuId}", BuyWithCrystals).RequireAuthorization();
        store.MapPost("/webhooks/xsolla", XsollaNotification);
    }

    private static async Task<IResult> Purchase(
        string platform,
        PurchaseRequest request,
        ClaimsPrincipal user,
        IEnumerable<IReceiptValidator> validators,
        IGrainFactory grains,
        FeatureFlags features,
        IAnalytics analytics,
        CancellationToken cancellation)
    {
        if (!features.IsEnabled(StoreFeatureFlag))
        {
            return Error("store.closed", "Магазин временно закрыт.", StatusCodes.Status503ServiceUnavailable);
        }
        IReceiptValidator? validator = validators.FirstOrDefault(candidate => candidate.Platform == platform);
        if (validator == null)
        {
            return Error("store.unknown_platform", "Платформа не поддерживается.");
        }
        Guid accountId = AccountId(user);
        ReceiptVerdict verdict = await validator.Validate(new PurchaseClaim(platform, request.SkuId, request.TransactionId, request.Receipt), cancellation);
        if (!verdict.Valid)
        {
            analytics.Track(AnalyticsEvents.PurchaseRejected, accountId, properties: Properties(platform, request.SkuId, verdict.Reason));
            return Error(verdict.Reason, "Покупка не подтверждена платформой.");
        }
        return await Grant(grains.GetGrain<IAccountGrain>(accountId), accountId, platform, verdict.TransactionId, request.SkuId, analytics);
    }

    private static async Task<IResult> BuyWithCrystals(string skuId, ClaimsPrincipal user, IGrainFactory grains, FeatureFlags features, IAnalytics analytics)
    {
        if (!features.IsEnabled(StoreFeatureFlag))
        {
            return Error("store.closed", "Магазин временно закрыт.", StatusCodes.Status503ServiceUnavailable);
        }
        DomainResult result = await Account(grains, user).BuyWithCrystals(skuId);
        if (result.Success)
        {
            analytics.Track(AnalyticsEvents.CrystalsSpent, AccountId(user), properties: new Dictionary<string, string> { ["sku"] = skuId });
        }
        return From(result);
    }

    /// <summary>Xsolla retries until it gets 2xx, so already-processed transactions must also answer 204.</summary>
    private static async Task<IResult> XsollaNotification(HttpRequest http, StoreOptions options, IGrainFactory grains, IAnalytics analytics)
    {
        using var reader = new StreamReader(http.Body);
        string body = await reader.ReadToEndAsync();
        if (!XsollaWebhook.HasValidSignature(body, http.Headers.Authorization, options.XsollaWebhookSecret))
        {
            return Results.Json(new { error = new { code = "INVALID_SIGNATURE" } }, statusCode: StatusCodes.Status400BadRequest);
        }
        if (XsollaWebhook.NotificationType(body) != XsollaWebhook.PaymentNotification)
        {
            return Results.NoContent();
        }
        XsollaPayment? payment = XsollaWebhook.ParsePayment(body);
        if (payment == null)
        {
            return Results.Json(new { error = new { code = "INVALID_PARAMETER" } }, statusCode: StatusCodes.Status400BadRequest);
        }
        IResult granted = await Grant(grains.GetGrain<IAccountGrain>(payment.AccountId), payment.AccountId, StorePlatforms.Xsolla, payment.TransactionId, payment.SkuId, analytics);
        return granted is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK } ? Results.NoContent() : granted;
    }

    private static async Task<IResult> Grant(IAccountGrain account, Guid accountId, string platform, string transactionId, string skuId, IAnalytics analytics)
    {
        DomainResult result = await account.ApplyPlatformPurchase(platform, transactionId, skuId);
        if (result.Success)
        {
            analytics.Track(AnalyticsEvents.PurchaseCompleted, accountId, properties: Properties(platform, skuId, transactionId));
        }
        return From(result);
    }

    private static Dictionary<string, string> Properties(string platform, string skuId, string detail) =>
        new() { ["platform"] = platform, ["sku"] = skuId, ["detail"] = detail };

    private static IAccountGrain Account(IGrainFactory grains, ClaimsPrincipal user) => grains.GetGrain<IAccountGrain>(AccountId(user));

    private static Guid AccountId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? throw new InvalidOperationException("Authenticated request without subject."));

    private static IResult From(DomainResult result) => result.Success ? Results.Ok() : Error(result.Code, result.Message);

    private static IResult Error(string code, string message, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new StoreError(code, message), statusCode: status);
}
