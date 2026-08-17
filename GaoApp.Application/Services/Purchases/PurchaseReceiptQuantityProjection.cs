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
        if (orderedQuantity < 0m || confirmedReceivedQuantity < 0m ||
            shortClosedQuantity < 0m || inFlightQuantity < 0m)
            throw new InvalidOperationException("Dữ liệu số lượng đơn đặt hàng không nhất quán.");
        var ordered = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(orderedQuantity);
        var confirmed = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(confirmedReceivedQuantity);
        var shortClosed = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(shortClosedQuantity);
        var inFlight = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(inFlightQuantity);
        if (ordered < 0m || confirmed < 0m || shortClosed < 0m || inFlight < 0m ||
            confirmed + shortClosed + inFlight > ordered)
            throw new InvalidOperationException("Dữ liệu số lượng đơn đặt hàng không nhất quán.");
        var available = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
            Math.Max(0m, ordered - confirmed - shortClosed - inFlight));
        return new(ordered, confirmed, shortClosed, inFlight, available);
    }
}
