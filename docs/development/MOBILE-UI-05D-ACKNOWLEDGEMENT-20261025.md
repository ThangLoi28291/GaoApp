# UI-05d — Xác nhận lưu và thử lại nhận hàng

Ngày 25/10/2026. Đã triển khai và tự kiểm thử UI-05d; bước kế tiếp **UI-05e**. Giữ nguyên giao diện máy tính, nguồn menu và quyền cửa hàng. Android/iOS thật còn nghiệm thu riêng.

## Thay đổi

Lần nhận hàng có sẵn hoặc hàng chờ hoàn thiện giữ đúng command ID và business payload khi chưa rõ kết quả. Lỗi đọc lại trạng thái được trả về caller, gồm cả trường hợp POST đã ghi nhưng GET sau đó bị 403 hoặc trả HTML/JSON thiếu trạng thái. Chỉ thay config/state sau khi kiểm tra cấu trúc và rowversion; phản hồi sai không làm mất quyền hợp lệ trước đó hoặc khóa nhầm nút thử lại. Popup giữ số lượng, quy cách, tên, ghi chú và ảnh; đóng lúc lỗi vẫn quay lại được đúng thao tác qua Lưu lại. Khi đang ghi/đọc xác nhận thì không đóng được popup.

Chỉ phát xác nhận thành công, đóng popup và quét tiếp sau khi danh sách hợp lệ cùng phiếu đã tải, nhật ký máy chủ có đúng command và dòng hiện tại khớp số lượng. Snapshot cũ dù có rowversion hợp lệ vẫn không được coi là thành công. Không đẩy nhật ký mới lên thẻ xác nhận trước khi đủ điều kiện; chọn một sản phẩm khác không xóa lệnh đang chờ.

Test toàn luồng phát hiện thêm lỗi cũ: PUT sửa tổng số lượng đã ghi nhưng mất phản hồi, thử lại gặp rowversion cũ. Client nay đối chiếu snapshot: số lượng/đơn vị/ghi chú khớp thì xác nhận bằng GET; phiên bản phiếu và dòng gốc chưa đổi thì mới gửi lại payload/tokens gốc. Có xung đột thì giữ chờ đối chiếu, không lấy token mới để ghi đè. PUT này vốn không có command ID và tiếp tục giữ giao thức đó. Với POST quantity hàng chờ hoàn thiện, command/flag chờ được chuyển sang input sau khi partial thay dòng; Lưu lại mở đúng sheet ngay cả khi value đã bằng default.

Chỉ sửa bốn owner JS hiện có: detail, intake, feedback và một điều kiện tìm dòng chờ trong app. Giữ owner camera, batch quantity, photo và review/catalogue; không thêm API, migration, menu, thư viện hoặc hàng đợi offline. Các lệnh chờ chỉ ở bộ nhớ phiên trang, không bổ sung khôi phục sau F5. Razor và CSS không đổi trong bước này, desktop không đổi bố cục/màu sắc.

## Kiểm thử và đánh giá

- Build Release: **0 lỗi, 0 cảnh báo**. **35/35** hồi quy qua, không skip: receiving/provisional SQL và contract, barcode/quyền, completion, cancellation, photo và CSP. Chạy lại 16 bài contract/cancellation/photo sau thay đổi cuối cũng qua; đây là tập con, không cộng thành bài mới.
- **17 nhóm browser UI-05d** qua trên Razor/HTTP/SQL thật: mất phản hồi sau ghi, GET 403 sau POST, retry 403 tiếp theo, snapshot cũ, chặn đóng lúc chờ, chọn khác, command/payload lặp, quantity trước/sau ghi, phiên bản xung đột và partial thay input. Chỉ năm command nhận hàng được phát feedback thành công.
- Toàn luồng camera/voice/chọn quy cách/ảnh/private read/số lượng/gửi duyệt/chỉ xem/yêu cầu sửa qua; tương tác bàn phím desktop, tăng giảm gộp, giữ vị trí và replay qua. Probe cuộn gốc giữ nguyên, độ dịch lớn nhất **0px**.
- Ảnh desktop 1440px khớp bản trước sửa sau khi cùng đóng ô tìm kiếm; Razor/CSS và **87 file bảo vệ** giữ hash. Đã xem ảnh popup chờ và trạng thái sau xác nhận.
- Chạy lại script trước sửa tái hiện đúng lỗi ở lần POST đầu: reload nuốt GET 403, không vào trạng thái chờ/lỗi như test yêu cầu. Lỗi này được giữ trong thư mục before như bằng chứng đối chiếu, không tính là test pass của bản hiện tại.

SQL sau ma trận: phiếu vẫn Draft, ba dòng hàng đã biết; hai dòng ban đầu giữ Quantity 2. Dòng Hộp thêm đạt **2,75**, không bị cộng lặp. Hàng mới có đúng một mục **2,5 Thùng ×12**, Unresolved, tên/ghi chú đúng và ảnh JPEG **5930 byte**. Danh mục, quy cách, số dư tồn, posting/công nợ, menu và phiếu khác giữ snapshot trước/sau.

Hai kỳ vọng cũ trong probe toàn luồng được cập nhật theo giao diện đã có: **768px là desktop/tablet**; xóa hàng phải xác nhận trong popup xóa hiện tại. Test kiểm tra dòng vẫn còn trước khi bấm xác nhận rồi mới kiểm tra bị loại khỏi phiếu.

Baseline 560 file không sửa: **518 không đổi, 42 đổi cộng dồn, 0 mất**. Chỉ chạy database GUID/Web/Chrome headless/profile của fixture, tắt launcher Chrome thường. Không dùng database cửa hàng, không đóng hoặc đổi cửa sổ Chrome đang mở.

## Phần còn riêng

UI-05e tiếp tục ma trận số lẻ, quét lại cùng mã, quy cách cùng tên, phiếu chỉ xem và thiết bị. Camera/micro được giả lập để kiểm soát phản hồi; decoder, HTTP, Razor và SQL chạy thật. Chưa nghiệm thu camera, bàn phím, safe area và thao tác chạm trên Android/iOS thật. Không mở rộng ma trận thử lại review/duyệt danh mục ở bước này; UI-02/UI-03 menu/ghim/F5-Stop còn trạng thái riêng.

## Bằng chứng

- [Runtime](mobile-ui-20261025/evidence/ui-05d/runtime-review.json), [source](mobile-ui-20261025/evidence/ui-05d/static-review.json), [ma trận](mobile-ui-20261025/evidence/ui-05d/acceptance-matrix.json), [baseline delta](mobile-ui-20261025/evidence/ui-05d/baseline-delta.json).
- [35 hồi quy](../../TestResults/mobile-ui-05d-20261025/regression/regression.trx), [browser](../../TestResults/mobile-ui-05d-20261025/browser/result.json), [SQL](../../TestResults/mobile-ui-05d-20261025/browser/sql-checks.json).
- [Lỗi đọc sau ghi](../../TestResults/mobile-ui-05d-20261025/browser/known-read-failure.png), [snapshot cũ](../../TestResults/mobile-ui-05d-20261025/browser/known-snapshot-mismatch.png), [hàng mới chờ](../../TestResults/mobile-ui-05d-20261025/browser/unknown-awaiting-confirmation.png), [sau xác nhận](../../TestResults/mobile-ui-05d-20261025/browser/confirmed-mobile.png), [desktop](../../TestResults/mobile-ui-05d-20261025/browser/desktop-current.png).
- [Toàn luồng](../../.artifacts/ui-05d/full-flow-final.log), [tương tác](../../.artifacts/ui-05d/interaction.log), [cuộn](../../.artifacts/ui-05d/scroll.log), [đối chiếu trước sửa](../../.artifacts/ui-05d/before-final.log).
