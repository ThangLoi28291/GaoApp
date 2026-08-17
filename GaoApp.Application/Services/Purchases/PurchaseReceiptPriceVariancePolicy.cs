namespace GaoApp.Application.Services.Purchases;

/// <summary>
/// Compares persisted-money prices in the receipt unit. Historical prices are
/// supplied in base-unit terms and converted to the current line unit so the
/// authoritative decision matches the value shown to the approver.
/// </summary>
public static class PurchaseReceiptPriceVariancePolicy
{
    private const string EvidencePrefix = "PriceVariance";
    public static PurchaseReceiptPriceVarianceDecision? Evaluate(
        int lineNo,
        decimal currentUnitPriceBeforeVat,
        decimal currentFactor,
        decimal? lastPurchaseBaseUnitPriceBeforeVat)
    {
        if (currentFactor <= 0m)
            throw new ArgumentOutOfRangeException(nameof(currentFactor));

        if (!lastPurchaseBaseUnitPriceBeforeVat.HasValue ||
            lastPurchaseBaseUnitPriceBeforeVat.Value <= 0m)
        {
            return null;
        }

        var previousUnitPrice = ToReceiptUnitPrice(
            currentFactor,
            lastPurchaseBaseUnitPriceBeforeVat.Value);
        var currentUnitPrice = PurchasePricingPolicy.RoundMoney(
            currentUnitPriceBeforeVat);
        var difference = PurchasePricingPolicy.RoundMoney(
            currentUnitPrice - previousUnitPrice);
        if (difference == 0m) return null;

        return new PurchaseReceiptPriceVarianceDecision(
            lineNo,
            previousUnitPrice,
            currentUnitPrice,
            difference);
    }

    public static decimal ToReceiptUnitPrice(
        decimal currentFactor,
        decimal lastPurchaseBaseUnitPriceBeforeVat)
    {
        if (currentFactor <= 0m)
            throw new ArgumentOutOfRangeException(nameof(currentFactor));
        if (lastPurchaseBaseUnitPriceBeforeVat <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(lastPurchaseBaseUnitPriceBeforeVat));

        try
        {
            return PurchasePricingPolicy.RoundMoney(
                lastPurchaseBaseUnitPriceBeforeVat * currentFactor);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastPurchaseBaseUnitPriceBeforeVat));
        }
    }

    public static IReadOnlyDictionary<string, object?> BuildAuditEvidence(
        IEnumerable<PurchaseReceiptPriceVarianceDecision> decisions)
    {
        var evidence = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var decision in decisions.OrderBy(x => x.LineNo))
        {
            var prefix = $"{EvidencePrefix}.Line.{decision.LineNo}";
            if (!evidence.TryAdd(
                    $"{prefix}.PreviousUnitPriceBeforeVat",
                    decision.PreviousUnitPriceBeforeVat) ||
                !evidence.TryAdd(
                    $"{prefix}.CurrentUnitPriceBeforeVat",
                    decision.CurrentUnitPriceBeforeVat))
            {
                throw new InvalidOperationException(
                    $"Duplicate price-variance evidence for line {decision.LineNo}.");
            }
        }

        return evidence;
    }
}

public sealed record PurchaseReceiptPriceVarianceDecision(
    int LineNo,
    decimal PreviousUnitPriceBeforeVat,
    decimal CurrentUnitPriceBeforeVat,
    decimal Difference);
