# A3 — Audit source nhiều phân hệ; BEFORE runtime bị chặn

Ngày 02/11/2026. **SOURCE VERIFIED** cho mapping dưới đây. **NOT TESTED** cho browser, role demo, dữ liệu, focus thực tế, click count, overflow, accessibility và thời gian. Gate **BLOCKED** vì chưa mở được app test an toàn và chưa có hai màn quan trọng chụp BEFORE mobile/desktop.

Đã khảo sát **11 nhóm** có route/controller/view thật. Không mặc định quyền test đã được cấp chỉ vì có `[Authorize]`. Các policy dưới đây là symbol từ source; chưa kiểm tra HTTP 401/403 hoặc Store/terminal bằng tài khoản demo. Mọi row count hiện tại UNKNOWN.

## Mapping và nhận xét có giới hạn

Tên controller trong bảng nằm dưới `GaoApp.Web/Areas/Admin/Controllers/`; view dưới `GaoApp.Web/Areas/Admin/Views/`; asset dưới `GaoApp.Web/wwwroot/Admin/`. Conventional route được đối chiếu `Program.cs:573–579`, route attribute được ghi riêng. Các đường dẫn này để người dùng tự cấu hình menu khi cần.

| Nhóm / route | Controller → View → JS/CSS | Quyền/call chain đã thấy | Nhận xét source và điểm phải quan sát |
|---|---|---|---|
| Đăng nhập/terminal: `/admin/account/login` | `AccountController.cs:49–55` Login/BuildLoginVm → `Account/Login.cshtml` | AllowAnonymous cho login; model dùng nguồn terminal/account. | Phải quan sát chọn quầy, phiên và điều hướng bằng demo; không login tài khoản đang bán. Chưa audit toàn bộ validation/backend login. |
| POS: `/admin/pos`, `/admin/pos/v3` | `POSController.cs:860–919` → `POS/Index.cshtml`, `_Toolbar.cshtml`, `_CartTable.cshtml` → `js/pos/pos.barcode.js`, `pos.render.js`; `css/pos/pos.entry.mobile.css`, `pos.cart.mobile.css` | `Pos.Order.View`; `products/search` tại controller:927–931 → `POSService.SearchProductsForPOSAsync` tại Application/Services/Orders/POSService.cs:3509. | Input/giỏ có owner và hooks sẵn. Test Node scan race pass không chứng minh focus/lag browser. Giỏ dài BLOCKED nếu chưa có sandbox không ghi giao dịch. |
| Giá/khuyến mãi: `/Admin/Promotion` | `PromotionController.cs:33–69` → `Promotion/Index.cshtml` → `js/promotions.page.js`, `promotions.mobile.js`; CSS tương ứng | `Catalog.Promotion.View`; `IPromotionAdminService.GetPagedAsync`, pageSize tối đa 200. | Có lọc type/tier/date/status; cần xem mật độ nhãn và phân biệt giá áp dụng/preview. Không mở lệnh Save/Toggle/Delete hoặc thay quy tắc giá. |
| Sản phẩm **P1**: `/Admin/Product`, `/Admin/Product/Search` | `ProductController.cs:97–163,190–257` → `Product/Index.cshtml`, `_ProductTable.cshtml` → `products.index.js`, `products.filters.js`, `products.mobile.js`; CSS index/mobile | `Catalog.Product.View`; supplier filter cần `Catalog.Supplier.View`; ProductService → `Repositories/Products/ProductRepository.cs:51–171`. | Paging server 20 mặc định, tối đa 200; hủy response cũ, loading, URL state, empty và mobile cards đã có. Không đề xuất xây lại các chức năng này. |
| Tồn kho: `/admin/inventory-inquiry`, `/data`, `/quick-view` | `InventoryInquiryController.cs:10–43` → `InventoryInquiry/Index.cshtml` → `inventory-inquiry.js`, `inventory-lookup.mobile.js`; CSS tương ứng | `Inventory.Balance.View`; `InventoryInquiryReadService.GetPageAsync` → `InventoryInquiryReadRepository.cs:49–101`, Count/Skip/Take. | Đã có trang dữ liệu phân trang và live/busy region. Cần đo filter theo kho, phân biệt đơn vị và quyền xem cost; không trộn Store/LegalEntity. |
| Đơn mua/nhập hàng: `/admin/purchase-orders`, `/data`, `/filter-options`, `/quick-view` | `PurchaseOrdersController.cs:18–94` → `PurchaseOrders/Index.cshtml` → `purchase-order-index.js`, `purchasing.mobile.js`; CSS tương ứng | Controller `[Authorize]`, read-service/access/cost checks; `PurchaseOrderIndexReadService.GetPageAsync` → repository Count/Skip/Take:54–108. | Source có list và quick-view; cần role mua hàng/view-cost thực tế. Không mở create/edit/approve/confirm hoặc đồng nhất trạng thái đã đặt/đã nhập. Audit này chỉ bao phủ list đơn mua, chưa xác nhận toàn bộ receipt workflow. |
| Đơn POS: `/admin/pos/orders-page`, dữ liệu `/admin/pos/orders` | `POSOrderPageController.cs:11–27`; `POSController.cs:543–554` → `POSOrderPage/Index.cshtml` → `js/pos/pos.orders.page.js`, filters/mobile; `css/pos/pos-orders-page.css`, mobile | `Pos.Order.View`; `_pos.GetOrdersAsync(query)`; page header cần POS context. | Filter/paging/receipt drawer có sẵn và đã có quyết định UI riêng. Cần kiểm tra giữ filter khi reload/back, mất phiên và loading. Không bấm ACB/in/hoàn/hủy. |
| Hóa đơn điện tử: `/Admin/Invoice` | `InvoiceController.cs:10–12`, `InvoiceController.List.cs:11–50` → `Invoice/Index.cshtml` → `invoice-order-draft.js`, `invoice-workspace.mobile.js`; invoice-index/mobile CSS | `System.Integration.Manage`; `InvoiceService.GetInvoicesAsync` → read-service; normalize query và PagedResult. | Có xử lý lỗi list và page metadata. Đọc list không chứng minh transport an toàn; tránh phát hành/điều chỉnh/thay thế/đồng bộ provider. Không lấy ảnh chứa dữ liệu pháp lý thật. |
| Khách hàng/cờ công nợ: `/Admin/Customer`, `/Search`, `/QuickView` | `CustomerController.cs:34–72` → `Customer/Index.cshtml`, `_CustomerTable.cshtml` → `customer.js`, `customer-directory.mobile.js`; CSS tương ứng | `Catalog.Customer.View`; `CustomerManagementService.GetPageAsync:35–49` → summary/page repository; filter haveDebt/status/tier. | PII và phân quyền là điều kiện chụp. Có live/busy region; cần quan sát lọc, error/empty và focus thực. Chưa audit các lệnh tài chính công nợ riêng. |
| Báo cáo: `/admin/reports/sales` và `/context,/orders,/products,...` | `SalesReportController.cs:10–49` → `SalesReport/Index.cshtml` → `js/reports/sales-report.page.js`, `report-detail.mobile.js`; ApexCharts và CSS báo cáo | `Report.Sales.View`; `SalesReportReadService` gọi repository với RequireStoreId và resolved query. | Tách thời gian API/tổng hợp/chart khỏi thời gian navigation; role/time zone/terminal/date phải cố định. Không nhận là đã audit mọi dashboard/report trong source. |
| Quản trị/menu: `/Admin/AdminMenus` | `AdminMenusController.cs:11–27` → `AdminMenus/Index.cshtml` → `admin-menu-index.js`; `admin-menu-index.css`, `gaoapp.menu-management.mobile.css` | `Security.Role.Permissions`; `AdminMenuService.GetForAdminAsync(CurrentStoreId)`; model menu cấu hình. | Chỉ mapping/quan sát dự kiến. Không create/edit/pin/ẩn-hiện menu hoặc đổi role. Không tạo menu giả để đủ audit; role admin demo chưa có. |

## P1 đọc sâu: phần đã có và giả thuyết cần đo

**SOURCE VERIFIED:** `ProductController.Normalize` giới hạn 200; repository tenant-scope, AsNoTracking, CountAsync, Id descending, Skip/Take, rồi PopulateSaleUnitsAsync. `BuildProductIndexAsync` gọi GetSummaryAsync mỗi lần Index/Search; category options chỉ nạp cho initial Index, giới hạn 500. Signature Index/Search đã đọc không có tham số sort; không tự thêm chức năng sort trong pilot.

**SOURCE VERIFIED:** `_ProductTable.cshtml:69–83,227–228` render hai biểu diễn desktop/mobile cho cùng page. Ở pageSize 200, source tạo tối đa 200 vòng row và 200 vòng card, chưa tính node con. Đây là **400 biểu diễn bản ghi theo source**, không phải số DOM đo trên browser. Ảnh có lazy loading; giá/đơn vị và label trạng thái đang dùng dữ liệu server.

**SOURCE VERIFIED:** `products.index.js:367–431` dùng AbortController + sequence, thay innerHTML sau response hợp lệ, đồng bộ history/KPI, debounce 300 ms. Có `aria-live`, `aria-busy`, empty state và focus-visible trong source CSS. `products.mobile.css` đã có input 16px/vùng chạm 44px. Không ghi chúng là thiếu chức năng hoặc đã đạt accessibility thực tế.

| Ưu tiên | Finding | Loại bằng chứng / ảnh hưởng | Xử lý đề xuất |
|---|---|---|---|
| Cao | Chưa có runtime an toàn/baseline trên dataset rõ nguồn | SOURCE VERIFIED + NOT TESTED; không thể chọn thay đổi dựa trên lag hay ảnh thật | Mở chặn A1 trước; giữ A3/A4 BLOCKED. |
| Cao | P1 có Index đã modified và CSS mobile untracked từ trước | SOURCE VERIFIED Git; nguy cơ ghi đè công việc có sẵn | Khóa baseline hash/diff/phạm vi với chủ dự án trước A7. |
| Vừa | Hai biểu diễn desktop/mobile cùng render mỗi page | SOURCE VERIFIED; **ASSUMPTION** về chi phí DOM/resize ở 100–200 dòng | Đo node count/HTML bytes/long task trước; P1 chỉ sửa trình bày, chưa tháo DOM/virtualize. |
| Vừa | Search có count + summary aggregate + dữ liệu đơn vị | SOURCE VERIFIED call chain; độ chậm SQL **NOT TESTED** | Đo TTFB và UI riêng; không đổi query/index trong pilot UI. |
| Vừa | Source có palette legacy, gds và navy/lam theo scope | SOURCE VERIFIED các CSS/token; tính nhất quán cảm nhận **NOT TESTED** | Dùng hướng B giới hạn P1, tôn trọng mobile v2; không đổi shared theme. |

Không phát hiện lỗi overflow, số click thừa, mất focus hoặc tỷ lệ tương phản thực trên browser trong lượt này. Chúng là checklist chờ kiểm tra, không phải defect đã tái hiện.

## Ma trận ảnh và role phải bổ sung

| Màn ưu tiên | Role dự kiến (chưa có tài khoản test) | Dataset | 390×844 | 768×1024 | 1440×900 | 1920×1080 |
|---|---|---|---|---|---|---|
| Product Index P1 | catalog-reader; đối chiếu supplier-view/no-supplier-view | UNKNOWN | NOT TESTED | NOT TESTED | NOT TESTED | Không bắt buộc |
| POS scan/cart P2 | cashier demo đúng Store/terminal/ca | UNKNOWN | NOT TESTED | NOT TESTED | NOT TESTED | NOT TESTED nếu quầy dùng viewport này |
| Các nhóm còn lại | role module tương ứng, ít nhất một read-only role | UNKNOWN | NOT TESTED | NOT TESTED | NOT TESTED | Tùy module |

Không tạo ảnh placeholder, không dùng ảnh lịch sử làm BEFORE của source hiện tại, không tạo `evidence/before` rỗng để giả có ảnh. Khi mở chặn, chụp ít nhất P1 và P2 ở mobile/desktop, bổ sung tablet; chỉ giữ ảnh dữ liệu tổng hợp, ghi role label, source hash, dữ liệu/count, URL/viewport/cache và ngày chụp.

Kịch bản kiểm tra browser: overflow toàn trang và scroller riêng; Tab/Enter/Escape và trả focus; search cancel/out-of-order; filter chips/reset/back/reload/paging; tên tiếng Việt dài/giá/đơn vị; loading/empty/error/401/403; resize/focus/scroll với 20/100/200 dòng. POS thêm scan/quantity prefix nhưng chỉ khi sandbox và phạm vi mutation test được duyệt. Không hoàn tất giao dịch.
