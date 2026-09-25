# Quản lý ảnh sản phẩm và dọn ảnh không sử dụng

## Phạm vi

- Trang `/admin/media-library`, có nút mở từ danh sách Sản phẩm và định nghĩa menu Danh mục → Quản lý hình ảnh.
- Xem trước có kiểm tra cửa hàng/quyền; tìm theo tên, lọc trạng thái, phân trang 24 ảnh, liên kết về sản phẩm, thống kê số ảnh/dung lượng theo bản ghi.
- Xem thư viện dùng quyền `catalog.product.view`; hủy ảnh tạm, lên lịch/dọn thủ công cần thêm `catalog.product.delete`. Các POST có antiforgery.
- API JSON: `/admin/media-library/data`, `/{id}`, `/policy`; cùng quyền/cửa hàng với trang quản trị. Hợp đồng, endpoint thao tác và ví dụ gọi tại [API quản lý hình ảnh](../media-library-api.md).
- Giữ nguyên những thay đổi có sẵn trong working tree; không commit, deploy hoặc dọn dữ liệu thực của cửa hàng trong lần triển khai mã nguồn này.

## Chính sách mặc định

```json
{
  "MediaCleanup": {
    "Enabled": true,
    "TempLifetimeHours": 6,
    "UnusedRetentionDays": 7,
    "IntervalMinutes": 60,
    "BatchSize": 100
  }
}
```

Options có giá trị mặc định và validation, không cần sửa cấu hình để bật. Có thể override bằng cấu hình triển khai hoặc các biến môi trường `MediaCleanup__...`.

- Worker bắt đầu sau một phút, quét theo lô mỗi giờ khi ứng dụng đang chạy. Nếu IIS cho ứng dụng ngủ/dừng thì không có job ngoài tiến trình đánh thức nó; lần chạy lại tiếp tục dùng deadline trong database.
- Ảnh tải tạm hết hạn sau 6 giờ. Hủy upload làm mất hiệu lực token và đưa deadline về hiện tại; lượt quét sau xóa file. Việc đóng tab không cần gửi được yêu cầu hủy để ảnh hết hạn.
- Gỡ ảnh sản phẩm chỉ bỏ liên kết khi lưu. Worker bắt đầu **đủ 7 ngày kể từ khi phát hiện không dùng**, kể cả ảnh cũ đã tồn tại nhiều năm hoặc ảnh của sản phẩm bị xóa mềm.
- Ảnh của sản phẩm/biến thể chưa xóa được giữ, kể cả tạm ngừng bán. Kiểm tra cả biến thể còn trỏ tới ProductImage đã xóa mềm, URL trong nội dung sản phẩm và màn hình quảng cáo.
- Khi ảnh đang chờ được sử dụng lại, lượt kiểm tra hủy deadline.
- Không có nút bỏ qua thời gian chờ hoặc cưỡng chế xóa ảnh đang dùng.

## Tính nhất quán và dữ liệu cũ

- Không đổi schema. `MediaAssets.ExpireAtUtc` dùng làm deadline; `IsDeleted=true` cùng deadline là yêu cầu xóa file đang chờ/thử lại; `IsDeleted=true` và deadline null là đã hoàn tất. Giữ bản ghi để tra lịch sử.
- Commit dấu xóa vào DB **trước** thao tác file. Rowversion chặn biểu mẫu cũ khôi phục upload đã bị dọn. Giai đoạn xóa file mở transaction Serializable, khóa tác vụ SQL Server và kiểm tra lại các liên kết.
- Kiểm tra bản ghi dùng chung đường dẫn trên toàn bộ cửa hàng trước khi xóa file; không hiển thị thông tin cửa hàng khác. Các bản ghi dùng chung có thể phải chờ lượt sau để dọn đủ.
- Xóa cả bản lưu hiện tại và bản cũ trong `wwwroot/uploads/products` hoặc `_temp` nếu có. Chặn đường dẫn vượt storage root/junction. File mất sẵn được coi là đã dọn; root lưu trữ mất hoặc lỗi truy cập thì giữ yêu cầu để thử lại.
- Upload mới đăng ký MediaAsset trước khi ghi file. Mỗi upload có đường dẫn riêng `uploads/products/{storeId}/{yyyy/MM/dd}/{token}/{filename}`. Khi lưu sản phẩm chỉ cập nhật DB, không chuyển/ghi đè file; tránh file mất khi lưu DB thất bại và trùng tên ảnh.
- Token chỉ có hiệu lực khi file đã ghi xong. Upload bị gián đoạn vẫn có bản ghi hết hạn để dọn. Tạo/sửa sản phẩm được bọc transaction: nếu ảnh hết hạn hoặc commit ảnh thất bại, dữ liệu sản phẩm/biến thể/barcode trong lần lưu đó cũng rollback, tránh sản phẩm được tạo dở khi người dùng tải lại ảnh.
- Ảnh đã lưu trước đây giữ nguyên URL. Upload cũ **chưa lưu sản phẩm** còn ở `_temp` phải tải lại sau nâng cấp; token cũ bị từ chối khi commit và ảnh sẽ được dọn theo hạn cũ. Thư viện vẫn xem trước được ảnh tạm cũ qua endpoint có phân quyền.
- Chỉ dọn file được đăng ký trong MediaAssets thuộc vùng ảnh sản phẩm hoặc `_temp`. Không quét/xóa file rời không có bản ghi, file tự chép vào ổ đĩa, hóa đơn, hoặc toàn bộ thư mục dựa trên tuổi file. Những file rời cũ cần đối soát riêng vì không xác định chắc cửa hàng/chủ sở hữu chỉ từ đường dẫn.
- Dung lượng hiển thị được tính từ metadata, không phải đo dung lượng ổ đĩa; các bản ghi cũ trùng đường dẫn có thể làm tổng cao hơn thực tế. Trạng thái lượt quét gần nhất nằm trong bộ nhớ tiến trình, còn deadline và yêu cầu thử lại nằm trong DB.

## Triển khai

Build/publish bản mới và khởi động lại ứng dụng theo quy trình hiện có. Không cần migration schema. Menu được đồng bộ qua seeder chuẩn trong đợt chạy migrator/provisioning tiếp theo; nút từ Sản phẩm và URL trực tiếp dùng được ngay sau khi cập nhật ứng dụng.

Tắt worker bằng `MediaCleanup__Enabled=false` rồi khởi động lại nếu muốn tạm dừng dọn tự động; thao tác dọn thủ công vẫn áp dụng đầy đủ kiểm tra/thời gian chờ. File đã dọn chỉ có thể khôi phục từ backup hoặc tải lại.

## Kiểm tra

- `GaoApp.Tests/Media/MediaLibraryTests.cs`: hạn tạm, grace period, sản phẩm tạm ngừng/xóa, biến thể trỏ ảnh xóa mềm, hủy upload, tên trùng, ảnh dùng chung khác cửa hàng, lỗi file, bảo vệ vùng lưu trữ, phân trang/lô.
- `GaoApp.Tests/Media/MediaLibrarySqlServerTests.cs`: truy vấn SQL thật, rowversion của upload cũ, worker đồng thời và xóa bản lưu cũ.
- `GaoApp.Tests/Media/MediaCleanupWorkerTests.cs`: chạy worker qua nhiều lô và nhiều cửa hàng, kiểm tra tenant scope và chạy lại không xóa trùng.
- `GaoApp.Tests/Media/MediaLibraryHttpTests.cs`: upload/list/detail/policy/preview/cancel/cleanup qua ứng dụng thật, quyền và tenant/CSRF; token lấy trực tiếp từ API danh sách.
- Browser probe: `GaoApp.Tests.Browser/PosOffline.Browser.csproj --media-library`, dùng fixture LocalDB riêng và upload thử. Ảnh chụp/kết quả lưu trong `Logs/media-library-results/browser`.
- Build/test artifacts riêng: `Logs/media-library-build`; kết quả TRX: `Logs/media-library-results`. Không sử dụng database được cấu hình của cửa hàng.

Trạng thái bàn giao: implementation để review, không phải kết luận CI/commit đã được duyệt.

Kết quả local ngày 11/09/2026: build Release thành công; **85/85** kiểm thử được chọn đạt (media, SQL Server, HTTP/quyền/tenant/CSRF, storage, sản phẩm, menu và startup). Probe Chrome trên bản build cuối đạt ở 1440, 1024, 768 và 390 px; tìm kiếm/lọc/xem ảnh/hủy/dọn hoạt động, không tràn ngang hoặc lỗi JavaScript. Chỉ dùng database, tài khoản và ảnh thử của fixture.

Sau khi bổ sung API JSON: build Release và **28/28** kiểm thử Media/HostStorage đạt; kết quả `Logs/media-library-results/media-api.trx`. Không thay đổi giao diện trong đợt bổ sung API.

## Sửa timeout trên dữ liệu cửa hàng lớn

Người dùng báo `/admin/media-library/data` trả 500. Log `web-20260911.log`, TraceId `0HNOFCTGPQ5NC:00000004`, xác nhận SQL timeout 30 giây ngay ở phép đếm đầu tiên. Database local có 16.216 MediaAssets/ProductImages và 30.742 Products/ProductVariant. Bộ fixture nhỏ trước đó không phát hiện được chi phí của truy vấn tương quan `CASE`/`EXISTS` lặp qua toàn bộ danh mục.

- Đọc metadata ảnh trong phạm vi cửa hàng, xác định tập ID được sản phẩm/biến thể sử dụng bằng phép join/union một lần, rồi tính trạng thái/thống kê/lọc trên metadata. Chỉ tải tên và liên kết sản phẩm của trang 24 ảnh cần hiển thị.
- Chỉ đối soát URL trong nội dung/quảng cáo cho ảnh chưa có liên kết; giữ kiểm tra tham chiếu khác cửa hàng mà không lộ thông tin cửa hàng đó.
- Đổi truy vấn liên kết sản phẩm từ tích chéo có OR sang join/union. Kiểm tra trước xóa vẫn chạy lại trong transaction; ảnh đã có liên kết không phải quét tiếp nội dung/quảng cáo.
- Không tăng command timeout hoặc thay đổi schema. Chi phí bộ nhớ của danh sách tăng theo số metadata ảnh của cửa hàng; phép đối soát URL nội dung cũ chỉ chạy cho phần chưa có liên kết.

Đo chỉ đọc trên database local với bản sửa: danh sách 16.216 ảnh mất 351 ms; các lượt lọc 98–113 ms; chi tiết 17 ms. Đây là thời gian service/truy vấn, không bao gồm truyền HTTP hoặc tải file ảnh. Công cụ đo `Logs/media-library-read-probe` chỉ gọi List/Get và chặn mọi thao tác storage.

Thêm `MediaLibraryScaleSqlServerTests`: tạo riêng 32.000 sản phẩm/biến thể, 16.000 ảnh liên kết và các ảnh trạng thái khác trong LocalDB fixture; kiểm tra thống kê, lọc không phân biệt hoa thường, trang cuối, liên kết, ảnh HTML/quảng cáo và giữ ảnh đang dùng với giới hạn tổng 15 giây. Không sao chép dữ liệu cửa hàng vào fixture.

Kết quả cuối: build Release của Web thành công; **29/29** kiểm thử Media/HostStorage đạt, gồm HTTP và scale test (`Logs/media-library-results/media-timeout-final.trx`). Bản local mới đã khởi động tại `http://localhost:5100` bằng cấu hình Development hiện có. Trình duyệt kiểm tra riêng được chuyển đúng về đăng nhập vì không có phiên của Chrome; người dùng tải lại tab đã đăng nhập để xem JSON. Chưa triển khai lên server ngoài máy local.

## Khắc phục Visual Studio bị khóa DLL

Lượt chạy nền local phía trên (PID 1072) giữ DLL trong `GaoApp.Web/bin/Release/net8.0`, gây MSB3021/MSB3027 khi Visual Studio build lại và thông báo không kết nối được web server `https`. Đã xác minh và dừng đúng host này; build Release đạt 0 lỗi/0 cảnh báo. Kiểm tra khởi động Development trên hai cổng tạm: HTTP và HTTPS `/health/live` đều trả 200, giữ nguyên xác minh chứng chỉ. Tiến trình kiểm tra được dừng trong `finally`, không để lại host chạy nền.

Trạng thái sau khắc phục: trả quyền quản lý tiến trình cho Visual Studio; người dùng đóng thông báo cũ và chạy F5 với profile `https`. Không thay đổi launchSettings hoặc kho chứng chỉ. Các lần kiểm tra tiếp theo cần dùng output/cổng riêng và luôn dừng host sau kiểm tra để tránh khóa output mà Visual Studio sử dụng.
