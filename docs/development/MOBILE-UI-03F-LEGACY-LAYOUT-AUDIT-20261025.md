# UI-03f · Rà layout/navbar cũ và Shared/Error

Ngày 25/10/2026. **Đã rà source và bổ sung mã; chờ người dùng chạy Visual Studio để nghiệm thu.** Không chạy app, Chrome, build, bộ test, bộ kiểm tra CSP hoặc migration. Các bước trước vẫn giữ trạng thái chờ nghiệm thu.

## Kết quả và quyết định giữ/thay

Đã quét tham chiếu literal trong source Web/Infrastructure, kiểm tra hai `_ViewStart`, các phép gán Layout, section và đường resolve lỗi. [layout-audit.json](mobile-ui-20261025/evidence/ui-03f/layout-audit.json) lưu các vị trí tham chiếu, biểu thức Layout, ma trận section và kết luận. Đây là bằng chứng source; chưa chứng minh một file có thể xóa chỉ bằng việc không tìm thấy caller.

| Thành phần | Bằng chứng và kết luận |
|---|---|
| `_ContentNavbarLayout` → `_CommonMasterLayout` | Layout mặc định của Admin. Giữ; dùng `_AdminToolbar`, header điện thoại và `_VerticalMenu` lấy cấu hình cửa hàng. |
| `_POSPrimeLayout` → `_CommonMasterLayout` | POS/Index chọn theo biến thể POS. Giữ quyền sở hữu header/dock của POS; không chuyển về layout admin thường. |
| `_AdminLayout` | Không tìm thấy caller literal. Có menu Home/Product viết cố định và section `styles/scripts` kiểu cũ; **giữ file tạm thời, không kích hoạt lại**. Nếu tìm thấy caller cần phục hồi, chuyển caller sang `_ContentNavbarLayout` cùng menu cấu hình, kiểm tra desktop/quyền rồi mới xét xóa file cũ. |
| `_BlankLayout` → `_CommonMasterLayout` | Không tìm thấy caller literal. Giữ layout trống; bổ sung chuyển tiếp tùy chọn `Scripts` còn thiếu, cùng bốn section sẵn có. Không thêm menu hoặc app shell. |
| `Sections/Navbar/_Navbar` | Không tìm thấy caller literal; chỉ gọi `_NavbarPartial` hai nhánh. Giữ tạm, không thay vào navbar đang hoạt động. |
| `Sections/Navbar/_NavbarPartial` | Chỉ có hai tham chiếu nội bộ từ `_Navbar`; phần tài khoản mẫu nằm trong Razor comment. Giữ cùng cụm legacy, không kích hoạt brand/toggle mẫu. |
| `Sections/Navbar/_AdminToolbar` | Đang được `_ContentNavbarLayout` dùng. Giữ slot tiện ích/tài khoản thật; không thay bằng navbar mẫu. |
| Admin `Shared/_Layout` | Được Admin `Error/Error` chọn bằng đường dẫn đầy đủ. Giữ layout lỗi cũ và cơ chế CSS điện thoại opt-in của UI-03c. |
| Public `Shared/_Layout` | Mặc định qua public `_ViewStart`, dùng cho public Home và Error. Giữ; không đổi các trang công khai sang khung Admin. |
| Hai `Shared/Error` | Public là fallback của `HomeController.Error`; Admin chưa thấy caller rõ ràng. Giữ cả hai và hợp đồng `ErrorViewModel`; dùng chung partial điện thoại, giữ nguyên nội dung desktop. Không tạo route để kích hoạt fallback Admin. |
| `_CommercePreviewLayout` / `_CommerceMobileLayout` | Chỉ phục vụ demo UiFoundation. Giữ tách biệt khỏi màn nghiệp vụ và nguồn menu thật. |
| Trang in/kiosk/màn khách hàng/nhúng với Layout null | Giữ chủ sở hữu riêng, không gắn khung ứng dụng điện thoại vào toàn bộ view một cách tự động. |

Không xóa, đổi tên hoặc nối lại layout/navbar legacy. Việc dọn mã sau này cần xác minh caller động/ngoài source và kiểm tra runtime trước. Source Web/Infrastructure không thấy cấu hình tùy biến `IViewLocationExpander`, `ViewLocationFormats`, `AreaViewLocationFormats` hoặc `RazorViewEngineOptions`; kết luận này giới hạn trong phạm vi đã quét.

## Shared/Error trên điện thoại

Hai view giữ nguyên model, `ShowRequestId`, RequestId được Razor escape, Title và Layout kế thừa. Nội dung desktop cũ được bọc bằng một div không có CSS desktop; partial mới ẩn từ 768 px trở lên. Không sửa thông báo hoặc hướng dẫn Development đang có trên desktop.

Dưới 768 px, cùng `_MobileSharedError` hiển thị thẻ trắng, navy/lam v2, thông báo tiếng Việt, mã hỗ trợ xuống dòng và hai nút cao tối thiểu 48 px. Khi không có RequestId thì không dựng khối mã/hướng dẫn gửi mã. Không tự gán mã lỗi 500 vì ErrorViewModel không cung cấp HTTP status. Phần hướng dẫn môi trường Development không hiện trong giao diện điện thoại.

Public Shared/Error bật `MobileStatePage` để layout cũ nạp stylesheet UI-03c và safe area. Admin Shared/Error kế thừa khung hiện có; thẻ không chiếm thêm 100dvh phía dưới header/dock. CSS mới chỉ nhắm Shared/Error; các rule tài khoản, toast, SweetAlert, AccessDenied và Error/Error trước đó giữ nguyên. Reduced motion và forced colors dùng các rule thẻ đã có.

Nút Quay lại dùng `data-csp-action="back"`; `ContentSecurityHeadTagHelper` vốn nạp `csp-actions.js` kèm nonce ở head qua `_ViewImports` của cả hai khu vực. Không thêm script trùng hoặc inline handler. Nút Về trang chủ dùng `Url`/Anchor TagHelper theo area của request, không dùng ReturnUrl ngoài hoặc tạo mục menu. Không reload, submit lại hoặc tự thử lại yêu cầu giao dịch.

## Luồng lỗi và section

`/Home/Error` trả ErrorViewModel bằng `View(...)` không chỉ tên view; không có `Views/Home/Error.cshtml`, nên theo cấu trúc source hiện tại sẽ resolve public `Views/Shared/Error.cshtml`. Action và `ResponseCache(NoStore)` không sửa.

`/error/{statusCode}` tiếp tục chọn `Views/Error/Error.cshtml` hoặc bản Admin theo `isAdmin=1`, dùng Message/TraceId như UI-03c. Không đổi sang Shared/Error vì hai loại view có hợp đồng dữ liệu khác nhau. `GlobalExceptionMiddleware` vẫn map exception ra JSON; không đổi thành redirect hoặc trang HTML. Không thay response status, quyền, tenant, terminal hoặc route.

`_BlankLayout`, `_ContentNavbarLayout` và `_POSPrimeLayout` hiện có đủ năm section tùy chọn `VendorStyles`, `PageStyles`, `VendorScripts`, `PageScripts`, `Scripts`, mỗi section chuyển tiếp một lần về CommonMaster. `_ContentNavbarLayout` giữ `header` tại vị trí riêng. `_BlankLayout` chỉ được thêm một section tùy chọn; chưa có caller tĩnh nên không thay bố cục trang đang hoạt động.

## Đối chiếu đã thực hiện

[static-review.json](mobile-ui-20261025/evidence/ui-03f/static-review.json) so với snapshot `.artifacts/ui-03f/before/` ghi nhận:

- **35 file bảo vệ giữ hash**, gồm các layout/navbar/menu đang dùng và legacy, controller lỗi/Home/menu/ghim, provider quyền/menu, tenant/terminal, CSP/TagHelper/action script, Program và Chrome launcher/profile.
- **Bốn đối chiếu file cũ**: hai Shared/Error chỉ thêm wrapper/partial và cờ CSS public; Blank chỉ thêm Scripts; stylesheet chỉ thêm các rule Shared/Error, ngoài breakpoint chỉ ẩn partial mới. Đối chiếu chuẩn hóa BOM/CRLF/newline cuối file.
- Quét **279 Razor hiện tại, 9 layout**, lưu tham chiếu và section trong audit. Một partial mới nằm ngoài baseline gốc.
- [Baseline delta](mobile-ui-20261025/evidence/ui-03f/baseline-delta.json): 560 file gốc, **526 không đổi, 34 đổi, 0 mất**, cộng dồn các bước; baseline không sửa.

Đây là đối chiếu source, không phải build Razor, kiểm tra pixel hoặc bộ test CSP. Không đổi JS ứng dụng, backend, dữ liệu, migration hoặc thiết lập Chrome trong bước này. Không đọc/đóng tab Chrome đang có.

## Bạn chạy Visual Studio để tự test

1. F5 bằng profile `http` hoặc `https`. Mở Home admin, một màn có menu, POS và nhập hàng ở 390 px; kiểm tra chỉ một header/dock/tài khoản, không có navbar hoặc menu mẫu cũ. Dùng tài khoản kiểm tra để đối chiếu quyền/cửa hàng.
2. Trên đúng host cửa hàng, mở `/Home/Error`: thử 320/390/430 px, mã hỗ trợ, Quay lại sau khi đã mở trang trước và Về trang chủ khi mở trực tiếp. Ở 768/1024/1366 px phải giữ nội dung desktop cũ.
3. Thử `/error/404` và `/error/403?isAdmin=1`: vẫn là Error/Error với TraceId, thông báo và nút cũ. Kiểm tra Console/Network không có lỗi section/CSP. URL chứa mã lỗi không phải bằng chứng response HTTP có mã đó.
4. Xoay máy, phóng chữ, reduced motion; mở lại một trang in/kiosk/màn khách hàng đang dùng để xác nhận layout riêng. F5/Stop phải tiếp tục giữ Chrome cũ như yêu cầu UI-03e.

[16 ca nghiệm thu](mobile-ui-20261025/evidence/ui-03f/acceptance-matrix.json) đều đang chờ. Hai layout/fallback không có caller tĩnh (`_BlankLayout`, Admin Shared/Error) chỉ kiểm tra render qua harness hoặc caller đã xác minh khi cần; không có URL mới để người dùng truy cập và không tự tạo route/menu chỉ để thử.

Không cần migration. Bước triển khai kế tiếp: **UI-04a — danh sách đơn POS (`POSOrderPage/Index`)**, tiếp tục giữ desktop và quyền cửa hàng. Chuyển bước không có nghĩa các ca runtime trước đã được nghiệm thu.
