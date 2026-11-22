using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Realmforge.Server.Core.Store;

/// <summary>What the client claims it bought; never trusted until a validator confirms it with the platform.</summary>
public sealed record PurchaseClaim(string Platform, string SkuId, string TransactionId, string Receipt);

public sealed record ReceiptVerdict(bool Valid, string TransactionId, string Reason)
{
    public static ReceiptVerdict Accept(string transactionId) => new(true, transactionId, string.Empty);

    public static ReceiptVerdict Reject(string reason) => new(false, string.Empty, reason);
}

public interface IReceiptValidator
{
    string Platform { get; }
    Task<ReceiptVerdict> Validate(PurchaseClaim claim, CancellationToken cancellation);
}

public static class StorePlatforms
{
    public const string Apple = "apple";
    public const string Google = "google";
    public const string Steam = "steam";
    public const string Xsolla = "xsolla";
    public const string Dev = "dev";
}

/// <summary>Accepts any receipt; registered only in Development so the purchase flow can be tested without a store account.</summary>
public sealed class DevReceiptValidator : IReceiptValidator
{
    public string Platform => StorePlatforms.Dev;

    public Task<ReceiptVerdict> Validate(PurchaseClaim claim, CancellationToken cancellation) =>
        Task.FromResult(string.IsNullOrWhiteSpace(claim.TransactionId)
            ? ReceiptVerdict.Reject("receipt.missing_transaction")
            : ReceiptVerdict.Accept(claim.TransactionId));
}

/// <summary>
/// Placeholder for App Store / Google Play / Steam verification: every purchase is refused until real
/// server-to-server credentials are wired in, so a missing integration can never hand out free goods.
/// </summary>
public sealed class UnconfiguredPlatformValidator : IReceiptValidator
{
    public UnconfiguredPlatformValidator(string platform)
    {
        Platform = platform;
    }

    public string Platform { get; }

    public Task<ReceiptVerdict> Validate(PurchaseClaim claim, CancellationToken cancellation) =>
        Task.FromResult(ReceiptVerdict.Reject("receipt.platform_not_configured"));
}

public sealed record XsollaPayment(Guid AccountId, string TransactionId, string SkuId);

/// <summary>
/// Xsolla pays out through webhooks rather than client receipts. Each request is signed as
/// sha1(raw body + project secret) in "Authorization: Signature &lt;hex&gt;".
/// </summary>
public static class XsollaWebhook
{
    public const string SignaturePrefix = "Signature ";
    public const string PaymentNotification = "payment";
    public const string UserValidationNotification = "user_validation";

    public static bool HasValidSignature(string rawBody, string? authorizationHeader, string secret)
    {
        if (string.IsNullOrEmpty(secret) || authorizationHeader == null || !authorizationHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return false;
        }
        byte[] expected = SHA1.HashData(Encoding.UTF8.GetBytes(rawBody + secret));
        byte[] provided;
        try
        {
            provided = Convert.FromHexString(authorizationHeader[SignaturePrefix.Length..].Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    public static string NotificationType(string rawBody)
    {
        using JsonDocument document = JsonDocument.Parse(rawBody);
        return document.RootElement.TryGetProperty("notification_type", out JsonElement type) ? type.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>The store page passes our account id as the Xsolla user id and the SKU as a custom parameter.</summary>
    public static XsollaPayment? ParsePayment(string rawBody)
    {
        using JsonDocument document = JsonDocument.Parse(rawBody);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("user", out JsonElement user) || !Guid.TryParse(user.GetProperty("id").GetString(), out Guid accountId)
            || !root.TryGetProperty("transaction", out JsonElement transaction)
            || !root.TryGetProperty("custom_parameters", out JsonElement parameters)
            || !parameters.TryGetProperty("sku_id", out JsonElement sku))
        {
            return null;
        }
        string transactionId = transaction.GetProperty("id").ValueKind == JsonValueKind.Number
            ? transaction.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : transaction.GetProperty("id").GetString() ?? string.Empty;
        return transactionId.Length == 0 ? null : new XsollaPayment(accountId, transactionId, sku.GetString() ?? string.Empty);
    }
}
