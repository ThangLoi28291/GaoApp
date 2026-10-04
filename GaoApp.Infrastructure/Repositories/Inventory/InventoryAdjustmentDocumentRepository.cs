using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// EF Core repository cho phiếu điều chỉnh kho.
/// Lưu ý:
/// - AppDbContext đã có global filter StoreId + IsDeleted.
/// - Không cần tự filter StoreId thủ công nếu đang chạy trong tenant context.
/// </summary>
public class InventoryAdjustmentDocumentRepository
    : IInventoryAdjustmentDocumentRepository
{
    private readonly AppDbContext _db;

    public InventoryAdjustmentDocumentRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<InventoryAdjustmentDocument?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        return _db.InventoryAdjustmentDocuments
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<InventoryAdjustmentDocument?> GetDetailEntityAsync(
        int id,
        CancellationToken ct = default)
    {
        return _db.InventoryAdjustmentDocuments
            .Include(x => x.Warehouse)
            .Include(x => x.Lines)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Unit)
            .Include(x => x.Lines)
                .ThenInclude(x => x.ProductUnitConversion)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<InventoryAdjustmentDocumentDetailDto?> GetDetailDtoAsync(
        int id,
        CancellationToken ct = default)
    {
        var document = await _db.InventoryAdjustmentDocuments
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Unit)
          .Include(x => x.Lines)
    .ThenInclude(x => x.ProductUnitConversion!)
        .ThenInclude(x => x.Barcodes)
            .Include(x => x.Lines)
    .ThenInclude(x => x.ProductVariant)
        .ThenInclude(x => x.PrimaryProductImage!)
            .ThenInclude(x => x.MediaAsset)
            .Include(x => x.Lines)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
                        .ThenInclude(x => x.ProductImages.Where(pi => !pi.IsDeleted))
                            .ThenInclude(x => x.MediaAsset)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (document == null)
            return null;

        return new InventoryAdjustmentDocumentDetailDto
        {
            Id = document.Id,
            DocumentNo = document.DocumentNo,
            DocumentDate = document.DocumentDate,
            WarehouseId = document.WarehouseId,
            WarehouseName = document.Warehouse?.Name ?? string.Empty,

            AdjustmentType = document.AdjustmentType,
            AdjustmentTypeText = document.AdjustmentType switch
            {
                InventoryTransactionType.AdjustmentIncrease => "Điều chỉnh tăng",
                InventoryTransactionType.AdjustmentDecrease => "Điều chỉnh giảm",
                InventoryTransactionType.Revaluation => "Điều chỉnh giá vốn",
                _ => "Điều chỉnh khác"
            },

            Status = document.Status,
            StatusText = document.Status.ToString(),

            ReasonType = document.ReasonType,
            ReasonTypeText = document.ReasonType.ToString(),

            Note = document.Note,
            ApprovalNote = document.ApprovalNote,

            SubmittedAtUtc = document.SubmittedAtUtc,
            SubmittedByUserId = document.SubmittedByUserId,
            ApprovedAtUtc = document.ApprovedAtUtc,
            ApprovedByUserId = document.ApprovedByUserId,
            RejectedAtUtc = document.RejectedAtUtc,
            RejectedByUserId = document.RejectedByUserId,
            CancelledAtUtc = document.CancelledAtUtc,
            CancelledByUserId = document.CancelledByUserId,

            Lines = document.Lines
                .OrderBy(l => l.Id)
                .Select(l =>
                {
                    var variant = l.ProductVariant;
                    var product = variant.Product;
                    var conversion = l.ProductUnitConversion;

                    var price = conversion?.Price
                        ?? variant.Price
                        ?? product.BasePrice;

                    return new InventoryAdjustmentLineDto
                    {
                        Id = l.Id,
                        ProductVariantId = l.ProductVariantId,
                        Barcode =
    l.ProductUnitConversion != null &&
    l.ProductUnitConversion.Barcodes.Any(x => !x.IsDeleted && x.IsActive)
        ? l.ProductUnitConversion.Barcodes
            .Where(x => !x.IsDeleted && x.IsActive)
            .Select(x => x.Barcode)
            .FirstOrDefault()
        : null,
                        ProductName = product.Name,
                        ProductVariantName = variant.ProductVariantName ?? string.Empty,
                        Sku = variant.Sku,

                        UnitId = l.UnitId,
                        UnitName = l.Unit?.Name ?? string.Empty,
                        ProductUnitConversionId = l.ProductUnitConversionId,

                        Quantity = l.Quantity,
                        Factor = l.Factor,
                        BaseQuantity = l.BaseQuantity,

                        UnitCost = l.UnitCost,
                        ProvisionalUnitCost = l.ProvisionalUnitCost,

                        Price = price,
                        CostPrice = variant.CostPrice,
                        ImageUrl = BuildImageUrl(variant),

                        Note = l.Note
                    };
                })
                .ToList()
        };
    }

    public async Task<PagedResult<InventoryAdjustmentDocumentListItemDto>> GetPagedAsync(
        InventoryAdjustmentDocumentFilterDto filter,
        CancellationToken ct = default)
    {
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;
        pageSize = Math.Min(pageSize, 100);

        var query = _db.InventoryAdjustmentDocuments
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();

            query = query.Where(x =>
                x.DocumentNo.Contains(keyword) ||
                (x.Note != null && x.Note.Contains(keyword)));
        }

        if (filter.WarehouseId.HasValue && filter.WarehouseId.Value > 0)
        {
            query = query.Where(x => x.WarehouseId == filter.WarehouseId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(x => x.Status == filter.Status.Value);
        }

        if (filter.AdjustmentType.HasValue)
        {
            query = query.Where(x => x.AdjustmentType == filter.AdjustmentType.Value);
        }

        if (filter.FromDate.HasValue)
        {
            var from = filter.FromDate.Value.Date;
            query = query.Where(x => x.DocumentDate >= from);
        }

        if (filter.ToDate.HasValue)
        {
            var toExclusive = filter.ToDate.Value.Date.AddDays(1);
            query = query.Where(x => x.DocumentDate < toExclusive);
        }

        var totalItems = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new InventoryAdjustmentDocumentListItemDto
            {
                Id = x.Id,
                DocumentNo = x.DocumentNo,
                DocumentDate = x.DocumentDate,
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name,

                AdjustmentType = x.AdjustmentType,
                AdjustmentTypeText = x.AdjustmentType == InventoryTransactionType.AdjustmentIncrease
                    ? "Điều chỉnh tăng"
                    : x.AdjustmentType == InventoryTransactionType.AdjustmentDecrease
                        ? "Điều chỉnh giảm"
                        : x.AdjustmentType == InventoryTransactionType.Revaluation
                            ? "Điều chỉnh giá vốn"
                            : "Điều chỉnh khác",

                Status = x.Status,
                StatusText = x.Status.ToString(),

                ReasonType = x.ReasonType,
                ReasonTypeText = x.ReasonType.ToString(),

                LineCount = x.Lines.Count,
                Note = x.Note,
                CreatedAtUtc = x.CreatedAtUtc,
                SubmittedAtUtc = x.SubmittedAtUtc,
                ApprovedAtUtc = x.ApprovedAtUtc
            })
            .ToListAsync(ct);

        return new PagedResult<InventoryAdjustmentDocumentListItemDto>
        {
            Items = items,
            TotalItems = totalItems,
            Page = page,
            PageSize = pageSize
        };
    }

    public Task<bool> ExistsDocumentNoAsync(
        string documentNo,
        int? excludeId = null,
        CancellationToken ct = default)
    {
        var query = _db.InventoryAdjustmentDocuments
            .AsNoTracking()
            .Where(x => x.DocumentNo == documentNo);

        if (excludeId.HasValue)
        {
            query = query.Where(x => x.Id != excludeId.Value);
        }

        return query.AnyAsync(ct);
    }

    public async Task AddAsync(
        InventoryAdjustmentDocument document,
        CancellationToken ct = default)
    {
        await _db.InventoryAdjustmentDocuments.AddAsync(document, ct);
    }

    public void Update(InventoryAdjustmentDocument document)
    {
        _db.InventoryAdjustmentDocuments.Update(document);
    }

    public void Remove(InventoryAdjustmentDocument document)
    {
        _db.InventoryAdjustmentDocuments.Remove(document);
    }
    private static string? BuildImageUrl(ProductVariant? variant)
    {
        var storagePath =
            variant?.PrimaryProductImage?.MediaAsset?.StoragePath
            ?? variant?.Product?.ProductImages?
                .Where(x => !x.IsDeleted)
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.SortOrder)
                .Select(x => x.MediaAsset.StoragePath)
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        storagePath = storagePath.Trim();

        if (storagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            storagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return storagePath;
        }

        return "/" + storagePath.TrimStart('/');
    }
}
