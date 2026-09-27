using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public sealed class DraftInvoiceReturnSyncService : IDraftInvoiceReturnSyncService
{
    private const decimal QuantityTolerance = 0.0001m;

    private readonly IInvoiceRepository _invoices;
    private readonly IInvoiceInputStockRepository _inputStock;
    private readonly IOrderLegalEntityAllocationReversalRepository _reversals;
    private readonly ISalesReturnRepository _salesReturns;
    private readonly IAutoInvoiceRepository _autoInvoices;

    public DraftInvoiceReturnSyncService(
     IInvoiceRepository invoices,
     IInvoiceInputStockRepository inputStock,
     IAutoInvoiceRepository autoInvoices,
     IOrderLegalEntityAllocationReversalRepository reversals,
     ISalesReturnRepository salesReturns)
    {
        _invoices = invoices;
        _inputStock = inputStock;
        _reversals = reversals;
        _salesReturns = salesReturns;
        _autoInvoices = autoInvoices;
    }

    public async Task EnsurePosReturnAllowedAsync(
        int storeId,
        int orderId,
        IReadOnlyCollection<int> orderLineIds,
        CancellationToken ct = default)
    {
        var lineIds = NormalizeLineIds(orderLineIds);

        if (lineIds.Count == 0)
            return;

        // Dùng cùng điểm khóa với phát hành HĐĐT.
        // Trong transaction SalesReturn, khóa này ngăn invoice
        // chuyển sang Issuing giữa lúc validate và sync.
        await _inputStock.LockStoreForIssueAsync(
            storeId,
            ct);

        var heads = await _invoices
            .GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(
                orderId,
                ct);

        EnsureReturnedLinesAreNotAccountingManaged(
            heads,
            lineIds);
        await EnsureReturnedLinesAreNotActivelyClaimedAsync(
    storeId,
    heads,
    lineIds,
    ct);
    }

    public async Task SyncAfterReturnAsync(
        int orderId,
        int salesReturnId,
        CancellationToken ct = default)
    {
        var salesReturn =
            await _salesReturns.GetByIdWithDetailsAsync(
                salesReturnId,
                ct)
            ?? throw new InvalidOperationException(
                "Không tìm thấy phiếu trả hàng để đồng bộ hóa đơn nháp.");

        var returnedLineIds = NormalizeLineIds(
            salesReturn.Lines
                .Select(x => x.OrderLineId)
                .ToList());

        if (returnedLineIds.Count == 0)
            return;

        var heads = await _invoices
            .GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(
                orderId,
                ct);

        if (heads.Count == 0)
            return;

        // Chống race: kiểm tra lại ngay trước khi sửa
        // dù đã khóa từ đầu transaction.
        EnsureReturnedLinesAreNotAccountingManaged(
            heads,
            returnedLineIds);
        await EnsureReturnedLinesAreNotActivelyClaimedAsync(
    salesReturn.StoreId,
    heads,
    returnedLineIds,
    ct);

        var order =
            await _invoices.GetOrderWithLinesForInvoiceAsync(
                orderId,
                ct)
            ?? throw new InvalidOperationException(
                "Không tìm thấy order để đồng bộ hóa đơn nháp.");

        var linesById = order.Lines
            .Where(x => !x.IsDeleted)
            .ToDictionary(x => x.Id);

        var reversals =
            await _reversals.GetForOrderAsync(
                orderId,
                ct);

        var reversedByAllocation = reversals
            .GroupBy(x => x.OrderLegalEntityAllocationId)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(y => y.BaseQuantity));

        var returnedBaseByLine =
            new Dictionary<int, decimal>();

        var now = DateTime.UtcNow;

        foreach (var head in heads.Where(
                     x => !IsAccountingManaged(x)))
        {
            var changed = false;

            foreach (var detail in head.Details
                         .Where(x =>
                             !x.IsDeleted
                             && x.SourceType ==
                                InvoiceDetailSourceType.FromOrderLine
                             && x.OrderLineId.HasValue
                             && returnedLineIds.Contains(
                                 x.OrderLineId.Value))
                         .ToList())
            {
                if (!linesById.TryGetValue(
                        detail.OrderLineId!.Value,
                        out var orderLine))
                {
                    throw new InvalidOperationException(
                        $"Không tìm thấy OrderLine #{detail.OrderLineId} " +
                        $"của InvoiceDetail #{detail.Id}.");
                }

                if (detail.OrderLegalEntityAllocationId.HasValue)
                {
                    var allocation =
                        detail.OrderLegalEntityAllocation
                        ?? throw new InvalidOperationException(
                            $"InvoiceDetail #{detail.Id} thiếu allocation gốc " +
                            "để đồng bộ trả hàng.");

                    var reversedBase =
                        reversedByAllocation.GetValueOrDefault(
                            allocation.Id);

                    changed |= ApplyRemainingQuantity(
                        detail,
                        allocation.Quantity,
                        allocation.BaseQuantity,
                        allocation.NetAmount,
                        reversedBase,
                        now);
                }
                else
                {
                    if (!returnedBaseByLine.TryGetValue(
                            orderLine.Id,
                            out var returnedBase))
                    {
                        returnedBase =
                            await _salesReturns
                                .GetReturnedBaseQuantityByOrderLineAsync(
                                    orderLine.Id,
                                    ct);

                        returnedBaseByLine.Add(
                            orderLine.Id,
                            returnedBase);
                    }

                    var soldBase =
                        orderLine.BaseQuantity > 0m
                            ? orderLine.BaseQuantity
                            : orderLine.Quantity *
                              (orderLine.Multiplier <= 0m
                                  ? 1m
                                  : orderLine.Multiplier);

                    changed |= ApplyRemainingQuantity(
                        detail,
                        orderLine.Quantity,
                        soldBase,
                        orderLine.LineTotal,
                        returnedBase,
                        now);
                }
            }

            if (changed)
            {
                InvoiceAmountCalculator.RecalculateHead(
                    head);

                // MinimumAgeMinutes phải tính lại từ lần thay đổi
                // có ảnh hưởng nội dung/số tiền hóa đơn gần nhất.
                head.LastIssuanceRelevantChangeAtUtc =
                    salesReturn.CompletedAtUtc ?? now;

                head.Note = AppendSyncNote(
                    head.Note,
                    salesReturn.ReturnNumber);
            }
        }

        await _invoices.SaveChangesAsync(ct);
    }

    private static bool ApplyRemainingQuantity(
        InvoiceDetail detail,
        decimal originalQuantity,
        decimal originalBaseQuantity,
        decimal originalAmount,
        decimal reversedBaseQuantity,
        DateTime now)
    {
        if (originalBaseQuantity <= 0m)
        {
            throw new InvalidOperationException(
                $"InvoiceDetail #{detail.Id} có base quantity gốc không hợp lệ.");
        }

        var remainingBase =
            Math.Max(
                0m,
                originalBaseQuantity - reversedBaseQuantity);

        // Trả hết phần allocation/detail này.
        if (remainingBase <= QuantityTolerance)
        {
            if (detail.IsDeleted)
                return false;

            detail.IsDeleted = true;
            detail.DeletedAtUtc = now;

            return true;
        }

        var ratio =
            Math.Min(
                1m,
                remainingBase / originalBaseQuantity);

        var remainingQuantity =
            Math.Round(
                originalQuantity * ratio,
                3,
                MidpointRounding.AwayFromZero);

        var remainingAmount =
            Math.Round(
                originalAmount * ratio,
                2,
                MidpointRounding.AwayFromZero);

        var remainingUnitPrice =
            remainingQuantity > 0m
                ? Math.Round(
                    remainingAmount / remainingQuantity,
                    2,
                    MidpointRounding.AwayFromZero)
                : 0m;

        var remainingVatAmount =
            Math.Round(
                remainingAmount *
                detail.VatRate /
                100m,
                2,
                MidpointRounding.AwayFromZero);

        var remainingTotalAmount =
            remainingAmount + remainingVatAmount;

        // Chỉ đánh dấu changed nếu dữ liệu invoice thực sự thay đổi.
        // Điều này tránh reset MinimumAgeMinutes cho sibling allocation
        // không bị tác động bởi phiếu trả.
        var changed =
            detail.Quantity != remainingQuantity
            || detail.UnitPrice != remainingUnitPrice
            || detail.Amount != remainingAmount
            || detail.VatAmount != remainingVatAmount
            || detail.TotalAmount != remainingTotalAmount;

        if (!changed)
            return false;

        detail.Quantity = remainingQuantity;
        detail.UnitPrice = remainingUnitPrice;
        detail.Amount = remainingAmount;
        detail.VatAmount = remainingVatAmount;
        detail.TotalAmount = remainingTotalAmount;

        return true;
    }
    private async Task
    EnsureReturnedLinesAreNotActivelyClaimedAsync(
        int storeId,
        IReadOnlyCollection<InvoiceHead> heads,
        IReadOnlySet<int> returnedLineIds,
        CancellationToken ct)
    {
        var affectedHeadIds = heads
            .Where(head =>
                head.Details.Any(detail =>
                    !detail.IsDeleted &&
                    detail.OrderLineId.HasValue &&
                    returnedLineIds.Contains(
                        detail.OrderLineId.Value)))
            .Select(x => x.Id)
            .Distinct()
            .ToArray();

        foreach (var invoiceHeadId in affectedHeadIds)
        {
            if (!await _autoInvoices.HasActiveSourceAsync(
                    storeId,
                    invoiceHeadId,
                    ct))
            {
                continue;
            }

            throw new InvalidOperationException(
                "Sản phẩm trả thuộc hóa đơn đang được hệ thống " +
                "phát hành. Vui lòng chờ thao tác phát hành hoàn tất " +
                "hoặc xác minh trạng thái trước khi trả hàng.");
        }
    }
    private static void EnsureReturnedLinesAreNotAccountingManaged(
        IReadOnlyCollection<InvoiceHead> heads,
        IReadOnlySet<int> returnedLineIds)
    {
        var protectedHead =
            heads.FirstOrDefault(head =>
                IsAccountingManaged(head)
                && head.Details.Any(detail =>
                    !detail.IsDeleted
                    && detail.OrderLineId.HasValue
                    && returnedLineIds.Contains(
                        detail.OrderLineId.Value)));

        if (protectedHead == null)
            return;

        var invoiceNo =
            protectedHead.ProviderInvoiceNo
            ?? protectedHead.InvoiceNumber
            ?? $"#{protectedHead.Id}";

        throw new InvalidOperationException(
            $"Sản phẩm trả thuộc hóa đơn {invoiceNo} đã phát hành " +
            "hoặc đang chờ xác minh kết quả phát hành. " +
            "Vui lòng xử lý trả hàng bằng nghiệp vụ kế toán/hóa đơn điện tử.");
    }

    private static bool IsAccountingManaged(
        InvoiceHead head)
    {
        if (!string.IsNullOrWhiteSpace(
                head.ProviderInvoiceNo)
            || head.IssuedAtUtc.HasValue)
        {
            return true;
        }

        if (head.ProviderStatus is
            InvoiceProviderStatus.Issuing or
            InvoiceProviderStatus.IssuedWaitingNumber or
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent)
        {
            return true;
        }

        if (head.ProviderStatus !=
            InvoiceProviderStatus.IssueFailed)
        {
            return false;
        }

        var code =
            head.LastErrorCode ?? string.Empty;

        var message =
            head.LastErrorMessage ?? string.Empty;

        return code.Equals(
                   "TIMEOUT",
                   StringComparison.OrdinalIgnoreCase)
               || code.StartsWith(
                   "HTTP_5",
                   StringComparison.OrdinalIgnoreCase)
               || code.Equals(
                   "VIETTEL_SERVER_500",
                   StringComparison.OrdinalIgnoreCase)
               || message.Contains(
                   "timeout",
                   StringComparison.OrdinalIgnoreCase)
               || message.Contains(
                   "HTTP 500",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<int> NormalizeLineIds(
        IReadOnlyCollection<int>? orderLineIds)
        => (orderLineIds ?? Array.Empty<int>())
            .Where(x => x > 0)
            .ToHashSet();

    private static string AppendSyncNote(
        string? current,
        string? returnNumber)
    {
        var marker =
            $"Đã đồng bộ số lượng theo phiếu trả {returnNumber ?? "#"}.";

        if (string.IsNullOrWhiteSpace(current))
            return marker;

        if (current.Contains(
                marker,
                StringComparison.Ordinal))
        {
            return current;
        }

        var combined =
            $"{current.Trim()} {marker}";

        return combined.Length <= 500
            ? combined
            : combined[..500];
    }
}