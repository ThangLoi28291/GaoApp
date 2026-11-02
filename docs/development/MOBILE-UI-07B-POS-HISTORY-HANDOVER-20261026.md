# UI-07b — Lịch sử ca và phiếu bàn giao trên điện thoại

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu thiết bị thật. Bước tiếp UI-07c: đối soát và quản lý duyệt ca.

## Giao diện và phạm vi

- Lịch sử ca: /admin/pos-shift/history. Danh sách dạng thẻ; bộ lọc trạng thái/ngày và các nút ngày nhanh trong disclosure. Chạm/Enter vào ca mở chi tiết riêng, có nút quay lại giữ vị trí danh sách. Tổng quan, tài chính, ghi chú, đơn trong ca, thu/chi, in báo cáo và CSV dùng đúng control/owner cũ.
- Phiếu: /admin/pos-shift/handover-slips, giữ nguyên cửa hàng/quyền quản trị cửa hàng hiện hữu. Thẻ có mã/barcode, trạng thái, kho/quầy, người nhận, tổng tiền/ngày tạo và các tác vụ theo trạng thái. Thêm bộ lọc quầy phone bằng assignments và terminalId đã có trong API; bỏ giá trị lọc này khi trở lại desktop để không giữ một điều kiện ẩn.
- Tạo/sửa phiếu một cột, kho/quầy/tổng tiền/bảng mệnh giá/ghi chú gốc. Header/footer cố định, body cuộn, ô nhập không bị tràn ngang; chi tiết phiếu và các bảng đơn/thu chi trong modal vừa phone. Màu navy/blue theo mẫu duyệt; sửa độ tương phản ngày ca sau rà ảnh. Motion giữ Bootstrap hiện hữu, tắt chuyển động modal khi prefers-reduced-motion.

Màn lịch sử là “ca của tôi”: server luôn lấy currentUserId, bỏ UserId do client gửi. Vì vậy không mở bộ lọc thu ngân khác hoặc quyền quản lý trong bước này. Lọc thời gian/trạng thái giữ owner cũ; lọc quầy áp dụng cho phiếu qua API sẵn có. Phần đối soát/duyệt và dashboard quản lý tiếp ở UI-07c/d.

Production thay 2 view (import asset và một select phone mặc định hidden), thêm CSS/adapter phone và mở rộng phần lookup/query phone trong script handover. Script history giữ nguyên; các hàm handover tạo/sửa/xem/in/hủy/bind giữ nguyên từng nội dung. Adapter không gọi API hoặc command/storage, không clone field; native details và vị trí node được trả lại desktop. Không đổi controller/service/DTO/permission/menu/seeder/migration, CSS desktop, sidebar/_POSNavTabs hay mẫu/luồng in. Không tự tạo menu; dùng các đường dẫn chức năng hiện hữu khi người dùng cần cấu hình.

## Kiểm tra

- Build Release 0 lỗi/cảnh báo; 48/48 .NET và 20/20 JS qua.
- 7 nhóm browser với Razor/HTTP/SQL thật. 30 lượt phone gồm 320/360/390/430/600/767px, màn thấp 390×440, bộ lọc mở và mô phỏng viewport keyboard 420px. 38 scan toàn document không ID trùng, không lỗi JS/CSP.
- 7 đối chiếu toàn bộ computed property/box của workspace/modal ở desktop/tablet 768/1024/1440px: các stylesheet mới không thay đổi giao diện; wrapper và nút phone đã được gỡ khi về desktop. Resize giữ đúng node ô số tờ và giá trị đang nhập.
- 25 ca của admin trên nhiều trang, ca nhân viên khác bị loại ngay cả khi truyền UserId khác. Lọc trạng thái/ngày, chọn 10 dòng/phân trang server, modal 8 dòng/trang, CSV tải thực và đường in bị chặn popup vẫn giữ trạng thái. Response summary cũ đến muộn không thay ca mới đang chọn; owner abort/sequence không bị sửa.
- Phiếu nhiều trang, trạng thái rỗng, lọc quầy có terminalId thật trong URL; desktop trở lại toàn bộ danh sách. Lỗi lưu giữ form/số tờ; bấm lặp trong lúc lưu chậm vẫn chỉ tạo một phiếu thành công. Xem/in chi tiết dùng URL gốc và không gửi thêm tiền.
- SQL: một phiếu tạo 300.000, sửa thành 350.000 với rowVersion; barcode cũ thay đổi và có cảnh báo in lại. Gửi rowVersion cũ bị 409. Nhân viên thực quét mã mới qua trang nhận ca cũ, nhận đúng 350.000, chỉ một ca; mở lại bị 400/POS_SHIFT_ALREADY_OPEN theo hợp đồng gốc. Phiếu Used không còn nút Sửa/Hủy. Một phiếu khác 100.000 được hủy kèm lý do chính xác.
- Denomination phiếu đã nhận: 200.000×1 + 50.000×3. Fingerprint ca/đơn/thu chi/phiếu mẫu ban đầu, menu/store/quầy/giá/tùy chọn khách/store khác không đổi. Không tạo payment hoặc thay tồn kho.
- Trang phiếu của nhân viên thường redirect đúng access-denied; API phiếu/assignments trả 403. Thiếu quyền không vào history; cửa hàng khác không đọc phiếu (404). Không thay chính sách để làm test qua.
- Probe quản lý ca gốc qua luồng tạo/in/sửa/in lại → bác barcode đã tải cũ → nhận đúng nhân viên → đóng → in chốt → quản lý xác nhận tiền và reload. Probe chỉ được cập nhật đoạn đầu đang đòi 4 nhóm menu cố định thành kỳ vọng không có POS menu cấu hình của fixture; toàn bộ vòng đời tài chính sau đó giữ nguyên. Không thêm menu để phục vụ probe.
- 305 file bảo vệ khớp snapshot. Baseline 560 file bất biến: 506 không đổi, 54 đã đổi, 0 mất; bước này thay 2 view và 1 asset trong baseline, thêm 2 asset mới ngoài baseline.

## Giới hạn và bằng chứng

Chỉ LocalDB GUID/Web dùng một lần và Chrome headless/profile riêng; tắt launcher thường, không chạm database cấu hình hoặc cửa sổ Chrome đang mở. Probe in dùng window.print giả và đóng riêng popup của nó. Chưa thử in/két/điện thoại Android/iOS thật; keyboard/safe-area/một tay vẫn chờ thiết bị. Các nghiệm thu trước vẫn mở.

Các lần đầu của probe mới đã sửa kỳ vọng theo hợp đồng gốc: modal 8 dòng/trang, nhãn ShiftCode, CSRF token cho lệnh trực tiếp, đã mở ca trả 400/errorCode và HTML từ chối quyền redirect 302 (API 403). Log trước sửa được giữ trong .artifacts/ui-07b; final run và SQL đều qua. Không đổi server để khớp kỳ vọng sai của probe.

[Runtime](mobile-ui-20261025/evidence/ui-07b/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-07b/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-07b/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-07b/baseline-delta.json). Ảnh/CSV/SQL/TRX: TestResults/mobile-ui-07b-20261026; snapshot/log: .artifacts/ui-07b.
