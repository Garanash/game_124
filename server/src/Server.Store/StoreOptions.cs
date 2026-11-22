namespace Realmforge.Server.Store;

public sealed class StoreOptions
{
    public const string SectionName = "Store";

    /// <summary>Enables the "dev" platform that accepts any receipt; must stay off in production.</summary>
    public bool AllowDevPurchases { get; set; }
    public string XsollaWebhookSecret { get; set; } = string.Empty;
}
