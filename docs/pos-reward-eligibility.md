# Tích điểm POS theo đơn vị gốc và ngành hàng

Cấu hình tại `/admin/reward-vouchers/settings`, bằng tài khoản có quyền sửa cấu hình hệ thống.

- Chỉ dòng bán bằng đơn vị gốc, đúng giá bán lẻ của đơn vị gốc và không giảm giá dòng hàng mới được tích điểm.
- Ví dụ giá hộp 6.000đ: bán hộp 6.000đ được tích; bốn hộp tự áp giá lốc còn 5.000đ/hộp không tích; bán một lốc 20.000đ không tích.
- Số lượng lớn vẫn tích nếu bán đúng đơn vị gốc và giữ nguyên giá lẻ. Ngưỡng số lượng cũ chỉ dùng đối chiếu trả hàng của đơn cũ chưa có dữ liệu tích điểm theo dòng.
- Đánh dấu **Ngành hàng không tích điểm** để loại toàn bộ sản phẩm trong ngành hàng và ngành hàng con. Loại trừ này ưu tiên hơn thiết lập tích điểm riêng của sản phẩm. Không có ngành hàng mới nào bị tự động loại trừ khi triển khai.
- Điểm được tính khi thanh toán, lưu số tiền đủ điều kiện theo dòng đơn hàng. Trả hàng dựa vào số đã tích, kể cả khi giá hoặc ngành hàng thay đổi sau đó. Không cộng lại điểm hay sửa số dư/voucher lịch sử khi triển khai.

## Cập nhật cơ sở dữ liệu khi triển khai

Source có migration `20260914073514_AddOrderRewardEligibilitySnapshots`: thêm hai cột nullable `decimal(18,2)` vào `OrderLines`: `RewardBaseUnitPrice` và `RewardableAmountSnapshot`. Không cập nhật dữ liệu đơn cũ.

Publish cả Web và Migrator từ cùng phiên bản source. Dùng quy trình triển khai có thay đổi schema của dự án (`eng/deployment/Deploy-WithSchemaMigration.ps1`). Lệnh cập nhật schema của Migrator là:

```powershell
dotnet GaoApp.Migrator.dll --schema-only
```

Migrator phải được cấu hình đúng cơ sở dữ liệu đích trước khi chạy. Web không tự chạy migration lúc khởi động; chỉ chép bản publish Web sẽ chưa bổ sung hai cột này.

## Kiểm tra hồi quy

- `OrderRewardCalculatorTests`: giá lẻ, giá lốc, đơn vị, giảm giá, ngành hàng cha/con, snapshot và làm tròn đồng.
- `RewardSettingsAdminServiceTests`: lưu loại trừ, bỏ chọn, phiên bản cấu hình cũ và ngăn thay đổi ngành hàng của cửa hàng khác.
- `PosRewardEligibilitySqlServerTests`: HTTP POS thật, SQL riêng, chốt đơn/retry, đổi cấu hình, trả hàng và phân quyền.
- `GaoApp.Tests.Browser/reward-settings.browser.cjs`: giao diện thật tại 320px, 390px và 1440px, tìm ngành hàng không dấu và giữ lựa chọn khi lọc.
