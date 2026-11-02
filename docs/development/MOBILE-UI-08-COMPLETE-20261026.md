# UI-08 — Hoàn tất triển khai và kiểm tra tự động

Ngày 26/10/2026. Toàn bộ UI-08a/b/c/d đã triển khai và qua kiểm tra tự động; còn nghiệm thu điện thoại thật. Bước tiếp theo: UI-09a, soạn hàng.

## Phần đã hoàn thành

- UI-08a: chạy lại toàn bộ yêu cầu thu/chi, thanh toán, cọc, duyệt/từ chối/rút và đối soát trên Razor/API/SQL thật. Giữ native form, node action, preview, busy, phạm vi và quyền từ bước trước.
- UI-08b: /admin/pos/returns/pending-restock. Phone có header navy, phiếu theo thẻ, đơn gốc, mã phiếu/dòng trả, ngày nhận và số đã hoàn. Mỗi dòng tách số đã nhận theo đơn vị bán, quy về đơn vị gốc, số đã nhập tại kho này, kho nhận và lượng chờ nhập. Giá vốn/cảnh báo giữ nguyên; nút gốc đặt sau nội dung của đúng phiếu, không clone hoặc relay lệnh.
- UI-08c: /admin/pos/legacy-returns và /admin/pos/legacy-returns/{id}. Danh sách tìm kiếm/phân trang GET thành thẻ; chi tiết tách metadata, các dòng và nguồn JSON có thể mở. Controller hiện tại chỉ cho tra cứu, chưa có thao tác sửa hoặc liên kết, nên giữ đúng giới hạn này.
- UI-08d: rà duplicate/retry, bị từ chối/rút, thiếu quyền, CSRF, store khác, giá vốn chưa đủ, chứng từ đổi lúc form mở và đóng/mở ca. Không thay controller hoặc quyền để làm test qua.

## Phạm vi thay đổi và bảo toàn

Ba view thêm import/hook cho CSS/adapter dưới 768px; riêng PendingRestock thêm facts d-md-none. Desktop từ 768px giữ bảng và nút gốc. Adapter chỉ đặt role và nhãn lấy từ thead rồi hoàn trả khi lên desktop; không API/storage, tính tiền hoặc chuyển tiếp click.

Projection đọc bổ sung snapshot số nhận/đơn vị bán/số gốc và tổng fragment đã nhập thực có CompletedAtUtc, InventoryTransactionId, theo dòng trả + kho. Truy vấn dùng AsNoTracking và tenant filter hiện hành; không cộng gộp các mặt hàng/đơn vị khác nhau. Số nhận là cả dòng, số đã nhập và số chờ là tại kho được nêu rõ. Không thay trường dữ liệu cũ, schema hoặc lệnh ghi. Toàn bộ phần ReadCost/Complete, khóa, transaction, idempotency, movement và audit khớp nguyên byte snapshot.

Script nghiệp vụ restock, return, request/cash/payment; SalesReturnService; controller/quyền; layout/sidebar/_POSNavTabs; cấu hình AdminMenuItems và cửa hàng đều giữ nguyên. Không menu/seeder/migration/API mới. Đường dẫn trên chỉ để truy cập chức năng có sẵn, người dùng tự cấu hình menu nếu cần.

## Kết quả

- Release build: 0 cảnh báo, 0 lỗi. 79/79 .NET qua, không bỏ qua; gồm ca POS, cash/payment/deposit adjustment, return/refund, giá vốn/phân bổ nguồn, invoice return sync, responsive/CSP/dashboard. 31/31 kiểm tra JS qua.
- 11 nhóm browser/API/SQL thật: 6 nhóm mới + chạy lại 5 nhóm requests. 55 lượt phone ở 320/360/390/430/600/767px và 390×440, không tràn ngang. 16 đối chiếu toàn bộ computed CSS/box ở 768/1440px đều giống khi tắt stylesheet mới. 72 scan không trùng ID; không lỗi JS/CSP.
- Cả ba probe gốc requests, returns, pending-restock đều qua và source giữ nguyên. Probe restock dùng HTML vừa render từ controller trong bộ SQL lần này; các ghi của probe gốc vẫn là API giả. Probe mới dùng lệnh thực và SQL kiểm chứng.
- Queue thực có phiếu 97 đơn vị đủ giá vốn và phiếu 1 đơn vị bị chặn. 409 giữ thẻ/nút để tải lại; thiếu CSRF trả 400, thiếu quyền trả 403, nguồn store khác bị chặn. Double tap chậm chỉ gửi một POST; gọi hoàn tất lại thành no-op, chỉ 1 audit nhập kho và không hoàn tiền thêm.
- Form return thực giữ số lượng/đơn vị/giá gốc. Double tap chỉ tạo một phiếu; một return đã được lệnh khác ghi lúc form đang mở làm lần gửi cũ thất bại. Đóng ca trong lúc form đang mở chặn thao tác; mở ca mới rồi retry ghi đúng ca mới. SQL cuối: tồn 96, 5 phiếu trả/5 khoản hoàn, 1 audit nhập kho, 1 fragment còn chờ; hoàn ca cũ 2.020, actual chốt 8.060; ca mới mở 500, hoàn 20. Query đọc đã nhập trả 97, store khác không thấy dữ liệu (0).
- 53 archive rows chỉ trong database test: 50/3 dòng ở hai trang, tìm %/_ theo ký tự thật, tìm kiếm mới về trang đầu, empty state, mã hàng dài và nội dung HTML/script nguồn được hiển thị dưới dạng văn bản. Không phát sinh thao tác sửa/liên kết hoặc tiền/kho từ màn tra cứu.
- Requests chạy lại đưa expected về 195.000 sau ba duyệt cash/payment/cọc; một rút/một từ chối và đối soát thành công. Actual 259.000, received 258.000, phiếu chốt gốc, số tiền/ngày payment, balance cọc, menu/cấu hình giữ nguyên.
- 410 file bảo vệ khớp snapshot. Hai before-image archive được khôi phục và xác nhận hash đúng frozen inventory, gồm BOM/line ending. Baseline 560 không sửa: 499 không đổi, 61 đã đổi qua các bước, 0 mất. Bước này đổi 3 view baseline và thêm 2 asset ngoài baseline. Đã rà ảnh phiên bản CSS cuối, gồm trạng thái đủ/chưa đủ giá vốn, sau nhập, archive, form và lỗi chứng từ/đóng ca.

## Môi trường và phần còn lại

Chỉ dùng Web/LocalDB GUID tạm và Chrome headless/profile mới; tắt launcher thường, chặn transport thiết bị/bank/ngoài. Không dùng database cấu hình hoặc đóng/thay đổi Chrome đang mở. Sửa lỗi URL và chờ điều hướng trong harness được giữ trong chẩn đoán .artifacts/ui-08; controller/quyền không đổi. Phiên bản cuối đều có result.completed, SQL PASS và source hash tương ứng.

Còn nghiệm thu Android/iOS thật: bàn phím, safe area, thao tác một tay. Các nghiệm thu trước, gồm helper két ngoài UI-07 và in/két thật, vẫn mở; không đánh dấu đã thử thiết bị.

[Runtime](mobile-ui-20261025/evidence/ui-08/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-08/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-08/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-08/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-08-20261026; snapshot/log: .artifacts/ui-08.
