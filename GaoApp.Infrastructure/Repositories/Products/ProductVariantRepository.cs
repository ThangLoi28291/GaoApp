using GaoApp.Application.Common.Helpers;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Infrastructure.Repositories.Products;

/// <summary>
/// Repository xử lý ProductVariant.
///
/// CHỐT KIẾN TRÚC:
/// - ProductVariant không còn dùng Barcode làm nguồn lookup chính
/// - Barcode được quản lý hoàn toàn ở ProductVariantUnitBarcode
/// - Variant được nhận diện nghiệp vụ theo combo AttributeValueIds
/// - Khi tạo variant:
///   + tự sinh SKU (nếu tầng trên đã làm thì repo chỉ nhận vào)
///   + tự sinh ProductVariantName theo cấu trúc:
///       {Tên product} size l vị cam hương bưởi
/// - Nếu UI gửi ProductVariantName thì ưu tiên tên do người dùng sửa
/// - Luôn lưu thêm ProductVariantNameNormalized để search nhanh
/// - Nếu variant đã phát sinh OrderLine thì không cho xóa, không cho đổi combo
/// - Nếu variant đã soft delete và tạo lại đúng combo thì restore lại variant cũ
/// </summary>
public sealed class ProductVariantRepository : IProductVariantRepository
{
    private readonly AppDbContext _db;

    public ProductVariantRepository(AppDbContext db)
    {
        _db = db;
    }
    /// <summary>
    /// Sinh barcode nội bộ khả dụng cho variant.
    /// 
    /// Rule:
    /// - ưu tiên prefix 20
    /// - nếu trùng thì thử 21, 22, 23... đến 29
    /// - check cả dữ liệu soft delete để tránh vỡ unique index DB
    /// </summary>
    private async Task<string> GenerateAvailableInternalBarcodeAsync(
        int storeId,
        int variantId,
        CancellationToken ct)
    {
        var prefixes = new[]
        {
        "20", "21", "22", "23", "24",
        "25", "26", "27", "28", "29"
    };

        foreach (var prefix in prefixes)
        {
            var candidate = Ean13Helper.GenerateInternal(storeId, variantId, prefix);

            var exists = await _db.ProductVariantUnitBarcodes
                .IgnoreQueryFilters()
                .AnyAsync(x =>
                    x.StoreId == storeId &&
                    x.Barcode == candidate,
                    ct);

            if (!exists)
                return candidate;
        }

        throw new BusinessRuleException(
            "Không thể cấp barcode nội bộ cho biến thể. " +
            "Vui lòng thử lại hoặc liên hệ quản lý.");
    }

    /// <summary>
    /// Đảm bảo variant luôn có:
    /// - 1 ProductUnitConversion đơn vị gốc
    /// - 1 ProductVariantUnitBarcode nội bộ EAN13 cho base conversion
    /// </summary>
    private async Task EnsureVariantInfrastructureAsync(
        int storeId,
        int baseUnitId,
        int variantId,
        CancellationToken ct)
    {
        // =====================================================
        // 1. Ensure base conversion
        // =====================================================
        var conversion = await _db.ProductUnitConversions
            .FirstOrDefaultAsync(x =>
                x.ProductVariantId == variantId &&
                x.IsBaseUnit &&
                !x.IsDeleted,
                ct);

        if (conversion == null)
        {
            conversion = new ProductUnitConversion
            {
                StoreId = storeId,
                ProductVariantId = variantId,
                UnitId = baseUnitId,
                Factor = 1m,
                IsBaseUnit = true,
                IsDefaultForSale = true,
                IsActive = true,
                SortOrder = 0
            };

            _db.ProductUnitConversions.Add(conversion);
            await _db.SaveChangesAsync(ct);
        }

        // =====================================================
        // 2. Ensure barcode cho base conversion
        // =====================================================
        var hasBarcode = await _db.ProductVariantUnitBarcodes
            .AnyAsync(x =>
                x.ProductUnitConversionId == conversion.Id &&
                !x.IsDeleted,
                ct);

        if (!hasBarcode)
        {
            // FIX:
            // - không generate cứng prefix 20 nữa
            // - nếu trùng thì tự fallback 21 -> 22 -> ... -> 29
            // - check cả soft delete để tránh vỡ unique index
            var barcode = await GenerateAvailableInternalBarcodeAsync(
                storeId,
                variantId,
                ct);

            var entity = new ProductVariantUnitBarcode
            {
                StoreId = storeId,
                ProductUnitConversionId = conversion.Id,
                Barcode = barcode,
                BarcodeType = BarcodeType.Internal,
                IsPrimary = true,
                IsActive = true,
                Note = "Tự sinh khi tạo biến thể"
            };

            _db.ProductVariantUnitBarcodes.Add(entity);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<AttributeWithValuesDto>> GetAttributesWithValuesAsync(int storeId, CancellationToken ct)
    {
        var attrs = await _db.Set<ProductAttribute>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.SortOrder)
            .Select(x => new AttributeWithValuesDto
            {
                AttributeId = x.Id,
                AttributeName = x.Name,
                Values = new List<AttributeValueMiniDto>()
            })
            .ToListAsync(ct);

        var values = await _db.Set<AttributeValue>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Status)
            .OrderBy(x => x.SortOrder)
            .Select(x => new AttributeValueMiniDto
            {
                Id = x.Id,
                AttributeId = x.AttributeId,
                Code = x.Code,
                Name = x.Name
            })
            .ToListAsync(ct);

        var map = values
            .GroupBy(v => v.AttributeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var a in attrs)
        {
            if (map.TryGetValue(a.AttributeId, out var list))
            {
                a.Values = list;
            }
        }

        return attrs;
    }

    public Task<List<ProductVariant>> GetByProductAsync(int storeId, int productId, CancellationToken ct)
    => _db.Set<ProductVariant>()
        .Where(x => x.StoreId == storeId && x.ProductId == productId && !x.IsDeleted)

        .Include(x => x.AttributeValues)

        // NEW:
        // Load đơn vị quy đổi để màn Variant lấy giá lẻ/giá sỉ của đơn vị gốc.
        .Include(x => x.UnitConversions.Where(c => !c.IsDeleted))
            .ThenInclude(c => c.Unit)

        .ToListAsync(ct);

    public Task<bool> ExistsSkuAsync(int storeId, string sku, int? excludeVariantId, CancellationToken ct)
        => _db.Set<ProductVariant>()
            .IgnoreQueryFilters()
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.Sku == sku &&
                (!excludeVariantId.HasValue || x.Id != excludeVariantId.Value) &&
                !x.IsDeleted, ct);

    public Task AddAsync(ProductVariant entity, CancellationToken ct)
    {
        _db.Add(entity);
        return Task.CompletedTask;
    }

    public Task RemoveRangeAsync(IEnumerable<ProductVariant> entities, CancellationToken ct)
    {
        _db.RemoveRange(entities);
        return Task.CompletedTask;
    }

    public async Task RemoveMappingsByVariantAsync(int storeId, int variantId, CancellationToken ct)
    {
        var maps = await _db.Set<ProductVariantAttributeValue>()
            .Where(m => m.StoreId == storeId && m.VariantId == variantId)
            .ToListAsync(ct);

        _db.RemoveRange(maps);
    }

    public Task AddMappingsAsync(IEnumerable<ProductVariantAttributeValue> mappings, CancellationToken ct)
    {
        _db.AddRange(mappings);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<Dictionary<int, int>> MapValueIdsToAttributeIdsAsync(
        int storeId,
        IEnumerable<int> valueIds,
        CancellationToken ct = default)
    {
        var ids = valueIds.Distinct().ToList();
        if (ids.Count == 0)
            return new();

        return await _db.AttributeValues
            .Where(x => x.StoreId == storeId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.AttributeId })
            .ToDictionaryAsync(x => x.Id, x => x.AttributeId, ct);
    }

    /// <summary>
    /// Chuẩn hóa danh sách AttributeValueIds:
    /// - bỏ số <= 0
    /// - distinct
    /// - sort tăng dần
    /// </summary>
    private static List<int> NormalizeAttributeValueIds(IEnumerable<int>? valueIds)
    {
        return (valueIds ?? Enumerable.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    /// <summary>
    /// Build tên variant theo đúng cấu trúc:
    /// {Tên product} size l vị cam hương bưởi
    ///
    /// Nếu không có attribute nào thì fallback về tên product.
    /// </summary>
    private async Task<string> BuildGeneratedVariantNameAsync(
        int storeId,
        string productName,
        List<int> normalizedValueIds,
        CancellationToken ct)
    {
        productName = (productName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(productName))
            return string.Empty;

        if (normalizedValueIds.Count == 0)
            return productName;

        var valueEntities = await _db.AttributeValues
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && normalizedValueIds.Contains(x.Id))
            .Include(x => x.Attribute)
            .ToListAsync(ct);

        var parts = valueEntities
            .Select(x => new VariantNamePart
            {
                AttributeId = x.AttributeId,
                AttributeName = x.Attribute?.Name ?? string.Empty,
                ValueName = x.Name ?? string.Empty,
                SortOrder = x.Attribute?.SortOrder ?? 0
            })
            .ToList();

        return ProductVariantNameHelper.Build(productName, parts);
    }

    /// <summary>
    /// Chốt tên variant sẽ lưu:
    /// - nếu UI nhập ProductVariantName thì ưu tiên dùng
    /// - nếu UI bỏ trống thì tự sinh theo product + attribute
    /// </summary>
    private async Task<(string ProductVariantName, string ProductVariantNameNormalized)> ResolveVariantNameAsync(
        int storeId,
        string productName,
        List<int> normalizedValueIds,
        string? inputVariantName,
        CancellationToken ct)
    {
        string finalName;

        if (!string.IsNullOrWhiteSpace(inputVariantName))
        {
            finalName = inputVariantName.Trim();
        }
        else
        {
            finalName = await BuildGeneratedVariantNameAsync(
                storeId,
                productName,
                normalizedValueIds,
                ct);
        }

        var normalized = ProductVariantNameHelper.NormalizeForSearch(finalName);
        return (finalName, normalized);
    }

    /// <summary>
    /// Tìm variant cùng combo chính xác trong cùng product, kể cả soft delete.
    /// </summary>
    private async Task<ProductVariant?> FindExistingVariantByExactComboAsync(
        int storeId,
        int productId,
        List<int> normalizedValueIds,
        int? excludeVariantId,
        CancellationToken ct)
    {
        var candidates = await _db.ProductVariants
            .IgnoreQueryFilters()
            .Where(x =>
                x.StoreId == storeId &&
                x.ProductId == productId &&
                (!excludeVariantId.HasValue || x.Id != excludeVariantId.Value))
            .Include(x => x.AttributeValues)
            .ToListAsync(ct);

        foreach (var variant in candidates)
        {
            var existingIds = variant.AttributeValues
                .Select(x => x.AttributeValueId)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (existingIds.SequenceEqual(normalizedValueIds))
                return variant;
        }

        return null;
    }

    /// <summary>
    /// Đồng bộ mapping ProductVariantAttributeValue cho 1 variant.
    /// </summary>
    private async Task SyncVariantAttributeMappingsAsync(
        int storeId,
        int variantId,
        List<int> normalizedValueIds,
        CancellationToken ct)
    {
        await RemoveMappingsByVariantAsync(storeId, variantId, ct);

        if (normalizedValueIds.Count == 0)
            return;

        var valueAttributeMap = await MapValueIdsToAttributeIdsAsync(
            storeId,
            normalizedValueIds,
            ct);

        if (valueAttributeMap.Count != normalizedValueIds.Count)
        {
            throw new BusinessRuleException(
                "Có AttributeValueId không hợp lệ hoặc không thuộc store hiện tại.");
        }

        var duplicatedAttribute = valueAttributeMap.Values
            .GroupBy(x => x)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicatedAttribute != null)
        {
            throw new BusinessRuleException(
                "Một biến thể không được chứa nhiều giá trị của cùng một thuộc tính.");
        }

        var mappings = normalizedValueIds.Select(valueId => new ProductVariantAttributeValue
        {
            StoreId = storeId,
            VariantId = variantId,
            AttributeId = valueAttributeMap[valueId],
            AttributeValueId = valueId
        }).ToList();

        await AddMappingsAsync(mappings, ct);
    }

    /// <summary>
    /// Restore variant đã soft delete thay vì tạo variant mới.
    /// </summary>
    private void RestoreSoftDeletedVariant(
        ProductVariant variant,
        ProductVariantRowDto row,
        string resolvedVariantName,
        string resolvedVariantNameNormalized,
        int? userId)
    {
        variant.Sku = row.Sku?.Trim() ?? string.Empty;
        variant.ProductVariantName = resolvedVariantName;
        variant.ProductVariantNameNormalized = resolvedVariantNameNormalized;
        variant.Price = row.Price;
        // NEW: Lưu giá sỉ khi khôi phục variant đã xóa mềm
        variant.WholesalePrice = NormalizeNullableMoney(row.WholesalePrice);
        variant.CostPrice = row.CostPrice;

        variant.IsActive = row.IsActive;
        variant.HasInputInvoice = row.HasInputInvoice;
        variant.PrimaryProductImageId = row.PrimaryProductImageId;

        variant.IsDeleted = false;
        variant.DeletedAtUtc = null;
        variant.DeletedBy = null;

        variant.UpdatedAtUtc = DateTime.UtcNow;
        variant.UpdatedBy = userId;
    }
    /// <summary>
    /// Chuẩn hóa tiền nullable.
    /// null hoặc <= 0 thì lưu null để hiểu là chưa cấu hình.
    /// </summary>
    private static decimal? NormalizeNullableMoney(decimal? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value <= 0 ? null : value.Value;
    }
    /// <summary>
    /// Kiểm tra variant đã từng phát sinh OrderLine chưa.
    /// Nếu đã có OrderLine thì không cho xóa và không cho đổi combo.
    /// </summary>
    private Task<bool> HasOrderLineUsageAsync(int storeId, int variantId, CancellationToken ct)
    {
        return _db.Set<OrderLine>()
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.VariantId == variantId,
                ct);
    }

    public async Task SaveVariantsAsync(
        int storeId,
        int productId,
        List<ProductVariantRowDto> variants,
        int? userId,
        CancellationToken ct)
    {
        var product = await _db.Products
            .AsNoTracking()
            .FirstAsync(x => x.Id == productId && x.StoreId == storeId, ct);

        foreach (var row in variants)
        {
            var normalizedSku = (row.Sku ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedSku))
                throw new BusinessRuleException("SKU không được rỗng.");

            var normalizedValueIds = NormalizeAttributeValueIds(row.AttributeValueIds);

            // Resolve tên variant trước cho cả create / update / restore
            var resolvedName = await ResolveVariantNameAsync(
                storeId,
                product.Name,
                normalizedValueIds,
                row.ProductVariantName,
                ct);

            ProductVariant variant;

            // =====================================================
            // UPDATE
            // =====================================================
            if (row.Id.HasValue && row.Id.Value > 0)
            {
                variant = await _db.ProductVariants
                    .Include(x => x.AttributeValues)
                    .FirstAsync(x => x.Id == row.Id.Value && x.StoreId == storeId, ct);

                var hasUsage = await HasOrderLineUsageAsync(storeId, variant.Id, ct);
                if (hasUsage)
                {
                    var oldCombo = variant.AttributeValues
                        .Select(x => x.AttributeValueId)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    if (!oldCombo.SequenceEqual(normalizedValueIds))
                    {
                        throw new BusinessRuleException(
                            "Biến thể đã phát sinh bán hàng, không được thay đổi tổ hợp thuộc tính.");
                    }
                }

                var comboConflict = await FindExistingVariantByExactComboAsync(
                    storeId,
                    productId,
                    normalizedValueIds,
                    excludeVariantId: variant.Id,
                    ct);

                if (comboConflict != null)
                {
                    if (!comboConflict.IsDeleted)
                    {
                        throw new BusinessRuleException(
                            "Biến thể với tổ hợp thuộc tính này đã tồn tại.");
                    }

                    throw new BusinessRuleException(
                        "Tổ hợp thuộc tính này đã tồn tại ở biến thể đã xóa. Hãy khôi phục biến thể cũ thay vì đổi chồng.");
                }

                var skuExists = await _db.ProductVariants
                    .IgnoreQueryFilters()
                    .AnyAsync(x =>
                        x.StoreId == storeId &&
                        x.Id != variant.Id &&
                        x.Sku == normalizedSku &&
                        !x.IsDeleted,
                        ct);

                if (skuExists)
                {
                    throw new BusinessRuleException($"SKU đã tồn tại: {normalizedSku}");
                }

                variant.Sku = normalizedSku;
                variant.ProductVariantName = resolvedName.ProductVariantName;
                variant.ProductVariantNameNormalized = resolvedName.ProductVariantNameNormalized;
                variant.Price = row.Price;
                // NEW: Giá sỉ theo đơn vị gốc
                variant.WholesalePrice = NormalizeNullableMoney(row.WholesalePrice);
                variant.CostPrice = row.CostPrice;
                variant.IsActive = row.IsActive;
                variant.HasInputInvoice = row.HasInputInvoice;
                variant.PrimaryProductImageId = row.PrimaryProductImageId;

                variant.UpdatedAtUtc = DateTime.UtcNow;
                variant.UpdatedBy = userId;

                await _db.SaveChangesAsync(ct);

                await SyncVariantAttributeMappingsAsync(
                    storeId,
                    variant.Id,
                    normalizedValueIds,
                    ct);

                await EnsureVariantInfrastructureAsync(
                    storeId,
                    product.BaseUnitId,
                    variant.Id,
                    ct);

                continue;
            }

            // =====================================================
            // CREATE hoặc RESTORE
            // =====================================================
            var existingByCombo = await FindExistingVariantByExactComboAsync(
                storeId,
                productId,
                normalizedValueIds,
                excludeVariantId: null,
                ct);

            if (existingByCombo != null)
            {
                // Nếu combo đang active thì không tạo trùng
                if (!existingByCombo.IsDeleted)
                {
                    throw new BusinessRuleException(
                        "Biến thể với tổ hợp thuộc tính này đã tồn tại. Không thể tạo trùng.");
                }

                // Nếu combo đã soft delete thì restore lại
                RestoreSoftDeletedVariant(
                    existingByCombo,
                    row,
                    resolvedName.ProductVariantName,
                    resolvedName.ProductVariantNameNormalized,
                    userId);

                await _db.SaveChangesAsync(ct);

                await SyncVariantAttributeMappingsAsync(
                    storeId,
                    existingByCombo.Id,
                    normalizedValueIds,
                    ct);

                await EnsureVariantInfrastructureAsync(
                    storeId,
                    product.BaseUnitId,
                    existingByCombo.Id,
                    ct);

                continue;
            }

            // Không có combo cũ => create mới
            var activeSkuExists = await _db.ProductVariants
                .IgnoreQueryFilters()
                .AnyAsync(x =>
                    x.StoreId == storeId &&
                    x.Sku == normalizedSku &&
                    !x.IsDeleted,
                    ct);

            if (activeSkuExists)
            {
                throw new BusinessRuleException($"SKU đã tồn tại: {normalizedSku}");
            }

            variant = new ProductVariant
            {
                StoreId = storeId,
                ProductId = productId,
                Sku = normalizedSku,
                ProductVariantName = resolvedName.ProductVariantName,
                ProductVariantNameNormalized = resolvedName.ProductVariantNameNormalized,
                Price = row.Price,
                // NEW: Giá sỉ theo đơn vị gốc
                WholesalePrice = NormalizeNullableMoney(row.WholesalePrice),
                CostPrice = row.CostPrice,
                IsActive = row.IsActive,
                HasInputInvoice = row.HasInputInvoice,
                PrimaryProductImageId = row.PrimaryProductImageId,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = userId,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = userId
            };

            await _db.ProductVariants.AddAsync(variant, ct);
            await _db.SaveChangesAsync(ct);

            await SyncVariantAttributeMappingsAsync(
                storeId,
                variant.Id,
                normalizedValueIds,
                ct);

            await EnsureVariantInfrastructureAsync(
                storeId,
                product.BaseUnitId,
                variant.Id,
                ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> ToggleStatusAsync(int storeId, int variantId, int? userId, CancellationToken ct)
    {
        var v = await _db.ProductVariants
            .Where(x => x.StoreId == storeId && x.Id == variantId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (v == null)
            return false;

        v.IsActive = !v.IsActive;
        v.UpdatedAtUtc = DateTime.UtcNow;
        v.UpdatedBy = userId;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SoftDeleteVariantAsync(int storeId, int variantId, int? userId, CancellationToken ct)
    {
        var v = await _db.ProductVariants
            .Where(x => x.StoreId == storeId && x.Id == variantId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (v == null)
            return false;

        var hasUsage = await HasOrderLineUsageAsync(storeId, variantId, ct);
        if (hasUsage)
        {
            throw new BusinessRuleException(
                "Biến thể đã phát sinh bán hàng, không thể xóa. Hãy chuyển sang ngưng bán.");
        }

        v.IsActive = false;
        v.IsDeleted = true;
        v.DeletedAtUtc = DateTime.UtcNow;
        v.DeletedBy = userId;
        v.UpdatedAtUtc = DateTime.UtcNow;
        v.UpdatedBy = userId;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetVariantImageAsync(
        int storeId,
        int variantId,
        int? primaryProductImageId,
        int? userId,
        CancellationToken ct)
    {
        var v = await _db.ProductVariants
            .Where(x => x.StoreId == storeId && x.Id == variantId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (v == null)
            return false;

        if (primaryProductImageId.HasValue)
        {
            var okImg = await _db.ProductImages
                .AnyAsync(x =>
                    x.StoreId == storeId &&
                    x.Id == primaryProductImageId.Value &&
                    x.ProductId == v.ProductId &&
                    !x.IsDeleted,
                    ct);

            if (!okImg)
                primaryProductImageId = null;
        }

        v.PrimaryProductImageId = primaryProductImageId;
        v.UpdatedAtUtc = DateTime.UtcNow;
        v.UpdatedBy = userId;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Lấy variant active kèm product + base unit + unit conversions + barcodes.
    /// Dùng cho lookup / POS / stock.
    /// </summary>
    public Task<ProductVariant?> GetActiveWithProductAsync(int variantId, CancellationToken ct = default)
      => _db.ProductVariants
          .AsNoTracking()
          .Include(v => v.Product)
              .ThenInclude(p => p.BaseUnit)
          .Include(v => v.Product)
              .ThenInclude(p => p.ProductImages.Where(pi => !pi.IsDeleted))
                  .ThenInclude(pi => pi.MediaAsset)
          .Include(v => v.PrimaryProductImage)
              .ThenInclude(pi => pi!.MediaAsset)
          .Include(v => v.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
              .ThenInclude(c => c.Unit)
          .Include(v => v.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
              .ThenInclude(c => c.Barcodes.Where(b => !b.IsDeleted && b.IsActive))
          .FirstOrDefaultAsync(v => v.Id == variantId && !v.IsDeleted && v.IsActive, ct);

    public async Task<int?> ResolveVariantIdByBarcodeHistoryAsync(string barcode, CancellationToken ct = default)
    {
        var h = await _db.ProductVariantBarcodeHistories
            .OrderByDescending(x => x.ChangedAtUtc)
            .FirstOrDefaultAsync(x => x.OldBarcode == barcode || x.NewBarcode == barcode, ct);

        return h?.ProductVariantId;
    }

    /// <summary>
    /// Search variant theo từ khóa để phục vụ POS / stock lookup.
    ///
    /// CHỐT:
    /// - Không search theo ProductVariant.Barcode
    /// - Ưu tiên search theo ProductVariantNameNormalized
    /// - Fallback theo ProductVariantName / SKU / ProductName
    /// </summary>
    public Task<List<ProductVariant>> SearchForPOSAsync(
        string keyword,
        int take = 20,
        CancellationToken ct = default)
        => SearchActiveAsync(keyword, take, requireSellable: true, ct);

    public Task<List<ProductVariant>> SearchForStockDocumentAsync(
        string keyword,
        int take = 20,
        CancellationToken ct = default)
        => SearchActiveAsync(keyword, take, requireSellable: false, ct);

    private async Task<List<ProductVariant>> SearchActiveAsync(
        string keyword,
        int take,
        bool requireSellable,
        CancellationToken ct)
    {
        keyword = (keyword ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return new List<ProductVariant>();

        take = Math.Clamp(take <= 0 ? 20 : take, 1, 50);

        var normalizedKeyword = ProductVariantNameHelper.NormalizeForSearch(keyword);

        // Bước 1 chỉ tìm ID. Không Include collection tại đây vì ProductImages x
        // UnitConversions x Barcodes có thể tạo một result set rất lớn trước khi
        // EF dựng lại entity, làm autocomplete POS chậm và dễ bị browser hủy.
        var matchedIds = await _db.ProductVariants
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                x.Product != null &&
                !x.Product.IsDeleted &&
                x.Product.IsActive &&
                (!requireSellable || x.Product.IsSellable) &&
                (
                    (!string.IsNullOrEmpty(x.ProductVariantNameNormalized) && x.ProductVariantNameNormalized.Contains(normalizedKeyword)) ||
                    (!string.IsNullOrEmpty(x.ProductVariantName) && x.ProductVariantName.Contains(keyword)) ||
                    (!string.IsNullOrEmpty(x.Sku) && x.Sku.Contains(keyword)) ||
                    x.Product.Name.Contains(keyword) ||
                    x.UnitConversions.Any(c =>
                        !c.IsDeleted &&
                        c.IsActive &&
                        c.Unit != null &&
                        !c.Unit.IsDeleted &&
                        c.Unit.Name.Contains(keyword))
                ))
            .OrderBy(x => x.ProductVariantName)
            .ThenBy(x => x.Sku)
            .Select(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

        if (matchedIds.Count == 0)
            return new List<ProductVariant>();

        // Bước 2 chỉ nạp graph cho số ID đã giới hạn. AsSplitQuery tránh phép
        // nhân dòng giữa các collection Include độc lập.
        var variants = await _db.ProductVariants
            .AsNoTracking()
            .Where(x => matchedIds.Contains(x.Id))

            // Product + BaseUnit
            .Include(x => x.Product)
                .ThenInclude(p => p.BaseUnit)

            // Ảnh fallback của Product
            .Include(x => x.Product)
                .ThenInclude(p => p.ProductImages.Where(pi => !pi.IsDeleted))
                    .ThenInclude(pi => pi.MediaAsset)

            // Ảnh riêng của Variant
            .Include(x => x.PrimaryProductImage)
                .ThenInclude(pi => pi!.MediaAsset)

            // Unit conversion
            .Include(x => x.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.Unit)

            .Include(x => x.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.Barcodes.Where(b => !b.IsDeleted && b.IsActive))

            .AsSplitQuery()
            .ToListAsync(ct);

        // SQL IN không giữ thứ tự của bước tìm ID, nên khôi phục đúng thứ tự
        // autocomplete đã chốt ở bước 1.
        var positions = matchedIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index);

        return variants
            .OrderBy(x => positions[x.Id])
            .ToList();
    }
}
