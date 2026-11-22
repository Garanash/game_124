using System.Security.Cryptography;
using System.Text;
using Realmforge.Server.Core.LiveOps;
using Realmforge.Server.Core.Store;
using Xunit;

namespace Realmforge.Server.Tests;

public sealed class StoreTests
{
    private const string Secret = "project-secret";

    private static string Sign(string body) =>
        XsollaWebhook.SignaturePrefix + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(body + Secret))).ToLowerInvariant();

    private static string PaymentBody(Guid accountId) =>
        $$$"""{"notification_type":"payment","user":{"id":"{{{accountId}}}"},"transaction":{"id":987654},"custom_parameters":{"sku_id":"sku_crystals_500"}}""";

    [Fact]
    public void XsollaSignatureMustMatchBodyAndSecret()
    {
        string body = PaymentBody(Guid.NewGuid());

        Assert.True(XsollaWebhook.HasValidSignature(body, Sign(body), Secret));
        Assert.False(XsollaWebhook.HasValidSignature(body + " ", Sign(body), Secret));
        Assert.False(XsollaWebhook.HasValidSignature(body, Sign(body), "other-secret"));
        Assert.False(XsollaWebhook.HasValidSignature(body, "Signature not-hex", Secret));
        Assert.False(XsollaWebhook.HasValidSignature(body, Sign(body), string.Empty));
    }

    [Fact]
    public void XsollaPaymentCarriesAccountTransactionAndSku()
    {
        Guid accountId = Guid.NewGuid();

        XsollaPayment payment = XsollaWebhook.ParsePayment(PaymentBody(accountId))!;

        Assert.Equal(XsollaWebhook.PaymentNotification, XsollaWebhook.NotificationType(PaymentBody(accountId)));
        Assert.Equal(new XsollaPayment(accountId, "987654", "sku_crystals_500"), payment);
        Assert.Null(XsollaWebhook.ParsePayment("""{"notification_type":"payment","user":{"id":"not-a-guid"}}"""));
    }

    [Fact]
    public async Task UnconfiguredPlatformsNeverGrantGoods()
    {
        var claim = new PurchaseClaim(StorePlatforms.Apple, "sku_crystals_500", "txn-1", "forged-receipt");

        ReceiptVerdict apple = await new UnconfiguredPlatformValidator(StorePlatforms.Apple).Validate(claim, CancellationToken.None);
        ReceiptVerdict dev = await new DevReceiptValidator().Validate(claim with { Platform = StorePlatforms.Dev }, CancellationToken.None);

        Assert.False(apple.Valid);
        Assert.True(dev.Valid);
        Assert.Equal("txn-1", dev.TransactionId);
    }

    [Fact]
    public void UnknownFeatureFlagsAreOff()
    {
        var features = new FeatureFlags { Flags = { ["store"] = true, ["auction"] = false } };

        Assert.True(features.IsEnabled("STORE"));
        Assert.False(features.IsEnabled("auction"));
        Assert.False(features.IsEnabled("new_continent"));
        Assert.Equal(new[] { "store" }, features.Enabled());
    }
}
