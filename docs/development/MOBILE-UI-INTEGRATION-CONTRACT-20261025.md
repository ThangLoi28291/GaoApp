# Hợp đồng tích hợp giao diện v2 · UI-01c/e

Ngày 25/10/2026. Người dùng đã duyệt [mẫu điện thoại v2](MOBILE-UI-MOBILE-V2-20261025.md) làm chuẩn và yêu cầu chuyển view thật từng bước. Tài liệu này chốt cách thích ứng, nạp tài nguyên và các điểm cần bảo toàn trước các bước UI-02 đến UI-06. Không coi các màn nghiệp vụ trong bảng đã chuyển chỉ vì đã kiểm kê selector.

## Chuẩn hiển thị

Ràng buộc hiện hành: chỉ đổi giao diện điện thoại, giữ giao diện máy tính hiện tại; nguồn menu là `AdminMenuItems`, theo cửa hàng/quyền/cấu hình. Từ UI-04, người dùng yêu cầu tự chạy test và đánh giá sau khi hoàn thành, giữ nguyên Chrome đang mở; dùng fixture SQL/Web và Chrome riêng. Yêu cầu không tự chạy tại UI-02c/UI-03 là lịch sử trong các báo cáo tương ứng, không thay thế yêu cầu test mới. Các trường hợp nghiệm thu thiết bị/Visual Studio cũ vẫn còn riêng.

- Một nguồn dữ liệu và một DOM cho mỗi control/chứng từ. CSS đổi cách trình bày; không dựng giỏ, số tiền, form hoặc handler nghiệp vụ thứ hai cho điện thoại.
- Điện thoại dưới 768 px: một cột; màu navy/lam v2; input 16 px; nút từ 44 px; thông tin chính trước, công cụ phụ theo ngữ cảnh. Menu đầy màn hình. Tablet 768–1199 px giữ drawer điều hướng; desktop từ 1200 px giữ sidebar hiện hành.
- Breakpoint theo chiều rộng xác định bố cục. Khả năng chạm chỉ điều chỉnh vùng tương tác; không dùng nhận diện thiết bị để chọn một luồng nghiệp vụ khác. Thử thêm bàn phím ở mọi kích thước.
- Menu có phần cuộn riêng. Trên Admin, khi menu overlay mở thì vùng nội dung phía sau inert và cuộn document được khóa; đóng trả focus về nút mở. Trong POS, Bootstrap offcanvas tiếp tục sở hữu focus/scroll lock; thành phần menu không tạo thêm modal.
- Bảng trượt của màn mới dùng native dialog hoặc cơ chế Bootstrap hiện hữu của module sau khi đối chiếu luồng. Không đồng thời mở hai cơ chế cho cùng một tương tác. Không đổi hàng loạt modal hiện hữu chỉ để giống demo.
- Dock giao dịch thuộc POS/nhận hàng; điều hướng chung không chiếm vị trí dock. Thanh tối đa bốn mục + Menu dùng ghim riêng nếu khả dụng; theo quyết định 28/10/2026, nếu chưa có ghim riêng thì ưu tiên bốn phần thật Tổng quan/Đơn hàng/Nhập hàng/Bán hàng theo menu và quyền. Không tạo menu hoặc ghi cấu hình mặc định. Phối hợp quyền sở hữu đáy màn ở UI-03.
- Giảm chuyển động theo `prefers-reduced-motion`; giữ focus visible, nhãn trạng thái bằng chữ và tên đầy đủ cho mục dài. Kiểm tra thiết bị vật lý, zoom và forced colors còn thuộc UI-01d/UI-24.

## Nạp CSS/JS

| Chủ sở hữu | Tài nguyên và quy tắc |
|---|---|
| `_CommonMasterLayout` | Nạp một lần `gaoapp.commerce-tokens.css`, `gaoapp.admin-navigation.css`; sau `main.js` nạp `gaoapp.admin-navigation.js`. CSS chỉ áp dụng root opt-in của menu; script thoát ngay nếu không có root |
| `_CommerceMobileLayout` | Nạp cùng token đã duyệt trước `ui-mobile.v2.css`; giữ script fixture trong demo, không chuyển logic fixture vào nghiệp vụ |
| Token dùng chung | `gaoapp.commerce-tokens.css`, phạm vi `[data-gao-theme="commerce"]`; không đổi `:root`/Bootstrap toàn cục. CSS v2 dùng alias từ token này |
| Màn nghiệp vụ | Nạp CSS/JS trong các section đã có, cache busting bằng `asp-append-version`; không sao chép cả bundle chung hoặc gắn handler hai lần |
| Module giao dịch | Giữ trình tự loader, namespace, submit guard, command ID, retry/journal và sự kiện lưu hiện hữu; chỉ đổi trình bày trong bước sở hữu |

Không nạp `ui-mobile.v2.js` vào trang thật: file này chỉ có dữ liệu và thao tác mô phỏng, không phải controller của POS/nhận hàng.

## Điểm tích hợp của các workspace thí điểm

[Snapshot đầy đủ](mobile-ui-20261025/evidence/ui-02ab/pilot-contracts.json) ghi SHA256, ID, thuộc tính data, script, stylesheet và section của 11 Razor: 389 ID tĩnh và 46 thẻ script trong source. Đây là số xuất hiện trong template, không khẳng định số control/script đồng thời ở runtime.

| Bước | Điểm phải giữ | Chủ sở hữu tương tác |
|---|---|---|
| UI-02 · Menu | `#layout-menu`, `.layout-menu-toggle`, `.menu-inner`, `.menu-item`, `.menu-link`, `.menu-sub`, `.open` và `.active`; `posPrimeAdminDrawer` | Helpers đóng/mở sidebar; Bootstrap đóng/mở POS drawer; script opt-in mới sở hữu tìm và mở nhóm |
| UI-04 · Danh sách đơn | `ordersFilters`, `keyword`, `fromDate`, `toDate`, `status`, `orderFilterDetails`, `product`, `employeeId`, `terminalId`, `ordersBody`, nút tìm/lọc/phân trang và xem nhanh | Script danh sách, lookup, in phiếu và ACB hiện tại; giữ định nghĩa số tổng cả bộ lọc so với số tiền trên trang |
| UI-04 · Chi tiết đơn | Các `data-csp-action` sau bán/thu tiền/in, `orderActionModal` và các điểm mở lịch sử; kiểm tra quyền/trạng thái từ model/controller | Handler sau bán và service hiện tại; không tạo đường thu tiền từ script mẫu |
| UI-05 · Danh sách nhận hàng | `data-warehouse-receiving-index`, `data-status-filter`, `wrSearch`, `wrReceiptList`, `wrLoadStatus`, phân trang và `btnOpenCreateReceiving` | Loader nhận hàng hiện tại; tìm/lọc không đổi phạm vi cửa hàng |
| UI-05 · Chi tiết nhận hàng | `wrdLinesContainer`, `btnSubmitReceiving`, xác minh barcode; `data-wra-tab`, `wraStatus`, `wraRetry`, `wraDockActions`, `wraQuantityModal`, `wraQuantity`, `wraSaveQuantity` | Module nhận hàng, command/revision, hàng chờ xác nhận và retry; số lượng luôn theo từng quy cách |
| UI-06 · POS | `posShell` cùng store/terminal/user, `posPrimeContextLauncher`, các host giỏ/khách/tổng, `posPrimeCheckoutDock`, `data-prime-open`, context/money sheet và nút mở admin drawer | `pos.state`, `pos.dom`, loader/render, offline/journal, checkout, QR và in hiện hữu; giữ chống nhấn lặp và trạng thái chưa xác nhận |

Khi sửa một module, đối chiếu lại hash snapshot vì source có thể thay đổi giữa các lượt. Nếu phải đổi selector hoặc loader, đổi cả nơi gọi và ghi bằng chứng ở đúng bước; không xóa selector chỉ vì phiên bản mobile chưa hiển thị phần đó.

### Bổ sung UI-02e ngày 25/10

Sidebar và các lối tắt dùng `AdminNavigationProvider` scoped theo request, cùng đầu vào `GetForRenderAsync`; không tạo danh sách chức năng thay thế. `_CommonMasterLayout` nạp thêm `gaoapp.configured-shortcuts.js` sau script menu/ghim, một lần. `_POSNavTabs`, hai menu “thêm” POS, lối tắt trang chủ/dashboard/tài khoản và nhận hàng chỉ chiếu các node đủ điều kiện.

Đổi selector popup `btnOpenCustomerDisplay` thành `data-configured-customer-display`, URL lấy từ node cấu hình. Binder cũ trong `pos.cockpit.js` đã bỏ; handler chung là chủ sở hữu duy nhất, hai contract test nguồn đã cập nhật. Giữ popup name/features, mẫu hóa đơn mở tab mới và các selector nghiệp vụ POS/nhận hàng còn lại. Nhận hàng dùng lại `.layout-menu-toggle`, đóng sheet xong mới mở menu; Bootstrap vẫn sở hữu drawer POS. Chi tiết và giới hạn kiểm tra ở [báo cáo UI-02e](MOBILE-UI-02E-CONFIGURED-SHORTCUTS-20261025.md). Chưa build/chạy app/bộ test; chờ người dùng tự nghiệm thu Visual Studio.

### Bổ sung UI-02f ngày 25/10

`_CommonMasterLayout` nạp một lần `gaoapp.navigation-edge-cases.mobile.css` sau CSS menu/ghim. Toàn bộ rule mới chỉ áp dụng dưới 768 px; dropdown POS dùng thêm `data-configured-pos-menu` để xuống dòng/cuộn trong màn hình. Cây sidebar giữ cấu trúc nhưng chặn thụt lề cộng dồn ở tầng sâu trên điện thoại. Không đổi CSS desktop, token, controller/service hoặc nguồn quyền.

`gaoapp.mobile-pins.js` kiểm tra request hiện hành trước/sau JSON, hủy GET khi nhận kết quả lưu, giữ focus điều khiển và cho thử lại sau lỗi qua mở menu/online; vẫn chỉ đối chiếu ID vào cây thật đã render. Các bài kiểm tra và ma trận runtime của UI-02f được chuẩn bị nhưng chưa chạy; xem [báo cáo UI-02f](MOBILE-UI-02F-MENU-EDGE-CASES-20261025.md).

### Bổ sung UI-03a ngày 25/10

`_CommonMasterLayout` nạp một lần `gaoapp.mobile-shell.css` sau `PageStyles`, và `gaoapp.mobile-shell.js` sau các script menu hiện có. CSS ngoài media chỉ ẩn control mới; mọi thay đổi hiển thị khác chỉ dưới 768 px. Script chạy sau `employee-account.js`, chuyển nguyên node account/tiện ích vào slot mobile và trả theo marker desktop, không clone form hoặc tạo handler tài khoản thứ hai.

`_ContentNavbarLayout` dùng `_MobileAppHeader` cho trang quản trị thường, giữ nội dung/section module. POS/Index và WarehouseReceiving/Detail khai báo `MobileShellOwner`; layout chuẩn hóa `admin`/`pos-legacy`/`pos-prime`/`receiving`, cùng `MobileDockOwner` là `navigation`/`pos`/`receiving`/`none`. Đây là metadata trình bày. POS/nhập hàng giữ header, dock, handler nghiệp vụ; chỉ thêm slot account và Menu điện thoại cho legacy. Không có menu/quyền/dữ liệu mới được tạo.

Đối chiếu source ở [báo cáo UI-03a](MOBILE-UI-03A-APP-SHELL-20261025.md); chưa build/chạy app/bộ test. Safe area/visual viewport đầy đủ và phối hợp trợ lý/toast tiếp tục ở UI-03b; không coi việc thêm metadata dock là đã nghiệm thu các trường hợp bàn phím hay lớp phủ.

### Bổ sung UI-03b ngày 25/10

`_CommonMasterLayout` nạp `gaoapp.mobile-viewport.css/js` ngay sau bộ mobile shell, một lần và có cache busting. Meta viewport thêm `viewport-fit=cover`, giữ khả năng zoom. Mọi rule CSS mới nằm dưới 768 px và opt-in bằng trạng thái viewport trên body; script thoát khi không có shell, khôi phục các metric/inert do nó đặt khi ra khỏi breakpoint. Các biến `--gao-mobile-*` chỉ thuộc lớp trình bày.

Coordinator đo header/dock hiện có và vùng nhìn thấy; hỗ trợ safe area, thanh ghim/dock nhận hàng nhường bàn phím, lớp phủ và toast. Menu thường giữ controller focus/scroll hiện có, bổ sung lưu/trả cuộn body chỉ trên điện thoại. Bootstrap/native dialog tiếp tục sở hữu focus và khóa cuộn; POS Prime giữ geometry/thanh toán và cuộn sheet của chính nó. Trợ lý nhường tương tác cho lớp phủ ngoài nhưng native dialog của trợ lý vẫn dùng được; không đổi gọi mạng, xác nhận hay quyền. Không thêm form, focus trap, submit hoặc nguồn menu.

Chế độ app nhập hàng đổi một matchMedia và một media query từ 768 px xuống 767.98 px cho khớp shell; các quy tắc responsive cũ khác giữ nguyên. Đây là thay đổi ranh giới trình bày tại đúng 768 px, không thay form/tab/scanner/lưu/retry. Phát hiện bàn phím là ước lượng và cần thử thiết bị thật; pinch zoom không kích hoạt tự cuộn field. Xem [báo cáo UI-03b](MOBILE-UI-03B-VIEWPORT-FOCUS-20261025.md) và 16 ca đang chờ nghiệm thu; chưa build/chạy app/browser/bộ test/migration.

### Bổ sung UI-03c ngày 25/10

`_CommonMasterLayout` nạp thêm một lần `gaoapp.mobile-account-states.css` sau viewport. Ngoài media chỉ ẩn nhãn/back/help mới; mọi rule còn lại thuộc dưới 768 px. Account giữ cùng DOM/form/ID và handler panel; chỉ thêm nhãn nhóm và nút về hồ sơ. Popup điện thoại dùng `visualViewport` để giới hạn chiều cao; desktop tiếp tục dùng thuật toán đặt popup cũ. Submit mật khẩu, CSRF, dữ liệu phiên, redirect, pagehide/pageshow và các controller/service không đổi.

Toast và SweetAlert chỉ đổi trình bày; không đổi nội dung, escaping, timer, kết quả xác nhận, challenge hoặc quyền tại điểm gọi. Viewport coordinator nhận diện thêm popup tài khoản và SweetAlert modal để trợ lý nhường tương tác; SweetAlert toast không trở thành lớp phủ modal. Không thêm focus trap, form hoặc hộp thông báo lưu trữ.

AccessDenied và hai Error/Error của controller lỗi dùng metadata `data-mobile-state-*`; hai layout cũ chỉ nạp CSS khi `MobileStatePage` là true. Selector body kiểm tra giá trị `="true"` để không bật khi Razor render data attribute rỗng. Giữ toàn bộ message/TraceId/returnUrl, điều kiện và URL cũ; CSP back vẫn do handler cũ sở hữu. Hai Shared/Error chưa được chuyển, tiếp tục rà ở UI-03f. [Báo cáo UI-03c](MOBILE-UI-03C-ACCOUNT-FEEDBACK-20261025.md) có phạm vi, đối chiếu source và 16 ca chờ người dùng chạy Visual Studio; chưa build/chạy app/browser/bộ test/migration.

### Bổ sung UI-03d ngày 25/10

`Home/Index` thêm partial `_MobileOverview` và hai asset qua PageStyles/PageScripts của layout hiện có. CSS ngoài media chỉ ẩn root mới; điện thoại dưới 768 px ẩn phần Home cũ bằng marker, desktop giữ nguyên nội dung trước lượt này. Không đổi controller hoặc layout chung. Tên chào đọc từ DOM profile hiện có, không thêm lần gọi service tài khoản.

Khu vực báo cáo chỉ render khi `Report.Sales.View` đạt; Home controller vẫn yêu cầu DashboardView. Adapter chỉ GET URL `SalesExecutiveReport.GetData` sinh bởi Url.Action, `compare=none`, server chọn ngày UTC+7/cửa hàng/quầy theo read service cũ. Không tạo endpoint hoặc truyền store/user. Không fetch trên desktop; không polling/storage, hủy/xóa số khi ẩn trang/rời breakpoint và bỏ response cũ trước/sau JSON. Dữ liệu lỗi/thiếu không thành 0; 401/403 xóa số và dùng CSP reload hiện có. NetSales/AOV/return/void giữ định nghĩa từ response.

`gaoapp.mobile-pins.js` chỉ phát sự kiện đọc `gao:mobile-pins-rendered` sau mỗi render. Home lấy anchor đã qua bộ lọc từ thanh ghim, kể cả danh sách rỗng; không tạo request ghi, logic quyền hoặc danh sách chức năng thay thế. SSR giữ GetPinsAsync hiện có. Link báo cáo chỉ dùng Href/ID của node cấu hình đủ quyền; thiếu node thì không có link. [Báo cáo UI-03d](MOBILE-UI-03D-HOME-OVERVIEW-20261025.md) ghi metric mapping, các read model mở rộng tách riêng, đối chiếu và 16 ca chờ người dùng chạy Visual Studio. Chưa build/chạy app/browser/bộ test/migration.

### Bổ sung UI-03e ngày 25/10

Login standalone nạp `account-login.mobile.css` sau CSS Halloween và `account-login.mobile.js` sau script cũ. Ngoài media chỉ ẩn branding/nhãn mới; dưới 768 px một cột v2, context cửa hàng/quầy, native password reveal và xử lý viewport/lỗi/busy. Vẫn cùng form/binding/token/action; controller, VM, tenant/terminal, rate limit, offline privacy và local redirect không đổi. Legacy JS giữ submit/geometry/cảnh desktop, chỉ thêm guard phone và ngừng/khôi phục canvas/atlas theo breakpoint. Adapter không đọc/copy/lưu/gửi mật khẩu.

Theo yêu cầu bổ sung, chỉ hai profile Windows http/https tắt IDE launchBrowser và opt-in helper `DevelopmentChromeLauncher`. Một lệnh đăng ký trước app.Run; helper chỉ Windows/Development/flag, sau ApplicationStarted chọn loopback HTTP(S), mở Chrome `--new-window`; không remote debugging/profile riêng, không liệt kê/kill process hay hook shutdown. Không sửa các URL/env khác, IIS Express/WSL hoặc thiết lập cá nhân. Chưa chạy helper/app/Chrome; người dùng kiểm tra F5/Stop theo [báo cáo UI-03e](MOBILE-UI-03E-LOGIN-CHROME-20261025.md). C#/Razor chưa build, browser test chỉ chuẩn bị lại cho phone mới; 18 ca runtime đều chờ nghiệm thu. Không có migration/menu/quyền mới.

### Bổ sung UI-03f ngày 25/10

Đã rà hai ViewStart, tham chiếu literal, Layout và section. `_AdminLayout`, `_BlankLayout` và navbar mẫu chưa có caller tĩnh bên ngoài cụm legacy; giữ source, không xóa hoặc kích hoạt lại. `_AdminLayout` có menu cố định không phù hợp hợp đồng hiện tại, nên caller phục hồi sau này phải chuyển sang `_ContentNavbarLayout`/menu cấu hình trước khi xét dọn mã. `_AdminToolbar`, hai `_Layout` dùng cho public/lỗi, các layout POS/demo và layout null của trang in/kiosk giữ quyền sở hữu hiện tại. Blank chỉ bổ sung chuyển tiếp tùy chọn Scripts, không thêm menu/shell.

Hai Shared/Error giữ ErrorViewModel/ShowRequestId/escaping và toàn bộ nội dung desktop trong wrapper không có CSS desktop. Partial `_MobileSharedError` ẩn từ 768 px; dưới 768 px dùng thẻ lỗi v2, mã hỗ trợ và Back/Home qua handler CSP/route area hiện tại. Public bật MobileStatePage để dùng opt-in UI-03c; Admin tiếp tục kế thừa ContentNavbarLayout, không thêm chiều cao toàn màn phía dưới header/dock. CSS chỉ thêm rule Shared/Error, không đổi các rule cũ. Không đổi Error/Error, controller lỗi/Home, GlobalExceptionMiddleware trả JSON, HTTP status hoặc route/menu/quyền. Csp-actions đã được Head TagHelper nạp bằng nonce; không thêm script/inline handler.

[Báo cáo UI-03f](MOBILE-UI-03F-LEGACY-LAYOUT-AUDIT-20261025.md) ghi kết luận giữ/thay trước dọn mã, giới hạn quét source và 16 ca chờ người dùng chạy Visual Studio. Đã đối chiếu 35 hash bảo vệ và bốn file cũ; chưa build/chạy app/browser/bộ test/CSP suite/migration. Chrome launcher/profile UI-03e không sửa, không tác động cửa sổ/tab đang mở. Bước triển khai kế tiếp UI-04a, các ca nghiệm thu trước vẫn giữ trạng thái chờ.

### Bổ sung UI-04a ngày 25/10

`POSOrderPage/Index` chỉ thêm root marker và hai asset `pos-orders-page.mobile.css`/`pos.orders.mobile.js`. Toàn bộ CSS mới dưới 768 px; renderer/filter/CSS desktop/receipt/ACB cũ không đổi. Adapter giữ reference child node của sáu ô, sắp cùng nút/badge/text vào article T01 và phần phụ native details trên phone; từ 768 px trả đúng node, ô/thứ tự/colspan. Không clone dữ liệu/nút, đọc giá trị form, gọi mạng/storage, tính tiền, thay history hoặc thêm handler nghiệp vụ. Observer chỉ theo dõi childList của tbody trên phone, bỏ hàng đã tháo và tắt ở desktop/pagehide; pageshow đồng bộ breakpoint, không giành focus của dialog đang mở.

Nav POS đã được provider lọc quyền/menu được bọc trong disclosure phone rồi trả về vị trí desktop; không có nav thì không dựng wrapper. Các link header cố định cũ ẩn trên phone, không tự thêm mục thay thế. Một form filter và các ID/lookup/payload/applied filters/URL/phân trang giữ nguyên. Xem nhanh và popover chỉ đổi CSS phone, tiếp tục dùng native dialog/popover cùng handler gốc; summary của thẻ được guard double-click cũ nhận diện.

Giữ metricCount toàn bộ lọc so với ba số tiền trên trang, loại trừ thanh toán của đơn giao nguồn như updateMetrics cũ; các caption giá trị dự kiến/hàng thực giao, tiền đã trả/còn thiếu, UTC+7 và mapping status dùng cùng DOM. Controller, DTO, validator, repository, tenant/terminal và check quyền menu/in/sau bán không đổi. [Báo cáo UI-04a](MOBILE-UI-04A-POS-ORDER-LIST-20261025.md) có 30 hash bảo vệ, hai đối chiếu source và 20 ca chờ Visual Studio. Browser fixture chuẩn bị thêm kiểm tra 10 kích thước, disclosure, node identity và không refetch do resize; chỉ kiểm tra cú pháp JS, chưa build/chạy app/browser/test/CSP suite/migration. Bước tiếp theo UI-04b, các bước trước vẫn chờ nghiệm thu.

### Kiểm thử và đánh giá UI-04a theo yêu cầu bổ sung ngày 25/10

Đã build Release Web/Razor/runner 0 warning/error, chạy 10 test SQL/quyền ghim/CSP và một test lọc sản phẩm; Chrome probe qua 10 kích thước với 0 lỗi JS/CSP/lệnh tài chính. Test product cập nhật cách lấy root HTML và nạp asset phone; dữ liệu receipt fixture bổ sung grossAmount. Không đổi renderer/phép tính của ứng dụng.

Sau xem ảnh, phone dùng tiêu đề app bar (H1 nội dung vẫn dành cho trình đọc màn hình), thống kê được bọc trong native disclosure và trả về vị trí desktop khi đổi breakpoint. Filter mặc định giữ tìm/ngày nhanh; delivery-only/reset trong panel mở rộng. Thẻ đặt cùng node tiền cạnh khách/thời gian, không đọc hoặc tính lại giá trị; test xác nhận tiền đơn đầu nằm trên dock cả 320 px. Desktop tiếp tục nhận đúng các node/ô gốc, không refetch khi resize. 29 file nghiệp vụ/nền còn giữ hash.

[Báo cáo test UI-04a](MOBILE-UI-04A-RUNTIME-REVIEW-20261025.md) và runtime-review.json lưu bằng chứng cuối. Chỉ dùng Web/database GUID tạm và Chrome headless do fixture sở hữu; flag Chrome thường tắt trong test, không đổi dữ liệu cửa hàng/menu/profile hoặc cửa sổ Chrome đang dùng. Điện thoại vật lý và các tổ hợp menu/giao hàng/cọc, F5/Stop còn riêng; không đánh dấu các bước trước đã nghiệm thu.

### Bổ sung UI-04b ngày 25/10

Chi tiết đơn POS dưới 768 px dùng T02: tóm tắt từ node gốc, ba tab Hàng hóa / Thanh toán / Lịch sử, disclosure chứng từ/HKD/thao tác và hai liên kết Quay lại / In gốc trên menu cửa hàng. Adapter chỉ chuyển/khôi phục DOM, không clone, gọi API, lưu storage hoặc tính tiền. Modal thanh toán trên phone dùng thẻ; cả bốn modal/action/CSRF/handler và điều kiện Razor gốc giữ nguyên. Desktop từ 768 px, quyền controller/service, cấu hình AdminMenuItems và mọi asset/layout chung không đổi.

Người dùng cho phép chạy test trong lượt này. Build Release 0 lỗi/cảnh báo, 7 hồi quy Razor/CSP và 17 nhóm kiểm tra browser/10 viewport qua; ảnh desktop 1440 px khớp khi bỏ phần thêm mobile, node gốc khôi phục sau resize. Fixture kiểm tra quyền xem/thiếu quyền/cửa hàng khác/HKD, snapshot Orders/Lines/Payments/AdminMenuItems không đổi; 34 hash bảo vệ khớp. Chỉ Web/SQL GUID/Chrome headless riêng; giữ Chrome đang mở, không đổi database cửa hàng hoặc thêm migration/menu. [Báo cáo UI-04b](MOBILE-UI-04B-POS-ORDER-DETAIL-20261025.md) và ma trận nêu rõ phần thiết bị thật, dữ liệu đầy đủ và submit nghiệp vụ còn lại. Bước tiếp theo UI-04c; không tự nghiệm thu các bước trước.

### Bổ sung UI-04c ngày 25/10

Trên phone, các node sau bán/thanh toán được đưa vào đúng tab: trả hàng ở Hàng hóa; lịch sử thanh toán, chuyển khoản và yêu cầu sửa thanh toán ở Thanh toán; lịch sử trả và chờ nhập kho ở Lịch sử. Hủy/hoàn toàn bộ mở riêng. Controller detail chỉ bổ sung kết quả authorization của các đích hiện có; adapter giữ nút không được phép trong vùng hidden/inert và khôi phục toàn bộ node cũ khi về desktop. Chính sách quyền, submit, payload, API, tenant/service và nguồn menu không đổi; không thêm cơ chế thu tiền hoặc menu/migration.

Eligibility thiếu tên khách: renderer chỉ thêm marker ID, adapter phone lấy text khách từ receipt đúng đơn và trả text cũ trên desktop. Không đọc/sửa giá trị form, không gọi API/storage hoặc tính tiền. Đã kiểm tra giữ nguyên lý do đang nhập qua resize khi modal mở; lỗi tải giữ nút lưu khóa và đóng khi đang tải không mở lại modal.

Theo yêu cầu người dùng đã build 0 lỗi/cảnh báo, chạy 12 SQL/Razor/CSP và 26 nhóm browser ở 10 viewport, bảy tổ hợp quyền. Desktop 1440 px khớp ảnh; 40 file bảo vệ và snapshot SQL Orders/Lines/Payments/AdminMenuItems trước/sau browser khớp. Chỉ dùng Web/SQL GUID/Chrome headless riêng, giữ Chrome đang mở và database cửa hàng. [Báo cáo UI-04c](MOBILE-UI-04C-CONTEXTUAL-ACTIONS-20261025.md) ghi ảnh và giới hạn; UI-04d tiếp tục tổ hợp dữ liệu/thiết bị thật, không tự nghiệm thu các bước trước.

### Bổ sung UI-04d ngày 25/10

Đã bổ sung nghiệm thu SQL/Razor/browser cho chín tổ hợp: cọc/công nợ, khách lẻ, ba voucher, 50 dòng/tổng 3 tỷ, đơn trống, hai HKD/kho/hóa đơn, giao chờ/hủy và hai lần hoàn. Đối chiếu giá trị API/Razor/phone/desktop, quyền HKD/cửa hàng khác, chữ 200%/forced colors/reduced motion mô phỏng, Back giữ lọc và các lỗi tải.

Chỉ sửa phone: ngắt mã voucher dài; form hậu mãi lấy text số đã hoàn từ receipt đúng ID qua marker mới, không đổi phép tính/payload và khôi phục text cũ trên desktop; lịch sử thống kê hai cột để thấy bản ghi sớm. Các controller/service/quyền/layout/menu/Chrome launcher giữ nguyên, renderer và Razor chỉ thêm marker hiển thị.

Build 0 lỗi/cảnh báo, 25 hồi quy cùng 7 Razor/CSP trên build cuối, 16 nhóm mở rộng + 26 nhóm chi tiết và probe danh sách đều qua. Khoản hoàn 10 trên đơn thử đã thu 40 chỉ phát sinh một POST có CSRF/một phiếu/một khoản hoàn; lịch sử tải lại và số có thể hoàn 30. SQL giữ nguyên các bản lưu, dòng bán/khoản thu/voucher/allocation/hóa đơn/tồn/menu ngoài thay đổi hoàn chuyên biệt; 45 hash bảo vệ khớp. Chỉ SQL GUID/Web/Chrome headless riêng, giữ Chrome và database cửa hàng đang dùng, không thêm menu/migration. [Báo cáo UI-04d](MOBILE-UI-04D-ACCEPTANCE-20261025.md) ghi rõ nghiệm thu Android/iOS thật/UI-02/UI-03 còn riêng. UI-04 đạt tự động; bước triển khai kế tiếp UI-05a.

### Bổ sung UI-05a ngày 25/10

Danh sách `WarehouseReceiving/Index` chỉ bổ sung marker header, stylesheet và adapter dưới 768 px. Base renderer thêm các hook đọc trước lọc/sau render; loader, request singleton/timeout, thống kê toàn bộ phiếu, pagination, create modal/payload và `GaoReceiptDocumentActions` vẫn là chủ sở hữu cũ. Adapter không fetch/storage/submit/clone; chỉ chuyển button/mã/trạng thái bằng anchor rồi khôi phục khi về desktop, thêm lọc cục bộ và nội dung trình bày.

Lọc kho dùng `LegalEntityId` + tên kho có sẵn trong summary; hai kho cùng tên trong cùng HKD được nhóm chung vì API không có WarehouseId. Nguồn là có/không liên kết `PurchaseOrderId`, không suy diễn lý do nhập trực tiếp. Tiến trình thể hiện giai đoạn Nhập hàng/Cần sửa → Chờ duyệt theo status, không dựng phần trăm hay cộng số lượng khác đơn vị. Quyền đổi tên/xóa do partial/controller cũ; menu vẫn từ `AdminMenuItems`, không sinh dữ liệu menu.

Build 0 lỗi/cảnh báo, 7 hồi quy và 28 nhóm browser/10 viewport qua; DOM desktop khôi phục và ảnh khớp khi cố định animation, 60 hash cùng snapshot chứng từ/dòng/tồn/công nợ/menu giữ nguyên. Chỉ SQL GUID/Web/Chrome headless riêng; không đụng Chrome hoặc database cửa hàng. [Báo cáo UI-05a](MOBILE-UI-05A-RECEIVING-LIST-20261025.md). UI-05b–e và nghiệm thu thiết bị thật còn riêng.

### Bổ sung UI-05b ngày 25/10

Chi tiết `WarehouseReceiving/Detail` thêm marker và hai asset opt-in dưới 768 px. Adapter chỉ chuyển node topbar và `receiptDocumentActions` vào Thông tin rồi khôi phục khi về desktop; nhãn chỉ xem lấy CanEditReceiving/status từ Razor, không ghi trạng thái nghiệp vụ và vẫn hiện lỗi gốc. Giữ nguyên `_ReceivingApp`, hai tab, capture, feedback, form/handler lệnh nhận/gửi/đổi số lượng, quyền và menu cấu hình. Không tạo API/menu/migration. Tổng SL trộn quy cách ẩn riêng phone; số lượng từng dòng giữ nguyên.

Giới hạn drag-target của sidebar trong vùng nội dung để không chặn header/dock. `restoreListViewport` trong owner cũ chỉ thêm nhánh phone đang ở scroll 0 giữ 0; các dòng phía dưới và desktop giữ phép neo cũ. Source đối chiếu toàn phần ngoài nhánh này, không thay command ID/row version/payload/ack. Probe cuộn cũ được giữ nguyên và qua sau sửa; baseline trước đó cũng có dịch 154 px.

Build 0 lỗi/cảnh báo, 18 hồi quy, 23 nhóm browser/10 viewport và probe cuộn SQL dài qua; desktop khớp pixel/node, nhận 3 Hộp đúng một dòng/một lần rồi gửi duyệt, tồn/menu/công nợ/phiếu khác giữ nguyên. 72 hash bảo vệ giữ nguyên. Chỉ Web/SQL GUID/Chrome headless riêng, không tác động Chrome/database cửa hàng. [Báo cáo UI-05b](MOBILE-UI-05B-RECEIVING-DETAIL-20261025.md). Camera/mic trong test được mô phỏng; UI-05c–e và thiết bị thật còn riêng.

### Bổ sung UI-05c ngày 25/10

`WarehouseReceiving/Detail` chỉ thêm stylesheet/script packing phone. Adapter thêm nhãn đơn vị cạnh control gốc, aria-describedby có khôi phục, tóm tắt số dòng/mục từ counter chủ sở hữu cũ; nút cần xử lý/ảnh chỉ chọn tab Thông tin và mở details có sẵn. Note/photo được chuyển nguyên node vào details không bắt buộc; label Chụp/chọn ảnh dùng native file input gốc, owner photo vẫn xử lý/kiểm tra/thu nhỏ. riQuantity min/step chỉ trên phone là .001 theo số lẻ hiện hành; trở về desktop khôi phục thuộc tính gốc. Không gán giá trị form, tính lại quy đổi, gọi API/storage, clone control hoặc tạo lệnh nghiệp vụ.

Quyền capture/review/approve, trạng thái pending, ảnh và dữ liệu draft vẫn từ owner máy chủ/các module cũ. Không thêm menu/API/migration, không cộng số lượng khác quy cách thành tiến độ. Hai lốc ×4 và ×6 trong fixture được phân biệt bằng metadata gốc. Build 0 lỗi/cảnh báo, 26 hồi quy, 18 nhóm UI-05c/10 viewport, 23 nhóm UI-05b và probe cuộn cũ qua; 88 hash và desktop pixel/node/form giữ nguyên. SQL xác nhận lượng/ảnh/bản nháp chính xác, danh mục/tồn/menu không đổi; chỉ SQL GUID/Web/Chrome headless riêng. [Báo cáo UI-05c](MOBILE-UI-05C-PACKING-20261025.md); UI-05d/e và thiết bị thật còn riêng.

### Bổ sung UI-05d ngày 25/10

Owner nhận hàng hiện có giữ command ID và business payload cho lần nhận chưa được xác nhận; rowversion/lease vẫn theo giao thức hiện có. Reload phục vụ xác nhận phải báo lỗi lên caller; không đóng popup hoặc phát saved/quét tiếp nếu thiếu nhật ký đúng command, dòng tương ứng hoặc lượng trên dòng không khớp. Trường số lượng/quy cách/tên/ảnh/ghi chú chờ được giữ, thao tác chọn khác không thay lệnh đang chờ. Camera và batch quantity giữ owner cũ.

PUT sửa tổng số lượng hàng đã biết hiện không có command ID: không thêm ID/API mới. Khi mất phản hồi, đối chiếu snapshot cùng phiếu/dòng, số lượng/đơn vị/ghi chú. Nếu khớp thì xác nhận bằng lần đọc; nếu phiên bản phiếu và dòng gốc chưa đổi thì mới gửi lại đúng payload/tokens cũ; xung đột giữ chờ đối chiếu. POST quantity của hàng chờ hoàn thiện tiếp tục dùng command cũ, giữ flag và payload khi partial thay input; nút Lưu lại nhận cả flag chưa xác nhận dù value bằng default. Không bỏ quyền/rowversion hoặc tạo hàng đợi offline.

Razor/CSS, desktop và menu giữ nguyên; chỉ sửa bốn owner JS hiện có, không thêm controller/service/schema/menu. Build 0 lỗi/cảnh báo, 35 hồi quy, 17 nhóm browser, toàn luồng, tương tác và cuộn qua; 87 hash, desktop pixel và SQL bảo vệ khớp. [Báo cáo UI-05d](MOBILE-UI-05D-ACKNOWLEDGEMENT-20261025.md); UI-05e và thiết bị thật còn riêng.

### Bổ sung UI-05e ngày 25/10

Ba handler bước trên phone (thẻ quantity, sheet và quick popup) dùng tối thiểu 0,001 và làm tròn ba số thập phân, cùng miền lượng hiện có của máy chủ. Giảm 1,125 đi một phải giữ 0,125, không ép lên 1. Điều kiện width <768px; nhánh desktop giữ mức 1 và đúng cách tính trước đây, kể cả số có nhiều chữ số thập phân ở popup. Thêm một rule CSS phone ẩn khung quét trống của phiếu chỉ xem chưa có lần nhận; khi có xác nhận vẫn hiển thị. Phần còn lại của ba module khớp snapshot; command/retry/ack, controller/service/DTO/quyền, Razor/menu và CSS khác không đổi. Không thêm migration, API, storage hoặc offline queue.

Build 0 lỗi/cảnh báo, 50 hồi quy, 28 nhóm UI-05e/10 viewport, 17 nhóm ack và probe tương tác/cuộn/reload qua. Hai quy cách cùng tên Lốc vẫn có conversion và hệ số riêng; retry không cộng trùng, offline chỉ gửi lại khi người dùng yêu cầu. Probe chỉ xem gửi request có CSRF hợp lệ để xác nhận máy chủ chặn trạng thái; store khác trả lỗi không tìm thấy của cửa hàng hiện tại, tài khoản chỉ View bị chặn theo gate cũ. Desktop pixel/DOM/cách tính, 95 hash và SQL bảo vệ khớp. [Báo cáo UI-05e](MOBILE-UI-05E-ACCEPTANCE-20261025.md). UI-05a–e hoàn tất phần tự động; thiết bị thật vẫn mở, bước triển khai kế tiếp UI-06a.

### Bổ sung UI-06a ngày 25/10

Đầu vào POS dùng một adapter trình bày và CSS chỉ dưới 768px. Header Prime/v3/legacy mở thông tin ca/kho/quầy/nhân viên bằng details native; các node context/readiness/phím tắt được chuyển cùng node và hoàn nguyên vị trí ở desktop. Quầy legacy chỉ đọc dữ liệu terminal đã được máy chủ resolve trên posShell. Giữ nút menu cấu hình, đơn giữ, tài khoản, input/autocomplete, command chips, scan feedback/history và toàn bộ owner giỏ/thanh toán/offline/overlay gốc. Sửa position tài khoản, nhãn phím tắt/đơn giữ và nút đóng đơn giữ bị CSS cũ che/tràn riêng phone. Khi chiều cao ≤680px hoặc đang mở ca/quầy, tái dùng natural-flow fallback của Prime để không co mất giỏ; thanh tác vụ đi sau giỏ trong chế độ cuộn tự nhiên để không che dòng. Camera được bổ sung theo yêu cầu tiếp theo ở mục dưới; menu, API, migration, storage và handler nghiệp vụ giữ nguyên.

Build 0 lỗi/cảnh báo, 49 hồi quy, 16 nhóm browser/10 viewport và hai probe tìm/barcode qua. Giỏ cũ sống qua đổi viewport/Prime/v3/legacy; SQL kiểm đúng từng lượng/quy cách, menu/tồn/giá/thanh toán/ca/store khác không đổi; 191 hash bảo vệ khớp. Desktop đối chiếu mọi computed property/box trong cùng lượt event-loop, hoàn nguyên header DOM gốc và rà ảnh; không tuyên bố ảnh PNG đồng nhất byte do khử răng cưa Chrome. Một probe tìm hàng cũ được sửa kỳ vọng numeric suggestion theo xử lý POS hiện có; production barcode không đổi. [Báo cáo UI-06a](MOBILE-UI-06A-POS-ENTRY-20261025.md). UI-06b là bước tiếp, nghiệm thu Android/iOS/máy quét thật còn riêng.

### Bổ sung camera POS theo yêu cầu ngày 25/10

Camera POS chỉ hiện trên phone <768px, dùng cùng ZXing BrowserMultiFormatOneDReader 0.1.5 bản local của nhận hàng, tải theo nhu cầu bằng nonce CSP hiện có. Modal nằm cùng layout để Prime giữ owner overlay; guard phím chỉ hoạt động khi camera đang mở và được đăng ký trước keyboard POS, bao gồm legacy. Camera dừng trước khi chuyển đúng một mã qua input gốc và PosBarcode.scanCurrentCart(); số lượng, quy cách, giá, quyền, online/checkout locks, command/ack/retry và scan guard do module cũ xử lý. Không tự xác nhận thành công hoặc dựng API/queue mới.

Generation chặn kết quả/stream đến muộn sau đóng, đổi viewport, chuyển nền/pagehide; session/order/store/terminal/user được đối chiếu trước handoff. Chỉ quét lại theo thao tác người dùng, không tự bật camera khi quay lại. Dùng UI lock và chính sách phiên gốc: có thể thao tác ngoại tuyến nếu PosOffline.canWork() cho phép, khóa khi phiên không sẵn sàng hoặc đang checkout; không tạo queue riêng. Nút nhập tay và máy quét hiện có vẫn dùng được. Desktop/tablet ẩn control mới và không yêu cầu camera.

49 hồi quy, 16 nhóm camera và 16 nhóm POS gốc qua; SQL xác nhận bia 2 Lon, 4 Lốc ×6, nước 3 Lon trong cùng draft, không xuất tồn hoặc thanh toán; menu/giá/ca/store khác không đổi. 197 hash bảo vệ khớp. Probe camera tiêm lỗi mạng chặn worker riêng trong profile thử; probe POS gốc kiểm với worker thật. [Bổ sung camera POS](MOBILE-UI-06A-POS-CAMERA-20261025.md). Camera/torch/giấy in/Android/iOS thật còn nghiệm thu; bước tiếp UI-06b.

### UI-06b: giỏ, khách hàng và công cụ đơn

Thêm pos.cart.mobile.css, chỉ media <768px và root data-pos-phone của adapter UI-06a. Giữ tr/inputs/actions do renderer gốc tạo; menu Bootstrap hiển thị trong flow thẻ để không bị clip bởi vùng cuộn. Bảng giá gốc trình bày thành thẻ quy cách trên phone, giữ đủ giá lẻ/sỉ/áp dụng và header ngữ nghĩa. Khách/điểm/voucher, eligibility, link hồ sơ và editor ghi chú/giảm giá dùng nguyên node, state, IDs và handlers. Không thêm DOM/owner/storage/API/menu/migration.

PosOrder chỉ đổi một selector closest từ .pos-line-price-btn thành .pos-line-price-btn, [data-price-line-id], vì menu renderer đã xuất data hook nhưng thiếu class cũ. Vẫn cùng handler và popup bảng giá, không sửa giá, lượng, điều kiện áp dụng hoặc desktop CSS. Tất cả phần còn lại của PosOrder bằng snapshot; renderer, customer, payment, barcode, keyboard, Prime, offline, camera và controller/service/quyền/menu không đổi.

Build 0 lỗi/cảnh báo, 57 hồi quy + 3 JS, 14 nhóm giỏ/7 viewport và 16 nhóm POS gốc với worker thật qua. Năm so sánh CSS/box ở 768/1024/1440px khớp; SQL xác nhận cùng Draft/tổng 287.000/không thanh toán/xuất tồn/tiêu điểm/voucher, fingerprint cửa hàng không đổi và 203 hash bảo vệ khớp. [Báo cáo UI-06b](MOBILE-UI-06B-POS-CART-CUSTOMER-20261025.md). UI-06c là bước tiếp; Android/iOS thật còn nghiệm thu.

### UI-06c: thanh toán điện thoại, ngày 26/10/2026

Thêm pos.checkout.mobile.css, chỉ media <768px/root data-pos-phone. Cùng paymentModal/paymentForm/payMethod/payAmount/payReference/ledger/error/footer gốc; Bootstrap giữ owner modal. Prime tiếp tục dùng submit host gốc. Adapter mới chỉ đổi hai câu hướng dẫn phone và dời đúng btnAddPayment legacy vào footer, khôi phục node/vị trí/nội dung desktop; geometry legacy dùng visualViewport, không quản phím hoặc thu tiền. PosPayment chỉ gọi lại renderer preview có sẵn sau phục hồi input, tránh giữ trạng thái đủ tiền cũ; phần còn lại khớp snapshot. Không chạm render/keyboard/Prime/offline/receipt/controller/service/DTO/quyền/menu, không thêm input/handler/API/storage/queue/migration.

Giữ validation mã tham chiếu/số tiền, reset theo balance khi mở lại, pending collection identity, ledger và tự chốt tiền mặt. Phản hồi 409 sau lần thu đã commit được đối chiếu cùng mã; 5xx vẫn theo prepared-offline journal gốc, không sửa chính sách để làm test qua. 59 hồi quy, 7 nhóm HTTP/SQL, probe tiền gốc và 7 nhóm feedback/in giả lập qua; 14 phone, 2 bàn phím mô phỏng, 5 desktop và 206 hash khớp. [Báo cáo UI-06c](MOBILE-UI-06C-POS-PAYMENT-20261026.md). Bước tiếp UI-06d; thiết bị/in thật còn nghiệm thu.

### UI-06d: QR và bằng chứng ngân hàng, ngày 26/10/2026

Thêm pos.transfer.mobile.css/js, chỉ phone <768px và root data-pos-phone. Cùng paymentQrModal, bankTransferReviewModal, QR image/amount/account/content và PosQrHistory gốc. Adapter dời chính nhóm nút QR, review actions/error/footer vào footer phone; dời vùng paymentInlineErrorBox gốc vào QR khi QR hiện, trả về khi đóng hoặc ≥768px. Không clone ID, tạo nút xác nhận, bắt phím, thêm fetch/storage/state/API/tính tiền hoặc tranh owner Bootstrap/Prime. Các comment marker giữ đúng vị trí/node/focus/disabled state khi resize. PosPayment chỉ đổi cách tìm canonical paymentInlineErrorBox từ query trong paymentModal sang document.getElementById để node dời qua QR vẫn được owner tái sử dụng; tránh tạo trùng ID và giữ cảnh báo cũ. Phần còn lại payment/history/bank review/notifications/ACB/offline và controller/service/DTO/quyền/menu/config/migration khớp snapshot. Nút ACB động vẫn do owner gốc chèn vào nhóm tác vụ gốc.

Chế độ chờ/cảnh báo/thủ công/ACB tự động/read-only do server và owner hiện hành quyết định; CSS giữ hidden/disabled, không mở quyền xác nhận. Local QR thật mở/reload cùng ảnh/số tiền. Sau commit nhưng phản hồi 409, lỗi hiện ngay QR và thử lại cùng QR tạo đúng 1 payment; hủy/chờ không thành tiền. 394 hồi quy, 7 nhóm browser/SQL, 28 phone/4 visualViewport mô phỏng/5 desktop và 211 hash qua. [Báo cáo UI-06d](MOBILE-UI-06D-POS-QR-BANK-20261026.md). Điện thoại/ngân hàng/notification device thật còn nghiệm thu; UI-06e là bước tiếp.

### UI-06e: phiên và giao hàng, ngày 26/10/2026

Thêm pos.session.mobile.css/js chỉ <768px/root data-pos-phone. Status banner/tác vụ journal, native privacy dialog và deliveryModal/form/fields/actions dùng node/owner gốc. Adapter chỉ dời deliveryMessage vào footer, link tra đơn gốc vào body để chừa chỗ khi bàn phím mở, dùng comment marker khôi phục đúng vị trí ≥768px và visualViewport. Không tạo form/control, handler, phím, fetch/storage/queue hoặc tự cấp quyền. Delivery partial và mọi offline/core/privacy/worker/writer-lock, payment, delivery, customer display và opener cấu hình, controller/service/DTO/menu/quyền/config/migration khớp snapshot.

Worker thật tải lại phone offline và giữ journal. Đăng xuất khi offline khóa ngay hai tab; đăng xuất server và đăng nhập đúng nhân viên/quầy đồng bộ lại. Thuật toán giữ/khóa/xóa cache theo privacy owner gốc; tab thứ hai không ghi. Giao hàng vẫn yêu cầu online/đồng bộ xong, recipient validation, kiểm phiên bản/fingerprint và cùng intent sau 503/reload; SQL đúng 1 đơn/revision/audit, 0 payment và tồn không đổi. Mở màn khách chỉ từ DOM menu đủ điều kiện, không thêm shortcut/menu. 358 hồi quy, 111 JS, 9 nhóm browser/SQL, 42 phone/2 viewport mô phỏng/6 desktop và 219 hash qua. [Báo cáo UI-06e](MOBILE-UI-06E-POS-SESSION-DELIVERY-20261026.md). Còn điện thoại/màn khách thật; tiếp UI-06f.

### UI-06f: partial canonical, ngày 26/10/2026

_OrderNoteBox/_OrderDiscountBox cũ chưa có caller được thay bằng đúng hai fragment ghi chú/giảm giá trước đây nằm trong _DraftMetaPanel. Parent giữ wrapper và gọi mỗi fragment một lần; Prime qua _POSPrimeShell, legacy qua nhánh Index hiện hữu. Bung partial tái tạo HTML canonical cũ ngoài comment/whitespace giữa tag. Giữ mọi id/class/data hook/label/type/min/step, không thêm CSS/JS/handler/control/menu/quyền/API/migration. Tất cả script/style/Index/layout/controller/service/DTO/config khớp snapshot. PosOrder giữ owner lưu; PosApp chỉ toggle/đóng editor, không thêm lệnh hoặc thu tiền.

21 phone/3 route, 5 so sánh DOM/style/box desktop và 33 scan ID không trùng. Slow note khóa bấm lặp; Ctrl+Enter/Enter/Ctrl+S/save/clear đúng một request. Chốt 128.000 sau giảm 3.000, đúng một khoản thu/movement; menu/store/giá/tùy chọn khách/store khác giữ nguyên. 67 hồi quy, probe tiền gốc và 7 feedback qua; 225 hash bảo vệ. [Báo cáo UI-06f](MOBILE-UI-06F-POS-EDITOR-AUDIT-20261026.md). UI-06a–f qua tự động, còn thiết bị thật; bước tiếp UI-07a.

### UI-07a: ca trên điện thoại, ngày 26/10/2026

POSShiftPage chỉ thêm import CSS/adapter. CSS dưới 768px; adapter đưa chính block gốc vào một cột, không clone ô nhập/nút, dùng marker trả lại desktop. Thông tin cửa hàng/kho dùng details điện thoại và trả nguyên vị trí khi resize. Modal theo visualViewport, header/footer cố định, body cuộn; native keyboard thay keypad trình bày trên phone. pos.shift.page.js/close-draft/printing và mọi controller/service/DTO/quyền/menu/config giữ nguyên. Tiền dự kiến từ server, tiền thực đếm/nháp và xác nhận lệch dùng owner cũ.

48 .NET + 20 JS, 6 nhóm browser/SQL, 10 so sánh CSS/box desktop và 3 probe gốc qua. Nháp đi đối soát/quay lại giữ đúng ca/số tờ/note; SQL đúng một ca/phiếu đóng, lệch -5.000, không payment/tồn. 298 hash bảo vệ. [Báo cáo UI-07a](MOBILE-UI-07A-POS-SHIFT-20261026.md). Còn điện thoại/két/in thật; nguồn server.py ngoài repo cho probe atomic hiện không có nên chưa chạy lại probe này. Tiếp UI-07b.

### UI-07b: lịch sử ca/phiếu trên điện thoại, ngày 26/10/2026

Hai view thêm asset; phiếu thêm select quầy chỉ hiện phone. CSS dưới 768px; adapter dùng details cho context/filter, mở summary ca thành màn riêng với nút quay lại và giữ nguyên node/giá trị. Modal phiếu một cột, header/footer tách body cuộn theo visualViewport; node được trả lại vị trí cũ khi về desktop. pos.shift.history.page.js giữ nguyên cả abort/sequence/report/CSV. Các hàm tạo/sửa/xem/in/hủy/bind của handover giữ nguyên. Chỉ thêm lookup option từ assignments có sẵn, terminalId vào query phone và xóa lọc ẩn khi trở lại desktop. Không sửa controller/service/DTO/quyền/menu/config/migration/print template. Lịch sử luôn ca của user hiện tại, không thêm filter thu ngân khác.

48 .NET + 20 JS, 7 browser/SQL, 30 phone/7 desktop/38 scan ID; 305 hash bảo vệ. Phiếu 300.000 sửa/nhận 350.000 một lần, một phiếu 100.000 hủy; số lịch sử/thu chi/menu/store/tồn/payment giữ nguyên. Probe quản lý ca chỉ sửa kỳ vọng menu cố định thành menu cấu hình rỗng của fixture; toàn bộ vòng đời nghiệp vụ còn nguyên và qua. [Báo cáo UI-07b](MOBILE-UI-07B-POS-HISTORY-HANDOVER-20261026.md). Còn điện thoại/in/két thật; tiếp UI-07c.

### UI-07c: đối soát và quản lý duyệt ca trên điện thoại, ngày 26/10/2026

Hai view chỉ thêm import CSS/adapter. Dưới 768px, đối soát tách rõ quỹ dự kiến/thực đếm/chênh lệch, tab và bằng chứng dạng thẻ. Quản lý đưa queue và ca lệch lên trước, bộ lọc/thống kê trong native details; không tự đổi bộ lọc ban đầu. Adapter chuyển chính form và submit button gốc vào bố cục phone; form= liên kết native submit, hidden theo form gốc, không relay click hoặc gửi command. Modal body cuộn theo visualViewport, header/footer cố định, min 44px/field 16px; giảm motion theo hệ thống. Tất cả node/attribute/vị trí được hoàn trả desktop. Các owner reconciliation/dashboard/detail, controller/service/DTO/permission/menu/print/config không đổi. Không tạo menu/migration hoặc thêm quyền.

Build sạch; 48 .NET + 20 JS, 5 nhóm browser/SQL, 26 phone/9 đối chiếu computed CSS/box desktop/35 scan ID, 315 hash bảo vệ. SQL xác nhận expected 226.000/employee actual 225.000/admin received 224.000, lý do/actor chính xác; sai reason không POST, lỗi giữ draft, double tap chỉ một POST, reload lỗi sau thành công vẫn khóa form, ghi đè bị 409. Hai probe gốc đối soát và bàn giao/in/nhận/đóng/duyệt qua, source không sửa. [Báo cáo UI-07c](MOBILE-UI-07C-POS-RECONCILIATION-APPROVAL-20261026.md). Còn điện thoại/keyboard/safe-area và in/két thật; tiếp UI-07d.

### UI-07d: dashboard POS trên điện thoại, ngày 26/10/2026

Một view chỉ thêm CSS/adapter. CSS dưới 768px: header navy/blue, context ca, KPI hai cột, quỹ/cảnh báo trước, thu-chi/biểu đồ trong native details. Adapter reparent chính node bằng marker, không clone số/field, không fetch/storage/command. Mở nhóm biểu đồ chỉ gửi resize để owner cũ đo lại khung. Desktop trả nguyên vị trí; 3 so sánh computed CSS/box ở 768/1024/1440px qua. Script gốc chỉ thêm guard trước redraw resize: bỏ qua khi aria-busy hoặc content hidden, tránh vẽ lại snapshot ca cũ khi đang chuyển ca/lỗi. Guard áp dụng mọi kích thước; bố cục desktop, API, timer/abort/sequence, format/tính tiền và controller/service/DTO/quyền/config/menu giữ nguyên.

52 .NET + 20 JS, 5 nhóm browser/SQL, 12 phone/3 desktop/15 scan ID, 319 hash. SQL không đổi đơn/payment/thu-chi/tồn/phiếu/ca/menu/cấu hình; completed sales 120.000, cash+noncash buckets 200.000, expected 340.000 được phân biệt đúng. Test contract cũ đổi kỳ vọng lối tắt cố định thành provider cấu hình, không tạo menu. [Báo cáo UI-07d](MOBILE-UI-07D-POS-DASHBOARD-20261026.md). Tiếp UI-08a; còn điện thoại và nghiệm thu thiết bị/helper két ngoài đã ghi ở UI-07a.

### UI-08a: yêu cầu nghiệp vụ trên điện thoại, ngày 26/10/2026

Index chỉ thêm CSS/adapter dưới 768px. Hai partial và ba script requests/cash/payment giữ nguyên. Danh sách dùng node bảng gốc, role và nhãn lấy từ thead; status/tab/filter/URL/page/owner giữ nguyên. Native dialog toàn màn hình, header/footer tách body cuộn theo visualViewport. Action node gốc reparent bằng marker; submit button dùng form= trỏ form gốc, giữ native validation và listener. Mutation microtask loại action cũ khi body rerender; không clone/relay click/API/storage hay tính tiền. Busy/disabled/Escape vẫn do script gốc. Desktop hoàn trả node/thuộc tính/vị trí, 10 so sánh computed CSS/box qua. Không đổi controller/service/DTO/quyền/menu/config/print/migration/seeder.

65 .NET + 20 JS, 5 nhóm browser/SQL, 25 phone/10 desktop/36 scan ID, 362 hash; probe requests gốc qua. Tạo yêu cầu chưa đổi quỹ; admin duyệt cash/payment/deposit lần lượt đưa expected 260.040 → 245.040 → 245.000 → 195.000. Một yêu cầu rút, một từ chối, đối soát lại cả ba điều chỉnh đã duyệt; actual 259.000/received 258.000/phiếu chốt/tồn/balance cọc/menu/store giữ nguyên. [Báo cáo UI-08a](MOBILE-UI-08A-POS-REQUESTS-20261026.md). Tiếp UI-08b; còn thiết bị thật và các nghiệm thu trước.

### Toàn UI-08a/b/c/d, ngày 26/10/2026

UI-08b/c dùng CSS dưới 768px và adapter chỉ thêm/hoàn trả role, nhãn từ thead trên node bảng gốc. PendingRestock giữ button/listener/disabled và POST cũ; CSS đặt action sau các dòng đúng phiếu. Chỉ thêm dữ liệu đọc: snapshot số nhận/đơn vị bán/số gốc và SUM fragment đã hoàn tất có InventoryTransactionId theo dòng trả + kho, dùng tenant filter hiện hành. Phần ReadCost/Complete/khóa/idempotency/tiền/kho/audit nguyên byte; controller/quyền/quầy/menu/cấu hình không đổi, không migration/seeder/API mới. Desktop không hiện các fact d-md-none; 6 đối chiếu CSS/box mới và 10 của requests đều qua.

Archive vẫn chỉ đọc theo controller gốc, không tạo lệnh sửa/liên kết. GET search/page, metadata, số tiền/phương thức nguồn và JSON gốc giữ nguyên; 53 bản ghi test chứng minh phân trang, literal %/_, dữ liệu nguồn đã escape và store scope. UI-08d có lệnh thực bị chặn vì giá vốn/CSRF/quyền/store, bấm lặp và retry không nhập/hoàn thêm, dữ liệu return đổi khi form đang mở, ca đóng rồi mở lại. SQL cuối stock 96, 5 return/refund, 1 restock audit; refund ca cũ 2.020/ca mới 20. Query đọc đã nhập trả 97 và store khác 0. Requests được chạy lại, expected 195.000, actual 259.000/received 258.000 và phiếu chốt gốc giữ nguyên.

79 .NET + 31 JS, 11 browser/SQL, 55 phone/16 desktop/72 scan ID, 410 file bảo vệ; ba probe gốc qua. [Báo cáo toàn UI-08](MOBILE-UI-08-COMPLETE-20261026.md). Triển khai UI-08 hoàn thành; nghiệm thu Android/iOS và các nghiệm thu trước vẫn mở. Tiếp UI-09a.

### Toàn UI-09a/b/c/d, ngày 26/10/2026

Ba view DeliveryPage chỉ thêm asset dưới 768px; CSS/adapter mới tách danh sách và chi tiết trên phone, giữ node input/button/disabled/draft/listener gốc. Bảng chuyến và giao/trả dùng thẻ với role/nhãn thead, không tính lại lượng/tiền. Kho nguồn/chủ hàng gốc dùng chính node readonly gốc đưa lên gần người nhận bằng marker rồi trả về inventory khi desktop. Chọn lại cùng mục đợi owner render xong mới chuyển chi tiết; lựa chọn bị chặn không hiện nhầm dữ liệu. Nút lưu/chốt sau dòng hàng để không che nội dung. Không fetch/storage/click relay/submit hoặc command mới trong adapter. Desktop từ 768px trả nguyên bố cục/thuộc tính, 20 so sánh toàn computed CSS/box qua.

Controller/service/repository/DTO, ba owner nghiệp vụ, bill/in, sidebar/layout/AdminMenuItems/quyền/cấu hình giữ hash; không migration/seeder/menu/API mới. 303 hồi quy + 1 test browser bàn giao/SQL (304), 107 JS; 26 nhóm, 70 phone/20 desktop/90 scan ID, 553 hash. Soạn 18 intents/21 events không tiền/tồn vật lý; inventory 4 intents không đổi tiền/tồn; chuyến 9 root commands, xuất đúng 5 đơn vị/51.000 và custody thật; bàn giao một phần 2,5/4 và đơn không giao trả tổng 4,5, chi phí trả 46.500/bán 22.500, chỉ một thanh toán cash 50.000, không xuất kho lần hai. Mất response rồi retry đúng body không ghi lặp; source/cart/config giữ nguyên; bill dẫn đơn đã chốt và chuyến chỉ hoàn thành sau đủ điểm. [Báo cáo toàn UI-09](MOBILE-UI-09-COMPLETE-20261026.md). Tiếp UI-10a; còn thiết bị/QR/in thật và các nghiệm thu trước.

### Toàn UI-10a/b/c/d/e, ngày 26/10/2026

Chín view PurchaseRequests/PurchaseOrders/PurchaseReceiving chỉ thêm CSS/adapter dưới 768px. Danh sách giữ renderer/phạm vi/bộ lọc cũ; các bảng thành thẻ có nhãn tính đúng rowspan, cùng sản phẩm nhiều đơn vị không nhầm cột. Modal/bộ lọc dùng khung cuộn có footer vừa viewport; thao tác đặt sau nội dung. Adapter chỉ thêm role/nhãn/trình bày, không fetch/storage/clone/click/submit/tính lượng; giữ node input/form gốc và trả toàn bộ thuộc tính về desktop. 30 đối chiếu computed CSS/box từ 768px khớp.

Form vi-VN thực phát hiện 2.5 bị đọc thành 25; Web binder mới giới hạn đúng bốn trường lượng của PR/PO, giữ ý nghĩa số thập phân không phân cách hàng nghìn. JSON, giá tiền, receipt binding và các service/chính sách/quyền gốc không đổi. 14 test số/vùng/scope và luồng native/SQL qua. CommercialEdit giữ chính sách khóa tên/đơn vị/lượng; giá/VAT/chi phí ở phiếu nhập theo quyền, không thêm trường thương mại giả.

593 .NET + 37 JS, 7 browser/API/SQL, 15 trạng thái/105 phone/30 desktop/27 hộp thoại và 796 hash qua. Duyệt 2,5 đơn vị +3 hộp, nhận 1,25 hộp ×6 =7,5 gốc vào đúng dòng/đơn; Finish chỉ chờ duyệt, tồn vẫn 100 và lượng PO đã nhận vẫn 0. Store/config/menu không đổi; không seeder/migration/menu mới; không chạm Chrome đang mở. [Báo cáo toàn UI-10](MOBILE-UI-10-COMPLETE-20261026.md). Tiếp UI-11a; còn thiết bị thật và các nghiệm thu trước.

### Toàn UI-11a/b/c/d/e/f, ngày 26/10/2026

Bốn view StockDocumentManagement/Index, Edit, Split và InputInvoiceLibrary/Index chỉ thêm import asset dưới 768px. Adapter gắn role/nhãn lên bảng vật lý/tách phiếu gốc, đưa lối đến phần có sẵn, thu gọn bộ lọc tại chỗ và khôi phục thuộc tính desktop. Giữ chủ tab goods/pricing/settlement/freight, hidden/disabled/read-only, native form/dialog, RowVersion, lease và lệnh gốc; không clone trường, tính lượng/tiền, fetch/storage hoặc relay lệnh. XML/bill/chương trình/giá bán giữ bảng và chủ tính riêng.

Quyền PO/direct, giá vốn, duyệt, giá bán, audit và chủ thể XML giữ nguyên. Không thay sidebar/AdminMenuItems, tạo menu/seeder/migration/schema/endpoint/quyền/cấu hình. 38 đối chiếu computed CSS/box ở 768/1440px khớp. 1.304 file bảo vệ, 25 file nghiệm thu; 11 probe giữ các kiểm tra nghiệp vụ. Chỉ bốn kỳ vọng hình học được cập nhật: hai footer quick price sticky→static và hai vị trí cuộn lịch sử theo scroll-margin/scroll-padding; không bỏ kiểm tra giá/lượng/lưu/duyệt.

Kiểm tra 320px phát hiện Select2 body-appended có min-width 320px ở x=33 làm rộng trang 353px, kể cả outer span cao 0. CSS phone giới hạn đúng owner/dropdown; negative control tái hiện 353, các biến thể body/modal/zero-height sau sửa vừa màn. Helper đợi chủ calculator/save/receipt rồi fonts/hai frame/animation hữu hạn, poll có hạn 30 giây; chỉ ghi riêng hai route tính thử read-only và heartbeat registry, không loại lệnh ghi khỏi kiểm tra. Gzip sinh từ final-build của bốn asset khớp byte source.

993 .NET duy nhất +143 JS; 8 CLI +4 browser/SQL release, 133 phone/38 desktop/48 hộp thoại; 84 lượt quyền thư viện; 1.304 hash qua. Tổng .NET gộp bằng case/testId ổn định: 989 qua ở broad, hai lỗi đã được chạy lại qua và hai ca browser/SQL mới; không cộng lặp lượt release. Thư viện 14 vai trò/nguồn, 84 resize, năm native filter validation; không lộ giá/file/quyền ghi. Tách phiếu giữ lượng/hệ số nguồn, hai kết quả chờ duyệt, không ghi kho/công nợ. Duyệt thương mại đi qua HTTP/SQL và kiểm tra ghi sổ/payables/audit gốc. [Báo cáo toàn UI-11](MOBILE-UI-11-COMPLETE-20261026.md). Tiếp UI-12a; thiết bị Android/iOS/camera/scanner thật và các nghiệm thu trước vẫn mở.

### Toàn UI-12a/b/c/d/e, ngày 26/10/2026

Mười view StockCountPages, StockTransfer, InventoryAdjustmentDocuments và InventoryIssueManagement chỉ thêm import ba cặp CSS/adapter dưới 768px. Thẻ có nhãn dùng chính dòng, input và renderer gốc; giữ số hệ thống/số đếm/chênh lệch kiểm kê, kho đi/đến và lượng/hệ số/gốc, lý do và trạng thái điều chỉnh. Lối tới phần nội dung chỉ cuộn node hiện có; không clone form, tính lại lượng/giá, lưu nháp hay relay lệnh ghi. Dialog dùng chiều cao vùng nhìn thực tế, body cuộn và footer gốc. Desktop từ 768px khôi phục thuộc tính và bố cục.

UI-12d đã rà theo source: StockDocumentType.Issue=2 chưa có luồng phiếu xuất độc lập. InventoryIssueManagement là case tồn âm có nguồn đơn. Giữ luồng điều chỉnh giảm loại 31 qua movement gốc; loại 70 chỉ điều chỉnh giá vốn, không đổi lượng. Điều chỉnh chỉ hiển thị lượng nguồn cung cấp, không tự gán số tồn trước/sau. Legacy /admin/inventory-adjustment vẫn yêu cầu đăng nhập/Adjustment.View và chuyển tới danh sách điều chỉnh; file Index cũ và controller giữ hash.

177/177 .NET; 2 luồng browser/SQL, 22 trạng thái, 154 phone/44 desktop/42 hộp thoại; 3.488 hash qua. Chuyển 2,5×4=10 ghi đúng cặp -10/+10; điều chỉnh 1,5×4=6 ghi đúng một movement giá vốn 60; nguồn 96 / kho nhận 10. Loại 70 giữ Draft chưa ghi sổ; case tồn âm giữ lượng nguồn, ghi chú native đúng một lần. Quyền/CSRF/cửa hàng và fingerprint menu/cấu hình khớp. Baseline 473 không đổi/87 đã đổi/0 mất. Controller/service/repository/DTO, quyền, RowVersion, chủ lệnh, AdminMenuItems/sidebar và cấu hình cửa hàng giữ nguyên; không migration/seeder/menu/API/workflow mới. Chrome đang mở giữ nguyên; Android/iOS/camera/scanner thật và nghiệm thu trước vẫn mở. [Báo cáo toàn UI-12](MOBILE-UI-12-COMPLETE-20261026.md). Tiếp UI-13a.

### Toàn UI-13a/b/c/d, ngày 26/10/2026

Năm view InventoryInquiry, InventoryLedger, WarehouseManagement, LegalEntityManagement và LegalEntityReconciliation chỉ thêm import ba cặp CSS/adapter dưới 768px. Inquiry/ledger/kho giữ renderer điện thoại gốc; bảng HKD và đối soát thành thẻ có nhãn bằng chính node hiện có. Các select bộ lọc kho được đặt vào DETAILS trên phone, rồi phục hồi đúng vị trí desktop; field identity/value/form/validation/disabled/hidden/readonly giữ nguyên. Form kho/HKD chia nhóm, body cuộn, footer gốc và nút đóng 44px trong header. Không fetch/storage, clone, tính lại số liệu hay chuyển tiếp lệnh ghi trong adapter.

Giá vốn tra cứu tuân vai trò hệ thống ADMIN đúng cửa hàng; quyền * không tự cấp giá. Giữ quyền pháp nhân/Activate/Reconcile/Export, preflight/canary, trạng thái tắt, ưu tiên và mặc định kho/HKD/hóa đơn. Chỉ hai ghi chú trong fixture được sửa qua form gốc; toàn bộ snapshot cấu hình/menu/roles/catalog/tồn/cửa hàng khác khớp. Native đối soát mở đúng đơn nguồn.

Sổ kho hiện có số trước/biến động/sau từng giao dịch và mã nguồn/receipt/XML văn bản. Summary chỉ đếm phát sinh, chưa có tổng tồn đầu/cuối kỳ hoặc SourceUrl/ID mở nguồn tổng quát; không suy ra số dư từ phân trang hay tạo URL giả. 47/47 .NET duy nhất; probe sổ kho gốc và browser/SQL 5 trang qua; 119 phone/34 desktop/30 hộp thoại; 1,5 triệu dòng chạy riêng qua; 3.510 hash. Lượt SQL đầu timeout ở dữ liệu lớn; lượt riêng qua cùng query/test/dataset/timeout, không cộng lặp. Tìm theo sản phẩm nhanh hơn phạm vi Tất cả; lưu thời gian thực trong evidence. Desktop 768/1440px khớp computed CSS/hình học; baseline 468 không đổi/92 đã đổi/0 mất. Không đổi backend/menu/quyền/cấu hình, seeder/migration/schema hoặc Chrome đang mở. [Báo cáo toàn UI-13](MOBILE-UI-13-COMPLETE-20261026.md). Tiếp UI-14a; thiết bị thật và nghiệm thu trước vẫn mở.

### UI-14a/b/c/d/e/f, ngày 26/10/2026

Mười tám parent view chỉ thêm import ba cặp CSS/adapter dưới 768px cho sản phẩm, sáu danh mục và lịch sử/chuẩn hóa barcode. Nội dung Razor, partial, controller/service, quyền giá vốn/nhà cung cấp, validation/RowVersion, giá/quy đổi và trạng thái sẵn sàng bán giữ owner gốc. Filter/nav điện thoại dùng chính node gốc; desktop phục hồi vị trí/thuộc tính. Không fetch/storage/clone/ghi value/chuyển tiếp lệnh trong adapter; không đổi AdminMenuItems, menu/ghim, cấu hình, seeder/migration/schema hoặc Chrome đang mở.

R05 đã xác minh: GET /admin/barcode-manager/{conversionId:int} có Barcode.View và header đúng cửa hàng nhưng thiếu Index view; Shared/_BarcodeManager không có nơi gọi hoặc JavaScript owner. Không công bố route độc lập đã sửa. Điểm vào chuẩn đang hoạt động là Sửa sản phẩm → quy cách → Đơn vị & Barcode → đơn vị đã chọn; chọn đúng biến thể/quy đổi, barcode và lịch sử đã qua native browser. Điểm chạm ảnh trống vẫn mở bộ chọn ảnh gốc, đọc ảnh rồi hủy không ghi dữ liệu. Product/Detail giữ phạm vi gallery/tên/bí danh gốc.

101/101 .NET duy nhất; probe giá đơn vị gốc và browser/SQL qua; 182 phone/52 desktop/45 hộp thoại; 3.513 hash. Bảy viewport 320–767px và chiều cao 440px; desktop 768/1440 khớp computed CSS/hình học; 15 trạng thái hộp thoại tại ba kích thước giữ footer/nút đóng. Mọi thao tác hủy/xác thực/tìm kiếm không ghi nghiệp vụ, fingerprint dữ liệu cửa hàng/menu/quyền/sản phẩm/giá/đơn vị/barcode/tồn khớp; fixture GUID/headless riêng. Hai lượt acceptance đầu sai kỳ vọng 404/302 và bỏ RequestType của fixture; đã sửa riêng test/fixture theo owner gốc rồi qua, không cộng lặp testId. Baseline 450 không đổi/110 đã đổi/0 mất; 6 asset nén khớp byte nguồn. [Báo cáo UI-14](MOBILE-UI-14-COMPLETE-20261026.md). Tiếp UI-15a; thiết bị thật, R05 độc lập và nghiệm thu trước vẫn mở.

### Toàn UI-15a/b/c/d/e, ngày 26/10/2026

Mười view Customer/Index/Edit/Profile, Supplier/Index/Edit, CustomerDebt/Index/Collections, CustomerDeposit/Index, RewardSettings/Index và RewardVouchers/Index nạp ba cặp asset màn hình dưới 768px. Chín view chỉ thêm import; riêng RewardSettings/Index sửa thêm đúng ba data-attribute số sang InvariantCulture. Rà ảnh phát hiện số thập phân vi-VN dùng dấu phẩy làm JavaScript Number đọc NaN rồi về 0 trong xác nhận; serialize bất biến khôi phục số nguồn 1.000/30/30.000 mà không ghi cấu hình, đổi trường nhập hoặc owner JavaScript. Danh sách và tab giữ renderer/quyền gốc; bảng công nợ/cọc thành thẻ có nhãn bằng chính node vật lý. Form/filter giữ giá trị, validation, RowVersion, disabled/hidden/readonly và chủ form. Bộ lọc báo cáo đặt node gốc vào details trong form trên điện thoại rồi trả đúng vị trí desktop; anchor Thu nợ chỉ dẫn tới form gốc. Không request/storage/clone/ghi value/relay lệnh hoặc tính tiền trong adapter.

Trước in, tài chính và thưởng bỏ phần trình bày mới; cờ printing chặn việc áp dụng lại khi media thay đổi. 29 đối chiếu in và 58 đối chiếu desktop khớp computed CSS/hình học; field/form gốc giữ nguyên. Không sửa các view/chủ in UI-22. Supplier/Create GET và POST lỗi dùng Edit; file Create cũ giữ nguyên. Voucher có trạng thái Expired nhưng entity/DTO danh sách/xem nhanh không có ngày hết hạn; không dựng ngày/quy tắc mới.

124/124 .NET duy nhất; ba CLI live và probe thưởng từ Razor gốc qua; 203 phone/58 desktop/29 in/27 hộp thoại; 3.537 hash. Chỉ hai ghi chú khách/NCC trong fixture được lưu qua form native. Công nợ 50.000, cọc 160.000, reward ledger 11.500/23 phát sinh/3 voucher cùng toàn bộ cấu hình/menu/quyền/tồn/cửa hàng khác giữ snapshot. Thu nợ/nhận/hoàn cọc trong bước chuẩn bị fixture qua API gốc và replay cùng request giữ ID; dòng Apply 50.000 gắn đơn là dữ liệu đọc được seed trực tiếp, không tuyên bố ghi qua browser/API. Ba CLI live gốc xác minh công nợ, cọc và hồ sơ; probe thưởng dùng HTML Razor thật do test SQL gốc chụp, không gọi trực tiếp API settings trong lượt probe đó. Giữ bố cục desktop, menu/ghim, cấu hình, seeder/migration/schema và Chrome đang mở; chỉ giá trị xác nhận thưởng được sửa để khớp số nguồn. [Báo cáo UI-15](MOBILE-UI-15-COMPLETE-20261026.md). Tiếp UI-16a; thiết bị thật, ngày hết hạn voucher và điểm chờ lịch sử vẫn mở.

### Toàn UI-16a/b/c/d/e/f, ngày 27/10/2026

Mười bốn view cha Invoice/Index/Detail/ViettelDashboard/ViettelPayload, InvoiceDashboard/Index, InvoiceCorrection/Create, InvoiceInputStock/Index, AutoInvoice/Index, InvoiceProviderSetting/Index/Form, ViettelInvoiceSync/Index và InvoiceIntegrationLog/Index/Detail/Cleanup nạp ba cặp asset chỉ screen dưới 768px. Mười ba view cha chỉ thêm import; InvoiceDashboard sửa thêm một dòng định dạng ngày; _ManualLinesTable sửa ba data-attribute quantity/unit-price/VAT sang InvariantCulture. Mười partial Invoice khác, controller/service/DTO, native JavaScript/CSS, quyền, menu/ghim, cấu hình, khóa và lệnh gốc giữ byte nguồn.

Bộ lọc dùng node gốc trong cùng form, khôi phục đúng vị trí desktop/in. Adapter không request/storage/clone/ghi value/relay lệnh hoặc tính tiền/thuế/tồn. Trước in bỏ trình bày mới và điều hướng nội trang; printing guard chặn observer dựng lại. 62 đối chiếu desktop và 31 đối chiếu in khớp computed CSS/hình học; hidden/d-none và native form/type/CSRF/RowVersion giữ nguồn.

727/727 .NET duy nhất gồm SQL220k riêng; CLI tồn chứng từ gốc qua; 31 trạng thái/217 phone/62 desktop/31 in/21 hộp thoại; 3.548 hash. Browser giữ số 1,25 × 8.000, VAT 8%, tổng 10.800 trong editor nháp và hủy; không gửi ghi nghiệp vụ. Tồn chứng từ từ hóa đơn có đầu kỳ 20 trừ đã phát hành 2 bằng 18, còn phụ thuộc held theo owner gốc; tồn vật lý InventoryBalance là 100 riêng và giữ nguyên. API native chỉ chuẩn bị draft của fixture trước fingerprint. Không phát hành, gọi nhà cung cấp, email, retry/reprocess, lưu cấu hình, đồng bộ hoặc POST cleanup. Quyền ADMIN của điều chỉnh và các policy integration/stock/auto vẫn do controller gốc kiểm tra; canary credentials không xuất hiện trong HTML/log đã sanitize. GET Cleanup giữ DryRun=true.

Release build 0 cảnh báo/0 lỗi; sáu gzip khớp byte; 24 source và 3.548 file bảo vệ; baseline 425/135/0. Chỉ dùng SQL/Web GUID và Chrome headless/profile riêng, giữ Chrome đang mở. [Báo cáo UI-16](MOBILE-UI-16-COMPLETE-20261027.md). Tiếp UI-17a; nghiệm thu thiết bị thật và điểm chờ lịch sử giữ nguyên.

CLI invoice-input-stock gốc qua đủ bốn marker: lượt đầu Razor/API SQL thật trên fixture rỗng, các kiểm tra phân trang/dữ liệu nhiều dòng/preview/filter/holds/retry sau đó dùng response tổng hợp bị chặn bắt theo CLI gốc. SQL 220k riêng qua test Large_legacy_import_is_consumed_once_and_both_tabs_reuse_the_snapshot, gồm giới hạn cold <15s/warm <1s của test và không phát sinh InventoryTransactions; không suy diễn thời gian đo chính xác.

Lượt acceptance đầu thất bại tại FullApplicationFixture vì Web startup timeout, trước khi vào assertion giao diện; TRX và acceptance-startup-timeout.log được lưu nguyên. Root ghi nhận quá tải do nhiều Web fixture khởi động đồng thời, dừng đúng runner sở hữu và chạy lại tuần tự. Lượt broad đầu bị dừng chưa sinh TRX hoàn tất: chỉ ghi sự kiện, không gán số test/pass/fail hay cộng vào executions. Các lượt có TRX vẫn được merge theo testId/tên, giữ lỗi lịch sử và kết quả cuối cùng.

Fixture chuẩn bị draft qua API gốc và gọi GET ViettelPayload thuần trước fingerprint để owner gốc bảo vệ mật khẩu legacy thành gdp:v1; xác nhận storage đã protected và HTML không có canary. Mật khẩu đã protected trước ranh giới chỉ đọc, raw log chứa canary vẫn giữ nguyên trong storage. Fingerprint sau đó tiếp tục bao toàn bộ RowVersion và UserInStores. Ba lượt lỗi route-fixture/log-assertion/legacy-credential-fixture được lưu TRX/log riêng; sửa đúng URL test, đọc redacted:true theo contract gốc và chuẩn bị credential trước boundary, không nới kiểm tra dữ liệu hay đổi nghiệp vụ.

### Toàn UI-17a/b/c/d/e, ngày 27/10/2026

Chín view cha và ba cặp asset mobile chỉ thêm imports, bốn partial UI-17 cùng _AcbCallbackHealth nguyên byte; desktop/in và owner ngân hàng, tiền, bằng chứng, menu/quyền/cấu hình giữ nguồn. 461/461 .NET duy nhất; 50 Node và 3 probe gốc qua; 24 trạng thái/168 phone/48 desktop/24 in/12 hộp thoại; paired snapshot, chưa xác minh toàn workspace.

UI-17 chỉ xác minh paired snapshot + 19 nguồn UI-17. Workspace chung và InvoiceNotifications P3 đang thay đổi không được nhận là đã build/test qua. Bảy file gốc được lấy từ snapshot bất biến; Category.CS bổ sung có byte trùng Git HEAD sạch trước tác vụ; SDK/package/generated inputs giữ nguyên và actual Compile TSV chứng minh đủ 7 shadow + supplement + Test SDK Main, không lọt source mới ngoài UI-17. Không sửa hoặc suppress EF migration warning.

Trong 24 trạng thái có 17 native GET/read states, 4 body Razor native đã capture và 3 phản hồi GET tổng hợp/ràng chờ có provenance. SandboxResult không được mở qua bank test POST; routing thật của host fixture rỗng. Callback key của fixture không giải mã được trong ephemeral key ring: chỉ kiểm tra trình bày Unreadable, không nhận là kết nối hay giải mã thành công. Các legacy probe chỉ dùng synthetic APIs, gồm một xác nhận tiền giả có chủ ý, không viết cửa hàng/ngân hàng thật.

Native browser kiểm tra lịch sử đơn/QR, nguồn xác nhận, đối soát khớp/lệch/chờ, live selection/focus/evidence, modal nháp/hủy, tài khoản, cài đặt và quyền hạn chế. Không bank network hoặc command ghi tiền/settings/routing; SQL giữ khoản thu 10.000, tồn vật lý 100, callback attempts 0 và biên nhận chưa áp dụng. Source scope 3.570 =3.563 current exact +7 snapshot compile copies; baseline 416/144/0; 41 verification cũ giữ nguyên. [Báo cáo UI-17](MOBILE-UI-17-COMPLETE-20261027.md). Tiếp UI-18a; còn full workspace và thiết bị thật.

### Toàn UI-18a/b/c/d/e/f/g, ngày 27/10/2026

Bảy view cha chỉ thêm imports; shared _ManagementReportWorkspace giữ từng byte/BOM. Ba cặp asset mobile dùng native form, dữ liệu, biểu đồ và quyền; desktop/in giữ computed CSS/hình học. 150/150 .NET duy nhất; 16 Node VM và 3 probe gốc qua; 39 trạng thái phân biệt/40 lượt thực thi/280 phone/80 desktop/40 in/33 lượt hộp thoại (11 loại); composite full39 raw giữ nguyên + focused Sales thay một case, nguồn cuối22, paired snapshot, chưa xác minh end-to-end toàn workspace.

UI-18 end-to-end xác minh paired snapshot trước P3 + 19 nguồn UI-17 + 22 nguồn UI-18 (7 view, 6 asset mobile, 2 native JS sửa giữ terminal/giờ local, 7 nguồn kiểm thử). Build mặc định của workspace hiện tại đã qua tại checkpoint riêng: 1 cảnh báo/0 lỗi; initial CS0029/CS1662 đã được xử lý ngoài UI-18. Probe runtime riêng của workspace hiện tại thất bại ở EF PendingModelChangesWarning trước financial/UI assertions; nằm ngoài counts UI-18. End-to-end toàn bộ workspace và InvoiceNotifications P3 đồng thời chưa được xác minh. Paired build dùng 12 bản compile gốc bất biến trước P3 (7 đã dùng ở UI-17 và 5 privacy/audit/security/startup), supplement Category.CS và Test SDK Main gốc. Actual Before-CoreCompile TSV, SDK/content, 6 gzip mobile và 2 gzip native được kiểm riêng; không suppress cảnh báo EF migration, không ghi đè file hiện tại ngoài UI-18.

Native acceptance mới không gửi lệnh tài chính; SQL giữ số liệu, row version, menu/quyền/cấu hình và payload báo cáo. Hai probe CLI gốc có lifecycle chi phí/thu chi có chủ ý trên fixture viết cục bộ riêng; loading probe đi qua fact .NET gốc. Scope nguồn 3629: 3620 file trùng byte tại thời điểm quan sát và 9 drift P3 ghi path/hash/diff riêng; không đồng nhất với 12 compile copies. Baseline 407/153/0; giữ 42 verification trước. [Báo cáo UI-18](MOBILE-UI-18-COMPLETE-20261027.md). Tiếp UI-19a; còn full workspace và thiết bị thật.

### Toàn UI-19a/b/c/d, ngày 28/10/2026

Ba view cha Promotion/Index, DisplayPromotion/Index và PromotionLabels/Index chỉ thêm hai import mỗi file; hai partial _PromotionTable và DisplayPromotion/_Workspace cùng native JavaScript/CSS giữ nguyên. Ba cặp asset chỉ áp dụng screen dưới 768px. Khuyến mãi thành thẻ có nhãn, KPI gọn và phần lọc mở riêng trên điện thoại để thấy chương trình ngay từ màn đầu; form chia thông tin/điều kiện/thời gian/quyền lợi/hàng áp dụng; trình chiếu chọn nội dung rồi sửa, tách lịch/media/xem trước; tem chia chọn hàng/danh sách/mẫu và máy in. Dùng cùng node và field gốc, không đổi giá trị/validation/disabled/readonly/open/hidden, tính ưu đãi hoặc chuyển tiếp lệnh ghi. Desktop/in khôi phục vị trí/thuộc tính và khớp computed CSS/hình học. Đã sửa vùng bấm đóng/xem mẫu, vị trí nút đóng modal trình chiếu từng bị lệch khỏi khung nhìn, vùng cuộn khi bàn phím mở, khoảng đệm icon tìm kiếm và lớp xác nhận validation SweetAlert trên điện thoại sau khi kiểm thử phát hiện nằm sau modal.

73/73 .NET duy nhất; 9 Node và probe promotion-labels gốc qua; 27 trạng thái/189 phone/54 desktop/27 in/91 lượt hộp thoại (13 loại); 81 lượt .NET thực thi, 5 lỗi lịch sử đã có cùng testcase chạy cuối qua, không cộng rerun vào unique. Root xem 14 ảnh thật. SQL fixture xác minh dữ liệu chương trình/điều kiện/lịch trình chiếu, sản phẩm/giá/tồn, menu/quyền/cấu hình cửa hàng và phiên bản hàng giữ nguyên. Kiểm tra cửa hàng khác và lệnh thiếu quyền 403; active display loại nội dung đã ẩn/hết hạn, danh sách tem chỉ trả chương trình hợp lệ. Giá tem nguồn 34.000đ với lượng tối thiểu 2; không gửi job tới máy in thật. Probe promotion-labels cũ dùng HTML trích Razor cùng phản hồi API/Label Print Client tổng hợp và CSS gốc để kiểm hồi quy lệnh; chỉ ma trận browser/SQL mới là bằng chứng các màn mobile từ ứng dụng native. Không nhận probe cũ là đã kết nối/in phần cứng. Màn Promotion với quyền chỉ xem giữ nguyên nút tạo/sửa từ markup gốc; các lệnh Manage vẫn bị controller từ chối 403 như trước.

Build và kiểm thử UI-19 dùng paired snapshot trước P3, giữ toàn bộ 41 nguồn UI-17/UI-18 đã nghiệm thu và ghép đúng 12 nguồn UI-19. Ngoài 12 bản nguồn gốc trước P3 đã dùng ở UI-18, có 43 nguồn baseline khác đã thay đổi trước khi UI-19 bắt đầu; bản compile/content cũ được lấy từ snapshot bất biến, không ghi đè file đang làm. Phạm vi này loại các thay đổi POS/nav/Invoice/channel mới ngoài UI-19; số bản nguồn paired không đồng nghĩa với drift trong scope bảo vệ. Build workspace hiện tại được ghi riêng; runtime hiện tại còn bị chặn tại EF PendingModelChanges trước assertion nghiệp vụ. Không sửa hoặc bỏ qua migration warning; không nhận kết quả paired là nghiệm thu end-to-end workspace chung. GaoApp.LabelPrintClient được tham chiếu khi build và được gắn hash tại checkpoint cuối; nguồn này nằm ngoài snapshot đầu bước, không phải bản nguồn bất biến trước P3. Luồng UI không kết nối bộ in đang chạy trên máy. Các regression LabelClientHost chạy host loopback riêng với FakeTransport và FakeA4Transport, không gửi lệnh đến máy in thật. Assembly đang chạy có đủ 11 Razor view bắt buộc; 28 lượt HTTP yêu cầu identity/gzip đều khớp byte nguồn sau giải mã. Yêu cầu gzip không được coi là bằng chứng máy chủ đã trả gzip; sáu file gzip do SDK tạo được kiểm riêng tại artifact build.

Baseline hiện tại 394 không đổi/166 đã đổi/0 mất trên 560 file; tại đầu UI-19 là 397/163/0. UI-19 chỉ đổi ba view cha trong baseline. Có 14 file baseline khác hash so với nghiệm thu UI-18 nhưng đã có nguyên trạng ở snapshot bắt đầu UI-19; không gán lại cho UI-19. Lịch sử hash/trạng thái của các bước trước giữ nguyên. Giữ 43 verification cũ và toàn bộ cửa sổ/tab Chrome đang mở; chỉ dùng fixture GUID và Chrome headless/profile riêng. [Báo cáo UI-19](MOBILE-UI-19-COMPLETE-20261027.md). Tiếp UI-20a; thiết bị thật và phần nghiệm thu trước vẫn mở.

### Toàn UI-20a/b/c/d/e/f, ngày 28/10/2026

Đã nâng hộp thư, kênh/chất lượng CSKH, cấu hình/tìm hàng/kiến thức/chất lượng trợ lý, trợ lý nổi và media trên điện thoại. Giữ menu cấu hình, quyền/tenant, trường và handler gốc; desktop/in giữ trình bày cũ. 299/299 .NET duy nhất, 3 Node, 41 trạng thái/287 phone/82 desktop/41 in. Kiểm thử dùng toàn bộ nguồn Web/Application/Domain/Infrastructure hiện tại và schema SQL GUID mới tạo bằng EnsureCreated; không dùng paired snapshot, không lọc hoặc thay thế nguồn production. Mười bản test HTTP giữ nguyên toàn bộ assertions và các byte khác, chỉ đổi binding FullApplicationFixture sang Ui20CurrentModelApplicationFixture để chạy trên schema hiện tại. Fixture migration nghiêm ngặt gốc giữ nguyên; checkpoint đó vẫn bị EF PendingModelChanges trước các assertion UI/nghiệp vụ. Kết quả này không nghiệm thu migration hoặc cơ sở dữ liệu cửa hàng đang dùng, không bỏ qua cảnh báo EF. SQLite CSKH, file thử và các provider giả lập chỉ nằm trong fixture riêng; không gửi tin đến khách, AI hay kênh thật. [Báo cáo UI-20](MOBILE-UI-20-COMPLETE-20261028.md). Tiếp UI-21a.

### Toàn UI-21a/b/c/d/e, ngày 28/10/2026

Đã nâng giao diện điện thoại cho nhân viên, vai trò, phân quyền, máy POS, kiosk và audit. 149/149 ca .NET duy nhất; 30 trạng thái/210 phone/60 desktop/30 in; hai probe trình duyệt gốc qua. Dùng FullApplicationFixture gốc với MigrateAsync trên database SQL GUID riêng; không dùng EnsureCreated, paired snapshot hoặc lọc nguồn production. Metadata EF P3 được sửa, không tạo migration trùng và không bỏ qua cảnh báo. Giữ challenge, query scope/limit và cấu hình menu/quyền; nhãn CSKH còn thiếu được bổ sung tiếng Việt ở lớp hiển thị chung. Bố cục desktop/print đối chiếu CSS/hình học. Chrome đang mở giữ nguyên, chỉ dùng profile headless mới. [Báo cáo UI-21](MOBILE-UI-21-COMPLETE-20261028.md). Tiếp UI-22a.

### Toàn UI-22a/b/c/d/e/f, ngày 28/10/2026

Đã nâng giao diện điện thoại cho không gian in và màn chuyên dụng. 170/170 ca .NET duy nhất, 99 ca Node; 42 trạng thái/294 phone/84 desktop/42 print; 24 PDF đối chiếu. Điều khiển preview 100%/Vừa khung, vùng cuộn giấy và nút đóng hộp số lượng kiosk chỉ hiện trên phone; phục hồi DOM khi desktop/print. Canonical print là ReceiptTemplates/Print; hai view cũ giữ nguyên và không gắn route/menu mới. StoreMonitor dùng tài khoản được cấp quyền riêng, không tự thêm quyền cho ADMIN. Không đổi renderer, QZ/helper, LabelPrintClient/Server hoặc bundle 3D. Còn máy in, helper Python đã cung cấp và thiết bị thật theo báo cáo. [Báo cáo UI-22](MOBILE-UI-22-COMPLETE-20261028.md). Tiếp UI-23a.

### Bổ sung thanh điều hướng chính, 28/10/2026

Người dùng chọn bốn phần ưu tiên như mẫu. Các mục được chiếu từ cây AdminMenuItems đã lọc quyền/ẩn hiện, giữ href/query/ID/icon gốc; chỉ đổi nhãn ngắn của thanh điện thoại. SSR tránh còn mỗi Menu khi chưa có pin hoặc API pin không tải được. Cấu hình ghim riêng vẫn được giữ, không seed/menu giả/default data. Preview mobile dùng cùng bốn phần khi chưa chọn pin, kể cả kiểm tra theo vai trò. Desktop/sidebars/dock giao dịch giữ nguyên. 42 ca .NET, 3 ca Node và native Chrome/SQL đã qua; còn thiết bị thật. [Báo cáo](MOBILE-PRIMARY-NAV-20261028.md).

### UI-23 Trang công khai, 28/10/2026

Phạm vi public tách khỏi Admin. Sáu view nâng phone, năm file hợp đồng/error hiện có giữ nguyên; một partial và hai asset mới. Giữ nguyên form hóa đơn, token/CSRF, kiểm tra cửa hàng, toàn bộ command chat, controller/service và native HTTP, kể cả Privacy POST. 79 ca .NET, 133 kiểm tra phone và 38 desktop đã đạt; không pending migration/model. Chat web chưa có API đính kèm, không thêm upload giả hoặc chuyển quyền tệp nội bộ sang khách. Giữ các cửa sổ Chrome bằng browser headless riêng. Còn thiết bị thật. [Báo cáo UI-23](MOBILE-UI-23-COMPLETE-20261028.md).

### UI-24 Nghiệm thu cục bộ, 29/10/2026

Kiểm kê 283 Razor/440 asset riêng, không mất baseline; compiled Razor và asset WebOnly khớp source. 628 .NET/721 Node, ma trận 253 trạng thái, PDF và chuyển/rollback HTTPS cùng SQL/DP keys đã qua. Desktop chỉ sửa menu dấu ba chấm POS theo yêu cầu mới; sidebar/menu cửa hàng vẫn từ cấu hình, không tạo menu/pin. Khôi phục route BarcodeManager thiếu Index bằng API/quyền hiện có. UI-17–20 đã kiểm lại với source/migration đầy đủ; giữ nguyên 50 verification cũ và các nghiệm thu vật lý còn chờ. Còn thiết bị, tải lớn/host và rollout thật. [Báo cáo UI-24](MOBILE-UI-24-ACCEPTANCE-20261029.md).

## Đầu ra và trạng thái

- UI-01c đã chốt cơ chế thích ứng; UI-01e đã có hợp đồng và snapshot thí điểm.
- UI-01d còn kiểm tra phần cứng/trình duyệt thật, zoom/forced colors và build toàn ứng dụng trên baseline .NET yêu cầu. Các phần kiểm tra bằng viewport không thay các mục đó.
- UI-02a–b bắt đầu áp dụng chuẩn lên partial menu thật. Chỉ các phần có bằng chứng được đánh dấu hoàn thành trong [tiến độ thực hiện](mobile-ui-20261025/implementation-progress.json); [baseline](mobile-ui-20261025/view-inventory.json) giữ nguyên hash trước triển khai.
