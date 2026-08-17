using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public static class PurchaseReceiptPolicy
{
    public static PurchaseReceiptOverdeliveryDecision EvaluateOverdelivery(
        decimal canonicalOrderedQuantity,
        decimal canonicalConfirmedQuantity,
        decimal canonicalReceiptQuantity,
        int lineNo)
    {
        var ordered = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(
            canonicalOrderedQuantity);
        var confirmed = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(
            canonicalConfirmedQuantity);
        var receipt = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(
            canonicalReceiptQuantity);
        var before = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
            Math.Max(0m, confirmed - ordered));
        var after = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
            Math.Max(0m, checked(confirmed + receipt) - ordered));
        return new PurchaseReceiptOverdeliveryDecision(
            lineNo,
            PurchaseReceiptQuantityConversionPolicy.RoundQuantity(after - before),
            after);
    }

    public static PurchaseReceiptLineDecision ValidateLine(
        decimal pendingQuantity,
        decimal receivedQuantity,
        PurchaseShortageDisposition shortageDisposition,
        string? shortageReason,
        int lineNo)
    {
        var pending = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(pendingQuantity);
        var received = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(receivedQuantity);
        if (received <= 0)
            throw new InvalidOperationException($"Dòng {lineNo}: số lượng nhận phải lớn hơn 0.");

        var isShort = pending > 0m && received < pending;
        if (isShort && shortageDisposition == PurchaseShortageDisposition.None)
            throw new InvalidOperationException($"Dòng {lineNo}: phải chọn chờ giao bù hoặc đóng phần thiếu.");
        if (isShort && shortageDisposition == PurchaseShortageDisposition.ShortClose && string.IsNullOrWhiteSpace(shortageReason))
            throw new InvalidOperationException($"Dòng {lineNo}: đóng phần thiếu bắt buộc phải có lý do.");

        return new PurchaseReceiptLineDecision(
            received,
            Math.Max(0m, pending),
            isShort ? shortageDisposition : PurchaseShortageDisposition.None,
            isShort && shortageDisposition == PurchaseShortageDisposition.ShortClose
                ? shortageReason?.Trim()
                : null);
    }

    public static void ApplyApprovedLine(
        PurchaseOrderLine orderLine,
        decimal receivedQuantity,
        PurchaseShortageDisposition shortageDisposition,
        string? shortageReason,
        DateTime occurredAtUtc,
        int? userId)
    {
        var decision = ValidateLine(
            orderLine.PendingQuantity,
            receivedQuantity,
            shortageDisposition,
            shortageReason,
            orderLine.LineNo);

        orderLine.ReceivedQuantity = PurchaseReceiptQuantityConversionPolicy.NormalizeCanonicalQuantity(
            checked(orderLine.ReceivedQuantity + decision.ReceivedQuantity));

        if (decision.ShortageDisposition == PurchaseShortageDisposition.ShortClose)
        {
            var shortQuantity = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
                decision.PendingBefore - decision.ReceivedQuantity);
            orderLine.ShortClosedQuantity = PurchaseReceiptQuantityConversionPolicy.RoundQuantity(
                orderLine.ShortClosedQuantity + shortQuantity);
            orderLine.ShortCloseReason = decision.ShortageReason;
            orderLine.ShortClosedAtUtc = occurredAtUtc;
            orderLine.ShortClosedByUserId = userId;
        }

        orderLine.ReceiptStatus = orderLine.PendingQuantity > 0
            ? PurchaseOrderLineReceiptStatus.PartiallyReceived
            : orderLine.ShortClosedQuantity > 0
                ? PurchaseOrderLineReceiptStatus.ShortClosed
                : PurchaseOrderLineReceiptStatus.FullyReceived;
    }

    public static PurchaseOrderStatus ResolveOrderStatus(
        IReadOnlyCollection<PurchaseOrderLine> activeLines,
        PurchaseOrderStatus currentStatus)
    {
        if (activeLines.Count == 0) return currentStatus;
        if (activeLines.All(x => PurchaseReceiptQuantityConversionPolicy.RoundQuantity(x.PendingQuantity) == 0m))
            return activeLines.Any(x => x.ShortClosedQuantity > 0m)
                ? PurchaseOrderStatus.ShortClosed
                : PurchaseOrderStatus.FullyReceived;
        return activeLines.Any(x => x.ReceivedQuantity > 0m)
            ? PurchaseOrderStatus.PartiallyReceived
            : currentStatus;
    }
}

public sealed record PurchaseReceiptLineDecision(
    decimal ReceivedQuantity,
    decimal PendingBefore,
    PurchaseShortageDisposition ShortageDisposition,
    string? ShortageReason);

public sealed record PurchaseReceiptOverdeliveryDecision(
    int LineNo,
    decimal IncrementalCanonicalQuantity,
    decimal CanonicalQuantityAfterConfirmation);

public static class PurchasePostingIdentity
{
    public static string MerchandisePayable(int stockDocumentId)
        => $"STOCK:{stockDocumentId}:MERCHANDISE";

    public static string FreightPayable(int stockDocumentId)
        => $"STOCK:{stockDocumentId}:FREIGHT";
}
