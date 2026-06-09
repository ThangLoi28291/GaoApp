# GaoApp - AI Context

## Tổng quan dự án

GaoApp là hệ thống bán hàng / POS / ERP cho mô hình cửa hàng, siêu thị mini, tạp hóa, gạo, sữa, hàng tiêu dùng. Source hiện tại là ứng dụng .NET 8 MVC theo hướng Clean Architecture.

## Công nghệ chính phát hiện trong source

- .NET 8
- ASP.NET Core MVC
- Entity Framework Core 8
- SQL Server
- Cookie Authentication
- Permission-based Authorization
- FluentValidation
- AutoMapper
- SignalR cho POS realtime
- Serilog logging
- ClosedXML để xuất Excel
- QRCoder / QR thanh toán
- Bootstrap / jQuery / AJAX

## Project chính

```txt
GaoApp.Domain          # Entity, enum, base entity, constant nghiệp vụ
GaoApp.Application     # DTO, interface service/repository, service nghiệp vụ, validator
GaoApp.Infrastructure  # EF Core DbContext, configuration, repository implementation, tenant, storage
GaoApp.Web             # MVC controller/view, middleware, hub, js/css
```

Số lượng file quét được:

| Khu vực | Số file C# |
|---|---:|
| GaoApp.Domain | 132 |
| GaoApp.Application | 512 |
| GaoApp.Infrastructure | 161, chưa tính migration |
| GaoApp.Web | 132 |

Ngoài ra có khoảng 133 file `.cshtml` và nhiều file JS/CSS cho Admin/POS.

## Kiến trúc bắt buộc

Luồng chuẩn khi phát triển module mới:

```txt
Controller/View
    ↓
Service Interface trong Application
    ↓
Service Implementation trong Application
    ↓
Repository Interface trong Application
    ↓
Repository Implementation trong Infrastructure
    ↓
AppDbContext trong Infrastructure
```

Quy tắc quan trọng:

1. Không query trực tiếp `AppDbContext` trong Controller.
2. Controller chỉ gọi Service.
3. Service không phụ thuộc trực tiếp Infrastructure implementation.
4. Repository implementation mới query EF Core.
5. DTO đặt trong `GaoApp.Application.DTOs.<Module>`.
6. Validator đặt trong `GaoApp.Application.Validators.<Module>`.
7. Interface repository đặt trong `GaoApp.Application.Interfaces.Repositories.<Module>`.
8. Interface service đặt trong `GaoApp.Application.Interfaces.Services.<Module>`.
9. Implementation repository đặt trong `GaoApp.Infrastructure.Repositories.<Module>`.
10. Implementation service đặt trong `GaoApp.Application.Services.<Module>`.

## Tenant / StoreId

Source đang dùng multi-store theo tenant:

- `TenantContext`
- `ITenantContext`
- `ITenantContextWriter`
- `ICurrentStore`
- `BaseStoreEntity`
- `AppDbContext.CurrentStoreId`
- Global Query Filter theo `StoreId`

`AppDbContext` có guard quan trọng:

- Entity kế thừa `BaseStoreEntity` khi thêm mới sẽ tự gán `StoreId` theo tenant hiện tại.
- Khi update/delete sẽ kiểm tra `StoreId` gốc khớp tenant hiện tại.
- Nếu sai tenant sẽ throw exception.

Khi viết code mới phải luôn nhớ: mọi query dữ liệu cửa hàng phải đúng StoreId. Nhiều query đã được global filter xử lý, nhưng khi dùng `IgnoreQueryFilters`, raw SQL, hoặc system context thì phải cực kỳ cẩn thận.

## Soft delete / audit

`AppDbContext.SaveChanges/SaveChangesAsync` tự xử lý:

- `CreatedAtUtc`
- `CreatedBy`
- `UpdatedAtUtc`
- `UpdatedBy`
- `IsDeleted`
- `DeletedAtUtc`
- `DeletedBy`

Delete với `BaseEntity` thường được chuyển sang soft delete. Ngoại lệ có hard delete cho `ProductVariantAttributeValue`.

## Middleware / Startup chính

`GaoApp.Web/Program.cs` đang có:

- Serilog bootstrap logger
- Kestrel force HTTP/1.1
- MVC + FluentValidation
- `AddApplication()`
- `AddInfrastructure()`
- Cookie auth `/admin/account/login`
- Permission policy provider / authorization handler
- Forwarded Headers
- Health checks `/health/live`, `/health/ready`
- `TenantResolutionMiddleware`
- `TerminalResolutionMiddleware`
- SignalR hub `/hubs/pos`

## Module chính hiện có

- Catalog: Category, Brand, Supplier, Tax, Unit
- Product, ProductVariant, ProductImage, MediaAsset
- Unit Conversion, Variant Unit Barcode, Barcode History, Barcode Verification
- POS bán hàng
- Order, OrderLine, OrderPayment
- Customer
- Reward / Voucher tích điểm
- Promotion / Buy X Get Y / Combo
- POS Shift / Cash transaction / Handover / Closing Slip
- Inventory / Warehouse / StockDocument / StockCount / StockTransfer / Adjustment
- Input Invoice / Invoice
- Return / Refund / Void
- Security / Role / Permission / UserInStore
- AuditLog
- DisplayPromotion
- StoreBankAccount / QR payment

## Ưu tiên khi sửa code

1. POS phải nhanh, hạn chế query nặng trong thao tác quét mã / đổi số lượng / thanh toán.
2. Không làm mất dữ liệu cũ.
3. Không tự ý đổi migration nếu chưa cần.
4. Không viết lại toàn bộ service lớn nếu chỉ sửa một phần.
5. Khi sửa hàm, ghi rõ file, hàm, đoạn thay thế.
6. Với chức năng bán hàng, phải kiểm tra tác động tới: Order total, Voucher, Reward, Promotion, Inventory, Shift, Receipt.
7. Với kho, phải kiểm tra tác động tới: InventoryBalance, InventoryTransaction, Valuation, CostLayer, StockDocument status.
8. Với multi-store, không bỏ qua StoreId.
