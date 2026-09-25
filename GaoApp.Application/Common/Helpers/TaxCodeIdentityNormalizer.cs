namespace GaoApp.Application.Common.Helpers;

/// <summary>
/// Canonical tax-code identity used by both XML invoice identity and Supplier
/// resolution. Display values remain untouched by this component.
/// </summary>
public static class TaxCodeIdentityNormalizer
{
    public static string? Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return null;

        normalized = normalized
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized.Length == 0 ? null : normalized;
    }
}
