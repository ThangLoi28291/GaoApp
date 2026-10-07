using GaoApp.Application.DTOs.Promotions;
using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Repositories.Promotions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Promotions;

public sealed class PromotionRepository : IPromotionRepository
{
    private readonly AppDbContext _db;

    public PromotionRepository(AppDbContext db)
    {
        _db = db;
    }

    // =========================================================
    // POS ENGINE: TYPE 1 + TYPE 3
    // ProductDiscount + BuyXGetY dùng PromotionItems
    // =========================================================
    public async Task<List<Promotion>> GetActiveProductDiscountPromotionsAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.Promotions
            .AsNoTracking()
            .Include(x => x.Items.Where(i => !i.IsDeleted))
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                (
                    x.Type == PromotionType.ProductDiscount ||
                    x.Type == PromotionType.BuyXGetY
                ) &&
                x.StartAtUtc <= now &&
                x.EndAtUtc >= now)
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    // =========================================================
    // POS ENGINE: TYPE 2
    // ComboFixedPrice dùng PromotionComboRule
    // =========================================================
    public async Task<List<Promotion>> GetActiveComboPromotionsAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.Promotions
            .AsNoTracking()
            .Include(x => x.ComboRules.Where(r => !r.IsDeleted))
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.Type == PromotionType.ComboFixedPrice &&
                x.StartAtUtc <= now &&
                x.EndAtUtc >= now)
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    // =========================================================
    // ADMIN: DANH SÁCH PHÂN TRANG
    // =========================================================
    public async Task<(IReadOnlyList<Promotion> Items, int TotalItems)> GetPagedAsync(
        int storeId,
        PromotionType? type,
        bool? isActive,
        string? customerPriceTier,
        string? keyword,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        keyword = (keyword ?? string.Empty).Trim();
        customerPriceTier = (customerPriceTier ?? string.Empty).Trim().ToUpperInvariant();

        var q = _db.Promotions
            .AsNoTracking()
            .Include(x => x.Items.Where(i => !i.IsDeleted))
            .Include(x => x.ComboRules.Where(r => !r.IsDeleted))
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (type.HasValue)
            q = q.Where(x => x.Type == type.Value);

        if (isActive.HasValue)
            q = q.Where(x => x.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(customerPriceTier))
            q = q.Where(x => (x.CustomerPriceTier ?? "") == customerPriceTier);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(x =>
                x.Name.Contains(keyword) ||
                (x.Description != null && x.Description.Contains(keyword)) ||
                (x.ComboNote != null && x.ComboNote.Contains(keyword)));
        }

        if (fromUtc.HasValue)
            q = q.Where(x => x.EndAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            q = q.Where(x => x.StartAtUtc <= toUtc.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Promotion?> GetByIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        return _db.Promotions
            .AsNoTracking()
            .Include(x => x.Items.Where(i => !i.IsDeleted))
            .Include(x => x.ComboRules.Where(r => !r.IsDeleted))
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == id &&
                !x.IsDeleted,
                ct);
    }

    public Task<Promotion?> GetByIdForUpdateAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        return _db.Promotions
            .Include(x => x.Items.Where(i => !i.IsDeleted))
            .Include(x => x.ComboRules.Where(r => !r.IsDeleted))
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == id &&
                !x.IsDeleted,
                ct);
    }

    public Task<bool> ExistsNameAsync(
        int storeId,
        string name,
        int? excludeId,
        CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();

        return _db.Promotions.AnyAsync(x =>
            x.StoreId == storeId &&
            !x.IsDeleted &&
            x.Name == name &&
            (!excludeId.HasValue || x.Id != excludeId.Value),
            ct);
    }

    public Task AddAsync(Promotion entity, CancellationToken ct = default)
        => _db.Promotions.AddAsync(entity, ct).AsTask();

    public void Update(Promotion entity)
        => _db.Promotions.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(
                "Dữ liệu đã được người khác thay đổi. Vui lòng tải lại và thử lại.");
        }
    }
    public async Task<List<PromotionProductLookupDto>> SearchProductsForPromotionAsync(
     int storeId,
     string keyword,
     int take = 20,
     CancellationToken ct = default)
    {
        keyword = (keyword ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return new List<PromotionProductLookupDto>();

        if (take <= 0) take = 20;
        if (take > 50) take = 50;

        var items = await _db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v =>
                v.StoreId == storeId &&
                !v.IsDeleted &&
                v.IsActive &&
                v.Product != null &&
                !v.Product.IsDeleted &&
                v.Product.IsActive &&
                (v.Product.Name.Contains(keyword) || v.Sku.Contains(keyword) ||
                    (v.ProductVariantName != null && v.ProductVariantName.Contains(keyword))))
            .OrderBy(v => v.Product.Name)
            .ThenBy(v => v.Id)
            .Take(take)
            .Select(v => new PromotionProductLookupDto
            {
                ProductId = v.ProductId,
                VariantId = v.Id,
                ProductName = v.Product.Name,

                VariantName = v.ProductVariantName,
                Sku = v.Sku,
                Barcode = null,
                Price = 0m,

                BaseUnitId = v.Product.BaseUnitId,
                BaseUnitName = v.Product.BaseUnit != null
                    ? v.Product.BaseUnit.Name
                    : null,

                Text = v.Product.Name + " · " + (v.ProductVariantName ?? v.Sku) + " | " + v.Sku + " | #" + v.Id
            })
            .ToListAsync(ct);

        return items;
    }
    public async Task<List<PromotionProductUnitLookupDto>> GetUnitsForPromotionAsync(
     int storeId,
     int variantId,
     CancellationToken ct = default)
    {
        if (variantId <= 0)
            return new List<PromotionProductUnitLookupDto>();

        var variant = await _db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
                .ThenInclude(p => p.BaseUnit)
            .FirstOrDefaultAsync(v =>
                v.StoreId == storeId &&
                v.Id == variantId &&
                !v.IsDeleted,
                ct);

        if (variant == null || variant.Product == null)
            return new List<PromotionProductUnitLookupDto>();

        var result = new List<PromotionProductUnitLookupDto>();

        if (variant.Product.BaseUnitId > 0)
        {
            var baseUnitName = variant.Product.BaseUnit?.Name ?? "Đơn vị gốc";

            result.Add(new PromotionProductUnitLookupDto
            {
                ProductUnitConversionId = null,
                UnitId = variant.Product.BaseUnitId,
                UnitName = baseUnitName,
                Factor = 1m,
                RetailPrice = null,
                WholesalePrice = null,
                IsBaseUnit = true,
                Text = baseUnitName + " - x1"
            });
        }

        var conversions = await _db.ProductUnitConversions
            .AsNoTracking()
            .Include(x => x.Unit)
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.ProductVariantId == variantId &&
                x.IsActive)
            .OrderBy(x => x.Factor)
            .ThenBy(x => x.Id)
            .Select(x => new PromotionProductUnitLookupDto
            {
                ProductUnitConversionId = x.Id,
                UnitId = x.UnitId,
                UnitName = x.Unit != null ? x.Unit.Name : "Đơn vị",
                Factor = x.Factor,
                RetailPrice = null,
                WholesalePrice = null,
                IsBaseUnit = x.IsBaseUnit,
                Text = (x.Unit != null ? x.Unit.Name : "Đơn vị") + " - x" + x.Factor
            })
            .ToListAsync(ct);

        result.AddRange(conversions);

        return result;
    }
    public async Task<PromotionProductLookupDto?> GetProductForPromotionAsync(
    int storeId,
    int variantId,
    CancellationToken ct = default)
    {
        if (variantId <= 0)
            return null;

        var item = await _db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
                .ThenInclude(p => p.BaseUnit)
            .Where(v =>
                v.StoreId == storeId &&
                !v.IsDeleted &&
                v.Id == variantId &&
                v.Product != null &&
                !v.Product.IsDeleted &&
                v.IsActive && v.Product.IsActive && v.Product.StoreId == storeId)
            .Select(v => new PromotionProductLookupDto
            {
                ProductId = v.ProductId,
                VariantId = v.Id,
                ProductName = v.Product.Name,
                VariantName = v.ProductVariantName,
                Sku = v.Sku,
                Barcode = null,
                Price = 0m,
                BaseUnitId = v.Product.BaseUnitId,
                BaseUnitName = v.Product.BaseUnit != null ? v.Product.BaseUnit.Name : null,
                Text = v.Product.Name + " · " + (v.ProductVariantName ?? v.Sku) + " | " + v.Sku + " | #" + v.Id
            })
            .FirstOrDefaultAsync(ct);

        return item;
    }
}
