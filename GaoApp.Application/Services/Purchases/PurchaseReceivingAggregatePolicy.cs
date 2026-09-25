namespace GaoApp.Application.Services.Purchases;

public static class PurchaseReceivingAggregatePolicy
{
    public static PurchaseReceivingAggregateResult Calculate(
        decimal orderedQuantity,
        decimal orderedFactor,
        decimal confirmedOrderedQuantity,
        decimal shortClosedOrderedQuantity,
        decimal otherInFlightBaseQuantity,
        IEnumerable<PurchaseReceivingComponentQuantity> components)
    {
        var materialized = components.ToArray();
        var factor = PurchaseReceiptQuantityConversionPolicy.ValidateFactor(orderedFactor);
        var sameUnitQuantity = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
            materialized.Where(x => x.IsOrderedUnit).Sum(x => x.ReceiptQuantity));
        var alternateBase = SumCanonical(materialized.Where(x => !x.IsOrderedUnit)
            .Select(x => PurchaseReceiptQuantityConversionPolicy.ToCanonical(
                x.ReceiptQuantity, x.Factor)));
        var alternateOrdered = alternateBase <= 0m
            ? 0m
            : PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(alternateBase, factor);
        var orderedEquivalent = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
            checked(sameUnitQuantity + alternateOrdered));
        var bases = materialized.Select(x =>
            PurchaseReceiptQuantityConversionPolicy.ToCanonical(x.ReceiptQuantity, x.Factor));
        return CalculateCore(
            orderedQuantity, orderedFactor, confirmedOrderedQuantity,
            shortClosedOrderedQuantity, otherInFlightBaseQuantity, bases,
            orderedEquivalent);
    }

    public static PurchaseReceivingAggregateResult Calculate(
        decimal orderedQuantity,
        decimal orderedFactor,
        decimal confirmedOrderedQuantity,
        decimal shortClosedOrderedQuantity,
        decimal otherInFlightBaseQuantity,
        IEnumerable<decimal> componentBaseQuantities)
        => CalculateCore(
            orderedQuantity, orderedFactor, confirmedOrderedQuantity,
            shortClosedOrderedQuantity, otherInFlightBaseQuantity,
            componentBaseQuantities, currentReceiptOrderedEquivalent: null);

    private static PurchaseReceivingAggregateResult CalculateCore(
        decimal orderedQuantity,
        decimal orderedFactor,
        decimal confirmedOrderedQuantity,
        decimal shortClosedOrderedQuantity,
        decimal otherInFlightBaseQuantity,
        IEnumerable<decimal> componentBaseQuantities,
        decimal? currentReceiptOrderedEquivalent)
    {
        var factor = PurchaseReceiptQuantityConversionPolicy.ValidateFactor(orderedFactor);
        var orderedBase = PurchaseReceiptQuantityConversionPolicy.ToCanonical(orderedQuantity, factor);
        var confirmedBase = confirmedOrderedQuantity <= 0m
            ? 0m
            : PurchaseReceiptQuantityConversionPolicy.ToCanonical(confirmedOrderedQuantity, factor);
        var shortClosedBase = shortClosedOrderedQuantity <= 0m
            ? 0m
            : PurchaseReceiptQuantityConversionPolicy.ToCanonical(shortClosedOrderedQuantity, factor);
        var otherInFlight = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(
            otherInFlightBaseQuantity);
        var current = SumCanonical(componentBaseQuantities);
        var confirmedAfter = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(
            checked(confirmedBase + current));
        var projectedAfter = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(
            checked(confirmedBase + otherInFlight + current));
        var remaining = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
            Math.Max(0m, orderedBase - confirmedBase - shortClosedBase - otherInFlight - current));
        var confirmedOverdelivery = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
            Math.Max(0m, confirmedAfter - orderedBase));
        var projectedOverdelivery = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
            Math.Max(0m, projectedAfter - orderedBase));

        var orderedEquivalent = currentReceiptOrderedEquivalent ?? 0m;
        if (current > 0m)
        {
            if (!currentReceiptOrderedEquivalent.HasValue)
                orderedEquivalent = PurchaseReceiptQuantityConversionPolicy.ToOrderedEquivalent(current, factor);
            _ = PurchaseReceiptQuantityConversionPolicy.EnsureCumulativeOrderedInvariant(
                confirmedOrderedQuantity, orderedEquivalent, current, factor);
        }

        return new PurchaseReceivingAggregateResult(
            orderedBase,
            confirmedBase,
            shortClosedBase,
            otherInFlight,
            current,
            orderedEquivalent,
            remaining,
            confirmedOverdelivery,
            projectedOverdelivery);
    }

    private static decimal SumCanonical(IEnumerable<decimal> quantities)
    {
        decimal total = 0m;
        checked
        {
            foreach (var quantity in quantities)
                total += PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(quantity);
        }
        return PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(total);
    }
}

public sealed record PurchaseReceivingComponentQuantity(
    decimal ReceiptQuantity,
    decimal Factor,
    bool IsOrderedUnit);

public sealed record PurchaseReceivingAggregateResult(
    decimal OrderedBaseQuantity,
    decimal ConfirmedBaseQuantity,
    decimal ShortClosedBaseQuantity,
    decimal OtherInFlightBaseQuantity,
    decimal CurrentReceiptBaseQuantity,
    decimal CurrentReceiptOrderedEquivalent,
    decimal RemainingBaseQuantity,
    decimal ConfirmedOverdeliveryAfterReceipt,
    decimal ProjectedOverdeliveryAfterReceipt);
