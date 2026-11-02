# UI-13 — Tra cứu tồn, thẻ kho, kho và hộ kinh doanh trên điện thoại

Ngày 26/10/2026. Đã triển khai và hoàn tất kiểm tra tự động UI-13a/b/c/d trên cả năm trang hiện có. Còn nghiệm thu thiết bị thật. Bước tiếp theo: UI-14a — danh sách và chi tiết sản phẩm.

## Giao diện đã triển khai

- **UI-13a — Tra cứu tồn kho:** `InventoryInquiry/Index` dùng thẻ điện thoại, ô tìm kiếm rõ, bộ lọc và xem nhanh gốc. Tồn thực, giữ hàng, sẵn có và đơn vị giữ số liệu/nhãn do renderer và dịch vụ hiện hành cung cấp. Kho, sản phẩm, quy cách, SKU/barcode và trạng thái bộ lọc vẫn do chủ xử lý gốc quản lý. Không bổ sung một bộ quét hoặc phép tính tồn mới trong adapter. Phần giá vốn chỉ trình bày khi dữ liệu gốc và quyền máy chủ cho phép.
- **UI-13b — Thẻ kho:** `InventoryLedger/Index` nâng bố cục tìm kiếm, kho/kỳ/loại phát sinh, danh sách và xem nhanh trên điện thoại. Thẻ giữ chiều tăng/giảm, số lượng, đơn vị, số trước/sau và bằng chứng nguồn do dữ liệu gốc trả về; không tính lại số dư hoặc giá trị. Bộ lọc ngày, mã tham chiếu, receipt/XML và phân trang tiếp tục dùng luồng native.
- **UI-13c — Quản lý kho, hộ kinh doanh và đối soát:** `WarehouseManagement/Index` dùng KPI hai cột, thẻ kho và biểu mẫu gốc trong sheet có body cuộn/footer cố định. Các bộ lọc hiện có được đặt vào một `details` chỉ trên điện thoại; chính node gốc được giữ, không clone, giữ giá trị và listener. Khi trở lại desktop, adapter đặt từng node về đúng vị trí gốc rồi xóa wrapper/comment trình bày. `LegalEntityManagement/Index` dùng các dòng bảng vật lý thành thẻ có nhãn; form HKD một cột, chia nhóm nhận diện, pháp lý, kho/hóa đơn và thiết lập nhập hàng. `LegalEntityReconciliation/Index` có bộ lọc một cột, KPI hai cột, thẻ tổng hợp theo HKD và thẻ đối soát theo đơn; vẫn giữ liên kết đơn nguồn và nút xuất theo quyền gốc.
- **UI-13d — Phạm vi và dữ liệu biên:** giữ phạm vi cửa hàng/kho, đơn vị/quy cách, nguồn nhập và giá vốn theo chủ dữ liệu hiện hành. Ca nghiệm thu đã đối chiếu tài khoản quản trị, người có quyền nghiệp vụ nhưng không có vai trò xem giá vốn, người chỉ xem, thiếu quyền và cửa hàng khác; các luồng nhập, xuất, giữ hàng và ghi sổ không được chuyển thành tác vụ của adapter. Danh sách dài và tìm kiếm được kiểm tra cùng các ca đọc tồn hiện có; kết quả cuối đã ghi bên dưới sau lượt chạy độc lập.

Năm Razor view chỉ thêm imports của ba cặp asset mới: `inventory-lookup.mobile.css/js`, `warehouse-management.mobile.css/js` và `legal-entities.mobile.css/js`. Nội dung Razor gốc, form, renderer, endpoint và script nghiệp vụ giữ nguyên. CSS mới chỉ có phạm vi dưới 768px, theo mẫu navy `#172d4d`, lam `#2463eb`, nền `#f3f6fb`, trường nhập 16px/vùng chạm tối thiểu 44px, safe-area và reduced motion. Hộp thoại gốc giữ nút đóng trong header, một body cuộn và footer không co; Select2 được giới hạn cả wrapper và dropdown.

Các adapter gắn thuộc tính trình bày `data-inventory-lookup-phone`/`data-lookup-phone-*`, `data-warehouse-phone-*`, `data-legal-phone-*` và khôi phục chúng khi chiều rộng từ 768px trở lên. Thuộc tính `role` thêm cho bảng thẻ cũng được khôi phục đúng giá trị ban đầu. Không ghi giá trị trường, thay `required`, `hidden`, `disabled`, `readOnly`, tên/type/ID, chủ form hoặc validation; không clone form, thêm fetch/storage, tính lượng/giá hay chuyển tiếp lệnh ghi. Riêng wrapper bộ lọc kho được tháo và vị trí node gốc được phục hồi khi về desktop.

## Giữ đúng nguồn, quyền và cấu hình

**Nguồn thẻ kho có giới hạn dữ liệu cần ghi rõ:** source hiện có trả `ReferenceCode` dạng chuỗi, `ReceiptDocumentNo` và thông tin XML dạng văn bản; DTO không có `SourceUrl` hoặc `ReferenceEntityId` để định tuyến chắc chắn tới chứng từ nguồn. UI-13 giữ các bằng chứng văn bản gốc, không tự suy đoán loại chứng từ từ mã, tạo URL nguồn hoặc biến văn bản thành liên kết giả. Phần mở nguồn tổng quát chưa được triển khai vì chưa có hợp đồng dữ liệu tương ứng; không tạo liên kết giả trong đợt giao diện. Summary hiện chỉ đếm tổng/tăng/giảm/âm; BeforeQty/QuantityChange/AfterQty là số lưu trên từng giao dịch, chưa có tổng tồn đầu/cuối kỳ. Không tính suy ra số dư kỳ từ dữ liệu phân trang. Báo cáo đối soát HKD có sẵn liên kết `/admin/pos/order-detail/{OrderId}`; liên kết này được giữ nguyên.

Giữ `Inventory.Balance.View`, `Inventory.Transaction.View`, `Inventory.Warehouse.View`, `System.LegalEntity.View/Create/Update/Activate/Reconcile` và `Report.Sales.Export` ở controller/service. **Xem giá vốn của hai trang tra cứu dựa vào vai trò hệ thống ADMIN thuộc đúng cửa hàng trong cơ sở dữ liệu; quyền `*` riêng lẻ không cấp quyền xem giá vốn.** Adapter không thay quy tắc này, không tin query client để mở giá và không bật lại phần dữ liệu bị chủ xử lý gốc ẩn.

Tạo/sửa/kích hoạt hoặc khóa HKD, cấu hình kho mặc định, HKD nhập hàng mặc định, hóa đơn, trạng thái hoạt động, preflight, canary và kill switch tiếp tục do luồng native quyết định. Việc hiển thị/chạm vào switch không được adapter biến thành kích hoạt feature. Ca thử dùng bản ghi riêng trong fixture; chỉ thao tác ghi chú được ủy quyền trong dữ liệu thử, không kích hoạt Multi LegalEntity hoặc đổi ưu tiên/mặc định để phục vụ trình bày. Đối soát đọc allocation hiện có trong đúng cửa hàng; không suy diễn lại đơn legacy trước ngày kích hoạt.

Menu vẫn lấy từ `AdminMenuItems` đúng cửa hàng, quyền, trạng thái và thứ tự đã cấu hình. Không thêm menu/ghim, tạo dữ liệu menu bằng seeder/migration/script, sửa sidebar, quyền, cấu hình cửa hàng, schema, controller/service/repository/DTO hoặc nghiệp vụ gốc.

## Bảo vệ source và build

Lần rà tĩnh hiện tại ghi nhận snapshot **3.518 đường dẫn**, **3.510 file được bảo vệ** và **14 file nguồn nghiệm thu**. Năm view tương đương bản trước sau khi bỏ đúng imports mới; sáu asset phone mới được theo dõi riêng. Ba file kiểm thử/quan sát phục vụ nghiệm thu được ghi trong bằng chứng nguồn. Baseline 560 file giữ nguyên danh mục: **468 không đổi /92 đã đổi qua các bước /0 mất**. Không cập nhật baseline để hợp thức hóa thay đổi.

Release build đã qua **0 cảnh báo, 0 lỗi**. Probe CLI `ledger-search.browser.cjs` gốc đã qua. Sáu asset gzip khớp byte source cuối; kết quả runtime và rà ảnh dưới đây đã hoàn tất.

## Kết quả kiểm tra runtime

<!-- UI13_RUNTIME_RESULTS_START -->
- **47/47 test .NET duy nhất qua**,0 lỗi/0 bỏ qua. Broad 46 ca có 45 qua/1 SQL timeout; lượt độc lập chạy lại đúng ca lớn qua và acceptance mới qua. Gộp theo testId ổn định, không cộng lặp ca chạy lại.
- Probe CLI sổ kho gốc qua: tìm tên không dấu, barcode, phạm vi ghi chú/tham chiếu/Tất cả, phân trang, rỗng, hủy phản hồi cũ và báo timeout theo owner gốc. Không sửa probe/runner gốc.
- **17 trạng thái**, **119 lượt phone**, **34 đối chiếu desktop**, **30 lượt hộp thoại**. Bảy cấu hình 320×568,360×780,390×844,430×932,600×900,767×900,390×440; desktop 768/1440px khớp CSS/hình học khi tắt stylesheet mới. Không tràn ngang/ID trùng/field drift, không có lệnh ghi do resize; font nhập≥16px/touch≥44px, footer và nút đóng trong khung.
- HTTP/SQL xác nhận tồn thực 105/giữ 12/khả dụng 93; đơn vị hệ số 4/24 và barcode số giữ nguyên. Receipt gốc100/+8/108, giảm 3 còn 105, mã NK-UI13-SOURCE đúng. Giá vốn chỉ ADMIN hệ thống đúng cửa hàng; quyền* riêng lẻ và query giả không mở được giá. Thiếu quyền 403, khác cửa hàng 404/rỗng, thiếu CSRF 400; người chỉ xem không có thao tác quản lý/xuất.
- Form quản lý kho và HKD chỉ đổi đúng hai ghi chú trong fixture. Feature vẫn tắt; mã/owner kho bị khóa, ưu tiên 2, mặc định kho/HKD/hóa đơn và tất cả settings/roles/catalog/tồn/phát sinh/đơn/allocations/menu/cửa hàng khác giữ scalar snapshot. Native đối soát lọc đúng đơn, phân bổ 120, chênh lệch 0 và mở đúng liên kết đơn gốc.
- Dữ liệu lớn **1.500.047 phát sinh**: lượt chạy song song timeout SQL; chạy riêng cùng test/query/dataset/timeout đã qua. Tìm theo sản phẩm có từ khóa khoảng 6–124ms; các lượt Tất cả khoảng 14.652–33.987ms. Đây là độ trễ đọc dữ liệu hiện hành, không kết luận đã tối ưu hiệu năng. Mọi thời gian được giữ trong runtime evidence/TRX.
- Build 0 cảnh báo/0 lỗi; 6 asset nén khớp nguồn;3.510 file bảo vệ/14 nguồn nghiệm thu,5 view chỉ thêm imports; baseline 468/92/0.
<!-- UI13_RUNTIME_RESULTS_END -->

## Rà ảnh và môi trường kiểm thử

<!-- UI13_VISUAL_RESULTS_START -->
Root đã xem 12 ảnh cuối. Tra cứu tồn hiển thị rõ tồn thực/giữ hàng/khả dụng 105/12/93, bản chỉ xem không có giá vốn. Xem nhanh thẻ kho giữ số trước100/biến động+8/sau108 và nguồn gốc. Thẻ kho và HKD có nhãn, nội dung dài xuống dòng; bộ lọc kho thu gọn, form kho/HKD cuộn trong thân, nút đóng và footer nằm trong khung. Form pháp lý chia nhóm; trạng thái tính năng tắt và các lựa chọn bị khóa giữ nguyên. Đối soát hiển thị phân bổ 120, chênh lệch 0 và liên kết đơn gốc; bản chỉ xem không có nút xuất. Không thấy lỗi bố cục trong các ảnh đã rà. Ảnh full-page có thể ghi thanh shell cố định tại vị trí chụp; đã đối chiếu ảnh viewport và số đo thực. Thiết bị Android/iOS thật vẫn chờ.
<!-- UI13_VISUAL_RESULTS_END -->

Các lượt chạy sử dụng Web/LocalDB GUID và Chrome headless/profile mới thuộc fixture; launcher trình duyệt mặc định được tắt. Không đóng hoặc thay đổi các cửa sổ/tab Chrome đang mở của người dùng, không dùng database/cấu hình cửa hàng thật làm dữ liệu thử. Kiểm tra browser chặn nguồn ngoài và thay thao tác in/mở cửa sổ trong fixture; không thực hiện phát hành hóa đơn hoặc kích hoạt canary thật.

Còn nghiệm thu **Android/iOS thật**, bàn phím, safe-area, zoom, xoay màn hình và thao tác một tay; camera/máy quét phần cứng vẫn thuộc nghiệm thu thiết bị của toàn kế hoạch. Mô phỏng viewport không thay kiểm tra trên thiết bị. Các trường hợp nghiệm thu còn mở của các bước trước tiếp tục được giữ.

## Bằng chứng và bước tiếp theo

Snapshot/log tại `.artifacts/ui-13`; TRX/ảnh/SQL tại `TestResults/mobile-ui-13-20261026`. Bằng chứng đã có: [rà source](mobile-ui-20261025/evidence/ui-13/static-review.json), [baseline](mobile-ui-20261025/evidence/ui-13/baseline-delta.json), [asset nén](mobile-ui-20261025/evidence/ui-13/asset-bundle.json). [Runtime](mobile-ui-20261025/evidence/ui-13/runtime-review.json), [ma trận nghiệm thu](mobile-ui-20261025/evidence/ui-13/acceptance-matrix.json), [rà ảnh](mobile-ui-20261025/evidence/ui-13/visual-inspection.json).

UI-13 đã hoàn tất kiểm tra tự động; bước tiếp theo là **UI-14a — danh sách và chi tiết sản phẩm**. Không thêm mục menu hoặc quyền mặc định khi chuyển bước.
