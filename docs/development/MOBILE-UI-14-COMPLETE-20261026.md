# UI-14 — Sản phẩm, barcode và danh mục trên điện thoại

Ngày 26/10/2026. Giao diện UI-14a/b/c/e/f và các luồng barcode hiện hành đã qua kiểm tra tự động. UI-14d đã chốt kiểm toán R05; route BarcodeManager độc lập còn lỗi nguồn, ghi rõ bên dưới. Còn nghiệm thu thiết bị thật. Bước tiếp theo: UI-15a — khách hàng.

## Giao diện đã triển khai

- **UI-14a:** danh sách sản phẩm có KPI gọn, tìm SKU/barcode/tên, bộ lọc thu gọn và xem nhanh đơn vị/giá gốc. Detail giữ thư viện ảnh, tên và bí danh hiện có; không tự tạo dữ liệu giá/đơn vị chưa có trong model.
- **UI-14b:** tạo/sửa sản phẩm dùng nhóm nội dung và điều hướng nội trang trên điện thoại. Field, validation, ảnh, trạng thái sẵn sàng bán và luồng lưu giữ nguyên.
- **UI-14c:** quy cách, đơn vị, barcode và lịch sử theo biến thể/đơn vị đã chọn; hộp thoại có body cuộn, footer và nút đóng trong khung. Giá/hệ số vẫn do owner gốc quản lý. Ảnh trống có placeholder điện thoại; node ảnh và điểm chạm mở bộ chọn ảnh native giữ nguyên.
- **UI-14e/f:** Brand, Category, Unit, Tax, ProductAttribute và AttributeValue dùng bộ danh sách/form chung; giữ validation và RowVersion. Chuẩn hóa barcode dùng thẻ yêu cầu và modal gốc; lịch sử giữ từ khóa, bộ lọc loại/thời gian thu gọn và vùng cuộn tải thêm gốc.

Mười tám view chỉ thêm import của ba cặp products.mobile, catalog-foundation.mobile và catalog-barcodes.mobile. CSS chỉ dưới 768px; adapter trả thuộc tính và vị trí node về nguyên bản khi từ 768px. Trường nhập 16px, vùng chạm 44px, safe-area và reduced motion theo mẫu duyệt. Không sửa Razor body/partial, renderer, controller/service/repository/DTO, lệnh ghi hoặc cơ chế giá vốn/nhà cung cấp.

## Điểm R05 và quyền

**UI-14d đã xác minh nguồn, chưa sửa route độc lập:** BarcodeManagerController có GET /admin/barcode-manager/{conversionId:int}, kiểm tra Catalog.Barcode.View và đọc header theo StoreId hiện tại, nhưng trả View(vm) khi không có Views/BarcodeManager/Index.cshtml. Shared/_BarcodeManager là partial chưa có nơi gọi; changeBarcodeForm và nút đổi mã chưa có JavaScript owner. Không đưa partial chưa hoạt động ra màn hình hoặc kết luận trang độc lập đã sửa.

Điểm vào hoạt động được giữ và kiểm tra là **Sửa sản phẩm → quy cách → Đơn vị & Barcode → đơn vị đã chọn → barcode/lịch sử**. Luồng đọc qua ProductUnitConversion/GetByVariantId và GetBarcodeHistoryByConversionId. Controller giữ Product.View; lưu quy đổi cần Product.Update, lưu barcode cần đồng thời Barcode.Create/Barcode.Update và antiforgery. Lịch sử/chuẩn hóa giữ Barcode.View; duyệt/từ chối/xác nhận không mã giữ Barcode.Update ở máy chủ.

Menu tiếp tục lấy từ AdminMenuItems đúng cửa hàng, quyền, trạng thái và thứ tự. Không thêm menu/ghim, quyền mặc định, seeder/migration/schema hoặc cấu hình cửa hàng. Adapter không fetch/storage, clone form, ghi giá trị trường hay chuyển tiếp submit/click.

## Kiểm tra tự động

<!-- UI14_RUNTIME_RESULTS_START -->
- **101/101 test .NET duy nhất qua**,0 lỗi/0 bỏ qua; 104 lượt thực thi, các lượt chạy lại không cộng trùng. Hai lượt acceptance đầu phát hiện lỗi trong test mới: kỳ vọng 404 thay vì redirect 302 của controller gốc và fixture bỏ RequestType khiến không có nút duyệt. Đã sửa kỳ vọng/fixture theo owner gốc rồi chạy lại qua; không sửa nghiệp vụ để ép kết quả.
- Probe giá đơn vị sản phẩm gốc qua. Native browser kiểm tra tìm SKU/barcode/thương hiệu, giá lẻ/sỉ, đơn vị hệ số 1/4/24, fallback theo giá lẻ và quyền riêng tư nhà cung cấp.
- **26 trạng thái, 182 lượt phone, 52 đối chiếu desktop, 45 lượt hộp thoại.** Phone 320×568,360×780,390×844,430×932,600×900,767×900,390×440; desktop 768/1440px khớp computed CSS và hình học khi tắt stylesheet mới. Không tràn ngang, ID trùng, field drift hoặc lệnh ghi do resize.
- Sáu danh mục qua tìm kiếm gốc, hủy xóa, validation Name bắt buộc, Code khóa khi sửa và RowVersion. Quy cách/đơn vị/barcode mở đúng bản ghi; bộ chọn ảnh native mở từ ảnh trống, tải dữ liệu gốc rồi hủy mà không chọn/xóa ảnh; modal chuẩn hóa mở đúng yêu cầu rồi hủy.
- HTTP/SQL giữ quyền xem/tạo/sửa/duyệt barcode và phạm vi cửa hàng. Fingerprint sản phẩm, quy cách, giá, đơn vị, barcode/lịch sử/yêu cầu, menu, quyền, settings, tồn và cửa hàng khác khớp; không ghi nghiệp vụ trong các luồng đọc/hủy/xác thực.
- Release build 0 cảnh báo/0 lỗi; sáu asset gzip khớp byte nguồn. Snapshot 3.534 đường dẫn, 3.513 file bảo vệ, 27 nguồn nghiệm thu; baseline 560 file giữ nguyên: 450 không đổi/110 đã đổi/0 mất.
<!-- UI14_RUNTIME_RESULTS_END -->

## Rà ảnh và môi trường

<!-- UI14_VISUAL_RESULTS_START -->
Root đã xem 14 ảnh cuối. Đã xem ảnh cuối của danh sách/sửa sản phẩm, sheet quy cách, bộ chọn ảnh, editor quy đổi/barcode, danh mục nền, thuộc tính, lịch sử/chuẩn hóa barcode và các hộp xác nhận, cùng popup desktop. Nội dung và giá/hệ số rõ; trường nhập, footer và nút đóng nằm trong khung. Nút đầu trang dùng màu lam, ảnh trống có nhãn chọn ảnh và giữ thao tác native; bộ chọn ảnh nằm trên sheet chi tiết. Desktop giữ bố cục và màu gốc. Kết hợp với ma trận số đo tự động; chưa thay cho kiểm tra thiết bị Android/iOS và máy quét thật.
<!-- UI14_VISUAL_RESULTS_END -->

Kiểm thử dùng Web/LocalDB GUID và Chrome headless/profile mới thuộc fixture; tắt launcher trình duyệt mặc định, chặn nguồn ngoài và thay thao tác in/mở cửa sổ. Các cửa sổ/tab Chrome đang mở và database/cấu hình thật được giữ nguyên.

Android/iOS thật, bàn phím, safe-area, zoom, xoay màn hình, thao tác một tay và camera/máy quét vẫn chờ. Mô phỏng viewport không thay nghiệm thu thiết bị. Các điểm chờ trước đây và lỗi R05 độc lập tiếp tục được ghi nhận.

## Bằng chứng

Snapshot/log tại .artifacts/ui-14; TRX/ảnh/SQL tại TestResults/mobile-ui-14-20261026. [Source](mobile-ui-20261025/evidence/ui-14/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-14/baseline-delta.json), [asset nén](mobile-ui-20261025/evidence/ui-14/asset-bundle.json), [runtime](mobile-ui-20261025/evidence/ui-14/runtime-review.json), [ma trận](mobile-ui-20261025/evidence/ui-14/acceptance-matrix.json), [rà ảnh](mobile-ui-20261025/evidence/ui-14/visual-inspection.json). Tiếp **UI-15a — khách hàng**, không tự tạo menu hoặc quyền khi chuyển bước.
