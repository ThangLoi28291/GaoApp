# UI-07c — Đối soát và quản lý duyệt ca trên điện thoại

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu điện thoại thật. Bước tiếp: UI-07d, dashboard POS riêng.

## Giao diện

- Đối soát: /admin/pos-shift/reconciliation. Header navy/blue theo mẫu duyệt; hai thẻ tiền dự kiến/thực đếm và thẻ chênh lệch riêng. Cách tính quỹ, bảng mệnh giá, các mục cần kiểm tra và giao dịch giữ số liệu gốc. Tab, tìm kiếm, lọc ngày/phạm vi, phân trang và liên kết điều chỉnh hiện hữu dùng lại. Bảng bằng chứng thành thẻ có nhãn lấy từ tiêu đề cột thật.
- Quản lý duyệt: /admin/pos-shift/manager-dashboard. Hàng đợi ca và ca lệch quỹ được đưa lên trước; bộ lọc và thống kê mở rộng khi cần. Không tự đổi trạng thái lọc hoặc mặc định chỉ hiện ca chờ. Thẻ ca vẫn đủ nhân viên, quầy, trạng thái, doanh thu, tiền mặt, bàn giao và lệch quỹ.
- Chi tiết ca: form nhận tiền ở đầu, đối soát giao dịch và thông tin bàn giao mở rộng. Header/footer cố định, body cuộn theo phần viewport còn nhìn thấy. Nút xác nhận chính và in/đóng phụ theo navy/blue; ô nhập ít nhất 16px, vùng chạm ít nhất 44px, có safe-area và reduced-motion. Giữ nguyên yêu cầu lý do khi chênh lệch và trạng thái khóa của owner gốc.

## Bảo toàn

Chỉ hai Razor view thêm asset và hai asset CSS/adapter mới. CSS giới hạn dưới 768px. Adapter dùng lại chính node, marker vị trí và native details; không clone tiền/field, không tính tiền, gọi API, lưu draft hoặc tạo command. Submit button gốc liên kết với form gốc bằng thuộc tính form trên phone; native validation và listener submit cũ tiếp tục xử lý. Khi về desktop, node, role, nhãn phụ và thuộc tính được hoàn trả.

Ba script nghiệp vụ đối soát/dashboard/detail và CSS desktop/inline giữ nguyên. Không sửa controller/service/DTO/quyền, cấu hình, sidebar/_POSNavTabs, seeder/migration hoặc owner in. Menu tiếp tục chỉ lấy AdminMenuItems đã cấu hình theo quyền/trạng thái/thứ tự. Người dùng có thể dùng các đường dẫn chức năng trên khi tự cấu hình menu. Màn yêu cầu điều chỉnh/duyệt yêu cầu không được mở rộng trong bước này.

## Kiểm tra thực hiện

- Release build: 0 lỗi, 0 cảnh báo. 48/48 .NET, 20/20 JavaScript qua.
- 5 nhóm browser với Razor/HTTP/SQL thật; 26 lượt phone gồm 320/360/390/430/600/767px, màn thấp 390×440, thống kê mở và keyboard visualViewport 420px mô phỏng. Không tràn ngang; footer/modal trong viewport; 35 scan không ID trùng, không lỗi JS/CSP.
- 9 đối chiếu toàn bộ computed property và box của workspace/modal ở 768/1024/1440px: stylesheet mới không làm thay đổi desktop/tablet. Không còn wrapper/label phone; node nhập và nút xác nhận gốc cùng giá trị vẫn đúng sau resize.
- Hàng đợi 25 ca, lọc chờ/đã nhận, lọc trạng thái, phân trang và trạng thái rỗng hoạt động. Thống kê và bảng phương thức của nhân viên vẫn đóng/mở theo owner gốc. Đối soát 26 đơn/26 phiếu qua hai trang, tìm theo mã, lọc bank/thu-chi, số tổng ca không đổi theo lọc và text chứng từ được escape. Response chi tiết cũ bị hủy không thay ca vừa chọn.
- Native validation thiếu lý do chênh lệch không gửi POST. Lỗi 409 giữ nguyên tiền/ghi chú. Trong request chậm, bấm lặp chỉ gửi một lần. Sau POST thành công, lỗi tải lại báo cáo vẫn khóa form/nút; reload cho thấy đúng số nhận và lý do. Ca đang mở hoặc đã nhận tiền không hiện nút duyệt; ghi đè khoản đã nhận bị 409 theo service gốc.
- SQL: dự kiến 226.000 đ, nhân viên đếm 225.000 đ, quản lý nhận 224.000 đ; actor/thời điểm và lý do “UI07c kiểm lại thiếu thêm 1.000” được lưu đúng. Không ghi đè tiền nhân viên. Fingerprint các đơn/payment/thu-chi/tồn/phiếu và khoản nhận của ca khác, menu/store/quầy/giá/tùy chọn khách/cửa hàng khác không đổi.
- Nhân viên không vào dashboard quản lý, không đọc ca người khác; thiếu Shift.View bị chặn, cửa hàng khác nhận 404. Giữ nguyên chính sách server.
- Probe quản lý ca gốc qua vòng đời tạo/in/sửa/in lại/nhận/chốt/duyệt/reload bằng SQL thật, in giả lập. Probe đối soát gốc với API giả qua scoped close draft, bộ lọc, khoản dư, cảnh báo, snapshot ca đóng và lỗi quyền; zero financial writes. Cả hai probe không sửa source trong UI-07c.
- 315 file bảo vệ khớp snapshot. Baseline 560 file giữ nguyên bản ghi gốc: 504 không đổi, 56 đã đổi, 0 mất; bước này đổi 2 view trong baseline và thêm 2 asset ngoài baseline.

## Phạm vi chạy và bằng chứng

Chỉ GUID LocalDB/Web tạm và Chrome headless/profile mới; tắt launcher thường. Không dùng database cấu hình, không đóng/thay đổi cửa sổ Chrome đang mở. Probe in dùng window.print giả và popup của chính nó; không gọi thiết bị/nhà băng thật. Chưa nghiệm thu Android/iOS thật, keyboard/safe-area/một tay hoặc in/két thật; các nghiệm thu trước vẫn mở.

[Runtime](mobile-ui-20261025/evidence/ui-07c/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-07c/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-07c/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-07c/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-07c-20261026; snapshot/log: .artifacts/ui-07c.
