# Module Promotion

## File chính

- `GaoApp.Domain/Entities/Promotion.cs`
- `GaoApp.Domain/Entities/PromotionItem.cs`
- `GaoApp.Domain/Entities/PromotionComboRule.cs`
- `GaoApp.Domain/Enums/PromotionType.cs`
- `GaoApp.Domain/Enums/PromotionDiscountType.cs`
- `GaoApp.Application/Services/Promotions/PromotionEngine.cs`
- `GaoApp.Application/Services/Promotions/PromotionAdminService.cs`
- `GaoApp.Application/Interfaces/Services/Promotions/IPromotionEngine.cs`
- `GaoApp.Application/Interfaces/Repositories/Promotions/IPromotionRepository.cs`
- `GaoApp.Infrastructure/Repositories/Promotions/PromotionRepository.cs`
- `GaoApp.Web/Areas/Admin/Controllers/PromotionController.cs`
- `GaoApp.Web/wwwroot/Admin/js/promotions.page.js`
- `GaoApp.Web/Areas/Admin/Views/Promotion/*`

## Loại khuyến mãi hiện có

`PromotionType`:

- `ProductDiscount = 1`
- `ComboFixedPrice = 2`
- `BuyXGetY = 3`

`PromotionDiscountType`:

- `Percentage = 1`
- `FixedAmount = 2`

## Entity Promotion

Field quan trọng:

- `Name`
- `Description`
- `Type`
- `DiscountType`
- `DiscountValue`
- `StartAtUtc`
- `EndAtUtc`
- `IsActive`
- `Priority`
- `CustomerPriceTier`
- `ComboFixedPrice`
- `ComboNote`
- `BuyQuantity`
- `GetQuantity`
- `RequireGiftQuantityInCart`
- `Items`
- `ComboRules`

## OrderLine hỗ trợ promotion

Field promotion trên `OrderLine`:

- `OriginalUnitPrice`
- `PromotionDiscount`
- `PromotionId`
- `PromotionName`
- `ComboPromotionId`
- `ComboPromotionName`
- `ComboPromotionNote`
- `ComboAllocatedDiscount`
- `PromotionType`
- `PromotionBuyQuantity`
- `PromotionGiftQuantity`
- `IsPromotionGift`
- `GiftPromotionId`
- `GiftSourceLineId`
- `GiftPromotionName`
- `GiftPromotionNote`

## Buy X Get Y - quy tắc nên giữ

Mô hình tốt nhất với source hiện tại: tạo `OrderLine` riêng cho hàng tặng.

Dòng hàng mua:

- Quantity > 0
- UnitPrice theo giá bán thật
- Promotion metadata có thể ghi promotion áp dụng

Dòng hàng tặng:

- `IsPromotionGift = true`
- `GiftPromotionId = Promotion.Id`
- `GiftSourceLineId = Id dòng mua chính`
- `UnitPrice = 0`
- `LineDiscount = 0`
- `LineTotal = 0`
- `PromotionDiscount = 0`
- `GiftPromotionName = Promotion.Name`
- `GiftPromotionNote` ghi rõ mua X tặng Y

## Khi sửa Buy X Get Y phải kiểm tra

1. Mua chưa đủ X thì không có hàng tặng.
2. Mua đủ X thì tạo đúng Y.
3. Mua nhiều lần X thì tặng đúng bội số.
4. Đổi số lượng từ đủ X xuống chưa đủ X phải xóa/cập nhật dòng tặng.
5. Xóa dòng mua chính phải xóa dòng tặng liên quan.
6. Dòng tặng không tính điểm reward.
7. Dòng tặng không làm tăng `GrandTotal`.
8. Dòng tặng vẫn phải xử lý tồn kho nếu cửa hàng thực sự xuất hàng tặng.
9. In bill phải hiển thị rõ `(Hàng tặng)`.
10. Không cho sửa giá dòng tặng như hàng thường.

## Combo Fixed Price

Entity hỗ trợ:

- `Promotion.ComboFixedPrice`
- `Promotion.ComboNote`
- `Promotion.ComboRules`
- `Order.ComboDiscountTotal`
- `Order.ComboPromotionId`
- `Order.ComboPromotionName`
- `Order.ComboPromotionNote`
- `OrderLine.ComboPromotionId`
- `OrderLine.ComboAllocatedDiscount`

Khi sửa combo cần kiểm tra phân bổ giảm giá vào từng dòng để tính lãi/gross profit đúng.

## Lưu ý hiệu năng

- Không query promotion từng dòng nếu có thể load promotion active một lần theo StoreId/time/type.
- Ưu tiên xử lý trên lines đã loaded trong draft.
- Index `Promotions` đã có theo StoreId/Type/Active/Start/End/IsDeleted.
