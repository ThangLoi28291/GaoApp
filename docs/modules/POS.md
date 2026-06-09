# Module POS

## File chính

- `GaoApp.Application/Services/Orders/POSService.cs`
- `GaoApp.Application/Interfaces/Services/Orders/IPOSService.cs`
- `GaoApp.Infrastructure/Repositories/Orders/OrderRepository.cs`
- `GaoApp.Web/Areas/Admin/Controllers/POSController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/POSReceiptController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/POSReturnController.cs`
- `GaoApp.Web/wwwroot/Admin/js/pos/*.js`
- `GaoApp.Web/wwwroot/Admin/css/pos/pos.css`

## Nghiệp vụ đã có

- Tạo đơn nháp.
- Lấy giỏ hiện tại.
- Quét barcode vào giỏ.
- Tìm sản phẩm cho POS.
- Tìm / tạo / set / clear khách hàng.
- Giữ đơn và mở lại đơn.
- Thêm/xóa thanh toán.
- Hoàn tất đơn.
- In hóa đơn.
- Hủy đơn nháp.
- Void đơn đã hoàn thành.
- Refund / SalesReturn.
- Apply voucher reward vào giỏ.
- Clear voucher khỏi giỏ.
- Tính lại giá theo nhóm khách.
- Áp dụng promotion.
- Áp dụng giá pack tốt nhất.
- Ghi POS audit log.
- Kiểm tra ca POS đang mở.
- Kiểm tra ownership ca.

## Các hàm POSService đáng chú ý

- `CreateDraftAsync`
- `GetDraftAsync`
- `AddItemAsync`
- `AddItemByBarcodeAsync`
- `UpdateLineQtyAsync`
- `RemoveLineAsync`
- `AddPaymentAsync`
- `RemovePaymentAsync`
- `FinalizeAsync`
- `GetReceiptAsync`
- `GetOrdersAsync`
- `HoldAndCreateNewDraftAsync`
- `ResumeHeldAsync`
- `GetCurrentCartAsync`
- `EnsureCurrentCartAsync`
- `ScanToCurrentCartAsync`
- `FinalizeCurrentCartAsync`
- `SearchProductsForPOSAsync`
- `SearchCustomersForPOSAsync`
- `SetCustomerForCurrentCartAsync`
- `UpdateCurrentCartDiscountAsync`
- `VoidCompletedOrderAsync`
- `RefundCompletedOrderAsync`
- `ApplyRewardVouchersToCurrentCartAsync`
- `ClearRewardVouchersFromCurrentCartAsync`
- `ApplyPromotionsForOrderAsync`
- `ApplyBestPackPriceForVariantLinesAsync`

## Entity liên quan

- `Order`
- `OrderLine`
- `OrderPayment`
- `Customer`
- `Product`
- `ProductVariant`
- `ProductUnitConversion`
- `ProductVariantUnitBarcode`
- `Promotion`
- `CustomerRewardVoucher`
- `POSShift`
- `SalesReturn`

## Quy tắc phải giữ

1. POS phải nhanh.
2. Không load toàn bộ sản phẩm nếu chỉ tìm barcode.
3. Không làm mất giỏ hiện tại khi lỗi validate.
4. Đơn completed/voided/refunded không được xử lý như draft.
5. Finalize phải cập nhật payment, inventory, reward, shift, receipt đúng.
6. Void/refund phải đảo reward/inventory/shift đúng.
7. Khi đổi khách hàng phải kiểm tra lại giá, voucher, reward.
8. Khi thay đổi giỏ phải tính lại promotion và totals.

## Khi sửa POS cần test

- Quét barcode sản phẩm thường.
- Quét barcode đơn vị quy đổi.
- Thêm số lượng lớn.
- Đổi số lượng.
- Xóa dòng.
- Áp voucher.
- Đổi khách hàng sau khi đã có voucher.
- Mua hàng có promotion.
- Thanh toán đủ.
- Thanh toán dư tiền mặt.
- Thanh toán chuyển khoản.
- In hóa đơn.
- Void đơn.
- Refund đơn.
- Ca không mở.
- Ca người khác đang sở hữu.
