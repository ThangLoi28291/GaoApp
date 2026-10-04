using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Products;

public sealed partial class ReceiptIntakeCatalog(AppDbContext db, IProcurementCatalogService catalog,
    IUnitService unitService, IProductUnitConversionService conversions, ICurrentUser user,
    IReceiptBarcodeProposalService barcodeProposals,
    GaoApp.Application.Interfaces.Services.Media.ITempUploadService tempUploads,
    GaoApp.Application.Interfaces.Services.Media.IProductImageService productImages) : IReceiptIntakeCatalog
{
    public async Task<ProductUnitConversion> PrepareKnownAsync(StockDocument document, RecordKnownReceiptItemRequest request, CancellationToken ct)
    {
        var conversion = await db.ProductUnitConversions.Include(x => x.Unit)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.BaseUnit)
            .SingleOrDefaultAsync(x => x.StoreId == document.StoreId && x.Id == request.ProductUnitConversionId &&
                x.IsActive && !x.IsDeleted && x.Unit.StoreId == document.StoreId && x.Unit.IsActive && !x.Unit.IsDeleted, ct)
            ?? throw new BusinessRuleException("Đơn vị sản phẩm không còn sử dụng được.");
        await VariantAsync(document.StoreId, conversion.ProductVariantId, ct);
        if (conversion.Factor != request.Factor)
            throw new BusinessRuleException("Quy đổi đã thay đổi. Hãy chọn lại đơn vị.");
        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var code = request.Barcode.Trim();
            var active = await db.ProductVariantUnitBarcodes.SingleOrDefaultAsync(x => x.StoreId == document.StoreId &&
                x.Barcode == code && x.IsActive && !x.IsDeleted, ct);
            if (active is not null && active.ProductUnitConversionId != conversion.Id)
                throw new BusinessRuleException("Mã thuộc một sản phẩm hoặc đơn vị khác.");
            if (active is null)
                await barcodeProposals.ProposeWithinTransactionAsync(document.StoreId, document.Id, user.UserId ?? 0, new()
                {
                    ProductUnitConversionId = conversion.Id, Factor = request.Factor,
                    Barcode = code, Note = request.Note, LeaseToken = request.LeaseToken
                }, ct);
        }
        return conversion;
    }
    public Task LockAsync(int storeId, CancellationToken ct)
    {
        var resource = $"receipt-barcode-proposals:{storeId}";
        return db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51000, 'Receipt catalog is busy. Please retry.', 1;", ct);
    }

    public async Task ValidateAsync(int storeId, CaptureReceiptIntakeRequest request, CancellationToken ct)
    {
        request.Name = Text(request.Name, 200, "Tên hàng");
        request.Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        if (request.Barcode is { } code && (code.Length > 64 || code.Any(c => c < '!' || c > '~')))
            throw new BusinessRuleException("Barcode gồm tối đa 64 ký tự in được, không có khoảng trắng.");
        if (request.Barcode is not null && await db.ProductVariantUnitBarcodes.AnyAsync(x =>
            x.StoreId == storeId && x.Barcode == request.Barcode && x.IsActive && !x.IsDeleted, ct))
            throw new BusinessRuleException("Mã đã có trong danh mục. Hãy quét lại để chọn đúng sản phẩm và đơn vị.");
        if (request.Barcode is not null && await db.ProductBarcodeVerificationRequests.AnyAsync(x =>
            x.StoreId == storeId && x.SuggestedBarcode == request.Barcode && x.Status == BarcodeVerificationRequestStatus.Pending, ct))
            throw new BusinessRuleException("Mã đang được đề xuất cho một đơn vị có sẵn. Hãy kiểm tra đề xuất mã trước.");
        var receiving = await FindUnitAsync(storeId, request.UnitId, request.UnitName, ct);
        request.UnitId = receiving?.Id;
        request.UnitName = receiving?.Name ?? Text(request.UnitName, 100, "Đơn vị nhận");
        if (request.ProductVariantId is > 0)
        {
            var variant = await VariantAsync(storeId, request.ProductVariantId.Value, ct);
            if (request.BaseUnitId.HasValue && request.BaseUnitId != variant.Product.BaseUnitId)
                throw new BusinessRuleException("Đơn vị gốc của sản phẩm đã thay đổi. Hãy chọn lại sản phẩm.");
            request.BaseUnitId = variant.Product.BaseUnitId;
            request.BaseUnitName = variant.Product.BaseUnit.Name;
            request.Name = variant.ProductVariantName ?? variant.Product.Name;
            if (receiving is not null)
            {
                var existing = await db.ProductUnitConversions.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.StoreId == storeId && x.ProductVariantId == variant.Id && x.UnitId == receiving.Id, ct);
                if (existing is not null)
                    throw new BusinessRuleException(existing.IsActive && !existing.IsDeleted && existing.Factor == request.Factor
                        ? "Đơn vị này đã có. Hãy chọn đơn vị có sẵn để nhập hàng."
                        : "Sản phẩm đã có đơn vị này với quy đổi khác hoặc đã ngừng sử dụng. Hãy kiểm tra danh mục.");
            }
        }
        else
        {
            request.ProductVariantId = null;
            var baseUnit = await FindUnitAsync(storeId, request.BaseUnitId, request.BaseUnitName, ct);
            request.BaseUnitId = baseUnit?.Id;
            request.BaseUnitName = baseUnit?.Name ?? Text(request.BaseUnitName, 100, "Đơn vị gốc");
        }
        if (string.Equals(request.UnitName, request.BaseUnitName, StringComparison.OrdinalIgnoreCase) && request.Factor != 1m)
            throw new BusinessRuleException("Đơn vị nhận trùng đơn vị gốc thì tỷ lệ quy đổi phải bằng 1.");
        if (request.CategoryId.HasValue && !await db.Categories.AnyAsync(x => x.Id == request.CategoryId &&
            x.StoreId == storeId && x.IsActive && !x.IsDeleted, ct))
            throw new BusinessRuleException("Danh mục không còn sử dụng được.");
    }

    public async Task<ProductUnitConversion> ResolveAsync(StockDocument document, StockDocumentProvisionalItem item,
        int? categoryId, ReceiptIntakePermissions permissions, CancellationToken ct)
    {
        var storeId = document.StoreId;
        if (item.ProposedFactor is not > 0 || string.IsNullOrWhiteSpace(item.ProposedBaseUnitName))
            throw new BusinessRuleException("Thiếu khai báo tỷ lệ quy đổi của nhân viên.");
        if (!string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot) && !permissions.CanCreateBarcode)
            throw new BusinessRuleException("Cần quyền tạo barcode để duyệt mã hàng này.");
        int variantId;
        if (item.ProposedProductVariantId is > 0)
        {
            if (!permissions.CanUpdateProduct)
                throw new BusinessRuleException("Cần quyền cập nhật sản phẩm để thêm quy cách.");
            var variant = await VariantAsync(storeId, item.ProposedProductVariantId.Value, ct);
            if (variant.Product.BaseUnitId != item.ProposedBaseUnitId ||
                !string.Equals(variant.Product.BaseUnit.Name, item.ProposedBaseUnitName, StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("Đơn vị gốc đã thay đổi so với khai báo. Hãy kiểm tra lại phiếu.");
            variantId = variant.Id;
        }
        else
        {
            if (!permissions.CanCreateProduct)
                throw new BusinessRuleException("Cần quyền tạo sản phẩm để duyệt hàng mới.");
            if (!document.SupplierId.HasValue)
                throw new BusinessRuleException("Hãy chọn nhà cung cấp của phiếu trước khi duyệt sản phẩm mới.");
            var created = await catalog.CreateProductWithinTransactionAsync(new()
            {
                GenerateDefaultBarcode = string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot),
                Name = item.NameSnapshot, CategoryId = categoryId ?? item.ProposedCategoryId ?? 0,
                SupplierId = document.SupplierId.Value, UnitId = item.ProposedBaseUnitId,
                NewUnitName = item.ProposedBaseUnitId.HasValue ? null : item.ProposedBaseUnitName
            }, permissions.CanCreateUnit, ct);
            variantId = created.ProductVariantId;
        }
        var receiving = await FindUnitAsync(storeId, item.UnitId, item.UnitNameSnapshot, ct);
        if (receiving is null)
        {
            if (!permissions.CanCreateUnit)
                throw new BusinessRuleException("Cần quyền tạo đơn vị để duyệt đơn vị mới.");
            var result = await unitService.CreateAsync(storeId, new CreateUnitRequest
            {
                Name = Text(item.UnitNameSnapshot, 100, "Đơn vị nhận"), Status = true
            }, user.UserId, ct);
            if (!result.IsSuccess)
                throw new BusinessRuleException(result.HasValidationErrors
                    ? string.Join(" ", result.ValidationErrors.Select(x => x.ErrorMessage)) : result.Error.Message);
            receiving = await FindUnitAsync(storeId, result.Value, null, ct);
        }
        var conversion = await db.ProductUnitConversions.IgnoreQueryFilters().Include(x => x.Unit)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.StoreId == storeId && x.ProductVariantId == variantId && x.UnitId == receiving!.Id, ct);
        if (conversion is not null)
        {
            if (conversion.IsDeleted || !conversion.IsActive || conversion.Factor != item.ProposedFactor.Value)
                throw new BusinessRuleException("Quy cách đã tồn tại với tỷ lệ khác hoặc đã ngừng sử dụng. Không thể tự ghi đè.");
        }
        else
        {
            conversion = await conversions.CreateAsync(new CreateProductUnitConversionRequest
            {
                StoreId = storeId, ProductVariantId = variantId, UnitId = receiving!.Id,
                Factor = item.ProposedFactor.Value, IsActive = true, IsBaseUnit = false,
                IsDefaultForSale = false, AutoGeneratePrimaryBarcode = string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot),
                BarcodeNote = "Tạo quy cách sau khi duyệt khai báo nhận hàng."
            }, ct);
        }
        // Retired/reserved barcodes must be decided explicitly, never silently discarded.
        if (!string.IsNullOrWhiteSpace(item.RawBarcodeSnapshot))
        {
            var codes = await db.ProductVariantUnitBarcodes.IgnoreQueryFilters().Where(x =>
                x.StoreId == storeId && x.Barcode == item.RawBarcodeSnapshot).ToListAsync(ct);
            if (codes.Any(x => x.ProductUnitConversionId != conversion.Id || x.IsDeleted || !x.IsActive) ||
                (!codes.Any() && await db.ProductVariantBarcodeHistories.AnyAsync(x => x.StoreId == storeId &&
                    (x.OldBarcode == item.RawBarcodeSnapshot || x.NewBarcode == item.RawBarcodeSnapshot), ct)))
                throw new BusinessRuleException("Mã đã thuộc sản phẩm khác hoặc được giữ trong lịch sử. Hãy kiểm tra mã trước khi duyệt.");
        }
        return conversion;
    }

    private async Task<ProductVariant> VariantAsync(int storeId, int id, CancellationToken ct)
        => await db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .SingleOrDefaultAsync(x => x.Id == id && x.StoreId == storeId && x.IsActive && !x.IsDeleted &&
                x.Product.StoreId == storeId && x.Product.IsActive && !x.Product.IsDeleted &&
                x.Product.BaseUnit.StoreId == storeId && x.Product.BaseUnit.IsActive && !x.Product.BaseUnit.IsDeleted, ct)
            ?? throw new BusinessRuleException("Sản phẩm hoặc đơn vị gốc không còn sử dụng được.");

    private async Task<Unit?> FindUnitAsync(int storeId, int? id, string? name, CancellationToken ct)
    {
        if (id is > 0)
            return await db.Units.SingleOrDefaultAsync(x => x.Id == id && x.StoreId == storeId && x.IsActive && !x.IsDeleted, ct)
                ?? throw new BusinessRuleException("Đơn vị không tồn tại trong cửa hàng hoặc đã ngừng sử dụng.");
        var normalized = Text(name, 100, "Tên đơn vị").ToUpperInvariant();
        var unit = await db.Units.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Name.Trim().ToUpper() == normalized, ct);
        if (unit is not null && (unit.IsDeleted || !unit.IsActive))
            throw new BusinessRuleException("Đơn vị đã ngừng sử dụng. Hãy chọn đơn vị khác hoặc nhờ quản lý kiểm tra.");
        return unit;
    }

    private static string Text(string? text, int limit, string label)
    {
        text = text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > limit)
            throw new BusinessRuleException($"{label} là bắt buộc, tối đa {limit} ký tự.");
        return text;
    }
}
