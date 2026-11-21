using Realmforge.Rules.Definitions;

namespace Realmforge.Server.Core.Store;

/// <summary>
/// Per-account record of premium purchases. Platform transactions are applied idempotently so a retried
/// receipt or webhook can never grant twice.
/// </summary>
public sealed class EntitlementLedger
{
    public const int BaseCharacterSlots = 4;
    public const int BaseBankTabs = 1;
    public const string CharacterSlotSkuId = "sku_character_slot";
    public const string BankTabSkuId = "sku_bank_tab";

    public long Crystals { get; set; }
    public HashSet<string> OwnedCosmetics { get; set; } = new();
    public HashSet<string> ProcessedTransactions { get; set; } = new();
    public int ExtraCharacterSlots { get; set; }
    public int ExtraBankTabs { get; set; }

    public int CharacterSlots => BaseCharacterSlots + ExtraCharacterSlots;
    public int BankTabs => BaseBankTabs + ExtraBankTabs;

    /// <summary>Applies a real-money purchase verified by a platform (receipt or webhook).</summary>
    public DomainResult ApplyPlatformPurchase(string platform, string transactionId, StoreSkuDefinition sku)
    {
        string key = platform + ":" + transactionId;
        if (ProcessedTransactions.Contains(key))
        {
            return DomainResult.Fail("store.duplicate", "Transaction already processed.");
        }
        if (sku.PriceCents <= 0)
        {
            return DomainResult.Fail("store.not_real_money", "SKU is not sold for real money.");
        }
        ProcessedTransactions.Add(key);
        Grant(sku);
        return DomainResult.Ok;
    }

    public DomainResult BuyWithCrystals(StoreSkuDefinition sku)
    {
        if (sku.PriceCrystals <= 0)
        {
            return DomainResult.Fail("store.not_for_crystals", "SKU is not sold for crystals.");
        }
        if (sku.Kind != SkuKind.Convenience && sku.Grants.Count > 0 && sku.Grants.All(OwnedCosmetics.Contains))
        {
            return DomainResult.Fail("store.owned", "Already owned.");
        }
        if (Crystals < sku.PriceCrystals)
        {
            return DomainResult.Fail("store.insufficient_crystals", "Not enough crystals.");
        }
        Crystals -= sku.PriceCrystals;
        Grant(sku);
        return DomainResult.Ok;
    }

    private void Grant(StoreSkuDefinition sku)
    {
        Crystals += sku.CrystalAmount;
        OwnedCosmetics.UnionWith(sku.Grants);
        if (sku.Id == CharacterSlotSkuId)
        {
            ExtraCharacterSlots++;
        }
        else if (sku.Id == BankTabSkuId)
        {
            ExtraBankTabs++;
        }
    }
}
