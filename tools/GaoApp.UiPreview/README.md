# GaoApp UI preview

Máy chủ loopback chỉ đọc để xem bộ thành phần Commerce, render trực tiếp Razor/model trong `GaoApp.Web`. Không khởi động Web/worker hoặc kết nối database. Trang mẫu chỉ nhận GET; gửi POST trả 405. Static file có thể nhận HEAD.

Sau khi có SDK/runtime đúng `global.json` và `Directory.Build.props` của repo:

```powershell
dotnet build tools/GaoApp.UiPreview/GaoApp.UiPreview.csproj
dotnet tools/GaoApp.UiPreview/bin/Debug/net10.0/GaoApp.UiPreview.dll --check-contract
dotnet tools/GaoApp.UiPreview/bin/Debug/net10.0/GaoApp.UiPreview.dll --Logging:LogLevel:Default=Warning
```

Mở `http://127.0.0.1:5206/admin/ui-foundation`. Đóng máy chủ bằng Ctrl+C. `/isolation?commerce=0` và `/isolation?commerce=1` dùng so sánh CSS cũ khi chưa/có nạp Commerce nhưng chưa opt-in. `/health` xác nhận đây là preview.

Mở `/admin/ui-foundation/compare` để đổi chế độ Máy tính / Điện thoại mới / So sánh. Iframe giữ viewport desktop 1440 × 960 và điện thoại 390 × 844. Mặc định mở bản điện thoại v2; route trực tiếp `/admin/ui-foundation/mobile` có năm màn tương tác bằng dữ liệu mẫu. Thanh menu là fixture trình bày; chưa lấy hoặc sửa menu thật. Bản desktop `/admin/ui-foundation` giữ bộ nền tham chiếu trước.

Ứng dụng chính cung cấp cùng các route `/admin/ui-foundation`, `/admin/ui-foundation/compare`, `/admin/ui-foundation/mobile`, có xác thực và policy `security.role.permissions` từ controller thật. Công cụ chỉ render view bằng dữ liệu mẫu; không ánh xạ các controller nghiệp vụ. `--check-contract` kiểm tra metadata controller thật đã link vào build, không thay thế kiểm thử authorization HTTP của ứng dụng đầy đủ.

Hướng thiết kế, kết quả và giới hạn môi trường: [UI-01 foundation](../../docs/development/MOBILE-UI-01-FOUNDATION-20261025.md).

Bản điện thoại mới nhất, nguồn tham khảo và bằng chứng: [Mobile v2](../../docs/development/MOBILE-UI-MOBILE-V2-20261025.md).

## Menu thật · UI-02a–b

`/admin/ui-foundation/navigation` render trực tiếp `_VerticalMenu`, `_NavigationItems` và model trình bày dùng trong Web, bằng cấu hình in-memory. Thêm `?surface=pos` để kiểm tra cùng partial trong Bootstrap drawer; `?case=empty`, `one`, `filtered`, `tenantless`, `userless`, `encoded` để kiểm tra trạng thái. `&admin=1` chỉ bật quyền của fixture, không dùng tài khoản thật.

Route `/navigation` chỉ thuộc công cụ này. Trong ứng dụng, menu thật vẫn xuất hiện ở các layout Admin/POS hiện có và dùng `AdminMenuService` thật. Fixture không được đăng ký vào Web; mọi phương thức ghi của fixture đều ném lỗi.

```powershell
dotnet tools/GaoApp.UiPreview/bin/Debug/net10.0/GaoApp.UiPreview.dll --check-navigation
```

Lệnh kiểm tra model trình bày thật: cây sâu, URL/query/area, thứ tự, nguồn đã lọc/trống, quy tắc receipt, cycle/duplicate và không sửa DTO. Không thay kiểm thử SQL hoặc authorization HTTP thực. Xem [báo cáo UI-02a–b](../../docs/development/MOBILE-UI-02AB-NAVIGATION-20261025.md).
