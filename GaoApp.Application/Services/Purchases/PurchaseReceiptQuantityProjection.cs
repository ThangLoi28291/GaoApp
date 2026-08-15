namespace GaoApp.Application.Services.Purchases;

public sealed record PurchaseReceiptQuantityProjection(
    decimal OrderedQuantity,
    decimal ConfirmedReceivedQuantity,
    decimal ShortClosedQuantity,
    decimal InFlightQuantity,
    decimal AvailableToAllocateQuantity)
{
    public static PurchaseReceiptQuantityProjection Create(
        decimal orderedQuantity,
        decimal confirmedReceivedQuantity,
        decimal shortClosedQuantity,
        decimal inFlightQuantity)
    {
        var ordered = PurchasePricingPolicy.RoundQuantity(orderedQuantity);
        var confirmed = PurchasePricingPolicy.RoundQuantity(confirmedReceivedQuantity);
        var shortClosed = PurchasePricingPolicy.RoundQuantity(shortClosedQuantity);
        var inFlight = PurchasePricingPolicy.RoundQuantity(inFlightQuantity);
        if (ordered < 0m || confirmed < 0m || shortClosed < 0m || inFlight < 0m ||
            confirmed + shortClosed + inFlight > ordered)
            throw new InvalidOperationException("Dữ liệu số lượng đơn đặt hàng không nhất quán.");
        var available = PurchasePricingPolicy.RoundQuantity(
            Math.Max(0m, ordered - confirmed - shortClosed - inFlight));
        return new(ordered, confirmed, shortClosed, inFlight, available);
    }
}
