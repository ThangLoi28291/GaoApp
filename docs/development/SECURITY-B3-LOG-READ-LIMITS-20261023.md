# B3 — Giới hạn tài nguyên khi tra cứu log — 23/10/2026

Trạng thái: đã hoàn thành B3 theo phương án A trong source. Bộ hồi quy đạt 291/291; sau thay đổi đồng thời ở phân luồng Assistant, build lại và kiểm tra bổ sung đạt 73/73. Các tệp B3 và tệp phân luồng hạn mức chung khớp hash bản đã kiểm thử. Chưa triển khai lên ứng dụng đang vận hành.

## Phạm vi và hành vi sau sửa

B3 ban đầu cho phép tài khoản có quyền xem log gửi `pageSize=2147483647` và nhận toàn bộ 331 dòng trong một phản hồi ở CSDL test. Cả nhật ký thao tác và danh sách log tích hợp hóa đơn đều thiếu trần phân trang.

Đã thêm `LogReadLimits` và quy tắc chuẩn hóa/kiểm tra dùng chung. Controller, service và repository đều áp dụng quy tắc, bao gồm trường hợp gọi trực tiếp tầng đọc mà không đi qua HTTP.

| Đầu vào | Xử lý |
|---|---|
| Bỏ trống hoặc `PageSize <= 0` | Mặc định 20 dòng |
| `PageSize > 100` | Đưa về 100; kết quả và liên kết phân trang dùng kích thước thực tế |
| `Page <= 0` | Đưa về trang 1 |
| Offset vượt `int.MaxValue` | Lỗi kiểm tra đầu vào, HTTP 400; tính bằng `long` trước khi chuyển sang `int` |
| Offset hợp lệ nhưng vượt tổng số dòng | Trả trang rỗng sau truy vấn đếm; không thực hiện truy vấn lấy trang xa |
| Từ khóa dài hơn 200 ký tự | Lỗi kiểm tra đầu vào |
| Tên/mã đối tượng audit vượt 150/100 ký tự | Lỗi kiểm tra đầu vào, tương ứng độ dài trong mô hình dữ liệu |
| Ngày bắt đầu sau ngày kết thúc | Lỗi kiểm tra đầu vào |
| Ngày hóa đơn không thể chuyển sang khoảng UTC hợp lệ | Lỗi kiểm tra đầu vào, tránh tràn khi trừ UTC+07 hoặc cộng ngày kết thúc |
| Kiểu số/ngày không thể bind từ query string | HTTP 400 trước khi gọi service |

Khoảng ngày hóa đơn vẫn tính theo ngày Việt Nam, bắt đầu bao gồm và kết thúc loại trừ ở đầu ngày tiếp theo. Bộ lọc audit vẫn dùng mốc UTC. Không đặt thêm giới hạn số ngày lịch sử được phép tra cứu. MVC coi chuỗi chỉ chứa khoảng trắng là không có bộ lọc; chuỗi có nội dung vẫn được kiểm tra chiều dài đầy đủ.

Audit sắp xếp thời điểm giảm dần rồi ID giảm dần, tránh thứ tự tùy ý khi nhiều bản ghi cùng thời điểm. Log hóa đơn giữ thứ tự thời điểm và ID giảm dần hiện có. `GetLatestByInvoiceHeadAsync` cũng có trần 100, giữ mặc định 20 đang được màn hình payload hóa đơn sử dụng. Truy vấn dashboard vốn có trần 500 giữ nguyên.

## Chống gọi dồn

Bốn thao tác được đưa vào nhóm `HeavyIngress` / `HeavyRead` hiện có:

- `GET /admin/api/audit-logs` và `GET /admin/api/audit-logs/{id}`.
- `GET /Admin/InvoiceIntegrationLog/Index` và `GET /Admin/InvoiceIntegrationLog/Detail/{id}`.

Đường dẫn chỉ để tham chiếu; không thêm hoặc sửa menu.

| Phạm vi HeavyRead mặc định | Yêu cầu trong 60 giây | Số yêu cầu đồng thời |
|---|---:|---:|
| Tài khoản đã xác thực | 120 | 4 |
| Cửa hàng | 600 | 16 |
| Tiến trình Web | 2.400 | 32 |

Đây là ngân sách dùng chung với các thao tác đọc nặng đã có, bao gồm báo cáo; không phải hạn mức riêng bổ sung cho từng màn hình. Khóa tài khoản/cửa hàng lấy từ danh tính và tenant đã xác thực. Các giá trị trên là mặc định của source; cấu hình `Security:ResourceProtection` có thể điều chỉnh theo cơ chế hiện có.

Vượt tần suất hoặc số yêu cầu đồng thời trả 429; hết dung lượng bảng quản lý hạn mức trả 503. Phản hồi có `Retry-After`, mã `resource_busy`, thông báo thử lại và `Cache-Control: no-store` theo cơ chế sẵn có.

Các thao tác ghi log, audit outbox, callback ngân hàng, phát hành hóa đơn và dọn log không được bổ sung vào quy tắc đọc này. Không giảm số log được lưu. Ví dụ có 1.000 đơn hoặc callback trong ngày vẫn ghi nhận theo luồng hiện có; người dùng đọc lịch sử qua nhiều trang, mỗi trang tối đa 100 dòng.

## Kiểm thử

Kết quả nghiệm thu:

| Lượt | Kết quả | Phạm vi |
|---|---|---|
| Hồi quy mở rộng | 291/291 đạt; 0 lỗi, 0 bỏ qua | 57 ca B3 mới; 24 ca B1; 84 ca quyền thương mại B2; các ca thư viện hóa đơn, audit, worker, hạn mức và ngày Việt Nam liên quan |
| Xác nhận sau cập nhật phân luồng Assistant | 73/73 đạt; 0 lỗi, 0 bỏ qua | Chạy lại 57 ca B3, 9 ca hạn mức, 6 ca HTTP hạn mức và 1 ca BFF Assistant |
| Hai bản build tương ứng | 0 lỗi, 0 cảnh báo | SDK/runtime portable hiện có; không cập nhật package hoặc schema |

73 là lượt xác nhận bổ sung, có lặp lại các ca B3; không cộng hai lượt để trình bày thành số ca khác nhau. Thời gian VSTest báo lần lượt 12 phút 4 giây và 4 phút 14 giây.

Các ca B3 dùng Web thật, SQL Server thật và CSDL GUID riêng do fixture tạo/dọn. Mỗi loại có 1.205 log chính của cửa hàng, dữ liệu cửa hàng khác, bản ghi đã xóa và dữ liệu ở ranh giới ngày.

- HTTP với kích thước trang cực lớn chỉ trả 100 dòng; metadata và số trang phản ánh trần thực tế.
- Duyệt 13 trang lấy đủ 1.205 dòng mỗi loại, đúng thứ tự, không thiếu/trùng khi thời điểm bằng nhau.
- Gọi trực tiếp repository/service vẫn bị giới hạn; đầu vào gây tràn số hoặc ngày không hợp lệ bị từ chối.
- Các bộ lọc, ranh giới ngày UTC+07, quyền xem và cách ly cửa hàng được kiểm tra lại.
- Cả bốn đường đọc dùng chung ngân sách tài khoản; tài khoản khác vẫn đọc được; POS vẫn tạo giỏ hàng được khi tài khoản đã hết ngân sách đọc.
- Giữ bốn truy vấn đọc thật bằng khóa SQL trong CSDL test, xác nhận chuỗi chờ; 12 yêu cầu đọc gọi dồn tiếp theo bị chặn. POS vẫn hoạt động và đọc log phục hồi sau khi hoàn tác khóa. Phép thử này chạy cho từng loại log. Cột dùng để giữ khóa chỉ tồn tại trong transaction test và bị rollback.
- Chạy hồi quy B1, B2, audit bền vững, log worker hóa đơn, chuyển đổi ngày Việt Nam, cơ chế hạn mức và callback mô phỏng.

Hai lượt chuẩn bị test đầu có sai lệch do `AppDbContext` khởi tạo dòng mới với `IsDeleted=false`, MVC chuyển khoảng trắng thành bộ lọc trống và cách đếm chờ khóa SQL chưa bao gồm toàn bộ chuỗi chờ. Đã điều chỉnh bộ test theo hành vi thực tế; giữ kết quả các lượt này trong `attempt1/`, `attempt2/` để đối chiếu. Kết quả nghiệm thu sử dụng lần chạy đầy đủ sau cùng.

## Dữ liệu vận hành và giới hạn kết luận

Không ghi vào `GaoAppDb`, không thay đổi schema, migration, phân quyền hay menu vận hành. Không gọi ngân hàng, hóa đơn điện tử hoặc máy in thật. Các thay đổi B1 và B2 được đối chiếu hash để bảo toàn.

Đối chiếu danh mục 3.730 file trước B3 phát hiện các chỉnh sửa Assistant diễn ra đồng thời, gồm thêm `AssistantSearch`/`AssistantSearchAdmin` vào phân luồng ingress. Đã giữ nguyên chúng, chụp 3.739 file làm ngữ cảnh build tiếp theo và thực hiện lượt 73 kiểm thử nêu trên. Quy tắc B3 vẫn tồn tại nguyên vẹn trong tệp phân luồng chung.

Trong lượt xác nhận bổ sung, công việc Assistant tiếp tục sửa riêng các tệp tìm kiếm, giao diện và thêm test Assistant. Các thay đổi mới này được bảo toàn, ghi hash tại `laterUnrelatedAssistantEditsOutsideTestedBuild` trong `verification.json`, và **không nằm trong binary đã kiểm thử**. Kết luận nghiệm thu áp dụng cho B3 và tệp phân luồng chung đã đối chiếu hash; không khẳng định toàn bộ phần Assistant đang sửa đã được nghiệm thu. Không ghi đè source ngoài B3 để làm cho bản làm việc khớp binary cũ.

Tại thời điểm dọn xong lượt test B3 cuối, danh sách CSDL trước/sau giống nhau: 7 CSDL ban đầu đều ONLINE, không còn CSDL GUID do lượt B3 tạo. Mọi transaction giữ khóa/cột test đã rollback. Không đụng tới các CSDL test có sẵn từ trước.

Cơ chế hạn mức hiện có áp dụng theo từng tiến trình; triển khai nhiều instance muốn ngân sách tổng cần gateway/bộ đếm dùng chung. Phân trang và hạn mức giúp khống chế kích thước và số truy vấn đồng thời; truy vấn đếm/tìm chuỗi vẫn có thể quét nhiều dữ liệu. Kết quả thử nghiệm trên dữ liệu tổng hợp không khẳng định hiệu năng ở mọi quy mô triển khai.

B4 về che thông tin nhạy cảm khi đọc log cũ chưa thuộc thay đổi này.

## Bằng chứng

Thư mục: `.artifacts/b3-log-read-limits-20261023/`.

- `before/`, `before-hashes.json`, `source-baseline.json`: source trước B3 và danh mục hash kế thừa B1/B2.
- `build-regression.log`, `build-verified.log`: hai bản build tương ứng hai lượt nghiệm thu.
- `regression-source-before-run.json`, `current-source-before-build.json`, `current-source-before-run.json`: hash source và ngữ cảnh hai lượt.
- `results/b3-regression.trx`, `regression.log`, `regression-filter.txt`: kết quả 291 kiểm thử.
- `results/b3-current.trx`, `current.log`, `current-filter.txt`: kết quả 73 kiểm thử bổ sung.
- `load/audit.json`, `load/invoice.json`: kết quả kiểm thử đồng thời qua SQL và HTTP.
- `databases-before.txt`, `databases-after.txt`: đối chiếu CSDL trước/sau.
- `verification.json`, `verify-final.py`: tổng hợp nghiệm thu, hash source/binary, phạm vi thay đổi song song và trình xác minh cuối.
