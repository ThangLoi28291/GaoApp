namespace GaoApp.Application.Services.Inventory;

public static class PurchaseReceiptCatalogReviewPolicy
{
    private const string LegacyPrefix = "Sản phẩm mới";

    public static bool IsLegacyPlaceholderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;

        var normalized = value.Trim();
        return normalized.Equals(LegacyPrefix, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith($"{LegacyPrefix} (", StringComparison.OrdinalIgnoreCase);
    }

    public static PurchaseReceiptCatalogPresentation Resolve(
        string? snapshotName,
        string? variantName,
        string? productName)
    {
        var officialVariantName = IsLegacyPlaceholderName(variantName) ? null : variantName?.Trim();
        var officialProductName = IsLegacyPlaceholderName(productName) ? null : productName?.Trim();
        var displayName = officialVariantName ?? officialProductName ?? snapshotName?.Trim() ?? LegacyPrefix;
        var usesLegacySnapshot = IsLegacyPlaceholderName(snapshotName);

        return new PurchaseReceiptCatalogPresentation(
            displayName,
            officialProductName,
            officialVariantName,
            officialVariantName is null && officialProductName is null && usesLegacySnapshot,
            usesLegacySnapshot);
    }
}

public sealed record PurchaseReceiptCatalogPresentation(
    string DisplayName,
    string? ProductName,
    string? VariantName,
    bool RequiresReview,
    bool UsesLegacySnapshot);
