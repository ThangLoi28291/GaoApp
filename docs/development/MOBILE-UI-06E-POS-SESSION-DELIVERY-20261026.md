# UI-06e — Phiên làm việc, offline và giao hàng POS trên điện thoại

Ngày 26/10/2026. Đã triển khai và qua kiểm tra tự động; còn nghiệm thu thiết bị thật. Bước tiếp UI-06f: rà partial ghi chú/giảm giá và ID/handler trùng.

## Giao diện

Áp dụng /admin/pos, /admin/pos/v3 và /admin/pos/legacy dưới 768px theo mẫu navy/blue. Trạng thái chuẩn bị/offline/đang đồng bộ/cần kiểm tra giữ đúng nội dung owner gốc, xuống dòng rõ ràng. Nút thử đồng bộ, lưu bản đối soát và cô lập thao tác lỗi giữ handler cũ, cỡ chạm tối thiểu 44px. Không biến mất mạng thành quyền bán khi chưa có phiên hợp lệ.

Native dialog “Phiên POS đã khóa” phủ kín màn, giữ lời giải thích giao dịch chờ và liên kết đăng nhập gốc; không có nút đóng hoặc cách bỏ qua. CSS chỉ đổi màu/khoảng cách/cỡ chạm phone; owner giữ focus trap/cancel/authentication. Desktop giữ nguyên inline style cũ.

Giao hàng có header/nút đóng 44px, body cuộn độc lập, danh sách hàng và trường người nhận chia khối một cột; input tối thiểu 16px. Dời nguyên deliveryMessage vào footer để luôn thấy lỗi/trạng thái cạnh nút gửi. Link tra đơn gốc chuyển xuống cuối body để chừa chỗ khi bàn phím mở; cả hai trả đúng node/vị trí ≥768px. Giữ form, required/maxlength, disabled state và tác vụ gốc. Không tạo menu hoặc lối tắt màn khách mới.

## Bảo toàn nghiệp vụ

Index chỉ thêm CSS/script; adapter chỉ di chuyển node có sẵn và xử lý visualViewport/focus field. _DeliveryModal không sửa. PosOffline/core/privacy/worker, writer lock, PosDelivery, payment/receipt, customer display và gaoapp.configured-shortcuts, controller/service/DTO/quyền/menu/config/migration khớp snapshot. Không thêm API, dữ liệu menu, queue, submit handler, tính tiền hoặc tự mở khóa phiên.

Offline/journal vẫn do owner gốc quản lý. Phiên chưa sẵn sàng/hết hạn/khác nhân viên/quầy và tab thứ hai không được ghi; dữ liệu chờ giữ để đồng bộ/đối soát theo hàng rào bảo vệ hiện hữu. Giao hàng chỉ online và đồng bộ giỏ xong, không xếp yêu cầu giao vào queue offline. Mất phản hồi vẫn giữ cùng sourceCartId/version/fingerprint/clientRequestId/người nhận; không hiện thành công trước xác nhận server. Màn khách chỉ xuất hiện theo menu cửa hàng đủ điều kiện; opener gốc giữ URL cùng origin, cửa sổ có tên và thông báo popup bị chặn.

## Kiểm tra và đánh giá

- Build Release 0 lỗi/cảnh báo; 358/358 hồi quy POS/offline/privacy/menu/CSP/đơn giao/SQL qua. Có probe SQL/Chrome gốc cho hạn phiên, đăng xuất, đăng nhập lại và dữ liệu khách/cache.
- 111/111 JavaScript qua: offline core/transport/worker/privacy/FIFO, chống trùng giao dịch/intent; dữ liệu người nhận, renderer giao hàng và bảo vệ màn khách/payment/receipt.
- 9 nhóm browser trên Razor/HTTP/SQL thật. Prime/legacy, mỗi bản 7 kích thước 320×568, 360×780, 390×844, 430×932, 600×900, 767×900, 390×440 cho delivery và offline banner: 28 lượt; thêm 7 banner có journal chờ và 7 khóa native: tổng 42. Không tràn ngang, nút đủ cỡ chạm, field/status giữ node/giá trị khi resize. Hai visualViewport 360px mô phỏng giữ body/footer khả dụng.
- 6 đối chiếu mọi computed property và box: delivery/network Prime ở 768/1024/1440, legacy 768/1440 và privacy dialog ở 1440. Bật/tắt CSS mới khớp; trở về desktop khôi phục đúng vị trí các node, không còn footer phone riêng.
- Service worker thật tải lại phone khi mất mạng, bao gồm asset trình bày mới. IndexedDB giữ mọi command ban đầu; chỉ cart/ensure phục hồi được phép thêm. Thử đồng bộ khi offline không đổi queue; xuất đối soát giữ đúng queue và không có CSRF. Giao hàng offline bị chặn, không thêm delivery command.
- Tab thứ hai cùng context/quầy bị chặn ghi 409. Đăng xuất form thật khi offline khóa ngay hai tab trước khi server có mạng, Escape không đóng dialog; journal còn nguyên. Khi có mạng, đăng xuất server rồi đăng nhập đúng tài khoản/quầy khôi phục và đồng bộ note cũ. 6 tình huống session và 4 nhóm tab lock gốc cũng qua, gồm tự lấy lại khóa khi tab chủ đóng.
- Giao hàng: required số điện thoại vẫn chặn submit. Xung đột fingerprint 409 không hiện thành công. Submit chậm nhận lặp vẫn chỉ gửi một request. Server commit nhưng trả 503: không hiện thành công, giữ intent/khóa field; reload và gửi lại chính xác cùng body nhận đúng kết quả cũ. SQL đúng 1 delivery, 1 revision, 1 audit; không payment/đơn bán Completed, tồn 100 giữ nguyên. Fingerprint menu/store/quầy/giá/tùy chọn khách/store khác khớp.
- Thiếu quyền: server không render entry/modal giao hàng và từ chối API. Màn khách thật nhận đúng store/terminal; opener cấu hình thử riêng với DOM giả lập: phone/desktop giữ cùng cửa sổ tên, bỏ qua target không có cấu hình/cross-origin, thông báo popup bị chặn. Không chèn control vào app hay tạo record menu để test.
- Cập nhật ba fixture cũ: unified renderer nhận root có data-mobile-orders; offline session/tab lock tải privacy module và device binding; cache mẫu có privacy authorization; session sau bị từ chối phải chịu 401 hiện tại, tab HTTP khai báo UTF-8. Production giữ nguyên; không nới privacy fence để đáp ứng kỳ vọng cũ.
- 219 file bảo vệ khớp snapshot. Baseline bất biến 560 file: 513 không đổi, 47 đã đổi, 0 mất; hai asset mới ghi riêng trong sourceFiles. Đã rà ảnh phone/legacy, journal chờ, khóa native, lỗi/mất phản hồi/thành công giao hàng và desktop.

## Giới hạn và bằng chứng

Chỉ SQL GUID/Web và Chrome headless/profile riêng, tắt launcher thường và delivery outbox, chặn external traffic/transport in. Profile offline dùng worker/IndexedDB thật; profile tiêm lỗi giao hàng chặn worker riêng để can thiệp phản hồi HTTP. Không chạm database cấu hình hoặc cửa sổ Chrome đang mở; không gọi máy in/ngăn kéo/camera/ngân hàng hay giao hàng thật.

Còn Android/iOS thật: bàn phím mềm/safe area, viewport trình duyệt, tải file đối soát và thao tác một tay. Popup/màn hình khách vật lý cần nghiệm thu trên thiết bị. UI-06f và nghiệm thu cũ giữ riêng.

[Runtime](mobile-ui-20261025/evidence/ui-06e/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-06e/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-06e/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-06e/baseline-delta.json). Ảnh/SQL/TRX: TestResults/mobile-ui-06e-20261026; snapshot/log: .artifacts/ui-06e.
