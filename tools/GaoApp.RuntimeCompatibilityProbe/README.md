# Kiểm chứng chuyển tiếp .NET 8 → .NET 10

Công cụ dành cho kiểm thử, không dùng để cập nhật CSDL thật. Nó nạp assembly test cùng các dependency đã build, dùng fixture `InventoryPostingLocalDb` để tạo CSDL GUID tổng hợp trên SQL instance local, rồi kiểm tra cùng CSDL trong tiến trình runtime mới.

Yêu cầu: runtime .NET 8.0.31 và .NET 10.0.13, SDK theo `global.json`, một bộ `GaoApp.Tests.dll` cùng dependencies được lưu trước nâng cấp và một bộ build mới. Đặt `GAOAPP_TEST_SQL_SERVER=.\SQLEXPRESS` nếu dùng SQL Express; fixture mặc định dùng LocalDB.

```powershell
# Build bằng SDK 10 đã chuẩn bị; mỗi runtime dùng artifacts riêng.
dotnet build tools/GaoApp.RuntimeCompatibilityProbe -c Release `
  -p:BuildLegacyBaseline=true -p:RuntimeFrameworkVersion=8.0.31 `
  --artifacts-path .artifacts/compat-probe8
dotnet build tools/GaoApp.RuntimeCompatibilityProbe -c Release `
  --artifacts-path .artifacts/compat-probe10
```

Chạy `create` bằng host có runtime 8, truyền assembly test .NET 8 và một thư mục bằng chứng **chưa tồn tại** dưới `.artifacts/`. Sau đó chạy `verify` bằng host runtime 10, truyền assembly test mới và chính thư mục đó. Hai tiến trình độc lập kiểm tra:

- Fingerprint cột/index/FK/check/module, migration history và catalog tổng hợp.
- Hash mật khẩu cũ, từ chối mật khẩu sai.
- Key ring được bảo vệ bằng PFX, dữ liệu mã hóa và authentication ticket cũ.
- EF10 truy vấn catalog tạo bởi EF8.

Muốn dựng schema 79 giống dữ liệu local trước triển khai, thêm `20261014140000_AddBankNotificationBridge` làm tham số thứ tư của `create`; ở bước runtime 10 dùng `upgrade-audit` thay `verify`. Chế độ này xuất SQL review, áp dụng duy nhất migration audit hiện có thứ 80 trên CSDL test và kiểm tra các cấu trúc cũ ngoài phần audit mới, lịch sử cũ và catalog được giữ nguyên.

```text
create <legacy GaoApp.Tests.dll> <new evidence directory> [20261014140000_AddBankNotificationBridge]
verify <new GaoApp.Tests.dll> <same evidence directory>
upgrade-audit <new GaoApp.Tests.dll> <same evidence directory>
```

Đường dẫn bằng chứng phải nằm dưới `.artifacts/` của workspace. Tên CSDL phải khớp `GaoApp_R2_InventoryPosting_[A-F0-9]{32}`. Không đọc connection string của ứng dụng; không chấp nhận tên `GaoAppDb`. `create` ghi file ownership trước tạo DB; khi thành công cố ý giữ baseline cho tiến trình kế tiếp. `verify`/`upgrade-audit` luôn dọn CSDL thuộc ownership đó trong `finally`, kể cả khi assert lỗi. Nếu bỏ dở giữa hai bước, xem `owned-database.txt` để kiểm tra và dọn đúng CSDL test, không dọn theo wildcard.

PFX và tài khoản trong bằng chứng là dữ liệu tổng hợp, không dùng certificate hoặc mật khẩu thật. Không chỉnh `baseline.json`, không dùng bằng chứng của lượt khác để tránh nhầm ownership. Không đưa công cụ này vào Web/worker publish.
