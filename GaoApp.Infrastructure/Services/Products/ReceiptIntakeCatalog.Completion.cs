using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Products;

public sealed partial class ReceiptIntakeCatalog
{
    public async Task ValidateCompletionAsync(int storeId, ReceiptIntakeCompletionDto draft, bool approving, CancellationToken ct)
    {
        draft.Name = Text(draft.Name, 200, "Tên sản phẩm");
        draft.Note = string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim();
        if (draft.Note?.Length > 500) throw new BusinessRuleException("Ghi chú tối đa 500 ký tự.");
        draft.Barcode = string.IsNullOrWhiteSpace(draft.Barcode) ? null : draft.Barcode.Trim();
        if (draft.Barcode is { } code && (code.Length > 64 || code.Any(c => c < '!' || c > '~')))
            throw new BusinessRuleException("Barcode tối đa 64 ký tự, không chứa khoảng trắng. Giữ nguyên số 0 đầu mã.");
        foreach (var price in new[] { draft.PurchasePrice, draft.RetailPrice, draft.WholesalePrice })
            if (price is < 0 or > 99999999999999.99m || price.HasValue && decimal.Round(price.Value, 2) != price.Value)
                throw new BusinessRuleException("Giá phải không âm, tối đa 2 chữ số thập phân và trong giới hạn lưu trữ.");
        if (draft.PurchasePrice is 0) throw new BusinessRuleException("Giá nhập phải lớn hơn 0 hoặc để trống để bổ sung sau.");
        var receiving = await FindUnitAsync(storeId, draft.UnitId, draft.UnitName, ct);
        draft.UnitId = receiving?.Id; draft.UnitName = receiving?.Name ?? Text(draft.UnitName, 100, "Đơn vị nhận");
        ProductUnitConversion? target = null;
        if (draft.ProductVariantId is > 0)
        {
            var variant = await VariantAsync(storeId, draft.ProductVariantId.Value, ct);
            draft.Name = variant.ProductVariantName ?? variant.Product.Name;
            draft.BaseUnitId = variant.Product.BaseUnitId; draft.BaseUnitName = variant.Product.BaseUnit.Name;
            target = await db.ProductUnitConversions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.StoreId == storeId &&
                x.ProductVariantId == variant.Id && x.UnitId == draft.UnitId, ct);
            if (target is not null && (!target.IsActive || target.IsDeleted || target.Factor != draft.Factor))
                throw new BusinessRuleException("Đơn vị đã có quy đổi khác hoặc ngừng hoạt động. Chọn lại đúng đơn vị sản phẩm.");
            // Linking never overwrites catalog prices, brand or sale permission.
            draft.BrandId = null; draft.RetailPrice = null; draft.WholesalePrice = null; draft.IsSellable = false;
        }
        else
        {
            draft.ProductVariantId = null;
            var baseUnit = await FindUnitAsync(storeId, draft.BaseUnitId, draft.BaseUnitName, ct);
            draft.BaseUnitId = baseUnit?.Id; draft.BaseUnitName = baseUnit?.Name ?? Text(draft.BaseUnitName, 100, "Đơn vị gốc");
            if (approving && draft.CategoryId is not > 0) throw new BusinessRuleException("Chọn danh mục sản phẩm trước khi duyệt.");
            if (draft.CategoryId.HasValue && !await db.Categories.AnyAsync(x => x.StoreId == storeId && x.Id == draft.CategoryId && x.IsActive && !x.IsDeleted, ct))
                throw new BusinessRuleException("Danh mục không còn sử dụng được.");
            if (draft.BrandId.HasValue && !await db.Brands.AnyAsync(x => x.StoreId == storeId && x.Id == draft.BrandId && x.IsActive && !x.IsDeleted, ct))
                throw new BusinessRuleException("Thương hiệu không còn sử dụng được.");
            if (approving && draft.IsSellable && draft.RetailPrice is not > 0)
                throw new BusinessRuleException("Nhập giá bán lẻ lớn hơn 0 trước khi cho phép bán POS.");
        }
        if (string.Equals(draft.UnitName, draft.BaseUnitName, StringComparison.OrdinalIgnoreCase) && draft.Factor != 1)
            throw new BusinessRuleException("Đơn vị nhận trùng đơn vị gốc thì quy đổi phải bằng 1.");
        if (draft.Barcode is not null && approving)
        {
            var owner = await db.ProductVariantUnitBarcodes.IgnoreQueryFilters()
                .Include(x => x.ProductUnitConversion).ThenInclude(x => x.ProductVariant).ThenInclude(x => x.Product)
                .Include(x => x.ProductUnitConversion).ThenInclude(x => x.Unit)
                .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Barcode == draft.Barcode, ct);
            if (owner is not null && (owner.ProductUnitConversionId != target?.Id || owner.IsDeleted || !owner.IsActive))
                throw new BusinessRuleException($"Barcode '{draft.Barcode}' đã thuộc '{owner.ProductUnitConversion.ProductVariant.Product.Name}' / đơn vị '{owner.ProductUnitConversion.Unit.Name}'. Hãy liên kết đúng sản phẩm hoặc kiểm tra lại mã.");
            if (owner is null && await db.ProductVariantBarcodeHistories.AnyAsync(x => x.StoreId == storeId &&
                (x.OldBarcode == draft.Barcode || x.NewBarcode == draft.Barcode), ct))
                throw new BusinessRuleException("Barcode đang được giữ trong lịch sử. Hãy kiểm tra mã, không tự tạo mã khác để bỏ qua xung đột.");
            if (await db.ProductBarcodeVerificationRequests.AnyAsync(x => x.StoreId == storeId && x.SuggestedBarcode == draft.Barcode &&
                x.Status == BarcodeVerificationRequestStatus.Pending && (target == null || x.ProductUnitConversionId != target.Id), ct))
                throw new BusinessRuleException("Barcode đang chờ duyệt cho đơn vị khác. Hãy xử lý đề xuất mã trước.");
        }
    }

    public async Task CompleteAsync(StockDocument document, StockDocumentProvisionalItem item, ReceiptIntakeCompletionDto draft, CancellationToken ct)
    {
        var conversion = await db.ProductUnitConversions.Include(x => x.ProductVariant).ThenInclude(x => x.Product)
            .SingleAsync(x => x.StoreId == document.StoreId && x.Id == item.ResolvedProductUnitConversionId, ct);
        if (draft.ProductVariantId is null)
        {
            var product = conversion.ProductVariant.Product;
            product.BrandId = draft.BrandId; product.IsSellable = draft.IsSellable;
            var selectedPhoto = item.ReviewPhoto ?? (draft.UsePackagingPhoto ? item.PackagingPhoto : null);
            if (selectedPhoto is { Length: > 0 } photo)
            {
                using var stream = new MemoryStream(photo, writable: false);
                var token = await tempUploads.UploadAsync(new() { Content = stream, FileName = "receipt-product.jpg",
                    ContentType = "image/jpeg", SizeBytes = photo.Length }, document.StoreId, user.UserId, ct);
                await productImages.CommitTempImagesAsync(document.StoreId, product.Id, [token], token, user.UserId, ct);
            }
            var baseUnit = await db.ProductUnitConversions.SingleAsync(x => x.StoreId == document.StoreId &&
                x.ProductVariantId == conversion.ProductVariantId && x.IsBaseUnit && !x.IsDeleted, ct);
            conversion.Price = draft.RetailPrice; conversion.WholesalePrice = draft.WholesalePrice;
            baseUnit.Price = draft.RetailPrice.HasValue ? decimal.Round(draft.RetailPrice.Value / conversion.Factor, 2) : null;
            baseUnit.WholesalePrice = draft.WholesalePrice.HasValue ? decimal.Round(draft.WholesalePrice.Value / conversion.Factor, 2) : null;
            product.BasePrice = baseUnit.Price ?? 0;
            if (draft.GenerateBaseBarcode && !await db.ProductVariantUnitBarcodes.AnyAsync(x => x.StoreId == document.StoreId &&
                x.ProductUnitConversionId == baseUnit.Id && x.IsActive && !x.IsDeleted, ct))
                await conversions.SaveBarcodeAsync(new UpsertProductVariantUnitBarcodeRequest {
                    ProductUnitConversionId = baseUnit.Id, BarcodeType = BarcodeType.Internal,
                    IsActive = true, IsPrimary = true, Note = "Quản lý yêu cầu tạo mã cho đơn vị gốc khi hoàn thiện hàng mới."
                }, ct);
        }
        if (draft.MakeBarcodePrimary && !string.IsNullOrWhiteSpace(draft.Barcode))
        {
            var code = await db.ProductVariantUnitBarcodes.SingleAsync(x => x.StoreId == document.StoreId &&
                x.ProductUnitConversionId == conversion.Id && x.Barcode == draft.Barcode && !x.IsDeleted && x.IsActive, ct);
            if (!code.IsPrimary) await conversions.SaveBarcodeAsync(new UpsertProductVariantUnitBarcodeRequest {
                Id = code.Id, ProductUnitConversionId = conversion.Id, Barcode = code.Barcode,
                BarcodeType = code.BarcodeType, IsActive = true, IsPrimary = true, Note = code.Note
            }, ct);
        }
        var line = document.Lines.Single(x => x.Id == item.ResolvedStockDocumentLineId);
        if (draft.PurchasePrice.HasValue && line.Quantity > item.Quantity && line.UnitPriceBeforeVat > 0 && line.UnitPriceBeforeVat != draft.PurchasePrice.Value)
            throw new BusinessRuleException("Sản phẩm đã có trên phiếu với giá nhập khác. Hãy để trống giá tại đây, rồi điều chỉnh giá trên dòng hàng sau khi liên kết.");
        var purchasePrice = draft.PurchasePrice ?? line.UnitPriceBeforeVat;
        if (purchasePrice > 0)
        {
            var amount = PurchasePricingPolicy.CalculateLineFromBeforeVat(line.Quantity, purchasePrice, document.HasVat, line.TaxRate);
            if (amount.LineTotalAfterVat > 9999999999999999.99m) throw new BusinessRuleException("Thành tiền vượt giới hạn.");
            // Seed the new variant's reference cost in base units; stock valuation is posted separately.
            if (draft.ProductVariantId is null)
            {
                var baseCost = decimal.Round(amount.UnitPriceBeforeVat / conversion.Factor, 2, MidpointRounding.AwayFromZero);
                if (baseCost > 9999999999999999.99m)
                    throw new BusinessRuleException("Giá vốn quy đổi về đơn vị gốc vượt giới hạn lưu trữ.");
                conversion.ProductVariant.CostPrice = baseCost;
            }
            line.UnitPriceBeforeVat = line.UnitCost = amount.UnitPriceBeforeVat;
            line.UnitPriceAfterVat = amount.UnitPriceAfterVat; line.TaxRate = amount.TaxRate;
            line.VatAmount = amount.VatAmount; line.LineTotal = amount.LineTotalAfterVat;
            document.TotalAmount = document.Lines.Where(x => !x.IsDeleted).Sum(x => x.LineTotal);
            document.VatAmount = document.Lines.Where(x => !x.IsDeleted).Sum(x => x.VatAmount);
            document.SubtotalBeforeVat = document.TotalAmount - document.VatAmount;
        }
        await db.SaveChangesAsync(ct);
    }
}
