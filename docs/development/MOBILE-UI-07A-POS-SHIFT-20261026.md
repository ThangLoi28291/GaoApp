# UI-07a — Ca POS trên điện thoại

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu thiết bị thật. URL hiện hữu: /admin/pos-shift. Bước tiếp: UI-07b, lịch sử ca và phiếu bàn giao.

## Phạm vi

Nâng trang ca hiện tại, trạng thái chưa mở/đang mở, xác nhận két, mở/nhận/đóng ca, thu/chi trong ca và nháp kiểm đếm. Màu navy/blue theo chuẩn phone đã duyệt; thông tin cửa hàng/kho mở rộng bằng details, trạng thái và tác vụ ưu tiên trên thẻ ca. Bảng thu/chi trong ca thành thẻ; không đổi màn lịch sử ca riêng của UI-07b.

Mở/đóng ca một cột, field tiền lớn, mệnh giá vừa chiều ngang, nút xác nhận ở footer và body cuộn theo phần màn hình còn thấy. Giữ nút tăng/giảm, tổng theo mệnh giá, ô tổng tiền trực tiếp và ghi chú gốc. Phone dùng bàn phím thiết bị, ẩn keypad desktop trùng chức năng. Đóng ca hiển thị riêng tiền dự kiến, thực đếm và thiếu/thừa. Tham chiếu phiếu, kho, nhận ca từ POS/quay lại đúng route, xác nhận két/lệch và nháp đối soát giữ đường chạy gốc.

Production chỉ đổi POSShiftPage/Index để thêm 2 import, thêm pos-shift.mobile.css và pos.shift.mobile.js. CSS giới hạn dưới 768px. Adapter di chuyển chính node gốc, giữ listener/giá trị, trả bằng marker khi về desktop; không fetch, storage, submit, clone hay phép tính tiền mới. Giữ nguyên pos.shift.page.js, close-draft, CSS desktop, layout, _POSNavTabs, printing, controller/service/DTO/menu/quyền/cấu hình/migration. Không tạo menu; người dùng tự cấu hình đường dẫn hiện hữu khi cần.

## Kiểm tra và đánh giá

- Build Release: 0 lỗi, 0 cảnh báo. 48/48 hồi quy .NET và 20/20 JS qua; các probe thu/chi/két, đối soát và nhận ca gốc qua.
- 6 nhóm browser trên Razor/HTTP/SQL thật, 21 lượt phone (320/360/390/430/600/767px, màn thấp và mô phỏng keyboard), 33 lần scan không ID trùng. Không lỗi JavaScript/CSP.
- 10 đối chiếu mọi computed property và box desktop/tablet 768/1024/1440px ở các trạng thái chưa có ca, mở, đang hoạt động và đóng: stylesheet mới không làm thay đổi hình học/style. Đổi kích thước giữ chính ô nhập/số tờ đang nhập.
- Lỗi validation giữ form và số kiểm đếm; bấm lặp lúc mở ca chậm mạng vẫn chỉ một lần mở thành công. Thu/chi gọi owner hiện hữu. Mở màn nhập hay resize không tự gửi lệnh.
- Nháp thực đếm 225.000, note và số tờ qua màn đối soát rồi trở về đúng ca giữ nguyên. Hủy xác nhận thiếu quỹ không đóng; xác nhận sau đó đóng một lần và xóa nháp đúng scope.
- SQL: mở 200.000, thu 50.000, chi 20.000; dự kiến 230.000, thực đếm 225.000, lệch -5.000. Đúng một ca Closed, một phiếu đóng, hai phiếu thu/chi; ba mệnh giá đóng tổng 225.000. Không payment hoặc thay đổi tồn. Fingerprint menu/store/quầy/giá/tùy chọn khách/store khác không đổi.
- Tài khoản chỉ xem bị server chặn mở/đóng/két/thu chi; cửa hàng khác không đọc được ca. Không sửa các kiểm tra quyền để phục vụ UI.
- 298 file bảo vệ khớp snapshot. Baseline 560 file giữ nguyên: 509 không đổi, 51 đã thay đổi, 0 mất; bước này thêm một view đã đổi và hai asset mới ngoài baseline.
- Đã rà ảnh chưa mở ca, nhận ca, nhập tiền/mệnh giá, ca hoạt động, lịch sử thu/chi, kiểm đếm đóng, xác nhận thiếu quỹ, keyboard mô phỏng và desktop. Đã sửa xung đột margin/viewport CSS phát hiện khi chạy test; final run qua.

## Giới hạn và bằng chứng

Chỉ dùng LocalDB GUID/Web tạm và Chrome headless/profile riêng; launcher thường tắt, cửa sổ Chrome người dùng giữ nguyên. Không chạm database cấu hình; không in hoặc mở két vật lý. Phone keyboard là mô phỏng viewport, còn Android/iOS/safe-area/một tay và máy in/két thật. Probe pos-drawer-atomic cần server.py do người dùng cung cấp ngoài repo; đường dẫn cũ hiện không có, nên không khẳng định đã chạy lại. Owner in/két production giữ nguyên, probe transport giả và hồi quy SQL quyền két đã qua.

[Runtime](mobile-ui-20261025/evidence/ui-07a/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-07a/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-07a/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-07a/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-07a-20261026; snapshot/log, kể cả lượt khởi động timeout và CSS trước sửa: .artifacts/ui-07a.
