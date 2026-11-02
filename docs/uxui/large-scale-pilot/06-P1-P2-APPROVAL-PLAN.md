# A6 — Kế hoạch P1/P2 và điểm dừng

Ngày 02/11/2026. **SOURCE VERIFIED** cho route/file; **ASSUMPTION/PROPOSAL** cho thiết kế/test tương lai; **NOT TESTED** cho role test, BEFORE và benchmark. Trạng thái **BLOCKED điều kiện baseline; STOP — CHỜ DUYỆT PILOT P1**. Chưa đủ bằng chứng để ghi PASS A6. Không thực hiện A7/A8.

## Lựa chọn P1 — Danh sách sản phẩm

Route thật: **`/Admin/Product`**, action Index; partial GET **`/Admin/Product/Search`**. `ProductController.cs:190–257` → `Product/Index.cshtml` → `_ProductTable.cshtml` → ProductService/ProductRepository paging. Quyền đọc `PermissionCodes.Catalog.Product.View`; bộ lọc supplier còn cần Catalog.Supplier.View. Role dự kiến `catalog-reader`, chưa có role/account test được xác minh.

Lý do chọn từ source: bảng sáu nhóm cột, nhiều điều kiện/lifecycle/lookup, page20–200; đã có loading/cancel/history/mobile và được đọc sâu trong A3. Ít nguy cơ giao dịch hơn các màn POS/payment/invoice. Route vẫn có nút tạo/sửa/xóa/toggle; test P1 chỉ mở list/filter/page/quick-view đã kiểm tra an toàn, không nhấn lệnh ghi. Không coi cả trang là bất biến chỉ vì mục tiêu read-mostly.

BEFORE mobile390×844, tablet768×1024, desktop1440×900: **CHƯA CÓ**. Dataset/tier thực chạy: **không có**, row count UNKNOWN, 0 mẫu benchmark. Không thay bằng ảnh lịch sử hoặc preview foundation. Đây là blocker phải hoàn thành trước sửa.

Phương án đề xuất: **B — Balanced giới hạn theo A5**, chỉ cải thiện phân cấp chữ, khoảng cách, nhóm cột/giá/trạng thái, bộ lọc và cách trình bày loading/empty hiện có. Không thêm sort hay pagination mới; không thay owner JS/SQL. Phương án A là fallback nhỏ hơn; C chưa khuyến nghị. Hiệu quả UX/performance chưa đo, không hứa tỷ lệ tăng tốc.

## Bốn file dự kiến xin duyệt cho P1 — chưa sửa

| File chính xác | Thay đổi dự kiến | Baseline hiện có / rủi ro |
|---|---|---|
| `GaoApp.Web/Areas/Admin/Views/Product/Index.cshtml` | Nhóm header/filter rõ hơn bằng class/ngữ nghĩa trình bày; giữ input, URL, permission và ID/data-* | **Modified trước pilot**; phải snapshot/diff được user chấp thuận. |
| `GaoApp.Web/Areas/Admin/Views/Product/_ProductTable.cshtml` | Typography/hierarchy cột và empty/paging presentation; giữ foreach, dữ liệu giá/đơn vị, actions và authorization | Tracked, chưa thấy diff đầu pilot ở file này; vẫn khóa hash trước A7. |
| `GaoApp.Web/wwwroot/Admin/css/products.index.css` | CSS scope Product/quick-view cho desktop/density/focus/truncation | Tracked, chưa thấy diff đầu pilot ở file này; kiểm tra modal ngoài page root. |
| `GaoApp.Web/wwwroot/Admin/css/products.mobile.css` | Hoàn thiện trình bày mobile theo palette v2, không tháo mobile adapter/hook | **Untracked đã có trước pilot**, không phải file do pilot tạo; tuyệt đối không ghi đè. File còn có scope setup, nên patch chỉ scope list. |

Không đưa `products.index.js`, `products.filters.js`, `products.mobile.js`, shared layout, sidebar, gds/Commerce token toàn cục hoặc backend/config vào P1. Nếu cần JS để làm một cải tiến feedback/state mới, dừng xin bổ sung scope thay vì tự sửa. Bốn file trên cũng **chưa được duyệt** chỉ vì đã xuất hiện trong kế hoạch.

Không tạo menu. Nếu cần đường vào, user tự tạo qua `/Admin/AdminMenus` với route `/Admin/Product` và quyền gốc, theo AGENTS.md. Pilot không thao tác dữ liệu menu.

## Hồi quy và tiêu chí nghiệm thu P1 sau duyệt

- Chốt source/Git baseline riêng trong phạm vi bốn file. Theo A7 của contract, chủ dự án chuẩn bị backup/điểm xuất phát và thống nhất xử lý dirty files; không tự stash/reset/commit hay tạo branch. Nhánh `uxui/gaoapp-large-scale-p1` chỉ được tạo khi được cho phép, không force tên đã có.
- Mở app thật trên DB test và role demo đã xác minh; hoàn thành BEFORE và ba scenario A4 **trước sửa**. Nếu còn blocker, tiếp tục dừng implementation dù kế hoạch P1 được duyệt có điều kiện.
- Giữ `id/name/asp-*/data-*`, antiforgery, URL/query, paging/history, quyền supplier/label/status và giá/đơn vị server. Không clone input, tự tính lại giá/discount hoặc thay giá theo lốc/thùng.
- Tìm tên tiếng Việt/SKU/barcode, zero/many results, filter/reset, page20/100/200, back/reload/deep link; response muộn/cancel/error/401/403 phải giữ hành vi. Quick-view/keyboard/focus/image preview không bị che. Chỉ read actions đã chứng minh an toàn.
- Chụp AFTER cùng viewport/role/count/state và đo cùng protocol. Kiểm tra tablet768px và một trang setup sản phẩm lân cận do mobile CSS chứa scope setup. Kiểm tra POS/kho/khách hàng không bị ảnh hưởng bởi selector rò; không thay shared CSS để sửa lỗi lan rộng.
- Build Web source thật với output riêng; test tập trung phù hợp diff, có thể tái dùng ProductIndexModernUiContractTests/ProductFiltersSqlServerTests/ProductCatalogMobileBrowserAcceptanceTests **chỉ sau review fixture và duyệt mọi DB mutation của chúng**. Các tên test hiện có là nguồn kế hoạch, chưa được chạy ở pilot. Không chạy full test hoặc fixture tự seed menu.
- `git diff --check` trong phạm vi pilot; review đúng diff so baseline và hash inputs. 33 Node test hiện đã pass không thay thế hồi quy Product runtime sau sửa. Chỉ ghi PASS A8 khi build/UI/benchmark/regression thực tế đáp ứng contract.

Rủi ro chính: dirty source, hai biểu diễn row/card làm DOM lớn ở page200, CSS mobile dùng chung cả list/setup, label giá/đơn vị có thể bị cắt khó hiểu, focus khi partial replace. Độ ảnh hưởng/độ trễ runtime chưa đo. Không xử lý bottleneck SQL hay thay đổi repository trong P1.

Rollback P1 tương lai: lưu patch và hash cho đúng bốn file từ baseline đã chốt; hoàn tác **chỉ hunk do P1 tạo**, kiểm tra source hash và UI lại. Với file mobile đang untracked từ trước, không xóa file để rollback. Không `git reset --hard`, `git clean -fd`, restore cả file có thay đổi của người khác hoặc rollback database. Không có migration/data patch nên không cần DB downgrade.

## P2 — Vùng tìm/scan và giỏ POS, chưa xin triển khai

Route thật **`/admin/pos`** hoặc `/admin/pos/v3`, `POSController.Index/Prime` → `POS/Index.cshtml`; vùng `_Toolbar.cshtml` (`txtBarcode`, `barcodeAutocomplete`) và `_CartTable.cshtml` (`currentDraftBody`, `paymentListBox`). Role dự kiến cashier demo đúng Store/terminal/ca, quyền Pos.Order.View và quyền thao tác native cần thiết; chưa xác minh role demo.

File dự kiến cho scope P2 riêng: `GaoApp.Web/Areas/Admin/Views/POS/_Toolbar.cshtml`, `GaoApp.Web/Areas/Admin/Views/POS/_CartTable.cshtml`, `GaoApp.Web/wwwroot/Admin/css/pos/pos.entry.mobile.css`, `GaoApp.Web/wwwroot/Admin/css/pos/pos.cart.mobile.css`. Toolbar hiện đã modified từ trước; phải khóa lại baseline. Chỉ cùng một vùng scan/cart, không payment/finalize/modal giao dịch hoặc shared POS layout. CSS tablet/desktop ngoài phạm vi này cần duyệt bổ sung nếu có nhu cầu.

Không sửa `pos.barcode.js`, `pos.keyboard.js`, `pos.render.js`, payment/price/promotion/service. Phải giữ mọi event/binding/scan prefix/keyboard/line identity và focus. Chưa có ảnh BEFORE390/1440/1920, chưa benchmark giỏ dài; P2 vẫn BLOCKED. Chỉ review/duyệt P2 sau P1 được nghiệm thu. Việc thử thêm/sửa dòng POS là mutation riêng cần sandbox và quyền cụ thể; không hoàn tất đơn, thu tiền, in hoặc phát hành hóa đơn.

## Phạm vi quyền cần phân biệt

| Quyết định | Tình trạng hiện tại |
|---|---|
| Chỉ định/dùng DB test riêng đã có để đọc | Chưa xác minh tên/profile/provenance/quyền. Cần chủ dự án chỉ định trước runtime. |
| Tạo/copy DB, fixture, seed T0/T1/T2 hoặc mutation test | Chưa được phép; không kèm ngầm theo duyệt P1. Không dùng dữ liệu thật. |
| Bốn file UI P1 ở trên | Đang đề xuất; chưa sửa. |
| Concurrency/load test | Chưa được phép; giữ **một phiên** thao tác bình thường. |
| Cài skill/MCP, sửa preview, đổi config | Task/phê duyệt riêng; không kèm ngầm theo P1. |
| P2 hoặc rollout toàn hệ thống | Chưa được phép; P2 chỉ sau review P1. |

## Artifact/rollback hiện tại và yêu cầu duyệt kế tiếp

Hiện chỉ có bảy report00–06, evidence build/test/inventory/hash; không có UI/backend/DB changes hoặc AFTER. `00` liệt kê Gate và source; `01` giữ cả build thất bại và thành công; `02` là kế hoạch chưa seed; `03/04` giữ rõ phần chưa kiểm tra; `05` không báo skill PASS giả.

Nếu muốn hủy chính lượt discovery này, chỉ bỏ đúng các report/evidence mới sau khi xác nhận nhu cầu; giữ mọi thay đổi source và artifact lịch sử có sẵn. Không thực hiện cleanup trong lượt này. Generated build output riêng không phải bản triển khai; không trỏ IIS/Service vào đó.

**Yêu cầu tiếp theo duy nhất:** Anh có duyệt **kế hoạch P1-B với đúng bốn file nêu trên**, đồng thời chỉ định **DB test riêng đã có và profile an toàn được phép dùng để bổ sung BEFORE/benchmark**, giữ **một phiên và chưa seed**, với điều kiện chỉ bắt đầu sửa UI sau khi các blocker A1/A3/A4 và baseline dirty files được giải quyết không?

Có thể trả lời `DUYỆT KẾ HOẠCH GAOAPP P1-B` kèm tên/đường dẫn profile DB test đã chuẩn bị (không gửi secret trong báo cáo). Điều này không duyệt tạo DB/seed, load đồng thời, cài skill, sửa preview hoặc P2. Nếu chưa có DB test, chỉ duyệt kế hoạch; bước tạo môi trường phải được trình riêng.

Lý do phải dừng: chính hợp đồng `CODEX_GAOAPP_UXUI_LARGE_SCALE_PILOT_V2.md:208–221` quy định **“KHÔNG CÓ QUYỀN SỬA UI TẠI A6”** và **“STOP — CHỜ DUYỆT PILOT P1”**. Đây là giới hạn user đã đặt, không phải yêu cầu duyệt phát sinh từ skill hoặc auto-review. **STOP — CHỜ DUYỆT PILOT P1.**
