# UI-05c — Quy cách, số lượng và hàng cần hoàn thiện

Ngày 25/10/2026. Đã triển khai và kiểm thử tự động UI-05c trên chi tiết phiếu nhận hàng. Bước tiếp theo **UI-05d — command ID, trường đang chờ xác nhận và đóng/quét tiếp sau xác nhận máy chủ**. Android/iOS thật còn nghiệm thu riêng.

## Giao diện đã nâng

Chỉ dưới 768 px, mỗi control số lượng có nhãn đơn vị ngay cạnh: SL nhận · Lốc / Thùng / Hộp. Dùng đúng ô đơn vị của dòng hiện tại; barcode và hệ số quy đổi gốc tiếp tục hiển thị để phân biệt hai quy cách cùng tên. Bổ sung liên kết mô tả bằng `aria-describedby`, khôi phục thuộc tính cũ khi về desktop. Không thay nút tăng/giảm, sheet số lượng hoặc handler lưu.

Phần tóm tắt dùng **số dòng trên phiếu** và **số mục cần xử lý** từ các counter hiện có. Không coi số dòng này là số sản phẩm đã được duyệt, không dựng phần trăm hoàn tất hoặc cộng lốc/thùng/hộp thành một tổng. Lối vào mục cần xử lý và ảnh bao bì chọn tab Thông tin, mở đúng details gốc; các nút hoàn thiện/loại/nhận thêm vẫn do quyền và module cũ quyết định.

Form hàng mới/quy cách mới dùng thẻ quy cách, phần quy đổi và số lượng rõ hơn. Ảnh bao bì và ghi chú được gom vào phần mở rộng không bắt buộc, chuyển nguyên node gốc. Nút **Chụp / chọn ảnh bao bì** là label cho native file input cũ; vẫn dùng kiểm tra, thu nhỏ, xem trước và bỏ ảnh của owner cũ. Form giữ ảnh và dữ liệu đang nhập khi đổi phone/desktop hoặc chiều cao màn hình. `riQuantity` nhận `min/step = 0.001` riêng phone để phản ánh số lẻ hiện hành; desktop khôi phục đúng thuộc tính cũ.

Các thẻ cần xử lý, ảnh đã nhận và màn hoàn thiện theo mẫu navy/lam; vùng chạm, chữ nhập, phần cuộn và footer phù hợp màn hình nhỏ. CSS mới chỉ nằm trong media dưới 768 px và chỉ áp dụng khi chi tiết phiếu bật adapter. Shared partial dùng ở các màn khác giữ nguyên.

## Quyền và nghiệp vụ giữ nguyên

View chỉ thêm hai import asset. Không sửa `_ReceivingApp`, quantity, intake, photo, review, feedback, viewport, controller/service/repository hoặc DTO. Adapter không gọi API/storage, clone control, gán giá trị form, tính lại quy đổi hay tạo lệnh nhận/duyệt. Chỉ chuyển node, thêm nhãn/count và mở nội dung trình bày hiện có.

Quyền nhận hàng/hoàn thiện/duyệt tiếp tục lấy từ máy chủ; không làm hiện nút duyệt cho nhân viên. Không thêm menu, API hoặc migration. Sidebar/ghim vẫn từ cấu hình `AdminMenuItems`; các nút cần xử lý/ảnh là thao tác ngữ cảnh trong phiếu.

## Kết quả kiểm thử

- Build Release Web/Razor/Tests/runner: **0 lỗi, 0 cảnh báo**.
- **26/26 hồi quy**: 2 SQL hoàn thiện/ảnh/quyền, 15 projection số lượng/quy cách, 7 kiểm tra ảnh và 2 CSP.
- **18 nhóm UI-05c** trên Razor/HTTP/SQL thật, 10 kích thước: 1600, 1440, 1366, 1024, 768, 767, 430, 390, 360 và 320 px.
- Chạy lại **23 nhóm UI-05b**: tìm/quét, xác nhận nhận hàng, gửi duyệt/chỉ xem, quyền, tab/menu, desktop và các ca trình bày cũ đều qua.
- Nguyên probe cuộn danh sách dài qua: tăng/giảm ở 320/390 px, sheet lượng lẻ, hàng chờ hoàn thiện, người dùng cuộn trong lúc phản hồi chậm và nhận mã mới. Các lần đo trong log có **max frame shift 0 px**.

Fixture UI-05c có hai dòng đơn vị Lốc nhưng hệ số 4 và 6, cùng một dòng Thùng ×24. Browser mở đúng dòng Lốc ×4, lưu 2,5; hai dòng còn lại vẫn giữ 2 Thùng và 3 Lốc ×6. Nhãn và metadata không bị trộn khi renderer thay dòng sau lưu.

Nhân viên khai báo hàng mới **2,5 Thùng ×12 = 30 Hộp**, không có quyền duyệt ngay. Thử ảnh SVG không hợp lệ: owner báo lỗi và không gửi lệnh nhận. Sau bỏ ảnh lỗi, dùng nút mới mở hộp chọn tệp native, tải ảnh PNG tổng hợp; owner thật chuyển sang JPEG và lưu cùng lần nhận. Giữ đúng node file input, dữ liệu JPEG, tên/ghi chú/số lượng/hệ số qua các kích thước 320×568, 390×440, desktop và 390×844.

Sau xác nhận máy chủ, tóm tắt có **4 dòng trên phiếu, 1 mục cần xử lý**. Lối vào mở đúng chi tiết cần xử lý hoặc ảnh đã nhận; nhân viên không có nút review. Ảnh lưu được đọc lại bằng GET thật và đúng kiểu JPEG. Quản lý mở màn hoàn thiện gốc, thấy khai báo/ảnh/quy cách, sửa tên chính thức và Lưu nháp; tải lại vẫn có bản nháp, khai báo ban đầu được giữ, chưa duyệt vào danh mục.

Probe chính chỉ có **ba lệnh ghi**: một PUT số lượng, một POST hàng chờ hoàn thiện và một POST lưu nháp review. SQL xác nhận:

- Lốc ×4: Quantity **2,5**, BaseQuantity **10**; dòng Thùng ×24 và Lốc ×6 không đổi.
- Hàng mới: Quantity **2,5**, ProposedFactor **12**, trạng thái **Unresolved**, có ảnh JPEG và tên bản nháp đúng.
- Phiếu vẫn **Draft**, ba dòng hàng đã biết; dòng khai báo thêm được trình bày theo aggregate tạm hiện có.
- Danh mục biến thể/quy cách, số dư tồn, posting/công nợ và menu giữ snapshot trước/sau.

Không lỗi JavaScript/CSP; không tràn ngang ở các viewport, forced colors và chữ 150%. Đã xem ảnh số lượng theo đơn vị, form khai báo, ảnh, danh sách cần xử lý và màn quản lý. Ảnh desktop 1440 px khớp khi bật/tắt CSS mới; DOM/node/form khôi phục. Phép so HTML chỉ chuẩn hóa `style=""` rỗng do thư viện đo kích thước để lại trên input và riêng textarea `riNote`; không bỏ qua giá trị hoặc thuộc tính có nội dung.

**88 file bảo vệ giữ hash**. Baseline 560 file giữ nguyên: **521 không đổi, 39 đổi cộng dồn, 0 mất**; asset mới và hash sau thay đổi ghi riêng.

Chỉ dùng SQL GUID dùng một lần, Web và Chrome headless/profile thuộc fixture; launcher Chrome thường tắt trong môi trường test. Fixture đã kết thúc, không thay database cửa hàng hoặc đóng/đổi các cửa sổ Chrome đang mở.

## Phần còn riêng

Ảnh thử là ảnh tổng hợp được chọn qua hộp tệp native; chưa nghiệm thu chụp ảnh thật, bàn phím, safe area và thao tác chạm trên Android/iOS. UI-05d tiếp tục ma trận lệnh/trường chưa được xác nhận; UI-05e tiếp tục các trường hợp biên và thiết bị thật. Các ca menu/ghim/F5-Stop UI-02/UI-03 giữ trạng thái riêng.

## Bằng chứng

- [Runtime review](mobile-ui-20261025/evidence/ui-05c/runtime-review.json), [source review](mobile-ui-20261025/evidence/ui-05c/static-review.json), [ma trận nghiệm thu](mobile-ui-20261025/evidence/ui-05c/acceptance-matrix.json), [baseline delta](mobile-ui-20261025/evidence/ui-05c/baseline-delta.json).
- [26 bài hồi quy](../../TestResults/mobile-ui-05c-20261025/regression/regression.trx), [18 nhóm UI-05c](../../TestResults/mobile-ui-05c-20261025/browser/result.json), [SQL](../../TestResults/mobile-ui-05c-20261025/browser/sql-checks.json), [23 nhóm UI-05b chạy lại](../../TestResults/mobile-ui-05c-20261025/ui05b-regression/result.json), [log cuộn](../../.artifacts/ui-05c/scroll.log).
- [Điện thoại 390 px](../../TestResults/mobile-ui-05c-20261025/browser/packing-phone-390.png), [320 px](../../TestResults/mobile-ui-05c-20261025/browser/packing-phone-320.png), [form quy cách](../../TestResults/mobile-ui-05c-20261025/browser/packing-unknown-390.png), [ảnh/ghi chú](../../TestResults/mobile-ui-05c-20261025/browser/packing-photo-390.png), [mục cần xử lý](../../TestResults/mobile-ui-05c-20261025/browser/packing-employee-pending-390.png), [quản lý hoàn thiện](../../TestResults/mobile-ui-05c-20261025/browser/packing-manager-review-390.png).
- [Máy tính](../../TestResults/mobile-ui-05c-20261025/browser/packing-desktop-current.png), [máy tính khi tắt CSS mới](../../TestResults/mobile-ui-05c-20261025/browser/packing-desktop-baseline.png).
