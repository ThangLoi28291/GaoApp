# Yêu cầu sửa / hủy thu chi POS

## Sử dụng

- Tại **Ca POS → Lịch sử thu / chi tiền mặt**, chọn **Yêu cầu sửa / hủy** trên phiếu. Hoặc vào **Yêu cầu nghiệp vụ → Điều chỉnh thu/chi → Thu/chi của tôi** để tìm cả phiếu thuộc ca đã đóng.
- Nhân viên chỉ đề nghị trên phiếu do mình tạo trong cửa hàng đang đăng nhập. Cần quyền xem ca và quyền đối soát ca (quyền đang dùng để tạo thu/chi).
- Sửa được loại **Thu ↔ Chi**, số tiền, nội dung và ghi chú. Hủy phiếu cũng phải nêu lý do. Popup hiển thị ảnh hưởng lên tiền dự kiến cuối ca.
- Gửi yêu cầu chưa làm thay đổi phiếu hay số tiền. Mỗi phiếu có tối đa một yêu cầu chờ; người gửi được rút khi chưa xử lý.
- Admin vào **Yêu cầu nghiệp vụ → Điều chỉnh thu/chi** (`/admin/pos-shift/requests?tab=cash`), xem trước/sau rồi duyệt hoặc từ chối. Tab bên cạnh quản lý đổi phương thức thanh toán đơn hàng và nhận cọc. Đường dẫn thu/chi cũ tự chuyển sang trang chung, giữ bộ lọc ca/phiếu/yêu cầu. Từ chối bắt buộc ghi lý do. Chỉ vai trò ADMIN của cửa hàng được duyệt, không suy ra từ việc có tất cả permission.
- Bộ lọc: Chờ duyệt, Đã duyệt, Từ chối, Đã rút, Cần đối soát lại, Tất cả. Chi tiết lưu người gửi, người xử lý, thời điểm, lý do, số liệu trước/sau duyệt.

## Số liệu và ca đã đóng

- Duyệt sửa: loại bỏ đóng góp cũ, cộng đóng góp mới vào tổng thu/chi, tính lại tiền dự kiến cuối ca. Ví dụ Thu 10.000 → Chi 15.000 làm tiền dự kiến giảm 25.000, không phải 5.000.
- Duyệt hủy: bỏ đóng góp của phiếu khỏi tổng ca, đánh dấu hủy; vẫn giữ bản ghi và lịch sử yêu cầu.
- Cho phép duyệt cả ca đã đóng/đã nhận tiền. **Không sửa tiền thực đếm, tiền Admin đã nhận, thông tin nhận tiền hoặc phiếu chốt ca ban đầu.** Chênh lệch hiện tại được tính theo tiền dự kiến mới.
- Ca đóng được đánh dấu **Cần đối soát lại**, có liên kết từ Quản lý / duyệt ca. Admin kiểm tra số liệu rồi ghi chú và chọn **Xác nhận đã đối soát lại ca**. Thao tác này ghi nhận đã xem xét toàn bộ điều chỉnh đã duyệt còn chờ đối soát của ca; không tự ghi thu/chi bù hoặc thay đổi số tiền.
- Duyệt thêm yêu cầu sau đó sẽ bật lại cờ đối soát. Bản chốt ca cũ là bằng chứng tại thời điểm chốt; xem số liệu điều chỉnh tại trang ca và lịch sử yêu cầu.
- Admin được duyệt điều chỉnh làm **tiền dự kiến âm**. Ví dụ ca đang có 30.000đ, sửa Thu 30.000đ thành Chi 35.000đ làm giảm 65.000đ, còn **-35.000đ**. Popup cảnh báo rõ trước/sau, nguyên nhân cần kiểm tra và giữ cảnh báo sau khi duyệt. Ca POS và trang quản lý ca cũng hiển thị cảnh báo âm. Đây là số dư theo dữ liệu, không tự kết luận tiền thực tế thiếu hay tự bù tiền.
- Chỉ bỏ giới hạn không âm trên số **dự kiến**. Tiền thực đếm và các khoản tiền gốc vẫn có ràng buộc; tạo khoản chi mới vẫn kiểm tra tiền dự kiến đủ chi. Hoàn hàng/hoàn cọc giữ kiểm tra quỹ hiện có.

## Tính nhất quán

- Giới hạn theo StoreId và người tạo; xác thực Admin phía server, chống CSRF.
- Khóa ca trong transaction, kiểm tra rowversion của yêu cầu/phiếu gốc; cập nhật phiếu, tổng ca và trạng thái yêu cầu trong một giao dịch.
- ClientRequestId và chỉ mục duy nhất ngăn tạo lặp. Duyệt lặp / duyệt đồng thời không áp dụng chênh lệch lần hai.
- Phiếu gốc đã thay đổi thì không duyệt yêu cầu cũ; từ chối và lập lại theo dữ liệu mới.
- Xác nhận đối soát dùng phiên bản ca để không vô tình xác nhận một điều chỉnh mới chưa xem.

## Cập nhật server

Có migration **20260930150000_AddPOSCashAdjustmentRequests**: thêm bảng yêu cầu và cờ đối soát trên POSShifts. Không sửa số tiền của ca/phiếu hiện có.

Bản sửa lỗi duyệt số dư âm bổ sung migration **20260930160000_AllowSignedPOSExpectedCash**, bỏ constraint `CK_POSShifts_ClosingCashExpected_NonNegative`. Cần publish lại Web + Migrator và chạy `--schema-only` kể cả đã chạy migration tạo bảng yêu cầu. Migration không sửa số tiền hoặc tự duyệt yêu cầu đang chờ. Rollback constraint cũ chỉ thực hiện được khi không còn số dự kiến âm; không tự sửa dữ liệu để ép rollback.

1. Publish **Web + Migrator** từ cùng phiên bản này; sao lưu database và dừng Web trước khi thay bản chạy.
2. Chép bản publish lên server, giữ cấu hình server (DefaultConnection và các cấu hình bảo vệ dữ liệu). Migrator phải trỏ đúng cùng database với Web; không chép đè cấu hình server bằng file cấu hình trống.
3. Tại thư mục Migrator trên server, chạy:

   ```powershell
   cd C:\GaoAppDeploy\ServerMoi\Migrator
   dotnet GaoApp.Migrator.dll --schema-only
   ```

4. Chỉ bật Web mới sau khi migration báo thành công. Nếu preflight báo không tương thích schema, giữ Web dừng và kiểm tra báo cáo; không bỏ qua kiểm tra hoặc tự xóa lịch sử migration.
5. Đăng nhập nhân viên thử gửi một yêu cầu, dùng Admin kiểm tra trang **Yêu cầu nghiệp vụ**.

## Kiểm thử trên dữ liệu thử riêng

```powershell
dotnet test GaoApp.Tests/GaoApp.Tests.csproj --settings GaoApp.Tests/test.local.runsettings --filter "FullyQualifiedName~POSCashAdjustmentSqlServerTests|FullyQualifiedName~PosShiftAdministrationSqlServerTests|FullyQualifiedName~Current_model_and_migration_snapshot_have_no_differences|FullyQualifiedName~DatabaseSchemaInspectionTests"
$env:GAOAPP_TEST_SQL_SERVER = '.\SQLEXPRESS'
dotnet run --project GaoApp.Tests.Browser/PosOffline.Browser.csproj -- --cash-adjustments
```

SQL kiểm tra phân quyền, tenant, pending, sửa loại/số tiền, hủy mềm, từ chối/rút, retry/concurrent approve, phiên bản cũ, CSRF, ca đóng và chứng từ cũ. Browser kiểm tra luồng thật nhân viên/Admin, popup, Esc và màn hình 390/768/1440px. Fixture tạo và xóa database riêng, không chạy trên database bán hàng.
