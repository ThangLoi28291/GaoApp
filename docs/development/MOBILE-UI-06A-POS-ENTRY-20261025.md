# UI-06a — Đầu vào POS trên điện thoại

Ngày: 25/10/2026. Trạng thái: qua kiểm thử tự động; còn nghiệm thu thiết bị thật. Bước tiếp: UI-06b.

## Đã áp dụng

Chỉ dưới 768px, trên /admin/pos, /admin/pos/v3 và /admin/pos/legacy. Thanh đầu trắng/navy, nút chạm tối thiểu 44px; thông tin ca/quầy gọn, mở bằng details native để xem kho, nhân viên và trạng thái thật từ máy chủ. Giữ nguyên menu được cấu hình và kiểm tra quyền cửa hàng. Nút đơn giữ hiện nhãn và số từ renderer gốc; tài khoản được trả về luồng bố cục để không đè nút đơn giữ. Phím tắt giữ nút và handler gốc, khôi phục nhãn bị CSS phone cũ cắt. Thu gọn đúng một nhãn Đơn giữ trên legacy; nút đóng modal đơn giữ có vùng chạm 44px trong khung.

Ô tìm/quét 16px, kết quả và nút mở quy cách dễ chạm, command chips giữ tác vụ gốc. Viewport thấp ẩn hàng chip phụ; vẫn nhập được lệnh bằng ô quét. Feedback và lịch sử quét giữ nguồn dữ liệu/ack/overlay hiện có. Prime tái dùng natural-flow fallback khi chiều cao ≤680px hoặc ca/quầy đang mở để giỏ không bị co về 0; thanh tác vụ đi sau giỏ, tránh che dòng hàng. Probe cuộn tới giỏ xác nhận có vùng chạm, không bị thanh tác vụ đè và vẫn cuộn tới nút thanh toán gốc ở hai viewport thấp. Không thêm camera vì POS chưa có luồng camera.

Razor chỉ thêm hai import; thêm pos.entry.mobile.css và pos.entry.mobile.js. Adapter chuyển cùng node context/readiness/phím tắt, trả về marker gốc khi width ≥768px, không clone control, gọi API, ghi giá trị form hoặc storage. Không sửa controller/service/quyền/menu/migration, hay owner giỏ, giá, quy cách, thanh toán, offline và Prime. UI-06b–f tiếp tục theo kế hoạch.

## Kết quả tự kiểm tra

- Build Release: 0 lỗi, 0 cảnh báo.
- 49/49 hồi quy: Prime architecture/responsive/business owner, cockpit/cart, SQL tìm hàng/giá quy cách/ca-quầy và CSP.
- 16 nhóm browser với Razor/HTTP/SQL thật: 10 viewport 320×568, 360×780, 390×844, 430×932, 600×900, 767×900, 768×1024, 1024×768, 1440×1000, 390×440. Thêm kiểm tra lại 320/390 thấp/430 sau quét. Không tràn ngang; input/cart duy nhất, vùng chạm header không bị che.
- Giỏ đã có 2 Lon nước được giữ khi đổi viewport; Enter và chạm chọn bia, chạm chọn Lốc, quét prefix + Enter lặp trong lúc lưu chỉ gửi một request; history giữ quyền phím tắt; reload/v3/legacy giữ giỏ.
- SQL: cùng một draft; bia 5 Lon + 1 Lốc ×6, nước 3 Lon. Không phát sinh thanh toán hoặc xuất tồn. Menu, tồn, giá quy đổi, ca và dữ liệu store khác giữ nguyên fingerprint. Store khác chưa mở ca hiển thị cảnh báo thật; tài khoản thiếu quyền bị máy chủ chặn cả ba route.
- Hai probe hồi quy tìm/barcode qua: 17 ca search/keyboard/IME, cùng probe barcode số một phần, highlight, quy cách, scanner và mã không tồn tại.
- Desktop: mọi computed property/box trên desktop-1440 (320 node), legacy-desktop-1440 (370 node) khớp khi bật/tắt stylesheet phone; header hoàn nguyên đúng cấu trúc HTML và cùng node (chuẩn hóa riêng aria-expanded của nút menu do Bootstrap quản lý) sau quay lại desktop. Rà ảnh riêng; không dùng đẳng thức byte PNG làm kết luận vì Chrome có thể đổi khử răng cưa chữ khi repaint.
- 191 file bảo vệ khớp snapshot trước bước này. Baseline bất biến: 560 file, 516 không đổi, 44 đã đổi qua các bước, 0 mất.
- Chỉ dùng fixture SQL GUID/Web và Chrome headless riêng; không truy cập database cấu hình của cửa hàng, không đóng/điều khiển Chrome đang mở.

## Điều chỉnh test

Probe pos-search-enter trước đây yêu cầu số đang có suggestion phải đi exact scan; source POS hiện tại đã chọn dòng highlight khi suggestion đang mở. Probe pos-barcode-search độc lập đã bảo vệ hành vi đó và qua từ đầu. Cập nhật hai kỳ vọng số của probe cũ; ca barcode đầy đủ gửi Enter trước khi autocomplete mở; giữ kiểm tra scanner, gõ tên, chờ kết quả, Enter lặp, Escape và IME. Production pos.barcode.js giữ nguyên hash. Whitelist thứ tự script của contract được bổ sung adapter ở cuối.

## Bằng chứng và phần còn lại

[Runtime](mobile-ui-20261025/evidence/ui-06a/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-06a/acceptance-matrix.json), [source](mobile-ui-20261025/evidence/ui-06a/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-06a/baseline-delta.json). Ảnh/log/SQL/TRX ở TestResults/mobile-ui-06a-20261025; lệnh và snapshot ở .artifacts/ui-06a.

Điện thoại Android/iOS thật, bàn phím/IME, safe area/browser chrome, xoay màn hình/background-resume và máy quét thật chưa được chứng nhận bằng viewport headless. Nghiệm thu vật lý các bước trước vẫn mở. Không đổi trạng thái toàn UI-06 thành hoàn tất.
