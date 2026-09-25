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
                LegalEntityId = x.LegalEntityId,
                OriginalInvoiceHeadId = x.OriginalInvoiceHeadId,
                DefaultWarehouseId = x.LegalEntity != null
                    ? x.LegalEntity.DefaultWarehouseId
                    : null,
                OrderWarehouseId = x.Order != null ? x.Order.POSShift.WarehouseId : null,
                // Legacy/old POS drafts can have no OrderLegalEntityAllocation and
                // no navigable Order. Keep the stock preflight aligned with the
                // XML-stock screen by using the store's explicit default warehouse
                // as a compatibility fallback. Never guess from product stock.
                StoreDefaultWarehouseId = _db.Warehouses
                    .Where(w => w.StoreId == x.StoreId && !w.IsDeleted && w.IsActive && w.IsDefault
                        && (!x.LegalEntityId.HasValue || w.LegalEntityId == x.LegalEntityId.Value))
                    .OrderBy(w => w.Id)
                    .Select(w => (int?)w.Id)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(ct);

        if (header == null)
        {
            return new InvoiceInputStockAvailabilityDto
            {
                InvoiceHeadId = invoiceHeadId
            };
        }

        // Some older stores have exactly one active warehouse but never marked
        // it as IsDefault. That is still an unambiguous warehouse identity, so
        // use it as a compatibility fallback. If there is more than one, do
        // not guess: the draft remains blocked until its source warehouse is
        // configured explicitly.
        if (!header.StoreDefaultWarehouseId.HasValue)
        {
            var activeWarehouseIds = await _db.Warehouses
                .AsNoTracking()
                .Where(w => w.StoreId == header.StoreId && !w.IsDeleted && w.IsActive
                    && (!header.LegalEntityId.HasValue || w.LegalEntityId == header.LegalEntityId.Value))
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .Take(2)
                .ToListAsync(ct);
            if (activeWarehouseIds.Count == 1)
                header.StoreDefaultWarehouseId = activeWarehouseIds[0];
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

        var variantIds = required
            .Select(x => x.ProductVariantId)
            .Distinct()
            .ToArray();
        var warehouseIds = required
            .Select(x => x.WarehouseId)
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        var movements = await new InvoiceInputStockReadRepository(_db)
            .GetMovementsAsync(header.StoreId, variantIds, warehouseIds, ct);
        var eligibleInbound = movements.Where(x => x.Kind == "increase" && (!header.LegalEntityId.HasValue || x.LegalEntityId == header.LegalEntityId))
            .Select(x => new QuantitySnapshot { WarehouseId = x.WarehouseId, ProductVariantId = x.ProductVariantId, BaseQuantity = x.Change }).ToList();
        var committedOutbound = movements.Where(x => x.InvoiceHeadId != invoiceHeadId && (x.Kind == "decrease" || x.Kind == "hold"))
            .Select(x => new QuantitySnapshot { WarehouseId = x.WarehouseId, ProductVariantId = x.ProductVariantId, BaseQuantity = -x.Change + x.Held }).ToList();

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
                && x.ProductVariantId.HasValue)
            .Select(x => new DetailSnapshot
            {
                InvoiceHeadId = x.InvoiceHeadId,
                ProductVariantId = x.ProductVariantId!.Value,
                ItemName = x.ItemName,
                Quantity = x.Quantity,
                LegacyUnitFactor = x.LegacyUnitFactor,
                AllocationWarehouseId = x.OrderLegalEntityAllocation != null
                    ? x.OrderLegalEntityAllocation.WarehouseId
                    : null,
                OrderLineWarehouseId = x.OrderLine != null && x.OrderLine.Order != null
                    ? x.OrderLine.Order.POSShift.WarehouseId
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
                          ?? detail.OrderLineWarehouseId
                          ?? header.OrderWarehouseId;

        warehouseId ??= header.StoreDefaultWarehouseId;

        var baseQuantity = detail.AllocationBaseQuantity
                           ?? ResolveLegacyBaseQuantity(detail);

        return new ResolvedDetailSnapshot
        {
            WarehouseId = warehouseId ?? 0,
            ProductVariantId = detail.ProductVariantId,
            ItemName = detail.ItemName,
            BaseQuantity = Math.Abs(baseQuantity)
        };
    }

    private static decimal ResolveLegacyBaseQuantity(DetailSnapshot detail)
    {
        if (detail.LegacyUnitFactor.HasValue) return detail.Quantity * detail.LegacyUnitFactor.Value;
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


    private interface IWarehouseContext
    {
        int? DefaultWarehouseId { get; }
        int? OrderWarehouseId { get; }
        int? StoreDefaultWarehouseId { get; }
    }

    private sealed class InvoiceHeaderSnapshot : IWarehouseContext
    {
        public int Id { get; init; }
        public int StoreId { get; init; }
        public int? LegalEntityId { get; init; }
        public int? OriginalInvoiceHeadId { get; init; }
        public int? DefaultWarehouseId { get; init; }
        public int? OrderWarehouseId { get; init; }
        public int? StoreDefaultWarehouseId { get; set; }
    }


    private sealed class DetailSnapshot
    {
        public int InvoiceHeadId { get; init; }
        public int ProductVariantId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public decimal? LegacyUnitFactor { get; init; }
        public int? AllocationWarehouseId { get; init; }
        public int? OrderLineWarehouseId { get; init; }
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
