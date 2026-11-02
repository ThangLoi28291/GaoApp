# B4 — Bảo vệ dữ liệu nhạy cảm khi đọc log hóa đơn lịch sử — 23/10/2026

Trạng thái: đã hoàn thành B4 theo phương án A trong source. Bộ hồi quy mở rộng đạt 533/533; bộ xác nhận trên binary cuối đạt 312/312, không lỗi hoặc bỏ qua. Build cuối 0 lỗi, 0 cảnh báo. Chưa triển khai lên ứng dụng đang vận hành.

## Nội dung sửa

Trước B4, `InvoiceIntegrationLogService` đưa nguyên request/response body, URL và lỗi của log cũ lên DTO. Bằng chứng ban đầu dùng credential giả lập; chưa xác nhận log vận hành từng chứa credential thật.

Đã thêm `InvoiceLogReadSanitizer`, áp dụng ở cả repository và service. Bốn phương thức đọc của repository (`GetByIdAsync`, `QueryAsync`, `GetLatestByInvoiceHeadAsync`, `GetLogsForDashboardAsync`) đều trả đối tượng đã che, được materialize bằng `AsNoTracking`. Vì vậy danh sách/chi tiết log, lịch sử trên payload hóa đơn và dashboard cùng đi qua lớp bảo vệ. Service kiểm tra lại khi tạo DTO.

| Trường | Quy tắc đọc |
|---|---|
| Request/response JSON | Tạo tóm tắt mới; chỉ giữ HTTP status, mã lỗi chuẩn, operation đã biết, UUID trace/request/correlation hợp lệ và metadata kích thước/loại file được kiểm tra |
| Trường nhạy cảm và đối tượng con chứa credential | Bỏ toàn bộ; không lấy metadata từ bên trong credential container |
| XML, form, text, binary/base64, JSON lỗi hoặc quá sâu/lớn | Thay bằng `[REDACTED]`; không trả nguyên nội dung khi không phân tích được |
| URL | Giữ scheme, host và port; bỏ user-info, toàn bộ path, query và fragment, kể cả route có token trong path |
| Mã lỗi | Giữ HTTP 100–599, mã số tối đa 8 chữ số, nhóm mã chuẩn và 72 mã Viettel công khai định nghĩa trong source; giá trị khác bị che. Không cho phép cả namespace theo prefix |
| Thông báo lỗi | Chỉ giữ dấu hiệu HTTP/timeout/loại exception ở phần chẩn đoán trước credential, JSON/XML hoặc URL nhúng; nội dung tự do còn lại bị che |

Không dùng cờ `redacted` trong dữ liệu cũ để bỏ qua kiểm tra. Bộ lọc có tính idempotent khi repository và service áp dụng liên tiếp. Body tối đa 65.536 ký tự và JSON depth 32 cho phép phân tích; giá trị vượt giới hạn bị che trực tiếp. Xử lý regex có timeout và trả kết quả đã che nếu hết thời gian.

Controller log có `Cache-Control: no-store` để tránh lưu phản hồi chứa dữ liệu chẩn đoán vào cache trình duyệt/proxy.

## Đóng đường dò qua tìm kiếm

Che HTML/DTO vẫn chưa đủ nếu `Keyword` tiếp tục lọc URL/lỗi thô trong SQL: số dòng trả về có thể giúp thử đoán chuỗi bị che. Đã bỏ tìm chuỗi tự do trên `RequestUrl` và `ErrorMessage`, đồng thời chỉ tìm `ErrorCode` bằng phép so sánh chính xác với mã được chính sách đọc cho phép hiển thị.

Tìm theo số hóa đơn, số hóa đơn nhà cung cấp và UUID vẫn hoạt động; bộ lọc ngày, hóa đơn/đơn hàng, loại thao tác, thành công/thất bại và phân trang được giữ. Gợi ý tìm kiếm trên giao diện đã đổi tương ứng.

Fixture B3 được đổi từ tìm nhãn giả lập trong lỗi thô sang số hóa đơn hiển thị và tách hóa đơn ranh giới ngày. Không giảm các kiểm tra 1.205 dòng, trần 100, cách ly cửa hàng, phân trang và đồng thời của B3.

## Lịch sử và kiểm tra GaoAppDb

Không cập nhật, xóa hoặc ghi đè log lịch sử. Bộ lọc chỉ sửa đối tượng đọc không tracking trong bộ nhớ. Kiểm thử so sánh SHA-256 của các trường gốc và RowVersion trước/sau tất cả đường đọc, kể cả gọi `SaveChangesAsync` trên context đọc.

Đã chạy thống kê chỉ đọc trên GaoAppDb, xét cả 5 trường request body, response body, URL, mã lỗi và thông báo lỗi: **23 bản ghi, 0 ứng viên chứa nhãn credential phổ biến**. Chỉ xuất số đếm, không xuất giá trị log hoặc credential.

Đây là phép dò theo nhãn (`password`, `token`, `secret`, authorization, cookie, API/private key, connection string...), không chứng minh mọi chuỗi lịch sử đều không phải bí mật. Chưa có bằng chứng credential vận hành bị lộ nên không tự xoay/thu hồi khóa hoặc sửa lịch sử. Nếu sau này xác nhận credential thật trong log, cần đánh giá phạm vi và xoay credential liên quan; che khi đọc không thu hồi được credential đã bị sao chép trước đó.

## Kiểm thử và phạm vi

Kết quả:

| Lượt kiểm tra | Kết quả | Phạm vi |
|---|---|---|
| Hồi quy mở rộng | 533/533 đạt | Gồm 24 ca B1, 84 ca quyền thương mại B2, 57 ca B3, các ca B4 trước tinh chỉnh cuối, audit, resource và 166 ca client HTTP mô phỏng |
| Xác nhận binary cuối | 312/312 đạt | 83 ca B4, 57 ca B3, 166 ca HTTP mô phỏng, 5 ca log worker SQL và 1 ca ngày Việt Nam |
| Build cuối | 0 lỗi, 0 cảnh báo | `build-final/`; mã lỗi công khai và xử lý lỗi nhúng đã được đưa vào binary này |

Lượt sau có lặp các ca B3/B4/client; không cộng hai lượt thành số ca khác nhau. Bản cuối bổ sung 7 trường hợp và giữ danh sách 72 mã lỗi công khai cùng quy tắc cắt dữ liệu nhúng; không sửa B1/B2 sau lượt hồi quy rộng. Hash các tệp B4 khớp bản build và bộ xác nhận cuối.

83 ca B4 gồm bộ lọc độc lập và Web/SQL Server thật trên CSDL GUID riêng. Có 18 biến thể log chứa credential giả lập: JSON lồng nhau/array, tên khóa/giá trị lạ, header, form, XML, text, private key, JSON lỗi/quá sâu/quá lớn và URL mang credential. Các kiểm tra bao gồm:

- Tài khoản chỉ có quyền xem tích hợp nhận HTML/DTO đã che; `raw=true`/`includeSecrets=true` không mở đường trả raw.
- Danh sách, chi tiết, dashboard và bốn phương thức repository không trả chuỗi bí mật giả lập.
- Trạng thái, HTTP, timeout, UUID trace, mã lỗi chuẩn và mã Viettel hợp lệ còn dùng được.
- Dò chuỗi bí mật/URL/lỗi bằng `Keyword` không làm thay đổi số kết quả; tra cứu định danh/mã lỗi công khai vẫn đúng.
- Tenant và quyền đọc vẫn chặn truy cập sai cửa hàng/thiếu quyền; phản hồi chi tiết có `no-store`.
- Bytes/RowVersion gốc không thay đổi; không có entity log được tracking sau đọc.
- Client nhà cung cấp mô phỏng tiếp tục giảm dữ liệu trước khi lưu log mới.

Bộ hồi quy còn chạy B1, B2, B3, audit bền vững, ngày Việt Nam, hạn mức tài nguyên và các client hóa đơn với HTTP mô phỏng. Không phát sinh giao dịch ngân hàng, hóa đơn hay lệnh in thật. Không thay đổi menu, quyền vận hành, schema hoặc migration.

Đã đối chiếu 3.127 file của danh mục source trước B4 và bảo toàn hash các thay đổi B1/B2. Các chỉnh sửa Assistant diễn ra song song được giữ nguyên và ghi riêng trong `verification.json`; không coi đó là thay đổi do B4 thực hiện. Tệp phân luồng chung được bổ sung controller Assistant trước bản build cuối; nhánh bảo vệ đọc audit/log hóa đơn giữ nguyên và đã được kiểm tra lại bằng toàn bộ B3 trên binary cuối.

Sau khi test kết thúc, 7 CSDL ban đầu đều ONLINE và danh sách trước/sau khớp nhau. Không còn CSDL GUID do B4 tạo. Một CSDL test của công việc song song xuất hiện tạm thời rồi được công việc đó dọn; không thực hiện lệnh xóa CSDL không thuộc B4.

Một kỳ vọng test ở lượt đầu chưa khớp quy tắc không lấy marker từ phần sau credential; đã chỉnh và giữ bằng chứng tại `attempt1/`. Các lượt trung gian không phải kết quả nghiệm thu bản cuối.

## Bằng chứng

Thư mục `.artifacts/b4-log-redaction-20261023/`:

- `before/`, `before-hashes.json`, `source-baseline.json`: source trước B4.
- `build-verified.log`, `build-final/`: build/binary cuối; `final-source-before-run.json`: hash các tệp B4 trước chạy cuối.
- `results/b4-regression.trx`, `regression.log`, `regression-filter.txt`: bộ hồi quy mở rộng 533 ca.
- `results/b4-final.trx`, `final.log`, `final-filter.txt`: bộ xác nhận 312 ca trên bản cuối.
- `historical-label-statistics.txt`: thống kê số đếm GaoAppDb; không có raw log.
- `databases-before.txt`, `databases-after.txt`: đối chiếu CSDL.
- `verification.json`, `verify.py`: kết quả xác minh, hash source/binary và phạm vi thay đổi.
