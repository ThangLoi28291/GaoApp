# Cách mở chat mới để nâng cấp GaoApp

## Mẫu nhanh nhất

Upload các file:

```txt
docs/00_AI_CONTEXT.md
docs/CHANGELOG_AI.md
docs/modules/<MODULE>.md
file code đang cần sửa
```

Sau đó nhắn:

```txt
Đọc context trước.
Đây là dự án GaoApp.
Chỉ tập trung module <MODULE>.
Không viết lại toàn bộ.
Chỉ rõ lỗi nằm ở file nào, hàm nào, và viết đoạn thay thế.
```

## Ví dụ sửa Buy X Get Y

Upload:

```txt
docs/00_AI_CONTEXT.md
docs/CHANGELOG_AI.md
docs/modules/PROMOTION.md
GaoApp.Application/Services/Orders/POSService.cs
GaoApp.Application/Services/Promotions/PromotionEngine.cs
GaoApp.Domain/Entities/OrderLine.cs
```

Tin nhắn:

```txt
Đọc context trước.
Tôi đang sửa Buy X Get Y.
Lỗi: đổi số lượng dòng chính nhưng dòng hàng tặng chưa cập nhật đúng.
Chỉ kiểm tra POSService và PromotionEngine.
Không sửa lan qua Reward/Inventory nếu không cần.
```

## Ví dụ sửa giao diện POS điện thoại

Upload:

```txt
docs/00_AI_CONTEXT.md
docs/modules/POS.md
GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml
GaoApp.Web/wwwroot/Admin/css/pos/pos.css
GaoApp.Web/wwwroot/Admin/js/pos/pos.render.js
```

Tin nhắn:

```txt
Đọc context trước.
Tôi muốn chỉnh POS dùng tốt trên điện thoại.
Chỉ tập trung layout giỏ hàng và danh sách sản phẩm.
```

## Ví dụ sửa nhập kho

Upload:

```txt
docs/00_AI_CONTEXT.md
docs/modules/INVENTORY.md
GaoApp.Application/Services/Inventory/StockDocumentService.cs
GaoApp.Application/Services/Inventory/InventoryMovementService.cs
```

Tin nhắn:

```txt
Đọc context trước.
Kiểm tra luồng nhập kho theo đơn vị quy đổi.
Chỉ rõ công thức Quantity, Factor, BaseQuantity, UnitCost, LineTotal.
```
