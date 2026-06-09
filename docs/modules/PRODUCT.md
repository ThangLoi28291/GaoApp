# Module Product / Barcode / Unit Conversion

## File chính

- `GaoApp.Domain/Entities/Product.cs`
- `GaoApp.Domain/Entities/ProductVariant.cs`
- `GaoApp.Domain/Entities/ProductUnitConversion.cs`
- `GaoApp.Domain/Entities/ProductVariantUnitBarcode.cs`
- `GaoApp.Domain/Entities/ProductVariantBarcodeHistory.cs`
- `GaoApp.Application/Services/Products/ProductService.cs`
- `GaoApp.Application/Services/Products/ProductVariantService.cs`
- `GaoApp.Application/Services/Products/ProductUnitConversionService.cs`
- `GaoApp.Application/Services/Products/BarcodeLookupService.cs`
- `GaoApp.Application/Services/Products/BarcodeGovernanceService.cs`
- `GaoApp.Application/Services/Products/BarcodeHistoryService.cs`
- `GaoApp.Infrastructure/Repositories/Products/*.cs`
- `GaoApp.Web/Areas/Admin/Controllers/ProductController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/ProductUnitConversionController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/Barcode*.cs`

## Entity chính

### Product

- `Name`
- `Alias`
- `CategoryId`
- `SupplierId`
- `BrandId`
- `TaxId`
- `BaseUnitId`
- `BasePrice`
- `Description`
- `Content`
- `IsActive`
- `IsRewardEligibleOverride`
- `RewardBulkExcludeQuantity`

### ProductVariant

- `ProductId`
- `Sku`
- `ProductVariantName`
- `CostPrice`
- `Price`
- `IsActive`
- `PrimaryProductImageId`
- `HasInputInvoice`
- `WholesalePrice`

### ProductUnitConversion

- `ProductVariantId`
- `UnitId`
- `Factor`
- `IsBaseUnit`
- `IsDefaultForSale`
- `Price`
- `WholesalePrice`
- `IsActive`
- `SortOrder`

Unique index: `{{StoreId, ProductVariantId, UnitId}}`.

### ProductVariantUnitBarcode

- `ProductUnitConversionId`
- `Barcode`
- `BarcodeType`
- `IsPrimary`
- `IsActive`
- `Note`

Barcode active unique theo `{{StoreId, Barcode}}`.

## BarcodeType

- `Internal = 0`
- `External = 1`
- `Supplier = 2`
- `Packaging = 3`
- `Legacy = 4`

## Quy tắc cần giữ

1. Một variant có nhiều đơn vị quy đổi.
2. Barcode nên gắn theo `ProductUnitConversion`, không chỉ theo variant.
3. POS quét barcode phải biết đúng unit/factor/price.
4. Không cho trùng barcode active trong cùng store.
5. Barcode history phải giữ dấu vết đổi mã.
6. Product alias unique theo store.
7. Không xóa variant nếu đã phát sinh nhập/bán, nên khóa hoặc inactive.

## Khi sửa Product cần test

- Tạo sản phẩm không thuộc tính.
- Tạo sản phẩm có variant.
- Thêm đơn vị quy đổi.
- Gắn barcode cho đơn vị.
- Đổi barcode.
- Quét barcode ở POS.
- Nhập kho theo barcode đơn vị.
- Giá sỉ/giá lẻ theo unit.
- Ẩn/hiện sản phẩm/variant.
