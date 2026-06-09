# Module Reward / Voucher

## File chính

- `GaoApp.Domain/Entities/RewardSettings.cs`
- `GaoApp.Domain/Entities/CustomerRewardLedger.cs`
- `GaoApp.Domain/Entities/CustomerRewardVoucher.cs`
- `GaoApp.Domain/Entities/OrderRewardVoucher.cs`
- `GaoApp.Domain/Enums/CustomerRewardLedgerType.cs`
- `GaoApp.Domain/Enums/CustomerRewardVoucherStatus.cs`
- `GaoApp.Application/Services/Rewards/CustomerRewardService.cs`
- `GaoApp.Application/Services/Rewards/OrderRewardCalculator.cs`
- `GaoApp.Application/Services/Orders/POSService.cs`
- `GaoApp.Infrastructure/Repositories/Rewards/*.cs`
- `GaoApp.Web/Areas/Admin/Controllers/CustomerRewardsController.cs`
- `GaoApp.Web/Areas/Admin/Controllers/RewardVouchersController.cs`

## Entity chính

### RewardSettings

- `MoneyPerPoint`
- `PointsPerVoucher`
- `VoucherValue`
- `IsEnabled`
- `Note`

### CustomerRewardLedger

- `CustomerId`
- `Type`
- `Amount`
- `OrderId`
- `SalesReturnId`
- `VoucherId`
- `ReferenceCode`
- `Description`

### CustomerRewardVoucher

- `CustomerId`
- `VoucherCode`
- `Value`
- `RequiredAmount`
- `Status`
- `IssuedAtUtc`
- `UsedAtUtc`
- `UsedOrderId`
- `Description`
- `ReferenceCode`

## Ledger type

- `ImportOldBalance = 1`
- `SaleEarned = 2`
- `ReturnDeducted = 3`
- `VoucherRedeemed = 4`
- `ManualAdjust = 5`
- `SaleVoided = 6`
- `SaleRefunded = 7`

## Voucher status

- `Available = 1`
- `Used = 2`
- `Cancelled = 3`
- `Expired = 4`
- `Locked = 5`

## Nghiệp vụ đã có

- Xem balance.
- Tạo ledger thủ công.
- Xem voucher khả dụng.
- Redeem voucher.
- Xem summary.
- Danh sách voucher.
- Chi tiết voucher.
- Cancel voucher.
- Lock/unlock voucher.
- Log voucher.
- Print voucher.
- Lookup voucher by code.
- Apply voucher vào current cart.
- Clear voucher khỏi current cart.
- Mark voucher as used khi finalize.
- Reverse reward khi void/refund.

## Quy tắc phải giữ

1. Voucher phải thuộc đúng customer đang chọn trên đơn.
2. Không dùng voucher Used/Cancelled/Expired/Locked.
3. Không cho đơn âm tiền vì voucher.
4. Đổi customer phải xử lý voucher đang áp.
5. Void/refund phải xử lý ledger và voucher đúng.
6. Hàng tặng khuyến mãi không được tính điểm.
7. Nếu category/unit có rule loại trừ tích điểm thì `OrderRewardCalculator` phải xử lý.

## Khi sửa reward cần test

- Đơn thường có tích điểm.
- Đơn có hàng tặng.
- Đơn có voucher.
- Void đơn đã tích điểm.
- Refund đơn đã tích điểm.
- Voucher đã dùng không dùng lại được.
- Voucher của khách A không dùng cho khách B.
