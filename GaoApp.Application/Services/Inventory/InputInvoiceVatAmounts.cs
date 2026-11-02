using GaoApp.Application.Services.Purchases;

namespace GaoApp.Application.Services.Inventory;

public static class InputInvoiceVatAmounts
{
    public static bool CanDetermineVat(string? rate, decimal reportedVat, decimal amount)
        => reportedVat != 0m || amount == 0m ||
           InputInvoiceVatRateNormalizer.TryNormalize(rate, out _, out _) ||
           rate?.Trim().ToUpperInvariant() is "KCT" or "KKKNT";

    public static decimal VatAmount(decimal amount, string? rate, decimal reportedVat)
    {
        if (reportedVat != 0m) return reportedVat;
        return InputInvoiceVatRateNormalizer.TryNormalize(rate, out var percent, out _)
            ? PurchasePricingPolicy.RoundMoney(checked(amount * percent / 100m))
            : 0m;
    }
}
