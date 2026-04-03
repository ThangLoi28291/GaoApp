using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Products;

/// <summary>
/// Service quản lý ProductUnitConversion và ProductVariantUnitBarcode.
///
/// CHỐT NGHIỆP VỤ PHASE 5.12:
/// - 1 ProductUnitConversion có nhiều ProductVariantUnitBarcode
/// - Barcode chính thức nằm ở ProductVariantUnitBarcode
/// - ProductVariantBarcodeHistory là bảng append-only để truy vết thay đổi
/// - Lịch sử chỉ ghi các action enum hợp lệ:
///   Assigned / Replaced / Deactivated / Reactivated / Imported
/// </summary>
public class ProductUnitConversionService : IProductUnitConversionService
{
    private readonly IProductUnitConversionRepository _conversionRepo;
    private readonly IProductVariantUnitBarcodeRepository _barcodeRepo;
    private readonly IProductVariantBarcodeHistoryRepository _historyRepo;
    private readonly ICurrentStore _currentStore;
    private readonly ICurrentUser _currentUser;

    public ProductUnitConversionService(
        IProductUnitConversionRepository conversionRepo,
        IProductVariantUnitBarcodeRepository barcodeRepo,
        IProductVariantBarcodeHistoryRepository historyRepo,
        ICurrentStore currentStore,
        ICurrentUser currentUser)
    {
        _conversionRepo = conversionRepo;
        _barcodeRepo = barcodeRepo;
        _historyRepo = historyRepo;
        _currentStore = currentStore;
        _currentUser = currentUser;
    }

    public async Task<List<UpsertProductUnitConversionRequest>> GetByVariantIdAsync(
        int productVariantId,
        CancellationToken ct = default)
    {
        var items = await _conversionRepo.GetByVariantIdAsync(productVariantId, ct);

        return items.Select(x => new UpsertProductUnitConversionRequest
        {
            Id = x.Id,
            ProductVariantId = x.ProductVariantId,
            UnitId = x.UnitId,
            UnitName = x.Unit?.Name ?? string.Empty,
            Factor = x.Factor,
            IsBaseUnit = x.IsBaseUnit,
            IsDefaultForSale = x.IsDefaultForSale,
            Price = x.Price,
            IsActive = x.IsActive,
            SortOrder = x.SortOrder,
            Barcodes = x.Barcodes?
                .Where(b => !b.IsDeleted)
                .OrderByDescending(b => b.IsPrimary)
                .ThenByDescending(b => b.IsActive)
                .ThenBy(b => b.Id)
                .Select(b => new ProductVariantUnitBarcodeDto
                {
                    Id = b.Id,
                    ProductUnitConversionId = b.ProductUnitConversionId,
                    Barcode = b.Barcode,
                    BarcodeType = b.BarcodeType,
                    IsPrimary = b.IsPrimary,
                    IsActive = b.IsActive,
                    Note = b.Note
                })
                .ToList() ?? new List<ProductVariantUnitBarcodeDto>()
        }).ToList();
    }

    public async Task<int> SaveConversionAsync(
        ProductUnitConversionUpsertDto dto,
        CancellationToken ct = default)
    {
        if (dto == null)
            throw new InvalidOperationException("Dữ liệu conversion không được null.");

        if (dto.ProductVariantId <= 0)
            throw new InvalidOperationException("ProductVariantId không hợp lệ.");

        if (dto.UnitId <= 0)
            throw new InvalidOperationException("UnitId không hợp lệ.");

        var factor = dto.Factor <= 0 ? 1m : dto.Factor;

        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            var entity = await _conversionRepo.GetByIdAsync(dto.Id.Value, ct);
            if (entity == null)
                throw new InvalidOperationException("Không tìm thấy ProductUnitConversion.");

            var exists = await _conversionRepo.ExistsByVariantAndUnitAsync(
                dto.ProductVariantId,
                dto.UnitId,
                dto.Id.Value,
                ct);

            if (exists)
                throw new InvalidOperationException("Biến thể đã có đơn vị này.");

            if (dto.IsBaseUnit)
                factor = 1m;

            entity.UnitId = dto.UnitId;
            entity.Factor = factor;
            entity.IsBaseUnit = dto.IsBaseUnit;
            entity.IsDefaultForSale = dto.IsDefaultForSale;
            entity.Price = dto.Price;
            entity.IsActive = dto.IsActive;
            entity.SortOrder = dto.SortOrder;

            await _conversionRepo.SaveChangesAsync(ct);
            return entity.Id;
        }

        var created = await CreateAsync(
            new CreateProductUnitConversionRequest
            {
                StoreId = _currentStore.StoreId,
                ProductVariantId = dto.ProductVariantId,
                UnitId = dto.UnitId,
                Factor = factor,
                IsBaseUnit = dto.IsBaseUnit,
                IsDefaultForSale = dto.IsDefaultForSale,
                Price = dto.Price,
                IsActive = dto.IsActive,
                SortOrder = dto.SortOrder,
                AutoGeneratePrimaryBarcode = true,
                BarcodeNote = "Tự sinh khi tạo đơn vị quy đổi"
            },
            ct);

        return created.Id;
    }

    public async Task<int> SaveBarcodeAsync(
      UpsertProductVariantUnitBarcodeRequest dto,
      CancellationToken ct = default)
    {
        if (dto == null)
            throw new InvalidOperationException("Dữ liệu barcode không được null.");

        if (dto.ProductUnitConversionId <= 0)
            throw new InvalidOperationException("ProductUnitConversionId không hợp lệ.");

        var storeId = _currentStore.StoreId;
        var inputBarcode = NormalizeBarcode(dto.Barcode);

        // =========================================================
        // NHÁNH 1: UPDATE barcode đã có
        // =========================================================
        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            var entity = await _barcodeRepo.GetByIdAsync(dto.Id.Value, ct);
            if (entity == null)
                throw new InvalidOperationException("Không tìm thấy barcode.");

            var conversion = await _conversionRepo.GetByIdAsync(entity.ProductUnitConversionId, ct);
            if (conversion == null)
                throw new InvalidOperationException("Không tìm thấy đơn vị quy đổi của barcode.");

            if (conversion.ProductVariantId <= 0)
                throw new InvalidOperationException("ProductVariantId của đơn vị quy đổi không hợp lệ.");

            var oldBarcodeValue = entity.Barcode;
            var oldIsPrimary = entity.IsPrimary;
            var oldIsActive = entity.IsActive;

            BarcodeHistoryActionType? historyActionType = null;
            string? historyOldBarcode = null;
            string? historyNewBarcode = null;

            // ---------------------------------------------------------
            // 1. Nếu người dùng nhập barcode mới thì check trùng
            // ---------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(inputBarcode) &&
                !string.Equals(oldBarcodeValue, inputBarcode, StringComparison.OrdinalIgnoreCase))
            {
                var duplicate = await _barcodeRepo.FindDuplicateWithDetailsAsync(
                    storeId,
                    inputBarcode,
                    dto.Id.Value,
                    ct);

                if (duplicate != null)
                {
                    var productName = duplicate.ProductUnitConversion?.ProductVariant?.Product?.Name ?? "(không rõ sản phẩm)";
                    var sku = duplicate.ProductUnitConversion?.ProductVariant?.Sku ?? "(không rõ SKU)";
                    var unitName = duplicate.ProductUnitConversion?.Unit?.Name ?? "(không rõ đơn vị)";

                    throw new InvalidOperationException(
                        $"Barcode '{inputBarcode}' đã thuộc sản phẩm '{productName}' / SKU '{sku}' / đơn vị '{unitName}'.");
                }

                entity.Barcode = inputBarcode;
                historyActionType = BarcodeHistoryActionType.Replaced;
                historyOldBarcode = oldBarcodeValue;
                historyNewBarcode = inputBarcode;
            }

            // ---------------------------------------------------------
            // 2. Cập nhật thông tin cơ bản trước
            // ---------------------------------------------------------
            entity.BarcodeType = dto.BarcodeType;
            entity.Note = dto.Note?.Trim();

            // ---------------------------------------------------------
            // 3. Xử lý rule IsPrimary / IsActive mới
            // ---------------------------------------------------------
            // CASE A:
            // Barcode hiện tại đang là chính, và user bấm ngưng
            // => tự tìm barcode active gần nhất để đôn lên làm chính
            if (oldIsPrimary && !dto.IsActive)
            {
                var replacement = await _barcodeRepo.GetNearestActiveCandidateAsync(
                    entity.ProductUnitConversionId,
                    entity.Id,
                    ct);

                if (replacement == null)
                {
                    throw new InvalidOperationException(
                        "Không thể ngưng barcode chính vì không còn barcode active nào khác để thay thế.");
                }

                // Hạ mã hiện tại xuống
                entity.IsPrimary = false;
                entity.IsActive = false;

                // Đôn mã gần nhất lên làm chính
                replacement.IsPrimary = true;
                replacement.IsActive = true;

                if (!historyActionType.HasValue)
                {
                    historyActionType = BarcodeHistoryActionType.Deactivated;
                    historyOldBarcode = entity.Barcode;
                    historyNewBarcode = replacement.Barcode;
                }

                await _barcodeRepo.SaveChangesAsync(ct);

                await _historyRepo.AddAsync(new ProductVariantBarcodeHistory
                {
                    StoreId = storeId,
                    ProductVariantId = conversion.ProductVariantId,
                    ProductUnitConversionId = entity.ProductUnitConversionId,
                    OldBarcodeId = entity.Id,
                    NewBarcodeId = replacement.Id,
                    OldBarcode = entity.Barcode,
                    NewBarcode = replacement.Barcode,
                    ActionType = historyActionType.Value,
                    Reason = string.IsNullOrWhiteSpace(dto.Note)
                        ? "Ngưng barcode chính, tự chuyển mã chính sang barcode active gần nhất."
                        : dto.Note.Trim(),
                    ChangedByUserId = _currentUser.UserId,
                    ChangedByUserName = _currentUser.UserName,
                    ChangedAtUtc = DateTime.UtcNow
                }, ct);

                await _historyRepo.SaveChangesAsync(ct);

                return entity.Id;
            }

            // CASE B:
            // Barcode này chưa phải chính, nhưng user tick thành chính
            // => hạ mã chính cũ xuống false, barcode hiện tại lên true
            if (dto.IsPrimary)
            {
                // BƯỚC 1:
                // hạ mọi barcode chính active khác xuống trước
                await ClearOtherPrimaryActiveBarcodesAsync(
                    entity.ProductUnitConversionId,
                    entity.Id,
                    ct);

                // BƯỚC 2:
                // mới nâng barcode hiện tại lên làm chính
                entity.IsPrimary = true;
                entity.IsActive = true;
            }
            else
            {
                if (oldIsPrimary && dto.IsActive)
                {
                    entity.IsPrimary = true;
                    entity.IsActive = true;
                }
                else
                {
                    entity.IsPrimary = false;
                    entity.IsActive = dto.IsActive;
                }
            }
      

            // ---------------------------------------------------------
            // 4. Ghi lịch sử khi chỉ đổi active (không phải case A)
            // ---------------------------------------------------------
            if (!historyActionType.HasValue && oldIsActive != entity.IsActive)
            {
                historyActionType = entity.IsActive
                    ? BarcodeHistoryActionType.Reactivated
                    : BarcodeHistoryActionType.Deactivated;

                historyOldBarcode = entity.Barcode;
                historyNewBarcode = entity.Barcode;
            }

            await _barcodeRepo.SaveChangesAsync(ct);

            if (historyActionType.HasValue)
            {
                await _historyRepo.AddAsync(new ProductVariantBarcodeHistory
                {
                    StoreId = storeId,
                    ProductVariantId = conversion.ProductVariantId,
                    ProductUnitConversionId = entity.ProductUnitConversionId,
                    OldBarcodeId = entity.Id,
                    NewBarcodeId = entity.Id,
                    OldBarcode = historyOldBarcode,
                    NewBarcode = historyNewBarcode,
                    ActionType = historyActionType.Value,
                    Reason = dto.Note?.Trim(),
                    ChangedByUserId = _currentUser.UserId,
                    ChangedByUserName = _currentUser.UserName,
                    ChangedAtUtc = DateTime.UtcNow
                }, ct);

                await _historyRepo.SaveChangesAsync(ct);
            }

            return entity.Id;
        }

        // =========================================================
        // NHÁNH 2: CREATE barcode mới
        // =========================================================
        string finalBarcode;
        var finalBarcodeType = dto.BarcodeType;

        if (!string.IsNullOrWhiteSpace(inputBarcode))
        {
            var duplicate = await _barcodeRepo.FindDuplicateWithDetailsAsync(
                storeId,
                inputBarcode,
                excludeId: null,
                ct);

            if (duplicate != null)
            {
                var productName = duplicate.ProductUnitConversion?.ProductVariant?.Product?.Name ?? "(không rõ sản phẩm)";
                var sku = duplicate.ProductUnitConversion?.ProductVariant?.Sku ?? "(không rõ SKU)";
                var unitName = duplicate.ProductUnitConversion?.Unit?.Name ?? "(không rõ đơn vị)";

                throw new InvalidOperationException(
                    $"Barcode '{inputBarcode}' đã thuộc sản phẩm '{productName}' / SKU '{sku}' / đơn vị '{unitName}'.");
            }

            finalBarcode = inputBarcode;
        }
        else
        {
            finalBarcode = await GenerateUniqueInternalBarcodeAsync(
                storeId,
                dto.ProductUnitConversionId,
                ct);

            finalBarcodeType = BarcodeType.Internal;
        }

        var createConversion = await _conversionRepo.GetByIdAsync(dto.ProductUnitConversionId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy ProductUnitConversion.");

        if (createConversion.ProductVariantId <= 0)
            throw new InvalidOperationException("ProductVariantId của đơn vị quy đổi không hợp lệ.");

        var existingBarcodes = await _barcodeRepo.GetByConversionIdAsync(dto.ProductUnitConversionId, ct);
        var hasAnyBarcode = existingBarcodes.Any(x => !x.IsDeleted);
        var hasPrimaryActive = existingBarcodes.Any(x => !x.IsDeleted && x.IsActive && x.IsPrimary);

        // Rule tạo mới:
        // - nếu chưa có barcode nào => barcode mới là chính
        // - nếu user tick là chính => hạ mã chính cũ xuống false trước
        var shouldBePrimary = !hasAnyBarcode || dto.IsPrimary;

        if (shouldBePrimary && hasPrimaryActive)
        {
            var existingItems = await _barcodeRepo.GetByConversionIdAsync(dto.ProductUnitConversionId, ct);

            foreach (var item in existingItems.Where(x => !x.IsDeleted && x.IsActive && x.IsPrimary))
            {
                item.IsPrimary = false;
            }

            await _barcodeRepo.SaveChangesAsync(ct);
        }

        var newEntity = new ProductVariantUnitBarcode
        {
            StoreId = storeId,
            ProductUnitConversionId = dto.ProductUnitConversionId,
            Barcode = finalBarcode,
            BarcodeType = finalBarcodeType,
            IsPrimary = shouldBePrimary,
            IsActive = dto.IsActive,
            Note = string.IsNullOrWhiteSpace(dto.Note)
                ? (string.IsNullOrWhiteSpace(inputBarcode)
                    ? "Tự sinh khi thêm barcode theo đơn vị"
                    : "Nhập tay khi thêm barcode theo đơn vị")
                : dto.Note.Trim()
        };

        // Nếu user tick "chính" mà lại bỏ active, ta ép active=true
        if (newEntity.IsPrimary && !newEntity.IsActive)
        {
            newEntity.IsActive = true;
        }

        await _barcodeRepo.AddAsync(newEntity, ct);
        await _barcodeRepo.SaveChangesAsync(ct);

        await _historyRepo.AddAsync(new ProductVariantBarcodeHistory
        {
            StoreId = storeId,
            ProductVariantId = createConversion.ProductVariantId,
            ProductUnitConversionId = dto.ProductUnitConversionId,
            OldBarcodeId = null,
            NewBarcodeId = newEntity.Id,
            OldBarcode = null,
            NewBarcode = newEntity.Barcode,
            ActionType = BarcodeHistoryActionType.Assigned,
            Reason = newEntity.Note,
            ChangedByUserId = _currentUser.UserId,
            ChangedByUserName = _currentUser.UserName,
            ChangedAtUtc = DateTime.UtcNow
        }, ct);

        await _historyRepo.SaveChangesAsync(ct);

        return newEntity.Id;
    }

    /// <summary>
    /// Tạo mới conversion và tự sinh barcode nội bộ nếu được yêu cầu.
    /// </summary>
    public async Task<ProductUnitConversion> CreateAsync(
        CreateProductUnitConversionRequest request,
        CancellationToken ct = default)
    {
        if (request.StoreId <= 0)
            throw new InvalidOperationException("StoreId không hợp lệ.");

        if (request.ProductVariantId <= 0)
            throw new InvalidOperationException("ProductVariantId không hợp lệ.");

        if (request.UnitId <= 0)
            throw new InvalidOperationException("UnitId không hợp lệ.");

        var factor = request.Factor <= 0 ? 1m : request.Factor;

        if (request.IsBaseUnit)
            factor = 1m;

        var exists = await _conversionRepo.ExistsByVariantAndUnitAsync(
            request.ProductVariantId,
            request.UnitId,
            null,
            ct);

        if (exists)
            throw new InvalidOperationException("Biến thể đã có đơn vị này.");

        var conversion = new ProductUnitConversion
        {
            StoreId = request.StoreId,
            ProductVariantId = request.ProductVariantId,
            UnitId = request.UnitId,
            Factor = factor,
            IsBaseUnit = request.IsBaseUnit,
            IsDefaultForSale = request.IsDefaultForSale,
            Price = request.Price,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder
        };

        await _conversionRepo.AddAsync(conversion, ct);
        await _conversionRepo.SaveChangesAsync(ct);

        if (request.AutoGeneratePrimaryBarcode)
        {
            var internalBarcode = await GenerateUniqueInternalBarcodeAsync(
                request.StoreId,
                conversion.Id,
                ct);

            await _barcodeRepo.ClearPrimaryFlagsAsync(conversion.Id, null, ct);

            var barcodeEntity = new ProductVariantUnitBarcode
            {
                StoreId = request.StoreId,
                ProductUnitConversionId = conversion.Id,
                Barcode = internalBarcode,
                BarcodeType = BarcodeType.Internal,
                IsPrimary = true,
                IsActive = true,
                Note = string.IsNullOrWhiteSpace(request.BarcodeNote)
                    ? "Tự sinh khi tạo đơn vị quy đổi"
                    : request.BarcodeNote!.Trim()
            };

            await _barcodeRepo.AddAsync(barcodeEntity, ct);
            await _barcodeRepo.SaveChangesAsync(ct);

            await _historyRepo.AddAsync(new ProductVariantBarcodeHistory
            {
                StoreId = request.StoreId,
                ProductVariantId = conversion.ProductVariantId,
                ProductUnitConversionId = conversion.Id,
                OldBarcodeId = null,
                NewBarcodeId = barcodeEntity.Id,
                OldBarcode = null,
                NewBarcode = barcodeEntity.Barcode,
                ActionType = BarcodeHistoryActionType.Assigned,
                Reason = barcodeEntity.Note,
                ChangedByUserId = _currentUser.UserId,
                ChangedByUserName = _currentUser.UserName,
                ChangedAtUtc = DateTime.UtcNow
            }, ct);

            await _historyRepo.SaveChangesAsync(ct);
        }

        return conversion;
    }

    public async Task<List<ProductVariantBarcodeHistoryRowDto>> GetBarcodeHistoryByConversionIdAsync(
        int productUnitConversionId,
        int take = 20,
        CancellationToken ct = default)
    {
        if (productUnitConversionId <= 0)
            return new List<ProductVariantBarcodeHistoryRowDto>();

        var storeId = _currentStore.StoreId;

        var items = await _historyRepo.GetRecentByConversionIdAsync(
            storeId,
            productUnitConversionId,
            take,
            ct);

        return items.Select(x => new ProductVariantBarcodeHistoryRowDto
        {
            Id = x.Id,
            ProductUnitConversionId = x.ProductUnitConversionId,
            ActionType = x.ActionType.ToString(),
            ActionTypeText = ToActionTypeText(x.ActionType),
            OldBarcode = x.OldBarcode,
            NewBarcode = x.NewBarcode,
            Reason = x.Reason,
            ChangedByUserName = x.ChangedByUserName,
            ChangedAtUtc = x.ChangedAtUtc
        }).ToList();
    }

    private async Task<string> GenerateUniqueInternalBarcodeAsync(
        int storeId,
        int seed,
        CancellationToken ct = default)
    {
        for (var i = 0; i < 1000; i++)
        {
            var barcode = Ean13Helper.GenerateInternal(storeId, seed + i);

            var exists = await _barcodeRepo.ExistsBarcodeAsync(
                storeId,
                barcode,
                excludeId: null,
                ct);

            if (!exists)
                return barcode;
        }

        throw new InvalidOperationException("Không thể sinh barcode EAN13 duy nhất.");
    }

    private static string? NormalizeBarcode(string? barcode)
    {
        barcode = (barcode ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(barcode) ? null : barcode;
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
    private async Task ClearOtherPrimaryActiveBarcodesAsync(
    int productUnitConversionId,
    int keepBarcodeId,
    CancellationToken ct = default)
    {
        var items = await _barcodeRepo.GetByConversionIdAsync(productUnitConversionId, ct);

        var needUpdate = items
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                x.IsPrimary &&
                x.Id != keepBarcodeId)
            .ToList();

        if (!needUpdate.Any())
            return;

        foreach (var item in needUpdate)
        {
            item.IsPrimary = false;
        }

        await _barcodeRepo.SaveChangesAsync(ct);
    }

    private async Task DemoteBarcodeAsync(
        ProductVariantUnitBarcode entity,
        bool deactivate,
        CancellationToken ct = default)
    {
        entity.IsPrimary = false;

        if (deactivate)
            entity.IsActive = false;

        await _barcodeRepo.SaveChangesAsync(ct);
    }
}