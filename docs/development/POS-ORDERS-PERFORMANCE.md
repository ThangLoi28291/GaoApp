# Danh sách đơn POS: phân trang lịch sử lớn

Áp dụng cho `/admin/pos/orders-page` và API `GET /admin/pos/orders`.

## Bộ lọc mở rộng 30/09/2026

Nhân viên và máy tính tiền có autocomplete gọi `GET /admin/pos/orders/filter-options?kind=employee|terminal&term=...`, giới hạn 20 kết quả, chỉ trong cửa hàng hiện tại. Nhân viên cần cả tài khoản và phân công cửa hàng đang kích hoạt/chưa xóa; máy cần `IsActive` và trạng thái `Active`. Danh sách gợi ý hiện tên kèm tài khoản/mã máy, hỗ trợ chuột, ↑/↓, Enter và Esc. Khi chọn, lọc bằng `employeeId`/`terminalId` để phân biệt tên trùng; gõ sửa xóa ID đã chọn, xóa bộ lọc xóa cả ID. ID không thuộc cửa hàng hoặc đã bị tắt không trả đơn. Lịch sử không lọc vẫn giữ các đơn của nhân viên/máy cũ.

Tên khách tìm bằng SQL collation `Latin1_General_100_CI_AI`, chuẩn hóa Đ/đ thành D/d ở hai vế; không phân biệt hoa thường/dấu, ví dụ `nguyen` khớp Nguyễn/nguyễn/Nguyen/nguyen và `dang anh` khớp Đặng Ánh. Autocomplete nhân viên và máy cũng cho phép gõ không dấu. Không cần thay collation database hoặc thêm migration.

- Nhân viên: tìm một phần họ tên hoặc tài khoản của người nhận ca, đồng nhất với thu ngân trên bill.
- Khách hàng: tìm theo tên, số điện thoại hoặc mã khách. Máy tính tiền: tìm tên quầy hoặc mã máy.
- Thanh toán: tiền mặt, chuyển khoản, thẻ, ví điện tử, khác, nhiều hình thức; công nợ có lựa chọn tất cả đơn bán công nợ hoặc chỉ đơn đã chốt còn nợ.
- Các bộ lọc kết hợp bằng AND với ngày, trạng thái, mã đơn/ghi chú. Áp dụng tại SQL trước đếm và phân trang; giữ lại qua URL, tải lại và chuyển trang. Nút Xóa bộ lọc xóa cả các điều kiện mới.
- Hình thức thanh toán chỉ tính `OrderPayments` chưa xóa, đúng cửa hàng, số tiền dương, gồm cả khoản thu nợ đã ghi nhận. QR chưa thanh toán vẫn được mở để đối soát nhưng không được coi là đơn chuyển khoản. Đơn nhiều hình thức khớp từng hình thức có mặt, đồng thời hiện đủ nhãn. `IsCreditSale` xác định đơn bán công nợ; không suy diễn mọi đơn nháp chưa trả tiền là công nợ.
- Danh sách hiển thị khách/tel, nhân viên, máy và nhãn thanh toán. Lịch sử thiếu chi tiết phương thức không tự suy ra tiền mặt. Khách đã xóa không bị ghi nhầm thành khách lẻ.
- Truy vấn không có bộ lọc mới giữ nguyên đường đếm/phân trang đã tối ưu. Hai truy vấn bổ sung chỉ lấy thông tin danh tính và phương thức theo các ID trên trang; không tải dòng hàng, không truy vấn riêng từng đơn. Thông tin liên quan bị xóa không làm biến mất đơn khỏi trang.
- Không thêm migration trong lần mở rộng bộ lọc này. Publish lại Web; vẫn phải hoàn tất các migration của những tính năng khác đi cùng bản triển khai.

Kiểm thử: `PosOrderFiltersSqlServerTests` dùng Web/API và SQL thật trong database thử nghiệm; kiểm tra bộ lọc kết hợp, tổng/phân trang, tiền mặt/chuyển khoản hỗn hợp, QR chưa trả, khoản đã xóa/0 đồng, công nợ đã trả hết/còn nợ, dữ liệu khác cửa hàng và thông tin đã xóa. `PosOrderListSqlServerTests` kiểm tra lại lịch sử 413.913 đơn. Browser probe `--pos-orders-page` kiểm tra giao diện bảy kích thước, bộ lọc mới, URL/reset/chuyển trang, xem nhanh và xử lý lỗi.

## Thay đổi

- Dùng cột SQL tính sẵn `ListSortAtUtc = COALESCE(CompletedAtUtc, CreatedAtUtc)` và chỉ mục `IX_Orders_ListTimeline` theo cửa hàng, thời gian giảm dần, ID giảm dần. Giữ nguyên thứ tự cũ khi hai đơn cùng thời điểm.
- Tách truy vấn đếm thành hai nhóm không trùng nhau: đơn không phải nháp và nháp có nội dung, gộp bằng `UNION ALL` trong một lượt SQL. Không kiểm tra dòng hàng/thanh toán trên toàn bộ lịch sử để quyết định nháp rỗng. Truy vấn con giới hạn cửa hàng tường minh.
- Bổ sung `IX_Orders_ListCount` nhỏ gọn theo cửa hàng/trạng thái, gồm thời điểm tạo/chốt, chỉ chứa đơn chưa xóa. Đếm không còn phải đọc chỉ mục danh sách chứa ghi chú dài. Tổng số vẫn tính trực tiếp, không dùng bộ nhớ đệm có thể cũ sau khi chốt/hủy đơn.
- Chỉ đọc các cột cần cho danh sách; voucher, chuyển khoản và hậu mãi lấy theo các ID của trang hiện tại. Không tải toàn bộ entity đơn hàng hoặc chi tiết hàng hóa.
- Lọc cửa hàng tường minh, không trả lịch sử khi thiếu ngữ cảnh cửa hàng. Giữ nguyên quy tắc ngày, trạng thái, tìm mã/ghi chú và ẩn giỏ nháp rỗng.
- Giới hạn mỗi trang tối đa 200 đơn. Trang vượt phạm vi chỉ lấy tổng số, không thực hiện truy vấn trang/voucher.
- Giữ bảng, KPI và số trang hiện tại trong lúc chờ dữ liệu mới; hiện thông báo đang tải. Vẫn hủy yêu cầu cũ và ngăn phản hồi cũ ghi đè kết quả mới.
- Các nút đầu trang xuống dòng trên điện thoại để tránh tràn ngang.

## Cập nhật server

1. Publish lại **GaoApp.Migrator** và **GaoApp.Web**. Giữ cấu hình kết nối/môi trường của server.
2. Sao lưu database và chọn thời điểm ít giao dịch: thêm cột persisted và xây chỉ mục trên bảng Orders lớn có thể mất thời gian và khóa ghi tạm thời.
3. Trong thư mục Migrator đã publish, chạy:

   ```powershell
   dotnet GaoApp.Migrator.dll --schema-only
   ```

4. Xác nhận cả `20260928160000_OptimizePosOrdersTimeline` và `20260929090000_OptimizePosOrdersCount` hoàn tất, rồi triển khai/khởi động Web mới. Không chạy Web mới trước migration: truy vấn cần cột `ListSortAtUtc`. Lần nâng cấp 29/09 chỉ bổ sung chỉ mục đếm; không sửa/xóa dữ liệu đơn.
5. Mở trang đơn POS, kiểm tra phân trang, lọc ngày/trạng thái và tìm mã đơn.

Không cần cấu hình lại máy POS. Chưa có thao tác cập nhật database/server thật trong lần phát triển này.

Nếu Migrator báo `StructuralSchemaMismatch`, phải xử lý đúng báo cáo cấu trúc rồi chạy lại; không tự tạo chỉ mục thủ công hoặc sửa `__EFMigrationsHistory` để bỏ qua kiểm tra.

Để xác nhận trên server, chọn đúng database trong SSMS rồi chạy truy vấn chỉ đọc:

```sql
SELECT MigrationId FROM dbo.__EFMigrationsHistory
WHERE MigrationId IN ('20260928160000_OptimizePosOrdersTimeline', '20260929090000_OptimizePosOrdersCount');
SELECT name, is_disabled, filter_definition FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.Orders') AND name IN (N'IX_Orders_ListTimeline', N'IX_Orders_ListCount');
```

API trả `Server-Timing: orders;dur=...` (ms, phần service lấy danh sách và thông tin kèm theo). Xem trong Network của trình duyệt để phân biệt thời gian truy vấn với tải giao diện/kết nối. Yêu cầu mất từ 1 giây ghi log `Slow POS order list` cùng trang, kích thước trang, cửa hàng và TraceId; không ghi nội dung từ khóa. Thời gian này chưa bao gồm middleware/xác thực và truyền JSON. Lượt đầu sau khi khởi động Web còn có chi phí khởi tạo EF/JIT.

## Kiểm thử

- `PosOrderListSqlServerTests`: database SQL Server dùng riêng cho kiểm thử, 8.000 đơn; đối chiếu thứ tự với truy vấn cũ tại trang 1, 2, 200, 400; kiểm tra tenant, ngày, nháp rỗng, soft delete, voucher, hậu mãi, chuyển khoản, giới hạn trang và cập nhật cột tính toán.
- Bài kiểm thử thêm 413.913 đơn giả lập bằng SQL trong database dùng một lần, đối chiếu trang 1, 2, 1.000 và 20.696; kiểm tra đếm/lọc ngày/từ khóa và migration lên/xuống vẫn giữ nguyên dữ liệu. Không dùng thời gian làm điều kiện pass vì cấu hình máy và tải SQL khác nhau.
- `dotnet run --project GaoApp.Tests.Browser/PosOffline.Browser.csproj -- --pos-orders-page`: Razor/API đăng nhập thật trên database thử, các tình huống giao diện dùng dữ liệu mô phỏng; kiểm tra nhiều độ rộng, phản hồi chậm, phân trang, bộ lọc, lỗi/retry và phản hồi đến sai thứ tự.
- Thời gian đo trong bài SQL là truy vấn cũ so với truy vấn mới trên cùng database đã có chỉ mục, không phải số đo trước/sau trên server thật. Không dùng ngưỡng thời gian cố định làm điều kiện pass.

Vẫn dùng OFFSET và đếm tổng chính xác để giữ giao diện số trang. Tìm chuỗi nằm giữa mã/ghi chú và trang rất sâu vẫn phụ thuộc dung lượng dữ liệu; chưa có benchmark trực tiếp trên server của người dùng.

Đã đo chỉ đọc trên dữ liệu nguồn 389.683 đơn, 1.399.622 dòng hàng và 385.960 thanh toán (SQL Express tại máy phát triển). Chỉ thay truy vấn, chưa thêm chỉ mục đếm vào database nguồn: trang 2 giảm từ 573 ms xuống 218 ms; trang 1.000 từ 535 ms xuống 225 ms; trang 19.000 từ 711 ms xuống 276 ms. Đây là thời gian repository gồm đếm, lấy đầu đơn và voucher; chưa bao gồm HTTP/giao diện. Các số này không phải thời gian tải trang trên server.

Đo trên database kiểm thử 413.913 đơn giả lập, có ghi chú 450 ký tự/đơn, sau khi làm nóng truy vấn (log `Logs/orders-page-large-history-final.log`):

| Trang (20 đơn/trang) | Trước: đếm cũ + ID trang, chưa có chỉ mục đếm | Sau: đếm mới + đầu đơn + voucher, có chỉ mục đếm |
|---|---:|---:|
| 1 | 350,7 ms | 87,1 ms |
| 2 | 341,7 ms | 81,0 ms |
| 1.000 | 354,2 ms | 92,7 ms |
| 20.696 | 671,8 ms | 384,7 ms |

Dữ liệu giả lập không đại diện đầy đủ phân bố dòng hàng/thanh toán và tải đồng thời trên server. Bộ kiểm thử 8.000 đơn riêng kiểm tra voucher, hoàn trả, chuyển khoản và nhiều cửa hàng. Các phép đo không bao gồm middleware, mạng, dựng giao diện hoặc khởi tạo ứng dụng lần đầu.
