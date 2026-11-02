# UI-09 — Hoàn tất triển khai và kiểm tra tự động

Ngày 26/10/2026. Đã triển khai UI-09a/b/c/d và qua kiểm tra tự động; còn nghiệm thu điện thoại thật. Bước tiếp: UI-10a, đề nghị mua.

## Giao diện điện thoại

- UI-09a — /admin/deliveries: tìm đơn hoặc dùng QR/mã theo luồng hiện có; danh sách và phần đang soạn tách màn trong cùng workspace. Kho nguồn/chủ hàng gốc xuất hiện gần người nhận. Các dòng hàng, quy cách, lượng thực có, lý do thiếu, hàng thay thế và trạng thái đã lưu giữ nguyên. Nút lưu/gửi chốt ở sau các dòng, không che nội dung.
- UI-09b — /admin/deliveries/trips: danh sách, tạo/xếp chuyến và chi tiết rõ từng việc. Giữ người giao, thứ tự ghé, trạng thái, kho/chủ hàng theo từng đơn, lượng đã duyệt/đã xuất/đã nhận trả/đang giữ. Chưa có bằng chứng giữ hàng vẫn ghi Chưa xác nhận. Xác nhận vật lý, xuất phát, đổi người giao và thu hồi dùng đúng quyền/lệnh cũ.
- UI-09c — /admin/deliveries/trips/{tripId}/outcomes: chọn điểm giao riêng, bảng lượng giao/trả thành thẻ, đơn vị gốc rõ. Lý do, tiền người giao báo, tiền quầy thực nhận, tài khoản/phương thức, preview, bằng chứng và xác nhận cuối là nội dung và node gốc. Sau chốt có liên kết đúng bill đơn bán hiện hành.
- UI-09d: đã kiểm tra các giới hạn quyền/phiên/store, stale phiên bản/GET, hàng thiếu/thay thế, giao một phần/không giao, lost response/retry, đổi người giữ hàng, bill và trạng thái hoàn thành sau đủ điểm.

## Phạm vi và bảo toàn

Ba view chỉ thêm CSS/adapter mới; CSS chỉ áp dụng dưới 768px, input 16px/vùng chạm tối thiểu 44px. Adapter chỉ điều khiển phần đang xem, role/nhãn bảng và vị trí node nguồn readonly. Không clone field, phát lệnh, tính lượng/tiền, fetch/storage hoặc relay click/submit. Chuyển màn/resize giữ nguyên node, giá trị, disabled và draft; nguồn trả về marker khi desktop. Chọn lại mục hiện tại đợi owner render rồi chuyển chi tiết; lựa chọn bị owner từ chối không ép mở bản cũ.

Toàn bộ controller/service/repository/DTO, delivery.page.js/delivery.trip.js/delivery.outcomes.js, các CSS desktop và owner A5/POS bill giữ nguyên. Không đổi sidebar/layout/_POSNavTabs, quyền hoặc AdminMenuItems/cấu hình cửa hàng; không menu/seeder/migration/schema/API mới. Bốn probe cũ chỉ thêm hook rà mobile và guard transport test; toàn bộ assertions/lệnh tài chính gốc được đối chiếu giữ nguyên sau loại hook.

## Kết quả kiểm tra

- Release build: 0 cảnh báo/lỗi. 303/303 hồi quy .NET + 1/1 browser/SQL mới = **304/304**, không bỏ qua. Bao gồm picking, nguồn/tồn, custody/chuyến, outcome/handover, cost/ledger/rollback/concurrency/idempotency, quyền, completion và bill/CSP. **107/107 JS** qua.
- **26 nhóm browser/API/SQL**: 10 picking + 6 inventory + 6 trip + 4 handover. **10 trạng thái UI, 70 lượt phone**, gồm 320/360/390/430/600/767px và 390×440, không tràn ngang và field đủ cỡ. **20 đối chiếu desktop** toàn computed CSS/box ở 768/1440px giống khi bỏ stylesheet mới; không còn nav/label phone. **90 scan** không ID trùng. Bốn probe không lỗi JS; probe handover thu console CSP và xác nhận 0 lỗi.
- Soạn hàng: 18 intents thực, 21 revision/receipt/outbox, 3 planned holds đúng nguồn/người/thời gian. Claim cạnh tranh, thiếu/0 bắt buộc lý do, thay thế, duyệt/reopen/reassign, lost response, exact retry, delayed GET và stale QR đều qua. Không payment/đơn bán hoặc đổi tồn vật lý; bản duyệt 880 tách giá nguồn 830.
- Nguồn/tồn: 4 intents thực, đủ source/legal owner/warehouse, phân quyền giá vốn, tồn giữ/đã xuất/nhận trả/còn ngoài. Hủy và uncertain retry không thêm tiền/kho. PDF A5 giữ QR và nguồn; không in thiết bị.
- Chuyến: 9 root commands, 2 child xuất kho, 9 history/outbox đúng hợp đồng; xuất đúng 5 đơn vị và 51.000 giá trị. Bàn giao thực, thứ tự, bỏ đơn, bỏ chuyến nháp, đổi người giữ, server departure, replay và recall một đơn giữ nguyên nguồn/custody/đơn còn lại. Không phát sinh hoàn/bán/thanh toán sớm.
- Bàn giao mới: nhận thực giao 2,5/4 và trả 1,5, preview 50.000. Thiếu xác nhận vật lý không chốt. Double tap chậm chỉ một POST; server commit rồi mất response, reload không tự gửi; retry dùng đúng body/GUID/version cũ và chỉ một khoản tiền. View-only 403, thiếu CSRF 400, thiếu Delivery.View 403, store khác 404. Bill trỏ đúng /admin/pos/orders/{id}/print?autoPrint=false và GET thành công.
- Điểm thứ hai không giao, nhận về đủ 3, không tạo đơn bán hoặc thu tiền. SQL tổng trả 4,5, cost trả 46.500 và cost hàng bán 22.500; một sale/payment cash 50.000, hai counter confirmations, **0 movement xuất bán lần hai**. Chốt vào đúng ca/quầy hiện tại; source/dispatch/current cart/menu/store config giữ nguyên. Chuyến chỉ Completed sau khi hai điểm được chốt.
- **553 file bảo vệ** khớp snapshot. Baseline 560 bất biến: 496 không đổi, 64 đã đổi qua các bước, 0 mất; bước này đổi 3 view và thêm 2 asset ngoài baseline. Đã rà ảnh phone của soạn, tạo/chuyến nháp/xuất phát, nguồn/tồn, giao một phần, sau chốt, không giao và hoàn thành. Sau rà ảnh, vùng hành động phone được đặt sau các dòng để tránh che header/field; picking và trip đã chạy lại bản CSS cuối.

## Môi trường và còn lại

Chỉ Web/SQL GUID tạm, Chrome headless/profile mới; tắt launcher thường và chặn transport ngoài/thiết bị thật. Không dùng database cấu hình hoặc đóng/thay đổi cửa sổ Chrome đang mở. Các POST online/presence là heartbeat registry có sẵn, được harness ghi riêng khỏi lệnh nghiệp vụ; preview là tính đọc. Chẩn đoán các lần sửa harness (form ban đầu chưa nhập lượng, chờ đúng busy/uncertain trước reload và phân loại heartbeat) giữ tại .artifacts/ui-09. Không sửa service/quyền để làm test qua.

Còn Android/iOS thật, bàn phím/safe-area/touch/zoom/một tay và scanner/QR phần cứng. Bill/PDF là kiểm tra đường dẫn/render; in thật được nghiệm thu tại UI-22. Các nghiệm thu trước, gồm helper két ngoài UI-07, vẫn mở.

[Runtime](mobile-ui-20261025/evidence/ui-09/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-09/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-09/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-09/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-09-20261026; snapshot/log: .artifacts/ui-09.
