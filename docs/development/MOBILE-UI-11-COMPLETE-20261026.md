# UI-11 — Quản lý và duyệt phiếu nhập trên điện thoại

Ngày 26/10/2026. Đã triển khai và hoàn tất kiểm tra tự động UI-11a/b/c/d/e/f. Còn nghiệm thu thiết bị thật. Bước tiếp theo: UI-12a, kiểm kê.

## Giao diện đã nâng cấp

- **UI-11a — Danh sách và đầu phiếu:** thẻ phiếu nhập, thống kê gọn, tìm kiếm và bộ lọc dùng dữ liệu gốc. Nhận diện rõ kho, nhà cung cấp, pháp nhân, nguồn trực tiếp/đơn mua và trạng thái. Tạo phiếu, sửa đầu phiếu, gửi duyệt và thao tác khác tiếp tục theo quyền/trạng thái sẵn có.
- **UI-11b — Nội dung phiếu:** lối đến Hàng nhận, Cần xử lý, Thương mại và Chứng từ chỉ xuất hiện khi phần gốc tồn tại. Các lối này cuộn đến node gốc, mở phần chi tiết hoặc gọi chủ tab có sẵn; không tạo thêm bộ tab điều khiển nội dung. Tab Hàng hóa, Giá & khuyến mãi, Thanh toán & thuế và Vận chuyển vẫn do script nghiệp vụ hiện tại quản lý.
- **UI-11c — Hoàn thiện hàng nhận:** thẻ hàng và hộp hoàn thiện sản phẩm/quy cách/barcode/ảnh phù hợp màn nhỏ. Giữ mục cần xử lý, dữ liệu nhân viên khai báo, yêu cầu sửa, hàng ngoài đơn, quá lượng và đường mở công việc gốc. Không tự mở quyền sửa, bỏ điều kiện duyệt hoặc biến mục hỗ trợ XML thành điều kiện bắt buộc.
- **UI-11d — Thương mại:** giá nhập, cảnh báo chênh lệch, giá bán, VAT, cước và phân bổ trình bày theo từng phần hiện có; hộp tính/đối chiếu/xác nhận cuộn được. Giữ các bước bill/chương trình/giá và chủ tính toán gốc. Giá thử, giá lưu nháp và giá áp dụng tiếp tục có ý nghĩa nghiệp vụ riêng; không tính lại tiền bằng adapter giao diện.
- **UI-11e — Tách phiếu, ghi chú và lịch sử:** xem lượng nguồn/còn lại và các phiếu kết quả bằng bố cục thẻ, đối chiếu trước xác nhận. Giữ lý do, ghi chú và lịch sử chỉ đọc theo quyền. Thanh thao tác nằm sau nội dung trên điện thoại để trường nhập và nội dung xác nhận không bị che; không thay chủ xử lý duyệt/trả phiếu.
- **UI-11f — Thư viện hóa đơn và picker XML:** danh sách hóa đơn thành thẻ, bộ lọc thu gọn có nhãn điều kiện đang chọn, hộp chi tiết toàn màn hình và hộp thêm file phù hợp chiều cao thấp. Picker giữ nhà cung cấp/MST, chủ thể người mua, nguồn XML và khả năng liên kết do máy chủ quyết định. Quyền xem giá vốn, tải XML/PDF và xác nhận nội bộ được giữ riêng.

Bốn view đầu vào chỉ thêm import hai cặp asset mới. Các partial, CSS desktop và script nghiệp vụ gốc được tái sử dụng. CSS mới giới hạn dưới 768px; màu navy/blue, trường nhập 16px, vùng chạm tối thiểu 44px, vùng cuộn và safe-area/reduced motion. Adapter chỉ bổ sung trình bày vào node gốc và khôi phục thuộc tính khi chuyển về desktop. Không clone form/trường tiền, thêm fetch, bộ nhớ nháp, phép tính lượng/tiền hoặc relay lệnh nghiệp vụ.

## Quyền, cấu hình và bảo vệ source

Phiếu theo đơn mua tiếp tục dùng quyền Purchase.Receipt; phiếu trực tiếp dùng quyền Inventory.StockDocument. Quyền/trạng thái sửa vật lý, duyệt thương mại, tách phiếu, sửa giá bán và xem lịch sử giữ nguyên. Người chỉ xem không được bật hành động ghi; người chỉ xem giá vốn không tự có quyền xác nhận hóa đơn. Nguồn có liên kết PO vẫn dùng quyền mua hàng dù cờ nguồn cũ của phiếu là trực tiếp. Snapshot chủ thể của phiếu đã xác nhận và kiểm tra chủ thể XML không đổi.

- **1.304 file bảo vệ**, **25 file nguồn nghiệm thu** trong kiểm tra tĩnh hiện tại. Controller/service/repository/DTO, chính sách quyền, RowVersion/lease/GUID lệnh, chủ tính toán/phân bổ, ghi kho, công nợ và audit gốc giữ hash.
- Bốn Razor view có thân gốc tương đương sau khi bỏ import asset mới. 11 browser probe giữ nguyên kiểm tra nghiệp vụ sau khi bỏ review hook và chuẩn hóa đúng hai kỳ vọng hình học cuộn lịch sử được đánh dấu.
- Chỉ bốn kỳ vọng bố cục được cập nhật: hai footer quick price từ `sticky` sang `static`, hai vị trí cuộn lịch sử theo scroll-margin và scroll-padding thực tế. So sánh cấu trúc xác nhận các phần còn lại, assertion nghiệp vụ và kiểm tra thao tác chạm giữ nguyên. Không bỏ qua lỗi giá, lượng, lưu nháp hoặc điều kiện duyệt.
- Không đổi layout/sidebar/pin/menu, quyền hay cấu hình cửa hàng. Menu vẫn lấy từ `AdminMenuItems` theo cửa hàng, quyền, trạng thái và thứ tự; không tạo menu, seeder, migration, schema, endpoint hoặc quyền mới.
- Baseline 560 file không sửa: **483 không đổi /77 đã đổi qua các bước /0 mất** tại lần rà hiện tại.

## Kết quả kiểm tra

- Release build **0 cảnh báo, 0 lỗi**; gzip của bốn asset khớp byte source cuối. **993/993 test .NET duy nhất**, **143/143 JavaScript** qua; không bỏ qua test. Broad có 989 qua/hai lỗi; release chạy lại bốn ca qua. Gộp bằng testId/case ổn định chỉ tính hai ca mới thêm, không cộng lặp hai ca chạy lại. Hai lỗi ban đầu và cách xử lý được giữ trong runtime evidence.
- **8 luồng CLI browser/API/SQL +4 ca release browser/SQL**, 19 trạng thái, **133 lượt phone**, **38 đối chiếu desktop**, **48 lượt hộp thoại**. Phone kiểm 320/360/390/430/600/767px và 390×440; không tràn ngang/ID trùng. Trường gốc giữ node, giá trị, disabled/read-only/validation và chủ form; desktop 768/1440px khớp toàn bộ computed CSS và hình học phần đang hiển thị khi tắt stylesheet mới, không còn thuộc tính phone. Hình học nội dung nằm trong native DETAILS đang đóng được chuẩn hóa riêng; summary và toàn bộ CSS vẫn được đối chiếu.
- Tách phiếu qua giao diện thật tạo hai kết quả PendingApproval, giữ tổng lượng, đơn vị/hệ số nguồn; tồn và giao dịch kho không đổi, chưa tạo công nợ, audit nguồn/con đúng một lần. Thiếu quyền/CSRF và khác cửa hàng bị từ chối; menu/cấu hình/dữ liệu cửa hàng khác khớp fingerprint.
- Thư viện **14 tổ hợp vai trò/nguồn**, **84 lượt resize hộp chi tiết**, năm ca native filter validation. Giữ giá trị năm/tháng khi thu gọn/resize; năm sai mở đúng trường gốc. Người không có quyền không thấy giá/ghi chú riêng hoặc tải preview/XML/PDF; chỉ xem giá không tự có quyền xác nhận. Ghi giả mạo 403, cửa hàng khác 404/danh sách rỗng; XML ngoài chủ thể bị loại. SQL trước/sau giữ hóa đơn/lịch sử/tồn/phiếu/công nợ/giao dịch và cấu hình.
- Entry, hành động phiếu, nhà cung cấp, intake, duyệt thương mại, yêu cầu sửa, lịch sử và thư viện tám XML đi qua HTTP/Razor/SQL thật. Hồi quy XML và ma trận quyền thương mại nằm trong 991 ca broad, đã được kiểm tra riêng. Luồng duyệt kiểm tra giá/VAT/cước, công nợ, kho và audit gốc; không chỉ chụp giao diện.
- Đã rà ảnh cuối theo xác nhận root tại .artifacts/ui-11/visual-inspection.json. Đã xem ảnh viewport và các ảnh toàn trang liên quan: trường nhập và tác vụ dễ đọc, thẻ hàng giữ lượng/giá/quy cách, lịch sử có bộ lọc một cột, các sheet giữ footer. Picker XML cuối dùng thẻ hai cột nên ngày/ký hiệu không bị bẻ vụn. Đánh giá vị trí dựa trên ảnh viewport và kiểm tra geometry/hit-target; ảnh full-page của shell fixed có thể vẽ header/nav tại điểm chụp. Còn nghiệm thu Android/iOS và phần cứng thật.

## Sửa bố cục và ổn định nghiệm thu

Test 320px tìm thấy dropdown Select2 được gắn ngoài root: min-width cũ 320px ở x=33 làm trang rộng **353px**, kể cả outer span cao 0. CSS chỉ trên điện thoại nay giới hạn cả owner/dropdown và giữ ô tìm 16px/44px. Negative control tái hiện lỗi; 54 biến thể body/modal/zero-height sau sửa vừa màn. Bảng bằng chứng có scroller riêng và nội dung trong native DETAILS đóng được phân biệt khi chẩn đoán; không che tràn ngang bằng cách bỏ kiểm tra toàn trang.

Helper chờ chủ calculator/save/receipt sẵn sàng, fonts, hai frame và animation hữu hạn bằng poll tối đa 30 giây. Chỉ hai API tính thử read-only chính xác được ghi riêng, cùng heartbeat registry hiện có; mọi lệnh ghi vẫn bị kiểm. Footer đặt sau nội dung; lịch sử cuộn theo header/scroll-margin. Hai ca hồi quy đầu lỗi vì kỳ vọng bố cục và readiness đã chạy lại qua với kiểm tra nghiệp vụ giữ nguyên.

## Môi trường và phần còn lại

Các lượt đã xác nhận chỉ dùng Web/LocalDB GUID và thư mục XML thử riêng; Chrome headless/profile mới, tắt launcher thông thường, chặn kết nối ngoài và transport đến thiết bị thật. Không đóng cửa sổ/tab Chrome của người dùng hoặc đổi database/cấu hình cửa hàng đang dùng.

Còn nghiệm thu **Android/iOS thật**, bàn phím, safe-area, zoom, thao tác một tay, camera và máy quét phần cứng. Chưa chạy in hoặc thiết bị vật lý. Các nghiệm thu thiết bị thật của những bước trước tiếp tục mở; không coi mô phỏng viewport là thay thế kiểm tra thiết bị.

[Source](mobile-ui-20261025/evidence/ui-11/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-11/baseline-delta.json), [quyền thư viện](../../TestResults/mobile-ui-11-20261026/library-permissions/mobile-library-permissions.json), [SQL thư viện](../../TestResults/mobile-ui-11-20261026/library-permissions/sql-review.json). TRX/ảnh/SQL: `TestResults/mobile-ui-11-20261026`; snapshot/log: `.artifacts/ui-11`. [Runtime](mobile-ui-20261025/evidence/ui-11/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-11/acceptance-matrix.json), [asset nén](mobile-ui-20261025/evidence/ui-11/asset-bundle.json).
