# Danh mục view và bố cục điện thoại GaoApp

Ngày chốt kiểm kê 25/10/2026. Danh mục bao phủ toàn bộ file Razor dưới `GaoApp.Web/Areas` và `GaoApp.Web/Views` trong working tree, kèm tài nguyên CSS và JavaScript tự quản lý. Mỗi file có một bước triển khai chính và một bố cục đích. Các màn dùng chung một workspace được liệt kê thêm ở phần luồng động.

Kế hoạch thực hiện: [Nâng cấp toàn bộ giao diện](MOBILE-UI-UPGRADE-PLAN-20261025.md). Dữ liệu đầy đủ gồm đường dẫn, SHA256, controller, route template, layout, partial, trường nhập, nút, CSS và JS: [view-inventory.json](mobile-ui-20261025/view-inventory.json).

Cập nhật thực hiện UI-01a–b: [bộ nền Commerce và trang xem thử](MOBILE-UI-01-FOUNDATION-20261025.md), [bổ sung so sánh thiết bị và thanh menu điện thoại](MOBILE-UI-DEVICE-COMPARISON-20261025.md). Danh mục bên dưới và JSON là baseline trước triển khai. Người dùng đã duyệt v2; [UI-02a–b](MOBILE-UI-02AB-NAVIGATION-20261025.md) đã đổi partial menu/layout thật. Tổng phần bổ sung hiện là 10 Razor/11 asset; trạng thái và hash sau thay đổi theo [implementation-progress](mobile-ui-20261025/implementation-progress.json), không ghi đè baseline.

## Thống kê phạm vi

| Loại | Số file |
|---|---:|
| Trang nhúng | 1 |
| Layout | 7 |
| Trang | 144 |
| Partial | 85 |
| Bản in | 8 |
| Cấu hình Razor | 4 |
| Màn chuyên dụng | 3 |
| Trang trạng thái | 6 |
| Component | 2 |
| **Tổng** | **260** |

Có 248 file Razor thuộc Admin và 12 file ở Views công khai hoặc khung gốc. 162 file thuộc nhóm trang, bản in, màn chuyên dụng, trạng thái hoặc vùng nhúng; con số này là số file, không phải số màn nghiệp vụ đang hoạt động. Một workspace có thể phục vụ nhiều route và nhiều trạng thái.

Có 300 tài nguyên CSS hoặc JS tự quản lý ngoài `lib`, `vendor`, `uploads`: 188 JS và 112 CSS. Trong đó 139 JS có dấu hiệu dựng DOM động. Số tài nguyên là phạm vi rà soát, không đồng nghĩa tất cả đều đang được tải hoặc cần viết lại.

## Quy tắc đọc danh mục

- Mỗi ID `Vxxx` chỉ một file trong lần kiểm kê này; đường dẫn và SHA256 trong JSON là khóa đối chiếu khi source thay đổi.
- Các bước `UI-01` đến `UI-23` là chủ sở hữu thay đổi; `UI-24` nghiệm thu tổng thể. Thành phần dùng chung có thể ảnh hưởng nhiều bước.
- `Tham chiếu trực tiếp` chỉ có nghĩa source controller có lời gọi View hoặc PartialView phân giải được. Route và trạng thái chạy thực tế được xác nhận khi bắt đầu từng bước.
- `Rà sử dụng` là ứng viên cần kiểm tra thêm, không phải quyết định xóa hoặc một kết luận file không dùng.
- File bản in và màn chuyên dụng vẫn nằm trong phạm vi đánh giá; giữ khổ in và mục đích thiết bị.

## Bộ mẫu bố cục

| Mã | Mẫu |
|---|---|
| T01 | Danh sách |
| T02 | Chi tiết chứng từ |
| T03 | Biểu mẫu |
| T04 | Tác nghiệp quét và số lượng |
| T05 | POS |
| T06 | Báo cáo |
| T07 | Hộp thư |
| T08 | Cây và phân quyền |
| T09 | Cài đặt |
| T10 | Media |
| T11 | In hoặc trình chiếu |
| T12 | Trang trạng thái |
| T13 | Khung và thành phần dùng chung |

## Các điểm cần xử lý trước hoặc trong bước tương ứng

| Mã | Bước | Điểm cần xử lý | Bằng chứng |
|---|---|---|---|
| R01 | UI-12 | Route điều chỉnh cũ chuyển hướng sang InventoryAdjustmentDocuments. File view cũ cần xác minh sử dụng trước khi quyết định bảo trì hoặc loại bỏ. | [InventoryAdjustmentController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryAdjustmentController.cs) |
| R02 | UI-15 | Create của SupplierController dùng view Edit cho cả GET và lỗi POST. Không coi Create.cshtml là một màn tạo đang được route sử dụng. | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) |
| R03 | UI-22 | PrintReceipt trong POSController trả ReceiptTemplates/Print.cshtml. Đối chiếu bản in cũ trước thay thế; sửa ưu tiên luồng in thực tế. | [POSController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSController.cs) |
| R04 | UI-22 | POSReceiptController trả ReceiptTemplates/Print.cshtml hoặc JSON; Index.cshtml là ứng viên cần rà lại sử dụng. | [POSReceiptController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSReceiptController.cs) |
| R05 | UI-14 | BarcodeManagerController.Index trả View(vm), nhưng không có BarcodeManager/Index.cshtml trong kiểm kê. Kiểm tra route này và cơ chế resolve view trước triển khai; không tự tạo menu mới. | [BarcodeManagerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BarcodeManagerController.cs) |
| R06 | UI-03 | Layout cũ chứa menu viết cố định và chưa tìm thấy tham chiếu trực tiếp. Khung mới dùng renderer theo AdminMenuItems; xác minh nơi dùng trước dọn layout. | [_AdminLayout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_AdminLayout.cshtml) |
| R07 | UI-02 | Thanh điều hướng POS và trang Home có liên kết chức năng viết cố định. Hợp nhất điều hướng chung với cấu hình menu; giữ nút tác nghiệp trong ngữ cảnh đúng quyền. | [_POSNavTabs.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_POSNavTabs.cshtml) |
| R08 | UI-12 | CSS mobile của bảng chi tiết kiểm kê vẫn có min-width 980px; chuyển danh sách tác nghiệp sang dòng theo quy cách, giữ bảng đối chiếu khi cần. | [stock-count.css](../../GaoApp.Web/wwwroot/Admin/css/stock-count.css) |
| R09 | UI-23 | Controller nằm trong thư mục Admin nhưng namespace và route là công khai /invoice-info/{token}. Thiết kế theo ngữ cảnh khách hàng; không đặt trong khung quản trị. | [InvoiceBuyerSelfServiceController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceBuyerSelfServiceController.cs) |
| R10 | UI-05 | Luồng nhận hàng hiện có command ID, thử lại và giới hạn online. Demo chỉ minh họa bố cục; tổng đơn vị khác nhau và trạng thái lưu phải theo source thực tế. | [WAREHOUSE-RECEIVING-MOBILE-20260912.md](../../docs/development/WAREHOUSE-RECEIVING-MOBILE-20260912.md) |

## Toàn bộ file Razor theo bước

### UI-01 Nền thiết kế và bộ thành phần

2 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V204 | [Areas/Admin/Views/Shared/Sections/_Styles.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/_Styles.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V205 | [Areas/Admin/Views/Shared/Sections/_Variables.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/_Variables.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |

Tài nguyên giao cho bước này để rà soát: 1 file (0 JS, 1 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-02 Menu và mục ghim theo cấu hình

7 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V014 | [Admin/AdminMenus/_MenuPermissionSelector.cshtml](../../GaoApp.Web/Areas/Admin/Views/AdminMenus/_MenuPermissionSelector.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V015 | [Admin/AdminMenus/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/AdminMenus/Create.cshtml) | Trang | T03 · Biểu mẫu AdminMenus: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V016 | [Admin/AdminMenus/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/AdminMenus/Edit.cshtml) | Trang | T03 · Biểu mẫu AdminMenus: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V017 | [Admin/AdminMenus/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AdminMenus/Index.cshtml) | Trang | T08 · Cây menu theo cấu hình; tìm mục; chọn mục rồi sửa ở trang riêng; xem trước điện thoại. | Tham chiếu trực tiếp |
| V018 | [Admin/AdminMenuVisibility/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AdminMenuVisibility/Index.cshtml) | Trang | T08 · Chọn nhóm hoặc nhân viên trước; cây ẩn hiện ở bước kế tiếp; xem trước và lưu rõ phạm vi. | Tham chiếu trực tiếp |
| V187 | [Areas/Admin/Views/Shared/_POSNavTabs.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_POSNavTabs.cshtml) | Partial | T13 · Hợp nhất điều hướng chức năng với menu được cấu hình; giữ tác vụ mở màn khách trong ngữ cảnh POS. | Dùng chung hoặc component; R07 |
| V207 | [Areas/Admin/Views/Shared/Sections/Menu/_VerticalMenu.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/Menu/_VerticalMenu.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |

Tài nguyên giao cho bước này để rà soát: 6 file (2 JS, 4 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-03 Khung ứng dụng và trang nền

25 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V001 | [Admin/_Partials/_Macros.cshtml](../../GaoApp.Web/Areas/Admin/Views/_Partials/_Macros.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V002 | [Areas/Admin/Views/_ViewImports.cshtml](../../GaoApp.Web/Areas/Admin/Views/_ViewImports.cshtml) | Cấu hình Razor | T13 · Giữ imports và kế thừa layout; kiểm tra phạm vi Admin hoặc công khai; không chèn menu chức năng cố định. | Quy ước Razor |
| V003 | [Areas/Admin/Views/_ViewStart.cshtml](../../GaoApp.Web/Areas/Admin/Views/_ViewStart.cshtml) | Cấu hình Razor | T13 · Giữ imports và kế thừa layout; kiểm tra phạm vi Admin hoặc công khai; không chèn menu chức năng cố định. | Quy ước Razor |
| V012 | [Admin/Account/AccessDenied.cshtml](../../GaoApp.Web/Areas/Admin/Views/Account/AccessDenied.cshtml) | Trang trạng thái | T12 · Thông báo tiếng Việt gọn; lý do và hành động quay lại rõ; chỉ hiện chi tiết kỹ thuật được phép. | Tham chiếu trực tiếp |
| V013 | [Admin/Account/Login.cshtml](../../GaoApp.Web/Areas/Admin/Views/Account/Login.cshtml) | Trang | T02 · Đăng nhập một cột; trường nhập và lỗi rõ; bàn phím không che nút; giữ nhận diện cửa hàng. | Tham chiếu trực tiếp |
| V058 | [Admin/Error/Error.cshtml](../../GaoApp.Web/Areas/Admin/Views/Error/Error.cshtml) | Trang trạng thái | T12 · Thông báo tiếng Việt gọn; lý do và hành động quay lại rõ; chỉ hiện chi tiết kỹ thuật được phép. | Tham chiếu trực tiếp |
| V059 | [Admin/Home/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Home/Index.cshtml) | Trang | T01 · Tóm tắt theo quyền và dữ liệu thật; việc cần xử lý; lối tắt chỉ từ menu được cấu hình. | Tham chiếu trực tiếp |
| V179 | [Areas/Admin/Views/Shared/_AcbCallbackHealth.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_AcbCallbackHealth.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Tham chiếu trực tiếp |
| V180 | [Areas/Admin/Views/Shared/_AdminLayout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_AdminLayout.cshtml) | Layout | T13 · Khung kế thừa theo mục đích; header, menu và dock có chủ sở hữu; giữ section và chỉ tải script một lần. | Rà sử dụng; R06 |
| V182 | [Areas/Admin/Views/Shared/_BlankLayout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_BlankLayout.cshtml) | Layout | T13 · Khung kế thừa theo mục đích; header, menu và dock có chủ sở hữu; giữ section và chỉ tải script một lần. | Rà sử dụng |
| V183 | [Areas/Admin/Views/Shared/_CommonMasterLayout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_CommonMasterLayout.cshtml) | Layout | T13 · Khung kế thừa theo mục đích; header, menu và dock có chủ sở hữu; giữ section và chỉ tải script một lần. | Dùng chung hoặc component |
| V184 | [Areas/Admin/Views/Shared/_ContentNavbarLayout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ContentNavbarLayout.cshtml) | Layout | T13 · Khung kế thừa theo mục đích; header, menu và dock có chủ sở hữu; giữ section và chỉ tải script một lần. | Dùng chung hoặc component |
| V185 | [Areas/Admin/Views/Shared/_Layout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_Layout.cshtml) | Layout | T13 · Khung kế thừa theo mục đích; header, menu và dock có chủ sở hữu; giữ section và chỉ tải script một lần. | Dùng chung hoặc component |
| V188 | [Areas/Admin/Views/Shared/_POSPrimeLayout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_POSPrimeLayout.cshtml) | Layout | T13 · Khung kế thừa theo mục đích; header, menu và dock có chủ sở hữu; giữ section và chỉ tải script một lần. | Dùng chung hoặc component |
| V189 | [Areas/Admin/Views/Shared/_POSWorkContextBar.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_POSWorkContextBar.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V197 | [Areas/Admin/Views/Shared/_ToastMessages.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ToastMessages.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V198 | [Areas/Admin/Views/Shared/_ValidationScriptsPartial.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ValidationScriptsPartial.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V200 | [Areas/Admin/Views/Shared/Components/EmployeeAccount/Default.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Components/EmployeeAccount/Default.cshtml) | Component | T13 · Avatar mở hồ sơ và tiện ích tài khoản; bố cục một cột; xác nhận và đăng xuất theo luồng hiện có. | Dùng chung hoặc component |
| V201 | [Areas/Admin/Views/Shared/Error.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Error.cshtml) | Trang trạng thái | T12 · Thông báo tiếng Việt gọn; lý do và hành động quay lại rõ; chỉ hiện chi tiết kỹ thuật được phép. | Rà sử dụng |
| V202 | [Areas/Admin/Views/Shared/Sections/_Scripts.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/_Scripts.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V203 | [Areas/Admin/Views/Shared/Sections/_ScriptsIncludes.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/_ScriptsIncludes.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V206 | [Areas/Admin/Views/Shared/Sections/Footer/_Footer.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/Footer/_Footer.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V208 | [Areas/Admin/Views/Shared/Sections/Navbar/_AdminToolbar.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/Navbar/_AdminToolbar.cshtml) | Partial | T13 · Header gọn với tài khoản và ngữ cảnh; đối chiếu nơi sử dụng trước hợp nhất hoặc thay thế. | Dùng chung hoặc component |
| V209 | [Areas/Admin/Views/Shared/Sections/Navbar/_Navbar.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/Navbar/_Navbar.cshtml) | Partial | T13 · Header gọn với tài khoản và ngữ cảnh; đối chiếu nơi sử dụng trước hợp nhất hoặc thay thế. | Rà sử dụng |
| V210 | [Areas/Admin/Views/Shared/Sections/Navbar/_NavbarPartial.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/Navbar/_NavbarPartial.cshtml) | Partial | T13 · Header gọn với tài khoản và ngữ cảnh; đối chiếu nơi sử dụng trước hợp nhất hoặc thay thế. | Dùng chung hoặc component |

Tài nguyên giao cho bước này để rà soát: 22 file (17 JS, 5 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-04 Danh sách và chi tiết đơn POS

2 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V125 | [Admin/POSOrderDetail/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSOrderDetail/Index.cshtml) | Trang | T02 · Tổng tiền và trạng thái trước; Hàng hóa, Thanh toán, Lịch sử; thanh thu tiền theo quyền. | Tham chiếu trực tiếp |
| V126 | [Admin/POSOrderPage/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSOrderPage/Index.cshtml) | Trang | T01 · Tìm mã hoặc khách; thẻ trạng thái và tiền; lọc dưới đáy; mở đơn giữ nguyên bộ lọc. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 4 file (3 JS, 1 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-05 Nhập hàng tại cửa hàng

6 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V243 | [Admin/WarehouseReceiving/_MobileCapture.cshtml](../../GaoApp.Web/Areas/Admin/Views/WarehouseReceiving/_MobileCapture.cshtml) | Partial | T13 · Lựa chọn ngắn dùng bảng trượt; biểu mẫu dài toàn màn hình; giữ focus, hủy và dữ liệu đang nhập. | Dùng chung hoặc component |
| V244 | [Admin/WarehouseReceiving/_ReceivingApp.cshtml](../../GaoApp.Web/Areas/Admin/Views/WarehouseReceiving/_ReceivingApp.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V245 | [Admin/WarehouseReceiving/_ReceivingLinesTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/WarehouseReceiving/_ReceivingLinesTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V246 | [Admin/WarehouseReceiving/_ScanFeedback.cshtml](../../GaoApp.Web/Areas/Admin/Views/WarehouseReceiving/_ScanFeedback.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V247 | [Admin/WarehouseReceiving/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/WarehouseReceiving/Detail.cshtml) | Trang | T04 · Quét hoặc tìm; dòng vừa nhận; Hàng nhập và Thông tin; chỉ báo lưu và thử lại cùng command ID. | Tham chiếu trực tiếp; R10 |
| V248 | [Admin/WarehouseReceiving/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/WarehouseReceiving/Index.cshtml) | Trang | T01 · Danh sách phiếu theo kho và trạng thái; thẻ tiến độ; lọc gọn; tiếp tục phiếu đang nhận. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 11 file (6 JS, 5 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-06 Bán hàng POS

16 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V106 | [Admin/POS/_CartTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_CartTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Dùng chung hoặc component |
| V107 | [Admin/POS/_CockpitHeader.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_CockpitHeader.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V108 | [Admin/POS/_CustomerBox.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_CustomerBox.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V109 | [Admin/POS/_CustomerDisplayCustomer.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_CustomerDisplayCustomer.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V110 | [Admin/POS/_DeliveryModal.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_DeliveryModal.cshtml) | Partial | T13 · Lựa chọn ngắn dùng bảng trượt; biểu mẫu dài toàn màn hình; giữ focus, hủy và dữ liệu đang nhập. | Dùng chung hoặc component |
| V111 | [Admin/POS/_DraftMetaPanel.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_DraftMetaPanel.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V112 | [Admin/POS/_HeldOrdersPanel.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_HeldOrdersPanel.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V113 | [Admin/POS/_OrderDiscountBox.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_OrderDiscountBox.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Rà sử dụng |
| V114 | [Admin/POS/_OrderNoteBox.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_OrderNoteBox.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Rà sử dụng |
| V115 | [Admin/POS/_POSPrimeHeader.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_POSPrimeHeader.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V116 | [Admin/POS/_POSPrimeShell.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_POSPrimeShell.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V117 | [Admin/POS/_ScanFeedback.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_ScanFeedback.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V118 | [Admin/POS/_SummaryActionsPanel.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_SummaryActionsPanel.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V119 | [Admin/POS/_SummaryPanel.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_SummaryPanel.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V120 | [Admin/POS/_Toolbar.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_Toolbar.cshtml) | Partial | T13 · Khối POS trong cùng phiên giỏ; bố trí theo tác vụ; giữ ID, số tiền và sự kiện được gắn một lần. | Dùng chung hoặc component |
| V123 | [Admin/POS/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/Index.cshtml) | Trang | T05 · Tìm hoặc quét hàng; giỏ theo từng quy cách; khách và ưu đãi mở riêng; thanh thu tiền. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 33 file (24 JS, 9 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-07 Ca làm việc và bàn giao

6 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V122 | [Admin/POS/Dashboard.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/Dashboard.cshtml) | Trang | T02 · Dashboard POS: chỉ số bán hàng và ca trước; phần phân tích mở sâu; giữ phạm vi quầy và quyền. | Tham chiếu trực tiếp |
| V133 | [Admin/POSShiftHandoverSlipPage/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSShiftHandoverSlipPage/Index.cshtml) | Trang | T01 · Danh sách phiếu nhận ca; số bàn giao và chênh lệch rõ; mở phiếu và bản in riêng. | Tham chiếu trực tiếp |
| V135 | [Admin/POSShiftHistoryPage/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSShiftHistoryPage/Index.cshtml) | Trang | T01 · Thẻ ca theo ngày và thu ngân; lọc ngày; xem chi tiết và bàn giao không mất vị trí. | Tham chiếu trực tiếp |
| V136 | [Admin/POSShiftManagerDashboardPage/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSShiftManagerDashboardPage/Index.cshtml) | Trang | T01 · Các ca cần xử lý trước; thẻ theo người và quầy; duyệt trong chi tiết ca. | Tham chiếu trực tiếp |
| V137 | [Admin/POSShiftPage/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSShiftPage/Index.cshtml) | Trang | T01 · Ca hiện tại và quỹ tiền trước; mở nhận đóng ca theo trạng thái; số liệu đối chiếu mở riêng. | Tham chiếu trực tiếp |
| V138 | [Admin/POSShiftReconciliation/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSShiftReconciliation/Index.cshtml) | Trang | T01 · Chọn ca; tóm tắt chênh lệch; xem khoản lệch; xác nhận theo quy trình đối soát hiện hành. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 13 file (8 JS, 5 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-08 Nghiệp vụ sau bán và trả hàng

6 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V098 | [Admin/LegacyReturnArchive/Details.cshtml](../../GaoApp.Web/Areas/Admin/Views/LegacyReturnArchive/Details.cshtml) | Trang | T02 · Tóm tắt LegacyReturnArchive trước; thông tin, dòng chi tiết và lịch sử chia phần; thanh tác vụ theo trạng thái. | Tham chiếu trực tiếp |
| V099 | [Admin/LegacyReturnArchive/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/LegacyReturnArchive/Index.cshtml) | Trang | T01 · Tìm chứng từ trả hàng cũ; trạng thái liên kết trước; xem chứng từ và thao tác được phép. | Tham chiếu trực tiếp |
| V128 | [Admin/POSRequests/_CashPanel.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSRequests/_CashPanel.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V129 | [Admin/POSRequests/_PaymentPanel.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSRequests/_PaymentPanel.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V130 | [Admin/POSRequests/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSRequests/Index.cshtml) | Trang | T01 · Chọn loại yêu cầu; thẻ trạng thái; mở nội dung và số tiền; duyệt hoặc từ chối theo quyền. | Tham chiếu trực tiếp |
| V131 | [Admin/POSReturn/PendingRestock.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSReturn/PendingRestock.cshtml) | Trang | T01 · Danh sách hàng trả chờ nhập lại; chọn chứng từ; đối chiếu số lượng và xác nhận nhập lại. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 7 file (4 JS, 3 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-09 Soạn hàng và giao hàng

3 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V053 | [Admin/DeliveryPage/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/DeliveryPage/Index.cshtml) | Trang | T04 · Hàng cần soạn theo nguồn; nhập thực có từng quy cách; lưu rồi chốt theo revision. | Tham chiếu trực tiếp |
| V054 | [Admin/DeliveryPage/Outcomes.cshtml](../../GaoApp.Web/Areas/Admin/Views/DeliveryPage/Outcomes.cshtml) | Trang | T04 · Chọn điểm giao; số giao, thu và trả; bằng chứng theo từng đơn; chốt kết quả theo revision. | Tham chiếu trực tiếp |
| V055 | [Admin/DeliveryPage/Trips.cshtml](../../GaoApp.Web/Areas/Admin/Views/DeliveryPage/Trips.cshtml) | Trang | T02 · Thẻ chuyến theo người giao và trạng thái; danh sách đơn; thêm hàng hoặc điều phối qua chi tiết. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 8 file (4 JS, 4 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-10 Đề nghị mua và đơn mua

9 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V158 | [Admin/PurchaseOrders/CommercialEdit.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseOrders/CommercialEdit.cshtml) | Trang | T03 · Thông tin thương mại, chiết khấu và thuế theo nhóm; tổng đối chiếu; bảo vệ giá vốn theo quyền. | Tham chiếu trực tiếp |
| V159 | [Admin/PurchaseOrders/Details.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseOrders/Details.cshtml) | Trang | T02 · Đơn và nhà cung cấp trước; số đã nhận và còn chờ từng dòng; lịch nhận và chứng từ liên quan. | Tham chiếu trực tiếp |
| V160 | [Admin/PurchaseOrders/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseOrders/Edit.cshtml) | Trang | T03 · Biểu mẫu PurchaseOrders: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V161 | [Admin/PurchaseOrders/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseOrders/Index.cshtml) | Trang | T01 · Thẻ đơn mua với tiến độ nhận; lọc nhà cung cấp và trạng thái; nhận hàng từ đúng đơn nguồn. | Tham chiếu trực tiếp |
| V163 | [Admin/PurchaseReceiving/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseReceiving/Index.cshtml) | Trang | T04 · Chọn đơn mua và phiếu nhận; việc chưa hoàn thiện trước; thao tác nhận và duyệt tách rõ. | Tham chiếu trực tiếp |
| V164 | [Admin/PurchaseRequests/Details.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseRequests/Details.cshtml) | Trang | T02 · Đề nghị, trạng thái và người yêu cầu trước; hàng hóa; đơn mua liên quan; tác vụ theo trạng thái. | Tham chiếu trực tiếp |
| V165 | [Admin/PurchaseRequests/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseRequests/Edit.cshtml) | Trang | T03 · Biểu mẫu PurchaseRequests: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V166 | [Admin/PurchaseRequests/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseRequests/Index.cshtml) | Trang | T01 · Thẻ đề nghị theo người, nhà cung cấp, trạng thái; chọn phiếu và mở phần cần chuẩn bị. | Tham chiếu trực tiếp |
| V167 | [Admin/PurchaseRequests/Prepare.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseRequests/Prepare.cshtml) | Trang | T02 · Từng dòng cần chuẩn bị; nhà cung cấp và số lượng; tóm tắt trước tạo đơn mua theo luồng hiện có. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 18 file (9 JS, 9 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-11 Quản lý và duyệt phiếu nhập

15 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V060 | [Admin/InputInvoiceLibrary/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InputInvoiceLibrary/Index.cshtml) | Trang | T01 · Tìm XML theo nhà cung cấp, số hóa đơn, trạng thái liên kết; xem trước và chọn đúng chủ thể. | Tham chiếu trực tiếp |
| V191 | [Areas/Admin/Views/Shared/_ReceiptBarcodeProposals.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ReceiptBarcodeProposals.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V192 | [Areas/Admin/Views/Shared/_ReceiptDocumentActions.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ReceiptDocumentActions.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V193 | [Areas/Admin/Views/Shared/_ReceiptIntake.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ReceiptIntake.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V194 | [Areas/Admin/Views/Shared/_ReceiptIntakeReview.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ReceiptIntakeReview.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V195 | [Areas/Admin/Views/Shared/_ReceiptIntakeRows.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ReceiptIntakeRows.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V213 | [Admin/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml) | Partial | T13 · Khối phiếu nhập theo quyền và trạng thái; tách nội dung dài; giữ ràng buộc số lượng và giá vốn. | Dùng chung hoặc component |
| V214 | [Admin/StockDocumentManagement/_DocumentNote.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_DocumentNote.cshtml) | Partial | T13 · Khối phiếu nhập theo quyền và trạng thái; tách nội dung dài; giữ ràng buộc số lượng và giá vốn. | Dùng chung hoặc component |
| V215 | [Admin/StockDocumentManagement/_InputInvoicePickerModal.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_InputInvoicePickerModal.cshtml) | Partial | T13 · Lựa chọn ngắn dùng bảng trượt; biểu mẫu dài toàn màn hình; giữ focus, hủy và dữ liệu đang nhập. | Dùng chung hoặc component |
| V216 | [Admin/StockDocumentManagement/_InputInvoiceXmlPreview.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_InputInvoiceXmlPreview.cshtml) | Partial | T13 · Khối phiếu nhập theo quyền và trạng thái; tách nội dung dài; giữ ràng buộc số lượng và giá vốn. | Tham chiếu trực tiếp |
| V217 | [Admin/StockDocumentManagement/_ReceiptSellingPrices.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_ReceiptSellingPrices.cshtml) | Partial | T13 · Khối phiếu nhập theo quyền và trạng thái; tách nội dung dài; giữ ràng buộc số lượng và giá vốn. | Dùng chung hoặc component |
| V218 | [Admin/StockDocumentManagement/_StockDocumentLinesTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_StockDocumentLinesTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V219 | [Admin/StockDocumentManagement/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml) | Trang | T03 · Tóm tắt phiếu; Hàng nhận, Cần hoàn thiện, Thương mại, Chứng từ; tác vụ nhận và duyệt theo quyền. | Tham chiếu trực tiếp |
| V220 | [Admin/StockDocumentManagement/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Index.cshtml) | Trang | T01 · Danh sách phiếu nhập; lọc kho và nguồn; thẻ trạng thái; mở phiếu theo quyền nhận hoặc duyệt. | Tham chiếu trực tiếp |
| V221 | [Admin/StockDocumentManagement/Split.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Split.cshtml) | Trang | T03 · Chọn dòng và lượng cần tách; xem hai phiếu dự kiến; kiểm tra số lượng trước xác nhận hiện hành. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 30 file (21 JS, 9 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-12 Kiểm kê và chứng từ biến động kho

12 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V061 | [Admin/InventoryAdjustment/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryAdjustment/Index.cshtml) | Trang | T01 · Giữ chuyển hướng sang chứng từ điều chỉnh; chỉ đánh giá lại file cũ sau khi xác minh sử dụng. | Rà sử dụng; R01 |
| V062 | [Admin/InventoryAdjustmentDocuments/_AdjustmentDocumentForm.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryAdjustmentDocuments/_AdjustmentDocumentForm.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V063 | [Admin/InventoryAdjustmentDocuments/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryAdjustmentDocuments/Create.cshtml) | Trang | T03 · Chọn kho và lý do; thêm dòng điều chỉnh; kiểm tra lượng và tổng trước lưu nháp. | Tham chiếu trực tiếp |
| V064 | [Admin/InventoryAdjustmentDocuments/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryAdjustmentDocuments/Detail.cshtml) | Trang | T02 · Kho, lý do và trạng thái trước; lượng trước sau; duyệt hoặc từ chối theo luồng hiện hành. | Tham chiếu trực tiếp |
| V065 | [Admin/InventoryAdjustmentDocuments/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryAdjustmentDocuments/Edit.cshtml) | Trang | T03 · Sửa phần chứng từ còn cho phép; nhóm lý do và dòng hàng; hiển thị lỗi hoặc xung đột rõ. | Tham chiếu trực tiếp |
| V066 | [Admin/InventoryAdjustmentDocuments/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryAdjustmentDocuments/Index.cshtml) | Trang | T01 · Thẻ điều chỉnh theo kho và trạng thái; tạo phiếu qua biểu mẫu; duyệt trong chi tiết. | Tham chiếu trực tiếp |
| V068 | [Admin/InventoryIssueManagement/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryIssueManagement/Detail.cshtml) | Trang | T02 · Nguồn xuất, kho và trạng thái trước; dòng hàng; thông tin ghi sổ và hành động được phép. | Tham chiếu trực tiếp |
| V069 | [Admin/InventoryIssueManagement/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryIssueManagement/Index.cshtml) | Trang | T01 · Danh sách phiếu xuất với kho và nguồn; mở chứng từ; trạng thái ghi kho và hủy rõ. | Tham chiếu trực tiếp |
| V211 | [Admin/StockCountPages/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockCountPages/Detail.cshtml) | Trang | T04 · Quét hoặc tìm; từng quy cách và số đếm; phần chênh lệch riêng; gửi hoặc xác nhận theo trạng thái. | Tham chiếu trực tiếp; R08 |
| V212 | [Admin/StockCountPages/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockCountPages/Index.cshtml) | Trang | T01 · Thẻ phiếu với kho và tiến độ; chọn phiếu; bộ lọc dưới đáy; giữ trạng thái kiểm kê. | Tham chiếu trực tiếp |
| V222 | [Admin/StockTransfer/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockTransfer/Detail.cshtml) | Trang | T02 · Kho đi, kho đến và trạng thái trước; từng dòng chuyển; thanh tác vụ theo vòng đời hiện hành. | Tham chiếu trực tiếp |
| V223 | [Admin/StockTransfer/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/StockTransfer/Index.cshtml) | Trang | T01 · Thẻ chuyển kho; kho đi và kho đến đặt cạnh nhãn; trạng thái và ngày trước; mở chứng từ. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 9 file (6 JS, 3 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-13 Tra cứu kho và pháp nhân

5 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V067 | [Admin/InventoryInquiry/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryInquiry/Index.cshtml) | Trang | T01 · Tìm hoặc quét sản phẩm trước; chọn kho; số tồn và giữ hàng tách biệt theo đơn vị. | Tham chiếu trực tiếp |
| V070 | [Admin/InventoryLedger/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryLedger/Index.cshtml) | Trang | T01 · Tìm sản phẩm và khoảng ngày; số đầu cuối kỳ trước; từng phát sinh mở xem chứng từ nguồn. | Tham chiếu trực tiếp |
| V100 | [Admin/LegalEntityManagement/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/LegalEntityManagement/Index.cshtml) | Trang | T01 · Danh sách pháp nhân dạng dòng; nhóm thông tin nhận diện và thuế; biểu mẫu một cột. | Tham chiếu trực tiếp |
| V101 | [Admin/LegalEntityReconciliation/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/LegalEntityReconciliation/Index.cshtml) | Trang | T01 · Chọn kỳ và pháp nhân; chênh lệch trước; mở nhóm và chứng từ đối chiếu liên quan. | Tham chiếu trực tiếp |
| V242 | [Admin/WarehouseManagement/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/WarehouseManagement/Index.cshtml) | Trang | T01 · Danh sách kho dạng dòng; tìm kho; sửa thông tin trong trang riêng; giữ phạm vi cửa hàng. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 7 file (4 JS, 3 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-14 Sản phẩm và danh mục

28 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V023 | [Admin/AttributeValue/_AttributeValueTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/AttributeValue/_AttributeValueTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V024 | [Admin/AttributeValue/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/AttributeValue/Edit.cshtml) | Trang | T03 · Biểu mẫu AttributeValue: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V025 | [Admin/AttributeValue/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AttributeValue/Index.cshtml) | Trang | T01 · Danh sách giá trị theo thuộc tính; bộ lọc giữ ngữ cảnh; sửa giá trị ở trang riêng. | Tham chiếu trực tiếp |
| V030 | [Admin/BarcodeHistory/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/BarcodeHistory/Index.cshtml) | Trang | T01 · Tìm mã hoặc sản phẩm; dòng thời gian thay đổi; xem người thực hiện và chứng từ nguồn. | Tham chiếu trực tiếp |
| V031 | [Admin/BarcodeNormalization/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/BarcodeNormalization/Index.cshtml) | Trang | T01 · Chọn phạm vi; danh sách vấn đề theo mức xử lý; xem trước thay đổi trước hành động hiện có. | Tham chiếu trực tiếp |
| V032 | [Admin/Brand/_BrandTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Brand/_BrandTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V033 | [Admin/Brand/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/Brand/Edit.cshtml) | Trang | T03 · Biểu mẫu Brand: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V034 | [Admin/Brand/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Brand/Index.cshtml) | Trang | T01 · Danh sách thương hiệu gọn; tìm và lọc trạng thái; thêm sửa qua biểu mẫu chung. | Tham chiếu trực tiếp |
| V035 | [Admin/Category/_CategoryTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Category/_CategoryTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V036 | [Admin/Category/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/Category/Edit.cshtml) | Trang | T03 · Biểu mẫu Category: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V037 | [Admin/Category/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Category/Index.cshtml) | Trang | T08 · Cây danh mục có đường dẫn; tìm tên; sửa từng mục; giữ quan hệ cha con. | Tham chiếu trực tiếp |
| V143 | [Admin/Product/_ProductTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Product/_ProductTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V144 | [Admin/Product/_Variants.cshtml](../../GaoApp.Web/Areas/Admin/Views/Product/_Variants.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V145 | [Admin/Product/_VariantUnitConversionsModal.cshtml](../../GaoApp.Web/Areas/Admin/Views/Product/_VariantUnitConversionsModal.cshtml) | Partial | T13 · Lựa chọn ngắn dùng bảng trượt; biểu mẫu dài toàn màn hình; giữ focus, hủy và dữ liệu đang nhập. | Dùng chung hoặc component |
| V146 | [Admin/Product/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/Product/Create.cshtml) | Trang | T03 · Luồng tạo sản phẩm: thông tin, quy cách, đơn vị và giá, ảnh; kiểm tra trước lưu. | Tham chiếu trực tiếp |
| V147 | [Admin/Product/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/Product/Detail.cshtml) | Trang | T02 · Ảnh, tên, SKU, trạng thái trước; Quy cách, Giá và tồn, Barcode; giá vốn chỉ theo quyền. | Tham chiếu trực tiếp |
| V148 | [Admin/Product/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/Product/Edit.cshtml) | Trang | T03 · Thông tin sản phẩm và quy cách theo nhóm; lưu phần được hỗ trợ; cảnh báo thay đổi chưa lưu. | Tham chiếu trực tiếp |
| V149 | [Admin/Product/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Product/Index.cshtml) | Trang | T01 · Tìm hoặc quét trước; thẻ sản phẩm với tên, SKU, trạng thái; bộ lọc và chỉ số thu gọn. | Tham chiếu trực tiếp |
| V150 | [Admin/ProductAttribute/_ProductAttributeTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/ProductAttribute/_ProductAttributeTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V151 | [Admin/ProductAttribute/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/ProductAttribute/Edit.cshtml) | Trang | T03 · Biểu mẫu ProductAttribute: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V152 | [Admin/ProductAttribute/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/ProductAttribute/Index.cshtml) | Trang | T01 · Danh sách thuộc tính với loại và trạng thái; mở giá trị tương ứng; biểu mẫu một cột. | Tham chiếu trực tiếp |
| V181 | [Areas/Admin/Views/Shared/_BarcodeManager.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_BarcodeManager.cshtml) | Partial | T13 · Quản lý nhiều barcode của một quy cách; mã chính và trạng thái rõ; đối chiếu với route thiếu view. | Rà sử dụng; R05 |
| V232 | [Admin/Tax/_TaxTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Tax/_TaxTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V233 | [Admin/Tax/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/Tax/Edit.cshtml) | Trang | T03 · Biểu mẫu Tax: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V234 | [Admin/Tax/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Tax/Index.cshtml) | Trang | T01 · Danh sách thuế với mã và mức áp dụng; tìm; sửa có nhãn và kiểm tra giá trị hiện hành. | Tham chiếu trực tiếp |
| V235 | [Admin/Unit/_UnitTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Unit/_UnitTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V236 | [Admin/Unit/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/Unit/Edit.cshtml) | Trang | T03 · Biểu mẫu Unit: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V237 | [Admin/Unit/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Unit/Index.cshtml) | Trang | T01 · Danh sách đơn vị gọn; tên ký hiệu và trạng thái; thêm sửa qua biểu mẫu chung. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 17 file (11 JS, 6 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-15 Khách hàng nhà cung cấp và công nợ

14 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V039 | [Admin/Customer/_CustomerTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Customer/_CustomerTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V040 | [Admin/Customer/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/Customer/Edit.cshtml) | Trang | T03 · Biểu mẫu Customer: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V041 | [Admin/Customer/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Customer/Index.cshtml) | Trang | T01 · Tìm tên hoặc điện thoại; thẻ thông tin chính; mở hồ sơ và các tab theo quyền. | Tham chiếu trực tiếp |
| V042 | [Admin/Customer/Profile.cshtml](../../GaoApp.Web/Areas/Admin/Views/Customer/Profile.cshtml) | Trang | T02 · Tóm tắt khách trước; Tổng quan, Đơn, Điểm, Voucher theo quyền; chọn tab giữ ngữ cảnh. | Tham chiếu trực tiếp |
| V044 | [Admin/CustomerDebt/Collections.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerDebt/Collections.cshtml) | Trang | T02 · Các lần thu nợ theo ngày và khách; bộ lọc gọn; mở biên nhận giữ nguyên điều kiện tra cứu. | Tham chiếu trực tiếp |
| V045 | [Admin/CustomerDebt/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerDebt/Index.cshtml) | Trang | T01 · Chọn khách và kỳ; số còn nợ trước; danh sách khoản nợ; thu nợ trong luồng riêng. | Tham chiếu trực tiếp |
| V047 | [Admin/CustomerDeposit/_DepositList.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerDeposit/_DepositList.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V048 | [Admin/CustomerDeposit/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerDeposit/Index.cshtml) | Trang | T01 · Số dư cọc theo khách; danh sách phát sinh; thu hoặc hoàn cọc theo quyền và chứng từ. | Tham chiếu trực tiếp |
| V170 | [Admin/RewardSettings/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/RewardSettings/Index.cshtml) | Trang | T09 · Nhóm thiết lập điểm và voucher thành các phần; nhãn rõ; lưu tập trung có kiểm tra dữ liệu. | Tham chiếu trực tiếp |
| V171 | [Admin/RewardVouchers/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/RewardVouchers/Index.cshtml) | Trang | T01 · Thẻ voucher theo khách và trạng thái; mã, hạn dùng, giá trị trước; điều kiện mở chi tiết. | Tham chiếu trực tiếp |
| V228 | [Admin/Supplier/_SupplierTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Supplier/_SupplierTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V229 | [Admin/Supplier/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/Supplier/Create.cshtml) | Trang | T03 · Biểu mẫu Supplier: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Rà sử dụng; R02 |
| V230 | [Admin/Supplier/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/Supplier/Edit.cshtml) | Trang | T03 · Biểu mẫu Supplier: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V231 | [Admin/Supplier/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Supplier/Index.cshtml) | Trang | T01 · Tìm nhà cung cấp; thông tin liên hệ và trạng thái trước; mở hồ sơ để sửa hoặc giao dịch. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 19 file (11 JS, 8 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-16 Hóa đơn và tích hợp hóa đơn

25 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V027 | [Admin/AutoInvoice/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AutoInvoice/Index.cshtml) | Trang | T01 · Trạng thái hàng đợi và lý do chưa phát hành trước; bộ lọc; xem từng đơn trước tác vụ hiện có. | Tham chiếu trực tiếp |
| V071 | [Admin/Invoice/_CreateManualDetailModal.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_CreateManualDetailModal.cshtml) | Partial | T13 · Lựa chọn ngắn dùng bảng trượt; biểu mẫu dài toàn màn hình; giữ focus, hủy và dữ liệu đang nhập. | Tham chiếu trực tiếp |
| V072 | [Admin/Invoice/_CreateOrderDraftModal.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_CreateOrderDraftModal.cshtml) | Partial | T13 · Lựa chọn ngắn dùng bảng trượt; biểu mẫu dài toàn màn hình; giữ focus, hủy và dữ liệu đang nhập. | Dùng chung hoặc component |
| V073 | [Admin/Invoice/_InvoiceAutoLinesTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceAutoLinesTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Dùng chung hoặc component |
| V074 | [Admin/Invoice/_InvoiceBuyerViettelCards.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceBuyerViettelCards.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V075 | [Admin/Invoice/_InvoiceCorrectionHistory.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceCorrectionHistory.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V076 | [Admin/Invoice/_InvoiceDetailAlerts.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceDetailAlerts.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V077 | [Admin/Invoice/_InvoiceDetailHeader.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceDetailHeader.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V078 | [Admin/Invoice/_InvoiceManualModals.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceManualModals.cshtml) | Partial | T13 · Lựa chọn ngắn dùng bảng trượt; biểu mẫu dài toàn màn hình; giữ focus, hủy và dữ liệu đang nhập. | Dùng chung hoặc component |
| V079 | [Admin/Invoice/_InvoiceSummary.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_InvoiceSummary.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Tham chiếu trực tiếp |
| V080 | [Admin/Invoice/_ManualLinesTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_ManualLinesTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V081 | [Admin/Invoice/_ViettelActionToolbar.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/_ViettelActionToolbar.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V082 | [Admin/Invoice/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/Detail.cshtml) | Trang | T02 · Hóa đơn và tình trạng phát hành trước; Người mua, Hàng hóa, Tổng tiền, Lịch sử; tác vụ theo quyền. | Tham chiếu trực tiếp |
| V083 | [Admin/Invoice/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/Index.cshtml) | Trang | T01 · Danh sách hóa đơn với trạng thái phát hành và tiền; bộ lọc dưới đáy; mở chi tiết và chứng từ. | Tham chiếu trực tiếp |
| V084 | [Admin/Invoice/ViettelDashboard.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/ViettelDashboard.cshtml) | Trang | T02 · Tình trạng Viettel, chứng từ cần xử lý và đồng bộ; chỉ số gọn; chi tiết mở theo trạng thái. | Tham chiếu trực tiếp |
| V085 | [Admin/Invoice/ViettelPayload.cshtml](../../GaoApp.Web/Areas/Admin/Views/Invoice/ViettelPayload.cshtml) | Trang | T02 · Thông tin yêu cầu và kết quả trước; payload đã làm sạch trong vùng mở rộng; không tràn toàn trang. | Tham chiếu trực tiếp |
| V086 | [Admin/InvoiceCorrection/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/InvoiceCorrection/Create.cshtml) | Trang | T03 · Biểu mẫu InvoiceCorrection: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V087 | [Admin/InvoiceDashboard/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InvoiceDashboard/Index.cshtml) | Trang | T06 · Tóm tắt cần xử lý theo trạng thái; chỉ số gọn; chạm chỉ số để mở đúng danh sách. | Tham chiếu trực tiếp |
| V088 | [Admin/InvoiceInputStock/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InvoiceInputStock/Index.cshtml) | Trang | T01 · Tìm hóa đơn và sản phẩm; trạng thái tồn liên quan trước; tách thông tin giá vốn theo quyền. | Tham chiếu trực tiếp |
| V089 | [Admin/InvoiceIntegrationLog/Cleanup.cshtml](../../GaoApp.Web/Areas/Admin/Views/InvoiceIntegrationLog/Cleanup.cshtml) | Trang | T02 · Điều kiện dọn log, số bản ghi dự kiến và xác nhận hiện hành; tách khỏi màn tra cứu thường xuyên. | Tham chiếu trực tiếp |
| V090 | [Admin/InvoiceIntegrationLog/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/InvoiceIntegrationLog/Detail.cshtml) | Trang | T02 · Trạng thái và thông tin lỗi được phép trước; dữ liệu kỹ thuật mở rộng và đã làm sạch. | Tham chiếu trực tiếp |
| V091 | [Admin/InvoiceIntegrationLog/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InvoiceIntegrationLog/Index.cshtml) | Trang | T01 · Danh sách nhật ký đã làm sạch; lọc thời gian và trạng thái; xem chi tiết với giới hạn hiện có. | Tham chiếu trực tiếp |
| V092 | [Admin/InvoiceProviderSetting/Form.cshtml](../../GaoApp.Web/Areas/Admin/Views/InvoiceProviderSetting/Form.cshtml) | Trang | T03 · Biểu mẫu InvoiceProviderSetting: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V093 | [Admin/InvoiceProviderSetting/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InvoiceProviderSetting/Index.cshtml) | Trang | T09 · Danh sách cấu hình nhà cung cấp hóa đơn; trạng thái kết nối; thông tin nhạy cảm giữ che. | Tham chiếu trực tiếp |
| V241 | [Admin/ViettelInvoiceSync/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/ViettelInvoiceSync/Index.cshtml) | Trang | T01 · Chọn khoảng thời gian và phạm vi; kết quả tiến trình từng đợt; lỗi và thử lại rõ. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 7 file (5 JS, 2 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-17 Ngân hàng và đối soát

13 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V004 | [Admin/AcbCallbackRouting/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AcbCallbackRouting/Index.cshtml) | Trang | T09 · Dòng định tuyến theo tài khoản và cửa hàng; xem trạng thái; sửa hoặc kiểm tra trong biểu mẫu riêng. | Tham chiếu trực tiếp |
| V005 | [Admin/AcbPayments/_LookupContent.cshtml](../../GaoApp.Web/Areas/Admin/Views/AcbPayments/_LookupContent.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V006 | [Admin/AcbPayments/Embedded.cshtml](../../GaoApp.Web/Areas/Admin/Views/AcbPayments/Embedded.cshtml) | Trang nhúng | T02 · Tra cứu ngân hàng trong vùng nhúng; giao dịch và số tiền rõ; đóng trả lại đúng ngữ cảnh đơn. | Tham chiếu trực tiếp |
| V007 | [Admin/AcbPayments/Lookup.cshtml](../../GaoApp.Web/Areas/Admin/Views/AcbPayments/Lookup.cshtml) | Trang | T01 · Đơn và số cần tra cứu trước; giao dịch phù hợp thành dòng; mở bằng chứng và trạng thái xác nhận. | Tham chiếu trực tiếp |
| V008 | [Admin/AcbReconciliation/_AcbBankLookup.cshtml](../../GaoApp.Web/Areas/Admin/Views/AcbReconciliation/_AcbBankLookup.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Tham chiếu trực tiếp |
| V009 | [Admin/AcbReconciliation/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AcbReconciliation/Index.cshtml) | Trang | T01 · Kỳ và nguồn đối soát; chỉ số lệch trước; dòng giao dịch; xem liên kết đơn và tra cứu ngân hàng. | Tham chiếu trực tiếp |
| V010 | [Admin/AcbSettings/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AcbSettings/Index.cshtml) | Trang | T09 · Các nhóm cấu hình thu gọn; trạng thái kết nối trước; dữ liệu nhạy cảm giữ che và xác nhận hiện hành. | Tham chiếu trực tiếp |
| V011 | [Admin/AcbSettings/SandboxResult.cshtml](../../GaoApp.Web/Areas/Admin/Views/AcbSettings/SandboxResult.cshtml) | Trang | T02 · Kết quả kiểm tra cấu hình và lỗi trước; thông tin kỹ thuật thu gọn; dữ liệu nhạy cảm giữ che. | Tham chiếu trực tiếp |
| V028 | [Admin/BankNotifications/_LiveState.cshtml](../../GaoApp.Web/Areas/Admin/Views/BankNotifications/_LiveState.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Tham chiếu trực tiếp |
| V029 | [Admin/BankNotifications/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/BankNotifications/Index.cshtml) | Trang | T01 · Danh sách thông báo và trạng thái xử lý; lọc theo quầy; cập nhật trực tiếp không làm mất vị trí. | Tham chiếu trực tiếp |
| V224 | [Admin/StoreBankAccounts/_StoreBankAccountTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/StoreBankAccounts/_StoreBankAccountTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V225 | [Admin/StoreBankAccounts/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/StoreBankAccounts/Edit.cshtml) | Trang | T03 · Biểu mẫu StoreBankAccounts: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V226 | [Admin/StoreBankAccounts/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/StoreBankAccounts/Index.cshtml) | Trang | T01 · Dòng tài khoản ngân hàng theo chủ thể; số che theo hiện trạng; chỉnh trong biểu mẫu một cột. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 10 file (7 JS, 3 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-18 Báo cáo và chi phí vận hành

8 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V038 | [Admin/CostAdjustmentReport/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/CostAdjustmentReport/Index.cshtml) | Trang | T06 · Chọn kỳ và phạm vi; số điều chỉnh và nguồn; mở từng đơn hoặc bút toán liên quan. | Tham chiếu trực tiếp |
| V102 | [Admin/ManagementReport/Workspace.cshtml](../../GaoApp.Web/Areas/Admin/Views/ManagementReport/Workspace.cshtml) | Trang | T06 · Bộ lọc kỳ thu gọn; chỉ số chính trước; nội dung theo section; biểu đồ có bảng dữ liệu tương ứng. | Tham chiếu trực tiếp |
| V105 | [Admin/OperationsReport/Workspace.cshtml](../../GaoApp.Web/Areas/Admin/Views/OperationsReport/Workspace.cshtml) | Trang | T06 · Bộ lọc kỳ và phạm vi; tổng thu chi, tồn hoặc nợ; mở phát sinh để xem nguồn. | Tham chiếu trực tiếp |
| V153 | [Admin/ProfitReport/Detail.cshtml](../../GaoApp.Web/Areas/Admin/Views/ProfitReport/Detail.cshtml) | Trang | T06 · Thẻ đơn và lợi nhuận theo quyền; nhãn giá vốn tạm tính; mở nguyên nhân và chứng từ nguồn. | Tham chiếu trực tiếp |
| V154 | [Admin/ProfitReport/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/ProfitReport/Index.cshtml) | Trang | T06 · Doanh thu, giá vốn và lợi nhuận trước; nhãn tạm tính rõ; xem đơn và nguyên nhân thiếu giá vốn. | Tham chiếu trực tiếp |
| V177 | [Admin/SalesExecutiveReport/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/SalesExecutiveReport/Index.cshtml) | Trang | T06 · Chỉ số quản lý trước; tình trạng dữ liệu; biểu đồ theo kỳ; mở phân tích giữ bộ lọc. | Tham chiếu trực tiếp |
| V178 | [Admin/SalesReport/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/SalesReport/Index.cshtml) | Trang | T06 · Các nhóm tổng quan, đơn, sản phẩm, giảm giá, trả hàng, void; chọn nhóm rồi xem dữ liệu. | Tham chiếu trực tiếp |
| V186 | [Areas/Admin/Views/Shared/_ManagementReportWorkspace.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ManagementReportWorkspace.cshtml) | Partial | T13 · Khung báo cáo chung: lọc kỳ, chỉ số, biểu đồ và bảng; dialog chi phí tách bước nhập và xác nhận. | Dùng chung hoặc component |

Tài nguyên giao cho bước này để rà soát: 8 file (4 JS, 4 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-19 Khuyến mãi và nội dung trình chiếu

5 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V056 | [Admin/DisplayPromotion/_Workspace.cshtml](../../GaoApp.Web/Areas/Admin/Views/DisplayPromotion/_Workspace.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V057 | [Admin/DisplayPromotion/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/DisplayPromotion/Index.cshtml) | Trang | T01 · Danh sách nội dung và lịch trình; ảnh xem trước gọn; chỉnh nội dung và thứ tự rõ. | Tham chiếu trực tiếp |
| V155 | [Admin/Promotion/_PromotionTable.cshtml](../../GaoApp.Web/Areas/Admin/Views/Promotion/_PromotionTable.cshtml) | Partial | T13 · Dòng hoặc thẻ trên điện thoại; bảng trên desktop; giữ cùng nguồn dữ liệu và tổng theo đúng đơn vị. | Tham chiếu trực tiếp |
| V156 | [Admin/Promotion/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Promotion/Index.cshtml) | Trang | T01 · Thẻ chương trình theo thời gian và trạng thái; phần điều kiện và quyền lợi mở riêng. | Tham chiếu trực tiếp |
| V157 | [Admin/PromotionLabels/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/PromotionLabels/Index.cshtml) | Trang | T01 · Chọn chương trình và hàng cần in; cấu hình khổ; xem trước; gửi lệnh qua luồng in hiện có. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 5 file (2 JS, 3 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-20 Chăm sóc khách hàng trợ lý và media

10 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V019 | [Admin/AssistantKnowledge/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AssistantKnowledge/Index.cshtml) | Trang | T01 · Danh sách kiến thức theo loại và trạng thái; tìm; mở nội dung; sửa và duyệt theo quyền. | Tham chiếu trực tiếp |
| V020 | [Admin/AssistantQuality/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AssistantQuality/Index.cshtml) | Trang | T01 · Tóm tắt chất lượng theo kỳ; hàng đợi và trường hợp lỗi; mở bằng chứng và đánh giá riêng. | Tham chiếu trực tiếp |
| V021 | [Admin/AssistantSearch/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AssistantSearch/Index.cshtml) | Trang | T01 · Ô truy vấn trước; kết quả sản phẩm; cách gọi và đề xuất sửa tách khu vực; lưu theo quyền. | Tham chiếu trực tiếp |
| V022 | [Admin/AssistantSettings/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AssistantSettings/Index.cshtml) | Trang | T09 · Chia thiết lập trợ lý theo nhóm; trạng thái trước; quyền quản lý và thông tin nhạy cảm giữ nguyên. | Tham chiếu trực tiếp |
| V043 | [Admin/CustomerChannels/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerChannels/Index.cshtml) | Trang | T01 · Danh sách kênh với trạng thái hoạt động; mở cấu hình từng kênh; che thông tin tài khoản. | Tham chiếu trực tiếp |
| V050 | [Admin/CustomerInbox/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerInbox/Index.cshtml) | Trang | T07 · Danh sách hội thoại trước; mở cuộc trò chuyện toàn màn hình; thông tin khách ở trang phụ. | Tham chiếu trực tiếp |
| V051 | [Admin/CustomerQuality/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerQuality/Index.cshtml) | Trang | T01 · Chọn nhóm phản hồi; tình huống thành dòng; xem bằng chứng trước đánh giá hoặc đề xuất. | Tham chiếu trực tiếp |
| V103 | [Admin/Media/Test.cshtml](../../GaoApp.Web/Areas/Admin/Views/Media/Test.cshtml) | Trang | T10 · Giữ màn kiểm tra media trong phạm vi kỹ thuật; dùng chung trạng thái upload và quyền hiện có. | Tham chiếu trực tiếp |
| V104 | [Admin/MediaLibrary/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/MediaLibrary/Index.cshtml) | Trang | T10 · Lưới ảnh theo mục đích; tìm hoặc lọc; chọn nhiều khi cần; upload và xem ảnh toàn màn hình. | Tham chiếu trực tiếp |
| V199 | [Areas/Admin/Views/Shared/Components/Assistant/Default.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Components/Assistant/Default.cshtml) | Component | T13 · Trợ lý mở thành màn hội thoại; vị trí tránh menu và dock; giữ quyền, ngữ cảnh và luồng xác nhận. | Dùng chung hoặc component |

Tài nguyên giao cho bước này để rà soát: 18 file (9 JS, 9 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-21 Quản trị nhân viên quyền và thiết bị

16 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V026 | [Admin/AuditLogs/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/AuditLogs/Index.cshtml) | Trang | T01 · Lọc thời gian, người và sự kiện; danh sách gọn; xem chi tiết đã che dữ liệu và giới hạn truy vấn. | Tham chiếu trực tiếp |
| V094 | [Admin/KioskManagement/Activation.cshtml](../../GaoApp.Web/Areas/Admin/Views/KioskManagement/Activation.cshtml) | Trang | T02 · Mã ghép và thời hạn dễ đọc; thông tin thiết bị rõ; sao chép hoặc quay lại danh sách. | Tham chiếu trực tiếp |
| V095 | [Admin/KioskManagement/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/KioskManagement/Index.cshtml) | Trang | T01 · Danh sách kiosk theo quầy; mở thiết bị; kích hoạt hoặc thu hồi theo luồng hiện hành. | Tham chiếu trực tiếp |
| V139 | [Admin/POSTerminals/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSTerminals/Create.cshtml) | Trang | T03 · Biểu mẫu POSTerminals: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V140 | [Admin/POSTerminals/CreateKey.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSTerminals/CreateKey.cshtml) | Trang | T03 · Chọn phạm vi và thời hạn khóa; tổng kết thiết bị; xác nhận bảo mật hiện có. | Tham chiếu trực tiếp |
| V141 | [Admin/POSTerminals/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSTerminals/Index.cshtml) | Trang | T01 · Danh sách quầy và trạng thái; mở chi tiết thiết bị; tạo và thu hồi khóa theo quyền hiện có. | Tham chiếu trực tiếp |
| V142 | [Admin/POSTerminals/KeyCreated.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSTerminals/KeyCreated.cshtml) | Trang | T02 · Khóa mới chỉ hiển thị theo chính sách hiện có; sao chép có phản hồi; thời hạn và hướng dẫn ghép thiết bị. | Tham chiếu trực tiếp |
| V172 | [Admin/RolePermissions/_PermissionGroupCard.cshtml](../../GaoApp.Web/Areas/Admin/Views/RolePermissions/_PermissionGroupCard.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V173 | [Admin/RolePermissions/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/RolePermissions/Index.cshtml) | Trang | T08 · Chọn nhóm quyền; tìm chức năng; nhóm quyền đọc và ghi rõ; thay đổi có tổng kết trước lưu. | Tham chiếu trực tiếp |
| V174 | [Admin/Roles/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/Roles/Create.cshtml) | Trang | T03 · Biểu mẫu Roles: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V175 | [Admin/Roles/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/Roles/Edit.cshtml) | Trang | T03 · Biểu mẫu Roles: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V176 | [Admin/Roles/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/Roles/Index.cshtml) | Trang | T01 · Danh sách nhóm quyền; tìm; thêm sửa tên và trạng thái; mở phân quyền cho đúng nhóm. | Tham chiếu trực tiếp |
| V196 | [Areas/Admin/Views/Shared/_SecurityActionConfirmation.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_SecurityActionConfirmation.cshtml) | Partial | T13 · Xác nhận bảo mật gọn, đúng thao tác và đối tượng; focus rõ; giữ challenge và giới hạn hiện có. | Dùng chung hoặc component |
| V238 | [Admin/UserInStores/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/UserInStores/Create.cshtml) | Trang | T03 · Biểu mẫu UserInStores: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V239 | [Admin/UserInStores/Edit.cshtml](../../GaoApp.Web/Areas/Admin/Views/UserInStores/Edit.cshtml) | Trang | T03 · Biểu mẫu UserInStores: một cột, nhóm thông tin rõ, lỗi tại trường và tổng lỗi; lưu theo quyền hiện có. | Tham chiếu trực tiếp |
| V240 | [Admin/UserInStores/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/UserInStores/Index.cshtml) | Trang | T01 · Danh sách nhân viên với nhóm và trạng thái; thêm sửa riêng; tác vụ tài khoản có xác nhận hiện hành. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 8 file (5 JS, 3 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-22 In ấn và màn hình chuyên dụng

16 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V046 | [Admin/CustomerDebt/Receipt.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerDebt/Receipt.cshtml) | Bản in | T11 · Điều khiển xem hoặc in gọn trên điện thoại; nội dung chứng từ giữ đúng khổ và tỷ lệ in hiện hành. | Tham chiếu trực tiếp |
| V049 | [Admin/CustomerDeposit/Print.cshtml](../../GaoApp.Web/Areas/Admin/Views/CustomerDeposit/Print.cshtml) | Bản in | T11 · Điều khiển xem hoặc in gọn trên điện thoại; nội dung chứng từ giữ đúng khổ và tỷ lệ in hiện hành. | Tham chiếu trực tiếp |
| V052 | [Admin/DeliveryPage/Bill.cshtml](../../GaoApp.Web/Areas/Admin/Views/DeliveryPage/Bill.cshtml) | Bản in | T11 · Điều khiển xem hoặc in gọn trên điện thoại; nội dung chứng từ giữ đúng khổ và tỷ lệ in hiện hành. | Tham chiếu trực tiếp |
| V096 | [Admin/LabelPrinting/_QuickPrint.cshtml](../../GaoApp.Web/Areas/Admin/Views/LabelPrinting/_QuickPrint.cshtml) | Partial | T13 · Khối con thích ứng theo trang chủ; thống nhất trạng thái tải, lỗi và thao tác; giữ mô hình dữ liệu. | Dùng chung hoặc component |
| V097 | [Admin/LabelPrinting/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/LabelPrinting/Index.cshtml) | Trang | T11 · Chọn hàng và quy cách; giỏ in; khổ tem và máy in; xem trước theo đúng đơn vị. | Tham chiếu trực tiếp |
| V121 | [Admin/POS/CustomerDisplay.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/CustomerDisplay.cshtml) | Màn chuyên dụng | T11 · Tìm hoặc quét hàng; giỏ theo từng quy cách; khách và ưu đãi mở riêng; thanh thu tiền. | Tham chiếu trực tiếp |
| V124 | [Admin/POS/PrintReceipt.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/PrintReceipt.cshtml) | Bản in | T11 · Điều khiển xem hoặc in gọn trên điện thoại; nội dung chứng từ giữ đúng khổ và tỷ lệ in hiện hành. | Rà sử dụng; R03 |
| V127 | [Admin/POSReceipt/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSReceipt/Index.cshtml) | Trang | T01 · Đánh giá file cũ cạnh luồng in ReceiptTemplates đang được controller sử dụng; chưa tự xóa. | Rà sử dụng; R04 |
| V132 | [Admin/POSShiftClosingSlipPrintPage/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSShiftClosingSlipPrintPage/Index.cshtml) | Bản in | T11 · Điều khiển xem hoặc in gọn trên điện thoại; nội dung chứng từ giữ đúng khổ và tỷ lệ in hiện hành. | Tham chiếu trực tiếp |
| V134 | [Admin/POSShiftHandoverSlipPrintPage/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSShiftHandoverSlipPrintPage/Index.cshtml) | Bản in | T11 · Điều khiển xem hoặc in gọn trên điện thoại; nội dung chứng từ giữ đúng khổ và tỷ lệ in hiện hành. | Tham chiếu trực tiếp |
| V162 | [Admin/PurchaseOrders/Print.cshtml](../../GaoApp.Web/Areas/Admin/Views/PurchaseOrders/Print.cshtml) | Bản in | T11 · Điều khiển xem hoặc in gọn trên điện thoại; nội dung chứng từ giữ đúng khổ và tỷ lệ in hiện hành. | Tham chiếu trực tiếp |
| V168 | [Admin/ReceiptTemplates/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/ReceiptTemplates/Index.cshtml) | Trang | T11 · Danh sách mẫu; chọn khổ trước; chỉnh theo từng nhóm; xem trước bản in giữ tỷ lệ thực. | Tham chiếu trực tiếp |
| V169 | [Admin/ReceiptTemplates/Print.cshtml](../../GaoApp.Web/Areas/Admin/Views/ReceiptTemplates/Print.cshtml) | Bản in | T11 · Điều khiển xem hoặc in gọn trên điện thoại; nội dung chứng từ giữ đúng khổ và tỷ lệ in hiện hành. | Tham chiếu trực tiếp |
| V190 | [Areas/Admin/Views/Shared/_ProductLabelReceiptLink.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_ProductLabelReceiptLink.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Dùng chung hoặc component |
| V227 | [Admin/StoreMonitor/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/StoreMonitor/Index.cshtml) | Màn chuyên dụng | T11 · Giữ bố cục màn hình lớn; điều khiển cảnh và thông tin gọn khi dùng điện thoại để quản trị. | Tham chiếu trực tiếp |
| V257 | [Public/Kiosk/Index.cshtml](../../GaoApp.Web/Views/Kiosk/Index.cshtml) | Màn chuyên dụng | T11 · Giữ thao tác tự phục vụ cho màn chạm; sản phẩm, giỏ và thanh toán thích ứng theo thiết bị. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 39 file (26 JS, 13 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

### UI-23 Trang công khai và dự phòng

11 file. Trạng thái thực hiện toàn bộ nhóm: **Chưa bắt đầu**.

| ID | File | Loại | Mẫu và bố cục đích | Kiểm tra sử dụng |
|---|---|---|---|---|
| V249 | [Views/_ViewImports.cshtml](../../GaoApp.Web/Views/_ViewImports.cshtml) | Cấu hình Razor | T13 · Giữ imports và kế thừa layout; kiểm tra phạm vi Admin hoặc công khai; không chèn menu chức năng cố định. | Quy ước Razor |
| V250 | [Views/_ViewStart.cshtml](../../GaoApp.Web/Views/_ViewStart.cshtml) | Cấu hình Razor | T13 · Giữ imports và kế thừa layout; kiểm tra phạm vi Admin hoặc công khai; không chèn menu chức năng cố định. | Quy ước Razor |
| V251 | [Public/CustomerSupport/Index.cshtml](../../GaoApp.Web/Views/CustomerSupport/Index.cshtml) | Trang | T01 · Trang chat khách hàng toàn màn hình; thông điệp, đính kèm và bàn phím; quay lại giữ hội thoại. | Tham chiếu trực tiếp |
| V252 | [Public/Error/Error.cshtml](../../GaoApp.Web/Views/Error/Error.cshtml) | Trang trạng thái | T12 · Thông báo tiếng Việt gọn; lý do và hành động quay lại rõ; chỉ hiện chi tiết kỹ thuật được phép. | Tham chiếu trực tiếp |
| V253 | [Public/Home/Index.cshtml](../../GaoApp.Web/Views/Home/Index.cshtml) | Trang | T12 · Trang vào hệ thống hoặc thông tin công khai theo vai trò hiện tại; nội dung gọn và đường vào ứng dụng rõ. | Tham chiếu trực tiếp |
| V254 | [Public/Home/Privacy.cshtml](../../GaoApp.Web/Views/Home/Privacy.cshtml) | Trang | T12 · Trang vào hệ thống hoặc thông tin công khai theo vai trò hiện tại; nội dung gọn và đường vào ứng dụng rõ. | Tham chiếu trực tiếp |
| V255 | [Public/InvoiceBuyerSelfService/Index.cshtml](../../GaoApp.Web/Views/InvoiceBuyerSelfService/Index.cshtml) | Trang | T03 · Biểu mẫu thông tin xuất hóa đơn một cột; trạng thái liên kết và kết quả gửi rõ. | Tham chiếu trực tiếp; R09 |
| V256 | [Public/InvoiceBuyerSelfService/Unavailable.cshtml](../../GaoApp.Web/Views/InvoiceBuyerSelfService/Unavailable.cshtml) | Trang trạng thái | T12 · Trạng thái hết hạn, đã khóa hoặc không còn khả dụng; thông báo và cách liên hệ cửa hàng. | Tham chiếu trực tiếp |
| V258 | [Views/Shared/_Layout.cshtml](../../GaoApp.Web/Views/Shared/_Layout.cshtml) | Layout | T13 · Khung kế thừa theo mục đích; header, menu và dock có chủ sở hữu; giữ section và chỉ tải script một lần. | Dùng chung hoặc component |
| V259 | [Views/Shared/_ValidationScriptsPartial.cshtml](../../GaoApp.Web/Views/Shared/_ValidationScriptsPartial.cshtml) | Partial | T13 · Thành phần dùng chung theo màn chủ; giữ dữ liệu, validation, quyền và một lần gắn sự kiện. | Rà sử dụng |
| V260 | [Views/Shared/Error.cshtml](../../GaoApp.Web/Views/Shared/Error.cshtml) | Trang trạng thái | T12 · Thông báo tiếng Việt gọn; lý do và hành động quay lại rõ; chỉ hiện chi tiết kỹ thuật được phép. | Tham chiếu trực tiếp |

Tài nguyên giao cho bước này để rà soát: 0 file (0 JS, 0 CSS). Phân bổ tài nguyên bao gồm tham chiếu trực tiếp và phân loại theo chức năng; kiểm tra loader trước khi sửa hoặc loại bỏ.

## Các luồng động cần nghiệm thu riêng

Danh sách dưới đây bổ sung các trạng thái hoặc route dùng chung file Razor. Đây là các ca nghiệm thu trọng tâm; toàn bộ JS và các điểm dựng DOM vẫn được rà theo owner trong JSON.

| Mã | Bước | Màn hoặc luồng | Điểm mở hoặc trạng thái | Bố cục và điều kiện giữ nguyên |
|---|---|---|---|---|
| D01 | UI-18 | Phân tích sản phẩm | `/admin/reports/products` · `products` | Lọc kỳ; xếp hạng sản phẩm; lượng theo đơn vị gốc từng sản phẩm. |
| D02 | UI-18 | Nhóm sản phẩm | `/admin/reports/categories` · `categories` | Cơ cấu doanh thu và lợi nhuận; mở nhóm để xem hàng. |
| D03 | UI-18 | Phân tích khách hàng | `/admin/reports/customers` · `customers` | Khách đóng góp; biểu đồ có lựa chọn và bảng tương đương. |
| D04 | UI-18 | Khách sỉ và lẻ | `/admin/reports/groups` · `groups` | So sánh nhóm; giữ định nghĩa khách và trạng thái giá vốn. |
| D05 | UI-18 | Chi phí vận hành | `/admin/reports/expenses` · `expenses` | Sổ chi phí; form nháp; ghi nhận hoặc hủy theo luồng hiện có. |
| D06 | UI-18 | Thu chi và dòng tiền | `/admin/reports/cashflow` · `cashflow` | Quỹ, đầu kỳ, thu chi và cuối kỳ; phát sinh mở chứng từ nguồn. |
| D07 | UI-18 | Tồn kho và luân chuyển | `/admin/reports/inventory` · `inventory` | Tồn và giá trị theo kho; lượng không cộng gộp các đơn vị khác nhau. |
| D08 | UI-18 | Công nợ và tuổi nợ | `/admin/reports/debts` · `debts` | Nhóm tuổi nợ; chọn đối tượng để xem chi tiết và nguồn. |
| D09 | UI-18 | Các nhóm báo cáo bán hàng | `/admin/reports/sales` · `overview/orders/products/discounts/returns/voids` | Từng nhóm có dữ liệu, lỗi và trạng thái rỗng; giữ drilldown. |
| D10 | UI-06 | Thanh toán tiền mặt và chuyển khoản | `/admin/pos` · `payment` | Kiểm tra tổng, trả lại, tiền thiếu; xử lý mất phản hồi và nhấn lặp. |
| D11 | UI-06 | QR và đối chiếu chuyển khoản tại POS | `/admin/pos` · `qr/review` | Trạng thái QR, giao dịch đến và khoản thu đã ghi; đóng mở giữ phiên. |
| D12 | UI-06 | Phiên POS khi mất kết nối | `/admin/pos` · `offline` | Dùng lại cơ chế offline hiện có; khóa theo phiên và bảo vệ dữ liệu trên thiết bị. |
| D13 | UI-11 | Hoàn thiện dòng nhận hàng và barcode | `/admin/stock-documents/{id:int}` · `provisional/catalog/barcode` | Dòng cần xử lý và bằng chứng; quản lý hoàn thiện theo quyền. |
| D14 | UI-11 | Duyệt thương mại và liên kết XML | `/admin/stock-documents/{id:int}` · `commercial/xml/pricing` | Phân bổ giá và chứng từ theo nguồn nhận; giá vốn chỉ hiện với quyền tương ứng. |
| D15 | UI-09 | Chi tiết chuyến và điểm giao | `/admin/deliveries/trips` · `trip-detail` | Chọn chuyến rồi điểm giao; thao tác theo revision và nguồn tồn. |
| D16 | UI-20 | Hội thoại và thông tin khách | `/admin/customer-service` · `conversation/profile/media` | Danh sách, hội thoại, hồ sơ ở các màn rõ; bàn phím và đính kèm không che nhau. |
| D17 | UI-14 | Quy cách đơn vị và barcode | `/Admin/Product/Edit` · `variants/conversions/barcodes` | Chọn quy cách rồi đơn vị; tránh bảng lồng; lưu và báo xung đột theo hiện trạng. |
| D18 | UI-22 | Trình sửa mẫu in và xem trước | `/admin/receipt-templates` · `template/preview` | Chọn khổ; chỉnh nhóm; xem trước đúng tỷ lệ; không thay transport máy in. |

## Các view cần xác minh sử dụng

Các trường hợp đã có bằng chứng đổi luồng được ghi ở R01 đến R10. Các file còn lại dưới đây cần kiểm tra entry point hoặc tham chiếu gián tiếp; không xóa chỉ vì tìm kiếm tĩnh chưa thấy nơi gọi.

| ID | File | Bước |
|---|---|---|
| V061 | [Admin/InventoryAdjustment/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/InventoryAdjustment/Index.cshtml) | UI-12 |
| V113 | [Admin/POS/_OrderDiscountBox.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_OrderDiscountBox.cshtml) | UI-06 |
| V114 | [Admin/POS/_OrderNoteBox.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/_OrderNoteBox.cshtml) | UI-06 |
| V124 | [Admin/POS/PrintReceipt.cshtml](../../GaoApp.Web/Areas/Admin/Views/POS/PrintReceipt.cshtml) | UI-22 |
| V127 | [Admin/POSReceipt/Index.cshtml](../../GaoApp.Web/Areas/Admin/Views/POSReceipt/Index.cshtml) | UI-22 |
| V180 | [Areas/Admin/Views/Shared/_AdminLayout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_AdminLayout.cshtml) | UI-03 |
| V181 | [Areas/Admin/Views/Shared/_BarcodeManager.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_BarcodeManager.cshtml) | UI-14 |
| V182 | [Areas/Admin/Views/Shared/_BlankLayout.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/_BlankLayout.cshtml) | UI-03 |
| V201 | [Areas/Admin/Views/Shared/Error.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Error.cshtml) | UI-03 |
| V209 | [Areas/Admin/Views/Shared/Sections/Navbar/_Navbar.cshtml](../../GaoApp.Web/Areas/Admin/Views/Shared/Sections/Navbar/_Navbar.cshtml) | UI-03 |
| V229 | [Admin/Supplier/Create.cshtml](../../GaoApp.Web/Areas/Admin/Views/Supplier/Create.cshtml) | UI-15 |
| V259 | [Views/Shared/_ValidationScriptsPartial.cshtml](../../GaoApp.Web/Views/Shared/_ValidationScriptsPartial.cshtml) | UI-23 |

## Chỉ mục route được trích từ controller

Chỉ liệt kê route template cho action công khai có lời gọi View hoặc PartialView đã phân giải. Route conventional giữ nguyên dạng Controller/Action; các giá trị id, token và query là mẫu, không phải dữ liệu để tự tạo menu. Các helper private và route wrapper gọi Page được đối chiếu ở danh sách luồng động.

| View | HTTP | Route template | Controller và action |
|---|---|---|---|
| V004 | GET | `/admin/acb/callback-routing` | [AcbCallbackRoutingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AcbCallbackRoutingController.cs) · `Index` dòng 17 |
| V006 | GET | `/admin/acb/payments/orders/{orderId:int}` | [AcbPaymentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AcbPaymentsController.cs) · `Lookup` dòng 27 |
| V007 | GET | `/admin/acb/payments/orders/{orderId:int}` | [AcbPaymentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AcbPaymentsController.cs) · `Lookup` dòng 27 |
| V008 | POST | `/admin/acb/reconciliation/bank-lookup` | [AcbReconciliationController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AcbReconciliationController.cs) · `BankLookup` dòng 48 |
| V009 | GET | `/admin/acb/reconciliation` | [AcbReconciliationController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AcbReconciliationController.cs) · `Index` dòng 24 |
| V010 | GET | `/admin/acb/settings` | [AcbSettingsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AcbSettingsController.cs) · `Index` dòng 67 |
| V012 | GET | `/admin/account/access-denied` | [AccountController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AccountController.cs) · `AccessDenied` dòng 204 |
| V013 | GET | `/admin/account/login` | [AccountController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AccountController.cs) · `Login` dòng 55 |
| V013 | POST | `/admin/account/login` | [AccountController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AccountController.cs) · `Login` dòng 69 |
| V013 | POST | `/admin/account/login` | [AccountController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AccountController.cs) · `Login` dòng 102 |
| V015 | GET | `/Admin/AdminMenus/Create` | [AdminMenusController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AdminMenusController.cs) · `Create` dòng 35 |
| V015 | POST | `/Admin/AdminMenus/Create` | [AdminMenusController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AdminMenusController.cs) · `Create` dòng 53 |
| V015 | POST | `/Admin/AdminMenus/Create` | [AdminMenusController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AdminMenusController.cs) · `Create` dòng 72 |
| V016 | GET | `/Admin/AdminMenus/Edit` | [AdminMenusController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AdminMenusController.cs) · `Edit` dòng 109 |
| V016 | POST | `/Admin/AdminMenus/Edit` | [AdminMenusController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AdminMenusController.cs) · `Edit` dòng 121 |
| V016 | POST | `/Admin/AdminMenus/Edit` | [AdminMenusController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AdminMenusController.cs) · `Edit` dòng 146 |
| V017 | GET | `/Admin/AdminMenus/Index` | [AdminMenusController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AdminMenusController.cs) · `Index` dòng 27 |
| V018 | GET | `/Admin/AdminMenuVisibility/Index` | [AdminMenuVisibilityController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AdminMenuVisibilityController.cs) · `Index` dòng 13 |
| V019 | GET | `/admin/assistant/knowledge` | [AssistantKnowledgeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AssistantKnowledgeController.cs) · `Index` dòng 45 |
| V019 | POST | `/admin/assistant/knowledge/save` | [AssistantKnowledgeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AssistantKnowledgeController.cs) · `Save` dòng 66 |
| V020 | GET | `/admin/assistant/quality` | [AssistantQualityController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AssistantQualityController.cs) · `Index` dòng 55 |
| V021 | GET | `/admin/assistant/search` | [AssistantSearchAdminController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AssistantSearchAdminController.cs) · `Index` dòng 34 |
| V022 | GET | `/admin/assistant/settings` | [AssistantSettingsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AssistantSettingsController.cs) · `Index` dòng 31 |
| V022 | POST | `/admin/assistant/settings` | [AssistantSettingsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AssistantSettingsController.cs) · `Save` dòng 51 |
| V023 | GET | `/Admin/AttributeValue/Search` | [AttributeValueController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AttributeValueController.cs) · `Search` dòng 83 |
| V024 | GET | `/Admin/AttributeValue/Create` | [AttributeValueController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AttributeValueController.cs) · `Create` dòng 105 |
| V024 | GET | `/Admin/AttributeValue/Edit` | [AttributeValueController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AttributeValueController.cs) · `Edit` dòng 160 |
| V024 | POST | `/Admin/AttributeValue/Create` | [AttributeValueController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AttributeValueController.cs) · `Create` dòng 121 |
| V024 | POST | `/Admin/AttributeValue/Create` | [AttributeValueController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AttributeValueController.cs) · `Create` dòng 138 |
| V024 | POST | `/Admin/AttributeValue/Edit` | [AttributeValueController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AttributeValueController.cs) · `Edit` dòng 187 |
| V024 | POST | `/Admin/AttributeValue/Edit` | [AttributeValueController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AttributeValueController.cs) · `Edit` dòng 206 |
| V025 | GET | `/Admin/AttributeValue/Index` | [AttributeValueController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AttributeValueController.cs) · `Index` dòng 52 |
| V026 | GET | `/admin/audit-logs` | [AuditLogsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AuditLogsController.cs) · `Index` dòng 16 |
| V027 | GET | `/Admin/AutoInvoice/Index` | [AutoInvoiceController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AutoInvoiceController.cs) · `Index` dòng 63 |
| V028 | GET | `/admin/bank-notifications/live-state` | [BankNotificationsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BankNotificationsController.cs) · `LiveState` dòng 31 |
| V029 | GET | `/admin/bank-notifications` | [BankNotificationsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BankNotificationsController.cs) · `Index` dòng 22 |
| V030 | GET | `/admin/barcode-history` | [BarcodeHistoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BarcodeHistoryController.cs) · `Index` dòng 35 |
| V031 | GET | `/admin/barcode-normalization` | [BarcodeNormalizationController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BarcodeNormalizationController.cs) · `Index` dòng 33 |
| V032 | GET | `/Admin/Brand/Search` | [BrandController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BrandController.cs) · `Search` dòng 68 |
| V033 | GET | `/Admin/Brand/Create` | [BrandController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BrandController.cs) · `Create` dòng 77 |
| V033 | GET | `/Admin/Brand/Edit` | [BrandController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BrandController.cs) · `Edit` dòng 129 |
| V033 | POST | `/Admin/Brand/Create` | [BrandController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BrandController.cs) · `Create` dòng 91 |
| V033 | POST | `/Admin/Brand/Create` | [BrandController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BrandController.cs) · `Create` dòng 108 |
| V033 | POST | `/Admin/Brand/Edit` | [BrandController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BrandController.cs) · `Edit` dòng 148 |
| V033 | POST | `/Admin/Brand/Edit` | [BrandController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BrandController.cs) · `Edit` dòng 167 |
| V034 | GET | `/Admin/Brand/Index` | [BrandController.cs](../../GaoApp.Web/Areas/Admin/Controllers/BrandController.cs) · `Index` dòng 50 |
| V035 | GET | `/Admin/Category/Search` | [CategoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CategoryController.cs) · `Search` dòng 183 |
| V036 | GET | `/Admin/Category/Create` | [CategoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CategoryController.cs) · `Create` dòng 197 |
| V036 | GET | `/Admin/Category/Edit` | [CategoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CategoryController.cs) · `Edit` dòng 251 |
| V036 | POST | `/Admin/Category/Create` | [CategoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CategoryController.cs) · `Create` dòng 218 |
| V036 | POST | `/Admin/Category/Create` | [CategoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CategoryController.cs) · `Create` dòng 225 |
| V036 | POST | `/Admin/Category/Edit` | [CategoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CategoryController.cs) · `Edit` dòng 268 |
| V036 | POST | `/Admin/Category/Edit` | [CategoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CategoryController.cs) · `Edit` dòng 275 |
| V037 | GET | `/Admin/Category/Index` | [CategoryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CategoryController.cs) · `Index` dòng 148 |
| V038 | GET | `/admin/reports/cost-adjustments` | [CostAdjustmentReportController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CostAdjustmentReportController.cs) · `Index` dòng 15 |
| V039 | GET | `/Admin/Customer/Search` | [CustomerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerController.cs) · `Search` dòng 64 |
| V040 | GET | `/Admin/Customer/Create` | [CustomerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerController.cs) · `Create` dòng 80 |
| V040 | GET | `/Admin/Customer/Edit` | [CustomerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerController.cs) · `Edit` dòng 112 |
| V040 | POST | `/Admin/Customer/Create` | [CustomerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerController.cs) · `Create` dòng 88 |
| V040 | POST | `/Admin/Customer/Create` | [CustomerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerController.cs) · `Create` dòng 93 |
| V040 | POST | `/Admin/Customer/Edit` | [CustomerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerController.cs) · `Edit` dòng 120 |
| V040 | POST | `/Admin/Customer/Edit` | [CustomerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerController.cs) · `Edit` dòng 125 |
| V041 | GET | `/Admin/Customer/Index` | [CustomerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerController.cs) · `Index` dòng 48 |
| V042 | GET | `/admin/customers/{id:int}/profile` | [CustomerProfileController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerProfileController.cs) · `Index` dòng 17 |
| V043 | GET | `/admin/customer-service/channels` | [CustomerChannelsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerChannelsController.cs) · `Index` dòng 17 |
| V044 | GET | `/admin/customer-debt/collections` | [CustomerDebtController.Reports.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerDebtController.Reports.cs) · `Collections` dòng 14 |
| V045 | GET | `/admin/customer-debt` | [CustomerDebtController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerDebtController.cs) · `Index` dòng 19 |
| V046 | GET | `/admin/customer-debt/receipts/{receiptId:int}` | [CustomerDebtController.Reports.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerDebtController.Reports.cs) · `Receipt` dòng 19 |
| V048 | GET | `/admin/customer-deposit` | [CustomerDepositController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerDepositController.cs) · `Index` dòng 35 |
| V049 | GET | `/admin/customer-deposit/{depositId:int}/print` | [CustomerDepositController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerDepositController.cs) · `Print` dòng 40 |
| V050 | GET | `/admin/customer-service` | [CustomerInboxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerInboxController.cs) · `Index` dòng 19 |
| V051 | GET | `/admin/customer-service/quality` | [CustomerQualityController.cs](../../GaoApp.Web/Areas/Admin/Controllers/CustomerQualityController.cs) · `Index` dòng 37 |
| V052 | GET | `/admin/deliveries/{id:int}/bill` | [DeliveryPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/DeliveryPageController.cs) · `Bill` dòng 60 |
| V053 | GET | `/admin/deliveries` | [DeliveryPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/DeliveryPageController.cs) · `Index` dòng 24 |
| V054 | GET | `/admin/deliveries/trips/{tripId:int}/outcomes` | [DeliveryPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/DeliveryPageController.cs) · `Outcomes` dòng 39 |
| V055 | GET | `/admin/deliveries/trips` | [DeliveryPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/DeliveryPageController.cs) · `Trips` dòng 31 |
| V057 | ANY | `/Admin/DisplayPromotion/Index` | [DisplayPromotionController.cs](../../GaoApp.Web/Areas/Admin/Controllers/DisplayPromotionController.cs) · `Index` dòng 59 |
| V058 | GET | `/error/{statusCode:int}` | [ErrorController.cs](../../GaoApp.Web/Controllers/ErrorController.cs) · `Handle` dòng 25 |
| V059 | GET/GET/GET | `/admin/dashboard` | [HomeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/HomeController.cs) · `Index` dòng 19 |
| V059 | GET/GET/GET | `/admin/home` | [HomeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/HomeController.cs) · `Index` dòng 19 |
| V060 | GET | `/admin/input-invoices` | [InputInvoiceLibraryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InputInvoiceLibraryController.cs) · `Index` dòng 19 |
| V063 | GET | `/admin/inventory-adjustment-documents/create` | [InventoryAdjustmentDocumentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryAdjustmentDocumentsController.cs) · `Create` dòng 35 |
| V064 | GET | `/admin/inventory-adjustment-documents/{id:int}` | [InventoryAdjustmentDocumentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryAdjustmentDocumentsController.cs) · `Detail` dòng 43 |
| V065 | GET | `/admin/inventory-adjustment-documents/{id:int}/edit` | [InventoryAdjustmentDocumentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryAdjustmentDocumentsController.cs) · `Edit` dòng 52 |
| V066 | GET | `/admin/inventory-adjustment-documents` | [InventoryAdjustmentDocumentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryAdjustmentDocumentsController.cs) · `Index` dòng 27 |
| V067 | GET | `/admin/inventory-inquiry` | [InventoryInquiryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryInquiryController.cs) · `Index` dòng 28 |
| V068 | GET | `/admin/inventory-issues/{id:int}` | [InventoryIssueManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryIssueManagementController.cs) · `Detail` dòng 196 |
| V069 | GET | `/admin/inventory-issues` | [InventoryIssueManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryIssueManagementController.cs) · `Index` dòng 86 |
| V070 | GET | `/admin/inventory-ledger` | [InventoryLedgerController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InventoryLedgerController.cs) · `Index` dòng 28 |
| V071 | GET | `/Admin/Invoice/CreateManualDetail` | [InvoiceController.ManualDetail.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceController.ManualDetail.cs) · `CreateManualDetail` dòng 20 |
| V079 | GET | `/Admin/Invoice/InvoiceSummaryPartial` | [InvoiceController.List.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceController.List.cs) · `InvoiceSummaryPartial` dòng 85 |
| V080 | GET | `/Admin/Invoice/ManualLinesPartial` | [InvoiceController.List.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceController.List.cs) · `ManualLinesPartial` dòng 98 |
| V082 | GET | `/Admin/Invoice/Detail` | [InvoiceController.List.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceController.List.cs) · `Detail` dòng 72 |
| V083 | GET | `/Admin/Invoice/Index` | [InvoiceController.List.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceController.List.cs) · `Index` dòng 25 |
| V083 | GET | `/Admin/Invoice/Index` | [InvoiceController.List.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceController.List.cs) · `Index` dòng 50 |
| V084 | GET | `/Admin/Invoice/ViettelDashboard` | [InvoiceController.ViettelDashboard.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceController.ViettelDashboard.cs) · `ViettelDashboard` dòng 32 |
| V085 | GET | `/Admin/Invoice/ViettelPayload` | [InvoiceController.Viettel.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceController.Viettel.cs) · `ViettelPayload` dòng 44 |
| V086 | GET | `/Admin/InvoiceCorrection/Create` | [InvoiceCorrectionController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceCorrectionController.cs) · `Create` dòng 46 |
| V086 | POST | `/Admin/InvoiceCorrection/Create` | [InvoiceCorrectionController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceCorrectionController.cs) · `Create` dòng 84 |
| V087 | GET | `/Admin/InvoiceDashboard/Index` | [InvoiceDashboardController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceDashboardController.cs) · `Index` dòng 38 |
| V087 | GET | `/Admin/InvoiceDashboard/Index` | [InvoiceDashboardController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceDashboardController.cs) · `Index` dòng 44 |
| V088 | GET | `/admin/invoice-input-stock` | [InvoiceInputStockController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceInputStockController.cs) · `Index` dòng 16 |
| V089 | GET | `/Admin/InvoiceIntegrationLog/Cleanup` | [InvoiceIntegrationLogController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceIntegrationLogController.cs) · `Cleanup` dòng 79 |
| V089 | POST | `/Admin/InvoiceIntegrationLog/Cleanup` | [InvoiceIntegrationLogController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceIntegrationLogController.cs) · `Cleanup` dòng 105 |
| V089 | POST | `/Admin/InvoiceIntegrationLog/Cleanup` | [InvoiceIntegrationLogController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceIntegrationLogController.cs) · `Cleanup` dòng 115 |
| V090 | GET | `/Admin/InvoiceIntegrationLog/Detail` | [InvoiceIntegrationLogController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceIntegrationLogController.cs) · `Detail` dòng 66 |
| V091 | GET | `/Admin/InvoiceIntegrationLog/Index` | [InvoiceIntegrationLogController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceIntegrationLogController.cs) · `Index` dòng 40 |
| V091 | GET | `/Admin/InvoiceIntegrationLog/Index` | [InvoiceIntegrationLogController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceIntegrationLogController.cs) · `Index` dòng 46 |
| V092 | GET | `/Admin/InvoiceProviderSetting/Create` | [InvoiceProviderSettingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceProviderSettingController.cs) · `Create` dòng 54 |
| V092 | GET | `/Admin/InvoiceProviderSetting/Edit` | [InvoiceProviderSettingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceProviderSettingController.cs) · `Edit` dòng 92 |
| V092 | POST | `/Admin/InvoiceProviderSetting/Create` | [InvoiceProviderSettingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceProviderSettingController.cs) · `Create` dòng 65 |
| V092 | POST | `/Admin/InvoiceProviderSetting/Create` | [InvoiceProviderSettingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceProviderSettingController.cs) · `Create` dòng 72 |
| V092 | POST | `/Admin/InvoiceProviderSetting/Edit` | [InvoiceProviderSettingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceProviderSettingController.cs) · `Edit` dòng 103 |
| V092 | POST | `/Admin/InvoiceProviderSetting/Edit` | [InvoiceProviderSettingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceProviderSettingController.cs) · `Edit` dòng 110 |
| V093 | GET | `/Admin/InvoiceProviderSetting/Index` | [InvoiceProviderSettingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceProviderSettingController.cs) · `Index` dòng 29 |
| V093 | GET | `/Admin/InvoiceProviderSetting/Index` | [InvoiceProviderSettingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceProviderSettingController.cs) · `Index` dòng 32 |
| V094 | POST | `/admin/kiosks/create` | [KioskManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/KioskManagementController.cs) · `Create` dòng 51 |
| V094 | POST | `/admin/kiosks/{id:int}/{actionName}` | [KioskManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/KioskManagementController.cs) · `Action` dòng 66 |
| V095 | GET | `/admin/kiosks` | [KioskManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/KioskManagementController.cs) · `Index` dòng 33 |
| V097 | GET | `/admin/label-printing` | [LabelPrintingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/LabelPrintingController.cs) · `Index` dòng 23 |
| V097 | GET | `/admin/label-printing-settings` | [LabelPrintingSettingsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/LabelPrintingSettingsController.cs) · `Index` dòng 19 |
| V098 | GET | `/admin/pos/legacy-returns/{legacyOrderId:long}` | [LegacyReturnArchiveController.cs](../../GaoApp.Web/Areas/Admin/Controllers/LegacyReturnArchiveController.cs) · `Details` dòng 49 |
| V099 | GET | `/admin/pos/legacy-returns` | [LegacyReturnArchiveController.cs](../../GaoApp.Web/Areas/Admin/Controllers/LegacyReturnArchiveController.cs) · `Index` dòng 38 |
| V100 | GET | `/Admin/LegalEntityManagement/Index` | [LegalEntityManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/LegalEntityManagementController.cs) · `Index` dòng 12 |
| V101 | GET | `/admin/legal-entities/reconciliation` | [LegalEntityReconciliationController.cs](../../GaoApp.Web/Areas/Admin/Controllers/LegalEntityReconciliationController.cs) · `Index` dòng 37 |
| V102 | GET | `/admin/reports/expenses` | [OperatingExpenseController.cs](../../GaoApp.Web/Areas/Admin/Controllers/OperatingExpenseController.cs) · `Index` dòng 19 |
| V103 | GET | `/admin/media/test` | [MediaController.cs](../../GaoApp.Web/Areas/Admin/Controllers/MediaController.cs) · `Test` dòng 78 |
| V104 | GET | `/admin/media-library` | [MediaLibraryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/MediaLibraryController.cs) · `Index` dòng 102 |
| V121 | GET | `/admin/pos/customer-display` | [POSController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSController.cs) · `CustomerDisplay` dòng 1244 |
| V122 | GET | `/admin/pos/dashboard` | [POSController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSController.cs) · `Dashboard` dòng 924 |
| V125 | GET | `/admin/pos/order-detail/{orderId:int}` | [POSOrderDetailController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSOrderDetailController.cs) · `Index` dòng 55 |
| V126 | GET | `/admin/pos/orders-page` | [POSOrderPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSOrderPageController.cs) · `Index` dòng 27 |
| V130 | GET | `/admin/pos-shift/requests` | [POSRequestsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSRequestsController.cs) · `Index` dòng 18 |
| V131 | GET | `/admin/pos/returns/pending-restock` | [POSReturnController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSReturnController.cs) · `PendingRestock` dòng 80 |
| V132 | GET | `/admin/pos-shift/closing-slip-print` | [POSShiftClosingSlipPrintPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSShiftClosingSlipPrintPageController.cs) · `Index` dòng 21 |
| V133 | GET | `/admin/pos-shift/handover-slips` | [POSShiftHandoverSlipPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSShiftHandoverSlipPageController.cs) · `Index` dòng 29 |
| V134 | GET | `/admin/pos-shift/handover-slip-print` | [POSShiftHandoverSlipPrintPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSShiftHandoverSlipPrintPageController.cs) · `Index` dòng 21 |
| V135 | GET | `/admin/pos-shift/history` | [POSShiftHistoryPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSShiftHistoryPageController.cs) · `Index` dòng 27 |
| V136 | GET | `/admin/pos-shift/manager-dashboard` | [POSShiftManagerDashboardPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSShiftManagerDashboardPageController.cs) · `Index` dòng 29 |
| V137 | GET | `/admin/pos-shift` | [POSShiftPageController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSShiftPageController.cs) · `Index` dòng 26 |
| V138 | GET | `/admin/pos-shift/reconciliation` | [POSShiftReconciliationController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSShiftReconciliationController.cs) · `Index` dòng 14 |
| V139 | GET | `/admin/pos-terminals/create` | [POSTerminalsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSTerminalsController.cs) · `Create` dòng 33 |
| V139 | POST | `/admin/pos-terminals/create` | [POSTerminalsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSTerminalsController.cs) · `Create` dòng 41 |
| V139 | POST | `/admin/pos-terminals/create` | [POSTerminalsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSTerminalsController.cs) · `Create` dòng 47 |
| V139 | POST | `/admin/pos-terminals/create` | [POSTerminalsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSTerminalsController.cs) · `Create` dòng 60 |
| V140 | GET | `/admin/pos-terminals/{terminalId:int}/keys/create` | [POSTerminalsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSTerminalsController.cs) · `CreateKey` dòng 72 |
| V140 | POST | `/admin/pos-terminals/{terminalId:int}/keys/create` | [POSTerminalsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSTerminalsController.cs) · `CreateKey` dòng 84 |
| V141 | GET | `/admin/pos-terminals` | [POSTerminalsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSTerminalsController.cs) · `Index` dòng 29 |
| V142 | POST | `/admin/pos-terminals/{terminalId:int}/keys/create` | [POSTerminalsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSTerminalsController.cs) · `CreateKey` dòng 94 |
| V143 | ANY | `/Admin/Product/Search` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Search` dòng 256 |
| V146 | GET | `/Admin/Product/Create` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Create` dòng 285 |
| V146 | POST | `/Admin/Product/Create` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Create` dòng 302 |
| V146 | POST | `/Admin/Product/Create` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Create` dòng 310 |
| V147 | GET | `/Admin/Product/Detail` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Detail` dòng 447 |
| V148 | GET | `/Admin/Product/Edit` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Edit` dòng 346 |
| V148 | POST | `/Admin/Product/Edit` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Edit` dòng 367 |
| V148 | POST | `/Admin/Product/Edit` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Edit` dòng 380 |
| V149 | ANY | `/Admin/Product/Index` | [ProductController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductController.cs) · `Index` dòng 222 |
| V150 | GET | `/Admin/ProductAttribute/Search` | [ProductAttributeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductAttributeController.cs) · `Search` dòng 73 |
| V151 | GET | `/Admin/ProductAttribute/Create` | [ProductAttributeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductAttributeController.cs) · `Create` dòng 89 |
| V151 | GET | `/Admin/ProductAttribute/Edit` | [ProductAttributeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductAttributeController.cs) · `Edit` dòng 137 |
| V151 | POST | `/Admin/ProductAttribute/Create` | [ProductAttributeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductAttributeController.cs) · `Create` dòng 97 |
| V151 | POST | `/Admin/ProductAttribute/Create` | [ProductAttributeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductAttributeController.cs) · `Create` dòng 115 |
| V151 | POST | `/Admin/ProductAttribute/Edit` | [ProductAttributeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductAttributeController.cs) · `Edit` dòng 153 |
| V151 | POST | `/Admin/ProductAttribute/Edit` | [ProductAttributeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductAttributeController.cs) · `Edit` dòng 173 |
| V152 | GET | `/Admin/ProductAttribute/Index` | [ProductAttributeController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProductAttributeController.cs) · `Index` dòng 46 |
| V153 | GET | `/admin/reports/profit/detail` | [ProfitReportController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProfitReportController.cs) · `Detail` dòng 18 |
| V154 | GET | `/admin/reports/profit` | [ProfitReportController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ProfitReportController.cs) · `Index` dòng 15 |
| V155 | GET | `/Admin/Promotion/Search` | [PromotionController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PromotionController.cs) · `Search` dòng 100 |
| V156 | GET | `/Admin/Promotion/Index` | [PromotionController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PromotionController.cs) · `Index` dòng 60 |
| V157 | GET | `/admin/promotion-labels` | [PromotionLabelsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PromotionLabelsController.cs) · `Index` dòng 17 |
| V158 | GET | `/admin/purchase-orders/{id:int}/edit` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `Edit` dòng 188 |
| V158 | POST | `/admin/purchase-orders/{id:int}/commercial` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `UpdateCommercial` dòng 282 |
| V158 | POST | `/admin/purchase-orders/{id:int}/commercial` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `UpdateCommercial` dòng 293 |
| V159 | GET | `/admin/purchase-orders/{id:int}` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `Details` dòng 328 |
| V160 | GET | `/admin/purchase-orders/create` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `Create` dòng 101 |
| V160 | GET | `/admin/purchase-orders/{id:int}/edit` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `Edit` dòng 213 |
| V160 | POST | `/admin/purchase-orders/save` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `Save` dòng 253 |
| V160 | POST | `/admin/purchase-orders/save` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `Save` dòng 264 |
| V161 | GET | `/admin/purchase-orders` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `Index` dòng 60 |
| V162 | GET | `/admin/purchase-orders/{id:int}/print` | [PurchaseOrdersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseOrdersController.cs) · `Print` dòng 420 |
| V163 | GET | `/admin/purchase-receiving/{id:int}` | [PurchaseReceivingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseReceivingController.cs) · `Index` dòng 67 |
| V164 | GET | `/admin/purchase-requests/{id:int}` | [PurchaseRequestsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseRequestsController.cs) · `Details` dòng 368 |
| V165 | GET | `/admin/purchase-requests/create` | [PurchaseRequestsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseRequestsController.cs) · `Create` dòng 126 |
| V165 | GET | `/admin/purchase-requests/{id:int}/edit` | [PurchaseRequestsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseRequestsController.cs) · `Edit` dòng 167 |
| V165 | POST | `/admin/purchase-requests/save` | [PurchaseRequestsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseRequestsController.cs) · `Save` dòng 328 |
| V165 | POST | `/admin/purchase-requests/save` | [PurchaseRequestsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseRequestsController.cs) · `Save` dòng 341 |
| V166 | GET | `/admin/purchase-requests` | [PurchaseRequestsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseRequestsController.cs) · `Index` dòng 53 |
| V167 | GET | `/admin/purchase-requests/{id:int}/prepare` | [PurchaseRequestsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/PurchaseRequestsController.cs) · `Prepare` dòng 254 |
| V168 | GET | `/admin/receipt-templates` | [ReceiptTemplatesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ReceiptTemplatesController.cs) · `Index` dòng 37 |
| V169 | GET | `/admin/pos/orders/{orderId:int}/print` | [POSController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSController.cs) · `PrintReceipt` dòng 538 |
| V169 | GET | `/admin/pos/receipt/{orderId:int}` | [POSReceiptController.cs](../../GaoApp.Web/Areas/Admin/Controllers/POSReceiptController.cs) · `Index` dòng 118 |
| V170 | GET | `/admin/reward-vouchers/settings` | [RewardSettingsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RewardSettingsController.cs) · `Index` dòng 37 |
| V170 | POST | `/admin/reward-vouchers/settings` | [RewardSettingsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RewardSettingsController.cs) · `Save` dòng 52 |
| V170 | POST | `/admin/reward-vouchers/settings` | [RewardSettingsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RewardSettingsController.cs) · `Save` dòng 86 |
| V171 | GET | `/admin/reward-vouchers` | [RewardVouchersController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RewardVouchersController.cs) · `Index` dòng 24 |
| V173 | GET | `/Admin/RolePermissions/Index` | [RolePermissionsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolePermissionsController.cs) · `Index` dòng 41 |
| V173 | GET | `/Admin/RolePermissions/Index` | [RolePermissionsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolePermissionsController.cs) · `Index` dòng 52 |
| V174 | GET | `/Admin/Roles/Create` | [RolesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolesController.cs) · `Create` dòng 66 |
| V174 | POST | `/Admin/Roles/Create` | [RolesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolesController.cs) · `Create` dòng 80 |
| V174 | POST | `/Admin/Roles/Create` | [RolesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolesController.cs) · `Create` dòng 94 |
| V175 | GET | `/Admin/Roles/Edit` | [RolesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolesController.cs) · `Edit` dòng 123 |
| V175 | POST | `/Admin/Roles/Edit` | [RolesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolesController.cs) · `Edit` dòng 139 |
| V175 | POST | `/Admin/Roles/Edit` | [RolesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolesController.cs) · `Edit` dòng 155 |
| V175 | POST | `/Admin/Roles/Edit` | [RolesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolesController.cs) · `Edit` dòng 161 |
| V176 | GET | `/Admin/Roles/Index` | [RolesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/RolesController.cs) · `Index` dòng 35 |
| V177 | GET | `/admin/reports/overview` | [SalesExecutiveReportController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SalesExecutiveReportController.cs) · `Index` dòng 25 |
| V178 | GET | `/admin/reports/sales` | [SalesReportController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SalesReportController.cs) · `Index` dòng 25 |
| V179 | GET | `/admin/acb/reconciliation/callback-status` | [AcbReconciliationController.cs](../../GaoApp.Web/Areas/Admin/Controllers/AcbReconciliationController.cs) · `CallbackStatus` dòng 35 |
| V211 | GET | `/admin/stock-counts/{id:int}` | [StockCountPagesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockCountPagesController.cs) · `Detail` dòng 33 |
| V212 | GET | `/admin/stock-counts` | [StockCountPagesController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockCountPagesController.cs) · `Index` dòng 25 |
| V216 | GET | `/admin/api/stock-documents/split/invoice-picker/xml` | [StockDocumentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs) · `PreviewSplitInvoiceXml` dòng 775 |
| V216 | GET | `/admin/api/stock-documents/{id:int}/input-invoices/picker/xml` | [StockDocumentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs) · `PreviewInputInvoiceXml` dòng 866 |
| V216 | GET | `/admin/api/stock-documents/{id:int}/input-invoices/{inputInvoiceId:int}/preview/xml` | [StockDocumentsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs) · `PreviewLinkedInputInvoiceXml` dòng 949 |
| V216 | GET | `/admin/input-invoices/{id:int}/preview` | [InputInvoiceLibraryController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InputInvoiceLibraryController.cs) · `Preview` dòng 28 |
| V218 | GET | `/admin/stock-documents/lines-table` | [StockDocumentManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs) · `LinesTable` dòng 352 |
| V219 | GET | `/admin/stock-documents/{id:int}` | [StockDocumentManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs) · `Edit` dòng 96 |
| V220 | GET | `/admin/stock-documents` | [StockDocumentManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs) · `Index` dòng 49 |
| V221 | GET | `/admin/stock-documents/{id:int}/split` | [StockDocumentManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs) · `Split` dòng 153 |
| V222 | GET | `/admin/stock-transfers/{id:int}` | [StockTransferController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockTransferController.cs) · `Detail` dòng 33 |
| V223 | GET | `/admin/stock-transfers` | [StockTransferController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StockTransferController.cs) · `Index` dòng 25 |
| V224 | GET | `/Admin/StoreBankAccounts/Search` | [StoreBankAccountsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StoreBankAccountsController.cs) · `Search` dòng 51 |
| V225 | GET | `/Admin/StoreBankAccounts/Edit` | [StoreBankAccountsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StoreBankAccountsController.cs) · `Edit` dòng 60 |
| V225 | GET | `/Admin/StoreBankAccounts/Edit` | [StoreBankAccountsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StoreBankAccountsController.cs) · `Edit` dòng 71 |
| V225 | POST | `/Admin/StoreBankAccounts/Edit` | [StoreBankAccountsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StoreBankAccountsController.cs) · `Edit` dòng 81 |
| V226 | GET | `/Admin/StoreBankAccounts/Index` | [StoreBankAccountsController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StoreBankAccountsController.cs) · `Index` dòng 28 |
| V227 | GET | `/admin/store-monitor` | [StoreMonitorController.cs](../../GaoApp.Web/Areas/Admin/Controllers/StoreMonitorController.cs) · `Index` dòng 23 |
| V228 | GET | `/Admin/Supplier/Search` | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) · `Search` dòng 75 |
| V230 | GET | `/Admin/Supplier/Create` | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) · `Create` dòng 83 |
| V230 | GET | `/Admin/Supplier/Edit` | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) · `Edit` dòng 140 |
| V230 | POST | `/Admin/Supplier/Create` | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) · `Create` dòng 91 |
| V230 | POST | `/Admin/Supplier/Create` | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) · `Create` dòng 118 |
| V230 | POST | `/Admin/Supplier/Edit` | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) · `Edit` dòng 165 |
| V230 | POST | `/Admin/Supplier/Edit` | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) · `Edit` dòng 194 |
| V231 | GET | `/Admin/Supplier/Index` | [SupplierController.cs](../../GaoApp.Web/Areas/Admin/Controllers/SupplierController.cs) · `Index` dòng 52 |
| V232 | GET | `/Admin/Tax/Search` | [TaxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/TaxController.cs) · `Search` dòng 77 |
| V233 | GET | `/Admin/Tax/Create` | [TaxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/TaxController.cs) · `Create` dòng 94 |
| V233 | GET | `/Admin/Tax/Edit` | [TaxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/TaxController.cs) · `Edit` dòng 144 |
| V233 | POST | `/Admin/Tax/Create` | [TaxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/TaxController.cs) · `Create` dòng 101 |
| V233 | POST | `/Admin/Tax/Create` | [TaxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/TaxController.cs) · `Create` dòng 121 |
| V233 | POST | `/Admin/Tax/Edit` | [TaxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/TaxController.cs) · `Edit` dòng 152 |
| V233 | POST | `/Admin/Tax/Edit` | [TaxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/TaxController.cs) · `Edit` dòng 176 |
| V234 | GET | `/Admin/Tax/Index` | [TaxController.cs](../../GaoApp.Web/Areas/Admin/Controllers/TaxController.cs) · `Index` dòng 48 |
| V235 | GET | `/Admin/Unit/Search` | [UnitController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UnitController.cs) · `Search` dòng 107 |
| V236 | GET | `/Admin/Unit/Create` | [UnitController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UnitController.cs) · `Create` dòng 121 |
| V236 | GET | `/Admin/Unit/Edit` | [UnitController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UnitController.cs) · `Edit` dòng 201 |
| V236 | POST | `/Admin/Unit/Create` | [UnitController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UnitController.cs) · `Create` dòng 136 |
| V236 | POST | `/Admin/Unit/Create` | [UnitController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UnitController.cs) · `Create` dòng 162 |
| V236 | POST | `/Admin/Unit/Edit` | [UnitController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UnitController.cs) · `Edit` dòng 222 |
| V236 | POST | `/Admin/Unit/Edit` | [UnitController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UnitController.cs) · `Edit` dòng 250 |
| V237 | GET | `/Admin/Unit/Index` | [UnitController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UnitController.cs) · `Index` dòng 67 |
| V238 | GET | `/Admin/UserInStores/Create` | [UserInStoresController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs) · `Create` dòng 90 |
| V238 | POST | `/Admin/UserInStores/Create` | [UserInStoresController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs) · `Create` dòng 115 |
| V238 | POST | `/Admin/UserInStores/Create` | [UserInStoresController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs) · `Create` dòng 122 |
| V239 | GET | `/Admin/UserInStores/Edit` | [UserInStoresController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs) · `Edit` dòng 175 |
| V239 | POST | `/Admin/UserInStores/Edit` | [UserInStoresController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs) · `Edit` dòng 210 |
| V239 | POST | `/Admin/UserInStores/Edit` | [UserInStoresController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs) · `Edit` dòng 217 |
| V239 | POST | `/Admin/UserInStores/Edit` | [UserInStoresController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs) · `Edit` dòng 249 |
| V240 | GET | `/Admin/UserInStores/Index` | [UserInStoresController.cs](../../GaoApp.Web/Areas/Admin/Controllers/UserInStoresController.cs) · `Index` dòng 48 |
| V241 | GET | `/Admin/ViettelInvoiceSync/Index` | [ViettelInvoiceSyncController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ViettelInvoiceSyncController.cs) · `Index` dòng 25 |
| V241 | POST | `/Admin/ViettelInvoiceSync/Index` | [ViettelInvoiceSyncController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ViettelInvoiceSyncController.cs) · `Index` dòng 49 |
| V241 | POST | `/Admin/ViettelInvoiceSync/Index` | [ViettelInvoiceSyncController.cs](../../GaoApp.Web/Areas/Admin/Controllers/ViettelInvoiceSyncController.cs) · `Index` dòng 57 |
| V242 | GET | `/Admin/WarehouseManagement/Index` | [WarehouseManagementController.cs](../../GaoApp.Web/Areas/Admin/Controllers/WarehouseManagementController.cs) · `Index` dòng 14 |
| V245 | GET | `/admin/warehouse-receiving/{id:int}/lines` | [WarehouseReceivingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/WarehouseReceivingController.cs) · `Lines` dòng 238 |
| V247 | GET | `/admin/warehouse-receiving/{id:int}` | [WarehouseReceivingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/WarehouseReceivingController.cs) · `Detail` dòng 74 |
| V248 | GET | `/admin/warehouse-receiving` | [WarehouseReceivingController.cs](../../GaoApp.Web/Areas/Admin/Controllers/WarehouseReceivingController.cs) · `Index` dòng 42 |
| V251 | GET | `/support` | [CustomerSupportController.cs](../../GaoApp.Web/Controllers/CustomerSupportController.cs) · `Index` dòng 25 |
| V252 | GET | `/error/{statusCode:int}` | [ErrorController.cs](../../GaoApp.Web/Controllers/ErrorController.cs) · `Handle` dòng 29 |
| V253 | ANY | `/Home/Index` | [HomeController.cs](../../GaoApp.Web/Controllers/HomeController.cs) · `Index` dòng 18 |
| V254 | POST | `/Home/Privacy` | [HomeController.cs](../../GaoApp.Web/Controllers/HomeController.cs) · `Privacy` dòng 24 |
| V255 | POST | `/invoice-info/{token}` | [InvoiceBuyerSelfServiceController.cs](../../GaoApp.Web/Areas/Admin/Controllers/InvoiceBuyerSelfServiceController.cs) · `Submit` dòng 115 |
| V257 | GET | `/kiosk` | [KioskController.cs](../../GaoApp.Web/Controllers/KioskController.cs) · `Index` dòng 21 |
| V260 | ANY | `/Home/Error` | [HomeController.cs](../../GaoApp.Web/Controllers/HomeController.cs) · `Error` dòng 30 |

## Quy trình cập nhật sau mỗi bước

1. Đối chiếu file thay đổi với đường dẫn và hash baseline; bổ sung view mới ngay khi phát sinh.
2. Trong JSON, đổi `status` của view và tài nguyên thuộc bước; ghi trạng thái kỹ thuật và kết quả xem trên thiết bị thực khi có.
3. Trong kế hoạch chính, cập nhật tiến độ bước, đường dẫn bằng chứng kiểm thử và mục còn lại.
4. Giữ mọi file còn trong kiểm kê ở một trạng thái rõ: đã nâng cấp, giữ có lý do, hoặc được xác nhận thay thế. Không dùng số file xóa làm thước đo hoàn thành.
5. Trước UI-24, quét lại source để đối chiếu view mới, view đổi tên, workspace và route; hoàn tất các ca nghiệm thu chưa có bằng chứng.
