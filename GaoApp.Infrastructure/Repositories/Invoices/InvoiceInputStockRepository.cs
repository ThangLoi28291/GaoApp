using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public sealed class InvoiceInputStockRepository : IInvoiceInputStockRepository
{
    private readonly AppDbContext _db;

    public InvoiceInputStockRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task LockStoreForIssueAsync(
        int storeId,
        CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
        {
            _ = await _db.Stores
                .AsNoTracking()
                .AnyAsync(x => x.Id == storeId && !x.IsDeleted, ct);
            return;
        }

        // Store là điểm khóa ổn định, tồn tại sẵn và không cần thêm bảng reservation.
        // UPDLOCK + HOLDLOCK giữ khóa đến khi transaction phát hành commit/rollback.
        _ = await _db.Stores
            .FromSqlInterpolated($@"
                SELECT TOP (1) *
                FROM [Stores] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                WHERE [Id] = {storeId}
                  AND [IsDeleted] = 0")
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }

    public async Task<InvoiceInputStockAvailabilityDto> GetAvailabilityAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        var header = await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x => x.Id == invoiceHeadId && !x.IsDeleted)
            .Select(x => new InvoiceHeaderSnapshot
            {
                Id = x.Id,
                StoreId = x.StoreId,
                OriginalInvoiceHeadId = x.OriginalInvoiceHeadId,
                DefaultWarehouseId = x.LegalEntity != null
                    ? x.LegalEntity.DefaultWarehouseId
                    : null,
                OrderWarehouseId = x.Order.POSShift.WarehouseId
            })
            .SingleOrDefaultAsync(ct);

        if (header == null)
        {
            return new InvoiceInputStockAvailabilityDto
            {
                InvoiceHeadId = invoiceHeadId
            };
        }

        // Hóa đơn điều chỉnh/thay thế không tiêu tồn đầu vào lần hai.
        if (header.OriginalInvoiceHeadId.HasValue)
        {
            return new InvoiceInputStockAvailabilityDto
            {
                InvoiceHeadId = invoiceHeadId
            };
        }

        var currentDetails = await QueryCheckableDetails(invoiceHeadId)
            .ToListAsync(ct);

        var required = ResolveAndGroupDetails(currentDetails, header)
            .Where(x => x.BaseQuantity > 0m)
            .ToList();

        if (required.Count == 0)
        {
            return new InvoiceInputStockAvailabilityDto
            {
                InvoiceHeadId = invoiceHeadId
            };
        }

        var warehouseIds = required.Select(x => x.WarehouseId).Distinct().ToList();
        var variantIds = required.Select(x => x.ProductVariantId).Distinct().ToList();

        var eligibleInbound = await (
                from lineMap in _db.StockDocumentLineInputInvoiceMaps.AsNoTracking()
                join line in _db.StockDocumentLines.AsNoTracking()
                    on lineMap.StockDocumentLineId equals line.Id
                join document in _db.StockDocuments.AsNoTracking()
                    on lineMap.StockDocumentId equals document.Id
                where lineMap.StoreId == header.StoreId
                      && !lineMap.IsDeleted
                      && lineMap.UseInputInvoice
                      && !line.IsDeleted
                      && !document.IsDeleted
                      && document.Type == StockDocumentType.Receipt
                      && document.Status == StockDocumentStatus.Confirmed
                      && warehouseIds.Contains(document.WarehouseId)
                      && variantIds.Contains(line.ProductVariantId)
                      && _db.StockDocumentInputInvoiceMaps.Any(headMap =>
                          headMap.StoreId == header.StoreId
                          && headMap.StockDocumentId == document.Id
                          && !headMap.IsDeleted
                          && !headMap.InputInvoiceHead.IsDeleted)
                group line by new
                {
                    document.WarehouseId,
                    line.ProductVariantId
                }
                into grouped
                select new QuantitySnapshot
                {
                    WarehouseId = grouped.Key.WarehouseId,
                    ProductVariantId = grouped.Key.ProductVariantId,
                    BaseQuantity = grouped.Sum(x => x.BaseQuantity)
                })
            .ToListAsync(ct);

        var candidateHeaders = await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                x.StoreId == header.StoreId
                && x.Id != invoiceHeadId
                && !x.IsDeleted
                && x.OriginalInvoiceHeadId == null
                && (
                    x.ProviderStatus == InvoiceProviderStatus.Issuing
                    || x.ProviderStatus == InvoiceProviderStatus.IssuedWaitingNumber
                    || x.ProviderStatus == InvoiceProviderStatus.Issued
                    || x.ProviderStatus == InvoiceProviderStatus.PdfDownloaded
                    || x.ProviderStatus == InvoiceProviderStatus.ZipDownloaded
                    || x.ProviderStatus == InvoiceProviderStatus.EmailSent
                    || x.ProviderStatus == InvoiceProviderStatus.IssueFailed
                    || x.ProviderInvoiceNo != null))
            .Select(x => new CommittedHeaderSnapshot
            {
                Id = x.Id,
                ProviderStatus = x.ProviderStatus,
                ProviderInvoiceNo = x.ProviderInvoiceNo,
                LastErrorCode = x.LastErrorCode,
                LastErrorMessage = x.LastErrorMessage,
                DefaultWarehouseId = x.LegalEntity != null
                    ? x.LegalEntity.DefaultWarehouseId
                    : null,
                OrderWarehouseId = x.Order.POSShift.WarehouseId
            })
            .ToListAsync(ct);

        var committedHeaders = candidateHeaders
            .Where(IsCommitted)
            .ToDictionary(x => x.Id);

        var committedOutbound = new List<QuantitySnapshot>();
        if (committedHeaders.Count > 0)
        {
            var committedDetails = await _db.InvoiceDetails
                .AsNoTracking()
                .Where(x =>
                    committedHeaders.Keys.Contains(x.InvoiceHeadId)
                    && !x.IsDeleted
                    && x.ProductVariantId.HasValue
                    && x.ProductVariant!.HasInputInvoice)
                .Select(x => new DetailSnapshot
                {
                    InvoiceHeadId = x.InvoiceHeadId,
                    ProductVariantId = x.ProductVariantId!.Value,
                    ItemName = x.ItemName,
                    Quantity = x.Quantity,
                    AllocationWarehouseId = x.OrderLegalEntityAllocation != null
                        ? x.OrderLegalEntityAllocation.WarehouseId
                        : null,
                    AllocationBaseQuantity = x.OrderLegalEntityAllocation != null
                        ? x.OrderLegalEntityAllocation.BaseQuantity
                        : null,
                    OrderLineBaseQuantity = x.OrderLine != null
                        ? x.OrderLine.BaseQuantity
                        : null,
                    OrderLineQuantity = x.OrderLine != null
                        ? x.OrderLine.Quantity
                        : null
                })
                .ToListAsync(ct);

            committedOutbound = committedDetails
                .Select(x => ResolveDetail(x, committedHeaders[x.InvoiceHeadId]))
                .Where(x => x.BaseQuantity > 0m)
                .GroupBy(x => new { x.WarehouseId, x.ProductVariantId })
                .Select(x => new QuantitySnapshot
                {
                    WarehouseId = x.Key.WarehouseId,
                    ProductVariantId = x.Key.ProductVariantId,
                    BaseQuantity = x.Sum(y => y.BaseQuantity)
                })
                .ToList();
        }

        var lines = required
            .Select(item => new InvoiceInputStockAvailabilityLineDto
            {
                WarehouseId = item.WarehouseId,
                ProductVariantId = item.ProductVariantId,
                ItemName = item.ItemName,
                RequiredBaseQuantity = item.BaseQuantity,
                EligibleInboundBaseQuantity = eligibleInbound
                    .Where(x =>
                        x.WarehouseId == item.WarehouseId
                        && x.ProductVariantId == item.ProductVariantId)
                    .Sum(x => x.BaseQuantity),
                CommittedOutboundBaseQuantity = committedOutbound
                    .Where(x =>
                        x.WarehouseId == item.WarehouseId
                        && x.ProductVariantId == item.ProductVariantId)
                    .Sum(x => x.BaseQuantity)
            })
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.ProductVariantId)
            .ToList();

        return new InvoiceInputStockAvailabilityDto
        {
            InvoiceHeadId = invoiceHeadId,
            Lines = lines
        };
    }

    private IQueryable<DetailSnapshot> QueryCheckableDetails(int invoiceHeadId)
        => _db.InvoiceDetails
            .AsNoTracking()
            .Where(x =>
                x.InvoiceHeadId == invoiceHeadId
                && !x.IsDeleted
                && x.ProductVariantId.HasValue
                && x.ProductVariant!.HasInputInvoice)
            .Select(x => new DetailSnapshot
            {
                InvoiceHeadId = x.InvoiceHeadId,
                ProductVariantId = x.ProductVariantId!.Value,
                ItemName = x.ItemName,
                Quantity = x.Quantity,
                AllocationWarehouseId = x.OrderLegalEntityAllocation != null
                    ? x.OrderLegalEntityAllocation.WarehouseId
                    : null,
                AllocationBaseQuantity = x.OrderLegalEntityAllocation != null
                    ? x.OrderLegalEntityAllocation.BaseQuantity
                    : null,
                OrderLineBaseQuantity = x.OrderLine != null
                    ? x.OrderLine.BaseQuantity
                    : null,
                OrderLineQuantity = x.OrderLine != null
                    ? x.OrderLine.Quantity
                    : null
            });

    private static IEnumerable<ResolvedDetailSnapshot> ResolveAndGroupDetails(
        IEnumerable<DetailSnapshot> details,
        InvoiceHeaderSnapshot header)
        => details
            .Select(x => ResolveDetail(x, header))
            .GroupBy(x => new { x.WarehouseId, x.ProductVariantId })
            .Select(x => new ResolvedDetailSnapshot
            {
                WarehouseId = x.Key.WarehouseId,
                ProductVariantId = x.Key.ProductVariantId,
                ItemName = x.Select(y => y.ItemName).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y))
                           ?? $"Variant #{x.Key.ProductVariantId}",
                BaseQuantity = x.Sum(y => y.BaseQuantity)
            });

    private static ResolvedDetailSnapshot ResolveDetail(
        DetailSnapshot detail,
        IWarehouseContext header)
    {
        var warehouseId = detail.AllocationWarehouseId
                          ?? header.DefaultWarehouseId
                          ?? header.OrderWarehouseId;

        var baseQuantity = detail.AllocationBaseQuantity
                           ?? ResolveLegacyBaseQuantity(detail);

        return new ResolvedDetailSnapshot
        {
            WarehouseId = warehouseId,
            ProductVariantId = detail.ProductVariantId,
            ItemName = detail.ItemName,
            BaseQuantity = Math.Abs(baseQuantity)
        };
    }

    private static decimal ResolveLegacyBaseQuantity(DetailSnapshot detail)
    {
        if (detail.OrderLineBaseQuantity.HasValue
            && detail.OrderLineQuantity.HasValue
            && detail.OrderLineQuantity.Value != 0m)
        {
            var multiplier = detail.OrderLineBaseQuantity.Value
                             / detail.OrderLineQuantity.Value;
            return detail.Quantity * multiplier;
        }

        // Dòng thêm tay không có quy đổi: Quantity được xem là base unit.
        return detail.Quantity;
    }

    private static bool IsCommitted(CommittedHeaderSnapshot header)
    {
        if (!string.IsNullOrWhiteSpace(header.ProviderInvoiceNo))
            return true;

        if (header.ProviderStatus is
            InvoiceProviderStatus.Issuing or
            InvoiceProviderStatus.IssuedWaitingNumber or
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent)
        {
            return true;
        }

        if (header.ProviderStatus != InvoiceProviderStatus.IssueFailed)
            return false;

        var code = header.LastErrorCode ?? string.Empty;
        var message = header.LastErrorMessage ?? string.Empty;

        return code.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase)
               || code.StartsWith("HTTP_5", StringComparison.OrdinalIgnoreCase)
               || code.Equals("HTTP_500", StringComparison.OrdinalIgnoreCase)
               || code.Equals("VIETTEL_SERVER_500", StringComparison.OrdinalIgnoreCase)
               || message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase);
    }

    private interface IWarehouseContext
    {
        int? DefaultWarehouseId { get; }
        int OrderWarehouseId { get; }
    }

    private sealed class InvoiceHeaderSnapshot : IWarehouseContext
    {
        public int Id { get; init; }
        public int StoreId { get; init; }
        public int? OriginalInvoiceHeadId { get; init; }
        public int? DefaultWarehouseId { get; init; }
        public int OrderWarehouseId { get; init; }
    }

    private sealed class CommittedHeaderSnapshot : IWarehouseContext
    {
        public int Id { get; init; }
        public InvoiceProviderStatus ProviderStatus { get; init; }
        public string? ProviderInvoiceNo { get; init; }
        public string? LastErrorCode { get; init; }
        public string? LastErrorMessage { get; init; }
        public int? DefaultWarehouseId { get; init; }
        public int OrderWarehouseId { get; init; }
    }

    private sealed class DetailSnapshot
    {
        public int InvoiceHeadId { get; init; }
        public int ProductVariantId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public int? AllocationWarehouseId { get; init; }
        public decimal? AllocationBaseQuantity { get; init; }
        public decimal? OrderLineBaseQuantity { get; init; }
        public decimal? OrderLineQuantity { get; init; }
    }

    private sealed class ResolvedDetailSnapshot
    {
        public int WarehouseId { get; init; }
        public int ProductVariantId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public decimal BaseQuantity { get; init; }
    }

    private sealed class QuantitySnapshot
    {
        public int WarehouseId { get; init; }
        public int ProductVariantId { get; init; }
        public decimal BaseQuantity { get; init; }
    }
}
