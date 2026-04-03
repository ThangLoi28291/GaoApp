using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed class ProductVariantBarcodeHistoryRepository : IProductVariantBarcodeHistoryRepository
{
    private readonly AppDbContext _context;

    public ProductVariantBarcodeHistoryRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ProductVariantBarcodeHistory entity, CancellationToken ct = default)
    {
        await _context.ProductVariantBarcodeHistories.AddAsync(entity, ct);
    }

    public async Task<PagedResult<BarcodeHistoryRowDto>> GetPagedAsync(
        int storeId,
        BarcodeHistoryQueryRequest request,
        CancellationToken ct = default)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        var query = _context.ProductVariantBarcodeHistories
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .Include(x => x.ProductVariant)
                .ThenInclude(v => v.Product)
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.Unit)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();

            query = query.Where(x =>
                (x.OldBarcode != null && x.OldBarcode.Contains(keyword)) ||
                (x.NewBarcode != null && x.NewBarcode.Contains(keyword)) ||
                (x.Reason != null && x.Reason.Contains(keyword)) ||
                (x.ChangedByUserName != null && x.ChangedByUserName.Contains(keyword)) ||
                (x.ProductVariant.Product.Name != null && x.ProductVariant.Product.Name.Contains(keyword)) ||
                (x.ProductVariant.Sku != null && x.ProductVariant.Sku.Contains(keyword)));
        }

        if (request.ProductVariantId.HasValue)
            query = query.Where(x => x.ProductVariantId == request.ProductVariantId.Value);

        if (request.ProductUnitConversionId.HasValue)
            query = query.Where(x => x.ProductUnitConversionId == request.ProductUnitConversionId.Value);

        if (!string.IsNullOrWhiteSpace(request.ActionType) &&
            Enum.TryParse<BarcodeHistoryActionType>(request.ActionType, true, out var actionType))
        {
            query = query.Where(x => x.ActionType == actionType);
        }

        if (request.FromUtc.HasValue)
            query = query.Where(x => x.ChangedAtUtc >= request.FromUtc.Value);

        if (request.ToUtc.HasValue)
            query = query.Where(x => x.ChangedAtUtc <= request.ToUtc.Value);

        var totalItems = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.ChangedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new BarcodeHistoryRowDto
            {
                Id = x.Id,
                ProductId = x.ProductVariant.ProductId,
                ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.ProductUnitConversionId,
                ProductName = x.ProductVariant.Product.Name,
                VariantSku = x.ProductVariant.Sku,
                VariantName = null,
                UnitId = x.ProductUnitConversion.UnitId,
                UnitName = x.ProductUnitConversion.Unit.Name,
                OldBarcode = x.OldBarcode,
                NewBarcode = x.NewBarcode,
                ActionType = x.ActionType.ToString(),
                ActionTypeText = ToActionTypeText(x.ActionType),
                Reason = x.Reason,
                ChangedByUserId = x.ChangedByUserId,
                ChangedByUserName = x.ChangedByUserName,
                ChangedAtUtc = x.ChangedAtUtc
            })
            .ToListAsync(ct);

        return new PagedResult<BarcodeHistoryRowDto>(page, pageSize, totalItems, items);
    }

    public async Task<List<ProductVariantBarcodeHistory>> GetRecentByConversionIdAsync(
        int storeId,
        int productUnitConversionId,
        int take = 20,
        CancellationToken ct = default)
    {
        if (take <= 0) take = 20;

        return await _context.ProductVariantBarcodeHistories
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.ProductUnitConversionId == productUnitConversionId &&
                !x.IsDeleted)
            .OrderByDescending(x => x.ChangedAtUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<ProductVariantBarcodeHistory?> FindByHistoricalBarcodeAsync(
        int storeId,
        string barcode,
        CancellationToken ct = default)
    {
        barcode = (barcode ?? string.Empty).Trim();

        return _context.ProductVariantBarcodeHistories
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                (x.OldBarcode == barcode || x.NewBarcode == barcode))
            .OrderByDescending(x => x.ChangedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }

    private static string ToActionTypeText(BarcodeHistoryActionType actionType)
    {
        return actionType switch
        {
            BarcodeHistoryActionType.Assigned => "Gán barcode",
            BarcodeHistoryActionType.Replaced => "Đổi barcode",
            BarcodeHistoryActionType.Deactivated => "Ngưng sử dụng",
            BarcodeHistoryActionType.Reactivated => "Kích hoạt lại",
            BarcodeHistoryActionType.Imported => "Import dữ liệu",
            _ => actionType.ToString()
        };
    }
}