using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Purchases;

public static class PurchaseReceiptPolicy
{
    public static PurchaseReceiptLineDecision ValidateLine(
        decimal pendingQuantity,
        decimal receivedQuantity,
        PurchaseShortageDisposition shortageDisposition,
        string? shortageReason,
        int lineNo)
    {
        var pending = PurchasePricingPolicy.RoundQuantity(pendingQuantity);
        var received = PurchasePricingPolicy.RoundQuantity(receivedQuantity);
        if (pending <= 0)
            throw new InvalidOperationException($"Dòng {lineNo}: không còn số lượng chờ nhận.");
        if (received <= 0)
            throw new InvalidOperationException($"Dòng {lineNo}: số lượng nhận phải lớn hơn 0.");
        if (received > pending)
            throw new InvalidOperationException($"Dòng {lineNo}: số lượng nhận {received:N3} vượt số còn chờ {pending:N3}.");

        var isShort = received < pending;
        if (isShort && shortageDisposition == PurchaseShortageDisposition.None)
            throw new InvalidOperationException($"Dòng {lineNo}: phải chọn chờ giao bù hoặc đóng phần thiếu.");
        if (isShort && shortageDisposition == PurchaseShortageDisposition.ShortClose && string.IsNullOrWhiteSpace(shortageReason))
            throw new InvalidOperationException($"Dòng {lineNo}: đóng phần thiếu bắt buộc phải có lý do.");

        return new PurchaseReceiptLineDecision(
            received,
            pending,
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

        orderLine.ReceivedQuantity = PurchasePricingPolicy.RoundQuantity(
            orderLine.ReceivedQuantity + decision.ReceivedQuantity);

        if (decision.ShortageDisposition == PurchaseShortageDisposition.ShortClose)
        {
            var shortQuantity = PurchasePricingPolicy.RoundQuantity(
                decision.PendingBefore - decision.ReceivedQuantity);
            orderLine.ShortClosedQuantity = PurchasePricingPolicy.RoundQuantity(
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
        if (activeLines.All(x => PurchasePricingPolicy.RoundQuantity(x.PendingQuantity) == 0m))
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

public static class PurchasePostingIdentity
{
    public static string MerchandisePayable(int stockDocumentId)
        => $"STOCK:{stockDocumentId}:MERCHANDISE";

    public static string FreightPayable(int stockDocumentId)
        => $"STOCK:{stockDocumentId}:FREIGHT";
}
