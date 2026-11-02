# Bổ sung quét camera cho POS

Ngày: 25/10/2026. Theo yêu cầu bổ sung sau UI-06a. Đã qua tự động; còn camera/điện thoại thật. Bước triển khai tiếp theo vẫn là UI-06b.

## Sử dụng và phạm vi

Trên điện thoại, nút quét ở bên phải ô tìm/mã vạch trên /admin/pos, /admin/pos/v3 và /admin/pos/legacy. Bấm để mở camera sau; đưa mã vào khung. Có Bật/Tắt đèn nếu camera hỗ trợ, Quét lại khi cần và nút đóng. Đọc xong một mã, camera dừng và modal đóng; mã được chuyển vào exact scan POS gốc. Muốn quét tiếp thì bấm nút camera lần nữa. Badge số lượng vẫn áp dụng: SL 4 + barcode Lốc tạo 4 Lốc theo conversion gốc. Badge được bố trí cạnh nút camera để không che nút hoặc ô nhập.

Camera yêu cầu HTTPS/localhost và quyền trình duyệt. Thiếu quyền, không có camera, camera bận hoặc lỗi bộ đọc đều hiện hướng khắc phục; đóng để tiếp tục gõ mã hoặc dùng máy quét. Chỉ đọc barcode sản phẩm 1D như nhận hàng, không xử lý QR thanh toán thành hàng bán. Desktop/tablet ≥768px không hiện control camera và không xin quyền camera.

## Cơ chế giữ nghiệp vụ

Dùng đúng thư viện ZXing BrowserMultiFormatOneDReader local hiện có của nhận hàng; cùng origin, nonce CSP, không thêm CDN. Nút toolbar ban đầu hidden; CSS/adapter chỉ kích hoạt phone. Thêm một modal, CSS và JS camera; Index chỉ thêm import/partial, toolbar thêm nút.

Không sửa PosBarcode, PosApp, PosPrime, PosKeyboard, PosCommon, state/render/scan feedback/scan guard, offline/service worker, controller/service/DTO/quyền hoặc menu. Chỉ chuyển mã được nhận vào input duy nhất và gọi scanCurrentCart() đúng một lần; owner gốc giữ số lượng, giá, conversion, khóa giỏ/thanh toán, online, command/ack/retry và lỗi mã lạ. Không có API, storage, queue, menu hoặc migration mới.

Guard phím được cài trước keyboard POS, chỉ chặn khi modal camera đang mở; F2/F3/F4/F6/F8/Ctrl+Del/Enter không lọt sang nghiệp vụ. Tab và nút native giữ hoạt động; Esc dừng camera. Đóng, chuyển nền/pagehide, đổi sang tablet/desktop sẽ hủy stream và bỏ kết quả muộn. Callback camera kiểm session/order/store/terminal/user trước khi handoff; đổi giỏ thì yêu cầu quét lại. Không tự bật camera sau quay lại trang.

## Kết quả kiểm tra

- Build Release: 0 lỗi, 0 cảnh báo; 49/49 hồi quy POS/SQL giá quy cách/tìm/ca-quầy/CSP qua.
- 16 nhóm camera trên Razor/HTTP/SQL thật: video canvas CODE128 được đọc bởi decoder ZXing thật; camera/torch và lỗi thiết bị là mô phỏng. Bao gồm mã lặp qua nhiều frame, người dùng mở lại cùng mã, SL 4 + Lốc, mã lạ, thiếu quyền/thiếu thiết bị/camera bận, torch có/không hỗ trợ và lỗi constraint, phím tắt, đóng khi getUserMedia còn chờ, chuyển nền, đổi viewport, lỗi tải/Quét lại, điều kiện offline gốc, đổi phiên và legacy/v3.
- 7 viewport modal: 320×568, 360×780, 390×844, 430×932, 600×900, 767×900, 390×440. 768/1024/1440px không hiện control; resize dừng stream.
- Chạy lại 16 nhóm đầu vào POS gốc với service worker thật ở 10 viewport; giữ giỏ khi đổi phone/desktop, tìm bằng Enter/touch, quy cách, scan/prefix, modal/history, quyền, chưa mở ca và fallback. Source probe gốc giữ nguyên. Desktop mọi computed property/box và header DOM vẫn khớp theo phương pháp UI-06a.
- SQL: cùng một Draft, bia 2 Lon + 4 Lốc = 24 Lon, nước 3 Lon. Không thanh toán hoặc xuất tồn; fingerprint menu/tồn/giá/ca/store khác giữ nguyên.
- 197 hash bảo vệ khớp snapshot; baseline bất biến 560 file, 515 không đổi, 45 đã đổi, 0 mất. View/asset mới ghi riêng trong sourceFiles.
- Chỉ SQL GUID/Web và Chrome headless/profile riêng; launcher Chrome thường tắt. Không truy cập database cấu hình, camera thật hoặc các cửa sổ Chrome đang mở.

Probe camera tắt service worker riêng để Playwright tiêm được lỗi tải script thay vì cache trả tài nguyên; probe POS gốc vẫn kiểm worker thật. Không thay service worker production để làm test qua. getUserMedia còn pending không thể lấy track để dừng trước khi promise trả về; generation guard dừng ngay stream trả muộn, không đưa mã vào giỏ.

## Bằng chứng và nghiệm thu còn lại

[Runtime](mobile-ui-20261025/evidence/ui-06a-camera/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-06a-camera/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-06a-camera/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-06a-camera/baseline-delta.json). Ảnh/SQL/TRX tại TestResults/mobile-ui-06a-camera-20261025; log/snapshot tại .artifacts/ui-06a-camera. Báo cáo UI-06a trước camera giữ làm lịch sử, bằng chứng bổ sung này là trạng thái hiện tại.

Còn thử Android/iOS thật: cấp/từ chối quyền, camera sau/autofocus/torch, mã in thực tế, bàn phím và safe area. Các phần UI-06b–f và nghiệm thu vật lý cũ chưa được đóng bằng test này.
